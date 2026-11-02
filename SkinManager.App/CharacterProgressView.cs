// The Stat Tracker's character page: click a character in Progress (Character mastery) and this shows everything about
// playing it: its level, record and grades, the badges earned with it, its personal bests, its best combos (each one can
// be watched), every move it landed (times, damage, kills, combos started), its tech and habits next to your usual,
// how its stocks are taken and lost, and its matchups and stages. All your games count here (like the rest of Progress).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class CharacterProgressView : ScrollView
    {
        public StatsData Data;
        public int? Character;
        public event Action Back;
        public event Action<string> WatchReplay;
        public event Action<long> OpenGame;

        readonly List<(RectangleF Box, Action Do)> _clicks = new List<(RectangleF, Action)>();
        RectangleF _hot;

        protected override void Clicked(Point p, MouseButtons b)
        {
            foreach (var c in _clicks.ToList()) if (c.Box.Contains(p)) { c.Do(); return; }
        }
        protected override void Hover(Point p)
        {
            var hot = _clicks.Where(c => c.Box.Contains(p)).Select(c => c.Box).DefaultIfEmpty(RectangleF.Empty).First();
            Cursor = hot.IsEmpty ? Cursors.Default : Cursors.Hand;
            if (hot != _hot) { _hot = hot; Invalidate(); }
        }

        void Link(Graphics g, string text, RectangleF r, Action act, bool button = false)
        {
            bool on = r == _hot;
            if (button)
            {
                using (var b = new SolidBrush(on ? Color.FromArgb(70, Theme.Accent) : Color.FromArgb(38, 255, 255, 255))) using (var p = StatsArt.Round(r, 5)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(on ? 220 : 120, Theme.Accent))) using (var p = StatsArt.Round(r, 5)) g.DrawPath(pen, p);
                Text(g, text, Theme.Small, on ? Theme.Text : Theme.Accent, new RectangleF(r.X, r.Y + (r.Height - 16) / 2, r.Width, 18), Center);
            }
            else Text(g, text, Theme.BodyBold, on ? Theme.Text : Theme.Accent, r);
            _clicks.Add((r, act));
        }

        protected override void Draw(Graphics g, int w)
        {
            _clicks.Clear();
            if (Data == null || Character == null) { ContentHeight = 0; return; }
            int c = Character.Value;
            var career = Data.Career;
            var games = Data.History.Singles().Where(x => x.Me.Character == c).OrderBy(x => x.Game.Start).ToList();
            var sm = games.Select(x => Data.Smash.TryGetValue(x.Game.Id, out var s) ? s : null).Where(s => s != null).ToList();
            var allSm = Data.Smash.Values.ToList();
            var prog = games.Select(x => career != null && career.Games.TryGetValue(x.Game.Id, out var p) ? p : null).Where(p => p != null).ToList();
            var rec = MatchHistory.Count(games);
            int y = 0;

            Link(g, "‹  All characters", new RectangleF(2, y, 160, 22), () => Back?.Invoke());
            y += 30;

            // ---- banner: the character, its level and record
            var top = new RectangleF(0, y, w, 156);
            Card(g, top);
            var por = Data.Portrait(c);
            if (por != null)
            {
                StatsArt.Cover(g, por, new RectangleF(w - 190, y + 2, 188, 152), 8, false, 0.5f, 0.1f);
                using (var p = StatsArt.Round(top, 8)) { g.SetClip(p); StatsArt.Shade(g, new RectangleF(w - 240, y, 120, 156), 215, 0, LinearGradientMode.Horizontal); g.ResetClip(); }
            }
            int xp = career != null && career.CharXp.TryGetValue(c, out var cx) ? cx : 0;
            var (level, into, need) = Progress.LevelOf(xp);
            int rank = career == null ? 0 : career.CharXp.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList().IndexOf(c) + 1;
            Caps(g, rank > 0 ? $"Character mastery · #{rank} of {career.CharXp.Count}" : "Character mastery", 16, y + 12, w - 220);
            Text(g, Fighters.Name(c), Theme.Bold(20f), Theme.Text, new RectangleF(15, y + 28, w - 230, 38));
            Text(g, $"Lv {level}", Theme.Bold(20f), Theme.Accent, new RectangleF(15, y + 68, 110, 38));
            var bar = new RectangleF(120, y + 88, Math.Max(80, w - 360), 10);
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) using (var p = StatsArt.Round(bar, 5)) g.FillPath(b, p);
            using (var b = new SolidBrush(Theme.Accent)) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(10, bar.Width * into / need), bar.Height), 5)) g.FillPath(b, p);
            double perGame = prog.Count == 0 ? 0 : prog.Skip(Math.Max(0, prog.Count - 20)).Average(p => p.Xp);
            Text(g, $"{into} / {need} XP to level {level + 1}" + (perGame > 0 ? $" · about {Math.Ceiling((need - into) / perGame):0} more games" : "") + $" · {xp:N0} XP in all", Theme.Small, Theme.Muted, new RectangleF(120, y + 68, bar.Width, 18));
            var time = TimeSpan.FromSeconds(games.Sum(x => (double)x.Game.Frames) / 60.0);
            string played = time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{time.Minutes}m" : $"{time.Seconds}s";
            Text(g, $"{rec} · {rec.Percent} wins · {Games(rec.Games)} · {played} played", Theme.BodyBold, Theme.Text, new RectangleF(16, y + 110, w - 230, 20));
            if (games.Count > 0)
                Text(g, $"First played {games[0].Game.Start.ToLocalTime():MMM d, yyyy} · last played {games[games.Count - 1].Game.Start.ToLocalTime():MMM d}", Theme.Small, Theme.Muted, new RectangleF(16, y + 132, w - 230, 18));
            y += (int)top.Height + 14;

            if (games.Count == 0) { ContentHeight = y; return; }

            // ---- quick numbers
            var sum = SmashSummary.Of(sm); var usual = SmashSummary.Of(allSm);
            var scores = prog.Select(p => (double)p.Score).ToList();
            string avgGrade = scores.Count > 0 ? Progress.GradeOf((int)Math.Round(scores.Average())) : "–";
            int bestStreak = 0, streak = 0; foreach (var x in games) { streak = x.Me.Won == true ? streak + 1 : 0; bestStreak = Math.Max(bestStreak, streak); }
            var tiles = new (string Title, string Value, string Sub)[]
            {
                ("Average grade", avgGrade, scores.Count > 0 ? $"score {scores.Average():0}" : ""),
                ("Stocks taken", sum.Kills.ToString("N0"), sm.Count > 0 ? $"{Num(sum.KillsPerGame, "0.0")} a game" : "needs replays"),
                ("Damage dealt", $"{sum.Dealt:N0}%", sm.Count > 0 ? $"{sum.Dealt / Math.Max(1, sm.Count):0}% a game" : "needs replays"),
                ("Neutral wins", Pct(sum.NeutralWin), "usual " + Pct(usual.NeutralWin)),
                ("Best win streak", bestStreak.ToString(), "with this character"),
            };
            int per = tiles.Length; float tw = (w - (per - 1) * 10f) / per;
            for (int i = 0; i < per; i++)
            {
                var r = new RectangleF(i * (tw + 10), y, tw, 78);
                Card(g, r);
                Caps(g, tiles[i].Title, r.X + 12, r.Y + 10, r.Width - 20);
                var col = i == 0 && avgGrade != "–" ? ReportArt.GradeColor(avgGrade) : Theme.Text;
                Text(g, tiles[i].Value, Theme.Bold(16f), col, new RectangleF(r.X + 11, r.Y + 26, r.Width - 20, 30));
                Text(g, tiles[i].Sub, Theme.Small, Theme.Muted, new RectangleF(r.X + 12, r.Y + 56, r.Width - 20, 18));
            }
            y += 92;

            // ---- grades over time
            {
                var r = new RectangleF(0, y, w, 120);
                Card(g, r);
                Caps(g, "Your grades with " + Fighters.Name(c), 14, y + 10, w * 0.4f);
                string[] letters = { "S", "A", "B", "C", "D" };
                int most = Math.Max(1, letters.Max(l => prog.Count(p => p.Grade == l)));
                float colW = Math.Min(44, w * 0.32f / 5);
                for (int i = 0; i < 5; i++)
                {
                    int n = prog.Count(p => p.Grade == letters[i]);
                    float bh = 46f * n / most, bx = 14 + i * colW;
                    using (var b = new SolidBrush(ReportArt.GradeColor(letters[i]))) g.FillRectangle(b, bx + colW * 0.2f, y + 80 - bh, colW * 0.6f, bh);
                    Text(g, letters[i], Theme.BodyBold, ReportArt.GradeColor(letters[i]), new RectangleF(bx, y + 82, colW, 18), Center);
                    Text(g, n.ToString(), Theme.Small, Theme.Muted, new RectangleF(bx, y + 98, colW, 18), Center);
                }
                float sx = 14 + 5 * colW + 30;
                var spark = new RectangleF(sx, y + 34, w - sx - 16, 62);
                Text(g, scores.Count >= 2 ? "score, game by game (rolling over 5)" : "a trend shows after a few graded games", Theme.Small, Theme.Muted, new RectangleF(sx, y + 10, spark.Width, 18));
                if (scores.Count >= 2) Spark(g, Rolling(scores, 5), spark, Theme.Accent);
                y += (int)r.Height + 14;
            }

            // ---- badges earned with this character
            var feats = career?.Badges.Select(b => b.Badge).ToList() ?? new List<Badge>();
            var counts = prog.SelectMany(p => p.NewBadges.Where(b => b.Tier == 0)).GroupBy(b => b.Id).ToDictionary(x => x.Key, x => x.Count());
            Caps(g, $"Badges with {Fighters.Name(c)} · {counts.Values.Sum()} earned", 2, y, w); y += 22;
            if (feats.Count > 0)
            {
                int bw = 168, bh = 92; int bper = Math.Max(1, (w + 10) / (bw + 10)); bw = (w - (bper - 1) * 10) / bper;
                var order = feats.OrderByDescending(f => counts.TryGetValue(f.Id, out var n) ? n : 0).ToList();
                for (int i = 0; i < order.Count; i++)
                {
                    var f = order[i]; counts.TryGetValue(f.Id, out int n);
                    var r = new RectangleF(i % bper * (bw + 10), y + i / bper * (bh + 10), bw, bh);
                    Card(g, r, n > 0 ? (Color?)null : Color.FromArgb(150, 26, 28, 36));
                    var medal = new RectangleF(r.X + 10, r.Y + 14, 38, 38);
                    if (n > 0) ReportArt.Medal(g, f, medal);
                    else using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash }) g.DrawEllipse(pen, medal);
                    float tx = r.X + 56, tww = r.Width - 62;
                    Text(g, f.Name, Theme.BodyBold, n > 0 ? Theme.Text : Theme.Muted, new RectangleF(tx, r.Y + 8, tww, 20));
                    Text(g, f.What, Theme.Small, Theme.Muted, new RectangleF(tx, r.Y + 28, tww, 42), new StringFormat { Trimming = StringTrimming.EllipsisWord });
                    Text(g, n > 0 ? $"×{n}" : "not yet", Theme.Small, n > 0 ? Theme.Accent : Theme.Muted, new RectangleF(tx, r.Y + 70, tww, 18));
                }
                y += (order.Count + bper - 1) / bper * (bh + 10) + 6;
            }
            else { Text(g, "Badges show once your replays are read.", Theme.Small, Theme.Muted, new RectangleF(4, y, w, 18)); y += 26; }

            // ---- personal bests with this character
            var bests = Bests(games, sm, prog, c);
            Caps(g, "Personal bests with " + Fighters.Name(c), 2, y, w); y += 22;
            {
                var bc = new RectangleF(0, y, w, 16 + Math.Max(1, bests.Count) * 26);
                Card(g, bc);
                float by = y + 8;
                if (bests.Count == 0) Text(g, "needs replays", Theme.Small, Theme.Muted, new RectangleF(14, by + 4, w - 28, 18));
                foreach (var b in bests)
                {
                    Text(g, b.Name, Theme.Body, Theme.Muted, new RectangleF(14, by + 3, w * 0.3f, 20));
                    Text(g, b.Value, Theme.BodyBold, Theme.Accent, new RectangleF(w * 0.3f, by + 3, w * 0.14f, 20));
                    Text(g, b.Detail, Theme.Small, Theme.Muted, new RectangleF(w * 0.44f, by + 5, w * 0.56f - 14, 18));
                    by += 26;
                }
                y += (int)bc.Height + 14;
            }

            // ---- best combos
            var combos = sm.SelectMany(s => s.Replay.Openings.Where(o => o.Attacker == s.Me && o.Hits >= 2).Select(o => (S: s, O: o)))
                           .OrderByDescending(x => x.O.Damage).ThenByDescending(x => x.O.Hits).Take(6).ToList();
            Caps(g, "Best combos", 2, y, w); y += 22;
            {
                var cc = new RectangleF(0, y, w, 16 + Math.Max(1, combos.Count) * 54);
                Card(g, cc);
                float cy = y + 8;
                if (combos.Count == 0) Text(g, "needs replays", Theme.Small, Theme.Muted, new RectangleF(14, cy + 4, w - 28, 18));
                for (int i = 0; i < combos.Count; i++)
                {
                    var (s, o) = combos[i];
                    var (names, start) = ComboMoves(s, o, c);
                    Text(g, $"#{i + 1}", Theme.Bold(12f), Theme.Muted, new RectangleF(14, cy + 6, 30, 22));
                    Text(g, $"{o.Damage:0}%", Theme.Bold(15f), Theme.Accent, new RectangleF(46, cy + 2, 80, 28));
                    Text(g, $"{o.Hits} hits" + (o.Killed ? " · KILL" : ""), Theme.Small, o.Killed ? Theme.Good : Theme.Muted, new RectangleF(46, cy + 28, 90, 18));
                    float mx = 140, mw = w - mx - 230;
                    Text(g, names, Theme.Body, Theme.Text, new RectangleF(mx, cy + 4, mw, 20));
                    Text(g, (double.IsNaN(start) ? "" : $"from {start:0}% · ") + $"vs {s.ThemPlayer.Name} ({Fighters.Name(s.TheirCharacter)}) · {s.Game.Start.ToLocalTime():MMM d}", Theme.Small, Theme.Muted, new RectangleF(mx, cy + 26, mw, 18));
                    Head(g, Data.Head(s.TheirCharacter), new RectangleF(w - 226, cy + 6, 30, 34));
                    var file = s.Game.File; long gid = s.Game.Id;
                    Link(g, "▶ Watch", new RectangleF(w - 186, cy + 10, 76, 26), () => WatchReplay?.Invoke(file), true);
                    if (Data.Games.Any(x => x.Game.Id == gid)) Link(g, "Open game", new RectangleF(w - 104, cy + 10, 90, 26), () => OpenGame?.Invoke(gid), true);
                    if (i < combos.Count - 1) using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255))) g.DrawLine(pen, 14, cy + 50, w - 14, cy + 50);
                    cy += 54;
                }
                y += (int)cc.Height + 14;
            }

            // ---- every move landed
            var moves = MoveTable(sm, c);
            Caps(g, $"Your moves as {Fighters.Name(c)}" + (sm.Count > 0 ? $" · from {sm.Count} replays" : ""), 2, y, w); y += 22;
            {
                var mc = new RectangleF(0, y, w, 40 + Math.Max(1, moves.Count) * 26);
                Card(g, mc);
                float c1 = w * 0.30f, c2 = w * 0.60f, c3 = w * 0.72f, c4 = w * 0.84f;
                Caps(g, "Move", 14, y + 12, c1);
                Caps(g, "Landed", c1, y + 12, 100);
                Text(g, "DAMAGE", Theme.Small, Theme.Muted, new RectangleF(c2, y + 12, c3 - c2 - 8, 18), Right);
                Text(g, "KILLS", Theme.Small, Theme.Muted, new RectangleF(c3, y + 12, c4 - c3 - 8, 18), Right);
                Text(g, "STARTED COMBOS", Theme.Small, Theme.Muted, new RectangleF(c4, y + 12, w - c4 - 14, 18), Right);
                float my = y + 36;
                if (moves.Count == 0) Text(g, "needs replays", Theme.Small, Theme.Muted, new RectangleF(14, my, w - 28, 18));
                int most = moves.Count == 0 ? 1 : Math.Max(1, moves.Max(m => m.Landed));
                foreach (var m in moves)
                {
                    bool special = m.Name.EndsWith(" B");
                    Text(g, m.Name, special ? Theme.BodyBold : Theme.Body, Theme.Text, new RectangleF(14, my + 1, c1 - 20, 20));
                    float bw = c2 - c1 - 60;
                    using (var b = new SolidBrush(special ? Theme.Accent : Color.FromArgb(160, Theme.Accent))) g.FillRectangle(b, c1, my + 7, Math.Max(2, bw * m.Landed / most), 8);
                    Text(g, m.Landed.ToString("N0"), Theme.Small, Theme.Text, new RectangleF(c1 + bw + 6, my + 2, 54, 18));
                    Text(g, $"{m.Damage:N0}%", Theme.Small, Theme.Muted, new RectangleF(c2, my + 2, c3 - c2 - 8, 18), Right);
                    Text(g, m.Kills > 0 ? m.Kills.ToString() : "–", Theme.Small, m.Kills > 0 ? Theme.Good : Theme.Muted, new RectangleF(c3, my + 2, c4 - c3 - 8, 18), Right);
                    Text(g, m.Openers > 0 ? m.Openers.ToString() : "–", Theme.Small, Theme.Muted, new RectangleF(c4, my + 2, w - c4 - 14, 18), Right);
                    my += 26;
                }
                y += (int)mc.Height + 14;
            }

            // ---- tech and habits, next to your usual (all your characters)
            var mine = new PlayerHabits(); int mineN = 0; foreach (var s in sm) if (s.MyHabits != null && s.MyHabits.Known) { mine.Add(s.MyHabits); mineN++; }
            var all = new PlayerHabits(); int allN = 0; foreach (var s in allSm) if (s.MyHabits != null && s.MyHabits.Known) { all.Add(s.MyHabits); allN++; }
            Caps(g, "Tech & habits" + (mineN > 0 ? $" · from {mineN} replays" : ""), 2, y, w); y += 22;
            {
                double R(int a, int b) => b == 0 ? double.NaN : 100.0 * a / b;
                double P(int a, int n) => n == 0 ? double.NaN : (double)a / n;
                var left = new (string Name, double Value, double Usual, string Fmt, string Unit)[]
                {
                    ("L-cancels", R(mine.LCancels, mine.AerialLandings), R(all.LCancels, all.AerialLandings), "0", "%"),
                    ("Techs made", R(mine.Techs, mine.TechChances), R(all.Techs, all.TechChances), "0", "%"),
                    ("Wavedashes a game", P(mine.Wavedashes, mineN), P(all.Wavedashes, allN), "0.0", ""),
                    ("Grabs that landed", R(mine.Grabs, mine.GrabTries), R(all.Grabs, all.GrabTries), "0", "%"),
                    ("Shields a game", P(mine.Shields, mineN), P(all.Shields, allN), "0.0", ""),
                    ("Spot dodges a game", P(mine.SpotDodges, mineN), P(all.SpotDodges, allN), "0.0", ""),
                    ("Rolls a game", P(mine.RollsToward + mine.RollsAway, mineN), P(all.RollsToward + all.RollsAway, allN), "0.0", ""),
                    ("Air dodges a game", P(mine.AirDodges, mineN), P(all.AirDodges, allN), "0.0", ""),
                    ("Time in the middle", R(mine.FramesCenter, mine.FramesAlive), R(all.FramesCenter, all.FramesAlive), "0", "%"),
                    ("Time off the stage", R(mine.FramesOffstage, mine.FramesAlive), R(all.FramesOffstage, all.FramesAlive), "0", "%"),
                };
                // (the same rule as the badges: a stock taken under 20% was given away, so it doesn't count)
                var kills = sm.Where(s => s.EarliestKill >= 0).Select(s => (double)s.EarliestKill).ToList();
                var right = new (string Name, double Value, double Usual, string Fmt, string Unit)[]
                {
                    ("Average kill %", sum.AvgKill, usual.AvgKill, "0", "%"),
                    ("Earliest kill", kills.Count > 0 ? kills.Min() : double.NaN, double.NaN, "0", "%"),
                    ("Average death %", sum.AvgDeath, usual.AvgDeath, "0", "%"),
                    ("Openings per kill", sum.OpeningsPerKill, usual.OpeningsPerKill, "0.0", ""),
                    ("Damage per opening", sum.DamagePerOpening, usual.DamagePerOpening, "0.0", "%"),
                    ("Edgeguard kills", sm.Sum(s => s.EdgeguardKills), double.NaN, "0", ""),
                    ("Zero to deaths", sm.Sum(s => s.ZeroToDeaths), double.NaN, "0", ""),
                    ("Your self-destructs", sm.Sum(s => s.SelfDestructs), double.NaN, "0", ""),
                    ("Throws: forward / back", mine.Throws == 0 ? double.NaN : 0, double.NaN, "", ""),
                    ("Throws: up / down", mine.Throws == 0 ? double.NaN : 0, double.NaN, "", ""),
                };
                string Throw(int a, int b) => mine.Throws == 0 ? "–" : $"{R(a, mine.Throws):0}% / {R(b, mine.Throws):0}%";
                float half = (w - 12) / 2f;
                float hh = 16 + Math.Max(left.Length, right.Length) * 26;
                void Column(RectangleF r, (string Name, double Value, double Usual, string Fmt, string Unit)[] rows)
                {
                    Card(g, r);
                    for (int i = 0; i < rows.Length; i++)
                    {
                        var x = rows[i]; float ry = r.Y + 8 + i * 26;
                        Text(g, x.Name, Theme.Body, Theme.Muted, new RectangleF(r.X + 14, ry + 2, r.Width * 0.5f, 20));
                        string v = x.Name == "Throws: forward / back" ? Throw(mine.ThrowF, mine.ThrowB) : x.Name == "Throws: up / down" ? Throw(mine.ThrowUp, mine.ThrowDown) : Num(x.Value, x.Fmt) + (double.IsNaN(x.Value) ? "" : x.Unit);
                        Text(g, v, Theme.BodyBold, Theme.Text, new RectangleF(r.X + r.Width * 0.5f, ry + 2, r.Width * 0.22f, 20));
                        if (!double.IsNaN(x.Usual)) Text(g, "usual " + Num(x.Usual, x.Fmt) + x.Unit, Theme.Small, Theme.Muted, new RectangleF(r.X + r.Width * 0.72f, ry + 4, r.Width * 0.28f - 12, 18));
                    }
                }
                Column(new RectangleF(0, y, half, hh), left);
                Column(new RectangleF(half + 12, y, half, hh), right);
                y += (int)hh + 14;

                // the ledge
                if (mine.LedgeOptions > 0)
                {
                    var lr = new RectangleF(0, y, w, 70);
                    Card(g, lr);
                    Caps(g, $"Off the ledge · {mine.LedgeOptions} times", 14, y + 10, w);
                    var parts = new[] { ("Getup", mine.LedgeGetup), ("Roll", mine.LedgeRoll), ("Attack", mine.LedgeAttack), ("Jump", mine.LedgeJump), ("Drop", mine.LedgeDrop) };
                    Color[] cols = { Theme.Accent, Color.FromArgb(96, 182, 255), Theme.Bad, Theme.Good, Color.FromArgb(178, 128, 255) };
                    float lx = 14, lw = w - 28;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        float pw = lw * parts[i].Item2 / mine.LedgeOptions; if (pw <= 0) continue;
                        using (var b = new SolidBrush(cols[i])) g.FillRectangle(b, lx, y + 32, pw, 10);
                        lx += pw;
                    }
                    // the key, every option with its share
                    float kx = 14, kw = (w - 28) / (float)parts.Length;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        using (var b = new SolidBrush(cols[i])) g.FillRectangle(b, kx + i * kw, y + 52, 10, 10);
                        Text(g, $"{parts[i].Item1} {100.0 * parts[i].Item2 / mine.LedgeOptions:0}% ({parts[i].Item2})", Theme.Small, Theme.Text, new RectangleF(kx + i * kw + 14, y + 48, kw - 16, 18));
                    }
                    y += (int)lr.Height + 14;
                }
            }

            // ---- matchups and stages
            {
                float half = (w - 12) / 2f;
                var mu = games.GroupBy(x => x.Them.Character).Select(x => (Key: x.Key, Rec: MatchHistory.Count(x))).OrderByDescending(x => x.Rec.Games).ThenByDescending(x => x.Rec.Rate).ToList();
                var st = games.GroupBy(x => x.Game.Stage).Select(x => (Key: x.Key, Rec: MatchHistory.Count(x))).OrderByDescending(x => x.Rec.Games).ThenByDescending(x => x.Rec.Rate).ToList();
                Caps(g, "Matchups", 2, y, half); Caps(g, "Stages", half + 14, y, half); y += 22;
                float hh = 16 + Math.Max(mu.Count, st.Count) * 40;
                var mr = new RectangleF(0, y, half, 16 + mu.Count * 40); var sr = new RectangleF(half + 12, y, half, 16 + st.Count * 40);
                Card(g, mr); Card(g, sr);
                for (int i = 0; i < mu.Count; i++)
                {
                    float ry = y + 8 + i * 40;
                    Head(g, Data.Head(mu[i].Key), new RectangleF(12, ry, 30, 34));
                    Text(g, "vs " + Fighters.Name(mu[i].Key), Theme.BodyBold, Theme.Text, new RectangleF(50, ry + 1, half - 130, 20));
                    Text(g, $"{mu[i].Rec} · {Games(mu[i].Rec.Games)}", Theme.Small, Theme.Muted, new RectangleF(50, ry + 19, half - 130, 18));
                    Text(g, mu[i].Rec.Percent, Theme.Bold(12f), RecordTable.RateColor(mu[i].Rec.Rate), new RectangleF(half - 74, ry + 7, 62, 22), Right);
                }
                for (int i = 0; i < st.Count; i++)
                {
                    float ry = y + 8 + i * 40;
                    var pic = Data.Stage(st[i].Key);
                    var pr = new RectangleF(sr.X + 12, ry + 2, 52, 30);
                    using (var bg = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) using (var p = StatsArt.Round(pr, 4)) g.FillPath(bg, p);
                    if (pic != null) StatsArt.Cover(g, pic, pr, 4);
                    Text(g, Data.History.StageName(st[i].Key), Theme.BodyBold, Theme.Text, new RectangleF(sr.X + 72, ry + 1, half - 150, 20));
                    Text(g, $"{st[i].Rec} · {Games(st[i].Rec.Games)}", Theme.Small, Theme.Muted, new RectangleF(sr.X + 72, ry + 19, half - 150, 18));
                    Text(g, st[i].Rec.Percent, Theme.Bold(12f), RecordTable.RateColor(st[i].Rec.Rate), new RectangleF(sr.Right - 74, ry + 7, 62, 22), Right);
                }
                y += (int)hh + 8;
            }
            ContentHeight = y;
        }

        static string Games(int n) => n == 1 ? "1 game" : $"{n} games";

        /// <summary>A combo's moves in order ("Up throw → Up air", a move hitting a few times in a row once) and their percent when it began.</summary>
        (string Moves, float Start) ComboMoves(GameSmash s, ReplayOpening o, int c)
        {
            var hits = s.Replay.Hits.Where(h => h.Attacker == s.Me && h.Victim == s.Them && h.Frame >= o.Start && h.Frame <= o.End).ToList();
            var names = new List<string>();
            foreach (var h in hits) { var n = Data.MoveName(c, h.Move) ?? "projectile"; if (names.Count == 0 || names[names.Count - 1] != n) names.Add(n); }
            return (string.Join(" → ", names), hits.Count > 0 ? hits[0].Before : float.NaN);
        }

        sealed class MoveRow { public string Name; public int Landed, Kills, Openers; public float Damage; }

        /// <summary>Every move this character landed in your games: times (a multi-hit move once), damage, kills, combos it started.</summary>
        List<MoveRow> MoveTable(List<GameSmash> sm, int c)
        {
            var rows = new Dictionary<string, MoveRow>();
            MoveRow Of(int sub) { var n = Data.MoveName(c, sub) ?? MoveNames.Other; if (!rows.TryGetValue(n, out var r)) rows[n] = r = new MoveRow { Name = n }; return r; }
            foreach (var s in sm)
            {
                foreach (var m in s.Landed) Of(m).Landed++;
                foreach (var m in s.KillMoves) Of(m).Kills++;
                foreach (var m in s.Openers) Of(m).Openers++;
                foreach (var h in s.Replay.Hits) if (h.Attacker == s.Me && h.Victim == s.Them) Of(h.Move).Damage += h.Damage;
            }
            return rows.Values.Where(r => r.Name != "Pummel" && (r.Landed > 0 || r.Kills > 0))
                       .OrderBy(r => r.Name == MoveNames.Other ? 1 : 0).ThenByDescending(r => r.Landed).ToList();
        }

        /// <summary>This character's bests in your games, with when and against whom.</summary>
        List<(string Name, string Value, string Detail)> Bests(List<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games, List<GameSmash> sm, List<GameProgress> prog, int c)
        {
            var o = new List<(string, string, string)>();
            string When(GameRecord gr, string them) => $"vs {them}, {gr.Start.ToLocalTime():MMM d}";
            if (sm.Count > 0)
            {
                var combos = sm.SelectMany(s => s.Replay.Openings.Where(x => x.Attacker == s.Me && x.Hits >= 2).Select(x => (S: s, O: x))).ToList();
                if (combos.Count > 0)
                {
                    var a = combos.OrderByDescending(x => x.O.Damage).ThenByDescending(x => x.O.Hits).First();
                    o.Add(("Biggest combo", $"{a.O.Damage:0}%", ComboMoves(a.S, a.O, c).Moves + " · " + When(a.S.Game, a.S.ThemPlayer.Name)));
                    var b = combos.OrderByDescending(x => x.O.Hits).ThenByDescending(x => x.O.Damage).First();
                    o.Add(("Longest combo", $"{b.O.Hits} hits", ComboMoves(b.S, b.O, c).Moves + " · " + When(b.S.Game, b.S.ThemPlayer.Name)));
                }
                var e = sm.Where(s => s.EarliestKill >= 0).OrderBy(s => s.EarliestKill).FirstOrDefault();
                if (e != null) o.Add(("Earliest kill", $"{e.EarliestKill}%", When(e.Game, e.ThemPlayer.Name)));
                var h = sm.OrderByDescending(s => s.HighestPercent).First();
                if (h.HighestPercent > 0) o.Add(("Highest % lived through", $"{h.HighestPercent:0}%", When(h.Game, h.ThemPlayer.Name)));
                var f = sm.Where(s => s.Won && s.TheirStocksLeft == 0 && s.Kills >= 3).OrderBy(s => s.Seconds).FirstOrDefault();
                if (f != null) o.Add(("Fastest win", $"{f.Seconds / 60}:{f.Seconds % 60:00}", When(f.Game, f.ThemPlayer.Name)));
                var d = sm.OrderByDescending(s => s.Dealt).First();
                o.Add(("Most damage in a game", $"{d.Dealt:0}%", When(d.Game, d.ThemPlayer.Name)));
                var k = sm.OrderByDescending(s => s.Kills).ThenBy(s => s.Seconds).First();
                o.Add(("Most stocks taken in a game", k.Kills.ToString(), When(k.Game, k.ThemPlayer.Name)));
            }
            var best = prog.OrderByDescending(p => p.Score).FirstOrDefault();
            if (best != null)
            {
                var gm = games.FirstOrDefault(x => x.Game.Id == best.GameId);
                o.Add(("Best score", $"{best.Score} ({best.Grade})", gm.Game != null ? When(gm.Game, gm.Them.Name) : ""));
            }
            return o;
        }
    }
}
