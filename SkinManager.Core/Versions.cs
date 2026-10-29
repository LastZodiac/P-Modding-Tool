// Versions of Brawl Online, its Dolphin and P+, read from Brawl Online's own files, and P+'s release lists
// (every file of a release with its fingerprint) that Brawl Online keeps in netplay\pplus-versions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public sealed class VersionInfo
    {
        public string Launcher, Dolphin, PPlus, NewestPPlus;
        public string Key => $"{Launcher}|{Dolphin}|{PPlus}";
    }

    public sealed class PPlusRelease
    {
        public string Version, ProbePath, ProbeSha;
        public Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // path -> sha256
    }

    public static class Versions
    {
        static string ProgramFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Brawl Online");

        public static VersionInfo Read(BrawlOnlineInstall install)
        {
            var v = new VersionInfo();
            try { v.Launcher = AsarVersion(Path.Combine(ProgramFolder, "resources", "app.asar")); } catch { }
            try { v.Dolphin = Field(File.ReadAllText(Path.Combine(install.NetplayFolder, "dolphin.json")), "version"); } catch { }
            try { v.PPlus = Field(File.ReadAllText(Path.Combine(install.NetplayFolder, "pplus", "pplus.json")), "version"); } catch { }
            try { v.NewestPPlus = Field(File.ReadAllText(Path.Combine(install.NetplayFolder, "pplus-versions", "index.json")), "newest"); } catch { }
            return v;
        }

        static string Field(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>The launcher's version, from package.json inside its app.asar.</summary>
        static string AsarVersion(string asar)
        {
            using (var f = File.OpenRead(asar))
            {
                var head = new byte[16]; f.Read(head, 0, 16);
                int size = BitConverter.ToInt32(head, 4), jsonLen = BitConverter.ToInt32(head, 12);
                var json = new byte[jsonLen]; f.Read(json, 0, jsonLen);
                var m = Regex.Match(Encoding.UTF8.GetString(json), "\"package\\.json\"\\s*:\\s*\\{\"size\":(\\d+),\"offset\":\"(\\d+)\"");
                if (!m.Success) return null;
                long offset = 8 + size + long.Parse(m.Groups[2].Value); int len = int.Parse(m.Groups[1].Value);
                var pkg = new byte[len]; f.Position = offset; f.Read(pkg, 0, len);
                return Field(Encoding.UTF8.GetString(pkg), "version");
            }
        }

        /// <summary>The newest Brawl Online launcher on its update site (null if it can't be reached).</summary>
        public static string LatestLauncherOnline()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://brawlonline.net/updates/launcher/latest.yml");
                req.Timeout = 6000; req.ReadWriteTimeout = 6000; req.UserAgent = "PPlusModdingTool";
                using (var res = req.GetResponse()) using (var r = new StreamReader(res.GetResponseStream()))
                {
                    var m = Regex.Match(r.ReadToEnd(), @"(?m)^version:\s*['""]?([0-9][^\s'""]*)");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch { return null; }
        }

        public static bool Newer(string a, string b)   // is a newer than b?
        {
            int[] P(string s) => Regex.Matches(s ?? "", @"\d+").Cast<Match>().Select(x => int.Parse(x.Value)).ToArray();
            var x1 = P(a); var y1 = P(b);
            for (int i = 0; i < Math.Max(x1.Length, y1.Length); i++)
            {
                int p = i < x1.Length ? x1[i] : 0, q = i < y1.Length ? y1[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        /// <summary>A P+ release's file list as Brawl Online keeps it (null if not on this PC).</summary>
        public static PPlusRelease Release(BrawlOnlineInstall install, string version)
        {
            string path = Path.Combine(install.NetplayFolder, "pplus-versions", "releases", version + ".json");
            if (version == null || !File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            var r = new PPlusRelease { Version = version };
            var probe = Regex.Match(json, "\"probe\"\\s*:\\s*\\{\"path\":\"((?:[^\"\\\\]|\\\\.)*)\",\"sha256\":\"([0-9a-f]{64})\"");
            if (probe.Success) { r.ProbePath = Regex.Unescape(probe.Groups[1].Value); r.ProbeSha = probe.Groups[2].Value; }
            foreach (Match m in Regex.Matches(json, "\\{\"path\":\"((?:[^\"\\\\]|\\\\.)*)\",\"sha256\":\"([0-9a-f]{64})\",\"size\":-?\\d+\\}"))
                r.Files[Regex.Unescape(m.Groups[1].Value)] = m.Groups[2].Value;
            return r;
        }

        /// <summary>Is this card on that release (by the file Brawl Online itself checks)?</summary>
        public static bool IsOn(Fat32Image card, PPlusRelease r) =>
            r != null && r.ProbePath != null && card.Exists(r.ProbePath) && Library.Sha256(card.ReadFile(r.ProbePath)) == r.ProbeSha;
    }
}
