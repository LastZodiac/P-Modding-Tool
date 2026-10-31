// P+'s own model of a costume as a 3D file (GLB) anyone's 3D tools can open: its shape in the default pose (arms
// out), its pictures (textures) and how they wrap around it. For painting a new look in a 3D program: paint it
// look onto P+'s exact shape, then bring it back with the Model Swapper: it fits the skeleton perfectly.
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

namespace SkinManager.Core
{
    public static unsafe class ModelExport
    {
        sealed class Part { public string Name; public byte[] Png; public bool Cutout, SeeThrough; public List<Vector3> P = new List<Vector3>(), N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); }

        /// <summary>The costume file (.pac/.pcs) as a GLB. Throws if it has no model.</summary>
        public static byte[] ToGlb(byte[] costume, out int triangles, IEnumerable<int> hiddenBones = null)
        {
            var parts = new List<Part>();
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault()
                    ?? throw new InvalidOperationException("This costume has no model.");
                model.ApplyCHR(null, 0);
                // things the fighter only shows during some moves (Peach's racket, Fox's blaster...): left out
                var bones = model._linker.BoneCache;
                var hidden = new HashSet<string>((hiddenBones ?? new int[0]).Where(i => i >= 0 && i < bones.Length).Select(i => bones[i].Name));
                var textures = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var pngs = new Dictionary<string, (byte[] png, bool alpha)>(StringComparer.OrdinalIgnoreCase);
                var field = typeof(PrimitiveManager).GetField("_triangles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>())
                {
                    var call = obj._drawCalls.FirstOrDefault(c => c.VisibilityBoneNode == null || c.VisibilityBoneNode.Visible && !hidden.Contains(c.VisibilityBoneNode.Name));
                    if (call == null) continue;
                    // the other faces (blinking, hurt...) share the head: only the normal one
                    string vis = call.VisibilityBoneNode?.Name ?? "";
                    if (vis.IndexOf("Blink", StringComparison.OrdinalIgnoreCase) >= 0 || vis.IndexOf("Ouch", StringComparison.OrdinalIgnoreCase) >= 0 || vis.IndexOf("Half", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var mgr = obj._manager;
                    if (mgr?._vertices == null || mgr._faceData?[0] == null) continue;
                    var prim = field?.GetValue(mgr) as GLPrimitive;
                    if (prim == null) continue;
                    var mat = call.MaterialNode;
                    string tex = mat?.Children.OfType<MDL0MaterialRefNode>().FirstOrDefault(r => r.TextureNode != null)?.TextureNode?.Name;
                    string key = (mat?.Name ?? obj.Name) + "|" + tex;
                    var part = parts.FirstOrDefault(x => x.Name == key);
                    if (part == null)
                    {
                        part = new Part { Name = key, SeeThrough = mat != null && mat.XLUMaterial };
                        if (tex != null && textures.TryGetValue(tex, out var t))
                        {
                            if (!pngs.TryGetValue(tex, out var p))
                                using (var bmp = new Bitmap(t.GetImage(0)))
                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Png);
                                    bool alpha = false;
                                    for (int y = 0; y < bmp.Height && !alpha; y += 2) for (int x = 0; x < bmp.Width; x += 2) if (bmp.GetPixel(x, y).A < 250) { alpha = true; break; }
                                    pngs[tex] = p = (ms.ToArray(), alpha);
                                }
                            part.Png = p.png; part.Cutout = p.alpha && !part.SeeThrough;
                        }
                        parts.Add(part);
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
                            part.P.Add(vert?.WeightedPosition ?? new Vector3());
                            var n = nrm != null ? nrm[fp] : new Vector3(0, 1, 0);
                            if (vert?.GetMatrixNode() is IBoneNode bone) n = bone.BindMatrix.GetRotationMatrix() * n;
                            float l = (float)Math.Sqrt(n._x * n._x + n._y * n._y + n._z * n._z);
                            part.N.Add(l > 1e-6f ? new Vector3(n._x / l, n._y / l, n._z / l) : new Vector3(0, 1, 0));
                            part.UV.Add(uv != null ? uv[fp] : new Vector2(0, 0));
                        }
                }
            }
            parts.RemoveAll(p => p.P.Count == 0);
            if (parts.Count == 0) throw new InvalidOperationException("No shapes were found in this costume's model.");
            triangles = parts.Sum(p => p.P.Count / 3);
            return Write(parts);
        }

        static byte[] Write(List<Part> parts)
        {
            var bin = new MemoryStream();
            var w = new BinaryWriter(bin);
            var views = new List<string>(); var accessors = new List<string>(); var images = new List<string>(); var materials = new List<string>(); var prims = new List<string>(); var texturesJ = new List<string>();
            int View(int offset, int length, int? target) { views.Add($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{length}{(target != null ? $",\"target\":{target}" : "")}}}"); return views.Count - 1; }
            void Pad() { while (bin.Length % 4 != 0) w.Write((byte)0); }
            var pngIndex = new Dictionary<byte[], int>();
            foreach (var p in parts)
            {
                int n = p.P.Count;
                // positions
                Pad(); int at = (int)bin.Length;
                float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
                foreach (var v in p.P) { w.Write(v._x); w.Write(v._y); w.Write(v._z); x0 = Math.Min(x0, v._x); y0 = Math.Min(y0, v._y); z0 = Math.Min(z0, v._z); x1 = Math.Max(x1, v._x); y1 = Math.Max(y1, v._y); z1 = Math.Max(z1, v._z); }
                int pv = View(at, n * 12, 34962);
                accessors.Add(Inv($"{{\"bufferView\":{pv},\"componentType\":5126,\"count\":{n},\"type\":\"VEC3\",\"min\":[{x0},{y0},{z0}],\"max\":[{x1},{y1},{z1}]}}"));
                int pa = accessors.Count - 1;
                Pad(); at = (int)bin.Length;
                foreach (var v in p.N) { w.Write(v._x); w.Write(v._y); w.Write(v._z); }
                int nv = View(at, n * 12, 34962);
                accessors.Add($"{{\"bufferView\":{nv},\"componentType\":5126,\"count\":{n},\"type\":\"VEC3\"}}");
                int na = accessors.Count - 1;
                Pad(); at = (int)bin.Length;
                foreach (var v in p.UV) { w.Write(v._x); w.Write(v._y); }
                int tv = View(at, n * 8, 34962);
                accessors.Add($"{{\"bufferView\":{tv},\"componentType\":5126,\"count\":{n},\"type\":\"VEC2\"}}");
                int ta = accessors.Count - 1;
                // material (with its picture)
                string texPart = "";
                if (p.Png != null)
                {
                    if (!pngIndex.TryGetValue(p.Png, out int img))
                    {
                        Pad(); at = (int)bin.Length; w.Write(p.Png);
                        int iv = View(at, p.Png.Length, null);
                        images.Add($"{{\"bufferView\":{iv},\"mimeType\":\"image/png\"}}");
                        texturesJ.Add($"{{\"source\":{images.Count - 1},\"sampler\":0}}");
                        img = texturesJ.Count - 1; pngIndex[p.Png] = img;
                    }
                    texPart = $",\"baseColorTexture\":{{\"index\":{img}}}";
                }
                string mode = p.SeeThrough ? ",\"alphaMode\":\"BLEND\"" : p.Cutout ? ",\"alphaMode\":\"MASK\",\"alphaCutoff\":0.45" : "";
                materials.Add($"{{\"name\":{Q(p.Name.Split('|')[0])},\"doubleSided\":true,\"pbrMetallicRoughness\":{{\"metallicFactor\":0,\"roughnessFactor\":1{texPart}}}{mode}}}");
                prims.Add($"{{\"attributes\":{{\"POSITION\":{pa},\"NORMAL\":{na},\"TEXCOORD_0\":{ta}}},\"material\":{materials.Count - 1}}}");
            }
            Pad();
            string json = "{\"asset\":{\"version\":\"2.0\",\"generator\":\"P+ Modding Tool\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"P+ model\",\"mesh\":0}]," +
                $"\"meshes\":[{{\"name\":\"P+ model\",\"primitives\":[{string.Join(",", prims)}]}}]," +
                $"\"materials\":[{string.Join(",", materials)}]," +
                (texturesJ.Count > 0 ? $"\"textures\":[{string.Join(",", texturesJ)}],\"images\":[{string.Join(",", images)}],\"samplers\":[{{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}}]," : "") +
                $"\"accessors\":[{string.Join(",", accessors)}],\"bufferViews\":[{string.Join(",", views)}],\"buffers\":[{{\"byteLength\":{bin.Length}}}]}}";
            var jb = Encoding.UTF8.GetBytes(json);
            int jlen = (jb.Length + 3) / 4 * 4;
            var outS = new MemoryStream();
            var o = new BinaryWriter(outS);
            o.Write(0x46546C67u); o.Write(2u); o.Write((uint)(12 + 8 + jlen + 8 + bin.Length));
            o.Write((uint)jlen); o.Write(0x4E4F534Au); o.Write(jb); for (int i = jb.Length; i < jlen; i++) o.Write((byte)' ');
            o.Write((uint)bin.Length); o.Write(0x004E4942u); o.Write(bin.ToArray());
            return outS.ToArray();
        }

        static string Inv(FormattableString s) => FormattableString.Invariant(s);   // decimal points, whatever the PC's language
        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
