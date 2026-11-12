// isPassable and crossable laid out one mover at a time (2026-11-12, the second quality pass): the new layout gives
// every answer the old one did. The old functions are copied here as they were (the only change: `grid.parkedCount >
// 0 &&` dropped from the parked test, which isParked does itself, as parkedCount is internal), and both are asked about
// every mover (and a name none of them is) on a map with every tile kind, doors open, shut and locked, water deep and
// shallow and cars parked, and on every edge kind, open, locked and boarded, each way, off the map's edge too.
// When a rule changes on purpose, this test has done its job: delete it.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Grids;
using static LoonieRoo.Sim.Pathfinding;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class PassableLayoutTest
{
    static bool oldIsPassable(Grid grid, int x, int y, string mover)
    {
        if (mover == SWIMMER) { if (getTile(grid, x, y) == WATER) return true; mover = FREE; }
        if (mover != VEHICLE && Grids.isParked(grid, x, y)) return false;
        if (mover == BREACHER) return inBounds(grid, x, y) && getTile(grid, x, y) != WATER;
        var tile = getTile(grid, x, y);
        bool openDoor = tile == DOOR && isOpen(grid, x, y);
        if (mover == CRAWLER) return tile == FLOOR || tile == BARRICADE || openDoor;
        if (mover == VEHICLE) return tile == FLOOR;
        bool shallows = tile == WATER && Swim.depth(grid, x, y) == Swim.SHALLOW;
        if (mover == ANIMAL) return tile == FLOOR || shallows;
        if (mover == RAIDER) return tile == FLOOR || (tile == DOOR && !isLocked(grid, x, y));
        if (mover == SURVIVOR) return isWalkable(grid, x, y);
        if (mover == FREE) return tile == FLOOR || tile == BARRICADE || openDoor;
        return tile == FLOOR || openDoor || shallows;
    }

    static bool oldCrossable(Grid grid, int ax, int ay, int bx, int by, string mover)
    {
        int e = edgeBetween(grid, ax, ay, bx, by, out int kind);
        if (e < 0) return true;
        if (mover == SWIMMER) mover = FREE;
        if (kind == EDGE_NONE || mover == BREACHER) return true;
        if (kind == EDGE_WALL || kind == EDGE_FENCE || mover == VEHICLE) return false;
        if (kind == EDGE_DOOR && grid.edgeBoarded.Contains(e)) return mover == SURVIVOR || mover == FREE;
        if (kind == EDGE_DOOR) return mover == SURVIVOR || (mover == RAIDER && !grid.edgeLocked.Contains(e)) || edgeIsOpen(grid, e);
        if (kind == EDGE_BARRICADE) return mover == SURVIVOR || mover == CRAWLER;
        if (kind == EDGE_WINDOW || grid.edgeBoarded.Contains(e)) return false;
        if (kind == EDGE_WINDOW_BROKEN) return mover != SURVIVOR && mover != FREE;
        return true;
    }

    static readonly string[] MOVERS = [SURVIVOR, FREE, ZOMBIE, BREACHER, RAIDER, CRAWLER, VEHICLE, ANIMAL, RUNNER, SWIMMER, "nobody"];

    /// <summary>A small, fixed stand-in for randomness (tests may not need the sim's RNG to stay put).</summary>
    static int roll(ref uint seed, int n)
    {
        seed = seed * 1664525 + 1013904223;
        return (int)((seed >> 8) % (uint)n);
    }

    static Grid map(uint seed)
    {
        const int N = 40;
        var grid = createGrid(N, N);
        string[] tiles = [FLOOR, FLOOR, WALL, DOOR, DOOR, WATER, BARRICADE];
        // A lake in one corner, so there's deep water as well as shallows.
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool lake = x >= 28 && y >= 28;
                setTile(grid, x, y, lake && roll(ref seed, 6) > 0 ? WATER : tiles[roll(ref seed, tiles.Length)]);
            }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = tileIndex(grid, x, y);
                if (getTile(grid, x, y) == DOOR)
                {
                    int d = roll(ref seed, 4); // shut, open, locked, both
                    if (d == 1 || d == 3) grid.open.Add(i);
                    if (d >= 2) grid.locked.Add(i);
                }
                if (roll(ref seed, 8) == 0) setParked(grid, i, true);
                foreach (var side in new[] { "n", "w" })
                {
                    if (!setEdge(grid, x, y, side, roll(ref seed, EDGE_FENCE + 1))) continue;
                    int e = edgeIndex(grid, x, y, side);
                    if (roll(ref seed, 2) == 0) grid.edgeOpen.Add(e);
                    if (roll(ref seed, 3) == 0) grid.edgeLocked.Add(e);
                    if (roll(ref seed, 3) == 0) grid.edgeBoarded.Add(e);
                }
            }
        return grid;
    }

    [Fact(DisplayName = "passable layout > isPassable gives every answer the old one did, for every mover and tile")]
    public void Same_tiles()
    {
        var seen = new HashSet<string>();
        foreach (uint seed in new uint[] { 1, 2, 3 })
        {
            var grid = map(seed);
            for (int y = -1; y <= grid.height; y++)
                for (int x = -1; x <= grid.width; x++)
                    foreach (var mover in MOVERS)
                    {
                        bool was = oldIsPassable(grid, x, y, mover);
                        expect(isPassable(grid, x, y, mover), $"{mover} at ({x}, {y}): {getTile(grid, x, y)}").toBe(was);
                        seen.Add($"{getTile(grid, x, y)} {Swim.depth(grid, x, y)} {isOpen(grid, x, y)} {isLocked(grid, x, y)} {isParked(grid, x, y)}");
                    }
        }
        // Every tile kind with a car parked on it and without: floor, wall, barricade, water shallow and deep, and doors
        // shut, open, locked and both; and off the map.
        expect(seen.Count, string.Join("\n", seen.Order())).toBe(2 * (3 + 2 + 4) + 1);
    }

    [Fact(DisplayName = "passable layout > crossable gives every answer the old one did, for every mover and edge")]
    public void Same_edges()
    {
        var seen = new HashSet<string>();
        foreach (uint seed in new uint[] { 1, 2, 3 })
        {
            var grid = map(seed);
            for (int y = -1; y <= grid.height; y++)
                for (int x = -1; x <= grid.width; x++)
                    foreach (var d in DIRS)
                        foreach (var mover in MOVERS)
                        {
                            int bx = x + d.x, by = y + d.y;
                            bool was = oldCrossable(grid, x, y, bx, by, mover);
                            expect(crossable(grid, x, y, bx, by, mover), $"{mover} ({x}, {y}) to ({bx}, {by})").toBe(was);
                            int e = edgeBetween(grid, x, y, bx, by, out int kind);
                            if (e >= 0) seen.Add($"{kind} {grid.edgeOpen.Contains(e)} {grid.edgeLocked.Contains(e)} {grid.edgeBoarded.Contains(e)} {mover}");
                        }
        }
        // For every mover: no edge, and every other edge kind in every state of open, locked and boarded.
        expect(seen.Count, string.Join("\n", seen.Order())).toBe((1 + EDGE_FENCE * 8) * MOVERS.Length);
    }
}
