#!/usr/bin/env bash
# Offline DAoC + Living Server on Ubuntu: mirror everything locally, then install.
#
#   ./setup-offline-daoc.sh            run every step in order, asking before each one
#   ./setup-offline-daoc.sh <step>...  run only the named steps
#   ./setup-offline-daoc.sh list       show the steps
#
# Every step says what it will download or install, where it goes and roughly how big it is,
# then waits for y/N. Nothing runs sudo: system packages are printed for you to install yourself.
# Steps can be re-run safely; finished, verified downloads are skipped.
#
# Settings (environment variables):
#   ODAOC_ROOT      project folder                       (default: ~/OfflineDAoC)
#   LS_TAGS         Living Server releases to mirror     (default: "v1.1-beta v1.1.1-beta")
#   LS_CHECKOUT     Living Server tag to build from      (default: v1.1-beta)
#   GAME_RELEASES   Offline DAoC releases to mirror      (default: "v0.36 v0.36b v0.37 v0.37b v0.36c v0.37c")
#   NEW_CLIENT      also install the 0.37c new client in the game step: yes/no (default: no)
#   WINE_ARCH       win32 or win64 Wine prefix           (default: win64)
#   GITHUB_TOKEN    optional, only if GitHub's API rate limit (60 requests/hour) gets in the way

set -Eeuo pipefail

ROOT="${ODAOC_ROOT:-$HOME/OfflineDAoC}"
SCRIPTS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HELPER="$SCRIPTS/odaoc_helper.py"
LS_TAGS="${LS_TAGS:-v1.1-beta v1.1.1-beta}"
LS_CHECKOUT="${LS_CHECKOUT:-v1.1-beta}"
GAME_RELEASES="${GAME_RELEASES:-v0.36 v0.36b v0.37 v0.37b v0.36c v0.37c}"
NEW_CLIENT="${NEW_CLIENT:-no}"
WINE_ARCH="${WINE_ARCH:-win64}"   # the launcher, server and bundled .NET are 64-bit; only the client is 32-bit

ASSETS="$ROOT/assets"
ADDON="$ROOT/addon"
SOURCE="$ROOT/source"
MIRRORS="$SOURCE/mirrors"
TOOLS="$ROOT/tools"
GAME="$ROOT/game"
PREFIX="$ROOT/wineprefix"
LOGS="$ROOT/logs"
META="$ROOT/assets/.meta"

REPOS=(
  "chris4aker/OfflineDAoC-LivingServer"
  "shadowofze/OfflineDAoC"
  "OpenDAoC/OpenDAoC-Core"
  "OpenDAoC/OpenDAoC-Database"
)
WINETRICKS_VERBS=(corefonts vcrun2019 d3dx9 dotnet35 dxvk)

STEPS=(dirs packages assets addon git dotnet nuget winetricks wineprefix game)
declare -A STEP_HELP=(
  [dirs]="create the project folders"
  [packages]="check Ubuntu packages (Wine, cmake, ...) and print the sudo command for missing ones"
  [assets]="mirror the Offline DAoC game releases into assets/<tag>/"
  [addon]="mirror the Living Server releases into addon/<tag>/"
  [git]="git clone --mirror the four source repos, plus working copies"
  [dotnet]="install the .NET 10 SDK into tools/dotnet (tarball kept for offline reinstall)"
  [nuget]="restore the server's NuGet packages into tools/nuget-packages for offline builds"
  [winetricks]="save the latest winetricks script into tools/"
  [wineprefix]="create the Wine prefix and install ${WINETRICKS_VERBS[*]} (downloads cached in tools/)"
  [game]="install Offline DAoC 0.37b into game/ from the local mirror (no network)"
)

# ---------------------------------------------------------------- helpers
c_bold=$'\e[1m'; c_dim=$'\e[2m'; c_red=$'\e[31m'; c_grn=$'\e[32m'; c_yel=$'\e[33m'; c_off=$'\e[0m'
say()  { printf '%s\n' "$*"; }
info() { printf '%s==>%s %s\n' "$c_bold" "$c_off" "$*"; }
ok()   { printf '%s  ok%s %s\n' "$c_grn" "$c_off" "$*"; }
warn() { printf '%swarning:%s %s\n' "$c_yel" "$c_off" "$*" >&2; }
die()  { printf '%serror:%s %s\n' "$c_red" "$c_off" "$*" >&2; exit 1; }
trap 'die "failed at line $LINENO: $BASH_COMMAND (see above). Fix it and run the same step again."' ERR

human() { # bytes -> "1.2 GB"
  awk -v b="${1:-0}" 'BEGIN{ split("B KB MB GB TB",u," "); i=1; while (b>=1000 && i<5) {b/=1000; i++}
                             printf (i==1 ? "%d %s" : "%.1f %s"), b, u[i] }'
}

confirm() { # confirm "question" -> 0 for yes
  local reply
  read -r -p "$1 [y/N] " reply || return 1
  [[ $reply =~ ^[Yy] ]]
}

free_bytes() { df -B1 --output=avail "$1" | tail -1 | tr -d ' '; }

need_space() { # need_space <dir> <bytes>
  local avail; avail=$(free_bytes "$1")
  if (( avail < $2 )); then
    die "not enough disk space in $1: need about $(human "$2"), $(human "$avail") free"
  fi
}

have() { command -v "$1" >/dev/null 2>&1; }

py() { python3 -I "$HELPER" "$@"; }

gh_api() { # gh_api <path> <outfile>
  local auth=()
  [[ -n ${GITHUB_TOKEN:-} ]] && auth=(-H "Authorization: Bearer $GITHUB_TOKEN")
  curl -fsSL --retry 3 -H "Accept: application/vnd.github+json" "${auth[@]}" \
       -o "$2.tmp" "https://api.github.com/$1" \
    || die "GitHub API request failed: $1 (rate limit? set GITHUB_TOKEN and retry)"
  mv "$2.tmp" "$2"
}

download() { # download <url> <outfile> <bytes> [sha256]
  local url=$1 out=$2 bytes=$3 sha=${4:-}
  if [[ -f $out && $(stat -c %s "$out") == "$bytes" ]]; then
    if [[ -z $sha ]] || sha256sum "$out" | grep -qi "^$sha "; then
      say "  ${c_dim}have${c_off} $(basename "$out")"; return 0
    fi
    warn "$(basename "$out") failed its hash check; downloading it again"; rm -f "$out"
  fi
  say "  get  $(basename "$out")  ($(human "$bytes"))"
  curl -fL --retry 5 --retry-delay 5 -C - --progress-bar -o "$out.part" "$url"
  [[ $(stat -c %s "$out.part") == "$bytes" ]] || die "$(basename "$out"): size mismatch after download"
  if [[ -n $sha ]] && ! sha256sum "$out.part" | grep -qi "^$sha "; then
    rm -f "$out.part"; die "$(basename "$out"): SHA256 mismatch; run the step again"
  fi
  mv "$out.part" "$out"
}

# mirror_release <owner/repo> <tag> <destdir>
mirror_release() {
  local repo=$1 tag=$2 dest=$3
  local meta="$META/${repo//\//__}-$tag.json"
  mkdir -p "$dest" "$META"
  gh_api "repos/$repo/releases/tags/$tag" "$meta"
  local list total=0 have_bytes=0 n=0 name size sha url
  list=$(py release-assets "$meta")
  while IFS=$'\t' read -r name size sha url; do
    [[ -z $name ]] && continue
    n=$((n+1)); total=$((total+size))
    [[ -f $dest/$name && $(stat -c %s "$dest/$name") == "$size" ]] && have_bytes=$((have_bytes+size))
  done <<<"$list"
  local todo=$((total-have_bytes))
  say ""
  say "${c_bold}$repo $tag${c_off}: $n files, $(human "$total") total, $(human "$todo") still to download"
  say "  into $dest"
  if (( todo == 0 )); then
    say "  everything is already here; checking hashes"
  else
    need_space "$dest" $((todo + 1000000000))
    confirm "  Download $(human "$todo") from $repo release $tag?" || { say "  skipped $tag"; return 0; }
  fi
  while IFS=$'\t' read -r name size sha url; do
    [[ -z $name ]] && continue
    [[ $sha == - ]] && sha=""
    download "$url" "$dest/$name" "$size" "$sha"
  done <<<"$list"
  verify_manifest_parts "$dest"
  ok "$repo $tag mirrored"
}

# Offline DAoC's own download-manifest.json lists SHA256 for every game part: check them too.
verify_manifest_parts() {
  local m="$1/download-manifest.json" key rel name bytes sha bad=0
  [[ -f $m ]] || return 0
  for key in Parts GamePatch ClientPatch; do
    while IFS=$'\t' read -r rel name bytes sha; do
      [[ -z $name ]] && continue
      local f="$ASSETS/v$rel/$name"
      [[ -f $f ]] || continue     # that part lives in another release; checked when that one is mirrored
      if [[ $(stat -c %s "$f") != "$bytes" ]] || ! sha256sum "$f" | grep -qi "^$sha "; then
        warn "$f does not match its manifest"; bad=1
      fi
    done < <(py manifest-parts "$m" "$key" 2>/dev/null || true)
  done
  (( bad == 0 )) || die "some parts failed verification; delete them and run the step again"
}

env_file() {
  cat >"$TOOLS/env.sh" <<EOF
# source this before building or running: . "$TOOLS/env.sh"
export ODAOC_ROOT="$ROOT"
export DOTNET_ROOT="$TOOLS/dotnet"
export PATH="$TOOLS/bin:$TOOLS/dotnet:\$PATH"
export WINESERVER="\${WINESERVER:-/usr/lib/x86_64-linux-gnu/wine/wineserver}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$TOOLS/nuget-packages"
export WINEPREFIX="$PREFIX"
export WINEARCH="$WINE_ARCH"
export W_CACHE="$TOOLS/winetricks-cache"
EOF
}

# ---------------------------------------------------------------- steps
step_dirs() {
  info "Create the project folders in $ROOT"
  say "  assets addon source tools game wineprefix logs (no downloads)"
  confirm "  Create them?" || return 0
  mkdir -p "$ASSETS" "$META" "$ADDON" "$MIRRORS" "$TOOLS" "$GAME" "$LOGS"
  env_file
  ok "folders ready; settings written to tools/env.sh"
}

step_packages() {
  info "Check Ubuntu packages"
  local pkgs=(git curl unzip python3 cmake build-essential cabextract libicu-dev
              wine wine64 wine32:i386 winbind libvulkan1 libvulkan1:i386 vulkan-tools)
  local missing=() p
  for p in "${pkgs[@]}"; do
    dpkg-query -W -f='${Status}' "$p" 2>/dev/null | grep -q "install ok installed" || missing+=("$p")
  done
  if (( ${#missing[@]} == 0 )); then ok "all packages are installed"; return 0; fi
  say "  Missing: ${missing[*]}"
  say "  Wine with its 32-bit half is the big one (roughly 600 MB to 1 GB installed, depending on what you have)."
  say "  Run these yourself in another terminal:"
  say ""
  [[ " ${missing[*]} " == *":i386"* ]] && ! dpkg --print-foreign-architectures | grep -qx i386 \
    && say "    sudo dpkg --add-architecture i386"
  say "    sudo apt update"
  say "    sudo apt install ${missing[*]}"
  say ""
  read -r -p "  Press Enter when that has finished (or Ctrl+C to stop)... " _
  local still=()
  for p in "${missing[@]}"; do
    dpkg-query -W -f='${Status}' "$p" 2>/dev/null | grep -q "install ok installed" || still+=("$p")
  done
  (( ${#still[@]} == 0 )) && ok "all packages are installed" || warn "still missing: ${still[*]}"
}

step_assets() {
  info "Mirror the Offline DAoC releases: $GAME_RELEASES"
  say "  Each release is asked separately. For the original client you need v0.36b (5.1 GB, the base game)"
  say "  and v0.37b (the 0.37 update, about 40 MB). v0.36c (about 30 GB) and v0.37c (about 1.7 GB) are"
  say "  only for the optional new Godot client. v0.36 and v0.37 are small (manifests and downloaders)."
  local tag
  for tag in $GAME_RELEASES; do
    mirror_release "shadowofze/OfflineDAoC" "$tag" "$ASSETS/$tag"
  done
}

step_addon() {
  info "Mirror the Living Server releases: $LS_TAGS"
  say "  Each is about 63 MB (the add-on zip plus its Windows Setup exe)."
  say "  v1.1.1-beta is a bug-fix release from 2026-10-31 (bots stuck following you, launcher stuck on STARTING, ...)."
  local tag
  for tag in $LS_TAGS; do
    mirror_release "chris4aker/OfflineDAoC-LivingServer" "$tag" "$ADDON/$tag"
  done
}

step_git() {
  info "Mirror the source repositories (full history, all branches and tags) into source/mirrors/"
  mkdir -p "$MIRRORS" "$META"
  local repo name meta size
  for repo in "${REPOS[@]}"; do
    name=${repo#*/}; meta="$META/repo-${repo//\//__}.json"
    gh_api "repos/$repo" "$meta"
    size=$(py repo-size "$meta")
    say ""
    if [[ -d $MIRRORS/$name.git ]]; then
      say "${c_bold}$repo${c_off}: already mirrored; fetching updates (usually small)"
      confirm "  Fetch updates for $repo?" || continue
      git -C "$MIRRORS/$name.git" remote update --prune
    else
      say "${c_bold}$repo${c_off}: about $(human "$size") (GitHub's estimate) into source/mirrors/$name.git"
      confirm "  Clone $repo?" || continue
      git clone --mirror "https://github.com/$repo.git" "$MIRRORS/$name.git"
    fi
    if [[ ! -d $SOURCE/$name ]]; then
      git clone -q "$MIRRORS/$name.git" "$SOURCE/$name"
    fi
    ok "$repo mirrored; working copy in source/$name"
  done
  if [[ -d $SOURCE/OfflineDAoC-LivingServer ]]; then
    git -C "$SOURCE/OfflineDAoC-LivingServer" -c advice.detachedHead=false checkout -q "$LS_CHECKOUT"
    ok "source/OfflineDAoC-LivingServer is at $LS_CHECKOUT (the server is built from this)"
  fi
}

step_dotnet() {
  info "Install the .NET 10 SDK into tools/dotnet"
  mkdir -p "$TOOLS/downloads"
  local rel="$TOOLS/downloads/dotnet-10.0-releases.json" ver url sha tarball
  curl -fsSL --retry 3 -o "$rel" "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"
  IFS=$'\t' read -r ver url sha < <(py dotnet-sdk "$rel" linux-x64)
  tarball="$TOOLS/downloads/$(basename "$url")"
  [[ $tarball == *"$ver"* ]] || tarball="$TOOLS/downloads/dotnet-sdk-$ver-linux-x64.tar.gz"
  if [[ -x $TOOLS/dotnet/dotnet ]] && "$TOOLS/dotnet/dotnet" --list-sdks 2>/dev/null | grep -q "^$ver "; then
    ok ".NET SDK $ver is already installed"; return 0
  fi
  say "  .NET SDK $ver for linux-x64: about 230 MB download, about 800 MB installed"
  say "  The tarball is kept in tools/downloads/ so it can be reinstalled offline."
  if [[ ! -f $tarball ]]; then
    confirm "  Download and install it?" || return 0
    curl -fL --retry 5 -C - --progress-bar -o "$tarball.part" "$url"
    sha512sum "$tarball.part" | grep -qi "^$sha " || { rm -f "$tarball.part"; die "SDK SHA512 mismatch"; }
    mv "$tarball.part" "$tarball"
  else
    confirm "  Install it from the saved tarball (no download)?" || return 0
  fi
  mkdir -p "$TOOLS/dotnet"
  tar -xzf "$tarball" -C "$TOOLS/dotnet"
  env_file
  "$TOOLS/dotnet/dotnet" --list-sdks
  ok ".NET SDK $ver installed"
}

step_nuget() {
  info "Restore the server's NuGet packages into tools/nuget-packages (so later builds work offline)"
  local sln="$SOURCE/OfflineDAoC-LivingServer/source/server/DOLLinux.sln"
  [[ -f $sln ]] || die "run the git step first ($sln is missing)"
  [[ -x $TOOLS/dotnet/dotnet ]] || die "run the dotnet step first"
  say "  Restores $sln"
  say "  Roughly 300-800 MB of packages from nuget.org."
  confirm "  Restore now?" || return 0
  # shellcheck disable=SC1091
  . "$TOOLS/env.sh"
  mkdir -p "$NUGET_PACKAGES"
  dotnet restore "$sln" 2>&1 | tee "$LOGS/nuget-restore.log"
  ok "packages in $NUGET_PACKAGES ($(du -sh "$NUGET_PACKAGES" | cut -f1)); build later with --no-restore"
}

step_winetricks() {
  info "Save the latest winetricks script into tools/winetricks (about 1 MB)"
  say "  Ubuntu's packaged winetricks is often too old for dotnet35/dxvk."
  confirm "  Download it?" || return 0
  curl -fsSL --retry 3 -o "$TOOLS/winetricks.part" \
    "https://raw.githubusercontent.com/Winetricks/winetricks/master/src/winetricks"
  head -1 "$TOOLS/winetricks.part" | grep -q '^#!' || die "that did not look like the winetricks script"
  mv "$TOOLS/winetricks.part" "$TOOLS/winetricks"; chmod +x "$TOOLS/winetricks"
  ok "winetricks $("$TOOLS/winetricks" --version 2>/dev/null | head -1)"
}

step_wineprefix() {
  info "Create the Wine prefix ($WINE_ARCH) in wineprefix/ and install: ${WINETRICKS_VERBS[*]}"
  have wine || die "Wine is not installed; run the packages step"
  [[ -x $TOOLS/winetricks ]] || die "run the winetricks step first"
  say "  Downloads about 400 MB (fonts, Visual C++ 2019, DirectX 9 helpers, .NET 3.5, DXVK),"
  say "  cached in tools/winetricks-cache/ so the prefix can be rebuilt offline. Prefix ends up around 1.5 GB."
  say "  Some installers open windows; .NET 3.5 can take 10+ minutes."
  confirm "  Set up the prefix?" || return 0
  env_file
  # shellcheck disable=SC1091
  . "$TOOLS/env.sh"
  mkdir -p "$W_CACHE"
  if [[ ! -f $PREFIX/system.reg ]]; then
    WINEDLLOVERRIDES="mscoree,mshtml=" wineboot -i 2>&1 | tee "$LOGS/wineboot.log"
    wineserver -w
  fi
  local verb
  for verb in "${WINETRICKS_VERBS[@]}"; do
    say "  winetricks $verb"
    "$TOOLS/winetricks" -q "$verb" 2>&1 | tee "$LOGS/winetricks-$verb.log"
  done
  wineserver -w
  ok "Wine prefix ready in $PREFIX"
}

step_game() {
  info "Install Offline DAoC 0.37b into game/ from the local mirror (no network)"
  local base="$ASSETS/v0.36b" upd="$ASSETS/v0.37b" m36b m37b
  m36b="$base/download-manifest.json"; m37b="$upd/download-manifest.json"
  [[ -f $m36b && -f $m37b ]] || die "mirror v0.36b and v0.37b first (assets step)"
  [[ -x $TOOLS/dotnet/dotnet ]] || die "run the dotnet step first: the 0.37 update is applied by a .NET 10 program"
  if [[ -e $GAME/runtime/OfflineDAoC.exe ]]; then
    warn "a game is already installed in $GAME; move it aside to reinstall"; return 0
  fi
  [[ -z $(ls -A "$GAME" 2>/dev/null) ]] || die "$GAME is not empty; move its contents aside first"

  local parts=() rel name bytes sha unpacked
  while IFS=$'\t' read -r rel name bytes sha; do parts+=("$ASSETS/v$rel/$name"); done < <(py manifest-parts "$m36b")
  unpacked=$(py manifest-field "$m36b" ArchiveBytes)
  say "  1. Unpack the 0.36b base (${#parts[@]} parts) into $GAME: needs about $(human $((unpacked * 2))) free"
  say "  2. Apply the 0.37 update with its own patcher (backs up what it replaces)"
  [[ $NEW_CLIENT == yes ]] && say "  3. Unpack the 0.36c new client (about 34 GB) and apply the 0.37c client update"
  need_space "$ROOT" $((unpacked * 2))
  confirm "  Install now?" || return 0

  info "Checking the complete 0.36b archive hash"
  local want got
  want=$(py manifest-field "$m36b" ArchiveSHA256 | tr 'A-F' 'a-f')
  got=$(py sha256-concat "${parts[@]}")
  [[ $got == "$want" ]] || die "0.36b archive hash mismatch: re-run the assets step"
  ok "archive verified"
  info "Unpacking 0.36b"
  py extract-split "$GAME" "OfflineDAoC-v0.36b" "${parts[@]}"
  [[ -f $GAME/runtime/OfflineDAoC.exe ]] || die "the unpacked game has no runtime/OfflineDAoC.exe"

  apply_patch "$m37b" GamePatch "game update"

  if [[ $NEW_CLIENT == yes ]]; then
    local m36c="$ASSETS/v0.36c/download-manifest.json" cparts=()
    [[ -f $m36c ]] || die "mirror v0.36c and v0.37c for the new client"
    while IFS=$'\t' read -r rel name bytes sha; do cparts+=("$ASSETS/v$rel/$name"); done < <(py manifest-parts "$m36c")
    info "Unpacking the 0.36c new client"
    py extract-zips "$GAME" "OfflineDAoC-v0.36c" "${cparts[@]}"
    apply_patch "$m37b" ClientPatch "new client update"
  fi
  ok "Offline DAoC 0.37b is in $GAME"
  say "  Next: Living Server (INSTALL.cmd's job), the native server build and start-daoc.sh."
}

# apply_patch <manifest> <GamePatch|ClientPatch> <label>
apply_patch() {
  local m=$1 key=$2 label=$3 root tmp zips=() rel name bytes sha code
  root=$(py manifest-field "$m" "$key.RootFolder")
  while IFS=$'\t' read -r rel name bytes sha; do
    local f="$ASSETS/v$rel/$name"
    [[ -f $f ]] || die "$f is missing: mirror release v$rel"
    sha256sum "$f" | grep -qi "^$sha " || die "$f failed its hash check"
    zips+=("$f")
  done < <(py manifest-parts "$m" "$key")
  tmp=$(mktemp -d "$ROOT/.patch-XXXXXX")
  info "Applying the $label"
  py extract-zips "$tmp" "$root" "${zips[@]}"
  set +e
  DOTNET_ROOT="$TOOLS/dotnet" "$TOOLS/dotnet/dotnet" "$tmp/patcher/OfflineDaoc.Patcher.dll" \
      --game "$GAME" --patch "$tmp" 2>&1 | tee "$LOGS/patch-$key.log"
  code=${PIPESTATUS[0]}
  set -e
  rm -rf "$tmp"
  case $code in
    0) ok "$label applied" ;;
    2) ok "$label was already installed" ;;
    3) die "$label: some files differ from the released ones (listed above); nothing was changed" ;;
    4) die "$label: the game is not a version this update knows" ;;
    5) die "$label: close the server/game first" ;;
    *) die "$label failed (exit $code); the patcher puts files back on failure. See $LOGS/patch-$key.log" ;;
  esac
}

# ---------------------------------------------------------------- main
usage() {
  say "Usage: $0 [step...]   (no steps = all, in order)"
  local s; for s in "${STEPS[@]}"; do printf '  %-11s %s\n' "$s" "${STEP_HELP[$s]}"; done
}

main() {
  [[ ${1:-} == list || ${1:-} == -h || ${1:-} == --help ]] && { usage; exit 0; }
  have python3 || die "python3 is required (it ships with Ubuntu)"
  have curl || die "curl is required: sudo apt install curl"
  [[ -f $HELPER ]] || die "odaoc_helper.py must sit next to this script"
  mkdir -p "$ROOT" "$LOGS"
  local todo=("$@") s
  (( ${#todo[@]} )) || todo=("${STEPS[@]}")
  for s in "${todo[@]}"; do
    [[ -n ${STEP_HELP[$s]:-} ]] || { usage; die "unknown step: $s"; }
  done
  say "Project folder: $ROOT   (free: $(human "$(free_bytes "$ROOT")"))"
  for s in "${todo[@]}"; do
    say ""
    "step_$s"
  done
  say ""
  ok "done: ${todo[*]}"
}

mkdir -p "$LOGS"
trap - ERR
set +e
main "$@" 2>&1 | tee -a "$LOGS/setup-$(date +%Y%m%d).log"
exit "${PIPESTATUS[0]}"
