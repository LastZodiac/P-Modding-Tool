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
    /// <summary>Drawing pieces shared by the after-game card and the full report: grade rings, badge colours, combo
    /// lines.</summary>
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
        public static void XpBar(Graphics g, string label, int before, int gained, RectangleF r, float t, Color fill, bool brief = false)
        {
            int now = before + (int)(gained * EaseOut(t));
            var (level, into, need) = Progress.LevelOf(now);
            var lb = Progress.LevelOf(before).Level;
            bool up = level > lb;
            if (brief)
            {
                // half-width bars (the card over the game): the name cut short if it must be, and "+XP" or "LEVEL UP!"
                var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                using (var b = new SolidBrush(Theme.Text)) g.DrawString($"{label} {level}", Theme.BodyBold, b, new RectangleF(r.X, r.Y, r.Width - (up ? 66 : 40), 20), fmt);
                using (var b = new SolidBrush(up ? Theme.Accent : Theme.Muted))
                    g.DrawString(up ? "LEVEL UP!" : $"+{(int)(gained * EaseOut(t))}", up ? Theme.Bold(8.5f) : Theme.Small, b, new RectangleF(r.X, r.Y + 2, r.Width, 18), new StringFormat { Alignment = StringAlignment.Far });
            }
            else using (var b = new SolidBrush(Theme.Text)) g.DrawString($"{label} {level}", Theme.BodyBold, b, r.X, r.Y);
            if (up && !brief) using (var b = new SolidBrush(Theme.Accent)) g.DrawString("LEVEL UP!", Theme.Bold(9.5f), b, r.X + g.MeasureString($"{label} {level}", Theme.BodyBold).Width + 6, r.Y + 1);
            if (!brief) using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"+{(int)(gained * EaseOut(t))} XP · {into}/{need}", Theme.Small, m, new RectangleF(r.X, r.Y + 1, r.Width, 18), new StringFormat { Alignment = StringAlignment.Far });
            var bar = new RectangleF(r.X, r.Y + 22, r.Width, 9);
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) using (var p = StatsArt.Round(bar, 4.5f)) g.FillPath(b, p);
            // what was there before (this level), then what this game added
            float startFrac = level > lb ? 0 : (float)Progress.LevelOf(before).Into / need;
            float nowFrac = (float)into / need;
            if (nowFrac > 0.005f) using (var b = new SolidBrush(fill)) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(9, bar.Width * nowFrac), bar.Height), 4.5f)) g.FillPath(b, p);
            if (startFrac > 0.005f) using (var b = new SolidBrush(ControlPaint.Dark(fill, 0.25f))) using (var p = StatsArt.Round(new RectangleF(bar.X, bar.Y, Math.Max(9, bar.Width * Math.Min(startFrac, nowFrac)), bar.Height), 4.5f)) g.FillPath(b, p);
        }

        /// <summary>A control stick seen from above, with an arrow where to hold it (the DI that would have helped).</summary>
        public static void Stick(Graphics g, DeathTip t, RectangleF r, float pop = 1)
        {
            var c = t.Saved ? Theme.Good : Color.FromArgb(240, 180, 90);
            float s = 0.7f + 0.3f * EaseOut(pop);
            var box = new RectangleF(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
            using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(b, box);
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f)) g.DrawEllipse(pen, box);
            float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
            if (t.Stick == null) { using (var b = new SolidBrush(Theme.Muted)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8); return; }
            double a = t.ScreenAngle * Math.PI / 180;
            float len = box.Width / 2 - 3;
            var tip = new PointF(cx + (float)Math.Cos(a) * len, cy - (float)Math.Sin(a) * len);
            using (var pen = new Pen(c, 3.2f) { EndCap = LineCap.ArrowAnchor, StartCap = LineCap.Round }) g.DrawLine(pen, cx, cy, tip.X, tip.Y);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 3, cy - 3, 6, 6);
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

    /// <summary>The small card over the game right after it: the grade pops, the score counts up, XP floats up as the
    /// two bars fill, then the coach's main note (how to DI a hit that took a stock) and the move to look out for slide
    /// in, the move playing on a loop. Kept small so it doesn't cover the game; all the notes are in Stat Tracker &gt;
    /// Matches.</summary>
    class ReportCard : OverlayForm
    {
        GameProgress _p; GameSmash _s; GamePlayer _me, _them; string _stage;
        GameCoach _coach; DateTime _coachAt; Rectangle? _under;
        readonly Timer _timer = new Timer { Interval = 30 };
        DateTime _shown; public Rectangle? Game; public new bool Left;
        static readonly bool Testing = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") != null;
        float Seconds => Testing ? 90 : _coach != null && _coach.Any ? 24 : 16;
        public long GameId => _p?.GameId ?? 0;

        /// <summary>Sets up the timer that redraws the card while it animates and takes it away after a while.</summary>
        public ReportCard()
        {
            Size = new Size(CardWidth, 200); BaseOpacity = 0.93;
            Draggable = true; Cursor = Cursors.Hand;   // drag it anywhere over the game
            _timer.Tick += (s, e) =>
            {
                if ((DateTime.Now - _shown).TotalSeconds > Seconds) { _timer.Stop(); Disappear(); return; }
                Invalidate();
            };
        }

        // the card is kept small so it doesn't cover the game: the result, both level bars, one line of badges and bests,
        // the coach's main note and the move to look out for (the rest of the coach's notes are in Stat Tracker > Matches)
        const int CardWidth = 340;
        public const int PictureWidth = 132, PictureHeight = 94;   // the "look out for" picture, drawn at this size
        bool HasNews => _p.NewBadges.Count > 0 || _p.NewBests.Count > 0;
        int BaseHeight => 128 + (HasNews ? 20 : 0);
        int CoachHeight => _coach == null || !_coach.Any ? 0 : 22 + (_coach.Tips.Count > 0 ? 40 : _coach.Combo != null ? 34 : 0) + (_coach.Watch != null ? PictureHeight + 22 : 0);

        /// <summary>Shows the card for a game that just ended, starting its animations and its time on screen.</summary>
        public void Show(GameProgress p, GameSmash s, GamePlayer me, GamePlayer them, string stage, GameCoach coach = null)
        {
            _p = p; _s = s; _me = me; _them = them; _stage = stage; _coach = coach; _coachAt = DateTime.Now.AddSeconds(2.6);
            _shown = DateTime.Now;
            Fit();
            _timer.Start();
            Appear();
            Invalidate();
        }

        /// <summary>The coach's notes, once worked out (they slide in under the rest).</summary>
        public void SetCoach(GameCoach c)
        {
            if (c == null || !Visible) return;
            _coach = c; _coachAt = DateTime.Now > _shown.AddSeconds(2.6) ? DateTime.Now : _shown.AddSeconds(2.6);
            Fit(); Invalidate();
        }

        /// <summary>Sizes the card to what it has to show and puts it back in its corner.</summary>
        void Fit()
        {
            Size = new Size(CardWidth, BaseHeight + CoachHeight);
            Place();
        }

        /// <summary>Right under the opponent card (in its corner), or in that corner when the card isn't up.</summary>
        public void Place(Rectangle? card = null)
        {
            if (card != null) _under = card;
            var area = Game ?? Screen.PrimaryScreen.WorkingArea;
            if (Spot != null) { var to = AtSpot(area, Spot.Value); if (Location != to) Location = to; return; }   // where you dragged it
            int x = Left ? area.X + 14 : area.Right - Width - 14;
            int y = _under != null && _under.Value.Height > 0 ? _under.Value.Bottom + 10 : area.Y + 14;
            if (y + Height > area.Bottom - 10) y = Math.Max(area.Y + 14, area.Bottom - 10 - Height);
            var at = new Point(x, y);
            if (Location != at) Location = at;
        }

        protected override Rectangle DragArea => Game ?? Screen.PrimaryScreen.WorkingArea;
        // it takes clicks (to be dragged), unlike the slim bar during the fight
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle &= ~0x20; return cp; } }

        float T(double from, double length) => (float)Math.Max(0, Math.Min(1, ((DateTime.Now - _shown).TotalSeconds - from) / length));
        float Since(DateTime at, double length) => (float)Math.Max(0, Math.Min(1, (DateTime.Now - at).TotalSeconds / length));

        protected override void PaintContent(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (_p == null) return;
            var gc = ReportArt.GradeColor(_p.Grade);
            using (var border = new Pen(gc, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            bool won = _me?.Won == true;
            // top: the grade, the result
            ReportArt.Grade(g, _p.Grade, _p.Score, new RectangleF(10, 10, 56, 56), T(0, 0.5));
            float x = 76;
            Text(g, won ? "WIN" : "LOSS", Theme.Bold(14f), won ? Theme.Good : Theme.Bad, new RectangleF(x, 8, 70, 26));
            string stocks = _s?.MyStocksLeft != null ? (won ? $"{_s.MyStocksLeft} stock{(_s.MyStocksLeft == 1 ? "" : "s")} left" : $"took {_s.Kills} stock{(_s.Kills == 1 ? "" : "s")}") : "";
            Text(g, stocks, Theme.Small, Theme.Text, new RectangleF(x + 62, 13, Width - x - 72, 18));
            Text(g, $"vs {_them?.Name} ({Fighters.Name(_them?.Character ?? 0)})" + (_stage != null ? " · " + _stage : ""), Theme.Small, Theme.Muted, new RectangleF(x, 32, Width - x - 10, 18));
            int score = (int)Math.Round(_p.Score * ReportArt.EaseOut(T(0.1, 0.8)));
            Text(g, $"Score {score} · better than {_p.Percentile}% of your games" + (_p.WinStreak >= 2 ? $" · {_p.WinStreak} in a row" : ""), Theme.Small, gc, new RectangleF(x, 49, Width - x - 10, 18));
            // the two bars side by side, and each bit of XP floating up off them as they fill
            float y = 76, half = (Width - 30) / 2f;
            float fill = T(0.5, 1.6);
            ReportArt.XpBar(g, "Level", _p.XpBefore, _p.Xp, new RectangleF(10, y, half, 34), fill, OverlayLook.Accent, true);
            for (int i = 0; i < _p.XpParts.Count; i++)
            {
                float t = T(0.5 + i * 1.4 / Math.Max(1, _p.XpParts.Count), 1.1);
                if (t <= 0 || t >= 1) continue;
                int alpha = (int)(255 * (t < 0.2f ? t / 0.2f : 1 - (t - 0.2f) / 0.8f));
                var part = _p.XpParts[i];
                Text(g, $"+{part.Xp} {part.Why}", Theme.Bold(9f), Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), OverlayLook.Accent), new RectangleF(10, y + 4 - 26 * ReportArt.EaseOut(t), Width - 20, 18));
            }
            bool levelUp = Progress.LevelOf(_p.XpBefore + _p.Xp).Level > Progress.LevelOf(_p.XpBefore).Level;
            if (levelUp) Burst(g, new PointF(40, y + 8), T(2.1, 0.9), OverlayLook.Accent);
            ReportArt.XpBar(g, Fighters.Name(_p.Character), _p.CharXpBefore, _p.Xp, new RectangleF(20 + half, y, half, 34), T(0.8, 1.6), Color.FromArgb(96, 182, 255), true);
            bool charUp = Progress.LevelOf(_p.CharXpBefore + _p.Xp).Level > Progress.LevelOf(_p.CharXpBefore).Level;
            if (charUp) Burst(g, new PointF(50 + half, y + 8), T(2.4, 0.9), Color.FromArgb(96, 182, 255));
            y += 44;
            // badges and personal bests: one line
            if (HasNews)
            {
                float t = T(2.0, 0.4);
                var news = _p.NewBadges.Select(b => b.Tier > 0 ? $"{b.Name} {Badge.Tiers[b.Tier]}" : b.Name).ToList();
                string line = (news.Count > 0 ? "★ " + string.Join(", ", news) : "") + (_p.NewBests.Count > 0 ? (news.Count > 0 ? " · " : "★ ") + $"new best: {_p.NewBests[0].Name} {_p.NewBests[0].Text}" : "");
                if (t > 0) Text(g, line, Theme.Small, Color.FromArgb((int)(255 * t), OverlayLook.Accent), new RectangleF(10 + 20 * (1 - ReportArt.EaseOut(t)), y, Width - 20, 18));
                y += 20;
            }
            // the coach's main note, then the move to look out for
            if (_coach != null && _coach.Any)
            {
                float t = Since(_coachAt, 0.5);
                if (t > 0)
                {
                    float off = 16 * (1 - ReportArt.EaseOut(t));
                    int a = (int)(255 * t);
                    using (var pen = new Pen(Color.FromArgb(a / 3, 255, 255, 255))) g.DrawLine(pen, 10, y + 2, Width - 10, y + 2);
                    y += 6;
                    if (_coach.Tips.Count > 0)
                    {
                        var tip = _coach.Tips[0];
                        ReportArt.Stick(g, tip, new RectangleF(10 + off, y + 3, 30, 30), t);
                        using (var b = new SolidBrush(Color.FromArgb(a, tip.Saved ? Theme.Text : Theme.Muted)))
                            g.DrawString(DeathCoach.Say(tip), Theme.Small, b, new RectangleF(46 + off, y + 2, Width - 56, 34), new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit });
                        y += 40;
                    }
                    else if (_coach.Combo != null)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(a, OverlayLook.Accent)))
                            g.DrawString(_coach.Combo, Theme.Small, b, new RectangleF(10 + off, y + 2, Width - 20, 32), new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit });
                        y += 34;
                    }
                    // look out for this move: their move around the moment it hits (on a loop, slowed down, holding on the
                    // hit), how much it did, and what to do on shield
                    var wo = _coach.Watch;
                    if (wo != null)
                    {
                        float tw = Since(_coachAt.AddSeconds(0.6), 0.5);
                        int aw = (int)(255 * tw);
                        Text(g, "LOOK OUT FOR", Theme.Bold(8f), Color.FromArgb(aw, Theme.Bad), new RectangleF(10 + off, y, 200, 16));
                        y += 18;
                        var pr = new Rectangle(10 + (int)off, (int)y, PictureWidth, PictureHeight);
                        using (var path = Theme.Rounded(pr, 6))
                        {
                            using (var b = new SolidBrush(Color.FromArgb(aw / 2, 0, 0, 0))) g.FillPath(b, path);
                            var pic = FrameOf(wo);
                            if (pic != null)
                            {
                                var clip = g.Clip; g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = tw };
                                using (var ia = new System.Drawing.Imaging.ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(pic, pr, 0, 0, pic.Width, pic.Height, GraphicsUnit.Pixel, ia); }
                                g.Clip = clip;
                            }
                            using (var pen = new Pen(Color.FromArgb(aw, Theme.Bad), 1.5f)) g.DrawPath(pen, path);
                        }
                        float tx = pr.Right + 10;
                        Text(g, Fighters.Name(wo.Character) + "'s " + wo.Move, Theme.BodyBold, Color.FromArgb(aw, Theme.Text), new RectangleF(tx, y, Width - tx - 8, 20));
                        string did = $"Hit you {wo.Times}× for {wo.Damage:0}%" + (wo.Stocks > 0 ? $", took {wo.Stocks} stock{(wo.Stocks == 1 ? "" : "s")}." : ".");
                        using (var b = new SolidBrush(Color.FromArgb(aw, Theme.Muted)))
                            g.DrawString(did, Theme.Small, b, new RectangleF(tx, y + 20, Width - tx - 8, 32), new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit });
                        var advice = wo.Advice;
                        if (advice != null)
                            using (var b = new SolidBrush(Color.FromArgb(aw, OverlayLook.Accent)))
                                g.DrawString(advice, Theme.Small, b, new RectangleF(tx, y + 50, Width - tx - 8, PictureHeight - 50), new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit });
                    }
                }
            }
            // how long it stays
            float left = 1 - (float)((DateTime.Now - _shown).TotalSeconds / Seconds);
            using (var b = new SolidBrush(Color.FromArgb(120, gc))) g.FillRectangle(b, 2, Height - 4, (Width - 4) * Math.Max(0, left), 2);
        }

        /// <summary>The picture of the move to look out for right now: it plays at a third of game speed, holds on the
        /// hit for half a second, and pauses a moment before going round again.</summary>
        static Bitmap FrameOf(WatchOut wo)
        {
            var f = wo.Frames;
            if (f == null || f.Count == 0) return null;
            int hold = 10, rest = 8, n = f.Count + hold + rest;                // in steps of 50 ms
            int step = (int)(Environment.TickCount / 50 % n); if (step < 0) step += n;
            int i = step <= wo.HitIndex ? step : step <= wo.HitIndex + hold ? wo.HitIndex : step - hold;
            return f[Math.Max(0, Math.Min(f.Count - 1, i))];
        }

        /// <summary>A little burst: a ring spreading out and sparks flying (t: 0..1).</summary>
        static void Burst(Graphics g, PointF at, float t, Color c)
        {
            if (t <= 0 || t >= 1) return;
            int a = (int)(220 * (1 - t));
            float r = 6 + 34 * ReportArt.EaseOut(t);
            using (var pen = new Pen(Color.FromArgb(a, c), 2.5f * (1 - t) + 0.5f)) g.DrawEllipse(pen, at.X - r, at.Y - r, r * 2, r * 2);
            for (int i = 0; i < 10; i++)
            {
                double ang = i * Math.PI / 5 + 0.3;
                float d1 = r * 0.7f, d2 = r * 1.15f;
                using (var pen = new Pen(Color.FromArgb(a, i % 2 == 0 ? c : Color.White), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, at.X + (float)Math.Cos(ang) * d1, at.Y + (float)Math.Sin(ang) * d1, at.X + (float)Math.Cos(ang) * d2, at.Y + (float)Math.Sin(ang) * d2);
            }
        }
    }
}
