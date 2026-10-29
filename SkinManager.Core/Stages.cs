// P+'s stage files (/Project+/pf/stage/melee/STG*.pac), their friendly names, and a cache of each
// one's gameplay fingerprint (so finding which stages a download can replace is quick).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public sealed class StageFile
    {
        public string CardPath;      // /Project+/pf/stage/melee/STGBATTLEFIELD_L_BF_MELEE.pac
        public string FileName => Path.GetFileName(CardPath);
        public string Base;          // STGBATTLEFIELD
        public string Suffix;        // _L_BF_MELEE
        public string StageName;     // Battlefield
        public string Version;       // "L: BF Melee", or "" for the main one
        public string Label => Version == "" ? StageName : $"{StageName} • {Version}";
        public bool IsMain => Suffix == "";
        public override string ToString() => Label;
    }

    public static class Stages
    {
        // Brawl's own file names for stages P+ renamed
        static readonly Dictionary<string, string> OldNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STGFINAL"] = "STGFINALDESTINATION", ["STGGREENHILL"] = "STGGREENHILLZONE", ["STGSTADIUM"] = "STGPOKEMONSTADIUM",
            ["STGVILLAGE"] = "STGSMASHVILLE", ["STGBATTLE"] = "STGBATTLEFIELD", ["STGDXGREENS"] = "STGFOUNTAINOFDREAMS",
            ["STGDXYORSTER"] = "STGYOSHISSTORY", ["STGPUPUPU"] = "STGDREAMLAND", ["STGDXPSTADIUM"] = "STGPOKEMONSTADIUM",
        };
        // short names people use in download titles
        static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GHZ"] = "STGGREENHILLZONE", ["FD"] = "STGFINALDESTINATION", ["BF"] = "STGBATTLEFIELD", ["SV"] = "STGSMASHVILLE",
            ["PS"] = "STGPOKEMONSTADIUM", ["PS1"] = "STGPOKEMONSTADIUM", ["PS2"] = "STGPOKEMONSTADIUM", ["DL"] = "STGDREAMLAND", ["DL64"] = "STGDREAMLAND",
            ["FOD"] = "STGFOUNTAINOFDREAMS", ["YS"] = "STGYOSHISTORY", ["WW"] = "STGWARIOWARE", ["LC"] = "STGLYLATCRUISE", ["HC"] = "STGHYRULECASTLE",
        };

        /// <summary>The short names people use for a stage ("SV" for STGSMASHVILLE).</summary>
        public static IEnumerable<string> ShortNamesFor(string stageBase) => ShortNames.Where(kv => string.Equals(kv.Value, stageBase, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key);

        /// <summary>The P+ stages (by base name, like STGGREENHILLZONE) a stage file is named for: from its file name
        /// (Brawl's old names too) and from the download's title ("...GHZ redesign", "Green Hill Zone ...").</summary>
        public static List<string> NamedFor(string stageFile, string downloadName, IEnumerable<StageFile> all)
        {
            var bases = new HashSet<string>(all.Select(x => x.Base), StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            void Add(string b) { if (b != null && bases.Contains(b) && !result.Contains(b, StringComparer.OrdinalIgnoreCase)) result.Add(b.ToUpperInvariant()); }
            // the title first: authors often reuse whatever stage file was handy (STGFINAL for anything)
            string title = (downloadName ?? "") + " " + Path.GetFileNameWithoutExtension(stageFile ?? "");
            string plain = new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            foreach (var b in bases.OrderByDescending(b => b.Length))
            {
                string n = Names.TryGetValue(b, out var nm) ? nm : null;
                if (n == null) continue;
                string pn = new string(n.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                if (pn.Length >= 5 && plain.Contains(pn)) Add(b);
            }
            foreach (var w in Regex.Split(title, @"[^A-Za-z0-9]+"))
                if (ShortNames.TryGetValue(w, out var b)) Add(b);
            string fileBase = Describe(stageFile ?? "").Base;
            Add(OldNames.TryGetValue(fileBase, out var renamed) ? renamed : fileBase);
            return result;
        }

        /// <summary>The stage program (module) a stage file was made for: P+'s own setting when the file is named like
        /// one of P+'s stages, else Brawl's program for that stage name (STGFINAL -> st_final.rel), else null.</summary>
        public static string MadeFor(string stageFile, Dictionary<string, (string TrackList, string Module)> info)
        {
            string stem = Path.GetFileNameWithoutExtension(stageFile ?? "").ToUpperInvariant();
            if (info != null && info.TryGetValue(stem, out var i) && i.Module != "") return i.Module;
            var m = System.Text.RegularExpressions.Regex.Match(stem, @"^STG([A-Z0-9]+)");
            if (!m.Success) return null;
            string b = m.Groups[1].Value.ToLowerInvariant();
            if (b == "battlefield") b = "battle";
            if (b.StartsWith("final")) b = "final";
            return "st_" + b + ".rel";
        }

        /// <summary>Can a file made for one stage program go on a P+ stage? Only when that stage runs the same program,
        /// or the plain one (st_final) that just shows a stage's models; another program may look for parts the file
        /// doesn't have and crash the game.</summary>
        public static bool SameProgram(string madeFor, string target, Dictionary<string, (string TrackList, string Module)> info)
        {
            string stem = Path.GetFileNameWithoutExtension(target ?? "").ToUpperInvariant();
            if (info == null || !info.TryGetValue(stem, out var t) || t.Module == "") return true;   // P+ doesn't say: as before
            return t.Module == "st_final.rel" || (madeFor != null && Family(t.Module) == Family(madeFor));
        }

        // "_ht" (hazards switch) and "_noshadowrotate" are the same program with a setting changed; Brawl's st_village is the
        // program P+ calls st_smashville (both are stage program number 70, like the copy Project M shipped as st_kart.rel)
        static string Family(string module)
        {
            string f = module.ToLowerInvariant().Replace(".rel", "").Replace("_noshadowrotate", "").Replace("_ht", "");
            return f == "st_village" ? "st_smashville" : f;
        }

        /// <summary>The program number (REL module ID) of each stage program on the card, by file name.</summary>
        public static Dictionary<string, uint> ModuleIds(Fat32Image card)
        {
            var r = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            const string folder = "/Project+/pf/module";
            if (!card.Exists(folder)) return r;
            foreach (var e in card.ListFolder(folder))
            {
                if (e.IsDirectory || !e.Name.StartsWith("st_", StringComparison.OrdinalIgnoreCase) || !e.Name.EndsWith(".rel", StringComparison.OrdinalIgnoreCase)) continue;
                try { var d = card.ReadFile(folder + "/" + e.Name); if (d.Length >= 4) r[e.Name] = (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]); } catch { }
            }
            return r;
        }

        /// <summary>The program a stage file needs, using the program file its download came with when there is one: authors
        /// often ship a program under another stage's name (Project M's "st_kart.rel" in Mario Circuit Ville is Smashville's).
        /// The program number inside it is matched with P+'s programs; a number P+ doesn't have keeps the file's own name.</summary>
        public static string MadeFor(string stageFile, Dictionary<string, (string TrackList, string Module)> info, Dictionary<string, uint> cardModules)
        {
            string named = MadeFor(stageFile, info);
            if (named == null || cardModules == null) return named;
            try
            {
                string dir = Path.GetDirectoryName(stageFile);
                string rel = Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), named, StringComparison.OrdinalIgnoreCase));
                if (rel == null) return named;
                var b = new byte[4];
                using (var fs = File.OpenRead(rel)) if (fs.Read(b, 0, 4) < 4) return named;
                uint id = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
                var same = cardModules.Where(kv => kv.Value == id).Select(kv => kv.Key).OrderBy(k => k.Length).FirstOrDefault();
                return same ?? named;
            }
            catch { return named; }
        }

        /// <summary>A numbered stage file (Project M's STGVILLAGE_02) goes on the P+ version with the same number (_02_DUSK).</summary>
        public static bool SameNumber(string stageFile, StageFile target)
        {
            var m = Regex.Match(Path.GetFileNameWithoutExtension(stageFile ?? ""), @"^STG[A-Za-z]+_(\d\d)$", RegexOptions.IgnoreCase);
            return m.Success && target.Suffix.StartsWith("_" + m.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>P+ stage versions whose own name (FOD_SAURIA) shows up in a download's title ("Fountain of Sauria"):
        /// the same stage, already in P+.</summary>
        public static List<StageFile> AlreadyInPPlus(string downloadName, IEnumerable<StageFile> all)
        {
            string plain = new string((downloadName ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return all.Where(s => !s.IsMain && s.Suffix.Split('_').Any(w => w.Length >= 5 && !Regex.IsMatch(w, @"^\d+$") && plain.Contains(w.ToLowerInvariant()))).ToList();
        }

        public const string Folder = "/Project+/pf/stage/melee";

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STGBATTLEFIELD"] = "Battlefield", ["STGBELLTOWER"] = "Bell Tower", ["STGBIGBLUE"] = "Big Blue", ["STGBOWSERCASTLE"] = "Bowser's Castle",
            ["STGCASTLESIEGE"] = "Castle Siege", ["STGCERESSPACECOLONY"] = "Ceres Space Colony", ["STGCHARAROLL"] = "Chararoll", ["STGCLOCKTOWN"] = "Clock Town",
            ["STGCONFIGTEST"] = "Config Test", ["STGCOOKIECOUNTRY"] = "Cookie Country", ["STGCORNERIA"] = "Corneria", ["STGDEADLINE"] = "Deadline",
            ["STGDELFINOSECRET"] = "Delfino's Secret", ["STGDINOSAURLAND"] = "Dinosaur Land", ["STGDISTANTPLANET"] = "Distant Planet", ["STGDRACULASCASTLE"] = "Dracula's Castle",
            ["STGDREAMLAND"] = "Dream Land", ["STGDUALDUMMY"] = "Dual Dummy", ["STGFINALDESTINATION"] = "Final Destination", ["STGFLATZONE"] = "Flat Zone",
            ["STGFOUNTAINOFDREAMS"] = "Fountain of Dreams", ["STGFOURSIDE"] = "Fourside", ["STGFRIGATEHUSK"] = "Frigate Husk", ["STGGOLDENTEMPLE"] = "Golden Temple",
            ["STGGREENHILLZONE"] = "Green Hill Zone", ["STGHALBERD"] = "Halberd", ["STGHANENBOW"] = "Hanenbow", ["STGHYRULECASTLE"] = "Hyrule Castle",
            ["STGHYRULETEMPLE"] = "Hyrule Temple", ["STGINFINITEGLACIER"] = "Infinite Glacier", ["STGJUNGLEJAPES"] = "Jungle Japes", ["STGKONGOJUNGLE"] = "Kongo Jungle",
            ["STGLUIGISMANSION"] = "Luigi's Mansion", ["STGLYLATCRUISE"] = "Lylat Cruise", ["STGMARIOCIRCUIT"] = "Mario Circuit", ["STGMETALCAVERN"] = "Metal Cavern",
            ["STGMINECARTMADNESS"] = "Minecart Madness", ["STGMUSHROOMKINGDOM"] = "Mushroom Kingdom", ["STGNORFAIR"] = "Norfair", ["STGONETT"] = "Onett",
            ["STGPEACHCASTLE"] = "Peach's Castle", ["STGPICTOCHAT"] = "PictoChat", ["STGPIRATESHIP"] = "Pirate Ship", ["STGPLANETZEBES"] = "Planet Zebes",
            ["STGPOKEFLOATS"] = "Poké Floats", ["STGPOKEMONSTADIUM"] = "Pokémon Stadium", ["STGPORTTOWN"] = "Port Town Aero Dive", ["STGRAINBOWCRUISE"] = "Rainbow Cruise",
            ["STGRESTAREA"] = "Rest Area", ["STGRESULT"] = "Results screen", ["STGRUMBLEFALLS"] = "Rumble Falls", ["STGSAFFRONCITY"] = "Saffron City",
            ["STGSHADOWMOSES"] = "Shadow Moses Island", ["STGSKYSANCTUARY"] = "Sky Sanctuary", ["STGSKYWORLD"] = "Skyworld", ["STGSMASHVILLE"] = "Smashville",
            ["STGSPEARPILLAR"] = "Spear Pillar", ["STGSUBSPACE"] = "Subspace", ["STGTEMPLEOFTIME"] = "Temple of Time", ["STGTRAININGROOM"] = "Training Room",
            ["STGVENOM"] = "Venom", ["STGVENUSLIGHTHOUSE"] = "Venus Lighthouse", ["STGWARIOLAND"] = "Wario Land", ["STGWARIOWARE"] = "WarioWare",
            ["STGYOSHISISLAND"] = "Yoshi's Island", ["STGYOSHISTORY"] = "Yoshi's Story",
        };

        static readonly HashSet<string> Short = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "BF", "FD", "SV", "PS1", "PS2", "BC", "CS", "DL", "DS", "DP", "FH", "FOD", "GHZ", "KJ", "LM", "MC", "MK3", "NF", "SBG", "SSZ", "TOT", "WL", "YI", "CSC", "DED", "N64", "GBA", "P+", "X", "64", "OOT", "SSE" };

        public static StageFile Describe(string cardPath)
        {
            string name = Path.GetFileNameWithoutExtension(cardPath);
            var m = Regex.Match(name, @"^(STG[A-Za-z]+)(.*)$", RegexOptions.IgnoreCase);
            string bas = m.Success ? m.Groups[1].Value.ToUpperInvariant() : name.ToUpperInvariant();
            string suffix = m.Success ? m.Groups[2].Value : "";
            string stage = Names.TryGetValue(bas, out var n) ? n : Pretty(bas.Length > 3 ? bas.Substring(3) : bas);
            string version = "";
            var parts = suffix.Trim('_').Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0)
            {
                string button = null;
                if (Regex.IsMatch(parts[0], @"^(L|R|Z|ML|MR)$", RegexOptions.IgnoreCase)) { button = parts[0].ToUpperInvariant(); parts.RemoveAt(0); }
                if (parts.Count > 0 && Regex.IsMatch(parts[0], @"^\d\d$") && parts.Count > 1) parts.RemoveAt(0);   // _00_MORNING -> Morning
                string rest = string.Join(" ", parts.Select(Pretty));
                version = button != null ? (rest == "" ? $"{button} version" : $"{button}: {rest}") : rest;
            }
            return new StageFile { CardPath = cardPath, Base = bas, Suffix = suffix, StageName = stage, Version = version };
        }

        static string Pretty(string word)
        {
            if (Short.Contains(word) || Regex.IsMatch(word, @"\d")) return word.ToUpperInvariant();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant());
        }

        /// <summary>Every stage file P+ has on the card.</summary>
        public static List<StageFile> All(Fat32Image card) =>
            card.ListFolder(Folder).Where(e => !e.IsDirectory && e.Name.StartsWith("STG", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".pac", StringComparison.OrdinalIgnoreCase))
                .Select(e => Describe(Folder + "/" + e.Name))
                .Where(s => s.Base != "STGRESULT" && s.Base != "STGCONFIGTEST" && !s.Suffix.Contains("ABBREVIATIONS"))
                .OrderBy(s => s.StageName).ThenBy(s => s.IsMain ? 0 : 1).ThenBy(s => s.Suffix)
                .ToList();

        /// <summary>Is this file name a stage file (STGxxx.pac)?</summary>
        public static bool IsStageFile(string fileName) =>
            Regex.IsMatch(Path.GetFileName(fileName ?? ""), @"^STG[A-Za-z0-9][^\\/]*\.pac$", RegexOptions.IgnoreCase);
    }

    /// <summary>Gameplay fingerprints of P+'s own stage files, kept in a file so they're worked out once.</summary>
    public sealed class StageIndex
    {
        [DataContract]
        sealed class Entry
        {
            [DataMember] public string CardPath;
            [DataMember] public string Sha;      // of the file the fingerprint was made from
            [DataMember(EmitDefaultValue = false)] public int Version;   // StageCheck.FingerprintVersion it was made with
            [DataMember] public List<string> Keys = new List<string>();
            [DataMember] public List<string> Values = new List<string>();
        }

        readonly string _file;
        readonly Dictionary<string, Entry> _byPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public StageIndex(string dataFolder)
        {
            _file = Path.Combine(dataFolder, "stage-index-4.json");   // -2: fingerprints compare content, not bytes
            try
            {
                if (File.Exists(_file))
                    using (var s = File.OpenRead(_file))
                        foreach (var e in (List<Entry>)new DataContractJsonSerializer(typeof(List<Entry>)).ReadObject(s)) _byPath[e.CardPath] = e;
            }
            catch { _byPath.Clear(); }
        }

        void Save()
        {
            try
            {
                string tmp = _file + ".tmp";
                using (var s = File.Create(tmp)) new DataContractJsonSerializer(typeof(List<Entry>)).WriteObject(s, _byPath.Values.ToList());
                if (File.Exists(_file)) File.Delete(_file);
                File.Move(tmp, _file);
            }
            catch { }
        }

        /// <summary>The gameplay fingerprint of P+'s stage at this path (P+'s own file even when a stage of ours is on the card).</summary>
        public Dictionary<string, string> Of(Fat32Image card, Library lib, string cardPath)
        {
            byte[] bytes = lib.HasOriginalFile(cardPath) ? lib.OriginalFile(cardPath) : card.ReadFile(cardPath);
            if (bytes == null) return null;
            string sha = Library.Sha256(bytes);
            // only a fingerprint made the way the app makes them today counts (an older one may describe the same file differently)
            if (_byPath.TryGetValue(cardPath, out var e) && e.Sha == sha && e.Version == StageCheck.FingerprintVersion)
                return e.Keys.Zip(e.Values, (k, v) => new KeyValuePair<string, string>(k, v)).ToDictionary(kv => kv.Key, kv => kv.Value);
            Dictionary<string, string> g;
            try { g = StageCheck.Gameplay(bytes); }
            catch { g = new Dictionary<string, string> { ["unreadable"] = sha }; }   // remembered, so it isn't tried again
            _byPath[cardPath] = new Entry { CardPath = cardPath, Sha = sha, Version = StageCheck.FingerprintVersion, Keys = g.Keys.ToList(), Values = g.Values.ToList() };
            Save();
            return g;
        }

        public sealed class Near
        {
            public StageFile Stage;
            public List<string> Differences;
            public bool FloorsDiffer;
        }

        /// <summary>P+'s stages that come closest to a stage file that doesn't match any exactly:
        /// the ones with the same name first, then the fewest differences.</summary>
        public List<Near> Closest(Fat32Image card, Library lib, Dictionary<string, string> mine, string baseName, int count = 4, Func<StageFile, bool> allowed = null)
        {
            var result = new List<Near>();
            foreach (var s in Stages.All(card))
            {
                if (allowed != null && !allowed(s)) continue;
                Dictionary<string, string> theirs;
                try { theirs = Of(card, lib, s.CardPath); } catch { continue; }
                if (theirs == null || theirs.ContainsKey("unreadable")) continue;
                result.Add(new Near { Stage = s, Differences = StageCheck.Differences(mine, theirs), FloorsDiffer = StageCheck.FloorsDiffer(mine, theirs) });
            }
            return result.Where(n => n.Differences.Count > 0)
                .OrderBy(n => string.Equals(n.Stage.Base, baseName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n.FloorsDiffer ? 1 : 0)
                .ThenBy(n => n.Differences.Count)
                .ThenBy(n => n.Stage.IsMain ? 0 : 1)
                .Take(count).ToList();
        }

        // ----- the stage programs -----

        Dictionary<string, (string TrackList, string Module)> _info;
        readonly Dictionary<string, HashSet<string>> _programNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The names a P+ stage's program looks up in its stage file (every text in the program file, like
        /// "StgGreenhillMain"). Models with these names must be exactly P+'s for the stage to play the same.</summary>
        public HashSet<string> ProgramNames(Fat32Image card, string cardPath)
        {
            if (_info == null) { try { _info = Songs.StageInfo(card); } catch { _info = new Dictionary<string, (string, string)>(); } }
            string stem = Path.GetFileNameWithoutExtension(cardPath ?? "").ToUpperInvariant();
            string module = _info.TryGetValue(stem, out var i) && !string.IsNullOrEmpty(i.Module) ? i.Module : Stages.MadeFor(cardPath, _info);
            if (module == null) return new HashSet<string>(StringComparer.Ordinal);
            if (_programNames.TryGetValue(module, out var have)) return have;
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string path = "/Project+/pf/module/" + module;
                if (card.Exists(path))
                {
                    var d = card.ReadFile(path);
                    int start = -1;
                    for (int k = 0; k <= d.Length; k++)
                    {
                        bool text = k < d.Length && d[k] >= 0x20 && d[k] < 0x7F;
                        if (text && start < 0) start = k;
                        if (!text && start >= 0) { if (k - start >= 3) names.Add(System.Text.Encoding.ASCII.GetString(d, start, k - start)); start = -1; }
                    }
                }
            }
            catch { }
            return _programNames[module] = names;
        }

        /// <summary>P+'s stages shaped most like a map (where you stand lines up best), any stage program.</summary>
        public List<(StageFile Stage, StageCheck.Fit Fit)> ByShape(Fat32Image card, Library lib, Dictionary<string, string> mine, int count = 3, float within = 10f)
        {
            var result = new List<(StageFile, StageCheck.Fit)>();
            foreach (var s in Stages.All(card))
            {
                Dictionary<string, string> theirs;
                try { theirs = Of(card, lib, s.CardPath); } catch { continue; }
                var fit = StageCheck.ShapeFit(mine, theirs);
                if (fit != null && fit.Floors <= within) result.Add((s, fit));
            }
            return result.OrderBy(x => x.Item2.Floors).ThenBy(x => x.Item2.Rest).ThenBy(x => x.Item1.IsMain ? 0 : 1).Take(count).ToList();
        }

        /// <summary>P+'s stages a stage file plays exactly like (the ones it can safely replace).</summary>
        public List<StageFile> Matches(Fat32Image card, Library lib, Dictionary<string, string> mine, Action<int, int> progress = null, Func<StageFile, bool> allowed = null)
        {
            var all = Stages.All(card);
            var result = new List<StageFile>();
            for (int i = 0; i < all.Count; i++)
            {
                progress?.Invoke(i, all.Count);
                Dictionary<string, string> theirs;
                try { theirs = Of(card, lib, all[i].CardPath); } catch { continue; }
                if (theirs != null && StageCheck.Compare(mine, theirs, "", ProgramNames(card, all[i].CardPath)).Passed && (allowed == null || allowed(all[i]))) result.Add(all[i]);
            }
            return result;
        }
    }
}
