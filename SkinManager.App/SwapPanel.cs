// Model Swapper (beta): put a model from anywhere (a .dae or .obj) on a P+ character's skeleton.
// The app sizes it, simplifies it, rigs it to P+'s own bones and shows it next to P+'s costume, standing or in a
// pose from the character's own animations. "Make it a costume" hands the result to the normal install steps
// (slot, pictures, the same safety checks as any skin).
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
    class SwapPanel : GlassPanel
    {
        readonly Func<Installer> _installer;
        readonly Action<string> _install;   // opens a folder with the usual "add a skin" steps

        readonly Button _choose = Theme.Primary("Choose a model...");
        readonly Label _file = Theme.Label("No model chosen yet. It can be a .glb, .gltf, .obj or .dae file (Tripo3D: download as GLB).", Theme.Body, Theme.Muted);
        readonly ComboBox _character = Combo(200), _slot = Combo(200), _detail = Combo(230), _pose = Combo(180);
        readonly CheckBox _standUp = new CheckBox { Text = "It's lying down: stand it up", AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body };
        readonly Button _left = Theme.Secondary("⟲ Turn"), _right = Theme.Secondary("Turn ⟳");
        readonly TrackBar _size = new TrackBar { Minimum = 80, Maximum = 120, Value = 100, TickFrequency = 5, Width = 150, AutoSize = false, Height = 30, TickStyle = TickStyle.None, BackColor = Theme.Back };
        readonly Label _sizeText = Theme.Label("Size: 100%", Theme.Body, Theme.Muted);
        readonly PictureBox _theirs = Pic(), _mine = Pic();
        readonly Label _theirsText = Theme.Label("P+'s costume", Theme.Small, Theme.Muted), _mineText = Theme.Label("Yours", Theme.Small, Theme.Muted);
        readonly Label _status = Theme.Label("", Theme.Small, Theme.Muted);
        readonly Button _make = Theme.Primary("Make it a costume...");
        readonly Button _pictures = Theme.Secondary("Save P+ Model Pictures...");
        readonly Button _export3d = Theme.Secondary("Save P+ Model as 3D...");
        readonly Button _lineUp = Theme.Secondary("Line up the joints...");
        List<SwapJoint> _joints;
        SwapMesh _placed;
        Dictionary<string, BrawlLib.Internal.Vector3> _marks = new Dictionary<string, BrawlLib.Internal.Vector3>(StringComparer.Ordinal);
        string _marksKey;
        List<PaintDab> _dabs = new List<PaintDab>();
        Dictionary<string, BrawlLib.Internal.Vector3> _auto = new Dictionary<string, BrawlLib.Internal.Vector3>();

        string _path;
        SwapMesh _mesh;
        byte[] _target, _motion;
        Character _targetFor; CostumeSlot _targetSlot;
        ModelSwap.Result _built;
        int _turn, _gen;
        readonly Timer _later = new Timer { Interval = 500 };

        internal static readonly (string Name, string Anim, int Frame)[] Poses =
        {
            ("Standing", null, 0), ("Waiting", "Wait1", 30), ("Running", "Run", 8), ("Side smash", "AttackS4S", 20), ("Up smash", "AttackHi4", 14), ("Jumping", "JumpF", 10),
        };
        static readonly (string Name, int Budget)[] Details =
        {
            ("Light (about 3,000 triangles)", 3000), ("Medium (about 6,000)", 6000), ("Detailed (about 10,000)", 10000),
        };

        static ComboBox Combo(int w) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text };
        static PictureBox Pic() => new PictureBox { Size = new Size(256, 320), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20, 22, 28) };

        public SwapPanel(Func<Installer> installer, Action<string> install)
        {
            _installer = installer; _install = install;
            Dock = DockStyle.Fill; Padding = new Padding(28, 18, 16, 8); AutoScroll = true;
            var title = Theme.Label("Model Swapper (beta)", Theme.Title); title.Location = new Point(28, 14);
            var wip = Theme.Label("⚠  Work in progress: results depend a lot on the model, and some moves can still stretch it. Check it in a few poses before playing online.", Theme.Small, Theme.Warn);
            wip.AutoSize = false; wip.Size = new Size(760, 18);
            title.SizeChanged += (s, e) => wip.Location = new Point(title.Right + 14, 22);
            wip.Location = new Point(title.Right + 14, 22);
            var hint = Theme.Label("Put a model from another game or a 3D program on a P+ character. It keeps P+'s own skeleton and moves with P+'s animations,\r\nso hits stay the same for both players. Only use models you're allowed to use, and credit whoever made them.", Theme.Small, Theme.Muted);
            hint.Location = new Point(30, 50); hint.AutoSize = false; hint.Size = new Size(900, 40);

            _choose.Location = new Point(28, 96);
            _file.Location = new Point(200, 104); _file.AutoSize = false; _file.Size = new Size(760, 20);

            var l1 = Theme.Label("On", Theme.Body, Theme.Muted); l1.Location = new Point(28, 146);
            _character.Location = new Point(60, 142);
            var l2 = Theme.Label("using the skeleton of", Theme.Body, Theme.Muted); l2.Location = new Point(270, 146);
            _slot.Location = new Point(410, 142);
            _pictures.Location = new Point(626, 138);
            _pictures.SizeChanged += (s, e) => _export3d.Location = new Point(_pictures.Right + 8, 138);
            _export3d.Location = new Point(_pictures.Right + 8, 138);
            foreach (var c in Characters.All.OrderBy(c => c.Name)) _character.Items.Add(c);

            _standUp.Location = new Point(28, 188); _standUp.BackColor = Color.Transparent;
            _left.Location = new Point(250, 182); _right.Location = new Point(340, 182);
            _sizeText.Location = new Point(446, 188); _sizeText.AutoSize = false; _sizeText.Width = 84; _size.Location = new Point(530, 184);
            var ld = Theme.Label("Detail", Theme.Body, Theme.Muted); ld.Location = new Point(696, 188); Controls.Add(ld);
            _detail.Location = new Point(746, 184); _detail.Width = 214;
            foreach (var d in Details) _detail.Items.Add(d.Name);
            _detail.SelectedIndex = 1;

            var lp = Theme.Label("Show it", Theme.Body, Theme.Muted); lp.Location = new Point(28, 236);
            _pose.Location = new Point(90, 232);
            foreach (var p in Poses) _pose.Items.Add(p.Name);
            _pose.SelectedIndex = 0;
            _lineUp.Location = new Point(286, 228); _lineUp.Width = 190; _lineUp.Enabled = false;

            _theirs.Location = new Point(28, 272); _mine.Location = new Point(300, 272);
            _theirsText.Location = new Point(28, 598); _mineText.Location = new Point(300, 598);
            _theirsText.Font = _mineText.Font = Theme.BodyBold;
            _status.Location = new Point(580, 280); _status.AutoSize = false; _status.Size = new Size(380, 240);
            _make.Location = new Point(580, 540); _make.Enabled = false;

            Controls.AddRange(new Control[] { title, wip, hint, _choose, _export3d, _file, l1, _character, l2, _slot, _pictures, _standUp, _left, _right, _sizeText, _size, _detail, lp, _pose, _lineUp, _theirs, _mine, _theirsText, _mineText, _status, _make });

            _choose.Click += (s, e) => Choose();
            _character.SelectedIndexChanged += (s, e) => FillSlots();
            _slot.SelectedIndexChanged += (s, e) => { _target = null; Later(); };
            _standUp.CheckedChanged += (s, e) => Later();
            _left.Click += (s, e) => { _turn = (_turn + 3) % 4; Later(); };
            _right.Click += (s, e) => { _turn = (_turn + 1) % 4; Later(); };
            _size.ValueChanged += (s, e) => { _sizeText.Text = $"Size: {_size.Value}%"; Later(); };
            _detail.SelectedIndexChanged += (s, e) => Later();
            _pose.SelectedIndexChanged += (s, e) => Later();
            _later.Tick += (s, e) => { _later.Stop(); Refresh2(); };
            _make.Click += (s, e) => Make();
            _pictures.Click += (s, e) => SavePictures();
            _export3d.Click += (s, e) => SaveModel3D();
            _lineUp.Click += (s, e) => LineUp();
            _character.SelectedIndex = 0;
            string testChar = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_CHARACTER");   // UI tests
            if (testChar != null) _character.SelectedItem = _character.Items.Cast<Character>().FirstOrDefault(x => x.Name == testChar);
            VisibleChanged += (s, e) => { if (Visible && _slot.Items.Count == 0) FillSlots(); };
        }

        void Later() { _later.Stop(); _later.Start(); }

        /// <summary>Runs work on its own single-threaded (STA) thread: BrawlLib's model importer is a window class.</summary>
        internal static Task<T> Sta<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>();
            var t = new System.Threading.Thread(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } }) { IsBackground = true };
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            return done.Task;
        }

        void FillSlots()
        {
            _slot.Items.Clear();
            var c = _character.SelectedItem as Character;
            if (c == null) return;
            try
            {
                using (var card = _installer()?.OpenCard())
                    if (card != null) foreach (var s in PPlusCard.ReadSlots(card, c)) _slot.Items.Add(new SlotItem { Slot = s, Text = $"costume {s.Index + 1} (Fit{c.FilePrefix}{s.FileNumber:00}{(s.OnCard ? "" : ", from the disc")})" });
            }
            catch { }
            // start on a costume that's on the SD card (the others are read from the Brawl disc)
            int first = _slot.Items.Cast<SlotItem>().ToList().FindIndex(i => i.Slot.OnCard);
            if (_slot.Items.Count > 0) _slot.SelectedIndex = Math.Max(0, first);
            _motion = null;
        }

        class SlotItem { public CostumeSlot Slot; public string Text; public override string ToString() => Text; }

        /// <summary>The chosen P+ costume as a 3D file (GLB): its exact shape, pictures and how they wrap around it, for
        /// re-texturing tools (paint a new look on P+'s own shape, then bring it back here: it fits perfectly).</summary>
        async void SaveModel3D()
        {
            var c = _character.SelectedItem as Character;
            var slotItem = _slot.SelectedItem as SlotItem;
            var installer = _installer();
            if (c == null || slotItem == null || installer == null) return;
            string name = c.Name + " " + slotItem.Text;
            foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '-');
            string path;
            using (var d = new SaveFileDialog { Title = "Save P+'s model as a 3D file", Filter = "3D model (GLB)|*.glb", FileName = name + ".glb", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = d.FileName;
            }
            _export3d.Enabled = false;
            string before = _status.Text;
            _status.Text = "Saving " + c.Name + "'s model...";
            try
            {
                int tris = await Task.Run(() =>
                {
                    byte[] costume, fighter = null;
                    using (var card = installer.OpenCard())
                    {
                        costume = installer.OriginalCostume(card, c, slotItem.Slot, out _);
                        string fit = c.FighterFolderPath + "/Fit" + c.FilePrefix + ".pac";
                        try { if (card.Exists(fit)) fighter = card.ReadFile(fit); } catch { }
                    }
                    if (costume == null) throw new InvalidOperationException("P+'s costume for that slot couldn't be read.");
                    // what the fighter normally hides (items in hand, other faces): from its moveset
                    IEnumerable<int> hidden = null;
                    if (fighter != null)
                        try
                        {
                            lock (BrawlGate.Lock)
                            using (var tmp = new TempFile(".pac", fighter))
                            using (var root = BrawlLib.SSBB.ResourceNodes.NodeFactory.FromFile(null, tmp.Path))
                            {
                                var src = root.Children[0].WorkingUncompressed;
                                var bytes = new byte[src.Length];
                                System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                                hidden = new Moveset(bytes).DefaultHidden().ToList();
                            }
                        }
                        catch { }
                    var glb = ModelExport.ToGlb(costume, out int t, hidden);
                    File.WriteAllBytes(path, glb);
                    return t;
                });
                _status.Text = before;
                MessageBox.Show(FindForm(),
                    $"Saved: {Path.GetFileName(path)} ({tris:N0} triangles, with its pictures).\r\n\r\n" +
                    "This is P+'s exact model, so anything you make from it keeps P+'s proportions. To give it a new look:\r\n\r\n" +
                    "1. Open a re-texturing tool (for example Meshy's \"AI Texturing\") and upload this file.\r\n" +
                    "2. Describe the look you want, or give it a picture to copy.\r\n" +
                    "3. Download the result as GLB.\r\n" +
                    $"4. Here in the Model Swapper: Choose a model, pick that file, and keep {c.Name} and the same costume selected.\r\n\r\n" +
                    "Because the shape is P+'s own, it lines up with the skeleton by itself: no dots to place.",
                    "Save P+ Model as 3D", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); } catch { }
            }
            catch (Exception e)
            {
                Program.Log("save model 3d", e);
                _status.Text = before;
                MessageBox.Show(FindForm(), "The model couldn't be saved: " + e.Message, "Save P+ Model as 3D", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _export3d.Enabled = true; }
        }

        /// <summary>The chosen P+ costume from the front, side and back, true to size, to make a matching model from
        /// with an AI 3D tool (Tripo3D and the like) or by hand.</summary>
        async void SavePictures()
        {
            var c = _character.SelectedItem as Character;
            var slot = (_slot.SelectedItem as SlotItem)?.Slot;
            var installer = _installer();
            if (c == null || slot == null || installer == null) return;
            string folder;
            using (var d = new FolderBrowserDialog { Description = "Where should the pictures go? (A new folder is made inside the one you pick.)", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                folder = d.SelectedPath;
            }
            string name = c.Name + " " + (_slot.SelectedItem as SlotItem).Text;
            foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '-');
            folder = Path.Combine(folder, name + " - P+ model pictures");
            _pictures.Enabled = false;
            string before = _status.Text;
            _status.Text = "Drawing " + c.Name + " from the front, side and back...";
            try
            {
                var r = await Task.Run(() =>
                {
                    byte[] costume;
                    using (var card = installer.OpenCard()) costume = installer.OriginalCostume(card, c, slot, out _);
                    if (costume == null) throw new InvalidOperationException("P+'s costume for that slot couldn't be read.");
                    return ModelPreview.RenderViews(costume, 1024);
                });
                Directory.CreateDirectory(folder);
                string[] names = { "front", "side", "back" };
                for (int i = 0; i < r.Views.Length; i++) using (r.Views[i]) r.Views[i].Save(Path.Combine(folder, name + " - " + names[i] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(Path.Combine(folder, "How to use these.txt"),
                    $"{name}: P+'s costume in its default pose (arms out), drawn from the front, the side and the back.\r\n" +
                    "All three are the same scale and have no perspective, so its proportions are true: use them as the reference for a new model.\r\n\r\n" +
                    "In a 3D model maker (like Tripo3D): use the front picture, or front + side + back if it takes several views. Ask for the same pose (T-pose, arms out).\r\n" +
                    "Download the result as GLB (or OBJ), then open it in P+ Modding Tool > Model Swapper and pick this same character and slot.\r\n\r\n" +
                    $"Height in P+'s units: {r.Height:0.0} (the swapper sizes the new model to this by itself).\r\n");
                _status.Text = "Saved. The pictures are in the folder that just opened.";
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
            }
            catch (Exception e)
            {
                Program.Log("saving P+ model pictures", e);
                _status.Text = before;
                MessageBox.Show(FindForm(), "The pictures couldn't be saved: " + e.Message, "Save P+ Model Pictures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _pictures.Enabled = true; }
        }

        async void Choose()
        {
            string test = Environment.GetEnvironmentVariable("SKINMANAGER_TEST_MODEL");   // UI tests (no file dialog)
            if (test != null) _path = test;
            else
            using (var dlg = new OpenFileDialog { Title = "Choose a model", Filter = "3D models (*.glb; *.gltf; *.obj; *.dae)|*.glb;*.gltf;*.obj;*.dae|All files|*.*" })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                _path = dlg.FileName;
            }
            _file.Text = "Reading " + Path.GetFileName(_path) + "...";
            _mesh = null; _built = null; _make.Enabled = false;
            string path = _path;
            try
            {
                var mesh = await Sta(() => ModelSwap.Load(path));
                if (path != _path) return;
                _mesh = mesh;
                int pics = mesh.Parts.Count(p => p.TexturePath != null);
                _file.Text = $"{Path.GetFileName(path)}: {mesh.Triangles:N0} triangles, {mesh.Parts.Count} part(s), {pics} picture(s) found.";
                _file.ForeColor = Theme.Text;
                // a model much deeper than it is tall was most likely made lying down (Z up): stand it up to start with
                var box = mesh.Box();
                _turn = 0;
                _standUp.Checked = box.Max._z - box.Min._z > 1.5f * (box.Max._y - box.Min._y);
                Refresh2();
            }
            catch (Exception e)
            {
                _file.Text = "That model couldn't be read: " + e.Message;
                _file.ForeColor = Theme.Bad;
            }
        }

        /// <summary>Rebuilds the costume with the current choices and shows both, in the chosen pose.</summary>
        async void Refresh2()
        {
            var c = _character.SelectedItem as Character;
            var slot = (_slot.SelectedItem as SlotItem)?.Slot;
            if (c == null || slot == null) return;
            int gen = ++_gen;
            var mesh = _mesh;
            var fit = new SwapFit { StandUp = _standUp.Checked, Turn = _turn, Size = _size.Value / 100f };
            int budget = Details[Math.Max(0, _detail.SelectedIndex)].Budget;
            var pose = Poses[Math.Max(0, _pose.SelectedIndex)];
            _status.Text = mesh == null ? "Choose a model to see it here." : "Fitting your model to P+'s skeleton...";
            _status.ForeColor = Theme.Muted;
            _make.Enabled = false; _lineUp.Enabled = false;
            try
            {
                var installer = _installer();
                if (installer == null) { _status.Text = "Brawl Online isn't set up on this PC yet."; return; }
                if (_target == null || _targetFor != c || _targetSlot != slot)
                {
                    var r = await Task.Run(() =>
                    {
                        using (var card = installer.OpenCard())
                        {
                            var t = installer.OriginalCostume(card, c, slot, out _);
                            byte[] m = null;
                            foreach (var name in new[] { $"Fit{c.FilePrefix}MotionEtc.pac", $"Fit{c.FilePrefix}Motion.pac" })
                            {
                                string p = $"{c.FighterFolderPath}/{name}";
                                if (card.Exists(p)) { m = card.ReadFile(p); break; }
                            }
                            return (t, m);
                        }
                    });
                    if (gen != _gen) return;
                    if (r.t == null) { _status.Text = "P+'s costume for that slot couldn't be read."; _status.ForeColor = Theme.Bad; return; }
                    _target = r.t; _motion = r.m; _targetFor = c; _targetSlot = slot; _joints = null;
                }
                byte[] target = _target, motion = _motion;
                string key = _path + "|" + c.Name + "|" + slot.FileNumber;
                if (key != _marksKey) { _marksKey = key; _marks = LoadMarks(key); _dabs = LoadDabs(key); }
                var marks = new Dictionary<string, BrawlLib.Internal.Vector3>(_marks);
                var dabs = new List<PaintDab>(_dabs);
                var known = _joints;
                var theirs = await Task.Run(() => pose.Anim != null && motion != null ? ModelPreview.RenderPosed(target, motion, pose.Anim, pose.Frame) : ModelPreview.Render(target));
                if (gen != _gen) return;
                Swap(_theirs, theirs);
                if (mesh == null) return;
                string modelName = Path.GetFileNameWithoutExtension(_path);
                var made = await Sta(() =>
                {
                    var joints = known;
                    if (joints == null) try { joints = ModelSwap.Joints(target); } catch (Exception e) { Program.Trace("no joints: " + e.Message); }
                    // made from P+'s own model ("Save P+ Model as 3D", re-textured): it fits exactly, each point keeps P+'s own rigging
                    SwapMesh own = null;
                    try { own = ModelSwap.AsOwnShape(mesh, target); } catch (Exception e) { Program.Trace("own shape: " + e.Message); }
                    if (own != null)
                        return (Built: ModelSwap.Build(own, target, modelName, null, ModelSwap.OwnWeights(target, joints != null ? ModelSwap.SightWeights(own, joints) : null)), Placed: own, Joints: joints, Auto: new Dictionary<string, BrawlLib.Internal.Vector3>(), Own: true);
                    var box = ModelSwap.TargetBox(target);
                    var placed = ModelSwap.Simplify(ModelSwap.Fit(mesh, box, fit), budget);
                    // nothing placed by hand yet: the dots are placed by measuring the model
                    var auto = joints != null ? ModelSwap.AutoMarks(placed, joints) : new Dictionary<string, BrawlLib.Internal.Vector3>();
                    if (marks.Count == 0) marks = auto;
                    var lined = ModelSwap.LineUp(placed, joints, marks);
                    var keep = ModelSwap.KeepTest(placed, lined, dabs);
                    return (Built: ModelSwap.Build(lined, target, modelName, keep, ModelSwap.SightWeights(lined, joints, keep)), Placed: placed, Joints: joints, Auto: auto, Own: false);
                });
                if (gen != _gen) return;
                var built = made.Built;
                _placed = made.Placed; _joints = made.Joints; _auto = made.Auto;
                var mine = await Task.Run(() => pose.Anim != null && motion != null ? ModelPreview.RenderPosed(built.Costume, motion, pose.Anim, pose.Frame) : ModelPreview.Render(built.Costume));
                if (gen != _gen) return;
                Swap(_mine, mine);
                _built = built;
                _status.Text = $"{built.Triangles:N0} triangles, {built.Pictures} picture(s), {built.Costume.Length / 1024:N0} KB.\r\n" +
                    $"{built.Mixes} bone blends (P+'s own costume uses {built.MixBudget}; more would crash the game).\r\n" +
                    (built.Notes.Count > 0 ? string.Join("\r\n", built.Notes) + "\r\n" : "") +
                    (made.Own ? "\r\nThis is P+'s own shape (re-textured): it fits the skeleton exactly and moves just like P+'s costume. No lining up needed.\r\n\r\n"
                              : "\r\nLooks wrong? Turn it, stand it up or change its size above, then look at it in a few poses.\r\n\r\n") +
                    "When it looks right: \"Make it a costume\". It then goes through the same steps and safety checks as any skin.";
                _status.ForeColor = Theme.Text;
                _make.Enabled = true;
                _lineUp.Enabled = _joints != null && !made.Own;
            }
            catch (Exception e)
            {
                if (gen != _gen) return;
                Program.Trace("swap failed: " + e);
                _status.Text = "That didn't work: " + e.Message;
                _status.ForeColor = Theme.Bad;
            }
        }

        /// <summary>Opens "Line up the joints" for the model as it's placed now; the dots moved there are kept for this model.</summary>
        void LineUp()
        {
            if (_placed == null || _joints == null || _target == null) return;
            using (var f = new JointsForm(_placed, _joints, _marks.Count > 0 ? _marks : _auto, _auto, _dabs, _target, _motion, Path.GetFileNameWithoutExtension(_path)))
            {
                if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
                _marks = f.Marks; _dabs = f.Dabs;
            }
            try
            {
                string file = MarksFile(_marksKey);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                if (_marks.Count == 0 && _dabs.Count == 0) File.Delete(file); else File.WriteAllText(file, ModelSwap.SaveMarks(_marks, _dabs));
            }
            catch (Exception e) { Program.Log("saving the joint dots", e); }
            Refresh2();
        }

        static string MarksFile(string key)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                string id = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
                return Path.Combine(Program.DataFolder, "Swaps", "joints", id + ".txt");
            }
        }

        static List<PaintDab> LoadDabs(string key)
        {
            try { string f = MarksFile(key); if (File.Exists(f)) return ModelSwap.ReadDabs(File.ReadAllText(f)); } catch { }
            return new List<PaintDab>();
        }

        static Dictionary<string, BrawlLib.Internal.Vector3> LoadMarks(string key)
        {
            try { string f = MarksFile(key); if (File.Exists(f)) return ModelSwap.ReadMarks(File.ReadAllText(f)); } catch { }
            return new Dictionary<string, BrawlLib.Internal.Vector3>(StringComparer.Ordinal);
        }

        static void Swap(PictureBox box, Image img) { var old = box.Image; box.Image = img; old?.Dispose(); }

        void Make()
        {
            var c = _targetFor; var slot = _targetSlot;
            if (_built == null || c == null || slot == null) return;
            if (_built.Mixes > _built.MixBudget)
            {
                MessageBox.Show(FindForm(), $"This model needs {_built.Mixes} bone blends and P+'s own costume has {_built.MixBudget}. The game can't load that many and would crash, so it wasn't made into a costume.", "Model Swapper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string name = Path.GetFileNameWithoutExtension(_path);
            string safe = string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' ? ch : '_')).Trim();
            if (safe.Length == 0) safe = "Model";
            string folder = Path.Combine(Program.DataFolder, "Swaps", safe + " on " + c.Name);
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.GetFiles(folder, "*.pac")) try { File.Delete(f); } catch { }
            File.WriteAllBytes(Path.Combine(folder, $"Fit{c.FilePrefix}{slot.FileNumber:00}.pac"), _built.Costume);
            _install(folder);
        }
    }
}
