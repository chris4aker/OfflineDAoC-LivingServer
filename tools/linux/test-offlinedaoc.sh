#!/usr/bin/env bash
# Tests tools/linux/offlinedaoc (the Wine route's Linux script) against a fake game folder, with a real Living Server
# package and PowerShell 7 for Linux. No Wine, no game files: install, reinstall, refusal while the game runs, rollback.
#
#   tools/linux/test-offlinedaoc.sh <package folder LivingServer-<build>>
set -uo pipefail

PKG=$(cd "${1:?usage: test-offlinedaoc.sh <package folder>}" && pwd)
HERE="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
WORK=$(mktemp -d "${TMPDIR:-/tmp}/offlinedaoc-test-XXXXXX")
trap 'kill "${FAKE_PID:-0}" 2> /dev/null; rm -rf "$WORK"' EXIT
fails=0
check() { if eval "$1"; then echo "PASS  $2"; else echo "FAIL  $2"; fails=$((fails + 1)); [ -n "${3:-}" ] && echo "$3" | sed 's/^/      /'; fi; }
sha() { sha256sum "$1" | cut -c1-64; }
info() { sed -n "s#^$1=##p" "$PKG/BUILD-INFO.txt" | tr -d '\r' | tr 'A-F' 'a-f'; }
base=$(info BaseRelease | cut -d, -f1)

# A game as released (the layout test-install-scripts.ps1 fakes): the server, its libraries, the launcher, a world and
# PACKAGE MANIFEST.sha256 naming the release notes of an updated game.
GAME="$WORK/game"
manifest="$GAME/PACKAGE MANIFEST.sha256"
mkdir -p "$GAME"
: > "$manifest"
add() { mkdir -p "$(dirname "$GAME/$1")"; printf '%s' "$2" > "$GAME/$1"; echo "$(sha "$GAME/$1")  $1" >> "$manifest"; }
for f in runtime/server runtime/server/lib runtime/server/win-x64; do
  add "$f/GameServer.dll" "original release server"; add "$f/GameServer.pdb" "original release symbols"
done
for f in runtime/server/CoreBase.dll runtime/server/CoreBase.pdb runtime/server/CoreDatabase.dll runtime/server/CoreDatabase.pdb \
         runtime/server/CoreServer.dll runtime/server/CoreServer.pdb runtime/server/lib/CoreBase.dll runtime/server/lib/CoreBase.pdb \
         runtime/server/lib/CoreDatabase.dll runtime/server/lib/CoreDatabase.pdb runtime/server/win-x64/CoreBase.dll \
         runtime/server/win-x64/CoreDatabase.dll runtime/server/win-x64/CoreServer.dll; do
  add "$f" "original release $f"
done
for f in OfflineDAoC.dll OfflineDAoC.exe OfflineDAoC.pdb OfflineDAoC.deps.json OfflineDAoC.runtimeconfig.json; do
  add "runtime/$f" "original launcher $f"
done
add "docs/RELEASE-$base.md" "release $base"
mkdir -p "$GAME/runtime/data" "$GAME/linux"
WORLD="the player's world and saves"
printf '%s' "$WORLD" > "$GAME/runtime/data/opendaoc.sqlite3.db"
install -m 0755 "$HERE/offlinedaoc" "$GAME/linux/offlinedaoc"
OD="$GAME/linux/offlinedaoc"

zip_dir="$WORK/zip"; mkdir -p "$zip_dir"
( cd "$(dirname "$PKG")" && zip -qr "$zip_dir/OfflineDAoC-LivingServer-test.zip" "$(basename "$PKG")" )

out=$("$OD" status 2>&1); check '[ $? -eq 0 ] && grep -q "port:      10300 (free)" <<< "$out"' "status reads the game" "$out"

out=$("$OD" install "$zip_dir/OfflineDAoC-LivingServer-test.zip" 2>&1); code=$?
check '[ $code -eq 0 ]' "install from the zip succeeds" "$out"
for f in runtime/server runtime/server/lib runtime/server/win-x64; do
  check '[ "$(sha "$GAME/$f/GameServer.dll")" = "$(info GameServer.dll)" ]' "new GameServer.dll in $f"
done
check '[ "$(sha "$GAME/runtime/OfflineDAoC.dll")" = "$(info launcher/OfflineDAoC.dll)" ]' "new launcher"
check '[ "$(sha "$GAME/runtime/server/CoreDatabase.dll")" = "$(info core/CoreDatabase.dll)" ]' "new server library"
check 'ls "$GAME"/runtime/server/deployment-backups/fork-build-*/data/opendaoc.sqlite3.db > /dev/null 2>&1' "the world is backed up first"
check '[ -d "$GAME/runtime/updates/$(basename "$PKG")" ]' "the package is unpacked into runtime/updates"
out=$("$OD" status 2>&1); check 'grep -q "installed: $(info BuildName)" <<< "$out"' "status names the installed build" "$out"

out=$("$OD" install "$zip_dir/OfflineDAoC-LivingServer-test.zip" 2>&1)
check 'grep -q "Already installed" <<< "$out"' "reinstall is a no-op" "$out"

# A game part running (Wine names a process after its .exe): refused, nothing changed.
cp "$(command -v sleep)" "$WORK/CoreServer.exe"; "$WORK/CoreServer.exe" 60 & FAKE_PID=$!
before=$(sha "$GAME/runtime/server/GameServer.dll")
out=$("$OD" rollback 2>&1); code=$?
check '[ $code -ne 0 ] && grep -q "running: CoreServer" <<< "$out" && [ "$(sha "$GAME/runtime/server/GameServer.dll")" = "$before" ]' \
  "refused while CoreServer.exe runs, nothing changed" "$out"
kill "$FAKE_PID" 2> /dev/null; wait "$FAKE_PID" 2> /dev/null

out=$("$OD" rollback 2>&1); code=$?
check '[ $code -eq 0 ] && [ "$(cat "$GAME/runtime/server/GameServer.dll")" = "original release server" ]' "rollback puts the released server back" "$out"
check '[ "$(cat "$GAME/runtime/OfflineDAoC.dll")" = "original launcher OfflineDAoC.dll" ]' "rollback puts the released launcher back"
check '[ "$(cat "$GAME/runtime/data/opendaoc.sqlite3.db")" = "$WORLD" ]' "rollback never touches the world"

out=$("$OD" install "$WORK/nothing.zip" 2>&1); code=$?
check '[ $code -ne 0 ] && grep -q "no package" <<< "$out"' "a missing package is refused" "$out"
printf 'x' > "$WORK/evil.txt"; ( cd "$WORK" && zip -q evil.zip evil.txt )
out=$("$OD" install "$WORK/evil.zip" 2>&1); code=$?
check '[ $code -ne 0 ] && grep -q "not a Living Server package" <<< "$out"' "a zip that isn't a package is refused" "$out"

[ $fails -eq 0 ] && { echo "All Linux script checks passed."; exit 0; }
echo "$fails check(s) failed"; exit 1
