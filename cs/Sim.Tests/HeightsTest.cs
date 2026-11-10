// The ground's height (Heights.cs): pure, the same in any order, gentle, buildings level, water under land.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HeightsTest
{
    const int SIZE = 256;

    [Fact(DisplayName = "heights > the same corners whatever order chunks are asked for in")]
    public void same_in_any_order()
    {
        var a = Heights.make(42, SIZE, "mainland");
        var b = Heights.make(42, SIZE, "mainland");
        var fromA = new double[(SIZE + 1) * (SIZE + 1)];
        for (int y = 0; y <= SIZE; y++)
            for (int x = 0; x <= SIZE; x++) fromA[y * (SIZE + 1) + x] = a.corner(x, y);
        for (int y = SIZE; y >= 0; y--)
            for (int x = SIZE; x >= 0; x--) expect(b.corner(x, y)).toBe(fromA[y * (SIZE + 1) + x]);
    }

    [Fact(DisplayName = "heights > flat on a hand-made map")]
    public void flat_on_hand_made_maps()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.%.."] });
        expect(Heights.of(state)).toBeNull();
        expect(Heights.height(state, 2.5, 0.5)).toBe(0);
    }

    [Fact(DisplayName = "heights > gentle: ten metres in all at most, and no step over a half metre between neighbouring land corners")]
    public void gentle()
    {
        foreach (var seed in new double[] { 1, 42 })
        {
            var f = Heights.make(seed, SIZE, "mainland");
            double lo = 1e9, hi = -1e9, steep = 0;
            for (int y = 0; y <= SIZE; y++)
                for (int x = 0; x <= SIZE; x++)
                {
                    double h = f.corner(x, y);
                    lo = Math.Min(lo, h);
                    hi = Math.Max(hi, h);
                    if (x < SIZE && !nearWater(f, x, y) && !nearWater(f, x + 1, y)) steep = Math.Max(steep, Math.Abs(f.corner(x + 1, y) - h));
                    if (y < SIZE && !nearWater(f, x, y) && !nearWater(f, x, y + 1)) steep = Math.Max(steep, Math.Abs(f.corner(x, y + 1) - h));
                }
            expect(hi - lo).toBeLessThan(14);
            expect(hi - lo).toBeGreaterThan(1);
            expect(steep).toBeLessThan(0.5);
        }
    }

    static readonly Dictionary<Heights.Field, bool[]> wetOf = new();

    /// <summary>A corner within a tile of water (the bank's lip and the bed are steeper, on purpose).</summary>
    static bool nearWater(Heights.Field f, int x, int y)
    {
        if (!wetOf.TryGetValue(f, out var wet))
        {
            wet = new bool[SIZE * SIZE];
            for (int ty = 0; ty < SIZE; ty++)
                for (int tx = 0; tx < SIZE; tx++) wet[ty * SIZE + tx] = NatureMod.isWater(f.regions.Region(0, 0).nature, tx, ty);
            wetOf[f] = wet;
        }
        for (int dy = -2; dy <= 1; dy++)
            for (int dx = -2; dx <= 1; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (tx >= 0 && ty >= 0 && tx < SIZE && ty < SIZE && wet[ty * SIZE + tx]) return true;
            }
        return false;
    }

    [Fact(DisplayName = "heights > every building sits on one level")]
    public void buildings_level()
    {
        var f = Heights.make(42, SIZE, "mainland");
        int checkedBuildings = 0;
        foreach (var lot in f.regions.Region(0, 0).lots)
            foreach (var p in Lots.layoutLot(42, lot).buildings)
            {
                bool turned = p.facing == "east" || p.facing == "west";
                int w = turned ? p.plan.h : p.plan.w, h = turned ? p.plan.w : p.plan.h;
                double level = f.corner(p.ox, p.oy);
                for (int y = p.oy; y <= p.oy + h; y++)
                    for (int x = p.ox; x <= p.ox + w; x++) expect(f.corner(x, y)).toBe(level);
                checkedBuildings++;
            }
        expect(checkedBuildings).toBeGreaterThan(10);
    }

    [Fact(DisplayName = "heights > water stands at or under the ground at its edge, and land is over it")]
    public void water_under_land()
    {
        foreach (var map in new[] { "mainland", "islands" })
        {
            var f = Heights.make(7, SIZE, map);
            int wetTiles = 0;
            for (int y = 0; y < SIZE; y++)
                for (int x = 0; x < SIZE; x++)
                {
                    bool wet = NatureMod.isWater(f.regions.Region(0, 0).nature, x, y) && f.regions.Region(0, 0).roadMask[y * SIZE + x] == 0;
                    double surface = f.water(x + 0.5, y + 0.5);
                    if (wet && !besideBridge(f, x, y))
                    {
                        wetTiles++;
                        // The bed under the surface at every corner.
                        expect(f.corner(x, y)).toBeLessThan(f.water(x, y));
                    }
                    else if (!nearWater(f, x, y) && !nearWater(f, x + 1, y + 1)) expect(f.at(x + 0.5, y + 0.5)).toBeGreaterThan(surface);
                }
            expect(wetTiles).toBeGreaterThan(100);
        }
    }

    /// <summary>A tile touching a road over water: a bridge's deck and its sides stand over the surface.</summary>
    static bool besideBridge(Heights.Field f, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (tx >= 0 && ty >= 0 && tx < SIZE && ty < SIZE && f.regions.Region(0, 0).roadMask[ty * SIZE + tx] != 0) return true;
            }
        return false;
    }

    [Fact(DisplayName = "heights > between corners the ground is bilinear")]
    public void bilinear()
    {
        var f = Heights.make(3, 128, "mainland");
        expect(f.at(10, 20)).toBe(f.corner(10, 20));
        expect(f.at(10.5, 20)).toBeCloseTo((f.corner(10, 20) + f.corner(11, 20)) / 2, 9);
    }

    [Fact(DisplayName = "heights > the far field's coarse heights stay near the real ones on land")]
    public void coarse_near_corner()
    {
        var f = Heights.make(42, SIZE, "mainland");
        var off = new List<double>();
        for (int y = 0; y <= SIZE; y += 2)
            for (int x = 0; x <= SIZE; x += 2)
            {
                bool wet = false;
                for (int dy = -3; dy <= 3 && !wet; dy++)
                    for (int dx = -3; dx <= 3 && !wet; dx++) wet = NatureMod.isWater(f.regions.Region(0, 0).nature, x + dx, y + dy);
                if (!wet) off.Add(Math.Abs(f.coarse(x, y, false) - f.corner(x, y)));
            }
        off.Sort();
        expect(off.Count > 1000).toBe(true);
        expect(off[(int)(off.Count * 0.95)]).toBeLessThan(0.5);
        expect(off[^1]).toBeLessThan(1.6);
    }
}
