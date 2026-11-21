# Open-Source Jenkins CI/CD

D2D Audit Services uses **open-source Jenkins** (`Jenkinsfile` at the repository root) as its only CI/CD system. There are no GitHub Actions workflows in `.github/workflows/`, so no Actions minutes can be consumed. Render (`render.yaml`) still auto-deploys the demo when `main` changes.

Two ways to host the controller:

| Host | How | Notes |
| --- | --- | --- |
| **Windows, no Docker** (current dev machine) | `ci/jenkins/windows/start-jenkins.ps1` | Portable JDK 21 + `jenkins.war`, runs builds on the built-in node with `powershell`. |
| **Linux with Docker** | `ci/jenkins/docker-compose.jenkins.yml` | Jenkins + sibling Postgres 16 container, runs builds with `sh`. |

The same `Jenkinsfile` runs on both: every step goes through `runStep()`, which calls `sh` when `isUnix()` and `powershell` otherwise.

## Native Windows controller

PowerShell script execution is disabled by policy on the dev machine, so call the scripts through `powershell -ExecutionPolicy Bypass`:

```powershell
# start (idempotent: downloads, installs plugins, starts in the background, waits for /login)
powershell -NoProfile -ExecutionPolicy Bypass -File ci\jenkins\windows\start-jenkins.ps1

# stop
powershell -NoProfile -ExecutionPolicy Bypass -File ci\jenkins\windows\stop-jenkins.ps1
```

- URL: <http://localhost:8085/> (bound to `127.0.0.1` only). User `admin`.
- Tools directory (outside the repo): `<parent of the main checkout>\tools\jenkins`, for example `C:\Users\<you>\source\repos\D2DAudit\tools\jenkins`. Override with `-ToolsDir` or `$env:D2D_JENKINS_TOOLS`.
  - `jdk\` portable Eclipse Temurin 21, `jenkins.war` (latest LTS), `jenkins-plugin-manager.jar`
  - `home\` is `JENKINS_HOME`; plugins from `ci/jenkins/plugins.txt` are installed into `home\plugins` (re-installed when `plugins.txt` changes)
  - `admin-password.txt` random admin password generated on first start
  - `jenkins.log` controller log, `jenkins.pid` process id, `casc.yaml` copy of `ci/jenkins/casc.windows.yaml`
- Secrets never enter the repository. The GitHub token is read at start-up from Git Credential Manager (`git credential fill`, non-interactive) and handed to Jenkins as an environment variable only; JCasC turns it into the `github-cvpeceee` username/password credential (stored encrypted in `JENKINS_HOME`). The `Jenkinsfile` blanks these start-up variables for every build.
- `TEST_POSTGRES` is set for builds when Postgres answers on `localhost:5432` at start-up (`d2d` / `d2d_dev_password`, the dev role). Otherwise the pipeline runs the integration subset that needs no database.
- Jenkins keeps running after the shell that started it exits. It does not start on boot; run `start-jenkins.ps1` again after a reboot (or register it as a scheduled task at log-on).

### Job

JCasC creates the multibranch job **D2D Audit Services** for the private repo `cvpeceee/D2DAuditServices` (GitHub branch source, credential `github-cvpeceee`). It discovers all branches and same-repo pull requests and is **scanned every 2 minutes**, which is the polling trigger: there is no public webhook endpoint for a localhost Jenkins. Only branches that contain a `Jenkinsfile` get a job. The commit status (success/failure) is reported back to GitHub.

## Docker Compose controller (Linux)

```bash
export JENKINS_ADMIN_USER=admin
export JENKINS_ADMIN_PASSWORD="<choose-a-strong-password>"
export CASC_GITHUB_TOKEN="<GitHub token: Contents read/write, Pages write>"
export CASC_RENDER_DEPLOY_HOOK_URL="<optional Render deploy hook>"
docker compose -f ci/jenkins/docker-compose.jenkins.yml up -d --build
```

The stack starts `postgres:16-alpine` (`jenkins-postgres`) and injects `TEST_POSTGRES`, so the full `D2DAudit.Tests` suite runs without mounting `/var/run/docker.sock`. `ci/jenkins/casc.yaml` creates the same `github-cvpeceee` and `render-deploy-hook-url` credentials from those variables and seeds the same multibranch job.

## Pipeline stages

| Stage | What runs |
| --- | --- |
| **Checkout** | `checkout scm`; on Windows the tree is re-checked-out with `core.autocrlf=false` so Prettier sees LF. |
| **CI / API** | `dotnet build src/api/D2DAudit.slnx -c Release` (warnings are errors), `D2DAudit.UnitTests`, `D2DAudit.Tests` (full suite when `TEST_POSTGRES` is reachable or Docker exists, else the architecture/schema-parity/pipeline-security subset), `dotnet list package --vulnerable --include-transitive`. Test results are written as TRX and JUnit (`JunitXml.TestLogger`). |
| **CI / Web** | `npm ci`, `npm run lint`, `npm run format:check`, Vitest with a JUnit reporter, `tsc --noEmit`, `npm run build`, `npm run build:demo`, `npm audit --audit-level=high` (3 attempts). |
| **CI / Secrets scan** | `gitleaks detect` over the full history with `.gitleaks.toml` and `.gitleaksignore` (binary cached under `JENKINS_HOME/ci-tools`, Windows or Linux build). |
| **Package review site** | `docs/` + the demo build in `app/` (with `404.html` and `.nojekyll`) archived as `d2d-review-site.tar.gz`. |
| **CD: Render deploy** (`main`) | Posts the optional `render-deploy-hook-url` secret (value `unset` = skip; Render auto-deploys on push anyway), then polls <https://d2d-audit-web.onrender.com> until HTTP 200. |
| **CD: GitHub Pages** (`main`) | Force-pushes the review site to the `gh-pages` branch and sets the repo's Pages source to `gh-pages` `/` (`build_type: legacy`) through the GitHub REST API. No Actions involved. |
| **CD: Publish tutorials** (`main`) | Runs `scripts/tutorial/publish.ps1` with `GITHUB_TOKEN` when that file exists. |

Post actions always publish JUnit results, archive TRX/JUnit files, the NuGet audit output and the review-site tarball, then wipe the workspace. CD stages are skipped on other branches and pull requests, and can be switched off with the `DEPLOY` build parameter.

## Manual steps

- **Render deploy hook (optional):** put the hook URL in `<tools>\jenkins\render-deploy-hook-url.txt` (or `$env:RENDER_DEPLOY_HOOK_URL`) and restart Jenkins. JCasC owns the credential list, so credentials added in the UI are replaced on the next restart.
- **Instant builds (optional):** a localhost Jenkins cannot receive GitHub webhooks. Expose `http://localhost:8085/github-webhook/` through a tunnel such as smee.io or ngrok and add it as a repository webhook (content type `application/json`, push + pull request events). Polling every 2 minutes works without it.
