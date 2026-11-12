// The ground's relief (Heights.relief: Terrain's elevation as metres, 2026-11-10): continuous over chunk and region
// borders, gentle, flat where a building needs it, rivers in valleys, lakes level and never over the land beside them.
// (Heights is read by the renderer only: nothing here is the sim's state.)
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HeightsReliefTest
{
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
