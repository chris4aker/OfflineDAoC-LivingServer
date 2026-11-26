@echo off
rem Packs this PC's navmeshes for CI's navmesh tests (issue #684). It only reads the game install, writes only to
rem C:\Games\OfflineDAoC\navmesh-upload, then opens GitHub's new-release page and shows the upload steps.
rem Another install: RUN-ME.cmd -NavmeshFolder "D:\Games\LivingServer\runtime\server\navmesh"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-NavmeshSet.ps1" %*
echo.
pause
