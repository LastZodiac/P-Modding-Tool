// The game loads a stage file into its stage memory and unpacks it there, in place: the packed file sits at the end of
// the memory and the unpacked parts are written from the front, over the packed data as it's used up. Some stage programs
// take part of that memory before the file loads, so their files have less room to unpack into. When the unpacked parts
// run into packed data the game hasn't read yet, the stage comes out scrambled and the game freezes right after loading it
// ("gfPoolFree Error"). Seen with "KOF Stadium [PS2 ALT]" on Pokemon Stadium 2: the game's own crash address was
// reproduced exactly from the file with the room below, and P+'s own Pokemon Stadium 2 files all fit in it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using BrawlLib.Internal.IO;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Textures;

namespace SkinManager.Core
{
    public static partial class StageCheck
    {
        /// <summary>Stage memory a stage program has taken when its file loads (measured), by stage program.</summary>
        static readonly Dictionary<string, long> TakenFirst = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["st_stadium_static.rel"] = 0x209800,   // Pokemon Stadium 2 (and its alts): 2,086 KB
        };

        /// <summary>Kept free on top, so a file never unpacks right up to the edge.</summary>
        public const long LoadMargin = 64 * 1024;

        /// <summary>How much a file for this stage program may unpack to.</summary>
        public static long RoomFor(string program) =>
            program != null && TakenFirst.TryGetValue(program, out long taken) ? StageHeap - taken - LoadMargin : StageHeap;

        public sealed class MemoryFit
        {
            public byte[] Bytes;            // the file made to fit (null: it couldn't be)
            public long Before, After;      // unpacked sizes
            public List<string> Changes = new List<string>();
        }

        /// <summary>Makes a stage file unpack to no more than <paramref name="room"/>: first it leaves out effects banks P+'s
        /// file doesn't have (P+'s stage program doesn't load them), then it makes the biggest pictures half as wide and half
        /// as tall, biggest first, until the file fits.</summary>
        public static MemoryFit FitMemory(byte[] stage, byte[] original, long room)
        {
            var r = new MemoryFit { Before = MemorySize(stage) };
            if (r.Before <= 0) return r;
            if (r.Before <= room) { r.Bytes = stage; r.After = r.Before; return r; }
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (original != null)
                try
                {
                    Installer.Quiet();
                    using (var t = new TempFile(".pac", original))
                    lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, t.Path))
                        if (root != null) foreach (var c in root.Children) keep.Add(c.Name);
                }
                catch { }
            byte[] bytes = stage;
            for (int round = 0; round < 4; round++)
            {
                long size = MemorySize(bytes);
                if (size <= room) break;
                Installer.Quiet();
                using (var tIn = new TempFile(".pac", bytes))
                using (var tOut = new TempFile(".pac"))
                {
                    lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tIn.Path))
                    {
                        if (root == null) return r;
                        foreach (var ef in root.Children.Where(c => c.Name.StartsWith("ef_", StringComparison.OrdinalIgnoreCase) && original != null && !keep.Contains(c.Name)).ToList())
                        {
                            size -= (long)ef.UncompressedSize;
                            root.RemoveChild(ef);
                            r.Changes.Add("left out its effects bank " + ef.Name + " (P+'s stage program doesn't load it)");
                        }
                        var pictures = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>()
                            .Where(t => !t.SharesData && !t.HasPalette && !LendsPixels(t) && t.Width >= 64 && t.Height >= 64)
                            .OrderByDescending(t => (long)t.WorkingUncompressed.Length).ToList();
                        foreach (var t in pictures)
                        {
                            if (size <= room) break;
                            long before = t.WorkingUncompressed.Length;
                            int w = t.Width / 2, h = t.Height / 2;
                            using (var img = new Bitmap(t.GetImage(0)))
                            using (var half = new Bitmap(w, h))
                            {
                                using (var g = Graphics.FromImage(half))
                                {
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
                                }
                                t.ReplaceRaw(TextureConverter.Get(t.Format).EncodeTEX0Texture(half, Math.Max(1, t.LevelOfDetail - 1)));
                            }
                            size -= Math.Max(0, before - (long)t.WorkingUncompressed.Length);
                            r.Changes.Add($"made {t.Name} {w}x{h}");
                        }
                        root.Export(tOut.Path);
                    }
                    bytes = File.ReadAllBytes(tOut.Path);
                }
            }
            r.After = MemorySize(bytes);
            r.Bytes = r.After > 0 && r.After <= room ? bytes : null;
            return r;
        }

        static bool LendsPixels(TEX0Node t)
        {
            var sib = t.Parent?.Children; int k = sib?.IndexOf(t) ?? -1;
            return k > 0 && sib[k - 1] is TEX0Node prev && prev.SharesData;
        }

        /// <summary>Does the stage fit in the stage memory its stage program leaves it once unpacked?</summary>
        public static CheckResult MemoryCheck(string fileName, byte[] stage, string program)
        {
            long room = RoomFor(program);
            if (room >= StageHeap) return MemoryCheck(fileName, stage);
            long n = MemorySize(stage);
            if (n <= 0) return new CheckResult { Passed = true, Title = "Stage memory not checked", Detail = "The file couldn't be unpacked to measure it." };
            bool ok = n <= room;
            return new CheckResult
            {
                Passed = ok,
                Title = ok ? "Fits in the stage memory this stage leaves" : "Too big for the stage memory this stage leaves",
                Detail = ok
                    ? $"{fileName} unpacks to {n / 1024} KB in the game; this stage's program leaves {room / 1024} KB for it."
                    : $"{fileName} unpacks to {n / 1024} KB in the game, but this stage's program leaves only {room / 1024} KB for it. The game would freeze as the stage loads.",
            };
        }
    }
}
