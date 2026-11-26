# The frozen update contract, end to end on Windows (issue #685, docs/RELEASE-TRAIN.md; CI: update-contract.yml).
#
#   powershell -File tools\release\test-update-contract.ps1 -Released <dir> -Candidate <dir> -Harness <dll> [-Work D:\uc]
#
# -Released: what `update_contract.py released` wrote (the newest full release older than the candidate: its package
# and Setup, checked against GitHub's digests). -Candidate: what `update_contract.py candidate` wrote (this commit's
# package zipped under every release name, and the release JSON GitHub would answer). -Harness: the built
# DAoCLivingServer.UpdateContract.dll.
#
# 1. A fake 0.37 game (no game download) gets the released package installed by its own install script: a released
#    install, with the released launcher's real files in runtime\.
# 2. The released launcher's own update code (the installed dll, loaded by the harness) checks a fake GitHub that
#    serves the candidate release, downloads and checks the package, and hands over to its own apply-update.ps1,
#    which runs the candidate's INSTALL.cmd.
# 3. The game is then the candidate: server, server libraries and launcher files are the candidate's BUILD-INFO.txt
#    hashes, OfflineDAoC.exe (what the game's start file and old shortcuts open) is there, the world is untouched.
# 4. The released Setup, asked which package it would install (--info) with the candidate as GitHub's newest release
#    (--release-json), chooses the candidate: its own feed parse, asset name, digest, address and package checks.
#    The candidate's zip is put where that Setup keeps its download, so it reuses it after checking the digest.
param(
    [Parameter(Mandatory = $true)][string]$Released,
    [Parameter(Mandatory = $true)][string]$Candidate,
    [Parameter(Mandatory = $true)][string]$Harness,
    [string]$Work = 'D:\uc'
)
$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  PASS  $what" -ForegroundColor Green; return }
    Write-Host "  FAIL  $what" -ForegroundColor Red
    $failures.Add($what)
    if ($env:GITHUB_ACTIONS) { Write-Host ("::error title=Update contract::" + (($what -replace "`r", '') -replace "`n", ' | ')) }
}
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-Info([string]$path) {
    $info = @{}
    foreach ($line in Get-Content -LiteralPath $path) { if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $info[$Matches[1]] = $Matches[2] } }
    return $info
}

$rel = Get-Content -LiteralPath (Join-Path $Released 'released.json') -Raw | ConvertFrom-Json
$feed = Get-Content -LiteralPath (Join-Path $Candidate 'feed.json') -Raw | ConvertFrom-Json
Write-Host "Released $($rel.tag) -> candidate $($feed.tag) (build $($feed.build)) in $($feed.repository)" -ForegroundColor Cyan
if (Test-Path $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
$game = Join-Path $Work 'game'
$logs = Join-Path $Work 'logs'
New-Item -ItemType Directory -Force -Path $game, $logs | Out-Null
foreach ($f in @((Join-Path $Candidate 'feed.json'), (Join-Path $Candidate 'latest.json'), (Join-Path $Released 'released.json'))) {
    Copy-Item -LiteralPath $f -Destination $logs -ErrorAction SilentlyContinue
}

# 1. A fake 0.37 game (tools\fork-build\test-install-scripts.ps1's layout: server and its libraries, launcher, release
#    notes in the manifest, a world), then the released package installed with its own script.
$core = @{
    'runtime/server' = @('CoreBase.dll', 'CoreBase.pdb', 'CoreDatabase.dll', 'CoreDatabase.pdb', 'CoreServer.dll', 'CoreServer.pdb')
    'runtime/server/lib' = @('CoreBase.dll', 'CoreBase.pdb', 'CoreDatabase.dll', 'CoreDatabase.pdb')
    'runtime/server/win-x64' = @('CoreBase.dll', 'CoreDatabase.dll', 'CoreServer.dll')
}
$lines = @()
foreach ($folder in $core.Keys) {
    $dir = Join-Path $game $folder
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    foreach ($name in @('GameServer.dll', 'GameServer.pdb') + $core[$folder]) {
        [IO.File]::WriteAllText((Join-Path $dir $name), "original release $folder/$name")
        $lines += "$(Sha (Join-Path $dir $name))  $folder/$name"
    }
}
foreach ($name in @('OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json', 'OfflineDAoC.runtimeconfig.json')) {
    [IO.File]::WriteAllText((Join-Path $game "runtime\$name"), "original launcher $name")
    $lines += "$(Sha (Join-Path $game "runtime\$name"))  runtime/$name"
}
foreach ($version in @('0.35', '0.36', '0.37')) {
    $notes = Join-Path $game "docs\RELEASE-$version.md"
    New-Item -ItemType Directory -Path (Split-Path -Parent $notes) -Force | Out-Null
    [IO.File]::WriteAllText($notes, "release $version")
    $lines += "$(Sha $notes)  docs/RELEASE-$version.md"
}
New-Item -ItemType Directory -Path (Join-Path $game 'runtime\data') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $game 'runtime\data\opendaoc.sqlite3.db'), 'the player''s world and saves')
$world = Sha (Join-Path $game 'runtime\data\opendaoc.sqlite3.db')
Set-Content -LiteralPath (Join-Path $game 'PACKAGE MANIFEST.sha256') -Value $lines -Encoding UTF8

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($rel.package, $game)
$relFolder = Join-Path $game "LivingServer-$($rel.build)"
$relInfo = Read-Info (Join-Path $relFolder 'BUILD-INFO.txt')
$out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $relFolder 'install-server-build.ps1') -GameFolder $game 2>&1 | Out-String
Check ($LASTEXITCODE -eq 0 -and (Sha (Join-Path $game 'runtime\server\GameServer.dll')) -eq $relInfo['GameServer.dll']) "the $($rel.tag) package installs on the fake game`n$out"
$relDll = @('DAoCLivingServer.dll', 'OfflineDAoC.dll' | Where-Object { $relInfo["launcher/$_"] })[0]
$installedDll = Join-Path $game "runtime\$relDll"
Check ($relDll -and (Sha $installedDll) -eq $relInfo["launcher/$relDll"]) "the installed launcher is $($rel.tag)'s ($relDll)"
if ($failures.Count -gt 0) { exit 1 }

# 2. The released launcher's update code. A copy of its installed files is loaded, so the install can replace them.
$copy = Join-Path $Work 'released-launcher'
New-Item -ItemType Directory -Force -Path $copy | Out-Null
Copy-Item (Join-Path $game 'runtime\*') $copy -Include '*.dll', '*.json'
Check ((Sha (Join-Path $copy $relDll)) -eq $relInfo["launcher/$relDll"]) "the harness loads $($rel.tag)'s launcher, byte for byte"
Write-Host ''
Write-Host "== $($rel.tag)'s launcher: check, GET UPDATE, hand-over" -ForegroundColor Cyan
& dotnet $Harness --launcher (Join-Path $copy $relDll) --game $game --feed (Join-Path $Candidate 'feed.json') --apply
$code = $LASTEXITCODE
Check ($code -eq 0) "$($rel.tag)'s launcher takes $($feed.tag) and installs it (harness exit $code)"
Copy-Item -LiteralPath (Join-Path $game 'runtime\logs\launcher-updates.log') -Destination $logs -ErrorAction SilentlyContinue

# 3. The result is the candidate.
$candInfo = Read-Info (Join-Path $game "LivingServer-$($feed.build)\BUILD-INFO.txt")
if ($code -eq 0) {
    foreach ($folder in @('runtime\server', 'runtime\server\lib', 'runtime\server\win-x64')) {
        Check ((Sha (Join-Path $game "$folder\GameServer.dll")) -eq $candInfo['GameServer.dll']) "$folder\GameServer.dll is the candidate's"
    }
    foreach ($key in @($candInfo.Keys | Where-Object { $_ -like 'launcher/*' })) {
        $file = Join-Path $game ('runtime\' + $key.Substring('launcher/'.Length))
        Check ((Test-Path -LiteralPath $file) -and (Sha $file) -eq $candInfo[$key]) "runtime\$($key.Substring(9)) is the candidate's"
    }
    Check (Test-Path -LiteralPath (Join-Path $game 'runtime\OfflineDAoC.exe')) 'runtime\OfflineDAoC.exe is there (the game''s start file and old shortcuts open it)'
    Check ((Sha (Join-Path $game 'runtime\data\opendaoc.sqlite3.db')) -eq $world) 'the world database was not touched'
}

# 4. The released Setup chooses the candidate as GitHub's newest release.
Write-Host ''
Write-Host "== $($rel.tag)'s Setup: --info with the candidate as the newest release" -ForegroundColor Cyan
$download = Join-Path ([IO.Path]::GetTempPath()) 'LivingServer-Setup-download'
New-Item -ItemType Directory -Force -Path $download | Out-Null
foreach ($name in $feed.assets) { Copy-Item -LiteralPath (Join-Path $Candidate $name) -Destination (Join-Path $download $name) -Force }
$setupLog = Join-Path $logs 'released-setup-info.log'
$p = Start-Process -FilePath $rel.setup -ArgumentList @('--silent', '--info', '--release-json', ('"' + (Join-Path $Candidate 'latest.json') + '"'), '--log', ('"' + $setupLog + '"')) -Wait -PassThru -NoNewWindow
$text = if (Test-Path $setupLog) { Get-Content -LiteralPath $setupLog -Raw } else { '' }
($text -split "`r?`n" | Where-Object { $_ -match 'PACKAGE |WOULD INSTALL|newer|digest|refused' }) | ForEach-Object { Write-Host "   $_" }
Check ($p.ExitCode -eq 0 -and $text -match "WOULD INSTALL $([regex]::Escape($feed.build)) ") "$($rel.tag)'s Setup installs $($feed.tag) when it is GitHub's newest release"
Remove-Item -LiteralPath $download -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed: $($rel.tag) players would not get $($feed.tag)." -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host "The update contract holds: $($rel.tag)'s launcher and Setup take $($feed.tag)." -ForegroundColor Green
exit 0
