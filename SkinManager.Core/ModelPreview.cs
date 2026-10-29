// Draws a costume's 3D model into a picture, so you can see what a skin looks like before
// installing it. A small software renderer: the model in its default pose, textured, lit from the
// front, turned a little to the side. No graphics card or game needed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BrawlLib.Internal;
using BrawlLib.Modeling;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static unsafe partial class ModelPreview
    {
        sealed class Tex { public int W, H; public int[] Px; }

        static readonly object Gate = new object();   // BrawlLib isn't made for two files at once

        /// <summary>Renders a costume file on disk (null if it can't be read or has no model).</summary>
        public static Bitmap RenderFile(string path, int width = 256, int height = 320)
        {
            try { return Render(System.IO.File.ReadAllBytes(path), width, height); } catch { return null; }
        }

        /// <summary>A preview saved next to a skin's files (made once, then reused).</summary>
        public static Bitmap Cached(string costumePath, string pngPath)
        {
            try
            {
                if (System.IO.File.Exists(pngPath)) using (var b = new Bitmap(pngPath)) return new Bitmap(b);
                var bmp = RenderFile(costumePath);
                if (bmp != null) try { bmp.Save(pngPath, ImageFormat.Png); } catch { }
                return bmp;
            }
            catch { return null; }
        }

        static readonly FieldInfo TrianglesField = typeof(PrimitiveManager).GetField("_triangles", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Renders the costume file (.pac or .pcs bytes). Null if it has no model.</summary>
        public static Bitmap Render(byte[] costume, int width = 256, int height = 320, float yawDegrees = 22)
        {
            lock (BrawlGate.Lock) return RenderLocked(costume, width, height, yawDegrees);
        }

        /// <summary>Renders the costume in a pose: an animation (CHR0) from a motion file at a frame.</summary>
        public static Bitmap RenderPosed(byte[] costume, byte[] motionFile, string animation, int frame, int width = 256, int height = 320, float yawDegrees = 22)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", motionFile))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var anim = root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().FirstOrDefault(a => string.Equals(a.Name, animation, StringComparison.OrdinalIgnoreCase));
                return RenderLocked(costume, width, height, yawDegrees, anim, frame);
            }
        }

        static Bitmap RenderLocked(byte[] costume, int width, int height, float yawDegrees, CHR0Node anim = null, int frame = 0)
        {
            using (var tmp = new TempFile(".pac", costume))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault();
                if (model == null) return null;
                model.ApplyCHR(anim, anim == null ? 0 : Math.Min(frame, anim.FrameCount));   // bones posed (default pose without an animation), vertices placed

                var tris = Gather(root, new[] { model }, yawDegrees);
                if (tris.Count == 0) return null;

                if (tris.Count == 0) return null;
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (var t in tris)
                    foreach (var v in new[] { t.a, t.b, t.c })
                    { minX = Math.Min(minX, v._x); maxX = Math.Max(maxX, v._x); minY = Math.Min(minY, v._y); maxY = Math.Max(maxY, v._y); }
                return Draw(tris, width, height, minX, maxX, minY, maxY, 0.92f, 0.94f, null);
            }
        }

        /// <summary>How much a costume's surface stretches or squashes in a pose, against its default pose: for each triangle the
        /// worst change of an edge's length. Returns the share of the surface changed by more than a third, and the average change.</summary>
        public static (float Torn, float Average) Stretch(byte[] costume, byte[] motionFile, string animation, int frame)
        {
            lock (BrawlGate.Lock)
            using (var mt = new TempFile(".pac", motionFile))
            using (var mroot = NodeFactory.FromFile(null, mt.Path))
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var anim = mroot.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().FirstOrDefault(x => string.Equals(x.Name, animation, StringComparison.OrdinalIgnoreCase));
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().First();
                model.ApplyCHR(null, 0);
                var rest = Gather(root, new[] { model }, 0);
                model.ApplyCHR(anim, anim == null ? 0 : Math.Min(frame, anim.FrameCount));
                var posed = Gather(root, new[] { model }, 0);
                double torn = 0, total = 0, sum = 0;
                for (int i = 0; i < rest.Count && i < posed.Count; i++)
                {
                    var r = rest[i]; var p = posed[i];
                    float area = Len(Cross(Sub(r.b, r.a), Sub(r.c, r.a))) / 2;
                    if (area < 1e-6f) continue;
                    double worst = 0;
                    foreach (var (ra, rb, pa, pb) in new[] { (r.a, r.b, p.a, p.b), (r.b, r.c, p.b, p.c), (r.c, r.a, p.c, p.a) })
                    {
                        float l0 = Len(Sub(rb, ra)), l1 = Len(Sub(pb, pa));
                        if (l0 < 1e-5f) continue;
                        worst = Math.Max(worst, Math.Abs(Math.Log(Math.Max(1e-4, l1) / l0)));
                    }
                    total += area; sum += area * worst;
                    if (worst > Math.Log(1.33)) torn += area;
                }
                return total > 0 ? ((float)(torn / total), (float)(sum / total)) : (0, 0);
            }
        }

        static float Len(Vector3 v) => (float)Math.Sqrt(v._x * v._x + v._y * v._y + v._z * v._z);

        /// <summary>The triangles of models (turned by a yaw), with their pictures.</summary>
        static List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)> Gather(ResourceNode root, IEnumerable<MDL0Node> models, float yawDegrees, Dictionary<string, Tex> cache = null, HashSet<string> hiddenBones = null)
        {
            var textures = cache ?? new Dictionary<string, Tex>(StringComparer.OrdinalIgnoreCase);   // kept between calls when a cache is given
            foreach (var t in root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>())
                if (!textures.ContainsKey(t.Name)) textures[t.Name] = null;   // decoded when first used

            Tex TextureFor(MDL0MaterialNode m)
            {
                var r = m?.Children.OfType<MDL0MaterialRefNode>().FirstOrDefault(x => x.TextureNode != null);
                if (r == null) return null;
                string name = r.TextureNode.Name;
                if (!textures.TryGetValue(name, out var tex)) return null;
                if (tex != null) return tex;
                var node = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().First(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                using (var bmp = new Bitmap(node.GetImage(0)))
                {
                    tex = new Tex { W = bmp.Width, H = bmp.Height, Px = new int[bmp.Width * bmp.Height] };
                    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < bmp.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, tex.Px, y * bmp.Width, bmp.Width);
                    bmp.UnlockBits(data);
                }
                return textures[name] = tex;
            }

            // gather triangles: position (turned), uv, texture, see-through
            double yaw = yawDegrees * Math.PI / 180, cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)>();
            foreach (var obj in models.SelectMany(model => model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>()))
            {
                // its first draw call that is shown by default (other hands, faces... are hidden)
                var call = obj._drawCalls.FirstOrDefault(c => c.VisibilityBoneNode == null || (c.VisibilityBoneNode.Visible && (hiddenBones == null || !hiddenBones.Contains(c.VisibilityBoneNode.Name))));
                if (call == null) continue;
                var mgr = obj._manager;
                if (mgr?._vertices == null || mgr._faceData == null || mgr._faceData[0] == null) continue;
                var prim = TrianglesField?.GetValue(mgr) as GLPrimitive;
                if (prim == null) continue;
                var mat = call.MaterialNode;
                var tex = TextureFor(mat);
                bool xlu = mat != null && mat.XLUMaterial;
                ushort* idx = (ushort*)mgr._indices.Address;
                Vector2* uv = mgr._faceData[4] != null ? (Vector2*)mgr._faceData[4].Address : null;
                Vector3 P(uint fp)
                {
                    int vi = idx[fp];
                    var w = vi < mgr._vertices.Count ? mgr._vertices[vi].WeightedPosition : new Vector3();
                    return new Vector3((float)(w._x * cy + w._z * sy), w._y, (float)(-w._x * sy + w._z * cy));
                }
                Vector2 U(uint fp) => uv != null ? uv[fp] : new Vector2(0.5f, 0.5f);
                var ind = prim._indices;
                for (int i = 0; i + 2 < ind.Length; i += 3)
                    tris.Add((P(ind[i]), P(ind[i + 1]), P(ind[i + 2]), U(ind[i]), U(ind[i + 1]), U(ind[i + 2]), tex, xlu));
            }
            return tris;
        }

        /// <summary>A model that isn't a costume yet (the Model Swapper's), from the front, showing exactly the given area.</summary>
        public static Bitmap RenderMesh(SwapMesh mesh, int width, int height, float minX, float maxX, float minY, float maxY)
        {
            var texs = new Dictionary<string, Tex>(StringComparer.OrdinalIgnoreCase);
            Tex Load(string path)
            {
                if (path == null) return null;
                if (texs.TryGetValue(path, out var t)) return t;
                try
                {
                    using (var fs = System.IO.File.OpenRead(path)) using (var img = Image.FromStream(fs)) using (var bmp = new Bitmap(img))
                    {
                        t = new Tex { W = bmp.Width, H = bmp.Height, Px = new int[bmp.Width * bmp.Height] };
                        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        for (int y = 0; y < bmp.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, t.Px, y * bmp.Width, bmp.Width);
                        bmp.UnlockBits(data);
                    }
                }
                catch { t = null; }
                return texs[path] = t;
            }
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)>();
            foreach (var p in mesh.Parts)
            {
                var tex = Load(p.TexturePath);
                for (int i = 0; i + 2 < p.P.Count; i += 3) tris.Add((p.P[i], p.P[i + 1], p.P[i + 2], p.UV[i], p.UV[i + 1], p.UV[i + 2], tex, false));
            }
            return Draw(tris, width, height, minX, maxX, minY, maxY, 1f, 1f, null);
        }

        /// <summary>A costume from the front, the side and the back, all at the same scale and without perspective (true
        /// to size, for making a matching 3D model from), in its default pose, on white. Plus its height in P+ units.</summary>
        public static (Bitmap[] Views, float Height) RenderViews(byte[] costume, int size = 1024)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault() ?? throw new InvalidDataException("That costume has no model.");
                model.ApplyCHR(null, 0);
                var yaws = new[] { 0f, 90f, 180f };
                var all = yaws.Select(y => Gather(root, new[] { model }, y)).ToList();
                if (all[0].Count == 0) throw new InvalidDataException("That costume has nothing to draw.");
                // one frame for all three: the same scale, the same floor line
                float minY = float.MaxValue, maxY = float.MinValue, half = 0;
                foreach (var tris in all)
                    foreach (var t in tris)
                        foreach (var v in new[] { t.a, t.b, t.c })
                        { minY = Math.Min(minY, v._y); maxY = Math.Max(maxY, v._y); half = Math.Max(half, Math.Abs(v._x)); }
                float h = maxY - minY, span = Math.Max(h, 2 * half) * 1.08f, cy = (minY + maxY) / 2;
                var views = all.Select(tris =>
                {
                    using (var drawn = Draw(tris, size, size, -span / 2, span / 2, cy - span / 2, cy + span / 2, 1f, 1f, null))
                    {
                        var white = new Bitmap(size, size, PixelFormat.Format24bppRgb);
                        using (var g = Graphics.FromImage(white)) { g.Clear(Color.White); g.DrawImage(drawn, 0, 0, size, size); }
                        return white;
                    }
                }).ToArray();
                return (views, h);
            }
        }

        /// <summary>A stage seen from the front: its scenery, with its floors drawn on top (gold: where you stand,
        /// grey: walls and undersides). Framed on the floors.</summary>
        public static int Cull = 1;
        public static Bitmap RenderStage(byte[] stage, int width = 480, int height = 270)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", stage))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var models = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => (m.FindChild("Objects", false)?.Children.Count ?? 0) > 0).ToList();
                foreach (var m in models) { try { m.ApplyCHR(null, 0); } catch { } }
                var tris = Gather(root, models, 0);
                var floors = StageCheck.Surfaces(root);
                if (floors.Count == 0) floors = StageCheck.Surfaces(root, moving: true);   // a stage you stand on moving parts of: where they start
                var stand = floors.Where(f => f.floor).ToList();
                if (stand.Count == 0) return null;
                float fx0 = stand.Min(f => Math.Min(f.x1, f.x2)), fx1 = stand.Max(f => Math.Max(f.x1, f.x2));
                float fy0 = stand.Min(f => Math.Min(f.y1, f.y2)), fy1 = stand.Max(f => Math.Max(f.y1, f.y2));
                float w = (fx1 - fx0) * 1.6f, cx = (fx0 + fx1) / 2, cy = (fy0 + fy1) / 2 + (fx1 - fx0) * 0.12f;
                float h = w * height / width;
                var lines = floors.Select(f => (f.x1, f.y1, f.x2, f.y2, f.floor ? Color.FromArgb(230, 255, 190, 40) : Color.FromArgb(200, 210, 210, 210))).ToList();
                return Draw(tris, width, height, cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2, 1f, 1f, lines, Cull, (fx1 - fx0) * 1.5f);
            }
        }

        /// <summary>Draws triangles (x right, y up, bigger z nearer) into a picture showing the given area,
        /// with optional lines on top (world coordinates).</summary>
        static Bitmap Draw(List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)> tris, int width, int height,
            float minX, float maxX, float minY, float maxY, float fillX, float fillY, List<(float x1, float y1, float x2, float y2, Color color)> lines,
            int cull = 0, float nearZ = float.MaxValue)
        {
            const int SS = 2;   // drawn at twice the size, then shrunk (smooth edges)
            int W = width * SS, H = height * SS;
            float scale = Math.Min((W * fillX) / Math.Max(0.001f, maxX - minX), (H * fillY) / Math.Max(0.001f, maxY - minY));
            float ox = W / 2f - (minX + maxX) / 2f * scale, oy = H / 2f + (minY + maxY) / 2f * scale;

            var color = new int[W * H];
            var depth = new float[W * H];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MinValue;
            var light = Normalize(new Vector3(-0.35f, 0.55f, 0.85f));

            foreach (var pass in new[] { false, true })   // solid parts first, see-through parts on top
                foreach (var t in tris.Where(x => x.xlu == pass))
                {
                    var n = Normalize(Cross(Sub(t.b, t.a), Sub(t.c, t.a)));
                    float lambert = Math.Abs(n._x * light._x + n._y * light._y + n._z * light._z);
                    float shade = 0.62f + 0.5f * lambert;
                    float ax = ox + t.a._x * scale, ay = oy - t.a._y * scale;
                    float bx = ox + t.b._x * scale, by = oy - t.b._y * scale;
                    float cx = ox + t.c._x * scale, cyy = oy - t.c._y * scale;
                    float area = (bx - ax) * (cyy - ay) - (by - ay) * (cx - ax);
                    if (Math.Abs(area) < 1e-6f) continue;
                    if (cull != 0 && Math.Sign(area) == cull) continue;   // its back faces the camera (the near side of a sky dome)
                    if (t.a._z > nearZ || t.b._z > nearZ || t.c._z > nearZ) continue;   // behind the camera
                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cyy)))), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cyy))));
                    for (int py = y0; py <= y1; py++)
                        for (int px = x0; px <= x1; px++)
                        {
                            float fx = px + 0.5f, fy = py + 0.5f;
                            float w0 = ((bx - fx) * (cyy - fy) - (by - fy) * (cx - fx)) / area;
                            float w1 = ((cx - fx) * (ay - fy) - (cyy - fy) * (ax - fx)) / area;
                            float w2 = 1 - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            float z = w0 * t.a._z + w1 * t.b._z + w2 * t.c._z;
                            int at = py * W + px;
                            if (z <= depth[at]) continue;
                            int argb = unchecked((int)0xFFC8C8C8);
                            if (t.tex != null)
                            {
                                float u = w0 * t.ta._x + w1 * t.tb._x + w2 * t.tc._x;
                                float v = w0 * t.ta._y + w1 * t.tb._y + w2 * t.tc._y;
                                int tx = ((int)Math.Floor(u * t.tex.W) % t.tex.W + t.tex.W) % t.tex.W;
                                int ty = ((int)Math.Floor(v * t.tex.H) % t.tex.H + t.tex.H) % t.tex.H;
                                argb = t.tex.Px[ty * t.tex.W + tx];
                            }
                            int a = (argb >> 24) & 0xFF;
                            if (a < 110) continue;   // cut-out parts (hair edges, lashes)
                            int r = Clamp(((argb >> 16) & 0xFF) * shade), g = Clamp(((argb >> 8) & 0xFF) * shade), b = Clamp((argb & 0xFF) * shade);
                            if (pass && a < 250 && color[at] != 0)
                            {
                                int old = color[at]; float k = a / 255f;
                                r = (int)(r * k + ((old >> 16) & 0xFF) * (1 - k)); g = (int)(g * k + ((old >> 8) & 0xFF) * (1 - k)); b = (int)(b * k + (old & 0xFF) * (1 - k));
                            }
                            else depth[at] = z;
                            color[at] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                        }
                }

            // shrink to the final size (average of each 2x2 block, keeping see-through background)
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var outPx = new int[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int sa = 0, sr = 0, sg = 0, sb = 0;
                    for (int dy = 0; dy < SS; dy++)
                        for (int dx = 0; dx < SS; dx++)
                        {
                            int c = color[(y * SS + dy) * W + x * SS + dx];
                            if (c == 0) continue;
                            sa += 255; sr += (c >> 16) & 0xFF; sg += (c >> 8) & 0xFF; sb += c & 0xFF;
                        }
                    int n = sa / 255;
                    outPx[y * width + x] = n == 0 ? 0 : ((sa / (SS * SS)) << 24) | ((sr / n) << 16) | ((sg / n) << 8) | (sb / n);
                }
            var bd = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++) Marshal.Copy(outPx, y * width, bd.Scan0 + y * bd.Stride, width);
            result.UnlockBits(bd);
            if (lines != null)
                using (var g = Graphics.FromImage(result))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    foreach (var l in lines)
                        using (var pen = new Pen(l.color, 2f))
                            g.DrawLine(pen, (ox + l.x1 * scale) / SS, (oy - l.y1 * scale) / SS, (ox + l.x2 * scale) / SS, (oy - l.y2 * scale) / SS);
                }
            return result;
        }

        static int Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)v;
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a._x - b._x, a._y - b._y, a._z - b._z);
        static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a._y * b._z - a._z * b._y, a._z * b._x - a._x * b._z, a._x * b._y - a._y * b._x);
        static Vector3 Normalize(Vector3 v) { float l = (float)Math.Sqrt(v._x * v._x + v._y * v._y + v._z * v._z); return l < 1e-9f ? new Vector3(0, 0, 1) : new Vector3(v._x / l, v._y / l, v._z / l); }
    }
}
