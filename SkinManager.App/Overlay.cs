// The stats overlay over the game: where the game's window is, the full overlay (opened with a hotkey), and the hotkey.
// Overlays let clicks through to the game and never take focus from it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    static class GameWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);

        /// <summary>Where the game is drawn on screen (the biggest visible window of Brawl Online's Dolphin), or null.</summary>
        public static Rectangle? Find(BrawlOnlineInstall install)
        {
            try
            {
                var pids = new HashSet<uint>(Process.GetProcessesByName("Dolphin").Where(p =>
                {
                    try { return install == null || p.MainModule.FileName.StartsWith(install.NetplayFolder, StringComparison.OrdinalIgnoreCase); } catch { return true; }
                }).Select(p => (uint)p.Id));
                if (pids.Count == 0) return null;
                Rectangle? best = null;
                EnumWindows((h, l) =>
                {
                    GetWindowThreadProcessId(h, out uint pid);
                    if (!pids.Contains(pid) || !IsWindowVisible(h) || IsIconic(h)) return true;
                    if (!GetClientRect(h, out var r)) return true;
                    var p = new POINT();
                    ClientToScreen(h, ref p);
                    var rect = new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
                    if (rect.Width >= 320 && rect.Height >= 240 && (best == null || rect.Width * rect.Height > best.Value.Width * best.Value.Height)) best = rect;
                    return true;
                }, IntPtr.Zero);
                return best;
            }
            catch { return null; }   // not on Windows (tests) or no access
        }
    }

    /// <summary>A window over the game: see-through, clicks go to the game, never takes focus.</summary>
    /// <summary>The look of everything shown over the game: a theme (its colors, and a few little touches drawn in it:
    /// snow, bats, stars...), picked in Settings. "Seasonal" follows the calendar: Halloween in October, Christmas in December.</summary>
    static class OverlayLook
    {
        public sealed class Look
        {
            public string Id, Name; public Color Accent, Accent2, Back, Back2; public string Deco;
        }
        public static readonly Look[] Looks =
        {
            new Look { Id = "classic", Name = "Classic gold", Accent = Color.FromArgb(255, 176, 46), Accent2 = Color.FromArgb(96, 182, 255), Back = Color.FromArgb(14, 15, 20), Back2 = Color.FromArgb(24, 22, 30) },
            new Look { Id = "halloween", Name = "Halloween", Accent = Color.FromArgb(255, 132, 24), Accent2 = Color.FromArgb(170, 110, 255), Back = Color.FromArgb(18, 10, 24), Back2 = Color.FromArgb(40, 18, 44), Deco = "bats" },
            new Look { Id = "christmas", Name = "Christmas", Accent = Color.FromArgb(235, 70, 70), Accent2 = Color.FromArgb(80, 210, 120), Back = Color.FromArgb(10, 22, 18), Back2 = Color.FromArgb(18, 38, 30), Deco = "snow" },
            new Look { Id = "midnight", Name = "Midnight blue", Accent = Color.FromArgb(110, 190, 255), Accent2 = Color.FromArgb(200, 160, 255), Back = Color.FromArgb(8, 12, 26), Back2 = Color.FromArgb(16, 26, 50), Deco = "stars" },
            new Look { Id = "neon", Name = "Neon", Accent = Color.FromArgb(255, 80, 200), Accent2 = Color.FromArgb(60, 230, 255), Back = Color.FromArgb(14, 8, 22), Back2 = Color.FromArgb(30, 12, 40) },
            new Look { Id = "mono", Name = "Clean white", Accent = Color.FromArgb(235, 238, 245), Accent2 = Color.FromArgb(150, 160, 180), Back = Color.FromArgb(18, 19, 24), Back2 = Color.FromArgb(28, 30, 36) },
        };
        public const string Seasonal = "seasonal";

        public static string Choice = Seasonal;     // a Look id, "seasonal", or "custom"
        public static Color? CustomAccent;
        public static bool Effects = true;

        public static Look Current
        {
            get
            {
                string id = Choice ?? Seasonal;
                if (id == Seasonal) id = DateTime.Now.Month == 10 ? "halloween" : DateTime.Now.Month == 12 ? "christmas" : "classic";
                if (id == "custom")
                {
                    var c = CustomAccent ?? Looks[0].Accent;
                    return new Look { Id = "custom", Name = "Your color", Accent = c, Accent2 = Color.FromArgb(c.B, c.R, c.G), Back = Looks[0].Back, Back2 = Color.FromArgb(Math.Min(255, 14 + c.R / 12), Math.Min(255, 15 + c.G / 12), Math.Min(255, 20 + c.B / 12)) };
                }
                return Looks.FirstOrDefault(l => l.Id == id) ?? Looks[0];
            }
        }
        public static Color Accent => Current.Accent;
        public static bool Moving => Effects && Current.Deco != null;

        /// <summary>The background: a soft diagonal gradient in the theme's colors.</summary>
        public static void PaintBack(Graphics g, Rectangle r)
        {
            var l = Current;
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var b = new LinearGradientBrush(r, l.Back, l.Back2, 25f)) g.FillRectangle(b, r);
        }

        /// <summary>The theme's little touches, drawn over everything (faint, so the text stays easy to read).</summary>
        public static void Decorate(Graphics g, Rectangle r)
        {
            var l = Current;
            if (!Effects || l.Deco == null || r.Width < 40) return;
            double t = DateTime.Now.TimeOfDay.TotalSeconds;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rnd = new Random(r.Width * 31 + r.Height);
            if (l.Deco == "snow")
            {
                // falling snow, and a string of little lights along the top
                for (int i = 0; i < 26; i++)
                {
                    double speed = 10 + rnd.NextDouble() * 18, sway = rnd.NextDouble() * 6.28;
                    float x = (float)((rnd.NextDouble() * r.Width + Math.Sin(t * 0.8 + sway) * 6) % r.Width);
                    float y = (float)((rnd.NextDouble() * r.Height + t * speed) % (r.Height + 6)) - 3;
                    float sz = 1.5f + (float)rnd.NextDouble() * 2.2f;
                    using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillEllipse(b, x, y, sz, sz);
                }
                Color[] bulbs = { l.Accent, l.Accent2, Color.FromArgb(255, 210, 80), Color.FromArgb(90, 170, 255) };
                for (int i = 0, n = Math.Max(4, r.Width / 34); i < n; i++)
                {
                    float x = 10 + i * (r.Width - 20f) / Math.Max(1, n - 1), y = 3 + (i % 2) * 2;
                    bool on = ((int)(t * 2) + i) % 3 != 0;
                    using (var b = new SolidBrush(Color.FromArgb(on ? 220 : 70, bulbs[i % bulbs.Length]))) g.FillEllipse(b, x - 2.5f, y, 5, 6);
                }
            }
            else if (l.Deco == "bats")
            {
                // a couple of bats flapping across, and a little pumpkin in the corner
                for (int i = 0; i < 3; i++)
                {
                    double speed = 22 + i * 9, phase = rnd.NextDouble() * 500;
                    float x = (float)((t * speed + phase) % (r.Width + 60)) - 30;
                    float y = 10 + (float)(rnd.NextDouble() * Math.Max(10, r.Height - 30)) + (float)Math.Sin(t * 2 + i) * 4;
                    float flap = (float)Math.Sin(t * 12 + i * 2) * 4;
                    var pts = new[] { new PointF(x, y), new PointF(x - 5, y - 3 - flap), new PointF(x - 9, y - 1 - flap), new PointF(x - 6, y + 1), new PointF(x, y + 3), new PointF(x + 6, y + 1), new PointF(x + 9, y - 1 - flap), new PointF(x + 5, y - 3 - flap) };
                    using (var b = new SolidBrush(Color.FromArgb(150, 8, 4, 12))) g.FillPolygon(b, pts);
                    using (var pen = new Pen(Color.FromArgb(170, l.Accent2), 1.2f)) g.DrawPolygon(pen, pts);
                }
                float px = 6, py = r.Height - 18, glow = 0.6f + 0.4f * (float)Math.Sin(t * 3);
                using (var b = new SolidBrush(Color.FromArgb(150, l.Accent))) { g.FillEllipse(b, px, py, 10, 12); g.FillEllipse(b, px + 5, py, 10, 12); g.FillEllipse(b, px + 2.5f, py, 10, 12); }
                using (var b = new SolidBrush(Color.FromArgb(150, 60, 140, 60))) g.FillRectangle(b, px + 7, py - 3, 2.5f, 4);
                using (var b = new SolidBrush(Color.FromArgb((int)(220 * glow), 255, 230, 120)))
                {
                    g.FillPolygon(b, new[] { new PointF(px + 5, py + 4), new PointF(px + 7, py + 4), new PointF(px + 6, py + 6) });
                    g.FillPolygon(b, new[] { new PointF(px + 11, py + 4), new PointF(px + 13, py + 4), new PointF(px + 12, py + 6) });
                    g.FillRectangle(b, px + 6, py + 8, 7, 1.5f);
                }
            }
            else if (l.Deco == "stars")
            {
                for (int i = 0; i < 18; i++)
                {
                    float x = (float)(rnd.NextDouble() * r.Width), y = (float)(rnd.NextDouble() * r.Height);
                    double tw = 0.5 + 0.5 * Math.Sin(t * (1.5 + rnd.NextDouble() * 2) + i);
                    float sz = 1 + (float)tw * 1.8f;
                    using (var b = new SolidBrush(Color.FromArgb((int)(40 + 110 * tw), 220, 230, 255))) g.FillEllipse(b, x - sz / 2, y - sz / 2, sz, sz);
                }
            }
            g.Restore(state);
        }
    }

    class OverlayForm : Form
    {
        public double BaseOpacity = 0.86;   // how see-through it is when fully in
        double _fade = 1; int _dir;         // fading in (1) or out (-1)
        readonly Timer _fader = new Timer { Interval = 15 };
        static readonly Timer Effects = new Timer { Interval = 50 };
        static readonly System.Collections.Generic.List<OverlayForm> Open = new System.Collections.Generic.List<OverlayForm>();

        static OverlayForm()
        {
            Effects.Tick += (s, e) => { if (!OverlayLook.Moving) return; foreach (var f in Open.ToArray()) if (f.Visible && f._fade > 0) f.Invalidate(); };
            Effects.Start();
        }

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(14, 15, 20); DoubleBuffered = true; Opacity = BaseOpacity;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            _fader.Tick += (s, e) =>
            {
                _fade = Math.Max(0, Math.Min(1, _fade + _dir * 0.09));
                ApplyOpacity();
                if (_dir < 0 && _fade <= 0) { _fader.Stop(); base.Hide(); }
                else if (_dir > 0 && _fade >= 1) _fader.Stop();
            };
            Open.Add(this);
            Disposed += (s, e) => Open.Remove(this);
        }

        /// <summary>The see-through amount it settles at (applied smoothly while fading).</summary>
        public void SetOpacity(double o) { BaseOpacity = o; ApplyOpacity(); }
        void ApplyOpacity() { double e = 1 - (1 - _fade) * (1 - _fade); double o = BaseOpacity * e; if (Math.Abs(Opacity - o) > 0.002) Opacity = o; }

        /// <summary>Shows it, fading in.</summary>
        public void Appear()
        {
            if (Visible && _dir >= 0) return;
            if (!Visible) { _fade = 0; ApplyOpacity(); base.Show(); }
            _dir = 1; _fader.Start();
        }

        /// <summary>Hides it, fading out.</summary>
        public void Disappear()
        {
            if (!Visible || _dir < 0) return;
            _dir = -1; _fader.Start();
        }

        public bool Fading => _dir < 0;

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            // no activation, tool window, topmost, layered + transparent to the mouse
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x80000 | 0x20; return cp; }
        }
        protected override void OnPaintBackground(PaintEventArgs e) => OverlayLook.PaintBack(e.Graphics, ClientRectangle);
        protected sealed override void OnPaint(PaintEventArgs e)
        {
            PaintContent(e);
            OverlayLook.Decorate(e.Graphics, ClientRectangle);
        }
        protected virtual void PaintContent(PaintEventArgs e) { }
        protected static void Text(Graphics g, string t, Font f, Color c, RectangleF r, bool center = false)
        {
            using (var b = new SolidBrush(c)) g.DrawString(t ?? "", f, b, r, new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, Alignment = center ? StringAlignment.Center : StringAlignment.Near });
        }
    }

    /// <summary>The full overlay: everything about the opponent (or, outside a match, about you), opened with the hotkey.</summary>
    class FullOverlay : OverlayForm
    {
        public LiveState State; public MatchHistory History; public StatsArt Art; public string Hotkey = "Alt+T";
        public Func<string, List<(string Habit, string Tip)>> Scout;

        public FullOverlay() { Size = new Size(860, 520); }

        public void Place(Rectangle? game)
        {
            var area = game ?? Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(860, area.Width - 40), Math.Min(520, area.Height - 40));
            Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
        }

        static Color Rate(double r) => RecordTable.RateColor(r);

        /// <summary>One line of a list: a picture, a name, a record and its win rate.</summary>
        void Row(Graphics g, ref float y, float x, float w, Image icon, bool wide, string name, Record r, string note = null)
        {
            float tx = x;
            if (icon != null)
            {
                var box = wide ? new RectangleF(x, y + 2, 74, 28) : new RectangleF(x, y, 28, 32);
                StatsArt.Cover(g, icon, box, 4, false, 0.5f, wide ? 0.5f : 0f);
                tx = box.Right + 8;
            }
            Text(g, name, Theme.BodyBold, Theme.Text, new RectangleF(tx, y + (note != null ? 0 : 6), w - (tx - x) - 92, 20));
            if (note != null) Text(g, note, Theme.Small, Theme.Muted, new RectangleF(tx, y + 17, w - (tx - x) - 92, 16));
            if (r.Games > 0)
            {
                Text(g, r.ToString(), Theme.Body, Theme.Text, new RectangleF(x + w - 88, y + 6, 46, 20));
                Text(g, r.Percent, Theme.BodyBold, Rate(r.Rate), new RectangleF(x + w - 42, y + 6, 42, 20));
            }
            y += 36;
        }

        void Heading(Graphics g, ref float y, float x, float w, string t) { Text(g, t.ToUpperInvariant(), Theme.Small, OverlayLook.Accent, new RectangleF(x, y, w, 18)); y += 22; }

        protected override void PaintContent(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var border = new Pen(OverlayLook.Accent, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            var h = History; var s = State;
            Text(g, $"Press {Hotkey} to close", Theme.Small, Theme.Muted, new RectangleF(Width - 200, Height - 24, 186, 18));
            if (h == null) { Text(g, Program.HistoryProblem ?? "Reading your match history…", Theme.Heading, Theme.Text, new RectangleF(20, 20, Width - 40, 30)); return; }
            bool match = s != null && s.OpponentName != null && s.Phase != MatchPhase.None && s.Phase != MatchPhase.Searching;
            if (match) Opponent(g, h, s); else Yourself(g, h);
        }

        /// <summary>Top: the two characters face to face over the stage, the chance to win in the middle.</summary>
        void Banner(Graphics g, Image stage, Image left, Image right, string title, string sub, double? chance, string chanceNote)
        {
            var top = new RectangleF(2, 2, Width - 5, 150);
            if (stage != null) { StatsArt.Cover(g, stage, top, 0); using (var b = new SolidBrush(Color.FromArgb(160, 12, 13, 18))) g.FillRectangle(b, top); }
            if (left != null) StatsArt.Cover(g, left, new RectangleF(14, 10, 116, 134), 8, false, 0.5f, 0.1f);
            if (right != null) StatsArt.Cover(g, right, new RectangleF(Width - 130, 10, 116, 134), 8, true, 0.5f, 0.1f);
            float cx = Width / 2f;
            Text(g, title, Theme.Title, Theme.Text, new RectangleF(140, 14, Width - 280, 30), true);
            Text(g, sub, Theme.Small, Theme.Text, new RectangleF(140, 46, Width - 280, 18), true);
            if (chance != null)
            {
                Text(g, $"{chance.Value * 100:0}%", Theme.Bold(30f), Rate(chance.Value), new RectangleF(140, 66, Width - 280, 50), true);
                Text(g, chanceNote, Theme.Small, Theme.Text, new RectangleF(140, 118, Width - 280, 18), true);
            }
        }

        void Opponent(Graphics g, MatchHistory h, LiveState s)
        {
            string oppId = s.OpponentCode != null ? h.UserIdOf(s.OpponentCode) : null;
            var all = h.Singles().ToList();
            var vs = all.Where(x => oppId != null && x.Them.UserId == oppId).ToList();
            var p = h.Project(s.MyChar, s.TheirChar, oppId, s.Stage);
            string mode = s.Mode == "ranked" ? "Ranked" : s.Mode == "unranked" ? "Unranked" : s.Mode == "direct" ? "Direct" : s.Mode;
            Banner(g, Art?.Stage(s.Stage), Art?.Portrait(s.MyChar), Art?.Portrait(s.TheirChar), "vs " + s.OpponentName,
                $"{s.OpponentCode} · {mode} · game {s.Game}" + (s.MyWins + s.TheirWins > 0 ? $" · set {s.MyWins}–{s.TheirWins}" : "") + (s.Stage != null ? " · " + h.StageName(s.Stage.Value) : ""),
                p.Chance, $"chance to win · from {p.Evidence} matching games");
            float colW = (Width - 4 * 18) / 3f, y0 = 166;

            // 1: them
            float x = 18, y = y0;
            var r = MatchHistory.Count(vs);
            Heading(g, ref y, x, colW, vs.Count == 0 ? "First time playing them" : $"Head-to-head: {r} ({r.Percent})");
            foreach (var c in vs.GroupBy(v => v.Them.Character).OrderByDescending(v => v.Count()).Take(5))
                Row(g, ref y, x, colW, Art?.Head(c.Key), false, Fighters.Name(c.Key), MatchHistory.Count(c), $"{c.Count()} of {vs.Count} games");
            if (vs.Count == 0) Text(g, "Nothing kept about them yet: the matchup and stage records still count.", Theme.Small, Theme.Muted, new RectangleF(x, y, colW, 40));
            var scout = Scout != null && oppId != null ? Scout(oppId) : new List<(string Habit, string Tip)>();
            if (scout.Count > 0)
            {
                y += 4; Heading(g, ref y, x, colW, "They tend to");
                foreach (var l in scout.Take(3))
                {
                    if (y > Height - 60) break;
                    Text(g, l.Habit, Theme.Small, Theme.Text, new RectangleF(x, y, colW, 18));
                    Text(g, "→ " + l.Tip, Theme.Small, OverlayLook.Accent, new RectangleF(x, y + 16, colW, 18));
                    y += 38;
                }
            }

            // 2: the matchup and your picks against their character
            x += colW + 18; y = y0;
            if (s.TheirChar != null)
            {
                Heading(g, ref y, x, colW, $"Your picks vs {Fighters.Name(s.TheirChar.Value)}");
                var picks = h.Counterpicks(s.TheirChar.Value, 4);
                if (picks.Count == 0) Text(g, "Not enough games against it yet (2+ with a character).", Theme.Small, Theme.Muted, new RectangleF(x, y, colW, 36));
                foreach (var pk in picks) Row(g, ref y, x, colW, Art?.Head(pk.Character), false, Fighters.Name(pk.Character), pk.Versus, pk.Character == s.MyChar ? "what you're playing" : null);
            }
            else { Heading(g, ref y, x, colW, "Their character"); Text(g, "Shows once the game loads.", Theme.Small, Theme.Muted, new RectangleF(x, y, colW, 20)); }

            // 3: stages
            x += colW + 18; y = y0;
            Heading(g, ref y, x, colW, "Your best stages for this");
            foreach (var st in h.BestStages(s.LegalStages.Count > 0 ? s.LegalStages : null, s.TheirChar, oppId, 4))
                Row(g, ref y, x, colW, Art?.Stage(st.Stage), true, h.StageName(st.Stage), st.Record);
            y += 6;
            Heading(g, ref y, x, colW, "What the chance is based on");
            foreach (var f in p.Factors.Where(f => f.Record.Games > 0))
                Text(g, $"{f.What}: {f.Record} ({f.Record.Percent})", Theme.Small, Rate(f.Record.Rate), new RectangleF(x, (y += 18) - 18, colW, 18));
        }

        void Yourself(Graphics g, MatchHistory h)
        {
            var all = h.Singles().ToList();
            var overall = MatchHistory.Count(all);
            var last = all.OrderByDescending(x => x.Game.Start).Take(20).Reverse().ToList();
            int? main = all.GroupBy(x => x.Me.Character).OrderByDescending(x => x.Count()).Select(x => (int?)x.Key).FirstOrDefault();
            Banner(g, null, Art?.Portrait(main), null, $"{h.MyName}  ({h.MyCode})", $"{overall} · {overall.Percent} in {overall.Games} games · last 20: {MatchHistory.Count(last)}", null, null);
            float sx = 140, sy = 74, size = Math.Min(16, (Width - 280) / 22f);
            float startX = Width / 2f - last.Count * (size + 3) / 2f;
            foreach (var won in last.Select(x => x.Me.Won == true)) { using (var b = new SolidBrush(won ? Theme.Good : Theme.Bad)) g.FillRectangle(b, startX, sy + 20, size, size); startX += size + 3; }
            Text(g, "No match right now: your summary", Theme.Small, Theme.Muted, new RectangleF(sx, sy + 50, Width - 280, 18), true);
            float colW = (Width - 4 * 18) / 3f, y0 = 166;
            float x = 18, y = y0;
            Heading(g, ref y, x, colW, "Your characters");
            foreach (var c in h.By(v => v.Me.Character).Take(6)) Row(g, ref y, x, colW, Art?.Head(c.Key), false, Fighters.Name(c.Key), c.Record);
            x += colW + 18; y = y0;
            Heading(g, ref y, x, colW, "Your stages");
            foreach (var c in h.By(v => v.Game.Stage).Take(6)) Row(g, ref y, x, colW, Art?.Stage(c.Key), true, h.StageName(c.Key), c.Record);
            x += colW + 18; y = y0;
            Heading(g, ref y, x, colW, "Hardest to face (5+ games)");
            foreach (var c in h.By(v => v.Them.Character).Where(c => c.Record.Games >= 5).OrderBy(c => c.Record.Rate).Take(6)) Row(g, ref y, x, colW, Art?.Head(c.Key), false, Fighters.Name(c.Key), c.Record);
        }
    }

    /// <summary>A system-wide hotkey (works while the game has focus).</summary>
    static class Hotkeys
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        public const int WM_HOTKEY = 0x0312, Id = 0x5354;
        public static readonly string[] Choices = { "Alt+T", "Alt+S", "Ctrl+Alt+S", "F9", "F10", "Off" };

        /// <summary>Registers the chosen hotkey for this window (any earlier one is let go); false if Windows refused it
        /// (another program already uses it).</summary>
        public static bool Set(IntPtr hwnd, string choice)
        {
            try
            {
                UnregisterHotKey(hwnd, Id);
                if (choice == null || choice == "Off") return true;
                uint mods = 0x4000;   // no repeat
                if (choice.Contains("Ctrl+")) mods |= 0x2;
                if (choice.Contains("Alt+")) mods |= 0x1;
                string key = choice.Split('+').Last();
                var k = (Keys)Enum.Parse(typeof(Keys), key);
                return RegisterHotKey(hwnd, Id, mods, (uint)k);
            }
            catch { return false; }   // not on Windows (tests)
        }
    }
}
