// Smash stats from replays: openings, kills, the moves you land and kill with, per game and summed up.
// An opening is a run of hits on one player by the other; the one who starts it won that exchange in neutral.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    /// <summary>One of your 1-on-1 games, with what its replay says.</summary>
    public sealed class GameSmash
    {
        public GameRecord Game; public GamePlayer MePlayer, ThemPlayer;
        public ReplayData Replay; public int Me, Them;          // the players' places in the replay
        public int MyOpenings, TheirOpenings, Kills, Deaths;
        public float Dealt, Taken;
        public List<float> KillPercents = new List<float>(), DeathPercents = new List<float>();
        /// <summary>Your moves that landed (a multi-hit move counts once), the ones that killed, the ones that started an
        /// opening; and theirs that killed you. Subaction numbers.</summary>
        public List<int> Landed = new List<int>(), KillMoves = new List<int>(), Openers = new List<int>(), DeathMoves = new List<int>();
        public bool Won => MePlayer.Won == true;
        // the game's highlights
        public PlayerHabits MyHabits, TheirHabits;
        public float BestComboDamage; public int BestComboHits; public List<int> BestComboMoves = new List<int>();
        public float BestComboStart;                    // their percent when it started
        public int ZeroToDeaths, EdgeguardKills, SelfDestructs, EarliestKill = -1;
        public float HighestPercent;                    // the most you were on and lived through a hit
        public int WorstDeficit;                        // the most stocks you were behind at any moment
        public bool LastStockClutch;                    // both on their last stock at some point, and you won
        public int Seconds => Replay.Frames / 60;
        public int MyCharacter => MePlayer.Character;
        public int TheirCharacter => ThemPlayer.Character;
        public int? MyStocksLeft => Replay.EndStocks.Count > Me ? Replay.EndStocks[Me] : (int?)null;
        public int? TheirStocksLeft => Replay.EndStocks.Count > Them ? Replay.EndStocks[Them] : (int?)null;

        public double OpeningsPerKill => Kills == 0 ? double.NaN : (double)MyOpenings / Kills;
        public double DamagePerOpening => MyOpenings == 0 ? double.NaN : Dealt / MyOpenings;
        public double NeutralWin => MyOpenings + TheirOpenings == 0 ? double.NaN : (double)MyOpenings / (MyOpenings + TheirOpenings);
        public double AvgKill => KillPercents.Count == 0 ? double.NaN : KillPercents.Average();
        public double AvgDeath => DeathPercents.Count == 0 ? double.NaN : DeathPercents.Average();

        public static GameSmash Of(GameRecord g, GamePlayer me, GamePlayer them, ReplayData d)
        {
            if (d == null || d.Characters.Count != 2) return null;
            int mi = d.PlayerOf(me.UserId, me.Code), ti = d.PlayerOf(them.UserId, them.Code);
            if (mi < 0 || ti < 0 || mi == ti) return null;
            var s = new GameSmash { Game = g, MePlayer = me, ThemPlayer = them, Replay = d, Me = mi, Them = ti };
            foreach (var o in d.Openings)
            {
                if (o.Attacker == mi) { s.MyOpenings++; s.Dealt += o.Damage; s.Openers.Add(o.FirstMove); }
                else if (o.Attacker == ti) { s.TheirOpenings++; s.Taken += o.Damage; }
            }
            foreach (var death in d.Deaths)
            {
                if (death.Victim == ti) { s.Kills++; s.KillPercents.Add(death.Percent); if (death.Killer == mi && death.Move >= 0) s.KillMoves.Add(death.Move); }
                else if (death.Victim == mi) { s.Deaths++; s.DeathPercents.Add(death.Percent); if (death.Killer == ti && death.Move >= 0) s.DeathMoves.Add(death.Move); }
            }
            s.MyHabits = d.Habits.Count > mi ? d.Habits[mi] : null; s.TheirHabits = d.Habits.Count > ti ? d.Habits[ti] : null;
            // your best opening, and the ones that took a stock from (near) nothing
            foreach (var o in d.Openings.Where(o => o.Attacker == mi))
            {
                var hits = d.Hits.Where(h => h.Attacker == mi && h.Victim == ti && h.Frame >= o.Start && h.Frame <= o.End).ToList();
                if (hits.Count == 0) continue;
                if (o.Damage > s.BestComboDamage || o.Damage == s.BestComboDamage && o.Hits > s.BestComboHits)
                { s.BestComboDamage = o.Damage; s.BestComboHits = o.Hits; s.BestComboMoves = hits.Select(h => h.Move).ToList(); s.BestComboStart = hits[0].Before; }
                if (o.Killed && hits[0].Before <= 10) s.ZeroToDeaths++;
            }
            float left = d.StageLeft, right = d.StageRight;
            foreach (var death in d.Deaths.Where(x => x.Victim == ti))
            {
                if (death.Killer == mi)
                {
                    if (death.Percent >= 20 && (s.EarliestKill < 0 || death.Percent < s.EarliestKill)) s.EarliestKill = (int)death.Percent;   // under 20%: they gave it away
                    var killing = d.Hits.LastOrDefault(h => h.Victim == ti && h.Attacker == mi && h.Frame <= death.Frame);
                    // off the stage: well past its edge and not above it, or below it
                    if (killing != null && right - left > 20 && ((killing.X < left - 10 || killing.X > right + 10) && killing.Y < 15 || killing.Y < -15)) s.EdgeguardKills++;
                }
            }
            s.SelfDestructs = d.Deaths.Count(x => x.Victim == mi && x.Killer < 0);
            foreach (var h in d.Hits.Where(h => h.Victim == mi)) s.HighestPercent = Math.Max(s.HighestPercent, h.Before + h.Damage);
            // stocks through the game
            int mine = (s.MyStocksLeft ?? 0) + s.Deaths, theirs = (s.TheirStocksLeft ?? 0) + s.Kills;
            foreach (var death in d.Deaths.OrderBy(x => x.Frame))
            {
                if (death.Victim == mi) mine--; else if (death.Victim == ti) theirs--;
                s.WorstDeficit = Math.Max(s.WorstDeficit, theirs - mine);
                if (mine == 1 && theirs == 1) s.LastStockClutch = true;
            }
            if (!s.Won) s.LastStockClutch = false;
            ReplayHit last = null;
            foreach (var h in d.Hits.Where(h => h.Attacker == mi))
            {
                if (last == null || last.Move != h.Move || h.Frame - last.Frame > 12) s.Landed.Add(h.Move);
                last = h;
            }
            return s;
        }
    }

    /// <summary>Stats summed over many games.</summary>
    public sealed class SmashSummary
    {
        public int Games, Wins, Openings, TheirOpenings, Kills, Deaths;
        public float Dealt, Taken;
        public double KillPercentSum, DeathPercentSum; public int KillCount, DeathCount;

        public double WinRate => Games == 0 ? double.NaN : (double)Wins / Games;
        public double OpeningsPerKill => Kills == 0 ? double.NaN : (double)Openings / Kills;
        public double DamagePerOpening => Openings == 0 ? double.NaN : Dealt / Openings;
        public double NeutralWin => Openings + TheirOpenings == 0 ? double.NaN : (double)Openings / (Openings + TheirOpenings);
        public double AvgKill => KillCount == 0 ? double.NaN : KillPercentSum / KillCount;
        public double AvgDeath => DeathCount == 0 ? double.NaN : DeathPercentSum / DeathCount;
        public double KillsPerGame => Games == 0 ? double.NaN : (double)Kills / Games;

        public static SmashSummary Of(IEnumerable<GameSmash> games)
        {
            var s = new SmashSummary();
            foreach (var g in games)
            {
                s.Games++; if (g.Won) s.Wins++;
                s.Openings += g.MyOpenings; s.TheirOpenings += g.TheirOpenings; s.Kills += g.Kills; s.Deaths += g.Deaths;
                s.Dealt += g.Dealt; s.Taken += g.Taken;
                s.KillPercentSum += g.KillPercents.Sum(); s.KillCount += g.KillPercents.Count;
                s.DeathPercentSum += g.DeathPercents.Sum(); s.DeathCount += g.DeathPercents.Count;
            }
            return s;
        }
    }

    /// <summary>Move names the way players say them, from a fighter's subaction (animation) names.</summary>
    public static class MoveNames
    {
        public const string Other = "Other (projectile, item…)";

        /// <summary>"Forward smash", "Neutral air", "Up B"... or null if the animation isn't an attack.</summary>
        public static string Friendly(string anim)
        {
            if (string.IsNullOrEmpty(anim)) return null;
            if (anim.StartsWith("Special", StringComparison.OrdinalIgnoreCase))
            {
                var n = FrameDataBook.SpecialName(anim);
                int cut = n.IndexOf(" B", StringComparison.Ordinal);
                return cut > 0 ? n.Substring(0, cut + 2) : n;
            }
            var name = FrameDataBook.StandardName(anim);
            if (name == null) return null;
            name = name.Replace(" (up)", "").Replace(" (down)", "").Replace(" (100%+)", "");
            if (name.StartsWith("Jab ")) return "Jab";
            if (name.StartsWith("Rapid jab")) return "Rapid jab";
            return System.Text.RegularExpressions.Regex.Replace(name, " [23]$", "");
        }

        /// <summary>Named counts of a list of subaction numbers, most first.</summary>
        public static List<(string Name, int Count)> Count(IEnumerable<(int Character, int Sub)> moves, Func<int, int, string> name) =>
            moves.Select(m => name(m.Character, m.Sub) ?? Other).GroupBy(n => n).Select(g => (g.Key, g.Count()))
                 .OrderBy(x => x.Key == Other ? 1 : 0).ThenByDescending(x => x.Item2).ToList();
    }
}
