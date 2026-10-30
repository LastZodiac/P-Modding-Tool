// Your match history, from the database Brawl Online keeps of every game played (replays.sqlite, read only), and
// what it says: records by stage, by character, by matchup and by opponent, and a projected chance to win.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class GamePlayer
    {
        [DataMember] public string UserId, Name, Code;
        [DataMember] public int Character;     // the game's character number (Fighters.Name gives the name)
        [DataMember] public bool? Won;
    }

    [DataContract]
    public sealed class GameRecord
    {
        [DataMember] public long Id;           // our own: the same for a game however it was read (see Key)
        [DataMember] public DateTime Start;    // UTC
        [DataMember] public int Stage;         // stage slot (0x21 = Smashville)
        [DataMember] public bool Ranked, Teams;
        [DataMember] public int Frames;        // length (60 a second)
        [DataMember] public string Session;    // the set it belongs to
        [DataMember] public int GameNumber;
        [DataMember] public string File;       // the replay (full path, as Brawl Online saved it)
        [DataMember] public List<GamePlayer> Players = new List<GamePlayer>();

        /// <summary>What makes a game the same game: when it started, which game of the set, and who played.</summary>
        public string Key => $"{Start.Ticks}|{GameNumber}|{string.Join(",", Players.Select(p => p.UserId ?? p.Code).OrderBy(x => x, StringComparer.Ordinal))}";

        public static long IdOf(string key)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToInt64(sha.ComputeHash(Encoding.UTF8.GetBytes(key)), 0) & long.MaxValue;
        }
    }

    /// <summary>Every game the app has ever read, kept in its own file: Brawl Online's history and replays can be
    /// cleared or deleted (to make room), but what was read from them stays.</summary>
    public static class MatchArchive
    {
        static readonly object Gate = new object();
        static string FileIn(string folder) => Path.Combine(folder, "games.json");

        public static List<GameRecord> Read(string folder)
        {
            lock (Gate)
            {
                try
                {
                    var f = FileIn(folder);
                    if (!File.Exists(f)) return new List<GameRecord>();
                    using (var s = File.OpenRead(f)) return (List<GameRecord>)new DataContractJsonSerializer(typeof(List<GameRecord>)).ReadObject(s) ?? new List<GameRecord>();
                }
                catch { return new List<GameRecord>(); }
            }
        }

        /// <summary>Adds the games Brawl Online knows to the kept ones (its newer copy wins), saves them if anything
        /// was new, and returns them all.</summary>
        public static List<GameRecord> Merge(IEnumerable<GameRecord> fromBrawlOnline, string folder)
        {
            lock (Gate)
            {
                var kept = Read(folder).Where(g => g.Players.Count >= 2).ToDictionary(g => g.Key);
                bool changed = false;
                foreach (var g in fromBrawlOnline)
                {
                    if (!kept.TryGetValue(g.Key, out var old)) changed = true;
                    else if (old.File != g.File || old.Frames != g.Frames || old.Players.Zip(g.Players, (a, b) => a.Won != b.Won || a.Character != b.Character).Any(x => x)) changed = true;
                    kept[g.Key] = g;
                }
                var all = kept.Values.OrderBy(g => g.Start).ToList();
                if (changed)
                    try
                    {
                        Directory.CreateDirectory(folder);
                        var f = FileIn(folder); var tmp = f + ".tmp";
                        using (var s = File.Create(tmp)) new DataContractJsonSerializer(typeof(List<GameRecord>)).WriteObject(s, all);
                        if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
                    }
                    catch { }
                return all;
            }
        }
    }

    /// <summary>A win-loss record.</summary>
    public struct Record
    {
        public int Wins, Losses;
        public int Games => Wins + Losses;
        public double Rate => Games == 0 ? 0 : (double)Wins / Games;
        public override string ToString() => $"{Wins}–{Losses}";
        public string Percent => Games == 0 ? "–" : $"{Rate * 100:0}%";
    }

    /// <summary>The game's character numbers (the select screen's list) and their names.</summary>
    public static class Fighters
    {
        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            [0] = "Mario", [1] = "Donkey Kong", [2] = "Link", [3] = "Samus", [4] = "Zero Suit Samus", [5] = "Yoshi", [6] = "Kirby",
            [7] = "Fox", [8] = "Pikachu", [9] = "Luigi", [10] = "Captain Falcon", [11] = "Ness", [12] = "Bowser", [13] = "Peach",
            [14] = "Zelda", [15] = "Sheik", [16] = "Ice Climbers", [17] = "Ice Climbers", [18] = "Ice Climbers", [19] = "Marth",
            [20] = "Mr. Game & Watch", [21] = "Falco", [22] = "Ganondorf", [23] = "Wario", [24] = "Meta Knight", [25] = "Pit",
            [26] = "Olimar", [27] = "Lucas", [28] = "Diddy Kong", [29] = "Charizard", [30] = "Charizard", [31] = "Squirtle",
            [32] = "Squirtle", [33] = "Ivysaur", [34] = "Ivysaur", [35] = "King Dedede", [36] = "Lucario", [37] = "Ike",
            [38] = "R.O.B.", [39] = "Jigglypuff", [40] = "Toon Link", [41] = "Wolf", [42] = "Snake", [43] = "Sonic",
            [50] = "Roy", [51] = "Mewtwo", [53] = "Knuckles",
        };
        public static string Name(int id) => Names.TryGetValue(id, out var n) ? n : $"Character {id}";
        /// <summary>One number per character (the Pokémon Trainer's and the Ice Climbers' variants count as one).</summary>
        public static int Same(int id) => id == 17 || id == 18 ? 16 : id == 29 ? 30 : id == 31 ? 32 : id == 33 ? 34 : id;
    }

    public sealed class MatchHistory
    {
        public List<GameRecord> Games = new List<GameRecord>();
        public string Me;                       // your user id
        public string MyName, MyCode;
        public Dictionary<int, string> StageNames = new Dictionary<int, string>();
        public string Source;                   // the database file

        public string StageName(int slot) => StageNames.TryGetValue(slot, out var n) ? n : $"Stage {slot:X2}";

        /// <summary>Brawl Online's folder (…\AppData\Roaming\Brawl Online) from its netplay folder.</summary>
        public static string DatabasePath(BrawlOnlineInstall install) => Path.Combine(Path.GetDirectoryName(install.NetplayFolder.TrimEnd('\\', '/')), "replays.sqlite");

        /// <summary>Your games: Brawl Online's history (if it's there) and every game read before (archiveFolder: kept
        /// there, so deleting replays or the history doesn't lose them).</summary>
        public static MatchHistory Load(string databasePath, Dictionary<int, string> stageNames = null, string archiveFolder = null)
        {
            var h = new MatchHistory { Source = databasePath, StageNames = stageNames ?? new Dictionary<int, string>() };
            var fromDb = File.Exists(databasePath) ? FromDatabase(databasePath) : new List<GameRecord>();
            // Brawl Online adds games to its list only now and then (when it looks through its replays), but writes each
            // game's replay as soon as it ends: games newer than its list are read from their replays, so they count
            // right away (with the same record Brawl Online makes later, so they're never counted twice)
            try
            {
                var kept = archiveFolder != null ? MatchArchive.Read(archiveFolder) : new List<GameRecord>();
                fromDb.AddRange(FromNewReplays(fromDb.Concat(kept).ToList()));
            }
            catch { }
            h.Games = archiveFolder != null ? MatchArchive.Merge(fromDb, archiveFolder) : fromDb;
            foreach (var g in h.Games) g.Id = GameRecord.IdOf(g.Key);
            // you: whoever plays in (nearly) every game
            var me = h.Games.SelectMany(g => g.Players.Select(p => p.UserId)).Where(u => !string.IsNullOrEmpty(u)).GroupBy(u => u).OrderByDescending(x => x.Count()).FirstOrDefault();
            h.Me = me?.Key;
            var mine = h.Games.SelectMany(g => g.Players).LastOrDefault(p => p.UserId == h.Me);
            h.MyName = mine?.Name; h.MyCode = mine?.Code;
            return h;
        }

        static List<GameRecord> FromDatabase(string databasePath)
        {
            var db = SqliteReader.Open(databasePath);
            if (!db.HasTable("game") || !db.HasTable("player")) return new List<GameRecord>();
            var files = db.HasTable("file") ? db.Table("file").ToDictionary(r => L(r, "_id"), r => S(r, "folder") is string dir && S(r, "name") is string n ? Path.Combine(dir, n) : S(r, "name")) : new Dictionary<long, string>();
            var games = new Dictionary<long, GameRecord>();
            foreach (var r in db.Table("game"))
            {
                var g = new GameRecord
                {
                    Id = L(r, "_id"), Stage = (int)L(r, "stage"), Ranked = L(r, "is_ranked") == 1, Teams = L(r, "is_teams") == 1,
                    Frames = (int)L(r, "last_frame"), Session = S(r, "session_id"), GameNumber = (int)L(r, "game_number"),
                };
                if (DateTime.TryParse(S(r, "start_time"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)) g.Start = t;
                files.TryGetValue(L(r, "file_id"), out g.File);
                games[g.Id] = g;
            }
            foreach (var r in db.Table("player"))
            {
                if (!games.TryGetValue(L(r, "game_id"), out var g)) continue;
                var won = r.TryGetValue("is_winner", out var w) && w is long wl ? (bool?)(wl == 1) : null;
                g.Players.Add(new GamePlayer { UserId = S(r, "user_id"), Name = S(r, "display_name"), Code = S(r, "connect_code"), Character = Fighters.Same((int)L(r, "character_id")), Won = won });
            }
            foreach (var g in games.Values) g.Players = g.Players.OrderBy(p => p.UserId, StringComparer.Ordinal).ToList();
            return games.Values.Where(g => g.Players.Count >= 2).OrderBy(g => g.Start).ToList();
        }

        /// <summary>Games whose replays are newer than every game known so far (read from the replays' own headers).</summary>
        static List<GameRecord> FromNewReplays(List<GameRecord> known)
        {
            var result = new List<GameRecord>();
            var withFiles = known.Where(g => !string.IsNullOrEmpty(g.File)).ToList();
            var names = new HashSet<string>(withFiles.Select(g => Path.GetFileName(g.File)), StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(known.Select(g => g.Key));
            DateTime since = known.Count > 0 ? known.Max(g => g.Start).AddHours(-6) : DateTime.UtcNow.AddDays(-2);
            // where the replays are: next to the newest known ones (a folder per month), else Documents\Brawl Online
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in withFiles.OrderByDescending(g => g.Start).Take(20))
                try { var month = Path.GetDirectoryName(g.File); var root = Path.GetDirectoryName(month); if (Directory.Exists(root)) roots.Add(root); } catch { }
            try { var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Brawl Online"); if (Directory.Exists(docs)) roots.Add(docs); } catch { }
            foreach (var root in roots)
            {
                var folders = new List<string> { root };
                try { folders.AddRange(Directory.GetDirectories(root).Where(d => Directory.GetLastWriteTimeUtc(d) > since.AddDays(-1))); } catch { }
                foreach (var folder in folders)
                {
                    string[] files;
                    try { files = Directory.GetFiles(folder, "*.brep"); } catch { continue; }
                    foreach (var file in files)
                    {
                        if (names.Contains(Path.GetFileName(file))) continue;
                        try
                        {
                            if (File.GetLastWriteTimeUtc(file) < since) continue;
                            var g = ReplayHeader(file);
                            if (g == null || !keys.Add(g.Key)) continue;
                            names.Add(Path.GetFileName(file));
                            result.Add(g);
                        }
                        catch { }
                    }
                }
            }
            return result;
        }

        /// <summary>A game's record from its replay's header and ending (null if the game hasn't finished).</summary>
        public static GameRecord ReplayHeader(string path)
        {
            object info = null, end = null;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new BinaryReader(f))
            {
                var magic = r.ReadBytes(8);
                if (magic.Length < 8 || magic[1] != 'B' || magic[2] != 'R' || magic[3] != 'P') return null;
                f.Position = 0x20;
                while (f.Position + 20 <= f.Length)
                {
                    string name = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                    r.ReadUInt32(); uint packed = r.ReadUInt32(); uint size = r.ReadUInt32(); r.ReadUInt32();
                    if (f.Position + packed > f.Length) break;
                    if (name != "INFO" && name != "ENDG") { f.Position += packed; continue; }
                    var raw = r.ReadBytes((int)packed);
                    var data = raw.Length >= 2 && raw[0] == 0x78 ? Replays.Inflate(raw, (int)size) : raw;
                    var obj = Json.Parse(System.Text.Encoding.UTF8.GetString(data));
                    if (name == "INFO") info = obj; else end = obj;
                }
            }
            if (info == null || end == null) return null;
            var g = new GameRecord
            {
                File = path, Stage = (int)Json.Num(info, "stageId"), Ranked = Json.Str(info, "mode") == "ranked", Teams = Json.Bool(info, "isTeams"),
                GameNumber = (int)Json.Num(info, "gameIndex"), Session = Json.Str(info, "matchId"),
                Frames = (int)Json.Num(end, "lastFrame"),
            };
            if (!DateTime.TryParse(Json.Str(info, "startTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)) return null;
            g.Start = t;
            var won = new Dictionary<long, bool>();
            foreach (var p in Json.Arr(end, "players")) won[Json.Num(p, "port")] = Json.Bool(p, "isWinner");
            foreach (var p in Json.Arr(info, "players"))
                g.Players.Add(new GamePlayer
                {
                    UserId = Json.Str(p, "uid"), Name = Json.Str(p, "displayName"), Code = Json.Str(p, "connectCode"), Character = Fighters.Same((int)Json.Num(p, "characterId")),
                    Won = won.TryGetValue(Json.Num(p, "port"), out var w) ? (bool?)w : null,
                });
            g.Players = g.Players.OrderBy(p => p.UserId, StringComparer.Ordinal).ToList();
            return g.Players.Count >= 2 ? g : null;
        }

        static long L(Dictionary<string, object> r, string c) => r.TryGetValue(c, out var v) && v is long l ? l : 0;
        static string S(Dictionary<string, object> r, string c) => r.TryGetValue(c, out var v) ? v as string : null;

        /// <summary>Your 1-on-1 games, each as (you, the opponent).</summary>
        public IEnumerable<(GameRecord Game, GamePlayer Me, GamePlayer Them)> Singles(bool? ranked = null) =>
            Games.Where(g => !g.Teams && g.Players.Count == 2 && (ranked == null || g.Ranked == ranked))
                 .Select(g => (g, g.Players.FirstOrDefault(p => p.UserId == Me), g.Players.FirstOrDefault(p => p.UserId != Me)))
                 .Where(x => x.Item2 != null && x.Item3 != null && x.Item2.Won != null);

        public static Record Count(IEnumerable<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games)
        {
            var r = new Record();
            foreach (var g in games) if (g.Me.Won == true) r.Wins++; else r.Losses++;
            return r;
        }

        public Record Overall(bool? ranked = null) => Count(Singles(ranked));

        public List<(T Key, Record Record)> By<T>(Func<(GameRecord Game, GamePlayer Me, GamePlayer Them), T> key, bool? ranked = null) =>
            Singles(ranked).GroupBy(key).Select(g => (g.Key, Count(g))).OrderByDescending(x => x.Item2.Games).ToList();

        /// <summary>The opponent known by this connect code or name.</summary>
        public string UserIdOf(string codeOrName) =>
            Games.SelectMany(g => g.Players).LastOrDefault(p => p.UserId != Me && (string.Equals(p.Code, codeOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, codeOrName, StringComparison.OrdinalIgnoreCase)))?.UserId;

        // ----- the projection -----

        public sealed class Factor { public string What; public Record Record; public double Shrunk; public double Effect; }

        public sealed class Projection
        {
            public double Chance;                       // 0..1
            public List<Factor> Factors = new List<Factor>();
            public int Evidence;                        // games behind it (the factors' own games)
            public string Confidence => Evidence >= 40 ? "good" : Evidence >= 15 ? "some" : "little";
        }

        const double Prior = 8;   // how many games of your overall rate each factor starts from (few games: little pull)

        static double Logit(double p) { p = Math.Max(0.02, Math.Min(0.98, p)); return Math.Log(p / (1 - p)); }
        static double Shrink(Record r, double towards) => (r.Wins + Prior * towards) / (r.Games + Prior);

        /// <summary>Your chance to win, from your overall record moved by each thing known about this game: your character
        /// against theirs (with anyone), against this player, and on this stage. Few games move it little.</summary>
        public Projection Project(int? myChar, int? theirChar, string opponentId, int? stage)
        {
            var all = Singles().ToList();
            var baseRec = Count(all);
            double b = baseRec.Games == 0 ? 0.5 : Shrink(baseRec, 0.5);
            var p = new Projection();
            double logit = Logit(b);
            void Add(string what, IEnumerable<(GameRecord, GamePlayer, GamePlayer)> games)
            {
                var r = Count(games);
                if (r.Games == 0) { p.Factors.Add(new Factor { What = what, Record = r, Shrunk = b }); return; }
                double s = Shrink(r, b);
                var f = new Factor { What = what, Record = r, Shrunk = s, Effect = Logit(s) - Logit(b) };
                p.Factors.Add(f); logit += f.Effect; p.Evidence += r.Games;
            }
            if (myChar != null && theirChar != null) Add($"{Fighters.Name(myChar.Value)} vs {Fighters.Name(theirChar.Value)} (anyone)", all.Where(x => x.Me.Character == myChar && x.Them.Character == theirChar));
            else if (theirChar != null) Add($"vs {Fighters.Name(theirChar.Value)} (anyone)", all.Where(x => x.Them.Character == theirChar));
            else if (myChar != null) Add($"as {Fighters.Name(myChar.Value)}", all.Where(x => x.Me.Character == myChar));
            if (opponentId != null) Add("against this player", all.Where(x => x.Them.UserId == opponentId));
            if (stage != null) Add($"on {StageName(stage.Value)}", all.Where(x => x.Game.Stage == stage));
            // the factors overlap (you meet the same player on the same stages), so they aren't simply added: the strongest
            // counts fully, the next half as much, the next a quarter
            logit = Logit(b); double weight = 1;
            foreach (var f in p.Factors.OrderByDescending(f => Math.Abs(f.Effect))) { f.Effect *= weight; logit += f.Effect; weight /= 2; }
            p.Chance = Math.Max(0.03, Math.Min(0.97, 1 / (1 + Math.Exp(-logit))));
            return p;
        }

        /// <summary>Your characters best against this one (your record with each against it, pulled towards that character's
        /// overall record when there are few games).</summary>
        public List<(int Character, Record Versus, double Score)> Counterpicks(int theirChar, int count = 3)
        {
            var all = Singles().ToList();
            return all.GroupBy(x => x.Me.Character).Select(g =>
            {
                var overall = Count(g);
                var vs = Count(g.Where(x => x.Them.Character == theirChar));
                return (g.Key, vs, Shrink(vs, Shrink(overall, 0.5)), overall.Games);
            }).Where(x => x.Item4 >= 3 && x.vs.Games >= 2).OrderByDescending(x => x.Item3).Take(count).Select(x => (x.Key, x.vs, x.Item3)).ToList();
        }

        /// <summary>Your best stages (optionally against one character or player), from those given or all you've played.</summary>
        public List<(int Stage, Record Record, double Score)> BestStages(IEnumerable<int> legal = null, int? theirChar = null, string opponentId = null, int count = 3)
        {
            var all = Singles().ToList();
            var pool = legal?.ToList() ?? all.Select(x => x.Game.Stage).Distinct().ToList();
            return pool.Select(s =>
            {
                var onStage = all.Where(x => x.Game.Stage == s).ToList();
                var general = Count(onStage);
                var specific = Count(onStage.Where(x => (theirChar == null || x.Them.Character == theirChar) && (opponentId == null || x.Them.UserId == opponentId)));
                double score = theirChar == null && opponentId == null ? Shrink(general, 0.5) : Shrink(specific, Shrink(general, 0.5));
                return (s, theirChar == null && opponentId == null ? general : specific, score);
            }).Where(x => x.Item2.Games >= (theirChar == null && opponentId == null ? 3 : 2)).OrderByDescending(x => x.Item3).Take(count).ToList();
        }
    }
}
