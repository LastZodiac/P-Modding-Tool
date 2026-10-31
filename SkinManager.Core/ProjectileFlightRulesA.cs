// Flight rules: Mario, Luigi, Link, Toon Link, Pit, Samus, Zero Suit Samus (see ProjectileFlightRules.cs).
using System;
using System.Linq;

namespace SkinManager.Core
{
    public static partial class ProjectileFlight
    {
        static partial void LoadRulesA()
        {
            // ---- Mario, Luigi: fireball. wnMarioFireball: speed article param +0xC at +0x10 (radians), gravity +0x0, life
            // +0x14; each bounce keeps +0x8 of its speed and it's gone once slower than +0x18 (Luigi's has no gravity).
            foreach (var who in new[] { "Mario", "Luigi" })
                Add(who, 6, c =>
                {
                    float g = c.AF(0x0), keep = c.AF(0x8), sp = c.AF(0xC), a = c.AF(0x10), min = c.AF(0x18); int life = c.AI(0x14);
                    if (life <= 0 || float.IsNaN(sp)) return null;
                    float vx = (float)(sp * Math.Cos(a)), vy = (float)(sp * Math.Sin(a));
                    var f = Fly(c, Bouncing(life, vx, vy, g, 1000, keep, min), "Regular", g > 0 ? $"bounces along the floor, up to {life} frames" : $"flies straight for {life} frames ({life * sp:0} units)");
                    if (g > 0) f.PathForHeight = h => Bouncing(life, vx, vy, g, h, keep, min);
                    return f;
                });

            // ---- Link, Toon Link: arrow. sora_melee bow arrow fly: speed from article param +8 (no charge) to +0xC (full
            // charge), aimed fighter param 0 +0xC radians up, gravity article param +0x1C, life +0x4. Let go when the bow
            // is drawn back (the start of the "end" part); shown with no charge.
            foreach (var who in new[] { "Link", "ToonLink" })
                Add(who, 15, c =>
                {
                    float sp = c.AF(8), max = c.AF(0xC), g = c.AF(0x1C), a = c.FF(0, 0xC); int life = c.AI(4);
                    if (life <= 0 || float.IsNaN(sp)) return null;
                    int start = c.PartStart(c.Air ? "SpecialAirNEnd" : "SpecialNEnd"); if (start <= 0) start = c.PartStart("SpecialNEnd");
                    var f = Fly(c, Ballistic(life, (float)(sp * Math.Cos(a)), (float)(sp * Math.Sin(a)), g, 1000), "Fly", $"no charge: {sp:0.##} a frame, dropping a little; fully charged {max:0.##} a frame; {life} frames", start: Math.Max(1, start));
                    f.StopAtGround = true; return f;
                });

            // ---- Link, Toon Link: boomerang. wnLinkBoomerang: thrown at article param +0xC a frame, slowing by +0x1C a frame;
            // it comes back speeding up by +0x1C to +0x20, turning toward the thrower, until caught. Shown turning back when
            // it has slowed to a stop (the game's own turn-back timer isn't read anywhere in its code).
            foreach (var who in new[] { "Link", "ToonLink" })
                Add(who, 11, c =>
                {
                    float sp = c.AF(0xC), dec = c.AF(0x1C), back = c.AF(0x20);
                    if (float.IsNaN(sp) || dec <= 0) return null;
                    var path = new System.Collections.Generic.List<(float X, float Y)>(); float x = 0, v = sp; int outFrames = 0;
                    while (v > 0 && path.Count < 400) { path.Add((x, 0)); x += v; v -= dec; outFrames++; }
                    v = 0;
                    while (x > 0 && path.Count < 600) { path.Add((x, 0)); v = Math.Min(back, v + dec); x -= v; }
                    path.Add((0, 0));
                    return Fly(c, path, "Fly", $"out {path.Take(outFrames).Max(q => q.X):0} units slowing down, then back to the thrower", start: c.ShotFrame > 0 ? c.ShotFrame : 0, later: (outFrames + 1, "Turn"));
                });

            // ---- Pit: arrow. wnPitBowArrow fly: article param +4 a frame (straight: no gravity), forward or straight up
            // (+8 = 90° for the "up" version), life +0x24; let go at the fire part's shoot event.
            Add("Pit", 10, c =>
            {
                float sp = c.AF(4); int life = c.AI(0x24); if (life <= 0 || float.IsNaN(sp)) return null;
                bool up = c.Entry.Parts.Any(p => p.Contains("Hi"));
                return Fly(c, Straight(life, sp, up ? c.AF(8) : 0), "Fly", $"{life} frames at {sp:0.##} a frame ({life * sp:0} units); it can be steered", start: c.ShotFrame);
            });

            // ---- Samus: charge shot. sora_melee charge shot: straight, article param +8 a frame uncharged to +0xC at full
            // charge; it keeps going until it hits something. Let go at the shot part.
            Add("Samus", 8, c =>
            {
                bool full = c.Shot.Name.Contains("full");
                float sp = full ? c.AF(0xC) : c.AF(8); if (float.IsNaN(sp) || sp <= 0) return null;
                int start = c.ShotFrame > 0 ? c.ShotFrame : c.PartStart(c.Air ? "SpecialAirN" : "SpecialN");
                return Fly(c, Straight(Ctx.UntilOut(sp), sp, 0), full ? "Shoot7" : "Shoot0", $"keeps going at {sp:0.##} a frame until it hits something", start: start);
            });

            // ---- Samus: homing missile. Article param +0xC a frame; it homes in for +8 frames (straight with nobody to
            // chase), then slows by x(+0x10) a frame down to +0x14.
            Add("Samus", 12, c =>
            {
                float sp = c.AF(0xC), mul = c.AF(0x10), min = c.AF(0x14); int home = c.AI(8); if (float.IsNaN(sp)) return null;
                var path = new System.Collections.Generic.List<(float X, float Y)>(); float x = 0, v = sp;
                while (path.Count < 200 && x < Reach) { path.Add((x, 0)); x += v; if (path.Count > home) v = Math.Max(min, v * mul); }
                return Fly(c, path, "Homing", $"{sp:0.##} a frame, chasing the nearest opponent for {home} frames, then slowing");
            });

            // ---- Samus: super missile. Article param +0xC a frame for +8 frames, then speeds up by +0x10 a frame.
            Add("Samus", 20, c =>
            {
                float sp = c.AF(0xC), acc = c.AF(0x10); int slow = c.AI(8); if (float.IsNaN(sp)) return null;
                var path = new System.Collections.Generic.List<(float X, float Y)>(); float x = 0, v = sp;
                while (path.Count < 200 && x < Reach) { path.Add((x, 0)); x += v; if (path.Count > slow) v += acc; }
                return Fly(c, path, "Straight", $"{sp:0.##} a frame for {slow} frames, then speeds up");
            });

            // ---- Samus: bomb. Pops up at article param +0x18, falls with gravity +0x10 to +0x14 a frame, bursts after +0 frames.
            Add("Samus", 10, c =>
            {
                float vy = c.AF(0x18), g = c.AF(0x10), lim = c.AF(0x14); int fuse = c.AI(0); if (fuse <= 0 || float.IsNaN(vy)) return null;
                var path = Ballistic(fuse, 0, vy, g, lim);
                var f = Fly(c, path, "Fall", $"drops and bursts after {fuse} frames (or when touched)", later: (fuse - 4, "BurstAttack"));
                f.PathForHeight = h => Ballistic(fuse, 0, vy, g, lim, -h);
                return f;
            });

            // ---- Snake: Nikita missile. ft_snake missile activate: speeds up by article param +4 to +8 a frame on its
            // first frame and holds it, along the way it's aimed (straight ahead with no stick); it's steered with the stick
            // and keeps going until it hits something or Snake stops steering it.
            Add("Snake", 13, c =>
            {
                float sp = c.AF(8); if (float.IsNaN(sp) || sp <= 0) return null;
                return Fly(c, Straight(Ctx.UntilOut(sp), sp, 0), "Fly", $"{sp:0.##} a frame, steered with the stick, until it hits something");
            });

            // ---- Squirtle: Water Gun. sora_melee water activate: each blob comes out of node 8 at article param +0 a frame
            // (+ +4 more per unit of charge), aimed where the mouth bone points, falls by +8 a frame, lasts +0x10 frames.
            // One blob per "code makes its projectile" event in the script; the first uncharged one is drawn, straight ahead.
            Add("PokeZenigame", 5, c =>
            {
                float sp = c.AF(0), more = c.AF(4), g = c.AF(8); int life = c.AI(0x10);
                if (life <= 0 || life > 600 || float.IsNaN(sp)) return null;
                int blobs = c.Entry.Data.CodeShots.Count;
                return Fly(c, Ballistic(life, sp, 0, g, 1000), "Regular", $"{(blobs > 1 ? blobs + " blobs, " : "")}{sp:0.##} a frame uncharged (faster when charged), drooping, {life} frames each; shown going straight ahead", bone: 8);
            });

            // ---- Game & Watch: Chef's food. The move's script sets flag 0x22000011 and the fighter's action script (its every-frame
            // loop) makes the food then. Its action counts down the food's first int (article param +4) before it stops; it flies
            // along one of the speed records the article's +0xC points to (+0x10 of them, 0x14 bytes each: forward, up, gravity,
            // falling limit), picked at random. Drawn: the first record, from the pan's bone (29).
            Add("GameWatch", 6, c =>
            {
                int table = c.AI(0xC), n = c.AI(0x10), life = c.AI(4), start = c.FlagFrame(0x22000011);
                if (table <= 0 || n <= 0 || n > 16 || life <= 0 || life > 600 || start <= 0) return null;
                float vx = c.M.RawF(table), vy = c.M.RawF(table + 4), g = c.M.RawF(table + 8), lim = c.M.RawF(table + 12);
                if (float.IsNaN(vx) || float.IsNaN(vy) || Math.Abs(vx) > 20 || Math.Abs(vy) > 20) return null;
                return Fly(c, Ballistic(life, vx, vy, g, lim), "Sausage", $"one of {n} arcs picked at random (drawn: the first); flies {life} frames", start: start, bone: 29);
            });

            // ---- Zero Suit Samus: paralyzer. Straight, no gravity: article param +0x10 a frame (or +0x14 for the held
            // version), its script's life +0x18 (+0x1C); made at bone 54 + (fighter param 0 +0 forward, +4 up).
            Add("SZerosuit", 5, c =>
            {
                bool held = c.Entry.Parts.Any(p => p.EndsWith("H"));
                float sp = held ? c.AF(0x14) : c.AF(0x10); int life = held ? c.AI(0x1C) : c.AI(0x18);
                if (life <= 0 || float.IsNaN(sp)) return null;
                return Fly(c, Straight(life, sp, 0), held ? "ShootTame" : "Shoot", $"{life} frames at {sp:0.##} a frame ({life * sp:0} units)", bone: 54, fwd: c.FF(0, 0), up: c.FF(0, 4));
            });
        }
    }
}
