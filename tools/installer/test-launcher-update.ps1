# End-to-end check of the launcher's Living Server update path on Windows (CI: installer.yml, job launcher-update).
#
#   powershell -File tools\installer\test-launcher-update.ps1 -Launcher <built OfflineDAoC.exe> -Work C:\lsupdate
#
# A fake Offline DAoC 0.37 game folder (the layout tools\fork-build\test-install-scripts.ps1 uses: no real game
# download) gets the published -From package installed with its own install script, as a player would. Then the
# launcher built from this branch runs its update path headless (--apply-update, the GET UPDATE button's steps
# without the window or the dialog) with the published -To package:
#   - a wrong SHA-256 and a missing one are refused and change nothing;
#   - a running game program (a stand-in named connect.exe) blocks the update;
#   - with GitHub's digest it unpacks the package into the game folder, hands over to apply-update.ps1, which waits
#     for the launcher to close and runs the package's INSTALL.cmd; the server, the launcher and the install record
#     are then the -To build's, BUILD-INFO.txt names it, and the package is kept in runtime\updates;
#   - the package's ROLLBACK.cmd then puts the -From build back.
# Not covered here: the window (GET UPDATE, the dialog, the progress line), the launcher opening again (-Restart is
# left empty), and a real game folder. See docs\KNOWN-ISSUES.md.
param(
    [Parameter(Mandatory = $true)][string]$Launcher,
    [string]$Work = 'C:\lsupdate',
    [string]$From = 'v1.1-beta',
    [string]$To = 'v1.1.1-beta'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest's progress bar makes Windows PowerShell 5.1 downloads crawl
$Launcher = (Resolve-Path $Launcher).Path
$failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  PASS  $what" -ForegroundColor Green; return }
    Write-Host "  FAIL  $what" -ForegroundColor Red
    $failures.Add($what)
    if ($env:GITHUB_ACTIONS) { Write-Host ("::error title=Launcher update test::" + (($what -replace "`r", '') -replace "`n", ' | ')) }
}
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Info([string]$path) {
    $info = @{}
    foreach ($line in Get-Content -LiteralPath $path) { if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $info[$Matches[1]] = $Matches[2] } }
    return $info
}

if (Test-Path $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
$downloads = Join-Path $Work 'downloads'
$logs = Join-Path $Work 'logs'
New-Item -ItemType Directory -Force -Path $downloads, $logs | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# The two published packages, checked against the digests GitHub's API gives (as the launcher does).
$headers = @{ 'User-Agent' = 'OfflineDAoC-LivingServer-tests' }
if ($env:GH_TOKEN) { $headers['Authorization'] = "Bearer $env:GH_TOKEN" }
$packages = @{}
foreach ($tag in @($From, $To)) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/chris4aker/OfflineDAoC-LivingServer/releases/tags/$tag" -Headers $headers
    $name = "OfflineDAoC-LivingServer-$($tag.TrimStart('v')).zip"
    $asset = @($release.assets | Where-Object { $_.name -eq $name })[0]
    if (!$asset) { throw "The $tag release has no $name." }
    $zip = Join-Path $downloads $name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'OfflineDAoC-LivingServer-tests' }
    $digest = ($asset.digest -replace '^sha256:', '').ToLowerInvariant()
    Check ($digest.Length -eq 64 -and (Sha $zip) -eq $digest) "$name matches GitHub's digest $($asset.digest)"
    $packages[$tag] = [pscustomobject]@{ Zip = $zip; Digest = $digest; Build = $tag.TrimStart('v'); Folder = "LivingServer-$($tag.TrimStart('v'))" }
}
$fromPkg = $packages[$From]; $toPkg = $packages[$To]

# A fake 0.37 game (a game updated by the 0.37 patch: release notes in its manifest, no editions folder).
$game = Join-Path $Work 'game'
$lines = @()
foreach ($folder in @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')) {
    $dir = Join-Path $game $folder
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $dir 'GameServer.dll'), 'original release server')
    [IO.File]::WriteAllText((Join-Path $dir 'GameServer.pdb'), 'original release symbols')
    $lines += "$(Sha (Join-Path $dir 'GameServer.dll'))  $folder/GameServer.dll"
    $lines += "$(Sha (Join-Path $dir 'GameServer.pdb'))  $folder/GameServer.pdb"
}
foreach ($name in @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')) {
    [IO.File]::WriteAllText((Join-Path $game "runtime\$name"), "original launcher $name")
    $lines += "$(Sha (Join-Path $game "runtime\$name"))  runtime/$name"
}
foreach ($version in @('0.36', '0.37')) {
    $notes = Join-Path $game "docs\RELEASE-$version.md"
    New-Item -ItemType Directory -Path (Split-Path -Parent $notes) -Force | Out-Null
    [IO.File]::WriteAllText($notes, "release $version")
    $lines += "$(Sha $notes)  docs/RELEASE-$version.md"
}
New-Item -ItemType Directory -Path (Join-Path $game 'runtime\data') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $game 'runtime\data\opendaoc.sqlite3.db'), 'the player''s world')
$world = Sha (Join-Path $game 'runtime\data\opendaoc.sqlite3.db')
Set-Content -LiteralPath (Join-Path $game 'PACKAGE MANIFEST.sha256') -Value $lines -Encoding UTF8

# The -From build, installed by hand: unzipped into the game folder, its installer run.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($fromPkg.Zip, $game)
$fromInfo = Read-Info (Join-Path $game "$($fromPkg.Folder)\BUILD-INFO.txt")
$out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $game "$($fromPkg.Folder)\install-server-build.ps1") -GameFolder $game 2>&1 | Out-String
Check ($LASTEXITCODE -eq 0 -and (Sha (Join-Path $game 'runtime\server\GameServer.dll')) -eq $fromInfo['GameServer.dll']) "the $From package installs on the fake game`n$out"

function Launch([string]$name, [string[]]$arguments) {
    $all = @('--apply-update') + $arguments + @('--game', $game, '--no-restart', '--no-pause')
    $p = Start-Process -FilePath $Launcher -ArgumentList ($all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -Wait -PassThru
    Write-Host "   OfflineDAoC.exe $($all -join ' ')  -> exit $($p.ExitCode)"
    return $p.ExitCode
}
$updateLog = Join-Path $game 'runtime\logs\launcher-updates.log'
$serverBefore = Sha (Join-Path $game 'runtime\server\GameServer.dll')

# Refusals: nothing changes.
$code = Launch 'wrong-digest' @($toPkg.Zip, '--sha256', ('0' * 64))
Check ($code -eq 2 -and (Sha (Join-Path $game 'runtime\server\GameServer.dll')) -eq $serverBefore -and !(Test-Path (Join-Path $game $toPkg.Folder))) 'a wrong SHA-256 is refused and nothing changes'
$code = Launch 'no-digest' @($toPkg.Zip)
Check ($code -eq 2 -and !(Test-Path (Join-Path $game $toPkg.Folder))) 'a package without a SHA-256 is refused'
$fake = Join-Path $Work 'connect.exe'
Copy-Item "$env:SystemRoot\System32\ping.exe" $fake -Force
$running = Start-Process -FilePath $fake -ArgumentList '-n 60 127.0.0.1' -PassThru -WindowStyle Hidden
Start-Sleep 2
$code = Launch 'while-running' @($toPkg.Zip, '--sha256', $toPkg.Digest)
Stop-Process -Id $running.Id -Force -ErrorAction SilentlyContinue
Start-Sleep 1
Check ($code -eq 3 -and !(Test-Path (Join-Path $game $toPkg.Folder))) 'a running game program blocks the update'

# The update.
$code = Launch 'update' @($toPkg.Zip, '--sha256', $toPkg.Digest)
Check ($code -eq 0) 'the launcher hands the update over (exit 0)'
$result = Join-Path $game 'runtime\updates\last-update.txt'
$deadline = (Get-Date).AddMinutes(5)
while (!(Test-Path $result) -and (Get-Date) -lt $deadline) { Start-Sleep 1 }
Check (Test-Path $result) 'apply-update.ps1 wrote its result'
$r = if (Test-Path $result) { Read-Info $result } else { @{} }
Write-Host "   result: $($r['Result']): $($r['Detail'])"
Check ($r['Result'] -eq 'installed' -and $r['Build'] -eq $toPkg.Build) "the result says $($toPkg.Build) is installed"
$toInfo = Read-Info (Join-Path $game "$($toPkg.Folder)\BUILD-INFO.txt")
Check ($toInfo['BuildName'] -eq $toPkg.Build) "BUILD-INFO.txt in the game's $($toPkg.Folder) folder names $($toPkg.Build)"
foreach ($folder in @('runtime\server', 'runtime\server\lib', 'runtime\server\win-x64')) {
    Check ((Sha (Join-Path $game "$folder\GameServer.dll")) -eq $toInfo['GameServer.dll']) "$folder\GameServer.dll is the $($toPkg.Build) build"
}
Check ((Sha (Join-Path $game 'runtime\OfflineDAoC.dll')) -eq $toInfo['launcher/OfflineDAoC.dll']) "the launcher is the $($toPkg.Build) build"
$record = Get-ChildItem (Join-Path $game 'runtime\server\deployment-backups') -Directory -Filter 'fork-build-*' | Sort-Object Name | Select-Object -Last 1
Check ($record -and (Get-Content (Join-Path $record.FullName 'install-record.json') -Raw | ConvertFrom-Json).BuildName -eq $toPkg.Build) 'the newest install record names it'
Check (Test-Path (Join-Path $game "runtime\updates\$(Split-Path -Leaf $toPkg.Zip)")) 'the package is kept in runtime\updates'
Check (Test-Path (Join-Path $game "$($fromPkg.Folder)\INSTALL.cmd")) "the $From package folder is still there to reinstall"
Check ((Sha (Join-Path $game 'runtime\data\opendaoc.sqlite3.db')) -eq $world) 'the database was not touched'
$logText = if (Test-Path $updateLog) { Get-Content -LiteralPath $updateLog -Raw } else { '' }
Check ($logText -match 'Waiting for the launcher' -and $logText -match 'Running INSTALL\.cmd' -and $logText -match 'Handing over') 'every step is in runtime\logs\launcher-updates.log'

# ROLLBACK.cmd, as a player would run it (its pause gets no key).
$rollback = Join-Path $game "$($toPkg.Folder)\ROLLBACK.cmd"
$out = & cmd.exe /d /c "`"$rollback`" -GameFolder `"$game`" < NUL" 2>&1 | Out-String
Check ((Sha (Join-Path $game 'runtime\server\GameServer.dll')) -eq $fromInfo['GameServer.dll'] -and
       (Sha (Join-Path $game 'runtime\OfflineDAoC.dll')) -eq $fromInfo['launcher/OfflineDAoC.dll']) "ROLLBACK.cmd puts the $From build back`n$out"

Copy-Item -LiteralPath $updateLog -Destination $logs -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $game 'runtime\updates') -Destination (Join-Path $logs 'updates') -Recurse -Exclude '*.zip' -ErrorAction SilentlyContinue
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host 'All launcher update checks passed.' -ForegroundColor Green
exit 0
