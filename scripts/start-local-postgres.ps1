<#
.SYNOPSIS
  Starts a portable PostgreSQL server for integration tests when Docker is not available.

.DESCRIPTION
  Uses the EnterpriseDB Windows x64 binaries zip (no installer, no service).
  Download once, for example:
    curl.exe -L -o pg.zip https://get.enterprisedb.com/postgresql/postgresql-17.7-1-windows-x64-binaries.zip
    tar -xf pg.zip    # creates .\pgsql
  The first run initialises a cluster in -DataDir with a superuser role. Later runs only start it.
  Then run the integration tests with:
    $env:TEST_POSTGRES = "Host=localhost;Port=5432;Username=d2d;Password=d2d_dev_password;Database=postgres"
    dotnet test src/api/D2DAudit.slnx
  Stop the server with:
    & "<PgHome>\bin\pg_ctl.exe" -D "<DataDir>" stop

  Invoke with `powershell -ExecutionPolicy Bypass -File scripts/start-local-postgres.ps1` when
  script execution is disabled.
#>
param(
    [string]$PgHome = (Join-Path $PSScriptRoot "..\..\tools\pgsql"),
    [string]$DataDir = (Join-Path $PSScriptRoot "..\..\tools\pgdata"),
    [int]$Port = 5432,
    [string]$User = "d2d",
    [string]$Password = "d2d_dev_password"
)

$ErrorActionPreference = "Stop"
$bin = Join-Path $PgHome "bin"
if (-not (Test-Path (Join-Path $bin "pg_ctl.exe"))) {
    throw "pg_ctl.exe not found under $bin. Extract the EDB binaries zip so that -PgHome points at its pgsql folder."
}

if (-not (Test-Path (Join-Path $DataDir "PG_VERSION"))) {
    New-Item -ItemType Directory -Force (Split-Path $DataDir) | Out-Null
    $pwFile = New-TemporaryFile
    try {
        Set-Content -Path $pwFile -Value $Password -NoNewline -Encoding ascii
        & (Join-Path $bin "initdb.exe") -D $DataDir -U $User --pwfile=$pwFile -A scram-sha-256 -E UTF8 --locale=C
        if ($LASTEXITCODE -ne 0) { throw "initdb failed" }
    }
    finally {
        Remove-Item $pwFile -Force
    }
}

& (Join-Path $bin "pg_ctl.exe") -D $DataDir status | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "PostgreSQL is already running from $DataDir"
    exit 0
}

$log = Join-Path $DataDir "server.log"
& (Join-Path $bin "pg_ctl.exe") -D $DataDir -l $log -o "-p $Port -c max_connections=300" -w start
if ($LASTEXITCODE -ne 0) { throw "pg_ctl start failed; see $log" }
Write-Host "PostgreSQL listening on localhost:$Port (data: $DataDir, log: $log)"
Write-Host "TEST_POSTGRES=Host=localhost;Port=$Port;Username=$User;Password=$Password;Database=postgres"
