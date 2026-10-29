// Reading glTF models (.glb, and .gltf with its files): what Tripo3D, Source 2 Viewer, Blender and most sites
// export. The scene's meshes are placed as the scene places them, each material becomes a part, and its base
// colour picture (packed inside a .glb) is saved next to the app's temporary files so the swapper can use it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using BrawlLib.Internal;

namespace SkinManager.Core
{
    public static partial class ModelSwap
    {
        static SwapMesh LoadGltf(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            string json; byte[] bin = null;
            if (file.Length >= 12 && BitConverter.ToUInt32(file, 0) == 0x46546C67)   // "glTF": binary
            {
                int at = 12;
                json = null;
                while (at + 8 <= file.Length)
                {
                    int len = BitConverter.ToInt32(file, at); uint type = BitConverter.ToUInt32(file, at + 4);
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(file, at + 8, len);
                    else if (type == 0x004E4942) { bin = new byte[len]; Buffer.BlockCopy(file, at + 8, bin, 0, len); }
                    at += 8 + len;
                }
                if (json == null) throw new InvalidDataException("That .glb file has no model description inside.");
            }
            else json = Encoding.UTF8.GetString(file);

            var g = Json.Parse(json) as Dictionary<string, object> ?? throw new InvalidDataException("That file isn't a glTF model.");
            var required = (Json.Arr(g, "extensionsRequired") ?? new List<object>()).Select(x => x as string).ToList();
            if (required.Any(x => x == "KHR_draco_mesh_compression" || x == "EXT_meshopt_compression"))
                throw new InvalidOperationException("This model is saved compressed (Draco/meshopt), which the swapper can't read. Download it again as OBJ, or as GLB without compression.");
            string dir = Path.GetDirectoryName(path);

            // buffers
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
            byte[] ViewBytes(int v, out int offset, out int stride)
            {
                var bv = views[v];
                offset = (int)Json.Num(bv, "byteOffset"); stride = (int)Json.Num(bv, "byteStride");
                return buffers[(int)Json.Num(bv, "buffer")];
            }
            // an accessor's values as floats (count x components), normalized integers scaled to 0..1
            float[][] Read(int a)
            {
                var acc = accessors[a];
                int count = (int)Json.Num(acc, "count");
                int comps = (Json.Str(acc, "type") ?? "SCALAR") switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
                int ct = (int)Json.Num(acc, "componentType");
                int size = ct == 5126 || ct == 5125 ? 4 : ct == 5123 || ct == 5122 ? 2 : 1;
                bool norm = Json.Bool(acc, "normalized");
                var r = new float[count][];
                if (!Has(acc, "bufferView")) { for (int i = 0; i < count; i++) r[i] = new float[comps]; return r; }   // all zeros (sparse not read)
                var data = ViewBytes((int)Json.Num(acc, "bufferView"), out int vo, out int stride);
                int start = vo + (int)Json.Num(acc, "byteOffset");
                if (stride == 0) stride = size * comps;
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
                            case 5122: e[c] = norm ? Math.Max(BitConverter.ToInt16(data, o) / 32767f, -1f) : BitConverter.ToInt16(data, o); break;
                            case 5121: e[c] = norm ? data[o] / 255f : data[o]; break;
                            case 5120: e[c] = norm ? Math.Max((sbyte)data[o] / 127f, -1f) : (sbyte)data[o]; break;
                        }
                    }
                    r[i] = e;
                }
                return r;
            }

            // pictures: each material's base colour picture (or a small plain-colour picture)
            string pics = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "model-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pics);
            var images = Json.Arr(g, "images") ?? new List<object>();
            var textures = Json.Arr(g, "textures") ?? new List<object>();
            var savedImages = new Dictionary<int, string>();
            string Image(int i)
            {
                if (savedImages.TryGetValue(i, out var have)) return have;
                string result = null;
                try
                {
                    var im = images[i];
                    byte[] bytes;
                    string uri = Json.Str(im, "uri");
                    if (uri != null && uri.StartsWith("data:")) bytes = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                    else if (uri != null) bytes = File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri)));
                    else { var data = ViewBytes((int)Json.Num(im, "bufferView"), out int o, out _); int len = (int)Json.Num(views[(int)Json.Num(im, "bufferView")], "byteLength"); bytes = new byte[len]; Buffer.BlockCopy(data, o, bytes, 0, len); }
                    // saved as PNG (also checks the picture can be read)
                    using (var ms = new MemoryStream(bytes)) using (var img = System.Drawing.Image.FromStream(ms)) using (var bmp = new Bitmap(img))
                    {
                        result = Path.Combine(pics, "picture" + i + ".png");
                        bmp.Save(result, ImageFormat.Png);
                    }
                }
                catch { result = null; }
                return savedImages[i] = result;
            }
            var materials = Json.Arr(g, "materials") ?? new List<object>();
            var mesh = new SwapMesh();
            var partOf = new Dictionary<int, SwapPart>();
            SwapPart PartFor(int m)
            {
                if (partOf.TryGetValue(m, out var p)) return p;
                p = new SwapPart { Material = m >= 0 && m < materials.Count ? (Json.Str(materials[m], "name") ?? "material" + m) : "default" };
                if (m >= 0 && m < materials.Count)
                {
                    var pbr = Json.Obj(materials[m], "pbrMetallicRoughness");
                    var tex = Json.Obj(pbr, "baseColorTexture");
                    if (tex != null)
                    {
                        int t = (int)Json.Num(tex, "index");
                        if (t < textures.Count)
                        {
                            var tx = textures[t];
                            int src = Has(tx, "source") ? (int)Json.Num(tx, "source") : -1;
                            if (src < 0) { var ext = Json.Obj(Json.Obj(tx, "extensions"), "EXT_texture_webp"); if (ext != null) src = (int)Json.Num(ext, "source"); }
                            if (src >= 0 && src < images.Count) p.TexturePath = Image(src);
                            if (p.TexturePath == null) mesh.Notes.Add("A picture in the model couldn't be read (it may be WebP); that part shows its plain colour.");
                        }
                    }
                    if (p.TexturePath == null)
                    {
                        // no picture: its colour as a tiny picture, so it doesn't come out white
                        var f = Json.Arr(pbr, "baseColorFactor");
                        if (f != null && f.Count >= 3)
                        {
                            int C(int k) => Math.Max(0, Math.Min(255, (int)Math.Round(255 * Math.Pow(Convert.ToDouble(f[k]), 1 / 2.2))));
                            string fp = Path.Combine(pics, "colour" + m + ".png");
                            using (var b = new Bitmap(4, 4)) { using (var gr = Graphics.FromImage(b)) gr.Clear(Color.FromArgb(C(0), C(1), C(2))); b.Save(fp, ImageFormat.Png); }
                            p.TexturePath = fp;
                        }
                    }
                }
                mesh.Parts.Add(p);
                return partOf[m] = p;
            }

            // the scene: every node with a mesh, placed by its node and its parents
            var nodes = Json.Arr(g, "nodes") ?? new List<object>();
            var meshes = Json.Arr(g, "meshes") ?? new List<object>();
            var scenes = Json.Arr(g, "scenes");
            List<int> roots;
            if (scenes != null && scenes.Count > 0)
                roots = (Json.Arr(scenes[Has(g, "scene") ? (int)Json.Num(g, "scene") : 0], "nodes") ?? new List<object>()).Select(x => Convert.ToInt32(x)).ToList();
            else
            {
                var children = new HashSet<int>(nodes.SelectMany(n => (Json.Arr(n, "children") ?? new List<object>()).Select(x => Convert.ToInt32(x))));
                roots = Enumerable.Range(0, nodes.Count).Where(i => !children.Contains(i)).ToList();
            }
            void Visit(int n, double[] parent, int depth)
            {
                if (n < 0 || n >= nodes.Count || depth > 64) return;
                var node = nodes[n];
                var world = Mul(parent, Local(node));
                if (Has(node, "mesh"))
                {
                    var normalMatrix = world;
                    foreach (var prim in Json.Arr(meshes[(int)Json.Num(node, "mesh")], "primitives") ?? new List<object>())
                    {
                        int mode = Has(prim, "mode") ? (int)Json.Num(prim, "mode") : 4;
                        if (mode != 4 && mode != 5 && mode != 6) continue;   // points and lines: nothing to draw
                        var attr = Json.Obj(prim, "attributes");
                        if (!Has(attr, "POSITION")) continue;
                        var pos = Read((int)Json.Num(attr, "POSITION"));
                        var nor = Has(attr, "NORMAL") ? Read((int)Json.Num(attr, "NORMAL")) : null;
                        var uv = Has(attr, "TEXCOORD_0") ? Read((int)Json.Num(attr, "TEXCOORD_0")) : null;
                        var idx = Has(prim, "indices") ? Read((int)Json.Num(prim, "indices")).Select(x => (int)x[0]).ToList() : Enumerable.Range(0, pos.Length).ToList();
                        var tris = new List<int>();
                        if (mode == 4) tris = idx;
                        else if (mode == 5) for (int i = 0; i + 2 < idx.Count; i++) { if (i % 2 == 0) tris.AddRange(new[] { idx[i], idx[i + 1], idx[i + 2] }); else tris.AddRange(new[] { idx[i + 1], idx[i], idx[i + 2] }); }
                        else for (int i = 1; i + 1 < idx.Count; i++) tris.AddRange(new[] { idx[0], idx[i], idx[i + 1] });
                        var part = PartFor(Has(prim, "material") ? (int)Json.Num(prim, "material") : -1);
                        foreach (int k in tris)
                        {
                            if (k < 0 || k >= pos.Length) { part.P.Add(new Vector3()); part.N.Add(new Vector3()); part.UV.Add(new Vector2()); continue; }
                            part.P.Add(Point(world, pos[k]));
                            part.N.Add(nor != null && k < nor.Length ? Direction(normalMatrix, nor[k]) : new Vector3(0, 0, 0));
                            part.UV.Add(uv != null && k < uv.Length ? new Vector2(uv[k][0], uv[k][1]) : new Vector2(0, 0));   // glTF: top-left origin, like the swapper
                        }
                    }
                }
                foreach (var c in Json.Arr(node, "children") ?? new List<object>()) Visit(Convert.ToInt32(c), world, depth + 1);
            }
            foreach (var r in roots) Visit(r, Identity(), 0);
            foreach (var p in mesh.Parts) FillMissingNormals(p);
            mesh.Parts.RemoveAll(p => p.Triangles == 0);
            if (mesh.Triangles == 0) throw new InvalidOperationException("No triangles were found in that model.");
            return mesh;
        }

        static bool Has(object o, string key) => o is Dictionary<string, object> d && d.ContainsKey(key) && d[key] != null;

        // 4x4 matrices, column-major like glTF
        static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        static double[] Local(object node)
        {
            var m = Json.Arr(node, "matrix");
            if (m != null && m.Count == 16) return m.Select(Convert.ToDouble).ToArray();
            var t = (Json.Arr(node, "translation") ?? new List<object> { 0.0, 0.0, 0.0 }).Select(Convert.ToDouble).ToArray();
            var q = (Json.Arr(node, "rotation") ?? new List<object> { 0.0, 0.0, 0.0, 1.0 }).Select(Convert.ToDouble).ToArray();
            var s = (Json.Arr(node, "scale") ?? new List<object> { 1.0, 1.0, 1.0 }).Select(Convert.ToDouble).ToArray();
            double x = q[0], y = q[1], z = q[2], w = q[3];
            // rotation then scale, columns; translation last column
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
            var r = new double[16];
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                {
                    double v = 0;
                    for (int k = 0; k < 4; k++) v += a[k * 4 + row] * b[c * 4 + k];
                    r[c * 4 + row] = v;
                }
            return r;
        }
        static Vector3 Point(double[] m, float[] p) => new Vector3(
            (float)(m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12]),
            (float)(m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13]),
            (float)(m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14]));
        static Vector3 Direction(double[] m, float[] n)
        {
            double x = m[0] * n[0] + m[4] * n[1] + m[8] * n[2], y = m[1] * n[0] + m[5] * n[1] + m[9] * n[2], z = m[2] * n[0] + m[6] * n[1] + m[10] * n[2];
            double l = Math.Sqrt(x * x + y * y + z * z);
            return l < 1e-9 ? new Vector3(0, 0, 0) : new Vector3((float)(x / l), (float)(y / l), (float)(z / l));
        }
    }
}
