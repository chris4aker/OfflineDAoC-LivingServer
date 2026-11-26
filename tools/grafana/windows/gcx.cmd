@echo off
rem gcx against this PC's Grafana (issue #628): its config stays in this folder, nothing goes to the credential store.
rem   gcx.cmd resources push -p "%~dp0resources"     gcx.cmd logs metrics -d offlinedaoc-loki "..." --since 1h
setlocal
set "GCX_CONFIG=%~dp0gcx\config.yaml"
set "GCX_KEYCHAIN=off"
for /f "delims=" %%e in ('dir /b /s "%~dp0bin\gcx.exe"') do set "GCX_EXE=%%e"
"%GCX_EXE%" %*
