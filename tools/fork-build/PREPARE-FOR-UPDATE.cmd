@echo off
setlocal
cd /d "%~dp0"
rem Puts the released server and launcher back before a base game update (ROLLBACK.cmd -ToRelease).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0rollback-server-build.ps1" -ToRelease %*
echo.
pause
