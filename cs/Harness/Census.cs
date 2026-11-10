// The dead in a fresh world: how many, how near the start, which tier. Not part of the check.
// dotnet test cs/Harness -c Release --filter "DisplayName=census" (SEEDS, PRESET; report in cs/Harness/out/census.txt)
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Harness.Node;
namespace LoonieRoo.Harness;

public class Census
{
    [Fact(DisplayName = "census")]
    public void T001_census()
    {
        mkdirSync("cs/Harness/out", new() { recursive = true });
        var preset = process.env.PRESET ?? "apocalypse";
        var settings = Js.Get(Difficulty.PRESETS, preset).settings;
        List<string> lines = [$"census ({preset}): dormant by Chebyshev distance from the start, awake at tick 0"];
        foreach (var seed in Js.Iter((process.env.SEEDS ?? "1,2,3,4,5,6,7,8").split(",").map((v) => Js.Num(v))))
        {
            var state = Worldgen.createWorld(seed, settings: settings);
            var s = state.survivors[0].pos;
            double d(double x, double y) => JsMath.max(JsMath.abs(x - s.x), JsMath.abs(y - s.y));
            int within(double r) => state.dormant.Count((z) => d(z.x, z.y) <= r);
            var awake = state.zombies.Count;
            var awakeNear = state.zombies.Count((z) => d(z.pos.x, z.pos.y) <= 30);
            var nearest = state.zombies.Select((z) => d(z.pos.x, z.pos.y)).Concat(state.dormant.Select((z) => d(z.x, z.y))).DefaultIfEmpty(-1).Min();
            lines.Add($"seed {seed}: dormant {state.dormant.Count} (<=20 {within(20)}, <=30 {within(30)}, <=40 {within(40)}, <=60 {within(60)}) awake {awake} (<=30 {awakeNear}) nearest {nearest} herds {state.herds.Count}; start {s.x},{s.y} awake at {string.Join(" ", state.zombies.Select((z) => $"{z.pos.x},{z.pos.y}"))}");
        }
        writeFileSync("cs/Harness/out/census.txt", lines.join("\n") + "\n");
    }

    /// <summary>What a weapon's noise wakes: for every tenth of the sleeping dead as a place to fire from (where the dead lie), how many sleepers lie within the radius (Manhattan); and from the start. dotnet test cs/Harness -c Release --filter "DisplayName=census noise" (SEEDS, PRESET; cs/Harness/out/census-noise.txt)</summary>
    [Fact(DisplayName = "census noise")]
    public void T002_census_noise()
    {
        mkdirSync("cs/Harness/out", new() { recursive = true });
        var preset = process.env.PRESET ?? "apocalypse";
        var settings = Js.Get(Difficulty.PRESETS, preset).settings;
        var radii = new (string, int)[] { ("crossbow", 4), ("a blow (crowbar)", 7), ("pistol", 22), ("revolver", 26), ("shotgun", 32), ("rifle", 36) };
        List<string> lines = [$"census noise ({preset}): sleepers woken by one report, fired from where the dead lie (every 10th sleeper as a spot: mean / median / 90th percentile / max) and from the start"];
        var acc = radii.ToDictionary((r) => r.Item1, (r) => new List<double>());
        var atStart = radii.ToDictionary((r) => r.Item1, (r) => new List<double>());
        foreach (var seed in Js.Iter((process.env.SEEDS ?? "1,2,3,4,5,6,7,8").split(",").map((v) => Js.Num(v))))
        {
            var state = Worldgen.createWorld(seed, settings: settings);
            // The whole world, generated (the dead are made by region as you go).
            for (var gy = 0; gy < state.grid.height; gy += Worldgen.CHUNK)
                for (var gx = 0; gx < state.grid.width; gx += Worldgen.CHUNK)
                    Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(gx), y = Js.I(gy) });
            var s = state.survivors[0].pos;
            var all = state.dormant.ToList();
            for (var k = 0; k < all.Count; k += 10)
                foreach (var (name, r) in radii)
                    acc[name].Add(all.Count((z) => JsMath.abs(z.x - all[k].x) + JsMath.abs(z.y - all[k].y) <= r));
            foreach (var (name, r) in radii)
                atStart[name].Add(all.Count((z) => JsMath.abs(z.x - s.x) + JsMath.abs(z.y - s.y) <= r));
        }
        foreach (var (name, r) in radii)
        {
            var v = acc[name].OrderBy((x) => x).ToList();
            var n = v.Count;
            lines.Add($"{name,-18} radius {r,2}: {(n == 0 ? 0 : v.Average()):0.0} / {(n == 0 ? 0 : v[n / 2])} / {(n == 0 ? 0 : v[(int)(n * 0.9)])} / {(n == 0 ? 0 : v[n - 1])}   from the start: mean {(atStart[name].Count == 0 ? 0 : atStart[name].Average()):0.0}, max {(atStart[name].Count == 0 ? 0 : atStart[name].Max())}");
        }
        writeFileSync("cs/Harness/out/census-noise.txt", lines.join("\n") + "\n");
    }
}
