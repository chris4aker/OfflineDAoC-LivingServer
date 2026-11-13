#!/usr/bin/env bash
# Offline DAoC 0.37 with Living Server on Linux: the player's setup (docs/LINUX.md §0, the Wine route).
# Built from Ryan's setup-offline-daoc.sh (tools/linux/wine/scripts, GPL-3.0 with his permission), cut down to what a
# player needs. The Windows game runs under Wine; nothing is built from source.
#
#   ./setup.sh                    every step in order, asking before each one
#   ./setup.sh <step>...          only the named steps
#   ./setup.sh list               the steps
#
# Steps: check  dotnet  game  prefix  living  desktop
# Settings (environment variables):
#   OFFLINEDAOC_GAME   the game folder                          (default ~/Games/OfflineDAoC)
#   EDITION            0.37b (Sluaghbinder class on) or 0.37    (default 0.37b)
#   WINE_VERBS         winetricks verbs for the Wine folder     (default "corefonts vcrun2019 d3dx9")
#                      Ryan's working desktop also has dotnet35 and dxvk; add them if the game needs them there.
#                      DXVK is your choice in your own Wine folder; Living Server never ships it.
#   GITHUB_TOKEN       optional, only if GitHub's rate limit (60 requests an hour) gets in the way
#
# Downloads come only from their publishers: the game from shadowofze's GitHub releases (checked against his
# download-manifest.json), .NET from Microsoft (SHA-512 from its release list), winetricks' own downloads, and Living
# Server from its GitHub release (GitHub's SHA-256 digest). Nothing here runs sudo; missing system packages are named.
set -Eeuo pipefail

GAME=${OFFLINEDAOC_GAME:-$HOME/Games/OfflineDAoC}
EDITION=${EDITION:-0.37b}
WINE_VERBS=${WINE_VERBS:-corefonts vcrun2019 d3dx9}
HERE="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
HELPER="$HERE/wine/scripts/odaoc_helper.py"
[ -f "$HELPER" ] || HELPER="$HERE/odaoc_helper.py"
LINUX="$GAME/linux"
DL="$LINUX/downloads"
META="$DL/meta"
LOGS="$LINUX/logs"
export WINEPREFIX="$LINUX/wine" WINEARCH=win64
GAME_REPO="shadowofze/OfflineDAoC"
LS_REPO="chris4aker/OfflineDAoC-LivingServer"

case "$EDITION" in
  0.37b) BASE_TAG=v0.36b UPDATE_TAG=v0.37b ;;
  0.37) BASE_TAG=v0.36 UPDATE_TAG=v0.37 ;;
  *) echo "EDITION must be 0.37b or 0.37" >&2; exit 2 ;;
esac

bold=$'\e[1m'; red=$'\e[31m'; grn=$'\e[32m'; yel=$'\e[33m'; off=$'\e[0m'
say() { printf '%s\n' "$*"; }
info() { printf '%s==>%s %s\n' "$bold" "$off" "$*"; }
ok() { printf '%s  ok%s %s\n' "$grn" "$off" "$*"; }
warn() { printf '%swarning:%s %s\n' "$yel" "$off" "$*" >&2; }
die() { printf '%serror:%s %s\n' "$red" "$off" "$*" >&2; exit 1; }
have() { command -v "$1" > /dev/null 2>&1; }
py() { python3 -I "$HELPER" "$@"; }
confirm() { local r; read -r -p "$1 [y/N] " r || return 1; [[ $r =~ ^[Yy] ]]; }
human() { awk -v b="${1:-0}" 'BEGIN{split("B KB MB GB TB",u," ");i=1;while(b>=1000&&i<5){b/=1000;i++}printf(i==1?"%d %s":"%.1f %s"),b,u[i]}'; }
free_bytes() { df -B1 --output=avail "$1" | tail -1 | tr -d ' '; }
need_space() { local a; a=$(free_bytes "$1"); (( a >= $2 )) || die "not enough disk space in $1: need about $(human "$2"), $(human "$a") free"; }

gh_api() { # gh_api <path> <outfile>
  local auth=()
  [ -n "${GITHUB_TOKEN:-}" ] && auth=(-H "Authorization: Bearer $GITHUB_TOKEN")
  curl -fsSL --retry 3 -H "Accept: application/vnd.github+json" "${auth[@]}" -o "$2.tmp" "https://api.github.com/$1" ||
    die "GitHub API request failed: $1 (rate limit? set GITHUB_TOKEN and retry)"
  mv "$2.tmp" "$2"
}

download() { # download <url> <out> <bytes> [sha256]  (Ryan's: resumes, checks size and hash)
  local url=$1 out=$2 bytes=$3 sha=${4:-}
  if [ -f "$out" ] && [ "$(stat -c %s "$out")" = "$bytes" ]; then
    if [ -z "$sha" ] || sha256sum "$out" | grep -qi "^$sha "; then say "  have $(basename "$out")"; return 0; fi
    warn "$(basename "$out") failed its hash check; downloading it again"; rm -f "$out"
  fi
  say "  get  $(basename "$out")  ($(human "$bytes"))"
  curl -fL --retry 5 --retry-delay 5 -C - --progress-bar -o "$out.part" "$url"
  [ "$(stat -c %s "$out.part")" = "$bytes" ] || die "$(basename "$out"): size mismatch after download"
  if [ -n "$sha" ] && ! sha256sum "$out.part" | grep -qi "^$sha "; then rm -f "$out.part"; die "$(basename "$out"): SHA-256 mismatch; run the step again"; fi
  mv "$out.part" "$out"
}

# The release's files that a manifest key lists (Parts, GamePatch), downloaded into downloads/<tag>/ and checked.
fetch_parts() { # fetch_parts <manifest> <key>
  local m=$1 key=$2 rel name bytes sha meta url
  while IFS=$'\t' read -r rel name bytes sha; do
    [ -n "$name" ] || continue
    meta="$META/release-v$rel.json"
    [ -f "$meta" ] || gh_api "repos/$GAME_REPO/releases/tags/v$rel" "$meta"
    url=$(py release-assets "$meta" | awk -F'\t' -v n="$name" '$1 == n { print $4 }')
    [ -n "$url" ] || die "release v$rel has no $name"
    mkdir -p "$DL/v$rel"
    download "$url" "$DL/v$rel/$name" "$bytes" "$sha"
  done < <(py manifest-parts "$m" "$key")
}

manifest_of() { # manifest_of <tag> -> path of its download-manifest.json
  local tag=$1 meta="$META/release-$1.json" url
  mkdir -p "$META" "$DL/$tag"
  gh_api "repos/$GAME_REPO/releases/tags/$tag" "$meta"
  url=$(py release-assets "$meta" | awk -F'\t' '$1 == "download-manifest.json" { print $4 }')
  [ -n "$url" ] || die "$GAME_REPO $tag has no download-manifest.json"
  curl -fsSL --retry 3 -o "$DL/$tag/download-manifest.json" "$url"
  echo "$DL/$tag/download-manifest.json"
}

step_check() {
  info "Check what the game needs on this computer"
  local missing=() t
  for t in wine winepath curl unzip python3 sha256sum; do have "$t" || missing+=("$t"); done
  if ! have pwsh; then warn "PowerShell 7 is missing: Living Server's installer needs it (Ubuntu: sudo snap install powershell --classic)"; fi
  if ! python3 -c 'import ctypes.util, sys; sys.exit(0 if ctypes.util.find_library("X11") else 1)' 2> /dev/null; then
    warn "libX11 not found: the fullscreen helper won't run (the game still does)"
  fi
  if [ ${#missing[@]} -gt 0 ]; then
    say "  Missing: ${missing[*]}"
    say "  Ubuntu or Debian (run these yourself):"
    say "    sudo dpkg --add-architecture i386 && sudo apt update"
    say "    sudo apt install wine64 wine32:i386 winbind cabextract curl unzip python3"
    say "  Fedora: sudo dnf install wine cabextract curl unzip python3    Arch: sudo pacman -S wine cabextract curl unzip python"
    die "install the missing tools, then run this step again"
  fi
  ok "$(wine --version), python3, curl, unzip$(have pwsh && echo ", pwsh $(pwsh --version | cut -d' ' -f2)")"
}

step_dotnet() {
  info "Microsoft's .NET runtime for Linux (shadowofze's update tool runs on it): into linux/dotnet"
  if [ -x "$LINUX/dotnet/dotnet" ]; then ok ".NET is there ($("$LINUX/dotnet/dotnet" --list-runtimes | tail -1))"; return 0; fi
  mkdir -p "$DL"
  curl -fsSL --retry 3 -o "$DL/dotnet-10.0-releases.json" "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"
  local ver url sha tarball
  IFS=$'\t' read -r ver url sha < <(py dotnet-runtime "$DL/dotnet-10.0-releases.json" linux-x64)
  tarball="$DL/$(basename "$url")"
  say "  .NET runtime $ver: about 30 MB"
  confirm "  Download it?" || return 0
  if [ ! -f "$tarball" ] || ! sha512sum "$tarball" | grep -qi "^$sha "; then
    curl -fL --retry 5 -C - --progress-bar -o "$tarball.part" "$url"
    sha512sum "$tarball.part" | grep -qi "^$sha " || { rm -f "$tarball.part"; die ".NET SHA-512 mismatch"; }
    mv "$tarball.part" "$tarball"
  fi
  mkdir -p "$LINUX/dotnet" && tar -xzf "$tarball" -C "$LINUX/dotnet"
  ok ".NET $ver in linux/dotnet"
}

step_game() {
  info "Offline DAoC $EDITION from shadowofze's releases ($BASE_TAG, then the $UPDATE_TAG update) into $GAME"
  if [ -f "$GAME/runtime/OfflineDAoC.exe" ]; then ok "a game is already there; skipped"; return 0; fi
  [ -x "$LINUX/dotnet/dotnet" ] || die "run the dotnet step first: the update is applied by shadowofze's own .NET tool"
  local mbase mupd parts=() rel name bytes sha unpacked total=0
  mbase=$(manifest_of "$BASE_TAG"); mupd=$(manifest_of "$UPDATE_TAG")
  while IFS=$'\t' read -r rel name bytes sha; do total=$((total + bytes)); done < <(py manifest-parts "$mbase"; py manifest-parts "$mupd" GamePatch)
  unpacked=$(py manifest-field "$mbase" ArchiveBytes)
  say "  Downloads $(human "$total") (kept in linux/downloads so a reinstall needs no network) and unpacks about $(human "$unpacked")."
  need_space "$(dirname "$GAME")" $((total + unpacked + 1000000000))
  confirm "  Download and install the game?" || return 0
  fetch_parts "$mbase" Parts
  fetch_parts "$mupd" GamePatch
  while IFS=$'\t' read -r rel name bytes sha; do parts+=("$DL/v$rel/$name"); done < <(py manifest-parts "$mbase")
  info "Checking the whole $BASE_TAG archive"
  [ "$(py sha256-concat "${parts[@]}")" = "$(py manifest-field "$mbase" ArchiveSHA256 | tr 'A-F' 'a-f')" ] || die "$BASE_TAG archive hash mismatch: delete linux/downloads/$BASE_TAG and run again"
  info "Unpacking $BASE_TAG"
  local stage="$GAME/.unpack"
  rm -rf "$stage" && mkdir -p "$stage"
  py extract-split "$stage" "OfflineDAoC-$BASE_TAG" "${parts[@]}"
  [ -f "$stage/runtime/OfflineDAoC.exe" ] || die "the unpacked game has no runtime/OfflineDAoC.exe"
  local e; for e in "$stage"/* "$stage"/.[!.]*; do [ -e "$e" ] && mv "$e" "$GAME/"; done; rmdir "$stage"
  info "Applying the $UPDATE_TAG update with shadowofze's patcher"
  local zips=() root tmp code
  while IFS=$'\t' read -r rel name bytes sha; do zips+=("$DL/v$rel/$name"); done < <(py manifest-parts "$mupd" GamePatch)
  root=$(py manifest-field "$mupd" GamePatch.RootFolder)
  tmp=$(mktemp -d "$LINUX/.patch-XXXXXX")
  py extract-zips "$tmp" "$root" "${zips[@]}"
  mkdir -p "$LOGS"
  set +e
  DOTNET_ROOT="$LINUX/dotnet" "$LINUX/dotnet/dotnet" "$tmp/patcher/OfflineDaoc.Patcher.dll" --game "$GAME" --patch "$tmp" 2>&1 | tee "$LOGS/game-update.log"
  code=${PIPESTATUS[0]}
  set -e
  rm -rf "$tmp"
  case $code in
    0|2) ok "Offline DAoC $EDITION is in $GAME" ;;
    *) die "the update stopped (exit $code; the patcher puts files back on failure). See $LOGS/game-update.log" ;;
  esac
}

step_prefix() {
  info "The game's own Wine folder (linux/wine) with: $WINE_VERBS"
  say "  Downloads those through winetricks (cached in linux/downloads/winetricks); some installers open windows."
  confirm "  Set it up?" || return 0
  mkdir -p "$LOGS" "$DL/winetricks"
  local wt="$DL/winetricks/winetricks"
  if [ ! -x "$wt" ]; then
    curl -fsSL --retry 3 -o "$wt.part" "https://raw.githubusercontent.com/Winetricks/winetricks/master/src/winetricks"
    head -1 "$wt.part" | grep -q '^#!' || die "that didn't look like the winetricks script"
    mv "$wt.part" "$wt" && chmod +x "$wt"
  fi
  [ -f "$WINEPREFIX/system.reg" ] || { WINEDLLOVERRIDES="mscoree,mshtml=" wineboot -i 2>&1 | tee "$LOGS/wineboot.log"; wineserver -w; }
  local verb
  for verb in $WINE_VERBS; do say "  winetricks $verb"; W_CACHE="$DL/winetricks" "$wt" -q "$verb" 2>&1 | tee "$LOGS/winetricks-$verb.log"; done
  wineserver -w
  ok "Wine folder ready: $WINEPREFIX"
}

step_living() {
  info "Living Server: the newest release from GitHub, installed with its own installer (PowerShell 7)"
  [ -f "$GAME/runtime/server/GameServer.dll" ] || die "run the game step first"
  have pwsh || die "PowerShell 7 is needed for Living Server's installer (Ubuntu: sudo snap install powershell --classic)"
  local meta="$META/living-latest.json" tag name url digest
  mkdir -p "$META" "$GAME/runtime/updates"
  gh_api "repos/$LS_REPO/releases/latest" "$meta"
  tag=$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["tag_name"])' "$meta")
  name="OfflineDAoC-LivingServer-${tag#v}.zip"
  IFS=$'\t' read -r url digest < <(py release-assets "$meta" | awk -F'\t' -v n="$name" '$1 == n { print $4 "\t" $3 }')
  [ -n "${url:-}" ] || die "Living Server $tag has no $name"
  [ "${digest:--}" != "-" ] || die "GitHub gave no SHA-256 for $name, so it can't be checked"
  local zip="$GAME/runtime/updates/$name" size
  size=$(py release-assets "$meta" | awk -F'\t' -v n="$name" '$1 == n { print $2 }')
  confirm "  Download Living Server $tag ($(human "$size")) and install it?" || return 0
  download "$url" "$zip" "$size" "$digest"
  local top; top=$(unzip -Z1 "$zip" | cut -d/ -f1 | sort -u)
  [[ $top == LivingServer-* ]] && [ "$(echo "$top" | wc -l)" -eq 1 ] || die "$name is not a Living Server package"
  rm -rf "$GAME/runtime/updates/$top" && unzip -q "$zip" -d "$GAME/runtime/updates"
  mkdir -p "$LINUX"
  if [ -f "$GAME/runtime/updates/$top/linux/offlinedaoc" ]; then
    install -m 0755 "$GAME/runtime/updates/$top/linux/offlinedaoc" "$LINUX/offlinedaoc"
    install -m 0644 "$GAME/runtime/updates/$top/linux/daoc_fullscreen.py" "$LINUX/daoc_fullscreen.py"
  else
    install -m 0755 "$HERE/offlinedaoc" "$LINUX/offlinedaoc"
    install -m 0644 "$HERE/wine/scripts/daoc_fullscreen.py" "$LINUX/daoc_fullscreen.py"
  fi
  OFFLINEDAOC_GAME="$GAME" "$LINUX/offlinedaoc" install "$zip"
  ok "Living Server $tag installed. Turn its features on in the launcher's Living Server tab (server stopped)."
}

step_desktop() {
  info "A desktop entry: Offline DAoC in your applications menu"
  local f="$HOME/.local/share/applications/offline-daoc.desktop"
  confirm "  Add $f?" || return 0
  mkdir -p "$(dirname "$f")"
  cat > "$f" << EOF
[Desktop Entry]
Type=Application
Name=Offline DAoC
Comment=Offline DAoC with Living Server (Wine)
Exec="$LINUX/offlinedaoc" play
Path=$GAME
Terminal=false
Categories=Game;
EOF
  ok "added: Offline DAoC (or run $LINUX/offlinedaoc play)"
}

STEPS=(check dotnet game prefix living desktop)
main() {
  if [ "${1:-}" = list ] || [ "${1:-}" = -h ] || [ "${1:-}" = --help ]; then sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'; exit 0; fi
  [ -f "$HELPER" ] || die "odaoc_helper.py must sit beside this script (or in wine/scripts/)"
  local todo=("$@") s
  [ ${#todo[@]} -gt 0 ] || todo=("${STEPS[@]}")
  for s in "${todo[@]}"; do [[ " ${STEPS[*]} " == *" $s "* ]] || die "unknown step: $s (steps: ${STEPS[*]})"; done
  mkdir -p "$GAME" "$LOGS"
  say "Game folder: $GAME (free: $(human "$(free_bytes "$GAME")")), edition $EDITION"
  for s in "${todo[@]}"; do say ""; "step_$s"; done
  say ""; ok "done: ${todo[*]}"
}
mkdir -p "$LOGS"
main "$@" 2>&1 | tee -a "$LOGS/setup-$(date +%Y%m%d).log"
exit "${PIPESTATUS[0]}"
