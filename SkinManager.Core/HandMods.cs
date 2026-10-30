// Mods put on the SD card by hand (the old way, not through this app): every file of P+ is compared with the list of
// P+'s own files Brawl Online keeps (each one's SHA-256), and files P+ doesn't have are noticed too. What this app put
// there itself isn't counted. Each one found is sorted out: a costume (which character and slot), a stage, music,
// menu pictures, or a file that changes how the game plays (those must match the other player's, or online games go
// out of sync). Hashes are kept between looks (by the file's size and where it sits on the card), so only files that
// changed are read again.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public sealed class HandMod
    {
        public string Path;
        public string Kind;            // "Costume", "Stage", "Music", "Menus & pictures", "Gameplay", "Other"
        public string Label;           // "Sonic, costume 3", "Battlefield", "FitSonic.pac (Sonic's moves)"
        public bool Added;             // P+ has no file there at all (a new costume, a new song...)
        public bool AffectsGameplay;   // must match the other player's
        public Character Character; public int SlotIndex = -1;
        public long Bytes;
    }

    public sealed class HandScan
    {
        public string Version;
        public DateTime When;
        public List<HandMod> Mods = new List<HandMod>();
        public HandMod At(string path) => Mods.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
        public HandMod Costume(Character c, int slotIndex) => Mods.FirstOrDefault(m => m.Kind == "Costume" && m.Character?.SlotListId == c.SlotListId && m.SlotIndex == slotIndex);
    }

    public static class HandMods
    {
        /// <summary>The latest look at the card in use (null until the first one is done).</summary>
        public static HandScan Last;

        [DataContract] sealed class Known { [DataMember] public string Path, Sha; [DataMember] public long Size; [DataMember] public uint Cluster; }

        /// <summary>Looks through the card. libraries: the app's own mods lists (their files aren't hand-installed).</summary>
        public static HandScan Scan(Fat32Image card, PPlusRelease release, IEnumerable<Library> libraries, string cacheFile, Action<int> progress = null)
        {
            var scan = new HandScan { Version = release?.Version, When = DateTime.Now };
            if (release == null) return scan;
            var libs = libraries.Where(l => l != null).ToList();
            var ours = new HashSet<string>(libs.SelectMany(l => l.Skins.SelectMany(k => k.Files.Select(f => f.CardPath))
                .Concat(l.Skins.SelectMany(k => k.Pictures.Select(p => p.CardPath)))
                .Concat(l.Stages.Select(k => k.CardPath)).Concat(l.Written.Keys)), StringComparer.OrdinalIgnoreCase);
            bool songs = libs.Any(l => l.Stages.Any(s => s.IsSong));
            // what's been hashed before
            var known = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(cacheFile)) using (var s = File.OpenRead(cacheFile)) foreach (var k in (List<Known>)new DataContractJsonSerializer(typeof(List<Known>)).ReadObject(s)) known[k.Path] = k; } catch { }
            var seen = new List<Known>();
            // every file under /Project+ (the codes at its top, and everything in pf)
            var files = new List<(string Path, Fat32Image.Entry Entry)>();
            void Walk(string folder, bool deep)
            {
                foreach (var e in card.ListFolder(folder + "/"))
                {
                    if (e.Name == "." || e.Name == "..") continue;
                    string p = folder + "/" + e.Name;
                    if (e.IsDirectory) { if (deep) Walk(p, true); }
                    else files.Add((p, e));
                }
            }
            Walk(Build.Root, false);
            Walk(Build.Pf, true);
            var fighters = Characters.All.ToDictionary(c => c.FighterFolderPath, c => c, StringComparer.OrdinalIgnoreCase);
            var slotCache = new Dictionary<int, List<CostumeSlot>>();
            int i = 0;
            foreach (var (path, entry) in files)
            {
                if (++i % 50 == 0) progress?.Invoke(i * 100 / Math.Max(1, files.Count));
                if (path.EndsWith("/plugins/PPOnline.rel", StringComparison.OrdinalIgnoreCase)) continue;   // Brawl Online's own
                if (ours.Contains(path)) continue;
                if (songs && path.EndsWith(".tlst", StringComparison.OrdinalIgnoreCase)) continue;      // the app rewrites track lists for its songs
                bool inPPlus = release.Files.TryGetValue(path, out var sha);
                if (inPPlus)
                {
                    if (release.Sizes.TryGetValue(path, out var size) && size != entry.Size) { }   // a different size: changed
                    else
                    {
                        string have;
                        if (known.TryGetValue(path, out var k) && k.Size == entry.Size && k.Cluster == entry.FirstCluster) have = k.Sha;
                        else { try { have = Library.Sha256(card.ReadFile(path)); } catch { continue; } }
                        seen.Add(new Known { Path = path, Sha = have, Size = entry.Size, Cluster = entry.FirstCluster });
                        if (have == sha) continue;   // P+'s own
                    }
                }
                scan.Mods.Add(Describe(path, entry.Size, !inPPlus, fighters, card, slotCache));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                using (var s = File.Create(cacheFile)) new DataContractJsonSerializer(typeof(List<Known>)).WriteObject(s, seen);
            }
            catch { }
            progress?.Invoke(100);
            scan.Mods = scan.Mods.OrderBy(m => Order(m.Kind)).ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase).ToList();
            return scan;
        }

        static int Order(string kind) => Array.IndexOf(new[] { "Gameplay", "Costume", "Stage", "Music", "Menus & pictures", "Other" }, kind);

        static HandMod Describe(string path, long bytes, bool added, Dictionary<string, Character> fighters, Fat32Image card, Dictionary<int, List<CostumeSlot>> slotCache)
        {
            var m = new HandMod { Path = path, Added = added, Bytes = bytes };
            string name = System.IO.Path.GetFileName(path), folder = path.Substring(0, path.Length - name.Length - 1);
            string lower = path.ToLowerInvariant();
            if (fighters.TryGetValue(folder, out var c))
            {
                m.Character = c;
                var costume = Regex.Match(name, $"^Fit{Regex.Escape(c.FilePrefix)}(\\d\\d)\\.pac$", RegexOptions.IgnoreCase);
                if (costume.Success)
                {
                    m.Kind = "Costume";
                    int number = int.Parse(costume.Groups[1].Value);
                    try
                    {
                        if (!slotCache.TryGetValue(c.SlotListId, out var slots)) slotCache[c.SlotListId] = slots = PPlusCard.ReadSlots(card, c);
                        var slot = slots.FirstOrDefault(s => s.FileNumber == number);
                        m.SlotIndex = slot?.Index ?? -1;
                    }
                    catch { }
                    m.Label = m.SlotIndex >= 0 ? $"{c.Name}, costume {m.SlotIndex + 1}" : $"{c.Name}, costume file {number:00} (not on the select screen)";
                    return m;
                }
                if (Regex.IsMatch(name, $"^Fit{Regex.Escape(c.FilePrefix)}\\d\\d(Kirby|Spy|Final|Dark|Fake).*\\.pac$", RegexOptions.IgnoreCase) || Regex.IsMatch(name, "\\d\\d\\.pac$"))
                { m.Kind = "Costume"; m.Label = $"{c.Name}: {name} (part of a costume)"; return m; }
                m.Kind = "Gameplay"; m.AffectsGameplay = true;
                m.Label = name.Equals($"Fit{c.FilePrefix}.pac", StringComparison.OrdinalIgnoreCase) ? $"{c.Name}'s moves ({name})"
                        : name.IndexOf("Motion", StringComparison.OrdinalIgnoreCase) >= 0 ? $"{c.Name}'s animations ({name})" : $"{c.Name}: {name}";
                return m;
            }
            if (lower.Contains("/pf/stage/"))
            {
                m.Kind = "Stage"; m.AffectsGameplay = !lower.Contains("/stage/stageslot/") && !lower.Contains("/stage/stagelist/") && lower.EndsWith(".pac");
                m.Label = lower.EndsWith(".pac") ? Stages.Describe(path).Label ?? name : name;
                return m;
            }
            if (lower.Contains("/pf/sound/")) { m.Kind = "Music"; m.Label = System.IO.Path.GetFileNameWithoutExtension(name); return m; }
            if (lower.Contains("/pf/menu") || lower.Contains("/pf/info") || lower.Contains("/pf/system/common") || lower.Contains("/pf/toy/") || lower.Contains("/pf/movie/"))
            { m.Kind = "Menus & pictures"; m.Label = name; return m; }
            if (lower.EndsWith(".gct") || lower.Contains("/pf/module/") || lower.Contains("/pf/item/") || lower.Contains("/pf/fighter/") || lower.EndsWith(".rel") || lower.Contains("/pf/system/"))
            { m.Kind = "Gameplay"; m.AffectsGameplay = true; m.Label = name.EndsWith(".GCT", StringComparison.OrdinalIgnoreCase) ? $"Game codes ({name})" : name; return m; }
            m.Kind = "Other"; m.Label = name;
            return m;
        }
    }
}
