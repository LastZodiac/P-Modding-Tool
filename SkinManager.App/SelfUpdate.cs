// The app updates itself: a newer build put in the "Update" folder next to the app (or source\new-build) is swapped
// in when the app starts. Windows lets a running program's files be renamed (not overwritten), so the old files
// become *.old, the new ones move in, and the app starts again; the *.old files are deleted on the next start.
// Also: shortcuts on the desktop and in the Start menu.
using System;
using SkinManager.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SkinManager.App
{
    static class SelfUpdate
    {
        static string AppFolder => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        static string ExeName => Path.GetFileName(Application.ExecutablePath);
        /// <summary>Put in the Update folder by OnlineUpdate after checking the download: newer by version, whatever the file dates say.</summary>
        public const string VerifiedMark = "verified-update.txt";

        static IEnumerable<string> Drops => new[] { Path.Combine(AppFolder, "Update"), Path.Combine(AppFolder, "source", "new-build") };

        /// <summary>When the app was built (the date of its program file).</summary>
        public static DateTime BuiltAt => File.GetLastWriteTime(Application.ExecutablePath);

        /// <summary>Removes the old files left by the last update.</summary>
        public static void CleanUp()
        {
            try
            {
                bool Old(string f) => System.Text.RegularExpressions.Regex.IsMatch(f, @"\.old\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var f in Directory.EnumerateFiles(AppFolder, "*.old*").Where(Old))
                    try { File.Delete(f); } catch { }
                string lib = Path.Combine(AppFolder, "lib");
                if (Directory.Exists(lib)) foreach (var f in Directory.EnumerateFiles(lib, "*.old*").Where(Old)) try { File.Delete(f); } catch { }
            }
            catch { }
        }

        /// <summary>True when a newer build was put in and the app restarted with it (this copy should close).</summary>
        public static bool Run()
        {
            foreach (var drop in Drops)
            {
                string exe = Path.Combine(drop, ExeName);
                if (!File.Exists(exe)) continue;
                var mine = new FileInfo(Application.ExecutablePath);
                var theirs = new FileInfo(exe);
                if (theirs.Length == mine.Length && Same(theirs.FullName, mine.FullName)) { Clear(drop); continue; }   // already this version
                bool verified = File.Exists(Path.Combine(drop, VerifiedMark));
                if (!verified && theirs.LastWriteTimeUtc <= mine.LastWriteTimeUtc) continue;   // an older build: leave it
                if (!verified)
                {
                    var answer = MessageBox.Show($"A new version of P+ Modding Tool is ready (made {theirs.LastWriteTime:MMM d, h:mm tt}).\r\n\r\nUpdate now? It takes a second. Your mods and SD card aren't touched.",
                        "Update the app", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) return false;
                }
                var moved = new List<(string Target, string Old)>();
                try
                {
                    foreach (var file in Directory.GetFiles(drop, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(drop.Length).TrimStart('\\', '/');
                        string first = rel.Split('\\', '/')[0];
                        if (first.Equals("Data", StringComparison.OrdinalIgnoreCase) || first.Equals("source", StringComparison.OrdinalIgnoreCase)) continue;
                        if (rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || rel.Equals(VerifiedMark, StringComparison.OrdinalIgnoreCase)) continue;
                        string target = Path.Combine(AppFolder, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target))
                        {
                            string old = target + ".old";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(target, old);
                            moved.Add((target, old));
                        }
                        else moved.Add((target, null));
                        File.Copy(file, target);
                    }
                }
                catch (Exception e)
                {
                    // put everything back as it was
                    foreach (var m in Enumerable.Reverse(moved))
                        try { if (File.Exists(m.Target)) File.Delete(m.Target); if (m.Old != null) File.Move(m.Old, m.Target); } catch { }
                    MessageBox.Show("The update couldn't be put in (" + Friendly.Explain(e) + "). The app stays as it was.", "Update the app", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                Clear(drop);
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false, WorkingDirectory = AppFolder }); return true; }
                catch { MessageBox.Show("Updated. Open the app again to use the new version.", "Update the app"); return true; }
            }
            return false;
        }

        static void Clear(string drop)
        {
            try { foreach (var f in Directory.GetFiles(drop, "*", SearchOption.AllDirectories)) File.Delete(f); } catch { }
        }

        static bool Same(string a, string b)
        {
            using (var sa = File.OpenRead(a)) using (var sb = File.OpenRead(b))
            {
                var ba = new byte[1 << 16]; var bb = new byte[1 << 16];
                int na;
                while ((na = sa.Read(ba, 0, ba.Length)) > 0)
                {
                    int nb = sb.Read(bb, 0, na);
                    if (nb != na) return false;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
                return true;
            }
        }

        // ----- shortcuts -----

        public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "P+ Modding Tool.lnk");
        public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "P+ Modding Tool.lnk");

        /// <summary>Makes a Windows shortcut to the app (through Windows' own scripting object).</summary>
        public static void MakeShortcut(string path)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows' shortcut maker isn't available.");
            object shell = Activator.CreateInstance(type);
            try
            {
                object lnk = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var lt = lnk.GetType();
                void Set(string name, object value) => lt.InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, lnk, new[] { value });
                Set("TargetPath", Application.ExecutablePath);
                Set("WorkingDirectory", AppFolder);
                Set("IconLocation", Application.ExecutablePath + ",0");
                Set("Description", "Mods, stats and frame data for Project+ on Brawl Online");
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        /// <summary>The app used to be called "P+ Skin Manager": its old shortcuts are swapped for new ones.</summary>
        public static void RenameOldShortcuts()
        {
            foreach (var (old, now) in new[] {
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "P+ Skin Manager.lnk"), DesktopShortcut),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "P+ Skin Manager.lnk"), StartMenuShortcut) })
                try { if (File.Exists(old)) { File.Delete(old); if (!File.Exists(now)) MakeShortcut(now); } } catch { }
        }

        /// <summary>Once, on the first start: offers the two shortcuts.</summary>
        public static void OfferShortcuts(IWin32Window owner)
        {
            string asked = Path.Combine(Program.DataFolder, "shortcuts-offered.txt");
            if (File.Exists(asked) || Environment.GetEnvironmentVariable("SKINMANAGER_TRACE") != null) return;
            try { Directory.CreateDirectory(Program.DataFolder); File.WriteAllText(asked, DateTime.Now.ToString("u")); } catch { return; }
            if (File.Exists(DesktopShortcut) && File.Exists(StartMenuShortcut)) return;
            if (MessageBox.Show(owner, "Put a shortcut to P+ Modding Tool on your desktop and in the Start menu?\r\n\r\n(You can do this later in Settings too.)", "Shortcuts",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { MakeShortcut(DesktopShortcut); MakeShortcut(StartMenuShortcut); }
            catch (Exception e) { MessageBox.Show(owner, "The shortcuts couldn't be made: " + Friendly.Explain(e), "Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
