#!/usr/bin/env bash
# Start Offline DAoC (with Living Server) on Linux under Wine: the Linux counterpart of START OFFLINE DAOC.cmd.
#
#   linux/start-offline-daoc.sh [game folder]
#
# The game folder is the one that holds "START OFFLINE DAOC.cmd" and runtime/. It defaults to $ODAOC_GAME, then
# ~/OfflineDAoC/game. The Wine prefix is $WINEPREFIX, else <game folder>/../wineprefix.
#
# On top of what the .cmd does, this adds two Linux fixes:
#   - Updates: the launcher's "Get update" hands the install to Windows PowerShell, which Wine doesn't have (its
#     powershell.exe is a stub that runs nothing). finish_launcher_update.py does the install instead, with the same
#     checks and backups as INSTALL.cmd, and the launcher opens again, as it does on Windows.
#   - Fullscreen: under GNOME the client's borderless window sits below the top bar and its bottom edge (Continue /
#     Accept buttons) falls off the screen. daoc_fullscreen.py makes the client window truly fullscreen.
set -euo pipefail
# A set CDPATH makes cd go somewhere else for a relative folder and print it, which would end up in GAME.
unset CDPATH

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="${1:-${ODAOC_GAME:-$HOME/OfflineDAoC/game}}"
GAME="$(cd "$GAME" 2>/dev/null && pwd)" || { echo "No game folder at ${1:-${ODAOC_GAME:-$HOME/OfflineDAoC/game}}" >&2; exit 1; }
[[ -f "$GAME/runtime/OfflineDAoC.exe" ]] || { echo "$GAME has no runtime/OfflineDAoC.exe: is it the game folder?" >&2; exit 1; }

export WINEPREFIX="${WINEPREFIX:-$(dirname "$GAME")/wineprefix}"
[[ -d "$WINEPREFIX" ]] || { echo "No Wine prefix at $WINEPREFIX (set WINEPREFIX)" >&2; exit 1; }
for tool in wine winepath python3; do
  command -v "$tool" >/dev/null || { echo "$tool is not installed" >&2; exit 1; }
done

LOGS="$GAME/runtime/logs"
mkdir -p "$LOGS"
cd "$GAME"
G="$(winepath -w "$GAME" 2>/dev/null || echo "Z:${GAME//\//\\}")"

# Installs an update the launcher closed for. 0 = installed, 1 = failed (the launcher shows why), 3 = none waiting,
# 4 = one is waiting but the game is still running, 5 = the script itself failed.
finish_update() { python3 -I "$HERE/finish_launcher_update.py" "$GAME"; }

# An update left half done by an earlier run (the launcher closed for it, but it never got installed): finish it first.
finish_update || true

while :; do
  WINEDEBUG="${WINEDEBUG:--all,err+module,err+seh}" \
  DOTNET_ROOT="$G\\tools\\dotnet" DOTNET_ROOT_X64="$G\\tools\\dotnet" DOTNET_MULTILEVEL_LOOKUP=0 \
    wine runtime/OfflineDAoC.exe >>"$LOGS/linux-launcher.log" 2>&1 &
  LAUNCHER=$!

  # Keep the client window fullscreen for as long as the launcher runs.
  python3 -I "$HERE/daoc_fullscreen.py" --watch --while-pid "$LAUNCHER" >>"$LOGS/linux-fullscreen.log" 2>&1 &

  wait "$LAUNCHER" || true
  # Did the launcher close for an update? Then open it again, whether the install worked (0) or failed (1): as on
  # Windows, it shows the result (runtime/updates/last-update.txt). Anything else and we're done: nothing was waiting
  # (3), the game is still running (4), or the script failed (5).
  rc=0
  finish_update || rc=$?
  if [[ $rc -ne 0 && $rc -ne 1 ]]; then
    break
  fi
done
