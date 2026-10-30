// Where Brawl Online keeps P+ on this PC, and whether it is running.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SkinManager.Core
{
    public sealed class BrawlOnlineInstall
    {
        public string NetplayFolder;   // ...\Brawl Online\netplay
        /// <summary>The SD card the app works on: Brawl Online's own, unless another one was picked in Settings
        /// (your own Dolphin's, for making and testing mods).</summary>
        public string CardPath => CardOverride ?? Path.Combine(NetplayFolder, "User", "Wii", "sd.raw");
        public string CardOverride;    // another Dolphin's sd.raw (null: Brawl Online's)
        public string OtherDolphin;    // that Dolphin's folder (its game window uses the card too)
        public bool OwnCard => CardOverride == null;
        public string DolphinToolPath => Path.Combine(NetplayFolder, "DolphinTool.exe");
        public string BrawlDiscPath;   // the Brawl disc image Brawl Online plays (may be null)

        public static BrawlOnlineInstall Find()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string root = Path.Combine(appData, "Brawl Online");
            if (!Directory.Exists(root)) return null;
            string netplay = Path.Combine(root, "netplay");
            // Settings > Dolphin can move the game files; the launcher writes the folder to its settings.
            string moved = ReadSetting(root, "gameFilesFolder");
            if (!string.IsNullOrEmpty(moved) && Directory.Exists(moved)) netplay = moved;
            var install = new BrawlOnlineInstall { NetplayFolder = netplay };
            if (!File.Exists(install.CardPath)) return null;
            string disc = ReadSetting(root, "isoPath");
            if (string.IsNullOrEmpty(disc))
            {
                // Dolphin's own setting, as a fallback
                string ini = Path.Combine(netplay, "User", "Config", "Dolphin.ini");
                if (File.Exists(ini))
                {
                    var line = File.ReadAllLines(ini).FirstOrDefault(l => l.StartsWith("DefaultISO", StringComparison.OrdinalIgnoreCase));
                    if (line != null) disc = line.Substring(line.IndexOf('=') + 1).Trim();
                }
            }
            install.BrawlDiscPath = !string.IsNullOrEmpty(disc) && File.Exists(disc) ? disc : null;
            return install;
        }

        static string ReadSetting(string root, string key)
        {
            string settings = Path.Combine(root, "Settings");
            if (!File.Exists(settings)) return null;
            var m = Regex.Match(File.ReadAllText(settings), "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        /// <summary>The program file of a process; works for 64-bit processes from this 32-bit app.</summary>
        static string ProcessPath(Process p)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                try { return p.MainModule?.FileName; } catch { return null; }
            }
            IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, p.Id);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>True while Brawl Online or one of its game windows is open (they use the card).</summary>
        public bool IsRunning(out string what)
        {
            what = null;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName;
                    if (name.Equals("Brawl Online", StringComparison.OrdinalIgnoreCase)) { what = "Brawl Online"; return true; }
                    if (name.Equals("Dolphin", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only Brawl Online's own Dolphin uses the card; another Dolphin on this PC does not.
                        string file = ProcessPath(p);
                        if (file == null || file.StartsWith(NetplayFolder, StringComparison.OrdinalIgnoreCase)) { what = "Brawl Online's game window"; return true; }
                        if (OtherDolphin != null && file.StartsWith(OtherDolphin, StringComparison.OrdinalIgnoreCase)) { what = "your Dolphin"; return true; }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }
    }
}
