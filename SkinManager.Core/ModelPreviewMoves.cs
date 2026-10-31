// Draws a move frame by frame from the side, like a frame data site: the fighter posed on that frame, its hitboxes
// (coloured per hitbox id; grab boxes purple) and its hurtboxes (yellow, blue while intangible, green while invincible).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using BrawlLib.Internal;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static unsafe partial class ModelPreview
    {
        public static readonly Color[] HitColors = { Color.FromArgb(240, 60, 60), Color.FromArgb(255, 140, 30), Color.FromArgb(240, 60, 200), Color.FromArgb(60, 200, 90), Color.FromArgb(70, 150, 255), Color.FromArgb(255, 230, 60), Color.FromArgb(150, 90, 255), Color.FromArgb(40, 220, 220) };

        sealed class Shot { public List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)> Tris; public List<(Vector3 p, float r, Color c)> Hits = new List<(Vector3, float, Color)>(); public List<(Vector3 a, Vector3 b, float r)> Hurt = new List<(Vector3, Vector3, float)>(); public int Body; public float Shift; public List<Hitbox> HitOf = new List<Hitbox>(); public HashSet<int> Flying = new HashSet<int>(); public List<List<Vector3>> Trails = new List<List<Vector3>>(); public bool After; }

        /// <summary>Where things are in a move's pictures: a point (x, y) in game units (x forward from where the fighter
        /// stood, y up from the ground) is at pixel ((x - MinX) * Scale, (MaxY - y) * Scale). Each frame's hitboxes in pixels.</summary>
        public sealed class MoveCamera
        {
            public float MinX, MaxY, Scale;
            public int Width, Height;
            public List<List<(Hitbox Hit, float X, float Y, float R)>> Hits = new List<List<(Hitbox, float, float, float)>>();
            public PointF ToPicture(float x, float y) => new PointF((x - MinX) * Scale, (MaxY - y) * Scale);
            public PointF ToGame(float px, float py) => new PointF(px / Scale + MinX, MaxY - py / Scale);
        }

        /// <summary>One picture per frame of a move. motionFile: the fighter's Fit&lt;Name&gt;MotionEtc.pac; costume: Fit&lt;Name&gt;00.pac.</summary>
        public static List<Bitmap> RenderMove(byte[] costume, byte[] motionFile, Moveset moves, MoveData move, int width = 360, int height = 300, bool hurtboxes = true, Action<int, int> progress = null, Func<bool> cancelled = null, MoveCamera camera = null)
        {
            var shots = new List<Shot>();
            lock (BrawlGate.Lock)
            using (var mt = new TempFile(".pac", motionFile))
            using (var mroot = NodeFactory.FromFile(null, mt.Path))
            using (var tmp = new TempFile(".pac", costume))
            using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                Installer.Quiet();
                var anims = mroot.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>().GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault();
                if (model == null) return new List<Bitmap>();
                var bones = model._linker.BoneCache;
                int trans = Array.FindIndex(bones, b => b.Name == "TransN");
                MDL0BoneNode BoneAt(int i) { if (i >= 400) i -= 400;   // Kirby's and Wario's moves name bones through a table; without it, as BrawlBox does
                    if (i == 0 && trans >= 0) i = trans; return i >= 0 && i < bones.Length ? bones[i] as MDL0BoneNode : null; }
                Vector3 Place(MDL0BoneNode b, float x, float y, float z)
                {
                    var bp = b._frameMatrix.GetPoint(); var r = b._frameMatrix.GetRotationMatrix(); var s = Scale(b);
                    var g = r.Multiply(new Vector3(x / NZ(s._x), y / NZ(s._y), z / NZ(s._z)));
                    return Yaw(new Vector3(bp._x + g._x, bp._y + g._y, bp._z + g._z));
                }
                // a hitbox on a held thing (ZSS's whip): the thing's model, posed by its own animation, hanging off the fighter's bone
                var artModels = mroot.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                Vector3? ArticlePlace(Hitbox h)
                {
                    if (!artModels.TryGetValue(h.ArticleModel, out var am)) return null;
                    anims.TryGetValue(h.ArticleAnim ?? "", out var aa);
                    am.ApplyCHR(aa, aa == null ? 0 : Math.Max(1, Math.Min(h.ArticleFrame, aa.FrameCount)));
                    var abs = am._linker?.BoneCache;
                    var ab = abs != null && h.Bone >= 0 && h.Bone < abs.Length ? abs[h.Bone] as MDL0BoneNode : null;
                    var fb = BoneAt(h.AttachBone);
                    if (ab == null || fb == null) return null;
                    var bp = ab._frameMatrix.GetPoint(); var r = ab._frameMatrix.GetRotationMatrix(); var sc = Scale(ab);
                    var g = r.Multiply(new Vector3(h.X / NZ(sc._x), h.Y / NZ(sc._y), h.Z / NZ(sc._z)));
                    var local = new Vector3(bp._x + g._x, bp._y + g._y, bp._z + g._z);
                    // its animation is made around the fighter: its own "HaveN" follows the hand. Line that up with the fighter's
                    // hand (RHaveN/LHaveN) so the thing sits in the hand it's held in.
                    string handName = fb.Name.Length > 1 && (fb.Name[0] == 'R' || fb.Name[0] == 'L') ? fb.Name.Substring(1) : fb.Name;
                    var hand = abs.OfType<MDL0BoneNode>().FirstOrDefault(x => string.Equals(x.Name, handName, StringComparison.OrdinalIgnoreCase));
                    if (hand != null) local = fb._frameMatrix * (hand._frameMatrix.Invert() * local);
                    else local = fb._frameMatrix * local;
                    return Yaw(local);
                }
                var texCache = new Dictionary<string, Tex>(StringComparer.OrdinalIgnoreCase);
                var origins = new Dictionary<Projectile, Vector3>();
                Vector3 Origin(Projectile p)
                {
                    var b = p.Article?.Bone >= 0 ? BoneAt(p.Article.Bone) : null;
                    if (b == null) b = bones.OfType<MDL0BoneNode>().Where(x => x.Name.EndsWith("HaveN", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => Yaw(x._frameMatrix.GetPoint())._x).FirstOrDefault();
                    if (b == null) b = BoneAt(0);
                    return b == null ? new Vector3() : Yaw(b._frameMatrix.GetPoint());
                }
                // a move played in parts (start, dash, end): each part's animation moves the fighter from where the last one
                // left it (the game adds a part's root movement to the fighter's position), so it's carried over
                string lastAnim = null; float lastRootX = 0, carryX = 0;
                foreach (var f in move.Frames)
                {
                    if (cancelled?.Invoke() == true) return new List<Bitmap>();
                    anims.TryGetValue(f.Animation ?? "", out var anim);
                    model.ApplyCHR(anim, anim == null ? 0 : Math.Max(1, Math.Min(f.AnimFrame, anim.FrameCount)));
                    var rootBone = BoneAt(trans >= 0 ? trans : 0);
                    float rootX = rootBone == null ? 0 : Yaw(rootBone._frameMatrix.GetPoint())._x;
                    if (lastAnim != null && f.Animation != lastAnim) carryX += lastRootX;
                    lastAnim = f.Animation; lastRootX = rootX;
                    // the camera stays put and the fighter moves across it (a dash or a lunge shows how far it goes), with
                    // a mark on the ground every 5 units
                    float shift = 0;
                    var shot = new Shot { After = f.After, Shift = shift, Tris = Gather(root, new[] { model }, 90, texCache, new HashSet<string>(f.Hidden.Where(i => i >= 0 && i < bones.Length).Select(i => bones[i].Name))), Body = f.Body };
                    foreach (var h in f.Hitboxes)
                    {
                        if (h.ArticleModel != null)
                        {
                            var ap = ArticlePlace(h); if (ap == null) continue;
                            shot.Hits.Add((ap.Value, h.Size, HitColors[h.Id % HitColors.Length])); shot.HitOf.Add(h);
                            continue;
                        }
                        var b = BoneAt(h.Bone); if (b == null) continue;
                        shot.Hits.Add((Place(b, h.X, h.Y, h.Z), h.Size, h.Grab ? Color.FromArgb(170, 80, 255) : HitColors[h.Id % HitColors.Length])); shot.HitOf.Add(h);
                    }
                    int ownCount = shot.Hits.Count;
                    // what the move shoots or puts out (Fox's laser, ZSS's paralyzer shot...): from where it comes out (the bone
                    // it's made at, or the hand in front), facing the way the fighter faces; along its flight when the game's
                    // code for it is known, else just where it came out
                    // one whose frame the game decides (a boomerang coming back) is only drawn when nothing else is
                    bool known = move.Projectiles.Any(x => !x.Unknown);
                    foreach (var p in move.Projectiles.Where(x => !x.Unknown || !known))
                    {
                        if (p.Flight != null)
                        {
                            var fl = p.Flight;
                            int j = f.Frame - fl.Start + 1; if (j < 1 || j > fl.Life) continue;
                            Vector3 Start()
                            {
                                var sb = fl.Bone >= 0 ? BoneAt(fl.Bone) : null;
                                var basePos = sb != null ? Yaw(sb._frameMatrix.GetPoint()) : Origin(p);
                                return new Vector3(basePos._x + fl.Forward + f.MoveX + carryX, basePos._y + fl.Up + f.MoveY, basePos._z);
                            }
                            // one that stays on the fighter is placed from where the fighter is now, every frame
                            if (fl.FollowFighter) origins[p] = Start();
                            if (!origins.TryGetValue(p, out var fo))
                            {
                                origins[p] = fo = Start();
                                // a bouncing one: its path depends on how high above the floor it starts
                                if (fl.PathForHeight != null) { var np = fl.PathForHeight(Math.Max(0, fo._y)); for (int q = 0; q < fl.Path.Count; q++) fl.Path[q] = q < np.Count ? np[q] : np[np.Count - 1]; }
                            }
                            fo = origins[p];
                            var at = new Vector3(fo._x + fl.Path[j - 1].X, fo._y + fl.Path[j - 1].Y, fo._z);
                            // one that comes down to the floor ends there
                            if (fl.StopAtGround && j > 1 && at._y < 0) continue;
                            foreach (var h in fl.Hits[j - 1])
                            {
                                if (h.Grab) continue;
                                shot.Flying.Add(shot.Hits.Count);
                                shot.Hits.Add((new Vector3(at._x + h.Z, at._y + h.Y, at._z + h.X), h.Size, HitColors[h.Id % HitColors.Length])); shot.HitOf.Add(h);
                            }
                            // a faint line along where it has been
                            if (!fl.FollowFighter) shot.Trails.Add(fl.Path.Take(j).Select(q => new Vector3(fo._x + q.X, fo._y + q.Y, fo._z)).Where(v => !fl.StopAtGround || v._y >= 0).ToList());
                            continue;
                        }
                        int k = f.Frame - (p.Unknown ? 1 : p.Frame) + 1; if (k < 1) continue;
                        if (!origins.TryGetValue(p, out var o)) origins[p] = o = Origin(p) + new Vector3(f.MoveX + carryX, f.MoveY, 0);
                        var pf = p.Data.Frames.FirstOrDefault(x => x.Frame == k); if (pf == null) continue;
                        foreach (var h in pf.Hitboxes)
                        {
                            if (h.Grab) continue;
                            shot.Hits.Add((new Vector3(o._x + h.Z, o._y + h.Y, o._z + h.X), h.Size, HitColors[h.Id % HitColors.Length])); shot.HitOf.Add(h);
                        }
                    }
                    if (hurtboxes)
                        foreach (var hb in moves.HurtBoxes)
                        {
                            var b = BoneAt(hb.Bone); if (b == null) continue;
                            shot.Hurt.Add((Place(b, hb.X, hb.Y, hb.Z), Place(b, hb.X + hb.SX, hb.Y + hb.SY, hb.Z + hb.SZ), hb.Radius));
                        }
                    // the fighter itself is moved by the code's movement (only up/down shows: sideways the camera follows)
                    float fx = f.MoveX + carryX, fy = f.MoveY;
                    for (int k = 0; k < shot.Tris.Count; k++) { var t = shot.Tris[k]; t.a._x += fx - shift; t.b._x += fx - shift; t.c._x += fx - shift; t.a._y += fy; t.b._y += fy; t.c._y += fy; shot.Tris[k] = t; }
                    for (int k = 0; k < shot.Hits.Count; k++) { var h = shot.Hits[k]; if (k < ownCount) { h.p._x += fx; h.p._y += fy; } h.p._x -= shift; shot.Hits[k] = h; }
                    for (int k = 0; k < shot.Hurt.Count; k++) { var h = shot.Hurt[k]; h.a._x += fx - shift; h.b._x += fx - shift; h.a._y += fy; h.b._y += fy; shot.Hurt[k] = h; }
                    foreach (var tr in shot.Trails) for (int k = 0; k < tr.Count; k++) { var v = tr[k]; v._x -= shift; tr[k] = v; }
                    shots.Add(shot);
                }
            }
            // one view for the whole move, so the pictures don't jump
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            void Grow(float x, float y, float r) { minX = Math.Min(minX, x - r); maxX = Math.Max(maxX, x + r); minY = Math.Min(minY, y - r); maxY = Math.Max(maxY, y + r); }
            foreach (var s in shots)
            {
                foreach (var t in s.Tris) { Grow(t.a._x, t.a._y, 0); Grow(t.b._x, t.b._y, 0); Grow(t.c._x, t.c._y, 0); }
                // a projectile far out isn't allowed to shrink the fighter to a dot: the view takes in about a stage's
                // width around the fighter, and a little arrow at the edge shows it flying on
                for (int i = 0; i < s.Hits.Count; i++) { var h = s.Hits[i]; if (s.Flying.Contains(i) && (Math.Abs(h.p._x) > Reach || h.p._y > Reach || h.p._y < -Reach / 2)) continue; Grow(h.p._x, h.p._y, h.r); }
            }
            if (minX > maxX) return new List<Bitmap>();
            if (Environment.GetEnvironmentVariable("SKINMANAGER_DEBUG_BOUNDS") != null) Console.WriteLine($"bounds x {minX:0.0}..{maxX:0.0} y {minY:0.0}..{maxY:0.0}");
            // keep the picture's shape, a little margin, feet near the bottom
            float pad = Math.Max(maxX - minX, maxY - minY) * 0.06f; minX -= pad; maxX += pad; minY -= pad; maxY += pad;
            float aspect = width / (float)height, w = maxX - minX, hgt = maxY - minY;
            if (w / hgt < aspect) { float nw = hgt * aspect; minX -= (nw - w) / 2; maxX += (nw - w) / 2; } else { float nh = w / aspect; maxY += nh - hgt; }

            float scale = width / (maxX - minX);
            PointF S(Vector3 v) => new PointF((v._x - minX) * scale, (maxY - v._y) * scale);
            if (camera != null)
            {
                camera.MinX = minX; camera.MaxY = maxY; camera.Scale = scale; camera.Width = width; camera.Height = height;
                foreach (var s in shots) camera.Hits.Add(s.Hits.Select((h, i) => { var p = S(h.p); return (s.HitOf[i], p.X, p.Y, h.r * scale); }).ToList());
            }
            var pictures = new Bitmap[shots.Count];
            int done = 0;
            // each frame is drawn on its own: all the processor's cores at once
            System.Threading.Tasks.Parallel.For(0, shots.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, (k, state) =>
            {
                if (cancelled?.Invoke() == true) { state.Stop(); return; }
                var s = shots[k];
                var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var model = Draw(s.Tris, width, height, minX, maxX, minY, maxY, 1f, 1f, null))
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(44, 47, 58), Color.FromArgb(24, 26, 33), 90f)) g.FillRectangle(bg, 0, 0, width, height);
                    g.DrawImage(model, 0, 0, width, height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // ground line
                    using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
                    {
                        var y = S(new Vector3(0, 0, 0)).Y; g.DrawLine(pen, 0, y, width, y);
                        // a mark every 5 units, moving back as the fighter moves forward
                        for (float wx = (float)Math.Floor((minX + s.Shift) / 5) * 5; wx <= maxX + s.Shift; wx += 5)
                        { float px = (wx - s.Shift - minX) * scale; g.DrawLine(pen, px, y, px, y + 5); }
                    }
                    var hurtColor = s.Body == 1 ? Color.FromArgb(80, 60, 220, 90) : s.Body >= 2 ? Color.FromArgb(90, 70, 140, 255) : Color.FromArgb(38, 255, 220, 40);
                    foreach (var hb in s.Hurt)
                    {
                        var a = S(hb.a); var b = S(hb.b); float d = 2 * hb.r * scale;
                        using (var pen = new Pen(hurtColor, Math.Max(1, d)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 0.5f) using (var br = new SolidBrush(hurtColor)) g.FillEllipse(br, a.X - d / 2, a.Y - d / 2, d, d);
                            else g.DrawLine(pen, a, b);
                    }
                    foreach (var tr in s.Trails)
                        if (tr.Count > 1)
                            using (var pen = new Pen(Color.FromArgb(110, 255, 210, 90), 1.5f) { DashStyle = DashStyle.Dash })
                                g.DrawLines(pen, tr.Select(S).ToArray());
                    for (int i = 0; i < s.Hits.Count; i++)
                    {
                        if (!s.Flying.Contains(i)) continue;
                        var p = S(s.Hits[i].p);
                        if (p.X >= 0 && p.X <= width && p.Y >= 0 && p.Y <= height) continue;
                        // flying on past the picture's edge: an arrow there, pointing the way it went
                        using (var br = new SolidBrush(Color.FromArgb(230, s.Hits[i].c)))
                            if (p.X < 0 || p.X > width)
                            {
                                float ex = p.X < 0 ? 10 : width - 10, ey = Math.Max(12, Math.Min(height - 12, p.Y)); int dir = p.X < 0 ? -1 : 1;
                                g.FillPolygon(br, new[] { new PointF(ex + dir * 8, ey), new PointF(ex - dir * 4, ey - 7), new PointF(ex - dir * 4, ey + 7) });
                            }
                            else
                            {
                                float ex = Math.Max(12, Math.Min(width - 12, p.X)), ey = p.Y < 0 ? 10 : height - 10; int dir = p.Y < 0 ? -1 : 1;
                                g.FillPolygon(br, new[] { new PointF(ex, ey + dir * 8), new PointF(ex - 7, ey - dir * 4), new PointF(ex + 7, ey - dir * 4) });
                            }
                    }
                    if (s.After)
                        using (var br = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                        using (var fnt = new Font("Segoe UI", 9f))
                            g.DrawString("move over, projectile still out", fnt, br, 8, height - 22);
                    foreach (var h in s.Hits.OrderByDescending(x => x.r))
                    {
                        var p = S(h.p); float d = 2 * h.r * scale;
                        using (var br = new SolidBrush(Color.FromArgb(120, h.c))) g.FillEllipse(br, p.X - d / 2, p.Y - d / 2, d, d);
                        using (var pen = new Pen(Color.FromArgb(230, h.c), 1.5f)) g.DrawEllipse(pen, p.X - d / 2, p.Y - d / 2, d, d);
                    }
                }
                pictures[k] = bmp;
                progress?.Invoke(System.Threading.Interlocked.Increment(ref done) - 1, shots.Count);
            });
            if (cancelled?.Invoke() == true) { foreach (var b in pictures) b?.Dispose(); return new List<Bitmap>(); }
            var list = pictures.ToList();
            return list;
        }

        const float Reach = 70;    // how far from the fighter the picture follows a projectile (about half a stage each way)
        static Vector3 Scale(MDL0BoneNode b) { var s = new Vector3(1, 1, 1); for (var n = b; n != null; n = n.Parent as MDL0BoneNode) { var k = n._frameState._scale; s = new Vector3(s._x * k._x, s._y * k._y, s._z * k._z); } return s; }
        static float NZ(float v) => Math.Abs(v) < 1e-6f ? 1 : v;
        static Vector3 Yaw(Vector3 w) => new Vector3(w._z, w._y, -w._x);   // the same turn as Gather(…, 90): facing right
    }
}
