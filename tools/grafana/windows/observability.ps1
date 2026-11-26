<#
.SYNOPSIS
  Start, stop, check or remove the portable Grafana + Loki + Alloy (issue #628). start.cmd, stop.cmd, status.cmd and
  uninstall.cmd call this; it lives in the observability folder that setup.ps1 made, and reads its settings.json.

  gcx-login makes a Grafana service-account token (account "gcx-local"), logs gcx in with it (context "local", config
  in observability\gcx, plaintext storage so nothing goes to the Windows credential store) and pushes the dashboards
  in observability\resources. Run it again when the token is lost; each run makes a new token.

  Each service runs as a plain background process of this user (no service, no admin), listening on 127.0.0.1 only,
  with its output in observability\logs and its process id in observability\run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('start', 'stop', 'status', 'gcx-login', 'uninstall')][string]$Action,
    [switch]$NoBrowser,
    [switch]$Yes
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Settings = Get-Content -Raw -LiteralPath (Join-Path $Root 'settings.json') | ConvertFrom-Json

function Say([string]$text) { Write-Host "[observability] $text" }

# Name, executable, arguments, health URL, the 127.0.0.1 ports it takes.
$Services = @(
    @{ Name = 'loki'; Exe = $Settings.Loki; Ready = 'http://127.0.0.1:3100/ready'; Ports = @(3100, 9096)
       Args = @("-config.file=$Root/loki.yaml") },
    @{ Name = 'alloy'; Exe = $Settings.Alloy; Ready = 'http://127.0.0.1:12345/-/ready'; Ports = @(12345)
       Args = @('run', '--disable-reporting', '--server.http.listen-addr=127.0.0.1:12345', "--storage.path=$Root/data/alloy",
                "$Root/alloy/config.alloy") },
    @{ Name = 'grafana'; Exe = $Settings.Grafana; Ready = 'http://127.0.0.1:3000/api/health'; Ports = @(3000)
       Args = @('server', '--homepath', $(if ($Settings.Grafana) { Split-Path -Parent (Split-Path -Parent $Settings.Grafana) } else { '' }),
                '--config', "$Root/grafana.ini") }
)

function Get-Running($svc) {
    $pidFile = Join-Path $Root "run/$($svc.Name).pid"
    if (-not (Test-Path -LiteralPath $pidFile)) { return $null }
    $id = [int](Get-Content -Raw -LiteralPath $pidFile)
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    # Only a process started from this folder's executable counts (a reused process id is someone else's).
    if ($p -and $p.Path -and ($p.Path -ieq $svc.Exe)) { return $p }
    Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
    return $null
}

function Test-PortFree([int]$port) {
    $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    try { $l.Start(); return $true } catch { return $false } finally { try { $l.Stop() } catch { } }
}

function Test-Ready([string]$url) {
    try { $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Uri $url; return ($r.StatusCode -eq 200) } catch { return $false }
}

function Start-All {
    $env:OFFLINEDAOC_LOGS = $Settings.LogsDir
    $env:LOKI_URL = 'http://127.0.0.1:3100/loki/api/v1/push'
    if (-not $env:OFFLINEDAOC_BUILD) { $env:OFFLINEDAOC_BUILD = 'local' }
    foreach ($svc in $Services) {
        if (Get-Running $svc) { Say "$($svc.Name) is already running"; continue }
        foreach ($port in $svc.Ports) {
            if (-not (Test-PortFree $port)) { throw "127.0.0.1:$port is taken by another program; $($svc.Name) needs it." }
        }
        $out = Join-Path $Root "logs/$($svc.Name).out.log"
        $err = Join-Path $Root "logs/$($svc.Name).err.log"
        $quoted = $svc.Args | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
        $start = @{ FilePath = $svc.Exe; ArgumentList = $quoted; WorkingDirectory = $Root; PassThru = $true
                    RedirectStandardOutput = $out; RedirectStandardError = $err }
        # No console window on Windows (PowerShell 5.1 is always Windows; -WindowStyle exists only there).
        if ($PSVersionTable.PSEdition -ne 'Core' -or $IsWindows) { $start.WindowStyle = 'Hidden' }
        $p = Start-Process @start
        Set-Content -LiteralPath (Join-Path $Root "run/$($svc.Name).pid") -Value $p.Id -Encoding ASCII
        Say "$($svc.Name) started (process $($p.Id))"
    }
    foreach ($svc in $Services) {
        $deadline = (Get-Date).AddSeconds(120)
        while (-not (Test-Ready $svc.Ready)) {
            if ((Get-Date) -gt $deadline) { throw "$($svc.Name) did not get ready; see $Root/logs/$($svc.Name).err.log" }
            if (-not (Get-Running $svc)) { throw "$($svc.Name) stopped; see $Root/logs/$($svc.Name).err.log" }
            Start-Sleep -Seconds 2
        }
        Say "$($svc.Name) ready"
    }
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:3000' }
}

function Stop-All {
    [array]::Reverse($Services)
    foreach ($svc in $Services) {
        $p = Get-Running $svc
        if ($p) { Stop-Process -Id $p.Id -Force; Say "$($svc.Name) stopped" } else { Say "$($svc.Name) was not running" }
        Remove-Item -LiteralPath (Join-Path $Root "run/$($svc.Name).pid") -Force -ErrorAction SilentlyContinue
    }
}

function Connect-Gcx {
    $pw = [System.IO.File]::ReadAllText((Join-Path $Root 'admin-password.txt'))
    $h = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:$pw")) }
    $api = 'http://127.0.0.1:3000/api'
    $found = Invoke-RestMethod -UseBasicParsing -Headers $h -Uri "$api/serviceaccounts/search?query=gcx-local"
    $sa = $found.serviceAccounts | Where-Object { $_.name -eq 'gcx-local' } | Select-Object -First 1
    if (-not $sa) {
        $sa = Invoke-RestMethod -UseBasicParsing -Headers $h -Method Post -ContentType 'application/json' `
            -Uri "$api/serviceaccounts" -Body '{"name":"gcx-local","role":"Admin"}'
    }
    $body = '{"name":"gcx-' + (Get-Date -Format 'yyyyMMddHHmmss') + '"}'
    $token = (Invoke-RestMethod -UseBasicParsing -Headers $h -Method Post -ContentType 'application/json' `
        -Uri "$api/serviceaccounts/$($sa.id)/tokens" -Body $body).key
    $env:GCX_CONFIG = Join-Path $Root 'gcx/config.yaml'
    $env:GCX_KEYCHAIN = 'off'
    $env:GCX_AGENT_MODE = 'off'
    & $Settings.Gcx login local --yes --allow-server-override --server http://127.0.0.1:3000 --token $token | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "gcx login failed ($LASTEXITCODE)" }
    & $Settings.Gcx resources push -p (Join-Path $Root 'resources') -o text
    if ($LASTEXITCODE -ne 0) { throw "gcx resources push failed ($LASTEXITCODE)" }
    Say 'gcx logged in (context local); dashboards pushed. Update them with: gcx.cmd resources push -p resources'
}

switch ($Action) {
    'gcx-login' { Connect-Gcx }
    'start' { Start-All }
    'stop' { Stop-All }
    'status' {
        foreach ($svc in $Services) {
            $p = Get-Running $svc
            $state = if ($p) { "running (process $($p.Id))" } else { 'stopped' }
            $ready = if (Test-Ready $svc.Ready) { 'ready' } else { 'not answering' }
            Say ("{0,-8} {1}, {2}" -f $svc.Name, $state, $ready)
        }
        Say "server logs read from $($Settings.LogsDir)"
    }
    'uninstall' {
        if (-not $Yes) {
            $answer = Read-Host "Delete $Root and everything in it (stored rows, password, downloads)? Type DELETE"
            if ($answer -ne 'DELETE') { Say 'nothing deleted'; return }
        }
        Stop-All
        Start-Sleep -Seconds 2
        Set-Location -LiteralPath (Split-Path -Parent $Root)
        Remove-Item -LiteralPath $Root -Recurse -Force
        Say "deleted $Root"
    }
}
