"""Build the D2D Audit product-tour video from scripts/tutorial/storyboard.json.

Usage (from the repo root):
    python scripts/tutorial/build_tutorial.py                # full build: vite demo build, TTS, record, compose
    python scripts/tutorial/build_tutorial.py --skip-build   # reuse src/web/dist
    python scripts/tutorial/build_tutorial.py --only evidence,exposure   # re-record some scenes, reuse other takes

Outputs:
    src/web/public/tutorial/d2d-super-app-tutorial.{mp4,vtt}, tutorial-chapters.json  (used by the in-app tutorial page)
    .tutorial-build/dist/  full media set for the public tutorials site (clips, stills, poster, chapters VTT)
"""

from __future__ import annotations

import argparse
import asyncio
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import compose  # noqa: E402
from recorder import Recorder, SceneTake  # noqa: E402
from serve import start as serve  # noqa: E402
from tts import synthesize  # noqa: E402

ROOT = HERE.parents[1]
WEB = ROOT / "src" / "web"
BUILD = ROOT / ".tutorial-build"
APP_OUT = WEB / "public" / "tutorial"
VIDEO_NAME = "d2d-super-app-tutorial.mp4"
VTT_NAME = "d2d-super-app-tutorial.vtt"
MANIFEST_NAME = "tutorial-chapters.json"


def log(msg: str) -> None:
    print(f"[build] {msg}", flush=True)


def vite_build() -> None:
    log("building demo bundle (vite --mode demo)")
    npx = "npx.cmd" if sys.platform == "win32" else "npx"
    subprocess.run([npx, "vite", "build", "--mode", "demo"], cwd=WEB, check=True, stdout=subprocess.DEVNULL)


def add_chapter_lower_thirds(scenes: list[dict], chapters: list[dict]) -> None:
    numbers = {c["id"]: i + 1 for i, c in enumerate(chapters)}
    titles = {c["id"]: c["title"] for c in chapters}
    seen: set[str] = set()
    for scene in scenes:
        ch = scene["chapter"]
        if ch in seen:
            continue
        seen.add(ch)
        if scene.get("type") != "stage":
            scene.setdefault("actions", []).insert(
                0, {"do": "lowerThird", "kicker": f"Chapter {numbers[ch]}", "title": titles[ch], "ms": 3600}
            )
        scene["_chapterStart"] = True


async def snapshot_poster(out: Path) -> None:
    from playwright.async_api import async_playwright

    async with async_playwright() as pw:
        browser = await pw.chromium.launch()
        page = await browser.new_page(viewport={"width": 1920, "height": 1080})
        await page.goto((HERE / "stage.html").as_uri() + "?poster")
        await page.wait_for_timeout(600)
        await page.screenshot(path=str(out), type="jpeg", quality=88)
        await browser.close()


def load_take(scene_id: str) -> SceneTake | None:
    path = BUILD / "frames" / scene_id / "take.json"
    if not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    take = SceneTake(scene_id, path.parent, [tuple(f) for f in data["frames"]], data["duration"], data["clicks"])
    take.issues = data.get("issues", [])
    return take


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--storyboard", default=str(HERE / "storyboard.json"))
    parser.add_argument("--skip-build", action="store_true", help="reuse the existing src/web/dist demo build")
    parser.add_argument("--only", default="", help="comma-separated scene ids to re-record (others reuse cached takes)")
    parser.add_argument("--no-clips", action="store_true", help="skip per-chapter clips and stills")
    parser.add_argument("--no-app-copy", action="store_true", help="do not overwrite src/web/public/tutorial")
    args = parser.parse_args()

    started = time.time()
    sb = json.loads(Path(args.storyboard).read_text(encoding="utf-8"))
    video_cfg, voice = sb["video"], sb["voice"]
    BUILD.mkdir(exist_ok=True)
    dist_out = BUILD / "dist"
    if dist_out.exists():
        shutil.rmtree(dist_out)
    (dist_out / "clips").mkdir(parents=True)
    (dist_out / "shots").mkdir()

    if not args.skip_build or not (WEB / "dist" / "index.html").exists():
        vite_build()
    server, url = serve(WEB / "dist", sb["demo"]["base"])
    log(f"serving demo at {url}")

    recorder = Recorder(sb, url, BUILD)
    scenes, skipped = asyncio.run(recorder.probe(sb["scenes"]))
    for s in skipped:
        log(f"skipped scene '{s['id']}' ({s['feature']}): {s['reason']}")
    add_chapter_lower_thirds(scenes, sb["chapters"])

    log(f"synthesising narration with {voice['name']} ({len(scenes)} scenes)")
    narrations = {s["id"]: synthesize(s["narration"], voice["name"], voice["rate"], voice["pitch"], BUILD / "tts") for s in scenes}

    only = {x.strip() for x in args.only.split(",") if x.strip()}
    to_record = [s for s in scenes if not only or s["id"] in only or load_take(s["id"]) is None]
    takes = asyncio.run(recorder.record(to_record, narrations))
    for s in scenes:
        if s["id"] not in takes:
            cached = load_take(s["id"])
            if cached is None:
                raise SystemExit(f"no take for scene {s['id']}")
            takes[s["id"]] = cached
    server.shutdown()

    fps, fade = video_cfg["fps"], video_cfg["fade"]
    lead = video_cfg["lead"]
    clips_dir = BUILD / "scenes"
    clips_dir.mkdir(exist_ok=True)
    durations, scene_files, transitions = [], [], []
    for i, s in enumerate(scenes):
        take = takes[s["id"]]
        out = clips_dir / f"{i:02d}-{s['id']}.mp4"
        compose.scene_clip(take.frames_dir, take.frames, take.duration, fps, out)
        durations.append(round(take.duration, 3))
        scene_files.append(out)
        transitions.append(video_cfg["chapterTransition"] if s.get("_chapterStart") and i > 0 and s.get("type") != "stage" else video_cfg["sceneTransition"])
    starts = compose.timeline(scenes, durations, fade)
    log("assembling video with transitions")
    silent = BUILD / "silent.mp4"
    total = compose.assemble_video(scene_files, durations, transitions, fade, silent)

    log("mixing narration, music and sound design")
    voice_items = [(starts[i] + lead, narrations[s["id"]].audio) for i, s in enumerate(scenes)]
    clicks = [starts[i] + c for i, s in enumerate(scenes) for c in takes[s["id"]].clicks]
    whooshes = [starts[i] for i, s in enumerate(scenes) if i > 0 and s.get("_chapterStart")]
    raw_wav, norm_wav = BUILD / "mix.wav", BUILD / "mix-norm.wav"
    compose.mix_audio(total, voice_items, clicks, whooshes, sb.get("music", {}), raw_wav)
    stats = compose.loudnorm(raw_wav, norm_wav, -16.0)
    log(f"loudness before normalisation: {stats['input_i']} LUFS")

    final = dist_out / VIDEO_NAME
    compose.mux(silent, norm_wav, final, total)
    size_mb = final.stat().st_size / 1_048_576
    log(f"video: {total:.1f}s, {size_mb:.1f} MB")

    cues = compose.captions_for([(starts[i] + lead, narrations[s["id"]]) for i, s in enumerate(scenes)])
    compose.write_vtt(cues, dist_out / VTT_NAME)

    chapters_meta = {c["id"]: c for c in sb["chapters"]}
    chapter_rows: list[dict] = []
    for i, s in enumerate(scenes):
        if s.get("_chapterStart"):
            meta = chapters_meta[s["chapter"]]
            chapter_rows.append({
                "number": len(chapter_rows) + 1,
                "id": meta["id"],
                "title": meta["title"],
                "shortTitle": meta.get("shortTitle", meta["title"]),
                "summary": meta.get("summary", ""),
                "startTime": round(starts[i], 2),
                "route": next((x.get("route") for x in scenes if x["chapter"] == meta["id"] and x.get("route") and x["route"] != "/login"), None),
            })
    for j, row in enumerate(chapter_rows):
        row["endTime"] = round(chapter_rows[j + 1]["startTime"] if j + 1 < len(chapter_rows) else total, 2)
        row["duration"] = round(row["endTime"] - row["startTime"], 2)
        row["clip"] = f"clips/{row['number']:02d}-{row['id']}.mp4"
    compose.write_vtt([(r["startTime"], r["endTime"], r["title"]) for r in chapter_rows], dist_out / "chapters.vtt")

    scene_rows = []
    for i, s in enumerate(scenes):
        end = starts[i] + durations[i]
        row = {
            "id": s["id"],
            "title": s.get("feature", s["id"]),
            "chapter": s["chapter"],
            "route": s.get("route"),
            "optional": bool(s.get("optional")),
            "startTime": round(starts[i], 2),
            "endTime": round(end, 2),
            "narration": s["narration"],
        }
        if not args.no_clips and s.get("type") != "stage":
            shot = f"shots/{i:02d}-{s['id']}.jpg"
            compose.still(final, starts[i] + durations[i] * 0.78, dist_out / shot)
            row["still"] = shot
        if takes[s["id"]].issues:
            row["issues"] = takes[s["id"]].issues
        scene_rows.append(row)

    if not args.no_clips:
        log("cutting per-chapter clips")
        for row in chapter_rows:
            compose.cut_clip(final, row["startTime"], row["endTime"], dist_out / row["clip"])
        asyncio.run(snapshot_poster(dist_out / "poster.jpg"))

    manifest = {
        "version": 2,
        "title": sb["title"],
        "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "voice": voice["name"],
        "resolution": f"{video_cfg['width']}x{video_cfg['height']}",
        "fps": fps,
        "totalDurationSeconds": round(total, 2),
        "videoFile": VIDEO_NAME,
        "captionsFile": VTT_NAME,
        "liveDemo": sb["demo"]["liveUrl"],
        "tutorialsSite": sb["demo"]["tutorialsSite"],
        "chapters": chapter_rows,
        "scenes": scene_rows,
        "skippedScenes": skipped,
    }
    (dist_out / MANIFEST_NAME).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    if not args.no_app_copy:
        APP_OUT.mkdir(parents=True, exist_ok=True)
        app_manifest = {k: v for k, v in manifest.items() if k != "scenes"}
        app_manifest["chapters"] = [{k: v for k, v in c.items() if k != "clip"} for c in chapter_rows]
        shutil.copy2(final, APP_OUT / VIDEO_NAME)
        shutil.copy2(dist_out / VTT_NAME, APP_OUT / VTT_NAME)
        (APP_OUT / MANIFEST_NAME).write_text(json.dumps(app_manifest, indent=2) + "\n", encoding="utf-8")
        log(f"updated {APP_OUT}")

    issues = [(r["id"], r["issues"]) for r in scene_rows if r.get("issues")]
    for sid, items in issues:
        log(f"scene '{sid}' had {len(items)} action issue(s): {items}")
    log(f"done in {time.time() - started:.0f}s -> {final} ({total:.1f}s, {size_mb:.1f} MB); skipped {len(skipped)} optional scene(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
