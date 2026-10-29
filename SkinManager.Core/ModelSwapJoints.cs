// Lining a model up with P+'s skeleton. The skeleton itself never moves (hits and hurtboxes hang on its bones, so
// moving them would change how the character plays). Instead you mark where your model's own joints are (its knees,
// elbows...) and the model is gently bent and stretched so those land on P+'s joints. Then it bends where P+ bends.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    /// <summary>One of P+'s body joints, where it is in the costume's default pose.</summary>
    public sealed class SwapJoint
    {
        public string Name;     // the bone's name (LKneeJ)
        public string Label;    // in plain words ("left knee"), null for joints without a dot
        public Vector3 Pos;
        public string To;       // the joint its bone runs to (null: none)
        public string Mirror;   // the same joint on the other side (null: a centre joint)
        public string Parent;   // the body joint it hangs from (null: the root)
        public bool Handle => Label != null;
    }

    /// <summary>A dab of the "keep with the body" brush (front view, P+'s units), or of the eraser.</summary>
    public sealed class PaintDab { public float X, Y, R; public bool Erase; }

    public static partial class ModelSwap
    {
        static readonly (string Pattern, string Label)[] Handles =
        {
            ("^HeadN$", "head"), ("^NeckN$", "neck"), ("^BustN$", "chest"), ("^WaistN$", "waist"), ("^HipN$", "hips"),
            ("^[LR]ShoulderJ$", "shoulder"), ("^[LR]ArmJ$", "elbow"), ("^[LR]HandN$", "wrist"),
            ("^[LR]LegJ$", "top of the leg"), ("^[LR]KneeJ$", "knee"), ("^[LR]FootJ$", "ankle"),
        };

        /// <summary>P+'s body joints in this costume (the ones a new model follows), the main ones with a plain name.</summary>
        public static List<SwapJoint> Joints(byte[] costume)
        {
            lock (BrawlGate.Lock)
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault() ?? throw new InvalidOperationException("That costume file has no model.");
                model.ApplyCHR(null, 0);
                var body = SkeletonRig.Body(model, ReferencePoints(model), out var parents) ?? throw new InvalidOperationException("This character's skeleton has too few body bones to line a model up with.");
                var list = body.Select(x => new SwapJoint { Name = x.Bone.Name, Pos = SkeletonRig.P(x.Bone), To = x.To?.Name, Parent = parents.TryGetValue(x.Bone.Name, out var pa) ? pa : null }).ToList();
                foreach (var j in list)
                {
                    var h = Handles.FirstOrDefault(x => System.Text.RegularExpressions.Regex.IsMatch(j.Name, x.Pattern));
                    if (h.Label == null) continue;
                    bool sided = SkeletonRig.Sided(j.Name);
                    j.Label = sided ? (j.Name[0] == 'L' ? "left " : "right ") + h.Label : h.Label;
                }
                // a skeleton named its own way: every body joint gets a dot, named by its bone
                if (list.Count(j => j.Handle) < 8) foreach (var j in list) j.Label = j.Name;
                var names = new HashSet<string>(list.Select(j => j.Name));
                foreach (var j in list)
                    if (SkeletonRig.Sided(j.Name))
                    {
                        string m = (j.Name[0] == 'L' ? "R" : "L") + j.Name.Substring(1);
                        if (names.Contains(m)) j.Mirror = m;
                    }
                return list;
            }
        }

        /// <summary>The model bent and stretched so the joints marked on it (bone name -> where that joint is on the
        /// model) land on P+'s. Joints without a mark stay put; the ones in between follow their neighbours.</summary>
        public static SwapMesh LineUp(SwapMesh source, List<SwapJoint> joints, IDictionary<string, Vector3> marks)
        {
            if (joints == null || marks == null || marks.Count == 0) return source;
            // how far each joint's part of the model moves
            var move = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (var j in joints.Where(j => j.Handle))
                move[j.Name] = marks.TryGetValue(j.Name, out var m) ? Sub(j.Pos, m) : new Vector3();
            if (move.Values.All(v => Len(v) < 1e-4f)) return source;
            var handles = joints.Where(j => j.Handle).ToList();
            foreach (var j in joints.Where(j => !j.Handle))
            {
                // in between: the two closest joints with a dot, the closer one counting more
                var near = handles.OrderBy(h => Dist2(h.Pos, j.Pos)).Take(2).ToList();
                double tw = 0; double x = 0, y = 0, z = 0;
                foreach (var h in near) { double w = 1 / Math.Max(1e-6, Dist2(h.Pos, j.Pos)); var d = move[h.Name]; x += d._x * w; y += d._y * w; z += d._z * w; tw += w; }
                move[j.Name] = new Vector3((float)(x / tw), (float)(y / tw), (float)(z / tw));
            }
            Vector3 Src(string name) { var j = joints.First(k => k.Name == name); return Sub(j.Pos, move[name]); }
            var byName = joints.ToDictionary(j => j.Name, j => j, StringComparer.Ordinal);
            var segs = joints.Select(j => (j.Name, Src(j.Name), j.To != null ? Src(j.To) : Src(j.Name))).ToList();
            var rig = SkeletonRig.From(segs);
            // each body part is turned (and lengthened or shortened along its bone) so its joints land on P+'s: a part keeps
            // its shape, like a suit put on a skeleton, instead of being smeared
            var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            Part PartOf(string name)
            {
                if (parts.TryGetValue(name, out var done)) return done;
                var j = byName[name];
                var a = Src(name); var d = j.Pos;
                Part r;
                if (j.To != null && Len(Sub(Src(j.To), a)) > 1e-4f && Len(Sub(byName[j.To].Pos, d)) > 1e-4f)
                    r = new Part(a, d, Sub(Src(j.To), a), Sub(byName[j.To].Pos, d));
                else
                {
                    // the end of a chain (a wrist, the head): turned like the part before it
                    var parent = joints.FirstOrDefault(k => k.To == name);
                    r = parent != null ? PartOf(parent.Name).MovedTo(a, d) : new Part(a, d, new Vector3(0, 1, 0), new Vector3(0, 1, 0));
                }
                return parts[name] = r;
            }
            foreach (var j in joints) PartOf(j.Name);
            var m2 = source.Copy();
            foreach (var p in m2.Parts)
                for (int i = 0; i < p.P.Count; i++)
                {
                    var v = p.P[i];
                    var w = rig.Weights(v);
                    float x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0;
                    foreach (var (bone, wt) in w)
                    {
                        var part = parts[bone];
                        var q = part.Point(v); var n = part.Turn(p.N[i]);
                        x += wt * q._x; y += wt * q._y; z += wt * q._z; nx += wt * n._x; ny += wt * n._y; nz += wt * n._z;
                    }
                    p.P[i] = new Vector3(x, y, z);
                    p.N[i] = Normalize(new Vector3(nx, ny, nz));
                }
            m2.Notes.Add("Lined up with P+'s joints.");
            return m2;
        }

        /// <summary>Which points of the placed model the brush covers (seen from the front; the last dab over a point decides).</summary>
        public static bool Painted(Vector3 v, IList<PaintDab> dabs)
        {
            bool on = false;
            if (dabs != null) foreach (var d in dabs) { float x = v._x - d.X, y = v._y - d.Y; if (x * x + y * y <= d.R * d.R) on = !d.Erase; }
            return on;
        }

        /// <summary>A test for the finished model's points: was this one painted? (placed and lined-up models have their points in the same order)</summary>
        public static Func<Vector3, bool> KeepTest(SwapMesh placed, SwapMesh lined, IList<PaintDab> dabs)
        {
            if (dabs == null || dabs.Count == 0) return null;
            const float cell = 0.01f;
            var cells = new Dictionary<(int, int, int), List<Vector3>>();
            (int, int, int) K(Vector3 v) => ((int)Math.Floor(v._x / cell), (int)Math.Floor(v._y / cell), (int)Math.Floor(v._z / cell));
            int n = 0;
            for (int pi = 0; pi < placed.Parts.Count; pi++)
                for (int i = 0; i < placed.Parts[pi].P.Count; i++)
                    if (Painted(placed.Parts[pi].P[i], dabs))
                    {
                        var w = lined.Parts[pi].P[i];
                        if (!cells.TryGetValue(K(w), out var l)) cells[K(w)] = l = new List<Vector3>();
                        l.Add(w); n++;
                    }
            if (n == 0) return null;
            return v =>
            {
                var (x, y, z) = K(v);
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++) for (int c = -1; c <= 1; c++)
                    if (cells.TryGetValue((x + a, y + b, z + c), out var l)) foreach (var w in l) if (Dist2(w, v) < 4e-6f) return true;
                return false;
            };
        }

        /// <summary>Dots placed by measuring the model (made standing, arms out): each limb's middle line from slices of it, the
        /// body's middle from its thighs, and the depth of every joint, which can't be seen (or dragged) from the front.
        /// A joint the model gives no clear answer for keeps P+'s spot.</summary>
        public static Dictionary<string, Vector3> AutoMarks(SwapMesh placed, List<SwapJoint> joints)
        {
            var r = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var v = placed.Parts.SelectMany(p => p.P).ToList();
            if (v.Count < 50 || joints == null) return r;
            var box = placed.Box();
            float h = box.Max._y - box.Min._y;
            var by = joints.ToDictionary(j => j.Name, j => j, StringComparer.Ordinal);
            SwapJoint J(string n) => by.TryGetValue(n, out var j) ? j : null;
            // the middle of the body: the thighs (below any long hair)
            var thighs = v.Where(p => p._y > box.Min._y + h * 0.42f && p._y < box.Min._y + h * 0.5f).ToList();
            if (thighs.Count < 10) return r;
            float mid = (thighs.Min(p => p._x) + thighs.Max(p => p._x)) / 2;
            (float X, float Y, float Z)? Slice(Func<Vector3, bool> keep)
            {
                var q = v.Where(keep).ToList();
                if (q.Count < 4) return null;
                return ((q.Min(p => p._x) + q.Max(p => p._x)) / 2, (q.Min(p => p._y) + q.Max(p => p._y)) / 2, (q.Min(p => p._z) + q.Max(p => p._z)) / 2);
            }
            foreach (int side in new[] { 1, -1 })
            {
                string S = side > 0 ? "L" : "R";
                // legs: each one's middle at the height of P+'s joint (an ankle a bit higher, above the shoe)
                foreach (var (name, up) in new[] { ("LegJ", 0f), ("KneeJ", 0f), ("FootJ", 0.6f) })
                {
                    var j = J(S + name); if (j == null) continue;
                    float y = j.Pos._y + up;
                    var c = Slice(p => Math.Abs(p._y - y) < 0.3f && (p._x - mid) * side > 0.05f);
                    if (c != null) r[j.Name] = new Vector3(c.Value.X, j.Pos._y, c.Value.Z);
                }
                // arms (out to the sides): P+'s arm joints spread to this model's reach, each at the arm's middle there
                var sh = J(S + "ShoulderJ"); var el = J(S + "ArmJ"); var wr = J(S + "HandN");
                if (sh == null || el == null || wr == null) continue;
                var reach = v.Where(p => (p._x - mid) * side > 0).Select(p => (p._x - mid) * side).DefaultIfEmpty(0).Max();
                float theirs = Math.Abs(wr.Pos._x) * 1.2f;   // P+'s fingertips are about a fifth past the wrist
                float k = reach > 0 && theirs > 0 ? Math.Max(0.7f, Math.Min(1.5f, reach / theirs)) : 1;
                var armMid = Slice(p => Math.Abs((p._x - mid) * side - Math.Abs(el.Pos._x) * k) < 0.3f && p._y > sh.Pos._y - h * 0.12f);
                foreach (var j in new[] { sh, el, wr })
                {
                    float x = mid + side * Math.Abs(j.Pos._x) * k;
                    var c = j == sh ? armMid : Slice(p => Math.Abs(p._x - x) < 0.3f && p._y > sh.Pos._y - h * 0.12f);
                    if (c != null) r[j.Name] = new Vector3(x, c.Value.Y, c.Value.Z);
                }
            }
            // the middle line: hips between the legs, chest, neck and head kept where P+ has them below the shoulders
            Vector3? M(string n) => r.TryGetValue(n, out var m) ? m : (Vector3?)null;
            var legL = M("LLegJ"); var legR = M("RLegJ"); var shL = M("LShoulderJ"); var shR = M("RShoulderJ");
            if (legL != null && legR != null && shL != null && shR != null)
            {
                float hipZ = (legL.Value._z + legR.Value._z) / 2, shY = (shL.Value._y + shR.Value._y) / 2, shZ = (shL.Value._z + shR.Value._z) / 2;
                float theirShY = (J("LShoulderJ").Pos._y + J("RShoulderJ").Pos._y) / 2;
                foreach (var j in joints.Where(j => j.Handle && !SkeletonRig.Sided(j.Name)))
                {
                    bool upper = j.Pos._y > theirShY - 0.01f;
                    float y = upper ? shY + (j.Pos._y - theirShY) : j.Pos._y;   // neck and head ride on the shoulders
                    if (!upper && j.Pos._y > J("HipN")?.Pos._y + 0.5f) y = j.Pos._y + (shY - theirShY) * (j.Pos._y - J("HipN").Pos._y) / Math.Max(0.1f, theirShY - J("HipN").Pos._y);
                    float t = Math.Max(0, Math.Min(1, (j.Pos._y - J("HipN").Pos._y) / Math.Max(0.1f, theirShY - J("HipN").Pos._y)));
                    r[j.Name] = new Vector3(mid, y, hipZ + (shZ - hipZ) * t);
                }
            }
            return r;
        }

        /// <summary>One body part's move: turned from the model's bone direction to P+'s, stretched along it, moved onto P+'s joint.</summary>
        sealed class Part
        {
            readonly Vector3 _from, _to, _axis; readonly float[] _r; readonly float _stretch;
            Part(Vector3 from, Vector3 to, float[] r, Vector3 axis, float stretch) { _from = from; _to = to; _r = r; _axis = axis; _stretch = stretch; }
            public Part(Vector3 from, Vector3 to, Vector3 mine, Vector3 theirs)
            {
                _from = from; _to = to;
                var a = Normalize(mine); var b = Normalize(theirs);
                _axis = a;
                _stretch = Math.Max(0.5f, Math.Min(2f, Len(theirs) / Math.Max(1e-4f, Len(mine))));
                _r = Rotation(a, b);
            }
            public Part MovedTo(Vector3 from, Vector3 to) => new Part(from, to, _r, _axis, 1f);
            public Vector3 Turn(Vector3 n) => Mul(_r, n);
            public Vector3 Point(Vector3 p)
            {
                var o = Sub(p, _from);
                float along = o._x * _axis._x + o._y * _axis._y + o._z * _axis._z;
                float k = (_stretch - 1) * along;
                o = new Vector3(o._x + _axis._x * k, o._y + _axis._y * k, o._z + _axis._z * k);
                var t = Mul(_r, o);
                return new Vector3(_to._x + t._x, _to._y + t._y, _to._z + t._z);
            }
            static Vector3 Mul(float[] r, Vector3 v) => new Vector3(r[0] * v._x + r[1] * v._y + r[2] * v._z, r[3] * v._x + r[4] * v._y + r[5] * v._z, r[6] * v._x + r[7] * v._y + r[8] * v._z);
            /// <summary>The smallest turn taking direction a to direction b (both length 1).</summary>
            static float[] Rotation(Vector3 a, Vector3 b)
            {
                var c = Cross(a, b);
                float cos = a._x * b._x + a._y * b._y + a._z * b._z, sin = Len(c);
                if (sin < 1e-6f)
                {
                    if (cos > 0) return new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
                    // opposite: half a turn around any axis at right angles to a
                    var any = Math.Abs(a._x) < 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
                    c = Normalize(Cross(a, any)); sin = 0; cos = -1;
                }
                else c = new Vector3(c._x / sin, c._y / sin, c._z / sin);
                float t = 1 - cos, x = c._x, y = c._y, z = c._z;
                return new float[] { t * x * x + cos, t * x * y - sin * z, t * x * z + sin * y,
                                     t * x * y + sin * z, t * y * y + cos, t * y * z - sin * x,
                                     t * x * z - sin * y, t * y * z + sin * x, t * z * z + cos };
            }
        }

        /// <summary>Marks saved as text (one "bone x y z" per line) and read back.</summary>
        public static string SaveMarks(IDictionary<string, Vector3> marks, IList<PaintDab> dabs = null) =>
            string.Join("\n", marks.Select(kv => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}", kv.Key, kv.Value._x, kv.Value._y, kv.Value._z))
                .Concat((dabs ?? new List<PaintDab>()).Select(d => string.Format(CultureInfo.InvariantCulture, "~{0} {1} {2} {3}", d.Erase ? "erase" : "paint", d.X, d.Y, d.R))));

        public static List<PaintDab> ReadDabs(string text)
        {
            var r = new List<PaintDab>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var t = line.Trim().Split(' ');
                if (t.Length == 4 && (t[0] == "~paint" || t[0] == "~erase") && float.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && float.TryParse(t[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rad))
                    r.Add(new PaintDab { X = x, Y = y, R = rad, Erase = t[0] == "~erase" });
            }
            return r;
        }

        public static Dictionary<string, Vector3> ReadMarks(string text)
        {
            var r = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (var line in (text ?? "").Split('\n'))
            {
                var t = line.Trim().Split(' ');
                if (t.Length == 4 && !t[0].StartsWith("~") && float.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && float.TryParse(t[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    r[t[0]] = new Vector3(x, y, z);
            }
            return r;
        }
    }
}
