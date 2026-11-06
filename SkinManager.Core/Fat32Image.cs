// Reads and writes files inside a FAT32 disk image (the P+ SD card, sd.raw) without mounting it.
// Supports what the modding tool needs: find, read, write (replace or create), delete, make folders.
// Long file names are read and written. Every change goes to both FATs and the FSInfo sector.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkinManager.Core
{
    /// <summary>The SD card image opened for reading or writing files inside it. Keeps the whole file table (FAT) in
    /// memory.</summary>
    public sealed class Fat32Image : IDisposable
    {
        /// <summary>One file or folder in a folder on the card, with where its entry sits so it can be changed or
        /// deleted.</summary>
        public sealed class Entry
        {
            public string Name;          // long name if present, else 8.3
            public string ShortName;     // "NAME    EXT" (11 chars)
            public byte Attr;
            public uint FirstCluster;
            public uint Size;
            public bool IsDirectory => (Attr & 0x10) != 0;
            internal uint DirCluster;    // first cluster of the folder holding this entry
            internal long EntryOffset;   // byte offset of the 8.3 entry in the image
            internal List<long> LfnOffsets = new List<long>();
        }

        const uint EOC = 0x0FFFFFFF, MASK = 0x0FFFFFFF;
        readonly FileStream _fs;
        readonly int _bps, _spc, _reserved, _numFats;
        readonly uint _fatSize, _rootCluster, _totalClusters;
        readonly int _fsInfoSector;
        readonly long _dataStart;
        readonly int _clusterBytes;
        uint[] _fat;
        uint _nextFreeHint = 2;

        /// <summary>deferred: changes are kept in memory (reads see them) and only reach the card in Commit, with a
        /// journal of what they replace written first. Nothing is written to the card before Commit.</summary>
        public Fat32Image(string path, bool writable, bool deferred) : this(path, writable)
        {
            if (deferred) _pending = new Dictionary<long, byte[]>();
        }

        /// <summary>Opens the card image and reads its layout and file table. Writable opens it so nothing else can use
        /// it meanwhile; read-only lets other programs keep it open.</summary>
        public Fat32Image(string path, bool writable)
        {
            _fs = new FileStream(path, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read,
                writable ? FileShare.None : FileShare.ReadWrite | FileShare.Delete);
            var b = ReadAt(0, 512);
            if (b[510] != 0x55 || b[511] != 0xAA) throw new InvalidDataException("This is not a FAT disk image.");
            _bps = BitConverter.ToUInt16(b, 11);
            _spc = b[13];
            _reserved = BitConverter.ToUInt16(b, 14);
            _numFats = b[16];
            _fatSize = BitConverter.ToUInt32(b, 36);
            _rootCluster = BitConverter.ToUInt32(b, 44);
            _fsInfoSector = BitConverter.ToUInt16(b, 48);
            uint totalSectors = BitConverter.ToUInt16(b, 19) != 0 ? BitConverter.ToUInt16(b, 19) : BitConverter.ToUInt32(b, 32);
            if (BitConverter.ToUInt16(b, 22) != 0 || _fatSize == 0 || _bps != 512 || _spc == 0)
                throw new InvalidDataException("This disk image is not FAT32.");
            _clusterBytes = _bps * _spc;
            _dataStart = (long)(_reserved + _numFats * _fatSize) * _bps;
            _totalClusters = (uint)((totalSectors - (_reserved + _numFats * _fatSize)) / _spc);
            var raw = ReadAt((long)_reserved * _bps, (int)(_fatSize * _bps));
            _fat = new uint[_totalClusters + 2];
            for (int i = 0; i < _fat.Length; i++) _fat[i] = BitConverter.ToUInt32(raw, i * 4) & MASK;
        }

        public void Dispose() => _fs.Dispose();

        public long FreeBytes => (long)CountFree() * _clusterBytes;
        public long TotalBytes => (long)_totalClusters * _clusterBytes;
        /// <summary>Space a file of this many bytes takes on the card (whole clusters).</summary>
        public long OnCard(long bytes) => (bytes + _clusterBytes - 1) / _clusterBytes * _clusterBytes;

        // ---------- reading ----------

        public Entry Find(string path)
        {
            var parts = Split(path);
            if (parts.Length == 0) return new Entry { Name = "", Attr = 0x10, FirstCluster = _rootCluster };
            uint dir = _rootCluster; Entry found = null;
            for (int i = 0; i < parts.Length; i++)
            {
                found = List(dir).FirstOrDefault(e => NameEquals(e, parts[i]));
                if (found == null) return null;
                if (i < parts.Length - 1)
                {
                    if (!found.IsDirectory) return null;
                    dir = found.FirstCluster == 0 ? _rootCluster : found.FirstCluster;
                }
            }
            return found;
        }

        public bool Exists(string path) => Find(path) != null;
        public long FileSize(string path) => Find(path)?.Size ?? 0;

        /// <summary>The whole of a file on the card. Throws if it isn't there, is a folder, or is cut short.</summary>
        public byte[] ReadFile(string path)
        {
            var e = Find(path) ?? throw new FileNotFoundException("Not on the SD card: " + path);
            if (e.IsDirectory) throw new IOException(path + " is a folder.");
            var data = new byte[e.Size];
            int pos = 0;
            foreach (var c in Chain(e.FirstCluster))
            {
                if (pos >= data.Length) break;
                int n = Math.Min(_clusterBytes, data.Length - pos);
                var chunk = ReadAt(ClusterOffset(c), n);
                Buffer.BlockCopy(chunk, 0, data, pos, n);
                pos += n;
            }
            if (pos < data.Length) throw new InvalidDataException("The file " + path + " is cut short on the SD card.");
            return data;
        }

        /// <summary>What's in a folder on the card (empty if there's no such folder).</summary>
        public List<Entry> ListFolder(string path)
        {
            var e = Find(path);
            if (e == null || !e.IsDirectory) return new List<Entry>();
            return List(e.FirstCluster == 0 ? _rootCluster : e.FirstCluster);
        }

        /// <summary>Reads a folder's entries, joining long names to their 8.3 entries; skips deleted ones, the volume
        /// label and "."/"..".</summary>
        List<Entry> List(uint dirCluster)
        {
            var result = new List<Entry>();
            var lfnParts = new SortedDictionary<int, string>();
            var lfnOffsets = new List<long>();
            byte lfnChecksum = 0;
            foreach (var c in Chain(dirCluster))
            {
                long baseOff = ClusterOffset(c);
                var buf = ReadAt(baseOff, _clusterBytes);
                for (int i = 0; i < _clusterBytes; i += 32)
                {
                    byte first = buf[i];
                    if (first == 0x00) return result;
                    if (first == 0xE5) { lfnParts.Clear(); lfnOffsets.Clear(); continue; }
                    byte attr = buf[i + 11];
                    if (attr == 0x0F)
                    {
                        int ord = first & 0x1F;
                        if ((first & 0x40) != 0) { lfnParts.Clear(); lfnOffsets.Clear(); lfnChecksum = buf[i + 13]; }
                        lfnParts[ord] = LfnChars(buf, i);
                        lfnOffsets.Add(baseOff + i);
                        continue;
                    }
                    if ((attr & 0x08) != 0) { lfnParts.Clear(); lfnOffsets.Clear(); continue; } // volume label
                    string sfn = Encoding.ASCII.GetString(buf, i, 11);
                    var e = new Entry
                    {
                        ShortName = sfn,
                        Attr = attr,
                        FirstCluster = ((uint)BitConverter.ToUInt16(buf, i + 20) << 16) | BitConverter.ToUInt16(buf, i + 26),
                        Size = BitConverter.ToUInt32(buf, i + 28),
                        DirCluster = dirCluster,
                        EntryOffset = baseOff + i,
                    };
                    if (lfnParts.Count > 0 && lfnChecksum == SfnChecksum(buf, i))
                    {
                        e.Name = string.Concat(lfnParts.Values);
                        e.LfnOffsets = new List<long>(lfnOffsets);
                    }
                    else e.Name = PrettyShort(buf, i);
                    lfnParts.Clear(); lfnOffsets.Clear();
                    if (e.Name != "." && e.Name != "..") result.Add(e);
                }
            }
            return result;
        }

        // ---------- writing ----------

        /// <summary>Writes a file, replacing it if it exists. Missing folders are created.</summary>
        public void WriteFile(string path, byte[] data)
        {
            var parts = Split(path);
            uint dir = EnsureFolders(parts.Take(parts.Length - 1));
            string name = parts[parts.Length - 1];
            var existing = List(dir).FirstOrDefault(e => NameEquals(e, name));
            if (existing != null && existing.IsDirectory) throw new IOException(path + " is a folder.");

            uint needed = (uint)((data.Length + _clusterBytes - 1) / _clusterBytes);
            uint reusable = existing != null ? (uint)Chain(existing.FirstCluster).Count() : 0;
            if (needed > CountFree() + reusable) throw new IOException("The SD card is full.");
            if (existing != null) FreeChain(existing.FirstCluster);
            uint first = needed == 0 ? 0 : Allocate(needed);
            int pos = 0;
            foreach (var c in Chain(first))
            {
                var chunk = new byte[_clusterBytes];
                int n = Math.Min(_clusterBytes, data.Length - pos);
                if (n > 0) Buffer.BlockCopy(data, pos, chunk, 0, n);
                WriteAt(ClusterOffset(c), chunk);
                pos += n;
            }

            if (existing != null)
            {
                var ent = ReadAt(existing.EntryOffset, 32);
                SetCluster(ent, first);
                BitConverter.GetBytes((uint)data.Length).CopyTo(ent, 28);
                StampTime(ent, false);
                WriteAt(existing.EntryOffset, ent);
            }
            else
            {
                AddEntry(dir, name, 0x20, first, (uint)data.Length);
            }
            FlushFat();
        }

        /// <summary>Deletes a file. Does nothing if it is not there.</summary>
        public bool DeleteFile(string path)
        {
            var e = Find(path);
            if (e == null) return false;
            if (e.IsDirectory) throw new IOException(path + " is a folder.");
            FreeChain(e.FirstCluster);
            foreach (var off in e.LfnOffsets.Concat(new[] { e.EntryOffset })) WriteAt(off, new byte[] { 0xE5 });
            FlushFat();
            return true;
        }

        /// <summary>Walks down a folder path from the top of the card, making any folder that's missing; returns the
        /// last one's cluster.</summary>
        uint EnsureFolders(IEnumerable<string> folders)
        {
            uint dir = _rootCluster;
            foreach (var f in folders)
            {
                var e = List(dir).FirstOrDefault(x => NameEquals(x, f));
                if (e != null)
                {
                    if (!e.IsDirectory) throw new IOException(f + " is a file, not a folder.");
                    dir = e.FirstCluster == 0 ? _rootCluster : e.FirstCluster;
                    continue;
                }
                uint c = Allocate(1);
                var buf = new byte[_clusterBytes];
                MakeDotEntry(buf, 0, ".          ", c);
                MakeDotEntry(buf, 32, "..         ", dir == _rootCluster ? 0 : dir);
                WriteAt(ClusterOffset(c), buf);
                AddEntry(dir, f, 0x10, c, 0);
                FlushFat();
                dir = c;
            }
            return dir;
        }

        /// <summary>Writes a new folder's "." or ".." entry into its first cluster.</summary>
        void MakeDotEntry(byte[] buf, int at, string name, uint cluster)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(buf, at);
            buf[at + 11] = 0x10;
            var e = new byte[32]; Array.Copy(buf, at, e, 0, 32);
            SetCluster(e, cluster); StampTime(e, true);
            Array.Copy(e, 0, buf, at, 32);
        }

        /// <summary>Adds a file or folder entry to a folder: a unique 8.3 name, plus long-name entries when the name
        /// doesn't fit 8.3.</summary>
        void AddEntry(uint dir, string longName, byte attr, uint firstCluster, uint size)
        {
            var siblings = List(dir);
            string sfn = MakeShortName(longName, siblings.Select(s => s.ShortName));
            var sfnBytes = Encoding.ASCII.GetBytes(sfn);
            bool needLfn = PrettyShortFromSfn(sfn) != longName;
            var entries = new List<byte[]>();
            if (needLfn)
            {
                byte sum = SfnChecksum(sfnBytes, 0);
                int count = (longName.Length + 12) / 13;
                for (int k = count; k >= 1; k--)
                {
                    var lfn = new byte[32];
                    lfn[0] = (byte)(k | (k == count ? 0x40 : 0));
                    lfn[11] = 0x0F; lfn[13] = sum;
                    int[] pos = { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 };
                    for (int j = 0; j < 13; j++)
                    {
                        int ci = (k - 1) * 13 + j;
                        ushort ch = ci < longName.Length ? longName[ci] : (ci == longName.Length ? (ushort)0 : (ushort)0xFFFF);
                        BitConverter.GetBytes(ch).CopyTo(lfn, pos[j]);
                    }
                    entries.Add(lfn);
                }
            }
            var main = new byte[32];
            sfnBytes.CopyTo(main, 0);
            main[11] = attr;
            SetCluster(main, firstCluster);
            BitConverter.GetBytes(size).CopyTo(main, 28);
            StampTime(main, true);
            entries.Add(main);

            var offsets = FindFreeSlots(dir, entries.Count);
            for (int i = 0; i < entries.Count; i++) WriteAt(offsets[i], entries[i]);
        }

        // Consecutive free 32-byte slots in a folder; the folder grows by a cluster if needed.
        List<long> FindFreeSlots(uint dir, int count)
        {
            var run = new List<long>();
            uint last = dir;
            foreach (var c in Chain(dir))
            {
                last = c;
                long baseOff = ClusterOffset(c);
                var buf = ReadAt(baseOff, _clusterBytes);
                for (int i = 0; i < _clusterBytes; i += 32)
                {
                    if (buf[i] == 0x00 || buf[i] == 0xE5)
                    {
                        run.Add(baseOff + i);
                        if (run.Count == count) return run;
                    }
                    else run.Clear();
                }
            }
            // grow the folder
            uint added = Allocate(1);
            _fat[last] = added;
            WriteAt(ClusterOffset(added), new byte[_clusterBytes]);
            FlushFat();
            long b = ClusterOffset(added);
            for (int i = 0; run.Count < count; i += 32) run.Add(b + i);
            return run;
        }

        // ---------- clusters ----------

        IEnumerable<uint> Chain(uint first)
        {
            if (first < 2) yield break;
            var seen = new HashSet<uint>();
            uint c = first;
            while (c >= 2 && c < 0x0FFFFFF8)
            {
                if (c >= _fat.Length || !seen.Add(c)) throw new InvalidDataException("The SD card's file table is damaged.");
                yield return c;
                c = _fat[c];
            }
        }

        /// <summary>How many clusters are free.</summary>
        uint CountFree()
        {
            uint n = 0;
            for (uint i = 2; i < _fat.Length; i++) if (_fat[i] == 0) n++;
            return n;
        }

        /// <summary>Takes this many free clusters (searching on from the last one taken) and links them into a chain;
        /// returns the first. Only changes the table in memory: FlushFat saves it.</summary>
        uint Allocate(uint count)
        {
            var got = new List<uint>();
            uint start = _nextFreeHint < 2 || _nextFreeHint >= _fat.Length ? 2 : _nextFreeHint;
            for (uint pass = 0, i = start; got.Count < count && pass < _fat.Length; pass++, i++)
            {
                if (i >= _fat.Length) i = 2;
                if (_fat[i] == 0) got.Add(i);
            }
            if (got.Count < count) throw new IOException("The SD card is full.");
            for (int k = 0; k < got.Count; k++) _fat[got[k]] = k + 1 < got.Count ? got[k + 1] : EOC;
            _nextFreeHint = got[got.Count - 1] + 1;
            return got[0];
        }

        /// <summary>Marks every cluster of a chain free (in memory: FlushFat saves it).</summary>
        void FreeChain(uint first)
        {
            foreach (var c in Chain(first).ToList()) _fat[c] = 0;
        }

        /// <summary>Writes the table in memory to every FAT copy and updates the free count in the FSInfo
        /// sector.</summary>
        void FlushFat()
        {
            var raw = ReadAt((long)_reserved * _bps, (int)(_fatSize * _bps));
            for (int i = 0; i < _fat.Length; i++)
            {
                uint old = BitConverter.ToUInt32(raw, i * 4);
                uint v = (old & ~MASK) | (_fat[i] & MASK);   // keep the reserved top 4 bits
                BitConverter.GetBytes(v).CopyTo(raw, i * 4);
            }
            for (int f = 0; f < _numFats; f++) WriteAt((long)(_reserved + f * _fatSize) * _bps, raw);
            if (_fsInfoSector > 0 && _fsInfoSector < _reserved)
            {
                long off = (long)_fsInfoSector * _bps;
                var fsi = ReadAt(off, 512);
                if (BitConverter.ToUInt32(fsi, 0) == 0x41615252 && BitConverter.ToUInt32(fsi, 484) == 0x61417272)
                {
                    BitConverter.GetBytes(CountFree()).CopyTo(fsi, 488);
                    BitConverter.GetBytes(_nextFreeHint < _fat.Length ? _nextFreeHint : 0xFFFFFFFF).CopyTo(fsi, 492);
                    WriteAt(off, fsi);
                }
            }
            if (_pending == null) _fs.Flush(true);
        }

        long ClusterOffset(uint c) => _dataStart + (long)(c - 2) * _clusterBytes;

        // ---------- names ----------

        static string[] Split(string path) => path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>True when an entry has this name, long or 8.3 (case doesn't matter).</summary>
        static bool NameEquals(Entry e, string name) =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(PrettyShortFromSfn(e.ShortName), name, StringComparison.OrdinalIgnoreCase);

        /// <summary>The (up to 13) letters held in one long-name entry.</summary>
        static string LfnChars(byte[] b, int i)
        {
            var sb = new StringBuilder();
            foreach (int p in new[] { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 })
            {
                ushort ch = BitConverter.ToUInt16(b, i + p);
                if (ch == 0 || ch == 0xFFFF) break;
                sb.Append((char)ch);
            }
            return sb.ToString();
        }

        /// <summary>The checksum of an 8.3 name that its long-name entries carry, so they can be matched to
        /// it.</summary>
        static byte SfnChecksum(byte[] b, int i)
        {
            byte sum = 0;
            for (int k = 0; k < 11; k++) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + b[i + k]);
            return sum;
        }

        static string PrettyShort(byte[] b, int i) => PrettyShortFromSfn(Encoding.ASCII.GetString(b, i, 11));

        /// <summary>"NAME    EXT" as "NAME.EXT".</summary>
        static string PrettyShortFromSfn(string sfn)
        {
            if (sfn == null) return null;
            string n = sfn.Substring(0, 8).TrimEnd(), x = sfn.Substring(8, 3).TrimEnd();
            return x.Length > 0 ? n + "." + x : n;
        }

        /// <summary>An 8.3 name for a new entry that no other entry in the folder has: the name itself if it fits, else
        /// NAME~1.EXT and so on.</summary>
        static string MakeShortName(string longName, IEnumerable<string> taken)
        {
            var set = new HashSet<string>(taken.Where(t => t != null));
            string upper = longName.ToUpperInvariant();
            int dot = upper.LastIndexOf('.');
            string bas = dot > 0 ? upper.Substring(0, dot) : upper;
            string ext = dot > 0 ? upper.Substring(dot + 1) : "";
            string Clean(string s) => new string(s.Where(ch => ch < 128 && (char.IsLetterOrDigit(ch) || "$%'-_@~`!(){}^#&".IndexOf(ch) >= 0)).ToArray());
            bas = Clean(bas); ext = Clean(ext);
            if (ext.Length > 3) ext = ext.Substring(0, 3);
            if (bas.Length == 0) bas = "FILE";
            // the name may fit 8.3 exactly
            if (bas.Length <= 8 && bas + (ext.Length > 0 ? "." + ext : "") == upper)
            {
                string exact = bas.PadRight(8) + ext.PadRight(3);
                if (!set.Contains(exact)) return exact;
            }
            for (int n = 1; n < 1000000; n++)
            {
                string tail = "~" + n;
                string b = bas.Length + tail.Length > 8 ? bas.Substring(0, 8 - tail.Length) : bas;
                string cand = (b + tail).PadRight(8) + ext.PadRight(3);
                if (!set.Contains(cand)) return cand;
            }
            throw new IOException("Too many files with similar names.");
        }

        /// <summary>Puts a first cluster number into a 32-byte entry (it's split in two halves).</summary>
        static void SetCluster(byte[] e, uint c)
        {
            BitConverter.GetBytes((ushort)(c >> 16)).CopyTo(e, 20);
            BitConverter.GetBytes((ushort)(c & 0xFFFF)).CopyTo(e, 26);
        }

        /// <summary>Sets an entry's changed and last-used dates to now (and its created date too for a new
        /// one).</summary>
        static void StampTime(byte[] e, bool created)
        {
            var t = DateTime.Now;
            ushort time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            ushort date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
            if (created) { BitConverter.GetBytes(time).CopyTo(e, 14); BitConverter.GetBytes(date).CopyTo(e, 16); }
            BitConverter.GetBytes(date).CopyTo(e, 18);
            BitConverter.GetBytes(time).CopyTo(e, 22);
            BitConverter.GetBytes(date).CopyTo(e, 24);
        }

        // ---------- deferred writing ----------

        const int Block = 4096;
        Dictionary<long, byte[]> _pending;   // block number -> its new bytes; null: writes go straight to the card

        int BlockLength(long block) => (int)Math.Min(Block, _fs.Length - block * Block);

        /// <summary>Writes the changes kept in memory to the card. First the journal: every block of the card about to
        /// change, as it is now, saved and flushed to disk; then the blocks; then the journal is marked written. If anything
        /// goes wrong after the journal is saved, CardJournal.RollBack puts the card back exactly. Returns the blocks written.</summary>
        public int Commit(string journalPath, Action<int> progress)
        {
            if (_pending == null) return 0;
            var changed = new List<KeyValuePair<long, byte[]>>();   // offset -> the bytes there now
            foreach (var kv in _pending.OrderBy(k => k.Key))
            {
                var now = ReadRaw(kv.Key * Block, kv.Value.Length);
                if (!now.SequenceEqual(kv.Value)) changed.Add(new KeyValuePair<long, byte[]>(kv.Key * Block, now));
            }
            CardJournal.Write(journalPath, _fs.Name, _fs.Length, changed);
            int i = 0;
            foreach (var kv in changed)
            {
                WriteRaw(kv.Key, _pending[kv.Key / Block]);
                if (++i % 64 == 0) progress?.Invoke(i * 100 / changed.Count);
            }
            _fs.Flush(true);
            CardJournal.SetState(journalPath, CardJournal.Written);
            _pending.Clear();
            return changed.Count;
        }

        // ---------- raw I/O ----------

        byte[] ReadAt(long off, int n)
        {
            var b = ReadRaw(off, n);
            if (_pending != null && _pending.Count > 0 && n > 0)
                for (long blk = off / Block; blk <= (off + n - 1) / Block; blk++)
                {
                    if (!_pending.TryGetValue(blk, out var nb)) continue;
                    long start = Math.Max(off, blk * Block), end = Math.Min(off + n, blk * Block + nb.Length);
                    if (end > start) Buffer.BlockCopy(nb, (int)(start - blk * Block), b, (int)(start - off), (int)(end - start));
                }
            return b;
        }

        /// <summary>Writes to the card, or, when changes are kept for Commit, into the copies of the blocks in
        /// memory.</summary>
        void WriteAt(long off, byte[] b)
        {
            if (_pending == null) { WriteRaw(off, b); return; }
            if (b.Length == 0) return;
            for (long blk = off / Block; blk <= (off + b.Length - 1) / Block; blk++)
            {
                if (!_pending.TryGetValue(blk, out var nb)) _pending[blk] = nb = ReadRaw(blk * Block, BlockLength(blk));
                long start = Math.Max(off, blk * Block), end = Math.Min(off + b.Length, blk * Block + nb.Length);
                if (end > start) Buffer.BlockCopy(b, (int)(start - off), nb, (int)(start - blk * Block), (int)(end - start));
            }
        }

        /// <summary>Reads straight from the image file, ignoring changes kept in memory.</summary>
        byte[] ReadRaw(long off, int n)
        {
            var b = new byte[n];
            _fs.Position = off;
            int got = 0;
            while (got < n)
            {
                int r = _fs.Read(b, got, n - got);
                if (r <= 0) throw new EndOfStreamException("The SD card image ends early.");
                got += r;
            }
            return b;
        }

        /// <summary>Writes straight to the image file.</summary>
        void WriteRaw(long off, byte[] b)
        {
            _fs.Position = off;
            _fs.Write(b, 0, b.Length);
        }
    }

    /// <summary>What a save replaced on the SD card, block by block: written (and flushed) before the card is touched, so a
    /// save that stops halfway can be put back, and kept afterwards for "Undo last save". A few MB instead of a copy of the
    /// whole card. State: 1 = being written to the card, 2 = on the card, 3 = the mods list saved too.</summary>
    public static class CardJournal
    {
        public const int Writing = 1, Written = 2, Done = 3;
        const uint Magic = 0x4A434350;   // "PCCJ"

        /// <summary>Saves the journal: which card it's for, its size, and each block's bytes as they are now. Written
        /// through to disk.</summary>
        public static void Write(string path, string cardPath, long cardLength, List<KeyValuePair<long, byte[]>> blocks)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(Magic); w.Write(1); w.Write(Writing); w.Write(cardLength); w.Write(Path.GetFullPath(cardPath)); w.Write(blocks.Count);
                foreach (var kv in blocks) { w.Write(kv.Key); w.Write(kv.Value.Length); w.Write(kv.Value); }
                w.Write(Magic);
                w.Flush(); fs.Flush(true);
            }
        }

        /// <summary>Changes a journal's state (Writing, Written or Done) in place, written through to disk.</summary>
        public static void SetState(string path, int state)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Position = 8;
                fs.Write(BitConverter.GetBytes(state), 0, 4);
                fs.Flush(true);
            }
        }

        /// <summary>The journal's state, or 0 when there is none (or it was never finished being written: the card
        /// wasn't touched then).</summary>
        public static int State(string path, out string cardPath, out long cardLength)
        {
            cardPath = null; cardLength = 0;
            try
            {
                if (!File.Exists(path)) return 0;
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != Magic || r.ReadInt32() != 1) return 0;
                    int state = r.ReadInt32(); cardLength = r.ReadInt64(); cardPath = r.ReadString();
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) { r.ReadInt64(); int len = r.ReadInt32(); r.BaseStream.Seek(len, SeekOrigin.Current); }
                    return r.ReadUInt32() == Magic ? state : 0;
                }
            }
            catch { return 0; }
        }

        /// <summary>Puts every block the journal saved back on the card: the card is then exactly as it was before.</summary>
        public static void RollBack(string path, string cardPath)
        {
            if (State(path, out _, out long length) == 0) throw new InvalidDataException("The record of the last save is damaged, so it can't be put back.");
            using (var r = new BinaryReader(File.OpenRead(path)))
            using (var card = new FileStream(cardPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (card.Length != length) throw new InvalidOperationException("The SD card isn't the one that save was made on.");
                r.ReadUInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt64(); r.ReadString();
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    long off = r.ReadInt64(); int len = r.ReadInt32(); var b = r.ReadBytes(len);
                    card.Position = off; card.Write(b, 0, b.Length);
                }
                card.Flush(true);
            }
        }
    }
}
