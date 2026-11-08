#!/usr/bin/env bash
# Living Server 1.2.1 Linux prototype (docs/LINUX-1.2.1.md): assembles a clean Linux game folder around a server build,
# in the layout a Linux player's folder would have. For tests and CI only; it never touches an installed game.
#
#   tools/linux/assemble-server.sh --server-build source/server/Release --detour libDetour.so --world test-world.db \
#       --out /tmp/OfflineDAoC [--dotnet <aspnetcore runtime folder>] [--navmesh <folder>] [--port 10300]
#
# The result:
#   <out>/runtime/server/            the server build (the same IL as Windows), its config, lib/libDetour.so
#   <out>/runtime/server/navmesh     a symbolic link to --navmesh (never copied), when given
#   <out>/runtime/data/opendaoc.sqlite3.db   a copy of --world (a test world: never a played database)
#   <out>/linux/offlinedaoc          the command-line launcher prototype
#   <out>/linux/dotnet/              the bundled .NET runtime, when --dotnet is given (else the system's dotnet)
set -euo pipefail

usage() { sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
SERVER_BUILD="" DETOUR="" WORLD="" OUT="" DOTNET_DIR="" NAVMESH="" PORT=10300
while [ $# -gt 0 ]; do
  case "$1" in
    --server-build) SERVER_BUILD=$2; shift 2 ;;
    --detour) DETOUR=$2; shift 2 ;;
    --world) WORLD=$2; shift 2 ;;
    --out) OUT=$2; shift 2 ;;
    --dotnet) DOTNET_DIR=$2; shift 2 ;;
    --navmesh) NAVMESH=$2; shift 2 ;;
    --port) PORT=$2; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$SERVER_BUILD" ] && [ -n "$DETOUR" ] && [ -n "$WORLD" ] && [ -n "$OUT" ] || usage
[ -f "$SERVER_BUILD/CoreServer.dll" ] && [ -f "$SERVER_BUILD/lib/GameServer.dll" ] || { echo "No server build in $SERVER_BUILD"; exit 1; }
[ -f "$DETOUR" ] || { echo "No Detour library at $DETOUR"; exit 1; }
[ -f "$WORLD" ] || { echo "No world database at $WORLD"; exit 1; }
[ ! -e "$OUT" ] || { echo "$OUT exists; give a new folder"; exit 1; }
HERE=$(cd "$(dirname "$0")" && pwd)

mkdir -p "$OUT/runtime/server" "$OUT/runtime/data" "$OUT/runtime/logs" "$OUT/linux"
# The server build without anything a run leaves behind (logs, run state) and without the build's own config.
tar -C "$SERVER_BUILD" --exclude=./logs --exclude=./config/serverconfig.xml --exclude='./*.log' \
    --exclude=./bot-world.json --exclude=./rvr-world.json --exclude=./rvr-relic-campaigns.json -cf - . \
  | tar -C "$OUT/runtime/server" -xf -
# The Windows package's lib\Detour.dll has a Linux twin: GameServer imports "lib/Detour", which .NET resolves to
# lib/libDetour.so on Linux. Added beside it, never replacing a Windows file.
install -m 0755 "$DETOUR" "$OUT/runtime/server/lib/libDetour.so"
if [ -n "$NAVMESH" ]; then ln -s "$(cd "$NAVMESH" && pwd)" "$OUT/runtime/server/navmesh"; fi
cp "$WORLD" "$OUT/runtime/data/opendaoc.sqlite3.db"

# The config a release ships (runtime/server/config/serverconfig.xml): this PC only, no UPnP, SQLite beside the server.
# Its paths are relative and use '/', so the Windows file works unchanged on Linux.
mkdir -p "$OUT/runtime/server/config"
cat > "$OUT/runtime/server/config/serverconfig.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<root>
    <Server>
        <Port>$PORT</Port>
        <IP>127.0.0.1</IP>
        <RegionIP>127.0.0.1</RegionIP>
        <RegionPort>$((PORT + 100))</RegionPort>
        <UdpIP>127.0.0.1</UdpIP>
        <UdpPort>$((PORT + 100))</UdpPort>
        <EnableUPnP>False</EnableUPnP>
        <DetectRegionIP>False</DetectRegionIP>
        <ServerName>Offline DAoC: Classic SI</ServerName>
        <ServerNameShort>OfflineClassicSI</ServerNameShort>
        <LogConfigFile>./config/logconfig.xml</LogConfigFile>
        <ScriptCompilationTarget>./lib/GameServerScripts.dll</ScriptCompilationTarget>
        <ScriptAssemblies> </ScriptAssemblies>
        <EnableCompilation>True</EnableCompilation>
        <AutoAccountCreation>True</AutoAccountCreation>
        <GameType>Normal</GameType>
        <CheatLoggerName>cheats</CheatLoggerName>
        <GMActionLoggerName>gmactions</GMActionLoggerName>
        <InvalidNamesFile>./config/invalidnames.txt</InvalidNamesFile>
        <DBType>SQLITE</DBType>
        <DBConnectionString>Data Source=../data/opendaoc.sqlite3.db;Version=3;Pooling=True;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60</DBConnectionString>
        <DBAutosave>True</DBAutosave>
        <DBAutosaveInterval>10</DBAutosaveInterval>
        <MetricsEnabled>false</MetricsEnabled>
    </Server>
</root>
EOF

install -m 0755 "$HERE/offlinedaoc" "$OUT/linux/offlinedaoc"
if [ -n "$DOTNET_DIR" ]; then
  [ -x "$DOTNET_DIR/dotnet" ] || { echo "No dotnet in $DOTNET_DIR"; exit 1; }
  cp -a "$DOTNET_DIR" "$OUT/linux/dotnet"
fi
echo "Linux prototype folder: $OUT"
