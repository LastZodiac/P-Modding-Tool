// Your match history, from the database Brawl Online keeps of every game played (replays.sqlite, read only), and
// what it says: records by stage, by character, by matchup and by opponent, and a projected chance to win.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkinManager.Core
{
    public sealed class GamePlayer
    {
        public string UserId, Name, Code;
        public int Character;     // the game's character number (Fighters.Name gives the name)
        public bool? Won;
    }

    public sealed class GameRecord
    {
        public long Id;
        public DateTime Start;    // UTC
        public int Stage;         // stage slot (0x21 = Smashville)
        public bool Ranked, Teams;
        public int Frames;        // length (60 a second)
        public string Session;    // the set it belongs to
        public int GameNumber;
        public string File;       // the replay (full path, as Brawl Online saved it)
        public List<GamePlayer> Players = new List<GamePlayer>();
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

        public static MatchHistory Load(string databasePath, Dictionary<int, string> stageNames = null)
        {
            var db = SqliteReader.Open(databasePath);
            var h = new MatchHistory { Source = databasePath, StageNames = stageNames ?? new Dictionary<int, string>() };
            if (!db.HasTable("game") || !db.HasTable("player")) return h;
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
            h.Games = games.Values.Where(g => g.Players.Count >= 2).OrderBy(g => g.Start).ToList();
            // you: whoever plays in (nearly) every game
            var me = h.Games.SelectMany(g => g.Players.Select(p => p.UserId)).Where(u => !string.IsNullOrEmpty(u)).GroupBy(u => u).OrderByDescending(x => x.Count()).FirstOrDefault();
            h.Me = me?.Key;
            var mine = h.Games.SelectMany(g => g.Players).LastOrDefault(p => p.UserId == h.Me);
            h.MyName = mine?.Name; h.MyCode = mine?.Code;
            return h;
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
