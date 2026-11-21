<#
.SYNOPSIS
  Installs the D2D Audit agent as a per-user Scheduled Task that runs every 6 hours.

.EXAMPLE
  .\install.ps1 -Server https://app.example.com -Token d2de_...
  .\install.ps1 -From .\d2d-agent.json
  .\install.ps1 -From .\d2d-agent.json -Exe .\d2d-agent.exe   # use the .NET agent instead of the script
#>
[CmdletBinding()]
param(
  [string]$Server,
  [string]$Token,
  [string]$From,
  [string]$Exe
)

$ErrorActionPreference = 'Stop'
$TaskName = 'D2D Audit Agent'
$InstallDir = Join-Path $env:LOCALAPPDATA 'D2DAudit'
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$enrollArgs = @()
if ($From) { $enrollArgs += '-From', (Resolve-Path $From).Path }
if ($Server) { $enrollArgs += '-Server', $Server }
if ($Token) { $enrollArgs += '-Token', $Token }

if ($Exe) {
  $target = Join-Path $InstallDir 'd2d-agent.exe'
  Copy-Item -Force $Exe $target
  $netArgs = @('enroll') + ($enrollArgs -replace '^-From$', '--from' -replace '^-Server$', '--server' -replace '^-Token$', '--token')
  & $target @netArgs
  if ($LASTEXITCODE -ne 0) { throw 'Enrollment failed.' }
  $action = New-ScheduledTaskAction -Execute $target -Argument 'run'
} else {
  $target = Join-Path $InstallDir 'd2d-agent.ps1'
  Copy-Item -Force (Join-Path $PSScriptRoot 'd2d-agent.ps1') $target
  & $target -Enroll @enrollArgs
  $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$target`""
}

# Runs as the signed-in user so per-user settings (screen lock) are read correctly.
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Hours 6)
$logon = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger, $logon -Settings $settings -Description 'Sends device posture to D2D Audit every 6 hours.' -Force | Out-Null
Write-Output "Installed scheduled task '$TaskName' (every 6 hours and at logon)."
