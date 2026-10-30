#!/usr/bin/env bash
# Builds BrawlLib for .NET Framework 4.7.2 on Linux, the way the Windows release uses it:
# BrawlCrate at the pinned commit, prepared by build/patch_brawllib.sh, built against Mono's
# reference assemblies.
#
# usage: build/linux/build-brawllib.sh
#   BRAWLCRATE=<folder>  where to keep the BrawlCrate clone (default: build/linux/out/BrawlCrate).
#                        The script owns that folder: it resets it to the pinned commit on every run.
set -euo pipefail
. "$(dirname "$0")/common.sh"
need git dotnet resgen
need_mono_api

# 1. BrawlCrate at the pinned commit (only BrawlLib and the licence: that's all the app uses).
if [ ! -d "$BRAWLCRATE/.git" ]; then
  mkdir -p "$BRAWLCRATE"
  git -C "$BRAWLCRATE" init -q
  git -C "$BRAWLCRATE" remote add origin https://github.com/soopercool101/BrawlCrate.git
  git -C "$BRAWLCRATE" sparse-checkout set --no-cone '/BrawlLib/' '/LICENSE*'
fi
if ! git -C "$BRAWLCRATE" cat-file -e "$BRAWLCRATE_COMMIT^{commit}" 2>/dev/null; then
  git -C "$BRAWLCRATE" fetch -q --depth 1 --filter=blob:none origin "$BRAWLCRATE_COMMIT"
fi
# Start from a clean tree each time, so the patch below always applies to the same files.
git -C "$BRAWLCRATE" checkout -q -f "$BRAWLCRATE_COMMIT"
git -C "$BRAWLCRATE" clean -q -f -d -x

# 2. The owner's patch (no NuGet, no WPF, 64-bit pointer and Mono fixes).
#    RESGEN: Mono's resgen compiles BrawlLib's .resx images (Windows' build tools do it on Windows).
RESGEN=resgen bash "$REPO/build/patch_brawllib.sh" "$BRAWLCRATE"

# 3. Build.
#    DefineConstants=NOWPF: the patch puts the WPF-only code behind "#if !NOWPF" and removes the WPF
#                           references; without it the build fails on System.Windows.Media.
#    PreBuildEvent=         : BrawlLib's pre-build step is a Windows cmd command (RD /S /Q) that the
#                           Linux shell can't run.
dotnet build "$BRAWLCRATE/BrawlLib/BrawlLib.csproj" -c Release -p:Platform=AnyCPU \
  -p:DefineConstants=NOWPF -p:PreBuildEvent= "${FW[@]}" -v q -nologo

echo "BrawlLib: $BRAWLLIB_DLL"
