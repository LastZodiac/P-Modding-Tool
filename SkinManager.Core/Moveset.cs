// Reads a fighter's moveset (Fit<Name>.pac, the "Misc Data" entry: Brawl's fighter definition) without BrawlLib's moveset
// reader (it crashes on P+'s files). Only what frame data needs: each subaction's animation and its main script.
// The layout follows BrawlLib's own struct definitions (SSBB/Types/FighterDefinition.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkinManager.Core
{
    public sealed class MoveEvent
    {
        public uint Id;                 // e.g. 0x06000D00 Offensive Collision
        public int[] Types, Values;     // argument types (0 value, 1 scalar, 2 pointer, 3 bool...) and raw values
        public int Offset;              // where it is in the file (relative to the data)
        public float Scalar(int i) => Values[i] / 60000f;
        public int Hi(int i) => (int)((uint)Values[i] >> 16);
        public int Lo(int i) => Values[i] & 0xFFFF;
        public override string ToString() => $"{Id:X8}(" + string.Join(", ", Values.Select((v, i) => Types[i] == 1 ? (v / 60000f).ToString("0.###") : v.ToString())) + ")";
    }

    public sealed class Subaction
    {
        public int Index;
        public string Animation;       // its animation's name (the CHR0 in the motion file)
        public byte Flags;
        public int ScriptOffset;
        public List<MoveEvent> Main = new List<MoveEvent>();
        public List<MoveEvent> Gfx = new List<MoveEvent>(), Sfx = new List<MoveEvent>(), Other = new List<MoveEvent>();
    }

    /// <summary>A fighter's article: a projectile or a separate object (Fox's laser, Link's arrow, Fox's side B image...).</summary>
    public sealed class Article
    {
        public int Offset, Id, Group, Bone, Slot;   // Slot: its place in the fighter-specific offsets (-1 = the entry article)
        public List<Subaction> Subactions = new List<Subaction>();
        public bool HasHitbox => Subactions.Any(s => s.Main.Any(e => e.Id == 0x06000D00 || e.Id == 0x06150F00));
    }

    public sealed class Moveset
    {
        readonly byte[] _d;            // the file data, from 0x20 on (offsets are relative to it)
        public List<Subaction> Subactions = new List<Subaction>();
        public Dictionary<string, int> Sections = new Dictionary<string, int>();
        public int SubactionFlagsAt, MainAt, MiscAt, AttributesAt;
        /// <summary>Fighter-specific actions (0x112 on): their entry scripts (some specials make their hitboxes here).</summary>
        public List<List<MoveEvent>> ActionEntry = new List<List<MoveEvent>>();
        public List<Article> Articles = new List<Article>();
        /// <summary>A fighter attribute (0x00..0x2DC), as a float or an int.</summary>
        public float Attr(int off) => F(AttributesAt + off);
        public int AttrInt(int off) => I(AttributesAt + off);
        public List<(float X, float Y, float Z, float SX, float SY, float SZ, float Radius, int Bone, int Zone)> HurtBoxes = new List<(float, float, float, float, float, float, float, int, int)>();

        /// <summary>Model visibility: [reference][switch][group] = bone indices; and each switch's group shown by default.</summary>
        public List<List<List<int>>>[] Visibility = { new List<List<List<int>>>(), new List<List<List<int>>>() };
        public Dictionary<int, int> VisibilityDefaults = new Dictionary<int, int>();

        /// <summary>The bones hidden when the fighter stands normally (the other switch groups: Fox's blaster, blinking eyes...).</summary>
        public HashSet<int> DefaultHidden()
        {
            var hidden = new HashSet<int>();
            if (Visibility[0].Count == 0) return hidden;
            for (int sw = 0; sw < Visibility[0].Count; sw++) SetGroup(hidden, 0, sw, VisibilityDefaults.TryGetValue(sw, out int g) ? g : -1);
            return hidden;
        }

        /// <summary>Shows one group of a switch, hiding its others (the "Model Changer" event).</summary>
        public void SetGroup(HashSet<int> hidden, int reference, int sw, int group)
        {
            if (reference < 0 || reference > 1 || sw < 0 || sw >= Visibility[reference].Count) return;
            var groups = Visibility[reference][sw];
            foreach (var gr in groups) foreach (var b in gr) hidden.Add(b);
            if (group >= 0 && group < groups.Count) foreach (var b in groups[group]) hidden.Remove(b);
        }

        int I(int at) => at < 0 || at + 4 > _d.Length ? 0 : (_d[at] << 24 | _d[at + 1] << 16 | _d[at + 2] << 8 | _d[at + 3]);
        float F(int at) => BitConverter.ToSingle(BitConverter.GetBytes(I(at)), 0);
        string Str(int at) { if (at < 0 || at >= _d.Length) return null; int e = at; while (e < _d.Length && _d[e] != 0) e++; return Encoding.ASCII.GetString(_d, at, e - at); }

        /// <summary>From the moveset entry's bytes (a fighter definition: a 0x20 header, then the data).</summary>
        public Moveset(byte[] file)
        {
            int size = BE(file, 0), lookup = BE(file, 4), lookupCount = BE(file, 8), tableCount = BE(file, 12), extCount = BE(file, 16);
            _d = new byte[file.Length - 0x20]; Buffer.BlockCopy(file, 0x20, _d, 0, _d.Length);
            int table = lookup + lookupCount * 4, strings = table + (tableCount + extCount) * 8;
            for (int i = 0; i < tableCount; i++) { int off = I(table + i * 8), str = I(table + i * 8 + 4); var name = Str(strings + str); if (name != null) Sections[name] = off; }
            if (!Sections.TryGetValue("data", out int data)) throw new InvalidOperationException("No 'data' section in this moveset.");
            SubactionFlagsAt = I(data + 0); AttributesAt = I(data + 8); MiscAt = I(data + 16); MainAt = I(data + 48);
            // every offset in the header, sorted: the subaction tables end where the next thing begins
            var starts = new SortedSet<int>(Enumerable.Range(0, 27).Select(k => I(data + k * 4)).Where(o => o > 0));
            int Next(int o) => starts.Where(x => x > o).DefaultIfEmpty(_d.Length).Min();
            int count = (Next(MainAt) - MainAt) / 4;
            int flagsCount = (Next(SubactionFlagsAt) - SubactionFlagsAt) / 8;
            count = Math.Min(count, flagsCount > 0 ? flagsCount : count);
            for (int i = 0; i < count; i++)
            {
                var s = new Subaction { Index = i, Flags = _d[SubactionFlagsAt + i * 8 + 1], ScriptOffset = I(MainAt + i * 4) };
                int nameAt = I(SubactionFlagsAt + i * 8 + 4);
                s.Animation = nameAt > 0 ? Str(nameAt) : null;
                if (s.ScriptOffset > 0) s.Main = Script(s.ScriptOffset, 0);
                int go = I(I(data + 52) + i * 4), so = I(I(data + 56) + i * 4), oo = I(I(data + 60) + i * 4);
                if (I(data + 52) > 0 && go > 0) s.Gfx = Script(go, 0);
                if (I(data + 56) > 0 && so > 0) s.Sfx = Script(so, 0);
                if (I(data + 60) > 0 && oo > 0) s.Other = Script(oo, 0);
                Subactions.Add(s);
            }
            // articles: in the fighter-specific offsets after the header (and the entry/static ones)
            try { ReadArticles(data, starts); } catch { }
            // fighter-specific actions' entry scripts
            int entryAt = I(data + 36), exitAt = I(data + 40);
            if (entryAt > 0)
            {
                int n = (Next(entryAt) - entryAt) / 4;
                for (int k = 0; k < n && k < 400; k++) { int o = I(entryAt + k * 4); ActionEntry.Add(o > 0 ? Script(o, 0) : new List<MoveEvent>()); }
            }
            // model visibility: header (entries, switch count, defaults, defaults count)
            int vis = I(data + 4);
            if (vis > 0)
            {
                int entries = I(vis), swCount = I(vis + 4), defaults = I(vis + 8), defCount = I(vis + 12);
                int refs = entries == 0 ? 0 : ((defaults == 0 ? vis : defaults) - entries) / 4;
                for (int r = 0; r < refs && r < 2; r++)
                {
                    int at = I(entries + r * 4);
                    if (at <= 0) continue;
                    for (int sw = 0; sw < swCount && sw < 64; sw++)
                    {
                        int gAt = I(at + sw * 8), gCount = I(at + sw * 8 + 4);
                        var groups = new List<List<int>>();
                        for (int g = 0; g < gCount && g < 64; g++)
                        {
                            int bAt = I(gAt + g * 8), bCount = I(gAt + g * 8 + 4);
                            var bones = new List<int>();
                            for (int b = 0; b < bCount && b < 256; b++) bones.Add(I(bAt + b * 4));
                            groups.Add(bones);
                        }
                        Visibility[r].Add(groups);
                    }
                }
                for (int k = 0; k < defCount && k < 64; k++) VisibilityDefaults[I(defaults + k * 8)] = I(defaults + k * 8 + 4);
            }
            // hurtboxes: misc section +12 offset, +16 count; each 0x20 bytes
            if (MiscAt > 0)
            {
                int hb = I(MiscAt + 12), n = I(MiscAt + 16);
                for (int k = 0; k < n && k < 64; k++)
                {
                    int a = hb + k * 0x20;
                    int bits = _d[a + 0x1C] << 8 | _d[a + 0x1D];
                    HurtBoxes.Add((F(a), F(a + 4), F(a + 8), F(a + 12), F(a + 16), F(a + 20), F(a + 24), (bits >> 7) & 0x1FF, (bits >> 3) & 3));
                }
            }
        }

        void ReadArticles(int data, SortedSet<int> starts)
        {
            var all = new SortedSet<int>(starts);
            var candidates = new List<int>();
            int end = int.MaxValue;
            foreach (var o in starts) if (o > data) { end = o; break; }
            for (int at = data + 124; at + 4 <= _d.Length && at < end && candidates.Count < 200; at += 4)
            {
                int v = I(at);
                candidates.Add(v);
                if (v > data && v < end) end = v;
                if (v > 0) all.Add(v);
            }
            if (Environment.GetEnvironmentVariable("SKINMANAGER_DEBUG_ARTICLES") != null) Console.WriteLine("extras: " + string.Join(" ", candidates.Select((v, k) => k + ":" + v)));
            bool Ok(int o) => o >= 0 && o < _d.Length && o % 4 == 0;
            var found = new List<Article>();
            for (int slot = 0; slot < candidates.Count; slot++)
            {
                int at = candidates[slot];
                if (Environment.GetEnvironmentVariable("SKINMANAGER_DEBUG_ARTICLES") != null && at > 0 && at + 52 <= _d.Length) Console.WriteLine($"slot {slot} @{at}: " + string.Join(" ", Enumerable.Range(0, 13).Select(k => I(at + k * 4))));
                if (at <= 0 || at + 52 > _d.Length || at % 4 != 0) continue;
                int id = I(at), grp = I(at + 4), bone = I(at + 8), sFlags = I(at + 16), sMain = I(at + 24);
                if (id < 0 || id > 1480 || grp < -1 || grp > 255 || bone < -1 || bone > 32767) continue;
                if (!Ok(I(at + 12)) || !Ok(sFlags) || !Ok(I(at + 20)) || !Ok(sMain) || !Ok(I(at + 28)) || !Ok(I(at + 32)) || !Ok(I(at + 36))) continue;
                if (sMain <= 0 || sFlags <= 0) continue;
                for (int k = 12; k < 52; k += 4) if (I(at + k) > 0) all.Add(I(at + k));
                if (found.Any(f => f.Offset == at)) continue;
                found.Add(new Article { Offset = at, Id = id, Group = grp, Bone = bone, Slot = slot });
            }
            foreach (var a in found)
            {
                int sFlags = I(a.Offset + 16), sMain = I(a.Offset + 24);
                int nMain = (all.Where(x => x > sMain).DefaultIfEmpty(_d.Length).Min() - sMain) / 4;
                int nFlags = (all.Where(x => x > sFlags).DefaultIfEmpty(_d.Length).Min() - sFlags) / 8;
                int n = Math.Min(Math.Min(nMain, nFlags), 200);
                for (int i = 0; i < n; i++)
                {
                    var s = new Subaction { Index = i, Flags = _d[sFlags + i * 8 + 1], ScriptOffset = I(sMain + i * 4) };
                    int nameAt = I(sFlags + i * 8 + 4);
                    s.Animation = nameAt > 0 && nameAt < _d.Length ? Str(nameAt) : null;
                    if (s.ScriptOffset > 0 && s.ScriptOffset < _d.Length) s.Main = Script(s.ScriptOffset, 0);
                    a.Subactions.Add(s);
                }
                Articles.Add(a);
            }
        }

        static int BE(byte[] b, int at) => b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3];

        /// <summary>The events of a script from an offset (to its end marker), subroutines included inline.</summary>
        public List<MoveEvent> Script(int at, int depth)
        {
            var list = new List<MoveEvent>();
            for (int guard = 0; guard < 2000 && at > 0 && at + 8 <= _d.Length; guard++, at += 8)
            {
                uint id = (uint)I(at);
                if (id == 0) break;   // end of the script
                int n = _d[at + 2], args = I(at + 4);
                var e = new MoveEvent { Id = id & 0xFFFFFF00, Offset = at, Types = new int[n], Values = new int[n] };
                for (int k = 0; k < n; k++) { e.Types[k] = I(args + k * 8); e.Values[k] = I(args + k * 8 + 4); }
                if (e.Id == 0x00070100 && depth < 6 && n > 0 && e.Types[0] == 2) { list.AddRange(Script(e.Values[0], depth + 1)); continue; }   // subroutine
                if (e.Id == 0x00090100 && depth < 6 && n > 0 && e.Types[0] == 2) { list.AddRange(Script(e.Values[0], depth + 1)); break; }   // goto
                if (e.Id == 0x00080000) break;   // return
                list.Add(e);
            }
            return list;
        }
    }
}
