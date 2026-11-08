#!/usr/bin/env bash
# Living Server 1.2.1 Linux prototype (docs/LINUX-1.2.1.md): the headless smoke of a Linux game folder made by
# assemble-server.sh. Starts the server with linux/offlinedaoc as a player would, checks the log (the build, Detour,
# navmeshes), logs two scripted players in with the playtester (source/tools/OfflineDaoc.Playtester, --attach), stops
# the server with "exit", then starts and stops it once more. Optionally (--new-client <engine>) the released 0.37c
# new client is checked on that Linux engine (tools/linux/newclient_check.py) and started headless with
# `offlinedaoc play --headless` (ENTER REALM: the folder's own account) against the same server.
#
#   tools/linux/smoke-server.sh --game <folder> --playtester <OfflineDaoc.Playtester.dll> --out <report folder>
#       [--scenarios chat-players,group-players] [--new-client <Godot mono linux executable>]
#
# The folder's world must be a test world (make_test_world.py): the playtester refuses any other. Exit 0 = all passed.
set -uo pipefail

GAME="" PLAYTESTER="" OUT="" SCENARIOS="chat-players,group-players" ENGINE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --game) GAME=$2; shift 2 ;;
    --playtester) PLAYTESTER=$2; shift 2 ;;
    --out) OUT=$2; shift 2 ;;
    --scenarios) SCENARIOS=$2; shift 2 ;;
    --new-client) ENGINE=$2; shift 2 ;;
    *) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
  esac
done
[ -n "$GAME" ] && [ -n "$PLAYTESTER" ] && [ -n "$OUT" ] || { sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
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
if grep -q "did not find the Detour library" "$LOG"; then fail "the server did not find lib/libDetour.so"; else pass "no Detour library error"; fi
meshes=$(grep -c "Loading NavMesh successful" "$LOG"); failedmeshes=$(grep -c "Loading NavMesh failed" "$LOG")
if [ -L "$GAME/runtime/server/navmesh" ] || [ -d "$GAME/runtime/server/navmesh" ]; then
  [ "$meshes" -gt 0 ] && [ "$failedmeshes" -eq 0 ] && pass "navmeshes loaded through Detour: $meshes zones" || fail "navmeshes: $meshes loaded, $failedmeshes failed"
else
  echo "SKIP  no navmesh folder (CI without game files): Detour only loaded, no zone meshes" | tee -a "$OUT/smoke.txt"
fi
pid=$(cat "$GAME/linux/run/server.pid")
grep -q "libDetour.so" "/proc/$pid/maps" && pass "libDetour.so mapped into the server process" ||
  echo "INFO  libDetour.so not mapped yet (it loads with the first navmesh)" | tee -a "$OUT/smoke.txt"

# 2. Log in: two scripted players on the original 1.127 protocol.
dotnet "$PLAYTESTER" run --world "$GAME/runtime/data/opendaoc.sqlite3.db" --attach --port "$PORT" \
    --scenarios "$SCENARIOS" --out "$OUT/playtest" > "$OUT/playtester.txt" 2>&1
code=$?
[ $code -eq 0 ] && pass "playtester ($SCENARIOS): exit 0" || fail "playtester ($SCENARIOS): exit $code (see playtest/report.md)"

# 3. Optional: the new client, headless, with the folder's own account (the launcher's ENTER REALM).
if [ -n "$ENGINE" ]; then
  "$CLI" account > /dev/null
  acc=$(sed -n 's/^Account:[[:space:]]*//p' "$GAME/runtime/account.txt" | tr -d '\r')
  python3 -I "$HERE/newclient_check.py" "$ENGINE" "$OUT/new-client" > "$OUT/new-client.txt" 2>&1
  [ $? -eq 0 ] && pass "new client: the released 0.37c scripts and C# build and compile on the Linux engine" || fail "new client check (new-client.txt)"
  # a folder without game files: the checked project and the engine stand in for new-client/godot and linux/godot
  [ -e "$GAME/new-client/godot" ] || { mkdir -p "$GAME/new-client"; ln -s "$OUT/new-client/new-client/godot" "$GAME/new-client/godot"; }
  [ -e "$GAME/linux/godot" ] || ln -s "$(cd "$(dirname "$ENGINE")" && pwd)" "$GAME/linux/godot"
  before=$(grep -c . "$LOG")
  timeout 60 "$CLI" play --headless > "$OUT/new-client-play.log" 2>&1
  echo "INFO  offlinedaoc play --headless ran 60 s (exit $?; 124 = stopped by the time limit)" | tee -a "$OUT/smoke.txt"
  if tail -n +"$before" "$LOG" | grep -qi "User $acc logging on"; then
    pass "server log: the new client's account ($acc) logged on"
  else
    fail "the server log doesn't show $acc logging on"
  fi
  # the server's own record: AutoAccountCreation made the account on the client's first login
  if python3 -I -c 'import sqlite3,sys
c=sqlite3.connect("file:"+sys.argv[1]+"?mode=ro",uri=True)
sys.exit(0 if c.execute("SELECT COUNT(*) FROM Account WHERE Name=?",(sys.argv[2],)).fetchone()[0] else 1)' \
      "$GAME/runtime/data/opendaoc.sqlite3.db" "$acc" 2>/dev/null; then
    pass "the server has the account $acc (made at the new client's login)"
  else
    fail "no Account row for $acc"
  fi
fi

# 4. Stop with "exit", start again, stop again.
"$CLI" stop && pass "stopped with exit" || fail "stop"
"$CLI" status | grep -q STOPPED && pass "status says STOPPED" || fail "status after stop"
cp -f "$LOG" "$OUT/server-console-1.log"
if "$CLI" start; then pass "second start"; else fail "second start"; fi
"$CLI" stop && pass "second stop" || fail "second stop"
cp -f "$LOG" "$OUT/server-console-2.log"
echo "Smoke: $([ $FAILED -eq 0 ] && echo passed || echo FAILED). Report: $OUT/smoke.txt"
exit $FAILED
