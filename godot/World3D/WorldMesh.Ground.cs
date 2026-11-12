// WorldMesh's ground (the class's parts: WorldMesh.cs): a tile's quad on the sim's shared corner heights, normals
// from the neighbouring corners (so no seams between tiles or chunks); a street's asphalt sunk a kerb under them with
// the kerb's faces; a two-lane road's lines and a country road's diagonal corners; the water's bed and see-through
// surface, and the water running on under a bridge. Which of these a tile gets is TileGround's choice; the smooth
// banks and edges between kinds are WorldMesh.Smooth.cs's. Drawn only: the sim's tiles are untouched.

using System;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static partial class WorldMesh
{
    static readonly Color WATER = Ctx.Css(COLOURS.tiles["water"]);
    /// <summary>How much of the bed the water hides, looking straight down into it (WorldLook.Water).</summary>
    const float WATER_ALPHA = 0.58f;
    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road");
    static readonly int BRIDGE_GROUND = Array.IndexOf(Grids.GROUNDS, "bridge");
    static readonly Look LANE = new(new Color(0.62f, 0.6f, 0.5f), Surfaces.None);

    /// <summary>Country roads are drawn as ribbons (RoadRibbons.cs sets this each frame: a world with a plan).</summary>
    public static bool Ribboned;

    /// <summary>A tile's ground, by what it is and what is round it; returns whether it put water in.</summary>
    static bool TileGround(MeshBuf gs, MeshBuf ws, Grid g, int x, int y, string tile)
    {
        string ground = GroundAt(g, x, y);
        // A country road's tiles are drawn as the verge beside them while the road itself is a ribbon along
        // its planned line (RoadRibbons.cs); a street between kerbs keeps its tiles and its lines.
        bool ribbon = Ribboned && tile == "floor" && ground == "road" && !Terrain.Sunk(x, y);
        // The water's edge is drawn smooth (Banks): a tile the bank runs through is part land, part water; one wholly
        // on the far side of it is drawn as that side, whatever the sim's tile says.
        long q0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int bank = Terrain.Flat || ground == "bridge" || tile is not ("floor" or "water") ? BANK_AS_IS : Banks(gs, ws, g, x, y, tile, ribbon ? Nearest(g, x, y, VERGE_REACH) : ground);
        long q1 = System.Diagnostics.Stopwatch.GetTimestamp();
        PartTicks[4] += q1 - q0;
        if (bank == BANK_DRAWN) return true;
        if (bank == BANK_WATER) { Ground(gs, ws, x, y, "water", ground); return true; }
        if (bank == BANK_LAND && tile == "water") { Ground(gs, ws, x, y, "floor", Nearest(g, x, y, SHORE_REACH)); return false; }
        // Where kinds of open ground meet, smooth too (Edges).
        bool edged = bank == BANK_AS_IS && !Terrain.Flat && tile == "floor" && Soft(ground) >= 0 && Edges(gs, g, x, y, ground);
        long q2 = System.Diagnostics.Stopwatch.GetTimestamp();
        PartTicks[5] += q2 - q1;
        if (edged) return false;
        if (Ribboned && tile == "floor" && ground == "bridge") { UnderBridge(gs, ws, x, y); return true; }
        if (ribbon) { Ground(gs, ws, x, y, tile, Nearest(g, x, y, VERGE_REACH)); return false; }
        Ground(gs, ws, x, y, tile, ground, Ribboned ? -1 : RoadCorner(g, x, y, tile, ground));
        LaneMark(gs, g, x, y, tile, ground);
        // (A bridge's floor tile drew its water above; a tile that is not floor drew none, whatever its ground.)
        return tile == "water";
    }

    /// <summary>The kind of ground under tile (x, y) (grass where the sim's number is not a kind).</summary>
    static string GroundAt(Grid g, int x, int y)
    {
        int k = Grids.groundAt(g, y * g.width + x);
        return k >= 0 && k < Grids.GROUNDS.Length ? Grids.GROUNDS[k] : "grass";
    }

    // --- each tile's speckle: the one place a tile's lightness is worked out ---

    /// <summary>A tile's own small variation, 0 to 1, so the ground reads as ground and not as a sheet. The far view
    /// (FarField.cs) and the map pictures (Labs/MapShot.cs) take the same, so a tile is the same wherever it is drawn.</summary>
    internal static float Speckle(int x, int y) => Math.Abs(((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f);

    // Less from tile to tile than the water's and the far view's (0.06 to 0.095), about the same middle: the detail
    // says "ground" now, and squares a metre across under it said "tiles".
    static Color Lit(Color c, int x, int y) => c.Lightened(0.07f + Speckle(x, y) * 0.015f);

    /// <summary>Tile (x, y)'s look as ground of that kind: its colour (the far view has the same) and its detail.</summary>
    static Look GroundLook(string kind, int x, int y) => new(Lit(WorldLook.GroundColour(kind), x, y), WorldLook.Ground(kind));

    /// <summary>Tile (x, y)'s river bed.</summary>
    static Look BedLook(int x, int y) => new(Lit(WorldLook.RiverBed.colour, x, y), WorldLook.RiverBed.what);

    /// <summary>Tile (x, y)'s water surface: see-through, and no detail (flat colour until it gets a surface of its own).</summary>
    static Look SheetLook(int x, int y) => new(WATER.Lightened(0.06f + Speckle(x, y) * 0.035f) with { A = WATER_ALPHA }, Surfaces.None);

    // --- the tile's ground ---

    static void Ground(MeshBuf st, MeshBuf ws, int x, int y, string tile, string ground, int roadCorner = -1)
    {
        bool water = tile == "water";
        var w = SheetLook(x, y);
        if (water && Terrain.Flat)
        {
            float h = -0.25f;
            Quad(st, new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), w, Vector3.Up);
            return;
        }
        var own = water ? BedLook(x, y) : GroundLook(ground, x, y);
        if (roadCorner >= 0) GroundHalves(st, x, y, own, GroundLook("road", x, y), roadCorner);
        else GroundQuad(st, x, y, own);
        // The surface, on its own heights (it stands where the water level is, over the bed).
        if (water) WaterQuad(ws, x, y, w);
    }

    /// <summary>A tile's water surface, at the water's level on its corners.</summary>
    static void WaterQuad(MeshBuf ws, int x, int y, Look look)
    {
        float W(int cx, int cy) => Terrain.Water(cx, cy);
        Quad(ws, new(x, W(x, y), y), new(x + 1, W(x + 1, y), y), new(x + 1, W(x + 1, y + 1), y + 1), new(x, W(x, y + 1), y + 1), look, Vector3.Up);
    }

    /// <summary>A tile of ground on its four corner heights, each corner with its own (shared) normal.</summary>
    static void GroundQuad(MeshBuf st, int x, int y, Look look)
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
    static void GroundHalves(MeshBuf st, int x, int y, Look own, Look cut, int corner)
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
    static void Kerbs(MeshBuf st, int x, int y)
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

    // --- roads ---

    /// <summary>Whether tile (x, y) is in the grid and open road; `sunk`: a street's sunk asphalt counts too.</summary>
    static bool IsRoad(Grid g, int x, int y, bool sunk) => x >= 0 && y >= 0 && x < g.width && y < g.height
        && Grids.groundAt(g, y * g.width + x) == ROAD && Grids.tileAt(g, y * g.width + x) == "floor" && (sunk || !Terrain.Sunk(x, y));

    /// <summary>A country road on the slant is a staircase of tiles: a tile of open ground in the inside corner of a
    /// step (road on two sides that meet, none on the other two) is drawn half road, cut along its diagonal, so the
    /// road's edge runs across the step instead of round it. Which corner the road fills (0 north-east, 1 south-east,
    /// 2 south-west, 3 north-west), or -1. Drawn only: the tile is still what the sim says it is.</summary>
    static int RoadCorner(Grid g, int x, int y, string tile, string ground)
    {
        if (tile != "floor" || Soft(ground) < 0) return -1;
        bool n = IsRoad(g, x, y - 1, false), e = IsRoad(g, x + 1, y, false), s = IsRoad(g, x, y + 1, false), w = IsRoad(g, x - 1, y, false);
        if (n && e && !s && !w) return 0;
        if (e && s && !w && !n) return 1;
        if (s && w && !n && !e) return 2;
        if (w && n && !e && !s) return 3;
        return -1;
    }

    /// <summary>The broken line down the middle of a two-lane road (2026-11-12; drawn only, the sim has no lanes):
    /// a road tile on the line between the two lanes carries a dash along that side, two metres in every four,
    /// just over the asphalt. A road is read off the tiles: an even number across (four to eight) and a straight
    /// run of at least five tiles each way along it, so junctions, yards and one-tile lanes have none.</summary>
    static void LaneMark(MeshBuf st, Grid g, int x, int y, string tile, string ground)
    {
        if (tile != "floor" || ground != "road") return;
        int Run(int dx, int dy) { int n = 0; while (n < 12 && IsRoad(g, x + dx * (n + 1), y + dy * (n + 1), true)) n++; return n; }
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

    // --- bridges, while roads are ribbons ---

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

    /// <summary>A bridge's tile while roads are ribbons: the water running on under it (river bed and surface), the
    /// deck being the ribbon's (RoadRibbons.Deck), so a bridge on the slant is not a staircase of planks.</summary>
    static void UnderBridge(MeshBuf st, MeshBuf ws, int x, int y)
    {
        float W(int cx, int cy) => Terrain.Water(cx, cy);
        const float BED = 0.6f;
        Quad(st, new(x, W(x, y) - BED, y), new(x + 1, W(x + 1, y) - BED, y), new(x + 1, W(x + 1, y + 1) - BED, y + 1), new(x, W(x, y + 1) - BED, y + 1), BedLook(x, y), Vector3.Up);
        WaterQuad(ws, x, y, SheetLook(x, y));
    }
}
