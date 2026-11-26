<#
.SYNOPSIS
  Removes this kit's Alloy for the current Windows user (issue #649). Keeps monitoring.env unless -Purge.
  Touches nothing else: any other Alloy or Grafana Agent stays as it was.
#>
[CmdletBinding()]
param([string]$AddonHome = '', [switch]$Purge)
$ErrorActionPreference = 'Stop'
if (-not $AddonHome) { $AddonHome = Join-Path $env:LOCALAPPDATA 'LivingServerMonitoring' }
$TaskName = 'LivingServerAlloy'
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path ([Environment]::GetFolderPath('Startup')) "$TaskName.lnk")
# Stop the start script first (it restarts Alloy), then this kit's Alloy only.
Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*$AddonHome*run-alloy.ps1*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($AddonHome, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $AddonHome) {
    if ($Purge) { Remove-Item -Recurse -Force $AddonHome }
    else { Get-ChildItem -LiteralPath $AddonHome | Where-Object { $_.Name -ne 'monitoring.env' } | Remove-Item -Recurse -Force }
}
Write-Host "[uninstall] removed the logon start and $AddonHome$(if (-not $Purge) { ' (kept monitoring.env; -Purge deletes it)' })."
Write-Host '[uninstall] In Grafana Cloud, revoke the access policy token and delete the gcx service account if you no longer use them.'
