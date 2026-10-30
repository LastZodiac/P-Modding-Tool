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

        /// <summary>Is this a Wii stream file (BRSTM) the game can play?</summary>
        public static bool IsBrstm(byte[] b) => b != null && b.Length > 64 && b[0] == 'R' && b[1] == 'S' && b[2] == 'T' && b[3] == 'M';

        public static bool IsSongFile(string path) => Path.GetExtension(path ?? "").Equals(".brstm", StringComparison.OrdinalIgnoreCase);
    }
}
