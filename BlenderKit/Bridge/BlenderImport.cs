// Reading a GLB saved from Blender with the P+ Kit add-on: every shape (placed as the file places it), its picture,
// and the bones each point follows (by bone name, from the file's skin). Face/hair pieces the Bridge sent only to look
// at ("PPlusFace_...") can be left out: the costume's own ones are put back as they were.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using BrawlLib.Internal;
using SkinManager.Core;

namespace PPlusBlenderBridge
{
    sealed class FromBlender
    {
        public SwapMesh Mesh = new SwapMesh();
        public Dictionary<(long, long, long), List<(string Bone, float W)>> Weights = new Dictionary<(long, long, long), List<(string, float)>>();
        public List<string> Shapes = new List<string>(), Skipped = new List<string>(), UnriggedShapes = new List<string>();
        public int Unrigged;   // points with no bones of their own (they get the nearest P+ point's)
        public Dictionary<SwapPart, string> PictureName = new Dictionary<SwapPart, string>();   // the costume picture a part shows (its name, from the Bridge)
        public const float Cell = 0.001f;
        public static (long, long, long) Key(Vector3 v) => ((long)Math.Round(v._x / Cell), (long)Math.Round(v._y / Cell), (long)Math.Round(v._z / Cell));

        /// <summary>The bones a point follows (null if the file didn't say), by where it is.</summary>
        public List<(string Bone, float W)> At(Vector3 v)
        {
            if (Weights.TryGetValue(Key(v), out var w)) return w;
            // rounding: the neighbours
            var (x, y, z) = Key(v);
            for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) for (long dz = -1; dz <= 1; dz++)
                if (Weights.TryGetValue((x + dx, y + dy, z + dz), out w)) return w;
            return null;
        }

        public static FromBlender Read(string path, bool skipFaces)
        {
            var r = new FromBlender();
            byte[] file = File.ReadAllBytes(path);
            string json = null; byte[] bin = null;
            if (file.Length >= 12 && BitConverter.ToUInt32(file, 0) == 0x46546C67)
            {
                int at = 12;
                while (at + 8 <= file.Length)
                {
                    int len = BitConverter.ToInt32(file, at); uint type = BitConverter.ToUInt32(file, at + 4);
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(file, at + 8, len);
                    else if (type == 0x004E4942) { bin = new byte[len]; Buffer.BlockCopy(file, at + 8, bin, 0, len); }
                    at += 8 + len;
                }
            }
            else json = Encoding.UTF8.GetString(file);
            if (json == null) throw new InvalidDataException("That file isn't a GLB model.");
            var g = Json.Parse(json) as Dictionary<string, object> ?? throw new InvalidDataException("That file isn't a GLB model.");
            string dir = Path.GetDirectoryName(path);
            var buffers = new List<byte[]>();
            foreach (var b in Json.Arr(g, "buffers") ?? new List<object>())
            {
                string uri = Json.Str(b, "uri");
                if (uri == null) buffers.Add(bin);
                else if (uri.StartsWith("data:")) buffers.Add(Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1)));
                else buffers.Add(File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri))));
            }
            var views = Json.Arr(g, "bufferViews") ?? new List<object>();
            var accessors = Json.Arr(g, "accessors") ?? new List<object>();
            float[][] Read(int a)
            {
                var acc = accessors[a];
                int count = (int)Json.Num(acc, "count");
                int comps = (Json.Str(acc, "type") ?? "SCALAR") switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
                int ct = (int)Json.Num(acc, "componentType");
                int size = ct == 5126 || ct == 5125 ? 4 : ct == 5123 || ct == 5122 ? 2 : 1;
                bool norm = Json.Bool(acc, "normalized");
                var res = new float[count][];
                if (!Has(acc, "bufferView")) { for (int i = 0; i < count; i++) res[i] = new float[comps]; return res; }
                var bv = views[(int)Json.Num(acc, "bufferView")];
                var data = buffers[(int)Json.Num(bv, "buffer")];
                int stride = (int)Json.Num(bv, "byteStride"); if (stride == 0) stride = size * comps;
                int start = (int)Json.Num(bv, "byteOffset") + (int)Json.Num(acc, "byteOffset");
                for (int i = 0; i < count; i++)
                {
                    var e = new float[comps];
                    for (int c = 0; c < comps; c++)
                    {
                        int o = start + i * stride + c * size;
                        switch (ct)
                        {
                            case 5126: e[c] = BitConverter.ToSingle(data, o); break;
                            case 5125: e[c] = BitConverter.ToUInt32(data, o); break;
                            case 5123: e[c] = norm ? BitConverter.ToUInt16(data, o) / 65535f : BitConverter.ToUInt16(data, o); break;
                            case 5121: e[c] = norm ? data[o] / 255f : data[o]; break;
                            case 5122: e[c] = norm ? Math.Max(BitConverter.ToInt16(data, o) / 32767f, -1f) : BitConverter.ToInt16(data, o); break;
                            case 5120: e[c] = norm ? Math.Max((sbyte)data[o] / 127f, -1f) : (sbyte)data[o]; break;
                        }
                    }
                    res[i] = e;
                }
                return res;
            }

            // pictures
            string pics = Path.Combine(Path.GetTempPath(), "PPlusBlenderBridge", "in-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pics);
            var images = Json.Arr(g, "images") ?? new List<object>();
            var textures = Json.Arr(g, "textures") ?? new List<object>();
            var materials = Json.Arr(g, "materials") ?? new List<object>();
            var saved = new Dictionary<int, string>();
            string Image(int i)
            {
                if (saved.TryGetValue(i, out var have)) return have;
                string result = null;
                try
                {
                    var im = images[i]; byte[] bytes; string uri = Json.Str(im, "uri");
                    if (uri != null && uri.StartsWith("data:")) bytes = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                    else if (uri != null) bytes = File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri)));
                    else
                    {
                        var bv = views[(int)Json.Num(im, "bufferView")];
                        int len = (int)Json.Num(bv, "byteLength"); bytes = new byte[len];
                        Buffer.BlockCopy(buffers[(int)Json.Num(bv, "buffer")], (int)Json.Num(bv, "byteOffset"), bytes, 0, len);
                    }
                    using (var ms = new MemoryStream(bytes)) using (var img = System.Drawing.Image.FromStream(ms)) using (var bmp = new Bitmap(img))
                    { result = Path.Combine(pics, "picture" + i + ".png"); bmp.Save(result, ImageFormat.Png); }
                }
                catch { }
                return saved[i] = result;
            }
            var parts = new Dictionary<string, SwapPart>();
            // the picture a material shows (-1: none)
            int ImageOf(int m)
            {
                if (m < 0 || m >= materials.Count) return -1;
                var t0 = Json.Obj(Json.Obj(materials[m], "pbrMetallicRoughness"), "baseColorTexture");
                if (t0 == null) return -1;
                int ti = (int)Json.Num(t0, "index");
                return ti < textures.Count && Has(textures[ti], "source") ? (int)Json.Num(textures[ti], "source") : -1;
            }
            SwapPart PartFor(int m)
            {
                // shapes showing the same picture share one part (and one picture in the costume): Blender gives every
                // shape its own material, which would store the same picture again for each
                int img = ImageOf(m);
                string key = img >= 0 ? "img" + img : "mat" + m;
                if (parts.TryGetValue(key, out var p)) return p;
                p = new SwapPart { Material = m >= 0 && m < materials.Count ? (Json.Str(materials[m], "name") ?? "material" + m) : "default" };
                if (m >= 0 && m < materials.Count)
                {
                    var pbr = Json.Obj(materials[m], "pbrMetallicRoughness");
                    var tex = Json.Obj(pbr, "baseColorTexture");
                    if (tex != null)
                    {
                        int t = (int)Json.Num(tex, "index");
                        if (t < textures.Count && Has(textures[t], "source"))
                        {
                            int src = (int)Json.Num(textures[t], "source");
                            p.TexturePath = Image(src);
                            string nm = src < images.Count ? Json.Str(images[src], "name") : null;
                            if (!string.IsNullOrEmpty(nm)) r.PictureName[p] = nm;
                        }
                    }
                    if (p.TexturePath == null)
                    {
                        var f = Json.Arr(pbr, "baseColorFactor");
                        int C(int k) => f != null && f.Count > k ? Math.Max(0, Math.Min(255, (int)Math.Round(255 * Math.Pow(Convert.ToDouble(f[k]), 1 / 2.2)))) : 200;
                        string fp = Path.Combine(pics, "colour" + m + ".png");
                        using (var b = new Bitmap(4, 4)) { using (var gr = Graphics.FromImage(b)) gr.Clear(Color.FromArgb(C(0), C(1), C(2))); b.Save(fp, ImageFormat.Png); }
                        p.TexturePath = fp;
                    }
                }
                r.Mesh.Parts.Add(p);
                return parts[key] = p;
            }

            var nodes = Json.Arr(g, "nodes") ?? new List<object>();
            var meshes = Json.Arr(g, "meshes") ?? new List<object>();
            var skins = Json.Arr(g, "skins") ?? new List<object>();
            string NodeName(int i) => i >= 0 && i < nodes.Count ? Json.Str(nodes[i], "name") : null;
            var scenes = Json.Arr(g, "scenes");
            List<int> roots = scenes != null && scenes.Count > 0
                ? (Json.Arr(scenes[Has(g, "scene") ? (int)Json.Num(g, "scene") : 0], "nodes") ?? new List<object>()).Select(x => Convert.ToInt32(x)).ToList()
                : Enumerable.Range(0, nodes.Count).ToList();
            void Visit(int n, double[] parent, int depth)
            {
                if (n < 0 || n >= nodes.Count || depth > 64) return;
                var node = nodes[n];
                var world = Mul(parent, Local(node));
                string name = Json.Str(node, "name") ?? "shape";
                if (Has(node, "mesh"))
                {
                    var meshObj = meshes[(int)Json.Num(node, "mesh")];
                    string shapeName = Json.Str(meshObj, "name") ?? name;
                    bool face = name.StartsWith(RiggedExport.FacePrefix) || shapeName.StartsWith(RiggedExport.FacePrefix);
                    if ((skipFaces && face) || name.StartsWith("PPlusSource")) r.Skipped.Add(name);
                    else
                    {
                        r.Shapes.Add(name);
                        List<string> joints = null;
                        if (Has(node, "skin")) joints = (Json.Arr(skins[(int)Json.Num(node, "skin")], "joints") ?? new List<object>()).Select(x => NodeName(Convert.ToInt32(x))).ToList();
                        if (joints == null || joints.Count == 0) r.UnriggedShapes.Add(name);
                        foreach (var prim in Json.Arr(meshObj, "primitives") ?? new List<object>())
                        {
                            int mode = Has(prim, "mode") ? (int)Json.Num(prim, "mode") : 4;
                            if (mode != 4) continue;
                            var attr = Json.Obj(prim, "attributes");
                            if (!Has(attr, "POSITION")) continue;
                            var pos = Read((int)Json.Num(attr, "POSITION"));
                            var nor = Has(attr, "NORMAL") ? Read((int)Json.Num(attr, "NORMAL")) : null;
                            var uv = Has(attr, "TEXCOORD_0") ? Read((int)Json.Num(attr, "TEXCOORD_0")) : null;
                            var jj = Has(attr, "JOINTS_0") ? Read((int)Json.Num(attr, "JOINTS_0")) : null;
                            var ww = Has(attr, "WEIGHTS_0") ? Read((int)Json.Num(attr, "WEIGHTS_0")) : null;
                            var jj1 = Has(attr, "JOINTS_1") ? Read((int)Json.Num(attr, "JOINTS_1")) : null;
                            var ww1 = Has(attr, "WEIGHTS_1") ? Read((int)Json.Num(attr, "WEIGHTS_1")) : null;
                            var idx = Has(prim, "indices") ? Read((int)Json.Num(prim, "indices")).Select(x => (int)x[0]).ToList() : Enumerable.Range(0, pos.Length).ToList();
                            var part = PartFor(Has(prim, "material") ? (int)Json.Num(prim, "material") : -1);
                            foreach (int k in idx)
                            {
                                var p = Point(world, pos[k]);
                                part.P.Add(p);
                                part.N.Add(nor != null ? Direction(world, nor[k]) : new Vector3());
                                part.UV.Add(uv != null ? new Vector2(uv[k][0], uv[k][1]) : new Vector2());
                                var list = new List<(string, float)>();
                                if (joints != null && jj != null && ww != null)
                                {
                                    for (int q = 0; q < 4; q++) { int ji = (int)jj[k][q]; if (ww[k][q] > 0.001f && ji < joints.Count && joints[ji] != null) list.Add((joints[ji], ww[k][q])); }
                                    if (jj1 != null && ww1 != null)
                                        for (int q = 0; q < 4; q++) { int ji = (int)jj1[k][q]; if (ww1[k][q] > 0.001f && ji < joints.Count && joints[ji] != null) list.Add((joints[ji], ww1[k][q])); }
                                }
                                // the same bone twice (Blender can split them): added up
                                list = list.GroupBy(x => x.Item1).Select(x => (x.Key, x.Sum(y => y.Item2))).ToList();
                                if (list.Count == 0) r.Unrigged++;
                                else r.Weights[Key(p)] = list;
                            }
                        }
                    }
                }
                foreach (var c in Json.Arr(node, "children") ?? new List<object>()) Visit(Convert.ToInt32(c), world, depth + 1);
            }
            foreach (var root in roots) Visit(root, new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }, 0);
            r.Mesh.Parts.RemoveAll(p => p.Triangles == 0);
            return r;
        }

        static bool Has(object o, string key) => o is Dictionary<string, object> d && d.ContainsKey(key) && d[key] != null;
        static double[] Local(object node)
        {
            var m = Json.Arr(node, "matrix");
            if (m != null && m.Count == 16) return m.Select(Convert.ToDouble).ToArray();
            var t = (Json.Arr(node, "translation") ?? new List<object> { 0.0, 0.0, 0.0 }).Select(Convert.ToDouble).ToArray();
            var q = (Json.Arr(node, "rotation") ?? new List<object> { 0.0, 0.0, 0.0, 1.0 }).Select(Convert.ToDouble).ToArray();
            var s = (Json.Arr(node, "scale") ?? new List<object> { 1.0, 1.0, 1.0 }).Select(Convert.ToDouble).ToArray();
            double x = q[0], y = q[1], z = q[2], w = q[3];
            return new[]
            {
                (1 - 2 * (y * y + z * z)) * s[0], (2 * (x * y + z * w)) * s[0], (2 * (x * z - y * w)) * s[0], 0,
                (2 * (x * y - z * w)) * s[1], (1 - 2 * (x * x + z * z)) * s[1], (2 * (y * z + x * w)) * s[1], 0,
                (2 * (x * z + y * w)) * s[2], (2 * (y * z - x * w)) * s[2], (1 - 2 * (x * x + y * y)) * s[2], 0,
                t[0], t[1], t[2], 1,
            };
        }
        static double[] Mul(double[] a, double[] b)
        {
            var res = new double[16];
            for (int c = 0; c < 4; c++) for (int row = 0; row < 4; row++) { double v = 0; for (int k = 0; k < 4; k++) v += a[k * 4 + row] * b[c * 4 + k]; res[c * 4 + row] = v; }
            return res;
        }
        static Vector3 Point(double[] m, float[] p) => new Vector3(
            (float)(m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12]),
            (float)(m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13]),
            (float)(m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14]));
        static Vector3 Direction(double[] m, float[] n)
        {
            double x = m[0] * n[0] + m[4] * n[1] + m[8] * n[2], y = m[1] * n[0] + m[5] * n[1] + m[9] * n[2], z = m[2] * n[0] + m[6] * n[1] + m[10] * n[2];
            double l = Math.Sqrt(x * x + y * y + z * z);
            return l < 1e-9 ? new Vector3() : new Vector3((float)(x / l), (float)(y / l), (float)(z / l));
        }
    }
}
