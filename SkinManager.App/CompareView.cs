// Two fighters side by side: every move they share, with startup, active frames and first actionable frame for
// each, and the faster one in green.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    /// <summary>The Frame Data page's compare tab: two fighters' shared moves side by side.</summary>
    class CompareView : ScrollView
    {
        FighterBook _a, _b; string _status; static int _col = 74;
        readonly ComboBox _other = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        public event Action<Character> OtherChanged;
        /// <summary>A fighter in the "Compare with" list.</summary>
        sealed class Item { public string Text; public Character Value; public override string ToString() => Text; }

        /// <summary>Adds the "Compare with" list; picking a fighter there raises OtherChanged.</summary>
        public CompareView()
        {
            var l = Theme.Label("Compare with", Theme.Small, Theme.Muted); l.Location = new Point(0, 2);
            _other.Location = new Point(0, 20);
            Controls.Add(l); Controls.Add(_other);
            _other.SelectionChangeCommitted += (s, e) => { if (_other.SelectedItem is Item it) OtherChanged?.Invoke(it.Value); };
        }

        /// <summary>Fills the "Compare with" list with the fighters and picks the one being compared.</summary>
        public void SetCast(List<Character> cast, Character selected)
        {
            _other.BeginUpdate(); _other.Items.Clear();
            foreach (var c in cast) _other.Items.Add(new Item { Text = c.Name, Value = c });
            _other.SelectedIndex = Math.Max(0, cast.IndexOf(selected));
            _other.EndUpdate();
        }

        public void Status(string s) { _status = s; _a = _b = null; Invalidate(); }
        public void Show(FighterBook a, FighterBook b) { _a = a; _b = b; _status = null; Invalidate(); }

        protected override void Draw(Graphics g, int width)
        {
            int y = 64, w = width - 14;
            if (_a == null || _b == null) { DrawT(g, _status ?? "", Theme.Body, new Point(0, y), Theme.Muted); ContentHeight = y + 40; return; }
            // columns: move | (startup, active, FAF) for each
            // fit the window: the name column takes what the six number columns leave (each 44-74 wide)
            int col = Math.Max(44, Math.Min(74, (w - 120) / 6)), nameW = Math.Max(120, w - 6 * col), cx = nameW; _col = col;
            string[] heads = { "Startup", "Active", "FAF" };
            DrawT(g, _a.Character.Name, Theme.BodyBold, new Rectangle(cx, y, 3 * _col, 20), Theme.Accent, TextFormatFlags.HorizontalCenter);
            DrawT(g, _b.Character.Name, Theme.BodyBold, new Rectangle(cx + 3 * _col, y, 3 * _col, 20), Theme.Accent, TextFormatFlags.HorizontalCenter);
            y += 22;
            for (int k = 0; k < 6; k++) DrawT(g, heads[k % 3], Theme.Small, new Rectangle(cx + k * _col, y, _col, 18), Theme.Muted, TextFormatFlags.HorizontalCenter);
            y += 22;
            string group = null;
            var others = _b.Entries.GroupBy(e => e.Name).ToDictionary(x => x.Key, x => x.First());
            // same move under a slightly different name (one fighter's "Forward tilt" is the other's "Forward tilt (up)"):
            // matched by the game's own animation name, then by the start of the name
            MoveEntry Match(MoveEntry e) =>
                others.TryGetValue(e.Name, out var o) ? o
                : _b.Entries.FirstOrDefault(x => x.Group == e.Group && x.Parts.Length > 0 && e.Parts.Length > 0 && x.Parts[0] == e.Parts[0])
                ?? _b.Entries.FirstOrDefault(x => x.Group == e.Group && (x.Name.StartsWith(e.Name + " ") || e.Name.StartsWith(x.Name + " ")));
            foreach (var e in _a.Entries)
            {
                if (e.Group != group)
                {
                    group = e.Group; y += 6;
                    DrawT(g, group.ToUpperInvariant(), Theme.Small, new Point(0, y), Theme.Accent); y += 22;
                }
                var o = Match(e);
                using (var b = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) g.FillRectangle(b, 0, y, w, 26);
                DrawT(g, o != null && o.Name != e.Name ? e.Name + "  ·  " + o.Name : e.Name, Theme.Body, new Rectangle(4, y, nameW - 8, 26), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                Cells(g, e.Data, o?.Data, cx, y);
                if (o != null) Cells(g, o.Data, e.Data, cx + 3 * _col, y);
                else DrawT(g, "no such move", Theme.Small, new Rectangle(cx + 3 * _col, y, 3 * _col, 26), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                y += 28;
            }
            y += 8;
            DrawT(g, "Green: the faster of the two (earlier hit, or able to act sooner). Moves are matched by name; ones only the other fighter has aren't listed.",
                Theme.Small, new Rectangle(0, y, w, 40), Theme.Muted, TextFormatFlags.WordBreak);
            ContentHeight = y + 44;
        }

        /// <summary>Draws one fighter's startup, active frames and FAF for a move, in green where it beats the other
        /// fighter's.</summary>
        static void Cells(Graphics g, MoveData d, MoveData other, int x, int y)
        {
            Color C(int mine, int theirs) => other == null || mine <= 0 || theirs <= 0 || mine == theirs ? Theme.Text : mine < theirs ? Theme.Good : Theme.Muted;
            var f = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            int c = _col;
            DrawT(g, d.Startup > 0 ? d.Startup.ToString() : "—", Theme.Body, new Rectangle(x, y, c, 26), C(d.Startup, other?.Startup ?? 0), f);
            // several hits: just when the first starts and the last ends (the full list is on the moves tab)
            string act = d.Active.Count == 0 ? "—" : d.Active.Count == 1 ? d.ActiveText : d.Active[0].From + "…" + d.Active[d.Active.Count - 1].To;
            DrawT(g, act, Theme.Small, new Rectangle(x + c, y, c, 26), Theme.Bad, f);
            DrawT(g, d.Faf.ToString(), Theme.Body, new Rectangle(x + 2 * c, y, c, 26), C(d.Faf, other?.Faf ?? 0), f);
        }
    }
}
