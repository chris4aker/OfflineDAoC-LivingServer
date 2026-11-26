@echo off
rem Offline DAoC observability: stop everything, then delete this folder after a prompt (issue #628).
rem Runs from the parent folder, on one line, because this file is deleted with the folder.
cd /d "%~dp0.." & powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0observability.ps1" -Action uninstall & pause & exit /b
