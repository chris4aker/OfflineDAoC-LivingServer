@echo off
rem Offline DAoC observability: which of Grafana, Loki and Alloy run and answer (issue #628).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0observability.ps1" -Action status
pause
