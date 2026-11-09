// Where world generation's time goes (2026-11-09: a whole 2048 world took ~19 minutes to generate).
// Generates every chunk of a world as ensureGeneratedAround does (generateChunk, then settle), but with
// generateChunk's four phases run here one by one (a copy of its body: terrain, lots, trees, strays) so
// each can be timed, and reports where the time went overall and how a chunk's cost grows as the world
// fills (if a later chunk costs more than an early one, something scans what's already there).
//
//   SIZE=648 dotnet test cs/Harness -c Release --filter "DisplayName=gen profile"     (648: a tenth of 2048's area)
//   SIZES=256,512,648,1024 for the whole-world times at several sizes. Report: cs/Harness/out/gen-profile.txt

using System.Diagnostics;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class GenProfile
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    [Fact(DisplayName = "gen profile")]
    public void Profile()
    {
        var out_ = new List<string>();
        var sizes = (Env("SIZES") ?? Env("SIZE") ?? "648").Split(',').Select(int.Parse).ToList();
        foreach (var size in sizes)
        {
            var sw = Stopwatch.StartNew();
            var state = Worldgen.createWorld(42, size, Difficulty.NORMAL);
            double createMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var l = Worldgen.layout(42, size, state.settings.map);
            double layoutMs = sw.Elapsed.TotalMilliseconds;
            var world = state.world!;
            int chunks = (int)world.chunks;
            var phase = new double[5]; // terrain, lots, trees, strays, settle
            var perChunk = new List<double>();
            var counts = new List<string>();
            var ctx = Worldgen.lotContext(l);
            var total = Stopwatch.StartNew();
            for (int cy = 0; cy < chunks; cy++)
                for (int cx = 0; cx < chunks; cx++)
                {
                    int i = cy * chunks + cx;
                    if (world.generated[i] == 1) continue;
                    world.generated[i] = 1;
                    var one = Stopwatch.StartNew();
                    var t = Stopwatch.StartNew();
                    Terrain(state, l, cx, cy);
                    phase[0] += t.Elapsed.TotalMilliseconds; t.Restart();
                    double x0 = cx * Worldgen.CHUNK, y0 = cy * Worldgen.CHUNK;
                    foreach (var id in l.regions.LotsInChunk(cx, cy))
                        Lots.drawLot(state, ctx, l.regions.Lot(id), x0, y0, JsMath.min(l.size, x0 + Worldgen.CHUNK) - 1, JsMath.min(l.size, y0 + Worldgen.CHUNK) - 1);
                    phase[1] += t.Elapsed.TotalMilliseconds; t.Restart();
                    Trees(state, l, x0, y0);
                    phase[2] += t.Elapsed.TotalMilliseconds; t.Restart();
                    Worldgen.strays(state, l, x0, y0);
                    phase[3] += t.Elapsed.TotalMilliseconds; t.Restart();
                    Worldgen.settle(state, cx, cy);
                    phase[4] += t.Elapsed.TotalMilliseconds;
                    state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
                    perChunk.Add(one.Elapsed.TotalMilliseconds);
                }
            double all = total.Elapsed.TotalMilliseconds;
            out_.Add($"size {size}: {chunks}x{chunks} chunks, createWorld {createMs / 1000:0.0} s, layout (world plan) {layoutMs / 1000:0.00} s, every chunk {all / 1000:0.0} s ({perChunk.Count} chunks, {all / Math.Max(1, perChunk.Count):0.00} ms each)");
            string[] names = { "terrain", "lots", "trees", "strays", "settle" };
            out_.Add("   phases: " + string.Join(", ", names.Select((n, k) => $"{n} {phase[k] / 1000:0.0} s ({100 * phase[k] / all:0}%)")));
            // How a chunk's cost grows as the world fills: the mean of each tenth, in the order generated.
            int tenth = Math.Max(1, perChunk.Count / 10);
            var tenths = Enumerable.Range(0, 10).Select(k => perChunk.Skip(k * tenth).Take(tenth).DefaultIfEmpty(0).Average());
            out_.Add("   ms a chunk by tenth generated: " + string.Join(" ", tenths.Select(v => v.ToString("0.0"))));
            out_.Add($"   then: furniture {state.furniture.Count:N0}, items {state.items.Count:N0}, dormant {state.dormant.Count:N0}, zombies {state.zombies.Count:N0}, animals {state.animals.Count:N0}, settlements {state.settlements.Count}");
            // The real path, as the game and the draw bench call it: ensureGeneratedAround at every chunk, timed by tenths.
            var real = Worldgen.createWorld(42, size, Difficulty.NORMAL);
            var tr = new List<double>();
            var rt = Stopwatch.StartNew();
            for (int y = 0; y < size; y += Worldgen.CHUNK)
                for (int x = 0; x < size; x += Worldgen.CHUNK)
                {
                    var one = Stopwatch.StartNew();
                    Worldgen.ensureGeneratedAround(real, new Point(x, y));
                    tr.Add(one.Elapsed.TotalMilliseconds);
                }
            int tt = Math.Max(1, tr.Count / 10);
            out_.Add($"   real path (ensureGeneratedAround at every chunk): {rt.Elapsed.TotalSeconds:0.0} s; ms a call by tenth: " + string.Join(" ", Enumerable.Range(0, 10).Select(k => tr.Skip(k * tt).Take(tt).DefaultIfEmpty(0).Average().ToString("0.0"))));
            File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile.txt"), string.Join("\n", out_) + "\n");
        }
    }

    [Fact(DisplayName = "gen profile create")]
    public void ProfileCreate()
    {
        var out_ = new List<string>();
        foreach (var size in (Env("SIZES") ?? "1024,1448,2048").Split(',').Select(int.Parse))
        {
            var t = Stopwatch.StartNew();
            var times = new List<string>();
            void Lap(string what) { times.Add($"{what} {t.Elapsed.TotalSeconds:0.00}"); t.Restart(); }
            var n = NatureMod.natureFor(42, size, "mainland"); Lap("nature");
            var land = Towns.landGrid(n); Lap("landGrid");
            var towns = Towns.siteTowns(n, land); Lap($"siteTowns ({towns.Count})");
            var streets = towns.flatMap((tw, i) => Towns.growStreets(n, tw, i)); Lap($"growStreets ({streets.Count})");
            var mask = new Uint8Array((double)size * size);
            Worldplan.layRoads(n, towns, streets, mask); Lap("layRoads");
            Worldplan.makePlan(n); Lap("makePlan (all of it again)");
            var state = States.createState(new NewStateOptions { seed = 42, width = size, height = size }); Lap("createState");
            var st = Worldgen.createWorld(42, size, Difficulty.NORMAL); Lap("createWorld (plan cached now)");
            out_.Add($"size {size}: " + string.Join(", ", times));
            File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile-create.txt"), string.Join("\n", out_) + "\n");
        }
    }

    /// <summary>The world plan's steps, each timed and with the managed heap after it, then the sim state (createState:
    /// the grid) on its own, at sizes up to the scale target (10,000). The plan's steps are run as makePlan runs them.</summary>
    [Fact(DisplayName = "gen profile big")]
    public void ProfileBig()
    {
        var out_ = new List<string>();
        double Mb() => GC.GetTotalMemory(true) / 1e6;
        foreach (var size in (Env("SIZES") ?? "2048,4096").Split(',').Select(int.Parse))
        {
            var t = Stopwatch.StartNew();
            double m0 = Mb();
            var times = new List<string>();
            void Lap(string what) { times.Add($"{what} {t.Elapsed.TotalSeconds:0.00} s ({Mb() - m0:0} MB)"); t.Restart(); }
            try
            {
                var n = NatureMod.natureFor(42, size, "mainland"); Lap("nature");
                if (Env("PLAN") == "0") goto state;
                var land = Towns.landGrid(n); Lap("landGrid");
                var towns = Towns.siteTowns(n, land); Lap($"siteTowns ({towns.Count})");
                var streets = towns.flatMap((tw, i) => Towns.growStreets(n, tw, i)); Lap($"growStreets ({streets.Count})");
                var mask = new Uint8Array((double)size * size); Lap("roadMask");
                var roads = Worldplan.layRoads(n, towns, streets, mask); Lap($"layRoads ({roads.Count})");
                land = null; mask = null; roads = null; streets = null; towns = null;
                var plan = Worldplan.makePlan(n); Lap($"makePlan whole ({plan.lots.Count} lots)");
                plan = null;
            state:
                GC.Collect();
                m0 = Mb();
                t.Restart();
                if (Env("STATE") != "0")
                {
                    var state = States.createState(new NewStateOptions { seed = 42, width = size, height = size }); Lap("createState (the grid)");
                    state = null;
                }
                GC.Collect();
                m0 = Mb();
                t.Restart();
                var world = Worldgen.createWorld(42, size, Difficulty.NORMAL);
                Lap($"createWorld ({Worldplan.regions(42, size, Difficulty.NORMAL.map).Planned} regions planned)");
                if (Env("ALL") == "1")
                {
                    for (int y = 0; y < size; y += Worldgen.CHUNK)
                        for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(world, new Point(x, y));
                    Lap("every chunk");
                }
                world = null;
            }
            catch (OutOfMemoryException) { times.Add("OUT OF MEMORY"); }
            out_.Add($"size {size}: " + string.Join(", ", times));
            File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile-big.txt"), string.Join("\n", out_) + "\n");
            GC.Collect();
        }
    }

    /// <summary>Save sizes and times: the chunked save (SavefileChunks) against the whole one (what a state without a
    /// world gets), for worlds generated round the start only (as a game is) and in full.</summary>
    [Fact(DisplayName = "gen profile saves")]
    public void ProfileSaves()
    {
        var out_ = new List<string>();
        foreach (var spec in (Env("SAVES") ?? "512:all,2048:start,2048:all,4096:start").Split(','))
        {
            var p = spec.Split(':');
            int size = int.Parse(p[0]);
            bool all = p[1] == "all";
            var s = Worldgen.createWorld(42, size, Difficulty.NORMAL);
            if (all)
                for (int y = 0; y < size; y += Worldgen.CHUNK)
                    for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(s, new Point(x, y));
            for (int i = 0; i < 50; i++) Tick.tick(s);
            var sw = Stopwatch.StartNew();
            string chunked = Savefile.toSaveFile(s);
            double saveMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var back = Savefile.loadFile(chunked);
            double loadMs = sw.Elapsed.TotalMilliseconds;
            string whole = "";
            double wholeMs = 0;
            if (Env("WHOLE") != "0" && size <= 4096)
            {
                var w = s.world;
                s.world = null;
                sw.Restart();
                whole = Savefile.toSaveFile(s);
                wholeMs = sw.Elapsed.TotalMilliseconds;
                s.world = w;
            }
            out_.Add($"{size} {(all ? "every chunk" : "the start only")}: chunked save {chunked.Length / 1e6:0.00} MB in {saveMs / 1000:0.0} s, loaded in {loadMs / 1000:0.0} s ({(back.ok ? "ok" : back.error)}); whole save {(whole.Length > 0 ? $"{whole.Length / 1e6:0.00} MB in {wholeMs / 1000:0.0} s" : "not made")}; furniture {s.furniture.Count:N0}, dormant {s.dormant.Count:N0}");
            File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile-saves.txt"), string.Join("\n", out_) + "\n");
        }
    }

    [Fact(DisplayName = "gen profile roads")]
    public void ProfileRoads()
    {
        var out_ = new List<string>();
        foreach (var size in (Env("SIZES") ?? "1024,2048").Split(',').Select(int.Parse))
        {
            var n = NatureMod.natureFor(42, size, "mainland");
            var towns = Towns.siteTowns(n, Towns.landGrid(n));
            var sw = Stopwatch.StartNew();
            List<Point> nodes = [.. towns.Select(t => new Point { x = t.x, y = t.y }), new Point { x = n.start.x, y = n.start.y + 8 }];
            // The links as layRoads makes them: each end at the town's nearest main-street end, plus roads to the map's edge.
            var streets = towns.flatMap((tw, i) => Towns.growStreets(n, tw, i));
            List<Point> Ends(int ti) => streets.Where(s => s.town == ti && s.main).SelectMany(s => s.eastWest
                ? new[] { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x1), y = Js.I(s.y0) } }
                : new[] { new Point { x = Js.I(s.x0), y = Js.I(s.y0) }, new Point { x = Js.I(s.x0), y = Js.I(s.y1) } }).ToList();
            Point NearestEnd(int node, Point toward)
            {
                if (node >= towns.Count) return nodes[node];
                var options = Ends(node);
                if (options.Count == 0) return nodes[node];
                return options.Aggregate((a, b) => Worldplan.dist(b.x, b.y, toward.x, toward.y) < Worldplan.dist(a.x, a.y, toward.x, toward.y) ? b : a);
            }
            var pairs = Roads.network(n.seed, nodes).Select(l => (NearestEnd((int)l[0], nodes[(int)l[1]]), NearestEnd((int)l[1], nodes[(int)l[0]]), "town")).ToList();
            for (int i = 0; i < towns.Count; i++)
            {
                var t = towns[i];
                if (NatureMod.roll(n.seed, t.x, t.y, 0x7301) > 0.45) continue;
                var edges = new[] { new Point { x = 0, y = t.y }, new Point { x = size - 1, y = t.y }, new Point { x = t.x, y = 0 }, new Point { x = t.x, y = size - 1 } };
                var edge = edges.Aggregate((a, b) => Worldplan.dist(b.x, b.y, t.x, t.y) < Worldplan.dist(a.x, a.y, t.x, t.y) ? b : a);
                pairs.Add((NearestEnd(i, edge), edge, "edge"));
            }
            double networkMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var cost = Roads.costGrid(n, towns);
            double costMs = sw.Elapsed.TotalMilliseconds;
            var each = new List<(double ms, double dist, bool ok, string kind, Point a, Point b)>();
            var mask = new Uint8Array((double)size * size);
            foreach (var (a, b, kind) in pairs)
            {
                if (Env("DIAG") is { } diag && $"{a.x},{a.y}" == diag)
                {
                    var (dp, dpo, de, dm, dh) = Counted(n, cost, a, b, asDouble: true);
                    out_.Add($"   DIAG with g in doubles: pushes {dp:N0}, pops {dpo:N0}, expansions {de:N0}, most of one cell {dm:N0}, heap peak {dh:N0}");
                    var (pushes, pops, expanded, maxRe, heapMax) = Counted(n, cost, a, b);
                    out_.Add($"   DIAG route ({a.x},{a.y})->({b.x},{b.y}): pushes {pushes:N0}, pops {pops:N0}, expansions {expanded:N0} over {(double)size / 4 * size / 4:N0} cells, most expansions of one cell {maxRe:N0}, heap peak {heapMax:N0}");
                    File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile-roads.txt"), string.Join("\n", out_) + "\n");
                    return;
                }
                sw.Restart();
                var line = Roads.route(n, cost, a, b);
                each.Add((sw.Elapsed.TotalMilliseconds, Math.Sqrt((double)(a.x - b.x) * (a.x - b.x) + (double)(a.y - b.y) * (a.y - b.y)), line != null, kind, a, b));
                if (line != null) { Roads.reuse(cost, size, line); Roads.drawLine(mask, size, line, 1); }
            }
            each.Sort((p, q) => q.ms.CompareTo(p.ms));
            out_.Add($"size {size}: {towns.Count} towns, {pairs.Count} links ({pairs.Count(p => p.Item3 == "edge")} to the edge); network {networkMs:0} ms, costGrid {costMs:0} ms, routes {each.Sum(e => e.ms) / 1000:0.0} s (mean {each.Average(e => e.ms):0} ms; failed {each.Count(e => !e.ok)})");
            out_.Add("   slowest routes (ms, length in tiles, found, kind, from -> to): " + string.Join("; ", each.Take(8).Select(e => $"{e.ms:0} {e.dist:0} {(e.ok ? "y" : "n")} {e.kind} ({e.a.x},{e.a.y})->({e.b.x},{e.b.y})")));
            out_.Add($"   by kind: town {each.Where(e => e.kind == "town").Sum(e => e.ms) / 1000:0.0} s, edge {each.Where(e => e.kind == "edge").Sum(e => e.ms) / 1000:0.0} s");
            out_.Add("   fastest: " + string.Join("; ", each.TakeLast(4).Select(e => $"{e.ms:0.0} {e.dist:0}")));
            File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/gen-profile-roads.txt"), string.Join("\n", out_) + "\n");
        }
    }

    /// <summary>Roads.route's search (a copy, in plain C#, same numbers: g in float32 as the Float32Array keeps it), counting
    /// its work: heap pushes and pops, cells expanded, the most times one cell was expanded, the heap's peak.</summary>
    static (long pushes, long pops, long expanded, long maxRe, long heapMax) Counted(Nature n, Float32Array cost, Point from, Point to, bool asDouble = false)
    {
        int N = (int)Math.Ceiling(n.size / (double)Roads.ROAD_CELL);
        int Cell(Point p) => Math.Min(N - 1, Math.Max(0, p.y / Roads.ROAD_CELL)) * N + Math.Min(N - 1, Math.Max(0, p.x / Roads.ROAD_CELL));
        int start = Cell(from), goal = Cell(to), gx = goal % N, gy = goal / N;
        var g = new double[N * N];
        Array.Fill(g, double.PositiveInfinity);
        var times = new long[N * N];
        var heap = new PriorityQueue<int, double>();
        long pushes = 0, pops = 0, expanded = 0, heapMax = 0;
        const double MIN = 0.35;
        g[start] = 0;
        heap.Enqueue(start, 0); pushes++;
        while (heap.TryDequeue(out int c, out double f))
        {
            pops++;
            if (c == goal) break;
            int cx = c % N, cy = c / N;
            if (f > g[c] + MIN * Math.Sqrt((double)(gx - cx) * (gx - cx) + (double)(gy - cy) * (gy - cy)) + 1e-6) continue;
            expanded++;
            times[c]++;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                    int nc = ny * N + nx;
                    double step = (cost[c] + cost[nc]) / 2 * (dx != 0 && dy != 0 ? Math.Sqrt(2) : 1);
                    if (!double.IsFinite(step)) continue;
                    double ng = g[c] + step;
                    if (ng < g[nc])
                    {
                        g[nc] = asDouble ? ng : (float)ng;
                        heap.Enqueue(nc, ng + MIN * Math.Sqrt((double)(gx - nx) * (gx - nx) + (double)(gy - ny) * (gy - ny))); pushes++;
                        heapMax = Math.Max(heapMax, heap.Count);
                    }
                }
            if (pops > 200_000_000) break;
        }
        return (pushes, pops, expanded, times.Max(), heapMax);
    }

    /// <summary>A hash of each world plan's roads and lots for seeds 1-30 at a few sizes, to see which worlds a change to
    /// planning moves (run before and after, compare the files).</summary>
    [Fact(DisplayName = "gen plan hashes")]
    public void PlanHashes()
    {
        var out_ = new List<string>();
        foreach (var size in (Env("SIZES") ?? "128,256,512").Split(',').Select(int.Parse))
            for (int seed = 1; seed <= 30; seed++)
            {
                var p = Worldplan.makePlan(NatureMod.natureFor(seed, size, "mainland"));
                ulong h = 1469598103934665603;
                foreach (var v in p.roadMask) h = (h ^ (ulong)v) * 1099511628211;
                foreach (var l in p.lots) h = (h ^ (ulong)(l.x * 73856093 ^ l.y * 19349663 ^ l.w * 83492791 ^ l.h)) * 1099511628211;
                // The ground's heights (Heights: not in the save), every 3rd corner, exactly.
                ulong hh = 1469598103934665603;
                if (size <= 512)
                {
                    var f = Heights.make(seed, size, "mainland");
                    for (int y = 0; y <= size; y += 3)
                        for (int x = 0; x <= size; x += 3) hh = (hh ^ (ulong)BitConverter.DoubleToInt64Bits(f.corner(x, y))) * 1099511628211;
                }
                out_.Add($"{size} {seed} {h:x16} lots {p.lots.Count} heights {hh:x16}");
            }
        // Whole generated worlds: the grid, furniture, the dormant, items, survivors, a few seeds.
        foreach (var size in (Env("WORLD_SIZES") ?? "256,512").Split(',').Select(int.Parse))
            for (int seed = 1; seed <= 6; seed++)
            {
                var s = Worldgen.createWorld(seed, size, Difficulty.NORMAL);
                for (int y = 0; y < size; y += Worldgen.CHUNK)
                    for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(s, new Point(x, y));
                // The whole state as a save file: everything generation made.
                ulong h = 1469598103934665603;
                foreach (char c in Savefile.toSaveFile(s)) h = (h ^ c) * 1099511628211;
                out_.Add($"world {size} {seed} {h:x16} furniture {s.furniture.Count} dormant {s.dormant.Count}");
            }
        File.WriteAllText(Path.Combine(Node.Root, Env("OUT") ?? "cs/Harness/out/plan-hashes.txt"), string.Join("\n", out_) + "\n");
    }

    // generateChunk's first loop (terrain), as Worldgen has it.
    static void Terrain(SimState state, Layout l, int cx, int cy)
    {
        double x0 = (double)cx * Worldgen.CHUNK, y0 = (double)cy * Worldgen.CHUNK;
        for (var y = y0; y < y0 + Worldgen.CHUNK; y++)
            for (var x = x0; x < x0 + Worldgen.CHUNK; x++)
            {
                if (!Grids.inBounds(state.grid, x, y)) continue;
                var fence = Worldgen.fenceAt(l, x, y);
                if (Js.T(fence)) Grids.setTile(state.grid, x, y, fence);
                else if (NatureMod.isSea(l, x, y) && !Worldgen.isRoad(l, x, y)) Grids.setTile(state.grid, x, y, "water");
                else if (Worldgen.isRoad(l, x, y)) Worldgen.ground(state, x, y, NatureMod.isWater(l, x, y) ? "bridge" : "road");
                else if (NatureMod.isWater(l, x, y)) Grids.setTile(state.grid, x, y, "water");
                else if (Worldgen.onLot(l, x, y)) continue;
                else if (NatureMod.isForest(l, x, y)) Worldgen.ground(state, x, y, "forest");
                else if (NatureMod.noise(l.seed, x, y, 6, 11) > 0.72) Worldgen.ground(state, x, y, "dirt");
            }
    }

    // generateChunk's third loop (trees), as Worldgen has it.
    static void Trees(SimState state, Layout l, double x0, double y0)
    {
        for (var y = y0; y < y0 + Worldgen.CHUNK; y++)
            for (var x = x0; x < x0 + Worldgen.CHUNK; x++)
            {
                if (!Grids.inBounds(state.grid, x, y) || Worldgen.isRoad(l, x, y) || Worldgen.onLot(l, x, y) || NatureMod.nearStart(l, x, y, NatureMod.START_CLEAR_RADIUS)) continue;
                var i = Grids.tileIndex(state.grid, x, y);
                if (Js.Get(state.grid.tiles, i) == "floor" && Js.Get(state.grid.ground, i) == Worldgen.FOREST && NatureMod.roll(l.seed, x, y, 12) < 0.3)
                    FurnitureMod.addFurniture(state, "tree", x, y);
            }
    }
}
