# Implementation Ledger

Living architectural and engineering ledger tracking completed milestones, security fixes, architectural decisions, diagrams, verification results, and commits for **D2D Audit Services** (Vanta + Drata + VRINDA super-app roadmap).

---

## Status Summary

| Milestone | Theme | Status | Verification |
| --- | --- | --- | --- |
| **M1–M6** | Foundation, Integrations/Evidence, Policies/Auditor Portal, Risk/People/SoA/Vendors/Access Reviews, Employee Portal/Training/Assets/Tasks, Computers/Vulns/Trust/Automation | Completed (baseline `ebf29c4`) | 25 unit tests, 5 arch tests, 14 web tests |
| **M7** | Security Hardening, Production Correctness, Real Automation Adapters & Core UX Polish | **Completed** | 45 unit tests, 7 arch/schema tests, 15 web tests, ESLint, Prettier, Release & Demo builds |
| **M8** | VRINDA Parity+: Multi-Cloud Azure/GCP Collectors, Deep CSPM, Public Exposure & Open Ports + IaC Remediation, Container/Secret/SARIF Scanner, DSPM PII Discovery & FinOps × Compliance | Planned (Next) | Pending |
| **M9** | Vanta + Drata Parity+ & Differentiators: PCI DSS 4.0 / NIST CSF 2.0 / DORA / ISO 42001 / NIST AI RMF / EU AI Act / IEC 62443, Trust Center v2 + Cryptographic Trust Receipts & Clickwrap NDA, Unified Blast-Radius Attack Path Graph, Grounded AI Assistant & Security Questionnaire / RFP Autofill, Framework What-If Simulator & Endpoint Agent Check-In | Planned | Pending |

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
