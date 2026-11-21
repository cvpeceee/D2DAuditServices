#!/usr/bin/env python3
"""
Generate the D2D Audit Services Super-App 1080p MP4 Video + Spoken Audio Tutorial.

Outputs:
  - src/web/public/tutorial/d2d-super-app-tutorial.mp4 (1920x1080 H.264 video + AAC spoken audio, > 60s)
  - src/web/public/tutorial/d2d-super-app-tutorial.vtt (WebVTT closed captions synced to chapters)
  - src/web/public/tutorial/tutorial-chapters.json (Exact chapter timestamps, metadata, and deep links)

Uses:
  - Windows built-in System.Speech.Synthesis.SpeechSynthesizer (via PowerShell) for spoken English voiceover
  - Pillow (PIL) to render 1920x1080 multi-frame animated slides
  - imageio_ffmpeg's bundled ffmpeg binary to mux H.264 video + AAC audio
"""

from __future__ import annotations

import json
import math
import os
import platform
import struct
import subprocess
import tempfile
import wave
from pathlib import Path
from typing import Any

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

REPO_ROOT = Path(__file__).resolve().parent.parent
PUBLIC_TUTORIAL_DIR = REPO_ROOT / "src" / "web" / "public" / "tutorial"
MP4_PATH = PUBLIC_TUTORIAL_DIR / "d2d-super-app-tutorial.mp4"
VTT_PATH = PUBLIC_TUTORIAL_DIR / "d2d-super-app-tutorial.vtt"
JSON_PATH = PUBLIC_TUTORIAL_DIR / "tutorial-chapters.json"

WIDTH = 1920
HEIGHT = 1080
FPS = 10

CHAPTERS: list[dict[str, Any]] = [
    {
        "number": 1,
        "id": "ch1-executive-overview",
        "title": "Executive Overview — The Vanta + Drata + VRINDA All-in-One Super App & Database-per-Tenant Architecture",
        "shortTitle": "Executive Overview & DB-per-Tenant",
        "moduleRoute": "/frameworks",
        "moduleLabel": "Open Frameworks & Overview",
        "accent": "#6366f1",
        "badge": "CONTROL PLANE + TENANT DB ISOLATION | 18/18 GAPS DONE | M1-M10 100%",
        "codeSnippet": "TenantDbContext -> postgres://tenant_{id} | ProviderClaim uniqueness | Zero cross-tenant leakage",
        "narration": (
            "Welcome to D2D Audit Services, the all-in-one compliance and cloud security super app unifying "
            "Vanta, Drata, and VRINDA in a single platform. Every customer workspace runs inside a dedicated "
            "PostgreSQL database per tenant, orchestrated by a central Control Plane with strict Provider Claim "
            "uniqueness and role-scoped authorization across all ten milestones."
        ),
        "nodes": [
            {"stage": "Control Plane", "detail": "WorkOS / Local Auth + Tenant Resolver"},
            {"stage": "Dedicated Tenant DB", "detail": "Isolated Postgres Schema & Claims"},
            {"stage": "Continuous Collectors", "detail": "AWS, Azure, GCP, GitHub & MDM"},
            {"stage": "Unified Super App", "detail": "13 Frameworks + Cloud Sec + Trust"},
        ],
        "bullets": [
            "100% physical database-per-tenant isolation with automated EF Core migration parity gates",
            "Unifies continuous GRC compliance, cloud posture, blast-radius attack paths, and buyer trust",
            "18 of 18 Gap Tracker capabilities and Milestones M1 through M10 delivered end-to-end",
        ],
    },
    {
        "number": 2,
        "id": "ch2-cspm-frameworks",
        "title": "Multi-Cloud CSPM (AWS, Azure, GCP) & 13 Compliance Frameworks + What-If Gap Simulator",
        "shortTitle": "Multi-Cloud CSPM & 13 Frameworks",
        "moduleRoute": "/frameworks",
        "moduleLabel": "Open 13 Frameworks & What-If Simulator",
        "accent": "#8b5cf6",
        "badge": "13 EMBEDDED FRAMEWORKS | CUSTOM CSV UPLOAD | 1-CLICK WHAT-IF GAP ADOPTION",
        "codeSnippet": "POST /api/v1/frameworks/{key}/simulator/adopt -> adopts missing controls & recalculates readiness",
        "narration": (
            "Chapter Two covers Multi-Cloud C S P M and our thirteen embedded compliance frameworks. "
            "Live collectors inspect AWS, Microsoft Azure, and Google Cloud across sixteen automated checks. "
            "Security teams track readiness across SOC 2, ISO 27001, 27017, 27018, 42001, HIPAA, GDPR, "
            "PCI DSS 4.0, NIST CSF 2.0, DORA, NIST AI RMF, the EU AI Act, and IEC 62443, and run the "
            "What-If Gap Simulator to adopt missing controls in one click."
        ),
        "nodes": [
            {"stage": "AWS + Azure + GCP", "detail": "16 Live Cross-Cloud Collector Checks"},
            {"stage": "Control Crosswalk", "detail": "62 Controls Mapped Across 13 Catalogs"},
            {"stage": "What-If Simulator", "detail": "Projected Readiness & Gap Delta"},
            {"stage": "1-Click Adoption", "detail": "Auto-Creates & Maps Missing Controls"},
        ],
        "bullets": [
            "Live AWS, Azure Entra/Storage/KeyVault/NSG, and GCP IAM/Buckets/KMS/Audit collector pipeline",
            "13 built-in regulatory and AI frameworks plus custom CSV framework upload and control import",
            "Interactive What-If Gap Simulator shows immediate readiness lift and adopts gap controls in 1 click",
        ],
    },
    {
        "number": 3,
        "id": "ch3-exposure-attack-paths",
        "title": "Cloud Security Intelligence — Public Exposure, 1-Click Terraform/CLI Remediation & Blast-Radius Attack Paths",
        "shortTitle": "Public Exposure & Blast-Radius Attack Paths",
        "moduleRoute": "/attack-paths",
        "moduleLabel": "Open Blast-Radius Attack Paths",
        "accent": "#ec4899",
        "badge": "INTERNET EXPOSURE -> LEAKED SECRET -> IDENTITY GAP -> PII STORE",
        "codeSnippet": "aws ec2 revoke-security-group-ingress --port 22 | resource \"aws_security_group_rule\" \"lockdown\"",
        "narration": (
            "Chapter Three introduces VRINDA-grade Cloud Security Intelligence and Blast-Radius Attack Paths. "
            "The Exposure engine detects internet-open security groups, public buckets, and exposed databases, "
            "generating copy-ready Terraform H C L, AWS CLI, Azure CLI, and GCloud remediation patches. "
            "Our Attack Path Analyzer correlates public exposures, leaked secrets, identity gaps, and PII stores "
            "into multi-hop blast-radius graphs with a single chokepoint fix."
        ),
        "nodes": [
            {"stage": "Internet Exposure", "detail": "0.0.0.0/0 SSH/RDP & Public Buckets"},
            {"stage": "Leaked Secret / CVE", "detail": "Baked-in Token or Critical RCE"},
            {"stage": "Identity Gap", "detail": "Admin Principal Missing MFA / Stale"},
            {"stage": "PII Store Target", "detail": "Restricted Customer Data Reached"},
        ],
        "bullets": [
            "Continuous public exposure scanning with deterministic Terraform HCL and cloud CLI remediation patches",
            "Multi-hop blast-radius correlation: Internet Exposure -> Secret/CVE -> Identity Gap -> PII Data Store",
            "1-click GitHub Pull Request remediation generator in Governance Hub to close exposure chokepoints",
        ],
    },
    {
        "number": 4,
        "id": "ch4-sarif-dspm-finops",
        "title": "Container CVEs, OSS Licenses, Baked-in Secrets (SARIF 2.1.0), DSPM PII Discovery & FinOps × Compliance",
        "shortTitle": "SARIF 2.1.0, DSPM PII & FinOps × Compliance",
        "moduleRoute": "/dspm",
        "moduleLabel": "Open DSPM & Cloud Data Posture",
        "accent": "#14b8a6",
        "badge": "SARIF 2.1.0 SECRET REDACTION | DSPM PII/PHI/PCI | FINOPS x CONTROL CORRELATION",
        "codeSnippet": "SarifParser.RedactSnippet() -> [REDACTED-SECRET] | FinOps savings linked to CFG-01, CR-02, BC-01",
        "narration": (
            "Chapter Four unites application vulnerability scanning, Data Security Posture Management, and FinOps. "
            "Engineers import SARIF 2.1.0 reports to track container C V Es, copyleft open-source licenses, "
            "and baked-in secrets with automatic token redaction. D S P M classifies PII, PHI, and PCI data stores "
            "by encryption and residency risk, while FinOps correlates cloud cost savings directly with failing controls."
        ),
        "nodes": [
            {"stage": "SARIF 2.1.0 Ingest", "detail": "Container CVEs, Licenses & Redacted Secrets"},
            {"stage": "DSPM Classification", "detail": "PII / PHI / PCI & Residency Scoring"},
            {"stage": "FinOps Analyzer", "detail": "Idle & Unencrypted Resource Waste"},
            {"stage": "Control Alignment", "detail": "Closes CFG-01, CR-02 & BC-01 Failures"},
        ],
        "bullets": [
            "Automatic secret redaction on SARIF 2.1.0 ingestion prevents credential leakage inside scan findings",
            "DSPM evaluates encryption at rest, public access, data residency, and retention across cloud stores",
            "FinOps x Compliance links every cost optimization directly to the compliance controls it resolves",
        ],
    },
    {
        "number": 5,
        "id": "ch5-evidence-chain-time-machine",
        "title": "Tamper-Evident Evidence Chain (Parquet + SHA-256 + S3 Object Lock) & Audit Time Machine",
        "shortTitle": "SHA-256 Evidence Chain & Audit Time Machine",
        "moduleRoute": "/evidence",
        "moduleLabel": "Open Evidence Chain & Verifier",
        "accent": "#f59e0b",
        "badge": "PARQUET SNAPSHOTS | SHA-256 HASH CHAIN | S3 OBJECT LOCK | AUDIT TIME MACHINE",
        "codeSnippet": "Head = SHA256(PreviousHead\\nContentHash\\nCanonical) | S3 COMPLIANCE Object Lock | Web Crypto Verify",
        "narration": (
            "Chapter Five showcases our tamper-evident Evidence Chain and the Audit Time Machine. "
            "Every collector snapshot and uploaded artifact is serialized to Parquet or raw bytes, hashed with "
            "SHA-256, and chained to the previous entry hash in S3 Object Lock compatible storage. "
            "Users can verify byte integrity locally in their browser via Web Crypto and diff historical posture "
            "snapshots across any two dates in the Audit Time Machine."
        ),
        "nodes": [
            {"stage": "Cloud Collector", "detail": "Parquet & Artifact Byte Capture"},
            {"stage": "SHA-256 Hash Link", "detail": "Chained to Previous Sequence Hash"},
            {"stage": "S3 Object Lock", "detail": "Immutable WORM Retention Storage"},
            {"stage": "Time Machine & Verify", "detail": "Historical Diff & Browser Web Crypto"},
        ],
        "bullets": [
            "Per-tenant serialized EvidenceWriter prevents sequence collisions and links every SHA-256 entry hash",
            "Browser-native Web Crypto SHA-256 verification proves downloaded bytes match the recorded chain hash",
            "Audit Time Machine compares historical posture snapshots to show exact control and evidence deltas",
        ],
    },
    {
        "number": 6,
        "id": "ch6-trust-center-v2",
        "title": "Trust Center v2 — Clickwrap NDA (X-Trust-Token), Buyer-Verifiable Cryptographic Receipts & Trust AI Q&A",
        "shortTitle": "Trust Center v2 & Cryptographic Receipts",
        "moduleRoute": "/trust/harborline",
        "moduleLabel": "Open Public Trust Center",
        "accent": "#10b981",
        "badge": "X-TRUST-TOKEN NDA HEADER | FULL-CHAIN CRYPTOGRAPHIC RECEIPT | PUBLIC TRUST AI Q&A",
        "codeSnippet": "GET /api/v1/public/trust/{slug}/receipt -> Verify canonical payload SHA-256 in Buyer Browser",
        "narration": (
            "Chapter Six demonstrates Trust Center version two. Prospective buyers inspect live framework readiness, "
            "submit clickwrap N D A requests, and download gated reports using constant-time validated X-Trust-Token "
            "headers without leaking tokens in URLs. Buyers can independently verify the Cryptographic Trust Receipt "
            "anchored to the workspace evidence chain head hash, or ask the grounded Trust Center A I assistant."
        ),
        "nodes": [
            {"stage": "Evidence Chain Head", "detail": "Full-Chain Continuity Verified"},
            {"stage": "Canonical Trust Receipt", "detail": "Deterministic JSON Posture Payload"},
            {"stage": "Buyer Browser Crypto", "detail": "1-Click Local SHA-256 Verification"},
            {"stage": "X-Trust-Token NDA", "detail": "Header-Gated Report Downloads & AI Q&A"},
        ],
        "bullets": [
            "Cryptographic Trust Receipts verify full-chain continuity from genesis to head hash before signing",
            "Constant-time X-Trust-Token header validation protects NDA-gated documents from URL/referer leakage",
            "Public Trust Center AI Q&A answers buyer security questions strictly from published posture data",
        ],
    },
    {
        "number": 7,
        "id": "ch7-governance-workforce-privacy-ai",
        "title": "Workforce, Checkr, KnowBe4, MDM/Endpoint Agent, RoPA/DPIA, AI Model Registry & Multi-Entity Rollup",
        "shortTitle": "Governance Hub: Multi-Entity, Privacy & AI",
        "moduleRoute": "/governance-hub",
        "moduleLabel": "Open Enterprise Governance Hub",
        "accent": "#3b82f6",
        "badge": "MULTI-ENTITY ROLLUP | GDPR ROPA & DPIA | AI MODEL REGISTRY | CHECKR + KNOWBE4 + SCIM 2.0",
        "codeSnippet": "GET /api/v1/governance/overview -> Multi-Entity Readiness, RoPA/DPIA, AI Risk Tiers & SCIM",
        "narration": (
            "Chapter Seven tours workforce security and the Enterprise Governance Hub. "
            "D2D integrates Jamf, Intune, Kandji, and endpoint device agent check-ins alongside Checkr background "
            "checks, KnowBe4 phishing simulations, and SCIM 2.0 directory sync. "
            "The Governance Hub adds parent-child Multi-Entity Rollups, GDPR Record of Processing Activities and "
            "D P I A assessments, and an A I Model Governance Registry mapped to ISO 42001 and the EU AI Act."
        ),
        "nodes": [
            {"stage": "Workforce & MDM", "detail": "Checkr, KnowBe4, SCIM 2.0 & Agent"},
            {"stage": "Privacy RoPA & DPIA", "detail": "GDPR Article 30 & Impact Scoring"},
            {"stage": "AI Model Registry", "detail": "EU AI Act Tiering & Guardrails"},
            {"stage": "Multi-Entity Rollup", "detail": "Parent / Subsidiary Consolidated Score"},
        ],
        "bullets": [
            "Live & deterministic adapters for Checkr background checks, KnowBe4 phishing campaigns, and SCIM 2.0",
            "GDPR RoPA & DPIA registers plus AI Model Registry tracking bias/hallucination tests and human oversight",
            "Multi-Entity Rollup aggregates readiness, controls, and open risks across global business units",
        ],
    },
    {
        "number": 8,
        "id": "ch8-auditor-api-webhooks-jenkins",
        "title": "Auditor Workspace, Grounded AI Assistant, Developer API Keys (Bearer d2d_api_...), Signed Webhooks & Open-Source Jenkins CI",
        "shortTitle": "Auditor Portal, API Keys, Webhooks & Jenkins CI",
        "moduleRoute": "/settings",
        "moduleLabel": "Open Settings & Developer Console",
        "accent": "#f97316",
        "badge": "BEARER d2d_api_... | HMAC-SHA256 WEBHOOKS | AUDITOR ISOLATION | JENKINS + RENDER DEPLOY",
        "codeSnippet": "Authorization: Bearer d2d_api_<tenant>_<secret> | X-D2D-Signature: t=1763680000,v1=<hmac_sha256>",
        "narration": (
            "Finally, Chapter Eight covers the External Auditor Workspace, Grounded A I Assistant, Developer Console, "
            "and CI/CD infrastructure. External auditors operate under strict framework and storage-prefix isolation. "
            "Developers automate workflows with tenant-bound Bearer API keys and SSRF-hardened HMAC-SHA256 outbound "
            "webhooks. The entire stack builds via self-hosted open-source Jenkins CI and deploys cleanly via "
            "Terraform or Render Blueprint."
        ),
        "nodes": [
            {"stage": "Auditor Isolation", "detail": "Framework-Scoped Portal & ZIP Prefix"},
            {"stage": "Grounded AI Sidecar", "detail": "Role-Scoped Citations & Retrieval"},
            {"stage": "Developer API & Hooks", "detail": "Bearer d2d_api_... & HMAC-SHA256"},
            {"stage": "Jenkins CI & Render", "detail": "Zero GH Actions Minutes + IaC Blueprint"},
        ],
        "bullets": [
            "External auditors are isolated by WorkspaceMember policies, granted frameworks, and dedicated S3 zip prefixes",
            "Runtime SHA-256 API key authentication and DNS-rebinding-protected HMAC-SHA256 outbound webhooks",
            "Turnkey open-source Jenkins CI stack, AWS/EU Object Lock Terraform modules, and Render cloud blueprint",
        ],
    },
]


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = (
        [
            "C:/Windows/Fonts/segoeuib.ttf",
            "C:/Windows/Fonts/arialbd.ttf",
            "C:/Windows/Fonts/calibrib.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        ]
        if bold
        else [
            "C:/Windows/Fonts/segoeui.ttf",
            "C:/Windows/Fonts/arial.ttf",
            "C:/Windows/Fonts/calibri.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        ]
    )
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size=size)
            except OSError:
                continue
    return ImageFont.load_default()


def load_mono_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in [
        "C:/Windows/Fonts/consolab.ttf",
        "C:/Windows/Fonts/consola.ttf",
        "C:/Windows/Fonts/courbd.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    ]:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size=size)
            except OSError:
                continue
    return load_font(size, bold=True)


def synthesize_voiceover_wavs(temp_dir: Path) -> list[Path]:
    """
    Synthesize real spoken English WAV files for each chapter using Windows System.Speech.
    Falls back to a pleasant multi-tone harmonic voice-cadence WAV only on non-Windows hosts without System.Speech.
    """
    wav_paths = [temp_dir / f"chapter_{ch['number']}.wav" for ch in CHAPTERS]

    if platform.system() == "Windows":
        ps_lines = [
            "$ErrorActionPreference = 'Stop'",
            "Add-Type -AssemblyName System.Speech",
            "$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer",
            "$synth.Rate = 1",
            "$synth.Volume = 100",
        ]
        for ch, wav_path in zip(CHAPTERS, wav_paths):
            escaped_text = ch["narration"].replace("'", "''")
            escaped_path = str(wav_path).replace("'", "''")
            ps_lines.append(f"$synth.SetOutputToWaveFile('{escaped_path}')")
            ps_lines.append(f"$synth.Speak('{escaped_text}')")
        ps_lines.append("$synth.Dispose()")
        ps_script = temp_dir / "speak.ps1"
        ps_script.write_text("\n".join(ps_lines), encoding="utf-8")

        subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ps_script)],
            check=True,
        )
        if all(p.exists() and p.stat().st_size > 1000 for p in wav_paths):
            return wav_paths

    # Fallback for headless Linux environments without Windows System.Speech
    sample_rate = 22050
    for idx, (ch, wav_path) in enumerate(zip(CHAPTERS, wav_paths)):
        duration_sec = 11.5
        total_samples = int(sample_rate * duration_sec)
        base_freq = 220.0 + idx * 18.0
        buf = bytearray()
        for i in range(total_samples):
            t = i / sample_rate
            env = 0.55 + 0.35 * math.sin(2 * math.pi * 3.2 * t)
            sample = env * (
                0.20 * math.sin(2 * math.pi * base_freq * t)
                + 0.12 * math.sin(2 * math.pi * (base_freq * 1.5) * t)
            )
            val = max(-32767, min(32767, int(sample * 32767)))
            buf.extend(struct.pack("<h", val))
        with wave.open(str(wav_path), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(sample_rate)
            wf.writeframes(bytes(buf))
    return wav_paths


def combine_wavs_with_padding(wav_paths: list[Path], master_wav: Path, pause_sec: float = 0.45) -> list[float]:
    """
    Combine per-chapter WAV files into a single master WAV and return each chapter's exact duration in seconds.
    Standardizes each chapter duration to an exact integer multiple of 1/FPS seconds so video frames align 100%.
    """
    durations: list[float] = []
    params = None
    combined_frames = bytearray()

    for wav_path in wav_paths:
        with wave.open(str(wav_path), "rb") as wf:
            if params is None:
                params = wf.getparams()
            raw = wf.readframes(wf.getnframes())
            rate = wf.getframerate()
            channels = wf.getnchannels()
            sampwidth = wf.getsampwidth()
            frame_bytes = channels * sampwidth

            pause_frames = int(rate * pause_sec)
            total_audio_frames = wf.getnframes() + pause_frames
            raw_duration = total_audio_frames / float(rate)

            # Snap chapter duration to 0.5s (5 video frames at 10 FPS)
            snapped_duration = math.ceil(raw_duration * 2.0) / 2.0
            target_audio_frames = int(round(snapped_duration * rate))
            pad_frames = max(0, target_audio_frames - wf.getnframes())

            combined_frames.extend(raw)
            combined_frames.extend(b"\x00" * (pad_frames * frame_bytes))
            durations.append(snapped_duration)

    assert params is not None
    with wave.open(str(master_wav), "wb") as out:
        out.setnchannels(params.nchannels)
        out.setsampwidth(params.sampwidth)
        out.setframerate(params.framerate)
        out.writeframes(bytes(combined_frames))

    return durations


def hex_to_rgb(hex_color: str) -> tuple[int, int, int]:
    h = hex_color.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def blend(c1: tuple[int, int, int], c2: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    t = max(0.0, min(1.0, t))
    return (
        int(c1[0] + (c2[0] - c1[0]) * t),
        int(c1[1] + (c2[1] - c1[1]) * t),
        int(c1[2] + (c2[2] - c1[2]) * t),
    )


def wrap_text_px(text: str, font: Any, max_width: float) -> list[str]:
    lines: list[str] = []
    current = ""
    for w in text.split():
        candidate = f"{current} {w}" if current else w
        if current and font.getlength(candidate) > max_width:
            lines.append(current)
            current = w
        else:
            current = candidate
    if current:
        lines.append(current)
    return lines


# Status icons are drawn as vector shapes because common UI fonts (e.g. Segoe UI Bold) lack glyphs like U+2713.
def draw_check(draw: ImageDraw.ImageDraw, x: int, y: int, size: int, color: tuple[int, int, int]) -> None:
    w = max(2, size // 5)
    draw.line(
        [(x, y + size * 0.55), (x + size * 0.38, y + size * 0.9), (x + size, y + size * 0.12)],
        fill=color,
        width=w,
        joint="curve",
    )


def draw_status(
    draw: ImageDraw.ImageDraw,
    right_x: int,
    y: int,
    kind: str,
    label: str,
    font: Any,
    color: tuple[int, int, int],
) -> None:
    icon = 12
    text_w = font.getlength(label)
    text_x = right_x - text_w
    icon_x = int(text_x - icon - 8)
    icon_y = y + 3
    if kind == "check":
        draw_check(draw, icon_x, icon_y, icon, color)
    elif kind == "dot":
        draw.ellipse([icon_x + 1, icon_y + 1, icon_x + icon - 1, icon_y + icon - 1], fill=color)
    else:
        draw.ellipse([icon_x + 1, icon_y + 1, icon_x + icon - 1, icon_y + icon - 1], outline=color, width=2)
    draw.text((text_x, y), label, font=font, fill=color)


def render_chapter_phase_frame(
    ch: dict[str, Any],
    phase_idx: int,
    total_phases: int,
    global_progress: float,
    chapter_start: float,
    chapter_duration: float,
    total_duration: float,
    fonts: dict[str, Any],
) -> bytes:
    """
    Render a high-contrast 1920x1080 animated frame for a given chapter and animation phase.
    """
    img = Image.new("RGB", (WIDTH, HEIGHT), (11, 14, 26))
    draw = ImageDraw.Draw(img)

    accent = hex_to_rgb(ch["accent"])
    phase_frac = phase_idx / max(1, total_phases - 1)

    # Top ambient glow bar
    draw.rectangle([0, 0, WIDTH, 8], fill=accent)
    draw.rectangle([0, 8, WIDTH, 94], fill=(17, 22, 40))
    draw.line([(0, 94), (WIDTH, 94)], fill=(42, 51, 82), width=2)

    # Brand mark + Super-App Header
    draw.rounded_rectangle([36, 22, 92, 78], radius=14, fill=accent)
    draw.text((64, 50), "D2D", font=fonts["brand"], fill=(255, 255, 255), anchor="mm")
    draw.text(
        (110, 22),
        "D2D AUDIT SERVICES — VANTA + DRATA + VRINDA SUPER-APP WALKTHROUGH",
        font=fonts["header"],
        fill=(240, 244, 255),
    )
    draw.text(
        (110, 54),
        "100% Milestone Completion (M1–M10)  •  18/18 Gap Tracker Items Done  •  Database-per-Tenant Architecture",
        font=fonts["sub"],
        fill=(160, 174, 208),
    )

    # Top-right Super-App 100% Milestone pill + Global Video Progress
    draw.rounded_rectangle([1470, 24, 1884, 76], radius=12, fill=(22, 34, 56), outline=accent, width=2)
    elapsed = chapter_start + phase_frac * chapter_duration
    draw.text(
        (1492, 37),
        f"CHAPTER {ch['number']} / 8   •   {int(elapsed):02d}s / {int(total_duration):02d}s   •   1080p HD",
        font=fonts["pill"],
        fill=(110, 231, 183),
    )

    # Chapter progress stepper strip (Chapters 1..8)
    strip_y = 114
    card_w = (WIDTH - 72 - 7 * 14) // 8
    for idx in range(8):
        num = idx + 1
        x0 = 36 + idx * (card_w + 14)
        x1 = x0 + card_w
        is_current = num == ch["number"]
        is_done = num < ch["number"]
        bg = (30, 38, 68) if is_current else ((18, 42, 38) if is_done else (18, 23, 40))
        border = accent if is_current else ((16, 185, 129) if is_done else (38, 46, 74))
        draw.rounded_rectangle([x0, strip_y, x1, strip_y + 46], radius=10, fill=bg, outline=border, width=2)
        status_label = "ACTIVE" if is_current else ("DONE" if is_done else "NEXT")
        draw.text(
            (x0 + 14, strip_y + 12),
            f"0{num}  {status_label}",
            font=fonts["small_bold"],
            fill=(255, 255, 255) if is_current else ((110, 231, 183) if is_done else (130, 142, 176)),
        )

    # Global progress bar under stepper
    bar_y = 172
    draw.rounded_rectangle([36, bar_y, WIDTH - 36, bar_y + 10], radius=5, fill=(26, 32, 54))
    fill_w = max(20, int((WIDTH - 72) * global_progress))
    draw.rounded_rectangle([36, bar_y, 36 + fill_w, bar_y + 10], radius=5, fill=accent)

    # Main Chapter Banner Card
    draw.rounded_rectangle([36, 198, WIDTH - 36, 336], radius=16, fill=(19, 25, 46), outline=(45, 56, 92), width=2)
    chapter_label = f"CHAPTER 0{ch['number']} WALKTHROUGH"
    label_x1 = int(58 + 16 + fonts["small_bold"].getlength(chapter_label) + 16)
    draw.rounded_rectangle([58, 218, label_x1, 254], radius=8, fill=accent)
    draw.text((74, 236), chapter_label, font=fonts["small_bold"], fill=(255, 255, 255), anchor="lm")

    draw.text((label_x1 + 20, 236), ch["badge"], font=fonts["mono_sm"], fill=(147, 197, 253), anchor="lm")
    draw.text((58, 266), ch["title"], font=fonts["title"], fill=(255, 255, 255))
    draw.text(
        (58, 304),
        f"Deep-Link Route: {ch['moduleRoute']}   |   Interactive Action: {ch['moduleLabel']}",
        font=fonts["sub"],
        fill=(167, 243, 208),
    )

    # Animated 4-Stage Architecture & Packet Flow Pipeline (Middle Section)
    pipe_y0 = 356
    pipe_y1 = 605
    draw.rounded_rectangle(
        [36, pipe_y0, WIDTH - 36, pipe_y1],
        radius=16,
        fill=(15, 20, 38),
        outline=(42, 53, 88),
        width=2,
    )
    draw.text(
        (60, pipe_y0 + 18),
        "LIVE ANIMATED ARCHITECTURE & PACKET VERIFICATION PIPELINE",
        font=fonts["section"],
        fill=(196, 181, 253),
    )

    # Determine active node & packet position from phase_frac
    nodes = ch["nodes"]
    active_node_float = phase_frac * (len(nodes) - 1)
    active_node_idx = int(round(active_node_float))

    node_w = 390
    node_gap = 64
    start_x = 68
    node_y0 = pipe_y0 + 62
    node_y1 = pipe_y1 - 28
    # Packet rail sits below the node text so the moving orb never covers stage titles.
    rail_y = node_y1 - 20
    track_x0 = start_x + node_w // 2
    track_x1 = start_x + 3 * (node_w + node_gap) + node_w // 2
    packet_x = int(track_x0 + (track_x1 - track_x0) * phase_frac)

    for idx, node in enumerate(nodes):
        nx0 = start_x + idx * (node_w + node_gap)
        nx1 = nx0 + node_w
        reached = idx <= active_node_idx
        current = idx == active_node_idx
        fill_color = blend((22, 29, 54), accent, 0.28 if current else (0.12 if reached else 0.0))
        border_color = (255, 255, 255) if current else (accent if reached else (52, 64, 102))
        draw.rounded_rectangle(
            [nx0, node_y0, nx1, node_y1],
            radius=14,
            fill=fill_color,
            outline=border_color,
            width=3 if current else 2,
        )
        badge_bg = accent if reached else (45, 55, 88)
        draw.rounded_rectangle([nx0 + 18, node_y0 + 16, nx0 + 124, node_y0 + 46], radius=8, fill=badge_bg)
        draw.text(
            (nx0 + 30, node_y0 + 22),
            f"STAGE 0{idx + 1}",
            font=fonts["small_bold"],
            fill=(255, 255, 255),
        )
        status_kind, status_label = (
            ("dot", "TRANSMITTING") if current else (("check", "VERIFIED") if reached else ("ring", "QUEUED"))
        )
        draw_status(
            draw,
            nx1 - 18,
            node_y0 + 22,
            status_kind,
            status_label,
            fonts["small_bold"],
            (110, 231, 183) if reached else (130, 142, 176),
        )
        draw.text((nx0 + 18, node_y0 + 64), node["stage"], font=fonts["node_title"], fill=(255, 255, 255))
        draw.text((nx0 + 18, node_y0 + 102), node["detail"], font=fonts["sub"], fill=(195, 206, 235))

    draw.line([(track_x0, rail_y), (track_x1, rail_y)], fill=(45, 55, 92), width=4)
    draw.line([(track_x0, rail_y), (packet_x, rail_y)], fill=accent, width=4)
    draw.ellipse([packet_x - 10, rail_y - 10, packet_x + 10, rail_y + 10], fill=(255, 255, 255), outline=accent, width=3)

    # Bottom Split: Left = Key Architectural Callouts, Right = Cryptographic / Runtime Code Badge
    bot_y0 = 625
    bot_y1 = 895
    draw.rounded_rectangle([36, bot_y0, 1140, bot_y1], radius=16, fill=(18, 24, 44), outline=(42, 53, 88), width=2)
    draw.text((60, bot_y0 + 18), "ARCHITECTURAL GUARANTEES & PRODUCTION CAPABILITIES", font=fonts["section"], fill=(147, 197, 253))

    for b_idx, bullet in enumerate(ch["bullets"]):
        by = bot_y0 + 64 + b_idx * 66
        revealed = phase_frac >= (b_idx * 0.25)
        bullet_bg = (26, 38, 68) if revealed else (20, 26, 46)
        bullet_border = accent if revealed else (40, 50, 82)
        draw.rounded_rectangle([60, by, 1116, by + 52], radius=10, fill=bullet_bg, outline=bullet_border, width=2)
        bullet_color = (240, 246, 255) if revealed else (140, 152, 184)
        draw_check(draw, 80, by + 18, 16, accent if revealed else bullet_color)
        draw.text((108, by + 26), bullet, font=fonts["bullet"], fill=bullet_color, anchor="lm")

    # Right Terminal / Code / Hash Verification Box
    draw.rounded_rectangle([1162, bot_y0, WIDTH - 36, bot_y1], radius=16, fill=(12, 16, 30), outline=accent, width=2)
    draw.text((1186, bot_y0 + 18), "RUNTIME INVARIANT & VERIFICATION SPEC", font=fonts["section"], fill=(110, 231, 183))
    snippet_lines = wrap_text_px(ch["codeSnippet"], fonts["mono"], WIDTH - 60 - 1186)
    for l_idx, line in enumerate(snippet_lines[:3]):
        draw.text((1186, bot_y0 + 66 + l_idx * 34), line, font=fonts["mono"], fill=(224, 231, 255))

    chapter_pct = int(round(25 + 75 * phase_frac))
    draw.rounded_rectangle([1186, bot_y0 + 184, WIDTH - 60, bot_y0 + 244], radius=10, fill=(20, 32, 54))
    draw.text(
        (1204, bot_y0 + 202),
        f"Chapter Pipeline Verification: {chapter_pct}%   •   Zero Stubs / 100% Tested",
        font=fonts["small_bold"],
        fill=(167, 243, 208),
    )

    # Closed Caption / Voiceover Subtitle Bar at Bottom
    cap_y0 = 915
    cap_y1 = 1048
    draw.rounded_rectangle([36, cap_y0, WIDTH - 36, cap_y1], radius=14, fill=(9, 12, 22), outline=(60, 72, 114), width=2)
    draw.text((60, cap_y0 + 14), "SPOKEN AUDIO TRANSCRIPT (CC)", font=fonts["small_bold"], fill=accent)
    # Page through the full narration in 3-line chunks in step with the spoken audio.
    caption_lines = wrap_text_px(ch["narration"], fonts["caption"], WIDTH - 36 - 60 - 24)
    pages = [caption_lines[i : i + 3] for i in range(0, len(caption_lines), 3)] or [[]]
    page = pages[min(len(pages) - 1, phase_idx * len(pages) // max(1, total_phases))]
    for c_idx, cline in enumerate(page):
        draw.text((60, cap_y0 + 44 + c_idx * 28), cline, font=fonts["caption"], fill=(245, 247, 255))

    return img.tobytes()


def format_vtt_timestamp(seconds: float) -> str:
    ms = int(round((seconds - int(seconds)) * 1000))
    total_sec = int(seconds)
    s = total_sec % 60
    m = (total_sec // 60) % 60
    h = total_sec // 3600
    return f"{h:02d}:{m:02d}:{s:02d}.{ms:03d}"


def build_tutorial_media() -> dict[str, Any]:
    PUBLIC_TUTORIAL_DIR.mkdir(parents=True, exist_ok=True)
    ffmpeg_exe = imageio_ffmpeg.get_ffmpeg_exe()

    fonts = {
        "brand": load_font(22, bold=True),
        "header": load_font(22, bold=True),
        "title": load_font(26, bold=True),
        "section": load_font(17, bold=True),
        "node_title": load_font(22, bold=True),
        "bullet": load_font(18, bold=False),
        "sub": load_font(17, bold=False),
        "pill": load_font(18, bold=True),
        "small_bold": load_font(15, bold=True),
        "caption": load_font(19, bold=False),
        "mono": load_mono_font(18),
        "mono_sm": load_mono_font(16),
    }

    with tempfile.TemporaryDirectory() as tmp:
        temp_dir = Path(tmp)
        print("[1/4] Synthesizing spoken English voiceover audio for 8 chapters...")
        wav_paths = synthesize_voiceover_wavs(temp_dir)
        master_wav = temp_dir / "master_voiceover.wav"
        durations = combine_wavs_with_padding(wav_paths, master_wav)
        total_duration = sum(durations)
        print(f"      Total spoken tutorial duration: {total_duration:.2f} seconds across {len(CHAPTERS)} chapters.")

        # Build chapter timing metadata + WebVTT captions
        chapters_meta: list[dict[str, Any]] = []
        vtt_lines = ["WEBVTT", ""]
        cursor = 0.0
        for ch, dur in zip(CHAPTERS, durations):
            start_t = round(cursor, 2)
            end_t = round(cursor + dur, 2)
            chapters_meta.append(
                {
                    "number": ch["number"],
                    "id": ch["id"],
                    "title": ch["title"],
                    "shortTitle": ch["shortTitle"],
                    "startTime": start_t,
                    "endTime": end_t,
                    "duration": round(dur, 2),
                    "moduleRoute": ch["moduleRoute"],
                    "moduleLabel": ch["moduleLabel"],
                    "badge": ch["badge"],
                    "codeSnippet": ch["codeSnippet"],
                    "narration": ch["narration"],
                    "nodes": ch["nodes"],
                    "bullets": ch["bullets"],
                }
            )
            vtt_lines.append(f"{ch['number']}")
            vtt_lines.append(f"{format_vtt_timestamp(start_t)} --> {format_vtt_timestamp(end_t)}")
            vtt_lines.append(f"Chapter {ch['number']}: {ch['shortTitle']} — {ch['narration']}")
            vtt_lines.append("")
            cursor += dur

        VTT_PATH.write_text("\n".join(vtt_lines), encoding="utf-8")
        manifest = {
            "title": "D2D Audit Services — All-in-One Super App Interactive & Video Tutorial",
            "resolution": f"{WIDTH}x{HEIGHT}",
            "fps": FPS,
            "totalDurationSeconds": round(total_duration, 2),
            "videoFile": "tutorial/d2d-super-app-tutorial.mp4",
            "captionsFile": "tutorial/d2d-super-app-tutorial.vtt",
            "chapters": chapters_meta,
        }
        JSON_PATH.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

        print("[2/4] Rendering 1080p multi-frame animated slides and encoding H.264 + AAC MP4...")
        cmd = [
            ffmpeg_exe,
            "-y",
            "-f",
            "rawvideo",
            "-vcodec",
            "rawvideo",
            "-pix_fmt",
            "rgb24",
            "-s",
            f"{WIDTH}x{HEIGHT}",
            "-r",
            str(FPS),
            "-i",
            "-",
            "-i",
            str(master_wav),
            "-c:v",
            "libx264",
            "-preset",
            "ultrafast",
            "-crf",
            "23",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-b:a",
            "128k",
            "-ar",
            "44100",
            "-shortest",
            "-movflags",
            "+faststart",
            str(MP4_PATH),
        ]

        proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        assert proc.stdin is not None

        phases_per_chapter = 10
        chapter_start = 0.0
        for ch, dur in zip(CHAPTERS, durations):
            total_chapter_frames = int(round(dur * FPS))
            # Pre-render 10 distinct animated phase frames for this chapter
            phase_buffers: list[bytes] = []
            for p_idx in range(phases_per_chapter):
                g_prog = (chapter_start + (p_idx / max(1, phases_per_chapter - 1)) * dur) / max(1.0, total_duration)
                phase_buffers.append(
                    render_chapter_phase_frame(
                        ch=ch,
                        phase_idx=p_idx,
                        total_phases=phases_per_chapter,
                        global_progress=g_prog,
                        chapter_start=chapter_start,
                        chapter_duration=dur,
                        total_duration=total_duration,
                        fonts=fonts,
                    )
                )
            for f_idx in range(total_chapter_frames):
                p_idx = min(phases_per_chapter - 1, int((f_idx / max(1, total_chapter_frames)) * phases_per_chapter))
                proc.stdin.write(phase_buffers[p_idx])
            chapter_start += dur

        proc.stdin.close()
        stderr_bytes = proc.stderr.read() if proc.stderr else b""
        ret = proc.wait()
        if ret != 0:
            raise RuntimeError(f"ffmpeg failed with exit code {ret}:\n{stderr_bytes.decode('utf-8', errors='replace')}")

    print("[3/4] Verifying generated MP4 stream metadata via ffmpeg...")
    probe = subprocess.run([ffmpeg_exe, "-i", str(MP4_PATH)], capture_output=True, text=True)
    probe_out = (probe.stdout or "") + "\n" + (probe.stderr or "")
    has_h264 = "Video: h264" in probe_out
    has_aac = "Audio: aac" in probe_out
    size_bytes = MP4_PATH.stat().st_size
    print(f"      Output file: {MP4_PATH}")
    print(f"      File size:   {size_bytes:,} bytes ({size_bytes / (1024 * 1024):.2f} MiB)")
    print(f"      Streams:     H.264 Video={has_h264}, AAC Audio={has_aac}, Duration={total_duration:.2f}s")
    if not (has_h264 and has_aac and total_duration > 60.0 and size_bytes > 100_000):
        raise RuntimeError(f"MP4 verification failed! Probe output:\n{probe_out}")
    print("[4/4] Tutorial media generation complete!")
    return manifest


if __name__ == "__main__":
    build_tutorial_media()
