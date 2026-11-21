// Open-source Jenkins Declarative Pipeline for D2D Audit Services (the only CI/CD system; no GitHub Actions).
// Cross-platform: every step runs through runStep(), which uses `sh` on Linux agents (Docker Compose setup in
// ci/jenkins/) and `powershell` on the native Windows controller (ci/jenkins/windows/start-jenkins.ps1).
// See ci/jenkins/README.md.

String psPrelude() {
    return '''
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Exec {
    param([scriptblock]$Command)
    $ErrorActionPreference = 'Continue'
    & $Command 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $Command" }
}
'''
}

void runStep(String label, String unixScript, String windowsScript) {
    if (isUnix()) {
        sh(label: label, script: '#!/usr/bin/env bash\nset -euo pipefail\n' + unixScript)
    } else {
        powershell(label: label, script: psPrelude() + windowsScript)
    }
}

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        skipDefaultCheckout()
        buildDiscarder(logRotator(numToKeepStr: '20', artifactNumToKeepStr: '10'))
        timeout(time: 60, unit: 'MINUTES')
    }

    parameters {
        booleanParam(
            name: 'RUN_INTEGRATION_TESTS',
            defaultValue: true,
            description: 'Run the full D2DAudit.Tests suite when TEST_POSTGRES is reachable (or Docker is available on Linux); otherwise only the non-database subset runs.'
        )
        booleanParam(
            name: 'DEPLOY',
            defaultValue: true,
            description: 'On main only: run the CD stages (Render deploy check, GitHub Pages gh-pages publish, tutorial publish).'
        )
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        CI                          = 'true'
        // Start-up secrets of the Windows controller must never leak into build steps (empty = removed).
        CASC_GITHUB_TOKEN           = ''
        CASC_RENDER_DEPLOY_HOOK_URL = ''
        JENKINS_ADMIN_PASSWORD      = ''
        GITHUB_REPOSITORY           = 'cvpeceee/D2DAuditServices'
        RENDER_SITE_URL             = 'https://d2d-audit-web.onrender.com'
        GITLEAKS_VERSION            = '8.28.0'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    if (!isUnix()) {
                        // Git for Windows defaults to core.autocrlf=true; the repo (and Prettier) expects LF.
                        powershell(label: 'Normalize line endings (LF)', script: psPrelude() + '''
                            Exec { git config core.autocrlf false }
                            Exec { git rm -r -q --cached . }
                            Exec { git reset -q --hard HEAD }
                        ''')
                    }
                }
                runStep('Show toolchain', '''
                    git log -1 --oneline
                    dotnet --version
                    node --version
                    npm --version
                    rm -rf test-results build
                    mkdir -p test-results
                ''', '''
                    Exec { git log -1 --oneline }
                    Exec { dotnet --version }
                    Exec { node --version }
                    Exec { npm.cmd --version }
                    Remove-Item -Recurse -Force test-results, build -ErrorAction SilentlyContinue
                    New-Item -ItemType Directory -Force test-results | Out-Null
                ''')
            }
        }

        stage('CI') {
            parallel {
                stage('API') {
                    stages {
                        stage('API build (Release)') {
                            steps {
                                // TreatWarningsAsErrors is enabled in src/api/Directory.Build.props.
                                runStep('dotnet build', '''
                                    dotnet build src/api/D2DAudit.slnx --configuration Release
                                ''', '''
                                    Exec { dotnet build src/api/D2DAudit.slnx --configuration Release }
                                ''')
                            }
                        }
                        stage('API unit tests') {
                            steps {
                                runStep('Unit tests', '''
                                    dotnet test src/api/tests/D2DAudit.UnitTests/D2DAudit.UnitTests.csproj \
                                        --configuration Release --no-build --results-directory test-results \
                                        --logger "trx;LogFileName=unit-tests.trx" \
                                        --logger "junit;LogFilePath=$WORKSPACE/test-results/unit-tests.xml"
                                ''', '''
                                    Exec { dotnet test src/api/tests/D2DAudit.UnitTests/D2DAudit.UnitTests.csproj `
                                        --configuration Release --no-build --results-directory test-results `
                                        --logger "trx;LogFileName=unit-tests.trx" `
                                        --logger "junit;LogFilePath=$env:WORKSPACE\\test-results\\unit-tests.xml" }
                                ''')
                            }
                        }
                        stage('API integration tests') {
                            steps {
                                // Full suite when Postgres is reachable; otherwise the subset that needs no database.
                                runStep('Integration tests', '''
                                    FILTER=""
                                    if [ "${RUN_INTEGRATION_TESTS}" != "true" ]; then
                                        FILTER="FullyQualifiedName~ArchitectureTests|FullyQualifiedName~SchemaParityTests|FullyQualifiedName~CloudInventoryTests|FullyQualifiedName~Framework_content_matches_the_public_structure|FullyQualifiedName~ApiPipelineSecurityTests"
                                    elif [ -z "${TEST_POSTGRES:-}" ] && ! docker info >/dev/null 2>&1; then
                                        echo "No TEST_POSTGRES and no Docker: running the non-database subset."
                                        FILTER="FullyQualifiedName~ArchitectureTests|FullyQualifiedName~SchemaParityTests|FullyQualifiedName~CloudInventoryTests|FullyQualifiedName~Framework_content_matches_the_public_structure|FullyQualifiedName~ApiPipelineSecurityTests"
                                    fi
                                    ARGS=(src/api/tests/D2DAudit.Tests/D2DAudit.Tests.csproj --configuration Release --no-build
                                        --results-directory test-results --logger "trx;LogFileName=integration-tests.trx"
                                        --logger "junit;LogFilePath=$WORKSPACE/test-results/integration-tests.xml")
                                    if [ -n "$FILTER" ]; then ARGS+=(--filter "$FILTER"); fi
                                    dotnet test "${ARGS[@]}"
                                ''', '''
                                    $subset = 'FullyQualifiedName~ArchitectureTests|FullyQualifiedName~SchemaParityTests|FullyQualifiedName~CloudInventoryTests|FullyQualifiedName~Framework_content_matches_the_public_structure|FullyQualifiedName~ApiPipelineSecurityTests'
                                    $filter = $null
                                    if ($env:RUN_INTEGRATION_TESTS -ne 'true') {
                                        $filter = $subset
                                    } elseif (-not $env:TEST_POSTGRES) {
                                        Write-Output 'TEST_POSTGRES is not set: running the non-database subset.'
                                        $filter = $subset
                                    } else {
                                        $hostName = 'localhost'; $port = 5432
                                        foreach ($part in $env:TEST_POSTGRES.Split(';')) {
                                            $kv = $part.Split('=', 2)
                                            if ($kv.Length -eq 2 -and $kv[0].Trim() -eq 'Host') { $hostName = $kv[1].Trim() }
                                            if ($kv.Length -eq 2 -and $kv[0].Trim() -eq 'Port') { $port = [int]$kv[1].Trim() }
                                        }
                                        $client = New-Object Net.Sockets.TcpClient
                                        try { $reachable = $client.ConnectAsync($hostName, $port).Wait(3000) } catch { $reachable = $false } finally { $client.Dispose() }
                                        if ($reachable) {
                                            Write-Output "Postgres reachable at ${hostName}:${port}: running the full integration suite."
                                        } else {
                                            Write-Output "Postgres at ${hostName}:${port} is not reachable: running the non-database subset."
                                            $filter = $subset
                                        }
                                    }
                                    $testArgs = @('test', 'src/api/tests/D2DAudit.Tests/D2DAudit.Tests.csproj', '--configuration', 'Release', '--no-build',
                                        '--results-directory', 'test-results', '--logger', 'trx;LogFileName=integration-tests.trx',
                                        '--logger', "junit;LogFilePath=$env:WORKSPACE\\test-results\\integration-tests.xml")
                                    if ($filter) { $testArgs += @('--filter', $filter) }
                                    Exec { dotnet @testArgs }
                                ''')
                            }
                        }
                        stage('NuGet vulnerability audit') {
                            steps {
                                runStep('dotnet list package --vulnerable', '''
                                    dotnet list src/api/D2DAudit.slnx package --vulnerable --include-transitive | tee nuget-vulnerabilities.txt
                                    if grep -q "has the following vulnerable packages" nuget-vulnerabilities.txt; then
                                        echo "Vulnerable NuGet packages were reported."
                                        exit 1
                                    fi
                                ''', '''
                                    Exec { dotnet list src/api/D2DAudit.slnx package --vulnerable --include-transitive } | Tee-Object -FilePath nuget-vulnerabilities.txt
                                    if (Select-String -Path nuget-vulnerabilities.txt -Pattern 'has the following vulnerable packages' -Quiet) {
                                        throw 'Vulnerable NuGet packages were reported.'
                                    }
                                ''')
                            }
                        }
                    }
                }

                stage('Web') {
                    stages {
                        stage('Web install') {
                            steps {
                                dir('src/web') {
                                    runStep('npm ci', 'npm ci --no-fund --no-audit', 'Exec { npm.cmd ci --no-fund --no-audit }')
                                }
                            }
                        }
                        stage('Web lint & format') {
                            steps {
                                dir('src/web') {
                                    runStep('lint + format:check', '''
                                        npm run lint
                                        npm run format:check
                                    ''', '''
                                        Exec { npm.cmd run lint }
                                        Exec { npm.cmd run format:check }
                                    ''')
                                }
                            }
                        }
                        stage('Web tests') {
                            steps {
                                dir('src/web') {
                                    runStep('vitest', '''
                                        npx vitest run --reporter=default --reporter=junit --outputFile.junit="$WORKSPACE/test-results/web-tests.xml"
                                    ''', '''
                                        Exec { npx.cmd vitest run --reporter=default --reporter=junit "--outputFile.junit=$env:WORKSPACE\\test-results\\web-tests.xml" }
                                    ''')
                                }
                            }
                        }
                        stage('Web typecheck & build') {
                            steps {
                                dir('src/web') {
                                    // build:demo runs last so dist/ holds the demo build used by the review site.
                                    runStep('tsc + build + build:demo', '''
                                        npx tsc --noEmit
                                        npm run build
                                        npm run build:demo
                                    ''', '''
                                        Exec { npx.cmd tsc --noEmit }
                                        Exec { npm.cmd run build }
                                        Exec { npm.cmd run build:demo }
                                    ''')
                                }
                            }
                        }
                        stage('npm audit (high)') {
                            steps {
                                dir('src/web') {
                                    runStep('npm audit', '''
                                        for attempt in 1 2 3; do
                                            npm audit --audit-level=high && exit 0
                                            echo "npm audit attempt ${attempt} failed"
                                            sleep $((attempt * 15))
                                        done
                                        exit 1
                                    ''', '''
                                        for ($attempt = 1; $attempt -le 3; $attempt++) {
                                            try { Exec { npm.cmd audit --audit-level=high }; return } catch { Write-Output "npm audit attempt $attempt failed: $_" }
                                            Start-Sleep -Seconds (15 * $attempt)
                                        }
                                        throw 'npm audit reported high or critical vulnerabilities.'
                                    ''')
                                }
                            }
                        }
                    }
                }

                stage('Secrets scan (gitleaks)') {
                    steps {
                        runStep('gitleaks', '''
                            if command -v gitleaks >/dev/null 2>&1; then
                                GITLEAKS_BIN=$(command -v gitleaks)
                            else
                                case "$(uname -m)" in
                                    x86_64|amd64) GL_ARCH=x64 ;;
                                    aarch64|arm64) GL_ARCH=arm64 ;;
                                    *) echo "Unsupported architecture: $(uname -m)"; exit 1 ;;
                                esac
                                GL_DIR="${JENKINS_HOME:-$HOME}/ci-tools/gitleaks-${GITLEAKS_VERSION}"
                                if [ ! -x "$GL_DIR/gitleaks" ]; then
                                    mkdir -p "$GL_DIR"
                                    curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_${GL_ARCH}.tar.gz" | tar -xz -C "$GL_DIR" gitleaks
                                fi
                                GITLEAKS_BIN="$GL_DIR/gitleaks"
                            fi
                            "$GITLEAKS_BIN" detect --source . --config .gitleaks.toml --redact --verbose \
                                --report-format junit --report-path test-results/gitleaks.xml
                        ''', '''
                            $gl = Get-Command gitleaks.exe -ErrorAction SilentlyContinue
                            if ($gl) {
                                $bin = $gl.Source
                            } else {
                                $dir = Join-Path $env:JENKINS_HOME "ci-tools\\gitleaks-$env:GITLEAKS_VERSION"
                                $bin = Join-Path $dir 'gitleaks.exe'
                                if (-not (Test-Path $bin)) {
                                    New-Item -ItemType Directory -Force $dir | Out-Null
                                    $zip = Join-Path $dir 'gitleaks.zip'
                                    Invoke-WebRequest -UseBasicParsing -OutFile $zip `
                                        -Uri "https://github.com/gitleaks/gitleaks/releases/download/v$env:GITLEAKS_VERSION/gitleaks_$($env:GITLEAKS_VERSION)_windows_x64.zip"
                                    Expand-Archive -Path $zip -DestinationPath $dir -Force
                                    Remove-Item $zip
                                }
                            }
                            Exec { & $bin detect --source . --config .gitleaks.toml --redact --verbose --report-format junit --report-path test-results\\gitleaks.xml }
                        ''')
                    }
                }
            }
        }

        stage('Package review site') {
            steps {
                runStep('docs + demo -> d2d-review-site.tar.gz', '''
                    rm -rf build/pages
                    mkdir -p build/pages/app
                    cp -a docs/. build/pages/
                    rm -rf build/pages/app
                    mkdir -p build/pages/app
                    cp -a src/web/dist/. build/pages/app/
                    cp build/pages/app/index.html build/pages/app/404.html
                    touch build/pages/.nojekyll
                    tar -czf d2d-review-site.tar.gz -C build/pages .
                ''', '''
                    Remove-Item -Recurse -Force build\\pages -ErrorAction SilentlyContinue
                    New-Item -ItemType Directory -Force build\\pages | Out-Null
                    Copy-Item -Recurse -Force docs\\* build\\pages\\
                    Remove-Item -Recurse -Force build\\pages\\app -ErrorAction SilentlyContinue
                    New-Item -ItemType Directory -Force build\\pages\\app | Out-Null
                    Copy-Item -Recurse -Force src\\web\\dist\\* build\\pages\\app\\
                    Copy-Item build\\pages\\app\\index.html build\\pages\\app\\404.html
                    New-Item -ItemType File -Force build\\pages\\.nojekyll | Out-Null
                    Exec { tar.exe -czf d2d-review-site.tar.gz -C build/pages . }
                ''')
            }
        }

        stage('CD: Render deploy') {
            when {
                allOf {
                    branch 'main'
                    expression { return params.DEPLOY }
                }
            }
            steps {
                script {
                    // Render auto-deploys pushes to main (render.yaml). The optional deploy hook forces a deploy.
                    try {
                        withCredentials([string(credentialsId: 'render-deploy-hook-url', variable: 'RENDER_DEPLOY_HOOK_URL')]) {
                            runStep('Trigger Render deploy hook', '''
                                if [ "${RENDER_DEPLOY_HOOK_URL}" != "unset" ] && [ -n "${RENDER_DEPLOY_HOOK_URL}" ]; then
                                    curl -sSf -X POST "${RENDER_DEPLOY_HOOK_URL}" >/dev/null && echo "Render deploy hook triggered."
                                else
                                    echo "No Render deploy hook configured; relying on Render auto-deploy."
                                fi
                            ''', '''
                                if ($env:RENDER_DEPLOY_HOOK_URL -and $env:RENDER_DEPLOY_HOOK_URL -ne 'unset') {
                                    Invoke-RestMethod -Method Post -Uri $env:RENDER_DEPLOY_HOOK_URL | Out-Null
                                    Write-Output 'Render deploy hook triggered.'
                                } else {
                                    Write-Output 'No Render deploy hook configured; relying on Render auto-deploy.'
                                }
                            ''')
                        }
                    } catch (org.jenkinsci.plugins.credentialsbinding.impl.CredentialNotFoundException e) {
                        echo 'Credential render-deploy-hook-url not defined; relying on Render auto-deploy.'
                    }
                }
                runStep('Wait for Render site', '''
                    for attempt in $(seq 1 60); do
                        code=$(curl -s -o /dev/null -w "%{http_code}" "$RENDER_SITE_URL" || true)
                        if [ "$code" = "200" ]; then echo "Render site is up (HTTP 200)."; exit 0; fi
                        echo "Attempt $attempt: HTTP $code"; sleep 15
                    done
                    exit 1
                ''', '''
                    for ($attempt = 1; $attempt -le 60; $attempt++) {
                        try {
                            $r = Invoke-WebRequest -UseBasicParsing -Uri $env:RENDER_SITE_URL -TimeoutSec 30
                            if ($r.StatusCode -eq 200) { Write-Output 'Render site is up (HTTP 200).'; return }
                            Write-Output "Attempt ${attempt}: HTTP $($r.StatusCode)"
                        } catch { Write-Output "Attempt ${attempt}: $($_.Exception.Message)" }
                        Start-Sleep -Seconds 15
                    }
                    throw "Render site $env:RENDER_SITE_URL did not return HTTP 200."
                ''')
            }
        }

        stage('CD: GitHub Pages (gh-pages)') {
            when {
                allOf {
                    branch 'main'
                    expression { return params.DEPLOY }
                }
            }
            steps {
                withCredentials([usernamePassword(credentialsId: 'github-cvpeceee', usernameVariable: 'GH_USER', passwordVariable: 'GH_TOKEN')]) {
                    runStep('Force-push gh-pages + set Pages source', '''
                        AUTH_B64=$(printf '%s:%s' "$GH_USER" "$GH_TOKEN" | base64 | tr -d '\\n')
                        COMMIT=$(git rev-parse --short HEAD)
                        rm -rf build/gh-pages && mkdir -p build/gh-pages
                        cp -a build/pages/. build/gh-pages/
                        cd build/gh-pages
                        git init -q -b gh-pages
                        git config user.name "Jenkins CI"
                        git config user.email "jenkins@d2daudit.local"
                        git add -A
                        git commit -q -m "Deploy review site & live demo from ${COMMIT}"
                        git -c credential.helper= -c "http.extraHeader=Authorization: Basic ${AUTH_B64}" \
                            push --force "https://github.com/${GITHUB_REPOSITORY}.git" gh-pages
                        cd ../..
                        API="https://api.github.com/repos/${GITHUB_REPOSITORY}/pages"
                        BODY='{"build_type":"legacy","source":{"branch":"gh-pages","path":"/"}}'
                        HDR=(-H "Authorization: Bearer ${GH_TOKEN}" -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28")
                        status=$(curl -s -o /dev/null -w "%{http_code}" "${HDR[@]}" "$API")
                        if [ "$status" = "404" ]; then
                            curl -sSf -X POST "${HDR[@]}" "$API" -d "$BODY" >/dev/null
                        else
                            curl -sSf -X PUT "${HDR[@]}" "$API" -d "$BODY" >/dev/null
                        fi
                        curl -sSf "${HDR[@]}" "$API" | grep -E '"html_url"|"branch"|"build_type"' || true
                    ''', '''
                        $auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($env:GH_USER):$($env:GH_TOKEN)"))
                        $commit = (git rev-parse --short HEAD).Trim()
                        Remove-Item -Recurse -Force build\\gh-pages -ErrorAction SilentlyContinue
                        New-Item -ItemType Directory -Force build\\gh-pages | Out-Null
                        Copy-Item -Recurse -Force build\\pages\\* build\\gh-pages\\
                        Copy-Item -Force build\\pages\\.nojekyll build\\gh-pages\\
                        Push-Location build\\gh-pages
                        try {
                            Exec { git init -q -b gh-pages }
                            Exec { git config user.name 'Jenkins CI' }
                            Exec { git config user.email 'jenkins@d2daudit.local' }
                            Exec { git config core.autocrlf false }
                            Exec { git add -A }
                            Exec { git commit -q -m "Deploy review site & live demo from $commit" }
                            Exec { git -c credential.helper= -c "http.extraHeader=Authorization: Basic $auth" push --force "https://github.com/$env:GITHUB_REPOSITORY.git" gh-pages }
                        } finally { Pop-Location }
                        $api = "https://api.github.com/repos/$env:GITHUB_REPOSITORY/pages"
                        $headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
                        $body = '{"build_type":"legacy","source":{"branch":"gh-pages","path":"/"}}'
                        $exists = $true
                        try { Invoke-RestMethod -Headers $headers -Uri $api | Out-Null } catch {
                            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { $exists = $false } else { throw }
                        }
                        $method = if ($exists) { 'Put' } else { 'Post' }
                        Invoke-RestMethod -Method $method -Headers $headers -Uri $api -Body $body -ContentType 'application/json' | Out-Null
                        $pages = Invoke-RestMethod -Headers $headers -Uri $api
                        Write-Output "GitHub Pages: $($pages.html_url) (build_type=$($pages.build_type), source=$($pages.source.branch)$($pages.source.path))"
                    ''')
                }
            }
        }

        stage('CD: Publish tutorials') {
            when {
                allOf {
                    branch 'main'
                    expression { return params.DEPLOY }
                    expression { return fileExists('scripts/tutorial/publish.ps1') }
                }
            }
            steps {
                withCredentials([usernamePassword(credentialsId: 'github-cvpeceee', usernameVariable: 'GH_USER', passwordVariable: 'GH_TOKEN')]) {
                    runStep('scripts/tutorial/publish.ps1', '''
                        export GITHUB_TOKEN="$GH_TOKEN"
                        if command -v pwsh >/dev/null 2>&1; then
                            pwsh -NoProfile -File scripts/tutorial/publish.ps1
                        else
                            echo "pwsh is not installed on this agent; skipping tutorial publish."
                        fi
                    ''', '''
                        $env:GITHUB_TOKEN = $env:GH_TOKEN
                        Exec { powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\\tutorial\\publish.ps1 }
                    ''')
                }
            }
        }
    }

    post {
        always {
            junit(testResults: 'test-results/**/*.xml', allowEmptyResults: true)
            archiveArtifacts(
                artifacts: 'd2d-review-site.tar.gz,nuget-vulnerabilities.txt,test-results/**/*.trx,test-results/**/*.xml',
                allowEmptyArchive: true,
                fingerprint: true
            )
            cleanWs(deleteDirs: true, notFailBuild: true)
        }
    }
}
