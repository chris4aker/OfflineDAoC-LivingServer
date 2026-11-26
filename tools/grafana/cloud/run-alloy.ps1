# Starts this kit's Alloy on Windows (issue #649); copied next to monitoring.env by install.ps1 and started at logon.
# Loads monitoring.env into this process only (nothing is printed) and restarts Alloy if it exits.
$ErrorActionPreference = 'Stop'
$home_ = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $home_
foreach ($line in Get-Content -LiteralPath (Join-Path $home_ 'monitoring.env')) {
    if ($line -match '^\s*([A-Z_][A-Z0-9_]*)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2].Trim().Trim('"'), 'Process') }
}
if ($env:GC_LOKI_URL -and $env:GC_LOKI_URL -notmatch '/loki/api/v1/push$') { $env:GC_LOKI_URL = $env:GC_LOKI_URL.TrimEnd('/') + '/loki/api/v1/push' }
if (-not $env:LS_ALLOY_ADDR) { $env:LS_ALLOY_ADDR = '127.0.0.1:12347' }
$log = Join-Path $home_ 'logs\alloy.log'
while ($true) {
    $p = Start-Process -FilePath (Join-Path $home_ 'bin\alloy.exe') -NoNewWindow -PassThru -Wait `
        -ArgumentList @('run', 'config', "--server.http.listen-addr=$($env:LS_ALLOY_ADDR)", '--storage.path=data', '--disable-reporting') `
        -RedirectStandardError $log
    Start-Sleep -Seconds 30
}
