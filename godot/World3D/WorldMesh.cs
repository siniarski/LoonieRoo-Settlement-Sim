// The world in 3D for first person: one mesh per CHUNK x CHUNK tiles, built from the grid (ground,
// tile walls, edge walls, doors, windows, barricades, ceilings over indoor tiles) and furniture.
// What's indoors, and the roofs over it, is Roofs.cs's: a ceiling goes over each of its Indoor tiles.
// Reads state only (rule 5). 1 tile = 1 m; grid x is +X and grid y is +Z; the ground's height is the
// sim's (Terrain.cs): the ground is a quad per tile on the shared corner heights, normals from the
// neighbouring corners (so no seams between tiles or chunks); everything stands on its tile's floor
// (its highest corner) and walls go down to its lowest (a footing where the ground falls away).
// Plain boxes in vertex colours, each face with a detail layer in its UV (WorldLook.cs says which: the ground by its
// kind, a wall's outside and inside by its building; docs/textures.md): one surface and one material a chunk.
// Furniture with a model is Furniture.cs's; only a piece without one is a box here.

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

    /// <summary>The walls' grey: the far view's blocks, and a wall that fills its tile (hand-made maps).</summary>
    internal static readonly Color WALL = new(0.62f, 0.6f, 0.56f);
    static Look TileWall => new(WALL, Surfaces.What("concrete"));
    static readonly Color WATER = Ctx.Css(COLOURS.tiles["water"]);
    /// <summary>How much of the bed the water hides, looking straight down into it (WorldLook.Water).</summary>
    const float WATER_ALPHA = 0.58f;
    /// <summary>An edge wall stops this short of its corner's far side, so its end is inside the wall it meets, not level with that wall's face.</summary>
    const float TUCK = 0.002f;
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
        var g = state.grid;
        deckGrid = g;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var gs = new SurfaceTool();
        gs.Begin(Mesh.PrimitiveType.Triangles);
        var gl = new SurfaceTool();
        gl.Begin(Mesh.PrimitiveType.Triangles);
        var ws = new SurfaceTool();  // the water's surface: see-through (WorldLook.Water), over the bed
        ws.Begin(Mesh.PrimitiveType.Triangles);
        bool anyWater = false;
        bool anyGlass = false;
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                string tile = Grids.tileAt(g, i);
                string ground = Grids.groundAt(g, i) >= 0 && Grids.groundAt(g, i) < Grids.GROUNDS.Length ? Grids.GROUNDS[Grids.groundAt(g, i)] : "grass";
                // A country road's tiles are drawn as the verge beside them while the road itself is a ribbon along
                // its planned line (RoadRibbons.cs); a street between kerbs keeps its tiles and its lines.
                bool ribbon = Ribboned && tile == "floor" && ground == "road" && !Terrain.Sunk(x, y);
                // The water's edge is drawn smooth (Banks, below): a tile the bank runs through is part land, part
                // water; one wholly on the far side of it is drawn as that side, whatever the sim's tile says.
                int bank = Terrain.Flat || ground == "bridge" || tile is not ("floor" or "water") ? BANK_AS_IS : Banks(gs, ws, g, x, y, tile, ribbon ? Verge(g, x, y) : ground);
                if (bank == BANK_DRAWN) { }
                else if (bank == BANK_WATER) Ground(gs, ws, x, y, "water", ground);
                else if (bank == BANK_LAND && tile == "water") Ground(gs, ws, x, y, "floor", Shore(g, x, y));
                else if (bank == BANK_AS_IS && !Terrain.Flat && tile == "floor" && Soft(ground) >= 0 && Edges(gs, g, x, y, ground)) { }
                else if (Ribboned && tile == "floor" && ground == "bridge") UnderBridge(gs, ws, x, y);
                else if (ribbon) Ground(gs, ws, x, y, tile, Verge(g, x, y));
                else
                {
                    Ground(gs, ws, x, y, tile, ground, Ribboned ? -1 : RoadCorner(g, x, y, tile, ground));
                    LaneMark(gs, g, x, y, tile, ground);
                }
                anyWater |= bank is BANK_DRAWN or BANK_WATER || (bank == BANK_AS_IS && (tile == "water" || (Ribboned && ground == "bridge")));
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
                    // A door with a door beside it in the same wall is part of one wide door (a garage's): which ends of it are open.
                    int wide = 0;
                    if (kind == Grids.EDGE_DOOR)
                    {
                        int ax = side == 0 ? 1 : 0, ay = side == 0 ? 0 : 1; // along the wall
                        if (Grids.inBounds(g, x - ax, y - ay) && Doors.sameDoor(g, x - ax, y - ay, x, y, side)) wide |= 1;
                        if (Grids.inBounds(g, x + ax, y + ay) && Doors.sameDoor(g, x, y, x + ax, y + ay, side)) wide |= 2;
                    }
                    anyGlass |= Edge(st, gl, x, y, side == 0, kind, look.edgeOpen.Contains(e), look.edgeLocked.Contains(e), look.edgeBoarded.Contains(e), there, here, wide);
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
        if (anyWater)
        {
            ws.Commit(groundMesh);
            groundMesh.SurfaceSetMaterial(groundMesh.GetSurfaceCount() - 1, WorldLook.Water);
        }
        return (mesh, groundMesh);
    }

    /// <summary>A ceiling over an indoor tile at the walls' top, seen from below.</summary>
    static void Ceiling(SurfaceTool st, int x, int y, Look look)
    {
        float h = Terrain.Floor(x, y) + WALL_H;
        Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), look, Vector3.Down);
    }

    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road");

    /// <summary>A country road on the slant is a staircase of tiles: a tile of open ground in the inside corner of a
    /// step (road on two sides that meet, none on the other two) is drawn half road, cut along its diagonal, so the
    /// road's edge runs across the step instead of round it. Which corner the road fills (0 north-east, 1 south-east,
    /// 2 south-west, 3 north-west), or -1. Drawn only: the tile is still what the sim says it is.</summary>
    static int RoadCorner(Grid g, int x, int y, string tile, string ground)
    {
        if (tile != "floor" || ground is not ("grass" or "dirt" or "forest" or "field")) return -1;
        bool Road(int ax, int ay) => ax >= 0 && ay >= 0 && ax < g.width && ay < g.height && Grids.groundAt(g, ay * g.width + ax) == ROAD
            && Grids.tileAt(g, ay * g.width + ax) == "floor" && !Terrain.Sunk(ax, ay);
        bool n = Road(x, y - 1), e = Road(x + 1, y), s = Road(x, y + 1), w = Road(x - 1, y);
        if (n && e && !s && !w) return 0;
        if (e && s && !w && !n) return 1;
        if (s && w && !n && !e) return 2;
        if (w && n && !e && !s) return 3;
        return -1;
    }

    static Grid? deckGrid;

    /// <summary>A ground corner's height, but for one out in the water by a bridge while roads are ribbons: the
    /// bridge's corners stand at its deck (Heights.DECK over the surface), and with the deck drawn as the ribbon's
    /// the river bed round it would rise out of the water to them. Such a corner (a bridge's tile on one side, no
    /// land on any) is put under the surface; one at the bank keeps its height.</summary>
    static float Cor(int cx, int cy)
    {
        float h = Terrain.Corner(cx, cy);
        var g = deckGrid;
        if (!Ribboned || g == null) return h;
        bool bridge = false;
        for (int dy = -1; dy <= 0; dy++)
            for (int dx = -1; dx <= 0; dx++)
            {
                int x = Math.Clamp(cx + dx, 0, g.width - 1), y = Math.Clamp(cy + dy, 0, g.height - 1), i = y * g.width + x;
                if (Grids.groundAt(g, i) == BRIDGE_GROUND) bridge = true;
                else if (Grids.tileAt(g, i) != "water") return h;
            }
        return bridge ? Math.Min(h, Terrain.Water(cx, cy) - 0.3f) : h;
    }

    static readonly int BRIDGE_GROUND = Array.IndexOf(Grids.GROUNDS, "bridge");

    /// <summary>A bridge's tile while roads are ribbons: the water running on under it (river bed and surface), the
    /// deck being the ribbon's (RoadRibbons.Deck), so a bridge on the slant is not a staircase of planks.</summary>
    static void UnderBridge(SurfaceTool st, SurfaceTool ws, int x, int y)
    {
        float W(int cx, int cy) => Terrain.Water(cx, cy);
        const float BED = 0.6f;
        float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
        var bed = new Look(WorldLook.RiverBed.colour.Lightened(0.07f + Math.Abs(n) * 0.015f), WorldLook.RiverBed.what);
        Quad(st, new(x, W(x, y) - BED, y), new(x + 1, W(x + 1, y) - BED, y), new(x + 1, W(x + 1, y + 1) - BED, y + 1), new(x, W(x, y + 1) - BED, y + 1), bed, Vector3.Up);
        var w = new Look(WATER.Lightened(0.06f + Math.Abs(n) * 0.035f) with { A = WATER_ALPHA }, Surfaces.None);
        Quad(ws, new(x, W(x, y), y), new(x + 1, W(x + 1, y), y), new(x + 1, W(x + 1, y + 1), y + 1), new(x, W(x, y + 1), y + 1), w, Vector3.Up);
    }

    /// <summary>Country roads are drawn as ribbons (RoadRibbons.cs sets this each frame: a world with a plan).</summary>
    public static bool Ribboned;

    /// <summary>The ground beside a road tile: the nearest kind that is not road, looking out the four ways (grass if none near).</summary>
    static string Verge(Grid g, int x, int y)
    {
        for (int d = 1; d <= 6; d++)
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int ax = x + dx * d, ay = y + dy * d;
                if (ax < 0 || ay < 0 || ax >= g.width || ay >= g.height) continue;
                int i = ay * g.width + ax, k = Grids.groundAt(g, i);
                if (k < 0 || k >= Grids.GROUNDS.Length || k == ROAD || Grids.tileAt(g, i) != "floor") continue;
                string kind = Grids.GROUNDS[k];
                if (kind is "grass" or "dirt" or "forest" or "field") return kind;
            }
        return "grass";
    }

    // --- where one kind of open ground meets another, smooth (2026-11-12) ---
    // Grass, forest floor, bare earth and fields change kind tile by tile in the sim; drawn that way their edges are
    // staircases. As for the water's edge (below): each tile corner counts the kinds of the sixteen tiles about it,
    // and the edge between two kinds runs where their counts are equal. A tile it crosses is drawn in two parts; one
    // wholly on the other side of it is drawn as that side. Only these four kinds: paving, floors and roads have
    // edges that are meant to be square. Drawn only.
    static readonly string[] SOFT = { "grass", "forest", "dirt", "field" };

    static int Soft(string ground) => Array.IndexOf(SOFT, ground);

    static int SoftAt(Grid g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.width || y >= g.height) return -1;
        int i = y * g.width + x, k = Grids.groundAt(g, i);
        if (Grids.tileAt(g, i) != "floor" || k < 0 || k >= Grids.GROUNDS.Length) return -1;
        return Soft(Grids.GROUNDS[k]);
    }

    /// <summary>A tile of open ground where two kinds meet: draws it (in two parts along the edge, or whole as the
    /// kind round it) and returns true; false where it is its own kind all round, or three kinds meet.</summary>
    static bool Edges(SurfaceTool st, Grid g, int x, int y, string ground)
    {
        var at = new[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) };
        var counts = new int[4, 4];
        var top = new int[4];
        int a = -1, b = -1;
        for (int c = 0; c < 4; c++)
        {
            for (int ty = at[c].Item2 - 2; ty <= at[c].Item2 + 1; ty++)
                for (int tx = at[c].Item1 - 2; tx <= at[c].Item1 + 1; tx++)
                {
                    int k = SoftAt(g, tx, ty);
                    if (k >= 0) counts[c, k]++;
                }
            int best = 0;
            for (int k = 1; k < 4; k++) if (counts[c, k] > counts[c, best]) best = k;
            top[c] = best;
            if (a < 0 || best == a) a = best;
            else if (b < 0 || best == b) b = best;
            else return false; // three kinds meet here: as the tiles are
        }
        int own = Soft(ground);
        float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
        Look Of(int kind) => new(WorldLook.GroundColour(SOFT[kind]).Lightened(0.07f + Math.Abs(n) * 0.015f), WorldLook.Ground(SOFT[kind]));
        if (b < 0)
        {
            if (a == own) return false;
            GroundQuad(st, x, y, Of(a)); // an odd tile out in the other kind's ground: drawn as that
            return true;
        }
        // Which side of the edge each corner is on: more of a than of b round it, or the other way.
        var f = new float[4];
        for (int c = 0; c < 4; c++) f[c] = counts[c, a] - counts[c, b] + (a < b ? 0.01f : -0.01f);
        void Side(bool first)
        {
            var poly = new List<(Vector3 p, Vector3 normal)>();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                bool ia = f[i] >= 0, ja = f[j] >= 0;
                var (ax, ay) = at[i];
                var (bx, by) = at[j];
                if (ia == first) poly.Add((new Vector3(ax, Terrain.Corner(ax, ay), ay), Terrain.Normal(ax, ay)));
                if (ia != ja)
                {
                    float t = f[i] / (f[i] - f[j]);
                    float h = Terrain.Corner(ax, ay) + (Terrain.Corner(bx, by) - Terrain.Corner(ax, ay)) * t;
                    poly.Add((new Vector3(ax + (bx - ax) * t, h, ay + (by - ay) * t), (Terrain.Normal(ax, ay) + Terrain.Normal(bx, by)).Normalized()));
                }
            }
            if (poly.Count < 3) return;
            var look = Of(first ? a : b);
            st.SetColor(look.colour);
            st.SetUV(look.what);
            for (int k = 1; k < poly.Count - 1; k++)
            {
                var (p0, p1, p2) = (poly[0], poly[k], poly[k + 1]);
                if ((p1.p - p0.p).Cross(p2.p - p0.p).Y > 0) (p1, p2) = (p2, p1); // clockwise from above, as the ground's
                foreach (var v in new[] { p0, p1, p2 }) { st.SetNormal(v.normal); st.AddVertex(v.p); }
            }
        }
        Side(true);
        Side(false);
        return true;
    }

    // --- the water's edge, smooth (2026-11-12) ---
    // The sim's water is tiles, and a bank drawn tile by tile is a staircase. Each tile corner is given how much of
    // the ground round it is water (the sixteen tiles about it: 0 to 1), and the bank is where that is a half: a
    // line through each tile it crosses, straight along a straight bank and slanting or curving where the tiles
    // step. Drawn only: wading and swimming go by the tiles, which the line is never more than about half a tile from.
    const int BANK_AS_IS = 0, BANK_DRAWN = 1, BANK_WATER = 2, BANK_LAND = 3;

    static bool Wet(Grid g, int x, int y)
    {
        x = Math.Clamp(x, 0, g.width - 1);
        y = Math.Clamp(y, 0, g.height - 1);
        int i = y * g.width + x;
        if (Grids.tileAt(g, i) == "water") return true;
        int k = Grids.groundAt(g, i);
        return k >= 0 && k < Grids.GROUNDS.Length && Grids.GROUNDS[k] == "bridge"; // the water runs on under a bridge
    }

    /// <summary>How much of the ground round tile corner (cx, cy) is water: the sixteen tiles about it.</summary>
    static float WaterAt(Grid g, int cx, int cy)
    {
        int n = 0;
        for (int y = cy - 2; y <= cy + 1; y++)
            for (int x = cx - 2; x <= cx + 1; x++)
                if (Wet(g, x, y)) n++;
        return n / 16f;
    }

    /// <summary>The land beside a water tile: the nearest ground that is not water, looking out the four ways.</summary>
    static string Shore(Grid g, int x, int y)
    {
        for (int d = 1; d <= 4; d++)
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int ax = x + dx * d, ay = y + dy * d;
                if (ax < 0 || ay < 0 || ax >= g.width || ay >= g.height) continue;
                int i = ay * g.width + ax, k = Grids.groundAt(g, i);
                if (Grids.tileAt(g, i) != "floor" || k < 0 || k >= Grids.GROUNDS.Length) continue;
                string kind = Grids.GROUNDS[k];
                if (kind is "grass" or "dirt" or "forest" or "field") return kind;
            }
        return "grass";
    }

    /// <summary>A tile at the water's edge: draws it in two parts along the bank and says so (BANK_DRAWN), or says
    /// which side of the bank the whole tile is on, or that no water is near (BANK_AS_IS).</summary>
    static int Banks(SurfaceTool st, SurfaceTool ws, Grid g, int x, int y, string tile, string ground)
    {
        var at = new[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) };
        var wet = new float[4];
        int inside = 0;
        bool none = true;
        for (int i = 0; i < 4; i++)
        {
            wet[i] = WaterAt(g, at[i].Item1, at[i].Item2);
            if (wet[i] >= 0.5f) inside++;
            if (wet[i] > 0) none = false;
        }
        if (none) return BANK_AS_IS;
        if (inside == 4) return BANK_WATER;
        if (inside == 0) return tile == "water" ? BANK_LAND : BANK_AS_IS;
        float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
        string dry = tile == "water" ? Shore(g, x, y) : ground;
        var land = new Look(WorldLook.GroundColour(dry).Lightened(0.07f + Math.Abs(n) * 0.015f), WorldLook.Ground(dry));
        var bed = new Look(WorldLook.RiverBed.colour.Lightened(0.07f + Math.Abs(n) * 0.015f), WorldLook.RiverBed.what);
        var sheet = new Look(WATER.Lightened(0.06f + Math.Abs(n) * 0.035f) with { A = WATER_ALPHA }, Surfaces.None);
        // Each side of the bank as a polygon: the corners on that side, and the points where the bank crosses the tile's edges.
        void Side(bool water)
        {
            var ground3 = new List<(Vector3 p, Vector3 normal)>();
            var top = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                bool a = wet[i] >= 0.5f, b = wet[j] >= 0.5f;
                var (ax, ay) = at[i];
                var (bx, by) = at[j];
                if (a == water)
                {
                    ground3.Add((new Vector3(ax, Cor(ax, ay), ay), Terrain.Normal(ax, ay)));
                    top.Add(new Vector3(ax, Terrain.Water(ax, ay), ay));
                }
                if (a != b)
                {
                    float t = (0.5f - wet[i]) / (wet[j] - wet[i]);
                    float px = ax + (bx - ax) * t, py = ay + (by - ay) * t;
                    float h = Cor(ax, ay) + (Cor(bx, by) - Cor(ax, ay)) * t;
                    ground3.Add((new Vector3(px, h, py), (Terrain.Normal(ax, ay) + Terrain.Normal(bx, by)).Normalized()));
                    top.Add(new Vector3(px, Terrain.Water(ax, ay) + (Terrain.Water(bx, by) - Terrain.Water(ax, ay)) * t, py));
                }
            }
            if (ground3.Count < 3) return;
            var look = water ? bed : land;
            st.SetColor(look.colour);
            st.SetUV(look.what);
            for (int k = 1; k < ground3.Count - 1; k++)
            {
                var (a, b, c) = (ground3[0], ground3[k], ground3[k + 1]);
                if ((b.p - a.p).Cross(c.p - a.p).Y > 0) (b, c) = (c, b); // clockwise from above, as the ground's
                foreach (var v in new[] { a, b, c }) { st.SetNormal(v.normal); st.AddVertex(v.p); }
            }
            if (!water) return;
            ws.SetColor(sheet.colour);
            ws.SetUV(sheet.what);
            ws.SetNormal(Vector3.Up);
            for (int k = 1; k < top.Count - 1; k++)
            {
                var (a, b, c) = (top[0], top[k], top[k + 1]);
                if ((b - a).Cross(c - a).Y > 0) (b, c) = (c, b);
                ws.AddVertex(a); ws.AddVertex(b); ws.AddVertex(c);
            }
        }
        Side(false);
        Side(true);
        return BANK_DRAWN;
    }

    static readonly Look LANE = new(new Color(0.62f, 0.6f, 0.5f), Surfaces.None);

    /// <summary>The broken line down the middle of a two-lane road (2026-11-12; drawn only, the sim has no lanes):
    /// a road tile on the line between the two lanes carries a dash along that side, two metres in every four,
    /// just over the asphalt. A road is read off the tiles: an even number across (four to eight) and a straight
    /// run of at least five tiles each way along it, so junctions, yards and one-tile lanes have none.</summary>
    static void LaneMark(SurfaceTool st, Grid g, int x, int y, string tile, string ground)
    {
        if (tile != "floor" || ground != "road") return;
        bool Road(int ax, int ay) => ax >= 0 && ay >= 0 && ax < g.width && ay < g.height && Grids.groundAt(g, ay * g.width + ax) == ROAD && Grids.tileAt(g, ay * g.width + ax) == "floor";
        int Run(int dx, int dy) { int n = 0; while (n < 12 && Road(x + dx * (n + 1), y + dy * (n + 1))) n++; return n; }
        int north = Run(0, -1), south = Run(0, 1), west = Run(-1, 0), east = Run(1, 0);
        float H(int cx, int cy) => Terrain.Corner(cx, cy) - (Terrain.Sunk(x, y) ? Terrain.Kerb(cx, cy) : 0f) + 0.012f;
        const float HALF = 0.06f;
        // Running east-west: the line is this tile's north side when as many tiles lie north of that side as south of it.
        int across = north + south + 1;
        if (across is >= 4 and <= 8 && across % 2 == 0 && north == across / 2 && west >= 5 && east >= 5 && ((x % 4 + 4) % 4) < 2)
            Quad(st, new(x, H(x, y), y - HALF), new(x + 1, H(x + 1, y), y - HALF), new(x + 1, H(x + 1, y), y + HALF), new(x, H(x, y), y + HALF), LANE, Vector3.Up);
        // And a solid line a third of a metre in from each kerb, along the outer rows.
        const float IN = 0.3f, THIN = 0.04f;
        if (across is >= 4 and <= 8 && west >= 5 && east >= 5 && (north == 0 || south == 0))
        {
            float z = north == 0 ? y + IN : y + 1 - IN;
            float h0 = H(x, north == 0 ? y : y + 1), h1 = H(x + 1, north == 0 ? y : y + 1);
            Quad(st, new(x, h0, z - THIN), new(x + 1, h1, z - THIN), new(x + 1, h1, z + THIN), new(x, h0, z + THIN), LANE, Vector3.Up);
        }
        // Running north-south: its west side.
        across = west + east + 1;
        if (across is >= 4 and <= 8 && across % 2 == 0 && west == across / 2 && north >= 5 && south >= 5 && ((y % 4 + 4) % 4) < 2)
            Quad(st, new(x - HALF, H(x, y), y), new(x + HALF, H(x, y), y), new(x + HALF, H(x, y + 1), y + 1), new(x - HALF, H(x, y + 1), y + 1), LANE, Vector3.Up);
        if (across is >= 4 and <= 8 && north >= 5 && south >= 5 && (west == 0 || east == 0))
        {
            float xx = west == 0 ? x + IN : x + 1 - IN;
            float h0 = H(west == 0 ? x : x + 1, y), h1 = H(west == 0 ? x : x + 1, y + 1);
            Quad(st, new(xx - THIN, h0, y), new(xx + THIN, h0, y), new(xx + THIN, h1, y + 1), new(xx - THIN, h1, y + 1), LANE, Vector3.Up);
        }
    }

    static void Ground(SurfaceTool st, SurfaceTool ws, int x, int y, string tile, string ground, int roadCorner = -1)
    {
        bool water = tile == "water";
        // A little variation per tile, so the ground reads as ground and not as a sheet.
        float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
        // Water has no detail: it is flat colour until it gets a surface of its own.
        var w = new Look(WATER.Lightened(0.06f + Math.Abs(n) * 0.035f) with { A = WATER_ALPHA }, Surfaces.None);
        if (water && Terrain.Flat)
        {
            float h = -0.25f;
            Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), w, Vector3.Up);
            return;
        }
        // The ground's colour (the far view has the same) and its detail, by its kind.
        var c = water ? WorldLook.RiverBed.colour : WorldLook.GroundColour(ground);
        // Less from tile to tile than the water's and the far view's (0.06 to 0.095), about the same middle: the
        // detail says "ground" now, and squares a metre across under it said "tiles".
        c = c.Lightened(0.07f + Math.Abs(n) * 0.015f);
        var own = new Look(c, water ? WorldLook.RiverBed.what : WorldLook.Ground(ground));
        if (roadCorner >= 0) GroundHalves(st, x, y, own, new Look(WorldLook.GroundColour("road").Lightened(0.07f + Math.Abs(n) * 0.015f), WorldLook.Ground("road")), roadCorner);
        else GroundQuad(st, x, y, own);
        // The surface, on its own heights (it stands where the water level is, over the bed).
        if (water) Quad(ws, new(x, Terrain.Water(x, y), y), new(x + 1, Terrain.Water(x + 1, y), y), new(x + 1, Terrain.Water(x + 1, y + 1), y + 1), new(x, Terrain.Water(x, y + 1), y + 1), w, Vector3.Up);
    }

    /// <summary>A tile of ground on its four corner heights, each corner with its own (shared) normal.</summary>
    static void GroundQuad(SurfaceTool st, int x, int y, Look look)
    {
        // A street's asphalt lies a kerb's height under its corners (Terrain.Sunk), the kerb's face along each side
        // that isn't asphalt too.
        bool sunk = Terrain.Sunk(x, y);
        Vector3 P((int x, int y) k) => new(k.x, Cor(k.x, k.y) - (sunk ? Terrain.Kerb(k.x, k.y) : 0f), k.y);
        if (sunk) Kerbs(st, x, y);
        // Split along the diagonal whose ends are closer in height, so a ridge or a crease runs the way the ground does.
        bool alt = Math.Abs(Cor(x, y) - Cor(x + 1, y + 1)) > Math.Abs(Cor(x + 1, y) - Cor(x, y + 1));
        var (a, b, cc, d) = alt ? ((x + 1, y), (x + 1, y + 1), (x, y + 1), (x, y)) : ((x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1));
        void V((int x, int y) k) { st.SetNormal(Terrain.Normal(k.x, k.y)); st.AddVertex(P(k)); }
        st.SetColor(look.colour);
        st.SetUV(look.what);
        // Front faces wind clockwise seen from above (as Quad turns them).
        if ((P(b) - P(a)).Cross(P(cc) - P(a)).Y > 0) (b, d) = (d, b);
        V(a); V(b); V(cc);
        V(a); V(cc); V(d);
    }

    /// <summary>A tile of ground as two triangles of different looks, split on the diagonal that cuts off `corner`
    /// (0 north-east, 1 south-east, 2 south-west, 3 north-west): that half is `cut`, the rest `own`.</summary>
    static void GroundHalves(SurfaceTool st, int x, int y, Look own, Look cut, int corner)
    {
        // The tile's corners in order round it: north-west, north-east, south-east, south-west.
        var k = new (int x, int y)[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) };
        Vector3 P((int x, int y) q) => new(q.x, Terrain.Corner(q.x, q.y), q.y);
        void Tri(Look look, int a, int b, int c)
        {
            var (pa, pb, pc) = (k[a % 4], k[b % 4], k[c % 4]);
            // Front faces wind clockwise seen from above (as GroundQuad turns them).
            if ((P(pb) - P(pa)).Cross(P(pc) - P(pa)).Y > 0) (pb, pc) = (pc, pb);
            st.SetColor(look.colour);
            st.SetUV(look.what);
            foreach (var q in new[] { pa, pb, pc }) { st.SetNormal(Terrain.Normal(q.x, q.y)); st.AddVertex(P(q)); }
        }
        // The cut corner is k[corner + 1]: the triangle of it and its two neighbours; the other half is the rest.
        int at = corner + 1;
        Tri(cut, at, at + 1, at + 3);
        Tri(own, at + 1, at + 2, at + 3);
    }

    /// <summary>The kerb's faces round a sunk tile: from the asphalt up to the ground beside it, on each side whose
    /// neighbour isn't sunk.</summary>
    static void Kerbs(SurfaceTool st, int x, int y)
    {
        foreach (var (dx, dy, ax, ay, bx, by, n) in new[] {
            (0, -1, x, y, x + 1, y, Vector3.Back), (0, 1, x, y + 1, x + 1, y + 1, Vector3.Forward),
            (-1, 0, x, y, x, y + 1, Vector3.Right), (1, 0, x + 1, y, x + 1, y + 1, Vector3.Left) })
        {
            if (Terrain.Sunk(x + dx, y + dy)) continue;
            float ha = Terrain.Corner(ax, ay), hb = Terrain.Corner(bx, by), ka = Terrain.Kerb(ax, ay), kb = Terrain.Kerb(bx, by);
            if (ka < 0.005f && kb < 0.005f) continue;
            Quad(st, new(ax, ha - ka, ay), new(bx, hb - kb, by), new(bx, hb, by), new(ax, ha, ay), WorldLook.Kerb, n);
        }
    }

    /// <summary>An edge: a wall, door, barricade or window on the north or west side of tile (x, y). `before` is the
    /// building of the tile north or west of it and `after` the tile's own (null: none): a face looks like the inside of
    /// the building it faces into, or the outside of the one behind it. Returns whether it put glass in.</summary>
    static bool Edge(SurfaceTool st, SurfaceTool gl, int x, int y, bool north, int kind, bool open, bool locked, bool boarded, WorldLook.Style? before, WorldLook.Style? after, int wide = 0)
    {
        // Along the edge from a to a + 1, thickness across it. Heights are over the edge's floor (its higher
        // end); what starts at the ground goes down to a footing under its lower end.
        float c0 = Terrain.Corner(x, y), c1 = north ? Terrain.Corner(x + 1, y) : Terrain.Corner(x, y + 1);
        float floor = Math.Max(c0, c1), foot = Math.Min(c0, c1) - FOOTING;
        var own = after ?? before ?? WorldLook.Built;
        Look faceBefore = before != null ? before.inside : own.outside, faceAfter = after != null ? after.inside : own.outside;
        Vector3 Lo(float along, float yy, float across) => north ? new(x + along, yy, y + across) : new(x + across, yy, y + along);
        // A slab of the edge: its two faces, its top and its ends (a wall's ends are its outside; an opening's are trim).
        void Slab(float a0, float a1, float h0, float h1, float t, Look sideBefore, Look sideAfter, Look top, Look ends)
        {
            var p = Lo(a0, h0, -t);
            var q = Lo(a1, h1, t);
            Vector3 lo = new(Math.Min(p.X, q.X), h0 == 0 ? foot : floor + h0, Math.Min(p.Z, q.Z)), hi = new(Math.Max(p.X, q.X), floor + h1, Math.Max(p.Z, q.Z));
            // On a slope the wall stands on a footing: concrete from the lower ground up to the floor.
            if (h0 == 0 && floor - foot > FOOTING + 0.03f)
            {
                Box(st, lo, hi with { Y = floor }, WorldLook.Footing);
                lo.Y = floor;
            }
            if (north) Box(st, lo, hi, top, sideBefore, sideAfter, ends, ends);
            else Box(st, lo, hi, top, ends, ends, sideBefore, sideAfter);
        }
        // A wall between two rooms ends as it faces; an outer wall ends as its outside.
        var ends = before != null && after != null ? faceAfter : own.outside;
        void Wall(float a0, float a1, float h0, float h1, bool opening = false) => Slab(a0, a1, h0, h1, WALL_T / 2, faceBefore, faceAfter, ends, opening ? own.trim : ends);
        void Piece(float a0, float a1, float h0, float h1, float t, Look look) => Slab(a0, a1, h0, h1, t, look, look, look, look);
        float t = WALL_T / 2, end = t - TUCK;
        switch (kind)
        {
            case Grids.EDGE_WALL:
                Wall(-end, 1 + end, 0, WALL_H);
                return false;
            case Grids.EDGE_DOOR when wide != 0:
            {
                // Part of a wide door (a garage's): a jamb only at the run's ends, a lintel across, and a sheet-metal
                // leaf from side to side; open, it is rolled up under the lintel.
                float a0 = (wide & 1) != 0 ? 0 : 0.05f, a1 = (wide & 2) != 0 ? 1 : 0.95f;
                if ((wide & 1) == 0) Wall(-end, 0.05f, 0, WALL_H, true);
                if ((wide & 2) == 0) Wall(0.95f, 1 + end, 0, WALL_H, true);
                Wall(a0, a1, DOOR_H + 0.15f, WALL_H);
                var sheet = new Look(new Color(0.55f, 0.56f, 0.55f), Surfaces.What("sheetmetal"));
                if (!open) Piece(a0, a1, 0.01f, DOOR_H + 0.15f, 0.03f, sheet);
                else Piece(a0, a1, DOOR_H - 0.1f, DOOR_H + 0.15f, 0.09f, sheet);
                return false;
            }
            case Grids.EDGE_DOOR:
                Wall(-end, 0.05f, 0, WALL_H, true);
                Wall(0.95f, 1 + end, 0, WALL_H, true);
                Wall(0.05f, 0.95f, DOOR_H, WALL_H);
                // The leaf hangs from the lintel: its foot clears the floor, not the ground below it.
                if (!open) Piece(0.06f, 0.94f, 0.01f, DOOR_H, 0.03f, locked ? WorldLook.DoorLocked : WorldLook.Door);
                else
                {
                    // Swung back against the wall, hinged at the edge's start.
                    var p = Lo(0.06f, 0, 0);
                    var q = Lo(0.12f, DOOR_H, 0.88f);
                    Box(st, new(Math.Min(p.X, q.X), floor + 0.01f, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + DOOR_H, Math.Max(p.Z, q.Z)), WorldLook.Door);
                }
                return false;
            case Grids.EDGE_BARRICADE:
                foreach (var h in new[] { 0.25f, 0.65f, 1.05f }) Piece(-0.05f, 1.05f, h, h + 0.22f, 0.05f, WorldLook.Plank);
                return false;
            case Grids.EDGE_FENCE:
                // A tall fence: two posts and rails, boards close side by side to above head height.
                Piece(-0.05f, 0.05f, 0, 2.05f, 0.07f, WorldLook.Plank);
                Piece(0.95f, 1.05f, 0, 2.05f, 0.07f, WorldLook.Plank);
                foreach (var h in new[] { 0.3f, 1.7f }) Piece(-0.05f, 1.05f, h, h + 0.1f, 0.06f, WorldLook.Plank);
                for (int k = 0; k < 6; k++) Piece(0.06f + k * 0.148f, 0.06f + k * 0.148f + 0.13f, 0.05f, 1.95f, 0.035f, WorldLook.Plank);
                return false;
            case Grids.EDGE_WINDOW:
            case Grids.EDGE_WINDOW_BROKEN:
                Wall(-end, 1 + end, 0, SILL_H);
                Wall(-end, 1 + end, LINTEL_H, WALL_H);
                Wall(-end, 0.06f, SILL_H, LINTEL_H, true);
                Wall(0.94f, 1 + end, SILL_H, LINTEL_H, true);
                // The frame, a little proud of the wall: a sill, a head and two stiles.
                float ft = t + 0.012f;
                Piece(0.04f, 0.96f, SILL_H - 0.03f, SILL_H + 0.03f, ft + 0.02f, own.trim);
                Piece(0.06f, 0.94f, LINTEL_H - 0.04f, LINTEL_H, ft, own.trim);
                Piece(0.06f, 0.1f, SILL_H + 0.03f, LINTEL_H - 0.04f, ft, own.trim);
                Piece(0.9f, 0.94f, SILL_H + 0.03f, LINTEL_H - 0.04f, ft, own.trim);
                if (boarded) foreach (var h in new[] { 1.0f, 1.4f, 1.8f }) Piece(0.0f, 1.0f, h, h + 0.18f, t + 0.04f, WorldLook.Plank);
                if (kind == Grids.EDGE_WINDOW)
                {
                    var p = Lo(0.06f, SILL_H, -0.01f);
                    var q = Lo(0.94f, LINTEL_H, 0.01f);
                    Box(gl, new(Math.Min(p.X, q.X), floor + SILL_H, Math.Min(p.Z, q.Z)), new(Math.Max(p.X, q.X), floor + LINTEL_H, Math.Max(p.Z, q.Z)), WorldLook.Pane);
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
            Box(st, new(x, floor + DOOR_H, y + c - 0.06f), new(x + 1, floor + WALL_H, y + c + 0.06f), TileWall);
            if (!open) Box(st, new(x + 0.04f, low, y + c - 0.03f), new(x + 0.96f, floor + DOOR_H, y + c + 0.03f), locked ? WorldLook.DoorLocked : WorldLook.Door);
        }
        else
        {
            Box(st, new(x + c - 0.06f, floor + DOOR_H, y), new(x + c + 0.06f, floor + WALL_H, y + 1), TileWall);
            if (!open) Box(st, new(x + c - 0.03f, low, y + 0.04f), new(x + c + 0.03f, floor + DOOR_H, y + 0.96f), locked ? WorldLook.DoorLocked : WorldLook.Door);
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
        Box(st, new(x - r, foot - 0.02f, z - r), new(x + r, at + h, z + r), new Look(Ctx.Css(colour), Surfaces.None));
    }

    // --- primitives ---

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Look look, Vector3 normal)
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
    public static void Box(SurfaceTool st, Vector3 lo, Vector3 hi, Look look) => Box(st, lo, hi, look, look, look, look, look);

    /// <summary>A box with a look per face: its top, its north (-z) and south faces, its west (-x) and east ones.</summary>
    public static void Box(SurfaceTool st, Vector3 lo, Vector3 hi, Look top, Look north, Look south, Look west, Look east)
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
