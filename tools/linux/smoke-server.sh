#!/usr/bin/env bash
# Living Server Linux prototype (docs/LINUX.md): the headless smoke of a Linux game folder made by
# assemble-server.sh. Starts the server with linux/offlinedaoc as a player would and checks the log (the build, Detour,
# navmeshes). Then two scripted players log in (source/tools/OfflineDaoc.Playtester, --attach), the server stops
# with "exit", and it starts and stops once more.
#
#   tools/linux/smoke-server.sh --game <folder> --playtester <OfflineDaoc.Playtester.dll> --out <report folder>
#       [--scenarios chat-players,group-players]
#
# The folder's world must be a test world (make_test_world.py): the playtester refuses any other. Exit 0 = all passed.
set -uo pipefail

GAME="" PLAYTESTER="" OUT="" SCENARIOS="chat-players,group-players"
while [ $# -gt 0 ]; do
  case "$1" in
    --game) GAME=$2; shift 2 ;;
    --playtester) PLAYTESTER=$2; shift 2 ;;
    --out) OUT=$2; shift 2 ;;
    --scenarios) SCENARIOS=$2; shift 2 ;;
    *) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
  esac
done
[ -n "$GAME" ] && [ -n "$PLAYTESTER" ] && [ -n "$OUT" ] || { sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
GAME=$(cd "$GAME" && pwd); mkdir -p "$OUT"; OUT=$(cd "$OUT" && pwd)
HERE=$(cd "$(dirname "$0")" && pwd)
CLI="$GAME/linux/offlinedaoc"
LOG="$GAME/runtime/logs/server-console.log"
PORT=$(sed -n 's:.*<Port>\([0-9]*\)</Port>.*:\1:p' "$GAME/runtime/server/config/serverconfig.xml" | head -n 1)
FAILED=0
pass() { echo "PASS  $*" | tee -a "$OUT/smoke.txt"; }
fail() { echo "FAIL  $*" | tee -a "$OUT/smoke.txt"; FAILED=1; }
: > "$OUT/smoke.txt"

# 1. Start as a player would.
if "$CLI" start; then pass "server started and listens on $PORT"; else fail "server start"; cp -f "$LOG" "$OUT/" 2>/dev/null; exit 1; fi
grep -q "FORK_BUILD name=" "$LOG" && pass "log: $(grep -o 'FORK_BUILD name=[^ ]* commit=[^ ]*' "$LOG" | head -n 1)" || fail "no FORK_BUILD line in the log"
if grep -q "did not find the Detour library" "$LOG"; then fail "the server did not find lib/Detour.so"; else pass "no Detour library error"; fi
# "Loading NavMesh failed ... (File not found" is a zone without a mesh file (debug); only the others are failures.
meshes=$(grep -c "Loading NavMesh successful" "$LOG")
failedmeshes=$(grep "Loading NavMesh failed" "$LOG" | grep -vc "File not found")
if [ -L "$GAME/runtime/server/navmesh" ] || [ -d "$GAME/runtime/server/navmesh" ]; then
  files=$(find -L "$GAME/runtime/server/navmesh" -maxdepth 1 -name '*.nav' | wc -l)
  [ "$meshes" -eq "$files" ] && [ "$failedmeshes" -eq 0 ] && pass "navmeshes loaded through Detour: $meshes of $files .nav files" ||
    fail "navmeshes: $meshes of $files .nav files loaded, $failedmeshes failed"
else
  echo "SKIP  no navmesh folder (CI without game files): Detour only loaded, no zone meshes" | tee -a "$OUT/smoke.txt"
fi
pid=$(cat "$GAME/linux/run/server.pid")
grep -q "lib/Detour.so" "/proc/$pid/maps" && pass "lib/Detour.so mapped into the server process" ||
  echo "INFO  lib/Detour.so not mapped yet (it loads with the first navmesh)" | tee -a "$OUT/smoke.txt"

# 3. Log in: two scripted players on the original 1.127 protocol.
dotnet "$PLAYTESTER" run --world "$GAME/runtime/data/opendaoc.sqlite3.db" --attach --port "$PORT" \
    --scenarios "$SCENARIOS" --out "$OUT/playtest" > "$OUT/playtester.txt" 2>&1
code=$?
[ $code -eq 0 ] && pass "playtester ($SCENARIOS): exit 0" || fail "playtester ($SCENARIOS): exit $code (see playtest/report.md)"

# 4. Stop with "exit", start again, stop again.
"$CLI" stop && pass "stopped with exit" || fail "stop"
"$CLI" status | grep -q STOPPED && pass "status says STOPPED" || fail "status after stop"
cp -f "$LOG" "$OUT/server-console-1.log"
if "$CLI" start; then pass "second start"; else fail "second start"; fi
"$CLI" stop && pass "second stop" || fail "second stop"
cp -f "$LOG" "$OUT/server-console-2.log"
echo "Smoke: $([ $FAILED -eq 0 ] && echo passed || echo FAILED). Report: $OUT/smoke.txt"
exit $FAILED
