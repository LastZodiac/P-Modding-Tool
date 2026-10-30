// Another Dolphin's SD card (a mod maker's own P+ setup): found from the Dolphin folder you pick, or the sd.raw itself.
// Dolphin keeps its card at User\Wii\sd.raw (a portable Dolphin, next to Dolphin.exe), at Wii\sd.raw in its user
// folder (Documents\Dolphin Emulator), or wherever its settings say (Dolphin.ini: WiiSDCardPath). Some Dolphins copy a
// folder into the card each time they start ("sync SD folder"): then changes to the card don't last, so that's said.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class OtherCard
    {
        public string Card;            // the sd.raw
        public string Dolphin;         // the Dolphin folder it belongs to (to know when that Dolphin is open)
        public string Problem;         // why it can't be used (null: it can)
        public string Warning;         // usable, but something to know
        public string Root;            // the build's folder on it ("/Project+", or another build's own)
    }

    public static class OtherCards
    {
        /// <summary>The card of the Dolphin in this folder (or this file, if an sd.raw was picked), checked.</summary>
        public static OtherCard Find(string picked)
        {
            var r = new OtherCard();
            try
            {
                string card = null, root = null;
                if (File.Exists(picked)) { card = picked; root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(picked))); }   // ...\User\Wii\sd.raw
                else if (Directory.Exists(picked))
                {
                    root = picked;
                    // where Dolphin's settings say the card is
                    foreach (var ini in new[] { Path.Combine(picked, "User", "Config", "Dolphin.ini"), Path.Combine(picked, "Config", "Dolphin.ini") })
                    {
                        if (!File.Exists(ini)) continue;
                        var set = Setting(ini, "WiiSDCardPath");
                        if (!string.IsNullOrEmpty(set))
                        {
                            string full = Path.IsPathRooted(set) ? set : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ini)), set);
                            if (File.Exists(full)) { card = full; break; }
                        }
                    }
                    card = card ?? new[] { Path.Combine(picked, "User", "Wii", "sd.raw"), Path.Combine(picked, "Wii", "sd.raw"), Path.Combine(picked, "sd.raw") }.FirstOrDefault(File.Exists);
                }
                if (card == null) { r.Problem = "No SD card (sd.raw) was found in that folder. Pick the folder Dolphin.exe is in, or the sd.raw file itself (usually in User\\Wii)."; return r; }
                r.Card = Path.GetFullPath(card); r.Dolphin = root != null && Directory.Exists(root) ? Path.GetFullPath(root) : Path.GetDirectoryName(r.Card);
                using (var img = new Fat32Image(r.Card, false))
                {
                    r.Root = Build.FindRoot(img);
                    if (r.Root == null) { r.Problem = "That SD card doesn't have Project+ (or a build made from it) on it: no folder with pf\\fighter in it."; return r; }
                }
                // a Dolphin that copies a folder into the card at every start: changes made to the card are lost
                foreach (var ini in new[] { Path.Combine(r.Dolphin, "User", "Config", "Dolphin.ini"), Path.Combine(r.Dolphin, "Config", "Dolphin.ini") })
                    if (File.Exists(ini) && string.Equals(Setting(ini, "WiiSDCardEnableFolderSync"), "True", StringComparison.OrdinalIgnoreCase))
                        r.Warning = "This Dolphin copies a folder into its SD card every time it starts (\"Sync SD folder\" in Dolphin's Wii settings). Mods put on the card would be replaced: turn that off in Dolphin first, or put your mods in that folder yourself.";
            }
            catch (Exception e) { r.Problem = "That SD card couldn't be read: " + e.Message; }
            return r;
        }

        static string Setting(string ini, string key)
        {
            foreach (var line in File.ReadAllLines(ini))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        /// <summary>True when the mods list for this card hasn't been started yet (nothing brought over, nothing added).</summary>
        public static bool IsNew(string folder) => !File.Exists(Path.Combine(folder, "installed.json"));

        /// <summary>Copies the mods in My Mods over to another card's mods list, all turned off (so that card doesn't change
        /// until you turn them on there). Files are linked where Windows allows it, so they don't take the space twice.</summary>
        public static int BringMods(string from, string to)
        {
            if (!File.Exists(Path.Combine(from, "installed.json"))) return 0;
            Directory.CreateDirectory(to);
            foreach (var part in new[] { "Skins", "StageMods" })
                if (Directory.Exists(Path.Combine(from, part))) LinkFolder(Path.Combine(from, part), Path.Combine(to, part));
            foreach (var file in new[] { "installed.json", "stage-index-4.json" })
                if (File.Exists(Path.Combine(from, file))) File.Copy(Path.Combine(from, file), Path.Combine(to, file), true);
            var lib = new Library(to);
            foreach (var x in lib.Skins) x.Disabled = true;
            foreach (var x in lib.Stages) x.Disabled = true;
            lib.Profiles.Clear(); lib.ActiveProfile = null; lib.ProfilesMade = false; lib.Written.Clear();
            lib.Save();
            return lib.Skins.Count + lib.Stages.Count;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newFile, string existing, IntPtr security);

        static void LinkFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(dest)) continue;
                bool linked = false;
                try { linked = CreateHardLink(dest, f, IntPtr.Zero); } catch { }
                if (!linked) File.Copy(f, dest);
            }
            foreach (var d in Directory.GetDirectories(from)) LinkFolder(d, Path.Combine(to, Path.GetFileName(d)));
        }

        /// <summary>Where the app keeps the mods list of a card that isn't Brawl Online's (its own, so the two don't mix).</summary>
        public static string DataFolderFor(string dataFolder, string card)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var key = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(card).ToLowerInvariant()))).Replace("-", "").Substring(0, 12);
                return Path.Combine(dataFolder, "Other cards", key);
            }
        }
    }
}
