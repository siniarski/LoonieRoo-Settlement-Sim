// Saves written by one build, loaded by another (2026-11-10, schema 77 -> 78): DUMP=dir writes, for a few played
// states, the save file, a bug report and the state's whole JSON; CHECK=dir loads them and compares.
//   DUMP=<dir> dotnet test cs/Harness -c Release --filter "DisplayName=save compat"   (on the old build)
//   CHECK=<dir> ... (on the new). Report: cs/Harness/out/save-compat.txt
using System.Text.Json;
using System.Text.Json.Nodes;
using LoonieRoo.Sim;

namespace LoonieRoo.Harness;

public class SaveCompat
{
    static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    static IEnumerable<(string name, SimState initial, SimState state)> States_()
    {
        foreach (var (size, all) in new[] { (128, true), (256, false), (512, false), (512, true) })
        {
            var s = SaveSpeed.Played(size, all);
            var initial = Worldgen.createWorld(42, size, Difficulty.NORMAL);
            yield return ($"world-{size}-{(all ? "all" : "start")}", initial, s);
        }
        var map = ScenarioMod.loadScenario(new Scenario { seed = 9, map = ["..........", ".####D###.", ".#..s...#.", ".########.", "w.f...c..."] });
        var played = LoonieRoo.Sim.States.cloneState(map);
        Tick.step(played, 300);
        yield return ("map", map, played);
    }

    /// <summary>The state's JSON without what schema 78 added (world.made) and with the schema number at 77.</summary>
    static string Old(SimState s)
    {
        var o = JsonSerializer.SerializeToNode(s, StateJson.Options)!.AsObject();
        o["schemaVersion"] = 77;
        o["commandLog"] = new JsonArray();
        if (o["world"] is JsonObject w) w.Remove("made");
        return o.ToJsonString(StateJson.Options);
    }

    [Fact(DisplayName = "save compat")]
    public void Run()
    {
        var out_ = new List<string>();
        if (Env("DUMP") is { } dump)
        {
            Directory.CreateDirectory(dump);
            foreach (var (name, initial, s) in States_())
            {
                File.WriteAllText(Path.Combine(dump, name + ".save.json"), Savefile.toSaveFile(s));
                File.WriteAllText(Path.Combine(dump, name + ".bug.json"), Savefile.toBugReport(initial, s));
                File.WriteAllText(Path.Combine(dump, name + ".state.json"), Old(s));
                // A bug report's state is its replay (the played states have hand edits no command reproduces): as this build replays it.
                var replayed = Savefile.loadFile(Savefile.toBugReport(initial, s));
                File.WriteAllText(Path.Combine(dump, name + ".bugstate.json"), replayed.ok ? Old(replayed.state) : "failed: " + replayed.error);
                out_.Add($"{name}: written");
            }
        }
        if (Env("CHECK") is { } check)
            foreach (var f in Directory.GetFiles(check, "*.state.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(f)[..^".state.json".Length];
                foreach (var kind in new[] { "save", "bug" })
                {
                    string want = File.ReadAllText(kind == "save" ? f : Path.Combine(check, name + ".bugstate.json"));
                    var r = Savefile.loadFile(File.ReadAllText(Path.Combine(check, $"{name}.{kind}.json")));
                    if (!r.ok) { out_.Add($"{name} {kind}: did not load: {r.error}"); continue; }
                    string got = Old(r.state);
                    var diff = StateJson.Diff(JsonNode.Parse(got), JsonNode.Parse(want), 5);
                    // The save written again by this build: the old file but for schema 78's additions?
                    string same = "";
                    if (kind == "save")
                    {
                        var file = JsonNode.Parse(Savefile.toSaveFile(r.state))!.AsObject();
                        file["schemaVersion"] = 77;
                        file["state"]!["schemaVersion"] = 77;
                        if (file["state"]!["world"] is JsonObject fw) fw.Remove("made");
                        string old = File.ReadAllText(Path.Combine(check, $"{name}.save.json"));
                        string mine = file.ToJsonString(StateJson.Options);
                        int at = 0;
                        while (at < Math.Min(mine.Length, old.Length) && mine[at] == old[at]) at++;
                        same = mine == old ? ", written again the same" : $", WRITTEN AGAIN DIFFERENT at {at}: ...{old[Math.Max(0, at - 60)..Math.Min(old.Length, at + 60)]} vs ...{mine[Math.Max(0, at - 60)..Math.Min(mine.Length, at + 60)]}";
                    }
                    out_.Add($"{name} {kind}: {(got == want ? "identical" : "DIFFERS " + string.Join("; ", diff))}" +
                        (r.state.world != null ? $", {r.state.world.made.Count} chunk hashes, schema {r.state.schemaVersion}" : "") + same);
                }
            }
        File.WriteAllText(Path.Combine(Node.Root, "cs/Harness/out/save-compat.txt"), string.Join("\n", out_) + "\n");
    }
}
