# Shared by install-server-build.ps1 and rollback-server-build.ps1 (docs/RELEASE-PLAN.md).
# Windows PowerShell 5.1 compatible.

$Script:ServerFolders = @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')
# The launcher (its Living Server tab): the game's own copy, and the 0.37c new client's copy when present.
$Script:LauncherFolders = @('runtime', 'new-client/runtime')
$Script:LauncherFiles = @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')
# The server's own libraries beside GameServer.dll, shipped in the build's core\ folder (Living Server 1.1.2: save safety
# changed CoreDatabase, CoreBase and CoreServer, and a server whose GameServer.dll was new but CoreDatabase.dll released
# stopped at start, playtest 2026-11-04). Each one replaces the folder's copy only where the folder has one; its .pdb
# goes with it where the folder has a .pdb too.
$Script:CoreFiles = @('CoreBase.dll', 'CoreDatabase.dll', 'CoreServer.dll')
# Also the 0.37c new client and its launcher, which share the game's runtime.
$Script:ProcessNames = @('CoreServer', 'DOLServer', 'OfflineDAoC', 'game', 'camelot', 'connect', 'Godot_v4.7.2-stable_mono_win64')

function Stop-WithMessage([string]$Message) {
    Write-Host ''
    Write-Host "NOTHING WAS CHANGED: $Message" -ForegroundColor Red
    exit 1
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-KeyValueFile([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $values[$Matches[1]] = $Matches[2] }
    }
    return $values
}

function Test-GameFolder([string]$Path) {
    return $Path -and (Test-Path -LiteralPath (Join-Path $Path 'runtime\server\GameServer.dll'))
}

# The game folder: the one given, else the folder this build sits in (or its parents), or a
# playable*/OfflineDAoC* folder inside one of those.
function Find-GameFolder([string]$Given, [string]$BuildDir) {
    if ($Given) {
        $full = [IO.Path]::GetFullPath($Given)
        if (Test-GameFolder $full) { return $full }
        Stop-WithMessage "No Offline DAoC game in '$full' (runtime\server\GameServer.dll is missing)."
    }
    $parent = Split-Path -Parent $BuildDir
    # The documented place is inside the game folder: when the build sits there, that folder wins, even if other
    # playable* folders sit next to it (playtest 2026-11-04: three side-by-side installs refused to install).
    if ($parent -and (Test-GameFolder $parent)) { return [IO.Path]::GetFullPath($parent) }
    $starts = @()
    if ($parent) { $starts += $parent; $grand = Split-Path -Parent $parent; if ($grand) { $starts += $grand } }
    $starts += $BuildDir
    $found = @()
    foreach ($start in $starts) {
        if (Test-GameFolder $start) { $found += [IO.Path]::GetFullPath($start); continue }
        foreach ($child in @(Get-ChildItem -LiteralPath $start -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like 'playable*' -or $_.Name -like 'OfflineDAoC*' })) {
            if (Test-GameFolder $child.FullName) { $found += $child.FullName }
        }
    }
    $found = @($found | Select-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($found.Count -gt 1) {
        Stop-WithMessage ("More than one game folder found. Run again with -GameFolder and one of:`n  " + ($found -join "`n  "))
    }
    Stop-WithMessage ("Can't find the game. Put this build's folder inside your Offline DAoC folder (the one with " +
        "START OFFLINE DAOC.cmd), or run INSTALL.cmd -GameFolder ""C:\path\to\playable-v0.37b"".")
}

function Assert-GameClosed {
    $running = @(Get-Process -Name $Script:ProcessNames -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Stop-WithMessage ("Close the server, launcher and game first (running: " +
            (($running | ForEach-Object { $_.ProcessName } | Select-Object -Unique) -join ', ') + ').')
    }
}

# 'hash  relative/path' lines of the release's PACKAGE MANIFEST.sha256, keyed by path.
function Read-PackageManifest([string]$Game) {
    return Read-ManifestFile (Join-Path $Game 'PACKAGE MANIFEST.sha256')
}

# The same for any manifest file (0.37c's new client has its own: new-client\PACKAGE MANIFEST.sha256).
function Read-ManifestFile([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return $null }
    $manifest = @{}
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^([0-9a-fA-F]{64})\s+(.+)$') { $manifest[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
    }
    return $manifest
}

# Whether a release's PACKAGE MANIFEST.sha256 is Offline DAoC <base>. Each release lists its own docs/RELEASE-<version>.md and
# keeps the earlier ones, so the newest listed is the game's release (an updated 0.37 lists 0.36 and 0.37). An updated
# game has no editions\ folder (the 0.37 update removes it), so that check, which the 0.36 full downloads pass, is only
# the fallback for a manifest without release notes. Found on a real 0.36c game updated to 0.37c (2026-10-18).
# A build may fit several releases (BaseRelease=0.37,0.38 in BUILD-INFO.txt): once its server code carries a newer
# release's server changes, that release is added so the build installs on either. The game must be one of them.
function Get-BaseReleases([string]$base) {
    return @($base -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Test-ReleaseManifest($manifest, [string]$base) {
    $bases = Get-BaseReleases $base
    $versions = @($manifest.Keys | ForEach-Object { if ($_ -match '^docs/RELEASE-(\d+\.\d+)\.md$') { [version]$Matches[1] } })
    if ($versions.Count -gt 0) {
        $newest = $versions | Sort-Object | Select-Object -Last 1
        return [bool]($bases | Where-Object { [version]$_ -eq $newest } | Select-Object -First 1)
    }
    foreach ($one in $bases) {
        if ($manifest.Keys | Where-Object { $_ -like "editions/$one-no-custom-class/*" } | Select-Object -First 1) { return $true }
    }
    return $false
}

function Get-InstallRecords([string]$Game) {
    $root = Join-Path $Game 'runtime\server\deployment-backups'
    if (!(Test-Path -LiteralPath $root)) { return @() }
    return @(Get-ChildItem -LiteralPath $root -Directory -Filter 'fork-build-*' |
        Sort-Object Name |
        ForEach-Object {
            $file = Join-Path $_.FullName 'install-record.json'
            if (Test-Path -LiteralPath $file) {
                $record = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
                $record | Add-Member -NotePropertyName BackupFolder -NotePropertyValue $_.FullName -Force
                $record | Add-Member -NotePropertyName RolledBack -NotePropertyValue (Test-Path -LiteralPath (Join-Path $_.FullName 'rolled-back.txt')) -Force
                $record
            }
        })
}

function Get-BackupName([string]$Folder) { return ($Folder -replace '[\\/]', '_') }

# Game files this build fixes in place (content-patches.json, made by make_content_patch.py): byte patches against the
# released file, so no game file ships whole. A file is changed only when it has the released fingerprint, and the
# result must have the fixed one. Beta 1.0: Hall of the Corrupt's navmesh and eight new-client gate models; Beta 1.1:
# the game's loading screen (pregame/splash.mpk, shipped whole under content/); Beta 1.1.1: the new client's quest
# marker script (godot/game/quests/quest_markers.gd), a text file patched the same way; 1.2: 14 more new-client scripts
# from shadowofze's final code (game/net/server.gd, character_state, nif_particles, pregame, sky_system, npcs/name_plates,
# npcs/npc_state, ui/chat_window, item_db, merchant_window, panel_sheets, player_data, quickbar, trainer_window; the ones
# held back and why: docs/CLIENT-SCRIPTS-1.2.md).
function Read-ContentPatches([string]$BuildDir) {
    $path = Join-Path $BuildDir 'content-patches.json'
    if (!(Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

# An entry with "File" ships its fixed file whole in the build folder instead of byte ops (Living Server's own art, the
# game's loading screen, owner 2026-10-28): only for files that are entirely ours, never a game file.
function Get-ContentPatchFile($Patch, [string]$BuildDir) {
    $relative = [string]$Patch.File
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Bad content file path: $relative" }
    return (Join-Path $BuildDir ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}

# An entry with "File" may also name earlier Living Server versions of its file it replaces ("Replaces": SHA-256 list;
# Living Server 1.2: the 1.1 loading screen, which every 1.1 player has instead of the released one, owner 2026-11-07).
# Such a file is backed up and replaced like the released one. Only whole-file entries: byte ops need the released bytes.
# A file that is neither the released one nor one of these is never touched.
function Get-ContentPatchReplaces($Patch) {
    if (!$Patch.File -or $null -eq $Patch.Replaces) { return @() }
    return @($Patch.Replaces | ForEach-Object { ([string]$_).ToLowerInvariant() } | Where-Object { $_ })
}

# An entry with "File" and "Keep": true is a file of Living Server's own that stays when Living Server is rolled back
# (Living Server 1.2: the loading screen; owner 2026-11-07, "I don't want the original splash screens, I want mine").
# ROLLBACK.cmd puts back only an earlier Living Server version of it, never the released file: after a first install it
# leaves the file as it is. PREPARE-FOR-UPDATE.cmd (-ToRelease) leaves it too: the Offline DAoC patcher
# (source/tools/OfflineDaoc.Patcher) checks only the files an update replaces or removes, and pregame/splash.mpk is the
# same in 0.36, 0.36b and 0.37b (release-baselines), so the 0.37 update doesn't list it. Should an update ever list it,
# the patcher stops and names it (UPDATE-<version>-CHANGED-FILES.txt) before changing anything.
# Keyed by Path: the content patches of the build folder a script runs from.
function Get-ContentPatchMap([string]$BuildDir) {
    $map = @{}
    # Assigned first: Windows PowerShell 5.1's ConvertFrom-Json writes the whole list as one object, and a foreach over
    # the call itself would see that one object (CI 2026-11-07); the assignment unrolls it, as the installer's does.
    $patches = Read-ContentPatches $BuildDir
    foreach ($patch in $patches) { if ($patch.Path) { $map[[string]$patch.Path] = $patch } }
    return $map
}

# Whether an install record's content entry is a kept file: its own Keep (1.2 records on), or this build's entry for the
# path (records of 1.1 and earlier have no Keep).
function Test-ContentKeep($Entry, $BuildPatches) {
    if ($Entry.Keep) { return $true }
    $patch = $BuildPatches[[string]$Entry.Path]
    return [bool]($patch -and $patch.File -and $patch.Keep)
}

# The released file's SHA-256 for an install record's content entry: its own Released (1.2 records on), else this
# build's entry for the path, else $null.
function Get-ContentReleased($Entry, $BuildPatches) {
    if ($Entry.Released) { return ([string]$Entry.Released).ToLowerInvariant() }
    $patch = $BuildPatches[[string]$Entry.Path]
    if ($patch -and $patch.Released) { return ([string]$patch.Released).ToLowerInvariant() }
    return $null
}

function Expand-ContentPatch($Patch, [byte[]]$Released, [string]$BuildDir) {
    if ($Patch.File) { return ,([IO.File]::ReadAllBytes((Get-ContentPatchFile $Patch $BuildDir))) }
    $out = New-Object IO.MemoryStream
    foreach ($op in @($Patch.Ops)) {
        if ($null -ne $op.copy) { $out.Write($Released, [int]$op.copy[0], [int]$op.copy[1]) }
        else { $bytes = [Convert]::FromBase64String([string]$op.data); $out.Write($bytes, 0, $bytes.Length) }
    }
    return ,$out.ToArray()
}

function Get-BytesSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') }) -join '') } finally { $sha.Dispose() }
}

function Get-ContentPath([string]$Game, [string]$Relative) {
    return (Join-Path $Game ($Relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}

# The world database and its WAL files, when present (Living Server 1.1.2: the installer backs them up first). With the
# server closed, the database file and its -wal are a consistent set; -shm is only an index SQLite rebuilds.
function Get-WorldDatabaseFiles([string]$Game) {
    $db = Join-Path $Game 'runtime\data\opendaoc.sqlite3.db'
    if (!(Test-Path -LiteralPath $db)) { return @() }
    $files = @($db)
    foreach ($suffix in @('-wal', '-shm')) { if (Test-Path -LiteralPath "$db$suffix") { $files += "$db$suffix" } }
    return $files
}

function Get-FreeBytes([string]$Path) {
    try {
        $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path))
        return (New-Object IO.DriveInfo $root).AvailableFreeSpace
    } catch { return $null }
}

# Keeps the world database copies of the newest $Keep installs (each install's data\ folder) and removes older ones. Only
# the data\ folders go; the server and launcher backups stay, so ROLLBACK.cmd and -ToRelease work as before.
function Remove-OldDatabaseBackups([string]$Game, [int]$Keep) {
    $root = Join-Path $Game 'runtime\server\deployment-backups'
    if (!(Test-Path -LiteralPath $root)) { return 0 }
    $withData = @(Get-ChildItem -LiteralPath $root -Directory -Filter 'fork-build-*' | Sort-Object Name |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'data') })
    $old = if ($withData.Count -gt $Keep) { $withData[0..($withData.Count - $Keep - 1)] } else { @() }
    foreach ($folder in $old) { Remove-Item -LiteralPath (Join-Path $folder.FullName 'data') -Recurse -Force }
    return @($old).Count
}
