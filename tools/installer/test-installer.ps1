# End-to-end checks of the Living Server Setup exe on a real Windows PC (GitHub's windows runners in CI):
# a real download of Offline DAoC from shadowofze's releases, then every path Setup takes with it.
#
#   powershell -File tools\installer\test-installer.ps1 -Exe dist\LivingServer-Setup-1.0-beta.exe -Package payload\LivingServer.zip -Work C:\lstest
#
# Scenarios (all by default, in this order, sharing one download cache):
#   fresh     new install of the b edition with Living Server; scan; run again (already installed); refuse while the
#             game runs; remove; reinstall; refuse a hand-changed server, then -Force
#   update036 shadowofze's 0.36b installed with his own downloader, the 0.36 Social Expansion build on top, then Setup:
#             released server back, official update to 0.37, Living Server
#   client    new install with the 0.37c new client (about 33 GB), Living Server in both launchers
#   resume    Stop part-way through the download; kill Setup mid-download and mid-unpack (a crash); then finish
#   import    shadowofze's 0.35b, then a new install that brings its progress over
#   freshcopy a 0.36b game with a game file changed by hand: the update refuses, then a fresh copy with the progress
#   netfx     .NET Framework 3.5 turned on through Setup (informational where the runner can't turn it off first)
#   smoke     the installed game's server starts with Living Server (FORK_BUILD in its log) and a login works
#   ui        the real window, driven page by page through a new install (--autopilot), with pictures
param(
    [Parameter(Mandatory)] [string]$Exe,
    [Parameter(Mandatory)] [string]$Package,
    [string]$Work = 'C:\lstest',
    [string[]]$Scenario = @('fresh', 'update036'),
    [string]$Shots = ''
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$Package = (Resolve-Path $Package).Path
$root = Join-Path $Work 'games'
$logs = Join-Path $Work 'logs'
New-Item -ItemType Directory -Force -Path $root, $logs | Out-Null
$failures = New-Object System.Collections.Generic.List[string]
$script:n = 0

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  PASS  $what" -ForegroundColor Green }
    else { Write-Host "  FAIL  $what" -ForegroundColor Red; $failures.Add($what) }
}

function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }

# BUILD-INFO.txt of the package the exe embeds
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($Package)
$info = @{}
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -match '^[^/]+/BUILD-INFO\.txt$' } | Select-Object -First 1
    $reader = New-Object IO.StreamReader($entry.Open())
    foreach ($line in ($reader.ReadToEnd() -split "`r?`n")) { if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $info[$Matches[1]] = $Matches[2] } }
    $reader.Dispose()
    $pkgRoot = $entry.FullName.Split('/')[0]
} finally { $zip.Dispose() }
Write-Host "Package $($info['BuildName']) for $($info['BaseRelease']) (root folder $pkgRoot)"

function Setup([string]$name, [string[]]$argList) {
    $script:n++
    $log = Join-Path $logs ('{0:00}-{1}.log' -f $script:n, $name)
    $all = @('--silent') + $argList + @('--log', $log)
    Write-Host ''
    Write-Host "== $name : Setup $($all -join ' ')" -ForegroundColor Cyan
    $start = Get-Date
    $p = Start-Process -FilePath $Exe -ArgumentList ($all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -Wait -PassThru -NoNewWindow
    $text = if (Test-Path $log) { Get-Content -LiteralPath $log -Raw } else { '' }
    $tail = ($text -split "`r?`n" | Where-Object { $_ -match 'STEP |RESULT |\[ *\d+%\]|FAILED|NOTHING WAS CHANGED' } | Select-Object -Last 40) -join "`n"
    Write-Host $tail
    Write-Host ("   exit {0} after {1:N0} s" -f $p.ExitCode, ((Get-Date) - $start).TotalSeconds)
    return [pscustomobject]@{ Code = $p.ExitCode; Log = $text }
}

function Start-Setup([string]$name, [string[]]$argList) {
    $script:n++
    $log = Join-Path $logs ('{0:00}-{1}.log' -f $script:n, $name)
    $all = @('--silent') + $argList + @('--log', $log)
    Write-Host ''
    Write-Host "== $name : Setup $($all -join ' ') (in the background)" -ForegroundColor Cyan
    $p = Start-Process -FilePath $Exe -ArgumentList ($all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -PassThru -NoNewWindow
    return [pscustomobject]@{ Process = $p; Log = $log }
}

# Kills Setup with everything it started (PowerShell, curl, the patcher), like a crash or a power cut.
function Crash($run, [string]$pattern, [int]$timeoutSec = 900) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline -and !$run.Process.HasExited) {
        if ((Test-Path $run.Log) -and ((Get-Content -LiteralPath $run.Log -Raw) -match $pattern)) { break }
        Start-Sleep -Milliseconds 300
    }
    $hit = (Test-Path $run.Log) -and ((Get-Content -LiteralPath $run.Log -Raw) -match $pattern)
    if (!$run.Process.HasExited) { & taskkill.exe /PID $run.Process.Id /T /F | Out-Null }
    Start-Sleep 2
    Write-Host "   killed Setup when its log matched '$pattern': $hit"
    return $hit
}

function Assert-LivingServer([string]$game, [string]$what) {
    foreach ($f in 'runtime\server', 'runtime\server\lib', 'runtime\server\win-x64') {
        $dll = Join-Path $game "$f\GameServer.dll"
        if (Test-Path $dll) { Check ((Sha $dll) -eq $info['GameServer.dll'].ToLowerInvariant()) "$what : $f\GameServer.dll is the Living Server build" }
    }
    Check ((Sha (Join-Path $game 'runtime\OfflineDAoC.dll')) -eq $info['launcher/OfflineDAoC.dll'].ToLowerInvariant()) "$what : the launcher has the Living Server tab"
    if (Test-Path (Join-Path $game 'new-client\runtime\OfflineDAoC.dll')) {
        Check ((Sha (Join-Path $game 'new-client\runtime\OfflineDAoC.dll')) -eq $info['launcher/OfflineDAoC.dll'].ToLowerInvariant()) "$what : the new client's launcher has the Living Server tab"
    }
    Check (Test-Path (Join-Path $game "$pkgRoot\ROLLBACK.cmd")) "$what : the package folder (with ROLLBACK.cmd) is in the game"
    $rec = Get-ChildItem (Join-Path $game 'runtime\server\deployment-backups') -Directory -Filter 'fork-build-*' | Sort-Object Name | Select-Object -Last 1
    Check ($rec -and (Get-Content (Join-Path $rec.FullName 'install-record.json') -Raw | ConvertFrom-Json).BuildName -eq $info['BuildName']) "$what : the install record names $($info['BuildName'])"
}

function Released-ServerHash([string]$game) {
    foreach ($line in Get-Content -LiteralPath (Join-Path $game 'PACKAGE MANIFEST.sha256')) {
        if ($line -match '^([0-9a-fA-F]{64})\s+runtime/server/GameServer\.dll$') { return $Matches[1].ToLowerInvariant() }
    }
}

function Newest-Release([string]$game) {
    $v = Get-Content -LiteralPath (Join-Path $game 'PACKAGE MANIFEST.sha256') | ForEach-Object { if ($_ -match 'docs/RELEASE-(\d+\.\d+)\.md$') { [version]$Matches[1] } } | Sort-Object | Select-Object -Last 1
    return "$v"
}

$base = $info['BaseRelease']
$fresh = Join-Path $root "playable-v$($base)b"
Get-PSDrive -PSProvider FileSystem | Format-Table Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}, @{n='UsedGB';e={[math]::Round($_.Used/1GB,1)}} | Out-String | Write-Host

if ($Scenario -contains 'fresh') {
    $r = Setup 'fresh-install' @('--new', '--dest', $fresh)
    Check ($r.Code -eq 0) 'fresh: Setup finished'
    Check ((Newest-Release $fresh) -eq $base) "fresh: the game is Offline DAoC $base"
    Check (!(Test-Path (Join-Path $fresh 'EDITION.txt'))) 'fresh: the b edition (no EDITION.txt)'
    Assert-LivingServer $fresh 'fresh'
    $desk = [Environment]::GetFolderPath('DesktopDirectory')
    Check (Test-Path (Join-Path $desk 'Offline DAoC.lnk')) 'fresh: desktop shortcut'
    Check (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'Offline DAoC\Offline DAoC.lnk')) 'fresh: Start menu shortcut'
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desk 'Offline DAoC.lnk'))
    Check ($lnk.IconLocation -like '*runtime\living-server.ico*' -and (Test-Path (Join-Path $fresh 'runtime\living-server.ico'))) "fresh: the shortcut has Living Server's icon ($($lnk.IconLocation))"
    $setupDir = Join-Path $root 'Offline DAoC setup files'
    Check (Test-Path (Join-Path $setupDir 'Get-OfflineDAoC.ps1')) 'fresh: the official downloader sits in the setup files folder'
    # the steps were recognised from the downloader's own output, not only marked done at the end
    Check ($r.Log -match '\] Downloading Offline DAoC \| [\d.]+ (MB|GB) of ') 'fresh: download progress measured in bytes'
    Check ($r.Log -match '\] Unpacking the game \| [\d.]+ (MB|GB) of ') 'fresh: unpacking progress measured in bytes'
    Check ($r.Log -match '\] Updating the game to ') 'fresh: the official update step recognised from its output'
    # A first install turns every Living Server feature on at the suggested settings (owner 2026-10-28).
    # (A package from before Beta 1.1 has a launcher without the settings mode: Setup says so and goes on.)
    Check (($r.Log -match 'Recommended settings: exit 0') -or ($r.Log -match 'Recommended settings: this launcher has no settings mode')) 'fresh: every Living Server feature turned on at the suggested settings (or skipped for an older launcher)'

    $r = Setup 'scan' @('--scan', '--game', $fresh)
    Check ($r.Log -match [regex]::Escape("GAME $fresh | $($base)b | LivingServer=ThisBuild")) 'scan: the game is found as this build'

    $r = Setup 'again' @('--game', $fresh, '--no-shortcuts')
    Check ($r.Code -eq 0 -and $r.Log -match 'Already installed') 'again: a second run changes nothing'

    # a program named like the launcher is running: Setup must refuse before touching anything
    $fake = Join-Path $Work 'OfflineDAoC.exe'
    Copy-Item "$env:SystemRoot\System32\ping.exe" $fake -Force
    $running = Start-Process -FilePath $fake -ArgumentList '-n 120 127.0.0.1' -PassThru -WindowStyle Hidden
    Start-Sleep 2
    $r = Setup 'while-running' @('--game', $fresh, '--no-shortcuts')
    Check ($r.Code -eq 1 -and $r.Log -match 'still running') 'running: Setup refuses while Offline DAoC runs'
    Stop-Process -Id $running.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 1

    $r = Setup 'remove' @('--game', $fresh, '--remove')
    $released = Released-ServerHash $fresh
    Check ($r.Code -eq 0) 'remove: Setup finished'
    Check ((Sha (Join-Path $fresh 'runtime\server\GameServer.dll')) -eq $released) 'remove: the released server is back'
    $r = Setup 'scan-after-remove' @('--scan', '--game', $fresh)
    Check ($r.Log -match [regex]::Escape("GAME $fresh | $($base)b | LivingServer=None")) 'remove: the game scans as plain Offline DAoC'

    $r = Setup 'reinstall' @('--game', $fresh, '--no-shortcuts')
    Check ($r.Code -eq 0) 'reinstall: Setup finished'
    Assert-LivingServer $fresh 'reinstall'

    # a server file changed by hand: refused, then replaced with --force (backed up)
    $r = Setup 'remove-again' @('--game', $fresh, '--remove')
    $lib = Join-Path $fresh 'runtime\server\lib\GameServer.dll'
    [IO.File]::AppendAllText($lib, ' ')
    $changed = Sha $lib
    $r = Setup 'changed-by-hand' @('--game', $fresh, '--no-shortcuts')
    Check ($r.Code -eq 1 -and $r.Log -match '\[can --force\]') 'changed: Setup refuses a hand-changed server and offers to replace it'
    $r = Setup 'force' @('--game', $fresh, '--no-shortcuts', '--force')
    Check ($r.Code -eq 0) 'force: Setup replaces it'
    Assert-LivingServer $fresh 'force'
    $backup = Get-ChildItem (Join-Path $fresh 'runtime\server\deployment-backups') -Directory -Filter 'fork-build-*' | Sort-Object Name | Select-Object -Last 1
    Check ((Sha (Join-Path $backup.FullName 'runtime_server_lib\GameServer.dll')) -eq $changed) 'force: the hand-changed file was backed up'
}

if ($Scenario -contains 'update036') {
    $old = Join-Path $root 'playable-v0.36b'
    $setupDir = Join-Path $root 'Offline DAoC setup files'
    New-Item -ItemType Directory -Force -Path $setupDir | Out-Null
    $repoDownloader = Join-Path $PSScriptRoot '..\..\Get-OfflineDAoC.ps1'
    Copy-Item $repoDownloader (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -Force
    Write-Host ''
    Write-Host '== update036: shadowofze''s own downloader installs 0.36b (same download cache)' -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -ReleaseVersion 0.36b -Destination $old | Select-Object -Last 3
    Check ((Newest-Release $old) -eq '0.36') 'update036: a 0.36b game to start from'

    # the 0.36 Social Expansion build on it, as players had it
    $seZip = Join-Path $Work 'se.zip'
    Invoke-WebRequest -UseBasicParsing 'https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/v0.36-se.1/OfflineDAoC-SocialExpansion-0.36-se.1.zip' -OutFile $seZip
    Expand-Archive $seZip -DestinationPath $old -Force
    $seDir = Get-ChildItem $old -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'install-server-build.ps1') } | Select-Object -First 1
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $seDir.FullName 'install-server-build.ps1') -GameFolder $old | Select-Object -Last 3
    $r = Setup 'scan-036' @('--scan', '--game', $old)
    Check ($r.Log -match 'LivingServer=OtherBuild \(0\.36-se\.1\)') 'update036: the Social Expansion build is recognised'

    $r = Setup 'update-036-to-base' @('--game', $old, '--no-shortcuts')
    Check ($r.Code -eq 0) 'update036: Setup finished'
    Check ($r.Log -match 'STEP Done: Put the released server back') 'update036: the released server went back first'
    Check ((Newest-Release $old) -eq $base) "update036: the game is Offline DAoC $base now"
    Assert-LivingServer $old 'update036'
}


if ($Scenario -contains 'client') {
    $game = Join-Path $root "playable-v$($base)c"
    $r = Setup 'new-client-install' @('--new', '--dest', $game, '--new-client', '--no-shortcuts')
    Check ($r.Code -eq 0) 'client: Setup finished'
    Check ((Newest-Release $game) -eq $base) "client: the game is Offline DAoC $base"
    Check ((Sha (Join-Path $game 'new-client\VERSION.txt')) -eq '230e6013e920273c7a399b892b92ab91cfa1381e61e845cb8f89b1692b283244') "client: the new client has its $base update"
    Check (Test-Path (Join-Path $game 'START OFFLINE DAOC - NEW CLIENT.cmd')) 'client: the new client start file'
    Check ($r.Log -match '\] Download and unpack the new client|Downloading the new client, part') 'client: the client step was recognised from the output'
    Assert-LivingServer $game 'client'
    $r = Setup 'scan-client' @('--scan', '--game', $game)
    Check ($r.Log -match [regex]::Escape("GAME $game | $($base)c | LivingServer=ThisBuild")) 'client: scans as this build'
    $script:smokeGame = $game
}

if ($Scenario -contains 'resume') {
    $game = Join-Path $root "playable-resume"
    $r = Setup 'stop-mid-download' @('--new', '--dest', $game, '--no-shortcuts', '--stop-after-seconds', '12')
    Check ($r.Code -eq 2 -and $r.Log -match 'RESULT Cancelled') 'resume: Stop during the download ends as stopped'
    $cache = Join-Path $root 'Offline DAoC setup files\.downloads'
    $partial = (Get-ChildItem $cache -Recurse -File -Filter '*.zip.0*' -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    Check ($partial -gt 0) ("resume: downloaded parts are kept ({0:N0} MB)" -f ($partial / 1MB))

    $run = Start-Setup 'crash-mid-download' @('--new', '--dest', $game, '--no-shortcuts')
    $hit = Crash $run '\] Downloading Offline DAoC \| [\d.]+ GB of'
    Check $hit 'resume: Setup was killed part-way through the download'

    $run = Start-Setup 'crash-mid-unpack' @('--new', '--dest', $game, '--no-shortcuts')
    $hit = Crash $run '\] Unpacking the game \| [\d.]+ (MB|GB) of'
    Check $hit 'resume: Setup was killed part-way through unpacking'

    $r = Setup 'finish-after-crashes' @('--new', '--dest', $game, '--no-shortcuts')
    Check ($r.Code -eq 0) 'resume: the next run finishes'
    Check ($r.Log -match 'NOTE An unfinished copy from an earlier attempt was moved aside') 'resume: the half-unpacked copy was moved aside, not deleted'
    Check (@(Get-ChildItem $root -Directory -Filter 'playable-resume.unfinished-*').Count -ge 1) 'resume: the moved copy is still there'
    Assert-LivingServer $game 'resume'
    $script:smokeGame = $game
}

if ($Scenario -contains 'import') {
    $old = Join-Path $root 'playable-v0.35b'
    $setupDir = Join-Path $root 'Offline DAoC setup files'
    New-Item -ItemType Directory -Force -Path $setupDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot '..\..\Get-OfflineDAoC.ps1') (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -Force
    Write-Host ''
    Write-Host '== import: shadowofze''s own downloader installs 0.35b' -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -ReleaseVersion 0.35b -Destination $old | Select-Object -Last 2
    $oldDb = Join-Path $old 'runtime\data\opendaoc.sqlite3.db'
    $before = Sha $oldDb
    $game = Join-Path $root 'playable-import'
    $r = Setup 'new-with-import' @('--new', '--dest', $game, '--no-shortcuts', '--import-from', $old)
    Check ($r.Code -eq 0) 'import: Setup finished'
    Check ($r.Log -match 'STEP Done: Bring your characters over') 'import: the progress import ran'
    Check (Test-Path (Join-Path $game 'PROGRESS IMPORT REPORT.txt')) 'import: the importer wrote its report'
    Check ((Sha $oldDb) -eq $before) 'import: the old game''s world was only read'
    Assert-LivingServer $game 'import'
}

if ($Scenario -contains 'freshcopy') {
    $old = Join-Path $root 'playable-v0.36b'
    $setupDir = Join-Path $root 'Offline DAoC setup files'
    New-Item -ItemType Directory -Force -Path $setupDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot '..\..\Get-OfflineDAoC.ps1') (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -Force
    if (!(Test-Path $old)) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setupDir 'Get-OfflineDAoC.ps1') -ReleaseVersion 0.36b -Destination $old | Select-Object -Last 2
    }
    $quests = Join-Path $old 'runtime\server\classic-quests.json'
    [IO.File]::AppendAllText($quests, ' ')
    $changed = Sha $quests
    $r = Setup 'update-changed-game' @('--game', $old, '--no-shortcuts')
    Check ($r.Code -eq 1 -and $r.Log -match '\[can --fresh-copy\]') 'freshcopy: the update refuses a hand-changed game file and offers a fresh copy'
    Check ($r.Log -match 'RESULT Failed: [^|]*(Nothing was changed|not the|changed)') 'freshcopy: the player sees the downloader''s own message, not a script position'
    $r = Setup 'fresh-copy' @('--game', $old, '--no-shortcuts', '--fresh-copy')
    Check ($r.Code -eq 0) 'freshcopy: Setup finished'
    $copy = if ($r.Log -match 'RESULT Success \| game (.+)') { $Matches[1].Trim() } else { '' }
    Check ($copy -and $copy -ne $old -and (Test-Path $copy)) "freshcopy: the game went to a new folder ($copy)"
    if ($copy -and (Test-Path $copy)) {
        Check ((Newest-Release $copy) -eq $base) "freshcopy: the new folder is Offline DAoC $base"
        Assert-LivingServer $copy 'freshcopy'
    }
    Check ((Sha $quests) -eq $changed) 'freshcopy: the old folder was left as it was'
}

if ($Scenario -contains 'netfx') {
    $game = if ($script:smokeGame) { $script:smokeGame } else { $fresh }
    $state = (& dism.exe /online /get-featureinfo /featurename:NetFx3 /English | Select-String 'State :').ToString()
    Write-Host "   .NET 3.5 before: $state"
    if ($state -match 'Enabled') {
        & dism.exe /online /disable-feature /featurename:NetFx3 /norestart /quiet | Out-Null
        $state = (& dism.exe /online /get-featureinfo /featurename:NetFx3 /English | Select-String 'State :').ToString()
        Write-Host "   after turning it off for the test: $state"
    }
    $r = Setup 'netfx' @('--game', $game, '--no-shortcuts', '--netfx')
    $after = (& dism.exe /online /get-featureinfo /featurename:NetFx3 /English | Select-String 'State :').ToString()
    Write-Host "   .NET 3.5 after Setup: $after"
    Write-Host ("   INFO  netfx: Setup's step: " + (($r.Log -split "`r?`n" | Where-Object { $_ -match 'STEP .*NET Framework' }) -join ' / '))
    if ($state -notmatch 'Enabled') { Check ($after -match 'Enabled' -or $after -match 'Enable Pending') 'netfx: Setup turned .NET Framework 3.5 on' }
    else { Write-Host '   INFO  netfx: the runner kept it on, so turning it on could not be tested here' }
}

if ($Scenario -contains 'smoke') {
    $game = if ($script:smokeGame) { $script:smokeGame } else { $fresh }
    $test = Join-Path $Work 'smoke'
    Write-Host ''
    Write-Host "== smoke: start the installed server and log in ($game)" -ForegroundColor Cyan
    $out = & python (Join-Path $PSScriptRoot '..\..\source\tools\smoke_release_037.py') $game $test 2>&1 | Out-String
    Write-Host $out
    Check ($out -match 'PASS: server started') 'smoke: the installed server starts'
    $fork = Get-ChildItem $test -Recurse -File -Include *.log -ErrorAction SilentlyContinue | Select-String -Pattern "FORK_BUILD name=$([regex]::Escape($info['BuildName']))" -List | Select-Object -First 1
    Check ($null -ne $fork) "smoke: its log says FORK_BUILD name=$($info['BuildName'])"
    if ($fork) { Write-Host "   $($fork.Line.Trim())" }
    Check ($out -match 'PASS: ENTER REALM login') 'smoke: a login like ENTER REALM works'
}

if ($Scenario -contains 'ui') {
    $game = Join-Path $root "playable-ui"
    $shotDir = if ($Shots) { $Shots } else { Join-Path $Work 'autopilot' }
    $log = Join-Path $logs 'ui-autopilot.log'
    Write-Host ''
    Write-Host "== ui: the real window, clicked through a new install" -ForegroundColor Cyan
    $p = Start-Process -FilePath $Exe -ArgumentList '--autopilot', ('"' + $game + '"'), '--shots', ('"' + $shotDir + '"'), '--log', ('"' + $log + '"'), '--minutes', '90' -Wait -PassThru
    Get-Content $log | Where-Object { $_ -match 'autopilot|RESULT|FAILED' } | Select-Object -Last 20 | Write-Host
    Check ($p.ExitCode -eq 0) "ui: the window went from Begin to The realm awaits (exit $($p.ExitCode))"
    Check (@(Get-ChildItem $shotDir -Filter *.png).Count -ge 5) 'ui: a picture of every page'
    if ($p.ExitCode -eq 0) { Assert-LivingServer $game 'ui' }
}

Write-Host ''
Get-PSDrive -PSProvider FileSystem | Format-Table Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}} | Out-String | Write-Host
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
exit 0
