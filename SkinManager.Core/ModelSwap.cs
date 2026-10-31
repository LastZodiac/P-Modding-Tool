// Model swapper (first stage): puts another model (a .dae or .obj) on a P+ character's own skeleton.
//   1. Load: the model's triangles, texture coordinates and pictures.
//   2. Fit: stood up and turned as asked, sized to the P+ costume's height, put where the P+ costume stands.
//   3. Simplify: fewer triangles when it has more than the budget (close points merged on a grid).
//   4. Rig: every point of the new model follows the bones that the nearest points of the P+ costume follow
//      (their weights are blended), so it moves with P+'s own skeleton and animations.
//   5. Build: the P+ costume file with its model's meshes, materials and pictures replaced by the new ones.
//      The skeleton isn't touched (bones, their names, order and positions stay P+'s), which is what keeps hits
//      the same for both players; the app's normal checks confirm that before anything goes on the SD card.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using BrawlLib.Internal;
using BrawlLib.Modeling;
using BrawlLib.Modeling.Collada;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Models;
using BrawlLib.Wii.Textures;

namespace SkinManager.Core
{
    public sealed class SwapPart
    {
        public string Material;
        public string TexturePath;          // a picture file, or null (plain colour)
        public List<Vector3> P = new List<Vector3>();   // three corners per triangle
        public List<Vector3> N = new List<Vector3>();
        public List<Vector2> UV = new List<Vector2>();
        public int Triangles => P.Count / 3;
    }

    public sealed class SwapMesh
    {
        public List<SwapPart> Parts = new List<SwapPart>();
        public int Triangles => Parts.Sum(p => p.Triangles);
        public List<string> Notes = new List<string>();

        public SwapMesh Copy()
        {
            var m = new SwapMesh { Notes = new List<string>(Notes) };
            foreach (var p in Parts)
                m.Parts.Add(new SwapPart { Material = p.Material, TexturePath = p.TexturePath, P = new List<Vector3>(p.P), N = new List<Vector3>(p.N), UV = new List<Vector2>(p.UV) });
            return m;
        }

        public (Vector3 Min, Vector3 Max) Box()
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var p in Parts) foreach (var v in p.P)
            {
                min = new Vector3(Math.Min(min._x, v._x), Math.Min(min._y, v._y), Math.Min(min._z, v._z));
                max = new Vector3(Math.Max(max._x, v._x), Math.Max(max._y, v._y), Math.Max(max._z, v._z));
            }
            return (min, max);
        }
    }

    /// <summary>How to place the model: stood up (for models made lying down), turned, and a size change.</summary>
    public sealed class SwapFit
    {
        public bool StandUp;     // the model was made with Z up (lying on its back here)
        public int Turn;         // quarter turns around the up axis
        public float Size = 1f;  // 1 = the P+ costume's height
    }

    public static partial class ModelSwap
    {
        /// <summary>Rig by P+'s skeleton (true) or by the nearest points of P+'s own costume (false, the first way).</summary>
        public static bool UseSkeleton = true;
        public const int DefaultBudget = 8000;   // triangles; P+'s own costumes are about 3,000 to 10,000
        static readonly FieldInfo TrianglesField = typeof(PrimitiveManager).GetField("_triangles", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly string[] PictureTypes = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        // ----- 1. load -----

        public static SwapMesh Load(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".obj") return LoadObj(path);
            if (ext == ".dae") return LoadDae(path);
            if (ext == ".glb" || ext == ".gltf") return LoadGltf(path);
            if (ext == ".fbx") throw new InvalidOperationException("FBX files can't be read yet. Save or download the model as GLB or OBJ instead (most 3D programs and sites offer both).");
            throw new InvalidOperationException("The model swapper reads .glb, .gltf, .obj and .dae files. Most 3D programs and sites (Blender, for example) can save one of those.");
        }

        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        static SwapMesh LoadObj(string path)
        {
            var mesh = new SwapMesh();
            var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
            var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string dir = Path.GetDirectoryName(path);
            SwapPart part = null;
            SwapPart Part(string mat)
            {
                var p = mesh.Parts.FirstOrDefault(x => x.Material == mat);
                if (p == null) { p = new SwapPart { Material = mat }; mesh.Parts.Add(p); }
                return p;
            }
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "v": v.Add(new Vector3(F(t[1]), F(t[2]), F(t[3]))); break;
                    case "vt": vt.Add(new Vector2(F(t[1]), t.Length > 2 ? 1 - F(t[2]) : 0)); break;
                    case "vn": vn.Add(new Vector3(F(t[1]), F(t[2]), F(t[3]))); break;
                    case "mtllib":
                        try { foreach (var kv in ReadMtl(Path.Combine(dir, line.Substring(7).Trim()))) textures[kv.Key] = kv.Value; } catch { }
                        break;
                    case "usemtl": part = Part(line.Substring(7).Trim()); break;
                    case "f":
                        if (part == null) part = Part("default");
                        var corners = t.Skip(1).Select(c =>
                        {
                            var k = c.Split('/');
                            int Idx(string s, int count) { int i = int.Parse(s, CultureInfo.InvariantCulture); return i < 0 ? count + i : i - 1; }
                            int pi = Idx(k[0], v.Count);
                            int ti = k.Length > 1 && k[1] != "" ? Idx(k[1], vt.Count) : -1;
                            int ni = k.Length > 2 && k[2] != "" ? Idx(k[2], vn.Count) : -1;
                            return (pi, ti, ni);
                        }).ToList();
                        for (int i = 1; i + 1 < corners.Count; i++)
                            foreach (var c in new[] { corners[0], corners[i], corners[i + 1] })
                            {
                                part.P.Add(v[c.pi]);
                                part.UV.Add(c.ti >= 0 && c.ti < vt.Count ? vt[c.ti] : new Vector2(0, 0));
                                part.N.Add(c.ni >= 0 && c.ni < vn.Count ? vn[c.ni] : new Vector3(0, 0, 0));
                            }
                        break;
                }
            }
            foreach (var p in mesh.Parts)
            {
                if (textures.TryGetValue(p.Material, out string tex)) p.TexturePath = tex;
                FillMissingNormals(p);
            }
            mesh.Parts.RemoveAll(p => p.Triangles == 0);
            if (mesh.Triangles == 0) throw new InvalidOperationException("No triangles were found in that model.");
            return mesh;
        }

        static Dictionary<string, string> ReadMtl(string path)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string mat = null, dir = Path.GetDirectoryName(path);
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("newmtl ")) mat = line.Substring(7).Trim();
                else if (line.StartsWith("map_Kd ") && mat != null)
                {
                    string f = line.Substring(7).Trim();
                    var parts = f.Split(' ');
                    f = parts[parts.Length - 1];   // options like -s 1 1 1 come first
                    string full = FindPicture(dir, f);
                    if (full != null) r[mat] = full;
                }
            }
            return r;
        }

        /// <summary>A picture the model names, looked for next to the model (and in its folders).</summary>
        static string FindPicture(string dir, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = Uri.UnescapeDataString(name.Replace("file:///", "").Replace("file://", "")).Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            try { string direct = Path.IsPathRooted(name) ? name : Path.Combine(dir, name); if (File.Exists(direct)) return direct; } catch { }
            string file = Path.GetFileName(name), stem = Path.GetFileNameWithoutExtension(name);
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(f), file, StringComparison.OrdinalIgnoreCase)) return f;
                }
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase) && PictureTypes.Contains(Path.GetExtension(f).ToLowerInvariant())) return f;
            }
            catch { }
            return null;
        }

        static unsafe SwapMesh LoadDae(string path)
        {
            var mesh = new SwapMesh();
            lock (BrawlGate.Lock)
            {
                Installer.Quiet();
                Collada._importOptions = new Collada.ImportOptions();
                IModel imported;
                using (var form = new Collada()) imported = form.ImportModel(path, Collada.ImportType.MDL0);
                var model = imported as MDL0Node ?? throw new InvalidOperationException("That model couldn't be read: " + Collada.Error);
                try
                {
                    model.Populate();
                    model.ApplyCHR(null, 0);
                    string dir = Path.GetDirectoryName(path);
                    var picture = ImagePaths(path);
                    foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>())
                    {
                        var mgr = obj._manager;
                        if (mgr?._vertices == null || mgr._faceData?[0] == null) continue;
                        var prim = TrianglesField?.GetValue(mgr) as GLPrimitive;
                        if (prim == null) continue;
                        var mat = obj._drawCalls.FirstOrDefault()?.MaterialNode;
                        string texName = mat?.Children.OfType<MDL0MaterialRefNode>().FirstOrDefault()?.Texture;
                        string matName = mat?.Name ?? obj.Name;
                        var part = mesh.Parts.FirstOrDefault(x => x.Material == matName);
                        if (part == null)
                        {
                            part = new SwapPart { Material = matName };
                            if (texName != null) part.TexturePath = picture.TryGetValue(texName, out var p) ? p : FindPicture(dir, texName);
                            mesh.Parts.Add(part);
                        }
                        ushort* idx = (ushort*)mgr._indices.Address;
                        Vector3* nrm = mgr._faceData[1] != null ? (Vector3*)mgr._faceData[1].Address : null;
                        Vector2* uv = mgr._faceData[4] != null ? (Vector2*)mgr._faceData[4].Address : null;
                        var ind = prim._indices;
                        for (int i = 0; i + 2 < ind.Length; i += 3)
                            for (int k = 0; k < 3; k++)
                            {
                                uint fp = ind[i + k];
                                int vi = idx[fp];
                                var vert = vi < mgr._vertices.Count ? mgr._vertices[vi] : null;
                                var w = vert?.WeightedPosition ?? new Vector3();
                                part.P.Add(w);
                                var n = nrm != null ? nrm[fp] : new Vector3();
                                // normals turn with the vertex's bone (weighted points are already placed)
                                var mtx = vert?.GetMatrixNode();
                                if (mtx is IBoneNode bone) n = bone.BindMatrix.GetRotationMatrix() * n;
                                part.N.Add(n);
                                part.UV.Add(uv != null ? uv[fp] : new Vector2(0, 0));
                            }
                    }
                }
                finally { model.Dispose(); }
            }
            foreach (var p in mesh.Parts) FillMissingNormals(p);
            mesh.Parts.RemoveAll(p => p.Triangles == 0);
            if (mesh.Triangles == 0) throw new InvalidOperationException("No triangles were found in that model.");
            return mesh;
        }

        /// <summary>Image name -> file, from the .dae's own list of pictures.</summary>
        static Dictionary<string, string> ImagePaths(string dae)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = new XmlDocument(); doc.Load(dae);
                string dir = Path.GetDirectoryName(dae);
                foreach (XmlElement img in doc.GetElementsByTagName("image"))
                {
                    var init = img.GetElementsByTagName("init_from").Cast<XmlElement>().FirstOrDefault();
                    string file = init?.InnerText?.Trim();
                    if (string.IsNullOrEmpty(file)) continue;
                    string full = FindPicture(dir, file);
                    if (full == null) continue;
                    foreach (var key in new[] { img.GetAttribute("name"), img.GetAttribute("id"), Path.GetFileNameWithoutExtension(file) })
                        if (!string.IsNullOrEmpty(key) && !r.ContainsKey(key)) r[key] = full;
                }
            }
            catch { }
            return r;
        }

        static void FillMissingNormals(SwapPart p)
        {
            for (int i = 0; i + 2 < p.P.Count; i += 3)
            {
                if (Len(p.N[i]) > 1e-6f && Len(p.N[i + 1]) > 1e-6f && Len(p.N[i + 2]) > 1e-6f) continue;
                var n = Normalize(Cross(Sub(p.P[i + 1], p.P[i]), Sub(p.P[i + 2], p.P[i])));
                for (int k = 0; k < 3; k++) if (Len(p.N[i + k]) <= 1e-6f) p.N[i + k] = n;
            }
        }

        // ----- 2. fit -----

        /// <summary>The model stood up, turned and sized to the box the P+ costume stands in.</summary>
        public static SwapMesh Fit(SwapMesh source, (Vector3 Min, Vector3 Max) target, SwapFit fit)
        {
            var m = source.Copy();
            double turn = fit.Turn * Math.PI / 2, ct = Math.Cos(turn), st = Math.Sin(turn);
            Vector3 Rot(Vector3 v)
            {
                if (fit.StandUp) v = new Vector3(v._x, v._z, -v._y);           // Z up -> Y up
                return new Vector3((float)(v._x * ct + v._z * st), v._y, (float)(-v._x * st + v._z * ct));
            }
            foreach (var p in m.Parts)
            {
                for (int i = 0; i < p.P.Count; i++) { p.P[i] = Rot(p.P[i]); p.N[i] = Normalize(Rot(p.N[i])); }
            }
            var box = m.Box();
            float h = Math.Max(1e-6f, box.Max._y - box.Min._y), th = target.Max._y - target.Min._y;
            float k = th / h * fit.Size;
            float cx = (box.Min._x + box.Max._x) / 2, cz = (box.Min._z + box.Max._z) / 2;
            // centred on what it stands on (the lower quarter: legs and feet), so long hair or a cape on one side doesn't push it over
            float low = box.Min._y + (box.Max._y - box.Min._y) * 0.25f;
            var legs = m.Parts.SelectMany(p => p.P).Where(v => v._y <= low).ToList();
            if (legs.Count > 20) { cx = (legs.Min(v => v._x) + legs.Max(v => v._x)) / 2; cz = (legs.Min(v => v._z) + legs.Max(v => v._z)) / 2; }
            float tx = (target.Min._x + target.Max._x) / 2, tz = (target.Min._z + target.Max._z) / 2;
            foreach (var p in m.Parts)
                for (int i = 0; i < p.P.Count; i++)
                {
                    var v = p.P[i];
                    p.P[i] = new Vector3((v._x - cx) * k + tx, (v._y - box.Min._y) * k + target.Min._y, (v._z - cz) * k + tz);
                }
            return m;
        }

        // ----- 3. simplify -----

        /// <summary>Fewer triangles: points closer than a grid cell are merged, the cell size found by trying.</summary>
        public static SwapMesh Simplify(SwapMesh source, int budget)
        {
            if (source.Triangles <= budget) return source;
            var box = source.Box();
            float size = Math.Max(box.Max._x - box.Min._x, Math.Max(box.Max._y - box.Min._y, box.Max._z - box.Min._z));
            float lo = size / 4000f, hi = size / 8f;
            SwapMesh best = null;
            for (int step = 0; step < 18; step++)
            {
                float cell = (float)Math.Sqrt(lo * hi);
                var m = Cluster(source, cell, box.Min);
                if (m.Triangles > budget) lo = cell; else { best = m; hi = cell; }
                if (best != null && best.Triangles > budget * 0.9) break;
            }
            best = best ?? Cluster(source, hi, box.Min);
            best.Notes.Add($"Simplified from {source.Triangles:N0} to {best.Triangles:N0} triangles.");
            return best;
        }

        static SwapMesh Cluster(SwapMesh source, float cell, Vector3 origin)
        {
            var sum = new Dictionary<long, (double x, double y, double z, int n)>();
            long Key(Vector3 v)
            {
                long x = (long)Math.Floor((v._x - origin._x) / cell), y = (long)Math.Floor((v._y - origin._y) / cell), z = (long)Math.Floor((v._z - origin._z) / cell);
                return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
            }
            foreach (var p in source.Parts) foreach (var v in p.P)
            {
                long k = Key(v);
                sum.TryGetValue(k, out var s);
                sum[k] = (s.x + v._x, s.y + v._y, s.z + v._z, s.n + 1);
            }
            var m = new SwapMesh { Notes = new List<string>(source.Notes) };
            foreach (var p in source.Parts)
            {
                var q = new SwapPart { Material = p.Material, TexturePath = p.TexturePath };
                for (int i = 0; i + 2 < p.P.Count; i += 3)
                {
                    long a = Key(p.P[i]), b = Key(p.P[i + 1]), c = Key(p.P[i + 2]);
                    if (a == b || b == c || a == c) continue;   // the triangle shrank to a line or a point
                    foreach (var (k, j) in new[] { (a, i), (b, i + 1), (c, i + 2) })
                    {
                        var s = sum[k];
                        q.P.Add(new Vector3((float)(s.x / s.n), (float)(s.y / s.n), (float)(s.z / s.n)));
                        q.N.Add(p.N[j]); q.UV.Add(p.UV[j]);
                    }
                }
                if (q.Triangles > 0) m.Parts.Add(q);
            }
            return m;
        }

        // ----- the P+ costume: where its points are and which bones they follow -----

        sealed class RefPoint { public Vector3 P; public List<(string Bone, float W)> Weights; }

        /// <summary>The box the P+ costume stands in (its own model, as shown by default).</summary>
        public static (Vector3 Min, Vector3 Max) TargetBox(byte[] costume)
        {
            var pts = ReferencePoints(costume);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var r in pts)
            {
                min = new Vector3(Math.Min(min._x, r.P._x), Math.Min(min._y, r.P._y), Math.Min(min._z, r.P._z));
                max = new Vector3(Math.Max(max._x, r.P._x), Math.Max(max._y, r.P._y), Math.Max(max._z, r.P._z));
            }
            return (min, max);
        }

        static List<RefPoint> ReferencePoints(byte[] costume)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault() ?? throw new InvalidOperationException("That costume file has no model.");
                model.ApplyCHR(null, 0);
                return ReferencePoints(model);
            }
        }

        static List<RefPoint> ReferencePoints(MDL0Node model)
        {
            var list = new List<RefPoint>();
            foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>())
            {
                var call = obj._drawCalls.FirstOrDefault(c => c.VisibilityBoneNode == null || c.VisibilityBoneNode.Visible);
                if (call == null || obj._manager?._vertices == null) continue;
                foreach (var v in obj._manager._vertices)
                {
                    var node = v.GetMatrixNode();
                    if (node == null) continue;
                    var w = node.Weights.Where(x => x.Bone is MDL0BoneNode && x.Weight > 0).Select(x => (((MDL0BoneNode)x.Bone).Name, x.Weight)).ToList();
                    if (w.Count == 0) continue;
                    list.Add(new RefPoint { P = v.WeightedPosition, Weights = w });
                }
            }
            if (list.Count == 0) throw new InvalidOperationException("The P+ costume's model has no points to copy the rigging from.");
            return list;
        }

        // ----- 4 and 5. rig and build -----

        public sealed class Result
        {
            public byte[] Costume;          // the new costume file
            public int Triangles, Pictures;
            public int Mixes, MixBudget;   // different mixes of bones used, and the P+ costume's own number
            public List<string> Notes = new List<string>();
        }

        /// <summary>The P+ costume file with the new model in place of its own (skeleton untouched).</summary>
        static void Trace(string what) { if (Environment.GetEnvironmentVariable("SKINMANAGER_TRACE") != null) Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + "swap: " + what); }

        public static Result Build(SwapMesh mesh, byte[] costume, string name, Func<Vector3, bool> keepWithBody = null, Func<Vector3, List<(string Bone, float W)>> weights = null)
        {
            mesh = mesh.Copy();   // its pictures are re-saved below: the caller's model stays as it was
            var result = new Result { Notes = new List<string>(mesh.Notes) };
            string work = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "swap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // the new model as a plain .dae with its pictures (sized for the Wii), read back by BrawlLib's importer
                var pictures = WritePictures(mesh, work, result.Notes);
                string dae = Path.Combine(work, "model.dae");
                WriteDae(mesh, pictures, dae); Trace("dae written");
                lock (BrawlGate.Lock)
                {
                    Installer.Quiet();
                    Collada._importOptions = new Collada.ImportOptions();
                    IModel imported;
                    using (var form = new Collada()) imported = form.ImportModel(dae, Collada.ImportType.MDL0);
                    Trace("imported");
                    var newModel = imported as MDL0Node ?? throw new InvalidOperationException("The new model couldn't be made: " + Collada.Error);
                    using (var tmp = new TempFile(".pac", costume))
                    using (var root = NodeFactory.FromFile(null, tmp.Path))
                    using (var outFile = new TempFile(".pac"))
                    {
                        var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault() ?? throw new InvalidOperationException("That costume file has no model.");
                        var brres = model.Parent?.Parent as BRRESNode ?? root.FindChildrenByType(null, ResourceType.BRES).OfType<BRRESNode>().First(b => b.FindChildrenByType(null, ResourceType.MDL0).Contains(model));
                        model.Populate();
                        model.ApplyCHR(null, 0);
                        Trace("posed");
                        var refs = ReferencePoints(model);
                        Trace("refs");
                        var grid = new PointGrid(refs);
                        var rig = UseSkeleton ? SkeletonRig.Make(model, refs) : null;
                        var centre = rig != null && keepWithBody != null ? rig.Centre() : null;   // painted points (hair...) follow only the middle of the body
                        Trace("grid+rig");
                        var bones = model._linker.BoneCache.ToDictionary(b => b.Name, b => b, StringComparer.Ordinal);
                        // the pictures live in the file's texture BRRES (Texture Data [n]), next to the model's
                        var oldNames = new HashSet<string>(model.FindChildrenByType(null, ResourceType.MDL0Material).OfType<MDL0MaterialNode>()
                            .SelectMany(m => m.Children.OfType<MDL0MaterialRefNode>()).Select(r => r.Texture).Where(n => n != null), StringComparer.Ordinal);
                        var texBrres = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Where(t => oldNames.Contains(t.Name))
                            .Select(t => t.Parent?.Parent as BRRESNode).FirstOrDefault(b => b != null) ?? brres;
                        bool hadMetal = model.FindChildrenByType(null, ResourceType.MDL0Material).Any(m => m.Name.EndsWith("_ExtMtl", StringComparison.Ordinal));

                        Trace("textures found");
                        // out with the P+ model's own meshes, materials and pictures
                        foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().ToList())
                            obj.Remove(true, true, true, true, true, true, true, true, true, true, true, true);
                        Trace("objects removed");
                        foreach (var mat in model.FindChildrenByType(null, ResourceType.MDL0Material).OfType<MDL0MaterialNode>().ToList()) mat.Remove(true);
                        foreach (var t in texBrres.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Where(t => oldNames.Contains(t.Name)).ToList()) t.Remove(true);   // true: its palette goes too, without BrawlLib asking (a question box stopped the swap)
                        foreach (var p in texBrres.FindChildrenByType(null, ResourceType.PLT0).Where(p => oldNames.Contains(p.Name)).ToList()) p.Remove();

                        Trace("old model cleared");
                        // in with the new meshes, each point following the bones its nearest P+ points follow
                        newModel.Populate();
                        newModel.ResetToBindState();
                        int points = 0;
                        // every different mix of bones is a matrix the game keeps for the fighter: too many and the game can't make
                        // the model (it crashed with 2,331 where P+'s Peach has 253). Mixes are snapped to a few steps so there are
                        // never more than the P+ costume itself has.
                        int budget = Math.Max(60, model._influences?.Count ?? 0);
                        var newObjs = newModel.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().ToList();
                        var raw = new Dictionary<Vertex3, List<(string Bone, float W)>>();
                        foreach (var obj in newObjs)
                            foreach (var v in obj._manager._vertices)
                                raw[v] = weights?.Invoke(v._position) ?? (centre != null && keepWithBody(v._position) ? centre.Weights(v._position) : rig != null ? rig.Weights(v._position) : grid.Weights(v._position));
                        var snapped = SnapWeights(raw.Values.ToList(), budget, out int mixes, out string how);
                        Trace($"bone mixes: {mixes} (P+'s own costume: {budget}), {how}");
                        result.Mixes = mixes; result.MixBudget = budget;
                        int at = 0;
                        var snappedFor = new Dictionary<Vertex3, List<(string Bone, float W)>>();
                        foreach (var kv in raw) snappedFor[kv.Key] = snapped[at++];
                        foreach (var obj in newObjs)
                        {
                            obj.MatrixNode = null;   // points to world positions
                            foreach (var v in obj._manager._vertices)
                            {
                                var w = snappedFor[v];
                                IMatrixNode target = w.Count == 1 ? (IMatrixNode)bones[w[0].Bone] : new Influence(w.Select(x => new BoneWeight(bones[x.Bone], x.W)).ToList());
                                v.DeferUpdateAssets();   // the object's lists are updated once, below
                                v.MatrixNode = target;
                                points++;
                            }
                            obj.SetEditedAssets(false, true, true);
                            foreach (var call in obj._drawCalls) call.VisibilityBoneNode = model._linker.BoneCache[0];   // always shown
                            obj._reOptimized = true;
                            model.ReplaceOrAddMesh(obj, false, false, true);
                            obj.SetEditedAssets(false, true, true, false, false, true);   // the copy made by the move forgets that its points changed: rebuild its draw lists
                        }
                        Trace("meshes moved, " + points + " points");
                        if (hadMetal) { try { model.GenerateMetalMaterials(); } catch (Exception e) { result.Notes.Add("Metal look not made: " + e.Message); } }

                        // the pictures, in the Wii's compressed format
                        foreach (var kv in pictures)
                        {
                            using (var bmp = new Bitmap(kv.Value))
                            {
                                bool alpha = HasAlpha(bmp);
                                var tex = new TEX0Node { Name = kv.Key };
                                texBrres.GetOrCreateFolder<TEX0Node>().AddChild(tex);
                                var conv = alpha ? TextureConverter.RGB5A3 : (TextureConverter)TextureConverter.CMPR;
                                tex.ReplaceRaw(conv.EncodeTEX0Texture(bmp, 1));
                            }
                        }
                        result.Pictures = pictures.Count;
                        result.Triangles = mesh.Triangles;
                        Trace("pictures added"); root.Export(outFile.Path); Trace("saved");
                        result.Costume = File.ReadAllBytes(outFile.Path);
                    }
                    newModel.Dispose();
                }
            }
            finally { try { Directory.Delete(work, true); } catch { } }
            return result;
        }

        /// <summary>Weights snapped to steps (a tenth, a quarter, a half...) and to fewer bones per point until the number
        /// of different mixes is within the budget. The coarsest step (one bone per point) always fits: one mix per bone.</summary>
        internal static List<List<(string Bone, float W)>> SnapWeights(List<List<(string Bone, float W)>> raw, int budget, out int mixes, out string how)
        {
            List<List<(string, float)>> best = null; mixes = 0; how = "";
            foreach (var (bones, steps) in new[] { (3, 10), (2, 10), (2, 8), (2, 5), (2, 4), (2, 3), (2, 2), (1, 1) })
            {
                var r = raw.Select(w => Snap(w, bones, steps)).ToList();
                int n = r.Select(Key).Distinct().Count(w => w.Contains("|"));   // single bones aren't mixes
                best = r; mixes = n; how = $"up to {bones} bones per point, in steps of 1/{steps}";
                if (n <= budget) break;
            }
            return best;
        }

        static string Key(List<(string Bone, float W)> w) => string.Join("|", w.OrderBy(x => x.Bone, StringComparer.Ordinal).Select(x => x.Bone + ":" + x.W.ToString("0.###", CultureInfo.InvariantCulture)));

        /// <summary>The strongest bones, their weights in whole steps adding up to 1 (largest remainders get the leftover steps).</summary>
        static List<(string Bone, float W)> Snap(List<(string Bone, float W)> w, int bones, int steps)
        {
            var top = w.OrderByDescending(x => x.W).Take(bones).ToList();
            float total = top.Sum(x => x.W);
            if (total <= 0) return new List<(string, float)> { (w[0].Bone, 1f) };
            var share = top.Select(x => (x.Bone, Exact: x.W / total * steps)).ToList();
            var whole = share.Select(x => (x.Bone, N: (int)Math.Floor(x.Exact), Frac: x.Exact - Math.Floor(x.Exact))).ToList();
            int left = steps - whole.Sum(x => x.N);
            foreach (var i in Enumerable.Range(0, whole.Count).OrderByDescending(i => whole[i].Frac).Take(left).ToList())
                whole[i] = (whole[i].Bone, whole[i].N + 1, 0);
            var r = whole.Where(x => x.N > 0).OrderByDescending(x => x.N).Select(x => (x.Bone, (float)x.N / steps)).ToList();
            return r.Count > 0 ? r : new List<(string, float)> { (top[0].Bone, 1f) };
        }

        /// <summary>Nearest P+ points (a grid of buckets) and their blended bone weights.</summary>
        sealed class PointGrid
        {
            readonly Dictionary<long, List<RefPoint>> _cells = new Dictionary<long, List<RefPoint>>();
            readonly float _cell;
            readonly List<RefPoint> _all;
            public PointGrid(List<RefPoint> points)
            {
                _all = points;
                float minX = points.Min(p => p.P._x), maxX = points.Max(p => p.P._x), minY = points.Min(p => p.P._y), maxY = points.Max(p => p.P._y);
                _cell = Math.Max(0.01f, Math.Max(maxX - minX, maxY - minY) / 40f);
                foreach (var p in points)
                {
                    long k = Key(p.P);
                    if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<RefPoint>();
                    l.Add(p);
                }
            }
            long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);
            long Key(Vector3 v) => Key((int)Math.Floor(v._x / _cell), (int)Math.Floor(v._y / _cell), (int)Math.Floor(v._z / _cell));

            List<RefPoint> Nearest(Vector3 v, int count)
            {
                int cx = (int)Math.Floor(v._x / _cell), cy = (int)Math.Floor(v._y / _cell), cz = (int)Math.Floor(v._z / _cell);
                var found = new List<RefPoint>();
                for (int r = 0; r < 64 && found.Count < count; r++)
                {
                    for (int x = cx - r; x <= cx + r; x++)
                        for (int y = cy - r; y <= cy + r; y++)
                            for (int z = cz - r; z <= cz + r; z++)
                            {
                                if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) != r) continue;   // the shell only
                                if (_cells.TryGetValue(Key(x, y, z), out var l)) found.AddRange(l);
                            }
                    if (found.Count >= count && r == 0) { r++; for (int x = cx - r; x <= cx + r; x++) for (int y = cy - r; y <= cy + r; y++) for (int z = cz - r; z <= cz + r; z++) if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) == r && _cells.TryGetValue(Key(x, y, z), out var l2)) found.AddRange(l2); }
                }
                if (found.Count == 0) found = _all;
                return found.OrderBy(p => Dist2(p.P, v)).Take(count).ToList();
            }

            /// <summary>Up to 3 bones, weights adding up to 1, from the 4 nearest P+ points (closer ones count more).</summary>
            public List<(string Bone, float W)> Weights(Vector3 v)
            {
                var near = Nearest(v, 4);
                var sum = new Dictionary<string, double>();
                foreach (var p in near)
                {
                    double k = 1.0 / Math.Max(1e-6, Dist2(p.P, v));
                    foreach (var (bone, w) in p.Weights) { sum.TryGetValue(bone, out double s); sum[bone] = s + k * w; }
                }
                var top = sum.OrderByDescending(kv => kv.Value).Take(3).ToList();
                double total = top.Sum(kv => kv.Value);
                var r = top.Select(kv => (kv.Key, (float)(kv.Value / total))).Where(x => x.Item2 >= 0.08f).ToList();
                float t2 = r.Sum(x => x.Item2);
                r = r.Select(x => (x.Key, (float)Math.Round(x.Item2 / t2, 3))).ToList();
                if (r.Count > 1) { float rest = 1 - r.Skip(1).Sum(x => x.Item2); r[0] = (r[0].Key, rest); }
                return r;
            }
        }

        static float Dist2(Vector3 a, Vector3 b) { float x = a._x - b._x, y = a._y - b._y, z = a._z - b._z; return x * x + y * y + z * z; }

        // ----- pictures and the .dae written for the importer -----

        /// <summary>Each part's picture as a PNG the Wii can use (sides a power of two, at most 512), by texture name.</summary>
        static Dictionary<string, string> WritePictures(SwapMesh mesh, string dir, List<string> notes)
        {
            var r = new Dictionary<string, string>();
            int n = 0;
            foreach (var p in mesh.Parts)
            {
                if (p.TexturePath == null) continue;
                string name = "SwapTex" + (n++).ToString("00");
                try
                {
                    using (var src = LoadPicture(p.TexturePath))
                    {
                        int w = Pow2(src.Width), h = Pow2(src.Height);
                        string file = Path.Combine(dir, name + ".png");
                        using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                        {
                            using (var g = Graphics.FromImage(dst)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
                            dst.Save(file, ImageFormat.Png);
                        }
                        r[name] = file;
                        p.TexturePath = file;   // the part now uses this picture
                        p.Material = name;
                    }
                }
                catch (Exception e)
                {
                    notes.Add($"The picture {Path.GetFileName(p.TexturePath)} couldn't be read ({e.Message}); that part is plain grey.");
                    p.TexturePath = null;
                }
            }
            return r;
        }

        static Bitmap LoadPicture(string path)
        {
            using (var fs = File.OpenRead(path)) using (var img = Image.FromStream(fs)) return new Bitmap(img);
        }

        static int Pow2(int v) { int p = 8; while (p < v && p < 512) p *= 2; return p; }

        static bool HasAlpha(Bitmap b)
        {
            for (int y = 0; y < b.Height; y += 4) for (int x = 0; x < b.Width; x += 4) if (b.GetPixel(x, y).A < 250) return true;
            return false;
        }

        static string Num(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);

        static void WriteDae(SwapMesh mesh, Dictionary<string, string> pictures, string path)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">\n");
            sb.Append("<asset><unit meter=\"1\" name=\"meter\"/><up_axis>Y_UP</up_axis></asset>\n<library_images>\n");
            foreach (var kv in pictures) sb.Append($"<image id=\"{kv.Key}-image\" name=\"{kv.Key}\"><init_from>{Path.GetFileName(kv.Value)}</init_from></image>\n");
            sb.Append("</library_images>\n<library_effects>\n");
            var parts = mesh.Parts.Select((p, i) => (p, id: "M" + i)).ToList();
            foreach (var (p, id) in parts)
            {
                sb.Append($"<effect id=\"{id}-fx\"><profile_COMMON>");
                string tex = p.TexturePath != null ? pictures.FirstOrDefault(kv => kv.Value == p.TexturePath).Key : null;
                if (tex != null)
                    sb.Append($"<newparam sid=\"{id}-surface\"><surface type=\"2D\"><init_from>{tex}-image</init_from></surface></newparam>" +
                              $"<newparam sid=\"{id}-sampler\"><sampler2D><source>{id}-surface</source><wrap_s>WRAP</wrap_s><wrap_t>WRAP</wrap_t></sampler2D></newparam>");
                sb.Append("<technique sid=\"common\"><phong><diffuse>");
                sb.Append(tex != null ? $"<texture texture=\"{id}-sampler\" texcoord=\"UVSET0\"/>" : "<color>0.8 0.8 0.8 1</color>");
                sb.Append("</diffuse></phong></technique></profile_COMMON></effect>\n");
            }
            sb.Append("</library_effects>\n<library_materials>\n");
            foreach (var (p, id) in parts) sb.Append($"<material id=\"{id}\" name=\"{id}\"><instance_effect url=\"#{id}-fx\"/></material>\n");
            sb.Append("</library_materials>\n<library_geometries>\n");
            foreach (var (p, id) in parts)
            {
                // shared lists of positions, normals and texture points (corners refer to them), like most .dae files
                var pos = new List<Vector3>(); var posAt = new Dictionary<(float, float, float), int>();
                var nrm = new List<Vector3>(); var nrmAt = new Dictionary<(float, float, float), int>();
                var uvs = new List<Vector2>(); var uvAt = new Dictionary<(float, float), int>();
                var corners = new List<(int, int, int)>();
                int Add3(List<Vector3> list, Dictionary<(float, float, float), int> at, Vector3 v)
                {
                    var k = (v._x, v._y, v._z);
                    if (!at.TryGetValue(k, out int i)) { i = list.Count; list.Add(v); at[k] = i; }
                    return i;
                }
                for (int i = 0; i < p.P.Count; i++)
                {
                    var uv = new Vector2(p.UV[i]._x, 1 - p.UV[i]._y);
                    var uk = (uv._x, uv._y);
                    if (!uvAt.TryGetValue(uk, out int ui)) { ui = uvs.Count; uvs.Add(uv); uvAt[uk] = ui; }
                    corners.Add((Add3(pos, posAt, p.P[i]), Add3(nrm, nrmAt, p.N[i]), ui));
                }
                sb.Append($"<geometry id=\"{id}-mesh\" name=\"{id}\"><mesh>");
                sb.Append($"<source id=\"{id}-pos\"><float_array id=\"{id}-pos-a\" count=\"{pos.Count * 3}\">");
                foreach (var v in pos) sb.Append(Num(v._x)).Append(' ').Append(Num(v._y)).Append(' ').Append(Num(v._z)).Append(' ');
                sb.Append($"</float_array><technique_common><accessor source=\"#{id}-pos-a\" count=\"{pos.Count}\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source>");
                sb.Append($"<source id=\"{id}-nrm\"><float_array id=\"{id}-nrm-a\" count=\"{nrm.Count * 3}\">");
                foreach (var v in nrm) sb.Append(Num(v._x)).Append(' ').Append(Num(v._y)).Append(' ').Append(Num(v._z)).Append(' ');
                sb.Append($"</float_array><technique_common><accessor source=\"#{id}-nrm-a\" count=\"{nrm.Count}\" stride=\"3\"><param name=\"X\" type=\"float\"/><param name=\"Y\" type=\"float\"/><param name=\"Z\" type=\"float\"/></accessor></technique_common></source>");
                sb.Append($"<source id=\"{id}-uv\"><float_array id=\"{id}-uv-a\" count=\"{uvs.Count * 2}\">");
                foreach (var v in uvs) sb.Append(Num(v._x)).Append(' ').Append(Num(v._y)).Append(' ');
                sb.Append($"</float_array><technique_common><accessor source=\"#{id}-uv-a\" count=\"{uvs.Count}\" stride=\"2\"><param name=\"S\" type=\"float\"/><param name=\"T\" type=\"float\"/></accessor></technique_common></source>");
                sb.Append($"<vertices id=\"{id}-vtx\"><input semantic=\"POSITION\" source=\"#{id}-pos\"/></vertices>");
                sb.Append($"<triangles material=\"{id}\" count=\"{corners.Count / 3}\"><input semantic=\"VERTEX\" source=\"#{id}-vtx\" offset=\"0\"/><input semantic=\"NORMAL\" source=\"#{id}-nrm\" offset=\"1\"/><input semantic=\"TEXCOORD\" source=\"#{id}-uv\" offset=\"2\" set=\"0\"/><p>");
                foreach (var (a, b, c) in corners) sb.Append(a).Append(' ').Append(b).Append(' ').Append(c).Append(' ');
                sb.Append("</p></triangles></mesh></geometry>\n");
            }
            sb.Append("</library_geometries>\n<library_visual_scenes><visual_scene id=\"Scene\" name=\"Scene\">\n");
            sb.Append("<node id=\"SwapRoot\" name=\"SwapRoot\" sid=\"SwapRoot\" type=\"JOINT\"><matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix></node>\n");
            foreach (var (p, id) in parts)
                sb.Append($"<node id=\"{id}-node\" name=\"{id}\"><instance_geometry url=\"#{id}-mesh\"><bind_material><technique_common><instance_material symbol=\"{id}\" target=\"#{id}\"><bind_vertex_input semantic=\"UVSET0\" input_semantic=\"TEXCOORD\" input_set=\"0\"/></instance_material></technique_common></bind_material></instance_geometry></node>\n");
            sb.Append("</visual_scene></library_visual_scenes>\n<scene><instance_visual_scene url=\"#Scene\"/></scene>\n</COLLADA>\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static float Len(Vector3 v) => (float)Math.Sqrt(v._x * v._x + v._y * v._y + v._z * v._z);
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a._x - b._x, a._y - b._y, a._z - b._z);
        static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a._y * b._z - a._z * b._y, a._z * b._x - a._x * b._z, a._x * b._y - a._y * b._x);
        static Vector3 Normalize(Vector3 v) { float l = Len(v); return l < 1e-9f ? new Vector3(0, 1, 0) : new Vector3(v._x / l, v._y / l, v._z / l); }
    }
}
