// Roofs and what's indoors, for first person. Reads state only (rule 5).
//
// What a building is, and so what's indoors:
//   - Generated buildings come from the world plan (Worldplan: lots, Lots.layoutLot: each lot's
//     placements, Buildings: the plans). A plan's FLOOR cells, turned onto the map, are the
//     building's footprint; its outer walls stand on the footprint's edge (Buildings.toEdges).
//     The plan is derived from the seed, so the footprint is known whatever state the walls are in.
//     A footprint is indoors while the building stands: at least STANDING of its outer edges
//     still hold a wall, door, window or barricade. Below that it's a ruin: no roof, no ceiling
//     (so knocking walls down never leaves a roof floating; a few holes leave it on).
//   - Anything else the sim calls a room (Rooms: an enclosed region of floor, flood-filled
//     between walls and doors, at most Rooms.MAX_ROOM_TILES) is somewhere the player walled in:
//     indoors, with a flat roof. Hand-made maps (no world plan) are all this kind.
// Roofs: per building, their own node (a roof spans chunk edges); pitched for houses, barns,
// sheds and churches (gable or hip, by a hash of the lot), flat with a parapet for the rest
// (shops, civic buildings, warehouses). Player rooms share one node of flat roofs, rebuilt when
// walls change. Only lots near you are known (lotsByChunk), a few roofs are built a frame, and
// the standing test and rooms rerun only when walkabilityVersion changes or you move a chunk (rule 14).
// From inside, the ceiling (WorldMesh, at WALL_H over Indoor tiles) hides the roof above it.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Roofs
{
    /// <summary>A building stands (keeps its roof) while at least this share of its outer edges hold something.</summary>
    const double STANDING = 0.5;
    /// <summary>Roof meshes built per frame at most.</summary>
    const int BUILDS_PER_FRAME = 3;
    /// <summary>Lots this far (m) beyond the chunks first person keeps are known, so their ceilings are in before the chunk is built.</summary>
    const int MARGIN = 24;

    // Heights (m) over the building's floor (Terrain.Floor: a generated building sits on one level): the walls' top is WorldMesh.WALL_H.
    const float EAVE_OVERHANG = 0.35f, GABLE_OVERHANG = 0.3f, SLAB = 0.12f, PARAPET = 0.55f, CAP = 0.06f;
    /// <summary>Rise per metre across: 32 degrees for houses, steeper for barns and churches.</summary>
    const float PITCH = 0.62f, PITCH_STEEP = 0.9f;

    // Muted, weathered colours, given in sRGB (the vertex colours are linear).
    static Color C(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();
    static readonly Color[] SHINGLES = { C(0.27f, 0.26f, 0.25f), C(0.33f, 0.27f, 0.22f), C(0.26f, 0.29f, 0.28f), C(0.37f, 0.24f, 0.2f), C(0.4f, 0.39f, 0.37f) };
    static readonly Color TIN = C(0.42f, 0.27f, 0.2f);
    static readonly Color TAR = C(0.2f, 0.2f, 0.21f);
    static readonly Color COPING = C(0.52f, 0.5f, 0.47f);
    static readonly Color SOFFIT = C(0.3f, 0.29f, 0.27f);
    static readonly Color FASCIA = C(0.44f, 0.41f, 0.37f);
    static readonly Color BUILT = C(0.36f, 0.33f, 0.29f);

    static readonly HashSet<string> PITCHED = new() { "house", "barn", "shed", "church" };

    sealed class Building
    {
        public string kind = "";
        public double roll;
        public readonly HashSet<int> tiles = new();
        /// <summary>Each outer edge: its edge index (-1 at the map's edge) and the tile outside it.</summary>
        public readonly List<(int edge, int outside)> perimeter = new();
        public bool standing;
        public MeshInstance3D? node;
    }

    readonly Node3D parent;
    readonly StandardMaterial3D material = new() { VertexColorUseAsAlbedo = true, Roughness = 1 };
    readonly Dictionary<int, List<Building>> lots = new();
    MeshInstance3D? built;
    object? grid;
    (int, int) at = (int.MinValue, 0);
    int version = -1, generated = -1;

    /// <summary>Tiles under a roof: generated buildings that stand, and rooms you walled in. WorldMesh puts a ceiling over them.</summary>
    public readonly HashSet<int> Indoor = new();
    /// <summary>Bumped whenever Indoor changes, so chunks look again.</summary>
    public int Version { get; private set; }
    public int RoofCount { get; private set; }

    public Roofs(Node3D parent) => this.parent = parent;

    public void Update(SimState state, Vector3 eye, int keepRadius)
    {
        var g = state.grid;
        if (!ReferenceEquals(g, grid)) Reset(g);
        int cx = (int)Math.Floor(eye.X / WorldMesh.CHUNK), cy = (int)Math.Floor(eye.Z / WorldMesh.CHUNK);
        // Walls change (walkabilityVersion), or more of the world is drawn (a lot's chunk comes in later).
        int gen = 0;
        if (state.world != null) foreach (var c in state.world.generated) gen += (int)c;
        bool dirty = state.walkabilityVersion != version || gen != generated;
        generated = gen;
        if ((cx, cy) != at)
        {
            at = (cx, cy);
            Near(state, (keepRadius + 1) * WorldMesh.CHUNK + MARGIN, eye);
            dirty = true;
        }
        if (dirty)
        {
            version = state.walkabilityVersion;
            Recompute(state, eye, (keepRadius + 1) * WorldMesh.CHUNK);
        }
        int made = 0;
        foreach (var list in lots.Values)
            foreach (var b in list)
            {
                if (b.standing && b.node == null && made < BUILDS_PER_FRAME)
                {
                    b.node = new MeshInstance3D { Mesh = BuildingMesh(state, b), CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
                    parent.AddChild(b.node);
                    made++;
                }
                if (b.node != null) b.node.Visible = b.standing;
            }
    }

    void Reset(Grid g)
    {
        grid = g;
        foreach (var list in lots.Values) foreach (var b in list) b.node?.QueueFree();
        lots.Clear();
        built?.QueueFree();
        built = null;
        at = (int.MinValue, 0);
        version = generated = -1;
    }

    /// <summary>Learns the lots within `range` of the eye (their footprints are fixed) and forgets those well beyond it.</summary>
    void Near(SimState state, int range, Vector3 eye)
    {
        if (state.world == null) return;
        var regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
        int chunks = (int)Math.Ceiling(state.world.size / Worldplan.PLAN_CHUNK);
        var want = new HashSet<int>();
        int px0 = (int)Math.Floor((eye.X - range) / Worldplan.PLAN_CHUNK), px1 = (int)Math.Floor((eye.X + range) / Worldplan.PLAN_CHUNK);
        int py0 = (int)Math.Floor((eye.Z - range) / Worldplan.PLAN_CHUNK), py1 = (int)Math.Floor((eye.Z + range) / Worldplan.PLAN_CHUNK);
        for (int py = Math.Max(0, py0); py <= Math.Min(chunks - 1, py1); py++)
            for (int px = Math.Max(0, px0); px <= Math.Min(chunks - 1, px1); px++)
                foreach (var id in regions.LotsInChunk(px, py)) want.Add((int)id);
        var drop = new List<int>();
        foreach (var id in lots.Keys) if (!want.Contains(id)) drop.Add(id);
        foreach (var id in drop)
        {
            foreach (var b in lots[id]) b.node?.QueueFree();
            lots.Remove(id);
        }
        foreach (var id in want)
            if (!lots.ContainsKey(id)) lots[id] = Footprints(state, regions.Lot(id));
    }

    static List<Building> Footprints(SimState state, Lot lot)
    {
        var g = state.grid;
        var list = new List<Building>();
        var layout = Lots.layoutLot(state.world!.seed, lot);
        for (int bi = 0; bi < layout.buildings.Count; bi++)
        {
            var p = layout.buildings[bi];
            var plan = p.plan;
            var b = new Building { kind = plan.kind, roll = Hash.hashRoll(state.world.seed, lot.x, lot.y, 0x7200 + bi) };
            for (int y = 0; y < plan.h; y++)
                for (int x = 0; x < plan.w; x++)
                {
                    if (Buildings.cellAt(plan, x, y) == Buildings.OUTSIDE) continue;
                    var t = Lots.transform(plan, x, y, p.facing, p.mirror);
                    int mx = p.ox + t.x, my = p.oy + t.y;
                    if (Grids.inBounds(g, mx, my)) b.tiles.Add(my * g.width + mx);
                }
            foreach (int i in b.tiles)
            {
                int x = i % g.width, y = i / g.width;
                foreach (var (dx, dy) in DIR4)
                {
                    int nx = x + dx, ny = y + dy;
                    int n = ny * g.width + nx;
                    if (Grids.inBounds(g, nx, ny) && b.tiles.Contains(n)) continue;
                    b.perimeter.Add((Grids.edgeBetween(g, x, y, nx, ny), Grids.inBounds(g, nx, ny) ? n : -1));
                }
            }
            if (b.tiles.Count > 0) list.Add(b);
        }
        return list;
    }

    static readonly (int, int)[] DIR4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    static bool Stands(Grid g, Building b)
    {
        int held = 0;
        foreach (var (e, outside) in b.perimeter)
        {
            if (e >= 0 && g.edges[e] != Grids.EDGE_NONE) held++;
            else if (outside >= 0 && g.tiles[outside] is "wall" or "door" or "barricade") held++;
        }
        return b.perimeter.Count > 0 && held >= STANDING * b.perimeter.Count;
    }

    /// <summary>Which buildings stand, which rooms you walled in, and so what's indoors; the flat roofs over your rooms.</summary>
    void Recompute(SimState state, Vector3 eye, int range)
    {
        var g = state.grid;
        var was = new HashSet<int>(Indoor);
        Indoor.Clear();
        var footprint = new HashSet<int>();
        int count = 0;
        foreach (var list in lots.Values)
            foreach (var b in list)
            {
                footprint.UnionWith(b.tiles);
                b.standing = Stands(g, b);
                if (b.standing) { Indoor.UnionWith(b.tiles); count++; }
            }
        // Rooms you walled in: enclosed floor that isn't a generated building's.
        var map = Rooms.getRoomMap(state);
        var mine = new List<List<int>>();
        foreach (var room in map.rooms)
        {
            if (room.maxX < eye.X - range || room.minX > eye.X + range || room.maxY < eye.Z - range || room.minY > eye.Z + range) continue;
            var tiles = new List<int>();
            foreach (var t in room.tiles) if (!footprint.Contains((int)t)) tiles.Add((int)t);
            if (tiles.Count == 0) continue;
            Indoor.UnionWith(tiles);
            mine.Add(tiles);
        }
        built?.QueueFree();
        built = null;
        if (mine.Count > 0)
        {
            var st = Begin();
            foreach (var tiles in mine) FlatBuilt(state, st, tiles);
            built = new MeshInstance3D { Mesh = End(st), CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided };
            parent.AddChild(built);
        }
        RoofCount = count + mine.Count;
        if (!was.SetEquals(Indoor)) Version++;
    }

    // --- the meshes ---

    SurfaceTool Begin()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        return st;
    }

    ArrayMesh End(SurfaceTool st)
    {
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    ArrayMesh BuildingMesh(SimState state, Building b)
    {
        var g = state.grid;
        var st = Begin();
        float floor = float.MaxValue;
        foreach (int i in b.tiles) floor = Math.Min(floor, Terrain.Floor(i % g.width, i / g.width));
        float top = floor + WorldMesh.WALL_H;
        var rects = Rectangles(g, b.tiles);
        if (PITCHED.Contains(b.kind))
        {
            bool steep = b.kind is "barn" or "church";
            var colour = b.kind == "barn" ? TIN : SHINGLES[(int)(b.roll * SHINGLES.Length) % SHINGLES.Length];
            bool hip = b.kind == "house" && b.roll > 0.55;
            // The biggest part first: the rest butt into it.
            rects.Sort((p, q) => (q.w * q.h).CompareTo(p.w * p.h));
            foreach (var r in rects) Pitched(st, r, top, steep ? PITCH_STEEP : PITCH, colour, hip && r.w > 2 && r.h > 2);
        }
        else Flat(st, g, b.tiles, rects, top);
        return End(st);
    }

    record struct Rect(int x, int y, int w, int h);

    /// <summary>A footprint cut into rectangles: rows run along x as far as they go, then grow down while the row below matches.</summary>
    static List<Rect> Rectangles(Grid g, IEnumerable<int> tiles)
    {
        var left = new SortedSet<int>(tiles);
        var rects = new List<Rect>();
        while (left.Count > 0)
        {
            int i = left.Min;
            int x = i % g.width, y = i / g.width;
            int w = 1;
            while (x + w < g.width && left.Contains(i + w)) w++;
            int h = 1;
            while (true)
            {
                int row = (y + h) * g.width + x;
                bool full = y + h < g.height;
                for (int k = 0; full && k < w; k++) full = left.Contains(row + k);
                if (!full) break;
                h++;
            }
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++) left.Remove(yy * g.width + xx);
            rects.Add(new Rect(x, y, w, h));
        }
        return rects;
    }

    /// <summary>A gable or hip roof over a rectangle, its ridge along the longer side; eaves overhang the walls.</summary>
    static void Pitched(SurfaceTool st, Rect r, float top, float pitch, Color colour, bool hip)
    {
        bool alongX = r.w >= r.h;
        float L = alongX ? r.w : r.h, D = alongX ? r.h : r.w;
        // Roof frame: u along the ridge, v across it, h up; to the world.
        Vector3 P(float u, float h, float v) => alongX ? new(r.x + u, h, r.y + v) : new(r.x + v, h, r.y + u);
        float o = EAVE_OVERHANG, base0 = top + 0.02f;
        float rise = D / 2 * pitch;
        float eaveH = base0 - o * pitch, ridgeH = base0 + rise;
        var under = SOFFIT;
        if (!hip)
        {
            float og = GABLE_OVERHANG;
            float u0 = -og, u1 = L + og;
            foreach (var (v0, sign) in new[] { (-o, 1f), (D + o, -1f) })
            {
                float vr = D / 2;
                // The slope, its underside and the fascia board along the eave.
                Quad(st, P(u0, eaveH + SLAB, v0), P(u1, eaveH + SLAB, v0), P(u1, ridgeH + SLAB, vr), P(u0, ridgeH + SLAB, vr), colour, Vector3.Up);
                Quad(st, P(u0, eaveH, v0), P(u1, eaveH, v0), P(u1, ridgeH, vr), P(u0, ridgeH, vr), under, Vector3.Down);
                Quad(st, P(u0, eaveH, v0), P(u1, eaveH, v0), P(u1, eaveH + SLAB, v0), P(u0, eaveH + SLAB, v0), FASCIA, P(0, 0, -sign) - P(0, 0, 0));
                // The slab's ends (barge boards).
                foreach (var (u, out_) in new[] { (u0, -1f), (u1, 1f) })
                {
                    var n = P(out_, 0, 0) - P(0, 0, 0);
                    Quad(st, P(u, eaveH, v0), P(u, ridgeH, vr), P(u, ridgeH + SLAB, vr), P(u, eaveH + SLAB, v0), FASCIA, n);
                }
            }
            // The gable ends: wall up from the wall tops to under the ridge, just outside the end walls.
            const float t = 0.06f;
            foreach (var (u, out_) in new[] { (-t, -1f), (L + t, 1f) })
            {
                var n = P(out_, 0, 0) - P(0, 0, 0);
                Tri(st, P(u, top, -t), P(u, top, D + t), P(u, base0 + rise, D / 2), WorldMesh.WALL, n);
                // ...and its inner face, should you see it from under a broken ceiling.
                Tri(st, P(u + out_ * -0.02f, top, -t), P(u + out_ * -0.02f, top, D + t), P(u + out_ * -0.02f, base0 + rise, D / 2), WorldMesh.WALL, -n);
            }
            return;
        }
        // Hip: four slopes meeting at a ridge D/2 in from each end (a point for a square).
        float e0 = -o, eu1 = L + o, ev1 = D + o;
        float r0 = D / 2, r1 = L - D / 2;
        if (r1 < r0) r0 = r1 = L / 2;
        float vm = D / 2;
        foreach (float lift in new[] { SLAB, 0f })
        {
            var c = lift > 0 ? colour : under;
            var up = lift > 0 ? Vector3.Up : Vector3.Down;
            float eh = eaveH + lift, rh = ridgeH + lift;
            Quad(st, P(e0, eh, e0), P(eu1, eh, e0), P(r1, rh, vm), P(r0, rh, vm), c, up);
            Quad(st, P(e0, eh, ev1), P(eu1, eh, ev1), P(r1, rh, vm), P(r0, rh, vm), c, up);
            Tri(st, P(e0, eh, e0), P(e0, eh, ev1), P(r0, rh, vm), c, up);
            Tri(st, P(eu1, eh, e0), P(eu1, eh, ev1), P(r1, rh, vm), c, up);
        }
        // Fascia round the eaves.
        Vector3 Out(float du, float dv) => P(du, 0, dv) - P(0, 0, 0);
        Quad(st, P(e0, eaveH, e0), P(eu1, eaveH, e0), P(eu1, eaveH + SLAB, e0), P(e0, eaveH + SLAB, e0), FASCIA, Out(0, -1));
        Quad(st, P(e0, eaveH, ev1), P(eu1, eaveH, ev1), P(eu1, eaveH + SLAB, ev1), P(e0, eaveH + SLAB, ev1), FASCIA, Out(0, 1));
        Quad(st, P(e0, eaveH, e0), P(e0, eaveH, ev1), P(e0, eaveH + SLAB, ev1), P(e0, eaveH + SLAB, e0), FASCIA, Out(-1, 0));
        Quad(st, P(eu1, eaveH, e0), P(eu1, eaveH, ev1), P(eu1, eaveH + SLAB, ev1), P(eu1, eaveH + SLAB, e0), FASCIA, Out(1, 0));
    }

    /// <summary>A flat roof: a tarred deck at the walls' top, a parapet round the footprint's edge with a coping on it.</summary>
    static void Flat(SurfaceTool st, Grid g, HashSet<int> tiles, List<Rect> rects, float top)
    {
        foreach (var r in rects) WorldMesh.Box(st, new(r.x, top + 0.01f, r.y), new(r.x + r.w, top + SLAB, r.y + r.h), TAR);
        const float outT = 0.1f, inT = 0.12f, capT = 0.04f;
        // The footprint's outer sides, joined into straight runs: (line, side, along) -> one strip per run.
        var sides = new SortedSet<(int dir, int line, int along)>();
        foreach (int i in tiles)
        {
            int x = i % g.width, y = i / g.width;
            for (int d = 0; d < 4; d++)
            {
                var (dx, dy) = DIR4[d];
                int nx = x + dx, ny = y + dy;
                if (Grids.inBounds(g, nx, ny) && tiles.Contains(ny * g.width + nx)) continue;
                sides.Add(dy != 0 ? (d, y, x) : (d, x, y));
            }
        }
        var list = new List<(int dir, int line, int along)>(sides);
        for (int k = 0; k < list.Count;)
        {
            var (d, line, from) = list[k];
            int to = from;
            while (k + 1 < list.Count && list[k + 1].dir == d && list[k + 1].line == line && list[k + 1].along == to + 1) { k++; to++; }
            k++;
            var (dx, dy) = DIR4[d];
            // The strip along this run, from outT outside the edge line to inT in (a little long, to close the corners).
            Vector3 lo, hi;
            if (dy != 0)
            {
                float z = dy < 0 ? line : line + 1;
                float z0 = dy < 0 ? z - outT : z - inT, z1 = dy < 0 ? z + inT : z + outT;
                lo = new(from - outT, 0, z0); hi = new(to + 1 + outT, 0, z1);
            }
            else
            {
                float xx = dx < 0 ? line : line + 1;
                float x0 = dx < 0 ? xx - outT : xx - inT, x1 = dx < 0 ? xx + inT : xx + outT;
                lo = new(x0, 0, from - outT); hi = new(x1, 0, to + 1 + outT);
            }
            WorldMesh.Box(st, new(lo.X, top, lo.Z), new(hi.X, top + PARAPET, hi.Z), WorldMesh.WALL);
            WorldMesh.Box(st, new(lo.X - capT, top + PARAPET, lo.Z - capT), new(hi.X + capT, top + PARAPET + CAP, hi.Z + capT), COPING);
        }
    }

    /// <summary>A roof you'd put on yourself: boards laid flat over the room, a hand past the walls.</summary>
    static void FlatBuilt(SimState state, SurfaceTool st, List<int> tiles)
    {
        var g = state.grid;
        // Over the highest floor in the room (on a slope the walls step up with it).
        float floor = float.MinValue;
        foreach (int i in tiles) floor = Math.Max(floor, Terrain.Floor(i % g.width, i / g.width));
        float top = WorldMesh.WALL_H + floor;
        const float lip = 0.15f;
        // Over the walls too, where they're whole tiles (barricades, the planner's walls, old maps).
        var under = new HashSet<int>(tiles);
        foreach (int i in tiles)
        {
            int x = i % g.width, y = i / g.width;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Grids.inBounds(g, x + dx, y + dy) && g.tiles[(y + dy) * g.width + x + dx] is "wall" or "door" or "barricade")
                        under.Add((y + dy) * g.width + x + dx);
        }
        foreach (var r in Rectangles(g, under))
            WorldMesh.Box(st, new(r.x - lip, top + 0.01f, r.y - lip), new(r.x + r.w + lip, top + 0.1f, r.y + r.h + lip), BUILT);
    }

    // --- primitives (front faces wind clockwise seen from the front, as WorldMesh's) ---

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color colour, Vector3 hint)
    {
        var n = (b - a).Cross(c - a).Normalized();
        if (n.Dot(hint) > 0) (b, c) = (c, b);
        else n = -n;
        st.SetColor(colour);
        st.SetNormal(n);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3 hint)
    {
        Tri(st, a, b, c, colour, hint);
        Tri(st, a, c, d, colour, hint);
    }
}
