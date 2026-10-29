// Short guided tours: the first time you open a page (or with the "?" button), the window dims and a card walks
// through the page's parts one at a time, outlining each. Can be switched off in Settings.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SkinManager.App
{
    sealed class TutorialStep
    {
        public string Title, Text;
        public Func<Control> Target;   // the part to outline (null: the whole page)
        public TutorialStep(string title, string text, Func<Control> target = null) { Title = title; Text = text; Target = target; }
    }

    /// <summary>Draws over the whole window: a picture of it, dimmed, with the current part lit and outlined, and the card.</summary>
    class TutorialOverlay : Control
    {
        readonly List<TutorialStep> _steps; readonly string _page;
        int _at;
        Bitmap _shot;
        Rectangle _card, _back, _next, _skip, _never;
        public event Action Finished, TurnedOff;

        public TutorialOverlay(string page, List<TutorialStep> steps)
        {
            _page = page; _steps = steps;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Default; TabStop = true;
        }

        /// <summary>Starts over the given window (takes its picture first).</summary>
        public void Start(Form owner)
        {
            Bounds = owner.ClientRectangle;
            _shot = new Bitmap(Math.Max(1, owner.ClientSize.Width), Math.Max(1, owner.ClientSize.Height));
            try
            {
                using (var g = Graphics.FromImage(_shot))
                    g.CopyFromScreen(owner.PointToScreen(Point.Empty), Point.Empty, owner.ClientSize);
            }
            catch { try { owner.DrawToBitmap(_shot, new Rectangle(Point.Empty, owner.ClientSize)); } catch { } }
            owner.Controls.Add(this); BringToFront(); Focus();
            owner.Resize += Close;
            Invalidate();
        }

        void Close(object s, EventArgs e) => Finish();
        void Finish()
        {
            var f = FindForm(); if (f != null) f.Resize -= Close;
            Parent?.Controls.Remove(this);
            _shot?.Dispose();
            Finished?.Invoke();
            Dispose();
        }

        protected override bool IsInputKey(Keys k) => true;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Finish();
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Go(1);
            else if (e.KeyCode == Keys.Left) Go(-1);
        }

        void Go(int d)
        {
            int n = _at + d;
            if (n >= _steps.Count) { Finish(); return; }
            _at = Math.Max(0, n); Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_next.Contains(e.Location)) Go(1);
            else if (_back.Contains(e.Location) && _at > 0) Go(-1);
            else if (_skip.Contains(e.Location)) Finish();
            else if (_never.Contains(e.Location)) { TurnedOff?.Invoke(); Finish(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hand = _next.Contains(e.Location) || (_back.Contains(e.Location) && _at > 0) || _skip.Contains(e.Location) || _never.Contains(e.Location);
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        Rectangle? TargetRect()
        {
            try
            {
                var c = _steps[_at].Target?.Invoke();
                if (c == null || !c.Visible || c.Width < 4) return null;
                var r = RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                r.Intersect(ClientRectangle);
                return r.Width > 4 && r.Height > 4 ? Rectangle.Inflate(r, 6, 6) : (Rectangle?)null;
            }
            catch { return null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (_shot != null) g.DrawImageUnscaled(_shot, 0, 0); else g.Clear(Theme.Back);
            var spot = TargetRect();
            using (var veil = new Region(ClientRectangle))
            {
                if (spot != null) using (var p = StatsArt.Round(spot.Value, 10)) veil.Exclude(p);
                using (var b = new SolidBrush(Color.FromArgb(spot != null ? 175 : 150, 8, 9, 13))) g.FillRegion(b, veil);
            }
            if (spot != null) using (var pen = new Pen(Theme.Accent, 3)) using (var p = StatsArt.Round(spot.Value, 10)) g.DrawPath(pen, p);

            // the card: under the lit part if there's room, else above it, else in the middle
            var step = _steps[_at];
            int w = Math.Min(460, Width - 40);
            var textSize = TextRenderer.MeasureText(g, step.Text, Theme.Body, new Size(w - 40, 1000), TextFormatFlags.WordBreak);
            int h = 92 + textSize.Height + 50;
            int x, y;
            if (spot == null) { x = (Width - w) / 2; y = (Height - h) / 2; }
            else
            {
                var s = spot.Value;
                x = Math.Max(20, Math.Min(Width - w - 20, s.X + s.Width / 2 - w / 2));
                if (s.Bottom + 14 + h < Height - 10) y = s.Bottom + 14;
                else if (s.Top - 14 - h > 10) y = s.Top - 14 - h;
                else { y = Math.Max(10, (Height - h) / 2); x = s.Right + 20 + w < Width ? s.Right + 20 : Math.Max(20, s.Left - 20 - w); }
            }
            _card = new Rectangle(x, y, w, h);
            using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) using (var p = StatsArt.Round(new RectangleF(x + 4, y + 6, w, h), 12)) g.FillPath(shadow, p);
            using (var bg = new SolidBrush(Theme.Panel)) using (var p = StatsArt.Round(_card, 12)) g.FillPath(bg, p);
            using (var pen = new Pen(Theme.Accent, 1.5f)) using (var p = StatsArt.Round(_card, 12)) g.DrawPath(pen, p);
            using (var m = new SolidBrush(Theme.Accent)) g.DrawString($"{_page.ToUpperInvariant()}  ·  {_at + 1} OF {_steps.Count}", Theme.Small, m, x + 20, y + 16);
            using (var t = new SolidBrush(Theme.Text)) g.DrawString(step.Title, Theme.Heading, t, x + 20, y + 36);
            TextRenderer.DrawText(g, step.Text, Theme.Body, new Rectangle(x + 20, y + 66, w - 40, textSize.Height + 4), Theme.Muted, TextFormatFlags.WordBreak);
            // dots
            for (int i = 0; i < _steps.Count; i++) using (var b = new SolidBrush(i == _at ? Theme.Accent : Color.FromArgb(70, 255, 255, 255))) g.FillEllipse(b, x + 20 + i * 12, y + h - 30, 7, 7);
            // buttons
            string nextText = _at == _steps.Count - 1 ? "Got it" : "Next  →";
            var ns = TextRenderer.MeasureText(nextText, Theme.BodyBold);
            _next = new Rectangle(x + w - ns.Width - 46, y + h - 42, ns.Width + 26, 30);
            using (var b = new SolidBrush(Theme.Accent)) using (var p = StatsArt.Round(_next, 6)) g.FillPath(b, p);
            TextRenderer.DrawText(g, nextText, Theme.BodyBold, _next, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _back = new Rectangle(_next.Left - 74, _next.Top, 66, 30);
            if (_at > 0) TextRenderer.DrawText(g, "←  Back", Theme.Body, _back, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _skip = new Rectangle(x + w - 70, y + 12, 56, 20);
            TextRenderer.DrawText(g, "Skip ✕", Theme.Small, _skip, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            int dotsRight = x + 20 + _steps.Count * 12 + 10;
            _never = new Rectangle(dotsRight, y + h - 38, Math.Max(0, _back.Left - dotsRight - 6), 24);
            if (_never.Width > 120) TextRenderer.DrawText(g, "Don't show tours", Theme.Small, _never, Color.FromArgb(150, Theme.Muted), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            else _never = Rectangle.Empty;
        }
    }

    /// <summary>The tours of each page.</summary>
    static class Tutorials
    {
        static Control Find(Control root, Func<Control, bool> match)
        {
            if (root == null) return null;
            foreach (Control c in root.Controls)
            {
                if (c.Visible && match(c)) return c;
                var inner = Find(c, match);
                if (inner != null) return inner;
            }
            return null;
        }
        static Func<Control> Button(Control root, string text) => () => Find(root, c => c is Button b && b.Text.Replace("&&", "&").StartsWith(text, StringComparison.OrdinalIgnoreCase));
        static Func<Control> Of<T>(Control root, Func<T, bool> ok = null) where T : Control => () => Find(root, c => c is T t && (ok == null || ok(t)));
        static Func<Control> Biggest(Control root, Func<Control, bool> ok) => () =>
        {
            Control best = null; long area = 0;
            void Walk(Control p) { foreach (Control c in p.Controls) { if (c.Visible && ok(c) && (long)c.Width * c.Height > area) { area = (long)c.Width * c.Height; best = c; } Walk(c); } }
            if (root != null) Walk(root);
            return best;
        };

        public static List<TutorialStep> For(string page, Control p, Control header)
        {
            switch (page)
            {
                case "My Mods": return new List<TutorialStep>
                {
                    new TutorialStep("Your mods", "Everything you've added lives here: costumes, stages, music and texture packs. Nothing changes on your SD card until you click Save in the gold bar that appears at the bottom."),
                    new TutorialStep("Kinds of mods", "Switch between Skins, Stages, Music and Textures.", Button(p, "Skins")),
                    new TutorialStep("Filters", "Show only what's on or off, your favorites, mods with a newer version (Downloaded), or two mods fighting for the same slot (Conflicts).", Button(p, "All")),
                    new TutorialStep("Each card is one mod", "Flip its switch to turn it on or off, click ☆ to favorite it, Delete to remove it. Turned-off mods stay here, ready to come back.", Biggest(p, c => c is FlowLayoutPanel)),
                    new TutorialStep("Add mods", "Add a downloaded zip, rar or 7z, or a file from an extracted mod. Your own MP3s work too: they become songs (Music tab: \"+ Add your own music\"). You can also drag files onto the window.", Button(header, "Add from file")),
                    new TutorialStep("Play", "Launch modded plays P+ with your mods. Launch unmodded plays P+ as it comes, without touching your mods.", Button(header, "Launch modded")),
                };
                case "Mod Store": return new List<TutorialStep>
                {
                    new TutorialStep("The Mod Store", "Mods from GameBanana, ready to install with one click. Every download is checked before anything touches your SD card."),
                    new TutorialStep("What to browse", "Costumes, stages or music.", Button(p, "Skins")),
                    new TutorialStep("Find something", "Search by name, pick a character, and sort by most downloaded, newest and more.", Of<TextBox>(p)),
                    new TutorialStep("Pick a mod", "Click a mod to see its pictures and what's inside on the right, then Install. Mods you already have are marked.", Biggest(p, c => c is FlowLayoutPanel)),
                    new TutorialStep("Favorites", "Star mods to find them again here.", Button(p, "★ Favorites")),
                };
                case "Costume Slots": return new List<TutorialStep>
                {
                    new TutorialStep("Pick a character", "Every P+ fighter, with how many of your skins are on.", Of<ListBox>(p)),
                    new TutorialStep("Their costumes", "Every costume P+ has for them, in select-screen order. Your skins show in the slot they replace; click a slot to put a skin there or change it.", Biggest(p, c => c is FlowLayoutPanel)),
                };
                case "Model Swapper": return new List<TutorialStep>
                {
                    new TutorialStep("Model Swapper (work in progress)", "Put a 3D model from another game or program on a P+ character. It keeps P+'s skeleton and animations, so hits stay the same for both players. Results still vary from model to model."),
                    new TutorialStep("Best results: start from P+'s own model", "\"Save P+ Model as 3D\" gives you the character's real model. Re-texture it in a tool like Meshy (describe a look or give it a picture), then load the result here: it fits the skeleton exactly.", Button(p, "Save P+ Model as 3D")),
                    new TutorialStep("Or bring any model", "Choose a .glb, .gltf, .obj or .dae file. Tools like Tripo3D can make one from a picture; \"Save P+ Model Pictures\" gives them P+'s proportions to copy.", Button(p, "Choose a model")),
                    new TutorialStep("Which character and costume", "Pick the fighter and the costume slot whose skeleton it uses.", Of<ComboBox>(p)),
                    new TutorialStep("Fix the fit", "If arms or legs look off, \"Line up the joints\" lets you drag dots onto the joints. Try a few poses with \"Show it\".", Button(p, "Line up the joints")),
                    new TutorialStep("Make it a costume", "When it looks right in a few poses, make it a costume. It goes through the same safety checks as any skin.", Button(p, "Make it a costume")),
                };
                case "Settings": return new List<TutorialStep>
                {
                    new TutorialStep("Settings", "The app's background, where your files are, updates, and add-ons like the Stat Tracker and its in-game overlay."),
                    new TutorialStep("Tours", "These tours can be switched off here, or all shown again. The \"?\" button at the top shows the tour of any page.", Of<CheckBox>(p, c => c.Text.StartsWith("Show a short tour"))),
                };
                case "Frame Data": return new List<TutorialStep>
                {
                    new TutorialStep("Pick a fighter", "Every fighter's moves, read straight from P+'s own files.", Biggest(p, c => c.GetType().Name == "CastStrip")),
                    new TutorialStep("Moves", "Startup: the first frame it can hit. Active: the frames it hits on. FAF: the first frame you can act again. Filter with the buttons above, click a move to see it.", Biggest(p, c => c.GetType().Name == "MoveList")),
                    new TutorialStep("Frame by frame", "Red and colored circles are hitboxes, yellow is where you can be hit (blue while intangible). Drag along the strip to scrub, ◀ ▶ to step, and Save GIF to share.", Biggest(p, c => c.GetType().Name == "MoveView")),
                    new TutorialStep("Launch and combos", "Where a hit sends someone at any percent, and which of your moves true combo after it.", Button(p, "Launch")),
                    new TutorialStep("How to DI", "Drag your fighter onto the move as it plays: it shows the hitbox that gets you there, where it sends you, and which way to hold the stick to survive or get out of a combo, plus how to SDI.", Button(p, "How to DI")),
                };
                case "Stat Tracker": return new List<TutorialStep>
                {
                    new TutorialStep("Your Stat Tracker", "Everything here comes from Brawl Online's own records of your games and its replays (only read, never changed). Smash stats (openings, kill moves...) need the replays, which are read once in the background."),
                    new TutorialStep("Views", "Overview: your form, Smash stats and habits. Progress: your level, badges and personal bests. Characters: each fighter you play. Matches: your games, each with its report. Then matchups, stages, opponents (with what they tend to do) and your chance to win.", Biggest(p, c => c is FlowLayoutPanel && c.Controls.OfType<Button>().Any())),
                    new TutorialStep("Ranked or not", "Show all games, only ranked, or only unranked.", Of<ComboBox>(p)),
                    new TutorialStep("After every game", "With the overlay on (Settings), a report card pops up over the game a few seconds after it ends: your grade, the XP you earned, new badges and personal bests. The full report is under Matches."),
                };
            }
            return null;
        }
    }
}
