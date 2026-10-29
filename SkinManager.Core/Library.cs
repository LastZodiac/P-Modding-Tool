// The modding tool's own records, kept next to the app: what is installed where, a copy of each
// installed skin (to put it back after a P+ update), and P+'s originals (to remove a skin).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace SkinManager.Core
{
    [DataContract]
    public sealed class InstalledFile
    {
        [DataMember] public string CardPath;
        [DataMember] public string Sha256;
    }

    [DataContract]
    public sealed class InstalledPicture
    {
        [DataMember] public PictureKind Kind;
        [DataMember] public string CardPath;       // the .brres holding it
        [DataMember] public string TextureName;
        [DataMember] public string PixelHash;      // what the picture looked like right after installing
    }

    [DataContract]
    public sealed class InstalledSkin
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public int SlotListId;        // which character
        [DataMember] public int SlotIndex;
        [DataMember] public int FileNumber;
        [DataMember] public DateTime InstalledAt;
        [DataMember] public List<InstalledFile> Files = new List<InstalledFile>();
        [DataMember] public List<InstalledPicture> Pictures = new List<InstalledPicture>();
        [DataMember(EmitDefaultValue = false)] public long GameBananaModId;    // 0 when added from a file
        [DataMember(EmitDefaultValue = false)] public long GameBananaFileId;
        [DataMember(EmitDefaultValue = false)] public string Author;
        [DataMember(EmitDefaultValue = false)] public string PageUrl;
        [DataMember(EmitDefaultValue = false)] public string ThumbUrl;
        [DataMember(EmitDefaultValue = false)] public bool Disabled;
        [DataMember(EmitDefaultValue = false)] public string PackageKey;   // which costume of its download
        [DataMember(EmitDefaultValue = false)] public string SourcePath;   // the download it came from           // kept in My Mods but not on the card
        public bool Enabled => !Disabled;
        public InstalledSkin Copy()
        {
            var c = (InstalledSkin)MemberwiseClone();
            c.Files = Files.Select(f => new InstalledFile { CardPath = f.CardPath, Sha256 = f.Sha256 }).ToList();
            c.Pictures = Pictures.Select(p => new InstalledPicture { Kind = p.Kind, CardPath = p.CardPath, TextureName = p.TextureName, PixelHash = p.PixelHash }).ToList();
            return c;
        }
        public Character Character => Characters.All.First(c => c.SlotListId == SlotListId);
    }

    /// <summary>A stage reskin: one stage file that replaces P+'s file at CardPath.</summary>
    [DataContract]
    public sealed class InstalledStage
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string CardPath;
        [DataMember] public string Sha256;
        [DataMember] public DateTime InstalledAt;
        [DataMember(EmitDefaultValue = false)] public bool Disabled;
        [DataMember(EmitDefaultValue = false)] public long GameBananaModId;
        [DataMember(EmitDefaultValue = false)] public long GameBananaFileId;
        [DataMember(EmitDefaultValue = false)] public string Author;
        [DataMember(EmitDefaultValue = false)] public string PageUrl;
        [DataMember(EmitDefaultValue = false)] public string ThumbUrl;
        [DataMember(EmitDefaultValue = false)] public string SourcePath;
        [DataMember(EmitDefaultValue = false)] public string PackageKey;
        [DataMember(EmitDefaultValue = false)] public string Kind;          // null: stage, "song": music
        [DataMember(EmitDefaultValue = false)] public string TargetLabel;   // what it replaces, in words
        [DataMember(EmitDefaultValue = false)] public string TrackList;     // "song-new": the stage's song list it's added to (Smashville.tlst)
        [DataMember(EmitDefaultValue = false)] public string SongTitle;     // "song-new": its name in the game's song lists
        [DataMember(EmitDefaultValue = false)] public bool LooksOnly;       // the map's looks on P+'s own stage file (looks-map.pac: the map)
        public bool Enabled => !Disabled;
        public bool IsSong => Kind == "song" || Kind == "song-new";
        public bool IsNewSong => Kind == "song-new";
        public StageFile Target => IsSong ? new StageFile { CardPath = CardPath, StageName = TargetLabel ?? Path.GetFileNameWithoutExtension(CardPath), Version = "" } : Stages.Describe(CardPath);
        public InstalledStage Copy() => (InstalledStage)MemberwiseClone();
    }

    /// <summary>A named set of mods that are on (switching to it turns exactly those on).</summary>
    [DataContract]
    public sealed class Profile
    {
        [DataMember] public string Name;
        [DataMember] public List<string> On = new List<string>();   // ids of skins and stages that are on
    }

    [DataContract]
    public sealed class WrittenFile
    {
        [DataMember] public string CardPath;
        [DataMember] public string Sha256;
    }

    [DataContract]
    sealed class LibraryFile
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<InstalledSkin> Skins = new List<InstalledSkin>();
        [DataMember] public List<WrittenFile> Written = new List<WrittenFile>();
        [DataMember(EmitDefaultValue = false)] public List<InstalledStage> Stages = new List<InstalledStage>();
        [DataMember(EmitDefaultValue = false)] public List<Profile> Profiles = new List<Profile>();
        [DataMember(EmitDefaultValue = false)] public string ActiveProfile;
    }

    public sealed class Library
    {
        public string Folder { get; }
        public List<InstalledSkin> Skins { get; private set; } = new List<InstalledSkin>();
        public List<InstalledStage> Stages { get; private set; } = new List<InstalledStage>();
        public List<Profile> Profiles { get; private set; } = new List<Profile>();
        /// <summary>The profile in use. Null means "P+ as it comes" (no mods on).</summary>
        public string ActiveProfile { get; set; }
        public const string DefaultName = "P+ as it comes (no mods)";

        /// <summary>The ids of everything that is on right now.</summary>
        public List<string> OnNow() => Skins.Where(s => s.Enabled).Select(s => s.Id).Concat(Stages.Where(s => s.Enabled).Select(s => s.Id)).ToList();

        public Profile Active => Profiles.FirstOrDefault(p => p.Name == ActiveProfile);

        /// <summary>The active profile remembers what's on now; deleted mods leave every profile.</summary>
        public void SyncProfiles()
        {
            if (ActiveProfile == null && OnNow().Count > 0)
            {
                // something was turned on while "P+ as it comes" was chosen: that's the Main profile now
                if (!Profiles.Any(p => p.Name == "Main")) Profiles.Insert(0, new Profile { Name = "Main" });
                ActiveProfile = "Main";
            }
            var ids = new HashSet<string>(Skins.Select(s => s.Id).Concat(Stages.Select(s => s.Id)));
            foreach (var p in Profiles) p.On.RemoveAll(id => !ids.Contains(id));
            var a = Active;
            if (a != null) a.On = OnNow();
        }
        public string StageFolder(string id) => Path.Combine(Folder, "StageMods", id);
        /// <summary>The last version of each shared picture file this app wrote (to know if P+ changed it since).</summary>
        public Dictionary<string, string> Written { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string RecordsPath => Path.Combine(Folder, "installed.json");
        public string DiscCacheFolder => Path.Combine(Folder, "DiscCostumes");
        public string SkinFolder(string id) => Path.Combine(Folder, "Skins", id);
        string OriginalsFolder => Path.Combine(Folder, "Originals");

        public Library(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
            if (File.Exists(RecordsPath))
            {
                using (var s = File.OpenRead(RecordsPath))
                {
                    var f = (LibraryFile)new DataContractJsonSerializer(typeof(LibraryFile)).ReadObject(s);
                    Skins = f.Skins ?? new List<InstalledSkin>();
                    Stages = f.Stages ?? new List<InstalledStage>();
                    Profiles = f.Profiles ?? new List<Profile>();
                    ActiveProfile = f.ActiveProfile;
                    foreach (var w in f.Written ?? new List<WrittenFile>()) Written[w.CardPath] = w.Sha256;
                }
            }
        }

        /// <summary>Makes sure there's at least one profile (a "Main" one with whatever is on).</summary>
        public void EnsureProfiles()
        {
            if (Profiles.Count == 0)
            {
                Profiles.Add(new Profile { Name = "Main", On = OnNow() });
                if (ActiveProfile == null && (Skins.Count > 0 || Stages.Count > 0)) ActiveProfile = "Main";
                else if (ActiveProfile == null) ActiveProfile = "Main";
            }
        }

        public void Save()
        {
            SyncProfiles();
            string tmp = RecordsPath + ".tmp";
            using (var s = File.Create(tmp))
                new DataContractJsonSerializer(typeof(LibraryFile)).WriteObject(s, new LibraryFile
                {
                    Skins = Skins,
                    Stages = Stages,
                    Profiles = Profiles,
                    ActiveProfile = ActiveProfile,
                    Written = Written.Select(kv => new WrittenFile { CardPath = kv.Key, Sha256 = kv.Value }).ToList(),
                });
            if (File.Exists(RecordsPath)) File.Replace(tmp, RecordsPath, null); else File.Move(tmp, RecordsPath);
        }

        /// <summary>The skin that is on in this slot, if any.</summary>
        public InstalledSkin At(Character c, int slot) => Skins.FirstOrDefault(s => s.Enabled && s.SlotListId == c.SlotListId && s.SlotIndex == slot);
        /// <summary>Every skin in My Mods for this slot (only one can be on).</summary>
        public List<InstalledSkin> AllAt(int slotListId, int slot) => Skins.Where(s => s.SlotListId == slotListId && s.SlotIndex == slot).ToList();
        public bool InConflict(InstalledSkin s) => Skins.Count(x => x.SlotListId == s.SlotListId && x.SlotIndex == s.SlotIndex) > 1;

        // ----- P+'s originals -----

        static string Key(string cardPath) => cardPath.Trim('/').Replace('/', '_').ToLowerInvariant();

        public bool HasOriginalFile(string cardPath) =>
            File.Exists(Path.Combine(OriginalsFolder, Key(cardPath) + ".bin")) || File.Exists(Path.Combine(OriginalsFolder, Key(cardPath) + ".absent"));

        /// <summary>Saves what the card had at this path before any skin touched it (null: no file).</summary>
        public void SaveOriginalFile(string cardPath, byte[] contents, bool replace = false)
        {
            if (HasOriginalFile(cardPath) && !replace) return;
            Directory.CreateDirectory(OriginalsFolder);
            string bin = Path.Combine(OriginalsFolder, Key(cardPath) + ".bin"), absent = Path.Combine(OriginalsFolder, Key(cardPath) + ".absent");
            if (File.Exists(bin)) File.Delete(bin);
            if (File.Exists(absent)) File.Delete(absent);
            if (contents == null) File.WriteAllText(absent, "This file was not on the card; the game read it from the Brawl disc.");
            else File.WriteAllBytes(bin, contents);
        }

        public void ForgetOriginalFile(string cardPath)
        {
            foreach (var ext in new[] { ".bin", ".absent" })
            {
                string p = Path.Combine(OriginalsFolder, Key(cardPath) + ext);
                if (File.Exists(p)) File.Delete(p);
            }
        }

        /// <summary>Size of the original file (0 when the card had none there), or null when no original was saved.</summary>
        public long? OriginalSize(string cardPath)
        {
            string bin = Path.Combine(OriginalsFolder, Key(cardPath) + ".bin");
            if (File.Exists(bin)) return new FileInfo(bin).Length;
            if (File.Exists(Path.Combine(OriginalsFolder, Key(cardPath) + ".absent"))) return 0;
            return null;
        }

        /// <summary>The original contents, or null when the card had no file there.</summary>
        public byte[] OriginalFile(string cardPath)
        {
            string bin = Path.Combine(OriginalsFolder, Key(cardPath) + ".bin");
            return File.Exists(bin) ? File.ReadAllBytes(bin) : null;
        }

        string PictureKey(string brresPath, string texture) => Path.Combine(OriginalsFolder, "pictures", Key(brresPath) + "__" + texture + ".png");
        public bool HasOriginalPicture(string brresPath, string texture) => File.Exists(PictureKey(brresPath, texture));
        public void SaveOriginalPicture(string brresPath, string texture, Bitmap picture, bool replace = false)
        {
            string p = PictureKey(brresPath, texture);
            if (File.Exists(p) && !replace) return;
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            picture.Save(p, ImageFormat.Png);
        }
        public void ForgetOriginalPicture(string brresPath, string texture) { try { File.Delete(PictureKey(brresPath, texture)); } catch { } }
        /// <summary>The pictures of a file whose originals were saved (their texture names).</summary>
        public List<string> OriginalPictureNames(string brresPath)
        {
            string dir = Path.Combine(OriginalsFolder, "pictures"), prefix = Key(brresPath) + "__";
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, prefix + "*.png").Select(f => Path.GetFileNameWithoutExtension(f).Substring(prefix.Length)).ToList();
        }
        public Bitmap OriginalPicture(string brresPath, string texture)
        {
            string p = PictureKey(brresPath, texture);
            if (!File.Exists(p)) return null;
            using (var tmp = new Bitmap(p)) return new Bitmap(tmp);
        }

        public static string Sha256(byte[] b)
        {
            using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2")));
        }
    }
}
