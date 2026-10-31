// What a move's scripts know while they run: the variables they set (and the fighter settings they read), whether the
// fighter is on the ground or in the air, and the conditions they test. A condition the files can't answer (the stick,
// a flag the game's code sets) is "unknown": the script then follows its first branch, as the game usually does for a
// move done with no stick and nothing in the way.
using System;
using System.Collections.Generic;

namespace SkinManager.Core
{
    public sealed class ScriptState
    {
        public Moveset M;
        public string Fighter;          // the fighter's file name (Fox...): its numbered settings (SettingIds)
        public bool Air;                // the piece being played is the air version
        readonly Dictionary<int, float> _vars = new Dictionary<int, float>();   // LA/RA variables by their raw id (basic, float, bit)

        public ScriptState(Moveset m, string fighter) { M = m; Fighter = fighter; }

        /// <summary>A variable's value (null: not known: never set by these scripts, or set by the game's code).</summary>
        public float? Get(int raw)
        {
            int mem = (raw >> 28) & 0xF, idx = raw & 0xFFFFFF;
            if (mem == 0)   // IC: the game's values; the fighter's numbered settings are known
            {
                if (idx >= 24000 && idx < 25000 && SettingIds.Find(Fighter, idx, out int b, out int o)) { int v = M.FighterI(b, o); return v == int.MinValue ? (float?)null : v; }
                if (idx >= 4000 && idx < 5000 && SettingIds.Find(Fighter, idx, out b, out o)) { float f = M.FighterF(b, o); return float.IsNaN(f) ? (float?)null : f; }
                return null;
            }
            return _vars.TryGetValue(raw, out var x) ? x : (float?)null;
        }

        public void Set(int raw, float? v)
        {
            if (((raw >> 28) & 0xF) == 0) return;   // the game's own values can't be set by a script
            if (v == null) _vars.Remove(raw); else _vars[raw] = v.Value;
        }

        /// <summary>An event's argument as a number: a value, a scalar, a yes/no, or a variable (null: not known).</summary>
        public float? Arg(MoveEvent e, int k)
        {
            if (k >= e.Values.Length) return null;
            switch (e.Types[k])
            {
                case 0: return e.Values[k];
                case 1: return e.Scalar(k);
                case 3: return e.Values[k] != 0 ? 1 : 0;
                case 5: return Get(e.Values[k]);
                default: return null;
            }
        }

        /// <summary>Runs a variable event (set, add, subtract, step, flags). False if it isn't one.</summary>
        public bool Variable(MoveEvent e)
        {
            int Dst(int k) => k < e.Values.Length && e.Types[k] == 5 ? e.Values[k] : 0;
            switch (e.Id)
            {
                case 0x12000200: case 0x12060200: Set(Dst(1), Arg(e, 0)); return true;                                   // set
                case 0x12010200: case 0x12070200: { var d = Dst(1); Set(d, Get(d) + Arg(e, 0)); return true; }           // add
                case 0x12020200: case 0x12080200: { var d = Dst(1); Set(d, Get(d) - Arg(e, 0)); return true; }           // subtract
                case 0x12030100: { var d = Dst(0); Set(d, Get(d) + 1); return true; }                                    // + 1
                case 0x12040100: { var d = Dst(0); Set(d, Get(d) - 1); return true; }                                    // - 1
                case 0x120A0100: Set(Dst(0), 1); return true;                                                            // flag on
                case 0x120B0100: Set(Dst(0), 0); return true;                                                            // flag off
            }
            if ((e.Id >> 24) == 0x12) { Set(Dst(e.Values.Length - 1), null); return true; }   // other maths: the result isn't known
            return false;
        }

        /// <summary>A requirement (in an If, And, Or, Else If): true, false, or null when the files can't say.</summary>
        public bool? Requirement(MoveEvent e)
        {
            int k = Array.IndexOf(e.Types, 6);
            if (k < 0) return null;
            uint r = (uint)e.Values[k];
            bool not = (r & 0x80000000) != 0;
            bool? v;
            switch ((int)(r & 0x7FFFFFFF))
            {
                case 3: v = !Air; break;                    // on the ground
                case 4: v = Air; break;                     // in the air
                case 7:                                     // compare two values
                {
                    var a = Arg(e, k + 1); var b = Arg(e, k + 3);
                    int how = k + 2 < e.Values.Length ? e.Values[k + 2] : -1;
                    if (a == null || b == null) { v = null; break; }
                    float x = a.Value, y = b.Value;
                    v = how == 0 ? x < y : how == 1 ? x <= y : how == 2 ? Math.Abs(x - y) < 1e-4f : how == 3 ? Math.Abs(x - y) >= 1e-4f : how == 4 ? x >= y : how == 5 ? x > y : (bool?)null;
                    break;
                }
                case 8:                                     // a flag is on
                {
                    var f = Arg(e, k + 1);
                    v = f == null ? (bool?)null : f.Value != 0;
                    break;
                }
                case 9: v = true; break;                    // facing right (as it's drawn)
                case 0xA: v = false; break;                 // facing left
                case 0xB: v = false; break;                 // a hitbox connected (frame data is the move hitting nothing)
                default: v = null; break;
            }
            return v == null ? (bool?)null : not ? !v.Value : v.Value;
        }
    }
}
