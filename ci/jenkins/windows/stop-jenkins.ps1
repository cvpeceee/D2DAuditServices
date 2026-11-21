<#
.SYNOPSIS
  Stops the native Windows Jenkins controller started by start-jenkins.ps1.
  Run with: powershell -NoProfile -ExecutionPolicy Bypass -File ci\jenkins\windows\stop-jenkins.ps1
#>
[CmdletBinding()]
param([string]$ToolsDir)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $ToolsDir) {
    if ($env:D2D_JENKINS_TOOLS) {
        $ToolsDir = $env:D2D_JENKINS_TOOLS
    } else {
        $commonDir = (& git -C $repoRoot rev-parse --path-format=absolute --git-common-dir 2>$null)
        $mainRoot = if ($LASTEXITCODE -eq 0 -and $commonDir) { Split-Path -Parent $commonDir } else { $repoRoot }
        $ToolsDir = Join-Path (Split-Path -Parent $mainRoot) 'tools\jenkins'
    }
}

$pidFile = Join-Path $ToolsDir 'jenkins.pid'
if (-not (Test-Path $pidFile)) {
    Write-Host 'Jenkins is not running (no PID file).'
    exit 0
}
$jenkinsPid = [int](Get-Content $pidFile -Raw)
$process = Get-Process -Id $jenkinsPid -ErrorAction SilentlyContinue
if ($process -and $process.ProcessName -eq 'java') {
    Stop-Process -Id $jenkinsPid
    $process.WaitForExit(60000) | Out-Null
    Write-Host "Stopped Jenkins (PID $jenkinsPid)."
} else {
    Write-Host "No Jenkins process with PID $jenkinsPid."
}
Remove-Item $pidFile -Force
