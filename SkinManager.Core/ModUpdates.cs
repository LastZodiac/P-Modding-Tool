// "Update available": a mod in My Mods whose GameBanana page has a newer version of the file it was installed from.
// A newer version is a file added later with the same name once version numbers are left out ("ZSS v1.2.zip" and
// "ZSS v1.3.zip"), or, when the installed file was taken down, the only file left (or the one with the same name).
// Other new files on the page (another colour, another stage) aren't updates. Checked at most twice a day per mod.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public static class ModUpdates
    {
        /// <summary>The installed mods' GameBanana mods and files: (mod id, file id, page url).</summary>
        public static IEnumerable<(long Mod, long File, string Page)> Installed(Library lib) =>
            lib.Skins.Where(s => s.GameBananaModId != 0 && s.GameBananaFileId != 0).Select(s => (s.GameBananaModId, s.GameBananaFileId, s.PageUrl))
               .Concat(lib.Stages.Where(s => s.GameBananaModId != 0 && s.GameBananaFileId != 0).Select(s => (s.GameBananaModId, s.GameBananaFileId, s.PageUrl)))
               .Distinct();

        public static string ModelOf(string pageUrl) => pageUrl != null && pageUrl.IndexOf("/sounds/", StringComparison.OrdinalIgnoreCase) >= 0 ? "Sound" : "Mod";

        static string CacheFile(string data) => Path.Combine(data, "updates.txt");

        /// <summary>Installed file id -> the id of its newer version. Uses the saved answers when they're less than 12 hours old.</summary>
        public static Dictionary<long, long> Check(Library lib, string dataFolder, bool force = false)
        {
            var cache = new Dictionary<long, (DateTime When, long Newer)>();
            try
            {
                if (File.Exists(CacheFile(dataFolder)))
                    foreach (var l in File.ReadAllLines(CacheFile(dataFolder)))
                    {
                        var p = l.Split('|');
                        if (p.Length == 3) cache[long.Parse(p[0])] = (new DateTime(long.Parse(p[1])), long.Parse(p[2]));
                    }
            }
            catch { cache.Clear(); }
            var result = new Dictionary<long, long>();
            var names = FileNames(dataFolder);
            foreach (var mod in Installed(lib).GroupBy(x => x.Mod))
            {
                var files = mod.Select(x => x.File).Distinct().ToList();
                bool fresh = !force && files.All(f => cache.TryGetValue(f, out var c) && DateTime.Now - c.When < TimeSpan.FromHours(12));
                if (!fresh)
                {
                    GbDetails d;
                    try { d = GameBanana.Details(mod.Key, ModelOf(mod.First().Page), contents: false); }
                    catch { continue; }   // offline or the page is gone: say nothing
                    foreach (var f in files)
                    {
                        names.TryGetValue(f, out string name);
                        cache[f] = (DateTime.Now, Newer(d, f, name)?.Id ?? 0);
                    }
                }
                foreach (var f in files) if (cache.TryGetValue(f, out var c) && c.Newer != 0) result[f] = c.Newer;
            }
            try { File.WriteAllLines(CacheFile(dataFolder), cache.Select(kv => kv.Key + "|" + kv.Value.When.Ticks + "|" + kv.Value.Newer)); } catch { }
            return result;
        }

        /// <summary>The newer version of an installed file on the mod's page, or null.</summary>
        public static GbFile Newer(GbDetails d, long installedFileId, string installedName = null)
        {
            var old = d.Files.FirstOrDefault(f => f.Id == installedFileId);
            if (old != null)
                return d.Files.Where(f => f.Id != old.Id && f.Added > old.Added && Stem(f.Name) == Stem(old.Name)).OrderByDescending(f => f.Added).FirstOrDefault();
            // the installed file was taken down
            if (installedName != null)
            {
                var same = d.Files.Where(f => Stem(f.Name) == Stem(installedName)).OrderByDescending(f => f.Added).FirstOrDefault();
                if (same != null) return same;
            }
            return d.Files.Count == 1 ? d.Files[0] : null;
        }

        /// <summary>A file's name without its version: "ZSS_Sm4sh_v1.2-fixed.zip" -> "zsssm4sh".</summary>
        public static string Stem(string name)
        {
            string s = Regex.Replace(Path.GetFileNameWithoutExtension(name ?? "").ToLowerInvariant(), @"[_\-+]", " ");
            s = Regex.Replace(s, @"\b(v|ver|version)?\s*\d+([._]\d+)*[a-z]?\b", " ");
            s = Regex.Replace(s, @"\b(update[sd]?|fix(ed)?|final|new|latest|hotfix|patch(ed)?|copy)\b", " ");
            s = Regex.Replace(s, @"\(\d+\)", " ");
            s = Regex.Replace(s, @"[^a-z0-9]", "");
            return s;
        }

        /// <summary>File id -> its name, from the app's download records.</summary>
        static Dictionary<long, string> FileNames(string dataFolder)
        {
            var r = new Dictionary<long, string>();
            try { foreach (var d in DownloadInfo.All(dataFolder)) if (d.FileId != 0 && d.FileName != null) r[d.FileId] = d.FileName; } catch { }
            return r;
        }
    }
}
