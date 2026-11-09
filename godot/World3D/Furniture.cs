// Furniture in first person (2026-11-09): one model per furniture kind or look (art/scripts/build_furniture.py:
// godot/assets/furniture/furniture.glb, a mesh per name, vertex colours with metalness in the alpha, like
// the items), drawn as one MultiMesh per model. A piece's model is its look's for a crate or decor that has
// one (a kitchen counter, a sofa, a car), else its kind's (a built bed, a well); variants ("crate_v1"...)
// by a hash of its id. A piece with no model stays WorldMesh's box (Has tells it which); trees are Trees.cs's.
//
// Which pieces: those in the chunks FirstPerson builds round you (WorldMesh.Lookups buckets them by chunk),
// gathered again when you move into another chunk or the lookups are refreshed (walls, doors, furniture
// changed), not every frame (rule 14).
//
// Facing: the sim has none, so it's read off the grid. A piece with a wall (edge wall, window, barricade, or a
// wall tile) on a side stands with its back to it, pushed back against it (a bed's head at the wall, its foot
// out into the room; a shelf flush to the wall); in a corner, one of the two walls by a hash. Free-standing,
// a piece in a row of its own model faces across the row (warehouse shelves, pews, hay), pews towards the
// altar; anything else one of the four ways by a hash. A car, truck or tractor lies along the way with more
// room (along a garage). A silo stands on 2 x 2 tiles, drawn once over the group's middle. Each stands on
// its tile's floor (Terrain.Floor). Reads state only (rule 5).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class FurnitureModels
{
    /// <summary>Chunks round yours whose furniture is drawn (FirstPerson's radius: the options' Detail distance).</summary>
    static int RADIUS => FirstPerson.Radius;
    const int CHUNK = WorldMesh.CHUNK;
    /// <summary>How far a wall's face is from its tile's centre (half a tile less half a wall).</summary>
    const float WALL_FACE = 0.5f - 0.06f;

    const string SHADER = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
void fragment() {
    ALBEDO = COLOR.rgb;
    METALLIC = COLOR.a * 0.7;
    ROUGHNESS = mix(0.9, 0.45, COLOR.a);
}";

    /// <summary>The model names furniture.glb has (WorldMesh draws no box for a piece with one).</summary>
    static readonly HashSet<string> names = new();
    static readonly string[] LONG = { "car", "truck", "tractor" };

    readonly Node3D parent;
    readonly Dictionary<string, Mesh> meshes = new();
    readonly Dictionary<string, List<string>> variants = new();
    /// <summary>Each model's bounds (to push it against a wall and fit it between its neighbours).</summary>
    readonly Dictionary<string, Aabb> boxes = new();
    readonly Dictionary<string, MultiMeshInstance3D> nodes = new();
    readonly Dictionary<(int, int), Furniture> byTile = new();
    (int, int) lastChunk = (int.MinValue, 0);
    int lastVersion = -1;

    /// <summary>Pieces drawn (the debug panel).</summary>
    public int Count { get; private set; }

    public FurnitureModels(Node3D parent)
    {
        this.parent = parent;
        var material = new ShaderMaterial { Shader = new Shader { Code = SHADER } };
        var scene = GD.Load<PackedScene>("res://assets/furniture/furniture.glb").Instantiate<Node3D>();
        Collect(scene, material);
        scene.Free();
        foreach (var name in meshes.Keys)
        {
            names.Add(name);
            boxes[name] = meshes[name].GetAabb();
            int v = name.LastIndexOf("_v", StringComparison.Ordinal);
            if (v <= 0 || !int.TryParse(name.AsSpan(v + 2), out _)) continue;
            string kind = name[..v];
            if (!meshes.ContainsKey(kind)) continue;
            if (!variants.TryGetValue(kind, out var list)) variants[kind] = list = new() { kind };
            list.Add(name);
        }
        foreach (var list in variants.Values) list.Sort(string.CompareOrdinal);
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

    /// <summary>A piece's model name before variants: its look's (a crate or decor with one), else its kind's.</summary>
    static string Base(Furniture f) =>
        (f.kind == "decor" || f.kind == "crate") && f.look != null && names.Contains(f.look) ? f.look : f.kind;

    /// <summary>Whether a piece is drawn as a model here (so WorldMesh leaves out its box).</summary>
    public static bool Has(Furniture f) => names.Contains(Base(f));

    /// <summary>The model a piece is drawn with (one of its variants by its id's hash), or "" for none.</summary>
    public string ModelOf(Furniture f)
    {
        string b = Base(f);
        if (!meshes.ContainsKey(b)) return "";
        if (!variants.TryGetValue(b, out var list)) return b;
        return list[(int)(Mix(Fnv(f.id)) % (uint)list.Count)];
    }

    public void Update(SimState state, Vector3 eye, WorldMesh.Lookups look)
    {
        var chunk = ((int)Math.Floor(eye.X / CHUNK), (int)Math.Floor(eye.Z / CHUNK));
        if (chunk == lastChunk && look.Version == lastVersion) return;
        lastChunk = chunk;
        lastVersion = look.Version;
        // Every piece in range, and a tile index of them (a ring wider, for neighbours at the edge).
        byTile.Clear();
        var near = new List<Furniture>();
        for (int cy = chunk.Item2 - RADIUS - 1; cy <= chunk.Item2 + RADIUS + 1; cy++)
            for (int cx = chunk.Item1 - RADIUS - 1; cx <= chunk.Item1 + RADIUS + 1; cx++)
            {
                if (!look.furniture.TryGetValue((cx, cy), out var list)) continue;
                bool inRange = Math.Abs(cx - chunk.Item1) <= RADIUS && Math.Abs(cy - chunk.Item2) <= RADIUS;
                foreach (var f in list)
                {
                    if (f.kind == "tree") continue;
                    byTile[(f.x, f.y)] = f;
                    if (inRange) near.Add(f);
                }
            }
        var byModel = new Dictionary<string, List<Transform3D>>();
        foreach (var f in near)
        {
            string model = ModelOf(f);
            if (model == "") continue;
            if (Place(state.grid, f, model) is not { } t) continue;
            if (!byModel.TryGetValue(model, out var l)) byModel[model] = l = new();
            l.Add(t);
        }
        int count = 0;
        float span = (2 * RADIUS + 1) * CHUNK + 8;
        var bounds = new Aabb(new Vector3((chunk.Item1 - RADIUS) * CHUNK - 4, -60, (chunk.Item2 - RADIUS) * CHUNK - 4), new Vector3(span, 200, span));
        foreach (var (model, list) in byModel)
        {
            if (!nodes.TryGetValue(model, out var node))
            {
                node = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = meshes[model] } };
                parent.AddChild(node);
                nodes[model] = node;
            }
            var mm = node.Multimesh;
            if (mm.InstanceCount < list.Count) mm.InstanceCount = Math.Max(list.Count, mm.InstanceCount * 2);
            mm.VisibleInstanceCount = list.Count;
            for (int k = 0; k < list.Count; k++) mm.SetInstanceTransform(k, list[k]);
            node.CustomAabb = bounds;
            count += list.Count;
        }
        foreach (var (model, node) in nodes)
            if (!byModel.ContainsKey(model)) node.Multimesh.VisibleInstanceCount = 0;
        Count = count;
    }

    // The four ways, as grid steps: north, east, south, west.
    static readonly (int dx, int dy)[] DIRS = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    /// <summary>Whether something you can't put furniture's back through stands on the tile's side d (0 north, 1
    /// east, 2 south, 3 west): an edge wall, window or barricade, or a wall tile beyond. A door isn't.</summary>
    static bool WallOn(Grid g, int x, int y, int d)
    {
        var (dx, dy) = DIRS[d];
        int nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= g.width || ny >= g.height) return false;
        int e = d switch
        {
            0 => (y * g.width + x) * 2,
            2 => (ny * g.width + x) * 2,
            3 => (y * g.width + x) * 2 + 1,
            _ => (y * g.width + nx) * 2 + 1,
        };
        int kind = g.edges[e];
        if (kind is Grids.EDGE_WALL or Grids.EDGE_WINDOW or Grids.EDGE_WINDOW_BROKEN or Grids.EDGE_BARRICADE) return true;
        return g.tiles[ny * g.width + nx] == "wall";
    }

    /// <summary>Tiles clear of walls from (x, y) along d, up to max.</summary>
    static int Run(Grid g, int x, int y, int d, int max)
    {
        int n = 0;
        while (n < max && !WallOn(g, x + DIRS[d].dx * n, y + DIRS[d].dy * n, d)) n++;
        return n;
    }

    bool Same(int x, int y, string b) => byTile.TryGetValue((x, y), out var o) && Base(o) == b;

    /// <summary>Where and which way a piece stands, or null when it isn't drawn on its own (a silo's other tiles).</summary>
    Transform3D? Place(Grid g, Furniture f, string model)
    {
        string b = Base(f);
        uint h = Fnv(f.id);
        float px = f.x + 0.5f, pz = f.y + 0.5f;
        float floor = Terrain.Floor(f.x, f.y);
        if (b == "silo")
        {
            if (Same(f.x - 1, f.y, b) || Same(f.x, f.y - 1, b)) return null;
            bool wide = Same(f.x + 1, f.y, b), deep = Same(f.x, f.y + 1, b);
            px = f.x + (wide ? 1 : 0.5f);
            pz = f.y + (deep ? 1 : 0.5f);
            return new Transform3D(new Basis(Vector3.Up, (h & 3) * Mathf.Pi / 2), new Vector3(px, floor, pz));
        }
        if (Array.IndexOf(LONG, b) >= 0)
        {
            // Along the axis with more room (both ways summed), either end first by the hash.
            int along = Run(g, f.x, f.y, 1, 3) + Run(g, f.x, f.y, 3, 3);
            int across = Run(g, f.x, f.y, 0, 3) + Run(g, f.x, f.y, 2, 3);
            float yaw = (across > along ? Mathf.Pi / 2 : 0) + ((h & 1) == 0 ? 0 : Mathf.Pi);
            return new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(px, floor, pz));
        }
        // Which side its back is to (0 north, 1 east, 2 south, 3 west).
        var walls = new List<int>(4);
        for (int d = 0; d < 4; d++) if (WallOn(g, f.x, f.y, d)) walls.Add(d);
        int backTo;
        bool against = walls.Count > 0;
        if (against) backTo = walls[(int)((h >> 4) % (uint)walls.Count)];
        else
        {
            bool rowX = Same(f.x - 1, f.y, b) || Same(f.x + 1, f.y, b);
            bool rowY = Same(f.x, f.y - 1, b) || Same(f.x, f.y + 1, b);
            int flip = (int)((h >> 4) & 1);
            backTo = rowX && !rowY ? flip * 2 : rowY && !rowX ? 1 + flip * 2 : (int)((h >> 4) & 3);
            if (b == "pew" && Altar(f) is { } a)
            {
                // Face the altar along the way it lies furthest.
                int ax = a.x - f.x, ay = a.y - f.y;
                backTo = Math.Abs(ay) >= Math.Abs(ax) ? (ay < 0 ? 2 : 0) : (ax < 0 ? 1 : 3);
            }
        }
        var (bx, bz) = DIRS[backTo];
        // The model's front is its +Z: turn it to face away from its back.
        float turn = Mathf.Atan2(-bx, -bz);
        // Fit it to the room: its sides may overhang its tile, but not through a wall or into a neighbour's
        // piece (a sofa beside the television); pushed back flush to its wall; squeezed if it can't fit.
        int right = Dir(Mathf.Cos(turn), -Mathf.Sin(turn)), front = (backTo + 2) % 4, left = (right + 2) % 4;
        var box = boxes[model];
        var (sx, ox) = Fit(-box.Position.X, box.End.X, Limit(g, f, left), Limit(g, f, right), false);
        var (sz, oz) = Fit(-box.Position.Z, box.End.Z, against ? WALL_FACE : Limit(g, f, backTo), Limit(g, f, front), against);
        var rot = new Basis(Vector3.Up, turn);
        var at = new Vector3(px, floor, pz) + rot * new Vector3(ox, 0, oz);
        return new Transform3D(rot * Basis.FromScale(new Vector3(sx, 1, sz)), at);
    }

    /// <summary>How far from its tile's centre a piece may reach on side d: to a wall's face, half way to a
    /// neighbouring piece, or anywhere.</summary>
    float Limit(Grid g, Furniture f, int d)
    {
        if (WallOn(g, f.x, f.y, d)) return WALL_FACE;
        return byTile.ContainsKey((f.x + DIRS[d].dx, f.y + DIRS[d].dy)) ? 0.5f : float.PositiveInfinity;
    }

    /// <summary>Along one axis, a model reaching lo behind and hi ahead of its origin, in room from -limLo to
    /// limHi: the scale (1 unless it can't fit) and where its origin goes (the least move that fits, or flush
    /// against the low side).</summary>
    static (float scale, float offset) Fit(float lo, float hi, float limLo, float limHi, bool flush)
    {
        float s = Math.Min(1, (limLo + limHi) / (lo + hi));
        lo *= s; hi *= s;
        if (flush) return (s, -limLo + lo);
        float min = -limLo + lo, max = limHi - hi;
        return (s, Math.Clamp(0, min, Math.Max(min, max)));
    }

    /// <summary>The grid way (0 north, 1 east, 2 south, 3 west) a unit vector (x, z) points nearest.</summary>
    static int Dir(float x, float z) =>
        Math.Abs(x) > Math.Abs(z) ? (x > 0 ? 1 : 3) : (z > 0 ? 2 : 0);

    /// <summary>The nearest altar among the pieces gathered, or null.</summary>
    Furniture? Altar(Furniture pew)
    {
        Furniture? best = null;
        int bestD = int.MaxValue;
        for (int dy = -12; dy <= 12; dy++)
            for (int dx = -12; dx <= 12; dx++)
                if (byTile.TryGetValue((pew.x + dx, pew.y + dy), out var o) && Base(o) == "altar" && dx * dx + dy * dy < bestD)
                {
                    best = o;
                    bestD = dx * dx + dy * dy;
                }
        return best;
    }

    /// <summary>--lineup=furniture: one of every furniture kind and look in the open east of you (the land round
    /// you generated first, so --at can go anywhere): rows of eight, three tiles apart, the big ones (vehicles,
    /// the silo, the tower) in a row of their own beyond. A debug start for screenshots, like --lineup: not a
    /// command, so not in the log.</summary>
    public static void Lineup(SimState state, Survivor you)
    {
        Worldgen.ensureGeneratedAround(state, you.pos);
        string[] kinds = ["bed", "table", "workbench", "stove", "trap", "well", "collector", "generator", "lamp", "fridge",
            "heater", "coop", "radio", "lab", "beacon", "turret", "lure", "pit", "crate"];
        var looks = new List<(string kind, string? look)>();
        foreach (var k in kinds) looks.Add((k, null));
        foreach (var l in FurnitureMod.CONTAINER_LOOKS) if (l != "crate") looks.Add(("crate", l));
        foreach (var l in FurnitureMod.DECOR_LOOKS) if (l is not ("bed" or "table" or "car" or "tractor" or "silo")) looks.Add(("decor", l));
        for (int k = 0; k < looks.Count; k++)
        {
            var f = FurnitureMod.addFurniture(state, looks[k].kind, you.pos.x + 3 + 3 * (k % 8), you.pos.y - 6 + 3 * (k / 8));
            f.look = looks[k].look;
        }
        int y = you.pos.y - 6 + 3 * ((looks.Count + 7) / 8) + 2;
        (string kind, string? look, int dx)[] big = [("truck", null, 3), ("decor", "car", 10), ("decor", "tractor", 16), ("tower", null, 21), ("decor", "silo", 25)];
        foreach (var (kind, look, dx) in big)
        {
            int n = look == "silo" ? 2 : 1;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    FurnitureMod.addFurniture(state, kind, you.pos.x + dx + a, y + b).look = look;
        }
    }

    static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    static uint Mix(uint h)
    {
        h ^= h >> 16; h *= 0x85EBCA6B;
        h ^= h >> 13; h *= 0xC2B2AE35;
        h ^= h >> 16;
        return h;
    }
}
