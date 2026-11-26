#!/usr/bin/env bash
# Removes this kit's Alloy user service and its folder (issue #649). Keeps monitoring.env unless --purge.
# Touches nothing else: any other Alloy or Grafana Agent stays as it was.
#   tools/grafana/cloud/uninstall.sh [--home DIR] [--purge]
set -euo pipefail
ADDON_HOME="${LS_ADDON_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/livingserver-monitoring}"
UNIT="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/livingserver-alloy.service"
purge=0
while [ $# -gt 0 ]; do
  case "$1" in --home) ADDON_HOME="$2"; shift 2 ;; --purge) purge=1; shift ;; *) echo "unknown argument: $1" >&2; exit 2 ;; esac
done
systemctl --user disable --now livingserver-alloy.service 2>/dev/null || true
rm -f "$UNIT"; systemctl --user daemon-reload 2>/dev/null || true
if [ -d "$ADDON_HOME" ]; then
  if [ "$purge" = 1 ]; then rm -rf "$ADDON_HOME"
  else find "$ADDON_HOME" -mindepth 1 -maxdepth 1 ! -name monitoring.env -exec rm -rf {} +; fi
fi
echo "[uninstall] removed the service and $ADDON_HOME$([ $purge = 1 ] || echo ' (kept monitoring.env; --purge deletes it)')."
echo "[uninstall] In Grafana Cloud, revoke the access policy token and delete the gcx service account if you no longer use them."
