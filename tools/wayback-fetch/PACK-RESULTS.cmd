@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0pack-results.ps1" goto missing
echo Zips each job's results (saved pages, index.csv, manifest.jsonl) into out\_packs\,
echo one zip per job, named JOB-YYYYMMDD.zip. Jobs unchanged since their last pack are skipped.
echo It reads only files on this PC and downloads nothing.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack-results.ps1" -Folder "%~dp0."
echo.
pause
exit /b 0

:missing
echo pack-results.ps1 is missing. Copy the whole wayback-fetch folder, then retry.
pause
exit /b 1
