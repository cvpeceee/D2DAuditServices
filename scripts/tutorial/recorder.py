"""Records each storyboard scene from the real demo build with Playwright + the CDP screencast.

Frames are written as JPEGs with timestamps so compose.py can rebuild an exact-timed clip.
Actions are scheduled against narration word timings ("at" cue words), so the cursor,
highlights and zooms land on the words that describe them.
"""

from __future__ import annotations

import asyncio
import base64
import json
import re
import time
from dataclasses import dataclass, field
from pathlib import Path

from playwright.async_api import Page, async_playwright

from tts import Narration

HERE = Path(__file__).resolve().parent


def log(msg: str) -> None:
    print(f"[record] {msg}", flush=True)


@dataclass
class SceneTake:
    scene_id: str
    frames_dir: Path
    frames: list[tuple[str, float]] = field(default_factory=list)
    duration: float = 0.0
    clicks: list[float] = field(default_factory=list)
    route: str | None = None
    issues: list[str] = field(default_factory=list)


class Screencast:
    def __init__(self, page: Page, out_dir: Path, width: int, height: int):
        self.page, self.out_dir, self.width, self.height = page, out_dir, width, height
        self.frames: list[tuple[str, float]] = []
        self.cdp = None
        self.t0 = 0.0

    async def start(self) -> float:
        self.out_dir.mkdir(parents=True, exist_ok=True)
        for old in self.out_dir.glob("*.jpg"):
            old.unlink()
        self.cdp = await self.page.context.new_cdp_session(self.page)
        self.cdp.on("Page.screencastFrame", self._on_frame)
        self.t0 = time.time()
        await self.cdp.send(
            "Page.startScreencast",
            {"format": "jpeg", "quality": 88, "maxWidth": self.width, "maxHeight": self.height, "everyNthFrame": 1},
        )
        return self.t0

    def _on_frame(self, params: dict) -> None:
        idx = len(self.frames)
        name = f"f{idx:05d}.jpg"
        (self.out_dir / name).write_bytes(base64.b64decode(params["data"]))
        ts = params.get("metadata", {}).get("timestamp") or time.time()
        self.frames.append((name, max(0.0, ts - self.t0)))
        asyncio.ensure_future(self.cdp.send("Page.screencastFrameAck", {"sessionId": params["sessionId"]}))

    async def stop(self) -> list[tuple[str, float]]:
        try:
            await self.cdp.send("Page.stopScreencast")
        finally:
            await asyncio.sleep(0.15)
            await self.cdp.detach()
        return self.frames


class Recorder:
    def __init__(self, storyboard: dict, base_url: str, build_dir: Path):
        self.sb = storyboard
        self.base_url = base_url
        self.build_dir = build_dir
        video = storyboard["video"]
        self.width, self.height = video["width"], video["height"]
        self.lead, self.tail = video["lead"], video["tail"]
        self.demo = storyboard["demo"]

    # ---------- element helpers ----------
    def _sub(self, value):
        if isinstance(value, str) and value.startswith("@"):
            return self.demo.get(value[1:], value)
        return value

    def _locator(self, page: Page, spec: dict):
        name = self._sub(spec.get("name"))
        if "role" in spec:
            loc = page.get_by_role(spec["role"], name=name, exact=spec.get("exact", False))
        elif "text" in spec:
            scope = page.locator(spec["css"]) if "css" in spec else page
            loc = scope.get_by_text(spec["text"], exact=spec.get("exact", False))
        else:
            loc = page.locator(spec["css"], has_text=spec["hasText"]) if "hasText" in spec else page.locator(spec["css"])
        nth = spec.get("nth", 0)
        return loc.last if nth == -1 else loc.nth(nth)

    async def resolve(self, page: Page, spec: dict, timeout: float = 4000):
        loc = self._locator(page, spec)
        await loc.wait_for(state="visible", timeout=timeout)
        handle = await loc.element_handle()
        up = spec.get("up", 0)
        if up:
            handle = await handle.evaluate_handle(
                "(e, n) => { for (let i = 0; i < n && e.parentElement; i++) e = e.parentElement; return e; }", up
            )
        return loc, handle

    async def _center(self, handle) -> tuple[float, float]:
        box = await handle.bounding_box()
        if not box:
            raise RuntimeError("element has no box")
        return box["x"] + box["width"] / 2, box["y"] + min(box["height"] / 2, 40)

    async def _ensure_visible(self, page: Page, handle) -> None:
        zoomed = await page.evaluate("() => !!(window.__tut && window.__tut.zoomed)")
        if zoomed:
            return
        moved = await handle.evaluate(
            """e => { const r = e.getBoundingClientRect();
                 if (r.top >= 70 && r.bottom <= innerHeight - 20) return false;
                 e.scrollIntoView({behavior: 'smooth', block: r.height > innerHeight * 0.7 ? 'start' : 'center'}); return true; }"""
        )
        if moved:
            await asyncio.sleep(0.8)

    async def _move_to(self, page: Page, handle, ms: int) -> tuple[float, float]:
        await self._ensure_visible(page, handle)
        x, y = await self._center(handle)
        await page.evaluate("([x, y, ms]) => window.__tut.move(x, y, ms)", [x, y, ms])
        await asyncio.sleep(ms / 1000 + 0.05)
        return x, y

    # ---------- actions ----------
    async def run_action(self, page: Page, action: dict, take: SceneTake | None, t0: float | None) -> None:
        kind = action["do"]
        if kind == "wait":
            await asyncio.sleep(action.get("seconds", 0.5))
        elif kind == "js":
            await page.evaluate(action["js"])
        elif kind == "goto":
            await self.goto(page, action["route"])
        elif kind == "logout":
            await page.evaluate("() => { try { localStorage.clear(); sessionStorage.clear(); } catch (e) {} }")
            await page.goto(self.base_url + "#/login")
            await page.reload()
            await page.wait_for_timeout(600)
        elif kind == "waitFor":
            await self.resolve(page, action["target"], timeout=action.get("timeout", 6000))
        elif kind in ("move", "click", "type", "select"):
            loc, handle = await self.resolve(page, action["target"])
            x, y = await self._move_to(page, handle, action.get("ms", 650))
            if kind == "move":
                return
            await page.evaluate("([x, y]) => window.__tut.ripple(x, y)", [x, y])
            if take is not None and t0 is not None:
                take.clicks.append(time.time() - t0)
            if kind == "click":
                await loc.click(timeout=3000)
            elif kind == "select":
                await loc.select_option(label=action["label"], timeout=3000)
            else:
                await loc.click(timeout=3000)
                await loc.press_sequentially(action["text"], delay=action.get("delay", 55))
        elif kind == "highlight":
            _, handle = await self.resolve(page, action["target"])
            await self._ensure_visible(page, handle)
            opts = {k: action[k] for k in ("n", "color", "below", "pad") if k in action}
            await page.evaluate("([el, label, opts]) => window.__tut.highlight(el, label, opts)", [handle, action.get("label"), opts])
        elif kind == "spotlight":
            _, handle = await self.resolve(page, action["target"])
            await self._ensure_visible(page, handle)
            await page.evaluate("([el]) => window.__tut.spotlight(el)", [handle])
        elif kind == "clear":
            await page.evaluate("() => window.__tut.clear()")
        elif kind == "scrollTo":
            _, handle = await self.resolve(page, action["target"])
            await handle.evaluate("e => e.scrollIntoView({behavior: 'smooth', block: 'center'})")
            await asyncio.sleep(0.9)
        elif kind == "zoom":
            if await page.evaluate("() => window.__tut.zoomed"):
                await page.evaluate("() => window.__tut.unzoom(400)")
                await asyncio.sleep(0.45)
            _, handle = await self.resolve(page, action["target"])
            await self._ensure_visible(page, handle)
            await page.evaluate("([el, s]) => window.__tut.zoom(el, s)", [handle, action.get("scale", 1.5)])
        elif kind == "unzoom":
            await page.evaluate("() => window.__tut.unzoom()")
        elif kind == "lowerThird":
            await page.evaluate(
                "([k, t, ms]) => window.__tut.lowerThird(k, t, ms)", [action.get("kicker", ""), action["title"], action.get("ms", 3800)]
            )
        elif kind == "press":
            await page.keyboard.press(action["key"])
        else:
            raise ValueError(f"unknown action {kind}")

    async def goto(self, page: Page, route: str) -> None:
        await page.goto(self.base_url + "#" + route)
        try:
            await page.wait_for_load_state("networkidle", timeout=4000)
        except Exception:
            pass
        await page.wait_for_timeout(900)

    async def route_ok(self, page: Page, route: str, requires_text: str | None) -> bool:
        await self.goto(page, route)
        current = await page.evaluate("() => location.hash.replace(/^#/, '') || '/'")
        if route != "/" and not current.startswith(route):
            return False
        if requires_text:
            body = await page.evaluate("() => document.body.innerText")
            if not re.search(requires_text, body, re.I):
                return False
        return True

    async def reset_view(self, page: Page) -> None:
        await page.evaluate(
            "() => { if (window.__tut) { window.__tut.ensure(); window.__tut.clear(); window.__tut.unzoom(1); } window.scrollTo(0, 0); }"
        )
        await asyncio.sleep(0.2)

    # ---------- main loops ----------
    async def probe(self, scenes: list[dict]) -> tuple[list[dict], list[dict]]:
        """Resolve each scene's route; drop scenes whose feature isn't in this build yet."""
        async with async_playwright() as pw:
            browser = await pw.chromium.launch()
            ctx = await browser.new_context(viewport={"width": self.width, "height": self.height}, device_scale_factor=1)
            page = await ctx.new_page()
            await self.login(page)
            kept, skipped = [], []
            for scene in scenes:
                if scene.get("type") == "stage":
                    kept.append(scene)
                    continue
                candidates = scene.get("routes") or [scene["route"]]
                chosen = None
                for route in candidates:
                    if route == "/login" or await self.route_ok(page, route, scene.get("requiresText")):
                        chosen = route
                        break
                if chosen:
                    kept.append({**scene, "route": chosen})
                else:
                    reason = f"route(s) {', '.join(candidates)} not available" + (
                        f" or text /{scene['requiresText']}/ not found" if scene.get("requiresText") else ""
                    )
                    log(f"SKIP {scene['id']}: {reason}")
                    skipped.append({"id": scene["id"], "feature": scene.get("feature", scene["id"]), "optional": bool(scene.get("optional")), "reason": reason})
            await browser.close()
        return kept, skipped

    async def login(self, page: Page) -> None:
        await page.goto(self.base_url + "#/login")
        await page.get_by_role("button", name=self.demo["loginButton"]).click()
        await page.wait_for_selector("h1", timeout=10000)
        await page.wait_for_timeout(800)

    async def record(self, scenes: list[dict], narrations: dict[str, Narration], only: set[str] | None = None) -> dict[str, SceneTake]:
        takes: dict[str, SceneTake] = {}
        overlay = (HERE / "overlay.js").read_text(encoding="utf-8")
        stage_url = (HERE / "stage.html").as_uri()
        async with async_playwright() as pw:
            browser = await pw.chromium.launch(args=["--hide-scrollbars", "--force-color-profile=srgb"])
            ctx = await browser.new_context(viewport={"width": self.width, "height": self.height}, device_scale_factor=1)
            app = await ctx.new_page()
            await app.add_init_script(overlay)
            await self.login(app)
            for scene in scenes:
                if only and scene["id"] not in only:
                    continue
                narration = narrations[scene["id"]]
                take = SceneTake(scene["id"], self.build_dir / "frames" / scene["id"], route=scene.get("route"))
                is_stage = scene.get("type") == "stage"
                page = app
                stage_page = None
                if is_stage:
                    stage_page = await ctx.new_page()
                    await stage_page.goto(stage_url)
                    await stage_page.wait_for_timeout(500)
                    page = stage_page
                else:
                    for action in scene.get("setup", []):
                        await self.safe(page, action, None, None, take)
                    if scene.get("route") and scene["route"] != "/login":
                        await self.goto(page, scene["route"])
                    await self.reset_view(page)
                await page.bring_to_front()
                cast = Screencast(page, take.frames_dir, self.width, self.height)
                t0 = await cast.start()
                speech_t0 = t0 + self.lead
                last_cue = 0.0
                for action in scene.get("actions", []):
                    if "at" in action:
                        cue = narration.cue_time(action["at"], last_cue)
                        if cue is None:
                            take.issues.append(f"cue '{action['at']}' not found")
                            cue = last_cue
                        last_cue = cue
                        target = speech_t0 + cue + action.get("after", 0.0)
                        if action["do"] in ("move", "click", "type", "select"):
                            target -= action.get("ms", 650) / 1000
                        delay = target - time.time()
                        if delay > 0:
                            await asyncio.sleep(delay)
                    elif action.get("after"):
                        await asyncio.sleep(action["after"])
                    await self.safe(page, action, take, t0, take)
                end = speech_t0 + narration.duration + self.tail
                remaining = end - time.time()
                if remaining > 0:
                    await asyncio.sleep(remaining)
                else:
                    await asyncio.sleep(0.4)
                take.duration = time.time() - t0
                take.frames = await cast.stop()
                for action in scene.get("teardown", []):
                    await self.safe(page, action, None, None, take)
                if stage_page:
                    await stage_page.close()
                (take.frames_dir / "take.json").write_text(
                    json.dumps({"duration": take.duration, "frames": take.frames, "clicks": take.clicks, "issues": take.issues}, indent=0),
                    encoding="utf-8",
                )
                log(f"{scene['id']}: {take.duration:.1f}s, {len(take.frames)} frames" + (f", issues: {take.issues}" if take.issues else ""))
                takes[scene["id"]] = take
            await browser.close()
        return takes

    async def safe(self, page: Page, action: dict, take_for_clicks, t0, take: SceneTake) -> None:
        try:
            await self.run_action(page, action, take_for_clicks, t0)
        except Exception as exc:  # a missing element should never abort the whole video
            msg = f"{action['do']} {json.dumps(action.get('target', ''))[:80]} failed: {str(exc).splitlines()[0][:120]}"
            take.issues.append(msg)
