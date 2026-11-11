// Who wears what (docs/characters.md "Clothing, hair"): the hair, hat and beard a person has, picked by a
// stateless hash of their id (never the sim's RNG, nothing saved), so the same person always looks the same,
// and the hair's meshes put on a character (art/scripts/build_hair.py: each style its own mesh on the shared
// rig, painted grey, tinted per person; an under-hat version of every style for wearing a hat). The world's
// people (World3D/People.cs) and the look lab share it.
//
// Colour is the material's: one material per source material and tint, shared by everyone with that tint
// (no per-instance shader values: Godot reserves global buffer slots per mesh for those, the near tier's
// limit, docs/characters.md). The palette is small on purpose.
//
// Cost: the hair meshes are on the plus ladder (30k ... 600); the pz bodies' 3 LODs take its 3rd to 5th
// (HAIR_LOD_OFFSET), so a near body wears 250-850 triangles of hair (with a beard or hat), a far one 60-200,
// and no strand cards.
//
// The dead's clothes: an outfit (a model file: the tee with one of the five faces, a long-sleeved shirt, a
// jacket, a hoodie) and the colours of its top and trousers, each from a small worn palette, put on by
// recolouring the model's own painted map through its recolour mask (recolour.gdshaderinc: the palette colour
// times each texel's brightness against the cloth's mean, so the shading and wear stay): one material per
// file, surface and colour pair, shared by everyone in it (10 x 7 pairs at most per file; no instance values).
// The living wear the same shader in their file's own colours (one material per file and surface), for the detail
// kit's cloth and rubber on clothes and shoes, by the file's detail map (docs/characters.md "Detail on bodies").

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LoonieRoo.Game.Characters;

public static class Looks
{
    /// <summary>The pz ladder's 3k / 1.2k / 600 wear the hair ladder's 3 / 4 / 5.</summary>
    public const int HAIR_LOD_OFFSET = 3;

    /// <summary>The bodies (the look model files): the living's clothes set, the dead's face.</summary>
    public static readonly string[] CLOTHES = { "painted", "jacket", "hoodie" };
    public static readonly string[] ZOMBIE_FACES = { "feral", "feral", "slack", "gaunt", "bloated", "torn" };
    public static string LivingClothes(string id) => Pick(CLOTHES, id, 1);
    public static string DeadFace(string id) => Pick(ZOMBIE_FACES, id, 1);

    /// <summary>What the dead died in: the tee (and jeans) half the time, with any face; the rest a long-sleeved
    /// work shirt and trousers, a jacket and jeans, a hoodie and jeans, each built with one face
    /// (art/scripts/build_lookdev.py --outfit / --clothes).</summary>
    public static readonly string[] DEAD_OUTFITS = { "tee", "tee", "tee", "longsleeve", "jacket", "slack_hoodie" };

    /// <summary>The dead's model variant (look_pz_zombie[_variant].glb): a face for the tee, else the outfit's file.</summary>
    public static string DeadBody(string id) => Pick(DEAD_OUTFITS, id, 40) is var o && o == "tee" ? DeadFace(id) : o;

    /// <summary>The face in a model variant ("slack_hoodie" is slack; "jacket" the default, feral).</summary>
    public static string FaceOf(string variant) => variant.Split('_')[0] is var f && ZOMBIE_FACES.Contains(f) ? f : "feral";

    /// <summary>Tops and trousers the dead wear: muted, worn (sRGB). The first of each is the tee's own.</summary>
    public static readonly (string name, Color c)[] DEAD_TOPS =
    {
        ("faded blue", new(0.30f, 0.36f, 0.42f)), ("grey", new(0.34f, 0.34f, 0.34f)), ("khaki", new(0.34f, 0.32f, 0.25f)),
        ("maroon", new(0.25f, 0.15f, 0.15f)), ("forest", new(0.20f, 0.25f, 0.20f)), ("black", new(0.13f, 0.13f, 0.14f)),
        ("mustard", new(0.34f, 0.30f, 0.18f)), ("off-white", new(0.50f, 0.49f, 0.46f)), ("navy", new(0.17f, 0.19f, 0.26f)),
        ("brown", new(0.26f, 0.21f, 0.17f)),
    };
    public static readonly (string name, Color c)[] DEAD_BOTTOMS =
    {
        ("jeans", new(0.17f, 0.21f, 0.30f)), ("dark jeans", new(0.12f, 0.14f, 0.20f)), ("black", new(0.11f, 0.11f, 0.12f)),
        ("khaki", new(0.32f, 0.30f, 0.24f)), ("grey", new(0.27f, 0.27f, 0.28f)), ("brown", new(0.24f, 0.19f, 0.15f)),
        ("olive", new(0.23f, 0.25f, 0.18f)),
    };

    /// <summary>Everything a person wears over their body: the hair, and the clothes' colours (indices into
    /// DEAD_TOPS and DEAD_BOTTOMS; -1 for the model's own, as painted).</summary>
    public readonly record struct Outfit(Hair hair, int top, int bottom)
    {
        public override string ToString() => $"{hair}/{top}/{bottom}";
    }

    public static Outfit LivingOutfit(string id) => new(LivingHair(id), -1, -1);

    public static Outfit DeadOutfit(string id) => new(DeadHair(id),
        (int)(Hash01(id, 41) * DEAD_TOPS.Length) % DEAD_TOPS.Length, (int)(Hash01(id, 42) * DEAD_BOTTOMS.Length) % DEAD_BOTTOMS.Length);

    public static readonly string[] LIVING_HAIR = { "buzz", "crop", "sidepart", "long", "receding", "ponytail", "none" };
    /// <summary>The dead: mostly matted; some still with the cut they died with; "none" is their painted, patchy scalp.</summary>
    public static readonly string[] DEAD_HAIR = { "matted", "matted", "matted", "long", "receding", "crop", "buzz", "none" };

    public static readonly (string name, Color c)[] HAIR_COLOURS =
    {
        ("black", new(0.10f, 0.09f, 0.085f)), ("brown", new(0.34f, 0.24f, 0.16f)), ("blond", new(0.78f, 0.64f, 0.42f)),
        ("red", new(0.58f, 0.27f, 0.13f)), ("grey", new(0.64f, 0.62f, 0.60f)),
    };
    public static readonly (string name, Color c)[] HAT_COLOURS =
    {
        ("charcoal", new(0.22f, 0.22f, 0.23f)), ("olive", new(0.32f, 0.33f, 0.20f)), ("navy", new(0.15f, 0.18f, 0.28f)), ("red", new(0.45f, 0.12f, 0.10f)),
    };

    /// <summary>How often each wears a hat or a beard.</summary>
    const double LIVING_HAT = 0.22, DEAD_HAT = 0.10, LIVING_BEARD = 0.25, DEAD_BEARD = 0.18;

    /// <summary>A person's hair: style, colour (an index into HAIR_COLOURS), hat ("none" or "beanie") and its colour, beard.</summary>
    public readonly record struct Hair(string style, int colour, string hat, int hatColour, bool beard, bool dead)
    {
        /// <summary>A key: people with equal keys look the same from the neck up.</summary>
        public override string ToString() => $"{style}/{colour}/{hat}/{hatColour}/{(beard ? "beard" : "-")}{(dead ? "/dead" : "")}";
    }

    public static Hair LivingHair(string id) => new(
        Pick(LIVING_HAIR, id, 2), (int)(Hash01(id, 4) * HAIR_COLOURS.Length) % HAIR_COLOURS.Length,
        Hash01(id, 21) < LIVING_HAT ? "beanie" : "none", (int)(Hash01(id, 22) * HAT_COLOURS.Length) % HAT_COLOURS.Length,
        Hash01(id, 23) < LIVING_BEARD && Pick(LIVING_HAIR, id, 2) is not ("long" or "ponytail"), false);

    public static Hair DeadHair(string id) => new(
        Pick(DEAD_HAIR, id, 31), (int)(Hash01(id, 32) * HAIR_COLOURS.Length) % HAIR_COLOURS.Length,
        Hash01(id, 33) < DEAD_HAT ? "beanie" : "none", (int)(Hash01(id, 34) * HAT_COLOURS.Length) % HAT_COLOURS.Length,
        Hash01(id, 35) < DEAD_BEARD, true);

    /// <summary>The tint: the palette's, the dead's dulled (grime, greying) and their hats faded and dirty.</summary>
    public static Color Tint(Hair h, bool hat)
    {
        var c = hat ? HAT_COLOURS[h.hatColour].c : HAIR_COLOURS[h.colour].c;
        if (!h.dead) return c;
        var grime = new Color(0.22f, 0.19f, 0.15f);
        return c.Lerp(grime, hat ? 0.45f : 0.35f);
    }

    // --- recolouring ---

    sealed class MaskInfo
    {
        public Texture2D? mask;
        public Color top, bottom;  // what the mask's cloth was painted in (sRGB)
        public float topLum, bottomLum;  // its mean linear luminance in the map
    }

    static readonly Dictionary<string, MaskInfo?> masks = new();
    static readonly Dictionary<string, ShaderMaterial> recoloured = new();
    static Shader? recolourShader, recolourCloth;

    /// <summary>A model file's recolour mask and the colours it was painted in (look_..._mask.png and .json beside the .glb), or null.</summary>
    static MaskInfo? Mask(string modelPath)
    {
        if (masks.TryGetValue(modelPath, out var info)) return info;
        string bas = modelPath[..^4];
        info = null;
        if (ResourceLoader.Exists(bas + "_mask.png") && FileAccess.FileExists(bas + "_mask.json"))
        {
            var json = Json.ParseString(FileAccess.GetFileAsString(bas + "_mask.json")).AsGodotDictionary();
            Color C(string k) { var a = json[k].AsFloat32Array(); return new Color(a[0], a[1], a[2]); }
            float L(string k) => json.ContainsKey(k) ? (float)json[k].AsDouble() : 0.1f;
            info = new MaskInfo { mask = GD.Load<Texture2D>(bas + "_mask.png"), top = C("top"), bottom = C("bottom"), topLum = L("top_lum"), bottomLum = L("bottom_lum") };
        }
        masks[modelPath] = info;
        return info;
    }

    /// <summary>The kit's detail on a body (docs/characters.md "Detail on bodies"): metres a repeat of the cloth and
    /// shoe layers covers on clothes (finer than the layers' own, which are sized for upholstery and soles).</summary>
    public const float CLOTH_METRES = 0.4f, SHOE_METRES = 0.5f;

    sealed class DetailInfo
    {
        public Texture2D? map;
        public float metresPerUv;
    }

    static readonly Dictionary<string, DetailInfo?> details = new();

    /// <summary>A model file's detail map (look_..._detail.png and .json beside the .glb, art/scripts/build_detail_maps.py), or null.</summary>
    static DetailInfo? Detail(string modelPath)
    {
        if (details.TryGetValue(modelPath, out var info)) return info;
        string bas = modelPath[..^4];
        info = null;
        if (ResourceLoader.Exists(bas + "_detail.png") && FileAccess.FileExists(bas + "_detail.json"))
        {
            var json = Json.ParseString(FileAccess.GetFileAsString(bas + "_detail.json")).AsGodotDictionary();
            info = new DetailInfo { map = GD.Load<Texture2D>(bas + "_detail.png"), metresPerUv = (float)json["metres_per_uv"].AsDouble() };
        }
        details[modelPath] = info;
        return info;
    }

    /// <summary>Puts the clothes' colours and the kit's detail on a look model's pieces (its own surfaces, the ones
    /// painted from its map): a shared material per surface material and colour pair; top &lt; 0 for the file's own
    /// colours (the living). A file with neither a mask nor a detail map keeps its own material.</summary>
    public static void Recolour(IEnumerable<MeshInstance3D> pieces, string modelPath, int top, int bottom)
    {
        var info = top >= 0 ? Mask(modelPath) : null;
        var detail = Detail(modelPath);
        foreach (var mi in pieces)
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D src || src.AlbedoTexture == null) { continue; }
                mi.SetSurfaceOverrideMaterial(s, info == null && detail == null ? null : Recoloured(src, info, detail, top, bottom));
            }
    }

    static ShaderMaterial Recoloured(BaseMaterial3D src, MaskInfo? info, DetailInfo? detail, int top, int bottom)
    {
        string key = info == null ? $"{src.GetInstanceId()}/own" : $"{src.GetInstanceId()}/{top}/{bottom}";
        if (recoloured.TryGetValue(key, out var mat)) return mat;
        bool cloth = src.CullMode == BaseMaterial3D.CullModeEnum.Disabled;
        World3D.Surfaces.Ensure();  // the detail's texture array is a global: declared before the shaders compile
        recolourShader ??= GD.Load<Shader>("res://Characters/recolour.gdshader");
        recolourCloth ??= GD.Load<Shader>("res://Characters/recolour_cloth.gdshader");
        mat = new ShaderMaterial { Shader = cloth ? recolourCloth : recolourShader };
        mat.SetShaderParameter("albedo_tex", src.AlbedoTexture);
        mat.SetShaderParameter("roughness", src.Roughness);
        if (info != null)
        {
            mat.SetShaderParameter("mask_tex", info.mask);
            mat.SetShaderParameter("top_colour", Linear(DEAD_TOPS[top].c));
            mat.SetShaderParameter("bottom_colour", Linear(DEAD_BOTTOMS[bottom].c));
            mat.SetShaderParameter("top_lum", Math.Max(info.topLum, 1e-3f));
            mat.SetShaderParameter("bottom_lum", Math.Max(info.bottomLum, 1e-3f));
        }
        if (detail != null)
        {
            mat.SetShaderParameter("detail_tex", detail.map);
            mat.SetShaderParameter("detail", new Vector4(detail.metresPerUv / CLOTH_METRES, detail.metresPerUv / SHOE_METRES,
                World3D.Surfaces.Layer("cloth"), World3D.Surfaces.Layer("rubber")));
        }
        recoloured[key] = mat;
        return mat;
    }

    /// <summary>A palette colour (sRGB) in linear light, what the shader works in.</summary>
    static Vector3 Linear(Color c)
    {
        var l = c.SrgbToLinear();
        return new Vector3(l.R, l.G, l.B);
    }

    // --- the hash (stable between runs and machines: FNV-1a of the id's chars, then a salt) ---

    public static double Hash01(string id, int salt)
    {
        uint h = 2166136261;
        foreach (char c in id) h = (h ^ c) * 16777619;
        h = (h ^ (uint)salt) * 16777619;
        h ^= h >> 13;
        return (h % 10007) / 10007.0;
    }

    public static string Pick(string[] from, string id, int salt) => from[(int)(Hash01(id, salt) * from.Length) % from.Length];

    // --- putting it on ---

    /// <summary>A style's file: its under-hat version when a hat is on (the beard has none: no hat covers it).</summary>
    public static string HairPath(string style, bool underHat) =>
        $"res://assets/characters/look/hair/{(style == "beanie" ? "hat_beanie" : "hair_" + style + (underHat && style != "beard" ? "_hat" : ""))}.glb";

    static readonly Dictionary<string, PackedScene> scenes = new();
    static readonly Dictionary<string, StandardMaterial3D> materials = new();

    /// <summary>Puts a person's hair, beard and hat on the character's skeleton: each mesh into the body LOD
    /// group it's drawn with (hair LOD minus lodOffset; the levels above the body's top are left out). Returns
    /// the meshes added, by body LOD.</summary>
    public static List<MeshInstance3D>[] Attach(Skeleton3D skel, Hair h, int bodyLods, int lodOffset)
    {
        var groups = Enumerable.Range(0, bodyLods).Select((_) => new List<MeshInstance3D>()).ToArray();
        bool hatOn = h.hat != "none";
        foreach (var (style, isHat) in new[] { (h.style, false), (h.beard ? "beard" : "none", false), (h.hat, true) })
        {
            if (style == "none") continue;
            string path = HairPath(style, hatOn);
            if (!ResourceLoader.Exists(path)) continue;
            if (!scenes.TryGetValue(path, out var scene)) scenes[path] = scene = GD.Load<PackedScene>(path);
            var inst = scene.Instantiate<Node3D>();
            foreach (var mi in MeshesUnder(inst).ToList())
            {
                int lod = LodIndex(mi.Name) - lodOffset;
                if (lod < 0) continue;  // the ladder's top levels (and their strand cards): not drawn
                mi.GetParent().RemoveChild(mi);
                mi.Owner = null;
                mi.Name = $"{style}_{mi.Name}";
                skel.AddChild(mi);
                mi.Skeleton = new NodePath("..");
                mi.MaterialOverride = Material(mi, Tint(h, isHat), isHat);
                groups[Math.Min(lod, bodyLods - 1)].Add(mi);
            }
            inst.QueueFree();
        }
        return groups;
    }

    /// <summary>The mesh's own material (the hair's, or the strand cards' cut-out one) tinted; shared per source and tint.</summary>
    public static StandardMaterial3D Material(MeshInstance3D mi, Color tint, bool hat)
    {
        var src = (BaseMaterial3D)mi.Mesh.SurfaceGetMaterial(0);
        string key = src.GetInstanceId() + "/" + tint.ToHtml();
        if (!materials.TryGetValue(key, out var mat))
        {
            mat = (StandardMaterial3D)src.Duplicate();
            mat.AlbedoColor = tint;
            mat.Roughness = hat ? 0.95f : 0.6f;
            materials[key] = mat;
        }
        return mat;
    }

    public static int LodIndex(StringName name)
    {
        var s = name.ToString();
        int i = s.LastIndexOf("lod", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(s[(i + 3)..], out var n) ? n : 0;
    }

    static IEnumerable<MeshInstance3D> MeshesUnder(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var f in MeshesUnder(c)) yield return f;
    }
}
