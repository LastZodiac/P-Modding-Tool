// The main window: a sidebar (My Mods, Mod Store, Costume Slots) and the page you picked.
// Switching skins on and off is collected first and written to the SD card in one go ("Save").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class MainForm : Form, IModHost
    {
        BrawlOnlineInstall _install;
        Library _lib;
        Installer _installer;
        readonly Label _status = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Size = new Size(760, 18), BackColor = Color.Transparent };
        readonly Panel _banner = new Panel();
        readonly Label _bannerText = Theme.Label("", Theme.Body, Theme.AccentText);
        readonly Panel _appBanner = new Panel();
        readonly Label _appText = Theme.Label("", Theme.Body, Theme.AccentText);
        OnlineUpdate.Release _appRelease;
        readonly Panel _unsafeBanner = new Panel();
        readonly Label _unsafeText = Theme.Label("", Theme.Body, Color.White);
        List<InstalledStage> _unsafe = new List<InstalledStage>();
        readonly Button _addButton = Theme.Primary("Add from file...");
        readonly Button _profileButton = Theme.Secondary("Profile");
        readonly Button _helpButton = Theme.Secondary("?");
        TutorialOverlay _tour;
        readonly Button _launchModded = Theme.Primary("Launch modded");
        readonly Button _launchClean = Theme.Secondary("Launch unmodded");
        Action _placeHeader = () => { };
        List<InstalledSkin> _overwritten = new List<InstalledSkin>();
        List<InstalledStage> _overwrittenStages = new List<InstalledStage>();
        bool _busy;
        readonly string _openOnStart;   // a skin dropped on the app's icon

        // pages
        readonly GlassPanel _content = new GlassPanel { Dock = DockStyle.Fill };
        readonly LibraryPanel _library;
        readonly BrowsePanel _store;
        readonly GlassPanel _slotsPage = new GlassPanel { Dock = DockStyle.Fill };
        SettingsPanel _settings;
        StatsPanel _stats;
        FrameDataPanel _frameData;
        LiveCard _card;
        LogFollower _log;
        LiveMatch _live = new LiveMatch();
        MatchHistory _cardHistory;
        DateTime _reloadAt = DateTime.MaxValue;
        bool _historyLoading;

        FullOverlay _full;
        ReportCard _report;
        DateTime _checkAgainAt = DateTime.MaxValue;

        /// <summary>A game just ended: its report over the game (if the overlay is on), and the Stat Tracker shows it.</summary>
        void ShowReport(long gameId)
        {
            try
            {
                var d = _stats.Data;
                Program.Trace($"report for game {gameId}: career {(d.Career == null ? "not ready" : "ready")}");
                if (d.Career == null || !d.Career.Games.TryGetValue(gameId, out var p)) return;
                var x = d.History.Singles().FirstOrDefault(v => v.Game.Id == gameId);
                if (x.Game == null) return;
                d.Smash.TryGetValue(gameId, out var smash);
                _stats.MarkNew(gameId);
                bool test = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") != null;
                if (!(_look.Stats && _look.Card) && !test) return;
                if (_report == null) _report = new ReportCard();
                _report.Game = _game ?? (_install != null ? GameWindow.Find(_install) : null);
                _report.Show(p, smash, x.Me, x.Them, d.History.StageName(x.Game.Stage), d.MoveName, StatsArt.Cached);
            }
            catch (Exception e) { Program.Log("after-game report", e); }
        }
        DateTime _pillAt = DateTime.MaxValue, _gameLookAt;
        Rectangle? _game;

        /// <summary>Reads what the game wrote since last time and shows, updates or hides the overlay.</summary>
        void LiveTick()
        {
            try
            {
                if (!_look.Stats || !_look.Card || _install == null) { _card?.Hide(); _full?.Hide(); return; }
                if (_log == null) _log = new LogFollower(LogFollower.LogPath(_install));
                bool changed = false;
                foreach (var line in _log.NewLines()) changed |= _live.Feed(line);
                var s = _live.State;
                if (DateTime.Now >= _reloadAt) { _reloadAt = DateTime.MaxValue; _cardHistory = null; changed = true; _stats.CheckNewGame(); _checkAgainAt = DateTime.Now.AddSeconds(12); }
                if (DateTime.Now >= _checkAgainAt) { _checkAgainAt = DateTime.MaxValue; _stats.CheckNewGame(); }   // in case the game was saved late
                // where the game is (it can be moved or resized): checked every 2 seconds
                if (DateTime.Now >= _gameLookAt)
                {
                    _gameLookAt = DateTime.Now.AddSeconds(2);
                    var g = GameWindow.Find(_install);
                    if (g != _game) { _game = g; if (_card != null) { _card.Game = g; if (_card.Visible) _card.Place(); } if (_full != null && _full.Visible) _full.Place(g); }
                }
                if (changed)
                {
                    if (_cardHistory == null && !_historyLoading) LoadCardHistory();
                    if (s.Phase == MatchPhase.Playing) _pillAt = DateTime.Now.AddSeconds(12);       // the slim bar soon after the fight starts
                    else _pillAt = DateTime.MaxValue;
                    if (s.Phase == MatchPhase.GameOver) _reloadAt = DateTime.Now.AddSeconds(4);       // Brawl Online has saved the game by then
                    bool show = s.Phase == MatchPhase.Found || s.Phase == MatchPhase.Loading || s.Phase == MatchPhase.Playing || s.Phase == MatchPhase.GameOver;
                    if (show && s.OpponentName != null)
                    {
                        if (_card == null) _card = new LiveCard { Scout = id => _stats.Scout(id, 2) };
                        _card.Game = _game;
                        if (s.Phase != MatchPhase.Playing) _card.Pill = false;
                        _card.State = s.Copy(); _card.History = _cardHistory; if (_card.Art == null) _card.Art = StatsArt.Cached;
                        _card.Refresh2(); if (!_card.Visible) _card.Show();
                    }
                    else _card?.Hide();
                    if (s.Phase == MatchPhase.Found || s.Phase == MatchPhase.Loading) _report?.Hide();   // on to the next game
                    if (_full != null && _full.Visible) { _full.State = s.Copy(); _full.History = _cardHistory; _full.Invalidate(); }
                }
                if (_card != null && _card.Visible && DateTime.Now >= _pillAt) { _pillAt = DateTime.MaxValue; _card.Pill = true; _card.Refresh2(); }
                var flag = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_FULL_FLAG");   // UI tests: the hotkey, by a file
                if (flag != null && System.IO.File.Exists(flag) != (_full != null && _full.Visible)) ToggleFull();
            }
            catch (Exception e) { Program.Log("overlay", e); }
        }

        /// <summary>The hotkey: shows or hides the full overlay over the game.</summary>
        void ToggleFull()
        {
            if (!_look.Stats || _install == null) return;
            if (_full == null) _full = new FullOverlay { Scout = id => _stats.Scout(id, 3) };
            if (_full.Visible) { _full.Hide(); return; }
            if (_cardHistory == null && !_historyLoading) LoadCardHistory();
            _full.Hotkey = _look.HotkeyText; _full.State = _live.State.Copy(); _full.History = _cardHistory; _full.Art = StatsArt.Cached;
            _full.Place(_game ?? GameWindow.Find(_install));
            _full.Show(); _full.Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Hotkeys.WM_HOTKEY && (int)m.WParam == Hotkeys.Id) ToggleFull();
            base.WndProc(ref m);
        }

        /// <summary>(Re)registers the overlay hotkey from the settings.</summary>
        public void ApplyHotkey()
        {
            bool ok = Hotkeys.Set(Handle, _look.Stats && _look.Card ? _look.HotkeyText : "Off");
            if (!ok) Program.Trace("hotkey " + _look.HotkeyText + " is taken by another program");
        }

        async void LoadCardHistory()
        {
            _historyLoading = true;
            try
            {
                var install = _install; var installer = _installer;
                _cardHistory = await System.Threading.Tasks.Task.Run(() =>
                {
                    Dictionary<int, string> names;
                    try { using (var card = installer?.OpenCard()) names = card != null ? StageSelect.SlotListNames(card) : new Dictionary<int, string>(); } catch { names = new Dictionary<int, string>(); }
                    var path = MatchHistory.DatabasePath(install);
                    return System.IO.File.Exists(path) ? MatchHistory.Load(path, names) : null;
                });
                var art = await System.Threading.Tasks.Task.Run(() => StatsArt.Get(() => _installer));
                if (_card != null) { _card.History = _cardHistory; _card.Art = art; _card.Refresh2(); }
                if (_full != null) { _full.History = _cardHistory; _full.Art = art; _full.Invalidate(); }
            }
            catch (Exception e) { Program.Log("opponent card history", e); }
            finally { _historyLoading = false; }
        }
        Action _statsToggle;
        CreditsPanel _credits;
        readonly Backdrop _backdrop = new Backdrop();
        LookSettings _look = LookSettings.Load();
        readonly Dictionary<Control, NavButton> _nav = new Dictionary<Control, NavButton>();

        // costume slots page
        readonly ListBox _characters = new ListBox();
        readonly GlassFlow _slots = new GlassFlow();
        readonly Label _charTitle = Theme.Label("", Theme.Title);
        readonly Label _charHint = Theme.Label("", Theme.Small, Theme.Muted);
        readonly Dictionary<int, Dictionary<string, Bitmap>> _pictureCache = new Dictionary<int, Dictionary<string, Bitmap>>();

        // switches not saved to the card yet: skin id -> wanted on
        readonly Dictionary<string, bool> _pending = new Dictionary<string, bool>();
        readonly Panel _saveBar = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Accent, Visible = false };
        readonly Label _saveText = Theme.Label("", Theme.BodyBold, Theme.AccentText);

        public MainForm(string openOnStart = null)
        {
            _openOnStart = openOnStart;
            Text = "P+ Modding Tool";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1240, 820); MinimumSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AllowDrop = true;
            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) =>
            {
                if (!(e.Data.GetData(DataFormats.FileDrop) is string[] f) || f.Length == 0) return;
                if (f.Any(MusicImport.IsAudio)) AddOwnMusic(f.Where(MusicImport.IsAudio).ToList());   // your own songs (MP3/WAV)
                else AddSkin(f[0]);
            };

            // header
            var header = new GlassPanel { Dock = DockStyle.Top, Height = 64, Tint = 150 };
            _header = header;
            var title = Theme.Label("P+ Modding Tool", Theme.Title);
            var logo = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(42, 42), Location = new Point(16, 11), BackColor = Color.Transparent };
            title.Location = new Point(66, 8);
            _status.Location = new Point(68, 38);
            header.Controls.Add(logo);
            Icon = Theme.AppIcon ?? Icon;
            _addButton.Click += (s, e) => PickSkin();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 14, 14, 0) };
            _addButton.Margin = new Padding(10, 0, 0, 0); _profileButton.Margin = new Padding(0);
            _launchModded.Margin = new Padding(0, 0, 10, 0); _launchClean.Margin = new Padding(0, 0, 10, 0);
            buttons.Controls.Add(_helpButton); buttons.Controls.Add(_addButton); buttons.Controls.Add(_profileButton);
            _helpButton.Margin = new Padding(10, 0, 0, 0); _helpButton.AutoSize = false; _helpButton.Size = new Size(36, 36); _helpButton.Font = Theme.BodyBold;
            _helpButton.Click += (s, e) => ShowTour(true);
            new ToolTip().SetToolTip(_helpButton, "A short tour of this page"); buttons.Controls.Add(_launchModded); buttons.Controls.Add(_launchClean);
            foreach (var b in new[] { _launchModded, _launchClean })
            {
                b.AutoSize = false; b.Padding = new Padding(0);
                b.MinimumSize = b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + 40, 36);
            }
            _launchModded.Click += (s, e) => Launch(true);
            _launchClean.Click += (s, e) => Launch(false);
            header.Controls.Add(title); header.Controls.Add(_status); header.Controls.Add(buttons);
            buttons.BringToFront();   // the status line under the title must not cover the buttons
            _profileButton.Click += (s, e) => ShowProfileMenu();
            _profileButton.Enabled = false;
            _addButton.AutoSize = false;
            _addButton.Size = new Size(TextRenderer.MeasureText(_addButton.Text, _addButton.Font).Width + 40, 36);

            // banner shown after a P+ update replaced skins
            _banner.Dock = DockStyle.Top; _banner.Height = 44; _banner.BackColor = Theme.Accent; _banner.Visible = false;
            _bannerText.Location = new Point(18, 12);
            var putBack = new Button { Text = "Put them back", FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentText, ForeColor = Theme.Accent, Font = Theme.BodyBold, AutoSize = true, Cursor = Cursors.Hand };
            putBack.FlatAppearance.BorderSize = 0;
            putBack.Click += (s, e) => { var c = new Installer.Change { Reapply = _overwritten.ToList() }; c.StageReapply.AddRange(_overwrittenStages); Run(c, "Your mods were checked against the updated P+ and put back where they still fit."); };
            _banner.Controls.Add(_bannerText); _banner.Controls.Add(putBack);
            _banner.Resize += (s, e) => putBack.Location = new Point(_banner.Width - putBack.Width - 18, (_banner.Height - putBack.Height) / 2);

            // banner for a new version of the app
            _appBanner.Dock = DockStyle.Top; _appBanner.Height = 44; _appBanner.BackColor = Theme.Accent; _appBanner.Visible = false;
            _appText.Location = new Point(18, 12); _appText.AutoSize = false; _appText.Height = 22;
            var updateNow = new Button { Text = "Update now", FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentText, ForeColor = Theme.Accent, Font = Theme.BodyBold, AutoSize = true, Cursor = Cursors.Hand };
            updateNow.FlatAppearance.BorderSize = 0;
            var whatsNew = new LinkLabel { Text = "What's new", AutoSize = true, Font = Theme.Body, LinkColor = Theme.AccentText, ActiveLinkColor = Theme.AccentText, BackColor = Color.Transparent };
            whatsNew.LinkClicked += (s, e) => { if (_appRelease?.Page != null) try { Process.Start(new ProcessStartInfo(_appRelease.Page) { UseShellExecute = true }); } catch { } };
            updateNow.Click += async (s, e) => await UpdateApp(updateNow);
            _appBanner.Controls.Add(_appText); _appBanner.Controls.Add(whatsNew); _appBanner.Controls.Add(updateNow);
            _appBanner.Resize += (s, e) =>
            {
                updateNow.Location = new Point(_appBanner.Width - updateNow.Width - 18, (_appBanner.Height - updateNow.Height) / 2);
                whatsNew.Location = new Point(updateNow.Left - whatsNew.Width - 16, 13);
                _appText.Width = whatsNew.Left - 30;
            };

            // banner for stage mods that don't pass today's safety check
            _unsafeBanner.Dock = DockStyle.Top; _unsafeBanner.Height = 44; _unsafeBanner.BackColor = Theme.Bad; _unsafeBanner.Visible = false;
            _unsafeText.Location = new Point(18, 12); _unsafeText.AutoSize = false; _unsafeText.Height = 22;
            var turnOff = new Button { Text = "Turn them off", FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Bad, Font = Theme.BodyBold, AutoSize = true, Cursor = Cursors.Hand };
            turnOff.FlatAppearance.BorderSize = 0;
            turnOff.Click += (s, e) =>
            {
                var c = new Installer.Change(); c.StageDisable.AddRange(_unsafe);
                Run(c, "Turned off. To keep their looks, open My Mods, use \"Add more from this download\" on them and pick \"Use its looks on P+'s ...\": that version is safe online.");
                _unsafeBanner.Visible = false;
            };
            _unsafeBanner.Controls.Add(_unsafeText); _unsafeBanner.Controls.Add(turnOff);
            _unsafeBanner.Resize += (s, e) => { turnOff.Location = new Point(_unsafeBanner.Width - turnOff.Width - 18, (_unsafeBanner.Height - turnOff.Height) / 2); _unsafeText.Width = turnOff.Left - 30; };

            // save bar
            _saveText.Location = new Point(20, 17);
            var save = new Button { Text = "Save to SD card", FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentText, ForeColor = Theme.Accent, Font = Theme.BodyBold, AutoSize = true, Padding = new Padding(12, 4, 12, 4), Cursor = Cursors.Hand };
            save.FlatAppearance.BorderSize = 0;
            save.Click += (s, e) => SavePending();
            var undo = new Button { Text = "Undo", FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Theme.AccentText, Font = Theme.Body, AutoSize = true, Cursor = Cursors.Hand };
            undo.FlatAppearance.BorderColor = Theme.AccentText;
            undo.Click += (s, e) => { _pending.Clear(); PendingChanged(); };
            _saveBar.Controls.Add(_saveText); _saveBar.Controls.Add(save); _saveBar.Controls.Add(undo);
            _saveBar.Resize += (s, e) =>
            {
                save.Location = new Point(_saveBar.Width - save.Width - 18, (_saveBar.Height - save.Height) / 2);
                undo.Location = new Point(save.Left - undo.Width - 10, (_saveBar.Height - undo.Height) / 2);
            };

            // pages
            _library = new LibraryPanel(this);
            _store = new BrowsePanel(() => _lib, Program.DataFolder) { Visible = false };
            _store.Favs = _favs;
            _store.InstallRequested += (path, source) => AddSkin(path, source);
            _store.InstallAllRequested += (paths, source) => AddSkin(Path.GetDirectoryName(paths[0]), source, paths);
            _store.TurnOffRequested += skin => Run(new Installer.Change { Disable = { skin } }, $"Turned off. {skin.Character.Name}'s costume {skin.SlotIndex + 1} is P+'s again ({skin.Name} stays in My Mods).");
            BuildSlotsPage();
            _swap = new SwapPanel(() => _installer, folder => AddSkin(folder)) { Visible = false };
            _settings = new SettingsPanel(_look, () => { _backdrop.Apply(_look); _statsToggle?.Invoke(); ApplyHotkey(); Invalidate(true); }) { Visible = false };
            _content.Controls.Add(_library); _content.Controls.Add(_store); _content.Controls.Add(_slotsPage); _content.Controls.Add(_swap); _content.Controls.Add(_settings);
            _credits = new CreditsPanel { Visible = false }; _content.Controls.Add(_credits);
            _stats = new StatsPanel(() => _install, () => _installer) { Visible = false }; _content.Controls.Add(_stats);
            _stats.NewGame += ShowReport;
            _frameData = new FrameDataPanel(() => _installer) { Visible = false }; _content.Controls.Add(_frameData);

            // sidebar
            var side = new GlassPanel { Dock = DockStyle.Left, Width = 208, Tint = 170 };
            int y = 18;
            void Section(string text) { var l = Theme.Label(text, Theme.Small, Theme.Muted); l.Location = new Point(22, y); side.Controls.Add(l); y += 24; }
            void Nav(string text, string icon, Control page)
            {
                var b = new NavButton { Text = text, Icon = icon, Location = new Point(12, y) };
                b.Click += (s, e) => ShowPage(page);
                side.Controls.Add(b); _nav[page] = b; y += 44;
            }
            Section("MODS");
            Nav("My Mods", "\uE8F1", _library);
            Nav("Mod Store", "\uE719", _store);
            Nav("Costume Slots", "\uE77B", _slotsPage);
            Nav("Model Swapper", "\uE7AD", _swap);
            y += 12;
            Section("GENERAL");
            Nav("Settings", "\uE713", _settings);
            Nav("Credits", "\uE734", _credits);
            y += 12;
            var playLabel = Theme.Label("PLAY", Theme.Small, Theme.Muted); playLabel.Location = new Point(22, y); side.Controls.Add(playLabel); y += 24;
            Nav("Frame Data", "\uE916", _frameData);
            Nav("Stat Tracker", "\uE9D2", _stats);
            void StatsShown() { _nav[_stats].Visible = _look.Stats; if (!_look.Stats && _stats.Visible) ShowPage(_library); }
            _statsToggle = StatsShown; StatsShown();
            // the opponent card: follows the game's log while the app is open
            var liveTimer = new Timer { Interval = 500 };
            liveTimer.Tick += (s2, e2) => LiveTick();
            liveTimer.Start();

            Controls.Add(_content); Controls.Add(side); Controls.Add(_saveBar); Controls.Add(_banner); Controls.Add(_unsafeBanner); Controls.Add(_appBanner); Controls.Add(header);
            ShowPage(_library);
            DoubleBuffered = true;
            _backdrop.Apply(_look);
            Resize += (s, e) => Invalidate(true);
            Shown += (s, e) => { ApplyHotkey(); Startup(); };
            Activated += (s, e) => { if (_install != null && !_busy) UpdateStatus(); };
            FormClosing += (s, e) =>
            {
                if (_pending.Count == 0 || _busy) return;
                var answer = MessageBox.Show(this, $"You switched {_pending.Count} skin(s) on or off but didn't save that to your SD card yet.\r\n\r\nSave before closing?", "Save changes?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
                if (answer == DialogResult.Yes) { e.Cancel = true; if (SavePending()) BeginInvoke((Action)Close); }
                else _pending.Clear();
            };
        }

        protected override void OnPaintBackground(PaintEventArgs e) => _backdrop.Paint(e.Graphics, ClientSize);

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Stops a window drawing while it changes, then draws it once: page switches without flicker.</summary>
        static void Frozen(Control c, Action change)
        {
            bool frozen = false;
            try { if (c.IsHandleCreated) { SendMessage(c.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); frozen = true; } } catch { }   // WM_SETREDRAW off
            try { change(); }
            finally
            {
                if (frozen) { try { SendMessage(c.Handle, 0x000B, (IntPtr)1, IntPtr.Zero); } catch { } c.Invalidate(true); c.Update(); }
            }
        }

        Control _shownPage;
        Control _header;
        string PageName(Control page) =>
            page == _library ? "My Mods" : page == _store ? "Mod Store" : page == _slotsPage ? "Costume Slots" : page == _swap ? "Model Swapper"
            : page == _settings ? "Settings" : page == _frameData ? "Frame Data" : page == _stats ? "Stat Tracker" : page == _credits ? "Credits" : null;

        /// <summary>The tour of the page on show (asked: with the "?" button; otherwise the first time).</summary>
        void ShowTour(bool asked)
        {
            if (_tour != null || _shownPage == null || !Visible || WindowState == FormWindowState.Minimized || OwnedForms.Any(f => f.Visible) || Program.Testing && !asked && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_TOURS") == null) return;
            string name = PageName(_shownPage);
            var steps = name == null ? null : Tutorials.For(name, _shownPage, _header);
            if (steps == null || steps.Count == 0) { if (asked) MessageBox.Show(this, "This page has no tour.", "Tour", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!_look.ToursSeen.Contains(name)) { _look.ToursSeen.Add(name); _look.Save(); }
            _tour = new TutorialOverlay(name, steps);
            _tour.Finished += () => _tour = null;
            _tour.TurnedOff += () => { _look.Tours = false; _look.Save(); };
            _tour.Start(this);
        }

        void ShowPage(Control page)
        {
            Frozen(_content, () =>
            {
                foreach (var kv in _nav)
                {
                    bool on = kv.Key == page;
                    kv.Key.Visible = on;
                    kv.Value.Selected = on; kv.Value.Invalidate();
                }
            });
            if (page == _store) _store.ShownFirstTime();
            if (page == _slotsPage) ShowCharacter();
            _shownPage = page;
            if (_look.Tours && !_look.ToursSeen.Contains(PageName(page)))
            {
                // once the page has drawn (and read what it shows: Frame Data, Stat Tracker)
                var t = new Timer { Interval = 700 }; int waited = 0;
                t.Tick += (s, e) =>
                {
                    waited += t.Interval;
                    bool ready = page == _frameData ? _frameData.Ready : page == _stats ? _stats.Ready : true;
                    if (_shownPage != page || ready || waited > 30000) { t.Stop(); t.Dispose(); if (_shownPage == page) ShowTour(false); }
                };
                t.Start();
            }
        }

        void BuildSlotsPage()
        {
            var left = new GlassPanel { Dock = DockStyle.Left, Width = 210, Tint = 210, TintColor = Theme.Back, Padding = new Padding(10, 14, 0, 10) };
            var pick = Theme.Label("CHARACTERS", Theme.Small, Theme.Muted); pick.Dock = DockStyle.Top; pick.Padding = new Padding(6, 0, 0, 8);
            _characters.Dock = DockStyle.Fill; _characters.BorderStyle = BorderStyle.None; _characters.BackColor = Theme.Back;
            _characters.ForeColor = Theme.Text; _characters.Font = Theme.Body; _characters.DrawMode = DrawMode.OwnerDrawFixed; _characters.ItemHeight = 30;
            _characters.DrawItem += DrawCharacter;
            _characters.SelectedIndexChanged += (s, e) => ShowCharacter();
            left.Controls.Add(_characters); left.Controls.Add(pick);

            var right = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 14, 12, 12) };
            var top = new Panel { Dock = DockStyle.Top, Height = 74 };
            _charTitle.Location = new Point(0, 0); _charHint.Location = new Point(2, 34);
            _charHint.MaximumSize = new Size(820, 0);
            top.Resize += (s, e) => _charHint.MaximumSize = new Size(Math.Max(300, top.Width - 20), 0);
            top.Controls.Add(_charTitle); top.Controls.Add(_charHint);
            _slots.Dock = DockStyle.Fill; _slots.AutoScroll = true; _slots.Padding = new Padding(0, 4, 0, 0);
            right.Controls.Add(_slots); right.Controls.Add(top);
            _slotsPage.Controls.Add(right); _slotsPage.Controls.Add(left);
        }

        void Startup()
        {
            Program.Trace("startup");
            _install = BrawlOnlineInstall.Find();
            Program.Trace("brawl online: " + (_install?.NetplayFolder ?? "not found"));
            if (_install == null)
            {
                _status.Text = "Brawl Online was not found on this PC.";
                _status.ForeColor = Theme.Bad;
                _addButton.Enabled = false;
                MessageBox.Show(this, "Brawl Online isn't set up on this PC yet.\r\n\r\nOpen Brawl Online once and let it finish setting up Project+, then open this app again.",
                    "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _cards = new Cards(_install, Program.DataFolder);
            _textures = new TexturePacks(_install, Program.DataFolder);
            LastSave.RemoveOldBackup(_install.CardPath);   // older versions kept a 2 GB copy next to the card
            _lib = new Library(_cards.LibraryFolder(_cards.Active));
            _lib.EnsureProfiles();
            UpdateProfileButton();
            _profileButton.Enabled = true;
            Program.Trace("library: " + _lib.Skins.Count + " skins");
            _installer = NewInstaller();
            Program.Trace("installer ready");
            // the Stat Tracker reads your games in the background, so a game played from now on gets its report
            if (_look.Stats || Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") != null) _stats.CheckNewGame();
            foreach (var c in Characters.All.OrderBy(c => c.Name)) _characters.Items.Add(c);
            _characters.SelectedIndex = 0;
            _library.Refresh();
            UpdateStatus();
            CheckOverwritten();
            CheckVersions();
            CheckModUpdates();
            CheckAppUpdate(false);
            BeginInvoke((Action)(() => SelfUpdate.OfferShortcuts(this)));
            if (_openOnStart != null) BeginInvoke((Action)(() => AddSkin(_openOnStart)));
        }

        void UpdateStatus()
        {
            try
            {
                using (var card = _installer.OpenCard()) _space = SkinManager.Core.Space.Measure(card, _lib);
                bool running = _install.IsRunning(out string what);
                _library.ShowSpace(_space);
                if (_updateNote != null && !running) { _status.Text = $"{(_cards?.HasBig == true ? Cards.Label(_cards.Active) : "Your P+ SD card")}: {SkinManager.Core.Space.MB(_space.Usable)} free for mods   •   {_updateNote}"; _status.ForeColor = Theme.Warn; return; }
                _status.Text = $"{(_cards?.HasBig == true ? Cards.Label(_cards.Active) : "Your P+ SD card")}: {SkinManager.Core.Space.MB(_space.Usable)} free for mods   •   " +
                    (running ? $"Close {what} to change skins" : "Ready");
                _status.ForeColor = running ? Theme.Warn : Theme.Muted;
            }
            catch (Exception e) { _status.Text = "Could not read the SD card: " + e.Message; _status.ForeColor = Theme.Bad; }
        }

        /// <summary>Looks for a newer version of the app on its releases page (asked: right now, and says so when there's none).</summary>
        public void CheckAppUpdate(bool asked)
        {
            if (OnlineUpdate.Repo == null) { if (asked) MessageBox.Show(this, "This copy of the app doesn't have a releases page set, so it can't look for new versions.", "App updates"); return; }
            if (!asked && !OnlineUpdate.Due()) return;
            Task.Run(() => OnlineUpdate.Check()).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) { Program.Log("checking for a new version", t.Exception); if (asked) MessageBox.Show(this, "Couldn't reach the releases page. Check your internet and try again.", "App updates"); return; }
                _appRelease = t.Result;
                if (_appRelease == null) { if (asked) MessageBox.Show(this, $"You have the newest version ({OnlineUpdate.CurrentText}).", "App updates"); return; }
                _appText.Text = $"P+ Modding Tool {_appRelease.Version.ToString(3)} is out (you have {OnlineUpdate.CurrentText}). Your mods and SD card aren't touched by updating.";
                _appBanner.Visible = true;
                _appBanner.PerformLayout(); _appBanner.Width += 0;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        async Task UpdateApp(Button button)
        {
            if (_appRelease == null) return;
            button.Enabled = false;
            string before = _appText.Text;
            try
            {
                var progress = new Progress<int>(p => _appText.Text = $"Downloading the new version... {p}%");
                IProgress<int> pr = progress;
                await Task.Run(() => OnlineUpdate.Download(_appRelease, p => pr.Report(p)));
                _appText.Text = "Downloaded and checked. Restarting the app...";
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
                Close();
            }
            catch (Exception e)
            {
                Program.Log("updating the app", e);
                _appText.Text = before;
                MessageBox.Show(this, "The update couldn't be downloaded: " + e.Message + "\r\n\r\nNothing was changed. You can try again later.", "App updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                button.Enabled = true;
            }
        }

        void CheckUnsafe()
        {
            if (_lib.Stages.Count == 0 || OnCleanCard) { _unsafeBanner.Visible = false; return; }
            Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.UnsafeStages(card); })
                .ContinueWith(t =>
                {
                    if (t.IsFaulted || IsDisposed) return;
                    _unsafe = t.Result.Select(x => x.Stage).ToList();
                    _unsafeBanner.Visible = _unsafe.Count > 0;
                    if (_unsafe.Count == 0) return;
                    _unsafeText.Text = (_unsafe.Count == 1 ? _unsafe[0].Name + " doesn't" : $"{_unsafe.Count} stage mods ({string.Join(", ", _unsafe.Take(2).Select(x => x.Name))}) don't")
                        + " pass the app's newer online-safety check and could make online games go out of sync.";
                    foreach (var x in t.Result) Program.Trace("unsafe: " + x.Stage.Name + ": " + x.Why);
                    new ToolTip().SetToolTip(_unsafeText, string.Join("\r\n\r\n", t.Result.Select(x => x.Stage.Name + ": " + x.Why)));
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void CheckOverwritten()
        {
            CheckUnsafe();
            if (_lib.Skins.Count == 0 && _lib.Stages.Count == 0) { _banner.Visible = false; return; }
            Task.Run(() => { using (var card = _installer.OpenCard()) return (_installer.Overwritten(card), _installer.OverwrittenStages(card)); })
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) return;
                    _overwritten = t.Result.Item1; _overwrittenStages = t.Result.Item2;
                    int n = _overwritten.Count + _overwrittenStages.Count;
                    _banner.Visible = n > 0;
                    _bannerText.Text = n == 1
                        ? "A P+ update replaced one of your mods (" + (_overwritten.FirstOrDefault()?.Name ?? _overwrittenStages[0].Name) + ")."
                        : $"A P+ update replaced {n} of your mods.";
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ----- profiles -----

        void UpdateProfileButton()
        {
            _profileButton.Text = "Profile: " + (_lib?.ActiveProfile ?? "P+ as it comes") + "  ▾";
            _profileButton.AutoSize = false;
            _profileButton.Size = new Size(TextRenderer.MeasureText(_profileButton.Text, _profileButton.Font).Width + 36, _addButton.Height);
            _placeHeader();
        }

        void ShowProfileMenu()
        {
            if (_lib == null || _busy) return;
            var menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
            foreach (var p in _lib.Profiles)
            {
                var name = p.Name;
                var item = new ToolStripMenuItem($"{p.Name}   ({p.On.Count} on)") { Checked = _lib.ActiveProfile == p.Name, ForeColor = Theme.Text };
                item.Click += (s, e) => SwitchProfile(name);
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var vanilla = new ToolStripMenuItem(Library.DefaultName) { Checked = _lib.ActiveProfile == null, ForeColor = Theme.Text };
            vanilla.Click += (s, e) => SwitchProfile(null);
            menu.Items.Add(vanilla);
            menu.Items.Add(new ToolStripSeparator());
            var add = new ToolStripMenuItem("New profile...") { ForeColor = Theme.Accent };
            add.Click += (s, e) => NewProfile();
            menu.Items.Add(add);
            if (_lib.Active != null)
            {
                var rename = new ToolStripMenuItem($"Rename \"{_lib.ActiveProfile}\"...") { ForeColor = Theme.Text };
                rename.Click += (s, e) => RenameProfile();
                menu.Items.Add(rename);
                if (_lib.Profiles.Count > 1)
                {
                    var del = new ToolStripMenuItem($"Delete \"{_lib.ActiveProfile}\"") { ForeColor = Theme.Bad };
                    del.Click += (s, e) => DeleteProfile();
                    menu.Items.Add(del);
                }
            }
            // the SD cards
            if (_cards != null)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("SD CARD") { Enabled = false, ForeColor = Theme.Muted });
                foreach (var name in new[] { Cards.Normal, Cards.Big, Cards.Clean })
                {
                    if (name == Cards.Big && !_cards.HasBig) continue;
                    if (name == Cards.Clean && !_cards.HasClean) continue;
                    var n = name;
                    var item = new ToolStripMenuItem(Cards.Label(name)) { Checked = _cards.Active == name, ForeColor = Theme.Text };
                    item.Click += (s, e) => SwitchCard(n);
                    menu.Items.Add(item);
                }
                var last = _lastSave.Read();
                if (last != null)
                {
                    bool can = _lastSave.CantUndo(_cards.Active, _install.CardPath) == null;
                    var undo = new ToolStripMenuItem($"Undo last save ({last.SavedAt:ddd h:mm tt}: {last.What})") { ForeColor = can ? Theme.Text : Theme.Muted };
                    if (!can) undo.ToolTipText = _lastSave.CantUndo(_cards.Active, _install.CardPath);
                    undo.Click += (s, e) => UndoLastSave();
                    menu.Items.Add(undo);
                }
                if (!_cards.HasBig)
                {
                    var make = new ToolStripMenuItem("Make a big card for Unranked...") { ForeColor = Theme.Accent };
                    make.Click += (s, e) => MakeBigCard();
                    menu.Items.Add(make);
                }
                else if (_cards.Active == Cards.Normal)
                {
                    var del = new ToolStripMenuItem("Delete the big card...") { ForeColor = Theme.Bad };
                    del.Click += (s, e) => DeleteBigCard();
                    menu.Items.Add(del);
                }
                if (_cards.HasClean && _cards.Active != Cards.Clean)
                {
                    var del = new ToolStripMenuItem("Delete the clean card (no mods)...") { ForeColor = Theme.Bad };
                    del.Click += (s, e) =>
                    {
                        if (!CardsCanChange()) return;
                        try { _cards.DeleteClean(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                        UpdateStatus();
                    };
                    menu.Items.Add(del);
                }
            }
            menu.Show(_profileButton, new Point(0, _profileButton.Height));
        }

        // ----- Brawl Online and P+ versions -----

        string VersionsFile => Path.Combine(Program.DataFolder, "versions.txt");

        /// <summary>Says what changed since last time (Brawl Online, its Dolphin, P+), and whether updates are waiting.</summary>
        void CheckVersions()
        {
            VersionInfo v;
            try { v = Versions.Read(_install); } catch { return; }
            string before = null;
            try { if (File.Exists(VersionsFile)) before = File.ReadAllText(VersionsFile).Trim(); } catch { }
            try { File.WriteAllText(VersionsFile, v.Key); } catch { }
            if (before != null && before != v.Key)
            {
                var old = before.Split('|');
                var lines = new List<string>();
                if (old.Length > 0 && old[0] != (v.Launcher ?? "")) lines.Add($"Brawl Online: {old[0]} → {v.Launcher}");
                if (old.Length > 1 && old[1] != (v.Dolphin ?? "")) lines.Add($"Its Dolphin: {old[1]} → {v.Dolphin}");
                if (old.Length > 2 && old[2] != (v.PPlus ?? "")) lines.Add($"Project+: {old[2]} → {v.PPlus}. Your mods were checked against it; if any were replaced by the update, the yellow bar at the top puts them back after checking them again.");
                if (lines.Count > 0)
                    BeginInvoke((Action)(() => MessageBox.Show(this, "Updated since you last opened the app:\r\n\r\n• " + string.Join("\r\n• ", lines), "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            Task.Run(() =>
            {
                var notes = new List<string>();
                var release = Versions.Release(_install, v.PPlus);
                try { using (var card = _installer.OpenCard()) if (release != null && !Versions.IsOn(card, release)) notes.Add($"this card isn't on P+ {v.PPlus}: open Brawl Online once so it updates it"); } catch { }
                if (v.NewestPPlus != null && Versions.Newer(v.NewestPPlus, v.PPlus)) notes.Add($"P+ {v.NewestPPlus} is out: open Brawl Online to get it");
                string latest = Versions.LatestLauncherOnline();
                if (latest != null && v.Launcher != null && Versions.Newer(latest, v.Launcher)) notes.Add($"Brawl Online {latest} is out: open Brawl Online to get it");
                return notes;
            }).ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result.Count == 0) return;
                _updateNote = char.ToUpper(t.Result[0][0]) + string.Join("; ", t.Result).Substring(1);
                UpdateStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ----- texture packs -----

        public bool OnCleanCard => _cards?.Active == Cards.Clean;

        // where P+'s stages are on the stage select: read once from the card in the background
        StageSelect _stageSelect; bool _stageSelectReading;
        public StageSpot WhereInGame(InstalledStage s)
        {
            if (_stageSelect == null)
            {
                if (!_stageSelectReading && _installer != null)
                {
                    _stageSelectReading = true;
                    Task.Run(() => { using (var card = _installer.OpenCard()) return StageSelect.Read(card); }).ContinueWith(t =>
                    {
                        _stageSelectReading = false;
                        if (t.IsFaulted) { Program.Log("reading the stage select", t.Exception); return; }
                        _stageSelect = t.Result; _library.Refresh();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return null;
            }
            var spots = _stageSelect.SpotsOf(s.CardPath);
            return spots.Count == 0 ? new StageSpot { Page = 0 } : spots[0];
        }
        public void BackToModded() { if (_cards != null) SwitchCard(_cards.LastModded); }

        TexturePacks _textures;
        SwapPanel _swap;
        public TexturePacks Textures => _textures;

        void AddTextures(string folder, string name, SkinSource source, string sourcePath)
        {
            int count = TexturePacks.Find(folder).Count;
            if (MessageBox.Show(this, $"\"{name}\" is a Dolphin texture pack ({count} pictures).\r\n\r\nIt changes how things look on your screen only: nothing on the SD card changes and the other player doesn't get it, so it's always safe online.\r\n\r\nAdd it? The app also turns on Dolphin's \"Load Custom Textures\" for Brawl Online.",
                "Texture pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            TexturePack pack = null;
            var error = WithProgress(report => pack = _textures.Add(folder, name, source, sourcePath, report));
            if (error != null) { MessageBox.Show(this, error.Message, "Couldn't add the textures", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _library.Refresh();
            MessageBox.Show(this, $"Added \"{pack.Name}\". You'll see it the next time you play from Brawl Online.\r\n\r\nIt's in My Mods > Textures, where you can turn it off or on.", "Texture pack", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void SetOn(TexturePack p, bool on)
        {
            if (_busy) return;
            var error = WithProgress(report => _textures.SetOn(p, on, report));
            if (error != null) MessageBox.Show(this, error.Message, "Couldn't change the textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _library.Refresh();
        }

        public void Delete(TexturePack p)
        {
            if (MessageBox.Show(this, $"Delete the texture pack \"{p.Name}\"?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { _textures.Delete(p); } catch (Exception e) { MessageBox.Show(this, e.Message, "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            _library.Refresh();
        }

        // ----- mod updates (newer files on GameBanana) -----

        Dictionary<long, long> _updates = new Dictionary<long, long>();   // installed file id -> its newer version
        long _updatingFile;                                                // the old file while an update installs

        public bool HasUpdate(long fileId) => fileId != 0 && _updates.ContainsKey(fileId);

        void CheckModUpdates(bool force = false)
        {
            if (_lib == null) return;
            var lib = _lib;
            Task.Run(() => ModUpdates.Check(lib, Program.DataFolder, force)).ContinueWith(t =>
            {
                if (t.IsFaulted || lib != _lib) return;
                _updates = t.Result;
                _library.Refresh();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        List<InstalledSkin> Replacing() => _updatingFile == 0 ? null : _lib.Skins.Where(k => k.GameBananaFileId == _updatingFile).ToList();

        /// <summary>Downloads the newer version of a mod and installs it in the old one's place, through the usual checks.</summary>
        public void UpdateMod(long modId, long fileId, string pageUrl, string name)
        {
            if (_busy || modId == 0 || fileId == 0) return;
            GbDetails d = null; GbFile f = null; string path = null;
            var error = WithProgress(report =>
            {
                report("Looking for the new version", 0);
                d = GameBanana.Details(modId, ModUpdates.ModelOf(pageUrl), contents: false);
                string oldName = DownloadInfo.All(Program.DataFolder).FirstOrDefault(x => x.FileId == fileId)?.FileName;
                f = ModUpdates.Newer(d, fileId, oldName);
                if (f == null) return;
                path = GameBanana.Download(f, GameBanana.DownloadFolder(Program.DataFolder, modId),
                    (done, total) => report($"Downloading {f.Name}", total > 0 ? (int)Math.Min(100, done * 100 / total) : 0), System.Threading.CancellationToken.None);
            });
            if (error != null) { MessageBox.Show(this, (error is GbException ? error.Message : "The update didn't download: " + error.Message), "Couldn't update", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (f == null || path == null)
            {
                _updates.Remove(fileId); _library.Refresh();
                MessageBox.Show(this, $"\"{name}\" is already the newest version.", "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var source = new SkinSource { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, PageUrl = d.Mod.ProfileUrl, ThumbUrl = d.Mod.ThumbUrl };
            try
            {
                new DownloadInfo { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, Category = d.Mod.Category,
                    ThumbUrl = d.Mod.ThumbUrl, PageUrl = d.Mod.ProfileUrl, FileName = f.Name, Path = path, DownloadedAt = DateTime.Now }.Save();
            }
            catch { }
            MessageBox.Show(this, $"Downloaded the new version of \"{name}\" ({f.Name}, added {f.Added:MMM d}).\r\n\r\nNext it goes through the same safety checks as any mod. It takes the old version's place, and the old one leaves My Mods.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _updatingFile = fileId;
            try { AddSkin(path, source); }
            finally { _updatingFile = 0; }
            if (!_lib.Skins.Any(k => k.GameBananaFileId == fileId) && !_lib.Stages.Any(k => k.GameBananaFileId == fileId)) _updates.Remove(fileId);
            _library.Refresh();
        }

        // ----- SD cards -----

        readonly LastSave _lastSave = new LastSave(Program.DataFolder);
        Installer NewInstaller() => new Installer(_install, _lib) { Undo = _lastSave, CardName = _cards?.Active ?? Cards.Normal };

        void UndoLastSave()
        {
            if (!CardsCanChange()) return;
            var info = _lastSave.Read();
            string why = _lastSave.CantUndo(_cards.Active, _install.CardPath);
            if (why != null) { MessageBox.Show(this, why, "Undo last save", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this, $"Put your SD card and mods back the way they were before your last save ({info.SavedAt:ddd h:mm tt}: {info.What})?",
                "Undo last save", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var error = WithProgress(report => _lastSave.Undo(_install, _lib, _cards.Active, report));
            ReloadLibrary();
            UpdateStatus();
            if (error != null) MessageBox.Show(this, error.Message + "\r\n\r\nYour SD card wasn't changed.", "Couldn't undo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, "Done: your SD card and mods are back to how they were before your last save.", "Undo last save", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        bool CardsCanChange()
        {
            if (_install.IsRunning(out string what))
            {
                MessageBox.Show(this, $"Close {what} first. It uses the SD card.", "Brawl Online is open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return SavedFirst();
        }

        /// <summary>Runs a long card job with the progress window; returns the error, if any.</summary>
        Exception WithProgress(Action<Action<string, int>> job)
        {
            _busy = true;
            Exception error = null;
            using (var progress = new ProgressForm())
            {
                progress.Shown += async (s, e) =>
                {
                    try { await Task.Run(() => job((m, p) => progress.Report(m, p))); }
                    catch (Exception ex) { error = ex; }
                    progress.Close();
                };
                progress.ShowDialog(this);
            }
            _busy = false;
            if (error != null) Program.Trace("card job failed: " + error);
            return error;
        }

        void MakeBigCard()
        {
            if (!CardsCanChange()) return;
            var answer = MessageBox.Show(this,
                "This makes a second, bigger SD card (4 GB instead of 2 GB), so you get about 2 GB of room for songs and other mods.\r\n\r\n" +
                "Use it for Unranked and Ranked: there, everyone plays their own card.\r\n" +
                "Use the normal card for rooms: players in a room get the host's card, and a bigger card would make them download most of it.\r\n\r\n" +
                "You switch between them in this menu (Profile ▾ > SD card). The big card starts with the mods you have now; after that, each card has its own mods.\r\n\r\n" +
                "It takes about 4 GB on your PC (and Brawl Online keeps a copy of whichever card is in use). Make it?",
                "Make a big card", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            var error = WithProgress(report => _cards.MakeBig(report));
            if (error != null) { MessageBox.Show(this, error.Message + "\r\n\r\nNothing was changed.", "Couldn't make the big card", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, "The big card is ready. Switch to it now?", "Big card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SwitchCard(Cards.Big);
        }

        void DeleteBigCard()
        {
            if (!CardsCanChange()) return;
            if (MessageBox.Show(this, "Delete the big card and the mods list that goes with it? The mods' downloads stay in My Mods' Downloaded tab.", "Delete the big card",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _cards.DeleteBig(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            UpdateStatus();
        }

        bool SwitchCard(string to, bool quiet = false)
        {
            _stageSelect = null;
            if (to == _cards.Active) return true;
            if (!CardsCanChange()) return false;
            var notes = new List<string>();
            var release = Versions.Release(_install, Versions.Read(_install).PPlus);
            var error = WithProgress(report =>
            {
                // the card coming in may have missed P+ updates while it was put away
                if (release != null)
                {
                    var parkedLib = new Library(_cards.LibraryFolder(to));
                    var current = _lib;
                    _cards.UseOriginals(p => current.OriginalFile(p));
                    notes.AddRange(_cards.CatchUp(to, parkedLib, release, report));
                }
                report("Switching cards", 100);
                _cards.Switch(to);
            });
            if (error != null) { MessageBox.Show(this, error.Message + "\r\n\r\nThe card in use wasn't changed.", "Couldn't switch cards", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            ReloadLibrary();
            UpdateStatus();
            if (quiet && notes.Count == 0) return true;
            MessageBox.Show(this, "Now using the " + Cards.Label(to).ToLowerInvariant() + "." + (to == Cards.Big
                ? "\r\n\r\nPlay Unranked and Ranked with it. Switch back to the normal card before hosting or joining a room."
                : to == Cards.Clean ? "\r\n\r\nThis is P+ exactly as it comes, with none of your mods. Your mods are still on your other card(s)."
                : "\r\n\r\nThis is the card for rooms.") + (notes.Count > 0 ? "\r\n\r\n" + string.Join("\r\n", notes) : ""),
                "SD card", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        // ----- launching Brawl Online -----

        string LauncherFile => Path.Combine(Program.DataFolder, "launcher.txt");

        /// <summary>Where Brawl Online's launcher is: remembered, its usual place, Windows' list of installed programs, or asked once.</summary>
        string FindLauncher()
        {
            try { if (File.Exists(LauncherFile)) { string saved = File.ReadAllText(LauncherFile).Trim(); if (File.Exists(saved)) return saved; } } catch { }
            var places = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Brawl Online", "Brawl Online.exe") };
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                try
                {
                    using (var un = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        if (un != null) foreach (var k in un.GetSubKeyNames())
                            using (var e = un.OpenSubKey(k))
                            {
                                if (!((e?.GetValue("DisplayName") as string) ?? "").StartsWith("Brawl Online", StringComparison.OrdinalIgnoreCase)) continue;
                                if (e.GetValue("InstallLocation") is string dir && dir.Length > 0) places.Add(Path.Combine(dir.Trim('"'), "Brawl Online.exe"));
                                if (e.GetValue("DisplayIcon") is string icon && icon.Length > 0) places.Add(icon.Split(',')[0].Trim('"'));
                            }
                }
                catch { }
            var found = places.FirstOrDefault(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p) && !Path.GetFileName(p).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            MessageBox.Show(this, "The app couldn't find Brawl Online on this PC. Show it where \"Brawl Online.exe\" is (only needed once).", "Find Brawl Online", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using (var dlg = new OpenFileDialog { Title = "Where is Brawl Online.exe?", Filter = "Brawl Online|Brawl Online.exe|Programs|*.exe" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                try { Directory.CreateDirectory(Program.DataFolder); File.WriteAllText(LauncherFile, dlg.FileName); } catch { }
                return dlg.FileName;
            }
        }

        /// <summary>Puts the right SD card in place and opens Brawl Online: your mods (the card you last used with mods), or none.</summary>
        void Launch(bool modded)
        {
            if (_install == null || _cards == null || _busy) return;
            if (_install.IsRunning(out string what))
            {
                MessageBox.Show(this, $"{what} is already open.\r\n\r\nClose it first, then click Launch again: the SD card can only be swapped while it's closed.", "Already open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string exe = FindLauncher();
            if (exe == null) return;
            if (modded)
            {
                if (!SavePending()) return;
                if (_cards.Active == Cards.Clean && !SwitchCard(_cards.LastModded, quiet: true)) return;
                if (!StagesSafeToLaunch()) return;
            }
            else
            {
                if (!_cards.HasClean)
                {
                    if (MessageBox.Show(this, "To play without mods, the app makes a clean SD card once: P+ exactly as it comes, kept next to your other card(s).\r\n\r\n" +
                        "It takes about 2 GB on the drive Brawl Online is on and a minute or two. After that, switching between modded and unmodded is instant, and your mods stay where they are.\r\n\r\nMake it now?",
                        "Clean card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    if (!CardsCanChange()) return;
                    var release = Versions.Release(_install, Versions.Read(_install).PPlus);
                    var libs = new List<Library> { new Library(_cards.LibraryFolder(Cards.Normal)) };
                    if (Directory.Exists(_cards.LibraryFolder(Cards.Big))) libs.Add(new Library(_cards.LibraryFolder(Cards.Big)));
                    var error = WithProgress(report => _cards.MakeClean(release, libs, report));
                    if (error != null) { MessageBox.Show(this, error.Message + "\r\n\r\nYour SD card wasn't changed.", "Couldn't make the clean card", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                }
                if (!SwitchCard(Cards.Clean, quiet: true)) return;
            }
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
            catch (Exception e) { MessageBox.Show(this, "Brawl Online didn't open: " + e.Message, "Launch", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _status.Text = $"Opened Brawl Online with the {Cards.Label(_cards.Active).ToLowerInvariant()}.";
            _status.ForeColor = Theme.Good;
        }

        /// <summary>Online safety before "Launch modded": a stage mod that doesn't pass today's check would make online
        /// games go out of sync, so Brawl Online isn't opened while one is on. One click turns them off and launches.</summary>
        bool StagesSafeToLaunch()
        {
            if (_lib == null || !_lib.Stages.Any(x => x.Enabled && !x.IsSong)) return true;
            List<(InstalledStage Stage, string Why)> found = null;
            var error = WithProgress(report =>
            {
                report("Checking your stages are safe for online play", 20);
                using (var card = _installer.OpenCard()) found = _installer.UnsafeStages(card);
            });
            if (error != null)
            {
                Program.Log("checking stages before launch", error);
                return MessageBox.Show(this, "The app couldn't check your stage mods before launching (" + error.Message + ").\r\n\r\n" +
                    "If a stage mod doesn't match P+, online games on it go out of sync. Launch anyway?", "Launch modded",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            _unsafe = found.Select(x => x.Stage).ToList();
            _unsafeBanner.Visible = _unsafe.Count > 0;
            if (found.Count == 0) return true;
            string list = string.Join("\r\n", found.Select(x => "• " + x.Stage.Name + "  (" + x.Stage.Target.Label + ")"));
            var answer = MessageBox.Show(this,
                (found.Count == 1 ? "This stage mod makes" : "These stage mods make") + " online games go out of sync, so Brawl Online can't be opened with " + (found.Count == 1 ? "it" : "them") + " on:\r\n\r\n" + list +
                "\r\n\r\nTurn " + (found.Count == 1 ? "it" : "them") + " off and launch?\r\n\r\n(To keep " + (found.Count == 1 ? "its" : "their") + " looks, add " + (found.Count == 1 ? "it" : "them") +
                " again later from My Mods with \"Use its looks on P+'s ...\": that version is safe online.)",
                "Not safe online", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return false;
            var change = new Installer.Change(); change.StageDisable.AddRange(found.Select(x => x.Stage));
            if (!Run(change, null)) return false;
            _unsafe.Clear(); _unsafeBanner.Visible = false;
            return true;
        }

        /// <summary>The card in place changed: its own mods list, installer and screens.</summary>
        void ReloadLibrary()
        {
            _lib = new Library(_cards.LibraryFolder(_cards.Active));
            _lib.EnsureProfiles();
            _installer = NewInstaller();
            _pending.Clear(); PendingChanged();
            _pictureCache.Clear();
            UpdateProfileButton();
            ShowCharacter(refreshPictures: true);
            _library.Refresh();
            _store.RefreshInstalled();
            UpdateStatus();
            CheckOverwritten();
        }

        bool SavedFirst()
        {
            if (_pending.Count == 0) return true;
            var answer = MessageBox.Show(this, "You have switches that aren't saved yet. Save them to this profile before switching?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return SavePending();
            _pending.Clear(); PendingChanged();
            return true;
        }

        /// <summary>Turns on exactly the mods of a profile (null: none, P+ as it comes), in one save.</summary>
        void SwitchProfile(string name)
        {
            if (name == _lib.ActiveProfile || !SavedFirst()) return;
            var want = new HashSet<string>(name == null ? new List<string>() : _lib.Profiles.First(p => p.Name == name).On);
            var change = new Installer.Change();
            var usedSlots = new HashSet<string>();
            foreach (var s in _lib.Skins.OrderBy(x => x.Enabled ? 0 : 1))
            {
                bool on = want.Contains(s.Id) && usedSlots.Add(s.SlotListId + ":" + s.SlotIndex);   // one per slot
                if (on && !s.Enabled) change.Enable.Add(s); else if (!on && s.Enabled) change.Disable.Add(s);
            }
            var usedStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in _lib.Stages.OrderBy(x => x.Enabled ? 0 : 1))
            {
                bool on = want.Contains(st.Id) && usedStages.Add(st.CardPath);
                if (on && !st.Enabled) change.StageEnable.Add(st); else if (!on && st.Enabled) change.StageDisable.Add(st);
            }
            string before = _lib.ActiveProfile;
            _lib.ActiveProfile = name;
            if (change.IsEmpty) { _lib.Save(); UpdateProfileButton(); PendingChanged(); return; }
            int ons = change.Enable.Count + change.StageEnable.Count, offs = change.Disable.Count + change.StageDisable.Count;
            if (!Run(change, name == null ? "Switched to P+ as it comes: all your mods are off (they're still in My Mods)."
                                          : $"Switched to \"{name}\": {ons} mod(s) turned on, {offs} turned off."))
            { _lib.ActiveProfile = before; }
            _lib.Save();
            UpdateProfileButton();
        }

        void NewProfile()
        {
            if (!SavedFirst()) return;
            using (var f = new InputForm("New profile", "Name the new profile (for example \"Tournament\" or \"Fun\"):", "", "Create", "Start with the mods that are on now"))
            {
                f.Option.Checked = true;
                if (f.ShowDialog(this) != DialogResult.OK || f.Value == "") return;
                if (f.Value == Library.DefaultName || _lib.Profiles.Any(p => p.Name.Equals(f.Value, StringComparison.OrdinalIgnoreCase)))
                { MessageBox.Show(this, "There's already a profile with that name.", "New profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _lib.Profiles.Add(new Profile { Name = f.Value, On = f.Option.Checked ? _lib.OnNow() : new List<string>() });
                _lib.Save();
                if (f.Option.Checked) { _lib.ActiveProfile = f.Value; _lib.Save(); UpdateProfileButton(); PendingChanged(); }
                else SwitchProfile(f.Value);
            }
        }

        void RenameProfile()
        {
            var p = _lib.Active; if (p == null) return;
            using (var f = new InputForm("Rename profile", "New name:", p.Name, "Rename"))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Value == "" || f.Value == p.Name) return;
                if (f.Value == Library.DefaultName || _lib.Profiles.Any(x => x != p && x.Name.Equals(f.Value, StringComparison.OrdinalIgnoreCase)))
                { MessageBox.Show(this, "There's already a profile with that name.", "Rename profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                p.Name = f.Value; _lib.ActiveProfile = f.Value; _lib.Save(); UpdateProfileButton();
            }
        }

        void DeleteProfile()
        {
            var p = _lib.Active; if (p == null || _lib.Profiles.Count < 2) return;
            if (MessageBox.Show(this, $"Delete the profile \"{p.Name}\"?\r\n\r\nYour mods stay in My Mods and nothing on the SD card changes. Pick another profile afterwards to switch to it.",
                "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _lib.Profiles.Remove(p);
            var next = _lib.Profiles.First();
            _lib.ActiveProfile = null;    // nothing active until the next one is switched to
            SwitchProfile(next.Name);
        }

        // ----- IModHost (My Mods) -----

        public Library Lib => _lib;
        readonly Favorites _favs = new Favorites(Program.DataFolder);
        public Favorites Favs => _favs;
        SpaceReport _space;
        Cards _cards;
        string _updateNote;   // an update waiting or a card behind, shown in the status line
        public SpaceReport Space => _space;
        public string DataFolder => Program.DataFolder;
        public bool IsOn(InstalledSkin s) => _pending.TryGetValue(s.Id, out bool on) ? on : s.Enabled;
        public bool IsPending(InstalledSkin s) => _pending.ContainsKey(s.Id);
        public bool IsOn(InstalledStage s) => _pending.TryGetValue(s.Id, out bool on) ? on : s.Enabled;
        public bool IsPending(InstalledStage s) => _pending.ContainsKey(s.Id);

        public void SetOn(IEnumerable<InstalledStage> stages, bool on)
        {
            foreach (var s in stages.ToList())
            {
                if (on == s.Enabled) _pending.Remove(s.Id); else _pending[s.Id] = on;
                if (on)   // one stage per stage file
                    foreach (var other in _lib.Stages.Where(x => x.Id != s.Id && string.Equals(x.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase) && IsOn(x)).ToList())
                    { if (other.Enabled) _pending[other.Id] = false; else _pending.Remove(other.Id); }
            }
            PendingChanged();
        }

        public void Delete(InstalledStage s)
        {
            string text = s.Enabled
                ? $"Delete \"{s.Name}\" from My Mods?\r\n\r\nIt's on right now, so P+'s own {s.Target.Label} goes back."
                : $"Delete \"{s.Name}\" from My Mods?\r\n\r\nIt's off, so nothing on your SD card changes.";
            if (MessageBox.Show(this, text, "Delete stage", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _pending.Remove(s.Id);
            if (!s.Enabled)
            {
                _lib.Stages.Remove(s); _lib.Save();
                try { Directory.Delete(_lib.StageFolder(s.Id), true); } catch { }
                PendingChanged();
                return;
            }
            Run(new Installer.Change { StageRemove = { s } }, $"Deleted. {s.Target.Label} is P+'s again.");
        }

        public string SourceOf(InstalledStage s) => SourceOf(new InstalledSkin { SourcePath = s.SourcePath, GameBananaModId = s.GameBananaModId, GameBananaFileId = s.GameBananaFileId });
        public void AddMore(InstalledStage s) => AddMore(new InstalledSkin { Name = s.Name, SourcePath = s.SourcePath, GameBananaModId = s.GameBananaModId, GameBananaFileId = s.GameBananaFileId, Author = s.Author, PageUrl = s.PageUrl, ThumbUrl = s.ThumbUrl });
        public void GoToStore() => ShowPage(_store);

        public void SetOn(IEnumerable<InstalledSkin> skins, bool on)
        {
            foreach (var s in skins.ToList())
            {
                Want(s, on);
                if (on)   // one skin per costume slot: whatever else is on there turns off
                    foreach (var other in _lib.Skins.Where(x => x.Id != s.Id && x.SlotListId == s.SlotListId && x.SlotIndex == s.SlotIndex && IsOn(x)).ToList())
                        Want(other, false);
            }
            PendingChanged();
        }

        void Want(InstalledSkin s, bool on)
        {
            if (on == s.Enabled) _pending.Remove(s.Id); else _pending[s.Id] = on;
        }

        void PendingChanged()
        {
            int on = _pending.Count(kv => kv.Value), off = _pending.Count - on;
            var parts = new List<string>();
            if (on > 0) parts.Add($"{on} to turn on");
            if (off > 0) parts.Add($"{off} to turn off");
            _saveText.Text = $"Not saved yet: {string.Join(", ", parts)}. Brawl Online uses them once they're saved to your SD card.";
            _saveBar.Visible = _pending.Count > 0;
            _library.Refresh();
            _characters.Invalidate();
        }

        bool SavePending()
        {
            if (_pending.Count == 0) return true;
            var change = new Installer.Change();
            AddPending(change);
            int on = change.Enable.Count + change.StageEnable.Count, off = change.Disable.Count + change.StageDisable.Count;
            return Run(change, on > 0 && off > 0 ? $"Saved. {on} mod(s) on, {off} off." : on > 0 ? $"Saved. {on} mod(s) turned on." : $"Saved. {off} mod(s) turned off.");
        }

        void AddPending(Installer.Change change)
        {
            foreach (var kv in _pending)
            {
                var s = _lib.Skins.FirstOrDefault(x => x.Id == kv.Key);
                if (s != null) { if (kv.Value) change.Enable.Add(s); else change.Disable.Add(s); continue; }
                var st = _lib.Stages.FirstOrDefault(x => x.Id == kv.Key);
                if (st != null) { if (kv.Value) change.StageEnable.Add(st); else change.StageDisable.Add(st); }
            }
        }

        public void Delete(InstalledSkin s)
        {
            string text = s.Enabled
                ? $"Delete \"{s.Name}\" from My Mods?\r\n\r\nIt's on right now, so P+'s own costume goes back in {s.Character.Name}'s costume {s.SlotIndex + 1}."
                : $"Delete \"{s.Name}\" from My Mods?\r\n\r\nIt's off, so nothing on your SD card changes.";
            if (MessageBox.Show(this, text, "Delete skin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _pending.Remove(s.Id);
            if (!s.Enabled)
            {
                // nothing on the card: just forget it
                _lib.Skins.Remove(s);
                _lib.Save();
                try { Directory.Delete(_lib.SkinFolder(s.Id), true); } catch { }
                PendingChanged();
                return;
            }
            Run(new Installer.Change { Remove = { s } }, $"Deleted. {s.Character.Name}'s costume {s.SlotIndex + 1} is P+'s again.");
        }

        public void InstallDownload(DownloadInfo d) => AddSkin(d.Path, d.Source);

        void RememberCostumeCount(string path, int count)
        {
            try
            {
                var d = DownloadInfo.All(Program.DataFolder).FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (d != null && d.CostumeCount != count) { d.CostumeCount = count; d.Save(); }
            }
            catch { }
        }

        /// <summary>The download a skin came from, if it's still on this PC.</summary>
        public string SourceOf(InstalledSkin s)
        {
            if (!string.IsNullOrEmpty(s.SourcePath) && (File.Exists(s.SourcePath) || Directory.Exists(s.SourcePath))) return s.SourcePath;
            if (s.GameBananaModId != 0)
            {
                string folder = GameBanana.DownloadFolder(Program.DataFolder, s.GameBananaModId);
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder).Where(f => !f.EndsWith(".json") && !f.EndsWith(".part")).ToList();
                    return files.FirstOrDefault(f => s.GameBananaFileId != 0 && Path.GetFileName(f).StartsWith(s.GameBananaFileId + "-")) ?? files.FirstOrDefault();
                }
            }
            return null;
        }

        public void AddMore(InstalledSkin s)
        {
            string path = SourceOf(s);
            if (path == null)
            {
                MessageBox.Show(this, $"The download \"{s.Name}\" came from isn't on this PC anymore." + (s.GameBananaModId != 0 ? " Find it again in the Mod Store and click Install: costumes you already added are marked." : " Use \"Add from file\" to open it again."),
                    "Download not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var src = s.GameBananaModId != 0 ? new SkinSource { ModId = s.GameBananaModId, FileId = s.GameBananaFileId, ModName = BaseName(s.Name), Author = s.Author, PageUrl = s.PageUrl, ThumbUrl = s.ThumbUrl } : null;
            AddSkin(path, src);
        }

        static string BaseName(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s+(\(.*\)|#\w+)$", "");

        public Image SlotPicture(InstalledSkin s)
        {
            var c = s.Character;
            // P+'s own picture for the slot (saved when a skin first replaced it), else what the card shows now
            var original = _lib.OriginalPicture(c.CspFilePath, c.CspTextureName(s.SlotIndex));
            if (original != null) return original;
            if (!_pictureCache.TryGetValue(c.SlotListId, out var pics))
                using (var card = _installer.OpenCard())
                {
                    var slots = PPlusCard.ReadSlots(card, c);
                    _pictureCache[c.SlotListId] = pics = Cosmetics.ReadPictures(card.ReadFile(c.CspFilePath), slots.Select(x => c.CspTextureName(x.Index)));
                }
            return pics.TryGetValue(c.CspTextureName(s.SlotIndex), out var p) ? p : null;
        }

        // ----- costume slots page -----

        void DrawCharacter(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var c = (Character)_characters.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.PanelHover : Theme.Back)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var a = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
            TextRenderer.DrawText(e.Graphics, c.Name, sel ? Theme.BodyBold : Theme.Body, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 60, e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            int mine = _lib?.Skins.Count(s => s.SlotListId == c.SlotListId && s.Enabled) ?? 0;
            if (mine > 0)
                TextRenderer.DrawText(e.Graphics, mine + " on", Theme.Small, new Rectangle(e.Bounds.Right - 60, e.Bounds.Y, 54, e.Bounds.Height), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        Character Current => _characters.SelectedItem as Character;

        void ShowCharacter(bool refreshPictures = false)
        {
            var c = Current;
            if (c == null || _installer == null || !_slotsPage.Visible) return;
            if (refreshPictures) _pictureCache.Remove(c.SlotListId);
            Program.Trace("show " + c.Name);
            _charTitle.Text = c.Name;
            _slots.SuspendLayout();
            foreach (Control old in _slots.Controls) old.Dispose();
            _slots.Controls.Clear();
            List<CostumeSlot> slots;
            Dictionary<string, Bitmap> pics;
            try
            {
                using (var card = _installer.OpenCard())
                {
                    slots = PPlusCard.ReadSlots(card, c);
                    if (!_pictureCache.TryGetValue(c.SlotListId, out pics))
                        _pictureCache[c.SlotListId] = pics = Cosmetics.ReadPictures(card.ReadFile(c.CspFilePath), slots.Select(s => c.CspTextureName(s.Index)));
                }
            }
            catch (Exception e)
            {
                Program.Trace("read failed: " + e);
                _charHint.Text = "Could not read this character from the SD card: " + e.Message;
                _slots.ResumeLayout();
                return;
            }
            Program.Trace($"slots {slots.Count}, pictures {pics.Count}");
            _charHint.Text = $"{slots.Count} costumes, in the order the character select screen shows them, as they are on your SD card now. Click one of your skins to turn it off.";
            foreach (var s in slots)
            {
                var skin = _lib.At(c, s.Index);
                int others = _lib.AllAt(c.SlotListId, s.Index).Count(x => !x.Enabled);
                var tile = new SlotTile
                {
                    Picture = pics.TryGetValue(c.CspTextureName(s.Index), out var p) ? p : null,
                    Caption = $"Costume {s.Index + 1}",
                    Badge = skin != null ? "Your skin: " + skin.Name : PPlusCard.TeamColorName(s.TeamColor) + (s.OnCard ? "" : " • from Brawl") + (others > 0 ? $"\r\n{others} of yours (off)" : ""),
                    BadgeColor = skin != null ? Theme.Accent : Theme.Muted,
                    Margin = new Padding(0, 0, 14, 14),
                };
                if (skin != null) tile.Click += (o, e) => OfferTurnOff(skin);
                else if (others > 0) tile.Click += (o, e) => { ShowPage(_library); _library.ShowCharacter(c); };
                else tile.Click += (o, e) => { ShowPage(_store); _store.ShowCharacter(c); };
                _slots.Controls.Add(tile);
            }
            _slots.ResumeLayout();
            _characters.Invalidate();
        }

        void OfferTurnOff(InstalledSkin skin)
        {
            var answer = MessageBox.Show(this, $"Turn off \"{skin.Name}\" in {skin.Character.Name}'s costume {skin.SlotIndex + 1}?\r\n\r\nP+'s own costume goes back in that slot. The skin stays in My Mods, so you can turn it on again any time.",
                "Turn off skin", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) Run(new Installer.Change { Disable = { skin } }, $"Turned off. {skin.Character.Name}'s costume {skin.SlotIndex + 1} is P+'s again.");
        }

        // ----- adding a skin -----

        void PickSkin()
        {
            if (_installer == null) return;
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose a mod (zip, rar or 7z, or a file from an extracted mod), or your own music (MP3, WAV)",
                Filter = "Mods and music|*.zip;*.rar;*.7z;*.pac;*.pcs;*.brstm;*.mp3;*.wav|Your own music|*.mp3;*.wav|All files|*.*",
                Multiselect = true,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.FileNames.Any(MusicImport.IsAudio)) { AddOwnMusic(dlg.FileNames.Where(MusicImport.IsAudio).ToList()); return; }
                string path = dlg.FileName;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                AddSkin(ext == ".pac" || ext == ".pcs" ? Path.GetDirectoryName(path) : path);
            }
        }

        /// <summary>Your own MP3/WAV songs: made into game songs, then placed like any music.</summary>
        public void AddOwnMusic(List<string> files = null)
        {
            if (_busy) return;
            if (_installer == null) { AddSkin(null); return; }
            files = files ?? MusicImportForm.Pick(this);
            if (files == null || files.Count == 0) return;
            using (var f = new MusicImportForm(files))
                if (f.ShowDialog(this) != DialogResult.OK || f.ResultFolder == null) return;
                else AddSkin(f.ResultFolder);
        }

        void AddSkin(string path, SkinSource source = null, List<string> files = null)
        {
            if (_busy) return;
            if (path != null && MusicImport.IsAudio(path) && _installer != null) { AddOwnMusic(new List<string> { path }); return; }
            if (_installer == null)
            {
                MessageBox.Show(this, "Brawl Online isn't set up on this PC yet, so there's no P+ SD card to put skins on.\r\n\r\nOpen Brawl Online once and let it finish setting up Project+, then open this app again.", "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Program.Trace("add skin: " + path);
            string originalPath = path;
            // A single file from an extracted skin (a .pac, a picture...): use the folder it is in.
            if (File.Exists(path) && !Archives.IsArchive(path)) path = Path.GetDirectoryName(path);
            SkinPackage pkg;
            string folder;
            try
            {
                Cursor = Cursors.WaitCursor;
                folder = files != null ? Archives.OpenAll(files) : Archives.Open(path);
                string name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
                pkg = SkinPackage.Scan(folder, source?.ModName ?? name);
                pkg.Source = source;
                pkg.SourcePath = originalPath;
            }
            catch (Exception e)
            {
                Program.Trace("open failed: " + e);
                MessageBox.Show(this, e.Message, "Couldn't open that skin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { Cursor = Cursors.Default; }
            Program.Trace($"package: {pkg.Costumes.Count} costumes, {pkg.StageFiles.Count} stages, {pkg.Pictures.Count} pictures");
            if (pkg.Costumes.Count == 0 && pkg.StageFiles.Count == 0 && pkg.SongFiles.Count > 0)
            {
                RememberCostumeCount(originalPath, pkg.SongFiles.Count);
                using (var form = new SongForm(_installer, _lib, pkg))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Plans.Count == 0) return;
                    var change = new Installer.Change(); change.StageInstall.AddRange(form.Plans);
                    Run(change, "Installed! " + string.Join("\r\n", form.Plans.Take(10).Select(p => p.Kind == "song-new" ? $"\"{p.Name}\" was added to the songs that can play on {p.Target.StageName.Replace("New song on ", "")}." : $"\"{p.Target.StageName}\" now plays \"{p.Name}\".")) + (form.Plans.Count > 10 ? $"\r\n...and {form.Plans.Count - 10} more." : ""));
                }
                return;
            }
            if (pkg.Costumes.Count == 0 && pkg.StageFiles.Count > 0)
            {
                RememberCostumeCount(originalPath, pkg.StageFiles.Count);
                var stagePlans = new List<StagePlan>();
                using (var form = new StageForm(_installer, _lib, pkg))
                {
                    if (form.ShowDialog(this) == DialogResult.OK) stagePlans = form.Plans;
                    else if (pkg.SongFiles.Count == 0) return;
                }
                // music that came with the stage goes on that stage
                var songPlans = new List<StagePlan>();
                if (pkg.SongFiles.Count > 0)
                    using (var songs = new SongForm(_installer, _lib, pkg, stagePlans.Count > 0 ? stagePlans.Select(p => p.Target.CardPath).ToList() : null))
                        if (songs.ShowDialog(this) == DialogResult.OK) songPlans = songs.Plans;
                if (stagePlans.Count == 0 && songPlans.Count == 0) return;
                var change = new Installer.Change(); change.StageInstall.AddRange(stagePlans); change.StageInstall.AddRange(songPlans);
                Run(change, "Installed! " + string.Join("\r\n", stagePlans.Select(p => $"{p.Target.Label} now looks like \"{p.Name}\".")) +
                    (songPlans.Count > 0 ? (stagePlans.Count > 0 ? "\r\n" : "") + string.Join("\r\n", songPlans.Take(10).Select(p => p.Kind == "song-new" ? $"\"{p.Name}\" was added to the songs that can play on {p.Target.StageName.Replace("New song on ", "")}." : $"\"{p.Target.StageName}\" now plays \"{p.Name}\".")) + (songPlans.Count > 10 ? $"\r\n...and {songPlans.Count - 10} more." : "") : "") +
                    (stagePlans.Count > 0 ? "\r\n\r\nIt plays exactly like P+'s stage (the app checked), so it's safe online. In rooms you host, the people who join will see it too." : ""));
                return;
            }
            if (pkg.Costumes.Count == 0 && pkg.StageFiles.Count == 0 && pkg.SongFiles.Count == 0 && TexturePacks.Find(folder).Count > 0)
            {
                AddTextures(folder, source?.ModName ?? Path.GetFileNameWithoutExtension(originalPath.TrimEnd('\\', '/')), source, originalPath);
                return;
            }
            if (pkg.Costumes.Count == 0)
            {
                if (source != null)
                    MessageBox.Show(this, $"\"{source.ModName}\" has no costume, stage or music files the app can use (files named like FitMario03.pac, STGBATTLEFIELD.pac or a .brstm song).\r\n\r\nIt may be a model or project files rather than a ready-to-use skin. Its GameBanana page may explain how to use it.",
                        "No costume found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show(this, "No costume files were found in that skin.\r\n\r\nA skin has files named like FitMario03.pac (or .pcs). If you picked a folder, make sure it is the skin's folder.",
                        "No costume found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RememberCostumeCount(originalPath, pkg.Costumes.Count);
            if (pkg.Costumes.Count <= 1) { SetUpOne(pkg, null); return; }
            // a pack: the list of its costumes. "Set up this one step by step" does that one, then comes back to the list
            while (true)
            {
                PackageCostume start;
                using (var pack = new PackForm(_installer, _lib, pkg, Replacing()))
                {
                    var answer = pack.ShowDialog(this);
                    if (answer == DialogResult.OK && pack.Plans.Count > 0)
                    {
                        var plans = pack.Plans;
                        var change = new Installer.Change(); change.Install.AddRange(plans);
                        Run(change, plans.Count == 1
                            ? $"Installed! In Brawl Online, {plans[0].Character.Name}'s costume {plans[0].Slot.Index + 1} is now \"{plans[0].Name}\"."
                            : $"Installed {plans.Count} costumes:\r\n\r\n" + string.Join("\r\n", plans.Select(p => $"{p.Character.Name}'s costume {p.Slot.Index + 1}: {p.Name}")) +
                              "\r\n\r\nYou can add the rest of this download any time: click \"Add more\" on one of its cards in My Mods.");
                        return;
                    }
                    if (answer != DialogResult.Retry || pack.StepByStep == null) return;
                    start = pack.StepByStep;
                }
                SetUpOne(pkg, start);
            }
        }

        /// <summary>The step-by-step wizard for one costume (slot, pictures, checks), then the save.</summary>
        void SetUpOne(SkinPackage pkg, PackageCostume start)
        {
            var was = Replacing()?.FirstOrDefault();
            using (var wizard = new AddSkinForm(_installer, _lib, pkg, was?.Character ?? Current, start) { Replacing = was })
            {
                if (wizard.ShowDialog(this) == DialogResult.OK && wizard.Plan != null)
                {
                    var plan = wizard.Plan;
                    Run(new Installer.Change { Install = { plan } },
                        $"Installed! In Brawl Online, {plan.Character.Name}'s costume {plan.Slot.Index + 1} is now \"{plan.Name}\".");
                }
            }
        }

        /// <summary>Writes a change to the SD card (plus any switches not saved yet). True when it worked.</summary>
        bool Run(Installer.Change change, string doneMessage)
        {
            if (_cards?.Active == Cards.Clean)
            {
                if (MessageBox.Show(this, "The clean card (no mods) is in place right now, so mods can't go on it.\r\n\r\nSwitch back to your " + Cards.Label(_cards.LastModded).ToLowerInvariant() + "? Then do this again.",
                    "Clean card in place", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) SwitchCard(_cards.LastModded, quiet: true);
                return false;
            }
            if (_install.IsRunning(out string what))
            {
                MessageBox.Show(this, $"Close {what} first.\r\n\r\nIt uses the SD card, so the card can't be changed while it is open.", "Brawl Online is open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            // an update: the old version goes where the new one takes its place
            if (_updatingFile != 0)
            {
                foreach (var old in _lib.Skins.Where(k => k.GameBananaFileId == _updatingFile).ToList())
                    if (change.Install.Any(p => p.Character.SlotListId == old.SlotListId && p.Slot.Index == old.SlotIndex) && !change.Remove.Any(r => r.Id == old.Id))
                        change.Remove.Add(old);
                foreach (var old in _lib.Stages.Where(k => k.GameBananaFileId == _updatingFile).ToList())
                    if (change.StageInstall.Any(p => string.Equals(p.Target.CardPath, old.CardPath, StringComparison.OrdinalIgnoreCase) ||
                                                     (old.IsNewSong && p.Kind == "song-new" && string.Equals(p.TrackList, old.TrackList, StringComparison.OrdinalIgnoreCase)))
                        && !change.StageRemove.Any(r => r.Id == old.Id))
                        change.StageRemove.Add(old);
            }
            // switches waiting to be saved go along, except for skins this change already handles
            var handled = new HashSet<string>(change.Remove.Concat(change.Enable).Concat(change.Disable).Select(s => s.Id)
                .Concat(change.StageRemove.Concat(change.StageEnable).Concat(change.StageDisable).Select(s => s.Id)));
            foreach (var kv in _pending.Where(kv => !handled.Contains(kv.Key)).ToList())
            {
                var st = _lib.Stages.FirstOrDefault(x => x.Id == kv.Key);
                if (st != null)
                {
                    if (kv.Value && change.StageInstall.Any(p => string.Equals(p.Target.CardPath, st.CardPath, StringComparison.OrdinalIgnoreCase))) continue;
                    if (kv.Value) change.StageEnable.Add(st); else change.StageDisable.Add(st);
                    continue;
                }
                var s = _lib.Skins.FirstOrDefault(x => x.Id == kv.Key);
                if (s == null) continue;
                // a new install into a slot decides that slot: don't turn another skin on there
                if (kv.Value && change.Install.Any(p => p.Character.SlotListId == s.SlotListId && p.Slot.Index == s.SlotIndex)) continue;
                if (kv.Value) change.Enable.Add(s); else change.Disable.Add(s);
            }
            _busy = true;
            Exception error = null;
            using (var progress = new ProgressForm())
            {
                progress.Shown += async (s, e) =>
                {
                    try { await Task.Run(() => _installer.Apply(change, (m, p) => progress.Report(m, p))); }
                    catch (Exception ex) { error = ex; }
                    progress.Close();
                };
                progress.ShowDialog(this);
            }
            _busy = false;
            if (error == null) _pending.Clear();
            _pictureCache.Clear();
            PendingChanged();
            ShowCharacter(refreshPictures: true);
            UpdateStatus();
            if (error != null)
            {
                Program.Trace("apply failed: " + error);
                MessageBox.Show(this, error.Message + "\r\n\r\nNothing was changed on your SD card.", "Couldn't finish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (doneMessage != null)
            {
                var notes = _installer.Notes.ToList();
                string undoLine = _installer.UndoNote ?? "Changed your mind? Profile ▾ > Undo last save puts it back.";
                MessageBox.Show(this, doneMessage + "\r\n\r\n" + undoLine + (notes.Count > 0 ? "\r\n\r\nP+ has changed since some of your mods were made:\r\n• " + string.Join("\r\n• ", notes) : ""),
                    "Done", MessageBoxButtons.OK, notes.Any(n => n.Contains("turned off") || n.Contains("stayed off")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            CheckOverwritten();
            _store.RefreshInstalled();
            UpdateProfileButton();
            return error == null;
        }
    }

    class ProgressForm : Form
    {
        readonly Label _text = Theme.Label("Starting...", Theme.Body);
        readonly ProgressBar _bar = new ProgressBar { Width = 380, Height = 14, Maximum = 100 };
        public ProgressForm()
        {
            Text = "Working"; FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Back; ForeColor = Theme.Text;
            ClientSize = new Size(420, 100); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            _text.Location = new Point(20, 20); _bar.Location = new Point(20, 56);
            Controls.Add(_text); Controls.Add(_bar);
        }
        public void Report(string message, int percent)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { _text.Text = message + "..."; _bar.Value = Math.Max(0, Math.Min(100, percent)); })); }
            catch (InvalidOperationException) { }
        }
    }
}
