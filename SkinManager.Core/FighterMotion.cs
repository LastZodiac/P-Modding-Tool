// Specials where the game's code moves the fighter instead of the animation (Fire Fox, Quick Attack, Quick Draw...):
// how far it moves on each frame, read from the fighter's own parameters on the card, so the preview shows the fighter
// travelling. Each rule names the fighter parameter the game's code reads (block, byte offset) and where that code is.
// Shown with no stick held (straight up for the moves you aim, the way the fighter faces for the others).
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public static class FighterMotion
    {
        sealed class Rule
        {
            public string Fighter; public string[] Parts;
            public Func<Moveset, bool, (float Vx, float Vy, float Brake, float Gravity, float MaxFall)?> Speed;
            public string About;
        }

        static readonly List<Rule> Rules = new List<Rule>
        {
            // Fire Fox / Fire Bird: ft_fox_status_uniq_process_fire (shared by Fox, Falco, Wolf): flies at fighter param 2
            // +0x24 a frame toward the stick (straight up with none), no gravity, for the flight animation
            R("Fox", new[] { "SpecialHi", "SpecialAirHi" }, (m, air) => (0, m.FighterF(2, 0x24), 0, 0, 0), "flies straight up with no stick (any direction with one)"),
            R("Falco", new[] { "SpecialHi", "SpecialAirHi" }, (m, air) => (0, m.FighterF(2, 0x24), 0, 0, 0), "flies straight up with no stick (any direction with one)"),
            R("Wolf", new[] { "SpecialHi", "SpecialAirHi" }, (m, air) => (0, m.FighterF(2, 0x24), 0, 0, 0), "flies straight up with no stick (any direction with one)"),
            // Vanish / Farore's Wind: ft_sheik / ft_zelda special hi move: fighter param 2 +0x1C (Sheik) / +0x20 (Zelda) a
            // frame, more with the stick, straight up with none
            R("Sheik", new[] { "SpecialHi", "SpecialAirHi" }, (m, air) => (0, m.FighterF(2, 0x1C), 0, 0, 0), "travels straight up with no stick (farther and any way with one)"),
            R("Zelda", new[] { "SpecialHi", "SpecialAirHi" }, (m, air) => (0, m.FighterF(2, 0x20), 0, 0, 0), "travels straight up with no stick (farther and any way with one)"),
            // Extreme Speed / teleport: ft_lucario (and P+'s Mewtwo, same code) special hi move: fighter param 4 +0x14 a frame
            R("Lucario", new[] { "SpecialHiMove", "SpecialAirHiMove" }, (m, air) => (0, m.FighterF(4, 0x14), 0, 0, 0), "rushes straight up with no stick (the stick picks the way)"),
            R("Mewtwo", new[] { "SpecialHiMove", "SpecialAirHiMove" }, (m, air) => (0, m.FighterF(4, 0x14), 0, 0, 0), "teleports straight up with no stick (the stick picks the way)"),
            // Quick Draw: ft_ike special s: dash at fighter param 1 +0x8 (ground) / +0xC (air) a frame with no charge,
            // slowing by +0x18 / +0x1C a frame
            R("Ike", new[] { "SpecialSDash", "SpecialAirSDash" }, (m, air) => (air ? m.FighterF(1, 0xC) : m.FighterF(1, 0x8), 0, air ? m.FighterF(1, 0x1C) : m.FighterF(1, 0x18), 0, 0), "dashes forward (faster the longer it's charged)"),
            // Green Missile: ft_luigi kinetic transactor: fighter param 2 +0x24 forward and +0x2C up with no charge, gravity
            // +0x34, falling limit +0x38
            R("Luigi", new[] { "SpecialS", "SpecialAirS" }, (m, air) => (m.FighterF(2, 0x24), m.FighterF(2, 0x2C), 0, m.FighterF(2, 0x34), m.FighterF(2, 0x38)), "launches forward (farther the longer it's charged)"),
            // Super Dedede Jump: ft_dedede super jump jump: fighter param 2 +0x10 up, gravity +0x1C
            R("Dedede", new[] { "SpecialHiJump", "SpecialAirHiJump" }, (m, air) => (0, m.FighterF(2, 0x10) * (air ? m.FighterF(2, 0x18) : 1), 0, m.FighterF(2, 0x1C), 0), "jumps high, then comes down"),
        };

        static Rule R(string f, string[] parts, Func<Moveset, bool, (float, float, float, float, float)?> speed, string about) =>
            new Rule { Fighter = f, Parts = parts, Speed = speed, About = about };

        /// <summary>Whether the game's code moves the fighter during this move (a row worth showing even with no hitbox).</summary>
        public static bool Moves(FighterBook b, MoveEntry e) => Rules.Any(r => r.Fighter == b.Character.FilePrefix && e.Parts.Any(p => r.Parts.Contains(p)));

        /// <summary>Moves the fighter along on the frames its code moves it (FrameState.MoveX/MoveY); a note for the move.</summary>
        public static string Apply(FighterBook b, MoveEntry e)
        {
            string about = null;
            foreach (var r in Rules.Where(x => x.Fighter == b.Character.FilePrefix))
            {
                if (!e.Parts.Any(p => r.Parts.Contains(p))) continue;
                float x = 0, y = 0, vx = 0, vy = 0; bool on = false;
                (float Vx, float Vy, float Brake, float Gravity, float MaxFall)? s = null;
                foreach (var f in e.Data.Frames)
                {
                    bool now = r.Parts.Contains(f.Animation);
                    if (now && !on)
                    {
                        s = r.Speed(b.Moves, f.Animation.Contains("Air"));
                        if (s == null || float.IsNaN(s.Value.Vx) || float.IsNaN(s.Value.Vy)) break;
                        vx = s.Value.Vx; vy = s.Value.Vy; about = r.About;
                    }
                    on = now;
                    if (now && s != null)
                    {
                        x += vx; y += vy;
                        if (s.Value.Brake > 0) vx = Math.Max(0, vx - s.Value.Brake);
                        if (s.Value.Gravity > 0) vy = s.Value.MaxFall > 0 ? Math.Max(-s.Value.MaxFall, vy - s.Value.Gravity) : vy - s.Value.Gravity;
                    }
                    f.MoveX += x; f.MoveY += Math.Max(0, y);   // never shown below the floor it started on
                }
            }
            return about;
        }
    }
}
