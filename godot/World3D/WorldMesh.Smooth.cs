// WorldMesh's smooth edges (2026-11-12; the class's parts: WorldMesh.cs). The sim changes water to land, and grass to
// forest floor, bare earth or field, tile by tile; drawn that way each edge is a staircase. Instead each tile corner
// counts the sixteen tiles about it, and the edge runs where the count says "half": a line through each tile it
// crosses, straight along a straight edge and slanting or curving where the tiles step. A tile it crosses is drawn in
// two parts (Cut); one wholly on the far side of it is drawn as that side, whatever the sim's tile says. Drawn only:
// wading and swimming go by the tiles, which the line is never more than about half a tile from.
//   Banks: the water's edge (a corner's share of water tiles, the bank at a half).
//   Edges: open ground of two kinds (a corner's count of each, the edge where they are equal). Only grass, forest
//          floor, bare earth and fields: paving, floors and roads have edges that are meant to be square.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public static partial class WorldMesh
{
    /// <summary>A tile's four corners in order round it: north-west, north-east, south-east, south-west.</summary>
    static (int x, int y)[] Corners(int x, int y) => new[] { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) };

    /// <summary>The part of a tile on one side of a line through it: `f` is a value at each corner (Corners' order), the
    /// line is where it is 0 (found along each side by straight interpolation), and `positive` picks the side where it
    /// is 0 or more. The polygon's points at `height` over the tile, each with the ground's normal there.</summary>
    static List<(Vector3 p, Vector3 normal)> Cut((int x, int y)[] at, float[] f, bool positive, Func<int, int, float> height)
    {
        var poly = new List<(Vector3 p, Vector3 normal)>();
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            bool ia = f[i] >= 0, ja = f[j] >= 0;
            var (ax, ay) = at[i];
            var (bx, by) = at[j];
            if (ia == positive) poly.Add((new Vector3(ax, height(ax, ay), ay), Terrain.Normal(ax, ay)));
            if (ia != ja)
            {
                float t = f[i] / (f[i] - f[j]);
                float h = height(ax, ay) + (height(bx, by) - height(ax, ay)) * t;
                poly.Add((new Vector3(ax + (bx - ax) * t, h, ay + (by - ay) * t), (Terrain.Normal(ax, ay) + Terrain.Normal(bx, by)).Normalized()));
            }
        }
        return poly;
    }

    /// <summary>A convex polygon as a fan of triangles in one look, each wound clockwise from above (as the ground's);
    /// with `flat`, every normal straight up (the water's surface), else each point's own.</summary>
    static void Fan(MeshBuf st, List<(Vector3 p, Vector3 normal)> poly, Look look, bool flat = false)
    {
        st.SetColor(look.colour);
        st.SetUV(look.what);
        for (int k = 1; k < poly.Count - 1; k++)
        {
            var (a, b, c) = (poly[0], poly[k], poly[k + 1]);
            if ((b.p - a.p).Cross(c.p - a.p).Y > 0) (b, c) = (c, b);
            foreach (var v in new[] { a, b, c }) { st.SetNormal(flat ? Vector3.Up : v.normal); st.AddVertex(v.p); }
        }
    }

    // --- open ground of two kinds ---

    static readonly string[] SOFT = { "grass", "forest", "dirt", "field" };

    static int Soft(string ground) => Array.IndexOf(SOFT, ground);

    /// <summary>Tile (x, y)'s kind of open ground (its index in SOFT), or -1: out of the grid, not floor, or another kind.</summary>
    static int SoftAt(Grid g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.width || y >= g.height) return -1;
        int i = y * g.width + x, k = Grids.groundAt(g, i);
        if (Grids.tileAt(g, i) != "floor" || k < 0 || k >= Grids.GROUNDS.Length) return -1;
        return Soft(Grids.GROUNDS[k]);
    }

    const int VERGE_REACH = 6, SHORE_REACH = 4;

    /// <summary>The open ground nearest tile (x, y), looking out the four ways up to `reach` tiles (grass if none): what
    /// a ribboned road's tile (VERGE_REACH) or a water tile on the land side of the bank (SHORE_REACH) is drawn as.</summary>
    static string Nearest(Grid g, int x, int y, int reach)
    {
        for (int d = 1; d <= reach; d++)
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int k = SoftAt(g, x + dx * d, y + dy * d);
                if (k >= 0) return SOFT[k];
            }
        return "grass";
    }

    /// <summary>A tile of open ground where two kinds meet: draws it (in two parts along the edge, or whole as the
    /// kind round it) and returns true; false where it is its own kind all round, or three kinds meet.</summary>
    static bool Edges(MeshBuf st, Grid g, int x, int y, string ground)
    {
        var at = Corners(x, y);
        var counts = new int[4, 4];
        int a = -1, b = -1;
        for (int c = 0; c < 4; c++)
        {
            for (int ty = at[c].y - 2; ty <= at[c].y + 1; ty++)
                for (int tx = at[c].x - 2; tx <= at[c].x + 1; tx++)
                {
                    int k = SoftAt(g, tx, ty);
                    if (k >= 0) counts[c, k]++;
                }
            int best = 0;
            for (int k = 1; k < 4; k++) if (counts[c, k] > counts[c, best]) best = k;
            if (a < 0 || best == a) a = best;
            else if (b < 0 || best == b) b = best;
            else return false; // three kinds meet here: as the tiles are
        }
        if (b < 0)
        {
            if (a == Soft(ground)) return false;
            GroundQuad(st, x, y, GroundLook(SOFT[a], x, y)); // an odd tile out in the other kind's ground: drawn as that
            return true;
        }
        // Which side of the edge each corner is on: more of a than of b round it, or the other way.
        var f = new float[4];
        for (int c = 0; c < 4; c++) f[c] = counts[c, a] - counts[c, b] + (a < b ? 0.01f : -0.01f);
        foreach (bool first in new[] { true, false })
        {
            var poly = Cut(at, f, first, Terrain.Corner);
            if (poly.Count >= 3) Fan(st, poly, GroundLook(SOFT[first ? a : b], x, y));
        }
        return true;
    }

    // --- the water's edge ---

    const int BANK_AS_IS = 0, BANK_DRAWN = 1, BANK_WATER = 2, BANK_LAND = 3;

    static bool Wet(Grid g, int x, int y)
    {
        x = Math.Clamp(x, 0, g.width - 1);
        y = Math.Clamp(y, 0, g.height - 1);
        int i = y * g.width + x;
        if (Grids.tileAt(g, i) == "water") return true;
        return Grids.groundAt(g, i) == BRIDGE_GROUND; // the water runs on under a bridge
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

    /// <summary>A tile at the water's edge: draws it in two parts along the bank and says so (BANK_DRAWN), or says
    /// which side of the bank the whole tile is on, or that no water is near (BANK_AS_IS). `ground`: the land's kind.</summary>
    static int Banks(MeshBuf st, MeshBuf ws, Grid g, int x, int y, string tile, string ground)
    {
        var at = Corners(x, y);
        // Each corner's water less a half: the bank is where that is 0, the water where it is 0 or more.
        var f = new float[4];
        int inside = 0;
        bool none = true;
        for (int i = 0; i < 4; i++)
        {
            float wet = WaterAt(g, at[i].x, at[i].y);
            f[i] = wet - 0.5f;
            if (wet >= 0.5f) inside++;
            if (wet > 0) none = false;
        }
        if (none) return BANK_AS_IS;
        if (inside == 4) return BANK_WATER;
        if (inside == 0) return tile == "water" ? BANK_LAND : BANK_AS_IS;
        var land = GroundLook(tile == "water" ? Nearest(g, x, y, SHORE_REACH) : ground, x, y);
        // The land, then the river bed with the water's surface over it, on its own heights.
        foreach (bool water in new[] { false, true })
        {
            var bed = Cut(at, f, water, Cor);
            if (bed.Count < 3) continue;
            Fan(st, bed, water ? BedLook(x, y) : land);
            if (water) Fan(ws, Cut(at, f, true, (cx, cy) => Terrain.Water(cx, cy)), SheetLook(x, y), flat: true);
        }
        return BANK_DRAWN;
    }
}
