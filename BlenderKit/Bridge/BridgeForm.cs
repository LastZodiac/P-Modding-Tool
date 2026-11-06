// The Bridge's window: 1. send a costume to Blender, 2. bring what you made in Blender back as a costume file,
// 3. set up the Blender add-on. Nothing here changes your SD card or the Mod Manager: the costume file it makes is
// added with the Mod Manager's "Add from file", through its usual checks.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkinManager.Core;

namespace PPlusBlenderBridge
{
    /// <summary>The Bridge's only window: send a costume to Blender, bring it back as a costume file, and set up the
    /// add-on.</summary>
    class BridgeForm : Form
    {
        static readonly Color Back = Color.FromArgb(22, 24, 30), Panel = Color.FromArgb(34, 37, 46), Text1 = Color.FromArgb(235, 236, 240), Muted = Color.FromArgb(150, 155, 168), Gold = Color.FromArgb(255, 176, 46);
        readonly ComboBox _who = Combo(220), _slot = Combo(260);
        readonly PictureBox _preview = new PictureBox { Size = new Size(180, 225), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 18, 24) };
        readonly Label _status = Label("", 9f, Muted);
        readonly Button _send = Big("Open it in Blender"), _file = Small("Or open a costume file (.pac)..."), _bring = Big("Choose what you saved from Blender..."), _addon = Small("Install the P+ Kit into Blender anyway (optional)"), _folder = Small("Open the P+ Blender Kit folder");
        readonly CheckBox _keepFaces = new CheckBox { Text = "Keep the costume's own face and hair pieces exactly (eyes, blinking, hair...)", Checked = true, AutoSize = true, ForeColor = Text1, BackColor = Back, Font = new Font("Segoe UI", 9.5f) };
        readonly FlowLayoutPanel _results = new FlowLayoutPanel { AutoScroll = true, BackColor = Back };
        BrawlOnlineInstall _install;
        Library _lib;

        static ComboBox Combo(int w) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w, Font = new Font("Segoe UI", 10f), BackColor = Panel, ForeColor = Text1, FlatStyle = FlatStyle.Flat };
        static Label Label(string t, float size, Color c, bool bold = false) => new Label { Text = t, AutoSize = true, ForeColor = c, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
        static Button Big(string t) => new Button { Text = t, AutoSize = true, Padding = new Padding(14, 6, 14, 6), BackColor = Gold, ForeColor = Color.FromArgb(28, 22, 10), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), Cursor = Cursors.Hand };
        static Button Small(string t) => new Button { Text = t, AutoSize = true, Padding = new Padding(8, 3, 8, 3), BackColor = Panel, ForeColor = Text1, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f), Cursor = Cursors.Hand };

        /// <summary>Lays out the three steps (send, bring back, other) and wires up their buttons.</summary>
        public BridgeForm()
        {
            Text = "P+ Blender Bridge (test)"; BackColor = Back; ForeColor = Text1; ClientSize = new Size(940, 760); StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            int y = 16;
            var title = Label("P+ Blender Bridge", 16f, Text1, true); title.Location = new Point(20, y);
            var tag = Label("test tool · separate from P+ Modding Tool · never changes your SD card", 9f, Gold); tag.Location = new Point(230, y + 10);
            y += 44;
            var h1 = Label("1.  Pick a costume and open it in Blender", 12f, Text1, true); h1.Location = new Point(20, y); y += 32;
            var l1 = Label("Fighter", 10f, Muted); l1.Location = new Point(22, y + 4);
            _who.Location = new Point(84, y);
            var l2 = Label("Costume", 10f, Muted); l2.Location = new Point(320, y + 4);
            _slot.Location = new Point(386, y);
            _preview.Location = new Point(730, 60);
            y += 40;
            var t1 = Label("Blender opens with the costume (and its skeleton) loaded and the P+ Kit panel on the right. Nothing to install: just follow the panel's steps.", 9f, Muted); t1.Location = new Point(22, y); t1.AutoSize = false; t1.Size = new Size(690, 36); y += 40;
            _send.Location = new Point(22, y); _file.Location = new Point(220, y + 6); y += 56;

            var h2 = Label("2.  Back from Blender", 12f, Text1, true); h2.Location = new Point(20, y); y += 32;
            var t2 = Label("The Blender panel's last button (\"Save and make the costume file\") makes the costume file here by itself. Then add it with P+ Modding Tool's \"Add from file...\".\r\nIf that didn't happen, pick the \"... - from Blender.glb\" with the button below.", 9f, Muted); t2.Location = new Point(22, y); t2.AutoSize = false; t2.Size = new Size(690, 54); y += 60;
            _keepFaces.Location = new Point(22, y); y += 30;
            _bring.Location = new Point(22, y); y += 52;

            var h3 = Label("Other", 12f, Text1, true); h3.Location = new Point(20, y); y += 32;
            _addon.Location = new Point(22, y); _folder.Location = new Point(380, y); y += 44;

            _status.Location = new Point(22, y); _status.AutoSize = false; _status.Size = new Size(890, 40); y += 44;
            _results.Location = new Point(20, y); _results.Size = new Size(900, ClientSize.Height - y - 10); _results.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.AddRange(new Control[] { title, tag, h1, l1, _who, l2, _slot, _preview, t1, _send, _file, h2, t2, _keepFaces, _bring, h3, _addon, _folder, _status, _results });

            foreach (var c in Characters.All.OrderBy(c => c.Name)) _who.Items.Add(c);
            _who.SelectedIndexChanged += (s, e) => FillSlots();
            _slot.SelectedIndexChanged += (s, e) => ShowPreview();
            _send.Click += (s, e) => SendPicked();
            _file.Click += (s, e) => SendFile();
            _bring.Click += (s, e) => BringBack();
            _addon.Click += (s, e) => AddonHelp();
            _folder.Click += (s, e) => { Directory.CreateDirectory(BridgeWork.KitFolder); Open(BridgeWork.KitFolder); };
            Shown += (s, e) => Start();
        }

        /// <summary>Once the window shows: finds Brawl Online's card and the Mod Manager's list, picks a first fighter,
        /// and brings back a file straight away if the Blender add-on started the Bridge with one.</summary>
        void Start()
        {
            _install = BrawlOnlineInstall.Find();
            if (_install == null || !File.Exists(_install.CardPath)) { Say("Brawl Online's SD card wasn't found on this PC: use \"Or open a costume file (.pac)...\" instead.", true); }
            // the Mod Manager's list, for the names of costumes added with it (it sits next to this folder)
            try
            {
                foreach (var d in new[] { Path.Combine(Application.StartupPath, "..", "Data"), Path.Combine(Application.StartupPath, "Data") })
                    if (File.Exists(Path.Combine(d, "installed.json"))) { _lib = new Library(Path.GetFullPath(d)); break; }
            }
            catch { _lib = null; }
            _who.SelectedItem = _who.Items.Cast<Character>().FirstOrDefault(c => c.Name == "Peach") ?? _who.Items[0];
            if (_bringOnStart != null) Bring(_bringOnStart, _ownHead);
        }

        /// <summary>A costume slot in the Costume list, named with the mod on it when the Mod Manager added
        /// one.</summary>
        sealed class SlotItem { public CostumeSlot Slot; public string Text; public override string ToString() => Text; }

        /// <summary>Lists the picked fighter's costumes that are on the card.</summary>
        void FillSlots()
        {
            _slot.Items.Clear(); _preview.Image = null;
            var c = _who.SelectedItem as Character;
            if (c == null || _install == null || !File.Exists(_install.CardPath)) return;
            try
            {
                using (var card = new Fat32Image(_install.CardPath, false))
                    foreach (var s in PPlusCard.ReadSlots(card, c))
                    {
                        if (!s.OnCard) continue;   // only what's on the card (the rest is on the game disc)
                        var mine = _lib?.Skins.FirstOrDefault(k => k.Enabled && k.SlotListId == c.SlotListId && k.FileNumber == s.FileNumber);
                        _slot.Items.Add(new SlotItem { Slot = s, Text = $"costume {s.Index + 1}" + (mine != null ? ": " + mine.Name : "") });
                    }
            }
            catch (Exception e) { Say("Couldn't read the SD card: " + e.Message, true); return; }
            if (_slot.Items.Count > 0) _slot.SelectedIndex = 0;
        }

        byte[] Read(Func<Fat32Image, byte[]> f) { using (var card = new Fat32Image(_install.CardPath, false)) return f(card); }

        /// <summary>Draws the picked costume in the preview box (in the background; dropped if another costume was
        /// picked meanwhile).</summary>
        async void ShowPreview()
        {
            var c = _who.SelectedItem as Character; var it = _slot.SelectedItem as SlotItem;
            if (c == null || it == null) return;
            try
            {
                var pic = await Task.Run(() => ModelPreview.Render(Read(card => card.ReadFile(it.Slot.CostumePath(c))), 180, 225));
                if (_slot.SelectedItem == it) _preview.Image = pic;
            }
            catch { }
        }

        void Say(string t, bool bad = false) { _status.Text = t; _status.ForeColor = bad ? Color.FromArgb(255, 110, 100) : Muted; }
        static void Open(string path) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }
        /// <summary>Opens the folder with the file picked out (highlighted).</summary>
        static void ShowFile(string file)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); }
            catch { Open(Path.GetDirectoryName(file)); }
        }
        void Busy(bool b) { _send.Enabled = _file.Enabled = _bring.Enabled = !b; Cursor = b ? Cursors.WaitCursor : Cursors.Default; }

        /// <summary>Sends the fighter and costume picked in the lists to Blender.</summary>
        async void SendPicked()
        {
            var c = _who.SelectedItem as Character; var it = _slot.SelectedItem as SlotItem;
            if (c == null || it == null) { Say("Pick a fighter and a costume first.", true); return; }
            await Send(c, it.Slot, $"{c.Name} {it.Text}".Replace(":", " -"), () => Read(card => card.ReadFile(it.Slot.CostumePath(c))));
        }

        /// <summary>Sends a costume .pac picked from disk, working out the fighter and slot number from its file name
        /// (FitPeach19.pac).</summary>
        async void SendFile()
        {
            using (var d = new OpenFileDialog { Title = "Open a costume file", Filter = "Costume (Fit*.pac)|*.pac" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileNameWithoutExtension(d.FileName);
                var c = Characters.All.Where(x => name.StartsWith("Fit" + x.FilePrefix, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.FilePrefix.Length).FirstOrDefault();
                if (c == null) { Say("The file's name should start with Fit and the fighter's name (like FitPeach19.pac), so the Bridge knows whose it is.", true); return; }
                int.TryParse(name.Substring(3 + c.FilePrefix.Length), out int nn);
                await Send(c, new CostumeSlot { FileNumber = nn, Index = 0, OnCard = true }, $"{c.Name} {name}", () => File.ReadAllBytes(d.FileName));
            }
        }

        /// <summary>Gets the costume (and the fighter's own file, for its skeleton) ready for Blender and opens Blender
        /// with it, asking once where Blender is if it can't be found.</summary>
        async Task Send(Character c, CostumeSlot slot, string label, Func<byte[]> costume)
        {
            Busy(true); Say("Sending " + label + " to Blender...");
            try
            {
                var r = await Task.Run(() =>
                {
                    byte[] fighter = null;
                    if (_install != null && File.Exists(_install.CardPath))
                        try { fighter = Read(card => { string p = c.FighterFolderPath + "/Fit" + c.FilePrefix + ".pac"; return card.Exists(p) ? card.ReadFile(p) : null; }); } catch { }
                    return BridgeWork.Send(costume(), fighter, c, slot, label);
                });
                // straight into Blender, with the P+ Kit loaded and the costume open
                string blender = BridgeWork.FindBlender();
                if (blender == null)
                {
                    MessageBox.Show(this, "Show the Bridge where Blender is (blender.exe, usually in Program Files > Blender Foundation > Blender 4.x). It's only asked once.", "Where's Blender?", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    using (var d = new OpenFileDialog { Title = "Where is blender.exe?", Filter = "Blender|blender.exe|Programs|*.exe" })
                        if (d.ShowDialog(this) == DialogResult.OK) { blender = d.FileName; BridgeWork.RememberBlender(blender); }
                }
                if (blender != null)
                {
                    BridgeWork.OpenInBlender(blender, r.Glb);
                    Say($"Opening Blender with {label}... The P+ Kit panel is on the right of the 3D view: follow its steps.");
                }
                else
                {
                    Say($"Sent: {Path.GetFileName(r.Glb)}. In Blender, open it with the P+ Kit's \"Open a P+ costume\".");
                    Open(Path.GetDirectoryName(r.Glb));
                }
            }
            catch (Exception e) { Say("Couldn't send it: " + e.Message, true); }
            finally { Busy(false); }
        }

        string _bringOnStart; bool _ownHead;
        public void BringOnStart(string glb, bool ownHead) { _bringOnStart = glb; _ownHead = ownHead; }

        /// <summary>Asks for the .glb saved from Blender and makes it into a costume file.</summary>
        void BringBack()
        {
            using (var d = new OpenFileDialog { Title = "What you saved from Blender", Filter = "Saved from Blender (*.glb)|*.glb", InitialDirectory = Directory.Exists(BridgeWork.KitFolder) ? BridgeWork.KitFolder : "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Bring(d.FileName, false);
            }
        }

        /// <summary>Makes a costume file from a .glb saved in Blender, then shows before/after pictures and the file. A
        /// .glb saved outside a Bridge folder is first tied to the costume picked above. ownHead: the head was made in
        /// Blender, so the costume's own face pieces aren't kept.</summary>
        async void Bring(string glb, bool ownHead)
        {
            // a file saved somewhere else (not into the folder the costume was sent to): it's made for the costume picked above
            if (KitInfo.Load(Path.GetDirectoryName(glb)) == null)
            {
                var c = _who.SelectedItem as Character; var it = _slot.SelectedItem as SlotItem;
                if (c == null || it == null || _install == null || !File.Exists(_install.CardPath))
                { Say("That file wasn't saved into a folder the Bridge made. Pick the fighter and costume it's for above, then choose the file again.", true); return; }
                string label = $"{c.Name} {it.Text}".Replace(":", " -");
                if (MessageBox.Show(this, "This file wasn't saved into a folder the Bridge made.\r\n\r\nMake it into " + label + " (the costume picked above)?",
                        "P+ Blender Bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Busy(true); Say("Getting " + label + " ready...");
                try
                {
                    string src = glb;
                    glb = await Task.Run(() =>
                    {
                        byte[] fighter = null;
                        try { fighter = Read(card => { string p = c.FighterFolderPath + "/Fit" + c.FilePrefix + ".pac"; return card.Exists(p) ? card.ReadFile(p) : null; }); } catch { }
                        var sent = BridgeWork.Send(Read(card => card.ReadFile(it.Slot.CostumePath(c))), fighter, c, it.Slot, label);
                        string dest = Path.Combine(Path.GetDirectoryName(sent.Glb), BridgeWork.Safe(label) + " - from Blender.glb");
                        if (!string.Equals(Path.GetFullPath(dest), Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) File.Copy(src, dest, true);
                        return dest;
                    });
                }
                catch (Exception e) { Say("Couldn't get the costume ready: " + e.Message, true); Busy(false); return; }
            }
            bool keep = _keepFaces.Checked && !ownHead;
            Busy(true); Say("Making the costume file from " + Path.GetFileName(glb) + "... (this can take a minute)");
            try
            {
                var r = await Task.Run(() => RunSta(() => BridgeWork.Bring(glb, keep, () =>
                {
                    var info = KitInfo.Load(Path.GetDirectoryName(glb));
                    var c = Characters.All.FirstOrDefault(x => x.Name == info?.Character);
                    if (c == null || _install == null || !File.Exists(_install.CardPath)) return null;
                    try { return Read(card => { foreach (var n in new[] { "MotionEtc", "Motion" }) { string p = $"{c.FighterFolderPath}/Fit{c.FilePrefix}{n}.pac"; if (card.Exists(p)) return card.ReadFile(p); } return null; }); } catch { return null; }
                }, _install != null && File.Exists(_install.CardPath) ? (Func<string, byte[]>)(p => Read(card => card.Exists(p) ? card.ReadFile(p) : null)) : null, ownHead)));
                ShowResult(r);
                Say($"Made {Path.GetFileName(r.SavedTo)} ({r.Costume.Length / 1024:N0} KB), in {Path.GetDirectoryName(r.SavedTo)}. Add it with P+ Modding Tool → \"Add from file...\" (the button below shows it).");
                ShowFile(r.SavedTo);
            }
            catch (Exception e) { Say("Couldn't make it: " + e.Message, true); }
            finally { Busy(false); }
        }

        /// <summary>BrawlLib's model importer is a window class: it runs on its own STA thread.</summary>
        static T RunSta<T>(Func<T> work)
        {
            T result = default; Exception error = null;
            var t = new System.Threading.Thread(() => { try { result = work(); } catch (Exception e) { error = e; } });
            t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start(); t.Join();
            if (error != null) throw error;
            return result;
        }

        /// <summary>Shows the result: a button to find the costume file, before and after pictures, and the notes from
        /// making it.</summary>
        void ShowResult(BridgeWork.Back r)
        {
            foreach (Control c in _results.Controls) c.Dispose();
            _results.Controls.Clear();
            if (r.SavedTo != null)
            {
                var show = Big("Show the costume file (to add in P+ Modding Tool)");
                show.Margin = new Padding(0, 0, 0, 10);
                string file = r.SavedTo;
                show.Click += (s, e) => ShowFile(file);
                _results.Controls.Add(show); _results.SetFlowBreak(show, true);
            }
            void Row(string label, Bitmap[] pics)
            {
                var l = Label(label, 10f, Text1, true); l.Width = 860; l.AutoSize = false; l.Height = 22; _results.Controls.Add(l); _results.SetFlowBreak(l, true);
                foreach (var p in pics) _results.Controls.Add(new PictureBox { Image = p, Size = new Size(220, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 18, 24), Margin = new Padding(0, 0, 8, 8) });
                _results.SetFlowBreak(_results.Controls[_results.Controls.Count - 1], true);
            }
            Row("Before (the costume you sent): standing, waiting, side smash", r.Before);
            Row("After (from Blender): standing, waiting, side smash", r.After);
            var notes = Label(string.Join("\r\n", r.Notes), 9f, Muted); notes.MaximumSize = new Size(860, 0); _results.Controls.Add(notes);
        }

        /// <summary>Installs and turns on the P+ Kit in Blender (for opening Blender on your own): Blender does it itself,
        /// in the background, and saves it in its preferences.</summary>
        async void AddonHelp()
        {
            string addon = Path.Combine(Application.StartupPath, "pplus_blender_kit.py");
            string blender = BridgeWork.FindBlender();
            if (blender == null)
                using (var d = new OpenFileDialog { Title = "Where is blender.exe?", Filter = "Blender|blender.exe|Programs|*.exe" })
                    if (d.ShowDialog(this) == DialogResult.OK) { blender = d.FileName; BridgeWork.RememberBlender(blender); }
            if (blender == null || !File.Exists(addon)) return;
            Say("Installing the P+ Kit into Blender...");
            string py = "import bpy; bpy.ops.preferences.addon_install(filepath=r'" + addon.Replace("'", "\\'") + "', overwrite=True); " +
                        "bpy.ops.preferences.addon_enable(module='pplus_blender_kit'); bpy.ops.wm.save_userpref(); print('PPLUS-KIT-OK')";
            try
            {
                string output = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo(blender, "-b --python-expr \"" + py.Replace("\"", "\\\"") + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(120000); return o; }
                });
                if (output.Contains("PPLUS-KIT-OK")) Say("Done: the P+ Kit is installed and turned on in Blender. In the 3D view, press N (with the mouse over the 3D view): the \"P+ Kit\" tab is on the right.");
                else Say("Blender didn't confirm the install. \"Open it in Blender\" works without installing anything.", true);
            }
            catch (Exception e) { Say("Couldn't run Blender: " + e.Message, true); }
        }
    }

    /// <summary>Starts the Bridge.</summary>
    static class Program
    {
        /// <summary>Opens the Bridge window; --bring "file.glb" [--own-head] (from the Blender add-on) makes that file
        /// into a costume as soon as it opens.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // from the Blender add-on's "Save and make the costume file": --bring "<glb>" [--own-head]
            int i = Array.IndexOf(args, "--bring");
            var form = new BridgeForm();
            if (i >= 0 && i + 1 < args.Length) form.BringOnStart(args[i + 1], args.Contains("--own-head"));
            Application.Run(form);
        }
    }
}
