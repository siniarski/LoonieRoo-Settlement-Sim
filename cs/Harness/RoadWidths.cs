// How wide the roads are, in numbers (2026-11-11, the player's "two lanes, each fits a car"): per seed, the towns,
// streets and lots of a world's plan; a street's cross-section (the road mask's values across its middle); the
// country roads' widths (tiles of road across the line, sampled along every road); and, in the generated world, each
// parked car standing on road ground with the clear road beside it (the widest run of road tiles to one side of
// its body, in metres): what a car driven down the street has to get past it.
//   SEEDS=1,2,3,4,5,6 SIZE=512 [MAP=x,y,r] dotnet test cs/Harness -c Release --filter "DisplayName=road widths"
// Report: cs/Harness/out/road-widths.txt (OUT overrides).

using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class RoadWidths
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    static string Spread(List<double> v)
    {
        if (v.Count == 0) return "none";
        v.Sort();
        return $"min {v[0]:0.0} tenth {v[v.Count / 10]:0.0} median {v[v.Count / 2]:0.0} max {v[^1]:0.0} (n {v.Count})";
    }

    [Fact(DisplayName = "road widths")]
    public void Widths()
    {
        var out_ = new List<string>();
        int size = int.Parse(Env("SIZE") ?? "512");
        int road = Array.IndexOf(Grids.GROUNDS, "road"), bridge = Array.IndexOf(Grids.GROUNDS, "bridge");
        foreach (var seed in (Env("SEEDS") ?? "1,2,3,4,5,6").Split(',').Select(double.Parse))
        {
            var p = Worldplan.makePlan(NatureMod.natureFor(seed, size, "mainland"));
            int at(double x, double y) => x < 0 || y < 0 || x >= size || y >= size ? 0 : (int)p.roadMask[(int)y * size + (int)x];
            var zones = p.lots.GroupBy((l) => l.zone).OrderBy((g) => g.Key).Select((g) => $"{g.Key} {g.Count()}");
            out_.Add($"seed {seed} size {size}: towns {p.towns.Count} ({string.Join(", ", p.towns.Select((t) => $"{t.kind} r{t.radius} at {t.x},{t.y}"))}); streets {p.streets.Count}; roads {p.roads.Count}; lots {p.lots.Count} ({string.Join(", ", zones)})");
            // A street's cross-section at its middle, from 6 tiles before its line to 14 after.
            var sections = new Dictionary<string, int>();
            foreach (var s in p.streets)
            {
                int mx = (s.x0 + s.x1) / 2, my = (s.y0 + s.y1) / 2;
                var cells = new List<int>();
                for (int o = -6; o <= 14; o++) cells.Add(s.eastWest ? at(mx, s.y0 + o) : at(s.x0 + o, my));
                string key = string.Join("", cells);
                sections[key] = sections.GetValueOrDefault(key) + 1;
            }
            foreach (var kv in sections.OrderByDescending((k) => k.Value).Take(3)) out_.Add($"  street section (mask across, -6..14): {kv.Key} x{kv.Value}");
            // Country roads: the run of road tiles across the line at every fifth point, in half tiles.
            var widths = new List<double>();
            foreach (var line in p.roads)
                for (int i = 5; i + 1 < line.Count; i += 5)
                {
                    var a = line[i]; var b = line[i + 1];
                    double dx = b.x - a.x, dy = b.y - a.y, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-6 || at(a.x, a.y) != 1) continue;
                    double nx = -dy / len, ny = dx / len, w = 0;
                    foreach (int sign in new[] { -1, 1 })
                        for (double d = sign > 0 ? 0 : 0.25; d < 12; d += 0.25)
                        {
                            if (at(Math.Floor(a.x + nx * d * sign), Math.Floor(a.y + ny * d * sign)) == 0) break;
                            w += 0.25;
                        }
                    widths.Add(w);
                }
            out_.Add($"  country road width (tiles across the line): {Spread(widths)}");
            // The generated world: parked cars on road ground, and the clear road beside each.
            if (Env("CARS") == "0") continue;
            var state = Worldgen.createWorld(seed, size, Difficulty.NORMAL);
            for (int y = 0; y < size; y += Worldgen.CHUNK)
                for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
            bool isRoad(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && (Grids.groundAt(state.grid, x, y) == road || Grids.groundAt(state.grid, x, y) == bridge);
            var clear = new List<double>();
            int cars = 0;
            foreach (var v in state.vehicles ?? [])
            {
                cars++;
                if (!isRoad(v.pos.x, v.pos.y)) continue;
                // Across the car: perpendicular to its heading (32 headings; 0 east, 8 south).
                double ang = v.heading / 32.0 * Math.PI * 2;
                bool alongX = Math.Abs(Math.Cos(ang)) >= Math.Abs(Math.Sin(ang));
                int px = alongX ? 0 : 1, py = alongX ? 1 : 0;
                double half = (v.kind == "truck" ? 1.9 : 1.7) / 2, best = 0;
                foreach (int sign in new[] { -1, 1 })
                {
                    int n = 0;
                    while (n < 12 && isRoad(v.pos.x + px * sign * (n + 1), v.pos.y + py * sign * (n + 1))) n++;
                    best = Math.Max(best, n + 0.5 - half);
                }
                clear.Add(best);
            }
            out_.Add($"  cars {cars}; on road ground {clear.Count}; clear road beside a parked car (m): {Spread(clear)}; passable by a pickup with 0.5 m to spare (>= 2.4 m): {clear.Count((c) => c >= 2.4)} of {clear.Count}");
            out_.Add($"  the dead asleep: {state.dormant.Count}; furniture {state.furniture.Count}");
            // MAP=x,y,r: the plan round a point as text (. open, = road, # street, : sidewalk, a lot by its zone letter, C a car, Y a tree).
            if (Env("MAP") is { } map)
            {
                var m = map.Split(',').Select(int.Parse).ToArray();
                var carAt = new HashSet<(int, int)>((state.vehicles ?? []).Select((v) => (v.pos.x, v.pos.y)));
                for (int y = m[1] - m[2]; y <= m[1] + m[2]; y++)
                {
                    var row = new System.Text.StringBuilder();
                    for (int x = m[0] - m[2]; x <= m[0] + m[2]; x++)
                    {
                        int k = at(x, y);
                        int lot = x < 0 || y < 0 || x >= size || y >= size ? 0 : (int)p.lotMask[y * size + x];
                        row.Append(carAt.Contains((x, y)) ? 'C' : FurnitureMod.furnitureAt(state, x, y) is { kind: "tree" } ? 'Y' : k == 1 ? '=' : k == 2 ? '#' : k == 3 ? ':' : lot > 0 ? p.lots[lot - 1].zone[0] : NatureMod.isWater(p.nature, x, y) ? '~' : '.');
                    }
                    out_.Add(row.ToString());
                }
            }
        }
        var path = Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/road-widths.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", out_) + "\n");
    }
}
