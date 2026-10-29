// Saves pictures as an animated GIF (for sharing a move's hitboxes). One palette for the whole animation:
// the 256 most used colours (after rounding), every pixel matched to the nearest of them.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SkinManager.Core
{
    public static class GifWriter
    {
        /// <summary>delay: hundredths of a second per picture (2 = 50 a second, about game speed).</summary>
        public static void Save(string path, IList<Bitmap> frames, int delay = 2)
        {
            if (frames.Count == 0) return;
            int w = frames[0].Width, h = frames[0].Height;
            var pixels = frames.Select(f => Pixels(f, w, h)).ToList();
            // palette: most common colours, rounded to 5 bits a channel
            var counts = new Dictionary<int, int>();
            foreach (var px in pixels) for (int i = 0; i < px.Length; i += 3) { int k = Key(px[i]); counts.TryGetValue(k, out int c); counts[k] = c + 1; }
            var palette = counts.OrderByDescending(kv => kv.Value).Take(256).Select(kv => kv.Key).ToList();
            while (palette.Count < 256) palette.Add(0);
            var pr = palette.Select(k => ((k >> 10) & 31) * 255 / 31).ToArray();
            var pg = palette.Select(k => ((k >> 5) & 31) * 255 / 31).ToArray();
            var pb = palette.Select(k => (k & 31) * 255 / 31).ToArray();
            var nearest = new Dictionary<int, byte>();
            byte Index(int argb)
            {
                int k = Key(argb);
                if (nearest.TryGetValue(k, out var b)) return b;
                int r = (k >> 10 & 31) * 255 / 31, g = (k >> 5 & 31) * 255 / 31, bl = (k & 31) * 255 / 31, best = 0, bd = int.MaxValue;
                for (int i = 0; i < 256; i++) { int d = (pr[i] - r) * (pr[i] - r) * 3 + (pg[i] - g) * (pg[i] - g) * 4 + (pb[i] - bl) * (pb[i] - bl) * 2; if (d < bd) { bd = d; best = i; } }
                return nearest[k] = (byte)best;
            }
            using (var fs = File.Create(path))
            using (var o = new BinaryWriter(fs))
            {
                o.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
                o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0);
                for (int i = 0; i < 256; i++) { o.Write((byte)pr[i]); o.Write((byte)pg[i]); o.Write((byte)pb[i]); }
                // loop forever
                o.Write(new byte[] { 0x21, 0xFF, 0x0B }); o.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); o.Write(new byte[] { 3, 1, 0, 0, 0 });
                foreach (var px in pixels)
                {
                    o.Write(new byte[] { 0x21, 0xF9, 4, 0 }); o.Write((ushort)delay); o.Write((byte)0); o.Write((byte)0);
                    o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0);
                    var idx = new byte[w * h];
                    for (int i = 0; i < idx.Length; i++) idx[i] = Index(px[i * 3]);
                    Lzw(o, idx);
                }
                o.Write((byte)0x3B);
            }
        }

        static int Key(int argb) => ((argb >> 19) & 31) << 10 | ((argb >> 11) & 31) << 5 | ((argb >> 3) & 31);

        // every pixel's colour, spaced 3 apart (only [i*3] used: keeps the indexing obvious)
        static int[] Pixels(Bitmap b, int w, int h)
        {
            using (var copy = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(copy)) g.DrawImage(b, 0, 0, w, h);
                var data = copy.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var raw = new int[w * h];
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, raw, y * w, w);
                copy.UnlockBits(data);
                var spaced = new int[w * h * 3];
                for (int i = 0; i < raw.Length; i++) spaced[i * 3] = raw[i];
                return spaced;
            }
        }

        static void Lzw(BinaryWriter o, byte[] idx)
        {
            const int minCode = 8;
            o.Write((byte)minCode);
            var block = new List<byte>();
            int bitBuf = 0, bitCount = 0;
            void Emit(int code, int size)
            {
                bitBuf |= code << bitCount; bitCount += size;
                while (bitCount >= 8) { block.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCount -= 8; if (block.Count == 255) Flush(); }
            }
            void Flush() { if (block.Count == 0) return; o.Write((byte)block.Count); o.Write(block.ToArray()); block.Clear(); }
            int clear = 1 << minCode, end = clear + 1, next = end + 1, size = minCode + 1;
            var table = new Dictionary<int, int>();
            Emit(clear, size);
            int prefix = idx[0];
            for (int i = 1; i < idx.Length; i++)
            {
                int c = idx[i], key = prefix << 8 | c;
                if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix, size);
                if (next < 4096) { table[key] = next++; if (next > (1 << size) && size < 12) size++; }
                else { Emit(clear, size); table.Clear(); next = end + 1; size = minCode + 1; }
                prefix = c;
            }
            Emit(prefix, size); Emit(end, size);
            if (bitCount > 0) { block.Add((byte)(bitBuf & 0xFF)); }
            Flush(); o.Write((byte)0);
        }
    }
}
