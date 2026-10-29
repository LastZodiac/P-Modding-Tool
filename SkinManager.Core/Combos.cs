// Launch maths and true combos. The rules (the Melee formulas P+ follows):
//   knockback = ((((p/10 + p*d/20) * 200/(weight+100) * 1.4) + 18) * KBG/100) + BKB   (p = percent after the hit)
//   with set knockback (WKB): p/10 + p*d/20 is replaced by 1 + WKB*10/20
//   hitstun = floor(knockback * 0.4); sent into the ground in tumble = knockdown (techable); launch speed = knockback * 0.03, slowing by 0.051 a frame; the target's own
//   gravity pulls it down meanwhile (up to its fall speed). 361 = 0° on the ground below 32 knockback, else 44°.
//   Shield stun = floor((damage + 4.45) / 2.235).
// A follow-up is a true combo when it can hit before the target can act again (hitstun over) and one of its
// hitboxes reaches where the target has been sent. Assumptions (shown on the page): no DI unless chosen, the target
// stands right in front of the hitbox, the attacker doesn't move between moves (beyond the move's own movement),
// aerials are taken as landed and L-cancelled right after the hit, a target in tumble that reaches the ground can tech.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class Body
    {
        public string Name;
        public float Weight = 100, Gravity = 0.1f, FallSpeed = 2, Size = 1;
        public int Jumpsquat = 4;
        public float JumpV = 3, HopV = 2, AirSpeed = 1;
        public float Width = 6, Height = 12;    // standing body in game units (from its hurtboxes)
        public static Body Of(FighterBook b) => new Body
        {
            Name = b.Character.Name, Weight = b.Moves.Attr(0xB0), Gravity = b.Moves.Attr(0x64), FallSpeed = b.Moves.Attr(0x68),
            Size = b.Moves.Attr(0xB4) > 0 ? b.Moves.Attr(0xB4) : 1, Jumpsquat = b.Moves.AttrInt(0x30),
            JumpV = b.Moves.Attr(0x38), HopV = b.Moves.Attr(0x44), AirSpeed = b.Moves.Attr(0x7C),
        };
    }

    public sealed class LaunchResult
    {
        public Hitbox Hit;
        public int HitFrame;              // the attacker's frame it hits on
        public float Knockback, Angle;    // degrees, 0 = forward, 90 = up
        public int Hitstun, Advantage, ShieldAdvantage;
        public bool Tumble, Grounded;     // tumble: 80+ knockback; grounded: stays on the ground (slides)
        public List<(float X, float Y)> Path = new List<(float, float)>();   // the target's middle, per frame after the hit (0 = when hit)
        public int TechFrame = -1;        // frame (after the hit) the target touches the ground in tumble: can tech from here
        public float StartX, StartY;      // the target's feet when hit
    }

    public sealed class FollowUp
    {
        public MoveEntry Move;
        public bool Works;
        public int Spare;                 // frames to spare (true combos), or how many frames late
        public int HitFrame;              // the follow-up's frame that connects
        public string Why;
        public string How;                // "short hop", "full hop" for aerials
    }

    public static class Combos
    {
        /// <summary>When the attacker can start something else: the move's first actionable frame, or for an aerial
        /// landing right after the hit with an L-cancel (half landing lag).</summary>
        public static int Ready(MoveData move, int hitFrame) => move.LandingLag > 0 ? hitFrame + 1 + move.LandingLag / 2 : move.Faf;

        public static float Knockback(Hitbox h, float percentBefore, float weight)
        {
            float d = float.IsNaN(h.Damage) ? 0 : h.Damage;
            float p = percentBefore + d;
            float core = h.Wkb > 0 ? 1 + h.Wkb * 10f / 20f : p / 10f + p * d / 20f;
            return ((core * 200f / (weight + 100f) * 1.4f) + 18f) * h.Kbg / 100f + h.Bkb;
        }

        public static int ShieldStun(float damage) => (int)Math.Floor(((float.IsNaN(damage) ? 0 : damage) + 4.45f) / 2.235f);

        /// <summary>Where a hit sends the target, from where it stood (feet at x,y). di: -1 towards the attacker, +1 away, 0 none (18° at most).</summary>
        public static LaunchResult Launch(MoveData move, Hitbox h, int hitFrame, float percentBefore, Body target, float footX, float footY, int di = 0)
        {
            var r = new LaunchResult { Hit = h, HitFrame = hitFrame, StartX = footX, StartY = footY };
            r.Knockback = Knockback(h, percentBefore, target.Weight);
            bool airborne = footY > 0.01f;
            float angle = h.Angle;
            if (h.Angle == 361) angle = airborne ? 45 : r.Knockback < 32 ? 0 : 44;
            else if (h.Angle > 361 || h.Angle < 0) angle = ((h.Angle % 360) + 360) % 360;
            if (di != 0)
            {
                // DI: up to 18° towards (-1) or away from (+1) the attacker, as far as the launch allows
                float towards = di > 0 ? 0 : 180;
                float diff = ((towards - angle + 540) % 360) - 180;
                angle += Math.Sign(diff) * Math.Min(18, Math.Abs(diff));
            }
            r.Angle = angle;
            r.Hitstun = (int)Math.Floor(r.Knockback * 0.4f);
            r.Tumble = r.Knockback >= 80;
            int faf = Ready(move, hitFrame);
            r.Advantage = hitFrame + r.Hitstun - (faf - 1);
            r.ShieldAdvantage = hitFrame + ShieldStun(h.Damage) - (faf - 1);
            // the flight
            double rad = angle * Math.PI / 180;
            float speed = r.Knockback * 0.03f;
            float vx = (float)Math.Cos(rad) * speed, vy = (float)Math.Sin(rad) * speed, sy = 0;
            if (!airborne && vy < -0.0001f)
            {
                // sent into the ground: in tumble it bounces back up (a bit weaker), otherwise it just slides
                // sent into the ground: in tumble it hits the ground at once (a knockdown it can tech: a tech chase,
                // not a true combo); otherwise it just slides
                if (r.Tumble) { r.TechFrame = 1; vy = 0; } else vy = 0;
            }
            r.Grounded = !airborne && vy <= 0.0001f;
            float x = footX, y = footY;
            r.Path.Add((x, y + target.Height / 2));
            for (int t = 1; t <= r.Hitstun + 2; t++)
            {
                if (!r.Grounded) { sy = Math.Max(-target.FallSpeed, sy - target.Gravity); }
                x += vx; y += r.Grounded ? 0 : vy + sy;
                if (y <= 0 && !r.Grounded)
                {
                    y = 0;
                    if (r.Tumble && t > 1 && r.TechFrame < 0) r.TechFrame = t;
                    if (vy + sy < 0) { sy = 0; if (vy < 0) vy = 0; }
                }
                // knockback slows down
                float sp = (float)Math.Sqrt(vx * vx + vy * vy);
                if (sp > 0) { float k = Math.Max(0, sp - 0.051f) / sp; vx *= k; vy *= k; }
                r.Path.Add((x, y + target.Height / 2));
            }
            return r;
        }

        /// <summary>The hitbox that hits first and where the target stands for it: right in front, overlapping it a little.</summary>
        public static (int Frame, Hitbox Hit, float FootX, float FootY)? FirstContact(MoveEntry move, List<FramePlace> places, Body target, int hitboxId = -1)
        {
            var d = move.Data;
            if (d.Throw != null && d.ThrowFrame > 0) return (d.ThrowFrame, d.Throw, target.Width / 2 + 3, 0);   // held just in front
            for (int i = 0; i < d.Frames.Count && i < places.Count; i++)
            {
                var hits = places[i].Hits.Where(h => !h.Hit.Grab && (hitboxId < 0 || h.Hit.Id == hitboxId)).OrderBy(h => h.Hit.Id).ToList();
                if (hits.Count == 0) continue;
                var h = hits[0];
                // the target's front edge just inside the hitbox, standing on the ground (or as high as the hitbox needs)
                float foot = Math.Max(0, h.Y - target.Height + h.R * 0.5f);
                if (move.Group == "Aerials") foot = 0;   // taken as landed on a standing opponent
                return (d.Frames[i].Frame, h.Hit, h.X + h.R * 0.5f + target.Width / 2, foot);
            }
            return null;
        }

        /// <summary>Which moves combo after this one (at this percent).</summary>
        public static List<FollowUp> FollowUps(FighterBook attacker, Dictionary<MoveData, List<FramePlace>> places, MoveEntry first, LaunchResult launch, Body target)
        {
            var list = new List<FollowUp>();
            var d = first.Data;
            // when the attacker can start the next move, and from where
            int ready = Ready(d, launch.HitFrame);
            float fromX = 0;
            if (d.LandingLag > 0) { }
            else if (places.TryGetValue(d, out var pl) && pl.Count > 0) fromX = pl[pl.Count - 1].RootX;           // moved forward (dash attack...)
            int canAct = launch.HitFrame + launch.Hitstun;     // the target's last frame of hitstun
            var me = attacker.Body ?? new Body();
            foreach (var m in attacker.Entries)
            {
                if (m.Group == "Dodges & getups" || m.Name.StartsWith("Pummel") || m.Name.Contains("throw")) continue;
                if (m.Name.StartsWith("Jab 2") || m.Name.StartsWith("Jab 3") || m.Name == "Rapid jab end") continue;
                if (!places.TryGetValue(m.Data, out var mp)) continue;
                bool aerial = m.Group == "Aerials";
                FollowUp best = null;
                foreach (var hop in aerial ? new[] { "short hop", "full hop" } : new string[] { null })
                {
                    var f = Try(m, mp, hop);
                    if (best == null || f.Works && (!best.Works || f.Spare > best.Spare) || !best.Works && !f.Works && f.Spare > best.Spare) best = f;
                }
                list.Add(best);
            }

            FollowUp Try(MoveEntry m, List<FramePlace> mp, string hop)
            {
                var f = new FollowUp { Move = m, How = hop, Spare = int.MinValue };
                int start = ready + (hop != null ? me.Jumpsquat : 0);   // the follow-up's frame 1 (aerials: first frame in the air)
                float v0 = hop == "full hop" ? me.JumpV : me.HopV;
                int late = int.MaxValue; bool tech = false;
                for (int i = 0; i < m.Data.Frames.Count && i < mp.Count; i++)
                {
                    if (mp[i].Hits.Count == 0) continue;
                    int when = start - 1 + m.Data.Frames[i].Frame;
                    int after = when - launch.HitFrame;
                    if (after < 1) continue;
                    if (launch.TechFrame > 0 && after >= launch.TechFrame) { late = Math.Min(late, after - launch.TechFrame + 1); tech = true; break; }
                    if (when > canAct) { late = Math.Min(late, when - canAct); break; }
                    var (tx, ty) = launch.Path[Math.Min(after, launch.Path.Count - 1)];
                    float bx0 = tx - target.Width / 2, bx1 = tx + target.Width / 2, by0 = ty - target.Height / 2, by1 = ty + target.Height / 2;
                    // aerials: the attacker rises with the jump and can drift sideways
                    int air = m.Data.Frames[i].Frame;   // frames in the air
                    float lift = hop == null ? 0 : Math.Max(0, v0 * air - me.Gravity * air * (air + 1) / 2);
                    float drift = hop == null ? 0 : me.AirSpeed * air * 0.8f;
                    foreach (var h in mp[i].Hits)
                    {
                        float hx = fromX + h.X, hy = h.Y + lift;
                        // the closest the hitbox can get to the body (drifting counts for aerials)
                        float gapX = hx < bx0 ? bx0 - hx : hx > bx1 ? hx - bx1 : 0;
                        gapX = Math.Max(0, gapX - drift);
                        float gapY = hy < by0 ? by0 - hy : hy > by1 ? hy - by1 : 0;
                        if (gapX * gapX + gapY * gapY <= h.R * h.R)
                        {
                            f.Works = true; f.HitFrame = m.Data.Frames[i].Frame; f.Spare = canAct - when;
                            f.Why = h.Hit.Grab ? "grabs" : "hits";
                            return f;
                        }
                    }
                }
                f.Why = late == int.MaxValue ? "out of reach" : tech ? "they can tech first" : $"{late} frame{(late == 1 ? "" : "s")} too late";
                f.Spare = late == int.MaxValue ? -999 : -late;
                return f;
            }
            return list.OrderByDescending(x => x.Works).ThenByDescending(x => x.Spare).ToList();
        }

        /// <summary>From 0 to 200%: at which percents each follow-up true combos.</summary>
        public static Dictionary<MoveEntry, List<(int From, int To)>> Ranges(FighterBook attacker, Dictionary<MoveData, List<FramePlace>> places, MoveEntry first, Body target, int hitboxId, int di, int step = 2)
        {
            var result = new Dictionary<MoveEntry, List<(int, int)>>();
            if (!places.TryGetValue(first.Data, out var pl)) return result;
            var c = FirstContact(first, pl, target, hitboxId);
            if (c == null) return result;
            for (int p = 0; p <= 200; p += step)
            {
                var l = Launch(first.Data, c.Value.Hit, c.Value.Frame, p, target, c.Value.FootX, c.Value.FootY, di);
                foreach (var f in FollowUps(attacker, places, first, l, target).Where(x => x.Works))
                {
                    if (!result.TryGetValue(f.Move, out var r)) result[f.Move] = r = new List<(int, int)>();
                    if (r.Count > 0 && r[r.Count - 1].Item2 == p - step) r[r.Count - 1] = (r[r.Count - 1].Item1, p);
                    else r.Add((p, p));
                }
            }
            return result;
        }
    }
}
