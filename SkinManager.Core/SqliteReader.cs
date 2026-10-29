// A small read-only reader for SQLite database files (the format is public: sqlite.org/fileformat.html), so the app can
// read Brawl Online's match history without any extra library. It only ever opens files for reading. Recent changes
// that Brawl Online hasn't folded into the main file yet (the "-wal" file next to it) are read too.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkinManager.Core
{
    public sealed class SqliteReader
    {
        readonly byte[] _db;
        readonly Dictionary<uint, byte[]> _wal = new Dictionary<uint, byte[]>();
        readonly int _pageSize, _usable;
        readonly Dictionary<string, (uint Root, List<string> Columns)> _tables = new Dictionary<string, (uint, List<string>)>(StringComparer.OrdinalIgnoreCase);

        public static SqliteReader Open(string path)
        {
            byte[] db = ReadShared(path), wal = null;
            try { if (File.Exists(path + "-wal")) wal = ReadShared(path + "-wal"); } catch { }
            return new SqliteReader(db, wal);
        }

        /// <summary>The whole file, read while the program that owns it keeps it open.</summary>
        static byte[] ReadShared(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }

        public SqliteReader(byte[] db, byte[] wal = null)
        {
            if (db.Length < 100 || Encoding.ASCII.GetString(db, 0, 15) != "SQLite format 3") throw new InvalidDataException("That isn't an SQLite database.");
            _db = db;
            _pageSize = db[16] << 8 | db[17]; if (_pageSize == 1) _pageSize = 65536;
            _usable = _pageSize - db[20];
            if (wal != null) ReadWal(wal);
            foreach (var row in Rows(1, null))
                if (row.Count >= 5 && row[0] as string == "table" && row[3] is long root)
                    _tables[(string)row[1]] = ((uint)root, Columns(row[4] as string ?? ""));
        }

        /// <summary>The newest committed copy of each page in the write-ahead log.</summary>
        void ReadWal(byte[] wal)
        {
            if (wal.Length < 32) return;
            uint magic = U32(wal, 0);
            if (magic != 0x377f0682 && magic != 0x377f0683) return;
            int ps = (int)U32(wal, 8);
            if (ps != _pageSize) return;
            uint salt1 = U32(wal, 16), salt2 = U32(wal, 20);
            var pending = new Dictionary<uint, byte[]>();
            for (int at = 32; at + 24 + ps <= wal.Length; at += 24 + ps)
            {
                uint page = U32(wal, at), commit = U32(wal, at + 4);
                if (U32(wal, at + 8) != salt1 || U32(wal, at + 12) != salt2) break;   // left over from an older log
                var data = new byte[ps]; Buffer.BlockCopy(wal, at + 24, data, 0, ps);
                pending[page] = data;
                if (commit != 0) { foreach (var kv in pending) _wal[kv.Key] = kv.Value; pending.Clear(); }
            }
        }

        byte[] Page(uint n, out int offset)
        {
            offset = n == 1 ? 100 : 0;   // page 1 starts with the file header
            if (_wal.TryGetValue(n, out var p)) return p;
            long at = (long)(n - 1) * _pageSize;
            if (at + _pageSize > _db.Length) throw new InvalidDataException("The database refers past its end.");
            var r = new byte[_pageSize]; Buffer.BlockCopy(_db, (int)at, r, 0, _pageSize); return r;
        }

        public bool HasTable(string name) => _tables.ContainsKey(name);
        public IReadOnlyList<string> ColumnsOf(string table) => _tables[table].Columns;

        /// <summary>Every row of a table as column name -> value (long, double, string, byte[] or null).</summary>
        public IEnumerable<Dictionary<string, object>> Table(string name)
        {
            if (!_tables.TryGetValue(name, out var t)) yield break;
            foreach (var row in Rows(t.Root, null))
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                long rowid = (long)row[row.Count - 1];
                for (int i = 0; i < t.Columns.Count; i++) d[t.Columns[i]] = i < row.Count - 1 ? row[i] : null;
                // an "integer primary key" column is stored as the row's id
                foreach (var c in t.Columns) if (d[c] == null && (c == "_id" || c.Equals("id", StringComparison.OrdinalIgnoreCase))) d[c] = rowid;
                yield return d;
            }
        }

        /// <summary>Rows of a table b-tree (values, then the row id last).</summary>
        IEnumerable<List<object>> Rows(uint page, HashSet<uint> seen)
        {
            seen = seen ?? new HashSet<uint>();
            if (!seen.Add(page)) yield break;   // a damaged file pointing in a circle
            var p = Page(page, out int h);
            byte type = p[h];
            int cells = p[h + 3] << 8 | p[h + 4];
            int ptrs = h + (type == 0x05 || type == 0x02 ? 12 : 8);
            if (type == 0x05)
            {
                for (int i = 0; i < cells; i++)
                {
                    int c = p[ptrs + 2 * i] << 8 | p[ptrs + 2 * i + 1];
                    foreach (var r in Rows(U32(p, c), seen)) yield return r;
                }
                foreach (var r in Rows(U32(p, h + 8), seen)) yield return r;
            }
            else if (type == 0x0D)
            {
                for (int i = 0; i < cells; i++)
                {
                    int c = p[ptrs + 2 * i] << 8 | p[ptrs + 2 * i + 1];
                    long size = Varint(p, ref c), rowid = Varint(p, ref c);
                    var payload = Payload(p, c, size);
                    var values = Record(payload);
                    values.Add(rowid);
                    yield return values;
                }
            }
        }

        byte[] Payload(byte[] p, int at, long size)
        {
            int x = _usable - 35;
            if (size <= x) { var all = new byte[size]; Buffer.BlockCopy(p, at, all, 0, (int)size); return all; }
            int m = (_usable - 12) * 32 / 255 - 23;
            int k = (int)(m + (size - m) % (_usable - 4));
            int local = k <= x ? k : m;
            var r = new byte[size];
            Buffer.BlockCopy(p, at, r, 0, local);
            int done = local; uint next = U32(p, at + local);
            var seen = new HashSet<uint>();
            while (done < size && next != 0 && seen.Add(next))
            {
                var o = Page(next, out _);
                int n = (int)Math.Min(size - done, _usable - 4);
                Buffer.BlockCopy(o, 4, r, done, n); done += n;
                next = U32(o, 0);
            }
            return r;
        }

        static List<object> Record(byte[] r)
        {
            int at = 0;
            long headerSize = Varint(r, ref at);
            var types = new List<long>();
            while (at < headerSize) types.Add(Varint(r, ref at));
            int pos = (int)headerSize;
            var values = new List<object>();
            foreach (var t in types)
            {
                switch (t)
                {
                    case 0: values.Add(null); break;
                    case 1: values.Add((long)(sbyte)r[pos]); pos += 1; break;
                    case 2: values.Add((long)(short)(r[pos] << 8 | r[pos + 1])); pos += 2; break;
                    case 3: { long v = r[pos] << 16 | r[pos + 1] << 8 | r[pos + 2]; if ((v & 0x800000) != 0) v -= 0x1000000; values.Add(v); pos += 3; break; }
                    case 4: values.Add((long)(int)U32(r, pos)); pos += 4; break;
                    case 5: { long v = 0; for (int i = 0; i < 6; i++) v = v << 8 | r[pos + i]; if ((v & 0x800000000000) != 0) v -= 0x1000000000000; values.Add(v); pos += 6; break; }
                    case 6: { long v = 0; for (int i = 0; i < 8; i++) v = v << 8 | r[pos + i]; values.Add(v); pos += 8; break; }
                    case 7: { long v = 0; for (int i = 0; i < 8; i++) v = v << 8 | r[pos + i]; values.Add(BitConverter.Int64BitsToDouble(v)); pos += 8; break; }
                    case 8: values.Add(0L); break;
                    case 9: values.Add(1L); break;
                    default:
                        int n = (int)((t - (t % 2 == 0 ? 12 : 13)) / 2);
                        if (t % 2 == 0) { var b = new byte[n]; Buffer.BlockCopy(r, pos, b, 0, n); values.Add(b); }
                        else values.Add(Encoding.UTF8.GetString(r, pos, n));
                        pos += n; break;
                }
            }
            return values;
        }

        /// <summary>Column names, in order, from a CREATE TABLE statement.</summary>
        static List<string> Columns(string sql)
        {
            var r = new List<string>();
            int open = sql.IndexOf('('), close = sql.LastIndexOf(')');
            if (open < 0 || close <= open) return r;
            string body = sql.Substring(open + 1, close - open - 1);
            int depth = 0, start = 0;
            var parts = new List<string>();
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '(') depth++; else if (body[i] == ')') depth--;
                else if (body[i] == ',' && depth == 0) { parts.Add(body.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(body.Substring(start));
            foreach (var raw in parts)
            {
                var d = raw.Trim();
                string first = d.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                string lower = first.ToLowerInvariant();
                if (lower == "constraint" || lower == "primary" || lower == "unique" || lower == "foreign" || lower == "check") continue;
                r.Add(first.Trim('"', '`', '[', ']', '\''));
            }
            return r;
        }

        static long Varint(byte[] b, ref int at)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) { byte x = b[at++]; v = v << 7 | (long)(x & 0x7f); if ((x & 0x80) == 0) return v; }
            return v << 8 | b[at++];
        }

        static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
    }
}
