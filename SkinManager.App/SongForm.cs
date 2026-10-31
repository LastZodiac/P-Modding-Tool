// Adding music: each song file in the download replaces one of P+'s songs. The app suggests the song
// with the same file name (packs made for Brawl use names like X02.brstm); otherwise you pick one.
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
    class SongForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;
        readonly SkinPackage _pkg;
        public List<StagePlan> Plans { get; private set; } = new List<StagePlan>();

        sealed class Row { public string File; public long Size; public ComboBox Pick; public Label Where; public bool SameName; public bool Auto = true; }

        readonly List<Row> _rows = new List<Row>();
        readonly Button _install = Theme.Primary("Install");
        readonly Label _summary = Theme.Label("", Theme.Body, Theme.Muted);
        readonly FlowLayoutPanel _list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 4, 24, 4) };
        Action _layoutFoot = () => { };
        List<SongInfo> _songs;
        long _free;
        bool _filling;
        readonly IList<string> _stages;
        readonly ComboBox _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380, BackColor = Theme.Back, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, MaxDropDownItems = 24 };
        const string Every = "Every song in P+";

        sealed class Item
        {
            public SongInfo Song;    // replace this P+ song
            public string AddTo;     // or: add it as a new song to this stage's list
            public override string ToString() => Song?.Label ?? (AddTo != null ? "+  Add it as a new song on " + AddTo + " (P+'s songs stay)" : "Don't add this one");
        }

        /// <param name="stages">stage files (card paths or names) this music came with; their stage's songs are suggested</param>
        public SongForm(Installer installer, Library lib, SkinPackage pkg, IList<string> stages = null)
        {
            _installer = installer; _lib = lib; _pkg = pkg; _stages = stages ?? pkg.StageFiles;
            Text = "Add music: " + pkg.Name;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1000, 720); MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var head = new Panel { Dock = DockStyle.Top, Height = 132 };
            var title = Theme.Label(pkg.SongFiles.Count == 1 ? "Where should this song go?" : $"This download has {pkg.SongFiles.Count} songs", Theme.Title);
            title.Location = new Point(24, 14);
            var hint = new Label
            {
                Text = "Pick the stage these songs are for. Each one is added to that stage's songs (P+'s stay), or you can pick a P+ song for it to replace instead, which takes less room on the SD card. Music is only what you hear: it doesn't change the game, and in rooms you host, players get your songs too.",
                Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(26, 50), Size = new Size(940, 44), BackColor = Color.Transparent,
            };
            var showFor = Theme.Label("Stage:", Theme.Body, Theme.Muted); showFor.Location = new Point(26, 100);
            _filter.Location = new Point(84, 96);
            _filter.SelectedIndexChanged += (s, e) => { if (!_filling) Assign(); };
            head.Controls.Add(title); head.Controls.Add(hint); head.Controls.Add(showFor); head.Controls.Add(_filter);
            head.Resize += (s, e) => hint.Width = head.Width - 50;

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Panel };
            _summary.Location = new Point(24, 22); _summary.AutoSize = false; _summary.Size = new Size(620, 22);
            var cancel = Theme.Secondary("Cancel");
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _install.Click += (s, e) => Install();
            foot.Controls.AddRange(new Control[] { _summary, cancel, _install });
            _layoutFoot = () =>
            {
                _install.Location = new Point(foot.Width - _install.Width - 24, (foot.Height - _install.Height) / 2);
                cancel.Location = new Point(_install.Left - cancel.Width - 10, (foot.Height - cancel.Height) / 2);
            };
            foot.Resize += (s, e) => _layoutFoot();
            _install.SizeChanged += (s, e) => _layoutFoot();
            Controls.Add(_list); Controls.Add(foot); Controls.Add(head);
            _list.Resize += (s, e) => { foreach (Control c in _list.Controls) c.Width = Math.Max(600, _list.ClientSize.Width - 50); };
            _summary.Text = "Reading P+'s track lists...";
            _install.Enabled = false;
            Shown += async (s, e) => await Fill();
        }

        async Task Fill()
        {
            Dictionary<string, string> map;
            try
            {
                var r = await Task.Run(() => { using (var card = _installer.OpenCard()) return (Songs.All(card), card.FreeBytes, Songs.StageTrackLists(card)); });
                _songs = r.Item1; _free = r.Item2; map = r.Item3;
            }
            catch (Exception e) { _summary.Text = "Couldn't read P+'s track lists: " + Friendly.Explain(e); _summary.ForeColor = Theme.Bad; return; }
            if (IsDisposed) return;
            _filling = true;
            // which stage is this music for? the stage files it came with, else a stage named in the download's name
            var lists = _songs.SelectMany(x => x.Lists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            string For(string l) => l == null ? null : lists.FirstOrDefault(x => string.Equals(x, l, StringComparison.OrdinalIgnoreCase));
            string pick = _stages.Select(f => For(Songs.TrackListOf(map, f))).FirstOrDefault(x => x != null)
                ?? Songs.GuessTrackList(lists, new[] { _pkg.Name, Path.GetFileName(_pkg.Folder ?? "") }.Concat(_pkg.SongFiles.Select(f => Path.GetFileName(Path.GetDirectoryName(f)))).ToArray());
            _filter.Items.Add(Every);
            foreach (var l in lists) _filter.Items.Add(l);
            _filter.SelectedItem = pick ?? Every;
            if (_pkg.SongFiles.Count > 120)
            {
                var more = Theme.Label($"This download has {_pkg.SongFiles.Count} songs: the first 120 are shown here. Add the rest by opening it again after this.", Theme.Small, Theme.Warn);
                more.Margin = new Padding(0, 0, 0, 8); _list.Controls.Add(more);
            }
            foreach (var f in _pkg.SongFiles.Take(120))
            {
                var row = new Row { File = f, Size = new FileInfo(f).Length };
                var panel = new Panel { Width = 900, Height = 86, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 8) };
                var name = Theme.Label(Path.GetFileNameWithoutExtension(f), Theme.Heading); name.Location = new Point(14, 10);
                var size = Theme.Label($"{row.Size / (1024.0 * 1024):0.0} MB", Theme.Small, Theme.Muted); size.Location = new Point(16, 36);
                var arrow = Theme.Label("replaces", Theme.Small, Theme.Muted); arrow.Location = new Point(300, 14);
                row.Pick = new ComboBox { Location = new Point(300, 32), Width = 560, DropDownStyle = ComboBoxStyle.DropDown, BackColor = Theme.Back, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, MaxDropDownItems = 20,
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
                row.Where = new Label { Location = new Point(300, 60), AutoSize = false, Size = new Size(560, 20), Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };
                // a file named like P+'s song (packs made for Brawl use names like X02.brstm) replaces that song
                string stem = Path.GetFileNameWithoutExtension(f);
                row.SameName = _songs.Any(x => string.Equals(Path.GetFileName(x.FileName), stem, StringComparison.OrdinalIgnoreCase));
                row.Pick.SelectedIndexChanged += (s, e) => { if (!_filling) row.Auto = false; Update(); };
                row.Pick.TextChanged += (s, e) => Update();
                panel.Controls.AddRange(new Control[] { name, size, arrow, row.Pick, row.Where });
                _list.Controls.Add(panel);
                _rows.Add(row);
            }
            _filling = false;
            foreach (Control c in _list.Controls) c.Width = Math.Max(600, _list.ClientSize.Width - 50);
            Assign();
        }

        /// <summary>Fill each song's list with the chosen stage's songs (or every song) and suggest one for each:
        /// the song with the same file name, else the stage's songs in turn.</summary>
        void Assign()
        {
            if (_songs == null) return;
            _filling = true;
            string list = _filter.SelectedItem as string;
            bool every = list == null || list == Every;
            var shown = every ? _songs : _songs.Where(x => x.Lists.Any(l => string.Equals(l, list, StringComparison.OrdinalIgnoreCase))).ToList();
            // songs from the rest of P+ stay reachable at the bottom of each list
            var addItem = every ? null : new Item { AddTo = list };
            var items = new object[] { new Item() }.Concat(addItem != null ? new object[] { addItem } : new object[0]).Concat(shown.Select(x => (object)new Item { Song = x }))
                .Concat(every ? Enumerable.Empty<object>() : _songs.Except(shown).Select(x => (object)new Item { Song = x })).ToArray();
            var taken = new HashSet<SongInfo>(_rows.Where(r => !r.Auto).Select(Chosen).Where(x => x != null));
            int next = 0;
            foreach (var r in _rows)
            {
                var keep = r.Auto ? null : Chosen(r);
                bool keepAdd = !r.Auto && (r.Pick.SelectedItem as Item)?.AddTo != null;
                r.Pick.Items.Clear();
                r.Pick.Items.AddRange(items);
                SongInfo want = keep;
                if (want == null)
                {
                    string stem = Path.GetFileNameWithoutExtension(r.File);
                    want = _songs.FirstOrDefault(x => string.Equals(Path.GetFileName(x.FileName), stem, StringComparison.OrdinalIgnoreCase));
                }
                if (want != null) taken.Add(want);
                // a stage is picked: the song joins that stage's songs (unless it's named like a P+ song it replaces)
                int i = want != null ? Array.FindIndex(items, x => ((Item)x).Song == want) : addItem != null && (r.Auto || keepAdd) ? 1 : 0;
                r.Pick.SelectedIndex = Math.Max(0, i);
            }
            _filling = false;
            Update();
        }

        SongInfo Chosen(Row r) => (r.Pick.SelectedItem as Item)?.Song;
        string AddTo(Row r) => (r.Pick.SelectedItem as Item)?.AddTo;

        new void Update()
        {
            if (_filling || _songs == null) return;
            long extra = 0; int n = 0;
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _rows)
            {
                var s = Chosen(r);
                if (s == null && AddTo(r) != null)
                {
                    n++; extra += r.Size;   // a new file on the card
                    r.Where.Text = $"Joins the songs that can play on {AddTo(r)}  •  uses {Space.MB(r.Size)}";
                    continue;
                }
                if (s == null) { r.Where.Text = r.Pick.SelectedIndex < 0 ? "Pick a song from the list." : ""; continue; }
                n++;
                used[s.CardPath] = used.TryGetValue(s.CardPath, out int k) ? k + 1 : 1;
                var mine = _lib.Stages.FirstOrDefault(x => x.Enabled && string.Equals(x.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase));
                r.Where.Text = "Plays on: " + string.Join(", ", s.Lists) + (s.FromBrawl ? "  •  Brawl's own song" : "") + (mine != null ? $"  •  turns off your {mine.Name}" : "");
                extra += r.Size - s.Size;   // it replaces what's there now (a song from the disc adds the whole file)
            }
            bool clash = used.Values.Any(v => v > 1);
            long room = _free - PPlusCard.MinFreeBytes;
            _summary.ForeColor = clash || extra > room ? Theme.Bad : Theme.Muted;
            _summary.Text = clash ? "Two songs are set to replace the same P+ song. Pick a different one for one of them."
                : extra > room ? $"Not enough room on the P+ SD card: these need {Space.MB(extra)} more and there's {Space.MB(Math.Max(0, room))} left for mods. Add fewer songs, or turn off another mod."
                : n == 0 ? "Pick which P+ song each one replaces."
                : $"{n} song(s) will be replaced, using {(extra >= 0 ? Space.MB(extra) + " more" : Space.MB(-extra) + " less")} space. Left for mods after: {Space.MB(Math.Max(0, room - extra))}.";
            _install.Text = n <= 1 ? "Install" : $"Install ({n})";
            _install.Enabled = n > 0 && !clash && extra <= room;
            _layoutFoot();
        }

        async void Install()
        {
            // what's picked is read here; checking each song (it reads and converts it) happens in the background
            var picked = _rows.Select(r => (r.File, Song: Chosen(r), Add: AddTo(r))).Where(x => x.Song != null || x.Add != null).ToList();
            string was = _install.Text; _install.Enabled = false; _install.Text = "Checking...";
            var plans = new List<StagePlan>(); var problems = new List<string>();
            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var (file, song, add) in picked)
                {
                    try
                    {
                        StagePlan plan;
                        if (song != null) plan = _installer.PlanSong(_pkg, file, song);
                        else using (var card = _installer.OpenCard()) plan = _installer.PlanNewSong(card, _pkg, file, add.Replace(' ', '_') + ".tlst", add);
                        if (_pkg.SongFiles.Count == 1 && !string.IsNullOrEmpty(_pkg.Name)) plan.Name = _pkg.Name;
                        if (plan.CanInstall) plans.Add(plan); else problems.Add($"{Path.GetFileName(file)}: " + string.Join("; ", plan.Checks.Where(c => !c.Passed).Select(c => c.Title)));
                    }
                    catch (Exception e) { problems.Add($"{Path.GetFileName(file)}: {SkinManager.Core.Friendly.Explain(e)}"); }
                }
            });
            if (IsDisposed) return;
            _install.Text = was; _install.Enabled = true;
            if (problems.Count > 0)
            {
                string text = "These can't be installed:\r\n\r\n" + string.Join("\r\n", problems.Take(12));
                if (plans.Count == 0) { MessageBox.Show(this, text, "Can't install", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (MessageBox.Show(this, text + $"\r\n\r\nInstall the other {plans.Count}?", "Some can't be installed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            Plans = plans;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
