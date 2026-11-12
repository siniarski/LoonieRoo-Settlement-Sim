// Things lying on the ground in first person (2026-11-09): one model per item kind (art/scripts/build_items.py:
// godot/assets/items/items.glb, a mesh per kind named after it, vertex colours with metalness in the alpha),
// drawn as one MultiMesh per model (ModelSet). Only the nearest MAX_ITEMS within RANGE m are drawn (rule 14), and the
// set is gathered again every RESCAN frames or when the item list's length changes, not every frame.
// Items carried or inside a container aren't drawn. Each rests on the ground (Terrain.Height), tilted to
// its slope, turned and nudged off the tile's centre by a hash of its id (stable between runs: not
// string.GetHashCode, which .NET salts per process). A kind with no model is the old grey box.
// Variants (2026-11-09): some kinds have more than one model ("food", "food_v1", "food_v2"...); each item
// shows one, picked by another hash of its id, so the same tin stays the same tin. The sim knows only the
// kind. Held.cs draws what you hold with the same meshes (MeshFor), so a pipe picked up is a pipe in hand.
// No LOD: measured (ItemBench.tscn, NOTES 2026-11-09), the 300 drawn cost ~0.02 ms of GPU.
// Reads state only (rule 5).

using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.IdHash;

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

    readonly ModelSet models;
    /// <summary>--lineup's items, each shown as a given variant (not by its hash) so all of them are seen.</summary>
    static readonly Dictionary<string, int> forced = new();
    readonly List<(float d, Item item, float x, float z)> near = new();
    int frame, lastCount = -1;
    Vector3 lastEye = new(float.NaN, 0, 0);

    /// <summary>Items drawn (the debug panel).</summary>
    public int Count { get; private set; }

    public ItemModels(Node3D parent)
    {
        // A little more metal and gloss than the world's models (ModelSet's defaults).
        models = new ModelSet(parent, "res://assets/items/items.glb", metal: 0.75f, dull: 0.88f, shiny: 0.42f)
        {
            Fallback = new BoxMesh { Size = new Vector3(0.22f, 0.14f, 0.22f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.55f), Roughness = 0.9f } },
        };
    }

    /// <summary>The model an item is drawn with (its kind's, or one of its variants), or "" for none (the grey box).</summary>
    public string ModelOf(Item item) => models.Pick(item.kind, item.id, forced.TryGetValue(item.id, out var f) ? f : null);

    /// <summary>A model's mesh by name (a kind or a variant, as in items.glb; "hand" is the gloved hand), with the
    /// game's material, or null.</summary>
    public Mesh? MeshFor(string name) => models.MeshOrNull(name);

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
        models.Begin();
        foreach (var (_, item, x, z) in near) models.Add(BenchBox ? "" : ModelOf(item), Place(item, x, z));
        // Bounds: the instances spread over the range round you, so the default AABB (the mesh's) culls wrongly.
        models.End(new Aabb(new Vector3(eye.X - RANGE - 2, -50, eye.Z - RANGE - 2), new Vector3(2 * RANGE + 4, 200, 2 * RANGE + 4)));
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

    /// <summary>Show each of these items as a given model variant, not the one its hash picks (--lineup: the items
    /// come from Sim.Staging.itemLineup, so that all of a kind's models are seen).</summary>
    public static void ShowAs(IEnumerable<(Item item, int variant)> shown)
    {
        foreach (var (item, variant) in shown) forced[item.id] = variant;
    }
}
