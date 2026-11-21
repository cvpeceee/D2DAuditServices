# D2D Audit Services

D2D Audit Services is a compliance automation platform for companies undergoing audit, in the same category as Vanta and Drata. The first release covers IT frameworks, starting with ISO 27001 and SOC 2, with HIPAA and GDPR catalogs on the same core. Manufacturing and IoT/OT frameworks come later. Locked decisions are in [DECISIONS.md](DECISIONS.md). The milestone plan is in [docs/ROADMAP.md](docs/ROADMAP.md). Engineering rules are in [docs/ENGINEERING.md](docs/ENGINEERING.md). The system shape is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The database design, indexing, integrity, and review findings are in [docs/DATABASE_ARCHITECTURE.md](docs/DATABASE_ARCHITECTURE.md). Each screen is in [docs/FEATURES.md](docs/FEATURES.md).

A public clickable demo of the example workspace is at [https://cvpeceee.github.io/D2DAuditServices/app/](https://cvpeceee.github.io/D2DAuditServices/app/). It runs in the browser with example data only. Sign in as `chandra.vell@harborline.example` or `auditor@northgate.example`. The example password is on the login screen.

The review site lives on [GitHub Pages](https://cvpeceee.github.io/D2DAuditServices/), holds example data only, and is public. The approved look is [docs/mockups/index.html](docs/mockups/index.html).

Milestones 1 through 6 are on main. The Pages demo includes computers, vulnerabilities, the trust center, automation rules, HIPAA and GDPR, custom controls, and the extended auditor package.

## Run locally

Preferred, from the repository root, when Docker and the .NET 10 SDK are installed. This starts the Aspire AppHost, which runs Postgres, MinIO, Temporal, the API, and the Vite app, and prints the dashboard URL for logs and traces:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet run --project src/aspire/D2DAudit.AppHost
```

`./scripts/dev.sh` does the same when Docker is available.

Alternative, without the Aspire dashboard:

```bash
docker compose up --build
```

- API: http://localhost:8080
- Web (nginx): http://localhost:8088
- Postgres: localhost:5432, database `d2d_control`, user `d2d`, password `d2d_dev_password`
- MinIO: http://localhost:9000 (console http://localhost:9001)
- Temporal: localhost:7233

If Docker is not installed, `./scripts/dev.sh` starts the API and Vite against a Postgres already listening on localhost, and the web app is http://localhost:5173. Collection then runs in-process and evidence files land under `data/evidence`.

Without Docker, from a machine that already has the .NET 10 SDK, Node 22, and Postgres 16:

```bash
# Postgres role used by the checked-in development settings.
# createuser -s d2d   (or)   CREATE ROLE d2d LOGIN PASSWORD 'd2d_dev_password' CREATEDB;
# createdb -O d2d d2d_control

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
cd src/api && dotnet run --project src/D2DAudit.Api
# second terminal
cd src/web && npm ci && npm run dev
```

The API listens on http://localhost:8080. Vite listens on http://localhost:5173 and proxies `/api` and `/hubs`.

On first start in Development, the API migrates the control-plane database, creates the example tenants, and loads SOC 2, ISO 27001, ISO 27017, and ISO 27018 into each tenant database. That provisioning job is real work; the jobs tray shows its step and percent.

### Example workspace

Local identity mode only. Password for every example account: `Example-Only-1!`

The default client is Harborline, a fictional multi-cloud data platform. Its name, legal name, product, and `.example` domain are in `src/seed/brand.json`. Change that file and run `node src/seed/build.mjs` to rebuild `src/seed/client.json`, which both the development seeder and the Pages demo read.

| Email | Workspace | Role |
| --- | --- | --- |
| chandra.vell@harborline.example | Harborline (US) | Admin |
| priya.nair@harborline.example, marcus.lee@harborline.example, lena.cho@harborline.example | Harborline | Control owner |
| dev.patel@harborline.example | Harborline | Employee, read-only |
| auditor@northgate.example | Harborline | Auditor, read-only, SOC 2 only, through 2026-12-15 |
| owner@northwind.example | Northwind Labs (EU) | Admin |

Harborline and Northwind are separate databases. A user of one cannot read the other. The auditor does not see ISO-only controls. The statement of applicability is the ISO 27001 Annex A list. ISO 27017 and ISO 27018 are additional framework catalogs.

## WorkOS

Create a WorkOS project and set both variables. Setting only one makes the API refuse to start.

```bash
WORKOS_API_KEY=sk_...
WORKOS_CLIENT_ID=client_...
```

In the WorkOS dashboard:

1. Enable email and password, and require an authenticator-app (TOTP) factor for those users. The login screen completes TOTP. Passkeys stay on the WorkOS side and are not a ceremony in this milestone.
2. Enable Google and Microsoft sign-in.
3. Add redirect URIs exactly: `http://localhost:5173/auth/callback` and `http://localhost:8088/auth/callback`, plus the production origin when you have one.
4. For company SSO, create an organization and set `WORKOS_ORGANIZATION_ID` and `WORKOS_SSO_DOMAIN` before the demo seed runs. The seed writes that organization id onto the example tenant's domain row. A domain with no organization returns `sso_not_configured`.
5. Optional: set a JWT template audience and `WORKOS_TOKEN_AUDIENCE` to the same value. Access tokens must be RS256 from `https://api.workos.com/sso/jwks/{clientId}`, issuer `https://api.workos.com`, and the `client_id` claim must match `WORKOS_CLIENT_ID`.

Leave both variables unset and the API uses local identity mode. That mode exists only when `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing`. Production and Staging refuse to start without WorkOS. Local mode does not simulate Google, Microsoft, SSO, or MFA.

There is no public signup endpoint. Tenants are created by the provisioner (the demo seed, and later an operator path).

## Integrations

Example workspaces (Harborline and Northwind Labs) load fixture resources and do not call AWS, Azure, Google Cloud, Google, Microsoft, or GitHub. A real workspace connects with the credentials below. Leave them unset until you are ready. `TENANT_DATA_KEY` is 32 bytes, base64, and is required before a Google refresh token is stored. Production and Staging also require `TEMPORAL_ADDRESS`, `Storage__Endpoint`, and `Storage__ObjectLock=true`.

### AWS

Create an IAM role in the collector account and set `AWS_COLLECTOR_ROLE_ARN` to its ARN. In the customer account, create a role with the permission policy the connect screen shows (read-only account summary, S3 public-access and encryption, CloudTrail, and `sts:GetCallerIdentity`). Trust the collector role and the external id the screen generates. No customer access keys are stored. The API assumes the role with the ambient credential chain.

### Google Workspace

In Google Cloud, create an OAuth client and set `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`. Enable the Admin SDK. The consent screen asks for `admin.directory.user.readonly`. Add the redirect URI `http://localhost:5173/integrations` (and the deployed origin). A Workspace admin grants consent. The refresh token is encrypted with `TENANT_DATA_KEY`.

### Microsoft 365

Register an application and set `MS_CLIENT_ID` and `MS_CLIENT_SECRET`. Application permissions: `User.Read.All`, `Directory.Read.All`, and `UserAuthenticationMethod.Read.All`. Admin consent returns the directory tenant id. Collection uses client credentials. No refresh token is stored.

### GitHub

Create a GitHub App. Permissions: repository administration read, metadata read, organization members read. No write permissions. Set `GITHUB_APP_ID`, `GITHUB_APP_SLUG`, and `GITHUB_APP_PRIVATE_KEY` (PEM). The installation id is stored per workspace. The private key stays in the environment.

## Evidence

Automated checks write one Parquet object per check. Manual upload stores the original bytes. Each object is SHA-256 hashed and linked to the previous hash. Verify reads the objects back and does not change them. Local development uses a folder or MinIO. Production object storage must have Object Lock enabled.

## Policies

The example workspace installs six short original policy templates (information security, access control, change management, incident response, business continuity, and privacy). Privacy maps only to the ISO control, so a SOC 2 auditor does not see it. Versions move Draft → In review → Approved. A tenant admin approves. People acknowledge the approved version. Reminders are due when the date has passed, the person has not acknowledged, and they were not reminded today. Sending reminders is an explicit action. There is no background mailer.

## Auditor portal

An engagement has a scope, a period, and a framework. Auditors request samples (population, sample size, source, due date). A workspace member responds and can attach evidence. The auditor accepts the sample and records findings as an exception or an observation. Management writes the response. Auditors cannot change customer records: controls, policies, evidence, responses, or management replies. Creating a request, accepting a sample, recording a finding, and exporting the package are auditor actions. Every one of those actions is written to the activity log. Employees cannot create requests or export.

## Audit package

Download builds a zip in the background and reports the step over the same jobs hub as collection. The zip holds in-scope evidence, a hashes manifest, an integrity report for the full chain, and a control matrix as CSV and PDF. A broken chain still produces a package whose report says so. The job is the record of that check.

## Insights, notifications, and reports

- Posture snapshots: one row per workspace per day (controls passing and total per framework, failing tests, open vulnerabilities by severity, open tasks, overdue items). A snapshot is written after each collection and by an in-process daily job. `GET /api/v1/insights/trends?days=` reads the history and `POST /api/v1/insights/snapshot` records today.
- Notifications: generated from current state for a failing test, a task assigned to you, a policy waiting for your acknowledgment, a risk or vendor review that is due or overdue, and an open auditor request. Each person can choose in-app, email, or Slack per event type (`/notifications`). Live updates go over the jobs hub. The daily email digest uses the existing notifier (`ConfigurableNotifier`), so it is mocked until Resend, SMTP, or Slack is configured.
- Search: `GET /api/v1/search?q=` returns controls, policies, risks, vendors, people, evidence, tests, and frameworks the caller can see. Auditors see only granted frameworks and their controls, policies, tests, and evidence. Employees see frameworks and policies. Ctrl/Cmd+K opens the palette.
- Reports (`/reports`): board, framework readiness, risk, vendor, and personnel reports as CSV or one-page PDF (`GET /api/v1/reports/{key}?format=csv|pdf`). Admins can schedule delivery (daily, weekly, monthly) to a recipient list. Delivery uses the same notifier and is mocked until configured. Auditors and employees cannot export reports.

No new configuration keys are needed.

## Tests

```bash
dotnet test src/api/D2DAudit.slnx
cd src/web && npm test && npm run lint && npm run format:check && npm run build
```

Integration tests start Postgres with Testcontainers. Set `TEST_POSTGRES` to an existing admin connection when Docker is not available, for example `Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres`. The role needs `CREATEDB`. Unit tests do not need a database.

Without Docker on Windows, `scripts/start-local-postgres.ps1` runs a portable PostgreSQL from the EnterpriseDB binaries zip. It creates the cluster on first run (superuser `d2d` / `d2d_dev_password`) and starts it on port 5432. `-PgHome`, `-DataDir`, `-Port`, `-User`, and `-Password` override the defaults (`..\tools\pgsql` and `..\tools\pgdata` next to the checkout). Stop it with `pg_ctl -D <DataDir> stop`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start-local-postgres.ps1 -PgHome C:\tools\pgsql -DataDir C:\tools\pgdata
$env:TEST_POSTGRES = "Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres"
dotnet test src/api/D2DAudit.slnx
```

Browser smoke test of the demo build (not part of `npm test`): `cd src/web && npx playwright install chromium && npm run e2e`. It signs in as the demo admin, opens every route, and fails on console errors, unhandled rejections, failed demo API calls, or raw i18n keys.

## Layout

- `src/api` — C# solution. Domain, Application, Infrastructure, Api, unit tests, and integration tests. HTTP is minimal APIs grouped by feature.
- `src/aspire` — Aspire AppHost for local orchestration and the dashboard.
- `src/web` — React, TypeScript, Vite. Features live under `src/web/src/features`. Visible strings are in `src/web/src/shared/i18n/locales/en.json`.
- `infra/terraform` — AWS production Terraform: ECS Fargate (API, web, Temporal worker), Multi-AZ RDS PostgreSQL 16, KMS keys, and S3 Object Lock evidence buckets with an optional `eu-central-1` residency replica. `terraform fmt -check` and `terraform validate` pass; it is not applied by CI. See `infra/terraform/README.md`.
- `docs/` — architecture, features, the roadmap, and the GitHub Pages review site, including the mockups.

## Choices that are not in DECISIONS.md

- .NET 10 is the LTS used here (`global.json`).
- Tenant provisioning runs in-process and records a job. Collection uses Temporal when `TEMPORAL_ADDRESS` is set, and the same runner in-process when it is not. Production and Staging require Temporal.
- The browser session is an httpOnly cookie (`d2d_session`, Lax, path `/`) plus a refresh cookie (Strict, path `/api/v1/auth`). Mutating requests send `X-D2D-Request: 1`. The active tenant is `X-Tenant-Id`.
- Local identity is Development and Testing only, signs tokens with a well-known development key, and does not simulate MFA.
- Database resiliency is configurable: `Database:CommandTimeoutSeconds` (default 30), `Database:MaxRetryCount` (default 3, `0` disables retries), and `Database:TenantMaxPoolSize` (default 20, the Npgsql pool cap for each tenant database). See [docs/DATABASE_ARCHITECTURE.md](docs/DATABASE_ARCHITECTURE.md).
- High contrast is a fourth theme, next to light, dark, and system. System follows `prefers-color-scheme` and `prefers-contrast`.
- The assistant panel is a reserved shell. It does not call a model.
- Evidence is a Parquet object (or the original upload bytes) plus a SHA-256 hash chain. Object Lock is required in Production and Staging. Development can use a local folder or MinIO.
- Example workspaces show fixture integration results. Other workspaces stay empty until someone connects them. Computers in the example workspace come from the MDM fixture plus manual rows. The installed device agent is still later.
- Framework content is JSON embedded in the API and loaded into each tenant database. Descriptions are short paraphrases. ISO/IEC 27001 clauses 4–10 are not seeded; Annex A is.
- An auditor's control mappings are limited to frameworks on the grant.
- The dashboard draws control-health, framework-readiness, and failing-test trends as plain SVG from daily posture snapshots. Each chart has a title, description, and a data-table fallback. Auditors do not see the trend panel.
- The mockup's "Production" label is "Example data" on the example tenants.
- Password reset calls WorkOS and always shows a generic result. The link opens `/reset-password?token=`, which confirms through WorkOS (`/user_management/password_reset/confirm`). Set the WorkOS password reset URL to `<web origin>/reset-password`. In local identity mode the signed 30-minute link is written to the development log instead of sent. New passwords need at least 12 characters.
- TLS terminates at the edge later. Local HTTP is development only.
- The development signing key, the example password, and `d2d_dev_password` are well known. They are refused as a production identity configuration.
- The HTTP edge is ASP.NET Core minimal APIs, one `MapGroup` per feature. Progress is SignalR on `/hubs/jobs`.
- Auditor read-only means customer records. Sample requests, accepting a submitted sample, findings, and the audit package are auditor actions and are allowed for an auditor, an admin, and a control owner. Approval of a policy is admin only.
- Policy template text is original and short. It is not ISO or AICPA wording. The language lives on the template so a later translation does not fork the workflow.
- The audit package runs on the same in-process job channel as local collection and reports five real steps. It is not a Temporal workflow. The PDF is a one-page Helvetica document so the export does not take a PDF library. The integrity report covers the whole chain, including evidence outside the auditor's framework, because skipping a link would report a false break. The zip itself contains only in-scope evidence.
- Average response time is the mean of request creation to customer response. The mockup's comparison with last year is omitted because there is no prior year. The controls-tested card uses the in-scope passing and total counts.
- Local orchestration prefers the Aspire AppHost. Docker Compose remains an alternative and includes MinIO and Temporal.
- Logging is `Microsoft.Extensions.Logging`. Analyzers are the .NET SDK analyzers plus SonarAnalyzer. StyleCop is not enabled. See [docs/ENGINEERING.md](docs/ENGINEERING.md).
- Session cookies are `Secure` outside Development and Testing. In Development, `Secure` follows HTTPS so local HTTP keeps the cookie.
