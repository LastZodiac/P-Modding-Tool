// After a game: for each stock you lost to a hit, what the hit was and how to DI it. The kill hit is read from the
// replay (the move, your percent and where you were); the hit's numbers come from the attacker's moveset; the flight
// is worked out like the DI trainer does (Melee rules, Battlefield-sized blast zones), from where you really were.
// "Away" means towards the edge you were sent to, "in" back towards the middle of the stage.
// Also small fun facts about how a game went, and a combo idea from the move you opened with most.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class DeathTip
    {
        public int Killer;             // their character
        public string Move;            // "Forward tilt"
        public float Percent;          // yours, before the hit
        public float? Stick;           // the best stick angle (attacker-facing frame: 0 = away, 90 = up)
        public bool Saved;             // that DI would have kept you alive
        public int? LivesTo;           // with that DI, the percent this move kills from the middle of the stage
        public bool SentLeft;          // the side of the stage you were sent to (for drawing the arrow)
        public string Words => DiCoach.Words(Stick);
        /// <summary>The stick on screen (0 = right, 90 = up).</summary>
        public float ScreenAngle => Stick == null ? 0 : SentLeft ? 180 - Stick.Value : Stick.Value;
    }

    /// <summary>Everything the coach has to say about one game.</summary>
    public sealed class GameCoach
    {
        public List<DeathTip> Tips = new List<DeathTip>();
        public List<string> Facts = new List<string>();
        public string Combo;
        public bool Any => Tips.Count + Facts.Count > 0 || Combo != null;
    }

    public static class DeathCoach
    {
        /// <summary>The stocks a player lost to hits, with the DI that would have helped most.</summary>
        public static List<DeathTip> For(ReplayData d, int me, Func<int, FighterBook> book, Func<int, int, string> moveName)
        {
            var tips = new List<DeathTip>();
            if (d == null || me < 0 || me >= d.Characters.Count) return tips;
            FighterBook mine = null;
            foreach (var death in d.Deaths.Where(x => x.Victim == me && x.Killer >= 0 && x.Killer != me && x.Move >= 0).OrderBy(x => x.Frame))
            {
                try
                {
                    var hit = d.Hits.Where(h => h.Victim == me && h.Attacker == death.Killer && h.Frame <= death.Frame && h.Move == death.Move).OrderByDescending(h => h.Frame).FirstOrDefault()
                              ?? d.Hits.Where(h => h.Victim == me && h.Attacker == death.Killer && h.Frame <= death.Frame).OrderByDescending(h => h.Frame).FirstOrDefault();
                    if (hit == null || death.Frame - hit.Frame > 240) continue;     // not sent flying by it (lost it offstage later)
                    int killerChar = d.Characters[death.Killer];
                    var them = book(killerChar);
                    mine = mine ?? book(d.Characters[me]);
                    if (them == null || mine == null) continue;
                    var entry = them.Entries.FirstOrDefault(e => e.Data != null && e.Data.Subactions.Contains(hit.Move));
                    if (entry == null) continue;
                    var hb = entry.Data.Throw != null && entry.Data.Hits.All(x => x.Grab) ? entry.Data.Throw
                        : entry.Data.Hits.Where(x => !x.Grab && !float.IsNaN(x.Damage)).OrderBy(x => Math.Abs(x.Damage - hit.Damage)).ThenByDescending(x => x.Kbg + x.Bkb).FirstOrDefault();
                    if (hb == null) continue;
                    var body = mine.Body ?? Body.Of(mine);
                    float pct = hit.Before;
                    bool air = hit.Y > 2;
                    // the attacker faces the side you were sent to
                    bool left = hit.X < 0;
                    float x = Math.Abs(hit.X), y = Math.Max(0, hit.Y);
                    float kb = Combos.Knockback(hb, pct, body.Weight), launch = DiCoach.Angle(hb, kb, air);
                    DiCoach.Fly(kb, launch, body, x, y, out bool diesNoDi);
                    if (!diesNoDi && !Lethal(kb, launch, body, x, y)) continue;   // by the numbers it wasn't the hit that killed: an edgeguard
                    float? best = null; float bestDanger = DiCoach.Danger(DiCoach.Fly(kb, launch, body, x, y, out _), kb);
                    for (int k = 0; k < 32; k++)
                    {
                        float stick = k * 360f / 32;
                        float dd = DiCoach.Danger(DiCoach.Fly(kb, DiCoach.WithDi(launch, stick), body, x, y, out _), kb);
                        if (dd < bestDanger - 0.002f) { bestDanger = dd; best = stick; }
                    }
                    DiCoach.Fly(kb, DiCoach.WithDi(launch, best), body, x, y, out bool diesBest);
                    int? livesTo = diesBest ? DiCoach.KillPercent(hb, body, air, (p, l) => DiCoach.SurvivalStick(hb, body, air, p)) : null;
                    tips.Add(new DeathTip
                    {
                        Killer = killerChar, Move = moveName?.Invoke(killerChar, hit.Move) ?? entry.Name, Percent = pct,
                        Stick = best, Saved = !diesBest, LivesTo = livesTo, SentLeft = left,
                    });
                }
                catch { }
            }
            return tips;
        }

        static bool Lethal(float kb, float launch, Body body, float x, float y) => DiCoach.Danger(DiCoach.Fly(kb, launch, body, x, y, out _), kb) > 0.9f;

        /// <summary>What to tell you about a stock, in a sentence.</summary>
        public static string Say(DeathTip t)
        {
            string n = Fighters.Name(t.Killer);
            string who = n + (n.EndsWith("s") ? "' " : "'s ") + (t.Move ?? "hit");
            if (t.Stick == null) return $"{who} at {t.Percent:0}%: no DI would have changed that one.";
            return t.Saved
                ? $"{who} at {t.Percent:0}%: DI {t.Words} and you'd have lived."
                : $"{who} at {t.Percent:0}%: no DI saves that. Best is {t.Words}" + (t.LivesTo != null ? $", and it kills from about {t.LivesTo}% even then." : ".");
        }
    }

    public static class FunFacts
    {
        /// <summary>A couple of things worth knowing about how a game went (the most notable first).</summary>
        public static List<string> For(GameSmash s, Func<int, int, string> moveName, int max = 2)
        {
            var facts = new List<(double Score, string Text)>();
            if (s == null) return new List<string>();
            string Name(int sub) => moveName?.Invoke(s.MyCharacter, sub) ?? "a projectile";
            if (s.ZeroToDeaths > 0) facts.Add((10, s.ZeroToDeaths == 1 ? "You zero-to-deathed them. One opening, one stock." : $"{s.ZeroToDeaths} zero-to-deaths this game!"));
            if (s.LastStockClutch) facts.Add((9, "Last stock vs last stock, and you took it. Clutch."));
            if (s.HighestPercent >= 150) facts.Add((7 + s.HighestPercent / 100, $"You lived through a hit at {s.HighestPercent:0}%."));
            if (s.EarliestKill >= 0 && s.EarliestKill < 70) facts.Add((7, $"Earliest kill: {s.EarliestKill}%. That's early."));
            var top = s.Landed.GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top != null && top.Count() >= 6) facts.Add((4 + top.Count() / 10.0, $"Your MVP move: {Name(top.Key)}, landed {top.Count()} times."));
            var h = s.MyHabits;
            if (h != null && h.Known)
            {
                if (h.AerialLandings >= 6)
                {
                    double r = (double)h.LCancels / h.AerialLandings;
                    facts.Add((r >= 0.9 ? 6 : r < 0.5 ? 5 : 2, $"You L-cancelled {h.LCancels} of {h.AerialLandings} aerials ({r * 100:0}%)." + (r < 0.5 ? " Press shield just before landing to halve the lag." : "")));
                }
                int techs = h.TechInPlace + h.TechToward + h.TechAway + h.WallTech, knocked = techs + h.MissedTech;
                if (knocked >= 4) facts.Add((techs == knocked ? 6 : h.MissedTech >= 4 ? 5 : 2, techs == knocked ? $"You teched all {knocked} knockdowns." : $"You teched {techs} of {knocked} knockdowns." + (h.MissedTech >= 4 ? " Press shield as you hit the ground." : "")));
                if (h.Wavedashes >= 5) facts.Add((3 + h.Wavedashes / 10.0, $"{h.Wavedashes} wavedashes. Smooth."));
                if (h.FramesAlive > 600 && h.FramesOffstage > h.FramesAlive * 0.15) facts.Add((3, $"You spent {100.0 * h.FramesOffstage / h.FramesAlive:0}% of the game off the stage."));
            }
            if (s.KillMoves.Count >= 2) facts.Add((2, "Your kills: " + string.Join(", ", s.KillMoves.Select(Name)) + "."));
            if (!double.IsNaN(s.NeutralWin) && s.MyOpenings + s.TheirOpenings >= 10 && s.NeutralWin >= 0.65) facts.Add((5, $"You won {s.NeutralWin * 100:0}% of the neutral exchanges."));
            return facts.OrderByDescending(f => f.Score).Take(max).Select(f => f.Text).ToList();
        }

        /// <summary>A combo idea: from the move you opened with most, the follow-up that works over the widest spread of
        /// percents on their character (no DI). Null when there's nothing worth saying.</summary>
        public static string ComboIdea(GameSmash s, FighterBook mine, FighterBook theirs, Func<int, int, string> moveName)
        {
            if (s == null || mine == null || theirs == null || s.Openers.Count == 0) return null;
            mine.Measure();
            var target = theirs.Body ?? Body.Of(theirs);
            foreach (var g in s.Openers.GroupBy(x => x).OrderByDescending(x => x.Count()).Take(3))
            {
                var first = mine.Entries.FirstOrDefault(e => e.Data != null && e.Data.Subactions.Contains(g.Key));
                if (first == null) continue;
                var ranges = Combos.Ranges(mine, mine.Places, first, target, -1, 0, 4);
                var best = ranges.Where(kv => kv.Key != first || kv.Value.Count > 0)
                    .Select(kv => (Move: kv.Key, Span: kv.Value.OrderByDescending(r => r.To - r.From).First()))
                    .Where(x => x.Span.To - x.Span.From >= 12)
                    .OrderByDescending(x => x.Move.Data.Hits.Where(hb => !hb.Grab && !float.IsNaN(hb.Damage)).Select(hb => hb.Damage).DefaultIfEmpty(0).Max() * (x.Span.To - x.Span.From))
                    .FirstOrDefault();
                if (best.Move == null) continue;
                string opener = moveName?.Invoke(s.MyCharacter, g.Key) ?? first.Name;
                return $"Combo idea: {opener} → {best.Move.Name} is a true combo on {Fighters.Name(s.TheirCharacter)} from {best.Span.From}% to {best.Span.To}% (if they don't DI).";
            }
            return null;
        }
    }
}
