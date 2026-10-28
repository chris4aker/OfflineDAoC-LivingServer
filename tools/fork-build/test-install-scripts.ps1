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
function New-FakeGame([string]$root, [string]$edition, [switch]$NewClient, [string[]]$ReleaseNotes = @()) {
    $folders = @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')
    $lines = @()
    foreach ($folder in $folders) {
        $dir = Join-Path $root $folder
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir 'GameServer.dll'), "original release server")
        [IO.File]::WriteAllText((Join-Path $dir 'GameServer.pdb'), "original release symbols")
        $lines += "$(Sha (Join-Path $dir 'GameServer.dll'))  $folder/GameServer.dll"
        $lines += "$(Sha (Join-Path $dir 'GameServer.pdb'))  $folder/GameServer.pdb"
    }
    $lines += New-FakeLauncher (Join-Path $root 'runtime') 'runtime'
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
Check ((Sha (Join-Path $game 'runtime\OfflineDAoC.dll')) -eq $originalLauncher) 'rollback restores the original launcher'
Check ([IO.File]::ReadAllText((Join-Path $game 'new-client\runtime\OfflineDAoC.dll')) -eq 'original launcher OfflineDAoC.dll') 'rollback restores the new client launcher'
Check (!(Test-Path (Join-Path $game 'tools\claude-version\set_bot_guilds.py'))) 'rollback removes helpers it added'
Check (!(Test-Path (Join-Path $game 'tools\claude-version\set_bot_social.py'))) 'rollback removes the social switch'
$nothing = Run 'rollback-server-build.ps1' @('-GameFolder', $game)
Check ($nothing.Code -ne 0) 'a second rollback has nothing to undo'
$reinstall = Run 'install-server-build.ps1' @('-GameFolder', $game)
Check ($reinstall.Code -eq 0) 'install works again after a rollback'

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
       $fixInstall.Output -match 'not the released file, left alone' -and $fixInstall.Output -match 'not in this game, skipped') 'content: a changed or missing file is left alone'
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

# No game: refused.
$none = Run 'install-server-build.ps1' @('-GameFolder', (Join-Path $work 'missing'))
Check ($none.Code -ne 0) 'a missing game folder is refused'

Remove-Item -LiteralPath $work -Recurse -Force
if ($failures -gt 0) { Write-Host "$failures check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All installer checks passed.' -ForegroundColor Green
exit 0
