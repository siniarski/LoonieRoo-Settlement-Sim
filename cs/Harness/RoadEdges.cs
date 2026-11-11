// How far the drawn country road (a ribbon ROAD_REACH each side of the plan's line: godot/World3D/RoadRibbons.cs)
// and the road tiles the sim reads disagree (2026-11-12, docs/roads-off-grid.md step 2's question). Per seed, every
// country road tile and its neighbours, sampled 4x4 a tile: a point within the ribbon on a tile that is not road
// ("looks road, drives as grass"), a point outside it on a road tile ("looks grass, drives as road"), each as area
// per metre of road, the share of road tiles with any such point, and how deep into the wrong side a point goes.
//   SEEDS=1,2,3 SIZE=512 dotnet test cs/Harness -c Release --filter "DisplayName=road edges"
// Report: cs/Harness/out/road-edges.txt (OUT overrides).

using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class RoadEdges
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    [Fact(DisplayName = "road edges")]
    public void Edges()
    {
        var out_ = new List<string>();
        int size = int.Parse(Env("SIZE") ?? "512");
        double R = Roads.ROAD_REACH;
        foreach (var seed in (Env("SEEDS") ?? "1,2,3").Split(',').Select(double.Parse))
        {
            var p = Worldplan.makePlan(NatureMod.natureFor(seed, size, "mainland"));
            int at(int x, int y) => x < 0 || y < 0 || x >= size || y >= size ? 0 : (int)p.roadMask[y * size + x];
            // The lines' segments by 8-tile bucket, for the distance to the nearest.
            var buckets = new Dictionary<(int, int), List<(double ax, double ay, double bx, double by)>>();
            double length = 0;
            foreach (var line in p.roads)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var (a, b) = (line[i], line[i + 1]);
                    length += Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                    int x0 = (int)Math.Floor((Math.Min(a.x, b.x) - 4) / 8), x1 = (int)Math.Floor((Math.Max(a.x, b.x) + 4) / 8);
                    int y0 = (int)Math.Floor((Math.Min(a.y, b.y) - 4) / 8), y1 = (int)Math.Floor((Math.Max(a.y, b.y) + 4) / 8);
                    for (int by = y0; by <= y1; by++)
                        for (int bx = x0; bx <= x1; bx++)
                        {
                            if (!buckets.TryGetValue((bx, by), out var l)) buckets[(bx, by)] = l = new();
                            l.Add((a.x, a.y, b.x, b.y));
                        }
                }
            double Dist(double px, double py)
            {
                double best = double.MaxValue;
                if (!buckets.TryGetValue(((int)Math.Floor(px / 8), (int)Math.Floor(py / 8)), out var l)) return best;
                foreach (var (ax, ay, bx, by) in l)
                {
                    double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                    double t = len2 < 1e-12 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
                    double ex = ax + dx * t - px, ey = ay + dy * t - py;
                    best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
                }
                return best;
            }
            // Every country road tile and the tiles round it, once; streets, sidewalks and water left out (their own looks).
            var seen = new HashSet<int>();
            int roadTiles = 0, badTiles = 0;
            double looksRoad = 0, looksGrass = 0, deepRoad = 0, deepGrass = 0;
            var depths = new List<double>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (at(x, y) != Worldplan.ROAD) continue;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int tx = x + ox, ty = y + oy, m = at(tx, ty);
                            if (tx < 0 || ty < 0 || tx >= size || ty >= size || !seen.Add(ty * size + tx)) continue;
                            if (m == Worldplan.STREET || m == Worldplan.SIDEWALK || NatureMod.isWater(p.nature, tx, ty)) continue;
                            bool road = m == Worldplan.ROAD, bad = false;
                            if (road) roadTiles++;
                            for (int sy = 0; sy < 4; sy++)
                                for (int sx = 0; sx < 4; sx++)
                                {
                                    double d = Dist(tx + (sx + 0.5) / 4, ty + (sy + 0.5) / 4);
                                    if (d <= R && !road) { looksRoad += 1 / 16.0; deepRoad = Math.Max(deepRoad, R - d); bad = true; depths.Add(R - d); }
                                    if (d > R && road) { looksGrass += 1 / 16.0; deepGrass = Math.Max(deepGrass, d - R); bad = true; depths.Add(d - R); }
                                }
                            if (bad && road) badTiles++;
                        }
                }
            depths.Sort();
            string q(double f) => depths.Count == 0 ? "-" : $"{depths[(int)(depths.Count * f)]:0.00}";
            out_.Add($"seed {seed} size {size}: {p.roads.Count} roads, {length:0} m of line, {roadTiles} country road tiles");
            out_.Add($"  looks road, drives as grass: {looksRoad:0} m2 ({looksRoad / Math.Max(1, length):0.000} m2 a metre), deepest {deepRoad:0.00} m");
            out_.Add($"  looks grass, drives as road: {looksGrass:0} m2 ({looksGrass / Math.Max(1, length):0.000} m2 a metre), deepest {deepGrass:0.00} m");
            out_.Add($"  road tiles with any: {100.0 * badTiles / Math.Max(1, roadTiles):0.0}%; depth of a wrong point: median {q(0.5)} m, 90th {q(0.9)} m");
            // Places to look at the joins: each road's ends (on a street, at another road, at the map's edge) and where it meets water.
            for (int ri = 0; ri < p.roads.Count; ri++)
            {
                var line = p.roads[ri];
                var marks = new List<string>();
                foreach (var e in new[] { line[0], line[^1] })
                {
                    int m = at((int)e.x, (int)e.y);
                    marks.Add($"end {e.x:0},{e.y:0} ({(m == Worldplan.STREET ? "street" : m == Worldplan.SIDEWALK ? "sidewalk" : m == Worldplan.ROAD ? "road" : "off")})");
                }
                bool wet = false;
                foreach (var pt in line)
                {
                    bool w = NatureMod.isWater(p.nature, (int)pt.x, (int)pt.y);
                    if (w && !wet) marks.Add($"bridge {pt.x:0},{pt.y:0}");
                    wet = w;
                }
                out_.Add($"  road {ri}: {string.Join("; ", marks)}");
            }
        }
        var dir = Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "road-edges.txt"), out_);
        foreach (var l in out_) Console.WriteLine(l);
    }
}
