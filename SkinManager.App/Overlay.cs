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
    class OverlayForm : Form
    {
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(14, 15, 20); DoubleBuffered = true; Opacity = 0.86;
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            // no activation, tool window, topmost, layered + transparent to the mouse
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x80000 | 0x20; return cp; }
        }
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

        void Heading(Graphics g, ref float y, float x, float w, string t) { Text(g, t.ToUpperInvariant(), Theme.Small, Theme.Accent, new RectangleF(x, y, w, 18)); y += 22; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var border = new Pen(Theme.Accent, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            var h = History; var s = State;
            Text(g, $"Press {Hotkey} to close", Theme.Small, Theme.Muted, new RectangleF(Width - 200, Height - 24, 186, 18));
            if (h == null) { Text(g, "Reading your match history…", Theme.Heading, Theme.Text, new RectangleF(20, 20, Width - 40, 30)); return; }
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
                    Text(g, "→ " + l.Tip, Theme.Small, Theme.Accent, new RectangleF(x, y + 16, colW, 18));
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
