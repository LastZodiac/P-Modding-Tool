// How sure each row of a fighter's frame data is: read straight from P+'s scripts (exact), worked out with an assumption
// the app has to make (approximate, and which), or not drawn at all because the game's code decides it (and why). This
// is what the Frame Data page's "How accurate" tab lists, so nobody has to find out by clicking around.
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public enum Sureness { Exact, Approximate, NotDrawn }

    public sealed class AccuracyLine
    {
        public string Move, Why;
        public Sureness Level;
    }

    public static class FrameDataAccuracy
    {
        public static List<AccuracyLine> For(FighterBook b)
        {
            var lines = new List<AccuracyLine>();
            foreach (var e in b.Entries)
            {
                var d = e.Data;
                var approx = new List<string>(); var notDrawn = new List<string>();
                if (d.Hits.Any(h => !h.Grab && float.IsNaN(h.Damage))) approx.Add("some damage is set by the game's code while it runs (shown as ?)");
                if (d.Hits.Any(h => !h.Grab) && d.Active.Count == 0 && d.Throw == null && d.Projectiles.Count == 0)
                    approx.Add("its hitboxes are set up, but when they're out is decided by the game's code");
                foreach (var p in d.Projectiles)
                {
                    string name = p.Name;
                    if (p.Flight != null)
                    {
                        string about = p.Flight.About ?? "";
                        if (about.Contains("drawn") || about.Contains("shown") || about.Contains("no stick") || about.Contains("steered"))
                            approx.Add($"{name}: flies by the game's rules with speeds from your files ({about})");
                    }
                    else if (p.Unknown) notDrawn.Add($"when {name} comes out is decided by the game's code, so its hits aren't on the timeline");
                    else approx.Add($"{name} is drawn where it comes out; where it goes after that is decided by the game's code");
                }
                if (FighterMotion.Moves(b, e)) approx.Add("how far the fighter travels is drawn with no stick held");
                if (e.Note != null && !e.Note.StartsWith("The fighter")) approx.Add(e.Note.TrimEnd('.'));
                bool drawsNothing = !d.Hits.Any() && !d.Projectiles.Any(p => p.Flight != null || !p.Unknown) && d.Intangible.Count == 0;
                var level = notDrawn.Count > 0 || drawsNothing && e.Note != null ? Sureness.NotDrawn : approx.Count > 0 ? Sureness.Approximate : Sureness.Exact;
                string why = level == Sureness.Exact
                    ? (e.Extra ? "Read straight from P+'s scripts (a move outside the usual list, found in the fighter's files)." : "Read straight from P+'s scripts for this move.")
                    : string.Join("; ", notDrawn.Concat(approx).Distinct()) + ".";
                lines.Add(new AccuracyLine { Move = e.Name, Level = level, Why = Capital(why) });
            }
            return lines;
        }

        /// <summary>Something about the whole fighter worth knowing (null: nothing).</summary>
        public static string FighterNote(FighterBook b) =>
            SettingIds.Find(b.Character.FilePrefix, 24000, out _, out _) || SettingIds.Find(b.Character.FilePrefix, 4000, out _, out _) ? null
            : "The app can't look up this fighter's numbered settings, so where a script tests one it follows the script's first choice.";

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
