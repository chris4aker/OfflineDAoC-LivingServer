# Tests tools/navmesh/Publish-NavmeshSet.ps1 in Windows PowerShell 5.1 on a fake install (issue #684). The navmesh
# workflow runs it on every change to tools/navmesh. Exits 1 on any failed check.
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/navmesh/tests/test-publish-script.ps1
# Checks: the script packs a small set into several parts (a small -PartLimit forces the split), the same files give
# byte-identical zips twice, navmesh_set.py accepts its manifest and parts (fetch --from) and refuses a planted
# mismatch, the install is left exactly as it was, and it stops on a reused version folder, a bad version, a damaged
# navmesh, a missing Detour.dll and an output folder inside the install.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$script = Join-Path $PSScriptRoot '..\Publish-NavmeshSet.ps1'
$tool = Join-Path $PSScriptRoot '..\navmesh_set.py'
$python = if ($env:PYTHON) { $env:PYTHON } else { 'python' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('navmesh-publish-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = 0

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok    $what" } else { Write-Host "  FAIL  $what" -ForegroundColor Red; $script:failures++ }
}

function Run-Publish([string[]]$argList) {
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @argList 2>&1 | Out-String
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $out }
}

function Snapshot([string]$folder) {
    return (Get-ChildItem -LiteralPath $folder -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($folder.Length) + '=' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '@' + $_.LastWriteTimeUtc.Ticks
    }) -join "`n"
}

function New-Nav([string]$path, [int]$seed, [int]$kb) {
    $rng = New-Object Random $seed
    $bytes = New-Object byte[] ($kb * 1024)
    $rng.NextBytes($bytes)
    [BitConverter]::GetBytes([int]0x4d534554).CopyTo($bytes, 0)
    [BitConverter]::GetBytes([int]1).CopyTo($bytes, 4)
    [IO.File]::WriteAllBytes($path, $bytes)
}

try {
    $game = Join-Path $work 'game'
    $nav = Join-Path $game 'runtime\server\navmesh'
    New-Item -ItemType Directory -Path $nav, (Join-Path $game 'runtime\server\lib'), (Join-Path $game 'LivingServer-1.2.3') -Force | Out-Null
    # Random bytes don't compress: 6 zones of 300 KB with a 1 MB + 700 KB part limit must make several parts.
    foreach ($z in 1, 21, 120, 181, 209, 216) { New-Nav (Join-Path $nav ('zone{0:000}.nav' -f $z)) $z 300 }
    foreach ($j in 'roads', 'seams', 'pockets') { [IO.File]::WriteAllText((Join-Path $nav "$j.json"), "{`"$j`": []}") }
    [IO.File]::WriteAllText((Join-Path $nav 'notes.txt'), 'left out')
    [IO.File]::WriteAllBytes((Join-Path $game 'runtime\server\lib\Detour.dll'), [byte[]](77, 90, 1, 2, 3))
    [IO.File]::WriteAllText((Join-Path $game 'LivingServer-1.2.3\BUILD-INFO.txt'), "BuildName=1.2.3`r`nCommit=820944dc428a55271bfb1ec9c919053697002ffb`r`nBaseRelease=0.37`r`n")
    $before = Snapshot $game
    $limit = [string](1MB + 700KB)

    Write-Host '== Publish-NavmeshSet.ps1: a fake install'
    $a = Run-Publish @('-NavmeshFolder', $nav, '-OutputRoot', (Join-Path $work 'out-a'), '-NoBrowser', '-PartLimit', $limit)
    Check ($a.Code -eq 0) "packs the set (exit $($a.Code))`n$($a.Output)"
    $outA = Join-Path $work 'out-a\v1'
    $parts = @(Get-ChildItem -LiteralPath $outA -Filter 'navmesh-v1.part*.zip' | Sort-Object Name)
    Check ($parts.Count -ge 2) "splits into several parts under the limit ($($parts.Count))"
    Check (@($parts | Where-Object { $_.Length -ge [long]$limit }).Count -eq 0) 'every part is under the limit'
    Check ($a.Output -match 'releases/new|Publish release') 'prints the upload steps'
    Check ($a.Output -match 'notes\.txt') 'names the file it leaves out'
    $manifest = Get-Content -LiteralPath (Join-Path $outA 'navmesh-manifest.json') -Raw | ConvertFrom-Json
    Check ($manifest.source.build -eq '1.2.3' -and $manifest.source.commit -like '820944dc*') 'reads the build and commit from BUILD-INFO.txt'
    Check (@($manifest.files).Count -eq 10) "lists 6 zones, 3 json files and Detour.dll ($(@($manifest.files).Count))"
    Check ((Snapshot $game) -eq $before) 'leaves the install exactly as it was'

    $b = Run-Publish @('-NavmeshFolder', $nav, '-OutputRoot', (Join-Path $work 'out-b'), '-NoBrowser', '-PartLimit', $limit)
    $same = $b.Code -eq 0
    foreach ($p in $parts) {
        $other = Join-Path $work "out-b\v1\$($p.Name)"
        $same = $same -and (Test-Path -LiteralPath $other) -and ((Get-FileHash $p.FullName).Hash -eq (Get-FileHash $other).Hash)
    }
    Check $same 'the same files give byte-identical zips'

    Write-Host '== navmesh_set.py reads what the script wrote'
    $dest = Join-Path $work 'set'
    $log = & $python $tool fetch (Join-Path $outA 'navmesh-manifest.json') --dest $dest --from $outA 2>&1 | Out-String
    Check ($LASTEXITCODE -eq 0) "fetch --from accepts the manifest and parts`n$log"
    Check ((Test-Path (Join-Path $dest 'lib\Detour.dll')) -and (Test-Path (Join-Path $dest 'navmesh\zone181.nav'))) 'unpacks lib\ and navmesh\'
    $planted = Join-Path $work 'planted.json'
    $text = Get-Content -LiteralPath (Join-Path $outA 'navmesh-manifest.json') -Raw
    $first = @($manifest.files)[0].sha256
    [IO.File]::WriteAllText($planted, $text.Replace($first, ('0' * 64)))
    $log = & $python $tool fetch $planted --dest (Join-Path $work 'set2') --from $outA 2>&1 | Out-String
    Check ($LASTEXITCODE -eq 1 -and $log -match '::error') "a planted hash mismatch fails`n$log"

    Write-Host '== it stops, and writes nothing, when it should'
    $r = Run-Publish @('-NavmeshFolder', $nav, '-OutputRoot', (Join-Path $work 'out-a'), '-NoBrowser')
    Check ($r.Code -eq 1 -and $r.Output -match 'never replaced') 'a version folder that holds files'
    $r = Run-Publish @('-NavmeshFolder', $nav, '-OutputRoot', (Join-Path $work 'out-c'), '-NoBrowser', '-Version', '1')
    Check ($r.Code -eq 1 -and !(Test-Path (Join-Path $work 'out-c'))) 'a bad -Version'
    $r = Run-Publish @('-NavmeshFolder', $nav, '-OutputRoot', (Join-Path $game 'upload'), '-NoBrowser')
    Check ($r.Code -eq 1 -and !(Test-Path (Join-Path $game 'upload'))) 'an output folder inside the install'
    $r = Run-Publish @('-NavmeshFolder', (Join-Path $work 'nowhere'), '-OutputRoot', (Join-Path $work 'out-d'), '-NoBrowser')
    Check ($r.Code -eq 1 -and !(Test-Path (Join-Path $work 'out-d'))) 'a folder that does not exist'
    $bad = Join-Path $work 'bad\runtime\server\navmesh'
    New-Item -ItemType Directory -Path $bad, (Join-Path $work 'bad\runtime\server\lib') -Force | Out-Null
    Copy-Item (Join-Path $nav '*') $bad
    [IO.File]::WriteAllText((Join-Path $bad 'zone002.nav'), 'not a navmesh')
    [IO.File]::WriteAllBytes((Join-Path $work 'bad\runtime\server\lib\Detour.dll'), [byte[]](77, 90))
    $r = Run-Publish @('-NavmeshFolder', $bad, '-OutputRoot', (Join-Path $work 'out-e'), '-NoBrowser')
    Check ($r.Code -eq 1 -and $r.Output -match 'zone002\.nav' -and !(Test-Path (Join-Path $work 'out-e'))) 'a damaged navmesh'
    Remove-Item (Join-Path $bad 'zone002.nav'); Remove-Item (Join-Path $work 'bad\runtime\server\lib\Detour.dll')
    $r = Run-Publish @('-NavmeshFolder', $bad, '-OutputRoot', (Join-Path $work 'out-f'), '-NoBrowser')
    Check ($r.Code -eq 1 -and $r.Output -match 'Detour' -and !(Test-Path (Join-Path $work 'out-f'))) 'a missing Detour.dll'
    Check ((Snapshot $game) -eq $before) 'the install is still exactly as it was'
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures) { Write-Host "$failures check(s) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All Publish-NavmeshSet.ps1 checks passed.' -ForegroundColor Green
exit 0
