// Your profiles in one window: switch to one, make a new one, rename, copy or delete them. "P+ as it comes" (no mods)
// is always at the bottom and can never be renamed or deleted.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class ProfilesForm : Form
    {
        readonly Library _lib;
        readonly Func<string, bool> _switch; readonly Action _new; readonly Action<string> _rename, _copy, _delete;
        readonly ProfileList _list = new ProfileList();
        readonly Button _use = Theme.Primary("Switch to it"), _ren = Theme.Secondary("Rename..."), _cp = Theme.Secondary("Copy..."),
                        _del = Theme.Secondary("Delete..."), _add = Theme.Secondary("New profile..."), _close = Theme.Secondary("Close");
        readonly Label _note = Theme.Label("", Theme.Small, Theme.Muted);

        public ProfilesForm(Library lib, Func<string, bool> switchTo, Action create, Action<string> rename, Action<string> copy, Action<string> delete)
        {
            _lib = lib; _switch = switchTo; _new = create; _rename = rename; _copy = copy; _delete = delete;
            Text = "Your profiles"; BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(600, 440);
            var intro = Theme.Label("A profile is a set of mods that are on. Switching to one turns exactly its mods on (one save to your SD card). Your mods always stay in My Mods.", Theme.Small, Theme.Muted);
            intro.AutoSize = false; intro.Location = new Point(18, 14); intro.Size = new Size(564, 36);
            _list.Location = new Point(18, 56); _list.Size = new Size(400, 360);
            int bx = 432, by = 56;
            foreach (var b in new[] { _use, _ren, _cp, _del, _add })
            {
                b.AutoSize = false; b.Size = new Size(150, 34); b.Location = new Point(bx, by); by += b == _del ? 58 : 42;
            }
            _del.ForeColor = Theme.Bad;
            _close.AutoSize = false; _close.Size = new Size(150, 34); _close.Location = new Point(bx, 382);
            _note.AutoSize = false; _note.Location = new Point(bx, 280); _note.Size = new Size(156, 96);
            Controls.AddRange(new Control[] { intro, _list, _use, _ren, _cp, _del, _add, _close, _note });
            CancelButton = _close; _close.DialogResult = DialogResult.Cancel;
            _list.Changed += Update2;
            _list.Activated += () => { if (_use.Enabled) _use.PerformClick(); };
            _use.Click += (s, e) => { _switch(_list.SelectedName); Fill(); };
            _ren.Click += (s, e) => { _rename(_list.SelectedName); Fill(); };
            _cp.Click += (s, e) => { _copy(_list.SelectedName); Fill(); };
            _del.Click += (s, e) => { _delete(_list.SelectedName); Fill(); };
            _add.Click += (s, e) => { _new(); Fill(); };
            Fill();
            _list.Select(_lib.ActiveProfile ?? ProfileList.AsItComes);
        }

        void Fill()
        {
            var rows = _lib.Profiles.Select(p => new ProfileList.Row { Name = p.Name, Mods = p.On.Count, InUse = _lib.ActiveProfile == p.Name }).ToList();
            rows.Add(new ProfileList.Row { Name = ProfileList.AsItComes, Locked = true, InUse = _lib.ActiveProfile == null });
            _list.Show(rows);
            Update2();
        }

        void Update2()
        {
            var r = _list.SelectedRow;
            bool locked = r == null || r.Locked;
            _use.Enabled = r != null && !r.InUse;
            _use.Text = r != null && r.InUse ? "In use" : "Switch to it";
            _ren.Enabled = _cp.Enabled = _del.Enabled = !locked;
            _note.Text = r == null ? "" : r.Locked ? "P+ as it comes is always here: it can't be renamed or deleted." : r.InUse ? "This is the profile in use." : "";
        }
    }

    /// <summary>The profiles, one row each; click to pick, double-click to switch.</summary>
    class ProfileList : Control
    {
        public const string AsItComes = Library.DefaultName;
        public sealed class Row { public string Name; public int Mods; public bool InUse, Locked; }
        List<Row> _rows = new List<Row>();
        int _sel = -1, _scroll;
        const int H = 52;
        public event Action Changed, Activated;
        public Row SelectedRow => _sel >= 0 && _sel < _rows.Count ? _rows[_sel] : null;
        public string SelectedName => SelectedRow?.Locked == true ? null : SelectedRow?.Name;

        public ProfileList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            MouseWheel += (s, e) => { _scroll = Math.Max(0, Math.Min(Math.Max(0, _rows.Count * H - Height), _scroll - e.Delta / 3)); Invalidate(); };
            MouseEnter += (s, e) => Focus();
            MouseDown += (s, e) => { int i = (e.Y + _scroll) / H; if (i >= 0 && i < _rows.Count) { _sel = i; Invalidate(); Changed?.Invoke(); } };
            MouseDoubleClick += (s, e) => { if ((e.Y + _scroll) / H == _sel) Activated?.Invoke(); };
        }

        public void Show(List<Row> rows)
        {
            string keep = SelectedRow?.Name;
            _rows = rows; _sel = keep == null ? -1 : rows.FindIndex(r => r.Name == keep);
            if (_sel < 0) _sel = rows.FindIndex(r => r.InUse);
            Invalidate();
        }

        public void Select(string name) { _sel = _rows.FindIndex(r => r.Name == name); Invalidate(); Changed?.Invoke(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(0, -_scroll);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i]; int y = i * H;
                if (i == _sel) using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, 0, y, Width, H - 1);
                if (i == _sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, y, 4, H - 1);
                using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, y + H - 1, Width, y + H - 1);
                float x = 16;
                if (r.Locked)
                {
                    // a small padlock
                    using (var pen = new Pen(Theme.Muted, 2)) g.DrawArc(pen, x + 2, y + 13, 10, 12, 180, 180);
                    using (var b = new SolidBrush(Theme.Muted)) g.FillRectangle(b, x, y + 20, 14, 11);
                    x += 24;
                }
                var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(r.Name, Theme.BodyBold, t, new RectangleF(x, y + 8, Width - x - 90, 20), fmt);
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString(r.Locked ? "no mods · always here, can't be deleted" : r.Mods == 1 ? "1 mod on" : $"{r.Mods} mods on", Theme.Small, m, new RectangleF(x, y + 28, Width - x - 90, 18), fmt);
                if (r.InUse)
                {
                    var pill = new RectangleF(Width - 78, y + 15, 64, 22);
                    using (var b = new SolidBrush(Color.FromArgb(60, Theme.Good))) using (var p = StatsArt.Round(pill, 11)) g.FillPath(b, p);
                    using (var t = new SolidBrush(Theme.Good)) g.DrawString("IN USE", Theme.Small, t, pill, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
            }
        }
    }
}
