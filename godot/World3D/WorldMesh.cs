// The world in 3D for first person: one mesh per CHUNK x CHUNK tiles, built from the grid (ground,
// tile walls, edge walls, doors, windows, barricades, ceilings over indoor tiles) and furniture.
// What's indoors, and the roofs over it, is Roofs.cs's: a ceiling goes over each of its Indoor tiles.
// Reads state only (rule 5). 1 tile = 1 m; grid x is +X and grid y is +Z; the ground's height is the
// sim's (Terrain.cs): the ground is a quad per tile on the shared corner heights, normals from the
// neighbouring corners (so no seams between tiles or chunks); everything stands on its tile's floor
// (its highest corner) and walls go down to its lowest (a footing where the ground falls away).
// Plain boxes in vertex colours for now: the look of the world is its own job. Furniture with a model is
// Furniture.cs's; only a piece without one is a box here.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static class WorldMesh
{
    public const int CHUNK = 16;
    public const float WALL_H = 2.5f;
    const float WALL_T = 0.12f;
    const float DOOR_H = 2.05f;
    const float SILL_H = 0.9f;
    const float LINTEL_H = 2.1f;

    internal static readonly Color WALL = new(0.62f, 0.6f, 0.56f);
    static readonly Color DOOR = new(0.42f, 0.28f, 0.16f);
    static readonly Color DOOR_LOCKED = new(0.3f, 0.2f, 0.12f);
    static readonly Color PLANK = Ctx.Css(COLOURS.plank);
    static readonly Color GLASS = new(0.25f, 0.32f, 0.36f, 0.35f);
    static readonly Color CEILING = new(0.5f, 0.49f, 0.46f);
    static readonly Color WATER = Ctx.Css(COLOURS.tiles["water"]);
    static readonly Color BED = new(0.2f, 0.18f, 0.14f);
    /// <summary>A wall's footing goes this far under the lowest ground along it.</summary>
    const float FOOTING = 0.1f;

    /// <summary>Furniture as a box: width, height, and colour (by kind; the rest a small grey box). Only for a
    /// piece with no model (Furniture.cs draws the rest).</summary>
    static readonly Dictionary<string, (float w, float h, string colour)> FURNITURE = new()
    {
        ["bed"] = (0.9f, 0.5f, "#6f6a80"), ["table"] = (0.8f, 0.75f, "#6a4a2c"), ["crate"] = (0.6f, 0.6f, "#7a6040"),
        ["workbench"] = (0.9f, 0.9f, "#6a5034"), ["stove"] = (0.7f, 0.9f, "#55585c"), ["fridge"] = (0.7f, 1.8f, "#c8ccd0"),
        ["generator"] = (0.8f, 0.8f, "#7a7040"), ["well"] = (0.9f, 0.8f, "#5a5a58"), ["collector"] = (0.8f, 1.0f, "#3e5a78"),
        ["heater"] = (0.6f, 0.8f, "#7a4a30"), ["radio"] = (0.5f, 0.6f, "#4a5a4a"), ["trap"] = (0.9f, 0.15f, "#8a8e92"),
        ["tower"] = (0.9f, 2.6f, "#6a5034"), ["turret"] = (0.6f, 1.2f, "#55606a"), ["sofa"] = (0.9f, 0.7f, "#5a4a44"),
        ["decor"] = (0.4f, 0.4f, "#5a5550"), ["lamp"] = (0.15f, 1.7f, "#a09a70"), ["coop"] = (0.9f, 1.0f, "#7a6a48"),
    };

    /// <summary>A cheap fingerprint of everything a chunk's mesh shows; a chunk is rebuilt when it changes.</summary>
    public static long Signature(SimState state, int cx, int cy, Lookups look)
    {
        var g = state.grid;
        long h = 17;
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                h = h * 31 + Grids.tileAt(g, i).GetHashCode();
                h = h * 31 + Grids.edgeKind(g, i * 2) * 7 + Grids.edgeKind(g, i * 2 + 1);
                if (look.open.Contains(i)) h = h * 31 + 1;
                if (look.edgeOpen.Contains(i * 2)) h = h * 31 + 2;
                if (look.edgeOpen.Contains(i * 2 + 1)) h = h * 31 + 3;
                if (look.edgeBoarded.Contains(i * 2)) h = h * 31 + 4;
                if (look.edgeBoarded.Contains(i * 2 + 1)) h = h * 31 + 5;
                if (look.indoor.Contains(i)) h = h * 31 + 6;
            }
        if (look.furniture.TryGetValue((cx, cy), out var list))
            foreach (var f in list) h = h * 31 + f.kind.GetHashCode() * 13 + f.x * 7 + f.y;
        return h;
    }

    /// <summary>Sets the frame's membership tests (the grid keeps these as lists).</summary>
    public sealed class Lookups
    {
        public readonly HashSet<int> open = new(), edgeOpen = new(), edgeBoarded = new(), locked = new(), edgeLocked = new();
        public readonly Dictionary<(int, int), List<Furniture>> furniture = new();
        /// <summary>Tiles with a ceiling over them (Roofs.Indoor).</summary>
        public HashSet<int> indoor = new();
        /// <summary>Bumped by each Refresh (FurnitureModels gathers again).</summary>
        public int Version { get; private set; }

        public void Refresh(SimState state)
        {
            Version++;
            var g = state.grid;
            Fill(open, g.open); Fill(edgeOpen, g.edgeOpen); Fill(edgeBoarded, g.edgeBoarded); Fill(locked, g.locked); Fill(edgeLocked, g.edgeLocked);
            furniture.Clear();
            foreach (var f in state.furniture)
            {
                var k = (f.x / CHUNK, f.y / CHUNK);
                if (!furniture.TryGetValue(k, out var l)) furniture[k] = l = new();
                l.Add(f);
            }
        }

        static void Fill(HashSet<int> set, List<int> from) { set.Clear(); foreach (var v in from) set.Add(v); }
    }

    /// <summary>A chunk's meshes: what stands on the ground (walls, doors, furniture: casts shadows), and the
    /// ground itself (no shadows: low sun on gentle facets only striped it).</summary>
    public static (ArrayMesh world, ArrayMesh ground) Build(SimState state, int cx, int cy, Lookups look, Material solid, Material glass)
    {
        var g = state.grid;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var gs = new SurfaceTool();
        gs.Begin(Mesh.PrimitiveType.Triangles);
        var gl = new SurfaceTool();
        gl.Begin(Mesh.PrimitiveType.Triangles);
        bool anyGlass = false;
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                string tile = Grids.tileAt(g, i);
                string ground = Grids.groundAt(g, i) >= 0 && Grids.groundAt(g, i) < Grids.GROUNDS.Length ? Grids.GROUNDS[Grids.groundAt(g, i)] : "grass";
                Ground(gs, x, y, tile, ground);
                float floor = Terrain.Floor(x, y), low = Terrain.Low(x, y) - FOOTING;
                if (tile == "wall") Box(st, new(x, low, y), new(x + 1, floor + WALL_H, y + 1), WALL);
                else if (tile == "barricade") Box(st, new(x + 0.05f, low, y + 0.05f), new(x + 0.95f, floor + 1.0f, y + 0.95f), PLANK);
                else if (tile == "door") TileDoor(st, g, x, y, look.open.Contains(i), look.locked.Contains(i), floor, low);
                if (look.indoor.Contains(i) && tile != "wall") Ceiling(st, state, x, y);
                // This tile's two edges: north (along x at z = y) and west (along z at x = x).
                for (int side = 0; side < 2; side++)
                {
                    int e = i * 2 + side;
                    int kind = Grids.edgeKind(g, e);
                    if (kind == Grids.EDGE_NONE) continue;
                    anyGlass |= Edge(st, gl, x, y, side == 0, kind, look.edgeOpen.Contains(e), look.edgeLocked.Contains(e), look.edgeBoarded.Contains(e));
                }
            }
        if (look.furniture.TryGetValue((cx, cy), out var list))
            foreach (var f in list) Thing(st, f);
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, solid);
        if (anyGlass)
        {
            gl.Commit(mesh);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, glass);
        }
        var groundMesh = gs.Commit();
        if (groundMesh.GetSurfaceCount() > 0) groundMesh.SurfaceSetMaterial(0, solid);
        return (mesh, groundMesh);
    }

    /// <summary>A ceiling over an indoor tile at the walls' top, seen from below.</summary>
    static void Ceiling(SurfaceTool st, SimState state, int x, int y)
    {
        float h = Terrain.Floor(x, y) + WALL_H;
        Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), CEILING, Vector3.Down);
    }

    static void Ground(SurfaceTool st, int x, int y, string tile, string ground)
    {
        bool water = tile == "water";
        // A little variation per tile, so the ground reads as ground and not as a sheet.
        float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
        if (water && Terrain.Flat)
        {
            float h = -0.25f;
            Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), WATER.Lightened(0.06f + Math.Abs(n) * 0.035f), Vector3.Up);
            return;
        }
        var c = water ? BED : Ctx.Css(COLOURS.ground.GetValueOrDefault(ground, COLOURS.ground["grass"]));
        c = c.Lightened(0.06f + Math.Abs(n) * 0.035f);
        GroundQuad(st, x, y, c);
        if (water)
        {
            // The surface, on its own heights (it stands where the water level is, over the bed).
            var w = WATER.Lightened(0.06f + Math.Abs(n) * 0.035f);
            Quad(st, new(x, Terrain.Water(x, y), y), new(x + 1, Terrain.Water(x + 1, y), y), new(x + 1, Terrain.Water(x + 1, y + 1), y + 1), new(x, Terrain.Water(x, y + 1), y + 1), w, Vector3.Up);
        }
    }

    /// <summary>A tile of ground on its four corner heights, each corner with its own (shared) normal.</summary>
    static void GroundQuad(SurfaceTool st, int x, int y, Color c)
    {
        Vector3 P((int x, int y) k) => new(k.x, Terrain.Corner(k.x, k.y), k.y);
        // Split along the diagonal whose ends are closer in height, so a ridge or a crease runs the way the ground does.
        bool alt = Math.Abs(Terrain.Corner(x, y) - Terrain.Corner(x + 1, y + 1)) > Math.Abs(Terrain.Corner(x + 1, y) - Terrain.Corner(x, y + 1));
        var (a, b, cc, d) = alt ? ((x + 1, y), (x + 1, y + 1), (x, y + 1), (x, y)) : ((x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1));
        void V((int x, int y) k) { st.SetNormal(Terrain.Normal(k.x, k.y)); st.AddVertex(P(k)); }
        st.SetColor(c);
        // Front faces wind clockwise seen from above (as Quad turns them).
        if ((P(b) - P(a)).Cross(P(cc) - P(a)).Y > 0) (b, d) = (d, b);
        V(a); V(b); V(cc);
        V(a); V(cc); V(d);
    }

    /// <summary>An edge: a wall, door, barricade or window on the north or west side of tile (x, y). Returns whether it put glass in.</summary>
    static bool Edge(SurfaceTool st, SurfaceTool gl, int x, int y, bool north, int kind, bool open, bool locked, bool boarded)
    {
        // Along the edge from a to a + 1, thickness across it. Heights are over the edge's floor (its higher
        // end); what starts at the ground goes down to a footing under its lower end.
        float c0 = Terrain.Corner(x, y), c1 = north ? Terrain.Corner(x + 1, y) : Terrain.Corner(x, y + 1);
        float floor = Math.Max(c0, c1), foot = Math.Min(c0, c1) - FOOTING;
        Vector3 Lo(float along, float yy, float across) => north ? new(x + along, yy, y + across) : new(x + across, yy, y + along);
        void Slab(float a0, float a1, float h0, float h1, float t, Color c)
        {
            var p = Lo(a0, h0, -t);
            var q = Lo(a1, h1, t);
            float y0 = h0 == 0 ? foot : floor + h0;
            Box(st, new(Math.Min(p.X, q.X), y0, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + h1, Math.Max(p.Z, q.Z)), c);
        }
        float t = WALL_T / 2;
        switch (kind)
        {
            case Grids.EDGE_WALL:
                Slab(-t, 1 + t, 0, WALL_H, t, WALL);
                return false;
            case Grids.EDGE_DOOR:
                Slab(-t, 0.05f, 0, WALL_H, t, WALL);
                Slab(0.95f, 1 + t, 0, WALL_H, t, WALL);
                Slab(0.05f, 0.95f, DOOR_H, WALL_H, t, WALL);
                // The leaf hangs from the lintel: its foot clears the floor, not the ground below it.
                if (!open) Slab(0.06f, 0.94f, 0.01f, DOOR_H, 0.03f, locked ? DOOR_LOCKED : DOOR);
                else
                {
                    // Swung back against the wall, hinged at the edge's start.
                    var p = Lo(0.06f, 0, 0);
                    var q = Lo(0.12f, DOOR_H, 0.88f);
                    Box(st, new(Math.Min(p.X, q.X), floor + 0.01f, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + DOOR_H, Math.Max(p.Z, q.Z)), DOOR);
                }
                return false;
            case Grids.EDGE_BARRICADE:
                foreach (var h in new[] { 0.25f, 0.65f, 1.05f }) Slab(-0.05f, 1.05f, h, h + 0.22f, 0.05f, PLANK);
                return false;
            case Grids.EDGE_WINDOW:
            case Grids.EDGE_WINDOW_BROKEN:
                Slab(-t, 1 + t, 0, SILL_H, t, WALL);
                Slab(-t, 1 + t, LINTEL_H, WALL_H, t, WALL);
                Slab(-t, 0.06f, SILL_H, LINTEL_H, t, WALL);
                Slab(0.94f, 1 + t, SILL_H, LINTEL_H, t, WALL);
                if (boarded) foreach (var h in new[] { 1.0f, 1.4f, 1.8f }) Slab(0.0f, 1.0f, h, h + 0.18f, t + 0.03f, PLANK);
                if (kind == Grids.EDGE_WINDOW)
                {
                    var p = Lo(0.06f, SILL_H, -0.01f);
                    var q = Lo(0.94f, LINTEL_H, 0.01f);
                    Box(gl, new(Math.Min(p.X, q.X), floor + SILL_H, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + LINTEL_H, Math.Max(p.Z, q.Z)), GLASS);
                    return true;
                }
                return false;
        }
        return false;
    }

    /// <summary>An old-style door that fills its tile: posts and a lintel, the leaf across the gap unless open.</summary>
    static void TileDoor(SurfaceTool st, Grid g, int x, int y, bool open, bool locked, float floor, float low)
    {
        // Across the gap the walls are on: walls west and east mean the door runs along x.
        bool alongX = Grids.getTile(g, x - 1, y) == "wall" || Grids.getTile(g, x + 1, y) == "wall";
        float c = 0.5f;
        if (alongX)
        {
            Box(st, new(x, floor + DOOR_H, y + c - 0.06f), new(x + 1, floor + WALL_H, y + c + 0.06f), WALL);
            if (!open) Box(st, new(x + 0.04f, low, y + c - 0.03f), new(x + 0.96f, floor + DOOR_H, y + c + 0.03f), locked ? DOOR_LOCKED : DOOR);
        }
        else
        {
            Box(st, new(x + c - 0.06f, floor + DOOR_H, y), new(x + c + 0.06f, floor + WALL_H, y + 1), WALL);
            if (!open) Box(st, new(x + c - 0.03f, low, y + 0.04f), new(x + c + 0.03f, floor + DOOR_H, y + 0.96f), locked ? DOOR_LOCKED : DOOR);
        }
    }

    static void Thing(SurfaceTool st, Furniture f)
    {
        float x = f.x + 0.5f, z = f.y + 0.5f;
        if (f.kind == "tree" || FurnitureModels.Has(f)) return; // Trees.cs and Furniture.cs draw them
        var (w, h, colour) = FURNITURE.GetValueOrDefault(f.kind, (0.45f, 0.45f, "#5a5a58"));
        float r = w / 2;
        // On the ground at its centre, its foot down to the lowest ground under it.
        float at = Terrain.Height(x, z);
        float foot = Math.Min(Math.Min(Terrain.Height(x - r, z - r), Terrain.Height(x + r, z - r)), Math.Min(Terrain.Height(x - r, z + r), Terrain.Height(x + r, z + r)));
        Box(st, new(x - r, foot - 0.02f, z - r), new(x + r, at + h, z + r), Ctx.Css(colour));
    }

    // --- primitives ---

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3 normal)
    {
        // Godot's front faces wind clockwise seen from the front: turn the quad round if it doesn't.
        if ((b - a).Cross(c - a).Dot(normal) > 0) (b, d) = (d, b);
        st.SetColor(colour);
        st.SetNormal(normal);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    /// <summary>An axis-aligned box from lo to hi (no bottom face).</summary>
    public static void Box(SurfaceTool st, Vector3 lo, Vector3 hi, Color c)
    {
        Vector3 v(float x, float y, float z) => new(x, y, z);
        float x0 = lo.X, y0 = lo.Y, z0 = lo.Z, x1 = hi.X, y1 = hi.Y, z1 = hi.Z;
        Quad(st, v(x0, y1, z0), v(x1, y1, z0), v(x1, y1, z1), v(x0, y1, z1), c, Vector3.Up);
        Quad(st, v(x0, y0, z0), v(x1, y0, z0), v(x1, y1, z0), v(x0, y1, z0), c, Vector3.Forward);
        Quad(st, v(x1, y0, z1), v(x0, y0, z1), v(x0, y1, z1), v(x1, y1, z1), c, Vector3.Back);
        Quad(st, v(x0, y0, z1), v(x0, y0, z0), v(x0, y1, z0), v(x0, y1, z1), c, Vector3.Left);
        Quad(st, v(x1, y0, z0), v(x1, y0, z1), v(x1, y1, z1), v(x1, y1, z0), c, Vector3.Right);
    }
}
