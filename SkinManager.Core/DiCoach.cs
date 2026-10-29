// How to DI (and SDI) a hit. DI (directional influence): holding the stick while you're sent flying turns the launch
// by up to 18°, as much as the stick points across (at a right angle to) the way you're sent; holding along it does
// nothing. So the stick is held at a right angle to the launch, on the side that turns it where you want:
//   to survive, the way that keeps the flight farthest from the blast zones (worked out by trying every direction:
//   usually "up and in" for sideways hits; for straight-up ones it depends on how fast the fighter falls);
//   to get out of a combo, towards level and away, so you fly out of their reach instead of staying in front of them.
// SDI (smash DI): during hitlag (the freeze when a hit lands) each tap of the stick shifts you a little; strong on
// moves that hit many times (tap out of them) and to stay near the stage.
// Kill percents are from the middle of a Battlefield-sized stage (blast zones 224 to each side, 200 up, 108 down),
// with the Melee rules P+ follows (see Combos).
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public static class DiCoach
    {
        public const float Side = 224, Top = 200, Bottom = -108, Edge = 68;
        public const float MaxDi = 18;

        static float Norm(float a) => ((a % 360) + 360) % 360;
        /// <summary>How far b is from a, -180..180 (positive = turning anticlockwise).</summary>
        static float Diff(float from, float to) => ((to - from + 540) % 360) - 180;

        /// <summary>The launch angle a hit really has (361 = Sakurai angle: 0° on the ground below 32 knockback, else 44°;
        /// in the air 45°).</summary>
        public static float Angle(Hitbox h, float knockback, bool airborne) =>
            h.Angle == 361 ? (airborne ? 45 : knockback < 32 ? 0 : 44) : Norm(h.Angle);

        /// <summary>The launch angle after holding the stick at stickAngle (null: not held).</summary>
        public static float WithDi(float launch, float? stickAngle)
        {
            if (stickAngle == null) return launch;
            float across = (float)Math.Sin((stickAngle.Value - launch) * Math.PI / 180);   // -1..1: how much the stick points across
            return Norm(launch + MaxDi * across);
        }

        /// <summary>How close a flight comes to a blast zone (1 = reaches it).</summary>
        public static float Danger(List<(float X, float Y)> path, float knockback)
        {
            int stun = (int)(knockback * 0.4f); float worst = 0;
            for (int i = 0; i < path.Count; i++)
            {
                var (x, y) = path[i];
                worst = Math.Max(worst, Math.Max(Math.Abs(x) / Side, y / Top));
                if (i < stun && y < 0) worst = Math.Max(worst, y / Bottom);
            }
            return worst;
        }

        /// <summary>Where to hold the stick to live longest at this percent: every direction is tried (and none), the one
        /// whose flight stays farthest from the blast zones wins.</summary>
        public static float? SurvivalStick(Hitbox h, Body t, bool airborne, float percent)
        {
            float kb = Combos.Knockback(h, percent, t.Weight), launch = Angle(h, kb, airborne);
            float? best = null; float bestDanger = Danger(Fly(kb, launch, t, 0, airborne ? 20 : 0, out _), kb) - 0.002f;
            for (int k = 0; k < 32; k++)
            {
                float stick = k * 360f / 32;
                float d = Danger(Fly(kb, WithDi(launch, stick), t, 0, airborne ? 20 : 0, out _), kb);
                if (d < bestDanger) { bestDanger = d; best = stick; }
            }
            return best;
        }

        /// <summary>To escape a combo: towards level, away (forward if sent forward, back if sent back).</summary>
        public static float EscapeTarget(float launch)
        {
            launch = Norm(launch);
            return launch <= 90 || launch >= 270 ? 0 : 180;
        }

        /// <summary>Where to hold the stick to turn a launch towards an angle: at a right angle to the launch, on that side
        /// (null when it's already there).</summary>
        public static float? StickFor(float launch, float target)
        {
            float d = Diff(launch, target);
            if (Math.Abs(d) < 0.5f) return null;
            return Norm(launch + (d > 0 ? 90 : -90));
        }

        /// <summary>A flight: the knockback's push (slowing by 0.051 a frame) plus the fighter's own fall, from (x, y).
        /// Stops when they land on the stage, start falling once the push is spent, or reach a blast zone (dies = true).</summary>
        public static List<(float X, float Y)> Fly(float knockback, float angle, Body t, float x, float y, out bool dies, int maxFrames = 400)
        {
            var path = new List<(float, float)> { (x, y) };
            double rad = angle * Math.PI / 180;
            float speed = knockback * 0.03f;
            float vx = (float)Math.Cos(rad) * speed, vy = (float)Math.Sin(rad) * speed, sy = 0;
            bool grounded = y <= 0.01f && vy <= 0.0001f && knockback < 80;
            if (y <= 0.01f && vy < 0 && knockback >= 80) vy = -vy * 0.8f;   // sent into the ground in tumble: it bounces up
            dies = false;
            int stun = (int)(knockback * 0.4f);   // below the stage they can only be lost while still in hitstun
            for (int f = 0; f < maxFrames; f++)
            {
                if (!grounded) sy = Math.Max(-t.FallSpeed, sy - t.Gravity);
                x += vx; y += grounded ? 0 : vy + sy;
                float sp = (float)Math.Sqrt(vx * vx + vy * vy);
                if (sp > 0) { float k = Math.Max(0, sp - 0.051f) / sp; vx *= k; vy *= k; }
                if (Math.Abs(x) > Side || y > Top || y < Bottom && f < stun) { dies = true; path.Add((x, y)); break; }
                if (sp <= 0.05f && f > 10 && path.Count > 1 && y < path[path.Count - 1].Item2 && Math.Abs(x) > Edge) { path.Add((x, y)); break; }   // falling off the stage: from here they recover
                if (y <= 0 && Math.Abs(x) <= Edge && vy + sy <= 0) { y = 0; path.Add((x, y)); if (sp < 0.05f || grounded) break; vy = 0; sy = 0; continue; }
                path.Add((x, y));
                if (sp < 0.01f && y > 0 && f > 120) break;
            }
            return path;
        }

        /// <summary>The first percent (before the hit) this hit kills at, hit in the middle of the stage, with the stick
        /// held as given (from the percent and the launch angle). Null if not before 300%.</summary>
        public static int? KillPercent(Hitbox h, Body target, bool airborne, Func<int, float, float?> stick)
        {
            for (int p = 0; p <= 300; p += 1)
            {
                float kb = Combos.Knockback(h, p, target.Weight);
                float launch = Angle(h, kb, airborne);
                float a = WithDi(launch, stick(p, launch));
                Fly(kb, a, target, 0, airborne ? 20 : 0, out bool dies);
                if (dies) return p;
            }
            return null;
        }

        /// <summary>Hitlag: the frames both fighters freeze when it lands (Melee: damage / 3 + 3, times the hitbox's own
        /// multiplier); each one is a chance to SDI.</summary>
        public static int Hitlag(Hitbox h) => (int)Math.Floor(((float.IsNaN(h.Damage) ? 0 : h.Damage) / 3f + 3) * (h.Hitlag > 0 ? h.Hitlag : 1));

        /// <summary>How far one SDI tap moves you (Melee: 6 units, times the hitbox's SDI multiplier).</summary>
        public static float SdiStep(Hitbox h) => 6f * (h.Sdi > 0 ? h.Sdi : 1);

        /// <summary>Does the move hit more than once (so SDI out of it is the point)?</summary>
        public static bool MultiHit(MoveData d)
        {
            int windows = 0; bool on = false;
            foreach (var f in d.Frames) { bool hit = f.Hitboxes.Any(x => !x.Grab); if (hit && !on) windows++; on = hit; }
            return windows >= 3 || d.Hits.Count(x => !x.Grab && x.Damage <= 3) >= 2 && windows >= 2;
        }

        /// <summary>A stick direction in words ("up and away"...), for a fighter hit by someone facing right.</summary>
        public static string Words(float? stick)
        {
            if (stick == null) return "no DI needed";
            float a = Norm(stick.Value);
            string[] names = { "away", "up and away", "up", "up and in", "in", "down and in", "down", "down and away" };
            return names[(int)Math.Round(a / 45) % 8];
        }
    }
}
