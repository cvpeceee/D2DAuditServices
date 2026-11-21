# D2D Audit Services — 1080p Video + Spoken Audio Tutorial & Interactive Product Tour

This directory documents the **1080p H.264 + AAC Video & Spoken Audio Tutorial** and the **Interactive Animated Architecture & Workflow Simulator (`/tutorial`)** for the **D2D Audit Services** Super App (Vanta + Drata + VRINDA all-in-one platform).

---

## 1. Generated Media Artifacts

| Artifact | Path | Format & Specification |
| --- | --- | --- |
| **1080p Video + Spoken Audio Tutorial** | `src/web/public/tutorial/d2d-super-app-tutorial.mp4` | `1920x1080` Full HD, H.264 (`libx264`, `yuv420p`, `10 fps`) + Spoken English Voiceover (`AAC`, `128 kbps`, `44.1 kHz`), `228.00s` (`3m 48s`) total duration, `9.85 MiB` (`10,323,734 bytes`) |
| **Synced WebVTT Closed Captions** | `src/web/public/tutorial/d2d-super-app-tutorial.vtt` | Standard `WEBVTT` closed captions track synchronized to each chapter's exact start and end timestamp |
| **Chapter Manifest & Timestamps** | `src/web/public/tutorial/tutorial-chapters.json` | Machine-readable JSON manifest containing exact `startTime`, `endTime`, `duration`, `moduleRoute`, `codeSnippet`, `nodes`, and `narration` for all 8 chapters |
| **Reproducible Generator Script** | `scripts/generate_tutorial_video.py` | Synthesizes spoken English `.wav` narration via Windows `System.Speech.Synthesis.SpeechSynthesizer`, renders multi-frame animated 1080p slides with `Pillow`, and muxes H.264 + AAC via `imageio-ffmpeg` |

---

## 2. Regenerating the Video & Audio Tutorial

To regenerate `d2d-super-app-tutorial.mp4`, `d2d-super-app-tutorial.vtt`, and `tutorial-chapters.json` locally:

```powershell
pip install --quiet pillow imageio imageio-ffmpeg
python scripts/generate_tutorial_video.py
```

The script automatically verifies that the resulting `.mp4` contains both an H.264 video stream and an AAC audio stream and exceeds 60 seconds in duration.

---

## 3. 8-Chapter Walkthrough Structure

1. **Chapter 1: Executive Overview — The Vanta + Drata + VRINDA All-in-One Super App & Database-per-Tenant Architecture**
   - Physical PostgreSQL database-per-tenant isolation (`TenantDbContext`), `ProviderClaim` cross-tenant uniqueness, and 100% completion across Milestones M1–M10 and all 18 Gap Tracker capabilities.
2. **Chapter 2: Multi-Cloud CSPM (AWS, Azure, GCP) & 13 Compliance Frameworks + What-If Gap Simulator**
   - 16 automated cross-cloud checks mapped across 13 embedded catalogs (`soc2`, `iso27001`, `iso27017`, `iso27018`, `hipaa`, `gdpr`, `pci-dss`, `nist-csf`, `dora`, `iso42001`, `nist-ai-rmf`, `eu-ai-act`, `iec62443`), custom CSV framework upload, and 1-click What-If Gap Simulator control adoption.
3. **Chapter 3: Cloud Security Intelligence — Public Exposure, 1-Click Terraform/CLI Remediation & Blast-Radius Attack Paths**
   - Internet-open security groups/NSGs/firewalls and public buckets with copy-ready Terraform HCL & CLI lockdown snippets, correlated into multi-hop blast-radius attack graphs (`Internet Exposure -> Leaked Secret -> Identity Gap -> PII Store`).
4. **Chapter 4: Container CVEs, OSS Licenses, Baked-in Secrets (SARIF 2.1.0), DSPM PII Discovery & FinOps × Compliance**
   - Automatic secret redaction on SARIF 2.1.0 ingestion (`SarifParser.RedactSnippet`), DSPM PII/PHI/PCI data store classification, and FinOps cost recommendations linked directly to failing compliance controls (`CFG-01`, `CR-02`, `BC-01`).
5. **Chapter 5: Tamper-Evident Evidence Chain (Parquet + SHA-256 + S3 Object Lock) & Audit Time Machine**
   - Per-tenant serialized `EvidenceWriter`, chained `SHA-256` entry hashes stored with S3 Object Lock WORM retention, client-side Web Crypto byte verification, and historical posture comparison in the Audit Time Machine.
6. **Chapter 6: Trust Center v2 — Clickwrap NDA (`X-Trust-Token`), Buyer-Verifiable Cryptographic Receipts & Trust AI Q&A**
   - Full-chain continuity verification (`genesis -> head`), browser-verifiable Cryptographic Trust Receipts, constant-time `X-Trust-Token` header NDA downloads, and public Trust Center AI Q&A.
7. **Chapter 7: Workforce, Checkr, KnowBe4, MDM/Endpoint Agent, RoPA/DPIA, AI Model Registry & Multi-Entity Rollup**
   - Jamf/Intune/Kandji + device agent check-ins, Checkr background checks, KnowBe4 phishing simulations, SCIM 2.0 provisioning, GDPR RoPA/DPIA registers, AI Model Governance Registry, and parent/subsidiary Multi-Entity Rollup in `/governance-hub`.
8. **Chapter 8: Auditor Workspace, Grounded AI Assistant, Developer API Keys (`Bearer d2d_api_...`), Signed Webhooks & Open-Source Jenkins CI**
   - Framework- and storage-prefix-isolated external Auditor Portal, role-scoped Grounded AI Assistant side panel, runtime SHA-256 `Bearer d2d_api_...` authentication, SSRF/DNS-rebinding-hardened `HMAC-SHA256` outbound webhooks, open-source Jenkins CI, and AWS/EU Terraform + Render Blueprint deployment.
