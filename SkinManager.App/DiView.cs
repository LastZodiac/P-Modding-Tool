// How to DI a move: the move plays, you drag your fighter (its head) onto it; the hitbox that would hit you there is
// picked out, and the page shows where it sends you and which way to hold the stick: to survive, to get out of a
// combo, and how to SDI (tap during the freeze when it lands). Kill percents from the middle of a Battlefield-sized stage.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class DiView : Control
    {
        FighterBook _me; Body _them; Character _themChar; MoveEntry _move; string _status;
        List<Bitmap> _pics; ModelPreview.MoveCamera _cam;
        readonly ComboBox _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        readonly TrackBar _pct = new TrackBar { Minimum = 0, Maximum = 200, TickFrequency = 25, Value = 80, Width = 190, AutoSize = false, Height = 28, BackColor = Theme.Back };
        readonly Label _pctLabel = Theme.Label("80%", Theme.BodyBold);
        public event Action<Character> TargetChanged;
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 16 };
        int _frame; float _acc; bool _dragging;
        PointF? _dot;              // where you are, in the move's picture (its own pixels)
        Rectangle _pic, _sticks, _map, _info;

        // what the spot gives
        (int Frame, Hitbox Hit)? _hit;
        bool _air, _behind;
        Dictionary<string, (int? None, int? Survive, int? Wrong)> _kills = new Dictionary<string, (int?, int?, int?)>();

        sealed class Item { public string Text; public object Value; public override string ToString() => Text; }

        public DiView()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            var l1 = Theme.Label("You play", Theme.Small, Theme.Muted); var l2 = Theme.Label("Your percent", Theme.Small, Theme.Muted);
            l1.AutoSize = l2.AutoSize = false; l1.Size = l2.Size = new Size(160, 16);
            l1.Tag = _target; l2.Tag = _pct;
            Controls.AddRange(new Control[] { l1, l2, _target, _pct, _pctLabel });
            _pct.ValueChanged += (s, e) => { _pctLabel.Text = _pct.Value + "%"; Invalidate(); };
            _target.SelectedIndexChanged += (s, e) => { if (_target.SelectedItem is Item it && it.Value is Character c) TargetChanged?.Invoke(c); };
            _timer.Tick += (s, e) =>
            {
                if (_pics == null || _pics.Count == 0 || !Visible) return;
                _acc += 0.5f * (_timer.Interval / (1000f / 60));   // half speed: easier to follow
                if (_acc < 1) return;
                int steps = (int)_acc; _acc -= steps;
                _frame = (_frame + steps) % _pics.Count;
                Invalidate(_pic);
            };
            _timer.Start();
            MouseDown += (s, e) => { if (_pic.Contains(e.Location) && _cam != null) { _dragging = true; Place(e.Location); } };
            MouseMove += (s, e) =>
            {
                if (_dragging) Place(e.Location);
                else Cursor = _pic.Contains(e.Location) && _cam != null ? Cursors.Hand : Cursors.Default;
            };
            MouseUp += (s, e) => _dragging = false;
            Resize += (s, e) => Arrange();
            Arrange();
        }

        void Arrange()
        {
            int w = Width;
            _target.Location = new Point(0, 16);
            _pct.Location = new Point(_target.Right + 12, 14); _pctLabel.Location = new Point(_pct.Right + 2, 18);
            foreach (Control c in Controls) if (c is Label l && l.Tag is Control tg) l.Location = new Point(tg.Left + 2, tg.Top - 16);
            int top = 54, stickH = 118, infoH = 64;
            int picH = Math.Max(150, Height - top - stickH - infoH - 24);
            int picW = Math.Min((int)(w * 0.55), (int)(picH * 520f / 400));
            picH = Math.Min(picH, (int)(picW * 400f / 520));
            _pic = new Rectangle(0, top, picW, picH);
            _map = new Rectangle(picW + 12, top, w - picW - 12, picH);
            _sticks = new Rectangle(0, _pic.Bottom + 12, w, stickH);
            _info = new Rectangle(0, _sticks.Bottom + 12, w, Math.Max(infoH, Height - _sticks.Bottom - 12));
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

        public void Show(FighterBook me, MoveEntry move, List<Bitmap> pics, ModelPreview.MoveCamera cam, Body them, Character themChar)
        {
            bool newMove = _move != move || _me != me || !ReferenceEquals(_cam, cam);
            _me = me; _move = move; _pics = pics; _cam = cam; _them = them; _themChar = themChar; _status = null;
            if (newMove) { _frame = 0; _dot = null; _kills.Clear(); }
            if (_dot == null && cam != null) _dot = StartSpot();
            Recalc();
        }

        /// <summary>To start: right on the move's first hitbox.</summary>
        PointF? StartSpot()
        {
            // standing in front, just inside the first hitbox, if it reaches someone standing; else right on it
            foreach (var f in _cam.Hits)
            {
                var h = f.Where(x => !x.Hit.Grab).OrderBy(x => x.Hit.Id).FirstOrDefault();
                if (h.Hit == null) continue;
                var stand = new PointF(h.X + h.R * 0.5f + BodyRadius * 0.5f, Ground - BodyHalf);
                return FromBody(h.X, h.Y, stand) <= h.R ? stand : new PointF(h.X + h.R * 0.4f, h.Y);
            }
            return null;
        }

        float Zoom => _pic.Width / (float)Math.Max(1, _cam?.Width ?? 520);
        PointF ToScreen(PointF p) => new PointF(_pic.X + p.X * Zoom, _pic.Y + p.Y * Zoom);
        // your body, in picture pixels: as wide and tall as you stand (a capsule); _dot is its middle
        float BodyRadius => _cam == null ? 10 : Math.Max(2.5f, (_them?.Width ?? 6) / 2f) * _cam.Scale;
        float BodyHalf => _cam == null ? 20 : Math.Max(4f, (_them?.Height ?? 12) * 0.85f / 2f) * _cam.Scale;   // middle to the top or bottom
        float Ground => _cam.ToPicture(0, 0).Y;

        /// <summary>How far a point is from your body (0 inside).</summary>
        float FromBody(float x, float y, PointF mid)
        {
            float inner = Math.Max(0, BodyHalf - BodyRadius);
            float cy = Math.Max(mid.Y - inner, Math.Min(mid.Y + inner, y));
            return Math.Max(0, Dist(x, cy, mid) - BodyRadius);
        }

        void Place(Point screen)
        {
            // the pointer holds your head: the body's middle is below it
            var p = new PointF((screen.X - _pic.X) / Zoom, (screen.Y - _pic.Y) / Zoom + BodyHalf - Math.Max(11 / Zoom, BodyRadius));
            p.X = Math.Max(0, Math.Min(_cam.Width, p.X)); p.Y = Math.Max(0, Math.Min(_cam.Height, p.Y));
            // standing: feet on the ground (it snaps there when close)
            float stand = Ground - BodyHalf;
            if (p.Y > stand - 3 * _cam.Scale) p.Y = stand;
            _dot = p; Recalc();
        }

        void Recalc()
        {
            _hit = null;
            if (_cam != null && _dot != null)
            {
                var d = _dot.Value; float br = BodyRadius;
                for (int i = 0; i < _cam.Hits.Count && _hit == null; i++)
                {
                    var on = _cam.Hits[i].Where(h => !h.Hit.Grab && FromBody(h.X, h.Y, d) <= h.R).OrderBy(h => h.Hit.Id).FirstOrDefault();
                    if (on.Hit != null) _hit = (i, on.Hit);
                }
                var g = _cam.ToGame(d.X, d.Y + BodyHalf);
                _air = g.Y > 1f;
                _behind = g.X < 0;
            }
            Invalidate();
        }

        static float Dist(float x, float y, PointF p) => (float)Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));

        (int? None, int? Survive, int? Wrong) Kills(Hitbox h)
        {
            string key = h.Id + "|" + h.Damage + "|" + h.Angle + "|" + h.Bkb + "|" + h.Kbg + "|" + h.Wkb + "|" + _air + "|" + _themChar?.Name;
            if (_kills.TryGetValue(key, out var k)) return k;
            var t = _them;
            k = (DiCoach.KillPercent(h, t, _air, (p, l) => null),
                 DiCoach.KillPercent(h, t, _air, (p, l) => DiCoach.SurvivalStick(h, t, _air, p)),
                 DiCoach.KillPercent(h, t, _air, (p, l) => DiCoach.StickFor(l, DiCoach.EscapeTarget(l))));
            _kills[key] = k;
            return k;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Color.FromArgb(200, 20, 22, 28)))
                foreach (var r in new[] { _pic, _sticks, _map, _info }) using (var p = StatsArt.Round(r, 10)) g.FillPath(bg, p);
            if (_status != null || _move == null) { Muted(g, _status ?? "Pick a move on the left.", _pic.X + 14, _pic.Y + 14); return; }
            DrawPicture(g);
            if (_cam == null) { Muted(g, "Drawing the move…", _sticks.X + 14, _sticks.Y + 14); return; }
            if (_hit == null)
            {
                Muted(g, "Drag your fighter onto the move:\nwherever a hitbox would hit you,\nthis shows how to DI it.", _sticks.X + 14, _sticks.Y + 14);
                Muted(g, _move.Data.Hits.Any(h => !h.Grab) ? "Not hit there." : "This move doesn't hit (nothing to DI).", _info.X + 14, _info.Y + 14);
                return;
            }
            var hit = _hit.Value.Hit;
            float kb = Combos.Knockback(hit, _pct.Value, _them.Weight);
            float launch = DiCoach.Angle(hit, kb, _air);
            float? survive = DiCoach.SurvivalStick(hit, _them, _air, _pct.Value);
            float? escape = DiCoach.StickFor(launch, DiCoach.EscapeTarget(launch));
            DrawSticks(g, hit, kb, launch, survive, escape);
            DrawMap(g, kb, launch, survive, escape);
            DrawInfo(g, hit, kb, launch);
        }

        static void Muted(Graphics g, string s, float x, float y) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString(s, Theme.Body, m, x, y); }

        // ----- the move, playing, with you on it

        void DrawPicture(Graphics g)
        {
            if (_pics == null || _pics.Count == 0 || _cam == null) { Muted(g, "Drawing the move…", _pic.X + 14, _pic.Y + 14); return; }
            int f = Math.Min(_frame, _pics.Count - 1);
            using (var path = StatsArt.Round(_pic, 10)) { g.SetClip(path); Theme.Blit(g, Theme.Scaled(_pics[f], _pic.Width, _pic.Height), _pic.X, _pic.Y); g.ResetClip(); }
            using (var t = new SolidBrush(Theme.Text)) g.DrawString($"{f + 1}", Theme.Bold(16f), t, _pic.X + 10, _pic.Y + 6);
            using (var m = new SolidBrush(Color.FromArgb(190, Theme.Text))) g.DrawString("drag your fighter onto the move", Theme.Small, m, _pic.X + 12, _pic.Bottom - 22);
            if (_dot == null) return;
            // the hitbox that gets you, ringed on the frames it's out
            if (_hit != null && f < _cam.Hits.Count)
                foreach (var h in _cam.Hits[f].Where(x => x.Hit.Id == _hit.Value.Hit.Id && !x.Hit.Grab))
                {
                    var c = ToScreen(new PointF(h.X, h.Y)); float r = h.R * Zoom + 3;
                    using (var pen = new Pen(Color.White, 2.5f)) g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
                }
            var d = ToScreen(_dot.Value);
            bool flash = _hit != null && f == _hit.Value.Frame;
            {
                float bw = BodyRadius * Zoom, bh = BodyHalf * Zoom;
                var capsule = new RectangleF(d.X - bw, d.Y - bh, 2 * bw, 2 * bh);
                using (var b = new SolidBrush(Color.FromArgb(_hit != null ? 70 : 45, _hit != null ? Theme.Bad : Theme.Good))) using (var path = StatsArt.Round(capsule, bw)) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(160, _hit != null ? Theme.Bad : Theme.Good), 1.5f) { DashStyle = DashStyle.Dash }) using (var path = StatsArt.Round(capsule, bw)) g.DrawPath(pen, path);
                d = new PointF(d.X, d.Y - bh + Math.Max(11, bw));   // the head at the top
            }
            float br = Math.Max(11, BodyRadius * Zoom);
            using (var b = new SolidBrush(Color.FromArgb(200, 20, 22, 28))) g.FillEllipse(b, d.X - br, d.Y - br, 2 * br, 2 * br);
            var img = HeadOf(_themChar);
            if (img != null)
            {
                var state = g.Save();
                using (var clip = new GraphicsPath()) { clip.AddEllipse(d.X - br, d.Y - br, 2 * br, 2 * br); g.SetClip(clip, CombineMode.Intersect); }
                StatsArt.Cover(g, img, new RectangleF(d.X - br, d.Y - br, 2 * br, 2 * br), 0, false, 0.5f, 0.2f);
                g.Restore(state);
            }
            using (var pen = new Pen(flash ? Color.White : _hit != null ? Theme.Bad : Theme.Good, flash ? 4 : 2.5f)) g.DrawEllipse(pen, d.X - br, d.Y - br, 2 * br, 2 * br);
            if (_hit != null)
            {
                // which way it sends you from here (no DI), as an arrow from you
                float kb = Combos.Knockback(_hit.Value.Hit, _pct.Value, _them.Weight);
                float a = DiCoach.Angle(_hit.Value.Hit, kb, _air);
                Arrow(g, ToScreen(_dot.Value), Screen(a), 26 + Math.Min(40, kb / 5), Color.FromArgb(230, 255, 255, 255), 3);
            }
        }

        /// <summary>A launch angle (forward = away from the attacker) as an angle on screen (the attacker faces right).</summary>
        float Screen(float a) => _behind ? 180 - a : a;

        static void Arrow(Graphics g, PointF from, float angle, float len, Color c, float width)
        {
            double rad = angle * Math.PI / 180;
            var to = new PointF(from.X + (float)Math.Cos(rad) * len, from.Y - (float)Math.Sin(rad) * len);
            using (var pen = new Pen(c, width) { EndCap = LineCap.ArrowAnchor, StartCap = LineCap.Round }) g.DrawLine(pen, from, to);
        }

        static Dictionary<string, int> _ids;
        static Image HeadOf(Character c)
        {
            if (c == null) return null;
            if (_ids == null) _ids = Enumerable.Range(0, 60).GroupBy(Fighters.Name).ToDictionary(x => x.Key, x => x.Min());
            return _ids.TryGetValue(c.Name, out int id) ? StatsArt.Cached?.Head(Fighters.Same(id)) : null;
        }

        // ----- the sticks

        static readonly Color SurviveColor = Color.FromArgb(92, 201, 120), EscapeColor = Color.FromArgb(96, 182, 255), SdiColor = Color.FromArgb(255, 176, 46);

        void DrawSticks(Graphics g, Hitbox hit, float kb, float launch, float? survive, float? escape)
        {
            int x = _sticks.X + 12, w = (_sticks.Width - 24 - 24) / 3;
            int y = _sticks.Y + 10, rowH = _sticks.Height - 20;
            bool tumble = kb >= 80;
            // to survive
            Stick(g, new Rectangle(x, y, w, rowH), survive, SurviveColor, "To survive",
                survive == null ? "No direction helps here" : "Hold " + DiCoach.Words(survive),
                (survive == null ? "" : $"sent at {launch:0}° → {DiCoach.WithDi(launch, survive):0}°") + (tumble ? "" : " · not a kill hit at this %"), false);
            x += w + 12;
            Stick(g, new Rectangle(x, y, w, rowH), escape, EscapeColor, "To get out of combos",
                escape == null ? "Already sends you away" : "Hold " + DiCoach.Words(escape),
                escape == null ? null : $"sent at {launch:0}° → {DiCoach.WithDi(launch, escape):0}°: out of their reach", false);
            x += w + 12;
            // SDI
            int lag = DiCoach.Hitlag(hit);
            bool multi = DiCoach.MultiHit(_move.Data);
            float? sdi; string line, small;
            if (multi) { sdi = 0; line = "Tap away, again and again"; small = $"it hits many times: each tap in the {lag}-frame freeze moves you ~{DiCoach.SdiStep(hit):0} units out"; }
            else if (!tumble && !_air && launch > 0 && launch < 180) { sdi = 270; line = "Hold down (ASDI down)"; small = "light hit on the ground: hold down as it lands to stay grounded and act sooner"; }
            else { sdi = 180; line = "Tap in, towards the stage"; small = $"{lag}-frame freeze: each tap moves you ~{DiCoach.SdiStep(hit):0} units first"; }
            Stick(g, new Rectangle(x, y, w, rowH), sdi, SdiColor, "SDI (in the freeze)", line, small, true);
        }

        void Stick(Graphics g, Rectangle r, float? dir, Color c, string title, string line, string small, bool taps)
        {
            using (var bg = new SolidBrush(Color.FromArgb(30, c))) using (var p = StatsArt.Round(r, 8)) g.FillPath(bg, p);
            using (var m = new SolidBrush(c)) g.DrawString(title.ToUpperInvariant(), Theme.Small, m, new RectangleF(r.X + 10, r.Y + 7, r.Width - 20, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            int size = (int)Math.Min(r.Height - 36, Math.Min(r.Width * 0.3f, 76));
            var gate = new RectangleF(r.X + 8, r.Y + 28 + (r.Height - 32 - size) / 2f, size, size);
            var center = new PointF(gate.X + size / 2f, gate.Y + size / 2f);
            float rad = size / 2f - 4;
            // the gate (octagon) and the stick
            var oct = Enumerable.Range(0, 8).Select(i => new PointF(center.X + (float)Math.Cos(i * Math.PI / 4 + Math.PI / 8) * rad, center.Y - (float)Math.Sin(i * Math.PI / 4 + Math.PI / 8) * rad)).ToArray();
            using (var b = new SolidBrush(Color.FromArgb(160, 12, 13, 18))) g.FillPolygon(b, oct);
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f)) g.DrawPolygon(pen, oct);
            var knob = center;
            if (dir != null)
            {
                float a = Screen(dir.Value);
                double ar = a * Math.PI / 180;
                knob = new PointF(center.X + (float)Math.Cos(ar) * rad * 0.62f, center.Y - (float)Math.Sin(ar) * rad * 0.62f);
                using (var pen = new Pen(Color.FromArgb(200, c), 3) { EndCap = LineCap.ArrowAnchor }) g.DrawLine(pen, center, new PointF(center.X + (float)Math.Cos(ar) * rad * 0.95f, center.Y - (float)Math.Sin(ar) * rad * 0.95f));
                if (taps) for (int k = 1; k <= 2; k++) { float kr = rad * (0.62f + 0.18f * k); using (var pen = new Pen(Color.FromArgb(150 - 50 * k, c), 2)) g.DrawArc(pen, center.X - kr, center.Y - kr, 2 * kr, 2 * kr, -a - 20, 40); }
            }
            float kr2 = size * 0.17f;
            using (var b = new SolidBrush(dir != null ? c : Color.FromArgb(150, 150, 156, 172))) g.FillEllipse(b, knob.X - kr2, knob.Y - kr2, 2 * kr2, 2 * kr2);
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawEllipse(pen, knob.X - kr2, knob.Y - kr2, 2 * kr2, 2 * kr2);
            float tx = gate.Right + 12, tw = r.Right - tx;
            var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
            tw -= 8;
            using (var t = new SolidBrush(Theme.Text)) g.DrawString(line, Theme.BodyBold, t, new RectangleF(tx, r.Y + 28, tw, 40), clip);
            float lh = Math.Min(40, g.MeasureString(line, Theme.BodyBold, (int)tw).Height);
            if (small != null) using (var m = new SolidBrush(Theme.Muted)) g.DrawString(small, Theme.Small, m, new RectangleF(tx, r.Y + 30 + lh, tw, Math.Max(18, r.Bottom - (r.Y + 30 + lh) - 4)), clip);
        }

        // ----- the flights, from the middle of a stage

        void DrawMap(Graphics g, float kb, float launch, float? survive, float? escape)
        {
            using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"WHERE IT SENDS YOU AT {_pct.Value}% (FROM MID-STAGE)", Theme.Small, m, new RectangleF(_map.X + 12, _map.Y + 8, _map.Width - 24, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            var area = new RectangleF(_map.X + 14, _map.Y + 30, _map.Width - 28, _map.Height - 60);
            float sx = area.Width / (2 * DiCoach.Side), sy = area.Height / (DiCoach.Top - DiCoach.Bottom);
            float s = Math.Min(sx, sy);
            float ox = area.X + area.Width / 2, oy = area.Y + (area.Height - (DiCoach.Top - DiCoach.Bottom) * s) / 2 + DiCoach.Top * s;
            PointF P(float x, float y) => new PointF(ox + (_behind ? -x : x) * s, oy - y * s);
            // blast zones and the stage
            var zone = RectangleF.FromLTRB(ox - DiCoach.Side * s, oy - DiCoach.Top * s, ox + DiCoach.Side * s, oy - DiCoach.Bottom * s);
            using (var pen = new Pen(Color.FromArgb(150, Theme.Bad), 1.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, zone.X, zone.Y, zone.Width, zone.Height);
            using (var b = new SolidBrush(Color.FromArgb(120, 150, 156, 172)))
            {
                var a = P(-DiCoach.Edge, 0); var c = P(DiCoach.Edge, 0);
                g.FillRectangle(b, Math.Min(a.X, c.X), a.Y, Math.Abs(c.X - a.X), 6);
            }
            void Path(float angle, Color c, string label, int row)
            {
                var path = DiCoach.Fly(kb, angle, _them, 0, _air ? 20 : 0, out bool dies);
                var pts = path.Select(p => P(p.X, p.Y)).ToArray();
                if (pts.Length >= 2) using (var pen = new Pen(c, 2.2f)) g.DrawLines(pen, pts);
                var end = pts[pts.Length - 1];
                if (dies) using (var pen = new Pen(c, 2.5f)) { g.DrawLine(pen, end.X - 5, end.Y - 5, end.X + 5, end.Y + 5); g.DrawLine(pen, end.X - 5, end.Y + 5, end.X + 5, end.Y - 5); }
                using (var b = new SolidBrush(c)) g.FillRectangle(b, _map.X + 14 + row * ((_map.Width - 28) / 3), _map.Bottom - 20, 10, 4);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(label + (dies ? " · dies" : ""), Theme.Small, t, _map.X + 28 + row * ((_map.Width - 28) / 3), _map.Bottom - 27);
            }
            Path(launch, Color.FromArgb(230, 255, 255, 255), "no DI", 0);
            if (escape != null) Path(DiCoach.WithDi(launch, escape), EscapeColor, "combo", 1);
            if (survive != null) Path(DiCoach.WithDi(launch, survive), SurviveColor, "survive", 2);
            var start = P(0, _air ? 20 : 0);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, start.X - 4, start.Y - 8, 8, 8);
        }

        // ----- the numbers

        void DrawInfo(Graphics g, Hitbox hit, float kb, float launch)
        {
            int x = _info.X + 14, y = _info.Y + 9, w = _info.Width - 28;
            var col = ModelPreview.HitColors[hit.Id % ModelPreview.HitColors.Length];
            var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            float cx = x;
            void Bit(string s, Color c, Font f = null) { f = f ?? Theme.BodyBold; using (var b = new SolidBrush(c)) g.DrawString(s, f, b, cx, y); cx += g.MeasureString(s, f).Width + 4; }
            Bit($"Hitbox #{hit.Id}", col); Bit($"frame {_hit.Value.Frame + 1} · {hit.DamageText} · sent at {launch:0}°" + (hit.Angle == 361 ? " (361)" : "") + (_behind ? " backwards" : ""), Theme.Text, Theme.Body);
            Bit($"· knockback {kb:0}{(kb >= 80 ? " (tumble)" : "")} · hitstun {(int)(kb * 0.4f)} · freeze {DiCoach.Hitlag(hit)} · {(_air ? "in the air" : "standing")}", Theme.Muted, Theme.Body);
            y += 24; cx = x;
            var k = Kills(hit);
            string P(int? p) => p == null ? "300%+" : p + "%";
            Bit("Kills mid-stage at:", Theme.Muted, Theme.Body);
            Bit($"no DI {P(k.None)}", Theme.Text); Bit("·", Theme.Muted); Bit($"survival DI {P(k.Survive)}", SurviveColor); Bit("·", Theme.Muted); Bit($"wrong DI {P(k.Wrong)}", Theme.Bad);
            using (var m = new SolidBrush(Color.FromArgb(150, Theme.Muted)))
                g.DrawString("Estimates (Melee rules, which P+ follows): DI turns a launch up to 18°; Battlefield-sized blast zones.", Theme.Small, m, new RectangleF(cx + 8, y + 2, Math.Max(10, x + w - cx - 8), 18), clip);
        }
    }
}
