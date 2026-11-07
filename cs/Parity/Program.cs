// Checks the C# sim against the TypeScript one. The TypeScript side writes reference outputs
// (npm run harness -- parity -> cs/Parity/data); this recomputes them in C# and reports any difference.
// Run: dotnet run --project cs/Parity [-- <data dir>]   Exit code 1 on any mismatch.
using System.Text.Json;
using LoonieRoo.Sim;

string dir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data");
int failures = 0;
int checks = 0;

void Check(string what, bool ok, string detail)
{
    checks++;
    if (ok) return;
    failures++;
    if (failures <= 30) Console.WriteLine($"MISMATCH {what}: {detail}");
}

static bool Same(double a, double b) => a == b || (double.IsNaN(a) && double.IsNaN(b));

var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "vectors.json"))).RootElement;

foreach (var h in vectors.GetProperty("hashes").EnumerateArray())
{
    var nums = h.GetProperty("args").EnumerateArray().Select(a => a.GetDouble()).ToArray();
    double want = h.GetProperty("value").GetDouble();
    double got = Hash.hashRoll(nums);
    Check("hashRoll", Same(got, want), $"[{string.Join(", ", nums)}] want {want:R} got {got:R}");
}
foreach (var h in vectors.GetProperty("ids").EnumerateArray())
{
    double want = h.GetProperty("value").GetDouble();
    double got = Hash.idNumber(h.GetProperty("id").GetString()!);
    Check("idNumber", Same(got, want), $"{h.GetProperty("id")} want {want:R} got {got:R}");
}
{
    var rng = vectors.GetProperty("rng").EnumerateArray().Select(a => a.GetDouble()).ToArray();
    uint state = Rng.seedRng(42);
    for (int k = 0; k < rng.Length / 2; k++)
    {
        var (value, next) = Rng.mulberry32(state);
        Check("mulberry32", Same(value, rng[2 * k]) && next == rng[2 * k + 1], $"step {k} want {rng[2 * k]:R}/{rng[2 * k + 1]} got {value:R}/{next}");
        state = next;
    }
}
foreach (var s in vectors.GetProperty("seeds").EnumerateArray())
{
    double want = s.GetProperty("value").GetDouble();
    uint got = Rng.seedRng(s.GetProperty("s").GetDouble());
    Check("seedRng", got == want, $"{s.GetProperty("s")} want {want} got {got}");
}
foreach (var c in vectors.GetProperty("clock").EnumerateArray())
{
    int scale = c.GetProperty("scale").GetInt32();
    int t = c.GetProperty("t").GetInt32();
    Time.setDayScale(scale);
    string at = $"scale {scale} tick {t}";
    Check("tickOfDay", Time.tickOfDay(t) == c.GetProperty("tickOfDay").GetInt32(), at);
    Check("dayOf", Time.dayOf(t) == c.GetProperty("dayOf").GetInt32(), at);
    Check("hourOf", Same(Time.hourOf(t), c.GetProperty("hourOf").GetDouble()), at);
    Check("isNight", Time.isNight(t) == c.GetProperty("isNight").GetBoolean(), at);
    var night = c.GetProperty("night");
    Check("nightNumber", Time.nightNumber(t) == (night.ValueKind == JsonValueKind.Null ? null : night.GetInt32()), at);
    var into = c.GetProperty("into");
    if (into.ValueKind != JsonValueKind.Null) Check("ticksIntoNight", Time.ticksIntoNight(t) == into.GetInt32(), at);
    Check("darkness", Same(Time.darkness(t), c.GetProperty("dark").GetDouble()), at);
    Check("clockText", Time.clockText(t) == c.GetProperty("text").GetString(), $"{at}: want {c.GetProperty("text")} got {Time.clockText(t)}");
}
Time.setDayScale(1);

// Pathfinding on a generated world: the same answers to the same queries.
{
    var opts = new JsonSerializerOptions { IncludeFields = true };
    var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "paths.json"))).RootElement;
    var grid = doc.GetProperty("grid").Deserialize<Grid>(opts)!;
    int version = doc.GetProperty("version").GetInt32();
    Point P(JsonElement e) => new(e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32());
    string Flat(List<Point>? path) => path == null ? "null" : string.Join(",", path.Select(q => $"{q.x},{q.y}"));
    string FlatJson(JsonElement e) => e.ValueKind == JsonValueKind.Null ? "null" : string.Join(",", e.EnumerateArray().Select(v => v.GetInt32()));
    int k = 0;
    foreach (var q in doc.GetProperty("queries").EnumerateArray())
    {
        string mover = q.GetProperty("mover").GetString()!;
        var from = P(q.GetProperty("from"));
        var goals = q.GetProperty("goals").EnumerateArray().Select(P).ToList();
        string at = $"query {k++} {mover} from {from}";
        var path = Pathfinding.findPathToAny(grid, from, goals, mover);
        Check("findPathToAny", Flat(path) == FlatJson(q.GetProperty("path")), at);
        Check("canReach", Pathfinding.canReach(grid, version, from, goals, mover) == q.GetProperty("reach").GetBoolean(), at);
        if (path != null && mover == "survivor") Check("straighten", Flat(Pathfinding.straighten(grid, from, path)) == FlatJson(q.GetProperty("straight")), at);
        var b = P(q.GetProperty("b"));
        Check("openBetween", Pathfinding.openBetween(grid, from, b) == q.GetProperty("open").GetBoolean(), at);
        Check("walksAcross", Pathfinding.walksAcross(grid, from, b, mover) == q.GetProperty("walks").GetBoolean(), at);
        var near = Space.nearestTile(grid, from, (x, y) => grid.tiles[y * 256 + x] == "door", 2000);
        var nearWant = q.GetProperty("near");
        Check("nearestTile", near == null ? nearWant.ValueKind == JsonValueKind.Null : nearWant.ValueKind != JsonValueKind.Null && near.x == nearWant.GetProperty("x").GetInt32() && near.y == nearWant.GetProperty("y").GetInt32(), at);
    }
}

// Whole states: read into the C# types and written back, the same JSON.
foreach (var file in Directory.GetFiles(Path.Combine(dir, "states"), "*.json"))
{
    string json = File.ReadAllText(file);
    SimState state;
    try { state = StateJson.Read(json); }
    catch (Exception e) { Check("state read", false, $"{Path.GetFileName(file)}: {e.Message}"); continue; }
    var diffs = StateJson.Diff(System.Text.Json.Nodes.JsonNode.Parse(json), System.Text.Json.Nodes.JsonNode.Parse(StateJson.Write(state)));
    Check("state round trip", diffs.Count == 0, $"{Path.GetFileName(file)}:\n  " + string.Join("\n  ", diffs));
}

// Worlds made from nothing: the same seed, size and settings must generate the same world.
var worldsDir = Path.Combine(dir, "worlds");
if (File.Exists(Path.Combine(worldsDir, "index.json")))
{
    foreach (var c in System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(worldsDir, "index.json")))!.AsArray())
    {
        string name = (string)c!["name"]!;
        double seed = (double)c["seed"]!, size = (double)c["size"]!;
        string? preset = (string?)c["preset"];
        var world = preset == null ? Worldgen.createWorld(seed, size) : Worldgen.createWorld(seed, size, Difficulty.PRESETS[preset].settings);
        var want = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(worldsDir, name + ".json")));
        var diffs = StateJson.Diff(want, System.Text.Json.Nodes.JsonNode.Parse(StateJson.Write(world)));
        Check("world", diffs.Count == 0, $"{name}:\n  " + string.Join("\n  ", diffs));
        // The C#'s world beside the TypeScript's, to look into.
        if (diffs.Count > 0) File.WriteAllText(Path.Combine(worldsDir, name + ".cs.json"), StateJson.Write(world));
    }
}

// Whole runs: from the TypeScript's start state, tick to each checkpoint and compare the whole state.
// Stops a run at its first differing checkpoint (later ones only repeat the difference).
var runsDir = Path.Combine(dir, "runs");
if (Directory.Exists(runsDir))
{
    foreach (var run in Directory.GetDirectories(runsDir).OrderBy(d => d, StringComparer.Ordinal))
    {
        string name = Path.GetFileName(run);
        SimState state;
        try { state = StateJson.Read(File.ReadAllText(Path.Combine(run, "start.json"))); }
        catch (Exception e) { Check("run start", false, $"{name}: {e.Message}"); continue; }
        int done = 0;
        var checkpoints = JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine(run, "checkpoints.json")))!;
        // A command run: the TypeScript's command log, each command applied once the clock reaches its tick
        // (after that tick ran; tick 0's before the first), in the order logged.
        var commandsPath = Path.Combine(run, "commands.json");
        var log = File.Exists(commandsPath) ? JsonSerializer.Deserialize<List<LoggedCommand>>(File.ReadAllText(commandsPath), StateJson.Options)! : new List<LoggedCommand>();
        int next = 0;
        void ApplyDue()
        {
            for (; next < log.Count && log[next].tick <= state.tick; next++) Commands.applyCommand(state, log[next].command);
        }
        ApplyDue();
        foreach (int at in checkpoints)
        {
            try
            {
                for (; done < at; done++)
                {
                    Tick.tick(state);
                    ApplyDue();
                }
            }
            catch (Exception e)
            {
                Check("run tick", false, $"{name}: tick {state.tick} (step {done + 1}) threw {e.GetType().Name}: {e.Message}\n{e.StackTrace?.Split('\n').Take(8).Aggregate((a, b) => a + "\n" + b)}");
                break;
            }
            var want = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(run, $"t{at}.json")));
            var diffs = StateJson.Diff(want, System.Text.Json.Nodes.JsonNode.Parse(StateJson.Write(state)));
            Check("run", diffs.Count == 0, $"{name} after {at} ticks:\n  " + string.Join("\n  ", diffs));
            if (diffs.Count > 0) break;
        }
    }
}
Time.setDayScale(1);

Console.WriteLine(failures == 0 ? $"parity: all {checks} checks match" : $"parity: {failures} of {checks} checks differ");
return failures == 0 ? 0 : 1;
