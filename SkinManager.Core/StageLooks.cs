// Two things for stage maps that can't simply replace a P+ stage:
//  - Explain(): what exactly differs from P+'s stage, in plain words ("the top blast zone is 12 units higher").
//  - SwapLooks(): the map's looks on P+'s own stage file. P+'s file stays the base (its stage program, floors,
//    blast zones, spawn points and moving platforms); its scenery is made invisible and the map's models,
//    textures and animations are added to its main scenery part. This is how P+'s own alternate stages are
//    built (Pokemon Stadium 2 "Gateway" keeps StgStadium00 with no pieces and adds Stage, BG_Crowd, BG_Sky...).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    /// <summary>Stage maps that don't play like P+'s stage: explaining what differs, and putting the map's looks on
    /// P+'s own file.</summary>
    public static partial class StageCheck
    {

        // ----- shapes -----

        static string Outline(ResourceNode root) => string.Join(";", Surfaces(root).Select(f => string.Format(CultureInfo.InvariantCulture,
            "{0}{1:0.#},{2:0.#},{3:0.#},{4:0.#}", f.floor ? "F" : "W", f.x1, f.y1, f.x2, f.y2)));

        // ----- the map's looks on P+'s stage -----

        public sealed class LooksResult
        {
            public byte[] Bytes;
            public string Problem;
            public int Models, Textures, Animations;
            public List<string> KeptLooks = new List<string>();   // P+'s parts that keep P+'s look (moving platforms)
            public bool StaticParts;                              // some of the map's moving decorations stand still
            public int TexturesShrunk;                            // P+'s pictures nothing visible uses any more, made tiny
            public int PlatformsLeftOut;                          // the map's own copies of P+'s moving platforms, not used
        }

        static IEnumerable<ResourceNode> All(ResourceNode n) { foreach (var c in n.Children) { yield return c; foreach (var d in All(c)) yield return d; } }
        /// <summary>The bones P+'s collision rides on (the map's models with a bone by one of these names are its copies of
        /// P+'s moving parts, and stay out: P+'s move instead).</summary>
        static HashSet<string> RideBones(ResourceNode b) =>
            new HashSet<string>(b.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>()
                .SelectMany(c => c.Children.OfType<CollisionObject>()).Select(o => o._boneName).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

        /// <summary>How wide a model is drawn at rest. (BrawlLib's GetBox reads points placed by ApplyCHR; on a model just read
        /// it measured 0, so every part P+'s collision rides on kept P+'s look, however wide: fixed 2026-11-04.)</summary>
        static float ModelWidth(MDL0Node m)
        {
            try { m.Populate(); m.ApplyCHR(null, 0); var box = m.GetBox(); float w = box.Max._x - box.Min._x; return float.IsNaN(w) || w < 0 ? 0 : w; }
            catch { return 0; }
        }

        static int Polys(MDL0Node m) => m.FindChild("Objects", false)?.Children.Count ?? 0;

        /// <summary>P+'s pictures that nothing left visible uses (its scenery is out of sight once the map's looks are on):
        /// each made a 4x4 grey picture under the same name, so everything that names it still finds it, but the game no
        /// longer holds the full pictures in its stage memory. Without this a map plus P+'s whole picture set can unpack past
        /// what the game has room for (Smashville: 1.8 MB of P+'s pictures). Returns how many were made tiny.</summary>
        static int ShrinkHidden(ResourceNode root)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var shownMaterials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(x => Polys(x) > 0))
            {
                m.Populate();
                foreach (var t in m.TextureList ?? new List<ResourceNode>()) used.Add(t.Name);
                foreach (var t in m.PaletteList ?? new List<ResourceNode>()) used.Add(t.Name);
                foreach (var mat in m.FindChildrenByType(null, ResourceType.MDL0Material)) shownMaterials.Add(mat.Name);
            }
            // picture-swap animations on something still shown keep every picture they swap in
            foreach (var pat in root.FindChildrenByType(null, ResourceType.PAT0).OfType<PAT0Node>())
            {
                pat.Populate();
                if (!pat.Children.Any(e => shownMaterials.Contains(e.Name))) continue;
                foreach (var e in All(pat).OfType<PAT0TextureEntryNode>())
                {
                    if (!string.IsNullOrEmpty(e.Texture)) used.Add(e.Texture);
                    if (!string.IsNullOrEmpty(e.Palette)) used.Add(e.Palette);
                }
            }
            int n = 0;
            using (var tiny = new System.Drawing.Bitmap(4, 4))
            {
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) tiny.SetPixel(x, y, System.Drawing.Color.Gray);
                foreach (var t in root.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().ToList())
                {
                    if (used.Contains(t.Name) || t.Width * t.Height <= 16) continue;
                    // a picture drawn with a palette stays as it is: made a plain picture, its model (still loaded by the game,
                    // even with nothing left to draw) would ask for a palette it no longer has, and the game crashes loading the
                    // stage (Snowman's Land on Dream Land, 2026-11-04: Dream Land's Scarfy). Same for pictures sharing their data.
                    if (t.HasPalette || t.SharesData) continue;
                    t.ReplaceRaw(BrawlLib.Wii.Textures.TextureConverter.I4.EncodeTEX0Texture(tiny, 1));
                    n++;
                }
            }
            return n;
        }

        /// <summary>A stage file with the looks already moved over (by an older version), with P+'s hidden pictures made
        /// tiny (see ShrinkHidden). Null if there was nothing to do or it couldn't be read.</summary>
        public static byte[] Slim(byte[] stage, out int shrunk)
        {
            shrunk = 0;
            try
            {
                using (var tIn = new TempFile(".pac", stage))
                using (var tOut = new TempFile(".pac"))
                {
                    lock (BrawlGate.Lock)
                        using (var root = NodeFactory.FromFile(null, tIn.Path))
                        {
                            if (root == null) return null;
                            shrunk = ShrinkHidden(root);
                            if (shrunk == 0) return null;
                            root.Export(tOut.Path);
                        }
                    return File.ReadAllBytes(tOut.Path);
                }
            }
            catch { return null; }
        }

        /// <summary>The names of a stage's models that gameplay uses: position markers and models collision rides on.</summary>
        static HashSet<string> GameplayModels(ResourceNode root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(IsPositionModel)) names.Add(m.Name);
            foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName))) names.Add(o._modelName);
            return names;
        }

        static List<BRRESNode> ModelFiles(ResourceNode root) =>
            root.FindChildrenByType(null, ResourceType.BRES).OfType<BRRESNode>().Where(b => b.FindChildrenByType(null, ResourceType.MDL0).Any()).ToList();

        /// <summary>Did an older version turn one of P+'s palette pictures into a plain one (ShrinkHidden, before
        /// 2026-11-04)? The game then can't put the picture on its model and crashes loading the stage. stage: the install;
        /// original: P+'s file.</summary>
        public static bool BrokenPictures(byte[] stage, byte[] original)
        {
            try
            {
                lock (BrawlGate.Lock)
                using (var tA = new TempFile(".pac", stage))
                using (var tB = new TempFile(".pac", original))
                using (var a = NodeFactory.FromFile(null, tA.Path))
                using (var b = NodeFactory.FromFile(null, tB.Path))
                {
                    var mine = a.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().HasPalette, StringComparer.Ordinal);
                    return b.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().Any(x => x.HasPalette && mine.TryGetValue(x.Name, out bool pal) && !pal);
                }
            }
            catch { return false; }
        }

        /// <summary>Is a part of P+'s file only far-off background (none of its scenery anywhere near where the fighters
        /// are) that the stage program doesn't name? Some stage programs draw such a part their own way (Green Hill Zone's
        /// distant hills, Frigate's husk), so a map's ground put there shows far off P+'s floor (Emerald Coast on Green Hill
        /// Zone, 2026-11-04: its ground missing, its boardwalk huge in front of the camera).</summary>
        static bool FarBackground(BRRESNode part, List<(float x1, float y1, float x2, float y2, bool floor)> surfaces, ICollection<string> program)
        {
            var floors = surfaces.Where(s => s.floor).ToList();
            if (floors.Count == 0) return false;
            var models = part.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().ToList();
            if (models.Any(m => program.Contains(m.Name) || m.BoneCache.Any(x => program.Contains(x.Name)))) return false;
            float x0 = floors.Min(s => Math.Min(s.x1, s.x2)) - 20, x1 = floors.Max(s => Math.Max(s.x1, s.x2)) + 20;
            float y0 = floors.Min(s => Math.Min(s.y1, s.y2)) - 40, y1 = floors.Max(s => Math.Max(s.y1, s.y2)) + 40;
            try
            {
                foreach (var (p, q, r) in ModelPreview.Triangles(models.Where(m => Polys(m) > 0)))
                    foreach (var v in new[] { p, q, r })
                        if (v._x >= x0 && v._x <= x1 && v._y >= y0 && v._y <= y1 && Math.Abs(v._z) <= 40) return false;
            }
            catch { return false; }
            return true;
        }

        /// <summary>Of some parts of a stage, the one whose scenery covers its main floor: the ground the fighters stand on
        /// (null if none does). Checked at 11 points along the longest floor, at the fighters' depth.</summary>
        static BRRESNode GroundPart(List<BRRESNode> parts, List<(float x1, float y1, float x2, float y2, bool floor)> surfaces)
        {
            var floor = surfaces.Where(s => s.floor).OrderByDescending(s => Math.Abs(s.x2 - s.x1)).FirstOrDefault();
            if (parts.Count == 0 || Math.Abs(floor.x2 - floor.x1) < 1) return null;
            BRRESNode best = null; int bestHits = 0;
            foreach (var part in parts)
            {
                List<(BrawlLib.Internal.Vector3 a, BrawlLib.Internal.Vector3 b, BrawlLib.Internal.Vector3 c)> tris;
                try { tris = ModelPreview.Triangles(part.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0)); }
                catch { continue; }
                int hits = 0;
                for (int s = 0; s <= 10; s++)
                {
                    float u = 0.05f + 0.9f * s / 10, x = floor.x1 + (floor.x2 - floor.x1) * u, y = floor.y1 + (floor.y2 - floor.y1) * u;
                    if (tris.Any(t => new[] { -5f, 0f, 5f }.Any(z => SurfaceAt(t.a, t.b, t.c, x, z) is float h && Math.Abs(h - y) < 3))) hits++;
                }
                if (hits > bestHits) { bestHits = hits; best = part; }
            }
            return bestHits >= 6 ? best : null;   // most of the floor: a few touches are props, not the ground
        }

        /// <summary>The height of a triangle straight above or below (x, z), or null if it isn't there.</summary>
        static float? SurfaceAt(BrawlLib.Internal.Vector3 a, BrawlLib.Internal.Vector3 b, BrawlLib.Internal.Vector3 c, float x, float z)
        {
            if (x < Math.Min(a._x, Math.Min(b._x, c._x)) || x > Math.Max(a._x, Math.Max(b._x, c._x))) return null;
            float d = (b._z - c._z) * (a._x - c._x) + (c._x - b._x) * (a._z - c._z);
            if (Math.Abs(d) < 1e-6) return null;
            float l1 = ((b._z - c._z) * (x - c._x) + (c._x - b._x) * (z - c._z)) / d, l2 = ((c._z - a._z) * (x - c._x) + (a._x - c._x) * (z - c._z)) / d, l3 = 1 - l1 - l2;
            if (l1 < 0 || l2 < 0 || l3 < 0) return null;
            return l1 * a._y + l2 * b._y + l3 * c._y;
        }

        /// <summary>The part of P+'s file a map's looks go in: P+'s biggest part with nothing gameplay uses in it (always
        /// drawn), unless that's far-off background the program doesn't name (see FarBackground): then the part with P+'s
        /// own ground in it. P+'s file must be posed at rest.</summary>
        static BRRESNode LooksPart(List<BRRESNode> files, Func<BRRESNode, bool> pure, Func<BRRESNode, int> size,
                                   List<(float x1, float y1, float x2, float y2, bool floor)> surfaces, ICollection<string> program)
        {
            var biggest = files.Where(pure).OrderByDescending(size).FirstOrDefault(f => size(f) > 0);
            if (biggest == null || !FarBackground(biggest, surfaces, program)) return biggest;
            return GroundPart(files.Where(pure).Where(f => size(f) > 0).ToList(), surfaces) ?? biggest;
        }

        /// <summary>Did an older version put a looks install in a part LooksPart wouldn't pick now (far-off background:
        /// Emerald Coast on Green Hill Zone, Phendrana Husk on Frigate)? stage: the install; original: P+'s file.</summary>
        public static bool LooksMisplaced(byte[] stage, byte[] original, ICollection<string> program)
        {
            try
            {
                program = program ?? new HashSet<string>();
                lock (BrawlGate.Lock)
                using (var tA = new TempFile(".pac", stage))
                using (var tB = new TempFile(".pac", original))
                using (var a = NodeFactory.FromFile(null, tA.Path))
                using (var b = NodeFactory.FromFile(null, tB.Path))
                {
                    var keep = GameplayModels(b);
                    bool Pure(BRRESNode f) => f.FindChildrenByType(null, ResourceType.MDL0).All(m => !keep.Contains(m.Name));
                    int Size(BRRESNode f) => f.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Sum(Polys);
                    foreach (var m in b.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>()) try { m.ApplyCHR(null, 0); } catch { }
                    var want = LooksPart(ModelFiles(b), Pure, Size, Surfaces(original), program);
                    if (want == null) return false;
                    // where the looks are now: the install's part with the most to draw (P+'s own pieces were taken out)
                    var now = ModelFiles(a).Where(Pure).OrderByDescending(Size).FirstOrDefault(f => Size(f) > 0);
                    return now != null && now.Name != want.Name;
                }
            }
            catch { return false; }
        }

        /// <param name="program">names P+'s stage program looks up (StageIndex.ProgramNames): those parts stay exactly P+'s</param>
        /// <param name="keepPPlus">P+'s pieces ("model/object") that stay in sight: floors the map draws nothing for (FitPieces)</param>
        public static LooksResult SwapLooks(byte[] map, byte[] original, ICollection<string> program = null, ICollection<string> keepPPlus = null)
        {
            program = program ?? new HashSet<string>();
            var r = new LooksResult();
            string dir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "looks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using (var tA = new TempFile(".pac", map))
                using (var tB = new TempFile(".pac", original))
                using (var outFile = new TempFile(".pac"))
                {
                    lock (BrawlGate.Lock)
                    {
                        using (var a = NodeFactory.FromFile(null, tA.Path))
                        using (var b = NodeFactory.FromFile(null, tB.Path))
                        {
                            if (a == null || b == null) { r.Problem = "One of the files couldn't be read."; return r; }
                            var keep = GameplayModels(b);
                            var files = ModelFiles(b);
                            bool Pure(BRRESNode f) => f.FindChildrenByType(null, ResourceType.MDL0).All(m => !keep.Contains(m.Name));
                            int Size(BRRESNode f) => f.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Sum(Polys);
                            // P+'s main scenery part (see LooksPart: the biggest part with nothing gameplay uses in it, unless that's
                            // far-off background the program draws its own way: then the part with P+'s ground)
                            foreach (var m in b.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>()) try { m.ApplyCHR(null, 0); } catch { }
                            var main = LooksPart(files, Pure, Size, Surfaces(original), program);
                            if (main == null) { r.Problem = "P+'s stage keeps its scenery together with its gameplay parts, so there's no place to put the map's looks."; return r; }
                            // the map's animations go first (the one a stage plays) only where the stage program uses nothing:
                            // otherwise after P+'s, so nothing the program times its events by changes
                            bool animsFirst = main.FindChildrenByType(null, ResourceType.MDL0).All(m => !program.Contains(m.Name));
                            var pplusBones = new HashSet<string>(b.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().SelectMany(m => m.BoneCache.Select(x => x.Name)), StringComparer.Ordinal);

                            // P+'s scenery out of sight: its pieces go, its bones and names stay (so nothing the stage program looks for is missing)
                            var surf = Surfaces(original);
                            float width = surf.Count == 0 ? 0 : surf.Max(s => Math.Max(s.x1, s.x2)) - surf.Min(s => Math.Min(s.x1, s.x2));
                            var pMoving = MovingGameplay(b);
                            foreach (var f in files)
                                foreach (var m in f.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0).ToList())
                                {
                                    if (keep.Contains(m.Name))
                                    {
                                        // a moving platform keeps P+'s look (the map's version wouldn't move with it); a part the whole stage rides on goes
                                        // (a part that moves always keeps it, however wide: the map's copy of it is left out too)
                                        float w = pMoving.Contains(m.Name) ? 0 : ModelWidth(m);
                                        if (pMoving.Contains(m.Name) || width <= 0 || w < width * 0.5f) { r.KeptLooks.Add(m.Name); continue; }
                                    }
                                    foreach (var obj in m.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().ToList())
                                        if (keepPPlus == null || !keepPPlus.Contains(m.Name + "/" + obj.Name))   // P+'s own look for a floor the map doesn't draw (see FitPieces)
                                        obj.Remove(true, true, true, true, true, true, true, true, true, true, true, true);
                                }

                            // the map's looks: every model with something to draw that its own gameplay doesn't use
                            // (a part collision rides on that doesn't move is only scenery here: Yoshi's Story 64 draws its whole stage
                            // in one; left out, its ground would be missing; 2026-11-04)
                            var mapKeep = MovingGameplay(a);
                            // the map's own copy of a moving platform (it has the bone P+'s platform floor rides on): left out, P+'s moves instead
                            var rideBones = RideBones(b);
                            foreach (var m in a.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                                if (!mapKeep.Contains(m.Name) && Polys(m) > 0 && m.BoneCache.Cast<MDL0BoneNode>().Any(x => rideBones.Contains(x.Name)))
                                { mapKeep.Add(m.Name); r.PlatformsLeftOut++; }
                            var groups = ModelFiles(a)
                                .Select(f => (file: f, models: f.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0 && !mapKeep.Contains(m.Name)).ToList()))
                                .Where(g => g.models.Count > 0).OrderByDescending(g => g.models.Sum(Polys)).ToList();
                            if (groups.Count == 0) { r.Problem = "The map has no scenery of its own to move over."; return r; }

                            // new names never match a part of P+'s file or a name the stage program looks for
                            var usedNames = new HashSet<string>(All(b).Select(n => n.Name ?? "").Concat(program), StringComparer.Ordinal);
                            string Unique(string name) { string n = name; for (int i = 2; usedNames.Contains(n); i++) n = name + "_" + i; usedNames.Add(n); return n; }
                            int file = 0;
                            var imported = new List<MDL0Node>();
                            var importedPat = new List<PAT0Node>();
                            for (int k = 0; k < groups.Count; k++)
                            {
                                var (src, models) = groups[k];
                                var anims = All(src).Where(n => n is CHR0Node || n is SRT0Node || n is CLR0Node || n is PAT0Node || n is VIS0Node || n is SHP0Node).Cast<BRESEntryNode>().ToList();
                                // bones named like one of P+'s or like something the stage program looks for get another name (with
                                // their animation tracks), so the map's animations never move P+'s parts
                                bool Clashes(string n) => program.Contains(n) || pplusBones.Contains(n);
                                foreach (var m in models)
                                    foreach (var bone in m.BoneCache.Cast<MDL0BoneNode>().Where(x => Clashes(x.Name)).ToList()) bone.Name += "_map";
                                foreach (var an in anims.Where(x => x is CHR0Node || x is VIS0Node))
                                    foreach (var e in an.Children.Where(e => Clashes(e.Name)).ToList()) e.Name += "_map";
                                if (k > 0)
                                {
                                    // another part of the map: its bones and materials get their own names, so its animations
                                    // don't move parts of the other models (animations work by bone and material names)
                                    string tag = "_m" + k;
                                    foreach (var m in models)
                                    {
                                        foreach (var bone in m.BoneCache.Cast<MDL0BoneNode>()) bone.Name += tag;
                                        foreach (var mat in m.FindChildrenByType(null, ResourceType.MDL0Material)) mat.Name += tag;
                                    }
                                    foreach (var an in anims)
                                        foreach (var e in an.Children) e.Name += tag;
                                    anims.RemoveAll(x => x is SHP0Node);
                                }
                                foreach (var m in models)
                                {
                                    string p = Path.Combine(dir, (file++) + ".mdl0");
                                    m.Export(p);
                                    var copy = (MDL0Node)NodeFactory.FromFile(null, p);
                                    copy.Name = Unique(m.Name);
                                    main.GetOrCreateFolder<MDL0Node>().AddChild(copy);
                                    imported.Add(copy);
                                    r.Models++;
                                }
                                int at = 0;
                                foreach (var an in anims)
                                {
                                    string ext = an is CHR0Node ? ".chr0" : an is SRT0Node ? ".srt0" : an is CLR0Node ? ".clr0" : an is PAT0Node ? ".pat0" : an is VIS0Node ? ".vis0" : ".shp0";
                                    string p = Path.Combine(dir, (file++) + ext);
                                    an.Export(p);
                                    var copy = (BRESEntryNode)NodeFactory.FromFile(null, p);
                                    copy.Name = Unique("map_" + an.Name);   // never named like one of P+'s parts (stages find animations by their part's name)
                                    BRESGroupNode folder =
                                        copy is CHR0Node ? main.GetOrCreateFolder<CHR0Node>() : copy is SRT0Node ? main.GetOrCreateFolder<SRT0Node>() :
                                        copy is CLR0Node ? main.GetOrCreateFolder<CLR0Node>() : copy is PAT0Node ? main.GetOrCreateFolder<PAT0Node>() :
                                        copy is VIS0Node ? main.GetOrCreateFolder<VIS0Node>() : main.GetOrCreateFolder<SHP0Node>();
                                    // the map's main animations first: a stage plays the first animation of each kind in a part
                                    if (k == 0 && animsFirst) folder.InsertChild(copy, true, Math.Min(at++, folder.Children.Count)); else folder.AddChild(copy);
                                    if (copy is PAT0Node pat) importedPat.Add(pat);
                                    r.Animations++;
                                }
                                if ((k > 0 || !animsFirst) && anims.Count > 0) r.StaticParts = true;   // may not play: only the first of each kind is sure to
                            }

                            // the pictures, renamed so they can't mix with P+'s
                            var texNames = new HashSet<string>(StringComparer.Ordinal);
                            var pltNames = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var m in imported) m.Populate();
                            foreach (var pat in importedPat) pat.Populate();
                            foreach (var m in imported)
                            {
                                foreach (var t in m.TextureList ?? new List<ResourceNode>()) texNames.Add(t.Name);
                                foreach (var t in m.PaletteList ?? new List<ResourceNode>()) pltNames.Add(t.Name);
                            }
                            foreach (var e in importedPat.SelectMany(All).OfType<PAT0TextureEntryNode>())
                            {
                                if (!string.IsNullOrEmpty(e.Texture)) texNames.Add(e.Texture);
                                if (!string.IsNullOrEmpty(e.Palette)) pltNames.Add(e.Palette);
                            }
                            var texRename = new Dictionary<string, string>(StringComparer.Ordinal);
                            var allTex = a.FindChildrenByType(null, ResourceType.TEX0).OfType<TEX0Node>().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
                            var allPlt = a.FindChildrenByType(null, ResourceType.PLT0).OfType<PLT0Node>().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
                            var pTex = new HashSet<string>(b.FindChildrenByType(null, ResourceType.TEX0).Select(t => t.Name)
                                .Concat(b.FindChildrenByType(null, ResourceType.PLT0).Select(t => t.Name)), StringComparer.Ordinal);
                            string NewName(string n) { string x = "zMap_" + n; for (int i = 2; pTex.Contains(x); i++) x = "zMap" + i + "_" + n; pTex.Add(x); return x; }
                            // a texture and its palette share a name: both get the same new one
                            foreach (var n in texNames.Concat(pltNames).Distinct())
                            {
                                var sources = new List<BRESEntryNode>();
                                if (allTex.TryGetValue(n, out var t0)) sources.Add(t0);
                                if (allPlt.TryGetValue(n, out var p0)) sources.Add(p0);
                                if (sources.Count == 0) continue;   // the map doesn't have it either (it showed nothing there before)
                                string newName = texRename[n] = NewName(n);
                                foreach (var src in sources)
                                {
                                    string p = Path.Combine(dir, (file++) + (src is TEX0Node ? ".tex0" : ".plt0"));
                                    src.Export(p);
                                    var copy = (BRESEntryNode)NodeFactory.FromFile(null, p);
                                    copy.Name = newName;
                                    if (copy is TEX0Node) { main.GetOrCreateFolder<TEX0Node>().AddChild(copy); r.Textures++; }
                                    else main.GetOrCreateFolder<PLT0Node>().AddChild(copy);
                                }
                            }
                            foreach (var m in imported)
                            {
                                foreach (var t in (m.TextureList ?? new List<ResourceNode>()).ToList()) if (texRename.TryGetValue(t.Name, out var nn)) t.Name = nn;
                                foreach (var t in (m.PaletteList ?? new List<ResourceNode>()).ToList()) if (texRename.TryGetValue(t.Name, out var nn)) t.Name = nn;
                            }
                            // in picture-swap animations: the names set directly, then each animation's list made once
                            // (BrawlLib's own setter remakes the whole list on every change, which takes forever on long animations)
                            foreach (var pat in importedPat)
                            {
                                foreach (var e in All(pat).OfType<PAT0TextureEntryNode>())
                                {
                                    if (e._tex != null && texRename.TryGetValue(e._tex, out var nt)) e._tex = nt;
                                    if (e._plt != null && texRename.TryGetValue(e._plt, out var np)) e._plt = np;
                                }
                                pat.RegenerateTextureList(); pat.RegeneratePaletteList();
                            }
                            r.TexturesShrunk = ShrinkHidden(b);
                            b.Export(outFile.Path);
                        }
                    }
                    r.Bytes = File.ReadAllBytes(outFile.Path);
                    return r;
                }
            }
            catch (Exception e) { r.Problem = "Couldn't move the looks over: " + e.Message; r.Bytes = null; return r; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
