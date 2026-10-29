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
        public static readonly string[] Groups = { "Ground attacks", "Aerials", "Neutral B", "Side B", "Up B", "Down B", "Grabs & throws", "Dodges & getups" };
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
            }
            Build(book);
            return book;
        }

        static void Build(FighterBook b)
        {
            var m = b.Moves;
            int Sub(string anim)
            {
                int k = m.Subactions.FindIndex(s => string.Equals(s.Animation, anim, StringComparison.OrdinalIgnoreCase) && s.Main.Count > 0);
                return k >= 0 ? k : m.Subactions.FindIndex(s => string.Equals(s.Animation, anim, StringComparison.OrdinalIgnoreCase));
            }
            int Len(string a) => b.Lengths.TryGetValue(a, out var l) ? l : 0;
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
                var d = FrameData.Run(m, n, subs, Len);
                if (d.Frames.Count == 0) continue;
                if (g == "Aerials") d.LandingLag = (int)Math.Round(m.Attr(a == "AttackAirN" ? 0x140 : a == "AttackAirF" ? 0x144 : a == "AttackAirB" ? 0x148 : a == "AttackAirHi" ? 0x14C : 0x150));
                used.Add(parts.Last());
                b.Entries.Add(new MoveEntry { Group = g, Name = n, Parts = keep, Data = d });
            }
            // specials: every special subaction that hits, grabs or makes the fighter intangible
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in m.Subactions.Where(s => s.Animation != null && s.Animation.StartsWith("Special", StringComparison.OrdinalIgnoreCase) && !s.Animation.StartsWith("SpecialAir", StringComparison.OrdinalIgnoreCase) || (s.Animation?.StartsWith("SpecialAir", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                if (!seen.Add(s.Animation) || s.Main.Count == 0) continue;
                var d = FrameData.Run(m, s.Animation, new[] { s.Index }, Len);
                if (d.Frames.Count == 0 || d.Hits.Count == 0 && d.Intangible.Count == 0 && d.Spawns.Count == 0) continue;
                var nm = SpecialName(s.Animation);
                string grp = nm.StartsWith("Neutral B") ? "Neutral B" : nm.StartsWith("Side B") ? "Side B" : nm.StartsWith("Up B") ? "Up B" : nm.StartsWith("Down B") ? "Down B" : "Neutral B";
                b.Entries.Add(new MoveEntry { Group = grp, Name = nm, Parts = new[] { s.Animation }, Data = d });
            }
            // projectiles and other things a move makes (Fox's laser, his side B's image...)
            foreach (var e in b.Entries) AddProjectiles(b, e);
            b.Entries.RemoveAll(e => e.Group.EndsWith(" B") && e.Data.Hits.Count == 0 && e.Data.Intangible.Count == 0 && e.Data.Projectiles.Count == 0);
            // a ground and an air version (or several angles) that are exactly the same: one row
            Merge(b);
            // order specials: neutral, side, up, down; ground before air
            var specials = b.Entries.Where(e => e.Group.EndsWith(" B")).ToList();
            b.Entries.RemoveAll(e => e.Group.EndsWith(" B"));
            int Rank(MoveEntry e) { var a = e.Parts[0]; var k = a.Replace("SpecialAir", "Special"); int r = k.StartsWith("SpecialN") ? 0 : k.StartsWith("SpecialS") ? 1 : k.StartsWith("SpecialHi") ? 2 : k.StartsWith("SpecialLw") ? 3 : 4; return r * 2 + (a.StartsWith("SpecialAir") ? 1 : 0); }
            int at = b.Entries.FindIndex(e => e.Group == "Grabs & throws"); if (at < 0) at = b.Entries.Count;
            b.Entries.InsertRange(at, specials.OrderBy(Rank).ThenBy(e => m.Subactions.FindIndex(s => s.Animation == e.Parts[0])));
        }

        static void AddProjectiles(FighterBook b, MoveEntry e)
        {
            var arts = b.Moves.Articles.OrderBy(a => a.Slot).ToList();
            bool air = e.Group == "Aerials" || e.Parts.Any(p => p.Contains("Air"));
            foreach (var (frame, index) in e.Data.Spawns)
            {
                if (index < 0 || index >= arts.Count) continue;
                var art = arts[index];
                var withHits = art.Subactions.Where(s => s.Main.Any(x => x.Id == 0x06000D00 || x.Id == 0x06150F00)).ToList();
                if (withHits.Count == 0) continue;
                var pick = withHits.FirstOrDefault(s => (s.Animation ?? "").Contains("Air") == air) ?? withHits[0];
                var d = FrameData.Run(b.Moves, pick.Animation, new[] { pick }, _ => 0);
                if (d.Hits.Count == 0) continue;
                string kind = art.Subactions.Any(s => Regex.IsMatch(s.Animation ?? "", "Fly|Shoot|Bullet|Arrow|Throw|Move|Roll|Fall", RegexOptions.IgnoreCase)) ? "Projectile" : "Spawned hitbox";
                e.Data.Projectiles.Add(new Projectile { Name = $"{kind} ({Regex.Replace(pick.Animation ?? "?", "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()})", Frame = frame, Article = art, Data = d });
            }
        }

        static string Signature(MoveData d) =>
            d.Startup + "|" + d.ActiveText + "|" + d.Faf + "|" + d.IntangibleText + "|" + string.Join(";", d.Hits.Select(h => h.Summary + h.Size.ToString("0.##"))) + "|" + string.Join(";", d.Projectiles.Select(p => p.Frame + p.Data.Hits.Count.ToString()));

        static void Merge(FighterBook b)
        {
            var result = new List<MoveEntry>();
            foreach (var e in b.Entries)
            {
                var twin = result.FirstOrDefault(r => r.Group == e.Group && Signature(r.Data) == Signature(e.Data) && Same(r.Name, e.Name));
                if (twin == null) { result.Add(e); continue; }
                twin.Name = Combine(twin.Name, e.Name);
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
