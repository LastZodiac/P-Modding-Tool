// Stat Tracker: your match history from Brawl Online (the database it keeps of every game, and its replays; only read,
// never changed): an overview of your form and Smash stats, your characters, your matches, records by matchup, stage
// and opponent, and your chance to win a game before it starts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    /// <summary>A list of records: a name, a small line under it, wins–losses, the win rate as a bar. Rows can be clicked.</summary>
    class RecordTable : Control
    {
        public sealed class Row { public string Name, Note; public Record Record; public object Tag; public bool Header; public double? Chance; public Image Icon; public bool Wide; }
        public List<Row> Rows = new List<Row>();
        public event Action<Row> RowClicked;
        public string Empty = "Nothing here yet.";
        const int H = 54;
        int _hover = -1;
        public object Selected;

        public RecordTable()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true; BackColor = Color.FromArgb(150, 20, 22, 28);
            MouseMove += (s, e) => { int i = At(e.Location); if (i != _hover) { _hover = i; Cursor = i >= 0 && RowClicked != null && !Rows[i].Header ? Cursors.Hand : Cursors.Default; Invalidate(); } };
            MouseLeave += (s, e) => { _hover = -1; Invalidate(); };
            MouseClick += (s, e) => { int i = At(e.Location); if (i >= 0 && !Rows[i].Header) { Selected = Rows[i].Tag; RowClicked?.Invoke(Rows[i]); Invalidate(); } };
        }

        // scrolling: the wheel, or dragging the thin bar on the right (its own, so it can't trip up)
        int _scroll; bool _drag; int _dragFrom, _scrollFrom;
        int MaxScroll => Math.Max(0, Rows.Count * H - ClientSize.Height);
        void SetScroll(int v) { int s = Math.Max(0, Math.Min(MaxScroll, v)); if (s != _scroll) { _scroll = s; Invalidate(); } }
        RectangleF Thumb() { if (MaxScroll <= 0) return RectangleF.Empty; float h = Math.Max(30, ClientSize.Height * (float)ClientSize.Height / (Rows.Count * H)); return new RectangleF(Width - 7, (ClientSize.Height - h) * _scroll / MaxScroll, 5, h); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); SetScroll(_scroll - e.Delta / 2); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); SetScroll(_scroll); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (MaxScroll > 0 && e.X >= Width - 12) { _drag = true; _dragFrom = e.Y; _scrollFrom = _scroll; } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) { float range = ClientSize.Height - Thumb().Height; if (range > 0) SetScroll(_scrollFrom + (int)((e.Y - _dragFrom) / range * MaxScroll)); } }

        int At(Point p) { if (p.X >= Width - 12 && MaxScroll > 0) return -1; int i = (p.Y + _scroll) / H; return i >= 0 && i < Rows.Count ? i : -1; }

        public void Show(List<Row> rows) { Rows = rows; _scroll = 0; Invalidate(); }

        public static Color RateColor(double rate) => rate >= 0.55 ? Theme.Good : rate >= 0.45 ? Theme.Warn : Theme.Bad;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var thumb = Thumb();
            if (!thumb.IsEmpty) using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) using (var p = StatsArt.Round(thumb, 2.5f)) g.FillPath(b, p);
            g.TranslateTransform(0, -_scroll);
            int w = ClientSize.Width - (MaxScroll > 0 ? 10 : 0);
            if (Rows.Count == 0) using (var b = new SolidBrush(Theme.Muted)) g.DrawString(Empty, Theme.Body, b, 16, 16);
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i]; int y = i * H;
                if (r.Chance != null)
                {
                    // the big number: your chance to win
                    using (var c = new SolidBrush(RateColor(r.Chance.Value))) g.DrawString($"{r.Chance.Value * 100:0}%", Theme.Bold(22f), c, 12, y + 2);
                    using (var t = new SolidBrush(Theme.Text)) g.DrawString(r.Name, Theme.BodyBold, t, 110, y + 5);
                    if (r.Note != null) using (var m = new SolidBrush(Theme.Muted)) g.DrawString(r.Note, Theme.Small, m, 110, y + 24);
                    continue;
                }
                if (r.Header) { using (var b = new SolidBrush(Theme.Accent)) g.DrawString(r.Name.ToUpperInvariant(), Theme.Small, b, 14, y + 30); continue; }
                if (i == _hover && RowClicked != null || Selected != null && Equals(Selected, r.Tag)) using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, 0, y, w, H - 2);
                using (var line = new Pen(Theme.Line)) g.DrawLine(line, 10, y + H - 1, w - 10, y + H - 1);
                int tx = 14;
                if (r.Icon != null)
                {
                    var box = r.Wide ? new RectangleF(12, y + 8, w < 560 ? 84 : 118, 36) : new RectangleF(12, y + 5, 38, 43);
                    using (var bg = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) using (var path = StatsArt.Round(box, 5)) g.FillPath(bg, path);
                    StatsArt.Cover(g, r.Icon, box, 5, false, 0.5f, r.Wide ? 0.5f : 0f);
                    tx = (int)box.Right + 12;
                }
                bool narrow = w < 560;
                int barW = narrow ? Math.Max(50, w / 6) : 150;
                int tail = narrow ? 48 : 120;                 // the % (and the game count when there's room)
                int barX = w - tail - barW - 16;
                int recX = barX - (narrow ? 50 : 80);
                var nameBox = new RectangleF(tx, y + 9, Math.Max(40, (r.Record.Games == 0 ? w - tx - 14 : recX - tx - 8)), 20);
                var noteBox = new RectangleF(tx, y + 29, nameBox.Width, 18);
                var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(r.Name, Theme.BodyBold, t, nameBox, clip);
                if (r.Note != null) using (var m = new SolidBrush(Theme.Muted)) g.DrawString(r.Note, Theme.Small, m, noteBox, clip);
                if (r.Record.Games == 0) continue;
                using (var t = new SolidBrush(Theme.Text)) g.DrawString(r.Record.ToString(), Theme.Body, t, recX, y + 17);
                using (var back = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillRectangle(back, barX, y + 22, barW, 8);
                using (var fill = new SolidBrush(RateColor(r.Record.Rate))) g.FillRectangle(fill, barX, y + 22, (float)(barW * r.Record.Rate), 8);
                using (var t = new SolidBrush(RateColor(r.Record.Rate))) g.DrawString(r.Record.Percent, Theme.BodyBold, t, barX + barW + 8, y + 16);
                if (!narrow) using (var m = new SolidBrush(Theme.Muted)) g.DrawString(r.Record.Games == 1 ? "1 game" : $"{r.Record.Games} games", Theme.Small, m, barX + barW + 56, y + 18);
            }
        }
    }

    /// <summary>A picture card: a stage behind (faded) and/or a character on the right, a title, a big line and a small one;
    /// optionally a row of recent results (green wins, red losses).</summary>
    class TileView : Control
    {
        public Image Back, Side; public string Top, Big, Small; public Color BigColor = Theme.Text; public List<bool> Streak;
        public TileView() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(Color.FromArgb(225, Theme.Panel))) using (var path = StatsArt.Round(box, 8)) g.FillPath(bg, path);
            if (Back != null) { StatsArt.Cover(g, Back, box, 8); using (var path = StatsArt.Round(box, 8)) { g.SetClip(path); StatsArt.Shade(g, box, 235, 90, LinearGradientMode.Horizontal); g.ResetClip(); } }
            if (Side != null)
            {
                var sb = new RectangleF(Width - Height * 0.8f - 2, 2, Height * 0.8f, Height - 4);
                StatsArt.Cover(g, Side, sb, 6, false, 0.5f, 0.1f);
                using (var path = StatsArt.Round(box, 8)) { g.SetClip(path); StatsArt.Shade(g, new RectangleF(sb.X - 30, 0, 60, Height), 225, 0, LinearGradientMode.Horizontal); g.ResetClip(); }
            }
            float textW = Width - 24 - (Side != null ? Height * 0.8f - 10 : 0);
            var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var m = new SolidBrush(Color.FromArgb(220, Theme.Muted))) g.DrawString(Top ?? "", Theme.Small, m, new RectangleF(12, 8, textW, 18), fmt);
            using (var c = new SolidBrush(BigColor)) g.DrawString(Big ?? "", Theme.Bold(15f), c, new RectangleF(11, 26, textW, 30), fmt);
            if (Streak != null && Streak.Count > 0)
            {
                float x = 13, size = Math.Min(10, (textW - 4) / Streak.Count - 2);
                foreach (var won in Streak) { using (var b = new SolidBrush(won ? Theme.Good : Theme.Bad)) g.FillRectangle(b, x, Height - 22, size, size); x += size + 2; }
            }
            else using (var t = new SolidBrush(Theme.Text)) g.DrawString(Small ?? "", Theme.Small, t, new RectangleF(12, Height - 26, textW, 18), fmt);
        }
    }

    /// <summary>You against them: both characters big over the stage, the chance to win in the middle.</summary>
    class Faceoff : Control
    {
        public Image Stage, Mine, Theirs; public string MineName, TheirsName, Line; public double Chance; public bool Few;
        public Faceoff() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(Color.FromArgb(230, Theme.Panel))) using (var path = StatsArt.Round(box, 10)) g.FillPath(bg, path);
            if (Stage != null) { StatsArt.Cover(g, Stage, box, 10); using (var path = StatsArt.Round(box, 10)) { g.SetClip(path); using (var b = new SolidBrush(Color.FromArgb(150, 12, 13, 18))) g.FillRectangle(b, box); g.ResetClip(); } }
            float pw = Height * 0.8f;
            if (Mine != null) StatsArt.Cover(g, Mine, new RectangleF(16, 8, pw, Height - 16), 8, false, 0.5f, 0.1f);
            if (Theirs != null) StatsArt.Cover(g, Theirs, new RectangleF(Width - pw - 16, 8, pw, Height - 16), 8, true, 0.5f, 0.1f);
            var center = new StringFormat { Alignment = StringAlignment.Center };
            float cx = Width / 2f;
            using (var t = new SolidBrush(Theme.Text)) g.DrawString((MineName ?? "Any character") + "   vs   " + (TheirsName ?? "any character"), Theme.BodyBold, t, cx, 14, center);
            using (var c = new SolidBrush(RecordTable.RateColor(Chance))) g.DrawString($"{Chance * 100:0}%", Theme.Bold(34f), c, cx, Height / 2f - 34, center);
            using (var m = new SolidBrush(Theme.Text)) g.DrawString(Few ? "chance to win (few games so far)" : "chance to win", Theme.Small, m, cx, Height / 2f + 22, center);
            if (Line != null) using (var m = new SolidBrush(Theme.Muted)) g.DrawString(Line, Theme.Small, m, cx, Height - 26, center);
        }
    }

    class StatsPanel : GlassPanel
    {
        readonly Func<BrawlOnlineInstall> _install;
        readonly Func<Installer> _installer;
        MatchHistory _h;
        public bool Ready => _h != null;
        StatsArt _art;
        bool? _ranked;
        string _tab = "Overview";
        Dictionary<int, string> _stageNames;
        readonly StatsData _data = new StatsData();
        int _loadId;

        readonly TileView _profile = new TileView();
        readonly ComboBox _which = Combo(170);
        readonly Button _refresh = Theme.Secondary("Refresh");
        readonly FlowLayoutPanel _tabs = new FlowLayoutPanel { BackColor = Color.Transparent, WrapContents = false };
        readonly Panel _body = new Panel { BackColor = Color.Transparent };
        readonly RecordTable _table = new RecordTable();
        readonly RecordTable _side = new RecordTable();
        readonly Label _sideTitle = Theme.Label("", Theme.Heading);
        readonly Panel _tools = new Panel { BackColor = Color.Transparent };
        readonly OverviewView _overview = new OverviewView();
        readonly CharacterChart _chart = new CharacterChart();
        readonly CharacterCard _charCard = new CharacterCard();
        readonly MatchList _matches = new MatchList();
        readonly MatchCard _matchCard = new MatchCard();
        readonly ProgressView _progress = new ProgressView();

        static ComboBox Combo(int w) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        sealed class Item { public string Text; public object Value; public override string ToString() => Text; }

        static readonly string[] Views = { "Overview", "Progress", "Characters", "Matches", "Matchups", "Stages", "Opponents", "Win chance" };

        public StatsPanel(Func<BrawlOnlineInstall> install, Func<Installer> installer)
        {
            _install = install; _installer = installer;
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8);
            _profile.Top = "STAT TRACKER"; _profile.Big = "Reading your games…";
            _which.Items.AddRange(new object[] { new Item { Text = "All games" }, new Item { Text = "Ranked only", Value = true }, new Item { Text = "Unranked only", Value = false } });
            _which.SelectedIndex = 0;
            _which.SelectedIndexChanged += (s, e) => { _ranked = (bool?)((Item)_which.SelectedItem).Value; ShowAll(); };
            _refresh.Click += (s, e) => Reload();
            Controls.AddRange(new Control[] { _profile, _which, _refresh, _tabs, _body });
            foreach (var t in Views)
            {
                var b = Theme.Secondary(t); b.Margin = new Padding(0, 0, 8, 0);
                b.Click += (s, e) => { _tab = t; if (t == "Matches") b.Text = "Matches"; ShowAll(); };
                _tabs.Controls.Add(b);
            }
            _overview.Data = _chart.Data = _charCard.Data = _matches.Data = _matchCard.Data = _progress.Data = _data;
            _chart.Picked += c => { _charCard.Character = c; _charCard.ToTop(); _charCard.Invalidate(); };
            _matches.Picked += id => { _matchCard.Game = id; _matchCard.ToTop(); _matchCard.Invalidate(); };
            _matchCard.ShowReplay += ShowReplayFile;
            _body.Controls.AddRange(new Control[] { _tools, _table, _sideTitle, _side, _overview, _chart, _charCard, _matches, _matchCard, _progress });
            Resize += (s, e) => Arrange();
            VisibleChanged += (s, e) => { if (Visible && _h == null) Reload(); };
            Arrange();
            var test = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_WINCHANCE");   // UI tests: "mine,theirs,stage"
            if (test != null) { var t = test.Split(','); _tab = "Win chance"; _wcMine = int.Parse(t[0]); _wcTheirs = int.Parse(t[1]); _wcStage = int.Parse(t[2]); }
            var view = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_STATSVIEW");     // UI tests: open on a view
            if (view != null && Views.Contains(view)) _tab = view;
        }

        void Arrange()
        {
            int w = Math.Max(700, ClientSize.Width - 44);
            _profile.Location = new Point(28, 12); _profile.Size = new Size(w - 300, 86);
            _which.Location = new Point(28 + w - 280, 20); _refresh.Location = new Point(28 + w - 100, 18);
            _tabs.Location = new Point(28, 110); _tabs.Size = new Size(w, 36);
            _body.Location = new Point(28, 154); _body.Size = new Size(w, Math.Max(200, ClientSize.Height - 164));
            int toolsH = _tools.Controls.Count > 0 ? _tools.Height : 0;
            bool side = _side.Visible;
            int tableW = side ? (int)(_body.Width * 0.58) : _body.Width;
            _tools.Location = new Point(0, 0); _tools.Width = _body.Width;
            foreach (Control c in _tools.Controls) if (c is Faceoff) c.Width = _body.Width;
            _table.Location = new Point(0, toolsH); _table.Size = new Size(tableW, _body.Height - toolsH);
            _sideTitle.Location = new Point(tableW + 16, toolsH); _sideTitle.AutoSize = false; _sideTitle.Size = new Size(_body.Width - tableW - 16, 26);
            _side.Location = new Point(tableW + 16, toolsH + 30); _side.Size = new Size(_body.Width - tableW - 16, _body.Height - toolsH - 30);
            _overview.Bounds = _progress.Bounds = new Rectangle(0, 0, _body.Width, _body.Height);
            int left = (int)(_body.Width * 0.52);
            _chart.Bounds = new Rectangle(0, 0, left, _body.Height);
            _charCard.Bounds = new Rectangle(left + 14, 0, _body.Width - left - 14, _body.Height);
            int list = (int)(_body.Width * 0.5);
            _matches.Bounds = new Rectangle(0, 0, list, _body.Height);
            _matchCard.Bounds = new Rectangle(list + 14, 0, _body.Width - list - 14, _body.Height);
        }

        void Reload() { _ = Load(false); }

        Task _loading;
        bool _again;
        DateTime _lastGameStart = DateTime.MinValue;
        /// <summary>Where every game read is kept for good (so deleting replays loses nothing).</summary>
        public static string KeptFolder => System.IO.Path.Combine(Program.DataFolder, "Stats");
        static readonly DateTime Started = DateTime.UtcNow;
        /// <summary>A game finished since the last look (its id), with its report ready.</summary>
        public event Action<long> NewGame;
        public StatsData Data => _data;

        long? _newGame;
        /// <summary>A new game's report is ready: Matches shows it (and says so on its tab).</summary>
        public void MarkNew(long gameId)
        {
            _newGame = gameId; _matchCard.Game = gameId; _matches.Selected = gameId; _matchCard.ToTop();
            foreach (Control b in _tabs.Controls) if (b.Text.StartsWith("Matches")) b.Text = "Matches •";
            ShowAll();
        }

        /// <summary>Reads the history again (and any new replays), after a game ends: the page needn't be open.</summary>
        public void CheckNewGame() { _ = Load(true); }

        /// <summary>Reads (or reads again) everything; one at a time (a second call while reading reads once more after).</summary>
        Task Load(bool quiet)
        {
            if (_loading != null && !_loading.IsCompleted) { _again = true; return _loading; }
            _loading = LoadOnce(quiet);
            return _loading;
        }

        async Task LoadOnce(bool quiet)
        {
            do
            {
                _again = false;
                var install = _install();
                if (install == null) { _profile.Big = "Brawl Online isn't set up on this PC yet."; _profile.Invalidate(); return; }
                if (!quiet || _h == null) { _profile.Big = "Reading your games…"; _profile.Invalidate(); }
                _refresh.Enabled = false;
                try
                {
                    if (_stageNames == null)
                        _stageNames = await Task.Run(() => { try { using (var card = _installer()?.OpenCard()) return card != null ? StageSelect.SlotListNames(card) : new Dictionary<int, string>(); } catch { return new Dictionary<int, string>(); } });
                    string path = MatchHistory.DatabasePath(install);
                    var names = _stageNames;
                    _h = await Task.Run(() => MatchHistory.Load(path, names, KeptFolder));
                    if (_h.Games.Count == 0) { _profile.Big = "No games yet"; _profile.Small = "Brawl Online keeps your history once you've played online."; _profile.Invalidate(); return; }
                    ShowAll();
                    if (_art == null) { var installer = _installer; _art = await Task.Run(() => StatsArt.Get(installer)); ShowAll(); }
                    await LoadReplays();
                    // a game played while the app is open (or, the first time, the newest one if it's from then) gets a report
                    var newest = _h.Singles().OrderBy(x => x.Game.Start).Select(x => x.Game).LastOrDefault();
                    Program.Trace($"stats read: newest game {newest?.Start:u}, last seen {_lastGameStart:u}");
                    bool test = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") != null && _lastGameStart == DateTime.MinValue;
                    if (newest != null && newest.Start > _lastGameStart && (_lastGameStart > DateTime.MinValue || newest.Start > Started || test)) NewGame?.Invoke(newest.Id);
                    if (newest != null && newest.Start > _lastGameStart) _lastGameStart = newest.Start;
                }
                catch (Exception e)
                {
                    Program.Log("reading the match history", e);
                    _profile.Big = "Your match history couldn't be read"; _profile.Small = Friendly.Explain(e); _profile.Invalidate();
                }
                finally { _refresh.Enabled = true; }
            } while (_again);
        }

        /// <summary>Where a replay is: where Brawl Online saved it, or the same month folder in this PC's Documents.</summary>
        static string Locate(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            try
            {
                if (System.IO.File.Exists(file)) return file;
                var parts = file.Split('\\', '/');
                string month = parts.Length > 1 ? parts[parts.Length - 2] : "";
                string here = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Brawl Online", month, parts[parts.Length - 1]);
                return System.IO.File.Exists(here) ? here : null;
            }
            catch { return null; }
        }

        Dictionary<int, MoveTable> _tables;
        string _tablesKey;

        /// <summary>Every fighter's move names and landing lags (a quick read of each moveset), once.</summary>
        async Task LoadTables()
        {
            if (_tables != null) return;
            var installer = _installer;
            var t = await Task.Run(() =>
            {
                var o = new Dictionary<int, MoveTable>();
                try
                {
                    using (var card = installer()?.OpenCard())
                        if (card != null)
                            foreach (var id in Enumerable.Range(0, 60).Select(Fighters.Same).Distinct())
                            {
                                var ch = Characters.All.FirstOrDefault(x => string.Equals(x.Name, Fighters.Name(id), StringComparison.OrdinalIgnoreCase));
                                if (ch == null) continue;
                                try { var mt = FrameDataBook.MoveTable(card, ch); if (mt != null) o[id] = mt; } catch { }
                            }
                }
                catch (Exception e) { Program.Log("reading move names", e); }
                return o;
            });
            _tables = t;
            // what the habits were read with: if P+'s moves change, the replays are read again
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var text = string.Join("|", t.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + string.Join(",", kv.Value.Names) + ":" + string.Join(",", kv.Value.LandingLag)));
                _tablesKey = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
            }
        }

        MoveTable TableOf(int character) => _tables != null && _tables.TryGetValue(Fighters.Same(character), out var t) ? t : null;

        /// <summary>Reads every game's replay in the background (once: kept in the cache), then works out your progress.</summary>
        async Task LoadReplays()
        {
            int id = ++_loadId;
            await LoadTables();
            var games = _h.Singles().Where(x => !_data.Smash.ContainsKey(x.Game.Id)).ToList();
            string kept = System.IO.Path.Combine(KeptFolder, "replays");
            try { var old = System.IO.Path.Combine(Program.DataFolder, "Cache", "replays"); if (System.IO.Directory.Exists(old)) System.IO.Directory.Delete(old, true); } catch { }   // the old cache (1.0), now kept in Stats
            _data.ReplaysLoading = games.Count > 0; _data.ReplaysTotal = games.Count; _data.ReplaysRead = 0;
            var found = new System.Collections.Concurrent.ConcurrentDictionary<long, GameSmash>();
            var progress = new Progress<int>(n =>
            {
                if (id != _loadId) return;
                _data.ReplaysRead = n;
                foreach (var kv in found) _data.Smash[kv.Key] = kv.Value;
                Profile(); if (n % 24 == 0) RefreshViews();
            });
            IProgress<int> report = progress;
            Func<int, MoveTable> tables = TableOf; string key = _tablesKey;
            await Task.Run(() =>
            {
                int done = 0;
                System.Threading.Tasks.Parallel.ForEach(games, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)) }, x =>
                {
                    try
                    {
                        var path = Locate(x.Game.File);
                        var d = Replays.Kept(x.Game.Id, path, kept, tables, key);
                        if (d != null) { var s = GameSmash.Of(x.Game, x.Me, x.Them, d); if (s != null) found[x.Game.Id] = s; }
                    }
                    catch (Exception e) { if (Program.Testing) Program.Log("reading a replay", e); }
                    int n = System.Threading.Interlocked.Increment(ref done);
                    if (n % 6 == 0) report.Report(n);
                });
            });
            if (id != _loadId) return;
            foreach (var kv in found) _data.Smash[kv.Key] = kv.Value;
            _data.ReplaysLoading = false;
            var all = _h.Singles().ToList(); var smash = _data.Smash;
            _data.Career = await Task.Run(() => Progress.Compute(all, smash, MoveName));
            _data.Typical = new PlayerHabits(); _data.TypicalGames = 0;
            foreach (var s in smash.Values) if (s.TheirHabits != null && s.TheirHabits.Known) { _data.Typical.Add(s.TheirHabits); _data.TypicalGames++; }
            Profile(); RefreshViews(); ShowAll();
        }

        // ----- the coach: how to DI the hits that took your stocks, fun facts, a combo idea (worked out per game, on demand)
        readonly Dictionary<long, GameCoach> _coach = new Dictionary<long, GameCoach>();
        readonly HashSet<long> _coaching = new HashSet<long>();
        readonly Dictionary<int, FighterBook> _books = new Dictionary<int, FighterBook>();
        readonly List<int> _bookOrder = new List<int>();

        /// <summary>The coach's notes on a game: right away when they're worked out already; otherwise null, and
        /// <paramref name="ready"/> is called (on the window's thread) once they are.</summary>
        public GameCoach Coach(long gameId, Action<GameCoach> ready = null)
        {
            if (_coach.TryGetValue(gameId, out var done)) return done;
            if (!_data.Smash.TryGetValue(gameId, out var smash) || smash?.Replay == null) return null;
            if (ready != null) _coachWaiting[gameId] = ready;
            if (!_coaching.Add(gameId)) return null;
            var installer = _installer; Func<int, int, string> name = MoveName;
            Task.Run(() =>
            {
                var c = new GameCoach();
                try
                {
                    using (var card = installer()?.OpenCard())
                    {
                        if (card == null) return c;
                        FighterBook Book(int kind)
                        {
                            kind = Fighters.Same(kind);
                            lock (_books)
                            {
                                if (_books.TryGetValue(kind, out var b)) return b;
                                var ch = Characters.All.FirstOrDefault(x => string.Equals(x.Name, Fighters.Name(kind), StringComparison.OrdinalIgnoreCase));
                                try { b = ch == null ? null : FrameDataBook.Load(card, ch); if (b != null && b.Body == null) b.Body = Body.Of(b); } catch { b = null; }
                                _books[kind] = b; _bookOrder.Add(kind);
                                if (_bookOrder.Count > 6) { _books.Remove(_bookOrder[0]); _bookOrder.RemoveAt(0); }   // they're big: keep a few
                                return b;
                            }
                        }
                        try { c.Tips = DeathCoach.For(smash.Replay, smash.Me, Book, name); } catch (Exception e) { Program.Log("DI tips", e); }
                        try { c.Facts = FunFacts.For(smash, name); } catch (Exception e) { Program.Log("fun facts", e); }
                        try { c.Combo = FunFacts.ComboIdea(smash, Book(smash.MyCharacter), Book(smash.TheirCharacter), name); } catch (Exception e) { Program.Log("combo idea", e); }
                    }
                }
                catch (Exception e) { Program.Log("coach", e); }
                return c;
            }).ContinueWith(t =>
            {
                _coaching.Remove(gameId);
                var c = t.IsFaulted ? new GameCoach() : t.Result;
                _coach[gameId] = c;
                if (_coachWaiting.TryGetValue(gameId, out var r)) { _coachWaiting.Remove(gameId); try { r(c); } catch (Exception e) { Program.Log("coach", e); } }
                RefreshViews();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }
        readonly Dictionary<long, Action<GameCoach>> _coachWaiting = new Dictionary<long, Action<GameCoach>>();

        /// <summary>What a player tends to do, from your games against them (empty until the replays are read).</summary>
        public List<(string Habit, string Tip)> Scout(string userId, int max = 3)
        {
            if (userId == null || _data.Career == null) return new List<(string, string)>();
            var h = new PlayerHabits(); int n = 0;
            foreach (var s in _data.Smash.Values) if (s.ThemPlayer.UserId == userId && s.TheirHabits != null && s.TheirHabits.Known) { h.Add(s.TheirHabits); n++; }
            return Scouting.Lines(h, n, _data.Typical, _data.TypicalGames, max);
        }

        string MoveName(int character, int sub) { var t = TableOf(character); return t != null && sub >= 0 && sub < t.Names.Count ? MoveNames.Friendly(t.Names[sub]) : null; }

        void RefreshViews() { foreach (Control c in _body.Controls) if (c is ScrollView || c is CharacterChart) c.Invalidate(); }

        void ShowReplayFile(string file)
        {
            var path = Locate(file);
            if (path == null) { MessageBox.Show(this, "That replay isn't in your Documents\\Brawl Online folder anymore.", "Stat Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch (Exception e) { Program.Log("showing a replay", e); }
        }

        static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            return d.TotalHours < 1 ? "just now" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : d.TotalDays < 2 ? "yesterday" : $"{(int)d.TotalDays} days ago";
        }

        Image Head(int? c) => _art?.Head(c);
        Image Portrait(int? c) => _art?.Portrait(c);
        Image StagePic(int? s) => _art?.Stage(s);

        void Profile()
        {
            if (_h == null) return;
            var games = _data.Games;
            var first = games.Count > 0 ? games.First().Game.Start.ToLocalTime() : DateTime.Now;
            int? main = games.GroupBy(g => g.Me.Character).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
            var all = MatchHistory.Count(games);
            _profile.Top = "STAT TRACKER · FROM BRAWL ONLINE'S OWN RECORDS";
            _profile.Big = $"{_h.MyName}  ({_h.MyCode})";
            _profile.Small = _data.ReplayNote ?? (games.Count == 0 ? "No 1-on-1 games here yet." : $"{all} ({all.Percent}) in {games.Count} games since {first:MMM d}" + (main != null ? $" · plays {Fighters.Name(main.Value)} most" : ""));
            _profile.Side = Portrait(main); _profile.Invalidate();
        }

        void ShowAll()
        {
            if (_h == null) return;
            var games = _h.Singles(_ranked).ToList();
            _data.History = _h; _data.Games = games;
            _data.Head = Head; _data.Portrait = Portrait; _data.Stage = StagePic; _data.MoveName = MoveName; _data.Coach = id => Coach(id);
            Profile();
            foreach (Control b in _tabs.Controls) Theme.Toggle((Button)b, b.Text.TrimEnd(' ', '•') == _tab);
            _tools.Controls.Clear(); _tools.Height = 0;
            _side.Visible = _sideTitle.Visible = false;
            _table.RowClicked -= OpponentClicked; _table.RowClicked -= MatchupClicked;
            _table.Selected = null;
            bool table = _tab == "Matchups" || _tab == "Stages" || _tab == "Opponents" || _tab == "Win chance";
            _table.Visible = _tools.Visible = table;
            _overview.Visible = _tab == "Overview";
            _progress.Visible = _tab == "Progress";
            _chart.Visible = _charCard.Visible = _tab == "Characters";
            _matches.Visible = _matchCard.Visible = _tab == "Matches";
            switch (_tab)
            {
                case "Overview": _overview.Invalidate(); break;
                case "Progress": _progress.Invalidate(); break;
                case "Characters":
                    if (_charCard.Character == null || !games.Any(g => g.Me.Character == _charCard.Character))
                        _charCard.Character = games.GroupBy(g => g.Me.Character).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
                    _chart.Selected = _charCard.Character; _chart.Invalidate(); _charCard.Invalidate();
                    break;
                case "Matches":
                    if (_matchCard.Game == null || !games.Any(g => g.Game.Id == _matchCard.Game)) _matchCard.Game = games.Count > 0 ? games.Last().Game.Id : (long?)null;
                    _matches.Selected = _matchCard.Game; _matches.Invalidate(); _matchCard.Invalidate();
                    break;
                case "Stages":
                    _table.Show(_h.By(x => x.Game.Stage, _ranked).Select(x => new RecordTable.Row { Name = _h.StageName(x.Key), Record = x.Record, Icon = StagePic(x.Key), Wide = true, Note = Note(games.Where(g => g.Game.Stage == x.Key)) }).ToList());
                    break;
                case "Matchups": Matchups(games); break;
                case "Opponents": Opponents(games); break;
                case "Win chance": WinChance(); break;
            }
            Arrange();
        }

        static string Note(IEnumerable<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games)
        {
            var l = games.OrderByDescending(g => g.Game.Start).ToList();
            if (l.Count == 0) return null;
            var recent = MatchHistory.Count(l.Take(10));
            return $"last played {Ago(l[0].Game.Start)}" + (l.Count > 10 ? $" · last 10: {recent}" : "");
        }

        // ----- matchups -----

        int? _mine;
        void Matchups(List<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games)
        {
            var pick = Combo(220);
            pick.Items.Add(new Item { Text = "Any of your characters" });
            foreach (var c in _h.By(x => x.Me.Character, _ranked)) pick.Items.Add(new Item { Text = $"As {Fighters.Name(c.Key)} ({c.Record.Games})", Value = c.Key });
            pick.SelectedItem = pick.Items.Cast<Item>().FirstOrDefault(i => Equals(i.Value, _mine)) ?? pick.Items[0];
            pick.Location = new Point(0, 0);
            pick.SelectedIndexChanged += (s, e) => { _mine = (int?)((Item)pick.SelectedItem).Value; ShowAll(); };
            var hint = Theme.Label("Your record against each character, with anyone playing it. Click one for your best picks against it.", Theme.Small, Theme.Muted);
            hint.Location = new Point(236, 4);
            _tools.Controls.AddRange(new Control[] { pick, hint }); _tools.Height = 40;
            var rows = games.Where(g => _mine == null || g.Me.Character == _mine).GroupBy(g => g.Them.Character)
                .Select(g => new RecordTable.Row { Name = "vs " + Fighters.Name(g.Key), Record = MatchHistory.Count(g), Tag = g.Key, Note = Note(g), Icon = Head(g.Key) })
                .OrderByDescending(r => r.Record.Games).ToList();
            _table.Show(rows);
            _table.RowClicked += MatchupClicked;
        }

        void MatchupClicked(RecordTable.Row row)
        {
            int theirs = (int)row.Tag;
            _side.Visible = _sideTitle.Visible = true;
            _sideTitle.Text = "Against " + Fighters.Name(theirs);
            var rows = new List<RecordTable.Row> { new RecordTable.Row { Header = true, Name = "Your best picks against it" } };
            var picks = _h.Counterpicks(theirs, 4);
            if (picks.Count == 0) rows.Add(new RecordTable.Row { Name = "Not enough games yet", Note = "Play 2+ games with a character against it to see it here" });
            rows.AddRange(picks.Select(p => new RecordTable.Row { Name = Fighters.Name(p.Character), Record = p.Versus, Icon = Head(p.Character) }));
            rows.Add(new RecordTable.Row { Header = true, Name = "Your best stages against it" });
            rows.AddRange(_h.BestStages(null, theirs, null, 4).Where(s => s.Record.Games > 0).Select(s => new RecordTable.Row { Name = _h.StageName(s.Stage), Record = s.Record, Icon = StagePic(s.Stage), Wide = true }));
            _side.Show(rows);
            Arrange();
        }

        // ----- opponents -----

        void Opponents(List<(GameRecord Game, GamePlayer Me, GamePlayer Them)> games)
        {
            var rows = games.GroupBy(g => g.Them.UserId).Select(g =>
            {
                var last = g.OrderByDescending(x => x.Game.Start).First();
                var mains = g.GroupBy(x => x.Them.Character).OrderByDescending(x => x.Count()).ToList();
                return new RecordTable.Row { Name = $"{last.Them.Name}  ({last.Them.Code})", Record = MatchHistory.Count(g), Tag = g.Key, Icon = Head(mains[0].Key),
                    Note = $"plays {string.Join(", ", mains.Take(2).Select(x => Fighters.Name(x.Key)))} · last played {Ago(last.Game.Start)}" };
            }).OrderByDescending(r => r.Record.Games).ToList();
            _table.Show(rows);
            _table.RowClicked += OpponentClicked;
        }

        void OpponentClicked(RecordTable.Row row)
        {
            string id = (string)row.Tag;
            var games = _h.Singles(_ranked).Where(g => g.Them.UserId == id).ToList();
            _side.Visible = _sideTitle.Visible = true;
            _sideTitle.Text = games.Last().Them.Name;
            var rows = new List<RecordTable.Row>();
            var scout = Scout(id, 4);
            if (scout.Count > 0)
            {
                rows.Add(new RecordTable.Row { Header = true, Name = "What they tend to do" });
                rows.AddRange(scout.Select(l => new RecordTable.Row { Name = l.Habit, Note = "→ " + l.Tip }));
            }
            rows.Add(new RecordTable.Row { Header = true, Name = "What they play against you" });
            rows.AddRange(games.GroupBy(g => g.Them.Character).OrderByDescending(g => g.Count()).Select(g => new RecordTable.Row { Name = Fighters.Name(g.Key), Note = $"{g.Count()} of {games.Count} games", Record = MatchHistory.Count(g), Icon = Head(g.Key) }));
            rows.Add(new RecordTable.Row { Header = true, Name = "Your stages against them" });
            rows.AddRange(games.GroupBy(g => g.Game.Stage).OrderByDescending(g => g.Count()).Select(g => new RecordTable.Row { Name = _h.StageName(g.Key), Record = MatchHistory.Count(g), Icon = StagePic(g.Key), Wide = true }));
            rows.Add(new RecordTable.Row { Header = true, Name = "Your characters against them" });
            rows.AddRange(games.GroupBy(g => g.Me.Character).OrderByDescending(g => g.Count()).Select(g => new RecordTable.Row { Name = Fighters.Name(g.Key), Record = MatchHistory.Count(g), Icon = Head(g.Key) }));
            _side.Show(rows);
            Arrange();
        }

        // ----- win chance -----

        int? _wcMine, _wcTheirs, _wcStage; string _wcOpp;
        void WinChance()
        {
            void Pick(string label, int x, IEnumerable<Item> items, object current, Action<object> set)
            {
                var l = Theme.Label(label, Theme.Small, Theme.Muted); l.Location = new Point(x, 0);
                var c = Combo(200); c.Location = new Point(x, 18);
                c.Items.Add(new Item { Text = "(any)" });
                foreach (var i in items) c.Items.Add(i);
                c.SelectedItem = c.Items.Cast<Item>().FirstOrDefault(i => Equals(i.Value, current)) ?? c.Items[0];
                c.SelectedIndexChanged += (s, e) => { set(((Item)c.SelectedItem).Value); ShowAll(); };
                _tools.Controls.AddRange(new Control[] { l, c });
            }
            var chars = Enumerable.Range(0, 60).Select(Fighters.Same).Distinct().Where(i => !Fighters.Name(i).StartsWith("Character ")).OrderBy(Fighters.Name).Select(i => new Item { Text = Fighters.Name(i), Value = i }).ToList();
            Pick("You play", 0, chars, _wcMine, v => _wcMine = (int?)v);
            Pick("They play", 212, chars, _wcTheirs, v => _wcTheirs = (int?)v);
            var opps = _h.Singles().GroupBy(g => g.Them.UserId).OrderByDescending(g => g.Count()).Select(g => new Item { Text = $"{g.Last().Them.Name} ({g.Count()})", Value = g.Key });
            Pick("Opponent", 424, opps, _wcOpp, v => _wcOpp = (string)v);
            var stages = _h.Singles().Select(g => g.Game.Stage).Distinct().OrderBy(_h.StageName).Select(s => new Item { Text = _h.StageName(s), Value = s });
            Pick("Stage", 636, stages, _wcStage, v => _wcStage = (int?)v);

            var p = _h.Project(_wcMine, _wcTheirs, _wcOpp, _wcStage);
            string oppName = _wcOpp != null ? _h.Singles().Last(g => g.Them.UserId == _wcOpp).Them.Name : null;
            var face = new Faceoff
            {
                Location = new Point(0, 56), Size = new Size(_body.Width, 190), Chance = p.Chance, Few = p.Evidence < 15,
                Mine = Portrait(_wcMine), Theirs = Portrait(_wcTheirs), Stage = StagePic(_wcStage),
                MineName = _wcMine != null ? Fighters.Name(_wcMine.Value) : null, TheirsName = _wcTheirs != null ? Fighters.Name(_wcTheirs.Value) : null,
                Line = (oppName != null ? "against " + oppName : "against anyone") + (_wcStage != null ? " · on " + _h.StageName(_wcStage.Value) : "") + $" · from {p.Evidence} matching games",
            };
            _tools.Controls.Add(face); _tools.Height = 256;

            var rows = new List<RecordTable.Row> { new RecordTable.Row { Header = true, Name = "What it's based on" }, new RecordTable.Row { Name = "Your overall record", Record = _h.Overall(), Note = "where every projection starts" } };
            foreach (var f in p.Factors)
                rows.Add(new RecordTable.Row { Name = f.What, Record = f.Record, Note = f.Record.Games == 0 ? "no games yet: doesn't change it" : $"moves it {(f.Effect >= 0 ? "up" : "down")}" });
            if (_wcTheirs != null)
            {
                rows.Add(new RecordTable.Row { Header = true, Name = $"Your best picks against {Fighters.Name(_wcTheirs.Value)}" });
                var picks = _h.Counterpicks(_wcTheirs.Value, 3);
                if (picks.Count == 0) rows.Add(new RecordTable.Row { Name = "Not enough games yet", Note = "Play 2+ games with a character against it to see it here" });
                rows.AddRange(picks.Select(c => new RecordTable.Row { Name = Fighters.Name(c.Character), Record = c.Versus, Icon = Head(c.Character) }));
            }
            rows.Add(new RecordTable.Row { Header = true, Name = _wcOpp != null || _wcTheirs != null ? "Your best stages for this" : "Your best stages" });
            rows.AddRange(_h.BestStages(null, _wcTheirs, _wcOpp, 3).Where(s => s.Record.Games > 0).Select(s => new RecordTable.Row { Name = _h.StageName(s.Stage), Record = s.Record, Icon = StagePic(s.Stage), Wide = true }));
            _table.Show(rows);
        }
    }
}
