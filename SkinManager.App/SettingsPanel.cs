// Settings: the background picture and how dark it is, plus where the app keeps its files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    class SettingsPanel : GlassPanel
    {
        readonly LookSettings _look;
        readonly Action _changed;
        readonly PictureBox _preview = new PictureBox { Size = new Size(320, 180), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Back };
        readonly TrackBar _dark = new TrackBar { Minimum = 0, Maximum = 90, TickFrequency = 10, Width = 320, BackColor = Theme.Panel };
        readonly Label _which = Theme.Label("", Theme.Small, Theme.Muted);

        public SettingsPanel(LookSettings look, Action changed)
        {
            _look = look; _changed = changed;
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8); AutoScroll = true;
            var title = Theme.Label("Settings", Theme.Title); title.Location = new Point(28, 14);

            var card = new GlassPanel { Location = new Point(28, 64), Size = new Size(720, 410), Tint = 215, TintColor = Theme.Panel };
            var h = Theme.Label("Background", Theme.Heading); h.Location = new Point(20, 16);
            var hint = Theme.Label("The picture behind the app. Pick any picture on your PC (a screenshot, fan art...).", Theme.Small, Theme.Muted); hint.Location = new Point(22, 44);
            _preview.Location = new Point(20, 76);
            var choose = Theme.Primary("Choose a picture...");
            var builtin = Theme.Secondary("Built-in background");
            var plain = Theme.Secondary("Plain (no picture)");
            choose.Location = new Point(362, 76); builtin.Location = new Point(362, 122); plain.Location = new Point(362, 160);
            _which.Location = new Point(362, 204); _which.AutoSize = false; _which.Size = new Size(340, 40);
            var darkLabel = Theme.Label("Darkness (makes text easier to read over a busy picture)", Theme.Small, Theme.Muted); darkLabel.Location = new Point(20, 266);
            _dark.Location = new Point(14, 284); _dark.Value = Math.Max(0, Math.Min(90, look.Darkness));
            choose.Click += (s, e) => Choose();
            builtin.Click += (s, e) => { _look.Background = "builtin"; Changed(); };
            plain.Click += (s, e) => { _look.Background = "none"; Changed(); };
            _dark.ValueChanged += (s, e) => { _look.Darkness = _dark.Value; Changed(); };
            card.Controls.AddRange(new Control[] { h, hint, _preview, choose, builtin, plain, _which, darkLabel, _dark });
            // the built-in background's two glows: pick their colors
            var glowLabel = Theme.Label("Built-in background colors (click one)", Theme.Small, Theme.Muted); glowLabel.Location = new Point(20, 326); glowLabel.AutoSize = true;
            card.Controls.Add(glowLabel);
            AddSwatches(card, "Glow", 350, () => _look.Glow, v => _look.Glow = v, LookSettings.GlowGold);
            AddSwatches(card, "Second glow", 378, () => _look.Glow2, v => _look.Glow2 = v, LookSettings.GlowBlue);

            var card2 = new GlassPanel { Location = new Point(28, 490), Size = new Size(720, 120), Tint = 215, TintColor = Theme.Panel };
            var h2 = Theme.Label("Your files", Theme.Heading); h2.Location = new Point(20, 16);
            var t2 = Theme.Label("Your mods, downloads and P+'s original files (to put them back) are kept in the Data folder next to this app.", Theme.Small, Theme.Muted);
            t2.Location = new Point(22, 44); t2.AutoSize = false; t2.Size = new Size(680, 20);
            var open = Theme.Secondary("Open the Data folder");
            open.Location = new Point(20, 72);
            open.Click += (s, e) => { try { Directory.CreateDirectory(Program.DataFolder); Process.Start(new ProcessStartInfo(Program.DataFolder) { UseShellExecute = true }); } catch { } };
            var desk = Theme.Secondary("Shortcut on the desktop");
            var start = Theme.Secondary("Add to the Start menu");
            desk.Top = start.Top = 72;
            card2.Layout += (s, e) => { desk.Left = open.Right + 12; start.Left = desk.Right + 12; };
            void Make(string path, string where)
            {
                try { SelfUpdate.MakeShortcut(path); MessageBox.Show(FindForm(), "Done: P+ Modding Tool is " + where + ".", "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "The shortcut couldn't be made: " + ex.Message, "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            desk.Click += (s, e) => Make(SelfUpdate.DesktopShortcut, "on your desktop");
            start.Click += (s, e) => Make(SelfUpdate.StartMenuShortcut, "in your Start menu");
            var ver = Theme.Label($"You have version {OnlineUpdate.CurrentText} (made {SelfUpdate.BuiltAt:MMM d, yyyy}). The app looks for new versions by itself when it opens.", Theme.Small, Theme.Muted);
            ver.Location = new Point(22, 120); ver.AutoSize = false; ver.Size = new Size(680, 20);
            var check = Theme.Secondary("Check for a new version");
            check.Location = new Point(20, 146);
            check.Click += (s, e) => (FindForm() as MainForm)?.CheckAppUpdate(true);
            var bug = Theme.Secondary("Report a problem...");
            bug.Location = new Point(260, 146);
            bug.Click += (s, e) => BugReport.Show(FindForm());
            card2.Height = 196;
            card2.Controls.AddRange(new Control[] { h2, t2, open, desk, start, ver, check, bug });

            var card3 = new GlassPanel { Location = new Point(28, 702), Size = new Size(720, 104), Tint = 215, TintColor = Theme.Panel };
            var h3 = Theme.Label("Add-ons", Theme.Heading); h3.Location = new Point(20, 16);
            var stats = new CheckBox { Text = "Stat Tracker: your records, Smash stats from your replays, matchups, stages and your chance to win", AutoSize = false, Size = new Size(680, 24), Location = new Point(22, 46), Checked = look.Stats, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
            var t3 = Theme.Label("Read from the history Brawl Online keeps of your games (on this PC; it's only read, never changed).", Theme.Small, Theme.Muted);
            t3.Location = new Point(40, 72); t3.AutoSize = false; t3.Size = new Size(660, 20);
            var cardBox = new CheckBox { Text = "Overlay over the game: who you're playing, your records and chance to win (a slim bar during the fight)", AutoSize = false, Size = new Size(680, 24), Location = new Point(22, 98), Checked = look.Card, Enabled = look.Stats, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
            var t4 = Theme.Label("See-through; clicks go to the game. Read from the game's log, so keep this app open while you play.", Theme.Small, Theme.Muted);
            t4.Location = new Point(40, 124); t4.AutoSize = false; t4.Size = new Size(660, 20);
            cardBox.CheckedChanged += (s, e) => { _look.Card = cardBox.Checked; Changed(); };
            stats.CheckedChanged += (s, e) => { _look.Stats = stats.Checked; cardBox.Enabled = stats.Checked; Changed(); };
            var hkLabel = Theme.Label("Full overlay over the game (everything about your opponent, or your own summary) opens and closes with:", Theme.Small, Theme.Muted);
            hkLabel.Location = new Point(40, 152); hkLabel.AutoSize = false; hkLabel.Size = new Size(560, 20);
            var hk = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Location = new Point(600, 148) };
            hk.Items.AddRange(Hotkeys.Choices);
            hk.SelectedItem = look.HotkeyText;
            hk.SelectedIndexChanged += (s, e) => { _look.HotkeyText = (string)hk.SelectedItem; Changed(); };
            var sideLabel = Theme.Label("Where the opponent card and slim bar go (Brawl Online's chat is on the left):", Theme.Small, Theme.Muted);
            sideLabel.Location = new Point(40, 186); sideLabel.AutoSize = false; sideLabel.Size = new Size(540, 20);
            var side = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Location = new Point(600, 182) };
            side.Items.AddRange(new object[] { "Top right", "Top left" });
            side.SelectedIndex = look.OverlayLeft == true ? 1 : 0;
            side.SelectedIndexChanged += (s, e) => { _look.OverlayLeft = side.SelectedIndex == 1 ? (bool?)true : null; Changed(); };
            // the look over the game: themes (seasonal by default), your own color, little animations, and a preview
            var lookLabel = Theme.Label("Overlay look:", Theme.Small, Theme.Muted); lookLabel.Location = new Point(40, 222); lookLabel.AutoSize = true;
            var lookBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text, Location = new Point(140, 218) };
            var ids = new List<string> { OverlayLook.Seasonal };
            lookBox.Items.Add("Seasonal (Halloween, Christmas...)");
            foreach (var l in OverlayLook.Looks) { ids.Add(l.Id); lookBox.Items.Add(l.Name); }
            ids.Add("custom"); lookBox.Items.Add("Your own color");
            lookBox.SelectedIndex = Math.Max(0, ids.IndexOf(look.OverlayTheme ?? OverlayLook.Seasonal));
            lookBox.SelectedIndexChanged += (s, e) => { var id = ids[lookBox.SelectedIndex]; _look.OverlayTheme = id == OverlayLook.Seasonal ? null : id; Changed(); };
            var preview = Theme.Secondary("Preview");
            preview.Location = new Point(420, 216);
            preview.Click += (s, e) => PreviewOverlay();
            var colorLabel = Theme.Label("Your own color:", Theme.Small, Theme.Muted); colorLabel.Location = new Point(40, 258); colorLabel.AutoSize = true;
            card3.Controls.Add(colorLabel);
            AddOverlaySwatches(card3, 256, () => { lookBox.SelectedIndex = ids.IndexOf("custom"); });
            var fx = new CheckBox { Text = "Little animations in the theme (snow, bats, stars)", AutoSize = false, Size = new Size(500, 24), Location = new Point(38, 288), Checked = look.OverlayEffectsOff != true, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
            fx.CheckedChanged += (s, e) => { _look.OverlayEffectsOff = fx.Checked ? null : (bool?)true; Changed(); };
            card3.Height = 326;
            card3.Controls.AddRange(new Control[] { h3, stats, t3, cardBox, t4, hkLabel, hk, sideLabel, side, lookLabel, lookBox, preview, fx });
            var card4 = new GlassPanel { Location = new Point(28, card3.Bottom + 16), Size = new Size(720, 96), Tint = 215, TintColor = Theme.Panel };
            card3.SizeChanged += (s, e) => card4.Top = card3.Bottom + 16;
            var h4 = Theme.Label("Tours", Theme.Heading); h4.Location = new Point(20, 16);
            var tours = new CheckBox { Text = "Show a short tour the first time you open each page", AutoSize = false, Size = new Size(460, 24), Location = new Point(22, 48), Checked = look.Tours, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
            tours.CheckedChanged += (s, e) => { _look.Tours = tours.Checked; Changed(); };
            var again = Theme.Secondary("Show all tours again");
            again.Location = new Point(500, 44);
            again.Click += (s, e) => { _look.ToursSeen.Clear(); _look.Tours = true; tours.Checked = true; Changed(); MessageBox.Show(FindForm(), "Each page shows its tour again the next time you open it. (The \"?\" button at the top shows a page's tour any time.)", "Tours", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            card4.Controls.AddRange(new Control[] { h4, tours, again });
            // which SD card: Brawl Online's (online play) or a mod maker's own Dolphin
            var card5 = new GlassPanel { Location = new Point(28, card4.Bottom + 16), Size = new Size(720, 172), Tint = 215, TintColor = Theme.Panel };
            var h5 = Theme.Label("SD card", Theme.Heading); h5.Location = new Point(20, 16);
            bool other = !string.IsNullOrEmpty(look.CardFile);
            var now5 = Theme.Label(other ? "Using your own Dolphin's SD card: " + look.CardFile : "Using Brawl Online's SD card (the one online games use).", Theme.Small, other ? Theme.Accent : Theme.Text);
            now5.Location = new Point(22, 44); now5.AutoSize = false; now5.Size = new Size(680, 18);
            var t5 = Theme.Label("Make or test mods in your own Dolphin? Point the app at its SD card and it puts mods there instead, with its own mods list. " +
                "Online games in Brawl Online always use Brawl Online's card, so mods on your Dolphin's card don't change them.", Theme.Small, Theme.Muted);
            t5.Location = new Point(22, 66); t5.AutoSize = false; t5.Size = new Size(680, 52);
            var pick = Theme.Secondary(other ? "Pick another Dolphin..." : "Use my own Dolphin's SD card...");
            var back = Theme.Secondary("Back to Brawl Online's card");
            pick.Location = new Point(20, 122); back.Top = 122; back.Visible = other;
            card5.Layout += (s, e) => back.Left = pick.Right + 12;
            pick.Click += (s, e) => PickDolphin();
            back.Click += (s, e) =>
            {
                if (MessageBox.Show(FindForm(), "Go back to Brawl Online's SD card? The app restarts to switch (switches you haven't saved are dropped). Your Dolphin's card and its mods list stay as they are.",
                    "SD card", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                _look.CardFile = null; _look.Save(); Application.Restart(); Environment.Exit(0);
            };
            card5.Controls.AddRange(new Control[] { h5, now5, t5, pick, back });
            AutoScroll = true;
            Controls.AddRange(new Control[] { title, card, card2, card3, card4, card5 });
            ShowPreview();
        }

        static readonly Color[] Swatches =
        {
            LookSettings.GlowGold, Color.FromArgb(255, 90, 70), Color.FromArgb(255, 80, 170), Color.FromArgb(170, 90, 255),
            LookSettings.GlowBlue, Color.FromArgb(60, 200, 255), Color.FromArgb(60, 220, 140), Color.FromArgb(240, 240, 250),
        };

        void AddOverlaySwatches(Control card, int y, Action pickedCustom)
        {
            for (int i = 0; i < Swatches.Length; i++)
            {
                var c = Swatches[i];
                var dot = new Panel { Location = new Point(150 + i * 30, y), Size = new Size(22, 22), BackColor = Color.Transparent, Cursor = Cursors.Hand };
                dot.Paint += (s, e) =>
                {
                    var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, 3, 3, 15, 15);
                    if (_look.OverlayTheme == "custom" && _look.OverlayColor == c.ToArgb()) using (var pen = new Pen(Theme.Text, 2)) g.DrawEllipse(pen, 1, 1, 19, 19);
                };
                dot.Click += (s, e) => { _look.OverlayColor = c.ToArgb(); _look.OverlayTheme = "custom"; pickedCustom(); Changed(); foreach (Control d in card.Controls) if (d is Panel) d.Invalidate(); };
                card.Controls.Add(dot);
            }
            var other = new LinkLabel { Text = "Other...", AutoSize = true, Location = new Point(150 + Swatches.Length * 30 + 6, y + 3), LinkColor = Theme.Accent, Font = Theme.Small, BackColor = Color.Transparent };
            other.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = _look.OverlayColor != null ? Color.FromArgb(_look.OverlayColor.Value) : Theme.Accent })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { _look.OverlayColor = Color.FromArgb(255, dlg.Color).ToArgb(); _look.OverlayTheme = "custom"; pickedCustom(); Changed(); }
            };
            card.Controls.Add(other);
        }

        LiveCard _overlayPreview; Timer _overlayPreviewTimer;
        /// <summary>A made-up opponent card over the app's top-right corner for a few seconds, in the chosen look.</summary>
        void PreviewOverlay()
        {
            var form = FindForm(); if (form == null) return;
            if (_overlayPreview == null)
            {
                _overlayPreview = new LiveCard
                {
                    Preview = true, Art = StatsArt.Cached,
                    State = new LiveState { Phase = MatchPhase.GameOver, OpponentName = "Your opponent", OpponentCode = "ABCD#123", Mode = "unranked", MyChar = 4, TheirChar = 12, Stage = 2, Game = 2, MyWins = 1 },
                };
                _overlayPreviewTimer = new Timer { Interval = 8000 };
                _overlayPreviewTimer.Tick += (s, e) => { _overlayPreviewTimer.Stop(); _overlayPreview.Disappear(); };
            }
            _overlayPreview.Game = form.RectangleToScreen(form.ClientRectangle);
            _overlayPreview.Refresh2(); _overlayPreview.Appear(); _overlayPreview.Invalidate();
            _overlayPreviewTimer.Stop(); _overlayPreviewTimer.Start();
        }

        /// <summary>A row of color dots (and "Other..." for any color) for one of the built-in background's glows.</summary>
        void AddSwatches(Control card, string name, int y, Func<int?> get, Action<int?> set, Color normal)
        {
            var label = Theme.Label(name, Theme.Small, Theme.Text); label.Location = new Point(22, y + 3); label.AutoSize = false; label.Size = new Size(100, 18);
            card.Controls.Add(label);
            var dots = new List<Panel>();
            void Mark() { var now = get() != null ? Color.FromArgb(get().Value) : normal; foreach (var d in dots) d.Invalidate(); }
            for (int i = 0; i < Swatches.Length; i++)
            {
                var c = Swatches[i];
                var dot = new Panel { Location = new Point(124 + i * 30, y), Size = new Size(22, 22), BackColor = Color.Transparent, Cursor = Cursors.Hand };
                dot.Paint += (s, e) =>
                {
                    var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, 3, 3, 15, 15);
                    var now = get() != null ? Color.FromArgb(get().Value) : normal;
                    if (now.ToArgb() == c.ToArgb()) using (var pen = new Pen(Theme.Text, 2)) g.DrawEllipse(pen, 1, 1, 19, 19);
                };
                dot.Click += (s, e) => { _look.Background = "builtin"; set(c.ToArgb() == normal.ToArgb() ? (int?)null : c.ToArgb()); Mark(); Changed(); };
                dots.Add(dot); card.Controls.Add(dot);
            }
            var other = new LinkLabel { Text = "Other...", AutoSize = true, Location = new Point(124 + Swatches.Length * 30 + 6, y + 3), LinkColor = Theme.Accent, Font = Theme.Small, BackColor = Color.Transparent };
            other.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { FullOpen = true, Color = get() != null ? Color.FromArgb(get().Value) : normal })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { _look.Background = "builtin"; set(Color.FromArgb(255, dlg.Color).ToArgb()); Mark(); Changed(); }
            };
            card.Controls.Add(other);
        }

        /// <summary>Your own Dolphin: pick its folder (or, if its card isn't there, the sd.raw itself), check it, switch.</summary>
        public void PickDolphin()
        {
            string picked = null;
            using (var dlg = new FolderBrowserDialog { Description = "Pick your Dolphin's folder (the one Dolphin.exe is in)", ShowNewFolderButton = false })
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) picked = dlg.SelectedPath; else return;
            var found = OtherCards.Find(picked);
            if (found.Problem != null)
            {
                if (MessageBox.Show(FindForm(), found.Problem + "\r\n\r\nPick the SD card file (sd.raw) itself instead?", "Your Dolphin's SD card", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
                using (var f = new OpenFileDialog { Title = "Pick your Dolphin's SD card", Filter = "SD card (sd.raw)|*.raw|All files|*.*", InitialDirectory = picked })
                    if (f.ShowDialog(FindForm()) == DialogResult.OK) found = OtherCards.Find(f.FileName); else return;
                if (found.Problem != null) { MessageBox.Show(FindForm(), found.Problem, "Your Dolphin's SD card", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            if (MessageBox.Show(FindForm(), "Found it:\r\n" + found.Card + "\r\n\r\n" + (found.Warning != null ? "Heads up: " + found.Warning + "\r\n\r\n" : "") +
                "The app puts mods on this card from now on, with its own mods list (your Brawl Online mods stay as they are). It restarts to switch (switches you haven't saved are dropped).",
                "Use your Dolphin's SD card", MessageBoxButtons.OKCancel, found.Warning != null ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK) return;
            string data = OtherCards.DataFolderFor(Program.DataFolder, found.Card);
            if (OtherCards.IsNew(data) && !OtherCards.IsNew(Program.DataFolder) &&
                MessageBox.Show(FindForm(), "Bring the mods in My Mods over to your Dolphin's card too?\r\n\r\nThey start turned off there, so nothing on that card changes until you turn them on. " +
                    "(Your Brawl Online card and its mods stay as they are.)", "Your Dolphin's SD card", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Cursor = Cursors.WaitCursor; OtherCards.BringMods(Program.DataFolder, data); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Your mods couldn't all be brought over: " + ex.Message + "\r\nYou can still add them again on that card.", "Your Dolphin's SD card", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Cursor = Cursors.Default; }
            }
            _look.CardFile = found.Card; _look.Save();
            Application.Restart(); Environment.Exit(0);
        }

        void Choose()
        {
            using (var dlg = new OpenFileDialog { Title = "Choose a background picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    using (var test = Image.FromFile(dlg.FileName)) { }   // can it be read?
                    Directory.CreateDirectory(Program.DataFolder);
                    // keep our own copy, so moving or deleting the original doesn't lose the background
                    string dest = Path.Combine(Program.DataFolder, "background-" + DateTime.Now.Ticks + Path.GetExtension(dlg.FileName).ToLowerInvariant());
                    File.Copy(dlg.FileName, dest, true);
                    string old = _look.Background;
                    _look.Background = dest;
                    Changed();
                    if (old != null && old.StartsWith(Program.DataFolder) && File.Exists(old)) try { File.Delete(old); } catch { }
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "That picture couldn't be opened: " + ex.Message, "Background", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void Changed()
        {
            _look.Save();
            ShowPreview();
            _changed();
        }

        void ShowPreview()
        {
            var b = new Backdrop(); b.Apply(_look);
            var bmp = new Bitmap(320, 180);
            using (var g = Graphics.FromImage(bmp)) b.Paint(g, bmp.Size);
            b.Dispose();
            var old = _preview.Image; _preview.Image = bmp; old?.Dispose();
            _which.Text = _look.Background == "builtin" ? "Using the built-in background." : _look.Background == "none" ? "No picture: plain dark." : "Using your picture.";
            _dark.Enabled = _look.Background != "none" && _look.Background != "builtin";
        }
    }
}
