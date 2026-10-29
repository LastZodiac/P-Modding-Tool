// Your progress, worked out again from all your games each time (so nothing can get out of step): a score and a
// grade for each game, XP and levels (yours and each character's), badges for feats in a game and milestones over
// time, and personal bests. A game's report is what that game added.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class Badge
    {
        public string Id, Name, What;       // "flawless", "Flawless", "Won without losing a stock"
        public int Tier;                    // milestones: 1 bronze .. 4 platinum; feats: 0
        public static readonly string[] Tiers = { "", "Bronze", "Silver", "Gold", "Platinum" };
    }

    public sealed class Best
    {
        public string Id, Name, Unit;       // "combo", "Biggest combo", "%"
        public double Value; public bool Lower;   // lower is better (earliest kill, fastest win)
        public long GameId; public string Detail;
        public string Text => Unit == "time" ? $"{(int)Value / 60}:{(int)Value % 60:00}" : Unit == "%" ? $"{Value:0}%" : Unit == "" ? $"{Value:0}" : $"{Value:0} {Unit}";
    }

    public sealed class GameProgress
    {
        public long GameId;
        public int Score = -1;              // 0..100 (needs the replay for more than the result)
        public string Grade;                // S A B C D
        public int Percentile;              // better than this % of your games before it
        public int Xp; public List<(string Why, int Xp)> XpParts = new List<(string, int)>();
        public int LevelBefore, LevelAfter, CharLevelBefore, CharLevelAfter;
        public int XpBefore, CharXpBefore;  // totals before this game
        public int Character;
        public List<Badge> NewBadges = new List<Badge>();
        public List<Best> NewBests = new List<Best>();
        public int WinStreak;               // after this game
        public List<(string Name, double Value, double Usual, bool HigherBetter, string Format)> VsUsual = new List<(string, double, double, bool, string)>();
    }

    public sealed class Career
    {
        public Dictionary<long, GameProgress> Games = new Dictionary<long, GameProgress>();
        public int Xp; public int Level => Progress.LevelOf(Xp).Level;
        public Dictionary<int, int> CharXp = new Dictionary<int, int>();
        public List<(Badge Badge, int Count, long LastGame)> Badges = new List<(Badge, int, long)>();   // feats: how many times
        public List<(Badge Badge, int Count, int Next, string Of)> Milestones = new List<(Badge, int, int, string)>();
        public Dictionary<string, Best> Bests = new Dictionary<string, Best>();
        public int WinStreak, BestWinStreak;
    }

    public static class Progress
    {
        /// <summary>A level from total XP: level n needs 400 + 100n XP to get to n+1. (level, XP into it, XP it needs)</summary>
        public static (int Level, int Into, int Need) LevelOf(int xp)
        {
            int level = 1;
            while (xp >= 400 + 100 * level) { xp -= 400 + 100 * level; level++; }
            return (level, xp, 400 + 100 * level);
        }

        public static string GradeOf(int score) => score >= 72 ? "S" : score >= 60 ? "A" : score >= 46 ? "B" : score >= 30 ? "C" : "D";

        static readonly Badge[] Feats =
        {
            new Badge { Id = "flawless", Name = "Flawless", What = "Won without losing a stock" },
            new Badge { Id = "comeback", Name = "Comeback Kid", What = "Won after being 2 stocks down" },
            new Badge { Id = "clutch", Name = "Clutch", What = "Won a last stock against last stock" },
            new Badge { Id = "zerodeath", Name = "Zero to Death", What = "Took a stock in one combo from 10% or less" },
            new Badge { Id = "early", Name = "Early Bird", What = "Took a stock under 60%" },
            new Badge { Id = "cockroach", Name = "Cockroach", What = "Lived through a hit at 160% or more" },
            new Badge { Id = "edgeguard", Name = "Gatekeeper", What = "Took a stock off the stage" },
            new Badge { Id = "combo", Name = "Combo Chef", What = "A combo of 7 hits or more" },
            new Badge { Id = "feast", Name = "Combo Feast", What = "A combo that did 60% or more" },
            new Badge { Id = "tech", Name = "Iron Wrists", What = "Teched 4+ times without missing one" },
            new Badge { Id = "lcancel", Name = "Butter Hands", What = "L-cancelled 90% of 8+ aerials" },
            new Badge { Id = "wavedash", Name = "Wavedash Wizard", What = "15 or more wavedashes in a game" },
            new Badge { Id = "speedrun", Name = "Speedrun", What = "Won in under 2 minutes" },
            new Badge { Id = "sgrade", Name = "S Rank", What = "Scored an S" },
        };

        static readonly (string Id, string Name, string Of, int[] Steps)[] MilestoneList =
        {
            ("games", "Grinder", "games played", new[] { 25, 100, 250, 500 }),
            ("wins", "Winner", "wins", new[] { 10, 50, 150, 300 }),
            ("kills", "Stock Taker", "stocks taken", new[] { 50, 250, 750, 1500 }),
            ("streak", "On Fire", "wins in a row (best)", new[] { 3, 5, 8, 12 }),
            ("chars", "Explorer", "characters played", new[] { 3, 8, 15, 25 }),
            ("rivals", "Rivalry", "opponents played 10+ times", new[] { 1, 3, 6, 10 }),
            ("badges", "Collector", "badges earned", new[] { 10, 50, 150, 400 }),
        };

        /// <summary>Everything, from your 1-on-1 games (oldest first) and what their replays say (by game id).</summary>
        public static Career Compute(List<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games, Dictionary<long, GameSmash> smash, Func<int, int, string> moveName = null)
        {
            var c = new Career();
            var featCount = Feats.ToDictionary(f => f.Id, f => (Count: 0, Last: 0L));
            var scores = new List<int>();
            var seenChars = new HashSet<int>(); var vsCount = new Dictionary<string, int>();
            int kills = 0, wins = 0, played = 0, badgesEarned = 0;
            var milestoneLevel = MilestoneList.ToDictionary(m => m.Id, m => 0);
            // your usual, as it was before each game (running sums)
            double sNeutral = 0, sDpo = 0, sOpk = 0, sLc = 0; int nNeutral = 0, nDpo = 0, nOpk = 0, nLc = 0;
            foreach (var x in games)
            {
                var gp = new GameProgress { GameId = x.Game.Id };
                smash.TryGetValue(x.Game.Id, out var s);
                bool won = x.Me.Won == true;
                played++; if (won) wins++;
                c.WinStreak = won ? c.WinStreak + 1 : 0; c.BestWinStreak = Math.Max(c.BestWinStreak, c.WinStreak); gp.WinStreak = c.WinStreak;
                seenChars.Add(x.Me.Character);
                if (x.Them.UserId != null) vsCount[x.Them.UserId] = vsCount.TryGetValue(x.Them.UserId, out var vc) ? vc + 1 : 1;

                // ---- the score
                double score = won ? 15 : 0;
                if (s != null)
                {
                    kills += s.Kills;
                    score += won ? Math.Min(15, (s.MyStocksLeft ?? 0) * 5) : Math.Min(15, s.Kills * 5);
                    double neutral = s.NeutralWin, dpo = s.DamagePerOpening, opk = s.OpeningsPerKill;
                    double uNeutral = nNeutral > 0 ? sNeutral / nNeutral : 0.5, uDpo = nDpo > 0 ? sDpo / nDpo : 12, uOpk = nOpk > 0 ? sOpk / nOpk : 10;
                    if (!double.IsNaN(neutral)) score += 25 * Clamp(0.5 + (neutral - uNeutral) * 2.5);
                    if (!double.IsNaN(dpo)) score += 15 * Clamp(0.5 + (dpo / uDpo - 1));
                    if (!double.IsNaN(opk)) score += 15 * Clamp(0.5 + (uOpk / opk - 1));
                    var hb = s.MyHabits;
                    var skill = new List<double>();
                    if (hb != null && hb.Known && hb.AerialLandings >= 3) skill.Add((double)hb.LCancels / hb.AerialLandings);
                    if (hb != null && hb.Known && hb.TechChances >= 2) skill.Add((double)hb.Techs / hb.TechChances);
                    score += 10 * (skill.Count > 0 ? skill.Average() : 0.5);
                    double lc = hb != null && hb.Known && hb.AerialLandings >= 3 ? (double)hb.LCancels / hb.AerialLandings : double.NaN;
                    gp.VsUsual.Add(("Neutral wins", neutral * 100, uNeutral * 100, true, "0'%'"));
                    gp.VsUsual.Add(("Openings per kill", opk, uOpk, false, "0.0"));
                    gp.VsUsual.Add(("Damage per opening", dpo, uDpo, true, "0.0'%'"));
                    if (!double.IsNaN(lc)) gp.VsUsual.Add(("L-cancels", lc * 100, nLc > 0 ? sLc / nLc * 100 : double.NaN, true, "0'%'"));
                    if (!double.IsNaN(neutral)) { sNeutral += neutral; nNeutral++; }
                    if (!double.IsNaN(dpo)) { sDpo += dpo; nDpo++; }
                    if (!double.IsNaN(opk)) { sOpk += opk; nOpk++; }
                    if (!double.IsNaN(lc)) { sLc += lc; nLc++; }
                    gp.Score = (int)Math.Round(Math.Max(0, Math.Min(100, score)));
                }
                else gp.Score = won ? 60 : 25;
                gp.Grade = GradeOf(gp.Score);
                gp.Percentile = scores.Count == 0 ? 50 : (int)Math.Round(100.0 * scores.Count(v => v < gp.Score) / scores.Count);
                scores.Add(gp.Score);

                // ---- feats
                void Feat(string id, bool ok) { if (!ok) return; var f = Feats.First(b => b.Id == id); gp.NewBadges.Add(f); var fc = featCount[id]; featCount[id] = (fc.Count + 1, x.Game.Id); badgesEarned++; }
                if (s != null)
                {
                    var hb = s.MyHabits;
                    Feat("flawless", won && s.Deaths == 0);
                    Feat("comeback", won && s.WorstDeficit >= 2);
                    Feat("clutch", s.LastStockClutch);
                    Feat("zerodeath", s.ZeroToDeaths > 0);
                    Feat("early", s.EarliestKill >= 0 && s.EarliestKill < 60);
                    Feat("cockroach", s.HighestPercent >= 160);
                    Feat("edgeguard", s.EdgeguardKills > 0);
                    Feat("combo", s.BestComboHits >= 7);
                    Feat("feast", s.BestComboDamage >= 60);
                    Feat("tech", hb != null && hb.Known && hb.Techs >= 4 && hb.MissedTech == 0);
                    Feat("lcancel", hb != null && hb.Known && hb.AerialLandings >= 8 && hb.LCancels >= hb.AerialLandings * 0.9);
                    Feat("wavedash", hb != null && hb.Known && hb.Wavedashes >= 15);
                    Feat("speedrun", won && s.Seconds < 120 && s.TheirStocksLeft == 0 && s.Kills >= 3);
                }
                Feat("sgrade", gp.Grade == "S");

                // ---- personal bests (only once there's something to beat: from the 5th game on)
                void Pb(string id, string name, string unit, double value, bool lower, string detail = null)
                {
                    if (double.IsNaN(value)) return;
                    if (!c.Bests.TryGetValue(id, out var b)) { c.Bests[id] = new Best { Id = id, Name = name, Unit = unit, Value = value, Lower = lower, GameId = x.Game.Id, Detail = detail }; return; }
                    if (lower ? value < b.Value : value > b.Value)
                    {
                        b.Value = value; b.GameId = x.Game.Id; b.Detail = detail;
                        if (played >= 5) gp.NewBests.Add(new Best { Id = id, Name = name, Unit = unit, Value = value, Lower = lower, GameId = x.Game.Id, Detail = detail });
                    }
                }
                if (s != null)
                {
                    string combo = moveName == null ? null : string.Join(" → ", s.BestComboMoves.Select(m => moveName(s.MyCharacter, m) ?? "projectile").Aggregate(new List<string>(), (l, m) => { if (l.Count == 0 || l[l.Count - 1] != m) l.Add(m); return l; }));
                    if (s.BestComboDamage > 0) Pb("combo", "Biggest combo", "%", s.BestComboDamage, false, combo);
                    if (s.BestComboHits > 0) Pb("hits", "Longest combo", "hits", s.BestComboHits, false, combo);
                    if (s.EarliestKill >= 0) Pb("early", "Earliest kill", "%", s.EarliestKill, true);
                    if (s.HighestPercent > 0) Pb("survive", "Highest % lived through", "%", s.HighestPercent, false);
                    if (won && s.TheirStocksLeft == 0 && s.Kills >= 3) Pb("fastest", "Fastest win", "time", s.Seconds, true);
                    Pb("damage", "Most damage in a game", "%", s.Dealt, false);
                }
                Pb("score", "Best score", "", gp.Score, false);
                Pb("streak", "Longest win streak", "wins", c.WinStreak, false);

                // ---- XP
                void Xp(string why, int v) { if (v > 0) gp.XpParts.Add((why, v)); }
                Xp("Played", 100);
                Xp("Won", won ? 150 : 0);
                if (s != null) Xp($"{s.Kills} stock{(s.Kills == 1 ? "" : "s")} taken", 25 * s.Kills);
                Xp($"Grade {gp.Grade}", gp.Grade == "S" ? 150 : gp.Grade == "A" ? 100 : gp.Grade == "B" ? 50 : gp.Grade == "C" ? 20 : 0);
                Xp($"{gp.NewBadges.Count} badge{(gp.NewBadges.Count == 1 ? "" : "s")}", 50 * gp.NewBadges.Count);
                Xp($"{gp.NewBests.Count} personal best{(gp.NewBests.Count == 1 ? "" : "s")}", 75 * gp.NewBests.Count);
                Xp(c.WinStreak >= 3 ? $"{c.WinStreak} wins in a row" : null, c.WinStreak >= 3 ? 25 * Math.Min(10, c.WinStreak) : 0);
                gp.Xp = gp.XpParts.Sum(p => p.Xp);
                gp.XpBefore = c.Xp; gp.Character = x.Me.Character; gp.LevelBefore = LevelOf(c.Xp).Level; c.Xp += gp.Xp; gp.LevelAfter = LevelOf(c.Xp).Level;
                c.CharXp.TryGetValue(x.Me.Character, out int cx);
                gp.CharXpBefore = cx; gp.CharLevelBefore = LevelOf(cx).Level; cx += gp.Xp; c.CharXp[x.Me.Character] = cx; gp.CharLevelAfter = LevelOf(cx).Level;

                // ---- milestones reached on this game
                var counts = new Dictionary<string, int>
                {
                    ["games"] = played, ["wins"] = wins, ["kills"] = kills, ["streak"] = c.BestWinStreak, ["chars"] = seenChars.Count,
                    ["rivals"] = vsCount.Values.Count(v => v >= 10), ["badges"] = badgesEarned,
                };
                foreach (var m in MilestoneList)
                {
                    int tier = m.Steps.Count(st => counts[m.Id] >= st);
                    if (tier > milestoneLevel[m.Id])
                    {
                        milestoneLevel[m.Id] = tier;
                        gp.NewBadges.Add(new Badge { Id = m.Id, Name = m.Name, Tier = tier, What = $"{m.Steps[tier - 1]} {m.Of}" });
                    }
                }
                c.Games[x.Game.Id] = gp;

                if (x.Equals(games[games.Count - 1]))
                    foreach (var m in MilestoneList)
                    {
                        int tier = milestoneLevel[m.Id];
                        c.Milestones.Add((new Badge { Id = m.Id, Name = m.Name, Tier = tier, What = m.Of }, counts[m.Id], tier < m.Steps.Length ? m.Steps[tier] : 0, m.Of));
                    }
            }
            c.Badges = Feats.Select(f => (f, featCount[f.Id].Count, featCount[f.Id].Last)).ToList();
            return c;
        }

        static double Clamp(double v) => Math.Max(0, Math.Min(1, v));
    }
}
