// Stage swaps: which version of a stage you get by just picking its icon. Each stage slot has a list of versions on the
// card (/stage/stageslot/<slot>.asl): a button (none, L, R, Z, a list entry...) and the version's settings. Swapping the
// settings of two entries makes the button give the normal version and just picking it give the other. Only done for
// versions that play exactly alike, so what you get online is the same stage for both players.
using System;
using System.Linq;
using System.Text;

namespace SkinManager.Core
{
    public static class StageSwaps
    {
        /// <summary>The two versions, as kept in My Mods: "0000|STGSMASHVILLE|0040|STGSMASHVILLE_L_SV_SAKURA".</summary>
        public static string Describe(StageSpot a, StageSpot b) => $"{a.Button:X4}|{a.Param}|{b.Button:X4}|{b.Param}";

        static bool Parse(string swap, out ushort a, out string pa, out ushort b, out string pb)
        {
            a = b = 0; pa = pb = null;
            var p = (swap ?? "").Split('|');
            if (p.Length != 4) return false;
            try { a = Convert.ToUInt16(p[0], 16); b = Convert.ToUInt16(p[2], 16); } catch { return false; }
            pa = p[1]; pb = p[3];
            return true;
        }

        /// <summary>The slot list with the two versions' settings swapped (null: it isn't laid out as expected, or doesn't
        /// have those two versions any more).</summary>
        public static byte[] Swap(byte[] asl, string swap)
        {
            if (asl == null || asl.Length < 8 || asl[0] != 'A' || asl[1] != 'S' || asl[2] != 'L' || asl[3] != 'S') return null;
            if (!Parse(swap, out ushort a, out string pa, out ushort b, out string pb)) return null;
            int count = asl[4] << 8 | asl[5], nameBase = asl[6] << 8 | asl[7];
            string Name(int i)
            {
                int at = nameBase + (asl[8 + 4 * i + 2] << 8 | asl[8 + 4 * i + 3]), end = at;
                if (at >= asl.Length) return null;
                while (end < asl.Length && asl[end] != 0) end++;
                return Encoding.ASCII.GetString(asl, at, end - at);
            }
            int Find(ushort button, string param)
            {
                for (int i = 0; i < count && 8 + 4 * i + 3 < asl.Length; i++)
                    if ((asl[8 + 4 * i] << 8 | asl[8 + 4 * i + 1]) == button && string.Equals(Name(i), param, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }
            int ia = Find(a, pa), ib = Find(b, pb);
            if (ia < 0 || ib < 0 || ia == ib) return null;
            var r = (byte[])asl.Clone();
            for (int k = 2; k < 4; k++) { byte t = r[8 + 4 * ia + k]; r[8 + 4 * ia + k] = r[8 + 4 * ib + k]; r[8 + 4 * ib + k] = t; }
            return r;
        }

        /// <summary>The two versions of a swap, as P+ has them (read from P+'s own list).</summary>
        public static (StageSpot A, StageSpot B) Spots(StageSelect select, string asl, string swap)
        {
            if (!Parse(swap, out ushort a, out string pa, out ushort b, out string pb)) return (null, null);
            var all = select.All.Where(x => string.Equals(x.Asl, asl, StringComparison.OrdinalIgnoreCase)).ToList();
            return (all.FirstOrDefault(x => x.Button == a && string.Equals(x.Param, pa, StringComparison.OrdinalIgnoreCase)),
                    all.FirstOrDefault(x => x.Button == b && string.Equals(x.Param, pb, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
