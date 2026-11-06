# Living Server: the last step of an update the launcher started. The launcher writes this file to
# runtime\updates\apply-update.ps1, starts it in its own window and closes, because the install replaces the
# launcher's own files and refuses while the launcher runs.
#
# 1. Waits for the launcher to close.
# 2. Runs the package's INSTALL.cmd, exactly as a player double-clicking it would (no elevation): it checks the
#    game, refuses while anything runs, backs up every file it replaces and verifies every copy. ROLLBACK.cmd in
#    the package folder undoes it, as after a manual install.
# 3. Checks that the server and launcher are now the package's files, and writes the result for the launcher.
# 4. Opens the launcher again (unless -Restart is empty).
# Every step goes to the launcher's update log. Windows PowerShell 5.1 compatible.
param(
    [int]$LauncherPid = 0,
    [Parameter(Mandatory = $true)][string]$PackageFolder,
    [Parameter(Mandatory = $true)][string]$GameFolder,
    [Parameter(Mandatory = $true)][string]$Build,
    [Parameter(Mandatory = $true)][string]$Log,
    [Parameter(Mandatory = $true)][string]$Result,
    [string]$Restart = '',
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
try { $host.UI.RawUI.WindowTitle = "Living Server: updating to $Build" } catch { }

function Say([string]$text) {
    Write-Host $text
    try { Add-Content -LiteralPath $Log -Value ('{0}  apply-update: {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $text) -Encoding UTF8 } catch { }
}
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Finish([string]$state, [string]$detail) {
    Set-Content -LiteralPath $Result -Encoding UTF8 -Value @(
        "Build=$Build", "Result=$state", "Detail=$detail", "Time=$((Get-Date).ToUniversalTime().ToString('o'))")
    Say "$state`: $detail"
    if ($Restart -and (Test-Path -LiteralPath $Restart)) {
        if (!$NoPause) { Write-Host ''; Read-Host 'Press Enter to open the launcher again' | Out-Null }
        # The game's own .NET, as START OFFLINE DAOC.cmd sets it (the launcher's environment is inherited too).
        $dotnet = Join-Path $GameFolder 'tools\dotnet'
        if (Test-Path -LiteralPath (Join-Path $dotnet 'dotnet.exe')) {
            $env:DOTNET_ROOT = $dotnet; $env:DOTNET_ROOT_X64 = $dotnet; $env:DOTNET_MULTILEVEL_LOOKUP = '0'
        }
        Say "Opening the launcher: $Restart"
        Start-Process -FilePath $Restart -WorkingDirectory (Split-Path -Parent $Restart)
    } elseif (!$NoPause) {
        Write-Host ''; Read-Host 'Press Enter to close this window' | Out-Null
    }
    if ($state -eq 'installed') { exit 0 } else { exit 1 }
}

try {
    Say "Update to $Build, from $PackageFolder into $GameFolder"
    if ($LauncherPid -gt 0) {
        Say "Waiting for the launcher (process $LauncherPid) to close..."
        $deadline = (Get-Date).AddSeconds(120)
        while ((Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue) {
            Finish 'not installed' 'the launcher did not close within two minutes; nothing was changed. Close it and try again.'
        }
    }
    $install = Join-Path $PackageFolder 'INSTALL.cmd'
    $info = @{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $PackageFolder 'BUILD-INFO.txt')) {
        if ($line -match '^\s*([^=#]+?)\s*=\s*(.*?)\s*$') { $info[$Matches[1]] = $Matches[2] }
    }
    if (!(Test-Path -LiteralPath $install) -or $info['BuildName'] -ne $Build) {
        Finish 'not installed' "the package folder isn't build $Build's; nothing was changed."
    }

    # INSTALL.cmd ends with a pause; its input is empty so it doesn't wait (this window waits instead), and its output
    # is kept for the log.
    Say 'Running INSTALL.cmd (it backs up every file it replaces)...'
    $output = Join-Path (Split-Path -Parent $Result) 'apply-update-install-output.txt'
    $line = '"' + $install + '" -GameFolder "' + $GameFolder.TrimEnd('\') + '" < NUL 2>&1'
    $p = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList ('/d /s /c "' + $line + '"') `
        -WorkingDirectory $PackageFolder -NoNewWindow -Wait -PassThru -RedirectStandardOutput $output
    foreach ($text in @(Get-Content -LiteralPath $output -ErrorAction SilentlyContinue)) {
        if ($text -notmatch '^Press any key') { Say "  $text" }
    }
    Say "INSTALL.cmd finished (exit $($p.ExitCode))."

    # Installed means: the server and launcher in the game folder are now this package's files.
    $ok = (Test-Path -LiteralPath (Join-Path $GameFolder 'runtime\server\GameServer.dll')) -and
          (Sha (Join-Path $GameFolder 'runtime\server\GameServer.dll')) -eq $info['GameServer.dll'].ToLowerInvariant()
    if ($ok -and $info['launcher/OfflineDAoC.dll']) {
        $ok = (Sha (Join-Path $GameFolder 'runtime\OfflineDAoC.dll')) -eq $info['launcher/OfflineDAoC.dll'].ToLowerInvariant()
    }
    if ($ok) { Finish 'installed' "Living Server $Build is installed. ROLLBACK.cmd in $PackageFolder undoes it." }
    Finish 'not installed' 'INSTALL.cmd did not install it (its output is above and in the log); the game is as it was before.'
} catch {
    try { Finish 'not installed' "the update stopped: $($_.Exception.Message)" } catch { exit 1 }
}
