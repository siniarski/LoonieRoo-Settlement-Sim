// The grid in chunks (GridLayers.cs, 2026-11-10): the accessors over chunk borders and the map's edge, land with no
// chunk allocated, and that what is allocated never shows in a value, in JSON or in a save.
#nullable disable warnings
using System.Text.Json;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class GridChunksTest
{
    static string Whole(SimState s) => JsonSerializer.Serialize(s, StateJson.Options);

    [Fact(DisplayName = "grid chunks > a new grid allocates nothing and reads as its fill everywhere")]
    public void NewGrid()
    {
        var g = Grids.createGrid(100, 70, "wall");
        var m = Grids.memory(g);
        expect(m.slots).toBe(4 * 3);
        expect(m.tiles + m.ground + m.explored + m.dirt + m.damage + m.edges).toBe(0);
        foreach (var (x, y) in new[] { (0, 0), (31, 31), (32, 32), (99, 69), (64, 0), (0, 64) })
        {
            expect(Grids.getTile(g, x, y)).toBe("wall");
            expect(Grids.groundAt(g, x, y)).toBe(0);
            expect(Grids.isExplored(g, x, y)).toBe(true);
            expect(Grids.isDirty(g, x, y)).toBe(false);
            expect(Grids.damageAt(g, y * 100 + x)).toBe(0);
            expect(Grids.edgeKind(g, Grids.edgeIndex(g, x, y, "n"))).toBe(Grids.EDGE_NONE);
            expect(Grids.edgeKind(g, Grids.edgeIndex(g, x, y, "w"))).toBe(Grids.EDGE_NONE);
        }
        expect(Grids.allTiles(g).Count).toBe(7000);
        expect(Grids.allTiles(g).All((t) => t == "wall")).toBe(true);
        expect(Grids.allEdges(g).Count).toBe(14000);
        expect(Grids.anyUnexplored(g)).toBe(false);
        expect(Grids.anyEdges(g)).toBe(false);
    }

    [Fact(DisplayName = "grid chunks > writes and reads agree on both sides of every chunk border and at the map's edge")]
    public void Borders()
    {
        // 70 x 45: three chunk columns (the last 6 tiles wide), two rows (the last 13 tall).
        int w = 70, h = 45;
        var g = Grids.createGrid(w, h);
        var tiles = new[] { "floor", "wall", "door", "water", "barricade" };
        int[] xs = [0, 1, 30, 31, 32, 33, 62, 63, 64, 65, 68, 69], ys = [0, 1, 30, 31, 32, 33, 43, 44];
        foreach (int y in ys)
            foreach (int x in xs)
            {
                int i = y * w + x;
                Grids.putTile(g, i, tiles[(x + 2 * y) % 5]);
                Grids.setGround(g, x, y, (x * 7 + y) % 10);
                Grids.setExplored(g, i, (x + y) % 2);
                Grids.putEdge(g, 2 * i, (x + y) % 6);
                Grids.putEdge(g, 2 * i + 1, (x * 3 + y) % 6);
                Grids.setDamage(g, i, x + y * 0.5);
            }
        foreach (int y in ys)
            foreach (int x in xs)
            {
                int i = y * w + x;
                string at = $"({x}, {y})";
                expect(Grids.getTile(g, x, y), at).toBe(tiles[(x + 2 * y) % 5]);
                expect(Grids.tileAt(g, i), at).toBe(tiles[(x + 2 * y) % 5]);
                expect(Grids.groundAt(g, x, y), at).toBe((x * 7 + y) % 10);
                expect(Grids.groundAt(g, i), at).toBe((x * 7 + y) % 10);
                expect(Grids.exploredAt(g, i), at).toBe((x + y) % 2);
                expect(Grids.isExplored(g, x, y), at).toBe((x + y) % 2 == 1);
                expect(Grids.edgeKind(g, 2 * i), at).toBe((x + y) % 6);
                expect(Grids.edgeKind(g, 2 * i + 1), at).toBe((x * 3 + y) % 6);
                expect(Grids.damageAt(g, i), at).toBe(x + y * 0.5);
            }
        // The edge between two chunks is the far tile's: read by index and by its two tiles.
        expect(Grids.edgeBetween(g, 31, 5, 32, 5)).toBe(Grids.edgeIndex(g, 32, 5, "w"));
        Grids.setEdge(g, 32, 5, "w", Grids.EDGE_WALL);
        expect(Grids.edgeBetween(g, 31, 5, 32, 5, out int kind)).toBe(Grids.edgeIndex(g, 32, 5, "w"));
        expect(kind).toBe(Grids.EDGE_WALL);
        expect(Pathfinding.crossable(g, 31, 5, 32, 5)).toBe(false);
        expect(Pathfinding.crossable(g, 32, 5, 31, 5)).toBe(false);
        Grids.setEdge(g, 5, 32, "n", Grids.EDGE_DOOR);
        expect(Grids.edgeBetween(g, 5, 31, 5, 32, out kind)).toBe(Grids.edgeIndex(g, 5, 32, "n"));
        expect(kind).toBe(Grids.EDGE_DOOR);
        // Untouched tiles beside the written ones still read as the fill.
        expect(Grids.getTile(g, 34, 34)).toBe("floor");
        expect(Grids.exploredAt(g, 34 * w + 34)).toBe(1);
    }

    [Fact(DisplayName = "grid chunks > off the grid: reads are null or 0, writes throw, as the lists did")]
    public void OffTheGrid()
    {
        var g = Grids.createGrid(40, 33);
        int n = 40 * 33;
        expect(Grids.getTile(g, -1, 0)).toBe(null);
        expect(Grids.getTile(g, 40, 0)).toBe(null);
        expect(Grids.getTile(g, 0, 33)).toBe(null);
        expect(Grids.tileAt(g, -1)).toBe(null);
        expect(Grids.tileAt(g, n)).toBe(null);
        expect(Grids.tileAt(g, 2.5)).toBe(null);
        expect(Grids.groundAt(g, n)).toBe(0);
        expect(Grids.groundAt(g, 40, 2)).toBe(0);
        expect(Grids.exploredAt(g, -3)).toBe(0);
        expect(Grids.isExplored(g, 0, -1)).toBe(false);
        expect(Grids.damageAt(g, n + 7)).toBe(0);
        expect(Grids.edgeKind(g, -1)).toBe(Grids.EDGE_NONE);
        expect(Grids.edgeKind(g, 2 * n)).toBe(Grids.EDGE_NONE);
        expect(Grids.setTile(g, 40, 0, "wall")).toBe(false);
        expect(Grids.setEdge(g, 0, 33, "n", Grids.EDGE_WALL)).toBe(false);
        Assert.Throws<ArgumentOutOfRangeException>(() => Grids.putTile(g, n, "wall"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Grids.setGround(g, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Grids.putEdge(g, 2 * n, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Grids.setDamage(g, n, 1));
        // A layer holds bytes: anything else is refused, not stored.
        Assert.Throws<InvalidDataException>(() => Grids.setGround(g, 0, 300));
        Assert.Throws<InvalidDataException>(() => Grids.setGround(g, 0, -1));
    }

    [Fact(DisplayName = "grid chunks > a chunk is allocated by the first value that isn't the fill, a layer at a time")]
    public void Allocation()
    {
        var g = Grids.createGrid(96, 96);
        Grids.setTile(g, 40, 40, "floor"); // the fill: nothing
        Grids.setGround(g, 40, 40, 0);
        expect(Grids.memory(g).tiles + Grids.memory(g).ground).toBe(0);
        Grids.setTile(g, 40, 40, "wall");
        Grids.setTile(g, 41, 40, "wall");
        expect(Grids.memory(g).tiles).toBe(1);
        expect(Grids.memory(g).edges).toBe(0);
        Grids.setEdge(g, 64, 64, "n", Grids.EDGE_WALL);
        expect(Grids.memory(g).edges).toBe(1);
        Grids.setDamage(g, 40 * 96 + 40, 3);
        expect(Grids.memory(g).damage).toBe(1);
        // fillExplored drops what was there.
        Grids.setExplored(g, 0, 0);
        expect(Grids.memory(g).explored).toBe(1);
        expect(Grids.anyUnexplored(g)).toBe(true);
        Grids.fillExplored(g, 0);
        expect(Grids.memory(g).explored).toBe(0);
        expect(Grids.exploredAt(g, 5000)).toBe(0);
        Grids.fillExplored(g, 1);
        expect(Grids.anyUnexplored(g)).toBe(false);
        // A tile kind that isn't one of the five is kept as it is.
        Grids.putTile(g, 3, "rubble");
        expect(Grids.tileAt(g, 3)).toBe("rubble");
    }

    [Fact(DisplayName = "grid chunks > ungenerated land of a world reads as bare land and holds nothing")]
    public void Ungenerated()
    {
        var s = Worldgen.createWorld(5, 512);
        int n = (int)s.world.chunks;
        int generated = s.world.generated.Count((v) => v == 1);
        expect(generated).toBeLessThan(n * n / 2);
        var m = Grids.memory(s.grid);
        // No layer holds more chunks than were generated (the start's reveal is inside them).
        foreach (int count in new[] { m.tiles, m.ground, m.explored, m.dirt, m.damage, m.edges }) expect(count).toBeLessThanOrEqual(generated);
        int checkedChunks = 0;
        for (int c = 0; c < n * n; c++)
        {
            if (s.world.generated[c] == 1) continue;
            int x0 = c % n * Worldgen.CHUNK, y0 = c / n * Worldgen.CHUNK;
            foreach (var (dx, dy) in new[] { (0, 0), (31, 0), (0, 31), (31, 31), (13, 7) })
            {
                int x = x0 + dx, y = y0 + dy, i = y * 512 + x;
                expect(Grids.getTile(s.grid, x, y)).toBe("floor");
                expect(Grids.groundAt(s.grid, i)).toBe(0);
                expect(Grids.exploredAt(s.grid, i)).toBe(0);
                expect(Grids.edgeKind(s.grid, 2 * i) + Grids.edgeKind(s.grid, 2 * i + 1)).toBe(0);
                expect(Grids.isWalkable(s.grid, x, y)).toBe(true);
            }
            checkedChunks++;
        }
        expect(checkedChunks).toBe(n * n - generated);
        expect(Grids.anyUnexplored(s.grid)).toBe(true);
    }

    [Fact(DisplayName = "grid chunks > a world generated in two chunk orders is the same state")]
    public void TwoOrders()
    {
        int size = 256, n = size / Worldgen.CHUNK;
        var l = Worldgen.layout(11, size);
        SimState Make(IEnumerable<int> order)
        {
            var s = Worldgen.createWorld(11, size);
            foreach (int c in order)
                if (s.world.generated[c] != 1) Worldgen.ensureGeneratedAround(s, new Point(c % n * Worldgen.CHUNK + 16, c / n * Worldgen.CHUNK + 16));
            return s;
        }
        var all = Enumerable.Range(0, n * n).ToList();
        var a = Make(all);
        var b = Make(all.AsEnumerable().Reverse());
        // Interleaved: odd chunks, then even ones from the far end.
        var c2 = Make(all.Where((c) => c % 2 == 1).Concat(all.Where((c) => c % 2 == 0).Reverse()));
        expect(Grids.allTiles(b.grid)).toEqual(Grids.allTiles(a.grid));
        expect(Grids.allGround(b.grid)).toEqual(Grids.allGround(a.grid));
        expect(Grids.allEdges(b.grid)).toEqual(Grids.allEdges(a.grid));
        expect(Grids.allTiles(c2.grid)).toEqual(Grids.allTiles(a.grid));
        expect(Grids.allGround(c2.grid)).toEqual(Grids.allGround(a.grid));
        expect(Grids.allEdges(c2.grid)).toEqual(Grids.allEdges(a.grid));
        expect(Grids.memory(b.grid)).toEqual(Grids.memory(a.grid));
        // The same save, whatever the order.
        expect(Savefile.toSaveFile(b).Length).toBe(Savefile.toSaveFile(a).Length);
    }

    [Fact(DisplayName = "grid chunks > a hand-made map is the same grid in JSON as a list of tiles ever was")]
    public void HandMade()
    {
        var s = ScenarioMod.loadScenario(new Scenario { seed = 9, map = ["..........", ".####D###.", ".#..s...#.", ".########.", "w.f...c..."] });
        expect(Grids.allTiles(s.grid).Count).toBe(50);
        expect(Grids.getTile(s.grid, 5, 1)).toBe("door");
        expect(Grids.getTile(s.grid, 1, 1)).toBe("wall");
        var json = JsonSerializer.SerializeToNode(s.grid, StateJson.Options).AsObject();
        // The layers are flat arrays under their old names, in their old places.
        expect(json.Select((p) => p.Key).Take(7).ToList()).toEqual(new List<string> { "width", "height", "tiles", "dirt", "damage", "ground", "explored" });
        expect(json["tiles"].AsArray().Count).toBe(50);
        expect(json["tiles"][15].GetValue<string>()).toBe("door");
        expect(json["edges"].AsArray().Count).toBe(100);
        expect(json["explored"].AsArray().All((v) => v.GetValue<int>() == 1)).toBe(true);
        expect(json["damage"][0].ToJsonString()).toBe("0");
        // Through JSON and back: the same state, and it plays the same.
        var copy = StateJson.Clone(s);
        expect(Whole(copy)).toBe(Whole(s));
        Tick.step(s, 200);
        Tick.step(copy, 200);
        expect(Whole(copy)).toBe(Whole(s));
        var back = Savefile.loadFile(Savefile.toSaveFile(s));
        expect(back.ok).toBe(true);
        expect(Whole(back.state)).toBe(Whole(s));
    }

    [Fact(DisplayName = "grid chunks > a played world comes back exactly from a save, from JSON, and saves again the same")]
    public void RoundTrips()
    {
        // 300 isn't a multiple of 32: the east and south chunks are cut by the map's edge.
        var s = Worldgen.createWorld(3, 300);
        var start = s.survivors[0].pos;
        Worldgen.ensureGeneratedAround(s, new Point(299, 299));
        Worldgen.ensureGeneratedAround(s, new Point(299, 0));
        for (int i = 0; i < 150; i++) Tick.tick(s);
        Grids.setTile(s.grid, start.x + 4, start.y + 4, "wall");
        Grids.setTile(s.grid, 298, 298, "wall");
        Grids.setEdge(s.grid, 299, 297, "w", Grids.EDGE_WALL);
        Grids.setDamage(s.grid, Grids.tileIndex(s.grid, 298, 298), 2.5);
        Grids.setDirty(s.grid, start.x + 1, start.y + 2, true);
        string whole = Whole(s);
        var text = Savefile.toSaveFile(s);
        var back = Savefile.loadFile(text);
        expect(back.ok).toBe(true);
        expect(Whole(back.state) == whole).toBe(true);
        expect(Savefile.toSaveFile(back.state) == text).toBe(true);
        // Loading allocates no more than the state it came from held.
        expect(Grids.memory(back.state.grid).bytes).toBeLessThanOrEqual(Grids.memory(s.grid).bytes);
        var clone = StateJson.Clone(s);
        expect(Whole(clone) == whole).toBe(true);
        // Both copies play on as the original does.
        for (int i = 0; i < 60; i++) { Tick.tick(s); Tick.tick(back.state); Tick.tick(clone); }
        string after = Whole(s);
        expect(Whole(back.state) == after).toBe(true);
        expect(Whole(clone) == after).toBe(true);
    }
}
