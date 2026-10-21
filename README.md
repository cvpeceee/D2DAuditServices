# D2D Audit Services

D2D Audit Services is a compliance automation platform for companies undergoing audit, in the same category as Vanta and Drata. The first release covers IT frameworks, starting with ISO 27001 and SOC 2, with manufacturing and IoT/OT frameworks planned later on the same core. Locked decisions are in [DECISIONS.md](DECISIONS.md). The milestone plan is in [docs/ROADMAP.md](docs/ROADMAP.md).

The review site lives on [GitHub Pages](https://cvpeceee.github.io/D2DAuditServices/), holds example data only, and is public. The approved look is [docs/mockups/index.html](docs/mockups/index.html).

Milestone 1 is a local vertical slice: a .NET API, a React app, a control-plane database, one database per tenant, and WorkOS behind `IIdentityProvider`.

## Run locally

One command, from the repository root, when Docker is installed:

```bash
docker compose up --build
```

- API: http://localhost:8080
- Web (nginx): http://localhost:8088
- Postgres: localhost:5432, database `d2d_control`, user `d2d`, password `d2d_dev_password`

`./scripts/dev.sh` runs that Compose command. If Docker is not installed, the script starts the API and Vite against a Postgres already listening on localhost, and the web app is http://localhost:5173.

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

On first start in Development, the API migrates the control-plane database, creates the example tenants, and loads SOC 2 and ISO 27001 into each tenant database. That provisioning job is real work; the jobs tray shows its step and percent.

### Example workspace

Local identity mode only. Password for every example account: `Example-Only-1!`

| Email | Workspace | Role |
| --- | --- | --- |
| chandra@acme.example | Acme Robotics (US) | Admin |
| priya@acme.example, marcus@acme.example, lena@acme.example | Acme Robotics | Control owner |
| dev@acme.example | Acme Robotics | Employee, read-only |
| auditor@northgate.example | Acme Robotics | Auditor, read-only, SOC 2 only, through 2026-12-15 |
| owner@northwind.example | Northwind Labs (EU) | Admin |

Acme and Northwind are separate databases. A user of one cannot read the other. The auditor does not see ISO-only controls.

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
4. For company SSO, create an organization and set `WORKOS_ORGANIZATION_ID` and `WORKOS_SSO_DOMAIN` before the demo seed runs. The seed writes that organization id onto the Acme domain row. A domain with no organization returns `sso_not_configured`.
5. Optional: set a JWT template audience and `WORKOS_TOKEN_AUDIENCE` to the same value. Access tokens must be RS256 from `https://api.workos.com/sso/jwks/{clientId}`, issuer `https://api.workos.com`, and the `client_id` claim must match `WORKOS_CLIENT_ID`.

Leave both variables unset and the API uses local identity mode. That mode exists only when `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing`. Production and Staging refuse to start without WorkOS. Local mode does not simulate Google, Microsoft, SSO, or MFA.

There is no public signup endpoint. Tenants are created by the provisioner (the demo seed, and later an operator path).

## Tests

```bash
dotnet test src/api/D2DAudit.slnx
cd src/web && npm run lint && npm run build
```

API tests need Postgres. `TEST_POSTGRES` overrides the admin connection. The default is `Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres`. The role needs `CREATEDB`.

## Layout

- `src/api` — C# solution. Domain, Application, Infrastructure, Api, and tests.
- `src/web` — React, TypeScript, Vite. All visible strings are in `src/web/src/i18n/locales/en.json`.
- `infra/terraform` — placeholder. Nothing is applied.
- `docs/` — the GitHub Pages review site, including the mockups.

## Choices that are not in DECISIONS.md

- .NET 10 is the LTS used here (`global.json`).
- Tenant provisioning runs in-process and records a job (queued, running with a step and a percent, succeeded, or failed). Temporal is later.
- The browser session is an httpOnly cookie (`d2d_session`, Lax, path `/`) plus a refresh cookie (Strict, path `/api/v1/auth`). Mutating requests send `X-D2D-Request: 1`. The active tenant is `X-Tenant-Id`.
- Local identity is Development and Testing only, signs tokens with a well-known development key, and does not simulate MFA.
- High contrast is a fourth theme, next to light, dark, and system. System follows `prefers-color-scheme` and `prefers-contrast`.
- The assistant panel is a reserved shell. It does not call a model.
- The evidence table exists. Integrity is `notStored`. No hashes and no object storage until M2.
- Integration health and the device agent are empty states. They do not show invented connection data.
- Framework content is JSON embedded in the API and loaded into each tenant database. Descriptions are short paraphrases. ISO/IEC 27001 clauses 4–10 are not seeded; Annex A is.
- An auditor's control mappings are limited to frameworks on the grant.
- The control-health chart from the mockup is not drawn. The dashboard shows current counts.
- The mockup's "Production" label is "Example data" on the example tenants.
- Password reset calls WorkOS and always shows a generic result. The confirm-reset page is not in this milestone.
- TLS terminates at the edge later. Local HTTP is development only.
- The development signing key, the example password, and `d2d_dev_password` are well known. They are refused as a production identity configuration.
