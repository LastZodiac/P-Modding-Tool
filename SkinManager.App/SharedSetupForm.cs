// A setup someone shared: every mod in it, whether you already have it, and a button to get each one (or all you're
// missing) from GameBanana. Each mod goes through the usual add steps and checks, one at a time.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class SharedSetupForm : Form
    {
        readonly SharedSetup _setup; readonly Library _lib; readonly Func<SharedMod, bool> _get;
        readonly Panel _rows = new Panel { AutoScroll = true, BackColor = Theme.Panel };
        readonly Button _all = Theme.Primary("Get everything I'm missing"), _close = Theme.Secondary("Close");
        readonly Label _sum = Theme.Label("", Theme.Small, Theme.Muted);

        public SharedSetupForm(SharedSetup setup, Library lib, Func<SharedMod, bool> get)
        {
            _setup = setup; _lib = lib; _get = get;
            Text = "Shared setup: " + setup.Name; BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(680, 520);
            var title = Theme.Label(setup.Name, Theme.Bold(15f), Theme.Text); title.Location = new Point(18, 12);
            var intro = Theme.Label($"{setup.Mods.Count} mod{(setup.Mods.Count == 1 ? "" : "s")} · shared {setup.Made.ToLocalTime():MMM d, yyyy}. Each one you get goes through the usual steps, like adding it from the Mod Store; nothing changes until you say so.", Theme.Small, Theme.Muted);
            intro.AutoSize = false; intro.Location = new Point(18, 42); intro.Size = new Size(644, 34);
            _rows.Location = new Point(18, 80); _rows.Size = new Size(644, 380);
            _all.AutoSize = false; _all.Size = new Size(230, 34); _all.Location = new Point(18, 472);
            _close.AutoSize = false; _close.Size = new Size(120, 34); _close.Location = new Point(542, 472);
            _sum.AutoSize = false; _sum.Location = new Point(258, 474); _sum.Size = new Size(276, 34);
            Controls.AddRange(new Control[] { title, intro, _rows, _all, _close, _sum });
            CancelButton = _close; _close.DialogResult = DialogResult.Cancel;
            _all.Click += (s, e) =>
            {
                foreach (var m in _setup.Mods.Where(x => x.Fetchable && !SharedSetup.Have(_lib, x)).ToList())
                {
                    if (!_get(m)) break;   // stopped or failed: leave the rest for later
                    Fill();
                }
                Fill();
            };
            Fill();
        }

        void Fill()
        {
            _rows.SuspendLayout();
            foreach (Control c in _rows.Controls.Cast<Control>().ToList()) c.Dispose();
            _rows.Controls.Clear();
            int y = 6, w = _rows.ClientSize.Width - 24;
            foreach (var m in _setup.Mods)
            {
                bool have = SharedSetup.Have(_lib, m);
                var name = Theme.Label(m.Name, Theme.BodyBold, Theme.Text); name.AutoSize = false; name.AutoEllipsis = true; name.Location = new Point(10, y); name.Size = new Size(w - 150, 22);
                string kind = m.Kind == "song" ? "Music" : m.Kind == "stage" ? "Stage" : "Skin";
                var sub = Theme.Label(kind + (string.IsNullOrEmpty(m.Where) ? "" : " · " + m.Where) + (string.IsNullOrEmpty(m.Author) ? "" : " · by " + m.Author), Theme.Small, Theme.Muted);
                sub.AutoSize = false; sub.AutoEllipsis = true; sub.Location = new Point(10, y + 22); sub.Size = new Size(w - 150, 18);
                _rows.Controls.Add(name); _rows.Controls.Add(sub);
                Control right;
                if (have) right = Theme.Label("✓ You have it", Theme.BodyBold, Theme.Good);
                else if (m.Fetchable)
                {
                    var b = Theme.Primary("Get it"); var mod = m;
                    b.Click += (s, e) => { _get(mod); Fill(); };
                    right = b;
                }
                else
                {
                    var l = Theme.Label("Added from a file: ask them for it", Theme.Small, Theme.Muted); l.AutoSize = false; l.Size = new Size(140, 36);
                    right = l;
                }
                right.Location = new Point(w - 136, y + 4);
                _rows.Controls.Add(right);
                y += 50;
            }
            _rows.ResumeLayout();
            int missing = _setup.Mods.Count(x => x.Fetchable && !SharedSetup.Have(_lib, x));
            int manual = _setup.Mods.Count(x => !x.Fetchable && !SharedSetup.Have(_lib, x));
            _all.Enabled = missing > 0;
            _all.Text = missing > 0 ? $"Get the {missing} I'm missing" : "You have them all";
            _sum.Text = manual > 0 ? $"{manual} came from a file on their PC, not GameBanana, so the app can't fetch {(manual == 1 ? "it" : "them")}." : "";
        }
    }
}
