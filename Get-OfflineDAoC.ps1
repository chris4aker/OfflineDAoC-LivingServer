param(
    [string]$Destination = (Join-Path $PSScriptRoot 'playable'),
    [ValidatePattern('^\d+\.\d+(b|c)?$')]
    [string]$ReleaseVersion = '0.36b',
    # 0.36c only: add the new client to an existing 0.36b or 0.36 game folder instead of installing a new game.
    # 'pick' opens a folder picker.
    [string]$AddTo = '',
    # 0.36c only: a new install gets the 0.36 game (classic classes only) instead of 0.36b (with the Sluaghbinder).
    [switch]$Classic,
    # 0.37 and later: update an existing game folder ('pick' opens a folder picker) instead of a new install.
    [string]$Update = '',
    # 0.37 and later, when files the update replaces were changed: ask, or answer yes (fresh install + progress import) / no.
    [ValidateSet('ask','yes','no')]
    [string]$IfChanged = 'ask',
    # 0.37c and later, updating a game that has no new client yet: ask, or add it (yes) / leave it out (no).
    [ValidateSet('ask','yes','no')]
    [string]$AddClient = 'ask',
    # Testing only: read release assets from <folder>/v<version>/ instead of GitHub.
    [string]$LocalAssets = ''
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-ExtendedPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if ($full.StartsWith('\\?\',[StringComparison]::Ordinal)) { return $full }
    if ($full.StartsWith('\\',[StringComparison]::Ordinal)) {
        return '\\?\UNC\' + $full.Substring(2)
    }
    return '\\?\' + $full
}

function Test-ExistsLong([string]$path) {
    $extended = Get-ExtendedPath $path
    return (Test-Path -LiteralPath $path) -or
        [IO.File]::Exists($extended) -or [IO.Directory]::Exists($extended)
}

function Move-FailedNewCopyAside([string]$path, [string]$parent, [string]$leaf) {
    # Only recover an exact, new child path that this downloader reserved. A
    # rename preserves the incomplete files for inspection and frees the name
    # for a retry without recursively deleting a player's data.
    $expected = [IO.Path]::GetFullPath((Join-Path $parent $leaf))
    $candidate = [IO.Path]::GetFullPath($path)
    if (![string]::Equals($candidate, $expected, [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($candidate, [IO.Path]::GetPathRoot($candidate), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "Refusing to move an unexpected failed download path: $candidate"
        return
    }
    $item = Get-Item -LiteralPath $candidate -Force -ErrorAction SilentlyContinue
    if (!$item) { return }
    if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Write-Warning "The failed download path needs manual inspection: $candidate"
        return
    }
    $aside = Join-Path $parent ($leaf + '.failed-' + [guid]::NewGuid().ToString('N').Substring(0,8))
    try {
        Move-Item -LiteralPath $candidate -Destination $aside
        Write-Warning "Preserved an incomplete download at $aside. Rerun the helper to retry; the verified download cache is retained."
    } catch {
        Write-Warning "Could not move the incomplete folder at $candidate. Do not launch it; rename it manually before retrying."
    }
}

# Living Server keeps its own copy of the Offline DAoC release files (chris4aker/OfflineDAoC-base-files, checked against
# the original SHA256SUMS): the original repository, shadowofze/OfflineDAoC, went offline in November 2026.
$releaseBase = "https://github.com/chris4aker/OfflineDAoC-base-files/releases/download/v$ReleaseVersion"
# Windows PowerShell's own download is several times slower while it draws its progress bar, so release files come down
# with Windows' built-in curl.exe (fast, with a progress bar, retries, and resuming a part that was cut off midway);
# PowerShell, without its progress bar, is only the fallback.
$ProgressPreference = 'SilentlyContinue'
function Get-ReleaseAsset([string]$Name, [string]$OutFile, [int64]$Bytes = -1) {
    Get-AssetFrom $ReleaseVersion $Name $OutFile $Bytes
}
# A file of any release (a patch's parts live on their own release page: the game patch on v<x>b, the client on v<x>c).
function Get-AssetFrom([string]$Release, [string]$Name, [string]$OutFile, [int64]$Bytes = -1) {
    $releaseBase = "https://github.com/chris4aker/OfflineDAoC-base-files/releases/download/v$Release"
    if ($LocalAssets) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $LocalAssets "v$Release") $Name) -Destination $OutFile -Force
        return
    }
    # Only a shorter, unfinished part is continued; anything else is fetched from the start.
    $existing = Get-Item -LiteralPath $OutFile -ErrorAction SilentlyContinue
    if ($existing -and ($Bytes -lt 0 -or $existing.Length -ge $Bytes)) { Remove-Item -LiteralPath $OutFile -Force }
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (Test-Path -LiteralPath $curl -PathType Leaf) {
        & $curl --location --fail --retry 5 --retry-delay 5 --continue-at - --progress-bar --output $OutFile "$releaseBase/$Name"
        if ($LASTEXITCODE -eq 0) { return }
        Write-Warning "curl.exe could not download $Name (code $LASTEXITCODE); trying again with PowerShell."
        Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
    }
    Invoke-WebRequest -UseBasicParsing -Uri "$releaseBase/$Name" -OutFile $OutFile
}
$rootFolder = "OfflineDAoC-v$ReleaseVersion"
$partPattern = '^' + [regex]::Escape($rootFolder) + '\.zip\.\d{3}$'
$target = [IO.Path]::GetFullPath($Destination)
# 0.36c can finish an interrupted install in the same folder, or add the client to an existing 0.36b game.
$patchRelease = [version]($ReleaseVersion -replace '[bc]$','') -ge [version]'0.37'
if ((Test-ExistsLong $target) -and !($ReleaseVersion -like '*c') -and !$patchRelease) { throw 'Destination already exists. Choose a NEW folder; existing games and saves are never overwritten.' }
$cache = Join-Path $PSScriptRoot (Join-Path '.downloads' "v$ReleaseVersion")
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$manifestPath = Join-Path $cache 'download-manifest.json'
Write-Host 'Downloading the release manifest...'
Get-ReleaseAsset 'download-manifest.json' $manifestPath
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Version -ne $ReleaseVersion) { throw 'Unexpected release manifest version.' }

# v0.31b is an optional, copy-first expansion. It downloads the immutable
# v0.31 playable baseline, then applies the separately hash-verified
# Sluaghbinder patch into a new sibling folder. The old v0.3/v0.31 paths below
# remain unchanged, so anyone who does not want the class can keep using them.
if ($manifest.Mode -eq 'optional-patch') {
    $expectedBase = switch ($ReleaseVersion) {
        '0.31b' { '0.31' }
        '0.32b' { '0.32' }
        default { throw 'Unexpected optional release version.' }
    }
    $expectedPatchRoot = "OfflineDAoC-Sluaghbinder-v$ReleaseVersion-patch"
    if ($manifest.BaseVersion -ne $expectedBase -or !$manifest.PatchName -or
        $manifest.PatchRootFolder -ne $expectedPatchRoot -or
        $manifest.PatchSHA256 -notmatch '^[a-f0-9]{64}$') { throw "Unexpected v$ReleaseVersion optional patch manifest." }
    $parent = Split-Path -Parent $target
    $leaf = Split-Path -Leaf $target
    $baseTarget = Join-Path $parent ($leaf + "-v$expectedBase-base")
    if (Test-ExistsLong $baseTarget) { throw "Base staging folder already exists: $baseTarget" }
    $legacyBaseTarget = if ($ReleaseVersion -eq '0.32b') { $baseTarget + '-v0.31-base' } else { $null }
    if ($legacyBaseTarget -and (Test-ExistsLong $legacyBaseTarget)) {
        throw "Legacy base staging folder already exists: $legacyBaseTarget"
    }
    try {
    Write-Host "Downloading the preserved v$expectedBase playable baseline first..."
    & $PSCommandPath -ReleaseVersion $manifest.BaseVersion -Destination $baseTarget
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "The v$expectedBase baseline download failed." }

    $patchPath = Join-Path $cache $manifest.PatchName
    $patchValid = (Test-Path -LiteralPath $patchPath) -and
        (Get-Item -LiteralPath $patchPath).Length -eq [int64]$manifest.PatchBytes
    if ($patchValid) { $patchValid = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -eq $manifest.PatchSHA256 }
    if (!$patchValid) {
        Write-Host "Downloading $($manifest.PatchName)..."
        Invoke-WebRequest -UseBasicParsing -Uri "$releaseBase/$($manifest.PatchName)" -OutFile $patchPath
        if ((Get-Item -LiteralPath $patchPath).Length -ne [int64]$manifest.PatchBytes -or
            (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -ne $manifest.PatchSHA256) {
            throw "Sluaghbinder patch verification failed. The v$expectedBase baseline is intact; run again to retry."
        }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # A release helper can live in a very deep Downloads folder. Extracting
    # below .downloads would push nested source paths past Windows PowerShell
    # 5.1's path limit. Use a short, unique temporary folder instead.
    $patchExtractParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $patchExtractLeaf = 'odcp-' + [guid]::NewGuid().ToString('N').Substring(0,12)
    $patchExtract = [IO.Path]::GetFullPath((Join-Path $patchExtractParent $patchExtractLeaf))
    if (Test-Path -LiteralPath $patchExtract) { throw "Patch extraction folder already exists: $patchExtract" }
    New-Item -ItemType Directory -Path $patchExtract | Out-Null
    try {
    $zip = [IO.Compression.ZipFile]::OpenRead($patchPath)
    try {
        $prefix = $manifest.PatchRootFolder + '/'
        $extractPrefix = $patchExtract.TrimEnd('\') + '\'
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            if (!$entryName.StartsWith($prefix,[StringComparison]::Ordinal) -or
                $entryName.Contains(':') -or $entryName.Contains('/../') -or $entryName.EndsWith('/..')) { throw 'Unexpected Sluaghbinder patch layout.' }
            $relative = $entryName.Substring($prefix.Length)
            if (!$relative) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $patchExtract $relative))
            if (!$resolved.StartsWith($extractPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe Sluaghbinder patch entry.' }
            if ($resolved.Length -ge 240) {
                throw 'The Windows temporary folder is too deep for this patch. Set TEMP to a shorter writable folder and retry.'
            }
        }
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('\','/').Substring($prefix.Length)
            if (!$relative) { continue }
            $resolved = Join-Path $patchExtract $relative
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $resolved -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $resolved) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$resolved,$false)
        }
    } finally { $zip.Dispose() }
    $installer = Join-Path $patchExtract 'Install-Sluaghbinder.ps1'
    if (!(Test-Path -LiteralPath $installer)) { throw 'Sluaghbinder patch installer is missing.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -BasePath $baseTarget -Destination $target
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Sluaghbinder patch installation failed; the v$expectedBase baseline remains intact." }
    Write-Host "Verified v$ReleaseVersion optional Sluaghbinder expansion and installed it to $target"
    Write-Host "The unmodified v$expectedBase staging copy is at $baseTarget for rollback."
    } finally {
        # The ZIP is retained in .downloads. Only this exact generated temp
        # child is discarded, never an existing game or a redirected folder.
        $expectedExtract = [IO.Path]::GetFullPath((Join-Path $patchExtractParent $patchExtractLeaf))
        $extractItem = Get-Item -LiteralPath $patchExtract -Force -ErrorAction SilentlyContinue
        if ($extractItem -and
            [string]::Equals($patchExtract, $expectedExtract, [StringComparison]::OrdinalIgnoreCase) -and
            $extractItem.PSIsContainer -and
            -not ($extractItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            try { Remove-Item -LiteralPath $patchExtract -Recurse -Force }
            catch { Write-Warning "Temporary patch files could not be removed from $patchExtract." }
        }
    }
    exit 0
    } catch {
        $failure = $_
        if ($ReleaseVersion -eq '0.32b') {
            Move-FailedNewCopyAside $target $parent $leaf
            Move-FailedNewCopyAside $baseTarget $parent ($leaf + '-v0.32-base')
            Move-FailedNewCopyAside $legacyBaseTarget $parent ($leaf + '-v0.32-base-v0.31-base')
        }
        throw $failure
    }
}

# v0.32 is a copy-first update of the preserved v0.31 playable baseline. The
# old folder is kept alongside the new one for a straightforward rollback. A
# clean world snapshot is used only in the NEW copy, never in an existing save.
if ($manifest.Mode -eq 'delta' -and $ReleaseVersion -eq '0.32') {
    $expectedPatchRoot = 'OfflineDAoC-v0.32-darkness-falls-beta-update'
    if ($manifest.BaseVersion -ne '0.31' -or !$manifest.PatchName -or
        $manifest.PatchRootFolder -ne $expectedPatchRoot -or
        $manifest.PatchSHA256 -notmatch '^[a-f0-9]{64}$') {
        throw 'Unexpected v0.32 update manifest.'
    }
    $parent = Split-Path -Parent $target
    $leaf = Split-Path -Leaf $target
    $baseTarget = Join-Path $parent ($leaf + '-v0.31-base')
    if (Test-ExistsLong $baseTarget) { throw "Base staging folder already exists: $baseTarget" }
    try {
    Write-Host 'Downloading a fresh v0.31 playable baseline for rollback...'
    & $PSCommandPath -ReleaseVersion '0.31' -Destination $baseTarget
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'The v0.31 baseline download failed.' }

    $patchPath = Join-Path $cache $manifest.PatchName
    $patchValid = (Test-Path -LiteralPath $patchPath) -and
        (Get-Item -LiteralPath $patchPath).Length -eq [int64]$manifest.PatchBytes
    if ($patchValid) { $patchValid = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -eq $manifest.PatchSHA256 }
    if (!$patchValid) {
        Write-Host "Downloading $($manifest.PatchName)..."
        Invoke-WebRequest -UseBasicParsing -Uri "$releaseBase/$($manifest.PatchName)" -OutFile $patchPath
        if ((Get-Item -LiteralPath $patchPath).Length -ne [int64]$manifest.PatchBytes -or
            (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -ne $manifest.PatchSHA256) {
            throw 'v0.32 update verification failed. The v0.31 baseline remains intact; run again to retry.'
        }
    }

    [IO.Directory]::CreateDirectory((Get-ExtendedPath $target)) | Out-Null
    & robocopy.exe $baseTarget $target /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
    if ($LASTEXITCODE -gt 7) { throw 'Copying the v0.31 base into the new v0.32 folder failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($patchPath)
    try {
        $prefix = $expectedPatchRoot + '/'
        $targetPrefix = $target.TrimEnd('\') + '\'
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            if (!$entryName.StartsWith($prefix,[StringComparison]::Ordinal) -or
                $entryName.Contains(':') -or $entryName.Contains('/../') -or
                $entryName.EndsWith('/..')) { throw 'Unexpected v0.32 update layout.' }
            $relative = $entryName.Substring($prefix.Length)
            if (!$relative) { continue }
            if (!$seen.Add($relative)) { throw "Duplicate v0.32 update entry: $relative" }
            $resolved = [IO.Path]::GetFullPath((Join-Path $target $relative))
            if (!$resolved.StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase)) {
                throw 'Unsafe v0.32 update entry.'
            }
        }
        foreach ($entry in $zip.Entries) {
            $relative = $entry.FullName.Replace('\','/').Substring($prefix.Length)
            if (!$relative) { continue }
            $resolved = Join-Path $target $relative
            if ($entry.FullName.EndsWith('/')) {
                [IO.Directory]::CreateDirectory((Get-ExtendedPath $resolved)) | Out-Null
                continue
            }
            [IO.Directory]::CreateDirectory((Get-ExtendedPath (Split-Path -Parent $resolved))) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Get-ExtendedPath $resolved),$true)
        }
    } finally { $zip.Dispose() }
    foreach ($required in @('runtime\OfflineDAoC.exe','runtime\server\GameServer.dll',
            'runtime\server\navmesh\zone249.nav','runtime\data\opendaoc.sqlite3.db')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $required) -PathType Leaf)) {
            throw "The v0.32 copy is missing $required. The v0.31 base remains intact."
        }
    }
    Write-Host "Verified v0.32 Darkness Falls Beta in $target"
    Write-Host "The untouched v0.31 baseline is at $baseTarget for rollback. Nothing was started automatically."
    exit 0
    } catch {
        $failure = $_
        Move-FailedNewCopyAside $target $parent $leaf
        Move-FailedNewCopyAside $baseTarget $parent ($leaf + '-v0.31-base')
        throw $failure
    }
}

# v0.31 is intentionally a small, hash-verified update over the immutable v0.3
# playable seed. This legacy download path remains unchanged.
if ($manifest.Mode -eq 'delta') {
    if ($manifest.BaseVersion -ne '0.3' -or !$manifest.PatchName -or
        $manifest.PatchRootFolder -ne 'OfflineDAoC-v0.31-update' -or
        $manifest.PatchSHA256 -notmatch '^[a-f0-9]{64}$') { throw 'Unexpected v0.31 update manifest.' }

    Write-Host 'Downloading the preserved v0.3 playable seed first...'
    & $PSCommandPath -ReleaseVersion $manifest.BaseVersion -Destination $target
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'The v0.3 seed download failed.' }

    $patchPath = Join-Path $cache $manifest.PatchName
    $patchValid = (Test-Path -LiteralPath $patchPath) -and
        (Get-Item -LiteralPath $patchPath).Length -eq [int64]$manifest.PatchBytes
    if ($patchValid) { $patchValid = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -eq $manifest.PatchSHA256 }
    if (!$patchValid) {
        Write-Host "Downloading $($manifest.PatchName)..."
        Invoke-WebRequest -UseBasicParsing -Uri "$releaseBase/$($manifest.PatchName)" -OutFile $patchPath
        if ((Get-Item -LiteralPath $patchPath).Length -ne [int64]$manifest.PatchBytes -or
            (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash -ne $manifest.PatchSHA256) {
            throw 'v0.31 update verification failed. The v0.3 seed is intact; run again to retry.'
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($patchPath)
    try {
        $prefix = $manifest.PatchRootFolder + '/'
        $targetPrefix = $target.TrimEnd('\') + '\'
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            if (!$entryName.StartsWith($prefix,[StringComparison]::Ordinal) -or
                $entryName.Contains(':')) { throw 'Unexpected v0.31 update layout.' }
            $relative = $entryName.Substring($prefix.Length)
            if (!$relative) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $target $relative))
            if (!$resolved.StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe v0.31 update entry.' }
        }
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            $relative = $entryName.Substring($prefix.Length)
            if (!$relative) { continue }
            $resolved = Join-Path $target $relative
            if ($entryName.EndsWith('/')) { [IO.Directory]::CreateDirectory((Get-ExtendedPath $resolved)) | Out-Null; continue }
            [IO.Directory]::CreateDirectory((Get-ExtendedPath (Split-Path -Parent $resolved))) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Get-ExtendedPath $resolved),$true)
        }
    } finally { $zip.Dispose() }
    $worldPatcher = Join-Path $target 'runtime\world-patches\OfflineDaoc.WorldPatch.exe'
    $beachRatSql = Join-Path $target 'tools\world-patches\shannon-beach-rat-camp.sql'
    $worldDatabase = Join-Path $target 'runtime\data\opendaoc.sqlite3.db'
    if (!(Test-Path -LiteralPath $worldPatcher -PathType Leaf) -or
        !(Test-Path -LiteralPath $beachRatSql -PathType Leaf)) {
        throw 'The v0.31 update is missing its Shannon Estuary world patch. Use the latest release manifest and update asset.'
    }
    $worldPatchArgs = '--database "' + $worldDatabase + '" --sql "' + $beachRatSql + '"'
    $worldPatchProcess = Start-Process -FilePath $worldPatcher -ArgumentList $worldPatchArgs -Wait -PassThru -NoNewWindow
    if ($worldPatchProcess.ExitCode -ne 0) {
        throw 'The Shannon Estuary world patch failed in the new copy. The original installation was not changed.'
    }
    Write-Host "Verified v0.31 update and applied it to $target"
    Write-Host 'Read the included README.md and docs, then open START OFFLINE DAOC.cmd. Nothing was started automatically.'
    exit 0
}

# From 0.37 on a version is an update patch on the 0.36 base (0.36 / 0.36b / 0.36c stay the full downloads):
#  -Update <folder|pick>  brings an existing game up to this version: the game patch (from the v<x>b release) and, when
#                         the folder has the new client, the client patch (from v<x>c). The patcher (shipped in the
#                         patch, run with the game's own .NET) checks that every file it replaces is as released,
#                         backs up, applies and puts everything back if anything fails.
#  (no -Update)           a new install: the 0.36 base release first (NewInstallBase; 0.36c asks 0.36b or 0.36 with
#                         -Classic), then the same patches.
# If files the update would replace were changed by the player, it offers a fresh install of this version in a new
# folder and brings the account, characters and bots over with the progress importer (-IfChanged ask|yes|no).
if ($manifest.Mode -eq 'patch') {
    if ($manifest.BaseVersions -notcontains '0.36' -or !$manifest.GamePatch -or !$manifest.ClientPatch -or !$manifest.NewInstallBase) {
        throw "Unexpected v$ReleaseVersion manifest."
    }

    function Get-PatchFolder($ref, [string]$what) {
        $partCache = Join-Path $PSScriptRoot (Join-Path '.downloads' ('v' + $ref.Release))
        New-Item -ItemType Directory -Path $partCache -Force | Out-Null
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('odu-' + [guid]::NewGuid().ToString('N').Substring(0,10))
        New-Item -ItemType Directory -Path $tmp | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $prefix = $ref.RootFolder + '/'
        $tmpPrefix = $tmp.TrimEnd('\') + '\'
        $index = 0
        foreach ($part in $ref.Parts) {
            $index++
            if ($part.Name -notmatch '^[A-Za-z0-9._-]+\.zip$' -or $part.SHA256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid update metadata.' }
            $path = Join-Path $partCache $part.Name
            $valid = (Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -eq $part.Bytes
            if ($valid) { $valid = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $part.SHA256 }
            if (!$valid) {
                Write-Host ("Downloading the {0}, part {1} of {2} ({3:N0} MB)..." -f $what, $index, $ref.Parts.Count, ($part.Bytes / 1MB))
                Get-AssetFrom $ref.Release $part.Name $path ([int64]$part.Bytes)
                if ((Get-Item -LiteralPath $path).Length -ne $part.Bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $part.SHA256) {
                    throw 'Download verification failed. Run this file again to retry; finished parts are kept.'
                }
            }
            $zip = [IO.Compression.ZipFile]::OpenRead($path)
            try {
                foreach ($entry in $zip.Entries) {
                    $name = $entry.FullName.Replace('\','/')
                    if (!$name.StartsWith($prefix,[StringComparison]::Ordinal) -or $name.Contains(':') -or
                        $name.Contains('/../') -or $name.EndsWith('/..')) { throw 'Unexpected update archive layout.' }
                    $resolved = [IO.Path]::GetFullPath((Join-Path $tmp $name))
                    if (!$resolved.StartsWith($tmpPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe update archive entry.' }
                }
                foreach ($entry in $zip.Entries) {
                    $resolved = Join-Path $tmp $entry.FullName.Replace('/','\')
                    if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory((Get-ExtendedPath $resolved)) | Out-Null; continue }
                    [IO.Directory]::CreateDirectory((Get-ExtendedPath (Split-Path -Parent $resolved))) | Out-Null
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Get-ExtendedPath $resolved),$true)
                }
            } finally { $zip.Dispose() }
        }
        return (Join-Path $tmp $ref.RootFolder)
    }

    function Remove-PatchFolder([string]$dir) {
        $top = Split-Path -Parent $dir
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if ((Split-Path -Leaf $top) -like 'odu-*' -and [IO.Path]::GetFullPath((Split-Path -Parent $top)).TrimEnd('\') -eq $tempRoot) {
            Remove-Item -LiteralPath $top -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    function Invoke-Patch([string]$game, $ref, [string]$what) {
        $dotnet = Join-Path $game 'tools\dotnet\dotnet.exe'
        if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "The game in $game has no bundled .NET (tools\dotnet). Use a new install instead." }
        if ($ref.Identity) {
            # the same version check the patcher makes, before anything is downloaded
            $idFile = Join-Path $game $ref.Identity.Path.Replace('/','\')
            $current = if (Test-Path -LiteralPath $idFile -PathType Leaf) { (Get-FileHash -LiteralPath $idFile -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'absent' }
            if ($current -eq $ref.Identity.To) { Write-Host "The $what is already installed in $game."; return 2 }
            if (@($ref.Identity.From) -notcontains $current) { return 4 }
        }
        $dir = Get-PatchFolder $ref $what
        try {
            Write-Host "Applying the $what..."
            # Out-Host: the patcher's own lines go to the screen, so the function returns only its exit code
            & $dotnet (Join-Path $dir 'patcher\OfflineDaoc.Patcher.dll') --game $game --patch $dir | Out-Host
            return [int]$LASTEXITCODE
        } finally { Remove-PatchFolder $dir }
    }

    $label = $manifest.Title
    if ($Update) {
        if ($Update -eq 'pick') {
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = 'Choose your Offline DAoC game folder (the one with START OFFLINE DAOC.cmd in it).'
            $dialog.ShowNewFolderButton = $false
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No folder was chosen. Nothing was changed.' }
            $Update = $dialog.SelectedPath
        }
        $game = [IO.Path]::GetFullPath($Update)
        if (!(Test-Path -LiteralPath (Join-Path $game 'runtime\OfflineDAoC.exe') -PathType Leaf)) {
            throw "No Offline DAoC game was found in $game. Choose the folder that holds START OFFLINE DAOC.cmd."
        }
    } else {
        $game = $target
        $marker = Join-Path $cache ('base-installed-' + [BitConverter]::ToString(
            [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($game.ToLowerInvariant()))).Replace('-','').Substring(0,16) + '.txt')
        if (!(Test-ExistsLong $game)) {
            $base = $manifest.NewInstallBase
            Write-Host "Step 1 of 2: the Offline DAoC $base game this version builds on."
            $global:LASTEXITCODE = 0   # a nested run that succeeds sets no exit code; don't read an older one
            if ($Classic) { & $PSCommandPath -ReleaseVersion $base -Destination $game -LocalAssets $LocalAssets -Classic }
            else { & $PSCommandPath -ReleaseVersion $base -Destination $game -LocalAssets $LocalAssets }
            if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "The $base download failed. Run this file again to retry." }
            Set-Content -LiteralPath $marker -Value $game -Encoding ASCII
        } elseif (!(Test-Path -LiteralPath $marker)) {
            throw "The folder $game exists, but the install in it did not finish. Rename or delete that folder and run this file again (finished downloads are kept)."
        }
        Write-Host "Step 2 of 2: updating it to $ReleaseVersion."
    }

    $hasClient = Test-Path -LiteralPath (Join-Path $game 'new-client\VERSION.txt')
    $need = [int64]0
    foreach ($p in $manifest.GamePatch.Parts) { $need += 2 * [int64]$p.UnpackedBytes }
    if ($hasClient) { foreach ($p in $manifest.ClientPatch.Parts) { $need += 2 * [int64]$p.UnpackedBytes } }
    $dbFile = Join-Path $game 'runtime\data\opendaoc.sqlite3.db'
    if (Test-Path -LiteralPath $dbFile) { $need += (Get-Item -LiteralPath $dbFile).Length }
    $need += 1GB
    $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($game))
    if ($drive.AvailableFreeSpace -lt $need) {
        throw ("The update needs about {0:N1} GB of free disk space on {1} ({2:N1} GB free). Free up space and run this again." -f ($need / 1GB), $drive.Name, ($drive.AvailableFreeSpace / 1GB))
    }

    $code = Invoke-Patch $game $manifest.GamePatch 'game update'
    if ($code -eq 3) {
        if (!$Update) { throw "The new install in $game could not be updated. Rename or delete that folder and run this file again." }
        Write-Host ''
        Write-Host 'Some game files in that folder were changed since they were installed (see the list above), so the'
        Write-Host 'update cannot be applied to it safely. Nothing was changed.'
        $choice = $IfChanged
        if ($choice -eq 'ask') {
            $answer = Read-Host "Install a fresh Offline DAoC $ReleaseVersion in a new folder next to it and bring your account, characters, items and bots over? (Y/N)"
            $choice = if ($answer -match '^\s*[Yy]') { 'yes' } else { 'no' }
        }
        if ($choice -ne 'yes') { throw 'Nothing was changed. Your game is still the version it was.' }
        $classicGame = Test-Path -LiteralPath (Join-Path $game 'EDITION.txt')
        $fresh = $game.TrimEnd('\') + "-v$($manifest.PatchVersion)"
        $n = 2
        while (Test-ExistsLong $fresh) { $fresh = $game.TrimEnd('\') + "-v$($manifest.PatchVersion)-$n"; $n++ }
        $release = if ($hasClient) { "$($manifest.PatchVersion)c" } elseif ($classicGame) { $manifest.PatchVersion } else { "$($manifest.PatchVersion)b" }
        Write-Host "Installing a fresh Offline DAoC $release in $fresh ..."
        $global:LASTEXITCODE = 0   # a nested run that succeeds sets no exit code; don't read an older one
        if ($hasClient -and $classicGame) { & $PSCommandPath -ReleaseVersion $release -Destination $fresh -LocalAssets $LocalAssets -Classic }
        else { & $PSCommandPath -ReleaseVersion $release -Destination $fresh -LocalAssets $LocalAssets }
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'The fresh install did not finish. Run this file again to retry.' }
        Write-Host 'Bringing your progress over from the old folder (it is only read, never changed)...'
        $importer = Join-Path $fresh 'tools\ProgressImporter\OfflineDaoc.ProgressImport.exe'
        $report = Join-Path $fresh 'PROGRESS IMPORT REPORT.txt'
        $env:DOTNET_ROOT = Join-Path $fresh 'tools\dotnet'
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $env:DOTNET_MULTILEVEL_LOOKUP = '0'
        $import = Start-Process -FilePath $importer -ArgumentList @('--import', "`"$game`"", "`"$fresh`"", '--replace-progress', "`"$report`"") -Wait -PassThru
        if ($import.ExitCode -ne 0) {
            throw "The progress transfer did not finish: see $report. The fresh install in $fresh is ready, and your old folder was not changed; you can run IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd there."
        }
        Write-Host ''
        Write-Host "Done. Your game is now in $fresh, with your progress. Your old folder was not changed."
        exit 0
    }
    if ($code -eq 5) { throw 'Close the Offline DAoC launcher, the server and the game, then run this file again.' }
    if ($code -eq 4) { throw "The game in $game is not a released version this update knows. Choose 2 (New install) instead; IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd can bring your progress over." }
    if ($code -ne 0 -and $code -ne 2) { throw 'The update failed and your game was put back as it was. Run this file again to retry.' }

    if (!$hasClient -and $ReleaseVersion -like '*c') {
        $choice = $AddClient
        if ($choice -eq 'ask') {
            Write-Host ''
            $answer = Read-Host 'This game does not have the new client yet. Add it now (about 30 GB to download, 40 GB of free space needed)? (Y/N)'
            $choice = if ($answer -match '^\s*[Yy]') { 'yes' } else { 'no' }
        }
        if ($choice -eq 'yes') {
            Write-Host 'Adding the new client...'
            $global:LASTEXITCODE = 0   # a nested run that succeeds sets no exit code; don't read an older one
            & $PSCommandPath -ReleaseVersion "$($manifest.BaseVersions[0])c" -AddTo $game -LocalAssets $LocalAssets
            if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'The new client was not added completely. Run this file again to continue.' }
            $hasClient = $true
        }
    }
    if ($hasClient) {
        $code = Invoke-Patch $game $manifest.ClientPatch 'new client update'
        if ($code -eq 3) { throw 'Some new client files were changed since they were installed (see the list above), so the new client was not updated. The game itself is up to date.' }
        if ($code -eq 4) { throw "The new client in $game is not a released version this update knows, so it was not updated (the game itself is up to date). Choose 2 (New install) for a fresh game with both clients; IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd there brings your progress over." }
        if ($code -eq 5) { throw 'Close the Offline DAoC launchers, the server and the game, then run this file again.' }
        if ($code -ne 0 -and $code -ne 2) { throw 'The new client update failed and the new client was put back as it was. Run this file again to retry.' }
    }
    Write-Host ''
    $now = if ($hasClient) { "$($manifest.PatchVersion)c" } elseif (Test-Path -LiteralPath (Join-Path $game 'EDITION.txt')) { $manifest.PatchVersion } else { "$($manifest.PatchVersion)b" }
    Write-Host "Done: $game is Offline DAoC $now now. Nothing has been started automatically."
    Write-Host 'Open START OFFLINE DAOC.cmd (or START OFFLINE DAOC - NEW CLIENT.cmd) there: the launcher shows the new version.'
    exit 0
}

# 0.36c is the new client, added to a 0.36b or 0.36 game: new-client\ plus its start file, beside START OFFLINE DAOC.cmd.
# Both clients then share the game's server, account, characters and bots. A new install first downloads the complete
# 0.36b game (-Classic: the 0.36 game), as its own DOWNLOAD-AND-PLAY cmd would, then the client; -AddTo adds the client
# to an existing 0.36b or 0.36 folder.
# The client comes in independent, separately verified parts: each one is downloaded, checked, unpacked and deleted
# before the next, so the large download never needs twice its size on disk, and an interrupted install continues
# where it stopped when the helper runs again.
if ($manifest.Mode -eq 'newclient') {
    if ($ReleaseVersion -ne '0.36c' -or $manifest.BaseVersion -ne '0.36b' -or $manifest.ClassicBaseVersion -ne '0.36' -or
        $manifest.RootFolder -ne $rootFolder -or !$manifest.Parts -or !$manifest.RootFiles) { throw "Unexpected v$ReleaseVersion manifest." }
    $newClientPartPattern = '^' + [regex]::Escape($rootFolder) + '-part\d{2}\.zip$'

    function Test-GameFolder([string]$game) {
        # A 0.36b game (with the Sluaghbinder) or a 0.36 game (classic classes only, marked by EDITION.txt): both carry
        # the 0.36 launcher, and each has its own verified game client. The new client plays either.
        $dll = Join-Path $game 'runtime\OfflineDAoC.dll'
        if (!(Test-Path -LiteralPath (Join-Path $game 'runtime\OfflineDAoC.exe') -PathType Leaf) -or
            !(Test-Path -LiteralPath $dll -PathType Leaf)) {
            throw "No Offline DAoC game was found in $game. Choose the folder that holds START OFFLINE DAOC.cmd."
        }
        $text = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($dll))
        if ($text -notmatch 'VERSION 0\.(3[6-9]|[4-9][0-9])b') {
            throw "The game in $game is not Offline DAoC 0.36 or 0.36b. The new client needs a 0.36 or 0.36b game (same server version). Use DOWNLOAD-AND-PLAY-v0.36c.cmd for a complete new install."
        }
        $gameDll = Join-Path $game 'runtime\client-opendaoc\app\game.dll'
        $clientHash = if (Test-Path -LiteralPath $gameDll -PathType Leaf) { (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash } else { '' }
        $classicGame = Test-Path -LiteralPath (Join-Path $game 'EDITION.txt')
        $expected = if ($classicGame) { $manifest.ClassicClientSHA256 } else { $manifest.BaseClientSHA256 }
        if ($clientHash -ne $expected) {
            throw "The game client in $game is not the one Offline DAoC 0.36 / 0.36b installs. Use DOWNLOAD-AND-PLAY-v0.36c.cmd for a complete new install."
        }
        if ($classicGame) { Write-Host 'Found the Offline DAoC game with the classic classes only. The new client will offer the classic classes only.' }
        else { Write-Host 'Found the Offline DAoC game with the Sluaghbinder.' }
    }

    if ($AddTo) {
        if ($AddTo -eq 'pick') {
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = 'Choose your Offline DAoC 0.36b or 0.36 folder (the one with START OFFLINE DAOC.cmd in it).'
            $dialog.ShowNewFolderButton = $false
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No folder was chosen. Nothing was changed.' }
            $AddTo = $dialog.SelectedPath
        }
        $game = [IO.Path]::GetFullPath($AddTo)
        Test-GameFolder $game
    } else {
        $game = $target
    }

    # The client's files sit about 150 characters below the game folder; the engine reads them with normal Windows paths.
    if ($game.Length -gt 100) { throw "The folder path is too long for the new client ($($game.Length) characters, 100 at most). Move the game to a shorter path, for example C:\Games\OfflineDAoC, and run this again." }
    $client = Join-Path $game 'new-client'
    $installing = Join-Path $client 'INSTALLING.txt'
    $done = Join-Path $client '.install'
    $clientBytes = [int64](($manifest.Parts | Measure-Object -Property UnpackedBytes -Sum).Sum)

    if (!$AddTo) {
        # A new install: the 0.36b game first, marked done so a rerun knows the folder is a complete game.
        $baseDone = Join-Path $done 'base.done'
        if (!(Test-ExistsLong $game)) {
            $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($game))
            $need = $clientBytes + 25GB
            if ($drive.AvailableFreeSpace -lt $need) {
                throw ("Offline DAoC 0.36c needs about {0:N0} GB of free disk space on {1} while it installs ({2:N0} GB free). Free up space and run this again." -f ($need / 1GB), $drive.Name, ($drive.AvailableFreeSpace / 1GB))
            }
            $baseVersion = if ($Classic) { $manifest.ClassicBaseVersion } else { $manifest.BaseVersion }
            Write-Host "Step 1 of 2: the complete $baseVersion game."
            $global:LASTEXITCODE = 0   # a nested run that succeeds sets no exit code; don't read an older one
            & $PSCommandPath -ReleaseVersion $baseVersion -Destination $game -LocalAssets $LocalAssets
            if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "The $baseVersion game download failed. Run this file again to retry." }
            [IO.Directory]::CreateDirectory((Get-ExtendedPath $done)) | Out-Null
            Set-Content -LiteralPath $baseDone -Value $baseVersion -Encoding ASCII
        } elseif (!(Test-Path -LiteralPath $baseDone) -and !(Test-Path -LiteralPath (Join-Path $client 'VERSION.txt'))) {
            throw "The folder $game exists, but the game in it did not finish installing. Rename or delete that folder and run this file again (the downloaded parts are kept and not downloaded again)."
        } else {
            Write-Host "Continuing the install in $game ..."
        }
        Test-GameFolder $game
    }
    if ((Test-Path -LiteralPath (Join-Path $client 'VERSION.txt')) -and !(Test-Path -LiteralPath $installing)) {
        Write-Host "The new client is already installed in $game. Open START OFFLINE DAOC - NEW CLIENT.cmd there."
        exit 0
    }
    $remaining = @($manifest.Parts | Where-Object { !(Test-Path -LiteralPath (Join-Path $done ($_.Name + '.done'))) })
    $need = [int64]0
    foreach ($part in $remaining) { $need += [int64]$part.UnpackedBytes }
    $need += [int64](($manifest.Parts | Measure-Object -Property Bytes -Maximum).Maximum) + 1GB
    $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($game))
    if ($drive.AvailableFreeSpace -lt $need) {
        throw ("The new client needs about {0:N0} GB of free disk space on {1} ({2:N0} GB free). Free up space and run this again." -f ($need / 1GB), $drive.Name, ($drive.AvailableFreeSpace / 1GB))
    }
    [IO.Directory]::CreateDirectory((Get-ExtendedPath $done)) | Out-Null
    Set-Content -LiteralPath $installing -Value 'The new client is being installed. If this file is still here, run DOWNLOAD-AND-PLAY-v0.36c.cmd or ADD-NEW-CLIENT-TO-0.36-OR-0.36b.cmd (the one you used) again to finish.' -Encoding ASCII

    $allowedRoot = @($manifest.RootFiles)
    $gamePrefix = $game.TrimEnd('\') + '\'
    $index = 0
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($part in $manifest.Parts) {
        $index++
        if ($part.Name -notmatch $newClientPartPattern -or $part.SHA256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid part metadata.' }
        $marker = Join-Path $done ($part.Name + '.done')
        if (Test-Path -LiteralPath $marker) { continue }
        $path = Join-Path $cache $part.Name
        $valid = (Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -eq $part.Bytes
        if ($valid) { $valid = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $part.SHA256 }
        if (!$valid) {
            Write-Host ("Step 2 of 2: new client part {0} of {1} ({2:N1} GB)..." -f $index, $manifest.Parts.Count, ($part.Bytes / 1GB))
            Get-ReleaseAsset $part.Name $path ([int64]$part.Bytes)
            if ((Get-Item -LiteralPath $path).Length -ne $part.Bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $part.SHA256) {
                throw 'Download verification failed. Run this file again to retry; finished parts are kept.'
            }
        }
        Write-Host ("Unpacking part {0} of {1}..." -f $index, $manifest.Parts.Count)
        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            # Validate EVERY entry first: each stays inside the game folder, in new-client\ or as one of the start files.
            $prefix = $manifest.RootFolder + '/'
            foreach ($entry in $zip.Entries) {
                $name = $entry.FullName.Replace('\','/')
                if (!$name.StartsWith($prefix,[StringComparison]::Ordinal) -or $name.Contains(':') -or
                    $name.Contains('/../') -or $name.EndsWith('/..')) { throw 'Unexpected new client archive layout.' }
                $relative = $name.Substring($prefix.Length)
                if (!$relative) { continue }
                if (!$relative.StartsWith('new-client/',[StringComparison]::Ordinal) -and ($allowedRoot -notcontains $relative)) {
                    throw "Unexpected file in the new client archive: $relative"
                }
                $resolved = [IO.Path]::GetFullPath((Join-Path $game $relative))
                if (!$resolved.StartsWith($gamePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe new client archive entry.' }
            }
            foreach ($entry in $zip.Entries) {
                $relative = $entry.FullName.Replace('\','/').Substring($prefix.Length)
                if (!$relative) { continue }
                $resolved = Join-Path $game $relative
                if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory((Get-ExtendedPath $resolved)) | Out-Null; continue }
                [IO.Directory]::CreateDirectory((Get-ExtendedPath (Split-Path -Parent $resolved))) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Get-ExtendedPath $resolved),$true)
            }
        } finally { $zip.Dispose() }
        Set-Content -LiteralPath $marker -Value $part.SHA256 -Encoding ASCII
        Remove-Item -LiteralPath $path -Force
    }
    foreach ($required in @('new-client\godot\project.godot','new-client\runtime\OfflineDAoC.exe','new-client\runtime\server_root.txt',
            'new-client\tools\godot_net\Godot_v4.7.2-stable_mono_win64.exe','new-client\VERSION.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $game $required) -PathType Leaf)) { throw "The new client is missing $required. Run this file again." }
    }
    foreach ($file in $allowedRoot) {
        if (!(Test-Path -LiteralPath (Join-Path $game $file) -PathType Leaf)) { throw "The new client is missing $file. Run this file again." }
    }
    Remove-Item -LiteralPath $done -Recurse -Force
    Remove-Item -LiteralPath $installing -Force
    Write-Host ''
    Write-Host "Verified and installed the new client in $game"
    Write-Host 'Open START OFFLINE DAOC - NEW CLIENT.cmd there (START OFFLINE DAOC.cmd still starts the original client).'
    Write-Host 'Both clients share the same account, characters and bots. Nothing has been started automatically.'
    exit 0
}

# 0.33, 0.34, 0.35 and 0.36 "no custom class" are the same complete 0.33b / 0.34b / 0.35b / 0.36b game plus two
# verified swap-in files (world database with the Sluaghbinder switched off, and a game.dll without
# the Sluaghbinder class). One download serves both editions; the edition files ship inside the "b" package.
if ($manifest.Mode -eq 'edition') {
    if ($ReleaseVersion -notin @('0.33','0.34','0.35','0.36') -or $manifest.BaseVersion -ne ($ReleaseVersion + 'b') -or
        $manifest.EditionFolder -ne ($ReleaseVersion + '-no-custom-class') -or !$manifest.EditionFiles) {
        throw "Unexpected v$ReleaseVersion edition manifest."
    }
    Write-Host "Downloading the complete $ReleaseVersion game (shared with $($manifest.BaseVersion))..."
    & $PSCommandPath -ReleaseVersion $manifest.BaseVersion -Destination $target -LocalAssets $LocalAssets
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "The $ReleaseVersion game download failed." }
    $editionRoot = Join-Path $target (Join-Path 'editions' $manifest.EditionFolder)
    foreach ($file in $manifest.EditionFiles) {
        if ($file.Path -notmatch '^runtime\\[A-Za-z0-9_\\. -]+$' -or $file.Path.Contains('..') -or $file.SHA256 -notmatch '^[a-f0-9]{64}$') {
            throw 'Invalid edition file entry.'
        }
        $source = Join-Path $editionRoot $file.Path
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.SHA256) { throw "Edition file failed verification: $($file.Path)" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $target $file.Path) -Force
        if ((Get-FileHash -LiteralPath (Join-Path $target $file.Path) -Algorithm SHA256).Hash -ne $file.SHA256) { throw "Edition file copy failed: $($file.Path)" }
    }
    Set-Content -LiteralPath (Join-Path $target 'EDITION.txt') -Value "Offline DAoC $ReleaseVersion - no custom class (Classic + Shrouded Isles classes only)." -Encoding UTF8
    Write-Host "Verified the $ReleaseVersion edition without the custom class in $target"
    Write-Host 'Read READ ME FIRST.txt, then open START OFFLINE DAOC.cmd. Nothing has been started automatically.'
    exit 0
}

if (!$manifest.Parts -or $manifest.RootFolder -ne $rootFolder) { throw 'Unexpected full-release manifest.' }
$partPaths = @()
foreach ($part in $manifest.Parts) {
    if ($part.Name -notmatch $partPattern -or $part.SHA256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid part metadata.' }
    $path = Join-Path $cache $part.Name
    $valid = (Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -eq $part.Bytes
    if ($valid) { $valid = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $part.SHA256 }
    if (!$valid) {
        Write-Host "Downloading $($part.Name)..."
        Get-ReleaseAsset $part.Name $path ([int64]$part.Bytes)
        if ((Get-Item -LiteralPath $path).Length -ne $part.Bytes -or (Get-FileHash -LiteralPath $path).Hash -ne $part.SHA256) { throw 'Download verification failed. Run again to retry the incomplete part.' }
    }
    $partPaths += $path
}
$archive = Join-Path $cache "$rootFolder.zip"
$joined = [IO.File]::Create($archive)
try {
    foreach ($partPath in $partPaths) {
        $inputStream = [IO.File]::OpenRead($partPath)
        try { $inputStream.CopyTo($joined) } finally { $inputStream.Dispose() }
    }
} finally { $joined.Dispose() }
if ((Get-FileHash -LiteralPath $archive).Hash -ne $manifest.ArchiveSHA256) { throw 'Complete archive hash mismatch; nothing was installed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    # Validate EVERY path before extracting anything. Archive cannot escape this new destination.
    $prefix = $manifest.RootFolder + '/'
    $targetPrefix = $target.TrimEnd('\') + '\'
    foreach ($entry in $zip.Entries) {
        if (!$entry.FullName.StartsWith($prefix,[StringComparison]::Ordinal) -or $entry.FullName.Contains(':')) { throw 'Unexpected archive layout.' }
        $relative = $entry.FullName.Substring($prefix.Length)
        if (!$relative) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $target $relative))
        if (!$resolved.StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive entry.' }
    }
    [IO.Directory]::CreateDirectory((Get-ExtendedPath $target)) | Out-Null
    foreach ($entry in $zip.Entries) {
        $relative = $entry.FullName.Substring($prefix.Length)
        if (!$relative) { continue }
        $resolved = Join-Path $target $relative
        if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory((Get-ExtendedPath $resolved)) | Out-Null; continue }
        [IO.Directory]::CreateDirectory((Get-ExtendedPath (Split-Path -Parent $resolved))) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Get-ExtendedPath $resolved),$false)
    }
} finally { $zip.Dispose() }
# The joined archive was only needed for extracting; the verified parts stay in .downloads for a reinstall.
Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
Write-Host "Verified and extracted to $target"
Write-Host 'Read READ ME FIRST.txt, then open START OFFLINE DAOC.cmd. Nothing has been started automatically.'
Write-Host 'The verified download parts are kept in .downloads; delete that folder to free the space.'
