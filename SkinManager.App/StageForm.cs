// Adding stage reskins: for each stage file in the download, the app finds every P+ stage it plays
// exactly like (same floors, platforms, blast zones and moving parts) and lets you pick which of
// those it replaces. Stages that would play differently can't be installed.
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
    class StageForm : Form
    {
        /// <summary>"(the stage is 160 wide)", when it's known.</summary>
        static string Wide(StageCheck.Fit fit) => fit != null && fit.Width >= 1 ? $" (the stage is {fit.Width:0} wide)" : "";

        /// <summary>One plain answer: does what you see line up with where you stand? (Measured between the mod's own floors
        /// and P+'s, which are the ones you stand on.)</summary>
        static (string Text, Color Color, bool Good) Verdict(StageCheck.Fit fit)
        {
            if (fit == null || fit.Floors >= 1e6f || float.IsNaN(fit.Floors))
                return ("?  Can't be measured here (you stand on moving parts). Compare the two pictures.", Theme.Warn, false);
            if (fit.Floors < 1f && fit.Rest < 5f) return ("✓  Lines up: you stand right on the mod's ground.", Theme.Good, true);
            if (fit.Floors < 1f) return ("✓  You stand right on the mod's ground. Some edges are a little off, which is hard to notice.", Theme.Good, true);
            if (fit.Floors < 4f) return ($"⚠  Slightly off: you'll stand up to {fit.Floors:0} units above or inside the mod's ground (a fighter is about 15 tall). See the red line.", Theme.Warn, false);
            return ($"✗  Doesn't line up: you'll stand up to {fit.Floors:0} units away from the mod's ground (a fighter is about 15 tall). See the red line.", Theme.Bad, false);
        }

        readonly Installer _installer;
        readonly Library _lib;
        readonly SkinPackage _pkg;
        public List<StagePlan> Plans { get; private set; } = new List<StagePlan>();

        sealed class Row
        {
            public string File;
            public Dictionary<string, string> Gameplay;
            public FlowLayoutPanel Targets;
            public Label Status, OtherLabel;
            public LinkLabel Why;
            public ComboBox Other;
            public List<CheckBox> Boxes = new List<CheckBox>();
            public HashSet<string> Tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<(CheckBox box, int score)> Candidates = new List<(CheckBox, int)>();
            // stage select picture
            public Panel Pic; public PictureBox PicBox; public Label PicText; public LinkLabel PicChange, PicSkip;
            public string CustomPicture;   // a picture the user picked
            public bool NoPicture;         // keep P+'s picture
            public string PicFor;          // the stage the shown picture is for
        }

        sealed class Fix { public StageFile Stage; public StagePlan Plan; public bool Looks; }
        /// <summary>A stage's normal version made of several files (Smashville: the game picks one of five by the time of day):
        /// put on all of them together, as one mod.</summary>
        sealed class Group { public int Slot; public string Name; public List<(StageFile Stage, Fix Fix)> Parts = new List<(StageFile, Fix)>(); }

        readonly List<Row> _rows = new List<Row>();
        readonly Button _install = Theme.Primary("Install");
        readonly Label _summary = Theme.Label("", Theme.Body, Theme.Muted);
        Action _layoutFoot = () => { };
        bool _checking = true;
        Dictionary<string, (string TrackList, string Module)> _info;
        Dictionary<string, uint> _modules;   // program numbers of P+'s stage programs
        List<StageFile> _all;
        StageSelect _select;                 // where P+'s stages are on the stage select
        StageSelect _front;                  // the same as P+ set it up (a stage swap of ours undone): which version is the normal one
        Dictionary<string, Bitmap> _previews = new Dictionary<string, Bitmap>();   // P+'s preview pictures, for showing the change
        readonly HashSet<string> _ticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // stages already ticked by default (one file each)

        public StageForm(Installer installer, Library lib, SkinPackage pkg)
        {
            _installer = installer; _lib = lib; _pkg = pkg;
            Text = "Add a stage: " + pkg.Name;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(980, 700); MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var head = new Panel { Dock = DockStyle.Top, Height = 96 };
            var title = Theme.Label(pkg.StageFiles.Count == 1 ? "Which stage should it replace?" : $"This download has {pkg.StageFiles.Count} stage files", Theme.Title);
            title.Location = new Point(24, 14);
            var hint = new Label
            {
                Text = "Pick which of P+'s stages it replaces. Every choice here plays exactly like P+'s stage, so it's safe online. Each one shows P+'s stage next to how it will look, and whether what you see lines up with where you stand.",
                Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(26, 50), Size = new Size(920, 44), BackColor = Color.Transparent,
            };
            head.Controls.Add(title); head.Controls.Add(hint);
            head.Resize += (s, e) => hint.Width = head.Width - 50;

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 4, 24, 4) };
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Panel };
            _summary.Location = new Point(24, 22); _summary.AutoSize = false; _summary.Size = new Size(560, 22);
            var cancel = Theme.Secondary("Cancel");
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _install.Click += async (s, e) => await Install();
            foot.Controls.AddRange(new Control[] { _summary, cancel, _install });
            _layoutFoot = () =>
            {
                _install.Location = new Point(foot.Width - _install.Width - 24, (foot.Height - _install.Height) / 2);
                cancel.Location = new Point(_install.Left - cancel.Width - 10, (foot.Height - cancel.Height) / 2);
            };
            foot.Resize += (s, e) => _layoutFoot();
            _install.SizeChanged += (s, e) => _layoutFoot();
            Controls.Add(list); Controls.Add(foot); Controls.Add(head);

            foreach (var f in pkg.StageFiles)
            {
                var r = new Row { File = f };
                var panel = new Panel { Width = 900, Height = 120, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(14) };
                var name = Theme.Label(Path.GetFileName(f), Theme.Heading); name.Location = new Point(14, 10);
                var size = Theme.Label($"{new FileInfo(f).Length / 1024:N0} KB" + (pkg.StageFiles.Count > 1 && Path.GetDirectoryName(f) != pkg.Folder ? "  •  folder: " + Path.GetFileName(Path.GetDirectoryName(f)) : ""), Theme.Small, Theme.Muted);
                size.Location = new Point(16, 36); size.AutoSize = false; size.Size = new Size(TextRenderer.MeasureText(size.Text, size.Font).Width + 12, 18);
                r.Status = new Label { Text = "Comparing it with P+'s stages...", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(16, 58), Size = new Size(860, 20), BackColor = Color.Transparent };
                r.Targets = new FlowLayoutPanel { Location = new Point(14, 82), Size = new Size(870, 24), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
                r.OtherLabel = Theme.Label("Put it on a different stage:", Theme.Small, Theme.Muted);
                r.OtherLabel.AutoSize = false;
                r.OtherLabel.Size = new Size(TextRenderer.MeasureText(r.OtherLabel.Text, r.OtherLabel.Font).Width + 8, 20);
                r.Other = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, BackColor = Theme.Back, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, MaxDropDownItems = 24, Enabled = false };
                r.OtherLabel.Visible = r.Other.Visible = false;
                r.Why = new LinkLabel { Text = "What's different from P+'s stage?", AutoSize = false, Font = Theme.Small, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, BackColor = Color.Transparent, Visible = false };
                r.Why.Size = new Size(TextRenderer.MeasureText(r.Why.Text, r.Why.Font).Width + 40, 20);
                var row = r;
                r.Other.SelectionChangeCommitted += async (s, e) => { if (r.Other.SelectedItem is StageFile st) await TryStage(row, st); };
                r.Why.LinkClicked += (s, e) => ShowDifferences(row);
                MakePictureStrip(r);
                panel.Controls.AddRange(new Control[] { name, size, r.Status, r.Targets, r.OtherLabel, r.Other, r.Why, r.Pic });
                list.Controls.Add(panel);
                _rows.Add(r);
            }
            list.Resize += (s, e) => { foreach (Control c in list.Controls) c.Width = Math.Max(600, list.ClientSize.Width - 50); foreach (var r in _rows) Relayout(r); };
            Shown += async (s, e) => await Check();
            Update();
        }

        async Task Check()
        {
            foreach (var r in _rows)
            {
                List<StageFile> matches; Exception error = null;
                var row = r;
                try
                {
                    var progress = new Progress<(int, int)>(p => { if (!IsDisposed) row.Status.Text = $"Comparing it with P+'s stages... {p.Item1 * 100 / Math.Max(1, p.Item2)}% (only slow the first time)"; });
                    IProgress<(int, int)> pr = progress;
                    matches = await Task.Run(() =>
                    {
                        row.Gameplay = StageCheck.Gameplay(File.ReadAllBytes(row.File));
                        using (var card = _installer.OpenCard())
                        {
                            if (_info == null) _info = Songs.StageInfo(card);
                            if (_modules == null) _modules = Stages.ModuleIds(card);
                            if (_all == null) _all = Stages.All(card);
                            if (_select == null) try { _select = StageSelect.Read(card); _front = _installer.OriginalSelect(card); } catch (Exception e) { Program.Trace("stage select: " + e); }
                            return _installer._stageIndex.Matches(card, _lib, row.Gameplay, (i, n) => pr.Report((i, n)), Allowed(row));
                        }
                    });
                }
                catch (Exception e) { matches = new List<StageFile>(); error = e; }
                if (IsDisposed) return;
                if (error != null) { r.Status.Text = "Couldn't read this stage file: " + Friendly.Explain(error); r.Status.ForeColor = Theme.Bad; Relayout(r); continue; }

                // the stages it's named for come first
                var named = Stages.NamedFor(r.File, _pkg.Name, _all);
                int Rank(StageFile s) { int i = named.FindIndex(b => string.Equals(b, s.Base, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; }
                // the normal version (what you get by just picking the stage) first. When it's several files (Smashville's
                // five times of day) it's offered as one choice that goes on all of them, so the map shows whenever you pick it.
                var bases = named.Take(3).Concat(matches.OrderBy(Rank).Select(m => m.Base)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
                var exactFiles = new HashSet<string>(matches.Select(m => m.CardPath), StringComparer.OrdinalIgnoreCase);
                var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in bases)
                {
                    var front = FrontOf(b);
                    if (front.Files.Count < 2) continue;
                    foreach (var f in front.Files) grouped.Add(f.CardPath);
                    await AddGroup(r, front.Slot, front.Name, front.Files, exactFiles, Rank(front.Files[0]) < 99);
                    if (IsDisposed) return;
                }
                foreach (var m in matches.Where(m => !grouped.Contains(m.CardPath)).OrderBy(Rank).ThenBy(m => IsFront(m) ? 0 : 1).ThenBy(m => Stages.SameNumber(r.File, m) ? 0 : 1)) AddExact(r, m, Rank(m) < 99);
                // per stage it's named for: its normal version, even when the map plays exactly like another version
                foreach (var b in named.Take(3))
                {
                    var versions = _all.Where(s => string.Equals(s.Base, b, StringComparison.OrdinalIgnoreCase) && !grouped.Contains(s.CardPath))
                        .OrderBy(s => IsFront(s) ? 0 : 1).ThenBy(s => Stages.SameNumber(r.File, s) ? 0 : 1).ToList();
                    if (versions.Count == 0) continue;
                    var main = versions.FirstOrDefault(IsFront);
                    if (main != null) { if (!r.Tried.Contains(main.CardPath)) await TryFix(r, main, true); continue; }
                    if (versions.Any(v => r.Boxes.Any(x => TargetsOf(x).Contains(v)))) continue;   // already plays exactly like one of them
                    var s = versions.FirstOrDefault(Allowed(r)) ?? versions.FirstOrDefault();
                    if (s != null && !r.Tried.Contains(s.CardPath)) await TryFix(r, s, true);
                }
                // nothing usable yet: the closest of P+'s stages
                if (!r.Boxes.Any())
                {
                    r.Status.Text = "It doesn't play exactly like any P+ stage. Trying the closest ones...";
                    List<StageIndex.Near> near = new List<StageIndex.Near>();
                    try { near = await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer._stageIndex.Closest(card, _lib, row.Gameplay, Stages.Describe(row.File).Base, 6, Allowed(row)); }); }
                    catch (Exception e) { Program.Trace("closest failed: " + e); }
                    foreach (var n in near)
                    {
                        if (IsDisposed) return;
                        if (r.Boxes.Count >= 3) break;
                        if (!r.Tried.Contains(n.Stage.CardPath)) await TryFix(r, n.Stage, false, quiet: true);
                    }
                }
                if (IsDisposed) return;
                // no stage it plays exactly like: its looks on P+'s stages shaped like it (any stage program)
                if (!r.Boxes.Any(b => b.Tag is StageFile))
                {
                    r.Status.Text = "Looking for P+ stages shaped like it...";
                    List<(StageFile Stage, StageCheck.Fit Fit)> shaped = new List<(StageFile, StageCheck.Fit)>();
                    try { shaped = await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer._stageIndex.ByShape(card, _lib, row.Gameplay, 3); }); }
                    catch (Exception e) { Program.Trace("by shape failed: " + e); }
                    foreach (var x in shaped)
                    {
                        if (IsDisposed) return;
                        bool fixedWell = r.Boxes.Any(b => b.Tag is Fix f && !f.Looks && f.Stage.CardPath == x.Stage.CardPath && f.Plan.Fit != null && f.Plan.Fit.Floors < 1f);
                        if (!fixedWell) await TryLooks(r, x.Stage, false, quiet: true);
                    }
                }
                if (IsDisposed) return;
                // tick the best one: what it's named for, playing exactly alike first
                var best = r.Candidates.OrderBy(c => c.score).FirstOrDefault();
                if (best.box != null && best.score < 50 && _ticked.Add(TargetOf(best.box).CardPath)) best.box.Checked = true;
                if (IsDisposed) return;
                bool exact = r.Boxes.Any(b => b.Tag is StageFile);
                r.Status.Text = exact ? "Plays exactly like P+'s stages below. Tick the one(s) it should replace:"
                    : r.Boxes.Any() ? "Made for an older P+ or another stage, so the app makes it safe for you. Pick one:"
                    : "It plays differently from P+'s stages, so it would make online games go out of sync, and the app couldn't fix it for the stages below. You can try another stage from the list." + AlreadyNote();
                r.Status.ForeColor = exact ? Theme.Good : r.Boxes.Any() ? Theme.Text : Theme.Bad;
                r.Other.Items.Clear();
                foreach (var s in _all.OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase)) r.Other.Items.Add(s);
                r.Other.Enabled = true; r.OtherLabel.Visible = r.Other.Visible = r.Why.Visible = true;
                Relayout(r);
                Update();
            }
            _checking = false;
            Update();
            if (Environment.GetEnvironmentVariable("SKINMANAGER_TEST_WHY") == "1" && _rows.Count > 0) ShowDifferences(_rows[0]);
        }

        /// <summary>When the download is a stage P+ already includes (Project M's Fountain of Sauria is P+'s Corneria, hold R), say so.</summary>
        string AlreadyNote()
        {
            var same = _all == null ? new List<StageFile>() : Stages.AlreadyInPPlus(_pkg.Name, _all);
            return same.Count == 0 ? "" : "\r\nGood news: P+ already includes this stage as " + string.Join(", ", same.Take(3).Select(x => x.Label) ) + "."
                + string.Concat(same.Take(3).Select(x => _select?.WhereText(x.CardPath)).Where(w => w != null).Select(w => "\r\nIn the game: " + w + "."));
        }

        /// <summary>"In the game: Smashville (page 1, row 3, 3rd icon), hold R".</summary>
        string WhereNote(StageFile s)
        {
            if (_select == null) return "";
            string w = _select.WhereText(s.CardPath);
            return w == null ? "\r\n    Not on the stage select (P+ only uses it in some modes)." : "\r\n    In the game: " + w + ".";
        }

        static StageFile TargetOf(CheckBox b) => b.Tag as StageFile ?? (b.Tag as Fix)?.Stage ?? (b.Tag as Group)?.Parts.FirstOrDefault().Stage;
        static List<StageFile> TargetsOf(CheckBox b) => b.Tag is Group g ? g.Parts.Select(p => p.Stage).ToList() : new List<StageFile> { TargetOf(b) };

        /// <summary>Is this the version you get by just picking the stage (as P+ set it up)?</summary>
        bool IsFront(StageFile s) => _front != null ? _front.SpotsOf(s.CardPath).Any(x => x.Button == 0 && x.Page > 0) : s.IsMain;

        /// <summary>A stage's normal version: its slot, name and the files it is (Smashville: its five times of day).</summary>
        (int Slot, string Name, List<StageFile> Files) FrontOf(string stageBase)
        {
            if (_front == null) return (0, null, new List<StageFile>());
            var spot = _all.Where(s => string.Equals(s.Base, stageBase, StringComparison.OrdinalIgnoreCase))
                .SelectMany(s => _front.SpotsOf(s.CardPath)).FirstOrDefault(x => x.Button == 0 && x.Page > 0);
            if (spot == null) return (0, null, new List<StageFile>());
            var files = _front.FrontFiles(spot.Slot).Select(f => _all.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s.CardPath), f, StringComparison.OrdinalIgnoreCase)))
                .Where(s => s != null).OrderBy(s => s.Suffix, StringComparer.OrdinalIgnoreCase).ToList();
            return (spot.Slot, spot.SlotName, files);
        }

        /// <summary>P+ stages this file may go on: ones run by the same stage program it was made for (another may
        /// look for parts the file doesn't have and crash the game).</summary>
        Func<StageFile, bool> Allowed(Row r)
        {
            string madeFor = Stages.MadeFor(r.File, _info, _modules);
            return s => Stages.SameProgram(madeFor, s.CardPath, _info);
        }

        void AddExact(Row r, StageFile m, bool named)
        {
            if (!r.Tried.Add(m.CardPath)) return;
            string key = Installer.StageKey(_pkg, r.File);
            bool done = _lib.Stages.Any(s => s.PackageKey == key && string.Equals(s.CardPath, m.CardPath, StringComparison.OrdinalIgnoreCase) &&
                ((_pkg.Source != null && s.GameBananaFileId == _pkg.Source.FileId && s.GameBananaFileId != 0) || string.Equals(s.SourcePath, _pkg.SourcePath, StringComparison.OrdinalIgnoreCase)));
            var mine = _lib.Stages.FirstOrDefault(s => s.Enabled && string.Equals(s.CardPath, m.CardPath, StringComparison.OrdinalIgnoreCase));
            var box = Box("Replace P+'s " + m.Label + (done ? "  (already yours)" : mine != null ? $"  (turns off {mine.Name})" : ""), m);
            AddOption(r, box, "Made for this stage: only the looks change. Safe online." + WhereNote(m), Theme.Muted, ("✓  Lines up: it has exactly P+'s floors.", Theme.Good, true));
            if (r.Boxes.Count(b => b.Tag is StageFile) == 1) _ = AddPreview(r, m, File.ReadAllBytes(r.File), false);
            if (!done) r.Candidates.Add((box, (named ? 0 : 20) + (IsFront(m) ? -2 : Stages.SameNumber(r.File, m) ? -1 : 1)));
        }

        CheckBox Box(string text, object tag)
        {
            var box = new CheckBox { Text = text, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body, Tag = tag, Margin = new Padding(0, 4, 0, 0) };
            box.CheckedChanged += (s, e) => Update();
            return box;
        }

        void AddOption(Row r, Control top, string note, Color color, (string Text, Color Color, bool Good)? verdict = null)
        {
            if (top is CheckBox cb) r.Boxes.Add(cb);
            r.Targets.Controls.Add(top);
            if (verdict != null)
                r.Targets.Controls.Add(new Label { Text = verdict.Value.Text, Font = Theme.BodyBold, ForeColor = verdict.Value.Color, AutoSize = false, BackColor = Color.Transparent, Margin = new Padding(20, 0, 0, 0) });
            if (!string.IsNullOrEmpty(note))
                r.Targets.Controls.Add(new Label { Text = note.Replace("\r\n    ", "\r\n"), Font = Theme.Small, ForeColor = color, AutoSize = false, BackColor = Color.Transparent, Margin = new Padding(20, 0, 0, 4) });
            Relayout(r);
        }

        /// <summary>Tries to make the file play like one of P+'s stages and adds the result as an option (or says why it can't).</summary>
        async Task TryFix(Row r, StageFile s, bool named, bool quiet = false)
        {
            if (!r.Tried.Add(s.CardPath)) return;
            if (!Allowed(r)(s))
            {
                // made for another stage program: as it is it could crash the game there, but its looks can go on P+'s file
                await TryLooks(r, s, named, quiet);
                return;
            }
            string before = r.Status.Text;
            r.Status.Text = "Trying to make it play like P+'s " + s.Label + "...";
            StagePlan plan = null; string problem = null;
            try { plan = await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.PlanFixedStage(card, _pkg, r.File, s); }); }
            catch (Exception e) { Program.Trace("fix failed: " + e); problem = e.Message; }
            if (IsDisposed) return;
            r.Status.Text = before;
            // stages it merely came close to are only offered when it fixes well enough to be worth it
            if (quiet && (plan == null || !plan.CanInstall || plan.Fit == null || plan.Fit.Floors > 10f)) return;
            if (plan == null || !plan.CanInstall)
            {
                problem = problem ?? string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title));
                AddOption(r, Theme.Label("✗  Can't copy P+'s " + s.Label + " gameplay into it", Theme.Small, Theme.Muted), null, Theme.Muted);
                await TryLooks(r, s, named, quiet: false);
                return;
            }
            var mine = _lib.Stages.FirstOrDefault(x => x.Enabled && string.Equals(x.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase));
            var box = Box("Make it play like P+'s " + s.Label + " and replace it" + (mine != null ? $"  (turns off {mine.Name})" : ""), new Fix { Stage = s, Plan = plan });
            var fit = plan.Fit;
            if (fit != null && (fit.Floors >= 1e6f || float.IsNaN(fit.Floors))) fit = null;   // moving floors only: can't be measured
            var verdict = Verdict(fit);
            bool good = verdict.Good;
            string how = "P+'s floors and blast zones are copied into it, so it's safe online.";
            if (plan.LooksSwapped.Count > 0)
            {
                how += " Its moving platform looks like P+'s.";
                if (plan.LooksSwapped.All(x => x.StartsWith("+"))) how += " The mod's own platform may still show, standing still.";
            }
            AddOption(r, box, how + WhereNote(s), Theme.Muted, verdict);
            // the normal version comes first: people pick a stage's icon, not a button version of it
            r.Candidates.Add((box, good && IsFront(s) ? (named ? -3 : 25) : (good ? (named ? 10 : 30) : 60) - (Stages.SameNumber(r.File, s) ? 1 : 0)));
            await AddPreview(r, s, plan.Bytes, plan.LooksSwapped.Count > 0);
        }

        /// <summary>Puts the map's looks on P+'s own stage file and adds that as an option, with a preview.</summary>
        async Task TryLooks(Row r, StageFile s, bool named, bool quiet)
        {
            if (!r.Tried.Add("looks|" + s.CardPath)) return;
            string before = r.Status.Text;
            r.Status.Text = "Trying its looks on P+'s " + s.Label + "...";
            StagePlan plan = null; string problem = null;
            try { plan = await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.PlanLooks(card, _pkg, r.File, s); }); }
            catch (Exception e) { Program.Trace("looks failed: " + e); problem = e.Message; }
            if (IsDisposed) return;
            r.Status.Text = before;
            if (quiet && (plan == null || !plan.CanInstall || plan.Fit == null || plan.Fit.Floors > 10f)) return;
            if (plan == null || !plan.CanInstall)
            {
                problem = problem ?? string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title));
                AddOption(r, Theme.Label("✗  Can't put its looks on P+'s " + s.Label, Theme.Body, Theme.Muted), problem, Theme.Muted);
                return;
            }
            var mine = _lib.Stages.FirstOrDefault(x => x.Enabled && string.Equals(x.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase));
            var box = Box("Use its looks on P+'s " + s.Label + (mine != null ? $"  (turns off {mine.Name})" : ""), new Fix { Stage = s, Plan = plan, Looks = true });
            var fit = plan.Fit;
            if (fit != null && (fit.Floors >= 1e6f || float.IsNaN(fit.Floors))) fit = null;   // a stage you stand on moving parts of (Corneria's Great Fox): can't be measured
            var verdict = Verdict(fit);
            bool good = verdict.Good;
            string how = "It's P+'s own stage with the mod's scenery on it, so it's safe online.";
            if (plan.Looks != null && plan.Looks.KeptLooks.Count > 0) how += " Moving platforms look like P+'s.";
            if (plan.Looks != null && plan.Looks.StaticParts) how += " Some moving decorations may stand still.";
            if (!Allowed(r)(s)) how += " " + LooksWarning;
            AddOption(r, box, how + WhereNote(s), Theme.Muted, verdict);
            // ticked by itself only when it lines up and the mod was made for this stage (its program places the scenery as
            // the mod expects); otherwise ranked so the best one is first in the list
            r.Candidates.Add((box, good && Allowed(r)(s) ? (IsFront(s) ? (named ? 0 : 28) : 35) : 60 + (fit != null && fit.Exact ? 0 : good ? 5 : 20) + (IsFront(s) ? 0 : 1)));
            await AddPreview(r, s, plan.Bytes, plan.Looks != null && plan.Looks.KeptLooks.Count > 0);
        }

        const string LooksWarning = "It was made for another stage, whose program may place the scenery differently: check it once offline.";

        /// <summary>P+'s stage as it is now, next to how it will look: the scenery as the game shows it at the start, with
        /// where you stand in gold, and the mod's own ground in red wherever it isn't where you'll stand.</summary>
        async Task AddPreview(Row r, StageFile target, byte[] after, bool platformsArePPlus)
        {
            if (after == null) return;
            const int W = 400, H = 225;
            var box = new Panel { Size = new Size(2 * W + 16, H + 44), Margin = new Padding(20, 4, 0, 14), BackColor = Color.Transparent };
            Label Cap(string t, int x) => new Label { Text = t, Font = Theme.BodyBold, ForeColor = Theme.Text, AutoSize = false, Location = new Point(x, 0), Size = new Size(W, 20), BackColor = Color.Transparent };
            var leftPic = new PictureBox { Location = new Point(0, 22), Size = new Size(W, H), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var rightPic = new PictureBox { Location = new Point(W + 16, 22), Size = new Size(W, H), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var legend = new Label
            {
                Text = "Gold: where you'll stand.  Red: where the mod's ground is, where that's different (no red = it lines up)." + (platformsArePPlus ? "  Moving platforms are P+'s own." : ""),
                Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(0, H + 24), Size = new Size(2 * W + 16, 18), BackColor = Color.Transparent,
            };
            box.Controls.AddRange(new Control[] { Cap("P+'s " + (target?.Label ?? "stage") + " now", 0), Cap("With this mod", W + 16), leftPic, rightPic, legend });
            r.Targets.Controls.Add(box);
            Relayout(r);
            string modFile = r.File, path = target?.CardPath;
            Bitmap before = null, now = null;
            try
            {
                now = await Task.Run(() =>
                {
                    var theirs = StageCheck.SurfacesOf(File.ReadAllBytes(modFile));
                    return ModelPreview.RenderStage(after, W, H, theirs: theirs.Count > 0 ? theirs : null);
                });
            }
            catch (Exception e) { Program.Trace("preview: " + e.Message); }
            if (IsDisposed) return;
            if (now != null) rightPic.Image = now;
            if (path != null)
                try
                {
                    before = await Task.Run(() =>
                    {
                        byte[] orig = _lib.HasOriginalFile(path) ? _lib.OriginalFile(path) : null;
                        if (orig == null) using (var card = _installer.OpenCard()) orig = card.ReadFile(path);
                        return orig == null ? null : ModelPreview.RenderStage(orig, W, H);
                    });
                }
                catch (Exception e) { Program.Trace("preview of P+'s stage: " + e.Message); }
            if (IsDisposed) return;
            if (before != null) leftPic.Image = before;
            // no picture (a stage with nothing to stand on that the app can find, like Castle Siege's first part): say so in the
            // boxes rather than taking them away again, which made the list jump
            void NoPicture(PictureBox p) => p.Controls.Add(new Label { Text = "No picture of this one", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            if (now == null) NoPicture(rightPic);
            if (before == null) NoPicture(leftPic);
        }

        /// <summary>The map on all the files of a stage's normal version (Smashville's five times of day), as one choice.</summary>
        async Task AddGroup(Row r, int slot, string slotName, List<StageFile> members, HashSet<string> exact, bool named)
        {
            if (!r.Tried.Add("group|" + slot)) return;
            var allowed = Allowed(r);
            var g = new Group { Slot = slot, Name = slotName };
            string before = r.Status.Text, problem = null;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                r.Status.Text = $"Checking it on P+'s normal {slotName} ({i + 1} of {members.Count})...";
                if (exact.Contains(m.CardPath) && allowed(m)) { g.Parts.Add((m, null)); continue; }
                StagePlan plan = null;
                try
                {
                    plan = await Task.Run(() =>
                    {
                        using (var card = _installer.OpenCard())
                        {
                            if (allowed(m)) { var p = _installer.PlanFixedStage(card, _pkg, r.File, m); if (p.CanInstall) return p; }
                            return _installer.PlanLooks(card, _pkg, r.File, m);
                        }
                    });
                }
                catch (Exception e) { problem = Friendly.Explain(e); }
                if (IsDisposed) return;
                if (problem == null && !plan.CanInstall) problem = string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title));
                if (problem != null) break;
                g.Parts.Add((m, new Fix { Stage = m, Plan = plan, Looks = plan.Looks != null }));
            }
            r.Status.Text = before;
            string versions = string.Join(", ", members.Select(m => m.Version == "" ? m.StageName : m.Version));
            if (problem != null)
            {
                AddOption(r, Theme.Label("✗  P+'s normal " + slotName, Theme.Body, Theme.Muted), "Can't be done: " + problem, Theme.Muted);
                return;
            }
            var mine = _lib.Stages.Where(x => x.Enabled && members.Any(m => string.Equals(x.CardPath, m.CardPath, StringComparison.OrdinalIgnoreCase))).Select(x => x.Name).Distinct().ToList();
            var box = Box($"Replace P+'s normal {slotName} (all {members.Count} of its versions)" + (mine.Count > 0 ? $"  (turns off {string.Join(", ", mine.Take(2))})" : ""), g);
            bool looks = g.Parts.Any(p => p.Fix != null && p.Fix.Looks), fixd = g.Parts.Any(p => p.Fix != null && !p.Fix.Looks);
            var fits = g.Parts.Where(p => p.Fix?.Plan.Fit != null).Select(p => p.Fix.Plan.Fit).Where(f => f.Floors < 1e6f && !float.IsNaN(f.Floors)).ToList();
            float floors = fits.Count == 0 ? 0 : fits.Max(f => f.Floors), rest = fits.Count == 0 ? 0 : fits.Max(f => f.Rest);
            var verdict = !looks && !fixd ? ("✓  Lines up: it has exactly P+'s floors.", Theme.Good, true)
                        : Verdict(fits.Count == 0 ? null : new StageCheck.Fit { Floors = floors, Rest = rest });
            bool good = verdict.Item3;
            string how = $"Goes on all {members.Count} versions P+ picks from ({versions}), so it always shows. ";
            how += looks ? "It's P+'s own stage with the mod's scenery on it, so it's safe online."
                 : fixd ? "P+'s floors and blast zones are copied into it, so it's safe online."
                 : "Made for this stage: only the looks change. Safe online.";
            if (looks && !g.Parts.All(p => p.Fix == null || allowed(p.Stage))) how += " " + LooksWarning;
            var spot = _front?.SpotsOf(members[0].CardPath).FirstOrDefault(x => x.Button == 0);
            if (spot != null) how += "\r\n    In the game: " + spot.Where + ", just pick it.";
            AddOption(r, box, how, Theme.Muted, verdict);
            r.Candidates.Add((box, looks ? 60 : !fixd ? (named ? -10 : 15) : good ? (named ? -4 : 25) : 70));
            var first = g.Parts[0];
            await AddPreview(r, first.Stage, first.Fix != null ? first.Fix.Plan.Bytes : File.ReadAllBytes(r.File), first.Fix?.Plan.Looks?.KeptLooks.Count > 0);
        }

        /// <summary>"What's different?": exactly why the map doesn't play like one of P+'s stages.</summary>
        void ShowDifferences(Row r)
        {
            using (var f = new Form { Text = "What's different: " + Path.GetFileName(r.File), BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Body,
                Size = new Size(760, 520), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false })
            {
                var top = Theme.Label("Compare it with P+'s:", Theme.Body, Theme.Muted); top.Location = new Point(20, 20);
                var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, Location = new Point(190, 16), BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, MaxDropDownItems = 24 };
                var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(20, 56), Size = new Size(704, 360),
                    BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
                var close = Theme.Secondary("Close"); close.Location = new Point(620, 428); close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                close.Click += (s, e) => f.Close();
                f.Controls.AddRange(new Control[] { top, pick, text, close });
                var all = _all ?? new List<StageFile>();
                foreach (var st in all.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)) pick.Items.Add(st);
                var named = Stages.NamedFor(r.File, _pkg.Name, all);
                var first = r.Boxes.Where(b => b.Checked).Select(TargetOf).FirstOrDefault(x => x != null) ?? r.Boxes.Select(TargetOf).FirstOrDefault(x => x != null)
                    ?? all.Where(x => named.Contains(x.Base, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.IsMain ? 0 : 1).FirstOrDefault();
                int version = 0;
                pick.SelectedIndexChanged += async (s, e) =>
                {
                    if (!(pick.SelectedItem is StageFile st)) return;
                    int me = ++version;
                    text.Text = "Comparing...";
                    List<string> lines;
                    try
                    {
                        lines = await Task.Run(() =>
                        {
                            using (var card = _installer.OpenCard())
                            {
                                byte[] original = _lib.HasOriginalFile(st.CardPath) ? _lib.OriginalFile(st.CardPath) : card.ReadFile(st.CardPath);
                                return StageCheck.Explain(File.ReadAllBytes(r.File), original, _installer._stageIndex.ProgramNames(card, st.CardPath));
                            }
                        });
                    }
                    catch (Exception ex) { lines = new List<string> { "Couldn't compare: " + Friendly.Explain(ex) }; }
                    if (f.IsDisposed || me != version) return;
                    var said = new List<string>();
                    if (!Allowed(r)(st))
                    {
                        string prog = Stages.MadeFor(r.File, _info, _modules);
                        said.Add("Stage program: the map was built to run with a different stage program" + (prog != null ? " (" + Path.GetFileNameWithoutExtension(prog) + ")" : "") +
                                 " than P+'s " + st.Label + " uses. As it is, it could crash the game there, so only its looks can be used (\"Use its looks on...\").");
                    }
                    said.AddRange(lines);
                    if (said.Count == 0) said.Add("Nothing: it plays exactly like P+'s " + st.Label + ".");
                    text.Text = "Compared with P+'s " + st.Label + ":\r\n\r\n• " + string.Join("\r\n\r\n• ", said);
                };
                if (first != null) pick.SelectedItem = first; else if (pick.Items.Count > 0) pick.SelectedIndex = 0;
                f.ShowDialog(this);
            }
        }

        /// <summary>The stage picked from "Put it on a different stage".</summary>
        async Task TryStage(Row r, StageFile s)
        {
            var existing = r.Boxes.FirstOrDefault(b => !(b.Tag is Group) && string.Equals(TargetOf(b)?.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { existing.Checked = true; return; }
            if (r.Tried.Contains(s.CardPath)) return;   // already shown as not possible
            r.Other.Enabled = false;
            bool same = false;
            try { same = await Task.Run(() => { using (var card = _installer.OpenCard()) { var theirs = _installer._stageIndex.Of(card, _lib, s.CardPath);
                return theirs != null && StageCheck.Compare(r.Gameplay, theirs, "", _installer._stageIndex.ProgramNames(card, s.CardPath)).Passed; } }); } catch { }
            if (IsDisposed) return;
            if (same && Allowed(r)(s)) AddExact(r, s, true);
            else await TryFix(r, s, true);
            if (IsDisposed) return;
            var added = r.Boxes.FirstOrDefault(b => !(b.Tag is Group) && string.Equals(TargetOf(b)?.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase));
            if (added != null) added.Checked = true;
            r.Other.Enabled = true;
        }

        void Relayout(Row r)
        {
            int w = Math.Max(400, r.Targets.Parent.Width - 40);
            r.Status.Width = w;
            r.Status.Height = TextRenderer.MeasureText(r.Status.Text, r.Status.Font, new Size(w, 1000), TextFormatFlags.WordBreak).Height + 10;
            r.Targets.Top = r.Status.Bottom + 4;
            r.Targets.Width = w + 10;
            int h = 0;
            foreach (Control c in r.Targets.Controls)
            {
                if (c is Label l && !(c is LinkLabel))
                {
                    l.Width = w - 10 - l.Margin.Left;
                    l.Height = TextRenderer.MeasureText(l.Text, l.Font, new Size(l.Width - 4, 1000), TextFormatFlags.WordBreak).Height + 6;
                    if (l.Font == Theme.Body) l.Height = Math.Max(l.Height, 24);
                }
                h += c.Height + c.Margin.Vertical;
            }
            r.Targets.Height = h + 4;
            r.OtherLabel.Location = new Point(16, r.Targets.Bottom + 10);
            r.Other.Location = new Point(r.OtherLabel.Right + 6, r.Targets.Bottom + 6);
            r.Why.Location = new Point(r.Other.Right + 16, r.Targets.Bottom + 10);
            int bottom = r.Other.Visible ? r.Other.Bottom : r.Targets.Bottom;
            r.Pic.Location = new Point(14, bottom + 12);
            r.Pic.Width = w + 10;
            r.PicText.Width = Math.Max(200, r.Pic.Width - r.PicText.Left - 6);
            r.PicText.Height = TextRenderer.MeasureText(r.PicText.Text, r.PicText.Font, new Size(r.PicText.Width - 8, 1000), TextFormatFlags.WordBreak).Height + 10;
            foreach (var l in new[] { r.PicChange, r.PicSkip }) { l.AutoSize = false; l.Size = new Size(TextRenderer.MeasureText(l.Text, l.Font).Width + 60, 20); }
            r.PicChange.Top = r.PicSkip.Top = r.PicText.Bottom + 2;
            r.PicSkip.Left = r.PicChange.Right + 16;
            r.Pic.Height = Math.Max(r.PicBox.Image != null ? r.PicBox.Bottom + 8 : 0, r.PicChange.Bottom + 8);
            r.Targets.Parent.Height = (r.Pic.Visible ? r.Pic.Bottom : bottom) + 14;
        }

        new void Update()
        {
            foreach (var r in _rows) ShowPicture(r);
            var ticked = _rows.SelectMany(r => r.Boxes.Where(b => b.Checked)).SelectMany(TargetsOf).Select(t => t?.CardPath).ToList();
            int n = ticked.Count;
            bool clash = ticked.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
            _summary.ForeColor = clash ? Theme.Bad : Theme.Muted;
            _summary.Text = _checking ? "Checking..." : clash ? "Two files are set to replace the same stage. Untick one of them."
                : n == 0 ? "Tick at least one stage to replace." : n == 1 ? "1 stage file will be replaced." : $"{n} stage files will be replaced.";
            _install.Text = n == 1 ? "Install" : $"Install ({n})";
            _install.Enabled = n > 0 && !_checking && !clash;
            _layoutFoot();
        }

        // ----- stage select picture -----

        void MakePictureStrip(Row r)
        {
            r.Pic = new Panel { BackColor = Theme.Back, Visible = false, Height = 110 };
            r.PicBox = new PictureBox { Location = new Point(10, 10), Size = new Size(234, 84), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            r.PicText = new Label { Location = new Point(256, 10), AutoSize = false, Size = new Size(500, 40), Font = Theme.Small, ForeColor = Theme.Text, BackColor = Color.Transparent };
            r.PicChange = new LinkLabel { Text = "Use a different picture...", AutoSize = true, Font = Theme.Small, Location = new Point(256, 60), LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, BackColor = Color.Transparent };
            r.PicSkip = new LinkLabel { Text = "Keep P+'s picture", AutoSize = true, Font = Theme.Small, Location = new Point(420, 60), LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, BackColor = Color.Transparent };
            var row = r;
            r.PicChange.LinkClicked += (s, e) =>
            {
                using (var d = new OpenFileDialog { Title = "Pick a picture of the stage (a screenshot works)", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif", InitialDirectory = _pkg.Folder })
                    if (d.ShowDialog(this) == DialogResult.OK) { row.CustomPicture = d.FileName; row.NoPicture = false; row.PicFor = null; Update(); }
            };
            r.PicSkip.LinkClicked += (s, e) => { row.NoPicture = !row.NoPicture; row.PicFor = null; Update(); };
            r.Pic.Controls.AddRange(new Control[] { r.PicBox, r.PicText, r.PicChange, r.PicSkip });
        }

        StageFile FirstTicked(Row r) => r.Boxes.Where(b => b.Checked).Select(TargetOf).FirstOrDefault();

        StageSpot SpotOf(StageFile s) => _select?.SpotsOf(s.CardPath).FirstOrDefault(x => x.Region != null && x.Picture >= 0);

        /// <summary>The picture files to use for a stage: the one the user picked, or what the download comes with.</summary>
        Dictionary<string, string> PictureFiles(Row r, StageFile target)
        {
            if (r.NoPicture || target == null) return new Dictionary<string, string>();
            if (r.CustomPicture != null) return new Dictionary<string, string> { ["preview"] = r.CustomPicture };
            return StageSelect.FindPictures(_pkg.Folder, target, SpotOf(target));
        }

        Dictionary<string, byte[]> PicturesFor(Row r, StageFile target)
        {
            var result = new Dictionary<string, byte[]>();
            foreach (var kv in PictureFiles(r, target))
                try
                {
                    using (var img = Image.FromFile(kv.Value)) using (var b = new Bitmap(img)) using (var ms = new MemoryStream())
                    { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); result[kv.Key] = ms.ToArray(); }
                }
                catch (Exception e) { Program.Trace("picture " + kv.Value + ": " + e.Message); }
            return result;
        }

        /// <summary>P+'s preview picture for a stage select icon (P+'s own, even when a mod changed it).</summary>
        Bitmap PreviewOf(int picture)
        {
            string tex = StageSelect.PreviewTexture(picture);
            if (_previews.TryGetValue(tex, out var b)) return b;
            try
            {
                byte[] file = _lib.OriginalFile(StageSelect.PicturesFile);
                if (file == null) using (var card = _installer.OpenCard()) file = card.ReadFile(StageSelect.PicturesFile);
                Cosmetics.ReadPictures(file, new[] { tex }).TryGetValue(tex, out b);
            }
            catch (Exception e) { Program.Trace("preview: " + e.Message); }
            return _previews[tex] = b;
        }

        void ShowPicture(Row r)
        {
            var target = FirstTicked(r);
            if (target == null || _checking && r.Boxes.Count == 0) { if (r.Pic.Visible) { r.Pic.Visible = false; Relayout(r); } return; }
            string key = target.CardPath + "|" + r.CustomPicture + "|" + r.NoPicture;
            if (r.PicFor == key && r.Pic.Visible) return;
            r.PicFor = key;
            var spot = SpotOf(target);
            var files = PictureFiles(r, target);
            var old = r.PicBox.Image; r.PicBox.Image = null; old?.Dispose();
            string where = _select?.WhereText(target.CardPath);
            string text;
            if (spot == null)
                text = "Stage select picture: " + (where == null ? "this stage isn't on the stage select pages, so there's no picture to change." : "this version has no picture of its own (" + where + ").");
            else if (r.NoPicture)
                text = "Stage select picture: P+'s picture stays as it is.";
            else if (!files.ContainsKey("preview"))
                text = "Stage select picture: the download has no picture of the stage, so P+'s picture stays. Pick one below if you like (a screenshot of the stage works).";
            else
            {
                string part = spot.Region == "main" ? "the big picture" + (files.ContainsKey("icon") ? ", the icon" : ", the icon (made from the picture)") + (files.ContainsKey("name") ? " and the name" : "")
                    : $"the small {spot.Region} picture at the top (its {spot.Region} mark stays)";
                text = $"Stage select picture: when you hover {spot.Where}, {part} will show this stage instead of P+'s. Picture: {Path.GetFileName(files["preview"])}"
                    + (r.CustomPicture != null ? " (you picked it)" : " (from the download)") + ".";
                var p = PreviewOf(spot.Picture);
                if (p != null)
                    try { using (var img = Image.FromFile(files["preview"])) using (var stage = StageSelect.StagePart(img)) r.PicBox.Image = StageSelect.PaintPreview(p, spot.Region, stage); }
                    catch (Exception e) { text += " (Couldn't open that picture: " + Friendly.Explain(e) + ")"; }
            }
            r.PicText.Text = text;
            r.PicBox.Visible = r.PicBox.Image != null;
            r.PicText.Left = r.PicBox.Image != null ? 256 : 10;
            r.PicChange.Left = r.PicText.Left;
            r.PicChange.Visible = spot != null;
            r.PicSkip.Visible = spot != null && (files.ContainsKey("preview") || r.NoPicture);
            r.PicSkip.Text = r.NoPicture ? "Change the picture after all" : "Keep P+'s picture";
            r.Pic.Visible = true;
            Relayout(r);
        }

        async Task Install()
        {
            _install.Enabled = false;
            var plans = new List<StagePlan>(); var problems = new List<string>();
            foreach (var r in _rows)
                foreach (var b in r.Boxes.Where(b => b.Checked))
                {
                    if (b.Tag is Group g)
                    {
                        // one mod in My Mods: the same group on each file, the stage select picture on the first
                        string id = Guid.NewGuid().ToString("N"); var row0 = r; bool firstPart = true;
                        foreach (var (stage, fx) in g.Parts)
                        {
                            try
                            {
                                _summary.Text = "Running safety checks on " + stage.Label + "...";
                                var plan = fx != null ? fx.Plan : await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.PlanStage(card, _pkg, row0.File, stage, row0.Gameplay); });
                                plan.Name = _pkg.StageFiles.Count == 1 ? _pkg.Name : plan.Name;
                                plan.Group = id;
                                if (firstPart) plan.SelectPictures = PicturesFor(row0, stage);
                                firstPart = false;
                                if (plan.CanInstall) plans.Add(plan); else problems.Add($"{stage.Label}: " + string.Join("; ", plan.Checks.Where(c => !c.Passed).Select(c => c.Title)));
                            }
                            catch (Exception e) { problems.Add($"{stage.Label}: {Friendly.Explain(e)}"); }
                            if (IsDisposed) return;
                        }
                        continue;
                    }
                    var target = TargetOf(b); var row = r;
                    try
                    {
                        var fixd = b.Tag as Fix;
                        _summary.Text = "Running safety checks on " + target.Label + "...";
                        var plan = fixd != null ? fixd.Plan : await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.PlanStage(card, _pkg, row.File, target, row.Gameplay); });
                        if (_pkg.StageFiles.Count == 1) plan.Name = _pkg.Name;
                        plan.SelectPictures = PicturesFor(row, target);
                        if (plan.CanInstall) plans.Add(plan); else problems.Add($"{target.Label}: " + string.Join("; ", plan.Checks.Where(c => !c.Passed).Select(c => c.Title)));
                    }
                    catch (Exception e) { problems.Add($"{target.Label}: {Friendly.Explain(e)}"); }
                    if (IsDisposed) return;
                }
            if (problems.Count > 0)
            {
                string text = "These didn't pass the safety checks:\r\n\r\n" + string.Join("\r\n", problems);
                if (plans.Count == 0) { Dialog.Show(this, text, "Can't install", MessageBoxButtons.OK, MessageBoxIcon.Warning); Update(); return; }
                if (Dialog.Show(this, text + $"\r\n\r\nInstall the other {plans.Count}?", "Some can't be installed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { Update(); return; }
            }
            Plans = plans;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
