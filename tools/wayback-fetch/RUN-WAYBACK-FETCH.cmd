@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0wayback-fetch.ps1" goto missing
echo Wayback fetch: slow, polite downloads of archived 2002-2005 pages.
echo One request about every 3 seconds. You can close this window any time;
echo run it again later and it continues where it stopped.
echo Results: %~dp0out\
echo.
if "%~1"=="" goto alljobs

:nextjob
if "%~1"=="" goto done
echo Job: %~1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0wayback-fetch.ps1" -Root "%~dp0." -Job "%~1"
if errorlevel 1 set FAILED=1
shift
goto nextjob

:alljobs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0wayback-fetch.ps1" -Root "%~dp0."
if errorlevel 1 set FAILED=1

:done
echo.
if defined FAILED (echo Something did not finish - see the newest log in out\_logs\) else (echo Done. The newest log in out\_logs\ ends with a SUMMARY line.)
pause
exit /b 0

:missing
echo wayback-fetch.ps1 is missing. Copy the whole wayback-fetch folder, then retry.
pause
exit /b 1
