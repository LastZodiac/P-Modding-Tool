# Shared settings for the build/linux scripts. Sourced, never run on its own.
# Every path can be changed with an environment variable of the same name.

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

# Everything the scripts make goes here (ignored by git).
OUT="${OUT:-$REPO/build/linux/out}"
# BrawlCrate clone used to build BrawlLib (made and reset by build-brawllib.sh).
BRAWLCRATE="${BRAWLCRATE:-$OUT/BrawlCrate}"
BRAWLCRATE_COMMIT="a4c1ee3385010d5c64a88df9c56267c61f213321"
BRAWLLIB_DLL="${BRAWLLIB_DLL:-$BRAWLCRATE/BrawlLib/bin/AnyCPU/Release/net472/BrawlLib.dll}"
# The runnable app, laid out like the Windows release (exe, exe.config, lib/, LICENSES/).
APP="${APP:-$OUT/app}"

# .NET Framework 4.7.2 reference assemblies. Windows' targeting pack doesn't exist on Linux;
# Mono ships the same API as reference assemblies, and the .NET SDK compiles against them.
MONO_API="${MONO_API:-/usr/lib/mono/4.7.2-api}"
FW=(-p:FrameworkPathOverride="$MONO_API" -p:AutomaticallyUseReferenceAssemblyPackages=false)

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

need() {
  local missing=()
  for tool in "$@"; do command -v "$tool" >/dev/null 2>&1 || missing+=("$tool"); done
  if [ ${#missing[@]} -gt 0 ]; then
    echo "Missing: ${missing[*]}" >&2
    echo "Arch: sudo pacman -S --needed git mono libgdiplus dotnet-sdk xorg-server-xvfb dosfstools mtools imagemagick fuse2 curl unzip" >&2
    echo "Debian/Ubuntu: sudo apt install git mono-devel mono-utils libgdiplus dotnet-sdk-8.0 xvfb dosfstools mtools imagemagick libfuse2 curl unzip" >&2
    exit 1
  fi
}

need_mono_api() {
  if [ ! -f "$MONO_API/mscorlib.dll" ]; then
    echo "Mono's .NET Framework 4.7.2 reference assemblies are not in $MONO_API." >&2
    echo "Install Mono's development package (Arch: mono, Debian/Ubuntu: mono-devel) or set MONO_API." >&2
    exit 1
  fi
}
