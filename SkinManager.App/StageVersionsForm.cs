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
    class StageVersionsForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;

        /// <summary>What to do when it closes with OK (null: nothing).</summary>
        public Installer.Change Change { get; private set; }
        public string Done { get; private set; }

        sealed class Version
        {
            public StageSpot Spot;                        // the first way to get it (its button, its settings)
            public List<string> Files = new List<string>();
            public string Name;                           // "Hold L: SV Sakura"
            public string Label;                          // "SV Sakura"
            public string Badge;                          // "Normal", "Hold L", "L list"
            public bool IsNormal;
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
        readonly Label _step1 = Theme.Label("1. Which version do you want to change?", Theme.Heading), _step2 = Theme.Label("2. What should it look like?", Theme.Heading);
        // the versions wrap onto more rows (Smashville has 13): the page scrolls, every choice stays in view
        readonly FlowLayoutPanel _targets = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
        readonly FlowLayoutPanel _looks = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
        readonly Label _status = Theme.Label("", Theme.Body, Theme.Muted);
        readonly PictureBox _pic = new PictureBox { Size = new Size(352, 198), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Visible = false };
        readonly Label _picCap = Theme.Label("How it will look", Theme.Small, Theme.Muted);
        readonly Button _use = Theme.Primary("Use this look");
        readonly Label _hint = Theme.Label("", Theme.Body, Theme.Muted);

        readonly Dictionary<string, (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture)> _checked = new Dictionary<string, (string, StagePlan, List<StagePlan>, byte[])>();
        readonly Dictionary<string, Bitmap> _renders = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int, string), Bitmap> _thumbs = new Dictionary<(int, string), Bitmap>();
        Dictionary<string, (string TrackList, string Module)> _info;
        List<Slot> _slots = new List<Slot>();
        Slot _slot; Version _target, _look; Mod _mod; bool _undo; int _asking;
        readonly CheckBox _move = new CheckBox { AutoSize = true, Checked = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.Body, Visible = false };

        public StageVersionsForm(Installer installer, Library lib)
        {
            _installer = installer; _lib = lib;
            Text = "Stage versions";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1180, 820); MinimumSize = new Size(940, 640);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var title = Theme.Label("Stage versions: choose how each one looks", Theme.Title); title.Location = new Point(24, 14);
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
            foot.Resize += (s, e) => close.Location = new Point(foot.Width - close.Width - 24, (foot.Height - close.Height) / 2);
            Controls.AddRange(new Control[] { title, _hint, _search, _stageTiles, _stagePage, foot });
            Resize += (s, e) => Lay();
            Lay();
            Shown += async (s, e) => await LoadStages();
        }

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
            _status.SetBounds(0, y, Math.Max(200, _pic.Left - 20), 110);
            _move.Location = new Point(0, y + 120);
            _use.Location = new Point(0, y + 152);
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
            foreach (var g in _lib.Stages.Where(x => !x.IsSong && !x.IsSwap && !(x.PackageKey ?? "").StartsWith("version|", StringComparison.OrdinalIgnoreCase))
                         .Select(x => (x, On: s.All.FirstOrDefault(v => v.Files.Any(f => string.Equals(f, x.CardPath, StringComparison.OrdinalIgnoreCase)))))
                         .Where(x => x.On != null)
                         .GroupBy(x => (x.x.Group ?? x.x.PackageKey ?? x.x.Id) + "|" + x.On.Spot.Entry))
                result.Add(new Mod { Entries = g.Select(x => x.x).ToList(), On = g.First().On });
            return result.OrderByDescending(m => m.Enabled).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The mod of yours that's on a version now (null: none).</summary>
        Mod ModOn(Slot s, Version v) => Mods(s).FirstOrDefault(m => m.On == v && m.Enabled);

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
                        var all = select.All.Where(x => x.Page > 0).ToList();
                        var result = new List<Slot>();
                        foreach (var g in all.GroupBy(x => x.Slot).OrderBy(g => g.First().Page).ThenBy(g => g.First().Row).ThenBy(g => g.First().Column))
                        {
                            var versions = g.GroupBy(x => x.Entry).OrderBy(e => e.Key).Select(e => new Version
                            {
                                Spot = e.First(), Files = e.Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
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
                            if (slot.Others.Count > 0) result.Add(slot);
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
        }

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
            _slot = s; _target = null; _look = null; _undo = false;
            _stageTiles.Visible = _search.Visible = false; _stagePage.Visible = true;
            _stageName.Text = s.Name;
            FillTargets();
            _looks.Controls.Clear(); _step2.Visible = false;
            _status.Text = "Pick the version you want to change. (In matchmaking, the game can give you any of them.)"; _status.ForeColor = Theme.Muted;
            _use.Enabled = false; _use.Visible = false; _pic.Visible = _picCap.Visible = false;
            _stagePage.AutoScrollPosition = Point.Empty;
            LayPage();
            RenderVersions(s);
        }

        PictureTile VersionTile(Slot s, Version v, string sub)
        {
            var tile = new PictureTile(214, 146) { Value = v, Title = v.IsNormal ? "Normal " + s.Name : v.Label, Badge = v.Badge, Subtitle = sub };
            tile.Picture = _renders.TryGetValue(v.Files[0], out var r) ? r : Thumb(s, v);
            return tile;
        }

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
                tile.Selected = v == _target;
                var version = v;
                tile.Click += (o, e) => PickTarget(version);
                _targets.Controls.Add(tile);
            }
        }

        void PickTarget(Version t)
        {
            var s = _slot; _target = t; _look = null; _mod = null; _undo = false;
            foreach (var tile in _targets.Controls.OfType<PictureTile>()) tile.Selected = tile.Value == t;
            _step2.Visible = true;
            _step2.Text = $"2. What should {(t.IsNormal ? "the normal " + s.Name : t.Label)} look like?";
            foreach (Control c in _looks.Controls.Cast<Control>().ToList()) c.Dispose();
            _looks.Controls.Clear();
            var now = LookNow(s, t);
            // its own look (P+'s): picking it undoes a change
            var own = VersionTile(s, t, now == null ? "What it looks like now" : "Back to how P+ made it");
            own.Title = "P+'s own look"; own.Badge = now == null ? "Now" : null;
            own.Click += (o, e) => PickLook(null);
            _looks.Controls.Add(own);
            foreach (var v in s.All.Where(v => v != t))
            {
                var tile = VersionTile(s, v, v.IsNormal ? "The normal stage's scenery" : (v.Badge.EndsWith("list") ? "From its list" : HowTo(v)) + "'s scenery");
                if (now == v) tile.Badge = "Now";
                var version = v;
                tile.Click += (o, e) => PickLook(version);
                _looks.Controls.Add(tile);
            }
            foreach (var m in Mods(s))
            {
                bool here = m.On == t;
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
            _status.Text = "Pick the look you want: another version's, or one of your stage mods."; _status.ForeColor = Theme.Muted;
            _use.Visible = true; _use.Enabled = false; _move.Visible = false; _pic.Visible = _picCap.Visible = false;
            LayPage();
            _stagePage.ScrollControlIntoView(_step2);
        }

        async void PickMod(Mod m)
        {
            var s = _slot; var t = _target; if (t == null) return;
            int me = ++_asking;
            _look = null; _mod = m; _undo = false;
            foreach (var tile in _looks.Controls.OfType<PictureTile>()) tile.Selected = tile.Value == m;
            _use.Enabled = false; _move.Visible = false; _pic.Visible = _picCap.Visible = false; _use.Text = "Use this mod here";
            string tName = t.IsNormal ? "the normal " + s.Name : t.Label, onName = m.On.IsNormal ? "the normal " + s.Name : m.On.Label;
            if (m.On == t)
            {
                _status.Text = m.Enabled ? $"{m.Name} is on {tName} now." : $"{m.Name} is on {tName} but turned off: turn it on in My Mods.";
                _status.ForeColor = Theme.Muted; return;
            }
            string key = "mod|" + m.First.Id + "|" + t.Spot.Entry;
            if (!_checked.TryGetValue(key, out var r))
            {
                _status.Text = $"Putting {m.Name} on {tName} and checking it plays exactly like P+'s…"; _status.ForeColor = Theme.Muted;
                try { r = await Task.Run(() => CheckMod(s, t, m)); }
                catch (Exception e) { r = (Friendly.Explain(e), null, null, null); }
                _checked[key] = r;
                if (IsDisposed || me != _asking) return;
            }
            if (r.Looks == null) { _status.Text = $"{m.Name} can't go on {tName}: {r.Why}."; _status.ForeColor = Theme.Bad; return; }
            _move.Text = $"Take it off {onName} (move it here)";
            _move.Visible = m.Enabled;
            _status.Text = $"{m.Name} goes on {tName}" + (t.Files.Count > 1 ? $" (all {t.Files.Count} of its files)" : "") +
                           $". You still play on {tName} exactly as P+ made it, so it's safe online." +
                           (m.Enabled ? $" Tick the box to take it off {onName} (it goes back to P+'s look there), or untick it to have it on both." : "");
            _status.ForeColor = Theme.Good;
            _use.Enabled = true;
            _stagePage.ScrollControlIntoView(_use);
            if (r.Picture != null)
            {
                Bitmap shot = null;
                try { var bytes = r.Picture; shot = await Task.Run(() => ModelPreview.RenderStage(bytes, 480, 270)); } catch (Exception e) { Program.Trace("preview: " + e.Message); }
                if (IsDisposed || me != _asking) return;
                var old = _pic.Image; _pic.Image = shot; old?.Dispose();
                _pic.Visible = _picCap.Visible = shot != null;
            }
        }

        /// <summary>A mod of yours on another version: made to play like P+'s version there (its gameplay copied in, when
        /// the mod is for the same stage program), else its looks on P+'s file for that version.</summary>
        (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture) CheckMod(Slot s, Version t, Mod m)
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
                    plan = plan ?? _installer.PlanLooks(card, pkg, file, target);
                    if (!plan.CanInstall) return (string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                    plan.Name = m.Name; plan.PackageKey = m.First.PackageKey; plan.Group = group;
                    plan.Source = source; plan.SourcePath = m.First.SourcePath;
                    foreach (var kv in pictures) plan.SelectPictures[kv.Key] = kv.Value;
                    plans.Add(plan);
                }
            }
            return (null, null, plans, plans[0].Bytes);
        }

        async void PickLook(Version v)
        {
            var s = _slot; var t = _target; if (t == null) return;
            int me = ++_asking;
            _look = v; _mod = null; _undo = v == null; _move.Visible = false;
            foreach (var tile in _looks.Controls.OfType<PictureTile>()) tile.Selected = v == null ? tile.Title == "P+'s own look" : tile.Value == v;
            _use.Enabled = false; _pic.Visible = _picCap.Visible = false;
            string tName = t.IsNormal ? "the normal " + s.Name : t.Label;
            if (v == null)
            {
                bool changed = Current(s, t).Any(x => x.Enabled);
                _status.Text = changed ? $"{char.ToUpper(tName[0]) + tName.Substring(1)} goes back to how P+ made it." : "That's how it looks now.";
                _status.ForeColor = Theme.Muted; _use.Enabled = changed; _use.Text = "Put P+'s look back";
                return;
            }
            _use.Text = "Use this look";
            string key = Key(s, t, v);
            if (!_checked.TryGetValue(key, out var r))
            {
                _status.Text = $"Putting {(v.IsNormal ? "the normal one" : v.Label)}'s look on {tName} and checking it plays exactly like P+'s…"; _status.ForeColor = Theme.Muted;
                try { r = await Task.Run(() => Check(s, t, v)); }
                catch (Exception e) { r = (Friendly.Explain(e), null, null, null); }
                _checked[key] = r;
                if (IsDisposed || me != _asking) return;
            }
            string vName = v.IsNormal ? "the normal " + s.Name : v.Label;
            if (r.Swap != null)
            {
                _status.Text = $"{Cap(vName)} plays exactly like {tName}, so they're swapped for real: just picking {s.Name} gives {v.Label}, and {v.Spot.HowToPick} gives the normal one. Only what you see changes, so it's safe online.";
                _status.ForeColor = Theme.Good;
            }
            else if (r.Looks != null)
            {
                _status.Text = $"{Cap(tName)} gets {vName}'s scenery" + (t.Files.Count > 1 ? $" (all {t.Files.Count} of its files)" : "") +
                               $". You still play on {tName} exactly as P+ made it (same floors, platforms and blast zones), so it's safe online. Try it offline once to check the scenery sits right.";
                _status.ForeColor = Theme.Good;
            }
            else { _status.Text = $"{Cap(vName)}'s look can't go on {tName}: {r.Why}."; _status.ForeColor = Theme.Bad; }
            _use.Enabled = r.Swap != null || r.Looks != null;
            _stagePage.ScrollControlIntoView(_use);
            if (r.Picture != null)
            {
                Bitmap shot = null;
                try { var bytes = r.Picture; shot = await Task.Run(() => ModelPreview.RenderStage(bytes, 480, 270)); } catch (Exception e) { Program.Trace("preview: " + e.Message); }
                if (IsDisposed || me != _asking) return;
                var old = _pic.Image; _pic.Image = shot; old?.Dispose();
                _pic.Visible = _picCap.Visible = shot != null;
            }
        }

        static string HowTo(Version v) { var h = (v.Spot.HowToPick ?? "").Trim(); return h == "" || h == "hold" ? "a button combination" : h; }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Draws each version (its first file) in the background, for its tile.</summary>
        async void RenderVersions(Slot s)
        {
            foreach (var v in s.All)
            {
                if (_slot != s || IsDisposed) return;
                string f = v.Files[0];
                if (_renders.ContainsKey(f)) continue;
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

        /// <summary>Works out what giving a version another's look would do: a real swap (the normal one, when they play
        /// alike), the look's scenery on the version's files, or why neither.</summary>
        (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture) Check(Slot s, Version t, Version v)
        {
            using (var card = _installer.OpenCard())
            {
                byte[] Read(string f) => _lib.HasOriginalFile(f) ? _lib.OriginalFile(f) : card.ReadFile(f);
                string pkgKey = KeyFor(s, t, v);
                if (t.IsNormal && _installer.PlayAlike(card, s.Normal.Files, v.Files, _info) == null)
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

        void Use()
        {
            var s = _slot; var t = _target; var v = _look;
            if (s == null || t == null) return;
            var change = new Installer.Change();
            change.StageRemove.AddRange(Current(s, t));   // this version's look set by us before: replaced by this choice
            if (_mod != null)
            {
                if (!_checked.TryGetValue("mod|" + _mod.First.Id + "|" + t.Spot.Entry, out var mr) || mr.Looks == null) return;
                // whatever else is on this version now (another mod) is turned off by the install itself
                change.StageInstall.AddRange(mr.Looks);
                bool moving = _move.Visible && _move.Checked;
                if (moving) foreach (var e in _mod.Entries) if (!change.StageRemove.Contains(e)) change.StageRemove.Add(e);
                string onName = _mod.On.IsNormal ? "the normal " + s.Name : $"{s.Name}'s {_mod.On.Label}", here = t.IsNormal ? "the normal " + s.Name : $"{s.Name}'s {t.Label}";
                Done = moving ? $"Done. {_mod.Name} is on {here} now, and {onName} looks the way P+ made it again. You still play on P+'s stage, so it's safe online."
                              : $"Done. {_mod.Name} is on {here} too. You still play on P+'s stage, so it's safe online.";
                Change = change; DialogResult = DialogResult.OK; Close();
                return;
            }
            string tName = t.IsNormal ? "the normal " + s.Name : $"{s.Name}'s {t.Label}";
            if (_undo) Done = $"Done. {Cap(tName)} looks the way P+ made it again.";
            else
            {
                if (v == null || !_checked.TryGetValue(Key(s, t, v), out var r) || (r.Swap == null && r.Looks == null)) return;
                // a real swap moves versions around: looks set on either of the two swapped versions are taken off first
                var others = r.Swap != null ? AnyCurrent(s).Where(x => x.PackageKey.Contains("|@" + v.Spot.Entry + "|")).ToList()
                           : !t.IsNormal ? Current(s, s.Normal).Where(x => x.IsSwap && x.PackageKey.EndsWith("#" + t.Spot.Entry, StringComparison.OrdinalIgnoreCase)).ToList()
                           : new List<InstalledStage>();
                foreach (var o in others) if (!change.StageRemove.Contains(o)) change.StageRemove.Add(o);
                string note = others.Count > 0 ? " (a change you made before that clashed with it was taken off)" : "";
                if (r.Swap != null) { change.StageInstall.Add(r.Swap); Done = $"Done. Just picking {s.Name} now gives {v.Label}; {v.Spot.HowToPick} gives the normal one. They play exactly alike, so it's safe online{note}."; }
                else { change.StageInstall.AddRange(r.Looks); Done = $"Done. {Cap(tName)} now has {(v.IsNormal ? "the normal " + s.Name : v.Label)}'s scenery and still plays exactly like P+'s, so it's safe online{note}. Try it offline once to check it looks right."; }
            }
            Change = change;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
