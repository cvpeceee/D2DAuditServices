# Roadmap

The customer demo is the constraint. Each milestone is a working slice. Security and the data model are not deferred.

## M1 — Foundation

Done. Monorepo, local Docker Compose, CI, control-plane and per-tenant databases, WorkOS behind `IIdentityProvider` (with a development-only local identity mode), SOC 2 and ISO 27001:2022 Annex A as data, the app shell (login, dashboard, frameworks, controls), an example workspace, and an auditor role that can only read its granted scope.

## M2 — Integrations, automated tests, and evidence

Done. Connect AWS, Google Workspace, Microsoft 365, and GitHub. Run automated tests from those connections. Store evidence as Parquet with a SHA-256 hash chain. The device agent stays out until a collector exists.

## M3 — Policies, auditor portal, and the audit package

Done. Policy workflow, the auditor's request and finding loop, and an exportable audit package. Auditor access stays scoped to the grant.

## M4a — Audit-critical gaps

Done. Risk register with scoring and a treatment-plan export, people synced from the directories with leaver access called out, the ISO 27001 statement of applicability, vendor reviews, access-review campaigns stored as evidence, and a readiness view of failing tests and upcoming due items.

## M4b — Realistic example client

Done. One fictional client is seeded the same way in the development database and the Pages demo. The default brand is Harborline, a cloud data platform of about 1,080 people, and the name, domain, and product live in `src/seed/brand.json`. Regenerate with `node src/seed/build.mjs` before a private rename. The workspace shows the workforce (paged), AWS, Azure, and Google Cloud plus the directories and GitHub, six months of test history, evidence, policies, risks, vendors, an open access review, the ISO 27001 statement of applicability, ISO 27017 and ISO 27018 catalogs, an active audit, activity, and notifications.

## M5 — Employee portal, training, assets, tasks, and roles

Done. My Tasks at `/me` covers policy acknowledgement, security training, MFA, a device note, and a background check. Completing a card stores evidence and refreshes the matching test. Security training has a built-in annual module (slides, quiz, certificate) and an external LMS link with a certificate upload. The asset inventory is filled from the connections, can be owned or marked out of scope, imports and exports CSV, and is included in the audit package. Tasks link to a control, test, risk, vendor, or policy, and the dashboard shows the signed-in person's work. Settings invites users, sets Admin, Editor, Viewer, Employee, or Auditor, deactivates an account, shows SSO status, and records per-audit auditor grants. The risk register adds a library and approval. Vendors add a questionnaire, a decision, and residual risk. Access reviews can be drafted, limited to selected apps, and closed with a remediation note. People show MFA, background check, training, device, hire and leave dates, and a status override with a reason.

## M6 — Background checks, computers, vulnerabilities, trust, automation, and the auditor package

Done. The person drawer records a background-check provider, link, upload, completed date, and an exclusion with a reason. The test "New hires have a background check" follows from that. Computers at `/people/computers` list serial, OS, owner, encryption, screen lock, antivirus, password manager, auto-update, last check-in, and MDM or manual source, with a screenshot upload. Vulnerabilities at `/vulnerabilities` group by asset, by vulnerability, ignored, and SLA history. The trust center has a public page and an admin page for documents and access requests. Settings automation rules call fake Jira, Slack, Teams, and email implementations. HIPAA and GDPR are framework catalogs mapped to the existing controls, and controls can be created or imported from CSV. The auditor portal filters evidence to the engagement dates and the package adds control mapping, connections, vendors, assets, infrastructure access, version control, and manual evidence.

## M7 — Competitor gaps, security hardening, and real automation adapters

Done. The ordered list, priority, and status live in the gap tracker in [FEATURES.md](FEATURES.md). P0 and P1 security & collector targets are complete: Azure and GCP collection, readiness, the executive PDF, the incident register, change management, questionnaire upload/review/export with CSV formula neutralization, editable SOC 2 system description, business continuity, live AWS Inspector + GitHub Dependabot ingest + pentests, and device agent check-in + Jamf/Intune/Kandji import. Security hardening and real configurable Jira/Linear/Slack/Teams/Email automation adapters are complete: cryptographic OAuth state nonces with constant-time verification and fail-closed redirect allowlisting, control-plane `provider_claims` uniqueness (`20261119220000_ProviderClaims`) preventing cross-tenant account hijacking, SSRF & private-IP/DNS protection (`SafeUrl.IsPublicHttpOrHttps` + `IsSameOrigin`), isolated auditor export storage prefixes and `WorkspaceMember` policy isolation, audit period write enforcement, background-check self-attestation protection, forwarded-headers & public trust rate limiting, last-Admin lockout protection, serialized evidence-chain writes, multi-page audit package PDF with CSV formula neutralization and missing-object resilience, GitHub/M365 pagination, live collection telemetry (`check_weeks` + notifications + automation dispatch), recurring task spawning, policy editor/library/acknowledgement UI, evidence downloads with client-side SHA-256 verification, and auditor finding creation UI.

## M8 — Cloud Security Intelligence (VRINDA parity+)

Done. Public Exposure & Open Ports analyzer (`/exposure`) with HCL/shell-escaped 1-click Terraform HCL & CLI IaC remediation snippets, Container Image CVE / OSS License / Baked-in Secret & SARIF 2.1.0 scanner (tabs on `/vulnerabilities` with automatic credential redaction in both messages and snippets), DSPM sensitive data & PII register (`/dspm`), FinOps × Compliance cost-waste correlation (`/finops`), and Unified Blast-Radius Attack Path Graph (`/attack-paths`), with EF Core migration `20261119180000_CloudSecurity`, `WorkspaceMember` + auditor-scope guards, and full Harborline browser demo parity.

## M9 — Enterprise Compliance & AI Super-App (Vanta + Drata parity+ & Differentiators)

Done. Expanded framework catalog to 13 frameworks (PCI DSS 4.0, NIST CSF 2.0, EU DORA, ISO/IEC 42001, NIST AI RMF 1.0, EU AI Act, IEC 62443 + Custom CSV Framework Upload + Multi-Framework What-If Gap Simulator with 1-click gap adoption), Trust Center v2 (real file upload/download, clickwrap NDA acceptance & constant-time approval tokens sent via `X-Trust-Token` header, buyer-verifiable Cryptographic Trust Receipts with full `EvidenceChain` continuity verification and in-browser SHA-256 verification, public Trust Center AI Q&A), live role-scoped Grounded AI Compliance Assistant side panel (`IAssistantProvider`), Developer Console with runtime `Bearer d2d_api_...` authentication & scope enforcement (`ApiKeyAuthenticator`) + HMAC-SHA256 signed outbound webhook delivery & test endpoint (`WebhookDispatcher`, `WebhookSecretProtector`), EF Core migrations `20261119200000_SuperAppEnterprise` and `20261119220000_DeveloperRuntime`, synchronized OpenAPI v1 snapshot & TypeScript schema, and open-source Jenkins CI/CD (`Jenkinsfile` + `ci/jenkins/`) replacing automatic GitHub Actions triggers.

## M10 — 100% Gap Tracker Completion, Standout Differentiators, Production IaC/Render & Interactive Tutorial

Done. Closed all remaining Gap Tracker items (#5, #13, #14, #17, #18) and Parity items (18/18 Gap Tracker `Done`, 100% Parity `Built`): upgraded `QuestionnairePortal` (`GroundedQuestionnaireAssistant`) to call `IAssistantProvider` (`ConfigurableAssistantProvider`) with deterministic citation anchors; shipped Checkr background-check sync, KnowBe4 security awareness & phishing sync, and inbound SCIM 2.0 deprovisioning (`POST /api/v1/scim/v2/Users/{email}/deprovision`) recomputing `offboarded-removed` (`HR-08`) and `HR-01`; built the Enterprise Governance Hub (`/governance-hub`, migration `20261120180000_SuperAppM10`) with multi-entity legal subsidiary rollup (`organization_entities`), GDPR Art. 30/35 RoPA & DPIA (`privacy_assessments`), ISO 42001/EU AI Act model registry (`ai_models`), phishing simulation campaigns (`phishing_campaigns`), external Attack Surface Monitoring (`attack_surface_assets`), Verified Auditor Network directory (`auditor_network`), 1-Click Remediation Pull Request Generator signed directly into `EvidenceChain` (`remediation_pull_requests`), and Audit Time Machine point-in-time posture & chain comparison; authored production AWS + EU (`eu-central-1`) Terraform (`infra/terraform/**`) and Render.com Blueprint (`render.yaml`, `docs/RENDER_DEPLOY.md`); and built the 1080p H.264/AAC spoken-audio video tutorial and interactive animated architecture simulator (`/tutorial`).

## M4 — Demo polish

Tighten the walkthrough used with a customer. The Nov 21, 2026 demo is a fictional multi-cloud data platform (Harborline).
