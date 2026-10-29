// Launch & combos: for the move picked on the left, where a hit sends the opponent (at a percent you choose, against
// the fighter you choose), how long they're stuck, and which of your moves still connect before they can act:
// a true combo. Plus a chart of the percents each follow-up works at.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class ComboView : Control
    {
        FighterBook _me, _them; MoveEntry _move; string _status;
        readonly ComboBox _target = Box(170), _di = Box(130), _hit = Box(150);
        readonly TrackBar _pct = new TrackBar { Minimum = 0, Maximum = 200, TickFrequency = 25, Value = 30, Width = 180, AutoSize = false, Height = 28, BackColor = Theme.Back };
        readonly Label _pctLabel = Theme.Label("30%", Theme.BodyBold);
        public event Action<Character> TargetChanged;
        LaunchResult _launch; List<FollowUp> _now; Dictionary<MoveEntry, List<(int From, int To)>> _ranges;
        (int Frame, Hitbox Hit, float FootX, float FootY)? _contact;
        Rectangle _diagram, _stats, _chart;
        List<(Rectangle r, int pct)> _clickable = new List<(Rectangle, int)>();
        int _scroll;   // chart rows scrolled past

        static ComboBox Box(int w) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        sealed class Item { public string Text; public object Value; public override string ToString() => Text; }

        public int Percent => _pct.Value;

        public ComboView()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            var l1 = Theme.Label("Against", Theme.Small, Theme.Muted); var l2 = Theme.Label("Percent", Theme.Small, Theme.Muted);
            var l3 = Theme.Label("DI", Theme.Small, Theme.Muted); var l4 = Theme.Label("Hitbox", Theme.Small, Theme.Muted);
            l1.Tag = "l1"; l2.Tag = "l2"; l3.Tag = "l3"; l4.Tag = "l4";
            Controls.AddRange(new Control[] { l1, l2, l3, l4, _target, _pct, _pctLabel, _di, _hit });
            _di.Items.AddRange(new object[] { new Item { Text = "No DI", Value = 0 }, new Item { Text = "DI towards you", Value = -1 }, new Item { Text = "DI away", Value = 1 } });
            _di.SelectedIndex = 0;
            _pct.ValueChanged += (s, e) => { _pctLabel.Text = _pct.Value + "%"; Recalc(false); };
            _di.SelectedIndexChanged += (s, e) => Recalc(true);
            _hit.SelectedIndexChanged += (s, e) => Recalc(true);
            _target.SelectedIndexChanged += (s, e) => { if (_target.SelectedItem is Item it && it.Value is Character c) TargetChanged?.Invoke(c); };
            MouseClick += (s, e) => { foreach (var (r, p) in _clickable) if (r.Contains(e.Location)) { _pct.Value = Math.Max(0, Math.Min(200, p)); break; } };
            Resize += (s, e) => Arrange();
            MouseWheel += (s, e) => { if (_chart.Contains(e.Location)) { _scroll = Math.Max(0, _scroll - Math.Sign(e.Delta) * 2); Invalidate(); } };
            MouseEnter += (s, e) => Focus();
            Arrange();
        }

        int _rowsTop = 52;
        void Arrange()
        {
            int w = Width;
            // one row when there's room, two otherwise
            bool two = w < 720;
            _target.Width = two ? Math.Min(200, w / 2 - 12) : 160;
            _pct.Width = two ? Math.Min(240, w / 2 - 60) : 170;
            _di.Width = two ? Math.Min(200, w / 2 - 12) : 130; _hit.Width = two ? Math.Min(240, w / 2 - 12) : Math.Max(120, w - 160 - 170 - 46 - 130 - 36);
            _target.Location = new Point(0, 16);
            _pct.Location = new Point(_target.Right + 12, 14); _pctLabel.Location = new Point(_pct.Right + 2, 18);
            if (two) { _di.Location = new Point(0, 62); _hit.Location = new Point(_di.Right + 12, 62); }
            else { _di.Location = new Point(_pctLabel.Left + 46, 16); _hit.Location = new Point(_di.Right + 12, 16); }
            foreach (Control c in Controls) if (c is Label l && l.Tag is string t) { var tg = t == "l1" ? (Control)_target : t == "l2" ? _pct : t == "l3" ? _di : _hit; l.Location = new Point(tg.Left + 2, tg.Top - 16); }
            int top = two ? 98 : 52;
            _rowsTop = top;
            int dh = Math.Max(150, Math.Min(240, (Height - top) / 2 - 10));
            int dw = (int)(w * 0.58);
            _diagram = new Rectangle(0, top, dw, dh);
            _stats = new Rectangle(dw + 12, top, w - dw - 12, dh);
            _chart = new Rectangle(0, top + dh + 12, w, Math.Max(80, Height - top - dh - 12));
            Invalidate();
        }

        public void SetCast(List<Character> cast, Character selected)
        {
            _target.BeginUpdate(); _target.Items.Clear();
            foreach (var c in cast) _target.Items.Add(new Item { Text = c.Name, Value = c });
            _target.SelectedIndex = Math.Max(0, cast.IndexOf(selected));
            _target.EndUpdate();
        }

        public void Status(string s) { _status = s; Invalidate(); }

        /// <summary>A new attacker move or target (both must be measured).</summary>
        public void Show(FighterBook me, FighterBook them, MoveEntry move)
        {
            bool newMove = _move != move || _me != me;
            _me = me; _them = them; _move = move; _status = null; if (newMove) _scroll = 0;
            if (newMove)
            {
                _hit.BeginUpdate(); _hit.Items.Clear();
                _hit.Items.Add(new Item { Text = "First to hit", Value = -1 });
                if (move != null) foreach (var h in move.Data.Hits.Where(h => !h.Grab).GroupBy(h => h.Id).Select(g => g.First()))
                        _hit.Items.Add(new Item { Text = $"#{h.Id}: {h.DamageText} {h.Angle}°", Value = h.Id });
                _hit.SelectedIndex = 0; _hit.EndUpdate();
            }
            Recalc(true);
        }

        void Recalc(bool ranges)
        {
            _launch = null; _now = null; _contact = null;
            if (_me?.Places == null || _them?.Body == null || _move == null) { if (ranges) _ranges = null; Invalidate(); return; }
            int hitId = _hit.SelectedItem is Item it && it.Value is int v ? v : -1;
            int di = _di.SelectedItem is Item d && d.Value is int dv ? dv : 0;
            if (_me.Places.TryGetValue(_move.Data, out var pl)) _contact = Combos.FirstContact(_move, pl, _them.Body, hitId);
            if (_contact != null)
            {
                var c = _contact.Value;
                _launch = Combos.Launch(_move.Data, c.Hit, c.Frame, _pct.Value, _them.Body, c.FootX, c.FootY, di);
                _now = Combos.FollowUps(_me, _me.Places, _move, _launch, _them.Body);
                if (ranges) _ranges = Combos.Ranges(_me, _me.Places, _move, _them.Body, hitId, di, 2);
            }
            else if (ranges) _ranges = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            _clickable.Clear();
            using (var bg = new SolidBrush(Color.FromArgb(200, 20, 22, 28)))
            {
                using (var p = StatsArt.Round(_diagram, 10)) g.FillPath(bg, p);
                using (var p = StatsArt.Round(_stats, 10)) g.FillPath(bg, p);
                using (var p = StatsArt.Round(_chart, 10)) g.FillPath(bg, p);
            }
            if (_status != null || _move == null) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString(_status ?? "Pick a move on the left.", Theme.Body, m, _diagram.X + 14, _diagram.Y + 14); return; }
            if (_launch == null)
            {
                using (var m = new SolidBrush(Theme.Muted))
                    g.DrawString(_move.Data.Throw != null || _move.Data.Hits.Any(h => !h.Grab) ? "Couldn't place this hit." : "This move doesn't hit (nothing to launch).", Theme.Body, m, _diagram.X + 14, _diagram.Y + 14);
                return;
            }
            DrawDiagram(g); DrawStats(g); DrawChart(g);
        }

        void DrawDiagram(Graphics g)
        {
            var L = _launch; var body = _them.Body; var me = _me.Body;
            var pts = L.Path.Take(L.Hitstun + 1).ToList();
            float minX = Math.Min(-me.Width, pts.Min(p => p.X) - body.Width / 2), maxX = Math.Max(L.StartX + body.Width, pts.Max(p => p.X) + body.Width / 2);
            float minY = 0, maxY = Math.Max(me.Height, pts.Max(p => p.Y) + body.Height / 2);
            // not too narrow a picture: widen around the middle
            float wantW = (maxY - minY) * _diagram.Width / Math.Max(1f, _diagram.Height) * 0.8f;
            if (maxX - minX < wantW) { float mid = (minX + maxX) / 2; minX = mid - wantW / 2; maxX = mid + wantW / 2; }
            var area = Rectangle.Inflate(_diagram, -18, -18); area.Y += 10; area.Height -= 10;
            float scale = Math.Min(area.Width / Math.Max(1, maxX - minX), area.Height / Math.Max(1, maxY - minY));
            float ox = area.X + (area.Width - (maxX - minX) * scale) / 2 - minX * scale, oy = area.Bottom;
            PointF P(float x, float y) => new PointF(ox + x * scale, oy - y * scale);
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString("WHERE IT SENDS THEM", Theme.Small, m, _diagram.X + 12, _diagram.Y + 8);
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f)) g.DrawLine(pen, _diagram.X + 8, oy, _diagram.Right - 8, oy);
            // you and them
            var you = new RectangleF(P(-me.Width / 2, me.Height).X, P(0, me.Height).Y, me.Width * scale, me.Height * scale);
            using (var b = new SolidBrush(Color.FromArgb(70, Theme.Accent))) using (var path = StatsArt.Round(you, 6)) g.FillPath(b, path);
            Head(g, _me.Character, you);
            using (var t = new SolidBrush(Theme.Accent)) g.DrawString(_me.Character.Name, Theme.Small, t, you.X, you.Y - 16);
            var start = new RectangleF(P(L.StartX - body.Width / 2, 0).X, P(0, L.StartY + body.Height).Y, body.Width * scale, body.Height * scale);
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash }) using (var path = StatsArt.Round(start, 6)) g.DrawPath(pen, path);
            // the path, a dot every frame, bigger every 5
            if (pts.Count > 1) using (var pen = new Pen(Color.FromArgb(220, 255, 120, 110), 2f)) g.DrawLines(pen, pts.Select(p => P(p.X, p.Y)).ToArray());
            for (int i = 1; i < pts.Count; i++)
            {
                var p = P(pts[i].X, pts[i].Y); float r = i % 5 == 0 ? 3.5f : 1.8f;
                using (var b = new SolidBrush(Color.FromArgb(230, 255, 160, 150))) g.FillEllipse(b, p.X - r, p.Y - r, 2 * r, 2 * r);
            }
            // where they can act again
            var end = pts[pts.Count - 1]; var ep = P(end.X, end.Y);
            var endBox = new RectangleF(P(end.X - body.Width / 2, 0).X, P(0, end.Y + body.Height / 2).Y, body.Width * scale, body.Height * scale);
            using (var b = new SolidBrush(Color.FromArgb(60, Theme.Good))) using (var path = StatsArt.Round(endBox, 6)) g.FillPath(b, path);
            Head(g, _them.Character, endBox);
            using (var t = new SolidBrush(Theme.Good)) g.DrawString($"{_them.Character.Name} can act ({L.Hitstun} frames)", Theme.Small, t, Math.Min(endBox.X, _diagram.Right - 190), Math.Max(_diagram.Y + 22, endBox.Y - 16));
            if (L.TechFrame > 0 && L.TechFrame < pts.Count)
            {
                var tp = P(pts[L.TechFrame].X, 0);
                using (var b = new SolidBrush(Color.FromArgb(90, 150, 255))) g.FillEllipse(b, tp.X - 5, tp.Y - 5, 10, 10);
                using (var t = new SolidBrush(Color.FromArgb(140, 180, 255))) g.DrawString($"can tech (frame {L.TechFrame})", Theme.Small, t, tp.X + 6, tp.Y - 18);
            }
            // launch arrow
            var sc = P(L.StartX, L.StartY + body.Height / 2);
            double rad = L.Angle * Math.PI / 180; float len = 34;
            using (var pen = new Pen(Theme.Bad, 3f) { EndCap = LineCap.ArrowAnchor }) g.DrawLine(pen, sc, new PointF(sc.X + (float)Math.Cos(rad) * len, sc.Y - (float)Math.Sin(rad) * len));
        }

        static Dictionary<string, int> _ids;
        /// <summary>The fighter's head at the top of a body box.</summary>
        static void Head(Graphics g, Character c, RectangleF box)
        {
            if (_ids == null) _ids = Enumerable.Range(0, 60).GroupBy(Fighters.Name).ToDictionary(x => x.Key, x => x.Min());
            var art = StatsArt.Cached;
            if (art == null || !_ids.TryGetValue(c.Name, out int id)) return;
            var img = art.Head(Fighters.Same(id)); if (img == null) return;
            float s = Math.Min(box.Width, 30); if (s < 10) return;
            StatsArt.Cover(g, img, new RectangleF(box.X + (box.Width - s) / 2, box.Y + 3, s, s), 5);
        }

        void DrawStats(Graphics g)
        {
            var L = _launch; int x = _stats.X + 14, y = _stats.Y + 10;
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"AT {_pct.Value}% ON {_them.Character.Name.ToUpperInvariant()}", Theme.Small, m, x, y);
            y += 22;
            void Row(string k, string v, Color? c = null)
            {
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString(k, Theme.Body, m, x, y);
                using (var t = new SolidBrush(c ?? Theme.Text)) g.DrawString(v, Theme.BodyBold, t, new RectangleF(x + 104, y, _stats.Right - x - 108, 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += 22;
            }
            Row("Hits on frame", L.HitFrame.ToString());
            Row("Knockback", L.Knockback.ToString("0") + (L.Tumble ? " · tumble" : ""));
            Row("Launch angle", L.Angle.ToString("0") + "°" + (L.Hit.Angle == 361 ? " (361)" : ""));
            Row("Hitstun", L.Hitstun + " frames");
            Row("On hit", (L.Advantage >= 0 ? "+" : "") + L.Advantage, L.Advantage >= 0 ? Theme.Good : Theme.Bad);
            Row("On shield", (L.ShieldAdvantage >= 0 ? "+" : "") + L.ShieldAdvantage, L.ShieldAdvantage >= 0 ? Theme.Good : Theme.Bad);
            if (_move.Data.LandingLag > 0) using (var m = new SolidBrush(Theme.Muted)) g.DrawString("aerial: landed + L-cancelled", Theme.Small, m, x, y);
        }

        void DrawChart(Graphics g)
        {
            int x = _chart.X + 14, y = _chart.Y + 10, w = _chart.Width - 28;
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString("TRUE COMBOS FROM " + _move.Name.ToUpperInvariant() + " — green: the percents it works at (click to jump there)", Theme.Small, m, x, y);
            y += 22;
            int nameW = Math.Min(190, w / 3), barX = x + nameW + 8, barW = w - nameW - 70;
            // the percent scale
            using (var m = new SolidBrush(Theme.Muted)) using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255)))
                for (int p = 0; p <= 200; p += 50) { float px = barX + barW * p / 200f; g.DrawString(p + "%", Theme.Small, m, px - 10, y - 2); }
            y += 18;
            float cur = barX + barW * _pct.Value / 200f;
            var rows = (_ranges ?? new Dictionary<MoveEntry, List<(int, int)>>()).OrderBy(kv => kv.Value[0].Item1).ThenByDescending(kv => kv.Value.Sum(r => r.Item2 - r.Item1)).ToList();
            if (rows.Count == 0) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString("Nothing true combos from this at any percent (against " + _them.Character.Name + ", with these settings).", Theme.Body, m, x, y + 4); }
            int top = y;
            int fit = Math.Max(1, (_chart.Bottom - 30 - y) / 22);
            _scroll = Math.Max(0, Math.Min(_scroll, rows.Count - fit));
            if (_scroll > 0) using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"▲ {_scroll} more (scroll)", Theme.Small, m, barX + barW - 90, top - 20);
            foreach (var kv in rows.Skip(_scroll))
            {
                if (y > _chart.Bottom - 48) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"▼ {rows.Count - rows.IndexOf(kv)} more (scroll)", Theme.Small, m, x, y); break; }
                var now = _now?.FirstOrDefault(f => f.Move == kv.Key);
                bool works = now != null && now.Works;
                using (var t = new SolidBrush(works ? Theme.Text : Theme.Muted))
                    g.DrawString(kv.Key.Name + (now?.How != null && works ? " · " + now.How : ""), works ? Theme.BodyBold : Theme.Body, t, new RectangleF(x, y + 1, nameW, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(b, barX, y + 5, barW, 10);
                foreach (var (from, to) in kv.Value)
                {
                    var r = new RectangleF(barX + barW * from / 200f, y + 5, Math.Max(3, barW * (to - from + 2) / 200f), 10);
                    using (var b = new SolidBrush(Theme.Good)) g.FillRectangle(b, r);
                    _clickable.Add((Rectangle.Round(new RectangleF(r.X, y, r.Width, 20)), (from + to) / 2));
                }
                if (works) using (var t = new SolidBrush(Theme.Good)) g.DrawString(now.Spare == 0 ? "frame perfect" : $"{now.Spare} to spare", Theme.Small, t, barX + barW + 6, y + 2);
                y += 22;
            }
            using (var pen = new Pen(Theme.Accent, 2)) g.DrawLine(pen, cur, top - 2, cur, Math.Max(top + 4, y - 4));
            using (var m = new SolidBrush(Color.FromArgb(150, Theme.Muted)))
                g.DrawString("Estimates from the game's numbers: the opponent stands right in front, no DI unless chosen, you don't move between moves (aerials: short or full hop with drift), aerials land L-cancelled.", Theme.Small, m, new RectangleF(x, _chart.Bottom - 20, w, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
        }
    }
}
