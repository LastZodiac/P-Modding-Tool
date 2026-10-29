// New versions of the app from its GitHub Releases page. The app asks GitHub for the newest release (at most every
// few hours), and when it's newer than this one shows a bar with an "Update" button. The update file is checked
// against the SHA-256 list published with the release before anything is used, then put in the Update folder,
// where SelfUpdate swaps it in. Where to look is in "update-source.txt" next to the app ("owner/repo").
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SkinManager.Core;

namespace SkinManager.App
{
    static class OnlineUpdate
    {
        public sealed class Release
        {
            public Version Version;
            public string Name, Notes, Page, ZipUrl, ZipName, SumsUrl;
        }

        public static Version Current => typeof(OnlineUpdate).Assembly.GetName().Version;
        public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

        static string AppFolder => AppDomain.CurrentDomain.BaseDirectory;
        static string StampFile => Path.Combine(Program.DataFolder, "app-update-check.txt");

        /// <summary>"owner/repo" of the releases page (null: this copy doesn't check).</summary>
        public static string Repo
        {
            get
            {
                try
                {
                    string f = Path.Combine(AppFolder, "update-source.txt");
                    if (!File.Exists(f)) return null;
                    string r = File.ReadAllLines(f).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
                    return r != null && System.Text.RegularExpressions.Regex.IsMatch(r, @"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$") ? r : null;
                }
                catch { return null; }
            }
        }

        /// <summary>Time to look again? (Not more than every 6 hours, unless asked.)</summary>
        public static bool Due()
        {
            try { return !File.Exists(StampFile) || DateTime.UtcNow - DateTime.Parse(File.ReadAllText(StampFile).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind) > TimeSpan.FromHours(6); }
            catch { return true; }
        }

        static WebClient Client()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var w = new WebClient { Encoding = Encoding.UTF8 };
            w.Headers[HttpRequestHeader.UserAgent] = "PPlusModdingTool/" + CurrentText;
            w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return w;
        }

        /// <summary>The newest release when it's newer than this copy, else null.</summary>
        public static Release Check()
        {
            string repo = Repo;
            if (repo == null) return null;
            string json;
            string api = (Environment.GetEnvironmentVariable("SKINMANAGER_UPDATE_API") ?? "https://api.github.com").TrimEnd('/');   // tests
            using (var w = Client()) json = w.DownloadString($"{api}/repos/{repo}/releases/latest");
            try { Directory.CreateDirectory(Program.DataFolder); File.WriteAllText(StampFile, DateTime.UtcNow.ToString("o")); } catch { }
            var o = Json.Parse(json);
            string tag = Json.Str(o, "tag_name") ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            var v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
            if (v <= new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build))) return null;
            var r = new Release { Version = v, Name = Json.Str(o, "name") ?? tag, Notes = Json.Str(o, "body") ?? "", Page = Json.Str(o, "html_url") };
            foreach (var a in Json.Arr(o, "assets") ?? new List<object>())
            {
                string name = Json.Str(a, "name") ?? "", url = Json.Str(a, "browser_download_url");
                if (name.StartsWith("PPlusModdingTool-update", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { r.ZipUrl = url; r.ZipName = name; }
                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) r.SumsUrl = url;
            }
            return r.ZipUrl != null && r.SumsUrl != null ? r : null;
        }

        /// <summary>Downloads the update, checks it, and puts it in the Update folder (SelfUpdate.Run swaps it in).</summary>
        public static void Download(Release r, Action<int> progress)
        {
            string temp = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "update-" + Guid.NewGuid().ToString("N") + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(temp));
            try
            {
                string sums;
                using (var w = Client()) sums = w.DownloadString(r.SumsUrl);
                using (var w = Client())
                {
                    w.DownloadProgressChanged += (s, e) => progress?.Invoke(e.ProgressPercentage);
                    w.DownloadFileTaskAsync(new Uri(r.ZipUrl), temp).GetAwaiter().GetResult();
                }
                string sha;
                using (var f = File.OpenRead(temp)) using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                bool listed = sums.Split('\n').Select(l => l.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries))
                    .Any(p => p.Length >= 2 && p[0].ToLowerInvariant() == sha && p.Last().Equals(r.ZipName, StringComparison.OrdinalIgnoreCase));
                if (!listed) throw new InvalidDataException("the downloaded file doesn't match the one published with the release, so it wasn't used");
                string drop = Path.Combine(AppFolder, "Update");
                if (Directory.Exists(drop)) Directory.Delete(drop, true);
                Directory.CreateDirectory(drop);
                using (var zip = ZipFile.OpenRead(temp))
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string target = Path.GetFullPath(Path.Combine(drop, e.FullName));
                        if (!target.StartsWith(Path.GetFullPath(drop), StringComparison.OrdinalIgnoreCase)) continue;   // nothing outside the folder
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                File.WriteAllText(Path.Combine(drop, SelfUpdate.VerifiedMark), r.Version.ToString());
            }
            finally { try { File.Delete(temp); } catch { } }
        }
    }
}
