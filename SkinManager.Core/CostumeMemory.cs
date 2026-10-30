// A costume has to fit in the game's memory. The game unpacks the costume file next to the character's moves and
// animations in one block of memory per player, and builds the character's 3D model in what's left. If the unpacked
// costume leaves too little room, building the model fails and the game crashes as the match starts (this is what a
// costume ported with big textures does). So: a costume may unpack to no more than the biggest costume the game itself
// uses for that character. A costume that's bigger is made smaller the way modders do it by hand: its biggest pictures
// are stored in a lighter format (true-color 32-bit pictures become 16-bit ones, which look the same on a character),
// until it fits.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using BrawlLib.Imaging;
using BrawlLib.Internal.Drawing;
using BrawlLib.Internal.IO;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Textures;

namespace SkinManager.Core
{
    public static class CostumeMemory
    {
        /// <summary>The memory block each player's character gets (Fighter1Resource..Fighter4Resource: 5.33 MB).</summary>
        public const long FighterBlock = 0x553400;
        /// <summary>Room the game needs left in that block to build the character (see Installer.MemoryBudget).</summary>
        public const long Headroom = 240 * 1024;

        /// <summary>How big a costume file is once the game unpacks it (Brawl's LZ77 header gives it).</summary>
        public static long Unpacked(byte[] pac)
        {
            if (pac == null || pac.Length < 8) return pac?.Length ?? 0;
            if (pac[0] == 0x10 || pac[0] == 0x11)
            {
                long n = pac[1] | (pac[2] << 8) | (pac[3] << 16);
                if (n == 0) n = BitConverter.ToUInt32(pac, 4);
                return n;
            }
            return pac.Length;
        }

        public sealed class Shrunk
        {
            public byte[] Bytes;          // the smaller costume (null: it couldn't be made small enough)
            public long Before, After;    // unpacked sizes
            public List<string> Pictures = new List<string>();   // what was changed, in words
        }

        /// <summary>Makes a costume unpack to no more than <paramref name="limit"/> bytes by storing its biggest
        /// pictures in lighter formats. Returns the new file (compressed the way P+ stores costumes).</summary>
        public static Shrunk Shrink(byte[] pac, long limit)
        {
            var r = new Shrunk { Before = Unpacked(pac) };
            Installer.Quiet();
            using (var tmp = new TempFile(".pac", pac))
            using (var outFile = new TempFile(".pac"))
            {
                lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    long size = r.Before;
                    // pictures that don't lend their pixels to another picture (color smashing) and aren't paletted
                    var all = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().ToList();
                    bool Free(TEX0Node t)
                    {
                        if (t.SharesData || t.HasPalette) return false;
                        var sib = t.Parent?.Children; int k = sib?.IndexOf(t) ?? -1;
                        return !(k > 0 && sib[k - 1] is TEX0Node prev && prev.SharesData);
                    }
                    // steps, lightest look change first: 32-bit -> 16-bit, then 16-bit/32-bit opaque -> compressed
                    var steps = new List<(TEX0Node Tex, WiiPixelFormat To)>();
                    foreach (var t in all.Where(t => Free(t) && t.Format == WiiPixelFormat.RGBA8).OrderByDescending(t => (long)t.Width * t.Height))
                        steps.Add((t, WiiPixelFormat.RGB5A3));
                    foreach (var t in all.Where(t => Free(t) && (t.Format == WiiPixelFormat.RGBA8 || t.Format == WiiPixelFormat.RGB5A3 || t.Format == WiiPixelFormat.RGB565) && t.Width * t.Height >= 128 * 128).OrderByDescending(t => (long)t.Width * t.Height))
                        steps.Add((t, WiiPixelFormat.CMPR));
                    foreach (var (tex, to) in steps)
                    {
                        if (size <= limit) break;
                        if (tex.Format == to) continue;
                        if (to == WiiPixelFormat.CMPR && HasSeeThrough(tex)) continue;
                        long before = (long)tex.WorkingUncompressed.Length;
                        using (var img = new Bitmap(tex.GetImage(0)))
                            tex.ReplaceRaw(TextureConverter.Get(to).EncodeTEX0Texture(img, Math.Max(1, tex.LevelOfDetail)));
                        long saved = before - (long)tex.WorkingUncompressed.Length;
                        size -= Math.Max(0, saved);
                        r.Pictures.Add($"{tex.Name} ({tex.Width}x{tex.Height}) stored as {Words(to)}");
                    }
                    root.Compression = "ExtendedLZ77";
                    root.Export(outFile.Path);
                }
                var bytes = File.ReadAllBytes(outFile.Path);
                r.After = Unpacked(bytes);
                r.Bytes = r.After <= limit ? bytes : null;
                return r;
            }
        }

        static string Words(WiiPixelFormat f) => f == WiiPixelFormat.RGB5A3 ? "16-bit color" : f == WiiPixelFormat.CMPR ? "compressed color" : f.ToString();

        /// <summary>Whether a picture has see-through parts beyond fully on/off (compressed pictures can't keep those).</summary>
        static bool HasSeeThrough(TEX0Node t)
        {
            using (var img = new Bitmap(t.GetImage(0)))
                for (int y = 0; y < img.Height; y += 2)
                    for (int x = 0; x < img.Width; x += 2)
                    {
                        int a = img.GetPixel(x, y).A;
                        if (a < 250) return true;
                    }
            return false;
        }

        public static string Kb(long b) => $"{(b + 512) / 1024:N0} KB";
    }
}
