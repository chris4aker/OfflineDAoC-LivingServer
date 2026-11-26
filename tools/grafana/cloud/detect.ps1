<#
.SYNOPSIS
  Read-only detect step of the Grafana Cloud runbook on Windows (issue #649). Windows PowerShell 5.1 or PowerShell 7.
.DESCRIPTION
  Prints one JSON object for the agent: the server's logs folder, any Alloy or Grafana Agent already on the PC, gcx,
  a free loopback port, the env file's state and the setup path (A-D). It changes nothing.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\grafana\cloud\detect.ps1 -HasAccount yes
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\grafana\cloud\detect.ps1 -CheckEnv "$env:LOCALAPPDATA\LivingServerMonitoring\monitoring.env"
#>
[CmdletBinding()]
param(
    [ValidateSet('yes', 'no', 'unknown')][string]$HasAccount = 'unknown',
    [string]$Logs = '',
    [string]$EnvFile = '',
    [string]$AddonHome = '',
    [string]$CheckEnv = ''
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$RequiredKeys = @('GC_PROM_URL', 'GC_PROM_USER', 'GC_LOKI_URL', 'GC_LOKI_USER', 'GC_TOKEN', 'LS_LOG_DIR', 'LS_INSTANCE')
$TaskName = 'LivingServerAlloy'
if (-not $AddonHome) { $AddonHome = Join-Path $env:LOCALAPPDATA 'LivingServerMonitoring' }
if (-not $EnvFile) { $EnvFile = Join-Path $AddonHome 'monitoring.env' }

function Get-MissingEnvKeys([string]$Path) {
    # Names of missing or empty keys only; values are never printed.
    $values = @{}
    foreach ($line in (Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue)) {
        if ($line -match '^\s*([A-Z_][A-Z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim().Trim('"') }
    }
    $missing = @()
    foreach ($k in $RequiredKeys) { if (-not $values.ContainsKey($k) -or -not $values[$k]) { $missing += $k } }
    return , $missing
}

function Test-Cmd([string]$Name) { return [bool](Get-Command $Name -ErrorAction SilentlyContinue) }
# Windows-only cmdlets are guarded so the script also runs (for its tests) under PowerShell 7 on Linux.
function Get-ServiceSafe([string]$Name) { if (Test-Cmd 'Get-Service') { return Get-Service -Name $Name -ErrorAction SilentlyContinue }; return $null }

if ($CheckEnv) {
    if (-not (Test-Path -LiteralPath $CheckEnv)) { [Console]::Error.WriteLine("env file not found: $CheckEnv"); exit 1 }
    $missing = Get-MissingEnvKeys $CheckEnv
    if ($missing.Count -gt 0) { [Console]::Error.WriteLine("missing or empty: " + ($missing -join ' ')); exit 1 }
    Write-Output "env ok: all $($RequiredKeys.Count) keys set"
    exit 0
}


# ---- Logs folder ----
$logsFound = @()
if ($Logs) {
    if (Test-Path -LiteralPath (Join-Path $Logs 'server.log')) { $logsFound += $Logs }
} else {
    $patterns = @('C:\Games\*\runtime\server\logs', 'C:\Games\*\*\runtime\server\logs',
        (Join-Path $env:USERPROFILE 'Games\*\runtime\server\logs'), (Join-Path $env:USERPROFILE 'Games\*\*\runtime\server\logs'),
        'D:\Games\*\runtime\server\logs', 'D:\Games\*\*\runtime\server\logs')
    foreach ($p in $patterns) {
        foreach ($d in (Get-Item -Path $p -ErrorAction SilentlyContinue)) {
            if (Test-Path -LiteralPath (Join-Path $d.FullName 'server.log')) { $logsFound += $d.FullName }
        }
    }
}

# ---- Alloy and Grafana Agent ----
$alloySvc = Get-ServiceSafe 'Alloy'
$alloySystem = [bool]($alloySvc -and $alloySvc.Status -eq 'Running')
$alloyBin = ''
$cmd = Get-Command 'alloy' -ErrorAction SilentlyContinue
if ($cmd) { $alloyBin = $cmd.Source }
elseif (Test-Path 'C:\Program Files\GrafanaLabs\Alloy\alloy-windows-amd64.exe') { $alloyBin = 'C:\Program Files\GrafanaLabs\Alloy\alloy-windows-amd64.exe' }
$otherAlloy = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like 'alloy*' -and -not ($_.Path -and $_.Path.StartsWith($AddonHome, [StringComparison]::OrdinalIgnoreCase)) }).Count
$agentSvc = Get-ServiceSafe 'Grafana Agent*' | Select-Object -First 1
$agentInstalled = [bool]($agentSvc -or (Test-Path 'C:\Program Files\Grafana Agent'))
$agentRunning = [bool]($agentSvc -and $agentSvc.Status -eq 'Running')
$existingCollector = [bool]($alloySystem -or $alloyBin -or $otherAlloy -gt 0 -or $agentInstalled)

$kitTask = $false
if (Test-Cmd 'Get-ScheduledTask') { $kitTask = [bool](Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) }
$startupDir = [Environment]::GetFolderPath('Startup')
$kitInstalled = $kitTask -or ($startupDir -and (Test-Path (Join-Path $startupDir "$TaskName.lnk")))
$kitRunning = [bool](Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like 'alloy*' -and $_.Path -and $_.Path.StartsWith($AddonHome, [StringComparison]::OrdinalIgnoreCase) })

# ---- gcx ----
$gcx = Test-Cmd 'gcx'; $gcxVersion = ''; $gcxLoggedIn = $false
if ($gcx) {
    try { $gcxVersion = (& gcx --version 2>$null | Select-Object -First 1) } catch { }
    try { & gcx config check *> $null; $gcxLoggedIn = ($LASTEXITCODE -eq 0) } catch { }
}

# ---- A free loopback port ----
$listening = @()
try { $listening = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | ForEach-Object { $_.LocalPort }) }
catch {
    try { $listening = @(netstat -an | Select-String 'LISTEN' | ForEach-Object { (($_.Line.Trim()) -split '\s+')[1] -replace '.*[:.]', '' }) } catch { }
}
$freePort = $null
foreach ($p in 12347..12360) { if ($listening -notcontains $p -and $listening -notcontains "$p") { $freePort = $p; break } }

# ---- Env file ----
$envExists = Test-Path -LiteralPath $EnvFile
if ($envExists) { $missing = Get-MissingEnvKeys $EnvFile } else { $missing = $RequiredKeys }

# ---- Path ----
$path = $null; $next = 'ask_has_account'
if ($HasAccount -eq 'no') { if ($existingCollector) { $path = 'B' } else { $path = 'A' } }
if ($HasAccount -eq 'yes') { if ($existingCollector) { $path = 'D' } else { $path = 'C' } }
if ($path) {
    if ($kitRunning) { $next = 'verify' }
    elseif ($logsFound.Count -eq 0) { $next = 'ask_logs_folder' }
    elseif (-not $envExists -or $missing.Count -gt 0) { $next = 'user_credentials' }
    else { $next = 'install' }
}

$osName = [Environment]::OSVersion.VersionString
if (Test-Cmd 'Get-CimInstance') { try { $osName = [string](Get-CimInstance Win32_OperatingSystem).Caption } catch { } }
$tz = ''
try { $tz = [System.TimeZoneInfo]::Local.Id } catch { }
[ordered]@{
    platform          = 'windows'
    os                = $osName
    powershell        = $PSVersionTable.PSVersion.ToString()
    logs_dirs         = @($logsFound)
    timezone          = $tz
    alloy             = [ordered]@{ binary = $alloyBin; system_service_active = $alloySystem; other_processes = $otherAlloy }
    grafana_agent     = [ordered]@{ installed = $agentInstalled; service_active = $agentRunning }
    existing_collector = $existingCollector
    kit               = [ordered]@{ home = $AddonHome; installed = $kitInstalled; running = $kitRunning }
    gcx               = [ordered]@{ installed = $gcx; version = $gcxVersion; logged_in = $gcxLoggedIn }
    free_port         = $freePort
    env               = [ordered]@{ file = $EnvFile; exists = $envExists; missing_keys = @($missing) }
    has_account       = $HasAccount
    path              = $path
    next              = $next
} | ConvertTo-Json -Depth 4
