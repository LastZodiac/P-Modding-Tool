using System;
using System.IO;
using System.Windows.Forms;
using SkinManager.Core;

namespace SkinManager.App
{
    static class Program
    {
        /// <summary>Downloads are unpacked into a temp folder to be read; ones older than half a day are no longer needed.</summary>
        static void CleanTemp()
        {
            // "PPlusSkinManager" is the folder versions before the rename (1.0.0) used: nothing reads it any more
            foreach (var name in new[] { "PPlusModdingTool", "PPlusSkinManager" })
            {
                try
                {
                    var dir = new System.IO.DirectoryInfo(System.IO.Path.Combine(System.IO.Path.GetTempPath(), name));
                    if (!dir.Exists) continue;
                    var cutoff = DateTime.Now.AddHours(-12);
                    foreach (var d in dir.GetDirectories()) if (d.LastWriteTime < cutoff) try { d.Delete(true); } catch { }
                    foreach (var f in dir.GetFiles()) if (f.LastWriteTime < cutoff) try { f.Delete(); } catch { }
                }
                catch { }
            }
        }

        [STAThread]   // Windows' file dialogs, drag and drop and autocomplete only work on a single-threaded (STA) main thread
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            SkinManager.Core.Installer.Quiet();
            Trace("main");
            SelfUpdate.CleanUp();
            if (SelfUpdate.Run()) return;   // a newer build was put in and started
            CleanTemp();
            SelfUpdate.RenameOldShortcuts();
            // every window gets the app's icon
            try { var icon = Theme.AppIcon; if (icon != null) typeof(Form).GetField("defaultIcon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, icon); } catch { }
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }

        /// <summary>Writes a line to the console when SKINMANAGER_TRACE is set (used when testing the app).</summary>
        public static void Trace(string line)
        {
            if (Environment.GetEnvironmentVariable("SKINMANAGER_TRACE") != null) Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + line);
        }

        /// <summary>Running under the UI tests (they set SKINMANAGER_TRACE).</summary>
        public static bool Testing => Environment.GetEnvironmentVariable("SKINMANAGER_TRACE") != null;

        /// <summary>Why the match history couldn't be read (shown on the overlays instead of "Reading..."); null when fine.</summary>
        public static string HistoryProblem;

        /// <summary>Why the overlay's hotkey couldn't be set (another program has it); null when it works.</summary>
        public static string HotkeyProblem;

        /// <summary>The app is changing the SD card right now (a save, an undo, a card switch).</summary>
        public static bool Busy;

        /// <summary>What a crash message can promise about the SD card.</summary>
        static string CardLine => Busy ? "It happened while the SD card was being changed: check My Mods to see whether the change went through." : "Your SD card was not changed.";

        public static string DataFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        /// <summary>Notes an error the app recovered from in Data\errors.log (to find out later what went wrong).</summary>
        public static void Log(string what, Exception e)
        {
            try { Directory.CreateDirectory(DataFolder); File.AppendAllText(Path.Combine(DataFolder, "errors.log"), $"{DateTime.Now:u} ({what})\r\n{e}\r\n\r\n"); } catch { }
        }

        /// <summary>What went wrong, with a button that copies the details (to paste in a message to the app's maker).</summary>
        /// <summary>What went wrong, with the way to report it (filled in for you).</summary>
        static void ShowCrash(Exception e)
        {
            using (var f = new Form { Text = "P+ Modding Tool", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new System.Drawing.Size(520, 210), BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Body, TopMost = true })
            {
                var text = new Label { AutoSize = false, Location = new System.Drawing.Point(18, 16), Size = new System.Drawing.Size(484, 120), ForeColor = Theme.Text,
                    Text = "Something went wrong:\r\n\r\n" + Friendly.Explain(e) + "\r\n\r\n" + CardLine + " Reporting it gets it fixed: it takes a few seconds." };
                var report = Theme.Primary("Report it"); report.Location = new System.Drawing.Point(18, 156);
                var ok = Theme.Secondary("Close"); ok.Location = new System.Drawing.Point(410, 156); ok.DialogResult = DialogResult.OK;
                report.Click += (s, a2) => { f.Hide(); BugReport.Show(null, e); f.Close(); };
                f.Controls.AddRange(new Control[] { text, report, ok }); f.AcceptButton = ok; f.CancelButton = ok;
                f.ShowDialog();
            }
        }

        static void Crash(Exception e)
        {
            Trace("crash: " + e);
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.AppendAllText(Path.Combine(DataFolder, "errors.log"), $"{DateTime.Now:u}\r\n{e}\r\n\r\n");
            }
            catch { }
            try { ShowCrash(e); }
            catch
            {
                MessageBox.Show("Something went wrong:\r\n\r\n" + Friendly.Explain(e) + "\r\n\r\n" + CardLine + " The details were saved to Data\\errors.log.",
                    "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
