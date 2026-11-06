// Adding music, in two plain steps for each song:
//   1. Where should it play? Pick a stage (by its stage select picture) or a menu.
//   2. Add it to the songs that can play there (P+'s stay), or replace one of them (shown as a short list).
// Every song in the download is listed on the left with what will happen to it; "Use this for every song" copies the
// place to the rest. Music is only what you hear, so none of this changes the game online.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    /// <summary>The "Add music" window: for each song in a download, where it plays and whether it's added or replaces
    /// one. On Install, Plans holds what to put on the card.</summary>
    class SongForm : Form
    {
        readonly Installer _installer;
        readonly SkinPackage _pkg;
        public List<StagePlan> Plans { get; private set; } = new List<StagePlan>();

        /// <summary>One song file from the download and what's been picked for it.</summary>
        sealed class Row
        {
            public string File; public long Size;
            public SongPlace Place;        // where it plays (null: not picked yet)
            public SongInfo Replace;       // the P+ song it replaces (null: added as a new song)
            public bool Skip;              // left out
            public string Name => Path.GetFileNameWithoutExtension(File);
        }

        readonly List<Row> _rows = new List<Row>();
        Row _row;
        List<SongInfo> _songs;
        List<SongPlace> _places;
        long _free;
        readonly IList<string> _stages;

        readonly ListBox _songList = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 54, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false };
        readonly Label _step1 = Theme.Label("1. Where should it play?", Theme.Heading), _step2 = Theme.Label("2. Add it, or replace one of the songs there?", Theme.Heading);
        readonly TextBox _search = new TextBox { Width = 220, BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        readonly Button _tabStages = Theme.Secondary("Stages"), _tabMenus = Theme.Secondary("Menus && more");
        readonly FlowLayoutPanel _tiles = new FlowLayoutPanel { AutoScroll = true, WrapContents = true, BackColor = Theme.Back, Padding = new Padding(0, 0, 0, 10) };
        readonly Panel _choice = new Panel { BackColor = Theme.Panel };
        readonly RadioButton _add = new RadioButton { AutoSize = false, Font = Theme.BodyBold, ForeColor = Theme.Text, BackColor = Color.Transparent };
        readonly RadioButton _replace = new RadioButton { AutoSize = false, Font = Theme.BodyBold, ForeColor = Theme.Text, BackColor = Color.Transparent };
        readonly Label _addNote = Theme.Label("", Theme.Small, Theme.Muted), _replaceNote = Theme.Label("", Theme.Small, Theme.Muted);
        readonly ListBox _replaceList = new ListBox { BorderStyle = BorderStyle.None, BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Body, IntegralHeight = false };
        readonly Button _install = Theme.Primary("Install"), _forAll = Theme.Secondary("Use this place for every song"), _skip = Theme.Secondary("Leave this song out");
        readonly Label _summary = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Label _safe = Theme.Label("Music is only what you hear: it never changes the game, so it's safe online. In rooms you host, players get your songs too.", Theme.Small, Theme.Muted);
        readonly List<PictureTile> _tileList = new List<PictureTile>();
        bool _menusTab, _filling;
        Dictionary<(int, string), Bitmap> _pics = new Dictionary<(int, string), Bitmap>();

        /// <param name="stages">stage files (card paths or names) this music came with: their stage is picked for it</param>
        public SongForm(Installer installer, SkinPackage pkg, IList<string> stages = null)
        {
            _installer = installer; _pkg = pkg; _stages = stages ?? pkg.StageFiles;
            Text = "Add music: " + pkg.Name;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1180, 800); MinimumSize = new Size(940, 600);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var title = Theme.Label(pkg.SongFiles.Count == 1 ? "Where should this song play?" : $"{pkg.SongFiles.Count} songs: where should each one play?", Theme.Title);
            title.Location = new Point(24, 14);
            var hint = Theme.Label("Pick a song on the left, then where it plays: a stage or a menu. Then add it to the songs there (P+'s stay) or have it replace one of them.", Theme.Body, Theme.Muted);
            hint.Location = new Point(26, 48); hint.AutoSize = false; hint.Size = new Size(1100, 22);

            var yourSongs = Theme.Label("Your songs", Theme.Heading); yourSongs.Location = new Point(24, 84);
            _songList.Location = new Point(24, 114); _songList.Width = 300;
            _songList.DrawItem += DrawSong;
            _songList.SelectedIndexChanged += (s, e) => { if (_songList.SelectedItem is Row r) ShowRow(r); };
            _forAll.Click += (s, e) => UseForAll();
            _skip.Click += (s, e) => { if (_row == null) return; _row.Skip = !_row.Skip; ShowRow(_row); Changed(); };

            _step1.Location = new Point(344, 84);
            Theme.Cue(_search, "Find a stage…");
            _search.TextChanged += (s, e) => FilterTiles();
            _tabStages.Click += (s, e) => { _menusTab = false; ShowTiles(); };
            _tabMenus.Click += (s, e) => { _menusTab = true; ShowTiles(); };
            _tiles.Location = new Point(344, 150);

            var step2Head = _step2; step2Head.Location = new Point(14, 10); step2Head.AutoSize = false; step2Head.Size = new Size(760, 24);
            _add.Location = new Point(16, 40); _add.Size = new Size(380, 24);
            _addNote.Location = new Point(36, 64); _addNote.AutoSize = false; _addNote.Size = new Size(360, 52);
            _replace.Location = new Point(410, 40); _replace.Size = new Size(380, 24);
            _replaceNote.Location = new Point(430, 64); _replaceNote.AutoSize = false; _replaceNote.Size = new Size(360, 20);
            _replaceList.Location = new Point(430, 86); _replaceList.Size = new Size(360, 96);
            _add.CheckedChanged += (s, e) => { if (_filling || !_add.Checked || _row == null) return; _row.Replace = null; ShowChoice(); Changed(); };
            _replace.CheckedChanged += (s, e) =>
            {
                if (_filling || !_replace.Checked || _row?.Place == null) return;
                if (_row.Replace == null) _row.Replace = _replaceList.SelectedItem is SongPick p ? p.Song : _row.Place.Songs.FirstOrDefault();
                ShowChoice(); Changed();
            };
            _replaceList.SelectedIndexChanged += (s, e) =>
            {
                if (_filling || _row?.Place == null || !(_replaceList.SelectedItem is SongPick p)) return;
                _row.Replace = p.Song; ShowChoice(); Changed();
            };
            _choice.Controls.AddRange(new Control[] { step2Head, _add, _addNote, _replace, _replaceNote, _replaceList });

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Panel };
            _summary.Location = new Point(24, 12); _summary.AutoSize = false; _summary.Size = new Size(700, 22);
            _safe.Location = new Point(24, 38); _safe.AutoSize = false; _safe.Size = new Size(760, 20);
            var cancel = Theme.Secondary("Cancel");
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _install.Click += (s, e) => Install();
            foot.Controls.AddRange(new Control[] { _summary, _safe, cancel, _install });
            void LayFoot()
            {
                _install.Location = new Point(foot.Width - _install.Width - 24, (foot.Height - _install.Height) / 2);
                cancel.Location = new Point(_install.Left - cancel.Width - 10, (foot.Height - cancel.Height) / 2);
            }
            foot.Resize += (s, e) => LayFoot(); _install.SizeChanged += (s, e) => LayFoot();
            Controls.AddRange(new Control[] { title, hint, yourSongs, _songList, _forAll, _skip, _step1, _search, _tabStages, _tabMenus, _tiles, _choice, foot });
            Resize += (s, e) => Lay();
            _choice.VisibleChanged += (s, e) => Lay();
            Lay();
            _summary.Text = "Reading P+'s songs…";
            _install.Enabled = false; _forAll.Enabled = _skip.Enabled = false; _choice.Visible = false;
            Shown += async (s, e) => await Fill();
        }

        /// <summary>Places everything for the window's size; when step 2 is hidden the stage pictures take its
        /// room.</summary>
        void Lay()
        {
            int bottom = ClientSize.Height - 70;
            _songList.Height = Math.Max(120, bottom - 114 - 96);
            _forAll.Location = new Point(24, _songList.Bottom + 10); _forAll.Width = 300;
            _skip.Location = new Point(24, _forAll.Bottom + 8); _skip.Width = 300;
            int x = 344, w = ClientSize.Width - x - 24;
            _tabStages.Location = new Point(x, 114); _tabMenus.Location = new Point(_tabStages.Right + 8, 114);
            _search.Location = new Point(x + w - _search.Width, 118);
            int choiceH = 196;
            _choice.SetBounds(x, bottom - choiceH - 12, w, choiceH);
            _tiles.SetBounds(x, 150, w, Math.Max(120, (_choice.Visible ? _choice.Top - 10 : bottom - 12) - 150));   // step 2 hidden: the pictures use its room
            int half = (w - 40) / 2;
            _add.Width = _addNote.Width = half; _replace.Left = 24 + half + 10; _replace.Width = half;
            _replaceNote.Left = _replaceList.Left = _replace.Left + 20; _replaceNote.Width = _replaceList.Width = half - 20;
            _step2.Width = w - 28; _summary.Width = Math.Max(300, ClientSize.Width - 340); _safe.Width = _summary.Width;
            foreach (var h in _tiles.Controls.OfType<TileHeading>()) h.Width = Math.Max(200, _tiles.ClientSize.Width - 24);
        }

        /// <summary>A song in the "replace" list, saying when it's Brawl's own or also plays elsewhere.</summary>
        sealed class SongPick
        {
            public SongInfo Song;
            public override string ToString() => Song.Title + (Song.FromBrawl ? "   (Brawl's own)" : "") + (Song.Lists.Count > 1 ? $"   · also on {Song.Lists.Count - 1} more" : "");
        }

        /// <summary>Reads P+'s songs, where they play and the stage pictures from the card, then lists your songs with
        /// a first guess at their place (the stage they came with, one named in the download, or the P+ song with the
        /// same file name).</summary>
        async Task Fill()
        {
            Dictionary<string, string> map;
            try
            {
                var r = await Task.Run(() =>
                {
                    using (var card = _installer.OpenCard())
                    {
                        var songs = Songs.All(card);
                        var places = Songs.Places(card, songs);
                        var pics = StagePictures.Read(card, places.Where(p => p.Picture >= 0).SelectMany(p => new[] { (p.Picture, p.Region ?? "main"), (p.Picture, "main") }).Distinct());
                        return (songs, places, card.FreeBytes, Songs.StageTrackLists(card), pics);
                    }
                });
                _songs = r.songs; _places = r.places; _free = r.FreeBytes; map = r.Item4; _pics = r.pics;
            }
            catch (Exception e) { _summary.Text = "Couldn't read P+'s songs: " + Friendly.Explain(e); _summary.ForeColor = Theme.Bad; return; }
            if (IsDisposed) return;
            // the place this music is for: the stage files it came with, else a stage named in the download's name
            SongPlace For(string list) => list == null ? null : _places.FirstOrDefault(p => string.Equals(p.List, list, StringComparison.OrdinalIgnoreCase));
            var forStage = _stages.Select(f => For(Songs.TrackListOf(map, f))).FirstOrDefault(x => x != null)
                ?? For(Songs.GuessTrackList(_places.Select(p => p.List), new[] { _pkg.Name, Path.GetFileName(_pkg.Folder ?? "") }.Concat(_pkg.SongFiles.Select(f => Path.GetFileName(Path.GetDirectoryName(f)))).ToArray()));
            foreach (var f in _pkg.SongFiles)
            {
                var row = new Row { File = f, Size = new FileInfo(f).Length, Place = forStage };
                // a file named like a P+ song (packs made for Brawl use names like X02.brstm) replaces that song
                var same = _songs.FirstOrDefault(x => string.Equals(Path.GetFileName(x.FileName), row.Name, StringComparison.OrdinalIgnoreCase));
                if (same != null) { row.Replace = same; row.Place = For(same.Lists.FirstOrDefault()) ?? forStage; }
                if (row.Place != null && row.Place.OnePerFighter && row.Replace == null) row.Place = null;
                _rows.Add(row);
                _songList.Items.Add(row);
            }
            BuildTiles();
            _forAll.Enabled = _skip.Enabled = true; _forAll.Visible = _rows.Count > 1;
            _songList.SelectedIndex = 0;
            Changed();
        }

        // ----- the songs on the left

        void DrawSong(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || !(_songList.Items[e.Index] is Row r)) return;
            var g = e.Graphics; bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.PanelHover : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
            var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var t = new SolidBrush(r.Skip ? Theme.Muted : Theme.Text)) g.DrawString(r.Name, Theme.BodyBold, t, new RectangleF(e.Bounds.X + 12, e.Bounds.Y + 6, e.Bounds.Width - 20, 20), fmt);
            var (text, color) = Status(r);
            using (var m = new SolidBrush(color)) g.DrawString(text, Theme.Small, m, new RectangleF(e.Bounds.X + 12, e.Bounds.Y + 28, e.Bounds.Width - 20, 18), fmt);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, e.Bounds.X + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
        }

        /// <summary>The short line under a song in the left list: left out, needs a place, or what will happen to
        /// it.</summary>
        (string, Color) Status(Row r) =>
            r.Skip ? ("Left out", Theme.Muted)
            : r.Place == null ? ("Pick where it plays", Theme.Warn)
            : r.Replace != null ? ($"Replaces \"{r.Replace.Title}\" · {r.Place.Title}", Theme.Good)
            : ($"New song on {r.Place.Title}", Theme.Good);

        /// <summary>Shows the song picked on the left: switches to the tab with its place and shows its step
        /// 2.</summary>
        void ShowRow(Row r)
        {
            _row = r;
            _step1.Text = $"1. Where should \"{Short(r.Name)}\" play?";
            _skip.Text = r.Skip ? "Put this song back in" : "Leave this song out";
            // the tab with its place, and its tile picked
            if (r.Place != null) _menusTab = r.Place.Kind != SongPlace.Stage;
            ShowTiles();
            ShowChoice();
        }

        static string Short(string s) => s.Length > 40 ? s.Substring(0, 38) + "…" : s;

        // ----- step 1: the places, as pictures

        void BuildTiles()
        {
            foreach (var p in _places)
            {
                var tile = new PictureTile(178, 134) { Value = p, Title = p.Title };
                if (p.Kind == SongPlace.Stage)
                {
                    _pics.TryGetValue((p.Picture, p.Region ?? "main"), out var pic);
                    if (pic == null || p.Region != "main" && pic.Width < 40) _pics.TryGetValue((p.Picture, "main"), out pic);
                    tile.Picture = pic;
                    tile.Badge = p.Region == "L" ? "Hold L" : p.Region == "R" ? "Hold R" : p.Region == "Z" ? "Hold Z" : p.HowToPick != null && p.Region == "main" ? "In a list" : null;
                    tile.Subtitle = $"{p.Songs.Count} song{(p.Songs.Count == 1 ? "" : "s")}" + (p.HowToPick != null ? " · " + p.HowToPick : "");
                }
                else
                {
                    tile.Glyph = p.List.Equals("Menu", StringComparison.OrdinalIgnoreCase) ? "♫" : p.OnePerFighter ? "★" : "♪";
                    tile.GlyphColor = p.Kind == SongPlace.Menu ? Theme.Accent : Theme.Muted;
                    tile.Subtitle = (p.About ?? "") + $" · {p.Songs.Count} songs";
                }
                tile.Click += (s, e) => PickPlace(p);
                _tileList.Add(tile);
            }
        }

        /// <summary>Switches between the Stages and "Menus &amp; more" tabs.</summary>
        void ShowTiles()
        {
            Theme.Toggle(_tabStages, !_menusTab); Theme.Toggle(_tabMenus, _menusTab);
            _search.Visible = !_menusTab;
            FilterTiles();
        }

        /// <summary>Shows the tiles for the tab (and search), under headings by stage select page or kind, with the
        /// song's place picked.</summary>
        void FilterTiles()
        {
            if (_places == null) return;
            string q = _menusTab ? "" : (_search.Text ?? "").Trim();
            bool Matches(PictureTile t)
            {
                var p = (SongPlace)t.Value;
                if (_menusTab ? p.Kind == SongPlace.Stage : p.Kind != SongPlace.Stage) return false;
                if (q == "") return true;
                return p.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (p.SlotName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || p.Songs.Any(s => s.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            _tiles.SuspendLayout();
            foreach (Control c in _tiles.Controls.OfType<TileHeading>().ToList()) c.Dispose();
            _tiles.Controls.Clear();
            string group = null;
            foreach (var t in _tileList.Where(Matches))
            {
                var p = (SongPlace)t.Value;
                string g = p.Kind == SongPlace.Stage ? (q != "" ? "Found" : $"Stage select, page {p.Page}") : p.Kind == SongPlace.Menu ? "Menus" : "Other song lists (modes and stage versions not on the stage select)";
                if (g != group) { group = g; var h = new TileHeading(g) { Width = Math.Max(200, _tiles.ClientSize.Width - 24) }; _tiles.Controls.Add(h); _tiles.SetFlowBreak(h, true); }
                t.Selected = _row != null && _row.Place == p;
                _tiles.Controls.Add(t);
            }
            if (_tiles.Controls.Count == 0) { var none = new TileHeading("Nothing found. Try another name, or look in \"Menus & more\".") { Width = 600 }; _tiles.Controls.Add(none); }
            _tiles.ResumeLayout();
            DrawMissing();
            var picked = _tileList.FirstOrDefault(x => x.Selected && x.Parent == _tiles);
            if (picked != null) _tiles.ScrollControlIntoView(picked);
        }

        bool _drawingMissing;
        readonly Dictionary<string, Bitmap> _renders = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Stages the stage select has no picture for: drawn from their stage file instead (the ones on show).</summary>
        async void DrawMissing()
        {
            if (_drawingMissing) return;
            _drawingMissing = true;
            try
            {
                foreach (var tile in _tiles.Controls.OfType<PictureTile>().Where(t => t.Picture == null && ((SongPlace)t.Value).File != null).ToList())
                {
                    string f = ((SongPlace)tile.Value).File;
                    if (!_renders.TryGetValue(f, out var b))
                    {
                        try { b = await Task.Run(() => { using (var card = _installer.OpenCard()) { var bytes = card.ReadFile(f); return bytes == null ? null : ModelPreview.RenderStage(bytes, 320, 180); } }); }
                        catch { b = null; }
                        if (IsDisposed) return;
                        _renders[f] = b;
                    }
                    if (!tile.IsDisposed && b != null) tile.Picture = b;
                }
            }
            finally { _drawingMissing = false; }
        }

        /// <summary>Sets where the song picked on the left plays. Places where each song belongs to one fighter
        /// (victory themes) can only be replaced, so one is picked for it.</summary>
        void PickPlace(SongPlace p)
        {
            if (_row == null) return;
            if (_row.Place != p)
            {
                _row.Place = p; _row.Skip = false;
                // a song replaced before stays the choice only if it plays here too; victory themes can only be replaced
                if (_row.Replace != null && !p.Songs.Contains(_row.Replace)) _row.Replace = null;
                if (p.OnePerFighter && _row.Replace == null) _row.Replace = p.Songs.FirstOrDefault();
            }
            foreach (var t in _tileList) t.Selected = t.Value == p;
            ShowChoice(); Changed();
        }

        // ----- step 2: add or replace

        void ShowChoice()
        {
            var r = _row;
            if (r == null || r.Place == null || r.Skip)
            {
                _choice.Visible = r != null && r.Place != null && !r.Skip;
                return;
            }
            _filling = true;
            var p = r.Place;
            _choice.Visible = true;
            _step2.Text = $"2. Add it to {p.Title}'s songs, or replace one of them?";
            _add.Text = "Add it as a new song";
            _add.Enabled = !p.OnePerFighter;
            _addNote.Text = p.OnePerFighter ? "Not here: each of these songs belongs to one fighter, so pick the one to replace."
                : $"It joins the {p.Songs.Count} song{(p.Songs.Count == 1 ? "" : "s")} that can play on {p.Title}; P+'s stay. Uses {Space.MB(r.Size)} on the SD card.";
            _replace.Text = "Replace one of its songs";
            _replaceNote.Text = "This song plays instead of:";
            _replaceList.Items.Clear();
            foreach (var s in p.Songs) _replaceList.Items.Add(new SongPick { Song = s });
            _add.Checked = r.Replace == null && !p.OnePerFighter;
            _replace.Checked = r.Replace != null || p.OnePerFighter;
            _replaceList.Enabled = _replace.Checked;
            int i = r.Replace == null ? -1 : p.Songs.IndexOf(r.Replace);
            _replaceList.SelectedIndex = i;
            if (r.Replace != null && r.Replace.Lists.Count > 1) _replaceNote.Text = $"Plays instead of (it's also on {string.Join(", ", r.Replace.Lists.Where(l => !string.Equals(l, p.List, StringComparison.OrdinalIgnoreCase)).Take(2))}{(r.Replace.Lists.Count > 3 ? "…" : "")}):";
            _filling = false;
        }

        /// <summary>Gives every other song the same place, each replacing a different song where only replacing is
        /// allowed.</summary>
        void UseForAll()
        {
            if (_row?.Place == null) return;
            var p = _row.Place;
            foreach (var r in _rows.Where(x => x != _row && !x.Skip))
            {
                r.Place = p;
                if (r.Replace != null && !p.Songs.Contains(r.Replace)) r.Replace = null;
                if (p.OnePerFighter && r.Replace == null) r.Replace = p.Songs.FirstOrDefault(s => !_rows.Any(o => o != r && o.Replace == s)) ?? p.Songs.FirstOrDefault();
            }
            Changed();
        }

        // ----- the total, and installing

        void Changed()
        {
            _songList.Invalidate();
            if (_songs == null) return;
            long extra = 0; int n = 0, unplaced = 0;
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _rows.Where(x => !x.Skip))
            {
                if (r.Place == null) { unplaced++; continue; }
                n++;
                if (r.Replace == null) { extra += r.Size; continue; }   // a new file on the card
                used[r.Replace.CardPath] = used.TryGetValue(r.Replace.CardPath, out int k) ? k + 1 : 1;
                extra += r.Size - r.Replace.Size;   // it replaces what's there now (a song from the disc adds the whole file)
            }
            var clash = used.Where(kv => kv.Value > 1).Select(kv => _songs.First(s => s.CardPath == kv.Key).Title).FirstOrDefault();
            long room = _free - PPlusCard.MinFreeBytes;
            _summary.ForeColor = clash != null || extra > room ? Theme.Bad : unplaced > 0 ? Theme.Warn : Theme.Muted;
            _summary.Text = clash != null ? $"Two of your songs replace \"{clash}\". Pick a different one for one of them."
                : extra > room ? $"Not enough room on the P+ SD card: these need {Space.MB(extra)} more and there's {Space.MB(Math.Max(0, room))} left for mods. Leave some out, or turn off another mod."
                : n == 0 ? "Pick where each song plays."
                : (unplaced > 0 ? $"{unplaced} song{(unplaced == 1 ? "" : "s")} still need{(unplaced == 1 ? "s" : "")} a place (or leave {(unplaced == 1 ? "it" : "them")} out). " : "")
                  + $"{n} song{(n == 1 ? "" : "s")} ready, using {(extra >= 0 ? Space.MB(extra) + " more" : Space.MB(-extra) + " less")} space. Left for mods after: {Space.MB(Math.Max(0, room - extra))}.";
            _install.Text = n <= 1 ? "Install" : $"Install ({n})";
            _install.Enabled = n > 0 && unplaced == 0 && clash == null && extra <= room;
            foreach (var t in _tileList) t.Selected = _row != null && t.Value == _row.Place;
        }

        /// <summary>Checks every song with the installer and closes with the ones that passed; if some can't go on,
        /// says why and asks before going on without them.</summary>
        async void Install()
        {
            var picked = _rows.Where(r => !r.Skip && r.Place != null).Select(r => (r.File, Song: r.Replace, Place: r.Place)).ToList();
            string was = _install.Text; _install.Enabled = false; _install.Text = "Checking...";
            var plans = new List<StagePlan>(); var problems = new List<string>();
            await Task.Run(() =>
            {
                foreach (var (file, song, place) in picked)
                {
                    try
                    {
                        StagePlan plan;
                        if (song != null) plan = _installer.PlanSong(_pkg, file, song);
                        else using (var card = _installer.OpenCard()) plan = _installer.PlanNewSong(card, _pkg, file, place.TrackListFile, place.Title);
                        if (_pkg.SongFiles.Count == 1 && !string.IsNullOrEmpty(_pkg.Name)) plan.Name = _pkg.Name;
                        if (plan.CanInstall) plans.Add(plan); else problems.Add($"{Path.GetFileName(file)}: " + string.Join("; ", plan.Checks.Where(c => !c.Passed).Select(c => c.Title)));
                    }
                    catch (Exception e) { problems.Add($"{Path.GetFileName(file)}: {Friendly.Explain(e)}"); }
                }
            });
            if (IsDisposed) return;
            _install.Text = was; _install.Enabled = true;
            if (problems.Count > 0)
            {
                string text = "These can't be installed:\r\n\r\n" + string.Join("\r\n", problems.Take(12));
                if (plans.Count == 0) { Dialog.Show(this, text, "Can't install", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (Dialog.Show(this, text + $"\r\n\r\nInstall the other {plans.Count}?", "Some can't be installed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            Plans = plans;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
