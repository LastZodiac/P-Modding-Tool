// Settings: the background picture and how dark it is, plus where the app keeps its files.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

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

            var card = new GlassPanel { Location = new Point(28, 64), Size = new Size(720, 330), Tint = 215, TintColor = Theme.Panel };
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

            var card2 = new GlassPanel { Location = new Point(28, 410), Size = new Size(720, 120), Tint = 215, TintColor = Theme.Panel };
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
            card2.Height = 196;
            card2.Controls.AddRange(new Control[] { h2, t2, open, desk, start, ver, check });

            var card3 = new GlassPanel { Location = new Point(28, 622), Size = new Size(720, 104), Tint = 215, TintColor = Theme.Panel };
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
            card3.Height = 190;
            card3.Controls.AddRange(new Control[] { h3, stats, t3, cardBox, t4, hkLabel, hk });
            var card4 = new GlassPanel { Location = new Point(28, card3.Bottom + 16), Size = new Size(720, 96), Tint = 215, TintColor = Theme.Panel };
            card3.SizeChanged += (s, e) => card4.Top = card3.Bottom + 16;
            var h4 = Theme.Label("Tours", Theme.Heading); h4.Location = new Point(20, 16);
            var tours = new CheckBox { Text = "Show a short tour the first time you open each page", AutoSize = false, Size = new Size(460, 24), Location = new Point(22, 48), Checked = look.Tours, ForeColor = Theme.Text, Font = Theme.Body, BackColor = Color.Transparent };
            tours.CheckedChanged += (s, e) => { _look.Tours = tours.Checked; Changed(); };
            var again = Theme.Secondary("Show all tours again");
            again.Location = new Point(500, 44);
            again.Click += (s, e) => { _look.ToursSeen.Clear(); _look.Tours = true; tours.Checked = true; Changed(); MessageBox.Show(FindForm(), "Each page shows its tour again the next time you open it. (The \"?\" button at the top shows a page's tour any time.)", "Tours", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            card4.Controls.AddRange(new Control[] { h4, tours, again });
            AutoScroll = true;
            Controls.AddRange(new Control[] { title, card, card2, card3, card4 });
            ShowPreview();
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
