// What a stage file holds that affects the match (so it must stay exactly as P+ has it for online
// play), as opposed to its looks (which a reskin may change freely):
//  - collision (floors, walls, platforms) and the surface types,
//  - the stage's settings blocks (stage parameters, hazards/traps, cameos, item spawns...),
//  - position markers: blast zones, camera limits, spawn and respawn points (the "stagePosition" model etc.),
//  - for platforms that move: the bone they ride on, its parents, and their animations.
// Textures, models, materials, lighting, effects and texture/colour animations are free to change.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BrawlLib.SSBB.ResourceNodes;

namespace SkinManager.Core
{
    public static partial class StageCheck
    {
        /// <summary>The largest stage file P+ ships (Castle Siege's alt). Bigger files may not fit in the game's memory.</summary>
        public const long MaxStageBytes = 4_660_608;

        /// <summary>Gameplay parts of a stage file, each reduced to a fingerprint. Two files with the same
        /// fingerprints play the same.</summary>
        public static Dictionary<string, string> Gameplay(byte[] stage)
        {
            Installer.Quiet();
            using (var tmp = new TempFile(".pac", stage))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path))
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (root == null) throw new InvalidDataException("This isn't a stage file.");

                // 1) every settings/collision block (anything that isn't models, textures or effects).
                //    Compared by content, not by exact bytes, so a file re-saved by a newer tool still matches.
                var brresList = new List<BRRESNode>();
                foreach (var (key, node) in GameplayNodes(root, brresList))
                {
                    if (key.EndsWith("/")) continue;   // a folder: its contents are listed one by one
                    if (node is CollisionNode col)
                    {
                        result["collision " + key] = Hash(CollisionText(col, false));
                        result["material " + key] = Hash(CollisionText(col, true));
                    }
                    else result[Category(node) + " " + key] = Hash(BlockBytes(node));
                }
                // the shape of the fixed floors and walls, for finding P+ stages shaped like a map (not part of the comparison)
                result["outline"] = Outline(root);
                var models = brresList.SelectMany(br => br.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>()).ToList();
                var anims = brresList.SelectMany(br => br.FindChildrenByType(null, ResourceType.CHR0).OfType<CHR0Node>()).ToList();

                // 2) position markers (blast zones, camera, spawns)
                foreach (var m in models.Where(IsPositionModel))
                    result["positions " + m.Name] = Hash(BoneText(m.BoneCache.Cast<MDL0BoneNode>()));

                // 3) every model in detail: which part of the file it's in, its bones, and the animations next to it. Only
                //    the models a stage's program works with by name count (Compare's "program"): Green Hill's program
                //    runs its stage events from StgGreenhillMain, so a map with its own StgGreenhillMain plays differently.
                foreach (var br in brresList)
                {
                    int slot = br is ARCEntryNode e ? e.FileIndex : -1;
                    var slotAnims = br.Children.SelectMany(g => g.Children).Where(x => x is CHR0Node || x is VIS0Node).ToList();   // in file order
                    foreach (var m in br.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                    {
                        var bones = new HashSet<string>(m.BoneCache.Select(x => x.Name), StringComparer.Ordinal);
                        var text = new StringBuilder("slot " + slot + "\n" + BoneText(m.BoneCache.Cast<MDL0BoneNode>()));
                        // the animations that move its bones: how long they are and which bones they move (stage events are timed by them)
                        foreach (var an in slotAnims)
                        {
                            var moved = an.Children.Select(x => x.Name).Where(bones.Contains).OrderBy(x => x, StringComparer.Ordinal).ToList();
                            if (moved.Count == 0 && !an.Name.StartsWith(m.Name, StringComparison.Ordinal)) continue;
                            text.Append(an is CHR0Node ? "chr " : "vis ").Append(an.Name).Append(' ').Append(FrameInfo(an)).Append(' ').Append(string.Join(",", moved)).Append('\n');
                        }
                        string key = "model " + m.Name;
                        if (result.ContainsKey(key)) key += "@" + slot;
                        result[key] = Hash(text.ToString());
                    }
                }

                // 4) moving platforms: the bones collision rides on, their parents and their animations
                foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                    foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName)))
                    {
                        var model = models.FirstOrDefault(m => m.Name == o._modelName);
                        var bone = model?.BoneCache.Cast<MDL0BoneNode>().FirstOrDefault(b => b.Name == o._boneName);
                        string key = $"platform {o._modelName}/{o._boneName}";
                        if (bone == null) { result[key] = "missing"; continue; }
                        var chain = new List<MDL0BoneNode>();
                        for (var b = bone; b != null; b = b.Parent as MDL0BoneNode) chain.Add(b);
                        var text = new StringBuilder(BoneText(chain));
                        foreach (var a in anims.Where(a => a.Name.StartsWith(o._modelName, StringComparison.Ordinal)).OrderBy(a => a.Name))
                        {
                            text.Append("anim ").Append(a.Name).Append(' ').Append(a.FrameCount).Append(' ').Append(a.Loop).Append('\n');
                            foreach (var b in chain)
                            {
                                var e = a.FindChild(b.Name, false) as CHR0EntryNode;
                                if (e == null) continue;
                                for (int f = 0; f < Math.Max(1, a.FrameCount); f++)
                                {
                                    var fr = e.GetAnimFrame(f);
                                    text.Append(b.Name).Append(':').Append(f).Append(' ').Append(V(fr.Scale)).Append(V(fr.Rotation)).Append(V(fr.Translation)).Append('\n');
                                }
                            }
                        }
                        result[key] = Hash(Encoding.UTF8.GetBytes(text.ToString()));
                    }
                return result;
            }
        }

        /// <summary>The gameplay blocks of a stage file, each with a unique key (its place in the file;
        /// "#2" marks a second block with the same name). Folders are walked into; models are skipped.</summary>
        static List<(string key, ResourceNode node)> GameplayNodes(ResourceNode root, List<BRRESNode> brresOut = null)
        {
            var list = new List<(string, ResourceNode)>();
            void Walk(ResourceNode n, string path)
            {
                var seen = new Dictionary<string, int>();
                foreach (var c in n.Children)
                {
                    string name = c.Name ?? "";
                    seen[name] = seen.TryGetValue(name, out int k) ? k + 1 : 1;
                    string p = path + "/" + name + (seen[name] > 1 ? "#" + seen[name] : "");
                    if (c is BRRESNode brres) { brresOut?.Add(brres); continue; }
                    if (IsLooksOnly(c)) continue;
                    if (c is ARCNode) { list.Add((p + "/", c)); Walk(c, p); continue; }
                    list.Add((p, c));
                }
            }
            Walk(root, "");
            return list;
        }

        /// <summary>What kind of gameplay data a block holds (for plain-language messages).</summary>
        static string Category(ResourceNode n)
        {
            switch (n.ResourceFileType)
            {
                case ResourceType.STPM: return "camera";
                case ResourceType.SCLA: return "surfaces";
                default:
                    string name = (n.Name ?? "").ToLowerInvariant();
                    if (name.Contains("item")) return "items";
                    return "settings";
            }
        }

        /// <summary>A block's content: its entries one by one (padding and layout don't count).</summary>
        static byte[] BlockBytes(ResourceNode n)
        {
            if (n.Children.Count == 0) return Raw(n);
            using (var ms = new MemoryStream())
            {
                foreach (var c in n.Children)
                {
                    var name = Encoding.UTF8.GetBytes(c.Name ?? ""); ms.Write(name, 0, name.Length); ms.WriteByte(0);
                    var b = BlockBytes(c); ms.Write(b, 0, b.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>The collision as geometry: every surface's two ends, kind and material, per object.</summary>
        /// <summary>The collision as geometry (shape, kinds, ledges, drop-through...), or with materials = true
        /// each surface's material (grass, metal, ice...: footstep sounds and effects, and how slippery it is).</summary>
        static string CollisionText(CollisionNode col, bool materials)
        {
            var sb = new StringBuilder();
            foreach (var o in col.Children.OfType<CollisionObject>())
            {
                sb.Append("object ").Append(o._modelName).Append('/').Append(o._boneName).Append(' ').Append((int)o._flags).Append('\n');
                var planes = o._planes.Select(pl => string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00}>{2:0.00},{3:0.00} {4}",
                    pl._linkLeft?._rawValue._x ?? 0, pl._linkLeft?._rawValue._y ?? 0, pl._linkRight?._rawValue._x ?? 0, pl._linkRight?._rawValue._y ?? 0,
                    materials ? pl._material.ToString() : (int)pl._type + " " + (int)pl._typeFlags + " " + (int)pl._materialFlags)).OrderBy(x => x, StringComparer.Ordinal);
                foreach (var pl in planes) sb.Append(pl).Append('\n');
            }
            return sb.ToString();
        }

        static bool IsLooksOnly(ResourceNode n)
        {
            if (n.Name != null && n.Name.StartsWith("ef_", StringComparison.OrdinalIgnoreCase)) return true;   // effects
            switch (n.ResourceFileType)
            {
                case ResourceType.EFLS: case ResourceType.REFF: case ResourceType.REFT: case ResourceType.MSBin:
                case ResourceType.Redirect:
                    return true;
            }
            return false;
        }

        static bool IsPositionModel(MDL0Node m)
        {
            // position markers are bones only: a model with anything to draw is scenery, whatever its name
            if ((m.FindChild("Objects", false)?.Children.Count ?? 0) > 0) return false;
            string n = (m.Name ?? "").ToLowerInvariant();
            return n.Contains("position") || n.EndsWith("_pos") || n.StartsWith("poketrainer");
        }

        static string Clean(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"^/[^/]*", "");   // the file's own name doesn't matter

        static byte[] Raw(ResourceNode n)
        {
            var src = n.WorkingUncompressed;
            var bytes = new byte[src.Length];
            if (src.Length > 0) System.Runtime.InteropServices.Marshal.Copy((IntPtr)src.Address, bytes, 0, src.Length);
            return bytes;
        }

        static string V(BrawlLib.Internal.Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000};", v._x, v._y, v._z);

        static string BoneText(IEnumerable<MDL0BoneNode> bones)
        {
            var sb = new StringBuilder();
            foreach (var b in bones.OrderBy(b => b.Name, StringComparer.Ordinal))
            {
                // the game works bones out from scale/rotation/translation; the stored matrices are often stale in stage files
                sb.Append(b.Name).Append('<').Append((b.Parent as MDL0BoneNode)?.Name ?? "").Append(' ').Append(b.Visible ? 'v' : 'h').Append(' ')
                  .Append(V(b.Scale)).Append(V(b.Rotation)).Append(V(b.Translation)).Append('\n');
            }
            return sb.ToString();
        }

        static string Hash(byte[] b) { using (var s = SHA1.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", ""); }
        static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));

        /// <summary>What differs between two fingerprints, in words ("blast zones or spawn points", ...).</summary>
        /// <param name="program">names the stage's program looks up (StageIndex.ProgramNames); null: models aren't compared</param>
        public static List<string> Differences(Dictionary<string, string> mine, Dictionary<string, string> original, ICollection<string> program = null)
        {
            var differ = original.Keys.Union(mine.Keys).Where(k => !mine.TryGetValue(k, out var a) || !original.TryGetValue(k, out var b) || a != b).ToList();
            var what = new List<string>();
            var parts = ProgramParts(differ, program);
            if (parts.Count > 0) what.Add("parts the stage program works with directly (" + string.Join(", ", parts.Take(3)) + "), which drive stage events");
            if (differ.Any(k => k.StartsWith("collision"))) what.Add("the shape of the floors and platforms");
            if (differ.Any(k => k.StartsWith("material"))) what.Add("floor materials (footstep sounds, and how slippery they are)");
            if (differ.Any(k => k.StartsWith("positions"))) what.Add("blast zones, camera limits or spawn points");
            if (differ.Any(k => k.StartsWith("platform"))) what.Add("how the moving platforms move");
            if (differ.Any(k => k.StartsWith("surfaces"))) what.Add("surface types (grip, ice...)");
            if (differ.Any(k => k.StartsWith("camera"))) what.Add("camera settings");
            if (differ.Any(k => k.StartsWith("items"))) what.Add("item spawns");
            if (differ.Any(k => k.StartsWith("settings"))) what.Add("other stage settings (hazards and the like)");
            if (differ.Any(k => k == "unreadable")) what.Add("(the file couldn't be read)");
            return what;
        }

        public static bool FloorsDiffer(Dictionary<string, string> mine, Dictionary<string, string> original) =>
            original.Keys.Union(mine.Keys).Where(k => k.StartsWith("collision")).Any(k => !mine.TryGetValue(k, out var a) || !original.TryGetValue(k, out var b) || a != b);

        /// <summary>Compares a stage file with the one it would replace. Passed = plays exactly the same.</summary>
        /// <summary>Raise this whenever Gameplay() changes what it reads or how: saved fingerprints of an older version are
        /// made again (v5: moving platforms include every animation of their model, like StgVillageFuyuuAshiba_Half).</summary>
        public const int FingerprintVersion = 8;   // v7: every model in detail ("model ..."), for the parts stage programs use   // v6: "outline" (the floors' shape) added

        /// <summary>The program-used models among differing fingerprint keys.</summary>
        static List<string> ProgramParts(IEnumerable<string> differ, ICollection<string> program)
        {
            if (program == null) return new List<string>();
            return differ.Where(k => k.StartsWith("model ", StringComparison.Ordinal)).Select(k => k.Substring(6).Split('@')[0])
                .Where(program.Contains).Distinct().ToList();
        }

        static string FrameInfo(ResourceNode an)
        {
            var t = an.GetType();
            var fc = t.GetProperty("FrameCount"); var lp = t.GetProperty("Loop");
            return (fc != null ? fc.GetValue(an, null)?.ToString() : "") + " " + (lp != null ? lp.GetValue(an, null)?.ToString() : "");
        }

        public static CheckResult Compare(Dictionary<string, string> mine, Dictionary<string, string> original, string originalName, ICollection<string> program = null)
        {
            var what = Differences(mine, original, program);
            if (what.Count == 0)
                return new CheckResult { Passed = true, Title = "Plays exactly like " + originalName, Detail = "Floors, platforms, blast zones, spawn points and moving parts are identical. Only the looks change, so it's safe online." };
            return new CheckResult
            {
                Passed = false, Title = "Plays differently from " + originalName,
                Detail = "It changes " + string.Join(", ", what) + ". Online, your game and your opponent's would disagree about where things are and the match would go out of sync.",
            };
        }

        public sealed class Fit
        {
            public float Floors;   // furthest the reskin's walkable tops are from P+'s
            public float Rest;     // furthest its sides and underside are
            public float Width;    // P+'s stage width, for scale
            public bool Exact => Floors < 0.5f && Rest < 0.5f;
        }

        /// <summary>How far off a reskin's looks would be on P+'s stage. A stage's own collision is built to match
        /// its scenery, so comparing the reskin's collision with P+'s shows where the scenery won't line up.
        /// Only the fixed (non-moving) parts are compared.</summary>
        public static Fit HowFarOff(byte[] reskin, byte[] original) => FitOf(Surfaces(reskin), Surfaces(original));

        static Fit FitOf(List<(float x1, float y1, float x2, float y2, bool floor)> a, List<(float x1, float y1, float x2, float y2, bool floor)> b)
        {
            var fit = new Fit();
            if (a.Count == 0 || b.Count == 0) { fit.Floors = fit.Rest = float.MaxValue; return fit; }
            fit.Width = b.Max(x => Math.Max(x.x1, x.x2)) - b.Min(x => Math.Min(x.x1, x.x2));
            void Measure(List<(float x1, float y1, float x2, float y2, bool floor)> from, List<(float x1, float y1, float x2, float y2, bool floor)> to)
            {
                foreach (var sgm in from)
                {
                    float len = (float)Math.Sqrt((sgm.x2 - sgm.x1) * (sgm.x2 - sgm.x1) + (sgm.y2 - sgm.y1) * (sgm.y2 - sgm.y1));
                    int n = Math.Max(1, (int)(len / 0.5f));
                    for (int i = 0; i <= n; i++)
                    {
                        float px = sgm.x1 + (sgm.x2 - sgm.x1) * i / n, py = sgm.y1 + (sgm.y2 - sgm.y1) * i / n;
                        float d = to.Min(t => Distance(px, py, t.x1, t.y1, t.x2, t.y2));
                        if (sgm.floor) fit.Floors = Math.Max(fit.Floors, d); else fit.Rest = Math.Max(fit.Rest, d);
                    }
                }
            }
            Measure(a, b); Measure(b, a);
            return fit;
        }

        /// <summary>Copies the pictures (and palettes) the models in some parts of file a use from file b, where a has none by that name.</summary>
        static void BringPictures(ResourceNode a, ResourceNode b, List<BRRESNode> parts)
        {
            string dir = Path.Combine(Path.GetTempPath(), "PPlusModdingTool", "pics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var have = new HashSet<string>(a.FindChildrenByType(null, ResourceType.TEX0).Select(t => t.Name)
                    .Concat(a.FindChildrenByType(null, ResourceType.PLT0).Select(t => t.Name).Select(n => "plt:" + n)), StringComparer.Ordinal);
                int file = 0;
                foreach (var part in parts)
                    foreach (var m in part.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>())
                    {
                        m.Populate();
                        foreach (var (name, plt) in (m.TextureList ?? new List<ResourceNode>()).Select(t => (t.Name, false)).Concat((m.PaletteList ?? new List<ResourceNode>()).Select(t => (t.Name, true))))
                        {
                            if (!have.Add((plt ? "plt:" : "") + name)) continue;
                            var src = (plt ? b.FindChildrenByType(null, ResourceType.PLT0) : b.FindChildrenByType(null, ResourceType.TEX0)).FirstOrDefault(t => t.Name == name) as BRESEntryNode;
                            if (src == null) continue;
                            string p = Path.Combine(dir, (file++) + (plt ? ".plt0" : ".tex0"));
                            src.Export(p);
                            var copy = NodeFactory.FromFile(null, p);
                            if (copy is TEX0Node tx) part.GetOrCreateFolder<TEX0Node>().AddChild(tx);
                            else if (copy is PLT0Node pl) part.GetOrCreateFolder<PLT0Node>().AddChild(pl);
                        }
                    }
            }
            finally { _cleanup.Add(dir); }
        }
        static readonly List<string> _cleanup = new List<string>();

        static float Distance(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1, dy = y2 - y1, l = dx * dx + dy * dy;
            float t = l < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / l));
            float qx = x1 + dx * t - px, qy = y1 + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        static List<(float x1, float y1, float x2, float y2, bool floor)> Surfaces(byte[] stage)
        {
            using (var tmp = new TempFile(".pac", stage))
            lock (BrawlGate.Lock) using (var root = NodeFactory.FromFile(null, tmp.Path)) return Surfaces(root);
        }

        /// <summary>A stage's fixed surfaces (moving platforms left out).</summary>
        internal static List<(float x1, float y1, float x2, float y2, bool floor)> Surfaces(ResourceNode root, bool moving = false)
        {
            var list = new List<(float, float, float, float, bool)>();
                foreach (var col in root.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                    foreach (var o in col.Children.OfType<CollisionObject>().Where(o => moving || string.IsNullOrEmpty(o._boneName)))
                        foreach (var pl in o._planes.Where(pl => pl._linkLeft != null && pl._linkRight != null))
                            list.Add((pl._linkLeft._rawValue._x, pl._linkLeft._rawValue._y, pl._linkRight._rawValue._x, pl._linkRight._rawValue._y,
                                      pl._type == BrawlLib.SSBB.Types.CollisionPlaneType.Floor));
            return list;
        }

        /// <summary>Makes a reskin play exactly like P+'s stage: P+'s gameplay parts (collision, settings,
        /// blast zones and spawns, moving platforms) are put into the reskin; its looks stay. Returns the new
        /// file, or null with the reason when the reskin is missing a part it would need.</summary>
        public static byte[] MakeMatch(byte[] reskin, byte[] original, out string problem, ICollection<string> program = null) => MakeMatch(reskin, original, out problem, out _, program);

        /// <param name="looks">parts of the reskin's looks that had to be swapped for P+'s (a moving platform's model)</param>
        public static byte[] MakeMatch(byte[] reskin, byte[] original, out string problem, out List<string> looks, ICollection<string> program = null)
        {
            problem = null; looks = new List<string>();
            var ga = Gameplay(reskin); var gb = Gameplay(original);
            // parts the stage program works with by name must be P+'s exactly; the map's own versions can't be swapped out
            // here without losing its scenery, so that's left to "its looks on P+'s stage"
            var parts = ProgramParts(gb.Keys.Union(ga.Keys).Where(k => !ga.TryGetValue(k, out var x) || !gb.TryGetValue(k, out var y) || x != y), program);
            if (parts.Count > 0)
            {
                problem = "The stage program works directly with parts of the map that are built differently from P+'s (" + string.Join(", ", parts.Take(3)) +
                          "). They drive stage events, so online games would go out of sync.";
                return null;
            }
            bool Same(string key) => ga.TryGetValue(key, out var x) && gb.TryGetValue(key, out var y) && x == y;
            using (var tA = new TempFile(".pac", reskin))
            using (var tB = new TempFile(".pac", original))
            using (var outFile = new TempFile(".pac"))
            {
                lock (BrawlGate.Lock)
                {
                    using (var a = NodeFactory.FromFile(null, tA.Path))
                    using (var b = NodeFactory.FromFile(null, tB.Path))
                    {
                        if (a == null || b == null) { problem = "One of the files couldn't be read."; return null; }
                        ResourceNode Find(ResourceNode root, string path)
                        {
                            ResourceNode n = root;
                            foreach (var part in path.Trim('/').Split('/'))
                            {
                                n = n.Children.FirstOrDefault(c => c.Name == part);
                                if (n == null) return null;
                            }
                            return n;
                        }
                        // a part P+'s stage has and the reskin doesn't: a copy of P+'s, with the same place in the archive.
                        // Parsed on its own: with an archive as parent BrawlLib reads an entry header in front of the data, which isn't there.
                        bool AddCopy(ResourceNode node, ResourceNode folder)
                        {
                            if (!(node is ARCEntryNode src)) return false;
                            try
                            {
                                var data = node.WorkingUncompressed;
                                var map = BrawlLib.Internal.IO.FileMap.FromTempFile(data.Length);
                                BrawlLib.Internal.Memory.Move(map.Address, data.Address, (uint)data.Length);
                                var added = NodeFactory.FromSource(null, new BrawlLib.SSBB.ResourceNodes.DataSource(map)) as ARCEntryNode;
                                if (added == null) return false;
                                folder.AddChild(added);
                                added.FileType = src.FileType; added.FileIndex = src.FileIndex; added.GroupID = src.GroupID; added.RedirectIndex = src.RedirectIndex;
                                added.Name = src.Name;
                                return true;
                            }
                            catch { return false; }
                        }
                        void Copy(ResourceNode from, ResourceNode to)
                        {
                            var data = from.WorkingUncompressed;
                            to.ReplaceRaw(data.Address, data.Length);
                        }
                        var missing = new List<string>();
                        var brought = new List<string>();
                        var bBrres = new List<BRRESNode>();
                        var bNodes = GameplayNodes(b, bBrres);
                        var aNodes = GameplayNodes(a).ToDictionary(x => x.key, x => x.node);
                        var addedFolders = new List<string>();
                        foreach (var (key, node) in bNodes)
                        {
                            if (addedFolders.Any(f => key.StartsWith(f, StringComparison.Ordinal))) continue;   // came along with its folder
                            if (!aNodes.TryGetValue(key, out var target))
                            {
                                // a block P+'s stage has and the reskin doesn't: add a copy of P+'s
                                string parentKey = key.EndsWith("/") ? key.Substring(0, key.TrimEnd('/').LastIndexOf('/') + 1) : key.Substring(0, key.LastIndexOf('/') + 1);
                                if (aNodes.TryGetValue(parentKey, out var folder) && AddCopy(node, folder))
                                {
                                    if (key.EndsWith("/")) addedFolders.Add(key);   // a whole folder (an item spawner): its contents came with it
                                    continue;
                                }
                                missing.Add(node.Name); continue;
                            }
                            if (!key.EndsWith("/")) Copy(node, target);
                        }
                        // gameplay blocks the reskin has and P+'s stage doesn't (an extra floor, ...) are taken out
                        var bKeys = new HashSet<string>(bNodes.Select(x => x.key));
                        foreach (var kv in aNodes.Where(kv => !bKeys.Contains(kv.Key)).OrderByDescending(kv => kv.Key.Length).ToList())
                            if (kv.Value.Parent != null) kv.Value.Remove();
                        var bModels = bBrres.SelectMany(br => br.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Select(m => (model: m, brres: (ResourceNode)br))).ToList();
                        // position markers and the models moving platforms ride on, where they differ: P+'s model file.
                        // Only when that file holds nothing else of the reskin's looks; otherwise the fix would paste
                        // P+'s scenery over the reskin's, so it isn't done.
                        int Polys(MDL0Node m) => m.FindChild("Objects", false)?.Children.Count ?? 0;
                        var needed = new HashSet<ResourceNode>();
                        var gameplayModels = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var (m, brres) in bModels)
                            if (IsPositionModel(m)) { gameplayModels.Add(m.Name); if (!Same("positions " + m.Name)) needed.Add(brres); }
                        foreach (var col in b.FindChildrenByType(null, ResourceType.CollisionDef).OfType<CollisionNode>())
                            foreach (var o in col.Children.OfType<CollisionObject>().Where(o => !string.IsNullOrEmpty(o._modelName)))
                            {
                                gameplayModels.Add(o._modelName);
                                if (Same($"platform {o._modelName}/{o._boneName}")) continue;
                                foreach (var (m, brres) in bModels.Where(x => x.model.Name == o._modelName)) needed.Add(brres);
                            }
                        foreach (var brres in needed)
                        {
                            string path = "";
                            for (var n = brres; n != null && n != b; n = n.Parent) path = "/" + n.Name + path;
                            var target = Find(a, path);
                            if (target == null)
                            {
                                // P+ keeps this moving part in a model file the reskin doesn't have: add P+'s (it brings P+'s look for that part)
                                string parentPath = path.Substring(0, path.LastIndexOf('/'));
                                var parent = parentPath == "" ? a : Find(a, parentPath);
                                if (parent != null && AddCopy(brres, parent))
                                {
                                    var addedNode = Find(a, path);
                                    brought.Add(path);
                                    var names = brres.FindChildrenByType(null, ResourceType.MDL0).Select(m => m.Name).ToList();
                                    foreach (var m in brres.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0)) looks.Add("+" + m.Name);
                                    // the reskin's own copy of that part (same name, elsewhere) goes, so there's only one for the platform to ride on
                                    bool Inside(ResourceNode n) { for (var x = n; x != null; x = x.Parent) if (x == addedNode) return true; return false; }
                                    foreach (var old in a.FindChildrenByType(null, ResourceType.MDL0).Where(m => names.Contains(m.Name) && !Inside(m)).ToList())
                                    {
                                        if (Polys((MDL0Node)old) > 0) looks.Add(old.Name);
                                        old.Remove();
                                    }
                                    foreach (var old in a.FindChildrenByType(null, ResourceType.CHR0).Where(c => names.Any(nm => c.Name.StartsWith(nm, StringComparison.Ordinal)) && !Inside(c)).ToList())
                                        old.Remove();
                                    continue;
                                }
                                missing.Add(brres.Name); continue;
                            }
                            var theirNames = new HashSet<string>(brres.FindChildrenByType(null, ResourceType.MDL0).Select(x => x.Name), StringComparer.Ordinal);
                            var lost = target.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>()
                                .Where(m => Polys(m) > 0 && !theirNames.Contains(m.Name)).Select(m => m.Name).ToList();
                            if (lost.Count > 0)
                            {
                                problem = "Its moving platforms are built into the same part of the file as its scenery (" + string.Join(", ", lost.Take(3)) +
                                          "), so P+'s platforms can't be put in without replacing that scenery.";
                                return null;
                            }
                            foreach (var m in target.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => Polys(m) > 0))
                                looks.Add(m.Name);
                            Copy(brres, target);
                            brought.Add(path);
                        }
                        // position markers only the reskin has (bones, nothing to draw) are left over from another stage: out
                        foreach (var m in a.FindChildrenByType(null, ResourceType.MDL0).OfType<MDL0Node>().Where(m => IsPositionModel(m) && !gameplayModels.Contains(m.Name)).ToList())
                            if (!bModels.Any(x => x.model.Name == m.Name)) m.Remove();
                        // the pictures P+'s parts are painted with (they live in P+'s picture files, not next to the parts): copied
                        // over when the map doesn't have a picture by that name, so P+'s platform isn't left unpainted
                        BringPictures(a, b, brought.Select(x => Find(a, x)).OfType<BRRESNode>().ToList());
                        if (missing.Count > 0) { problem = "P+ has rebuilt parts of this stage since the map was made (like its moving platforms), and the map doesn't have the pieces P+'s version needs (" + string.Join(", ", missing.Take(3)) + ")."; return null; }
                        a.Export(outFile.Path);
                    }
                }
                foreach (var d in _cleanup.ToList()) { try { Directory.Delete(d, true); } catch { } _cleanup.Remove(d); }
                return File.ReadAllBytes(outFile.Path);
            }
        }

    }
}
