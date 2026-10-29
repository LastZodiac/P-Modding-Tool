// Making a bigger copy of the P+ SD card without rewriting any file: the card's file table (FAT) is made big
// enough for the new size and the data area is copied byte for byte after it. Every file keeps its exact place
// (its clusters), so nothing about how P+ finds its files changes; only the card gets more free room.
using System;
using System.IO;

namespace SkinManager.Core
{
    public static class CardTools
    {
        /// <summary>Copies a FAT32 card image into a bigger one at <paramref name="to"/>. Sizes must be multiples of 512 KB.</summary>
        public static void Grow(string from, string to, long newBytes, Action<int> progress = null)
        {
            if (newBytes % (512 * 1024) != 0) throw new ArgumentException("The new size must be a multiple of 512 KB.");
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var boot = new byte[512];
                Read(src, 0, boot);
                if (boot[510] != 0x55 || boot[511] != 0xAA) throw new InvalidDataException("The SD card isn't a FAT disk image.");
                int bps = BitConverter.ToUInt16(boot, 11), spc = boot[13], reserved = BitConverter.ToUInt16(boot, 14), fats = boot[16];
                uint fatSize = BitConverter.ToUInt32(boot, 36);
                int fsInfo = BitConverter.ToUInt16(boot, 48), backupBoot = BitConverter.ToUInt16(boot, 50);
                if (bps != 512 || BitConverter.ToUInt16(boot, 22) != 0 || fatSize == 0) throw new InvalidDataException("The SD card isn't FAT32.");
                long oldSectors = BitConverter.ToUInt32(boot, 32);
                long oldDataStart = (reserved + (long)fats * fatSize) * bps;
                long oldClusters = (oldSectors - reserved - (long)fats * fatSize) / spc;
                if (newBytes <= src.Length) throw new ArgumentException("The new card must be bigger.");

                // a FAT big enough to number every cluster of the new card
                long newSectors = newBytes / bps;
                uint newFat = fatSize;
                while (true)
                {
                    long clusters = (newSectors - reserved - (long)fats * newFat) / spc;
                    long needed = ((clusters + 2) * 4 + bps - 1) / bps;
                    if (needed <= newFat) break;
                    newFat = (uint)needed;
                }
                long newClusters = (newSectors - reserved - (long)fats * newFat) / spc;
                if (newClusters > 0x0FFFFFF5) throw new ArgumentException("That's too big for FAT32.");
                long newDataStart = (reserved + (long)fats * newFat) * bps;

                string partial = to + ".partial";
                try
                {
                    using (var dst = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        dst.SetLength(newBytes);
                        // reserved area (boot sector, FSInfo, backup boot sector), with the new size and FAT size
                        var res = new byte[reserved * bps];
                        Read(src, 0, res);
                        foreach (int sector in new[] { 0, backupBoot })
                        {
                            if (sector <= 0 && sector != 0 || (sector + 1) * bps > res.Length) continue;
                            BitConverter.GetBytes((uint)newSectors).CopyTo(res, sector * bps + 32);
                            BitConverter.GetBytes(newFat).CopyTo(res, sector * bps + 36);
                        }
                        // FSInfo: the free count grows by the new clusters (0xFFFFFFFF means "unknown", left as is)
                        foreach (int sector in new[] { fsInfo, backupBoot > 0 ? backupBoot + fsInfo : -1 })
                        {
                            if (sector <= 0 || (sector + 1) * bps > res.Length) continue;
                            int o = sector * bps;
                            if (BitConverter.ToUInt32(res, o) != 0x41615252) continue;
                            uint free = BitConverter.ToUInt32(res, o + 488);
                            if (free != 0xFFFFFFFF) BitConverter.GetBytes((uint)(free + (newClusters - oldClusters))).CopyTo(res, o + 488);
                        }
                        Write(dst, 0, res);
                        // each FAT copy: the old table, then zeros (free) for the new clusters
                        var fat = new byte[fatSize * (long)bps];
                        Read(src, reserved * (long)bps, fat);
                        for (int i = 0; i < fats; i++) Write(dst, (reserved + (long)i * newFat) * bps, fat);
                        // the data area, byte for byte, moved after the bigger FATs
                        var buf = new byte[4 * 1024 * 1024];
                        long total = src.Length - oldDataStart, done = 0;
                        src.Position = oldDataStart; dst.Position = newDataStart;
                        int n, last = -1;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            dst.Write(buf, 0, n);
                            done += n;
                            int pct = (int)(done * 100 / Math.Max(1, total));
                            if (pct != last) { progress?.Invoke(pct); last = pct; }
                        }
                        dst.Flush(true);
                    }
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(partial, to);
                }
                catch { try { File.Delete(partial); } catch { } throw; }
            }
        }

        static void Read(FileStream s, long at, byte[] b) { s.Position = at; int got = 0; while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) throw new EndOfStreamException(); got += n; } }
        static void Write(FileStream s, long at, byte[] b) { s.Position = at; s.Write(b, 0, b.Length); }
    }
}
