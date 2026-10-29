// What the P+ card says about each character's costume slots.
using System;
using System.Collections.Generic;

namespace SkinManager.Core
{
    public sealed class CostumeSlot
    {
        public int Index;        // position on the character select screen (0 = first)
        public int FileNumber;   // NN in Fit<Name>NN.pac
        public int TeamColor;    // P+ color ID (0 red, 1 blue, 3 green, ...)
        public bool OnCard;      // false: the game loads this costume from the Brawl disc
        public string CostumePath(Character c, string prefix = null) => $"{c.FighterFolderPath}/Fit{prefix ?? c.FilePrefix}{FileNumber:00}.pac";
    }

    public static class PPlusCard
    {
        /// <summary>Largest costume file P+ itself ships (FitSamus27.pac, compressed). Brawl Online copies the
        /// compressed costume into one memory block online; files bigger than this freeze the loading screen.</summary>
        public const long MaxCostumeBytes = 1096170;

        /// <summary>Largest per-costume extra file P+ ships (FitDededeEtc20.pac, stored uncompressed).</summary>
        public const long MaxExtraFileBytes = 540896;

        /// <summary>Largest Kirby hat file P+ ships (FitKirbyPikachu00.pac, compressed).</summary>
        public const long MaxKirbyHatBytes = 247771;

        /// <summary>Space that must stay free on the card (Brawl Online prunes replays below 16 MiB).</summary>
        /// <summary>Room kept free on the card. Brawl Online adds its online plugin (about 240 KB) to its copy of the card,
        /// and P+ updates only need room for how much bigger they make P+ (the updater deletes each old file before
        /// writing the new one; 3.2.0 -> 3.2.1 made P+ 69 MB smaller). The game itself saves to Brawl Online's copy, not this card.</summary>
        public const long MinFreeBytes = 6L * 1024 * 1024;

        public static List<CostumeSlot> ReadSlots(Fat32Image card, Character c)
        {
            var b = card.ReadFile(c.SlotListPath);
            var slots = new List<CostumeSlot>();
            for (int i = 0; i + 1 < b.Length; i += 2)
            {
                if (b[i] == 0x0C) break;   // end of list
                var s = new CostumeSlot { Index = slots.Count, TeamColor = b[i], FileNumber = b[i + 1] };
                slots.Add(s);
            }
            var folder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in card.ListFolder(c.FighterFolderPath)) folder.Add(e.Name);
            foreach (var s in slots) s.OnCard = folder.Contains($"Fit{c.FilePrefix}{s.FileNumber:00}.pac");
            return slots;
        }

        public static string TeamColorName(int id)
        {
            switch (id)
            {
                case 0: return "Red"; case 1: return "Blue"; case 2: return "Yellow"; case 3: return "Green";
                case 4: return "Purple"; case 5: return "Light blue"; case 6: return "Pink"; case 7: return "Brown";
                case 8: return "Black"; case 9: return "White"; case 10: return "Orange"; case 11: return "Grey";
                default: return "Other";
            }
        }
    }
}
