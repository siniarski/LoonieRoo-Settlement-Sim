// Things lying on the ground in first person (2026-11-09): one model per item kind (art/scripts/build_items.py:
// godot/assets/items/items.glb, a mesh per kind named after it, vertex colours with metalness in the alpha),
// drawn as one MultiMesh per kind. Only the nearest MAX_ITEMS within RANGE m are drawn (rule 14), and the
// set is gathered again every RESCAN frames or when the item list's length changes, not every frame.
// Items carried or inside a container aren't drawn. Each rests on the ground (Terrain.Height), tilted to
// its slope, turned and nudged off the tile's centre by a hash of its id (stable between runs: not
// string.GetHashCode, which .NET salts per process). A kind with no model is the old grey box.
// Variants (2026-11-09): some kinds have more than one model ("food", "food_v1", "food_v2"...); each item
// shows one, picked by another hash of its id, so the same tin stays the same tin. The sim knows only the
// kind. Held.cs draws what you hold with the same meshes (MeshFor), so a pipe picked up is a pipe in hand.
// No LOD: measured (ItemBench.tscn, NOTES 2026-11-09), the 300 drawn cost ~0.02 ms of GPU.
// Reads state only (rule 5).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class ItemModels
{
    /// <summary>Items further than this (m) aren't drawn.</summary>
    const float RANGE = 40;
    /// <summary>The most drawn at once, nearest first.</summary>
    const int MAX_ITEMS = 300;
    /// <summary>Frames between gathering which items to draw (sooner when one is picked up or dropped).</summary>
    const int RESCAN = 6;
    /// <summary>The item bench's knobs (ItemBench.cs): a cap other than MAX_ITEMS (-1: the game's), and every item as the grey box.</summary>
    public static int BenchMax = -1;
    public static bool BenchBox;

    const string SHADER = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
void fragment() {
    ALBEDO = COLOR.rgb;
    METALLIC = COLOR.a * 0.75;
    ROUGHNESS = mix(0.88, 0.42, COLOR.a);
}";

    readonly Node3D parent;
    readonly Dictionary<string, Mesh> meshes = new();
    /// <summary>A kind's models: its own name first, then its variants in order.</summary>
    readonly Dictionary<string, List<string>> variants = new();
    /// <summary>--lineup's items, each shown as a given variant (not by its hash) so all of them are seen.</summary>
    static readonly Dictionary<string, int> forced = new();
    readonly Mesh fallback;
    readonly Dictionary<string, MultiMeshInstance3D> nodes = new();
    readonly List<(float d, Item item, float x, float z)> near = new();
    int frame, lastCount = -1;
    Vector3 lastEye = new(float.NaN, 0, 0);

    /// <summary>Items drawn (the debug panel).</summary>
    public int Count { get; private set; }

    public ItemModels(Node3D parent)
    {
        this.parent = parent;
        var material = new ShaderMaterial { Shader = new Shader { Code = SHADER } };
        var scene = GD.Load<PackedScene>("res://assets/items/items.glb").Instantiate<Node3D>();
        Collect(scene, material);
        scene.Free();
        foreach (var name in meshes.Keys)
        {
            int v = name.LastIndexOf("_v", StringComparison.Ordinal);
            if (v <= 0 || !int.TryParse(name.AsSpan(v + 2), out _)) continue;
            string kind = name[..v];
            if (!meshes.ContainsKey(kind)) continue;
            if (!variants.TryGetValue(kind, out var list)) variants[kind] = list = new() { kind };
            list.Add(name);
        }
        foreach (var list in variants.Values) list.Sort(string.CompareOrdinal);
        fallback = new BoxMesh { Size = new Vector3(0.22f, 0.14f, 0.22f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.55f), Roughness = 0.9f } };
    }

    void Collect(Node n, Material material)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) mi.Mesh.SurfaceSetMaterial(i, material);
            meshes[n.Name] = mi.Mesh;
        }
        foreach (var c in n.GetChildren()) Collect(c, material);
    }

    /// <summary>The model an item is drawn with (its kind's, or one of its variants), or "" for none (the grey box).</summary>
    public string ModelOf(Item item)
    {
        if (!meshes.ContainsKey(item.kind)) return "";
        if (!variants.TryGetValue(item.kind, out var list)) return item.kind;
        int k = forced.TryGetValue(item.id, out var f) ? f : (int)(Mix(Fnv(item.id)) % (uint)list.Count);
        return list[k % list.Count];
    }

    /// <summary>A model's mesh by name ("hand" is the gloved hand), or null.</summary>
    public Mesh? MeshFor(string name) => meshes.GetValueOrDefault(name);

    /// <summary>How many models a kind has (1 with no variants, 0 with none).</summary>
    public int VariantCount(string kind) => variants.TryGetValue(kind, out var l) ? l.Count : meshes.ContainsKey(kind) ? 1 : 0;

    public void Update(SimState state, Vector3 eye)
    {
        bool moved = float.IsNaN(lastEye.X) || (eye - lastEye).LengthSquared() > 1;
        if (++frame % RESCAN != 0 && state.items.Count == lastCount && !moved) return;
        lastCount = state.items.Count;
        lastEye = eye;
        near.Clear();
        foreach (var i in state.items)
        {
            if (i.pos == null || i.carriedBy != null || i.inside != null) continue;
            float x = i.pos.x + 0.5f, z = i.pos.y + 0.5f;
            float d = (x - eye.X) * (x - eye.X) + (z - eye.Z) * (z - eye.Z);
            if (d <= RANGE * RANGE) near.Add((d, i, x, z));
        }
        near.Sort((a, b) => a.d.CompareTo(b.d));
        int max = BenchMax >= 0 ? BenchMax : MAX_ITEMS;
        if (near.Count > max) near.RemoveRange(max, near.Count - max);
        Count = near.Count;

        var byKind = new Dictionary<string, List<Transform3D>>();
        foreach (var (_, item, x, z) in near)
        {
            string kind = BenchBox ? "" : ModelOf(item);
            if (!byKind.TryGetValue(kind, out var list)) byKind[kind] = list = new();
            list.Add(Place(item, x, z));
        }
        foreach (var (kind, list) in byKind)
        {
            if (!nodes.TryGetValue(kind, out var node))
            {
                node = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = kind == "" ? fallback : meshes[kind] } };
                parent.AddChild(node);
                nodes[kind] = node;
            }
            var mm = node.Multimesh;
            if (mm.InstanceCount < list.Count) mm.InstanceCount = Math.Max(list.Count, mm.InstanceCount * 2);
            mm.VisibleInstanceCount = list.Count;
            for (int k = 0; k < list.Count; k++) mm.SetInstanceTransform(k, list[k]);
            // Bounds: the instances spread over the range round you, so the default AABB (the mesh's) culls wrongly.
            node.CustomAabb = new Aabb(new Vector3(eye.X - RANGE - 2, -50, eye.Z - RANGE - 2), new Vector3(2 * RANGE + 4, 200, 2 * RANGE + 4));
        }
        foreach (var (kind, node) in nodes)
            if (!byKind.ContainsKey(kind)) node.Multimesh.VisibleInstanceCount = 0;
    }

    /// <summary>Where an item lies: off its tile's centre and turned by its id's hash, on the ground, tilted to the slope.</summary>
    static Transform3D Place(Item item, float x, float z)
    {
        uint h = Fnv(item.id);
        float jx = ((h & 0xff) / 255f - 0.5f) * 0.4f, jz = (((h >> 8) & 0xff) / 255f - 0.5f) * 0.4f;
        float yaw = ((h >> 16) & 0xffff) / 65535f * Mathf.Tau;
        // Big things stay on their tile.
        if (item.kind is "cart" or "corpse") { jx *= 0.3f; jz *= 0.3f; }
        float px = x + jx, pz = z + jz;
        const float E = 0.25f;
        float y = Terrain.Height(px, pz);
        var up = new Vector3(Terrain.Height(px - E, pz) - Terrain.Height(px + E, pz), 2 * E, Terrain.Height(px, pz - E) - Terrain.Height(px, pz + E)).Normalized();
        var turn = new Basis(Vector3.Up, yaw);
        var basis = up.Y > 0.9999f ? turn : new Basis(new Quaternion(Vector3.Up, up)) * turn;
        return new Transform3D(basis, new Vector3(px, y, pz));
    }

    static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>A second hash from the first (a finaliser), so the variant doesn't follow the yaw's or the nudge's bits.</summary>
    static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x85EBCA6B;
        h ^= h >> 13; h *= 0xC2B2AE35;
        h ^= h >> 16;
        return h;
    }

    /// <summary>--lineup: one of every kind with a model on the ground in front of you, three rows of seven
    /// eastward, each its first model. --lineup=variants: the kinds with variants instead, a kind to a row
    /// (north to south) and its models eastward, the first nearest. A debug start for screenshots, like --at:
    /// not a command, so not in the log.</summary>
    public static void Lineup(SimState state, Survivor you)
    {
        bool all = Array.Exists(OS.GetCmdlineUserArgs(), a => a.TrimStart('-') == "lineup=variants");
        if (all)
        {
            string[] many = ["food", "wood", "medicine", "bandage", "weapon", "cloth", "book", "scrap", "coat", "boots", "ammo", "meal"];
            for (int k = 0; k < many.Length; k++)
                for (int v = 0; v < 4; v++)
                {
                    var it = LoonieRoo.Sim.Items.spawnItem(state, many[k], you.pos.x + 2 + v, you.pos.y - many.Length / 2 + k);
                    forced[it.id] = v;
                }
            return;
        }
        string[] kinds = ["wood", "food", "meal", "bandage", "medicine", "medkit", "weapon", "cloth", "coat", "armour", "boots",
            "gun", "ammo", "corpse", "book", "cart", "axe", "hammer", "sample", "scrap", "fuel"];
        for (int k = 0; k < kinds.Length; k++)
            forced[LoonieRoo.Sim.Items.spawnItem(state, kinds[k], you.pos.x + 2 + k / 3, you.pos.y - 1 + k % 3).id] = 0;
    }
}
