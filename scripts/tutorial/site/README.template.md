# D2D Audit — Product Tutorials

**D2D Audit** is a compliance automation platform. Map a control once and it satisfies SOC 2, ISO 27001, HIPAA, GDPR, the EU AI Act and more. Evidence is collected automatically and chained so it can't be quietly changed. Cloud exposures and attack paths are prioritised, auditors get their own portal, and a public trust center lets customers verify your proof.

[![Watch D2D Audit in {{DURATION}}](media/poster.jpg)]({{SITE_URL}})

▶ **[Watch the video with chapters and captions]({{SITE_URL}})** · 🧭 **[Step-by-step walkthrough]({{SITE_URL}}walkthrough.html)** · 🚀 **[Open the live demo]({{LIVE_DEMO}})**

## Chapters

| # | Chapter | Starts | Clip |
|---|---------|--------|------|
{{CHAPTER_ROWS}}

## What's in this repository

| Path | What it is |
|------|------------|
| `index.html` | Video player with captions, chapter navigation and a per-feature clip gallery (GitHub Pages) |
| `walkthrough.html` | Interactive step-by-step walkthrough with screenshots and deep links into the live demo |
| `media/d2d-super-app-tutorial.mp4` | Full tutorial, 1080p H.264/AAC ({{DURATION}}, {{SIZE_MB}} MB) |
| `media/d2d-super-app-tutorial.vtt` | English captions (WebVTT) |
| `media/chapters.vtt`, `media/tutorial-chapters.json` | Chapter markers and manifest |
| `media/clips/` | One short clip per chapter |
| `media/shots/` | Screenshots used by the walkthrough |
| `.devcontainer/` | Opens in GitHub Codespaces and serves the site automatically |

This repository contains only tutorial media and static pages. It does not include D2D Audit source code.

## Open in GitHub Codespaces

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/{{REPO}}?quickstart=1)

The codespace starts a small static web server on port **8080** and opens the tutorial site in a preview tab. If the preview doesn't appear, open the **Ports** panel and click the globe icon next to port 8080.

To run it locally instead, use `python -m http.server 8080` in this folder and browse to <http://localhost:8080/>.

## About the video

- Narration uses the Microsoft neural voice `{{VOICE}}`. Captions are timed from the voice's word boundaries.
- Footage is recorded from the D2D Audit demo build, which uses example data only.
- The background music is generated procedurally, so there are no licensing concerns.
- Last generated: {{GENERATED}}.
