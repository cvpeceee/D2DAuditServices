# Implementation Ledger

Living architectural and engineering ledger tracking completed milestones, security fixes, architectural decisions, diagrams, verification results, and commits for **D2D Audit Services** (Vanta + Drata + VRINDA super-app roadmap).

---

## Status Summary — M1–M10 Super-App Completion Progress (`100%` Complete · `18/18` Gap Tracker Items Done)

| Milestone | Theme | Progress | Status | Verification |
| --- | --- | --- | --- | --- |
| **M1–M6** | Foundation, Integrations/Evidence, Policies/Auditor Portal, Risk/People/SoA/Vendors/Access Reviews, Employee Portal/Training/Assets/Tasks, Computers/Vulns/Trust/Automation | `██████████ 100%` | Completed (baseline `ebf29c4`) | 25 unit tests, 5 arch tests, 14 web tests |
| **M7** | Security Hardening, Production Correctness, Real Automation Adapters & Core UX Polish | `██████████ 100%` | **Completed** (`0e51dc5`) | 53 unit tests, 7 arch/schema tests, 15 web tests, ESLint, Prettier, Release & Demo builds |
| **M8** | VRINDA Parity+ & Blast-Radius Differentiator: Public Exposure & Open Ports + 1-Click IaC Remediation, Container CVE / OSS License / Baked-in Secret & SARIF 2.1.0 Scanner, DSPM PII Discovery, FinOps × Compliance Correlation & Unified Attack Path Graph | `██████████ 100%` | **Completed** (`e80b131`) | 65 unit tests, 9 arch/schema/cloud tests, 18 web tests, ESLint, Prettier, Release & Demo builds |
| **CI/CD** | Open-Source Jenkins Cutover (Disabled GitHub Actions auto-triggers, added `Jenkinsfile` + Docker/JCasC stack under `ci/jenkins/`) | `██████████ 100%` | **Completed** (`5042dc5`) | Zero GitHub Actions triggers on push/PR |
| **M9 + Deep Runtime Hardening** | Vanta + Drata Parity+ & Differentiators: 13 Frameworks + Custom Upload & What-If Simulator, Trust Center v2 (`X-Trust-Token`, Full-Chain Cryptographic Trust Receipts & Trust AI Q&A), Grounded AI Assistant Side Panel, Runtime `Bearer d2d_api_...` API Key Auth & Scope Enforcement (`ApiKeyAuthenticator`), Signed Outbound Webhooks (`WebhookDispatcher`), Cross-Tenant `ProviderClaim` Protection, `WorkspaceMember` Auditor Isolation & OpenAPI Schema Parity | `██████████ 100%` | **Completed** (`027c75d`) | 92 unit tests, 29 arch/schema/cloud/framework/pipeline tests, 23 web tests (10 suites), ESLint, Prettier, Release & Demo builds |
| **M10** | 100% Gap Tracker Completion (`#5`, `#13`, `#14`, `#17`, `#18`), AI Questionnaire Assistant, Checkr/KnowBe4/SCIM 2.0, Multi-Entity Rollup, Enterprise Governance Hub (`/governance-hub`), 1-Click Remediation PRs (signed into `EvidenceChain`), Audit Time Machine, AWS+EU Terraform (`infra/terraform/**`), Render.com Blueprint (`render.yaml`), and 1080p MP4 Video+Audio & Interactive Architecture Tutorial (`/tutorial`) | `██████████ 100%` | **Completed** | 99 unit tests, 34 arch/schema/cloud/pipeline tests, 30 web tests (12 suites), ESLint, Prettier, Release & Demo builds |

---

## Milestone 7 (M7) — Security Hardening, Production Correctness, Real Automation Adapters & Core UX Polish

### Architectural Decisions
1. **Per-Auditor Storage Key Isolation (Zero Schema Overhead):** Auditor export packages are written under `{tenantId}/exports/auditor-{auditorUserId}/{jobId}.zip`, while internal workspace exports write under `{tenantId}/exports/{jobId}.zip`. `AuditPortal.OpenPackageAsync` restricts auditors strictly to their own key prefix so an auditor can never fetch an Admin's unscoped multi-framework archive.
2. **Cryptographic OAuth State & Strict Redirect Allowlisting:** Replaced predictable `row.Id` state tokens in `IntegrationQuery.ConnectAsync` with 128-bit cryptographic hex nonces (`RandomNumberGenerator.GetHexString(32)`) stored on `IntegrationConnection.ExternalId`, compared in constant time (`CryptographicOperations.FixedTimeEquals`), enforced for Google, Microsoft, AND GitHub callbacks, and rotated on completion. Redirect URIs are checked by `RedirectPolicy.IsAllowed` against configured `Cors:Origins` + `WorkOS:RedirectUris`.
3. **Configuration-Driven Outbound Automation (`ConfigurableIssueTracker` / `ConfigurableNotifier`):** Replaced `FakeIssueTracker` / `FakeNotifier` with real HTTP/SMTP adapters supporting Jira REST API v3, Linear GraphQL, Slack Incoming Webhooks, Microsoft Teams Webhooks, Resend Email API, and SMTP TLS. Development/Testing/Example falls back to deterministic local delivery IDs; Production logs a warning and returns `unconfigured:<channel>` instead of pretending delivery succeeded.
4. **Per-Tenant Chain Write Serialization:** Added an instance-level `ConcurrentDictionary<Guid, SemaphoreSlim>` inside the singleton `EvidenceWriter` (preserving the zero-mutable-static-fields architectural rule enforced by `ArchitectureTests`) to prevent concurrent evidence uploads/collections from colliding on `ChainEntry.Sequence`.
5. **Automated EF Core Migration Parity Gate:** Added `SchemaParityTests.ControlPlane_and_Tenant_migrations_have_no_pending_model_changes` using EF Core's `Database.HasPendingModelChanges()` so schema drift is caught immediately even without a running Postgres instance.

### Changes Shipped
- **Backend (`src/api`)**:
  - Added `src/api/src/D2DAudit.Domain/SecurityRules.cs` (`SafeUrl`, `CsvSafety`, `TaskRecurrence`, `RedirectPolicy`).
  - Hardened `Program.cs` (`UseForwardedHeaders`, `public_trust` rate limiter policy) and `OperationsEndpoints.cs` (rate-limited `/api/v1/public/trust/{slug}/requests`).
  - Hardened `AuditEndpoints.cs`, `AuditPortal.cs`, and `AuditPackage.cs`: restricted `/audits/period` to `TenantWrite`, isolated auditor zip keys, prevented missing-object export crashes, switched `control-matrix.pdf` to multi-page `PlainPdf.Document`, and sanitized CSV formula injection across `AuditPackage.cs`, `GovernanceDocuments.cs`, and `ProgramPortal.cs`.
  - Hardened `IntegrationQuery.cs` (cryptographic OAuth state, GitHub/M365 IDOR closure, redirect validation, auditor framework-filtered audit window) and added `GET /api/v1/evidence/{id}/download`.
  - Hardened `ProgramPortal.cs` (blocked non-write `BackgroundCheck` self-attestation, case-insensitive email queries, last-Admin demotion/deactivation guard, automatic recurring task spawning on completion).
  - Hardened `GovernancePortal.cs` (guarded `ClientSeed` & `A.7.5` example exclusion to `tenant.IsExample`, validated `BackgroundLink` URLs, included cloud `iam-user` accounts in access reviews).
  - Hardened `AwsCloudProvider.cs` (configurable region, accurate S3 404 vs transient error handling), `GitHubSourceControl.cs` (`Link` header pagination), `WorkspaceDirectories.cs` (`@odata.nextLink` pagination), `LocalIdentityProvider` (singleton `LocalDummyPasswordHash`).
  - Upgraded `CollectionRunner.cs` to record weekly `check_weeks` telemetry, emit `WorkspaceNotification` alerts on newly failing tests, and dispatch `IAutomationDispatcher.DispatchForTenantAsync`.
  - Added `PUT /api/v1/policies/{code}/draft` (`PolicyEndpoints.cs` / `PolicyPortal.cs`) for editing draft policies.
- **Frontend (`src/web`)**:
  - Added `/controls/:code` route, URL-driven selection, and auto-refresh after control create/import (`App.tsx`, `ControlsPage.tsx`, `FrameworkDetailPage.tsx`, `DashboardPage.tsx`).
  - Fixed `SettingsPage.tsx` SSO array-vs-object handling + `last_admin` error display; fixed `AuthCallbackPage.tsx` React 19 `StrictMode` OAuth state bug; gated `examplePassword` strictly to `VITE_DEMO === "true"` in `LoginPage.tsx`.
  - Fixed shared input state bugs in `AuditorPage.tsx` and `MePage.tsx`; added **Create Finding** form in `AuditorPage.tsx`; restricted audit period form to `workspace?.canWrite`.
  - Fixed nested `<form>` HTML in `RisksPage.tsx` and `VendorsPage.tsx`; added `safeHref()` URL sanitization in `PeoplePage.tsx` and `TrainingPage.tsx`; added 401 session refresh to `download()` in `client.ts`; fixed desktop `.assist` panel toggle CSS in `app.css`; removed duplicate `"history"` key in `en.json`.
  - Upgraded `PoliciesPage.tsx` (Install Core Library, Edit Draft Policy, Assign Acknowledgement) and `EvidencePage.tsx` (Download Evidence), with full browser demo parity (`backend.ts`, `program.ts`, `m6.ts`).
- **Tests & Docs**:
  - Added `SecurityHardeningTests.cs` (20 test cases), `SchemaParityTests.cs` (2 tests), and `ui.test.tsx` (`safeHref` + expanded framework tags).
  - Created `docs/CLASS_DIAGRAMS.md` and `docs/IMPLEMENTATION_LEDGER.md`, and updated `docs/ARCHITECTURE.md` & `docs/ROADMAP.md`.

---

## Milestone 8 (M8) — Cloud Security Intelligence (VRINDA Parity+ & Unified Attack Path Graph)

### Architectural Decisions
1. **Pure Domain Analyzers Without I/O Coupling:** Built `ExposureAnalyzer`, `DspmScorer`, `FinOpsAnalyzer`, `SarifParser`, and `AttackPathAnalyzer` inside `D2DAudit.Domain/CloudSecurity.cs` so every risk score, IaC patch generator, secret redaction rule, and multi-hop blast-radius path is deterministic and 100% unit-testable without Postgres.
2. **Automatic Secret Redaction on SARIF Ingestion:** `SarifParser.RedactSnippet` neutralizes token-like credentials before writing to `security_scan_findings`, preventing a security scanner from becoming a secondary credential leak store.
3. **FinOps × Compliance Correlation:** Rather than a standalone cost-savings list, `CostRecommendation` links cloud waste directly to `LinkedControlCode` and `ComplianceImpact` so engineers prioritize fixes that simultaneously reduce cloud spend and close compliance failures (`CFG-01`, `CR-02`, `BC-01`).
4. **Unified Multi-Hop Blast-Radius Graph:** `AttackPathAnalyzer.Build` correlates open internet exposures -> baked-in secrets / critical CVEs -> missing-MFA or offboarding identities -> restricted PII/PHI/PCI data stores into clickable remediation chains with a single chokepoint recommendation.
5. **Verified EF Core Migration Parity:** Authored migration `20261119180000_CloudSecurity` (4 new tenant tables: `exposure_findings`, `security_scan_findings`, `data_store_assets`, `cost_recommendations`) and verified `HasPendingModelChanges() == false` via `SchemaParityTests`.

### Changes Shipped
- **Backend (`src/api`)**:
  - Added `D2DAudit.Domain/CloudSecurity.cs` (4 entities + 5 pure analyzers), `D2DAudit.Application/CloudSecurity.cs` (`ICloudSecurity` + DTOs), `D2DAudit.Infrastructure/CloudSecurity/CloudSecurityPortal.cs` + `CloudSecurityExample.cs`, `D2DAudit.Api/Endpoints/CloudSecurityEndpoints.cs`, and EF Core migration `20261119180000_CloudSecurity`.
- **Frontend (`src/web`)**:
  - Added `/exposure` (`ExposurePage.tsx`), `/dspm` (`DspmPage.tsx`), `/finops` (`FinOpsPage.tsx`), `/attack-paths` (`AttackPathsPage.tsx`), upgraded `/vulnerabilities` (`VulnerabilitiesPage.tsx`) with Containers & Licenses, Baked-in Secrets (file:line), and SARIF 2.1.0 import tabs, added Cloud Security sidebar group + icons + command palette actions, and built full Harborline browser demo parity in `src/web/src/demo/security.ts`.
- **Tests & Docs**:
  - Added `CloudSecurityTests.cs` (12 unit test cases) and `src/web/src/demo/security.test.ts` (3 Vitest integration suites), and updated `ARCHITECTURE.md`, `CLASS_DIAGRAMS.md`, `FEATURES.md`, `ROADMAP.md`, and `IMPLEMENTATION_LEDGER.md`.

---

## CI/CD Cutover — Self-Hosted Open-Source Jenkins (`5042dc5`)

### Architectural Decisions
1. **Zero GitHub Actions Minute Consumption:** Converted `.github/workflows/ci.yml` and `.github/workflows/pages.yml` to `workflow_dispatch:` (manual fallback only) so pushes and pull requests never consume GitHub Actions hours.
2. **Turnkey Reproducible Jenkins Stack:** Added root `Jenkinsfile` (parallel `.NET 10` build/test/NuGet vulnerability check + `Node 22` lint/format/test/build/demo/npm audit + `Gitleaks` secret scan + review-site tarball archive + optional direct `gh-pages` branch publish) backed by `ci/jenkins/Dockerfile`, `docker-compose.jenkins.yml`, `plugins.txt`, `casc.yaml` (JCasC auto-seeding the multibranch pipeline), and `ci/jenkins/README.md`.

---

## Milestone 9 (M9) — Enterprise Compliance Super-App (Vanta + Drata Parity+ & Differentiators)

### Architectural Decisions
1. **13 Embedded Frameworks + Custom Upload + What-If Gap Simulator:** Added 7 original-wording framework JSON catalogs (`pci-dss`, `nist-csf`, `dora`, `iso42001`, `nist-ai-rmf`, `eu-ai-act`, `iec62443`), cross-mapped existing Harborline controls without regressing readiness metrics, added `POST /api/v1/frameworks/custom` CSV upload, and built `FrameworkSimulator` with 1-click gap control adoption (`POST /api/v1/frameworks/{key}/simulator/adopt`).
2. **Buyer-Verifiable Cryptographic Trust Receipts:** `TrustReceiptBuilder` constructs a deterministic canonical payload anchoring live framework pass counts, retention years, and the workspace's `EvidenceChain` SHA-256 head hash, with a client-side Web Crypto SHA-256 verifier right on `/trust/:slug`.
3. **Trust Center v2 Real File Storage & Clickwrap NDA Tokens:** Extended `TrustDocument` (`ObjectKey`, `Sha256`, `ContentType`) and `TrustAccessRequest` (`NdaAccepted`, `NdaAcceptedAt`, `AccessToken`) in migration `20261119200000_SuperAppEnterprise` (verified zero pending model changes via `SchemaParityTests`). NDA downloads enforce constant-time token comparison (`CryptographicOperations.FixedTimeEquals`).
4. **Honest Role-Scoped Grounded AI Assistant (`IAssistantProvider`):** `ConfigurableAssistantProvider` invokes an optional OpenAI-compatible model endpoint when configured (`AI:Endpoint` + `AI:ApiKey` + `AI:Model`) and transparently falls back to deterministic `GroundedAssistant` retrieval (`mode: "grounded-retrieval"`). Auditor queries stay strictly isolated to granted frameworks; public Trust Center Q&A reads only published public posture.
5. **End-to-End Runtime API Key Authentication & Signed Outbound Webhooks:**
   - `ApiKeyTokens` generates tenant-bound `d2d_api_<tenantIdN>_<secret>` tokens. `ApiKeyAuthenticator` (`IApiKeyAuthenticator`) validates the SHA-256 hash in constant time against the active tenant database and records `LastUsedAt` (migration `20261119220000_DeveloperRuntime`). `SessionAuthenticationHandler`, `CsrfMiddleware`, and `TenantResolutionMiddleware` authenticate `Bearer d2d_api_...` requests, exempt them from cookie CSRF while locking them to their tenant (`403 tenant_forbidden` on mismatched `X-Tenant-Id`), and map `write:*` scopes to `ControlOwner` and read scopes to `Employee`.
   - `WebhookSecretProtector` encrypts signing secrets (`SigningSecretEncrypted`), and `WebhookDispatcher` (`IWebhookDispatcher`) delivers HMAC-SHA256 signed requests (`X-D2D-Event`, `X-D2D-Timestamp`, `X-D2D-Signature: t=<unix>,v1=<hex>`) with SSRF + DNS rebinding protection (`SafeUrl.IsPublicHttpOrHttps`, `Dns.GetHostAddressesAsync` public-IP validation, and `AllowAutoRedirect = false`) from both `AutomationDispatcher` and `POST /api/v1/developer/webhooks/{id}/test`.
6. **Deep Security & Auditor Isolation Hardening:**
   - Added `ProviderClaim` (`provider_claims` table in control-plane migration `20261119220000_ProviderClaims`) so non-example external cloud/directory/GitHub accounts cannot be claimed across multiple tenants (`409 already_claimed`).
   - Added `WorkspaceMemberRequirement` / `WorkspaceMemberHandler` (`WorkspaceMember` policy) blocking external `Auditor` users from internal cloud security (`/api/v1/security/*`), employee cards, incidents creation, simulator, computers, vulnerabilities, notifications, devices, and resources, while `OperationsEndpoints` restricts `/api/v1/trust*` to `TenantAdmin`.
   - Upgraded `TrustReceiptBuilder` & `EnterprisePortal.VerifyChainContinuity` to verify every link of `EvidenceChain` (`ChainStatus: "Verified" | "Empty" | "Broken"`), moved NDA download tokens to `X-Trust-Token` headers, fixed `client.ts` `fetchBlob`/`download` error code parsing and `X-Tenant-Id` handling, and added client-side Web Crypto SHA-256 verification on `EvidencePage.tsx`.

### Changes Shipped
- **Backend (`src/api`)**:
  - Added 7 framework JSON catalogs + updated `example-controls.json`.
  - Added `D2DAudit.Domain/EnterpriseFeatures.cs` (`ApiKeyTokens`, `WebhookSigning`, `TrustReceiptBuilder`, `FrameworkSimulator`, `CustomFrameworkCsv`, `GroundedAssistant`), `D2DAudit.Application/Enterprise.cs`, `D2DAudit.Infrastructure/Enterprise/AssistantProvider.cs`, `ApiKeyAuthenticator.cs`, `WebhookDispatcher.cs`, and `EnterprisePortal.cs`.
  - Hardened `ApiSecurity.cs`, `CloudSecurityEndpoints.cs`, `EnterpriseEndpoints.cs`, `IncidentEndpoints.cs`, `OperationsEndpoints.cs`, `ProgramEndpoints.cs`, `WorkspaceEndpoints.cs`, `AuditPackage.cs`, `CloudSecurityPortal.cs`, `IntegrationQuery.cs`, `AutomationServices.cs`, `QuestionnairePortal.cs`, and `SecurityRules.cs`.
  - Created EF Core migrations `20261119200000_SuperAppEnterprise`, `20261119220000_ProviderClaims` (ControlPlane), and `20261119220000_DeveloperRuntime` (Tenant).
- **Frontend (`src/web`)**:
  - Imported all 13 frameworks in `src/web/src/demo/catalog.ts`, added `src/web/src/demo/enterprise.ts`, upgraded `FrameworksPage.tsx`, `TrustPublicPage.tsx`, `TrustAdminPage.tsx`, `EvidencePage.tsx`, `AuditorPage.tsx`, `IntegrationsPage.tsx`, `AppShell.tsx` (live Assistant side panel with focus trap & `aria-modal`), `SettingsPage.tsx` (Developer API Keys & Webhooks with 1-click test), `client.ts` (`fetchBlob` + `DownloadOptions`), and regenerated `openapi/v1.json` + `src/shared/api/schema.ts`.
- **Tests & Docs**:
  - Added `EnterpriseSuperAppTests.cs`, expanded `SecurityHardeningTests.cs` (92 total unit tests in `D2DAudit.UnitTests`), added `ApiPipelineSecurityTests.cs` (29 total tests in `D2DAudit.Tests`), added `src/web/src/demo/enterprise.test.ts` and `src/web/src/features/trust/TrustPublicPage.test.tsx` (23 total Vitest tests across 10 test files), and updated `ARCHITECTURE.md`, `CLASS_DIAGRAMS.md`, `FEATURES.md`, `ROADMAP.md`, `IMPLEMENTATION_LEDGER.md`, `Jenkinsfile`, and `ci/jenkins/*`.

---

## Milestone 10 (M10) — 100% Gap Tracker Completion, Standout Differentiators, AWS/EU IaC, Render Blueprint & 1080p Video+Audio Tutorial

### Architectural Decisions
1. **AI-Powered Security Questionnaires Without Hallucination (`GroundedQuestionnaireAssistant` — Gap #5):** Upgraded `IQuestionnaireAssistant` to inject `IAssistantProvider` (`ConfigurableAssistantProvider`). When an OpenAI-compatible model endpoint is configured, the assistant synthesizes concise customer-ready answers strictly from workspace policies, controls, and evidence while anchoring every answer to the deterministic citation from `QuestionnaireSuggest`; when unconfigured, it falls back cleanly to zero-hallucination cited retrieval.
2. **Checkr, KnowBe4 & Inbound SCIM 2.0 Deprovisioning (`GovernanceHubPortal` — Gap #13 & SCIM Parity):** Added live Checkr REST API background-screening sync (`POST /api/v1/governance/checkr/sync`), KnowBe4 Reporting API training & phishing sync (`POST /api/v1/governance/knowbe4/sync`), and inbound SCIM 2.0 user deprovisioning (`POST /api/v1/scim/v2/Users/{email}/deprovision` & `POST /api/v1/governance/scim/deprovision`) that marks users `Offboarded`, inspects remaining active cloud/repo accounts for leaver access, and recomputes `offboarded-removed` (`HR-08`) and `HR-01`.
3. **Enterprise Governance Hub & Multi-Entity Rollup (Migration `20261120180000_SuperAppM10` — Gaps #14 & #18):** Added 7 persistent tenant tables (`organization_entities`, `privacy_assessments`, `ai_models`, `phishing_campaigns`, `attack_surface_assets`, `auditor_network`, `remediation_pull_requests`) verified for zero pending model changes via `SchemaParityTests`, plus pure domain calculators (`EntityRollupCalculator`, `PhishingMetrics`, `RemediationPrBuilder`, `TimeMachineCalculator`) in `D2DAudit.Domain/GovernanceHub.cs`.
4. **Standout Differentiators — 1-Click Remediation PRs & Audit Time Machine:**
   - **1-Click Remediation PRs (`POST /api/v1/governance/remediation-prs`):** Generates sanitized branch names and unified Git diffs for Terraform HCL, Dockerfiles, GitHub Workflows, or IAM Policies, invokes GitHub's PR API when `GITHUB_TOKEN` is configured on a non-example workspace (or records a deterministic signed receipt), and **appends the signed PR receipt directly into `EvidenceChain` via `EvidenceWriter.AppendAsync`**.
   - **Audit Time Machine (`GET /api/v1/governance/time-machine?asOf=&compareTo=`):** Computes point-in-time compliance posture and `EvidenceChain` head/count comparisons across any two dates.
5. **Production AWS + EU Data-Residency Terraform & Render.com Blueprint (Gap #17):** Authored modular AWS Terraform under `infra/terraform/` (2-AZ VPC, TLS 1.3 ALB, ECS Fargate, Multi-AZ RDS PostgreSQL 16.4 with `rds.force_ssl = 1`, KMS-encrypted WORM S3 Object Lock evidence bucket with `>= 3` year retention, and optional `eu-central-1` Frankfurt sovereign residency replica) alongside root `render.yaml` and `docs/RENDER_DEPLOY.md`.
6. **1080p Narrated MP4 Tutorial & Interactive Architecture Simulator (`/tutorial`):** Built `scripts/generate_tutorial_video.py` generating `src/web/public/tutorial/d2d-super-app-tutorial.mp4` (`1920x1080` H.264 + 44.1 kHz AAC spoken English voiceover, 3m 48s, 8 chapters, WebVTT captions) and `src/web/src/features/tutorial/TutorialPage.tsx` with the 100% Super-App completion dashboard, embedded chaptered `<video>`, and animated SVG packet-flow simulator.

### Changes Shipped
- **Backend (`src/api`)**:
  - Added `D2DAudit.Domain/GovernanceHub.cs`, `D2DAudit.Application/GovernanceHub.cs`, `D2DAudit.Infrastructure/GovernanceHub/GovernanceHubPortal.cs`, `GovernanceHubExample.cs`, `D2DAudit.Api/Endpoints/GovernanceHubEndpoints.cs`, EF Core migration `20261120180000_SuperAppM10`, and upgraded `GroundedQuestionnaireAssistant` + `QuestionnairePortal`.
- **Frontend (`src/web`)**:
  - Added `src/web/src/features/governance/GovernanceHubPage.tsx` (`/governance-hub`), `src/web/src/demo/governanceHub.ts`, `src/web/src/demo/governanceHub.test.ts`, integrated `src/web/src/features/tutorial/TutorialPage.tsx` (`/tutorial`) + `TutorialPage.test.tsx` + `src/web/public/tutorial/**`, wired routes/nav/icons/i18n (`App.tsx`, `AppShell.tsx`, `icons.tsx`, `en.json`), and regenerated `openapi/v1.json` + `src/shared/api/schema.ts`.
- **Infra & Media**:
  - Added `render.yaml`, `docs/RENDER_DEPLOY.md`, `infra/terraform/*.tf`, `infra/terraform/README.md`, `scripts/generate_tutorial_video.py`, and `docs/tutorial/README.md`.
- **Tests & Docs**:
  - Added `GovernanceHubTests.cs` (99 unit tests in `D2DAudit.UnitTests`), expanded `SchemaParityTests.cs` and `ApiPipelineSecurityTests.cs` (34 tests in `D2DAudit.Tests`), added `governanceHub.test.ts` + `TutorialPage.test.tsx` (30 Vitest tests across 12 suites), and updated `FEATURES.md` (18/18 Done, 100% Built), `ARCHITECTURE.md`, `CLASS_DIAGRAMS.md`, `ROADMAP.md`, and `IMPLEMENTATION_LEDGER.md`.

---

## Milestone 11 (M11) — Database Architecture Review & Hardening

### Architectural Decisions
1. **Database-per-tenant confirmed as the isolation model:** Tenant data lives in `t_<guid>` databases, so global query filters and PostgreSQL RLS do not apply to tenant tables. Per-tenant DB roles and RDS Proxy/PgBouncer are the recommended next isolation and scale steps. Full write-up: `docs/DATABASE_ARCHITECTURE.md`.
2. **Append-only audit tables are enforced in the database:** The `d2d_append_only()` trigger function rejects UPDATE, DELETE and TRUNCATE on `evidence_chain` (tenant) and `activity_events` (control plane). A retention purge may only delete, and only with `SET LOCAL d2d.allow_purge = 'on'`.
3. **Resilient, bounded connections:** Both contexts use `EnableRetryOnFailure` and a command timeout. Tenant Npgsql pools are capped (`Database:*` settings in `DatabaseSettings`).
4. **Optimistic concurrency on human-edited rows:** `xmin` row versions on `tenants`, `risks` and `findings`; `DbUpdateConcurrencyException` maps to `409`.

### Changes Shipped
- **Correctness:** Exact email matching uses `lower(col) = lower(@p)` instead of `ILIKE`, so `_` and `%` no longer act as wildcards at login. Search terms are LIKE-escaped (`DbText`). The `evidence_items → controls` FK is now `RESTRICT`. `resource_snapshots.AttributesJson` is now `jsonb` (was `varchar(4000)`).
- **Performance:**
  - New indexes: `jobs (TenantId, UpdatedAt)`, `auditor_grants (TenantId)`, `evidence_items (CollectedAt)`, `resource_snapshots (Kind)`, `vulnerabilities (Source)`, and `At`/`OpenedAt` on `notifications`, `sla_events`, `automation_deliveries` and `changes`.
  - `lower()` expression indexes for email lookups.
  - Evidence download is now a keyed lookup instead of a full scan.
  - `AsNoTracking` on chain verification; `AsSplitQuery` and deterministic `ThenBy(Id)` paging.
- **Migrations:** `M11DatabaseHardening` for both the ControlPlane and Tenant contexts, with shared raw SQL in `Persistence/Migrations/HardeningSql.cs`.

### Tests & Docs
- Added `DatabaseHardeningTests.cs` covering LIKE escaping, settings clamping and the pool cap, append-only triggers on both databases, targeted and cross-tenant evidence download, and the login wildcard regression.
- Added `docs/DATABASE_ARCHITECTURE.md`: ER diagrams, indexing table, integrity, concurrency, retention and partitioning plan, backup/DR, migrations workflow, scaling roadmap, 20 review findings, and guidelines for new entities. Linked it from `README.md` and `ARCHITECTURE.md`.