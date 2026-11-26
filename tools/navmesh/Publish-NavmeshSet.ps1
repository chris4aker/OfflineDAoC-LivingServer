<#
.SYNOPSIS
    Packs an install's navmeshes into the files of a navmesh-vN release, for CI's navmesh tests (issue #684).

.DESCRIPTION
    Run it once on the PC that has the install (double-click RUN-ME.cmd beside this file). Windows PowerShell 5.1, nothing
    to install. It:
      1. checks the navmesh folder (zoneNNN.nav files with a Detour header, roads.json, seams.json, pockets.json) and the
         Detour library beside it (..\lib\Detour.dll);
      2. hashes every file (SHA-256);
      3. packs them into as few zip parts as it can, each under 1.9 GB (GitHub takes files under 2 GB), in a fixed
         order with fixed dates, so the same files always give the same zips on this PC;
      4. opens each part again and checks every file in it against its hash;
      5. writes navmesh-manifest.json (every file's name, size, SHA-256 and part; each part's size and SHA-256; the
         install's build and commit; the date);
      6. opens GitHub's "new release" page for navmesh-vN and prints the steps to upload the files.

    It only reads the install: nothing in the game folder is written, moved or locked. It writes only into
    <OutputRoot>\<Version> (C:\Games\OfflineDAoC\navmesh-upload\v1 by default). It sends nothing anywhere: the upload
    is done by you, in the browser, signed in to GitHub as yourself. No token, no password, no git, no gh.

.PARAMETER NavmeshFolder
    The install's runtime\server\navmesh folder.

.PARAMETER Version
    v1 for the first set. A later rebuild of the meshes is v2, v3, ...: a published version is never replaced.

.PARAMETER OutputRoot
    Where the version folder is written. Leave the default; tests point it at a temp folder.

.PARAMETER NoBrowser
    Don't open the browser (tests).
#>
[CmdletBinding()]
param(
    [string]$NavmeshFolder = 'C:\Games\OfflineDAoC\LivingServer1.2.3-test\runtime\server\navmesh',
    [string]$Version = 'v1',
    [string]$OutputRoot = 'C:\Games\OfflineDAoC\navmesh-upload',
    [switch]$NoBrowser,
    [long]$PartLimit = 1900000000
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$Repository = 'chris4aker/OfflineDAoC-LivingServer'
$FixedDate = New-Object DateTimeOffset 2000, 1, 1, 0, 0, 0, ([TimeSpan]::Zero)
$Utf8 = New-Object Text.UTF8Encoding $false

function Stop-WithMessage([string]$text) {
    Write-Host ''
    Write-Host "STOPPED: $text" -ForegroundColor Red
    Write-Host 'Nothing was uploaded. Fix the above and run it again.'
    exit 1
}

function Get-FullPath([string]$path) { return [IO.Path]::GetFullPath($path).TrimEnd('\') }

function Test-Inside([string]$child, [string]$parent) {
    $c = (Get-FullPath $child) + '\'
    $p = (Get-FullPath $parent) + '\'
    return $c.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)
}

function Get-Sha256([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StreamSha256([IO.Stream]$stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (-join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })) } finally { $sha.Dispose() }
}

function ConvertTo-JsonString([string]$s) {
    $b = New-Object Text.StringBuilder
    [void]$b.Append('"')
    foreach ($ch in $s.ToCharArray()) {
        switch ($ch) {
            '"' { [void]$b.Append('\"') }
            '\' { [void]$b.Append('\\') }
            default {
                if ([int]$ch -lt 32) { [void]$b.Append(('\u{0:x4}' -f [int]$ch)) } else { [void]$b.Append($ch) }
            }
        }
    }
    [void]$b.Append('"')
    return $b.ToString()
}

function Read-KeyValues([string]$path) {
    $map = @{}
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -match '^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') { $map[$Matches[1]] = $Matches[2] }
    }
    return $map
}

# ---- 1. the folder ----------------------------------------------------------------------------------------------

Write-Host 'DAoC - Living Server: navmesh set for CI (issue #684)' -ForegroundColor Cyan
if ($Version -notmatch '^v[1-9][0-9]{0,3}$') { Stop-WithMessage "-Version must be v1, v2, ... (got '$Version')." }
$tag = "navmesh-$Version"
if (!(Test-Path -LiteralPath $NavmeshFolder -PathType Container)) {
    Stop-WithMessage "No folder at $NavmeshFolder. Give the install's runtime\server\navmesh folder: -NavmeshFolder '<game>\runtime\server\navmesh'"
}
$nav = Get-FullPath $NavmeshFolder
$serverFolder = Split-Path -Parent $nav
$game = Split-Path -Parent (Split-Path -Parent $serverFolder)
$detour = Join-Path $serverFolder 'lib\Detour.dll'
$outRoot = Get-FullPath $OutputRoot
$out = Join-Path $outRoot $Version
if ((Test-Inside $out $game) -or (Test-Inside $out $nav)) { Stop-WithMessage "The output folder $out is inside the game install. Choose another -OutputRoot." }
Write-Host "Navmesh folder : $nav"
Write-Host "Output folder  : $out"

$plan = New-Object 'System.Collections.Generic.List[object]'
$zones = @(Get-ChildItem -LiteralPath $nav -File -Filter 'zone*.nav' | Where-Object { $_.Name -match '^zone[0-9]{3}\.nav$' })
if ($zones.Count -eq 0) { Stop-WithMessage "No zoneNNN.nav files in $nav." }
$bad = @()
foreach ($z in $zones) {
    $fs = [IO.File]::Open($z.FullName, 'Open', 'Read', 'ReadWrite')
    try {
        $head = New-Object byte[] 8
        $read = $fs.Read($head, 0, 8)
    } finally { $fs.Dispose() }
    # A Detour MSET file: magic 'MSET' (0x4d534554, little-endian), version 1 (NavMeshFileCheck.cs).
    if ($read -lt 8 -or [BitConverter]::ToInt32($head, 0) -ne 0x4d534554 -or [BitConverter]::ToInt32($head, 4) -ne 1) { $bad += $z.Name }
    $plan.Add([pscustomobject]@{ Path = "navmesh/$($z.Name)"; Full = $z.FullName; Size = $z.Length })
}
if ($bad.Count -gt 0) { Stop-WithMessage "These aren't whole Detour navmeshes: $($bad -join ', ')." }
foreach ($name in 'roads.json', 'seams.json', 'pockets.json') {
    $p = Join-Path $nav $name
    if (!(Test-Path -LiteralPath $p -PathType Leaf)) { Stop-WithMessage "$name is missing from $nav." }
    $plan.Add([pscustomobject]@{ Path = "navmesh/$name"; Full = $p; Size = (Get-Item -LiteralPath $p).Length })
}
if (!(Test-Path -LiteralPath $detour -PathType Leaf)) { Stop-WithMessage "The Detour library is missing: $detour." }
$plan.Add([pscustomobject]@{ Path = 'lib/Detour.dll'; Full = $detour; Size = (Get-Item -LiteralPath $detour).Length })
$taken = @{}; foreach ($p in $plan) { $taken[$p.Full.ToLowerInvariant()] = $true }
$skipped = @(Get-ChildItem -LiteralPath $nav -Recurse -File | Where-Object { !$taken.ContainsKey($_.FullName.ToLowerInvariant()) })
if ($skipped.Count -gt 0) {
    Write-Host "Left out ($($skipped.Count) files that aren't zoneNNN.nav or the three .json files):" -ForegroundColor Yellow
    $skipped | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.FullName.Substring($nav.Length + 1))" }
}
# Ordinal order (not the culture's): the same order on every PC, so the zips come out the same.
$ordered = New-Object 'System.Collections.Generic.List[object]'
$names = New-Object 'System.Collections.Generic.List[string]'
foreach ($p in $plan) { $names.Add($p.Path) }
$names.Sort([StringComparer]::Ordinal)
foreach ($n in $names) { $ordered.Add(($plan | Where-Object { $_.Path -ceq $n } | Select-Object -First 1)) }
$total = [long]0; foreach ($p in $ordered) { $total += $p.Size }
Write-Host ("Found {0} zone navmeshes, 3 road/seam/pocket files and Detour.dll: {1:N0} bytes." -f $zones.Count, $total)

# The build the install runs, for the manifest: BUILD-INFO.txt if one is there, else the newest install record.
$build = @{ BuildName = 'unknown'; Commit = 'unknown'; BaseRelease = 'unknown' }
$infoFiles = @((Join-Path $game 'BUILD-INFO.txt'), (Join-Path $serverFolder 'BUILD-INFO.txt'))
$infoFiles += @(Get-ChildItem -LiteralPath $game -Directory -Filter 'LivingServer-*' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending | ForEach-Object { Join-Path $_.FullName 'BUILD-INFO.txt' })
$found = $infoFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($found) {
    $kv = Read-KeyValues $found
    foreach ($k in 'BuildName', 'Commit', 'BaseRelease') { if ($kv.ContainsKey($k)) { $build[$k] = $kv[$k] } }
    Write-Host "Build          : $($build.BuildName), commit $($build.Commit) (from $found)"
} else {
    $rec = Get-ChildItem -LiteralPath (Join-Path $serverFolder 'deployment-backups') -Directory -Filter 'fork-build-*' -ErrorAction SilentlyContinue |
        Sort-Object Name | Select-Object -Last 1
    $recFile = if ($rec) { Join-Path $rec.FullName 'install-record.json' } else { $null }
    if ($recFile -and (Test-Path -LiteralPath $recFile)) {
        $r = Get-Content -LiteralPath $recFile -Raw | ConvertFrom-Json
        foreach ($k in 'BuildName', 'Commit', 'BaseRelease') { if ($r.PSObject.Properties[$k] -and $r.$k) { $build[$k] = [string]$r.$k } }
        Write-Host "Build          : $($build.BuildName), commit $($build.Commit) (from $recFile)"
    } else {
        Write-Host 'Build          : unknown (no BUILD-INFO.txt or install record found; the manifest says unknown)' -ForegroundColor Yellow
    }
}

# ---- the output folder ------------------------------------------------------------------------------------------

if (Test-Path -LiteralPath $out) {
    if (@(Get-ChildItem -LiteralPath $out -Force).Count -gt 0) {
        Stop-WithMessage "$out already holds files. A published version is never replaced: delete that folder if it was never uploaded, or run with -Version v2 (or the next number)."
    }
} else {
    New-Item -ItemType Directory -Path $out -Force | Out-Null
}
$drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($out))
if ($drive.AvailableFreeSpace -lt ($total + 200MB)) {
    Stop-WithMessage ("{0} has {1:N1} GB free; the zips need up to {2:N1} GB." -f $drive.Name, ($drive.AvailableFreeSpace / 1GB), (($total + 200MB) / 1GB))
}

# ---- 2. hashes --------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host 'Hashing every file (SHA-256)...'
$i = 0
foreach ($p in $ordered) {
    $i++
    Write-Progress -Activity 'Hashing' -Status $p.Path -PercentComplete ([int](100 * $i / $ordered.Count))
    $p | Add-Member -NotePropertyName Sha -NotePropertyValue (Get-Sha256 $p.Full)
}
Write-Progress -Activity 'Hashing' -Completed

# ---- 3. zip parts -----------------------------------------------------------------------------------------------

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$parts = New-Object 'System.Collections.Generic.List[object]'
$archive = $null; $stream = $null; $partName = $null; $count = 0
function Close-Part {
    if ($script:archive) {
        $script:archive.Dispose(); $script:stream.Dispose()
        $script:archive = $null; $script:stream = $null
    }
}
Write-Host 'Packing the zip parts (a few minutes)...'
try {
    $i = 0
    foreach ($p in $ordered) {
        $i++
        # A new part when this file, even if it didn't compress at all, could take the part past the limit.
        $room = 1MB + 512 * ($count + 1)
        if ($archive -and ($stream.Position + $p.Size + $room) -ge $PartLimit) { Close-Part }
        if (!$archive) {
            if ($p.Size + 1MB -ge $PartLimit) { Stop-WithMessage "$($p.Path) alone is bigger than a part may be." }
            $partName = '{0}.part{1:00}.zip' -f $tag, ($parts.Count + 1)
            $stream = New-Object IO.FileStream ((Join-Path $out $partName), 'CreateNew', 'Write', 'None')
            $archive = New-Object IO.Compression.ZipArchive ($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
            $parts.Add([pscustomobject]@{ Name = $partName; Files = 0 })
            $count = 0
        }
        Write-Progress -Activity "Packing $partName" -Status $p.Path -PercentComplete ([int](100 * $i / $ordered.Count))
        $entry = $archive.CreateEntry($p.Path, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $FixedDate
        $src = [IO.File]::Open($p.Full, 'Open', 'Read', 'ReadWrite')
        $dst = $entry.Open()
        try { $src.CopyTo($dst, 1MB) } finally { $dst.Dispose(); $src.Dispose() }
        $p | Add-Member -NotePropertyName Part -NotePropertyValue $partName
        $parts[$parts.Count - 1].Files++
        $count++
    }
} finally { Close-Part }
Write-Progress -Activity 'Packing' -Completed

# ---- 4. check the parts -----------------------------------------------------------------------------------------

Write-Host 'Checking every file in the zips against its hash...'
foreach ($part in $parts) {
    $path = Join-Path $out $part.Name
    $size = (Get-Item -LiteralPath $path).Length
    if ($size -ge $PartLimit) { Stop-WithMessage "$($part.Name) came out at $size bytes, over the limit." }
    $part | Add-Member -NotePropertyName Size -NotePropertyValue $size
    $part | Add-Member -NotePropertyName Sha -NotePropertyValue (Get-Sha256 $path)
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $mine = @($ordered | Where-Object { $_.Part -eq $part.Name })
        if ($zip.Entries.Count -ne $mine.Count) { Stop-WithMessage "$($part.Name) holds $($zip.Entries.Count) files, expected $($mine.Count)." }
        foreach ($p in $mine) {
            $e = $zip.GetEntry($p.Path)
            if (!$e -or $e.Length -ne $p.Size) { Stop-WithMessage "$($p.Path) is missing or the wrong size in $($part.Name)." }
            $s = $e.Open()
            try { $got = Get-StreamSha256 $s } finally { $s.Dispose() }
            if ($got -ne $p.Sha) { Stop-WithMessage "$($p.Path) in $($part.Name) doesn't match its hash (the file changed while it was packed?)." }
        }
    } finally { $zip.Dispose() }
    Write-Host ("  {0}: {1:N0} bytes, {2} files, SHA-256 {3}" -f $part.Name, $part.Size, $part.Files, $part.Sha)
}

# ---- 5. the manifest --------------------------------------------------------------------------------------------

$nl = "`n"
$j = New-Object Text.StringBuilder
[void]$j.Append('{' + $nl)
[void]$j.Append('  "schema": 1,' + $nl)
[void]$j.Append('  "version": ' + (ConvertTo-JsonString $Version) + ',' + $nl)
[void]$j.Append('  "tag": ' + (ConvertTo-JsonString $tag) + ',' + $nl)
[void]$j.Append('  "repository": ' + (ConvertTo-JsonString $Repository) + ',' + $nl)
[void]$j.Append('  "captured": ' + (ConvertTo-JsonString ((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'))) + ',' + $nl)
[void]$j.Append('  "source": {"build": ' + (ConvertTo-JsonString $build.BuildName) + ', "commit": ' + (ConvertTo-JsonString $build.Commit) +
    ', "base_release": ' + (ConvertTo-JsonString $build.BaseRelease) + '},' + $nl)
[void]$j.Append('  "total_bytes": ' + $total + ',' + $nl)
[void]$j.Append('  "parts": [' + $nl)
for ($k = 0; $k -lt $parts.Count; $k++) {
    $pt = $parts[$k]
    [void]$j.Append('    {"name": ' + (ConvertTo-JsonString $pt.Name) + ', "bytes": ' + $pt.Size + ', "sha256": "' + $pt.Sha + '", "files": ' + $pt.Files + '}')
    [void]$j.Append($(if ($k -lt $parts.Count - 1) { ',' + $nl } else { $nl }))
}
[void]$j.Append('  ],' + $nl)
[void]$j.Append('  "files": [' + $nl)
for ($k = 0; $k -lt $ordered.Count; $k++) {
    $p = $ordered[$k]
    [void]$j.Append('    {"path": ' + (ConvertTo-JsonString $p.Path) + ', "bytes": ' + $p.Size + ', "sha256": "' + $p.Sha + '", "part": ' + (ConvertTo-JsonString $p.Part) + '}')
    [void]$j.Append($(if ($k -lt $ordered.Count - 1) { ',' + $nl } else { $nl }))
}
[void]$j.Append('  ]' + $nl + '}' + $nl)
$manifestPath = Join-Path $out 'navmesh-manifest.json'
[IO.File]::WriteAllText($manifestPath, $j.ToString(), $Utf8)

# ---- 6. the upload ----------------------------------------------------------------------------------------------

$title = [Uri]::EscapeDataString("Navmesh set $Version (CI only, not a game release)")
$body = [Uri]::EscapeDataString("Navmeshes for CI's navmesh tests (issue #684), from build $($build.BuildName), commit $($build.Commit). " +
    'Not a game update: the launcher and Setup never offer it. Never replace these files; a rebuild is a new navmesh-vN.')
$url = "https://github.com/$Repository/releases/new?tag=$tag&target=main&title=$title&body=$body&prerelease=1"
$files = @($parts | ForEach-Object { $_.Name }) + 'navmesh-manifest.json'

Write-Host ''
Write-Host "Done. The files are in $out" -ForegroundColor Green
Write-Host ''
Write-Host 'Now upload them (about 10-30 minutes, depending on your connection):' -ForegroundColor Cyan
Write-Host '  1. Your browser opens GitHub''s "new release" page. Sign in if it asks. The tag, title and description are'
Write-Host "     filled in already (tag $tag)."
Write-Host "  2. Drag these $($files.Count) files from the folder that opens onto ""Attach binaries by dropping them here"":"
foreach ($f in $files) { Write-Host "       $f" }
Write-Host '     Wait until every file shows its size (no progress bar left).'
Write-Host '  3. Make sure "Set as a pre-release" is ticked, and "Set as the latest release" is NOT ticked.'
Write-Host '  4. Click "Publish release".'
Write-Host '  5. Tell Claude it is published. That is all: the manifest is read from the release, you send nothing else.'
Write-Host ''
Write-Host "If the page doesn't open, go to https://github.com/$Repository/releases/new, type $tag in ""Choose a tag"","
Write-Host 'pick "Create new tag on publish", and follow steps 2-5.'
if (!$NoBrowser) {
    Start-Process explorer.exe -ArgumentList "`"$out`""
    Start-Process $url
}
exit 0
