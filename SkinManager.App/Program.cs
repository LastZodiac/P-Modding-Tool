using System;
using System.IO;
using System.Windows.Forms;

namespace SkinManager.App
{
    static class Program
    {
        /// <summary>Downloads are unpacked into a temp folder to be read; ones older than half a day are no longer needed.</summary>
        static void CleanTemp()
        {
            try
            {
                var dir = new System.IO.DirectoryInfo(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PPlusModdingTool"));
                if (!dir.Exists) return;
                var cutoff = DateTime.Now.AddHours(-12);
                foreach (var d in dir.GetDirectories()) if (d.LastWriteTime < cutoff) try { d.Delete(true); } catch { }
                foreach (var f in dir.GetFiles()) if (f.LastWriteTime < cutoff) try { f.Delete(); } catch { }
            }
            catch { }
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

        public static string DataFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        /// <summary>Notes an error the app recovered from in Data\errors.log (to find out later what went wrong).</summary>
        public static void Log(string what, Exception e)
        {
            try { Directory.CreateDirectory(DataFolder); File.AppendAllText(Path.Combine(DataFolder, "errors.log"), $"{DateTime.Now:u} ({what})\r\n{e}\r\n\r\n"); } catch { }
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
            MessageBox.Show("Something went wrong:\r\n\r\n" + e?.Message + "\r\n\r\nYour SD card was not changed. The details were saved to Data\\errors.log.",
                "P+ Modding Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
