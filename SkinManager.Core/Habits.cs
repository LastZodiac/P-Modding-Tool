// A player's habits in one game, read from the replay's subactions (by their animation names) and positions:
// how they tech and get up, get off the ledge, defend (shield, rolls, spot dodges, air dodges), grab and throw,
// L-cancel, wavedash, and how much of the game they spent near the middle of the stage.
// Forward (F) rolls and techs go the way the fighter faces, back (B) the other way (checked on many games); towards
// or away is from the other player's side at that moment.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class PlayerHabits
    {
        [DataMember] public bool Known;                                                   // read with move names
        // teching (when knocked down)
        [DataMember] public int TechInPlace, TechToward, TechAway, MissedTech, WallTech;
        // after a missed tech
        [DataMember] public int GetupStand, GetupToward, GetupAway, GetupAttack;
        // on the ledge
        [DataMember] public int LedgeGrabs, LedgeGetup, LedgeRoll, LedgeAttack, LedgeJump, LedgeDrop;
        // defending
        [DataMember] public int Shields, SpotDodges, RollsToward, RollsAway, AirDodges, Wavedashes;
        // grabbing
        [DataMember] public int GrabTries, Grabs, Pummels, ThrowF, ThrowB, ThrowUp, ThrowDown;
        // aerials landed on the ground, and how many were L-cancelled
        [DataMember] public int AerialLandings, LCancels;
        // frames alive, frames near the middle of the stage (the middle third), frames off the stage
        [DataMember] public int FramesAlive, FramesCenter, FramesOffstage;

        public int Techs => TechInPlace + TechToward + TechAway;
        public int TechChances => Techs + MissedTech;
        public int LedgeOptions => LedgeGetup + LedgeRoll + LedgeAttack + LedgeJump + LedgeDrop;
        public int Throws => ThrowF + ThrowB + ThrowUp + ThrowDown;

        public void Add(PlayerHabits o)
        {
            if (o == null || !o.Known) return;
            Known = true;
            foreach (var f in typeof(PlayerHabits).GetFields()) if (f.FieldType == typeof(int)) f.SetValue(this, (int)f.GetValue(this) + (int)f.GetValue(o));
        }
    }

    /// <summary>What's needed of a fighter's moveset to read habits: its subaction (animation) names by number and its
    /// aerials' full landing lag (neutral, forward, back, up, down).</summary>
    [DataContract]
    public sealed class MoveTable
    {
        [DataMember] public List<string> Names = new List<string>();
        [DataMember] public int[] LandingLag = new int[5];
        public int LagOf(string landingAnim)
        {
            switch (landingAnim) { case "LandingAirN": return LandingLag[0]; case "LandingAirF": return LandingLag[1]; case "LandingAirB": return LandingLag[2]; case "LandingAirHi": return LandingLag[3]; case "LandingAirLw": return LandingLag[4]; }
            return 0;
        }
    }

    public static class Habits
    {
        /// <summary>One player's habits. moves: each player's subaction per frame; names: this player's animation names.</summary>
        public static PlayerHabits Read(int me, int players, MoveTable table, List<int>[] move, List<float>[] px, List<float>[] py, List<sbyte>[] face, List<byte>[] sit, ReplayData d)
        {
            var h = new PlayerHabits { Known = true };
            var m = move[me]; int n = m.Count;
            int other = players == 2 ? 1 - me : -1;
            var names = table.Names;
            string Name(int sub) => sub >= 0 && sub < names.Count ? names[sub] ?? "" : "";
            // the runs: a subaction from a frame for some frames
            var runs = new List<(string Name, int Start, int Length)>();
            for (int i = 0, start = 0; i <= n; i++)
                if (i == n || i > 0 && m[i] != m[i - 1]) { if (i > start) runs.Add((Name(m[start]), start, i - start)); start = i; }
            float X(int i) => i < px[me].Count ? px[me][i] : 0;
            // towards the other player: moving the way they are
            bool Toward(int from, int to)
            {
                if (other < 0 || to >= n || from >= px[other].Count) return false;
                float dx = X(Math.Min(to, n - 1)) - X(from), side = px[other][from] - X(from);
                return Math.Sign(dx) == Math.Sign(side);
            }
            for (int r = 0; r < runs.Count; r++)
            {
                var (nm, s, len) = runs[r];
                string prev = r > 0 ? runs[r - 1].Name : "", next = r + 1 < runs.Count ? runs[r + 1].Name : "";
                int end = s + len;
                switch (nm)
                {
                    case "Passive": h.TechInPlace++; break;
                    case "PassiveStandF": case "PassiveStandB": if (Toward(s, end)) h.TechToward++; else h.TechAway++; break;
                    case "PassiveWall": case "PassiveWallJump": case "PassiveCeil": if (!prev.StartsWith("PassiveWall")) h.WallTech++; break;
                    case "DownBoundU": case "DownBoundD": h.MissedTech++; break;
                    case "DownStandU": case "DownStandD": h.GetupStand++; break;
                    case "DownForwardU": case "DownForwardD": case "DownBackU": case "DownBackD": if (Toward(s, end)) h.GetupToward++; else h.GetupAway++; break;
                    case "DownAttackU": case "DownAttackD": h.GetupAttack++; break;
                    case "CliffCatch": h.LedgeGrabs++; break;
                    case "CliffClimbQuick": case "CliffClimbSlow": h.LedgeGetup++; break;
                    case "CliffEscapeQuick": case "CliffEscapeSlow": h.LedgeRoll++; break;
                    case "CliffAttackQuick": case "CliffAttackSlow": h.LedgeAttack++; break;
                    case "CliffJumpQuick1": case "CliffJumpSlow1": h.LedgeJump++; break;
                    case "CliffWait":
                        // let go of the ledge: straight into falling or a jump in the air
                        if (next.StartsWith("Fall") || next.StartsWith("JumpAerial") || next.StartsWith("AttackAir") || next.StartsWith("SpecialAir")) h.LedgeDrop++;
                        break;
                    case "GuardOn": h.Shields++; break;
                    case "EscapeN": h.SpotDodges++; break;
                    case "EscapeF": case "EscapeB": if (Toward(s, end)) h.RollsToward++; else h.RollsAway++; break;
                    case "EscapeAir":
                        // a wavedash: straight out of a jump, into the ground within a few frames
                        if (len <= 4 && (prev.StartsWith("JumpF") || prev.StartsWith("JumpB") || prev == "JumpSquat") && next.StartsWith("Landing")) h.Wavedashes++;
                        else h.AirDodges++;
                        break;
                    case "Catch": case "CatchDash": case "CatchTurn":
                        h.GrabTries++;
                        if (next.StartsWith("CatchPull") || next == "CatchWait") h.Grabs++;
                        break;
                    case "CatchAttack": h.Pummels++; break;
                    case "ThrowF": h.ThrowF++; break;
                    case "ThrowB": h.ThrowB++; break;
                    case "ThrowHi": h.ThrowUp++; break;
                    case "ThrowLw": h.ThrowDown++; break;
                    default:
                        if (nm.StartsWith("LandingAir"))
                        {
                            // L-cancelled: the landing lag halved (a frame either way)
                            h.AerialLandings++;
                            int full = table.LagOf(nm);
                            if (full > 0 && len <= full / 2 + 1) h.LCancels++;
                        }
                        break;
                }
            }
            // where they spent the game
            float left = d.StageLeft, right = d.StageRight, third = (right - left) / 3;
            for (int i = 0; i < n && i < sit[me].Count; i++)
            {
                if (i >= 0 && i < px[me].Count && float.IsNaN(px[me][i])) continue;
                bool alive = sit[me][i] <= 2;
                if (!alive) continue;
                h.FramesAlive++;
                float x = px[me][i], y = py[me][i];
                if (right - left > 20 && x > left + third && x < right - third) h.FramesCenter++;
                if (sit[me][i] == 2 && (x < left - 4 || x > right + 4 || y < -10)) h.FramesOffstage++;
            }
            return h;
        }
    }
}
