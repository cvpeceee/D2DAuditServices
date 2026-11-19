# Self-Hosted Open-Source Jenkins CI/CD

D2D Audit Services uses **open-source Jenkins** (`Jenkinsfile` at the repository root) instead of GitHub Actions so CI builds, tests, security scans, and review-site packaging never consume GitHub Actions minutes.

Automatic triggers (`push`, `pull_request`) in `.github/workflows/ci.yml` and `.github/workflows/pages.yml` have been disabled (`workflow_dispatch` only).

## Quickstart (3 steps)

1. Set a strong admin password and start Jenkins + its sibling PostgreSQL 16 service with Docker Compose:

```bash
export JENKINS_ADMIN_USER=admin
export JENKINS_ADMIN_PASSWORD="<choose-a-strong-password>"
docker compose -f ci/jenkins/docker-compose.jenkins.yml up -d --build
```

The Compose stack starts a dedicated `postgres:16-alpine` container (`jenkins-postgres`) and injects `TEST_POSTGRES` into Jenkins so the full `D2DAudit.Tests` PostgreSQL integration suite runs without exposing `/var/run/docker.sock` to the Jenkins container.

2. Open `http://localhost:8085/` and sign in. Jenkins Configuration as Code (`ci/jenkins/casc.yaml`) automatically seeds the **D2D Audit Services** multibranch pipeline wired to `Jenkinsfile` (with `pipeline-graph-view` for stage visualization).

3. Click **Scan Multibranch Pipeline Now** (or add a GitHub webhook pointing to `https://<your-jenkins-host>/github-webhook/`).

## What `Jenkinsfile` runs

| Stage | Checks |
| --- | --- |
| **API Build, Test & NuGet Audit** | `dotnet build --configuration Release`, `D2DAudit.UnitTests`, `D2DAudit.Tests` (against `TEST_POSTGRES`, or the architecture + EF schema parity + HTTP security pipeline subset when `RUN_INTEGRATION_TESTS=false`), and `dotnet list ... package --vulnerable --include-transitive`. |
| **Web Lint, Format, Test, Build & Audit** | `npm ci`, `npm run lint`, `npm run format:check`, `npm test`, `npm run build`, `npm run build:demo`, and `npm audit --audit-level=high`. |
| **Secrets Scan** | `gitleaks detect --source . --config .gitleaks.toml --redact --verbose` (supports both `amd64` and `arm64` hosts). |
| **Package Review Site & Live Demo** | Combines `docs/` and `src/web/dist/` (`VITE_DEMO=true`) into `d2d-review-site.tar.gz` archived on every build. |
| **Publish `gh-pages` (Optional)** | When `DEPLOY_GH_PAGES=true` on `main`, pushes the built static site directly to the `gh-pages` branch using a Jenkins Username/Password credential named `github-pages-token` (GitHub username + fine-grained PAT with `Contents: Read and Write`) passed via an HTTP `Authorization` header (never embedded in git URLs). Configure GitHub Pages source to **Deploy from a branch (`gh-pages` / `/root`)** so publishing consumes zero GitHub Actions minutes. |
