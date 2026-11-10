// The look lab (docs/characters.md "The look"): the candidate character looks seen the way the game
// will mostly see them, in first person, at the distances that matter (a zombie at arm's length, across
// a room, down a street), with their triangle and texture budgets on screen and measured.
//
// Candidates (art/scripts/build_lookdev.py, each a ladder of LOD meshes on the shared rig):
//   mannequin  the segmented test mannequin (~2k triangles, flat colour): where we are
//   pz         "Zomboid-like": 2.5k / 1.2k / 600, a 512 painted texture with strong baked shading
//   plus       "Zomboid-plus": 30k / 12k / 6k / 3k / 1.2k / 600, a 2048 texture and a baked normal map
//   lineup     the four top looks side by side (mannequin, pz 2.5k, plus 12k, plus 30k)
//   faces      the zombie's five face candidates side by side (plus, near LOD)
//   clothes    the survivor painted, in a jacket, in a hoodie (garment meshes with volume) side by side
//   hair       the survivor with each hair style side by side (hair meshes, attached and tinted)
//   hats       every hair style under the beanie (its under-hat version), as the game draws them (pz)
//   people     survivors dressed as the world dresses them (Looks.cs: hair, beard, hat by a hash of the id)
//   dead       the dead dressed as the world dresses them (an outfit, its colours, hair, beard, hat by id)
//   stumps     every lost limb the sim can say (Wounds.lostLimb: an arm below the elbow, a leg below the
//              knee, either side), on the survivor in a jacket (pinned sleeve, knotted trouser leg) and the
//              dead (torn), cut at load (StumpCut.cs)
// Faces: the zombie's top LODs carry two blend shapes, jaw_open and angry. Neutral is a slack jaw;
// angry (it has seen you) eases in over a quarter second with a growl. Lower LODs carry no face.
//
// LOD is picked by the character's height on screen (auto), or forced. Godot's own mesh LOD is off
// (the viewport's threshold 0) so the counts are the ladder's, unless "Godot LOD" is ticked.
//
//   Godot --path godot res://Labs/LookLab.tscn [-- options]
//   --subject=zombie|survivor --cand=plus|pz|mannequin|lineup --lod=auto|0..5 --dist=M
//   --face=neutral|angry|auto --move=idle|walk|lunge --light=day|dusk|night --wire --turn=DEG
//   --zface=feral|slack|gaunt|bloated|torn (the zombie's face) --clothes=painted|jacket|hoodie (the survivor's)
//   --hair=none|buzz|crop|sidepart|long|receding|ponytail|matted --haircolour=black|brown|blond|red|grey --beard
//   --hat=none|beanie --hatcolour=charcoal|olive|navy|red (the hair under it swaps to its under-hat version)
//   --lose=armLeft:elbow,legRight:knee  lost limbs (the sim's lostLimbs), cut at load (StumpCut.cs)
//   --aimat=H                        look at height H on the subject (m; else the face up close, the chest further off)
//   --crowd=N [--crowdlod=auto|K]   N more of the candidate, walking, in a field ahead
//   --shot=out.png [--frames=N]      save a screenshot after N frames and quit
//   --shots=DIR                      the comparison set (candidates x distances x faces), then quit
//   --report=DIR                     the budget sweep (candidates x LODs x crowd sizes), look.md, then quit
//   --tracehair                      print each character's hair triangles by LOD, and what a stump cut cost
//   --crowdbench=FILE                the dead's crowd, as before this outfit work (one look per face, matted grey
//                                    hair) and dressed as the world dresses them, 40 and 200 walking: frame,
//                                    GPU and render CPU ms, draw calls, materials; a table to FILE, then quit
//   --novsync                        frame times uncapped; --nopanel hides the panel in a --shot; --noaim looks level
//
// Keys: wheel or [ ] distance, 1-6 distance presets, F face, L LOD (auto, 0..), V wireframe, T turn, right-drag look.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class LookLab : Node3D
{
    const string MannequinPath = "res://assets/characters/test_mannequin.glb";
    const string ClipsPath = "res://assets/characters/clips_ual.glb";
    static string CandPath(string cand, string subject, string variant)
    {
        string plain = $"res://assets/characters/look/look_{cand}_{subject}.glb";
        if (variant is "" or DefaultFace or "painted") return plain;
        string path = plain.Replace(".glb", $"_{variant}.glb");
        return ResourceLoader.Exists(path) ? path : plain;  // the variants are built for plus only
    }

    static readonly string[] Cands = { "mannequin", "pz", "plus", "lineup", "faces", "clothes", "hair", "hats", "people", "dead", "stumps" };
    // Lost limbs (the sim's lostLimbs: Wounds.lostLimb cuts arms below the elbow, legs below the knee; crawlers have lost both legs).
    static readonly string[] Losses = { "none", "armLeft:elbow", "armRight:elbow", "legLeft:knee", "legRight:knee", "legLeft:knee,legRight:knee", "armLeft:elbow,legRight:knee" };
    // The stumps lineup: (subject, clothes or face, lost limbs, label).
    static readonly (string subj, string variant, string limbs, string tag)[] StumpCases =
    {
        ("survivor", "jacket", "armLeft:elbow", "arm L"), ("survivor", "jacket", "armRight:elbow", "arm R"),
        ("survivor", "jacket", "legLeft:knee", "leg L"), ("survivor", "jacket", "legRight:knee", "leg R"),
        ("survivor", "hoodie", "armLeft:elbow,legRight:knee", "hoodie"), ("zombie", "feral", "armRight:elbow", "dead arm R"),
        ("zombie", "feral", "legLeft:knee,legRight:knee", "crawler"),
    };
    static readonly string[] Faces = { "feral", "slack", "gaunt", "bloated", "torn" };
    const string DefaultFace = "feral";  // chosen 2026-11-08; the others are kept to compare
    static readonly string[] ClothesSets = { "painted", "jacket", "hoodie" };
    // Hair (art/scripts/build_hair.py): each style its own mesh, attached to the character's skeleton and
    // tinted (the texture is grey). A survivor's scalp is painted skin: "none" is shaved bald.
    static readonly string[] HairStyles = { "none", "buzz", "crop", "sidepart", "long", "receding", "ponytail", "matted" };
    static readonly string[] Hats = { "none", "beanie" };
    // The palettes are the world's (Looks.cs); a style under a hat is its own file (flattened where the hat
    // covers it, no fringe cards). The pz candidate wears the hair ladder's 3rd to 5th levels, as in the game.
    static readonly string[] HatColours = Looks.HAT_COLOURS.Select(x => x.name).ToArray();
    static readonly string[] HairColours = Looks.HAIR_COLOURS.Select(x => x.name).ToArray();
    // How far each face's jaw hangs at rest (jaw_open), neutral.
    static readonly Dictionary<string, double> Slack = new() { ["slack"] = 0.12, ["gaunt"] = 0.22, ["bloated"] = 0.06, ["torn"] = 0.18, ["feral"] = 0.32 };
    static readonly double[] Presets = { 1, 2, 3, 5, 10, 30 };
    const double EyeHeight = 1.65, Fov = 60;  // vertical FOV: ~91 degrees across at 16:9

    // Auto LOD: the lowest screen height (pixels) at which each LOD is shown.
    static readonly Dictionary<string, double[]> LodAt = new()
    {
        ["plus"] = new double[] { 700, 340, 170, 85, 40, 0 },
        ["pz"] = new double[] { 260, 110, 0 },
        ["mannequin"] = new double[] { 0 },
    };

    string subject = "zombie", cand = "plus", face = "auto", move = "idle", light = "day", faceVariant = DefaultFace, clothesSet = "painted";
    double turn;  // the subject turned from facing you, degrees
    string hairStyle = "crop", hairColour = "brown", hat = "none", hatColour = "charcoal";  // the survivor's; a zombie's is matted
    bool beard;
    string lose = "";  // the lost limbs, comma-separated (the sim's form)
    double forceJaw = -1, aimAt = -1;  // --aimat=H: look at this height on the subject (m)
    bool noNormal, noAlbedo;  // --nonormal, --noalbedo: the body's maps off one at a time, to find what a mark is  // --jaw=V holds jaw_open (for checking the key)
    int forcedLod = -1, crowdLod = -1, crowdCount;
    double dist = 3, lodBias = 1, sightRange = 12;
    bool godotLod, wire, aim = true;
    CanvasLayer panelLayer = null!;

    Camera3D cam = null!;
    double lookYaw, lookPitch;
    bool looking;
    DirectionalLight3D sun = null!;
    SpotLight3D torch = null!;
    WorldEnvironment env = null!;
    readonly List<Actor> subjects = new(), crowd = new();
    Node3D crowdRoot = null!;
    AnimationLibrary mannequinLib = null!, ualLib = null!;
    Label readout = null!;
    double lungeZ = -8, time;
    string? shotPath;
    int shotFrames = 30, frame;
    bool busy, hidePanel;  // busy: a sweep (shots, report) drives the lab

    /// <summary>One character on stage: the instance, its LOD meshes, its player, its face.</summary>
    sealed class Actor
    {
        public Node3D Root = null!;
        public string Cand = "";
        public MeshInstance3D[][] Lods = Array.Empty<MeshInstance3D[]>();  // each LOD's pieces (body, garments)
        public int[] Tris = Array.Empty<int>();
        public string Subject = "", Variant = "";
        public AnimationPlayer Player = null!;
        public int Shown = -1;
        public double Angry, Phase;
        public Label3D? Tag;
        public long TexBytes;
        public string TexDesc = "";
        public double CutMs;
        public int[] HairTris = Array.Empty<int>();
        public string CutDesc = "";
    }

    public override void _Ready()
    {
        RenderingServer.SetDebugGenerateWireframes(true);
        var args = ParseArgs();
        args.TryGetValue("shot", out shotPath);
        if (args.TryGetValue("frames", out var f)) shotFrames = int.Parse(f);
        if (args.TryGetValue("subject", out var s)) subject = s;
        if (args.TryGetValue("cand", out var c)) cand = c;
        if (args.TryGetValue("lod", out var l)) forcedLod = l == "auto" ? -1 : int.Parse(l);
        if (args.TryGetValue("dist", out var d)) dist = Num(d);
        if (args.TryGetValue("face", out var fc)) face = fc;
        if (args.TryGetValue("move", out var mv)) move = mv;
        if (args.TryGetValue("light", out var li)) light = li;
        if (args.TryGetValue("crowd", out var cr)) crowdCount = int.Parse(cr);
        if (args.TryGetValue("crowdlod", out var cl)) crowdLod = cl == "auto" ? -1 : int.Parse(cl);
        if (args.TryGetValue("zface", out var zf)) faceVariant = zf;
        if (args.TryGetValue("clothes", out var cs)) clothesSet = cs;
        if (args.TryGetValue("turn", out var tn)) turn = Num(tn);
        if (args.TryGetValue("hair", out var hs)) hairStyle = hs;
        if (args.TryGetValue("haircolour", out var hc)) hairColour = hc;
        beard = args.ContainsKey("beard");
        if (args.TryGetValue("hat", out var ht)) hat = ht;
        if (args.TryGetValue("hatcolour", out var htc)) hatColour = htc;
        if (args.TryGetValue("lose", out var lo)) lose = lo == "none" ? "" : lo;
        if (args.TryGetValue("aimat", out var am)) aimAt = Num(am);
        if (args.TryGetValue("jaw", out var jw)) forceJaw = Num(jw);
        wire = args.ContainsKey("wire");
        hidePanel = args.ContainsKey("nopanel");
        noNormal = args.ContainsKey("nonormal");
        noAlbedo = args.ContainsKey("noalbedo");
        if (args.ContainsKey("noaim")) aim = false;
        if (args.ContainsKey("novsync")) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0; }

        LoadLibraries();
        BuildStage();
        BuildPanel();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        Rebuild();
        ApplyLight();
        ApplyView();
        if (args.TryGetValue("shots", out var sd)) _ = RunShots(sd);
        else if (args.TryGetValue("report", out var rd)) _ = RunReport(rd);
        else if (args.TryGetValue("crowdbench", out var cb)) _ = RunCrowdBench(cb);
    }

    // ------------------------------------------------------------------ characters

    void LoadLibraries()
    {
        var m = GD.Load<PackedScene>(MannequinPath).Instantiate<Node3D>();
        mannequinLib = Find<AnimationPlayer>(m)!.GetAnimationLibrary("");
        foreach (var n in new[] { "idle", "walk" }) mannequinLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        m.Free();
        var u = GD.Load<PackedScene>(ClipsPath).Instantiate<Node3D>();
        ualLib = Find<AnimationPlayer>(u)!.GetAnimationLibrary("");
        foreach (var n in new[] { "ual_idle", "ual_walk" }) ualLib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        u.Free();
    }

    Actor MakeActor(string which, Node parent, string? subj = null, string? variant = null, string? hair = null, string? limbs = null, Looks.Hair? dress = null, Looks.Outfit? outfit = null)
    {
        if (outfit is { } o) dress = o.hair;
        subj ??= subject;
        variant ??= subj == "zombie" ? faceVariant : clothesSet;
        string path = which == "mannequin" ? MannequinPath : CandPath(which, subj, variant);
        var root = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        parent.AddChild(root);
        var a = new Actor { Root = root, Cand = which, Subject = subj, Variant = path.Contains("_" + variant + ".glb") || variant is DefaultFace or "painted" ? variant : "(none for " + which + ")" };
        if (which == "mannequin")
        {
            // The region meshes together are its one "LOD"; the stumps stay hidden.
            var meshes = FindAll<MeshInstance3D>(root).ToList();
            foreach (var mi in meshes) mi.Visible = !mi.Name.ToString().StartsWith("stump_");
            a.Lods = new[] { meshes.Where(mi => mi.Visible).ToArray() };
            a.Tris = new[] { meshes.Where(mi => mi.Visible).Sum(Tris) };
            a.Player = Find<AnimationPlayer>(root)!;
        }
        else
        {
            a.Lods = FindAll<MeshInstance3D>(root).GroupBy(mi => LodIndex(mi.Name)).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
            a.Tris = a.Lods.Select(g => g.Sum(Tris)).ToArray();
            var player = new AnimationPlayer();
            root.AddChild(player);  // root_node ".." = the instance, where "Armature/Skeleton3D:..." starts
            player.AddAnimationLibrary("", mannequinLib);
            player.AddAnimationLibrary("ual", ualLib);
            a.Player = player;
        }
        if (which != "mannequin")
        {
            // Lost limbs: the character's own meshes cut and capped (before the hair goes on: it's never cut).
            var lost = (limbs ?? lose).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (lost.Length > 0)
            {
                ulong t0 = Godot.Time.GetTicksUsec();
                int cut = StumpCut.Apply(root, lost, subj == "zombie");
                a.CutMs = (Godot.Time.GetTicksUsec() - t0) / 1000.0;
                a.CutDesc = $"{string.Join(", ", lost)} ({cut} meshes, {a.CutMs:0.0} ms)";
                if (OS.GetCmdlineUserArgs().Contains("--tracehair")) GD.Print($"cut {subj}/{variant}: {a.CutDesc}");
                a.Lods = FindAll<MeshInstance3D>(root).GroupBy(mi => LodIndex(mi.Name)).OrderBy(g => g.Key).Select(g => g.ToArray()).ToArray();
            }
            var h = dress ?? new Looks.Hair(hair ?? (subj == "zombie" ? "matted" : hairStyle), Math.Max(0, Array.IndexOf(HairColours, hairColour)),
                hat, Math.Max(0, Array.IndexOf(HatColours, hatColour)), beard, false);
            var groups = Looks.Attach(Find<Skeleton3D>(root)!, h, a.Lods.Length, which == "pz" ? Looks.HAIR_LOD_OFFSET : 0);
            a.Lods = a.Lods.Select((g, i) => g.Concat(groups[i]).ToArray()).ToArray();
            a.Tris = a.Lods.Select(g => g.Sum(Tris)).ToArray();
            a.HairTris = groups.Select(g => g.Sum(Tris)).ToArray();
            if (outfit is { } oc) Looks.Recolour(a.Lods.SelectMany(g => g), path, oc.top, oc.bottom);
            if (OS.GetCmdlineUserArgs().Contains("--tracehair")) GD.Print($"hair {which} {h}: {string.Join(" / ", a.HairTris)} tris by body LOD; body {string.Join(" / ", a.Tris.Zip(a.HairTris, (t, x) => t - x))}");
        }
        (a.TexBytes, a.TexDesc) = Textures(a.Lods[0]);
        if (noNormal || noAlbedo)
            foreach (var mi in a.Lods.SelectMany(g => g))
                if (mi.MaterialOverride == null && mi.Mesh.SurfaceGetMaterial(0) is BaseMaterial3D bm)
                {
                    var m = (BaseMaterial3D)bm.Duplicate();
                    if (noNormal) m.NormalEnabled = false;
                    if (noAlbedo) { m.AlbedoTexture = null; m.AlbedoColor = new Color(0.7f, 0.7f, 0.7f); }
                    mi.MaterialOverride = m;
                }
        return a;
    }

    /// <summary>The character's textures as loaded (each once: the pieces share an atlas): their sizes and
    /// the memory they take with mipmaps.</summary>
    static (long bytes, string desc) Textures(MeshInstance3D[] pieces)
    {
        long bytes = 0;
        var parts = new List<string>();
        var seen = new HashSet<Rid>();
        foreach (var mi in pieces)
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            if (mi.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D m)
                foreach (var (name, tex) in new[] { ("albedo", m.AlbedoTexture), ("normal", m.NormalTexture) })
                    if (tex != null && seen.Add(tex.GetRid()) && tex.GetImage() is Image img)
                    {
                        bytes += img.GetData().Length;
                        parts.Add($"{name} {img.GetWidth()} {img.GetFormat()}{(img.HasMipmaps() ? "+mips" : "")}");
                    }
        return (bytes, parts.Count > 0 ? string.Join(", ", parts) : "none (flat colour)");
    }

    static int LodIndex(StringName name)
    {
        var s = name.ToString();
        int i = s.LastIndexOf("lod", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(s[(i + 3)..], out var n) ? n : 0;
    }

    static int Tris(MeshInstance3D mi)
    {
        int t = 0;
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = mi.Mesh.SurfaceGetArrays(s);
            var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            t += idx.Length > 0 ? idx.Length / 3 : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
        }
        return t;
    }

    void Rebuild()
    {
        foreach (var a in subjects) a.Root.QueueFree();
        subjects.Clear();
        // (look, LOD pinned or -1 for auto, label, subject, variant)
        (string c, int lod, string tag, string? subj, string? variant)[] list = cand switch
        {
            "lineup" => new (string c, int lod, string tag, string? subj, string? variant)[] { ("mannequin", 0, "mannequin", null, null), ("pz", 0, "pz", null, null), ("plus", 1, "plus", null, null), ("plus", 0, "plus", null, null) },
            "faces" => Faces.Select(f => ("plus", 0, f, (string?)"zombie", (string?)f)).ToArray(),
            "clothes" => ClothesSets.Select(c => ("plus", 0, c, (string?)"survivor", (string?)c)).ToArray(),
            "hair" => HairStyles.Select(h => ("plus", 0, h, (string?)"survivor", (string?)h)).ToArray(),
            // (hats and people: below)
            "stumps" => StumpCases.Select(x => ("pz", 0, x.tag, (string?)x.subj, (string?)x.variant)).ToArray(),
            "hats" => HairStyles.Where(h => h != "matted").Select(h => ("pz", 0, h, (string?)"survivor", (string?)h)).ToArray(),
            "people" => People().Select(x => ("pz", 0, x.tag, (string?)"survivor", (string?)x.id)).ToArray(),
            "dead" => Dead().Select(x => ("pz", 0, x.tag, (string?)"zombie", (string?)x.id)).ToArray(),
            _ => new (string c, int lod, string tag, string? subj, string? variant)[] { (cand, -1, cand, null, null) },
        };
        bool row = list.Length > 1;
        double spacing = cand is "faces" or "hair" or "hats" ? 0.62 : cand is "stumps" or "people" or "dead" ? 0.8 : 1.05;
        for (int i = 0; i < list.Length; i++)
        {
            var a = cand == "hair" ? MakeActor(list[i].c, this, "survivor", clothesSet, list[i].variant)
                : cand == "hats" ? MakeActor(list[i].c, this, "survivor", clothesSet, list[i].variant, null, new Looks.Hair(list[i].variant!, Math.Max(0, Array.IndexOf(HairColours, hairColour)), "beanie", Math.Max(0, Array.IndexOf(HatColours, hatColour)), false, false))
                : cand == "people" ? MakeActor(list[i].c, this, "survivor", Looks.LivingClothes(list[i].variant!), null, null, Looks.LivingHair(list[i].variant!))
                : cand == "dead" ? MakeActor(list[i].c, this, "zombie", Looks.DeadBody(list[i].variant!), null, null, null, Looks.DeadOutfit(list[i].variant!))
                : cand == "stumps" ? MakeActor(list[i].c, this, list[i].subj, list[i].variant, null, StumpCases[i].limbs)
                : MakeActor(list[i].c, this, list[i].subj, list[i].variant);
            if (row)
            {
                a.Shown = -2 - list[i].lod;  // pinned: -2 - lod
                a.Root.Position = new Vector3((float)((i - (list.Length - 1) / 2.0) * spacing), 0, 0);
                string label = cand == "lineup" ? $"{list[i].tag} {a.Tris[list[i].lod] / 1000.0:0.#}k" : list[i].tag;
                a.Tag = new Label3D { Text = label, PixelSize = 0.0015f, Position = new Vector3(0, 1.95f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
                a.Root.AddChild(a.Tag);
            }
            Play(a, 0);
            subjects.Add(a);
        }
        RebuildCrowd();
    }

    /// <summary>Eight survivor ids whose looks (Looks.LivingHair) show each style and a couple of hats and beards,
    /// found by trying ids in turn, labelled with what they wear.</summary>
    static List<(string id, string tag)> People()
    {
        var picked = new List<(string id, string tag)>();
        var styles = new HashSet<string>();
        int hats = 0, beards = 0;
        for (int i = 1; i < 2000 && picked.Count < 8; i++)
        {
            string id = "s" + i;
            var h = Looks.LivingHair(id);
            int bare = picked.Count - hats;
            bool wanted = h.hat != "none" ? hats < 2 : bare < 6 && (!styles.Contains(h.style) || (h.beard && beards < 2 && !styles.Contains(h.style + "b")));
            if (!wanted) continue;
            styles.Add(h.beard ? h.style + "b" : h.style);
            if (h.hat != "none") hats++;
            if (h.beard) beards++;
            string tag = $"{id}\n{h.style} {Looks.HAIR_COLOURS[h.colour].name}{(h.beard ? " +beard" : "")}{(h.hat != "none" ? $"\n{Looks.HAT_COLOURS[h.hatColour].name} {h.hat}" : "")}\n{Looks.LivingClothes(id)}";
            picked.Add((id, tag));
        }
        return picked;
    }

    /// <summary>Eight dead ids showing each outfit (and some colours, hair and hats), labelled with what they wear.</summary>
    static List<(string id, string tag)> Dead()
    {
        var picked = new List<(string id, string tag)>();
        var seen = new HashSet<string>();
        int hats = 0;
        for (int i = 1; i < 4000 && picked.Count < 8; i++)
        {
            string id = "z" + i;
            string body = Looks.DeadBody(id);
            var o = Looks.DeadOutfit(id);
            string outfitKey = Looks.FaceOf(body) == body ? "tee" : body;
            bool wanted = o.hair.hat != "none" ? hats < 1 : !seen.Contains(outfitKey) || (outfitKey == "tee" && !seen.Contains("tee:" + body) && picked.Count >= 4);
            if (!wanted) continue;
            seen.Add(outfitKey);
            seen.Add("tee:" + body);
            if (o.hair.hat != "none") hats++;
            string tag = $"{id} {body}\n{Looks.DEAD_TOPS[o.top].name} / {Looks.DEAD_BOTTOMS[o.bottom].name}\n{o.hair.style}{(o.hair.beard ? " +beard" : "")}{(o.hair.hat != "none" ? " +" + o.hair.hat : "")}";
            picked.Add((id, tag));
        }
        return picked;
    }

    void RebuildCrowd()
    {
        foreach (var a in crowd) a.Root.QueueFree();
        crowd.Clear();
        string which = cand is "lineup" or "faces" or "clothes" or "hair" ? "plus" : cand is "stumps" or "hats" or "people" or "dead" ? "pz" : cand;
        // A field ahead: rows 1.5 m apart from 4 m out, as wide as the view, to ~120 m.
        int perRow = Math.Max(8, (int)Math.Ceiling(Math.Sqrt(crowdCount) * 1.6));
        for (int i = 0; i < crowdCount; i++)
        {
            // The dead lineup's crowd is dressed as the world dresses the dead (a street of them).
            string zid = "z" + (i + 100);
            var a = cand == "dead" ? MakeActor("pz", crowdRoot, "zombie", Looks.DeadBody(zid), null, null, null, Looks.DeadOutfit(zid)) : MakeActor(which, crowdRoot);
            int row = i / perRow, col = i % perRow;
            double z = 4 + row * 1.5;
            double half = z * Math.Tan(Mathf.DegToRad(45)) * 0.9;
            double x = -half + (col + 0.5) / perRow * 2 * half + (Hash(i) - 0.5) * 0.6;
            a.Root.Position = new Vector3((float)x, 0, (float)-z);
            a.Root.RotationDegrees = new Vector3(0, (float)((Hash(i + 7) - 0.5) * 60), 0);
            a.Phase = Hash(i + 3);
            Play(a, a.Phase);
            crowd.Add(a);
        }
    }

    static double Hash(int i)
    {
        double v = Math.Sin(i * 12.9898 + 78.233) * 43758.5453;
        return v - Math.Floor(v);
    }

    void Play(Actor a, double phase)
    {
        bool walking = move != "idle";
        string clip = a.Cand == "mannequin" ? (walking ? "walk" : "idle") : walking ? "ual/ual_walk" : "ual/ual_idle";
        a.Player.Play(clip);
        a.Player.SpeedScale = walking && a.Subject == "zombie" ? 0.7f : 1f;  // the dead walk slower
        a.Player.Seek(phase * a.Player.CurrentAnimationLength, true);
    }

    // ------------------------------------------------------------------ each frame

    public override void _Process(double delta)
    {
        time += delta;
        // Lunge: walk at the camera from 8 m to arm's length, then start again.
        if (move == "lunge")
        {
            lungeZ += (subject == "zombie" ? 0.9 : 1.3) * delta;
            if (lungeZ > -0.7) lungeZ = -8;
        }
        double z = move == "lunge" ? lungeZ : -dist;
        double vpH = GetViewport().GetVisibleRect().Size.Y;
        foreach (var a in subjects)
        {
            var p = a.Root.Position;
            a.Root.Position = new Vector3(p.X, 0, (float)z);
            a.Root.RotationDegrees = new Vector3(0, (float)turn, 0);
            UpdateActor(a, -z, vpH, delta, face);
        }
        foreach (var a in crowd) UpdateActor(a, -a.Root.Position.Z, vpH, delta, "neutral", crowdLod);
        double pitch = lookPitch;
        if (aim)  // look at the subject: the face up close, the chest further off
        {
            double target = aimAt >= 0 ? aimAt : -z < 2.5 ? 1.62 : 1.25;
            pitch += Mathf.RadToDeg(Math.Atan2(target - EyeHeight, Math.Max(-z, 0.3)));
        }
        cam.Rotation = new Vector3((float)Mathf.DegToRad(pitch), (float)Mathf.DegToRad(lookYaw), 0);
        GetViewport().MeshLodThreshold = godotLod ? 1 : 0;
        readout.Text = Readout(-z, vpH);
        frame++;
        if (shotPath != null && frame == shotFrames && !busy) _ = SaveShotAndQuit(shotPath);
    }

    double ScreenHeight(double distance, double vpH) =>
        vpH * 1.75 / (2 * Math.Max(distance, 0.1) * Math.Tan(Mathf.DegToRad(Fov / 2)));

    void UpdateActor(Actor a, double distance, double vpH, double delta, string faceMode, int lodOverride = -1)
    {
        int lod;
        if (a.Shown <= -2) lod = -2 - a.Shown;
        else if (lodOverride >= 0) lod = Math.Min(lodOverride, a.Lods.Length - 1);
        else if (forcedLod >= 0 && !crowd.Contains(a)) lod = Math.Min(forcedLod, a.Lods.Length - 1);
        else
        {
            var at = LodAt[a.Cand];
            double h = ScreenHeight(distance, vpH) * lodBias;
            lod = Array.FindIndex(at, t => h >= t);
            if (lod < 0) lod = at.Length - 1;
        }
        if (a.Cand != "mannequin")
            for (int i = 0; i < a.Lods.Length; i++)
                foreach (var piece in a.Lods[i]) piece.Visible = i == lod;
        if (a.Shown > -2) a.Shown = lod;

        // The face: it sees you (auto) when you're within sight range.
        bool sees = faceMode == "angry" || (faceMode == "auto" && a.Subject == "zombie" && distance < sightRange);
        a.Angry = Math.Clamp(a.Angry + (sees ? 1 : -1) * delta / 0.25, 0, 1);
        var mi = a.Lods[Math.Min(lod, a.Lods.Length - 1)].FirstOrDefault(m => m.FindBlendShapeByName("angry") >= 0);
        if (a.Cand == "mannequin" || mi == null) return;
        double e = a.Angry * a.Angry * (3 - 2 * a.Angry);
        double slack = Slack.GetValueOrDefault(Looks.FaceOf(a.Variant), 0.12) + 0.03 * Math.Sin(time * 1.3 + a.Phase * 6);
        double growl = 0.75 + 0.20 * Math.Sin(time * 7.5 + a.Phase * 6) + 0.05 * Math.Sin(time * 17);
        SetShape(mi, "jaw_open", forceJaw >= 0 ? forceJaw : slack + (growl - slack) * e);
        SetShape(mi, "angry", e);
        if (OS.GetCmdlineUserArgs().Contains("--traceface") && frame % 30 == 0) GD.Print($"face {mi.Name} lod{lod} angry {e:0.00} jaw {mi.GetBlendShapeValue(mi.FindBlendShapeByName("jaw_open")):0.00} visible {mi.IsVisibleInTree()}");
    }

    static void SetShape(MeshInstance3D mi, string name, double v)
    {
        int i = mi.FindBlendShapeByName(name);
        if (i >= 0) mi.SetBlendShapeValue(i, (float)v);
    }

    string Readout(double distance, double vpH)
    {
        var vp = GetViewport().GetViewportRid();
        var sb = new StringBuilder();
        var a = subjects.FirstOrDefault();
        if (a != null)
        {
            int lod = a.Shown <= -2 ? -2 - a.Shown : Math.Max(a.Shown, 0);
            bool single = cand is not ("lineup" or "faces" or "clothes" or "hair" or "stumps" or "hats" or "people" or "dead");
            sb.AppendLine($"subject   {a.Subject} / {cand}{(a.Cand != "mannequin" && single ? $" lod{lod}" : "")}  {(single && a.Cand != "mannequin" ? a.Variant : "")}");
            sb.AppendLine($"distance  {distance:0.0} m   height on screen {ScreenHeight(distance, vpH):0} px");
            if (single) sb.AppendLine($"character {a.Tris[Math.Min(lod, a.Tris.Length - 1)]:N0} tris   ladder {string.Join(" / ", a.Tris.Select(t => t >= 1000 ? $"{t / 1000.0:0.#}k" : t.ToString()))}");
            if (single) sb.AppendLine($"textures  {a.TexBytes / 1048576.0:0.0} MB: {a.TexDesc}");
            sb.AppendLine($"face      {(a.Subject == "zombie" ? $"{face}, angry {a.Angry:0.00}" : "-")}");
            if (a.CutDesc != "") sb.AppendLine($"lost      {a.CutDesc}");
            if (single && a.HairTris.Length > 0) sb.AppendLine($"hair      {string.Join(" / ", a.HairTris)} tris by LOD");
            if (single && a.Lods.Length > 0 && a.Lods[0].Length > 1)
                sb.AppendLine($"pieces    {string.Join("  ", a.Lods[Math.Min(lod, a.Lods.Length - 1)].Select(m => $"{m.Name.ToString().Split("_lod")[0]} {Tris(m):N0}"))}");
        }
        sb.AppendLine($"crowd     {crowd.Count}");
        sb.AppendLine($"on screen {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame):N0} tris  {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draws  {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)} objects");
        sb.AppendLine($"memory    textures {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / 1048576.0:0.0} MB  buffers {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / 1048576.0:0.0} MB");
        sb.AppendLine($"frame     cpu {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms  render cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp):0.00}  gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp):0.00} ms");
        sb.Append($"fps       {Engine.GetFramesPerSecond():0}");
        return sb.ToString().Replace("\r\n", "\n");  // AppendLine is CRLF on Windows: a Label shows two lines
    }

    // ------------------------------------------------------------------ stage

    void BuildStage()
    {
        env = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            FogEnabled = true,
        } };
        AddChild(env);
        sun = new DirectionalLight3D { ShadowEnabled = true };
        sun.DirectionalShadowMaxDistance = 60;
        AddChild(sun);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(300, 300) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.20f, 0.19f), Roughness = 0.95f },
        });
        // Kerbs and a wall, for scale and to catch the light.
        foreach (var x in new[] { -3.2f, 3.2f })
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.15f, 200) },
                Position = new Vector3(x, 0.075f, -100),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.41f, 0.39f), Roughness = 1 },
            });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.3f, 3, 200) },
            Position = new Vector3(5.5f, 1.5f, -100),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.30f, 0.26f), Roughness = 1 },
        });
        cam = new Camera3D { Fov = (float)Fov, Near = 0.05f, Far = 400, Position = new Vector3(0, (float)EyeHeight, 0) };
        AddChild(cam);
        torch = new SpotLight3D { SpotRange = 30, SpotAngle = 24, LightEnergy = 2.5f, RotationDegrees = new Vector3(3, 0, 0), ShadowEnabled = true, Position = new Vector3(0.25f, -0.25f, 0) };
        cam.AddChild(torch);
        crowdRoot = new Node3D();
        AddChild(crowdRoot);
    }

    void ApplyLight()
    {
        var e = env.Environment;
        (Color bg, Color amb, float ambE, Color sunC, float sunE, Vector3 sunRot, bool torchOn, float fog) = light switch
        {
            "dusk" => (new Color(0.30f, 0.22f, 0.20f), new Color(0.45f, 0.40f, 0.45f), 0.35f, new Color(1.0f, 0.62f, 0.40f), 0.9f, new Vector3(-8, -60, 0), false, 0.010f),
            "night" => (new Color(0.01f, 0.012f, 0.02f), new Color(0.25f, 0.30f, 0.45f), 0.06f, new Color(0.5f, 0.6f, 0.9f), 0.05f, new Vector3(-50, 30, 0), true, 0.03f),
            _ => (new Color(0.55f, 0.58f, 0.60f), new Color(0.60f, 0.63f, 0.66f), 0.55f, new Color(1, 0.97f, 0.92f), 1.1f, new Vector3(-50, 35, 0), false, 0.006f),
        };
        e.BackgroundColor = bg;
        e.AmbientLightColor = amb;
        e.AmbientLightEnergy = ambE;
        e.FogLightColor = bg;
        e.FogDensity = fog;
        sun.LightColor = sunC;
        sun.LightEnergy = sunE;
        sun.RotationDegrees = sunRot;
        torch.Visible = torchOn;
    }

    void ApplyView()
    {
        GetViewport().DebugDraw = wire ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
    }

    // ------------------------------------------------------------------ panel

    OptionButton candPick = null!, lodPick = null!, facePick = null!;
    HSlider distSlider = null!, turnSlider = null!;

    void BuildPanel()
    {
        var layer = panelLayer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(12, 12), Theme = new Theme { DefaultFontSize = 13 } };
        layer.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddChild(new MarginContainer().Also(m => { foreach (var side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + side, 8); m.AddChild(box); }));
        box.AddChild(new Label { Text = "LOOK LAB" });
        var grid = new GridContainer { Columns = 2 };
        box.AddChild(grid);
        Pick(grid, "Subject", new[] { "zombie", "survivor" }, subject, v => { subject = v; Rebuild(); });
        candPick = Pick(grid, "Look", Cands, cand, v => { cand = v; Rebuild(); });
        lodPick = Pick(grid, "LOD", new[] { "auto", "0", "1", "2", "3", "4", "5" }, forcedLod < 0 ? "auto" : forcedLod.ToString(), v => forcedLod = v == "auto" ? -1 : int.Parse(v));
        facePick = Pick(grid, "Face", new[] { "auto", "neutral", "angry" }, face, v => face = v);
        Pick(grid, "Move", new[] { "idle", "walk", "lunge" }, move, v => { move = v; lungeZ = -8; foreach (var a in subjects.Concat(crowd)) Play(a, a.Phase); });
        Pick(grid, "Light", new[] { "day", "dusk", "night" }, light, v => { light = v; ApplyLight(); });
        Pick(grid, "Zombie face", Faces, faceVariant, v => { faceVariant = v; Rebuild(); });
        Pick(grid, "Clothes", ClothesSets, clothesSet, v => { clothesSet = v; Rebuild(); });
        Pick(grid, "Hair", HairStyles, hairStyle, v => { hairStyle = v; Rebuild(); });
        Pick(grid, "Hair colour", HairColours, hairColour, v => { hairColour = v; Rebuild(); });
        Pick(grid, "Beard", new[] { "none", "beard" }, beard ? "beard" : "none", v => { beard = v == "beard"; Rebuild(); });
        Pick(grid, "Hat", Hats, hat, v => { hat = v; Rebuild(); });
        Pick(grid, "Hat colour", HatColours, hatColour, v => { hatColour = v; Rebuild(); });
        Pick(grid, "Missing", Losses, lose == "" ? "none" : lose, v => { lose = v == "none" ? "" : v; Rebuild(); });
        var presets = new HBoxContainer();
        foreach (var p in Presets) presets.AddChild(Btn($"{p} m", () => SetDist(p)));
        box.AddChild(presets);
        distSlider = Slider(box, "Distance m", 0.6, 60, 0.1, dist, v => dist = v);
        turnSlider = Slider(box, "Turn °", -180, 180, 1, turn, v => turn = v);
        Slider(box, "LOD bias", 0.25, 4, 0.05, lodBias, v => lodBias = v);
        Slider(box, "Sight range m", 1, 30, 0.5, sightRange, v => sightRange = v);
        Slider(box, "Crowd", 0, 2000, 10, crowdCount, v => { crowdCount = (int)v; RebuildCrowd(); });
        var toggles = new HBoxContainer();
        var wireBox = new CheckBox { Text = "Wireframe (V)", ButtonPressed = wire, FocusMode = Control.FocusModeEnum.None };
        wireBox.Toggled += on => { wire = on; ApplyView(); };
        toggles.AddChild(wireBox);
        var lodBox = new CheckBox { Text = "Godot LOD", ButtonPressed = godotLod, FocusMode = Control.FocusModeEnum.None };
        lodBox.Toggled += on => godotLod = on;
        toggles.AddChild(lodBox);
        var aimBox = new CheckBox { Text = "Look at it", ButtonPressed = aim, FocusMode = Control.FocusModeEnum.None };
        aimBox.Toggled += on => aim = on;
        toggles.AddChild(aimBox);
        box.AddChild(toggles);
        readout = new Label();
        readout.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Consolas", "Courier New", "monospace" } });
        readout.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(readout);
        box.AddChild(new Label { Text = "wheel / [ ] distance, 1-6 presets, F face, L LOD, V wireframe\nright-drag to look round", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    void SetDist(double d) { dist = d; distSlider.SetValueNoSignal(d); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode >= Key.Key1 && k.Keycode <= Key.Key6) SetDist(Presets[k.Keycode - Key.Key1]);
            else if (k.Keycode == Key.Bracketleft) SetDist(Math.Max(0.6, dist / 1.15));
            else if (k.Keycode == Key.Bracketright) SetDist(Math.Min(60, dist * 1.15));
            else if (k.Keycode == Key.F) { face = face == "angry" ? "neutral" : "angry"; facePick.Select(face == "angry" ? 2 : 1); }
            else if (k.Keycode == Key.L) { forcedLod = forcedLod >= 5 ? -1 : forcedLod + 1; lodPick.Select(forcedLod + 1); }
            else if (k.Keycode == Key.V) { wire = !wire; ApplyView(); }
            else if (k.Keycode == Key.T) { turn = turn switch { 0 => 35, 35 => 90, 90 => 180, _ => 0 }; turnSlider.SetValueNoSignal(turn); }
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) looking = mb.Pressed;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) SetDist(Math.Max(0.6, dist / 1.1));
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) SetDist(Math.Min(60, dist * 1.1));
        }
        else if (e is InputEventMouseMotion mm && looking)
        {
            lookYaw -= mm.Relative.X * 0.2;
            lookPitch = Math.Clamp(lookPitch - mm.Relative.Y * 0.2, -80, 80);
        }
    }

    HSlider Slider(VBoxContainer box, string name, double min, double max, double step, double value, Action<double> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(120, 0) });
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0), FocusMode = Control.FocusModeEnum.None };
        var label = new Label { Text = value.ToString("0.##", CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(44, 0) };
        s.ValueChanged += v => { label.Text = v.ToString("0.##", CultureInfo.InvariantCulture); changed(v); };
        row.AddChild(s);
        row.AddChild(label);
        box.AddChild(row);
        return s;
    }

    static OptionButton Pick(GridContainer grid, string name, string[] items, string selected, Action<string> changed)
    {
        grid.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(70, 0) });
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 0) };
        foreach (var it in items) pick.AddItem(it);
        pick.Selected = Math.Max(0, Array.IndexOf(items, selected));
        pick.ItemSelected += v => changed(items[v]);
        grid.AddChild(pick);
        return pick;
    }

    static Button Btn(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    // ------------------------------------------------------------------ sweeps

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    async Task Shot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot: {path}");
    }

    async Task SaveShotAndQuit(string path)
    {
        busy = true;
        if (hidePanel) panelLayer.Visible = false;
        await Shot(path);
        GetTree().Quit();
    }

    /// <summary>The comparison set: each look at 1, 3, 10 and 30 m (auto LOD), the zombie's face neutral
    /// and angry up close, the lineup, and wireframes of the near LODs.</summary>
    async Task RunShots(string dir)
    {
        busy = true;
        panelLayer.Visible = false;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        // Zombie faces side by side, neutral and angry; then each close up.
        cand = "faces";
        Rebuild();
        foreach (var fm in new[] { "neutral", "angry" })
        {
            dist = 1.5;
            face = fm;
            foreach (var x in subjects) x.Angry = fm == "angry" ? 1 : 0;
            await Frames(8);
            await Shot($"{dir}/faces_{fm}.png");
        }
        cand = "plus";
        subject = "zombie";
        foreach (var fv in Faces)
        {
            faceVariant = fv;
            Rebuild();
            foreach (var fm in new[] { "neutral", "angry" })
            {
                dist = 0.7;
                face = fm;
                foreach (var x in subjects) x.Angry = fm == "angry" ? 1 : 0;
                await Frames(8);
                await Shot($"{dir}/face_{fv}_{fm}.png");
            }
        }
        faceVariant = DefaultFace;
        // Clothes side by side, facing and turned; then each closer, turned.
        cand = "clothes";
        Rebuild();
        foreach (var t in new[] { 0.0, 35.0, 90.0 })
        {
            dist = 2.6;
            turn = t;
            await Frames(8);
            await Shot($"{dir}/clothes_{t:0}.png");
        }
        cand = "plus";
        subject = "survivor";
        foreach (var cs in ClothesSets)
        {
            clothesSet = cs;
            Rebuild();
            dist = 1.3;
            turn = 35;
            await Frames(8);
            await Shot($"{dir}/clothes_{cs}_close.png");
        }
        clothesSet = "painted";
        turn = 0;
        // Hair: every style side by side, facing and turned; then each close, turned, in a few colours.
        cand = "hair";
        foreach (var t in new[] { 0.0, 60.0, 180.0 })
        {
            turn = t;
            Rebuild();
            dist = 1.5;
            await Frames(8);
            await Shot($"{dir}/hair_{t:0}.png");
        }
        cand = "plus";
        subject = "survivor";
        string[] colours = { "black", "brown", "blond", "red", "grey" };
        for (int i = 1; i < HairStyles.Length; i++)
        {
            hairStyle = HairStyles[i];
            hairColour = colours[i % colours.Length];
            beard = i == 3;
            Rebuild();
            dist = 0.8;
            turn = 30;
            await Frames(8);
            await Shot($"{dir}/hair_{hairStyle}_close.png");
        }
        hairStyle = "crop";
        hairColour = "brown";
        beard = false;
        turn = 0;
        foreach (var subj in new[] { "zombie", "survivor" })
        {
            subject = subj;
            foreach (var c in new[] { "mannequin", "pz", "plus" })
            {
                cand = c;
                Rebuild();
                foreach (var d in new[] { 1.0, 3.0, 10.0, 30.0 })
                {
                    dist = d;
                    var faces = subj == "zombie" && d <= 3 && c != "mannequin" ? new[] { "neutral", "angry" } : new[] { "neutral" };
                    foreach (var fm in faces)
                    {
                        face = fm;
                        foreach (var x in subjects) x.Angry = fm == "angry" ? 1 : 0;
                        await Frames(6);
                        await Shot($"{dir}/{subj}_{c}_{d:0}m_{fm}.png");
                    }
                }
            }
            cand = "lineup";
            Rebuild();
            foreach (var d in new[] { 2.5, 8.0 })
            {
                dist = d;
                face = "neutral";
                await Frames(6);
                await Shot($"{dir}/{subj}_lineup_{d:0.#}m.png");
            }
        }
        subject = "zombie";
        wire = true;
        ApplyView();
        foreach (var (c, lod) in new[] { ("pz", 0), ("plus", 0), ("plus", 1), ("plus", 2) })
        {
            cand = c;
            forcedLod = lod;
            dist = 1.6;
            Rebuild();
            await Frames(6);
            await Shot($"{dir}/wire_{c}_lod{lod}.png");
        }
        GetTree().Quit();
    }

    /// <summary>The budget sweep: a crowd of each look at each forced LOD, at several sizes; the frame
    /// times and what's on screen, written to DIR/look.md.</summary>
    async Task RunReport(string dir)
    {
        busy = true;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        var vp = GetViewport().GetViewportRid();
        var sb = new StringBuilder();
        sb.AppendLine("# Look lab budget sweep");
        sb.AppendLine();
        sb.AppendLine($"Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}, " +
                      $"{GetViewport().GetVisibleRect().Size.X}x{GetViewport().GetVisibleRect().Size.Y}, light {light}, shadows on, vsync off. " +
                      "A crowd walking (AnimationPlayer each, every frame) in a field 4-120 m ahead; the subject hidden. Means over 90 frames after 30 to settle.");
        sb.AppendLine();
        sb.AppendLine("| look | LOD | tris each | crowd | on screen tris | draws | frame ms | gpu ms | render cpu ms | fps |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        subject = "zombie";
        move = "walk";
        foreach (var (c, lods) in new[] { ("mannequin", new[] { 0 }), ("pz", new[] { 0, 1, 2 }), ("plus", new[] { 0, 1, 2, 3, 4, 5 }) })
            foreach (var lod in lods)
                foreach (var n in new[] { 100, 400, 1000 })
                {
                    cand = c;
                    crowdCount = n;
                    crowdLod = lod;
                    Rebuild();
                    foreach (var x in subjects) x.Root.Visible = false;
                    await Frames(30);
                    double cpu = 0, gpu = 0, rcpu = 0, tris = 0, draws = 0, ms = 0;
                    const int N = 90;
                    ulong last = Time.GetTicksUsec();
                    for (int i = 0; i < N; i++)
                    {
                        await Frames(1);
                        ulong now = Time.GetTicksUsec();
                        ms += (now - last) / 1000.0;
                        last = now;
                        gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                        rcpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
                        tris += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                        draws += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                    }
                    int each = crowd.Count > 0 ? crowd[0].Tris[Math.Min(lod, crowd[0].Tris.Length - 1)] : 0;
                    string line = $"| {c} | {lod} | {each:N0} | {n} | {tris / N:N0} | {draws / N:0} | {ms / N:0.00} | {gpu / N:0.00} | {rcpu / N:0.00} | {1000 / (ms / N):0} |";
                    sb.AppendLine(line);
                    GD.Print(line);
                }
        crowdCount = 0;
        RebuildCrowd();
        sb.AppendLine();
        sb.AppendLine("Texture memory with one of each look loaded: see the lab's readout (it counts what's on the GPU).");
        using (var f = FileAccess.Open($"{dir}/look.md", FileAccess.ModeFlags.Write)) f.StoreString(sb.ToString());
        GD.Print($"report: {dir}/look.md");
        GetTree().Quit();
    }

    /// <summary>The dead's crowd before and after the outfit work: "before" each wears its face's tee file as
    /// painted and matted grey hair (the world's look until 2026-11-10); "after" as the world dresses them now
    /// (Looks.DeadBody and DeadOutfit: eight files, recoloured, hair, beards, hats). 40 (the world's model
    /// budget) and 200, walking, auto LOD, in the field ahead; means over 240 frames after 60 to settle.</summary>
    async Task RunCrowdBench(string file)
    {
        busy = true;
        panelLayer.Visible = false;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        var vp = GetViewport().GetViewportRid();
        foreach (var x in subjects) x.Root.Visible = false;
        move = "walk";
        var sb = new StringBuilder();
        sb.AppendLine("# The dead's crowd: before and after their outfits");
        sb.AppendLine();
        sb.AppendLine($"Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}, " +
                      $"{GetViewport().GetVisibleRect().Size.X}x{GetViewport().GetVisibleRect().Size.Y}, light {light}, shadows on, vsync off. " +
                      "The look lab's field (rows 1.5 m apart from 4 m out), pz zombies walking (an AnimationPlayer each), auto LOD by height on screen; means over 240 frames after 60.");
        sb.AppendLine();
        sb.AppendLine("| dead | crowd | frame ms | gpu ms | render cpu ms | draws | objects | on screen tris | model files | materials | build ms |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var n in new[] { 40, 200 })
            foreach (var mode in new[] { "before", "after", "before", "after" })
            {
                foreach (var a in crowd) a.Root.QueueFree();
                crowd.Clear();
                await Frames(2);
                ulong t0 = Time.GetTicksUsec();
                var files = new HashSet<string>();
                int perRow = Math.Max(8, (int)Math.Ceiling(Math.Sqrt(n) * 1.6));
                for (int i = 0; i < n; i++)
                {
                    string id = "z" + (i + 1);
                    Actor a;
                    if (mode == "before")
                    {
                        string face = Looks.DeadFace(id);
                        a = MakeActor("pz", crowdRoot, "zombie", face, null, null, new Looks.Hair("matted", 4, "none", 0, false, false));
                        files.Add(face);
                    }
                    else
                    {
                        string body = Looks.DeadBody(id);
                        a = MakeActor("pz", crowdRoot, "zombie", body, null, null, null, Looks.DeadOutfit(id));
                        files.Add(body);
                    }
                    int row = i / perRow, col = i % perRow;
                    double z = 4 + row * 1.5;
                    double half = z * Math.Tan(Mathf.DegToRad(45)) * 0.9;
                    double x = -half + (col + 0.5) / perRow * 2 * half + (Hash(i) - 0.5) * 0.6;
                    a.Root.Position = new Vector3((float)x, 0, (float)-z);
                    a.Root.RotationDegrees = new Vector3(0, (float)((Hash(i + 7) - 0.5) * 60), 0);
                    a.Phase = Hash(i + 3);
                    Play(a, a.Phase);
                    crowd.Add(a);
                }
                double build = (Time.GetTicksUsec() - t0) / 1000.0;
                await Frames(60);
                double gpu = 0, rcpu = 0, tris = 0, draws = 0, objects = 0, ms = 0;
                const int N = 240;
                ulong last = Time.GetTicksUsec();
                for (int i = 0; i < N; i++)
                {
                    await Frames(1);
                    ulong now = Time.GetTicksUsec();
                    ms += (now - last) / 1000.0;
                    last = now;
                    gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                    rcpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
                    tris += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                    draws += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                    objects += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
                }
                var mats = new HashSet<ulong>();
                foreach (var a in crowd)
                    foreach (var mi in a.Lods.SelectMany(g => g))
                        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                            if ((mi.GetSurfaceOverrideMaterial(s) ?? mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(s)) is Material m) mats.Add(m.GetInstanceId());
                string line = $"| {mode} | {n} | {ms / N:0.00} | {gpu / N:0.00} | {rcpu / N:0.00} | {draws / N:0} | {objects / N:0} | {tris / N:N0} | {files.Count} | {mats.Count} | {build:0} |";
                sb.AppendLine(line);
                GD.Print("bench: " + line);
            }
        foreach (var a in crowd) a.Root.QueueFree();
        crowd.Clear();
        sb.AppendLine();
        sb.AppendLine("Each crowd size runs before, after, before, after (the second pair shows the noise). Materials: distinct ones on the crowd's meshes (body, garments, hair, beard, hat); after, the dead's recolour materials are shared per model file, surface and colour pair.");
        using (var f = FileAccess.Open(file, FileAccess.ModeFlags.Write)) f.StoreString(sb.ToString());
        GD.Print($"bench: {file}");
        GetTree().Quit();
    }

    // ------------------------------------------------------------------ helpers

    static T? Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T found) return found;
        return null;
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var x in FindAll<T>(c)) yield return x;
    }

    static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    static Dictionary<string, string> ParseArgs()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        return args;
    }
}
