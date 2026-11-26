@echo off
rem Offline DAoC observability: stop Grafana, Loki and Alloy (issue #628).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0observability.ps1" -Action stop
