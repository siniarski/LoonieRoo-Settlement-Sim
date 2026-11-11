// Walls and doors muffling sound (2026-11-09). Reads the grid only: the straight line from where a
// sound is to where you are, tile by tile, counting what stands in the way, on tile edges and on
// tiles. A wall counts 1, a shut door 0.7, glass 0.45, planks 0.35; an open door, a smashed window
// or nothing, 0. A sound behind walls is quieter (MUFFLE_DB a wall, at most MAX_DB) and goes through
// one of two low-pass buses: "Muffled" (one wall's worth) or "Walled" (more), so only its low end
// comes through. Line, not paths: sound round a corner through an open door isn't worked out.

using System;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.Audio;

public static class Muffle
{
    public const string MUFFLED = "Muffled", WALLED = "Walled";
    /// <summary>dB off a sound per wall's worth in the way, and the most taken off.</summary>
    public const float MUFFLE_DB = 7, MAX_DB = 21;
    /// <summary>The buses' low-pass cutoffs (Hz).</summary>
    const float MUFFLED_HZ = 1000, WALLED_HZ = 420;
    /// <summary>Tiles walked at most along a line (sounds further off than this aren't muffled any more).</summary>
    const int MAX_STEPS = 160;

    public const float WALL = 1, DOOR = 0.7f, GLASS = 0.45f, PLANKS = 0.35f;

    /// <summary>Makes the two buses (once), each sending to Master with a low-pass on it.</summary>
    public static void Buses()
    {
        Bus(MUFFLED, MUFFLED_HZ);
        Bus(WALLED, WALLED_HZ);
    }

    static void Bus(string name, float hz)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int i = AudioServer.BusCount;
        AudioServer.AddBus(i);
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
        AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = hz, Resonance = 0.5f, Db = AudioEffectFilter.FilterDB.Filter12Db });
    }

    /// <summary>The bus for an amount in the way.</summary>
    public static string BusFor(float amount) => amount < 0.3f ? "Master" : amount < 1.3f ? MUFFLED : WALLED;

    /// <summary>The dB off for an amount in the way.</summary>
    public static float DbFor(float amount) => -Math.Min(MAX_DB, amount * MUFFLE_DB);

    /// <summary>How much stands between two points (tile units, x and y as on the grid): walls, doors, glass, planks.
    /// Walks the tiles the line passes through (Amanatides and Woo), checking each border crossed and each tile
    /// entered (not the two ends' own tiles).</summary>
    public static float Between(Grid grid, double ax, double ay, double bx, double by)
    {
        int x = (int)Math.Floor(ax), y = (int)Math.Floor(ay);
        int ex = (int)Math.Floor(bx), ey = (int)Math.Floor(by);
        double dx = bx - ax, dy = by - ay;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        double tdx = sx != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity, tdy = sy != 0 ? Math.Abs(1 / dy) : double.PositiveInfinity;
        double tx = sx > 0 ? (x + 1 - ax) * tdx : sx < 0 ? (ax - x) * tdx : double.PositiveInfinity;
        double ty = sy > 0 ? (y + 1 - ay) * tdy : sy < 0 ? (ay - y) * tdy : double.PositiveInfinity;
        float amount = 0;
        for (int step = 0; step < MAX_STEPS && (x != ex || y != ey); step++)
        {
            int nx = x, ny = y;
            if (tx < ty) { nx += sx; tx += tdx; } else { ny += sy; ty += tdy; }
            if (!Grids.inBounds(grid, nx, ny)) break;
            amount += Edge(grid, Grids.edgeBetween(grid, x, y, nx, ny));
            x = nx; y = ny;
            if (x != ex || y != ey) amount += Tile(grid, x, y);
            if (amount >= 3) break;
        }
        return amount;
    }

    static float Edge(Grid grid, int e)
    {
        switch (Grids.edgeKind(grid, e))
        {
            case Grids.EDGE_WALL: return WALL;
            case Grids.EDGE_DOOR: return Grids.edgeIsOpen(grid, e) ? 0 : DOOR;
            case Grids.EDGE_WINDOW: return grid.edgeBoarded.Contains(e) ? PLANKS + GLASS : GLASS;
            case Grids.EDGE_WINDOW_BROKEN: return grid.edgeBoarded.Contains(e) ? PLANKS : 0;
            case Grids.EDGE_BARRICADE: return PLANKS;
            case Grids.EDGE_FENCE: return 2 * PLANKS;
            default: return 0;
        }
    }

    static float Tile(Grid grid, int x, int y)
    {
        var t = Grids.getTile(grid, x, y);
        return t == Grids.WALL ? WALL : t == Grids.DOOR ? (Grids.isOpen(grid, x, y) ? 0 : DOOR) : t == Grids.BARRICADE ? PLANKS : 0;
    }
}
