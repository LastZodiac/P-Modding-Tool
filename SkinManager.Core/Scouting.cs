// What a player tends to do, in a few plain lines, from their habits over the games you played them (compared with
// everyone you've played, so "a lot" means more than usual), each with what to do about it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public static class Scouting
    {
        /// <summary>The most telling habits first. games: how many replays they're from. typical: everyone's habits
        /// added up, typicalGames their number.</summary>
        public static List<(string Habit, string Tip)> Lines(PlayerHabits h, int games, PlayerHabits typical, int typicalGames, int max = 4)
        {
            var o = new List<(double Weight, string Habit, string Tip)>();
            if (h == null || !h.Known || games == 0) return new List<(string, string)>();
            string P(int n, int of) => $"{100.0 * n / Math.Max(1, of):0}%";
            void Pick(int total, int min, (int N, string What, string Tip)[] options)
            {
                if (total < min) return;
                var top = options.OrderByDescending(x => x.N).First();
                double share = (double)top.N / total;
                if (share >= 0.45) o.Add((share * Math.Min(1, total / 10.0), top.What.Replace("{p}", P(top.N, total)), top.Tip));
            }
            Pick(h.TechChances, 5, new[]
            {
                (h.TechAway, "Techs away {p} of the time", "Wait a step behind where they land"),
                (h.TechToward, "Techs in (towards you) {p} of the time", "Stay in place: they roll right into you"),
                (h.TechInPlace, "Techs in place {p} of the time", "Hit them right where they land"),
                (h.MissedTech, "Misses techs {p} of the time", "Hit them on the ground: jab reset or a getup chase"),
            });
            Pick(h.LedgeOptions, 5, new[]
            {
                (h.LedgeGetup, "Gets off the ledge with a normal getup {p} of the time", "Cover the getup: shield and grab"),
                (h.LedgeRoll, "Rolls off the ledge {p} of the time", "Stand back and punish the end of the roll"),
                (h.LedgeAttack, "Uses ledge attack {p} of the time", "Shield it, then punish"),
                (h.LedgeJump, "Jumps from the ledge {p} of the time", "Wait above with an up air or a back air"),
                (h.LedgeDrop, "Drops from the ledge {p} of the time (to jump or attack)", "Hold your ground and keep center stage"),
            });
            Pick(h.GetupStand + h.GetupToward + h.GetupAway + h.GetupAttack, 4, new[]
            {
                (h.GetupStand, "Stands up in place after a missed tech {p} of the time", "Wait and hit them as they stand"),
                (h.GetupToward, "Rolls in after a missed tech {p} of the time", "Stay put: they roll to you"),
                (h.GetupAway, "Rolls away after a missed tech {p} of the time", "Follow them back"),
                (h.GetupAttack, "Uses getup attack {p} of the time", "Shield it, then punish"),
            });
            int rolls = h.RollsToward + h.RollsAway;
            Pick(rolls, 6, new[]
            {
                (h.RollsToward, "Rolls in (behind you) {p} of the time", "Turn around: back air or a pivot grab"),
                (h.RollsAway, "Rolls away {p} of the time", "Chase to where the roll ends"),
            });
            // more than usual: shielding, air dodging, rolling
            double perGame(int v) => (double)v / games;
            double usual(int v) => typicalGames == 0 ? 0 : (double)v / typicalGames;
            void More(int v, int tv, string what, string tip, double min)
            {
                double a = perGame(v), b = usual(tv);
                if (b > 0 && a >= min && a >= b * 1.4) o.Add((Math.Min(1, a / b - 1), $"{what} a lot ({a:0.#} a game, most players {b:0.#})", tip));
            }
            More(h.Shields, typical?.Shields ?? 0, "Shields", "Grab them", 10);
            More(h.AirDodges, typical?.AirDodges ?? 0, "Air dodges", "Wait for the dodge to end, then punish the landing", 6);
            More(rolls, (typical?.RollsToward ?? 0) + (typical?.RollsAway ?? 0), "Rolls", "Read the roll and punish its end", 6);
            More(h.SpotDodges, typical?.SpotDodges ?? 0, "Spot dodges", "Delay your grab or attack a moment", 4);
            if (h.AerialLandings >= 10 && h.LCancels < h.AerialLandings * 0.4) o.Add((0.6, $"L-cancels only {P(h.LCancels, h.AerialLandings)} of aerials", "Shield their aerials and punish the landing"));
            if (h.Throws >= 6)
            {
                var t = new[] { (h.ThrowF, "forward"), (h.ThrowB, "back"), (h.ThrowUp, "up"), (h.ThrowDown, "down") }.OrderByDescending(x => x.Item1).First();
                if (t.Item1 >= h.Throws * 0.5) o.Add((0.4, $"Throws {t.Item2} {P(t.Item1, h.Throws)} of the time", "DI that throw ahead of time"));
            }
            return o.OrderByDescending(x => x.Weight).Take(max).Select(x => (x.Habit, x.Tip)).ToList();
        }
    }
}
