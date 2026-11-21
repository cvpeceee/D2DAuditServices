# D2D Audit Services — 1080p Video + Spoken Audio Tutorial & Interactive Product Tour

This directory documents the **Real-App 1080p H.264 + AAC Video & Spoken Audio Tutorial pipeline (`scripts/tutorial/`)**, the **Interactive Animated Architecture & Workflow Simulator (`/tutorial`)**, and the standalone tutorials microsite (`scripts/tutorial/site/`) for the **D2D Audit Services** Super App (Milestones M1–M11).

---

## 1. Real-App Video + Spoken Audio Pipeline (v2 — `scripts/tutorial/`)

The v2 pipeline records the actual Vite demo build (`vite build --mode demo`) in headless Chromium via Playwright + Chrome DevTools Protocol (CDP) screencast, synchronizes cursor movement, highlights, spotlights, zooms, and lower-third chapter banners against neural TTS word boundaries (`edge-tts`), mixes narration with procedurally generated ducked ambient music and UI sound effects, normalizes loudness to `-16 LUFS`, and muxes 1080p H.264 + AAC via `imageio-ffmpeg`.

### Key Files

| File | Purpose |
| --- | --- |
| `scripts/tutorial/build_tutorial.py` | End-to-end orchestrator: builds demo bundle, probes routes (auto-skipping unmerged optional scenes), synthesizes TTS, records scenes, composes transitions/audio/captions, cuts per-chapter clips & stills, and writes `src/web/public/tutorial/*` + `.tutorial-build/dist/*` |
| `scripts/tutorial/storyboard.json` | Declarative storyboard (chapters, scenes, narration text, cue-word-synced actions, and `optional: true` M11 scene probes) |
| `scripts/tutorial/recorder.py` | Playwright + CDP screencast recorder with action scheduling against TTS word boundaries and automatic route/text probing |
| `scripts/tutorial/tts.py` | Neural TTS via `edge-tts` (`en-US-AndrewMultilingualNeural`) with hash-based caching and WebVTT caption cue grouping |
| `scripts/tutorial/compose.py` | FFmpeg clip assembly (`xfade` transitions), procedural ambient music with voice-sidechain ducking, click/whoosh SFX, EBU R128 loudnorm (`-16 LUFS`), MP4 muxing, per-chapter clip cutting, and still extraction |
| `scripts/tutorial/overlay.js` | Motion-graphics overlay injected outside `#root` (animated cursor, click ripples, numbered callout boxes, spotlights, smooth zoom, lower-third chapter banners) |
| `scripts/tutorial/stage.html` | Animated kinetic-typography intro, brand reveal, outro CTA, and poster frame |
| `scripts/tutorial/serve.py` | Local static HTTP server for `src/web/dist` under `/D2DAuditServices/app/` |
| `scripts/tutorial/site/` | Standalone static tutorials site (`index.html`, `walkthrough.html`, `assets/site.{js,css}`, `.devcontainer/`) consuming `media/tutorial-chapters.json` |

### Recording the Full M1–M11 Video

```powershell
pip install --quiet playwright edge-tts imageio-ffmpeg numpy
python -m playwright install chromium
python scripts/tutorial/build_tutorial.py
```

Useful incremental flags:

- `python scripts/tutorial/build_tutorial.py --skip-build` — reuse an existing `src/web/dist` demo build.
- `python scripts/tutorial/build_tutorial.py --only evidence,exposure` — re-record selected scene IDs and reuse cached takes from `.tutorial-build/frames/`.
- `python scripts/tutorial/build_tutorial.py --no-clips --no-app-copy` — fast smoke run writing only to `.tutorial-build/dist/` without overwriting `src/web/public/tutorial/`.

### Outputs

- **In-app tutorial (`/tutorial`)**: `src/web/public/tutorial/d2d-super-app-tutorial.mp4`, `d2d-super-app-tutorial.vtt`, and `tutorial-chapters.json` (v2 manifest automatically recognized by `TutorialPage.tsx`).
- **Standalone tutorials site bundle (scratch, gitignored)**: `.tutorial-build/dist/` (full MP4, WebVTT captions, `chapters.vtt`, `poster.jpg`, per-chapter `clips/*.mp4`, per-scene `shots/*.jpg`, and `tutorial-chapters.json`).

---

## 2. Offline Animated Slide Generator (v1 Fallback — `scripts/generate_tutorial_video.py`)

For environments without Playwright or network access for `edge-tts`, `scripts/generate_tutorial_video.py` generates an 8-chapter 1080p animated architecture walkthrough (`10 fps` Pillow frames + Windows `System.Speech` voiceover):

```powershell
pip install --quiet pillow imageio imageio-ffmpeg
python scripts/generate_tutorial_video.py
```

---

## 3. Verified Technical References in `/tutorial`

All architecture diagrams, code snippets, and packet-flow presets inside `src/web/src/features/tutorial/TutorialPage.tsx` map directly to committed code:

1. **Evidence Chain (`EvidenceChain.cs`, `EvidenceWriter.cs`, `s3_object_lock.tf`)**:
   - `EvidenceWriter.AppendAsync` (per-tenant `SemaphoreSlim`) -> `EvidenceChain.NextHead(previousHead, contentHash, canonical)` (`SHA256(PreviousHead\nContentHash\nCanonical)`), backed by `infra/terraform/s3_object_lock.tf` (`COMPLIANCE` default, configurable to `GOVERNANCE`).
2. **Trust Center v2 (`EnterpriseEndpoints.cs`, `TrustReceiptBuilder`, `TrustPublicPage.tsx`)**:
   - `GET /api/v1/public/trust/{slug}/receipt` verified in-browser via Web Crypto SHA-256; NDA-gated downloads authenticated via constant-time `X-Trust-Token` header.
3. **Enterprise Governance Hub (`GovernanceHubEndpoints.cs`, `GovernanceHub.cs`)**:
   - `GET /api/v1/governance/overview`, `LegalEntityRecord` multi-entity rollup, `PrivacyAssessment` (RoPA & DPIA), `AiModelRecord`, Checkr/KnowBe4/SCIM 2.0, 1-click Remediation PRs, and Audit Time Machine (`GET /api/v1/governance/time-machine`).
4. **Cloud Security Intelligence (`CloudSecurity.cs`, `CloudSecurityEndpoints.cs`)**:
   - `ExposureAnalyzer`, `SarifParser.RedactSnippet`, `DspmScorer`, `FinOpsAnalyzer`, and `AttackPathAnalyzer`.
