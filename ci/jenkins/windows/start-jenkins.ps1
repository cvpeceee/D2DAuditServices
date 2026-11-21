<#
.SYNOPSIS
  Starts a native (no Docker) open-source Jenkins controller for D2D Audit Services on Windows.

.DESCRIPTION
  Idempotent. On first run it downloads a portable Eclipse Temurin JDK 21, the Jenkins LTS war and the
  Jenkins plugin installation manager into the tools directory (outside the repository), installs the
  plugins listed in ci/jenkins/plugins.txt, generates a random admin password, and starts Jenkins in the
  background on http://localhost:8085/ configured by ci/jenkins/casc.windows.yaml (JCasC).

  Secrets never touch the repository:
    * admin password  -> <ToolsDir>\admin-password.txt (generated once)
    * GitHub token    -> read from Git Credential Manager (`git credential fill`) and passed to the
                         Jenkins process as an environment variable only
    * Render hook URL -> optional, $env:RENDER_DEPLOY_HOOK_URL or <ToolsDir>\render-deploy-hook-url.txt

  Run with: powershell -NoProfile -ExecutionPolicy Bypass -File ci\jenkins\windows\start-jenkins.ps1
#>
[CmdletBinding()]
param(
    [string]$ToolsDir,
    [int]$Port = 8085,
    [string]$GitHubUser = 'cvpeceee',
    [int]$StartupTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $ToolsDir) {
    if ($env:D2D_JENKINS_TOOLS) {
        $ToolsDir = $env:D2D_JENKINS_TOOLS
    } else {
        # Resolve the main checkout (works from git worktrees too) and use its sibling "tools\jenkins".
        $commonDir = (& git -C $repoRoot rev-parse --path-format=absolute --git-common-dir 2>$null)
        $mainRoot = if ($LASTEXITCODE -eq 0 -and $commonDir) { Split-Path -Parent $commonDir } else { $repoRoot }
        $ToolsDir = Join-Path (Split-Path -Parent $mainRoot) 'tools\jenkins'
    }
}
New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
$ToolsDir = (Resolve-Path $ToolsDir).Path

$jenkinsHome = Join-Path $ToolsDir 'home'
$jdkDir = Join-Path $ToolsDir 'jdk'
$warPath = Join-Path $ToolsDir 'jenkins.war'
$pimPath = Join-Path $ToolsDir 'jenkins-plugin-manager.jar'
$pidFile = Join-Path $ToolsDir 'jenkins.pid'
$logFile = Join-Path $ToolsDir 'jenkins.out.log'
$errFile = Join-Path $ToolsDir 'jenkins.log'
$passwordFile = Join-Path $ToolsDir 'admin-password.txt'
$renderHookFile = Join-Path $ToolsDir 'render-deploy-hook-url.txt'
$pluginsFile = Join-Path $repoRoot 'ci\jenkins\plugins.txt'
$cascFile = Join-Path $repoRoot 'ci\jenkins\casc.windows.yaml'
$url = "http://localhost:$Port/"

function Test-JenkinsUp {
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri "${url}login" -TimeoutSec 5
        return $r.StatusCode -eq 200
    } catch {
        return $false
    }
}

if (Test-Path $pidFile) {
    $existingPid = [int](Get-Content $pidFile -Raw)
    $existing = Get-Process -Id $existingPid -ErrorAction SilentlyContinue
    if ($existing -and $existing.ProcessName -eq 'java') {
        Write-Host "Jenkins is already running (PID $existingPid) at $url"
        exit 0
    }
    Remove-Item $pidFile -Force
}

# 1. Portable JDK 21 (Eclipse Temurin)
$java = Join-Path $jdkDir 'bin\java.exe'
if (-not (Test-Path $java)) {
    Write-Host 'Downloading Eclipse Temurin JDK 21 (portable zip)...'
    $zip = Join-Path $ToolsDir 'temurin21.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jdk/hotspot/normal/eclipse' -OutFile $zip
    $extract = Join-Path $ToolsDir 'jdk-extract'
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $extract
    $inner = Get-ChildItem $extract -Directory | Select-Object -First 1
    if (Test-Path $jdkDir) { Remove-Item $jdkDir -Recurse -Force }
    Move-Item $inner.FullName $jdkDir
    Remove-Item $extract -Recurse -Force
    Remove-Item $zip -Force
}

# 2. Jenkins LTS war + plugin installation manager
if (-not (Test-Path $warPath)) {
    Write-Host 'Downloading Jenkins LTS war...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://get.jenkins.io/war-stable/latest/jenkins.war' -OutFile $warPath
}
if (-not (Test-Path $pimPath)) {
    Write-Host 'Downloading Jenkins plugin installation manager...'
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/jenkinsci/plugin-installation-manager-tool/releases/latest'
    $asset = $release.assets | Where-Object { $_.name -like 'jenkins-plugin-manager-*.jar' } | Select-Object -First 1
    Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $pimPath
}

# 3. Plugins (re-installed only when plugins.txt changes)
$pluginDir = Join-Path $jenkinsHome 'plugins'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
$pluginHash = (Get-FileHash $pluginsFile -Algorithm SHA256).Hash
$pluginMarker = Join-Path $ToolsDir 'plugins.sha256'
if (-not (Test-Path $pluginMarker) -or (Get-Content $pluginMarker -Raw).Trim() -ne $pluginHash) {
    Write-Host 'Installing Jenkins plugins from ci/jenkins/plugins.txt...'
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $java -jar $pimPath --war $warPath --plugin-download-directory $pluginDir --plugin-file $pluginsFile 2>&1 |
        ForEach-Object { "$_" }
    $ErrorActionPreference = $previousPreference
    if ($LASTEXITCODE -ne 0) { throw "Plugin installation failed (exit $LASTEXITCODE)." }
    Set-Content -Path $pluginMarker -Value $pluginHash -NoNewline
}

# 4. Secrets (never written to the repository)
if (-not (Test-Path $passwordFile)) {
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $generated = [Convert]::ToBase64String($bytes).Replace('+', 'A').Replace('/', 'z').TrimEnd('=')
    Set-Content -Path $passwordFile -Value $generated -NoNewline
    Write-Host "Generated Jenkins admin password in $passwordFile"
}
$adminPassword = (Get-Content $passwordFile -Raw).Trim()

$env:GCM_INTERACTIVE = 'never'
$credOutput = "protocol=https`nhost=github.com`nusername=$GitHubUser`n`n" | & git credential fill 2>$null
$gitHubToken = ($credOutput | Where-Object { $_ -like 'password=*' } | Select-Object -First 1)
if ($gitHubToken) { $gitHubToken = $gitHubToken.Substring('password='.Length) }
if (-not $gitHubToken) {
    throw "Could not read a GitHub token for '$GitHubUser' from Git Credential Manager. Run 'git fetch' once interactively to sign in, then retry."
}

$renderHook = $env:RENDER_DEPLOY_HOOK_URL
if (-not $renderHook -and (Test-Path $renderHookFile)) { $renderHook = (Get-Content $renderHookFile -Raw).Trim() }

$testPostgres = $env:TEST_POSTGRES
if (-not $testPostgres) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        if ($client.ConnectAsync('127.0.0.1', 5432).Wait(1500)) {
            $testPostgres = 'Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres'
        }
    } catch {
        $testPostgres = $null
    } finally {
        $client.Dispose()
    }
}

# 5. Start Jenkins in the background (child inherits these variables; JCasC reads the CASC_* ones)
$env:JENKINS_HOME = $jenkinsHome
$env:JAVA_HOME = $jdkDir
# Copy the (secret-free) JCasC file next to JENKINS_HOME so the running controller does not depend on
# the checkout/worktree it was started from.
$cascCopy = Join-Path $ToolsDir 'casc.yaml'
Copy-Item $cascFile $cascCopy -Force
$env:CASC_JENKINS_CONFIG = $cascCopy
$env:JENKINS_URL = $url
$env:JENKINS_ADMIN_USER = 'admin'
$env:JENKINS_ADMIN_PASSWORD = $adminPassword
$env:CASC_GITHUB_USER = $GitHubUser
$env:CASC_GITHUB_TOKEN = $gitHubToken
$env:CASC_RENDER_DEPLOY_HOOK_URL = if ($renderHook) { $renderHook } else { 'unset' }
if ($testPostgres) { $env:TEST_POSTGRES = $testPostgres } else { Remove-Item Env:TEST_POSTGRES -ErrorAction SilentlyContinue }

$javaArgs = @(
    '-Xmx1536m',
    '-Djava.awt.headless=true',
    '-Djenkins.install.runSetupWizard=false',
    '-Dfile.encoding=UTF-8',
    '-jar', "`"$warPath`"",
    "--httpPort=$Port",
    '--httpListenAddress=127.0.0.1'
)
$process = Start-Process -FilePath $java -ArgumentList $javaArgs -WorkingDirectory $ToolsDir -WindowStyle Hidden `
    -RedirectStandardOutput $logFile -RedirectStandardError $errFile -PassThru
Set-Content -Path $pidFile -Value $process.Id -NoNewline

# Do not keep secrets in this shell longer than needed.
Remove-Item Env:CASC_GITHUB_TOKEN, Env:JENKINS_ADMIN_PASSWORD, Env:CASC_RENDER_DEPLOY_HOOK_URL -ErrorAction SilentlyContinue
$gitHubToken = $null
$adminPassword = $null

Write-Host "Starting Jenkins (PID $($process.Id)); log: $errFile"
$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ($process.HasExited) { throw "Jenkins exited early with code $($process.ExitCode). See $errFile" }
    if (Test-JenkinsUp) {
        Write-Host "Jenkins is up: $url  (user: admin, password file: $passwordFile)"
        exit 0
    }
    Start-Sleep -Seconds 5
}
throw "Jenkins did not answer ${url}login within $StartupTimeoutSeconds seconds. See $errFile"
