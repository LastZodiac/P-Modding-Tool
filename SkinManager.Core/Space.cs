// How the P+ SD card's space is used: P+ itself, the mods that are on, the margin kept free, and what's left.
// A mod costs only the difference over the file it replaces (a skin about the size of P+'s costume costs ~nothing).
using System;
using System.Collections.Generic;
using System.IO;

namespace SkinManager.Core
{
    /// <summary>How the card's space splits up: P+, the mods that are on, the margin kept free, and what each mod costs.</summary>
    public sealed class SpaceReport
    {
        public long Total, Free, Margin = PPlusCard.MinFreeBytes;
        public long Mods;                                   // extra space the mods that are on take
        public long PPlus => Math.Max(0, Total - Free - Mods);
        public long Usable => Math.Max(0, Free - Margin);   // room left for more mods
        public Dictionary<string, long> Cost = new Dictionary<string, long>();   // mod id -> extra space when on (can be negative)
    }

    /// <summary>Works out how much room the card has and what each mod takes.</summary>
    public static class Space
    {
        /// <summary>Measures the card: each mod's cost is the size of its files minus the files they replace (as stored on the
        /// card), and the mods that are on are added up.</summary>
        public static SpaceReport Measure(Fat32Image card, Library lib)
        {
            var r = new SpaceReport { Total = card.TotalBytes, Free = card.FreeBytes };
            foreach (var s in lib.Skins)
            {
                long cost = 0;
                foreach (var f in s.Files)
                {
                    string copy = Path.Combine(lib.SkinFolder(s.Id), Path.GetFileName(f.CardPath));
                    long mine = File.Exists(copy) ? new FileInfo(copy).Length : 0;
                    cost += card.OnCard(mine) - card.OnCard(lib.OriginalSize(f.CardPath) ?? PPlusSize(card, f.CardPath));
                }
                r.Cost[s.Id] = cost;
                if (s.Enabled) r.Mods += cost;
            }
            foreach (var s in lib.Stages)
            {
                string copy = Path.Combine(lib.StageFolder(s.Id), Path.GetFileName(s.CardPath));
                long mine = File.Exists(copy) ? new FileInfo(copy).Length : 0;
                long cost = card.OnCard(mine) - card.OnCard(lib.OriginalSize(s.CardPath) ?? PPlusSize(card, s.CardPath));
                r.Cost[s.Id] = cost;
                if (s.Enabled) r.Mods += cost;
            }
            return r;
        }

        // no original saved (it's saved when a mod first goes on): what's on the card now is P+'s
        static long PPlusSize(Fat32Image card, string path) => card.Exists(path) ? card.FileSize(path) : 0;

        /// <summary>A size in words ("12 MB", "1.4 GB", "300 KB"); negative sizes get a minus sign.</summary>
        public static string MB(long bytes) => bytes < 0 ? "−" + MB(-bytes)
            : bytes >= 1000L * 1024 * 1024 ? $"{bytes / 1073741824.0:0.0} GB"
            : bytes >= 10L * 1024 * 1024 ? $"{bytes / 1048576.0:0} MB" : bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(0, bytes / 1024)} KB";
    }
}
