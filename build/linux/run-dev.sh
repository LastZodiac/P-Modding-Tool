#!/usr/bin/env bash
# Runs the app built by build-app.sh with the system's Mono.
#
# usage: build/linux/run-dev.sh [app arguments]
#   TRACE=1   prints the app's trace lines (SKINMANAGER_TRACE) to the terminal
set -euo pipefail
. "$(dirname "$0")/common.sh"
need mono
if [ ! -f "$APP/P+ Modding Tool.exe" ]; then
  echo "The app isn't built yet: run build/linux/build-app.sh first." >&2
  exit 1
fi
[ "${TRACE:-0}" = 1 ] && export SKINMANAGER_TRACE=1
cd "$APP"
exec mono "$APP/P+ Modding Tool.exe" "$@"
