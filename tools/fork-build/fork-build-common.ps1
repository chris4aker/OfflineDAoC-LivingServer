# Shared by install-server-build.ps1 and rollback-server-build.ps1 (docs/RELEASE-PLAN.md).
# Windows PowerShell 5.1 compatible.

$Script:ServerFolders = @('runtime/server', 'runtime/server/lib', 'runtime/server/win-x64')
# The launcher (its Living Server tab): the game's own copy, and the 0.37c new client's copy when present.
$Script:LauncherFolders = @('runtime', 'new-client/runtime')
$Script:LauncherFiles = @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')
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
    $starts = @()
    $parent = Split-Path -Parent $BuildDir
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
# the game's loading screen (pregame/splash.mpk, shipped whole under content/).
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
