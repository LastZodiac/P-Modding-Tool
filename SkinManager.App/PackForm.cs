// Adding several costumes from one download at once: tick the ones you want, check (or change)
// which costume slot each goes in, and install them together. Costumes you added before are marked,
// so you can come back later for the rest.
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
    class PackForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;
        readonly SkinPackage _pkg;
        public List<SkinPlan> Plans { get; private set; } = new List<SkinPlan>();
        public PackageCostume StepByStep { get; private set; }   // "set this one up step by step" was clicked

        sealed class Row
        {
            public PackageCostume Costume;
            public CheckBox Tick;
            public ComboBox Slot;
            public PictureBox Model, Csp, SlotPic;
            public Label Status;
            public Dictionary<CostumeSlot, CheckResult> Fits;   // null while checking
            public InstalledSkin Already;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly Dictionary<int, List<CostumeSlot>> _slots = new Dictionary<int, List<CostumeSlot>>();
        readonly Dictionary<int, Dictionary<string, Bitmap>> _slotPics = new Dictionary<int, Dictionary<string, Bitmap>>();
        readonly Label _hint = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Button _install = Theme.Primary("Install");
        readonly Label _summary = Theme.Label("", Theme.Body, Theme.Muted);
        bool _updating;
        List<InstalledSkin> _fromHere;   // skins already added from this download
        Action _layoutFoot = () => { };

        class SlotItem
        {
            public CostumeSlot Slot; public string Text;
            public override string ToString() => Text ?? "";   // Windows' dropdown refuses an item whose text is null
        }

        readonly List<InstalledSkin> _replacing;   // an update: the costumes of the old version

        /// <summary>Whether an installed costume is this costume of the package (same character, number and variant).</summary>
        static bool SameCostume(InstalledSkin k, PackageCostume c)
        {
            var p = (k.PackageKey ?? "").Split('|');
            if (p.Length >= 3) return p[0] == c.Character.SlotListId.ToString() && p[1] == c.Number.ToString() && p[2] == (c.Variant ?? "").ToString();
            return k.SlotListId == c.Character.SlotListId;
        }

        public PackForm(Installer installer, Library lib, SkinPackage pkg, List<InstalledSkin> replacing = null)
        {
            _installer = installer; _lib = lib; _pkg = pkg; _replacing = replacing ?? new List<InstalledSkin>();
            Text = "Add costumes: " + pkg.Name;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1120, 780); MinimumSize = new Size(900, 520);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var head = new Panel { Dock = DockStyle.Top, Height = 108 };
            var title = Theme.Label($"This download has {pkg.Costumes.Count} costumes", Theme.Title); title.Location = new Point(24, 14);
            _hint.Location = new Point(26, 50); _hint.AutoSize = false; _hint.Size = new Size(1040, 22);
            _hint.Text = "Tick the ones you want. Each goes into the costume slot shown on its right (you can change it). Checking which slots each one fits...";
            var all = Theme.Secondary("Tick all"); all.Location = new Point(24, 74);
            var none = Theme.Secondary("Untick all"); none.Location = new Point(120, 74);
            all.Click += (s, e) => { foreach (var r in _rows) if (r.Fits == null || r.Fits.Values.Any(f => f.Passed)) r.Tick.Checked = true; };
            none.Click += (s, e) => { foreach (var r in _rows) r.Tick.Checked = false; };
            all.Layout += (s, e) => none.Left = all.Right + 8;
            head.Controls.AddRange(new Control[] { title, _hint, all, none });

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 4, 24, 4) };
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Panel };
            _summary.Location = new Point(24, 22); _summary.AutoSize = false; _summary.Size = new Size(700, 22);
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

            using (var card = _installer.OpenCard())
                foreach (var c in pkg.Costumes.Select(x => x.Character).Distinct())
                {
                    var slots = PPlusCard.ReadSlots(card, c);
                    _slots[c.SlotListId] = slots;
                    _slotPics[c.SlotListId] = Cosmetics.ReadPictures(card.ReadFile(c.CspFilePath), slots.Select(x => c.CspTextureName(x.Index)));
                }

            // costumes from this download that are already in My Mods
            _fromHere = _lib.Skins.Where(k => (
                (pkg.Source != null && pkg.Source.FileId != 0 && k.GameBananaFileId == pkg.Source.FileId) ||
                (pkg.Source != null && pkg.Source.FileId == 0 && pkg.Source.ModId != 0 && k.GameBananaModId == pkg.Source.ModId) ||   // "all files" of a mod
                (pkg.SourcePath != null && string.Equals(k.SourcePath, pkg.SourcePath, StringComparison.OrdinalIgnoreCase)))).ToList();
            var mine = _fromHere.Where(k => k.PackageKey != null).ToList();
            foreach (var c in pkg.Costumes)
            {
                var r = new Row { Costume = c, Already = mine.FirstOrDefault(k => k.PackageKey == c.Key) };
                // tick every variant by default, except a Brawl (or untagged) copy of a costume that also comes in a P+ version
                bool pplusTwin = !c.MadeForPPlus && pkg.Costumes.Any(o => o != c && o.Character == c.Character && o.Number == c.Number && o.MadeForPPlus &&
                                                                         (c.MadeForBrawl || o.Folder == c.Folder));
                bool tick = !pplusTwin && r.Already == null;
                // an update: tick the costumes the old version had (when any match)
                if (_replacing.Count > 0 && pkg.Costumes.Any(o => _replacing.Any(k => SameCostume(k, o)))) tick = _replacing.Any(k => SameCostume(k, c));
                list.Controls.Add(BuildRow(r, tick));
                _rows.Add(r);
            }
            list.Resize += (s, e) => { foreach (Control c in list.Controls) c.Width = Math.Max(700, list.ClientSize.Width - 50); };
            Shown += (s, e) => { CheckFits(); RenderModels(); };
            UpdateSummary();
        }

        Control BuildRow(Row r, bool tick)
        {
            var c = r.Costume;
            var panel = new Panel { Width = 1040, Height = 112, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 8) };
            r.Tick = new CheckBox { Location = new Point(14, 44), Size = new Size(22, 22), Checked = tick };
            r.Model = new PictureBox { Location = new Point(44, 6), Size = new Size(80, 100), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back };
            var name = Theme.Label(c.Variant != "" ? $"{c.Character.Name} • {c.Variant}" : $"{c.Character.Name} • costume {c.NumberText}", Theme.Heading);
            name.Location = new Point(136, 10);
            var sub = Theme.Label($"Costume {c.NumberText} in the download" + (c.Files.Count > 1 ? $" (with {c.Files.Count - 1} extra file{(c.Files.Count == 2 ? "" : "s")} it needs)" : "") + (c.Folder != "" ? $"  •  from its \"{c.Folder}\" folder" : ""), Theme.Small, Theme.Muted);
            sub.Location = new Point(138, 36);
            r.Status = Theme.Label("Checking which slots it fits...", Theme.Small, Theme.Muted);
            r.Status.Location = new Point(138, 58); r.Status.AutoSize = false; r.Status.Size = new Size(420, 44);
            if (r.Already != null) { r.Status.Text = $"Already in My Mods ({r.Already.Character.Name}'s costume {r.Already.SlotIndex + 1}, {(r.Already.Enabled ? "on" : "off")})."; r.Status.ForeColor = Theme.Accent; }
            var arrow = Theme.Label("goes into", Theme.Small, Theme.Muted); arrow.Location = new Point(590, 14);
            r.Slot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(590, 36), Width = 290, BackColor = Theme.Back, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Enabled = false };
            r.SlotPic = new PictureBox { Location = new Point(900, 6), Size = new Size(80, 100), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back };
            var own = new LinkLabel { Text = "Set up this one step by step (choose its pictures)", Location = new Point(590, 72), AutoSize = false, Size = new Size(300, 20), Font = Theme.Small, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent };
            own.LinkClicked += (s, e) => { StepByStep = c; DialogResult = DialogResult.Retry; Close(); };
            panel.Controls.AddRange(new Control[] { r.Tick, r.Model, name, sub, r.Status, arrow, r.Slot, r.SlotPic, own });
            panel.Resize += (s, e) => { r.SlotPic.Left = panel.Width - 96; };
            r.Tick.CheckedChanged += (s, e) => { if (!_updating) { AutoAssign(); UpdateSummary(); } };
            r.Slot.SelectedIndexChanged += (s, e) => { if (!_updating) { ShowSlotPic(r); RefreshSlotLists(); UpdateSummary(); } };
            return panel;
        }

        async void RenderModels()
        {
            foreach (var r in _rows)
            {
                var path = r.Costume.Main?.Path;
                if (path == null) continue;
                Program.Trace("pack: render " + path);
                var bmp = await Task.Run(() => ModelPreview.RenderFile(path));
                Program.Trace("pack: rendered");
                if (IsDisposed) return;
                if (bmp != null) r.Model.Image = bmp;
            }
        }

        async void CheckFits()
        {
            foreach (var r in _rows)
            {
                var c = r.Costume; var slots = _slots[c.Character.SlotListId]; var main = c.Main;
                Program.Trace("pack: checking " + c.Label);
                Dictionary<CostumeSlot, CheckResult> fits;
                try
                {
                    fits = await Task.Run(() =>
                    {
                        if (r.Already == null && _fromHere.Count > 0)
                        {
                            // added before the app remembered which costume it was, or from a single file of this mod: recognise it by its file
                            string sha = Library.Sha256(SkinCheck.Prepared(main.Path, ""));
                            r.Already = _fromHere.FirstOrDefault(k => k.PackageKey != c.Key && k.Files.Any(f => f.Sha256 == sha));
                        }
                        var skeleton = SkinCheck.ReadSkeleton(File.ReadAllBytes(main.Path));
                        var result = new Dictionary<CostumeSlot, CheckResult>();
                        using (var card = _installer.OpenCard())
                            foreach (var s in slots) result[s] = _installer.SkeletonFits(card, skeleton, c.Character, s);
                        return result;
                    });
                }
                catch (Exception e)
                {
                    if (IsDisposed) return;
                    r.Status.Text = "Couldn't read this costume: " + Friendly.Explain(e); r.Status.ForeColor = Theme.Bad;
                    r.Tick.Checked = false; r.Tick.Enabled = false;
                    r.Fits = new Dictionary<CostumeSlot, CheckResult>();
                    continue;
                }
                if (IsDisposed) return;
                Program.Trace("pack: checked " + c.Label);
                r.Fits = fits;
                if (r.Already != null)
                {
                    r.Status.Text = $"Already in My Mods ({r.Already.Character.Name}'s costume {r.Already.SlotIndex + 1}, {(r.Already.Enabled ? "on" : "off")}).";
                    r.Status.ForeColor = Theme.Accent;
                    if (r.Already.PackageKey != c.Key) { _updating = true; r.Tick.Checked = false; _updating = false; }
                }
                int n = fits.Values.Count(f => f.Passed);
                if (n == 0)
                {
                    r.Status.Text = $"Doesn't fit any of {c.Character.Name}'s costumes (its gameplay bones are different, which would make online games go out of sync).";
                    r.Status.ForeColor = Theme.Bad;
                    _updating = true; r.Tick.Checked = false; r.Tick.Enabled = false; _updating = false;
                }
                else if (r.Already == null) { r.Status.Text = $"Fits {n} of {fits.Count} costumes."; r.Status.ForeColor = Theme.Good; }
                r.Slot.Enabled = n > 0;
                try { AutoAssign(); UpdateSummary(); }
                catch (Exception e) { Program.Trace("pack: assign failed: " + e); Program.Log("Choosing slots for a costume pack", e); }
            }
            _hint.Text = "Tick the ones you want. Each goes into the costume slot shown on its right; change it with the list if you like.";
        }

        /// <summary>Gives each ticked costume a slot: the one with its own file number if it fits, otherwise the first free one that fits.</summary>
        void AutoAssign()
        {
            _updating = true;
            var taken = new HashSet<int>(_rows.Where(r => r.Tick.Checked && r.Slot.SelectedItem is SlotItem si && si.Slot != null && Fits(r, si.Slot)).Select(r => Key(r, ((SlotItem)r.Slot.SelectedItem).Slot)));
            foreach (var r in _rows)
            {
                if (r.Fits == null || r.Fits.Count == 0) continue;
                FillSlots(r);
                if (!r.Tick.Checked) continue;
                if (r.Slot.SelectedItem is SlotItem cur && cur.Slot != null && Fits(r, cur.Slot)) continue;
                var slots = _slots[r.Costume.Character.SlotListId];
                var was = _replacing.FirstOrDefault(k => SameCostume(k, r.Costume)) ?? _replacing.FirstOrDefault(k => k.SlotListId == r.Costume.Character.SlotListId);
                var pick = (was != null ? slots.FirstOrDefault(s => s.Index == was.SlotIndex && Fits(r, s) && !taken.Contains(Key(r, s))) : null)
                        ?? slots.FirstOrDefault(s => s.FileNumber == r.Costume.Number && Fits(r, s) && !taken.Contains(Key(r, s)))
                        ?? slots.FirstOrDefault(s => Fits(r, s) && !taken.Contains(Key(r, s)) && _lib.At(r.Costume.Character, s.Index) == null)
                        ?? slots.FirstOrDefault(s => Fits(r, s) && !taken.Contains(Key(r, s)));
                if (pick != null) { taken.Add(Key(r, pick)); Select(r, pick); }
            }
            _updating = false;
            RefreshSlotLists();
        }

        static int Key(Row r, CostumeSlot s) => r.Costume.Character.SlotListId * 1000 + s.Index;
        static bool Fits(Row r, CostumeSlot s) => r.Fits != null && r.Fits.TryGetValue(s, out var f) && f.Passed;

        void FillSlots(Row r)
        {
            if (r.Slot.Items.Count > 0) return;
            r.Slot.Items.Add(new SlotItem { Text = "Choose a costume slot..." });
            foreach (var s in _slots[r.Costume.Character.SlotListId].Where(s => Fits(r, s)))
                r.Slot.Items.Add(new SlotItem { Slot = s, Text = $"Costume {s.Index + 1}" });
            r.Slot.SelectedIndex = 0;
        }

        void Select(Row r, CostumeSlot s)
        {
            foreach (SlotItem i in r.Slot.Items) if (i.Slot == s) { r.Slot.SelectedItem = i; break; }
            ShowSlotPic(r);
        }

        void ShowSlotPic(Row r)
        {
            var s = (r.Slot.SelectedItem as SlotItem)?.Slot;
            var c = r.Costume.Character;
            r.SlotPic.Image = s != null && _slotPics[c.SlotListId].TryGetValue(c.CspTextureName(s.Index), out var p) ? p : null;
        }

        /// <summary>Names each slot in the lists, marking ones another ticked costume uses and your skins that would turn off.</summary>
        void RefreshSlotLists()
        {
            _updating = true;
            foreach (var r in _rows)
            {
                var keep = r.Slot.SelectedItem;
                foreach (var i in r.Slot.Items.Cast<SlotItem>().ToList())
                {
                    if (i.Slot == null) continue;
                    var c = r.Costume.Character;
                    var other = _rows.FirstOrDefault(o => o != r && o.Tick.Checked && (o.Slot.SelectedItem as SlotItem)?.Slot == i.Slot && o.Costume.Character == c);
                    var mine = _lib.At(c, i.Slot.Index);
                    string text = $"Costume {i.Slot.Index + 1}" + (i.Slot.FileNumber == r.Costume.Number ? "  (same file number)" : "")
                        + (other != null ? "  — also picked for another" : mine != null ? $"  — turns off your {mine.Name}" : "");
                    if (i.Text != text) { i.Text = text; int at = r.Slot.Items.IndexOf(i); r.Slot.Items[at] = i; }
                }
                if (keep != null && r.Slot.SelectedItem != keep) r.Slot.SelectedItem = keep;
            }
            _updating = false;
        }

        List<Row> Chosen() => _rows.Where(r => r.Tick.Checked && (r.Slot.SelectedItem as SlotItem)?.Slot != null).ToList();

        void UpdateSummary()
        {
            var chosen = Chosen();
            var clash = chosen.GroupBy(r => Key(r, ((SlotItem)r.Slot.SelectedItem).Slot)).Where(g => g.Count() > 1).ToList();
            int ticked = _rows.Count(r => r.Tick.Checked);
            bool checking = _rows.Any(r => r.Fits == null);
            _summary.ForeColor = clash.Count > 0 ? Theme.Bad : Theme.Muted;
            _summary.Text = clash.Count > 0 ? "Two ticked costumes are set to the same slot. Pick a different slot for one of them."
                : ticked > chosen.Count ? $"{ticked - chosen.Count} ticked costume(s) still need a slot."
                : checking ? $"{chosen.Count} ready so far (still checking the rest)..."
                : $"{chosen.Count} costume(s) will be added.";
            _install.Text = chosen.Count == 1 ? "Install 1 costume" : $"Install {chosen.Count} costumes";
            _install.Enabled = chosen.Count > 0 && clash.Count == 0 && ticked == chosen.Count && !checking;
            _layoutFoot();
        }

        async Task Install()
        {
            var chosen = Chosen();
            _install.Enabled = false;
            var plans = new List<SkinPlan>();
            var problems = new List<string>();
            int i = 0;
            foreach (var r in chosen)
            {
                _summary.Text = $"Running safety checks... {++i} of {chosen.Count}";
                var costume = r.Costume; var slot = ((SlotItem)r.Slot.SelectedItem).Slot;
                var pics = new Dictionary<PictureKind, string>();
                foreach (PictureKind k in Enum.GetValues(typeof(PictureKind)))
                {
                    var p = _pkg.PictureFor(costume, k);
                    if (p != null && _pkg.Costumes.Count(x => _pkg.PictureFor(x, k) == p) == 1) pics[k] = p.Path;   // only pictures clearly for this costume
                }
                if (!pics.ContainsKey(PictureKind.SelectPortrait) && r.Model.Image is Bitmap model)
                {
                    string png = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "model-csp-" + Guid.NewGuid().ToString("N") + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(png));
                    using (var small = new Bitmap(128, 160))
                    {
                        using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(model, 0, 0, 128, 160); }
                        small.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    pics[PictureKind.SelectPortrait] = png;
                }
                try
                {
                    var plan = await Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.Plan(card, _pkg, costume, slot, pics); });
                    if (plan.CanInstall) plans.Add(plan);
                    else problems.Add($"{plan.Name}: " + string.Join("; ", plan.Checks.Where(c => !c.Passed).Select(c => c.Title)));
                }
                catch (Exception e) { problems.Add($"{costume.Label}: {Friendly.Explain(e)}"); }
                if (IsDisposed) return;
            }
            if (problems.Count > 0)
            {
                string text = "These didn't pass the safety checks and won't be installed:\r\n\r\n" + string.Join("\r\n", problems.Take(12));
                if (plans.Count == 0) { MessageBox.Show(this, text, "Can't install", MessageBoxButtons.OK, MessageBoxIcon.Warning); UpdateSummary(); return; }
                if (MessageBox.Show(this, text + $"\r\n\r\nInstall the other {plans.Count}?", "Some can't be installed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { UpdateSummary(); return; }
            }
            Plans = plans;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
