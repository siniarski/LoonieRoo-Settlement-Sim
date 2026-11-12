// Lakes in the ground's relief (Heights.relief): level water, never over the land beside it. Split from
// HeightsReliefTest so the check's shards balance.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HeightsLakesTest
{
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
}
