<#
.SYNOPSIS
  D2D Audit device agent for Windows (lightweight PowerShell alternative to d2d-agent.exe).

.DESCRIPTION
  Collects device posture (BitLocker, screen lock, anti-malware via SecurityCenter2, password manager,
  OS updates, firewall) and sends a signed check-in to the D2D Audit API. Uses the same JSON schema and
  HMAC signing as the .NET agent.

.EXAMPLE
  .\d2d-agent.ps1 -DryRun
  .\d2d-agent.ps1 -Enroll -Server https://app.example.com -Token d2de_...
  .\d2d-agent.ps1            # signed check-in using the saved config
#>
[CmdletBinding()]
param(
  [switch]$DryRun,
  [switch]$Enroll,
  [string]$Server,
  [string]$Token,
  [string]$From,
  [string]$Config = (Join-Path $env:LOCALAPPDATA 'D2DAudit\agent.json')
)

$ErrorActionPreference = 'Stop'
$AgentVersion = '1.0.0'
$MaxLockSeconds = 900
$UpdateWindowDays = 45
$Managers = '1Password', 'Bitwarden', 'LastPass', 'KeePassXC', 'KeePass', 'Dashlane', 'Keeper', 'NordPass', 'Proton Pass'

function New-Check([string]$Key, [bool]$Pass, [string]$Detail) {
  [ordered]@{ key = $Key; pass = $Pass; detail = $Detail }
}

function Get-RegValue([string]$Path, [string]$Name) {
  try { (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name } catch { $null }
}

function Test-DiskEncryption {
  try {
    $status = (Get-CimInstance -Namespace root/cimv2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume -Filter "DriveLetter='C:'" -ErrorAction Stop).ProtectionStatus
    if ($status -eq 1) { return New-Check 'diskEncryption' $true 'BitLocker protection on for C:' }
    if ($status -eq 0) { return New-Check 'diskEncryption' $false 'BitLocker protection off for C:' }
  } catch { }
  try {
    $value = (New-Object -ComObject Shell.Application).NameSpace('C:').Self.ExtendedProperty('System.Volume.BitLockerProtection')
  } catch { $value = $null }
  switch ($value) {
    { $_ -in 1, 3, 6 } { return New-Check 'diskEncryption' $true 'BitLocker protection on for C:' }
    5 { return New-Check 'diskEncryption' $false 'BitLocker protection suspended for C:' }
    { $_ -in 2, 4 } { return New-Check 'diskEncryption' $false 'BitLocker off for C:' }
  }
  New-Check 'diskEncryption' $false 'BitLocker status unavailable for C:'
}

function Test-ScreenLock {
  $limit = Get-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'InactivityTimeoutSecs'
  if ($limit -and [int]$limit -gt 0) {
    if ([int]$limit -le $MaxLockSeconds) { return New-Check 'screenLock' $true "Machine inactivity limit $($limit)s" }
    return New-Check 'screenLock' $false "Machine inactivity limit $($limit)s exceeds $($MaxLockSeconds)s"
  }
  $desktop = 'HKCU:\Control Panel\Desktop'
  $policy = 'HKCU:\Software\Policies\Microsoft\Windows\Control Panel\Desktop'
  $active = (Get-RegValue $policy 'ScreenSaveActive'), (Get-RegValue $desktop 'ScreenSaveActive') | Where-Object { $null -ne $_ } | Select-Object -First 1
  $secure = (Get-RegValue $policy 'ScreenSaverIsSecure'), (Get-RegValue $desktop 'ScreenSaverIsSecure') | Where-Object { $null -ne $_ } | Select-Object -First 1
  $timeout = (Get-RegValue $policy 'ScreenSaveTimeOut'), (Get-RegValue $desktop 'ScreenSaveTimeOut') | Where-Object { $null -ne $_ } | Select-Object -First 1
  if ("$active" -ne '1') { return New-Check 'screenLock' $false 'Screen saver is off' }
  if ("$secure" -ne '1') { return New-Check 'screenLock' $false 'Screen saver does not require sign-in' }
  if ($timeout -and [int]$timeout -gt 0 -and [int]$timeout -le $MaxLockSeconds) { return New-Check 'screenLock' $true "Secure screen saver after $($timeout)s" }
  New-Check 'screenLock' $false "Screen saver timeout $($timeout)s exceeds $($MaxLockSeconds)s"
}

function Test-Antivirus {
  try {
    $products = @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop)
  } catch { $products = @() }
  if ($products.Count -gt 0) {
    foreach ($product in $products) {
      $state = [int]$product.productState
      $enabled = (($state -shr 8) -band 0xFF) -in 0x10, 0x11
      $current = ($state -band 0xFF) -eq 0
      if ($enabled -and $current) { return New-Check 'antivirus' $true "$($product.displayName) enabled, signatures current" }
    }
    return New-Check 'antivirus' $false "$(($products | ForEach-Object displayName) -join ', ') registered but not enabled with current signatures"
  }
  try {
    $defender = Get-MpComputerStatus -ErrorAction Stop
    if ($defender.AMServiceEnabled -and $defender.RealTimeProtectionEnabled) { return New-Check 'antivirus' $true 'Microsoft Defender real-time protection on' }
    return New-Check 'antivirus' $false 'Microsoft Defender real-time protection off'
  } catch { }
  New-Check 'antivirus' $false 'No anti-malware product registered with Windows Security Center'
}

function Test-PasswordManager {
  $keys = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
  $names = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName } | ForEach-Object { $_.DisplayName })
  $names += @(Get-AppxPackage -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
  foreach ($path in (Join-Path $env:LOCALAPPDATA '1Password'), (Join-Path $env:LOCALAPPDATA 'Programs\Bitwarden'), (Join-Path $env:ProgramFiles 'KeePass Password Safe 2'), (Join-Path $env:ProgramFiles 'KeePassXC')) {
    if (Test-Path $path) { $names += Split-Path $path -Leaf }
  }
  foreach ($manager in $Managers) {
    $found = $names | Where-Object { $_ -like "*$manager*" } | Select-Object -First 1
    if ($found) { return New-Check 'passwordManager' $true $found }
  }
  New-Check 'passwordManager' $false 'No approved password manager (1Password, Bitwarden, LastPass, KeePass) found'
}

function Test-OsUpdates {
  if ((Get-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate') -eq 1) {
    return New-Check 'osUpdates' $false 'Automatic updates are disabled by policy'
  }
  $last = Get-HotFix -ErrorAction SilentlyContinue | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1
  if (-not $last) { return New-Check 'osUpdates' $false 'No installed update history found' }
  $age = [int][math]::Floor(((Get-Date) - $last.InstalledOn).TotalDays)
  $day = $last.InstalledOn.ToString('yyyy-MM-dd')
  if ($age -le $UpdateWindowDays) { return New-Check 'osUpdates' $true "Last update installed $day ($age days ago)" }
  New-Check 'osUpdates' $false "Last update installed $day ($age days ago, limit $UpdateWindowDays)"
}

function Test-Firewall {
  try {
    $profiles = @(Get-NetFirewallProfile -ErrorAction Stop)
    $off = @($profiles | Where-Object { -not $_.Enabled } | ForEach-Object Name)
    if ($off.Count -eq 0) { return New-Check 'firewall' $true "Windows Firewall on for $(($profiles | ForEach-Object Name) -join ', ')" }
    return New-Check 'firewall' $false "Windows Firewall off for $($off -join ', ')"
  } catch {
    New-Check 'firewall' $false 'Firewall status unavailable'
  }
}

function Get-Serial {
  try { $serial = (Get-CimInstance -ClassName Win32_BIOS -ErrorAction Stop).SerialNumber.Trim() } catch { $serial = '' }
  if (-not $serial -or $serial -in 'To Be Filled By O.E.M.', 'Default string', 'System Serial Number' -or $serial -match '^0+$') { $serial = $env:COMPUTERNAME }
  $serial
}

function Get-Payload {
  [ordered]@{
    schemaVersion   = 1
    agentVersion    = $AgentVersion
    agentKind       = 'powershell'
    serial          = Get-Serial
    hostname        = $env:COMPUTERNAME
    operatingSystem = "Microsoft Windows $([Environment]::OSVersion.Version)"
    platform        = 'windows'
    collectedAt     = (Get-Date).ToUniversalTime().ToString('o')
    checks          = @(Test-DiskEncryption; Test-ScreenLock; Test-Antivirus; Test-PasswordManager; Test-OsUpdates; Test-Firewall)
  }
}

function Get-Hex([byte[]]$Bytes) { -join ($Bytes | ForEach-Object { $_.ToString('x2') }) }

function Get-Signature([string]$Secret, [long]$Timestamp, [string]$Body) {
  $utf8 = [Text.Encoding]::UTF8
  $sha = [Security.Cryptography.SHA256]::Create()
  $key = Get-Hex $sha.ComputeHash($utf8.GetBytes($Secret))
  $hmac = New-Object Security.Cryptography.HMACSHA256 (, $utf8.GetBytes($key))
  'v1=' + (Get-Hex $hmac.ComputeHash($utf8.GetBytes("$Timestamp.$Body")))
}

function Send-Json([string]$Uri, [string]$Body, [hashtable]$Headers = @{}) {
  $Headers['X-D2D-Request'] = '1'
  Invoke-RestMethod -Method Post -Uri $Uri -Body ([Text.Encoding]::UTF8.GetBytes($Body)) -ContentType 'application/json' -Headers $Headers
}

if ($DryRun) {
  Get-Payload | ConvertTo-Json -Depth 5
  return
}

if ($Enroll) {
  if ($From) {
    $seed = Get-Content $From -Raw | ConvertFrom-Json
    if (-not $Server) { $Server = $seed.server }
    if (-not $Token) { $Token = $seed.token }
  }
  if (-not $Server -or -not $Token) { throw 'Enroll needs -Server and -Token (or -From d2d-agent.json).' }
  $uri = [Uri]$Server
  if ($uri.Scheme -ne 'https' -and -not $uri.IsLoopback) { throw 'The server must be https:// (http only for localhost).' }
  $payload = Get-Payload
  $request = [ordered]@{ token = $Token.Trim(); serial = $payload.serial; hostname = $payload.hostname; operatingSystem = $payload.operatingSystem; platform = 'windows'; agentKind = 'powershell'; agentVersion = $AgentVersion }
  $result = Send-Json ($uri.GetLeftPart('Authority') + '/api/v1/agent/enroll') ($request | ConvertTo-Json -Compress)
  New-Item -ItemType Directory -Force -Path (Split-Path $Config) | Out-Null
  [ordered]@{ server = $uri.GetLeftPart('Authority'); deviceId = $result.deviceId; secret = $result.secret; checkInPath = $result.checkInPath } | ConvertTo-Json | Set-Content -Path $Config -Encoding UTF8
  Write-Output "Enrolled $($payload.serial) for $($result.personEmail). Config saved to $Config."
}

if (-not (Test-Path $Config)) { throw "This device is not enrolled. Run: .\d2d-agent.ps1 -Enroll -Server <url> -Token <token>" }
$settings = Get-Content $Config -Raw | ConvertFrom-Json
$body = Get-Payload | ConvertTo-Json -Depth 5 -Compress
$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$headers = @{ 'X-D2D-Device' = $settings.deviceId; 'X-D2D-Timestamp' = "$timestamp"; 'X-D2D-Signature' = (Get-Signature $settings.secret $timestamp $body) }
$path = if ($settings.checkInPath) { $settings.checkInPath } else { '/api/v1/agent/check-in' }
$response = Send-Json ($settings.server + $path) $body $headers
Write-Output "Check-in accepted: $($response | ConvertTo-Json -Compress)"
