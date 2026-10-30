// What a card that isn't plain P+ looked like (a build made from P+, like Cobalt Smash; or any card when Brawl Online
// isn't here with P+'s list of files): a snapshot of every file in the build's folder, taken the first time the app
// looks at the card. "Mods put on by hand" then means what was added or changed since. It's kept with the card's own
// mods list (Data\Other cards\...), and can be taken again ("start over from now").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SkinManager.Core
{
    public static class CardBaseline
    {
        public const string Prefix = "snapshot:";
        [DataContract] sealed class Item { [DataMember] public string Path, Sha; [DataMember] public long Size; }
        [DataContract] sealed class Snapshot { [DataMember] public DateTime Taken; [DataMember] public string Root; [DataMember] public List<Item> Files = new List<Item>(); }

        static string FileIn(string folder) => System.IO.Path.Combine(folder, "card-snapshot.json");

        /// <summary>When the snapshot was taken (null: none yet).</summary>
        public static DateTime? Taken(string folder)
        {
            var s = Read(folder);
            return s?.Taken;
        }

        static Snapshot Read(string folder)
        {
            try { var f = FileIn(folder); if (!File.Exists(f)) return null; using (var st = File.OpenRead(f)) return (Snapshot)new DataContractJsonSerializer(typeof(Snapshot)).ReadObject(st); }
            catch { return null; }
        }

        static PPlusRelease AsRelease(Snapshot s)
        {
            var r = new PPlusRelease { Version = Prefix + s.Taken.ToString("o") };
            foreach (var i in s.Files) { r.Files[i.Path] = i.Sha; r.Sizes[i.Path] = i.Size; }
            return r;
        }

        /// <summary>Takes the snapshot now: every file in the build's folder, except the app's own mods.</summary>
        public static PPlusRelease Make(Fat32Image card, string folder, IEnumerable<Library> libraries, Action<int> progress = null)
        {
            var ours = new HashSet<string>(libraries.Where(l => l != null).SelectMany(l => l.Skins.SelectMany(k => k.Files.Select(f => f.CardPath))
                .Concat(l.Skins.SelectMany(k => k.Pictures.Select(p => p.CardPath))).Concat(l.Stages.Select(k => k.CardPath)).Concat(l.Written.Keys)), StringComparer.OrdinalIgnoreCase);
            var files = new List<(string Path, Fat32Image.Entry Entry)>();
            void Walk(string f, bool deep)
            {
                foreach (var e in card.ListFolder(f + "/"))
                {
                    if (e.Name == "." || e.Name == "..") continue;
                    string p = f + "/" + e.Name;
                    if (e.IsDirectory) { if (deep) Walk(p, true); } else files.Add((p, e));
                }
            }
            Walk(Build.Root, false); Walk(Build.Pf, true);
            var s = new Snapshot { Taken = DateTime.Now, Root = Build.Root };
            int i = 0;
            foreach (var (path, entry) in files)
            {
                if (++i % 50 == 0) progress?.Invoke(i * 100 / Math.Max(1, files.Count));
                if (ours.Contains(path)) continue;
                try { s.Files.Add(new Item { Path = path, Size = entry.Size, Sha = Library.Sha256(card.ReadFile(path)) }); } catch { }
            }
            try
            {
                Directory.CreateDirectory(folder);
                using (var st = File.Create(FileIn(folder))) new DataContractJsonSerializer(typeof(Snapshot)).WriteObject(st, s);
            }
            catch { }
            return AsRelease(s);
        }

        /// <summary>What to compare the card with: P+'s own list of files when it's Brawl Online's card (or plain P+ of
        /// the version Brawl Online has), else the card's snapshot (taken now if there's none).</summary>
        public static PPlusRelease For(Fat32Image card, BrawlOnlineInstall install, string folder, IEnumerable<Library> libraries)
        {
            PPlusRelease release = null;
            try { release = Versions.Release(install, Versions.Read(install).PPlus); } catch { }
            if (install.OwnCard) return release;
            if (release != null && Build.IsPPlusFolder && Versions.IsOn(card, release)) return release;
            var s = Read(folder);
            if (s != null && string.Equals(s.Root, Build.Root, StringComparison.OrdinalIgnoreCase)) return AsRelease(s);
            return Make(card, folder, libraries);
        }
    }
}
