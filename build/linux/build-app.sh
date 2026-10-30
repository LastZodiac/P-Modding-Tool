#!/usr/bin/env bash
# Builds "P+ Modding Tool.exe" on Linux with the same options as build/release/release.sh, then lays it
# out like the Windows release (exe, exe.config, lib/BrawlLib.dll, lib/OpenTK.dll, LICENSES/) in
# build/linux/out/app. The same exe runs on Windows (.NET Framework) and on Linux (Mono).
#
# usage: build/linux/build-app.sh            (run build-brawllib.sh first)
set -euo pipefail
. "$(dirname "$0")/common.sh"
need dotnet
need_mono_api
if [ ! -f "$BRAWLLIB_DLL" ]; then
  echo "BrawlLib isn't built yet: run build/linux/build-brawllib.sh first (or set BRAWLLIB_DLL)." >&2
  exit 1
fi

BUILD="$OUT/build-app"
rm -rf "$BUILD"
dotnet build "$REPO/SkinManager.App/SkinManager.App.csproj" -c Release -o "$BUILD" "${FW[@]}" \
  -p:BrawlLibPath="$BRAWLLIB_DLL" -v q -nologo

rm -rf "$APP"
mkdir -p "$APP/lib" "$APP/LICENSES"
cp "$BUILD/P+ Modding Tool.exe" "$BUILD/P+ Modding Tool.exe.config" "$APP/"
cp "$BRAWLLIB_DLL" "$BRAWLCRATE/BrawlLib/OpenTK.dll" "$APP/lib/"
cp "$REPO/LICENSE" "$APP/LICENSES/GPL-3.0.txt"
cp "$BRAWLCRATE/LICENSE" "$APP/LICENSES/BrawlLib-LGPL-3.0.txt"
cp "$REPO/build/release/THIRD-PARTY-NOTICES.txt" "$APP/LICENSES/"
cp "$REPO/build/release/README.txt" "$APP/"

echo "App: $APP"
