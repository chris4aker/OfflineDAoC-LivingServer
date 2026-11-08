# Undoes the most recent fork server build install (docs/RELEASE-PLAN.md).
#
#   ROLLBACK.cmd
#   ROLLBACK.cmd -GameFolder "C:\Games\OfflineDAoC\playable-v0.37b"
#   ROLLBACK.cmd -DryRun
#   PREPARE-FOR-UPDATE.cmd     (= ROLLBACK.cmd -ToRelease)
#
# Restores the GameServer.dll/.pdb files, the server libraries (CoreBase, CoreDatabase, CoreServer), the launcher files, helper scripts and game files the last install replaced or
# fixed, from its backup, and checks every restored file by SHA-256. It refuses if the installed files changed since
# (unless -Force). Run it again to undo the install before that one.
# Living Server's own loading screen (content-patches.json "Keep", 1.2, owner 2026-11-07) is the exception: the
# previous Living Server loading screen comes back, but never the released one, so after a first install it stays.
#
# -ToRelease puts back the game's released server and launcher files whatever was installed over them (fork builds,
# copies by hand): for each one it finds, among every backup in runtime\server\deployment-backups, a copy with the
# fingerprint the game's PACKAGE MANIFEST.sha256 gives it. An Offline DAoC update (0.37 and on) applies only to released
# files, and it tells the game's version by the launcher, so run this before updating; install this build again after.
# Game files an install fixed go back to the released copy found in any install's backup; the loading screen stays (the
# update doesn't need it: fork-build-common.ps1, Get-ContentPatchMap).
param(
    [string]$GameFolder = '',
    [switch]$DryRun,
    [switch]$Force,
    [switch]$ToRelease
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fork-build-common.ps1')

# A failure part-way through restoring: some files are already back, so don't say nothing changed.
function Stop-RestoreFailed([string]$Message) {
    Write-Host ''
    Write-Host "ROLLBACK STOPPED PART-WAY: $Message" -ForegroundColor Red
    Write-Host "The backup is untouched in $($record.BackupFolder). Close everything and run ROLLBACK.cmd -Force, or send Claude this output."
    exit 1
}

$game = Find-GameFolder $GameFolder $PSScriptRoot
Write-Host "Game folder: $game"
Assert-GameClosed

# The epic chains (Living Server 1.2): the server links them in memory as it reads the quests and never writes them to
# the world, so a rollback has nothing to undo there. A world that epic_chains.py --apply wrote them into (as Beta 1.1
# asked) still holds them, and the released server reads those links as quest names: it would never offer the Guild
# of Shadows again. epic_chains.py and the server leave this note beside the server for such a world.
$epicNote = Join-Path $game 'runtime\server\epic-chains-in-world.txt'
if (Test-Path -LiteralPath $epicNote) {
    if (!$Force) {
        Stop-WithMessage ("This world's quest rows hold the epic chain links that epic_chains.py wrote into them. The released server " +
            "reads them as quest names, so the Guild of Shadows would never be offered after this. Put them back first (Python " +
            "needed): python tools\claude-version\epic_chains.py rollback --apply - then run this again. -Force rolls back anyway.")
    }
    Write-Host 'The epic chain links stay written into this world (-Force); the Guild of Shadows will not be offered by the released server.' -ForegroundColor Yellow
}

if ($ToRelease) {
    $manifest = Read-PackageManifest $game
    if ($null -eq $manifest) { Stop-WithMessage "No PACKAGE MANIFEST.sha256 in $game, so the released files can't be told apart." }
    $clientManifest = Read-ManifestFile (Join-Path $game 'new-client\PACKAGE MANIFEST.sha256')
    $wanted = @()
    foreach ($folder in $Script:ServerFolders) { foreach ($name in @('GameServer.dll', 'GameServer.pdb')) { $wanted += "$folder/$name" } }
    foreach ($folder in $Script:ServerFolders) {
        foreach ($name in $Script:CoreFiles) { $wanted += "$folder/$name"; $wanted += "$folder/$([IO.Path]::ChangeExtension($name, '.pdb'))" }
    }
    foreach ($folder in $Script:LauncherFolders) { foreach ($name in $Script:LauncherFiles) { $wanted += "$folder/$name" } }
    $backups = Join-Path $game 'runtime\server\deployment-backups'
    $copies = @{}
    if (Test-Path -LiteralPath $backups) {
        foreach ($file in Get-ChildItem -LiteralPath $backups -Recurse -File) {
            $core = $Script:CoreFiles | Where-Object { $file.Name -eq $_ -or $file.Name -eq [IO.Path]::ChangeExtension($_, '.pdb') }
            if ($file.Name -like 'GameServer.*' -or $core -or $Script:LauncherFiles -contains $file.Name) {
                $hash = Get-Sha256 $file.FullName
                if (!$copies.ContainsKey($hash)) { $copies[$hash] = $file.FullName }
            }
        }
    }
    $restore = @(); $missing = @()
    foreach ($rel in $wanted) {
        $released = if ($rel -like 'new-client/*') { if ($clientManifest) { $clientManifest[$rel] } } else { $manifest[$rel] }
        if (!$released) { continue }                   # not part of this game (no new client, or not shipped)
        $path = Join-Path $game $rel
        $current = if (Test-Path -LiteralPath $path) { Get-Sha256 $path } else { '' }
        if ($current -eq $released) { continue }
        if ($copies.ContainsKey($released)) { $restore += [pscustomobject]@{ Path = $rel; From = $copies[$released]; Hash = $released } }
        else { $missing += $rel }
    }
    # Game files an install fixed in place (content-patches.json) go back to the released file, if what is there now is
    # one an install put there. An install may have replaced an earlier Living Server version (an entry's Replaces), so
    # the newest install's backup may hold that one: the released copy is looked for in every install's backup.
    # Living Server's own kept files (Keep: the loading screen) stay as they are.
    $buildPatches = Get-ContentPatchMap $PSScriptRoot
    $records = @(Get-InstallRecords $game)
    $fixed = @()
    foreach ($record in $records) {
        foreach ($entry in @($record.ContentFiles)) {
            if ($entry) { $fixed += [pscustomobject]@{ Entry = $entry; Backup = $record.BackupFolder } }
        }
    }
    foreach ($rel in @($fixed | ForEach-Object { [string]$_.Entry.Path } | Select-Object -Unique)) {
        $entries = @($fixed | Where-Object { $_.Entry.Path -eq $rel })
        if (@($entries | Where-Object { Test-ContentKeep $_.Entry $buildPatches }).Count -gt 0) {
            Write-Host "  $($rel): Living Server's own file, kept"
            continue
        }
        $ours = @($entries | ForEach-Object { ([string]$_.Entry.NewHash).ToLowerInvariant() })
        $patch = $buildPatches[$rel]
        if ($patch) { $ours += ([string]$patch.Fixed).ToLowerInvariant(); $ours += Get-ContentPatchReplaces $patch }
        $released = $null
        foreach ($item in $entries) { $released = Get-ContentReleased $item.Entry $buildPatches; if ($released) { break } }
        if (!$released) {
            # Records before 1.2 don't name it: the oldest file an install replaced that no install put there.
            $first = $entries | Where-Object { $ours -notcontains ([string]$_.Entry.OldHash).ToLowerInvariant() } | Select-Object -First 1
            if ($first) { $released = ([string]$first.Entry.OldHash).ToLowerInvariant() }
        }
        $path = Get-ContentPath $game $rel
        $current = if (Test-Path -LiteralPath $path) { Get-Sha256 $path } else { '' }
        if ($current -eq $released -or $ours -notcontains $current) { continue }   # released already, or not ours: left alone
        $from = $null
        foreach ($item in $entries) {
            if (!$released) { break }
            $saved = Join-Path (Join-Path $item.Backup 'content') ($rel.Replace('/', [IO.Path]::DirectorySeparatorChar))
            if ((Test-Path -LiteralPath $saved) -and (Get-Sha256 $saved) -eq $released) { $from = $saved; break }
        }
        if ($from) { $restore += [pscustomobject]@{ Path = $rel; From = $from; Hash = $released } }
        else { $missing += $rel }
    }
    if ($missing.Count -gt 0) {
        Stop-WithMessage ("No backup holds the released copy of:`n  " + ($missing -join "`n  ") +
            "`nThe update's downloader can repair the game (or install it fresh and bring your progress over).")
    }
    if ($restore.Count -eq 0) { Write-Host 'The released server and launcher files are already in place.' -ForegroundColor Green }
    foreach ($item in $restore) { Write-Host "  $($item.Path)  <-  $($item.From)" }
    if ($DryRun) { Write-Host ''; Write-Host 'Dry run: every check passed. Nothing was changed.' -ForegroundColor Green; exit 0 }
    foreach ($item in $restore) {
        $target = Get-ContentPath $game $item.Path
        Copy-Item -LiteralPath $item.From -Destination $target -Force
        if ((Get-Sha256 $target) -ne $item.Hash) {
            Write-Host "RESTORE STOPPED PART-WAY: $($item.Path) failed verification. Close everything and run it again." -ForegroundColor Red
            exit 1
        }
    }
    foreach ($open in @(Get-InstallRecords $game | Where-Object { !$_.RolledBack })) {
        Set-Content -LiteralPath (Join-Path $open.BackupFolder 'rolled-back.txt') -Value "Rolled back to the release at $(Get-Date -Format o)."
    }
    Write-Host ''
    Write-Host "The released server and launcher are back ($($restore.Count) file(s)). Now update the game, then run INSTALL.cmd of the" -ForegroundColor Green
    Write-Host 'Living Server build for the new version. Your settings stay in the database.' -ForegroundColor Green
    exit 0
}

$record = Get-InstallRecords $game | Where-Object { !$_.RolledBack } | Select-Object -Last 1
if ($null -eq $record) { Stop-WithMessage 'No fork build install to undo.' }
Write-Host "Undoing $($record.BuildName) installed $($record.Installed)"

foreach ($entry in $record.Folders) {
    $dll = Join-Path $game (Join-Path $entry.Folder 'GameServer.dll')
    $current = if (Test-Path -LiteralPath $dll) { Get-Sha256 $dll } else { '' }
    if ($current -ne $entry.NewDll -and !$Force) {
        Stop-WithMessage "$($entry.Folder)\GameServer.dll changed since that install (another build installed over it?). -Force restores the backup anyway."
    }
    $saved = Join-Path $record.BackupFolder (Get-BackupName $entry.Folder)
    if ((Get-Sha256 (Join-Path $saved 'GameServer.dll')) -ne $entry.OldDll) { Stop-WithMessage "The backup of $($entry.Folder) is damaged." }
}
foreach ($entry in @($record.CoreFiles)) {
    if ($null -eq $entry) { continue }
    $path = Join-Path $game (Join-Path $entry.Folder $entry.Name)
    $current = if (Test-Path -LiteralPath $path) { Get-Sha256 $path } else { '' }
    if ($current -ne $entry.NewHash -and !$Force) {
        Stop-WithMessage "$($entry.Folder)\$($entry.Name) changed since that install (another build installed over it?). -Force restores the backup anyway."
    }
    $saved = Join-Path $record.BackupFolder ((Get-BackupName $entry.Folder) + '_core')
    if ((Get-Sha256 (Join-Path $saved $entry.Name)) -ne $entry.OldHash) { Stop-WithMessage "The backup of $($entry.Folder)\$($entry.Name) is damaged." }
}
foreach ($entry in @($record.LauncherFolders)) {
    if ($null -eq $entry) { continue }
    $dll = Join-Path $game (Join-Path $entry.Folder 'OfflineDAoC.dll')
    $current = if (Test-Path -LiteralPath $dll) { Get-Sha256 $dll } else { '' }
    if ($current -ne $entry.NewDll -and !$Force) {
        Stop-WithMessage "$($entry.Folder)\OfflineDAoC.dll (the launcher) changed since that install. -Force restores the backup anyway."
    }
    $saved = Join-Path $record.BackupFolder ((Get-BackupName $entry.Folder) + '_launcher')
    foreach ($file in @($entry.Files)) {
        if ($file.OldHash -and (Get-Sha256 (Join-Path $saved $file.Name)) -ne $file.OldHash) { Stop-WithMessage "The launcher backup of $($entry.Folder) is damaged." }
    }
}
$contentRestore = @()
$buildPatches = Get-ContentPatchMap $PSScriptRoot
foreach ($entry in @($record.ContentFiles)) {
    if ($null -eq $entry) { continue }
    $path = Get-ContentPath $game $entry.Path
    $current = if (Test-Path -LiteralPath $path) { Get-Sha256 $path } else { '' }
    if ($current -eq $entry.OldHash) { continue }
    if ($current -ne $entry.NewHash) { Write-Host "  $($entry.Path) changed since that install; left as it is." -ForegroundColor Yellow; continue }
    # Living Server's own loading screen (Keep): the earlier Living Server one comes back, the released one never does.
    if ((Test-ContentKeep $entry $buildPatches) -and $entry.OldHash -eq (Get-ContentReleased $entry $buildPatches)) {
        Write-Host "  $($entry.Path): Living Server's own file, kept (the released one is not put back)."
        continue
    }
    $saved = Join-Path (Join-Path $record.BackupFolder 'content') ($entry.Path.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (!(Test-Path -LiteralPath $saved) -or (Get-Sha256 $saved) -ne $entry.OldHash) { Stop-WithMessage "The backup of $($entry.Path) is damaged." }
    $contentRestore += [pscustomobject]@{ Entry = $entry; Path = $path; Saved = $saved }
}
if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run: every check passed. Nothing was changed.' -ForegroundColor Green
    exit 0
}

foreach ($entry in $record.Folders) {
    $saved = Join-Path $record.BackupFolder (Get-BackupName $entry.Folder)
    $target = Join-Path $game $entry.Folder
    Copy-Item -LiteralPath (Join-Path $saved 'GameServer.dll') -Destination $target -Force
    if ($entry.OldPdb) { Copy-Item -LiteralPath (Join-Path $saved 'GameServer.pdb') -Destination $target -Force }
    elseif (Test-Path -LiteralPath (Join-Path $target 'GameServer.pdb')) { Remove-Item -LiteralPath (Join-Path $target 'GameServer.pdb') }
    if ((Get-Sha256 (Join-Path $target 'GameServer.dll')) -ne $entry.OldDll) { Stop-RestoreFailed "Restoring $($entry.Folder) failed verification." }
}
foreach ($entry in @($record.CoreFiles)) {
    if ($null -eq $entry) { continue }
    $saved = Join-Path $record.BackupFolder ((Get-BackupName $entry.Folder) + '_core')
    $target = Join-Path $game $entry.Folder
    Copy-Item -LiteralPath (Join-Path $saved $entry.Name) -Destination $target -Force
    if ($entry.OldPdb) { Copy-Item -LiteralPath (Join-Path $saved ([IO.Path]::ChangeExtension($entry.Name, '.pdb'))) -Destination $target -Force }
    if ((Get-Sha256 (Join-Path $target $entry.Name)) -ne $entry.OldHash) { Stop-RestoreFailed "Restoring $($entry.Folder)\$($entry.Name) failed verification." }
}
foreach ($entry in @($record.LauncherFolders)) {
    if ($null -eq $entry) { continue }
    $saved = Join-Path $record.BackupFolder ((Get-BackupName $entry.Folder) + '_launcher')
    $target = Join-Path $game $entry.Folder
    foreach ($file in @($entry.Files)) {
        $path = Join-Path $target $file.Name
        if ($file.OldHash) {
            Copy-Item -LiteralPath (Join-Path $saved $file.Name) -Destination $target -Force
            if ((Get-Sha256 $path) -ne $file.OldHash) { Stop-RestoreFailed "Restoring the launcher in $($entry.Folder) failed verification." }
        } elseif (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path
        }
    }
}
foreach ($helper in @($record.Helpers)) {
    if ($null -eq $helper) { continue }
    $target = Join-Path $game (Join-Path 'tools\claude-version' $helper.Name)
    if ($helper.Existed) {
        Copy-Item -LiteralPath (Join-Path $record.BackupFolder (Join-Path 'helpers' $helper.Name)) -Destination $target -Force
    } elseif ((Test-Path -LiteralPath $target) -and (Get-Sha256 $target) -eq $helper.New) {
        Remove-Item -LiteralPath $target
    }
}
foreach ($item in $contentRestore) {
    Copy-Item -LiteralPath $item.Saved -Destination $item.Path -Force
    if ((Get-Sha256 $item.Path) -ne $item.Entry.OldHash) { Stop-RestoreFailed "Restoring $($item.Entry.Path) failed verification." }
}
Set-Content -LiteralPath (Join-Path $record.BackupFolder 'rolled-back.txt') -Value "Rolled back at $(Get-Date -Format o)."
Write-Host ''
Write-Host "Rolled back $($record.BuildName). The files it replaced are back." -ForegroundColor Green
# Living Server 1.1.2: the world database is never rolled back (that would undo your play since that install).
if (Test-Path -LiteralPath (Join-Path $record.BackupFolder 'data\opendaoc.sqlite3.db')) {
    Write-Host "Your world database was left as it is. Its copy from before that install: $(Join-Path $record.BackupFolder 'data')"
}
Write-Host 'Settings it switched on stay in the database, where the original server ignores them. To clear them, turn'
Write-Host 'everything off in the Living Server tab before you roll back.'
exit 0
