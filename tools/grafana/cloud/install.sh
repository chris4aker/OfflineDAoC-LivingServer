#!/usr/bin/env bash
# Installs this kit's own Alloy as a systemd *user* service on Ubuntu/Linux (issue #649). No sudo, nothing under /etc,
# and any Alloy or Grafana Agent already on the PC is left alone (paths B and D run side by side).
#
#   tools/grafana/cloud/install.sh [--port 12347] [--home DIR] [--no-start] [--dry-run]
#
# Steps: download the pinned Alloy release and check its SHA256 (pins.json), copy the configs, create monitoring.env
# from the example if it is missing (mode 600; the player fills it in), write ~/.config/systemd/user/livingserver-alloy.service
# and, once the env file is complete, enable and start it. Run detect.sh first; the runbook says when.
set -euo pipefail
KIT="$(cd "$(dirname "$0")" && pwd)"
ADDON_HOME="${LS_ADDON_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/livingserver-monitoring}"
UNIT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
UNIT_NAME="livingserver-alloy.service"
port=12347; port_given=0; start=1; dry=0
while [ $# -gt 0 ]; do
  case "$1" in
    --port) port="$2"; port_given=1; shift 2 ;;
    --home) ADDON_HOME="$2"; shift 2 ;;
    --no-start) start=0; shift ;;
    --dry-run) dry=1; shift ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$port" in ''|*[!0-9]*) echo "--port must be a number" >&2; exit 2 ;; esac

say() { echo "[install] $*"; }
run() { if [ "$dry" = 1 ]; then echo "[dry-run] $*"; else "$@"; fi; }

case "$(uname -m)" in
  x86_64|amd64) arch=linux-amd64 ;;
  aarch64|arm64) arch=linux-arm64 ;;
  *) echo "unsupported CPU: $(uname -m)" >&2; exit 1 ;;
esac
pin() { python3 -c 'import json,sys; p=json.load(open(sys.argv[1]))["alloy"]; print(p["version"] if sys.argv[2]=="version" else p[sys.argv[3]][sys.argv[2]])' "$KIT/pins.json" "$1" "$arch"; }
version=$(pin version); file=$(pin file); url=$(pin url); sha=$(pin sha256); exe=$(pin exe)

BIN_DIR="$ADDON_HOME/bin"; CONF_DIR="$ADDON_HOME/config"; DATA_DIR="$ADDON_HOME/data"; CACHE="$ADDON_HOME/downloads"
ENV_FILE="$ADDON_HOME/monitoring.env"
say "Alloy $version ($arch) into $ADDON_HOME, UI on 127.0.0.1:$port"

# 1. Download and verify
run mkdir -p "$BIN_DIR" "$CONF_DIR" "$DATA_DIR" "$CACHE"
if [ -x "$BIN_DIR/alloy" ] && "$BIN_DIR/alloy" --version 2>/dev/null | grep -q "v$version"; then
  say "Alloy $version already in $BIN_DIR"
else
  if [ ! -f "$CACHE/$file" ] || ! echo "$sha  $CACHE/$file" | sha256sum -c --status 2>/dev/null; then
    say "downloading $url"
    run curl -fsSL --retry 3 -o "$CACHE/$file.part" "$url"
    run mv "$CACHE/$file.part" "$CACHE/$file"
  fi
  if [ "$dry" = 0 ]; then
    echo "$sha  $CACHE/$file" | sha256sum -c --status || { echo "SHA256 mismatch for $file: refusing it" >&2; rm -f "$CACHE/$file"; exit 1; }
    say "SHA256 ok"
    tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
    python3 -c 'import sys,zipfile; zipfile.ZipFile(sys.argv[1]).extract(sys.argv[2], sys.argv[3])' "$CACHE/$file" "$exe" "$tmp"
    install -m 0755 "$tmp/$exe" "$BIN_DIR/alloy"
  else
    echo "[dry-run] verify sha256 $sha, extract $exe to $BIN_DIR/alloy"
  fi
fi

# 2. Configs (replaced on every run; they hold no secrets)
run rm -f "$CONF_DIR"/*.alloy
run cp "$KIT/alloy/common/"*.alloy "$KIT/alloy/linux/"*.alloy "$CONF_DIR/"
run install -m 0755 "$KIT/run-alloy.sh" "$ADDON_HOME/run-alloy.sh"

# 3. Env file: created from the example once, never overwritten
if [ ! -f "$ENV_FILE" ]; then
  run install -m 0600 "$KIT/monitoring.env.example" "$ENV_FILE"
  say "created $ENV_FILE (mode 600): the player fills it in (runbook step U2)"
fi
# The Alloy address: added once; changed only when --port is given (a rerun keeps the port it had).
if [ "$dry" = 0 ]; then
  if ! grep -q '^LS_ALLOY_ADDR=' "$ENV_FILE"; then
    printf '\nLS_ALLOY_ADDR=127.0.0.1:%s\n' "$port" >> "$ENV_FILE"
  elif [ "$port_given" = 1 ]; then
    sed -i "s/^LS_ALLOY_ADDR=.*/LS_ALLOY_ADDR=127.0.0.1:$port/" "$ENV_FILE"
  fi
fi

# 4. systemd user unit
unit="[Unit]
Description=Living Server monitoring (Alloy -> Grafana Cloud)
After=network-online.target

[Service]
Type=simple
WorkingDirectory=$ADDON_HOME
EnvironmentFile=$ENV_FILE
ExecStart=/bin/bash \"$ADDON_HOME/run-alloy.sh\"
Restart=on-failure
RestartSec=30

[Install]
WantedBy=default.target"
if [ "$dry" = 1 ]; then echo "[dry-run] write $UNIT_DIR/$UNIT_NAME:"; echo "$unit"; else
  mkdir -p "$UNIT_DIR"; printf '%s\n' "$unit" > "$UNIT_DIR/$UNIT_NAME"; fi
run systemctl --user daemon-reload

# 5. Start only with a complete env file
if [ "$start" = 1 ]; then
  if [ "$dry" = 0 ] && ! "$KIT/detect.sh" --check-env "$ENV_FILE"; then
    say "not started: fill in $ENV_FILE, then run this install again"
    exit 0
  fi
  run systemctl --user enable "$UNIT_NAME"
  run systemctl --user restart "$UNIT_NAME"   # a rerun picks up the new configs and binary
  say "started. It runs while you are logged in. To keep it running after logout: loginctl enable-linger \"${USER:-$(id -un)}\" (optional)."
fi
say "done. Undo: tools/grafana/cloud/uninstall.sh"
