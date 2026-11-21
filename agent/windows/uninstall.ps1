<#
.SYNOPSIS
  Removes the D2D Audit agent scheduled task, scripts and device credential from this user profile.
#>
$ErrorActionPreference = 'Stop'
$TaskName = 'D2D Audit Agent'
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
  Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
$InstallDir = Join-Path $env:LOCALAPPDATA 'D2DAudit'
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
Write-Output 'D2D Audit agent removed. Ask an admin to revoke the device in People > Computers.'
