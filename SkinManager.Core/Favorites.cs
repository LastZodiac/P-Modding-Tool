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
