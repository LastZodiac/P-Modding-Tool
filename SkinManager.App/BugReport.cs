// Reporting a problem: a short window where you say what happened; the app adds what helps fix it (its version,
// Windows, P+'s version, the last errors it noted). It goes to the app's GitHub "Issues" page, filled in (you only
// press Submit there), or it's copied to paste on Discord. Nothing is sent without you: a report sent by itself would
// need a key inside the app, and anyone could take that key out of it.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SkinManager.App
{
    static class BugReport
    {
        const string FallbackRepo = "LastZodiac/P-Modding-Tool";

        /// <summary>The facts that help: versions, Windows, and the end of the error log.</summary>
        public static string Facts(Exception crash = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"App: P+ Modding Tool {OnlineUpdate.CurrentText} (built {SelfUpdate.BuiltAt:yyyy-MM-dd})");
            sb.AppendLine($"Windows: {Environment.OSVersion.Version} {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
            try { var v = File.ReadAllText(Path.Combine(Program.DataFolder, "versions.txt")).Trim(); if (v.Length > 0) sb.AppendLine("Brawl Online / Dolphin / P+: " + v.Replace("|", " / ")); } catch { }
            if (crash != null) { sb.AppendLine(); sb.AppendLine("Crash:"); sb.AppendLine(crash.ToString()); }
            try
            {
                var log = Path.Combine(Program.DataFolder, "errors.log");
                if (File.Exists(log))
                {
                    var text = File.ReadAllText(log);
                    if (text.Length > 6000) text = "…" + text.Substring(text.Length - 6000);
                    sb.AppendLine(); sb.AppendLine("Last errors noted (Data\\errors.log):"); sb.AppendLine(text.Trim());
                }
            }
            catch { }
            // your Windows user name is in file paths: left out
            string text2 = sb.ToString();
            try { var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); if (!string.IsNullOrEmpty(home)) text2 = text2.Replace(home, "%USERPROFILE%"); } catch { }
            try { text2 = text2.Replace(Environment.UserName, "<user>"); } catch { }
            return text2;
        }

        /// <summary>Opens the app's GitHub "new issue" page with the report filled in.</summary>
        public static void OpenIssue(string title, string body)
        {
            string repo = OnlineUpdate.Repo ?? FallbackRepo;
            const int max = 6500;   // web addresses can only be so long
            if (body.Length > max) body = body.Substring(0, max) + "\n…(cut: the rest was copied, paste it below if you like)";
            string url = $"https://github.com/{repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        /// <summary>The "Report a problem" window.</summary>
        public static void Show(IWin32Window owner, Exception crash = null, string what = null)
        {
            using (var f = new Form { Text = "Report a problem", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, ClientSize = new Size(560, 400), BackColor = Theme.Back, ForeColor = Theme.Text, Font = Theme.Body, TopMost = owner == null })
            {
                var intro = new Label { AutoSize = false, Location = new Point(18, 14), Size = new Size(524, 54), ForeColor = Theme.Muted, Font = Theme.Small,
                    Text = "Say what you were doing and what happened. The app adds its version, your Windows and P+ versions and the last errors it noted (nothing else: no files, no names)." };
                var title = new TextBox { Location = new Point(18, 72), Width = 524, Font = Theme.Body, Text = crash != null ? "Crash: " + crash.Message : what ?? "" };
                var cue = new Label { Text = "What happened (and what you clicked just before):", AutoSize = false, Size = new Size(524, 18), Location = new Point(18, 104), ForeColor = Theme.Muted, Font = Theme.Small };
                var desc = new TextBox { Location = new Point(18, 124), Size = new Size(524, 170), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Theme.Body };
                var note = new Label { AutoSize = false, Location = new Point(18, 300), Size = new Size(524, 36), ForeColor = Theme.Muted, Font = Theme.Small,
                    Text = "\"Send on GitHub\" opens the app's Issues page with it all filled in: press Submit there (a free GitHub account is needed). Or copy it and paste it on Discord." };
                var send = Theme.Primary("Send on GitHub"); send.Location = new Point(18, 346);
                var copy = Theme.Secondary("Copy it"); copy.Location = new Point(190, 346);
                var close = Theme.Secondary("Close"); close.Location = new Point(460, 346); close.DialogResult = DialogResult.Cancel;
                string Body() => (desc.Text.Trim().Length > 0 ? desc.Text.Trim() : "(no description)") + "\n\n---\n" + Facts(crash);
                string Title() => title.Text.Trim().Length > 0 ? title.Text.Trim() : "A problem";
                send.Click += (s, e) => { try { Clipboard.SetText(Title() + "\n\n" + Body()); } catch { } OpenIssue(Title(), Body()); send.Text = "Opened"; };
                copy.Click += (s, e) => { try { Clipboard.SetText(Title() + "\n\n" + Body()); copy.Text = "Copied!"; } catch { } };
                f.Controls.AddRange(new Control[] { intro, title, cue, desc, note, send, copy, close });
                f.CancelButton = close;
                f.Shown += (s, e) => (title.Text.Length == 0 ? (Control)title : desc).Focus();
                if (owner == null) f.ShowDialog(); else f.ShowDialog(owner);
            }
        }
    }
}
