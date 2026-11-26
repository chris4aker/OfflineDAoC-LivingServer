<#
.SYNOPSIS
  Installs this kit's own Alloy for the current Windows user (issue #649). No admin, no service, no PATH change.
.DESCRIPTION
  Downloads the pinned Alloy release and checks its SHA256 (pins.json), copies the configs into
  %LOCALAPPDATA%\LivingServerMonitoring, creates monitoring.env from the example if it is missing (readable by this user
  only; the player fills it in), and starts Alloy at logon: a per-user scheduled task, or a Startup-folder shortcut where
  Windows refuses the task without admin. Any Alloy or Grafana Agent already on the PC is left alone.
  Tests only: not yet run on a Windows PC.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\grafana\cloud\install.ps1 -Port 12347
#>
[CmdletBinding()]
param(
    [int]$Port = 12347,
    [string]$AddonHome = '',
    [switch]$NoStart,
    [switch]$DryRun
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$Kit = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $AddonHome) { $AddonHome = Join-Path $env:LOCALAPPDATA 'LivingServerMonitoring' }
$TaskName = 'LivingServerAlloy'

function Say([string]$m) { Write-Host "[install] $m" }
function Step([string]$what, [scriptblock]$do) { if ($DryRun) { Write-Host "[dry-run] $what" } else { & $do } }

$pins = (Get-Content -Raw -LiteralPath (Join-Path $Kit 'pins.json') | ConvertFrom-Json).alloy
$pin = $pins.'windows-amd64'
$bin = Join-Path $AddonHome 'bin'; $conf = Join-Path $AddonHome 'config'; $data = Join-Path $AddonHome 'data'
$downloads = Join-Path $AddonHome 'downloads'; $logs = Join-Path $AddonHome 'logs'
$envFile = Join-Path $AddonHome 'monitoring.env'; $alloyExe = Join-Path $bin 'alloy.exe'
Say "Alloy $($pins.version) into $AddonHome, UI on 127.0.0.1:$Port"

# 1. Download and verify
Step "create folders under $AddonHome" { foreach ($d in @($bin, $conf, $data, $downloads, $logs)) { New-Item -ItemType Directory -Force -Path $d | Out-Null } }
$zip = Join-Path $downloads $pin.file
$haveIt = (Test-Path $alloyExe) -and ((& $alloyExe --version 2>$null | Out-String) -match [regex]::Escape("v$($pins.version)"))
if ($haveIt) { Say "Alloy $($pins.version) already in $bin" } else {
    Step "download $($pin.url)" {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        if (-not (Test-Path $zip) -or (Get-FileHash -Algorithm SHA256 $zip).Hash -ne $pin.sha256.ToUpper()) {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -UseBasicParsing -Uri $pin.url -OutFile "$zip.part"
            Move-Item -Force "$zip.part" $zip
        }
    }
    Step "verify SHA256 $($pin.sha256) and extract $($pin.exe)" {
        $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash
        if ($hash -ne $pin.sha256.ToUpper()) { Remove-Item -Force $zip; throw "SHA256 mismatch for $($pin.file): refusing it" }
        Say 'SHA256 ok'
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ("lsalloy-" + [guid]::NewGuid())
        Expand-Archive -LiteralPath $zip -DestinationPath $tmp
        Copy-Item -Force (Join-Path $tmp $pin.exe) $alloyExe
        Remove-Item -Recurse -Force $tmp
    }
}

# 2. Configs (replaced on every run; they hold no secrets) and the start script
Step "copy configs to $conf" {
    Get-ChildItem -LiteralPath $conf -Filter '*.alloy' -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $Kit 'alloy\common\*.alloy'), (Join-Path $Kit 'alloy\windows\*.alloy') $conf
    Copy-Item -Force (Join-Path $Kit 'run-alloy.ps1') (Join-Path $AddonHome 'run-alloy.ps1')
}

# 3. Env file: created from the example once, never overwritten, readable by this user only
if (-not (Test-Path $envFile)) {
    Step "create $envFile (this user only)" {
        Copy-Item (Join-Path $Kit 'monitoring.env.example') $envFile
        Add-Content -LiteralPath $envFile -Value "`r`nLS_ALLOY_ADDR=127.0.0.1:$Port"
        & icacls $envFile /inheritance:r /grant:r "$($env:USERNAME):(R,W)" | Out-Null
    }
    Say "the player fills in $envFile (runbook step U2)"
} elseif ($PSBoundParameters.ContainsKey('Port')) {
    # A rerun keeps its port unless -Port is given.
    Step "set LS_ALLOY_ADDR=127.0.0.1:$Port in $envFile" {
        $lines = @(Get-Content -LiteralPath $envFile | Where-Object { $_ -notmatch '^\s*LS_ALLOY_ADDR=' })
        Set-Content -LiteralPath $envFile -Value ($lines + "LS_ALLOY_ADDR=127.0.0.1:$Port")
    }
}

# 4. Start at logon: a per-user scheduled task, else a Startup-folder shortcut
$psExe = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path $psExe)) { $psExe = (Get-Process -Id $PID).Path }
$runArgs = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$(Join-Path $AddonHome 'run-alloy.ps1')`""
$startupDir = [Environment]::GetFolderPath('Startup')
$startup = ''
if ($startupDir) { $startup = Join-Path $startupDir "$TaskName.lnk" }
Step "register logon start ($TaskName)" {
    $user = "$env:USERDOMAIN\$env:USERNAME"
    try {
        $action = New-ScheduledTaskAction -Execute $psExe -Argument $runArgs -WorkingDirectory $AddonHome
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
        Say "scheduled task $TaskName starts Alloy at logon"
    } catch {
        Say "scheduled task refused ($($_.Exception.Message.Trim())); using a Startup-folder shortcut instead"
        $sh = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($startup)
        $lnk.TargetPath = $psExe; $lnk.Arguments = $runArgs; $lnk.WorkingDirectory = $AddonHome; $lnk.WindowStyle = 7
        $lnk.Save()
    }
}

# 5. Start now, only with a complete env file
if (-not $NoStart) {
    $ok = $true
    if (-not $DryRun) {
        & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'detect.ps1') -CheckEnv $envFile
        $ok = ($LASTEXITCODE -eq 0)
    }
    if (-not $ok) { Say "not started: fill in $envFile, then run install.ps1 again"; exit 0 }
    Step 'stop a running copy of this kit (a rerun), then start Alloy now' {
        Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine -like "*$AddonHome*run-alloy.ps1*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($AddonHome, [StringComparison]::OrdinalIgnoreCase) } |
            Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Process -FilePath $psExe -ArgumentList $runArgs -WindowStyle Hidden -WorkingDirectory $AddonHome }
    Say "started; Alloy's own page: http://127.0.0.1:$Port"
}
Say 'done. Undo: tools\grafana\cloud\uninstall.ps1'
