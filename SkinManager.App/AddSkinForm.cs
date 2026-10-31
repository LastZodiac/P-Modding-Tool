// The "Add a skin" steps: which costume, which slot, which pictures, then the safety checks.
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
    class AddSkinForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;
        readonly SkinPackage _pkg;
        PackageCostume _costume;
        CostumeSlot _slot;
        readonly Dictionary<PictureKind, string> _pictures = new Dictionary<PictureKind, string>();
        public SkinPlan Plan { get; private set; }
        readonly Dictionary<PackageCostume, Bitmap> _renders = new Dictionary<PackageCostume, Bitmap>();
        string _modelCsp;   // a select-screen picture made from the 3D model, for skins that came without one

        /// <summary>Draws the costume's 3D model (in the background) and hands it over when ready.</summary>
        async void RenderModel(PackageCostume c, Action<Bitmap> done)
        {
            if (c?.Main == null) return;
            if (!_renders.TryGetValue(c, out var bmp))
            {
                bmp = await Task.Run(() => ModelPreview.RenderFile(c.Main.Path));
                _renders[c] = bmp;
            }
            if (bmp != null && !IsDisposed) done(bmp);
        }

        static Bitmap Smaller(Bitmap b, int w, int h)
        {
            var r = new Bitmap(w, h);
            using (var g = Graphics.FromImage(r)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(b, 0, 0, w, h); }
            return r;
        }

        readonly Label _stepTitle = Theme.Label("", Theme.Title);
        readonly Label _stepHint = Theme.Label("", Theme.Body, Theme.Muted);
        readonly Panel _body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 6) };
        readonly Button _back = Theme.Secondary("Back");
        readonly Button _next = Theme.Primary("Next");
        int _step;
        readonly int _firstStep;

        /// <summary>An update: the old version's costume, whose slot is picked when the new one fits it.</summary>
        public InstalledSkin Replacing;

        public AddSkinForm(Installer installer, Library lib, SkinPackage pkg, Character preferred, PackageCostume start = null)
        {
            _installer = installer; _lib = lib; _pkg = pkg;
            Text = "Add a skin: " + pkg.Name;
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1000, 760); MinimumSize = new Size(800, 600);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;

            var head = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(24, 16, 24, 0) };
            _stepTitle.Location = new Point(24, 14); _stepHint.Location = new Point(26, 50);
            _stepHint.MaximumSize = new Size(920, 0);
            head.Controls.Add(_stepTitle); head.Controls.Add(_stepHint);
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Panel };
            _back.Anchor = _next.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _back.Click += (s, e) => Go(_step - 1);
            _next.Click += (s, e) => Next();
            foot.Controls.Add(_back); foot.Controls.Add(_next);
            foot.Resize += (s, e) =>
            {
                _next.Location = new Point(foot.Width - _next.Width - 24, (foot.Height - _next.Height) / 2);
                _back.Location = new Point(_next.Left - _back.Width - 10, (foot.Height - _back.Height) / 2);
            };
            Controls.Add(_body); Controls.Add(foot); Controls.Add(head);

            _costume = start ?? pkg.Costumes.FirstOrDefault(c => c.Character == preferred) ?? pkg.Costumes[0];
            _firstStep = pkg.Costumes.Count > 1 && start == null ? 0 : 1;
            Shown += (s, e) => Go(_firstStep);
        }

        void Go(int step)
        {
            if (step < _firstStep) return;
            _step = step;
            foreach (Control c in _body.Controls) c.Dispose();
            _body.Controls.Clear();
            _back.Visible = step > _firstStep;
            _next.Enabled = true;
            switch (step)
            {
                case 0: ShowCostumes(); break;
                case 1: ShowSlots(); break;
                case 2: ShowPictures(); break;
                case 3: ShowChecks(); break;
            }
            _next.PerformLayout(); OnResize(EventArgs.Empty);
        }

        void Next()
        {
            if (_step == 3) { DialogResult = DialogResult.OK; Close(); return; }
            Go(_step + 1);
        }

        void SetHeader(string title, string hint, string next)
        {
            _stepTitle.Text = title; _stepHint.Text = hint; _stepHint.ForeColor = Theme.Muted; _next.Text = next;
            var foot = _next.Parent; foot?.PerformLayout();
            if (foot != null) { _next.Location = new Point(foot.Width - _next.Width - 24, (foot.Height - _next.Height) / 2); _back.Location = new Point(_next.Left - _back.Width - 10, _next.Top); }
        }

        FlowLayoutPanel Grid()
        {
            var g = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 6, 0, 0) };
            _body.Controls.Add(g);
            return g;
        }

        static Image LoadImage(string path)
        {
            if (path == null) return null;
            try { using (var i = Image.FromFile(path)) return new Bitmap(i); } catch { return null; }
        }

        // ----- step 0: which costume (packs with several) -----
        void ShowCostumes()
        {
            SetHeader("Which costume?", $"This download has {_pkg.Costumes.Count} costumes or versions. Pick the one to add (you can add this download again for the others)." +
                (_pkg.Costumes.Any(x => x.MadeForPPlus) && _pkg.Costumes.Any(x => !x.MadeForPPlus) ? " Versions made for P+ are listed first." : ""), "Next");
            var grid = Grid();
            foreach (var c in _pkg.Costumes)
            {
                var tile = new SlotTile
                {
                    Caption = c.Character.Name,
                    Badge = (c.Variant != "" ? c.Variant + "\r\n" : "") + $"File {c.NumberText}" + (c.Files.Count > 1 ? $" + {c.Files.Count - 1} extra" : ""),
                    BadgeColor = c.MadeForPPlus ? Theme.Good : c.MadeForBrawl ? Theme.Warn : Theme.Muted,
                    Selected = c == _costume, Margin = new Padding(0, 0, 14, 14),
                };
                tile.Click += (s, e) => { Choose(c); foreach (SlotTile t in grid.Controls) t.Selected = t == tile; grid.Invalidate(true); };
                tile.DoubleClick += (s, e) => { Choose(c); Next(); };
                grid.Controls.Add(tile);
                RenderModel(c, b => { tile.Picture = Smaller(b, 128, 160); tile.Invalidate(); });
            }
        }

        void Choose(PackageCostume c)
        {
            if (c == _costume) return;
            _costume = c; _slot = null; _pictures.Clear(); _modelCsp = null; _askedModel = false;   // pictures belong to the costume picked
        }

        // ----- step 1: which slot -----
        void ShowSlots()
        {
            var c = _costume.Character;
            SetHeader($"Which of {c.Name}'s costumes should it replace?",
                "Checking which costumes this skin fits...", "Next");
            _next.Enabled = false;
            var grid = Grid();
            List<CostumeSlot> slots; Dictionary<string, Bitmap> pics;
            try
            {
                using (var card = _installer.OpenCard())
                {
                    slots = PPlusCard.ReadSlots(card, c);
                    pics = Cosmetics.ReadPictures(card.ReadFile(c.CspFilePath), slots.Select(s => c.CspTextureName(s.Index)));
                }
            }
            catch (Exception e)
            {
                Program.Log("add skin: costumes", e);
                _stepHint.Text = "Couldn't read " + c.Name + "'s costumes from your SD card. " + Friendly.Explain(e) + " Go Back, or close this and try again.";
                _stepHint.ForeColor = Theme.Warn;
                return;
            }
            var tiles = new Dictionary<CostumeSlot, SlotTile>();
            foreach (var s in slots)
            {
                var mine = _lib.At(c, s.Index);
                var tile = new SlotTile
                {
                    Picture = pics.TryGetValue(c.CspTextureName(s.Index), out var p) ? p : null,
                    Caption = $"Costume {s.Index + 1}", Badge = "Checking...", Dimmed = true,
                    Margin = new Padding(0, 0, 14, 14), Tag = mine,
                };
                tiles[s] = tile;
                grid.Controls.Add(tile);
            }
            var tip = new ToolTip();
            var main = _costume.Main;
            Task.Run(() =>
            {
                var skeleton = SkinCheck.ReadSkeleton(File.ReadAllBytes(main.Path));
                var results = new Dictionary<CostumeSlot, CheckResult>();
                using (var card = _installer.OpenCard())
                    foreach (var s in slots) results[s] = _installer.SkeletonFits(card, skeleton, c, s);
                return results;
            }).ContinueWith(t =>
            {
                if (IsDisposed || _step != 1) return;
                if (t.IsFaulted)
                {
                    Program.Log("add skin: fit check", t.Exception);
                    foreach (var tile in tiles.Values) { tile.Badge = "Couldn't check"; tile.BadgeColor = Theme.Warn; tile.Invalidate(); }
                    _stepHint.Text = "Couldn't check which costumes this skin fits. " + Friendly.Explain(t.Exception.GetBaseException()) + " Go Back and pick the skin again to retry.";
                    _stepHint.ForeColor = Theme.Warn;
                    return;
                }
                int fits = 0;
                foreach (var kv in t.Result)
                {
                    var tile = tiles[kv.Key];
                    var mine = tile.Tag as InstalledSkin;
                    if (kv.Value.Passed)
                    {
                        fits++;
                        tile.Dimmed = false;
                        var byHand = mine == null ? HandMods.Last?.Costume(c, kv.Key.Index) : null;
                        tile.Badge = mine != null ? "Fits • turns off your " + mine.Name : byHand != null ? "Fits • replaces your own costume (kept)" : "Fits";
                        tile.BadgeColor = byHand != null ? Theme.Warn : Theme.Good;
                        var slot = kv.Key;
                        tile.Click += (s, e) => { _slot = slot; foreach (var x in tiles.Values) x.Selected = x == tile; grid.Invalidate(true); _next.Enabled = true; };
                        tile.DoubleClick += (s, e) => { _slot = slot; Next(); };
                        if (_slot == slot) { tile.Selected = true; _next.Enabled = true; }
                    }
                    else
                    {
                        tile.Badge = "Doesn't fit";
                        tile.BadgeColor = Theme.Bad;
                        tile.Cursor = Cursors.Help;
                    }
                    tip.SetToolTip(tile, kv.Value.Detail);
                    tile.Invalidate();
                }
                _stepHint.Text = fits > 0
                    ? $"This skin fits {fits} of {c.Name}'s {t.Result.Count} costumes. Click one. (Point at a costume to see why it fits or not.)"
                    : $"This skin doesn't fit any of {c.Name}'s costumes: it changes the bones that hits are worked out from, so it would make online games go out of sync. Point at a costume to see the details.";
                if (_slot == null && Replacing != null && Replacing.SlotListId == c.SlotListId)
                {
                    var was = t.Result.FirstOrDefault(kv => kv.Key.Index == Replacing.SlotIndex && kv.Value.Passed).Key;
                    if (was != null && tiles.ContainsKey(was)) { _slot = was; tiles[was].Selected = true; tiles[was].Invalidate(); _next.Enabled = true; }
                }
                if (fits == 1 && _slot == null) { var only = t.Result.First(kv => kv.Value.Passed).Key; _slot = only; tiles[only].Selected = true; tiles[only].Invalidate(); _next.Enabled = true; }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ----- step 2: pictures -----
        void ShowPictures()
        {
            SetHeader("Pictures", "These show the skin in the menus and during a match. Any picture you leave out keeps the one P+ has now.", "Next");
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var side = new Panel { Dock = DockStyle.Right, Width = 220 };
            var model = new PictureBox { Size = new Size(200, 250), Location = new Point(10, 4), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Panel };
            var cap = Theme.Label("The costume you're adding", Theme.Small, Theme.Muted); cap.AutoSize = false; cap.Size = new Size(200, 36); cap.Location = new Point(10, 260);
            side.Controls.Add(model); side.Controls.Add(cap);
            _body.Controls.Add(list); _body.Controls.Add(side);
            RenderModel(_costume, b => model.Image = b);
            foreach (PictureKind kind in Enum.GetValues(typeof(PictureKind)))
            {
                if (!_pictures.ContainsKey(kind)) _pictures[kind] = _pkg.PictureFor(_costume, kind)?.Path;
                list.Controls.Add(PictureRow(kind));
            }
        }

        Control PictureRow(PictureKind kind)
        {
            var size = Cosmetics.SizeOf(kind);
            int boxW = kind == PictureKind.SelectPortrait ? 96 : 64, boxH = kind == PictureKind.SelectPortrait ? 120 : kind == PictureKind.BattlePortrait ? 75 : 64;
            var row = new Panel { Width = 700, Height = Math.Max(boxH, 128) + 14, Margin = new Padding(0, 0, 0, 10) };
            var box = new PictureBox { Size = new Size(boxW, boxH), Location = new Point(0, 4), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Panel };
            var name = Theme.Label(char.ToUpper(Cosmetics.Describe(kind)[0]) + Cosmetics.Describe(kind).Substring(1), Theme.Heading);
            int x = boxW + 18;
            name.Location = new Point(x, 4);
            var state = Theme.Label("", Theme.Small, Theme.Muted); state.Location = new Point(x + 2, 28); state.AutoSize = false; state.Size = new Size(540, 28);
            var choose = Theme.Secondary("Choose a picture...");
            var fromModel = Theme.Secondary("Make one from the 3D model");
            fromModel.Visible = kind == PictureKind.SelectPortrait;
            var none = Theme.Secondary("Keep P+'s picture");
            var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Visible = false };
            choose.Location = new Point(x, 56);
            none.Location = new Point(x + 160, 56);
            pick.Location = new Point(x + 320, 59);
            pick.Width = 250;
            row.Layout += (s, e) => { none.Left = choose.Right + 8; fromModel.Left = x; pick.Left = fromModel.Visible ? fromModel.Right + 8 : x; pick.Top = 95; };
            fromModel.Location = new Point(x, 92);
            void Refresh()
            {
                string path = _pictures[kind];
                box.Image = LoadImage(path);
                state.Text = path == null
                    ? $"None chosen: P+'s current {Cosmetics.Describe(kind)} stays."
                    : path == _modelCsp ? "Made from the skin's 3D model (no picture for this costume came with the skin)."
                    : $"{Path.GetFileName(path)}" + (ImageSize(path) == size ? "" : $"  (it will be fitted to {size.Width}x{size.Height})");
                none.Visible = path != null;
            }
            var candidates = _pkg.Pictures.Where(p => p.Kind == kind).ToList();
            if (candidates.Count > 1)
            {
                pick.Visible = true;
                pick.Items.Add("Pictures in this skin...");
                foreach (var cnd in candidates) pick.Items.Add(Path.GetFileName(cnd.Path));
                pick.SelectedIndex = 0;
                pick.SelectedIndexChanged += (s, e) => { if (pick.SelectedIndex > 0) { _pictures[kind] = candidates[pick.SelectedIndex - 1].Path; Refresh(); } };
                if (_pictures[kind] == null) state.Text = $"This skin has {candidates.Count} of these; pick the one for this color.";
            }
            choose.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Pictures|*.png;*.bmp;*.jpg;*.jpeg;*.gif", Title = "Choose the " + Cosmetics.Describe(kind) })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { _pictures[kind] = dlg.FileName; Refresh(); }
            };
            none.Click += (s, e) => { _pictures[kind] = null; Refresh(); };
            void UseModel()
            {
                RenderModel(_costume, b =>
                {
                    if (_modelCsp == null)
                    {
                        _modelCsp = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "model-csp-" + Guid.NewGuid().ToString("N") + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(_modelCsp));
                        using (var small = Smaller(b, 128, 160)) small.Save(_modelCsp, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    _pictures[kind] = _modelCsp; Refresh();
                });
            }
            fromModel.Click += (s, e) => UseModel();
            row.Controls.AddRange(new Control[] { box, name, state, choose, none, pick, fromModel });
            // no select-screen picture came with this costume: show the model there by default
            if (kind == PictureKind.SelectPortrait && _pictures[kind] == null && candidates.Count == 0 && !_askedModel) { _askedModel = true; UseModel(); }
            Refresh();
            if (candidates.Count > 1 && _pictures[kind] == null) state.Text = $"This skin has {candidates.Count} of these; pick the one for this color.";
            return row;
        }

        bool _askedModel;

        static Size ImageSize(string path)
        {
            try { using (var i = Image.FromFile(path)) return i.Size; } catch { return Size.Empty; }
        }

        // ----- step 3: checks -----
        void ShowChecks()
        {
            var c = _costume.Character;
            SetHeader("Safety checks", $"Replacing {c.Name}'s costume {_slot.Index + 1} with \"{_pkg.Name}\".", "Install");
            _next.Enabled = false;
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            _body.Controls.Add(list);
            list.Resize += (s, e) => { foreach (Control c2 in list.Controls) if (c2 is Panel) c2.Width = Math.Max(400, list.ClientSize.Width - 24); };
            list.Controls.Add(Theme.Label("Checking...", Theme.Body, Theme.Muted));
            var costume = _costume; var slot = _slot; var pics = new Dictionary<PictureKind, string>(_pictures);
            Task.Run(() => { using (var card = _installer.OpenCard()) return _installer.Plan(card, _pkg, costume, slot, pics); })
                .ContinueWith(t =>
                {
                    if (IsDisposed || _step != 3) return;
                    list.Controls.Clear();
                    if (t.IsFaulted) { list.Controls.Add(Theme.Label("Could not check the skin: " + Friendly.Explain(t.Exception.GetBaseException()), Theme.Body, Theme.Bad)); return; }
                    Plan = t.Result;
                    foreach (var check in Plan.Checks.GroupBy(x => x.Title + x.Passed).Select(g => g.First()))
                    {
                        var row = CheckRow(check);
                        row.Width = Math.Max(400, list.ClientSize.Width - 24);
                        list.Controls.Add(row);
                    }
                    var mine = _lib.At(c, slot.Index);
                    string extra = mine != null ? $" Your skin \"{mine.Name}\" in this slot will be turned off (it stays in My Mods)." : "";
                    var files = string.Join(", ", Plan.Files.Select(f => Path.GetFileName(f.Key)));
                    var info = Theme.Label($"Files that go on the SD card: {files}. Pictures: {Plan.Pictures.Count} of 3.{extra}\r\nA copy of your current SD card is kept, and you can remove the skin any time to get P+'s costume back.", Theme.Small, Theme.Muted);
                    info.MaximumSize = new Size(Math.Max(400, list.ClientSize.Width - 30), 0); info.Margin = new Padding(0, 14, 0, 0);
                    list.Controls.Add(info);
                    _next.Enabled = Plan.CanInstall;
                    if (!Plan.CanInstall) _stepHint.Text = "This skin can't be installed safely. See why below.";
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static Control CheckRow(CheckResult r)
        {
            var row = new Panel { Width = 900, Height = 70, Margin = new Padding(0, 0, 0, 8), BackColor = Theme.Panel };
            var mark = Theme.Label(r.Passed ? "✓" : "✗", new Font("Segoe UI Semibold", 18f), r.Passed ? Theme.Good : Theme.Bad);
            mark.Location = new Point(14, 14);
            var title = Theme.Label(r.Title, Theme.BodyBold); title.Location = new Point(52, 10);
            var detail = new Label { Text = r.Detail, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, AutoSize = false, Location = new Point(52, 32), Size = new Size(830, 34) };
            row.Controls.AddRange(new Control[] { mark, title, detail });
            row.Layout += (s, e) =>
            {
                detail.Width = row.Width - 70;
                int h = TextRenderer.MeasureText(detail.Text, detail.Font, new Size(detail.Width, 1000), TextFormatFlags.WordBreak).Height;
                detail.Height = h + 4;
                row.Height = Math.Max(64, detail.Bottom + 10);
            };
            return row;
        }
    }
}
