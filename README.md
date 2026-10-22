# D2D Audit Services

D2D Audit Services is a compliance automation platform for companies undergoing audit, in the same category as Vanta and Drata. The first release covers IT frameworks, starting with ISO 27001 and SOC 2, with manufacturing and IoT/OT frameworks planned later on the same core. Locked decisions are in [DECISIONS.md](DECISIONS.md). The milestone plan is in [docs/ROADMAP.md](docs/ROADMAP.md). Engineering rules are in [docs/ENGINEERING.md](docs/ENGINEERING.md).

A public clickable demo of the example workspace is at [https://cvpeceee.github.io/D2DAuditServices/app/](https://cvpeceee.github.io/D2DAuditServices/app/). It runs in the browser with example data only. Sign in as `chandra.vell@harborline.example` or `auditor@northgate.example`. The example password is on the login screen.

The review site lives on [GitHub Pages](https://cvpeceee.github.io/D2DAuditServices/), holds example data only, and is public. The approved look is [docs/mockups/index.html](docs/mockups/index.html).

Milestone 1 is the foundation slice. Milestone 2 adds integrations, automated tests, and evidence on top of it.

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

## Tests

```bash
dotnet test src/api/D2DAudit.slnx
cd src/web && npm test && npm run lint && npm run format:check && npm run build
```

Integration tests start Postgres with Testcontainers. Set `TEST_POSTGRES` to an existing admin connection when Docker is not available, for example `Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres`. The role needs `CREATEDB`. Unit tests do not need a database.

## Layout

- `src/api` — C# solution. Domain, Application, Infrastructure, Api, unit tests, and integration tests. HTTP is minimal APIs grouped by feature.
- `src/aspire` — Aspire AppHost for local orchestration and the dashboard.
- `src/web` — React, TypeScript, Vite. Features live under `src/web/src/features`. Visible strings are in `src/web/src/shared/i18n/locales/en.json`.
- `infra/terraform` — placeholder. Nothing is applied.
- `docs/` — the GitHub Pages review site, including the mockups.

## Choices that are not in DECISIONS.md

- .NET 10 is the LTS used here (`global.json`).
- Tenant provisioning runs in-process and records a job. Collection uses Temporal when `TEMPORAL_ADDRESS` is set, and the same runner in-process when it is not. Production and Staging require Temporal.
- The browser session is an httpOnly cookie (`d2d_session`, Lax, path `/`) plus a refresh cookie (Strict, path `/api/v1/auth`). Mutating requests send `X-D2D-Request: 1`. The active tenant is `X-Tenant-Id`.
- Local identity is Development and Testing only, signs tokens with a well-known development key, and does not simulate MFA.
- High contrast is a fourth theme, next to light, dark, and system. System follows `prefers-color-scheme` and `prefers-contrast`.
- The assistant panel is a reserved shell. It does not call a model.
- Evidence is a Parquet object (or the original upload bytes) plus a SHA-256 hash chain. Object Lock is required in Production and Staging. Development can use a local folder or MinIO.
- Example workspaces show fixture integration results. Other workspaces stay empty until someone connects them. The device agent is still empty.
- Framework content is JSON embedded in the API and loaded into each tenant database. Descriptions are short paraphrases. ISO/IEC 27001 clauses 4–10 are not seeded; Annex A is.
- An auditor's control mappings are limited to frameworks on the grant.
- The control-health chart from the mockup is not drawn. The dashboard shows current counts.
- The mockup's "Production" label is "Example data" on the example tenants.
- Password reset calls WorkOS and always shows a generic result. The confirm-reset page is not in this milestone.
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
