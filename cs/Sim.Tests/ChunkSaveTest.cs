// Chunks made again from the seed, and saves that keep only what changed (Worldgen.makeChunk, SavefileChunks).
#nullable disable warnings
using System.Text.Json;
using System.Text.Json.Nodes;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class ChunkSaveTest
{
    static string Whole(SimState s) => JsonSerializer.Serialize(s, StateJson.Options);

    /// <summary>A world played a little and changed by hand the ways play changes it.</summary>
    static SimState Played(double seed, int size)
    {
        var s = Worldgen.createWorld(seed, size);
        var start = s.survivors[0].pos;
        foreach (var (dx, dy) in new[] { (60, 0), (-60, 30), (0, -70) })
            Worldgen.ensureGeneratedAround(s, new Point { x = Math.Clamp(start.x + dx, 0, size - 1), y = Math.Clamp(start.y + dy, 0, size - 1) });
        for (int i = 0; i < 200; i++) Tick.tick(s);
        // A tree cut down, a crate emptied, a wall put up and an edge wall knocked down, a floor dirtied, one of the dormant woken.
        var tree = s.furniture.First(f => f.kind == "tree");
        FurnitureMod.removeFurniture(s, tree);
        var crate = s.furniture.FirstOrDefault(f => f.kind == "crate");
        if (crate != null) crate.loot = 0;
        Grids.setTile(s.grid, start.x + 5, start.y + 5, "wall");
        int e = Grids.allEdges(s.grid).FindIndex(v => v == Grids.EDGE_WALL);
        if (e >= 0) Grids.putEdge(s.grid, e, Grids.EDGE_NONE);
        Grids.setDirty(s.grid, start.x + 2, start.y + 6, true);
        if (s.dormant.Count > 0) Dormant.remove(s, s.dormant[0]);
        FurnitureMod.addFurniture(s, "workbench", start.x + 3, start.y + 3);
        return s;
    }

    static void RoundTrip(double seed, int size)
    {
        var s = Played(seed, size);
        var text = Savefile.toSaveFile(s);
        var back = Savefile.loadFile(text);
        expect(back.ok).toBe(true);
        expect(Whole(back.state) == Whole(s) ? "" : $"{size}: the loaded state differs").toBe("");
        // And again: a save of the loaded state is the same file.
        expect(Savefile.toSaveFile(back.state) == text).toBe(true);
    }

    [Fact(DisplayName = "chunk saves > a played and changed world comes back exactly (one region)")]
    public void round_trip()
    {
        RoundTrip(5, 256);
    }

    // The several-region world is a second or two of planning before it's played at all (a test of its own, to share out).
    [Fact(DisplayName = "chunk saves > a played and changed world comes back exactly (several regions)")]
    public void round_trip_several_regions()
    {
        RoundTrip(8, 1536);
    }
}

// Its own class so ./check.sh can run it in another process beside the one above (the round trip is seconds).
public class ChunkSaveMoreTest
{
    [Fact(DisplayName = "chunk saves > a chunk made again later, in a played state, is the same chunk")]
    public void made_again()
    {
        var s = Worldgen.createWorld(4, 256);
        var l = Worldgen.layout(4, 256, s.settings.map);
        var first = Worldgen.makeChunk(s, l, 5, 2);
        for (int i = 0; i < 100; i++) Tick.tick(s);
        Worldgen.ensureGeneratedAround(s, new Point { x = 170, y = 70 });
        var again = Worldgen.makeChunk(s, l, 5, 2);
        expect(JsonSerializer.Serialize(again.grid, StateJson.Options)).toBe(JsonSerializer.Serialize(first.grid, StateJson.Options));
        expect(JsonSerializer.Serialize(again.furniture, StateJson.Options)).toBe(JsonSerializer.Serialize(first.furniture, StateJson.Options));
        expect(JsonSerializer.Serialize(again.dormant, StateJson.Options)).toBe(JsonSerializer.Serialize(first.dormant, StateJson.Options));
    }

    [Fact(DisplayName = "chunk saves > chunks generated in another order give the same world")]
    public void any_order()
    {
        SimState Gen(bool backwards)
        {
            var s = Worldgen.createWorld(6, 192);
            var l = Worldgen.layout(6, 192, s.settings.map);
            int n = (int)s.world.chunks;
            var order = Enumerable.Range(0, n * n).ToList();
            if (backwards) order.Reverse();
            foreach (var i in order)
                if (Js.Get(s.world.generated, i) != 1) { s.world.generated[i] = 1; Worldgen.generateChunk(s, l, i % n, i / n); }
            return s;
        }
        var a = Gen(false);
        var b = Gen(true);
        expect(JsonSerializer.Serialize(Grids.allTiles(b.grid), StateJson.Options) == JsonSerializer.Serialize(Grids.allTiles(a.grid), StateJson.Options)).toBe(true);
        expect(JsonSerializer.Serialize(Grids.allEdges(b.grid), StateJson.Options) == JsonSerializer.Serialize(Grids.allEdges(a.grid), StateJson.Options)).toBe(true);
        expect(JsonSerializer.Serialize(Grids.allGround(b.grid), StateJson.Options) == JsonSerializer.Serialize(Grids.allGround(a.grid), StateJson.Options)).toBe(true);
        string Set<T>(IEnumerable<T> xs) => string.Join("\n", xs.Select(x => JsonSerializer.Serialize(x, StateJson.Options)).OrderBy(x => x, StringComparer.Ordinal));
        expect(Set(b.furniture) == Set(a.furniture)).toBe(true);
        expect(Set(b.dormant) == Set(a.dormant)).toBe(true);
    }

    [Fact(DisplayName = "chunk saves > an untouched generated world keeps almost nothing of its chunks")]
    public void small()
    {
        var s = Worldgen.createWorld(2, 256);
        for (int y = 0; y < 256; y += Worldgen.CHUNK)
            for (int x = 0; x < 256; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(s, new Point { x = x, y = y });
        var file = JsonNode.Parse(Savefile.toSaveFile(s))!["state"]!;
        // Only the start's chunks differ (its camp, what you can see from it), and every tree and crate is a run.
        expect(file["chunks"]!.AsArray().Count <= 9).toBe(true);
        var furniture = file["furniture"]!.AsArray();
        expect(furniture.Count < s.furniture.Count / 20).toBe(true);
    }
}
