#!/usr/bin/env bash
# Read-only detect step of the Grafana Cloud runbook (issue #649), Ubuntu/Linux (native server or Wine).
# Prints one JSON object for the agent: the OS, the server's logs folder, any Alloy or Grafana Agent already on the PC,
# gcx, a free loopback port, the env file's state and the setup path (A-D). It changes nothing.
#
#   tools/grafana/cloud/detect.sh [--has-account yes|no] [--logs DIR] [--env FILE] [--home DIR]
#   tools/grafana/cloud/detect.sh --check-env FILE     # exit 0 when every required key is set; never prints values
set -uo pipefail

REQUIRED_KEYS=(GC_PROM_URL GC_PROM_USER GC_LOKI_URL GC_LOKI_USER GC_TOKEN LS_LOG_DIR LS_INSTANCE)
UNIT_NAME="livingserver-alloy.service"

has_account="unknown"; logs_arg=""; env_file=""; check_env_only=""
ADDON_HOME="${LS_ADDON_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/livingserver-monitoring}"
while [ $# -gt 0 ]; do
  case "$1" in
    --has-account) has_account="$2"; shift 2 ;;
    --logs) logs_arg="$2"; shift 2 ;;
    --env) env_file="$2"; shift 2 ;;
    --home) ADDON_HOME="$2"; shift 2 ;;
    --check-env) check_env_only="$2"; shift 2 ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$env_file" ] || env_file="$ADDON_HOME/monitoring.env"

json_str() {  # JSON string literal, or null for an empty value
  if [ -z "${1:-}" ]; then printf 'null'; return; fi
  local s=${1//\\/\\\\}; s=${s//\"/\\\"}; s=${s//$'\t'/\\t}; s=${s//$'\n'/\\n}; s=${s//$'\r'/}
  printf '"%s"' "$s"
}
json_bool() { if [ "$1" = 1 ]; then printf true; else printf false; fi; }
json_list() {  # JSON array of strings from the arguments
  local first=1; printf '['
  for v in "$@"; do [ $first = 1 ] || printf ','; first=0; json_str "$v"; done
  printf ']'
}
have() { command -v "$1" >/dev/null 2>&1; }

# Missing required keys of an env file (names only; values are never read out).
missing_env_keys() {
  local f="$1" k line val
  for k in "${REQUIRED_KEYS[@]}"; do
    line=$(grep -E "^[[:space:]]*${k}=" "$f" 2>/dev/null | tail -n 1)
    val=${line#*=}; val=${val%$'\r'}; val=${val#\"}; val=${val%\"}
    [ -n "$line" ] && [ -n "$val" ] || printf '%s\n' "$k"
  done
}

if [ -n "$check_env_only" ]; then
  if [ ! -f "$check_env_only" ]; then echo "env file not found: $check_env_only" >&2; exit 1; fi
  missing=$(missing_env_keys "$check_env_only")
  if [ -n "$missing" ]; then echo "missing or empty: $(echo $missing)" >&2; exit 1; fi
  perms=$(stat -c '%a' "$check_env_only" 2>/dev/null || echo "")
  case "$perms" in 600|400|"") ;; *) echo "warning: $check_env_only is mode $perms; run: chmod 600 \"$check_env_only\"" >&2 ;; esac
  echo "env ok: all ${#REQUIRED_KEYS[@]} keys set"
  exit 0
fi

# ---- OS ----
os_name=$(uname -s 2>/dev/null || echo unknown)
distro=""; [ -r /etc/os-release ] && distro=$(. /etc/os-release && echo "${PRETTY_NAME:-}")
wine=0; have wine && wine=1
systemd_user=0; have systemctl && systemctl --user show-environment >/dev/null 2>&1 && systemd_user=1

# ---- Logs folder ----
logs_found=()
if [ -n "$logs_arg" ]; then
  [ -f "$logs_arg/server.log" ] && logs_found+=("$logs_arg")
else
  shopt -s nullglob
  for d in "$HOME"/OfflineDAoC/runtime/server/logs "$HOME"/OfflineDAoC/*/runtime/server/logs \
           "$HOME"/OfflineDAoC/*/*/runtime/server/logs "$HOME"/Games/*/runtime/server/logs \
           "$HOME"/*/runtime/server/logs "$HOME"/.wine/drive_c/Games/*/runtime/server/logs \
           "$HOME"/.wine/drive_c/Games/*/*/runtime/server/logs; do
    [ -f "$d/server.log" ] && logs_found+=("$d")
  done
  shopt -u nullglob
fi

# ---- Time zone ----
tz=""
if have timedatectl; then tz=$(timedatectl show -p Timezone --value 2>/dev/null || true); fi
[ -z "$tz" ] && [ -r /etc/timezone ] && tz=$(head -n 1 /etc/timezone)
[ -z "$tz" ] && [ -L /etc/localtime ] && tz=$(readlink /etc/localtime | sed -n 's|.*zoneinfo/||p')

# ---- Alloy and Grafana Agent ----
active() { have systemctl && [ "$(systemctl "$@" 2>/dev/null)" = active ]; }
alloy_bin=""; have alloy && alloy_bin=$(command -v alloy)
alloy_version=""; [ -n "$alloy_bin" ] && alloy_version=$(alloy --version 2>/dev/null | head -n 1)
alloy_system=0; active is-active alloy.service && alloy_system=1
kit_installed=0; [ -f "${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/$UNIT_NAME" ] && kit_installed=1
kit_running=0; active --user is-active "$UNIT_NAME" && kit_running=1
alloy_procs=0; other_alloy_procs=0
if have pgrep; then
  alloy_procs=$(pgrep -c -f '(^|/)alloy[^ ]* run' 2>/dev/null || true); alloy_procs=${alloy_procs:-0}
  other_alloy_procs=$(pgrep -a -f '(^|/)alloy[^ ]* run' 2>/dev/null | grep -vc 'livingserver-monitoring' || true)
  other_alloy_procs=${other_alloy_procs:-0}
fi
agent_system=0; active is-active grafana-agent.service && agent_system=1
agent_proc=0; have pgrep && pgrep -f 'grafana-agent' >/dev/null 2>&1 && agent_proc=1
agent_installed=0; { have grafana-agent || [ -f /etc/grafana-agent.yaml ] || [ "$agent_system" = 1 ]; } && agent_installed=1

# Another collector (not this kit's) already on the PC: paths B and D run side by side with it.
existing_alloy=0
{ [ "$alloy_system" = 1 ] || [ "$other_alloy_procs" -gt 0 ] || [ -n "$alloy_bin" ]; } && existing_alloy=1
existing_collector=0; { [ "$existing_alloy" = 1 ] || [ "$agent_installed" = 1 ]; } && existing_collector=1

# ---- gcx ----
gcx=0; gcx_version=""; gcx_logged_in=0
if have gcx; then
  gcx=1; gcx_version=$(gcx --version 2>/dev/null | head -n 1)
  gcx config check >/dev/null 2>&1 && gcx_logged_in=1
fi

# ---- A free loopback port for this kit's Alloy UI ----
listening=""; have ss && listening=$(ss -ltnH 2>/dev/null | awk '{print $4}')
free_port=""
for p in $(seq 12347 12360); do
  if ! printf '%s\n' "$listening" | grep -Eq "[:.]$p\$"; then free_port=$p; break; fi
done

# ---- Env file ----
env_exists=0; missing=()
if [ -f "$env_file" ]; then env_exists=1; mapfile -t missing < <(missing_env_keys "$env_file"); else missing=("${REQUIRED_KEYS[@]}"); fi

# ---- Path ----
path=""; next="ask_has_account"
case "$has_account" in
  no)  if [ "$existing_collector" = 1 ]; then path=B; else path=A; fi ;;
  yes) if [ "$existing_collector" = 1 ]; then path=D; else path=C; fi ;;
esac
if [ -n "$path" ]; then
  if [ "$kit_running" = 1 ]; then next="verify"
  elif [ "${#logs_found[@]}" -eq 0 ]; then next="ask_logs_folder"
  elif [ "$env_exists" = 0 ] || [ "${#missing[@]}" -gt 0 ]; then next="user_credentials"
  else next="install"; fi
fi

printf '{\n'
printf '  "platform": "linux",\n'
printf '  "os": %s,\n' "$(json_str "$os_name${distro:+ ($distro)}")"
printf '  "wine": %s,\n' "$(json_bool $wine)"
printf '  "systemd_user": %s,\n' "$(json_bool $systemd_user)"
printf '  "logs_dirs": %s,\n' "$(json_list "${logs_found[@]+"${logs_found[@]}"}")"
printf '  "timezone": %s,\n' "$(json_str "$tz")"
printf '  "alloy": {"binary": %s, "version": %s, "system_service_active": %s, "other_processes": %s},\n' \
  "$(json_str "$alloy_bin")" "$(json_str "$alloy_version")" "$(json_bool $alloy_system)" "${other_alloy_procs}"
printf '  "grafana_agent": {"installed": %s, "service_active": %s, "running": %s},\n' \
  "$(json_bool $agent_installed)" "$(json_bool $agent_system)" "$(json_bool $agent_proc)"
printf '  "existing_collector": %s,\n' "$(json_bool $existing_collector)"
printf '  "kit": {"home": %s, "installed": %s, "running": %s},\n' \
  "$(json_str "$ADDON_HOME")" "$(json_bool $kit_installed)" "$(json_bool $kit_running)"
printf '  "gcx": {"installed": %s, "version": %s, "logged_in": %s},\n' \
  "$(json_bool $gcx)" "$(json_str "$gcx_version")" "$(json_bool $gcx_logged_in)"
printf '  "free_port": %s,\n' "${free_port:-null}"
printf '  "env": {"file": %s, "exists": %s, "missing_keys": %s},\n' \
  "$(json_str "$env_file")" "$(json_bool $env_exists)" "$(json_list "${missing[@]+"${missing[@]}"}")"
printf '  "has_account": %s,\n' "$(json_str "$has_account")"
printf '  "path": %s,\n' "$(json_str "$path")"
printf '  "next": %s\n' "$(json_str "$next")"
printf '}\n'
