// What the Bridge does, without its window: a costume to Blender (a folder with the rigged GLB, a copy of the costume
// and what the way back needs), and Blender's GLB back to a costume file the Mod Manager can add.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BrawlLib.SSBB.ResourceNodes;
using SkinManager.Core;

namespace PPlusBlenderBridge
{
    [DataContract]
    sealed class KitInfo
    {
        public const string FileName = "pplus-costume.json", CostumeFile = "costume.pac";
        [DataMember] public string Character;   // its name ("Peach")
        [DataMember] public int FileNumber;     // NN in Fit<Name>NN.pac
        [DataMember] public int Slot;           // costume number on the select screen, from 0
        [DataMember] public string Label;       // what was sent ("Peach costume 11")
        [DataMember] public List<int> Hidden = new List<int>();   // bones of what the fighter normally hides

        public void Save(string folder)
        {
            using (var s = File.Create(Path.Combine(folder, FileName))) new DataContractJsonSerializer(typeof(KitInfo)).WriteObject(s, this);
        }
        public static KitInfo Load(string folder)
        {
            string f = Path.Combine(folder, FileName);
            if (!File.Exists(f)) return null;
            using (var s = File.OpenRead(f)) return (KitInfo)new DataContractJsonSerializer(typeof(KitInfo)).ReadObject(s);
        }
    }

    static class BridgeWork
    {
        public static string KitFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "P+ Blender Kit");

        public static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-'); return s.Trim(); }

        /// <summary>Bones of what the fighter normally hides (items in hand, other faces), from its moveset file.</summary>
        public static List<int> HiddenBones(byte[] fighter)
        {
            if (fighter == null) return new List<int>();
            try
            {
                lock (BrawlGate.Lock)
                using (var tmp = new TempFile(".pac", fighter))
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var src = root.Children[0].WorkingUncompressed;
                    var bytes = new byte[src.Length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
                    return new Moveset(bytes).DefaultHidden().ToList();
                }
            }
            catch { return new List<int>(); }
        }

        /// <summary>Makes the folder for Blender; returns the GLB's path and what it holds.</summary>
        public static (string Glb, string About) Send(byte[] costume, byte[] fighter, Character c, CostumeSlot slot, string label)
        {
            string folder = Path.Combine(KitFolder, Safe(label));
            Directory.CreateDirectory(folder);
            var info = new KitInfo { Character = c.Name, FileNumber = slot?.FileNumber ?? 0, Slot = slot?.Index ?? 0, Label = label, Hidden = HiddenBones(fighter) };
            string glb = Path.Combine(folder, Safe(label) + " - for Blender.glb");
            string about = RiggedExport.ToGlb(costume, info.Hidden, glb);
            File.WriteAllBytes(Path.Combine(folder, KitInfo.CostumeFile), costume);
            info.Save(folder);
            return (glb, about);
        }

        /// <summary>How big the costume may unpack to: no bigger than the costume it was made from, which is known to work
        /// in that slot. (Working out the room left next to the fighter's moves isn't exact: a Peach costume 9 KB under
        /// that estimate but 9 KB over the one it replaced crashed the game.)</summary>
        static long Budget(Character c, KitInfo info, byte[] original, Func<string, byte[]> cardFile)
        {
            return CostumeMemory.Unpacked(original);
        }

        /// <summary>Checks the model's lists (bones, shapes, materials, shaders...) all point inside the model, without
        /// reading them (a broken one can't be read safely). Null when fine, else what's wrong.</summary>
        /// <summary>Takes out the pictures no material of any model in the costume points at. Pictures that are frames of
        /// one that's used (PeachTexEyeLid.01 next to a used PeachTexEyeLid.00: the game swaps them in for blinking) stay.</summary>
        /// <summary>The picture names the fighter's other files point at (its own file and every Etc file: the parasol,
        /// Toad, effects...), or null when they can't be read (no SD card).</summary>
        static HashSet<string> PicturesUsedElsewhere(Character c, Func<string, byte[]> cardFile)
        {
            if (cardFile == null) return null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<string> { $"Fit{c.FilePrefix}.pac", $"Fit{c.FilePrefix}Etc.pac" };
            for (int i = 0; i < 100; i++) files.Add($"Fit{c.FilePrefix}Etc{i:00}.pac");
            foreach (var x in new[] { "AltR", "AltZ", "Dark", "Fake" }) files.Add($"Fit{c.FilePrefix}Etc{x}.pac");
            int read = 0;
            foreach (var f in files)
            {
                byte[] b;
                try { b = cardFile(c.FighterFolderPath + "/" + f); } catch { b = null; }
                if (b == null) continue;
                try
                {
                    lock (BrawlGate.Lock)
                    using (var tmp = new TempFile(".pac", b))
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                        foreach (var rf in root.FindChildrenByType(null, ResourceType.MDL0MaterialEntry).OfType<MDL0MaterialRefNode>())
                            if (!string.IsNullOrEmpty(rf.Name)) names.Add(rf.Name);
                    read++;
                }
                catch { return null; }   // a file that can't be read: better keep every picture
            }
            return read > 0 ? names : null;
        }

        static CostumeMemory.Shrunk PutBackPicturesUsedElsewhere(byte[] pac, byte[] original, HashSet<string> usedElsewhere)
        {
            var r = new CostumeMemory.Shrunk { Before = CostumeMemory.Unpacked(pac) };
            if (usedElsewhere == null || usedElsewhere.Count == 0) return r;
            lock (BrawlGate.Lock)
            using (var otmp = new TempFile(".pac", original))
            using (var tmp = new TempFile(".pac", pac))
            using (var outFile = new TempFile(".pac"))
            {
                using (var oroot = NodeFactory.FromFile(null, otmp.Path))
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var have = new HashSet<string>(root.FindChildrenByType(null, ResourceType.TEX0).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                    var missing = oroot.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Where(t => usedElsewhere.Contains(t.Name) && !have.Contains(t.Name)).ToList();
                    if (missing.Count == 0) return r;
                    foreach (var t in missing)
                    {
                        // into the archive entry (BRRES) of the same name it was in; made if it's gone
                        string brresName = t.Parent?.Parent?.Name;
                        var brres = root.Children.OfType<BRRESNode>().FirstOrDefault(b => b.Name == brresName) ?? root.Children.OfType<BRRESNode>().First();
                        using (var raw = new TempFile(".tex0"))
                        {
                            t.Export(raw.Path);
                            var n = brres.CreateResource<TEX0Node>(t.Name);
                            n.Replace(raw.Path);
                            n.Name = t.Name;
                        }
                        r.Pictures.Add(t.Name);
                    }
                    root.Rebuild(true);
                    root.Export(outFile.Path);
                }
                var bytes = File.ReadAllBytes(outFile.Path);
                if (CheckModel(bytes) != null) return new CostumeMemory.Shrunk { Before = r.Before };
                r.Bytes = bytes; r.After = CostumeMemory.Unpacked(bytes);
                return r;
            }
        }

        /// <summary>How many mixes of bones (weighted matrices) the costume's main model has.</summary>
        static int MixCount(byte[] pac)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", pac))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var m = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault();
                if (m == null) return 0;
                m.Populate();
                return m._influences?.Count ?? 0;
            }
        }

        static CostumeMemory.Shrunk DropUnusedPictures(byte[] pac, HashSet<string> usedElsewhere)
        {
            var r = new CostumeMemory.Shrunk { Before = CostumeMemory.Unpacked(pac) };
            // without the fighter's other files to check, nothing is taken out (its parasol, Toad... use the costume's pictures)
            if (usedElsewhere == null) return r;
            string Family(string n) { int dot = n.LastIndexOf('.'); return dot > 0 && n.Substring(dot + 1).All(char.IsDigit) ? n.Substring(0, dot) : n; }
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", pac))
            using (var outFile = new TempFile(".pac"))
            {
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                        foreach (var rf in m.FindChildrenByType(null, ResourceType.MDL0MaterialEntry).OfType<MDL0MaterialRefNode>())
                        {
                            if (!string.IsNullOrEmpty(rf.Name)) used.Add(rf.Name);
                            if (rf.TextureNode != null) used.Add(rf.TextureNode.Name);
                        }
                    if (used.Count == 0) return r;
                    // only from the costume's own picture group (the one its model uses): other groups hold pictures
                    // for models in other files, like Peach's parasol
                    var ownGroups = new HashSet<ResourceNode>(root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>()
                        .Where(t => used.Contains(t.Name)).Select(t => t.Parent?.Parent).Where(x => x != null));
                    used.UnionWith(usedElsewhere);
                    var families = new HashSet<string>(used.Select(Family), StringComparer.OrdinalIgnoreCase);
                    var gone = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>()
                        .Where(t => ownGroups.Contains(t.Parent?.Parent) && !used.Contains(t.Name) && !families.Contains(Family(t.Name)) && !t.SharesData).ToList();
                    // a picture whose pixels another one borrows (shared data) stays, and so does the one before it
                    gone = gone.Where(t => { var sib = t.Parent?.Children; int k = sib?.IndexOf(t) ?? -1; return !(k >= 0 && k + 1 < sib.Count && sib[k + 1] is TEX0Node nx && nx.SharesData); }).ToList();
                    if (gone.Count == 0) return r;
                    foreach (var t in gone) { r.Pictures.Add(t.Name); t.Remove(); }
                    root.Rebuild(true);
                    root.Export(outFile.Path);
                }
                var bytes = File.ReadAllBytes(outFile.Path);
                if (CheckModel(bytes) != null) return new CostumeMemory.Shrunk { Before = r.Before };
                r.Bytes = bytes; r.After = CostumeMemory.Unpacked(bytes);
                return r;
            }
        }

        /// <summary>Makes the pictures that came from Blender (not the costume's own) half the size, biggest first, until the
        /// costume unpacks to no more than <paramref name="limit"/> (or they're all down to 128 pixels).</summary>
        static CostumeMemory.Shrunk HalveNewPictures(byte[] pac, long limit, HashSet<string> originals, int maxSteps = 100)
        {
            var r = new CostumeMemory.Shrunk { Before = CostumeMemory.Unpacked(pac) };
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", pac))
            using (var outFile = new TempFile(".pac"))
            {
                using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    long size = r.Before;
                    var mine = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Where(t => !originals.Contains(t.Name) && !t.SharesData && !t.HasPalette).ToList();
                    var halvedNames = new Dictionary<string, string>(); var lengths = new Dictionary<TEX0Node, long>();
                    int steps = 0;
                    while (size > limit && steps++ < maxSteps)
                    {
                        var t = mine.Where(x => x.Width > 128 && x.Height > 128).OrderByDescending(x => (long)x.Width * x.Height).FirstOrDefault();
                        if (t == null) break;
                        long before = lengths.TryGetValue(t, out var known) ? known : (long)t.WorkingUncompressed.Length;
                        int w = t.Width / 2, h = t.Height / 2;
                        using (var img = new Bitmap(t.GetImage(0)))
                        using (var small = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            using (var g = Graphics.FromImage(small))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                g.DrawImage(img, new Rectangle(0, 0, w, h));
                            }
                            var map = BrawlLib.Wii.Textures.TextureConverter.Get(t.Format).EncodeTEX0Texture(small, Math.Max(1, Math.Min(t.LevelOfDetail, 4)));
                            long after = map.Length; lengths[t] = after;
                            t.ReplaceRaw(map);
                            size -= Math.Max(0, before - after);
                        }
                        halvedNames[t.Name] = $"{t.Name} made {w}x{h}";
                    }
                    r.Pictures.AddRange(halvedNames.Values);
                    root.Compression = "ExtendedLZ77";
                    root.Export(outFile.Path);
                }
                var bytes = File.ReadAllBytes(outFile.Path);
                r.After = CostumeMemory.Unpacked(bytes);
                r.Bytes = r.Pictures.Count > 0 ? bytes : null;
                return r;
            }
        }

        public static string CheckModel(byte[] costume)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var stack = new Stack<ResourceNode>(); stack.Push(root);
                int models = 0;
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (n is MDL0Node)
                    {
                        models++;
                        var src = n.WorkingUncompressed;
                        int size = src.Length;
                        var m = new byte[size];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, m, 0, size);
                        uint U(int o) => o + 4 <= m.Length ? (uint)(m[o] << 24 | m[o + 1] << 16 | m[o + 2] << 8 | m[o + 3]) : 0;
                        int ver = (int)U(8);
                        string[] names = ver >= 10 ? new[] { "Definitions", "Bones", "Vertices", "Normals", "Colors", "UVs", "FurVec", "FurLayer", "Materials", "Shaders", "Objects", "Textures", "Palettes" }
                                                   : new[] { "Definitions", "Bones", "Vertices", "Normals", "Colors", "UVs", "Materials", "Shaders", "Objects", "Textures", "Palettes" };
                        for (int k = 0; k < names.Length; k++)
                        {
                            int o = (int)U(0x10 + 4 * k);
                            if (o <= 0) continue;
                            if (o >= size) return names[k] + " list out of place";
                            int count = (int)U(o + 4);
                            if (count < 0 || count > 10000) return names[k] + " list damaged";
                            for (int e = 1; e <= count; e++)
                            {
                                int eo = o + 8 + 16 * e;
                                int data = (int)U(eo + 12);
                                if (o + data <= 0 || o + data >= size) return names[k] + " entry " + e + " points outside the model";
                            }
                        }
                        continue;   // its own lists aren't opened
                    }
                    foreach (var c in n.Children) stack.Push(c);
                }
                return models == 0 ? "no model in it" : null;
            }
        }

        // ----- Blender -----

        static string BlenderFile => Path.Combine(KitFolder, "blender-path.txt");

        /// <summary>Where Blender is: remembered, Windows' list of programs, the usual folders, Steam, or what opens .blend
        /// files. Null if not found (the window then asks once).</summary>
        public static string FindBlender()
        {
            try { if (File.Exists(BlenderFile)) { var p = File.ReadAllText(BlenderFile).Trim(); if (File.Exists(p)) return p; } } catch { }
            var found = new List<string>();
            void Dir(string d) { try { if (!string.IsNullOrEmpty(d)) { var f = Path.Combine(d.Trim('"'), "blender.exe"); if (File.Exists(f)) found.Add(f); } } catch { } }
            foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                foreach (var key in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    try
                    {
                        using (var un = hive.OpenSubKey(key))
                            if (un != null) foreach (var k in un.GetSubKeyNames())
                                using (var e = un.OpenSubKey(k))
                                {
                                    if (!((e?.GetValue("DisplayName") as string) ?? "").StartsWith("Blender", StringComparison.OrdinalIgnoreCase)) continue;
                                    Dir(e.GetValue("InstallLocation") as string);
                                    if (e.GetValue("DisplayIcon") is string icon) Dir(Path.GetDirectoryName(icon.Split(',')[0].Trim('"')));
                                }
                    }
                    catch { }
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                try { var bf = Path.Combine(pf, "Blender Foundation"); if (Directory.Exists(bf)) foreach (var d in Directory.GetDirectories(bf)) Dir(d); } catch { }
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                Dir(Path.Combine(pf, "Steam", "steamapps", "common", "Blender"));
            // what opens .blend files
            try
            {
                using (var ext = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(".blend"))
                {
                    var prog = ext?.GetValue("") as string;
                    using (var cmd = prog == null ? null : Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(prog + @"\shell\open\command"))
                        if (cmd?.GetValue("") is string c && c.Length > 1)
                        {
                            string exe = c.StartsWith("\"") ? c.Substring(1, c.IndexOf('"', 1) - 1) : c.Split(' ')[0];
                            Dir(Path.GetDirectoryName(exe));
                        }
                }
            }
            catch { }
            // the newest Blender 4 or later
            return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(f => { try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(f).FileVersion ?? ""; } catch { return ""; } }).FirstOrDefault();
        }

        public static void RememberBlender(string path) { try { Directory.CreateDirectory(KitFolder); File.WriteAllText(BlenderFile, path); } catch { } }

        /// <summary>Opens Blender with the P+ Kit loaded (nothing to install) and the costume open.</summary>
        public static void OpenInBlender(string blender, string glb)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string start = Path.Combine(here, "pplus_start.py");
            if (!File.Exists(start) || !File.Exists(Path.Combine(here, "pplus_blender_kit.py")))
                throw new InvalidOperationException("pplus_start.py or pplus_blender_kit.py is missing from the Blender Kit folder.");
            string bridge = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string Q(string x) => "\"" + x + "\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(blender, $"--python {Q(start)} -- {Q(glb)} {Q(bridge)}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(blender) });
        }

        public sealed class Back
        {
            public byte[] Costume; public string SavedTo, Character; public int FileNumber;
            public List<string> Notes = new List<string>();
            public Bitmap[] Before, After;
        }

        /// <summary>Blender's GLB (saved with the add-on's "Save for P+") back into a costume file, next to it in a
        /// "... (from Blender)" folder. keepFaces: the costume's own face and hair pieces stay exactly as they were.</summary>
        public static Back Bring(string glb, bool keepFaces, Func<byte[]> motionFor, Func<string, byte[]> cardFile = null, bool ownHead = false)
        {
            string folder = Path.GetDirectoryName(glb);
            var info = KitInfo.Load(folder) ?? throw new InvalidOperationException("That file isn't in a folder the Bridge made (it needs the \"" + KitInfo.FileName + "\" and \"" + KitInfo.CostumeFile + "\" next to it). Save it from Blender into the same folder the costume was sent to.");
            string basePath = Path.Combine(folder, KitInfo.CostumeFile);
            if (!File.Exists(basePath)) throw new InvalidOperationException("The costume copy (\"" + KitInfo.CostumeFile + "\") is missing from that folder. Send the costume to Blender again.");
            var c = Characters.All.FirstOrDefault(x => x.Name == info.Character) ?? throw new InvalidOperationException("Unknown fighter in " + KitInfo.FileName + ": " + info.Character);
            byte[] target = File.ReadAllBytes(basePath);
            var from = FromBlender.Read(glb, keepFaces);
            if (from.Mesh.Triangles == 0) throw new InvalidOperationException("No shapes were found in that file. In Blender, use the P+ Kit's \"Save and make the costume file\" button.");
            if (from.UnriggedShapes.Count == from.Shapes.Count)
                throw new InvalidOperationException("That file has no P+ skeleton in it (it was saved with Blender's own Export). In Blender, use the P+ Kit's \"Save and make the costume file\" button instead: it saves your model rigged to P+'s bones.");
            if (from.UnriggedShapes.Count > 0)
                throw new InvalidOperationException("Some shapes in that file aren't rigged to P+'s bones (" + string.Join(", ", from.UnriggedShapes.Take(4)) + (from.UnriggedShapes.Count > 4 ? "..." : "") +
                    "): it was saved with Blender's own Export, so your model and the costume would both end up in it. In Blender, use the P+ Kit's \"Save and make the costume file\" button instead.");
            var r = new Back { Character = c.Name, FileNumber = info.FileNumber };
            if (from.Unrigged > 0) r.Notes.Add($"{from.Unrigged:N0} points had no bones in Blender: they follow the nearest point of the costume (use \"Rig new parts like P+\" in Blender to set them).");
            // which of the costume's own shapes stay: the ones the game shows only sometimes, and (if asked) its face and hair pieces
            HashSet<string> hiddenNames, head, known;
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", target))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().First();
                var bones = model._linker.BoneCache;
                hiddenNames = new HashSet<string>(info.Hidden.Where(i => i >= 0 && i < bones.Length).Select(i => bones[i].Name));
                head = RiggedExport.HeadBones(model);
                known = new HashSet<string>(bones.Select(b => b.Name), StringComparer.Ordinal);
            }
            // only the costume's own bones (a bone added in Blender doesn't exist in the game)
            List<(string Bone, float W)> WeightsAt(BrawlLib.Internal.Vector3 v)
            {
                var w = from.At(v)?.Where(x => known.Contains(x.Bone)).ToList();
                if (w == null || w.Count == 0) return null;
                float sum = w.Sum(x => x.W);
                return w.Select(x => (x.Bone, x.W / sum)).ToList();
            }
            // your model's own head: none of the costume's head pieces stay (not even its other faces, which would show
            // up on your head when blinking); only the things in hand do
            if (ownHead) keepFaces = false;
            bool Keep(MDL0ObjectNode obj) => !RiggedExport.ShownByDefault(obj, hiddenNames) ? !(ownHead && RiggedExport.HeadShare(obj, head) >= 0.5f)
                : keepFaces && RiggedExport.HeadShare(obj, head) >= 0.8f;
            var built = ModelSwap.BuildKeeping(from.Mesh, target, Path.GetFileNameWithoutExtension(glb), WeightsAt, Keep, from.PictureName);
            // every mix of bones is a matrix the game makes for the fighter: never more than the costume had (more crashed
            // the game as the match started: "fighter instance create", then a write to an empty pointer)
            int mixesBefore = MixCount(target);
            for (int tries = 0; tries < 4; tries++)
            {
                int now = MixCount(built.Costume);
                if (now <= mixesBefore) break;
                if (tries == 3) throw new InvalidOperationException($"The costume came out with {now} bone mixes where it had {mixesBefore}: the game can't build that. Please send the Blender file to Last Zodiac.");
                built = ModelSwap.BuildKeeping(from.Mesh, target, Path.GetFileNameWithoutExtension(glb), WeightsAt, Keep, from.PictureName, null, Math.Max(8, built.MixBudget - (now - mixesBefore) - 2));
            }
            r.Costume = built.Costume;
            // pictures no shape uses any more (the old body's, leftovers the costume already carried) are taken out
            var elsewhere = PicturesUsedElsewhere(c, cardFile);
            var dropped = DropUnusedPictures(r.Costume, elsewhere);
            if (dropped.Bytes != null) { r.Costume = dropped.Bytes; r.Notes.Add("Took out pictures nothing uses: " + string.Join(", ", dropped.Pictures) + "."); }
            // the fighter's other models (Peach's parasol...) take some of their pictures from the costume: any the
            // costume had that went missing are put back, or the game crashes loading them
            var restored = PutBackPicturesUsedElsewhere(r.Costume, target, elsewhere);
            if (restored.Bytes != null) { r.Costume = restored.Bytes; r.Notes.Add("Put back pictures the fighter's other models use: " + string.Join(", ", restored.Pictures) + "."); }
            // the file is read back the way the game reads it: every list in the model must point inside it
            string broken = CheckModel(r.Costume);
            if (broken != null) throw new InvalidOperationException("The costume file came out broken (" + broken + "), so it wasn't saved. Please send the Blender file to Last Zodiac.");
            r.Notes.AddRange(built.Notes);
            r.Notes.Add($"{built.Triangles:N0} triangles from Blender ({from.Shapes.Count} shapes), {built.Pictures} pictures; {built.Mixes} new bone mixes; {MixCount(built.Costume)} in all (the costume had {mixesBefore}).");
            // it has to fit in the game's memory (the Mod Manager checks this too): the same budget, the same shrinking
            long size = CostumeMemory.Unpacked(r.Costume), budget = Budget(c, info, target, cardFile);
            if (budget > 0 && size > budget)
            {
                var shrunk = CostumeMemory.Shrink(r.Costume, budget);
                if (shrunk.Bytes == null)
                {
                    // still too big: the pictures that came from Blender are made smaller (half the size each time, biggest first)
                    var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    lock (BrawlGate.Lock) using (var tmp = new TempFile(".pac", target)) using (var root = NodeFactory.FromFile(null, tmp.Path))
                        foreach (var t in root.FindChildrenByType(null, ResourceType.TEX0)) originals.Add(t.Name);
                    // one halving at a time, each followed by the usual lighter formats, so pictures are only made as small
                    // as they have to be
                    byte[] cur = r.Costume; var halvedNames = new Dictionary<string, string>();
                    for (int step = 0; step < 8 && shrunk.Bytes == null; step++)
                    {
                        var halved = HalveNewPictures(cur, 0, originals, 1);
                        if (halved.Bytes == null) break;
                        cur = halved.Bytes;
                        foreach (var n in halved.Pictures) halvedNames[n.Split(' ')[0]] = n;
                        var again = CostumeMemory.Unpacked(cur) <= budget ? null : CostumeMemory.Shrink(cur, budget);
                        var fits = again == null ? cur : again.Bytes;
                        if (fits == null) continue;
                        shrunk = new CostumeMemory.Shrunk { Bytes = fits, Before = size, After = CostumeMemory.Unpacked(fits) };
                        shrunk.Pictures.AddRange(halvedNames.Values);
                        if (again != null) shrunk.Pictures.AddRange(again.Pictures);
                        r.Notes.Add($"Your model's pictures had to be made smaller: {c.Name}'s memory is mostly used by " + (keepFaces ? "the P+ head (with its blinking faces) and " : "") +
                            "the rest of the costume. For sharper pictures: in Blender, \"Keep my model's head\" at Finish.");
                    }
                }
                if (shrunk.Bytes == null)
                    throw new InvalidOperationException($"The costume came out too big for the game's memory ({CostumeMemory.Kb(size)} unpacked, {CostumeMemory.Kb(budget)} fits for {c.Name}), even with lighter pictures. Use smaller pictures (textures) on your model in Blender, or fewer of them, and save again.");
                r.Costume = shrunk.Bytes;
                r.Notes.Add($"Made lighter to fit the game's memory ({CostumeMemory.Kb(size)} → {CostumeMemory.Kb(shrunk.After)}; {CostumeMemory.Kb(budget)} fits): " + string.Join("; ", shrunk.Pictures) + ".");
            }
            else if (budget > 0) r.Notes.Add($"Fits the game's memory: {CostumeMemory.Kb(size)} unpacked, {CostumeMemory.Kb(budget)} fits for {c.Name}.");
            else r.Notes.Add("The game's memory couldn't be checked here (no SD card found); P+ Modding Tool checks it when you add it.");
            // named after the costume: P+ Modding Tool names the skin after the folder it's in
            string outDir = Path.Combine(folder, Safe(info.Label ?? c.Name) + " (from Blender)");
            Directory.CreateDirectory(outDir);
            r.SavedTo = Path.Combine(outDir, $"Fit{c.FilePrefix}{info.FileNumber:00}.pac");
            File.WriteAllBytes(r.SavedTo, r.Costume);
            // before and after, standing and in two poses
            var motion = motionFor?.Invoke();
            Bitmap Pic(byte[] cos, string anim, int frame) { try { return anim == null || motion == null ? ModelPreview.Render(cos, 220, 280) : ModelPreview.RenderPosed(cos, motion, anim, frame, 220, 280); } catch { return null; } }
            var poses = new (string, int)[] { (null, 0), ("Wait1", 30), ("AttackS4S", 20) };
            r.Before = poses.Select(p => Pic(target, p.Item1, p.Item2)).ToArray();
            r.After = poses.Select(p => Pic(r.Costume, p.Item1, p.Item2)).ToArray();
            return r;
        }
    }
}
