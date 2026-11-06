// Older versions could save one of the app's own stage builds as "P+'s file" for a path: when a stage mod was made
// smaller to fit the game's memory (or fixed again after a P+ update), the copy still on the card was taken for P+'s.
// Turning that mod off would then put the old build back instead of P+'s stage (and an old build that was too big
// freezes the game). This finds those saved originals and puts P+'s real file in their place.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    /// <summary>Finds and fixes stage originals that are really an older build of one of your stage mods.</summary>
    public static class OriginalsRepair
    {
        /// <summary>P+'s real file for each stage path whose saved original is an old build of the mod on it. Only for a
        /// card that's on the release (on an older P+, a different original is expected). P+'s file comes from any of
        /// the cards or any mods list's saved originals; a path whose file isn't found is left as it is.</summary>
        public static Dictionary<string, byte[]> Find(Library lib, Fat32Image card, PPlusRelease release, IEnumerable<Fat32Image> cards, IEnumerable<Library> libs)
        {
            var fixes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (lib == null || card == null || release == null || !Versions.IsOn(card, release)) return fixes;
            foreach (var g in lib.Stages.Where(s => s.Kind == null && !s.IsSwap).GroupBy(s => s.CardPath, StringComparer.OrdinalIgnoreCase))
            {
                string path = g.Key;
                if (!release.Files.TryGetValue(path, out var pplusSha) || !lib.HasOriginalFile(path)) continue;
                var saved = lib.OriginalFile(path);
                if (saved == null || Library.Sha256(saved) == pplusSha) continue;
                // the saved file must be one of the mod's own builds (same pictures by name), not a stage put there by hand
                var names = Pictures(saved);
                if (names == null || names.Count == 0) continue;
                bool ownBuild = g.Any(s =>
                {
                    string copy = Path.Combine(lib.StageFolder(s.Id), Path.GetFileName(s.CardPath));
                    return File.Exists(copy) && Pictures(File.ReadAllBytes(copy)) is HashSet<string> mine && mine.SetEquals(names);
                });
                if (!ownBuild) continue;
                byte[] pplus = null;
                foreach (var c in cards)
                {
                    try { if (c.Exists(path)) { var b = c.ReadFile(path); if (Library.Sha256(b) == pplusSha) { pplus = b; break; } } }
                    catch { }
                }
                if (pplus == null)
                    pplus = libs.Where(l => l != null && l.HasOriginalFile(path)).Select(l => l.OriginalFile(path)).FirstOrDefault(b => b != null && Library.Sha256(b) == pplusSha);
                if (pplus != null) fixes[path] = pplus;
            }
            return fixes;
        }

        /// <summary>Saves P+'s files found by Find as the originals.</summary>
        public static void Apply(Library lib, Dictionary<string, byte[]> fixes)
        {
            foreach (var kv in fixes) lib.SaveOriginalFile(kv.Key, kv.Value, replace: true);
        }

        /// <summary>The names of the pictures in a stage file (null if it can't be read).</summary>
        static HashSet<string> Pictures(byte[] stage)
        {
            try
            {
                using (var tmp = new TempFile(".pac", stage))
                lock (BrawlGate.Lock)
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                    return new HashSet<string>(root.FindChildrenByType(null, ResourceType.TEX0).Select(t => t.Name), StringComparer.Ordinal);
            }
            catch { return null; }
        }
    }
}
