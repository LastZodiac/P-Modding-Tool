// Starred mods, shared by My Mods and the Mod Store (and by both SD cards). A GameBanana mod is starred by its
// GameBanana id, so starring it in the store shows in My Mods too; a mod added from a file by its own id.
// Each entry keeps the name and picture, so the store's Favorites list works without asking GameBanana.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class Favorite
    {
        [DataMember] public string Key;            // "gb:<mod id>" or "local:<installed id>"
        [DataMember] public string Name;
        [DataMember(EmitDefaultValue = false)] public string Author;
        [DataMember(EmitDefaultValue = false)] public string ThumbUrl;
        [DataMember(EmitDefaultValue = false)] public string Category;
        [DataMember(EmitDefaultValue = false)] public long ModId;
        [DataMember(EmitDefaultValue = false)] public string Kind;   // "skin", "stage", "music"
        [DataMember] public DateTime AddedAt;
        [DataMember(EmitDefaultValue = false)] public List<string> Lists;   // the collections it's in (your own named lists)
    }

    public sealed class Favorites
    {
        readonly string _file;
        readonly List<Favorite> _items;

        public Favorites(string dataFolder)
        {
            _file = Path.Combine(dataFolder, "favorites.json");
            try
            {
                using (var s = File.OpenRead(_file))
                    _items = (List<Favorite>)new DataContractJsonSerializer(typeof(List<Favorite>)).ReadObject(s);
            }
            catch { _items = new List<Favorite>(); }
            _items = _items ?? new List<Favorite>();
        }

        public static string KeyOf(long gameBananaModId, string installedId) =>
            gameBananaModId != 0 ? "gb:" + gameBananaModId : "local:" + installedId;

        public bool Has(string key) => _items.Any(f => f.Key == key);
        public IEnumerable<Favorite> All => _items.OrderByDescending(f => f.AddedAt);
        public event Action Changed;

        /// <summary>Stars or un-stars; returns whether it's starred now.</summary>
        public bool Toggle(Favorite f)
        {
            var have = _items.FirstOrDefault(x => x.Key == f.Key);
            if (have != null) _items.Remove(have);
            else { f.AddedAt = DateTime.Now; _items.Add(f); }
            Save();
            Changed?.Invoke();
            return have == null;
        }

        // ----- collections: named lists of starred mods ("Fox skins to try", "Tournament stages") -----

        /// <summary>Every collection's name, in the order they were first made.</summary>
        public List<string> Collections => _items.OrderBy(f => f.AddedAt).Where(f => f.Lists != null).SelectMany(f => f.Lists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool InCollection(string key, string list) => _items.Any(f => f.Key == key && f.Lists != null && f.Lists.Contains(list, StringComparer.OrdinalIgnoreCase));

        /// <summary>Puts a mod in a collection (starring it if it isn't), or takes it out (it stays starred).</summary>
        public void SetCollection(Favorite f, string list, bool on)
        {
            if (string.IsNullOrWhiteSpace(list)) return;
            var have = _items.FirstOrDefault(x => x.Key == f.Key);
            if (have == null)
            {
                if (!on) return;
                f.AddedAt = DateTime.Now; _items.Add(f); have = f;
            }
            have.Lists = have.Lists ?? new List<string>();
            have.Lists.RemoveAll(x => string.Equals(x, list, StringComparison.OrdinalIgnoreCase));
            if (on) have.Lists.Add(list.Trim());
            if (have.Lists.Count == 0) have.Lists = null;
            Save();
            Changed?.Invoke();
        }

        void Save()
        {
            try
            {
                string tmp = _file + ".tmp";
                using (var s = File.Create(tmp)) new DataContractJsonSerializer(typeof(List<Favorite>)).WriteObject(s, _items);
                if (File.Exists(_file)) File.Delete(_file);
                File.Move(tmp, _file);
            }
            catch { }
        }
    }
}
