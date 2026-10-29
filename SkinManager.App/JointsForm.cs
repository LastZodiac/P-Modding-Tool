// "Line up the joints": your model from the front with a dot on each of P+'s joints. Drag each dot onto the same joint
// on your model (knee onto its knee...). The model is then bent and stretched so those spots land on P+'s joints and it
// bends where P+ bends. P+'s skeleton itself never moves, so hits stay the same for both players.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using BrawlLib.Internal;
using SkinManager.Core;

namespace SkinManager.App
{
    class JointsForm : Form
    {
        sealed class Canvas : Panel { public Canvas() { DoubleBuffered = true; ResizeRedraw = true; } }

        readonly SwapMesh _placed;
        readonly List<SwapJoint> _joints;
        readonly byte[] _target, _motion;
        readonly string _name;
        public Dictionary<string, Vector3> Marks { get; }
        public List<PaintDab> Dabs { get; }
        readonly Dictionary<string, Vector3> _auto;   // where measuring the model put each dot
        enum Tool { Dots, Paint, Erase }
        Tool _tool = Tool.Dots;
        readonly Button _toolDots = Theme.Secondary("Move dots"), _toolPaint = Theme.Secondary("Paint: stays with body"), _toolErase = Theme.Secondary("Erase paint");
        readonly TrackBar _brush = new TrackBar { Minimum = 6, Maximum = 50, Value = 20, TickFrequency = 11, Width = 200, BackColor = Theme.Back };
        Bitmap _paint;
        Point? _mouse;
        PointF _lastDab;

        readonly Canvas _canvas = new Canvas { Size = new Size(440, 600), BackColor = Color.FromArgb(20, 22, 28) };
        readonly PictureBox _preview = new PictureBox { Size = new Size(256, 320), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20, 22, 28) };
        readonly ComboBox _pose = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        readonly CheckBox _mirror = new CheckBox { Text = "Move left and right together", Checked = true, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
        readonly Label _status = Theme.Label("", Theme.Small, Theme.Muted);
        Bitmap _picture;
        float _minX, _maxY, _scale;
        string _drag, _hover;
        int _gen;
        readonly Timer _later = new Timer { Interval = 400 };

        public JointsForm(SwapMesh placed, List<SwapJoint> joints, IDictionary<string, Vector3> marks, IDictionary<string, Vector3> auto, IList<PaintDab> dabs, byte[] target, byte[] motion, string name)
        {
            _placed = placed; _joints = joints; _target = target; _motion = motion; _name = name;
            Dabs = new List<PaintDab>(dabs ?? new List<PaintDab>());
            _auto = new Dictionary<string, Vector3>(auto ?? new Dictionary<string, Vector3>(), StringComparer.Ordinal);
            Marks = new Dictionary<string, Vector3>(marks ?? new Dictionary<string, Vector3>(), StringComparer.Ordinal);
            Text = "Line up the joints";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(900, 760);

            var title = Theme.Label("Line up the joints", Theme.Title); title.Location = new Point(22, 12);
            var how = Theme.Label("The blue dots were placed by measuring your model (depth too). Check them: each should sit on the same joint of your model,\r\n" +
                "knee dot on its knee and so on. Drag any that are off (the middle ones only slide up and down); right-click puts one back.\r\n" +
                "Hair, coat tails or a skirt flying off with an arm or a leg? Pick \"Paint\" and paint over them: painted parts move only with the body.", Theme.Body, Theme.Muted);
            how.AutoSize = false; how.Location = new Point(24, 46); how.Size = new Size(870, 54);
            _canvas.Location = new Point(24, 106);
            _mirror.Location = new Point(24, 712);

            var lp = Theme.Label("Your model in", Theme.Body, Theme.Muted); lp.Location = new Point(500, 104);
            _pose.Location = new Point(600, 100);
            foreach (var p in SwapPanel.Poses) _pose.Items.Add(p.Name);
            _pose.SelectedIndex = Math.Min(2, _pose.Items.Count - 1);   // running: knees and elbows bend
            _preview.Location = new Point(500, 140);
            _status.AutoSize = false; _status.Location = new Point(500, 470); _status.Size = new Size(380, 90);

            var tl = Theme.Label("What the mouse does:", Theme.Body, Theme.Muted); tl.Location = new Point(500, 540);
            _toolDots.Location = new Point(500, 564); _toolDots.Width = 100;
            _toolPaint.Location = new Point(606, 564); _toolPaint.Width = 170;
            _toolErase.Location = new Point(782, 564); _toolErase.Width = 100;
            var bl = Theme.Label("Brush size", Theme.Small, Theme.Muted); bl.Location = new Point(500, 612);
            _brush.Location = new Point(570, 604);
            var reset = Theme.Secondary("Place the dots for me"); reset.Location = new Point(500, 652); reset.Width = 160;
            var clear = Theme.Secondary("Clear the paint"); clear.Location = new Point(666, 652); clear.Width = 150;
            _status.Size = new Size(380, 64);
            var ok = Theme.Primary("Use these"); ok.Location = new Point(680, 700); ok.Width = 110;
            var cancel = Theme.Secondary("Cancel"); cancel.Location = new Point(796, 700); cancel.Width = 90;
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { title, how, _canvas, _mirror, lp, _pose, _preview, _status, tl, _toolDots, _toolPaint, _toolErase, bl, _brush, reset, clear, ok, cancel });
            _toolDots.Click += (s, e) => SetTool(Tool.Dots);
            _toolPaint.Click += (s, e) => SetTool(Tool.Paint);
            _toolErase.Click += (s, e) => SetTool(Tool.Erase);
            clear.Click += (s, e) => { Dabs.Clear(); MakePaint(); _canvas.Invalidate(); Later(); };
            _canvas.MouseLeave += (s, e) => { _mouse = null; _canvas.Invalidate(); };
            SetTool(Tool.Dots);

            MakePicture();
            MakePaint();
            _canvas.Paint += (s, e) => PaintCanvas(e.Graphics);
            _canvas.MouseDown += Down;
            _canvas.MouseMove += Move2;
            _canvas.MouseUp += (s, e) => { if (_drag != null) { _drag = null; _canvas.Invalidate(); Later(); } else if (_tool != Tool.Dots && e.Button == MouseButtons.Left) Later(); };
            _pose.SelectedIndexChanged += (s, e) => Later();
            reset.Click += (s, e) => { Marks.Clear(); foreach (var kv in _auto) Marks[kv.Key] = kv.Value; _canvas.Invalidate(); Later(); };
            _later.Tick += (s, e) => { _later.Stop(); UpdatePreview(); };
            Shown += (s, e) => UpdatePreview();
            FormClosed += (s, e) => { _later.Dispose(); _picture?.Dispose(); _paint?.Dispose(); _preview.Image?.Dispose(); };
        }

        void Later() { _later.Stop(); _later.Start(); }

        /// <summary>Your model from the front, framed so the whole skeleton and model fit.</summary>
        void MakePicture()
        {
            var box = _placed.Box();
            float x0 = box.Min._x, x1 = box.Max._x, y0 = box.Min._y, y1 = box.Max._y;
            foreach (var j in _joints) { x0 = Math.Min(x0, j.Pos._x); x1 = Math.Max(x1, j.Pos._x); y0 = Math.Min(y0, j.Pos._y); y1 = Math.Max(y1, j.Pos._y); }
            float w = (x1 - x0) * 1.08f, h = (y1 - y0) * 1.06f, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            float aspect = (float)_canvas.Width / _canvas.Height;
            if (w / h > aspect) h = w / aspect; else w = h * aspect;
            _minX = cx - w / 2; _maxY = cy + h / 2; _scale = _canvas.Width / w;
            try { _picture = ModelPreview.RenderMesh(_placed, _canvas.Width, _canvas.Height, cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2); }
            catch (Exception e) { Program.Log("drawing the model for lining up", e); }
        }

        void SetTool(Tool t)
        {
            _tool = t;
            Theme.Toggle(_toolDots, t == Tool.Dots); Theme.Toggle(_toolPaint, t == Tool.Paint); Theme.Toggle(_toolErase, t == Tool.Erase);
            _canvas.Cursor = t == Tool.Dots ? Cursors.Default : Cursors.Cross;
            _canvas.Invalidate();
        }

        /// <summary>The paint as a see-through layer over the model (pink: moves only with the body).</summary>
        void MakePaint()
        {
            _paint?.Dispose();
            _paint = new Bitmap(_canvas.Width, _canvas.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(_paint))
            using (var pink = new SolidBrush(Color.FromArgb(120, 255, 70, 200)))
            using (var clear = new SolidBrush(Color.Transparent))
            {
                g.CompositingMode = CompositingMode.SourceCopy; g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var d in Dabs) Dab(g, d, d.Erase ? clear : pink);
            }
        }

        void Dab(Graphics g, PaintDab d, Brush b)
        {
            var c = Screen(new Vector3(d.X, d.Y, 0)); float r = d.R * _scale;
            g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
        }

        void AddDab(Point p, bool force)
        {
            if (!force && Math.Abs(p.X - _lastDab.X) + Math.Abs(p.Y - _lastDab.Y) < _brush.Value * 0.3f) return;
            _lastDab = p;
            var d = new PaintDab { X = p.X / _scale + _minX, Y = _maxY - p.Y / _scale, R = _brush.Value / _scale, Erase = _tool == Tool.Erase };
            Dabs.Add(d);
            using (var g = Graphics.FromImage(_paint))
            using (var b = new SolidBrush(d.Erase ? Color.Transparent : Color.FromArgb(120, 255, 70, 200)))
            { g.CompositingMode = CompositingMode.SourceCopy; g.SmoothingMode = SmoothingMode.AntiAlias; Dab(g, d, b); }
        }

        PointF Screen(Vector3 v) => new PointF((v._x - _minX) * _scale, (_maxY - v._y) * _scale);
        Vector3 Where(SwapJoint j) => Marks.TryGetValue(j.Name, out var m) ? m : j.Pos;

        void PaintCanvas(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_picture != null) g.DrawImage(_picture, 0, 0, _canvas.Width, _canvas.Height);
            if (_paint != null) g.DrawImage(_paint, 0, 0, _canvas.Width, _canvas.Height);
            var at = _joints.ToDictionary(j => j.Name, j => j);
            using (var bone = new Pen(Color.FromArgb(200, Theme.Accent), 2.5f))
                foreach (var j in _joints)
                    if (j.To != null && at.ContainsKey(j.To)) g.DrawLine(bone, Screen(j.Pos), Screen(at[j.To].Pos));
            using (var gold = new SolidBrush(Theme.Accent))
            using (var moved = new Pen(Color.FromArgb(220, 120, 200, 255), 1.5f) { DashStyle = DashStyle.Dash })
            using (var blue = new SolidBrush(Color.FromArgb(70, 170, 255)))
            using (var hot = new SolidBrush(Color.FromArgb(150, 230, 255)))
            using (var edge = new Pen(Color.FromArgb(15, 20, 30), 1.5f))
                foreach (var j in _joints.Where(j => j.Handle))
                {
                    var p = Screen(j.Pos); var m = Screen(Where(j));
                    g.FillEllipse(gold, p.X - 3, p.Y - 3, 6, 6);
                    if (Marks.ContainsKey(j.Name)) g.DrawLine(moved, p, m);
                    float r = j.Name == _hover || j.Name == _drag ? 7 : 5.5f;
                    g.FillEllipse(j.Name == _hover || j.Name == _drag ? hot : blue, m.X - r, m.Y - r, 2 * r, 2 * r);
                    g.DrawEllipse(edge, m.X - r, m.Y - r, 2 * r, 2 * r);
                }
            if (_tool != Tool.Dots && _mouse is Point mp)
                using (var ring = new Pen(_tool == Tool.Erase ? Color.White : Color.FromArgb(255, 120, 220), 1.5f))
                    g.DrawEllipse(ring, mp.X - _brush.Value, mp.Y - _brush.Value, 2 * _brush.Value, 2 * _brush.Value);
            string tip = _tool == Tool.Dots ? _drag ?? _hover : null;
            var tj = _joints.FirstOrDefault(j => j.Name == tip);
            if (tj != null)
            {
                var m = Screen(Where(tj));
                var size = g.MeasureString(tj.Label, Theme.BodyBold);
                var r = new RectangleF(Math.Min(_canvas.Width - size.Width - 8, m.X + 12), Math.Max(2, m.Y - size.Height - 6), size.Width + 8, size.Height + 4);
                using (var back = new SolidBrush(Color.FromArgb(230, 15, 18, 26))) g.FillRectangle(back, r);
                using (var fore = new SolidBrush(Theme.Text)) g.DrawString(tj.Label, Theme.BodyBold, fore, r.X + 4, r.Y + 2);
            }
        }

        string Hit(Point p)
        {
            string best = null; double bd = 12 * 12;
            foreach (var j in _joints.Where(j => j.Handle))
            {
                var m = Screen(Where(j));
                double d = (m.X - p.X) * (m.X - p.X) + (m.Y - p.Y) * (m.Y - p.Y);
                if (d <= bd) { bd = d; best = j.Name; }
            }
            return best;
        }

        void Down(object s, MouseEventArgs e)
        {
            if (_tool != Tool.Dots) { if (e.Button == MouseButtons.Left) { AddDab(e.Location, true); _canvas.Invalidate(); } return; }
            string hit = Hit(e.Location);
            if (hit == null) return;
            if (e.Button == MouseButtons.Right)
            {
                var j = _joints.First(x => x.Name == hit);
                foreach (var n in _mirror.Checked && j.Mirror != null ? new[] { hit, j.Mirror } : new[] { hit })
                    if (_auto.TryGetValue(n, out var a)) Marks[n] = a; else Marks.Remove(n);
                _canvas.Invalidate(); Later();
                return;
            }
            _drag = hit;
        }

        static bool ModelSwapSided(string n) => n.Length > 1 && (n[0] == 'L' || n[0] == 'R') && char.IsUpper(n[1]);

        void Move2(object s, MouseEventArgs e)
        {
            if (_tool != Tool.Dots)
            {
                _mouse = e.Location;
                if (e.Button == MouseButtons.Left) AddDab(e.Location, false);
                _canvas.Invalidate();
                return;
            }
            if (_drag == null)
            {
                string h = Hit(e.Location);
                if (h != _hover) { _hover = h; _canvas.Cursor = h != null ? Cursors.Hand : Cursors.Default; _canvas.Invalidate(); }
                return;
            }
            var j = _joints.First(x => x.Name == _drag);
            float x = Math.Max(0, Math.Min(_canvas.Width, e.X)) / _scale + _minX, y = _maxY - Math.Max(0, Math.Min(_canvas.Height, e.Y)) / _scale;
            if (!ModelSwapSided(j.Name)) x = Marks.TryGetValue(j.Name, out var cm) ? cm._x : _auto.TryGetValue(j.Name, out var ca) ? ca._x : j.Pos._x;   // the middle line only moves up and down: a spine can't wander sideways
            float z = Marks.TryGetValue(j.Name, out var was) ? was._z : _auto.TryGetValue(j.Name, out var au) ? au._z : j.Pos._z;   // depth can't be seen from the front: it stays where it was measured
            Marks[j.Name] = new Vector3(x, y, z);
            if (_mirror.Checked && j.Mirror != null)
            {
                var o = _joints.First(k => k.Name == j.Mirror);
                // the same move, mirrored: further out on one side is further out on the other
                float oz = Marks.TryGetValue(o.Name, out var ow) ? ow._z : _auto.TryGetValue(o.Name, out var oa) ? oa._z : o.Pos._z;
                Marks[o.Name] = new Vector3(o.Pos._x - (x - j.Pos._x), y - j.Pos._y + o.Pos._y, oz);
            }
            _canvas.Invalidate();
        }

        bool _busy, _again;

        /// <summary>Rebuilds the preview, one at a time: a change made meanwhile is shown when the current one is done.</summary>
        async void UpdatePreview()
        {
            if (_busy) { _again = true; return; }
            _busy = true; _again = false;
            int gen = ++_gen;
            var marks = new Dictionary<string, Vector3>(Marks);
            var dabs = new List<PaintDab>(Dabs);
            var pose = SwapPanel.Poses[Math.Max(0, _pose.SelectedIndex)];
            _status.Text = "Updating the preview..."; _status.ForeColor = Theme.Muted;
            try
            {
                var img = await SwapPanel.Sta(() =>
                {
                    var lined = ModelSwap.LineUp(_placed, _joints, marks);
                    var keep = ModelSwap.KeepTest(_placed, lined, dabs);
                    var built = ModelSwap.Build(lined, _target, _name, keep, ModelSwap.SightWeights(lined, _joints, keep));
                    return pose.Anim != null && _motion != null ? ModelPreview.RenderPosed(built.Costume, _motion, pose.Anim, pose.Frame) : ModelPreview.Render(built.Costume);
                });
                if (IsDisposed) { img?.Dispose(); return; }
                var old = _preview.Image; _preview.Image = img; old?.Dispose();
                int n = marks.Count;
                _status.Text = (dabs.Any(d => !d.Erase) ? "Painted parts move only with the body. " : "") +
                    "\r\nLook at a few poses: knees and elbows should bend at the joint, and nothing should fly off with an arm or a leg.";
                _status.ForeColor = Theme.Text;
            }
            catch (Exception e)
            {
                if (IsDisposed) return;
                _status.Text = "The preview couldn't be made: " + e.Message; _status.ForeColor = Theme.Bad;
            }
            finally
            {
                _busy = false;
                if (_again && !IsDisposed) UpdatePreview();
            }
        }
    }
}
