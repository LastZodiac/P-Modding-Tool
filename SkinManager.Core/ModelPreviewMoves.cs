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

        sealed class Shot { public List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Tex tex, bool xlu)> Tris; public List<(Vector3 p, float r, Color c)> Hits = new List<(Vector3, float, Color)>(); public List<(Vector3 a, Vector3 b, float r)> Hurt = new List<(Vector3, Vector3, float)>(); public int Body; public float Shift; public List<Hitbox> HitOf = new List<Hitbox>(); }

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
                var texCache = new Dictionary<string, Tex>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in move.Frames)
                {
                    if (cancelled?.Invoke() == true) return new List<Bitmap>();
                    anims.TryGetValue(f.Animation ?? "", out var anim);
                    model.ApplyCHR(anim, anim == null ? 0 : Math.Max(1, Math.Min(f.AnimFrame, anim.FrameCount)));
                    // the camera follows the fighter's root (TransN) sideways; the ground's marks show how far it moved
                    var tb = BoneAt(trans >= 0 ? trans : 0);
                    float shift = tb == null ? 0 : Yaw(tb._frameMatrix.GetPoint())._x;
                    var shot = new Shot { Shift = shift, Tris = Gather(root, new[] { model }, 90, texCache, new HashSet<string>(f.Hidden.Where(i => i >= 0 && i < bones.Length).Select(i => bones[i].Name))), Body = f.Body };
                    foreach (var h in f.Hitboxes)
                    {
                        var b = BoneAt(h.Bone); if (b == null) continue;
                        shot.Hits.Add((Place(b, h.X, h.Y, h.Z), h.Size, h.Grab ? Color.FromArgb(170, 80, 255) : HitColors[h.Id % HitColors.Length])); shot.HitOf.Add(h);
                    }
                    if (hurtboxes)
                        foreach (var hb in moves.HurtBoxes)
                        {
                            var b = BoneAt(hb.Bone); if (b == null) continue;
                            shot.Hurt.Add((Place(b, hb.X, hb.Y, hb.Z), Place(b, hb.X + hb.SX, hb.Y + hb.SY, hb.Z + hb.SZ), hb.Radius));
                        }
                    for (int k = 0; k < shot.Tris.Count; k++) { var t = shot.Tris[k]; t.a._x -= shift; t.b._x -= shift; t.c._x -= shift; shot.Tris[k] = t; }
                    for (int k = 0; k < shot.Hits.Count; k++) { var h = shot.Hits[k]; h.p._x -= shift; shot.Hits[k] = h; }
                    for (int k = 0; k < shot.Hurt.Count; k++) { var h = shot.Hurt[k]; h.a._x -= shift; h.b._x -= shift; shot.Hurt[k] = h; }
                    shots.Add(shot);
                }
            }
            // one view for the whole move, so the pictures don't jump
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            void Grow(float x, float y, float r) { minX = Math.Min(minX, x - r); maxX = Math.Max(maxX, x + r); minY = Math.Min(minY, y - r); maxY = Math.Max(maxY, y + r); }
            foreach (var s in shots)
            {
                foreach (var t in s.Tris) { Grow(t.a._x, t.a._y, 0); Grow(t.b._x, t.b._y, 0); Grow(t.c._x, t.c._y, 0); }
                foreach (var h in s.Hits) Grow(h.p._x, h.p._y, h.r);
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

        static Vector3 Scale(MDL0BoneNode b) { var s = new Vector3(1, 1, 1); for (var n = b; n != null; n = n.Parent as MDL0BoneNode) { var k = n._frameState._scale; s = new Vector3(s._x * k._x, s._y * k._y, s._z * k._z); } return s; }
        static float NZ(float v) => Math.Abs(v) < 1e-6f ? 1 : v;
        static Vector3 Yaw(Vector3 w) => new Vector3(w._z, w._y, -w._x);   // the same turn as Gather(…, 90): facing right
    }
}
