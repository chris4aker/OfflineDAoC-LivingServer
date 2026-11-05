@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0make-thread-job.ps1" goto missing
echo Builds jobs\a2-raid-threads.json from the forum listings already saved in out\a-raid-sizes\.
echo It reads only files on this PC and downloads nothing.
echo Then double-click RUN-WAYBACK-FETCH.cmd to fetch the threads. Run this again after
echo that: threads not found by their exact link are looked up by thread number instead.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-thread-job.ps1" -Folder "%~dp0."
echo.
pause
exit /b 0

:missing
echo make-thread-job.ps1 is missing. Copy the whole wayback-fetch folder, then retry.
pause
exit /b 1
