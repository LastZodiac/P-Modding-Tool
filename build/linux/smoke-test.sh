#!/usr/bin/env bash
# Starts the app without a screen (Xvfb) on a fake Brawl Online install and checks that it stays up
# and logs no unexpected errors. Nothing outside a temporary folder is touched: HOME, the app copy,
# the SD card and the Data folder all live there.
#
# usage: build/linux/smoke-test.sh [path to a folder laid out like build-app.sh's output]
#   SECONDS_UP=45     how long the app must stay up
#   KEEP=1            keep the temporary folder (screenshot, errors.log) for a look afterwards
#   APPIMAGE_FILE=..  test an AppImage instead of the app folder
set -euo pipefail
. "$(dirname "$0")/common.sh"
need xvfb-run mkfs.vfat mmd mcopy import
SRC="${1:-$APP}"
UP="${SECONDS_UP:-45}"

T="$(mktemp -d /tmp/pmt-smoke.XXXXXX)"
cleanup() { if [ "${KEEP:-0}" = 1 ]; then echo "Kept: $T"; else rm -rf "$T"; fi; }
trap cleanup EXIT
export HOME="$T/home" XDG_CONFIG_HOME="$T/home/.config" XDG_DATA_HOME="$T/home/.local/share"
export SKINMANAGER_DATA="$T/data"   # used once the Platform layer is in (PR 1)
mkdir -p "$HOME"

# A fake Brawl Online install: the folders the launcher makes on Linux, and a small FAT32 SD card
# with P+'s folder layout.
NETPLAY="$XDG_CONFIG_HOME/Brawl Online/netplay"
mkdir -p "$NETPLAY/User/Wii" "$NETPLAY/User/Logs" "$NETPLAY/User/Config"
CARD="$NETPLAY/User/Wii/sd.raw"
truncate -s 64M "$CARD"
mkfs.vfat -F 32 -n PPLUS "$CARD" >/dev/null
export MTOOLS_SKIP_CHECK=1
mmd -i "$CARD" ::/Project+ ::/Project+/pf ::/Project+/pf/fighter ::/Project+/pf/fighter/mario
printf 'x' > "$T/FitMario00.pac"
mcopy -i "$CARD" "$T/FitMario00.pac" ::/Project+/pf/fighter/mario/FitMario00.pac
: > "$NETPLAY/User/Logs/dolphin.log"

# The app under test, copied so its Data folder (next to the exe before PR 1) stays in $T.
if [ -n "${APPIMAGE_FILE:-}" ]; then
  RUN=("$APPIMAGE_FILE")
  export APPIMAGE_EXTRACT_AND_RUN=1   # no FUSE needed
else
  need mono
  cp -r "$SRC" "$T/app"
  RUN=(mono "$T/app/P+ Modding Tool.exe")
fi

export T UP
export SKINMANAGER_TRACE=1
xvfb-run -a -s "-screen 0 1440x900x24" bash -c '
  "$@" >"$T/app.log" 2>&1 &
  pid=$!
  sleep "$UP"
  import -window root "$T/screenshot.png" 2>/dev/null || true
  if kill -0 "$pid" 2>/dev/null; then echo up >"$T/status"; kill "$pid"; wait "$pid" 2>/dev/null || true
  else wait "$pid"; echo "exited:$?" >"$T/status"; fi
' bash "${RUN[@]}"

status="$(cat "$T/status" 2>/dev/null || echo unknown)"
LOG="$(ls "$T/data/errors.log" "$T/app/Data/errors.log" 2>/dev/null | head -1 || true)"

# Errors the app is allowed to note in a test: it has no network access to GitHub or GameBanana
# here, or no certificates for them.
ALLOWED='checking for a new version|mod updates|GameBanana'
unexpected=""
if [ -n "$LOG" ]; then
  unexpected="$(grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+Z \(' "$LOG" | grep -vE "\(($ALLOWED)" || true)"
fi

echo "App: $status (after ${UP}s)"
echo "Screenshot: $T/screenshot.png"
if [ "$status" != up ]; then
  echo "FAIL: the app didn't stay up. Last lines of its output:"; tail -20 "$T/app.log"; KEEP=1; exit 1
fi
if [ -n "$unexpected" ]; then
  echo "FAIL: unexpected errors in errors.log:"; echo "$unexpected"; KEEP=1; exit 1
fi
echo "OK"
