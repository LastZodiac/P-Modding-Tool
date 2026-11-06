// The main window: a sidebar (My Mods, Mod Store, Costume Slots, Model Swapper, Frame Data, Stat Tracker, Settings...)
// and the page you picked.
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
    /// <summary>The app's main window: the header, the sidebar and its pages, the notice bars, and everything that changes
    /// the card (switching mods, profiles, cards, adding downloads).</summary>
    class MainForm : Form, IModHost
    {
        BrawlOnlineInstall _install;
        Library _lib;
        Installer _installer;
        readonly Label _status = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Size = new Size(760, 18), BackColor = Color.Transparent };
        readonly ToolTip _statusTip = new ToolTip();
        readonly Panel _banner = new Panel();
        readonly Label _bannerText = Theme.Label("", Theme.Body, Theme.AccentText);
        readonly Panel _appBanner = new Panel();
        readonly Label _appText = Theme.Label("", Theme.Body, Theme.AccentText);
        OnlineUpdate.Release _appRelease;
        readonly Panel _unsafeBanner = new Panel();
        readonly Label _unsafeText = Theme.Label("", Theme.Body, Color.White);
        List<InstalledStage> _unsafe = new List<InstalledStage>();
        readonly Button _addButton = Theme.Secondary("Add from file...");
        readonly Button _profileButton = Theme.Secondary("Profile");
        readonly Button _helpButton = Theme.Secondary("?");
        TutorialOverlay _tour;
        readonly Button _launchModded = Theme.Primary("Launch modded");
        readonly Button _launchClean = Theme.Secondary("Launch unmodded");
        Action _placeHeader = () => { };
        List<InstalledSkin> _overwritten = new List<InstalledSkin>();
        List<InstalledSkin> _tooBig = new List<InstalledSkin>();
        List<InstalledStage> _tooBigStages = new List<InstalledStage>();
        List<InstalledStage> _remake = new List<InstalledStage>();   // stage mods an older version made with a known fault
        bool _remakeCrash;                                           // ...one of which can crash the game
        Button _putBack;
        bool _bannerSerious;   // the mods banner is about something that crashes the game (red), not a notice (gold)
        readonly ToolTip _bannerTip = new ToolTip();

        /// <summary>A notice bar under the header: dark, with a colored stripe on the left saying how much it matters.</summary>
        static void StyleBanner(Panel bar, Label text, Func<Color> stripe)
        {
            bar.Dock = DockStyle.Top; bar.Height = 52; bar.BackColor = Theme.Panel; bar.Visible = false;
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(bar, true);
            text.AutoSize = false; text.AutoEllipsis = true; text.Height = 22; text.Location = new Point(24, 15);
            text.ForeColor = Theme.Text; text.Font = Theme.Body; text.UseMnemonic = false; text.BackColor = Color.Transparent;
            bar.Paint += (s, e) =>
            {
                using (var b = new SolidBrush(stripe())) e.Graphics.FillRectangle(b, 0, 0, 4, bar.Height);
                using (var b = new SolidBrush(Color.FromArgb(18, stripe()))) e.Graphics.FillRectangle(b, 4, 0, bar.Width - 4, bar.Height);
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };
        }

        /// <summary>Puts a notice bar's button at its right end and lets the text use the room left.</summary>
        static void PlaceBannerButton(Panel bar, Label text, Button button)
        {
            button.Location = new Point(bar.Width - button.Width - 18, (bar.Height - button.Height) / 2);
            text.Width = Math.Max(60, button.Left - text.Left - 16);
            bar.Invalidate();
        }

        /// <summary>A mod's name for a sentence: without the file name some downloads add in brackets ("(STGFINAL_LIGHT)").</summary>
        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var s = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\((?=[^)]*_)[A-Z0-9_.]+\)", "").Trim();
            return s.Length > 48 ? s.Substring(0, 46).TrimEnd() + "…" : s;
        }
        List<InstalledStage> _overwrittenStages = new List<InstalledStage>();
        /// <summary>Whether the app is changing the card. When it stops, work that had to wait for the card (a finished
        /// download) runs.</summary>
        bool _busy
        {
            get => Program.Busy;
            set
            {
                Program.Busy = value;
                if (!value && _afterCardWork.Count > 0)   // what had to wait for the card (a finished download): now
                {
                    var todo = _afterCardWork.ToList(); _afterCardWork.Clear();
                    BeginInvoke((Action)(() => { foreach (var a in todo) a(); }));
                }
            }
        }
        readonly List<Action> _afterCardWork = new List<Action>();
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
        LiveMatchPanel _livePage;
        LogFollower _log;
        LiveMatch _live = new LiveMatch();
        MatchHistory _cardHistory;
        DateTime _reloadAt = DateTime.MaxValue;
        bool _historyLoading;

        FullOverlay _full;
        ReportCard _report;
        DateTime _checkAgainAt = DateTime.MaxValue;
        DateTime _seenBefore;   // the newest game the Stat Tracker knew of before it looked again after a game

        /// <summary>A game just ended: its report over the game (if the overlay is on), and the Stat Tracker shows it.</summary>
        void ShowReport(long gameId)
        {
            Program.Doing = "showing the after-game report";
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
                if (_report == null)
                {
                    _report = new ReportCard { Spot = _look.ReportSpot };
                    _report.Dragged += () => { _look.ReportSpot = _report.Spot; _look.Save(); };
                }
                _report.Game = _game ?? (_install != null ? GameWindow.Find(_install) : null);
                _report.Left = _look.OverlayLeft == true;
                var coach = _stats.Coach(gameId, c =>
                {
                    _lastCoach = Line(c);
                    if (_card != null && _card.Visible && _card.State?.Phase == MatchPhase.GameOver) { _card.CoachLine = _lastCoach; _card.Refresh2(); }
                    if (_report != null && _report.Visible && _report.GameId == gameId) _report.SetCoach(c);
                });
                _lastCoach = Line(coach);
                _report.Show(p, smash, x.Me, x.Them, d.History.StageName(x.Game.Stage), coach);
                PlaceReport();
            }
            catch (Exception e) { Program.Log("after-game report", e); }
            finally { Program.Doing = null; }
        }
        /// <summary>The after-game report sits right under the opponent card.</summary>
        void PlaceReport()
        {
            if (_report == null || !_report.Visible) return;
            _report.Place(_card != null && _card.Visible ? _card.Bounds : Rectangle.Empty);
        }

        string _lastCoach;
        /// <summary>The coach's main note on a game, for the card between games.</summary>
        static string Line(GameCoach c) => c == null ? null : c.Tips.Count > 0 ? DeathCoach.Say(c.Tips.FirstOrDefault(t => t.Saved) ?? c.Tips[0]) : c.Facts.FirstOrDefault();
        DateTime _pillAt = DateTime.MaxValue, _gameLookAt, _hideCardAt = DateTime.MaxValue;
        string _cardSig; MatchPhase _cardPhase;
        Rectangle? _game;

        /// <summary>Reads what the game wrote since last time and shows, updates or hides the overlay.</summary>
        void LiveTick()
        {
            Program.Doing = "following the game's log";
            try
            {
                // where the game is (it can be moved or resized), and whether it's running at all: checked every 2 seconds
                if (_install != null && DateTime.Now >= _gameLookAt)
                {
                    _gameLookAt = DateTime.Now.AddSeconds(2);
                    var g = GameWindow.Find(_install);
                    GamePriority(GameWindow.Running);
                    if (g != _game) { _game = g; if (_card != null) { _card.Game = g; if (_card.Visible) _card.Place(); } if (_full != null && _full.Visible) _full.Place(g); if (_report != null) { _report.Game = g; PlaceReport(); } }
                    // the game was closed (or minimized): nothing stays over the desktop (the log may never say the match ended)
                    if (g == null && !Program.Testing) { if (++_gameGone >= 2) HideOverlays(); } else _gameGone = 0;
                }
                if (!_look.Stats || _install == null) { _card?.Hide(); _full?.Hide(); UpdateLivePage(false); return; }
                if (_log == null) _log = new LogFollower(LogFollower.LogPath(_install));
                bool changed = false;
                foreach (var line in _log.NewLines()) changed |= _live.Feed(line);
                // every game the log shows finishing is kept, so it counts even if Brawl Online couldn't save its replay
                foreach (var done in _live.TakeFinished()) LoggedGames.Add(StatsPanel.KeptFolder, done);
                var s = _live.State;
                UpdateLivePage(true);
                if (!_look.Card) { _card?.Hide(); _full?.Hide(); if (changed && s.Phase == MatchPhase.GameOver) _reloadAt = DateTime.Now.AddSeconds(4); if (DateTime.Now >= _reloadAt) { _reloadAt = DateTime.MaxValue; _stats.CheckNewGame(); } return; }
                if (DateTime.Now >= _reloadAt) { _reloadAt = DateTime.MaxValue; _cardHistory = null; changed = true; _seenBefore = _stats.NewestSeen; _stats.CheckNewGame(); _checkAgainAt = DateTime.Now.AddSeconds(12); }
                // in case the game was saved late (only when the first look didn't find it: reading everything again costs time)
                if (DateTime.Now >= _checkAgainAt) { _checkAgainAt = DateTime.MaxValue; if (_stats.NewestSeen == _seenBefore) _stats.CheckNewGame(); }
                if (changed)
                {
                    if (_cardHistory == null && !_historyLoading) LoadCardHistory();
                    if (s.Phase == MatchPhase.Playing) { if (_cardPhase != MatchPhase.Playing) _pillAt = DateTime.Now.AddSeconds(12); }   // the slim bar soon after the fight starts
                    else _pillAt = DateTime.MaxValue;
                    _cardPhase = s.Phase;
                    if (s.Phase == MatchPhase.GameOver) { _reloadAt = DateTime.Now.AddSeconds(4); _lastCoach = null; }   // Brawl Online has saved the game by then
                    bool show = s.Phase == MatchPhase.Found || s.Phase == MatchPhase.Loading || s.Phase == MatchPhase.Playing || s.Phase == MatchPhase.GameOver;
                    if (show && s.OpponentName != null)
                    {
                        _hideCardAt = DateTime.MaxValue;
                        if (_card == null)
                        {
                            _card = new LiveCard { Scout = id => _stats.Scout(id, 2), Expanded = _look.CardOpen == true, Spot = _look.CardSpot };
                            _card.Dragged += () => { _look.CardSpot = _card.Spot; _look.Save(); PlaceReport(); };
                            _card.ExpandedChanged += open => { _look.CardOpen = open ? (bool?)true : null; _look.Save(); PlaceReport(); };
                            _card.VisibleChanged += (o, e) => PlaceReport();
                        }
                        // only redraw the card when something it shows changed (the log says a lot; redrawing it all the time flickers)
                        string sig = $"{s.Phase}|{s.OpponentCode}|{s.MyChar}|{s.TheirChar}|{s.Stage}|{s.Game}|{s.MyWins}|{s.TheirWins}|{s.StocksTaken}|{s.StocksLost}|{_cardHistory?.Games.Count}|{_lastCoach}|{_look.OverlayLeft}";
                        if (sig != _cardSig || !_card.Visible)
                        {
                            _cardSig = sig;
                            _card.Game = _game; _card.Left = _look.OverlayLeft == true; _card.CoachLine = s.Phase == MatchPhase.GameOver ? _lastCoach : null;
                            if (s.Phase != MatchPhase.Playing) _card.Pill = false;
                            _card.State = s.Copy(); _card.History = _cardHistory; if (_card.Art == null) _card.Art = StatsArt.Cached;
                            _card.Refresh2(); if (!_card.Visible || _card.Fading) _card.Appear();
                            PlaceReport();
                        }
                    }
                    else if (_card != null && _card.Visible && _hideCardAt == DateTime.MaxValue) _hideCardAt = DateTime.Now.AddSeconds(8);   // a room between games, a retry: don't blink
                    if ((s.Phase == MatchPhase.Found || s.Phase == MatchPhase.Loading) && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_REPORT") == null) _report?.Disappear();   // on to the next game
                    if (_full != null && _full.Visible) { _full.State = s.Copy(); _full.History = _cardHistory; _full.Invalidate(); }
                }
                if (_card != null && _card.Visible && DateTime.Now >= _hideCardAt) { _hideCardAt = DateTime.MaxValue; _card.Disappear(); _cardSig = null; }
                if (_card != null && _card.Visible && DateTime.Now >= _pillAt) { _pillAt = DateTime.MaxValue; _card.Pill = true; _card.Refresh2(); }
                var flag = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_FULL_FLAG");   // UI tests: the hotkey, by a file
                if (flag != null && System.IO.File.Exists(flag) != (_full != null && _full.Visible)) ToggleFull();
            }
            catch (Exception e) { Program.Log("overlay", e); }
            finally { Program.Doing = null; }
        }

        int _gameGone;          // checks in a row the game's window wasn't found
        bool _lowPriority;      // the app runs below the game's priority (while the game is running)

        /// <summary>Takes everything over the game away (the game was closed): the card, the report, the full overlay.</summary>
        void HideOverlays()
        {
            if (_card != null && _card.Visible) { _card.Disappear(); _cardSig = null; }
            _hideCardAt = DateTime.MaxValue; _pillAt = DateTime.MaxValue;
            if (_report != null && _report.Visible) _report.Disappear();
            if (_full != null && _full.Visible) _full.Hide();
        }

        /// <summary>While the game runs, the app's own work (reading replays, the after-game notes, pictures) runs below
        /// it, so the game always gets the computer first; back to normal once it's closed.</summary>
        void GamePriority(bool game)
        {
            if (game == _lowPriority) return;
            _lowPriority = game;
            try { using (var me = Process.GetCurrentProcess()) me.PriorityClass = game ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; }
            catch (Exception e) { Program.Log("setting the app's priority", e); }
        }

        /// <summary>The Live Match page (when on show) and the recording dot by its name in the menu.</summary>
        void UpdateLivePage(bool following)
        {
            if (_livePage == null) return;
            var s = _live.State;
            bool rec = following && s.Phase == MatchPhase.Playing;
            if (_nav.TryGetValue(_livePage, out var nb) && (nb.Live != rec || rec)) { nb.Live = rec; nb.Invalidate(); }
            if (_livePage.Visible)
            {
                if (_cardHistory == null && !_historyLoading && following) LoadCardHistory();
                _livePage.Show(s, _live.Events, _cardHistory, following);
            }
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
            Program.HotkeyProblem = ok ? null : $"Another program already uses {_look.HotkeyText}, so it can't open the overlay. Pick a different key:";
        }

        DateTime _historyFailedAt;
        /// <summary>Reads your match history for the overlay over the game, in the background. If it fails, the overlay says so
        /// and it isn't tried again for 30 seconds.</summary>
        async void LoadCardHistory()
        {
            if (DateTime.Now - _historyFailedAt < TimeSpan.FromSeconds(30)) return;
            _historyLoading = true;
            Program.HistoryProblem = null;
            try
            {
                var install = _install; var installer = _installer; var known = _stats?.StageNames;
                _cardHistory = await System.Threading.Tasks.Task.Run(() =>
                {
                    // the stage names: the Stat Tracker's when it has read them (no need to open the SD card again)
                    Dictionary<int, string> names = known;
                    if (names == null)
                        try { using (var card = installer?.OpenCard()) names = card != null ? StageSelect.SlotListNames(card) : new Dictionary<int, string>(); } catch { names = new Dictionary<int, string>(); }
                    var path = MatchHistory.DatabasePath(install);
                    return MatchHistory.Load(path, names, StatsPanel.KeptFolder);
                });
                var art = await System.Threading.Tasks.Task.Run(() => StatsArt.Get(() => _installer));
                if (_card != null) { _card.History = _cardHistory; _card.Art = art; _card.Refresh2(); }
                if (_full != null) { _full.History = _cardHistory; _full.Art = art; _full.Invalidate(); }
            }
            catch (Exception e)
            {
                Program.Log("opponent card history", e);
                // said on the card instead of "Reading..." forever; tried again a bit later, not on every change to the game's log
                Program.HistoryProblem = "Couldn't read your match history. " + Friendly.Explain(e);
                _historyFailedAt = DateTime.Now;
                _card?.Invalidate(); _full?.Invalidate();
            }
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

        /// <summary>Builds the window: header, sidebar, pages, notice bars and save bar. openOnStart: a mod dropped on the app's
        /// icon, added once it has started.</summary>
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
                if (f.Any(MusicImport.IsAudio)) AddOwnMusic(MusicImportForm.AudioIn(f));   // your own songs (MP3/WAV)
                // a folder with only your own songs in it (no mod files): all its songs
                else if (f.Length == 1 && Directory.Exists(f[0]) && MusicImportForm.AudioIn(f).Count > 0 && !LooksLikeMod(f[0])) AddOwnMusic(MusicImportForm.AudioIn(f));
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
            void FitStatus() => _status.Width = Math.Max(120, buttons.Left - _status.Left - 16);
            header.Resize += (s, e) => FitStatus(); buttons.SizeChanged += (s, e) => FitStatus(); buttons.LocationChanged += (s, e) => FitStatus();
            _profileButton.Click += (s, e) => ShowProfileMenu();
            _profileButton.Enabled = false;
            _addButton.AutoSize = false;
            _addButton.Size = new Size(TextRenderer.MeasureText(_addButton.Text, _addButton.Font).Width + 40, 36);

            // banner shown after a P+ update replaced skins
            StyleBanner(_banner, _bannerText, () => _bannerSerious ? Theme.Bad : Theme.Accent);
            var putBack = Theme.Primary("Put them back");
            putBack.Click += (s, e) =>
            {
                if (_overwritten.Count + _overwrittenStages.Count > 0)
                {
                    var c = new Installer.Change { Reapply = _overwritten.ToList() }; c.StageReapply.AddRange(_overwrittenStages);
                    Run(c, "Your mods are back (each one re-checked against the new P+).");
                }
                else if (_tooBig.Count > 0)
                    Run(new Installer.Change { Refit = _tooBig.ToList() }, "Done: your costumes fit now. Only their pictures were made smaller.");
                else if (_tooBigStages.Count > 0)
                {
                    var c = new Installer.Change(); c.StageReapply.AddRange(_tooBigStages);
                    Run(c, "Done: your stages fit now and look the same.");
                }
                else if (_remake.Count > 0)
                {
                    var c = new Installer.Change(); c.StageReapply.AddRange(_remake);
                    Run(c, "Done: those stage mods were made again. You still play on P+'s stages exactly as P+ made them, so they're safe online.");
                }
            };
            _putBack = putBack;
            _banner.Controls.Add(_bannerText); _banner.Controls.Add(putBack);
            _banner.Resize += (s, e) => PlaceBannerButton(_banner, _bannerText, putBack);

            // banner for a new version of the app
            StyleBanner(_appBanner, _appText, () => Theme.Good);
            var updateNow = Theme.Primary("Update now");
            var whatsNew = new LinkLabel { Text = "What's new", AutoSize = true, Font = Theme.Body, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent };
            whatsNew.LinkClicked += (s, e) => { if (_appRelease?.Page != null) try { Process.Start(new ProcessStartInfo(_appRelease.Page) { UseShellExecute = true }); } catch { } };
            updateNow.Click += async (s, e) => await UpdateApp(updateNow);
            _appBanner.Controls.Add(_appText); _appBanner.Controls.Add(whatsNew); _appBanner.Controls.Add(updateNow);
            _appBanner.Resize += (s, e) =>
            {
                PlaceBannerButton(_appBanner, _appText, updateNow);
                whatsNew.Location = new Point(updateNow.Left - whatsNew.Width - 16, (_appBanner.Height - whatsNew.Height) / 2);
                _appText.Width = Math.Max(60, whatsNew.Left - _appText.Left - 16);
            };

            // banner for stage mods that don't pass today's safety check
            StyleBanner(_unsafeBanner, _unsafeText, () => Theme.Bad);
            var turnOff = Theme.Primary("Turn them off"); turnOff.BackColor = Theme.Bad; turnOff.ForeColor = Color.White; turnOff.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 120, 120);
            turnOff.Click += (s, e) =>
            {
                var c = new Installer.Change(); c.StageDisable.AddRange(_unsafe);
                if (Run(c, "Turned off. Want the look anyway? Add the mod again and pick \"Use its looks on P+'s stage\": that version is safe online."))
                    _unsafeBanner.Visible = false;
            };
            _unsafeBanner.Controls.Add(_unsafeText); _unsafeBanner.Controls.Add(turnOff);
            _unsafeBanner.Resize += (s, e) => PlaceBannerButton(_unsafeBanner, _unsafeText, turnOff);

            // save bar
            _saveText.Location = new Point(20, 17);
            var save = Theme.Secondary("Save to SD card"); save.BackColor = Theme.AccentText; save.ForeColor = Theme.Accent; save.Font = Theme.BodyBold; save.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 40, 22);
            save.Click += (s, e) => SavePending();
            var undo = Theme.Primary("Discard changes"); undo.Font = Theme.Body;
            undo.Click += (s, e) =>
            {
                if (Dialog.Show(this, "Forget the switches you haven't saved yet? Your SD card stays exactly as it is.", "Discard changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _pending.Clear(); PendingChanged();
            };
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
            _look.ApplyOverlay();
            _settings = new SettingsPanel(_look, () => { _look.ApplyOverlay(); if (_card != null) { _card.Spot = _look.CardSpot; if (_card.Visible) _card.Place(); } if (_report != null) { _report.Spot = _look.ReportSpot; PlaceReport(); } _card?.Invalidate(); _report?.Invalidate(); _full?.Invalidate(); _backdrop.Apply(_look); _statsToggle?.Invoke(); ApplyHotkey(); Invalidate(true); }) { Visible = false, SaveFirst = () => SavedFirst() };
            _settings.DatabasePath = () => _install == null ? null : MatchHistory.DatabasePath(_install);
            _settings.ReplayFoldersChanged = () => { _cardHistory = null; _stats?.CheckNewGame(); };
            _content.Controls.Add(_library); _content.Controls.Add(_store); _content.Controls.Add(_slotsPage); _content.Controls.Add(_swap); _content.Controls.Add(_settings);
            _credits = new CreditsPanel { Visible = false }; _content.Controls.Add(_credits);
            _stats = new StatsPanel(() => _install, () => _installer) { Visible = false }; _content.Controls.Add(_stats);
            _stats.Launcher = FindLauncher;
            _stats.NewGame += ShowReport;
            _frameData = new FrameDataPanel(() => _installer) { Visible = false }; _content.Controls.Add(_frameData);
            _livePage = new LiveMatchPanel { Visible = false }; _content.Controls.Add(_livePage);

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
            y += 14;
            Section("TOOLS");
            Nav("Model Swapper", "\uE7AD", _swap);
            Nav("Frame Data", "\uE916", _frameData);
            Nav("Stat Tracker", "\uE9D2", _stats);
            Nav("Live Match", "\uE7C8", _livePage);
            int bottomStart = y + 14;
            Nav("Settings", "\uE713", _settings);
            Nav("Credits", "\uE734", _credits);
            var settingsNav = _nav[_settings]; var creditsNav = _nav[_credits];
            var divider = new Panel { Height = 1, BackColor = Color.FromArgb(40, 255, 255, 255), Width = 184 };
            side.Controls.Add(divider);
            side.Resize += (s, e) =>
            {
                int top = Math.Max(bottomStart, side.Height - 2 * 44 - 14);   // pinned to the bottom when there's room
                divider.Location = new Point(12, top - 8);
                settingsNav.Top = top; creditsNav.Top = top + 44;
            };
            void StatsShown() { _nav[_stats].Visible = _look.Stats; _nav[_livePage].Visible = _look.Stats; if (!_look.Stats && (_stats.Visible || _livePage.Visible)) ShowPage(_library); }
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
            Shown += (s, e) =>
            {
                ApplyHotkey(); Startup(); Watchdog.Start(this);
                // UI tests: the window stops answering for 3 seconds once, so the watchdog's note can be checked
                if (Environment.GetEnvironmentVariable("SKINMANAGER_TEST_FREEZE") != null)
                {
                    var freeze = new Timer { Interval = 5000 };
                    freeze.Tick += (o, a) => { freeze.Stop(); Program.Doing = "a test freeze"; System.Threading.Thread.Sleep(3000); Program.Doing = null; };
                    freeze.Start();
                }
            };
            // the overlays are windows of their own: closed with the app, so none can be left over the desktop
            FormClosed += (s, e) => { foreach (var o in new Form[] { _card, _report, _full }) try { o?.Close(); } catch { } };
            Activated += (s, e) => { if (_install != null && !_busy) UpdateStatus(); };
            FormClosing += (s, e) =>
            {
                // never in the middle of changing the SD card (a save, an undo, a card switch): stopping half way could leave it half done
                if (_busy) { e.Cancel = true; if (e.CloseReason != CloseReason.WindowsShutDown) Dialog.Show(this, "The app is still changing your SD card. Wait for it to finish, then close it.", "One moment", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                if (_pending.Count == 0) return;
                var answer = Dialog.Show(this, $"You have {(_pending.Count == 1 ? "1 change" : _pending.Count + " changes")} that aren't saved to your SD card yet.\r\n\r\nSave before closing?", "Save changes?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
        /// <summary>A page's name as the sidebar shows it (tours are kept by these names).</summary>
        string PageName(Control page) =>
            page == _library ? "My Mods" : page == _store ? "Mod Store" : page == _slotsPage ? "Costume Slots" : page == _swap ? "Model Swapper"
            : page == _settings ? "Settings" : page == _frameData ? "Frame Data" : page == _stats ? "Stat Tracker" : page == _credits ? "Credits" : page == _livePage ? "Live Match" : null;

        /// <summary>The tour of the page on show (asked: with the "?" button; otherwise the first time).</summary>
        void ShowTour(bool asked)
        {
            if (_tour != null || _shownPage == null || !Visible || WindowState == FormWindowState.Minimized || OwnedForms.Any(f => f.Visible) || Program.Testing && !asked && Environment.GetEnvironmentVariable("SKINMANAGER_TEST_TOURS") == null) return;
            string name = PageName(_shownPage);
            var steps = name == null ? null : Tutorials.For(name, _shownPage, _header);
            if (steps == null || steps.Count == 0) { if (asked) Dialog.Show(this, "This page has no tour.", "Tour", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!_look.ToursSeen.Contains(name)) { _look.ToursSeen.Add(name); _look.Save(); }
            _tour = new TutorialOverlay(name, steps);
            _tour.Finished += () => _tour = null;
            _tour.TurnedOff += () => { _look.Tours = false; _look.Save(); };
            _tour.Start(this);
        }

        /// <summary>Settings' "How accurate is the frame data?": Frame Data, on its accuracy view.</summary>
        public void ShowFrameDataAccuracy() { ShowPage(_frameData); _frameData.ShowAccuracy(); }

        /// <summary>Shows a page and lights up its sidebar button; the first time, its tour starts once the page has drawn.</summary>
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

        /// <summary>Builds the Costume Slots page: the list of characters on the left, their costume slots on the right.</summary>
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

        /// <summary>Finds Brawl Online (or your own Dolphin's card, if picked in Settings) and loads your mods; with neither,
        /// offers to pick a Dolphin. A list that can't be loaded leaves the app usable without it.</summary>
        void Startup()
        {
            Program.Trace("startup");
            _install = BrawlOnlineInstall.Find();
            Program.Trace("brawl online: " + (_install?.NetplayFolder ?? "not found"));
            // your own Dolphin's SD card instead (picked in Settings)
            if (!string.IsNullOrEmpty(_look.CardFile))
            {
                var other = OtherCards.Find(_look.CardFile);
                if (other.Problem == null)
                {
                    if (_install == null) _install = new BrawlOnlineInstall { NetplayFolder = other.Dolphin };
                    _install.CardOverride = other.Card; _install.OtherDolphin = other.Dolphin;
                    Build.Root = other.Root ?? Build.PPlusRoot;
                    Program.Trace("using another card: " + other.Card);
                }
                else BeginInvoke((Action)(() => Dialog.Show(this, "The app was set to use your own Dolphin's SD card, but it can't right now:\r\n\r\n" + other.Problem +
                    "\r\n\r\nIt's using Brawl Online's card instead. You can pick it again in Settings.", "Your Dolphin's SD card", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
            if (_install == null)
            {
                _status.Text = "Brawl Online was not found on this PC.";
                _status.ForeColor = Theme.Bad;
                _addButton.Enabled = false;
                if (Dialog.Show(this, "Brawl Online isn't set up on this PC.\r\n\r\nPlay P+ (or a mod built on it) in your own Dolphin instead? Click Yes and pick that Dolphin's folder: " +
                        "the app then puts mods on its SD card, and Frame Data reads its characters.\r\n\r\n(For Brawl Online: click No, open Brawl Online once and let it finish setting up Project+, then open this app again.)",
                        "P+ Modding Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    BeginInvoke((Action)(() => { ShowPage(_settings); _settings.PickDolphin(); }));
                return;
            }
            try { StartWithCard(); }
            catch (Exception e)
            {
                // a damaged mods list (or a card that can't be read) mustn't leave the app half set up and crashing later
                Program.Log("startup", e);
                _lib = null; _installer = null;
                _status.Text = "Couldn't load your mods. " + Friendly.Explain(e); _status.ForeColor = Theme.Bad;
                _addButton.Enabled = false;
                Dialog.Show(this, "The app couldn't load your list of mods:\r\n\r\n" + Friendly.Explain(e) +
                    "\r\n\r\nYour SD card wasn't changed. Reporting it (Settings > Report a problem) gets it fixed; Frame Data and the Stat Tracker still work.",
                    "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Opens the card: puts back a save that was cut off, loads the mods list, then runs the checks done at start
        /// (replaced mods, versions, mod updates, hand-made changes, app update).</summary>
        void StartWithCard()
        {
            _cards = new Cards(_install, CardData);
            _lastSave = new LastSave(CardData);
            // a save that was stopped halfway (the app or the PC shut down) is put back before anything else
            try
            {
                string note = _lastSave.RecoverInterruptedSave(_install.CardPath);
                if (note != null) BeginInvoke((Action)(() => Dialog.Show(this, note, "Your last save", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            catch (Exception e)
            {
                BeginInvoke((Action)(() => Dialog.Show(this, "Your last save was stopped before it finished, and the SD card couldn't be put back yet (" + Friendly.Explain(e) +
                    "). Close Brawl Online if it's open, then close and reopen this app.", "Your last save", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
            _launchClean.Visible = _install.OwnCard;
            if (!_install.OwnCard) _launchModded.Text = "Open my Dolphin";
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
            RescanHand();
            CheckAppUpdate(false);
            // one question at a time: the shortcuts offer waits for a start when nothing else had to be said
            if (!_saidAtStart) BeginInvoke((Action)(() => SelfUpdate.OfferShortcuts(this)));
            if (_openOnStart != null) BeginInvoke((Action)(() => AddSkin(_openOnStart)));
        }

        bool _saidAtStart;   // a message was already shown at startup

        string CardName => _install != null && !_install.OwnCard ? "Your Dolphin's SD card (not used online)" : _cards?.HasBig == true ? Cards.Label(_cards.Active) : "Your P+ SD card";

        bool _statusRunning, _statusAgain;
        /// <summary>The header's line about the SD card (free space, whether Brawl Online is open). Read in the background:
        /// it runs every time the window comes back into focus, so it must never make the window wait.</summary>
        async void UpdateStatus()
        {
            if (_installer == null || _install == null) return;
            if (_statusRunning) { _statusAgain = true; return; }
            _statusRunning = true;
            try
            {
                var installer = _installer; var install = _install; var lib = _lib;
                var (space, running, what) = await Task.Run(() =>
                {
                    using (var card = installer.OpenCard())
                    {
                        var sp = SkinManager.Core.Space.Measure(card, lib);
                        bool r = install.IsRunning(out string w);
                        return (sp, r, w);
                    }
                });
                if (IsDisposed) return;
                _space = space;
                _library.ShowSpace(_space);
                string free = $"{SkinManager.Core.Space.MB(_space.Usable)} free for mods";
                _statusTip.SetToolTip(_status, $"{CardName}: {free}");
                if (_updateNote != null && !running) { _status.Text = $"{free}  ·  {_updateNote}"; _status.ForeColor = Theme.Warn; _statusTip.SetToolTip(_status, $"{CardName}: {free}\r\n{_updateNote}"); return; }
                _status.Text = running ? $"{free}  ·  Close {what} to change mods" : free;
                _status.ForeColor = running ? Theme.Warn : Theme.Muted;
            }
            catch (Exception e)
            {
                Program.Log("reading the SD card", e);
                if (!IsDisposed) { _status.Text = "Couldn't read the SD card. " + Friendly.Explain(e); _status.ForeColor = Theme.Bad; }
            }
            finally
            {
                _statusRunning = false;
                if (_statusAgain && !IsDisposed) { _statusAgain = false; UpdateStatus(); }
            }
        }

        /// <summary>Looks for a newer version of the app on its releases page (asked: right now, and says so when there's none).</summary>
        public void CheckAppUpdate(bool asked)
        {
            if (OnlineUpdate.Repo == null) { if (asked) Dialog.Show(this, "This copy of the app doesn't have a releases page set, so it can't look for new versions.", "App updates"); return; }
            if (!asked && !OnlineUpdate.Due()) return;
            Task.Run(() => OnlineUpdate.Check()).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.IsFaulted) { Program.Log("checking for a new version", t.Exception); if (asked) Dialog.Show(this, "Couldn't reach the releases page. Check your internet and try again.", "App updates"); return; }
                _appRelease = t.Result;
                if (_appRelease == null) { if (asked) Dialog.Show(this, $"You have the newest version ({OnlineUpdate.CurrentText}).", "App updates"); return; }
                _appText.Text = $"Version {_appRelease.Version.ToString(3)} is out (you have {OnlineUpdate.CurrentText}). Updating doesn't touch your mods.";
                _appBanner.Visible = true;
                _appBanner.PerformLayout(); _appBanner.Width += 0;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Downloads and checks the new version of the app, then restarts into it (the swap happens on start).</summary>
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
                Dialog.Show(this, "The update couldn't be downloaded: " + Friendly.Explain(e) + "\r\n\r\nNothing was changed. You can try again later.", "App updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                button.Enabled = true;
            }
        }

        /// <summary>Shows the red bar when a stage mod that's on plays differently from P+'s stage (it could desync online).</summary>
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
                    _unsafeText.Text = (_unsafe.Count == 1 ? $"{Short(_unsafe[0].Name)} plays differently from P+'s stage" : $"{_unsafe.Count} stage mods play differently from P+'s stages")
                        + " and could desync online games.";
                    foreach (var x in t.Result) Program.Trace("unsafe: " + x.Stage.Name + ": " + x.Why);
                    new ToolTip().SetToolTip(_unsafeText, string.Join("\r\n\r\n", t.Result.Select(x => x.Stage.Name + ": " + x.Why)));
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Stage mods to make again (Installer.NeedsRemaking), and whether one can crash the game.</summary>
        (List<InstalledStage> list, bool crash) Remake(Fat32Image card)
        {
            var list = _installer.NeedsRemaking(card, out bool crash);
            return (list, crash);
        }

        /// <summary>Updates the mods bar: mods a P+ update replaced, costumes or stages too big for the game's memory, or
        /// costumes that can have their full-quality pictures back.</summary>
        void CheckOverwritten()
        {
            CheckUnsafe();
            if (_lib.Skins.Count == 0 && _lib.Stages.Count == 0) { _banner.Visible = false; return; }
            Task.Run(() => { using (var card = _installer.OpenCard()) return (_installer.Overwritten(card), _installer.OverwrittenStages(card), _installer.TooBig(card), _installer.ShrunkForNothing(card), _installer.TooBigStages(), Remake(card)); })
                .ContinueWith(t =>
                {
                    if (t.IsFaulted) return;
                    _overwritten = t.Result.Item1; _overwrittenStages = t.Result.Item2;
                    _tooBig = t.Result.Item3.Concat(t.Result.Item4).Where(x => !_overwritten.Contains(x)).ToList();
                    var shrunk = t.Result.Item4.Where(x => !_overwritten.Contains(x)).ToList();
                    _tooBigStages = t.Result.Item5.Where(x => !_overwrittenStages.Any(o => o.Id == x.Id)).ToList();
                    _remakeCrash = t.Result.Item6.crash;
                    _remake = t.Result.Item6.list.Where(x => !_overwrittenStages.Any(o => o.Id == x.Id) && !_tooBigStages.Any(o => o.Id == x.Id)).ToList();
                    int n = _overwritten.Count + _overwrittenStages.Count;
                    _banner.Visible = n > 0 || _tooBig.Count > 0 || _tooBigStages.Count > 0 || _remake.Count > 0;
                    _bannerSerious = false;
                    string full = null;
                    if (n > 0)
                    {
                        var names = _overwritten.Select(x => x.Name).Concat(_overwrittenStages.Select(x => x.Name)).ToList();
                        _bannerText.Text = n == 1 ? $"A P+ update replaced {Short(names[0])}." : $"A P+ update replaced {n} of your mods.";
                        full = "Replaced by the P+ update:\r\n" + string.Join("\r\n", names);
                        _putBack.Text = "Put them back";
                    }
                    else if (_tooBig.Count > shrunk.Count)
                    {
                        var big = _tooBig.Where(x => !shrunk.Contains(x)).ToList();
                        _bannerSerious = true;
                        _bannerText.Text = (big.Count == 1 ? $"{Short(big[0].Name)} is" : $"{big.Count} costumes are") + " too big and will crash the game when picked.";
                        full = string.Join("\r\n", big.Select(x => x.Name)) + (shrunk.Count > 0 ? $"\r\n\r\n({shrunk.Count} more can also go back to full-quality pictures.)" : "");
                        _putBack.Text = "Make them fit";
                    }
                    else if (_tooBig.Count == 0 && _tooBigStages.Count > 0)
                    {
                        var names = _tooBigStages.Select(x => x.Name).Distinct().ToList();
                        _bannerSerious = true;
                        _bannerText.Text = (names.Count == 1 ? $"{Short(names[0])} is" : $"{names.Count} stages are") + " too big and will crash the game when loaded.";
                        full = string.Join("\r\n", names);
                        _putBack.Text = "Make them fit";
                    }
                    else if (_tooBig.Count == 0 && _remake.Count > 0)
                    {
                        var names = _remake.Select(x => x.Name).Distinct().ToList();
                        _bannerSerious = _remakeCrash;
                        _bannerText.Text = _remakeCrash
                            ? (names.Count == 1 ? $"{Short(names[0])} can crash the game when the stage loads." : $"{names.Count} stage mods need making again (one can crash the game when its stage loads).")
                            : (names.Count == 1 ? $"{Short(names[0])}'s ground shows off the floor you stand on." : $"{names.Count} stage mods show their ground off the floor you stand on.");
                        full = "Made by an older version of the app:\r\n" + string.Join("\r\n", names) + "\r\n\r\nMaking them again only changes what you see; you still play on P+'s stages exactly as P+ made them.";
                        _putBack.Text = "Fix them";
                    }
                    else if (shrunk.Count > 0)
                    {
                        _bannerText.Text = shrunk.Count == 1 ? $"{Short(shrunk[0].Name)} can use its full-quality pictures." : $"{shrunk.Count} costumes can use their full-quality pictures.";
                        full = "These fit in the game's memory without being made smaller:\r\n" + string.Join("\r\n", shrunk.Select(x => x.Name));
                        _putBack.Text = "Restore quality";
                    }
                    if (_bannerSerious) { _putBack.BackColor = Theme.Bad; _putBack.ForeColor = Color.White; _putBack.FlatAppearance.MouseOverBackColor = Color.FromArgb(250, 120, 120); }
                    else { _putBack.BackColor = Theme.Accent; _putBack.ForeColor = Theme.AccentText; _putBack.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 196, 92); }
                    _bannerTip.SetToolTip(_bannerText, full ?? _bannerText.Text);
                    _putBack.Width = _putBack.GetPreferredSize(Size.Empty).Width;
                    PlaceBannerButton(_banner, _bannerText, _putBack);

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

        /// <summary>The Profile menu: your profiles, new and manage, and (on Brawl Online's card) the SD cards, undo last save,
        /// and making or deleting the big and clean cards.</summary>
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
            var manage = new ToolStripMenuItem("Manage profiles (rename, copy, share)...") { ForeColor = Theme.Text };
            manage.Click += (s, e) => ManageProfiles();
            menu.Items.Add(manage);
            // the SD cards
            if (_cards != null && _install.OwnCard)
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
                        if (Dialog.Show(this, "Delete the clean card (P+ with no mods)? Your mods and your normal SD card aren't touched. The app makes a new clean card the next time you use \"Launch unmodded\".",
                            "Delete the clean card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        try { _cards.DeleteClean(); } catch (Exception ex) { Dialog.Show(this, Friendly.Explain(ex), "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            if (string.IsNullOrEmpty(v.Launcher) && string.IsNullOrEmpty(v.PPlus)) return;   // no Brawl Online (your own Dolphin only): nothing to compare
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
                    _saidAtStart = true;
                    BeginInvoke((Action)(() => Dialog.Show(this, "Updated since you last opened the app:\r\n\r\n• " + string.Join("\r\n• ", lines), "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            Task.Run(() =>
            {
                var notes = new List<string>();
                var release = Versions.Release(_install, v.PPlus);
                FindOriginalFixes(release);
                try { using (var card = _installer.OpenCard()) if (_install.OwnCard && release != null && !Versions.IsOn(card, release)) notes.Add($"this card isn't on P+ {v.PPlus}: open Brawl Online once so it updates it"); } catch { }
                if (v.NewestPPlus != null && Versions.Newer(v.NewestPPlus, v.PPlus)) notes.Add($"P+ {v.NewestPPlus} is out: open Brawl Online to get it");
                string latest = Versions.LatestLauncherOnline();
                if (latest != null && v.Launcher != null && Versions.Newer(latest, v.Launcher)) notes.Add($"Brawl Online {latest} is out: open Brawl Online to get it");
                return notes;
            }).ContinueWith(t =>
            {
                ApplyOriginalFixes();
                if (t.IsFaulted || t.Result.Count == 0) return;
                _updateNote = char.ToUpper(t.Result[0][0]) + string.Join("; ", t.Result).Substring(1);
                UpdateStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ----- texture packs -----

        public bool OnCleanCard => _cards?.Active == Cards.Clean;

        // where P+'s stages are on the stage select: read once from the card in the background
        StageSelect _stageSelect; bool _stageSelectReading;
        /// <summary>Where a stage mod is on the stage select. The first ask starts reading it in the background and returns
        /// null; My Mods refreshes when it's read.</summary>
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

        /// <summary>Asks, then adds a download of textures (screen-only pictures) as a texture pack.</summary>
        void AddTextures(string folder, string name, SkinSource source, string sourcePath)
        {
            int count = TexturePacks.Find(folder).Count;
            if (Dialog.Show(this, $"\"{name}\" is a texture pack ({count} pictures).\r\n\r\nIt only changes what your screen shows, so it's always safe online.\r\n\r\nAdd it?",
                "Texture pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            TexturePack pack = null;
            var error = WithProgress(report => pack = _textures.Add(folder, name, source, sourcePath, report));
            if (error != null) { Dialog.Show(this, Friendly.Explain(error), "Couldn't add the textures", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _library.Refresh();
            Dialog.Show(this, $"Added \"{pack.Name}\". You'll see it next time you play (My Mods > Textures turns it off and on).", "Texture pack", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Turns a texture pack on or off straight away (it doesn't touch the card, so it needs no save).</summary>
        public void SetOn(TexturePack p, bool on)
        {
            if (_busy) return;
            var error = WithProgress(report => _textures.SetOn(p, on, report));
            if (error != null) Dialog.Show(this, Friendly.Explain(error), "Couldn't change the textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _library.Refresh();
        }

        /// <summary>Asks, then deletes a texture pack.</summary>
        public void Delete(TexturePack p)
        {
            if (Dialog.Show(this, $"Delete the texture pack \"{p.Name}\"?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { _textures.Delete(p); } catch (Exception e) { Dialog.Show(this, Friendly.Explain(e), "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            _library.Refresh();
        }

        // ----- mod updates (newer files on GameBanana) -----

        Dictionary<long, long> _updates = new Dictionary<long, long>();   // installed file id -> its newer version
        long _updatingFile;                                                // the old file while an update installs

        public bool HasUpdate(long fileId) => fileId != 0 && _updates.ContainsKey(fileId);

        /// <summary>Looks for newer versions of your mods on GameBanana in the background, then shows them in My Mods.</summary>
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
            if (error != null) { Dialog.Show(this, (error is GbException ? error.Message : "The update didn't download: " + Friendly.Explain(error)), "Couldn't update", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (f == null || path == null)
            {
                _updates.Remove(fileId); _library.Refresh();
                Dialog.Show(this, $"\"{name}\" is already the newest version.", "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var source = new SkinSource { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, PageUrl = d.Mod.ProfileUrl, ThumbUrl = d.Mod.ThumbUrl };
            try
            {
                new DownloadInfo { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, Category = d.Mod.Category,
                    ThumbUrl = d.Mod.ThumbUrl, PageUrl = d.Mod.ProfileUrl, FileName = f.Name, Path = path, DownloadedAt = DateTime.Now }.Save();
            }
            catch { }
            Dialog.Show(this, $"Downloaded the new version of \"{name}\" ({f.Name}, added {f.Added:MMM d}).\r\n\r\nNext it goes through the same safety checks as any mod. It takes the old version's place, and the old one leaves My Mods.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _updatingFile = fileId;
            try { AddSkin(path, source); }
            finally { _updatingFile = 0; }
            if (!_lib.Skins.Any(k => k.GameBananaFileId == fileId) && !_lib.Stages.Any(k => k.GameBananaFileId == fileId)) _updates.Remove(fileId);
            _library.Refresh();
        }

        // ----- SD cards -----

        LastSave _lastSave = new LastSave(Program.DataFolder);
        /// <summary>Where this card's mods lists are kept: the Data folder for Brawl Online's card, a folder of its own for
        /// another Dolphin's (so the two never mix).</summary>
        string CardData => _install == null || _install.OwnCard ? Program.DataFolder : OtherCards.DataFolderFor(Program.DataFolder, _install.CardPath);
        Installer NewInstaller() => new Installer(_install, _lib) { Undo = _lastSave, CardName = _cards?.Active ?? Cards.Normal, FastSave = Program.FastSave };

        /// <summary>"Undo last save" from Settings.</summary>
        public void UndoLastSaveAsked() { if (_install == null || _cards == null) { Dialog.Show(this, "There's no save to undo yet.", "Undo last save"); return; } UndoLastSave(); }

        /// <summary>Asks, then puts the card and mods list back to how they were before the last save (when that can
        /// still be done).</summary>
        void UndoLastSave()
        {
            if (!CardsCanChange()) return;
            var info = _lastSave.Read();
            string why = _lastSave.CantUndo(_cards.Active, _install.CardPath);
            if (why != null) { Dialog.Show(this, why, "Undo last save", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (Dialog.Show(this, $"Put your SD card and mods back the way they were before your last save ({info.SavedAt:ddd h:mm tt}: {info.What})?",
                "Undo last save", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var error = WithProgress(report => _lastSave.Undo(_install, _lib, _cards.Active, report));
            ReloadLibrary();
            UpdateStatus();
            if (error != null) Dialog.Show(this, Problem(error, "Your SD card wasn't changed."), "Couldn't undo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else Dialog.Show(this, "Done: your SD card and mods are back to how they were before your last save.", "Undo last save", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Whether the card can be changed now: Brawl Online must be closed and unsaved switches dealt with.</summary>
        bool CardsCanChange()
        {
            if (_install.IsRunning(out string what))
            {
                Dialog.Show(this, $"Close {what} first. It uses the SD card.", "Brawl Online is open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return SavedFirst();
        }

        /// <summary>Runs slow work (opening a download, reading the SD card) off the window's thread, with a small "working"
        /// window so the app stays responsive and can't be clicked into a half-done state. Its error is thrown here.</summary>
        T Wait<T>(string message, Func<T> work)
        {
            T result = default(T); Exception error = null;
            using (var progress = new ProgressForm(message, marquee: true))
            {
                progress.Shown += async (s, e) =>
                {
                    try { result = await Task.Run(work); }
                    catch (Exception ex) { error = ex; }
                    progress.Close();
                };
                progress.ShowDialog(this);
            }
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        /// <summary>Older versions could save an old build of a stage mod as P+'s file for its path (when the mod was
        /// made smaller to fit), so turning the mod off put that build back. Finds P+'s real file for those, for every
        /// card's mods list, from your other cards or saved originals (in the background; ApplyOriginalFixes saves them).</summary>
        void FindOriginalFixes(PPlusRelease release)
        {
            if (release == null || _cards == null) return;
            var found = new List<(Library lib, Dictionary<string, byte[]> fixes, string card)>();
            var files = new[] { Cards.Normal, Cards.Big, Cards.Clean }.Select(n => (n, path: n == _cards.Active ? _install.CardPath : _cards.Parked(n))).Where(x => File.Exists(x.path)).ToList();
            var cards = new List<Fat32Image>();
            try
            {
                foreach (var f in files) try { cards.Add(new Fat32Image(f.path, false)); } catch { cards.Add(null); }
                var active = _lib;
                var libs = files.Select(f => f.n == _cards.Active ? active : new Library(_cards.LibraryFolder(f.n))).ToList();
                for (int i = 0; i < files.Count; i++)
                {
                    if (cards[i] == null) continue;
                    var fixes = OriginalsRepair.Find(libs[i], cards[i], release, cards.Where(c => c != null), libs);
                    if (fixes.Count > 0) found.Add((libs[i], fixes, files[i].n));
                }
            }
            catch (Exception e) { Program.Log("checking the saved originals", e); }
            finally { foreach (var c in cards) c?.Dispose(); }
            lock (this) _originalFixes = found;
        }
        List<(Library lib, Dictionary<string, byte[]> fixes, string card)> _originalFixes;

        /// <summary>Saves what FindOriginalFixes found, unless the app is changing a card right now (then next time).</summary>
        void ApplyOriginalFixes()
        {
            List<(Library lib, Dictionary<string, byte[]> fixes, string card)> found;
            lock (this) { found = _originalFixes; _originalFixes = null; }
            if (found == null || _busy) return;
            foreach (var (lib, fixes, card) in found)
            {
                try
                {
                    OriginalsRepair.Apply(lib, fixes);
                    Program.Trace($"P+'s own file put back as the original for {fixes.Count} stage(s) of the {card} card: {string.Join(", ", fixes.Keys)}");
                }
                catch (Exception e) { Program.Log("fixing the saved originals", e); }
            }
        }

        /// <summary>The P+ release Brawl Online has (its files, to compare cards with); null when it can't be read.</summary>
        PPlusRelease CurrentRelease()
        {
            try { return Versions.Release(_install, Versions.Read(_install).PPlus); }
            catch (Exception e) { Program.Log("reading the P+ version", e); return null; }
        }

        /// <summary>What went wrong with a card job, in plain words: and whether the card changed (a job that got past
        /// changing the card says so itself).</summary>
        static string Problem(Exception e, string unchanged) => e is SavedButException ? e.Message : Friendly.Explain(e) + "\r\n\r\n" + unchanged;

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
            if (error != null) { Program.Trace("card job failed: " + error); Program.Log("card job", error); }
            return error;
        }

        /// <summary>Explains and makes the big (4 GB) card for Unranked and Ranked, then offers to switch to it.</summary>
        void MakeBigCard()
        {
            if (!CardsCanChange()) return;
            var answer = Dialog.Show(this,
                "This makes a second, bigger SD card (4 GB instead of 2 GB), so you get about 2 GB of room for songs and other mods.\r\n\r\n" +
                "Use it for Unranked and Ranked: there, everyone plays their own card.\r\n" +
                "Use the normal card for rooms: players in a room get the host's card, and a bigger card would make them download most of it.\r\n\r\n" +
                "You switch between them in this menu (Profile ▾ > SD card). The big card starts with the mods you have now; after that, each card has its own mods.\r\n\r\n" +
                "It takes about 4 GB on your PC (and Brawl Online keeps a copy of whichever card is in use). Make it?",
                "Make a big card", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            var error = WithProgress(report => _cards.MakeBig(report));
            if (error != null) { Dialog.Show(this, Problem(error, "Nothing was changed."), "Couldn't make the big card", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (Dialog.Show(this, "The big card is ready. Switch to it now?", "Big card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                SwitchCard(Cards.Big);
        }

        /// <summary>Asks, then deletes the big card and its mods list.</summary>
        void DeleteBigCard()
        {
            if (!CardsCanChange()) return;
            if (Dialog.Show(this, "Delete the big card and the mods list that goes with it? The mods' downloads stay in My Mods' Downloaded tab.", "Delete the big card",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { _cards.DeleteBig(); } catch (Exception e) { Dialog.Show(this, Friendly.Explain(e), "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            UpdateStatus();
        }

        /// <summary>Switches to another of your SD cards (normal, big, clean), first catching it up with any P+ update it missed.
        /// quiet: say nothing unless there's something to say.</summary>
        bool SwitchCard(string to, bool quiet = false)
        {
            _stageSelect = null;
            if (to == _cards.Active) return true;
            if (!CardsCanChange()) return false;
            var notes = new List<string>();
            var release = CurrentRelease();
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
            if (error != null) { Dialog.Show(this, Problem(error, "The card in use wasn't changed."), "Couldn't switch cards", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            ReloadLibrary();
            UpdateStatus();
            if (quiet && notes.Count == 0) return true;
            Dialog.Show(this, "Now using the " + Cards.Label(to).ToLowerInvariant() + "." + (to == Cards.Big
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
            Dialog.Show(this, "The app couldn't find Brawl Online on this PC. Show it where \"Brawl Online.exe\" is (only needed once).", "Find Brawl Online", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                Dialog.Show(this, $"{what} is already open. Close it, then click Launch again.", "Already open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_install.OwnCard)
            {
                // your own Dolphin: save, then open it (it reads its card when it starts)
                if (!SavePending()) return;
                var dolphin = new[] { Path.Combine(_install.OtherDolphin, "Dolphin.exe"), Path.Combine(Path.GetDirectoryName(_install.OtherDolphin) ?? "", "Dolphin.exe") }.FirstOrDefault(File.Exists);
                if (dolphin == null) { Dialog.Show(this, "Your mods are saved on your Dolphin's SD card. Open your Dolphin as usual to play with them.", "Your Dolphin", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dolphin) { WorkingDirectory = Path.GetDirectoryName(dolphin), UseShellExecute = true }); }
                catch (Exception e) { Dialog.Show(this, "Couldn't open your Dolphin: " + Friendly.Explain(e), "Your Dolphin", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
                    if (Dialog.Show(this, "To play without mods, the app makes a clean SD card once: P+ exactly as it comes, kept next to your other card(s).\r\n\r\n" +
                        "It takes about 2 GB on the drive Brawl Online is on and a minute or two. After that, switching between modded and unmodded is instant, and your mods stay where they are.\r\n\r\nMake it now?",
                        "Clean card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    if (!CardsCanChange()) return;
                    var release = CurrentRelease();
                    var libs = new List<Library> { new Library(_cards.LibraryFolder(Cards.Normal)) };
                    if (Directory.Exists(_cards.LibraryFolder(Cards.Big))) libs.Add(new Library(_cards.LibraryFolder(Cards.Big)));
                    var error = WithProgress(report => _cards.MakeClean(release, libs, report));
                    if (error != null) { Dialog.Show(this, Problem(error, "Your SD card wasn't changed."), "Couldn't make the clean card", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                }
                if (!SwitchCard(Cards.Clean, quiet: true)) return;
            }
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
            catch (Exception e) { Dialog.Show(this, "Brawl Online didn't open: " + Friendly.Explain(e), "Launch", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
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
                return Dialog.Show(this, "The app couldn't check your stage mods before launching (" + Friendly.Explain(error) + ").\r\n\r\n" +
                    "If a stage mod doesn't match P+, online games on it go out of sync. Launch anyway?", "Launch modded",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            _unsafe = found.Select(x => x.Stage).ToList();
            _unsafeBanner.Visible = _unsafe.Count > 0;
            if (found.Count == 0) return true;
            string list = string.Join("\r\n", found.Select(x => "• " + x.Stage.Name + "  (" + x.Stage.Target.Label + ")"));
            var answer = Dialog.Show(this,
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
            RescanHand();
        }

        // ----- mods put on by hand -----

        bool _handScanning;
        /// <summary>Looks through the card in place for mods put on by hand (in the background; quick after the first time).</summary>
        async void RescanHand()
        {
            if (_install == null || _cards == null || _handScanning) return;
            _handScanning = true;
            try
            {
                var install = _install; string cardName = _cards.Active;
                var libs = new List<Library> { new Library(_cards.LibraryFolder(Cards.Normal)) };
                if (System.IO.Directory.Exists(_cards.LibraryFolder(Cards.Big))) libs.Add(new Library(_cards.LibraryFolder(Cards.Big)));
                string cache = System.IO.Path.Combine(CardData, "Cache", "card-files-" + cardName + ".json");
                string data = CardData;
                var scan = await Task.Run(() =>
                {
                    using (var card = new Fat32Image(install.CardPath, false)) return HandMods.Scan(card, CardBaseline.For(card, install, data, libs), libs, cache);
                });
                if (_cards.Active != cardName) return;
                HandMods.Last = scan;
                Program.Trace($"put on by hand: {scan.Mods.Count}");
                _library.ShowHand(scan);
            }
            catch (Exception e) { Program.Log("looking for mods put on by hand", e); }
            finally { _handScanning = false; }
        }

        /// <summary>Shows the changes made to the card by hand (outside the app), with a way to look again from scratch.</summary>
        public void ShowHandMods()
        {
            using (var f = new HandModsForm(HandMods.Last, fresh =>
            {
                try
                {
                    var libs = new List<Library> { new Library(_cards.LibraryFolder(Cards.Normal)) };
                    if (System.IO.Directory.Exists(_cards.LibraryFolder(Cards.Big))) libs.Add(new Library(_cards.LibraryFolder(Cards.Big)));
                    using (var card = new Fat32Image(_install.CardPath, false))
                    {
                        var release = fresh ? CardBaseline.Make(card, CardData, libs) : CardBaseline.For(card, _install, CardData, libs);
                        HandMods.Last = HandMods.Scan(card, release, libs, System.IO.Path.Combine(CardData, "Cache", "card-files-" + _cards.Active + ".json"));
                    }
                }
                catch (Exception e) { Program.Log("looking for mods put on by hand", e); }
                return HandMods.Last;   // (runs in the background: the window is updated once it's closed)
            }))
                f.ShowDialog(this);
            if (HandMods.Last != null) _library.ShowHand(HandMods.Last);
        }

        /// <summary>Before something that changes the card: asks to save or drop unsaved switches. False if you cancelled.</summary>
        bool SavedFirst()
        {
            if (_pending.Count == 0) return true;
            var answer = Dialog.Show(this, "You have switches that aren't saved yet. Save them to this profile before switching?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return SavePending();
            _pending.Clear(); PendingChanged();
            return true;
        }

        /// <summary>Turns on exactly the mods of a profile (null: none, P+ as it comes), in one save. False if it didn't.</summary>
        bool SwitchProfile(string name)
        {
            if (name == _lib.ActiveProfile) return true;
            if (!SavedFirst()) return false;
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
            bool ok = true;
            if (change.IsEmpty) { _lib.Save(); UpdateProfileButton(); PendingChanged(); return true; }
            int ons = change.Enable.Count + change.StageEnable.Count, offs = change.Disable.Count + change.StageDisable.Count;
            if (!Run(change, name == null ? "Switched to P+ as it comes: all your mods are off (they're still in My Mods)."
                                          : $"Switched to \"{name}\": {ons} mod(s) turned on, {offs} turned off."))
            { _lib.ActiveProfile = before; ok = false; }
            _lib.Save();
            UpdateProfileButton();
            return ok;
        }

        /// <summary>Whether a profile name is in use (or is "P+ as it comes"), ignoring case.</summary>
        bool NameTaken(string name, Profile except = null) =>
            name.Equals(Library.DefaultName, StringComparison.OrdinalIgnoreCase) || name.StartsWith("P+ as it comes", StringComparison.OrdinalIgnoreCase) ||
            _lib.Profiles.Any(p => p != except && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Makes a new profile, starting with the mods that are on now (it becomes the one in use) or with none
        /// (switched to).</summary>
        void NewProfile()
        {
            if (!SavedFirst()) return;
            using (var f = new InputForm("New profile", "Name the new profile (for example \"Tournament\" or \"Fun\"):", "", "Create", "Start with the mods that are on now"))
            {
                f.Option.Checked = true;
                if (f.ShowDialog(this) != DialogResult.OK || f.Value == "") return;
                if (NameTaken(f.Value)) { Dialog.Show(this, "There's already a profile with that name.", "New profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _lib.Profiles.Add(new Profile { Name = f.Value, On = f.Option.Checked ? _lib.OnNow() : new List<string>() });
                _lib.Save();
                if (f.Option.Checked) { _lib.ActiveProfile = f.Value; _lib.Save(); UpdateProfileButton(); PendingChanged(); }
                else SwitchProfile(f.Value);
            }
        }

        /// <summary>Renames a profile.</summary>
        void RenameProfile(string name)
        {
            var p = _lib.Profiles.FirstOrDefault(x => x.Name == name); if (p == null) return;
            using (var f = new InputForm("Rename profile", "New name:", p.Name, "Rename"))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Value == "" || f.Value == p.Name) return;
                if (NameTaken(f.Value, p)) { Dialog.Show(this, "There's already a profile with that name.", "Rename profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                bool active = _lib.ActiveProfile == p.Name;
                p.Name = f.Value; if (active) _lib.ActiveProfile = f.Value;
                _lib.Save(); UpdateProfileButton();
            }
        }

        /// <summary>Makes a new profile with the same mods as another, placed right after it.</summary>
        void CopyProfile(string name)
        {
            var p = _lib.Profiles.FirstOrDefault(x => x.Name == name); if (p == null) return;
            string suggestion = p.Name + " (copy)";
            for (int i = 2; NameTaken(suggestion); i++) suggestion = $"{p.Name} (copy {i})";
            using (var f = new InputForm("Copy profile", $"A new profile with the same mods as \"{p.Name}\". Its name:", suggestion, "Copy"))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Value == "") return;
                if (NameTaken(f.Value)) { Dialog.Show(this, "There's already a profile with that name.", "Copy profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _lib.Save();   // the one in use remembers what's on first
                _lib.Profiles.Insert(_lib.Profiles.IndexOf(p) + 1, new Profile { Name = f.Value, On = new List<string>(p.On) });
                _lib.Save();
            }
        }

        /// <summary>Deletes a profile. "P+ as it comes" isn't one: it can never be deleted. Deleting the one in use switches
        /// to another first (or to P+ as it comes when it was the last).</summary>
        void DeleteProfile(string name)
        {
            var p = _lib.Profiles.FirstOrDefault(x => x.Name == name); if (p == null) return;
            bool active = _lib.ActiveProfile == p.Name;
            var next = _lib.Profiles.FirstOrDefault(x => x != p);
            string what = !active ? "Your mods stay in My Mods, and nothing on your SD card changes."
                : next != null ? $"It's the one in use, so the app switches to \"{next.Name}\" first (its mods get turned on). Your mods stay in My Mods."
                : "It's the one in use and your last profile, so the app switches to P+ as it comes first: all your mods get turned off. They stay in My Mods, and you can make a new profile any time.";
            if (Dialog.Show(this, $"Delete the profile \"{p.Name}\"?\r\n\r\n{what}", "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (active)
            {
                if (!SavedFirst()) return;
                int at = _lib.Profiles.IndexOf(p);
                _lib.Save();                                   // it remembers what's on, in case the switch can't happen
                _lib.Profiles.Remove(p);
                _lib.ActiveProfile = "\u0001";                // none for a moment, so the switch goes ahead
                if (!SwitchProfile(next?.Name))
                {
                    _lib.Profiles.Insert(at, p); _lib.ActiveProfile = p.Name; _lib.Save(); UpdateProfileButton();
                    return;
                }
            }
            else { _lib.Profiles.Remove(p); _lib.Save(); }
            UpdateProfileButton();
        }

        /// <summary>Opens the profiles window (switch, new, rename, copy, delete, share, open a shared setup).</summary>
        void ManageProfiles()
        {
            if (_lib == null || _busy) return;
            using (var f = new ProfilesForm(_lib, n => SwitchProfile(n), NewProfile, RenameProfile, CopyProfile, DeleteProfile) { Share = ShareProfile, OpenShared = OpenSharedSetup })
                f.ShowDialog(this);
        }

        // ----- shared setups -----

        void ShareProfile(string name)
        {
            var p = _lib.Profiles.FirstOrDefault(x => x.Name == name);
            if (p == null) return;
            var setup = SharedSetup.From(_lib, p);
            if (setup.Mods.Count == 0) { Dialog.Show(this, $"\"{name}\" has no mods on, so there's nothing to share.", "Share", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            using (var dlg = new SaveFileDialog { Title = "Save the shared setup", FileName = safe + SharedSetup.Extension, Filter = "Shared setup|*" + SharedSetup.Extension, InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { setup.Save(dlg.FileName); }
                catch (Exception e) { Dialog.Show(this, "It couldn't be saved: " + Friendly.Explain(e), "Share", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                int local = setup.Mods.Count(m => !m.Fetchable);
                Dialog.Show(this, $"Saved \"{Path.GetFileName(dlg.FileName)}\" ({setup.Mods.Count} mods).\r\n\r\nSend it to a friend. In their P+ Modding Tool: Profile → Manage profiles → \"Open a shared one...\", and they can get the same mods with a click." +
                    (local > 0 ? $"\r\n\r\n{local} of them came from a file on this PC rather than GameBanana, so you'll need to send {(local == 1 ? "that one" : "those")} yourself." : ""),
                    "Shared", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Opens a .ppsetup file a friend sent and shows its mods, to fetch the ones you want.</summary>
        void OpenSharedSetup()
        {
            string path;
            using (var dlg = new OpenFileDialog { Title = "Open a shared setup", Filter = "Shared setup|*" + SharedSetup.Extension + "|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            SharedSetup setup;
            try { setup = SharedSetup.Open(path); }
            catch (Exception e) { Dialog.Show(this, e is InvalidDataException ? e.Message : "It couldn't be opened: " + Friendly.Explain(e), "Shared setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using (var f = new SharedSetupForm(setup, _lib, GetShared)) f.ShowDialog(this);
        }

        /// <summary>Downloads one mod of a shared setup and adds it the usual way. True when it's now in My Mods.</summary>
        bool GetShared(SharedMod m)
        {
            if (_busy || !m.Fetchable) return false;
            GbDetails d = null; GbFile f = null; string path = null;
            var error = WithProgress(report =>
            {
                report("Looking up " + m.Name, 0);
                d = GameBanana.Details(m.ModId, ModUpdates.ModelOf(m.PageUrl), contents: false);
                f = d.Files.FirstOrDefault(x => x.Id == m.FileId);
                if (f == null) return;
                path = GameBanana.Download(f, GameBanana.DownloadFolder(Program.DataFolder, m.ModId),
                    (done, total) => report($"Downloading {f.Name}", total > 0 ? (int)Math.Min(100, done * 100 / total) : 0), System.Threading.CancellationToken.None);
            });
            if (error != null) { Dialog.Show(this, (error is GbException ? error.Message : $"\"{m.Name}\" didn't download: " + Friendly.Explain(error)), "Couldn't get it", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            if (f == null || path == null)
            {
                Dialog.Show(this, $"That version of \"{m.Name}\" isn't on GameBanana anymore (it may have been updated or taken down). Look for it in the Mod Store instead.", "Couldn't get it", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            var source = new SkinSource { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, PageUrl = d.Mod.ProfileUrl, ThumbUrl = d.Mod.ThumbUrl };
            try
            {
                new DownloadInfo { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, Category = d.Mod.Category,
                    ThumbUrl = d.Mod.ThumbUrl, PageUrl = d.Mod.ProfileUrl, FileName = f.Name, Path = path, DownloadedAt = DateTime.Now }.Save();
            }
            catch { }
            AddSkin(path, source);
            return SharedSetup.Have(_lib, m);
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

        /// <summary>Marks stage mods to turn on or off at the next save; turning one on turns off any other on the same
        /// stage file.</summary>
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

        /// <summary>Asks, then deletes a stage mod (all of its versions together); if it's on, P+'s stage goes back on the card.</summary>
        public void Delete(InstalledStage s)
        {
            // a stage on all of a normal version's files (Smashville's five times of day) is one mod: they go together
            var all = s.Group == null ? new List<InstalledStage> { s } : _lib.Stages.Where(x => x.Group == s.Group).ToList();
            string what = s.IsSwap ? $"the normal {s.TargetLabel}" : all.Count > 1 ? $"P+'s normal {s.Target.StageName}" : $"P+'s own {s.Target.Label}";
            bool on = all.Any(x => x.Enabled);
            string text = $"Delete \"{s.Name}\" from My Mods?\r\n\r\n" + (on ? $"It's on right now, so {what} goes back. " : "It's off, so nothing on your SD card changes. ") +
                          (s.IsSwap ? "" : AgainLine(SourceOf(s)));
            if (Dialog.Show(this, text, s.IsSwap ? "Delete stage swap" : "Delete stage", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var x in all) _pending.Remove(x.Id);
            if (!on)
            {
                foreach (var x in all) _lib.Stages.Remove(x);
                if (!SaveList()) { _lib.Stages.AddRange(all); return; }
                foreach (var x in all) try { Directory.Delete(_lib.StageFolder(x.Id), true); } catch { }
                PendingChanged();
                return;
            }
            var change = new Installer.Change(); change.StageRemove.AddRange(all);
            Run(change, $"Deleted. {char.ToUpperInvariant(what[0]) + what.Substring(1)} is P+'s again.");
        }

        public string SourceOf(InstalledStage s) => SourceOf(new InstalledSkin { SourcePath = s.SourcePath, GameBananaModId = s.GameBananaModId, GameBananaFileId = s.GameBananaFileId });
        public void AddMore(InstalledStage s) => AddMore(new InstalledSkin { Name = s.Name, SourcePath = s.SourcePath, GameBananaModId = s.GameBananaModId, GameBananaFileId = s.GameBananaFileId, Author = s.Author, PageUrl = s.PageUrl, ThumbUrl = s.ThumbUrl });
        public void GoToStore() => ShowPage(_store);

        /// <summary>Marks skins to turn on or off at the next save; turning one on turns off whatever else is on in that
        /// costume slot.</summary>
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

        /// <summary>Notes a skin's wanted state (forgotten when it's already that way on the card).</summary>
        void Want(InstalledSkin s, bool on)
        {
            if (on == s.Enabled) _pending.Remove(s.Id); else _pending[s.Id] = on;
        }

        /// <summary>Shows or hides the save bar with how many switches wait, and redraws what shows them.</summary>
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

        /// <summary>Writes the waiting switches to the card in one go. False if the save didn't happen.</summary>
        bool SavePending()
        {
            if (_pending.Count == 0) return true;
            var change = new Installer.Change();
            AddPending(change);
            int on = change.Enable.Count + change.StageEnable.Count, off = change.Disable.Count + change.StageDisable.Count;
            return Run(change, on > 0 && off > 0 ? $"Saved: {on} on, {off} off." : on > 0 ? (on == 1 ? "Saved: 1 mod turned on." : $"Saved: {on} mods turned on.") : (off == 1 ? "Saved: 1 mod turned off." : $"Saved: {off} mods turned off."));
        }

        /// <summary>Adds the waiting switches to a change for the card.</summary>
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

        /// <summary>Asks, then deletes a skin; if it's on, P+'s own costume goes back in its slot.</summary>
        public void Delete(InstalledSkin s)
        {
            string text = $"Delete \"{s.Name}\" from My Mods?\r\n\r\n" + (s.Enabled ? $"It's on right now, so P+'s own costume goes back in {s.Character.Name}'s costume {s.SlotIndex + 1}. " : "It's off, so nothing on your SD card changes. ") +
                          AgainLine(SourceOf(s));
            if (Dialog.Show(this, text, "Delete skin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _pending.Remove(s.Id);
            if (!s.Enabled)
            {
                // nothing on the card: just forget it
                _lib.Skins.Remove(s);
                if (!SaveList()) { _lib.Skins.Add(s); return; }
                try { Directory.Delete(_lib.SkinFolder(s.Id), true); } catch { }
                PendingChanged();
                return;
            }
            Run(new Installer.Change { Remove = { s } }, $"Deleted. {s.Character.Name}'s costume {s.SlotIndex + 1} is P+'s again.");
        }

        public void InstallDownload(DownloadInfo d) => AddSkin(d.Path, d.Source);

        /// <summary>Notes how many costumes (or stages, songs) a download has, so My Mods' Downloaded tab can say how
        /// many are added.</summary>
        void RememberCostumeCount(string path, int count)
        {
            try
            {
                var d = DownloadInfo.All(Program.DataFolder).FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (d != null && d.CostumeCount != count) { d.CostumeCount = count; d.Save(); }
            }
            catch { }
        }

        /// <summary>Whether a deleted mod can come back: from its download, or not at all.</summary>
        static string AgainLine(string source) => source != null
            ? "Its download stays, so you can add it again later (My Mods > Downloaded, or the file you added it from)."
            : "Its download isn't on this PC any more, so this can't be undone.";

        /// <summary>Writes the list of mods; says so if it can't (nothing is lost: the list in memory is put back by the caller).</summary>
        bool SaveList()
        {
            try { _lib.Save(); return true; }
            catch (Exception e) { Program.Log("save list", e); Dialog.Show(this, Friendly.Explain(e) + "\r\n\r\nNothing was deleted.", "Couldn't delete it", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
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

        /// <summary>Opens a skin's download again to add more of its costumes (says so if the download is gone).</summary>
        public void AddMore(InstalledSkin s)
        {
            string path = SourceOf(s);
            if (path == null)
            {
                Dialog.Show(this, $"The download \"{s.Name}\" came from isn't on this PC anymore." + (s.GameBananaModId != 0 ? " Find it again in the Mod Store and click Install: costumes you already added are marked." : " Use \"Add from file\" to open it again."),
                    "Download not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var src = s.GameBananaModId != 0 ? new SkinSource { ModId = s.GameBananaModId, FileId = s.GameBananaFileId, ModName = BaseName(s.Name), Author = s.Author, PageUrl = s.PageUrl, ThumbUrl = s.ThumbUrl } : null;
            AddSkin(path, src);
        }

        static string BaseName(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s+(\(.*\)|#\w+)$", "");

        /// <summary>P+'s select-screen picture for the slot a skin is in (for skins without their own): read off the window's
        /// thread, so My Mods never waits on the SD card.</summary>
        public async Task<Image> SlotPictureAsync(InstalledSkin s)
        {
            var c = s.Character;
            // P+'s own picture for the slot (saved when a skin first replaced it), else what the card shows now
            var original = _lib.OriginalPicture(c.CspFilePath, c.CspTextureName(s.SlotIndex));
            if (original != null) return original;
            var look = await ReadCharacter(c);
            return look.Pictures.TryGetValue(c.CspTextureName(s.SlotIndex), out var p) ? p : null;
        }

        /// <summary>A character's costume slots and select-screen pictures as they are on the SD card. Read in the background
        /// (one read per character at a time); the pictures are kept until a save changes them. Only touched on the window's thread.</summary>
        Task<(List<CostumeSlot> Slots, Dictionary<string, Bitmap> Pictures)> ReadCharacter(Character c)
        {
            if (_reading.TryGetValue(c.SlotListId, out var running)) return running;
            _pictureCache.TryGetValue(c.SlotListId, out var known);
            var installer = _installer;
            var task = Task.Run(() =>
            {
                using (var card = installer.OpenCard())
                {
                    var slots = PPlusCard.ReadSlots(card, c);
                    return (slots, known ?? Cosmetics.ReadPictures(card.ReadFile(c.CspFilePath), slots.Select(x => c.CspTextureName(x.Index))));
                }
            });
            _reading[c.SlotListId] = task;
            task.ContinueWith(t =>
            {
                _reading.Remove(c.SlotListId);
                if (t.Status == TaskStatus.RanToCompletion) _pictureCache[c.SlotListId] = t.Result.Item2;
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return task;
        }
        readonly Dictionary<int, Task<(List<CostumeSlot> Slots, Dictionary<string, Bitmap> Pictures)>> _reading = new Dictionary<int, Task<(List<CostumeSlot> Slots, Dictionary<string, Bitmap> Pictures)>>();
        int _showing;   // which ShowCharacter is the latest (an older one that finishes later is ignored)

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

        /// <summary>Fills the Costume Slots page for the picked character: each slot's CSP and what's in it. Clicking a slot
        /// offers to turn your skin off there, or takes you to your skins or the Mod Store for that character.</summary>
        async void ShowCharacter(bool refreshPictures = false)
        {
            var c = Current;
            if (c == null || _installer == null || !_slotsPage.Visible) return;
            if (refreshPictures) _pictureCache.Remove(c.SlotListId);
            Program.Trace("show " + c.Name);
            int me = ++_showing;
            _charTitle.Text = c.Name;
            _charHint.Text = "Reading the costumes on your SD card...";
            _slots.SuspendLayout();
            foreach (Control old in _slots.Controls) old.Dispose();
            _slots.Controls.Clear();
            _slots.ResumeLayout();
            List<CostumeSlot> slots;
            Dictionary<string, Bitmap> pics;
            try { (slots, pics) = await ReadCharacter(c); }
            catch (Exception e)
            {
                Program.Trace("read failed: " + e); Program.Log("costume slots", e);
                if (me == _showing) _charHint.Text = "Couldn't read " + c.Name + " from your SD card. " + Friendly.Explain(e);
                return;
            }
            if (me != _showing || IsDisposed || Current != c) return;   // another character was picked meanwhile
            _slots.SuspendLayout();
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
                    Badge = skin != null ? "Your skin: " + skin.Name : HandMods.Last?.Costume(c, s.Index) != null ? "Your own costume\r\n(put on by hand)" + (others > 0 ? $" • {others} off" : "")
                        : PPlusCard.TeamColorName(s.TeamColor) + (s.OnCard ? "" : " • from Brawl") + (others > 0 ? $"\r\n{others} of yours (off)" : ""),
                    BadgeColor = skin != null ? Theme.Accent : HandMods.Last?.Costume(c, s.Index) != null ? Theme.Warn : Theme.Muted,
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

        /// <summary>Asks, then turns a skin off so P+'s costume is back in that slot.</summary>
        void OfferTurnOff(InstalledSkin skin)
        {
            var answer = Dialog.Show(this, $"Turn off \"{skin.Name}\"?\r\n\r\n{skin.Character.Name}'s costume {skin.SlotIndex + 1} goes back to P+'s. The skin stays in My Mods for later.",
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

        /// <summary>"Choose stage versions": which version of a stage you get by just picking it.</summary>
        public void StageVersions()
        {
            if (_busy || _installer == null || _lib == null) return;
            bool add = false;
            using (var f = new StageVersionsForm(_installer, _lib))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Change != null && !f.Change.IsEmpty) Run(f.Change, f.Done);
                add = f.AddStageAsked;
            }
            if (add) PickSkin();
        }

        /// <summary>"Other versions…" on a stage mod in My Mods: the stage versions window, opened at its stage with the
        /// mod as the look, to tick the versions it goes on.</summary>
        public void StageVersionsFor(InstalledStage stage)
        {
            if (_busy || _installer == null || _lib == null) return;
            using (var f = new StageVersionsForm(_installer, _lib, stage))
                if (f.ShowDialog(this) == DialogResult.OK && f.Change != null && !f.Change.IsEmpty) Run(f.Change, f.Done);
        }

        /// <summary>Does a folder have mod files in it (costumes, stages, game songs, archives)?</summary>
        static bool LooksLikeMod(string folder)
        {
            try { return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(x => { var e = Path.GetExtension(x).ToLowerInvariant(); return e == ".pac" || e == ".pcs" || e == ".brstm" || e == ".zip" || e == ".rar" || e == ".7z"; }); }
            catch { return false; }
        }

        /// <summary>Your own MP3/WAV songs: made into game songs, then placed like any music.</summary>
        public void AddOwnMusic(List<string> files = null)
        {
            if (_busy) return;
            if (_installer == null) { AddSkin(null); return; }
            // no songs yet: the window opens empty, with its buttons to add songs or a whole folder (or drop them on it)
            using (var f = new MusicImportForm(files ?? new List<string>()))
                if (f.ShowDialog(this) != DialogResult.OK || f.ResultFolder == null) return;
                else AddSkin(f.ResultFolder);
        }

        /// <summary>Adds a mod from a download, file or folder: opens it and, by what's inside, goes on to the songs, the stage
        /// versions, a texture pack, one costume's step-by-step setup, or the list of a pack's costumes. Waits if the
        /// card is busy.</summary>
        void AddSkin(string path, SkinSource source = null, List<string> files = null)
        {
            // a download that finishes while the SD card is being changed: its turn comes right after
            if (_busy) { _afterCardWork.Add(() => AddSkin(path, source, files)); return; }
            if (path != null && MusicImport.IsAudio(path) && _installer != null) { AddOwnMusic(new List<string> { path }); return; }
            if (_installer == null)
            {
                Dialog.Show(this, "Brawl Online isn't set up on this PC yet, so there's no P+ SD card to put skins on.\r\n\r\nOpen Brawl Online once and let it finish setting up Project+, then open this app again.", "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                string name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
                (folder, pkg) = Wait("Opening " + (source?.ModName ?? name), () =>
                {
                    var f = files != null ? Archives.OpenAll(files) : Archives.Open(path);
                    return (f, SkinPackage.Scan(f, source?.ModName ?? name));
                });
                pkg.Source = source;
                pkg.SourcePath = originalPath;
            }
            catch (Exception e)
            {
                Program.Trace("open failed: " + e); Program.Log("opening a mod", e);
                Dialog.Show(this, Friendly.Explain(e), "Couldn't open that mod", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Program.Trace($"package: {pkg.Costumes.Count} costumes, {pkg.StageFiles.Count} stages, {pkg.Pictures.Count} pictures");
            if (pkg.Costumes.Count == 0 && pkg.StageFiles.Count == 0 && pkg.SongFiles.Count > 0)
            {
                RememberCostumeCount(originalPath, pkg.SongFiles.Count);
                using (var form = new SongForm(_installer, pkg))
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
                // the stage manager, opened on the stage the download is for: tick the versions it goes on
                using (var form = new StageVersionsForm(_installer, _lib, pkg))
                {
                    if (form.ShowDialog(this) == DialogResult.OK) stagePlans = form.Plans;
                    else if (pkg.SongFiles.Count == 0) return;
                }
                // music that came with the stage goes on that stage
                var songPlans = new List<StagePlan>();
                if (pkg.SongFiles.Count > 0)
                    using (var songs = new SongForm(_installer, pkg, stagePlans.Count > 0 ? stagePlans.Select(p => p.Target.CardPath).ToList() : null))
                        if (songs.ShowDialog(this) == DialogResult.OK) songPlans = songs.Plans;
                if (stagePlans.Count == 0 && songPlans.Count == 0) return;
                var change = new Installer.Change(); change.StageInstall.AddRange(stagePlans); change.StageInstall.AddRange(songPlans);
                Run(change, "Installed! " + string.Join("\r\n", stagePlans.GroupBy(p => p.Group ?? p.Target.CardPath).Select(g => g.Count() > 1
                        ? $"P+'s normal {g.First().Target.StageName} (all {g.Count()} of its versions) now looks like \"{g.First().Name}\"."
                        : $"{g.First().Target.Label} now looks like \"{g.First().Name}\".")) +
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
                    Dialog.Show(this, $"\"{source.ModName}\" doesn't have any costume, stage or music files in it.\r\n\r\nIt may be a model or project files rather than a ready-to-use mod. Its GameBanana page may say how to use it.",
                        "No costume found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    Dialog.Show(this, "That doesn't have any costume files in it (they're named like FitMario03.pac).\r\n\r\nIf you picked a folder, pick the skin's own folder.",
                        "No costume found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RememberCostumeCount(originalPath, pkg.Costumes.Count);
            if (pkg.Costumes.Count <= 1) { SetUpOne(pkg, null); return; }
            // a pack: the list of its costumes. "Set up this one step by step" does that one, then comes back to the list
            while (true)
            {
                PackageCostume start;
                PackForm made;
                try { made = new PackForm(_installer, _lib, pkg, Replacing()); }
                catch (Exception e) { Program.Log("costume pack", e); Dialog.Show(this, "Couldn't show the costumes in this download. " + Friendly.Explain(e), "Couldn't open it", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                using (var pack = made)
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
                if (Dialog.Show(this, "The clean card (no mods) is in place right now, so mods can't go on it.\r\n\r\nSwitch back to your " + Cards.Label(_cards.LastModded).ToLowerInvariant() + "? Then do this again.",
                    "Clean card in place", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) SwitchCard(_cards.LastModded, quiet: true);
                return false;
            }
            if (_install.IsRunning(out string what))
            {
                Dialog.Show(this, $"Close {what} first: mods can't change while it's using the SD card.", "Brawl Online is open", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // mods put on by hand where this change puts the app's own: ask first (they're kept and come back)
            var hand = HandMods.Last;
            if (hand != null && hand.Mods.Count > 0)
            {
                var targets = change.Install.SelectMany(p => p.Files.Select(f => f.Key))
                    .Concat(change.Enable.SelectMany(k => k.Files.Select(f => f.CardPath)))
                    .Concat(change.StageInstall.Select(p => p.Target.CardPath))
                    .Concat(change.StageEnable.Select(k => k.CardPath));
                var clashes = targets.Select(t => hand.At(t)).Where(m => m != null).GroupBy(m => m.Label).Select(g => g.First()).ToList();
                if (clashes.Count > 0)
                {
                    string list = string.Join("\r\n", clashes.Take(8).Select(m => "• " + m.Label)) + (clashes.Count > 8 ? $"\r\n• and {clashes.Count - 8} more" : "");
                    if (Dialog.Show(this, $"{(clashes.Count == 1 ? "This spot has a mod" : "These spots have mods")} you put on by hand (not through this app):\r\n\r\n{list}\r\n\r\n" +
                        "Going ahead replaces them on the card. They're kept: turning the app's mod off puts yours back.\r\n\r\nGo ahead?",
                        "Your own mods are there", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                }
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
            if (error == null || error is SavedButException) _pending.Clear();   // the card has these changes now
            _pictureCache.Clear();
            PendingChanged();
            ShowCharacter(refreshPictures: true);
            UpdateStatus();
            if (error != null)
            {
                Program.Trace("apply failed: " + error);
                Program.Log("save", error);
                if (error is SavedButException) Dialog.Show(this, error.Message, "Saved, with a problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else Dialog.Show(this, Friendly.Explain(error) + "\r\n\r\nNothing was changed on your SD card.", "Couldn't save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (doneMessage != null)
            {
                var notes = _installer.Notes.ToList();
                if (notes.Count == 0 && _installer.UndoNote == null)
                {
                    // nothing to warn about: a note at the bottom instead of a box to click away
                    Toast.Show(this, doneMessage, "Undo", UndoLastSave);
                }
                else
                {
                string undoLine = _installer.UndoNote ?? "Changed your mind? Profile ▾ > Undo last save puts it back.";
                int stayedOff = notes.Count(n => n.Contains("stayed off"));   // asked to go on, but couldn't: the count above includes them
                if (stayedOff > 0) doneMessage = stayedOff == 1 ? "Saved, but one mod you turned on couldn't go on, so it stayed off (why is below)." : $"Saved, but {stayedOff} mods you turned on couldn't go on, so they stayed off (why is below).";
                Dialog.Show(this, doneMessage + "\r\n\r\n" + undoLine + (notes.Count > 0 ? "\r\n\r\nP+ has changed since some of your mods were made:\r\n• " + string.Join("\r\n• ", notes) : ""),
                    "Done", MessageBoxButtons.OK, notes.Any(n => n.Contains("turned off") || n.Contains("stayed off")) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
            _stageSelect = null;   // a stage swap changes where versions are on the stage select
            CheckOverwritten();
            _store.RefreshInstalled();
            UpdateProfileButton();
            RescanHand();
            return error == null;
        }
    }

    /// <summary>The small "Working" window shown while slow work runs.</summary>
    class ProgressForm : Form
    {
        readonly Label _text = Theme.Label("Starting...", Theme.Body);
        readonly ProgressBar _bar = new ProgressBar { Width = 380, Height = 14, Maximum = 100 };
        /// <summary>message: what it's doing; marquee: no percentage, just "working" (for work that can't say how far along it is).</summary>
        public ProgressForm(string message = null, bool marquee = false)
        {
            if (message != null) _text.Text = message + "...";
            if (marquee) { _bar.Style = ProgressBarStyle.Marquee; _bar.MarqueeAnimationSpeed = 30; }
            _text.AutoSize = false; _text.Size = new Size(380, 30); _text.AutoEllipsis = true;
            Text = "Working"; FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Back; ForeColor = Theme.Text;
            ClientSize = new Size(420, 100); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            _text.Location = new Point(20, 20); _bar.Location = new Point(20, 56);
            Controls.Add(_text); Controls.Add(_bar);
        }
        /// <summary>Updates the message and bar; safe to call from any thread.</summary>
        public void Report(string message, int percent)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { _text.Text = message + "..."; _bar.Value = Math.Max(0, Math.Min(100, percent)); })); }
            catch (InvalidOperationException) { }
        }
    }
}
