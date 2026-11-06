// Lining a stage mod's scenery up with P+'s floors. A stage's scenery is built around its own collision, so when a map
// was made for another version of a stage (vanilla Brawl, Project M, an older P+, or another stage of the same shape)
// its ground can sit higher, lower, wider or narrower than the floor P+ puts you on. When the difference is the same
// everywhere (the whole map moved, or the whole map bigger or smaller), the map's own collision tells by how much:
// LineUp() finds the shift (and, when it helps, the size) that puts the map's floors on P+'s, and MoveLooks() moves the
// map's scenery by exactly that: the first bone of every scenery model and the animations that move those bones.
// Nothing the match uses moves (collision, blast zones, spawns, the models moving platforms ride on, and the models the
// stage program works with by name), and MoveLooks checks that before it hands the file back. When the difference
// isn't the same everywhere (a platform in another place, a wider stage with the same height), no single move fixes
// it, and LineUp says how far off it stays.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static partial class StageCheck
    {
        /// <summary>How a map's scenery is moved to line up with P+'s floors: scaled by Scale (around the stage's centre
        /// point, 0,0) then shifted by X, Y. Before/After: how far off its floors are without and with it (game units).</summary>
        public sealed class Alignment
        {
            public float Scale = 1, X, Y;
            public float Before, After;
            public Fit Fit;   // the map's floors after the move against P+'s (what the install window shows)
            // fitted piece by piece too (FitPieces, StageFit.cs)
            public bool Pieces;
            public float Stretch = 1;      // the main floor's scenery stretched sideways by this much
            public bool MainMoved;         // the main floor's scenery moved (or stretched) onto P+'s
            public bool MainHidden;        // the map's main floor hidden: P+'s floors are laid out quite differently
            public bool MainOwn;           // ... because P+ draws its own there (a moving stage like Corneria's Great Fox)
            public int HiddenOwn;          // the map's platforms hidden where P+ draws its own (moving parts that keep P+'s look)
            public int Moved;              // platforms whose scenery was moved onto P+'s
            public int Hidden;             // the map's floors P+ doesn't have (and moving platforms), hidden
            public int Bare;               // the map's floors P+ doesn't have that it draws nothing for anyway
            public int FromPPlus;          // P+'s floors the map draws nothing for, kept with P+'s own look
            public int PBare;              // P+'s floors nothing draws afterwards (they'd be invisible)
            public float Base;             // how far off its scenery is before fitting piece by piece, measured like After
            public byte[] Map;             // the map with all of that done
            public byte[] PPlus;           // P+'s file with only the pieces it keeps (null: as it is)
            public HashSet<string> KeepPPlus;  // P+'s pieces that stay in sight ("model/object")
            /// <summary>Was anything done at all (moved as a whole, or a floor's scenery moved, stretched, hidden or kept)?</summary>
            public bool Did => Moves || (Pieces && (MainHidden || MainMoved || Math.Abs(Stretch - 1) > 0.005f || Moved > 0 || HiddenOwn > 0 || Hidden > 0 || FromPPlus > 0));
            /// <summary>Is there anything to move as a whole?</summary>
            public bool Moves => Math.Abs(Scale - 1) > 0.002f || Math.Abs(X) > 0.05f || Math.Abs(Y) > 0.05f;
            /// <summary>Worth doing: it moves something, and afterwards the floors line up or nearly do (under 6 units, the
            /// install window's "nearly lines up") and clearly better than before. A move that only halves a big
            /// difference would put the ground somewhere between two of P+'s floors: no better to stand on.</summary>
            public bool Worth => Moves && After < 6f && After < Before - 0.5f;
            /// <summary>What was done, in words: "its scenery is moved 4.2 units down and made 7% bigger, its main floor is
            /// stretched 8% wider and 2 of its platforms are moved onto P+'s".</summary>
            public string Words()
            {
                var parts = new List<string>();
                if (Math.Abs(Y) > 0.05f) parts.Add($"moved {Math.Abs(Y):0.#} units {(Y > 0 ? "up" : "down")}");
                if (Math.Abs(X) > 0.05f) parts.Add($"{(parts.Count > 0 ? "" : "moved ")}{Math.Abs(X):0.#} units {(X > 0 ? "right" : "left")}");
                if (Math.Abs(Scale - 1) > 0.002f) parts.Add($"made {Math.Abs(Scale - 1) * 100:0.#}% {(Scale > 1 ? "bigger" : "smaller")}");
                var all = new List<string>();
                if (parts.Count > 0) all.Add("its scenery is " + And(parts));
                if (Pieces)
                {
                    if (MainHidden) all.Add(MainOwn ? "its own ground is hidden (P+'s moving stage is drawn there)" : "its own ground is hidden (P+'s floors are laid out differently)");
                    else if (Math.Abs(Stretch - 1) > 0.005f) all.Add($"its main floor is {(Stretch > 1 ? "stretched" : "narrowed")} {Math.Abs(Stretch - 1) * 100:0}% to P+'s width");
                    else if (MainMoved) all.Add("its main floor is moved onto P+'s");
                    if (Moved > 0) all.Add(Moved == 1 ? "1 of its platforms is moved onto P+'s" : $"{Moved} of its platforms are moved onto P+'s");
                    if (HiddenOwn > 0) all.Add(HiddenOwn == 1 ? "1 of its platforms is hidden where P+'s moving part is drawn" : $"{HiddenOwn} of its platforms are hidden where P+'s moving parts are drawn");
                    if (Hidden > 0) all.Add(Hidden == 1 ? "1 platform P+ doesn't have is hidden" : $"{Hidden} platforms P+ doesn't have are hidden");
                    if (FromPPlus > 0) all.Add(FromPPlus == 1 ? "P+'s own look is kept for 1 floor it doesn't draw" : $"P+'s own look is kept for {FromPPlus} floors it doesn't draw");
                }
                return all.Count == 0 ? "left as it is" : And(all);
            }
            static string And(List<string> p) => p.Count == 1 ? p[0] : string.Join(", ", p.Take(p.Count - 1)) + " and " + p[p.Count - 1];
        }

        /// <summary>Finds the move that puts a map's floors on P+'s: a shift alone when that's as good, else a shift and a
        /// size. Moves that would change the map's size by more than half, or that don't help, are left out (Moves false).</summary>
        public static Alignment LineUp(byte[] map, byte[] original)
        {
            var a = Standing(map); var b = Standing(original);
            return LineUp(a, b);
        }

        /// <summary>LineUp on surfaces already read (map's, P+'s).</summary>
        public static Alignment LineUp(List<(float x1, float y1, float x2, float y2, bool floor)> a, List<(float x1, float y1, float x2, float y2, bool floor)> b)
        {
            var none = FitOf(a, b);
            var result = new Alignment { Before = none.Floors, After = none.Floors, Fit = none };
            if (a.Count(s => s.floor) == 0 || b.Count(s => s.floor) == 0 || none.Floors < 0.5f || none.Floors >= 1e6f) return result;
            Alignment best = result;
            foreach (bool scale in new[] { false, true })
            {
                var (s, x, y) = Solve(a, b, scale);
                if (float.IsNaN(s) || float.IsNaN(x) || float.IsNaN(y) || s < 0.5f || s > 2f) continue;
                var fit = FitOf(Moved(a, s, x, y), b);
                // a size change has to earn its place: clearly better than the shift alone
                if (fit.Floors < best.After - (scale ? 0.25f : 0f))
                    best = new Alignment { Scale = s, X = x, Y = y, Before = none.Floors, After = fit.Floors, Fit = fit };
            }
            return best;
        }

        /// <summary>Surfaces scaled by s (around 0,0) then shifted by x, y.</summary>
        static List<(float x1, float y1, float x2, float y2, bool floor)> Moved(List<(float x1, float y1, float x2, float y2, bool floor)> a, float s, float x, float y) =>
            a.Select(f => (f.x1 * s + x, f.y1 * s + y, f.x2 * s + x, f.y2 * s + y, f.floor)).ToList();

        /// <summary>Points along a set of floors, about one every game unit.</summary>
        static List<(float x, float y)> FloorPoints(IEnumerable<(float x1, float y1, float x2, float y2, bool floor)> segs)
        {
            var r = new List<(float, float)>();
            foreach (var f in segs.Where(f => f.floor))
            {
                float len = (float)Math.Sqrt((f.x2 - f.x1) * (f.x2 - f.x1) + (f.y2 - f.y1) * (f.y2 - f.y1));
                int n = Math.Max(1, (int)len);
                for (int i = 0; i <= n; i++) r.Add((f.x1 + (f.x2 - f.x1) * i / n, f.y1 + (f.y2 - f.y1) * i / n));
            }
            return r;
        }

        /// <summary>The nearest point on a set of floors.</summary>
        static (float x, float y, float d) Nearest(float px, float py, List<(float x1, float y1, float x2, float y2, bool floor)> to)
        {
            float best = float.MaxValue, bx = 0, by = 0;
            foreach (var t in to)
            {
                float dx = t.x2 - t.x1, dy = t.y2 - t.y1, l = dx * dx + dy * dy;
                float k = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((px - t.x1) * dx + (py - t.y1) * dy) / l));
                float qx = t.x1 + dx * k, qy = t.y1 + dy * k, d = (qx - px) * (qx - px) + (qy - py) * (qy - py);
                if (d < best) { best = d; bx = qx; by = qy; }
            }
            return (bx, by, (float)Math.Sqrt(best));
        }

        /// <summary>The move (size s when scale is allowed, shift x, y) that best puts floors a on floors b: starts from
        /// lining up the widest floors, then repeatedly pairs every point with the nearest point on the other side (both
        /// ways round) and solves for the move that brings the pairs together (least squares). Pairs much further apart
        /// than the rest (a platform one map has and the other doesn't) stop counting after the first rounds.</summary>
        static (float s, float x, float y) Solve(List<(float x1, float y1, float x2, float y2, bool floor)> a, List<(float x1, float y1, float x2, float y2, bool floor)> b, bool scale)
        {
            // the same floor listed twice (some stages repeat a platform in several collision sets) counts once
            List<(float x1, float y1, float x2, float y2, bool floor)> Once(IEnumerable<(float x1, float y1, float x2, float y2, bool floor)> f) =>
                f.Where(x => x.floor).GroupBy(x => (Math.Round(x.x1, 2), Math.Round(x.y1, 2), Math.Round(x.x2, 2), Math.Round(x.y2, 2))).Select(g => g.First()).ToList();
            var fa = Once(a); var fb = Once(b);
            var pa = FloorPoints(fa); var pb = FloorPoints(fb);
            float ax0 = pa.Min(p => p.x), ax1 = pa.Max(p => p.x), bx0 = pb.Min(p => p.x), bx1 = pb.Max(p => p.x);
            float s = scale ? (bx1 - bx0) / Math.Max(1e-3f, ax1 - ax0) : 1;
            float x = (bx0 + bx1) / 2 - s * (ax0 + ax1) / 2;
            var la = fa.OrderByDescending(f => Math.Abs(f.x2 - f.x1)).First(); var lb = fb.OrderByDescending(f => Math.Abs(f.x2 - f.x1)).First();
            float y = (lb.y1 + lb.y2) / 2 - s * (la.y1 + la.y2) / 2;
            for (int round = 0; round < 40; round++)
            {
                var pairs = new List<(float px, float py, float qx, float qy, float d)>();
                var moved = Moved(fa, s, x, y);
                foreach (var (px, py) in pa) { var q = Nearest(px * s + x, py * s + y, fb); pairs.Add((px, py, q.x, q.y, q.d)); }
                foreach (var (qx, qy) in pb) { var p = Nearest(qx, qy, moved); pairs.Add(((p.x - x) / s, (p.y - y) / s, qx, qy, p.d)); }
                if (round >= 8)
                {
                    var ds = pairs.Select(p => p.d).OrderBy(d => d).ToList();
                    float cut = Math.Max(2f, ds[ds.Count / 2] * 3);
                    var kept = pairs.Where(p => p.d <= cut).ToList();
                    if (kept.Count >= pairs.Count / 2) pairs = kept;
                }
                double mpx = pairs.Average(p => p.px), mpy = pairs.Average(p => p.py), mqx = pairs.Average(p => p.qx), mqy = pairs.Average(p => p.qy);
                if (scale)
                {
                    double num = 0, den = 0;
                    foreach (var p in pairs) { num += (p.px - mpx) * (p.qx - mqx) + (p.py - mpy) * (p.qy - mqy); den += (p.px - mpx) * (p.px - mpx) + (p.py - mpy) * (p.py - mpy); }
                    if (den > 1e-6) s = (float)(num / den);
                }
                x = (float)(mqx - s * mpx); y = (float)(mqy - s * mpy);
            }
            return (s, x, y);
        }

        /// <summary>The map with its scenery moved by an alignment: the first bone of every model that draws something and
        /// isn't a gameplay part, and every animation key that moves those bones. Gameplay parts: position markers, the
        /// models collision rides on, and models the stage program names. looks: the map's looks go on P+'s file, where only
        /// its position markers and the models that move its platforms stay out (MoveKeep). Null (with why) when it can't be
        /// done safely.</summary>
        public static byte[] MoveLooks(byte[] map, Alignment al, ICollection<string> program, out string problem, bool looks = false)
        {
            problem = null;
            if (al == null || !al.Moves) return map;
            float s = al.Scale, dx = al.X, dy = al.Y;
            Installer.Quiet();
            var keep = new HashSet<string>(StringComparer.Ordinal);
            using (var tIn = new TempFile(".pac", map))
            using (var tOut = new TempFile(".pac"))
            {
                lock (BrawlGate.Lock)
                using (var root = NodeFactory.FromFile(null, tIn.Path))
                {
                    if (root == null) { problem = "The file couldn't be read."; return null; }
                    keep.UnionWith(MoveKeep(root, program, looks));
                    var models = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().ToList();
                    var moving = models.Where(m => !keep.Contains(m.Name) && (m.FindChild("Objects", false)?.Children.Count ?? 0) > 0).ToList();
                    if (moving.Count == 0) { problem = "It has no scenery of its own to move."; return null; }
                    // bones the gameplay parts have: an animation key for one of those names could move a gameplay part too
                    var kept = new HashSet<string>(models.Where(m => keep.Contains(m.Name)).SelectMany(m => m.BoneCache.Select(b => b.Name)), StringComparer.Ordinal);
                    var roots = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var m in moving)
                    {
                        m.Populate();
                        foreach (var bone in m.BoneCache.OfType<MDL0BoneNode>().Where(b => !(b.Parent is MDL0BoneNode)))
                        {
                            // (its looks on P+'s file: the parts kept here don't go over, so a shared name can't move them there)
                            if (!looks && kept.Contains(bone.Name)) { problem = $"Its scenery shares a bone name ({bone.Name}) with a part the match uses."; return null; }
                            roots.Add(bone.Name);
                            var t = bone.Translation; var sc = bone.Scale;
                            bone.Scale = new BrawlLib.Internal.Vector3(sc._x * s, sc._y * s, sc._z * s);
                            bone.Translation = new BrawlLib.Internal.Vector3(t._x * s + dx, t._y * s + dy, t._z * s);
                            // the bones' resting places (bind matrices) stay as they are: everything drawn goes with the bone's
                            // new place, also scenery bent by several bones, whose points are kept relative to those resting
                            // places (recalculating them moved that scenery 20-60 units off, 2026-11-04, see build/README.md)
                        }
                        m.SignalPropertyChange();
                    }
                    // animations that move those bones: their keys get the same move
                    foreach (var anim in root.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>())
                        foreach (var e in anim.Children.OfType<CHR0EntryNode>().Where(e => roots.Contains(e.Name)))
                        {
                            var keys = e.Keyframes;
                            int frames = Math.Max(1, anim.FrameCount);
                            void Each(int array, Func<float, float> value, float tangentScale)
                            {
                                for (int f = 0; f <= frames; f++)
                                {
                                    var k = keys.GetKeyframe(array, f);
                                    if (k == null) continue;
                                    k._value = value(k._value); k._tangent *= tangentScale;
                                }
                            }
                            if (!e.UseModelScale) for (int i = 0; i < 3; i++) Each(i, v => v * s, s);
                            if (!e.UseModelTranslate)
                            {
                                Each(6, v => v * s + dx, s);
                                Each(7, v => v * s + dy, s);
                                Each(8, v => v * s, s);
                            }
                            e.SignalPropertyChange();
                        }
                    root.Export(tOut.Path);
                }
                var moved = File.ReadAllBytes(tOut.Path);
                // nothing the match uses may have changed: every gameplay fingerprint the same, apart from the moved models
                // (its looks on P+'s file: the map's own gameplay isn't used at all, P+'s file is checked when it's made)
                var before = looks ? new Dictionary<string, string>() : Gameplay(map); var after = looks ? new Dictionary<string, string>() : Gameplay(moved);
                foreach (var k in before.Keys.Union(after.Keys))
                {
                    if (k == "outline") continue;
                    if (k.StartsWith("model ") && !keepModel(k)) continue;
                    if (!before.TryGetValue(k, out var x) || !after.TryGetValue(k, out var y) || x != y)
                    { problem = "Moving its scenery would have changed a part the match uses (" + k + ")."; return null; }
                }
                // and every piece of scenery has to land exactly where the move puts it (drawn at rest, bent scenery too)
                var was = Drawn(map, keep); var now = Drawn(moved, keep);
                foreach (var kv in was)
                {
                    if (!now.TryGetValue(kv.Key, out var b)) { problem = "Moving its scenery lost a piece of it (" + kv.Key + ")."; return null; }
                    var a = kv.Value;
                    float tol = 0.05f + 0.001f * Math.Max(Math.Abs(a.x0), Math.Max(Math.Abs(a.x1), Math.Max(Math.Abs(a.y0), Math.Abs(a.y1))));
                    if (Math.Abs(b.x0 - (a.x0 * s + dx)) > tol * (1 + s) || Math.Abs(b.x1 - (a.x1 * s + dx)) > tol * (1 + s) ||
                        Math.Abs(b.y0 - (a.y0 * s + dy)) > tol * (1 + s) || Math.Abs(b.y1 - (a.y1 * s + dy)) > tol * (1 + s))
                    { problem = $"Moving its scenery would have put a piece of it in the wrong place ({kv.Key})."; return null; }
                }
                return moved;

                bool keepModel(string key)
                {
                    string name = key.Substring(6); int at = name.IndexOf('@'); if (at >= 0) name = name.Substring(0, at);
                    return keep.Contains(name);
                }
            }
        }

        /// <summary>Where each drawn object of a stage's scenery is at rest (its box, x and y), by "model/object" (models in
        /// skip left out). Bent scenery is placed by its bones like the game does.</summary>
        static Dictionary<string, (float x0, float x1, float y0, float y1)> Drawn(byte[] stage, ICollection<string> skip)
        {
            var res = new Dictionary<string, (float, float, float, float)>(StringComparer.Ordinal);
            using (var t = new TempFile(".pac", stage))
            lock (BrawlGate.Lock)
            using (var root = NodeFactory.FromFile(null, t.Path))
                foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => !skip.Contains(m.Name)))
                {
                    m.Populate();
                    try { m.ApplyCHR(null, 0); } catch { continue; }
                    foreach (var o in m.FindChild("Objects", false)?.Children.OfType<MDL0ObjectNode>() ?? Enumerable.Empty<MDL0ObjectNode>())
                    {
                        var vs = o._manager?._vertices;
                        if (vs == null || vs.Count == 0) continue;
                        string key = m.Name + "/" + o.Name;
                        for (int n = 2; res.ContainsKey(key); n++) key = m.Name + "/" + o.Name + "#" + n;
                        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                        foreach (var v in vs)
                        {
                            var w = v.WeightedPosition;
                            if (float.IsNaN(w._x) || float.IsNaN(w._y) || Math.Abs(w._x) > 1e6f || Math.Abs(w._y) > 1e6f) continue;
                            x0 = Math.Min(x0, w._x); x1 = Math.Max(x1, w._x); y0 = Math.Min(y0, w._y); y1 = Math.Max(y1, w._y);
                        }
                        if (x0 <= x1) res[key] = (x0, x1, y0, y1);
                    }
                }
            return res;
        }
    }
}