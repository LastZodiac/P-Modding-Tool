#!/bin/bash
# Prepares BrawlCrate's BrawlLib (pinned commit) to build without NuGet, WPF or Windows-only build steps.
# Used by the build workspace only; you never need to run this.
set -e
BL="$1/BrawlLib"; HERE="$(cd "$(dirname "$0")" && pwd)"
cp "$HERE/Shims.cs" "$BL/_Shims.cs"
sed -i '/System.IO.Hashing/d;/Reference Include="PresentationCore"/d;/Reference Include="System.Xaml"/d;/Reference Include="WindowsBase"/d' "$BL/BrawlLib.csproj"
grep -q NOWPF "$BL/Internal/Windows/Forms/TextureConverterDialog.cs" || {
  sed -i 's/^using System.Windows.Media.Imaging;/#if !NOWPF\nusing System.Windows.Media.Imaging;\n#endif/' "$BL/Internal/Windows/Forms/TextureConverterDialog.cs"
  sed -i 's/^#if !MONO$/#if !MONO \&\& !NOWPF/' "$BL/Internal/Windows/Forms/TextureConverterDialog.cs"
  sed -i 's/^\[assembly: System.Windows.Media.DisableDpiAwareness\]/#if !NOWPF\n[assembly: System.Windows.Media.DisableDpiAwareness]\n#endif/' "$BL/Properties/AssemblyInfo.cs"
  sed -i '/using Point = System.Windows.Point;/d' "$BL/Internal/Windows/Controls/ModelViewer/MainWindowBase/Variables.cs"
  sed -i '1i using Point = System.Windows.Point;' "$BL/Internal/Windows/Controls/ModelViewer/MainWindowBase/Variables.cs"
}
printf '%s\n' '<Project><ItemGroup><EmbeddedResource Remove="**/*.resx" /></ItemGroup></Project>' > "$BL/Directory.Build.targets"
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>' > "$BL/NuGet.config"
# Mono (used only for testing in the build workspace) refuses to map a file shorter than the mapping.
# Grow it first, as Windows does on its own. This class is never used on Windows.
grep -q "MONO_GROW" "$BL/Internal/IO/FileMap.cs" || sed -i 's/^            _length = length;$/            _length = length; \/\/ MONO_GROW\n            if (protect == FileMapProtect.ReadWrite \&\& stream.Length < (long) offset + length) stream.SetLength((long) offset + length);/' "$BL/Internal/IO/FileMap.cs"
# BrawlLib's .resx files hold images, which only Windows' build tools can compile from .resx.
# Compile them here with Mono's resgen and embed the results under the names the code expects.
RESGEN="${RESGEN:-$HOME/monorun.sh $HOME/mono/usr/lib/mono/4.5/resgen.exe}"
mkdir -p "$BL/_res"; ITEMS=""
while IFS= read -r -d '' r; do
  cs="${r%.resx}.cs"; base=$(basename "${r%.resx}")
  if [ "$base" = "Resources" ]; then name="BrawlLib.Properties.Resources"
  else ns=$(grep -m1 -oP '^\s*namespace\s+\K[\w.]+' "$cs"); cls=$(grep -m1 -oP '\bclass\s+\K\w+' "$cs"); name="$ns.$cls"; fi
  ( cd "$(dirname "$r")" && MONO_PATH=$HOME/mono/usr/lib/mono/4.5 $RESGEN "$(basename "$r")" "$BL/_res/$name.resources" >/dev/null )
  ITEMS="$ITEMS<EmbeddedResource Include=\"_res/$name.resources\" LogicalName=\"$name.resources\" />"
done < <(find "$BL" -name "*.resx" -print0)
printf '%s\n' "<Project><ItemGroup><EmbeddedResource Remove=\"**/*.resx\" />$ITEMS</ItemGroup></Project>" > "$BL/Directory.Build.targets"
# Mono's ColorPalette names its private field differently; tolerate that (Windows is unaffected).
grep -q "MONO_PAL" "$BL/Internal/Drawing/Imaging/ColorPaletteExtension.cs" || python3 - "$BL/Internal/Drawing/Imaging/ColorPaletteExtension.cs" <<'PY'
import sys; p = sys.argv[1]; s = open(p, encoding="utf-8-sig").read()
old = """            typeof(ColorPalette).GetField("flags", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pal, (int) flags);"""
new = """            // MONO_PAL
            var field = typeof(ColorPalette).GetField("flags", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? typeof(ColorPalette).GetField("_flags", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(pal, (int) flags);"""
assert old in s; open(p, "w", encoding="utf-8").write(s.replace(old, new))
PY
# Bitmap pixel pointers were cut to 32 bits; keep the full pointer (no change on 32-bit Windows).
sed -i 's/(int) data\.Scan0 + /(VoidPtr) data.Scan0 + /' "$BL/Imaging/DIB.cs"
sed -i 's/(int) dib\.Scan0 + /(VoidPtr) dib.Scan0 + /' "$BL/Wii/Textures/TextureConverter.cs"
sed -i 's/(int) _scan0 + /(VoidPtr) _scan0 + /' "$BL/Imaging/DIB.cs"
# Havok physics data compared pointers as 32-bit numbers; compare them as 64-bit (same result on 32-bit Windows).
H="$BL/SSBB/ResourceNodes/Havok Physics/HavokNode.cs"
sed -i 's/(int) patch - (int) start/(long) patch - (long) start/g' "$H"
sed -i 's/return (int) WorkingUncompressed.Address - (int) p._buffer.Address;/return WorkingUncompressed.Address - p._buffer.Address;/' "$H"

# Never show BrawlLib's "model has errors" / "unsupported version" boxes (the app sets HideMDL0Errors).
python3 - "$BL" <<'PY'
import sys
p=sys.argv[1]+'/SSBB/ResourceNodes/MDL0/MDL0Node.cs'; s=open(p).read()
old='''                    if (!SupportedVersions.Contains(_version))
                    {
                        MessageBox.Show('''
new='''                    if (!SupportedVersions.Contains(_version))
                    {
                        if (!Properties.Settings.Default.HideMDL0Errors) MessageBox.Show('''
if old in s: open(p,'w').write(s.replace(old,new))
PY
