// The flight rules for each fighter's projectiles (see ProjectileFlight.cs). Numbers are read from the card where the
// game reads them ("article param" = the projectile's own parameter block, "fighter param N" = the fighter's own
// parameter block N); the comments say where in the game's code each rule comes from (module, function or address).
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public static partial class ProjectileFlight
    {
        static float Deg(float rad) => rad * 180f / (float)Math.PI;

        static partial void LoadRules()
        {
            // ---- Fox, Falco, Wolf: blaster shots. sora_melee wn<Fighter>BlasterBullet activate: straight, no gravity; speed =
            // fighter param 0 +0x14, angle (radians) = fighter param 0 +0x10, life = article param +0; made at the
            // blaster bone with a local offset (forward fighter param 0 +0x4, up +0x0 on the ground).
            foreach (var (who, slot, bone) in new[] { ("Fox", 9, 63), ("Falco", 7, 65), ("Wolf", 9, 60) })
                Add(who, slot, c =>
                {
                    int life = c.AI(0); float speed = c.FF(0, 0x14), ang = Deg(c.FF(0, 0x10));
                    if (life <= 0 || float.IsNaN(speed)) return null;
                    return Fly(c, Straight(life, speed, ang), "Fly", $"flies {life} frames at {speed:0.##} a frame ({life * speed:0} units)", bone: bone, fwd: c.Air ? c.FF(0, 0x8) : c.FF(0, 0x0), up: c.Air ? c.FF(0, 0xC) : c.FF(0, 0x4));
                });

            // ---- Fox, Falco: side B afterimage. It has no movement of its own: it's put on the fighter as he dashes
            // (wnFoxIllusionUtil); its hitbox is out for its script's frames.
            foreach (var (who, slot) in new[] { ("Fox", 10), ("Falco", 8) })
                Add(who, slot, c =>
                {
                    var f = Fly(c, Still(c.Air ? 4 : 8), c.Air ? "MoveAir" : "MoveGround", "goes with the fighter as he dashes");
                    f.FollowFighter = true; return f;
                });

            // ---- Pikachu: thunder jolt in the air. sora_melee jolt activate: straight at article param +4 (radians, -40°)
            // and +8 per frame, life article param +0; made at TopN + (forward fighter param 0 +0x0, up +0x4). On reaching
            // the floor it becomes the crawling jolt (its crawl isn't in the code the game reads from the card).
            Add("Pikachu", 5, c =>
            {
                int life = c.AI(0); float ang = Deg(c.AF(4)), sp = c.AF(8);
                // on the ground it's made on the floor and crawls along it at once (that crawl isn't in the code read here)
                if (life <= 0 || float.IsNaN(sp) || !c.Air) return null;
                var f = Fly(c, Straight(life, sp, ang), "Regular", $"flies down at {-ang:0}° until it reaches the floor, then crawls along it", bone: 0, fwd: c.FF(0, 0x0), up: c.FF(0, 0x4));
                f.StopAtGround = true; return f;
            });

            // ---- Ness, Lucas: PK fire bolt. ftNess/ftLucas activate: ground speed fighter param 1 +0xC at angle +0x8 (radians),
            // in the air speed +0x4 at angle +0x0; it starts at node 59 (Ness) / 57 (Lucas) + (forward fighter param 1
            // +0x10, up +0x14). Its life is its script's ("Shoot" removes it) or until it hits.
            foreach (var (who, bone) in new[] { ("Ness", 59), ("Lucas", 57) })
                Add(who, 8, c =>
                {
                    float sp = c.FF(1, 0xC), ang = Deg(c.FF(1, 0x8));
                    if (c.Air) { sp = c.FF(1, 0x4); ang = Deg(c.FF(1, 0x0)); }
                    if (float.IsNaN(sp) || sp <= 0) return null;
                    int life = c.ScriptLife("Shoot"); bool until = life <= 0; if (until) life = Ctx.UntilOut(sp);
                    return Fly(c, Straight(life, sp, ang), "Shoot", (until ? "keeps going until it hits something" : $"flies {life} frames") + $" at {sp:0.##} a frame; a pillar comes up where it hits", bone: bone, fwd: c.FF(1, 0x10), up: c.FF(1, 0x14));
                });

            // ---- Ness, Lucas: PK flash / freeze. wn_*_pkflash: thrown up at 90°+20° (toward the front) at 1.4, gravity
            // article param +0x18, falling limit +0x20, side speed limit; it grows while B is held (up to article param +0 =
            // 70 frames), then bursts ("Bang"). Shown held to the end.
            foreach (var (who, bone) in new[] { ("Ness", 42), ("Lucas", 39) })
                Add(who, 7, c =>
                {
                    int hold = Math.Max(1, (int)c.AF(0)); float g = c.AF(0x18), lim = c.AF(0x20);   // +0 is a float (70.0)
                    if (float.IsNaN(g)) return null;
                    double a = (90 - 20) * Math.PI / 180;   // 20° toward the front of straight up
                    var path = Ballistic(hold, (float)(1.4 * Math.Cos(a)), (float)(1.4 * Math.Sin(a)), g, lim);
                    int bang = Math.Max(1, c.ScriptLife("Bang")); var last = path[path.Count - 1];
                    for (int k = 0; k < Math.Max(8, bang); k++) path.Add(last);
                    return Fly(c, path, "Move", $"floats up and over, growing while B is held (up to {hold} frames), then bursts", bone: bone, up: 3, later: (hold + 1, "Bang"));
                });

            // ---- Ness, Lucas: PK thunder head. wn_*_pk_thunder move: article param +4 per frame, starting straight up and
            // turning with the stick (shown with no stick: straight up). It keeps going until it hits or is let go.
            foreach (var (who, bone) in new[] { ("Ness", 42), ("Lucas", 39) })
                Add(who, 9, c =>
                {
                    float sp = c.AF(4); if (float.IsNaN(sp) || sp <= 0) return null;
                    int life = Ctx.UntilOut(sp);
                    return Fly(c, Straight(life, sp, 90), "Move", $"goes straight up at {sp:0.##} a frame with no stick; it turns where the stick points", bone: bone);
                });

            // ---- Lucario, Mewtwo: aura sphere / shadow ball. sora_melee aura ball shoot: straight, speed from article param
            // +8 (no charge) to +0xC (full charge), angle +4; no time limit (it goes until it hits or leaves the stage).
            foreach (var who in new[] { "Lucario", "Mewtwo" })
                Add(who, 9, c =>
                {
                    float sp = c.AF(8), ang = Deg(c.AF(4)); if (float.IsNaN(sp) || sp <= 0) return null;
                    string shot = c.Shot.Name.Contains("full") ? "Shoot8" : "Shoot1";
                    if (c.Shot.Name.Contains("full")) sp = c.AF(0xC);
                    int life = Ctx.UntilOut(sp);
                    int start = c.PartStart(c.Air ? "SpecialAirNShoot" : "SpecialNShoot"); if (start <= 0) start = c.PartStart(c.Air ? "SpecialAirN" : "SpecialN");
                    return Fly(c, Straight(life, sp, ang), shot, $"keeps going at {sp:0.##} a frame until it hits something", start: start);
                });

            // ---- Ice Climbers: ice shot. sora_melee ice shot shoot: forward at article param +0 a frame; in the air it falls
            // (gravity article param +0xC, limit +0x10) and slides once it lands; it lasts 200 frames. Made at TopN +
            // (forward fighter param 4 +0x8, up +0xC).
            Add("Popo", 13, c =>
            {
                float sp = c.AF(0), g = Math.Abs(c.AF(0xC)), lim = Math.Abs(c.AF(0x10)), up = c.FF(4, 0xC);
                if (float.IsNaN(sp)) return null;
                int life = Ctx.UntilOut(sp);
                // on the ground it slides along the floor; in the air it falls first
                var path = c.Air ? Ballistic(life, sp, 0, g, lim, -up) : Straight(life, sp, 0);
                return Fly(c, path, "Shoot", $"slides along at {sp:0.##} a frame (falls first when made in the air)", bone: 0, fwd: c.FF(4, 0x8), up: c.Air ? up : 0);
            });

            // ---- Ice Climbers: blizzard. wnPopoBlizzard activate: article param +0 a frame at an angle between +0xC and
            // +0x8 degrees from straight down (picked at random: shown in the middle), +4 added to its rising speed each
            // frame; its script removes it. Made at node 39 + (fighter param 5 +0 forward, +4 up).
            Add("Popo", 12, c =>
            {
                float sp = c.AF(0), acc = c.AF(4), lo = c.AF(0xC), hi = c.AF(8); if (float.IsNaN(sp)) return null;
                int life = c.ScriptLife("Fly"); if (life <= 0) life = Ctx.UntilOut(sp);
                double a = ((lo + hi) / 2 - 90) * Math.PI / 180;
                return Fly(c, Ballistic(life, (float)(sp * Math.Cos(a)), (float)(sp * Math.Sin(a)), -acc, 1000), "Fly", $"each puff flies {life} frames at {sp:0.##} a frame, {90 - hi:0}-{90 - lo:0}° below level", bone: 39, fwd: c.FF(5, 0), up: c.FF(5, 4));
            });

            // ---- Yoshi: egg throw. ft_yoshi activeArticle: speed fighter param 1 +0x24 (no extra strength) at fighter param
            // 1 +0x20 radians (73°, no stick), gravity article param +0xC, falling limit +0x10; made at TopN + (fighter param
            // 1 +0x2C forward, +0x30 up). It bursts on contact ("Burst").
            Add("Yoshi", 6, c =>
            {
                float sp = c.FF(1, 0x24), a = c.FF(1, 0x20), g = c.AF(0xC), lim = c.AF(0x10), up = c.FF(1, 0x30);
                if (float.IsNaN(sp) || float.IsNaN(g)) return null;
                var path = Ballistic(200, (float)(sp * Math.Cos(a)), (float)(sp * Math.Sin(a)), g, lim);
                int land = path.FindIndex(q => q.Y < -up); if (land > 0) path = path.Take(land + 1).ToList();
                int burst = Math.Max(4, c.ScriptLife("Burst")); var last = path[path.Count - 1];
                int flyEnd = path.Count;
                for (int k = 0; k < burst; k++) path.Add(last);
                return Fly(c, path, "Throwed", $"arcs up and over (no stick), bursting where it lands", bone: 0, fwd: c.FF(1, 0x2C), up: up, later: (flyEnd + 1, "Burst"));
            });

            // ---- Yoshi: down B stars. wn_yoshi_star activate: article param +0 forward, +8 up, slowing +4 a frame, gravity
            // +0x10, falling limit +0xC; two of them, one each way; hitbox frames from its script.
            Add("Yoshi", 5, c =>
            {
                float vx = c.AF(0), vy = c.AF(8), ax = Math.Abs(c.AF(4)), g = c.AF(0x10), lim = c.AF(0xC);
                if (float.IsNaN(vx)) return null;
                var f = Fly(c, Ballistic(20, vx, vy, g, lim, null, 0, ax), "Move", "two stars, one to each side", bone: 0, fwd: 8, up: 1);
                return f;
            });

            // ---- Kirby: final cutter wave. wn_kirby_final_cutter: starts at article param +0 a frame and slows by +0xC a
            // frame (to 0.01); made 14 ahead of node 400.
            Add("Kirby", 43, c =>
            {
                float sp = c.AF(0), dec = c.AF(0xC); if (float.IsNaN(sp) || sp <= 0) return null;
                int life = Math.Min(120, (int)Math.Ceiling(sp / Math.Max(0.01f, dec)) + 1);
                return Fly(c, Straight(life, sp, 0, -dec, 0.01f), "FinalCutterRegular", $"slides along the floor, slowing down ({life} frames)", bone: 400, fwd: c.FF(2, 0x8));
            });

            // ---- Kirby, Dedede: spit star. wn_*_starmissile: article param +0 a frame, slowing by article param +4 until it stops.
            foreach (var (who, slot) in new[] { ("Kirby", 45), ("Dedede", 12) })
                Add(who, slot, c =>
                {
                    float sp = c.AF(0), dec = c.AF(4); if (float.IsNaN(sp) || sp <= 0) return null;
                    if (float.IsNaN(dec) || dec <= 0 || dec > sp) dec = 0.13f;
                    int life = Math.Min(120, (int)Math.Ceiling(sp / dec) + 1);
                    return Fly(c, Straight(life, sp, 0, -dec, 0), "Fly", $"flies forward, slowing to a stop ({life} frames)");
                });

            // ---- Dedede: super jump landing stars. wn_dedede_star: 1.5 a frame at 20°, one each way, life article param +0.
            Add("Dedede", 8, c =>
            {
                int life = c.AI(0); if (life <= 0) return null;
                return Fly(c, Straight(life, 1.5f, 20), "Fly", $"two stars, one each way, {life} frames", bone: 0, fwd: 7, up: 2);
            });

            // ---- Sheik: needles. sora_melee needle activate: article param +0xC a frame; on the ground straight ahead, in the
            // air 45° down; made at the fighter + (8 ahead, 8 up) (air 7, 6). No time limit.
            Add("Sheik", 5, c =>
            {
                float sp = c.AF(0xC); if (float.IsNaN(sp) || sp <= 0) return null;
                int life = Ctx.UntilOut(sp);
                var f = Fly(c, Straight(life, sp, c.Air ? -45 : 0), "Move", $"each needle flies at {sp:0.##} a frame until it hits something", bone: 0, fwd: c.Air ? c.FF(0, 0x8) : c.FF(0, 0x0), up: c.Air ? c.FF(0, 0xC) : c.FF(0, 0x4));
                f.StopAtGround = c.Air; return f;
            });

            // ---- R.O.B.: robo beam. wn_robot_beam: weak shot article param +0xC a frame for +8 frames, full charge +0x18 for
            // +0x14 frames; made at bone 77 + (forward 4, up 1.8).
            Add("Robot", 8, c =>
            {
                bool full = c.Shot.Name.Contains("full");
                float sp = full ? c.AF(0x18) : c.AF(0xC); int life = full ? c.AI(0x14) : c.AI(0x8);
                if (life <= 0 || float.IsNaN(sp)) return null;
                return Fly(c, Straight(life, sp, 0), full ? "FlyMax" : "Fly", $"{life} frames at {sp:0.##} a frame ({life * sp:0} units)", bone: 77, fwd: 4, up: 1.8f);
            });

            // ---- Snake: up smash mortar shells (made by the mortar). Straight up at article param +0x10 (no charge),
            // gravity +0x18, falling limit +0x1C; they burst on contact.
            Add("Snake", 7, c =>
            {
                float vy = c.AF(0x10), g = c.AF(0x18), lim = c.AF(0x1C); if (float.IsNaN(vy)) return null;
                var path = Ballistic(200, 0, vy, g, lim);
                int land = path.FindIndex(1, q => q.Y < 0); if (land > 0) path = path.Take(land).ToList();
                // it only bursts ("Impact") where it lands; flying it has no hitbox of its own
                int flying = path.Count, burst = Math.Max(4, c.ScriptLife("Impact")); var last = path[path.Count - 1];
                for (int k = 0; k < burst; k++) path.Add(last);
                int start = c.PartStart("AttackHi4"); if (start > 0) start += 5;
                return Fly(c, path, "Fly", "fired straight up, they come back down and burst", start: start, up: 8, later: (flying + 1, "Impact"));
            });

            // ---- Ivysaur: razor leaf. leaf cutter: forward at fighter param 2 +0 a frame, slowing article param +4 a frame,
            // life article param +0; made at the fighter + (fighter param 2 +0x10 forward, +0x14 up) (air +0x18, +0x1C).
            Add("PokeFushigisou", 4, c =>
            {
                int life = c.AI(0); float sp = c.FF(2, 0x0), dec = c.AF(4);
                if (life <= 0 || float.IsNaN(sp)) return null;
                return Fly(c, Straight(life, sp, 0, -dec, 0), "Move", $"{life} frames, slowing down", bone: 0, fwd: c.Air ? c.FF(2, 0x18) : c.FF(2, 0x10), up: c.Air ? c.FF(2, 0x1C) : c.FF(2, 0x14));
            });

            // ---- Ivysaur: bullet seed. Straight up at fighter param 1 +4 a frame from node 33 (+ fighter param 1 +8 up).
            Add("PokeFushigisou", 5, c =>
            {
                float sp = c.FF(1, 0x4); if (float.IsNaN(sp) || sp <= 0) return null;
                return Fly(c, Straight(Ctx.UntilOut(sp), sp, 90), "Move", $"seeds go straight up at {sp:0.##} a frame", bone: 33, up: c.FF(1, 0x8));
            });

            LoadRulesA();
        }

        static partial void LoadRulesA();
    }
}
