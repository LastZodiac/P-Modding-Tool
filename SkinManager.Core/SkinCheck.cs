// The safety checks a skin must pass before it goes on the card.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public sealed class CheckResult
    {
        public bool Passed;
        public string Title;     // short, shown with a tick or a cross
        public string Detail;    // plain-language explanation
    }

    public static unsafe class SkinCheck
    {
        /// <summary>The model bones the character's hurtboxes ride on, read from the fighter's moveset (Fit<Name>.pac).</summary>
        public static int[] HurtboxBones(byte[] fighterPac)
        {
            using (var tmp = new TempFile(".pac", fighterPac))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                foreach (var entry in root.Children)
                {
                    var src = entry.WorkingUncompressed;
                    if (src.Length < 0x40) continue;
                    var b = new byte[src.Length];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)(void*)src.Address, b, 0, b.Length);
                    var bones = ParseHurtboxes(b);
                    if (bones != null) return bones;
                }
            }
            return null;
        }

        static int BE(byte[] b, int at) => at < 0 || at + 4 > b.Length ? -1 : (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        static int[] ParseHurtboxes(byte[] b)
        {
            int size = BE(b, 0), lookupOff = BE(b, 4), lookupCount = BE(b, 8), dataCount = BE(b, 12), extCount = BE(b, 16);
            if (size != b.Length || BE(b, 20) != 0 || BE(b, 24) != 0 || BE(b, 28) != 0) return null;   // not a moveset
            int table = 0x20 + lookupOff + lookupCount * 4;
            int strings = table + dataCount * 8 + extCount * 8;
            for (int i = 0; i < dataCount; i++)
            {
                int dataOff = BE(b, table + i * 8), strOff = BE(b, table + i * 8 + 4);
                int s = strings + strOff, e = s;
                while (e < b.Length && b[e] != 0) e++;
                if (System.Text.Encoding.ASCII.GetString(b, s, e - s) != "data") continue;
                int header = 0x20 + dataOff;
                int misc = 0x20 + BE(b, header + 0x10);
                int hurtOff = BE(b, misc + 0x0C), hurtCount = BE(b, misc + 0x10);
                if (hurtOff <= 0 || hurtCount <= 0 || hurtCount > 200) return null;
                var bones = new System.Collections.Generic.SortedSet<int>();
                for (int h = 0; h < hurtCount; h++)
                {
                    int at = 0x20 + hurtOff + h * 0x20 + 0x1C;
                    if (at + 2 > b.Length) return null;
                    byte dat0 = b[at], dat1 = b[at + 1];
                    bones.Add((((dat1 >> 7) & 1) | ((dat0 << 1) & 0xFF)) & 0x1FF);
                }
                return bones.ToArray();
            }
            return null;
        }

        /// <summary>Returns the costume file the way P+ stores that kind of file: the costume itself and
        /// Kirby's hats compressed (Brawl's LZ77), the extra files (Etc, Final, Entry, Result) uncompressed.</summary>
        public static byte[] Prepared(string path, string kind)
        {
            Installer.Quiet();
            bool compress = !(kind == "Etc" || kind == "Final" || kind == "Entry" || kind == "Result");
            var raw = File.ReadAllBytes(path);
            bool isCompressed = raw.Length >= 4 && (raw[0] == 0x11 || raw[0] == 0x10);
            if (isCompressed == compress) return raw;
            using (var tmp = new TempFile(".pac", raw))
            using (var outFile = new TempFile(".pac"))
            {
                lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                {
                    if (!(root is ARCNode)) throw new InvalidDataException(System.IO.Path.GetFileName(path) + " is not a Brawl costume file.");
                    root.Compression = compress ? "ExtendedLZ77" : "None";
                    root.Export(outFile.Path);
                }
                return File.ReadAllBytes(outFile.Path);
            }
        }

        /// <summary>The largest file of each kind that P+ itself ships (read from the card).</summary>
        public static long LimitFor(string kind) =>
            kind == "" || kind == "Nana" ? PPlusCard.MaxCostumeBytes
            : kind == "Etc" || kind == "Final" || kind == "Entry" || kind == "Result" ? PPlusCard.MaxExtraFileBytes
            : PPlusCard.MaxKirbyHatBytes;

        public static CheckResult Size(string fileName, long bytes, long limit) => new CheckResult
        {
            Passed = bytes <= limit,
            Title = bytes <= limit ? "Size is safe for online play" : "Too big for online play",
            Detail = bytes <= limit
                ? $"{fileName} is {bytes / 1024} KB. The biggest file of this kind P+ ships is {limit / 1024} KB."
                : $"{fileName} is {bytes / 1024} KB, bigger than anything of this kind P+ ships ({limit / 1024} KB). Online, costume files that don't fit in the game's memory freeze everyone on the loading screen.",
        };

        /// <summary>Bone layout of a costume's model: names and bind poses.</summary>
        public sealed class Skeleton
        {
            public string[] Names;
            public float[][] Matrices;
        }

        public static Skeleton ReadSkeleton(byte[] costume)
        {
            using (var tmp = new TempFile(".pac", costume))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var model = root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().FirstOrDefault();
                if (model == null) return null;
                var bones = model.BoneCache.Cast<MDL0BoneNode>().ToArray();
                return new Skeleton
                {
                    Names = bones.Select(b => b.Name).ToArray(),
                    Matrices = bones.Select(b => Enumerable.Range(0, 16).Select(i => b.BindMatrix[i]).ToArray()).ToArray(),
                };
            }
        }

        /// <summary>Gameplay-safe means the bones the character's hurtboxes ride on are the same bones, in the
        /// same place, as in the costume it is compared with. Each player's game builds the opponent's
        /// character from its own copy of that slot's costume, so if those bones differ, the two games
        /// would disagree about hits. Other bones (hair, capes, accessories) only change the look.</summary>
        public static CheckResult SameSkeleton(Skeleton skin, IEnumerable<Skeleton> originals, int[] hurtboxBones, string characterName, string comparedWith)
        {
            if (skin == null)
                return new CheckResult { Passed = false, Title = "No character model found", Detail = "The costume file has no model inside, so it is not a recolor." };
            string why = "there was no P+ costume to compare with";
            foreach (var o in originals.Where(x => x != null))
            {
                why = Compare(skin, o, hurtboxBones);
                if (why == null)
                    return new CheckResult
                    {
                        Passed = true,
                        Title = "Safe for online play",
                        Detail = $"The bones {characterName}'s hurtboxes use match {comparedWith} exactly, so both players' games see the same hits.",
                    };
            }
            return new CheckResult
            {
                Passed = false,
                Title = "Changes " + characterName + "'s skeleton",
                Detail = $"Compared with {comparedWith}: {why}. Hurtboxes ride on these bones, so online the two players' games would disagree about hits.",
            };
        }

        static string Compare(Skeleton a, Skeleton b, int[] hurtboxBones)
        {
            int common = Math.Min(a.Names.Length, b.Names.Length);
            bool useHurtboxes = hurtboxBones != null && hurtboxBones.Length > 0 && hurtboxBones.All(i => i < b.Names.Length);
            var important = useHurtboxes ? hurtboxBones : Enumerable.Range(0, b.Names.Length).ToArray();
            foreach (int i in important)
            {
                if (i >= a.Names.Length) return $"it is missing the bone \"{b.Names[i]}\"";
                if (!string.Equals(a.Names[i], b.Names[i], StringComparison.Ordinal)) return $"bone {i + 1} is \"{a.Names[i]}\" where P+ has \"{b.Names[i]}\"";
                for (int j = 0; j < 16; j++)
                    if (Math.Abs(a.Matrices[i][j] - b.Matrices[i][j]) > 0.01f) return $"the bone \"{a.Names[i]}\" is in a different place";
            }
            if (!useHurtboxes && a.Names.Length != b.Names.Length) return $"it has {a.Names.Length} bones where P+ has {b.Names.Length}";
            for (int i = 0; i < common; i++)
                if (!string.Equals(a.Names[i], b.Names[i], StringComparison.Ordinal)) return $"its bones are in a different order (bone {i + 1} is \"{a.Names[i]}\" where P+ has \"{b.Names[i]}\")";
            return null;
        }
    }
}
