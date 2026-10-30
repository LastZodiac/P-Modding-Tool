// Two SD cards: the normal one (2 GB, as Brawl Online made it; use it for rooms, where players get each other's
// card in chunks of the file) and a big one (more room for mods; for Unranked and Ranked, where each player plays
// their own card). Brawl Online always plays User\Wii\sd.raw; the other card waits next to it as
// sd.raw.parked-<name>, and switching renames the two. Each card has its own mods list (the big one's lives in
// Data\Cards\big).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class Cards
    {
        public const string Normal = "normal", Big = "big", Clean = "clean";   // clean: P+ exactly as it comes, no mods (for "Launch unmodded")
        public const long BigSize = 4096L * 1024 * 1024;

        readonly BrawlOnlineInstall _install;
        readonly string _data;

        public Cards(BrawlOnlineInstall install, string dataFolder)
        {
            _install = install; _data = dataFolder;
            Active = Normal; LastModded = Normal;
            try
            {
                if (File.Exists(StateFile))
                {
                    var t = File.ReadAllText(StateFile);
                    var a = System.Text.RegularExpressions.Regex.Match(t, "\"active\":\"(\\w+)\"");
                    var m = System.Text.RegularExpressions.Regex.Match(t, "\"modded\":\"(\\w+)\"");
                    if (a.Success && (a.Groups[1].Value == Big || a.Groups[1].Value == Clean)) Active = a.Groups[1].Value;
                    if (m.Success && m.Groups[1].Value == Big) LastModded = Big;
                }
            }
            catch { }
            // trust the files over the note: the card in place is the one that isn't parked
            if (File.Exists(Parked(Active)))
                Active = new[] { Normal, Big, Clean }.FirstOrDefault(n => !File.Exists(Parked(n)) && (n == Normal || Directory.Exists(LibraryFolder(n)))) ?? Normal;
            if (Active != Clean) LastModded = Active;
        }

        /// <summary>The modded card last in place (where "Launch modded" goes back to from the clean card).</summary>
        public string LastModded { get; private set; }
        public bool HasClean => Active == Clean || File.Exists(Parked(Clean));

        public string Active { get; private set; }
        public bool HasBig => Active == Big || File.Exists(Parked(Big));
        string Wii => Path.GetDirectoryName(_install.CardPath);
        public string Parked(string name) => Path.Combine(Wii, "sd.raw.parked-" + name);
        string StateFile => Path.Combine(_data, "cards.json");
        public string LibraryFolder(string name) => name == Normal ? _data : Path.Combine(_data, "Cards", name);
        public static string Label(string name) => name == Big ? "Big card (Unranked and Ranked)" : name == Clean ? "Clean card (no mods)" : "Normal card (rooms)";

        void Save() { try { File.WriteAllText(StateFile, "{\"active\":\"" + Active + "\",\"modded\":\"" + LastModded + "\"}"); } catch { } }

        static readonly string[] LibraryParts = { "installed.json", "Originals", "Skins", "StageMods", "stage-index-4.json" };

        /// <summary>Makes the big card: a bigger copy of the normal card (mods included), with a copy of its mods list.</summary>
        public void MakeBig(Action<string, int> progress)
        {
            if (Active != Normal) throw new InvalidOperationException("Switch to the normal card first.");
            if (HasBig) throw new InvalidOperationException("There's a big card already.");
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_install.CardPath)));
            // the big card, plus Brawl Online's copy of it and the app's safety copies when it changes
            long need = BigSize * 3 + 256L * 1024 * 1024;
            if (drive.AvailableFreeSpace < need && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_SKIP_SPACE") != "1")
                throw new IOException($"Drive {drive.Name} needs about {need / (1024L * 1024 * 1024) + 1} GB free for the big card (the card itself, Brawl Online's copy of it, and a safety copy while mods are saved). It has {drive.AvailableFreeSpace / (1024L * 1024 * 1024)} GB.");
            CardTools.Grow(_install.CardPath, Parked(Big), BigSize, p => progress?.Invoke("Making the big card", p));
            // its mods list starts as the normal card's (the same mods are on it)
            string to = LibraryFolder(Big);
            if (Directory.Exists(to)) Directory.Delete(to, true);
            Directory.CreateDirectory(to);
            foreach (var part in LibraryParts)
            {
                string src = Path.Combine(_data, part);
                if (File.Exists(src)) File.Copy(src, Path.Combine(to, part));
                else if (Directory.Exists(src)) CopyFolder(src, Path.Combine(to, part));
            }
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
        }

        /// <summary>Puts the other card in place. Brawl Online must be closed.</summary>
        public void Switch(string to)
        {
            if (to == Active) return;
            if (!File.Exists(Parked(to))) throw new FileNotFoundException("That card isn't on this PC.");
            string from = Active;
            File.Move(_install.CardPath, Parked(from));
            try { File.Move(Parked(to), _install.CardPath); }
            catch { File.Move(Parked(from), _install.CardPath); throw; }
            LastSave.RemoveOldBackup(_install.CardPath);   // (older versions kept a safety copy here)
            Active = to;
            if (to != Clean) LastModded = to;
            Save();
        }

        public void DeleteBig()
        {
            if (Active == Big) throw new InvalidOperationException("Switch to the normal card first.");
            if (File.Exists(Parked(Big))) File.Delete(Parked(Big));
            string lib = LibraryFolder(Big);
            if (Directory.Exists(lib)) Directory.Delete(lib, true);
        }

        /// <summary>Brings the parked card to the P+ release the card in place is on (it may have missed P+ updates
        /// while it was parked). P+'s files come from the card in place; mods of the parked card whose files P+ changed are
        /// replaced by P+'s new files, so the app re-checks them like after any P+ update. Done on a copy, then swapped in.</summary>
        public List<string> CatchUp(string parkedName, Library parkedLib, PPlusRelease release, Action<string, int> progress)
        {
            var notes = new List<string>();
            string parked = Parked(parkedName), work = parked + ".catchup";
            using (var current = new Fat32Image(_install.CardPath, false))
            {
                if (!Versions.IsOn(current, release)) throw new InvalidOperationException("The card in place isn't on the newest P+ either. Open Brawl Online once so it updates P+, then switch.");
                using (var check = new Fat32Image(parked, false)) if (Versions.IsOn(check, release)) return notes;   // already up to date
                progress?.Invoke("Copying the card", 0);
                File.Copy(parked, work, true);
                try
                {
                    var ours = new HashSet<string>(parkedLib.Skins.SelectMany(s => s.Files.Select(f => f.CardPath))
                        .Concat(parkedLib.Stages.Select(s => s.CardPath)).Concat(parkedLib.Written.Keys), StringComparer.OrdinalIgnoreCase);
                    using (var card = new Fat32Image(work, true))
                    {
                        int i = 0, written = 0, removed = 0;
                        foreach (var kv in release.Files)
                        {
                            progress?.Invoke("Bringing it up to date", 100 * i++ / Math.Max(1, release.Files.Count));
                            if (card.Exists(kv.Key) && Library.Sha256(card.ReadFile(kv.Key)) == kv.Value) continue;
                            if (ours.Contains(kv.Key))
                            {
                                // a mod of the parked card: left alone unless P+ itself changed the file under it
                                var o = parkedLib.OriginalFile(kv.Key);
                                if (!parkedLib.HasOriginalFile(kv.Key) || (o != null && Library.Sha256(o) == kv.Value)) continue;
                            }
                            if (!current.Exists(kv.Key)) continue;
                            var data = current.ReadFile(kv.Key);
                            if (Library.Sha256(data) != kv.Value)
                            {
                                // the card in place has a mod there: P+'s file is the one its mods list saved
                                var orig = CurrentOriginal(kv.Key);
                                if (orig == null || Library.Sha256(orig) != kv.Value) { notes.Add("Couldn't get P+'s " + kv.Key); continue; }
                                data = orig;
                            }
                            card.WriteFile(kv.Key, data); written++;
                        }
                        // files P+ took out (an old plugin, say) go too, unless they're ours
                        foreach (var path in AllFiles(card, Build.Pf).Where(p => !release.Files.ContainsKey(p) && !ours.Contains(p) && !current.Exists(p)).ToList())
                            if (card.DeleteFile(path)) removed++;
                        notes.Insert(0, $"The {Label(parkedName).ToLowerInvariant()} missed a P+ update while it was put away: {written} of P+'s files were brought up to date" + (removed > 0 ? $" and {removed} old ones removed." : "."));
                    }
                    using (var done = new Fat32Image(work, false))
                        if (!Versions.IsOn(done, release)) throw new InvalidOperationException("The card didn't come out on the newest P+.");
                    File.Delete(parked);
                    File.Move(work, parked);
                }
                catch { try { File.Delete(work); } catch { } throw; }
            }
            return notes;
        }

        /// <summary>Makes the clean card: a copy of the normal card with P+'s own file at every path of the P+ release
        /// (from the card in place, or the mods lists' saved originals where a mod sits) and the files mods added taken out.
        /// It counts as clean only when every file of the release reads back exactly as P+ ships it.</summary>
        public void MakeClean(PPlusRelease release, IEnumerable<Library> libraries, Action<string, int> progress)
        {
            if (release == null) throw new InvalidOperationException("The app couldn't find the list of P+'s files for this P+ version. Open Brawl Online once, then try again.");
            if (HasClean) return;
            string source = Active == Normal ? _install.CardPath : Parked(Normal);
            if (!File.Exists(source)) source = _install.CardPath;
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(source)));
            long need = new FileInfo(source).Length + 256L * 1024 * 1024;
            if (drive.AvailableFreeSpace < need && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_SKIP_SPACE") != "1")
                throw new IOException($"Drive {drive.Name} needs about {need / (1024 * 1024)} MB free for the clean card. It has {drive.AvailableFreeSpace / (1024 * 1024)} MB.");
            var libs = libraries.ToList();
            var ours = new HashSet<string>(libs.SelectMany(l => l.Skins.SelectMany(k => k.Files.Select(f => f.CardPath)).Concat(l.Stages.Select(k => k.CardPath)).Concat(l.Written.Keys)), StringComparer.OrdinalIgnoreCase);
            string work = Parked(Clean) + ".making";
            try
            {
                using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                using (var dst = new FileStream(work, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var buf = new byte[4 << 20]; long done = 0; int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); done += n; progress?.Invoke("Copying the normal card", (int)(done * 60 / Math.Max(1, src.Length))); }
                }
                // P+'s files can come from any of your cards (the one in place, the parked ones), whichever has P+'s version
                var others = new[] { Normal, Big }.Where(n => n != Active && File.Exists(Parked(n))).Select(n => new Fat32Image(Parked(n), false)).ToList();
                try
                {
                using (var current = new Fat32Image(_install.CardPath, false))
                using (var card = new Fat32Image(work, true))
                {
                    int i = 0;
                    foreach (var kv in release.Files)
                    {
                        progress?.Invoke("Putting P+'s own files back", 60 + 35 * i++ / Math.Max(1, release.Files.Count));
                        if (card.Exists(kv.Key) && Library.Sha256(card.ReadFile(kv.Key)) == kv.Value) continue;
                        byte[] data = current.Exists(kv.Key) ? current.ReadFile(kv.Key) : null;
                        if (data == null || Library.Sha256(data) != kv.Value)
                            data = others.Where(o => o.Exists(kv.Key)).Select(o => o.ReadFile(kv.Key)).FirstOrDefault(d => Library.Sha256(d) == kv.Value);
                        if (data == null)
                            data = libs.Select(l => l.HasOriginalFile(kv.Key) ? l.OriginalFile(kv.Key) : null).FirstOrDefault(o => o != null && Library.Sha256(o) == kv.Value);
                        if (data == null) throw new InvalidOperationException("P+'s own " + kv.Key + " wasn't found on your cards or in the app's saved originals.");
                        card.WriteFile(kv.Key, data);
                    }
                    // files that mods added (new songs, extra pictures) go
                    foreach (var path in ours.Where(p => !release.Files.ContainsKey(p)).ToList())
                        if (card.Exists(path)) card.DeleteFile(path);
                }
                }
                finally { foreach (var o in others) o.Dispose(); }
                progress?.Invoke("Checking every P+ file", 97);
                using (var check = new Fat32Image(work, false))
                {
                    var wrong = release.Files.Where(kv => !check.Exists(kv.Key) || Library.Sha256(check.ReadFile(kv.Key)) != kv.Value).Select(kv => kv.Key).ToList();
                    if (wrong.Count > 0) throw new InvalidOperationException($"The clean card didn't come out exactly like P+ ({wrong.Count} file(s) differ, like {wrong[0]}). It wasn't kept.");
                }
                File.Move(work, Parked(Clean));
                Directory.CreateDirectory(LibraryFolder(Clean));
                new Library(LibraryFolder(Clean)).Save();   // no mods on it
            }
            catch { try { if (File.Exists(work)) File.Delete(work); } catch { } throw; }
        }

        public void DeleteClean()
        {
            if (Active == Clean) throw new InvalidOperationException("Switch to another card first.");
            if (File.Exists(Parked(Clean))) File.Delete(Parked(Clean));
            if (Directory.Exists(LibraryFolder(Clean))) Directory.Delete(LibraryFolder(Clean), true);
        }

        Func<string, byte[]> _currentOriginal;
        /// <summary>Where to find P+'s file for a path the card in place has a mod at (its mods list's saved original).</summary>
        public void UseOriginals(Func<string, byte[]> original) => _currentOriginal = original;
        byte[] CurrentOriginal(string path) => _currentOriginal?.Invoke(path);

        static IEnumerable<string> AllFiles(Fat32Image c, string path)
        {
            foreach (var e in c.ListFolder(path))
            {
                if (e.Name == "." || e.Name == "..") continue;
                string p = path + "/" + e.Name;
                if (e.IsDirectory) foreach (var x in AllFiles(c, p)) yield return x;
                else yield return p;
            }
        }
    }
}
