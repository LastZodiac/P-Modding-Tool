// The Model Swapper's build, for the Bridge: the same steps, but some of the costume's own shapes stay exactly as they
// were (the other faces, items in hand, face and hair pieces), and each new point follows the bones Blender says.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using BrawlLib.Internal;
using BrawlLib.Modeling;
using BrawlLib.Modeling.Collada;
using BrawlLib.SSBB.ResourceNodes;
using BrawlLib.Wii.Models;
using BrawlLib.Wii.Textures;

namespace SkinManager.Core
{
    /// <summary>The part of model swapping the Bridge uses, which keeps some of the costume's own shapes as they
    /// were.</summary>
    public static partial class ModelSwap
    {
        /// <summary>True when more than 2% of a picture is partly see-through, so it needs the 16-bit format instead of
        /// the compressed one.</summary>
        static bool HasHalfAlpha(Bitmap b)
        {
            int n = 0, half = 0;
            for (int y = 0; y < b.Height; y += 2) for (int x = 0; x < b.Width; x += 2) { n++; int a = b.GetPixel(x, y).A; if (a > 16 && a < 240) half++; }
            return n > 0 && half > n * 0.02;
        }

        /// <summary>Builds the new costume like Build, but keeps the shapes keepObject picks as they are, weights each
        /// new point to the bones weights gives (from Blender), and stays under the costume's limit of bone mixes
        /// (mixLimit) so the game doesn't crash.</summary>
        public static Result BuildKeeping(SwapMesh mesh, byte[] costume, Func<Vector3, List<(string Bone, float W)>> weights, Func<MDL0ObjectNode, bool> keepObject, IDictionary<SwapPart, string> pictureNames = null, Func<Vector3, bool> keepWithBody = null, int mixLimit = -1)
        {
            // the costume picture each part showed when it went to Blender (by name)
            var namesByPart = mesh.Parts.Select(p => pictureNames != null && pictureNames.TryGetValue(p, out var n) ? n : null).ToList();
            mesh = mesh.Copy();   // its pictures are re-saved below: the caller's model stays as it was
            var result = new Result { Notes = new List<string>(mesh.Notes) };
            string work = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "swap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // the new model as a plain .dae with its pictures (sized for the Wii), read back by BrawlLib's importer
                var pictures = WritePictures(mesh, work, result.Notes);
                var wasPicture = new Dictionary<string, string>();   // new picture name -> the costume's picture it came from
                for (int i = 0; i < mesh.Parts.Count; i++) if (namesByPart[i] != null && pictures.ContainsKey(mesh.Parts[i].Material)) wasPicture[mesh.Parts[i].Material] = namesByPart[i];
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
                        // the shapes kept as they are (the other faces, items in hand, face and hair pieces), with their materials and pictures
                        var keptObjs = new HashSet<MDL0ObjectNode>(model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().Where(keepObject));
                        // the kept shapes' own mixes of bones count towards the costume's total (the game builds one matrix for
                        // each: more than the costume had and it can't make the fighter, it crashes as the match starts)
                        int originalMixes = model._influences?.Count ?? 0;
                        int keptMixes = keptObjs.SelectMany(o => o._manager?._vertices ?? new List<Vertex3>()).Select(v => v.MatrixNode).OfType<Influence>().Distinct().Count();
                        var keptMats = new HashSet<MDL0MaterialNode>(keptObjs.SelectMany(o => o._drawCalls.Select(c => c.MaterialNode)).Where(m => m != null));
                        var keptTex = new HashSet<string>(keptMats.SelectMany(m => m.Children.OfType<MDL0MaterialRefNode>()).Select(r => r.Texture).Where(n => n != null), StringComparer.Ordinal);
                        result.Notes.Add($"Kept {keptObjs.Count} of the costume's own shapes exactly as they were.");
                        // a picture that came back from Blender unchanged is the costume's own one: that one is kept
                        // (same quality, and not stored twice)
                        var reuse = new Dictionary<string, string>();
                        var allTex = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().GroupBy(t => t.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                        foreach (var kv in wasPicture)
                        {
                            if (!allTex.TryGetValue(kv.Value, out var orig) || !pictures.TryGetValue(kv.Key, out var png)) continue;
                            try
                            {
                                using (var a = new Bitmap(orig.GetImage(0))) using (var b = new Bitmap(png))
                                {
                                    if (a.Width != b.Width || a.Height != b.Height) continue;
                                    long diff = 0; int n = 0;
                                    for (int y = 0; y < a.Height; y += 3) for (int x = 0; x < a.Width; x += 3)
                                    { var pa = a.GetPixel(x, y); var pb = b.GetPixel(x, y); diff += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B); n++; }
                                    if (n > 0 && diff / (3.0 * n) < 2.0) reuse[kv.Key] = kv.Value;
                                }
                            }
                            catch { }
                        }
                        if (reuse.Count > 0) result.Notes.Add($"{reuse.Count} picture(s) came back unchanged: the costume's own are used.");
                        keptTex.UnionWith(reuse.Values);
                        var oldNames = new HashSet<string>(model.FindChildrenByType(null, ResourceType.MDL0Material).OfType<MDL0MaterialNode>()
                            .SelectMany(m => m.Children.OfType<MDL0MaterialRefNode>()).Select(r => r.Texture).Where(n => n != null && !keptTex.Contains(n)), StringComparer.Ordinal);
                        var texBrres = root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Where(t => oldNames.Contains(t.Name))
                            .Select(t => t.Parent?.Parent as BRRESNode).FirstOrDefault(b => b != null) ?? brres;
                        bool hadMetal = model.FindChildrenByType(null, ResourceType.MDL0Material).Any(m => m.Name.EndsWith("_ExtMtl", StringComparison.Ordinal));

                        Trace("textures found");
                        // out with the P+ model's own meshes, materials and pictures
                        foreach (var obj in model.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().Where(o => !keptObjs.Contains(o)).ToList())
                            obj.Remove(true, true, true, true, true, true, true, true, true, true, true, true);
                        Trace("objects removed");
                        // a material's shader goes with it, unless a kept material uses the same shader (shaders are shared)
                        var keptShaders = new HashSet<MDL0ShaderNode>(keptMats.Select(m => m.ShaderNode).Where(x => x != null));
                        foreach (var mat in model.FindChildrenByType(null, ResourceType.MDL0Material).OfType<MDL0MaterialNode>().Where(m => !keptMats.Contains(m)).ToList())
                            mat.Remove(mat.ShaderNode == null || !keptShaders.Contains(mat.ShaderNode));
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
                        int budget = mixLimit > 0 ? mixLimit : Math.Max(24, originalMixes - keptMixes);
                        var newObjs = newModel.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().ToList();
                        var raw = new Dictionary<Vertex3, List<(string Bone, float W)>>();
                        foreach (var obj in newObjs)
                            foreach (var v in obj._manager._vertices)
                                raw[v] = weights?.Invoke(v._position) ?? (centre != null && keepWithBody(v._position) ? centre.Weights(v._position) : rig != null ? rig.Weights(v._position) : grid.Weights(v._position));
                        var snapped = SnapWeights(raw.Values.ToList(), budget, out int mixes, out string how);
                        Trace($"bone mixes: {mixes} (P+'s own costume: {budget}), {how}");
                        result.Mixes = mixes; result.MixBudget = budget;
                        result.Notes.Add($"Bone mixes: {keptMixes} in the kept shapes + {mixes} new (the costume had {originalMixes}).");
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
                        // the reused pictures: their shapes point at the costume's own picture
                        foreach (var m in model.FindChildrenByType(null, ResourceType.MDL0Material).OfType<MDL0MaterialNode>())
                            foreach (var r in m.Children.OfType<MDL0MaterialRefNode>())
                                if (r.Texture != null && reuse.TryGetValue(r.Texture, out var was)) r.Texture = was;
                        foreach (var kv in pictures.Where(p => !reuse.ContainsKey(p.Key)))
                        {
                            using (var bmp = new Bitmap(kv.Value))
                            {
                                // see-through only where it's on or off (cut-outs, an empty background): the compressed
                                // format keeps that (and is a quarter of the size); half see-through needs the 16-bit one
                                bool alpha = HasHalfAlpha(bmp);
                                var tex = new TEX0Node { Name = kv.Key };
                                texBrres.GetOrCreateFolder<TEX0Node>().AddChild(tex);
                                var conv = alpha ? TextureConverter.RGB5A3 : (TextureConverter)TextureConverter.CMPR;
                                tex.ReplaceRaw(conv.EncodeTEX0Texture(bmp, 1));
                            }
                        }
                        result.Pictures = pictures.Count - reuse.Count;
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

    }
}
