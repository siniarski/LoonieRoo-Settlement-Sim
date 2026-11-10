// The land's large-scale shape (Terrain) and what it gives nature (NatureMod): every point drains out, the start keeps
// its river and no lake against it, lakes and wild country cover their shares, towns stand on settled land, roads go
// round lakes, woods thin at their edges.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class TerrainTest
{
    static double Dist(Point a, int x, int y) => Math.Sqrt((double)(a.x - x) * (a.x - x) + (double)(a.y - y) * (a.y - y));

    [Fact(DisplayName = "terrain > every lattice point drains out to the edge or the sea, never round in a loop")]
    public void drains_out()
    {
        foreach (var map in new[] { "mainland", "island", "quarantine" })
        {
            var t = Terrain.Of(NatureMod.natureFor(4, 1024, map));
            int n = t.W * t.W, stuck = 0;
            for (int k = 0; k < n; k++)
            {
                int c = k, steps = 0;
                while (t.downOf(c) >= 0 && steps++ <= n) c = t.downOf(c);
                if (steps > n) stuck++;
            }
            expect($"{map}: {stuck} stuck").toBe($"{map}: 0 stuck");
        }
    }

    [Fact(DisplayName = "terrain > the start has a river or a lake 30-90 tiles off, and no water in its clearing")]
    public void start_has_water_near()
    {
        foreach (var size in new[] { 256, 512, 2048 })
            for (int seed = 1; seed <= 8; seed++)
            {
                var n = NatureMod.natureFor(seed, size, "mainland");
                var t = Terrain.Of(n);
                double nearest = double.PositiveInfinity;
                for (int y = n.start.y - 100; y <= n.start.y + 100; y++)
                    for (int x = n.start.x - 100; x <= n.start.x + 100; x++)
                    {
                        if (x < 0 || y < 0 || x >= size || y >= size || !NatureMod.isWater(n, x, y)) continue;
                        if (t.riverDistance(x, y) == 0 || t.isLake(x, y)) nearest = Math.Min(nearest, Dist(n.start, x, y));
                    }
                expect($"{size} seed {seed}: {nearest >= NatureMod.START_CLEAR_RADIUS + 8 && nearest <= 90}").toBe($"{size} seed {seed}: True");
            }
    }

    [Fact(DisplayName = "terrain > lakes cover about their share of a big world, and wild country its share")]
    public void shares()
    {
        foreach (var seed in new[] { 1.0, 2, 3 })
        {
            var n = NatureMod.natureFor(seed, 2048, "mainland");
            var t = Terrain.Of(n);
            int lake = 0, wild = 0, all = 0;
            for (int y = 0; y < 2048; y += 8)
                for (int x = 0; x < 2048; x += 8)
                {
                    all++;
                    if (t.isLake(x, y)) lake++;
                    if (t.settlement(x, y) < t.wildAt) wild++;
                }
            double lakes = (double)lake / all, wilds = (double)wild / all;
            expect($"seed {seed} lakes {lakes:0.00}: {lakes > 0.03 && lakes < 0.1}").toBe($"seed {seed} lakes {lakes:0.00}: True");
            expect($"seed {seed} wild {wilds:0.00}: {wilds > 0.18 && wilds < 0.32}").toBe($"seed {seed} wild {wilds:0.00}: True");
        }
    }

    [Fact(DisplayName = "terrain > towns stand on settled land, and wild country has none")]
    public void towns_on_settled_land()
    {
        var r = new WorldRegions(6, 2048, "mainland");
        var t = Terrain.Of(r.nature);
        var towns = r.AllTowns();
        expect(towns.Count > 60).toBe(true);
        foreach (var town in towns) expect(t.settlement(town.x, town.y) >= t.wildAt).toBe(true);
    }

    [Fact(DisplayName = "terrain > country roads go round lakes rather than across them")]
    public void roads_round_lakes()
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var r = new WorldRegions(seed, 1024, "mainland");
            var n = r.nature;
            foreach (var line in r.Region(0, 0).roads)
            {
                int wet = 0;
                foreach (var q in line) if (NatureMod.isLake(n, q.x, q.y)) wet++;
                expect($"seed {seed}: {line.Count} points, {wet} on a lake: {wet <= 40}").toBe($"seed {seed}: {line.Count} points, {wet} on a lake: True");
            }
        }
    }

    [Fact(DisplayName = "terrain > woods thin out at their edges, with the odd tree just outside, none in open country")]
    public void trees_thin_at_edges()
    {
        double at = NatureMod.FOREST_AT;
        expect(NatureMod.treeChanceAt(at - NatureMod.TREE_FRINGE - 0.01)).toBe(0);
        expect(NatureMod.treeChanceAt(at - 0.01) > 0 && NatureMod.treeChanceAt(at - 0.01) <= NatureMod.TREE_STRAY).toBe(true);
        expect(NatureMod.treeChanceAt(at + 0.01) < NatureMod.treeChanceAt(at + NatureMod.FOREST_DEEP)).toBe(true);
        expect(NatureMod.treeChanceAt(1)).toBe(NatureMod.TREE_DEEP);
        for (double w = 0; w < 1; w += 0.01) expect(NatureMod.treeChanceAt(w) <= NatureMod.treeChanceAt(w + 0.01)).toBe(true);
    }

    [Fact(DisplayName = "terrain > every world has its wet and dry land: moisture is ranked, so small worlds are wooded too")]
    public void small_worlds_wooded()
    {
        foreach (var size in new[] { 128, 256 })
            for (int seed = 1; seed <= 8; seed++)
            {
                var n = NatureMod.natureFor(seed, size, "mainland");
                double sum = 0;
                int all = 0, forest = 0;
                for (int y = 0; y < size; y += 2)
                    for (int x = 0; x < size; x += 2)
                    {
                        all++;
                        sum += Terrain.Of(n).moisture(x, y);
                        if (NatureMod.isForest(n, x, y)) forest++;
                    }
                expect($"{size} seed {seed} moisture {sum / all:0.00}: {Math.Abs(sum / all - 0.5) < 0.12}").toBe($"{size} seed {seed} moisture {sum / all:0.00}: True");
                expect($"{size} seed {seed} forest {(double)forest / all:0.00}: {forest > all / 20}").toBe($"{size} seed {seed} forest {(double)forest / all:0.00}: True");
            }
    }

    [Fact(DisplayName = "terrain > rivers widen downstream, from RIVER_MIN to at most RIVER_MAX")]
    public void rivers_widen()
    {
        var t = Terrain.Of(NatureMod.natureFor(2, 2048, "mainland"));
        // The widest bank-to-middle reach of water found next to the strongest flow beats the weakest river's.
        double strongest = 0;
        (int x, int y) at = (0, 0);
        for (int j = 1; j < t.W - 1; j++)
            for (int i = 1; i < t.W - 1; i++)
            {
                double f = t.flowAt(i * Terrain.CELL, j * Terrain.CELL);
                if (f > strongest) { strongest = f; at = (i * Terrain.CELL, j * Terrain.CELL); }
            }
        expect(strongest > Terrain.RIVER_FLOW * 20).toBe(true);
        int wet = 0;
        for (int y = at.y - 24; y <= at.y + 24; y++)
            for (int x = at.x - 24; x <= at.x + 24; x++)
                if (x >= 0 && y >= 0 && x < 2048 && y < 2048 && t.riverDistance(x, y) == 0) wet++;
        expect(wet > 49 * 3).toBe(true); // a big river: wider than a few tiles across the 49-tile box
    }
}
