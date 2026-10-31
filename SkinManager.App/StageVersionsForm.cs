// "Choose stage versions": which version of a stage you get by just picking its icon. Every P+ stage has its normal
// version and others you get by holding L, R or Z (or from its list). A version that plays exactly like the normal one is
// swapped for real (picking the stage gives it, its button gives the normal one). One that plays differently can't be
// swapped without online games going out of sync, so its scenery goes on the normal stage instead (P+'s gameplay stays).
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
    class StageVersionsForm : Form
    {
        readonly Installer _installer;
        readonly Library _lib;

        /// <summary>What to do when it closes with OK (null: nothing).</summary>
        public Installer.Change Change { get; private set; }
        public string Done { get; private set; }

        sealed class Version
        {
            public StageSpot Spot;                        // the first way to get it (its button, its settings)
            public List<string> Files = new List<string>();
            public string Name;                           // "Hold L: SV Sakura"
            public string Label;                          // "SV Sakura"
            public override string ToString() => Name;
        }
        sealed class Slot
        {
            public int Id; public string Name; public Version Normal; public List<Version> Others = new List<Version>();
            public override string ToString() => Name;
        }

        readonly ListBox _stages = new ListBox { Font = Theme.Body, BorderStyle = BorderStyle.None, IntegralHeight = false };
        readonly FlowLayoutPanel _choices = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
        readonly Label _status = Theme.Label("Reading P+'s stages...", Theme.Body, Theme.Muted);
        readonly PictureBox _pic = new PictureBox { Size = new Size(400, 225), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Visible = false };
        readonly Button _use = Theme.Primary("Use this version");
        readonly Dictionary<string, (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture)> _checked = new Dictionary<string, (string, StagePlan, List<StagePlan>, byte[])>();
        Dictionary<string, (string TrackList, string Module)> _info;
        Slot _slot; Version _picked; int _asking;

        public StageVersionsForm(Installer installer, Library lib)
        {
            _installer = installer; _lib = lib;
            Text = "Choose stage versions";
            BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(1060, 760); MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ShowInTaskbar = false;

            var title = Theme.Label("Which version do you get when you pick a stage?", Theme.Title); title.Location = new Point(24, 14);
            var hint = new Label
            {
                Text = "Every P+ stage has a normal version (just pick its icon) and others you get by holding L, R or Z, or from its list. Pick a stage, then pick the version you want as its normal one.",
                Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(26, 50), Size = new Size(1000, 40), BackColor = Color.Transparent,
            };
            _stages.Location = new Point(24, 100); _stages.Size = new Size(240, 560);
            _stages.BackColor = Theme.Panel; _stages.ForeColor = Theme.Text;
            _stages.SelectedIndexChanged += (s, e) => ShowSlot(_stages.SelectedItem as Slot);
            _choices.Location = new Point(284, 100); _choices.Size = new Size(740, 250);
            _status.AutoSize = false; _status.Location = new Point(284, 356); _status.Size = new Size(740, 64);
            _pic.Location = new Point(284, 424);
            _use.Enabled = false;
            _use.Click += (s, e) => Use();
            var close = Theme.Secondary("Close");
            close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange(new Control[] { title, hint, _stages, _choices, _status, _pic, _use, close });
            void Lay()
            {
                hint.Width = ClientSize.Width - 50;
                _stages.Height = ClientSize.Height - 170;
                _choices.Width = _status.Width = ClientSize.Width - 308;
                close.Location = new Point(ClientSize.Width - close.Width - 24, ClientSize.Height - close.Height - 16);
                _use.Location = new Point(close.Left - _use.Width - 10, close.Top);
            }
            Resize += (s, e) => Lay();
            Lay();
            Shown += async (s, e) => await LoadStages();
        }

        static string Key(Slot s, Version v) => s.Id + "|" + v.Spot.Entry;
        static string ModKey(int slot) => $"version|{slot:X2}|";

        /// <summary>The mod of ours that sets this stage's version now (a swap, or another version's looks), if any.</summary>
        List<InstalledStage> Current(Slot s) => _lib.Stages.Where(x => x.PackageKey != null && x.PackageKey.StartsWith(ModKey(s.Id), StringComparison.OrdinalIgnoreCase)).ToList();

        async Task LoadStages()
        {
            List<Slot> slots;
            try
            {
                slots = await Task.Run(() =>
                {
                    using (var card = _installer.OpenCard())
                    {
                        _info = Songs.StageInfo(card);
                        var select = _installer.OriginalSelect(card);
                        var all = select.All.Where(x => x.Page > 0).ToList();
                        var result = new List<Slot>();
                        foreach (var g in all.GroupBy(x => x.Slot).OrderBy(g => g.First().Page).ThenBy(g => g.First().Row).ThenBy(g => g.First().Column))
                        {
                            var versions = g.GroupBy(x => x.Entry).OrderBy(e => e.Key).Select(e => new Version
                            {
                                Spot = e.First(), Files = e.Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
                            }).ToList();
                            var normal = versions.FirstOrDefault(v => v.Spot.Button == 0);
                            if (normal == null) continue;
                            normal.Name = "P+'s normal one";
                            var slot = new Slot { Id = g.Key, Name = g.First().SlotName, Normal = normal };
                            foreach (var v in versions.Where(v => v != normal && !v.Files.SequenceEqual(normal.Files, StringComparer.OrdinalIgnoreCase)))
                            {
                                var d = Stages.Describe(v.Files[0]);
                                string how = v.Spot.HowToPick;
                                v.Label = v.Spot.ListEntry ?? (d.Version == "" ? d.StageName : System.Text.RegularExpressions.Regex.Replace(d.Version, @"^(L|R|Z|ML|MR)(: | version)", ""));
                                v.Name = (how.StartsWith("its ") ? how.Substring(4, 1).ToUpperInvariant() + how.Substring(5) : char.ToUpperInvariant(how[0]) + how.Substring(1))
                                         + (how.Contains("\"") ? "" : ": " + v.Label);
                                slot.Others.Add(v);
                            }
                            if (slot.Others.Count > 0) result.Add(slot);
                        }
                        return result;
                    }
                });
            }
            catch (Exception e) { _status.Text = "Couldn't read P+'s stages: " + Friendly.Explain(e); _status.ForeColor = Theme.Bad; return; }
            if (IsDisposed) return;
            foreach (var s in slots) _stages.Items.Add(s);
            _status.Text = slots.Count == 0 ? "No stage on your SD card has other versions." : "Pick a stage on the left.";
            if (slots.Count > 0) _stages.SelectedIndex = 0;
        }

        void ShowSlot(Slot s)
        {
            _slot = s; _picked = null; _use.Enabled = false; _pic.Visible = false;
            _choices.Controls.Clear();
            if (s == null) return;
            var now = Current(s).Where(x => x.Enabled).ToList();
            string nowParam = now.Select(x => x.PackageKey.Substring(ModKey(s.Id).Length)).FirstOrDefault();
            foreach (var v in new[] { s.Normal }.Concat(s.Others))
            {
                bool isNow = v == s.Normal ? nowParam == null : string.Equals(nowParam, v.Spot.Param + "#" + v.Spot.Entry, StringComparison.OrdinalIgnoreCase);
                var rb = new RadioButton { Text = v.Name + (isNow ? "   (what you get now)" : ""), AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, Tag = v, Checked = isNow, Margin = new Padding(0, 2, 0, 2) };
                var version = v;
                rb.CheckedChanged += async (o, e) => { if (rb.Checked) await Pick(version); };
                _choices.Controls.Add(rb);
            }
            _status.Text = now.Count > 0 ? $"Right now, just picking {s.Name} gives {s.Others.FirstOrDefault(v => string.Equals(nowParam, v.Spot.Param + "#" + v.Spot.Entry, StringComparison.OrdinalIgnoreCase))?.Name ?? "another version"} (" + (now[0].IsSwap ? "swapped" : "its looks") + "). Pick another to change it."
                : $"Right now, just picking {s.Name} gives P+'s normal one. Pick another version to see what happens.";
            _status.ForeColor = Theme.Muted;
        }

        async Task Pick(Version v)
        {
            var s = _slot; _picked = v; int me = ++_asking;
            _use.Enabled = false; _pic.Visible = false;
            if (v == s.Normal)
            {
                bool any = Current(s).Any(x => x.Enabled);
                _status.Text = any ? $"Just picking {s.Name} will give P+'s normal one again." : $"That's what you get now.";
                _status.ForeColor = Theme.Muted; _use.Enabled = any;
                return;
            }
            string key = Key(s, v);
            if (!_checked.TryGetValue(key, out var r))
            {
                _status.Text = $"Comparing {v.Name} with the normal {s.Name}..."; _status.ForeColor = Theme.Muted;
                try { r = await Task.Run(() => Check(s, v)); }
                catch (Exception e) { r = (Friendly.Explain(e), null, null, null); }
                _checked[key] = r;
                if (IsDisposed || me != _asking) return;
            }
            if (r.Swap != null)
            {
                _status.Text = $"{v.Label} plays exactly like the normal {s.Name}, so they're swapped for real: just picking {s.Name} gives {v.Label}, and {v.Spot.HowToPick} gives the normal one. Only what you see changes, so it's safe online.";
                _status.ForeColor = Theme.Good;
            }
            else if (r.Looks != null)
            {
                _status.Text = $"{v.Name} plays differently from the normal {s.Name}, so a real swap would make online games go out of sync. " +
                               $"Instead, its scenery goes on the normal {s.Name}" + (s.Normal.Files.Count > 1 ? $" (all {s.Normal.Files.Count} of its versions)" : "") +
                               $", which keeps P+'s gameplay, so it's safe online. Try it offline first to check the scenery sits right.";
                _status.ForeColor = Theme.Warn;
            }
            else
            {
                _status.Text = $"{v.Name} can't be used as the normal {s.Name}: {r.Why}.";
                _status.ForeColor = Theme.Bad;
            }
            _use.Enabled = r.Swap != null || r.Looks != null;
            if (r.Picture != null)
            {
                Bitmap shot = null;
                try { var bytes = r.Picture; shot = await Task.Run(() => ModelPreview.RenderStage(bytes, 480, 270)); } catch (Exception e) { Program.Trace("preview: " + e.Message); }
                if (IsDisposed || me != _asking) return;
                var old = _pic.Image; _pic.Image = shot; old?.Dispose();
                _pic.Visible = shot != null;
            }
        }

        /// <summary>Works out what using a version would do: a swap (plays alike), its looks on the normal one, or why neither.</summary>
        (string Why, StagePlan Swap, List<StagePlan> Looks, byte[] Picture) Check(Slot s, Version v)
        {
            using (var card = _installer.OpenCard())
            {
                byte[] Read(string f) => _lib.HasOriginalFile(f) ? _lib.OriginalFile(f) : card.ReadFile(f);
                string pkgKey = ModKey(s.Id) + v.Spot.Param + "#" + v.Spot.Entry;
                var differ = _installer.PlayAlike(card, s.Normal.Files, v.Files, _info);
                if (differ == null)
                {
                    var swap = _installer.PlanSwap(card, s.Normal.Spot, v.Spot, v.Name);
                    swap.PackageKey = pkgKey; swap.Name = $"{s.Name}: {v.Label} as the normal one";
                    return swap.CanInstall ? (null, swap, null, Read(v.Files[0])) : (string.Join(" ", swap.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                }
                // its looks on each file of the normal version, from P+'s own file for that version
                string dir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "version-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Path.GetFileName(v.Files[0]));
                File.WriteAllBytes(file, Read(v.Files[0]));
                var pkg = new SkinPackage { Folder = dir, Name = $"{s.Name} with {v.Label}'s looks" };
                pkg.StageFiles.Add(file);
                var plans = new List<StagePlan>();
                string group = Guid.NewGuid().ToString("N");
                foreach (var f in s.Normal.Files)
                {
                    var target = Stages.Describe(f);
                    var plan = _installer.PlanLooks(card, pkg, file, target);
                    if (!plan.CanInstall) return (string.Join(" ", plan.Checks.Where(c => !c.Passed).Select(c => c.Detail ?? c.Title)), null, null, null);
                    plan.PackageKey = pkgKey; plan.Group = s.Normal.Files.Count > 1 ? group : null; plan.Name = pkg.Name;
                    plans.Add(plan);
                }
                return (Short(differ), null, plans, plans[0].Bytes);
            }
        }

        static string Short(string why) { int i = why.IndexOf(':'); return i > 0 && why.StartsWith("it plays differently") ? why.Substring(i + 1).Trim().TrimEnd('.') : why; }

        void Use()
        {
            var s = _slot; var v = _picked;
            if (s == null || v == null) return;
            var change = new Installer.Change();
            change.StageRemove.AddRange(Current(s));   // the stage's version set by us before: replaced by this choice
            if (v == s.Normal) Done = $"Done. Just picking {s.Name} gives P+'s normal one again.";
            else
            {
                if (!_checked.TryGetValue(Key(s, v), out var r) || (r.Swap == null && r.Looks == null)) return;
                if (r.Swap != null) { change.StageInstall.Add(r.Swap); Done = $"Done. Just picking {s.Name} now gives {v.Label}; {v.Spot.HowToPick} gives the normal one. They play exactly alike, so it's safe online."; }
                else { change.StageInstall.AddRange(r.Looks); Done = $"Done. The normal {s.Name} now has {v.Label}'s scenery, and plays like P+'s normal {s.Name}, so it's safe online. Try it offline first to check it looks right."; }
            }
            Change = change;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
