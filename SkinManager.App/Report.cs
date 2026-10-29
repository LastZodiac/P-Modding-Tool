// The after-game report: a card that slides in over the game a few seconds after it ends (your grade, the XP you
// earned filling your level bar, new badges and personal bests, your best combo), and the pieces it's drawn with,
// shared with the full report in the Stat Tracker.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    static class ReportArt
    {
        public static Color GradeColor(string grade) =>
            grade == "S" ? Color.FromArgb(255, 200, 60) : grade == "A" ? Color.FromArgb(92, 201, 120) : grade == "B" ? Color.FromArgb(96, 182, 255) : grade == "C" ? Color.FromArgb(170, 176, 192) : Color.FromArgb(240, 110, 100);

        public static Color TierColor(int tier) =>
            tier == 1 ? Color.FromArgb(205, 127, 50) : tier == 2 ? Color.FromArgb(200, 206, 218) : tier == 3 ? Color.FromArgb(255, 200, 60) : tier == 4 ? Color.FromArgb(150, 230, 255) : Color.FromArgb(178, 128, 255);

        /// <summary>The grade: a ring with the letter (pop: 0..1 grows it in).</summary>
        public static void Grade(Graphics g, string grade, int score, RectangleF r, float pop = 1)
        {
            var c = GradeColor(grade);
            float s = 0.6f + 0.4f * EaseOut(pop);
            var box = new RectangleF(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
            using (var b = new SolidBrush(Color.FromArgb(60, c))) g.FillEllipse(b, box);
            using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 5)) g.DrawEllipse(pen, Rectangle.Round(RectangleF.Inflate(box, -3, -3)));
            using (var pen = new Pen(c, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, RectangleF.Inflate(box, -3, -3), -90, 360f * Math.Max(0.01f, score / 100f) * EaseOut(pop));
            using (var f = new Font(Theme.Title.FontFamily, Math.Max(8, box.Height * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(c))
                g.DrawString(grade, f, b, box, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        /// <summary>A badge: a medal in its tier's color with its initials.</summary>
        public static void Medal(Graphics g, Badge b, RectangleF r, float pop = 1)
        {
            var c = TierColor(b.Tier);
            float s = Math.Max(0.01f, EaseOut(pop) * (pop < 1 ? 1.1f : 1f));
            var box = new RectangleF(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
            var pts = Enumerable.Range(0, 8).Select(i => new PointF(box.X + box.Width / 2 + (float)Math.Cos(i * Math.PI / 4 + Math.PI / 8) * box.Width / 2, box.Y + box.Height / 2 - (float)Math.Sin(i * Math.PI / 4 + Math.PI / 8) * box.Height / 2)).ToArray();
            using (var br = new LinearGradientBrush(box, ControlPaint.Light(c, 0.3f), ControlPaint.Dark(c, 0.15f), 90f)) g.FillPolygon(br, pts);
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawPolygon(pen, pts);
            string initials = string.Concat(b.Name.Split(' ').Where(w => w.Length > 0 && char.IsLetter(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0])));
            using (var f = new Font(Theme.Title.FontFamily, Math.Max(6, box.Height * 0.36f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var t = new SolidBrush(Color.FromArgb(30, 32, 40)))
                g.DrawString(initials, f, t, box, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }

        /// <summary>The level bar from the XP before to after (t: 0..1 of the fill); says LEVEL UP when it passes one.</summary>
        public static void XpBar(Graphics g, string label, int before, int gained, RectangleF r, float t, Color fill)
        {
            int now = before + (int)(gained * EaseOut(t));
            var (level, into, need) = Progress.LevelOf(now);
            var lb = Progress.LevelOf(before).Level;
            using (var b = new SolidBrush(Theme.Text)) g.DrawString($"{label} {level}", Theme.BodyBold, b, r.X, r.Y);
            bool up = level > lb;
            if (up) using (var b = new SolidBrush(Theme.Accent)) g.DrawString("LEVEL UP!", Theme.Bold(9.5f), b, r.X + g.MeasureString($"{label} {level}", Theme.BodyBold).Width + 6, r.Y + 1);
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"+{(int)(gained * EaseOut(t))} XP · {into}/{need}", Theme.Small, m, new RectangleF(r.X, r.Y + 1, r.Width, 18), new StringFormat { Alignment = StringAlignment.Far });
            var bar = new RectangleF(r.X, r.Y + 22, r.Width, 9);
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) using (var p = StatsArt.Round(bar, 4.5f)) g.FillPath(b, p);
            // what was there before (this level), then what this game added
            float startFrac = level > lb ? 0 : (float)Progress.LevelOf(before).Into / need;
            float nowFrac = (float)into / need;
            if (nowFrac > 0.005f) using (var b = new SolidBrush(fill)) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(9, bar.Width * nowFrac), bar.Height), 4.5f)) g.FillPath(b, p);
            if (startFrac > 0.005f) using (var b = new SolidBrush(ControlPaint.Dark(fill, 0.25f))) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(9, bar.Width * Math.Min(startFrac, nowFrac)), bar.Height), 4.5f)) g.FillPath(b, p);
        }

        public static float EaseOut(float t) { t = Math.Max(0, Math.Min(1, t)); return 1 - (1 - t) * (1 - t) * (1 - t); }

        /// <summary>A combo's moves in words: repeats folded ("Neutral air ×3").</summary>
        public static string Combo(GameSmash s, Func<int, int, string> name)
        {
            var parts = new List<(string Name, int Count)>();
            foreach (var m in s.BestComboMoves)
            {
                var n = name(s.MyCharacter, m) ?? "projectile";
                if (parts.Count > 0 && parts[parts.Count - 1].Name == n) parts[parts.Count - 1] = (n, parts[parts.Count - 1].Count + 1);
                else parts.Add((n, 1));
            }
            return string.Join(" → ", parts.Select(p => p.Count > 1 ? $"{p.Name} ×{p.Count}" : p.Name));
        }
    }

    /// <summary>The card over the game right after it.</summary>
    class ReportCard : OverlayForm
    {
        GameProgress _p; GameSmash _s; GamePlayer _me, _them; string _stage; Func<int, int, string> _name; StatsArt _art;
        readonly Timer _timer = new Timer { Interval = 30 };
        DateTime _shown; public Rectangle? Game;
        static readonly float Seconds = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") != null ? 90 : 16;   // UI tests: long enough to look at

        public ReportCard()
        {
            Size = new Size(460, 300); Opacity = 0.93;
            _timer.Tick += (s, e) =>
            {
                if ((DateTime.Now - _shown).TotalSeconds > Seconds) { _timer.Stop(); Hide(); return; }
                Invalidate();
            };
        }

        public void Show(GameProgress p, GameSmash s, GamePlayer me, GamePlayer them, string stage, Func<int, int, string> name, StatsArt art)
        {
            _p = p; _s = s; _me = me; _them = them; _stage = stage; _name = name; _art = art;
            int h = 196 + (p.NewBadges.Count > 0 ? 70 : 0) + Math.Min(2, p.NewBests.Count) * 20 + (s != null && s.BestComboHits >= 2 ? 22 : 0);
            Size = new Size(460, h);
            // top middle of the game (the opponent card sits top left)
            var area = Game ?? Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.X + Math.Max(14, (area.Width - Width) / 2), area.Y + 14);
            _shown = DateTime.Now; _timer.Start();
            if (!Visible) Show();
            Invalidate();
        }

        float T(double from, double length) => (float)Math.Max(0, Math.Min(1, ((DateTime.Now - _shown).TotalSeconds - from) / length));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (_p == null) return;
            var gc = ReportArt.GradeColor(_p.Grade);
            using (var border = new Pen(gc, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            bool won = _me?.Won == true;
            // top: the grade, the result
            ReportArt.Grade(g, _p.Grade, _p.Score, new RectangleF(14, 14, 82, 82), T(0, 0.5));
            float x = 108;
            Text(g, won ? "WIN" : "LOSS", Theme.Bold(18f), won ? Theme.Good : Theme.Bad, new RectangleF(x, 12, 120, 32));
            string stocks = _s?.MyStocksLeft != null ? (won ? $"{_s.MyStocksLeft} stock{(_s.MyStocksLeft == 1 ? "" : "s")} left" : $"took {_s.Kills} stock{(_s.Kills == 1 ? "" : "s")}") : "";
            Text(g, stocks, Theme.Body, Theme.Text, new RectangleF(x + 74, 20, Width - x - 90, 20));
            Text(g, $"vs {_them?.Name} ({Fighters.Name(_them?.Character ?? 0)})" + (_stage != null ? " · " + _stage : ""), Theme.Small, Theme.Muted, new RectangleF(x, 46, Width - x - 14, 18));
            Text(g, $"Score {_p.Score} · better than {_p.Percentile}% of your games", Theme.Small, gc, new RectangleF(x, 66, Width - x - 14, 18));
            if (_p.WinStreak >= 2) Text(g, $"{_p.WinStreak} wins in a row!", Theme.Small, Theme.Accent, new RectangleF(x, 82, Width - x - 14, 18));
            // the bars
            float y = 108;
            ReportArt.XpBar(g, "Level", _p.XpBefore, _p.Xp, new RectangleF(16, y, Width - 32, 34), T(0.5, 1.4), Theme.Accent);
            y += 40;
            ReportArt.XpBar(g, Fighters.Name(_p.Character), _p.CharXpBefore, _p.Xp, new RectangleF(16, y, Width - 32, 34), T(0.8, 1.4), Color.FromArgb(96, 182, 255));
            y += 44;
            // badges, one after another
            if (_p.NewBadges.Count > 0)
            {
                int n = Math.Min(5, _p.NewBadges.Count);
                float w = (Width - 32) / 5f;
                for (int i = 0; i < n; i++)
                {
                    var b = _p.NewBadges[i]; float pop = T(1.8 + i * 0.35, 0.35);
                    if (pop <= 0) continue;
                    ReportArt.Medal(g, b, new RectangleF(16 + i * w + w / 2 - 20, y, 40, 40), pop);
                    Text(g, b.Tier > 0 ? $"{b.Name} {Badge.Tiers[b.Tier]}" : b.Name, Theme.Small, Theme.Text, new RectangleF(16 + i * w, y + 44, w, 18), true);
                }
                if (_p.NewBadges.Count > n) Text(g, $"+{_p.NewBadges.Count - n} more", Theme.Small, Theme.Muted, new RectangleF(Width - 90, y - 16, 76, 16));
                y += 70;
            }
            foreach (var b in _p.NewBests.Take(2))
            {
                if (T(2.2, 0.3) > 0) Text(g, $"★ NEW BEST · {b.Name}: {b.Text}", Theme.BodyBold, Theme.Accent, new RectangleF(16, y, Width - 32, 20));
                y += 20;
            }
            if (_s != null && _s.BestComboHits >= 2)
                Text(g, $"Best combo: {_s.BestComboDamage:0}% · {ReportArt.Combo(_s, _name)}", Theme.Small, Theme.Text, new RectangleF(16, y + 2, Width - 32, 18));
            // how long it stays
            float left = 1 - (float)((DateTime.Now - _shown).TotalSeconds / Seconds);
            using (var b = new SolidBrush(Color.FromArgb(120, gc))) g.FillRectangle(b, 2, Height - 4, (Width - 4) * Math.Max(0, left), 2);
        }
    }
}
