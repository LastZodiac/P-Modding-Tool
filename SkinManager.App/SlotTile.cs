// One costume slot drawn as a card: the select-screen picture with a caption underneath.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SkinManager.App
{
    class SlotTile : Control
    {
        public Image Picture;
        public string Caption = "";
        public string Badge;              // small text under the caption ("Your skin: ...", "Fits", ...)
        public Color BadgeColor = Theme.Muted;
        public bool Selected;
        public bool Dimmed;               // can't be chosen
        bool _hover;

        public SlotTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(140, 230);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(card, 8))
            {
                using (var b = new SolidBrush(Color.FromArgb(232, _hover && !Dimmed ? Theme.PanelHover : Theme.Panel))) g.FillPath(b, path);
                using (var pen = new Pen(Selected ? Theme.Accent : Theme.Line, Selected ? 2.5f : 1f)) g.DrawPath(pen, path);
            }
            var pic = new Rectangle(6, 6, Width - 13, (Width - 13) * 160 / 128);
            Theme.Checkerboard(g, pic);
            if (Picture != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Picture, pic);
            }
            if (Dimmed)
                using (var veil = new SolidBrush(Color.FromArgb(160, Theme.Back))) g.FillRectangle(veil, pic);
            int y = pic.Bottom + 4;
            TextRenderer.DrawText(g, Caption, Theme.BodyBold, new Rectangle(4, y, Width - 8, 18), Dimmed ? Theme.Muted : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Badge))
                TextRenderer.DrawText(g, Badge, Theme.Small, new Rectangle(4, y + 18, Width - 8, 34), BadgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
