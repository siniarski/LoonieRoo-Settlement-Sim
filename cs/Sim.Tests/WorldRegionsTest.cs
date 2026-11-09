// The world plan by region (WorldRegions, docs/worldplan-regions.md): the same whatever order regions are planned in,
// towns never clashing across a border, lots inside their region, roads joining neighbouring regions.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class WorldRegionsTest
{
    // 2560: three regions to a side, the last one half a region wide (1024, 1024, 512).
    const int SIZE = 2560;

    static string Summary(WorldPlan p)
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
        var orders = new List<(int, int)[]>();
        var rowByRow = new List<(int, int)>();
        for (int ry = 0; ry < 3; ry++)
            for (int rx = 0; rx < 3; rx++) rowByRow.Add((rx, ry));
        orders.Add(rowByRow.ToArray());
        orders.Add(new[] { (1, 1), (2, 1), (2, 2), (1, 2), (0, 2), (0, 1), (0, 0), (1, 0), (2, 0) });
        Dictionary<(int, int), string> first = null;
        foreach (var order in orders)
        {
            var r = new WorldRegions(42, SIZE, "mainland");
            var got = new Dictionary<(int, int), string>();
            foreach (var (rx, ry) in order) got[(rx, ry)] = Summary(r.Region(rx, ry));
            if (first == null) { first = got; continue; }
            foreach (var (k, v) in first) expect($"{k}: {got[k]}").toBe($"{k}: {v}");
        }
    }

    [Fact(DisplayName = "world regions > towns stay apart across borders, lots inside their region, neighbours joined by a road")]
    public void borders()
    {
        var r = new WorldRegions(7, SIZE, "mainland");
        var all = r.AllTowns();
        expect(all.Count > 20).toBe(true);
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                var (a, b) = (all[i], all[j]);
                double d = Worldplan.dist(a.x, a.y, b.x, b.y);
                expect(d >= a.radius + b.radius + 48 ? "" : $"towns {a.index} and {b.index} {d:0} apart").toBe("");
            }
        int joined = 0;
        for (int ry = 0; ry < 3; ry++)
            for (int rx = 0; rx < 3; rx++)
            {
                var p = r.Region(rx, ry);
                var w = p.window;
                foreach (var l in p.lots)
                    expect(l.x >= w.x0 && l.y >= w.y0 && l.x + l.w <= w.x1 && l.y + l.h <= w.y1 ? "" : $"lot {l.id} leaves region ({rx}, {ry})").toBe("");
                // A road on the region's east border, where it has an east neighbour: the link crosses it.
                if (rx < 2)
                {
                    bool crosses = false;
                    for (int y = w.y0; y < w.y1 && !crosses; y++) crosses = r.IsRoad(w.x1 - 1, y) && r.IsRoad(w.x1, y);
                    if (crosses) joined++;
                }
            }
        expect(joined >= 4).toBe(true);
        // Every lot is found again by its id, and by its tiles.
        var p11 = r.Region(1, 1);
        foreach (var l in p11.lots.Take(50))
        {
            expect(ReferenceEquals(r.Lot(l.id), l)).toBe(true);
            expect(r.LotAt(l.x, l.y)?.id).toBe(l.id);
        }
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
