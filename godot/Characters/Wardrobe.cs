// Clothing (docs/characters.md "Clothing, hair, equipment"): garments from garments.glb, put on a
// character's skeleton, shown and hidden by the rules in garments.json (art/scripts/garments.py):
//   - a garment is pieces, one per body region (torso, upperarm_Left, ..., back);
//   - a body region is hidden when a worn piece covers it fully (no clipping, fewer triangles);
//   - a worn piece hides lower layers' pieces in its region when it's full or "hides_lower"
//     (a jacket over a shirt, boots over trousers' cuffs are partial so both show);
//   - a missing limb's regions hide the garments there as well as the body.
// Colours and conditions (dirt, blood, torn, wet) are per-mesh shader values (character.gdshader)
// on one shared material, so every outfit draws with the same material.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LoonieRoo.Game.Characters;

public sealed record GarmentPiece(string Mesh, string Region, bool Full, bool HidesLower);
public sealed record GarmentDef(string Id, string Slot, int Layer, Color Color, List<GarmentPiece> Pieces);

public static class Garments
{
    const string JsonPath = "res://assets/characters/garments.json", Glb = "res://assets/characters/garments.glb";
    public static readonly string[] Slots = { "shirt", "jacket", "legs", "feet", "hands", "head", "back" };
    public static readonly (string name, Color color)[] Palette =
    {
        ("default", default), ("black", new Color(0.09f, 0.09f, 0.1f)), ("grey", new Color(0.4f, 0.4f, 0.42f)),
        ("white", new Color(0.82f, 0.82f, 0.8f)), ("navy", new Color(0.12f, 0.16f, 0.3f)), ("olive", new Color(0.3f, 0.33f, 0.18f)),
        ("khaki", new Color(0.6f, 0.53f, 0.38f)), ("brown", new Color(0.33f, 0.22f, 0.13f)), ("red", new Color(0.55f, 0.1f, 0.08f)),
        ("orange", new Color(0.85f, 0.4f, 0.08f)),
    };
    public static readonly Color Skin = new(0.55f, 0.53f, 0.5f);

    static Dictionary<string, GarmentDef>? defs;
    static PackedScene? scene;
    static ShaderMaterial? material;

    public static Dictionary<string, GarmentDef> Defs => defs ??= Load();
    public static PackedScene Scene => scene ??= GD.Load<PackedScene>(Glb);
    public static ShaderMaterial Material => material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Characters/character.gdshader") };

    public static IEnumerable<GarmentDef> InSlot(string slot) => Defs.Values.Where(d => d.Slot == slot).OrderBy(d => d.Id);

    static Dictionary<string, GarmentDef> Load()
    {
        var root = Json.ParseString(FileAccess.GetFileAsString(JsonPath)).AsGodotDictionary();
        var all = new Dictionary<string, GarmentDef>();
        foreach (var (key, value) in root["garments"].AsGodotDictionary())
        {
            var g = value.AsGodotDictionary();
            var c = g["color"].AsGodotArray();
            var pieces = g["pieces"].AsGodotArray().Select(p => p.AsGodotDictionary())
                .Select(p => new GarmentPiece((string)p["mesh"], (string)p["region"], (bool)p["full"], (bool)p["hides_lower"])).ToList();
            string id = (string)key;
            all[id] = new GarmentDef(id, (string)g["slot"], (int)g["layer"], new Color((float)c[0], (float)c[1], (float)c[2]), pieces);
        }
        return all;
    }
}

/// <summary>What a character wears and the state it's in (in the game: from the sim's items and their condition).</summary>
public sealed class Outfit
{
    public readonly Dictionary<string, string> Worn = new();   // slot -> garment id
    public readonly Dictionary<string, Color> Dye = new();     // garment id -> colour (else its own)
    public double Dirt, Blood, Torn, Wet;

    public Color ColorOf(string garment) => Dye.TryGetValue(garment, out var c) ? c : Garments.Defs[garment].Color;

    /// <summary>A plausible random outfit: usually something on top and bottom, sometimes more.</summary>
    public static Outfit Random(RandomNumberGenerator rng)
    {
        var o = new Outfit();
        void Maybe(string slot, double chance)
        {
            var options = Garments.InSlot(slot).ToList();
            if (options.Count == 0 || rng.Randf() > chance) return;
            var g = options[rng.RandiRange(0, options.Count - 1)];
            o.Worn[slot] = g.Id;
            if (rng.Randf() < 0.7) o.Dye[g.Id] = Garments.Palette[rng.RandiRange(1, Garments.Palette.Length - 1)].color;
        }
        Maybe("shirt", 0.9); Maybe("jacket", 0.45); Maybe("legs", 0.95); Maybe("feet", 0.9);
        Maybe("hands", 0.2); Maybe("head", 0.3); Maybe("back", 0.35);
        o.Dirt = rng.Randf() * 0.8; o.Blood = rng.Randf() < 0.3 ? rng.Randf() * 0.6 : 0; o.Torn = rng.Randf() < 0.3 ? rng.Randf() * 0.6 : 0;
        return o;
    }
}

/// <summary>One character's clothes: every garment's pieces on its skeleton, shown by the outfit.</summary>
public sealed class Dresser
{
    readonly Dictionary<string, MeshInstance3D> pieces = new();  // mesh name -> piece
    readonly Dictionary<string, MeshInstance3D> body = new();    // region -> body mesh
    public static readonly string[] BodyRegions = { "torso", "head", "upperarm_Left", "forearm_Left", "hand_Left", "upperarm_Right", "forearm_Right", "hand_Right", "thigh_Left", "shin_Left", "foot_Left", "thigh_Right", "shin_Right", "foot_Right" };

    public Dresser(Node3D character, Skeleton3D skel)
    {
        foreach (var region in BodyRegions)
            if (character.FindChild(region, true, false) is MeshInstance3D m)
            {
                body[region] = m;
                m.SetSurfaceOverrideMaterial(0, Garments.Material);  // the head's face keeps its own (surface 1)
            }
        var clothes = Garments.Scene.Instantiate<Node3D>();
        foreach (var m in Find(clothes))
        {
            m.GetParent().RemoveChild(m);
            m.Owner = null;  // it belonged to the garments scene
            skel.AddChild(m);
            m.Skeleton = new NodePath("..");
            m.MaterialOverride = Garments.Material;
            m.Visible = false;
            pieces[m.Name] = m;
        }
        clothes.QueueFree();
    }

    static List<MeshInstance3D> Find(Node n)
    {
        var all = new List<MeshInstance3D>();
        if (n is MeshInstance3D m) all.Add(m);
        foreach (var c in n.GetChildren()) all.AddRange(Find(c));
        return all;
    }

    /// <summary>What shows of an outfit: the body regions left bare and the garment pieces on view
    /// (garments by layer, body regions they cover hidden, cut regions hidden).</summary>
    public static (List<string> body, List<(GarmentDef g, GarmentPiece p)> pieces) Plan(Outfit o, ISet<string> cut)
    {
        var worn = o.Worn.Values.Where(Garments.Defs.ContainsKey).Select(id => Garments.Defs[id]).ToList();
        var inRegion = worn.SelectMany(g => g.Pieces.Select(p => (g, p))).GroupBy(x => x.p.Region).ToDictionary(x => x.Key, x => x.ToList());
        var bare = BodyRegions.Where(r => !cut.Contains(r) && !(inRegion.TryGetValue(r, out var list) && list.Any(x => x.p.Full))).ToList();
        var shown = worn.SelectMany(g => g.Pieces.Select(p => (g, p)))
            .Where(x => !cut.Contains(x.p.Region) && !inRegion[x.p.Region].Any(y => y.g.Layer > x.g.Layer && (y.p.Full || y.p.HidesLower)))
            .ToList();
        return (bare, shown);
    }

    /// <summary>A garment's pattern seed (before the character's own), so two garments don't tear alike.</summary>
    public static int GarmentSeed(string id) => id.Sum(ch => ch) % 97;

    /// <summary>Show the outfit (Plan), with its colours and conditions.</summary>
    public void Apply(Outfit o, ISet<string> cut)
    {
        var (bare, shown) = Plan(o, cut);
        foreach (var (region, mesh) in body)
        {
            mesh.Visible = bare.Contains(region);
            mesh.SetInstanceShaderParameter("tint", new Color(Garments.Skin, 0));
            mesh.SetInstanceShaderParameter("wear", new Vector4((float)(o.Dirt * 0.6), (float)(o.Blood * 0.6), 0, (float)o.Wet));
        }
        foreach (var m in pieces.Values) m.Visible = false;
        foreach (var (g, p) in shown)
        {
            if (!pieces.TryGetValue(p.Mesh, out var m)) continue;
            m.Visible = true;
            m.SetInstanceShaderParameter("tint", new Color(o.ColorOf(g.Id), GarmentSeed(g.Id) + Seed));
            m.SetInstanceShaderParameter("wear", new Vector4((float)o.Dirt, (float)o.Blood, (float)o.Torn, (float)o.Wet));
        }
    }

    public float Seed;  // per character, so two people's shirts don't tear alike

    /// <summary>Drop the pieces of garments not worn (a crowd member's outfit doesn't change).</summary>
    public void KeepOnly(Outfit o)
    {
        var worn = o.Worn.Values.ToHashSet();
        foreach (var (name, m) in pieces.ToList())
            if (!worn.Contains(name[..name.IndexOf("__")])) { m.QueueFree(); pieces.Remove(name); }
    }

    public int VisiblePieces => pieces.Values.Count(m => m.Visible);
}
