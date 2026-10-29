#!/bin/bash
# release.sh <version> : builds a public release of P+ Modding Tool into "Releases\v<version>" next to the app
set -e
V="$1"; [ -z "$V" ] && { echo "usage: release.sh 1.2.3"; exit 1; }
SRC="$HOME/mnt/P+ ModManager/source"; OUT="${RELEASE_OUT:-$HOME/mnt/P+ ModManager/Releases}/v$V"; W=~/rel/work-$V
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DN=~/dn/usr/lib/dotnet/dotnet; FW="-p:FrameworkPathOverride=$HOME/mono/usr/lib/mono/4.7.2-api -p:AutomaticallyUseReferenceAssemblyPackages=false"
sed -i "s#<Version>[^<]*</Version>#<Version>$V</Version>#" "$SRC/SkinManager.App/SkinManager.App.csproj" "$SRC/SkinManager.Setup/SkinManager.Setup.csproj"
rm -rf "$W"; mkdir -p "$W/payload/lib" "$W/payload/LICENSES" "$W/app" "$W/setup"
# the app
(cd "$SRC/SkinManager.App" && $DN build -c Release -o "$W/app" $FW -p:BrawlLibPath=$HOME/bc/BrawlLib/bin/AnyCPU/Release/net472/BrawlLib.dll -p:BaseIntermediateOutputPath=$HOME/relobj/app/ -v q 2>&1 | grep -E "error|succeeded" | sort -u | sed 's#.*/source/##')
cp "$W/app/P+ Modding Tool.exe" "$W/payload/"
cp "$HOME/mnt/P+ ModManager/P+ Modding Tool.exe.config" "$W/payload/"
cp ~/bc/BrawlLib/bin/AnyCPU/Release/net472/BrawlLib.dll ~/bc/BrawlLib/OpenTK.dll "$W/payload/lib/"
[ -f ~/rel/update-source.txt ] && cp ~/rel/update-source.txt "$W/payload/"
cp /usr/share/common-licenses/LGPL-3 "$W/payload/LICENSES/BrawlLib-LGPL-3.0.txt"
cp /usr/share/common-licenses/GPL-3 "$W/payload/LICENSES/GPL-3.0.txt"
cp ~/rel/THIRD-PARTY-NOTICES.txt "$W/payload/LICENSES/"
cp ~/rel/README.txt "$W/payload/"
# the update file (what the app downloads) and the Setup (the app inside it)
(cd "$W/payload" && zip -q -r -X "$W/PPlusModdingTool-update-$V.zip" .)
cp "$W/PPlusModdingTool-update-$V.zip" "$W/payload.zip"
(cd "$SRC/SkinManager.Setup" && $DN build -c Release -o "$W/setup" $FW -p:PayloadPath="$W/payload.zip" -p:BaseIntermediateOutputPath=$HOME/relobj/setup/ -v q 2>&1 | grep -E "error|succeeded" | sort -u | sed 's#.*/source/##')
cp "$W/setup/Setup.exe" "$W/PPlusModdingTool-Setup-$V.exe"
# BrawlLib's source as used (LGPL: the library's source, with our changes, goes with every release)
(cd ~/bc && { echo "BrawlLib from BrawlCrate (https://github.com/soopercool101/BrawlCrate), commit $(git rev-parse HEAD), with the changes below."; echo "License: GNU LGPL 3.0 (see LICENSE)."; echo; git diff; } > "$W/BrawlLib-CHANGES.txt"
  zip -q -r -X "$W/BrawlLib-source-$V.zip" BrawlLib LICENSE -x "BrawlLib/bin/*" "BrawlLib/obj/*" "*/.vs/*" && zip -q -j "$W/BrawlLib-source-$V.zip" "$W/BrawlLib-CHANGES.txt")
# checksums, then everything to the Releases folder
(cd "$W" && sha256sum "PPlusModdingTool-Setup-$V.exe" "PPlusModdingTool-update-$V.zip" "BrawlLib-source-$V.zip" > SHA256SUMS.txt)
mkdir -p "$OUT"
cp "$W/PPlusModdingTool-Setup-$V.exe" "$W/PPlusModdingTool-update-$V.zip" "$W/BrawlLib-source-$V.zip" "$W/SHA256SUMS.txt" "$OUT/"
ls -la "$OUT"
