// The app's finish: smooth beveled buttons, dark dropdowns and scrollbars, dark title bars and the app's own message boxes.
// Polish.Install() (once, at start) styles every window and control the app opens, so screens don't each need to.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SkinManager.App
{
    /// <summary>A rounded button with a soft bevel: lighter at the top, a thin highlight along its top edge,
    /// a quiet shadow under it. Hover lifts it, pressing sinks it. Colors come from BackColor / ForeColor
    /// (and FlatAppearance.MouseOverBackColor for hover), so code that recolors buttons keeps working.</summary>
    class SmoothButton : Button
    {
        bool _hover, _down;
        public int Radius = 8;
        Color _fill = Theme.Panel;

        /// <summary>Draws itself (no Windows look) on a see-through background, flat with no border, with a hand
        /// cursor.</summary>
        public SmoothButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            base.BackColor = Color.Transparent;
        }
        bool _settingColor;
        protected override void OnBackColorChanged(EventArgs e)
        {
            if (!_settingColor && base.BackColor != Color.Transparent && GetStyle(ControlStyles.SupportsTransparentBackColor))
            {
                _fill = base.BackColor;
                _settingColor = true; try { base.BackColor = Color.Transparent; } finally { _settingColor = false; }
                Invalidate();
                return;
            }
            base.OnBackColorChanged(e);
        }

        /// <summary>The button's window is cut to its rounded shape, so whatever is around it is drawn by what's around
        /// it (never a dark box behind the corners); inside, what's behind it is drawn first so the soft edge blends.</summary>
        protected override void OnPaintBackground(PaintEventArgs e) => BackPicture.Behind(this, e.Graphics);
        Rectangle Shape => new Rectangle(0, 0, Width - 1, Height - 1);
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width < 4 || Height < 4) return;
            using (var p = Theme.Rounded(new Rectangle(0, 0, Width, Height), Radius)) { var old = Region; Region = new Region(p); old?.Dispose(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(Math.Max(t.Width + Padding.Horizontal + 18, 36), Math.Max(t.Height + Padding.Vertical + 12, 34));
        }

        static Color Mix(Color a, Color b, float t) =>
            Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var baseColor = _fill.A == 0 ? Theme.Panel : Color.FromArgb(255, _fill);
            var over = FlatAppearance.MouseOverBackColor;
            if (_hover && Enabled) baseColor = over.IsEmpty || over.A == 0 ? Mix(baseColor, Color.White, 0.08f) : Color.FromArgb(255, over);
            if (_down && Enabled) baseColor = Mix(baseColor, Color.Black, 0.12f);
            bool bright = baseColor.GetBrightness() > 0.45f;

            var r = Shape;
            if (r.Width < 4 || r.Height < 4) return;
            using (var path = Theme.Rounded(r, Radius))
            {
                var top = Mix(baseColor, Color.White, bright ? 0.16f : 0.07f);
                var bottom = Mix(baseColor, Color.Black, bright ? 0.10f : 0.12f);
                if (!Enabled) { top = Mix(top, Theme.Back, 0.5f); bottom = Mix(bottom, Theme.Back, 0.5f); }
                using (var fill = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                    g.FillPath(fill, path);
                // the bevel: a thin light line just inside the top edge
                var hl = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                using (var hp = Theme.Rounded(hl, Math.Max(1, Radius - 1)))
                using (var pen = new Pen(new LinearGradientBrush(new Rectangle(hl.X, hl.Y - 1, hl.Width, hl.Height + 2),
                    Color.FromArgb(_down ? 20 : bright ? 110 : 34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical), 1f))
                    g.DrawPath(pen, hp);
                // the outline: a faint light edge (a dark one reads as a black frame on the dark window)
                var edge = bright ? Mix(baseColor, Color.White, 0.25f) : Color.FromArgb(_hover && Enabled ? 70 : 38, 255, 255, 255);
                using (var pen = new Pen(Enabled ? edge : Color.FromArgb(20, 255, 255, 255), 1f)) g.DrawPath(pen, path);
            }
            var fore = Enabled ? ForeColor : Color.FromArgb(140, ForeColor);
            TextRenderer.DrawText(g, Text, Font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                using (var p = Theme.Rounded(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6), Math.Max(1, Radius - 3)))
                using (var pen = new Pen(Color.FromArgb(90, fore), 1f) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, p);
        }
    }

    /// <summary>The mouse wheel glides a list instead of jumping: each notch sets where it should end up, and it eases
    /// there over a few frames (a new notch while it's moving just moves the goal). Long lists stay readable while they
    /// move. Dragging the scrollbar stops the glide.</summary>
    sealed class SmoothScroll : NativeWindow
    {
        readonly ScrollableControl _c;
        readonly Timer _timer = new Timer { Interval = 10 };
        float _pos; int _goal;

        /// <summary>Listens in on the list's window messages, and follows it if its window is made again.</summary>
        SmoothScroll(ScrollableControl c)
        {
            _c = c;
            if (c.IsHandleCreated) AssignHandle(c.Handle);
            c.HandleCreated += (s, e) => { ReleaseHandle(); AssignHandle(c.Handle); };
            c.HandleDestroyed += (s, e) => { _timer.Stop(); ReleaseHandle(); };
            c.Disposed += (s, e) => _timer.Dispose();
            _timer.Tick += (s, e) => Step();
        }

        /// <summary>Turns on the gliding wheel for a list (Polish.Style does this for every scrolling panel).</summary>
        public static void Attach(ScrollableControl c)
        {
            if (c.IsDisposed) return;
            new SmoothScroll(c);
        }

        int Max => Math.Max(0, _c.DisplayRectangle.Height - _c.ClientSize.Height);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && _c.AutoScroll && _c.VerticalScroll.Visible && Max > 0)   // WM_MOUSEWHEEL
            {
                int delta = (short)((long)m.WParam >> 16 & 0xFFFF);
                if (!_timer.Enabled) { _pos = -_c.AutoScrollPosition.Y; _goal = (int)_pos; }
                int step = Math.Max(60, Math.Min(180, _c.ClientSize.Height / 4));
                _goal = Math.Max(0, Math.Min(Max, _goal - delta * step / 120));
                _timer.Start();
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0115)   // WM_VSCROLL: the scrollbar is being used: no glide, and drawn whole each step
            {
                _timer.Stop();
                var msg = m;
                Theme.Repaint(_c, () => base.WndProc(ref msg));
                m.Result = msg.Result;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>One frame of the glide: moves a third of the way to the goal, and stops once it's there.</summary>
        void Step()
        {
            if (_c.IsDisposed || !_c.IsHandleCreated) { _timer.Stop(); return; }
            _goal = Math.Max(0, Math.Min(Max, _goal));
            _pos += (_goal - _pos) * 0.3f;
            if (Math.Abs(_goal - _pos) < 0.75f) { _pos = _goal; _timer.Stop(); }
            int y = (int)Math.Round(_pos);
            if (y == -_c.AutoScrollPosition.Y) return;
            // drawn whole each step (see Theme.Repaint): no smearing however fast it goes
            Theme.Repaint(_c, () => _c.AutoScrollPosition = new Point(-_c.AutoScrollPosition.X, y));
        }
    }

    /// <summary>Paints a dropdown's closed box in the app's colors (Windows draws a light arrow button otherwise),
    /// and draws its list items dark.</summary>
    class ComboPainter : NativeWindow
    {
        readonly ComboBox _box;
        bool _hover;
        /// <summary>Listens in on the dropdown's window messages to paint over it, and keeps it cut to its rounded
        /// shape.</summary>
        ComboPainter(ComboBox box)
        {
            _box = box;
            if (box.IsHandleCreated) AssignHandle(box.Handle);
            box.HandleCreated += (s, e) => { ReleaseHandle(); AssignHandle(box.Handle); };
            box.HandleDestroyed += (s, e) => ReleaseHandle();
            box.MouseEnter += (s, e) => { _hover = true; box.Invalidate(); };
            box.MouseLeave += (s, e) => { _hover = false; box.Invalidate(); };
            box.SelectedIndexChanged += (s, e) => box.Invalidate();
            box.EnabledChanged += (s, e) => box.Invalidate();
            // cut to its rounded shape, so the corners show what's around it
            void Shape() { if (box.Width < 8 || box.Height < 8 || box.DropDownStyle != ComboBoxStyle.DropDownList) return; using (var p = Theme.Rounded(new Rectangle(0, 0, box.Width, box.Height), 6)) { var old = box.Region; box.Region = new Region(p); old?.Dispose(); } }
            box.SizeChanged += (s, e) => Shape(); Shape();
        }

        /// <summary>Gives a dropdown the app's colors and dark list items (once; a dropdown that draws its own items
        /// keeps them).</summary>
        public static void Attach(ComboBox box)
        {
            if (box.Tag is ComboPainter) return;
            box.FlatStyle = FlatStyle.Flat;
            if (box.BackColor == SystemColors.Window) box.BackColor = Theme.Panel;
            if (box.ForeColor == SystemColors.WindowText) box.ForeColor = Theme.Text;
            if (box.DrawMode == DrawMode.Normal)
            {
                box.DrawMode = DrawMode.OwnerDrawFixed;
                box.ItemHeight = Math.Max(box.ItemHeight, box.Font.Height + 6);
                box.DrawItem += DrawItem;
            }
            new ComboPainter(box);
        }

        /// <summary>Draws one item in a dropdown's open list: dark, with an accent bar beside the one under the
        /// mouse.</summary>
        static void DrawItem(object sender, DrawItemEventArgs e)
        {
            var box = (ComboBox)sender;
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Theme.PanelHover : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 3, 3, e.Bounds.Height - 6);
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, r, box.Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F || _box.DropDownStyle != ComboBoxStyle.DropDownList) return;   // WM_PAINT; an editable box keeps Windows' text field
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var full = new Rectangle(0, 0, _box.Width, _box.Height);
                    BackPicture.Behind(_box, g);
                    var r = new Rectangle(0, 0, _box.Width - 1, _box.Height - 1);
                    using (var path = Theme.Rounded(r, 6))
                    {
                        var fill = _box.Enabled ? (_hover || _box.DroppedDown ? Theme.PanelHover : Theme.Panel) : Theme.Back;
                        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                        using (var pen = new Pen(_box.DroppedDown ? Theme.Accent : _hover ? Color.FromArgb(150, Theme.Accent) : Theme.Line)) g.DrawPath(pen, path);
                    }
                    var text = new Rectangle(9, 0, _box.Width - 34, _box.Height);
                    TextRenderer.DrawText(g, _box.Text, _box.Font, text, _box.Enabled ? Theme.Text : Theme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    // the chevron
                    int cx = _box.Width - 16, cy = _box.Height / 2;
                    using (var pen = new Pen(_box.Enabled ? Theme.Muted : Theme.Line, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLines(pen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
                }
            }
            catch { }
        }
    }

    /// <summary>The app's own message box: dark, rounded buttons, same calls as MessageBox.Show.</summary>
    static class Dialog
    {
        public static DialogResult Show(string text, string caption) => Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(null, text, caption, buttons, icon);
        public static DialogResult Show(IWin32Window owner, string text, string caption) => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton def) => Show(owner, text, caption, buttons, icon, def == MessageBoxDefaultButton.Button2 ? 1 : def == MessageBoxDefaultButton.Button3 ? 2 : 0);

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(owner, text, caption, buttons, icon, 0);

        /// <summary>Shows the app's message box, falling back to Windows' own if anything goes wrong drawing
        /// it.</summary>
        static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, int defaultIndex)
        {
            try { return ShowOurs(owner, text, caption, buttons, icon, defaultIndex); }
            catch { return MessageBox.Show(owner, text, caption, buttons, icon); }   // never lose a message because of the look
        }

        /// <summary>Builds and shows the message box: the icon as a colored badge, the text (scrolling if very long)
        /// and the buttons in a footer, the default one highlighted.</summary>
        static DialogResult ShowOurs(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, int defaultIndex)
        {
            var choices = new List<(string, DialogResult)>();
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel: choices.Add(("OK", DialogResult.OK)); choices.Add(("Cancel", DialogResult.Cancel)); break;
                case MessageBoxButtons.YesNo: choices.Add(("Yes", DialogResult.Yes)); choices.Add(("No", DialogResult.No)); break;
                case MessageBoxButtons.YesNoCancel: choices.Add(("Yes", DialogResult.Yes)); choices.Add(("No", DialogResult.No)); choices.Add(("Cancel", DialogResult.Cancel)); break;
                case MessageBoxButtons.RetryCancel: choices.Add(("Try again", DialogResult.Retry)); choices.Add(("Cancel", DialogResult.Cancel)); break;
                case MessageBoxButtons.AbortRetryIgnore: choices.Add(("Stop", DialogResult.Abort)); choices.Add(("Try again", DialogResult.Retry)); choices.Add(("Skip", DialogResult.Ignore)); break;
                default: choices.Add(("OK", DialogResult.OK)); break;
            }
            Color mark; string glyph;
            switch (icon)
            {
                case MessageBoxIcon.Warning: mark = Theme.Warn; glyph = "!"; break;
                case MessageBoxIcon.Error: mark = Theme.Bad; glyph = "×"; break;
                case MessageBoxIcon.Question: mark = Theme.Accent; glyph = "?"; break;
                case MessageBoxIcon.Information: mark = Theme.Accent; glyph = "i"; break;
                default: mark = Color.Empty; glyph = null; break;
            }

            using (var f = new Form
            {
                Text = string.IsNullOrEmpty(caption) ? "P+ Modding Tool" : caption, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = owner == null, StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Body, AutoScaleMode = AutoScaleMode.Dpi,
            })
            {
                int left = glyph != null ? 70 : 24, width = 460;
                var size = TextRenderer.MeasureText(text ?? "", Theme.Body, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int textH = Math.Min(Math.Max(size.Height, 40), 520);
                var body = new Label { Text = text ?? "", AutoSize = false, Location = new Point(left, 24), Size = new Size(width, textH), Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false };
                if (size.Height > 520) { body.AutoSize = false; var scroll = new Panel { Location = body.Location, Size = new Size(width + 20, 520), AutoScroll = true, BackColor = Theme.Back }; body.Location = Point.Empty; body.Height = size.Height + 10; scroll.Controls.Add(body); f.Controls.Add(scroll); }
                else f.Controls.Add(body);
                if (glyph != null)
                {
                    var badge = new Panel { Location = new Point(24, 22), Size = new Size(32, 32), BackColor = Color.Transparent };
                    badge.Paint += (s, e) =>
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var b = new SolidBrush(Color.FromArgb(46, mark))) e.Graphics.FillEllipse(b, 0, 0, 31, 31);
                        using (var pen = new Pen(mark, 1.5f)) e.Graphics.DrawEllipse(pen, 1, 1, 29, 29);
                        TextRenderer.DrawText(e.Graphics, glyph, Theme.Bold(12f), new Rectangle(0, 0, 32, 32), mark, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    };
                    f.Controls.Add(badge);
                }
                int footerY = 24 + textH + 22;
                var footer = new Panel { Location = new Point(0, footerY), Height = 64, BackColor = Theme.Panel };
                f.Controls.Add(footer);
                f.ClientSize = new Size(left + width + 28, footerY + 64);
                footer.Width = f.ClientSize.Width;
                int x = f.ClientSize.Width - 20;
                Button chosen = null;
                for (int i = choices.Count - 1; i >= 0; i--)
                {
                    var (label, result) = choices[i];
                    bool main = i == Math.Min(defaultIndex, choices.Count - 1);
                    var b = main ? Theme.Primary(label) : Theme.Secondary(label);
                    b.AutoSize = false; b.Size = new Size(Math.Max(96, TextRenderer.MeasureText(label, b.Font).Width + 36), 36);
                    x -= b.Width; b.Location = new Point(x, 14); x -= 10;
                    b.DialogResult = result;
                    footer.Controls.Add(b);
                    if (main) chosen = b;
                    if (result == DialogResult.Cancel || result == DialogResult.No || (choices.Count == 1)) f.CancelButton = b;
                }
                if (chosen != null) { f.AcceptButton = chosen; f.Shown += (s, e) => chosen.Focus(); }
                if (choices.Count == 1) f.CancelButton = chosen;
                if (icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Error) System.Media.SystemSounds.Exclamation.Play();
                var r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                return r == DialogResult.None ? (choices.Count == 1 ? DialogResult.OK : DialogResult.Cancel) : r;
            }
        }
    }

    /// <summary>Gives every window the app's dark look without each screen having to ask (see Install).</summary>
    static class Polish
    {
        static readonly bool OnWindows = Type.GetType("Mono.Runtime") == null && Environment.OSVersion.Platform == PlatformID.Win32NT;
        static readonly HashSet<Control> _done = new HashSet<Control>();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Styles every window the app opens (now and later): dark title bars, dark scrollbars, the app's dropdowns.</summary>
        public static void Install()
        {
            Application.Idle += (s, e) =>
            {
                try { foreach (Form f in Application.OpenForms) Style(f); } catch { }
            };
        }

        /// <summary>Styles a control and everything in it, and anything added to it later.</summary>
        public static void Style(Control c)
        {
            if (c == null || c.IsDisposed || !_done.Add(c)) return;
            c.Disposed += (s, e) => _done.Remove(c);
            if (c is Form form) WhenHandle(form, () => DarkTitle(form));
            if (c is ComboBox box) { ComboPainter.Attach(box); WhenHandle(box, () => Dark(box, "DarkMode_CFD")); }
            else if (c is ScrollableControl || c is ListBox || c is TreeView || c is ListView || (c is TextBoxBase t && t.Multiline) || c is DataGridView)
                WhenHandle(c, () => Dark(c, "DarkMode_Explorer"));
            if (c is ScrollableControl sc && !(c is Form)) SmoothScroll.Attach(sc);
            c.ControlAdded += (s, e) => Style(e.Control);
            foreach (Control child in c.Controls) Style(child);
        }

        /// <summary>Runs a step now if the control has its window, and again each time its window is made.</summary>
        static void WhenHandle(Control c, Action a)
        {
            if (c.IsHandleCreated) a();
            c.HandleCreated += (s, e) => a();
        }

        /// <summary>Asks Windows to draw a control's scrollbars (or a dropdown's list) in its dark style. Does nothing
        /// off Windows.</summary>
        static void Dark(Control c, string theme)
        {
            if (!OnWindows) return;
            try { SetWindowTheme(c.Handle, theme, null); } catch { }
        }

        /// <summary>Asks Windows for a dark title bar on a window (it tries the older setting number if the newer one
        /// isn't known).</summary>
        static void DarkTitle(Form f)
        {
            if (!OnWindows) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);   // 19: Windows 10 before 20H1
            }
            catch { }
        }
    }
}

namespace SkinManager.App
{
    /// <summary>A short note that floats at the bottom of the window for a few seconds ("Saved · Undo"),
    /// for news that doesn't need a click to go away.</summary>
    class Toast : Control
    {
        static Toast _current;
        readonly string _action;
        readonly Action _onAction;
        readonly Timer _timer = new Timer { Interval = 5000 };
        Rectangle _actionRect;
        bool _hover, _actionHover;

        /// <summary>Sizes the note to its text (and its action link, if any); it closes itself after five seconds
        /// unless the mouse is on it.</summary>
        Toast(string text, string action, Action onAction)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Text = text; _action = action; _onAction = onAction; Font = Theme.Body;
            var t = TextRenderer.MeasureText(text, Theme.Body);
            int aw = action == null ? 0 : TextRenderer.MeasureText(action, Theme.BodyBold).Width + 28;
            Size = new Size(Math.Min(t.Width + aw + 44, 720), 46);
            _timer.Tick += (s, e) => { if (!_hover) Close(); };
        }

        /// <summary>Shows a note at the bottom of a window (replacing one already showing).</summary>
        public static void Show(Form host, string text, string action = null, Action onAction = null)
        {
            if (host == null || host.IsDisposed) return;
            _current?.Close();
            string line = (text ?? "").Replace("\r\n", "  ").Replace("\n", "  ");
            var t = new Toast(line, action, onAction);
            if (line != text || line.Length > 90) new ToolTip().SetToolTip(t, text);
            host.Controls.Add(t);
            void Place() => t.Location = new Point((host.ClientSize.Width - t.Width) / 2, host.ClientSize.Height - t.Height - 24);
            Place(); host.Resize += (s, e) => Place();
            t.BringToFront(); t._timer.Start(); _current = t;
        }

        /// <summary>Takes the note away and lets go of it.</summary>
        void Close()
        {
            _timer.Stop();
            if (_current == this) _current = null;
            if (!IsDisposed) { Parent?.Controls.Remove(this); Dispose(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _actionHover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool a = _actionRect.Contains(e.Location);
            if (a != _actionHover) { _actionHover = a; Cursor = a ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_action != null && _actionRect.Contains(e.Location)) { var a = _onAction; Close(); a?.Invoke(); return; }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 4);
            using (var p = Theme.Rounded(new Rectangle(r.X, r.Y + 2, r.Width, r.Height), 10)) using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillPath(b, p);
            using (var p = Theme.Rounded(r, 10))
            {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(52, 56, 70), Color.FromArgb(40, 43, 55), LinearGradientMode.Vertical)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(Theme.Good)) g.FillEllipse(b, 16, Height / 2 - 5, 8, 8);
            int aw = _action == null ? 0 : TextRenderer.MeasureText(_action, Theme.BodyBold).Width + 28;
            TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(32, 0, Width - 40 - aw, Height - 2), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_action != null)
            {
                _actionRect = new Rectangle(Width - aw - 6, 6, aw, Height - 14);
                using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, _actionRect.X, 12, _actionRect.X, Height - 14);
                TextRenderer.DrawText(g, _action, Theme.BodyBold, _actionRect, _actionHover ? Color.FromArgb(255, 200, 100) : Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
    }
}
