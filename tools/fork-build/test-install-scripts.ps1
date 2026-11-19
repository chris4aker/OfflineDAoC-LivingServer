# Tests install-server-build.ps1 and rollback-server-build.ps1 against fake game folders.
# Run by CI on Windows PowerShell 5.1:  powershell -File tools\fork-build\test-install-scripts.ps1 -BuildDir <package folder>
param([Parameter(Mandatory = $true)][string]$BuildDir)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "PASS  $what"; return }
    Write-Host "FAIL  $what" -ForegroundColor Red
    $script:failures++
    # CI annotations are readable without the job log.
    if ($env:GITHUB_ACTIONS) { Write-Host ("::error title=Installer test::" + (($what -replace "`r", '') -replace "`n", ' | ').Substring(0, [Math]::Min(900, $what.Length))) }
}
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Run([string]$scriptName, [string[]]$arguments) { return RunIn $BuildDir $scriptName $arguments }
function RunIn([string]$dir, [string]$scriptName, [string[]]$arguments) {
    $shell = if ($env:FORK_BUILD_TEST_SHELL) { $env:FORK_BUILD_TEST_SHELL } else { 'powershell.exe' }
    $out = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir $scriptName) @arguments 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $out }
}
$coreLayout = @{
    'runtime/server' = @('CoreBase.dll', 'CoreBase.pdb', 'CoreDatabase.dll', 'CoreDatabase.pdb', 'CoreServer.dll', 'CoreServer.pdb')
    'runtime/server/lib' = @('CoreBase.dll', 'CoreBase.pdb', 'CoreDatabase.dll', 'CoreDatabase.pdb')
    'runtime/server/win-x64' = @('CoreBase.dll', 'CoreDatabase.dll', 'CoreServer.dll')
}
$launcherFiles = @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')
function New-FakeLauncher([string]$dir, [string]$prefix) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $lines = @()
    foreach ($name in $launcherFiles) {
        [IO.File]::WriteAllText((Join-Path $dir $name), "original launcher $name")
        $lines += "$(Sha (Join-Path $dir $name))  $prefix/$name"
    }
    return $lines
}
# -ReleaseNotes: the layout of a game updated by a patch (0.37 on): no editions\ folder, and the manifest lists each
# release's docs/RELEASE-<version>.md up to this one.
# -NoDatabase: a game whose runtime\data has no world database yet. -Wal: the database has a -wal file beside it (WAL mode).
function New-FakeGame([string]$root, [string]$edition, [switch]$NewClient, [string[]]$ReleaseNotes = @(), [switch]$NoDatabase, [switch]$Wal) {
    $folders = @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')
    $lines = @()
    foreach ($folder in $folders) {
        $dir = Join-Path $root $folder
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir 'GameServer.dll'), "original release server")
        [IO.File]::WriteAllText((Join-Path $dir 'GameServer.pdb'), "original release symbols")
        $lines += "$(Sha (Join-Path $dir 'GameServer.dll'))  $folder/GameServer.dll"
        $lines += "$(Sha (Join-Path $dir 'GameServer.pdb'))  $folder/GameServer.pdb"
        # The server's own libraries as the release has them: CoreServer only beside the exe, no .pdb in win-x64.
        foreach ($name in $coreLayout[$folder]) {
            [IO.File]::WriteAllText((Join-Path $dir $name), "original release $folder/$name")
            $lines += "$(Sha (Join-Path $dir $name))  $folder/$name"
        }
    }
    $lines += New-FakeLauncher (Join-Path $root 'runtime') 'runtime'
    if (!$NoDatabase) {
        New-Item -ItemType Directory -Path (Join-Path $root 'runtime\data') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $root 'runtime\data\opendaoc.sqlite3.db'), 'the player''s world and saves')
        if ($Wal) { [IO.File]::WriteAllText((Join-Path $root 'runtime\data\opendaoc.sqlite3.db-wal'), 'saves not yet in the main file') }
    }
    if ($NewClient) {
        $client = New-FakeLauncher (Join-Path $root 'new-client\runtime') 'new-client/runtime'
        Set-Content -LiteralPath (Join-Path $root 'new-client\PACKAGE MANIFEST.sha256') -Value $client -Encoding UTF8
    }
    if ($ReleaseNotes.Count -gt 0) {
        foreach ($version in $ReleaseNotes) {
            $notes = Join-Path $root "docs\RELEASE-$version.md"
            New-Item -ItemType Directory -Path (Split-Path -Parent $notes) -Force | Out-Null
            [IO.File]::WriteAllText($notes, "release $version")
            $lines += "$(Sha $notes)  docs/RELEASE-$version.md"
        }
    } else {
        $editionFile = Join-Path $root "editions\$edition-no-custom-class\runtime\x.txt"
        New-Item -ItemType Directory -Path (Split-Path -Parent $editionFile) -Force | Out-Null
        [IO.File]::WriteAllText($editionFile, 'edition')
        $lines += "$(Sha $editionFile)  editions/$edition-no-custom-class/runtime/x.txt"
    }
    Set-Content -LiteralPath (Join-Path $root 'PACKAGE MANIFEST.sha256') -Value $lines -Encoding UTF8
}

$info = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $BuildDir 'BUILD-INFO.txt')) { if ($line -match '^(.+?)=(.*)$') { $info[$Matches[1]] = $Matches[2] } }
# A build may list several releases (BaseRelease=0.37,0.38); the tests below use its first one.
$base = @($info['BaseRelease'] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })[0]
$work = Join-Path ([IO.Path]::GetTempPath()) ('forkbuild-test-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

# Happy path: install, reinstall is a no-op, rollback restores the originals.
$game = Join-Path $work 'good'
New-FakeGame $game $base -NewClient
$originalLauncher = Sha (Join-Path $game 'runtime\OfflineDAoC.dll')
$original = Sha (Join-Path $game 'runtime\server\lib\GameServer.dll')
$dry = Run 'install-server-build.ps1' @('-GameFolder', $game, '-DryRun')
Check ($dry.Code -eq 0 -and (Sha (Join-Path $game 'runtime\server\lib\GameServer.dll')) -eq $original) 'dry run passes and changes nothing'
$install = Run 'install-server-build.ps1' @('-GameFolder', $game)
Check ($install.Code -eq 0) "install succeeds`n$($install.Output)"
foreach ($folder in @('runtime\server', 'runtime\server\lib', 'runtime\server\win-x64')) {
    Check ((Sha (Join-Path $game "$folder\GameServer.dll")) -eq $info['GameServer.dll']) "new dll in $folder"
    Check ((Sha (Join-Path $game "$folder\GameServer.pdb")) -eq $info['GameServer.pdb']) "new pdb in $folder"
}
foreach ($folder in @('runtime', 'new-client\runtime')) {
    foreach ($name in $launcherFiles) {
        Check ((Sha (Join-Path $game "$folder\$name")) -eq $info["launcher/$name"]) "new launcher $name in $folder"
    }
}
# The server's own libraries (1.1.2 on): each one the folder has is replaced, with its .pdb where the folder has one,
# and none is added where the folder has none.
$shipsCore = [bool]$info['core/CoreDatabase.dll']
Check $shipsCore 'the build ships the server libraries (core\)'
foreach ($folder in $coreLayout.Keys) {
    foreach ($name in $coreLayout[$folder]) {
        Check ((Sha (Join-Path $game "$folder\$name")) -eq $info["core/$name"]) "new $name in $folder"
    }
}
Check (!(Test-Path (Join-Path $game 'runtime\server\lib\CoreServer.dll')) -and !(Test-Path (Join-Path $game 'runtime\server\win-x64\CoreBase.pdb'))) 'no server library is added where the release has none'
Check (Test-Path (Join-Path $game 'tools\claude-version\set_bot_guilds.py')) 'helper scripts installed'
Check (Test-Path (Join-Path $game 'tools\claude-version\set_bot_social.py')) 'social switch installed'
Check (@(Get-ChildItem (Join-Path $game 'runtime\server\deployment-backups') -Directory).Count -eq 1) 'one backup folder'
$again = Run 'install-server-build.ps1' @('-GameFolder', $game)
Check ($again.Code -eq 0 -and $again.Output -match 'Already installed') 'reinstall is a no-op'
Start-Sleep -Seconds 2   # backup folders are named to the second
[IO.File]::WriteAllText((Join-Path $game 'runtime\OfflineDAoC.exe'), 'half-copied launcher')
$halfway = Run 'install-server-build.ps1' @('-GameFolder', $game)
Check ($halfway.Code -eq 0 -and $halfway.Output -notmatch 'Already installed' -and
       (Sha (Join-Path $game 'runtime\OfflineDAoC.exe')) -eq $info['launcher/OfflineDAoC.exe']) "a half-copied launcher is installed again`n$($halfway.Output)"
$rollbackHalf = Run 'rollback-server-build.ps1' @('-GameFolder', $game)
Check ($rollbackHalf.Code -eq 0) "rollback of the repair install`n$($rollbackHalf.Output)"
$rollback = Run 'rollback-server-build.ps1' @('-GameFolder', $game)
Check ($rollback.Code -eq 0) "rollback succeeds`n$($rollback.Output)"
Check ((Sha (Join-Path $game 'runtime\server\lib\GameServer.dll')) -eq $original) 'rollback restores the original dll'
$coreBack = $true
foreach ($folder in $coreLayout.Keys) {
    foreach ($name in $coreLayout[$folder]) {
        if ([IO.File]::ReadAllText((Join-Path $game "$folder\$name")) -ne "original release $folder/$name") { $coreBack = $false }
    }
}
Check $coreBack 'rollback restores the original server libraries and their .pdb files'
Check ((Sha (Join-Path $game 'runtime\OfflineDAoC.dll')) -eq $originalLauncher) 'rollback restores the original launcher'
Check ([IO.File]::ReadAllText((Join-Path $game 'new-client\runtime\OfflineDAoC.dll')) -eq 'original launcher OfflineDAoC.dll') 'rollback restores the new client launcher'
Check (!(Test-Path (Join-Path $game 'tools\claude-version\set_bot_guilds.py'))) 'rollback removes helpers it added'
Check (!(Test-Path (Join-Path $game 'tools\claude-version\set_bot_social.py'))) 'rollback removes the social switch'
$nothing = Run 'rollback-server-build.ps1' @('-GameFolder', $game)
Check ($nothing.Code -ne 0) 'a second rollback has nothing to undo'
$reinstall = Run 'install-server-build.ps1' @('-GameFolder', $game)
Check ($reinstall.Code -eq 0) 'install works again after a rollback'

# Living Server 1.1.2: the world database is backed up before an install, with its -wal file, verified; a rollback never
# puts it back (that would undo the play since); only the newest 3 installs keep their database copy.
$dbGame = Join-Path $work 'database'
New-FakeGame $dbGame $base -Wal
$db = Join-Path $dbGame 'runtime\data\opendaoc.sqlite3.db'
$dbHash = Sha $db; $walHash = Sha "$db-wal"
$dbDry = Run 'install-server-build.ps1' @('-GameFolder', $dbGame, '-DryRun')
Check ($dbDry.Code -eq 0 -and !(Test-Path (Join-Path $dbGame 'runtime\server\deployment-backups'))) "database: a dry run backs nothing up`n$($dbDry.Output)"
$dbInstall = Run 'install-server-build.ps1' @('-GameFolder', $dbGame)
$dbBackup = @(Get-ChildItem (Join-Path $dbGame 'runtime\server\deployment-backups') -Directory)[0].FullName
Check ($dbInstall.Code -eq 0 -and (Test-Path (Join-Path $dbBackup 'data\opendaoc.sqlite3.db')) -and
       (Sha (Join-Path $dbBackup 'data\opendaoc.sqlite3.db')) -eq $dbHash -and (Sha (Join-Path $dbBackup 'data\opendaoc.sqlite3.db-wal')) -eq $walHash) "database: the install backs up the database and its -wal first`n$($dbInstall.Output)"
Check ($dbInstall.Output -match 'World database backed up') 'database: the install says where the database copy is'
$dbRecord = Get-Content -LiteralPath (Join-Path $dbBackup 'install-record.json') -Raw | ConvertFrom-Json
Check (@($dbRecord.Database).Count -eq 2 -and @($dbRecord.Database | Where-Object { $_.Name -eq 'opendaoc.sqlite3.db' -and $_.Hash -eq $dbHash }).Count -eq 1) 'database: the install record lists the database copy'
Check ((Sha $db) -eq $dbHash) 'database: the install leaves the database itself unchanged'
[IO.File]::WriteAllText($db, 'the world after more play')
$played = Sha $db
$dbRollback = Run 'rollback-server-build.ps1' @('-GameFolder', $dbGame)
Check ($dbRollback.Code -eq 0 -and (Sha $db) -eq $played -and (Test-Path (Join-Path $dbBackup 'data\opendaoc.sqlite3.db'))) "database: rollback keeps the played database and its pre-install copy`n$($dbRollback.Output)"
foreach ($round in 1..3) {
    $null = Run 'install-server-build.ps1' @('-GameFolder', $dbGame)
    if ($round -lt 3) { $null = Run 'rollback-server-build.ps1' @('-GameFolder', $dbGame) }
}
$dataFolders = @(Get-ChildItem (Join-Path $dbGame 'runtime\server\deployment-backups') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'data') })
$allFolders = @(Get-ChildItem (Join-Path $dbGame 'runtime\server\deployment-backups') -Directory)
Check ($allFolders.Count -eq 4 -and $dataFolders.Count -eq 3 -and !(Test-Path (Join-Path $dbBackup 'data')) -and
       (Test-Path (Join-Path $dbBackup 'runtime_server\GameServer.dll'))) "database: only the newest 3 installs keep a database copy; their server backups all stay ($($allFolders.Count) folders, $($dataFolders.Count) with data)"
$noDb = Join-Path $work 'no-database'
New-FakeGame $noDb $base -NoDatabase
$noDbInstall = Run 'install-server-build.ps1' @('-GameFolder', $noDb)
Check ($noDbInstall.Code -eq 0 -and (Sha (Join-Path $noDb 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll'] -and
       $noDbInstall.Output -match 'No world database') "database: a game with no database yet still installs`n$($noDbInstall.Output)"

# Living Server 1.2, the epic chains: the server links them in memory and nothing is written to the world for them, so
# an install and a rollback leave the world database as it was. A world epic_chains.py --apply wrote the links into
# (its note beside the server) is refused by ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd until they are put back, since the
# released server would never offer the Guild of Shadows; -Force rolls back anyway.
$epic = Join-Path $work 'epic'
New-FakeGame $epic $base
$epicDb = Join-Path $epic 'runtime\data\opendaoc.sqlite3.db'
$epicHash = Sha $epicDb
$r = Run 'install-server-build.ps1' @('-GameFolder', $epic)
Check ($r.Code -eq 0 -and (Sha $epicDb) -eq $epicHash -and $r.Output -match 'epic chains are linked by the server') "epic chains: the install writes nothing to the world for them`n$($r.Output)"
$r = Run 'rollback-server-build.ps1' @('-GameFolder', $epic)
Check ($r.Code -eq 0 -and (Sha $epicDb) -eq $epicHash -and (Sha (Join-Path $epic 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) "epic chains: the rollback has nothing to undo in the world`n$($r.Output)"
$null = Run 'install-server-build.ps1' @('-GameFolder', $epic)
$epicNote = Join-Path $epic 'runtime\server\epic-chains-in-world.txt'
[IO.File]::WriteAllText($epicNote, 'The epic chain links are written into this world''s quest rows (epic_chains.py --apply).')
$r = Run 'rollback-server-build.ps1' @('-GameFolder', $epic)
Check ($r.Code -ne 0 -and $r.Output -match 'epic_chains.py rollback --apply' -and (Sha (Join-Path $epic 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "epic chains: a world with the links written into it is refused by ROLLBACK, nothing changed`n$($r.Output)"
$r = Run 'rollback-server-build.ps1' @('-GameFolder', $epic, '-ToRelease')
Check ($r.Code -ne 0 -and (Sha (Join-Path $epic 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "epic chains: and by PREPARE-FOR-UPDATE (-ToRelease)`n$($r.Output)"
$r = Run 'rollback-server-build.ps1' @('-GameFolder', $epic, '-Force')
Check ($r.Code -eq 0 -and $r.Output -match 'stay written' -and (Sha (Join-Path $epic 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll'] -and
       (Sha $epicDb) -eq $epicHash) "epic chains: -Force rolls back anyway and says so`n$($r.Output)"
Remove-Item -LiteralPath $epicNote
$null = Run 'install-server-build.ps1' @('-GameFolder', $epic)
$r = Run 'rollback-server-build.ps1' @('-GameFolder', $epic, '-ToRelease')
Check ($r.Code -eq 0 -and (Sha $epicDb) -eq $epicHash) "epic chains: with the links put back (no note) PREPARE-FOR-UPDATE goes ahead`n$($r.Output)"

# The build placed inside one game folder that has other game folders beside it (playtest 2026-11-04: three
# side-by-side installs): the folder it sits in wins, without -GameFolder.
$siblings = Join-Path $work 'siblings'
New-FakeGame (Join-Path $siblings 'playable-a') $base
New-FakeGame (Join-Path $siblings 'playable-b') $base
$placed = Join-Path $siblings 'playable-a\LivingServer-placed'
Copy-Item -LiteralPath $BuildDir -Destination $placed -Recurse
$sibDry = RunIn $placed 'install-server-build.ps1' @('-DryRun')
Check ($sibDry.Code -eq 0 -and $sibDry.Output -match [regex]::Escape((Join-Path $siblings 'playable-a'))) "folder search: the game folder the build sits in wins over its siblings`n$($sibDry.Output)"

# Wrong release: refused, nothing changed.
$other = Join-Path $work 'other'
New-FakeGame $other '9.99'
$wrong = Run 'install-server-build.ps1' @('-GameFolder', $other)
Check ($wrong.Code -ne 0 -and (Sha (Join-Path $other 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'another release is refused'

# A hand-changed dll: refused.
$changed = Join-Path $work 'changed'
New-FakeGame $changed $base
[IO.File]::WriteAllText((Join-Path $changed 'runtime\server\GameServer.dll'), 'someone else''s build')
$unknown = Run 'install-server-build.ps1' @('-GameFolder', $changed)
Check ($unknown.Code -ne 0 -and (Sha (Join-Path $changed 'runtime\server\lib\GameServer.dll')) -ne $info['GameServer.dll']) 'an unknown dll is refused'

# A hand-changed server library: refused, and nothing is touched.
$changedCore = Join-Path $work 'changed-core'
New-FakeGame $changedCore $base
[IO.File]::WriteAllText((Join-Path $changedCore 'runtime\server\CoreDatabase.dll'), 'someone else''s database layer')
$unknownCore = Run 'install-server-build.ps1' @('-GameFolder', $changedCore)
Check ($unknownCore.Code -ne 0 -and (Sha (Join-Path $changedCore 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll'] -and
       (Sha (Join-Path $changedCore 'runtime\server\lib\CoreDatabase.dll')) -ne $info['core/CoreDatabase.dll']) "an unknown server library is refused`n$($unknownCore.Output)"

# A hand-changed launcher: refused, and the server is not touched either.
$changedLauncher = Join-Path $work 'changed-launcher'
New-FakeGame $changedLauncher $base
[IO.File]::WriteAllText((Join-Path $changedLauncher 'runtime\OfflineDAoC.dll'), 'someone else''s launcher')
$unknownLauncher = Run 'install-server-build.ps1' @('-GameFolder', $changedLauncher)
Check ($unknownLauncher.Code -ne 0 -and (Sha (Join-Path $changedLauncher 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'an unknown launcher is refused'

# A game without the new client: only runtime's launcher is replaced.
$plain = Join-Path $work 'plain'
New-FakeGame $plain $base
$plainInstall = Run 'install-server-build.ps1' @('-GameFolder', $plain)
Check ($plainInstall.Code -eq 0 -and (Sha (Join-Path $plain 'runtime\OfflineDAoC.dll')) -eq $info['launcher/OfflineDAoC.dll'] -and
       !(Test-Path (Join-Path $plain 'new-client'))) "install without the new client`n$($plainInstall.Output)"

# Back to the release before an Offline DAoC update, whatever was installed over it: an install, a copy by hand on
# top, a forced install on top of that (the owner's PC, 2026-10-18). -ToRelease finds the released copies in the backups.
$chain = Join-Path $work 'chain'
New-FakeGame $chain $base -NewClient
$releasedDll = Sha (Join-Path $chain 'runtime\server\GameServer.dll')
$releasedLauncher = Sha (Join-Path $chain 'runtime\OfflineDAoC.dll')
$releasedClient = Sha (Join-Path $chain 'new-client\runtime\OfflineDAoC.dll')
$null = Run 'install-server-build.ps1' @('-GameFolder', $chain)
foreach ($folder in @('runtime\server', 'runtime\server\lib', 'runtime\server\win-x64')) {
    [IO.File]::WriteAllText((Join-Path $chain "$folder\GameServer.dll"), 'a build copied in by hand')
}
$forced = Run 'install-server-build.ps1' @('-GameFolder', $chain, '-Force')
Check ($forced.Code -eq 0) "a forced install over a copy by hand`n$($forced.Output)"
$dryRelease = Run 'rollback-server-build.ps1' @('-GameFolder', $chain, '-ToRelease', '-DryRun')
Check ($dryRelease.Code -eq 0 -and (Sha (Join-Path $chain 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "-ToRelease -DryRun changes nothing`n$($dryRelease.Output)"
$toRelease = Run 'rollback-server-build.ps1' @('-GameFolder', $chain, '-ToRelease')
$allBack = $true
foreach ($folder in @('runtime\server', 'runtime\server\lib', 'runtime\server\win-x64')) {
    if ((Sha (Join-Path $chain "$folder\GameServer.dll")) -ne $releasedDll) { $allBack = $false }
}
Check ($toRelease.Code -eq 0 -and $allBack -and (Sha (Join-Path $chain 'runtime\OfflineDAoC.dll')) -eq $releasedLauncher -and
       (Sha (Join-Path $chain 'new-client\runtime\OfflineDAoC.dll')) -eq $releasedClient) "-ToRelease puts the released server and launchers back`n$($toRelease.Output)"
$coreReleased = $true
foreach ($folder in $coreLayout.Keys) {
    foreach ($name in $coreLayout[$folder]) {
        if ([IO.File]::ReadAllText((Join-Path $chain "$folder\$name")) -ne "original release $folder/$name") { $coreReleased = $false }
    }
}
Check $coreReleased "-ToRelease puts the released server libraries back`n$($toRelease.Output)"
$nothingLeft = Run 'rollback-server-build.ps1' @('-GameFolder', $chain)
Check ($nothingLeft.Code -ne 0) 'after -ToRelease no install is left to undo'
$reinstall = Run 'install-server-build.ps1' @('-GameFolder', $chain)
Check ($reinstall.Code -eq 0 -and (Sha (Join-Path $chain 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "install works again after -ToRelease`n$($reinstall.Output)"

# -ToRelease with no released copy anywhere: refused, nothing changed.
$lost = Join-Path $work 'lost'
New-FakeGame $lost $base
[IO.File]::WriteAllText((Join-Path $lost 'runtime\OfflineDAoC.dll'), 'a launcher with no backup')
$noCopy = Run 'rollback-server-build.ps1' @('-GameFolder', $lost, '-ToRelease')
Check ($noCopy.Code -ne 0 -and [IO.File]::ReadAllText((Join-Path $lost 'runtime\OfflineDAoC.dll')) -eq 'a launcher with no backup') "-ToRelease without a released copy is refused`n$($noCopy.Output)"

# A game updated by a patch has no editions\ folder (the real 0.36c -> 0.37c update, 2026-10-18): its newest release
# notes say which release it is.
$updated = Join-Path $work 'updated'
New-FakeGame $updated $base -NewClient -ReleaseNotes @('0.35', '0.36', $base)
$updatedInstall = Run 'install-server-build.ps1' @('-GameFolder', $updated)
Check ($updatedInstall.Code -eq 0 -and (Sha (Join-Path $updated 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "install on an updated game (no editions folder)`n$($updatedInstall.Output)"
$older = Join-Path $work 'older'
New-FakeGame $older $base -ReleaseNotes @('0.35', '0.36')
$olderInstall = Run 'install-server-build.ps1' @('-GameFolder', $older)
Check ($olderInstall.Code -ne 0 -and (Sha (Join-Path $older 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'a game whose newest release notes are older is refused'
$newer = Join-Path $work 'newer'
New-FakeGame $newer $base -ReleaseNotes @('0.36', $base, '9.99')
$newerInstall = Run 'install-server-build.ps1' @('-GameFolder', $newer)
Check ($newerInstall.Code -ne 0 -and (Sha (Join-Path $newer 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'a game updated past this release is refused'

# A build that lists two releases (BaseRelease=<base>,9.98, as a build carrying the next release's server code will)
# installs on a game updated to either, and still refuses one updated past both.
$twoBuild = Join-Path $work 'two-release-build'
Copy-Item -LiteralPath $BuildDir -Destination $twoBuild -Recurse
$twoInfo = Join-Path $twoBuild 'BUILD-INFO.txt'
Set-Content -LiteralPath $twoInfo -Encoding UTF8 -Value (Get-Content -LiteralPath $twoInfo | ForEach-Object {
    if ($_ -match '^BaseRelease=') { "BaseRelease=$base,9.98" } else { $_ } })
$onBase = Join-Path $work 'two-on-base'
New-FakeGame $onBase $base -ReleaseNotes @('0.36', $base)
$r = RunIn $twoBuild 'install-server-build.ps1' @('-GameFolder', $onBase)
Check ($r.Code -eq 0 -and (Sha (Join-Path $onBase 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "a two-release build installs on the first release`n$($r.Output)"
$onNext = Join-Path $work 'two-on-next'
New-FakeGame $onNext $base -ReleaseNotes @('0.36', $base, '9.98')
$r = RunIn $twoBuild 'install-server-build.ps1' @('-GameFolder', $onNext)
Check ($r.Code -eq 0 -and (Sha (Join-Path $onNext 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "a two-release build installs on the second release`n$($r.Output)"
$pastBoth = Join-Path $work 'two-past-both'
New-FakeGame $pastBoth $base -ReleaseNotes @('0.36', $base, '9.98', '9.99')
$r = RunIn $twoBuild 'install-server-build.ps1' @('-GameFolder', $pastBoth)
Check ($r.Code -ne 0 -and (Sha (Join-Path $pastBoth 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'a two-release build refuses a game updated past both'
$twoEdition = Join-Path $work 'two-edition'
New-FakeGame $twoEdition '9.98'
$r = RunIn $twoBuild 'install-server-build.ps1' @('-GameFolder', $twoEdition)
Check ($r.Code -eq 0) "a two-release build accepts the second release's editions folder`n$($r.Output)"

# Game files fixed in place (content-patches.json): a copy of this build with test patches. The released file is fixed
# and backed up, a missing or changed file is left alone, rollback and -ToRelease put the released file back, and a
# patch list that doesn't match BUILD-INFO.txt is refused.
$patched = Join-Path $work 'patched-build'
Copy-Item -LiteralPath $BuildDir -Destination $patched -Recurse
$releasedText = 'released navmesh 0123456789'
$fixedText = $releasedText.Substring(0, 10) + 'LINKS' + $releasedText.Substring(10)
function TextSha([string]$text) { $s = [Security.Cryptography.SHA256]::Create(); (($s.ComputeHash([Text.Encoding]::ASCII.GetBytes($text)) | ForEach-Object { $_.ToString('x2') }) -join '') }
$patches = @(
    [pscustomobject]@{ Path = 'runtime/server/navmesh/zone999.nav'; Released = (TextSha $releasedText); Fixed = (TextSha $fixedText); Size = $fixedText.Length
        Ops = @(@{ copy = @(0, 10) }, @{ data = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('LINKS')) }, @{ copy = @(10, ($releasedText.Length - 10)) }) },
    [pscustomobject]@{ Path = 'new-client/godot/content/zones/zone998/models/gate.gltf'; Released = (TextSha 'x'); Fixed = (TextSha 'y'); Size = 1
        Ops = @(@{ data = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('y')) }) },
    [pscustomobject]@{ Path = 'runtime/server/navmesh/zone997.nav'; Released = (TextSha 'r'); Fixed = (TextSha 'f'); Size = 1
        Ops = @(@{ data = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('f')) }) },
    # Beta 1.1: a file of ours shipped whole under content/ (the loading screen), not as byte ops.
    [pscustomobject]@{ Path = 'runtime/client-opendaoc/app/pregame/splash996.mpk'; Released = (TextSha 'old splash'); Fixed = (TextSha 'new splash')
        Size = 10; File = 'content/test-splash996.mpk' })
New-Item -ItemType Directory -Path (Join-Path $patched 'content') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $patched 'content\test-splash996.mpk'), 'new splash')
[IO.File]::WriteAllText((Join-Path $patched 'content-patches.json'), (ConvertTo-Json -InputObject $patches -Depth 6))
$infoLines = @(Get-Content -LiteralPath (Join-Path $patched 'BUILD-INFO.txt') | Where-Object { $_ -notlike 'content-patches.json=*' })
$infoLines += "content-patches.json=$(Sha (Join-Path $patched 'content-patches.json'))"
Set-Content -LiteralPath (Join-Path $patched 'BUILD-INFO.txt') -Value $infoLines -Encoding UTF8
function New-PatchedGame([string]$root) {
    New-FakeGame $root $base -NewClient
    New-Item -ItemType Directory -Path (Join-Path $root 'runtime\server\navmesh') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'runtime\server\navmesh\zone999.nav'), $releasedText)
    [IO.File]::WriteAllText((Join-Path $root 'runtime\server\navmesh\zone997.nav'), 'someone else''s mesh')
    New-Item -ItemType Directory -Path (Join-Path $root 'runtime\client-opendaoc\app\pregame') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'runtime\client-opendaoc\app\pregame\splash996.mpk'), 'old splash')
}
$fixGame = Join-Path $work 'content'
New-PatchedGame $fixGame
$nav = Join-Path $fixGame 'runtime\server\navmesh\zone999.nav'
$fixDry = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $fixGame, '-DryRun')
Check ($fixDry.Code -eq 0 -and [IO.File]::ReadAllText($nav) -eq $releasedText) "content: dry run changes nothing`n$($fixDry.Output)"
$fixInstall = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $fixGame)
Check ($fixInstall.Code -eq 0 -and [IO.File]::ReadAllText($nav) -eq $fixedText) "content: the released file is fixed`n$($fixInstall.Output)"
$splash = Join-Path $fixGame 'runtime\client-opendaoc\app\pregame\splash996.mpk'
Check ([IO.File]::ReadAllText($splash) -eq 'new splash') "content: a whole file under content/ replaces the released one`n$($fixInstall.Output)"
Check ([IO.File]::ReadAllText((Join-Path $fixGame 'runtime\server\navmesh\zone997.nav')) -eq 'someone else''s mesh' -and
       $fixInstall.Output -match 'not the released file.*left alone' -and $fixInstall.Output -match 'not in this game, skipped') 'content: a changed or missing file is left alone'
$fixAgain = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $fixGame)
Check ($fixAgain.Code -eq 0 -and $fixAgain.Output -match 'Already installed') "content: reinstall is a no-op`n$($fixAgain.Output)"
$fixRollback = RunIn $patched 'rollback-server-build.ps1' @('-GameFolder', $fixGame)
Check ($fixRollback.Code -eq 0 -and [IO.File]::ReadAllText($nav) -eq $releasedText) "content: rollback puts the released file back`n$($fixRollback.Output)"
Check ([IO.File]::ReadAllText($splash) -eq 'old splash') "content: rollback puts the released whole file back`n$($fixRollback.Output)"
$badSplash = Join-Path $work 'content-bad-splash'
New-PatchedGame $badSplash
[IO.File]::WriteAllText((Join-Path $patched 'content\test-splash996.mpk'), 'damaged!!!')
$badSplashInstall = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $badSplash)
Check ($badSplashInstall.Code -ne 0 -and [IO.File]::ReadAllText((Join-Path $badSplash 'runtime\client-opendaoc\app\pregame\splash996.mpk')) -eq 'old splash' -and
       (Sha (Join-Path $badSplash 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) "content: a damaged whole file is refused before anything changes`n$($badSplashInstall.Output)"
[IO.File]::WriteAllText((Join-Path $patched 'content\test-splash996.mpk'), 'new splash')
$fixRelease = Join-Path $work 'content-release'
New-PatchedGame $fixRelease
$null = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $fixRelease)
$fixToRelease = RunIn $patched 'rollback-server-build.ps1' @('-GameFolder', $fixRelease, '-ToRelease')
Check ($fixToRelease.Code -eq 0 -and [IO.File]::ReadAllText((Join-Path $fixRelease 'runtime\server\navmesh\zone999.nav')) -eq $releasedText) "content: -ToRelease puts the released file back`n$($fixToRelease.Output)"
[IO.File]::WriteAllText((Join-Path $patched 'content-patches.json'), '[]')
$damaged = Join-Path $work 'content-damaged'
New-PatchedGame $damaged
$damagedInstall = RunIn $patched 'install-server-build.ps1' @('-GameFolder', $damaged)
Check ($damagedInstall.Code -ne 0 -and (Sha (Join-Path $damaged 'runtime\server\GameServer.dll')) -ne $info['GameServer.dll']) 'content: a patch list that doesn''t match BUILD-INFO.txt is refused'

# Living Server 1.2, the titled loading screen (owner 2026-11-07, "I don't want the original splash screens, I want
# mine"). Two test builds: "1.1" ships its loading screen over the released one (its entry as 1.1 had it, no Replaces or
# Keep, and its install record without the 1.2 fields), "1.2" ships the new one with Replaces (the 1.1 one) and Keep.
# Install: shadowofze's and the 1.1 loading screen are replaced, an unknown one is left alone. ROLLBACK.cmd: the 1.1
# loading screen comes back, shadowofze's never does. PREPARE-FOR-UPDATE.cmd (-ToRelease): the loading screen stays,
# since the Offline DAoC patcher doesn't need it. Also an entry with Replaces but no Keep (art995): -ToRelease puts the
# released file back from the 1.1 install's backup though the newest backup holds the 1.1 version.
$splashRel = 'runtime/client-opendaoc/app/pregame/splash.mpk'
$artRel = 'runtime/client-opendaoc/app/art995.dat'
$shadowSplash = 'shadowofze splash'; $ls11Splash = 'living server 1.1 splash'; $ls12Splash = 'living server 1.2 titled splash'
$artReleased = 'released art'; $art11 = 'art fixed in 1.1'; $art12 = 'art fixed in 1.2'
function New-TestBuild([string]$dir, [string]$dllText, $entries, [hashtable]$files) {
    Copy-Item -LiteralPath $BuildDir -Destination $dir -Recurse
    New-Item -ItemType Directory -Path (Join-Path $dir 'content') -Force | Out-Null
    foreach ($name in $files.Keys) { [IO.File]::WriteAllText((Join-Path $dir "content\$name"), $files[$name]) }
    [IO.File]::WriteAllText((Join-Path $dir 'content-patches.json'), (ConvertTo-Json -InputObject @($entries) -Depth 6))
    $lines = @(Get-Content -LiteralPath (Join-Path $dir 'BUILD-INFO.txt') | Where-Object { $_ -notlike 'content-patches.json=*' })
    if ($dllText) {
        [IO.File]::WriteAllText((Join-Path $dir 'GameServer.dll'), $dllText)
        $lines = @($lines | ForEach-Object { if ($_ -like 'GameServer.dll=*') { "GameServer.dll=$(Sha (Join-Path $dir 'GameServer.dll'))" } else { $_ } })
    }
    $lines += "content-patches.json=$(Sha (Join-Path $dir 'content-patches.json'))"
    Set-Content -LiteralPath (Join-Path $dir 'BUILD-INFO.txt') -Value $lines -Encoding UTF8
}
$build11 = Join-Path $work 'build-1.1'
New-TestBuild $build11 'fork build 1.1' @(
    [pscustomobject]@{ Path = $splashRel; Released = (TextSha $shadowSplash); Fixed = (TextSha $ls11Splash); Size = $ls11Splash.Length; File = 'content/pregame-splash.mpk' },
    [pscustomobject]@{ Path = $artRel; Released = (TextSha $artReleased); Fixed = (TextSha $art11); Size = $art11.Length; File = 'content/art995.dat' }
) @{ 'pregame-splash.mpk' = $ls11Splash; 'art995.dat' = $art11 }
$build12 = Join-Path $work 'build-1.2'
New-TestBuild $build12 $null @(
    [pscustomobject]@{ Path = $splashRel; Released = (TextSha $shadowSplash); Fixed = (TextSha $ls12Splash); Size = $ls12Splash.Length
        Replaces = @((TextSha $ls11Splash)); Keep = $true; File = 'content/pregame-splash.mpk' },
    [pscustomobject]@{ Path = $artRel; Released = (TextSha $artReleased); Fixed = (TextSha $art12); Size = $art12.Length
        Replaces = @((TextSha $art11)); File = 'content/art995.dat' }
) @{ 'pregame-splash.mpk' = $ls12Splash; 'art995.dat' = $art12 }
function New-SplashGame([string]$root, [string]$splashText) {
    New-FakeGame $root $base
    New-Item -ItemType Directory -Path (Join-Path $root 'runtime\client-opendaoc\app\pregame') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'runtime\client-opendaoc\app\pregame\splash.mpk'), $splashText)
    [IO.File]::WriteAllText((Join-Path $root 'runtime\client-opendaoc\app\art995.dat'), $artReleased)
}
function SplashOf([string]$root) { [IO.File]::ReadAllText((Join-Path $root 'runtime\client-opendaoc\app\pregame\splash.mpk')) }
function ArtOf([string]$root) { [IO.File]::ReadAllText((Join-Path $root 'runtime\client-opendaoc\app\art995.dat')) }
function DllOf([string]$root) { [IO.File]::ReadAllText((Join-Path $root 'runtime\server\GameServer.dll')) }
# A 1.1 player's game: the 1.1 build installed by the 1.1 installer, whose record has no Released or Keep.
function Install-As11([string]$root) {
    $r = RunIn $build11 'install-server-build.ps1' @('-GameFolder', $root)
    $newest = @(Get-ChildItem (Join-Path $root 'runtime\server\deployment-backups') -Directory | Sort-Object Name)[-1].FullName
    $recordFile = Join-Path $newest 'install-record.json'
    $record = Get-Content -LiteralPath $recordFile -Raw | ConvertFrom-Json
    $record.ContentFiles = @($record.ContentFiles | ForEach-Object { [pscustomobject]@{ Path = $_.Path; OldHash = $_.OldHash; NewHash = $_.NewHash } })
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $recordFile -Encoding UTF8
    Start-Sleep -Seconds 1
    return $r
}

# Released -> new, then ROLLBACK.cmd: the server goes back, the new loading screen stays.
$s1 = Join-Path $work 'splash-released'
New-SplashGame $s1 $shadowSplash
$r = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s1)
$s1Backup = @(Get-ChildItem (Join-Path $s1 'runtime\server\deployment-backups') -Directory)[0].FullName
Check ($r.Code -eq 0 -and (SplashOf $s1) -eq $ls12Splash -and $r.Output -match 'splash.mpk: released file, will be fixed' -and
       [IO.File]::ReadAllText((Join-Path $s1Backup 'content\runtime\client-opendaoc\app\pregame\splash.mpk')) -eq $shadowSplash) "splash: released -> new, shadowofze's backed up`n$($r.Output)"
$s1Record = Get-Content -LiteralPath (Join-Path $s1Backup 'install-record.json') -Raw | ConvertFrom-Json
$s1Entry = @($s1Record.ContentFiles | Where-Object { $_.Path -eq $splashRel })[0]
Check ($s1Entry.Keep -eq $true -and $s1Entry.Released -eq (TextSha $shadowSplash)) 'splash: the install record marks it kept and names the released file'
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s1)
Check ($r.Code -eq 0 -and (SplashOf $s1) -eq $ls12Splash -and $r.Output -match 'kept \(the released one is not put back\)' -and
       (DllOf $s1) -eq 'original release server' -and (ArtOf $s1) -eq $artReleased) "splash: ROLLBACK after a first install keeps the new loading screen, puts the rest back`n$($r.Output)"
$r = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s1)
Check ($r.Code -eq 0 -and (SplashOf $s1) -eq $ls12Splash -and $r.Output -match 'splash.mpk: fixed already') "splash: reinstall after that rollback finds it in place`n$($r.Output)"
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s1, '-ToRelease')
Check ($r.Code -eq 0 -and (SplashOf $s1) -eq $ls12Splash -and (DllOf $s1) -eq 'original release server' -and
       $r.Output -match "splash.mpk: Living Server's own file, kept") "splash: PREPARE-FOR-UPDATE keeps the new loading screen`n$($r.Output)"

# The 1.1 loading screen -> new, then ROLLBACK.cmd twice: the 1.1 one comes back, then stays (never shadowofze's).
$s2 = Join-Path $work 'splash-ls11'
New-SplashGame $s2 $shadowSplash
$r = Install-As11 $s2
Check ($r.Code -eq 0 -and (SplashOf $s2) -eq $ls11Splash -and (DllOf $s2) -eq 'fork build 1.1') "splash: the 1.1 build installs its loading screen`n$($r.Output)"
$r = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s2)
$s2Backup = @(Get-ChildItem (Join-Path $s2 'runtime\server\deployment-backups') -Directory | Sort-Object Name)[-1].FullName
Check ($r.Code -eq 0 -and (SplashOf $s2) -eq $ls12Splash -and (ArtOf $s2) -eq $art12 -and
       $r.Output -match 'splash.mpk: an earlier Living Server version, will be updated' -and
       [IO.File]::ReadAllText((Join-Path $s2Backup 'content\runtime\client-opendaoc\app\pregame\splash.mpk')) -eq $ls11Splash) "splash: the 1.1 loading screen -> new, backed up`n$($r.Output)"
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s2)
Check ($r.Code -eq 0 -and (SplashOf $s2) -eq $ls11Splash -and (ArtOf $s2) -eq $art11 -and (DllOf $s2) -eq 'fork build 1.1') "splash: ROLLBACK puts the 1.1 loading screen back`n$($r.Output)"
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s2)
Check ($r.Code -eq 0 -and (SplashOf $s2) -eq $ls11Splash -and (ArtOf $s2) -eq $artReleased -and (DllOf $s2) -eq 'original release server') "splash: ROLLBACK of the 1.1 install keeps its loading screen, never shadowofze's`n$($r.Output)"

# PREPARE-FOR-UPDATE after 1.1 then 1.2: the loading screen stays the new one; art995 goes back to the released file,
# found in the 1.1 install's backup (the 1.2 install's backup holds the 1.1 version).
$s3 = Join-Path $work 'splash-prepare'
New-SplashGame $s3 $shadowSplash
$null = Install-As11 $s3
$null = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s3)
$dry = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s3, '-ToRelease', '-DryRun')
Check ($dry.Code -eq 0 -and (ArtOf $s3) -eq $art12 -and (SplashOf $s3) -eq $ls12Splash) "splash: PREPARE-FOR-UPDATE -DryRun changes nothing`n$($dry.Output)"
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s3, '-ToRelease')
Check ($r.Code -eq 0 -and (SplashOf $s3) -eq $ls12Splash -and (ArtOf $s3) -eq $artReleased -and (DllOf $s3) -eq 'original release server') "splash: PREPARE-FOR-UPDATE after 1.1 and 1.2 keeps the new loading screen and puts the released art back from the older backup`n$($r.Output)"
$r = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s3)
Check ($r.Code -eq 0 -and (SplashOf $s3) -eq $ls12Splash -and (ArtOf $s3) -eq $art12) "splash: INSTALL after PREPARE-FOR-UPDATE`n$($r.Output)"
# Without the 1.1 install's backup the released art can't be found: refused, nothing changed.
$s4 = Join-Path $work 'splash-prepare-lost'
New-SplashGame $s4 $shadowSplash
$null = Install-As11 $s4
$null = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s4)
Remove-Item -LiteralPath (@(Get-ChildItem (Join-Path $s4 'runtime\server\deployment-backups') -Directory | Sort-Object Name)[0].FullName) -Recurse -Force
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s4, '-ToRelease')
Check ($r.Code -ne 0 -and $r.Output -match 'art995.dat' -and (ArtOf $s4) -eq $art12 -and (SplashOf $s4) -eq $ls12Splash -and
       (Sha (Join-Path $s4 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll']) "splash: PREPARE-FOR-UPDATE with no released copy of the art left is refused, nothing changed`n$($r.Output)"

# An unknown loading screen (neither shadowofze's nor ours): left alone by the install, ROLLBACK and PREPARE-FOR-UPDATE.
$s5 = Join-Path $work 'splash-unknown'
$otherSplash = 'someone else''s splash'
New-SplashGame $s5 $otherSplash
$r = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s5)
Check ($r.Code -eq 0 -and (SplashOf $s5) -eq $otherSplash -and $r.Output -match 'splash.mpk: not the released file \(nor an earlier Living Server one\), left alone') "splash: an unknown loading screen is left alone`n$($r.Output)"
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s5)
Check ($r.Code -eq 0 -and (SplashOf $s5) -eq $otherSplash -and (DllOf $s5) -eq 'original release server') "splash: ROLLBACK leaves the unknown loading screen`n$($r.Output)"
$null = RunIn $build12 'install-server-build.ps1' @('-GameFolder', $s5)
$r = RunIn $build12 'rollback-server-build.ps1' @('-GameFolder', $s5, '-ToRelease')
Check ($r.Code -eq 0 -and (SplashOf $s5) -eq $otherSplash) "splash: PREPARE-FOR-UPDATE leaves the unknown loading screen`n$($r.Output)"

# No game: refused.
$none = Run 'install-server-build.ps1' @('-GameFolder', (Join-Path $work 'missing'))
Check ($none.Code -ne 0) 'a missing game folder is refused'

Remove-Item -LiteralPath $work -Recurse -Force
if ($failures -gt 0) { Write-Host "$failures check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All installer checks passed.' -ForegroundColor Green
exit 0
