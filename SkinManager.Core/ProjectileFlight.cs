// How projectiles fly once they're out. The game's code moves them, so their speed, angle, gravity and how long they
// last aren't in the move's script: they come from each projectile's own parameters on the card (read live, so a
// changed build shows its own numbers) or from constants in the game's code. Every rule below was read from the
// game's code (Brawl's modules and Project+'s replacements); a projectile with no rule here is shown where it comes out.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public static partial class ProjectileFlight
    {
        /// <summary>A rule: which fighter and article, and how to fly it.</summary>
        sealed class Rule
        {
            public string Fighter; public int Slot;
            public Func<Ctx, Flight> Make;
        }

        /// <summary>What a rule gets to work with.</summary>
        public sealed class Ctx
        {
            public FighterBook Book; public MoveEntry Entry; public Projectile Shot; public Article Article;
            public Moveset M => Book.Moves;
            public float AF(int off) => M.ArticleF(Article, off);
            public int AI(int off) => M.ArticleI(Article, off);
            public float FF(int slot, int off) => M.FighterF(slot, off);
            public int FI(int slot, int off) => M.FighterI(slot, off);
            /// <summary>The move's first frame playing this animation (0: not in the move).</summary>
            public int PartStart(string anim) => Entry.Data.Frames.FirstOrDefault(f => string.Equals(f.Animation, anim, StringComparison.OrdinalIgnoreCase))?.Frame ?? 0;
            /// <summary>The frame an article script removes the article on (0: it doesn't).</summary>
            public int ScriptLife(string name)
            {
                var s = Article.Subactions.FirstOrDefault(x => x.Animation == name);
                return s == null ? 0 : FrameData.Run(M, name, new[] { s }, _ => 600).RemovedAt;
            }
            /// <summary>Frames to show one that keeps going until it hits something: until it's well past the picture.</summary>
            public static int UntilOut(float speed) => speed <= 0.01f ? 60 : Math.Max(10, Math.Min(200, (int)Math.Ceiling(Reach / speed)));
            public bool Air => Entry.Parts.Any(p => p.Contains("Air"));
            /// <summary>The move's first "let go of an article" event (0: none).</summary>
            /// <summary>The move's first frame setting this flag (0: it doesn't).</summary>
            public int FlagFrame(int v) => Entry.Data.FlagsSet.Where(x => x.Var == v).Select(x => x.Frame).DefaultIfEmpty(0).Min();
            public int ShotFrame => Entry.Data.Shots.Select(x => x.Frame).DefaultIfEmpty(0).Min();
            /// <summary>An article script's hitboxes frame by frame (frame 1 = its first).</summary>
            public List<List<Hitbox>> Script(string name, int frames)
            {
                var s = Article.Subactions.FirstOrDefault(x => x.Animation == name);
                var list = new List<List<Hitbox>>();
                if (s == null) { for (int k = 0; k < frames; k++) list.Add(new List<Hitbox>()); return list; }
                // run as long as the flight (a script looping forever stops there)
                var d = FrameData.Run(M, name, new[] { s }, _ => frames);
                for (int k = 1; k <= frames; k++)
                {
                    var f = d.Frames.LastOrDefault(x => x.Frame <= k);
                    // past the end of its script, the last hitboxes stay out (they're removed by the game when it hits or vanishes)
                    list.Add(f == null ? new List<Hitbox>() : f.Hitboxes.Select(h => h.Copy()).ToList());
                }
                return list;
            }
        }

        public const float Reach = 140;   // how far a flight is followed when nothing stops it
        static readonly List<Rule> Rules = new List<Rule>();
        static void Add(string fighter, int slot, Func<Ctx, Flight> make) => Rules.Add(new Rule { Fighter = fighter, Slot = slot, Make = make });

        /// <summary>Works out each projectile's flight on this move, when there's a rule for it.</summary>
        public static void Apply(FighterBook b, MoveEntry e)
        {
            if (Rules.Count == 0) Load();
            foreach (var p in e.Data.Projectiles)
            {
                var r = Rules.FirstOrDefault(x => x.Fighter == b.Character.FilePrefix && x.Slot == p.Article?.Slot);
                if (r == null) continue;
                if (Environment.GetEnvironmentVariable("SKINMANAGER_DEBUG_FLIGHT") != null) Console.WriteLine($"flight {b.Character.FilePrefix} {e.Name} slot {p.Article?.Slot}");
                try
                {
                    var f = r.Make(new Ctx { Book = b, Entry = e, Shot = p, Article = p.Article });
                    if (f == null || f.Life <= 0) continue;
                    // starts where it's made, else when the move has the fighter's code make it, else when it lets go of something, else at the start
                    if (f.Start <= 0) f.Start = p.Frame > 0 ? p.Frame : e.Data.CodeShots.Count > 0 ? e.Data.CodeShots.Min() : e.Data.Shots.Select(x => x.Frame).DefaultIfEmpty(1).Min();
                    p.Flight = f;
                }
                catch { }
            }
            // the flight covers the thing's whole life (a boomerang's way back, a missile bursting): its other scripts listed
            // on their own are dropped, but not the same thing at another charge (no charge / full charge)
            foreach (var p in e.Data.Projectiles.Where(x => x.Flight != null).ToList())
                e.Data.Projectiles.RemoveAll(q => q != p && q.Flight == null && q.Article == p.Article && !q.Name.Contains("charge"));
            var flown = new HashSet<Article>();
            e.Data.Projectiles.RemoveAll(q => q.Flight != null && !q.Name.Contains("charge") && !q.Name.Contains("Charge") && !flown.Add(q.Article));
            // the move's pictures go on until its projectiles are done (the fighter holds still at the end)
            int end = e.Data.Projectiles.Where(p => p.Flight != null).Select(p => p.Flight.End).DefaultIfEmpty(0).Max();
            var fr = e.Data.Frames;
            if (fr.Count == 0) return;
            end = Math.Min(end, fr.Count + 600);
            var last = fr[fr.Count - 1];
            for (int k = fr[fr.Count - 1].Frame + 1; k <= end; k++)
                fr.Add(new FrameState { Frame = k, AnimFrame = last.AnimFrame, Animation = last.Animation, Body = 0, Hidden = last.Hidden, After = true, MoveX = last.MoveX, MoveY = last.MoveY });
        }

        // ---- building blocks ----

        /// <summary>A straight line: speed (units a frame) at an angle (degrees, 0 = forward, 90 = up), optionally speeding up
        /// or slowing down each frame between min and max speed.</summary>
        public static List<(float X, float Y)> Straight(int life, float speed, float angleDeg, float accel = 0, float min = 0, float max = float.MaxValue)
        {
            life = Math.Max(0, Math.Min(life, 600));   // a number read wrong can't run away
            var path = new List<(float, float)>(); float x = 0, y = 0, v = speed; double a = angleDeg * Math.PI / 180;
            for (int k = 0; k < life; k++)
            {
                path.Add((x, y));
                x += (float)(v * Math.Cos(a)); y += (float)(v * Math.Sin(a));
                v = Math.Max(min, Math.Min(max, v + accel));
            }
            return path;
        }

        /// <summary>Thrown with a starting speed, pulled down by gravity up to a falling speed limit; groundY (from where it
        /// starts) stops it falling through the floor, and bounce keeps that share of its falling speed.</summary>
        public static List<(float X, float Y)> Ballistic(int life, float vx, float vy, float gravity, float maxFall, float? groundY = null, float bounce = 0, float dragX = 0)
        {
            life = Math.Max(0, Math.Min(life, 600));   // a number read wrong can't run away
            var path = new List<(float, float)>(); float x = 0, y = 0;
            for (int k = 0; k < life; k++)
            {
                path.Add((x, y));
                x += vx; y += vy;
                vy = Math.Max(-Math.Abs(maxFall), vy - gravity);
                if (dragX != 0) vx = Math.Sign(vx) * Math.Max(0, Math.Abs(vx) - dragX);
                if (groundY.HasValue && y < groundY.Value) { y = groundY.Value; vy = bounce > 0 ? -vy * bounce : 0; }
            }
            return path;
        }

        /// <summary>A flight along a path, with one article script's hitboxes (or several, one after another by frame).</summary>
        public static Flight Fly(Ctx c, List<(float X, float Y)> path, string script, string about, int start = 0, int bone = -1, float fwd = 0, float up = 0, params (int From, string Script)[] later)
        {
            var f = new Flight { Path = path, Start = start, About = about, Bone = bone, Forward = fwd, Up = up };
            var main = c.Script(script, path.Count);
            var after = later.Select(l => (l.From, Hits: c.Script(l.Script, path.Count))).ToList();
            for (int k = 0; k < path.Count; k++)
            {
                var hits = main[k];
                foreach (var l in after.Where(x => k + 1 >= x.From)) hits = l.Hits[k + 1 - l.From];
                f.Hits.Add(hits);
            }
            return f;
        }

        /// <summary>A fall with bounces: gravity, bounce keeps that share of its speed each time it lands, ends when slower
        /// than minSpeed or after life frames; the floor is height below where it starts.</summary>
        public static List<(float X, float Y)> Bouncing(int life, float vx, float vy, float gravity, float height, float keep, float minSpeed)
        {
            life = Math.Max(0, Math.Min(life, 600));   // a number read wrong can't run away
            var path = new List<(float, float)>(); float x = 0, y = 0;
            for (int k = 0; k < life; k++)
            {
                path.Add((x, y));
                x += vx; y += vy; vy -= gravity;
                if (y < -height) { y = -height; vy = -vy * keep; vx *= keep; if (Math.Sqrt(vx * vx + vy * vy) < minSpeed) break; }
            }
            return path;
        }

        /// <summary>Stays where it came out.</summary>
        public static List<(float X, float Y)> Still(int life) => Enumerable.Range(0, Math.Max(0, Math.Min(life, 600))).Select(_ => (0f, 0f)).ToList();

        static partial void LoadRules();
        static void Load() { LoadRules(); }
    }
}
