// A setup to share: the mods a profile has on, as a small file a friend opens in their own copy of the app. It holds
// where each mod came from (its GameBanana page and file), not the mod itself, so the file stays tiny and every mod
// still goes through the usual checks when it's added on the other PC.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class SharedMod
    {
        [DataMember] public string Name;
        [DataMember] public string Kind;              // "skin", "stage", "song"
        [DataMember(EmitDefaultValue = false)] public string Where;   // "Marth, costume 3" / "Smashville"
        [DataMember(EmitDefaultValue = false)] public long ModId, FileId;
        [DataMember(EmitDefaultValue = false)] public string PageUrl, Author;
        /// <summary>It can be fetched by the app (it came from GameBanana).</summary>
        public bool Fetchable => ModId > 0 && FileId > 0;
    }

    [DataContract]
    public sealed class SharedSetup
    {
        public const string Extension = ".ppsetup";
        [DataMember] public int Version = 1;
        [DataMember] public string Name;              // the profile's name
        [DataMember(EmitDefaultValue = false)] public string By;
        [DataMember] public DateTime Made;
        [DataMember] public List<SharedMod> Mods = new List<SharedMod>();

        /// <summary>The mods that are on in this profile (or, with no profile, every mod that's on now).</summary>
        public static SharedSetup From(Library lib, Profile profile)
        {
            var on = profile != null ? new HashSet<string>(profile.On) : null;
            var s = new SharedSetup { Name = profile?.Name ?? "My setup", Made = DateTime.UtcNow };
            // one row per download: a skin's costumes from the same file are one mod to fetch
            foreach (var g in lib.Skins.Where(k => on != null ? on.Contains(k.Id) : k.Enabled).GroupBy(k => k.GameBananaFileId != 0 ? "f" + k.GameBananaFileId : "i" + k.Id))
            {
                var k = g.First();
                s.Mods.Add(new SharedMod
                {
                    Name = k.Name, Kind = "skin", ModId = k.GameBananaModId, FileId = k.GameBananaFileId, PageUrl = k.PageUrl, Author = k.Author,
                    Where = string.Join(", ", g.Select(x => SafeName(x) + ", costume " + (x.SlotIndex + 1)).Distinct()),
                });
            }
            foreach (var g in lib.Stages.Where(k => !k.IsSwap && (on != null ? on.Contains(k.Id) : k.Enabled)).GroupBy(k => k.GameBananaFileId != 0 ? "f" + k.GameBananaFileId : "i" + (k.Group ?? k.Id)))
            {
                var k = g.First();
                string where; try { where = k.Target?.StageName; } catch { where = null; }
                s.Mods.Add(new SharedMod { Name = k.Name, Kind = k.IsSong ? "song" : "stage", ModId = k.GameBananaModId, FileId = k.GameBananaFileId, PageUrl = k.PageUrl, Author = k.Author, Where = where });
            }
            return s;
        }

        static string SafeName(InstalledSkin k) { try { return k.Character.Name; } catch { return "a fighter"; } }

        public void Save(string path)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(SharedSetup)).WriteObject(ms, this);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        /// <summary>Reads a shared setup; a file that isn't one throws with a message to show.</summary>
        public static SharedSetup Open(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("That file is too big to be a shared setup.");
            SharedSetup s;
            try { using (var ms = new MemoryStream(bytes)) s = (SharedSetup)new DataContractJsonSerializer(typeof(SharedSetup)).ReadObject(ms); }
            catch { throw new InvalidDataException("That file isn't a shared setup (it should be a " + Extension + " file made with \"Share\" in Profiles)."); }
            if (s?.Mods == null) throw new InvalidDataException("That shared setup is empty.");
            // only GameBanana pages are opened or fetched from it
            foreach (var m in s.Mods)
                if (m.PageUrl != null && !m.PageUrl.StartsWith("https://gamebanana.com/", StringComparison.OrdinalIgnoreCase)) m.PageUrl = null;
            return s;
        }

        /// <summary>Whether this mod is already in the library (the same GameBanana file).</summary>
        public static bool Have(Library lib, SharedMod m) =>
            m.FileId != 0 && (lib.Skins.Any(k => k.GameBananaFileId == m.FileId) || lib.Stages.Any(k => k.GameBananaFileId == m.FileId));
    }
}
