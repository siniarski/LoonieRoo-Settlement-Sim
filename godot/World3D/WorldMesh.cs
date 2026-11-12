// The world in 3D for first person: one mesh per CHUNK x CHUNK tiles, built from the grid (ground,
// tile walls, edge walls, doors, windows, barricades, ceilings over indoor tiles) and furniture.
// What's indoors, and the roofs over it, is Roofs.cs's: a ceiling goes over each of its Indoor tiles.
// Reads state only (rule 5). 1 tile = 1 m; grid x is +X and grid y is +Z; the ground's height is the
// sim's (Terrain.cs). Plain boxes and quads in vertex colours, each face with a detail layer in its UV (WorldLook.cs
// says which: the ground by its kind, a wall's outside and inside by its building; docs/textures.md): one surface and
// one material a chunk, and the water's see-through sheet and the windows' glass a surface each.
// One class in parts, a concern to a file:
//   WorldMesh.cs         this: what a chunk is built from (Lookups, Signature), the build's walk over its tiles,
//                        ceilings, furniture without a model (Furniture.cs draws the rest), and the box and quad.
//   WorldMesh.Ground.cs  a tile's ground: its quad on the corner heights, kerbs, a road's lane lines and its
//                        diagonal corners, the water's bed and surface, the water under a bridge; each tile's speckle.
//   WorldMesh.Smooth.cs  where the water meets land, and one kind of open ground another: drawn along a smooth
//                        line through the tiles instead of round them (one cut serves both).
//   WorldMesh.Walls.cs   what stands on a tile's edge (walls, doors, windows, fences, barricades) and the old
//                        tile-filling door.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static partial class WorldMesh
{
    public const int CHUNK = 16;
    public const float WALL_H = 2.5f;
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
                if (look.styles.TryGetValue(i, out var s)) h = h * 31 + s.id;
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
        /// <summary>Each generated building's surfaces, by the tiles of its footprint (Roofs.Styles).</summary>
        public Dictionary<int, WorldLook.Style> styles = new();
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
        long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var g = state.grid;
        deckGrid = g;
        var st = new MeshBuf();  // what stands: walls, doors, ceilings, furniture boxes
        st.Begin(Mesh.PrimitiveType.Triangles);
        var gs = new MeshBuf();  // the ground, the river bed and the lane lines
        gs.Begin(Mesh.PrimitiveType.Triangles);
        var gl = new MeshBuf();  // the windows' glass
        gl.Begin(Mesh.PrimitiveType.Triangles);
        var ws = new MeshBuf();  // the water's surface: see-through (WorldLook.Water), over the bed
        ws.Begin(Mesh.PrimitiveType.Triangles);
        bool anyWater = false;
        bool anyGlass = false;
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                string tile = Grids.tileAt(g, i);
                long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
                anyWater |= TileGround(gs, ws, g, x, y, tile);
                long p1 = System.Diagnostics.Stopwatch.GetTimestamp();
                PartTicks[0] += p1 - p0;
                float floor = Terrain.Floor(x, y), low = Terrain.Low(x, y) - FOOTING;
                if (tile == "wall") Box(st, new(x, low, y), new(x + 1, floor + WALL_H, y + 1), TileWall);
                else if (tile == "barricade") Box(st, new(x + 0.05f, low, y + 0.05f), new(x + 0.95f, floor + 1.0f, y + 0.95f), WorldLook.Plank);
                else if (tile == "door") TileDoor(st, g, x, y, look.open.Contains(i), look.locked.Contains(i), floor, low);
                if (look.indoor.Contains(i) && tile != "wall") Ceiling(st, x, y, (look.styles.GetValueOrDefault(i) ?? WorldLook.Built).ceiling);
                // This tile's two edges: north (along x at z = y) and west (along z at x = x).
                for (int side = 0; side < 2; side++)
                {
                    int e = i * 2 + side;
                    int kind = Grids.edgeKind(g, e);
                    if (kind == Grids.EDGE_NONE) continue;
                    // The building on each side of it: the tile's own, and the one north or west of it.
                    var here = look.styles.GetValueOrDefault(i);
                    var there = (side == 0 ? y > 0 : x > 0) ? look.styles.GetValueOrDefault(side == 0 ? i - g.width : i - 1) : null;
                    anyGlass |= Edge(st, gl, x, y, side == 0, kind, look.edgeOpen.Contains(e), look.edgeLocked.Contains(e), look.edgeBoarded.Contains(e), there, here, WideDoor(g, x, y, side, kind));
                }
            }
        long p2 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (look.furniture.TryGetValue((cx, cy), out var list))
            foreach (var f in list) Thing(st, f);
        long p3 = System.Diagnostics.Stopwatch.GetTimestamp();
        PartTicks[2] += p3 - p2;
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, solid);
        if (anyGlass)
        {
            gl.Commit(mesh);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, glass);
        }
        var groundMesh = gs.Commit();
        if (groundMesh.GetSurfaceCount() > 0) groundMesh.SurfaceSetMaterial(0, solid);
        if (anyWater)
        {
            ws.Commit(groundMesh);
            groundMesh.SurfaceSetMaterial(groundMesh.GetSurfaceCount() - 1, WorldLook.Water);
        }
        PartTicks[3] += System.Diagnostics.Stopwatch.GetTimestamp() - p3;
        PartTicks[1] += p2 - buildStart;
        return (mesh, groundMesh);
    }

    /// <summary>Where a chunk's build goes, in Stopwatch ticks (the chunk bench): the ground, the whole tile loop (the ground's share in it), furniture, the commits.</summary>
    public static readonly long[] PartTicks = new long[6];

    /// <summary>A ceiling over an indoor tile at the walls' top, seen from below.</summary>
    static void Ceiling(MeshBuf st, int x, int y, Look look)
    {
        float h = Terrain.Floor(x, y) + WALL_H;
        Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), look, Vector3.Down);
    }

    /// <summary>A piece of furniture with no model, as a box on the ground.</summary>
    static void Thing(MeshBuf st, Furniture f)
    {
        float x = f.x + 0.5f, z = f.y + 0.5f;
        if (f.kind == "tree" || FurnitureModels.Has(f)) return; // Trees.cs and Furniture.cs draw them
        var (w, h, colour) = FURNITURE.GetValueOrDefault(f.kind, (0.45f, 0.45f, "#5a5a58"));
        float r = w / 2;
        // On the ground at its centre, its foot down to the lowest ground under it.
        float at = Terrain.Height(x, z);
        float foot = Math.Min(Math.Min(Terrain.Height(x - r, z - r), Terrain.Height(x + r, z - r)), Math.Min(Terrain.Height(x - r, z + r), Terrain.Height(x + r, z + r)));
        Box(st, new(x - r, foot - 0.02f, z - r), new(x + r, at + h, z + r), new Look(Ctx.Css(colour), Surfaces.None));
    }

    // --- primitives ---

    static void Quad(MeshBuf st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Look look, Vector3 normal)
    {
        // Godot's front faces wind clockwise seen from the front: turn the quad round if it doesn't.
        if ((b - a).Cross(c - a).Dot(normal) > 0) (b, d) = (d, b);
        st.SetColor(look.colour);
        st.SetUV(look.what);
        st.SetNormal(normal);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    /// <summary>An axis-aligned box from lo to hi (no bottom face).</summary>
    public static void Box(MeshBuf st, Vector3 lo, Vector3 hi, Look look) => Box(st, lo, hi, look, look, look, look, look);

    /// <summary>A box with a look per face: its top, its north (-z) and south faces, its west (-x) and east ones.</summary>
    public static void Box(MeshBuf st, Vector3 lo, Vector3 hi, Look top, Look north, Look south, Look west, Look east)
    {
        Vector3 v(float x, float y, float z) => new(x, y, z);
        float x0 = lo.X, y0 = lo.Y, z0 = lo.Z, x1 = hi.X, y1 = hi.Y, z1 = hi.Z;
        Quad(st, v(x0, y1, z0), v(x1, y1, z0), v(x1, y1, z1), v(x0, y1, z1), top, Vector3.Up);
        Quad(st, v(x0, y0, z0), v(x1, y0, z0), v(x1, y1, z0), v(x0, y1, z0), north, Vector3.Forward);
        Quad(st, v(x1, y0, z1), v(x0, y0, z1), v(x0, y1, z1), v(x1, y1, z1), south, Vector3.Back);
        Quad(st, v(x0, y0, z1), v(x0, y0, z0), v(x0, y1, z0), v(x0, y1, z1), west, Vector3.Left);
        Quad(st, v(x1, y0, z0), v(x1, y0, z1), v(x1, y1, z1), v(x1, y1, z0), east, Vector3.Right);
    }
}
