"""Neural text-to-speech for the tutorial using Microsoft Edge voices (edge-tts).

Each narration line is synthesised once and cached by a hash of (voice, rate, pitch, text).
Word-boundary events are kept so captions and on-screen cues line up with the voice.
"""

from __future__ import annotations

import asyncio
import hashlib
import json
import re
import subprocess
import time
from dataclasses import dataclass, field
from pathlib import Path

import edge_tts
import imageio_ffmpeg

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()


@dataclass
class Narration:
    text: str
    audio: Path
    duration: float
    words: list[dict] = field(default_factory=list)  # {"text", "start", "end"} seconds

    def cue_time(self, cue: str, after: float = 0.0) -> float | None:
        """Start time of the first word matching ``cue`` (prefix match, punctuation ignored) at/after ``after``."""
        target = _norm(cue.split()[0]) if cue.strip() else ""
        if not target:
            return None
        for word in self.words:
            if word["start"] + 1e-6 < after:
                continue
            if _norm(word["text"]).startswith(target):
                return word["start"]
        return None


def _norm(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def _probe_duration(path: Path) -> float:
    proc = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path)], capture_output=True, text=True)
    match = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", proc.stderr)
    if not match:
        raise RuntimeError(f"Could not read duration of {path}")
    h, m, s = match.groups()
    return int(h) * 3600 + int(m) * 60 + float(s)


async def _synth(text: str, voice: str, rate: str, pitch: str, out: Path) -> list[dict]:
    communicate = edge_tts.Communicate(text, voice, rate=rate, pitch=pitch, boundary="WordBoundary")
    words: list[dict] = []
    with out.open("wb") as fh:
        async for chunk in communicate.stream():
            if chunk["type"] == "audio":
                fh.write(chunk["data"])
            elif chunk["type"] == "WordBoundary":
                start = chunk["offset"] / 10_000_000
                words.append({"text": chunk["text"], "start": round(start, 3), "end": round(start + chunk["duration"] / 10_000_000, 3)})
    return words


def synthesize(text: str, voice: str, rate: str, pitch: str, cache_dir: Path) -> Narration:
    cache_dir.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha256(json.dumps([voice, rate, pitch, text]).encode()).hexdigest()[:16]
    audio = cache_dir / f"{key}.mp3"
    meta = cache_dir / f"{key}.json"
    if audio.exists() and meta.exists():
        data = json.loads(meta.read_text(encoding="utf-8"))
        return Narration(text, audio, data["duration"], data["words"])
    last_error: Exception | None = None
    for attempt in range(6):
        try:
            words = asyncio.run(_synth(text, voice, rate, pitch, audio))
            break
        except Exception as exc:  # network hiccups / throttling from the TTS service are retried
            last_error = exc
            time.sleep(2 + attempt * 3)
    else:
        raise RuntimeError(f"TTS failed for: {text[:60]}") from last_error
    duration = _probe_duration(audio)
    meta.write_text(json.dumps({"duration": duration, "words": words}, indent=1), encoding="utf-8")
    return Narration(text, audio, duration, words)


def caption_cues(text: str, words: list[dict], offset: float, max_chars: int = 44) -> list[tuple[float, float, str]]:
    """Group boundary words into short caption phrases, using the original text (with punctuation)."""
    spans: list[tuple[int, int, float, float]] = []
    cursor = 0
    lowered = text.lower()
    for word in words:
        token = word["text"]
        idx = lowered.find(token.lower(), cursor)
        if idx < 0:
            continue
        end = idx + len(token)
        while end < len(text) and not text[end].isspace():
            end += 1
        spans.append((idx, end, word["start"], word["end"]))
        cursor = end
    cues: list[tuple[float, float, str]] = []
    group: list[tuple[int, int, float, float]] = []

    def flush() -> None:
        if not group:
            return
        phrase = text[group[0][0] : group[-1][1]].strip()
        cues.append((offset + group[0][2], offset + group[-1][3] + 0.25, phrase))
        group.clear()

    for span in spans:
        if group:
            length = span[1] - group[0][0]
            gap = span[2] - group[-1][3]
            if length > max_chars or gap > 0.6:
                flush()
        group.append(span)
        if text[span[1] - 1] in ".?!…" and (span[1] - group[0][0]) > 12:
            flush()
    flush()
    for i in range(len(cues) - 1):
        start, end, phrase = cues[i]
        if end > cues[i + 1][0]:
            cues[i] = (start, cues[i + 1][0] - 0.02, phrase)
    return cues
