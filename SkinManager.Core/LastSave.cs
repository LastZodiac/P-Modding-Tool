// "Undo last save": the SD card as it was before the last save, plus the mods list as it was, kept once in the
// app's Data folder (next to the app, not on the drive Brawl Online is on). Only one is ever kept: each save
// replaces it. Undo is allowed only while the card is still exactly what that save left (same card, same size,
// same date): if Brawl Online updated P+ since, putting the old card back would undo P+'s update too.
using System;
using System.IO;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class LastSave
    {
        readonly string _folder;
        public LastSave(string dataFolder) { _folder = Path.Combine(dataFolder, "LastSave"); }

        string Staging => _folder + ".new";
        string CardCopy => Path.Combine(_folder, "sd.raw");
        string InfoFile => Path.Combine(_folder, "info.txt");
        static readonly string[] Parts = { "installed.json", "Originals", "Skins", "StageMods" };

        public sealed class Info
        {
            public string Card, What;
            public DateTime SavedAt, CardTime;
            public long CardSize;
        }

        public Info Read()
        {
            try
            {
                if (!File.Exists(CardCopy) || !File.Exists(InfoFile)) return null;
                var l = File.ReadAllLines(InfoFile);
                return new Info
                {
                    Card = l[0], SavedAt = new DateTime(long.Parse(l[1])), CardSize = long.Parse(l[2]),
                    CardTime = new DateTime(long.Parse(l[3]), DateTimeKind.Utc), What = l.Length > 4 ? l[4] : "",
                };
            }
            catch { return null; }
        }

        /// <summary>Why undo isn't possible now, or null when it is.</summary>
        public string CantUndo(string activeCard, string cardPath)
        {
            var i = Read();
            if (i == null) return "There's no earlier save to go back to.";
            if (i.Card != activeCard) return "The last save was on the " + Cards.Label(i.Card).ToLowerInvariant() + ". Switch to it first.";
            var f = new FileInfo(cardPath);
            if (!f.Exists || f.Length != i.CardSize || f.LastWriteTimeUtc != i.CardTime)
                return "The SD card has changed since the last save (most likely a P+ update), so going back would undo that too.";
            return null;
        }

        /// <summary>Step 1, before a save changes anything: copies the mods list as it is now.</summary>
        public void Begin(Library lib)
        {
            Clear(Staging);
            Directory.CreateDirectory(Staging);
            foreach (var part in Parts) CopyPart(Path.Combine(lib.Folder, part), Path.Combine(Staging, part));
        }

        /// <summary>Step 2, after the new card is in place: moves the old card here and makes this the undo point.
        /// Returns false (and deletes the old card) when the drive has no room for it.</summary>
        public bool Finish(string oldCard, string cardPath, string cardName, string what, Action<int> progress)
        {
            try
            {
                long size = new FileInfo(oldCard).Length;
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_folder)));
                long reclaim = File.Exists(CardCopy) ? new FileInfo(CardCopy).Length : 0;
                if (drive.AvailableFreeSpace + reclaim < size + 32L * 1024 * 1024) throw new IOException("no room");
                Clear(_folder);   // only one copy is ever kept
                if (string.Equals(Path.GetPathRoot(Path.GetFullPath(oldCard)), Path.GetPathRoot(Path.GetFullPath(Staging)), StringComparison.OrdinalIgnoreCase))
                    File.Move(oldCard, Path.Combine(Staging, "sd.raw"));
                else
                {
                    Copy(oldCard, Path.Combine(Staging, "sd.raw"), progress);
                    File.Delete(oldCard);
                }
                var f = new FileInfo(cardPath);
                File.WriteAllLines(Path.Combine(Staging, "info.txt"), new[]
                {
                    cardName, DateTime.Now.Ticks.ToString(), f.Length.ToString(), f.LastWriteTimeUtc.Ticks.ToString(), (what ?? "").Replace("\r", " ").Replace("\n", " "),
                });
                Directory.Move(Staging, _folder);
                return true;
            }
            catch
            {
                try { if (File.Exists(oldCard)) File.Delete(oldCard); } catch { }
                Clear(Staging);
                return false;
            }
        }

        /// <summary>A save that failed: forget the copy of the mods list.</summary>
        public void Cancel() => Clear(Staging);

        /// <summary>Puts the card and the mods list back as they were before the last save. The caller reloads the library.</summary>
        public void Undo(BrawlOnlineInstall install, Library lib, string activeCard, Action<string, int> progress)
        {
            string why = CantUndo(activeCard, install.CardPath);
            if (why != null) throw new InvalidOperationException(why);
            if (install.IsRunning(out string what))
                throw new InvalidOperationException($"Close {what} first. It uses the SD card, so the card can't be changed while it is open.");
            string cardPath = install.CardPath, newPath = cardPath + ".skinmanager-new";
            bool sameDrive = string.Equals(Path.GetPathRoot(Path.GetFullPath(cardPath)), Path.GetPathRoot(Path.GetFullPath(CardCopy)), StringComparison.OrdinalIgnoreCase);
            if (sameDrive) File.Replace(CardCopy, cardPath, null);   // nothing to copy: it just moves back
            else
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cardPath)));
                long size = new FileInfo(CardCopy).Length;
                if (drive.AvailableFreeSpace < size + 64L * 1024 * 1024)
                    throw new IOException($"Drive {drive.Name} needs {size / (1024 * 1024) + 64} MB free to put the old SD card back.");
                try
                {
                    Copy(CardCopy, newPath, p => progress?.Invoke("Putting the SD card back", p * 95 / 100));
                    File.Replace(newPath, cardPath, null);
                }
                catch { try { if (File.Exists(newPath)) File.Delete(newPath); } catch { } throw; }
            }
            progress?.Invoke("Putting the mods list back", 97);
            foreach (var part in Parts)
            {
                string mine = Path.Combine(lib.Folder, part), saved = Path.Combine(_folder, part);
                Clear(mine);
                if (File.Exists(saved) || Directory.Exists(saved)) CopyPart(saved, mine);
            }
            Clear(_folder);   // the card it was taken from is gone; nothing to go back to now
            progress?.Invoke("Done", 100);
        }

        /// <summary>Removes the old safety copy the app used to keep next to the SD card.</summary>
        public static void RemoveOldBackup(string cardPath)
        {
            try { string old = cardPath + ".skinmanager-backup"; if (File.Exists(old)) File.Delete(old); } catch { }
        }

        static void Clear(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); else if (File.Exists(path)) File.Delete(path); } catch { }
        }

        static void CopyPart(string from, string to)
        {
            if (File.Exists(from)) { File.Copy(from, to, true); return; }
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyPart(d, Path.Combine(to, Path.GetFileName(d)));
        }

        static void Copy(string from, string to, Action<int> progress)
        {
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[4 << 20]; long done = 0, total = Math.Max(1, src.Length); int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n); done += n;
                    int p = (int)(done * 100 / total);
                    if (p != last) { progress?.Invoke(p); last = p; }
                }
            }
        }
    }
}
