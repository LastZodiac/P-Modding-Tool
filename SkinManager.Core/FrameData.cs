// Turns a subaction's script into frame data: which hitboxes are out on which frame, when the move can be cancelled,
// how long it lasts, and when the fighter is invincible. Timing rules (checked against known Melee/P+ numbers for
// Fox and Mario): an event after a script time of t happens on frame t+1; "sync timer x" waits x animation frames,
// "async timer x" waits until animation frame x; a Frame Speed Modifier f makes animation frames last 1/f real frames.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class Hitbox
    {
        public int Id, Bone;
        public float Damage, Size, X, Y, Z;
        public int Angle, Bkb, Kbg, Wkb, ShieldDamage, Trip, Flags, SpecialFlags;
        public float Hitlag, Sdi;
        public bool Special, Grab, Throw;
        public Hitbox Copy() => (Hitbox)MemberwiseClone();
        public string DamageText => float.IsNaN(Damage) ? "?" : Damage.ToString("0.#") + "%";
        public string Summary => Grab ? $"grab {Size:0.#}" : $"{DamageText} {Angle}° BKB {Bkb} KBG {Kbg}" + (Wkb > 0 ? $" WKB {Wkb}" : "");
    }

    public sealed class FrameState
    {
        public int Frame;              // 1-based real frame
        public float AnimFrame;        // the animation frame shown on it
        public string Animation;
        public List<Hitbox> Hitboxes = new List<Hitbox>();
        public int Body;               // 0 normal, 1 invincible, 2+ intangible
        public int[] Hidden = new int[0]; // model bones hidden on this frame (visibility switches)
    }

    public sealed class Projectile
    {
        public string Name;            // "Projectile (Fly)"
        public int Frame;              // the move's frame it comes out on
        public Article Article;
        public MoveData Data;          // its own frames, counted from when it appears
        public string ActiveText => Data.Active.Count == 0 ? "—" : string.Join(", ", Data.Active.Select(a => (a.From + Frame - 1) + (Data.Lingers && a.To == Data.Active.Last().To ? "+" : a.From == a.To ? "" : "-" + (a.To + Frame - 1))));
    }

    public sealed class MoveData
    {
        public string Name, Animation;
        public List<int> Subactions = new List<int>();
        public List<FrameState> Frames = new List<FrameState>();
        public int Total, Iasa, AutoCancelFrom, LandingLag;
        public List<(int From, int To)> Active = new List<(int, int)>();
        public List<(int From, int To)> Intangible = new List<(int, int)>();
        public List<Hitbox> Hits = new List<Hitbox>();   // every distinct hitbox, first time it comes out
        public Hitbox Throw;                               // a throw's own numbers
        public List<(int Frame, int Article)> Spawns = new List<(int, int)>();   // articles made (projectiles...), first frame each
        public List<Projectile> Projectiles = new List<Projectile>();
        public bool Lingers;                               // hitboxes still out when its script ends (a projectile flying)
        /// <summary>The first frame anything can hit: its own hitboxes, or else its projectile's.</summary>
        public int FirstHit => Startup > 0 ? Startup : Projectiles.Where(p => p.Data.Startup > 0).Select(p => p.Frame + p.Data.Startup - 1).DefaultIfEmpty(0).Min();
        public int ThrowFrame;                             // when the opponent is let go
        public int Startup => Active.Count > 0 ? Active[0].From : 0;
        public int Faf => Iasa > 0 ? Iasa : Total + 1;     // first frame you can act
        public string ActiveText => string.Join(", ", Active.Select(a => a.From == a.To ? $"{a.From}" : $"{a.From}-{a.To}"));
        public string IntangibleText => string.Join(", ", Intangible.Select(a => a.From == a.To ? $"{a.From}" : $"{a.From}-{a.To}"));
    }

    public static class FrameData
    {
        /// <summary>Plays the given subactions one after another (a smash attack's start then its swing).
        /// lengths: each animation's frame count (from the motion file); 0 = unknown.</summary>
        public static MoveData Run(Moveset m, string name, IList<int> subs, Func<string, int> length) =>
            Run(m, name, subs.Where(i => i >= 0 && i < m.Subactions.Count).Select(i => m.Subactions[i]).ToList(), length, subs);

        /// <summary>The same for subactions given directly (an article's: a projectile's own scripts).</summary>
        public static MoveData Run(Moveset m, string name, IList<Subaction> parts, Func<string, int> length, IList<int> indices = null)
        {
            var md = new MoveData { Name = name, Subactions = (indices ?? parts.Select(p => p.Index).ToList()).ToList() };
            float real = 0;   // real frames passed so far (whole move)
            var active = new Dictionary<int, Hitbox>();
            int body = 0;
            var hidden = m.DefaultHidden();
            for (int part = 0; part < parts.Count; part++)
            {
                var s = parts[part];
                md.Animation = md.Animation ?? s.Animation;
                int animLen = s.Animation == null ? 0 : length(s.Animation);
                float t = 0, rate = 1;
                var ev = s.Main;
                // loops: stack of (body start index, remaining)
                var loops = new Stack<(int start, int left)>();
                bool skipping = false; int ifDepth = 0, skipDepth = 0;
                bool lastPart = part == parts.Count - 1;

                void Advance(float until)
                {
                    // record each real frame between now and script time 'until'
                    while (t < until - 1e-4f)
                    {
                        float step = Math.Min(1, until - t);
                        int before = (int)Math.Floor(real + 1e-3f);
                        t += step; real += step / rate;
                        int after = (int)Math.Floor(real + 1e-3f);
                        for (int f = before + 1; f <= after; f++) Snap(f, t);
                    }
                }
                void Snap(int frame, float at)
                {
                    md.Frames.Add(new FrameState { Frame = frame, AnimFrame = at, Animation = s.Animation, Body = body, Hidden = hidden.ToArray(), Hitboxes = active.Values.Select(h => h.Copy()).ToList() });
                }

                for (int i = 0, guard = 0; i < ev.Count && guard < 5000; i++, guard++)
                {
                    var e = ev[i];
                    // If / Else / End If: follow the first branch only
                    if (e.Id == 0x000A0100 || e.Id == 0x000A0200 || e.Id == 0x000A0300 || e.Id == 0x000A0400) { ifDepth++; continue; }
                    if (e.Id == 0x000B0100 || e.Id == 0x000B0200 || e.Id == 0x000B0300 || e.Id == 0x000B0400) continue;   // And / Or
                    if (e.Id == 0x000E0000) { if (!skipping) { skipping = true; skipDepth = ifDepth; } continue; }
                    if (e.Id == 0x000F0000) { if (skipping && ifDepth == skipDepth) skipping = false; ifDepth = Math.Max(0, ifDepth - 1); continue; }
                    if (skipping) continue;

                    switch (e.Id)
                    {
                        case 0x00010100: Advance(t + e.Scalar(0)); break;                         // sync timer
                        case 0x00020100: Advance(Math.Max(t, e.Scalar(0))); break;                 // async timer
                        case 0x00040100: loops.Push((i + 1, e.Values[0])); break;                  // set loop (count; -1 = forever)
                        case 0x00050000:                                                           // execute loop
                            if (loops.Count > 0)
                            {
                                var l = loops.Pop();
                                int left = l.left < 0 ? 999 : l.left - 1;
                                bool forever = l.left < 0;
                                if (left > 0 && (!forever || animLen <= 0 || t < animLen)) { loops.Push((l.start, forever ? -1 : left)); i = l.start - 1; }
                            }
                            break;
                        case 0x04070100: rate = Math.Max(0.01f, e.Scalar(0)); break;               // frame speed modifier
                        case 0x06000D00: case 0x06150F00:                                         // hitbox
                        {
                            var h = new Hitbox
                            {
                                Bone = e.Hi(0), Id = e.Lo(0), Damage = e.Values[1], Angle = e.Values[2],
                                Wkb = e.Hi(3), Kbg = e.Lo(3), ShieldDamage = e.Hi(4), Bkb = e.Lo(4),
                                Size = e.Scalar(5), X = e.Scalar(6), Y = e.Scalar(7), Z = e.Scalar(8),   // as BrawlBox draws them
                                Trip = e.Values.Length > 9 ? e.Values[9] : 0, Hitlag = e.Values.Length > 10 ? e.Scalar(10) : 1,
                                Sdi = e.Values.Length > 11 ? e.Scalar(11) : 1, Flags = e.Values.Length > 12 ? e.Values[12] : 0,
                                Special = e.Id == 0x06150F00, SpecialFlags = e.Values.Length > 14 ? e.Values[14] : 0,
                            };
                            if (e.Types.Length > 1 && e.Types[1] == 1) h.Damage = e.Scalar(1);
                            if (e.Types.Length > 1 && e.Types[1] == 5) h.Damage = float.NaN;   // set by the game's code (a variable)
                            active[h.Id] = h;
                            int when = (int)Math.Floor(real + 1e-3f) + 1;
                            if (!md.Hits.Any(x => x.Id == h.Id && x.Bone == h.Bone && (Math.Abs(x.Damage - h.Damage) < .01f || float.IsNaN(x.Damage) && float.IsNaN(h.Damage)) && x.Angle == h.Angle && Math.Abs(x.Size - h.Size) < .01f)) md.Hits.Add(h);
                            break;
                        }
                        case 0x060A0800:                                                           // grab box
                        {
                            var h = new Hitbox { Grab = true, Id = e.Values[0], Bone = e.Values[1], Size = e.Scalar(2), X = e.Scalar(3), Y = e.Scalar(4), Z = e.Scalar(5) };
                            active[100 + h.Id] = h; if (!md.Hits.Any(x => x.Grab && x.Id == h.Id)) md.Hits.Add(h);
                            break;
                        }
                        case 0x060E1100:                                                           // throw specifier
                            if (e.Values.Length > 6 && e.Values[0] == 0 && md.Throw == null)
                            {
                                md.Throw = new Hitbox { Throw = true, Damage = e.Values[2], Angle = e.Values[3], Kbg = e.Values[4], Wkb = e.Values[5], Bkb = e.Values[6] };
                                if (e.Types[2] == 1) md.Throw.Damage = e.Scalar(2);
                                if (e.Types[2] == 5) md.Throw.Damage = float.NaN;
                            }
                            break;
                        case 0x060F0500: if (md.ThrowFrame == 0) md.ThrowFrame = (int)Math.Floor(real + 1e-3f) + 1; break;   // throw: let go
                        case 0x06040000: active.Clear(); break;                                    // remove all hitboxes
                        case 0x06030100: active.Remove(e.Values[0]); break;                        // remove one
                        case 0x060B0000: case 0x060C0100: foreach (var k in active.Keys.Where(k => k >= 100).ToList()) active.Remove(k); break; // grab boxes off
                        case 0x06010200: if (active.TryGetValue(e.Values[0], out var hd)) { hd = hd.Copy(); hd.Damage = e.Values[1]; active[e.Values[0]] = hd; } break;
                        case 0x061B0500: if (active.TryGetValue(e.Values[0], out var hm)) { hm = hm.Copy(); hm.Bone = e.Values[1]; hm.X = e.Scalar(2); hm.Y = e.Scalar(3); hm.Z = e.Scalar(4); active[e.Values[0]] = hm; } break;
                        case 0x0B000200: case 0x0B010200: m.SetGroup(hidden, (int)(e.Id >> 16) & 1, e.Values[0], e.Values[1]); break;   // model changer
                        case 0x06050100: body = e.Values[0]; break;                                // body: 0 normal, 1 invincible, 2+ intangible
                        case 0x10000100: case 0x10000200:                                          // generate article (projectile...)
                            if (e.Values.Length > 0 && !md.Spawns.Any(x => x.Article == e.Values[0])) md.Spawns.Add(((int)Math.Floor(real + 1e-3f) + 1, e.Values[0]));
                            break;
                        case 0x64000000: if (lastPart && md.Iasa == 0) md.Iasa = (int)Math.Floor(real + 1e-3f) + 1; break;   // allow interrupt
                    }
                }
                // play the animation out
                // a start part hands over on its last frame (Fox's smashes: start 4 frames + swing hit 8 = frame 12, as in Melee)
                if (animLen > 0) Advance(lastPart ? animLen : Math.Max(t, animLen - 1));
                else if (active.Count > 0) Advance(t + 1);   // no animation (a projectile): show its hitboxes out at least once
                active.Clear();
                if (body != 0) body = 0;
            }
            md.Lingers = md.Frames.Count > 0 && md.Frames[md.Frames.Count - 1].Hitboxes.Count > 0;
            md.Total = (int)Math.Ceiling(real - 1e-3f);
            // collect windows
            int start = 0, istart = 0;
            for (int k = 0; k <= md.Frames.Count; k++)
            {
                var f = k < md.Frames.Count ? md.Frames[k] : null;
                bool hit = f != null && f.Hitboxes.Any(h => !h.Grab || true);
                if (hit && start == 0) start = f.Frame;
                if (!hit && start > 0) { md.Active.Add((start, md.Frames[k - 1].Frame)); start = 0; }
                bool inv = f != null && f.Body != 0;
                if (inv && istart == 0) istart = f.Frame;
                if (!inv && istart > 0) { md.Intangible.Add((istart, md.Frames[k - 1].Frame)); istart = 0; }
            }
            return md;
        }
    }
}
