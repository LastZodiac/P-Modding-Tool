// The app's look: the background picture behind everything, see-through panels, sidebar buttons
// with icons, and the saved look settings.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace SkinManager.App
{
    [DataContract]
    class LookSettings
    {
        [DataMember] public string Background = "builtin";   // "builtin", "none" or a picture file in the Data folder
        [DataMember] public int Darkness = 55;               // 0..90: how much the picture is darkened
        [DataMember] public bool? StatsOff;                  // the Stat Tracker switched off in Settings
        public bool Stats { get => StatsOff != true; set => StatsOff = value ? (bool?)null : true; }
        [DataMember] public bool? CardOff;                   // the opponent card switched off
        public bool Card { get => CardOff != true; set => CardOff = value ? (bool?)null : true; }
        [DataMember] public bool? ToursOff;                  // the page tours switched off
        public bool Tours { get => ToursOff != true; set => ToursOff = value ? (bool?)null : true; }
        [DataMember] public System.Collections.Generic.List<string> ToursSeen = new System.Collections.Generic.List<string>();
        [DataMember] public string Hotkey;                    // the full overlay's hotkey (null: Alt+T)
        [DataMember] public bool? OverlayLeft;
        [DataMember] public bool? CardOpen;
        [DataMember] public string OverlayTheme;              // the look over the game: a theme id, "custom" (null: seasonal)
        [DataMember] public int? OverlayColor;                // "custom": its color
        [DataMember] public bool? OverlayEffectsOff;          // no snow, bats or stars
        public void ApplyOverlay() { OverlayLook.Choice = OverlayTheme ?? OverlayLook.Seasonal; OverlayLook.CustomAccent = OverlayColor != null ? (Color?)Color.FromArgb(OverlayColor.Value) : null; OverlayLook.Effects = OverlayEffectsOff != true; }                   // the opponent card opened up all the way (null: the small one)                // the opponent card in the top-left corner (null: top right)
        [DataMember] public int? Glow, Glow2;                 // the built-in background's two glows (ARGB; null: gold and blue)
        public static readonly Color GlowGold = Color.FromArgb(255, 176, 46), GlowBlue = Color.FromArgb(90, 110, 255);
        [DataMember] public string CardFile;                  // your own Dolphin's sd.raw (or its folder) to mod instead of Brawl Online's (null: Brawl Online's)
        [DataMember] public System.Collections.Generic.List<string> ReplayFolders;   // replay folders picked in Settings (read as well as Brawl Online's)
        public void ApplyReplayFolders() { lock (SkinManager.Core.MatchHistory.ChosenReplayFolders) { SkinManager.Core.MatchHistory.ChosenReplayFolders.Clear(); if (ReplayFolders != null) SkinManager.Core.MatchHistory.ChosenReplayFolders.AddRange(ReplayFolders); } }
        public string HotkeyText { get => string.IsNullOrEmpty(Hotkey) ? "Alt+T" : Hotkey; set => Hotkey = value == "Alt+T" ? null : value; }

        static string FilePath => Path.Combine(Program.DataFolder, "look.json");

        public static LookSettings Load()
        {
            LookSettings look = null;
            try { if (File.Exists(FilePath)) using (var s = File.OpenRead(FilePath)) look = (LookSettings)new DataContractJsonSerializer(typeof(LookSettings)).ReadObject(s); }
            catch { }
            look = look ?? new LookSettings();
            look.ApplyReplayFolders();
            return look;
        }

        public void Save()
        {
            try { Directory.CreateDirectory(Program.DataFolder); using (var s = File.Create(FilePath)) new DataContractJsonSerializer(typeof(LookSettings)).WriteObject(s, this); } catch { }
        }
    }

    /// <summary>Draws the window's backdrop: the chosen picture (cover-fitted, darkened) or the built-in one.</summary>
    class Backdrop : IDisposable
    {
        Image _picture;
        Bitmap _cache;
        Size _cacheSize;
        LookSettings _look;

        public void Apply(LookSettings look)
        {
            _look = look;
            _picture?.Dispose(); _picture = null;
            if (look.Background != "builtin" && look.Background != "none")
                try { using (var tmp = Image.FromFile(look.Background)) _picture = new Bitmap(tmp); } catch { _picture = null; }
            _cache?.Dispose(); _cache = null;
            BackCache.Generation++;
        }

        public void Paint(Graphics g, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0) return;
            if (_cache == null || _cacheSize != size)
            {
                _cache?.Dispose();
                _cache = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);   // the fastest kind to draw (redrawn behind every see-through panel)
                _cacheSize = size;
                using (var cg = Graphics.FromImage(_cache)) Render(cg, size);
                BackCache.Generation++;   // the see-through panels' copies of it are out of date
            }
            // only the part being painted (a panel or card asks for just what's behind it)
            var clip = Rectangle.Round(g.VisibleClipBounds);
            clip.Intersect(new Rectangle(0, 0, _cache.Width, _cache.Height));
            if (clip.Width > 0 && clip.Height > 0) g.DrawImage(_cache, clip, clip, GraphicsUnit.Pixel);
        }

        void Render(Graphics g, Size size)
        {
            var all = new Rectangle(Point.Empty, size);
            if (_look == null || _look.Background == "none") { using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, all); return; }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_picture != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Max((float)size.Width / _picture.Width, (float)size.Height / _picture.Height);
                int w = (int)(_picture.Width * scale), h = (int)(_picture.Height * scale);
                g.DrawImage(_picture, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
                int alpha = Math.Max(0, Math.Min(230, _look.Darkness * 255 / 100));
                using (var veil = new SolidBrush(Color.FromArgb(alpha, 12, 13, 18))) g.FillRectangle(veil, all);
            }
            else
            {
                // built-in: deep blue-black with a warm gold glow, like a stage at night
                using (var b = new LinearGradientBrush(all, Color.FromArgb(14, 16, 26), Color.FromArgb(26, 18, 34), 35f)) g.FillRectangle(b, all);
                using (var path = new GraphicsPath())
                {
                    var glow = new Rectangle(size.Width - size.Width * 2 / 3, -size.Height / 3, size.Width, size.Height);
                    path.AddEllipse(glow);
                    var c1 = _look.Glow != null ? Color.FromArgb(_look.Glow.Value) : LookSettings.GlowGold;
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, c1), SurroundColors = new[] { Color.FromArgb(0, c1) } })
                        g.FillRectangle(pb, all);
                }
                using (var path = new GraphicsPath())
                {
                    var glow = new Rectangle(-size.Width / 3, size.Height / 2, size.Width * 2 / 3, size.Height);
                    path.AddEllipse(glow);
                    var c2 = _look.Glow2 != null ? Color.FromArgb(_look.Glow2.Value) : LookSettings.GlowBlue;
                    using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(55, c2), SurroundColors = new[] { Color.FromArgb(0, c2) } })
                        g.FillRectangle(pb, all);
                }
            }
            // a soft shadow down the left, where the menu is
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 420, size.Height), Color.FromArgb(150, 8, 9, 12), Color.FromArgb(0, 8, 9, 12), 0f))
                g.FillRectangle(b, 0, 0, 420, size.Height);
        }

        public void Dispose() { _picture?.Dispose(); _cache?.Dispose(); }
    }

    /// <summary>A panel that lets the window's backdrop show through (tinted).</summary>
    /// <summary>What's behind a see-through panel, kept as a picture. Without it every card, button and label on the
    /// panel would have the window draw everything behind it again (the backdrop, each panel's tint, all the way up),
    /// on every frame of a scroll: that is what made scrolling choppy. Made again when the panel moves within the
    /// window, changes size, or the backdrop changes.</summary>
    static class BackCache
    {
        public static int Generation;   // goes up whenever the window's backdrop is drawn anew

        public static Point OffsetInForm(Control c)
        {
            int x = 0, y = 0;
            for (var p = c; p != null && !(p is Form); p = p.Parent) { x += p.Left; y += p.Top; }
            return new Point(x, y);
        }
    }

    /// <summary>A panel that keeps a picture of what's behind it (see BackPicture).</summary>
    interface IGlass { BackPicture Back { get; } }

    sealed class BackPicture : IDisposable
    {
        Bitmap _bmp; Point _at; Size _size; int _gen = -1;
        Action<PaintEventArgs> _build;

        bool Ensure(Control owner)
        {
            var size = owner.ClientSize;
            if (size.Width <= 0 || size.Height <= 0 || _build == null) return false;
            var at = BackCache.OffsetInForm(owner);
            if (_bmp == null || at != _at || size != _size || _gen != BackCache.Generation)
            {
                if (_bmp == null || _bmp.Size != size) { _bmp?.Dispose(); _bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); }
                using (var g = Graphics.FromImage(_bmp)) { g.Clear(Color.Transparent); _build(new PaintEventArgs(g, new Rectangle(Point.Empty, size))); }
                _at = at; _size = size; _gen = BackCache.Generation;
            }
            return true;
        }

        public void Paint(Control owner, PaintEventArgs e, Action<PaintEventArgs> build)
        {
            _build = build;
            if (!Ensure(owner)) return;
            var clip = e.ClipRectangle;
            clip.Intersect(new Rectangle(Point.Empty, _size));
            if (clip.Width > 0 && clip.Height > 0) e.Graphics.DrawImage(_bmp, clip, clip, GraphicsUnit.Pixel);
        }

        /// <summary>Draws the part at (x, y) of the panel, w by h, at 0,0 of g. False if it isn't ready.</summary>
        public bool DrawAt(Control owner, Graphics g, int x, int y, int w, int h)
        {
            try
            {
                if (!Ensure(owner)) return false;
                g.DrawImage(_bmp, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Draws what's behind a control (in its own coordinates), found directly: the nearest see-through
        /// panel's picture, or the nearest solid color. Used by rounded buttons and boxes, whose corners must show
        /// exactly what's around them (going through Windows' own see-through painting left them black).</summary>
        public static void Behind(Control c, Graphics g)
        {
            int x = 0, y = 0, w = c.Width, h = c.Height;
            for (Control p = c; p.Parent != null;)
            {
                x += p.Left; y += p.Top; p = p.Parent;
                if (p is IGlass glass && glass.Back.DrawAt(p, g, x, y, w, h)) return;
                if (p is Form || p.BackColor.A == 255)
                {
                    using (var b = new SolidBrush(p.BackColor.A == 255 ? p.BackColor : Theme.Back)) g.FillRectangle(b, 0, 0, w, h);
                    return;
                }
            }
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, 0, 0, w, h);
        }

        public void Drop() { _bmp?.Dispose(); _bmp = null; }
        public void Dispose() => Drop();
    }

    class GlassPanel : Panel, IGlass
    {
        public int Tint = 0;   // 0..255 darkness drawn over the backdrop
        public Color TintColor = Color.FromArgb(10, 11, 16);
        readonly BackPicture _back = new BackPicture();
        public BackPicture Back => _back;
        public GlassPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e) => _back.Paint(this, e, pe =>
        {
            base.OnPaintBackground(pe);   // the parent's backdrop
            if (Tint > 0) using (var b = new SolidBrush(Color.FromArgb(Tint, TintColor))) pe.Graphics.FillRectangle(b, pe.ClipRectangle);
        });
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) _back.Drop(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) _back.Dispose(); base.Dispose(disposing); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
    }

    /// <summary>A see-through grid of cards that redraws cleanly when scrolled.</summary>
    class GlassFlow : FlowLayoutPanel, IGlass
    {
        readonly BackPicture _back = new BackPicture();
        public BackPicture Back => _back;
        public GlassFlow()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaintBackground(PaintEventArgs e) => _back.Paint(this, e, pe => base.OnPaintBackground(pe));
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) _back.Drop(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) _back.Dispose(); base.Dispose(disposing); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
    }

    /// <summary>A sidebar button: icon, text, a gold bar when it's the current page.</summary>
    class NavButton : Control
    {
        public string Icon;     // a glyph from the Windows icon font
        public bool Selected;
        public bool Live;       // a red "recording" dot that pulses (Live Match, while a game is on)
        bool _hover;
        static readonly Font IconFont = MakeIconFont();

        static Font MakeIconFont()
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var f = new Font(name, 12f);
                if (f.Name == name) return f;
                f.Dispose();
            }
            return null;
        }

        public NavButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; Size = new Size(184, 40);
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Selected || _hover)
                using (var path = Theme.Rounded(r, 8))
                using (var b = new SolidBrush(Selected ? Color.FromArgb(60, Theme.Accent) : Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, path);
            if (Selected) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 9, 3, Height - 18);
            var color = Selected ? Theme.Accent : Theme.Text;
            int x = 16;
            if (IconFont != null && !string.IsNullOrEmpty(Icon))
            {
                TextRenderer.DrawText(g, Icon, IconFont, new Rectangle(x, 0, 24, Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                x += 34;
            }
            TextRenderer.DrawText(g, Text, Selected ? Theme.BodyBold : Theme.Body, new Rectangle(x, 0, Width - x, Height), Selected ? Theme.Text : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (Live)
            {
                double pulse = 0.5 + 0.5 * Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 4);
                using (var b = new SolidBrush(Color.FromArgb((int)(130 + 125 * pulse), 235, 64, 52))) g.FillEllipse(b, Width - 24, Height / 2 - 6, 12, 12);
            }
        }
    }
}
