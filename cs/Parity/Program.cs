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
    double got = Hash.HashRoll(nums);
    Check("hashRoll", Same(got, want), $"[{string.Join(", ", nums)}] want {want:R} got {got:R}");
}
foreach (var h in vectors.GetProperty("ids").EnumerateArray())
{
    double want = h.GetProperty("value").GetDouble();
    double got = Hash.IdNumber(h.GetProperty("id").GetString()!);
    Check("idNumber", Same(got, want), $"{h.GetProperty("id")} want {want:R} got {got:R}");
}
{
    var rng = vectors.GetProperty("rng").EnumerateArray().Select(a => a.GetDouble()).ToArray();
    uint state = Rng.SeedRng(42);
    for (int k = 0; k < rng.Length / 2; k++)
    {
        var (value, next) = Rng.Mulberry32(state);
        Check("mulberry32", Same(value, rng[2 * k]) && next == rng[2 * k + 1], $"step {k} want {rng[2 * k]:R}/{rng[2 * k + 1]} got {value:R}/{next}");
        state = next;
    }
}
foreach (var s in vectors.GetProperty("seeds").EnumerateArray())
{
    double want = s.GetProperty("value").GetDouble();
    uint got = Rng.SeedRng(s.GetProperty("s").GetDouble());
    Check("seedRng", got == want, $"{s.GetProperty("s")} want {want} got {got}");
}
foreach (var c in vectors.GetProperty("clock").EnumerateArray())
{
    int scale = c.GetProperty("scale").GetInt32();
    int t = c.GetProperty("t").GetInt32();
    Time.SetDayScale(scale);
    string at = $"scale {scale} tick {t}";
    Check("tickOfDay", Time.TickOfDay(t) == c.GetProperty("tickOfDay").GetInt32(), at);
    Check("dayOf", Time.DayOf(t) == c.GetProperty("dayOf").GetInt32(), at);
    Check("hourOf", Same(Time.HourOf(t), c.GetProperty("hourOf").GetDouble()), at);
    Check("isNight", Time.IsNight(t) == c.GetProperty("isNight").GetBoolean(), at);
    var night = c.GetProperty("night");
    Check("nightNumber", Time.NightNumber(t) == (night.ValueKind == JsonValueKind.Null ? null : night.GetInt32()), at);
    var into = c.GetProperty("into");
    if (into.ValueKind != JsonValueKind.Null) Check("ticksIntoNight", Time.TicksIntoNight(t) == into.GetInt32(), at);
    Check("darkness", Same(Time.Darkness(t), c.GetProperty("dark").GetDouble()), at);
    Check("clockText", Time.ClockText(t) == c.GetProperty("text").GetString(), $"{at}: want {c.GetProperty("text")} got {Time.ClockText(t)}");
}
Time.SetDayScale(1);

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
        var path = Pathfinding.FindPathToAny(grid, from, goals, mover);
        Check("findPathToAny", Flat(path) == FlatJson(q.GetProperty("path")), at);
        Check("canReach", Pathfinding.CanReach(grid, version, from, goals, mover) == q.GetProperty("reach").GetBoolean(), at);
        if (path != null && mover == "survivor") Check("straighten", Flat(Pathfinding.Straighten(grid, from, path)) == FlatJson(q.GetProperty("straight")), at);
        var b = P(q.GetProperty("b"));
        Check("openBetween", Pathfinding.OpenBetween(grid, from, b) == q.GetProperty("open").GetBoolean(), at);
        Check("walksAcross", Pathfinding.WalksAcross(grid, from, b, mover) == q.GetProperty("walks").GetBoolean(), at);
        var near = Space.NearestTile(grid, from, (x, y) => grid.tiles[y * 256 + x] == "door", 2000);
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

Console.WriteLine(failures == 0 ? $"parity: all {checks} checks match" : $"parity: {failures} of {checks} checks differ");
return failures == 0 ? 0 : 1;
