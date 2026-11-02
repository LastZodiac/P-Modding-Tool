// A P+ costume as a rigged GLB for Blender: the skeleton (every bone, in its default pose), each shape the costume
// shows by default with its picture, and which bones every point follows (up to 4 each). Things the fighter only
// shows sometimes (items in hand, the other faces) aren't sent: the Bridge puts them back from the costume itself.
// Shapes that are all head (eyes, hair, face pieces) are named "PPlusFace_..." and are also put back exactly as
// they were, so they're in Blender only to look at.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using BrawlLib.Internal;
using BrawlLib.Modeling;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Graphics;
using BrawlLib.Wii.Models;
using SkinManager.Core;

namespace PPlusBlenderBridge
{
    static unsafe class RiggedExport
    {
        public const string FacePrefix = "PPlusFace_", PartPrefix = "PPlus_";

        sealed class Shape
        {
            public string Name, Material, TexName; public byte[] Png; public bool Cutout, SeeThrough;
            public List<Vector3> P = new List<Vector3>(), N = new List<Vector3>();
            public List<Vector2> UV = new List<Vector2>();
            public List<int[]> J = new List<int[]>(); public List<float[]> W = new List<float[]>();
            public List<int> Index = new List<int>();
        }

        /// <summary>A shape whose material layers pictures on different UV sets (like eyes: white, iris, shine, then the lids
        /// with holes) flattened into one picture on the first layer's UVs, so it looks right in Blender. Null if it doesn't apply.</summary>
        static byte[] Flatten(MDL0MaterialNode mat, Dictionary<string, TEX0Node> textures, GLPrimitive prim, PrimitiveManager mgr)
        {
            var refs = mat?.Children.OfType<MDL0MaterialRefNode>().ToList();
            if (refs == null || refs.Count < 2) return null;
            var layers = new List<(Bitmap bmp, IntPtr uv, MatWrapMode wu, MatWrapMode wv)>();
            try
            {
                foreach (var r in refs)
                {
                    int set = (int)r.Coordinates - (int)TexSourceRow.TexCoord0;
                    if (r.Projection != TexProjection.ST || set < 0 || set > 7 || r.TextureNode == null) return null;
                    if (!textures.TryGetValue(r.TextureNode.Name, out var t) || mgr._faceData[4 + set] == null) return null;
                    layers.Add((new Bitmap(t.GetImage(0)), (IntPtr)(Vector2*)mgr._faceData[4 + set].Address, r.UWrapMode, r.VWrapMode));
                }
                var lid = layers[0];
                int scale = Math.Max(1, Math.Min(4, 256 / Math.Max(1, Math.Max(lid.bmp.Width, lid.bmp.Height))));
                int W = lid.bmp.Width * scale, H = lid.bmp.Height * scale;
                var outBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) outBmp.SetPixel(x, y, lid.bmp.GetPixel(x / scale, y / scale));
                Color Sample(int li, float u, float v)
                {
                    var L = layers[li];
                    float Wrap(float a, MatWrapMode m) => m == MatWrapMode.Clamp ? Math.Max(0f, Math.Min(0.9999f, a))
                        : m == MatWrapMode.Mirror ? (((int)Math.Floor(a) & 1) == 0 ? a - (float)Math.Floor(a) : 1 - (a - (float)Math.Floor(a))) * 0.9999f
                        : (a - (float)Math.Floor(a));
                    return L.bmp.GetPixel((int)(Wrap(u, L.wu) * L.bmp.Width), (int)(Wrap(v, L.wv) * L.bmp.Height));
                }
                Color Over(Color under, Color top)
                {
                    float a = top.A / 255f;
                    return Color.FromArgb(255, (int)(top.R * a + under.R * (1 - a)), (int)(top.G * a + under.G * (1 - a)), (int)(top.B * a + under.B * (1 - a)));
                }
                var ind = prim._indices;
                bool any = false;
                for (int i = 0; i + 2 < ind.Length; i += 3)
                {
                    uint a0 = ind[i], a1 = ind[i + 1], a2 = ind[i + 2];
                    var lu = (Vector2*)lid.uv; var p0 = lu[a0]; var p1 = lu[a1]; var p2 = lu[a2];
                    float x0 = p0._x * W, y0 = p0._y * H, x1 = p1._x * W, y1 = p1._y * H, x2 = p2._x * W, y2 = p2._y * H;
                    float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                    if (Math.Abs(den) < 1e-8f) continue;
                    int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
                    int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), maxY = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
                    for (int py = minY; py <= maxY; py++)
                        for (int px = minX; px <= maxX; px++)
                        {
                            float cx = px + 0.5f, cy = py + 0.5f;
                            float b0 = ((y1 - y2) * (cx - x2) + (x2 - x1) * (cy - y2)) / den;
                            float b1 = ((y2 - y0) * (cx - x2) + (x0 - x2) * (cy - y2)) / den;
                            float b2 = 1 - b0 - b1;
                            const float e = -0.02f;
                            if (b0 < e || b1 < e || b2 < e) continue;
                            // bottom layer first (the last picture), the lids (the first picture) on top
                            Color c = Color.Black; bool first = true;
                            for (int li = layers.Count - 1; li >= 0; li--)
                            {
                                var L = layers[li];
                                var qu = (Vector2*)L.uv; Vector2 q0 = qu[a0], q1 = qu[a1], q2 = qu[a2];
                                var col = Sample(li, q0._x * b0 + q1._x * b1 + q2._x * b2, q0._y * b0 + q1._y * b1 + q2._y * b2);
                                c = first ? Color.FromArgb(255, col) : Over(c, col); first = false;
                            }
                            outBmp.SetPixel(px, py, c); any = true;
                        }
                }
                if (!any) { outBmp.Dispose(); return null; }
                using (outBmp) using (var ms = new MemoryStream()) { outBmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
            }
            finally { foreach (var l in layers) l.bmp.Dispose(); }
        }

        /// <summary>Which shapes of the costume the game shows by default (and so go to Blender), by object name.</summary>
        public static bool ShownByDefault(MDL0ObjectNode obj, HashSet<string> hidden)
        {
            var call = obj._drawCalls.FirstOrDefault(c => c.VisibilityBoneNode == null || c.VisibilityBoneNode.Visible && !hidden.Contains(c.VisibilityBoneNode.Name));
            if (call == null) return false;
            string vis = call.VisibilityBoneNode?.Name ?? "";
            return !(vis.IndexOf("Blink", StringComparison.OrdinalIgnoreCase) >= 0 || vis.IndexOf("Ouch", StringComparison.OrdinalIgnoreCase) >= 0 || vis.IndexOf("Half", StringComparison.OrdinalIgnoreCase) >= 0
                || vis.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The head bone and every bone under it (empty if there's no HeadN).</summary>
        public static HashSet<string> HeadBones(MDL0Node model)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var head = model._linker?.BoneCache?.OfType<MDL0BoneNode>().FirstOrDefault(b => b.Name == "HeadN");
            void Add(MDL0BoneNode b) { set.Add(b.Name); foreach (var c in b.Children.OfType<MDL0BoneNode>()) Add(c); }
            if (head != null) Add(head);
            return set;
        }

        /// <summary>How much of the shape follows the head (0..1), counting each point's weights.</summary>
        public static float HeadShare(MDL0ObjectNode obj, HashSet<string> head)
        {
            var vs = obj._manager?._vertices;
            if (vs == null || vs.Count == 0 || head.Count == 0) return 0;
            double h = 0, all = 0;
            foreach (var v in vs)
            {
                var node = v.GetMatrixNode();
                if (node == null) continue;
                foreach (var w in node.Weights)
                {
                    if (!(w.Bone is MDL0BoneNode b) || w.Weight <= 0) continue;
                    all += w.Weight; if (head.Contains(b.Name)) h += w.Weight;
                }
            }
            return all <= 0 ? 0 : (float)(h / all);
        }

        /// <summary>Writes the GLB; returns what it holds, in words.</summary>
        public static string ToGlb(byte[] costume, IEnumerable<int> hiddenBones, string path)
        {
            var shapes = new List<Shape>();
            string[] boneNames; int[] parents; double[][] world;
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault()
                    ?? throw new InvalidOperationException("This costume has no model.");
                model.ApplyCHR(null, 0);
                var bones = model._linker.BoneCache.OfType<MDL0BoneNode>().ToArray();
                var index = new Dictionary<MDL0BoneNode, int>();
                for (int i = 0; i < bones.Length; i++) index[bones[i]] = i;
                boneNames = bones.Select(b => b.Name).ToArray();
                parents = bones.Select(b => b.Parent is MDL0BoneNode p && index.ContainsKey(p) ? index[p] : -1).ToArray();
                world = bones.Select(b => Enumerable.Range(0, 16).Select(i => (double)b.BindMatrix[i]).ToArray()).ToArray();
                var hidden = new HashSet<string>((hiddenBones ?? new int[0]).Where(i => i >= 0 && i < bones.Length).Select(i => bones[i].Name));
                var head = HeadBones(model);
                var textures = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var pngs = new Dictionary<string, (byte[] png, bool alpha)>(StringComparer.OrdinalIgnoreCase);
                var field = typeof(PrimitiveManager).GetField("_triangles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>())
                {
                    if (!ShownByDefault(obj, hidden)) continue;
                    var call = obj._drawCalls.First(c => c.VisibilityBoneNode == null || c.VisibilityBoneNode.Visible && !hidden.Contains(c.VisibilityBoneNode.Name));
                    var mgr = obj._manager;
                    if (mgr?._vertices == null || mgr._faceData?[0] == null) continue;
                    var prim = field?.GetValue(mgr) as GLPrimitive;
                    if (prim == null) continue;
                    var mat = call.MaterialNode;
                    string tex = mat?.Children.OfType<MDL0MaterialRefNode>().FirstOrDefault(r => r.TextureNode != null)?.TextureNode?.Name;
                    bool face = HeadShare(obj, head) >= 0.8f;
                    var s = new Shape { Name = (face ? FacePrefix : PartPrefix) + obj.Name, Material = mat?.Name ?? obj.Name, SeeThrough = mat != null && mat.XLUMaterial };
                    if (tex != null && textures.TryGetValue(tex, out var t))
                    {
                        if (!pngs.TryGetValue(tex, out var pg))
                            using (var bmp = new Bitmap(t.GetImage(0)))
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, ImageFormat.Png);
                                bool alpha = false;
                                for (int y = 0; y < bmp.Height && !alpha; y += 2) for (int x = 0; x < bmp.Width; x += 2) if (bmp.GetPixel(x, y).A < 250) { alpha = true; break; }
                                pngs[tex] = pg = (ms.ToArray(), alpha);
                            }
                        s.Png = pg.png; s.Cutout = pg.alpha && !s.SeeThrough; s.TexName = tex;
                        // eyes: the game layers several pictures (white, iris, shine, lids); Blender gets them flattened into one
                        if (pg.alpha && !s.SeeThrough)
                            try
                            {
                                var flat = Flatten(mat, textures, prim, mgr);
                                if (flat != null) { s.Png = flat; s.Cutout = false; }
                            }
                            catch { }
                    }
                    ushort* idx = (ushort*)mgr._indices.Address;
                    Vector3* nrm = mgr._faceData[1] != null ? (Vector3*)mgr._faceData[1].Address : null;
                    Vector2* uv = mgr._faceData[4] != null ? (Vector2*)mgr._faceData[4].Address : null;
                    // one glTF vertex per (point, picture spot): shared corners stay joined in Blender
                    var seen = new Dictionary<(int, float, float), int>();
                    var ind = prim._indices;
                    for (int i = 0; i + 2 < ind.Length; i += 3)
                        for (int k = 0; k < 3; k++)
                        {
                            uint fp = ind[i + k];
                            int vi = idx[fp];
                            var u = uv != null ? uv[fp] : new Vector2(0, 0);
                            var key = (vi, u._x, u._y);
                            if (!seen.TryGetValue(key, out int at))
                            {
                                var vert = vi < mgr._vertices.Count ? mgr._vertices[vi] : null;
                                at = s.P.Count; seen[key] = at;
                                s.P.Add(vert?.WeightedPosition ?? new Vector3());
                                var n = nrm != null ? nrm[fp] : new Vector3(0, 1, 0);
                                var node = vert?.GetMatrixNode();
                                if (node is IBoneNode bone) n = bone.BindMatrix.GetRotationMatrix() * n;
                                float l = (float)Math.Sqrt(n._x * n._x + n._y * n._y + n._z * n._z);
                                s.N.Add(l > 1e-6f ? new Vector3(n._x / l, n._y / l, n._z / l) : new Vector3(0, 1, 0));
                                s.UV.Add(u);
                                // its bones: the 4 strongest, adding up to 1
                                var ws = (node?.Weights ?? new List<BoneWeight>()).Where(w => w.Bone is MDL0BoneNode b && index.ContainsKey(b) && w.Weight > 0)
                                    .Select(w => (I: index[(MDL0BoneNode)w.Bone], W: w.Weight)).OrderByDescending(w => w.W).Take(4).ToList();
                                if (ws.Count == 0) ws.Add((0, 1f));
                                float sum = ws.Sum(w => w.W);
                                var j = new int[4]; var wt = new float[4];
                                for (int q = 0; q < ws.Count; q++) { j[q] = ws[q].I; wt[q] = ws[q].W / sum; }
                                s.J.Add(j); s.W.Add(wt);
                            }
                            s.Index.Add(at);
                        }
                    if (s.Index.Count > 0) shapes.Add(s);
                }
            }
            if (shapes.Count == 0) throw new InvalidOperationException("No shapes were found in this costume's model.");
            File.WriteAllBytes(path, Write(shapes, boneNames, parents, world));
            int faces = shapes.Count(s => s.Name.StartsWith(FacePrefix));
            return $"{boneNames.Length} bones, {shapes.Count} shapes ({faces} of them face/hair pieces), {shapes.Sum(s => s.Index.Count / 3):N0} triangles";
        }

        // ----- 4x4 matrices: column-major, like BrawlLib's and glTF's (translation in 12..14) -----
        static double[] Mul(double[] a, double[] b)
        {
            var r = new double[16];
            for (int c = 0; c < 4; c++) for (int row = 0; row < 4; row++) { double v = 0; for (int k = 0; k < 4; k++) v += a[k * 4 + row] * b[c * 4 + k]; r[c * 4 + row] = v; }
            return r;
        }
        static double[] Inverse(double[] m)
        {
            var inv = new double[16];
            inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
            inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
            inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
            inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
            inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
            inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
            inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
            inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
            inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
            inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
            inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
            inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
            inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
            inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
            inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
            inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];
            double det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
            if (Math.Abs(det) < 1e-12) return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            for (int i = 0; i < 16; i++) inv[i] /= det;
            return inv;
        }

        static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static byte[] Write(List<Shape> shapes, string[] boneNames, int[] parents, double[][] world)
        {
            var bin = new MemoryStream(); var w = new BinaryWriter(bin);
            var views = new List<string>(); var accessors = new List<string>(); var images = new List<string>(); var texturesJ = new List<string>(); var materials = new List<string>(); var meshes = new List<string>();
            void Pad() { while (bin.Length % 4 != 0) w.Write((byte)0); }
            int View(int offset, int length, int? target) { views.Add($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{length}{(target != null ? $",\"target\":{target}" : "")}}}"); return views.Count - 1; }
            int Acc(string json) { accessors.Add(json); return accessors.Count - 1; }

            // bones: nodes 0..n-1, each placed relative to its parent
            var nodes = new List<string>();
            for (int i = 0; i < boneNames.Length; i++)
            {
                var local = parents[i] >= 0 ? Mul(Inverse(world[parents[i]]), world[i]) : world[i];
                var kids = Enumerable.Range(0, boneNames.Length).Where(k => parents[k] == i).ToList();
                nodes.Add($"{{\"name\":{Q(boneNames[i])},\"matrix\":[{string.Join(",", local.Select(F))}]{(kids.Count > 0 ? ",\"children\":[" + string.Join(",", kids) + "]" : "")}}}");
            }
            // inverse bind matrices
            Pad(); int ibmAt = (int)bin.Length;
            foreach (var m in world) foreach (var v in Inverse(m)) w.Write((float)v);
            int ibm = Acc($"{{\"bufferView\":{View(ibmAt, boneNames.Length * 64, null)},\"componentType\":5126,\"count\":{boneNames.Length},\"type\":\"MAT4\"}}");

            var pngIndex = new Dictionary<byte[], int>();
            var meshNodes = new List<int>();
            foreach (var s in shapes)
            {
                int n = s.P.Count;
                Pad(); int at = (int)bin.Length;
                float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
                foreach (var v in s.P) { w.Write(v._x); w.Write(v._y); w.Write(v._z); x0 = Math.Min(x0, v._x); y0 = Math.Min(y0, v._y); z0 = Math.Min(z0, v._z); x1 = Math.Max(x1, v._x); y1 = Math.Max(y1, v._y); z1 = Math.Max(z1, v._z); }
                int pa = Acc($"{{\"bufferView\":{View(at, n * 12, 34962)},\"componentType\":5126,\"count\":{n},\"type\":\"VEC3\",\"min\":[{F(x0)},{F(y0)},{F(z0)}],\"max\":[{F(x1)},{F(y1)},{F(z1)}]}}");
                Pad(); at = (int)bin.Length; foreach (var v in s.N) { w.Write(v._x); w.Write(v._y); w.Write(v._z); }
                int na = Acc($"{{\"bufferView\":{View(at, n * 12, 34962)},\"componentType\":5126,\"count\":{n},\"type\":\"VEC3\"}}");
                Pad(); at = (int)bin.Length; foreach (var v in s.UV) { w.Write(v._x); w.Write(v._y); }
                int ta = Acc($"{{\"bufferView\":{View(at, n * 8, 34962)},\"componentType\":5126,\"count\":{n},\"type\":\"VEC2\"}}");
                Pad(); at = (int)bin.Length; foreach (var j in s.J) foreach (var q in j) w.Write((ushort)q);
                int ja = Acc($"{{\"bufferView\":{View(at, n * 8, 34962)},\"componentType\":5123,\"count\":{n},\"type\":\"VEC4\"}}");
                Pad(); at = (int)bin.Length; foreach (var wt in s.W) foreach (var q in wt) w.Write(q);
                int wa = Acc($"{{\"bufferView\":{View(at, n * 16, 34962)},\"componentType\":5126,\"count\":{n},\"type\":\"VEC4\"}}");
                Pad(); at = (int)bin.Length; foreach (var i in s.Index) w.Write((uint)i);
                int ia = Acc($"{{\"bufferView\":{View(at, s.Index.Count * 4, 34963)},\"componentType\":5125,\"count\":{s.Index.Count},\"type\":\"SCALAR\"}}");
                string texPart = "";
                if (s.Png != null)
                {
                    if (!pngIndex.TryGetValue(s.Png, out int img))
                    {
                        Pad(); at = (int)bin.Length; w.Write(s.Png);
                        images.Add($"{{\"bufferView\":{View(at, s.Png.Length, null)},\"mimeType\":\"image/png\",\"name\":{Q(s.TexName)}}}");
                        texturesJ.Add($"{{\"source\":{images.Count - 1},\"sampler\":0}}");
                        img = texturesJ.Count - 1; pngIndex[s.Png] = img;
                    }
                    texPart = $",\"baseColorTexture\":{{\"index\":{img}}}";
                }
                string mode = s.SeeThrough ? ",\"alphaMode\":\"BLEND\"" : s.Cutout ? ",\"alphaMode\":\"MASK\",\"alphaCutoff\":0.45" : "";
                materials.Add($"{{\"name\":{Q(s.Material)},\"doubleSided\":true,\"pbrMetallicRoughness\":{{\"metallicFactor\":0,\"roughnessFactor\":1{texPart}}}{mode}}}");
                meshes.Add($"{{\"name\":{Q(s.Name)},\"primitives\":[{{\"attributes\":{{\"POSITION\":{pa},\"NORMAL\":{na},\"TEXCOORD_0\":{ta},\"JOINTS_0\":{ja},\"WEIGHTS_0\":{wa}}},\"indices\":{ia},\"material\":{materials.Count - 1}}}]}}");
                nodes.Add($"{{\"name\":{Q(s.Name)},\"mesh\":{meshes.Count - 1},\"skin\":0}}");
                meshNodes.Add(nodes.Count - 1);
            }
            Pad();
            var roots = Enumerable.Range(0, boneNames.Length).Where(i => parents[i] < 0).ToList();
            // the skeleton sits under one "P+ skeleton" node (Blender makes it the armature)
            nodes.Add($"{{\"name\":\"P+ skeleton\",\"children\":[{string.Join(",", roots)}]}}");
            int arm = nodes.Count - 1;
            string json = "{\"asset\":{\"version\":\"2.0\",\"generator\":\"P+ Blender Bridge\"},\"scene\":0," +
                $"\"scenes\":[{{\"nodes\":[{string.Join(",", new[] { arm }.Concat(meshNodes))}]}}]," +
                $"\"nodes\":[{string.Join(",", nodes)}]," +
                $"\"skins\":[{{\"name\":\"P+ skeleton\",\"inverseBindMatrices\":{ibm},\"skeleton\":{arm},\"joints\":[{string.Join(",", Enumerable.Range(0, boneNames.Length))}]}}]," +
                $"\"meshes\":[{string.Join(",", meshes)}],\"materials\":[{string.Join(",", materials)}]," +
                (texturesJ.Count > 0 ? $"\"textures\":[{string.Join(",", texturesJ)}],\"images\":[{string.Join(",", images)}],\"samplers\":[{{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}}]," : "") +
                $"\"accessors\":[{string.Join(",", accessors)}],\"bufferViews\":[{string.Join(",", views)}],\"buffers\":[{{\"byteLength\":{bin.Length}}}]}}";
            var jb = Encoding.UTF8.GetBytes(json);
            int jlen = (jb.Length + 3) / 4 * 4;
            var outS = new MemoryStream(); var o = new BinaryWriter(outS);
            o.Write(0x46546C67u); o.Write(2u); o.Write((uint)(12 + 8 + jlen + 8 + bin.Length));
            o.Write((uint)jlen); o.Write(0x4E4F534Au); o.Write(jb); for (int i = jb.Length; i < jlen; i++) o.Write((byte)' ');
            o.Write((uint)bin.Length); o.Write(0x004E4942u); o.Write(bin.ToArray());
            return outS.ToArray();
        }
    }
}
