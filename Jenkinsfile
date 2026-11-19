// Open-source Jenkins Declarative Pipeline for D2D Audit Services.
// Replaces GitHub Actions CI + Review Site packaging with zero external CI minute limits.
// See ci/jenkins/README.md for self-hosted Docker Compose + JCasC setup.

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20', artifactNumToKeepStr: '10'))
        timeout(time: 30, unit: 'MINUTES')
    }

    parameters {
        booleanParam(
            name: 'RUN_INTEGRATION_TESTS',
            defaultValue: true,
            description: 'Run full D2DAudit.Tests suite (requires Docker/Testcontainers or Postgres service)'
        )
        booleanParam(
            name: 'DEPLOY_GH_PAGES',
            defaultValue: false,
            description: 'Push built docs + live demo directly to the gh-pages branch using Jenkins credential github-pages-token'
        )
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        CI                          = 'true'
    }

    triggers {
        // Fallback polling when GitHub webhook cannot reach a private/LAN Jenkins instance.
        pollSCM('H/5 * * * *')
    }

    stages {
        stage('Parallel CI Checks') {
            parallel {
                stage('API Build, Test & NuGet Audit') {
                    steps {
                        sh '''#!/usr/bin/env bash
                            set -euo pipefail
                            dotnet --info
                            dotnet build src/api/D2DAudit.slnx --configuration Release
                            dotnet test src/api/tests/D2DAudit.UnitTests/D2DAudit.UnitTests.csproj \
                                --configuration Release --no-build --logger "trx;LogFileName=unit-tests.trx"
                        '''
                        script {
                            if (params.RUN_INTEGRATION_TESTS) {
                                sh '''#!/usr/bin/env bash
                                    set -euo pipefail
                                    dotnet test src/api/tests/D2DAudit.Tests/D2DAudit.Tests.csproj \
                                        --configuration Release --no-build --logger "trx;LogFileName=integration-tests.trx"
                                '''
                            } else {
                                sh '''#!/usr/bin/env bash
                                    set -euo pipefail
                                    dotnet test src/api/tests/D2DAudit.Tests/D2DAudit.Tests.csproj \
                                        --configuration Release --no-build \
                                        --filter "FullyQualifiedName~ArchitectureTests|FullyQualifiedName~SchemaParityTests|FullyQualifiedName~CloudInventoryTests|FullyQualifiedName~Framework_content_matches_the_public_structure|FullyQualifiedName~ApiPipelineSecurityTests" \
                                        --logger "trx;LogFileName=arch-schema-tests.trx"
                                '''
                            }
                        }
                        sh '''#!/usr/bin/env bash
                            set -euo pipefail
                            dotnet list src/api/D2DAudit.slnx package --vulnerable --include-transitive | tee vuln.txt
                            if grep -q "has the following vulnerable packages" vuln.txt; then
                                echo "Vulnerable NuGet packages were reported."
                                exit 1
                            fi
                        '''
                    }
                }

                stage('Web Lint, Format, Test, Build & Audit') {
                    steps {
                        dir('src/web') {
                            sh '''#!/usr/bin/env bash
                                set -euo pipefail
                                node --version
                                npm ci
                                npm run lint
                                npm run format:check
                                npm test
                                npm run build
                                npm run build:demo
                                for attempt in 1 2 3; do
                                    npm audit --audit-level=high && exit 0
                                    echo "npm audit attempt ${attempt} failed"
                                    sleep $((attempt * 15))
                                done
                                exit 1
                            '''
                        }
                    }
                }

                stage('Secrets Scan (Gitleaks)') {
                    steps {
                        sh '''#!/usr/bin/env bash
                            set -euo pipefail
                            if ! command -v gitleaks >/dev/null 2>&1; then
                                ARCH=$(uname -m)
                                case "$ARCH" in
                                    x86_64|amd64) GL_ARCH=x64 ;;
                                    aarch64|arm64) GL_ARCH=arm64 ;;
                                    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
                                esac
                                curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v8.28.0/gitleaks_8.28.0_linux_${GL_ARCH}.tar.gz" | tar -xz -C /tmp gitleaks
                                GITLEAKS_BIN=/tmp/gitleaks
                            else
                                GITLEAKS_BIN=$(command -v gitleaks)
                            fi
                            "$GITLEAKS_BIN" detect --source . --config .gitleaks.toml --redact --verbose
                        '''
                    }
                }
            }
        }

        stage('Package Review Site & Live Demo') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    rm -rf build/pages
                    mkdir -p build/pages
                    cp -a docs/. build/pages/
                    rm -rf build/pages/app
                    mkdir -p build/pages/app
                    cp -a src/web/dist/. build/pages/app/
                    cp build/pages/app/index.html build/pages/app/404.html
                    tar -czf d2d-review-site.tar.gz -C build/pages .
                '''
                archiveArtifacts artifacts: 'd2d-review-site.tar.gz,vuln.txt,**/TestResults/*.trx', allowEmptyArchive: true
            }
        }

        stage('Publish gh-pages (Optional, Zero GitHub Actions)') {
            when {
                allOf {
                    branch 'main'
                    expression { return params.DEPLOY_GH_PAGES == true }
                }
            }
            steps {
                withCredentials([usernamePassword(credentialsId: 'github-pages-token', usernameVariable: 'GH_USER', passwordVariable: 'GH_TOKEN')]) {
                    sh '''#!/usr/bin/env bash
                        set -euo pipefail
                        RAW_URL=$(git config --get remote.origin.url)
                        if [[ "$RAW_URL" =~ ^git@([^:]+):(.+)$ ]]; then
                            HTTPS_URL="https://${BASH_REMATCH[1]}/${BASH_REMATCH[2]}"
                        elif [[ "$RAW_URL" =~ ^https://([^@]+@)?(.+)$ ]]; then
                            HTTPS_URL="https://${BASH_REMATCH[2]}"
                        else
                            echo "Unsupported origin URL format."
                            exit 1
                        fi
                        AUTH_B64=$(printf '%s:%s' "$GH_USER" "$GH_TOKEN" | base64 | tr -d '\n')
                        TMP_DIR=$(mktemp -d)
                        trap 'rm -rf "$TMP_DIR"' EXIT
                        cp -a build/pages/. "$TMP_DIR/"
                        touch "$TMP_DIR/.nojekyll"
                        cd "$TMP_DIR"
                        git init -b gh-pages
                        git config user.name "Jenkins CI"
                        git config user.email "jenkins@d2daudit.local"
                        git add -A
                        git commit -m "Deploy review site & live demo from ${GIT_COMMIT:-main}"
                        git -c "http.extraHeader=Authorization: Basic ${AUTH_B64}" push --force "$HTTPS_URL" gh-pages
                    '''
                }
            }
        }
    }

    post {
        always {
            cleanWs(deleteDirs: true, notFailBuild: true)
        }
    }
}
