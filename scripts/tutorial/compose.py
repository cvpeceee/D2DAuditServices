"""Turns recorded scene frames + narration into the finished tutorial (video, audio mix, captions, manifest, clips)."""

from __future__ import annotations

import json
import re
import subprocess
import wave
from pathlib import Path

import imageio_ffmpeg
import numpy as np

from tts import Narration, caption_cues

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
SR = 48_000


def log(msg: str) -> None:
    print(f"[compose] {msg}", flush=True)


def run(args: list[str], capture: bool = False) -> subprocess.CompletedProcess:
    proc = subprocess.run([FFMPEG, "-hide_banner", "-y", *args], capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg failed: {' '.join(args[:12])}...\n{proc.stderr[-2500:]}")
    return proc


# ---------------- video ----------------
def scene_clip(frames_dir: Path, frames: list[tuple[str, float]], duration: float, fps: int, out: Path) -> None:
    if not frames:
        raise RuntimeError(f"no frames captured in {frames_dir}")
    lines = ["ffconcat version 1.0"]
    times = [0.0] + [t for _, t in frames[1:]]
    for i, (name, _) in enumerate(frames):
        nxt = times[i + 1] if i + 1 < len(frames) else duration
        lines.append(f"file '{name}'")
        lines.append(f"duration {max(1 / fps / 4, nxt - times[i]):.4f}")
    lines.append(f"file '{frames[-1][0]}'")
    listing = frames_dir / "frames.ffconcat"
    listing.write_text("\n".join(lines) + "\n", encoding="utf-8")
    run([
        "-f", "concat", "-safe", "0", "-i", str(listing),
        "-vf", f"fps={fps},scale=1920:1080:flags=lanczos,format=yuv420p",
        "-t", f"{duration:.3f}", "-c:v", "libx264", "-preset", "veryfast", "-crf", "14", "-an", str(out),
    ])


def timeline(scenes: list[dict], durations: list[float], fade: float) -> list[float]:
    starts, running = [0.0], durations[0]
    for d in durations[1:]:
        starts.append(running - fade)
        running += d - fade
    return starts


def assemble_video(clips: list[Path], durations: list[float], transitions: list[str], fade: float, out: Path) -> float:
    inputs: list[str] = []
    for clip in clips:
        inputs += ["-i", str(clip)]
    chain, label, running = [], "[0:v]", durations[0]
    for i in range(1, len(clips)):
        offset = running - fade
        nxt = f"[x{i}]"
        chain.append(f"{label}[{i}:v]xfade=transition={transitions[i]}:duration={fade}:offset={offset:.3f}{nxt}")
        label, running = nxt, running + durations[i] - fade
    graph = ";".join(chain) if chain else "[0:v]null[x0]"
    final_label = label if chain else "[x0]"
    run([*inputs, "-filter_complex", graph, "-map", final_label, "-c:v", "libx264", "-preset", "veryfast", "-crf", "12", "-an", str(out)])
    return running


# ---------------- audio ----------------
def decode(path: Path) -> np.ndarray:
    proc = subprocess.run([FFMPEG, "-v", "error", "-i", str(path), "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"], capture_output=True)
    return np.frombuffer(proc.stdout, dtype=np.float32).copy()


def _env(t: np.ndarray, a: float, r: float, length: float) -> np.ndarray:
    return np.clip(np.minimum(t / a, (length - t) / r), 0, 1)


def ambient_music(seconds: float, seed: int = 7) -> np.ndarray:
    """Procedural ambient pad + soft plucks (royalty-free by construction). Returns stereo float32."""
    rng = np.random.default_rng(seed)
    n = int(seconds * SR)
    out = np.zeros((n, 2), dtype=np.float64)
    midi = lambda m: 440.0 * 2 ** ((m - 69) / 12)  # noqa: E731
    progression = [[48, 55, 59, 64, 67], [45, 52, 57, 60, 64], [41, 48, 53, 57, 64], [43, 50, 55, 59, 62]]
    bar = 4.8
    t_bar = np.arange(int((bar + 2.4) * SR)) / SR
    pos = 0.0
    k = 0
    while pos < seconds:
        chord = progression[k % len(progression)]
        seg = np.zeros((len(t_bar), 2))
        env = _env(t_bar, 1.6, 2.2, bar + 2.4)
        for j, note in enumerate(chord):
            f = midi(note)
            for det, pan in ((-0.12, 0.25), (0.0, 0.5), (0.13, 0.75)):
                ff = f * 2 ** (det / 12)
                ph = rng.uniform(0, 2 * np.pi)
                tone = np.sin(2 * np.pi * ff * t_bar + ph) + 0.25 * np.sin(4 * np.pi * ff * t_bar + ph) + 0.08 * np.sin(6 * np.pi * ff * t_bar)
                trem = 1 + 0.15 * np.sin(2 * np.pi * (0.13 + 0.05 * j) * t_bar + ph)
                seg[:, 0] += tone * env * trem * (1 - pan) * (0.6 if j == 0 else 0.35)
                seg[:, 1] += tone * env * trem * pan * (0.6 if j == 0 else 0.35)
        # gentle pluck arpeggio
        arp = chord[1:] + chord[2:4][::-1]
        step = bar / 8
        for s in range(8):
            note = arp[s % len(arp)] + 12
            start = int(s * step * SR)
            tt = np.arange(int(1.6 * SR)) / SR
            pl = np.sin(2 * np.pi * midi(note) * tt) * np.exp(-tt * 3.2) * 0.22
            pan = 0.3 + 0.4 * (s % 2)
            end = min(len(seg), start + len(pl))
            seg[start:end, 0] += pl[: end - start] * (1 - pan)
            seg[start:end, 1] += pl[: end - start] * pan
        a = int(pos * SR)
        b = min(n, a + len(seg))
        if a < n:
            out[a:b] += seg[: b - a]
        pos += bar
        k += 1
    out = _lowpass(out, 2400)
    fade = int(min(3.0, seconds / 4) * SR)
    ramp = np.linspace(0, 1, fade)
    out[:fade] *= ramp[:, None]
    out[-fade:] *= ramp[::-1][:, None]
    return (out / (np.abs(out).max() + 1e-9)).astype(np.float32)


def _lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    """Zero-phase gentle low-pass (2nd-order magnitude response) applied in the frequency domain."""
    spec = np.fft.rfft(x, axis=0)
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    spec *= (1 / (1 + (freqs / cutoff) ** 2))[:, None]
    return np.fft.irfft(spec, n=len(x), axis=0)


def click_sfx() -> np.ndarray:
    t = np.arange(int(0.06 * SR)) / SR
    return (np.sin(2 * np.pi * 2100 * t) * np.exp(-t * 90) * 0.18 + np.sin(2 * np.pi * 900 * t) * np.exp(-t * 60) * 0.12).astype(np.float32)


def whoosh_sfx(seconds: float = 0.7, seed: int = 3) -> np.ndarray:
    rng = np.random.default_rng(seed)
    n = int(seconds * SR)
    noise = rng.normal(0, 1, n)
    env = np.sin(np.linspace(0, np.pi, n)) ** 2
    x = _lowpass(noise[:, None], 1800)[:, 0]
    return (x * env / (np.abs(x).max() + 1e-9) * 0.10).astype(np.float32)


def _moving_avg(x: np.ndarray, win: int) -> np.ndarray:
    c = np.cumsum(np.concatenate([np.zeros(win // 2 + 1), x, np.zeros(win)]))
    return (c[win:win + len(x)] - c[: len(x)]) / win


def _place(track: np.ndarray, clip: np.ndarray, at: float) -> None:
    a = int(at * SR)
    if a >= len(track):
        return
    b = min(len(track), a + len(clip))
    if track.ndim == 2 and clip.ndim == 1:
        track[a:b] += clip[: b - a, None]
    else:
        track[a:b] += clip[: b - a]


def mix_audio(total: float, voice_items: list[tuple[float, Path]], clicks: list[float], whooshes: list[float], music: dict, out_wav: Path) -> None:
    n = int((total + 0.5) * SR)
    voice = np.zeros(n, dtype=np.float32)
    for at, path in voice_items:
        _place(voice, decode(path), at)
    sfx = np.zeros(n, dtype=np.float32)
    c = click_sfx()
    for at in clicks:
        _place(sfx, c, at)
    w = whoosh_sfx()
    for at in whooshes:
        _place(sfx, w, max(0.0, at - 0.25))
    mix = np.zeros((n, 2), dtype=np.float32)
    mix += voice[:, None]
    mix += sfx[:, None] * 0.9
    if music.get("enabled", True):
        bed = ambient_music(n / SR)
        # side-chain duck: music dips while the narrator is speaking
        frame = int(0.02 * SR)
        rms = np.sqrt(_moving_avg(voice.astype(np.float64) ** 2, frame))
        active = (rms > 0.01).astype(np.float64)
        active = _moving_avg(active, int(0.35 * SR))
        active = np.clip(active * 2, 0, 1)
        gain_db = music.get("levelDb", -27) + music.get("duckDb", -9) * active
        mix += bed[:n] * (10 ** (gain_db / 20))[:, None].astype(np.float32) * 3.0
    peak = np.abs(mix).max()
    if peak > 0.99:
        mix *= 0.99 / peak
    pcm = (mix * 32767).astype(np.int16)
    with wave.open(str(out_wav), "wb") as fh:
        fh.setnchannels(2)
        fh.setsampwidth(2)
        fh.setframerate(SR)
        fh.writeframes(pcm.tobytes())


def loudnorm(in_wav: Path, out_wav: Path, target: float = -16.0) -> dict:
    probe = run(["-i", str(in_wav), "-af", f"loudnorm=I={target}:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"])
    stats = json.loads(re.findall(r"\{[^{}]+\}", probe.stderr)[-1])
    af = (
        f"loudnorm=I={target}:TP=-1.5:LRA=11:measured_I={stats['input_i']}:measured_TP={stats['input_tp']}:"
        f"measured_LRA={stats['input_lra']}:measured_thresh={stats['input_thresh']}:offset={stats['target_offset']}:linear=true"
    )
    run(["-i", str(in_wav), "-af", af, "-ar", str(SR), str(out_wav)])
    return stats


def mux(video: Path, audio: Path, out: Path, duration: float) -> None:
    run([
        "-i", str(video), "-i", str(audio), "-map", "0:v", "-map", "1:a",
        "-c:v", "libx264", "-preset", "slow", "-crf", "23", "-maxrate", "1800k", "-bufsize", "3600k",
        "-pix_fmt", "yuv420p", "-g", "60", "-tune", "stillimage",
        "-c:a", "aac", "-b:a", "160k", "-t", f"{duration:.3f}", "-movflags", "+faststart", str(out),
    ])


def cut_clip(src: Path, start: float, end: float, out: Path) -> None:
    fade = 0.35
    length = end - start
    run([
        "-ss", f"{start:.3f}", "-i", str(src), "-t", f"{length:.3f}",
        "-vf", f"fade=t=in:st=0:d={fade},fade=t=out:st={max(0, length - fade):.3f}:d={fade}",
        "-af", f"afade=t=in:st=0:d={fade},afade=t=out:st={max(0, length - fade):.3f}:d={fade}",
        "-c:v", "libx264", "-preset", "slow", "-crf", "24", "-maxrate", "1800k", "-bufsize", "3600k", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(out),
    ])


def still(src: Path, at: float, out: Path, width: int = 1280) -> None:
    run(["-ss", f"{at:.3f}", "-i", str(src), "-frames:v", "1", "-vf", f"scale={width}:-2", "-q:v", "3", str(out)])


# ---------------- text outputs ----------------
def ts(seconds: float) -> str:
    seconds = max(0.0, seconds)
    h = int(seconds // 3600)
    m = int(seconds % 3600 // 60)
    s = seconds % 60
    return f"{h:02d}:{m:02d}:{s:06.3f}"


def write_vtt(cues: list[tuple[float, float, str]], out: Path, header: str = "WEBVTT") -> None:
    lines = [header, ""]
    for i, (a, b, text) in enumerate(cues, 1):
        lines += [str(i), f"{ts(a)} --> {ts(b)}", text, ""]
    out.write_text("\n".join(lines), encoding="utf-8")


def captions_for(items: list[tuple[float, Narration]]) -> list[tuple[float, float, str]]:
    cues: list[tuple[float, float, str]] = []
    for at, narration in items:
        cues += caption_cues(narration.text, narration.words, at)
    return cues
