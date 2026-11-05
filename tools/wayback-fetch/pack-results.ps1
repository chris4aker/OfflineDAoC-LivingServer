# pack-results.ps1 - zips each job's results for moving off the PC: out\_packs\<job>-<yyyymmdd>.zip
#
# Runs on Windows PowerShell 5.1 with Compress-Archive (no installs). PACK-RESULTS.cmd starts it. Offline: no requests.
#
# A pack holds the job's saved pages, index.csv and manifest.jsonl, with the same folders as out\<job>\. It leaves
# out the CDX index state (out\<job>\index\), unfinished downloads (*.part) and everything in out\_logs, out\_state
# and out\_packs. A job is packed again only when its files changed since its last pack (out\_packs\<job>.packstate
# holds a fingerprint of names, sizes and times). Packing the same job twice on one day replaces that day's zip.
#
# Parameter names differ from wayback-fetch.ps1's on purpose (this script does not dot-source it, but keep it so).
param(
    [string]$Folder = '',
    # Empty: every job folder in out\. Otherwise only these job names.
    [string[]]$Only = @(),
    # Pack even when nothing changed.
    [switch]$Again
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Folder) { $Folder = $here }

# The files a pack holds, relative to the job folder, in a stable order.
function Get-PackFiles([string]$jobDir) {
    $root = (Resolve-Path -LiteralPath $jobDir).Path.TrimEnd('\', '/')
    $files = New-Object System.Collections.Generic.List[object]
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
        $rel = $f.FullName.Substring($root.Length + 1)
        $first = ($rel -split '[\\/]')[0]
        if ($first -eq 'index' -and $rel.Length -gt 5) { continue }   # CDX index state, not results
        if ($f.Name.EndsWith('.part')) { continue }
        $files.Add([pscustomobject]@{ Rel = $rel; Full = $f.FullName; Length = $f.Length; Ticks = $f.LastWriteTimeUtc.Ticks })
    }
    return ,@($files | Sort-Object { $_.Rel.Replace('\', '/') } -CaseSensitive)
}

function Get-PackFingerprint($files) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in $files) { [void]$sb.Append($f.Rel.Replace('\', '/')).Append('|').Append($f.Length).Append('|').Append($f.Ticks).Append("`n") }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $h = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($sb.ToString())) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($h) -replace '-', '').ToLowerInvariant()
}

function Invoke-Pack {
    $outRoot = Join-Path $Folder 'out'
    if (-not (Test-Path -LiteralPath $outRoot)) { Write-Host 'pack-results: no out\ folder yet; run the fetcher first.'; return 1 }
    $packDir = Join-Path $outRoot '_packs'
    if (-not (Test-Path -LiteralPath $packDir)) { New-Item -ItemType Directory -Path $packDir -Force | Out-Null }
    $day = (Get-Date).ToString('yyyyMMdd')
    $packed = 0; $same = 0; $empty = 0
    foreach ($d in @(Get-ChildItem -LiteralPath $outRoot -Directory | Sort-Object Name)) {
        if ($d.Name.StartsWith('_')) { continue }
        if ($Only.Count -gt 0 -and $Only -notcontains $d.Name) { continue }
        $files = Get-PackFiles $d.FullName
        if ($files.Count -eq 0) { $empty++; continue }
        $fp = Get-PackFingerprint $files
        $statePath = Join-Path $packDir ($d.Name + '.packstate')
        $last = ''
        if (Test-Path -LiteralPath $statePath) { $last = ([IO.File]::ReadAllText($statePath)).Trim().Split(' ')[0] }
        if (-not $Again -and $last -eq $fp) { Write-Host ("  {0}: unchanged since its last pack, skipped" -f $d.Name); $same++; continue }

        # Compress-Archive keeps folders only for whole folders, so copy the chosen files to a staging folder first.
        $stage = Join-Path ([IO.Path]::GetTempPath()) ('wbf-pack-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Path $stage | Out-Null
        try {
            foreach ($f in $files) {
                $to = Join-Path $stage $f.Rel
                $toDir = Split-Path -Parent $to
                if (-not (Test-Path -LiteralPath $toDir)) { New-Item -ItemType Directory -Path $toDir -Force | Out-Null }
                [IO.File]::Copy($f.Full, $to, $true)
            }
            $zip = Join-Path $packDir ($d.Name + '-' + $day + '.zip')
            $tmpZip = $zip + '.tmp.zip'
            if (Test-Path -LiteralPath $tmpZip) { Remove-Item -LiteralPath $tmpZip -Force }
            $items = @(Get-ChildItem -LiteralPath $stage | ForEach-Object { $_.FullName })
            Compress-Archive -LiteralPath $items -DestinationPath $tmpZip -CompressionLevel Optimal
            if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
            [IO.File]::Move($tmpZip, $zip)
        } finally {
            Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
        [IO.File]::WriteAllText($statePath, $fp + ' ' + (Split-Path -Leaf $zip), (New-Object System.Text.UTF8Encoding($false)))
        $mb = (Get-Item -LiteralPath $zip).Length / 1MB
        Write-Host ("  {0}: {1} files -> {2} ({3:0.0} MB)" -f $d.Name, $files.Count, $zip, $mb)
        $packed++
    }
    Write-Host ("pack-results: {0} packed, {1} unchanged, {2} empty. Packs are in {3}" -f $packed, $same, $empty, $packDir)
    return 0
}

$code = Invoke-Pack
exit $code
