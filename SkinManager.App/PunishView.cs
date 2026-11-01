// "What punishes this?": the selected move hits your shield; your out-of-shield options, best first, each with
// whether it's fast enough (frames to spare) and whether it reaches as far as their hit did.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class PunishView : ScrollView
    {
        PunishReport _r; string _status; Character _meChar;
        readonly ComboBox _me = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        public event Action<Character> MeChanged;
        sealed class Item { public string Text; public Character Value; public override string ToString() => Text; }

        public PunishView()
        {
            var l = Theme.Label("You play", Theme.Small, Theme.Muted); l.Location = new Point(0, 2);
            _me.Location = new Point(0, 20);
            Controls.Add(l); Controls.Add(_me);
            _me.SelectionChangeCommitted += (s, e) => { if (_me.SelectedItem is Item it) { _meChar = it.Value; MeChanged?.Invoke(it.Value); } };
        }

        public void SetCast(List<Character> cast, Character selected)
        {
            _me.BeginUpdate(); _me.Items.Clear();
            foreach (var c in cast) _me.Items.Add(new Item { Text = c.Name, Value = c });
            _me.SelectedIndex = Math.Max(0, cast.IndexOf(selected));
            _me.EndUpdate();
            _meChar = selected;
        }

        public void Status(string s) { _status = s; _r = null; ToTop(); Invalidate(); }
        public void Show(PunishReport r) { _r = r; _status = null; ToTop(); Invalidate(); }

        int Para(Graphics g, string text, Font font, int y, int w, Color color)
        {
            var rect = new Rectangle(0, y, w, 400);
            TextRenderer.DrawText(g, text, font, rect, color, TextFormatFlags.WordBreak);
            return TextRenderer.MeasureText(g, text, font, new Size(w, 400), TextFormatFlags.WordBreak).Height;
        }

        protected override void Draw(Graphics g, int width)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = width - 14, y = 64;
            if (_r == null) { ContentHeight = y + 60; TextRenderer.DrawText(g, _status ?? "", Theme.Body, new Rectangle(0, y, w, 60), Theme.Muted, TextFormatFlags.WordBreak); return; }
            var m = _r.Theirs;
            TextRenderer.DrawText(g, "When " + m.Name + " hits your shield", Theme.Bold(13f), new Point(0, y), Theme.Text); y += 30;
            if (_r.Problem != null) { y += Para(g, _r.Problem, Theme.Body, y, w, Theme.Muted); ContentHeight = y + 20; return; }
            string adv = (_r.Advantage >= 0 ? "+" : "") + _r.Advantage;
            string line = _r.Advantage >= 0
                ? $"It's {adv} on shield: they can act before you can do anything, so nothing punishes it out of shield."
                : $"It's {adv} on shield: once your shield stun ends, you have {_r.Window} frame{(_r.Window == 1 ? "" : "s")} to hit them before they can act.";
            y += Para(g, line, Theme.Body, y, w, _r.Advantage >= 0 ? Theme.Bad : Theme.Text) + 4;
            y += Para(g, (_r.LastOfSeveral ? $"Its last hit (the one that keeps you in shield longest) lands on frame {_r.HitFrame}" : $"It hits on frame {_r.HitFrame}") +
                $" for {_r.Damage:0.#}% (shield stun {_r.Stun} frames)" + (_r.TheirReach > 0 ? $", reaching {_r.TheirReach:0} units out." : "."), Theme.Small, y, w, Theme.Muted) + 12;
            if (_r.Rows.Count == 0) { TextRenderer.DrawText(g, "No out-of-shield options found for this fighter.", Theme.Body, new Point(0, y), Theme.Muted); ContentHeight = y + 30; return; }
            foreach (var row in _r.Rows)
            {
                var rect = new Rectangle(0, y, w, 50);
                using (var path = Theme.Rounded(rect, 8)) using (var b = new SolidBrush(Color.FromArgb(row.Works ? 36 : 22, 255, 255, 255))) g.FillPath(b, path);
                var mark = row.Works && row.Reaches ? ("✓", Theme.Good) : row.Works ? ("~", Theme.Warn) : ("✗", Theme.Bad);
                TextRenderer.DrawText(g, mark.Item1, Theme.Bold(15f), new Rectangle(8, y, 28, 50), mark.Item2, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, row.Name, Theme.BodyBold, new Point(42, y + 7), Theme.Text);
                TextRenderer.DrawText(g, $"hits on frame {row.Startup}" + (row.Jump > 0 ? $" after a {row.Jump}-frame jump" : " straight out of shield") + (row.Reach > 0 ? $"  ·  reaches {row.Reach:0} units" : ""),
                    Theme.Small, new Point(42, y + 28), Theme.Muted);
                string verdict = !row.Works ? $"{-row.Spare} frame{(-row.Spare == 1 ? "" : "s")} too slow"
                    : (row.Spare == 0 ? "just in time" : $"{row.Spare} frame{(row.Spare == 1 ? "" : "s")} to spare") + (row.Reaches ? "" : " · only if they're close");
                var vs = TextRenderer.MeasureText(verdict, Theme.BodyBold);
                TextRenderer.DrawText(g, verdict, Theme.BodyBold, new Point(rect.Right - vs.Width - 12, y + 15), mark.Item2);
                y += 56;
            }
            y += 6;
            y += Para(g, "Assumes their hit lands on your shield and you act as soon as the stun ends. Aerials: they land right after with an L-cancel. Shield pushback isn't counted, so \"reaches\" compares how far your hit and theirs get.",
                Theme.Small, y, w, Theme.Muted);
            ContentHeight = y + 16;
        }
    }
}
