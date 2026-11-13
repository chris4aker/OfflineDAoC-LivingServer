#!/usr/bin/env bash
# Start the Offline DAoC launcher under Wine, and keep the game client truly fullscreen
# so GNOME's top bar doesn't push the bottom of the game (Continue/Accept buttons) off-screen.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT/game"
. ../tools/env.sh

G="$(winepath -w "$ROOT/game" 2>/dev/null || echo "Z:${ROOT//\//\\}\\game")"
LOG="$ROOT/logs/launcher.log"

# Server memory: the server ships with Server GC, which on a 16-core CPU keeps 16 heaps and grew to
# ~6 GB in one 48-minute session. Fewer heaps plus "conserve memory" keeps it far smaller for a
# single player. The launcher passes its environment on to the server, so these reach it.
# (Values are hex; 4 heaps, conserve level 5 of 9.) Comment them out to go back to the defaults.
export DOTNET_GCHeapCount=4
export DOTNET_GCConserveMemory=5

WINEDEBUG=-all,err+module,err+seh \
DOTNET_ROOT="$G\\tools\\dotnet" DOTNET_ROOT_X64="$G\\tools\\dotnet" DOTNET_MULTILEVEL_LOOKUP=0 \
  wine runtime/OfflineDAoC.exe >"$LOG" 2>&1 &
LAUNCHER=$!

# Watch for the client window for as long as the launcher runs.
python3 -I "$ROOT/scripts/daoc_fullscreen.py" --watch --while-pid "$LAUNCHER" >>"$ROOT/logs/fullscreen.log" 2>&1 &

wait "$LAUNCHER"
