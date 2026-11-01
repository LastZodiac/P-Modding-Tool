// "What punishes this?": an opponent's move hits your shield; which of your out-of-shield options hit them before
// they can act again. The rules (Melee's, which P+ follows):
//   shield stun = floor((damage + 4.45) / 2.235); their move's frame advantage on shield = hit frame + shield stun - (FAF - 1)
//   (an aerial: landing right after with an L-cancel, as Combos.Ready); you have -advantage frames once your stun ends.
//   Out of shield you can grab, up smash and up B straight away; an aerial needs a jump first (jumpsquat frames).
//   An option punishes when its own first hitting frame (plus the jump) is within those frames.
// Reach: how far in front each hitbox gets (the furthest edge of any hitbox), compared with how far theirs reached:
// if yours reaches at least as far, it punishes even when they hit your shield from their furthest.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class PunishRow
    {
        public string Name, How;          // "Up smash", "straight out of shield" / "jump, then the aerial"
        public MoveEntry Move;
        public int Startup, Jump;         // its first hitting frame; jump frames before it (aerials)
        public int Spare;                 // frames left over (0 or more: it punishes); negative: that many frames too slow
        public bool Works => Spare >= 0;
        public float Reach;               // how far in front it hits (units)
        public bool Reaches;              // reaches as far as their hit did
    }

    public sealed class PunishReport
    {
        public MoveEntry Theirs;
        public int HitFrame, Stun, Advantage;   // advantage: theirs on shield (negative: you have that many frames)
        public float Damage, TheirReach;
        public List<PunishRow> Rows = new List<PunishRow>();
        public string Problem;                  // why it can't be worked out (no hitbox of its own...)
        public bool LastOfSeveral;              // it hits more than once: worked out from its last hit
        public int Window => -Advantage;
    }

    public static class Punish
    {
        /// <summary>The first frame a move hits (not a grab) and the most damage of its hits on that frame; null if none.</summary>
        static (int Frame, float Damage)? FirstHit(MoveData d)
        {
            foreach (var f in d.Frames)
            {
                var hits = f.Hitboxes.Where(h => !h.Grab).ToList();
                if (hits.Count > 0) return (f.Frame, hits.Max(h => float.IsNaN(h.Damage) ? 0 : h.Damage));
            }
            return null;
        }

        static (int Frame, float Damage)? LastHit(MoveData d)
        {
            int from = d.Active.Last().From;
            var f = d.Frames.FirstOrDefault(x => x.Frame == from && x.Hitboxes.Any(h => !h.Grab));
            return f == null ? FirstHit(d) : (f.Frame, f.Hitboxes.Where(h => !h.Grab).Max(h => float.IsNaN(h.Damage) ? 0 : h.Damage));
        }

        /// <summary>How far in front its hitboxes reach while it's out (from where it's measured; 0 if not measured).</summary>
        static float Reach(FighterBook b, MoveData d, int from = 0, int to = int.MaxValue)
        {
            if (b.Places == null || !b.Places.TryGetValue(d, out var places)) return 0;
            float best = 0;
            for (int i = 0; i < places.Count && i < d.Frames.Count; i++)
            {
                int fr = d.Frames[i].Frame;
                if (fr < from || fr > to) continue;
                foreach (var h in places[i].Hits.Where(x => !x.Hit.Grab || from == 0)) best = Math.Max(best, Math.Abs(h.X) + h.R);
            }
            return best;
        }

        /// <summary>Your options out of shield against their move. Both books should be measured (FighterBook.Measure)
        /// for the reach; without it the frames are still right.</summary>
        public static PunishReport For(FighterBook them, MoveEntry move, FighterBook me)
        {
            var r = new PunishReport { Theirs = move };
            var d = move.Data;
            if (d.Throw != null || move.Group == "Grabs & throws") { r.Problem = "Grabs go through shields, so there's nothing to punish on shield."; return r; }
            // a move that hits more than once: its last hit is the one that leaves you in shield longest
            var first = d.Active.Count > 1 ? LastHit(d) : FirstHit(d);
            r.LastOfSeveral = d.Active.Count > 1 && first != null;
            if (first == null) { r.Problem = "This move has no hitbox of its own (it shoots something, or the game's code does the hitting), so its shield numbers can't be worked out here."; return r; }
            r.HitFrame = first.Value.Frame; r.Damage = first.Value.Damage;
            r.Stun = Combos.ShieldStun(r.Damage);
            int faf = Combos.Ready(d, r.HitFrame);
            r.Advantage = r.HitFrame + r.Stun - (faf - 1);
            r.TheirReach = Reach(them, d, r.HitFrame, r.HitFrame);
            int jump = Math.Max(1, me.Body?.Jumpsquat ?? me.Moves.AttrInt(0x30));

            void Add(MoveEntry e, string name, string how, int extra)
            {
                if (e == null) return;
                var f = FirstHit(e.Data);
                int start = f?.Frame ?? 0;
                if (name == "Grab") start = e.Data.Frames.FirstOrDefault(x => x.Hitboxes.Any(h => h.Grab))?.Frame ?? 0;
                if (start <= 0) return;
                float reach = name == "Grab"
                    ? (me.Places != null && me.Places.TryGetValue(e.Data, out var gp) ? gp.Take(e.Data.Frames.Count).SelectMany(p => p.Hits.Where(h => h.Hit.Grab)).Select(h => Math.Abs(h.X) + h.R).DefaultIfEmpty(0).Max() : 0)
                    : Reach(me, e.Data, start, start + 6);
                r.Rows.Add(new PunishRow
                {
                    Name = name, How = how, Move = e, Startup = start, Jump = extra, Spare = r.Window - (start + extra), Reach = reach,
                    Reaches = r.TheirReach <= 0 || reach >= r.TheirReach,
                });
            }
            MoveEntry Find(Func<string, bool> part) => me.Entries.FirstOrDefault(e => e.Parts.Any(part));
            Add(Find(p => p == "Catch"), "Grab", "straight out of shield", 0);
            Add(Find(p => p == "AttackHi4" || p == "AttackHi4Start"), "Up smash", "straight out of shield", 0);
            Add(me.Entries.FirstOrDefault(e => e.Parts[0].StartsWith("SpecialHi") && FirstHit(e.Data) != null), "Up B", "straight out of shield", 0);
            foreach (var (part, name) in new[] { ("AttackAirN", "Neutral air"), ("AttackAirHi", "Up air"), ("AttackAirF", "Forward air"), ("AttackAirB", "Back air"), ("AttackAirLw", "Down air") })
                Add(Find(p => p == part), name, $"jump ({jump} frames), then the aerial", jump);
            r.Rows = r.Rows.OrderByDescending(x => x.Works && x.Reaches).ThenByDescending(x => x.Works).ThenByDescending(x => x.Spare).ToList();
            return r;
        }
    }
}
