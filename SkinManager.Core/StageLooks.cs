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
    public static partial class StageCheck
    {
        // ----- what's different -----

        /// <summary>What differs between a map and P+'s stage, one plain line each (empty: plays the same).</summary>
        public static List<string> Explain(byte[] map, byte[] original, ICollection<string> program = null)
        {
            var lines = new List<string>();
            var ga = Gameplay(map); var gb = Gameplay(original);
            var programParts = ProgramParts(gb.Keys.Union(ga.Keys).Where(k => !ga.TryGetValue(k, out var x) || !gb.TryGetValue(k, out var y) || x != y), program);
            foreach (var part in programParts.Take(4))
                lines.Add($"{part}: the stage program works with this part directly, and the map's version is built differently " +
                          (ga.Keys.Any(k => k.StartsWith("model " + part, StringComparison.Ordinal)) ? "(other bones, animations or place in the file)" : "(the map doesn't have it)") +
                          ". It drives stage events, so online games would go out of sync.");
            bool Differ(string prefix) => gb.Keys.Union(ga.Keys).Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Any(k => !ga.TryGetValue(k, out var x) || !gb.TryGetValue(k, out var y) || x != y);

            // floors and platforms
            if (Differ("collision"))
            {
                var fit = HowFarOff(map, original);
                if (fit.Floors == float.MaxValue) lines.Add("Floors and platforms: completely different. One of the two only has floors that move (like Corneria's Great Fox), so they can't be lined up.");
                else if (fit.Exact) lines.Add("Floors and platforms: same shape, but some edges are a different kind (a ledge you can or can't grab, a platform you can or can't drop through, or a wall).");
                else
                {
                    string w = fit.Width > 0 ? $" (the stage is {fit.Width:0} wide)" : "";
                    if (fit.Floors >= 0.5f) lines.Add($"Floors and platforms: where you stand is off by up to {Units(fit.Floors)}{w}.");
                    if (fit.Rest >= 0.5f) lines.Add($"Sides and underside of the stage: off by up to {Units(fit.Rest)}{w}.");
                    if (fit.Floors < 0.5f && fit.Rest < 0.5f) lines.Add("Floors and platforms: a moving part's floor is different.");
                }
            }
            if (Differ("material")) lines.Add("Floor materials: some surfaces are made of something else (footstep sounds and effects, and how slippery they are).");

            // blast zones, camera, spawn points
            if (Differ("positions"))
            {
                var pa = Markers(map); var pb = Markers(original);
                var found = new List<string>();
                // markers that all moved by the same amount (P+ re-centered the stage: blast zones, spawns and respawns together)
                var moved = pb.Where(kv => pa.ContainsKey(kv.Key))
                    .Select(kv => (name: kv.Key, dx: (float)Math.Round(pa[kv.Key].x - kv.Value.x, 1), dy: (float)Math.Round(pa[kv.Key].y - kv.Value.y, 1)))
                    .Where(x => Math.Abs(x.dx) >= 0.5f || Math.Abs(x.dy) >= 0.5f).ToList();
                var together = moved.GroupBy(x => (x.dx, x.dy)).OrderByDescending(g => g.Count()).FirstOrDefault();
                var inGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (together != null && together.Count() >= 4)
                {
                    foreach (var x in together) inGroup.Add(x.name);
                    var kinds = together.Select(x => x.name.ToLowerInvariant()).Select(n => n.StartsWith("dead") ? "blast zones" : n.StartsWith("player") ? "start points"
                        : n.StartsWith("rebirth") ? "respawn points" : n.StartsWith("camcntr") ? "camera center" : n.StartsWith("camlimit") ? "camera limits" : n.StartsWith("item") ? "item spots" : null)
                        .Where(k => k != null).Distinct().ToList();
                    var (dx, dy) = together.Key;
                    var dir = new List<string>();
                    if (Math.Abs(dx) >= 0.05f) dir.Add($"{Units(Math.Abs(dx))} further {(dx > 0 ? "right" : "left")}");
                    if (Math.Abs(dy) >= 0.05f) dir.Add($"{Units(Math.Abs(dy))} {(dy > 0 ? "higher" : "lower")}");
                    string list = kinds.Count == 0 ? "position markers" : kinds.Count == 1 ? kinds[0] : string.Join(", ", kinds.Take(kinds.Count - 1)) + " and " + kinds.Last();
                    lines.Add($"The map's {list} are all {string.Join(" and ", dir)} than P+'s." + (kinds.Count >= 3 && kinds.Contains("blast zones")
                        ? " They moved together, so P+ most likely re-centered the stage after the map was made." : ""));
                }
                foreach (var kv in pb.OrderBy(kv => MarkerOrder(kv.Key)))
                {
                    if (!pa.TryGetValue(kv.Key, out var mine)) { if (!string.IsNullOrEmpty(MarkerName(kv.Key))) found.Add(MarkerName(kv.Key) + ": missing in the map"); continue; }
                    if (inGroup.Contains(kv.Key)) continue;
                    float dx = mine.x - kv.Value.x, dy = mine.y - kv.Value.y;
                    if (Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f) continue;
                    if (MarkerName(kv.Key) == "") continue;
                    string what = MarkerName(kv.Key) ?? "Marker \"" + kv.Key + "\"";
                    var parts = new List<string>();
                    if (Math.Abs(dx) >= 0.5f) parts.Add($"{Units(Math.Abs(dx))} further {(dx > 0 ? "right" : "left")}");
                    if (Math.Abs(dy) >= 0.5f) parts.Add($"{Units(Math.Abs(dy))} {(dy > 0 ? "higher" : "lower")}");
                    found.Add(what + ": " + string.Join(" and ", parts));
                }
                if (found.Count == 0 && inGroup.Count == 0) lines.Add("Blast zones, camera or spawn points: set up differently (the markers are arranged in another way).");
                else
                {
                    lines.AddRange(found.Take(6));
                    if (found.Count > 6) lines.Add($"...and {found.Count - 6} more markers.");
                }
            }

            // moving platforms
            foreach (var k in gb.Keys.Where(k => k.StartsWith("platform ", StringComparison.Ordinal)))
            {
                string name = k.Substring(9);
                if (!ga.TryGetValue(k, out var x) || x == "missing") lines.Add($"Moving platform ({name}): the map doesn't have it.");
                else if (x != gb[k]) lines.Add($"Moving platform ({name}): it moves differently (path, speed or timing).");
            }
            foreach (var k in ga.Keys.Where(k => k.StartsWith("platform ", StringComparison.Ordinal) && !gb.ContainsKey(k)))
                lines.Add($"Moving platform ({k.Substring(9)}): the map has it and P+'s stage doesn't.");

            if (Differ("surfaces")) lines.Add("Surface types (grip, ice and the like) are set up differently.");
            if (Differ("camera")) lines.Add("Camera settings are different (how far it zooms and follows).");
            if (Differ("items")) lines.Add("Where items appear is different.");
            if (Differ("settings")) lines.Add("Other stage settings differ (hazards, stage events and the like).");
            return lines;
        }

        static float Median(IEnumerable<float> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }

        static string Units(float v) => v.ToString(v < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + (Math.Abs(v - 1) < 0.05f ? " unit" : " units");

        /// <summary>Position markers by bone name: where each is (2D, as the game uses them).</summary>
        static Dictionary<string, (float x, float y)> Markers(byte[] stage)
        {
            var result = new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase);
            using (var tmp = new TempFile(".pac", stage))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
                foreach (var m in root.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(IsPositionModel))
                    foreach (var b in m.BoneCache.Cast<MDL0BoneNode>())
                    {
                        if (b.Parent is MDL0Node) continue;   // the model's own root
                        var p = b.Translation;
                        for (var up = b.Parent as MDL0BoneNode; up != null && !(up.Parent is MDL0Node); up = up.Parent as MDL0BoneNode) { p._x += up.Translation._x; p._y += up.Translation._y; }
                        if (!result.ContainsKey(b.Name)) result[b.Name] = (p._x, p._y);
                    }
            return result;
        }

        static string MarkerName(string bone)
        {
            string b = bone.ToLowerInvariant();
            if (b == "dead0n") return "Top-left blast zone corner";
            if (b == "dead1n") return "Bottom-right blast zone corner";
            if (b == "camlimit0n") return "Camera limit (top-left)";
            if (b == "camlimit1n") return "Camera limit (bottom-right)";
            var m = System.Text.RegularExpressions.Regex.Match(b, @"^player(\d)n$");
            if (m.Success) return $"Player {int.Parse(m.Groups[1].Value) + 1} start point";
            m = System.Text.RegularExpressions.Regex.Match(b, @"^rebirth(\d)n$");
            if (m.Success) return $"Respawn point {int.Parse(m.Groups[1].Value) + 1}";
            if (b.StartsWith("item")) return "Item spawn area";
            if (System.Text.RegularExpressions.Regex.IsMatch(b, @"^(player|rebirth)\de$")) return "";   // the far end of a start/respawn point: goes with it
            return null;
        }
        static int MarkerOrder(string bone) { string b = bone.ToLowerInvariant(); return b.StartsWith("dead") ? 0 : b.StartsWith("camlimit") ? 1 : b.StartsWith("player") ? 2 : b.StartsWith("rebirth") ? 3 : 4; }

        // ----- shapes -----

        static string Outline(ResourceNode root) => string.Join(";", Surfaces(root).Select(f => string.Format(CultureInfo.InvariantCulture,
            "{0}{1:0.#},{2:0.#},{3:0.#},{4:0.#}", f.floor ? "F" : "W", f.x1, f.y1, f.x2, f.y2)));

        static List<(float x1, float y1, float x2, float y2, bool floor)> FromOutline(string o) =>
            (o ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p =>
            {
                var n = p.Substring(1).Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                return (n[0], n[1], n[2], n[3], p[0] == 'F');
            }).ToList();

        /// <summary>How far apart two stages' shapes are (from their fingerprints' outlines); null when one has none.</summary>
        public static Fit ShapeFit(Dictionary<string, string> map, Dictionary<string, string> pplus)
        {
            if (map == null || pplus == null || !map.TryGetValue("outline", out var a) || !pplus.TryGetValue("outline", out var b)) return null;
            var sa = FromOutline(a); var sb = FromOutline(b);
            if (sa.Count == 0 || sb.Count == 0) return null;
            return FitOf(sa, sb);
        }

        // ----- the map's looks on P+'s stage -----

        public sealed class LooksResult
        {
            public byte[] Bytes;
            public string Problem;
            public int Models, Textures, Animations;
            public List<string> KeptLooks = new List<string>();   // P+'s parts that keep P+'s look (moving platforms)
            public bool StaticParts;                              // some of the map's moving decorations stand still
            public int PlatformsLeftOut;                          // the map's own copies of P+'s moving platforms, not used
        }

        static IEnumerable<ResourceNode> All(ResourceNode n) { foreach (var c in n.Children) { yield return c; foreach (var d in All(c)) yield return d; } }
        static int Polys(MDL0Node m) => m.FindChild("Objects", false)?.Children.Count ?? 0;

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

        /// <param name="program">names P+'s stage program looks up (StageIndex.ProgramNames): those parts stay exactly P+'s</param>
        public static LooksResult SwapLooks(byte[] map, byte[] original, ICollection<string> program = null)
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
                            // P+'s main scenery part: the biggest part with nothing gameplay uses in it (always drawn)
                            var main = files.Where(Pure).OrderByDescending(Size).FirstOrDefault(f => Size(f) > 0);
                            if (main == null) { r.Problem = "P+'s stage keeps its scenery together with its gameplay parts, so there's no place to put the map's looks."; return r; }
                            // the map's animations go first (the one a stage plays) only where the stage program uses nothing:
                            // otherwise after P+'s, so nothing the program times its events by changes
                            bool animsFirst = main.FindChildrenByType(null, ResourceType.MDL0).All(m => !program.Contains(m.Name));
                            var pplusBones = new HashSet<string>(b.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().SelectMany(m => m.BoneCache.Select(x => x.Name)), StringComparer.Ordinal);

                            // P+'s scenery out of sight: its pieces go, its bones and names stay (so nothing the stage program looks for is missing)
                            var surf = Surfaces(original);
                            float width = surf.Count == 0 ? 0 : surf.Max(s => Math.Max(s.x1, s.x2)) - surf.Min(s => Math.Min(s.x1, s.x2));
                            foreach (var f in files)
                                foreach (var m in f.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0).ToList())
                                {
                                    if (keep.Contains(m.Name))
                                    {
                                        // a moving platform keeps P+'s look (the map's version wouldn't move with it); a part the whole stage rides on goes
                                        float w = 0;
                                        try { m.Populate(); var box = m.GetBox(); w = box.Max._x - box.Min._x; } catch { }
                                        if (width <= 0 || w < width * 0.5f) { r.KeptLooks.Add(m.Name); continue; }
                                    }
                                    foreach (var obj in m.FindChildrenByType(null, ResourceType.MDL0Object).OfType<MDL0ObjectNode>().ToList())
                                        obj.Remove(true, true, true, true, true, true, true, true, true, true, true, true);
                                }

                            // the map's looks: every model with something to draw that its own gameplay doesn't use
                            var mapKeep = GameplayModels(a);
                            // the map's own copy of a moving platform (it has the bone P+'s platform floor rides on): left out, P+'s moves instead
                            var rideBones = new HashSet<string>(b.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>()
                                .SelectMany(c => c.Children.OfType<CollisionObject>()).Select(o => o._boneName).Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);
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
