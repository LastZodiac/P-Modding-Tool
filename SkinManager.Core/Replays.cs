// Brawl Online's replays (.brep, in Documents\Brawl Online): read for Smash stats. The file is a list of chunks
// (4-letter name, version, packed size, size, check, zlib data): INFO (players, JSON), FTRM (each fighter's state
// every frame: blocks of the game's memory, laid out by FTSP), ENDG (the result, JSON). FTRM chunks hold 60 frames,
// port by port; the first frame of a chunk is whole, every other frame is XOR'd with the frame before it.
// From each fighter's ftOwner: percent (float at +36) and stocks (byte at +55); from its motion module: the
// subaction (move) playing (u16 at +90). Checked against ENDG's final percents and stocks on many games.
// Only read, never changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SkinManager.Core
{
    [DataContract] public sealed class ReplayHit { [DataMember] public int Frame, Attacker, Victim, Move; [DataMember] public float Damage, Before, X, Y; }   // X, Y: where the one hit was
    [DataContract] public sealed class ReplayDeath { [DataMember] public int Frame, Victim, Killer = -1, Move = -1; [DataMember] public float Percent; }
    [DataContract] public sealed class ReplayOpening { [DataMember] public int Attacker, Start, End, Hits, FirstMove; [DataMember] public float Damage; [DataMember] public bool Killed; }

    [DataContract]
    public sealed class ReplayData
    {
        public const int CurrentVersion = 4;
        [DataMember] public int Version = CurrentVersion;
        [DataMember] public string File; [DataMember] public long Size;
        [DataMember] public int Frames;
        [DataMember] public List<int> Characters = new List<int>();      // per player (INFO order)
        [DataMember] public List<string> Codes = new List<string>(), Names = new List<string>(), Ids = new List<string>();
        [DataMember] public List<int> EndStocks = new List<int>();
        [DataMember] public List<ReplayHit> Hits = new List<ReplayHit>();
        [DataMember] public List<ReplayDeath> Deaths = new List<ReplayDeath>();
        [DataMember] public List<ReplayOpening> Openings = new List<ReplayOpening>();
        [DataMember] public float StageLeft, StageRight;                 // how far anyone stood on the ground, each way
        [DataMember] public List<PlayerHabits> Habits = new List<PlayerHabits>();   // per player (when the move names were known)
        [DataMember] public string NamesKey;                             // which move names the habits were read with
        public int PlayerOf(string userId, string code) { for (int i = 0; i < Ids.Count; i++) if (userId != null && Ids[i] == userId || code != null && Codes[i] == code) return i; return -1; }
    }

    public static class Replays
    {
        const int OwnerPercent = 128 + 36, OwnerStocks = 128 + 55, MotionSub = 768 + 90;
        const int PostureX = 1136 + 12, PostureY = 1136 + 16, PostureFacing = 1136 + 64, Situation = 1344 + 44;   // situation: 0 ground, 1 ledge, 2 air

        /// <summary>Reads a replay. Throws if it isn't one.</summary>
        public static ReplayData Read(string path) => Read(path, null, null);

        /// <summary>Reads a replay; with move names (a character's subaction animation names by number) also each
        /// player's habits. Throws if it isn't one.</summary>
        public static ReplayData Read(string path, Func<int, MoveTable> names, string namesKey)
        {
            var d = new ReplayData { File = path, Size = new FileInfo(path).Length };
            using (var f = File.OpenRead(path))
            using (var r = new BinaryReader(f))
            {
                var magic = r.ReadBytes(8);
                if (magic.Length < 8 || magic[1] != 'B' || magic[2] != 'R' || magic[3] != 'P') throw new InvalidDataException("Not a Brawl Online replay.");
                f.Position = 0x20;
                var ports = new List<int>();
                int blockSize = 2248;
                // per port, the bytes we follow (XOR state)
                var state = new Dictionary<int, byte[]>();
                var percent = new List<float>[4]; var stocks = new List<int>[4]; var move = new List<int>[4];
                var px = new List<float>[4]; var py = new List<float>[4]; var face = new List<sbyte>[4]; var sit = new List<byte>[4];
                for (int i = 0; i < 4; i++) { percent[i] = new List<float>(); stocks[i] = new List<int>(); move[i] = new List<int>(); px[i] = new List<float>(); py[i] = new List<float>(); face[i] = new List<sbyte>(); sit[i] = new List<byte>(); }
                int[] watch = { OwnerPercent, OwnerPercent + 1, OwnerPercent + 2, OwnerPercent + 3, OwnerStocks, MotionSub, MotionSub + 1,
                                PostureX, PostureX + 1, PostureX + 2, PostureX + 3, PostureY, PostureY + 1, PostureY + 2, PostureY + 3, PostureFacing, Situation + 3 };
                while (f.Position + 20 <= f.Length)
                {
                    string name = Encoding.ASCII.GetString(r.ReadBytes(4));
                    r.ReadUInt32(); uint packed = r.ReadUInt32(); uint size = r.ReadUInt32(); r.ReadUInt32();
                    if (f.Position + packed > f.Length) break;
                    var raw = r.ReadBytes((int)packed);
                    if (name != "INFO" && name != "FTRM" && name != "ENDG" && name != "FTSP") continue;
                    byte[] data = raw;
                    if (raw.Length >= 2 && raw[0] == 0x78) data = Inflate(raw, (int)size);
                    if (name == "INFO")
                    {
                        var info = Json.Parse(Encoding.UTF8.GetString(data));
                        foreach (var p in Json.Arr(info, "players"))
                        {
                            d.Characters.Add((int)Json.Num(p, "characterId")); d.Codes.Add(Json.Str(p, "connectCode")); d.Names.Add(Json.Str(p, "displayName")); d.Ids.Add(Json.Str(p, "uid"));
                            ports.Add((int)Json.Num(p, "port"));
                        }
                    }
                    else if (name == "FTSP")
                    {
                        try { blockSize = (int)Json.Num(Json.Parse(Encoding.UTF8.GetString(data)), "bytes"); } catch { }
                    }
                    else if (name == "ENDG")
                    {
                        var end = Json.Parse(Encoding.UTF8.GetString(data));
                        foreach (var p in Json.Arr(end, "players")) d.EndStocks.Add((int)Json.Num(p, "stocks"));
                    }
                    else if (name == "FTRM" && data.Length >= 16)
                    {
                        int start = BitConverter.ToInt32(data, 0), count = BitConverter.ToInt32(data, 4), mask = BitConverter.ToInt32(data, 8), bs = BitConverter.ToInt32(data, 12);
                        if (bs > 0) blockSize = bs;
                        var maskPorts = Enumerable.Range(0, 8).Where(i => (mask >> i & 1) != 0).ToList();
                        for (int k = 0; k < maskPorts.Count && k < 4; k++)
                        {
                            if (!state.TryGetValue(k, out var st)) state[k] = st = new byte[watch.Length];
                            for (int fr = 0; fr < count; fr++)
                            {
                                int at = 16 + (k * count + fr) * blockSize;
                                if (at + blockSize > data.Length) break;
                                for (int w = 0; w < watch.Length; w++) st[w] = fr == 0 ? data[at + watch[w]] : (byte)(st[w] ^ data[at + watch[w]]);
                                percent[k].Add(BitConverter.ToSingle(new[] { st[3], st[2], st[1], st[0] }, 0));
                                stocks[k].Add(st[4]);
                                move[k].Add(st[5] << 8 | st[6]);
                                px[k].Add(BitConverter.ToSingle(new[] { st[10], st[9], st[8], st[7] }, 0));
                                py[k].Add(BitConverter.ToSingle(new[] { st[14], st[13], st[12], st[11] }, 0));
                                face[k].Add((sbyte)((st[15] & 0x80) != 0 ? -1 : 1));   // the float's sign: -1 or 1
                                sit[k].Add(st[16]);
                            }
                        }
                    }
                }
                int players = Math.Min(d.Characters.Count, state.Count);
                if (players < 2) throw new InvalidDataException("This replay has no fighter data.");
                d.Frames = percent[0].Count;
                // the stage: as far as anyone stood on the ground
                float left = 0, right = 0;
                for (int k = 0; k < players; k++)
                    for (int i = 0; i < px[k].Count; i++)
                        if (sit[k][i] == 0 && !float.IsNaN(px[k][i]) && Math.Abs(px[k][i]) < 400) { left = Math.Min(left, px[k][i]); right = Math.Max(right, px[k][i]); }
                d.StageLeft = left; d.StageRight = right;
                Events(d, players, percent, stocks, move, px, py);
                if (names != null)
                {
                    d.NamesKey = namesKey;
                    for (int k = 0; k < players; k++)
                    {
                        var n = names(d.Characters[k]);
                        d.Habits.Add(n == null ? new PlayerHabits() : Habits.Read(k, players, n, move, px, py, face, sit, d));
                    }
                }
            }
            return d;
        }

        static byte[] Inflate(byte[] z, int size)
        {
            using (var ms = new MemoryStream(z, 2, z.Length - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                var o = new byte[size]; int n = 0, got;
                while (n < size && (got = ds.Read(o, n, size - n)) > 0) n += got;
                return o;
            }
        }

        /// <summary>Hits (percent going up), deaths (a stock lost), and openings (a run of hits by one player).</summary>
        static void Events(ReplayData d, int players, List<float>[] percent, List<int>[] stocks, List<int>[] move, List<float>[] px, List<float>[] py)
        {
            int frames = percent.Take(players).Min(p => p.Count);
            for (int v = 0; v < players; v++)
                for (int i = 1; i < frames; i++)
                {
                    float before = percent[v][i - 1], now = percent[v][i];
                    if (float.IsNaN(now) || float.IsNaN(before)) continue;
                    if (now > before + 0.01f && now < 1000)
                    {
                        // 1 on 1: the other player (in doubles: the opponent doing an attack, or the first one)
                        int a = players == 2 ? 1 - v : Enumerable.Range(0, players).Where(x => x != v).OrderBy(x => IsAttack(move[x][i]) ? 0 : 1).First();
                        d.Hits.Add(new ReplayHit { Frame = i, Attacker = a, Victim = v, Move = move[a][i], Damage = now - before, Before = before, X = px[v][i], Y = py[v][i] });
                    }
                    if (stocks[v][i] < stocks[v][i - 1] && stocks[v][i - 1] <= 99)
                        {
                        // the percent they died at: the most they had in the last half second (it can reset a frame early)
                        float most = 0; for (int k = Math.Max(1, i - 30); k < i; k++) if (!float.IsNaN(percent[v][k]) && percent[v][k] < 1000) most = Math.Max(most, percent[v][k]);
                        d.Deaths.Add(new ReplayDeath { Frame = i, Victim = v, Percent = most });
                    }
                }
            d.Hits.Sort((x, y) => x.Frame.CompareTo(y.Frame));
            // openings: hits in a row on one player by one player, until the one hit has been free to act (not in hitstun,
            // grabbed or knocked down) for 45 frames in a row, or dies
            var open = new Dictionary<int, ReplayOpening>();
            foreach (var h in d.Hits)
            {
                open.TryGetValue(h.Victim, out var o);
                bool died = o != null && d.Deaths.Any(x => x.Victim == h.Victim && x.Frame > o.End && x.Frame <= h.Frame);
                if (o == null || o.Attacker != h.Attacker || died || FreeRun(move[h.Victim], o.End, h.Frame) > 45)
                {
                    o = new ReplayOpening { Attacker = h.Attacker, Start = h.Frame, End = h.Frame, FirstMove = h.Move };
                    d.Openings.Add(o); open[h.Victim] = o;
                    // the victim's own run on the attacker ends: they got hit
                    if (open.TryGetValue(h.Attacker, out var theirs) && theirs.Attacker == h.Victim) open.Remove(h.Attacker);
                }
                o.Hits++; o.Damage += h.Damage; o.End = h.Frame;
            }
            // who killed: the last one to hit them (within 5 seconds); none = a self-destruct
            foreach (var death in d.Deaths)
            {
                var last = d.Hits.LastOrDefault(h => h.Victim == death.Victim && h.Frame <= death.Frame && death.Frame - h.Frame < 300);
                if (last == null) continue;
                death.Killer = last.Attacker; death.Move = last.Move;
                var op = d.Openings.LastOrDefault(o => o.Attacker == last.Attacker && o.Start <= last.Frame && o.End >= last.Frame);
                if (op != null) op.Killed = true;
            }
        }

        static bool IsAttack(int sub) => sub > 0;   // refined when names are known

        /// <summary>Hit, grabbed, thrown or knocked down (the subactions every fighter has in the same place: DamageFall,
        /// Thrown*, Capture*, Damage*, Down* before getting up, the special throws, DamageFace).</summary>
        static bool Stuck(int sub) => sub == 41 || sub >= 118 && sub <= 174 || sub >= 179 && sub <= 183 || sub >= 420 && sub <= 430 || sub == 459;

        /// <summary>The longest run of frames the player was free to act in, between two frames.</summary>
        static int FreeRun(List<int> move, int from, int to)
        {
            int run = 0, best = 0;
            for (int i = from + 1; i < to && i < move.Count; i++) { if (Stuck(move[i])) run = 0; else best = Math.Max(best, ++run); }
            return best;
        }

        /// <summary>A replay's events, read once and kept in the cache folder.</summary>
        public static ReplayData Cached(string path, string cacheFolder) => Cached(path, cacheFolder, null, null);

        /// <summary>The same, with each player's habits (read again if the names have changed since).</summary>
        public static ReplayData Cached(string path, string cacheFolder, Func<int, MoveTable> names, string namesKey)
        {
            var info = new FileInfo(path);
            string key = Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant() + "|" + info.Length))).Replace('/', '_').Replace('+', '-').TrimEnd('=');
            string file = Path.Combine(cacheFolder, key + ".json");
            var ser = new DataContractJsonSerializer(typeof(ReplayData));
            try
            {
                if (File.Exists(file)) using (var s = File.OpenRead(file)) { var old = (ReplayData)ser.ReadObject(s); if (old.Version == ReplayData.CurrentVersion && old.Size == info.Length && (names == null || old.NamesKey == namesKey)) return old; }
            }
            catch { }
            var d = Read(path, names, namesKey);
            try { Directory.CreateDirectory(cacheFolder); using (var s = File.Create(file)) ser.WriteObject(s, d); } catch { }
            return d;
        }
    }
}
