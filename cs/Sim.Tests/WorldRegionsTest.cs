// The world plan by region (WorldRegions, docs/worldplan-regions.md): the same whatever order regions are planned in,
// towns never clashing across a border, lots inside their region, roads joining neighbouring regions.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class WorldRegionsTest
{
    // 1536: two regions to a side, the last one half a region wide (1024, 512). Planning a region costs about a second
    // a full region's worth of tiles, so this is the smallest world that still has borders (every region has two
    // neighbours, a corner and an interior town set to clash across) without paying for a 3 x 3 (2560 took 28 s here).
    internal const int SIZE = 1536;
    internal const int SEED = 42;

    internal static string Summary(WorldPlan p)
    {
        ulong h = 1469598103934665603;
        void Mix(double v) => h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211;
        foreach (var v in p.roadMask) Mix(v);
        foreach (var v in p.lotMask) Mix(v);
        foreach (var l in p.lots) { Mix(l.id); Mix(l.x); Mix(l.y); Mix(l.w); Mix(l.h); }
        foreach (var t in p.towns) { Mix(t.x); Mix(t.y); Mix(t.radius); Mix(t.index); }
        return $"{h:x16} towns {p.towns.Count} lots {p.lots.Count} roads {p.roads.Count}";
    }

    [Fact(DisplayName = "world regions > every region the same whatever order they're planned in")]
    public void same_in_any_order()
    {
        // The three small regions (the corner is the full-size one: the borders class has it), row by row and the other
        // way round: each is planned with a different set of its neighbours done before it ((1, 0) first against last).
        var rowByRow = new WorldRegions(SEED, SIZE, "mainland");
        var backwards = new WorldRegions(SEED, SIZE, "mainland");
        var want = new Dictionary<(int, int), string>();
        foreach (var k in new[] { (1, 0), (0, 1), (1, 1) }) want[k] = Summary(rowByRow.Region(k.Item1, k.Item2));
        foreach (var k in new[] { (1, 1), (0, 1), (1, 0) }) expect($"{k}: {Summary(backwards.Region(k.Item1, k.Item2))}").toBe($"{k}: {want[k]}");
    }

    [Fact(DisplayName = "world regions > a world of one region is the whole plan, as before")]
    public void one_region()
    {
        var r = new WorldRegions(3, 512, "mainland");
        expect(r.Single).toBe(true);
        var whole = Worldplan.makePlan(NatureMod.natureFor(3, 512, "mainland"));
        expect(Summary(r.Region(0, 0))).toBe(Summary(whole));
    }
}

// Its own class so ./check.sh can run it in another process beside the one above (the corner region alone is a second).
public class WorldRegionsBordersTest
{
    const int SIZE = WorldRegionsTest.SIZE;
    const int SEED = WorldRegionsTest.SEED;

    // One world planned the long way round (the small regions last-to-first, then the corner), shared by the tests
    // below so each full-size plan is paid for once.
    static readonly Lazy<WorldRegions> Reverse = new(() =>
    {
        var r = new WorldRegions(SEED, SIZE, "mainland");
        foreach (var (rx, ry) in new[] { (1, 1), (0, 1), (1, 0), (0, 0) }) r.Region(rx, ry);
        return r;
    });

    [Fact(DisplayName = "world regions > the corner region the same planned first on its own as last after the rest")]
    public void corner_first_or_last()
    {
        var alone = new WorldRegions(SEED, SIZE, "mainland");
        expect(WorldRegionsTest.Summary(Reverse.Value.Region(0, 0))).toBe(WorldRegionsTest.Summary(alone.Region(0, 0)));
    }

    [Fact(DisplayName = "world regions > towns stay apart across borders, lots inside their region, neighbours joined by a road")]
    public void borders()
    {
        var r = Reverse.Value;
        var all = r.AllTowns();
        expect(all.Count > 20).toBe(true);
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a, b) = (all[i], all[j]);
                double d = Worldplan.dist(a.x, a.y, b.x, b.y);
                expect(d >= a.radius + b.radius + 48 ? "" : $"towns {a.index} and {b.index} {d:0} apart").toBe("");
            }
        int joinedEast = 0, joinedSouth = 0;
        for (int ry = 0; ry < 2; ry++)
            for (int rx = 0; rx < 2; rx++)
            {
                var p = r.Region(rx, ry);
                var w = p.window;
                foreach (var l in p.lots)
                    expect(l.x >= w.x0 && l.y >= w.y0 && l.x + l.w <= w.x1 && l.y + l.h <= w.y1 ? "" : $"lot {l.id} leaves region ({rx}, {ry})").toBe("");
                // A road on the region's east (or south) border, where it has a neighbour there: the link crosses it.
                if (rx < 1)
                {
                    bool crosses = false;
                    for (int y = w.y0; y < w.y1 && !crosses; y++) crosses = r.IsRoad(w.x1 - 1, y) && r.IsRoad(w.x1, y);
                    if (crosses) joinedEast++;
                }
                if (ry < 1)
                {
                    bool crosses = false;
                    for (int x = w.x0; x < w.x1 && !crosses; x++) crosses = r.IsRoad(x, w.y1 - 1) && r.IsRoad(x, w.y1);
                    if (crosses) joinedSouth++;
                }
            }
        expect($"joined east {joinedEast}, south {joinedSouth}").toBe("joined east 2, south 2");
        // Every lot is found again by its id, and by its tiles.
        foreach (var l in r.Region(0, 0).lots.Take(50))
        {
            expect(ReferenceEquals(r.Lot(l.id), l)).toBe(true);
            expect(r.LotAt(l.x, l.y)?.id).toBe(l.id);
        }
    }
}
