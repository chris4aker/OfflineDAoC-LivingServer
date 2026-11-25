<#
.SYNOPSIS
  Offline DAoC Living Server: a portable Grafana + Loki + Alloy + gcx on this PC (issue #628).

.DESCRIPTION
  No Docker, no installer, no admin rights, no service, no registry or PATH change. Everything goes under one folder
  (default C:\Games\OfflineDAoC\observability); deleting that folder (uninstall.cmd) removes it all. Every service
  listens on 127.0.0.1 only, and the game server's own port is not touched.

  The setup:
  1. downloads pinned Grafana OSS, Loki, Alloy and gcx releases over HTTPS from their official URLs, and refuses any file
     whose SHA256 is not the pinned one (the hashes are the releases' own checksum files);
  2. writes the Loki, Grafana and Alloy configs (templates from tools/grafana/windows/templates, the Alloy pipeline from
     tools/grafana/alloy/config.alloy: the allowlist and the name-dropping filter);
  3. generates the Grafana admin password (observability\admin-password.txt) and provisions the Loki data source;
  4. starts the three, makes a Grafana service-account token, logs gcx in to it (context "local", its config in
     observability\gcx with plaintext storage, so nothing goes to the Windows credential store) and pushes the
     dashboards with gcx, so `gcx resources push -p resources` can update them later. Without gcx (-DashboardMode Files)
     the dashboards are file-provisioned instead.

  Run it again at any time: it keeps the password, the stored rows and the downloads, and refreshes configs and
  dashboards.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File setup.ps1 -InstallRoot C:\Games\OfflineDAoC\LivingServer1.2.3-test
#>
[CmdletBinding()]
param(
    # The Living Server folder (the one with runtime\server). Its runtime\server\logs is read, never written.
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    # Where everything goes. Must be under C:\Games\OfflineDAoC\ unless -AllowOtherRoot.
    [string]$ObservabilityRoot = 'C:\Games\OfflineDAoC\observability',
    [switch]$AllowOtherRoot,
    # Gcx: push the dashboards with gcx. Files: file-provision them (gcx can't update those). Auto: gcx, else files.
    [ValidateSet('Auto', 'Gcx', 'Files')][string]$DashboardMode = 'Auto',
    # Write the configs only: no download, no start (used by the tests, and to refresh configs by hand).
    [switch]$ConfigOnly,
    [switch]$NoBrowser
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---- Pinned releases -------------------------------------------------------------------------------------------------
# SHA256 from each release's own checksums: dl.grafana.com/<file>.sha256, grafana/loki and grafana/alloy SHA256SUMS,
# grafana/gcx gcx_<version>_checksums.txt. A new version is a new pin here (tools/grafana/tests/test_windows.py checks
# the shape; docs/GRAFANA.md says how to bump).
$Releases = @(
    @{ Name = 'grafana'; Version = '13.3.2'; File = 'grafana-13.3.2.windows-amd64.zip'
       Url = 'https://dl.grafana.com/oss/release/grafana-13.3.2.windows-amd64.zip'
       Sha256 = '29137867c59d769b256959b264ee82e02b11cf87e44403bc58ebd2f230302319'; Exe = 'grafana.exe' },
    @{ Name = 'loki'; Version = '3.7.10'; File = 'loki-windows-amd64.exe.zip'
       Url = 'https://github.com/grafana/loki/releases/download/v3.7.10/loki-windows-amd64.exe.zip'
       Sha256 = '7e3ae97518b290cbcaf9dc359af00e9d0a541333fe131fe7ce3056197306bfaf'; Exe = 'loki-windows-amd64.exe' },
    @{ Name = 'alloy'; Version = '1.22.0'; File = 'alloy-windows-amd64.exe.zip'
       Url = 'https://github.com/grafana/alloy/releases/download/v1.22.0/alloy-windows-amd64.exe.zip'
       Sha256 = 'ba0303f8aaa8f7b457015e1454aff509a18358ebe0b583b3b276c5652ca679a7'; Exe = 'alloy-windows-amd64.exe' },
    @{ Name = 'gcx'; Version = '1.8.0'; File = 'gcx_1.8.0_windows_amd64.zip'
       Url = 'https://github.com/grafana/gcx/releases/download/v1.8.0/gcx_1.8.0_windows_amd64.zip'
       Sha256 = '915d8e7dbb688265b90a0c0b01f3dcc5c463779edd3d9cd7fbce10d68cc16685'; Exe = 'gcx.exe' }
)
$OfficialPrefixes = @('https://dl.grafana.com/oss/release/', 'https://github.com/grafana/loki/releases/download/',
                      'https://github.com/grafana/alloy/releases/download/', 'https://github.com/grafana/gcx/releases/download/')

$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Kit = Split-Path -Parent $Here   # tools\grafana

function Say([string]$text) { Write-Host "[observability] $text" }

function Get-FullPath([string]$path) { [System.IO.Path]::GetFullPath($path).TrimEnd('\', '/') }

function Test-Inside([string]$child, [string]$parent) {
    $c = (Get-FullPath $child) + [System.IO.Path]::DirectorySeparatorChar
    $p = (Get-FullPath $parent) + [System.IO.Path]::DirectorySeparatorChar
    return $c.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)
}

# ---- Paths -----------------------------------------------------------------------------------------------------------
$Root = Get-FullPath $ObservabilityRoot
$Allowed = 'C:\Games\OfflineDAoC'
if (-not $AllowOtherRoot -and -not (Test-Inside $Root $Allowed)) {
    throw "The observability folder must be under $Allowed\ (got $Root). Pass -AllowOtherRoot to use another place."
}
$Server = Get-FullPath $InstallRoot
if (-not (Test-Path -LiteralPath (Join-Path $Server 'runtime/server')) -and -not $ConfigOnly) {
    throw "$Server has no runtime\server: pass the Living Server folder as -InstallRoot."
}
if ((Test-Inside $Root $Server) -or (Test-Inside $Server $Root)) {
    throw "Keep the observability folder and the server folder apart ($Root, $Server)."
}
$LogsDir = Get-FullPath (Join-Path $Server 'runtime/server/logs')
$RootSlash = $Root -replace '\\', '/'

# Relative paths use '/': Windows takes it, and the tests run this script with -ConfigOnly on Linux too.
$Dirs = @('bin', 'downloads', 'data/loki', 'data/alloy', 'data/grafana', 'logs', 'run', 'alloy', 'gcx', 'resources',
          'dashboards', 'grafana-provisioning/datasources', 'grafana-provisioning/dashboards',
          'grafana-provisioning/plugins', 'grafana-provisioning/alerting')
foreach ($d in $Dirs) { New-Item -ItemType Directory -Force -Path (Join-Path $Root $d) | Out-Null }

function Write-Text([string]$path, [string]$text) {
    if (-not (Test-Inside $path $Root)) { throw "refusing to write outside $Root`: $path" }
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Write-Template([string]$name, [string]$target) {
    $text = [System.IO.File]::ReadAllText((Join-Path (Join-Path $Here 'templates') $name))
    Write-Text (Join-Path $Root $target) ($text.Replace('@@ROOT@@', $RootSlash))
}

# ---- Downloads -------------------------------------------------------------------------------------------------------
function Get-Release($r) {
    $ok = $false
    foreach ($p in $OfficialPrefixes) { if ($r.Url.StartsWith($p)) { $ok = $true } }
    if (-not $ok) { throw "not an official URL: $($r.Url)" }
    $zip = Join-Path $Root "downloads/$($r.File)"
    if (Test-Path -LiteralPath $zip) {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant() -ne $r.Sha256) {
            Remove-Item -LiteralPath $zip -Force
        }
    }
    if (-not (Test-Path -LiteralPath $zip)) {
        Say "downloading $($r.Name) $($r.Version)"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $r.Url -OutFile $zip
    }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
    if ($hash -ne $r.Sha256) {
        Remove-Item -LiteralPath $zip -Force
        throw "SHA256 mismatch for $($r.File): got $hash, pinned $($r.Sha256). Refusing it."
    }
    $dest = Join-Path $Root "bin/$($r.Name)-$($r.Version)"
    if (-not (Get-ChildItem -LiteralPath $dest -Recurse -Filter $r.Exe -ErrorAction SilentlyContinue)) {
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        Say "unpacking $($r.File)"
        Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    }
    $exe = Get-ChildItem -LiteralPath $dest -Recurse -Filter $r.Exe | Select-Object -First 1
    if (-not $exe) { throw "$($r.Exe) not found in $($r.File)" }
    return $exe.FullName
}

$Exes = @{}
if (-not $ConfigOnly) {
    foreach ($r in $Releases) { $Exes[$r.Name] = Get-Release $r }
}

# ---- Configs ---------------------------------------------------------------------------------------------------------
Write-Template 'loki.yaml' 'loki.yaml'
Write-Template 'grafana.ini' 'grafana.ini'
Write-Template 'datasource.yaml' 'grafana-provisioning/datasources/loki.yaml'
Write-Text (Join-Path $Root 'alloy/config.alloy') ([System.IO.File]::ReadAllText((Join-Path $Kit 'alloy/config.alloy')))

$Password = Join-Path $Root 'admin-password.txt'
if (-not (Test-Path -LiteralPath $Password)) {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    Write-Text $Password (([Convert]::ToBase64String($bytes)) -replace '[+/=]', 'x')
}

# gcx resources (for gcx push) and the provisioning copies (for -DashboardMode Files).
Get-ChildItem -LiteralPath (Join-Path $Root 'resources') -Recurse -File | Remove-Item -Force
Copy-Item -Path (Join-Path $Kit 'resources/*') -Destination (Join-Path $Root 'resources') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $Root 'dashboards') -File | Remove-Item -Force
Copy-Item -Path (Join-Path $Kit 'provisioning/dashboards/*.json') -Destination (Join-Path $Root 'dashboards') -Force
$Provider = Join-Path $Root 'grafana-provisioning/dashboards/offlinedaoc.yaml'
if ($DashboardMode -eq 'Files') { Write-Template 'dashboards.yaml' 'grafana-provisioning/dashboards/offlinedaoc.yaml' }
elseif (Test-Path -LiteralPath $Provider) { Remove-Item -LiteralPath $Provider -Force }

foreach ($f in @('observability.ps1', 'start.cmd', 'stop.cmd', 'status.cmd', 'uninstall.cmd', 'gcx.cmd')) {
    Copy-Item -LiteralPath (Join-Path $Here $f) -Destination (Join-Path $Root $f) -Force
}

$settings = [ordered]@{
    InstallRoot = $Server; LogsDir = $LogsDir; Root = $Root; DashboardMode = $DashboardMode
    Grafana = $Exes['grafana']; Loki = $Exes['loki']; Alloy = $Exes['alloy']; Gcx = $Exes['gcx']
    Versions = [ordered]@{}
}
foreach ($r in $Releases) { $settings.Versions[$r.Name] = $r.Version }
Write-Text (Join-Path $Root 'settings.json') ($settings | ConvertTo-Json -Depth 4)
Say "configs written under $Root"
if ($ConfigOnly) { return }

# ---- Start, token, gcx -----------------------------------------------------------------------------------------------
& (Join-Path $Root 'observability.ps1') -Action start -NoBrowser
if ($DashboardMode -ne 'Files') {
    try {
        & (Join-Path $Root 'observability.ps1') -Action gcx-login
        if (-not $?) { throw 'gcx login failed' }
    }
    catch {
        if ($DashboardMode -eq 'Gcx') { throw }
        Say "gcx did not work ($($_.Exception.Message)); file-provisioning the dashboards instead"
        Write-Template 'dashboards.yaml' 'grafana-provisioning/dashboards/offlinedaoc.yaml'
        & (Join-Path $Root 'observability.ps1') -Action stop
        & (Join-Path $Root 'observability.ps1') -Action start -NoBrowser
    }
}
Say "ready: http://127.0.0.1:3000 (user admin, password in $Password)"
if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:3000' }
