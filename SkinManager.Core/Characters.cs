// The P+ v3.2 characters the modding tool supports, with the IDs P+ uses for each.
// Every row was checked against the real card: the slot list (costumeslots/NN.masq) has as many
// entries as the character's battle portraits (InfFaceCCCC) and select-screen portraits.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class Character
    {
        public int SlotListId;      // costumeslots/NN.masq
        public int CosmeticId;      // portraits and stock icons
        public string Name;         // shown to the player
        public string Folder;       // /Project+/pf/fighter/<Folder>
        public string FilePrefix;   // Fit<FilePrefix>NN.pac
        public string[] PartnerPrefixes = new string[0]; // other fighters whose costume goes with this one (Nana)

        public string FighterFolderPath => "/Project+/pf/fighter/" + Folder;
        public string SlotListPath => $"/Project+/pf/info/costumeslots/{SlotListId:00}.masq";
        public string CspFilePath => $"/Project+/pf/menu/common/char_bust_tex/MenSelchrFaceB{CosmeticId:00}0.brres";
        public string CspTextureName(int slot) => $"MenSelchrFaceB.{CosmeticId * 10 + slot + 1:000}";
        public string BattlePortraitPath(int slot) => $"/Project+/pf/info/portrite/InfFace{CosmeticId * 50 + slot + 1:0000}.brres";
        public string BattlePortraitTextureName(int slot) => $"InfFace.{CosmeticId * 50 + slot + 1:000}";
        public const string StockFilePath = "/Project+/pf/menu/common/StockFaceTex.brres";
        public string StockTextureName(int slot) => $"InfStc.{CosmeticId * 50 + slot + 1:0000}";
        public override string ToString() => Name;
    }

    public static class Characters
    {
        static Character C(int slotList, int cosmetic, string name, string folder, string prefix, params string[] partners) =>
            new Character { SlotListId = slotList, CosmeticId = cosmetic, Name = name, Folder = folder, FilePrefix = prefix, PartnerPrefixes = partners };

        public static readonly IReadOnlyList<Character> All = new[]
        {
            C(0, 0, "Mario", "Mario", "Mario"),
            C(1, 1, "Donkey Kong", "Donkey", "Donkey"),
            C(2, 2, "Link", "Link", "Link"),
            C(3, 3, "Samus", "Samus", "Samus"),
            C(4, 23, "Zero Suit Samus", "SZerosuit", "SZerosuit"),
            C(5, 4, "Yoshi", "Yoshi", "Yoshi"),
            C(6, 5, "Kirby", "Kirby", "Kirby"),
            C(7, 6, "Fox", "Fox", "Fox"),
            C(8, 7, "Pikachu", "Pikachu", "Pikachu"),
            C(9, 8, "Luigi", "Luigi", "Luigi"),
            C(10, 9, "Captain Falcon", "Captain", "Captain"),
            C(11, 10, "Ness", "Ness", "Ness"),
            C(12, 11, "Bowser", "Koopa", "Koopa"),
            C(13, 12, "Peach", "Peach", "Peach"),
            C(14, 13, "Zelda", "Zelda", "Zelda"),
            C(15, 14, "Sheik", "Sheik", "Sheik"),
            C(16, 15, "Ice Climbers", "Popo", "Popo", "Nana"),
            C(17, 16, "Marth", "Marth", "Marth"),
            C(18, 17, "Mr. Game & Watch", "GameWatch", "GameWatch"),
            C(19, 18, "Falco", "Falco", "Falco"),
            C(20, 19, "Ganondorf", "Ganon", "Ganon"),
            C(21, 37, "Wario", "Wario", "Wario"),
            C(22, 21, "Meta Knight", "Metaknight", "Metaknight"),
            C(23, 22, "Pit", "Pit", "Pit"),
            C(24, 24, "Olimar", "Pikmin", "Pikmin"),
            C(25, 25, "Lucas", "Lucas", "Lucas"),
            C(26, 26, "Diddy Kong", "Diddy", "Diddy"),
            C(28, 28, "Charizard", "PokeLizardon", "PokeLizardon"),
            C(29, 29, "Squirtle", "PokeZenigame", "PokeZenigame"),
            C(30, 30, "Ivysaur", "PokeFushigisou", "PokeFushigisou"),
            C(31, 31, "King Dedede", "Dedede", "Dedede"),
            C(32, 32, "Lucario", "Lucario", "Lucario"),
            C(33, 33, "Ike", "Ike", "Ike"),
            C(34, 34, "R.O.B.", "Robot", "Robot"),
            C(35, 36, "Jigglypuff", "Purin", "Purin"),
            C(36, 40, "Toon Link", "ToonLink", "ToonLink"),
            C(37, 43, "Wolf", "Wolf", "Wolf"),
            C(38, 45, "Snake", "Snake", "Snake"),
            C(39, 46, "Sonic", "Sonic", "Sonic"),
            C(40, 27, "Mewtwo", "Mewtwo", "Mewtwo"),
            C(41, 39, "Roy", "Roy", "Roy"),
            C(42, 42, "Knuckles", "Knuckles", "Knuckles"),
        };

        /// <summary>The character whose costume files start with Fit + this prefix (or a partner prefix).</summary>
        public static Character ByFilePrefix(string prefix) =>
            All.FirstOrDefault(c => string.Equals(c.FilePrefix, prefix, StringComparison.OrdinalIgnoreCase)) ??
            All.FirstOrDefault(c => c.PartnerPrefixes.Any(p => string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase)));
    }
}
