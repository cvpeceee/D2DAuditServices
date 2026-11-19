# Implementation Ledger

Living architectural and engineering ledger tracking completed milestones, security fixes, architectural decisions, diagrams, verification results, and commits for **D2D Audit Services** (Vanta + Drata + VRINDA super-app roadmap).

---

## Status Summary

| Milestone | Theme | Status | Verification |
| --- | --- | --- | --- |
| **M1–M6** | Foundation, Integrations/Evidence, Policies/Auditor Portal, Risk/People/SoA/Vendors/Access Reviews, Employee Portal/Training/Assets/Tasks, Computers/Vulns/Trust/Automation | Completed (baseline `ebf29c4`) | 25 unit tests, 5 arch tests, 14 web tests |
| **M7** | Security Hardening, Production Correctness, Real Automation Adapters & Core UX Polish | **Completed** (`0e51dc5`) | 53 unit tests, 7 arch/schema tests, 15 web tests, ESLint, Prettier, Release & Demo builds |
| **M8** | VRINDA Parity+ & Blast-Radius Differentiator: Public Exposure & Open Ports + 1-Click IaC Remediation, Container CVE / OSS License / Baked-in Secret & SARIF 2.1.0 Scanner, DSPM PII Discovery, FinOps × Compliance Correlation & Unified Attack Path Graph | **Completed** | 65 unit tests, 9 arch/schema/cloud tests, 18 web tests, ESLint, Prettier, Release & Demo builds |
| **M9** | Vanta + Drata Parity+ & Differentiators: PCI DSS 4.0 / NIST CSF 2.0 / DORA / ISO 42001 / NIST AI RMF / EU AI Act / IEC 62443 + Custom Framework Upload & What-If Simulator, Trust Center v2 (File Upload/Download, Clickwrap NDA, Buyer-Verifiable Cryptographic Trust Receipts & Trust AI Chat), Grounded AI Compliance Assistant Side Panel & Public API Keys / Webhooks | Planned (Next) | Pending |

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
