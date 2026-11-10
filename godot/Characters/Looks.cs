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
// (HAIR_LOD_OFFSET), so a near body wears 150-640 triangles of hair, a far one 30-130, and no strand cards.

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
