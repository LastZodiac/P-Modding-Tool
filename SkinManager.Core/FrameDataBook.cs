// The frame data of a whole fighter: reads its moveset and animations from the card and names its moves the way
// players say them (jab, forward tilt, nair, up B...), grouped like a frame data site.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public sealed class MoveEntry
    {
        public string Group, Name;      // "Aerials", "Neutral air"
        public string[] Parts;          // animation names played in order
        public MoveData Data;
        public string Note;             // a line about the move when its numbers need explaining (a bomb is an item...)
        public bool NoteIsInfo;         // the note only explains what's drawn (it doesn't make the row any less exact)
        public List<string> Covers = new List<string>();   // the parts of identical rows merged into this one (its air version...)
        public bool Extra;              // a row for a move outside the usual list (a landing hit, a crouch attack...)
        public bool Sets;               // it puts out something that only hits later, when set off (Snake's C4)
    }

    public sealed class FighterBook
    {
        public Character Character;
        public Moveset Moves;
        public byte[] Costume, Motion;  // for drawing
        public Dictionary<string, int> Lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<MoveEntry> Entries = new List<MoveEntry>();
        public float LandingLag(int off) => Moves.Attr(off);
        public Body Body;                                   // weight, gravity, size... and the standing body
        public Dictionary<MoveData, List<FramePlace>> Places; // where each move's hitboxes are (read when combos are first asked for)
        public Moveset Item;                                // an item the fighter makes, its own scripts (R.O.B.'s gyro)
        public List<(string Move, string Name, Moveset Scripts)> Items = new List<(string, string, Moveset)>();   // items a special pulls out (Link's bomb)
        public float ItemGravity = float.NaN, ItemMaxFall, ItemAirFriction, ItemGroundFriction;   // ...and how it moves (P+'s item settings)

        /// <summary>Works out where every move's hitboxes are (a few seconds the first time).</summary>
        public void Measure()
        {
            if (Places != null) return;
            var body = Body.Of(this);
            var places = ModelPreview.Places(Costume, Motion, Moves, Entries.Select(e => e.Data), body.Size, out var box);
            body.Width = Math.Max(2, box.MaxX - box.MinX); body.Height = Math.Max(4, box.MaxY - box.MinY);
            Body = body; Places = places;
        }
    }

    public static class FrameDataBook
    {
        public static readonly string[] Groups = { "Ground attacks", "Aerials", "Neutral B", "Side B", "Up B", "Down B", "Grabs & throws", "Dodges & getups", "Other moves" };
        public static readonly string[] Tabs = { "All", "Ground", "Air", "Specials", "Grabs", "Defense" };
        public static bool InTab(MoveEntry e, string tab) =>
            tab == "All" || tab == "Ground" && e.Group == "Ground attacks" || tab == "Air" && e.Group == "Aerials" ||
            tab == "Specials" && e.Group.EndsWith(" B") || tab == "Grabs" && e.Group == "Grabs & throws" || tab == "Defense" && e.Group == "Dodges & getups";

        // standard moves: group, name, animation(s)
        static readonly (string g, string n, string a)[] Standard =
        {
            ("Ground attacks", "Jab 1", "Attack11"), ("Ground attacks", "Jab 2", "Attack12"), ("Ground attacks", "Jab 3", "Attack13"),
            ("Ground attacks", "Rapid jab", "Attack100Start+Attack100"), ("Ground attacks", "Rapid jab", "Attack100"), ("Ground attacks", "Rapid jab end", "Attack100End"),
            ("Ground attacks", "Dash attack", "AttackDash"),
            ("Ground attacks", "Forward tilt (up)", "AttackS3Hi"), ("Ground attacks", "Forward tilt", "AttackS3S"), ("Ground attacks", "Forward tilt (down)", "AttackS3Lw"),
            ("Ground attacks", "Forward tilt 2", "AttackS3S2"), ("Ground attacks", "Forward tilt 3", "AttackS3S3"),
            ("Ground attacks", "Up tilt", "AttackHi3"), ("Ground attacks", "Down tilt", "AttackLw3"),
            ("Ground attacks", "Forward smash", "AttackS4Start+AttackS4S"), ("Ground attacks", "Forward smash (up)", "AttackS4Start+AttackS4Hi"), ("Ground attacks", "Forward smash (down)", "AttackS4Start+AttackS4Lw"),
            ("Ground attacks", "Forward smash 2", "AttackS4S2"),
            ("Ground attacks", "Up smash", "AttackHi4Start+AttackHi4"), ("Ground attacks", "Down smash", "AttackLw4Start+AttackLw4"),
            ("Aerials", "Neutral air", "AttackAirN"), ("Aerials", "Forward air", "AttackAirF"), ("Aerials", "Back air", "AttackAirB"),
            ("Aerials", "Up air", "AttackAirHi"), ("Aerials", "Down air", "AttackAirLw"),
            ("Grabs & throws", "Grab", "Catch"), ("Grabs & throws", "Dash grab", "CatchDash"), ("Grabs & throws", "Pivot grab", "CatchTurn"),
            ("Grabs & throws", "Pummel", "CatchAttack"),
            ("Grabs & throws", "Forward throw", "ThrowF"), ("Grabs & throws", "Back throw", "ThrowB"), ("Grabs & throws", "Up throw", "ThrowHi"), ("Grabs & throws", "Down throw", "ThrowLw"),
            ("Dodges & getups", "Spot dodge", "EscapeN"), ("Dodges & getups", "Forward roll", "EscapeF"), ("Dodges & getups", "Back roll", "EscapeB"), ("Dodges & getups", "Air dodge", "EscapeAir"),
            ("Dodges & getups", "Ledge attack", "CliffAttackQuick"), ("Dodges & getups", "Ledge attack (100%+)", "CliffAttackSlow"),
            ("Dodges & getups", "Ledge roll", "CliffEscapeQuick"), ("Dodges & getups", "Ledge getup", "CliffClimbQuick"),
            ("Dodges & getups", "Getup attack (face up)", "DownAttackU"), ("Dodges & getups", "Getup attack (face down)", "DownAttackD"),
            ("Dodges & getups", "Trip attack", "SlipAttack"),
        };

        /// <summary>The fighters on the card that have a moveset, animations and at least one costume to draw.</summary>
        public static List<Character> Available(Fat32Image card)
        {
            var list = new List<Character>();
            foreach (var c in Characters.All)
            {
                try
                {
                    var names = card.ListFolder(c.FighterFolderPath + "/").Select(e => e.Name).ToList();
                    if (Find(names, $"^Fit{c.FilePrefix}\\.pac$") != null && MotionName(names, c) != null && CostumeName(names, c) != null) list.Add(c);
                }
                catch { }
            }
            return list;
        }

        static string Find(List<string> names, string pattern) => names.FirstOrDefault(n => Regex.IsMatch(n, pattern, RegexOptions.IgnoreCase));
        static string MotionName(List<string> names, Character c) => Find(names, $"^Fit{c.FilePrefix}MotionEtc\\.pac$") ?? Find(names, $"^Fit{c.FilePrefix}Motion\\.pac$");
        static string CostumeName(List<string> names, Character c) =>
            names.Where(n => Regex.IsMatch(n, $"^Fit{c.FilePrefix}\\d\\d\\.pac$", RegexOptions.IgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        /// <summary>Reads a fighter and works out the frame data of all its moves.</summary>
        public static FighterBook Load(Fat32Image card, Character c)
        {
            var dir = c.FighterFolderPath + "/";
            var names = card.ListFolder(dir).Select(e => e.Name).ToList();
            var book = new FighterBook { Character = c };
            var fit = card.ReadFile(dir + Find(names, $"^Fit{c.FilePrefix}\\.pac$"));
            book.Motion = card.ReadFile(dir + MotionName(names, c));
            var costume = CostumeName(names, c);
            book.Costume = costume == null ? null : card.ReadFile(dir + costume);
            lock (BrawlGate.Lock)
            {
                using (var tmp = new TempFile(".pac", fit))
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var entry = root.Children[0];
                    var src = entry.WorkingUncompressed;
                    var bytes = new byte[src.Length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                    book.Moves = new Moveset(bytes);
                }
                using (var tmp = new TempFile(".pac", book.Motion))
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                    foreach (var ch in root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>())
                        if (!book.Lengths.ContainsKey(ch.Name)) book.Lengths[ch.Name] = ch.FrameCount;
                // the costume file can carry a thing's animations too (Olimar's Pikmin: WpnPikminPikmin...)
                if (book.Costume != null && c.FilePrefix == "Pikmin")
                    using (var tmp = new TempFile(".pac", book.Costume))
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                        foreach (var ch in root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().Where(x => x.Name.StartsWith("Wpn", StringComparison.OrdinalIgnoreCase)))
                            if (!book.Lengths.ContainsKey(ch.Name)) book.Lengths[ch.Name] = ch.FrameCount;
            }
            if (c.FilePrefix == "Robot") ReadGyro(card, c, book);
            ReadItems(card, c, book);
            Build(book);
            return book;
        }

        // Items a special pulls out: their scripts are P+'s item data. Link's and Toon Link's bombs are the fighter's own
        // (fighter/<name>/ITEM/Itm<Name>00Param.pac), the rest are shared (item/ItmCommonParam.pac).
        static readonly (string Fighter, string Move, string Name, string Common)[] ItemMoves =
        {
            ("Link", "SpecialLw", "bomb", null), ("ToonLink", "SpecialLw", "bomb", null),
            ("Snake", "SpecialN", "grenade", "ItmSnakeGrenadeParam"), ("Peach", "SpecialLw", "turnip", "ItmPeachDaikonParam"),
            ("Diddy", "SpecialN", "peanut", "ItmDiddyPeanutsParam"),
        };

        static void ReadItems(Fat32Image card, Character c, FighterBook book)
        {
            foreach (var it in ItemMoves.Where(x => x.Fighter == c.FilePrefix))
                try
                {
                    string dir = c.FighterFolderPath; int k = dir.IndexOf("/fighter/", StringComparison.OrdinalIgnoreCase);
                    byte[] file = it.Common == null ? card.ReadFile(dir + "/ITEM/Itm" + c.FilePrefix + "00Param.pac") : k < 0 ? null : card.ReadFile(dir.Substring(0, k) + "/item/ItmCommonParam.pac");
                    if (file == null) continue;
                    lock (BrawlGate.Lock)
                        using (var tmp = new TempFile(".pac", file))
                        using (var root = NodeFactory.FromFile(null, tmp.Path))
                        {
                            var node = it.Common == null ? root.Children.FirstOrDefault() : root.Children.FirstOrDefault(x => string.Equals(x.Name, it.Common, StringComparison.OrdinalIgnoreCase))?.Children.FirstOrDefault();
                            if (node == null) continue;
                            var src = node.WorkingUncompressed;
                            var bytes = new byte[src.Length];
                            System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                            var m = Moveset.ItemParam(bytes);
                            if (m != null) book.Items.Add((it.Move, it.Name, m));
                        }
                }
                catch { }
        }

        /// <summary>The item's hitboxes, listed on the move that pulls it out: one per part of its scripts that hits
        /// ("Throw": when it's thrown; "Born": when it blows up).</summary>
        static void ItemHits(FighterBook b)
        {
            foreach (var (move, name, m) in b.Items)
                foreach (var e in b.Entries.Where(x => x.Parts.FirstOrDefault()?.Replace("Air", "") is string p && p.StartsWith(move) && x.Note != null && x.Note.Contains("item")))
                    foreach (var s in m.Subactions.Where(x => x.Main.Any(IsHit)))
                    {
                        var hits = FrameData.Run(m, s.Animation ?? name, new[] { s }, a => 60).Hits.Where(h => !h.Grab).ToList();
                        if (hits.Count == 0) continue;
                        string what = string.Equals(s.Animation, "Throw", StringComparison.OrdinalIgnoreCase) ? name + ", thrown"
                                    : string.Equals(s.Animation, "Born", StringComparison.OrdinalIgnoreCase) ? name + " blast" : name;
                        var data = new MoveData { Name = what }; data.Hits.AddRange(hits);
                        e.Data.Projectiles.Add(new Projectile { Name = what, Item = true, Data = data });
                    }
        }

        // R.O.B.'s gyro is an item: its hitboxes are in /item/ItmCommonParam.pac (ItmRobotGyroParam), how it moves in
        // /item/ItmParam.pac (its entry "R.O.B.'s Gyro"). Left out if either can't be read.
        static void ReadGyro(Fat32Image card, Character c, FighterBook book)
        {
            try
            {
                string dir = c.FighterFolderPath; int k = dir.IndexOf("/fighter/", StringComparison.OrdinalIgnoreCase);
                if (k < 0) return;
                var common = card.ReadFile(dir.Substring(0, k) + "/item/ItmCommonParam.pac");
                var param = card.ReadFile(dir.Substring(0, k) + "/item/ItmParam.pac");
                if (common == null || param == null) return;
                lock (BrawlGate.Lock)
                {
                    using (var tmp = new TempFile(".pac", common))
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                    {
                        var arc = root.Children.FirstOrDefault(x => string.Equals(x.Name, "ItmRobotGyroParam", StringComparison.OrdinalIgnoreCase));
                        if (arc == null || arc.Children.Count == 0) return;
                        var src = arc.Children[0].WorkingUncompressed;
                        var bytes = new byte[src.Length];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                        book.Item = Moveset.ItemParam(bytes);
                    }
                    using (var tmp = new TempFile(".pac", param))
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                    {
                        var e = root.Children.FirstOrDefault()?.Children.OfType<ItmParamEntryNode>().FirstOrDefault(x => x.Name.Contains("Gyro"));
                        if (e == null) { book.Item = null; return; }
                        book.ItemGravity = e.Gravity; book.ItemMaxFall = e.TerminalVelocity; book.ItemAirFriction = e.AirFriction; book.ItemGroundFriction = e.GroundFriction;
                    }
                }
            }
            catch { book.Item = null; }
        }

        static void Build(FighterBook b)
        {
            var m = b.Moves;
            // a name used more than once (Peach's air side B end): the copy the fighter's own actions switch to, else the first
            // with a script, else the first
            var played = new HashSet<int>(m.ActionEntry.SelectMany(l => l).Where(x => x.Id == 0x04000100 && x.Types.Length > 0 && x.Types[0] == 0).Select(x => x.Values[0]));
            // a special's script in the low, shared numbers that nothing plays, when a script in the fighter's own numbers covers
            // the same move (Peach's leftover air down B, beside the turnip pull her down B action plays): not a move
            bool Stray(Subaction s) => s.Index < 0x100 && s.Animation != null && s.Animation.StartsWith("Special") && !played.Contains(s.Index) &&
                m.Subactions.Any(o => o.Index >= 0x100 && (string.Equals(o.Animation, s.Animation, StringComparison.OrdinalIgnoreCase) ||
                                                           played.Contains(o.Index) && string.Equals(o.Animation, s.Animation.Replace("SpecialAir", "Special"), StringComparison.OrdinalIgnoreCase)));
            int Sub(string anim)
            {
                var all = m.Subactions.Where(s => string.Equals(s.Animation, anim, StringComparison.OrdinalIgnoreCase) && !Stray(s)).ToList();
                if (all.Count == 0) return -1;
                if (all.Count > 1 && all.Count(s => played.Contains(s.Index)) == 1) return m.Subactions.IndexOf(all.First(s => played.Contains(s.Index)));
                return m.Subactions.IndexOf(all.FirstOrDefault(s => s.Main.Count > 0) ?? all[0]);
            }
            int Len(string a) => b.Lengths.TryGetValue(a, out var l) ? l : 0;
            // what the scripts know while they run (the fighter's settings, their own variables), and which of the fighter's
            // actions plays each piece (its own script runs as the piece starts: P+ puts hitboxes and timers there)
            var acts = new Dictionary<int, (int, List<MoveEvent>)>();
            for (int k = 0; k < m.ActionEntry.Count; k++)
                foreach (var x in m.ActionEntry[k].Where(x => x.Id == 0x04000100 && x.Types.Length > 0 && x.Types[0] == 0))
                    if (!acts.ContainsKey(x.Values[0])) acts[x.Values[0]] = (0x112 + k, m.ActionEntry[k]);
            ScriptState St() => new ScriptState(m, b.Character.FilePrefix);
            (int, List<MoveEvent>) ActOf(Subaction sub) => acts.TryGetValue(sub.Index, out var a) ? a : (-1, null);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (g, n, a) in Standard)
            {
                var parts = a.Split('+');
                if (used.Contains(parts.Last())) continue;   // "Rapid jab" has two spellings
                var subs = parts.Select(Sub).ToList();
                if (subs.Last() < 0) continue;
                // a start part that doesn't exist is just skipped (some fighters' smashes have none)
                var keep = parts.Where((p, i) => subs[i] >= 0).ToArray();
                subs = subs.Where(i => i >= 0).ToList();
                var d = FrameData.Run(m, n, subs, Len, St(), ActOf);
                if (d.Frames.Count == 0) continue;
                if (g == "Aerials") d.LandingLag = (int)Math.Round(m.Attr(a == "AttackAirN" ? 0x140 : a == "AttackAirF" ? 0x144 : a == "AttackAirB" ? 0x148 : a == "AttackAirHi" ? 0x14C : 0x150));
                used.Add(parts.Last());
                b.Entries.Add(new MoveEntry { Group = g, Name = n, Parts = keep, Data = d });
            }
            // specials: every special subaction that hits, grabs or makes the fighter intangible
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in m.Subactions.Where(s => s.Animation != null && s.Animation.StartsWith("Special", StringComparison.OrdinalIgnoreCase) && !s.Animation.StartsWith("SpecialAir", StringComparison.OrdinalIgnoreCase) || (s.Animation?.StartsWith("SpecialAir", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                if (s.Main.Count == 0 || Stray(s) || m.Subactions.IndexOf(s) != Sub(s.Animation) || !seen.Add(s.Animation)) continue;   // a name used twice: the copy Sub picks
                var d = FrameData.Run(m, s.Animation, new[] { s.Index }, Len, St(), ActOf);
                if (d.Frames.Count == 0 || d.Hits.Count == 0 && d.Intangible.Count == 0 && d.Spawns.Count == 0) continue;
                var nm = SpecialName(s.Animation);
                string grp = nm.StartsWith("Neutral B") ? "Neutral B" : nm.StartsWith("Side B") ? "Side B" : nm.StartsWith("Up B") ? "Up B" : nm.StartsWith("Down B") ? "Down B" : "Neutral B";
                b.Entries.Add(new MoveEntry { Group = grp, Name = nm, Parts = new[] { s.Animation }, Data = d });
            }
            // a special played start to finish (its start, its middle, its end) as one row instead of each piece on its
            // own: Fox's side B is a 20-frame start, a 4-frame dash and a 40-frame end
            foreach (var key in new[] { "N", "S", "Hi", "Lw" })
                foreach (var air in new[] { false, true })
                {
                    string f = "Special" + (air ? "Air" : "") + key;
                    string Pick(params string[] names) => names.FirstOrDefault(n => Sub(n) >= 0 && Len(n) > 0);
                    var parts = new[] { Pick(f + "Start"), Pick(f + "Hold", f + "Loop", f + "Charge", f + "Move"), Pick(f), Pick(f + "Shoot", f + "Fire", f + "Light"), Pick(f + "End") }.Where(x => x != null).ToList();
                    // a quick "light" release (Squirtle's water gun tapped): its start, then the release; held, a charging part comes between
                    if (parts.Contains(f + "Light")) parts = parts.Where(x => x == f + "Start" || x == f + "Light").ToList();
                    if (parts.Count < 2) continue;
                    var d = FrameData.Run(m, f, parts.Select(Sub).ToList(), Len, St(), ActOf);
                    if (d.Frames.Count == 0) continue;
                    var nm = SpecialName(f);
                    string grp = nm.StartsWith("Side B") ? "Side B" : nm.StartsWith("Up B") ? "Up B" : nm.StartsWith("Down B") ? "Down B" : "Neutral B";
                    b.Entries.RemoveAll(e => e.Parts.Length == 1 && parts.Contains(e.Parts[0], StringComparer.OrdinalIgnoreCase));
                    b.Entries.Add(new MoveEntry { Group = grp, Name = nm, Parts = parts.ToArray(), Data = d });
                }
            // anything else that hits or makes something and isn't on a row yet: a row of its own, so no move is left out
            // (landing hits after an aerial, a second hit with its own animation, a crouch attack...)
            var covered = new HashSet<string>(b.Entries.SelectMany(x => x.Parts), StringComparer.OrdinalIgnoreCase);
            foreach (var s in m.Subactions)
            {
                var a = s.Animation;
                if (a == null || s.Main.Count == 0 || Len(a) <= 0 || covered.Contains(a) || Regex.IsMatch(a, Skip) || Stray(s)) continue;
                if (!s.Main.Any(x => IsHit(x) || x.Id == 0x10000100 || x.Id == 0x10000200)) continue;
                var d = FrameData.Run(m, a, new[] { s.Index }, Len, St(), ActOf);
                if (d.Frames.Count == 0) continue;
                covered.Add(a);
                var nm = StandardName(a) ?? (a.StartsWith("Special") ? SpecialName(a) : OtherName(a));
                string grp = nm.StartsWith("Neutral B") ? "Neutral B" : nm.StartsWith("Side B") ? "Side B" : nm.StartsWith("Up B") ? "Up B" : nm.StartsWith("Down B") ? "Down B"
                           : a.StartsWith("Landing") || a.StartsWith("AttackAir") ? "Aerials" : a.StartsWith("Attack") ? "Ground attacks" : "Other moves";
                b.Entries.Add(new MoveEntry { Group = grp, Name = nm, Parts = new[] { a }, Data = d, Extra = true });
            }
            // Pikachu's quick attack: its zip has no animation or subaction of its own (see QuickAttack)
            if (b.Character.FilePrefix == "Pikachu") QuickAttack(b, Sub, Len);
            // projectiles and other things a move makes (Fox's laser, his side B's image...)
            foreach (var e in b.Entries) AddProjectiles(b, e);
            // a row added for a move outside the usual list that turns out to show nothing (it only makes an item...)
            b.Entries.RemoveAll(e => e.Extra && e.Data.Hits.Count == 0 && e.Data.Projectiles.Count == 0 && !e.Sets);
            b.Entries.RemoveAll(e => e.Group.EndsWith(" B") && e.Data.Hits.Count == 0 && e.Data.Intangible.Count == 0 && e.Data.Projectiles.Count == 0 && !FighterMotion.Moves(b, e) && !e.Sets && !(e.Data.CodeShots.Count > 0 && e.Parts.Any(p => p.EndsWith("Light"))));
            // every special gets a row, even one whose hitboxes aren't in its script (Link's bomb pull, Lucas's PK fire...)
            foreach (var (grp, key) in new[] { ("Neutral B", "N"), ("Side B", "S"), ("Up B", "Hi"), ("Down B", "Lw") })
            {
                if (b.Entries.Any(e => e.Group == grp)) continue;
                var s = m.Subactions.Where(x => x.Animation != null && Regex.IsMatch(x.Animation, $"^Special{key}(Start|1|Hold|Shoot|Fire)?$"))
                                    .OrderBy(x => x.Main.Count > 0 ? 0 : 1).ThenBy(x => x.Animation.Length).FirstOrDefault();
                if (s == null) continue;
                var d = FrameData.Run(m, s.Animation, new[] { s.Index }, Len, St(), ActOf);
                if (d.Frames.Count == 0) continue;
                b.Entries.Add(new MoveEntry { Group = grp, Name = grp, Parts = new[] { s.Animation }, Data = d });
            }
            // what the game's code launches during a special (Samus's missiles, Ness's PK thunder...), on that special's own row
            foreach (var l in Launched.Where(x => x.Fighter == b.Character.FilePrefix).Select(x => x.Move).Distinct())
            {
                if (!l.StartsWith("Special") || b.Entries.Any(e => e.Parts.Any(pt => pt.Replace("Air", "") == l))) continue;
                var s = m.Subactions.FirstOrDefault(x => x.Animation == l);
                if (s == null) continue;
                var d = FrameData.Run(m, s.Animation, new[] { s.Index }, Len, St(), ActOf);
                if (d.Frames.Count == 0) continue;
                var nm = SpecialName(l);
                b.Entries.Add(new MoveEntry { Group = nm.StartsWith("Neutral B") ? "Neutral B" : nm.StartsWith("Side B") ? "Side B" : nm.StartsWith("Up B") ? "Up B" : "Down B", Name = nm, Parts = new[] { l }, Data = d });
            }
            // Kirby's stone: the falling and landing hits are in two scripts of their own (played by his down B's code
            // when he turns to stone in the air and when he lands), not in the down B scripts
            if (b.Character.FilePrefix == "Kirby")
                foreach (var (anim, pose, name) in new[] { ("DummySpecialLwToAir", "SpecialAirLw2", "Down B stone, falling"), ("DummySpecialLwToGround", "SpecialLw2", "Down B stone, landing") })
                {
                    var s = m.Subactions.FirstOrDefault(x => x.Animation == anim);
                    if (s == null || !b.Lengths.ContainsKey(pose) || b.Entries.Any(e => e.Parts.Contains(pose))) continue;
                    // no animation of their own: drawn on the stone pose, as long as the script goes (at least 30 frames)
                    var d = FrameData.Run(m, pose, new[] { s.Index }, x => Math.Max(30, Len(x)), St(), ActOf);
                    if (d.Hits.Count == 0) continue;
                    d.Animation = pose; int poseLen = Math.Max(1, Len(pose));
                    foreach (var f in d.Frames) { f.Animation = pose; f.AnimFrame = Math.Min(f.AnimFrame, poseLen); }
                    b.Entries.Add(new MoveEntry { Group = "Down B", Name = name, Parts = new[] { pose }, Data = d,
                        Note = anim.EndsWith("Air") ? "The stone's hit while it drops: it stays out until Kirby lands." : null, NoteIsInfo = true });
                }
            // Sheik's needles: charging (start, then the loop that adds a needle each time round) and throwing (the end)
            // are separate rows. Her neutral B scripts are empty (the game's code does it all); the needles leave as the
            // throw sound plays in the end animation (its sound event, after its 3-frame timer: frame 4).
            if (b.Character.FilePrefix == "Sheik" && Sub("SpecialNStart") >= 0 && Sub("SpecialNEnd") >= 0 && Len("SpecialNEnd") > 0)
            {
                b.Entries.RemoveAll(e => e.Group == "Neutral B");
                foreach (var air in new[] { "", "Air" })
                {
                    string tag = air == "" ? "" : " (air)";
                    var charge = new[] { $"Special{air}NStart", $"Special{air}NLoop" }.Where(x => Sub(x) >= 0 && Len(x) > 0).ToArray();
                    if (charge.Length > 0)
                    {
                        var dc = FrameData.Run(m, charge[0], charge.Select(Sub).ToList(), Len, St(), ActOf);
                        if (dc.Frames.Count > 0)
                            b.Entries.Add(new MoveEntry { Group = "Neutral B", Name = "Neutral B charge" + tag, Parts = charge, Data = dc, NoteIsInfo = true,
                                Note = "Charging: each time the loop goes round Sheik readies another needle (up to 6). Nothing hits until she throws: that's the throw row." });
                    }
                    string end = $"Special{air}NEnd";
                    if (Sub(end) < 0 || Len(end) <= 0) continue;
                    var dt = FrameData.Run(m, end, new List<int> { Sub(end) }, Len, St(), ActOf);
                    if (dt.Frames.Count > 0)
                        b.Entries.Add(new MoveEntry { Group = "Neutral B", Name = "Neutral B throw" + tag, Parts = new[] { end }, Data = dt,
                            Note = "Throws the needles readied (1 to 6). They leave on frame 4, when the throw sound plays; " + (air == "" ? "on the ground they fly straight ahead." : "in the air they fly 45° down.") });
                }
            }
            foreach (var e in b.Entries) AddLaunched(b, e);
            if (b.Character.FilePrefix == "Sheik")
                foreach (var e in b.Entries.Where(x => x.Group == "Neutral B"))
                {
                    if (e.Name.StartsWith("Neutral B charge")) e.Data.Projectiles.Clear();   // no needle while charging
                    else foreach (var p in e.Data.Projectiles.Where(x => x.Name == "Needle")) p.Frame = 4;
                }
            foreach (var e in b.Entries.Where(x => x.Note == null && x.Data.Hits.Count == 0))
                if (Harmless.FirstOrDefault(n => n.Fighter == b.Character.FilePrefix && NoteFits(n.Move, e.Parts[0].Replace("Air", ""))) is var hn && hn.Text != null) { e.Note = hn.Text; e.NoteIsInfo = true; }
            foreach (var e in b.Entries.Where(x => x.Note == null))
                if (Notes.FirstOrDefault(n => n.Fighter == b.Character.FilePrefix && NoteFits(n.Move, e.Parts[0].Replace("Air", ""))) is var note && note.Text != null) e.Note = note.Text;
                else if (e.Sets) e.Note = "Puts out something that only hits later, when it's set off.";
                else if (e.Group.EndsWith(" B") && e.Data.Hits.Count == 0 && e.Data.Projectiles.Count == 0 && e.Data.Intangible.Count == 0 && !FighterMotion.Moves(b, e)) e.Note = "No hitbox in this move's own files (the game's code handles what it does).";
            ItemHits(b);
            // Olimar's smashes and aerials: the Pikmin do the hitting
            if (b.Character.FilePrefix == "Pikmin") PikminAttacks(b);
            // Sheik's chain: its four hitboxes (special hitboxes 0-3, no bone of their own) are put on the chain's links by the
            // game's chain code, which also swings it. Drawn on the chain model (WpnSheikIronWire, links every 4 units out to
            // 28.5) held straight out of the hand it's held in (its article's bone, LHaveN): ids 0-3 on links 3, 5, 7 and 9,
            // the last (the strongest) at the tip.
            if (b.Character.FilePrefix == "Sheik")
            {
                var chain = b.Moves.Articles.FirstOrDefault(a => a.Slot == 7 && a.Bone > 0);
                if (chain != null && b.Lengths.Keys.Any(k => k.StartsWith("WpnSheikIronWire", StringComparison.OrdinalIgnoreCase)))
                    foreach (var e in b.Entries.Where(x => x.Parts.Any(pt => pt.Replace("Air", "") == "SpecialSStart")))
                    {
                        int[] links = { 4, 6, 8, 10 };
                        bool any = false;
                        foreach (var h in e.Data.Frames.SelectMany(f => f.Hitboxes).Concat(e.Data.Hits).Where(h => h.Special && h.Bone == 0 && h.Id >= 0 && h.Id < 4 && h.X == 0 && h.Y == 0 && h.Z == 0))
                        { h.ArticleModel = "WpnSheikIronWire"; h.ArticleAnim = null; h.AttachBone = chain.Bone; h.Bone = links[h.Id]; any = true; }
                        if (any) e.Note = "The chain is swung by the game's physics; it's drawn held straight out of Sheik's hand, with its four hitboxes along it (the strongest at the tip).";
                    }
            }
            // Peach's toad: the spores come out when the counter connects; the game's code makes them
            if (b.Character.FilePrefix == "Peach")
                foreach (var e in b.Entries.Where(x => x.Parts[0].Replace("Air", "") == "SpecialNHit" && x.Data.Hits.Count == 0 && x.Note == null))
                    e.Note = "Toad's spores come out when the counter is hit; the game's code makes them, so they aren't drawn.";
            // Pikachu's jolt and thunder: drawn by ProjectileFlightRules; these say in words what the game does with them
            if (b.Character.FilePrefix == "Pikachu")
            {
                float high = m.FighterF(3, 0), fall = Math.Abs(m.ArticleF(m.Articles.FirstOrDefault(a => a.Slot == 7), 4));
                int crawl = m.ArticleI(m.Articles.FirstOrDefault(a => a.Slot == 6), 0);
                foreach (var e in b.Entries.Where(x => x.Parts[0].Replace("Air", "") == "SpecialN"))
                    if (crawl > 0 && crawl <= 600)
                        e.Note = $"The jolt flies down at an angle until it reaches the floor (at once on the ground), then runs along the floor (and up walls) for up to {crawl} frames. How fast it runs is up to the game's code, so it's drawn at its flying speed.";
                foreach (var e in b.Entries.Where(x => x.Parts[0].Replace("Air", "") == "SpecialLw"))
                    if (!float.IsNaN(high) && !float.IsNaN(fall) && fall > 0)
                    { e.Note = $"A cloud appears {high:0} above Pikachu and a bolt falls from it at {fall:0.#} a frame; if it reaches him he's wrapped in the big hit (the \"Down B hit\" row)."; e.NoteIsInfo = true; }
            }
            if (b.Character.FilePrefix == "Robot" && b.Item != null && !float.IsNaN(b.ItemGravity)) GyroThrow(b);
            // a move whose animation isn't in the fighter's animation file can't be shown (it would stand in its default
            // pose): Kirby's copy abilities (their animations are with the fighter he copies), leftover unused moves
            if (b.Lengths.Count > 0)
                b.Entries.RemoveAll(e => e.Parts.Any(p => !b.Lengths.ContainsKey(p)) || e.Data.Total <= 1);
            // the fighter moved by the game's code (Fire Fox, Quick Attack...), then projectiles' flights (and the frames
            // after the move while one is still flying)
            foreach (var e in b.Entries) { var moved = FighterMotion.Apply(b, e); if (moved != null && e.Note == null) e.Note = "The fighter " + moved + "."; }
            foreach (var e in b.Entries) ProjectileFlight.Apply(b, e);
            // a ground and an air version (or several angles) that are exactly the same: one row
            Merge(b);
            // order specials: neutral, side, up, down; ground before air
            var specials = b.Entries.Where(e => e.Group.EndsWith(" B")).ToList();
            b.Entries.RemoveAll(e => e.Group.EndsWith(" B"));
            int Rank(MoveEntry e) { var a = e.Parts[0]; var k = a.Replace("SpecialAir", "Special"); int r = k.StartsWith("SpecialN") ? 0 : k.StartsWith("SpecialS") ? 1 : k.StartsWith("SpecialHi") ? 2 : k.StartsWith("SpecialLw") ? 3 : 4; return r * 2 + (a.StartsWith("SpecialAir") ? 1 : 0); }
            int at = b.Entries.FindIndex(e => e.Group == "Grabs & throws"); if (at < 0) at = b.Entries.Count;
            b.Entries.InsertRange(at, specials.OrderBy(Rank).ThenBy(e => m.Subactions.FindIndex(s => s.Animation == e.Parts[0])));
            // every row in its section (ones added for moves outside the usual list go with their kind), in order otherwise
            int Section(string g) => g == "Ground attacks" ? 0 : g == "Aerials" ? 1 : g.EndsWith(" B") ? 2 : g == "Grabs & throws" ? 3 : g == "Dodges & getups" ? 4 : 5;   // "Other moves" last
            b.Entries = b.Entries.Select((e, i) => (e, i)).OrderBy(x => Section(x.e.Group)).ThenBy(x => x.i).Select(x => x.e).ToList();
        }

        static void AddProjectiles(FighterBook b, MoveEntry e)
        {
            var arts = b.Moves.Articles.OrderBy(a => a.Slot).ToList();
            bool air = e.Group == "Aerials" || e.Parts.Any(p => p.Contains("Air"));
            foreach (var (frame, index) in e.Data.Spawns)
            {
                var art = Numbered(b, index);
                if (art == null) continue;
                var withHits = HitScripts(art);
                if (withHits.Count == 0) continue;
                string kind = Kind(art);
                // the article's script for this very move if it has one (the whip's "AttackHi4" for up smash)
                var exact = withHits.FirstOrDefault(s => e.Parts.Any(pt => string.Equals(pt, s.Animation, StringComparison.OrdinalIgnoreCase)));
                if (exact != null)
                {
                    var d = FrameData.Run(b.Moves, exact.Animation, new[] { exact }, _ => 0);
                    if (d.Hits.Count == 0) continue;
                    var p = new Projectile { Name = $"{kind} ({Words(exact.Animation)})", Frame = frame, Article = art, Data = d };
                    if (!Attach(b, e, p, exact.Animation)) e.Data.Projectiles.Add(p);
                    continue;
                }
                // else what it does once it's out: not its start or charging (Lucario's aura sphere), by ground/air
                var main = withHits.Where(s => !Regex.IsMatch(s.Animation ?? "", "^(Start|Charge|Wait|Haved?)", RegexOptions.IgnoreCase)).ToList();
                if (main.Count == 0) main = withHits;
                // all it does is blow up (or the like) later, when something sets it off (Snake's C4 and mine): nothing hits now
                if (main.All(x => Regex.IsMatch(x.Animation ?? "", FollowUp, RegexOptions.IgnoreCase))) { e.Sets = true; continue; }
                // shots with charge levels (Shoot1..Shoot8): the weakest and the strongest, fired when the button is let go
                var levels = main.Where(s => Regex.IsMatch(s.Animation ?? "", @"^Shoot\d+$")).ToList();
                if (levels.Count >= 2)
                {
                    foreach (var (s, what) in new[] { (levels.First(), "uncharged"), (levels.Last(), "full charge") })
                    {
                        var d = FrameData.Run(b.Moves, s.Animation, new[] { s }, _ => 0);
                        if (d.Hits.Count > 0) e.Data.Projectiles.Add(new Projectile { Name = $"{kind} ({what})", Frame = 0, Article = art, Data = d });
                    }
                    continue;
                }
                var pick = main.FirstOrDefault(s => (s.Animation ?? "").Contains("Air") == air) ?? main[0];
                var pd = FrameData.Run(b.Moves, pick.Animation, new[] { pick }, _ => 0);
                if (pd.Hits.Count == 0) continue;
                var pr = new Projectile { Name = $"{kind} ({Words(pick.Animation)})", Frame = frame, Article = art, Data = pd };
                if (Attach(b, e, pr, pick.Animation)) continue;
                e.Data.Projectiles.Add(pr);
                // what it does after (a boomerang coming back, a missile blowing up): the game decides when
                foreach (var s in main.Where(x => x != pick && Regex.IsMatch(x.Animation ?? "", FollowUp, RegexOptions.IgnoreCase)))
                {
                    var d = FrameData.Run(b.Moves, s.Animation, new[] { s }, _ => 0);
                    if (d.Hits.Count > 0) e.Data.Projectiles.Add(new Projectile { Name = $"{kind} ({Words(s.Animation)})", Frame = 0, Article = art, Data = d });
                }
            }
            // an article this move tells to do something (Snake's C4 told to blow up): what that action plays, from that frame
            foreach (var (frame, index, action) in e.Data.ArticleActions)
            {
                var art = Numbered(b, index);
                if (art == null || action < 0 || action >= art.Actions.Count) continue;
                var act = art.Actions[action];
                var s = act.Where(x => x.Id == 0x04000100 && x.Types.Length > 0 && x.Types[0] == 0 && x.Values[0] >= 0 && x.Values[0] < art.Subactions.Count)
                           .Select(x => art.Subactions[x.Values[0]]).FirstOrDefault(Hits);
                if (s == null && act.Any(IsHit)) s = new Subaction { Index = -1, Animation = "Hit", Main = act };
                if (s == null) continue;
                var d = FrameData.Run(b.Moves, s.Animation, new[] { s }, _ => 0);
                if (d.Hits.Count == 0) continue;
                e.Data.Projectiles.Add(new Projectile { Name = $"{Kind(art)} ({Words(s.Animation)})", Frame = frame, Article = art, Data = d });
            }
            foreach (var p in e.Data.Projectiles.Where(q => q.Name.EndsWith("(hit)")))
                if (Names.FirstOrDefault(n => n.Fighter == b.Character.FilePrefix && n.Slot == p.Article?.Slot).Name is string nm) p.Name = nm;
            // a held thing that plays along with the move without being made by it (ZSS's whip in her up B is out from the
            // start): its script for this move has hitboxes, so they're the move's too
            foreach (var art in arts.Where(x => x.Bone > 0))
            {
                if (e.Data.Hits.Any(h => h.ArticleModel != null || h.OnArticle)) break;
                bool any = false;
                foreach (var part in e.Parts)
                {
                    var own = art.Subactions.FirstOrDefault(s => string.Equals(part, s.Animation, StringComparison.OrdinalIgnoreCase) && s.Main.Any(x => x.Id == 0x06000D00 || x.Id == 0x06150F00));
                    if (own == null) continue;
                    var d = FrameData.Run(b.Moves, own.Animation, new[] { own }, _ => 0);
                    if (d.Hits.Count == 0) continue;
                    // it plays along from where this part of the move starts
                    int from = e.Data.Frames.FirstOrDefault(f => string.Equals(f.Animation, part, StringComparison.OrdinalIgnoreCase))?.Frame ?? 1;
                    var held = new Projectile { Name = "Held", Frame = from, Article = art, Data = d };
                    // with its own animation it's posed by that; without one (Ness's yo-yo: the game moves it, and the move's
                    // animation sweeps the bone it hangs off, ThrowN, along its path) its hitboxes go on that bone
                    if (!Attach(b, e, held, own.Animation)) OnBone(e, held);
                    any = true;
                }
                if (any) break;
            }
        }

        // not moves: getting hit, ledges, items, taunts, entries, final smashes (they have their own screens or aren't attacks)
        const string Skip = "^(Final|Appeal|Entry|Win|Lose|Item|Ladder|Swim|Cliff|Down|Damage|Capture|Thrown|Glide|Rebirth|Escape|Guard|Swing|Wait|Turn|Run|Walk|Dash$|Jump|Fall|Pass|Squat|Stand|Slip|Ottotto|Heavy|Light|AirCatch|Catch(Wait|Cut|Pull)|Throw[FBHL].*Item|Shoulder|WessDance|SwordSlash|Furafura|Stop|Rebound)";
        static string OtherName(string a)
        {
            var land = Regex.Match(a, "^LandingAir(N|F|B|Hi|Lw)$");
            if (land.Success) return (land.Groups[1].Value == "N" ? "Neutral" : land.Groups[1].Value == "F" ? "Forward" : land.Groups[1].Value == "B" ? "Back" : land.Groups[1].Value == "Hi" ? "Up" : "Down") + " air landing";
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AttackSquat"] = "Crouch attack", ["AttackDashAir"] = "Dash attack (off the edge)", ["LandingFallSpecial"] = "Landing (after an up B)",
                ["AttackLw31"] = "Down tilt 1", ["AttackLw32"] = "Down tilt 2", ["AttackLw41"] = "Down smash 1", ["AttackLw42"] = "Down smash 2",
                ["AttackS4SHi"] = "Forward smash (up)", ["AttackS4SLw"] = "Forward smash (down)",
                ["AttackHi4Ice"] = "Up smash (ice)", ["AttackLwShank"] = "Down attack (knife)", ["SmashThrowAirLw"] = "Down throw (in the air)", ["ThrowCutter"] = "Cutter throw",
                ["AttackS4Hold"] = "Forward smash (charging)", ["AttackHi4Hold"] = "Up smash (charging)", ["AttackLw4Hold"] = "Down smash (charging)",
            };
            if (known.TryGetValue(a, out var n)) return n;
            var w = Words(a); return char.ToUpper(w[0]) + w.Substring(1);
        }

        const string FollowUp = "Turn|Explosion|Burst|Impact|Break|Detach|Blast";
        static bool IsHit(MoveEvent x) => x.Id == 0x06000D00 || x.Id == 0x06150F00;
        static bool Hits(Subaction s) => s != null && s.Main.Any(IsHit);
        static string Words(string a) => Regex.Replace(a ?? "?", "([a-z])([A-Z0-9])", "$1 $2").ToLowerInvariant();
        static string Kind(Article art) => art.Subactions.Any(s => Regex.IsMatch(s.Animation ?? "", "Fly|Shoot|Bullet|Arrow|Throw|Move|Roll|Fall|Regular", RegexOptions.IgnoreCase)) ? "Projectile" : "Spawned hitbox";

        /// <summary>An article's scripts that hit. When none of its subactions do, its hitboxes are in one of its actions' own
        /// first action's script (Mewtwo's Disable): that script, as one to run.</summary>
        static List<Subaction> HitScripts(Article art)
        {
            var list = art.Subactions.Where(Hits).ToList();
            if (list.Count > 0) return list;
            // a new article starts in its first action
            if (art.Actions.Count > 0 && art.Actions[0].Any(IsHit)) list.Add(new Subaction { Index = -1, Animation = "Hit", Main = art.Actions[0] });
            return list;
        }

        /// <summary>Pikachu's quick attack, start to finish: its start; the zip, an action with no animation of its own (the
        /// game holds the air start's frame 12 while moving him) that lasts as long as its script's countdown (the fighter value
        /// it starts from, 24003, is parameter block 2 +4 in ft_pikachu's table) with the hitbox its script sets; then the end.
        /// Shown with no stick: straight up at block 2 +0x38 a frame (the stick adds up to +0x34 and picks the way).</summary>
        static void QuickAttack(FighterBook b, Func<string, int> sub, Func<string, int> len)
        {
            var m = b.Moves;
            int warp = m.FighterI(2, 4), act = 0x117 - 0x112, end = sub("SpecialAirHiEnd");
            float speed = m.FighterF(2, 0x38), more = m.FighterF(2, 0x34), again = m.FighterF(2, 0x3C);
            if (warp <= 0 || warp > 60 || float.IsNaN(speed) || act >= m.ActionEntry.Count || end < 0 || len("SpecialAirHiEnd") <= 0) return;
            var hitEv = m.ActionEntry[act].FirstOrDefault(IsHit);
            var made = new List<MoveEntry>();
            foreach (var air in new[] { false, true })
            {
                string startName = air ? "SpecialAirHiStart" : "SpecialHiStart";
                int st = sub(startName); if (st < 0 || len(startName) <= 0) continue;
                var md = FrameData.Run(m, air ? "SpecialAirHi" : "SpecialHi", new List<int> { st }, len);
                if (md.Frames.Count == 0) continue;
                int at = md.Frames.Count; var last = md.Frames[at - 1];
                var script = new List<MoveEvent>(); if (hitEv != null) script.Add(hitEv);
                script.Add(new MoveEvent { Id = 0x00010100, Types = new[] { 1 }, Values = new[] { warp * 60000 } });
                var zip = FrameData.Run(m, "zip", new[] { new Subaction { Index = -1, Main = script } }, _ => 0);
                float y = 0;
                for (int k = 0; k < warp; k++)
                {
                    y += speed;
                    md.Frames.Add(new FrameState { Frame = at + k + 1, Animation = "SpecialAirHiStart", AnimFrame = 13, Hidden = last.Hidden, MoveY = y,
                                                   Hitboxes = k < zip.Frames.Count ? zip.Frames[k].Hitboxes.Select(h => h.Copy()).ToList() : new List<Hitbox>() });
                }
                md.Hits.AddRange(zip.Hits);
                var en = FrameData.Run(m, "SpecialAirHiEnd", new List<int> { end }, len);
                int off = at + warp;
                foreach (var f in en.Frames) { f.Frame += off; f.MoveY = y; md.Frames.Add(f); }
                foreach (var h in en.Hits) if (!md.Hits.Contains(h)) md.Hits.Add(h);
                md.Total = md.Frames.Count; md.Iasa = en.Iasa > 0 ? en.Iasa + off : 0;
                md.Subactions = new List<int> { st, end };
                FrameData.Windows(md);
                made.Add(new MoveEntry { Group = "Up B", Name = air ? "Up B (air)" : "Up B", Parts = new[] { startName, "SpecialAirHiEnd" }, Data = md,
                    Note = $"Zips straight up with no stick: {speed:0.#} a frame for {warp} frames (up to {speed + more * 0.999f:0.#} a frame with the stick held, which picks the way). Aim again for a second, shorter zip ({again * 100:0}%)." });
            }
            if (made.Count == 0) return;
            var mine = new HashSet<string>(new[] { "SpecialHiStart", "SpecialAirHiStart", "SpecialHiEnd", "SpecialAirHiEnd" }, StringComparer.OrdinalIgnoreCase);
            b.Entries.RemoveAll(x => x.Parts.All(mine.Contains));
            b.Entries.AddRange(made);
        }

        // The number in a "generate article" event is the article's place in the fighter's moveset, except where the
        // fighter's own game code numbers them differently (worked out from each article's scripts: Link's bow has the
        // "BowN" ones, his arrow sticks, his boomerang turns back). Each exception names a script its article must have,
        // so a build whose files differ falls back to the moveset's order. Slot -1: not an article with hitboxes.
        static readonly (string Fighter, int Number, int Slot, string Has)[] Numbering =
        {
            ("Link", 0, 13, "BowN"), ("Link", 1, 15, "Stick"), ("Link", 2, 11, "Turn"),
            ("ToonLink", 0, 13, "BowN"), ("ToonLink", 1, 15, "Stick"), ("ToonLink", 2, 11, "Turn"),
            ("Dedede", 0, 8, "Fly"), ("Dedede", 1, 10, "HoldMax"), ("Dedede", 2, 12, "Fly"),
            ("Lucas", 4, 12, "CatchPull"),
            ("Popo", 0, 12, null), ("Popo", 1, 13, "StartFall"),
            ("Snake", 2, 8, "Shoot"), ("Snake", 3, 9, "Detach"), ("Snake", 4, 10, "StickTarget"), ("Snake", 5, -1, null), ("Snake", 6, 13, "StopFall"),
            ("Snake", 12, -1, null), ("Snake", 13, -1, null),
            // Peach's down B makes article 2 only when the game's code has no item for her to pull (her script's condition, which
            // is first in it): normally it's a turnip, an item
            ("Peach", 2, -1, null),
        };

        static Article Numbered(FighterBook b, int number)
        {
            var arts = b.Moves.Articles.OrderBy(a => a.Slot).ToList();
            foreach (var n in Numbering.Where(x => x.Fighter == b.Character.FilePrefix && x.Number == number))
            {
                if (n.Slot < 0) return null;
                var a = arts.FirstOrDefault(x => x.Slot == n.Slot);
                if (a != null && (n.Has == null || a.Subactions.Any(s => s.Animation == n.Has))) return a;
                if (n.Has == null) return null;   // the article the game uses isn't there: nothing, rather than the wrong one
            }
            return number >= 0 && number < arts.Count ? arts[number] : null;
        }

        // Projectiles the game's code makes during a special, with no "generate article" in the move's script: the
        // special (ground name), the article's place in the moveset, a script it must have, and which of its scripts to
        // show ("script:what it is", several split by |).
        static readonly (string Fighter, string Move, int Slot, string Scripts)[] Launched =
        {
            ("Samus", "SpecialN", 8, "Shoot0:charge shot, uncharged|Shoot7:charge shot, full"),
            ("Samus", "SpecialS", 12, "Homing:homing missile"), ("Samus", "SpecialS", 20, "Straight:super missile (smash input)"),
            ("Samus", "SpecialLw", 10, "BurstAttack:bomb"),
            ("Yoshi", "SpecialHi", 6, "Burst:egg"), ("Yoshi", "SpecialLw", 5, "Move:star"),
            ("Pikachu", "SpecialLw", 7, "Regular:thunder bolt"),
            ("Ness", "SpecialN", 7, "Bang:PK flash"), ("Ness", "SpecialHi", 9, "Move:PK thunder"),
            ("Lucas", "SpecialN", 7, "Bang:PK freeze"), ("Lucas", "SpecialS", 8, "Shoot:PK fire"), ("Lucas", "SpecialHi", 9, "Move:PK thunder"),
            ("Sheik", "SpecialN", 5, "Move:needle"),
            ("GameWatch", "SpecialN", 6, "Sausage:chef's food"),
            ("PokeLizardon", "SpecialS", 7, "Move:rock"),
            ("PokeZenigame", "SpecialN", 5, "Regular:water gun"),
            ("PokeFushigisou", "SpecialS", 4, "Move:razor leaf"),
            ("Robot", "SpecialN", 8, "Fly:laser|FlyMax:laser, full charge"),
            ("Wario", "SpecialS", 4, "SpecialSDrive:bike"),
            // (not Ike's sword in up B: the game only throws it in his Final Smash; in Aether his own hitboxes do the hitting)
            ("Snake", "AttackHi4", 7, "Impact:mortar shell"),
            ("Sonic", "SpecialHi", 6, "Fall:spring"), ("Knuckles", "SpecialHi", 6, "Fall:spring"),
            ("Zelda", "SpecialS", 7, "Move:Din's Fire blast"),
        };

        // what an article is, when its scripts don't say (its hitboxes are in its action's script, under no name)
        static readonly (string Fighter, int Slot, string Name)[] Names =
        {
            ("Mewtwo", 10, "Disable"), ("Lucario", 10, "Force Palm blast"),
        };

        static readonly (string Fighter, string Move, string Text)[] Notes =
        {
            ("Link", "SpecialLw", "Pulls out a bomb (an item). It hits when it blows up: thrown at someone, or when its fuse runs out. Its numbers are from P+'s item data."),
            ("ToonLink", "SpecialLw", "Pulls out a bomb (an item). It hits when it blows up: thrown at someone, or when its fuse runs out. Its numbers are from P+'s item data."),
            ("Snake", "SpecialN", "The grenade is an item: it blows up when its fuse runs out, thrown or not. Its numbers are from P+'s item data."),
            ("Peach", "SpecialLw", "Pulls a turnip (an item). It hits when thrown; how hard depends on its face and how fast it's going, so its damage shows as ?."),
            ("Diddy", "SpecialN", "Peanuts are items: they fly farther the longer you charge. Their numbers are from P+'s item data."),
            ("Pikmin", "SpecialS", "Throws a Pikmin. It sticks to whoever it touches and hits them over and over; how hard and how long depends on its colour, which the game's code decides, so its hits aren't drawn."),
            ("Dedede", "SpecialS", "Waddle Dees, Doos and Gordos are thrown helpers: their own moves aren't shown here."),
            ("GameWatch", "SpecialN", "The food flies along one of five set arcs picked at random."),
            ("PokeZenigame", "SpecialN", "Shown tapped. Holding B charges first, then shoots more, faster water."),
            ("Snake", "AttackLw4", "Sets a mine. It blows up when someone steps on it, so it doesn't hit during this move."),
            ("Snake", "SpecialLwStart$", "Sets C4 (on the floor, a wall or someone). It blows up when you press down B again: that's the \"Down B blast\" row."),
            ("Snake", "SpecialHiStart$", "The Cypher only hits once Snake lets go of it."),
            ("Snake", "SpecialLwBlast$", "Sets off the C4. It blows up wherever it's stuck: it's drawn where Snake is."),
            ("Sheik", "SpecialLw2$", "Turns into Zelda. Its script sets hitboxes and removes them on the same frame, so it never hits."),
            ("Marth", "SpecialLw$", "A counter: it only hits when something hits it first. The counterattack is the \"Down B hit\" row."),
            ("Roy", "SpecialLw$", "A counter: it only hits when something hits it first. The counterattack is the \"Down B hit\" row."),
            ("Ike", "SpecialLw$", "A counter: it only hits when something hits it first. The counterattack is the \"Down B hit\" row."),
        };

        // moves that really don't hit (nothing for the app to draw): the note says what they do instead
        static readonly (string Fighter, string Move, string Text)[] Harmless =
        {
            ("Pikmin", "SpecialN", "Plucks a Pikmin from the ground. Plucking doesn't hit anyone."),
            ("Mewtwo", "SpecialHi", "Teleports. It doesn't hit anyone."),
            ("Kirby", "SpecialLw1$", "Turns Kirby to stone. The stone's hits are on the \"stone, falling\" and \"stone, landing\" rows."),
            ("Pikmin", "SpecialLw", "Whistles the Pikmin back into line behind Olimar. The whistle doesn't hit anyone."),
        };

        // a note's move: "SpecialS" covers every piece of side B (start, end...); "SpecialLw$" only that exact piece
        static bool NoteFits(string move, string part) => move.EndsWith("$") ? string.Equals(part, move.TrimEnd('$'), StringComparison.OrdinalIgnoreCase) : part.StartsWith(move);

        static void AddLaunched(FighterBook b, MoveEntry e)
        {
            string Plain(string pt) => pt.Replace("Air", "");
            foreach (var l in Launched.Where(x => x.Fighter == b.Character.FilePrefix))
            {
                // on the rows that play the move itself (its ground and air versions); if none, the first row of that special
                // on each row that plays that special (its pieces are joined into one row, ground and air)
                if (!e.Parts.Any(pt => Regex.IsMatch(Plain(pt), "^" + l.Move + "(Start|Hold|Loop|Charge|Shoot|Fire|End)?$"))) continue;
                if (e.Data.Projectiles.Any(q => q.Article?.Slot == l.Slot)) continue;
                var art = b.Moves.Articles.FirstOrDefault(a => a.Slot == l.Slot);
                if (art == null) continue;
                foreach (var part in l.Scripts.Split('|'))
                {
                    var bits = part.Split(':');
                    var s = art.Subactions.FirstOrDefault(x => x.Animation == bits[0] && x.Main.Any(y => y.Id == 0x06000D00 || y.Id == 0x06150F00));
                    if (s == null) continue;
                    var d = FrameData.Run(b.Moves, s.Animation, new[] { s }, _ => 0);
                    if (d.Hits.Count == 0) continue;
                    e.Data.Projectiles.Add(new Projectile { Name = char.ToUpper(bits[1][0]) + bits[1].Substring(1), Frame = 0, Article = art, Data = d });
                }
            }
        }

        /// <summary>A thing the fighter holds while the move plays (ZSS's whip in her neutral air): its hitboxes become the
        /// move's own, drawn where the thing is (its model, posed by its animation for this move, hanging off the fighter's
        /// bone). True when it was such a thing.</summary>
        static bool Attach(FighterBook b, MoveEntry e, Projectile p, string anim)
        {
            if (string.IsNullOrEmpty(anim) || p.Article.Bone <= 0) return false;
            // its animation for this move: "Wpn<Model>[C05]<Anim>" in the motion file
            var chr = b.Lengths.Keys.FirstOrDefault(k => k.StartsWith("Wpn", StringComparison.OrdinalIgnoreCase) && k.EndsWith(anim, StringComparison.OrdinalIgnoreCase));
            if (chr == null) return false;
            var model = Regex.Replace(chr.Substring(0, chr.Length - anim.Length), "[A-Z][0-9]{2}$", "");
            if (model.Length <= 3) return false;
            var md = e.Data;
            foreach (var f in p.Data.Frames)
            {
                int at = p.Frame + f.Frame - 1;
                var target = md.Frames.FirstOrDefault(x => x.Frame == at);
                if (target == null) continue;
                foreach (var h in f.Hitboxes)
                {
                    var c = h.Copy(); c.ArticleModel = model; c.ArticleAnim = chr; c.AttachBone = p.Article.Bone; c.ArticleFrame = f.AnimFrame > 0 ? f.AnimFrame : f.Frame;
                    target.Hitboxes.Add(c);
                    if (!md.Hits.Any(x => x.Id == c.Id && x.Bone == c.Bone && x.ArticleModel == c.ArticleModel)) md.Hits.Add(c);
                }
            }
            FrameData.Windows(md);
            return true;
        }

        static readonly string[] PikminColours = { "red", "yellow", "blue", "white", "purple" };

        /// <summary>Olimar's smashes and aerials: his script sets flag 0x22000010 and the game sends the Pikmin in front
        /// (ftPikminStatusUniqProcessSmashAttack::execFixPos). The Pikmin's hitboxes are in its own scripts, one set per
        /// colour (Moveset.ArticleColourScript); its animation (WpnPikminPikmin..., in the costume file) carries it out from
        /// Olimar. Shown with a red Pikmin; the note gives each colour's damage.</summary>
        // R.O.B.'s down B throws the gyro (an item): ftRobotStatusUniqProcessSpecialGyro::execFixPos, when the move sets flag
        // 0x22000011, makes it at node 0xA2 (162) + (forward setting 0xFD2, up 0xFD3) and throws it forward at setting 0xFD0
        // + charge * 0xFD1 (the charge goes up to setting 0xFCF frames). The item then falls with its own gravity
        // and slides along the floor slowing by its floor friction (ItmParam). Its hitboxes are its own scripts: "Throw"
        // while thrown, "WaitGround" spinning on the floor. Drawn uncharged and fully charged.
        static void GyroThrow(FighterBook b)
        {
            var m = b.Moves; var g = b.Item;
            float Setting(int id) => SettingIds.Find(b.Character.FilePrefix, id, out int blk, out int off) ? m.FighterF(blk, off) : float.NaN;
            float max = Setting(0xFCF), v0 = Setting(0xFD0), per = Setting(0xFD1), fwd = Setting(0xFD2), up = Setting(0xFD3);
            if (float.IsNaN(v0) || v0 <= 0 || float.IsNaN(per) || float.IsNaN(max) || max < 0 || max > 600) return;
            if (float.IsNaN(fwd)) fwd = 0; if (float.IsNaN(up)) up = 0;
            Hitbox HitOf(string anim)
            {
                var s = g.Subactions.FirstOrDefault(x => string.Equals(x.Animation, anim, StringComparison.OrdinalIgnoreCase) && x.Main.Any(IsHit));
                if (s == null) return null;
                return FrameData.Run(g, anim, new[] { s }, a => 60).Hits.FirstOrDefault(h => !h.Grab);
            }
            var thrown = HitOf("Throw");
            if (thrown == null) return;
            var spin = HitOf("WaitGround") ?? thrown;
            float grav = b.ItemGravity, maxFall = Math.Abs(b.ItemMaxFall), airFr = Math.Max(0, b.ItemAirFriction), floorFr = b.ItemGroundFriction;
            if (float.IsNaN(grav) || grav <= 0 || float.IsNaN(floorFr) || floorFr <= 0) return;
            foreach (var e in b.Entries.Where(x => x.Parts.FirstOrDefault()?.Replace("Air", "") == "SpecialLw" && x.Data.Hits.Count == 0))
            {
                int at = e.Data.FlagsSet.Where(f => f.Var == 0x22000011).Select(f => f.Frame).DefaultIfEmpty(0).Min();
                if (at <= 0) continue;
                bool air = e.Parts[0].Contains("Air");
                foreach (var (speed, name) in new[] { (v0, "gyro, uncharged"), (v0 + max * per, "gyro, full charge") })
                {
                    if (speed <= 0) continue;
                    // its path from a start this high above the floor, and the frame it lands on
                    (List<(float X, float Y)> Path, int Landed) Sim(float h)
                    {
                        var p = new List<(float, float)>(); float x = 0, y = 0, vx = speed, vy = 0; int landed = h <= 0.01f ? 0 : -1;
                        for (int k = 0; k < 400; k++)
                        {
                            if (landed >= 0 && vx <= 0) break;
                            p.Add((x, y));
                            if (landed < 0)
                            {
                                x += vx; y += vy; vy = Math.Max(-maxFall, vy - grav); vx = Math.Max(0, vx - airFr);
                                if (y <= -h) { y = -h; landed = k + 1; }
                            }
                            else { x += vx; vx = Math.Max(0, vx - floorFr); }
                        }
                        return (p, landed < 0 ? p.Count : landed);
                    }
                    List<List<Hitbox>> HitsFrom(float h)
                    {
                        var r = Sim(h);
                        return r.Path.Select((q, k) => new List<Hitbox> { (k < r.Landed ? thrown : spin).Copy() }).ToList();
                    }
                    float tallest = air ? 40 : 10;
                    var first = Sim(tallest);
                    float slide = speed * speed / (2 * floorFr);
                    var f = new Flight
                    {
                        Start = at, Path = first.Path, Hits = HitsFrom(tallest), Bone = 162, Forward = fwd, Up = up,
                        About = $"thrown at {speed:0.##} a frame, it drops to the floor and slides about {slide:0} units, slowing by {floorFr:0.###} a frame, with its thrown hit until it lands and its spinning hit after. "
                              + "The game also makes it hit harder the faster it goes; how much isn't in the game's files, so the damage shown is its script's",
                        PathForHeight = h => Sim(h).Path, HitsForHeight = HitsFrom,
                    };
                    var data = new MoveData { Name = name };
                    data.Hits.Add(thrown); if (spin != thrown) data.Hits.Add(spin);
                    e.Data.Projectiles.Add(new Projectile { Name = name, Frame = at, Data = data, Flight = f });
                }
                e.Note = $"The gyro is an item: drawn thrown uncharged and fully charged ({max:0} frames of charging), from P+'s item data for it.";
                e.NoteIsInfo = true;
            }
        }

        static void PikminAttacks(FighterBook b)
        {
            var m = b.Moves;
            var pik = m.Articles.FirstOrDefault(a => a.Slot == 10);
            if (pik == null) return;
            foreach (var e in b.Entries.Where(x => (x.Group == "Ground attacks" || x.Group == "Aerials") && x.Data.Hits.Count == 0 && x.Data.Projectiles.Count == 0))
            {
                int sent = e.Data.FlagsSet.Where(f => f.Var == 0x22000010).Select(f => f.Frame).DefaultIfEmpty(0).Min();
                if (sent <= 0) continue;
                var part = e.Parts.LastOrDefault(p => !p.EndsWith("Start"));
                if (part == null) continue;
                string sub = part == "AttackS4S" ? "AttackS4SJump" : part;
                int si = pik.Subactions.FindIndex(s => string.Equals(s.Animation, sub, StringComparison.OrdinalIgnoreCase));
                var chr = b.Lengths.Keys.FirstOrDefault(k => k.StartsWith("WpnPikminPikmin", StringComparison.OrdinalIgnoreCase) && k.EndsWith(sub, StringComparison.OrdinalIgnoreCase));
                if (si < 0 || chr == null) continue;
                // the scripts test a power value (10000008, 0 to 2): drawn at 0, the note gives the range
                MoveData Run(int colour, int power = 0)
                {
                    var ev = m.ArticleColourScript(pik, colour, si);
                    if (ev == null || ev.Count == 0) return null;
                    var st = new ScriptState(m, b.Character.FilePrefix); st.Set(0x10000008, power);
                    return FrameData.Run(m, chr, new[] { new Subaction { Index = -1, Animation = chr, Main = ev } }, a => b.Lengths.TryGetValue(a, out var l) ? l : 0, state: st);
                }
                // where it is: aerials hang it on his right hand (node 0x29, RHaveN); smashes hold it at his feet plus his
                // settings 0xFA4/0xFA5 (forward smash) or 0xFA6/0xFA7 (up smash); down smash sends one each way from his feet
                float Setting(int id) => SettingIds.Find(b.Character.FilePrefix, id, out int blk, out int off) ? m.FighterF(blk, off) : 0f;
                bool aerial = sub.StartsWith("AttackAir");
                float ox = sub == "AttackS4SJump" ? Setting(0xFA4) : sub == "AttackHi4" ? Setting(0xFA6) : 0, oy = sub == "AttackS4SJump" ? Setting(0xFA5) : sub == "AttackHi4" ? Setting(0xFA7) : 0;
                if (float.IsNaN(ox)) ox = 0; if (float.IsNaN(oy)) oy = 0;
                bool both = sub == "AttackLw4";
                var red = Run(0);
                if (red == null || red.Hits.Count == 0) continue;
                var md = e.Data;
                foreach (var f in red.Frames)
                {
                    int at = sent + f.Frame - 1;
                    var target = md.Frames.FirstOrDefault(x => x.Frame == at);
                    if (target == null)
                    {
                        // it can still be out after Olimar's move ends: he holds his last pose
                        var last = md.Frames[md.Frames.Count - 1];
                        if (at != last.Frame + 1 || f.Hitboxes.Count == 0) continue;
                        target = new FrameState { Frame = at, AnimFrame = last.AnimFrame, Animation = last.Animation, After = true, MoveX = last.MoveX, MoveY = last.MoveY, Hidden = last.Hidden };
                        md.Frames.Add(target);
                    }
                    foreach (var h in f.Hitboxes)
                        foreach (bool back in both ? new[] { false, true } : new[] { false })
                        {
                            var c = h.Copy(); c.ArticleModel = "WpnPikminR"; c.ArticleAnim = chr; c.ArticleFrame = f.AnimFrame > 0 ? f.AnimFrame : f.Frame;
                            c.AttachBone = aerial ? 41 : -2; c.ArticleNoTurn = aerial; c.ArticleX = ox; c.ArticleY = oy; c.ArticleMirror = back;
                            if (back) c.Id += 100;   // the one behind: its own hitbox
                            target.Hitboxes.Add(c);
                            if (!back && !md.Hits.Any(x => x.Id == c.Id && x.Damage == c.Damage && x.Angle == c.Angle)) md.Hits.Add(c);
                        }
                }
                FrameData.Windows(md);
                string Range(int k)
                {
                    var lo = k == 0 ? red : Run(k); var hi = Run(k, 2);
                    if (lo == null || lo.Hits.Count == 0) return null;
                    float a = lo.Hits.Max(h => h.Damage), z = hi == null || hi.Hits.Count == 0 ? a : hi.Hits.Max(h => h.Damage);
                    return PikminColours[k] + " " + (Math.Abs(z - a) < 0.05f ? $"{a:0.#}%" : $"{a:0.#}-{z:0.#}%");
                }
                var by = Enumerable.Range(0, 5).Select(Range).Where(x => x != null).ToList();
                e.Note = "The Pikmin does the hitting; shown with a red one" + (both ? " (one goes each way)" : aerial ? ", hanging from his hand" : "") +
                         ". Each colour hits differently (strongest hit: " + string.Join(", ", by) + "; the higher numbers come with a Pikmin power value the game's code sets).";
                e.NoteIsInfo = true;
            }
            foreach (var e in b.Entries.Where(x => (x.Group == "Ground attacks" || x.Group == "Aerials") && x.Data.Hits.Count == 0 && x.Data.Projectiles.Count == 0 && x.Note == null))
                e.Note = "The Pikmin do the hitting here, and the app couldn't find their scripts for this move.";
        }

        /// <summary>A held thing's hitboxes on the fighter's bone it hangs off, from when it plays along.</summary>
        static void OnBone(MoveEntry e, Projectile p)
        {
            var md = e.Data;
            foreach (var f in p.Data.Frames)
            {
                var target = md.Frames.FirstOrDefault(x => x.Frame == p.Frame + f.Frame - 1);
                if (target == null) continue;
                foreach (var h in f.Hitboxes)
                {
                    var c = h.Copy(); c.Bone = p.Article.Bone; c.OnArticle = true;
                    target.Hitboxes.Add(c);
                    if (!md.Hits.Any(x => x.Id == c.Id && x.OnArticle && x.Damage == c.Damage)) md.Hits.Add(c);
                }
            }
            FrameData.Windows(md);
        }

        static string Signature(MoveData d) =>
            d.Startup + "|" + d.ActiveText + "|" + d.Faf + "|" + d.IntangibleText + "|" + string.Join(";", d.Hits.Select(h => h.Summary + h.Size.ToString("0.##"))) + "|" + string.Join(";", d.Projectiles.Select(p => p.Frame + p.Data.Hits.Count.ToString() + Path(p.Flight)));
        // where a flight goes (a ground and an air version that fly differently, like Sheik's needles, stay two rows)
        static string Path(Flight f) => f == null || f.Path.Count == 0 ? "" : $"@{f.Life}:{f.Path[f.Path.Count - 1].X:0}:{f.Path[f.Path.Count - 1].Y:0}";

        static void Merge(FighterBook b)
        {
            var result = new List<MoveEntry>();
            foreach (var e in b.Entries)
            {
                var twin = result.FirstOrDefault(r => r.Group == e.Group && Signature(r.Data) == Signature(e.Data) && Same(r.Name, e.Name));
                if (twin == null) { result.Add(e); continue; }
                twin.Name = Combine(twin.Name, e.Name);
                // the row is drawn with the ground version's animation when there is one (Snake's side B shown on the
                // ground, not floating in its air animation)
                bool Air(MoveEntry x) => x.Parts.Any(p => p.IndexOf("Air", StringComparison.OrdinalIgnoreCase) >= 0);
                if (Air(twin) && !Air(e))
                {
                    var (parts, data) = (twin.Parts, twin.Data);
                    twin.Parts = e.Parts; twin.Data = e.Data;
                    twin.Covers.AddRange(parts);
                    continue;
                }
                twin.Covers.AddRange(e.Parts);
            }
            b.Entries = result;
        }

        // "Forward tilt (up)" + "Forward tilt" → same move; "Up B" + "Up B (air)" → same move
        static string Base(string n) => Regex.Replace(n, @"\s*\((up|down|air)\)", "").Trim();
        static bool Same(string a, string b) => Base(a) == Base(b);
        static string Combine(string a, string b)
        {
            var bas = Base(a);
            if (bas.StartsWith("Forward tilt") || bas.StartsWith("Forward smash")) return bas + " (all angles)";
            var rest = bas.Contains(" ") && (bas.StartsWith("Up B") || bas.StartsWith("Down B") || bas.StartsWith("Side B") || bas.StartsWith("Neutral B")) ? bas : bas;
            return rest + " (ground & air)";
        }

        /// <summary>"SpecialAirHiStart" → "Up B (air) start".</summary>
        /// <summary>The name of a standard move one of whose animations this is (null if none).</summary>
        public static string StandardName(string anim)
        {
            foreach (var (g, n, a) in Standard)
                foreach (var part in a.Split('+'))
                    if (string.Equals(part, anim, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        /// <summary>A fighter's subaction (animation) names by number and its aerials' landing lag, from its moveset only
        /// (quick: no animations read).</summary>
        public static MoveTable MoveTable(Fat32Image card, Character c)
        {
            var dir = c.FighterFolderPath + "/";
            var names = card.ListFolder(dir).Select(e => e.Name).ToList();
            var fitName = Find(names, $"^Fit{c.FilePrefix}\\.pac$");
            if (fitName == null) return null;
            var fit = card.ReadFile(dir + fitName);
            lock (BrawlGate.Lock)
                using (var tmp = new TempFile(".pac", fit))
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var src = root.Children[0].WorkingUncompressed;
                    var bytes = new byte[src.Length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                    var m = new Moveset(bytes);
                    var t = new MoveTable { Names = m.Subactions.Select(s => s.Animation).ToList() };
                    for (int i = 0; i < 5; i++) t.LandingLag[i] = (int)Math.Round(m.Attr(0x140 + 4 * i));
                    return t;
                }
        }

        public static string SpecialName(string anim)
        {
            var rest = anim.Substring("Special".Length);
            bool air = rest.Contains("Air"); if (air) rest = rest.Replace("Air", "");
            string which = rest.StartsWith("Hi") ? "Up B" : rest.StartsWith("Lw") ? "Down B" : rest.StartsWith("N") ? "Neutral B" : rest.StartsWith("S") ? "Side B" : "Special";
            rest = rest.StartsWith("Hi") || rest.StartsWith("Lw") ? rest.Substring(2) : rest.Length > 0 && (rest[0] == 'N' || rest[0] == 'S') ? rest.Substring(1) : rest;
            var words = Regex.Replace(rest, "([a-z])([A-Z0-9])", "$1 $2").Trim().ToLowerInvariant();
            return which + (air ? " (air)" : "") + (words.Length > 0 ? " " + words : "");
        }
    }
}
