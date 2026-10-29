// Rigging by what each point can see (the way Blender's automatic weights work): a point follows the nearest body bone it can
// see from inside the model, never one on the far side of the model's own surface (hair hanging beside a sleeve can't see the
// arm bone inside that sleeve). Points that see no bone take the bones of what they're joined to, along the surface (hair
// joined to the head). Then the weights are smoothed along the surface, so joints bend gradually instead of tearing.
using System;
using System.Collections.Generic;
using System.Linq;
using BrawlLib.Internal;

namespace SkinManager.Core
{
    public static partial class ModelSwap
    {
        public static int HeatSmoothing = 0;      // (unused: blending is by distance at the joints)
        public static float BlendZone = 0.25f;    // how far a joint's bend spreads, as a share of the shorter bone
        public static int HeatCandidates = 6;     // nearest bones tried for sight
        public static float CloseEnough = 0.7f;
        public static bool ChainForMiddle = false;
        public static bool HangingHair = true;
        public static bool HangRigid = true;
        public static float BackDepth = 0.8f;
        public static float CentreSmooth = 0f;    // how far (along the surface) the middle of the body's bones fade into each other      // behind the spine by more than this share of the chest's depth: hanging, not body     // hair hanging below the head follows the neck and chest, not the head   // the middle of the body (and hair off the head) bends as one chain   // this near a bone, a point follows it without the sight check

        /// <summary>Weights for the model's points (as placed on P+'s skeleton): point -> bones and weights. keepWithBody: painted points.</summary>
        public static Func<Vector3, List<(string Bone, float W)>> SightWeights(SwapMesh mesh, List<SwapJoint> joints, Func<Vector3, bool> keepWithBody = null)
        {
            if (joints == null || joints.Count < 6) return null;
            // 1. points joined where they touch (the file repeats a point for each picture seam)
            var nodes = new List<Vector3>();
            var cell = new Dictionary<(long, long, long), List<int>>();
            const float tol = 1e-4f, q = 0.01f;
            int Node(Vector3 v)
            {
                var k = ((long)Math.Floor(v._x / q), (long)Math.Floor(v._y / q), (long)Math.Floor(v._z / q));
                for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++) for (long c = -1; c <= 1; c++)
                    if (cell.TryGetValue((k.Item1 + a, k.Item2 + b, k.Item3 + c), out var l)) foreach (var i in l) if (Dist2(nodes[i], v) < tol * tol) return i;
                if (!cell.TryGetValue(k, out var list)) cell[k] = list = new List<int>();
                list.Add(nodes.Count); nodes.Add(v); return nodes.Count - 1;
            }
            var tris = new List<(int A, int B, int C)>();
            foreach (var p in mesh.Parts) for (int i = 0; i + 2 < p.P.Count; i += 3) tris.Add((Node(p.P[i]), Node(p.P[i + 1]), Node(p.P[i + 2])));
            int n = nodes.Count;
            var nb = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
            foreach (var t in tris) { nb[t.A].Add(t.B); nb[t.A].Add(t.C); nb[t.B].Add(t.A); nb[t.B].Add(t.C); nb[t.C].Add(t.A); nb[t.C].Add(t.B); }

            // 2. the bones as segments (P+'s joints)
            var at = joints.ToDictionary(j => j.Name, j => j.Pos, StringComparer.Ordinal);
            // an end joint (a wrist with nothing after it) is a bone pointing on from the one before it, a hand's length long:
            // fingers past the wrist go with the hand, not the forearm
            Vector3 End(SwapJoint j)
            {
                if (j.To != null && at.ContainsKey(j.To)) return at[j.To];
                var parent = joints.FirstOrDefault(k => k.To == j.Name);
                if (parent == null) return j.Pos;
                var d = Sub(j.Pos, parent.Pos);
                return new Vector3(j.Pos._x + d._x * 0.55f, j.Pos._y + d._y * 0.55f, j.Pos._z + d._z * 0.55f);
            }
            var segs = joints.Select(j => (j.Name, A: j.Pos, B: End(j), Centre: !SkeletonRig.Sided(j.Name))).ToList();

            // 3. triangles in a grid, to test sight lines quickly
            var box = mesh.Box();
            float gs = 0.5f;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) G(float x, float y, float z) => ((int)Math.Floor((x - box.Min._x) / gs), (int)Math.Floor((y - box.Min._y) / gs), (int)Math.Floor((z - box.Min._z) / gs));
            for (int i = 0; i < tris.Count; i++)
            {
                var (a, b, c) = (nodes[tris[i].A], nodes[tris[i].B], nodes[tris[i].C]);
                var lo = G(Math.Min(a._x, Math.Min(b._x, c._x)), Math.Min(a._y, Math.Min(b._y, c._y)), Math.Min(a._z, Math.Min(b._z, c._z)));
                var hi = G(Math.Max(a._x, Math.Max(b._x, c._x)), Math.Max(a._y, Math.Max(b._y, c._y)), Math.Max(a._z, Math.Max(b._z, c._z)));
                for (int x = lo.Item1; x <= hi.Item1; x++) for (int y = lo.Item2; y <= hi.Item2; y++) for (int z = lo.Item3; z <= hi.Item3; z++)
                {
                    if (!grid.TryGetValue((x, y, z), out var l)) grid[(x, y, z)] = l = new List<int>();
                    l.Add(i);
                }
            }
            bool Blocked(int from, Vector3 to)
            {
                var p = nodes[from];
                var d = Sub(to, p); float len = Len(d);
                if (len < 1e-4f) return false;
                var dir = new Vector3(d._x / len, d._y / len, d._z / len);
                var seen = new HashSet<int>();
                for (float s = 0; s <= len + gs; s += gs * 0.5f)
                {
                    float t = Math.Min(s, len);
                    var c = G(p._x + dir._x * t, p._y + dir._y * t, p._z + dir._z * t);
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    {
                        if (!grid.TryGetValue((c.Item1 + x, c.Item2 + y, c.Item3 + z), out var l)) continue;
                        foreach (var ti in l)
                        {
                            if (!seen.Add(ti)) continue;
                            var tr = tris[ti];
                            if (tr.A == from || tr.B == from || tr.C == from) continue;
                            float hit = Hit(p, dir, nodes[tr.A], nodes[tr.B], nodes[tr.C]);
                            if (hit > 1e-3f && hit < len - 1e-3f) return true;
                        }
                    }
                }
                return false;
            }

            // how far the body reaches in front of the spine (the chest); anything much further BEHIND the spine than that is not
            // the body but something hanging behind it (long hair), and doesn't take the hips, waist or chest by sight
            var trunk = segs.Where(x => x.Centre).ToList();
            float backLimit = float.MaxValue;
            if (trunk.Count >= 3)
            {
                float yLo = trunk.Min(x => x.A._y), yHi = trunk.OrderByDescending(x => x.A._y).Skip(1).First().A._y;   // hips up to the neck
                float spineZ = trunk.Average(x => x.A._z);
                var front = nodes.Where(p => p._y > yLo && p._y < yHi && p._z > spineZ).Select(p => p._z - spineZ).OrderBy(z => z).ToList();
                if (front.Count > 20) backLimit = BackDepth * front[front.Count * 9 / 10];
            }
            bool Behind(Vector3 p, int s) => segs[s].Centre && segs[s].Name != trunk.OrderByDescending(x => x.A._y).First().Name && SkeletonRig.SegDist(p, segs[s].A, segs[s].B) >= 0 && (segs[s].A._z - p._z) > backLimit;

            // 4. each point: the nearest bone it can see
            var label = new int[n];
            var keep = new bool[n];
            for (int i = 0; i < n; i++)
            {
                label[i] = -1;
                keep[i] = keepWithBody != null && keepWithBody(nodes[i]);
                var v = nodes[i];
                var order = Enumerable.Range(0, segs.Count).Where(s => !keep[i] || segs[s].Centre)
                    .Select(s => (s, D: SkeletonRig.SegDist(v, segs[s].A, segs[s].B))).OrderBy(x => x.D).Take(HeatCandidates).ToList();
                foreach (var (s, dist0) in order)
                {
                    if (Behind(v, s)) continue;   // hanging behind the back: not the body
                    if (dist0 < CloseEnough) { label[i] = s; break; }   // right by a bone (a finger by the hand bone): no need to check sight
                    float t = SkeletonRig.Along(v, segs[s].A, segs[s].B);
                    var pt = new Vector3(segs[s].A._x + (segs[s].B._x - segs[s].A._x) * t, segs[s].A._y + (segs[s].B._y - segs[s].A._y) * t, segs[s].A._z + (segs[s].B._z - segs[s].A._z) * t);
                    if (!Blocked(i, pt)) { label[i] = s; break; }
                }
            }
            // 5. points that see no bone: the bone of what they're joined to (nearest along the surface)
            var dist = Enumerable.Repeat(float.MaxValue, n).ToArray();
            var heap = new SortedSet<(float D, int I)>();
            for (int i = 0; i < n; i++) if (label[i] >= 0) { dist[i] = 0; heap.Add((0, i)); }
            while (heap.Count > 0)
            {
                var (d, i) = heap.Min; heap.Remove(heap.Min);
                foreach (var k in nb[i])
                {
                    if (keep[k] && !segs[label[i]].Centre) continue;
                    if (Behind(nodes[k], label[i])) continue;   // hanging behind the back doesn't take the body's bones from the jacket either
                    float nd = d + Len(Sub(nodes[k], nodes[i]));
                    if (nd < dist[k]) { if (dist[k] < float.MaxValue) heap.Remove((dist[k], k)); dist[k] = nd; label[k] = label[i]; heap.Add((nd, k)); }
                }
            }
            for (int i = 0; i < n; i++)
                if (label[i] < 0)   // a loose piece seeing nothing: nearest bone
                    label[i] = Enumerable.Range(0, segs.Count).OrderBy(s => SkeletonRig.SegDist(nodes[i], segs[s].A, segs[s].B)).First();

            // 6. blended at the joints, over a real distance: a quarter of the shorter of the two bones meeting there
            float SegLen(int k) => Len(Sub(segs[k].B, segs[k].A));
            var index = Enumerable.Range(0, segs.Count).ToDictionary(k => segs[k].Name, k => k, StringComparer.Ordinal);
            var byName = joints.ToDictionary(x => x.Name, x => x, StringComparer.Ordinal);
            var result = new Dictionary<int, List<(string, float)>>();
            // the middle of the body (head, neck, chest, waist, hips) smoothed along the surface over a real distance, so where
            // two of them meet on something that isn't a joint (hair lying down the back: head above, chest below) it fades
            // from one to the other instead of tearing
            var centreMix = new Dictionary<string, float>[n];
            if (CentreSmooth > 0)
                for (int i = 0; i < n; i++)
                {
                    if (!segs[label[i]].Centre) continue;
                    var near = new Dictionary<int, float> { [i] = 0 };
                    var open = new SortedSet<(float, int)> { (0, i) };
                    while (open.Count > 0)
                    {
                        var (d, a) = open.Min; open.Remove(open.Min);
                        foreach (var b in nb[a])
                        {
                            float nd = d + Len(Sub(nodes[b], nodes[a]));
                            if (nd > CentreSmooth || (near.TryGetValue(b, out float od) && od <= nd)) continue;
                            if (near.ContainsKey(b)) open.Remove((near[b], b));
                            near[b] = nd; open.Add((nd, b));
                        }
                    }
                    var mix = new Dictionary<string, float>();
                    foreach (var kv in near)
                    {
                        if (!segs[label[kv.Key]].Centre) continue;
                        float w = 1 - kv.Value / CentreSmooth;
                        mix.TryGetValue(segs[label[kv.Key]].Name, out float o); mix[segs[label[kv.Key]].Name] = o + w;
                    }
                    float tot = mix.Values.Sum();
                    if (mix.Count > 1 && tot > 0) centreMix[i] = mix.ToDictionary(x => x.Key, x => x.Value / tot);
                }

            // the middle of the body as one chain, low to high (hips, waist, chest, neck, head)
            var chain = Enumerable.Range(0, segs.Count).Where(k => segs[k].Centre).OrderBy(k => segs[k].A._y).ToList();
            for (int i = 0; i < n; i++)
            {
                int L = label[i]; var v = nodes[i];
                if (centreMix[i] != null)
                {
                    result[i] = centreMix[i].Where(x => x.Value > 0.04f).OrderByDescending(x => x.Value).Take(3).Select(x => (x.Key, x.Value)).ToList();
                    float t3 = result[i].Sum(x => x.Item2); result[i] = result[i].Select(x => (x.Item1, x.Item2 / t3)).ToList();
                    continue;
                }
                bool hanging = HangingHair && chain.Count >= 3 && L == chain[chain.Count - 1] && v._y < segs[L].A._y && v._z < segs[L].A._z;   // (behind the head: the face is in front)   // tied to the head but below it: hair hanging down
                if (hanging && HangRigid)
                {
                    // hair hanging below the neck: one piece with the neck (no stretching), blended with the head just at neck level
                    int neck = chain[chain.Count - 2];
                    float below = segs[neck].A._y - v._y, band = 0.12f * Math.Max(0.5f, segs[L].A._y - segs[chain[0]].A._y);
                    float toHead = Math.Max(0, Math.Min(0.5f, 0.5f * (1 - below / band)));
                    result[i] = toHead > 0.02f ? new List<(string, float)> { (segs[neck].Name, 1 - toHead), (segs[L].Name, toHead) } : new List<(string, float)> { (segs[neck].Name, 1f) };
                    continue;
                }
                if ((ChainForMiddle && segs[L].Centre || hanging) && chain.Count >= 2)
                {
                    // whatever follows the middle of the body (the torso, and hair hanging off the head) bends with it like a
                    // rope: by height, blended between the two joints it's between, so long hair never tears in two
                    var cw = new List<(string, float)>();
                    if (v._y <= segs[chain[0]].A._y) cw.Add((segs[chain[0]].Name, 1f));
                    else if (v._y >= segs[chain[chain.Count - 1]].A._y) cw.Add((segs[chain[chain.Count - 1]].Name, 1f));
                    else
                        for (int c = 0; c + 1 < chain.Count; c++)
                        {
                            float y0 = segs[chain[c]].A._y, y1 = segs[chain[c + 1]].A._y;
                            if (v._y < y0 || v._y > y1) continue;
                            float h = (v._y - y0) / Math.Max(1e-3f, y1 - y0);
                            if (c + 1 == chain.Count - 1) h = Math.Min(1, h / 0.4f);   // the head: rigid above the bottom of it
                            if (h < 0.98f) cw.Add((segs[chain[c]].Name, 1 - h));
                            if (h > 0.02f) cw.Add((segs[chain[c + 1]].Name, h));
                            break;
                        }
                    result[i] = cw;
                    continue;
                }
                var jt = byName[segs[L].Name];
                float len = Math.Max(1e-3f, SegLen(L)), t = SkeletonRig.Along(v, segs[L].A, segs[L].B);
                var w = new Dictionary<int, float> { [L] = 1f };
                void Blend(string other, float along)
                {
                    if (other == null || !index.TryGetValue(other, out int o)) return;
                    if (keep[i] && !segs[o].Centre) return;
                    float zone = BlendZone * Math.Min(len, Math.Max(1e-3f, SegLen(o)));
                    if (along >= zone) return;
                    float k = 0.5f * (1 - along / zone);
                    w[L] -= k; w.TryGetValue(o, out float was); w[o] = was + k;
                }
                Blend(jt.To, (1 - t) * len);        // towards the next bone (elbow to forearm...)
                Blend(jt.Parent, t * len);          // back towards the one it hangs from
                var top = w.Where(kv => kv.Value > 0.01f).OrderByDescending(kv => kv.Value).Take(3).ToList();
                float tot = top.Sum(kv => kv.Value);
                result[i] = top.Select(kv => (segs[kv.Key].Name, kv.Value / tot)).ToList();
            }
            return v =>
            {
                var k = ((long)Math.Floor(v._x / q), (long)Math.Floor(v._y / q), (long)Math.Floor(v._z / q));
                int best = -1; float bd = float.MaxValue;
                for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++) for (long c = -1; c <= 1; c++)
                    if (cell.TryGetValue((k.Item1 + a, k.Item2 + b, k.Item3 + c), out var l)) foreach (var i in l) { float dd = Dist2(nodes[i], v); if (dd < bd) { bd = dd; best = i; } }
                return best >= 0 && bd < 1e-4f ? result[best] : null;
            };
        }

        /// <summary>Where a ray (from p, direction d) meets a triangle, or -1.</summary>
        static float Hit(Vector3 p, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = Sub(b, a); var e2 = Sub(c, a);
            var h = Cross(d, e2); float det = e1._x * h._x + e1._y * h._y + e1._z * h._z;
            if (Math.Abs(det) < 1e-9f) return -1;
            float f = 1 / det; var s = Sub(p, a);
            float u = f * (s._x * h._x + s._y * h._y + s._z * h._z);
            if (u < 0 || u > 1) return -1;
            var qv = Cross(s, e1);
            float v = f * (d._x * qv._x + d._y * qv._y + d._z * qv._z);
            if (v < 0 || u + v > 1) return -1;
            return f * (e2._x * qv._x + e2._y * qv._y + e2._z * qv._z);
        }
    }
}
