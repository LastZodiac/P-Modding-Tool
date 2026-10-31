// Puts skins on the card and takes them off again. Every change is made on a copy of the card,
// checked by reading it back, and only then swapped in (the previous card is kept as a backup).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace SkinManager.Core
{
    public sealed class SkinPlan
    {
        public string Name;
        public Character Character;
        public CostumeSlot Slot;
        public List<KeyValuePair<string, byte[]>> Files = new List<KeyValuePair<string, byte[]>>();   // card path -> bytes
        public Dictionary<string, byte[]> Originals = new Dictionary<string, byte[]>();   // card path -> the file as it came, when it was made smaller
        public Dictionary<PictureKind, Bitmap> Pictures = new Dictionary<PictureKind, Bitmap>();
        public List<CheckResult> Checks = new List<CheckResult>();
        public SkinSource Source;
        public string PackageKey, SourcePath;
        public bool CanInstall => Checks.All(c => c.Passed);
    }

    public sealed class StagePlan
    {
        public string Name;
        public StageFile Target;
        public byte[] Bytes;
        public SkinSource Source;
        public string SourcePath, PackageKey;
        public string Kind;   // null: stage, "song" (replaces a P+ song), "song-new" (added to a stage's song list)
        public string TrackList, SongTitle;   // "song-new"
        public Dictionary<string, byte[]> SelectPictures = new Dictionary<string, byte[]>();   // "preview", "icon", "name" (PNG) for the stage select
        public StageCheck.Fit Fit;                 // fixed stages: how far its looks are from P+'s floors
        public List<string> LooksSwapped = new List<string>();   // fixed stages: its models replaced by P+'s
        public StageCheck.LooksResult Looks;       // looks-only: the map's scenery on P+'s own stage file
        public byte[] LooksMap;                    // looks-only: the map itself (kept, to do it again on a newer P+)
        public List<CheckResult> Checks = new List<CheckResult>();
        public bool CanInstall => Checks.All(c => c.Passed);
    }

    public sealed class Installer
    {
        /// <summary>Largest select-screen portrait file P+ ships (MenSelchrFaceB360.brres).</summary>
        public const long MaxPortraitFileBytes = 317824;
        /// <summary>P+'s stock icon file plus a small margin for the copies color smashing needs.</summary>
        public const long MaxStockFileBytes = 346240 + 64 * 1024;

        readonly BrawlOnlineInstall _install;
        readonly Library _lib;
        readonly Dictionary<int, int[]> _hurtboxes = new Dictionary<int, int[]>();
        readonly Dictionary<string, SkinCheck.Skeleton> _skeletons = new Dictionary<string, SkinCheck.Skeleton>();
        List<InstalledSkin> _skinsAfter;
        Dictionary<string, string> _written;

        public Installer(BrawlOnlineInstall install, Library lib) { _install = install; _lib = lib; Quiet(); }

        /// <summary>Where the card from before each save is kept (for "Undo last save"); null keeps none.</summary>
        public LastSave Undo { get; set; }
        /// <summary>Edits the card file itself, with no safety copy: only for a throwaway copy (making the clean card).</summary>
        public bool WorkInPlace { get; set; }
        /// <summary>Which card is in place (normal or big), for the undo point.</summary>
        public string CardName { get; set; } = Cards.Normal;
        /// <summary>Set after a save when no undo point could be kept.</summary>
        public string UndoNote { get; private set; }

        /// <summary>BrawlLib shows its own progress windows while compressing; the app shows its own instead.</summary>
        public static void Quiet()
        {
            try { BrawlLib.Properties.Settings.Default["ShowProgressBars"] = false; } catch { }
            try { BrawlLib.Properties.Settings.Default["HideMDL0Errors"] = true; } catch { }
        }

        public Fat32Image OpenCard() => new Fat32Image(_install.CardPath, false);

        // ---------- planning ----------

        /// <summary>The costume P+ itself has in this slot (from the records if a skin is there now).</summary>
        public byte[] OriginalCostume(Fat32Image card, Character c, CostumeSlot slot, out string from)
        {
            string path = slot.CostumePath(c);
            if (_lib.HasOriginalFile(path))
            {
                var o = _lib.OriginalFile(path);
                if (o != null) { from = "P+'s own costume for this slot"; return o; }
            }
            else if (card.Exists(path)) { from = "P+'s own costume for this slot"; return card.ReadFile(path); }
            var disc = DiscCostumes.Read(_install, c, slot.FileNumber, _lib.DiscCacheFolder);
            from = "the Brawl disc's costume for this slot";
            return disc;
        }

        SkinCheck.Skeleton SkeletonOf(string key, Func<byte[]> bytes)
        {
            if (_skeletons.TryGetValue(key, out var s)) return s;
            var b = bytes();
            return _skeletons[key] = b == null ? null : SkinCheck.ReadSkeleton(b);
        }

        int[] HurtboxBones(Fat32Image card, Character c)
        {
            if (!_hurtboxes.TryGetValue(c.SlotListId, out var hb))
            {
                try { hb = SkinCheck.HurtboxBones(card.ReadFile($"{c.FighterFolderPath}/Fit{c.FilePrefix}.pac")); } catch { hb = null; }
                _hurtboxes[c.SlotListId] = hb;
            }
            return hb;
        }

        readonly Dictionary<int, long> _budgets = new Dictionary<int, long>();

        /// <summary>The most a costume of this character may unpack to. The game unpacks the costume into one block of
        /// memory per player (5.33 MB) next to the character's moves and animations, and builds the 3D model in what's
        /// left; with too little left the match crashes as it starts. Measured on Brawl Online: games with 268 KB or
        /// more left loaded, 205 KB and less crashed (Lucario's own Brawl costume leaves 239 KB). So a costume may fill
        /// the block up to 240 KB from the top, and never has to be smaller than the biggest costume the game itself uses
        /// for that character.</summary>
        public long MemoryBudget(Fat32Image card, Character c, CostumeSlot slot)
        {
            if (!_budgets.TryGetValue(c.SlotListId, out long shipped))
            {
                shipped = 0;
                foreach (var s in PPlusCard.ReadSlots(card, c))
                {
                    string path = s.CostumePath(c);
                    try
                    {
                        byte[] b = null;
                        if (_lib.HasOriginalFile(path)) b = _lib.OriginalFile(path);
                        else if (card.Exists(path)) b = card.ReadFile(path);
                        if (b == null)
                        {
                            string disc = Path.Combine(_lib.DiscCacheFolder, $"Fit{c.FilePrefix}{s.FileNumber:00}.pac");
                            if (File.Exists(disc)) b = File.ReadAllBytes(disc);
                        }
                        if (b != null) shipped = Math.Max(shipped, CostumeMemory.Unpacked(b));
                    }
                    catch { }
                }
                _budgets[c.SlotListId] = shipped;
            }
            long own = 0;
            try { var o = OriginalCostume(card, c, slot, out _); if (o != null) own = CostumeMemory.Unpacked(o); } catch { }
            long resident = Resident(card, c, slot);
            long room = resident > 0 ? CostumeMemory.FighterBlock - CostumeMemory.Headroom - resident : 0;
            return Math.Max(Math.Max(shipped, own), room);
        }

        /// <summary>What else the game keeps in the character's memory block: its moves and animations (and the
        /// costume's extra file, for characters that have one).</summary>
        long Resident(Fat32Image card, Character c, CostumeSlot slot)
        {
            long Size(string name)
            {
                string path = c.FighterFolderPath + "/" + name;
                try
                {
                    if (_lib.HasOriginalFile(path)) return CostumeMemory.Unpacked(_lib.OriginalFile(path));
                    return card.Exists(path) ? CostumeMemory.Unpacked(card.ReadFile(path)) : -1;
                }
                catch { return -1; }
            }
            long motionEtc = Size($"Fit{c.FilePrefix}MotionEtc.pac");
            if (motionEtc > 0) return motionEtc;
            long motion = Size($"Fit{c.FilePrefix}Motion.pac");
            if (motion <= 0) return 0;
            long etc = Size($"Fit{c.FilePrefix}Etc{slot.FileNumber:00}.pac");
            if (etc < 0) etc = Size($"Fit{c.FilePrefix}Etc.pac");
            return motion + Math.Max(0, etc);
        }

        /// <summary>Checks that a costume fits in the game's memory, making it smaller if it doesn't. Returns the
        /// bytes to use (the same ones when it already fits) and the check to show.</summary>
        public byte[] FitMemory(Fat32Image card, Character c, CostumeSlot slot, string fileName, byte[] costume, out CheckResult check)
        {
            long budget = MemoryBudget(card, c, slot), size = CostumeMemory.Unpacked(costume);
            if (budget <= 0 || size <= budget)
            {
                check = new CheckResult { Passed = true, Title = "Fits in the game's memory",
                    Detail = budget <= 0 ? "There was nothing of " + c.Name + "'s to compare with." : $"{fileName} unpacks to {CostumeMemory.Kb(size)} in the game; up to {CostumeMemory.Kb(budget)} fits for {c.Name}." };
                return costume;
            }
            CostumeMemory.Shrunk r;
            try { r = CostumeMemory.Shrink(costume, budget); }
            catch { r = new CostumeMemory.Shrunk { Before = size, After = size }; }
            if (r.Bytes == null)
            {
                check = new CheckResult { Passed = false, Title = "Too big for the game's memory",
                    Detail = $"{fileName} unpacks to {CostumeMemory.Kb(size)} in the game, but only {CostumeMemory.Kb(budget)} fits for {c.Name}. " +
                             "A costume this big crashes the game as the match starts, and it couldn't be made small enough." };
                return null;
            }
            check = new CheckResult { Passed = true, Title = "Made smaller to fit the game's memory",
                Detail = $"{fileName} unpacked to {CostumeMemory.Kb(size)}, but only {CostumeMemory.Kb(budget)} fits for {c.Name}: that crashes the game as the match starts. " +
                         $"Only its pictures were changed (the model is untouched) and it now unpacks to {CostumeMemory.Kb(r.After)}: " + string.Join("; ", r.Pictures) + "." };
            return r.Bytes;
        }

        /// <summary>A skin's main costume file as it came (kept beside the copy when the copy was made smaller).</summary>
        static string OriginalCopy(string copy) => copy + ".original";

        (CostumeSlot Slot, InstalledFile File, string Copy) MainCopy(Fat32Image card, InstalledSkin s)
        {
            var c = s.Character;
            var slot = PPlusCard.ReadSlots(card, c).FirstOrDefault(x => x.Index == s.SlotIndex);
            if (slot == null) return (null, null, null);
            var f = s.Files.FirstOrDefault(x => string.Equals(x.CardPath, slot.CostumePath(c), StringComparison.OrdinalIgnoreCase));
            if (f == null) return (slot, null, null);
            string copy = Path.Combine(_lib.SkinFolder(s.Id), Path.GetFileName(f.CardPath));
            return File.Exists(copy) ? (slot, f, copy) : (slot, f, null);
        }

        /// <summary>Costumes that are on and need fitting: too big for the game's memory (they crash the match), or made
        /// smaller earlier when the way as it came fits after all (they go back to how they came).</summary>
        public List<InstalledSkin> TooBig(Fat32Image card) => NeedsRefit(card).Where(x => x.Value).Select(x => x.Key).ToList();
        public List<InstalledSkin> ShrunkForNothing(Fat32Image card) => NeedsRefit(card).Where(x => !x.Value).Select(x => x.Key).ToList();

        List<KeyValuePair<InstalledSkin, bool>> NeedsRefit(Fat32Image card)
        {
            var result = new List<KeyValuePair<InstalledSkin, bool>>();
            foreach (var s in _lib.Skins.Where(x => x.Enabled))
            {
                try
                {
                    var (slot, f, copy) = MainCopy(card, s);
                    if (copy == null) continue;
                    long budget = MemoryBudget(card, s.Character, slot);
                    if (budget <= 0) continue;
                    long now = CostumeMemory.Unpacked(Head(copy));
                    string orig = OriginalCopy(copy);
                    if (now > budget) result.Add(new KeyValuePair<InstalledSkin, bool>(s, true));
                    else if (File.Exists(orig) && CostumeMemory.Unpacked(Head(orig)) <= budget) result.Add(new KeyValuePair<InstalledSkin, bool>(s, false));
                }
                catch { }
            }
            return result;
        }

        static byte[] Head(string file)
        {
            var head = new byte[8];
            using (var fs = File.OpenRead(file)) fs.Read(head, 0, 8);
            return head;
        }

        /// <summary>Fits a skin's saved costume to the game's memory, starting from the costume as it came: made smaller
        /// if needed, or put back as it came when that fits. Returns the card paths it changed.</summary>
        HashSet<string> RefitCopy(Fat32Image card, InstalledSkin skin)
        {
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var (slot, f, copy) = MainCopy(card, skin);
            if (copy == null) return changed;
            string orig = OriginalCopy(copy);
            var source = File.ReadAllBytes(File.Exists(orig) ? orig : copy);
            var fitted = FitMemory(card, skin.Character, slot, Path.GetFileName(f.CardPath), source, out var check);
            if (fitted == null) throw new InvalidOperationException(check.Detail);
            if (!ReferenceEquals(fitted, source) && !File.Exists(orig)) File.WriteAllBytes(orig, source);   // keep it as it came
            string sha = Library.Sha256(fitted);
            if (sha == f.Sha256) return changed;
            File.WriteAllBytes(copy, fitted);
            if (ReferenceEquals(fitted, source) && File.Exists(orig)) File.Delete(orig);
            f.Sha256 = sha;
            changed.Add(f.CardPath);
            Notes.Add(ReferenceEquals(fitted, source)
                ? $"{skin.Name} is back exactly as it came (it fits in the game's memory after all)."
                : $"{skin.Name} was made smaller to fit the game's memory (only its pictures; it crashed the match before).");
            return changed;
        }

        /// <summary>Whether a skin's model fits a slot (its gameplay bones match that slot's original).</summary>
        public CheckResult SkeletonFits(Fat32Image card, SkinCheck.Skeleton skin, Character c, CostumeSlot slot)
        {
            string from = null;
            var original = SkeletonOf($"{c.SlotListId}:{slot.FileNumber}", () => OriginalCostume(card, c, slot, out from));
            if (original != null)
                return SkinCheck.SameSkeleton(skin, new[] { original }, HurtboxBones(card, c), c.Name, from ?? "P+'s own costume for this slot");
            // The disc could not be read: compare with every costume of this character on the card.
            var others = PPlusCard.ReadSlots(card, c).Where(s => s.OnCard || _lib.HasOriginalFile(s.CostumePath(c)))
                .Select(s => SkeletonOf($"{c.SlotListId}:{s.FileNumber}", () => OriginalCostume(card, c, s, out _))).Where(x => x != null).ToList();
            var r = SkinCheck.SameSkeleton(skin, others, HurtboxBones(card, c), c.Name, $"P+'s other {c.Name} costumes");
            r.Detail += " (This slot's own costume is on the Brawl disc, which could not be read, so P+'s other costumes were used.)";
            return r;
        }

        public SkinPlan Plan(Fat32Image card, SkinPackage pkg, PackageCostume costume, CostumeSlot slot, Dictionary<PictureKind, string> pictureFiles)
        {
            var c = costume.Character;
            string which = pkg.Costumes.Count <= 1 ? "" : costume.Variant != "" ? $" ({costume.Variant})" : $" #{costume.NumberText}";
            var plan = new SkinPlan { Name = pkg.Name + which, Character = c, Slot = slot, Source = pkg.Source, PackageKey = costume.Key, SourcePath = pkg.SourcePath };
            foreach (var f in costume.Files)
            {
                byte[] bytes;
                try { bytes = SkinCheck.Prepared(f.Path, f.Kind); }
                catch (Exception e)
                {
                    plan.Checks.Add(new CheckResult { Passed = false, Title = "Could not read " + Path.GetFileName(f.Path), Detail = e.Message });
                    continue;
                }
                string target = f.TargetFolder(c) + "/" + f.TargetName(c, slot.FileNumber);
                plan.Files.Add(new KeyValuePair<string, byte[]>(target, bytes));
                plan.Checks.Add(SkinCheck.Size(Path.GetFileName(target), bytes.Length, SkinCheck.LimitFor(f.Kind)));
            }
            var main = plan.Files.FirstOrDefault(kv => Path.GetFileName(kv.Key).Equals($"Fit{c.FilePrefix}{slot.FileNumber:00}.pac", StringComparison.OrdinalIgnoreCase));
            if (main.Value != null) plan.Checks.Add(SkeletonFits(card, SkinCheck.ReadSkeleton(main.Value), c, slot));
            if (main.Value != null)
            {
                var fitted = FitMemory(card, c, slot, Path.GetFileName(main.Key), main.Value, out var memCheck);
                plan.Checks.Add(memCheck);
                if (fitted != null && !ReferenceEquals(fitted, main.Value))
                {
                    int at = plan.Files.FindIndex(kv => kv.Key == main.Key);
                    plan.Files[at] = new KeyValuePair<string, byte[]>(main.Key, fitted);
                    plan.Originals[main.Key] = main.Value;
                    var sizeCheck = plan.Checks.FirstOrDefault(x => x.Title == "Too big for online play" && x.Detail.StartsWith(Path.GetFileName(main.Key)));
                    if (sizeCheck != null) plan.Checks[plan.Checks.IndexOf(sizeCheck)] = SkinCheck.Size(Path.GetFileName(main.Key), fitted.Length, SkinCheck.LimitFor(""));
                }
            }
            foreach (var kv in pictureFiles.Where(p => p.Value != null))
            {
                try { using (var img = Image.FromFile(kv.Value)) plan.Pictures[kv.Key] = new Bitmap(img); }
                catch (Exception e) { plan.Checks.Add(new CheckResult { Passed = false, Title = "Could not read the " + Cosmetics.Describe(kv.Key), Detail = e.Message }); }
            }
            return plan;
        }

        // ---------- applying ----------

        public sealed class Change
        {
            public List<SkinPlan> Install = new List<SkinPlan>();
            public List<InstalledSkin> Remove = new List<InstalledSkin>();
            public List<InstalledSkin> Reapply = new List<InstalledSkin>();
            public List<InstalledSkin> Refit = new List<InstalledSkin>();      // too big for the game's memory: make smaller
            public List<InstalledSkin> Enable = new List<InstalledSkin>();    // turn back on (kept in My Mods)
            public List<InstalledSkin> Disable = new List<InstalledSkin>();   // turn off, keep in My Mods
            public List<StagePlan> StageInstall = new List<StagePlan>();
            public List<InstalledStage> StageRemove = new List<InstalledStage>();
            public List<InstalledStage> StageEnable = new List<InstalledStage>();
            public List<InstalledStage> StageDisable = new List<InstalledStage>();
            public List<InstalledStage> StageReapply = new List<InstalledStage>();
            public bool IsEmpty => Install.Count + Remove.Count + Reapply.Count + Refit.Count + Enable.Count + Disable.Count
                + StageInstall.Count + StageRemove.Count + StageEnable.Count + StageDisable.Count + StageReapply.Count == 0;
        }

        /// <summary>What the last Apply had to do differently (a mod re-fixed for a P+ update, or kept off), in plain words.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Remembers what's on the card at this path as P+'s file, unless it's one of ours
        /// (P+ may have updated it since we saved the original).</summary>
        void RefreshOriginal(Fat32Image card, string path)
        {
            byte[] now = card.Exists(path) ? card.ReadFile(path) : null;
            string sha = now == null ? null : Library.Sha256(now);
            bool ours = sha != null && (_lib.Skins.Any(k => k.Files.Any(f => string.Equals(f.CardPath, path, StringComparison.OrdinalIgnoreCase) && f.Sha256 == sha)) ||
                                        _lib.Stages.Any(k => string.Equals(k.CardPath, path, StringComparison.OrdinalIgnoreCase) && k.Sha256 == sha));
            if (!ours) _lib.SaveOriginalFile(path, now, replace: true);
        }

        /// <summary>Does a skin still fit P+'s current costume for its slot? (P+ updates could change a skeleton.)</summary>
        bool SkinStillFits(Fat32Image card, InstalledSkin skin, out string why)
        {
            why = null;
            var c = skin.Character;
            var slot = PPlusCard.ReadSlots(card, c).FirstOrDefault(x => x.Index == skin.SlotIndex);
            if (slot == null) { why = $"P+ no longer has {c.Name}'s costume {skin.SlotIndex + 1}"; return false; }
            string main = slot.CostumePath(c);
            var f = skin.Files.FirstOrDefault(x => string.Equals(x.CardPath, main, StringComparison.OrdinalIgnoreCase));
            if (f == null) return true;
            string copy = Path.Combine(_lib.SkinFolder(skin.Id), Path.GetFileName(f.CardPath));
            if (!File.Exists(copy)) return true;   // EnableSkin explains a missing copy
            RefreshOriginal(card, main);
            _skeletons.Remove($"{c.SlotListId}:{slot.FileNumber}");
            var r = SkeletonFits(card, SkinCheck.ReadSkeleton(File.ReadAllBytes(copy)), c, slot);
            if (!r.Passed) why = $"P+ changed {c.Name}'s costume {skin.SlotIndex + 1}, and the skin's bones no longer fit it";
            return r.Passed;
        }

        byte[] LooksSource(InstalledStage s)
        {
            string f = Path.Combine(_lib.StageFolder(s.Id), "looks-map.pac");
            return File.Exists(f) ? File.ReadAllBytes(f) : null;
        }

        /// <summary>The stage file to put on the card now: the saved copy if it still plays exactly like P+'s current
        /// stage, else the saved copy re-fixed against it (P+ changed the stage since), else null with the reason.</summary>
        byte[] StageForNow(Fat32Image card, InstalledStage s, out string why)
        {
            why = null;
            var bytes = StageCopy(s);
            if (s.Kind != null) return bytes;   // songs: no gameplay
            RefreshOriginal(card, s.CardPath);
            var original = _lib.OriginalFile(s.CardPath);
            if (original == null) { why = $"P+ no longer has {s.Target.Label}"; return null; }
            var theirs = _stageIndex.Of(card, _lib, s.CardPath);
            var program = _stageIndex.ProgramNames(card, s.CardPath);
            if (StageCheck.Compare(StageCheck.Gameplay(bytes), theirs, "", program).Passed) return bytes;
            if (s.LooksOnly)
            {
                // its looks on P+'s stage: made again on P+'s current file
                var src = LooksSource(s);
                var redone = src == null ? new StageCheck.LooksResult() : StageCheck.SwapLooks(src, original, program);
                if (redone.Bytes != null && StageCheck.Compare(StageCheck.Gameplay(redone.Bytes), theirs, "", program).Passed)
                {
                    File.WriteAllBytes(Path.Combine(_lib.StageFolder(s.Id), Path.GetFileName(s.CardPath)), redone.Bytes);
                    s.Sha256 = Library.Sha256(redone.Bytes);
                    return redone.Bytes;
                }
            }
            var made = StageCheck.MakeMatch(bytes, original, out string problem, out _, program);
            if (made != null && StageCheck.Compare(StageCheck.Gameplay(made), theirs, "", program).Passed)
            {
                File.WriteAllBytes(Path.Combine(_lib.StageFolder(s.Id), Path.GetFileName(s.CardPath)), made);
                s.Sha256 = Library.Sha256(made);
                Notes.Add($"P+ changed {s.Target.Label}, so {s.Name} was fixed again to play like P+'s new version.");
                return made;
            }
            why = $"{s.Name} doesn't play exactly like P+'s {s.Target.Label}, so it would make online games go out of sync" + (problem != null ? " (" + problem + ")" : "") +
                  ". Add it again from My Mods > \"Add more from this download\" to put just its looks on P+'s stage.";
            return null;
        }

        public void Apply(Change change, Action<string, int> progress)
        {
            Notes.Clear(); UndoNote = null;
            if (_install.IsRunning(out string what))
                throw new InvalidOperationException($"Close {what} first. It uses the SD card, so the card can't be changed while it is open.");

            string cardPath = _install.CardPath, newPath = WorkInPlace ? cardPath : cardPath + ".skinmanager-new", backupPath = cardPath + ".skinmanager-backup";
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cardPath)));
            long cardSize = new FileInfo(cardPath).Length;
            if (!WorkInPlace && drive.AvailableFreeSpace < cardSize + 64L * 1024 * 1024)
                throw new IOException($"Drive {drive.Name} needs {cardSize / (1024 * 1024) + 64} MB free to make a safe copy of the SD card.");

            LastSave.RemoveOldBackup(cardPath);   // older versions kept a 2 GB copy here, on the card's drive
            try { Undo?.Begin(_lib); } catch { }
            var skins = _lib.Skins.Select(s => s.Copy()).ToList();      // committed only after the swap
            var stages = _lib.Stages.Select(s => s.Copy()).ToList();
            var newStageCopies = new List<KeyValuePair<string, StagePlan>>();
            InstalledSkin Mine(InstalledSkin s) => skins.FirstOrDefault(k => k.Id == s.Id);
            _skinsAfter = skins;
            _written = new Dictionary<string, string>(_lib.Written, StringComparer.OrdinalIgnoreCase);
            var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);  // path -> bytes (null = deleted)
            var newSkinFolders = new List<KeyValuePair<string, SkinPlan>>();
            try
            {
                if (!WorkInPlace) CopyWithProgress(cardPath, newPath, p => progress?.Invoke("Making a safe copy of the SD card", p));
                using (var card = new Fat32Image(newPath, true))
                {
                    int step = 0, steps = change.Remove.Count + change.Install.Count + change.Reapply.Count + change.Refit.Count + change.Enable.Count + change.Disable.Count;
                    foreach (var asked in change.Disable)
                    {
                        var skin = Mine(asked);
                        if (skin == null || skin.Disabled) continue;
                        progress?.Invoke($"Turning off {skin.Name}", 100 * step++ / Math.Max(1, steps));
                        Restore(card, skin, expected);
                        skin.Disabled = true;
                    }
                    foreach (var asked in change.Remove)
                    {
                        var skin = Mine(asked);
                        if (skin == null) continue;
                        progress?.Invoke($"Removing {skin.Name}", 100 * step++ / Math.Max(1, steps));
                        if (skin.Enabled) Restore(card, skin, expected);
                        skins.Remove(skin);
                    }
                    foreach (var asked in change.Reapply)
                    {
                        var skin = Mine(asked);
                        if (skin == null || skin.Disabled) continue;
                        progress?.Invoke($"Putting {skin.Name} back", 100 * step++ / Math.Max(1, steps));
                        Reapply(card, skin, expected);
                    }
                    foreach (var asked in change.Refit)
                    {
                        var skin = Mine(asked);
                        if (skin == null || skin.Disabled) continue;
                        progress?.Invoke($"Making {skin.Name} fit", 100 * step++ / Math.Max(1, steps));
                        try
                        {
                            foreach (var path in RefitCopy(card, skin))
                            {
                                var mine = File.ReadAllBytes(Path.Combine(_lib.SkinFolder(skin.Id), Path.GetFileName(path)));
                                card.WriteFile(path, mine);
                                expected[path] = mine;
                            }
                        }
                        catch (Exception e)
                        {
                            Restore(card, skin, expected);
                            skin.Disabled = true;
                            Notes.Add($"{skin.Name} was turned off: {e.Message}");
                        }
                    }
                    foreach (var asked in change.Enable)
                    {
                        var skin = Mine(asked);
                        if (skin == null || skin.Enabled) continue;
                        progress?.Invoke($"Turning on {skin.Name}", 100 * step++ / Math.Max(1, steps));
                        foreach (var other in skins.Where(s => s.Enabled && s.Id != skin.Id && s.SlotListId == skin.SlotListId && s.SlotIndex == skin.SlotIndex).ToList())
                        {
                            Restore(card, other, expected);      // one skin per slot: the one that was on turns off
                            other.Disabled = true;
                        }
                        if (!SkinStillFits(card, skin, out string why))
                        {
                            Notes.Add($"{skin.Name} stayed off: {why}. Online that could make games go out of sync.");
                            continue;
                        }
                        try { RefitCopy(card, skin); }
                        catch (Exception e) { Notes.Add($"{skin.Name} stayed off: {e.Message}"); continue; }
                        EnableSkin(card, skin, expected);
                        skin.Disabled = false;
                    }
                    foreach (var plan in change.Install)
                    {
                        progress?.Invoke($"Installing {plan.Name}", 100 * step++ / Math.Max(1, steps));
                        if (!plan.CanInstall) throw new InvalidOperationException(plan.Name + " did not pass the safety checks.");
                        // the skin that was on in this slot turns off and stays in My Mods
                        foreach (var old in skins.Where(s => s.Enabled && s.SlotListId == plan.Character.SlotListId && s.SlotIndex == plan.Slot.Index).ToList())
                        { Restore(card, old, expected); old.Disabled = true; }
                        var record = Install(card, plan, expected);
                        skins.Add(record);
                        newSkinFolders.Add(new KeyValuePair<string, SkinPlan>(record.Id, plan));
                    }
                    ApplyStages(card, change, stages, newStageCopies, expected, progress);
                    if (card.FreeBytes < PPlusCard.MinFreeBytes)
                        throw new IOException($"The SD card would have only {card.FreeBytes / (1024 * 1024)} MB free. P+ needs about {PPlusCard.MinFreeBytes / (1024 * 1024)} MB to save replays and settings.");
                }

                progress?.Invoke("Checking the new SD card", 95);
                using (var check = new Fat32Image(newPath, false))
                {
                    foreach (var kv in expected)
                    {
                        if (kv.Value == null) { if (check.Exists(kv.Key)) throw new IOException("Check failed: " + kv.Key + " is still on the card."); }
                        else if (!check.ReadFile(kv.Key).SequenceEqual(kv.Value)) throw new IOException("Check failed: " + kv.Key + " did not read back the same.");
                    }
                    check.ListFolder(Build.Pf + "/fighter");
                }

                progress?.Invoke("Swapping in the new SD card", 98);
                if (!WorkInPlace) File.Replace(newPath, cardPath, backupPath);
            }
            catch
            {
                if (!WorkInPlace) try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                Undo?.Cancel();
                throw;
            }

            // The new card is in place: from here on the card HAS changed. Nothing below may fail as if it hadn't: copies of
            // the new mods are kept if they can be (a note says so if not), the list of mods is written (if that fails the
            // message says the card was saved), and only then are the old mods' folders removed.
            foreach (var kv in newSkinFolders)
                try { SaveSkinCopy(kv.Key, kv.Value); }
                catch (Exception e) { Notes.Add($"{kv.Value.Name} is on your SD card, but the app couldn't keep its own copy of it ({Friendly.Explain(e)}). If a P+ update ever removes it, add it again."); }
            foreach (var kv in newStageCopies)
                try
                {
                    Directory.CreateDirectory(_lib.StageFolder(kv.Key));
                    File.WriteAllBytes(Path.Combine(_lib.StageFolder(kv.Key), kv.Value.Target.FileName), kv.Value.Bytes);
                    foreach (var pic in kv.Value.SelectPictures) File.WriteAllBytes(Path.Combine(_lib.StageFolder(kv.Key), "select-" + pic.Key + ".png"), pic.Value);
                    if (kv.Value.LooksMap != null) File.WriteAllBytes(Path.Combine(_lib.StageFolder(kv.Key), "looks-map.pac"), kv.Value.LooksMap);
                }
                catch (Exception e) { Notes.Add($"{kv.Value.Name} is on your SD card, but the app couldn't keep its own copy of it ({Friendly.Explain(e)}). If a P+ update ever removes it, add it again."); }
            _newSelectPictures.Clear();
            var goneSkins = _lib.Skins.Where(s => !skins.Any(k => k.Id == s.Id)).ToList();
            var goneStages = _lib.Stages.Where(s => !stages.Any(k => k.Id == s.Id)).ToList();
            _lib.Skins.Clear(); _lib.Skins.AddRange(skins);
            _lib.Stages.Clear(); _lib.Stages.AddRange(stages);
            _lib.Written.Clear(); foreach (var kv in _written) _lib.Written[kv.Key] = kv.Value;
            Exception listFailed = null;
            try { _lib.Save(); } catch (Exception e) { listFailed = e; }
            if (listFailed == null)
            {
                foreach (var removed in goneSkins) try { Directory.Delete(_lib.SkinFolder(removed.Id), true); } catch { }
                foreach (var removed in goneStages) try { Directory.Delete(_lib.StageFolder(removed.Id), true); } catch { }
            }
            // the card from before this save goes to the app's folder (one copy, for "Undo last save")
            try
            {
                if (WorkInPlace) { }
                else if (Undo == null || !Undo.Finish(backupPath, cardPath, CardName, Describe(change), p => progress?.Invoke("Keeping the old SD card for \"Undo last save\"", p)))
                {
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                    if (Undo != null) UndoNote = "There wasn't room next to the app to keep the old SD card, so this save can't be undone.";
                }
            }
            catch (Exception e) { UndoNote = $"The old SD card couldn't be kept for \"Undo last save\" ({Friendly.Explain(e)}), so this save can't be undone."; }
            if (listFailed != null)
                throw new SavedButException("Your SD card was saved, but the app couldn't write down which mods are on it (" + Friendly.Explain(listFailed) +
                                            "). Close and reopen the app: if your mods look wrong after that, report it from Settings > Report a problem.", listFailed);
            progress?.Invoke("Done", 100);
        }

        static string Describe(Change c)
        {
            var parts = new List<string>();
            int on = c.Install.Count + c.Enable.Count + c.StageInstall.Count + c.StageEnable.Count,
                off = c.Disable.Count + c.Remove.Count + c.StageDisable.Count + c.StageRemove.Count;
            if (on > 0) parts.Add(on + " turned on");
            if (off > 0) parts.Add(off + " turned off");
            return parts.Count == 0 ? "changes" : string.Join(", ", parts);
        }

        InstalledSkin Install(Fat32Image card, SkinPlan plan, Dictionary<string, byte[]> expected)
        {
            var c = plan.Character;
            var record = new InstalledSkin
            {
                Id = Guid.NewGuid().ToString("N"), Name = plan.Name, SlotListId = c.SlotListId,
                SlotIndex = plan.Slot.Index, FileNumber = plan.Slot.FileNumber, InstalledAt = DateTime.Now,
                GameBananaModId = plan.Source?.ModId ?? 0, GameBananaFileId = plan.Source?.FileId ?? 0,
                Author = plan.Source?.Author, PageUrl = plan.Source?.PageUrl, ThumbUrl = plan.Source?.ThumbUrl,
                PackageKey = plan.PackageKey, SourcePath = plan.SourcePath,
            };
            foreach (var kv in plan.Files)
            {
                // what's there now is kept, to put back when this skin goes off: P+'s file, or a costume put on by hand
                RefreshOriginal(card, kv.Key);
                card.WriteFile(kv.Key, kv.Value);
                expected[kv.Key] = kv.Value;
                record.Files.Add(new InstalledFile { CardPath = kv.Key, Sha256 = Library.Sha256(kv.Value) });
            }
            foreach (var kv in plan.Pictures)
                record.Pictures.Add(PutPicture(card, c, plan.Slot.Index, kv.Key, kv.Value, expected, saveOriginal: true));
            return record;
        }

        InstalledPicture PutPicture(Fat32Image card, Character c, int slot, PictureKind kind, Bitmap picture, Dictionary<string, byte[]> expected, bool saveOriginal)
        {
            string file, texture;
            switch (kind)
            {
                case PictureKind.SelectPortrait: file = c.CspFilePath; texture = c.CspTextureName(slot); break;
                case PictureKind.BattlePortrait: file = c.BattlePortraitPath(slot); texture = c.BattlePortraitTextureName(slot); break;
                default: file = Character.StockFilePath; texture = c.StockTextureName(slot); break;
            }
            var before = card.ReadFile(file);
            if (saveOriginal && !_lib.HasOriginalFile(file) && !_written.ContainsKey(file)) _lib.SaveOriginalFile(file, before);
            if (saveOriginal && !_lib.HasOriginalPicture(file, texture))
            {
                using (var orig = Cosmetics.ReadPicture(before, texture))
                    if (orig != null) _lib.SaveOriginalPicture(file, texture, orig);
            }
            var after = Cosmetics.ReplacePicture(before, texture, picture);
            long limit = kind == PictureKind.SelectPortrait ? MaxPortraitFileBytes : kind == PictureKind.StockIcon ? MaxStockFileBytes : long.MaxValue;
            if (after.Length > limit && after.Length > before.Length)
                throw new IOException($"{c.Name}'s {Cosmetics.Describe(kind)}s would no longer fit in the space P+ gives them ({after.Length / 1024} KB, P+'s largest is {limit / 1024} KB). Remove another {c.Name} skin's picture first.");
            card.WriteFile(file, after);
            expected[file] = after;
            _written[file] = Library.Sha256(after);
            string hash;
            using (var shown = Cosmetics.ReadPicture(after, texture)) hash = PixelHash(shown);
            return new InstalledPicture { Kind = kind, CardPath = file, TextureName = texture, PixelHash = hash };
        }

        void Restore(Fat32Image card, InstalledSkin skin, Dictionary<string, byte[]> expected)
        {
            foreach (var f in skin.Files)
            {
                var orig = _lib.OriginalFile(f.CardPath);
                if (orig == null) { card.DeleteFile(f.CardPath); expected[f.CardPath] = null; }
                else { card.WriteFile(f.CardPath, orig); expected[f.CardPath] = orig; }
            }
            foreach (var group in skin.Pictures.GroupBy(p => p.CardPath, StringComparer.OrdinalIgnoreCase))
            {
                string file = group.Key;
                var current = card.ReadFile(file);
                bool othersUseIt = _skinsAfter.Any(s => s.Id != skin.Id && s.Enabled && s.Pictures.Any(p => string.Equals(p.CardPath, file, StringComparison.OrdinalIgnoreCase)));
                bool untouchedSince = _written.TryGetValue(file, out var sha) && sha == Library.Sha256(current);
                var original = _lib.HasOriginalFile(file) ? _lib.OriginalFile(file) : null;
                if (!othersUseIt && untouchedSince && original != null)
                {
                    // Nothing else of ours is in this file and P+ has not changed it: put P+'s exact file back.
                    card.WriteFile(file, original);
                    expected[file] = original;
                    _written.Remove(file);
                    continue;
                }
                foreach (var p in group)
                {
                    using (var orig = _lib.OriginalPicture(p.CardPath, p.TextureName))
                    {
                        if (orig == null) continue;
                        current = Cosmetics.ReplacePicture(current, p.TextureName, orig);
                    }
                }
                card.WriteFile(file, current);
                expected[file] = current;
                _written[file] = Library.Sha256(current);
            }
        }

        // ---------- after a P+ update ----------

        /// <summary>Skins whose files or pictures on the card are no longer what was installed
        /// (a P+ update writes its own files over them).</summary>
        public List<InstalledSkin> Overwritten(Fat32Image card)
        {
            var result = new List<InstalledSkin>();
            foreach (var s in _lib.Skins.Where(x => x.Enabled))
            {
                bool bad = s.Files.Any(f => !card.Exists(f.CardPath) || Library.Sha256(card.ReadFile(f.CardPath)) != f.Sha256);
                if (!bad)
                    foreach (var p in s.Pictures)
                        using (var shown = Cosmetics.ReadPicture(card.ReadFile(p.CardPath), p.TextureName))
                            if (shown == null || PixelHash(shown) != p.PixelHash) { bad = true; break; }
                if (bad) result.Add(s);
            }
            return result;
        }

        void Reapply(Fat32Image card, InstalledSkin skin, Dictionary<string, byte[]> expected)
        {
            string folder = _lib.SkinFolder(skin.Id);
            var c = skin.Character;
            var changed = new List<InstalledFile>();
            foreach (var f in skin.Files)
            {
                bool present = card.Exists(f.CardPath);
                byte[] now = present ? card.ReadFile(f.CardPath) : null;
                if (now != null && Library.Sha256(now) == f.Sha256) continue;
                _lib.SaveOriginalFile(f.CardPath, now, replace: true);     // P+'s new version is the new original
                changed.Add(f);
            }
            // P+'s new costume: does the skin still fit it?
            if (!SkinStillFits(card, skin, out string why))
            {
                Restore(card, skin, expected);
                skin.Disabled = true;
                Notes.Add($"{skin.Name} was turned off: {why}. Online that could make games go out of sync.");
                return;
            }
            foreach (var f in changed)
            {
                var mine = File.ReadAllBytes(Path.Combine(folder, Path.GetFileName(f.CardPath)));
                card.WriteFile(f.CardPath, mine);
                expected[f.CardPath] = mine;
            }
            var redone = new List<InstalledPicture>();
            foreach (var file in skin.Pictures.Select(p => p.CardPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var now = card.ReadFile(file);
                if (_written.TryGetValue(file, out var sha) && sha == Library.Sha256(now)) continue;
                // P+ wrote this file since: its version is the new original if no other skin of ours is in it.
                bool othersUseIt = _skinsAfter.Any(s => s.Id != skin.Id && s.Enabled && s.Pictures.Any(p => string.Equals(p.CardPath, file, StringComparison.OrdinalIgnoreCase)));
                if (othersUseIt) _lib.ForgetOriginalFile(file); else _lib.SaveOriginalFile(file, now, replace: true);
                _written.Remove(file);
            }
            foreach (var p in skin.Pictures)
            {
                using (var shown = Cosmetics.ReadPicture(card.ReadFile(p.CardPath), p.TextureName))
                {
                    if (shown != null && PixelHash(shown) == p.PixelHash) { redone.Add(p); continue; }
                    if (shown != null) _lib.SaveOriginalPicture(p.CardPath, p.TextureName, shown, replace: true);
                }
                using (var mine = new Bitmap(Path.Combine(folder, p.Kind + ".png")))
                    redone.Add(PutPicture(card, c, skin.SlotIndex, p.Kind, mine, expected, saveOriginal: false));
            }
            skin.Pictures = redone;
        }

        // ---------- stages ----------

        public StagePlan PlanStage(Fat32Image card, SkinPackage pkg, string stageFile, StageFile target, Dictionary<string, string> gameplay)
        {
            var plan = new StagePlan
            {
                Name = pkg.Name + (pkg.StageFiles.Count > 1 ? " (" + Path.GetFileNameWithoutExtension(stageFile) + ")" : ""),
                Target = target, Source = pkg.Source, SourcePath = pkg.SourcePath, PackageKey = StageKey(pkg, stageFile),
            };
            plan.Bytes = File.ReadAllBytes(stageFile);
            plan.Checks.Add(SkinCheck.Size(target.FileName, plan.Bytes.Length, StageCheck.MaxStageBytes));
            var original = _stageIndex.Of(card, _lib, target.CardPath);
            plan.Checks.Add(original == null
                ? new CheckResult { Passed = false, Title = "P+'s " + target.Label + " couldn't be read", Detail = "Without it the app can't compare the two." }
                : StageCheck.Compare(gameplay, original, "P+'s " + target.Label, _stageIndex.ProgramNames(card, target.CardPath)));
            return plan;
        }

        /// <summary>A plan for a stage that doesn't match: the app copies the gameplay parts of P+'s stage
        /// (floors, blast zones, spawns, camera, moving parts) into the reskin, keeps its looks, and checks the result.</summary>
        public StagePlan PlanFixedStage(Fat32Image card, SkinPackage pkg, string stageFile, StageFile target)
        {
            var plan = new StagePlan
            {
                Name = pkg.Name + (pkg.StageFiles.Count > 1 ? " (" + Path.GetFileNameWithoutExtension(stageFile) + ")" : ""),
                Target = target, Source = pkg.Source, SourcePath = pkg.SourcePath, PackageKey = StageKey(pkg, stageFile),
            };
            byte[] originalBytes = _lib.HasOriginalFile(target.CardPath) ? _lib.OriginalFile(target.CardPath) : card.ReadFile(target.CardPath);
            if (originalBytes == null)
            {
                plan.Checks.Add(new CheckResult { Passed = false, Title = "P+'s " + target.Label + " couldn't be read", Detail = "Without it the app can't copy its gameplay." });
                return plan;
            }
            byte[] made;
            string problem;
            byte[] reskin = File.ReadAllBytes(stageFile);
            var program = _stageIndex.ProgramNames(card, target.CardPath);
            made = StageCheck.MakeMatch(reskin, originalBytes, out problem, out plan.LooksSwapped, program);
            if (made != null) plan.Fit = StageCheck.HowFarOff(reskin, originalBytes);
            if (made == null)
            {
                plan.Checks.Add(new CheckResult { Passed = false, Title = "Couldn't make it play like P+'s " + target.Label, Detail = problem });
                return plan;
            }
            plan.Bytes = made;
            plan.Checks.Add(SkinCheck.Size(target.FileName, made.Length, StageCheck.MaxStageBytes));
            Dictionary<string, string> after;
            after = StageCheck.Gameplay(made);
            var original = _stageIndex.Of(card, _lib, target.CardPath);
            var compare = StageCheck.Compare(after, original ?? new Dictionary<string, string>(), "P+'s " + target.Label, program);
            if (compare.Passed) compare.Title = "Now plays exactly like P+'s " + target.Label;
            plan.Checks.Add(compare);
            return plan;
        }

        /// <summary>A plan that keeps P+'s stage file (its stage program, floors, blast zones, moving platforms) and puts
        /// the map's scenery on it: for maps made for another stage program or another layout.</summary>
        public StagePlan PlanLooks(Fat32Image card, SkinPackage pkg, string stageFile, StageFile target)
        {
            var plan = new StagePlan
            {
                Name = pkg.Name + (pkg.StageFiles.Count > 1 ? " (" + Path.GetFileNameWithoutExtension(stageFile) + ")" : ""),
                Target = target, Source = pkg.Source, SourcePath = pkg.SourcePath, PackageKey = StageKey(pkg, stageFile),
            };
            byte[] originalBytes = _lib.HasOriginalFile(target.CardPath) ? _lib.OriginalFile(target.CardPath) : card.ReadFile(target.CardPath);
            if (originalBytes == null)
            {
                plan.Checks.Add(new CheckResult { Passed = false, Title = "P+'s " + target.Label + " couldn't be read", Detail = "The map's looks go on P+'s own file." });
                return plan;
            }
            byte[] map = File.ReadAllBytes(stageFile);
            plan.LooksMap = map;
            var program = _stageIndex.ProgramNames(card, target.CardPath);
            var looks = StageCheck.SwapLooks(map, originalBytes, program);
            plan.Looks = looks;
            if (looks.Bytes == null)
            {
                plan.Checks.Add(new CheckResult { Passed = false, Title = "Couldn't put its looks on P+'s " + target.Label, Detail = looks.Problem });
                return plan;
            }
            plan.Bytes = looks.Bytes;
            plan.Fit = StageCheck.HowFarOff(map, originalBytes);
            plan.Checks.Add(SkinCheck.Size(target.FileName, looks.Bytes.Length, StageCheck.MaxStageBytes));
            var compare = StageCheck.Compare(StageCheck.Gameplay(looks.Bytes), StageCheck.Gameplay(originalBytes), "P+'s " + target.Label, program);
            if (compare.Passed) compare.Title = "Plays exactly like P+'s " + target.Label + " (it is P+'s stage underneath)";
            plan.Checks.Add(compare);
            return plan;
        }

        /// <summary>A plan to replace one of P+'s songs with a .brstm file.</summary>
        public StagePlan PlanSong(SkinPackage pkg, string songFile, SongInfo target)
        {
            var plan = new StagePlan
            {
                Name = Path.GetFileNameWithoutExtension(songFile), Kind = "song",
                Target = new StageFile { CardPath = target.CardPath, StageName = target.Title, Version = "" },
                Source = pkg.Source, SourcePath = pkg.SourcePath, PackageKey = StageKey(pkg, songFile),
                Bytes = File.ReadAllBytes(songFile),
            };
            plan.Checks.Add(Songs.IsBrstm(plan.Bytes)
                ? new CheckResult { Passed = true, Title = "It's a Wii music file (BRSTM)", Detail = "The game can play it." }
                : new CheckResult { Passed = false, Title = "Not a Wii music file", Detail = Path.GetFileName(songFile) + " isn't a BRSTM file, so the game couldn't play it." });
            plan.Checks.Add(SkinCheck.Size(Path.GetFileName(songFile), plan.Bytes.Length, Songs.MaxSongBytes));
            return plan;
        }

        public static string StageKey(SkinPackage pkg, string stageFile)
        {
            string rel = stageFile.StartsWith(pkg.Folder, StringComparison.OrdinalIgnoreCase) ? stageFile.Substring(pkg.Folder.Length) : Path.GetFileName(stageFile);
            return "stage|" + rel.Replace('\\', '/').Trim('/').ToUpperInvariant();
        }

        StageIndex _stageIndexField;
        public StageIndex _stageIndex => _stageIndexField ?? (_stageIndexField = new StageIndex(_lib.Folder));

        void RestoreStage(Fat32Image card, InstalledStage s, Dictionary<string, byte[]> expected)
        {
            var orig = _lib.OriginalFile(s.CardPath);
            if (orig == null) { card.DeleteFile(s.CardPath); expected[s.CardPath] = null; }
            else { card.WriteFile(s.CardPath, orig); expected[s.CardPath] = orig; }
        }

        void PutStage(Fat32Image card, InstalledStage s, byte[] bytes, Dictionary<string, byte[]> expected)
        {
            byte[] now = card.Exists(s.CardPath) ? card.ReadFile(s.CardPath) : null;
            // what's there now is P+'s (it may have been updated while ours was off)
            if (!_lib.HasOriginalFile(s.CardPath) || now == null || Library.Sha256(now) != s.Sha256) _lib.SaveOriginalFile(s.CardPath, now, replace: true);
            card.WriteFile(s.CardPath, bytes);
            expected[s.CardPath] = bytes;
        }

        /// <summary>Stage mods that are on but don't pass today's safety check (it learns more over time, like the parts each
        /// stage program works with directly), with the reason. They'd make online games go out of sync.</summary>
        public List<(InstalledStage Stage, string Why)> UnsafeStages(Fat32Image card)
        {
            var result = new List<(InstalledStage, string)>();
            foreach (var s in _lib.Stages.Where(x => x.Enabled && !x.IsSong).ToList())
            {
                try
                {
                    var original = _lib.OriginalFile(s.CardPath);
                    var mine = StageCopy(s);
                    if (original == null || mine == null) continue;
                    var r = StageCheck.Compare(StageCheck.Gameplay(mine), StageCheck.Gameplay(original), "P+'s " + s.Target.Label, _stageIndex.ProgramNames(card, s.CardPath));
                    if (!r.Passed) result.Add((s, r.Detail));
                }
                catch (Exception e) { Notes.Add("Couldn't recheck " + s.Name + ": " + e.Message); }
            }
            return result;
        }

        byte[] StageCopy(InstalledStage s)
        {
            string p = Path.Combine(_lib.StageFolder(s.Id), Path.GetFileName(s.CardPath));
            if (!File.Exists(p)) throw new FileNotFoundException($"The saved copy of {s.Name} is missing. Delete it from My Mods and add it again.");
            return File.ReadAllBytes(p);
        }

        void ApplyStages(Fat32Image card, Change change, List<InstalledStage> stages, List<KeyValuePair<string, StagePlan>> copies, Dictionary<string, byte[]> expected, Action<string, int> progress)
        {
            InstalledStage Mine(InstalledStage s) => stages.FirstOrDefault(k => k.Id == s.Id);
            foreach (var asked in change.StageDisable)
            {
                var s = Mine(asked); if (s == null || s.Disabled) continue;
                progress?.Invoke($"Turning off {s.Name}", 60);
                RestoreStage(card, s, expected); s.Disabled = true;
            }
            foreach (var asked in change.StageRemove)
            {
                var s = Mine(asked); if (s == null) continue;
                progress?.Invoke($"Removing {s.Name}", 60);
                if (s.Enabled) RestoreStage(card, s, expected);
                stages.Remove(s);
            }
            foreach (var asked in change.StageReapply)
            {
                var s = Mine(asked); if (s == null || s.Disabled) continue;
                progress?.Invoke($"Putting {s.Name} back", 60);
                var bytes = StageForNow(card, s, out string why);
                if (bytes == null)
                {
                    s.Disabled = true;   // the card already has P+'s new version there
                    Notes.Add($"{s.Name} was turned off: {why}.");
                    continue;
                }
                PutStage(card, s, bytes, expected);
            }
            foreach (var asked in change.StageEnable)
            {
                var s = Mine(asked); if (s == null || s.Enabled) continue;
                progress?.Invoke($"Turning on {s.Name}", 60);
                var bytes = StageForNow(card, s, out string why);
                if (bytes == null) { Notes.Add($"{s.Name} stayed off: {why}."); continue; }
                foreach (var other in stages.Where(o => o.Enabled && o.Id != s.Id && string.Equals(o.CardPath, s.CardPath, StringComparison.OrdinalIgnoreCase)).ToList())
                { RestoreStage(card, other, expected); other.Disabled = true; }
                PutStage(card, s, bytes, expected);
                s.Disabled = false;
            }
            foreach (var plan in change.StageInstall)
            {
                progress?.Invoke($"Installing {plan.Name}", 60);
                if (!plan.CanInstall) throw new InvalidOperationException(plan.Name + " did not pass the safety checks.");
                foreach (var other in stages.Where(o => o.Enabled && string.Equals(o.CardPath, plan.Target.CardPath, StringComparison.OrdinalIgnoreCase)).ToList())
                { RestoreStage(card, other, expected); other.Disabled = true; }
                var record = new InstalledStage
                {
                    Id = Guid.NewGuid().ToString("N"), Name = plan.Name, CardPath = plan.Target.CardPath, Sha256 = Library.Sha256(plan.Bytes),
                    InstalledAt = DateTime.Now, GameBananaModId = plan.Source?.ModId ?? 0, GameBananaFileId = plan.Source?.FileId ?? 0,
                    Author = plan.Source?.Author, PageUrl = plan.Source?.PageUrl, ThumbUrl = plan.Source?.ThumbUrl, SourcePath = plan.SourcePath, PackageKey = plan.PackageKey,
                    Kind = plan.Kind, TargetLabel = plan.Kind != null ? plan.Target.StageName : null,
                    TrackList = plan.TrackList, SongTitle = plan.SongTitle, LooksOnly = plan.Looks != null,
                };
                RefreshOriginal(card, record.CardPath);   // what's there now (P+'s, or a stage or song put on by hand) comes back when it's off
                card.WriteFile(record.CardPath, plan.Bytes);
                expected[record.CardPath] = plan.Bytes;
                stages.Add(record);
                copies.Add(new KeyValuePair<string, StagePlan>(record.Id, plan));
                if (plan.SelectPictures.Count > 0) _newSelectPictures[record.Id] = plan.SelectPictures;
            }
            RebuildTrackLists(card, stages, expected);
            progress?.Invoke("Updating the stage select pictures", 80);
            RebuildSelectPictures(card, stages, expected);
        }

        static string NetListFolder => Build.Pf + "/sound/netplaylist/"; static string ListFolder => Build.Pf + "/sound/tracklist/";

        readonly Dictionary<string, Dictionary<string, byte[]>> _newSelectPictures = new Dictionary<string, Dictionary<string, byte[]>>();

        /// <summary>A stage mod's stage select pictures ("preview", "icon", "name"), from its plan or its folder in My Mods.</summary>
        Dictionary<string, byte[]> SelectPicturesOf(InstalledStage s)
        {
            if (_newSelectPictures.TryGetValue(s.Id, out var p)) return p;
            var r = new Dictionary<string, byte[]>();
            foreach (var k in new[] { "preview", "icon", "name" })
            {
                string f = Path.Combine(_lib.StageFolder(s.Id), "select-" + k + ".png");
                if (File.Exists(f)) r[k] = File.ReadAllBytes(f);
            }
            return r;
        }

        static Bitmap Png(byte[] b) { using (var ms = new MemoryStream(b)) using (var i = Image.FromStream(ms)) return new Bitmap(i); }

        /// <summary>The stage select pictures (sc_selmap.pac): P+'s own, with the pictures of the stage mods that are on
        /// painted in: a main stage gets the big picture, its icon and name; an L/R/Z version gets its thumbnail (its button
        /// mark stays). Made again from P+'s saved originals each time, so turning mods off brings P+'s pictures back.</summary>
        void RebuildSelectPictures(Fat32Image card, List<InstalledStage> stages, Dictionary<string, byte[]> expected)
        {
            string file = StageSelect.PicturesFile;
            var withPictures = stages.Where(x => x.Enabled && !x.IsSong).Select(x => (stage: x, pics: SelectPicturesOf(x))).Where(x => x.pics.Count > 0).ToList();
            if ((withPictures.Count == 0 && !_lib.HasOriginalFile(file)) || !card.Exists(file)) return;
            byte[] now = card.ReadFile(file);
            // the file on the card is P+'s unless it's what we wrote (a P+ update may have replaced it): keep P+'s, exactly
            bool ours = _written.TryGetValue(file, out var sha) && sha == Library.Sha256(now);
            if (!ours) _lib.SaveOriginalFile(file, now, replace: true);
            byte[] original = _lib.OriginalFile(file) ?? now;
            byte[] result = original;
            if (withPictures.Count > 0)
            {
                var select = StageSelect.Read(card);
                var layers = new Dictionary<string, List<Func<Bitmap, Bitmap>>>();
                void Add(string tex, Func<Bitmap, Bitmap> f) { if (!layers.TryGetValue(tex, out var l)) layers[tex] = l = new List<Func<Bitmap, Bitmap>>(); l.Add(f); }
                foreach (var (stage, pics) in withPictures.OrderBy(x => x.stage.InstalledAt))
                    foreach (var spot in select.SpotsOf(stage.CardPath).Where(x => x.Region != null && x.Picture >= 0))
                    {
                        if (pics.TryGetValue("preview", out var pv))
                        {
                            var region = spot.Region;
                            Add(StageSelect.PreviewTexture(spot.Picture), b => { using (var img = Png(pv)) using (var part = StageSelect.StagePart(img)) return StageSelect.PaintPreview(b, region, part); });
                        }
                        if (spot.Region != "main") continue;
                        byte[] icon = pics.TryGetValue("icon", out var ic) ? ic : pics.TryGetValue("preview", out var pv2) ? pv2 : null;
                        if (icon != null) Add(StageSelect.IconTexture(spot.Picture), b => { using (var img = Png(icon)) using (var part = StageSelect.StagePart(img)) return StageSelect.Fitted(part, b.Size, false); });
                        if (pics.TryGetValue("name", out var nm)) Add(StageSelect.NameTexture(spot.Picture), b => { using (var img = Png(nm)) return StageSelect.Fitted(img, b.Size, true); });
                    }
                var shown = Cosmetics.ReadPictures(original, layers.Keys);
                foreach (var kv in layers)
                {
                    if (!shown.TryGetValue(kv.Key, out var pic)) continue;
                    Bitmap painted = new Bitmap(pic);
                    foreach (var step in kv.Value) { var next = step(painted); painted.Dispose(); painted = next; }
                    using (painted) result = Cosmetics.ReplacePicture(result, kv.Key, painted);
                }
                foreach (var b in shown.Values) b.Dispose();
            }
            if (Library.Sha256(result) != Library.Sha256(now)) { card.WriteFile(file, result); expected[file] = result; }
            if (withPictures.Count == 0) { _written.Remove(file); _lib.ForgetOriginalFile(file); }   // P+'s file is back as it was
            else _written[file] = Library.Sha256(result);
        }

        /// <summary>The song lists of stages with songs added by us: P+'s list plus the added songs that are on, written to
        /// both the online (netplaylist) and offline (tracklist) copies, so they stay alike. With none on, P+'s list goes back.</summary>
        void RebuildTrackLists(Fat32Image card, List<InstalledStage> stages, Dictionary<string, byte[]> expected)
        {
            var lists = stages.Concat(_lib.Stages).Where(x => x.IsNewSong && x.TrackList != null).Select(x => x.TrackList)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var list in lists)
                foreach (var folder in new[] { NetListFolder, ListFolder })
                {
                    string path = folder + list;
                    if (!card.Exists(path) && !_lib.HasOriginalFile(path)) continue;
                    byte[] now = card.Exists(path) ? card.ReadFile(path) : null;
                    // what's there is P+'s unless it's what we wrote (a P+ update may have replaced the list)
                    bool ours = now != null && _written.TryGetValue(path, out var sha) && sha == Library.Sha256(now);
                    if (!ours) _lib.SaveOriginalFile(path, now, replace: true);
                    var original = _lib.OriginalFile(path);
                    if (original == null) continue;
                    var adds = stages.Where(x => x.Enabled && x.IsNewSong && string.Equals(x.TrackList, list, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.InstalledAt).ToList();
                    byte[] result = adds.Count == 0 ? original
                        : Songs.AddToTrackList(original, adds.Select(a => (SongFile(a.CardPath), a.SongTitle ?? a.Name)));
                    if (now == null || Library.Sha256(now) != Library.Sha256(result)) { card.WriteFile(path, result); expected[path] = result; }
                    if (adds.Count == 0) _written.Remove(path); else _written[path] = Library.Sha256(result);
                }
        }

        /// <summary>A song list entry's file name: the path under the strm folder without .brstm ("Custom/My Song").</summary>
        static string SongFile(string cardPath)
        {
            string rel = cardPath.Substring(Songs.StrmFolder.Length).TrimStart('/');
            return rel.EndsWith(".brstm", StringComparison.OrdinalIgnoreCase) ? rel.Substring(0, rel.Length - 6) : rel;
        }

        /// <summary>A plan to add a song file to a stage's song list (P+'s songs stay; this one joins them).</summary>
        public StagePlan PlanNewSong(Fat32Image card, SkinPackage pkg, string songFile, string trackList, string listTitle)
        {
            string stem = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(songFile), @"[^\w\-\.\(\)\[\] ,'!&]", "_").Trim();
            if (stem == "") stem = "Song";
            string path = Songs.StrmFolder + "/Custom/" + stem + ".brstm";
            for (int i = 2; card.Exists(path) || _lib.Stages.Any(x => string.Equals(x.CardPath, path, StringComparison.OrdinalIgnoreCase)); i++)
                path = Songs.StrmFolder + "/Custom/" + stem + " " + i + ".brstm";
            var plan = new StagePlan
            {
                Name = Path.GetFileNameWithoutExtension(songFile), Kind = "song-new", TrackList = trackList,
                SongTitle = Path.GetFileNameWithoutExtension(songFile),
                Target = new StageFile { CardPath = path, StageName = "New song on " + listTitle, Version = "" },
                Source = pkg.Source, SourcePath = pkg.SourcePath, PackageKey = StageKey(pkg, songFile),
                Bytes = File.ReadAllBytes(songFile),
            };
            plan.Checks.Add(Songs.IsBrstm(plan.Bytes)
                ? new CheckResult { Passed = true, Title = "It's a Wii music file (BRSTM)", Detail = "The game can play it." }
                : new CheckResult { Passed = false, Title = "Not a Wii music file", Detail = Path.GetFileName(songFile) + " isn't a BRSTM file, so the game couldn't play it." });
            plan.Checks.Add(SkinCheck.Size(Path.GetFileName(songFile), plan.Bytes.Length, Songs.MaxSongBytes));
            plan.Checks.Add(card.Exists(NetListFolder + trackList)
                ? new CheckResult { Passed = true, Title = "The stage's song list was found", Detail = trackList }
                : new CheckResult { Passed = false, Title = "The stage's song list wasn't found", Detail = trackList + " isn't on the SD card." });
            return plan;
        }

        /// <summary>Stages of ours a P+ update wrote over.</summary>
        public List<InstalledStage> OverwrittenStages(Fat32Image card) =>
            _lib.Stages.Where(s => s.Enabled && (!card.Exists(s.CardPath) || Library.Sha256(card.ReadFile(s.CardPath)) != s.Sha256 || ListReplaced(card, s) || PicturesReplaced(card, s))).ToList();

        // a stage's stage select pictures are gone (a P+ update wrote its own sc_selmap.pac)
        bool PicturesReplaced(Fat32Image card, InstalledStage s)
        {
            if (s.IsSong || !File.Exists(Path.Combine(_lib.StageFolder(s.Id), "select-preview.png")) && !File.Exists(Path.Combine(_lib.StageFolder(s.Id), "select-icon.png"))) return false;
            string f = StageSelect.PicturesFile;
            return card.Exists(f) && _lib.Written.TryGetValue(f, out var sha) && sha != Library.Sha256(card.ReadFile(f));
        }

        // an added song's stage song list no longer has our additions (a P+ update wrote its own list)
        bool ListReplaced(Fat32Image card, InstalledStage s)
        {
            if (!s.IsNewSong || s.TrackList == null) return false;
            foreach (var folder in new[] { NetListFolder, ListFolder })
            {
                string path = folder + s.TrackList;
                if (card.Exists(path) && _lib.Written.TryGetValue(path, out var sha) && sha != Library.Sha256(card.ReadFile(path))) return true;
            }
            return false;
        }

        /// <summary>Puts a skin that was turned off back on the card, from the copy kept in My Mods.</summary>
        void EnableSkin(Fat32Image card, InstalledSkin skin, Dictionary<string, byte[]> expected)
        {
            string folder = _lib.SkinFolder(skin.Id);
            var c = skin.Character;
            foreach (var f in skin.Files)
            {
                string copy = Path.Combine(folder, Path.GetFileName(f.CardPath));
                if (!File.Exists(copy)) throw new FileNotFoundException($"The saved copy of {skin.Name} is missing ({Path.GetFileName(copy)}). Delete it from My Mods and add it again.");
                byte[] now = card.Exists(f.CardPath) ? card.ReadFile(f.CardPath) : null;
                // what's on the card now is P+'s file (it may have been updated while this skin was off)
                if (now == null || Library.Sha256(now) != f.Sha256) _lib.SaveOriginalFile(f.CardPath, now, replace: true);
                var mine = File.ReadAllBytes(copy);
                card.WriteFile(f.CardPath, mine);
                expected[f.CardPath] = mine;
            }
            var redone = new List<InstalledPicture>();
            foreach (var p in skin.Pictures)
            {
                string png = Path.Combine(folder, p.Kind + ".png");
                if (!File.Exists(png)) continue;
                var before = card.ReadFile(p.CardPath);
                bool ours = _written.TryGetValue(p.CardPath, out var sha) && sha == Library.Sha256(before);
                if (!ours) _lib.SaveOriginalFile(p.CardPath, before, replace: true);    // P+'s current version of the whole file
                using (var shown = Cosmetics.ReadPicture(before, p.TextureName))
                    if (shown != null) _lib.SaveOriginalPicture(p.CardPath, p.TextureName, shown, replace: true);
                using (var tmp = new Bitmap(png))
                using (var mine = new Bitmap(tmp))
                    redone.Add(PutPicture(card, c, skin.SlotIndex, p.Kind, mine, expected, saveOriginal: false));
            }
            skin.Pictures = redone;
        }

        void SaveSkinCopy(string id, SkinPlan plan)
        {
            string folder = _lib.SkinFolder(id);
            Directory.CreateDirectory(folder);
            foreach (var kv in plan.Files) File.WriteAllBytes(Path.Combine(folder, Path.GetFileName(kv.Key)), kv.Value);
            foreach (var kv in plan.Originals) File.WriteAllBytes(OriginalCopy(Path.Combine(folder, Path.GetFileName(kv.Key))), kv.Value);
            foreach (var kv in plan.Pictures) kv.Value.Save(Path.Combine(folder, kv.Key + ".png"), ImageFormat.Png);
        }

        public static string PixelHash(Bitmap b)
        {
            if (b == null) return null;
            var bytes = new List<byte>(b.Width * b.Height * 4);
            for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) bytes.AddRange(BitConverter.GetBytes(b.GetPixel(x, y).ToArgb()));
            return Library.Sha256(bytes.ToArray());
        }

        static void CopyWithProgress(string from, string to, Action<int> progress)
        {
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[8 << 20]; long done = 0, total = src.Length; int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n); done += n;
                    int pct = (int)(done * 90 / Math.Max(1, total));
                    if (pct != last) { progress?.Invoke(pct); last = pct; }
                }
                dst.Flush(true);
            }
        }
    }
}
