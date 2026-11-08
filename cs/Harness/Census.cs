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
}
