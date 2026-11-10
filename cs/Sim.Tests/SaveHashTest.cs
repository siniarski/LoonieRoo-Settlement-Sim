// Saves written straight to text (SaveWriter) and chunks skipped by their hash as made (WorldInfo.made, schema 78).
#nullable disable warnings
using System.Text.Json;
using System.Text.Json.Nodes;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SaveHashTest
{
    static string Whole(SimState s) => JsonSerializer.Serialize(s, StateJson.Options);

    static SimState World() => Worldgen.createWorld(3, 256);

    /// <summary>A generated chunk still as made, away from the start (none of the start's own chunks).</summary>
    static int Untouched(SimState s)
    {
        int n = (int)s.world.chunks;
        var changed = SavefileChunks.changed(s);
        var start = s.survivors[0].pos;
        for (int i = 0; i < n * n; i++)
        {
            if (s.world.generated[i] != 1 || changed.Contains(i)) continue;
            int cx = i % n, cy = i / n;
            if (Math.Abs(cx - start.x / Worldgen.CHUNK) >= 2 || Math.Abs(cy - start.y / Worldgen.CHUNK) >= 2) return i;
        }
        throw new InvalidOperationException("no untouched chunk");
    }

    static JsonObject ChunkEntry(string file, int index) =>
        JsonNode.Parse(file)!["state"]!["chunks"]!.AsArray().Select(c => c!.AsObject()).FirstOrDefault(c => (int)c["c"]!.GetValue<double>() == index);

    static void ComesBack(SimState s, string file)
    {
        var back = Savefile.loadFile(file);
        expect(back.ok).toBe(true);
        expect(Whole(back.state) == Whole(s) ? "" : "the loaded state differs").toBe("");
        expect(Savefile.toSaveFile(back.state) == file).toBe(true);
    }

    [Fact(DisplayName = "chunk hashes > every generated chunk has its hash as made, and a chunk made again has the same")]
    public void noted()
    {
        var s = World();
        int n = (int)s.world.chunks;
        int generated = s.world.generated.Count(g => g == 1);
        expect(s.world.made.Count).toBe(generated);
        var l = Worldgen.layout(3, 256, s.settings.map);
        for (int i = 0; i < 100; i++) Tick.tick(s);
        int index = Untouched(s);
        expect(SavefileChunks.madeHash(Worldgen.makeChunk(s, l, index % n, index / n))).toBe(s.world.made[Js.Str(index)]);
        // Only chunks touched since generation are made again at a save: the start's camp and what's moved there.
        expect(SavefileChunks.changed(s).Count < generated / 2).toBe(true);
    }

    [Fact(DisplayName = "chunk hashes > a chunk changed in its grid is saved, an unchanged one is skipped")]
    public void grid_change()
    {
        var s = World();
        int n = (int)s.world.chunks;
        int index = Untouched(s);
        var before = Savefile.toSaveFile(s);
        expect(ChunkEntry(before, index)).toBe(null);
        int x = index % n * Worldgen.CHUNK + 7, y = index / n * Worldgen.CHUNK + 9;
        Grids.setTile(s.grid, x, y, s.grid.tiles[y * s.grid.width + x] == "wall" ? "floor" : "wall");
        expect(SavefileChunks.changed(s).Contains(index)).toBe(true);
        var after = Savefile.toSaveFile(s);
        expect(ChunkEntry(after, index)?["tiles"] != null).toBe(true);
        ComesBack(s, after);
    }

    [Fact(DisplayName = "chunk hashes > a chunk whose furniture changed is saved, the piece in full")]
    public void furniture_change()
    {
        var s = World();
        int n = (int)s.world.chunks;
        int index = Untouched(s);
        string prefix = $"f{index % n}.{index / n}.";
        var piece = s.furniture.First(f => f.id.StartsWith(prefix));
        piece.loot += 3;
        expect(SavefileChunks.changed(s).Contains(index)).toBe(true);
        var file = Savefile.toSaveFile(s);
        var furniture = JsonNode.Parse(file)!["state"]!["furniture"]!.AsArray();
        expect(furniture.Any(r => r is JsonArray row && row[0]!.GetValue<string>() == piece.id)).toBe(true);
        expect(ChunkEntry(file, index)).toBe(null); // its grid is as made
        ComesBack(s, file);
        // Put back as it was, the chunk is as made again.
        piece.loot -= 3;
        expect(SavefileChunks.changed(s).Contains(index)).toBe(false);
    }

    [Fact(DisplayName = "chunk hashes > a schema-77 save (no hashes) loads, with its hashes made on load")]
    public void schema_77()
    {
        var s = World();
        for (int i = 0; i < 50; i++) Tick.tick(s);
        var file = JsonNode.Parse(Savefile.toSaveFile(s))!.AsObject();
        file["schemaVersion"] = 77;
        file["state"]!["schemaVersion"] = 77;
        file["state"]!["world"]!.AsObject().Remove("made");
        var back = Savefile.loadFile(file.ToJsonString(StateJson.Options));
        expect(back.ok).toBe(true);
        expect(back.state.schemaVersion).toBe(States.SCHEMA_VERSION);
        // The same hashes (in chunk order rather than the order generated), and otherwise the same state.
        expect(back.state.world.made.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList())
            .toEqual(s.world.made.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList());
        back.state.world.made = s.world.made;
        expect(Whole(back.state) == Whole(s) ? "" : "the loaded state differs").toBe("");
        // Older still is refused.
        file["schemaVersion"] = 76;
        file["state"]!["schemaVersion"] = 76;
        expect(Savefile.loadFile(file.ToJsonString(StateJson.Options)).ok).toBe(false);
    }
}

public class SaveWriterTest
{
    [Fact(DisplayName = "save writer > writes an object's members as the serializer does")]
    public void members()
    {
        var s = Worldgen.createWorld(2, 128);
        for (int i = 0; i < 30; i++) Tick.tick(s);
        foreach (object o in new object[] { s, s.grid, s.world, s.survivors[0], s.furniture[0] })
        {
            var text = SaveWriter.Text((w) => { w.WriteStartObject(); SaveWriter.Members(w, o); w.WriteEndObject(); });
            expect(text).toBe(JsonSerializer.Serialize(o, o.GetType(), StateJson.Options));
        }
    }

    [Fact(DisplayName = "save writer > run-length encodes as runLengthEncode does")]
    public void rle()
    {
        int[] ints = [0, 0, 0, 1, 2, 2, -1, 0];
        string[] strings = ["floor", "floor", "wall", null, null, "floor", "<&é>"];
        double[] doubles = [0, 0, 0.5, 0.5, 1e21, double.NaN, double.NaN, -0.0, 0, 3];
        string Old<T>(T[] v) => Savefile.runLengthEncode(JsonSerializer.SerializeToNode(v, StateJson.Options)!.AsArray()).ToJsonString(StateJson.Options);
        expect(SaveWriter.Text((w) => SaveWriter.Rle(w, ints))).toBe(Old(ints));
        expect(SaveWriter.Text((w) => SaveWriter.Rle(w, strings))).toBe(Old(strings));
        // Doubles: the same runs, but -0 kept apart from 0 (to the bit), which runLengthEncode may merge.
        var mine = JsonNode.Parse(SaveWriter.Text((w) => SaveWriter.Rle(w, doubles)))!.AsArray();
        var decoded = Savefile.runLengthDecode(mine).Select(v => v!.ToJsonString()).ToList();
        expect(string.Join(",", decoded)).toBe(string.Join(",", doubles.Select(d => JsonSerializer.Serialize(d, StateJson.Options))));
        expect(SaveWriter.Text((w) => SaveWriter.Rle(w, Array.Empty<int>()))).toBe("[]");
    }

    [Fact(DisplayName = "save writer > furniture rows as furnitureRow makes them")]
    public void rows()
    {
        var pieces = new List<Furniture>
        {
            new() { id = "f1.2.3", kind = "crate", x = 4, y = 5 },
            new() { id = "9", kind = "bed", x = 1, y = 2, occupant = "s1" },
            new() { id = "10", kind = "crate", x = 1, y = 2, loot = 2.5 },
            new() { id = "11", kind = "shelf", x = 1, y = 2, scavenge = true },
            new() { id = "12", kind = "shelf", x = 1, y = 2, stored = 3, storedKind = "food" },
            new() { id = "13", kind = "table", x = 0, y = 0, owner = "s2", look = "oak", table = "t1" },
            new() { id = "14", kind = "crate", x = 0, y = 0, loot = -0.0 },
        };
        foreach (var f in pieces)
        {
            var old = Savefile.furnitureRow(JsonSerializer.SerializeToNode(f, StateJson.Options))!.ToJsonString(StateJson.Options);
            var mine = SaveWriter.Text((w) => Savefile.writeFurnitureRow(w, f));
            // -0 loot: furnitureRow may drop it as the default 0; the writer keeps it, so it loads as -0.
            if (double.IsNegative(f.loot) && f.loot == 0) { expect(mine).toBe("[\"14\",\"crate\",0,0,null,null,-0]"); continue; }
            expect(mine).toBe(old);
        }
    }
}

public class BugStartTest
{
    [Fact(DisplayName = "bug reports > start from the packed state: the same file as from the state, and a world replays exactly")]
    public void packed_start()
    {
        var s = Worldgen.createWorld(4, 256);
        for (int i = 0; i < 20; i++) Tick.tick(s);
        var initial = States.cloneState(s);
        var start = Savefile.bugStart(s);
        var p = s.survivors[0].pos;
        for (int i = 0; i < 30; i++)
        {
            Commands.applyCommand(s, new Command { type = i % 2 == 0 ? "placeWall" : "placeDoor", x = p.x + 2 + i % 5, y = p.y - 3 + i / 5 });
            Tick.tick(s);
        }
        expect(s.commandLog.Count > initial.commandLog.Count).toBe(true);
        var report = Savefile.toBugReport(start, s, "walls");
        expect(report).toBe(Savefile.toBugReport(initial, s, "walls"));
        var back = Savefile.loadFile(report);
        expect(back.ok ? back.kind : back.error).toBe("bug");
        expect(JsonSerializer.Serialize(back.state, StateJson.Options) == JsonSerializer.Serialize(s, StateJson.Options)).toBe(true);
        // A world's start is its changes only: far smaller than the state.
        expect(start.Length < JsonSerializer.Serialize(initial, StateJson.Options).Length / 20).toBe(true);
    }
}
