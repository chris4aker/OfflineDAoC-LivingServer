#!/usr/bin/env bash
# Adds Offline DAoC to the GNOME app grid (and other freedesktop menus) with its launcher icon (linux/icons: an original
# sword-and-shield drawing, no game art). Everything goes in your home folder; nothing needs root.
#
#   linux/install-desktop-entry.sh [game folder]      add (or refresh) the entry
#   linux/install-desktop-entry.sh --uninstall        remove the entry and its icons
#
# The game folder defaults to $ODAOC_GAME, then ~/OfflineDAoC/game, as for start-offline-daoc.sh. The entry runs this
# clone's start-offline-daoc.sh with that folder, so run it again if you move the clone or the game folder. A
# WINEPREFIX set when you run this is kept in the entry; otherwise the start script's own default applies.
set -euo pipefail
# A set CDPATH makes cd go somewhere else for a relative folder and print it, which would end up in the paths below.
unset CDPATH

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ID="offline-daoc-living-server"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
APPS="$DATA/applications"
ICONS="$DATA/icons/hicolor"

# GNOME finds icons in your home folder without an icon cache, and a cache made here goes stale when other apps add
# icons without updating it. So an existing cache is kept current, but none is created.
refresh() {
  if [[ -f "$ICONS/icon-theme.cache" ]] && command -v gtk-update-icon-cache >/dev/null; then
    gtk-update-icon-cache -q -t "$ICONS" 2>/dev/null || true
  fi
  command -v update-desktop-database >/dev/null && update-desktop-database -q "$APPS" 2>/dev/null || true
}

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -f "$APPS/$ID.desktop"
  for icon in "$HERE"/icons/hicolor/*/apps/"$ID".*; do
    size="$(basename "$(dirname "$(dirname "$icon")")")"
    rm -f "$ICONS/$size/apps/$(basename "$icon")"
  done
  refresh
  echo "Removed Offline DAoC from the app menu."
  exit 0
fi

GAME="${1:-${ODAOC_GAME:-$HOME/OfflineDAoC/game}}"
GAME="$(cd "$GAME" 2>/dev/null && pwd)" || { echo "No game folder at ${1:-${ODAOC_GAME:-$HOME/OfflineDAoC/game}}" >&2; exit 1; }
[[ -f "$GAME/runtime/OfflineDAoC.exe" ]] || { echo "$GAME has no runtime/OfflineDAoC.exe: is it the game folder?" >&2; exit 1; }

# A newline in a value would start a new line (a new key) in the entry, so paths with control characters are refused.
plain() { [[ "$2" != *[[:cntrl:]]* ]] || { echo "The $1 has a control character (such as a newline) in it: $(printf '%q' "$2")" >&2; exit 1; }; }
plain "script folder" "$HERE"
plain "game folder" "$GAME"
plain "WINEPREFIX" "${WINEPREFIX:-}"

# Desktop-entry Exec quoting: each argument in double quotes, with \ " ` $ escaped by a backslash.
quote() { local s="$1"; s="${s//\\/\\\\}"; s="${s//\"/\\\"}"; s="${s//\`/\\\`}"; s="${s//\$/\\\$}"; printf '"%s"' "$s"; }
# Exec also expands %-codes, so a literal % is written %%. The file's own string escaping is undone before that
# quoting, so every backslash is then written twice.
exec_arg() { local q bs='\'; q="$(quote "$1")"; q="${q//%/%%}"; printf '%s' "${q//"$bs"/"$bs$bs"}"; }

START="$(exec_arg "$HERE/start-offline-daoc.sh")"
if [[ -n "${WINEPREFIX:-}" ]]; then
  START="env $(exec_arg "WINEPREFIX=$WINEPREFIX") $START"
fi

mkdir -p "$APPS"
tmp="$(mktemp --suffix=.desktop "$APPS/.$ID.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
# Path= is a plain string: no quoting, but the file's string escaping still applies, so its backslashes are doubled.
# Comment lines of the template are dropped. The replacements are quoted so bash keeps their backslashes as they are.
GAME_ARG="$(exec_arg "$GAME")"
bs='\'
GAME_PATH="${GAME//"$bs"/"$bs$bs"}"
while IFS= read -r line; do
  [[ "$line" == \#* ]] && continue
  line="${line//@START@/"$START"}"
  if [[ "$line" == Exec=* ]]; then line="${line//@GAME@/"$GAME_ARG"}"; else line="${line//@GAME@/"$GAME_PATH"}"; fi
  printf '%s\n' "$line"
done <"$HERE/$ID.desktop" >"$tmp"
chmod 0644 "$tmp"
if command -v desktop-file-validate >/dev/null; then desktop-file-validate "$tmp"; fi
# The icons go in only once the entry is known to be good, so a failed run leaves nothing half installed.
for icon in "$HERE"/icons/hicolor/*/apps/"$ID".*; do
  size="$(basename "$(dirname "$(dirname "$icon")")")"
  install -D -m 0644 "$icon" "$ICONS/$size/apps/$(basename "$icon")"
done
mv -f "$tmp" "$APPS/$ID.desktop"
trap - EXIT
refresh

echo "Added Offline DAoC to the app menu ($APPS/$ID.desktop)."
echo "It starts $GAME. Press the Super key and type \"Offline DAoC\"; right-click it to add it to the dock."
