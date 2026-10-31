// What's on the SD card that wasn't put there through this app: mods installed by hand, sorted by kind. The app
// leaves them alone; putting one of its own mods where one sits asks first, and keeps the one that was there.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class HandModsForm : Form
    {
        readonly HandList _list = new HandList();
        readonly Button _again = Theme.Secondary("Look again"), _close = Theme.Primary("Close"), _fresh = Theme.Secondary("Start over from now");
        readonly Label _intro = Theme.Label("", Theme.Small, Theme.Muted);
        readonly Func<bool, HandScan> _rescan;

        public HandModsForm(HandScan scan, Func<bool, HandScan> rescan)
        {
            _rescan = rescan;
            Text = "Mods put on by hand"; BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(720, 560); MinimumSize = new Size(560, 400);
            _intro.AutoSize = false; _intro.Location = new Point(18, 12);
            _list.Location = new Point(18, 84);
            Controls.AddRange(new Control[] { _intro, _list, _again, _close, _fresh });
            CancelButton = _close; _close.DialogResult = DialogResult.Cancel;
            _again.Click += (s, e) => Look(false);
            _fresh.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Count everything on your SD card right now as normal P+ files? From then on, only files added or changed after this point show up here.", "Start over from now", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                Look(true);
            };
            Resize += (s, e) => Arrange();
            Show(scan);
            Arrange();
        }

        /// <summary>Looks through the SD card again in the background (it reads every file: it can take a while).</summary>
        async void Look(bool fresh)
        {
            string was = _again.Text;
            _again.Enabled = _fresh.Enabled = false; _again.Text = "Looking...";
            try { var r = await System.Threading.Tasks.Task.Run(() => _rescan(fresh)); if (r != null && !IsDisposed) Show(r); }
            finally { if (!IsDisposed) { _again.Enabled = _fresh.Enabled = true; _again.Text = was; } }
        }

        void Arrange()
        {
            _intro.Size = new Size(ClientSize.Width - 36, 66);
            _list.Size = new Size(ClientSize.Width - 36, ClientSize.Height - 84 - 60);
            _close.Location = new Point(ClientSize.Width - _close.Width - 18, ClientSize.Height - _close.Height - 14);
            _again.Location = new Point(_close.Left - _again.Width - 10, _close.Top);
            _fresh.Location = new Point(18, _close.Top);
        }

        void Show(HandScan scan)
        {
            int n = scan?.Mods.Count ?? 0;
            bool snap = scan?.Version != null && scan.Version.StartsWith(CardBaseline.Prefix);
            _fresh.Visible = snap; _list.Snapshot = snap;
            if (snap)
            {
                DateTime.TryParse(scan.Version.Substring(CardBaseline.Prefix.Length), null, System.Globalization.DateTimeStyles.RoundtripKind, out var taken);
                _intro.Text = $"This SD card isn't the P+ that Brawl Online sets up (it's another mod pack, or P+ on its own), so the app compares it with how it looked on {taken:MMM d, h:mm tt}, the first time it checked: " +
                    (n == 0 ? "nothing was added or changed since. (Your costumes and stages already on the card show in Costume Slots and when you add a stage.)" : $"these {n} file(s) were added or changed since, not by this app. It leaves them alone and asks before putting a mod where one sits.");
                _list.Show(scan?.Mods ?? new List<HandMod>());
                return;
            }
            _intro.Text = n == 0
                ? "Everything on your SD card is either P+'s own file or a mod from this app. Nothing was put on by hand."
                : $"These {n} file(s) on your SD card aren't P+'s own and weren't put there by this app: mods installed by hand (the old way). " +
                  "The app leaves them alone. If you put one of your mods from the app where one of these sits, it asks first, and keeps yours: " +
                  "turning the app's mod off brings it back.";
            _list.Show(scan?.Mods ?? new List<HandMod>());
        }
    }

    /// <summary>The files, under a heading per kind (the ones that change how the game plays first, in red).</summary>
    class HandList : Control
    {
        List<(bool Header, string Kind, HandMod Mod)> _rows = new List<(bool, string, HandMod)>();
        int _scroll;
        public bool Snapshot;   // compared with the card's snapshot, not with P+
        const int H = 40, HeadH = 54;

        public HandList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            MouseWheel += (s, e) => { _scroll = Math.Max(0, Math.Min(Math.Max(0, Total - Height), _scroll - e.Delta / 2)); Invalidate(); };
            MouseEnter += (s, e) => Focus();
        }

        int Total => _rows.Sum(r => r.Header ? HeadH : H);

        public void Show(List<HandMod> mods)
        {
            _rows = new List<(bool, string, HandMod)>();
            foreach (var g in mods.GroupBy(m => m.Kind))
            {
                _rows.Add((true, g.Key, null));
                foreach (var m in g) _rows.Add((false, g.Key, m));
            }
            _scroll = 0; Invalidate();
        }

        static string Note(string kind, int count)
        {
            switch (kind)
            {
                case "Gameplay": return "These change how the game plays. The other player needs the exact same files, or online games go out of sync.";
                case "Costume": return "Looks only: fine online. Putting a skin from the app on one of these slots replaces it (it's kept and comes back).";
                case "Stage": return "A stage's looks are fine online; if its layout changed, the other player needs the same file.";
                case "Music": return "Music is only on your side: fine online.";
                case "Menus & pictures": return "Menus and pictures are only on your side: fine online.";
                default: return "";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_rows.Count == 0) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString("Nothing found.", Theme.Body, m, 14, 14); return; }
            g.TranslateTransform(0, -_scroll);
            var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            int y = 0;
            foreach (var r in _rows)
            {
                if (r.Header)
                {
                    int count = _rows.Count(x => !x.Header && x.Kind == r.Kind);
                    var color = r.Kind == "Gameplay" ? Theme.Bad : Theme.Accent;
                    using (var b = new SolidBrush(color)) g.DrawString($"{r.Kind.ToUpperInvariant()} · {count}", Theme.BodyBold, b, 12, y + 10);
                    using (var m = new SolidBrush(r.Kind == "Gameplay" ? Color.FromArgb(230, Theme.Bad) : Theme.Muted)) g.DrawString(Note(r.Kind, count), Theme.Small, m, new RectangleF(12, y + 31, Width - 24, 18), clip);
                    y += HeadH; continue;
                }
                var mod = r.Mod;
                using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 12, y + H - 1, Width - 12, y + H - 1);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(mod.Label, Theme.Body, t, new RectangleF(24, y + 4, Width * 0.62f, 20), clip);
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString(mod.Path, Theme.Small, m, new RectangleF(24, y + 21, Width * 0.62f, 16), clip);
                string tag = Snapshot ? (mod.Added ? "added since" : "changed since") : mod.Added ? "added (not in P+)" : "changed from P+'s";
                using (var m = new SolidBrush(mod.AffectsGameplay ? Theme.Bad : Theme.Muted)) g.DrawString(tag + (mod.AffectsGameplay ? " · gameplay" : ""), Theme.Small, m, new RectangleF(Width * 0.64f, y + 12, Width * 0.36f - 14, 18), new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += H;
            }
        }
    }
}
