// The ground's relief (Heights.relief: Terrain's elevation as metres, 2026-11-10): continuous over chunk and region
// borders, gentle, flat where a building needs it, rivers in valleys, lakes level and never over the land beside them.
// (Heights is read by the renderer only: nothing here is the sim's state.)
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HeightsReliefTest
{
    [Fact(DisplayName = "relief > continuous over chunk and region borders, and no steeper than a tenth anywhere")]
    public void relief_continuous()
    {
        var f = Heights.make(7, 2048, "mainland");
        double worst = 0;
        // Along x across the plan chunk borders (every 32) and the region border (1024), on several rows; and along y.
        foreach (int y in new[] { 100, 517, 1023, 1024, 1500 })
            for (int x = 0; x < 2047; x++)
            {
                double step = Math.Abs(Heights.relief(f, x + 1, y) - Heights.relief(f, x, y));
                worst = Math.Max(worst, step);
                if (x % 32 == 31 || x == 1023) expect(step).toBeLessThan(0.12);
            }
        foreach (int x in new[] { 100, 517, 1023, 1024, 1500 })
            for (int y = 0; y < 2047; y++) worst = Math.Max(worst, Math.Abs(Heights.relief(f, x, y + 1) - Heights.relief(f, x, y)));
        // The steepest the lattice allows: smoothstep's 1.5 x the biggest step of elevation between points, a fraction of RELIEF.
        expect(worst).toBeLessThan(0.12);
        // The start is the zero of the relief.
        var s = f.nature.start;
        expect(Heights.relief(f, s.x, s.y)).toBe(0);
    }

    [Fact(DisplayName = "relief > the ground is continuous across a region border too (a 1100-tile world: regions of 1024 and 76)")]
    public void ground_across_a_region_border()
    {
        var f = Heights.make(3, 1100, "mainland");
        double worst = 0;
        for (int y = 10; y < 1090; y += 5)
        {
            double across = Math.Abs(f.corner(1024, y) - f.corner(1023, y));
            worst = Math.Max(worst, across);
        }
        // No step at the border beyond what a bank or a building's ease makes anywhere (about half a metre a tile).
        expect(worst).toBeLessThan(0.5);
    }

    [Fact(DisplayName = "relief > a few metres in all, the start high on its rise and rivers in valleys under it")]
    public void relief_shape()
    {
        foreach (var seed in new double[] { 7, 42 })
        {
            var f = Heights.make(seed, 512, "mainland");
            double lo = 1e9, hi = -1e9;
            for (int y = 0; y < 512; y += 2)
                for (int x = 0; x < 512; x += 2)
                {
                    double r = Heights.relief(f, x, y);
                    lo = Math.Min(lo, r);
                    hi = Math.Max(hi, r);
                }
            expect(hi - lo).toBeLessThan(12);
            expect(hi - lo).toBeGreaterThan(2);
            // The start's rise: nothing round it (within a river's valley) stands far over it.
            expect(hi).toBeLessThan(4);
            double sum = 0; int wet = 0;
            for (int y = 0; y < 512; y += 2)
                for (int x = 0; x < 512; x += 2)
                    if (NatureMod.isWater(f.nature, x, y)) { sum += Heights.relief(f, x, y); wet++; }
            expect(wet).toBeGreaterThan(100);
            // The water's tiles are lower than the start by a metre at least, on average: the start stands on a rise.
            expect(sum / wet).toBeLessThan(-1);
        }
    }

    [Fact(DisplayName = "relief > the ground round every building is exactly its level for a tile (no step at a door)")]
    public void apron_is_level()
    {
        var f = Heights.make(42, 256, "mainland");
        var plan = f.regions.Region(0, 0);
        var rects = new List<int[]>();
        foreach (var lot in plan.lots)
            foreach (var p in Lots.layoutLot(42, lot).buildings)
            {
                bool turned = p.facing == "east" || p.facing == "west";
                rects.Add([p.ox, p.oy, turned ? p.plan.h : p.plan.w, turned ? p.plan.w : p.plan.h]);
            }
        int rings = 0;
        foreach (var r in rects)
        {
            double level = f.corner(r[0], r[1]);
            for (int y = r[1] - 1; y <= r[1] + r[3] + 1; y++)
                for (int x = r[0] - 1; x <= r[0] + r[2] + 1; x++)
                {
                    if (x < 0 || y < 0 || x > 256 || y > 256) continue;
                    // Corners one tile out along a side (not the diagonal ones), and clear of any other building and of water.
                    bool side = (x >= r[0] && x <= r[0] + r[2]) || (y >= r[1] && y <= r[1] + r[3]);
                    if (!side) continue;
                    bool crowded = false;
                    foreach (var o in rects)
                        if (!ReferenceEquals(o, r) && x >= o[0] - 6 && x <= o[0] + o[2] + 6 && y >= o[1] - 6 && y <= o[1] + o[3] + 6) crowded = true;
                    if (crowded || f.floor(x, y) < -50) continue;
                    bool nearWater = false;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++) nearWater |= NatureMod.isWater(f.nature, x + dx, y + dy);
                    if (nearWater) continue;
                    expect(f.corner(x, y)).toBeCloseTo(level, 9);
                    rings++;
                }
        }
        expect(rings).toBeGreaterThan(100);
    }

    /// <summary>A world (small seeds, 256 tiles) with a lake of real size: the seed, and the lake's tiles.</summary>
    static (double seed, Heights.Field f, List<(int x, int y)> tiles) aLake()
    {
        for (double seed = 1; seed <= 40; seed++)
        {
            var f = Heights.make(seed, 256, "mainland");
            var t = Terrain.Of(f.nature);
            var tiles = new List<(int, int)>();
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++) if (t.isLake(x, y) && !f.regions.IsRoad(x, y)) tiles.Add((x, y));
            if (tiles.Count > 400) return (seed, f, tiles);
        }
        throw new Exception("no seed up to 40 has a lake of 400 tiles at 256");
    }

    [Fact(DisplayName = "relief > a lake's water is level, and no lower than a metre under the land at its rim")]
    public void lake_is_level()
    {
        var (seed, f, tiles) = aLake();
        var set = new HashSet<(int, int)>(tiles);
        double lo = 1e9, hi = -1e9;
        int deep = 0;
        foreach (var (x, y) in tiles)
        {
            // Well inside: every tile within four is lake.
            bool inside = true;
            for (int dy = -4; dy <= 4 && inside; dy++)
                for (int dx = -4; dx <= 4 && inside; dx++) inside = set.Contains((x + dx, y + dy));
            if (!inside) continue;
            double w = f.water(x + 0.5, y + 0.5);
            lo = Math.Min(lo, w);
            hi = Math.Max(hi, w);
            deep++;
        }
        expect(deep).toBeGreaterThan(20);
        expect(hi - lo).toBeLessThan(0.05);
    }

    [Fact(DisplayName = "relief > water is never over the land beside it: not above the ground at the tile beside it")]
    public void water_not_over_banks()
    {
        foreach (var (seed, map) in new[] { (7.0, "mainland"), (42.0, "mainland"), (5.0, "islands") })
        {
            var f = Heights.make(seed, 256, map);
            var n = f.nature;
            var plan = f.regions.Region(0, 0);
            bool Wet(int x, int y) => x >= 0 && y >= 0 && x < 256 && y < 256 && NatureMod.isWater(n, x, y) && plan.roadMask[y * 256 + x] == 0;
            bool Road(int x, int y) => x >= 0 && y >= 0 && x < 256 && y < 256 && plan.roadMask[y * 256 + x] != 0;
            double worst = -1e9; int checkedCorners = 0;
            for (int y = 2; y < 254; y++)
                for (int x = 2; x < 254; x++)
                {
                    if (Wet(x, y) || Road(x, y)) continue;
                    // The nearest wet tile within 2 (and no road in the way: a bridge's deck is over its water on purpose).
                    double surface = double.NaN;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (Wet(x + dx, y + dy) && !Road(x + dx, y + dy)) surface = double.IsNaN(surface) ? f.water(x + dx + 0.5, y + dy + 0.5) : Math.Max(surface, f.water(x + dx + 0.5, y + dy + 0.5));
                    if (double.IsNaN(surface)) continue;
                    bool beside = false;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++) beside |= Road(x + dx, y + dy);
                    if (beside) continue;
                    // The land at this tile's centre, against the water's surface beside it (a river on a slope tilts with it: up to 7% across, 0.14 m over two tiles: a hand's breadth of slack).
                    double ground = f.at(x + 0.5, y + 0.5);
                    worst = Math.Max(worst, surface - ground);
                    checkedCorners++;
                }
            expect(checkedCorners).toBeGreaterThan(200);
            expect(worst).toBeLessThan(0.2);
        }
    }

    [Fact(DisplayName = "relief > the sim's state does not depend on the ground: ticks with and without reading the heights give the same save")]
    public void sim_does_not_read_heights()
    {
        string run(bool reading)
        {
            var state = Worldgen.createWorld(3, 192, Difficulty.NORMAL);
            for (int i = 0; i < 300; i++)
            {
                Tick.tick(state);
                if (reading && i % 20 == 0)
                {
                    var h = Heights.of(state);
                    for (int y = 0; y < 192; y += 6)
                        for (int x = 0; x < 192; x += 6) h.at(x + 0.5, y + 0.5);
                }
            }
            return Savefile.toSaveFile(state);
        }
        expect(run(true) == run(false)).toBe(true);
    }
}
