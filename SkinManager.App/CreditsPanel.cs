// Credits: who made the app, and the projects it's built on.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SkinManager.App
{
    class CreditsPanel : GlassPanel
    {
        public const string Creator = "Last Zodiac";

        public CreditsPanel()
        {
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8); AutoScroll = true;
            var title = Theme.Label("Credits", Theme.Title); title.Location = new Point(28, 14);

            var made = new GlassPanel { Location = new Point(28, 64), Size = new Size(720, 130), Tint = 215, TintColor = Theme.Panel };
            var logo = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Location = new Point(18, 17), BackColor = Color.Transparent };
            var app = Theme.Label("P+ Modding Tool", new Font(Theme.Title.FontFamily, 20f, FontStyle.Bold)); app.Location = new Point(128, 14); app.AutoSize = false; app.Size = new Size(560, 40);
            var by = Theme.Label("by", Theme.Body, Theme.Muted); by.Location = new Point(131, 58);
            var name = Theme.Label(Creator, new Font(Theme.Title.FontFamily, 13f, FontStyle.Bold), Theme.Accent); name.Location = new Point(152, 55);
            var ver = Theme.Label($"Version {OnlineUpdate.CurrentText}  ·  free software under the GPL-3.0  ·  © 2026 Last Zodiac", Theme.Small, Theme.Muted); ver.Location = new Point(131, 90); ver.AutoSize = false; ver.Size = new Size(570, 20);
            made.Controls.AddRange(new Control[] { logo, app, by, name, ver });

            var built = new GlassPanel { Location = new Point(28, 210), Size = new Size(720, 10), Tint = 215, TintColor = Theme.Panel };
            var h = Theme.Label("Built on the work of", Theme.Heading); h.Location = new Point(20, 16);
            built.Controls.Add(h);
            int y = 52;
            void Item(string what, string who, string link)
            {
                var t = Theme.Label(what, Theme.BodyBold); t.Location = new Point(22, y); t.AutoSize = false; t.Size = new Size(500, 22);
                var d = Theme.Label(who, Theme.Small, Theme.Muted); d.Location = new Point(22, y + 22); d.AutoSize = false; d.Size = new Size(670, 20);
                built.Controls.Add(t); built.Controls.Add(d);
                if (link != null)
                {
                    var l = new Label { Text = link, AutoSize = false, Size = new Size(500, 20), Font = new Font(Theme.Small, FontStyle.Underline), ForeColor = Theme.Accent, BackColor = Color.Transparent, Location = new Point(22, y + 42), Cursor = Cursors.Hand };
                    l.Click += (s, e) => Open(link);
                    built.Controls.Add(l);
                    y += 20;
                }
                y += 50;
            }
            Item("Project+", "The Project+ team, who make the game these mods are for.", "https://projectplusgame.com");
            Item("Brawl Online", "The launcher and netplay setup the app works with.", null);
            Item("BrawlLib (from BrawlCrate)", "soopercool101 and the BrawlCrate contributors. Reads and writes Brawl's files. License: LGPL 3.0.", "https://github.com/soopercool101/BrawlCrate");
            Item("OpenTK", "The Open Toolkit library, used by BrawlLib. License: MIT.", "https://opentk.net");
            Item("NLayer", "Mark Heath, Andrew Ward and contributors. Reads MP3 files for your own music. License: MIT.", "https://github.com/naudio/NLayer");
            Item("GameBanana", "Where the Mod Store finds mods. Every mod belongs to its creator; their names are shown with their mods.", "https://gamebanana.com");
            built.Height = y + 8;

            var lic = Theme.Secondary("License texts");
            lic.Location = new Point(28, built.Bottom + 16);
            lic.Click += (s, e) =>
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSES");
                if (Directory.Exists(folder)) Open(folder);
                else MessageBox.Show(FindForm(), "The license texts come with the app's Setup (LICENSES folder).", "Credits");
            };
            Controls.AddRange(new Control[] { title, made, built, lic });
        }

        static void Open(string what)
        {
            try { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); } catch { }
        }
    }
}
