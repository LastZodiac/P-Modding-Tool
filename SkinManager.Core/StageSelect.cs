// Where a stage file shows up on P+'s stage select screen, and its pictures there. Read from P+'s own files:
//   /Project+/pf/stage/SWITCH/Switch00.rss   the pages: at 0x3C + 0x28*page, a count then icon numbers; a table at 0x104
//                                            gives each icon number (entry i, 2 bytes) its stage slot and picture number
//   /Project+/pf/stage/stageslot/XX.ASL       per stage slot: which buttons load which version ("ASLS", count, name base,
//                                            then button mask + name offset); 0x8000+n / 0x4000+n are entry n of the
//                                            stage's L list / R list (stagelist/XX_L/st_nn_Name.bin)
//   /Project+/pf/stage/stageinfo/<name>.param the stage file a version loads (STG + StageName, + a sub-stage like _00_MORNING)
//   /Project+/pf/menu2/sc_selmap.pac          the pictures: MenSelmapIcon.NN (grid icon), MenSelmapFrontStname.NN (name),
//                                            MenSelmapPrevbase.NN (preview: L, R and Z thumbnails on top, the stage below)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.SSBB.ResourceNodes.ProjectPlus;

namespace SkinManager.Core
{
    /// <summary>One way to get a stage file in the game.</summary>
    public sealed class StageSpot
    {
        public int Slot;             // stage slot (0x21 Smashville)
        public string SlotName;      // "Smashville"
        public int Page, Row, Column;   // where its icon is (1-based); Page 0 = not on the pages
        public int Picture = -1;     // NN of its pictures in sc_selmap.pac
        public ushort Button;        // 0 = just pick it
        public string ListEntry;     // L/R list entry name ("N64")
        public string SubStage;      // a sub-stage the game picks itself (Smashville's time of day)

        public string Region =>      // its part of the preview picture
            Button == 0 ? "main" : Button == 0x40 ? "L" : Button == 0x20 ? "R" : Button == 0x10 ? "Z" : null;

        public string HowToPick
        {
            get
            {
                if (Button == 0) return "just pick it";
                if (Button >= 0x8000 && Button < 0xC000) return $"its L list: \"{ListEntry ?? "#" + ((Button & 0xFF) + 1)}\"";
                if (Button >= 0x4000 && Button < 0x8000) return $"its R list: \"{ListEntry ?? "#" + ((Button & 0xFF) + 1)}\"";
                var keys = new List<string>();
                foreach (var (bit, name) in new[] { (0x40, "L"), (0x20, "R"), (0x10, "Z"), (0x100, "A"), (0x200, "B"), (0x400, "X"), (0x800, "Y"), (0x8, "Up"), (0x4, "Down"), (0x1, "Left"), (0x2, "Right"), (0x1000, "Start") })
                    if ((Button & bit) != 0) keys.Add(name);
                return "hold " + string.Join("+", keys);
            }
        }

        static string Nth(int n) => n == 1 ? "1st" : n == 2 ? "2nd" : n == 3 ? "3rd" : n + "th";
        public string Where => Page == 0 ? SlotName : $"{SlotName} (page {Page}, row {Row}, {Nth(Column)} icon)";

        /// <summary>"Smashville (page 2, row 3, 3rd icon), hold R".</summary>
        public string Text => Where + ", " + HowToPick + (SubStage != null ? $" ({SubStage})" : "");
    }

    public sealed class StageSelect
    {
        public const string PicturesFile = "/Project+/pf/menu2/sc_selmap.pac";
        public static string PreviewTexture(int n) => $"MenSelmapPrevbase.{n:00}";
        public static string IconTexture(int n) => $"MenSelmapIcon.{n:00}";
        public static string NameTexture(int n) => $"MenSelmapFrontStname.{n:00}";

        // the preview picture's parts (312x112): three thumbnails on top, a line, the stage below
        public static readonly Size PreviewSize = new Size(312, 112);
        public static Rectangle RegionRect(string region) =>
            region == "L" ? new Rectangle(0, 0, 76, 44) : region == "R" ? new Rectangle(78, 0, 156, 44) : region == "Z" ? new Rectangle(236, 0, 76, 44)
            : new Rectangle(0, 46, 312, 66);
        public static readonly Rectangle Badge = new Rectangle(0, 0, 26, 20);   // the L/R/Z mark painted in a thumbnail's corner

        // ----- pictures -----

        static readonly string[] PictureTypes = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        /// <summary>The stage select pictures a download comes with for one of its stage files: "preview" (the stage),
        /// "icon" and "name" (paths). Recognises P+'s own picture names (MenSelmapPrevbase/Icon/FrontStname), a Cosmetics
        /// folder (preview-main, icon, name) and screenshots; with several stages in a download, the ones named like the
        /// stage it goes on win ("preview-SVmain" for Smashville), and "main" or "alt" by the version it replaces.</summary>
        public static Dictionary<string, string> FindPictures(string folder, StageFile target, StageSpot spot)
        {
            var result = new Dictionary<string, string>();
            List<string> files;
            try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => PictureTypes.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList(); }
            catch { return result; }
            string Plain(string x) => new string((x ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            var words = new List<string> { Plain(target?.StageName), Plain(target?.Version), Plain(spot?.SlotName) };
            foreach (var kv in Stages.ShortNamesFor(target?.Base)) words.Add(kv.ToLowerInvariant());
            words.RemoveAll(w => w.Length < 2);
            bool main = spot == null || spot.Region == "main";
            int Score(string f)
            {
                string n = Plain(Path.GetFileNameWithoutExtension(f));
                int sc = 0;
                foreach (var w in words) if (n.Contains(w)) sc += 10;
                if (n.Contains("main")) sc += main ? 4 : -4;
                if (n.Contains("alt")) sc += main ? -4 : 4;
                return sc;
            }
            string Best(Func<string, bool> kind) => files.Where(kind).OrderByDescending(Score).ThenBy(f => f.Length).FirstOrDefault();
            string Name(string f) => Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            var preview = Best(f => Name(f).StartsWith("menselmapprevbase")) ?? Best(f => Name(f).Contains("preview") || Name(f).StartsWith("prev"))
                ?? Best(f => Name(f).Contains("screenshot") || Name(f).Contains("screen") || Path.GetFileName(Path.GetDirectoryName(f)).ToLowerInvariant().StartsWith("pic"));
            if (preview != null) result["preview"] = preview;
            var icon = Best(f => Name(f).StartsWith("menselmapicon") || Name(f) == "icon" || Name(f).StartsWith("icon"));
            if (icon != null) result["icon"] = icon;
            var name = Best(f => Name(f).StartsWith("menselmapfrontstname") || Name(f) == "name" || Name(f).StartsWith("name"));
            if (name != null) result["name"] = name;
            return result;
        }

        /// <summary>The stage picture inside a picture: a whole P+ preview (312x112) gives its lower part.</summary>
        public static Bitmap StagePart(Image img)
        {
            if (img.Width == PreviewSize.Width && img.Height == PreviewSize.Height)
            {
                var r = RegionRect("main");
                var b = new Bitmap(r.Width, r.Height);
                using (var g = Graphics.FromImage(b)) g.DrawImage(img, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                return b;
            }
            return new Bitmap(img);
        }

        /// <summary>Draws a picture to fill a box (cut at the sides, keeping its shape).</summary>
        public static void Cover(Graphics g, Image img, Rectangle box)
        {
            float k = Math.Max((float)box.Width / img.Width, (float)box.Height / img.Height);
            float w = img.Width * k, h = img.Height * k;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            var clip = g.Clip; g.SetClip(box);
            g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            g.Clip = clip;
        }

        /// <summary>P+'s preview picture with a stage's picture put in its part (the L/R/Z mark of a thumbnail stays).</summary>
        public static Bitmap PaintPreview(Bitmap preview, string region, Image stage)
        {
            var result = new Bitmap(preview.Width, preview.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var box = RegionRect(region);
            using (var g = Graphics.FromImage(result))
            {
                g.DrawImage(preview, 0, 0, preview.Width, preview.Height);
                Cover(g, stage, box);
                if (region != "main")
                {
                    var badge = new Rectangle(box.X + Badge.X, box.Y + Badge.Y, Badge.Width, Badge.Height);
                    g.DrawImage(preview, badge, badge, GraphicsUnit.Pixel);
                }
            }
            return result;
        }

        /// <summary>A picture for a texture of a given size: icons fill it; name banners keep their shape on black.</summary>
        public static Bitmap Fitted(Image img, Size size, bool banner)
        {
            var b = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                if (!banner) { Cover(g, img, new Rectangle(Point.Empty, size)); return b; }
                g.Clear(Color.Black);
                float k = Math.Min((float)size.Width / img.Width, (float)size.Height / img.Height);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, (size.Width - img.Width * k) / 2, (size.Height - img.Height * k) / 2, img.Width * k, img.Height * k);
            }
            return b;
        }

        /// <summary>Each stage slot's picture number in sc_selmap.pac (MenSelmapPrevbase.NN, MenSelmapIcon.NN).</summary>
        public Dictionary<int, int> Pictures = new Dictionary<int, int>();
        readonly Dictionary<string, List<StageSpot>> _byFile = new Dictionary<string, List<StageSpot>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The ways to get this stage file in the game (none: it isn't on the stage select).</summary>
        public List<StageSpot> SpotsOf(string cardPath) =>
            _byFile.TryGetValue(Path.GetFileNameWithoutExtension(cardPath ?? ""), out var l) ? l : new List<StageSpot>();

        /// <summary>P+'s own names for its stage slots (stageslot/Stageslot IDs.txt): slot -> name.</summary>
        public static Dictionary<int, string> SlotListNames(Fat32Image card)
        {
            var names = new Dictionary<int, string>();
            try
            {
                string txt = Encoding.UTF8.GetString(card.ReadFile("/Project+/pf/stage/stageslot/Stageslot IDs.txt"));
                foreach (var line in txt.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*([0-9A-Fa-f]{2})\s*-\s*(.+?)\s*(\(.*)?$");
                    if (m.Success) names[Convert.ToInt32(m.Groups[1].Value, 16)] = m.Groups[2].Value.Trim();
                }
            }
            catch { }
            return names;
        }

        public string WhereText(string cardPath)
        {
            var s = SpotsOf(cardPath);
            return s.Count == 0 ? null : string.Join("; or ", s.Take(3).Select(x => x.Text));
        }

        public static StageSelect Read(Fat32Image card)
        {
            var ss = new StageSelect();
            const string root = "/Project+/pf/stage";
            // stage slot names (P+'s own list)
            var names = new Dictionary<int, string>();
            try
            {
                string txt = Encoding.UTF8.GetString(card.ReadFile(root + "/stageslot/Stageslot IDs.txt"));
                foreach (var line in txt.Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*([0-9A-Fa-f]{2})\s*-\s*(.+?)\s*(\(.*)?$");
                    if (m.Success) names[Convert.ToInt32(m.Groups[1].Value, 16)] = m.Groups[2].Value.Trim();
                }
            }
            catch { }

            // pages and picture numbers
            var where = new Dictionary<int, (int page, int row, int col)>();
            var picture = new Dictionary<int, int>();
            try
            {
                var rss = card.ReadFile(root + "/SWITCH/Switch00.rss");
                for (int page = 0; page < 5; page++)
                {
                    int at = 0x3C + 0x28 * page;
                    if (at >= rss.Length) break;
                    int count = rss[at];
                    for (int k = 0; k < count && k < 39; k++)
                    {
                        int icon = rss[at + 1 + k];
                        int e = 0x104 + 2 * icon;
                        if (e + 1 >= rss.Length) continue;
                        int slot = rss[e];
                        if (!where.ContainsKey(slot)) where[slot] = (page + 1, k / 7 + 1, k % 7 + 1);
                        picture[slot] = rss[e + 1];
                    }
                }
            }
            catch { }

            foreach (var kv in picture) ss.Pictures[kv.Key] = kv.Value;
            // each slot's versions and the stage files they load
            var infoFolder = root + "/stageinfo";
            var stageFiles = new HashSet<string>(card.ListFolder(Stages.Folder).Where(e => !e.IsDirectory).Select(e => Path.GetFileNameWithoutExtension(e.Name)), StringComparer.OrdinalIgnoreCase);
            if (!card.Exists(root + "/stageslot")) return ss;
            foreach (var asl in card.ListFolder(root + "/stageslot").Where(e => !e.IsDirectory && e.Name.EndsWith(".asl", StringComparison.OrdinalIgnoreCase)))
            {
                int slot;
                try { slot = Convert.ToInt32(Path.GetFileNameWithoutExtension(asl.Name), 16); } catch { continue; }
                byte[] d;
                try { d = card.ReadFile(root + "/stageslot/" + asl.Name); } catch { continue; }
                if (d.Length < 8 || d[0] != 'A' || d[1] != 'S' || d[2] != 'L' || d[3] != 'S') continue;
                int count = d[4] << 8 | d[5], nameBase = d[6] << 8 | d[7];
                for (int i = 0; i < count; i++)
                {
                    int e = 8 + 4 * i;
                    if (e + 3 >= d.Length) break;
                    ushort button = (ushort)(d[e] << 8 | d[e + 1]);
                    int at = nameBase + (d[e + 2] << 8 | d[e + 3]), end = at;
                    while (end < d.Length && d[end] != 0) end++;
                    if (at >= d.Length) continue;
                    string param = Encoding.ASCII.GetString(d, at, end - at);
                    foreach (var (file, sub) in FilesOf(card, infoFolder + "/" + param + ".param", stageFiles))
                    {
                        var spot = new StageSpot
                        {
                            Slot = slot, SlotName = names.TryGetValue(slot, out var n) ? n : param.Replace('_', ' '),
                            Button = button, SubStage = sub, Picture = picture.TryGetValue(slot, out var p) ? p : -1,
                        };
                        if (where.TryGetValue(slot, out var w)) { spot.Page = w.page; spot.Row = w.row; spot.Column = w.col; }
                        if (button >= 0x4000 && button < 0xC000) spot.ListEntry = ListEntry(card, root, slot, button);
                        if (!ss._byFile.TryGetValue(file, out var list)) ss._byFile[file] = list = new List<StageSpot>();
                        list.Add(spot);
                    }
                }
            }
            foreach (var k in ss._byFile.Keys.ToList()) ss._byFile[k] = ss._byFile[k].GroupBy(x => (x.Slot, x.Button)).Select(g => g.First()).ToList();
            foreach (var l in ss._byFile.Values) l.Sort((a, b) => (a.Page == 0 ? 99 : a.Page).CompareTo(b.Page == 0 ? 99 : b.Page) != 0 ? (a.Page == 0 ? 99 : a.Page).CompareTo(b.Page == 0 ? 99 : b.Page) : a.Button.CompareTo(b.Button));
            return ss;
        }

        /// <summary>The stage files a version's settings load: STG + its stage name, or that plus one of its sub-stages.</summary>
        static IEnumerable<(string File, string Sub)> FilesOf(Fat32Image card, string paramPath, HashSet<string> stageFiles)
        {
            var result = new List<(string, string)>();
            byte[] bytes;
            try { if (!card.Exists(paramPath)) return result; bytes = card.ReadFile(paramPath); } catch { return result; }
            try
            {
                using (var tmp = new TempFile(".param", bytes))
                lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    if (!(root is STEXNode stex) || string.IsNullOrWhiteSpace(stex.StageName)) return result;
                    string stage = "STG" + stex.StageName.Trim();
                    if (stageFiles.Contains(stage)) result.Add((stage, null));
                    foreach (var sub in stex.Children)
                    {
                        string n = (sub.Name ?? "").Trim();
                        if (n == "" || n.StartsWith("<")) continue;
                        string file = stage + (n.StartsWith("_") ? "" : "_") + n;
                        string pretty = System.Text.RegularExpressions.Regex.Replace(n.Trim('_'), @"^\d+_?", "").Replace('_', ' ');
                        pretty = pretty.Length > 0 ? char.ToUpper(pretty[0]) + pretty.Substring(1).ToLowerInvariant() : n;
                        if (stageFiles.Contains(file)) result.Add((file, "the game picks the " + pretty + " version itself"));
                    }
                }
            }
            catch { }
            return result;
        }

        static string ListEntry(Fat32Image card, string root, int slot, ushort button)
        {
            try
            {
                string folder = $"{root}/stagelist/{slot:X2}_{(button >= 0x8000 ? "L" : "R")}";
                int n = button & 0xFF;
                if (!card.Exists(folder)) return null;
                var e = card.ListFolder(folder).FirstOrDefault(x => x.Name.StartsWith($"st_{n:00}_", StringComparison.OrdinalIgnoreCase));
                if (e == null) return null;
                string entry = Path.GetFileNameWithoutExtension(e.Name).Substring(6).Replace('_', ' ');
                return System.Text.RegularExpressions.Regex.Replace(entry, @"(?<=[a-z])(?=[A-Z0-9])", " ");   // BeachConcert -> Beach Concert
            }
            catch { return null; }
        }
    }
}
