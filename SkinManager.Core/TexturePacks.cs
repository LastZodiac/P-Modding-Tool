// Dolphin texture packs: pictures named like tex1_64x64_1a2b3c4d5e6f7a8b_5.png that Dolphin shows instead of the game's
// own textures when "Load Custom Textures" is on. They only change what this PC draws (nothing is sent to the other
// player, and the game itself doesn't change), so they can't make online games go out of sync.
// Packs that are on live where Brawl Online's Dolphin looks for them (netplay\User\Load\Textures\RSBE01\<pack>);
// packs that are off are kept in the app's Data folder, so they don't take room on Brawl Online's drive.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class TexturePack
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Folder;          // its folder name under RSBE01
        [DataMember] public int Pictures;
        [DataMember] public long Bytes;
        [DataMember] public bool Enabled;
        [DataMember] public DateTime InstalledAt;
        [DataMember(EmitDefaultValue = false)] public long GameBananaModId;
        [DataMember(EmitDefaultValue = false)] public long GameBananaFileId;
        [DataMember(EmitDefaultValue = false)] public string Author;
        [DataMember(EmitDefaultValue = false)] public string PageUrl;
        [DataMember(EmitDefaultValue = false)] public string ThumbUrl;
        [DataMember(EmitDefaultValue = false)] public string SourcePath;
    }

    public sealed class TexturePacks
    {
        public const string GameId = "RSBE01";
        static readonly Regex TextureName = new Regex(@"^tex1_\d+x\d+_.*\.(png|dds)$", RegexOptions.IgnoreCase);
        public static bool IsTexture(string path) => TextureName.IsMatch(Path.GetFileName(path));

        readonly BrawlOnlineInstall _install;
        readonly string _data;
        public List<TexturePack> All { get; private set; } = new List<TexturePack>();

        public TexturePacks(BrawlOnlineInstall install, string dataFolder)
        {
            _install = install; _data = dataFolder;
            try
            {
                if (File.Exists(ListFile))
                    using (var s = File.OpenRead(ListFile)) All = (List<TexturePack>)new DataContractJsonSerializer(typeof(List<TexturePack>)).ReadObject(s) ?? new List<TexturePack>();
            }
            catch { All = new List<TexturePack>(); }
        }

        string ListFile => Path.Combine(_data, "textures.json");
        string UserFolder => Path.Combine(_install.NetplayFolder, "User");
        public string GameFolder => Path.Combine(UserFolder, "Load", "Textures", GameId);
        string GfxIni => Path.Combine(UserFolder, "Config", "GFX.ini");
        public string OnFolder(TexturePack p) => Path.Combine(GameFolder, p.Folder);
        public string OffFolder(TexturePack p) => Path.Combine(_data, "TexturePacks", p.Id);
        public string FolderOf(TexturePack p) => p.Enabled ? OnFolder(p) : OffFolder(p);

        void Save()
        {
            Directory.CreateDirectory(_data);
            string tmp = ListFile + ".tmp";
            using (var s = File.Create(tmp)) new DataContractJsonSerializer(typeof(List<TexturePack>)).WriteObject(s, All);
            if (File.Exists(ListFile)) File.Delete(ListFile);
            File.Move(tmp, ListFile);
        }

        /// <summary>The texture pictures in a folder (and its subfolders).</summary>
        public static List<string> Find(string folder) =>
            Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(IsTexture).ToList() : new List<string>();

        /// <summary>Whether Dolphin's "Load Custom Textures" is on for Brawl Online.</summary>
        public bool HiresOn()
        {
            try
            {
                string section = null;
                foreach (var raw in File.ReadAllLines(GfxIni))
                {
                    string l = raw.Trim();
                    var m = Regex.Match(l, @"^\[(.*)\]$");
                    if (m.Success) { section = m.Groups[1].Value; continue; }
                    var v = Regex.Match(l, @"^HiresTextures\s*=\s*(\S+)", RegexOptions.IgnoreCase);
                    if (section == "Settings" && v.Success) return !v.Groups[1].Value.Equals("False", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        /// <summary>Turns on Dolphin's "Load Custom Textures" (Graphics > Advanced) for Brawl Online's Dolphin.</summary>
        public void TurnOnHires()
        {
            if (HiresOn()) return;
            var lines = File.Exists(GfxIni) ? File.ReadAllLines(GfxIni).ToList() : new List<string>();
            int settings = lines.FindIndex(l => l.Trim() == "[Settings]");
            int existing = -1;
            if (settings >= 0)
                for (int i = settings + 1; i < lines.Count && !lines[i].Trim().StartsWith("["); i++)
                    if (Regex.IsMatch(lines[i].Trim(), @"^HiresTextures\s*=", RegexOptions.IgnoreCase)) existing = i;
            if (existing >= 0) lines[existing] = "HiresTextures = True";
            else if (settings >= 0) lines.Insert(settings + 1, "HiresTextures = True");
            else { lines.Add("[Settings]"); lines.Add("HiresTextures = True"); }
            Directory.CreateDirectory(Path.GetDirectoryName(GfxIni));
            File.WriteAllText(GfxIni, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        }

        void NotRunning()
        {
            if (_install.IsRunning(out string what))
                throw new InvalidOperationException($"Close {what} first. Dolphin reads its texture settings when it starts and saves them when it closes.");
        }

        void Room(long bytes)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(UserFolder)));
            long need = bytes + 1024L * 1024 * 1024;   // and a 1 GB cushion for Brawl Online
            if (drive.AvailableFreeSpace < need && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_SKIP_SPACE") != "1")
                throw new IOException($"Drive {drive.Name} needs {need / (1024 * 1024)} MB free for this texture pack (it has {drive.AvailableFreeSpace / (1024 * 1024)} MB). Turn off another pack or free some room.");
        }

        /// <summary>Installs the textures found in an unpacked download, turned on. Returns the new pack.</summary>
        public TexturePack Add(string folder, string name, SkinSource source, string sourcePath, Action<string, int> progress)
        {
            NotRunning();
            var files = Find(folder);
            if (files.Count == 0) throw new InvalidOperationException("No Dolphin texture pictures (named like tex1_64x64_...png) were found.");
            // keep the pack's own folders below its game folder (RSBE01, or the folder that holds every picture)
            string root = CommonRoot(files.Select(Path.GetDirectoryName).ToList());
            var game = files.Select(f => FindUp(Path.GetDirectoryName(f), folder)).FirstOrDefault(x => x != null);
            if (game != null) root = game;
            long bytes = files.Sum(f => new FileInfo(f).Length);
            Room(bytes);
            var pack = new TexturePack
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, Pictures = files.Count, Bytes = bytes, Enabled = true, InstalledAt = DateTime.Now,
                GameBananaModId = source?.ModId ?? 0, GameBananaFileId = source?.FileId ?? 0, Author = source?.Author, PageUrl = source?.PageUrl, ThumbUrl = source?.ThumbUrl, SourcePath = sourcePath,
            };
            pack.Folder = UniqueFolder(Safe(name));
            string dest = OnFolder(pack);
            try
            {
                int i = 0;
                foreach (var f in files)
                {
                    string rel = f.Substring(root.Length).TrimStart('\\', '/');
                    if (rel.StartsWith(GameId + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(GameId.Length + 1);
                    string to = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(f, to, true);
                    if (++i % 50 == 0) progress?.Invoke("Copying the textures", 100 * i / files.Count);
                }
                TurnOnHires();
            }
            catch { try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { } throw; }
            All.Add(pack);
            Save();
            return pack;
        }

        /// <summary>Turns a pack on (moves it where Dolphin looks) or off (moves it to the app's folder).</summary>
        public void SetOn(TexturePack pack, bool on, Action<string, int> progress)
        {
            if (pack.Enabled == on) return;
            NotRunning();
            string from = FolderOf(pack), to = on ? OnFolder(pack) : OffFolder(pack);
            if (on) { if (Directory.Exists(to)) pack.Folder = UniqueFolder(pack.Folder); to = OnFolder(pack); Room(pack.Bytes); }
            if (Directory.Exists(to)) Directory.Delete(to, true);
            MoveFolder(from, to, p => progress?.Invoke(on ? "Turning the textures on" : "Turning the textures off", p));
            pack.Enabled = on;
            if (on) TurnOnHires();
            Save();
        }

        public void Delete(TexturePack pack)
        {
            if (pack.Enabled) NotRunning();
            string f = FolderOf(pack);
            if (Directory.Exists(f)) Directory.Delete(f, true);
            All.Remove(pack);
            Save();
        }

        string UniqueFolder(string baseName)
        {
            string n = baseName; int k = 2;
            while (Directory.Exists(Path.Combine(GameFolder, n)) || All.Any(p => p.Enabled && string.Equals(p.Folder, n, StringComparison.OrdinalIgnoreCase))) n = baseName + " " + k++;
            return n;
        }

        static string Safe(string name)
        {
            string s = Regex.Replace(name ?? "Texture pack", @"[^\w\-\. ]", "_").Trim(' ', '.');
            return s.Length == 0 ? "Texture pack" : s.Length > 60 ? s.Substring(0, 60) : s;
        }

        static string FindUp(string dir, string stop)
        {
            for (var d = dir; d != null && d.Length >= stop.Length; d = Path.GetDirectoryName(d))
                if (Path.GetFileName(d).Equals(GameId, StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(d);
            return null;
        }

        static string CommonRoot(List<string> dirs)
        {
            string root = dirs[0];
            foreach (var d in dirs)
                while (!(d + Path.DirectorySeparatorChar).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root);
            return root;
        }

        static void MoveFolder(string from, string to, Action<int> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(from, to);
                return;
            }
            var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string target = Path.Combine(to, files[i].Substring(from.Length).TrimStart('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(files[i], target, true);
                    if (i % 50 == 0) progress?.Invoke(100 * i / Math.Max(1, files.Length));
                }
            }
            catch { try { Directory.Delete(to, true); } catch { } throw; }
            Directory.Delete(from, true);
        }
    }
}
