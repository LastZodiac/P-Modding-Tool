// "My Mods": every skin you've added, with an on/off switch, filters and the GameBanana files
// you've downloaded but not installed yet.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    interface IModHost
    {
        Library Lib { get; }
        string DataFolder { get; }
        bool IsOn(InstalledSkin s);              // includes changes not saved yet
        bool IsPending(InstalledSkin s);
        void SetOn(IEnumerable<InstalledSkin> skins, bool on);
        void Delete(InstalledSkin s);
        void InstallDownload(DownloadInfo d);
        void GoToStore();
        Image SlotPicture(InstalledSkin s);      // P+'s select-screen picture for that slot (for skins without their own)
        string SourceOf(InstalledSkin s);        // the download it came from, if still on this PC
        void AddMore(InstalledSkin s);           // add other costumes from the same download
        bool IsOn(InstalledStage s);
        bool IsPending(InstalledStage s);
        void SetOn(IEnumerable<InstalledStage> stages, bool on);
        void Delete(InstalledStage s);
        string SourceOf(InstalledStage s);
        void AddMore(InstalledStage s);
        SpaceReport Space { get; }               // how the SD card's space is used (null until read)
        Favorites Favs { get; }                  // starred mods (shared with the Mod Store)
        bool HasUpdate(long gameBananaFileId);   // a newer version is on GameBanana
        void UpdateMod(long modId, long fileId, string pageUrl, string name);
        TexturePacks Textures { get; }           // Dolphin texture packs (null when Brawl Online isn't set up)
        void SetOn(TexturePack p, bool on);
        void Delete(TexturePack p);
        bool OnCleanCard { get; }                // the clean card (no mods) is in place
        void BackToModded();
        StageSpot WhereInGame(InstalledStage s);
        void AddOwnMusic(List<string> files = null);   // your own MP3/WAV songs  // where it is on the stage select (null until read, or not there)
    }

    class LibraryPanel : GlassPanel
    {
        enum Tab { All, On, Off, Favorites, Downloaded, Conflicts }

        readonly IModHost _host;
        readonly Label _title = Theme.Label("My Mods", Theme.Title);
        readonly Label _count = Theme.Label("", Theme.Body, Theme.Muted);
        readonly TextBox _search = new TextBox { Width = 220, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle };
        readonly ComboBox _who = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Font = Theme.Body, FlatStyle = FlatStyle.Flat };
        readonly Button _allOff = Theme.Secondary("Turn all off");
        readonly Button _ownMusic = Theme.Primary("+ Add your own music");
        readonly Button _allOn = Theme.Secondary("Turn all on");
        readonly FlowLayoutPanel _tabs = new GlassFlow { AutoSize = false, WrapContents = false, Size = new Size(900, 32) };
        readonly GlassFlow _grid = new GlassFlow { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 6, 0, 0) };
        readonly Panel _empty = new Panel { Dock = DockStyle.Fill, Visible = false };
        readonly Label _emptyText = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Button _emptyButton = Theme.Primary("Open the Mod Store");
        readonly Dictionary<string, Image> _pictures = new Dictionary<string, Image>();
        Tab _tab = Tab.All;
        bool _stageMode, _musicMode;   // music cards are file mods too, like stages
        bool _textureMode;             // Dolphin texture packs
        readonly Button _kindTextures = Theme.Secondary("Textures");
        readonly Button _kindSkins = Theme.Secondary("Skins");
        readonly Button _kindStages = Theme.Secondary("Stages");
        readonly Button _kindMusic = Theme.Secondary("Music");
        readonly SpaceMeter _meter = new SpaceMeter();
        readonly ComboBox _sortBy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Font = Theme.Body, FlatStyle = FlatStyle.Flat };
        static readonly string[] Sorts = { "Favorites first", "Recently added", "Name (A to Z)", "Biggest on the SD card" };
        List<DownloadInfo> _downloads = new List<DownloadInfo>();

        readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

        public LibraryPanel(IModHost host)
        {
            _host = host;
            Dock = DockStyle.Fill; Padding = new Padding(24, 14, 16, 8);
            _who.Items.Add("All characters");
            foreach (var c in Characters.All.OrderBy(c => c.Name)) _who.Items.Add(c);
            _who.SelectedIndex = 0;
            _who.BackColor = Theme.Panel; _who.ForeColor = Theme.Text;
            _search.BackColor = Theme.Panel; _search.ForeColor = Theme.Text;
            _search.TextChanged += (s, e) => Refresh();
            Theme.Cue(_search, "Search by name, author or character");
            foreach (var so in Sorts) _sortBy.Items.Add("Sort: " + so);
            _sortBy.SelectedIndex = 0; _sortBy.BackColor = Theme.Panel; _sortBy.ForeColor = Theme.Text;
            _sortBy.SelectedIndexChanged += (s, e) => Refresh();
            _who.SelectedIndexChanged += (s, e) => Refresh();
            _allOff.Click += (s, e) => { if (_textureMode) { foreach (var p in VisibleTextures().Where(x => x.Enabled).ToList()) _host.SetOn(p, false); return; } if (_stageMode) _host.SetOn(VisibleStages(), false); else _host.SetOn(Visible_Skins(), false); };
            _allOn.Click += (s, e) => TurnAllOn();
            _kindSkins.Click += (s, e) => { _stageMode = false; _musicMode = false; _textureMode = false; Refresh(); };
            _kindStages.Click += (s, e) => { _stageMode = true; _musicMode = false; _textureMode = false; Refresh(); };
            _kindMusic.Click += (s, e) => { _stageMode = true; _musicMode = true; _textureMode = false; Refresh(); };
            _kindTextures.Click += (s, e) => { _stageMode = false; _musicMode = false; _textureMode = true; if (_tab != Tab.On && _tab != Tab.Off) _tab = Tab.All; Refresh(); };
            _ownMusic.Click += (s, e) => _host.AddOwnMusic();
            _emptyButton.Click += (s, e) => { if (_host.OnCleanCard && !_textureMode) _host.BackToModded(); else _host.GoToStore(); };

            var top = new Panel { Dock = DockStyle.Top, Height = 160 };
            _title.Location = new Point(0, 0);
            _count.Location = new Point(110, 10);
            _kindSkins.Location = new Point(0, 47); _kindStages.Location = new Point(70, 47);
            _kindSkins.Layout += (s, e) => { _kindStages.Left = _kindSkins.Right + 4; _kindMusic.Left = _kindStages.Right + 4; _kindTextures.Left = _kindMusic.Right + 4; _search.Left = _kindTextures.Right + 16; _who.Left = _search.Right + 12; };
            _kindMusic.Location = new Point(150, 47); _kindTextures.Location = new Point(230, 47);
            _search.Location = new Point(160, 50);
            _who.Location = new Point(432, 49);
            _allOff.Anchor = _allOn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Resize += (s, e) =>
            {
                _allOn.Location = new Point(top.Width - _allOn.Width - 4, 4);
                _allOff.Location = new Point(_allOn.Left - _allOff.Width - 8, 4);
                _ownMusic.Location = new Point(_allOff.Left - _ownMusic.Width - 12, 4);

            };
            _tabs.Location = new Point(0, 86);
            top.Controls.AddRange(new Control[] { _title, _count, _kindSkins, _kindStages, _kindMusic, _kindTextures, _search, _who, _sortBy, _allOff, _allOn, _ownMusic, _tabs, _meter });
            _ownMusic.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _sortBy.Location = new Point(640, 49);
            _meter.Location = new Point(0, 122); _meter.BringToFront();
            _title.SizeChanged += (s, e) => _count.Left = _title.Right + 10;

            _emptyText.Location = new Point(4, 24);
            _emptyButton.Location = new Point(4, 64);
            _empty.Controls.Add(_emptyText); _empty.Controls.Add(_emptyButton);
            Controls.Add(_grid); Controls.Add(_empty); Controls.Add(top);
            VisibleChanged += (s, e) => { if (Visible) Refresh(); };
            // the window's background is painted after the first layout; draw the meter again once it has settled
            var settle = new Timer { Interval = 1200 };
            settle.Tick += (s, e) => { settle.Stop(); settle.Dispose(); _meter.Invalidate(); };
            HandleCreated += (s, e) => settle.Start();
        }

        /// <summary>New numbers for the space meter and the cost on each card.</summary>
        public void ShowSpace(SpaceReport r) { _meter.Report = r; _meter.Invalidate(); if (Visible) Refresh(); }

        string CostText(string id)
        {
            var r = _host.Space;
            if (r == null || !r.Cost.TryGetValue(id, out long c)) return "";
            if (Math.Abs(c) < 64 * 1024) return "  •  ±0 MB";
            return c > 0 ? "  •  +" + Space.MB(c) : "  •  −" + Space.MB(-c);
        }

        /// <summary>Show one character's skins (from the Costume Slots page).</summary>
        public void ShowCharacter(Character c) { _who.SelectedItem = c; _tab = Tab.All; Refresh(); }

        Character Who => _who.SelectedItem as Character;

        bool Has(string text, string q) => (text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        bool Matches(InstalledSkin s)
        {
            if (Who != null && Who.SlotListId != s.SlotListId) return false;
            string q = _search.Text.Trim();
            return q == "" || Has(s.Name, q) || Has(s.Author, q) || Has(s.Character.Name, q);
        }

        bool Matches(DownloadInfo d)
        {
            string cat = d.Category ?? "";
            bool costume = cat.IndexOf("Costume", StringComparison.OrdinalIgnoreCase) >= 0;
            bool music = cat.IndexOf("Music", StringComparison.OrdinalIgnoreCase) >= 0 || cat.IndexOf("Victory", StringComparison.OrdinalIgnoreCase) >= 0;
            if (_musicMode ? !music : _stageMode ? (costume || music) : !costume) return false;   // each list shows its own kind of download
            if (!_stageMode && Who != null && !(d.Category ?? "").StartsWith(Who.Name + " ", StringComparison.OrdinalIgnoreCase)) return false;
            string q = _search.Text.Trim();
            return q == "" || Has(d.ModName, q) || Has(d.Author, q) || Has(d.Category, q);
        }

        List<InstalledSkin> Skins => _host.Lib?.Skins ?? new List<InstalledSkin>();

        int Added(DownloadInfo d) => Skins.Count(s => s.GameBananaFileId != 0 && s.GameBananaFileId == d.FileId)
            + (_host.Lib?.Stages.Count(s => s.GameBananaFileId != 0 && s.GameBananaFileId == d.FileId) ?? 0);

        List<InstalledStage> AllStages => (_host.Lib?.Stages ?? new List<InstalledStage>()).Where(x => x.IsSong == _musicMode).ToList();

        bool Matches(InstalledStage s)
        {
            string q = _search.Text.Trim();
            return q == "" || Has(s.Name, q) || Has(s.Author, q) || Has(s.Target.Label, q);
        }

        bool InConflict(InstalledStage s) => AllStages.Count(x => string.Equals(x.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase)) > 1;

        List<InstalledStage> VisibleStages()
        {
            var all = AllStages.Where(Matches);
            switch (_tab)
            {
                case Tab.On: return all.Where(_host.IsOn).ToList();
                case Tab.Off: return all.Where(x => !_host.IsOn(x)).ToList();
                case Tab.Favorites: return all.Where(IsFav).ToList();
                case Tab.Conflicts: return all.Where(InConflict).ToList();
                case Tab.Downloaded: return new List<InstalledStage>();
                default: return all.ToList();
            }
        }

        Control StageCard(InstalledStage s)
        {
            var card = new LibCard
            {
                Title = s.Name, Line1 = s.IsNewSong ? (s.TargetLabel ?? "New song") : "Replaces " + s.Target.Label, Music = s.IsSong,
                Line2 = (s.IsSong ? "Song" : "Stage") + (s.GameBananaModId != 0 ? "  •  by " + s.Author : "") + CostText(s.Id),
                On = _host.IsOn(s), ShowSwitch = true,
                Tag1 = _host.IsPending(s) ? "Not saved yet" : InConflict(s) ? "Same stage as another" : null,
                Tag1Color = _host.IsPending(s) ? Theme.Accent : Theme.Warn, Margin = new Padding(0, 0, 12, 12),
            };
            if (!s.IsSong)
            {
                var spot = _host.WhereInGame(s);
                if (spot != null)
                {
                    card.Line3 = spot.Page == 0 ? "Not on the stage select pages" : $"Page {spot.Page}, row {spot.Row}, icon {spot.Column}"
                        + (spot.Button == 0 ? "" : spot.Button >= 0x4000 ? (spot.Button >= 0x8000 ? ", L list" : ", R list") : ", " + spot.HowToPick);
                    card.Height += 18;
                    _tips.SetToolTip(card, "In the game: " + spot.Text);
                }
            }
            if (string.IsNullOrEmpty(s.ThumbUrl)) card.Picture = LocalPicture(s);
            LoadUrl(s.ThumbUrl, card);
            card.SwitchClicked += () => _host.SetOn(new[] { s }, !_host.IsOn(s));
            card.DeleteClicked += () => _host.Delete(s);
            if (_host.SourceOf(s) != null) { card.MoreText = "Add more from this download"; card.MoreClicked += () => _host.AddMore(s); }
            Updatable(card, s.GameBananaModId, s.GameBananaFileId, s.PageUrl, s.Name);
            card.Favorite = IsFav(s);
            card.FavoriteClicked += () => { _host.Favs?.Toggle(new Favorite { Key = Favorites.KeyOf(s.GameBananaModId, s.Id), Name = s.Name, Author = s.Author, ThumbUrl = s.ThumbUrl, ModId = s.GameBananaModId, Kind = s.IsSong ? "music" : "stage", Category = s.Target.Label }); Refresh(); };
            return card;
        }

        /// <summary>Downloads with costumes not added yet (none added, or fewer than the download has).</summary>
        List<DownloadInfo> NotInstalled() =>
            _downloads.Where(d => Added(d) == 0 || (d.CostumeCount > 0 && Added(d) < d.CostumeCount)).ToList();

        bool InConflict(InstalledSkin s) => Skins.Count(x => x.SlotListId == s.SlotListId && x.SlotIndex == s.SlotIndex) > 1;

        IEnumerable<InstalledSkin> Visible_Skins()
        {
            var all = Skins.Where(Matches);
            switch (_tab)
            {
                case Tab.On: return all.Where(_host.IsOn).ToList();
                case Tab.Off: return all.Where(s => !_host.IsOn(s)).ToList();
                case Tab.Favorites: return all.Where(IsFav).ToList();
                case Tab.Conflicts: return all.Where(InConflict).ToList();
                case Tab.Downloaded: return new List<InstalledSkin>();
                default: return all.ToList();
            }
        }

        void TurnAllOn()
        {
            if (_textureMode) { foreach (var p in VisibleTextures().Where(x => !x.Enabled).ToList()) _host.SetOn(p, true); return; }
            if (_stageMode)
            {
                _host.SetOn(VisibleStages().GroupBy(x => x.CardPath.ToUpperInvariant()).Select(g => g.FirstOrDefault(_host.IsOn) ?? g.OrderByDescending(x => x.InstalledAt).First()).ToList(), true);
                return;
            }
            // one skin per slot: if several are in the same slot, the one that is on (or the newest) wins
            var pick = Visible_Skins().GroupBy(s => new { s.SlotListId, s.SlotIndex })
                .Select(g => g.FirstOrDefault(_host.IsOn) ?? g.OrderByDescending(s => s.InstalledAt).First()).ToList();
            _host.SetOn(pick, true);
        }

        public new void Refresh()
        {
            _ownMusic.Visible = _musicMode;
            if (_host.Lib == null) { ShowEmpty("Brawl Online isn't set up on this PC yet."); return; }
            try { _downloads = DownloadInfo.All(_host.DataFolder); } catch { _downloads = new List<DownloadInfo>(); }
            var all = Skins.Where(Matches).ToList();
            var dls = NotInstalled().Where(Matches).ToList();
            int on = all.Count(_host.IsOn);
            var files = _host.Lib.Stages;
            _count.Text = $"{Skins.Count} skins, {files.Count(x => !x.IsSong)} stages, {files.Count(x => x.IsSong)} songs" + (AllTextures.Count > 0 ? $", {AllTextures.Count} texture pack{(AllTextures.Count == 1 ? "" : "s")}" : "") + $"  •  {Skins.Count(_host.IsOn) + files.Count(_host.IsOn)} on";
            Theme.Toggle(_kindSkins, !_stageMode && !_textureMode); Theme.Toggle(_kindStages, _stageMode && !_musicMode); Theme.Toggle(_kindMusic, _musicMode); Theme.Toggle(_kindTextures, _textureMode);
            _who.Visible = !_stageMode && !_textureMode;
            _sortBy.Left = (_stageMode || _textureMode ? _search.Right : _who.Right) + 12;
            if (_textureMode) { ShowTextures(); return; }
            if (_stageMode)
            {
                var allSt = AllStages.Where(Matches).ToList();
                int onSt = allSt.Count(_host.IsOn);
                BuildTabs(new Dictionary<Tab, int>
                {
                    [Tab.All] = allSt.Count, [Tab.On] = onSt, [Tab.Off] = allSt.Count - onSt, [Tab.Favorites] = allSt.Count(IsFav),
                    [Tab.Downloaded] = dls.Count, [Tab.Conflicts] = allSt.Count(InConflict),
                });
            }
            else
            BuildTabs(new Dictionary<Tab, int>
            {
                [Tab.All] = all.Count, [Tab.On] = on, [Tab.Off] = all.Count - on, [Tab.Favorites] = all.Count(IsFav),
                [Tab.Downloaded] = dls.Count, [Tab.Conflicts] = all.Count(InConflict),
            });

            int scroll = -_grid.AutoScrollPosition.Y;
            _grid.SuspendLayout();
            foreach (Control c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
            _grid.Controls.Clear();
            if (_tab == Tab.Downloaded)
            {
                foreach (var d in dls) _grid.Controls.Add(DownloadCard(d));
                if (dls.Count == 0) { _grid.ResumeLayout(); ShowEmpty("Nothing waiting here. Files you download in the Mod Store and don't install yet show up here."); return; }
            }
            else if (_stageMode)
            {
                var shownSt = Sorted(VisibleStages(), x => x.Id, x => x.Name, x => x.InstalledAt, x => x.Target.Label).ToList();
                foreach (var st in shownSt) _grid.Controls.Add(StageCard(st));
                if (shownSt.Count == 0)
                {
                    _grid.ResumeLayout();
                    ShowEmpty(_musicMode
                        ? (AllStages.Count == 0 ? "You haven't added any music yet. Add your own MP3s with \"+ Add your own music\" (top right), or find songs in the Mod Store's Music tab." : _tab == Tab.Conflicts ? "No conflicts: every P+ song has at most one of yours." : "No songs match these filters.")
                        : AllStages.Count == 0 ? "You haven't added any stages yet. In the Mod Store, switch to Stages to find reskins that play exactly like P+'s stages."
                        : _tab == Tab.Conflicts ? "No conflicts: every stage has at most one of your reskins."
                        : _tab == Tab.Favorites ? "No favorite stages yet. Hover over a stage and click the ☆ in its corner to star it."
                        : "No stages match these filters.");
                    return;
                }
            }
            else
            {
                var shown = Sorted(Visible_Skins(), x => x.Id, x => x.Name, x => x.InstalledAt, x => $"{x.Character.Name} {x.SlotIndex:00}").ToList();
                foreach (var s in shown) _grid.Controls.Add(SkinCard(s));
                if (shown.Count == 0)
                {
                    _grid.ResumeLayout();
                    ShowEmpty(Skins.Count == 0 ? "You haven't added any skins yet. Find some in the Mod Store, or click \"Add from file\" for one you already downloaded."
                        : _tab == Tab.Conflicts ? "No conflicts: every costume slot has at most one of your skins."
                        : _tab == Tab.Favorites ? "No favorites here yet. Hover over a mod and click the ☆ in its corner to star it."
                        : "No skins match these filters.");
                    return;
                }
            }
            _grid.ResumeLayout();
            _empty.Visible = false; _grid.Visible = true;
            _grid.AutoScrollPosition = new Point(0, scroll);
            _allOff.Enabled = _allOn.Enabled = _tab != Tab.Downloaded;
            _meter.Invalidate();
        }

        // ----- texture packs -----

        List<TexturePack> AllTextures => _host.Textures?.All ?? new List<TexturePack>();

        List<TexturePack> VisibleTextures()
        {
            string q = _search.Text.Trim();
            var all = AllTextures.Where(p => q == "" || Has(p.Name, q) || Has(p.Author, q));
            if (_tab == Tab.On) all = all.Where(p => p.Enabled);
            if (_tab == Tab.Off) all = all.Where(p => !p.Enabled);
            switch (_sortBy.SelectedIndex)
            {
                case 1: return all.OrderByDescending(p => p.InstalledAt).ToList();
                case 3: return all.OrderByDescending(p => p.Bytes).ToList();
                default: return all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        void ShowTextures()
        {
            var all = AllTextures;
            BuildTabs(new Dictionary<Tab, int> { [Tab.All] = all.Count, [Tab.On] = all.Count(p => p.Enabled), [Tab.Off] = all.Count(p => !p.Enabled) });
            _grid.SuspendLayout();
            foreach (Control c in _grid.Controls.Cast<Control>().ToList()) c.Dispose();
            _grid.Controls.Clear();
            var shown = VisibleTextures();
            foreach (var p in shown) _grid.Controls.Add(TextureCard(p));
            _grid.ResumeLayout();
            _allOff.Enabled = _allOn.Enabled = true;
            if (shown.Count == 0)
            {
                ShowEmpty(all.Count == 0
                    ? "No texture packs yet. A texture pack is a download full of pictures named like tex1_64x64_....png: Dolphin shows them instead of the game's own textures. Click \"Add from file\" and pick one. They only change what your screen shows, so they're always safe online."
                    : "No texture packs match these filters.");
                return;
            }
            _empty.Visible = false; _grid.Visible = true;
        }

        Control TextureCard(TexturePack p)
        {
            var card = new LibCard
            {
                Title = p.Name, Line1 = $"Texture pack  •  {p.Pictures} pictures",
                Line2 = Space.MB(p.Bytes) + (p.Author != null ? "  •  by " + p.Author : "") + (p.Enabled ? "  •  on Brawl Online's drive" : "  •  kept with the app"),
                On = p.Enabled, ShowSwitch = true, CanFavorite = false, Margin = new Padding(0, 0, 12, 12),
            };
            if (p.ThumbUrl != null) LoadUrl(p.ThumbUrl, card); else LoadSample(p, card);
            card.SwitchClicked += () => _host.SetOn(p, !p.Enabled);
            card.DeleteClicked += () => _host.Delete(p);
            return card;
        }

        /// <summary>One of the pack's own pictures, for its card.</summary>
        async void LoadSample(TexturePack p, LibCard card)
        {
            if (_pictures.TryGetValue("tex:" + p.Id, out var have)) { card.Picture = have; return; }
            string folder = _host.Textures.FolderOf(p);
            var img = await Task.Run(() =>
            {
                try
                {
                    // the biggest of the first few pictures looks best
                    var f = Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories).Take(40).OrderByDescending(x => new FileInfo(x).Length).FirstOrDefault();
                    if (f == null) return null;
                    using (var i = Image.FromFile(f))
                    {
                        float k = Math.Min(1f, 256f / Math.Max(i.Width, i.Height));
                        return (Image)new Bitmap(i, Math.Max(1, (int)(i.Width * k)), Math.Max(1, (int)(i.Height * k)));
                    }
                }
                catch { return null; }
            });
            if (img == null || card.IsDisposed) return;
            _pictures["tex:" + p.Id] = img; card.Picture = img; card.Invalidate();
        }

        bool IsFav(InstalledSkin s) => _host.Favs?.Has(Favorites.KeyOf(s.GameBananaModId, s.Id)) == true;
        bool IsFav(InstalledStage s) => _host.Favs?.Has(Favorites.KeyOf(s.GameBananaModId, s.Id)) == true;

        /// <summary>The list in the order chosen in the Sort box (the natural order breaks ties).</summary>
        IEnumerable<T> Sorted<T>(IEnumerable<T> items, Func<T, string> id, Func<T, string> name, Func<T, DateTime> added, Func<T, string> natural)
        {
            var cost = _host.Space?.Cost;
            long Cost(T x) => cost != null && cost.TryGetValue(id(x), out long c) ? c : 0;
            bool Fav(T x) => _host.Favs?.Has(x is InstalledSkin k ? Favorites.KeyOf(k.GameBananaModId, k.Id) : x is InstalledStage st ? Favorites.KeyOf(st.GameBananaModId, st.Id) : "") == true;
            switch (_sortBy.SelectedIndex)
            {
                case 1: return items.OrderByDescending(added);
                case 2: return items.OrderBy(name, StringComparer.CurrentCultureIgnoreCase);
                case 3: return items.OrderByDescending(Cost).ThenBy(natural);
                default: return items.OrderByDescending(Fav).ThenBy(natural);
            }
        }

        void ShowEmpty(string text)
        {
            bool clean = _host.OnCleanCard && !_textureMode;
            if (clean) text = "The clean card (no mods) is in place, for playing P+ exactly as it comes. Your mods are safe on your other card.\r\n\r\nClick \"Launch modded\" at the top to play with them, or switch back here.";
            _emptyButton.Text = clean ? "Back to my mods" : "Open the Mod Store";
            _grid.Visible = false; _empty.Visible = true;
            _emptyText.AutoSize = false;
            _emptyText.Size = new Size(520, TextRenderer.MeasureText(text, Theme.Body, new Size(520, 0), TextFormatFlags.WordBreak).Height + 4);
            _emptyText.Text = text;
            _emptyButton.Location = new Point(4, _emptyText.Bottom + 14);
        }

        void BuildTabs(Dictionary<Tab, int> counts)
        {
            foreach (Control c in _tabs.Controls.Cast<Control>().ToList()) c.Dispose();
            _tabs.Controls.Clear();
            foreach (var kv in counts)
            {
                var t = kv.Key;
                var b = new Button
                {
                    Text = $"{(t == Tab.Favorites ? "★ Favorites" : t.ToString())}  ({kv.Value})", FlatStyle = FlatStyle.Flat, AutoSize = true, Cursor = Cursors.Hand,
                    Font = t == _tab ? Theme.BodyBold : Theme.Body, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 6, 0),
                    BackColor = t == _tab ? Theme.PanelHover : Theme.Back, ForeColor = t == _tab ? Theme.Text : Theme.Muted,
                };
                b.FlatAppearance.BorderColor = t == _tab ? Theme.Accent : Theme.Line;
                b.Click += (s, e) => { _tab = t; Refresh(); };
                _tabs.Controls.Add(b);
            }
        }

        /// <summary>A newer version on GameBanana: a green tag and an "Update" link instead of "Add more".</summary>
        void Updatable(LibCard card, long mod, long file, string page, string name)
        {
            if (!_host.HasUpdate(file)) return;
            if (card.Tag1 == null) { card.Tag1 = "Update available"; card.Tag1Color = Theme.Good; }
            card.MoreText = "Update to the new version";
            card.ClearMore();
            card.MoreClicked += () => _host.UpdateMod(mod, file, page, name);
        }

        Control SkinCard(InstalledSkin s)
        {
            var card = new LibCard
            {
                Title = s.Name,
                Line1 = $"{s.Character.Name}  •  costume {s.SlotIndex + 1}",
                Line2 = (s.GameBananaModId != 0 ? "by " + s.Author : "added from a file") + CostText(s.Id),
                On = _host.IsOn(s), ShowSwitch = true,
                Tag1 = _host.IsPending(s) ? "Not saved yet" : InConflict(s) ? "Same slot as another" : null,
                Tag1Color = _host.IsPending(s) ? Theme.Accent : Theme.Warn,
                Margin = new Padding(0, 0, 12, 12),
            };
            card.Picture = PictureFor(s, card);
            LoadModel(s, card);
            card.SwitchClicked += () => _host.SetOn(new[] { s }, !_host.IsOn(s));
            if (_host.SourceOf(s) != null) { card.MoreText = "Add more from this download"; card.MoreClicked += () => _host.AddMore(s); }
            card.DeleteClicked += () => _host.Delete(s);
            Updatable(card, s.GameBananaModId, s.GameBananaFileId, s.PageUrl, s.Name);
            card.Favorite = IsFav(s);
            card.FavoriteClicked += () => { _host.Favs?.Toggle(new Favorite { Key = Favorites.KeyOf(s.GameBananaModId, s.Id), Name = s.Name, Author = s.Author, ThumbUrl = s.ThumbUrl, ModId = s.GameBananaModId, Kind = "skin", Category = s.Character.Name }); Refresh(); };
            return card;
        }

        Control DownloadCard(DownloadInfo d)
        {
            var card = new LibCard
            {
                Title = d.ModName, Line1 = (d.Category ?? "").Replace(" Costumes", "") + (Added(d) > 0 ? $"  •  {Added(d)} of {d.CostumeCount} added" : d.CostumeCount > 1 ? $"  •  {d.CostumeCount} costumes" : ""),
                Line2 = "by " + d.Author,
                ShowSwitch = false, ActionText = Added(d) > 0 ? "Add more" : "Install", Tag1 = "Downloaded", Tag1Color = Theme.Muted,
                Margin = new Padding(0, 0, 12, 12),
            };
            LoadUrl(d.ThumbUrl, card);
            card.ActionClicked += () => _host.InstallDownload(d);
            card.Favorite = _host.Favs?.Has("gb:" + d.ModId) == true;
            card.FavoriteClicked += () => { _host.Favs?.Toggle(new Favorite { Key = "gb:" + d.ModId, Name = d.ModName, Author = d.Author, ThumbUrl = d.ThumbUrl, ModId = d.ModId, Category = d.Category }); Refresh(); };
            card.DeleteClicked += () =>
            {
                if (MessageBox.Show(FindForm(), $"Delete the downloaded file for \"{d.ModName}\"?", "Delete download", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                d.Delete(); Refresh();
            };
            return card;
        }

        Image PictureFor(InstalledSkin s, LibCard card)
        {
            if (!string.IsNullOrEmpty(s.ThumbUrl)) { LoadUrl(s.ThumbUrl, card); return null; }
            string key = "skin:" + s.Id;
            if (_pictures.TryGetValue(key, out var img)) return img;
            string png = Path.Combine(_host.Lib.SkinFolder(s.Id), PictureKind.SelectPortrait + ".png");
            try { if (File.Exists(png)) using (var tmp = Image.FromFile(png)) return _pictures[key] = new Bitmap(tmp); } catch { }
            try { return _host.SlotPicture(s); } catch { return null; }
        }

        /// <summary>Swaps the card's picture for a drawing of the skin's 3D model (made once, kept in the skin's folder).</summary>
        async void LoadModel(InstalledSkin s, LibCard card)
        {
            string key = "model:" + s.Id;
            if (_pictures.TryGetValue(key, out var have)) { card.Picture = have; return; }
            var main = s.Files.FirstOrDefault(f => CostumeName.Parse(Path.GetFileName(f.CardPath))?.Kind == "");
            if (main == null) return;
            string folder = _host.Lib.SkinFolder(s.Id);
            string costume = Path.Combine(folder, Path.GetFileName(main.CardPath));
            if (!File.Exists(costume)) return;
            var bmp = await Task.Run(() => ModelPreview.Cached(costume, Path.Combine(folder, "model-preview.png")));
            if (bmp == null || card.IsDisposed) return;
            _pictures[key] = card.Picture = bmp;
            card.Invalidate();
        }

        /// <summary>A stage mod's own stage select picture, for mods added from a file.</summary>
        Image LocalPicture(InstalledStage s)
        {
            try
            {
                string f = Path.Combine(_host.Lib.StageFolder(s.Id), "select-preview.png");
                if (_pictures.TryGetValue(f, out var have)) return have;
                if (!File.Exists(f)) return null;
                using (var i = Image.FromFile(f)) return _pictures[f] = new Bitmap(i);
            }
            catch { return null; }
        }

        async void LoadUrl(string url, LibCard card)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (_pictures.TryGetValue(url, out var have)) { if (card.Picture == null) card.Picture = have; return; }
            string cache = Path.Combine(_host.DataFolder, "Cache");
            var bytes = await Task.Run(() => GameBanana.Picture(url, cache));
            if (bytes == null || card.IsDisposed) return;
            try
            {
                using (var ms = new MemoryStream(bytes)) using (var i = Image.FromStream(ms)) { _pictures[url] = new Bitmap(i); if (card.Picture == null || card.ShowSwitch == false) card.Picture = _pictures[url]; }
                card.Invalidate();
            }
            catch { }
        }
    }

    /// <summary>A card in My Mods: picture, name, an on/off switch and a delete button.</summary>
    /// <summary>The room for mods on the SD card: what mods use, what's kept free, what's left.</summary>
    class SpaceMeter : Control
    {
        public SpaceReport Report;
        readonly ToolTip _tip = new ToolTip();

        public SpaceMeter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(560, 36); BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = Report;
            if (r == null) return;
            long room = Math.Max(1, r.Mods + r.Free);
            var bar = new Rectangle(0, 22, Width - 1, 9);
            using (var back = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillRectangle(back, bar);
            int x = bar.Left;
            void Seg(long bytes, Color c)
            {
                int w = (int)Math.Round(bar.Width * (double)Math.Max(0, bytes) / room);
                if (w <= 0) return;
                using (var b = new SolidBrush(c)) g.FillRectangle(b, x, bar.Top, Math.Min(w, bar.Right - x), bar.Height);
                x += w;
            }
            Seg(r.Mods, Theme.Accent);
            Seg(Math.Min(r.Margin, r.Free), Color.FromArgb(140, 140, 150));
            Seg(r.Usable, Theme.Good);
            bool low = r.Usable < 5L * 1024 * 1024;
            string text = $"Room for mods: {Space.MB(r.Usable)} free";
            string rest = $" · your mods {Space.MB(r.Mods)} · kept free {Space.MB(Math.Min(r.Margin, r.Free))}";
            using (var f = new Font(Theme.Small, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, text, f, new Point(0, 2), low ? Theme.Bad : Theme.Text);
                int w = TextRenderer.MeasureText(text, f).Width;
                TextRenderer.DrawText(g, rest, Theme.Small, new Point(w - 4, 2), Theme.Muted);
            }
            _tip.SetToolTip(this, $"The SD card is {Space.MB(r.Total)}. P+ itself uses {Space.MB(r.PPlus)}.\r\n" +
                $"Your mods that are on use {Space.MB(r.Mods)} more than the P+ files they replace (skins and stages are about the same size; songs are the big ones).\r\n" +
                $"{Space.MB(r.Margin)} is kept free for Brawl Online's online plugin and P+ updates.\r\n" +
                $"{Space.MB(r.Usable)} is left for more mods.");
        }
    }

    class LibCard : Control
    {
        public Image Picture;
        public string Title, Line1, Line2, Line3, Tag1, ActionText, MoreText;
        public Color Tag1Color = Theme.Accent;
        public bool On, ShowSwitch, Music, Favorite, CanFavorite = true;
        public event Action SwitchClicked, DeleteClicked, ActionClicked, MoreClicked, FavoriteClicked;
        bool _hover, _starHover;
        Rectangle _switch, _delete, _action, _more, _star;

        public void ClearMore() => MoreClicked = null;

        public LibCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(218, 270); BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Cursor = Cursors.Default; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool star = CanFavorite && _star.Contains(e.Location);
            if (star != _starHover) { _starHover = star; Invalidate(); }
            bool hot = star || _switch.Contains(e.Location) || _delete.Contains(e.Location) || _action.Contains(e.Location) || (MoreText != null && _more.Contains(e.Location));
            Cursor = hot ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (CanFavorite && _star.Contains(e.Location)) { FavoriteClicked?.Invoke(); return; }
            if (ShowSwitch && _switch.Contains(e.Location)) SwitchClicked?.Invoke();
            else if (_delete.Contains(e.Location)) DeleteClicked?.Invoke();
            else if (!ShowSwitch && _action.Contains(e.Location)) ActionClicked?.Invoke();
            else if (MoreText != null && _more.Contains(e.Location)) MoreClicked?.Invoke();
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(2, 1, Width - 5, Height - 6);
            bool lit = !ShowSwitch || On;
            Theme.CardFrame(g, card, _hover, false);
            var pic = new Rectangle(card.X + 1, card.Y + 1, card.Width - 1, 122);
            using (var area = Theme.PictureArea(g, pic))
            {
                var clip = g.Clip; g.SetClip(area, CombineMode.Intersect);
                if (Picture != null)
                {
                    float scale = Math.Min((float)(pic.Width - 8) / Picture.Width, (float)(pic.Height - 8) / Picture.Height);
                    int pw = (int)(Picture.Width * scale), ph = (int)(Picture.Height * scale);
                    var small = Theme.Scaled(Picture, pw, ph);
                    Theme.Blit(g, small, pic.X + (pic.Width - pw) / 2, pic.Y + (pic.Height - ph) / 2);
                }
                else if (Music) using (var f = new Font("Segoe UI Symbol", 34f)) TextRenderer.DrawText(g, "♫", f, pic, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                else TextRenderer.DrawText(g, "no picture", Theme.Small, pic, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (!lit) using (var veil = new SolidBrush(Color.FromArgb(140, Theme.Back))) g.FillPath(veil, area);
                g.Clip = clip;
            }
            // on: a gold strip under the picture
            using (var b = new SolidBrush(ShowSwitch && On ? Theme.Accent : Color.FromArgb(50, 255, 255, 255)))
                g.FillRectangle(b, pic.X, pic.Bottom, pic.Width, ShowSwitch && On ? 3 : 1);
            if (Tag1 != null) Theme.Pill(g, new Point(pic.X + 7, pic.Y + 7), Tag1, Tag1Color, Theme.AccentText);
            _star = CanFavorite ? new Rectangle(pic.Right - 32, pic.Y + 6, 26, 26) : Rectangle.Empty;
            if (CanFavorite && (Favorite || _hover)) Theme.Star(g, _star, Favorite, _starHover);
            int y = pic.Bottom + 6;
            TextRenderer.DrawText(g, Title ?? "", Theme.BodyBold, new Rectangle(8, y, Width - 16, 38), lit ? Theme.Text : Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Line1 ?? "", Theme.Small, new Rectangle(8, y + 40, Width - 16, 18), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, Line2 ?? "", Theme.Small, new Rectangle(8, y + 58, Width - 16, 18), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (Line3 != null)
            {
                TextRenderer.DrawText(g, Line3, Theme.Small, new Rectangle(8, y + 76, Width - 16, 18), lit ? Theme.Text : Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                y += 18;
            }
            if (MoreText != null)
            {
                _more = new Rectangle(8, y + 78, Width - 16, 18);
                using (var u = new Font(Theme.Small, FontStyle.Underline))
                    TextRenderer.DrawText(g, MoreText, u, _more, Theme.Accent, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
            int by = Height - 34;
            _delete = new Rectangle(8, by, 60, 24);
            TextRenderer.DrawText(g, "Delete", Theme.Small, _delete, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (ShowSwitch)
            {
                _switch = new Rectangle(Width - 58, by + 1, 46, 24);
                TextRenderer.DrawText(g, On ? "On" : "Off", Theme.Small, new Rectangle(_switch.X - 36, by, 32, 24), On ? Theme.Accent : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                using (var path = Theme.Rounded(_switch, 11))
                {
                    using (var b = new SolidBrush(On ? Theme.Accent : Theme.Line)) g.FillPath(b, path);
                }
                int kx = On ? _switch.Right - 21 : _switch.X + 3;
                using (var b = new SolidBrush(On ? Theme.AccentText : Theme.Muted)) g.FillEllipse(b, kx, _switch.Y + 3, 18, 18);
            }
            else if (ActionText != null)
            {
                var size = TextRenderer.MeasureText(ActionText, Theme.BodyBold);
                _action = new Rectangle(Width - size.Width - 28, by - 2, size.Width + 20, 28);
                using (var path = Theme.Rounded(_action, 6)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                TextRenderer.DrawText(g, ActionText, Theme.BodyBold, _action, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
