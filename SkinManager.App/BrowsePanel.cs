// "Find skins online": search GameBanana's Brawl costumes, see what's inside a download, and
// install it in one click (it goes through the same steps and safety checks as "Add a skin").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class BrowsePanel : GlassPanel
    {
        public event Action<string, SkinSource> InstallRequested;   // downloaded file, where it came from
        public event Action<List<string>, SkinSource> InstallAllRequested;   // several downloaded files of one mod, added together
        public event Action<InstalledSkin> TurnOffRequested;

        readonly Func<Library> _lib;
        readonly string _dataFolder;
        readonly string _cache;

        readonly TextBox _search = new TextBox { Width = 280, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle };
        readonly ComboBox _sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Font = Theme.Body, FlatStyle = FlatStyle.Flat };
        readonly ComboBox _who = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Theme.Body, FlatStyle = FlatStyle.Flat };
        readonly ComboBox _stageCat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Theme.Body, FlatStyle = FlatStyle.Flat, Visible = false };
        readonly Button _kindSkins = Theme.Secondary("Skins");
        readonly Button _kindStages = Theme.Secondary("Stages");
        readonly Button _kindMusic = Theme.Secondary("Music");
        readonly Button _favButton = Theme.Secondary("★ Favorites");
        readonly CheckBox _hideInstalled = new CheckBox { Text = "Hide ones I have", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Body, BackColor = Color.Transparent };
        readonly CheckBox _hideOld = new CheckBox { Text = "Hide outdated", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Body, BackColor = Color.Transparent };
        bool _favMode;
        public Favorites Favs;
        readonly ComboBox _musicCat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Theme.Body, FlatStyle = FlatStyle.Flat, Visible = false };
        static readonly (string, int)[] MusicCategories = { ("All music", GameBanana.AllMusic), ("Stage & menu music", GameBanana.StageMusic), ("Victory themes", GameBanana.VictoryThemes) };
        bool _stageMode, _musicMode;
        string Model => _musicMode ? "Sound" : "Mod";
        static readonly (string, int)[] StageCategories = { ("1:1 stages (reskins)", GameBanana.OneToOneStages), ("Competitive stages", GameBanana.CompetitiveStages), ("All stages", GameBanana.AllStages) };
        readonly Button _go = Theme.Primary("Search");
        readonly Label _heading = Theme.Label("", Theme.Heading);
        readonly GlassFlow _results = new GlassFlow { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        readonly Panel _pager = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        readonly Button _prev = Theme.Secondary("< Previous");
        readonly Button _next = Theme.Secondary("Next >");
        readonly Label _pageText = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Panel _message = new Panel { Dock = DockStyle.Fill, Visible = false };
        readonly Label _messageText = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Button _retry = Theme.Secondary("Try again");
        readonly GlassPanel _details = new GlassPanel { Dock = DockStyle.Right, Width = 400, Tint = 215, TintColor = Theme.Panel, AutoScroll = true, Padding = new Padding(16, 14, 16, 14) };

        int _page = 1;
        int _generation;                 // bumps on every new search so late answers are dropped
        GbPage _current;
        long _shownMod;
        CancellationTokenSource _download;
        bool _loadedOnce;
        readonly SemaphoreSlim _pictureSlots = new SemaphoreSlim(4);

        public BrowsePanel(Func<Library> lib, string dataFolder)
        {
            _lib = lib; _dataFolder = dataFolder;
            _who.Items.Add("All characters");
            foreach (var c in Characters.All.OrderBy(c => c.Name)) _who.Items.Add(c);
            _who.SelectedIndex = 0;
            _who.BackColor = Theme.Panel; _who.ForeColor = Theme.Text;
            _cache = Path.Combine(dataFolder, "Cache");
            Dock = DockStyle.Fill;

            _sort.Items.AddRange(new object[] { "Most downloaded", "Most liked", "Newest" });
            _sort.SelectedIndex = 0;
            _sort.BackColor = Theme.Panel; _sort.ForeColor = Theme.Text;
            _search.BackColor = Theme.Panel; _search.ForeColor = Theme.Text;
            _search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NewSearch(); } };
            _go.Click += (s, e) => NewSearch();
            _sort.SelectedIndexChanged += (s, e) => { if (_loadedOnce) NewSearch(); };
            _who.SelectedIndexChanged += (s, e) => { if (_loadedOnce) NewSearch(); };
            foreach (var c in StageCategories) _stageCat.Items.Add(c.Item1);
            _stageCat.SelectedIndex = 0; _stageCat.BackColor = Theme.Panel; _stageCat.ForeColor = Theme.Text;
            _stageCat.SelectedIndexChanged += (s, e) => { if (_loadedOnce) NewSearch(); };
            _kindSkins.Click += (s, e) => SetMode(false);
            _kindStages.Click += (s, e) => SetMode(true);
            _kindMusic.Click += (s, e) => SetMode(false, music: true);
            foreach (var c in MusicCategories) _musicCat.Items.Add(c.Item1);
            _musicCat.SelectedIndex = 0; _musicCat.BackColor = Theme.Panel; _musicCat.ForeColor = Theme.Text;
            _musicCat.SelectedIndexChanged += (s, e) => { if (_loadedOnce) NewSearch(); };
            _prev.Click += (s, e) => { if (_page > 1) { _page--; Load(); } };
            _next.Click += (s, e) => { if (_current != null && _page < _current.Pages) { _page++; Load(); } };
            _retry.Click += (s, e) => Load();

            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 16, 8) };
            // row 1: what to look at; row 2: search; row 3: narrowing it down; then what's shown
            var bar = new Panel { Dock = DockStyle.Top, Height = 146 };
            _kindSkins.Location = new Point(0, 0);
            _kindSkins.Layout += (s, e) => { _kindStages.Left = _kindSkins.Right + 4; _kindMusic.Left = _kindStages.Right + 4; _favButton.Left = _kindMusic.Right + 16; };
            _kindStages.Location = new Point(70, 0); _kindMusic.Location = new Point(150, 0); _favButton.Location = new Point(240, 0);
            _search.Location = new Point(0, 44); _search.Width = 300;
            _go.Location = new Point(310, 40);
            _who.Location = new Point(0, 83); _stageCat.Location = new Point(0, 83);
            _musicCat.Location = new Point(0, 83);
            var sortLabel = Theme.Label("Sort:", Theme.Body, Theme.Muted); sortLabel.Location = new Point(212, 86);
            _sort.Location = new Point(256, 83);
            _hideInstalled.Location = new Point(0, 120); _hideOld.Location = new Point(150, 120);
            _heading.Visible = false;   // the page line under the results says what's shown
            bar.Controls.AddRange(new Control[] { _heading, _search, _go, _who, _stageCat, _musicCat, sortLabel, _sort, _kindSkins, _kindStages, _kindMusic, _favButton, _hideInstalled, _hideOld });
            _favButton.Click += (s, e) => { _favMode = !_favMode; Theme.Toggle(_favButton, _favMode); if (_favMode) ShowFavorites(); else NewSearch(); };
            _hideInstalled.CheckedChanged += (s, e) => { if (_favMode) ShowFavorites(); else if (_current != null) ShowPage(_current, _generation); };
            _hideOld.CheckedChanged += (s, e) => { if (_favMode) ShowFavorites(); else if (_current != null) ShowPage(_current, _generation); };
            SetMode(false, load: false);

            _pageText.AutoSize = true;
            _pager.Controls.AddRange(new Control[] { _prev, _pageText, _next });
            _pager.Resize += (s, e) => LayoutPager();

            _messageText.MaximumSize = new Size(440, 0);
            _messageText.Location = new Point(4, 20);
            _retry.Visible = false;
            _message.Controls.Add(_messageText); _message.Controls.Add(_retry);

            left.Controls.Add(_results); left.Controls.Add(_message); left.Controls.Add(_pager); left.Controls.Add(bar);
            Controls.Add(left); Controls.Add(_details);
            ShowNothingSelected();
            SetCue(_search, "Search GameBanana by name (optional)");
        }

        /// <summary>A label that wraps to a width and is exactly as tall as its text.</summary>
        static Label Wrapped(string text, Font font, Color color, int width)
        {
            var size = TextRenderer.MeasureText(text ?? "", font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false, Size = new Size(width, size.Height + 4), UseMnemonic = false };
        }

        static LinkLabel Link(string text, Color color, int width, Action click, string linkPart = null)
        {
            var size = TextRenderer.MeasureText(text, Theme.Small, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var l = new LinkLabel { Text = text, Font = Theme.Small, AutoSize = false, Size = new Size(width, size.Height + 4), LinkColor = color, ActiveLinkColor = color, VisitedLinkColor = color, BackColor = Color.Transparent, UseMnemonic = false };
            if (linkPart != null && text.IndexOf(linkPart) >= 0) l.LinkArea = new LinkArea(text.IndexOf(linkPart), linkPart.Length);
            l.LinkClicked += (s, e) => click();
            return l;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        static void SetCue(TextBox box, string text)
        {
            box.HandleCreated += (s, e) => { try { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); } catch { } };
        }

        void LayoutPager()
        {
            _prev.Location = new Point(0, (_pager.Height - _prev.Height) / 2);
            _pageText.Location = new Point(_prev.Right + 14, (_pager.Height - _pageText.Height) / 2);
            _next.Location = new Point(_pageText.Right + 14, (_pager.Height - _next.Height) / 2);
        }

        /// <summary>Show one character's costumes (from another page).</summary>
        public void ShowCharacter(Character c) { _who.SelectedItem = c; if (!_loadedOnce) NewSearch(); }

        public void ShownFirstTime() { if (!_loadedOnce) NewSearch(); }

        public void RefreshInstalled()
        {
            foreach (Control c in _results.Controls) if (c is ModCard card) { card.Installed = IsInstalled(card.Mod.Id); card.Invalidate(); }
            if (_shownMod != 0) ShowDetails(_shownMod, keepScroll: true);
        }

        bool IsInstalled(long modId) => (_lib()?.Skins.Any(s => s.GameBananaModId == modId) ?? false) || (_lib()?.Stages.Any(s => s.GameBananaModId == modId) ?? false);

        void SetMode(bool stages, bool load = true, bool music = false)
        {
            _stageMode = stages; _musicMode = music;
            _musicCat.Visible = music; Theme.Toggle(_kindMusic, music);
            if (music) { _who.Visible = false; _stageCat.Visible = false; Theme.Toggle(_kindSkins, false); Theme.Toggle(_kindStages, false); if (load && _loadedOnce) NewSearch(); return; }
            _who.Visible = !stages; _stageCat.Visible = stages;
            Theme.Toggle(_kindSkins, !stages); Theme.Toggle(_kindStages, stages);
            if (load && _loadedOnce) NewSearch();
        }

        public void ShowStages() { SetMode(true, load: false); NewSearch(); }

        Character Wanted => _stageMode || _musicMode ? null : _who.SelectedItem as Character;

        void NewSearch() { _page = 1; if (_favMode) { _favMode = false; Theme.Toggle(_favButton, false); } Load(); }

        async void Load()
        {
            _loadedOnce = true;
            int gen = ++_generation;
            var who = Wanted;
            int category = _musicMode ? MusicCategories[Math.Max(0, _musicCat.SelectedIndex)].Item2 : _stageMode ? StageCategories[Math.Max(0, _stageCat.SelectedIndex)].Item2 : who == null ? GameBanana.AllCostumes : GameBanana.CategoryFor(who);
            string search = _search.Text;
            var sort = (GbSort)Math.Max(0, _sort.SelectedIndex);
            int page = _page;
            _heading.Text = _musicMode ? "Brawl music on GameBanana" : _stageMode ? "Brawl stages on GameBanana" : who == null ? "Brawl costumes on GameBanana (all characters)" : $"{who.Name} costumes on GameBanana";
            ShowMessage("Loading from GameBanana...", false);
            _prev.Enabled = _next.Enabled = false;
            GbPage result;
            string model = Model;
            try { result = await Task.Run(() => GameBanana.List(category, search, sort, page, model)); }
            catch (Exception e)
            {
                if (gen != _generation) return;
                Program.Trace("list failed: " + e);
                ShowMessage(e is GbException ? e.Message : "Something went wrong: " + e.Message, true);
                return;
            }
            if (gen != _generation || IsDisposed) return;
            _current = result;
            ShowPage(result, gen);
        }

        /// <summary>A page of results, with the "hide" switches applied.</summary>
        void ShowPage(GbPage result, int gen)
        {
            var who = Wanted;
            string search = _search.Text;
            foreach (Control old in _results.Controls) old.Dispose();
            _results.Controls.Clear();
            var mods = result.Mods.Where(m => !(_hideInstalled.Checked && IsInstalled(m.Id)) && !(_hideOld.Checked && m.Obsolete)).ToList();
            if (mods.Count == 0)
            {
                ShowMessage(result.Mods.Count > 0 ? "Everything on this page is hidden by the switches above. Try the next page, or untick them."
                    : string.IsNullOrWhiteSpace(search) ? "Nothing here yet." : $"Nothing with \"{search.Trim()}\" in the name." + (who != null ? " Try \"All characters\"." : ""), false);
            }
            else
            {
                _message.Visible = false; _results.Visible = true;
                _results.SuspendLayout();
                foreach (var m in mods) _results.Controls.Add(Card(m, gen, who == null || _stageMode || _musicMode));
                _results.ResumeLayout();
                _results.AutoScrollPosition = new Point(0, 0);
            }
            _pager.Visible = true;
            _pageText.Text = result.Total == 0 ? "" : $"Page {result.Page} of {result.Pages}   ({result.Total} in all)";
            _prev.Enabled = result.Page > 1; _next.Enabled = result.Page < result.Pages;
            LayoutPager();
        }

        string KindNow => _musicMode ? "music" : _stageMode ? "stage" : "skin";

        ModCard Card(GbMod m, int gen, bool showCategory)
        {
            var card = new ModCard { Mod = m, Installed = IsInstalled(m.Id), ShowCategory = showCategory, Margin = new Padding(0, 0, 12, 12), Favorite = Favs?.Has("gb:" + m.Id) == true };
            card.Click += (s, e) => { foreach (Control c in _results.Controls) if (c is ModCard mc) { mc.Selected = mc == card; mc.Invalidate(); } ShowDetails(m.Id); };
            card.FavoriteClicked += () =>
            {
                card.Favorite = Favs?.Toggle(new Favorite { Key = "gb:" + m.Id, Name = m.Name, Author = m.Author, ThumbUrl = m.ThumbUrl, ModId = m.Id, Category = m.Category, Kind = KindNow }) == true;
                card.Invalidate();
                if (_favMode && !card.Favorite) ShowFavorites();
            };
            LoadPicture(m.HasContentRatings ? null : m.ThumbUrl, gen, img => card.Picture = img, card);
            return card;
        }

        /// <summary>Starred mods of the kind shown (Skins, Stages or Music); they come from the list on this PC, so no waiting.</summary>
        void ShowFavorites()
        {
            int gen = ++_generation;
            _heading.Text = "Your favorites";
            foreach (Control old in _results.Controls) old.Dispose();
            _results.Controls.Clear();
            var favs = (Favs?.All ?? Enumerable.Empty<Favorite>()).Where(f => f.ModId != 0 && (f.Kind ?? "skin") == KindNow)
                .Where(f => !(_hideInstalled.Checked && IsInstalled(f.ModId))).ToList();
            _pager.Visible = false;
            if (favs.Count == 0)
            {
                ShowMessage($"No favorite {(KindNow == "music" ? "songs" : KindNow + "s")} yet. Hover over one and click the ☆ in its corner to star it.", false);
                return;
            }
            _message.Visible = false; _results.Visible = true;
            _results.SuspendLayout();
            foreach (var f in favs)
                _results.Controls.Add(Card(new GbMod { Id = f.ModId, Name = f.Name, Author = f.Author, ThumbUrl = f.ThumbUrl, Category = f.Category ?? "" }, gen, true));
            _results.ResumeLayout();
        }

        void ShowMessage(string text, bool canRetry)
        {
            foreach (Control old in _results.Controls) old.Dispose();
            _results.Controls.Clear();
            _results.Visible = false;
            _messageText.AutoSize = false;
            _messageText.Size = new Size(440, TextRenderer.MeasureText(text, Theme.Body, new Size(440, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 4);
            _messageText.Text = text; _messageText.ForeColor = canRetry ? Theme.Bad : Theme.Muted;
            _retry.Visible = canRetry; _retry.Location = new Point(4, _messageText.Bottom + 14);
            _message.Visible = true;
        }

        async void LoadPicture(string url, int gen, Action<Image> set, Control owner)
        {
            if (string.IsNullOrEmpty(url)) return;
            await _pictureSlots.WaitAsync();
            byte[] bytes;
            try { bytes = await Task.Run(() => GameBanana.Picture(url, _cache)); }
            finally { _pictureSlots.Release(); }
            if (bytes == null || owner.IsDisposed || (gen != 0 && gen != _generation)) return;
            try
            {
                using (var ms = new MemoryStream(bytes)) using (var img = Image.FromStream(ms)) set(new Bitmap(img));
                owner.Invalidate();
            }
            catch { }   // a picture format Windows can't read (.webp): the card keeps its placeholder
        }

        // ----- the details panel on the right -----

        void ShowNothingSelected()
        {
            ClearDetails();
            int w = _details.ClientSize.Width - 40;
            var t = Wrapped("Pick a costume to see what's in it.", Theme.Body, Theme.Muted, w);
            t.Location = new Point(16, 18);
            var t2 = Wrapped("Every download goes through the same safety checks as \"Add a skin\", so anything that could break online play is refused before it touches your SD card.", Theme.Small, Theme.Muted, w);
            t2.Location = new Point(16, t.Bottom + 10);
            _details.Controls.Add(t); _details.Controls.Add(t2);
        }

        void ClearDetails()
        {
            _details.SuspendLayout();
            foreach (Control c in _details.Controls.Cast<Control>().ToList()) c.Dispose();
            _details.Controls.Clear();
            _details.ResumeLayout();
        }

        int _detailsGen;

        async void ShowDetails(long modId, bool keepScroll = false)
        {
            int gen = ++_detailsGen;
            _shownMod = modId;
            if (!keepScroll)
            {
                ClearDetails();
                var wait = Theme.Label("Loading...", Theme.Body, Theme.Muted); wait.Location = new Point(16, 18);
                _details.Controls.Add(wait);
            }
            GbDetails d;
            string model = Model;
            try { d = await Task.Run(() => GameBanana.Details(modId, model)); }
            catch (Exception e)
            {
                if (gen != _detailsGen) return;
                ClearDetails();
                var err = Wrapped(e is GbException ? e.Message : "Something went wrong: " + e.Message, Theme.Body, Theme.Bad, _details.ClientSize.Width - 40);
                err.Location = new Point(16, 18);
                var again = Theme.Secondary("Try again"); again.Location = new Point(16, err.Bottom + 12);
                again.Click += (s, ev) => ShowDetails(modId);
                _details.Controls.Add(err); _details.Controls.Add(again);
                return;
            }
            if (gen != _detailsGen || IsDisposed) return;
            BuildDetails(d);
        }

        void BuildDetails(GbDetails d)
        {
            ClearDetails();
            _details.SuspendLayout();
            int w = _details.ClientSize.Width - 32 - SystemInformation.VerticalScrollBarWidth;
            int y = 14;
            Control Add(Control c, int gap = 8) { c.Location = new Point(16, y); _details.Controls.Add(c); y = c.Bottom + gap; return c; }
            Label Line(string text, Font f, Color color, int gap = 6) { var l = Wrapped(text, f, color, w); Add(l, gap); return l; }

            var pic = new PictureBox { Size = new Size(w, w * 3 / 4), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back };
            Add(pic, 6);
            var caption = Wrapped("", Theme.Small, Theme.Muted, w); caption.Height = 34;
            if (!d.Mod.HasContentRatings)
            {
                LoadPicture(d.Mod.BigImageUrl, 0, img => pic.Image = img, pic);
                if (d.Mod.Shots.Count > 1)
                {
                    // every preview on its GameBanana page: click one to see it big
                    var strip = new FlowLayoutPanel { Width = w, Height = 10, WrapContents = true, BackColor = Color.Transparent };
                    PictureBox selected = null;
                    foreach (var shot in d.Mod.Shots.Take(30))
                    {
                        var thumb = new PictureBox { Size = new Size(72, 50), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back, Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand };
                        var big = shot.Big; var text = shot.Caption;
                        thumb.Paint += (s, e) => { if (thumb == selected) using (var pen = new Pen(Theme.Accent, 2)) e.Graphics.DrawRectangle(pen, 1, 1, thumb.Width - 3, thumb.Height - 3); };
                        thumb.Click += (s, e) =>
                        {
                            var old = selected; selected = thumb; old?.Invalidate(); thumb.Invalidate();
                            LoadPicture(big, 0, img => pic.Image = img, pic);
                            caption.Text = text ?? "";
                        };
                        strip.Controls.Add(thumb);
                        LoadPicture(shot.Small, 0, img => thumb.Image = img, thumb);
                    }
                    int rows = (d.Mod.Shots.Take(30).Count() + (w / 78) - 1) / Math.Max(1, w / 78);
                    strip.Height = rows * 56;
                    Add(strip, 2);
                    Add(caption, 6);
                }
            }
            else { pic.Paint += (s, e) => TextRenderer.DrawText(e.Graphics, "Preview hidden (content warning on GameBanana)", Theme.Small, pic.ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); }

            Line(d.Mod.Name, Theme.Heading, Theme.Text, 2);
            Line($"by {d.Mod.Author}  •  {d.Mod.Category}", Theme.Small, Theme.Muted, 2);
            Line($"{d.Downloads:N0} downloads  •  {d.Mod.Likes:N0} likes  •  added {d.Mod.Added:d}" + (d.Mod.Modified > d.Mod.Added.AddDays(1) ? $", updated {d.Mod.Modified:d}" : ""), Theme.Small, Theme.Muted, 6);
            Add(Link("View on GameBanana", Theme.Accent, w, () => OpenUrl(d.Mod.ProfileUrl)), 6);
            if (!string.IsNullOrEmpty(d.Mod.AudioUrl)) Add(Link("▶  Listen to a preview (opens in your web browser)", Theme.Accent, w, () => OpenUrl(d.Mod.AudioUrl)), 10);
            if (d.Mod.Obsolete) Line("The author marked this as outdated.", Theme.Small, Theme.Warn);

            // where it's installed now
            var mine = _lib()?.Skins.Where(s => s.GameBananaModId == d.Mod.Id).ToList() ?? new List<InstalledSkin>();
            foreach (var skin in mine)
            {
                var row = new Panel { Size = new Size(w, 30) };
                var l = Theme.Label(skin.Enabled ? $"On: {skin.Character.Name}'s costume {skin.SlotIndex + 1}" : $"In My Mods (off): {skin.Character.Name}'s costume {skin.SlotIndex + 1}", Theme.BodyBold, skin.Enabled ? Theme.Good : Theme.Muted);
                l.Location = new Point(0, 6);
                row.Controls.Add(l);
                if (skin.Enabled)
                {
                    var rm = Link("Turn off", Theme.Bad, 60, () => TurnOffRequested?.Invoke(skin));
                    rm.Location = new Point(w - 60, 7);
                    row.Controls.Add(rm);
                }
                Add(row, 2);
            }
            foreach (var st in _lib()?.Stages.Where(x => x.GameBananaModId == d.Mod.Id).ToList() ?? new List<InstalledStage>())
            {
                var l = Theme.Label((st.Enabled ? "On: " : "In My Mods (off): ") + st.Target.Label, Theme.BodyBold, st.Enabled ? Theme.Good : Theme.Muted);
                Add(l, 4);
            }
            if (mine.Count > 0) y += 8;

            Line("DOWNLOADS", Theme.Small, Theme.Muted, 6);
            if (d.Withheld) Line("GameBanana has taken this one down for now.", Theme.Body, Theme.Bad);
            if (d.Files.Count == 0 && d.OtherSites.Count == 0) Line("This page has no files to download.", Theme.Body, Theme.Muted);
            var wanted = Wanted;
            var several = d.Files.Where(f => { var k = GameBanana.Judge(f, wanted, d.OtherSites); return (k.Kind == GbVerdictKind.Good || k.Kind == GbVerdictKind.Maybe) && k.Costumes.Count > 0; }).ToList();
            if (several.Count > 1) Add(AllFilesRow(d, several, w), 10);
            foreach (var f in d.Files.OrderBy(f => VerdictOrder(GameBanana.Judge(f, wanted, d.OtherSites).Kind)))
                Add(FileRow(d, f, GameBanana.Judge(f, wanted, d.OtherSites), w), 10);
            foreach (var other in d.OtherSites)
            {
                var url = other.Key;
                Add(Link($"Also on {other.Value} (opens your web browser)", Theme.Accent, w, () => OpenUrl(url)), 4);
            }
            if (d.OtherSites.Count > 0)
                Line("Files from other sites can't be installed in one click: download it there, then drop it on this window.", Theme.Small, Theme.Muted, 12);

            if (!string.IsNullOrWhiteSpace(d.Text))
            {
                y += 6;
                Line("ABOUT", Theme.Small, Theme.Muted, 6);
                string about = d.Text.Length > 2500 ? d.Text.Substring(0, 2500) + "…" : d.Text;
                Line(about.Replace("\n", "\r\n"), Theme.Small, Theme.Text, 10);
            }
            if (!string.IsNullOrWhiteSpace(d.License)) Line("License: " + (d.License.Length > 400 ? d.License.Substring(0, 400) + "…" : d.License), Theme.Small, Theme.Muted, 10);
            Line("Made by the people credited on its GameBanana page. This app only downloads it for you.", Theme.Small, Theme.Muted, 16);
            _details.ResumeLayout();
        }

        /// <summary>Mods that put each color in its own download: get them all and pick from one list.</summary>
        Control AllFilesRow(GbDetails d, List<GbFile> files, int w)
        {
            var box = new Panel { Width = w, BackColor = Theme.Back, Padding = new Padding(10) };
            var title = Wrapped($"Get all {files.Count} costume downloads at once", Theme.BodyBold, Theme.Text, w - 20); title.Location = new Point(10, 10); box.Controls.Add(title);
            var info = Wrapped($"{SizeText(files.Sum(f => f.Size))} in total. You'll see every costume in one list, all ticked, and each goes into its own slot.", Theme.Small, Theme.Muted, w - 20);
            info.Location = new Point(10, title.Bottom + 2); box.Controls.Add(info);
            int y = info.Bottom + 4;
            var button = Theme.Primary("Install all"); button.Location = new Point(10, y + 4);
            var bar = new ProgressBar { Location = new Point(10, y + 8), Size = new Size(w - 130, 14), Visible = false, Maximum = 1000 };
            var status = Theme.Label("", Theme.Small, Theme.Muted); status.Location = new Point(10, y + 26); status.Visible = false;
            var cancel = Theme.Secondary("Cancel"); cancel.Visible = false; cancel.Location = new Point(w - 110, y + 2);
            cancel.Click += (s, e) => _download?.Cancel();
            button.Click += async (s, e) =>
            {
                if (_download != null) { MessageBox.Show(FindForm(), "Another download is still going. Wait for it to finish first.", "One at a time", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                button.Visible = false; bar.Visible = status.Visible = cancel.Visible = true; bar.Value = 0;
                var cts = _download = new CancellationTokenSource();
                var paths = new List<string>(); Exception error = null;
                long before = 0, all = Math.Max(1, files.Sum(f => f.Size));
                try
                {
                    var folder = GameBanana.DownloadFolder(_dataFolder, d.Mod.Id);
                    for (int i = 0; i < files.Count; i++)
                    {
                        var f = files[i]; int at = i;
                        string path = await Task.Run(() => GameBanana.Download(f, folder, (done, total) =>
                        {
                            try
                            {
                                BeginInvoke((Action)(() =>
                                {
                                    if (bar.IsDisposed) return;
                                    bar.Value = (int)Math.Min(1000, (before + done) * 1000 / all);
                                    status.Text = $"Downloading {at + 1} of {files.Count}... {SizeText(before + done)} of {SizeText(all)}";
                                }));
                            }
                            catch (InvalidOperationException) { }
                        }, cts.Token), cts.Token);
                        before += f.Size;
                        paths.Add(path);
                        try
                        {
                            new DownloadInfo { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, Category = d.Mod.Category,
                                ThumbUrl = d.Mod.ThumbUrl, PageUrl = d.Mod.ProfileUrl, FileName = f.Name, Path = path, DownloadedAt = DateTime.Now }.Save();
                        }
                        catch (Exception ex) { Program.Trace("download info not saved: " + ex.Message); }
                    }
                }
                catch (Exception ex) { error = ex; }
                finally { _download = null; cts.Dispose(); }
                if (IsDisposed) return;
                if (!box.IsDisposed) { button.Visible = true; bar.Visible = status.Visible = cancel.Visible = false; }
                if (error is OperationCanceledException) return;
                if (error != null)
                {
                    Program.Trace("download failed: " + error);
                    MessageBox.Show(FindForm(), error is GbException ? error.Message : "The download failed: " + error.Message, "Couldn't download", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                InstallAllRequested?.Invoke(paths, new SkinSource { ModId = d.Mod.Id, FileId = 0, ModName = d.Mod.Name, Author = d.Mod.Author, PageUrl = d.Mod.ProfileUrl, ThumbUrl = d.Mod.ThumbUrl });
            };
            box.Controls.Add(button); box.Controls.Add(bar); box.Controls.Add(status); box.Controls.Add(cancel);
            box.Height = Math.Max(button.Bottom, status.Bottom) + 12;
            return box;
        }

        static int VerdictOrder(GbVerdictKind k) => k == GbVerdictKind.Good ? 0 : k == GbVerdictKind.Maybe ? 1 : k == GbVerdictKind.OtherCharacter ? 2 : 3;

        Control FileRow(GbDetails d, GbFile f, GbVerdict v, int w)
        {
            var box = new Panel { Width = w, BackColor = Theme.Back, Padding = new Padding(10) };
            int y = 10;
            var name = Wrapped(f.Name, Theme.BodyBold, Theme.Text, w - 20); name.Location = new Point(10, y); box.Controls.Add(name); y = name.Bottom + 2;
            var info = Wrapped($"{SizeText(f.Size)}  •  {f.Added:d}" + (f.VirusScan == "clean" ? "  •  virus scan: clean" : string.IsNullOrEmpty(f.VirusScan) ? "" : "  •  virus scan: " + f.VirusScan), Theme.Small, Theme.Muted, w - 20);
            info.Location = new Point(10, y); box.Controls.Add(info); y = info.Bottom + 4;
            Color vc = v.Kind == GbVerdictKind.Good ? Theme.Good : v.Kind == GbVerdictKind.Maybe ? Theme.Muted : v.Kind == GbVerdictKind.OtherCharacter ? Theme.Warn : Theme.Bad;
            var verdict = Wrapped((v.Kind == GbVerdictKind.Good ? "✓ " : "") + v.Text, Theme.Small, vc, w - 20);
            verdict.Location = new Point(10, y); box.Controls.Add(verdict); y = verdict.Bottom + 4;
            if (v.Costumes.Count > 0 && v.Costumes.Any(c => c.MadeForPPlus) && v.Costumes.Any(c => !c.MadeForPPlus))
            {
                var both = Wrapped("Has a P+ version and another version. You'll pick one next (the P+ one is first).", Theme.Small, Theme.Muted, w - 20);
                both.Location = new Point(10, y); box.Controls.Add(both); y = both.Bottom + 4;
            }
            if ((f.Extension == ".rar" || f.Extension == ".7z") && !Archives.HasExtractor && v.Kind != GbVerdictKind.OtherSite)
            {
                var need = Link($"This is a {f.Extension} file. If it won't open, install 7-Zip (free) and try again.", Theme.Accent, w - 20, () => OpenUrl("https://www.7-zip.org/"), "install 7-Zip");
                need.Location = new Point(10, y); box.Controls.Add(need); y = need.Bottom + 4;
            }
            bool canTry = v.Kind == GbVerdictKind.Good || v.Kind == GbVerdictKind.Maybe || v.Kind == GbVerdictKind.OtherCharacter;
            var button = canTry ? Theme.Primary("Install") : Theme.Secondary("Try anyway");
            if (v.Kind == GbVerdictKind.OtherSite) button.Visible = false;
            if (v.Kind == GbVerdictKind.OtherCharacter) button.Text = "Install anyway";
            button.Location = new Point(10, y + 4);
            var bar = new ProgressBar { Location = new Point(10, y + 8), Size = new Size(w - 130, 14), Visible = false, Maximum = 1000 };
            var status = Theme.Label("", Theme.Small, Theme.Muted); status.Location = new Point(10, y + 26); status.Visible = false;
            var cancel = Theme.Secondary("Cancel"); cancel.Visible = false; cancel.Location = new Point(w - 110, y + 2);
            cancel.Click += (s, e) => _download?.Cancel();
            button.Click += async (s, e) =>
            {
                if (_download != null) { MessageBox.Show(FindForm(), "Another download is still going. Wait for it to finish first.", "One at a time", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                button.Visible = false; bar.Visible = status.Visible = cancel.Visible = true; bar.Value = 0;
                status.Text = "Starting download...";
                var cts = _download = new CancellationTokenSource();
                string path = null; Exception error = null;
                try
                {
                    var folder = GameBanana.DownloadFolder(_dataFolder, d.Mod.Id);
                    path = await Task.Run(() => GameBanana.Download(f, folder, (done, total) =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (bar.IsDisposed) return;
                                bar.Value = total > 0 ? (int)Math.Min(1000, done * 1000 / total) : 0;
                                status.Text = total > 0 ? $"Downloading... {SizeText(done)} of {SizeText(total)}" : $"Downloading... {SizeText(done)}";
                            }));
                        }
                        catch (InvalidOperationException) { }
                    }, cts.Token), cts.Token);
                }
                catch (Exception ex) { error = ex; }
                finally { _download = null; cts.Dispose(); }
                if (IsDisposed) return;
                if (!box.IsDisposed) { button.Visible = v.Kind != GbVerdictKind.OtherSite; bar.Visible = status.Visible = cancel.Visible = false; }
                if (error is OperationCanceledException) return;
                if (error != null)
                {
                    Program.Trace("download failed: " + error);
                    MessageBox.Show(FindForm(), error is GbException ? error.Message : "The download failed: " + error.Message, "Couldn't download", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var source = new SkinSource { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, PageUrl = d.Mod.ProfileUrl, ThumbUrl = d.Mod.ThumbUrl };
                try
                {
                    new DownloadInfo { ModId = d.Mod.Id, FileId = f.Id, ModName = d.Mod.Name, Author = d.Mod.Author, Category = d.Mod.Category,
                        ThumbUrl = d.Mod.ThumbUrl, PageUrl = d.Mod.ProfileUrl, FileName = f.Name, Path = path, DownloadedAt = DateTime.Now }.Save();
                }
                catch (Exception ex) { Program.Trace("download info not saved: " + ex.Message); }
                InstallRequested?.Invoke(path, source);
            };
            box.Controls.Add(button); box.Controls.Add(bar); box.Controls.Add(status); box.Controls.Add(cancel);
            box.Height = (button.Visible ? button.Bottom : y) + 12;
            if (box.Height < status.Bottom + 10) box.Height = status.Bottom + 10;
            return box;
        }

        static string SizeText(long b) => b >= 1024 * 1024 ? $"{b / (1024.0 * 1024):0.0} MB" : $"{Math.Max(1, b / 1024)} KB";

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("https://") || url.StartsWith("http://"))) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    /// <summary>One GameBanana result: picture, name, author.</summary>
    class ModCard : Control
    {
        public GbMod Mod;
        public Image Picture;
        public bool Installed, Selected, ShowCategory, Favorite;
        public event Action FavoriteClicked;
        bool _hover, _starHover, _starDown;
        Rectangle _star;

        public ModCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(218, 212); Cursor = Cursors.Hand; BackColor = Color.Transparent;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _starHover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool s = _star.Contains(e.Location);
            if (s != _starHover) { _starHover = s; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { _starDown = _star.Contains(e.Location); base.OnMouseDown(e); }
        protected override void OnClick(EventArgs e)
        {
            if (_starDown) { _starDown = false; FavoriteClicked?.Invoke(); return; }   // the star doesn't open the mod
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(2, 1, Width - 5, Height - 6);
            Theme.CardFrame(g, card, _hover, Selected);
            var pic = new Rectangle(card.X + 1, card.Y + 1, card.Width - 1, 116);
            using (var area = Theme.PictureArea(g, pic))
            {
                var clip = g.Clip; g.SetClip(area, CombineMode.Intersect);
                if (Picture != null)
                {
                    // fill the frame (pictures are screenshots): crop rather than letterbox
                    float scale = Math.Max((float)pic.Width / Picture.Width, (float)pic.Height / Picture.Height);
                    int pw = (int)(Picture.Width * scale), ph = (int)(Picture.Height * scale);
                    var small = Theme.Scaled(Picture, pw, ph);
                    Theme.Blit(g, small, pic.X + (pic.Width - pw) / 2, pic.Y + (pic.Height - ph) / 2);
                }
                else if (Mod != null && !string.IsNullOrEmpty(Mod.AudioUrl))
                    using (var f = new Font("Segoe UI Symbol", 34f)) TextRenderer.DrawText(g, "♫", f, pic, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                else TextRenderer.DrawText(g, Mod != null && Mod.HasContentRatings ? "preview hidden" : "no preview", Theme.Small, pic, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                // a soft shade at the bottom of the picture
                var fade = new Rectangle(pic.X, pic.Bottom - 30, pic.Width, 31);
                using (var b = new LinearGradientBrush(fade, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(110, 10, 11, 15), LinearGradientMode.Vertical)) g.FillRectangle(b, fade);
                g.Clip = clip;
            }
            using (var b = new SolidBrush(Installed ? Theme.Accent : Color.FromArgb(50, 255, 255, 255))) g.FillRectangle(b, pic.X, pic.Bottom, pic.Width, Installed ? 3 : 1);
            if (Installed) Theme.Pill(g, new Point(pic.X + 7, pic.Y + 7), "Installed", Theme.Accent, Theme.AccentText);
            _star = new Rectangle(pic.Right - 32, pic.Y + 6, 26, 26);
            if (Favorite || _hover) Theme.Star(g, _star, Favorite, _starHover);
            if (Mod == null) return;
            TextRenderer.DrawText(g, Mod.Name, Theme.BodyBold, new Rectangle(10, pic.Bottom + 8, Width - 20, 38), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            string line = ShowCategory ? (Mod.Category ?? "").Replace(" Costumes", "") : "by " + Mod.Author;
            TextRenderer.DrawText(g, line, Theme.Small, new Rectangle(10, pic.Bottom + 46, Width - 20, 18), Theme.Muted, TextFormatFlags.EndEllipsis);
            if (Mod.Likes > 0 || Mod.Views > 0 || Mod.Obsolete)
                TextRenderer.DrawText(g, $"♥ {Mod.Likes:N0}   •   {Mod.Views:N0} views" + (Mod.Obsolete ? "   •  outdated" : ""), Theme.Small, new Rectangle(10, pic.Bottom + 64, Width - 20, 18), Mod.Obsolete ? Theme.Warn : Theme.Muted, TextFormatFlags.EndEllipsis);
        }
    }
}
