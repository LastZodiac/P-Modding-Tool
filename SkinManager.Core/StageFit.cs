// Fitting a stage mod's scenery onto P+'s floors piece by piece. LineUp/MoveLooks (StageAlign.cs) handle a map that is
// moved or resized as a whole; FitPieces handles every other way a map's looks can miss P+'s floors:
//  - pieces in other places: each of the map's floors is paired with the P+ floor in the same place (main floor with
//    main floor, a platform with the platform on the same side at about the same height), and the scenery that belongs
//    to that floor is moved onto P+'s;
//  - a main floor of another width: the scenery of the main floor is stretched (or narrowed) sideways to P+'s width;
//  - a floor the map draws that P+ doesn't have: its pieces are shrunk to nothing, and so is a moving platform's art
//    when P+ has no such platform (it would float around with nothing to stand on);
//  - a floor of P+'s the map draws nothing for: P+'s own pieces for that floor stay in sight.
// Which pieces belong to a floor is read from the models themselves: a model's triangles are split into the separate
// pieces they make up (one platform built into the same mesh as the whole stage is still its own piece), and a piece
// belongs to a floor when its top is that floor's top (or it hangs right under it). Scenery built as one piece with
// everything else is bent near each floor only, fading out over a few units.
// Only looks change: collision, blast zones, spawns, the models collision rides on and the parts the stage program
// names stay exactly as they were, and that's checked before a file is handed back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BrawlLib.Internal;
using BrawlLib.Modeling;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static partial class StageCheck
    {
        // ----- floors -----

        /// <summary>One floor: the walkable surfaces of one collision piece that join up, in stage space.</summary>
        sealed class Run
        {
            public float X0, X1, Y;          // Y: its height, averaged along it
            public float Ymin, Ymax;         // its lowest and highest point (a sloped or curved floor: Corneria's Great Fox)
            public bool Moving;          // rides on a part that moves (its place is where the part starts)
            public float SX, SY;         // a moving floor: its middle in stage space when the match starts
            public bool Own;             // P+'s, on a part of P+'s that keeps P+'s own look (Corneria's Great Fox, an Arwing)
            public List<string> By = new List<string>();   // P+'s: its parts that keep P+'s look and draw this floor
            public string Model, Bone;   // what it rides on, if anything
            public List<(float x1, float y1, float x2, float y2)> S = new List<(float, float, float, float)>();
            public float W => X1 - X0;
            public float Cx => (X0 + X1) / 2;
            /// <summary>Works out its ends, average height and lowest/highest point once its segments are in.</summary>
            public void Done()
            {
                X0 = S.Min(s => Math.Min(s.x1, s.x2)); X1 = S.Max(s => Math.Max(s.x1, s.x2));
                double t = 0, l = 0;
                foreach (var s in S) { double len = Math.Max(0.01, Math.Abs(s.x2 - s.x1)); t += len * (s.y1 + s.y2) / 2; l += len; }
                Y = (float)(t / l);
                Ymin = S.Min(s => Math.Min(s.y1, s.y2)); Ymax = S.Max(s => Math.Max(s.y1, s.y2));
            }
            /// <summary>The floor's height at x (its nearest end past either end).</summary>
            public float YAt(float x)
            {
                float best = float.MaxValue, y = Y;
                foreach (var s in S)
                {
                    float lo = Math.Min(s.x1, s.x2), hi = Math.Max(s.x1, s.x2);
                    if (x >= lo && x <= hi) return hi - lo < 1e-4f ? Math.Max(s.y1, s.y2) : s.y1 + (s.y2 - s.y1) * (x - s.x1) / (s.x2 - s.x1);
                    float d = Math.Min(Math.Abs(x - lo), Math.Abs(x - hi));
                    if (d < best) { best = d; y = Math.Abs(x - s.x1) < Math.Abs(x - s.x2) ? s.y1 : s.y2; }
                }
                return y;
            }
            public override string ToString() => $"[{X0:0.#}..{X1:0.#} @{Y:0.#}{(Moving ? " moving " + Model + "/" + Bone : "")}]";
            /// <summary>Where the floor's scenery sits once the map was moved as a whole (scaled by s, shifted by x, y).</summary>
            public void Move(float s, float x, float y)
            {
                if (Moving) { SX = SX * s + x; SY = SY * s + y; return; }
                S = S.Select(f => (f.x1 * s + x, f.y1 * s + y, f.x2 * s + x, f.y2 * s + y)).ToList();
                Done();
            }
        }

        /// <summary>Names of the bones some animation in the file moves.</summary>
        static HashSet<string> AnimatedNames(ResourceNode root) =>
            new HashSet<string>(root.FindChildrenByType(null, ResourceType.CHR0).SelectMany(c => c.Children).Select(e => e.Name ?? ""), StringComparer.Ordinal);

        static bool Animated(MDL0BoneNode b, HashSet<string> names)
        {
            for (var x = b; x != null; x = x.Parent as MDL0BoneNode) if (names.Contains(x.Name)) return true;
            return false;
        }

        static MDL0BoneNode BoneOf(ResourceNode root, string model, string bone)
        {
            var m = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault(x => x.Name == model);
            if (m == null) return null;
            m.Populate();
            try { m.ApplyCHR(null, 0); } catch { }
            return m.BoneCache.OfType<MDL0BoneNode>().FirstOrDefault(b => b.Name == bone);
        }

        /// <summary>A stage's floors. A floor riding on a part nothing animates is where that part puts it; one riding on
        /// a moving part is marked Moving (and kept in the part's own space).</summary>
        static List<Run> Runs(ResourceNode root, HashSet<string> animated)
        {
            var res = new List<Run>();
            bool Real(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) < 1e6f;
            foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                foreach (var o in col.Children.OfType<CollisionObject>())
                {
                    bool riding = !string.IsNullOrEmpty(o._boneName);
                    var bone = riding ? BoneOf(root, o._modelName, o._boneName) : null;
                    // riding on a part the file doesn't have (P+'s Delfino's Secret and Rainbow Road ride "Colission"): it stays where it is
                    bool moving = riding && bone != null && Animated(bone, animated);
                    var at = riding && bone != null ? bone.Matrix : Matrix.Identity;
                    // a part whose animation barely moves it (a stage that bobs a little) counts as fixed, where it starts
                    if (moving && bone != null && Steady(root, o._modelName, bone, out var start)) { moving = false; at = start; }
                    // a moving floor: where its part puts it when the match starts (its animation's first frame: BrawlLib plays
                    // an animation from frame 1, frame 0 is the resting pose the file stores)
                    var first = Matrix.Identity;
                    if (moving)
                    {
                        var m = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault(x => x.Name == o._modelName);
                        var anim = Moves(root, bone).FirstOrDefault();
                        try { m?.ApplyCHR(anim, anim == null ? 0 : 1); first = bone.Matrix; } catch { first = bone.Matrix; }
                        try { m?.ApplyCHR(null, 0); } catch { }
                    }
                    var segs = new List<(float x1, float y1, float x2, float y2)>();
                    foreach (var p in o._planes.Where(p => p._linkLeft != null && p._linkRight != null && (p._type & BrawlLib.SSBB.Types.CollisionPlaneType.Floor) != 0))
                    {
                        var a = new Vector3(p._linkLeft._rawValue._x, p._linkLeft._rawValue._y, 0);
                        var b = new Vector3(p._linkRight._rawValue._x, p._linkRight._rawValue._y, 0);
                        if (riding && !moving && bone != null) { a = at * a; b = at * b; }
                        if (Real(a._x) && Real(a._y) && Real(b._x) && Real(b._y)) segs.Add((a._x, a._y, b._x, b._y));
                    }
                    var used = new bool[segs.Count];
                    for (int i = 0; i < segs.Count; i++)
                    {
                        if (used[i]) continue;
                        var r = new Run { Moving = moving, Model = riding ? o._modelName : null, Bone = riding ? o._boneName : null };
                        var q = new Queue<int>(); q.Enqueue(i); used[i] = true;
                        while (q.Count > 0)
                        {
                            var s = segs[q.Dequeue()]; r.S.Add(s);
                            for (int j = 0; j < segs.Count; j++)
                                if (!used[j] && Touch(s, segs[j])) { used[j] = true; q.Enqueue(j); }
                        }
                        r.Done();
                        if (moving) { var c = first * new Vector3(r.Cx, r.Y, 0); r.SX = c._x; r.SY = c._y; }
                        if (r.W < 0.5f) continue;   // a step, not something to stand on
                        // the same floor in several collision sets counts once
                        if (!res.Any(x => Math.Abs(x.X0 - r.X0) < 0.5f && Math.Abs(x.X1 - r.X1) < 0.5f && Math.Abs(x.Y - r.Y) < 0.5f && x.Moving == r.Moving && (!r.Moving || (x.Model == r.Model && x.Bone == r.Bone)))) res.Add(r);
                    }
                }
            // one floor listed in two collision sets a unit or two apart (a stage's forms): the wider one counts
            res = res.Where(r => r.Moving || !res.Any(x => x != r && !x.Moving && Math.Abs(x.Y - r.Y) <= 2 && (x.W > r.W || (x.W == r.W && res.IndexOf(x) < res.IndexOf(r))) &&
                Math.Min(x.X1, r.X1) - Math.Max(x.X0, r.X0) >= 0.8f * r.W)).ToList();
            return res;
        }

        /// <summary>Does an animated bone stay within 6 units all through each animation that moves it (checked at 17 points of each)?
        /// start: where it mostly is.</summary>
        static bool Steady(ResourceNode root, string model, MDL0BoneNode bone, out Matrix start)
        {
            start = bone.Matrix;
            var m = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault(x => x.Name == model);
            // every animation that moves it or a bone it hangs from (Yoshi's Story: one moves the stage, another Randall)
            var anims = Moves(root, bone);
            if (m == null || anims.Count == 0) return false;
            try
            {
                // each animation on its own: two that set the same bone are versions of the stage, one plays at a time
                // (Corneria 64's ship: "Small" and "RealBig", each holding it still, at different sizes)
                var probes = new[] { new Vector3(-20, 0, 0), new Vector3(20, 0, 0), new Vector3(0, 20, 0) };
                List<(Vector3[] p, Matrix m)> first = null;
                foreach (var anim in anims)
                {
                    var at = new List<(Vector3[] p, Matrix m)>();
                    int frames = Math.Max(1, anim.FrameCount);
                    for (int i = 0; i <= 16; i++)
                    {
                        // an animation BrawlLib can't play counts as moving (it used to make every looks install on the
                        // stage fail: Steady is asked about P+'s own file too)
                        try { m.ApplyCHR(anim, Math.Max(1, i * frames / 16)); } catch { return false; }
                        at.Add((probes.Select(q => bone.Matrix * q).ToArray(), bone.Matrix));
                    }
                    for (int k = 0; k < probes.Length; k++)
                    {
                        float x0 = at.Min(a => a.p[k]._x), x1 = at.Max(a => a.p[k]._x), y0 = at.Min(a => a.p[k]._y), y1 = at.Max(a => a.p[k]._y);
                        if (x1 - x0 > 6 || y1 - y0 > 6) return false;
                    }
                    if (first == null) first = at;
                }
                // where it spends the match: the middle of the places the first animation (the bone's own) puts it
                start = first.OrderBy(a => a.p[2]._y).ThenBy(a => a.p[2]._x).ElementAt(first.Count / 2).m;
                return true;
            }
            finally { try { m.ApplyCHR(null, 0); } catch { } }
        }

        /// <summary>The animations that move a bone or one it hangs from, the ones that move the bone itself first.</summary>
        static List<CHR0Node> Moves(ResourceNode root, MDL0BoneNode bone) =>
            root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().Where(c => HasEntry(c, bone))
                .OrderBy(c => c.FindChild(bone.Name, false) != null ? 0 : 1).ToList();

        static bool HasEntry(CHR0Node anim, MDL0BoneNode b)
        {
            for (var x = b; x != null; x = x.Parent as MDL0BoneNode) if (anim.FindChild(x.Name, false) != null) return true;
            return false;
        }

        /// <summary>Do two floor segments join (an end of one within a unit of an end of the other)?</summary>
        static bool Touch((float x1, float y1, float x2, float y2) s, (float x1, float y1, float x2, float y2) t)
        {
            bool Near(float ax, float ay, float bx, float by) => Math.Abs(ax - bx) < 1 && Math.Abs(ay - by) < 1;
            return Near(s.x2, s.y2, t.x1, t.y1) || Near(s.x1, s.y1, t.x2, t.y2) || Near(s.x1, s.y1, t.x1, t.y1) || Near(s.x2, s.y2, t.x2, t.y2);
        }

        // ----- scenery pieces -----

        /// <summary>One drawn object of a model: its points (in its bone's space and in stage space, at rest) and the
        /// separate pieces its triangles make up.</summary>
        sealed class Piece
        {
            public MDL0Node Model; public MDL0ObjectNode Obj; public MDL0VertexNode Vn; public MDL0BoneNode Bone;
            public bool Animated;
            public bool Locked;          // can't be changed safely (its points are shared with an object on another bone)
            public Vector3[] Local, World;
            public Matrix Inv;           // stage space -> its bone's space, in the pose the points were read in
            public List<Comp> Comps = new List<Comp>();
            public string Key => Model.Name + "/" + Obj.Name;
        }

        /// <summary>Triangles of an object that join up (share points), with where they are.</summary>
        sealed class Comp
        {
            public Piece P;
            public List<int> Verts = new List<int>();
            public List<(int a, int b, int c)> Tris = new List<(int, int, int)>();
            public float X0 = float.MaxValue, X1 = float.MinValue, Y0 = float.MaxValue, Y1 = float.MinValue, Z0 = float.MaxValue, Z1 = float.MinValue;
            public int Floor = -1;   // the floor it belongs to as a whole (moves with it)
            public bool Bend;        // part of something bigger: bent near each floor instead
            public bool Hide;        // shrunk to nothing
            public override string ToString() => $"{P.Key}#{P.Comps.IndexOf(this)}[{X0:0}..{X1:0},{Y0:0}..{Y1:0},z{Z0:0}..{Z1:0}]";
        }

        // BrawlLib's triangle list of an object (not public). If a BrawlLib update renamed it, every object would be skipped
        // and the fit would quietly find nothing to fit: the stage then goes on as it was, as before the fit existed.
        static readonly FieldInfo TriField = typeof(PrimitiveManager).GetField("_triangles", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>The drawn objects of the models chosen, split into pieces. Objects bent by several bones at once, and
        /// objects sharing their points with an object on another bone, are left out (nothing here can move them safely).</summary>
        static List<Piece> Pieces(ResourceNode root, Func<MDL0Node, bool> use, HashSet<string> animated)
        {
            var list = new List<Piece>();
            var owner = new Dictionary<MDL0VertexNode, MDL0BoneNode>();
            var locked = new HashSet<MDL0VertexNode>();
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
            {
                if (!use(m)) continue;
                m.Populate();
                StartPose(root, m);
                foreach (var o in m.FindChild("Objects", false)?.Children.OfType<MDL0ObjectNode>() ?? Enumerable.Empty<MDL0ObjectNode>())
                {
                    var vn = o._vertexNode;
                    if (vn == null) continue;
                    var bone = o.MatrixNode as MDL0BoneNode;
                    var mgr = o._manager;
                    var prim = mgr == null ? null : TriField?.GetValue(mgr) as GLPrimitive;
                    if (mgr?._vertices == null || prim?._indices == null) continue;
                    var raw = new Dictionary<int, int>();   // corner of a triangle -> point of the object
                    Vector3[] local;
                    Piece p;
                    if (bone == null)
                    {
                        // bent by several bones at once: only looked at (where it's drawn at rest), never changed (its point
                        // numbers here count BrawlLib's merged points, not the file's: they must never be passed to Edits)
                        locked.Add(vn);
                        local = mgr._vertices.Select(v => v.WeightedPosition).ToArray();
                        p = new Piece { Model = m, Obj = o, Vn = vn, Bone = null, Local = local, World = local };
                        for (int vi = 0; vi < mgr._vertices.Count; vi++) foreach (int fp in mgr._vertices[vi].FaceDataIndices) raw[fp] = vi;
                    }
                    else
                    {
                        if (owner.TryGetValue(vn, out var ob) && ob != bone) locked.Add(vn); else owner[vn] = bone;
                        local = vn.Vertices;
                        if (local == null || local.Length == 0) continue;
                        p = new Piece { Model = m, Obj = o, Vn = vn, Bone = bone, Animated = Animated(bone, animated), Local = local, Inv = bone.InverseMatrix };
                        var at = bone.Matrix;
                        p.World = local.Select(v => at * v).ToArray();
                        foreach (var v in mgr._vertices)
                        {
                            int ri = v.Facepoints != null && v.Facepoints.Count > 0 ? v.Facepoints[0]._vertexIndex : -1;
                            foreach (int fp in v.FaceDataIndices) raw[fp] = ri;
                        }
                    }
                    var parent = new Dictionary<int, int>();
                    int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                    void Join(int x, int y) { if (!parent.ContainsKey(x)) parent[x] = x; if (!parent.ContainsKey(y)) parent[y] = y; int a = Find(x), b = Find(y); if (a != b) parent[a] = b; }
                    var tris = new List<(int, int, int)>();
                    var ind = prim._indices;
                    for (int i = 0; i + 2 < ind.Length; i += 3)
                    {
                        if (!raw.TryGetValue((int)ind[i], out int a) || !raw.TryGetValue((int)ind[i + 1], out int b) || !raw.TryGetValue((int)ind[i + 2], out int c)) continue;
                        if (a < 0 || b < 0 || c < 0 || a >= local.Length || b >= local.Length || c >= local.Length) continue;
                        Join(a, b); Join(b, c);
                        tris.Add((a, b, c));
                    }
                    var byRoot = new Dictionary<int, Comp>();
                    foreach (var t in tris)
                    {
                        int r = Find(t.Item1);
                        if (!byRoot.TryGetValue(r, out var comp)) { comp = byRoot[r] = new Comp { P = p }; p.Comps.Add(comp); }
                        comp.Tris.Add(t);
                    }
                    foreach (var comp in p.Comps)
                    {
                        comp.Verts = comp.Tris.SelectMany(t => new[] { t.a, t.b, t.c }).Distinct().ToList();
                        foreach (int v in comp.Verts)
                        {
                            var w = p.World[v];
                            comp.X0 = Math.Min(comp.X0, w._x); comp.X1 = Math.Max(comp.X1, w._x);
                            comp.Y0 = Math.Min(comp.Y0, w._y); comp.Y1 = Math.Max(comp.Y1, w._y);
                            comp.Z0 = Math.Min(comp.Z0, w._z); comp.Z1 = Math.Max(comp.Z1, w._z);
                        }
                    }
                    if (p.Comps.Count > 0) list.Add(p);
                }
            }
            foreach (var p in list) p.Locked = locked.Contains(p.Vn);
            return list;
        }

        /// <summary>How much of a floor a piece's top covers (0..1): the stretch of the floor under triangles lying
        /// within a few units of its height.</summary>
        static float Cover(Comp c, Run f, float tol = 4f)
        {
            if (c.X1 < f.X0 || c.X0 > f.X1 || c.Y1 < f.Ymin - tol || c.Y0 > f.Ymax + tol) return 0;
            var w = c.P.World;
            var spans = new List<(float a, float b)>();
            float yLo = f.Ymin - tol, yHi = f.Ymax + tol;
            foreach (var (a, b, cc) in c.Tris)
            {
                var A = w[a]; var B = w[b]; var C = w[cc];
                // quick outs, same answer as the full test below: a corner out of the floor's height range (YAt never leaves
                // Ymin..Ymax) or a triangle wholly beside the floor adds nothing (this runs for every piece against every floor)
                if (A._y < yLo || A._y > yHi || B._y < yLo || B._y > yHi || C._y < yLo || C._y > yHi) continue;
                if (Math.Max(A._x, Math.Max(B._x, C._x)) <= f.X0 || Math.Min(A._x, Math.Min(B._x, C._x)) >= f.X1) continue;
                if (Math.Abs(A._y - f.YAt(A._x)) > tol || Math.Abs(B._y - f.YAt(B._x)) > tol || Math.Abs(C._y - f.YAt(C._x)) > tol) continue;
                float lo = Math.Max(f.X0, Math.Min(A._x, Math.Min(B._x, C._x))), hi = Math.Min(f.X1, Math.Max(A._x, Math.Max(B._x, C._x)));
                if (hi > lo) spans.Add((lo, hi));
            }
            if (spans.Count == 0) return 0;
            spans.Sort((x, y) => x.a.CompareTo(y.a));
            float total = 0, curA = spans[0].a, curB = spans[0].b;
            foreach (var s in spans.Skip(1))
            {
                if (s.a > curB) { total += curB - curA; curA = s.a; curB = s.b; }
                else curB = Math.Max(curB, s.b);
            }
            total += curB - curA;
            return total / Math.Max(1e-3f, f.W);
        }

        /// <summary>The front-to-back reach of the main floor's own pieces (the stage's body; scenery far in front of it
        /// or behind it isn't part of it).</summary>
        static (float z0, float z1) Depth(List<Piece> pieces, Run main)
        {
            float z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var c in pieces.SelectMany(p => p.Comps))
                if (c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, main) >= 0.15f) { z0 = Math.Min(z0, c.Z0); z1 = Math.Max(z1, c.Z1); }
            return z0 > z1 ? (-30f, 30f) : (z0, z1);
        }

        /// <summary>Which floor each piece belongs to. A piece whose top is one floor's top (and that isn't much bigger than
        /// that floor) belongs to it; so does a piece with no top that hangs right under one platform, and a piece of the
        /// stage's body under the main floor. Pieces whose top runs across several floors, and big ones, are bent instead
        /// (if they aren't animated: an animated piece is never bent, only moved or hidden as a whole piece).</summary>
        static void Assign(List<Piece> pieces, List<Run> floors, int main, (float z0, float z1) depth)
        {
            var M = floors[main];
            var all = pieces.SelectMany(p => p.Comps).ToList();
            // tops first: a platform's top lies right on it (within 3 units) over at least half of it; the main floor's
            // within 4 units over a quarter of it (a stage's body is often built in several pieces)
            foreach (var c in all)
            {
                // (and reaches the line fighters stand on, front to back: the ruins far behind a platform aren't it)
                if (c.Z0 > 10 || c.Z1 < -10) continue;
                var tops = Enumerable.Range(0, floors.Count).Where(i => i == main ? Cover(c, floors[i]) >= 0.25f : Cover(c, floors[i], 3f) >= 0.5f).ToList();
                if (tops.Count == 1) { if (Inside(c, floors[tops[0]], tops[0] == main)) c.Floor = tops[0]; else c.Bend = true; }
                else if (tops.Count > 1) c.Bend = true;
            }
            // then pieces with no top of their own: right under a platform and part of the same model as its top (its
            // underside, its rim), or the stage's body under the main floor
            foreach (var c in all.Where(c => c.Floor < 0 && !c.Bend))
            {
                var under = Enumerable.Range(0, floors.Count).Where(i => i != main && Hangs(c, floors[i]) && all.Any(t => t.Floor == i && t.P.Model == c.P.Model)).ToList();
                if (under.Count == 1) c.Floor = under[0];
                else if (under.Count == 0 && Body(c, M, depth)) c.Floor = main;
                else c.Bend = true;
            }
            foreach (var c in all) if (c.P.Animated) c.Bend = false;
            // (an animated object's separate pieces still go with their floors: each is moved inside its bone's space, as
            // it is when the match starts, so the animation carries it along from there)
        }

        /// <summary>A piece with its top on a floor is that floor's if it doesn't reach far past it.</summary>
        static bool Inside(Comp c, Run f, bool main)
        {
            if (main) return c.X0 >= f.X0 - 0.5f * f.W && c.X1 <= f.X1 + 0.5f * f.W && c.Y1 <= f.Ymax + 30 && c.Y0 >= f.Ymin - 2 * f.W;
            float m = Math.Max(10, 0.5f * f.W);
            return c.X0 >= f.X0 - m && c.X1 <= f.X1 + m && c.Y1 <= f.Ymax + 15 && c.Y0 >= f.Ymin - 60;
        }

        /// <summary>A piece with no top of its own right under a platform (its underside, its rim).</summary>
        static bool Hangs(Comp c, Run f) => c.Z0 <= 15 && c.Z1 >= -15 && c.X0 >= f.X0 - 4 && c.X1 <= f.X1 + 4 && c.Y1 <= f.Ymax + 6 && c.Y0 >= f.Ymin - 40;

        /// <summary>A piece of the stage's body: under the main floor, about as wide, and at the same depth.</summary>
        static bool Body(Comp c, Run m, (float z0, float z1) d) =>
            c.X0 >= m.X0 - 0.15f * m.W && c.X1 <= m.X1 + 0.15f * m.W && c.Y1 <= m.Ymax + 4 && c.Y0 >= m.Ymin - 0.6f * m.W && c.Z1 >= d.z0 - 25 && c.Z0 <= d.z1 + 25;

        // ----- moving a floor's scenery onto P+'s -----

        /// <summary>Puts a map floor's scenery on a P+ floor: sideways stretch Sx around the map floor's middle, then
        /// onto the P+ floor's middle and height.</summary>
        sealed class Shift
        {
            public Run From, To; public float Sx, Dy;
            public Shift(Run from, Run to)
            {
                From = from; To = to;
                Sx = Math.Max(0.5f, Math.Min(2f, to.W / Math.Max(1e-3f, from.W)));
                if (Math.Abs(Sx - 1) < 0.01f) Sx = 1;
                Dy = to.Y - from.Y;
            }
            public Vector3 At(Vector3 w) => new Vector3(To.Cx + (w._x - From.Cx) * Sx, w._y + Dy, w._z);
            public bool Nothing => Sx == 1 && Math.Abs(To.Cx - From.Cx) < 0.3f && Math.Abs(Dy) < 0.3f;
        }

        static float Ramp(float v, float lo, float hi, float fall) =>
            v >= lo && v <= hi ? 1 : fall <= 0 ? 0 : Math.Max(0, 1 - (v < lo ? lo - v : v - hi) / fall);

        /// <summary>How much of a floor's move a point of a bigger piece takes: all of it next to the floor, fading out a
        /// few units away. Under the main floor it reaches all the way down (the stage's body).</summary>
        static float Pull(Vector3 w, Run f, bool main, (float z0, float z1) d) =>
            Ramp(w._x, f.X0 - 3, f.X1 + 3, main ? 12 : 6) * Ramp(w._y, main ? float.MinValue : f.YAt(w._x) - 15, f.YAt(w._x) + 4, 6) * Ramp(w._z, d.z0 - 15, d.z1 + 15, 20);

        /// <summary>Pairs the map's floors with P+'s: the main floors (the widest) together, then each platform with the
        /// one at the same place relative to the main floor (the same side, within 30 units up or down and 35 across).</summary>
        static List<(int m, int p)> Pair(List<Run> a, int am, List<Run> b, int bm)
        {
            // the main floors go together when they're alike (about as wide, about as high); a stage laid out quite
            // differently (Rainbow Road's spread-out roads against Smashville's one long floor) pairs floors where they are
            float sx = b[bm].W / Math.Max(1e-3f, a[am].W);
            bool mains = sx >= 0.6f && sx <= 1.67f && Math.Abs(b[bm].Y - a[am].Y) < 40 && Math.Abs(b[bm].Cx - a[am].Cx) < 0.5f * b[bm].W;
            var pairs = new List<(int, int)>();
            if (mains) pairs.Add((am, bm));
            var cands = new List<(float cost, int i, int j)>();
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    if (mains && (i == am || j == bm)) continue;
                    float dw = Math.Abs(a[i].W - b[j].W);
                    if (mains)
                    {
                        float ua = (a[i].Cx - a[am].Cx) * sx, va = a[i].Y - a[am].Y, ub = b[j].Cx - b[bm].Cx, vb = b[j].Y - b[bm].Y;
                        float du = Math.Abs(ua - ub), dv = Math.Abs(va - vb);
                        if (dv > 30 || du > 35) continue;
                        if (Math.Abs(ua) > 8 && Math.Abs(ub) > 8 && Math.Sign(ua) != Math.Sign(ub)) continue;
                        cands.Add((0.6f * dv + du + 0.25f * dw, i, j));   // the same side counts most (a platform's art stays on its side)
                    }
                    else
                    {
                        float dx = Math.Abs(a[i].Cx - b[j].Cx), dy = Math.Abs(a[i].Y - b[j].Y), r = b[j].W / Math.Max(1e-3f, a[i].W);
                        if (dy > 10 || dx > 25 || r < 0.6f || r > 1.67f) continue;
                        cands.Add((dy + 0.5f * dx + 0.25f * dw, i, j));
                    }
                }
            var usedA = new HashSet<int>(pairs.Select(x => x.Item1)); var usedB = new HashSet<int>(pairs.Select(x => x.Item2));
            foreach (var c in cands.OrderBy(c => c.cost))
                if (!usedA.Contains(c.i) && !usedB.Contains(c.j)) { usedA.Add(c.i); usedB.Add(c.j); pairs.Add((c.i, c.j)); }
            return pairs;
        }

        /// <summary>The main floor: the one covering most of the middle of the stage (x within 40 of the centre), the
        /// widest of those that tie. A map with huge floors far out (Brawl's Green Hill Zone) still gets the one in the middle.</summary>
        static int Widest(List<Run> f) => f.Count == 0 ? -1 : Enumerable.Range(0, f.Count)
            .OrderByDescending(i => Math.Round(Math.Max(0, Math.Min(f[i].X1, 40) - Math.Max(f[i].X0, -40)) / 4)).ThenByDescending(i => f[i].W).First();

        /// <summary>The art that rides along with a moving floor: animated objects that stay on top of it all through its
        /// animation (checked at four points of it).</summary>
        static List<Piece> Riders(ResourceNode root, Run floor, List<Piece> pieces, Alignment moved)
        {
            var found = new List<Piece>();
            var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault(x => x.Name == floor.Model);
            var bone = model == null ? null : BoneOf(root, floor.Model, floor.Bone);
            if (bone == null) return found;
            var chrs = root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().ToList();
            bool Has(CHR0Node a, MDL0BoneNode b) { for (var x = b; x != null; x = x.Parent as MDL0BoneNode) if (a.FindChild(x.Name, false) != null) return true; return false; }
            var anim = Moves(root, bone).FirstOrDefault();
            if (anim == null) return found;
            int frames = Math.Max(1, anim.FrameCount);
            var at = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                try { model.ApplyCHR(anim, Math.Max(1, i * frames / 4)); } catch { try { model.ApplyCHR(null, 0); } catch { } return found; }
                var w = bone.Matrix * new Vector3(floor.Cx, floor.Y, 0);
                at.Add(moved == null ? w : new Vector3(w._x * moved.Scale + moved.X, w._y * moved.Scale + moved.Y, w._z));
            }
            try { model.ApplyCHR(null, 0); } catch { }
            foreach (var p in pieces.Where(x => x.Animated))
            {
                var own = chrs.Where(a => Has(a, p.Bone)).OrderBy(a => a == anim ? 0 : a.FrameCount == anim.FrameCount ? 1 : 2).FirstOrDefault();
                if (own == null) continue;
                bool rides = true;
                for (int i = 0; i < 4 && rides; i++)
                {
                    try { p.Model.ApplyCHR(own, Math.Max(1, i * Math.Max(1, own.FrameCount) / 4)); } catch { rides = false; break; }
                    float x0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    foreach (var v in p.Local) { var w = p.Bone.Matrix * v; x0 = Math.Min(x0, w._x); x1 = Math.Max(x1, w._x); y1 = Math.Max(y1, w._y); }
                    rides = Math.Abs(y1 - at[i]._y) <= 8 && at[i]._x >= x0 - 5 && at[i]._x <= x1 + 5 && x1 - x0 <= 3 * floor.W + 10;
                }
                StartPose(root, p.Model);
                if (rides) found.Add(p);
            }
            return found;
        }

        /// <summary>Puts a model in the pose it's in when the match starts: the first frame of the first animation that moves
        /// one of its bones (BrawlLib plays an animation from frame 1; frame 0 is the resting pose), else at rest.</summary>
        static void StartPose(ResourceNode root, MDL0Node m)
        {
            var bones = new HashSet<string>(m.BoneCache.Select(b => b.Name), StringComparer.Ordinal);
            var anim = root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().FirstOrDefault(c => c.Children.Any(e => bones.Contains(e.Name)));
            try { m.ApplyCHR(anim, anim == null ? 0 : 1); }
            catch { try { m.ApplyCHR(null, 0); } catch { } }
        }

        /// <summary>The new points of each changed object, in its bone's space (objects sharing points share the array).
        /// Two objects can share one array of points (a see-through pass over a solid one): a point that one of them would
        /// move or hide and the other would put somewhere else (or leave) is a conflict, and nothing is changed (conflict
        /// says where).</summary>
        static Dictionary<MDL0VertexNode, Vector3[]> Edits(IEnumerable<Piece> pieces, Func<Piece, Comp, int, Vector3?> move, out string conflict, bool othersStay = true)
        {
            conflict = null;
            var edits = new Dictionary<MDL0VertexNode, Vector3[]>();
            var want = new Dictionary<(MDL0VertexNode vn, int v), Vector3>();
            var stay = new HashSet<(MDL0VertexNode vn, int v)>();
            bool Same(Vector3 x, Vector3 y) => Math.Abs(x._x - y._x) < 1e-3f && Math.Abs(x._y - y._y) < 1e-3f && Math.Abs(x._z - y._z) < 1e-3f;
            foreach (var p in pieces)
                foreach (var c in p.Comps)
                {
                    var mid = p.Local[c.Verts[0]];
                    foreach (int v in c.Verts)
                    {
                        Vector3? to = c.Hide ? mid : move(p, c, v) is Vector3 w ? p.Inv * w : (Vector3?)null;
                        var key = (p.Vn, v);
                        if (to == null) { stay.Add(key); continue; }
                        if (want.TryGetValue(key, out var had) && !Same(had, to.Value)) { conflict = $"{p.Key} shares points with another part that would go elsewhere"; return null; }
                        want[key] = to.Value;
                    }
                }
            // objects using the same array that aren't among the pieces (bent by several bones, nothing but lines, skipped):
            // their points stay as they are (othersStay false: those objects are taken out anyway, P+'s scenery that goes)
            var mine = new HashSet<MDL0ObjectNode>(pieces.Select(p => p.Obj));
            foreach (var vn in othersStay ? want.Keys.Select(k => k.vn).Distinct().ToList() : new List<MDL0VertexNode>())
                foreach (var o in vn._objects.Where(o => !mine.Contains(o)))
                {
                    if (o._manager?._vertices == null) { conflict = $"{vn.Name} is shared by a part that can't be looked at"; return null; }
                    foreach (var v in o._manager._vertices)
                        if (v.Facepoints != null) foreach (var fp in v.Facepoints) stay.Add((vn, fp._vertexIndex));
                }
            foreach (var kv in want)
            {
                var p0 = kv.Key.vn.Vertices[kv.Key.v];
                if (stay.Contains(kv.Key) && !Same(p0, kv.Value)) { conflict = $"{kv.Key.vn.Name} is shared by a part that stays and one that moves"; return null; }
                if (!edits.TryGetValue(kv.Key.vn, out var arr)) arr = edits[kv.Key.vn] = (Vector3[])kv.Key.vn.Vertices.Clone();
                arr[kv.Key.v] = kv.Value;
            }
            return edits;
        }

        /// <summary>Writes new points into a vertex array in exactly the format it already has (number type, fraction bits,
        /// stride). Each object keeps its own copy of that format and the game reads the points with it, so letting BrawlLib
        /// pick a new format (what the Vertices setter does) can leave an object reading its points at the wrong scale
        /// (found 2026-11-04: Green Greens' apples stored with 6 fraction bits, read with 12). False, and nothing changed,
        /// when a point doesn't fit that format.</summary>
        static unsafe bool SetPoints(MDL0VertexNode vn, Vector3[] pts)
        {
            var src = vn.WorkingUncompressed;
            if (src.Address == null || src.Length < 0x40) return false;
            var buf = new byte[src.Length];
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)(void*)src.Address, buf, 0, src.Length);
            int BE32(int at) => buf[at] << 24 | buf[at + 1] << 16 | buf[at + 2] << 8 | buf[at + 3];
            int off = BE32(8), xyz = BE32(20), type = BE32(24), frac = buf[28], stride = buf[29], n = buf[30] << 8 | buf[31];
            int comps = xyz != 0 ? 3 : 2, size = type <= 1 ? 1 : type <= 3 ? 2 : type == 4 ? 4 : 0;
            if (size == 0 || pts.Length != n || stride < comps * size || off < 0x20 || off + (long)n * stride > buf.Length) return false;
            double scale = type == 4 ? 1 : Math.Pow(2, frac);
            double lo = type == 0 ? 0 : type == 1 ? -128 : type == 2 ? 0 : -32768, hi = type == 0 ? 255 : type == 1 ? 127 : type == 2 ? 65535 : 32767;
            float[] mn = { float.MaxValue, float.MaxValue, float.MaxValue }, mx = { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < n; i++)
                for (int k = 0; k < comps; k++)
                {
                    float v = k == 0 ? pts[i]._x : k == 1 ? pts[i]._y : pts[i]._z;
                    if (float.IsNaN(v) || float.IsInfinity(v)) return false;
                    // points stored without depth: one that would need some can't be put there
                    if (comps == 2 && k == 1 && Math.Abs(pts[i]._z) > 0.01f) return false;
                    int at = off + i * stride + k * size;
                    if (type == 4)
                    {
                        int bits = BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
                        buf[at] = (byte)(bits >> 24); buf[at + 1] = (byte)(bits >> 16); buf[at + 2] = (byte)(bits >> 8); buf[at + 3] = (byte)bits;
                    }
                    else
                    {
                        double q = Math.Round(v * scale);
                        if (q < lo || q > hi) return false;
                        int iq = (int)q;
                        if (size == 1) buf[at] = (byte)iq; else { buf[at] = (byte)(iq >> 8); buf[at + 1] = (byte)iq; }
                        v = (float)(q / scale);
                    }
                    mn[k] = Math.Min(mn[k], v); mx[k] = Math.Max(mx[k], v);
                }
            if (comps == 2) { mn[2] = mx[2] = 0; }
            for (int k = 0; k < 3; k++)
                foreach (var (at, val) in new[] { (32 + 4 * k, n > 0 ? mn[k] : 0f), (44 + 4 * k, n > 0 ? mx[k] : 0f) })
                {
                    int bits = BitConverter.ToInt32(BitConverter.GetBytes(val), 0);
                    buf[at] = (byte)(bits >> 24); buf[at + 1] = (byte)(bits >> 16); buf[at + 2] = (byte)(bits >> 8); buf[at + 3] = (byte)bits;
                }
            fixed (byte* p = buf) vn.ReplaceRaw(p, buf.Length);
            vn.SignalPropertyChange();
            vn.Model?.SignalPropertyChange();
            return true;
        }

        /// <summary>Fits the map's scenery onto P+'s floors piece by piece (see the top of this file). map: the map
        /// (already moved as a whole by LineUp/MoveLooks, if that helped); global: that move, or null. pplusArt: P+'s own
        /// pieces may be kept for floors the map draws nothing for (only where the map's looks go on P+'s file).
        /// Returns what was done (Map: the map; PPlus/KeepPPlus: P+'s file and the pieces of it that stay), or null when
        /// nothing needs doing or it can't be done safely (problem says why).</summary>
        public static Alignment FitPieces(byte[] map, byte[] original, ICollection<string> program, bool pplusArt, Alignment global, out string problem) =>
            FitPieces(map, original, program, pplusArt, global, out problem, out _);

        /// <summary>The map's floors before it was moved as a whole (unmoved: that file), each where the move puts what draws
        /// it: one drawn by a part MoveLooks leaves where it is is left out. Taken from the file before the move because
        /// a floor's collision can ride on a bone whose animation MoveLooks moved (Yoshi's Story 64 alts), so measuring
        /// it on the moved file would move it twice. Null when the file can't be read; main: false when the map's main
        /// floor is one that stays where it is.</summary>
        static List<Run> MovedRuns(byte[] unmoved, ICollection<string> program, bool looks, Alignment global, out bool main)
        {
            main = true;
            using (var t = new TempFile(".pac", unmoved))
            lock (BrawlGate.Lock)
            using (var a = NodeFactory.FromFile(null, t.Path))
            {
                if (a == null) return null;
                var anim = AnimatedNames(a);
                var all = Runs(a, anim);
                var put = StaysPut(a, all, MoveKeep(a, program, looks), anim);
                var fixedM = all.Where(r => !r.Moving).ToList();
                int wm = Widest(fixedM);
                if (wm >= 0 && put.Contains(fixedM[wm])) main = false;
                var res = all.Where(r => r.Moving || !put.Contains(r)).ToList();
                foreach (var r in res) r.Move(global.Scale, global.X, global.Y);
                return res;
            }
        }

        /// <summary>FitPieces; offBefore: how far off the map is as it stands (the same measure as the result's After),
        /// or -1 when it couldn't be measured.</summary>
        public static Alignment FitPieces(byte[] map, byte[] original, ICollection<string> program, bool pplusArt, Alignment global, out string problem, out float offBefore, byte[] unmoved = null)
        {
            problem = null; offBefore = -1;
            program = program ?? new HashSet<string>();
            Installer.Quiet();
            var result = new Alignment();
            if (global != null) { result.Scale = global.Scale; result.X = global.X; result.Y = global.Y; result.Before = global.Before; }
            List<Run> mRuns, pRuns;
            var keep = new HashSet<string>(StringComparer.Ordinal);   // the map's models the match uses
            int am, bm;
            List<(int m, int p)> pairs;
            var uncovered = new List<int>();   // P+ floors the map draws nothing for
            List<Run> pre = null;
            if (global != null && global.Moves && unmoved != null)
            {
                pre = MovedRuns(unmoved, program, pplusArt, global, out bool mainMoves);
                if (pre != null && !mainMoves) { problem = "Its main floor is drawn by a part that stays where it is."; return null; }
            }
            using (var tA = new TempFile(".pac", map))
            using (var tB = new TempFile(".pac", original))
            using (var oA = new TempFile(".pac"))
            using (var oB = new TempFile(".pac"))
            {
                bool mapChanged = false, pChanged = false;
                lock (BrawlGate.Lock)
                using (var a = NodeFactory.FromFile(null, tA.Path))
                using (var b = NodeFactory.FromFile(null, tB.Path))
                {
                    if (a == null || b == null) { problem = "One of the files couldn't be read."; return null; }
                    var animA = AnimatedNames(a); var animB = AnimatedNames(b);
                    var allM = Runs(a, animA); var allP = Runs(b, animB);
                    // the map's collision didn't move with its scenery (MoveLooks moves only scenery): its floors are taken where the scenery now is
                    // (from the file before the move when there is one, see MovedRuns)
                    // A floor riding on a model MoveLooks left where it was isn't where the moved scenery is any more: it's
                    // left out (its own art either doesn't go over at all, or is a gameplay part that stays as it is)
                    if (pre != null) allM = pre;
                    else if (global != null && global.Moves)
                    {
                        var put = StaysPut(a, allM, MoveKeep(a, program, pplusArt), animA);
                        var fixedM = allM.Where(r => !r.Moving).ToList();
                        int wm = Widest(fixedM);
                        // its main floor stays where it is while the rest moved: nothing to fit by
                        if (wm >= 0 && put.Contains(fixedM[wm])) { problem = "Its main floor is drawn by a part that stays where it is."; return null; }
                        allM = allM.Where(r => r.Moving || !put.Contains(r)).ToList();
                    }
                    if (pre == null && global != null && global.Moves) foreach (var r in allM) r.Move(global.Scale, global.X, global.Y);
                    mRuns = allM.Where(r => !r.Moving).ToList();
                    // P+'s floors on its own moving parts (Corneria's Arwings, a moving platform) keep P+'s look (see SwapLooks): nothing to fit there
                    var pShown = OwnLooks(b);
                    bool Shown(MDL0Node m) => pShown.Contains(m.Name);
                    pRuns = allP.Where(r => !r.Moving).ToList();
                    MarkOwn(b, pRuns, pShown, animB);
                    am = Widest(mRuns); bm = Widest(pRuns);
                    if (am < 0 || bm < 0) { problem = $"One of the stages has no fixed floor to fit to (map {string.Join(" ", allM)} | P+ {string.Join(" ", allP)})."; return null; }
                    if (Before(result) < 0) result.Before = FitOf(Segs(mRuns), Segs(pRuns)).Floors;
                    pairs = Pair(mRuns, am, pRuns, bm);
                    // a floor P+ draws itself: the map's scenery for it goes (both would show), nothing moves onto it
                    var shifts = pairs.Where(x => !pRuns[x.p].Own).ToDictionary(x => x.m, x => new Shift(mRuns[x.m], pRuns[x.p]));

                    // the map's scenery: everything its gameplay and the stage program don't use
                    // (on P+'s file, what SwapLooks takes over: everything but position markers and parts that move platforms; else
                    // everything the map's gameplay and stage program use stays as it is)
                    if (pplusArt)
                    {
                        keep.UnionWith(MovingGameplay(a));
                        var rideBones = RideBones(b);   // the same models SwapLooks leaves out
                        foreach (var m in a.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                            if (Polys(m) > 0 && m.BoneCache.Any(x => rideBones.Contains(x.Name))) keep.Add(m.Name);
                    }
                    else
                    {
                        foreach (var col in a.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                            foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName))) keep.Add(o._modelName);
                        foreach (var m in a.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                            if (IsPositionModel(m) || program.Contains(m.Name)) keep.Add(m.Name);
                    }
                    var seen = Pieces(a, m => !keep.Contains(m.Name) && Polys(m) > 0, animA);
                    var pieces = seen.Where(p => !p.Locked).ToList();
                    var depth = Depth(pieces, mRuns[am]);
                    Assign(pieces, mRuns, am, depth);

                    // how far off it is as it stands (the same measure as afterwards): the floors its scenery draws now, and
                    // P+'s floors P+ draws itself, against all of P+'s floors
                    {
                        var was = new List<(float x1, float y1, float x2, float y2, bool floor)>();
                        foreach (var r in mRuns.Where(r => seen.Any(p => p.Comps.Any(c => c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, r) >= 0.25f))))
                            was.AddRange(r.S.Select(s => (s.x1, s.y1, s.x2, s.y2, true)));
                        foreach (var r in pRuns.Where(r => r.Own)) was.AddRange(r.S.Select(s => (s.x1, s.y1, s.x2, s.y2, true)));
                        result.Base = FitOf(was, Segs(pRuns)).Floors;
                        offBefore = result.Base;
                    }

                    // floors P+ doesn't have: their pieces hidden
                    var paired = new HashSet<int>(shifts.Keys);
                    // only floors where the fighting is: a map's far-off ground (Brawl's Green Hill Zone's hills) is scenery to leave be
                    float ax0 = pRuns.Min(r => r.X0) - 20, ax1 = pRuns.Max(r => r.X1) + 20, ay0 = pRuns.Min(r => r.Y) - 30, ay1 = pRuns.Max(r => r.Y) + 40;
                    bool InPlay(Run r) => r.Cx >= ax0 && r.Cx <= ax1 && r.Y >= ay0 && r.Y <= ay1;
                    for (int i = 0; i < mRuns.Count; i++)
                    {
                        if (paired.Contains(i) || !InPlay(mRuns[i])) continue;
                        var mine = pieces.SelectMany(p => p.Comps).Where(c => c.Floor == i).ToList();
                        foreach (var c in mine) c.Hide = true;
                        bool own = pairs.Any(x => x.m == i);
                        if (mine.Count == 0) result.Bare++;
                        else if (i == am) { result.MainHidden = true; result.MainOwn = own; }
                        else if (own) result.HiddenOwn++;
                        else result.Hidden++;
                    }
                    // a loose piece that wasn't given to one floor (a sloped platform reaching over another's line) but whose top
                    // is only floors that were hidden: it goes too (left, it would float where P+ has nothing to stand on)
                    {
                        var gone = new HashSet<int>(Enumerable.Range(0, mRuns.Count).Where(i => !paired.Contains(i) && InPlay(mRuns[i])));
                        foreach (var c in pieces.SelectMany(p => p.Comps).Where(c => c.Floor < 0 && !c.Hide && !c.Bend).ToList())
                        {
                            var on = Enumerable.Range(0, mRuns.Count).Where(i => c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, mRuns[i], 3f) >= 0.5f).ToList();
                            if (on.Count > 0 && on.All(gone.Contains)) c.Hide = true;
                        }
                        // the same for a platform's own slab that was bent because it also touches a smaller floor next to it (a TV
                        // pile's top), when it's the map's copy of a platform P+ draws itself: only a thin piece about that platform's
                        // size, and only when every floor it touches goes (regression list 28: Entrance's left platform stayed
                        // drawn under P+'s)
                        var ownGone = new HashSet<int>(pairs.Where(x => pRuns[x.p].Own && gone.Contains(x.m)).Select(x => x.m));
                        foreach (var p in pieces.Where(p => !p.Animated))
                            foreach (var c in p.Comps.Where(c => c.Floor < 0 && !c.Hide && c.Bend && c.Z0 <= 10 && c.Z1 >= -10 && c.Y1 - c.Y0 <= 6).ToList())
                            {
                                var on = Enumerable.Range(0, mRuns.Count).Where(i => Cover(c, mRuns[i], 3f) >= 0.5f).ToList();
                                if (on.Count == 0 || !on.All(gone.Contains)) continue;
                                if (on.Any(i => ownGone.Contains(i) && Cover(c, mRuns[i], 3f) >= 0.75f && c.X1 - c.X0 <= 1.25f * mRuns[i].W && Inside(c, mRuns[i], false)))
                                { c.Hide = true; c.Bend = false; }
                            }
                    }
                    // moving platforms P+ doesn't have (P+'s own moving platforms replace the map's copies, see SwapLooks):
                    // the art riding along with them hidden
                    var pRide = new HashSet<string>(allP.Where(r => r.Bone != null).Select(r => r.Bone), StringComparer.Ordinal);
                    foreach (var r in allM.Where(r => r.Moving && !pRide.Contains(r.Bone)))
                    {
                        // P+ has a moving platform where this one starts: P+'s moves there instead, and what comes with the map's stays
                        if (allP.Any(q => q.Moving && Math.Abs(q.SY - r.SY) < 20 && Math.Abs(q.SX - r.SX) < 30)) continue;
                        var riders = Riders(a, r, pieces, global != null && global.Moves ? global : null);
                        foreach (var p in riders) foreach (var c in p.Comps) c.Hide = true;
                        if (riders.Count > 0) result.Hidden++;
                    }

                    var edits = Edits(pieces, (p, c, v) =>
                    {
                        var w = p.World[v];
                        if (c.Floor >= 0) return shifts.TryGetValue(c.Floor, out var s) && !s.Nothing ? s.At(w) : (Vector3?)null;
                        if (!c.Bend || p.Animated) return null;
                        float total = 0; float dx = 0, dy = 0;
                        foreach (var s in shifts.Values)
                        {
                            if (s.Nothing) continue;
                            float k = Pull(w, s.From, s.From == mRuns[am], depth);
                            if (k <= 0) continue;
                            var t = s.At(w);
                            total += k; dx += k * (t._x - w._x); dy += k * (t._y - w._y);
                        }
                        if (total <= 0) return null;
                        float n = Math.Max(1, total);
                        return new Vector3(w._x + dx / n, w._y + dy / n, w._z);
                    }, out string clash);
                    if (edits == null) { problem = "Fitting its scenery: " + clash + "."; return null; }
                    foreach (var kv in edits)
                        if (!SetPoints(kv.Key, kv.Value)) { problem = $"A piece of its scenery ({kv.Key.Name}) can't be moved that far in the way its points are stored."; return null; }
                    mapChanged = edits.Count > 0;

                    // what was done, for floors whose scenery really moved
                    bool FloorMoves(int f) => pieces.Any(p => p.Comps.Any(c => c.Floor == f && !c.Hide));
                    if (shifts.TryGetValue(am, out var main) && FloorMoves(am)) { result.Stretch = main.Sx; result.MainMoved = !main.Nothing; }
                    result.Moved = shifts.Where(kv => kv.Key != am && !kv.Value.Nothing).Count(kv => FloorMoves(kv.Key));

                    // P+'s floors the map draws nothing for: P+'s own pieces for them stay
                    var pairedP = new HashSet<int>(pairs.Select(x => x.p));
                    // drawn: the map's scenery that goes over has something on the paired floor (a floor drawn only by a part of
                    // the map's gameplay isn't: those parts don't go over)
                    bool Drawn(Run f) => seen.Any(p => p.Comps.Any(c => !c.Hide && c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, f) >= 0.25f));
                    var drawn = new HashSet<int>(pairs.Where(x => !pRuns[x.p].Own && Drawn(mRuns[x.m])).Select(x => x.p));
                    drawn.UnionWith(Enumerable.Range(0, pRuns.Count).Where(j => pRuns[j].Own));
                    for (int j = 0; j < pRuns.Count; j++) if (!drawn.Contains(j)) uncovered.Add(j);
                    if (pplusArt && uncovered.Count > 0)
                    {
                        // P+'s moving platforms that keep their look (see SwapLooks) aren't touched
                        var pPieces = Pieces(b, m => Polys(m) > 0 && !Shown(m), animB).Where(p => !p.Locked).ToList();
                        Assign(pPieces, pRuns, bm, Depth(pPieces, pRuns[bm]));
                        var want = new HashSet<int>(uncovered);
                        var keepers = pPieces.Where(p => p.Comps.Any(c => want.Contains(c.Floor))).ToList();
                        foreach (var p in keepers)
                            foreach (var c in p.Comps) if (!want.Contains(c.Floor)) c.Hide = true;
                        result.FromPPlus = uncovered.Count(j => keepers.Any(p => p.Comps.Any(c => c.Floor == j)));
                        result.KeepPPlus = new HashSet<string>(keepers.Select(p => p.Key), StringComparer.Ordinal);
                        var pEdits = Edits(keepers, (p, c, v) => null, out string pClash, false);
                        if (pEdits == null) { problem = "Keeping P+'s own look: " + pClash + "."; return null; }
                        foreach (var kv in pEdits)
                            if (!SetPoints(kv.Key, kv.Value)) { problem = $"A piece of P+'s scenery ({kv.Key.Name}) can't be changed in the way its points are stored."; return null; }
                        pChanged = pEdits.Count > 0;
                        drawn.UnionWith(uncovered.Where(j => keepers.Any(p => p.Comps.Any(c => c.Floor == j))));
                        // floors still bare whose P+ look can't be split off (bent by several bones, like Rainbow Road's
                        // roads): the whole object stays, as long as it draws no floor the map's scenery already draws
                        var bare = uncovered.Where(j => !drawn.Contains(j)).ToList();
                        if (bare.Count > 0)
                        {
                            foreach (var (key, tops) in WholeTops(b, m => Polys(m) > 0 && !Shown(m), pRuns))
                            {
                                if (result.KeepPPlus.Contains(key) || !tops.Any(j => bare.Contains(j)) || tops.Any(j => drawn.Contains(j) && !uncovered.Contains(j))) continue;
                                result.KeepPPlus.Add(key);
                                foreach (int j in tops.Where(bare.Contains)) if (drawn.Add(j)) result.FromPPlus++;
                            }
                        }
                    }
                    // nothing to change: only worth saying how far off it is when the map was moved as a whole first, or when P+
                    // draws some floors itself (the plain comparison of floors would count those as bare)
                    if (!mapChanged && result.FromPPlus == 0 && (global == null || !global.Moves) && !pRuns.Any(r => r.Own)) return null;
                    // every model back in its resting pose before the files are written (StartPose left some at frame 1)
                    foreach (var r in new[] { a, b }) foreach (var md in r.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>()) try { md.ApplyCHR(null, 0); } catch { }
                    if (mapChanged) a.Export(oA.Path);
                    if (pChanged) b.Export(oB.Path);

                    // how far off it is now: P+'s floors the scenery now sits on, against all of P+'s floors
                    var now = new List<(float x1, float y1, float x2, float y2, bool floor)>();
                    foreach (int j in drawn) now.AddRange(pRuns[j].S.Select(s => (s.x1, s.y1, s.x2, s.y2, true)));
                    for (int i = 0; i < mRuns.Count; i++)
                        // a floor P+ doesn't have still counts if some scenery that goes over still draws it
                        if (!paired.Contains(i) && InPlay(mRuns[i]) && seen.Any(p => p.Comps.Any(c => !c.Hide && c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, mRuns[i], 3f) >= 0.5f)))
                            now.AddRange(mRuns[i].S.Select(s => (s.x1, s.y1, s.x2, s.y2, true)));
                    result.PBare = Enumerable.Range(0, pRuns.Count).Count(j => !drawn.Contains(j));
                    result.Fit = FitOf(now, Segs(pRuns));
                    result.After = result.Fit.Floors;
                    result.Pieces = true;
                    if (Environment.GetEnvironmentVariable("SKINMANAGER_TRACE") != null)
                    {
                        Console.WriteLine($"fit: map {string.Join(" ", allM)} | P+ {string.Join(" ", allP)}");
                        Console.WriteLine($"fit: uncovered {string.Join(" ", uncovered.Select(j => pRuns[j]))}; drawn {string.Join(" ", drawn.Select(j => pRuns[j]))}");
                        Console.WriteLine($"fit: P+ own {string.Join(" ", pRuns.Select(r => r + (r.Own ? "OWN" : "") + (r.By.Count > 0 ? "by:" + string.Join("+", r.By) : "")))}; shown {string.Join(",", pShown)}");
                        if (Environment.GetEnvironmentVariable("FITDBG") != null)
                            for (int i = 0; i < mRuns.Count; i++)
                                foreach (var p in seen) foreach (var c in p.Comps) if (c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, mRuns[i], 3f) >= 0.5f)
                                    Console.WriteLine($"fit:   {mRuns[i]} drawn by {c} floor {c.Floor}{(c.Hide ? " hidden" : "")}{(c.Bend ? " bent" : "")}{(p.Animated ? " animated" : "")}{(p.Locked ? " locked" : "")}");
                        Console.WriteLine($"fit: pairs {string.Join(" ", pairs.Select(x => mRuns[x.m] + "->" + pRuns[x.p]))}");
                        if (Environment.GetEnvironmentVariable("FITDBG") != null)
                            for (int i = 0; i < mRuns.Count; i++)
                                if (!paired.Contains(i) && InPlay(mRuns[i]))
                                    foreach (var p in seen) foreach (var c in p.Comps) if (!c.Hide && c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, mRuns[i], 3f) >= 0.5f)
                                        Console.WriteLine($"fit:   still drawn {mRuns[i]} by {c} floor {c.Floor} locked={p.Locked} in pieces={pieces.Contains(p)}");
                            Console.WriteLine($"fit: now {string.Join(" ", now.Select(q => $"({q.x1:0.#},{q.y1:0.#})-({q.x2:0.#},{q.y2:0.#})"))} -> {result.After:0.0}");
                        Console.WriteLine($"fit: bent {pieces.Sum(p => p.Comps.Count(c => c.Bend))} pieces; P+ kept {string.Join(", ", result.KeepPPlus ?? new HashSet<string>())}");
                    }
                }
                result.Map = mapChanged ? File.ReadAllBytes(oA.Path) : map;
                result.PPlus = pChanged ? File.ReadAllBytes(oB.Path) : null;
            }

            // nothing the match uses may have changed
            // (on P+'s file the map's gameplay isn't used at all: only P+'s file has to stay the same)
            if (!pplusArt && !SameGameplay(map, result.Map, keep, out problem)) return null;
            if (result.PPlus != null && !SameGameplay(original, result.PPlus, GameplayModels(original), out problem)) return null;
            return result;
        }

        /// <summary>The move (shift, and size when the two differ) that puts a map's main floor, its widest, right on P+'s
        /// main floor. For maps whose platforms are elsewhere (so LineUp can't find one move for all): the whole map goes
        /// with its main floor, and the platforms are fitted afterwards. Moves is false when the main floors aren't alike
        /// (more than two thirds wider or narrower): that's a stage laid out differently, fitted floor by floor instead.</summary>
        public static Alignment MainLineUp(byte[] map, byte[] original, ICollection<string> program = null, bool looks = false)
        {
            var result = new Alignment();
            Run M, P;
            using (var tA = new TempFile(".pac", map))
            using (var tB = new TempFile(".pac", original))
            lock (BrawlGate.Lock)
            using (var a = NodeFactory.FromFile(null, tA.Path))
            using (var b = NodeFactory.FromFile(null, tB.Path))
            {
                if (a == null || b == null) return result;
                // (only floors whose scenery MoveLooks moves: one riding on a part it leaves where it is would move nothing)
                var stay = MoveKeep(a, program, looks);
                var animA = AnimatedNames(a);
                var allA = Runs(a, animA);
                var put = StaysPut(a, allA, stay, animA);
                var ma = allA.Where(r => !r.Moving && !put.Contains(r)).ToList();
                var pb = Runs(b, AnimatedNames(b)).Where(r => !r.Moving).ToList();
                if (ma.Count == 0 || pb.Count == 0) return result;
                M = ma[Widest(ma)]; P = pb[Widest(pb)];
                // a main floor away from the middle (the map's middle is drawn by a part that doesn't go over) isn't one to move by
                if (M.X0 > 40 || M.X1 < -40) return result;
                // its main floor is one of P+'s floors already (an alt made for this stage: P+'s Pokemon Stadium 2 has two
                // main floors 3 apart, its alts draw the lower one): it was built for this, nothing to move
                if (pb.Any(q => Math.Abs(q.X0 - M.X0) < 1 && Math.Abs(q.X1 - M.X1) < 1 && Math.Abs(q.Y - M.Y) < 1)) return result;
                // P+ draws its own main floor (a moving stage like Corneria's Great Fox): the map's ground won't show there anyway
                MarkOwn(b, pb, OwnLooks(b), AnimatedNames(b));
                if (P.Own) return result;
            }
            float r = P.W / Math.Max(1e-3f, M.W);
            if (r < 0.6f || r > 1.67f) return result;
            float s = Math.Abs(r - 1) < 0.01f ? 1 : r;
            var sa = Standing(map); var sb = Standing(original);
            var none = FitOf(sa, sb);
            result.Scale = s; result.X = P.Cx - s * M.Cx; result.Y = P.Y - s * M.Y;
            result.Fit = FitOf(Moved(sa, s, result.X, result.Y), sb);
            result.Before = none.Floors; result.After = result.Fit.Floors;
            return result;
        }

        /// <summary>Every drawn object of the models chosen (also ones bent by several bones) with the floors its top
        /// covers (at least half of each, within 3 units, reaching the line fighters stand on).</summary>
        static List<(string key, List<int> tops)> WholeTops(ResourceNode root, Func<MDL0Node, bool> use, List<Run> floors)
        {
            var res = new List<(string, List<int>)>();
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
            {
                if (!use(m)) continue;
                m.Populate();
                StartPose(root, m);
                foreach (var o in m.FindChild("Objects", false)?.Children.OfType<MDL0ObjectNode>() ?? Enumerable.Empty<MDL0ObjectNode>())
                {
                    var mgr = o._manager;
                    var prim = mgr == null ? null : TriField?.GetValue(mgr) as GLPrimitive;
                    if (mgr?._vertices == null || prim?._indices == null) continue;
                    var at = new Dictionary<int, Vector3>();
                    for (int vi = 0; vi < mgr._vertices.Count; vi++) foreach (int fp in mgr._vertices[vi].FaceDataIndices) at[fp] = mgr._vertices[vi].WeightedPosition;
                    var spans = floors.Select(_ => new List<(float a, float b)>()).ToList();
                    float z0 = float.MaxValue, z1 = float.MinValue;
                    var ind = prim._indices;
                    for (int i = 0; i + 2 < ind.Length; i += 3)
                    {
                        if (!at.TryGetValue((int)ind[i], out var A) || !at.TryGetValue((int)ind[i + 1], out var B) || !at.TryGetValue((int)ind[i + 2], out var C)) continue;
                        z0 = Math.Min(z0, Math.Min(A._z, Math.Min(B._z, C._z))); z1 = Math.Max(z1, Math.Max(A._z, Math.Max(B._z, C._z)));
                        for (int f = 0; f < floors.Count; f++)
                        {
                            var F = floors[f];
                            if (A._x > F.X1 + 3 || A._x < F.X0 - 3) continue;
                            if (Math.Abs(A._y - F.YAt(A._x)) > 3 || Math.Abs(B._y - F.YAt(B._x)) > 3 || Math.Abs(C._y - F.YAt(C._x)) > 3) continue;
                            float lo = Math.Max(F.X0, Math.Min(A._x, Math.Min(B._x, C._x))), hi = Math.Min(F.X1, Math.Max(A._x, Math.Max(B._x, C._x)));
                            if (hi > lo) spans[f].Add((lo, hi));
                        }
                    }
                    if (z0 > 10 || z1 < -10) continue;
                    var tops = new List<int>();
                    for (int f = 0; f < floors.Count; f++)
                    {
                        var sp = spans[f]; if (sp.Count == 0) continue;
                        sp.Sort((x, y) => x.a.CompareTo(y.a));
                        float total = 0, ca = sp[0].a, cb = sp[0].b;
                        foreach (var q in sp.Skip(1)) { if (q.a > cb) { total += cb - ca; ca = q.a; cb = q.b; } else cb = Math.Max(cb, q.b); }
                        total += cb - ca;
                        if (total >= 0.5f * floors[f].W) tops.Add(f);
                    }
                    if (tops.Count > 0) res.Add((m.Name + "/" + o.Name, tops));
                }
            }
            return res;
        }

        /// <summary>The map's models MoveLooks leaves where they are. Made to match (P+'s gameplay copied into the map):
        /// position markers, every model collision rides on and the models the stage program names. Its looks on P+'s
        /// file: only position markers and the models that move platforms (everything else goes over as scenery, so it
        /// moves with the rest; FitPieces reads the map's floors with this too, so floors and scenery stay together).</summary>
        internal static HashSet<string> MoveKeep(ResourceNode root, ICollection<string> program, bool looks)
        {
            if (looks) return MovingGameplay(root);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName))) keep.Add(o._modelName);
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                if (IsPositionModel(m) || (program != null && program.Contains(m.Name))) keep.Add(m.Name);
            return keep;
        }

        /// <summary>The parts of a map its looks can't do without on P+'s file: position markers, and models collision rides
        /// on that move (a moving platform's art can't move with P+'s platform). A part collision rides on that stays put is
        /// only scenery there (Yoshi's Story 64 draws its whole stage in the model its floors ride on).</summary>
        internal static HashSet<string> MovingGameplay(ResourceNode root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(IsPositionModel)) names.Add(m.Name);
            var animated = AnimatedNames(root);
            foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName) && !string.IsNullOrEmpty(o._boneName)))
                {
                    if (names.Contains(o._modelName)) continue;
                    var bone = BoneOf(root, o._modelName, o._boneName);
                    if (bone != null && Animated(bone, animated) && !Steady(root, o._modelName, bone, out _)) names.Add(o._modelName);
                }
            return names;
        }

        /// <summary>The map's fixed floors that stay where they are when MoveLooks moves its scenery as a whole: ones riding
        /// on a model it leaves as it is (stay) whose own drawing is that floor. A floor whose collision rides on such a model
        /// but is drawn by other scenery (Yoshi's Story 64 alts: the floors ride on Randall the cloud's model, the ground is
        /// drawn by another model) goes where that scenery goes.</summary>
        static HashSet<Run> StaysPut(ResourceNode a, List<Run> runs, HashSet<string> stay, HashSet<string> anim)
        {
            var res = new HashSet<Run>();
            var on = runs.Where(r => !r.Moving && r.Model != null && stay.Contains(r.Model)).ToList();
            if (on.Count == 0) return res;
            var models = new HashSet<string>(on.Select(r => r.Model), StringComparer.Ordinal);
            var drawn = Pieces(a, m => models.Contains(m.Name), anim);
            foreach (var r in on)
                if (drawn.Any(p => p.Model.Name == r.Model && p.Comps.Any(c => c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, r) >= 0.25f))) res.Add(r);
            return res;
        }

        /// <summary>Marks P+'s floors that P+ draws itself, on a part that keeps P+'s own look (see OwnLooks): only where
        /// that part's drawing covers the floor (Yoshi's Story rides on Randall the cloud's model, which draws only the cloud).
        /// Such a part keeps P+'s look whether or not it moves (the stage program can move it: Corneria's Arwings), so the
        /// map's art for that floor goes, never P+'s.</summary>
        static void MarkOwn(ResourceNode b, List<Run> pRuns, HashSet<string> shown, HashSet<string> animB)
        {
            var own = Pieces(b, m => shown.Contains(m.Name), animB);
            foreach (var r in pRuns)
            {
                r.By = own.Where(p => p.Comps.Any(c => c.Z0 <= 10 && c.Z1 >= -10 && Cover(c, r) >= 0.25f)).Select(p => p.Model.Name).Distinct().ToList();
                r.Own = r.By.Count > 0;
            }
        }

        /// <summary>P+'s parts that keep P+'s own look when a map's looks go on its file (SwapLooks): models collision
        /// rides on that are narrower than half the stage (moving platforms, Corneria's Arwings).</summary>
        static HashSet<string> OwnLooks(ResourceNode b)
        {
            var surf = Surfaces(b);
            float width = surf.Count == 0 ? 0 : surf.Max(q => Math.Max(q.x1, q.x2)) - surf.Min(q => Math.Min(q.x1, q.x2));
            var game = GameplayModels(b);
            var moving = MovingGameplay(b);
            var res = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in b.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0 && game.Contains(m.Name)))
            {
                // a part that moves always keeps its look (the map's copy is left out: a wide moving deck would be left bare)
                if (moving.Contains(m.Name)) { res.Add(m.Name); continue; }
                float w = ModelWidth(m);
                if (width <= 0 || w < width * 0.5f) res.Add(m.Name);
            }
            return res;
        }

        static float Before(Alignment a) => a.Before > 0 || a.Scale != 1 || a.X != 0 || a.Y != 0 ? a.Before : -1;

        static List<(float x1, float y1, float x2, float y2, bool floor)> Segs(IEnumerable<Run> runs) =>
            runs.SelectMany(r => r.S.Select(s => (s.x1, s.y1, s.x2, s.y2, true))).ToList();

        static HashSet<string> GameplayModels(byte[] stage)
        {
            using (var t = new TempFile(".pac", stage))
            lock (BrawlGate.Lock) using (var r = NodeFactory.FromFile(null, t.Path)) return GameplayModels(r);
        }

        /// <summary>Do two versions of a file have every gameplay fingerprint the same (other than the outline and the
        /// models that are only scenery)? keep: the models the match uses.</summary>
        static bool SameGameplay(byte[] before, byte[] after, ICollection<string> keep, out string problem)
        {
            problem = null;
            var x = Gameplay(before); var y = Gameplay(after);
            foreach (var k in x.Keys.Union(y.Keys))
            {
                if (k == "outline") continue;
                if (k.StartsWith("model "))
                {
                    string name = k.Substring(6); int at = name.IndexOf('@'); if (at >= 0) name = name.Substring(0, at);
                    if (!keep.Contains(name)) continue;
                }
                if (!x.TryGetValue(k, out var p) || !y.TryGetValue(k, out var q) || p != q)
                { problem = "Fitting its scenery would have changed a part the match uses (" + k + ")."; return false; }
            }
            return true;
        }
    }
}
