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
        // a hitbox on a thing the fighter holds (ZSS's whip...): its Bone is that thing's bone; it hangs off the fighter's AttachBone,
        // posed by the thing's own animation (ArticleAnim, frame ArticleFrame) on its model (ArticleModel, in the motion file)
        public string ArticleModel, ArticleAnim; public int AttachBone = -1; public float ArticleFrame;
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
        public bool After;             // past the move's end: only shown so a projectile can finish flying
        public float MoveX, MoveY;     // how far the game's code has moved the fighter so far (not the animation)
    }

    /// <summary>How a projectile flies once it's out: where it is and which hitboxes it has on each of its frames.</summary>
    public sealed class Flight
    {
        public int Start;                                   // the move's frame its first flight frame is on
        public List<(float X, float Y)> Path = new List<(float, float)>();   // from where it starts; X = the way the fighter faces
        public List<List<Hitbox>> Hits = new List<List<Hitbox>>();         // its hitboxes on each flight frame
        public string About;                                // what it does, in a few words ("flies 34 frames at 10 a frame")
        public int Bone = -1; public float Forward, Up;     // where it starts: this model bone (else where it's made) plus an offset
        public bool FollowFighter;                          // stays on the fighter as it moves (Fox's side B afterimage)
        public bool StopAtGround;                           // the flight ends when it comes down to the floor
        public Func<float, List<(float X, float Y)>> PathForHeight;   // a flight that bounces on the floor: its path given how high above the floor it starts
        public int Life => Path.Count;
        public int End => Start + Life - 1;
    }

    public sealed class Projectile
    {
        public string Name;            // "Projectile (Fly)"
        public int Frame;              // the move's frame it comes out on (0: the game's code makes it, the script doesn't say when)
        public Article Article;
        public MoveData Data;          // its own frames, counted from when it appears
        public bool Unknown => Frame <= 0 && Flight == null;
        string FlightText
        {
            get
            {
                int a = 0, b = 0;
                for (int k = 0; k < Flight.Hits.Count; k++) if (Flight.Hits[k].Any(h => !h.Grab)) { if (a == 0) a = Flight.Start + k; b = Flight.Start + k; }
                return a == 0 ? "—" : a == b ? a.ToString() : $"{a}-{b}";
            }
        }
        public Flight Flight;          // its whole flight (null: only where it comes out is known)
        /// <summary>The move's first frame it can hit (0: not known).</summary>
        public int FirstHitFrame
        {
            get
            {
                if (Flight != null) { for (int k = 0; k < Flight.Hits.Count; k++) if (Flight.Hits[k].Any(h => !h.Grab)) return Flight.Start + k; return 0; }
                return Unknown || Data.Startup <= 0 ? 0 : Frame + Data.Startup - 1;
            }
        }
        int Base => Unknown ? 1 : Frame;
        public string ActiveText => Flight != null ? FlightText : Data.Active.Count == 0 ? "—" : (Unknown ? "its frames " : "") + string.Join(", ", Data.Active.Select(a => (a.From + Base - 1) + (Data.Lingers && a.To == Data.Active.Last().To ? "+" : a.From == a.To ? "" : "-" + (a.To + Base - 1))));
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
        public int RemovedAt;                              // an article's script removing it ("delete self"), on this frame
        public List<(int Frame, int Article)> Shots = new List<(int, int)>();   // "shoot article" events: a held thing let go (an arrow, a charged shot)
        public List<(int Frame, int Var)> FlagsSet = new List<(int, int)>();   // "set bit" events: a flag the game's code acts on (G&W's chef: make food now)
        public List<int> CodeShots = new List<int>();     // "the fighter's code makes its projectile now" events (Squirtle's water, Ivysaur's leaf)
        public List<(int Frame, int Article, int Action)> ArticleActions = new List<(int, int, int)>();   // "article: change action" (Snake's C4 told to blow up)
        /// <summary>The first frame anything can hit: its own hitboxes, or else its projectile's.</summary>
        public int FirstHit => Startup > 0 ? Startup : Projectiles.Select(p => p.FirstHitFrame).Where(x => x > 0).DefaultIfEmpty(0).Min();
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
        public static MoveData Run(Moveset m, string name, IList<int> subs, Func<string, int> length, ScriptState state = null, Func<Subaction, (int Action, List<MoveEvent> Entry)> actionOf = null) =>
            Run(m, name, subs.Where(i => i >= 0 && i < m.Subactions.Count).Select(i => m.Subactions[i]).ToList(), length, subs, state, actionOf);

        // the kinds of events that steer a script rather than do something
        static bool IsIf(uint id) => id >> 16 == 0x000A;
        static bool IsAnd(uint id) => id >> 16 == 0x000B;
        static bool IsOr(uint id) => id >> 16 == 0x000C;
        static bool IsElseIf(uint id) => id >> 16 == 0x000D;
        const uint Else = 0x000E0000, EndIf = 0x000F0000;

        /// <summary>One If ... End If being run: whether its current branch runs.</summary>
        sealed class IfFrame { public bool Outer, Taken, Cond; public bool Runs => Outer && Cond && !Taken; }

        /// <summary>The same for subactions given directly (an article's: a projectile's own scripts).
        /// state: what the scripts know (shared by all the pieces; a new one if none); actionOf: for a piece, the fighter's
        /// action that plays it and that action's own script (it runs as the piece starts: its hitboxes and every-frame loops
        /// last as long as the action).</summary>
        public static MoveData Run(Moveset m, string name, IList<Subaction> parts, Func<string, int> length, IList<int> indices = null,
                                   ScriptState state = null, Func<Subaction, (int Action, List<MoveEvent> Entry)> actionOf = null)
        {
            var md = new MoveData { Name = name, Subactions = (indices ?? parts.Select(p => p.Index).ToList()).ToList() };
            state = state ?? new ScriptState(m, null);
            float real = 0;   // real frames passed so far (whole move)
            var active = new Dictionary<int, Hitbox>();
            int body = 0;
            var hidden = m.DefaultHidden();
            int action = -1;                                         // the fighter action playing now (-1: none known)
            var actionHits = new List<Hitbox>();                     // hitboxes that action's own script put out
            var threads = new Dictionary<int, List<MoveEvent>>();    // every-frame loops running now (by their thread number)
            int Now() => (int)Math.Floor(real + 1e-3f) + 1;
            bool lastPart = false;

            // one event that does something (not timing or branching); false if it isn't one of these
            bool Do(MoveEvent e, bool fromAction)
            {
                switch (e.Id)
                {
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
                        if (e.Types.Length > 1 && e.Types[1] == 5) h.Damage = state.Get(e.Values[1]) ?? float.NaN;   // a variable (its value if the scripts set it)
                        active[h.Id] = h;
                        if (fromAction) { actionHits.RemoveAll(x => x.Id == h.Id); actionHits.Add(h); }
                        if (!md.Hits.Any(x => x.Id == h.Id && x.Bone == h.Bone && (Math.Abs(x.Damage - h.Damage) < .01f || float.IsNaN(x.Damage) && float.IsNaN(h.Damage)) && x.Angle == h.Angle && Math.Abs(x.Size - h.Size) < .01f)) md.Hits.Add(h);
                        return true;
                    }
                    case 0x060A0800:                                                           // grab box
                    {
                        var h = new Hitbox { Grab = true, Id = e.Values[0], Bone = e.Values[1], Size = e.Scalar(2), X = e.Scalar(3), Y = e.Scalar(4), Z = e.Scalar(5) };
                        active[100 + h.Id] = h; if (!md.Hits.Any(x => x.Grab && x.Id == h.Id)) md.Hits.Add(h);
                        return true;
                    }
                    case 0x060E1100:                                                           // throw specifier
                        if (e.Values.Length > 6 && e.Values[0] == 0 && md.Throw == null)
                        {
                            md.Throw = new Hitbox { Throw = true, Damage = e.Values[2], Angle = e.Values[3], Kbg = e.Values[4], Wkb = e.Values[5], Bkb = e.Values[6] };
                            if (e.Types[2] == 1) md.Throw.Damage = e.Scalar(2);
                            if (e.Types[2] == 5) md.Throw.Damage = state.Get(e.Values[2]) ?? float.NaN;
                        }
                        return true;
                    case 0x060F0500: if (md.ThrowFrame == 0) md.ThrowFrame = Now(); return true;   // throw: let go
                    case 0x06040000: active.Clear(); actionHits.Clear(); return true;              // remove all hitboxes
                    case 0x06030100: active.Remove(e.Values[0]); actionHits.RemoveAll(x => x.Id == e.Values[0]); return true;   // remove one
                    case 0x060B0000: case 0x060C0100: foreach (var k in active.Keys.Where(k => k >= 100).ToList()) active.Remove(k); return true; // grab boxes off
                    case 0x06010200: if (active.TryGetValue(e.Values[0], out var hd)) { hd = hd.Copy(); hd.Damage = e.Values[1]; active[e.Values[0]] = hd; } return true;
                    case 0x061B0500: if (active.TryGetValue(e.Values[0], out var hm)) { hm = hm.Copy(); hm.Bone = e.Values[1]; hm.X = e.Scalar(2); hm.Y = e.Scalar(3); hm.Z = e.Scalar(4); active[e.Values[0]] = hm; } return true;
                    case 0x0B000200: case 0x0B010200: m.SetGroup(hidden, (int)(e.Id >> 16) & 1, e.Values[0], e.Values[1]); return true;   // model changer
                    case 0x06050100: body = e.Values[0]; return true;                                // body: 0 normal, 1 invincible, 2+ intangible
                    case 0x10000100: case 0x10000200:                                                // generate article (projectile...)
                        if (e.Values.Length > 0 && !md.Spawns.Any(x => x.Article == e.Values[0])) md.Spawns.Add((Now(), e.Values[0]));
                        return true;
                    case 0x10010100: case 0x10010200: case 0x10090100:                               // shoot / let go of an article
                        if (e.Values.Length > 0) md.Shots.Add((Now(), e.Values[0]));
                        return true;
                    case 0x10080200: if (e.Values.Length > 1) md.ArticleActions.Add((Now(), e.Values[0], e.Values[1])); return true;   // an article told to change action
                    case 0x10140100: md.CodeShots.Add(Now()); return true;                          // the fighter's code makes its projectile
                    case 0x0C050000: if (md.RemovedAt == 0) md.RemovedAt = Now(); return true;     // article: remove itself
                    case 0x64000000: if (lastPart && md.Iasa == 0) md.Iasa = Now(); return true;   // allow interrupt
                    case 0x0D000200: if (e.Values.Length > 1 && e.Values[1] > 0) threads[e.Values[0]] = m.Script(e.Values[1], 0); return true;   // every-frame loop
                    case 0x0D010100: if (e.Values.Length > 0) threads.Remove(e.Values[0]); return true;   // stop one
                }
                if (e.Id == 0x120A0100 && e.Values.Length > 0) md.FlagsSet.Add((Now(), e.Values[0]));   // a flag (the game's code may act on it)
                return state.Variable(e);
            }

            // If / And / Or / Else If / Else / End If: true if it was one (ifs: the ones open in this script). What the files
            // can't answer counts as "unknown": true in a move's own script (it follows its first branch, as before), false
            // in an every-frame loop or an action's script (a hit only the game knows the reason for isn't shown every frame)
            bool Branch(MoveEvent e, Stack<IfFrame> ifs, bool unknown)
            {
                bool outer = ifs.Count == 0 || ifs.Peek().Runs;
                if (IsIf(e.Id)) { ifs.Push(new IfFrame { Outer = outer, Cond = state.Requirement(e) ?? unknown }); return true; }
                if (ifs.Count == 0) return IsAnd(e.Id) || IsOr(e.Id) || IsElseIf(e.Id) || e.Id == Else || e.Id == EndIf;
                var f = ifs.Peek();
                if (IsAnd(e.Id)) { var r = state.Requirement(e); f.Cond = f.Cond && (r ?? unknown); return true; }
                if (IsOr(e.Id)) { var r = state.Requirement(e); f.Cond = f.Cond || (r ?? unknown); return true; }
                if (IsElseIf(e.Id)) { f.Taken |= f.Cond; f.Cond = state.Requirement(e) ?? unknown; return true; }
                if (e.Id == Else) { f.Taken |= f.Cond; f.Cond = true; return true; }
                if (e.Id == EndIf) { ifs.Pop(); return true; }
                return false;
            }

            // a script run start to end in one go (an every-frame loop, the start of an action): no waiting
            void Flat(List<MoveEvent> list, bool fromAction, bool stopAtWait)
            {
                var ifs = new Stack<IfFrame>(); var calls = new Stack<(List<MoveEvent>, int)>();
                int i = 0, guard = 0;
                while (guard++ < 4000)
                {
                    if (i >= list.Count) { if (calls.Count == 0) break; (list, i) = calls.Pop(); continue; }
                    var e = list[i++];
                    if (Branch(e, ifs, false)) continue;
                    if (ifs.Count > 0 && !ifs.Peek().Runs) continue;
                    if (e.Id == 0x00010100 || e.Id == 0x00020100 || e.Id == 0x00020000 || e.Id == 0x00040100) { if (stopAtWait) break; continue; }
                    if (e.Id == 0x00070100 && e.Values.Length > 0 && e.Values[0] > 0 && calls.Count < 16) { calls.Push((list, i)); list = m.Script(e.Values[0], 0); i = 0; continue; }
                    if (e.Id == 0x00080000) { if (calls.Count == 0) break; (list, i) = calls.Pop(); continue; }
                    if (e.Id == 0x00090100 && e.Values.Length > 0 && e.Values[0] > 0) { list = m.Script(e.Values[0], 0); i = 0; continue; }
                    Do(e, fromAction);
                }
            }

            for (int part = 0; part < parts.Count; part++)
            {
                var s = parts[part];
                md.Animation = md.Animation ?? s.Animation;
                state.Air = (s.Animation ?? "").Contains("Air");
                int animLen = s.Animation == null ? 0 : length(s.Animation);
                float t = 0, rate = 1;
                lastPart = part == parts.Count - 1;

                // the fighter's action for this piece: a new one runs its own script first (once), and what it put out stays
                // for every piece it plays; the old one's every-frame loops stop
                var act = actionOf?.Invoke(s) ?? (-1, null);
                if (act.Action != action)
                {
                    action = act.Action; actionHits.Clear(); threads.Clear();
                    if (act.Entry != null) Flat(act.Entry, true, true);
                }
                foreach (var h in actionHits) active[h.Id] = h;

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
                    foreach (var th in threads.Values.ToList()) Flat(th, false, false);   // the every-frame loops, once each frame
                    // a looping animation (subaction flag 0x02: rapid jabs, grab holds...) starts over at its end
                    if ((s.Flags & 0x02) != 0 && animLen > 1 && at > animLen) at = (at - 1) % animLen + 1;
                    md.Frames.Add(new FrameState { Frame = frame, AnimFrame = at, Animation = s.Animation, Body = body, Hidden = hidden.ToArray(), Hitboxes = active.Values.Select(h => h.Copy()).ToList() });
                }

                var ev = s.Main;
                var loops = new Stack<(List<MoveEvent> list, int start, int left)>();   // loop body start, times left (-1: forever)
                var calls = new Stack<(List<MoveEvent>, int)>();
                var ifsMain = new Stack<IfFrame>();
                int i = 0;
                for (int guard = 0; guard < 20000 && real < 1200; guard++)   // (a script that never ends stops at 20 seconds)
                {
                    if (i >= ev.Count) { if (calls.Count == 0) break; (ev, i) = calls.Pop(); continue; }
                    var e = ev[i++];
                    if (Branch(e, ifsMain, true)) continue;
                    if (ifsMain.Count > 0 && !ifsMain.Peek().Runs) continue;
                    switch (e.Id)
                    {
                        case 0x00010100: Advance(t + e.Scalar(0)); continue;                         // sync timer
                        case 0x00020100: Advance(Math.Max(t, e.Scalar(0))); continue;                 // async timer
                        case 0x00040100: loops.Push((ev, i, e.Values[0])); continue;                  // set loop (count; -1 = forever)
                        case 0x00050000:                                                              // execute loop
                            if (loops.Count > 0)
                            {
                                var l = loops.Pop();
                                int left = l.left < 0 ? 999 : l.left - 1;
                                bool forever = l.left < 0;
                                if (left > 0 && (!forever || animLen <= 0 || t < animLen)) { loops.Push((l.list, l.start, forever ? -1 : left)); ev = l.list; i = l.start; }
                            }
                            continue;
                        case 0x00070100:                                                              // subroutine
                            if (e.Values.Length > 0 && e.Values[0] > 0 && calls.Count < 16) { calls.Push((ev, i)); ev = m.Script(e.Values[0], 0); i = 0; }
                            continue;
                        case 0x00080000: if (calls.Count > 0) (ev, i) = calls.Pop(); else i = ev.Count; continue;   // return
                        case 0x00090100:                                                              // goto another script (one going back to its own start, a rapid jab, ends there as it always has)
                            if (e.Values.Length > 0 && e.Values[0] > 0 && e.Values[0] != s.ScriptOffset && (animLen <= 0 ? t < 600 : t < animLen)) { ev = m.Script(e.Values[0], 0); i = 0; }
                            else { i = ev.Count; calls.Clear(); }
                            continue;
                        case 0x04070100: rate = Math.Max(0.01f, e.Scalar(0)); continue;               // frame speed modifier
                    }
                    Do(e, false);
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
            Windows(md);
            return md;
        }

        /// <summary>Works out when a move's hitboxes are out and when it's intangible, from its frames.</summary>
        public static void Windows(MoveData md)
        {
            md.Active.Clear(); md.Intangible.Clear();
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
        }
    }
}
