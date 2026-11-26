@echo off
rem Offline DAoC observability: start Grafana, Loki and Alloy in the background, then open http://127.0.0.1:3000 (issue #628).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0observability.ps1" -Action start
if errorlevel 1 pause
