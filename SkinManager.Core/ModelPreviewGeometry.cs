// Where a move's hitboxes are on each frame (no picture): for launch and combo maths. Side view, in game units
// (the model's units times the fighter's size), x forward, y up, the fighter starting at 0,0.
using System;
using System.Collections.Generic;
using System.Linq;
using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public sealed class FramePlace
    {
        public List<(Hitbox Hit, float X, float Y, float R)> Hits = new List<(Hitbox, float, float, float)>();
        public float RootX, RootY;   // where the fighter's root (TransN) is
    }

    public static unsafe partial class ModelPreview
    {
        /// <summary>For each move, each frame's hitboxes in game units. Also the fighter's standing body (from its hurtboxes).</summary>
        public static Dictionary<MoveData, List<FramePlace>> Places(byte[] costume, byte[] motionFile, Moveset moves, IEnumerable<MoveData> list, float size,
            out (float MinX, float MaxX, float MinY, float MaxY) body)
        {
            var result = new Dictionary<MoveData, List<FramePlace>>();
            body = (-3, 3, 0, 12);
            lock (BrawlGate.Lock)
            using (var mt = new TempFile(".pac", motionFile))
            using (var mroot = NodeFactory.FromFile(null, mt.Path))
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var anims = mroot.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault();
                if (model == null) return result;
                var bones = model._linker.BoneCache;
                int trans = Array.FindIndex(bones, b => b.Name == "TransN");
                MDL0BoneNode BoneAt(int i) { if (i >= 400) i -= 400; if (i == 0 && trans >= 0) i = trans; return i >= 0 && i < bones.Length ? bones[i] as MDL0BoneNode : null; }
                Vector3 Place(MDL0BoneNode b, float x, float y, float z)
                {
                    var bp = b._frameMatrix.GetPoint(); var r = b._frameMatrix.GetRotationMatrix(); var s = Scale(b);
                    var g = r.Multiply(new Vector3(x / NZ(s._x), y / NZ(s._y), z / NZ(s._z)));
                    return Yaw(new Vector3(bp._x + g._x, bp._y + g._y, bp._z + g._z));
                }
                // standing body: the hurtboxes in the default pose
                model.ApplyCHR(null, 0);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var hb in moves.HurtBoxes)
                {
                    var b = BoneAt(hb.Bone); if (b == null) continue;
                    foreach (var p in new[] { Place(b, hb.X, hb.Y, hb.Z), Place(b, hb.X + hb.SX, hb.Y + hb.SY, hb.Z + hb.SZ) })
                    { x0 = Math.Min(x0, p._x - hb.Radius); x1 = Math.Max(x1, p._x + hb.Radius); y0 = Math.Min(y0, p._y - hb.Radius); y1 = Math.Max(y1, p._y + hb.Radius); }
                }
                if (x0 < x1) body = (x0 * size, x1 * size, Math.Max(0, y0) * size, y1 * size);
                foreach (var move in list)
                {
                    var frames = new List<FramePlace>();
                    foreach (var f in move.Frames)
                    {
                        var fp = new FramePlace();
                        if (f.Hitboxes.Count > 0 || f == move.Frames[move.Frames.Count - 1])
                        {
                            anims.TryGetValue(f.Animation ?? "", out var anim);
                            model.ApplyCHR(anim, anim == null ? 0 : Math.Max(1, Math.Min(f.AnimFrame, anim.FrameCount)));
                            var tb = BoneAt(trans >= 0 ? trans : 0);
                            if (tb != null) { var t = Yaw(tb._frameMatrix.GetPoint()); fp.RootX = t._x * size; fp.RootY = t._y * size; }
                            foreach (var h in f.Hitboxes)
                            {
                                var b = BoneAt(h.Bone); if (b == null) continue;
                                var p = Place(b, h.X, h.Y, h.Z);
                                fp.Hits.Add((h, p._x * size, p._y * size, h.Size * size));
                            }
                        }
                        frames.Add(fp);
                    }
                    result[move] = frames;
                }
            }
            return result;
        }
    }
}
