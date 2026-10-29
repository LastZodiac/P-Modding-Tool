// Your own music: pick MP3/WAV files, set each one's name, where it loops and how loud it is (with a listen
// button), and the app makes them into songs the game plays. Then the usual music window asks where they go
// (added to a stage's songs, or replacing a P+ song).
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
    class MusicImportForm : Form
    {
        /// <summary>The folder with the finished songs (.brstm), once made.</summary>
        public string ResultFolder { get; private set; }

        sealed class Row
        {
            public string File; public AudioClip Clip; public Panel Card;
            public TextBox Name; public ComboBox Loop; public TextBox LoopAt; public TrackBar Volume; public Label Info, VolText;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly FlowLayoutPanel _list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 4, 24, 4), BackColor = Theme.Back };
        readonly ComboBox _quality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
        readonly CheckBox _even = new CheckBox { Text = "Make them as loud as P+'s own songs", Checked = true, AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        readonly Button _next = Theme.Primary("Next: choose where they play  →"), _add = Theme.Secondary("+ Add more songs…");
        readonly Label _summary = Theme.Label("", Theme.Body, Theme.Muted);
        readonly ProgressBar _bar = new ProgressBar { Visible = false, Width = 260, Height = 10 };
        System.Media.SoundPlayer _player;
        bool _working;

        public MusicImportForm(IEnumerable<string> files)
        {
            Text = "Add your own music";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(980, 720); MinimumSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var head = new Panel { Dock = DockStyle.Top, Height = 132 };
            var title = Theme.Label("Add your own music", Theme.Title); title.Location = new Point(24, 14);
            var hint = new Label
            {
                Text = "MP3 or WAV files become songs the game can play. Give each a name (it shows in P+'s song list), choose where it loops back to when it ends, and listen to check. Music is only what you hear: it never changes the game online.",
                Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(26, 50), Size = new Size(920, 44), BackColor = Color.Transparent,
            };
            _quality.Items.AddRange(new object[] { "Standard: 32 kHz, like P+'s own songs (smaller)", "High: 44.1 kHz (about a third bigger)" });
            _quality.SelectedIndex = 0;
            _quality.Location = new Point(26, 96); _even.Location = new Point(400, 99);
            _add.Location = new Point(head.Width - 180, 94); _add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _add.Click += (s, e) => { var more = Pick(this); if (more != null) AddFiles(more); };
            head.Controls.AddRange(new Control[] { title, hint, _quality, _even, _add });
            head.Resize += (s, e) => { hint.Width = head.Width - 50; _add.Left = head.Width - _add.Width - 24; };

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Panel };
            _summary.Location = new Point(24, 14); _summary.AutoSize = false; _summary.Size = new Size(520, 22);
            _bar.Location = new Point(26, 40);
            var cancel = Theme.Secondary("Cancel");
            cancel.Click += (s, e) => { if (!_working) { DialogResult = DialogResult.Cancel; Close(); } };
            _next.Click += async (s, e) => await Make();
            foot.Controls.AddRange(new Control[] { _summary, _bar, cancel, _next });
            void LayoutFoot() { _next.Location = new Point(foot.Width - _next.Width - 24, (foot.Height - _next.Height) / 2); cancel.Location = new Point(_next.Left - cancel.Width - 10, (foot.Height - cancel.Height) / 2); }
            foot.Resize += (s, e) => LayoutFoot(); _next.SizeChanged += (s, e) => LayoutFoot();
            Controls.Add(_list); Controls.Add(foot); Controls.Add(head);
            _list.Resize += (s, e) => { foreach (Control c in _list.Controls) c.Width = Math.Max(700, _list.ClientSize.Width - 50); };
            FormClosed += (s, e) => { try { _player?.Stop(); _player?.Dispose(); } catch { } };
            AddFiles(files);
        }

        /// <summary>The file picker for music (several at once).</summary>
        public static List<string> Pick(IWin32Window owner)
        {
            using (var d = new OpenFileDialog { Title = "Choose your music (MP3 or WAV)", Filter = "Music|*.mp3;*.wav|All files|*.*", Multiselect = true })
                return d.ShowDialog(owner) == DialogResult.OK ? d.FileNames.ToList() : null;
        }

        async void AddFiles(IEnumerable<string> files)
        {
            foreach (var f in files.Where(MusicImport.IsAudio))
            {
                if (_rows.Any(r => string.Equals(r.File, f, StringComparison.OrdinalIgnoreCase))) continue;
                var row = new Row { File = f };
                _rows.Add(row);
                BuildCard(row);
                row.Info.Text = "Reading…";
                try
                {
                    row.Clip = await Task.Run(() => MusicImport.Load(f));
                    if (row.Name.Text == Path.GetFileNameWithoutExtension(f) || row.Name.Text == "") row.Name.Text = Title(row.Clip);
                    row.Info.Text = $"{Path.GetFileName(f)}  ·  {row.Clip.Duration:m\\:ss}  ·  {row.Clip.Rate / 1000.0:0.#} kHz";
                }
                catch (Exception e) { row.Info.Text = "Couldn't read this file: " + e.Message; row.Info.ForeColor = Theme.Bad; }
                Summary();
            }
            Summary();
        }

        static string Title(AudioClip c) => (c.Title ?? "My song").Trim();

        void BuildCard(Row row)
        {
            var card = new Panel { Width = Math.Max(700, _list.ClientSize.Width - 50), Height = 112, BackColor = Theme.Panel, Margin = new Padding(0, 6, 0, 6) };
            row.Card = card;
            var ln = Theme.Label("Name", Theme.Small, Theme.Muted); ln.Location = new Point(14, 10); ln.AutoSize = false; ln.Size = new Size(200, 16);
            row.Name = new TextBox { Location = new Point(14, 28), Width = 330, Text = Path.GetFileNameWithoutExtension(row.File), BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, MaxLength = 60 };
            row.Info = Theme.Label("", Theme.Small, Theme.Muted); row.Info.Location = new Point(14, 60); row.Info.AutoSize = false; row.Info.Size = new Size(340, 40);

            var ll = Theme.Label("When it ends", Theme.Small, Theme.Muted); ll.Location = new Point(366, 10); ll.AutoSize = false; ll.Size = new Size(200, 16);
            row.Loop = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(366, 28), Width = 200, BackColor = Theme.Back, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
            row.Loop.Items.AddRange(new object[] { "Start again from the top", "Loop back to a time…", "Stop (play once)" });
            row.Loop.SelectedIndex = 0;
            row.LoopAt = new TextBox { Location = new Point(572, 28), Width = 60, Text = "0:00", BackColor = Theme.Back, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Visible = false };
            row.Loop.SelectedIndexChanged += (s, e) => row.LoopAt.Visible = row.Loop.SelectedIndex == 1;

            var lv = Theme.Label("Volume", Theme.Small, Theme.Muted); lv.Location = new Point(366, 62);
            row.Volume = new TrackBar { Minimum = -6, Maximum = 6, Value = 0, TickStyle = TickStyle.None, AutoSize = false, Height = 26, Width = 160, Location = new Point(410, 58), BackColor = Theme.Panel };
            row.VolText = Theme.Label("as is", Theme.Small, Theme.Text); row.VolText.Location = new Point(574, 62);
            row.Volume.ValueChanged += (s, e) => row.VolText.Text = row.Volume.Value == 0 ? "as is" : (row.Volume.Value > 0 ? "+" : "") + row.Volume.Value + " dB";

            var listen = Theme.Secondary("▶ Listen"); listen.Location = new Point(650, 24);
            var loop = Theme.Secondary("▶ Hear the loop"); loop.Location = new Point(650, 58);
            var remove = Theme.Secondary("✕"); remove.Width = 34;
            listen.Click += (s, e) => Play(row, false);
            loop.Click += (s, e) => Play(row, true);
            remove.Click += (s, e) => { if (_working) return; _rows.Remove(row); _list.Controls.Remove(card); Summary(); };
            card.Controls.AddRange(new Control[] { ln, row.Name, row.Info, ll, row.Loop, row.LoopAt, lv, row.Volume, row.VolText, listen, loop, remove });
            remove.Location = new Point(card.Width - remove.Width - 12, 10); remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _list.Controls.Add(card);
        }

        MusicOptions Options(Row r)
        {
            var o = new MusicOptions { Rate = _quality.SelectedIndex == 1 ? 44100 : 32000, Even = _even.Checked, VolumeDb = r.Volume.Value, Loop = r.Loop.SelectedIndex != 2 };
            if (r.Loop.SelectedIndex == 1) o.LoopStartSeconds = ParseTime(r.LoopAt.Text);
            return o;
        }

        /// <summary>"1:23", "83" or "1:23.5" → seconds.</summary>
        static double ParseTime(string t)
        {
            t = (t ?? "").Trim();
            var parts = t.Split(':');
            double s = 0;
            try { foreach (var p in parts) s = s * 60 + double.Parse(p, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
            return Math.Max(0, s);
        }

        async void Play(Row r, bool loop)
        {
            if (r.Clip == null) return;
            try
            {
                var o = Options(r);
                var wav = await Task.Run(() => loop ? MusicImport.LoopPreview(r.Clip, o) : MusicImport.Preview(r.Clip, o));
                _player?.Stop(); _player?.Dispose();
                _player = new System.Media.SoundPlayer(new MemoryStream(wav));
                _player.Play();
                _summary.Text = loop ? $"Playing \"{r.Name.Text}\": the last seconds, then where it loops back to." : $"Playing the start of \"{r.Name.Text}\" as it will sound.";
            }
            catch (Exception e) { _summary.Text = "Couldn't play it here: " + e.Message; }
        }

        void Summary()
        {
            int ok = _rows.Count(r => r.Clip != null);
            _summary.Text = _rows.Count == 0 ? "Add some songs with \"+ Add more songs…\"." : $"{ok} song{(ok == 1 ? "" : "s")} ready" + (ok < _rows.Count ? $" ({_rows.Count - ok} still reading or couldn't be read)" : "");
            _next.Enabled = ok > 0 && !_working;
        }

        async Task Make()
        {
            var todo = _rows.Where(r => r.Clip != null).ToList();
            if (todo.Count == 0 || _working) return;
            _working = true; _next.Enabled = false; _bar.Visible = true;
            try { _player?.Stop(); } catch { }
            string folder = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "music-" + Guid.NewGuid().ToString("N"), "My music");
            Directory.CreateDirectory(folder);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    var r = todo[i];
                    string name = Safe(r.Name.Text.Trim().Length > 0 ? r.Name.Text.Trim() : Title(r.Clip));
                    string unique = name; int k = 2; while (!used.Add(unique)) unique = name + " " + k++;
                    var o = Options(r);
                    int index = i;
                    _summary.Text = $"Making song {i + 1} of {todo.Count}: \"{unique}\"…";
                    var bytes = await Task.Run(() => MusicImport.MakeBrstm(r.Clip, o, p => BeginInvoke((Action)(() => _bar.Value = (int)(((index + p) / todo.Count) * 100)))));
                    File.WriteAllBytes(Path.Combine(folder, unique + ".brstm"), bytes);
                }
                ResultFolder = folder;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception e)
            {
                Program.Log("make music", e);
                _summary.Text = "Couldn't make it: " + e.Message;
                _working = false; _bar.Visible = false; Summary();
            }
        }

        static string Safe(string n)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, ' ');
            n = n.Replace('.', ' ').Trim();
            return n.Length > 48 ? n.Substring(0, 48).Trim() : n.Length == 0 ? "My song" : n;
        }
    }
}
