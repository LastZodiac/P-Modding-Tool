// Colors, fonts and small drawing helpers shared by the windows.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SkinManager.App
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 26, 33);
        public static readonly Color Panel = Color.FromArgb(33, 36, 45);
        public static readonly Color PanelHover = Color.FromArgb(44, 48, 60);
        public static readonly Color Line = Color.FromArgb(58, 62, 76);
        public static readonly Color Text = Color.FromArgb(236, 238, 244);
        public static readonly Color Muted = Color.FromArgb(150, 156, 172);
        public static readonly Color Accent = Color.FromArgb(255, 176, 46);      // P+ gold
        public static readonly Color AccentText = Color.FromArgb(28, 22, 10);
        public static readonly Color Good = Color.FromArgb(92, 201, 120);
        public static readonly Color Bad = Color.FromArgb(240, 98, 98);
        public static readonly Color Warn = Color.FromArgb(240, 196, 80);

        public static readonly Font Title = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75f);

        static readonly System.Collections.Generic.Dictionary<float, Font> _bold = new System.Collections.Generic.Dictionary<float, Font>();
        /// <summary>A bold title-face font of this size, made once and reused (pictures redrawn often don't make new ones each time).</summary>
        public static Font Bold(float size) { lock (_bold) { if (!_bold.TryGetValue(size, out var f)) _bold[size] = f = new Font(Title.FontFamily, size, FontStyle.Bold); return f; } }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, System.Collections.Generic.Dictionary<long, Bitmap>> _scaled =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Image, System.Collections.Generic.Dictionary<long, Bitmap>>();
        /// <summary>A picture shrunk (smoothly) to a size, made once and kept: drawing it again is just a copy,
        /// so lists of pictures scroll and redraw smoothly.</summary>
        public static Bitmap Scaled(Image img, int w, int h, bool mirror = false)
        {
            if (img == null || w <= 0 || h <= 0) return null;
            var sizes = _scaled.GetOrCreateValue(img);
            long key = ((long)w << 32) | ((long)h << 1) | (mirror ? 1L : 0L);
            lock (sizes)
            {
                if (sizes.TryGetValue(key, out var b)) return b;
                if (sizes.Count > 8) { foreach (var old in sizes.Values) old.Dispose(); sizes.Clear(); }   // window resized a lot: start over
                b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    if (mirror) { g.TranslateTransform(w, 0); g.ScaleTransform(-1, 1); }
                    lock (img) g.DrawImage(img, 0, 0, w, h);
                }
                sizes[key] = b;
                return b;
            }
        }

        /// <summary>Copies a picture 1:1 (no resizing, whatever the screen's scaling).</summary>
        public static void Blit(Graphics g, Image b, int x, int y) { if (b != null) g.DrawImage(b, new Rectangle(x, y, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel); }

        static Icon _icon; static Image _logo;
        /// <summary>The app's icon and logo (built into the program).</summary>
        public static Icon AppIcon { get { if (_icon == null) try { using (var st = typeof(Theme).Assembly.GetManifestResourceStream("app.ico")) _icon = new Icon(st); } catch { } return _icon; } }
        public static Image Logo { get { if (_logo == null) try { using (var st = typeof(Theme).Assembly.GetManifestResourceStream("logo96.png")) _logo = new Bitmap(Image.FromStream(st)); } catch { } return _logo; } }

        public static Button Primary(string text)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = AccentText,
                Font = BodyBold, AutoSize = true, Padding = new Padding(14, 6, 14, 6), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 196, 92);
            return b;
        }

        public static Button Secondary(string text)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Text,
                Font = Body, AutoSize = true, Padding = new Padding(10, 4, 10, 4), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.MouseOverBackColor = PanelHover;
            return b;
        }

        /// <summary>Shows a button as the chosen one of a pair (gold) or not.</summary>
        public static void Toggle(Button b, bool on)
        {
            b.UseVisualStyleBackColor = false;
            b.BackColor = on ? Accent : Panel; b.ForeColor = on ? AccentText : Text;
            b.Font = on ? BodyBold : Body;
            b.FlatAppearance.BorderColor = on ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = on ? Accent : PanelHover;
        }

        public static Label Label(string text, Font font = null, Color? color = null) => new Label
        {
            Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = true, BackColor = Color.Transparent,
        };

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>A card's frame: rounded, a soft shadow, a gentle top-to-bottom shade, and a border that
        /// lights up on hover (gold when selected).</summary>
        public static void CardFrame(Graphics g, Rectangle r, bool hover, bool selected)
        {
            for (int i = 3; i >= 1; i--)
            {
                var shadow = new Rectangle(r.X + 1, r.Y + i + 1, r.Width - 2, r.Height - 2);
                using (var p = Rounded(shadow, 11)) using (var b = new SolidBrush(Color.FromArgb(hover ? 26 : 16, 0, 0, 0))) g.FillPath(b, p);
            }
            using (var path = Rounded(r, 10))
            {
                var top = hover ? Color.FromArgb(240, 50, 54, 68) : Color.FromArgb(236, 38, 41, 52);
                var bottom = hover ? Color.FromArgb(240, 40, 43, 55) : Color.FromArgb(236, 29, 32, 41);
                using (var b = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical)) g.FillPath(b, path);
                var edge = selected ? Accent : hover ? Color.FromArgb(150, Accent) : Color.FromArgb(70, 255, 255, 255);
                using (var pen = new Pen(edge, selected ? 2f : 1f)) g.DrawPath(pen, path);
            }
        }

        /// <summary>The picture area of a card: rounded top corners and a soft spotlight behind the picture.</summary>
        public static GraphicsPath PictureArea(Graphics g, Rectangle pic)
        {
            var p = new GraphicsPath();
            int d = 16;
            p.AddArc(pic.X, pic.Y, d, d, 180, 90);
            p.AddArc(pic.Right - d, pic.Y, d, d, 270, 90);
            p.AddLine(pic.Right, pic.Bottom, pic.X, pic.Bottom);
            p.CloseFigure();
            using (var b = new LinearGradientBrush(pic, Color.FromArgb(30, 33, 42), Color.FromArgb(18, 20, 26), LinearGradientMode.Vertical)) g.FillPath(b, p);
            using (var glow = new GraphicsPath())
            {
                var spot = new Rectangle(pic.X + pic.Width / 6, pic.Y + pic.Height / 8, pic.Width * 2 / 3, pic.Height * 3 / 4);
                glow.AddEllipse(spot);
                using (var b = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(46, 255, 214, 140), SurroundColors = new[] { Color.FromArgb(0, 255, 214, 140) } })
                    g.FillPath(b, glow);
            }
            return p;
        }

        /// <summary>A small rounded label ("Installed", "Not saved yet").</summary>
        public static Rectangle Pill(Graphics g, Point at, string text, Color back, Color fore)
        {
            var size = TextRenderer.MeasureText(text, Small);
            var r = new Rectangle(at.X, at.Y, size.Width + 12, 20);
            using (var p = Rounded(r, 9)) using (var b = new SolidBrush(back)) g.FillPath(b, p);
            TextRenderer.DrawText(g, text, Small, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return r;
        }

        /// <summary>A five-point star (filled gold when on), in a dark round button so it reads on any picture.</summary>
        public static void Star(Graphics g, Rectangle r, bool on, bool hover)
        {
            using (var b = new SolidBrush(Color.FromArgb(hover ? 200 : 150, 14, 15, 20))) g.FillEllipse(b, r);
            var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f + 0.5f);
            float outer = r.Width * 0.34f, inner = outer * 0.45f;
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -System.Math.PI / 2 + i * System.Math.PI / 5;
                float rad = i % 2 == 0 ? outer : inner;
                pts[i] = new PointF(c.X + (float)(System.Math.Cos(a) * rad), c.Y + (float)(System.Math.Sin(a) * rad));
            }
            if (on) using (var b = new SolidBrush(Accent)) g.FillPolygon(b, pts);
            else using (var pen = new Pen(hover ? Accent : Color.FromArgb(210, 230, 232, 240), 1.4f)) g.DrawPolygon(pen, pts);
        }

        /// <summary>Grey hint text in an empty search box (Windows draws it).</summary>
        public static void Cue(TextBox box, string text)
        {
            box.HandleCreated += (s, e) => { try { SendMessage(box.Handle, 0x1501, (System.IntPtr)1, text); } catch { } };
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern System.IntPtr SendMessage(System.IntPtr h, int msg, System.IntPtr w, string l);

        public static void Checkerboard(Graphics g, Rectangle r)
        {
            using (var a = new SolidBrush(Color.FromArgb(40, 43, 54))) using (var b = new SolidBrush(Color.FromArgb(48, 52, 64)))
                for (int y = r.Top; y < r.Bottom; y += 8)
                    for (int x = r.Left; x < r.Right; x += 8)
                        g.FillRectangle(((x - r.Left) / 8 + (y - r.Top) / 8) % 2 == 0 ? a : b, x, y, System.Math.Min(8, r.Right - x), System.Math.Min(8, r.Bottom - y));
        }
    }
}
