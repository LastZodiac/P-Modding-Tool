// Frame Data: every fighter's moves, like a frame data site. Pick a fighter, pick a move: it plays frame by frame with
// its hitboxes (coloured circles) and hurtboxes (yellow; blue while intangible), with startup, active frames, when you
// can act again, landing lag and each hitbox's numbers. Read from the game's own files on the card.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class FrameDataPanel : GlassPanel
    {
        readonly Func<Installer> _installer;
        List<Character> _cast;
        FighterBook _book;
        MoveEntry _move;
        readonly Dictionary<string, FighterBook> _books = new Dictionary<string, FighterBook>();
        readonly Dictionary<string, List<Bitmap>> _frames = new Dictionary<string, List<Bitmap>>();   // drawn moves (last few)
        readonly List<string> _frameOrder = new List<string>();
        CancellationTokenSource _drawing;
        /// <summary>A fighter and a move are on show (for the page's tour).</summary>
        public bool Ready => _book != null && _view.Pictures != null;

        readonly Label _title = Theme.Label("FRAME DATA", Theme.Small, Theme.Muted);
        readonly Label _who = Theme.Label("Pick a fighter", Theme.Title);
        readonly Label _note = Theme.Label("", Theme.Small, Theme.Muted);
        readonly CastStrip _strip = new CastStrip();
        readonly MoveList _list = new MoveList();
        readonly MoveView _view = new MoveView();
        readonly ComboView _combo = new ComboView { Visible = false };
        readonly DiView _di = new DiView { Visible = false };
        readonly Button _tabMoves = Theme.Secondary("Moves && hitboxes"), _tabCombos = Theme.Secondary("Launch && combos"), _tabDi = Theme.Secondary("How to DI"), _tabSure = Theme.Secondary("How accurate");
        readonly AccuracyView _sure = new AccuracyView { Visible = false };
        readonly Dictionary<string, ModelPreview.MoveCamera> _cams = new Dictionary<string, ModelPreview.MoveCamera>();
        readonly FlowLayoutPanel _chips = new FlowLayoutPanel { BackColor = Color.Transparent, WrapContents = false, Height = 30 };
        string _filter = "All";
        Character _targetChar, _diChar;

        public FrameDataPanel(Func<Installer> installer)
        {
            _installer = installer;
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8);
            Controls.AddRange(new Control[] { _title, _who, _note, _strip, _chips, _list, _view, _combo, _di, _sure, _tabMoves, _tabCombos, _tabDi, _tabSure });
            _tabMoves.Click += (s, e) => ShowTab(0);
            _tabCombos.Click += (s, e) => ShowTab(1);
            _tabDi.Click += (s, e) => ShowTab(2);
            _tabSure.Click += (s, e) => ShowTab(3);
            new ToolTip().SetToolTip(_tabSure, "Which numbers are read straight from P+'s files, which needed an assumption, and what isn't drawn");
            _sure.WholeCastRequested += CheckWholeCast;
            _sure.FighterPicked += c => ShowFighter(c);
            Theme.Toggle(_tabMoves, true); Theme.Toggle(_tabCombos, false); Theme.Toggle(_tabDi, false); Theme.Toggle(_tabSure, false);
            foreach (var t in FrameDataBook.Tabs)
            {
                var b = Theme.Secondary(t); b.Margin = new Padding(0, 0, 5, 0); b.Font = Theme.Small; b.Padding = new Padding(4, 1, 4, 1); b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.MinimumSize = new Size(0, 26);
                Theme.Toggle(b, t == _filter); b.Font = Theme.Small;
                b.Click += (s, e) => { _filter = t; foreach (Button x in _chips.Controls) { Theme.Toggle(x, x.Text == t); x.Font = Theme.Small; } _list.Filter = t; _list.Show(_book, null); };
                _chips.Controls.Add(b);
            }
            _combo.TargetChanged += c => { _targetChar = c; ShowCombos(); };
            _di.TargetChanged += c => { _diChar = c; ShowDi(); };
            _strip.Picked += c => ShowFighter(c);
            _list.Picked += m => ShowMove(m);
            _view.SaveGif += SaveGif;
            _view.HurtboxesChanged += () => { ClearFrames(); if (_move != null) ShowMove(_move); };
            Resize += (s, e) => Arrange();
            VisibleChanged += (s, e) => { if (Visible && _cast == null) Start(); };
            _note.Click += (s, e) => { if (_cast == null) Start(); };
            Arrange();
        }

        void Arrange()
        {
            int w = Math.Max(760, ClientSize.Width - 44), h = ClientSize.Height;
            _title.Location = new Point(28, 14);
            _who.Location = new Point(28, 32); _who.AutoSize = true;
            _note.Location = new Point(30, 64); _note.Size = new Size(w, 18);
            _strip.Location = new Point(28, 86); _strip.Width = w; _strip.Height = _strip.Needed(w);
            _tabSure.Location = new Point(28 + w - _tabSure.PreferredSize.Width, 24);
            _tabDi.Location = new Point(_tabSure.Left - 8 - _tabDi.PreferredSize.Width, 24);
            _tabCombos.Location = new Point(_tabDi.Left - 8 - _tabCombos.PreferredSize.Width, 24);
            _tabMoves.Location = new Point(_tabCombos.Left - 8 - _tabMoves.PreferredSize.Width, 24);
            int top = _strip.Bottom + 10;
            int listW = Math.Max(340, (int)(w * 0.40));
            _chips.Location = new Point(28, top); _chips.Size = new Size(listW, 28);
            _list.Location = new Point(28, top + 32); _list.Size = new Size(listW, Math.Max(200, h - top - 44));
            _view.Location = new Point(28 + listW + 14, top); _view.Size = new Size(w - listW - 14, Math.Max(200, h - top - 12));
            _combo.Bounds = _view.Bounds; _di.Bounds = _view.Bounds; _sure.Bounds = _view.Bounds;
        }

        bool _starting;
        async void Start()
        {
            if (_starting) return;
            _starting = true;
            _note.Text = "Reading the fighters from your P+ card…"; _note.ForeColor = Theme.Muted; _note.Cursor = Cursors.Default;
            try
            {
                var inst = _installer();
                if (inst == null) { Failed("Brawl Online isn't set up on this PC yet, so there's no P+ SD card to read the fighters from. Open Brawl Online once, then click here."); return; }
                var cast = await Task.Run(() => { using (var card = inst.OpenCard()) return FrameDataBook.Available(card); });
                var art = await Task.Run(() => StatsArt.Get(_installer));
                if (cast.Count == 0) { Failed("No fighters were found on your P+ SD card. Is P+ fully set up in Brawl Online? Click here to look again."); return; }
                _cast = cast;
                _strip.Set(cast, art);
                _combo.SetCast(cast, cast.FirstOrDefault(c => c.Name == "Fox") ?? cast.FirstOrDefault());
                _targetChar = _diChar = cast.FirstOrDefault(c => c.Name == "Fox") ?? cast.FirstOrDefault();
                _di.SetCast(cast, _diChar);
                Arrange();
                _note.Text = $"{cast.Count} fighters, read straight from P+'s own files on your card. FAF = the first frame you can act again.";
                ShowFighter(cast.FirstOrDefault(c => c.Name == "Fox") ?? cast[0]);
            }
            catch (Exception e) { Program.Log("frame data", e); Failed("Couldn't read the fighters from your SD card. " + SkinManager.Core.Friendly.Explain(e) + " Click here to try again."); }
            finally { _starting = false; }
        }

        /// <summary>The page couldn't start: says why, and clicking the line tries again (so does coming back to the page).</summary>
        void Failed(string why)
        {
            _cast = null;
            _note.Text = why; _note.ForeColor = Theme.Warn; _note.Cursor = Cursors.Hand;
        }

        async void ShowFighter(Character c)
        {
            _strip.Selected = c; _strip.Invalidate();
            _who.Text = c.Name;
            FighterBook book;
            lock (_books) _books.TryGetValue(c.FilePrefix, out book);
            if (book == null)
            {
                _list.Show(null, "Reading " + c.Name + "'s moves…");
                try
                {
                    var inst = _installer();
                    book = await Task.Run(() => { using (var card = inst.OpenCard()) return FrameDataBook.Load(card, c); });
                    lock (_books) _books[c.FilePrefix] = book;
                }
                catch (Exception e) { Program.Log("frame data " + c.Name, e); if (_strip.Selected == c) _list.Show(null, "Couldn't read " + c.Name + "'s moves. " + SkinManager.Core.Friendly.Explain(e) + " Click " + c.Name + " again to retry."); return; }
            }
            if (_strip.Selected != c) return;   // picked someone else meanwhile
            _book = book;
            _list.Show(book, null);
            ShowSure();
            var first = book.Entries.FirstOrDefault();
            if (first != null) ShowMove(first);
        }

        bool _combos, _diOn;
        bool _sureOn;
        void ShowTab(int tab)
        {
            _combos = tab == 1; _diOn = tab == 2; _sureOn = tab == 3;
            Theme.Toggle(_tabMoves, tab == 0); Theme.Toggle(_tabCombos, _combos); Theme.Toggle(_tabDi, _diOn); Theme.Toggle(_tabSure, _sureOn);
            _view.Visible = tab == 0; _combo.Visible = _combos; _di.Visible = _diOn; _sure.Visible = _sureOn;
            if (_combos) ShowCombos();
            if (_diOn) ShowDi();
            if (_sureOn) ShowSure();
        }

        void ShowSure()
        {
            if (!_sureOn) return;
            if (_book == null) { _sure.Status("Pick a fighter."); return; }
            _sure.Show(_book);
        }

        /// <summary>Every fighter's accuracy counts (each fighter read in the background; ones already open are reused).</summary>
        async void CheckWholeCast()
        {
            if (_cast == null || _cast.Count == 0) return;
            var inst = _installer(); if (inst == null) return;
            var rows = new List<(Character, int, int, int)>();
            _sure.ShowCast(rows, _cast.Count);
            foreach (var c in _cast.ToList())
            {
                FighterBook book;
                lock (_books) _books.TryGetValue(c.FilePrefix, out book);
                try
                {
                    // (read just for this and let go: keeping every fighter's models would take a lot of memory)
                    if (book == null) book = await Task.Run(() => { using (var card = inst.OpenCard()) return FrameDataBook.Load(card, c); });
                    var lines = FrameDataAccuracy.For(book);
                    book = null;
                    rows.Add((c, lines.Count(l => l.Level == Sureness.Exact), lines.Count(l => l.Level == Sureness.Approximate), lines.Count(l => l.Level == Sureness.NotDrawn)));
                }
                catch (Exception e) { Program.Log("accuracy " + c.Name, e); }
                if (!_sureOn || IsDisposed) return;   // left the tab: stop
                _sure.ShowCast(rows, _cast.Count);
            }
        }

        /// <summary>The DI side: the move's pictures (with where everything is in them) and the fighter being hit.</summary>
        async void ShowDi()
        {
            if (!_diOn || _book == null || _move == null) return;
            var me = _book; var move = _move;
            var who = _diChar ?? me.Character;
            try
            {
                FighterBook them;
                lock (_books) _books.TryGetValue(who.FilePrefix, out them);
                if (them == null)
                {
                    _di.Status("Reading " + who.Name + "…");
                    var inst = _installer();
                    them = await Task.Run(() => { using (var card = inst.OpenCard()) return FrameDataBook.Load(card, who); });
                    lock (_books) _books[who.FilePrefix] = them;
                }
                if (_book != me || _move != move || !_diOn) return;
                _frames.TryGetValue(Key(move), out var pics); _cams.TryGetValue(Key(move), out var cam);
                if (pics == null || cam == null) { _di.Status("Drawing the move…"); return; }   // ShowMove calls back when it's drawn
                _di.Show(me, move, pics, cam, them.Body ?? Body.Of(them), who);
            }
            catch (Exception e) { Program.Log("DI", e); _di.Status("Couldn't work it out: " + e.Message); }
        }

        /// <summary>The launch & combos side: measures both fighters the first time (a second or two).</summary>
        async void ShowCombos()
        {
            if (!_combos || _book == null) return;
            var me = _book; var move = _move;
            var targetChar = _targetChar ?? me.Character;
            try
            {
                if (me.Places == null || !_books.ContainsKey(targetChar.FilePrefix) || _books[targetChar.FilePrefix].Places == null) _combo.Status("Measuring where every hitbox reaches…");
                var inst = _installer();
                var them = await Task.Run(() =>
                {
                    me.Measure();
                    FighterBook t;
                    lock (_books) _books.TryGetValue(targetChar.FilePrefix, out t);
                    if (t == null) { using (var card = inst.OpenCard()) t = FrameDataBook.Load(card, targetChar); lock (_books) _books[targetChar.FilePrefix] = t; }
                    t.Measure();
                    return t;
                });
                if (_book != me || !_combos) return;
                _combo.Show(me, them, _move);
            }
            catch (Exception e) { Program.Log("combos", e); _combo.Status("Couldn't work it out: " + e.Message); }
        }

        void ClearFrames() { foreach (var l in _frames.Values) foreach (var b in l) b.Dispose(); _frames.Clear(); _frameOrder.Clear(); _cams.Clear(); }

        string Key(MoveEntry m) => _book.Character.FilePrefix + "|" + m.Group + "|" + m.Name + "|" + string.Join("+", m.Parts);

        async void ShowMove(MoveEntry m)
        {
            _move = m; _list.Selected = m; _list.Invalidate();
            var book = _book;
            if (_combos) { ShowCombos(); }
            _view.Show(book, m, null);
            var key = Key(m);
            if (_frames.TryGetValue(key, out var have) && _cams.ContainsKey(key)) { _view.Show(book, m, have); ShowDi(); return; }
            if (_diOn) _di.Status("Drawing the move…");
            _drawing?.Cancel();
            var cts = _drawing = new CancellationTokenSource();
            bool hurt = _view.Hurtboxes;
            try
            {
                var cam = new ModelPreview.MoveCamera();
                var pics = await Task.Run(() => ModelPreview.RenderMove(book.Costume, book.Motion, book.Moves, m.Data, 520, 400, hurt,
                    (i, n) => BeginInvoke((Action)(() => { if (_move == m) _view.Progress(i, n); })), () => cts.IsCancellationRequested, cam));
                if (cts.IsCancellationRequested || pics.Count == 0) { foreach (var p in pics) p.Dispose(); return; }
                if (_frames.TryGetValue(key, out var stale)) foreach (var b in stale) if (!ReferenceEquals(stale, _view.Pictures)) b.Dispose();
                _frames[key] = pics; _cams[key] = cam; if (!_frameOrder.Contains(key)) _frameOrder.Add(key);
                while (_frameOrder.Count > 8) { var old = _frameOrder[0]; _frameOrder.RemoveAt(0); if (_frames.TryGetValue(old, out var l) && !ReferenceEquals(l, _view.Pictures)) foreach (var b in l) b.Dispose(); _frames.Remove(old); _cams.Remove(old); }
                if (_move == m) { _view.Show(book, m, pics); ShowDi(); }
            }
            catch (Exception e) { Program.Log("frame data draw", e); if (_move == m) _view.Failed(e.Message); }
        }

        void SaveGif()
        {
            if (_move == null || _view.Pictures == null) return;
            using (var d = new SaveFileDialog { Filter = "Animated picture (*.gif)|*.gif", FileName = $"{_book.Character.Name} - {_move.Name}.gif".Replace("/", "-") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var labelled = new List<Bitmap>();
                try
                {
                    // each picture with its frame number and what's happening, like on the page
                    var data = _move.Data;
                    for (int i = 0; i < _view.Pictures.Count; i++)
                    {
                        var b = new Bitmap(_view.Pictures[i]);
                        var f = i < data.Frames.Count ? data.Frames[i] : null;
                        using (var g = Graphics.FromImage(b))
                        using (var big = new Font("Segoe UI", 16f, FontStyle.Bold))
                        using (var small = new Font("Segoe UI", 9f, FontStyle.Bold))
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.DrawString($"{i + 1}", big, Brushes.White, 10, 6);
                            var state = MoveView.StateOf(data, f);
                            if (state != "") using (var br = new SolidBrush(MoveView.StateColor(state))) g.DrawString(state, small, br, 12, 36);
                            var name = _book.Character.Name + " · " + _move.Name;
                            using (var br = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.DrawString(name, small, br, b.Width - 10 - g.MeasureString(name, small).Width, 10);
                        }
                        labelled.Add(b);
                    }
                    GifWriter.Save(d.FileName, labelled, 2); _note.Text = "Saved " + Path.GetFileName(d.FileName) + ".";
                }
                catch (Exception e) { _note.Text = "Couldn't save it: " + Friendly.Explain(e); }
                finally { foreach (var b in labelled) b.Dispose(); }
            }
        }
    }

    /// <summary>The fighters' heads in rows; click one.</summary>
    class CastStrip : Control
    {
        List<(Character c, Image head)> _items = new List<(Character, Image)>();
        public Character Selected; public event Action<Character> Picked;
        int _hover = -1;
        const int S = 40, Gap = 5;
        public CastStrip()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            MouseMove += (s, e) => { int i = At(e.Location); if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } };
            MouseLeave += (s, e) => { _hover = -1; Invalidate(); };
            MouseClick += (s, e) => { int i = At(e.Location); if (i >= 0) Picked?.Invoke(_items[i].c); };
        }
        public void Set(List<Character> cast, StatsArt art)
        {
            var ids = Enumerable.Range(0, 60).GroupBy(Fighters.Name).ToDictionary(g => g.Key, g => g.Min());
            _items = cast.Select(c => (c, (Image)(ids.TryGetValue(c.Name, out int id) ? art?.Head(Fighters.Same(id)) : null))).ToList();
            Invalidate();
        }
        int PerRow(int w) => Math.Max(1, (w + Gap) / (S + Gap));
        public int Needed(int w) { int rows = Math.Max(1, (_items.Count + PerRow(w) - 1) / PerRow(w)); return rows * (S + Gap); }
        int At(Point p) { int per = PerRow(Width); int i = p.Y / (S + Gap) * per + p.X / (S + Gap); return p.X % (S + Gap) < S && i >= 0 && i < _items.Count ? i : -1; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int per = PerRow(Width);
            for (int i = 0; i < _items.Count; i++)
            {
                var r = new RectangleF(i % per * (S + Gap), i / per * (S + Gap), S, S);
                bool sel = _items[i].c == Selected;
                using (var bg = new SolidBrush(sel ? Color.FromArgb(90, Theme.Accent) : i == _hover ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255)))
                using (var path = StatsArt.Round(r, 7)) g.FillPath(bg, path);
                if (_items[i].head != null) StatsArt.Cover(g, _items[i].head, RectangleF.Inflate(r, -2, -2), 6);
                else using (var t = new SolidBrush(Theme.Text)) g.DrawString(_items[i].c.Name.Substring(0, Math.Min(3, _items[i].c.Name.Length)), Theme.Small, t, r.X + 6, r.Y + 14);
                if (sel) using (var pen = new Pen(Theme.Accent, 2)) using (var path = StatsArt.Round(r, 7)) g.DrawPath(pen, path);
            }
            if (_hover >= 0)
            {
                // the name under the cursor
                var name = _items[_hover].c.Name; var size = g.MeasureString(name, Theme.Small);
                var r = new RectangleF(_hover % per * (S + Gap), _hover / per * (S + Gap) + S - 16, Math.Max(S, size.Width + 6), 16);
                if (r.Right > Width) r.X = Width - r.Width;
                using (var b = new SolidBrush(Color.FromArgb(220, 15, 16, 22))) g.FillRectangle(b, r);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(name, Theme.Small, t, r.X + 3, r.Y + 1);
            }
        }
    }

    /// <summary>The moves, grouped, with their main numbers.</summary>
    class MoveList : Panel
    {
        FighterBook _book; string _message;
        readonly List<(string header, MoveEntry move)> _rows = new List<(string, MoveEntry)>();
        public MoveEntry Selected; public event Action<MoveEntry> Picked;
        public string Filter = "All";
        int _hover = -1;
        const int H = 34;
        public MoveList()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(150, 20, 22, 28);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            MouseMove += (s, e) => { int i = At(e.Location); if (i != _hover) { _hover = i; Cursor = i >= 0 && _rows[i].move != null ? Cursors.Hand : Cursors.Default; Invalidate(); } };
            MouseLeave += (s, e) => { _hover = -1; Invalidate(); };
            MouseClick += (s, e) => { int i = At(e.Location); if (i >= 0 && _rows[i].move != null) Picked?.Invoke(_rows[i].move); };
        }
        int At(Point p) { int y = p.Y - AutoScrollPosition.Y - 30; int i = y < 0 ? -1 : y / H; return i >= 0 && i < _rows.Count ? i : -1; }
        public void Show(FighterBook book, string message)
        {
            _book = book; _message = message; _rows.Clear();
            if (book != null)
                foreach (var grp in FrameDataBook.Groups)
                {
                    var moves = book.Entries.Where(m => m.Group == grp && FrameDataBook.InTab(m, Filter)).ToList();
                    if (moves.Count == 0) continue;
                    _rows.Add((grp, null)); foreach (var m in moves) _rows.Add((null, m));
                }
            AutoScrollMinSize = new Size(0, 30 + _rows.Count * H + 8); AutoScrollPosition = new Point(0, 0); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width;
            // column positions (right side)
            int cFaf = w - 50, cAct = w - 150, cStart = w - 196;
            using (var m = new SolidBrush(Theme.Muted))
            {
                g.DrawString("MOVE", Theme.Small, m, 12, 8);
                g.DrawString("STARTUP", Theme.Small, m, cStart - 14, 8);
                g.DrawString("ACTIVE", Theme.Small, m, cAct, 8);
                g.DrawString("FAF", Theme.Small, m, cFaf, 8);
            }
            g.TranslateTransform(0, AutoScrollPosition.Y + 30);
            if (_book == null) { using (var m = new SolidBrush(Theme.Muted)) g.DrawString(_message ?? "", Theme.Body, m, 12, 10); return; }
            var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            for (int i = 0; i < _rows.Count; i++)
            {
                int y = i * H; var (header, mv) = _rows[i];
                if (header != null) { using (var b = new SolidBrush(Theme.Accent)) g.DrawString(header.ToUpperInvariant(), Theme.Small, b, 12, y + 14); continue; }
                if (mv == Selected) using (var b = new SolidBrush(Color.FromArgb(70, Theme.Accent))) g.FillRectangle(b, 0, y, w, H - 2);
                else if (i == _hover) using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, 0, y, w, H - 2);
                using (var line = new Pen(Color.FromArgb(60, Theme.Line))) g.DrawLine(line, 10, y + H - 1, w - 10, y + H - 1);
                var d = mv.Data;
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(mv.Name, Theme.BodyBold, t, new RectangleF(12, y + 8, cStart - 30, 20), clip);
                string start = d.FirstHit > 0 ? d.FirstHit.ToString() : "—";
                using (var t = new SolidBrush(d.FirstHit > 0 ? Theme.Text : Theme.Muted)) g.DrawString(start, Theme.BodyBold, t, cStart, y + 8);
                var act = d.Active.Count > 0 ? d.ActiveText : d.Projectiles.Count > 0 ? "shot " + d.Projectiles[0].ActiveText : d.Intangible.Count > 0 ? "invincible " + d.IntangibleText : "";
                using (var t = new SolidBrush(d.Active.Count > 0 ? Color.FromArgb(255, 140, 130) : Color.FromArgb(140, 170, 255))) g.DrawString(act, Theme.Small, t, new RectangleF(cAct, y + 10, cFaf - cAct - 6, 18), clip);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(d.Faf.ToString(), Theme.Body, t, cFaf, y + 8);
            }
        }
    }

    /// <summary>One move: the picture playing, a strip of its frames, the numbers.</summary>
    class MoveView : Control
    {
        FighterBook _book; MoveEntry _move; string _status;
        public List<Bitmap> Pictures;
        int _frame, _hold; bool _playing = true; int _speedIndex = 0; float _acc;
        static readonly float[] Speeds = { 1f, 0.5f, 0.25f };
        static readonly string[] SpeedNames = { "1×", "½×", "¼×" };
        public bool Hurtboxes = true;
        public event Action SaveGif, HurtboxesChanged;
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 16 };
        readonly Button _play = Theme.Secondary("❚❚"), _back = Theme.Secondary("◀"), _next = Theme.Secondary("▶"), _speed = Theme.Secondary("1×"), _gif = Theme.Secondary("Save GIF…");
        readonly CheckBox _hurt = new CheckBox { Text = "Hurtboxes", Checked = true, AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.Small };
        Rectangle _pic, _strip;

        public MoveView()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            foreach (var b in new[] { _play, _back, _next, _speed }) { b.Width = 46; b.Font = Theme.Body; }
            _gif.Width = 110;
            Controls.AddRange(new Control[] { _play, _back, _next, _speed, _gif, _hurt });
            _play.Click += (s, e) => { _playing = !_playing; _play.Text = _playing ? "❚❚" : "▶▶"; };
            _back.Click += (s, e) => Step(-1);
            _next.Click += (s, e) => Step(1);
            _speed.Click += (s, e) => { _speedIndex = (_speedIndex + 1) % Speeds.Length; _speed.Text = SpeedNames[_speedIndex]; };
            _gif.Click += (s, e) => SaveGif?.Invoke();
            _hurt.CheckedChanged += (s, e) => { Hurtboxes = _hurt.Checked; HurtboxesChanged?.Invoke(); };
            _timer.Tick += (s, e) =>
            {
                if (!_playing || Pictures == null || Pictures.Count == 0 || !Visible) return;
                _acc += Speeds[_speedIndex] * (_timer.Interval / (1000f / 60));
                if (_acc < 1) return;
                int steps = (int)_acc; _acc -= steps;
                if (_hold > 0) { _hold--; return; }
                for (int k = 0; k < steps; k++)
                {
                    _frame = (_frame + 1) % Pictures.Count;
                    // a hit that's out for only a frame or three (Fox's shine) would flash by: stay on it a moment
                    if (_move != null && _move.Data.Active.Any(a => a.From == _frame + 1 && a.To - a.From < 3)) { _hold = Math.Max(1, 400 / Math.Max(1, _timer.Interval)); break; }
                }
                Invalidate();
            };
            _timer.Start();
            MouseDown += (s, e) => { if (_strip.Contains(e.Location)) Scrub(e.X); };
            MouseMove += (s, e) => { if (e.Button == MouseButtons.Left && _strip.Contains(new Point(e.X, _strip.Y + 2))) Scrub(e.X); };
            Resize += (s, e) => Arrange();
            Arrange();
        }

        void Scrub(int x) { if (_move == null) return; int n = Math.Max(1, _move.Data.Frames.Count); _frame = Math.Max(0, Math.Min(n - 1, (x - _strip.X) * n / Math.Max(1, _strip.Width))); _playing = false; _play.Text = "▶▶"; Invalidate(); }
        void Step(int d) { if (_move == null) return; int n = Math.Max(1, _move.Data.Frames.Count); _frame = ((_frame + d) % n + n) % n; _playing = false; _play.Text = "▶▶"; Invalidate(); }

        void Arrange()
        {
            int w = Width;
            int picH = Math.Min((int)(w * 400f / 520), Math.Max(160, Height - 230));
            int picW = (int)(picH * 520f / 400);
            _pic = new Rectangle((w - picW) / 2, 0, picW, picH);
            _strip = new Rectangle(0, picH + 10, w, 26);
            int by = _strip.Bottom + 8;
            _back.Location = new Point(0, by); _play.Location = new Point(50, by); _next.Location = new Point(100, by); _speed.Location = new Point(150, by);
            _hurt.Location = new Point(206, by + 6);
            _gif.Location = new Point(w - _gif.Width, by);
        }

        public void Show(FighterBook book, MoveEntry m, List<Bitmap> pics)
        {
            bool same = _move == m;
            _book = book; _move = m; Pictures = pics; _status = pics == null ? "Drawing…" : null;
            if (!same) { _frame = 0; _playing = true; _play.Text = "❚❚"; }
            if (pics != null) _frame = Math.Min(_frame, pics.Count - 1);
            Invalidate();
        }
        public void Progress(int i, int n) { _status = $"Drawing frame {i + 1} of {n}…"; Invalidate(); }
        public void Failed(string why) { _status = "Couldn't draw it: " + why; Invalidate(); }

        public static string StateOf(MoveData d, FrameState f) =>
            f == null ? "" : f.Hitboxes.Any(h => !h.Grab) ? "ACTIVE" : f.Hitboxes.Any() ? "GRAB" : f.Body == 1 ? "INVINCIBLE" : f.Body >= 2 ? "INTANGIBLE" : d.Iasa > 0 && f.Frame >= d.Iasa ? "CAN ACT" : "";
        public static Color StateColor(string state) =>
            state == "ACTIVE" ? Theme.Bad : state == "GRAB" ? Color.FromArgb(170, 80, 255) : state == "CAN ACT" ? Theme.Good : Color.FromArgb(90, 150, 255);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var bg = new SolidBrush(Color.FromArgb(200, 20, 22, 28))) using (var path = StatsArt.Round(_pic, 10)) g.FillPath(bg, path);
            if (_move == null) return;
            var d = _move.Data; var mvNote = _move.Note;
            int n = Math.Max(1, d.Frames.Count);
            var f = _frame < d.Frames.Count ? d.Frames[_frame] : null;
            if (Pictures != null && _frame < Pictures.Count)
                using (var path = StatsArt.Round(_pic, 10)) { g.SetClip(path); Theme.Blit(g, Theme.Scaled(Pictures[_frame], _pic.Width, _pic.Height), _pic.X, _pic.Y); g.ResetClip(); }   // each frame shrunk once, then just copied
            else using (var m = new SolidBrush(Theme.Muted)) g.DrawString(_status ?? "", Theme.Body, m, _pic.X + 16, _pic.Y + 16);
            // frame counter over the picture
            if (f != null)
            {
                string state = StateOf(d, f);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString($"{f.Frame}", Theme.Bold(18f), t, _pic.X + 12, _pic.Y + 8);
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString($"/ {n}", Theme.Body, m, _pic.X + 14 + g.MeasureString($"{f.Frame}", Theme.Bold(18f)).Width, _pic.Y + 16);
                if (state != "") using (var b = new SolidBrush(StateColor(state))) g.DrawString(state, Theme.BodyBold, b, _pic.X + 14, _pic.Y + 40);
                using (var m = new SolidBrush(Color.FromArgb(170, Theme.Text))) g.DrawString(_book.Character.Name + " · " + _move.Name, Theme.Small, m, _pic.Right - 12 - g.MeasureString(_book.Character.Name + " · " + _move.Name, Theme.Small).Width, _pic.Y + 10);
            }
            // the strip: one cell per frame
            float cw = _strip.Width / (float)n;
            for (int i = 0; i < d.Frames.Count; i++)
            {
                var fr = d.Frames[i];
                Color c = fr.After ? Color.FromArgb(40, 255, 255, 255) : fr.Hitboxes.Any(h => !h.Grab) ? Theme.Bad : fr.Hitboxes.Any() ? Color.FromArgb(170, 80, 255) : fr.Body != 0 ? Color.FromArgb(90, 150, 255) : d.Iasa > 0 && fr.Frame >= d.Iasa ? Color.FromArgb(70, Theme.Good) : Color.FromArgb(70, 255, 255, 255);
                using (var b = new SolidBrush(c)) g.FillRectangle(b, _strip.X + i * cw + (cw > 4 ? 0.5f : 0), _strip.Y, Math.Max(1, cw - (cw > 4 ? 1 : 0)), _strip.Height);
            }
            // a gold line under the frames a projectile can hit on
            foreach (var p in d.Projectiles.Where(x => x.Flight != null))
                for (int k = 0; k < p.Flight.Hits.Count; k++)
                    if (p.Flight.Hits[k].Any(h => !h.Grab))
                    {
                        int i = p.Flight.Start + k - 1; if (i < 0 || i >= d.Frames.Count) continue;
                        using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, _strip.X + i * cw, _strip.Bottom + 1, Math.Max(1, cw), 3);
                    }
            foreach (var p in d.Projectiles.Where(x => !x.Unknown))
            {
                // a little gold marker where a projectile comes out
                float px = _strip.X + (p.Frame - 1) * cw + cw / 2;
                using (var b = new SolidBrush(Theme.Accent)) g.FillPolygon(b, new[] { new PointF(px - 5, _strip.Y - 8), new PointF(px + 5, _strip.Y - 8), new PointF(px, _strip.Y - 1) });
            }
            using (var pen = new Pen(Theme.Accent, 2)) g.DrawRectangle(pen, _strip.X + _frame * cw, _strip.Y - 2, Math.Max(2, cw), _strip.Height + 4);
            // numbers
            int y = _strip.Bottom + 48;
            var cols = new List<(string k, string v)>
            {
                ("Startup", d.Startup > 0 ? d.Startup.ToString() : d.FirstHit > 0 ? d.FirstHit + " (shot)" : "—"),
                ("Active", d.Active.Count > 0 ? d.ActiveText : "—"),
                ("First actionable", d.Faf.ToString()),
                ("Total", d.Total.ToString()),
            };
            if (d.LandingLag > 0) cols.Add(("Landing lag", $"{d.LandingLag} ({d.LandingLag / 2} if you L-cancel)"));
            if (d.Intangible.Count > 0) cols.Add(("Intangible", d.IntangibleText));
            foreach (var p in d.Projectiles) cols.Add((p.Name.StartsWith("Projectile") ? "Projectile" : "Spawned hitbox",
                (p.Flight != null ? $"{p.Name}: hits {p.ActiveText}" + (p.Flight.About != null ? " · " + p.Flight.About : "") : p.Unknown ? $"{p.Name}: when is up to the game, hits on {p.ActiveText}" : $"out on frame {p.Frame}, hits {p.ActiveText}") + (p.Flight == null && p.Data.Lingers ? " (+ = keeps going until it hits or vanishes)" : "")));
            if (mvNote != null) cols.Add(("Note", mvNote));
            if (d.Throw != null) cols.Add(("Throw", $"{d.Throw.DamageText} · {d.Throw.Angle}° · BKB {d.Throw.Bkb} · KBG {d.Throw.Kbg}" + (d.ThrowFrame > 0 ? $" · released frame {d.ThrowFrame}" : "")));
            int x = 0, colW = Math.Max(110, Width / Math.Min(4, cols.Count));
            for (int i = 0; i < cols.Count; i++)
            {
                bool wide = cols[i].k == "Throw" || cols[i].k == "Projectile" || cols[i].k == "Spawned hitbox" || cols[i].k == "Note";
                if (x + colW > Width + 4 || (wide && x > 0)) { x = 0; y += 44; }
                using (var m = new SolidBrush(Theme.Muted)) g.DrawString(cols[i].k.ToUpperInvariant(), Theme.Small, m, x, y);
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(cols[i].v, Theme.BodyBold, t, new RectangleF(x, y + 16, wide ? Width : colW - 8, 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                x += wide ? Width : colW;
            }
            y += 48;
            // hitboxes
            var all = d.Hits.Select(h => (h, own: true)).Concat(d.Projectiles.SelectMany(p => p.Data.Hits.Select(h => (h, own: false)))).ToList();
            if (all.Count > 0)
            {
                using (var m = new SolidBrush(Theme.Muted))
                {
                    string[] heads = { "", "DAMAGE", "ANGLE", "BKB", "KBG", "WKB", "SIZE", "SHIELD" };
                    float[] xs = { 0, 22, 92, 152, 202, 252, 302, 352 };
                    for (int i = 0; i < heads.Length; i++) g.DrawString(heads[i], Theme.Small, m, xs[i], y);
                    y += 18;
                    foreach (var (h, own) in all.Take(14))
                    {
                        if (y > Height - 16) break;
                        bool on = own ? f != null && f.Hitboxes.Any(a => a.Id == h.Id && a.Grab == h.Grab) : true;
                        if (!own && (h == all.First(a => !a.own).h)) { using (var a = new SolidBrush(Theme.Accent)) g.DrawString("▼ projectile", Theme.Small, a, xs[0], y); y += 18; }
                        var col = h.Grab ? Color.FromArgb(170, 80, 255) : ModelPreview.HitColors[h.Id % ModelPreview.HitColors.Length];
                        using (var b = new SolidBrush(on ? col : Color.FromArgb(90, col))) g.FillEllipse(b, 4, y + 3, 12, 12);
                        using (var t = new SolidBrush(on ? Theme.Text : Theme.Muted))
                        {
                            if (h.Grab) { g.DrawString("grab box", Theme.Body, t, xs[1], y); g.DrawString(h.Size.ToString("0.#"), Theme.Body, t, xs[6], y); }
                            else
                            {
                                g.DrawString(h.DamageText, Theme.Body, t, xs[1], y); g.DrawString(h.Angle + "°", Theme.Body, t, xs[2], y);
                                g.DrawString(h.Bkb.ToString(), Theme.Body, t, xs[3], y); g.DrawString(h.Kbg.ToString(), Theme.Body, t, xs[4], y);
                                g.DrawString(h.Wkb > 0 ? h.Wkb.ToString() : "—", Theme.Body, t, xs[5], y); g.DrawString(h.Size.ToString("0.#"), Theme.Body, t, xs[6], y);
                                g.DrawString(h.ShieldDamage != 0 ? (h.ShieldDamage > 32767 ? h.ShieldDamage - 65536 : h.ShieldDamage).ToString("+0;-0") : "0", Theme.Body, t, xs[7], y);
                            }
                        }
                        y += 20;
                    }
                    // what the short column names mean, for anyone who doesn't play competitively
                    if (y + 18 < Height)
                        g.DrawString("BKB = knockback at 0%  ·  KBG = how much more it knocks back as damage goes up  ·  WKB = the same knockback at any %  ·  SHIELD = extra damage to shields",
                            Theme.Small, m, new RectangleF(0, y + 4, Width, 32));
                }
            }
        }
    }
}
