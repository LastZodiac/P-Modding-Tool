// Costumes P+ does not replace come from the Brawl disc. To check a skin against the costume it
// replaces, the original is read from the disc with Brawl Online's own DolphinTool.exe.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SkinManager.Core
{
    public static class DiscCostumes
    {
        /// <summary>Returns the disc's copy of a costume file, or null if it cannot be read.</summary>
        public static byte[] Read(BrawlOnlineInstall install, Character c, int fileNumber, string cacheFolder)
        {
            if (install?.BrawlDiscPath == null || !File.Exists(install.DolphinToolPath)) return null;
            Directory.CreateDirectory(cacheFolder);
            string cached = Path.Combine(cacheFolder, $"Fit{c.FilePrefix}{fileNumber:00}.pac");
            if (File.Exists(cached)) return File.ReadAllBytes(cached);

            string folder = "/fighter/" + c.Folder.ToLowerInvariant();
            foreach (var ext in new[] { ".pac", ".pcs" })
            {
                string discPath = $"{folder}/Fit{c.FilePrefix}{fileNumber:00}{ext}";
                string outDir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", Guid.NewGuid().ToString("N"));
                try
                {
                    if (!Run(install.DolphinToolPath, $"extract -i \"{install.BrawlDiscPath}\" -o \"{outDir}\" -s \"{discPath}\" -g -q")) continue;
                    // DolphinTool writes <out>/DATA/files/<path>; find the file wherever the partition folder is.
                    var found = Directory.Exists(outDir)
                        ? Directory.EnumerateFiles(outDir, Path.GetFileName(discPath), SearchOption.AllDirectories).FirstOrDefault()
                        : null;
                    if (found == null) continue;
                    var bytes = File.ReadAllBytes(found);
                    File.WriteAllBytes(cached, bytes);
                    return bytes;
                }
                catch { }
                finally { try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { } }
            }
            return null;
        }

        static bool Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardError = true, RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 0;
            }
        }
    }
}
