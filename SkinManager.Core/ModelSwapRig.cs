// Rigging by the skeleton: every point of the new model follows the body bones it's closest to (the line from a
// joint to the next one: thigh, shin, upper arm...), blended where two meet so knees and elbows bend smoothly.
// Bones for things the new model doesn't have (a dress, hair, a cape, a tail, held items) are left out: copying the
// nearest point of P+'s own costume made jeans swing like Peach's dress.
using System;
using System.Collections.Generic;
using System.Linq;
using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static partial class ModelSwap
    {
        public static void SetStandard(bool on) => SkeletonRig.StandardOnly = on;
        static readonly string[] SoftParts = { "skirt", "hair", "cape", "cloth", "mant", "robe", "scarf", "ribbon", "tail", "ear", "coat", "have", "throw", "trans", "rot", "top", "mouth", "jaw", "eye", "brow", "lip", "tongue", "maekake" };

        /// <summary>A bone for a separate thing (a held item, a crown or eye patch model): its name ends in "M" (ClubM, CrownBM).</summary>
        static bool Attachment(string n) => n.Length > 1 && n[n.Length - 1] == 'M';

        sealed class SkeletonRig
        {
            readonly List<(string Name, Vector3 A, Vector3 B)> _segs = new List<(string, Vector3, Vector3)>();

            public static SkeletonRig Make(MDL0Node model, List<RefPoint> refs)
            {
                var body = Body(model, refs);
                if (body == null) return null;   // too few body bones to go by: the old way
                var rig = new SkeletonRig();
                foreach (var (b, to) in body) rig._segs.Add((b.Name, P(b), to != null ? P(to) : P(b)));
                return rig;
            }

            /// <summary>A rig over given segments (used for lining a model up: the segments where its own joints are).</summary>
            public static SkeletonRig From(IEnumerable<(string Name, Vector3 A, Vector3 B)> segs) { var r = new SkeletonRig(); r._segs.AddRange(segs); return r; }

            /// <summary>The same rig with only the middle of the body (hips, waist, chest, neck, head): for hair and the like.</summary>
            public SkeletonRig Centre() => From(_segs.Where(x => !Sided(x.Name)));

            public static bool StandardOnly = true;
            static readonly System.Text.RegularExpressions.Regex BodyName = new System.Text.RegularExpressions.Regex("^(HipN|WaistN|BustN|NeckN|HeadN|[LR](ShoulderN|ShoulderJ|ArmJ|HandN|LegJ|KneeJ|FootJ|ToeN))$");

            static bool Arm(string n) => Sided(n) && !System.Text.RegularExpressions.Regex.IsMatch(n, "Leg|Knee|Foot|Toe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            public static Vector3 P(MDL0BoneNode b) => b.Matrix.GetPoint();
            public static bool Sided(string n) => n.Length > 1 && (n[0] == 'L' || n[0] == 'R') && char.IsUpper(n[1]);

            /// <summary>The body bones (the ones the new model follows), each with the body bone its segment runs to (null: a point).</summary>
            public static List<(MDL0BoneNode Bone, MDL0BoneNode To)> Body(MDL0Node model, List<RefPoint> refs) => Body(model, refs, out _);

            /// <summary>The same, plus each body bone's parent body bone.</summary>
            public static List<(MDL0BoneNode Bone, MDL0BoneNode To)> Body(MDL0Node model, List<RefPoint> refs, out Dictionary<string, string> parent)
            {
                var deform = new HashSet<string>(refs.SelectMany(r => r.Weights.Select(w => w.Bone)), StringComparer.Ordinal);
                // fingers too: a new model's hand rarely has its fingers where P+'s are, so the hand moves as one piece
                bool Finger(string n) => System.Text.RegularExpressions.Regex.IsMatch(n, @"^[LR](1st|2nd|3rd|4th|5th|Thumb|Finger|Index|Middle|Ring|Pinky)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // helper bones (LArmNa, LShoulderJb, WaistNb...) are stretched and twisted by the animations to fake muscles on P+'s own model
                bool Helper(string n) => System.Text.RegularExpressions.Regex.IsMatch(n, "[NJ][a-z]$");
                Func<MDL0BoneNode, bool> Core = b => deform.Contains(b.Name) && !Attachment(b.Name) && !Finger(b.Name) && !Helper(b.Name) && !SoftParts.Any(s => b.Name.ToLowerInvariant().Contains(s));
                var bones = model.BoneCache.Cast<MDL0BoneNode>().ToList();
                // the standard body bones every Brawl fighter shares: nothing else (ponytails, swords, capes...) can move the model
                var standard = bones.Where(b => deform.Contains(b.Name) && BodyName.IsMatch(b.Name)).ToList();
                bool std = StandardOnly && standard.Count >= 12;
                var core = std ? standard : bones.Where(Core).ToList();
                if (std) { var keepSet = new HashSet<string>(core.Select(b => b.Name)); Core = b => keepSet.Contains(b.Name); }
                parent = new Dictionary<string, string>(StringComparer.Ordinal);
                if (core.Count < 6) return null;
                // the next body bones down the chain (through bones that aren't body bones)
                List<MDL0BoneNode> Next(MDL0BoneNode b)
                {
                    var r = new List<MDL0BoneNode>();
                    foreach (var c in b.Children.OfType<MDL0BoneNode>())
                        if (Core(c)) r.Add(c); else r.AddRange(Next(c));
                    return r;
                }
                var list = new List<(MDL0BoneNode, MDL0BoneNode)>();
                foreach (var b in core)
                {
                    var a = P(b);
                    var next = Next(b);
                    foreach (var c in next) parent[c.Name] = b.Name;
                    // a centre bone (hip, chest) runs on to the centre bone after it (waist, neck), not to a leg or arm
                    if (!Sided(b.Name) && next.Any(n => !Sided(n.Name))) next = next.Where(n => !Sided(n.Name)).ToList();
                    list.Add((b, next.OrderByDescending(n => Dist2(P(n), a)).FirstOrDefault()));
                }
                return list;
            }

            public static float Along(Vector3 p, Vector3 a, Vector3 b)
            {
                float dx = b._x - a._x, dy = b._y - a._y, dz = b._z - a._z, l = dx * dx + dy * dy + dz * dz;
                return l < 1e-9f ? 0 : Math.Max(0, Math.Min(1, ((p._x - a._x) * dx + (p._y - a._y) * dy + (p._z - a._z) * dz) / l));
            }

            public static float SegDist(Vector3 p, Vector3 a, Vector3 b)
            {
                float dx = b._x - a._x, dy = b._y - a._y, dz = b._z - a._z, l = dx * dx + dy * dy + dz * dz;
                float t = l < 1e-9f ? 0 : Math.Max(0, Math.Min(1, ((p._x - a._x) * dx + (p._y - a._y) * dy + (p._z - a._z) * dz) / l));
                float x = a._x + dx * t - p._x, y = a._y + dy * t - p._y, z = a._z + dz * t - p._z;
                return (float)Math.Sqrt(x * x + y * y + z * z);
            }

            /// <summary>Up to 3 bones, weights adding up to 1: the closest bone, and others nearly as close (blended at joints).</summary>
            public List<(string Bone, float W)> Weights(Vector3 v) => Weights(v, out _);

            /// <summary>The same, plus how far along each chosen segment the point is (0 at its joint, 1 at the next).</summary>
            public List<(string Bone, float W)> Weights(Vector3 v, out Dictionary<string, float> along)
            {
                // arms stand out to the sides: nothing nearer the middle than an arm bone's own joint is part of that arm
                // (hair down the back, the sides of a jacket), so it isn't pulled along when the arm lifts
                var d = _segs.Where(s => !Arm(s.Name) || Math.Abs(v._x) >= Math.Abs(s.A._x) - 0.15f)
                    .Select(s => (s.Name, D: SegDist(v, s.A, s.B))).OrderBy(x => x.D).ToList();
                if (d.Count == 0) d = _segs.Select(s => (s.Name, D: SegDist(v, s.A, s.B))).OrderBy(x => x.D).ToList();
                along = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var s in _segs) if (d.Take(3).Any(x => x.Name == s.Name)) along[s.Name] = Along(v, s.A, s.B);
                float best = d[0].D;
                var near = d.Where(x => x.D <= best * 1.35f + 0.15f).Take(3).ToList();
                var w = near.Select(x => (x.Name, W: 1.0 / Math.Pow(x.D + 0.05, 4))).ToList();
                double total = w.Sum(x => x.W);
                var r = w.Select(x => (x.Name, (float)(x.W / total))).Where(x => x.Item2 >= 0.08f).ToList();
                float t2 = r.Sum(x => x.Item2);
                r = r.Select(x => (x.Name, (float)Math.Round(x.Item2 / t2, 3))).ToList();
                if (r.Count > 1) { float rest = 1 - r.Skip(1).Sum(x => x.Item2); r[0] = (r[0].Name, rest); }
                return r;
            }
        }
    }
}
