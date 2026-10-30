// The Stat Tracker's pictures: the overview (your form and your Smash stats as tiles), the characters you play as a
// chart (comfort picks and traps) with a card for each, and a simple list of your matches with a card for each game.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    /// <summary>A painted view that scrolls with the mouse wheel (and a thin bar that can be dragged).</summary>
    abstract class ScrollView : Control
    {
        protected int Scroll;
        protected int ContentHeight;
        bool _dragging; int _dragFrom, _scrollFrom;

        protected ScrollView()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
        }

        int MaxScroll => Math.Max(0, ContentHeight - Height);
        public void ToTop() { Scroll = 0; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (FindForm()?.ActiveControl is TextBox == false) Focus(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); SetScroll(Scroll - e.Delta / 2); }
        void SetScroll(int v) { int s = Math.Max(0, Math.Min(MaxScroll, v)); if (s != Scroll) { Scroll = s; Invalidate(); } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); SetScroll(Scroll); }

        RectangleF Thumb()
        {
            if (MaxScroll <= 0) return RectangleF.Empty;
            float h = Math.Max(30, Height * (float)Height / ContentHeight);
            return new RectangleF(Width - 7, (Height - h) * Scroll / MaxScroll, 5, h);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var t = Thumb();
            if (!t.IsEmpty && e.X >= Width - 12) { _dragging = true; _dragFrom = e.Y; _scrollFrom = Scroll; if (!t.Contains(e.X - 0f, e.Y)) SetScroll((int)((float)e.Y / Height * ContentHeight - Height / 2f)); _scrollFrom = Scroll; return; }
            Clicked(new Point(e.X, e.Y + Scroll), e.Button);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && MaxScroll > 0) { float range = Height - Thumb().Height; if (range > 0) SetScroll(_scrollFrom + (int)((e.Y - _dragFrom) / range * MaxScroll)); return; }
            Hover(new Point(e.X, e.Y + Scroll));
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hover(new Point(-1, -1)); }

        protected virtual void Layout2() { }
        protected virtual void Clicked(Point p, MouseButtons b) { }
        protected virtual void Hover(Point p) { }
        protected abstract void Draw(Graphics g, int width);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var state = g.Save();
            g.TranslateTransform(0, -Scroll);
            Draw(g, Width - (MaxScroll > 0 ? 10 : 0));
            g.Restore(state);
            var t = Thumb();
            if (!t.IsEmpty) using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) using (var p = StatsArt.Round(t, 2.5f)) g.FillPath(b, p);
        }

        // ----- painting helpers shared by the views -----

        protected static readonly Color CardBack = Color.FromArgb(215, 26, 28, 36);
        protected static readonly Color You = Theme.Accent, Them = Color.FromArgb(96, 182, 255);
        protected static readonly StringFormat Clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        protected static readonly StringFormat Right = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        protected static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        protected static void Card(Graphics g, RectangleF r, Color? back = null)
        {
            using (var b = new SolidBrush(back ?? CardBack)) using (var p = StatsArt.Round(r, 8)) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) using (var p = StatsArt.Round(r, 8)) g.DrawPath(pen, p);
        }
        protected static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt = null) { using (var b = new SolidBrush(c)) g.DrawString(s ?? "", f, b, r, fmt ?? Clip); }
        protected static void Caps(Graphics g, string s, float x, float y, float w, Color? c = null) => Text(g, (s ?? "").ToUpperInvariant(), Theme.Small, c ?? Theme.Muted, new RectangleF(x, y, w, 18));
        protected static void Head(Graphics g, Image img, RectangleF r, bool mirror = false)
        {
            using (var bg = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) using (var p = StatsArt.Round(r, 5)) g.FillPath(bg, p);
            if (img != null) StatsArt.Cover(g, img, r, 5, mirror, 0.5f, 0f);
        }
        protected static string Num(double v, string fmt) => double.IsNaN(v) || double.IsInfinity(v) ? "–" : v.ToString(fmt);
        protected static string Pct(double v) => double.IsNaN(v) ? "–" : $"{v * 100:0}%";

        /// <summary>A small line chart of values (NaN = no value), scaled to its own range.</summary>
        protected static void Spark(Graphics g, IList<double> values, RectangleF r, Color c)
        {
            var pts = new List<PointF>();
            var real = values.Where(v => !double.IsNaN(v)).ToList();
            if (real.Count < 2) return;
            double lo = real.Min(), hi = real.Max(); if (hi - lo < 1e-6) { hi += 1; lo -= 1; }
            for (int i = 0; i < values.Count; i++)
                if (!double.IsNaN(values[i])) pts.Add(new PointF(r.X + r.Width * i / Math.Max(1, values.Count - 1), r.Bottom - (float)((values[i] - lo) / (hi - lo)) * r.Height));
            if (pts.Count < 2) return;
            var area = new List<PointF>(pts) { new PointF(pts.Last().X, r.Bottom), new PointF(pts[0].X, r.Bottom) };
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(70, c), Color.FromArgb(0, c), LinearGradientMode.Vertical)) g.FillPolygon(b, area.ToArray());
            using (var pen = new Pen(c, 1.8f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts.ToArray());
        }

        /// <summary>A rolling average (over the last n values that have one).</summary>
        protected static List<double> Rolling(IList<double> v, int n)
        {
            var o = new List<double>();
            for (int i = 0; i < v.Count; i++)
            {
                var w = v.Take(i + 1).Where(x => !double.IsNaN(x)).Reverse().Take(n).ToList();
                o.Add(w.Count == 0 ? double.NaN : w.Average());
            }
            return o;
        }
    }

    /// <summary>What the views are given: your games (oldest first, the filter applied), what replays say about them,
    /// the pictures, and move names.</summary>
    sealed class StatsData
    {
        public MatchHistory History;
        public List<(GameRecord Game, GamePlayer Me, GamePlayer Them)> Games = new List<(GameRecord, GamePlayer, GamePlayer)>();
        public Dictionary<long, GameSmash> Smash = new Dictionary<long, GameSmash>();
        public Func<int?, Image> Head = _ => null, Portrait = _ => null, Stage = _ => null;
        public Func<int, int, string> MoveName = (c, s) => null;
        public Func<long, GameCoach> Coach = _ => null;      // DI tips, fun facts, a combo idea (null while being worked out)
        public bool ReplaysLoading; public int ReplaysRead, ReplaysTotal;
        public Career Career;                       // progress over all your games (not just the filter's)
        public PlayerHabits Typical; public int TypicalGames;   // everyone you've played, added up
        public List<GameSmash> SmashGames => Games.Select(g => Smash.TryGetValue(g.Game.Id, out var s) ? s : null).Where(s => s != null).ToList();
        /// <summary>Counted move names, without the ones that only fill lists up (pummels, "other").</summary>
        public List<(string Name, int Count)> Moves(IEnumerable<(int Character, int Sub)> moves) =>
            MoveNames.Count(moves, MoveName).Where(m => m.Name != MoveNames.Other && m.Name != "Pummel").ToList();
        public string ReplayNote => ReplaysLoading ? $"Reading your replays for Smash stats… {ReplaysRead} of {ReplaysTotal}" : null;
    }

    // ============================================================== overview

    class OverviewView : ScrollView
    {
        public StatsData Data;

        sealed class Tile { public string Title, Value, Sub, Delta; public bool? Better; public List<double> Spark; public double? Ring; }

        protected override void Draw(Graphics g, int w)
        {
            if (Data == null) return;
            var games = Data.Games;
            int y = 0;
            if (games.Count == 0) { Text(g, "No 1-on-1 games here yet. Play some online and they show up here.", Theme.Body, Theme.Muted, new RectangleF(4, 8, w, 24)); ContentHeight = 40; return; }

            // ---- form: your win rate over your recent games
            var form = new RectangleF(0, y, w, 196);
            Card(g, form);
            var last = games.Skip(Math.Max(0, games.Count - 60)).ToList();
            var all = MatchHistory.Count(games);
            var recent = MatchHistory.Count(games.Skip(Math.Max(0, games.Count - 20)));
            Caps(g, $"Form · your last {last.Count} games", 16, y + 12, 300);
            Text(g, Pct(recent.Rate), Theme.Bold(22f), RecordTable.RateColor(recent.Rate), new RectangleF(w - 340, y + 6, 120, 36), Right);
            Text(g, "last 20 games", Theme.Small, Theme.Muted, new RectangleF(w - 216, y + 10, 200, 18));
            double diff = recent.Rate - all.Rate;
            Text(g, Math.Abs(diff) < 0.02 ? "about your usual" : $"{(diff > 0 ? "▲" : "▼")} {Math.Abs(diff) * 100:0}% vs your usual {all.Percent}", Theme.Small, Math.Abs(diff) < 0.02 ? Theme.Muted : diff > 0 ? Theme.Good : Theme.Bad, new RectangleF(w - 216, y + 26, 204, 18));
            var chart = new RectangleF(52, y + 50, w - 72, 100);
            using (var pen = new Pen(Color.FromArgb(45, 255, 255, 255))) foreach (var p in new[] { 0, 0.25, 0.5, 0.75, 1 }) g.DrawLine(pen, chart.X, chart.Bottom - (float)p * chart.Height, chart.Right, chart.Bottom - (float)p * chart.Height);
            using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255)) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, chart.X, chart.Bottom - chart.Height / 2, chart.Right, chart.Bottom - chart.Height / 2);
            foreach (var p in new[] { 0, 50, 100 }) Text(g, p + "%", Theme.Small, Theme.Muted, new RectangleF(4, chart.Bottom - p / 100f * chart.Height - 9, 44, 18), Right);
            // rolling win rate (10 games), each point the record of the 10 games up to it
            var idx = games.Count - last.Count;
            var rate = new List<double>();
            for (int i = 0; i < last.Count; i++) { var win = games.Skip(Math.Max(0, idx + i - 9)).Take(Math.Min(10, idx + i + 1)); rate.Add(MatchHistory.Count(win).Rate); }
            var pts = rate.Select((r, i) => new PointF(chart.X + chart.Width * (last.Count == 1 ? 0.5f : (float)i / (last.Count - 1)), chart.Bottom - (float)r * chart.Height)).ToArray();
            if (pts.Length >= 2)
            {
                var area = pts.Concat(new[] { new PointF(pts.Last().X, chart.Bottom), new PointF(pts[0].X, chart.Bottom) }).ToArray();
                using (var b = new LinearGradientBrush(new RectangleF(chart.X, chart.Y - 1, chart.Width, chart.Height + 2), Color.FromArgb(90, Theme.Accent), Color.FromArgb(0, Theme.Accent), LinearGradientMode.Vertical)) g.FillPolygon(b, area);
                using (var pen = new Pen(Theme.Accent, 2.2f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
            }
            // each game: a square, green won, red lost
            float sq = Math.Min(10, chart.Width / last.Count - 2);
            for (int i = 0; i < last.Count; i++)
            {
                float x = chart.X + (chart.Width - sq) * (last.Count == 1 ? 0.5f : (float)i / (last.Count - 1));
                using (var b = new SolidBrush(last[i].Me.Won == true ? Theme.Good : Theme.Bad)) g.FillRectangle(b, x, chart.Bottom + 12, sq, sq);
            }
            Text(g, "older", Theme.Small, Theme.Muted, new RectangleF(chart.X, chart.Bottom + 24, 80, 18));
            Text(g, "latest", Theme.Small, Theme.Muted, new RectangleF(chart.Right - 80, chart.Bottom + 24, 80, 18), Right);
            y += (int)form.Height + 14;

            // ---- your Smash stats, lately against usual
            var sm = Data.SmashGames;
            var smRecent = sm.Skip(Math.Max(0, sm.Count - 20)).ToList();
            var sAll = SmashSummary.Of(sm); var sNow = SmashSummary.Of(smRecent);
            List<double> Per(Func<GameSmash, double> f) => Rolling(sm.Skip(Math.Max(0, sm.Count - 30)).Select(f).ToList(), 5);
            Tile Make(string title, Func<SmashSummary, double> f, Func<GameSmash, double> per, string fmt, bool higherBetter, string unit = "", Func<double, string> sub = null)
            {
                double now = f(sNow), usual = f(sAll), d = now - usual;
                bool same = double.IsNaN(d) || Math.Abs(d) < Math.Abs(usual) * 0.04;
                return new Tile
                {
                    Title = title, Value = Num(now, fmt) + (double.IsNaN(now) ? "" : unit), Sub = sub?.Invoke(now) ?? $"usual {Num(usual, fmt)}{unit}",
                    Delta = same ? "about your usual" : $"{(d > 0 ? "▲" : "▼")} {Num(Math.Abs(d), fmt)}{unit} vs usual", Better = same ? (bool?)null : (d > 0) == higherBetter, Spark = Per(per),
                };
            }
            var tiles = new List<Tile>
            {
                new Tile { Title = "Win rate · last 20", Value = recent.Percent, Sub = $"{recent} · overall {all.Percent}", Ring = recent.Rate, Better = Math.Abs(diff) < 0.02 ? (bool?)null : diff > 0, Delta = Math.Abs(diff) < 0.02 ? "about your usual" : $"{(diff > 0 ? "▲" : "▼")} {Math.Abs(diff) * 100:0}% vs usual", Spark = rate.Skip(Math.Max(0, rate.Count - 30)).ToList() },
                Make("Openings per kill", s => s.OpeningsPerKill, x => x.OpeningsPerKill, "0.0", false, "", v => double.IsNaN(v) ? "" : $"{1 / v:0.00} kills per opening · fewer is better"),
                Make("Damage per opening", s => s.DamagePerOpening, x => x.DamagePerOpening, "0.0", true, "%"),
                Make("Neutral wins", s => s.NeutralWin * 100, x => x.NeutralWin * 100, "0", true, "%", v => "of the openings were yours"),
                Make("Average kill %", s => s.AvgKill, x => x.AvgKill, "0", false, "%", v => "the % you take stocks at · lower is better"),
                Make("Average death %", s => s.AvgDeath, x => x.AvgDeath, "0", true, "%", v => "the % you lose stocks at · higher is better"),
            };
            Caps(g, sm.Count == 0 ? "Smash stats" : $"Smash stats · your last {smRecent.Count} games with replays", 2, y, 400);
            if (Data.ReplayNote != null) Text(g, Data.ReplayNote, Theme.Small, Theme.Accent, new RectangleF(w - 360, y, 360, 18), Right);
            y += 22;
            int cols = w >= 900 ? 3 : 2, tw = (w - (cols - 1) * 12) / cols, th = 112;
            for (int i = 0; i < tiles.Count; i++)
            {
                var r = new RectangleF(i % cols * (tw + 12), y + i / cols * (th + 12), tw, th);
                DrawTile(g, tiles[i], r, sm.Count == 0 && i > 0);
            }
            y += (tiles.Count + cols - 1) / cols * (th + 12) + 4;

            // ---- habits: what you do with your hands (and what others could read on you)
            var mineH = new PlayerHabits(); int hGames = 0;
            foreach (var g2 in sm) if (g2.MyHabits != null && g2.MyHabits.Known) { mineH.Add(g2.MyHabits); hGames++; }
            if (hGames > 0)
            {
                Caps(g, $"Your habits · from {hGames} replays", 2, y, 400); y += 22;
                var typ = Data.Typical ?? new PlayerHabits(); int tg = Math.Max(1, Data.TypicalGames);
                string Rate(int a, int b) => b == 0 ? "–" : $"{100.0 * a / b:0}%";
                var hTiles = new List<(string Title, string Value, string Sub, (string, int, Color)[] Split)>
                {
                    ("Tech rate", Rate(mineH.Techs, mineH.TechChances), $"opponents {Rate(typ.Techs, typ.TechChances)}",
                        new[] { ("in place", mineH.TechInPlace, Theme.Accent), ("in", mineH.TechToward, Theme.Good), ("away", mineH.TechAway, Them), ("missed", mineH.MissedTech, Theme.Bad) }),
                    ("Off the ledge", mineH.LedgeOptions == 0 ? "–" : new[] { ("getup", mineH.LedgeGetup), ("roll", mineH.LedgeRoll), ("attack", mineH.LedgeAttack), ("jump", mineH.LedgeJump), ("drop", mineH.LedgeDrop) }.OrderByDescending(v => v.Item2).First().Item1,
                        $"most of the time ({mineH.LedgeOptions} times)",
                        new[] { ("getup", mineH.LedgeGetup, Theme.Accent), ("roll", mineH.LedgeRoll, Them), ("attack", mineH.LedgeAttack, Theme.Bad), ("jump", mineH.LedgeJump, Theme.Good), ("drop", mineH.LedgeDrop, Theme.Muted) }),
                    ("L-cancels", Rate(mineH.LCancels, mineH.AerialLandings), $"of {mineH.AerialLandings} aerial landings · opponents {Rate(typ.LCancels, typ.AerialLandings)}", null),
                    ("Movement", $"{(double)mineH.Wavedashes / hGames:0.#} wavedashes", $"a game · {Rate(mineH.FramesCenter, mineH.FramesAlive)} of the time mid-stage, {Rate(mineH.FramesOffstage, mineH.FramesAlive)} off it", null),
                    ("Defense, a game", $"{(double)mineH.Shields / hGames:0} shields", $"{(double)(mineH.RollsToward + mineH.RollsAway) / hGames:0.#} rolls ({Rate(mineH.RollsAway, mineH.RollsToward + mineH.RollsAway)} away) · {(double)mineH.SpotDodges / hGames:0.#} spot dodges · {(double)mineH.AirDodges / hGames:0.#} air dodges", null),
                    ("Grabs", Rate(mineH.Grabs, mineH.GrabTries), $"of {mineH.GrabTries} grabs land · throws most: " + (mineH.Throws == 0 ? "–" : new[] { ("forward", mineH.ThrowF), ("back", mineH.ThrowB), ("up", mineH.ThrowUp), ("down", mineH.ThrowDown) }.OrderByDescending(v => v.Item2).First().Item1), null),
                };
                for (int i = 0; i < hTiles.Count; i++)
                {
                    var r = new RectangleF(i % cols * (tw + 12), y + i / cols * (106 + 12), tw, 106);
                    var t = hTiles[i];
                    Card(g, r);
                    Caps(g, t.Title, r.X + 14, r.Y + 10, r.Width - 28);
                    Text(g, t.Value, Theme.Bold(17f), Theme.Text, new RectangleF(r.X + 13, r.Y + 28, r.Width - 26, 30));
                    Text(g, t.Sub, Theme.Small, Theme.Muted, new RectangleF(r.X + 14, r.Y + 56, r.Width - 28, 18));
                    if (t.Split != null)
                    {
                        int total = t.Split.Sum(v => v.Item2); float bx = r.X + 14, bw = r.Width - 28;
                        if (total > 0)
                            foreach (var v in t.Split)
                            {
                                float sw = bw * v.Item2 / total; if (sw < 1) continue;
                                using (var b = new SolidBrush(v.Item3)) g.FillRectangle(b, bx, r.Y + 78, sw - 1, 6);
                                if (sw > 40) Text(g, v.Item1, Theme.Small, v.Item3, new RectangleF(bx, r.Y + 86, sw, 16));
                                bx += sw;
                            }
                    }
                }
                y += (hTiles.Count + cols - 1) / cols * (106 + 12) + 4;
                var read = Scouting.Lines(mineH, hGames, Data.Typical, Data.TypicalGames, 4);
                if (read.Count > 0)
                {
                    var r = new RectangleF(0, y, w, 34 + read.Count * 22);
                    Card(g, r);
                    Caps(g, "What opponents could read on you", 14, y + 10, w - 28, Theme.Accent);
                    float yy = y + 32;
                    foreach (var l in read) { Text(g, "• " + l.Habit, Theme.Body, Theme.Text, new RectangleF(14, yy, w - 28, 20)); yy += 22; }
                    y += (int)r.Height + 14;
                }
            }

            // ---- signatures: your moves, and what beats you
            Caps(g, "Your game in a few lines", 2, y, 400); y += 22;
            var picks = new List<(string Title, string Big, string Small, int? Char)>();
            var kills = Data.Moves(sm.SelectMany(s => s.KillMoves.Select(m => (s.MyCharacter, m))));
            var landed = Data.Moves(sm.SelectMany(s => s.Landed.Select(m => (s.MyCharacter, m))));
            var openers = Data.Moves(sm.SelectMany(s => s.Openers.Select(m => (s.MyCharacter, m))));
            var deaths = Data.Moves(sm.SelectMany(s => s.DeathMoves.Select(m => (s.TheirCharacter, m))));
            int totalKills = sm.Sum(s => s.KillMoves.Count), totalDeaths = sm.Sum(s => s.DeathMoves.Count);
            picks.Add(("Favorite kill move", kills.Count > 0 ? kills[0].Name : "–", kills.Count > 0 ? $"{kills[0].Count} of your {totalKills} kills" + (kills.Count > 1 ? $" · then {kills[1].Name}" : "") : "needs replays", null));
            picks.Add(("Lands most", landed.Count > 0 ? landed[0].Name : "–", landed.Count > 1 ? $"{landed[0].Count} times · then {landed[1].Name}, {(landed.Count > 2 ? landed[2].Name : "")}".TrimEnd(',', ' ') : "needs replays", null));
            picks.Add(("Starts openings with", openers.Count > 0 ? openers[0].Name : "–", openers.Count > 0 ? $"{openers[0].Count} of {sm.Sum(s => s.MyOpenings)} openings" : "needs replays", null));
            picks.Add(("Kills you most", deaths.Count > 0 ? deaths[0].Name : "–", deaths.Count > 0 ? $"{deaths[0].Count} of your {totalDeaths} deaths" + (deaths.Count > 1 ? $" · then {deaths[1].Name}" : "") : "needs replays", null));
            var mine = games.GroupBy(x => x.Me.Character).Select(x => (x.Key, Rec: MatchHistory.Count(x))).Where(x => x.Rec.Games >= 5).OrderByDescending(x => (x.Rec.Wins + 4.0) / (x.Rec.Games + 8)).ToList();
            var vs = games.GroupBy(x => x.Them.Character).Select(x => (x.Key, Rec: MatchHistory.Count(x))).Where(x => x.Rec.Games >= 5).OrderBy(x => (x.Rec.Wins + 4.0) / (x.Rec.Games + 8)).ToList();
            picks.Add(("Your best character", mine.Count > 0 ? Fighters.Name(mine[0].Key) : "–", mine.Count > 0 ? $"{mine[0].Rec} ({mine[0].Rec.Percent})" : "5+ games with one needed", mine.Count > 0 ? mine[0].Key : (int?)null));
            picks.Add(("Hardest to face", vs.Count > 0 ? Fighters.Name(vs[0].Key) : "–", vs.Count > 0 ? $"{vs[0].Rec} ({vs[0].Rec.Percent})" : "5+ games against one needed", vs.Count > 0 ? vs[0].Key : (int?)null));
            int ph = 74;
            for (int i = 0; i < picks.Count; i++)
            {
                var r = new RectangleF(i % cols * (tw + 12), y + i / cols * (ph + 12), tw, ph);
                Card(g, r);
                float tx = r.X + 14;
                if (picks[i].Char != null) { Head(g, Data.Head(picks[i].Char), new RectangleF(r.X + 12, r.Y + 14, 40, 46)); tx = r.X + 64; }
                Caps(g, picks[i].Title, tx, r.Y + 10, r.Right - tx - 10);
                Text(g, picks[i].Big, Theme.Bold(13f), Theme.Text, new RectangleF(tx, r.Y + 27, r.Right - tx - 10, 26));
                Text(g, picks[i].Small, Theme.Small, Theme.Muted, new RectangleF(tx, r.Y + 50, r.Right - tx - 10, 18));
            }
            y += (picks.Count + cols - 1) / cols * (ph + 12);
            ContentHeight = y + 8;
        }

        static void DrawTile(Graphics g, Tile t, RectangleF r, bool waiting)
        {
            Card(g, r);
            Caps(g, t.Title, r.X + 14, r.Y + 10, r.Width - 28);
            float vx = r.X + 14;
            if (t.Ring != null)
            {
                var ring = new RectangleF(r.X + 14, r.Y + 34, 50, 50);
                using (var pen = new Pen(Color.FromArgb(55, 255, 255, 255), 6)) g.DrawEllipse(pen, ring);
                using (var pen = new Pen(RecordTable.RateColor(t.Ring.Value), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) if (t.Ring > 0.005) g.DrawArc(pen, ring, -90, (float)(360 * Math.Min(1, t.Ring.Value)));
                vx = ring.Right + 14;
            }
            Text(g, waiting ? "…" : t.Value, Theme.Bold(20f), Theme.Text, new RectangleF(vx, r.Y + 30, r.Width * 0.5f, 36));
            var deltaColor = t.Better == null ? Theme.Muted : t.Better.Value ? Theme.Good : Theme.Bad;
            if (!waiting) Text(g, t.Delta, Theme.Small, deltaColor, new RectangleF(vx, r.Y + 66, r.Right - vx - 10, 18));
            Text(g, waiting ? "appears once your replays are read" : t.Sub, Theme.Small, Theme.Muted, new RectangleF(r.X + 14, r.Bottom - 24, r.Width - 28, 18));
            if (!waiting && t.Spark != null) Spark(g, t.Spark, new RectangleF(r.X + r.Width * 0.55f, r.Y + 34, r.Width * 0.45f - 16, 40), t.Better == false ? Theme.Bad : t.Better == true ? Theme.Good : Theme.Accent);
        }
    }

    // ============================================================== characters

    /// <summary>Each character you've played: games played across, win rate up; click one for its card.</summary>
    class CharacterChart : Control
    {
        public StatsData Data;
        public int? Selected;
        public event Action<int> Picked;
        static readonly Color Learning = Color.FromArgb(96, 182, 255);
        readonly List<(int Char, RectangleF Box)> _spots = new List<(int, RectangleF)>();
        int? _hover;

        public CharacterChart()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        int? At(Point p) { foreach (var s in _spots.AsEnumerable().Reverse()) if (s.Box.Contains(p)) return s.Char; return null; }
        protected override void OnMouseMove(MouseEventArgs e) { var h = At(e.Location); if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { _hover = null; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { var h = At(e.Location); if (h != null) { Selected = h; Picked?.Invoke(h.Value); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Color.FromArgb(215, 26, 28, 36))) using (var p = StatsArt.Round(box, 8)) g.FillPath(b, p);
            _spots.Clear();
            if (Data == null) return;
            // how well you play each one: the average score of its games (a score already compares a game with your own
            // usual, so a strong opponent or a loss you played well in doesn't sink it), pulled towards your overall
            // average when there are few games. Win rate only when the scores aren't known yet.
            var career = Data.Career;
            double Score(GameRecord gr) => career != null && career.Games.TryGetValue(gr.Id, out var p) ? p.Score : double.NaN;
            var allScores = Data.Games.Select(x => Score(x.Game)).Where(v => !double.IsNaN(v)).ToList();
            bool scored = allScores.Count > 0;
            double usual = scored ? allScores.Average() : MatchHistory.Count(Data.Games).Rate * 100;
            const double prior = 5;
            var chars = Data.Games.GroupBy(x => x.Me.Character).Select(x =>
            {
                var list = x.OrderBy(v => v.Game.Start).ToList();
                var sc = list.Select(v => Score(v.Game)).Where(v => !double.IsNaN(v)).ToList();
                var rec = MatchHistory.Count(list);
                double raw = scored && sc.Count > 0 ? sc.Average() : rec.Rate * 100;
                int n = scored ? sc.Count : rec.Games;
                double value = (raw * n + usual * prior) / (n + prior);
                // getting better: its last 8 games against the ones before (10+ games)
                bool better = sc.Count >= 10 && sc.Skip(sc.Count - 8).Average() > sc.Take(sc.Count - 8).Average() + 4;
                return (Char: x.Key, Rec: rec, Value: value, Raw: raw, Better: better);
            }).ToList();
            using (var t = new SolidBrush(Theme.Muted)) g.DrawString(scored ? "YOUR CHARACTERS · GAMES PLAYED → · HOW WELL YOU PLAY THEM ↑" : "YOUR CHARACTERS · GAMES PLAYED → · WIN RATE ↑", Theme.Small, t, 14, 10);
            if (chars.Count == 0) return;
            var plot = new RectangleF(92, 80, Width - 124, Height - 200);
            int most = Math.Max(4, chars.Max(c => c.Rec.Games));
            float X(int games) => plot.X + plot.Width * (float)(Math.Log(games) / Math.Log(most * 1.15));
            // the middle is your own usual; the scale fits your characters (at least 15 points each way)
            double spread = Math.Max(6, chars.Max(c => Math.Abs(c.Value - usual)) * 1.15);
            float Y(double v) => plot.Bottom - (float)((v - usual + spread) / (2 * spread)) * plot.Height;
            // the four corners
            var sorted = chars.Select(c => c.Rec.Games).OrderBy(n => n).ToList();
            int split = Math.Max(3, sorted[sorted.Count / 2]);
            float sx = X(split), sy = Y(usual);
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255)) { DashStyle = DashStyle.Dash }) { g.DrawLine(pen, sx, plot.Y, sx, plot.Bottom); g.DrawLine(pen, plot.X, sy, plot.Right, sy); }
            using (var pen = new Pen(Color.FromArgb(45, 255, 255, 255))) g.DrawRectangle(pen, plot.X, plot.Y, plot.Width, plot.Height);
            void Corner(string s, float x, float y, bool right, Color c) { using (var b = new SolidBrush(Color.FromArgb(170, c))) g.DrawString(s, Theme.Small, b, new RectangleF(right ? x - 200 : x, y, 200, 18), new StringFormat { Alignment = right ? StringAlignment.Far : StringAlignment.Near }); }
            Corner("YOUR MAINS", plot.Right, plot.Y - 46, true, Theme.Good);
            Corner("PUTTING IN THE WORK", plot.Right, plot.Bottom + 46, true, Learning);
            Corner("HIDDEN GEMS", plot.X, plot.Y - 46, false, Theme.Accent);
            Corner("STILL NEW", plot.X, plot.Bottom + 46, false, Theme.Muted);
            using (var m = new SolidBrush(Theme.Muted))
            {
                var right = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString("better", Theme.Small, m, new RectangleF(0, plot.Y - 9, 80, 18), right);
                g.DrawString("your usual", Theme.Small, m, new RectangleF(0, sy - 9, 80, 18), right);
                g.DrawString("tougher", Theme.Small, m, new RectangleF(0, plot.Bottom - 9, 80, 18), right);
                foreach (var n in new[] { 1, 3, 10, 30, 100, 300 }.Where(n => n <= most * 1.15)) g.DrawString(n.ToString(), Theme.Small, m, X(n), plot.Bottom + 26, new StringFormat { Alignment = StringAlignment.Center });
                g.DrawString("games played", Theme.Small, m, new RectangleF(plot.X, plot.Bottom + 46, plot.Width, 18), new StringFormat { Alignment = StringAlignment.Center });
            }
            // fewest games first so the ones you play most sit on top
            foreach (var c in chars.OrderBy(c => c.Rec.Games))
            {
                float size = 30 + Math.Min(14, c.Rec.Games / 4f);
                var r = new RectangleF(X(c.Rec.Games) - size / 2, Y(c.Value) - size / 2, size, size);
                r.X = Math.Max(plot.X - size / 2, Math.Min(plot.Right - size / 2, r.X)); r.Y = Math.Max(plot.Y - size / 2, Math.Min(plot.Bottom - size / 2, r.Y));
                bool on = c.Char == Selected || c.Char == _hover;
                // above your usual green, around it gold, below it blue (still learning, not bad)
                var ring = c.Value >= usual + 4 ? Theme.Good : c.Value <= usual - 4 ? Learning : Theme.Accent;
                using (var bg = new SolidBrush(Color.FromArgb(230, 30, 32, 40))) g.FillEllipse(bg, r);
                var img = Data.Head(c.Char);
                if (img != null)
                {
                    var state = g.Save();
                    using (var clip = new GraphicsPath()) { clip.AddEllipse(r); g.SetClip(clip, CombineMode.Intersect); }
                    StatsArt.Cover(g, img, r, 0, false, 0.5f, 0.2f);
                    g.Restore(state);
                }
                using (var pen = new Pen(on ? Theme.Text : ring, on ? 3 : 2)) g.DrawEllipse(pen, r);
                if (c.Better) using (var b = new SolidBrush(Theme.Good)) g.FillPolygon(b, new[] { new PointF(r.Right - 2, r.Y - 4), new PointF(r.Right + 5, r.Y + 6), new PointF(r.Right - 9, r.Y + 6) });
                _spots.Add((c.Char, r));
            }
            using (var m = new SolidBrush(Color.FromArgb(150, Theme.Muted)))
                g.DrawString(scored ? "Height: the average grade score of its games. Scores compare each game with your own usual, so tough opponents and close losses don't sink a character. ▲ = getting better lately." : "Height: win rate (grades show once your replays are read).",
                    Theme.Small, m, new RectangleF(14, Height - 38, Width - 28, 36), new StringFormat { Trimming = StringTrimming.EllipsisWord });
            if (_hover != null)
            {
                var c = chars.First(x => x.Char == _hover);
                var s = _spots.First(x => x.Char == _hover).Box;
                string tip = $"{Fighters.Name(c.Char)} · {c.Rec.Games} games · {c.Rec}" + (scored ? $" · average grade {Progress.GradeOf((int)Math.Round(c.Raw))} ({c.Raw:0})" : "") + (c.Better ? " · getting better" : "");
                var size = g.MeasureString(tip, Theme.Small);
                var tr = new RectangleF(Math.Min(Width - size.Width - 18, Math.Max(4, s.X + s.Width / 2 - size.Width / 2 - 6)), s.Y > 40 ? s.Y - 26 : s.Bottom + 4, size.Width + 12, 22);
                using (var b = new SolidBrush(Color.FromArgb(240, 12, 13, 18))) using (var p = StatsArt.Round(tr, 5)) g.FillPath(b, p);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(tip, Theme.Small, t, tr.X + 6, tr.Y + 3);
            }
        }
    }

    /// <summary>One character of yours: its record, its Smash stats against your usual, its moves and matchups.</summary>
    class CharacterCard : ScrollView
    {
        public StatsData Data;
        public int? Character;

        protected override void Draw(Graphics g, int w)
        {
            if (Data == null) return;
            if (Character == null) { Card(g, new RectangleF(0, 0, w, 80)); Text(g, "Click a character on the chart to see how you do with it.", Theme.Body, Theme.Muted, new RectangleF(16, 28, w - 32, 24)); ContentHeight = 84; return; }
            int c = Character.Value;
            var games = Data.Games.Where(x => x.Me.Character == c).ToList();
            var rec = MatchHistory.Count(games);
            int y = 0;
            // banner
            var top = new RectangleF(0, 0, w, 112);
            Card(g, top);
            var por = Data.Portrait(c);
            if (por != null) { StatsArt.Cover(g, por, new RectangleF(w - 130, 2, 128, 108), 8, false, 0.5f, 0.1f); using (var p = StatsArt.Round(top, 8)) { g.SetClip(p); StatsArt.Shade(g, new RectangleF(w - 170, 0, 90, 112), 215, 0, LinearGradientMode.Horizontal); g.ResetClip(); } }
            Caps(g, "Your character", 16, 12, 200);
            Text(g, Fighters.Name(c), Theme.Bold(18f), Theme.Text, new RectangleF(15, 28, w - 160, 34));
            Text(g, $"{rec}  ·  {rec.Percent}", Theme.Bold(12f), Theme.Text, new RectangleF(16, 62, w - 160, 22));
            var lastPlayed = games.Count > 0 ? games.Last().Game.Start : DateTime.MinValue;
            var scores = Data.Career == null ? new List<int>() : games.Select(v => Data.Career.Games.TryGetValue(v.Game.Id, out var gp) ? gp.Score : -1).Where(v => v >= 0).ToList();
            string avg = scores.Count > 0 ? $" · average grade {Progress.GradeOf((int)Math.Round(scores.Average()))}" : "";
            Text(g, $"{rec.Games} games" + avg + (games.Count > 0 ? $" · last played {lastPlayed.ToLocalTime():MMM d}" : ""), Theme.Small, Theme.Muted, new RectangleF(16, 86, w - 160, 18));
            y = 124;

            // smash stats as this character vs your overall
            var sm = Data.SmashGames;
            var mine = SmashSummary.Of(sm.Where(s => s.MyCharacter == c)); var usual = SmashSummary.Of(sm);
            Caps(g, mine.Games == 0 ? "Smash stats (needs replays)" : $"Smash stats as {Fighters.Name(c)} · from {mine.Games} replays", 2, y, w); y += 40;
            var rows = new (string Name, double Value, double Usual, string Unit, bool HigherBetter, string Fmt)[]
            {
                ("Neutral wins", mine.NeutralWin * 100, usual.NeutralWin * 100, "%", true, "0"),
                ("Openings per kill", mine.OpeningsPerKill, usual.OpeningsPerKill, "", false, "0.0"),
                ("Damage per opening", mine.DamagePerOpening, usual.DamagePerOpening, "%", true, "0.0"),
                ("Average kill %", mine.AvgKill, usual.AvgKill, "%", false, "0"),
                ("Average death %", mine.AvgDeath, usual.AvgDeath, "%", true, "0"),
                ("Kills per game", mine.KillsPerGame, usual.KillsPerGame, "", true, "0.0"),
                ("L-cancels", HabitRate(sm.Where(x => x.MyCharacter == c), h => h.LCancels, h => h.AerialLandings), HabitRate(sm, h => h.LCancels, h => h.AerialLandings), "%", true, "0"),
                ("Tech rate", HabitRate(sm.Where(x => x.MyCharacter == c), h => h.Techs, h => h.TechChances), HabitRate(sm, h => h.Techs, h => h.TechChances), "%", true, "0"),
            };
            var stats = new RectangleF(0, y, w, rows.Length * 30 + 16);
            Card(g, stats);
            // each row: the value, then a bar out from the middle: right (green) when it's better than your usual, left
            // (red) when worse, as far as how much (half again = all the way)
            float valX = w * 0.40f, barX = w * 0.53f, barW = w * 0.24f, useX = barX + barW + 10;
            Text(g, "◂ worse    better ▸", Theme.Small, Theme.Muted, new RectangleF(barX - 20, y - 20, barW + 40, 18), Center);
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i]; float ry = y + 10 + i * 30;
                Text(g, r.Name, Theme.Body, Theme.Text, new RectangleF(14, ry + 2, valX - 20, 20));
                Text(g, Num(r.Value, r.Fmt) + (double.IsNaN(r.Value) ? "" : r.Unit), Theme.BodyBold, Theme.Text, new RectangleF(valX, ry + 1, barX - valX - 6, 20));
                using (var back = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(back, barX, ry + 8, barW, 8);
                using (var mid = new Pen(Color.FromArgb(160, 255, 255, 255))) g.DrawLine(mid, barX + barW / 2, ry + 4, barX + barW / 2, ry + 20);
                if (!double.IsNaN(r.Value) && !double.IsNaN(r.Usual) && Math.Abs(r.Usual) > 1e-6)
                {
                    double rel = (r.Value - r.Usual) / Math.Abs(r.Usual) * (r.HigherBetter ? 1 : -1);
                    float len = (float)(Math.Max(-1, Math.Min(1, rel / 0.5)) * barW / 2);
                    if (Math.Abs(len) < 2) len = len < 0 ? -2 : 2;
                    var color = Math.Abs(rel) < 0.04 ? Theme.Accent : rel > 0 ? Theme.Good : Theme.Bad;
                    using (var b = new SolidBrush(color)) g.FillRectangle(b, len >= 0 ? barX + barW / 2 : barX + barW / 2 + len, ry + 8, Math.Abs(len), 8);
                }
                Text(g, "usual " + Num(r.Usual, r.Fmt) + (double.IsNaN(r.Usual) ? "" : r.Unit), Theme.Small, Theme.Muted, new RectangleF(useX, ry + 3, w - useX - 8, 18));
            }
            y += (int)stats.Height + 14;

            // moves
            var mySm = sm.Where(s => s.MyCharacter == c).ToList();
            var landed = Data.Moves(mySm.SelectMany(s => s.Landed.Select(m => (c, m)))).Take(5).ToList();
            var kills = Data.Moves(mySm.SelectMany(s => s.KillMoves.Select(m => (c, m)))).Take(5).ToList();
            float half = (w - 12) / 2f;
            float listH = 34 + 5 * 26;
            MoveList(g, "Lands most", landed, new RectangleF(0, y, half, listH), "times");
            MoveList(g, "Kills with", kills, new RectangleF(half + 12, y, half, listH), "kills");
            y += (int)listH + 14;

            // matchups as it
            var mu = games.GroupBy(x => x.Them.Character).Select(x => (Char: x.Key, Rec: MatchHistory.Count(x))).Where(x => x.Rec.Games >= 2).ToList();
            var best = mu.OrderByDescending(x => (x.Rec.Wins + 1.0) / (x.Rec.Games + 2)).Take(3).ToList();
            var worst = mu.OrderBy(x => (x.Rec.Wins + 1.0) / (x.Rec.Games + 2)).Where(x => !best.Contains(x)).Take(3).ToList();
            float muH = 34 + 3 * 40;
            Matchups(g, "Best matchups", best, new RectangleF(0, y, half, muH));
            Matchups(g, "Toughest matchups", worst, new RectangleF(half + 12, y, half, muH));
            y += (int)muH;
            ContentHeight = y + 8;
        }

        static double HabitRate(IEnumerable<GameSmash> games, Func<PlayerHabits, int> part, Func<PlayerHabits, int> of)
        {
            int a = 0, b = 0;
            foreach (var x in games) if (x.MyHabits != null && x.MyHabits.Known) { a += part(x.MyHabits); b += of(x.MyHabits); }
            return b == 0 ? double.NaN : 100.0 * a / b;
        }

        void MoveList(Graphics g, string title, List<(string Name, int Count)> moves, RectangleF r, string unit)
        {
            Card(g, r);
            Caps(g, title, r.X + 14, r.Y + 10, r.Width - 28);
            if (moves.Count == 0) { Text(g, "needs replays", Theme.Small, Theme.Muted, new RectangleF(r.X + 14, r.Y + 34, r.Width - 28, 18)); return; }
            int most = moves.Max(m => m.Count);
            for (int i = 0; i < moves.Count; i++)
            {
                float y = r.Y + 34 + i * 26;
                Text(g, moves[i].Name, Theme.Body, Theme.Text, new RectangleF(r.X + 14, y, r.Width * 0.5f, 20));
                float bx = r.X + r.Width * 0.52f, bw = r.Width * 0.3f;
                using (var b = new SolidBrush(i == 0 ? Theme.Accent : Color.FromArgb(150, Theme.Accent))) g.FillRectangle(b, bx, y + 6, bw * moves[i].Count / most, 8);
                Text(g, moves[i].Count.ToString(), Theme.Small, Theme.Muted, new RectangleF(bx + bw + 6, y + 1, r.Right - bx - bw - 10, 18));
            }
        }

        void Matchups(Graphics g, string title, List<(int Char, Record Rec)> list, RectangleF r)
        {
            Card(g, r);
            Caps(g, title, r.X + 14, r.Y + 10, r.Width - 28);
            if (list.Count == 0) { Text(g, "2+ games against a character needed", Theme.Small, Theme.Muted, new RectangleF(r.X + 14, r.Y + 34, r.Width - 28, 18)); return; }
            for (int i = 0; i < list.Count; i++)
            {
                float y = r.Y + 32 + i * 40;
                Head(g, Data.Head(list[i].Char), new RectangleF(r.X + 12, y, 32, 36));
                Text(g, "vs " + Fighters.Name(list[i].Char), Theme.BodyBold, Theme.Text, new RectangleF(r.X + 52, y + 2, r.Width - 120, 20));
                Text(g, $"{list[i].Rec} · {list[i].Rec.Games} games", Theme.Small, Theme.Muted, new RectangleF(r.X + 52, y + 20, r.Width - 120, 18));
                Text(g, list[i].Rec.Percent, Theme.Bold(12f), RecordTable.RateColor(list[i].Rec.Rate), new RectangleF(r.Right - 66, y + 7, 54, 22), Right);
            }
        }
    }

    // ============================================================== matches

    /// <summary>Your games, newest first, a day at a time; click one for its card.</summary>
    class MatchList : ScrollView
    {
        public StatsData Data;
        public long? Selected;
        public event Action<long> Picked;
        readonly List<(long Id, RectangleF Box)> _rows = new List<(long, RectangleF)>();
        long? _hover;

        protected override void Hover(Point p)
        {
            long? h = null; foreach (var r in _rows) if (r.Box.Contains(p)) h = r.Id;
            if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void Clicked(Point p, MouseButtons b) { foreach (var r in _rows) if (r.Box.Contains(p)) { Selected = r.Id; Picked?.Invoke(r.Id); Invalidate(); } }

        static string Day(DateTime local)
        {
            var d = (DateTime.Now.Date - local.Date).TotalDays;
            return d < 1 ? "Today" : d < 2 ? "Yesterday" : local.ToString(local.Year == DateTime.Now.Year ? "dddd, MMM d" : "MMM d, yyyy");
        }

        protected override void Draw(Graphics g, int w)
        {
            _rows.Clear();
            if (Data == null) return;
            var games = Data.Games.AsEnumerable().Reverse().ToList();
            if (games.Count == 0) { Text(g, "No games here yet.", Theme.Body, Theme.Muted, new RectangleF(4, 8, w, 24)); ContentHeight = 40; return; }
            int y = 0;
            const int H = 52;
            foreach (var day in games.GroupBy(x => x.Game.Start.ToLocalTime().Date))
            {
                var rec = MatchHistory.Count(day);
                Text(g, Day(day.Key).ToUpperInvariant(), Theme.Small, Theme.Accent, new RectangleF(4, y + 8, w / 2f, 18));
                Text(g, $"{rec} · {day.Count()} games", Theme.Small, Theme.Muted, new RectangleF(w / 2f, y + 8, w / 2f - 6, 18), Right);
                y += 30;
                foreach (var x in day)
                {
                    var r = new RectangleF(0, y, w, H - 6);
                    bool on = Selected == x.Game.Id || _hover == x.Game.Id;
                    Card(g, r, on ? Color.FromArgb(235, 44, 48, 60) : (Color?)null);
                    bool won = x.Me.Won == true;
                    using (var b = new SolidBrush(won ? Theme.Good : Theme.Bad)) using (var p = StatsArt.Round(new RectangleF(r.X, r.Y, 5, r.Height), 2.5f)) g.FillPath(b, p);
                    Text(g, won ? "WIN" : "LOSS", Theme.Bold(10f), won ? Theme.Good : Theme.Bad, new RectangleF(14, y + 13, 44, 20));
                    Head(g, Data.Head(x.Me.Character), new RectangleF(60, y + 6, 30, 34));
                    Text(g, "vs", Theme.Small, Theme.Muted, new RectangleF(92, y + 14, 22, 18), Center);
                    Head(g, Data.Head(x.Them.Character), new RectangleF(116, y + 6, 30, 34), true);
                    bool narrow = w < 520;
                    Data.Smash.TryGetValue(x.Game.Id, out var s);
                    var time = TimeSpan.FromSeconds(x.Game.Frames / 60.0);
                    float nameW = narrow ? w - 160 - 70 : w * 0.34f;
                    Text(g, x.Them.Name, Theme.BodyBold, Theme.Text, new RectangleF(156, y + 5, nameW, 20));
                    Text(g, Data.History.StageName(x.Game.Stage) + (narrow ? $" · {(int)time.TotalMinutes}:{time.Seconds:00}" + (s?.MyStocksLeft != null && won ? $" · {s.MyStocksLeft} stock{(s.MyStocksLeft == 1 ? "" : "s")} left" : "") : ""), Theme.Small, Theme.Muted, new RectangleF(156, y + 24, nameW, 18));
                    string stocks = s?.MyStocksLeft != null ? won ? $"{s.MyStocksLeft} stock{(s.MyStocksLeft == 1 ? "" : "s")} left" : $"they had {s.TheirStocksLeft} left" : "";
                    if (!narrow)
                    {
                        float cx = 156 + nameW + 10;
                        Text(g, stocks, Theme.Body, Theme.Text, new RectangleF(cx, y + 5, w - cx - 90, 20));
                        Text(g, $"{(int)time.TotalMinutes}:{time.Seconds:00}" + (x.Game.Ranked ? " · Ranked" : " · Unranked"), Theme.Small, Theme.Muted, new RectangleF(cx, y + 24, w - cx - 90, 18));
                    }
                    Text(g, x.Game.Start.ToLocalTime().ToString("h:mm tt"), Theme.Small, Theme.Muted, new RectangleF(w - 84, y + 14, 74, 18), Right);
                    _rows.Add((x.Game.Id, r));
                    y += H;
                }
            }
            ContentHeight = y + 6;
        }
    }

    /// <summary>One game: who, where, how the percents went, and its Smash stats side by side.</summary>
    class MatchCard : ScrollView
    {
        public StatsData Data;
        public long? Game;
        public event Action<string> ShowReplay;
        RectangleF _replayButton;

        protected override void Clicked(Point p, MouseButtons b)
        {
            if (_replayButton.Contains(p) && Game != null) ShowReplay?.Invoke(Data.Games.First(x => x.Game.Id == Game).Game.File);
        }
        protected override void Hover(Point p) => Cursor = _replayButton.Contains(p) ? Cursors.Hand : Cursors.Default;

        protected override void Draw(Graphics g, int w)
        {
            _replayButton = RectangleF.Empty;
            if (Data == null) return;
            var x = Data.Games.FirstOrDefault(m => m.Game.Id == Game);
            if (x.Game == null) { Card(g, new RectangleF(0, 0, w, 80)); Text(g, "Click a game to see how it went.", Theme.Body, Theme.Muted, new RectangleF(16, 28, w - 32, 24)); ContentHeight = 84; return; }
            Data.Smash.TryGetValue(x.Game.Id, out var s);
            bool won = x.Me.Won == true;
            // banner: the stage behind, both characters
            var top = new RectangleF(0, 0, w, 130);
            Card(g, top);
            var stage = Data.Stage(x.Game.Stage);
            if (stage != null) { StatsArt.Cover(g, stage, top, 8); using (var p = StatsArt.Round(top, 8)) { g.SetClip(p); using (var b = new SolidBrush(Color.FromArgb(160, 12, 13, 18))) g.FillRectangle(b, top); g.ResetClip(); } }
            StatsArt.Cover(g, Data.Portrait(x.Me.Character), new RectangleF(8, 6, 110, 118), 6, false, 0.5f, 0.1f);
            StatsArt.Cover(g, Data.Portrait(x.Them.Character), new RectangleF(w - 118, 6, 110, 118), 6, true, 0.5f, 0.1f);
            Text(g, won ? "WIN" : "LOSS", Theme.Bold(22f), won ? Theme.Good : Theme.Bad, new RectangleF(120, 14, w - 240, 38), Center);
            Text(g, $"{Data.History.MyName} vs {x.Them.Name}", Theme.BodyBold, Theme.Text, new RectangleF(120, 54, w - 240, 20), Center);
            var time = TimeSpan.FromSeconds(x.Game.Frames / 60.0);
            Text(g, $"{Data.History.StageName(x.Game.Stage)} · {(int)time.TotalMinutes}:{time.Seconds:00}", Theme.Small, Theme.Text, new RectangleF(120, 76, w - 240, 18), Center);
            Text(g, x.Game.Start.ToLocalTime().ToString("MMM d, h:mm tt") + (x.Game.Ranked ? " · Ranked" : " · Unranked") + $" · game {x.Game.GameNumber}", Theme.Small, Theme.Muted, new RectangleF(120, 96, w - 240, 18), Center);
            int y = 142;
            if (Data.Career != null && Data.Career.Games.TryGetValue(x.Game.Id, out var gp)) y = DrawReport(g, w, y, gp, s);
            if (s == null)
            {
                Card(g, new RectangleF(0, y, w, 60));
                Text(g, Data.ReplaysLoading ? "Reading this game's replay…" : "No replay of this game was found, so there are no Smash stats for it.", Theme.Body, Theme.Muted, new RectangleF(14, y + 20, w - 28, 22));
                ContentHeight = y + 64; return;
            }

            // percents over the game
            var chart = new RectangleF(0, y, w, 170);
            Card(g, chart);
            Caps(g, "Percent over the game", 14, y + 10, 200);
            using (var b = new SolidBrush(You)) g.FillRectangle(b, w - 200, y + 15, 10, 4);
            Text(g, "you", Theme.Small, Theme.Text, new RectangleF(w - 186, y + 8, 50, 18));
            using (var b = new SolidBrush(Them)) g.FillRectangle(b, w - 120, y + 15, 10, 4);
            Text(g, "them", Theme.Small, Theme.Text, new RectangleF(w - 106, y + 8, 60, 18));
            var plot = new RectangleF(40, y + 34, w - 56, 110);
            var lines = new[] { s.Me, s.Them }.Select(p => Steps(s.Replay, p)).ToList();
            float top2 = Math.Max(100, lines.SelectMany(l => l).Select(p => p.Y).DefaultIfEmpty(100).Max());
            int frames = Math.Max(1, s.Replay.Frames);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) for (int p = 0; p <= top2; p += 50) { float ly = plot.Bottom - p / top2 * plot.Height; g.DrawLine(pen, plot.X, ly, plot.Right, ly); Text(g, p + "%", Theme.Small, Theme.Muted, new RectangleF(0, ly - 9, 36, 18), Right); }
            for (int k = 0; k < 2; k++)
            {
                var pts = lines[k].Select(p => new PointF(plot.X + plot.Width * p.X / frames, plot.Bottom - p.Y / top2 * plot.Height)).ToArray();
                if (pts.Length >= 2) using (var pen = new Pen(k == 0 ? You : Them, 2f)) g.DrawLines(pen, pts);
                foreach (var d in s.Replay.Deaths.Where(d => d.Victim == (k == 0 ? s.Me : s.Them)))
                {
                    float dx = plot.X + plot.Width * d.Frame / frames, dy = plot.Bottom - Math.Min(top2, d.Percent) / top2 * plot.Height;
                    using (var pen = new Pen(k == 0 ? You : Them, 2.2f)) { g.DrawLine(pen, dx - 4, dy - 4, dx + 4, dy + 4); g.DrawLine(pen, dx - 4, dy + 4, dx + 4, dy - 4); }
                }
            }
            Text(g, "✕ = a stock lost", Theme.Small, Theme.Muted, new RectangleF(plot.X, plot.Bottom + 4, 200, 18));
            y += (int)chart.Height + 14;

            // side by side
            var rows = new (string Name, string Mine, string Theirs, bool? MineBetter)[]
            {
                ("Stocks left", s.MyStocksLeft?.ToString() ?? "–", s.TheirStocksLeft?.ToString() ?? "–", null),
                ("Openings (neutral wins)", $"{s.MyOpenings} ({Pct(s.NeutralWin)})", $"{s.TheirOpenings} ({Pct(1 - s.NeutralWin)})", s.MyOpenings == s.TheirOpenings ? (bool?)null : s.MyOpenings > s.TheirOpenings),
                ("Damage per opening", Num(s.DamagePerOpening, "0.0") + "%", Num(s.TheirOpenings == 0 ? double.NaN : s.Taken / s.TheirOpenings, "0.0") + "%", null),
                ("Openings per kill", Num(s.OpeningsPerKill, "0.0"), Num(s.Deaths == 0 ? double.NaN : (double)s.TheirOpenings / s.Deaths, "0.0"), null),
                ("Damage done", $"{s.Dealt:0}%", $"{s.Taken:0}%", s.Dealt == s.Taken ? (bool?)null : s.Dealt > s.Taken),
            };
            var table = new RectangleF(0, y, w, 34 + rows.Length * 26);
            Card(g, table);
            Text(g, "YOU", Theme.Small, You, new RectangleF(w * 0.5f, y + 10, w * 0.22f, 18), Center);
            Text(g, "THEM", Theme.Small, Them, new RectangleF(w * 0.74f, y + 10, w * 0.22f, 18), Center);
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + 32 + i * 26;
                Text(g, rows[i].Name, Theme.Body, Theme.Muted, new RectangleF(14, ry, w * 0.48f, 20));
                Text(g, rows[i].Mine, Theme.BodyBold, rows[i].MineBetter == true ? Theme.Good : Theme.Text, new RectangleF(w * 0.5f, ry, w * 0.22f, 20), Center);
                Text(g, rows[i].Theirs, Theme.BodyBold, rows[i].MineBetter == false ? Theme.Good : Theme.Text, new RectangleF(w * 0.74f, ry, w * 0.22f, 20), Center);
            }
            y += (int)table.Height + 14;

            // each stock taken and lost
            var stocks = s.Replay.Deaths.Where(d => d.Victim == s.Me || d.Victim == s.Them).OrderBy(d => d.Frame).ToList();
            var list = new RectangleF(0, y, w, 34 + Math.Max(1, stocks.Count) * 24);
            Card(g, list);
            Caps(g, "Stocks", 14, y + 10, 200);
            if (stocks.Count == 0) Text(g, "No stocks were taken.", Theme.Small, Theme.Muted, new RectangleF(14, y + 32, w - 28, 18));
            for (int i = 0; i < stocks.Count; i++)
            {
                var d = stocks[i]; bool mine = d.Victim == s.Them; float ry = y + 32 + i * 24;
                var t = TimeSpan.FromSeconds(d.Frame / 60.0);
                int killerChar = mine ? s.MyCharacter : s.TheirCharacter;
                string how = d.Killer < 0 ? (mine ? "they self-destructed" : "a self-destruct") : Data.MoveName(killerChar, d.Move) ?? "a projectile or item";
                Text(g, $"{(int)t.TotalMinutes}:{t.Seconds:00}", Theme.Small, Theme.Muted, new RectangleF(14, ry + 1, 44, 18));
                using (var b = new SolidBrush(mine ? Theme.Good : Theme.Bad)) g.FillEllipse(b, 60, ry + 6, 8, 8);
                Text(g, mine ? $"You took a stock at {d.Percent:0}%" : $"You lost a stock at {d.Percent:0}%", Theme.Body, Theme.Text, new RectangleF(76, ry, w * 0.5f, 20));
                Text(g, d.Killer < 0 ? how : (mine ? "with " : "to ") + how, Theme.Body, Theme.Muted, new RectangleF(76 + w * 0.5f, ry, w * 0.5f - 90, 20));
            }
            y += (int)list.Height + 14;
            _replayButton = new RectangleF(0, y, 190, 32);
            Card(g, _replayButton, Color.FromArgb(230, 44, 48, 60));
            Text(g, "Show the replay file", Theme.BodyBold, Theme.Text, new RectangleF(_replayButton.X, y + 7, _replayButton.Width, 20), Center);
            ContentHeight = y + 40;
        }

        /// <summary>The game's report: grade, XP, levels, badges, bests, how it compares with your usual, your best combo
        /// and your habits in it.</summary>
        int DrawReport(Graphics g, int w, int y, GameProgress p, GameSmash s)
        {
            int top = y;
            var gc = ReportArt.GradeColor(p.Grade);
            // the grade and the XP
            int h1 = 128;
            Card(g, new RectangleF(0, y, w, h1));
            ReportArt.Grade(g, p.Grade, p.Score, new RectangleF(12, y + 12, 74, 74));
            Text(g, $"Score {p.Score}", Theme.Bold(13f), gc, new RectangleF(96, y + 12, w - 110, 24));
            Text(g, $"better than {p.Percentile}% of your games before it" + (p.WinStreak >= 2 ? $" · {p.WinStreak} wins in a row" : ""), Theme.Small, Theme.Muted, new RectangleF(96, y + 36, w - 110, 18));
            Text(g, $"+{p.Xp} XP: " + string.Join(", ", p.XpParts.Select(v => $"{v.Why} {v.Xp}")), Theme.Small, Theme.Text, new RectangleF(96, y + 56, w - 110, 34), new StringFormat { Trimming = StringTrimming.EllipsisWord });
            ReportArt.XpBar(g, "Level", p.XpBefore, p.Xp, new RectangleF(14, y + 90, w / 2f - 22, 34), 1, Theme.Accent);
            ReportArt.XpBar(g, Fighters.Name(p.Character), p.CharXpBefore, p.Xp, new RectangleF(w / 2f + 8, y + 90, w / 2f - 22, 34), 1, Them);
            y += h1 + 12;
            // badges and bests
            if (p.NewBadges.Count > 0 || p.NewBests.Count > 0)
            {
                int rows = p.NewBadges.Count > 0 ? 1 : 0;
                int h2 = 32 + rows * 66 + p.NewBests.Count * 20;
                Card(g, new RectangleF(0, y, w, h2));
                Caps(g, "Earned this game", 14, y + 10, w - 28, Theme.Accent);
                float yy = y + 32;
                if (p.NewBadges.Count > 0)
                {
                    int n = Math.Min(p.NewBadges.Count, Math.Max(1, (w - 20) / 86));
                    for (int i = 0; i < n; i++)
                    {
                        var b = p.NewBadges[i]; float bx = 14 + i * 86;
                        ReportArt.Medal(g, b, new RectangleF(bx + 23, yy, 36, 36));
                        Text(g, b.Tier > 0 ? $"{b.Name} {Badge.Tiers[b.Tier]}" : b.Name, Theme.Small, Theme.Text, new RectangleF(bx - 4, yy + 38, 90, 18), Center);
                    }
                    if (p.NewBadges.Count > n) Text(g, $"+{p.NewBadges.Count - n}", Theme.Small, Theme.Muted, new RectangleF(w - 50, yy + 10, 40, 18), Right);
                    yy += 66;
                }
                foreach (var b in p.NewBests) { Text(g, $"★ NEW BEST · {b.Name}: {b.Text}" + (b.Detail != null ? $" ({b.Detail})" : ""), Theme.BodyBold, Theme.Accent, new RectangleF(14, yy, w - 28, 20)); yy += 20; }
                y += h2 + 12;
            }
            // the coach
            if (s != null)
            {
                var coach = Data.Coach(p.GameId);
                if (coach == null || coach.Any)
                {
                    int h4 = 34 + (coach == null ? 22 : coach.Tips.Count * 48 + coach.Facts.Count * 36 + (coach.Combo != null ? 40 : 0));
                    Card(g, new RectangleF(0, y, w, h4));
                    Caps(g, "Coach's notes", 14, y + 10, w - 28, Theme.Accent);
                    float yy = y + 32;
                    if (coach == null) Text(g, "Working out how you lost your stocks…", Theme.Small, Theme.Muted, new RectangleF(14, yy, w - 28, 20));
                    else
                    {
                        foreach (var t in coach.Tips)
                        {
                            ReportArt.Stick(g, t, new RectangleF(14, yy + 2, 40, 40));
                            Text(g, DeathCoach.Say(t), Theme.Small, t.Saved ? Theme.Text : Theme.Muted, new RectangleF(62, yy + 2, w - 76, 40), new StringFormat { Trimming = StringTrimming.EllipsisWord });
                            yy += 48;
                        }
                        foreach (var f in coach.Facts) { Text(g, "★ " + f, Theme.Small, Theme.Text, new RectangleF(14, yy, w - 28, 34), new StringFormat { Trimming = StringTrimming.EllipsisWord }); yy += 36; }
                        if (coach.Combo != null) Text(g, coach.Combo, Theme.Small, Theme.Accent, new RectangleF(14, yy, w - 28, 38), new StringFormat { Trimming = StringTrimming.EllipsisWord });
                    }
                    y += h4 + 12;
                }
            }
            // compared with your usual, your best combo, your habits
            if (s != null)
            {
                var rows = p.VsUsual.Where(v => !double.IsNaN(v.Value)).ToList();
                var hb = s.MyHabits;
                bool habits = hb != null && hb.Known;
                int h3 = 34 + rows.Count * 22 + (s.BestComboHits >= 2 ? 44 : 0) + (habits ? 44 : 0);
                Card(g, new RectangleF(0, y, w, h3));
                Caps(g, "Compared with your usual", 14, y + 10, w - 28);
                float yy = y + 32;
                foreach (var v in rows)
                {
                    string fmt = v.Format;
                    Text(g, v.Name, Theme.Body, Theme.Muted, new RectangleF(14, yy, w * 0.45f, 20));
                    Text(g, v.Value.ToString(fmt), Theme.BodyBold, Theme.Text, new RectangleF(w * 0.46f, yy, w * 0.2f, 20));
                    if (!double.IsNaN(v.Usual))
                    {
                        double d = v.Value - v.Usual; bool better = (d > 0) == v.HigherBetter; bool same = Math.Abs(d) < Math.Abs(v.Usual) * 0.05;
                        Text(g, same ? "about usual" : $"{(d > 0 ? "▲" : "▼")} usual {v.Usual.ToString(fmt)}", Theme.Small, same ? Theme.Muted : better ? Theme.Good : Theme.Bad, new RectangleF(w * 0.66f, yy + 2, w * 0.34f - 10, 18));
                    }
                    yy += 22;
                }
                if (s.BestComboHits >= 2)
                {
                    Text(g, $"Best combo: {s.BestComboDamage:0}% in {s.BestComboHits} hits (from {s.BestComboStart:0}%)", Theme.BodyBold, Theme.Text, new RectangleF(14, yy + 4, w - 28, 20));
                    Text(g, ReportArt.Combo(s, Data.MoveName), Theme.Small, Theme.Muted, new RectangleF(14, yy + 24, w - 28, 18));
                    yy += 44;
                }
                if (habits)
                {
                    string Of(int a, int b) => b == 0 ? "–" : $"{a}/{b}";
                    Text(g, $"L-cancels {Of(hb.LCancels, hb.AerialLandings)} · techs {Of(hb.Techs, hb.TechChances)} · wavedashes {hb.Wavedashes} · grabs {Of(hb.Grabs, hb.GrabTries)}", Theme.Small, Theme.Text, new RectangleF(14, yy + 4, w - 28, 18));
                    Text(g, $"ledge: {hb.LedgeGetup} getups, {hb.LedgeRoll} rolls, {hb.LedgeJump} jumps, {hb.LedgeAttack} attacks, {hb.LedgeDrop} drops · {Pct((double)hb.FramesCenter / Math.Max(1, hb.FramesAlive))} of the game mid-stage", Theme.Small, Theme.Muted, new RectangleF(14, yy + 22, w - 28, 18));
                }
                y += h3 + 12;
            }
            return y;
        }

        /// <summary>A player's percent through the game: up at each hit, back to 0 at each stock lost.</summary>
        static List<PointF> Steps(ReplayData d, int p)
        {
            var events = d.Hits.Where(h => h.Victim == p).Select(h => (h.Frame, Kind: 0, Value: h.Before + h.Damage))
                .Concat(d.Deaths.Where(x => x.Victim == p).Select(x => (x.Frame, Kind: 1, Value: 0f))).OrderBy(e => e.Frame).ThenBy(e => e.Kind).ToList();
            var pts = new List<PointF> { new PointF(0, 0) };
            float now = 0;
            foreach (var e in events)
            {
                pts.Add(new PointF(e.Frame, now));
                now = e.Kind == 1 ? 0 : e.Value;
                pts.Add(new PointF(e.Frame, now));
            }
            pts.Add(new PointF(d.Frames, now));
            return pts;
        }
    }
}
