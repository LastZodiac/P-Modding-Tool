// Opens a downloaded skin: a folder, a .zip, or a .rar / .7z (using WinRAR or 7-Zip if installed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SkinManager.Core
{
    public static class Archives
    {
        static readonly string[] Kinds = { ".zip", ".rar", ".7z" };

        public static bool IsArchive(string path) => Kinds.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>Returns a folder with the skin's files (a new temp folder for archives).</summary>
        public static string Open(string path)
        {
            if (Directory.Exists(path))
            {
                // a folder of downloads (one archive per color): open them all together
                var archives = Directory.EnumerateFiles(path).Where(IsArchive).ToList();
                return archives.Count > 0 ? OpenAll(Directory.EnumerateFiles(path).Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList()) : path;
            }
            if (!File.Exists(path)) throw new FileNotFoundException("That file is gone: " + path);
            string dest = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "skin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dest);
            Extract(path, dest);
            // Packs sometimes hold more archives inside (one per color): open those too, one level deep.
            foreach (var inner in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Where(IsArchive).ToList())
            {
                string sub = Path.Combine(Path.GetDirectoryName(inner), Path.GetFileNameWithoutExtension(inner) + "_files");
                try { Directory.CreateDirectory(sub); Extract(inner, sub); } catch { }
            }
            return dest;
        }

        /// <summary>Several downloads opened into one folder, each in a sub-folder named after it, so they can be added in one go.</summary>
        public static string OpenAll(IList<string> paths)
        {
            string dest = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "skins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dest);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                string name = Path.GetFileNameWithoutExtension(p);
                name = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+-", "");   // downloads are saved as <file id>-<name>
                string sub = name; int n = 2;
                while (!used.Add(sub)) sub = name + " " + n++;
                string target = Path.Combine(dest, sub);
                if (Directory.Exists(p)) { CopyFolder(p, target); continue; }
                if (!IsArchive(p)) { Directory.CreateDirectory(target); File.Copy(p, Path.Combine(target, Path.GetFileName(p)), true); continue; }
                string opened = Open(p);
                Directory.Move(opened, target);
            }
            return dest;
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string t = Path.Combine(to, f.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(t));
                File.Copy(f, t, true);
            }
        }

        static void Extract(string archive, string dest)
        {
            string ext = Path.GetExtension(archive).ToLowerInvariant();
            if (ext == ".zip")
            {
                using (var zip = ZipFile.OpenRead(archive))
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('\\', '/')));
                        if (!target.StartsWith(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) continue;  // no paths outside the folder
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                return;
            }
            string sevenZip = FindProgram(@"7-Zip\7z.exe");
            if (sevenZip != null && Run(sevenZip, $"x -y -o\"{dest}\" \"{archive}\"")) return;
            string winrar = FindProgram(@"WinRAR\WinRAR.exe") ?? FindProgram(@"WinRAR\UnRAR.exe");
            if (winrar != null && Run(winrar, $"x -ibck -y -o+ \"{archive}\" \"{dest}{Path.DirectorySeparatorChar}\"")) return;
            // Windows 10 and 11 come with tar.exe, which opens most .7z and older .rar files.
            string tar = WindowsTar();
            if (tar != null && Run(tar, $"-xf \"{archive}\" -C \"{dest}\"") && Directory.EnumerateFileSystemEntries(dest).Any()) return;
            throw new InvalidOperationException(
                $"This skin is a {ext} file, and this PC has nothing that can open it.\r\n\r\nInstall 7-Zip (free, from 7-zip.org), then try again. Or extract it yourself and choose the folder instead.");
        }

        /// <summary>True when a 7-Zip or WinRAR install was found (zip files never need one).</summary>
        public static bool HasExtractor => FindProgram(@"7-Zip\7z.exe") != null || FindProgram(@"WinRAR\WinRAR.exe") != null || FindProgram(@"WinRAR\UnRAR.exe") != null;

        static string WindowsTar()
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (string.IsNullOrEmpty(sys)) return null;
            string p = Path.Combine(sys, "tar.exe");
            return File.Exists(p) ? p : null;
        }

        static string FindProgram(string relative)
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string p = Path.Combine(root, relative);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static bool Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } return false; }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}
