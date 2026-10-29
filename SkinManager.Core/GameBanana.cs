// Reads Brawl costume listings from GameBanana's public API (the one linked as "gbAPI" in the
// footer of gamebanana.com) and downloads the files. Nothing is uploaded and no account is used.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SkinManager.Core
{
    public enum GbSort { MostDownloaded, MostLiked, Newest }

    public sealed class GbMod
    {
        public long Id;
        public string Name, Author, Category, ProfileUrl;
        public string ThumbUrl, BigImageUrl;
        public long Likes, Views;
        public DateTime Added, Modified;
        public bool Obsolete, HasContentRatings, HasFiles;
        public string AudioUrl;   // a preview to listen to (music)
        public List<GbShot> Shots = new List<GbShot>();   // every preview picture on the mod's page
    }

    public sealed class GbShot { public string Small, Big, Caption; }

    public sealed class GbPage
    {
        public List<GbMod> Mods = new List<GbMod>();
        public long Total;
        public int Page, PerPage;
        public int Pages => PerPage <= 0 ? 1 : (int)Math.Max(1, (Total + PerPage - 1) / PerPage);
    }

    public sealed class GbFile
    {
        public long Id;
        public string Name, DownloadUrl, Md5, Description, VirusScan;
        public long Size;
        public DateTime Added;
        public List<string> Contents;   // paths inside the archive, when GameBanana knows them
        public string Extension => Path.GetExtension(Name ?? "").ToLowerInvariant();
    }

    public sealed class GbDetails
    {
        public GbMod Mod;
        public string Text, License;
        public long Downloads;
        public bool Withheld;
        public List<GbFile> Files = new List<GbFile>();
        public List<KeyValuePair<string, string>> OtherSites = new List<KeyValuePair<string, string>>();   // url, description
    }

    public enum GbVerdictKind { Good, OtherCharacter, Maybe, NotASkin, OtherSite }

    public sealed class GbVerdict
    {
        public GbVerdictKind Kind;
        public string Text;
        public List<PackageCostume> Costumes = new List<PackageCostume>();
    }

    public static class GameBanana
    {
        public const int BrawlGameId = 5678;
        public const int AllCostumes = 10131;
        public const int CostumePacks = 10170;
        public const int PokemonTrainer = 10149;
        public const int AllStages = 6540, CompetitiveStages = 12498, OneToOneStages = 12499;
        public const int AllMusic = 3108, StageMusic = 3110, VictoryThemes = 3111;
        public const int PerPage = 24;

        // GameBanana's costume categories for Brawl (read from /apiv11/Mod/Categories?_idCategoryRow=10131).
        static readonly Dictionary<string, int> CategoryOf = new Dictionary<string, int>
        {
            ["Bowser"] = 10141, ["Captain Falcon"] = 10159, ["Charizard"] = 10172, ["Diddy Kong"] = 10150,
            ["Donkey Kong"] = 10134, ["Falco"] = 10158, ["Fox"] = 10140, ["Ganondorf"] = 10162,
            ["Ice Climbers"] = 10143, ["Ike"] = 10148, ["Ivysaur"] = 10171, ["Jigglypuff"] = 10164,
            ["King Dedede"] = 10153, ["Kirby"] = 10139, ["Knuckles"] = 12259, ["Link"] = 10135,
            ["Lucario"] = 10160, ["Lucas"] = 10152, ["Luigi"] = 10157, ["Mario"] = 10133,
            ["Marth"] = 10156, ["Meta Knight"] = 10151, ["Mewtwo"] = 11866, ["Mr. Game & Watch"] = 10163,
            ["Ness"] = 10155, ["Olimar"] = 10154, ["Peach"] = 10142, ["Pikachu"] = 10132,
            ["Pit"] = 10146, ["R.O.B."] = 10161, ["Roy"] = 11867, ["Samus"] = 10136,
            ["Sheik"] = 10145, ["Snake"] = 10167, ["Sonic"] = 10168, ["Squirtle"] = 10180,
            ["Toon Link"] = 10165, ["Wario"] = 10147, ["Wolf"] = 10166, ["Yoshi"] = 10138,
            ["Zelda"] = 10144, ["Zero Suit Samus"] = 10137,
        };

        public static int CategoryFor(Character c) => c != null && CategoryOf.TryGetValue(c.Name, out int id) ? id : AllCostumes;

        // For testing against saved answers: SKINMANAGER_GB_BASE / SKINMANAGER_GB_IMAGES replace the real hosts.
        static readonly string Site = (Environment.GetEnvironmentVariable("SKINMANAGER_GB_BASE") ?? "https://gamebanana.com").TrimEnd('/');
        static readonly string Images = Environment.GetEnvironmentVariable("SKINMANAGER_GB_IMAGES")?.TrimEnd('/');

        public static string Local(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (Images != null && url.StartsWith("https://images.gamebanana.com", StringComparison.OrdinalIgnoreCase)) return Images + url.Substring("https://images.gamebanana.com".Length);
            if (Site != "https://gamebanana.com" && url.StartsWith("https://gamebanana.com", StringComparison.OrdinalIgnoreCase)) return Site + url.Substring("https://gamebanana.com".Length);
            return url;
        }

        static GameBanana()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        const string UserAgent = "PPlusModdingTool/1.1 (Project+ costume manager for Brawl Online)";

        static HttpWebRequest Request(string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(Local(url));
            r.UserAgent = UserAgent;
            r.Accept = "application/json, */*";
            r.Timeout = 30000; r.ReadWriteTimeout = 60000;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            r.AllowAutoRedirect = true;
            return r;
        }

        public static string GetText(string url)
        {
            try
            {
                using (var resp = (HttpWebResponse)Request(url).GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return rd.ReadToEnd();
            }
            catch (WebException e) when (e.Response is HttpWebResponse hr)
            {
                // GameBanana answers bad requests with a JSON error body; pass it on so callers can read it
                using (var rd = new StreamReader(hr.GetResponseStream(), Encoding.UTF8))
                {
                    string body = rd.ReadToEnd();
                    if (body.TrimStart().StartsWith("{")) return body;
                    throw new GbException($"GameBanana answered {(int)hr.StatusCode} {hr.StatusDescription}.", e);
                }
            }
            catch (WebException e) { throw new GbException("Couldn't reach GameBanana. Check that this PC is online, then try again.", e); }
        }

        static object GetJson(string url)
        {
            string text = GetText(url);
            object j;
            try { j = Json.Parse(text); }
            catch (Exception e) { throw new GbException("GameBanana sent something this app doesn't understand.", e); }
            string err = Json.Str(j, "_sErrorCode");
            if (err != null) throw new GbException("GameBanana said: " + (Json.Str(j, "_sErrorMessage") ?? err));
            return j;
        }

        // ----- listing and searching -----

        public static string ListUrl(int category, string search, GbSort sort, int page, string model = "Mod")
        {
            var sb = new StringBuilder(Site + "/apiv11/" + model + "/Index?_nPerpage=" + PerPage + "&_nPage=" + Math.Max(1, page));
            sb.Append("&_aFilters[Generic_Category]=").Append(category);
            string s = (search ?? "").Trim();
            if (s != "") sb.Append("&_aFilters[Generic_Name]=contains,").Append(Uri.EscapeDataString(s));
            sb.Append("&_sSort=").Append(sort == GbSort.MostLiked ? "Generic_MostLiked" : sort == GbSort.Newest ? "Generic_Newest" : "Generic_MostDownloaded");
            return sb.ToString();
        }

        public static GbPage List(int category, string search, GbSort sort, int page, string model = "Mod") =>
            ParseList(GetJson(ListUrl(category, search, sort, page, model)), page);

        public static GbPage ParseList(object j, int page)
        {
            var result = new GbPage { Page = page, PerPage = PerPage };
            var meta = Json.Obj(j, "_aMetadata");
            result.Total = Json.Num(meta, "_nRecordCount");
            long per = Json.Num(meta, "_nPerpage"); if (per > 0) result.PerPage = (int)per;
            foreach (var r in Json.Arr(j, "_aRecords") ?? new List<object>())
            {
                if (Json.Str(r, "_sModelName") is string model && model != "Mod" && model != "Sound") continue;
                result.Mods.Add(ParseMod(r));
            }
            return result;
        }

        static DateTime FromUnix(long ts) => ts <= 0 ? DateTime.MinValue : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts).ToLocalTime();

        static GbMod ParseMod(object r)
        {
            var m = new GbMod
            {
                Id = Json.Num(r, "_idRow"),
                Name = WebUtility.HtmlDecode(Json.Str(r, "_sName") ?? "(no name)"),
                Author = Json.Str(Json.Obj(r, "_aSubmitter"), "_sName") ?? "",
                Category = Json.Str(Json.Obj(r, "_aSubCategory") ?? Json.Obj(r, "_aCategory"), "_sName") ?? "",
                ProfileUrl = Json.Str(r, "_sProfileUrl"),
                Likes = Json.Num(r, "_nLikeCount"), Views = Json.Num(r, "_nViewCount"),
                Added = FromUnix(Json.Num(r, "_tsDateAdded")), Modified = FromUnix(Json.Num(r, "_tsDateModified")),
                Obsolete = Json.Bool(r, "_bIsObsolete"), HasContentRatings = Json.Bool(r, "_bHasContentRatings"),
                HasFiles = !(r is Dictionary<string, object> d && d.ContainsKey("_bHasFiles")) || Json.Bool(r, "_bHasFiles"),
            };
            if (string.IsNullOrEmpty(m.ProfileUrl)) m.ProfileUrl = "https://gamebanana.com/mods/" + m.Id;
            m.AudioUrl = Json.Str(Json.Obj(Json.Obj(r, "_aPreviewMedia"), "_aMetadata"), "_sAudioUrl");
            // the first screenshot, skipping .webp ones when possible (Windows' picture reader can't open those)
            var shots = (Json.Arr(Json.Obj(r, "_aPreviewMedia"), "_aImages") ?? new List<object>())
                .Where(x => Json.Str(x, "_sType") == "screenshot" && Json.Str(x, "_sFile") != null).ToList();
            var img = shots.FirstOrDefault(x => !Json.Str(x, "_sFile").EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ?? shots.FirstOrDefault();
            foreach (var x in shots)
            {
                string b0 = Json.Str(x, "_sBaseUrl");
                m.Shots.Add(new GbShot
                {
                    Small = b0 + "/" + (Json.Str(x, "_sFile100") ?? Json.Str(x, "_sFile")),
                    Big = b0 + "/" + (Json.Str(x, "_sFile530") ?? Json.Str(x, "_sFile")),
                    Caption = Json.Str(x, "_sCaption"),
                });
            }
            if (img != null)
            {
                string b = Json.Str(img, "_sBaseUrl");
                string small = Json.Str(img, "_sFile220") ?? Json.Str(img, "_sFile100") ?? Json.Str(img, "_sFile");
                string big = Json.Str(img, "_sFile530") ?? Json.Str(img, "_sFile");
                m.ThumbUrl = b + "/" + small; m.BigImageUrl = b + "/" + big;
            }
            return m;
        }

        // ----- one mod's page and files -----

        public static GbDetails Details(long modId, string model = "Mod", bool contents = true)
        {
            var j = GetJson(Site + "/apiv11/" + model + "/" + modId + "/ProfilePage");
            var d = ParseDetails(j);
            if (d.Mod.Id == 0) d.Mod.Id = modId;
            if (contents) foreach (var f in d.Files)
            {
                try { f.Contents = ParseContents(GetJson(Site + "/apiv13/File/" + f.Id)); }
                catch (GbException) { f.Contents = null; }   // the list of files inside is a nice-to-have
            }
            return d;
        }

        public static GbDetails ParseDetails(object j)
        {
            var d = new GbDetails
            {
                Mod = ParseMod(j),
                Text = HtmlToText(Json.Str(j, "_sText") ?? Json.Str(j, "_sDescription") ?? ""),
                License = HtmlToText(Json.Str(j, "_sLicense") ?? ""),
                Downloads = Json.Num(j, "_nDownloadCount"),
                Withheld = Json.Bool(j, "_bIsWithheld") || Json.Bool(j, "_bIsTrashed"),
            };
            if (Json.Obj(j, "_aCategory") is Dictionary<string, object> cat) d.Mod.Category = Json.Str(cat, "_sName") ?? d.Mod.Category;
            foreach (var f in Json.Arr(j, "_aFiles") ?? new List<object>())
            {
                d.Files.Add(new GbFile
                {
                    Id = Json.Num(f, "_idRow"), Name = Json.Str(f, "_sFile") ?? "download", Size = Json.Num(f, "_nFilesize"),
                    DownloadUrl = Json.Str(f, "_sDownloadUrl") ?? "https://gamebanana.com/dl/" + Json.Num(f, "_idRow"),
                    Md5 = Json.Str(f, "_sMd5Checksum"), Description = Json.Str(f, "_sDescription"),
                    VirusScan = Json.Str(f, "_sAvResult"), Added = FromUnix(Json.Num(f, "_tsDateAdded")),
                });
            }
            foreach (var a in Json.Arr(j, "_aAlternateFileSources") ?? new List<object>())
            {
                string url = Json.Str(a, "url");
                if (url != null && (url.StartsWith("http://") || url.StartsWith("https://")))
                    d.OtherSites.Add(new KeyValuePair<string, string>(url, Json.Str(a, "description") ?? "another site"));
            }
            return d;
        }

        /// <summary>The file paths inside an archive, from GameBanana's "_aArchiveFileTree" (folders are objects or arrays, files are strings).</summary>
        public static List<string> ParseContents(object j)
        {
            if (!(j is Dictionary<string, object> d) || !d.TryGetValue("_aArchiveFileTree", out var tree) || tree == null) return null;
            var paths = new List<string>();
            void Walk(object node, string prefix)
            {
                if (node is string s) { paths.Add(prefix + s); return; }
                if (node is List<object> l) { foreach (var x in l) Walk(x, prefix); return; }
                if (node is Dictionary<string, object> o)
                    foreach (var kv in o)
                    {
                        if (kv.Value is string file) paths.Add(prefix + file);
                        else Walk(kv.Value, prefix + kv.Key + "/");
                    }
            }
            Walk(tree, "");
            return paths;
        }

        public static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string t = Regex.Replace(html, @"<\s*(br|/p|/div|/li|/h\d|/tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<\s*li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<[^>]+>", "");
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"[ \t ]+", " ");
            t = Regex.Replace(t, @"\s*\n\s*(\n\s*)+", "\n\n");
            return t.Trim();
        }

        // ----- what's in a file, before downloading it -----

        public static GbVerdict Judge(GbFile f, Character wanted, IEnumerable<KeyValuePair<string, string>> otherSites)
        {
            var v = new GbVerdict();
            if (f.Contents == null)
            {
                v.Kind = GbVerdictKind.Maybe;
                v.Text = "GameBanana doesn't list what's inside. The app will look after downloading.";
                return v;
            }
            v.Costumes = SkinPackage.Group(f.Contents, "");
            var mine = v.Costumes.Where(c => wanted == null || c.Character == wanted).ToList();
            if (mine.Count > 0)
            {
                v.Kind = GbVerdictKind.Good;
                var who = mine.Select(c => c.Character).Distinct().ToList();
                string what = string.Join(", ", mine.Take(4).Select(c => c.NumberText + (c.Variant != "" ? " (" + c.Variant + ")" : "")));
                if (mine.Count > 4) what += $" and {mine.Count - 4} more";
                v.Text = (who.Count == 1 ? who[0].Name : "Character") + (mine.Count == 1 ? " costume " : " costumes ") + what;
                return v;
            }
            if (v.Costumes.Count > 0)
            {
                v.Kind = GbVerdictKind.OtherCharacter;
                v.Text = "Costume for " + string.Join(", ", v.Costumes.Select(c => c.Character.Name).Distinct()) + (wanted != null ? $" (not {wanted.Name})" : "");
                return v;
            }
            var songFiles = f.Contents.Where(Songs.IsSongFile).ToList();
            if (songFiles.Count > 0)
            {
                v.Kind = GbVerdictKind.Good;
                v.Text = (songFiles.Count == 1 ? "Song: " : $"{songFiles.Count} songs: ") + string.Join(", ", songFiles.Take(3).Select(Path.GetFileNameWithoutExtension)) + (songFiles.Count > 3 ? " ..." : "")
                    + ". After downloading you pick which P+ song each one replaces.";
                return v;
            }
            var stageFiles = f.Contents.Where(Stages.IsStageFile).ToList();
            if (stageFiles.Count > 0)
            {
                v.Kind = GbVerdictKind.Good;
                v.Text = "Stage file" + (stageFiles.Count > 1 ? "s" : "") + ": " + string.Join(", ", stageFiles.Take(3).Select(Path.GetFileName)) + (stageFiles.Count > 3 ? $" and {stageFiles.Count - 3} more" : "")
                    + ". After downloading, the app checks which P+ stage it plays exactly like.";
                if (f.Contents.Any(p => p.EndsWith(".rel", StringComparison.OrdinalIgnoreCase)))
                    v.Text += " It also has stage code (.rel), which the app leaves out; if the stage needs it, it won't match any P+ stage.";
                return v;
            }
            if (f.Contents.Any(p => Archives.IsArchive(p)))
            {
                v.Kind = GbVerdictKind.Maybe;
                v.Text = "Has more archives inside. The app will look in them after downloading.";
                return v;
            }
            if (f.Contents.Count <= 2 && f.Contents.Any(p => p.IndexOf("alternate", StringComparison.OrdinalIgnoreCase) >= 0) || otherSites != null && otherSites.Any() && f.Contents.Count <= 2)
            {
                v.Kind = GbVerdictKind.OtherSite;
                v.Text = "This file only points to another download site.";
                return v;
            }
            v.Kind = GbVerdictKind.NotASkin;
            v.Text = "No costume files (Fit….pac) inside. It may be a model or project files, not a ready-to-use skin.";
            return v;
        }

        // ----- downloading -----

        public static string DownloadFolder(string dataFolder, long modId) => Path.Combine(dataFolder, "Downloads", modId.ToString(CultureInfo.InvariantCulture));

        /// <summary>Downloads a file (or reuses an earlier download with the same checksum) and returns its path.</summary>
        public static string Download(GbFile f, string folder, Action<long, long> progress, CancellationToken cancel)
        {
            Directory.CreateDirectory(folder);
            string safe = Regex.Replace(f.Name ?? "download", @"[^\w\.\- ]", "_");
            string dest = Path.Combine(folder, f.Id + "-" + safe);
            if (File.Exists(dest) && (string.IsNullOrEmpty(f.Md5) || Md5Of(dest) == f.Md5.ToLowerInvariant())) return dest;
            string part = dest + ".part";
            try
            {
                var req = Request(f.DownloadUrl);
                req.Accept = "*/*";
                using (cancel.Register(() => { try { req.Abort(); } catch { } }))
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = File.Create(part))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : f.Size;
                    var buf = new byte[81920]; long done = 0; int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buf, 0, n); done += n;
                        progress?.Invoke(done, total);
                    }
                }
            }
            catch (WebException e) when (cancel.IsCancellationRequested) { TryDelete(part); throw new OperationCanceledException("Download cancelled.", e); }
            catch (WebException e) { TryDelete(part); throw new GbException("The download didn't finish. Check that this PC is online, then try again.", e); }
            catch { TryDelete(part); throw; }
            if (!string.IsNullOrEmpty(f.Md5) && Md5Of(part) != f.Md5.ToLowerInvariant())
            {
                TryDelete(part);
                throw new GbException("The downloaded file didn't match GameBanana's checksum (it may have been cut off). Please try again.");
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return dest;
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        static string Md5Of(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>A picture from GameBanana, kept in a cache folder so it only downloads once.</summary>
        public static byte[] Picture(string url, string cacheFolder)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string name;
            using (var sha = SHA1.Create()) name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "") + ".img";
            string cached = Path.Combine(cacheFolder, name);
            if (File.Exists(cached)) { try { return File.ReadAllBytes(cached); } catch { } }
            try
            {
                using (var resp = Request(url).GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    var bytes = ms.ToArray();
                    try { Directory.CreateDirectory(cacheFolder); File.WriteAllBytes(cached, bytes); } catch { }
                    return bytes;
                }
            }
            catch { return null; }
        }
    }

    /// <summary>A file downloaded from GameBanana, remembered so My Mods can list it under "Downloaded".</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class DownloadInfo
    {
        [System.Runtime.Serialization.DataMember] public long ModId;
        [System.Runtime.Serialization.DataMember] public long FileId;
        [System.Runtime.Serialization.DataMember] public string ModName;
        [System.Runtime.Serialization.DataMember] public string Author;
        [System.Runtime.Serialization.DataMember] public string Category;
        [System.Runtime.Serialization.DataMember] public string ThumbUrl;
        [System.Runtime.Serialization.DataMember] public string PageUrl;
        [System.Runtime.Serialization.DataMember] public string FileName;
        [System.Runtime.Serialization.DataMember] public string Path;
        [System.Runtime.Serialization.DataMember] public DateTime DownloadedAt;
        [System.Runtime.Serialization.DataMember] public int CostumeCount;   // filled in once the app has looked inside

        public SkinSource Source => new SkinSource { ModId = ModId, FileId = FileId, ModName = ModName, Author = Author, PageUrl = PageUrl, ThumbUrl = ThumbUrl };

        static readonly System.Runtime.Serialization.Json.DataContractJsonSerializer Ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(DownloadInfo));

        public void Save()
        {
            string p = System.IO.Path.ChangeExtension(Path, ".info.json");
            using (var f = File.Create(p)) Ser.WriteObject(f, this);
        }

        public static List<DownloadInfo> All(string dataFolder)
        {
            var list = new List<DownloadInfo>();
            string root = System.IO.Path.Combine(dataFolder, "Downloads");
            if (!Directory.Exists(root)) return list;
            foreach (var p in Directory.EnumerateFiles(root, "*.info.json", SearchOption.AllDirectories))
            {
                try
                {
                    DownloadInfo d;
                    using (var f = File.OpenRead(p)) d = (DownloadInfo)Ser.ReadObject(f);
                    if (d != null && File.Exists(d.Path)) list.Add(d);
                }
                catch { }
            }
            return list.OrderByDescending(d => d.DownloadedAt).ToList();
        }

        public void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
            try { File.Delete(System.IO.Path.ChangeExtension(Path, ".info.json")); } catch { }
        }
    }

    public sealed class GbException : Exception
    {
        public GbException(string message, Exception inner = null) : base(message, inner) { }
    }
}
