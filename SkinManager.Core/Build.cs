// Where the game's files are on the SD card: Project+ keeps them in /Project+ (its "pf" folder holds the fighters,
// stages, music...). Other builds made from P+ (Cobalt Smash, ...) can use a folder of their own name: the app works
// in whichever one the card has (found when a card is picked in Settings).
using System;
using System.Linq;

namespace SkinManager.Core
{
    public static class Build
    {
        public const string PPlusRoot = "/Project+";
        /// <summary>The build's folder on the card ("/Project+").</summary>
        public static string Root = PPlusRoot;
        public static string Pf => Root + "/pf";
        public static bool IsPPlusFolder => string.Equals(Root, PPlusRoot, StringComparison.OrdinalIgnoreCase);

        /// <summary>The build's folder on a card: /Project+ when it's there, else any top folder with pf\fighter in it.</summary>
        public static string FindRoot(Fat32Image card)
        {
            if (card.Exists(PPlusRoot + "/pf/fighter")) return PPlusRoot;
            try
            {
                foreach (var e in card.ListFolder("/").Where(e => e.IsDirectory && !e.Name.StartsWith(".")))
                    if (card.Exists("/" + e.Name + "/pf/fighter")) return "/" + e.Name;
            }
            catch { }
            return null;
        }
    }
}
