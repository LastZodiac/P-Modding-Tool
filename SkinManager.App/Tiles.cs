// Picture tiles: a choice shown as a card with a picture, a name and a line under it (stages, menus, stage versions).
// Click one to pick it; the picked one is ringed in gold.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SkinManager.App
{
    class PictureTile : Control
    {
        Image _picture; string _title = "", _sub = "", _badge; bool _selected, _hover, _dim;
        /// <summary>Drawn when there's no picture (a menu: a music note).</summary>
        public string Glyph = "♪";
        public Color GlyphColor = Theme.Accent;
        public object Value;

        public PictureTile(int width = 176, int height = 132)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Size = new Size(width, height); Margin = new Padding(0, 0, 10, 10); Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        public Image Picture { get => _picture; set { _picture = value; Invalidate(); } }
        public string Title { get => _title; set { _title = value ?? ""; Invalidate(); } }
        public string Subtitle { get => _sub; set { _sub = value ?? ""; Invalidate(); } }
        /// <summary>A small label in the picture's corner ("Now", "Hold L").</summary>
        public string Badge { get => _badge; set { _badge = value; Invalidate(); } }
        public bool Selected { get => _selected; set { if (_selected != value) { _selected = value; Invalidate(); } } }
        /// <summary>Shown faded: can't be picked here (it says why underneath).</summary>
        public bool Dim { get => _dim; set { _dim = value; Cursor = value ? Cursors.Default : Cursors.Hand; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var r = new Rectangle(2, 2, Width - 5, Height - 6);
                Theme.CardFrame(g, r, _hover && !_dim, _selected);
                var pic = new Rectangle(r.X + 1, r.Y + 1, r.Width - 1, (int)(r.Height * 0.62f));
                using (var area = Theme.PictureArea(g, pic))
                {
                    if (_picture != null)
                    {
                        var clip = g.Clip; g.SetClip(area, CombineMode.Intersect);
                        lock (_picture) Core.StageSelectCover(g, _picture, pic);
                        g.Clip = clip;
                    }
                    else
                        using (var b = new SolidBrush(Color.FromArgb(_dim ? 90 : 220, GlyphColor)))
                        {
                            var f = Theme.Bold((float)Math.Round(Math.Max(12f, pic.Height * 0.32f)));   // shared: not disposed here
                            var sz = g.MeasureString(Glyph, f);
                            g.DrawString(Glyph, f, b, pic.X + (pic.Width - sz.Width) / 2, pic.Y + (pic.Height - sz.Height) / 2);
                        }
                }
                if (!string.IsNullOrEmpty(_badge)) Theme.Pill(g, new Point(pic.X + 6, pic.Y + 6), _badge, _selected ? Theme.Accent : Color.FromArgb(200, 20, 22, 28), _selected ? Theme.AccentText : Theme.Text);
                var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                using (var t = new SolidBrush(_dim ? Theme.Muted : Theme.Text)) g.DrawString(_title, Theme.BodyBold, t, new RectangleF(r.X + 8, pic.Bottom + 4, r.Width - 14, 20), fmt);
                var subFmt = new StringFormat { Trimming = StringTrimming.EllipsisWord };
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString(_sub, Theme.Small, m, new RectangleF(r.X + 8, pic.Bottom + 22, r.Width - 14, r.Bottom - pic.Bottom - 24), subFmt);
                if (_dim) using (var veil = new SolidBrush(Color.FromArgb(90, 20, 22, 28))) using (var p = Theme.Rounded(r, 10)) g.FillPath(veil, p);
            }
            catch (Exception ex) { Program.Trace("tile paint: " + ex.Message); }
        }

        static class Core
        {
            public static void StageSelectCover(Graphics g, Image img, Rectangle box)
            {
                float k = Math.Max((float)box.Width / img.Width, (float)box.Height / img.Height);
                float w = img.Width * k, h = img.Height * k;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }
        }
    }

    /// <summary>A heading over a row of tiles ("Page 1", "Menus").</summary>
    class TileHeading : Label
    {
        public TileHeading(string text)
        {
            Text = text; Font = Theme.Heading; ForeColor = Theme.Text; AutoSize = false; Height = 30; BackColor = Color.Transparent;
            TextAlign = ContentAlignment.BottomLeft; Margin = new Padding(0, 6, 0, 4);
        }
    }
}

namespace SkinManager.App
{
    /// <summary>The stage select's preview pictures (sc_selmap.pac), read once: a stage's picture, or one of its versions'
    /// little pictures (Hold L, R, Z).</summary>
    static class StagePictures
    {
        static readonly System.Collections.Generic.Dictionary<string, Bitmap> Cache = new System.Collections.Generic.Dictionary<string, Bitmap>();

        /// <summary>The picture for a stage select picture number and part ("main", "L", "R", "Z"); null if it can't be read.
        /// Reads the card (call it off the window's thread).</summary>
        public static System.Collections.Generic.Dictionary<(int Picture, string Region), Bitmap> Read(SkinManager.Core.Fat32Image card, System.Collections.Generic.IEnumerable<(int Picture, string Region)> wanted)
        {
            var result = new System.Collections.Generic.Dictionary<(int, string), Bitmap>();
            var want = new System.Collections.Generic.List<(int Picture, string Region)>(wanted);
            var missing = new System.Collections.Generic.HashSet<int>();
            lock (Cache) foreach (var w in want) if (w.Picture >= 0 && !Cache.ContainsKey(Key(w))) missing.Add(w.Picture);
            if (missing.Count > 0)
                try
                {
                    var file = card.ReadFile(SkinManager.Core.StageSelect.PicturesFile);
                    var names = new System.Collections.Generic.List<string>(); foreach (var m in missing) names.Add(SkinManager.Core.StageSelect.PreviewTexture(m));
                    var pics = SkinManager.Core.Cosmetics.ReadPictures(file, names);
                    foreach (var m in missing)
                    {
                        pics.TryGetValue(SkinManager.Core.StageSelect.PreviewTexture(m), out var full);
                        foreach (var region in new[] { "main", "L", "R", "Z" })
                        {
                            Bitmap part = null;
                            if (full != null)
                                try
                                {
                                    bool whole = full.Width == SkinManager.Core.StageSelect.PreviewSize.Width && full.Height == SkinManager.Core.StageSelect.PreviewSize.Height;
                                    var src = whole ? SkinManager.Core.StageSelect.RegionRect(region) : new Rectangle(0, 0, full.Width, full.Height);
                                    if (!whole && region != "main") { lock (Cache) Cache[Key((m, region))] = null; continue; }
                                    part = new Bitmap(src.Width, src.Height);
                                    using (var g = Graphics.FromImage(part)) g.DrawImage(full, new Rectangle(0, 0, src.Width, src.Height), src, GraphicsUnit.Pixel);
                                }
                                catch { part = null; }
                            lock (Cache) Cache[Key((m, region))] = part;
                        }
                        full?.Dispose();
                    }
                }
                catch (Exception e) { Program.Trace("stage pictures: " + e.Message); }
            lock (Cache) foreach (var w in want) if (Cache.TryGetValue(Key(w), out var b) && b != null) result[(w.Picture, w.Region)] = b;
            return result;
        }

        static string Key((int Picture, string Region) w) => w.Picture + "|" + (w.Region ?? "main");
    }
}
