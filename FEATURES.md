# Features

What is on each screen, who can use it, and how it compares with a Vanta, Drata, Sprinto, or Oneleet program. "Built" means the screen works against the API and the Pages demo with Harborline data. "Later" means the decision is recorded and the screen does not pretend the capability exists.

## Gap tracker

Source of truth for the Nov 21, 2026 Harborline demo and the remaining competitor gaps. Status is Done, In progress, Not started, or Faked-for-demo. A pull request link is filled when the item merges.

| # | Priority | Item | Status | PR |
| --- | --- | --- | --- | --- |
| 1 | P0 | Live Azure and GCP collectors (Entra ID users and MFA, storage public access, Key Vault or KMS, logging, network rules; GCP IAM, buckets, KMS, audit logs) with tests mapped to controls | Done | https://github.com/cvpeceee/D2DAuditServices/pull/12 |
| 2 | P0 | Framework readiness percent on the dashboard and framework pages, plus a one-page executive PDF | Done | https://github.com/cvpeceee/D2DAuditServices/pull/13 |
| 3 | P0 | Security incident register, employee report on `/me`, and the test "incidents resolved within SLA" | Done | https://github.com/cvpeceee/D2DAuditServices/pull/14 |
| 4 | P0 | Change management from GitHub pull requests and Jira, with the test "production changes are reviewed" | Done | https://github.com/cvpeceee/D2DAuditServices/pull/15 |
| 5 | P0 | Security questionnaire automation with AI-powered `IAssistantProvider` (`GroundedQuestionnaireAssistant`), zero-hallucination cited knowledge-base anchor, review, and CSV export | Done | M10 |
| 6 | P0 | SOC 2 system description drafted from tenant data, editable, and included in the audit package | Done | https://github.com/cvpeceee/D2DAuditServices/pull/17 |
| 7 | P0 | Business continuity: impact analysis, plans, and exercise log linked to controls | Done | https://github.com/cvpeceee/D2DAuditServices/pull/18 |
| 8 | P0 | Live AWS Inspector and GitHub Dependabot ingest when connected, plus pentest report tracking | Done | https://github.com/cvpeceee/D2DAuditServices/pull/19 |
| 9 | P1 | Device agent MVP and MDM import (Jamf, Intune, Kandji) | Done | https://github.com/cvpeceee/D2DAuditServices/pull/20 |
| 10 | P1 | Real Jira, Linear, Slack, Microsoft Teams, Resend, and SMTP TLS automation delivery when configured (`ConfigurableIssueTracker`, `ConfigurableNotifier`) | Done | M7 |
| 11 | P1 | PCI DSS 4.0, ISO 42001, NIST CSF 2.0, DORA, NIST AI RMF, EU AI Act, IEC 62443, custom framework upload & What-If Gap Simulator | Done | M9 |
| 12 | P1 | Public REST API keys (`Bearer d2d_api_...` with runtime auth & scope enforcement), OpenAPI (`/openapi/v1.json`), and HMAC-SHA256 signed outbound webhooks (`IWebhookDispatcher`) | Done | M9 |
| 13 | P1 | Checkr background checks, KnowBe4 security awareness & phishing sync, and inbound SCIM 2.0 deprovisioning (`POST /api/v1/scim/v2/Users/{email}/deprovision`) | Done | M10 |
| 14 | P2 | Multi-entity workspaces inside one customer (`organization_entities` + consolidated readiness rollup) | Done | M10 |
| 15 | P2 | Trust Center v2 (real file upload/download, clickwrap NDA, buyer-verifiable Cryptographic Trust Receipts, Trust Center AI Q&A) | Done | M9 |
| 16 | P2 | Grounded AI assistant over product docs and role-scoped tenant data (`IAssistantProvider` + live side panel) | Done | M9 |
| 17 | P2 | Production Terraform for ECS Fargate, Multi-AZ RDS, and S3 Object Lock with EU (`eu-central-1`) data residency option + Render.com Blueprint (`render.yaml`) | Done | M10 |
| 18 | P3 | RoPA and DPIA, AI governance registry, phishing simulations, external attack surface monitoring (ASM), verified auditor network, 1-Click Remediation PRs, and Audit Time Machine (`/governance-hub`) | Done | M10 |

Harborline already shows fixture Azure, GCP, Inspector, and Dependabot data. Live Inspector and Dependabot ingest runs only for a connected workspace that is not the example fixture. Sync on Harborline leaves those rows in place.

Roles: Admin, Control owner, Editor, Viewer, Employee, Auditor. Auditor is read-only on customer records and limited to the frameworks on the grant.

## Screens

| Route | What it does | Key actions | Who |
| --- | --- | --- | --- |
| `/login` | Email and password, or the start of a WorkOS redirect. | Sign in. | Everyone |
| `/` | Counts for controls, failing tests, the signed-in person's work, and 30/90/180-day SVG trends for control health, framework readiness, and failing tests (with table fallback). | Open a failing area, change the trend range. | All workspace roles (trends hidden for Auditor) |
| `/frameworks` | 13 embedded frameworks (`soc2`, `iso27001`, `iso27017`, `iso27018`, `hipaa`, `gdpr`, `pci-dss`, `nist-csf`, `dora`, `iso42001`, `nist-ai-rmf`, `eu-ai-act`, `iec62443`), custom CSV framework upload, and What-If Gap Simulator with 1-click gap adoption. | Open a framework, run What-If simulation, adopt gap controls, upload custom framework CSV. | Auditor sees granted frameworks; simulator and custom upload are for workspace members/writers |
| `/frameworks/:key` | Requirement tree with mapped controls. | Jump to a control. | Same as frameworks |
| `/controls` | Control list, owner, status, mappings, tests. | Assign an owner, create a control, import CSV. | Write: Admin, Control owner, Editor |
| `/controls/:code` | Deep-linked control drawer view. | Inspect mappings, tests, and evidence for a single control code. | Auditor sees in-scope controls |
| `/tests` | Automated checks from the connected accounts. | Read the failing detail. | Auditor sees in-scope checks |
| `/evidence` | Chain items, source, hash, retention. | Upload a file, download evidence bytes, verify the chain. | Auditor sees evidence collected inside the audit period |
| `/policies` | Draft, review, approve, acknowledge. | Install core library, edit draft, approve, assign, remind. | Approve is Admin; draft edit & library install are Write roles |
| `/risks` | Register, score, treatment, library, approval. | Save, adopt a library risk, export. | Write roles |
| `/soa` | ISO 27001 statement of applicability. | Mark applicability and export. | Write roles |
| `/vendors` | Criticality, questionnaire, decision, residual risk. | Save a review. | Write roles |
| `/people` | Directory, MFA, training, device, background check. | Sync, set status, record a background check. | Write roles |
| `/people/computers` | Serial, OS, owner, encryption, screen lock, antivirus, password manager, auto-update, check-in, MDM or manual. | Add a computer, upload a screenshot as evidence. | Write roles |
| `/vulnerabilities` | By asset, by vulnerability, container CVEs & OSS licenses, baked-in secrets (file & line), SARIF 2.1.0 import, ignored, SLA history. | Ignore with a reason, set SLA days, upload pentests, import SARIF, resolve scan findings. | Write roles |
| `/exposure` | Internet-exposed security groups, NSGs, firewalls, public buckets, and databases with 1-click Terraform HCL and CLI remediation. | Scan snapshots, copy IaC/CLI patches, mark resolved with note. | Write roles |
| `/attack-paths` | Unified multi-hop blast-radius attack graph correlating internet exposure, secrets/CVEs, identity gaps, and PII data stores. | Inspect chokepoint remediation and jump to linked controls/findings. | All workspace roles |
| `/dspm` | Data Security Posture Management (DSPM): PII/PHI/PCI discovery, encryption, residency, and risk scoring across cloud stores. | Scan cloud data stores, register/update store classification. | Write roles |
| `/finops` | Cloud cost optimization correlated directly with compliance control failures (`CFG-01`, `CR-02`, `BC-01`). | Track projected/realized savings, apply or dismiss recommendations. | Write roles |
| `/incidents` | Severity, SLA, timeline, root cause, and a linked control. | Report, update status, record a root cause. | Report: any member. Update: write roles |
| `/changes` | GitHub pull requests, Jira issues, and manual changes, with approval and the deploy they shipped in. | Record, approve, reject, link a deploy, sync. | Write roles record and update. Anyone in the workspace can read. |
| `/questionnaires` | Customer security questionnaires. Suggestions cite a policy, control, or evidence item. A person accepts or edits each answer, then exports CSV. | Upload CSV or XLSX, accept, export. | Write roles upload and review. Anyone in the workspace can read and export. |
| `/system-description` | SOC 2 system description drafted from the workspace. The saved text is `system-description.txt` in the audit package. | Edit, save, draft again. | Write roles edit. Anyone in the workspace can read. |
| `/continuity` | Business impact, continuity and disaster-recovery plans, and exercise results linked to a control. | Add a service, plan, or exercise. | Write roles change records. Anyone in the workspace can read. |
| `/me` | The signed-in person's policy, training, MFA, device, background cards, and incident report. | Complete a card. Report an incident. | The signed-in person |
| `/training` | Annual module and an external course with a certificate upload. | Take the quiz, upload a certificate. | Assigned people |
| `/assets` | Inventory from connections, owners, scope, CSV. | Import and export. | Write roles |
| `/tasks` | Work linked to a control, test, risk, vendor, or policy. | Create and complete. | Write roles create. Owners complete |
| `/access-reviews` | Campaigns, selected apps, decisions, remediation. | Draft, start, decide, close. | Write roles |
| `/auditor` | Engagement, date range, sample requests, findings, in-period evidence. | Set the range (Write roles), request, accept, create finding, export isolated package. | Auditor, Admin, Control owner |
| `/trust/:slug` | Public frameworks, control counts, buyer-verifiable Cryptographic Trust Receipt, public & NDA-gated documents (`X-Trust-Token`), clickwrap NDA request, and public Trust Center AI Q&A. | Verify receipt SHA-256 locally, download public/approved-NDA documents, ask Trust AI, request NDA access. | Anyone with the link |
| `/trust-center` | Publish the page, upload/edit document files, approve or deny NDA requests (issuing download tokens). | Save, upload document bytes, approve, deny. | Admin |
| `/settings` | Members, SSO status, auditor grants, automation rules, and Developer Console (SHA-256 API keys & signed outbound webhooks with test delivery). | Invite, deactivate, save a rule, run due reviews, create/revoke API keys, create/test/delete webhooks. | Admin |
| `/integrations` | Connect AWS, Google, Microsoft, GitHub, or load the example fixture. | Connect, collect. | Write roles |
| `/governance-hub` | Multi-entity rollup, GDPR Art. 30/35 RoPA & DPIA, ISO 42001/EU AI Act model registry, Checkr & KnowBe4 phishing sync, SCIM 2.0 deprovisioning, external ASM, 1-Click Remediation PRs signed into `EvidenceChain`, Audit Time Machine, and Verified Auditor Network. | Add legal entities, log RoPA/DPIA & AI models, sync Checkr/KnowBe4, run SCIM 2.0 deprovision, generate Remediation PRs, diff Audit Time Machine dates, invite auditor firms. | Workspace members (Write roles mutate) |
| `/tutorial` | 100% Super-App Completion Dashboard, embedded 1080p MP4 real-app video + neural spoken English voiceover tutorial with 6 clickable chapters (24 scenes) and WebVTT captions, and interactive animated architecture simulator. | Watch/download MP4, jump chapters, simulate packet-flow pipelines, open live modules. | All workspace roles |
| `/activity` | Sign-in and product actions, including auditor access. | Read. | Admin |
| `/notifications` | Inbox for failing tests, assigned tasks, policy acknowledgments, due reviews, auditor requests, and overdue vendor reviews; per-event in-app, email, and Slack preferences. The shell bell shows the unread count. Email digest and Slack are mocked until configured. | Mark read, mark all read, save preferences, send the digest (Admin). | All workspace roles |
| `/reports` | Board, framework readiness, risk, vendor, and personnel reports. Scheduled delivery is mocked until a notifier is configured. | Export CSV or PDF, schedule delivery, send now. | Export: Admin, Control owner, Editor, Viewer. Schedule: Admin |
| `/reset-password` | Confirm a password reset link through WorkOS (local mode logs the link). | Set a new password. | Anyone with the link |
| `/signup` & `/signup/verify` | Self-serve workspace creation with work-email validation, region selection, and verification/provisioning progress. | Create workspace, verify email, launch onboarding. | Anonymous |
| `/invite/accept` | One-time tokenized team invite acceptance flow. | Accept invite and join tenant workspace. | Invited user |
| `/onboarding` | 6-step guided compliance setup wizard (company profile, recommended frameworks, integrations, team invites, policy templates, audit target readiness plan). | Recommend frameworks, pre-scope controls, generate milestones. | Write roles |
| `/settings/billing` | Subscription plan management (Starter / Growth / Enterprise), usage metering against plan limits, Stripe/mock checkout, and invoice history. | Switch plan, inspect usage gates, view invoices. | Admin |
| `/people/lifecycle` | Employee onboarding & offboarding checklists, employee group rules, access-revocation SLAs, and automated offboarding checks. | Sync HR dates, start onboarding/offboarding, revoke access, edit deadlines. | Write roles (employees see own tasks on `/me`) |
| `/vendors/:id` & `/vendor-portal/:token` | Deep vendor risk workspace (tiering, inherent risk, discovery scan, security questionnaires, document review sign-off) plus tokenized external vendor portal. | Scan SSO/expense discovery, send expiring questionnaire link, review & approve vendor answers/docs. | Write roles / invited vendor contact |

Ctrl/Cmd+K opens a command palette that searches pages, actions, and the workspace records the role can see. Every screen shows breadcrumbs, and on narrow screens the navigation is a drawer.

## Background checks

The person drawer records a provider, a link, an uploaded report, a completed date, and an exclusion with a reason. The control test "New hires have a background check" fails while an active person hired in the last 90 days has neither a clear result nor an exclusion with a reason. Saving a check refreshes that test. A new failure can fire the automation rule for a failed test.

## Computers

`/people/computers` lists machines. An agent check-in records encryption, screen lock, antivirus, password manager, and OS update, and marks the row Agent. Jamf, Intune, and Kandji are read when `JAMF_BASE_URL` and `JAMF_TOKEN`, `INTUNE_GRAPH_URL` and `INTUNE_TOKEN`, or `KANDJI_BASE_URL` and `KANDJI_TOKEN` are set. Without those settings sync imports nothing. Harborline keeps its example MDM rows and one agent check-in, HL-AGT-1001, whose screen lock is off. A screenshot upload is stored as evidence.

## Vulnerabilities

Tabs are by vulnerability, by asset, ignored, and SLA history. Each row has a CVE, severity, asset, source (AWS Inspector or GitHub Dependabot), first and last seen, an SLA due date from the workspace windows, and whether a fix is available. Ignore requires a reason and writes a history row. Changing the windows recomputes open due dates and records the change.

## Trust center

`/trust/harborline` is public. It shows frameworks, how many mapped controls are passing, a buyer-verifiable **Cryptographic Trust Receipt** anchored to the workspace `EvidenceChain` SHA-256 head hash (verifiable locally in the browser via Web Crypto), public documents, NDA-gated documents (downloaded via `X-Trust-Token` after approval), a clickwrap NDA access request form, and a public Trust Center security Q&A assistant grounded strictly in published posture. `/trust-center` is where an admin publishes the page, uploads real document files, and approves or denies NDA requests.

## Changes

`/changes` is the production change log. Harborline is seeded with two GitHub pull requests and one Jira issue. CHG-02 is deployed without an approval, so the CM-11 test "production changes are reviewed" fails and names that change. Approving it, or linking a deploy on a change that already has an approval, refreshes the test.

Sync reads merged pull requests when a non-example GitHub App installation is connected. A merged request counts as the deploy, and an approving review by someone other than the author counts as the approval. Jira is read when `JIRA_BASE_URL`, `JIRA_EMAIL`, `JIRA_API_TOKEN`, and `JIRA_PROJECT` are set. The feed asks for issues labeled `production-change`. Status Approved, Done, or Deployed counts as approval. Status Deployed also links the deploy. With those settings missing, sync adds nothing. The Harborline Jira row is seed data.

## Vulnerabilities, live ingest, and pentests

`/vulnerabilities` still lists findings by asset, by vulnerability, ignored, and SLA history. Sync asks AWS Inspector and GitHub Dependabot when the matching connection is connected and is not an example fixture. A successful list replaces the open findings from that source. Ignored rows stay. If the call fails, the current rows stay. Harborline's connections are examples, so sync imports nothing and the seeded Inspector and Dependabot rows remain.

A penetration-test upload takes `cve,severity,asset,title` and adds those rows with source Pentest. Harborline includes the 2026 application test and CVE-2024-5535.

## Continuity

`/continuity` lists the business impact analysis, continuity and disaster-recovery plans, and recovery exercises. Each row names a control. Harborline's region failover drill failed its four-hour RTO, so the BC-01 test "Recovery exercises are recorded" fails and names that exercise. A passing exercise does not clear the test while another exercise failed.

## System description

`/system-description` drafts a SOC 2 system description from the company, region, product summary, connected services, people, vendors, control counts, and framework names. A writer edits the text or drafts it again. The audit package writes that text to `system-description.txt`. Harborline opens on the generated draft.

## Questionnaires

`/questionnaires` uploads a CSV or XLSX whose first column, or a column headed `question`, is the question list. The knowledge base is the workspace policies, controls, and recent evidence titles. `IQuestionnaireAssistant` (`GroundedQuestionnaireAssistant`) injects `IAssistantProvider` (`ConfigurableAssistantProvider`): when an OpenAI-compatible LLM endpoint (`AI:Endpoint` + `AI:ApiKey` + `AI:Model`) is configured, it synthesizes concise answers grounded strictly in the workspace records while preserving the deterministic cited knowledge-base anchor (`Policy POL-...`, `Control CM-11`, `Evidence ...`); when no LLM endpoint is set, it falls back deterministically to zero-hallucination cited retrieval. A person edits the answer, accepts it, and exports the sheet as CSV. Harborline already has the Northgate questionnaire, with the change-management question still open.

## Automation and outbound webhooks

Settings holds automation rules and Developer Console webhooks. Rule triggers are test failed, control not ready, risk created, review due, and critical exposure discovered. Rule actions create a Jira or Linear issue (`ConfigurableIssueTracker`) or send a Slack, Microsoft Teams, Resend Email, or SMTP TLS notification (`ConfigurableNotifier`) when configured, falling back to deterministic local delivery IDs in Development/Testing/Example mode. Outbound webhooks (`WebhookDispatcher`) send HMAC-SHA256 signed JSON payloads (`X-D2D-Signature: t=<unix>,v1=<hex>`) to subscribed HTTPS/HTTP endpoints validated against SSRF and DNS rebinding (`SafeUrl.IsPublicHttpOrHttps` + public DNS IP resolution), and can be tested live from Settings via `POST /api/v1/developer/webhooks/{id}/test`.

## Frameworks, What-If simulator, and custom controls

All 13 built-in frameworks (`soc2`, `iso27001`, `iso27017`, `iso27018`, `hipaa`, `gdpr`, `pci-dss`, `nist-csf`, `dora`, `iso42001`, `nist-ai-rmf`, `eu-ai-act`, `iec62443`) are JSON catalogs mapped onto the control library with original, concise summaries. The frameworks page also provides a What-If Gap Simulator (`FrameworkSimulator`) with 1-click gap control adoption and a custom framework CSV uploader (`CustomFrameworkCsv`). The controls page can create a control and map it to a requirement, or import a CSV with `code,title,description,frequency,ownerName,ownerEmail,framework,requirement,coverage`.

## Auditor package

The portal shows the engagement date range and the evidence collected inside it. Auditors do not see evidence outside that window on the evidence page, in the sample picker, or in the package files. The zip adds:

| File | Contents |
| --- | --- |
| `control-mapping.csv` | Control, requirement, framework, coverage |
| `connections.csv` | Provider, name, status, account |
| `vendors.csv` | Vendor review fields |
| `assets.csv` | Asset inventory |
| `infrastructure-access.csv` | Person accounts |
| `version-control.csv` | Repository snapshots |
| `manual-evidence.csv` | Uploads and other non-automated evidence in scope |
| `system-description.txt` | The saved SOC 2 system description, or a fresh draft when none is saved |

The manifest, integrity report, control matrix, and evidence files are unchanged in purpose.

## Parity

| Capability | Status |
| --- | --- |
| SOC 2 and ISO 27001 catalogs | Built |
| ISO 27017 and ISO 27018 catalogs | Built |
| HIPAA and GDPR catalogs mapped to controls | Built |
| Custom controls and CSV import | Built |
| Policies, acknowledgement, reminders | Built |
| Risk register, library, treatment export | Built |
| Vendors, questionnaire, residual risk | Built |
| People, directory sync, leaver access | Built |
| Background checks and the new-hire test | Built |
| Security training | Built |
| Asset inventory | Built |
| Computers from MDM data and manual screenshot | Built |
| Vulnerability list, ignore, SLA | Built |
| Access reviews | Built |
| Automated tests from AWS, Google, Microsoft, GitHub | Built |
| Evidence hash chain and verify | Built |
| Auditor portal, sampling, findings | Built |
| Audit date range and in-period evidence | Built |
| Audit package with the registers above | Built |
| Trust center v2, real file upload/download, clickwrap NDA (`X-Trust-Token`), buyer-verifiable Cryptographic Trust Receipts, and public AI Q&A | Built |
| Automation rules with configurable Jira, Linear, Slack, Microsoft Teams, Resend, and SMTP TLS adapters (`ConfigurableIssueTracker`, `ConfigurableNotifier`) | Built |
| Developer API keys (`Bearer d2d_api_...` runtime authentication & scope enforcement) and HMAC-SHA256 signed outbound webhooks (`WebhookDispatcher`) | Built |
| Cloud Security Intelligence: Public Exposure & IaC Patches, SARIF 2.1.0 / Container / License / Secret Scanner, DSPM, FinOps × Compliance, and Blast-Radius Attack Path Graph | Built |
| 13 embedded frameworks (including PCI DSS 4.0, NIST CSF 2.0, DORA, ISO 42001, NIST AI RMF, EU AI Act, and IEC 62443) + Custom CSV upload + What-If Gap Simulator | Built |
| Device agent check-in (`IDeviceFleet`) and MDM import (`IMdmImport` for Jamf, Intune, Kandji) | Built |
| Live AWS, Azure (`AzureCloudProvider`), and GCP (`GcpCloudProvider`) collectors + 16 cross-cloud checks | Built |
| Role-scoped Grounded AI Assistant (`IAssistantProvider` / `ConfigurableAssistantProvider` + live side panel) | Built |
| WorkOS sign-in | Built behind `IIdentityProvider`. Local identity in dev. |
| Self-hosted open-source Jenkins CI/CD (`Jenkinsfile` + `ci/jenkins/`) native on Windows or via Docker Compose; GitHub Actions workflows removed | Built |
| SCIM 2.0 user deprovisioning (`POST /api/v1/scim/v2/Users/{email}/deprovision`), Checkr background-check sync, and KnowBe4 training & phishing sync | Built |
| Production AWS Terraform (ECS Fargate, Multi-AZ RDS PostgreSQL 16, WORM S3 Object Lock, KMS, EU `eu-central-1` residency) & Render.com Blueprint (`render.yaml`) | Built |
| Multi-entity legal subsidiary rollup (`organization_entities`), GDPR RoPA/DPIA, ISO 42001/EU AI Act model registry, external ASM, and Verified Auditor Network (`/governance-hub`) | Built |
| 1-Click Remediation Pull Request Generator (signed into `EvidenceChain`) and Audit Time Machine point-in-time posture & chain comparison | Built |
| 1080p MP4 Video + Spoken Audio Tutorial & Interactive Animated Architecture Tour (`/tutorial`) | Built |
| 60-Connector Integration Marketplace (45 new + 15 built-in across 13 categories), encrypted configs, test/sync lifecycle & 16 additional automated checks | Built |
| No-code Custom Test Builder, Test Snooze/Disable, Expiring Evidence Library & Requests, Control Readiness/Comments & Policy Diff/Attestation Campaigns | Built |
| Employee Lifecycle Onboarding/Offboarding Checklists & Access Revocation + Vendor Risk Discovery, Tiering & External Vendor Questionnaire Portal | Built |
| Self-Serve Signup, Guided 6-Step Onboarding Wizard, Stripe/Mock Billing & Feature Gates, Tokenized Team Invites & Risk Heatmap/Acceptance | Built |
| Cross-Platform `.NET` Device Agent (`D2DAudit.Agent`) + Windows/macOS/Linux Installers (HMAC-SHA256 Heartbeats) & 7-Course Security Academy with PDF Certificates | Built |
| Compliance Posture Trends, In-App/Email/Slack Notification Center, Role-Scoped Global Command Palette Search & Scheduled CSV/PDF Reports | Built |
| Edge & Session Security Hardening (`EdgeProtection`, `DistributedSessionGuard`) & PostgreSQL Hardening (`M11DatabaseHardening` append-only triggers, `xmin` concurrency, pool caps) | Built |
