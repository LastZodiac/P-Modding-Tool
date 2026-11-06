// "Stage versions": what a stage's versions look like, picked by picture.
//   1. Pick a stage.   2. Pick the version to change (the normal one, Hold L, an alt from its list...).   3. Pick the look.
// The version keeps P+'s gameplay, so online games stay in sync: only the scenery changes. For the normal one, a version
// that plays exactly alike is swapped for real (just picking the stage gives it, and its button gives the normal one);
// anything else (and any other version) gets the chosen version's scenery put on it. "P+'s own look" undoes it.
// Handy for matchmaking: when unranked picks an alt (say Smashville's Sakura), it can look like the normal Smashville.
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
    /// <summary>The "Stage versions" window: choose how each version of a stage looks, or (when adding a stage from a
    /// download) which versions it goes on.</summary>
    class StageVersionsForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;

        /// <summary>What to do when it closes with OK (null: nothing).</summary>
        public Installer.Change Change { get; private set; }
        public string Done { get; private set; }
        /// <summary>Closed with "Add a stage mod from a file…".</summary>
        public bool AddStageAsked { get; private set; }

        /// <summary>One version of a stage (the normal one, Hold L, an alt from its list...) and its files on the card.</summary>
        sealed class Version
        {
            public StageSpot Spot;                        // the first way to get it (its button, its settings)
            public List<string> Files = new List<string>();
            public string Name;                           // "Hold L: SV Sakura"
            public string Label;                          // "SV Sakura"
            public string Badge;                          // "Normal", "Hold L", "L list"
            public bool IsNormal;
            public Dictionary<string, string> SubStages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // file -> the time of day it is (Smashville)
            public override string ToString() => Name;
        }
        /// <summary>A stage mod of yours on one of the stage's versions (all its files there, like the normal Smashville's
        /// five times of day): it can be moved to another version, or used there too.</summary>
        sealed class Mod
        {
            public List<InstalledStage> Entries = new List<InstalledStage>();
            public InstalledStage First => Entries[0];
            public Version On;                            // the version it's on
            public string Name => First.Name;
            public bool Enabled => Entries.Any(e => e.Enabled);
        }
        /// <summary>A stage on the stage select screen, with its normal version and the others.</summary>
        sealed class Slot
        {
            public int Id; public string Name; public int Picture = -1; public Version Normal; public List<Version> Others = new List<Version>();
            public IEnumerable<Version> All => new[] { Normal }.Concat(Others);
            public override string ToString() => Name;
        }

        // page 1: the stages
        readonly FlowLayoutPanel _stageTiles = new FlowLayoutPanel { AutoScroll = true, WrapContents = true, BackColor = Theme.Back };
        readonly TextBox _search = new TextBox { Width = 240, BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
        // page 2: one stage's versions
        readonly Panel _stagePage = new Panel { BackColor = Theme.Back, Visible = false, AutoScroll = true };
        readonly Button _back = Theme.Secondary("←  All stages");
        readonly Label _stageName = Theme.Label("", Theme.Title);
        readonly Label _step1 = Theme.Label("1. Which versions do you want to change? Tick one or more.", Theme.Heading), _step2 = Theme.Label("2. What should it look like?", Theme.Heading);
        // the versions wrap onto more rows (Smashville has 13): the page scrolls, every choice stays in view
        readonly FlowLayoutPanel _targets = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
        readonly FlowLayoutPanel _looks = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
        readonly Label _status = Theme.Label("", Theme.Body, Theme.Muted);
        readonly PictureBox _pic = new PictureBox { Size = new Size(352, 198), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Visible = false };
        readonly Label _picCap = Theme.Label("How it will look", Theme.Small, Theme.Muted);
        readonly Button _use = Theme.Primary("Use this look");
        readonly Label _hint = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Label _title = Theme.Label("Stage versions: choose how each one looks", Theme.Title);

        readonly Dictionary<string, (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture)> _checked = new Dictionary<string, (string, StagePlan, List<StagePlan>, byte[])>();
        readonly Dictionary<string, Bitmap> _renders = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int, string), Bitmap> _thumbs = new Dictionary<(int, string), Bitmap>();
        Dictionary<string, (string TrackList, string Module)> _info;
        List<Slot> _slots = new List<Slot>();
        Slot _slot; Version _target, _look; Mod _mod; bool _undo; int _asking;
        readonly List<Version> _chosen = new List<Version>();   // the versions ticked to change (several get the same look)
        Mod _startMod;                                          // opened from a stage mod in My Mods: it's the look
        readonly InstalledStage _startFrom;                     // ...that mod (the stage it's on opens straight away)
        readonly CheckBox _move = new CheckBox { AutoSize = true, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.Body, Visible = false };

        // ----- adding a stage from a download (install mode): its stage files are the looks, put on the versions you tick

        readonly SkinPackage _pkg;
        /// <summary>One stage file from the download being added: its gameplay fingerprint once worked out, and its own
        /// choice (a download can bring files for several stages: Radical Highway for Green Hill Zone and for Luigi's
        /// Mansion), so each file keeps the versions ticked for it and what checking them found, and all go in together.</summary>
        sealed class NewLook
        {
            public string File; public string Name; public Dictionary<string, string> Gameplay;
            public Slot Slot;                                       // the stage it's for (the one shown when it's picked)
            public readonly List<Target> Picked = new List<Target>();
            public List<StagePlan> Plans;                           // what checking the ticked versions found (null: not yet)
            public List<string> Lines = new List<string>();         // the verdicts, one per ticked version
            public bool Bad;                                        // some ticked version can't have it
            public int Checks;                                      // counts the checks started (an older one's result is dropped)
            public bool Checking;
        }
        /// <summary>A version ticked to put the download on: all its files, or one of them (one time of day).</summary>
        sealed class Target { public Slot S; public Version V; public List<string> Files; public string Title; public string Key; }
        readonly List<NewLook> _new = new List<NewLook>();
        NewLook _newLook;
        /// <summary>The versions ticked for the stage file shown.</summary>
        List<Target> _picked => _newLook?.Picked ?? new List<Target>();
        StageSelect _select;
        Dictionary<string, uint> _modules;
        List<StageFile> _allFiles;
        /// <summary>Adding a stage: what to install (empty: nothing).</summary>
        public List<StagePlan> Plans { get; private set; } = new List<StagePlan>();

        /// <summary>The window for adding a stage from a download: its stage files are the looks, put on the versions
        /// you tick.</summary>
        public StageVersionsForm(Installer installer, Library lib, SkinPackage pkg) : this(installer, lib)
        {
            _pkg = pkg;
            foreach (var f in pkg.StageFiles) _new.Add(new NewLook { File = f, Name = pkg.StageFiles.Count == 1 ? pkg.Name : Path.GetFileNameWithoutExtension(f) });
            _newLook = _new.FirstOrDefault();
            Text = "Add a stage: " + pkg.Name;
            _title.Text = $"Where should \"{Short(pkg.Name, 50)}\" go?";
            _hint.Text = "Pick the stage, then tick every version it should go on (the normal one, one time of day, Hold L, R or Z, an alt from its list). " +
                         "You see how it will look before anything changes. You always play on P+'s own floors, platforms and blast zones, so it's safe online.";
        }

        /// <summary>The window opened from a stage mod in My Mods: its stage opens straight away, with the mod as the look,
        /// so ticking the versions to put it on is all that's left.</summary>
        public StageVersionsForm(Installer installer, Library lib, InstalledStage from) : this(installer, lib)
        {
            _startFrom = from;
            _title.Text = $"Put \"{Short(from.Name, 50)}\" on other versions";
            _hint.Text = "Tick every version it should be on (as many as you like), then press the button. You always play on P+'s own floors, platforms and blast zones, so it's safe online.";
        }

        static string Short(string t, int n) => t == null ? "" : t.Length > n ? t.Substring(0, n - 1) + "…" : t;

        /// <summary>Builds the window (stage tiles, then one stage's page) and reads the card's stages once it's shown.</summary>
        public StageVersionsForm(Installer installer, Library lib)
        {
            _installer = installer; _lib = lib;
            Text = "Stage versions";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1180, 820); MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var title = _title; title.Location = new Point(24, 14);
            _hint.Text = "Pick a stage. Then pick one of its versions (the normal one, or one you get by holding L, R or Z, or from its list) and the look you want it to have: " +
                         "another version's, or one of your stage mods (move it from one version to another). Only the scenery changes: you play on P+'s stage either way, so it's safe online.";
            _hint.Location = new Point(26, 50); _hint.AutoSize = false; _hint.Size = new Size(1100, 40);
            Theme.Cue(_search, "Find a stage…");
            _search.TextChanged += (s, e) => ShowStages();

            _back.Click += (s, e) => { _stagePage.Visible = false; _stageTiles.Visible = _search.Visible = true; ShowStages(); };
            _stageName.Location = new Point(0, 4);
            _step1.AutoSize = true; _step2.AutoSize = true;
            _status.AutoSize = false;
            _use.Enabled = false;
            _use.Click += (s, e) => Use();
            _stagePage.Controls.AddRange(new Control[] { _back, _stageName, _step1, _targets, _step2, _looks, _status, _picCap, _pic, _use, _move });

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Panel };
            var close = Theme.Secondary("Close");
            close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            foot.Controls.Add(close);
            // a stage mod from a download or file: opens this same window for it, to tick the versions it goes on
            var addStage = Theme.Secondary("Add a stage mod from a file…");
            addStage.Click += (s, e) => { AddStageAsked = true; DialogResult = DialogResult.Cancel; Close(); };
            foot.Controls.Add(addStage);
            foot.Resize += (s, e) => { close.Location = new Point(foot.Width - close.Width - 24, (foot.Height - close.Height) / 2); addStage.Location = new Point(24, close.Top); addStage.Visible = _pkg == null; };
            Controls.AddRange(new Control[] { title, _hint, _search, _stageTiles, _stagePage, foot });
            Resize += (s, e) => Lay();
            Lay();
            Shown += async (s, e) => await LoadStages();
        }

        /// <summary>Fits the stage list and the stage page to the window's size.</summary>
        void Lay()
        {
            int w = ClientSize.Width - 48, top = 100, bottom = ClientSize.Height - 56 - 12;
            _hint.Width = w;
            _search.Location = new Point(24 + w - _search.Width, top);
            _stageTiles.SetBounds(24, top + 32, w, bottom - top - 32);
            _stagePage.SetBounds(24, top, w, bottom - top);
            LayPage();
        }

        /// <summary>Places the stage page's parts under each other, each row of tiles as tall as its tiles need.</summary>
        void LayPage()
        {
            int scroll = _stagePage.VerticalScroll.Value;
            int pw = _stagePage.Width - SystemInformation.VerticalScrollBarWidth - 6, top = -scroll;   // room kept for the scroll bar: no sideways scrolling
            int Rows(FlowLayoutPanel f) { int per = Math.Max(1, pw / 224); int n = f.Controls.Count; return n == 0 ? 0 : (n + per - 1) / per; }
            _back.Location = new Point(pw - _back.Width, top + 2);
            _stageName.Location = new Point(0, top + 4);
            _step1.Location = new Point(0, top + 46);
            _targets.SetBounds(0, top + 72, pw, Rows(_targets) * 156);
            int y = _targets.Bottom + 8;
            _step2.Location = new Point(0, y);
            _looks.SetBounds(0, y + 26, pw, Rows(_looks) * 156);
            y = (_looks.Controls.Count > 0 ? _looks.Bottom : y) + 10;
            _pic.Size = new Size(352, 198);
            _pic.Location = new Point(pw - _pic.Width, y + 18); _picCap.Location = new Point(_pic.Left, y);
            int sw = Math.Max(200, _pic.Left - 20);
            int sh = Math.Max(110, TextRenderer.MeasureText(_status.Text ?? "", _status.Font, new Size(sw, 2000), TextFormatFlags.WordBreak).Height + 10);
            _status.SetBounds(0, y, sw, sh);
            int below = Math.Max(y + sh, _pic.Visible ? _pic.Bottom - 70 : 0);
            _move.Location = new Point(0, below + 10);
            _use.Location = new Point(0, below + 42);
        }

        static string Key(Slot s, Version target, Version look) => s.Id + "|" + target.Spot.Entry + "|" + look.Spot.Entry;
        static string ModKey(int slot) => $"version|{slot:X2}|";
        /// <summary>Our mods' key for a version's look: the normal one as before ("version|21|param#entry"); another version
        /// "version|21|@<entry>|param#entry".</summary>
        static string KeyFor(Slot s, Version target, Version look) =>
            ModKey(s.Id) + (target.IsNormal ? "" : "@" + target.Spot.Entry + "|") + look.Spot.Param + "#" + look.Spot.Entry;
        static string TargetPrefix(Slot s, Version target) => ModKey(s.Id) + (target.IsNormal ? "" : "@" + target.Spot.Entry + "|");

        /// <summary>Our mods that set this version's look now (a swap, or another version's looks).</summary>
        List<InstalledStage> Current(Slot s, Version target) => _lib.Stages.Where(x => x.PackageKey != null && x.PackageKey.StartsWith(TargetPrefix(s, target), StringComparison.OrdinalIgnoreCase)
            && (!target.IsNormal || !x.PackageKey.Substring(ModKey(s.Id).Length).StartsWith("@"))).ToList();
        List<InstalledStage> AnyCurrent(Slot s) => _lib.Stages.Where(x => x.Enabled && x.PackageKey != null && x.PackageKey.StartsWith(ModKey(s.Id), StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>The version whose look this one has now (null: its own).</summary>
        Version LookNow(Slot s, Version target)
        {
            var now = Current(s, target).FirstOrDefault(x => x.Enabled);
            if (now == null) return null;
            string rest = now.PackageKey.Substring(TargetPrefix(s, target).Length);
            return s.All.FirstOrDefault(v => string.Equals(rest, v.Spot.Param + "#" + v.Spot.Entry, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Your stage mods on this stage's versions (not the looks set here, not swaps or songs).</summary>
        List<Mod> Mods(Slot s)
        {
            var result = new List<Mod>();
            var mine = _lib.Stages.Where(x => !x.IsSong && !x.IsSwap && !(x.PackageKey ?? "").StartsWith("version|", StringComparison.OrdinalIgnoreCase)).ToList();
            string Key(InstalledStage x) => x.Group ?? x.PackageKey ?? x.Id;
            // A file can belong to more than one version (Smashville's normal one picks between the files its R list
            // names). A mod covering every file of the bigger version is on that one; otherwise it's on the smallest.
            Version OnFor(InstalledStage x)
            {
                var have = new HashSet<string>(mine.Where(y => Key(y) == Key(x)).Select(y => y.CardPath), StringComparer.OrdinalIgnoreCase);
                var fits = s.All.Where(v => v.Files.Any(f => string.Equals(f, x.CardPath, StringComparison.OrdinalIgnoreCase))).ToList();
                return fits.Where(v => v.Files.All(have.Contains)).OrderByDescending(v => v.Files.Count).FirstOrDefault()
                       ?? fits.OrderBy(v => v.Files.Count).FirstOrDefault();
            }
            foreach (var g in mine
                         .Select(x => (x, On: OnFor(x)))
                         .Where(x => x.On != null)
                         .GroupBy(x => (x.x.Group ?? x.x.PackageKey ?? x.x.Id) + "|" + x.On.Spot.Entry))
                result.Add(new Mod { Entries = g.Select(x => x.x).ToList(), On = g.First().On });
            return result.OrderByDescending(m => m.Enabled).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The mod of yours that's on a version now (null: none).</summary>
        Mod ModOn(Slot s, Version v) => Mods(s).FirstOrDefault(m => m.On == v && m.Enabled);

        /// <summary>Reads P+'s stages and their versions from the card (stages with only one version are left out
        /// unless adding a stage), then opens the stage a download is named for.</summary>
        async Task LoadStages()
        {
            try
            {
                (_slots, _thumbs) = await Task.Run(() =>
                {
                    using (var card = _installer.OpenCard())
                    {
                        _info = Songs.StageInfo(card);
                        var select = _installer.OriginalSelect(card);
                        _select = select;
                        if (_pkg != null) { _modules = Stages.ModuleIds(card); _allFiles = Stages.All(card); }
                        var all = select.All.Where(x => x.Page > 0).ToList();
                        var result = new List<Slot>();
                        foreach (var g in all.GroupBy(x => x.Slot).OrderBy(g => g.First().Page).ThenBy(g => g.First().Row).ThenBy(g => g.First().Column))
                        {
                            var versions = g.GroupBy(x => x.Entry).OrderBy(e => e.Key).Select(e => new Version
                            {
                                Spot = e.First(), Files = e.Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
                                SubStages = e.Where(x => x.SubStage != null).GroupBy(x => x.File, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().SubStage, StringComparer.OrdinalIgnoreCase),
                            }).ToList();
                            var normal = versions.FirstOrDefault(v => v.Spot.Button == 0);
                            if (normal == null) continue;
                            normal.Name = "The normal one"; normal.Label = "the normal " + g.First().SlotName; normal.Badge = "Normal"; normal.IsNormal = true;
                            var slot = new Slot { Id = g.Key, Name = g.First().SlotName, Normal = normal, Picture = normal.Spot.Picture };
                            foreach (var v in versions.Where(v => v != normal && !v.Files.SequenceEqual(normal.Files, StringComparer.OrdinalIgnoreCase)))
                            {
                                var d = Stages.Describe(v.Files[0]);
                                string how = v.Spot.HowToPick;
                                v.Label = v.Spot.ListEntry ?? (d.Version == "" ? d.StageName : System.Text.RegularExpressions.Regex.Replace(d.Version, @"^(L|R|Z|ML|MR)(: | version)", ""));
                                v.Name = (how.StartsWith("its ") ? how.Substring(4, 1).ToUpperInvariant() + how.Substring(5) : char.ToUpperInvariant(how[0]) + how.Substring(1))
                                         + (how.Contains("\"") ? "" : ": " + v.Label);
                                v.Badge = v.Spot.Region == "L" ? "Hold L" : v.Spot.Region == "R" ? "Hold R" : v.Spot.Region == "Z" ? "Hold Z"
                                        : v.Spot.Button >= 0x8000 && v.Spot.Button < 0xC000 ? "L list" : v.Spot.Button >= 0x4000 && v.Spot.Button < 0x8000 ? "R list" : "Buttons";
                                slot.Others.Add(v);
                            }
                            if (slot.Others.Count > 0 || _pkg != null) result.Add(slot);   // adding a stage: every stage
                        }
                        var thumbs = StagePictures.Read(card, result.SelectMany(s => s.All.Select(v => (s.Picture, v.Spot.Region ?? "main")).Concat(new[] { (s.Picture, "main") })).Distinct());
                        return (result, thumbs);
                    }
                });
            }
            catch (Exception e) { _hint.Text = "Couldn't read P+'s stages: " + Friendly.Explain(e); _hint.ForeColor = Theme.Bad; return; }
            if (IsDisposed) return;
            if (_slots.Count == 0) _hint.Text = "No stage on your SD card has other versions.";
            ShowStages();
            if (_startFrom != null)
            {
                var from = _slots.FirstOrDefault(sl => sl.All.Any(v => v.Files.Contains(_startFrom.CardPath, StringComparer.OrdinalIgnoreCase)));
                if (from != null) OpenStage(from);
                else { _hint.Text = $"{_startFrom.Name} is on a stage that has no other versions, so there's nowhere else to put it."; _hint.ForeColor = Theme.Bad; }
            }
            if (_pkg != null && _allFiles != null)
            {
                // each of the download's stage files: the stage it's for, and the version it's named exactly like ticked for it
                // (a file called like one of P+'s, STGGREENHILLZONE_Z_RADICALHIGHWAY.pac, was made to go there), so a
                // download made for several stages goes on all of them with one click
                foreach (var n in _new)
                {
                    n.Slot = SlotFor(n);
                    var exact = ExactTarget(n);
                    if (exact != null) n.Picked.Add(exact);
                }
                // the first file that has a stage is shown; the others are checked meanwhile
                var first = _new.FirstOrDefault(n => n.Slot != null);
                if (first != null) { _newLook = first; OpenStage(first.Slot); }
                foreach (var n in _new.Where(n => n != _newLook && n.Picked.Count > 0)) Recheck(n);
            }
        }

        /// <summary>The stage select picture for a version: its own one for Hold L/R/Z when there is one, else the
        /// stage's.</summary>
        Bitmap Thumb(Slot s, Version v)
        {
            if (v != null && v.Spot.Region != null && v.Spot.Region != "main" && _thumbs.TryGetValue((s.Picture, v.Spot.Region), out var part)) return part;
            _thumbs.TryGetValue((s.Picture, "main"), out var main); return main;
        }

        // ----- page 1: the stages

        void ShowStages()
        {
            string q = (_search.Text ?? "").Trim();
            _stageTiles.SuspendLayout();
            foreach (Control c in _stageTiles.Controls.Cast<Control>().ToList()) c.Dispose();
            _stageTiles.Controls.Clear();
            foreach (var s in _slots.Where(x => q == "" || x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.Others.Any(v => (v.Label ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                int changed = s.All.Count(v => LookNow(s, v) != null);
                var tile = new PictureTile(196, 146)
                {
                    Value = s, Title = s.Name, Picture = Thumb(s, null),
                    Subtitle = $"{s.Others.Count + 1} versions" + (changed > 0 ? $" · {changed} changed by you" : ""),
                    Badge = changed > 0 ? "Changed" : null,
                };
                var slot = s;
                tile.Click += (o, e) => OpenStage(slot);
                _stageTiles.Controls.Add(tile);
            }
            _stageTiles.ResumeLayout();
            DrawMissing();
        }

        bool _drawingMissing;
        /// <summary>Stages the stage select has no picture for: drawn from their normal file instead.</summary>
        async void DrawMissing()
        {
            if (_drawingMissing) return;
            _drawingMissing = true;
            try
            {
                foreach (var tile in _stageTiles.Controls.OfType<PictureTile>().Where(t => t.Picture == null).ToList())
                {
                    var s = (Slot)tile.Value; string f = s.Normal.Files[0];
                    if (!_renders.TryGetValue(f, out var b))
                    {
                        try { b = await Task.Run(() => { using (var card = _installer.OpenCard()) { var bytes = _lib.HasOriginalFile(f) ? _lib.OriginalFile(f) : card.ReadFile(f); return bytes == null ? null : ModelPreview.RenderStage(bytes, 320, 180); } }); }
                        catch { b = null; }
                        if (IsDisposed) return;
                        _renders[f] = b;
                    }
                    if (!tile.IsDisposed && b != null) tile.Picture = b;
                }
            }
            finally { _drawingMissing = false; }
        }

        // ----- page 2: one stage

        void OpenStage(Slot s)
        {
            _slot = s; _target = null; _look = null; _undo = false; _chosen.Clear();
            _stageTiles.Visible = _search.Visible = false; _stagePage.Visible = true;
            _stageName.Text = s.Name;
            if (_pkg != null) { OpenForInstall(s); return; }
            FillTargets();
            _looks.Controls.Clear(); _step2.Visible = false;
            _startMod = _startFrom == null ? null : Mods(s).FirstOrDefault(m => m.Entries.Any(e => e.Id == _startFrom.Id));
            _status.Text = _startMod != null ? $"Tick the versions to put {_startMod.Name} on (as many as you like)."
                         : "Tick the version you want to change, or several to give them all the same look. (In matchmaking, the game can give you any of them.)"; _status.ForeColor = Theme.Muted;
            _use.Enabled = false; _use.Visible = false; _pic.Visible = _picCap.Visible = false;
            _stagePage.AutoScrollPosition = Point.Empty;
            LayPage();
            RenderVersions(s);
        }

        // ----- install mode: tick versions, see the result, install

        void OpenForInstall(Slot s)
        {
            _step1.Text = $"1. Which versions of {s.Name} should it go on? Tick as many as you like.";
            foreach (Control c in _targets.Controls.Cast<Control>().ToList()) c.Dispose();
            _targets.Controls.Clear();
            var mods = Mods(s);
            foreach (var v in s.All)
            {
                var targets = new List<Target> { new Target { S = s, V = v, Files = v.Files, Title = v.IsNormal ? "Normal " + s.Name + (v.Files.Count > 1 ? $" (all {v.Files.Count})" : "") : v.Label, Key = v.Spot.Entry + "|all" } };
                // a normal version that's several files (Smashville's times of day): each one on its own too
                if (v.IsNormal && v.Files.Count > 1)
                    foreach (var f in v.Files)
                        targets.Add(new Target { S = s, V = v, Files = new List<string> { f }, Title = "Normal: " + (Stages.Describe(f).Version is string tv && tv != "" ? tv : v.SubStages.TryGetValue(f, out var sub) ? sub : Path.GetFileNameWithoutExtension(f)), Key = v.Spot.Entry + "|" + f });
                foreach (var t in targets)
                {
                    var on = mods.Where(m => m.Enabled && m.Entries.Any(e => t.Files.Contains(e.CardPath, StringComparer.OrdinalIgnoreCase))).Select(m => m.Name).Distinct().ToList();
                    string sub = (on.Count > 0 ? "Now: " + string.Join(", ", on) : v.IsNormal ? (v.Files.Count == 1 ? "just pick it" : t.Files.Count > 1 ? "just pick it (the game picks one)" : "when the game picks this one") : HowTo(v));
                    var tile = VersionTile(s, v, sub);
                    tile.Title = t.Title; tile.Value = t;
                    if (t.Files.Count == 1 && v.Files.Count > 1) tile.Badge = "One of them";
                    tile.Selected = _picked.Any(p => p.Key == t.Key && p.S == s);
                    var target = t;
                    tile.Click += (o, e) => ToggleTarget(target);
                    _targets.Controls.Add(tile);
                }
            }
            foreach (Control c in _looks.Controls.Cast<Control>().ToList()) c.Dispose();
            _looks.Controls.Clear();
            _step2.Visible = _new.Count > 1;
            _step2.Text = "2. Which of the download's stage files?";
            if (_new.Count > 1)
                foreach (var n in _new)
                {
                    var tile = new PictureTile(214, 146) { Value = n, Title = n.Name, Badge = "New", Subtitle = FileLine(n), Selected = n == _newLook, Glyph = "★" };
                    var look = n;
                    tile.Click += (o, e) => PickNewLook(look);
                    _looks.Controls.Add(tile);
                }
            _use.Visible = true; _move.Visible = false; _pic.Visible = _picCap.Visible = false;
            _stagePage.AutoScrollPosition = Point.Empty;
            ShowNewStatus();
            RenderVersions(s);
            DrawNewLooks();
            if (_newLook != null && _newLook.Picked.Count > 0 && _newLook.Plans == null && !_newLook.Checking) Recheck(_newLook);
        }

        /// <summary>Shows one of the download's stage files: its stage (when it's for another one than the one shown) with
        /// the versions ticked for it.</summary>
        void PickNewLook(NewLook n)
        {
            _newLook = n;
            var slot = n.Slot ?? _slot;
            if (slot != null && slot != _slot) { OpenStage(slot); return; }
            // the same stage: the ticks change to this file's
            n.Slot = slot;
            foreach (var t in _targets.Controls.OfType<PictureTile>()) t.Selected = t.Value is Target xt && _picked.Any(p => p.Key == xt.Key && p.S == _slot);
            foreach (var t in _looks.Controls.OfType<PictureTile>()) t.Selected = t.Value == n;
            ShowNewStatus();
            if (n.Picked.Count > 0 && n.Plans == null && !n.Checking) Recheck(n);
        }

        /// <summary>The stage a download's stage file is for: the one with a file of exactly that name, else the one it's
        /// named for (its title or file name), else none.</summary>
        Slot SlotFor(NewLook n)
        {
            string name = Path.GetFileName(n.File);
            var exact = _slots.FirstOrDefault(sl => sl.All.Any(v => v.Files.Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))));
            if (exact != null) return exact;
            // only its own file name here: the download's title names one stage, and the other files are for others
            foreach (var b in Stages.NamedFor(n.File, _new.Count == 1 ? _pkg.Name : "", _allFiles))
            {
                var sl = _slots.FirstOrDefault(x => x.All.Any(v => v.Files.Any(f => string.Equals(Stages.Describe(f).Base, b, StringComparison.OrdinalIgnoreCase))));
                if (sl != null) return sl;
            }
            return null;
        }

        /// <summary>The version a stage file is named exactly like (the whole version, or that one file of a normal version
        /// that's several: one time of day), or null.</summary>
        Target ExactTarget(NewLook n)
        {
            if (n.Slot == null) return null;
            string name = Path.GetFileName(n.File);
            var mods = Mods(n.Slot).Where(m => m.Enabled).ToList();
            foreach (var v in n.Slot.All)
            {
                var f = v.Files.FirstOrDefault(x => string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;
                // one of your mods is on it already: not replaced unless you tick it yourself
                if (mods.Any(m => m.Entries.Any(e => string.Equals(e.CardPath, f, StringComparison.OrdinalIgnoreCase)))) return null;
                if (v.Files.Count == 1 || !v.IsNormal)
                    return new Target { S = n.Slot, V = v, Files = v.Files, Title = v.IsNormal ? "Normal " + n.Slot.Name : v.Label, Key = v.Spot.Entry + "|all" };
                string sub = Stages.Describe(f).Version is string tv && tv != "" ? tv : v.SubStages.TryGetValue(f, out var ss) ? ss : Path.GetFileNameWithoutExtension(f);
                return new Target { S = n.Slot, V = v, Files = new List<string> { f }, Title = "Normal: " + sub, Key = v.Spot.Entry + "|" + f };
            }
            // named like the stage itself under another name (STGLUIGIMANSION for P+'s STGLUIGISMANSION), with no version in
            // its name: the normal one (one file only: a stage with several, like Smashville's times of day, is left to you)
            var normal = n.Slot.Normal;
            if (normal.Files.Count == 1 && Stages.Describe(n.File).Version == "" &&
                Stages.NamedFor(n.File, "", _allFiles).Contains(Stages.Describe(normal.Files[0]).Base, StringComparer.OrdinalIgnoreCase) &&
                !mods.Any(m => m.Entries.Any(e => string.Equals(e.CardPath, normal.Files[0], StringComparison.OrdinalIgnoreCase))))
                return new Target { S = n.Slot, V = normal, Files = normal.Files, Title = "Normal " + n.Slot.Name, Key = normal.Spot.Entry + "|all" };
            return null;
        }

        /// <summary>A stage file's line under its tile: where it's going, or its file name.</summary>
        string FileLine(NewLook n) =>
            n.Picked.Count == 0 ? Path.GetFileName(n.File) + (n.Slot != null ? " · for " + n.Slot.Name : "")
            : "→ " + (n.Picked.Count == 1 ? (n.Picked[0].S != null && !n.Picked[0].Title.Contains(n.Picked[0].S.Name) ? n.Picked[0].S.Name + ": " : "") + n.Picked[0].Title : $"{n.Picked.Count} versions of {n.Picked[0].S?.Name}");

        /// <summary>The verdicts for the file shown, a line for each other file going in too, and the install button (it
        /// installs every file that has versions ticked, once they're all checked).</summary>
        void ShowNewStatus()
        {
            var n = _newLook;
            var lines = new List<string>();
            if (n == null || n.Picked.Count == 0) lines.Add(_new.Count > 1 ? $"Tick the versions {n?.Name ?? "it"} should go on (or leave them unticked to leave it out)." : "Tick the versions it should go on.");
            else if (n.Checking || n.Plans == null) lines.Add($"Checking it on {n.Picked.Count} version{(n.Picked.Count == 1 ? "" : "s")}…");
            else lines.AddRange(n.Lines);
            var others = _new.Where(o => o != n && o.Picked.Count > 0).ToList();
            if (others.Count > 0)
            {
                lines.Add("");
                lines.Add("Also going in with it:");
                foreach (var o in others)
                    lines.Add((o.Checking || o.Plans == null ? "…  " : o.Plans.Count == 0 ? "✗  " : "✓  ") + o.Name + " " + FileLine(o).Replace("→", "on") +
                              (o.Checking || o.Plans == null ? " (checking)" : o.Plans.Count == 0 ? " (can't go on it: pick it to see why)"
                               : o.Lines.Any(l => l.Contains("doesn't line up")) ? " (doesn't line up well: pick it to see)" : ""));
            }
            var ready = _new.Where(o => o.Picked.Count > 0).ToList();
            bool checking = ready.Any(o => o.Checking || o.Plans == null);
            int files = ready.Count(o => o.Plans != null && o.Plans.Count > 0), versions = ready.Where(o => o.Plans != null && o.Plans.Count > 0).Sum(o => o.Picked.Count - o.Lines.Count(l => l.StartsWith("✗")));
            if (!checking && files > 0) lines.Add("You play on P+'s own floors, platforms and blast zones on each one, so it's safe online.");
            _status.Text = string.Join("\r\n", lines);
            bool anyBad = ready.Any(o => o.Bad);
            _status.ForeColor = checking || ready.Count == 0 ? Theme.Muted : anyBad ? (files > 0 ? Theme.Warn : Theme.Bad)
                              : ready.Any(o => o.Lines.Any(l => l.Contains("doesn't line up") || l.Contains("check it once offline"))) ? Theme.Warn : Theme.Good;
            _use.Text = files > 1 ? $"Install all {files} ({versions} versions)" : versions <= 1 ? "Install" : $"Install on {versions} versions";
            _use.Enabled = !checking && files > 0;
            foreach (var t in _looks.Controls.OfType<PictureTile>()) if (t.Value is NewLook tn) { t.Subtitle = FileLine(tn); t.Invalidate(); }
            LayPage();
        }

        bool _drawingNew;
        /// <summary>Draws the download's stage files in the background and puts the pictures on their tiles.</summary>
        async void DrawNewLooks()
        {
            if (_drawingNew) return; _drawingNew = true;
            try
            {
                foreach (var n in _new)
                {
                    string key = "new:" + n.File;
                    if (!_renders.ContainsKey(key))
                    {
                        Bitmap b = null;
                        try { var f = n.File; b = await Task.Run(() => ModelPreview.RenderStage(File.ReadAllBytes(f), 320, 180)); } catch (Exception e) { Program.Trace("new look picture: " + e.Message); }
                        if (IsDisposed) return;
                        _renders[key] = b;
                    }
                    foreach (var t in _looks.Controls.OfType<PictureTile>()) if (t.Value == n && _renders[key] != null) t.Picture = _renders[key];
                }
            }
            finally { _drawingNew = false; }
        }

        /// <summary>Ticks or unticks a version to put the download on; ticking "all of them" unticks the single files
        /// and the other way round.</summary>
        void ToggleTarget(Target t)
        {
            var n = _newLook;
            if (n == null) return;
            n.Slot = _slot;
            var have = n.Picked.FirstOrDefault(x => x.Key == t.Key && x.S == t.S);
            if (have != null) n.Picked.Remove(have); else n.Picked.Add(t);
            if (have == null)
            {
                // "all of them" and one of them: whichever was ticked last stays
                bool all = t.Files.Count > 1;
                n.Picked.RemoveAll(x => x != t && x.V == t.V && (all ? x.Files.Count == 1 : x.Files.Count > 1));
                // one file of the download per version: ticking it here unticks it for another of the download's files
                foreach (var o in _new.Where(o => o != n))
                    if (o.Picked.RemoveAll(x => x.Files.Intersect(t.Files, StringComparer.OrdinalIgnoreCase).Any()) > 0) Recheck(o);
            }
            // a file of the download goes on one stage (switching stages starts its ticks again)
            n.Picked.RemoveAll(x => x.S != _slot);
            foreach (var x in _targets.Controls.OfType<PictureTile>()) x.Selected = x.Value is Target xt && n.Picked.Any(p => p.Key == xt.Key && p.S == _slot);
            Recheck(n);
        }

        /// <summary>Works out how one of the download's stage files goes on every version ticked for it, and shows the
        /// verdicts (and a preview when it's the file shown). Waits a moment first, so ticking several in a row is checked once.</summary>
        async void Recheck(NewLook look)
        {
            int me = ++look.Checks;
            look.Plans = null; look.Lines = new List<string>(); look.Bad = false; look.Checking = look.Picked.Count > 0;
            if (look == _newLook) _pic.Visible = _picCap.Visible = false;
            ShowNewStatus();
            var picked = look.Picked.ToList();
            if (picked.Count == 0) return;
            await Task.Delay(350);   // ticking several in a row: checked once at the end
            if (IsDisposed || me != look.Checks) return;
            var lines = new List<string>(); var plans = new List<StagePlan>(); bool bad = false;
            try
            {
                await Task.Run(() =>
                {
                    if (look.Gameplay == null) look.Gameplay = StageCheck.Gameplay(File.ReadAllBytes(look.File));
                    using (var card = _installer.OpenCard())
                        foreach (var t in picked)
                        {
                            if (me != look.Checks) return;
                            string group = t.Files.Count > 1 ? Guid.NewGuid().ToString("N") : null;
                            var verdicts = new List<string>(); bool first = true; string problem = null;
                            foreach (var f in t.Files)
                            {
                                var (plan, how) = PlanFor(card, look, f);
                                if (!plan.CanInstall) { problem = string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)); break; }
                                // a download for several stages: each one's mod says which stage it's for
                                plan.Name = _new.Count > 1 && t.S != null ? $"{_pkg.Name} ({t.S.Name}{(t.V.IsNormal ? "" : ", " + t.Title)})" : look.Name;
                                plan.Group = group;
                                if (first) plan.SelectPictures = PicturesFor(Stages.Describe(f)); first = false;
                                plans.Add(plan); verdicts.Add(how);
                            }
                            if (problem != null) { bad = true; lines.Add($"✗  {t.Title}: can't go on it: {problem}"); continue; }
                            lines.Add($"✓  {t.Title}: " + string.Join("; ", verdicts.Distinct()));
                        }
                });
            }
            catch (Exception e) { lines.Add("✗  Couldn't check it: " + Friendly.Explain(e)); bad = true; plans.Clear(); }
            if (IsDisposed || me != look.Checks) return;
            look.Plans = plans; look.Lines = lines; look.Bad = bad; look.Checking = false;
            ShowNewStatus();
            if (look != _newLook || plans.Count == 0) return;
            _stagePage.ScrollControlIntoView(_use);
            Bitmap shot = null;
            try { var bytes = plans[0].Bytes; shot = await Task.Run(() => ModelPreview.RenderStage(bytes, 480, 270)); } catch (Exception e) { Program.Trace("preview: " + e.Message); }
            if (IsDisposed || me != look.Checks || look != _newLook) { shot?.Dispose(); return; }
            var old = _pic.Image; _pic.Image = shot; old?.Dispose();
            _pic.Visible = _picCap.Visible = shot != null;
        }

        /// <summary>The download's stage file on one of P+'s stage files: the whole file when it plays exactly like it (and
        /// was made for its stage program), else made to play like it (P+'s gameplay copied in), else its looks on P+'s file.</summary>
        (StagePlan Plan, string How) PlanFor(Fat32Image card, NewLook look, string target)
        {
            var t = Stages.Describe(target);
            bool same = Stages.SameProgram(Stages.MadeFor(look.File, _info, _modules), target, _info);
            StagePlan fixd = null;
            if (same)
            {
                var whole = _installer.PlanStage(card, _pkg, look.File, t, look.Gameplay);
                if (whole.CanInstall) return (whole, "made for it, it goes in as it is");
                fixd = _installer.PlanFixedStage(card, _pkg, look.File, t);
                if (fixd.CanInstall && Off(fixd) < 1f) return (fixd, Lines(fixd) + "P+'s floors and blast zones copied into it");
            }
            // its looks on P+'s file: also where they fit P+'s floors clearly better than with P+'s floors copied in
            // (only there can P+'s own look stay for a floor the map doesn't draw)
            var looks = _installer.PlanLooks(card, _pkg, look.File, t);
            if (fixd != null && fixd.CanInstall && !(looks.CanInstall && Off(looks) + 0.5f < Off(fixd)))
                return (fixd, Lines(fixd) + "P+'s floors and blast zones copied into it");
            return (looks, Lines(looks) + "its scenery on P+'s stage" + (same ? "" : " (made for another stage: check it once offline)"));
        }

        /// <summary>How far a plan's scenery is from P+'s floors (as far as can be when it wasn't measured).</summary>
        static float Off(StagePlan p) => p.Fit == null || float.IsNaN(p.Fit.Floors) ? float.MaxValue : p.Fit.Floors;

        /// <summary>How well the scenery lines up with P+'s floors, as the start of a verdict line.</summary>
        static string Lines(StagePlan p)
        {
            var fit = p.Fit;
            if (fit == null || fit.Floors >= 1e6f || float.IsNaN(fit.Floors)) return "";
            // its scenery was fitted onto P+'s floors (moved as a whole, or floor by floor): say what was done
            string moved = p.Aligned != null && p.Aligned.Did ? " once " + p.Aligned.Words() : "";
            return fit.Floors < 1f ? "lines up" + moved + ", " : fit.Floors < 6f ? "nearly lines up" + moved + ", " : "doesn't line up well (you may stand off its ground), ";
        }

        /// <summary>The stage select pictures in the download that go with this stage file, as PNGs.</summary>
        Dictionary<string, byte[]> PicturesFor(StageFile target)
        {
            var result = new Dictionary<string, byte[]>();
            var spot = _select?.SpotsOf(target.CardPath).FirstOrDefault(x => x.Region != null && x.Picture >= 0);
            foreach (var kv in StageSelect.FindPictures(_pkg.Folder, target, spot))
                try
                {
                    using (var img = Image.FromFile(kv.Value)) using (var b = new Bitmap(img)) using (var ms = new MemoryStream())
                    { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); result[kv.Key] = ms.ToArray(); }
                }
                catch (Exception e) { Program.Trace("picture " + kv.Value + ": " + e.Message); }
            return result;
        }

        /// <summary>A tile for a version, with its picture (drawn from its file when ready, else the stage select
        /// picture).</summary>
        PictureTile VersionTile(Slot s, Version v, string sub)
        {
            var tile = new PictureTile(214, 146) { Value = v, Title = v.IsNormal ? "Normal " + s.Name : v.Label, Badge = v.Badge, Subtitle = sub };
            tile.Picture = _renders.TryGetValue(v.Files[0], out var r) ? r : Thumb(s, v);
            return tile;
        }

        /// <summary>Fills step 1 with the stage's versions, each saying what it looks like now.</summary>
        void FillTargets()
        {
            var s = _slot;
            foreach (Control c in _targets.Controls.Cast<Control>().ToList()) c.Dispose();
            _targets.Controls.Clear();
            foreach (var v in s.All)
            {
                var now = LookNow(s, v); var mod = ModOn(s, v);
                string how = v.IsNormal ? "just pick it" : HowTo(v);
                var tile = VersionTile(s, v, mod != null ? $"Your mod: {mod.Name}" : now != null ? $"Looks like {(now.IsNormal ? "the normal one" : now.Label)} now" : how);
                if (mod != null) tile.Picture = _renders.TryGetValue("mod:" + mod.First.Id, out var mr) && mr != null ? mr : tile.Picture;
                tile.Selected = _chosen.Contains(v);
                var version = v;
                tile.Click += (o, e) => PickTarget(version);
                _targets.Controls.Add(tile);
            }
        }

        /// <summary>Ticks or unticks a version to change (several can be ticked: they all get the look you pick) and fills
        /// step 2 with the looks they can have: P+'s own, the other versions', your mods.</summary>
        void PickTarget(Version t)
        {
            var s = _slot; _look = null; _mod = null; _undo = false;
            if (_chosen.Contains(t)) _chosen.Remove(t); else _chosen.Add(t);
            _target = _chosen.FirstOrDefault();
            foreach (var tile in _targets.Controls.OfType<PictureTile>()) tile.Selected = tile.Value is Version tv && _chosen.Contains(tv);
            foreach (Control c in _looks.Controls.Cast<Control>().ToList()) c.Dispose();
            _looks.Controls.Clear();
            _use.Enabled = false; _move.Visible = false; _pic.Visible = _picCap.Visible = false;
            if (_chosen.Count == 0)
            {
                _step2.Visible = false; _use.Visible = false;
                _status.Text = _startMod != null ? $"Tick the versions to put {_startMod.Name} on." : "Tick the versions you want to change: one, or several to give them all the same look."; _status.ForeColor = Theme.Muted;
                LayPage(); return;
            }
            _step2.Visible = true;
            _step2.Text = $"2. What should {ChosenNames()} look like?";
            bool one = _chosen.Count == 1;
            var now = one ? LookNow(s, t) : null;
            // its own look (P+'s): picking it undoes a change
            var own = VersionTile(s, _target, one ? (now == null ? "What it looks like now" : "Back to how P+ made it") : "Back to how P+ made each one");
            own.Title = "P+'s own look"; own.Badge = one && now == null ? "Now" : null;
            own.Click += (o, e) => PickLook(null);
            _looks.Controls.Add(own);
            foreach (var v in s.All.Where(v => !_chosen.Contains(v)))
            {
                var tile = VersionTile(s, v, v.IsNormal ? "The normal stage's scenery" : (v.Badge.EndsWith("list") ? "From its list" : HowTo(v)) + "'s scenery");
                if (one && now == v) tile.Badge = "Now";
                var version = v;
                tile.Click += (o, e) => PickLook(version);
                _looks.Controls.Add(tile);
            }
            foreach (var m in Mods(s))
            {
                bool here = one && m.On == t;
                var tile = new PictureTile(214, 146)
                {
                    Value = m, Title = m.Name, Badge = here && m.Enabled ? "Now" : "Your mod",
                    Subtitle = (m.Enabled ? "On " : "Off, for ") + (m.On.IsNormal ? "the normal one" : m.On.Label) + (here ? "" : m.Enabled ? " now" : ""),
                    Picture = _renders.TryGetValue("mod:" + m.First.Id, out var mr) ? mr : null, Glyph = "★",
                };
                var mod = m;
                tile.Click += (o, e) => PickMod(mod);
                _looks.Controls.Add(tile);
            }
            _status.Text = one ? "Pick the look you want: another version's, or one of your stage mods. (Tick more versions to give them all the same look.)"
                               : $"{_chosen.Count} versions ticked. Pick the look they should all have."; _status.ForeColor = Theme.Muted;
            _use.Visible = true;
            LayPage();
            // opened from a mod in My Mods: that mod is the look, checked straight away
            if (_startMod != null) { var start = Mods(s).FirstOrDefault(m => m.First.Id == _startMod.First.Id); if (start != null) { PickMod(start); return; } }
            _stagePage.ScrollControlIntoView(_step2);
        }

        /// <summary>"the normal Smashville", "SV Sakura", or "3 versions" for the ticked versions.</summary>
        string ChosenNames() => _chosen.Count == 1 ? (_chosen[0].IsNormal ? "the normal " + _slot.Name : _chosen[0].Label) : $"these {_chosen.Count} versions";

        /// <summary>Picks one of your stage mods as the look for the ticked versions: checks it can go on each and offers
        /// to move it rather than copy.</summary>
        async void PickMod(Mod m)
        {
            var s = _slot; if (_chosen.Count == 0) return;
            int me = ++_asking;
            _look = null; _mod = m; _undo = false;
            foreach (var tile in _looks.Controls.OfType<PictureTile>()) tile.Selected = tile.Value == m;
            _use.Enabled = false; _move.Visible = false; _pic.Visible = _picCap.Visible = false; _use.Text = _chosen.Count > 1 ? $"Use this mod on {_chosen.Count} versions" : "Use this mod here";
            string tName = ChosenNames(), onName = m.On.IsNormal ? "the normal " + s.Name : m.On.Label;
            var targets = _chosen.Where(t => t != m.On).ToList();
            if (targets.Count == 0)
            {
                _status.Text = m.Enabled ? $"{m.Name} is on {tName} now." : $"{m.Name} is on {tName} but turned off: turn it on in My Mods.";
                _status.ForeColor = Theme.Muted; return;
            }
            byte[] picture = null;
            foreach (var t in targets)
            {
                string key = "mod|" + m.First.Id + "|" + t.Spot.Entry;
                if (!_checked.TryGetValue(key, out var r))
                {
                    string at = t.IsNormal ? "the normal " + s.Name : t.Label;
                    _status.Text = $"Putting {m.Name} on {at} and checking it plays exactly like P+'s…"; _status.ForeColor = Theme.Muted;
                    try { r = await Task.Run(() => CheckMod(t, m)); }
                    catch (Exception e) { r = (Friendly.Explain(e), null, null, null); }
                    _checked[key] = r;
                    if (IsDisposed || me != _asking) return;
                }
                if (r.Looks == null) { _status.Text = $"{m.Name} can't go on {(t.IsNormal ? "the normal " + s.Name : t.Label)}: {r.Why}."; _status.ForeColor = Theme.Bad; return; }
                picture = picture ?? r.Picture;
            }
            bool onAnother = m.Enabled && !_chosen.Contains(m.On);
            _move.Text = $"Take it off {onName} (move it here)";
            _move.Visible = onAnother;
            _status.Text = $"{m.Name} goes on {tName}" + (targets.Count == 1 && targets[0].Files.Count > 1 ? $" (all {targets[0].Files.Count} of its files)" : "") +
                           $". You still play on P+'s stage exactly as P+ made it, so it's safe online." +
                           (onAnother ? $" Tick the box to take it off {onName} (it goes back to P+'s look there), or untick it to keep it there too." : "");
            _status.ForeColor = Theme.Good;
            _use.Enabled = true;
            _stagePage.ScrollControlIntoView(_use);
            await ShowPreview(picture, me);
        }

        /// <summary>Draws a stage file for the "how it will look" box (unless another choice came since).</summary>
        async Task ShowPreview(byte[] bytes, int me)
        {
            if (bytes == null) return;
            Bitmap shot = null;
            try { shot = await Task.Run(() => ModelPreview.RenderStage(bytes, 480, 270)); } catch (Exception e) { Program.Trace("preview: " + e.Message); }
            if (IsDisposed || me != _asking) { shot?.Dispose(); return; }
            var old = _pic.Image; _pic.Image = shot; old?.Dispose();
            _pic.Visible = _picCap.Visible = shot != null;
        }

        /// <summary>A mod of yours on another version: made to play like P+'s version there (its gameplay copied in, when
        /// the mod is for the same stage program), else its looks on P+'s file for that version.</summary>
        (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture) CheckMod(Version t, Mod m)
        {
            var (map, pictures) = _installer.ModLooks(m.First);
            string dir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "move-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Path.GetFileName(m.First.CardPath));
            File.WriteAllBytes(file, map);
            var source = m.First.GameBananaModId != 0 || m.First.PageUrl != null
                ? new SkinSource { ModId = m.First.GameBananaModId, FileId = m.First.GameBananaFileId, ModName = m.Name, Author = m.First.Author, PageUrl = m.First.PageUrl, ThumbUrl = m.First.ThumbUrl } : null;
            var pkg = new SkinPackage { Folder = dir, Name = m.Name, Source = source, SourcePath = m.First.SourcePath };
            pkg.StageFiles.Add(file);
            var plans = new List<StagePlan>();
            string group = t.Files.Count > 1 ? Guid.NewGuid().ToString("N") : null;
            using (var card = _installer.OpenCard())
            {
                string madeFor = Stages.MadeFor(m.First.CardPath, _info);
                foreach (var f in t.Files)
                {
                    var target = Stages.Describe(f);
                    StagePlan plan = null;
                    if (Stages.SameProgram(madeFor, f, _info)) { plan = _installer.PlanFixedStage(card, pkg, file, target); if (!plan.CanInstall) plan = null; }
                    if (plan == null || Off(plan) >= 1f)
                    {
                        // its looks on P+'s file when they fit P+'s floors clearly better (see PlanFor)
                        var looks = _installer.PlanLooks(card, pkg, file, target);
                        if (plan == null || (looks.CanInstall && Off(looks) + 0.5f < Off(plan))) plan = looks;
                    }
                    if (!plan.CanInstall) return (string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                    plan.Name = m.Name; plan.PackageKey = m.First.PackageKey; plan.Group = group;
                    plan.Source = source; plan.SourcePath = m.First.SourcePath;
                    foreach (var kv in pictures) plan.SelectPictures[kv.Key] = kv.Value;
                    plans.Add(plan);
                }
            }
            return (null, null, plans, plans[0].Bytes);
        }

        /// <summary>Picks another version's look (or P+'s own, null) for the ticked versions: checks whether it can be
        /// swapped for real (one version ticked: the normal one, when they play alike) or its scenery put on each, and
        /// shows a preview.</summary>
        async void PickLook(Version v)
        {
            var s = _slot; if (_chosen.Count == 0) return;
            int me = ++_asking;
            _look = v; _mod = null; _undo = v == null; _move.Visible = false;
            foreach (var tile in _looks.Controls.OfType<PictureTile>()) tile.Selected = v == null ? tile.Title == "P+'s own look" : tile.Value == v;
            _use.Enabled = false; _pic.Visible = _picCap.Visible = false;
            string tName = ChosenNames();
            if (v == null)
            {
                bool changed = _chosen.Any(t => Current(s, t).Any(x => x.Enabled));
                _status.Text = changed ? $"{Cap(tName)} go{(_chosen.Count == 1 ? "es" : "")} back to how P+ made {(_chosen.Count == 1 ? "it" : "them")}." : "That's how it looks now.";
                _status.ForeColor = Theme.Muted; _use.Enabled = changed; _use.Text = "Put P+'s look back";
                return;
            }
            _use.Text = _chosen.Count > 1 ? $"Use this look on {_chosen.Count} versions" : "Use this look";
            bool swapOk = _chosen.Count == 1;
            byte[] picture = null; bool swapped = false;
            foreach (var t in _chosen.ToList())
            {
                string key = Key(s, t, v) + (swapOk ? "" : "|looks");
                if (!_checked.TryGetValue(key, out var r))
                {
                    _status.Text = $"Putting {(v.IsNormal ? "the normal one" : v.Label)}'s look on {(t.IsNormal ? "the normal " + s.Name : t.Label)} and checking it plays exactly like P+'s…"; _status.ForeColor = Theme.Muted;
                    try { var tt = t; r = await Task.Run(() => Check(s, tt, v, swapOk)); }
                    catch (Exception e) { r = (Friendly.Explain(e), null, null, null); }
                    _checked[key] = r;
                    if (IsDisposed || me != _asking) return;
                }
                if (r.Swap == null && r.Looks == null) { _status.Text = $"{Cap(v.IsNormal ? "the normal " + s.Name : v.Label)}'s look can't go on {(t.IsNormal ? "the normal " + s.Name : t.Label)}: {r.Why}."; _status.ForeColor = Theme.Bad; return; }
                swapped |= r.Swap != null;
                picture = picture ?? r.Picture;
            }
            string vName = v.IsNormal ? "the normal " + s.Name : v.Label;
            if (swapped)
                _status.Text = $"{Cap(vName)} plays exactly like {tName}, so they're swapped for real: just picking {s.Name} gives {v.Label}, and {v.Spot.HowToPick} gives the normal one. Only what you see changes, so it's safe online.";
            else
                _status.Text = $"{Cap(tName)} get{(_chosen.Count == 1 ? "s" : "")} {vName}'s scenery. You still play on each exactly as P+ made it (same floors, platforms and blast zones), so it's safe online. Try it offline once to check the scenery sits right.";
            _status.ForeColor = Theme.Good;
            _use.Enabled = true;
            _stagePage.ScrollControlIntoView(_use);
            await ShowPreview(picture, me);
        }

        static string HowTo(Version v) { var h = (v.Spot.HowToPick ?? "").Trim(); return h == "" || h == "hold" ? "a button combination" : h; }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Draws each version (its first file) in the background, for its tile.</summary>
        async void RenderVersions(Slot s)
        {
            // each version's first file; adding a stage, every file of the normal one too (its times of day)
            foreach (var (v, f) in s.All.Select(v => (v, v.Files[0])).Concat(_pkg != null ? s.Normal.Files.Skip(1).Select(x => (s.Normal, x)) : Enumerable.Empty<(Version, string)>()).ToList())
            {
                if (_slot != s || IsDisposed) return;
                if (_renders.ContainsKey(f)) { ShowRender(f); continue; }
                Bitmap b = null;
                try
                {
                    b = await Task.Run(() =>
                    {
                        using (var card = _installer.OpenCard())
                        {
                            byte[] bytes = _lib.HasOriginalFile(f) ? _lib.OriginalFile(f) : card.ReadFile(f);
                            return bytes == null ? null : ModelPreview.RenderStage(bytes, 320, 180);
                        }
                    });
                }
                catch (Exception e) { Program.Trace("version picture: " + e.Message); }
                if (IsDisposed) { b?.Dispose(); return; }
                _renders[f] = b;
                if (b != null)
                    foreach (var tile in _targets.Controls.OfType<PictureTile>().Concat(_looks.Controls.OfType<PictureTile>()))
                        if (tile.Value is Version tv && string.Equals(tv.Files[0], f, StringComparison.OrdinalIgnoreCase) && !(ModOn(s, tv) != null && _targets.Controls.Contains(tile))) tile.Picture = b;
                ShowRender(f);
            }
            // your mods on it, drawn from their own files
            foreach (var m in Mods(s))
            {
                if (_slot != s || IsDisposed) return;
                string key = "mod:" + m.First.Id;
                if (_renders.ContainsKey(key)) continue;
                Bitmap b = null;
                try { var mod = m; b = await Task.Run(() => { var bytes = _installer.ModLooks(mod.First).Map; return bytes == null ? null : ModelPreview.RenderStage(bytes, 320, 180); }); }
                catch (Exception e) { Program.Trace("mod picture: " + e.Message); }
                if (IsDisposed) { b?.Dispose(); return; }
                _renders[key] = b;
                if (b == null) continue;
                foreach (var tile in _looks.Controls.OfType<PictureTile>()) if (tile.Value == (object)m || (tile.Value is Mod tm && tm.First.Id == m.First.Id)) tile.Picture = b;
                foreach (var tile in _targets.Controls.OfType<PictureTile>()) if (tile.Value is Version tv && ModOn(s, tv)?.First.Id == m.First.Id) tile.Picture = b;
            }
        }

        /// <summary>Adding a stage: a drawing of a P+ file on the tiles for it.</summary>
        void ShowRender(string f)
        {
            if (!_renders.TryGetValue(f, out var b) || b == null) return;
            foreach (var tile in _targets.Controls.OfType<PictureTile>())
                if (tile.Value is Target t && string.Equals(t.Files[0], f, StringComparison.OrdinalIgnoreCase)) tile.Picture = b;
        }

        /// <summary>Works out what giving a version another's look would do: a real swap (the normal one, when they play
        /// alike), the look's scenery on the version's files, or why neither.</summary>
        (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture) Check(Slot s, Version t, Version v, bool swapOk = true)
        {
            using (var card = _installer.OpenCard())
            {
                byte[] Read(string f) => _lib.HasOriginalFile(f) ? _lib.OriginalFile(f) : card.ReadFile(f);
                string pkgKey = KeyFor(s, t, v);
                if (swapOk && t.IsNormal && _installer.PlayAlike(card, s.Normal.Files, v.Files, _info) == null)
                {
                    var swap = _installer.PlanSwap(card, s.Normal.Spot, v.Spot, v.Name);
                    swap.PackageKey = pkgKey; swap.Name = $"{s.Name}: {v.Label} as the normal one";
                    return swap.CanInstall ? (null, swap, null, Read(v.Files[0])) : (string.Join(" ", swap.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                }
                // the look's scenery on each file of the version, from P+'s own file for the look
                string dir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "version-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Path.GetFileName(v.Files[0]));
                File.WriteAllBytes(file, Read(v.Files[0]));
                string tName = t.IsNormal ? s.Name : $"{s.Name} ({t.Label})";
                var pkg = new SkinPackage { Folder = dir, Name = $"{tName} with {(v.IsNormal ? "the normal " + s.Name : v.Label)}'s looks" };
                pkg.StageFiles.Add(file);
                var plans = new List<StagePlan>();
                string group = Guid.NewGuid().ToString("N");
                foreach (var f in t.Files)
                {
                    var target = Stages.Describe(f);
                    var plan = _installer.PlanLooks(card, pkg, file, target);
                    if (!plan.CanInstall) return (string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                    plan.PackageKey = pkgKey; plan.Group = t.Files.Count > 1 ? group : null; plan.Name = pkg.Name;
                    plans.Add(plan);
                }
                return (null, null, plans, plans[0].Bytes);
            }
        }

        /// <summary>Turns the choice into the change to make (or, adding a stage, the plans to install) and closes with
        /// OK.</summary>
        void Use()
        {
            if (_pkg != null)
            {
                // every file of the download that has versions ticked (and passed), all at once
                var all = _new.Where(n => n.Picked.Count > 0 && n.Plans != null).SelectMany(n => n.Plans).ToList();
                if (all.Count == 0 || _new.Any(n => n.Picked.Count > 0 && (n.Checking || n.Plans == null))) return;
                Plans = all;
                DialogResult = DialogResult.OK; Close();
                return;
            }
            var s = _slot; var v = _look;
            if (s == null || _chosen.Count == 0) return;
            var change = new Installer.Change();
            void Remove(InstalledStage x) { if (!change.StageRemove.Contains(x)) change.StageRemove.Add(x); }
            foreach (var t in _chosen) foreach (var x in Current(s, t)) Remove(x);   // these versions' looks set by us before: replaced by this choice
            string tName = _chosen.Count == 1 ? (_chosen[0].IsNormal ? "the normal " + s.Name : $"{s.Name}'s {_chosen[0].Label}") : $"{_chosen.Count} versions of {s.Name}";
            if (_mod != null)
            {
                bool moving = _move.Visible && _move.Checked;
                foreach (var t in _chosen.Where(t => t != _mod.On))
                {
                    if (!_checked.TryGetValue("mod|" + _mod.First.Id + "|" + t.Spot.Entry, out var mr) || mr.Looks == null) return;
                    // whatever else is on these versions now (another mod) is turned off by the install itself
                    change.StageInstall.AddRange(mr.Looks);
                }
                if (moving) foreach (var e in _mod.Entries) Remove(e);
                string onName = _mod.On.IsNormal ? "the normal " + s.Name : $"{s.Name}'s {_mod.On.Label}";
                Done = moving ? $"Done. {_mod.Name} is on {tName} now, and {onName} looks the way P+ made it again. You still play on P+'s stage, so it's safe online."
                              : $"Done. {_mod.Name} is on {tName} too. You still play on P+'s stage, so it's safe online.";
                Change = change; DialogResult = DialogResult.OK; Close();
                return;
            }
            if (_undo) Done = $"Done. {Cap(tName)} {(_chosen.Count == 1 ? "looks" : "look")} the way P+ made {(_chosen.Count == 1 ? "it" : "them")} again.";
            else
            {
                if (v == null) return;
                bool swapOk = _chosen.Count == 1, swapped = false; int clashed = 0;
                foreach (var t in _chosen)
                {
                    if (!_checked.TryGetValue(Key(s, t, v) + (swapOk ? "" : "|looks"), out var r) || (r.Swap == null && r.Looks == null)) return;
                    // a real swap moves versions around: looks set on either of the two swapped versions are taken off first
                    var others = r.Swap != null ? AnyCurrent(s).Where(x => x.PackageKey.Contains("|@" + v.Spot.Entry + "|")).ToList()
                               : !t.IsNormal ? Current(s, s.Normal).Where(x => x.IsSwap && x.PackageKey.EndsWith("#" + t.Spot.Entry, StringComparison.OrdinalIgnoreCase)).ToList()
                               : new List<InstalledStage>();
                    foreach (var o in others) { if (!change.StageRemove.Contains(o)) clashed++; Remove(o); }
                    if (r.Swap != null) { change.StageInstall.Add(r.Swap); swapped = true; }
                    else change.StageInstall.AddRange(r.Looks);
                }
                string note = clashed > 0 ? " (a change you made before that clashed with it was taken off)" : "";
                Done = swapped ? $"Done. Just picking {s.Name} now gives {v.Label}; {v.Spot.HowToPick} gives the normal one. They play exactly alike, so it's safe online{note}."
                               : $"Done. {Cap(tName)} now {(_chosen.Count == 1 ? "has" : "have")} {(v.IsNormal ? "the normal " + s.Name : v.Label)}'s scenery and still play{(_chosen.Count == 1 ? "s" : "")} exactly like P+'s, so it's safe online{note}. Try it offline once to check it looks right.";
            }
            Change = change;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
