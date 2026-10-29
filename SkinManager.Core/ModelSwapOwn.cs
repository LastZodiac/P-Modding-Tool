// A model made from P+'s own model (saved with "Save P+ Model as 3D" and re-textured elsewhere) has P+'s exact
// shape: it needs no lining up, and each point can follow exactly the bones P+'s own point there follows.
using System;
using System.Collections.Generic;
using System.Linq;
using BrawlLib.Internal;

namespace SkinManager.Core
{
    public static partial class ModelSwap
    {
        sealed class ExactGrid
        {
            readonly Dictionary<long, List<RefPoint>> _cells = new Dictionary<long, List<RefPoint>>();
            readonly float _cell;
            public ExactGrid(List<RefPoint> pts, float cell)
            {
                _cell = Math.Max(1e-4f, cell);
                foreach (var p in pts) { long k = Key(p.P); if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<RefPoint>(); l.Add(p); }
            }
            long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);
            long Key(Vector3 v) => Key((int)Math.Floor(v._x / _cell), (int)Math.Floor(v._y / _cell), (int)Math.Floor(v._z / _cell));
            /// <summary>The closest point within one cell around (null if none).</summary>
            public RefPoint Near(Vector3 v, out float dist)
            {
                int cx = (int)Math.Floor(v._x / _cell), cy = (int)Math.Floor(v._y / _cell), cz = (int)Math.Floor(v._z / _cell);
                RefPoint best = null; float bd = float.MaxValue;
                for (int x = cx - 1; x <= cx + 1; x++) for (int y = cy - 1; y <= cy + 1; y++) for (int z = cz - 1; z <= cz + 1; z++)
                    if (_cells.TryGetValue(Key(x, y, z), out var l))
                        foreach (var p in l)
                        {
                            float dx = p.P._x - v._x, dy = p.P._y - v._y, dz = p.P._z - v._z, d = dx * dx + dy * dy + dz * dz;
                            if (d < bd) { bd = d; best = p; }
                        }
                dist = best == null ? float.MaxValue : (float)Math.Sqrt(bd);
                return best;
            }
        }

        static float Tolerance(List<RefPoint> refs)
        {
            float h = refs.Max(p => p.P._y) - refs.Min(p => p.P._y);
            return Math.Max(0.01f, h * 0.004f);   // 0.4% of the height: rounding, not a different shape
        }

        /// <summary>How much of the model sits exactly on P+'s own model's points (1 = it is P+'s shape).</summary>
        public static float SameShape(SwapMesh mesh, byte[] costume)
        {
            var refs = ReferencePoints(costume);
            float tol = Tolerance(refs);
            var grid = new ExactGrid(refs, tol * 2);
            int n = 0, on = 0;
            foreach (var part in mesh.Parts)
                for (int i = 0; i < part.P.Count; i += 7) { n++; if (grid.Near(part.P[i], out float d) != null && d <= tol) on++; }
            return n == 0 ? 0 : (float)on / n;
        }

        /// <summary>If the model is P+'s own shape (maybe moved or resized by the tool that re-textured it), that
        /// shape exactly where P+'s model stands; otherwise null.</summary>
        public static SwapMesh AsOwnShape(SwapMesh mesh, byte[] costume)
        {
            if (SameShape(mesh, costume) >= 0.9f) return mesh;
            // moved or resized as a whole: put its box on P+'s model's box (P+'s exported shape keeps its proportions)
            var target = TargetBox(costume);
            var (mn, mx) = mesh.Box();
            float h = mx._y - mn._y, th = target.Max._y - target.Min._y;
            if (h < 1e-6f) return null;
            float k = th / h;
            float cx = (mn._x + mx._x) / 2, cz = (mn._z + mx._z) / 2, tcx = (target.Min._x + target.Max._x) / 2, tcz = (target.Min._z + target.Max._z) / 2;
            var moved = mesh.Copy();
            foreach (var part in moved.Parts)
                for (int i = 0; i < part.P.Count; i++)
                {
                    var v = part.P[i];
                    part.P[i] = new Vector3(tcx + (v._x - cx) * k, target.Min._y + (v._y - mn._y) * k, tcz + (v._z - cz) * k);
                }
            return SameShape(moved, costume) >= 0.9f ? moved : null;
        }

        /// <summary>Each point follows exactly the bones of P+'s own point at the same place; anything elsewhere
        /// (an added part) uses the fallback.</summary>
        public static Func<Vector3, List<(string Bone, float W)>> OwnWeights(byte[] costume, Func<Vector3, List<(string Bone, float W)>> fallback)
        {
            var refs = ReferencePoints(costume);
            float tol = Tolerance(refs);
            var grid = new ExactGrid(refs, tol * 2);
            return v =>
            {
                var p = grid.Near(v, out float d);
                if (p != null && d <= tol * 1.5f) return p.Weights.Select(w => (w.Item1, w.Item2)).ToList();
                return fallback?.Invoke(v);
            };
        }
    }
}
