// Finds the costume files and pictures inside a downloaded skin (already extracted to a folder).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public sealed class CostumeFile
    {
        public string Path;
        public string Kind;      // "" main costume, "Etc", "Final", "Entry", "Result", a Kirby hat (e.g. "Donkey"), or "Nana"
        public int Number;       // NN in the file name (-1 for XX)
        public string TargetName(Character c, int fileNumber)
        {
            if (Kind == "Nana") return $"Fit{c.PartnerPrefixes[0]}{fileNumber:00}.pac";
            return $"Fit{c.FilePrefix}{Kind}{fileNumber:00}.pac";
        }
        public string TargetFolder(Character c) =>
            Kind == "Nana" ? "/Project+/pf/fighter/" + c.PartnerPrefixes[0] : c.FighterFolderPath;
    }

    public sealed class PackageCostume
    {
        public Character Character;
        public int Number;            // -1 when the file says XX
        public string Variant = "";   // e.g. "PM-P+", "vBrawl", "0" from FitMario00 [vBrawl].pac or FitMario00_0.pac
        public string Folder = "";    // folder inside the download
        public List<CostumeFile> Files = new List<CostumeFile>();
        public CostumeFile Main => Files.FirstOrDefault(f => f.Kind == "");
        public string NumberText => Number < 0 ? "XX" : Number.ToString("00");
        public string Label => $"{Character.Name} costume {NumberText}" + (Variant != "" ? $" ({Variant})" : "");
        /// <summary>Which costume of its download this is (to know later which ones were already added).</summary>
        public string Key => $"{Character.SlotListId}|{Number}|{Variant}|{Folder}";
        public bool MadeForPPlus => CostumeName.GameScore(Variant + " " + Folder) == 0;
        public bool MadeForBrawl => CostumeName.GameScore(Variant + " " + Folder) == 2;
        public override string ToString() => Label;
    }

    /// <summary>What a costume file name says: whose costume, which part, which number, which version.</summary>
    public sealed class CostumeName
    {
        public Character Character;
        public string Kind;
        public int Number;
        public string Variant;

        static readonly string[] Extras = { "Etc", "Final", "Entry", "Result" };

        public static CostumeName Parse(string file)
        {
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".pac" && ext != ".pcs") return null;
            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            var m = Regex.Match(stem, @"^Fit([A-Za-z]+?)(\d{2}|XX)(?![0-9])(.*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string body = m.Groups[1].Value;
            int number = m.Groups[2].Value.Equals("XX", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(m.Groups[2].Value);
            string variant = Regex.Replace(m.Groups[3].Value, @"^[\s_\-\.\[\(]+|[\s_\-\.\]\)]+$", "");
            foreach (var c in Characters.All.OrderByDescending(x => x.FilePrefix.Length))
            {
                string kind = null;
                if (body.Equals(c.FilePrefix, StringComparison.OrdinalIgnoreCase)) kind = "";
                else if (body.StartsWith(c.FilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = body.Substring(c.FilePrefix.Length);
                    kind = Extras.FirstOrDefault(e => e.Equals(rest, StringComparison.OrdinalIgnoreCase));
                    if (kind == null && c.FilePrefix == "Kirby")
                    {
                        var hat = Characters.All.FirstOrDefault(h => h.FilePrefix.Equals(rest, StringComparison.OrdinalIgnoreCase));
                        if (hat != null) kind = hat.FilePrefix;
                    }
                }
                if (kind == null && c.PartnerPrefixes.Any(p => body.Equals(p, StringComparison.OrdinalIgnoreCase))) kind = "Nana";
                if (kind == null) continue;
                return new CostumeName { Character = c, Kind = kind, Number = number, Variant = variant };
            }
            return null;
        }

        /// <summary>0 = says it is for P+ / PM, 1 = doesn't say, 2 = says it is for plain Brawl.</summary>
        public static int GameScore(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (Regex.IsMatch(t, @"p\+|pplus|project|(^|[^a-z])pm([^a-z]|$)")) return 0;
            if (Regex.IsMatch(t, @"vbrawl|vanilla|ssbb|(^|[^a-z])brawl([^a-z]|$)")) return 2;
            return 1;
        }
    }

    public sealed class PackagePicture
    {
        public string Path;
        public PictureKind Kind;
        public int? Number;      // a number in the file name, if any (helps match packs of several colors)
    }

    /// <summary>Where a skin came from, when it was downloaded in the app.</summary>
    public sealed class SkinSource
    {
        public long ModId;
        public long FileId;
        public string ModName;
        public string Author;
        public string PageUrl;
        public string ThumbUrl;
    }

    public sealed class SkinPackage
    {
        public string Folder;
        public string Name;
        public SkinSource Source;
        public string SourcePath;   // the file or folder it was opened from
        public List<PackageCostume> Costumes = new List<PackageCostume>();
        public List<PackagePicture> Pictures = new List<PackagePicture>();
        public List<string> Ignored = new List<string>();
        public List<string> StageFiles = new List<string>();   // STGxxx.pac files in the download
        public List<string> SongFiles = new List<string>();    // .brstm music files in the download

        public static SkinPackage Scan(string folder, string name)
        {
            var pkg = new SkinPackage { Folder = folder, Name = name };
            var costumeFiles = new List<string>();
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string file = System.IO.Path.GetFileName(path);
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".brstm") pkg.SongFiles.Add(path);
                else if (ext == ".pac" && Stages.IsStageFile(file)) pkg.StageFiles.Add(path);
                else if (ext == ".pac" || ext == ".pcs") costumeFiles.Add(path);
                else if (ext == ".png" || ext == ".bmp" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif")
                {
                    var pic = TryPicture(path, file);
                    if (pic != null) pkg.Pictures.Add(pic); else pkg.Ignored.Add(file);
                }
            }
            pkg.Costumes = Group(costumeFiles, folder, pkg.Ignored);
            return pkg;
        }

        /// <summary>The costumes a list of file paths gives (used for files on disk and for a download's file list).</summary>
        public static List<PackageCostume> Group(IEnumerable<string> paths, string root, List<string> ignored = null)
        {
            var parsed = new List<KeyValuePair<string, CostumeName>>();
            foreach (var path in paths)
            {
                var n = CostumeName.Parse(System.IO.Path.GetFileName(path));
                if (n == null) { ignored?.Add(System.IO.Path.GetFileName(path)); continue; }
                parsed.Add(new KeyValuePair<string, CostumeName>(path, n));
            }
            string Dir(string p)
            {
                string d = (System.IO.Path.GetDirectoryName(p) ?? "").Replace('\\', '/');
                string r = (root ?? "").Replace('\\', '/');
                if (r != "" && d.StartsWith(r, StringComparison.OrdinalIgnoreCase)) d = d.Substring(r.Length);
                return d.Trim('/');
            }
            var costumes = new List<PackageCostume>();
            // main costume files: one costume per character + number + version + folder
            foreach (var kv in parsed.Where(x => x.Value.Kind == ""))
            {
                var n = kv.Value; string dir = Dir(kv.Key);
                if (costumes.Any(x => x.Character == n.Character && x.Number == n.Number && x.Variant == n.Variant && x.Folder == dir))
                { ignored?.Add(System.IO.Path.GetFileName(kv.Key)); continue; }   // a second copy (.pcs and .pac): keep the first
                var c = new PackageCostume { Character = n.Character, Number = n.Number, Variant = n.Variant, Folder = dir };
                c.Files.Add(new CostumeFile { Path = kv.Key, Kind = "", Number = n.Number });
                costumes.Add(c);
            }
            // the same costume in two folders (e.g. "P+" and "vBrawl"): tell them apart by folder name
            foreach (var g in costumes.GroupBy(x => new { x.Character, x.Number, x.Variant }).Where(g => g.Count() > 1).ToList())
                foreach (var c in g)
                {
                    string last = c.Folder.Split('/').LastOrDefault() ?? "";
                    if (last == "") last = "main folder";
                    c.Variant = c.Variant == "" ? last : c.Variant + ", " + last;
                }
            // extra files (Etc, Final, hats, Nana...): same version and folder if there is one, else the same number
            foreach (var kv in parsed.Where(x => x.Value.Kind != ""))
            {
                var n = kv.Value; string dir = Dir(kv.Key);
                var mine = costumes.Where(x => x.Character == n.Character && x.Number == n.Number).ToList();
                var targets = mine.Where(x => x.Folder == dir && x.Variant.StartsWith(n.Variant, StringComparison.OrdinalIgnoreCase)).ToList();
                if (targets.Count == 0 && n.Variant != "") targets = mine.Where(x => x.Variant.IndexOf(n.Variant, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (targets.Count == 0) targets = mine;
                bool used = false;
                foreach (var c in targets)
                    if (!c.Files.Any(f => f.Kind == n.Kind)) { c.Files.Add(new CostumeFile { Path = kv.Key, Kind = n.Kind, Number = n.Number }); used = true; }
                if (!used) ignored?.Add(System.IO.Path.GetFileName(kv.Key));
            }
            return costumes
                .OrderBy(c => c.Character.Name)
                .ThenBy(c => CostumeName.GameScore(c.Variant + " " + c.Folder))
                .ThenBy(c => c.Number)
                .ThenBy(c => c.Variant.Length).ThenBy(c => c.Variant, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static PackagePicture TryPicture(string path, string file)
        {
            Size size;
            try { using (var img = Image.FromFile(path)) size = img.Size; } catch { return null; }
            PictureKind? kind = null;
            string lower = file.ToLowerInvariant();
            if (lower.Contains("csp") || lower.Contains("menselchrfaceb")) kind = PictureKind.SelectPortrait;
            else if (lower.Contains("stock") || lower.Contains("stc") || lower.Contains("infstc")) kind = PictureKind.StockIcon;
            else if (Regex.IsMatch(lower, @"(^|[^a-z])bp([^a-z]|$)") || lower.Contains("infface") || lower.Contains("battle")) kind = PictureKind.BattlePortrait;
            if (kind == null)
            {
                double r = (double)size.Width / size.Height;
                if (Math.Abs(r - 128.0 / 160) < 0.03 && size.Width >= 96) kind = PictureKind.SelectPortrait;
                else if (Math.Abs(r - 48.0 / 56) < 0.03 && size.Width <= 96) kind = PictureKind.BattlePortrait;
                else if (Math.Abs(r - 1) < 0.01 && size.Width <= 64) kind = PictureKind.StockIcon;
            }
            if (kind == null) return null;
            var num = Regex.Matches(System.IO.Path.GetFileNameWithoutExtension(file), @"\d+").Cast<Match>().LastOrDefault();
            return new PackagePicture { Path = path, Kind = kind.Value, Number = num != null ? (int?)int.Parse(num.Value) : null };
        }

        /// <summary>The picture of a kind that best fits a costume (same number in its name, else the only one).</summary>
        public PackagePicture PictureFor(PackageCostume c, PictureKind kind)
        {
            var all = Pictures.Where(p => p.Kind == kind).ToList();
            if (all.Count == 0) return null;
            if (all.Count == 1) return all[0];
            var byNumber = all.Where(p => p.Number == c.Number || p.Number == c.Number + 1).ToList();
            return byNumber.Count == 1 ? byNumber[0] : null;   // several and no clear match: the player chooses
        }
    }
}
