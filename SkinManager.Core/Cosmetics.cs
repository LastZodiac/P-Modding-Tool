// Select-screen portraits, battle portraits and stock icons: reading them for previews and
// replacing one slot's picture without disturbing the others.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using BrawlLib.Imaging;
using BrawlLib.Internal.Drawing;
using BrawlLib.Internal.IO;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Textures;

namespace SkinManager.Core
{
    public enum PictureKind { SelectPortrait, BattlePortrait, StockIcon }

    public static class Cosmetics
    {
        public static Size SizeOf(PictureKind k) =>
            k == PictureKind.SelectPortrait ? new Size(128, 160) : k == PictureKind.BattlePortrait ? new Size(48, 56) : new Size(32, 32);

        public static string Describe(PictureKind k) =>
            k == PictureKind.SelectPortrait ? "character select picture" : k == PictureKind.BattlePortrait ? "in-match portrait" : "stock icon";

        /// <summary>Reads one picture out of a .brres file. Returns null if it is not there.</summary>
        public static Bitmap ReadPicture(byte[] brres, string textureName)
        {
            using (var tmp = new TempFile(".brres", brres))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var tex = FindTexture(root, textureName);
                return tex == null ? null : new Bitmap(tex.GetImage(0));
            }
        }

        /// <summary>Reads several pictures from one .brres file (missing ones are left out).</summary>
        public static System.Collections.Generic.Dictionary<string, Bitmap> ReadPictures(byte[] brres, System.Collections.Generic.IEnumerable<string> textureNames)
        {
            var result = new System.Collections.Generic.Dictionary<string, Bitmap>();
            using (var tmp = new TempFile(".brres", brres))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                foreach (var name in textureNames)
                {
                    var tex = FindTexture(root, name);
                    if (tex != null) result[name] = new Bitmap(tex.GetImage(0));
                }
            return result;
        }

        /// <summary>Replaces one picture in a .brres file and returns the new file. Pictures that shared
        /// pixel data with it ("color smashed") keep exactly the pixels they had.</summary>
        public static byte[] ReplacePicture(byte[] brres, string textureName, Bitmap picture)
        {
            using (var tmp = new TempFile(".brres", brres))
            using (var outFile = new TempFile(".brres"))
            {
                lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var tex = FindTexture(root, textureName) ?? throw new InvalidDataException("The picture " + textureName + " is not in this file.");
                    var siblings = tex.Parent.Children;
                    int k = siblings.IndexOf(tex);

                    // Color smashing: a picture marked "shares data" uses the pixels of the next picture
                    // that has its own. If the picture right before this one borrows through it, give that
                    // one an exact copy of the pixels it was using, so it no longer depends on this slot.
                    if (k > 0 && siblings[k - 1] is TEX0Node prev && prev.SharesData)
                    {
                        var holder = tex.SharesData ? tex.SourceNode : tex;
                        using (var holderData = new TempFile(".tex0"))
                        {
                            holder.Export(holderData.Path);
                            prev.SetSharesData(false, false);
                            prev.Replace(holderData.Path);
                        }
                    }
                    if (tex.SharesData) tex.SetSharesData(false, false);

                    var size = new Size(tex.Width, tex.Height);
                    var palette = tex.GetPaletteNode();
                    using (var fitted = Fit(picture, size))
                    {
                        if (palette != null)
                        {
                            using (var indexed = fitted.Quantize(QuantizationAlgorithm.MedianCut, 256, WiiPixelFormat.CI8, WiiPaletteFormat.RGB5A3, null))
                            {
                                var texData = TextureConverter.Get(WiiPixelFormat.CI8).EncodeTEX0TextureIndexed(indexed, 1, WiiPaletteFormat.RGB5A3, out FileMap palData);
                                tex.ReplaceRaw(texData);
                                palette.ReplaceRaw(palData);
                            }
                        }
                        else
                        {
                            var texData = TextureConverter.Get(tex.Format).EncodeTEX0Texture(fitted, 1);
                            tex.ReplaceRaw(texData);
                        }
                    }
                    root.Export(outFile.Path);
                }
                return File.ReadAllBytes(outFile.Path);
            }
        }

        /// <summary>The picture by name. A file holding just one picture (P+'s battle portraits, InfFaceNNNN.brres: their
        /// pictures keep Brawl's old names, and P+'s extra slots reuse copies of the first one) gives that picture.</summary>
        static TEX0Node FindTexture(ResourceNode root, string name)
        {
            var all = root.FindChildrenByType(null, ResourceType.TEX0).Concat(root.FindChildrenByType(null, ResourceType.SharedTEX0)).OfType<TEX0Node>().ToList();
            return all.FirstOrDefault(t => t.Name == name) ?? (all.Count == 1 ? all[0] : null);
        }

        /// <summary>Scales a picture to the exact size, keeping its shape (transparent edges if needed).</summary>
        public static Bitmap Fit(Image src, Size size)
        {
            var dst = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                if (src.Width == size.Width && src.Height == size.Height)
                {
                    g.DrawImageUnscaled(src, 0, 0);
                    return dst;
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double scale = Math.Min((double)size.Width / src.Width, (double)size.Height / src.Height);
                int w = Math.Max(1, (int)Math.Round(src.Width * scale)), h = Math.Max(1, (int)Math.Round(src.Height * scale));
                g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
            }
            return dst;
        }
    }

    /// <summary>A file in the temp folder that is deleted when disposed.</summary>
    public sealed class TempFile : IDisposable
    {
        public string Path { get; }
        static readonly string Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PPlusModdingTool");
        public TempFile(string ext, byte[] contents = null)
        {
            Directory.CreateDirectory(Dir);
            Path = System.IO.Path.Combine(Dir, Guid.NewGuid().ToString("N") + ext);
            if (contents != null) File.WriteAllBytes(Path, contents);
        }
        public void Dispose() { try { File.Delete(Path); } catch { } }
    }
}
