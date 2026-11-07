// The benchmark and the stress test (harness/bench.harness.ts, stress.harness.ts, stressScenarios.ts) in C#, by hand:
// what a tick costs as the world fills up, and where the sim breaks at scale. Same scenarios, same output lines, so
// the two languages' numbers sit side by side (harness/out/cs-bench.txt, cs-stress.txt). The V8 CPU profiles of the
// TypeScript versions have no in-process equivalent here: profile the C# with dotnet-trace instead.
// Env: ONLY=<name> one stress scenario (or the zombie-heavy bench case, for longer); TICKS per stress scenario (300).

using System.Diagnostics;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Harness;

public static class Scenarios
{
    public static void generateAll(SimState state)
    {
        int n = state.grid.width;
        for (int y = 0; y < n; y += Worldgen.CHUNK)
            for (int x = 0; x < n; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
    }

    /// <summary>A walkable tile near (x, y), or null.</summary>
    static Point? near(SimState s, double x, double y) =>
        Space.nearestTile(s.grid, new Point((int)Math.Max(0, Math.Min(s.grid.width - 1, JsMath.round(x))), (int)Math.Max(0, Math.Min(s.grid.height - 1, JsMath.round(y)))), (a, b) => Grids.isWalkable(s.grid, a, b), 4000);

    static void colonists(SimState s, Point at, int n)
    {
        for (int i = 0; i < n; i++)
            if (near(s, at.x + i % 12 - 6, at.y + i / 12 - 4) is { } p) Survivors.addSurvivor(s, p.x, p.y);
    }

    static void zombiesRound(SimState s, Point at, int n, double r)
    {
        for (int i = 0; i < n; i++)
        {
            double a = (double)i / n * Math.PI * 2;
            if (near(s, at.x + Math.Cos(a) * r, at.y + Math.Sin(a) * r) is { } p) Zombies.addZombie(s, p.x, p.y, 200, i % 2 == 0 ? "herd" : "static");
        }
    }

    static void zombiesEverywhere(SimState s, int n)
    {
        int size = s.grid.width;
        for (int i = 0; i < n; i++)
            if (near(s, Hash.hashRoll(i, 1) * size, Hash.hashRoll(i, 2) * size) is { } p) Zombies.addZombie(s, p.x, p.y, 200, i % 3 == 0 ? "herd" : "static");
    }

    static void herds(SimState s, int n)
    {
        int size = s.grid.width;
        for (int i = 0; i < n; i++)
            s.herds.Add(new Herd { id = $"h{s.nextId++}", x = (int)Math.Floor(Hash.hashRoll(i, 3) * size), y = 0, to = new Point((int)Math.Floor(Hash.hashRoll(i, 4) * size), size - 1), size = 30, nextStepTick = s.tick, heard = false });
    }

    static void caravans(SimState s, int n)
    {
        var ss = s.settlements;
        if (ss.Count < 2) return;
        for (int i = 0; i < n; i++)
        {
            var a = ss[i % ss.Count];
            var b = ss[(i + 1) % ss.Count];
            Caravans.sendCaravan(s, a.id, b.id, new Point(b.x, b.y), 5);
        }
    }

    public static readonly (string name, Func<SimState> build)[] STRESS =
    {
        ("gen-512", () => { var s = Worldgen.createWorld(7, 512); generateAll(s); return s; }),
        ("gen-1024", () => { var s = Worldgen.createWorld(7, 1024); generateAll(s); return s; }),
        ("colony-512", () =>
        {
            var s = Worldgen.createWorld(7, 512);
            var home = s.survivors[0].pos;
            colonists(s, home, 100);
            zombiesRound(s, home, 500, 30);
            return s;
        }),
        ("horde-512", () =>
        {
            var s = Worldgen.createWorld(7, 512);
            generateAll(s);
            colonists(s, s.survivors[0].pos, 30);
            zombiesEverywhere(s, 2000);
            herds(s, 6);
            caravans(s, 20);
            return s;
        }),
        ("spread-1024", () =>
        {
            var s = Worldgen.createWorld(7, 1024);
            foreach (var c in new[] { new Point(100, 100), new Point(920, 100), new Point(100, 920), new Point(920, 920) })
            {
                Worldgen.ensureGeneratedAround(s, c);
                colonists(s, c, 10);
            }
            zombiesEverywhere(s, 1000);
            return s;
        }),
        ("city-1024", () =>
        {
            var s = Worldgen.createWorld(7, 1024);
            generateAll(s);
            var home = s.survivors[0].pos;
            colonists(s, home, 200);
            zombiesRound(s, home, 700, 40);
            zombiesEverywhere(s, 800);
            herds(s, 10);
            caravans(s, 30);
            return s;
        }),
    };
}

public class Bench
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
    static string OutPath(string name) => Path.Combine(Node.Root, Env("OUT") ?? $"harness/out/cs-{name}.txt");
    static double Mb() => GC.GetTotalMemory(true) / 1e6;

    static string Line(string size, SimState state, List<double> times)
    {
        times.Sort();
        double mean = times.Average(), p99 = times[(int)(times.Count * 0.99)], max = times[^1];
        string json = StateJson.Write(state);
        return $"size {size} colonists {state.survivors.Count,3} zombies {state.zombies.Count,3} | mean {mean:0.000} ms  p99 {p99:0.00}  max {max:0.00} | ticks/s at 1 core {1000 / mean:0} | save {json.Length / 1024} KB items {state.items.Count} furniture {state.furniture.Count}";
    }

    static string bench(int size, int colonists, int zombies, int ticks)
    {
        var state = Worldgen.createWorld(7, size);
        var start = state.survivors[0].pos;
        for (int i = state.survivors.Count; i < colonists; i++)
        {
            var p = Space.nearestTile(state.grid, new Point(start.x + i % 10 - 5, start.y + i / 10), (x, y) => Grids.isWalkable(state.grid, x, y))!;
            Survivors.addSurvivor(state, p.x, p.y);
        }
        for (int i = 0; i < zombies; i++)
        {
            double a = (double)i / zombies * Math.PI * 2;
            var p = Space.nearestTile(state.grid, new Point((int)JsMath.round(start.x + Math.Cos(a) * 25), (int)JsMath.round(start.y + Math.Sin(a) * 25)), (x, y) => Grids.isWalkable(state.grid, x, y));
            if (p != null) Zombies.addZombie(state, p.x, p.y, 200, "herd");
        }
        for (int t = 0; t < 60; t++) Tick.tick(state); // warm up
        var times = new List<double>();
        var sw = new Stopwatch();
        for (int t = 0; t < ticks; t++)
        {
            sw.Restart();
            Tick.tick(state);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        return Line($"{size}", state, times);
    }

    [Fact(DisplayName = "bench")]
    public void Run()
    {
        var out_ = new List<string>();
        bool only = Env("ONLY") != null;
        // The JIT compiles as it goes (tiered: quick code first, optimised after many calls): one round thrown away first,
        // so the figures are the steady state, as V8's are after its own warm-up.
        bench(128, 20, 50, 1500);
        foreach (var (size, c, z) in only ? new[] { (256, 3, 200) } : new[] { (128, 3, 0), (128, 20, 0), (128, 50, 0), (128, 100, 0), (128, 3, 50), (128, 3, 200), (128, 50, 200), (256, 3, 0), (256, 50, 200), (512, 3, 0) })
            out_.Add(bench(size, c, z, only ? 3000 : 600));
        if (!only)
        {
            // A long game: save size and memory after 10 days.
            var state = Worldgen.createWorld(7, 128);
            double before = Mb();
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < 10 * Time.DAY_LENGTH; t++) Tick.tick(state);
            double ms = sw.Elapsed.TotalMilliseconds;
            string json = StateJson.Write(state);
            out_.Add($"10 days on 128: {ms / 1000:0.0} s ({ms / (10 * Time.DAY_LENGTH):0.000} ms/tick), save {json.Length / 1024} KB, events {state.events.Count}, commandLog {state.commandLog.Count}, managed heap +{Mb() - before:0.0} MB");
        }
        File.WriteAllText(OutPath("bench"), string.Join("\n", out_) + "\n");
    }

    [Fact(DisplayName = "stress")]
    public void Stress()
    {
        int ticks = int.TryParse(Env("TICKS"), out var n) ? n : 300;
        var out_ = new List<string>();
        foreach (var (name, build) in Scenarios.STRESS.Where((s) => Env("ONLY") == null || s.name == Env("ONLY")))
        {
            double h0 = Mb();
            var sw = Stopwatch.StartNew();
            var state = build();
            double genMs = sw.Elapsed.TotalMilliseconds;
            // As the game does on a new or loaded game, while paused: every mover's reachability built up front.
            sw.Restart();
            Pathfinding.warmReach(state.grid, state.walkabilityVersion);
            double warmMs = sw.Elapsed.TotalMilliseconds;
            double h1 = Mb();
            for (int t = 0; t < 20; t++) Tick.tick(state); // warm up
            var times = new List<double>();
            for (int t = 0; t < ticks; t++)
            {
                sw.Restart();
                Tick.tick(state);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            double mean = times.Average();
            sw.Restart();
            string save = Savefile.toSaveFile(state);
            double saveMs = sw.Elapsed.TotalMilliseconds;
            double h2 = Mb();
            out_.Add($"{name,-12} size {state.grid.width} | colonists {state.survivors.Count} zombies {state.zombies.Count} herds {state.herds.Count} caravans {state.caravans?.Count ?? 0} furniture {state.furniture.Count} items {state.items.Count}");
            out_.Add($"   build {genMs / 1000:0.0} s, warm-up {warmMs:0} ms | tick mean {mean:0.00} ms  p99 {times[(int)(times.Count * 0.99)]:0.0}  max {times[^1]:0.0} | {1000 / mean:0} ticks/s (10 needed at 1x, 40 at 4x)");
            out_.Add($"   managed heap {h0:0} -> {h1:0} after build -> {h2:0} MB after run | save {save.Length / 1e6:0.0} MB in {saveMs:0} ms");
            File.WriteAllText(OutPath("stress"), string.Join("\n", out_) + "\n");
        }
    }
}
