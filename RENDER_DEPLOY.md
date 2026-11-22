# Deploying D2D Audit Services Super App on Render.com

D2D Audit Services (`d2d-audit-superapp`) ships with a root [`render.yaml`](../render.yaml) Render Blueprint supporting two deployment profiles from [`https://github.com/cvpeceee/D2DAuditServices`](https://github.com/cvpeceee/D2DAuditServices):

1. **Profile A — Zero-Friction Interactive Super App & Tutorial (Default in `render.yaml`)**: Deploys the complete interactive Harborline workspace, all 13 compliance frameworks, Governance Hub (`/governance-hub`), Cloud Security Intelligence, Trust Center v2 (`/trust/harborline`), and the Interactive Animation + Video/Audio Tutorial (`/tutorial`) as a globally cached Render Static SPA at `https://<service>.onrender.com/` with zero external infrastructure dependencies.
2. **Profile B — Full-Stack Docker API + Render PostgreSQL 16**: Runs the .NET 10 API (`src/api/Dockerfile`), control-plane + tenant databases on Render PostgreSQL 16, S3 Object Lock evidence storage, and the React web application.

---

## Profile A: 1-Click Zero-Friction Super App Blueprint (`render.yaml`)

### How It Works

The root [`render.yaml`](../render.yaml) configures the `d2d-audit-web` static service:

- **Repository**: `https://github.com/cvpeceee/D2DAuditServices` (`main` branch)
- **Root Directory**: `src/web`
- **Build Command**: `npm ci && npx vite build --mode demo --base=/`
  - Builds the SPA in self-contained Harborline demo mode while overriding Vite's base path to `/` so all assets, routes, and `/tutorial/d2d-super-app-tutorial.mp4` resolve cleanly at the root Render domain (`https://<service>.onrender.com/`).
- **Publish Directory**: `dist`
- **SPA Routing**: Rewrites `/* -> /index.html` so deep links (`/frameworks`, `/controls`, `/exposure`, `/attack-paths`, `/dspm`, `/finops`, `/governance-hub`, `/tutorial`, `/trust/harborline`) load directly on browser refresh.
- **Security Headers**:
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy: no-referrer`
  - `X-Frame-Options: DENY`
  - `Permissions-Policy: camera=(), microphone=(), geolocation=()`

### Steps to Deploy on Render

1. Sign in to [Render Dashboard](https://dashboard.render.com/).
2. Click **New +** -> **Blueprint**.
3. Connect the GitHub repository `https://github.com/cvpeceee/D2DAuditServices`.
4. Render automatically detects `render.yaml` at the repository root and provisions `d2d-audit-web`.
5. Click **Apply**. Within ~90 seconds your live instance is available at `https://d2d-audit-web.onrender.com/`:
   - **Dashboard & Command Center**: `/`
   - **Interactive Video+Audio & Live Canvas Tutorial**: `/tutorial`
   - **Unified Governance Hub (Multi-Entity, RoPA/DPIA, AI Registry, Checkr/KnowBe4/SCIM, ASM, Audit Time Machine)**: `/governance-hub`
   - **Public Buyer Trust Center**: `/trust/harborline`

---

## Profile B: Full-Stack Docker API + Render PostgreSQL 16

When deploying a live multi-tenant control plane backed by Render PostgreSQL 16 and `.NET 10` (`src/api/Dockerfile`), use the following multi-service Render Blueprint configuration:

```yaml
databases:
  - name: d2d-control-db
    databaseName: d2d_control
    user: d2d
    plan: standard
    postgresMajorVersion: "16"

services:
  - type: web
    name: d2d-audit-api
    runtime: docker
    repo: https://github.com/cvpeceee/D2DAuditServices
    branch: main
    dockerfilePath: ./src/api/Dockerfile
    dockerContext: .
    plan: standard
    healthCheckPath: /health
    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: ASPNETCORE_URLS
        value: http://+:8080
      - key: ConnectionStrings__ControlPlane
        fromDatabase:
          name: d2d-control-db
          property: connectionString
      - key: Storage__Bucket
        value: d2d-evidence-prod
      - key: Storage__Region
        value: us-east-1
      - key: Storage__ObjectLock
        value: "true"
      - key: Storage__ForcePathStyle
        value: "false"
      - key: Storage__Endpoint
        sync: false
      - key: Storage__AccessKey
        sync: false
      - key: Storage__SecretKey
        sync: false
      - key: TEMPORAL_ADDRESS
        sync: false
      - key: TENANT_DATA_KEY
        generateValue: true
      - key: WORKOS_API_KEY
        sync: false
      - key: WORKOS_CLIENT_ID
        sync: false

  - type: web
    name: d2d-audit-web
    runtime: docker
    repo: https://github.com/cvpeceee/D2DAuditServices
    branch: main
    dockerfilePath: ./src/web/Dockerfile
    dockerContext: .
    plan: starter
    envVars:
      - key: D2D_API_PROXY
        fromService:
          type: web
          name: d2d-audit-api
          property: hostport
```

### Production Readiness Checklist for Full-Stack Profile

| Requirement | Environment Variable | Notes |
| --- | --- | --- |
| PostgreSQL 16 Control Plane | `ConnectionStrings__ControlPlane` | Automatically injected by Render PostgreSQL (`fromDatabase`). |
| S3 Object Lock Evidence Bucket | `Storage__Bucket`, `Storage__ObjectLock=true` | Provision via [`infra/terraform/`](../infra/terraform/README.md) with KMS encryption and WORM Object Lock (`>= 3` years; `6` years for HIPAA). |
| Tenant Envelope Encryption Key | `TENANT_DATA_KEY` | 32-byte base64 key used to encrypt provider credentials and refresh tokens at rest. |
| Temporal Workflow Engine | `TEMPORAL_ADDRESS` | Required in `Production`/`Staging` for scheduled evidence collection runs. |
| WorkOS SSO & Directory Sync | `WORKOS_API_KEY`, `WORKOS_CLIENT_ID` | Required outside `Development`/`Testing` when `IIdentityProvider` runs in production mode. |
