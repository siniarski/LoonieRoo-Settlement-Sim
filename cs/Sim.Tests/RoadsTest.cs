// Roads (Roads.cs): the road search finishes in about one expansion a cell, whatever the costs.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class RoadsTest
{
    [Fact(DisplayName = "roads > a long road over mixed ground is found within the search's budget (g kept in doubles)")]
    public void long_road_within_budget()
    {
        // A 2048 world's coarse grid (512 x 512 cells) of land, woods, water and laid road (the costs costGrid and reuse
        // give), by a hash; then roads corner to corner and edge to edge. With g in float32, stored values rounding up
        // made cells re-push each other without end (2026-11-09: one road expanded a cell a million times).
        var n = new Nature { seed = 42, size = 2048 };
        int N = 512;
        var cost = new Float32Array((double)N * N);
        double[] costs = { 0.35, 1, 1, 1, 3, 3, 14 };
        for (int i = 0; i < N * N; i++) cost[i] = costs[(int)(Hash.hashRoll(42, i) * costs.Length) % costs.Length];
        var trips = new (Point a, Point b)[]
        {
            (new Point { x = 2, y = 2 }, new Point { x = 2045, y = 2045 }),
            (new Point { x = 1005, y = 820 }, new Point { x = 1005, y = 0 }),
            (new Point { x = 0, y = 1300 }, new Point { x = 2047, y = 700 }),
        };
        foreach (var (a, b) in trips) expect(Roads.route(n, cost, a, b) != null).toBe(true);
        // Even ground (one cost everywhere): many paths tie, the case that ran away.
        foreach (var even in new[] { 1.0, 0.35, 3.0 })
        {
            var flat = new Float32Array((double)N * N).fill(even);
            foreach (var (a, b) in trips) expect(Roads.route(n, flat, a, b) != null).toBe(true);
        }
    }
}
