// P+'s music: the songs its track lists (pf/sound/tracklist/*.tlst) play, and where each song's file
// is on the card. A song is replaced by writing a new .brstm over its file; the track lists themselves
// are never changed, so song choice stays the same for both players online.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.SSBB.ResourceNodes.ProjectPlus;

namespace SkinManager.Core
{
    /// <summary>One song P+ plays: its file on the card, its title, and the track lists (stages, menus) that play it.</summary>
    public sealed class SongInfo
    {
        public string CardPath;        // /Project+/pf/sound/strm/Melee/Multi-Man Melee 2.brstm
        public string FileName;        // "Melee/Multi-Man Melee 2" or "X02"
        public string Title;
        public bool FromBrawl;         // one of Brawl's own songs (played from the disc unless replaced)
        public long Size;              // size of the file on the card now (0: none, played from the disc)
        public List<string> Lists = new List<string>();   // track lists (stages, menus) that play it
        public string Label => Title + "  —  " + string.Join(", ", Lists.Take(3)) + (Lists.Count > 3 ? $" +{Lists.Count - 3}" : "");
        public override string ToString() => Label;
    }

    /// <summary>A place music plays: a stage (by its stage select icon) or a menu, and its songs (one track list).</summary>
    public sealed class SongPlace
    {
        public const int Stage = 0, Menu = 1, Other = 2;
        public string List;            // the track list ("Battlefield Melee": pf/sound/tracklist/Battlefield_Melee.tlst)
        public string Title;           // what it's called here
        public string About;           // a line about it (menus)
        public int Kind = Stage;
        public int Slot = -1;          // the stage select icon it's on (-1: none)
        public string SlotName, Region, HowToPick;   // the icon's stage; its part of the preview ("main", "L"...); how to get it (null: just pick it)
        public string File;            // a stage file that plays it (for drawing it when the stage select has no picture)
        public int Picture = -1, Page, Order;
        public bool OnePerFighter;     // victory themes, credits: each song belongs to a fighter, so songs can only be replaced
        public List<SongInfo> Songs = new List<SongInfo>();
        public string TrackListFile => List.Replace(' ', '_') + ".tlst";
        public override string ToString() => Title;
    }

    /// <summary>Reads P+'s track lists and stage settings off the card to find its songs and where each one plays.</summary>
    public static class Songs
    {
        public static string StrmFolder => Build.Pf + "/sound/strm";
        public static string TracklistFolder => Build.Pf + "/sound/tracklist";
        public const long MaxSongBytes = 40L * 1024 * 1024;   // bigger than any Brawl song; a sanity limit

        /// <summary>Every song P+'s track lists play.</summary>
        public static List<SongInfo> All(Fat32Image card)
        {
            var songs = new Dictionary<string, SongInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in card.ListFolder(TracklistFolder).Where(x => !x.IsDirectory && x.Name.EndsWith(".tlst", StringComparison.OrdinalIgnoreCase)))
            {
                string list = Path.GetFileNameWithoutExtension(e.Name).Replace('_', ' ');
                byte[] bytes;
                try { bytes = card.ReadFile(TracklistFolder + "/" + e.Name); } catch { continue; }
                using (var tmp = new TempFile(".tlst", bytes))
                lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    if (root == null) continue;
                    foreach (var entry in root.Children.OfType<TLSTEntryNode>())
                    {
                        string file = entry.SongFileName;
                        bool brawl = string.IsNullOrEmpty(file);
                        if (brawl && !TLSTEntryNode.BrawlBRSTMs.TryGetValue(entry.SongID, out file)) continue;
                        file = file.Replace('\\', '/').Trim('/');
                        if (!songs.TryGetValue(file, out var s))
                        {
                            string path = StrmFolder + "/" + file + ".brstm";
                            string title = (entry.Name ?? "").Trim();
                            if (title == "" || title == "<null>") title = Path.GetFileName(file);
                            // a song whose file isn't on the card is played from the Brawl disc
                            bool there = card.Exists(path);
                            songs[file] = s = new SongInfo { FileName = file, CardPath = path, FromBrawl = brawl || !there, Title = title, Size = there ? card.FileSize(path) : 0 };
                        }
                        if (!s.Lists.Contains(list)) s.Lists.Add(list);
                    }
                }
            }
            return songs.Values.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string StageInfoFolder => Build.Pf + "/stage/stageinfo";

        /// <summary>Which track list each stage file plays, from P+'s stage settings (pf/stage/stageinfo/*.param):
        /// "STGBATTLEFIELD_L_BF_MELEE" -> "Battlefield Melee". Keys are stage file names without .pac, upper case.</summary>
        public static Dictionary<string, string> StageTrackLists(Fat32Image card) =>
            StageInfo(card).ToDictionary(kv => kv.Key, kv => kv.Value.TrackList, StringComparer.OrdinalIgnoreCase);

        /// <summary>P+'s stage settings per stage file (name without .pac, upper case): the track list it plays and
        /// the stage program (module, a .rel) that runs it.</summary>
        public static Dictionary<string, (string TrackList, string Module)> StageInfo(Fat32Image card)
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in card.ListFolder(StageInfoFolder).Where(x => !x.IsDirectory && x.Name.EndsWith(".param", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] bytes;
                try { bytes = card.ReadFile(StageInfoFolder + "/" + e.Name); } catch { continue; }
                try
                {
                    using (var tmp = new TempFile(".param", bytes))
                    lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                    {
                        if (!(root is STEXNode stex) || string.IsNullOrWhiteSpace(stex.StageName)) continue;
                        var info = ((stex.TrackList ?? "").Trim().Replace('_', ' '), (stex.Module ?? "").Trim().ToLowerInvariant());
                        string stage = "STG" + stex.StageName.Trim().ToUpperInvariant();
                        if (!result.ContainsKey(stage)) result[stage] = info;
                        foreach (var sub in stex.Children)
                        {
                            string n = (sub.Name ?? "").Trim();
                            if (n == "" || n.StartsWith("<")) continue;
                            string key = stage + (n.StartsWith("_") ? "" : "_") + n.ToUpperInvariant();
                            if (!result.ContainsKey(key)) result[key] = info;
                            string whole = "STG" + n.TrimStart('_').ToUpperInvariant();   // some name the whole file (CastleSiege_SBG)
                            if (!result.ContainsKey(whole)) result[whole] = info;
                        }
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>The track list a stage file plays (by its file name), or null.</summary>
        public static string TrackListOf(Dictionary<string, string> map, string stageFile)
        {
            string stem = Path.GetFileNameWithoutExtension(stageFile ?? "").ToUpperInvariant();
            return map.TryGetValue(stem, out var l) && l != "" ? l : null;
        }

        static string Plain(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>Guess the stage a download's music is for, from its name: the longest track-list name it contains.</summary>
        public static string GuessTrackList(IEnumerable<string> lists, params string[] names)
        {
            string best = null;
            foreach (var l in lists)
            {
                string p = Plain(l);
                if (p.Length < 4) continue;
                if (names.Any(n => Plain(n).Contains(p)) && (best == null || p.Length > Plain(best).Length)) best = l;
            }
            return best;
        }

        /// <summary>A song list (TLST) with songs added after P+'s: each gets the next free custom song ID (F000 and up,
        /// below FE00 so it can be picked) and the usual frequency and volume.</summary>
        public static byte[] AddToTrackList(byte[] tlst, IEnumerable<(string file, string title)> songs)
        {
            using (var tmp = new TempFile(".tlst", tlst))
            using (var outFile = new TempFile(".tlst"))
            {
                lock (BrawlGate.Lock)
                {
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                    {
                        if (!(root is TLSTNode list)) throw new InvalidDataException("That isn't a song list.");
                        var used = new HashSet<uint>(list.Children.OfType<TLSTEntryNode>().Select(e => e.SongID));
                        uint next = 0xF000;
                        foreach (var (file, title) in songs)
                        {
                            while (used.Contains(next)) next++;
                            if (next >= 0xFE00) throw new InvalidOperationException("This stage's song list is full.");
                            var e = new TLSTEntryNode();
                            list.AddChild(e);
                            e.SongID = next; used.Add(next);
                            e.Volume = 80; e.Frequency = 40; e.SongDelay = 0;
                            e.SongFileName = file;
                            e.Name = title;
                        }
                        list.Export(outFile.Path);
                    }
                }
                return File.ReadAllBytes(outFile.Path);
            }
        }

        /// <summary>The places music plays: each track list, with the stage select icon it's for (or a menu).</summary>
        public static List<SongPlace> Places(Fat32Image card, List<SongInfo> songs)
        {
            var lists = songs.SelectMany(x => x.Lists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var places = lists.ToDictionary(l => l, l => new SongPlace { List = l, Title = l, Songs = songs.Where(x => x.Lists.Any(y => string.Equals(y, l, StringComparison.OrdinalIgnoreCase))).ToList() }, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, (string TrackList, string Module)> info;
            StageSelect select;
            try { info = StageInfo(card); select = StageSelect.Read(card); } catch { info = null; select = null; }
            if (info != null && select != null)
            {
                // the stage select's versions in order (page, row, icon; then the normal one, L, R, Z, the lists)
                foreach (var spot in select.All.Where(x => x.Page > 0).OrderBy(x => x.Page).ThenBy(x => x.Row).ThenBy(x => x.Column).ThenBy(x => x.Button == 0 ? 0 : x.Region != null ? 1 : 2).ThenBy(x => x.Entry))
                {
                    if (!info.TryGetValue(Path.GetFileNameWithoutExtension(spot.File ?? "").ToUpperInvariant(), out var i) || string.IsNullOrEmpty(i.TrackList)) continue;
                    if (!places.TryGetValue(i.TrackList, out var p) || p.Slot >= 0) continue;
                    p.Slot = spot.Slot; p.SlotName = spot.SlotName; p.Picture = spot.Picture; p.Region = spot.Region ?? "main";
                    p.Page = spot.Page; p.Order = (spot.Page * 100 + (spot.Row - 1) * 7 + spot.Column) * 100 + (spot.Button == 0 ? 0 : spot.Region != null ? 1 : 2);
                    p.HowToPick = spot.Button == 0 ? null : spot.HowToPick; p.File = spot.File;
                }
            }
            foreach (var p in places.Values.Where(x => x.Slot < 0))
            {
                string key = p.List.ToLowerInvariant();
                if (key == "menu") { p.Kind = SongPlace.Menu; p.Title = "Menus"; p.About = "The main menu, character select and stage select"; p.Order = 0; }
                else if (key == "results") { p.Kind = SongPlace.Menu; p.Title = "Victory themes"; p.About = "The results screen: one song per fighter"; p.OnePerFighter = true; p.Order = 1; }
                else if (key == "credits") { p.Kind = SongPlace.Menu; p.Title = "Credits"; p.About = "The credits after Classic mode: one song per fighter"; p.OnePerFighter = true; p.Order = 2; }
                else { p.Kind = SongPlace.Other; p.About = "Not an icon on the stage select"; p.Order = 10; }
            }
            return places.Values.OrderBy(p => p.Kind).ThenBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Is this a Wii stream file (BRSTM) the game can play?</summary>
        public static bool IsBrstm(byte[] b) => b != null && b.Length > 64 && b[0] == 'R' && b[1] == 'S' && b[2] == 'T' && b[3] == 'M';

        public static bool IsSongFile(string path) => Path.GetExtension(path ?? "").Equals(".brstm", StringComparison.OrdinalIgnoreCase);
    }
}
