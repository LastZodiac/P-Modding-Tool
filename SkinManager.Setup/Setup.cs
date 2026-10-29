// Setup for P+ Modding Tool: puts the app in a folder (in your user folder by default, no admin needed), makes
// shortcuts, and adds it to Windows' list of apps so it can be uninstalled from Settings. Run again later, it
// updates the copy already installed and leaves the mod library (the Data folder) alone.
// "Setup.exe /uninstall" (kept as Uninstall.exe in the app's folder) removes it again.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SkinManager.Setup
{
    static class Program
    {
        public const string AppName = "P+ Modding Tool";
        public const string ExeName = "P+ Modding Tool.exe";
        public const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PPlusModdingTool";
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Uninstaller.Run(args.Length > 1 ? args[1] : AppDomain.CurrentDomain.BaseDirectory);
                return;
            }
            Application.Run(new SetupForm());
        }

        public static string InstalledFolder()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RegKey)) return k?.GetValue("InstallLocation") as string; } catch { return null; }
        }

        public static bool AppRunningFrom(string folder)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try { if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName).TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; }   // can't tell: better to ask
            }
            return false;
        }

        public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        public static void MakeShortcut(string path, string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows' shortcut maker isn't available.");
            object shell = Activator.CreateInstance(type);
            try
            {
                object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Mods for Project+ on Brawl Online" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }

        // colors like the app's
        public static readonly Color Back = Color.FromArgb(22, 24, 30), Panel = Color.FromArgb(32, 35, 44), Text = Color.FromArgb(235, 236, 240),
            Muted = Color.FromArgb(160, 165, 178), Accent = Color.FromArgb(255, 176, 46), AccentText = Color.FromArgb(28, 22, 10), Bad = Color.FromArgb(235, 90, 90);

        public static Button Primary(string text) => new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = AccentText, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), AutoSize = true, Padding = new Padding(14, 4, 14, 4), Cursor = Cursors.Hand };
        public static Button Secondary(string text) => new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Text, Font = new Font("Segoe UI", 10f), AutoSize = true, Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand };
    }

    class SetupForm : Form
    {
        readonly TextBox _folder = new TextBox();
        readonly CheckBox _desktop = new CheckBox { Text = "Shortcut on the desktop", Checked = true, AutoSize = true };
        readonly CheckBox _start = new CheckBox { Text = "Add to the Start menu", Checked = true, AutoSize = true };
        readonly Button _install = Program.Primary("Install");
        readonly Label _status = new Label { AutoSize = false };
        readonly ProgressBar _bar = new ProgressBar { Visible = false };
        string _done;

        public SetupForm()
        {
            Text = Program.AppName + " " + Program.Version + " Setup";
            BackColor = Program.Back; ForeColor = Program.Text; Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(620, 360);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label { Text = Program.AppName, Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) };
            var sub = new Label { Text = "By Last Zodiac. Mods for Project+ on Brawl Online: skins, stages, music and more, safe online.", ForeColor = Program.Muted, AutoSize = false, Location = new Point(26, 60), Size = new Size(570, 22) };
            string existing = Program.InstalledFolder();
            bool update = existing != null && File.Exists(Path.Combine(existing, Program.ExeName));
            var where = new Label { Text = update ? "Already installed here. Setup updates it; your mods stay as they are:" : "Install it in this folder:", AutoSize = false, Location = new Point(26, 100), Size = new Size(570, 22) };
            _folder.Text = update ? existing : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName);
            _folder.Location = new Point(28, 126); _folder.Width = 440; _folder.BackColor = Program.Panel; _folder.ForeColor = Program.Text; _folder.BorderStyle = BorderStyle.FixedSingle;
            var browse = Program.Secondary("Change...");
            browse.Location = new Point(480, 122);
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Where should " + Program.AppName + " go? (A new folder is made inside the one you pick.)" })
                    if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = Path.Combine(d.SelectedPath, Program.AppName);
            };
            _desktop.Location = new Point(28, 170); _start.Location = new Point(28, 198);
            var note = new Label { Text = "Needs Brawl Online on this PC. Nothing on your SD card changes until you add mods in the app.", ForeColor = Program.Muted, AutoSize = false, Location = new Point(26, 238), Size = new Size(570, 40) };
            _status.Location = new Point(26, 292); _status.Size = new Size(380, 44); _status.ForeColor = Program.Muted;
            _bar.Location = new Point(28, 320); _bar.Size = new Size(360, 12);
            _install.FlatAppearance.BorderSize = 0;
            _install.Text = update ? "Update" : "Install";
            _install.Location = new Point(ClientSize.Width - 150, 300);
            _install.Click += async (s, e) => { if (_done != null) { Open(); return; } await Install(); };
            Controls.AddRange(new Control[] { title, sub, where, _folder, browse, _desktop, _start, note, _status, _bar, _install });
            AcceptButton = _install;
            Shown += (s, e) => _install.Focus();
            Shown += (s, e) => _install.Left = ClientSize.Width - _install.Width - 24;
        }

        async Task Install()
        {
            string folder;
            try { folder = Path.GetFullPath(_folder.Text.Trim()); }
            catch { MessageBox.Show(this, "That folder name isn't valid.", Text); return; }
            while (Program.AppRunningFrom(folder))
                if (MessageBox.Show(this, Program.AppName + " is open. Close it, then click Retry.", Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry) return;
            _install.Enabled = false; _folder.Enabled = false; _bar.Visible = true;
            _status.ForeColor = Program.Muted; _status.Text = "Installing...";
            bool desktop = _desktop.Checked, start = _start.Checked;
            var progress = new Progress<int>(p => _bar.Value = Math.Max(0, Math.Min(100, p)));
            IProgress<int> pr = progress;
            try
            {
                await Task.Run(() => Installer.Install(folder, desktop, start, p => pr.Report(p)));
                _done = Path.Combine(folder, Program.ExeName);
                _bar.Visible = false;
                _status.ForeColor = Program.Text;
                _status.Text = "Done! " + Program.AppName + " is installed.";
                _install.Text = "Open " + Program.AppName; _install.Enabled = true;
                _install.Left = ClientSize.Width - _install.Width - 24;
            }
            catch (Exception e)
            {
                _bar.Visible = false; _install.Enabled = true; _folder.Enabled = true;
                _status.ForeColor = Program.Bad; _status.Text = "It couldn't be installed: " + e.Message;
            }
        }

        void Open()
        {
            try { Process.Start(new ProcessStartInfo(_done) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(_done) }); } catch { }
            Close();
        }
    }

    static class Installer
    {
        public static void Install(string folder, bool desktop, bool start, Action<int> progress)
        {
            Directory.CreateDirectory(folder);
            var written = new System.Collections.Generic.List<string> { "Uninstall.exe" };
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                for (int i = 0; i < files.Count; i++)
                {
                    var e = files[i];
                    string target = Path.GetFullPath(Path.Combine(folder, e.FullName));
                    if (!target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) continue;   // the mod library is never replaced
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target)) { try { File.Delete(target); } catch { string old = target + ".old"; if (File.Exists(old)) File.Delete(old); File.Move(target, old); } }
                    e.ExtractToFile(target);
                    written.Add(e.FullName.Replace('/', Path.DirectorySeparatorChar));
                    progress((i + 1) * 90 / files.Count);
                }
            }
            // this Setup stays in the folder as the uninstaller
            string uninstaller = Path.Combine(folder, "Uninstall.exe");
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), uninstaller, StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninstaller, true);
            // what Setup put here: the only files the uninstaller removes (with the app's Update folder and old copies)
            string listFile = Path.Combine(folder, "installed-files.txt");
            var before = File.Exists(listFile) ? File.ReadAllLines(listFile) : new string[0];
            File.WriteAllLines(listFile, before.Concat(written).Distinct(StringComparer.OrdinalIgnoreCase));
            string exe = Path.Combine(folder, Program.ExeName);
            using (var k = Registry.CurrentUser.CreateSubKey(Program.RegKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "Last Zodiac");
                k.SetValue("InstallLocation", folder);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.TopDirectoryOnly).Sum(f => f.Length) / 1024;
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb + 8000), RegistryValueKind.DWord);
            }
            try { if (desktop) Program.MakeShortcut(Program.DesktopShortcut, exe); } catch { }
            try { if (start) Program.MakeShortcut(Program.StartMenuShortcut, exe); } catch { }
            progress(100);
        }
    }

    static class Uninstaller
    {
        public static void Run(string folder)
        {
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            // the folder can't be removed while this program runs from it: run a copy from the temp folder
            if (Path.GetDirectoryName(Path.GetFullPath(Application.ExecutablePath)).TrimEnd('\\').Equals(folder, StringComparison.OrdinalIgnoreCase))
            {
                string copy = Path.Combine(Path.GetTempPath(), "PPlusModdingTool-Uninstall.exe");
                File.Copy(Application.ExecutablePath, copy, true);
                Process.Start(new ProcessStartInfo(copy, "/uninstall \"" + folder + "\"") { UseShellExecute = false });
                return;
            }
            using (var f = new Form { Text = "Uninstall " + Program.AppName, BackColor = Program.Back, ForeColor = Program.Text, Font = new Font("Segoe UI", 10f),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(560, 250), AutoScaleMode = AutoScaleMode.Dpi })
            {
                var t = new Label { Text = "Remove " + Program.AppName + " from this PC?", Font = new Font("Segoe UI", 13f, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) };
                var n = new Label { Text = "Your SD card and Brawl Online aren't touched: mods you put on the card stay on it. To take them off first, open the app and turn them off (or use \"Launch unmodded\").", ForeColor = Program.Muted, AutoSize = false, Location = new Point(22, 56), Size = new Size(520, 62) };
                var data = new CheckBox { Text = "Also delete my mod library (downloaded mods, and the backups used to put P+'s files back)", AutoSize = false, Size = new Size(520, 44), Location = new Point(22, 124), ForeColor = Program.Text };
                var go = Program.Primary("Uninstall"); go.FlatAppearance.BorderSize = 0;
                var cancel = Program.Secondary("Cancel");
                go.Location = new Point(560 - 150, 190); cancel.Location = new Point(560 - 260, 192);
                cancel.Click += (s, e) => f.Close();
                go.Click += (s, e) =>
                {
                    while (Program.AppRunningFrom(folder))
                        if (MessageBox.Show(f, Program.AppName + " is open. Close it, then click Retry.", f.Text, MessageBoxButtons.RetryCancel) != DialogResult.Retry) return;
                    if (data.Checked && MessageBox.Show(f, "Delete the mod library too? Without its backups the app can't put P+'s own files back on your card later.", f.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    Remove(folder, data.Checked);
                    MessageBox.Show(f, Program.AppName + " was removed." + (data.Checked || !Directory.Exists(Path.Combine(folder, "Data")) ? "" : "\r\n\r\nYour mod library is still in:\r\n" + Path.Combine(folder, "Data")), f.Text);
                    f.Close();
                };
                f.Controls.AddRange(new Control[] { t, n, data, go, cancel });
                Application.Run(f);
            }
        }

        static void Remove(string folder, bool data)
        {
            foreach (var lnk in new[] { Program.DesktopShortcut, Program.StartMenuShortcut }) try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.RegKey, false); } catch { }
            if (!Directory.Exists(folder)) return;
            string listFile = Path.Combine(folder, "installed-files.txt");
            var files = File.Exists(listFile) ? File.ReadAllLines(listFile).Where(l => l.Trim().Length > 0).ToList() : new System.Collections.Generic.List<string> { Program.ExeName, "Uninstall.exe" };
            foreach (var rel in files)
            {
                string full = Path.GetFullPath(Path.Combine(folder, rel));
                if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (File.Exists(full)) File.Delete(full); } catch { }
                try { if (File.Exists(full + ".old")) File.Delete(full + ".old"); } catch { }
            }
            // the app's own extras: its update drop, old copies left by updates, its error log
            try { string upd = Path.Combine(folder, "Update"); if (Directory.Exists(upd)) Directory.Delete(upd, true); } catch { }
            foreach (var old in Directory.EnumerateFiles(folder, "*.old*", SearchOption.AllDirectories).ToList()) try { File.Delete(old); } catch { }
            try { File.Delete(listFile); } catch { }
            if (data) try { string d = Path.Combine(folder, "Data"); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            // folders left empty
            foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length).ToList())
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch { }
        }
    }
}
