// Growth in a generated world: saplings that become sim trees at a tick set by the tile, across runs and a save and load.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class GrowthWorldTest
{
    const double SEED = 7;
    const int SIZE = 256;

    static int TickAt(double d) => (int)Math.Round(d * Time.DAY_LENGTH - Time.START_HOUR * Time.TICKS_PER_HOUR);

    /// <summary>Tiles (within `r` of the start) where a sapling comes up, free of anything, nearest the start first.</summary>
    static List<(int x, int y, GrowthKind kind)> Candidates(SimState s, int r)
    {
        var start = s.survivors[0].pos;
        var found = new List<(int x, int y, GrowthKind kind)>();
        for (var y = Math.Max(0, start.y - r); y < Math.Min(SIZE, start.y + r); y++)
            for (var x = Math.Max(0, start.x - r); x < Math.Min(SIZE, start.x + r); x++)
            {
                var kind = Growth.kindAt(s, x, y);
                if (kind != GrowthKind.None && FurnitureMod.furnitureAt(s, x, y) == null && Growth.candidate(s, x, y, kind, out _, out _)) found.Add((x, y, kind));
            }
        return found.OrderBy(c => Math.Abs(c.x - start.x) + Math.Abs(c.y - start.y)).ToList();
    }

    /// <summary>Growth.update as the game calls it, until the chunk plans round the survivors are all made (a couple a call).</summary>
    static void Look(SimState s)
    {
        for (var i = 0; i < 16; i++) Growth.update(s);
    }

    static string Trees(SimState s) => string.Join(",", s.furniture.Where(f => f.kind == "tree" && f.id.StartsWith("g")).Select(f => f.id).OrderBy(i => i, StringComparer.Ordinal));

    [Fact(DisplayName = "growth > a sapling becomes a real tree at the tick its tile says, and not a tick before")]
    public void sapling_becomes_a_tree()
    {
        var s = Worldgen.createWorld(SEED, SIZE);
        var c = Candidates(s, 60);
        expect(c.Count).toBeGreaterThan(0);
        var (x, y, kind) = c[0];
        Growth.candidate(s, x, y, kind, out var start, out var span);
        var t = Growth.solidTick(s, x, y, start, span);
        // About 3 months to 8 months of game time: weeks into the world's second year at the soonest.
        expect(t).toBeGreaterThan(TickAt(50));
        s.tick = t - 1;
        Look(s);
        expect(FurnitureMod.furnitureAt(s, x, y)).toBeNull();
        s.tick = t;
        Look(s);
        var tree = FurnitureMod.furnitureAt(s, x, y);
        expect(tree?.kind).toBe("tree");
        expect(tree.loot).toBe(FurnitureMod.TREE_WOOD);   // choppable
        // Solid through Trunks' own rule.
        expect(Trunks.treeAt(s, x, y)).toBe(true);
        var c0 = Trunks.trunkAt(x, y);
        var pushed = Trunks.pushOut(s, new Point { x = c0.x + 10, y = c0.y }, Motion.BODY_RADIUS);
        expect(Motion.distance2(pushed, c0)).toBeGreaterThan(Trunks.RADIUS * Trunks.RADIUS);
    }

    [Fact(DisplayName = "growth > the same trees stand at the same ticks whatever order they were looked at, and across a save and load")]
    public void same_on_two_runs()
    {
        var a = Worldgen.createWorld(SEED, SIZE);
        var end = TickAt(260);
        // Run A: looked at often.
        for (var t = 0; t <= end; t += 97) { a.tick = t; Look(a); }
        a.tick = end; Look(a);
        // Run B: looked at in three jumps, with a save and load between.
        var b = Worldgen.createWorld(SEED, SIZE);
        b.tick = TickAt(90); Look(b);
        var loaded = Savefile.loadFile(Savefile.toSaveFile(b));
        expect(loaded.ok).toBe(true);
        b = loaded.state;
        b.tick = TickAt(170); Look(b);
        b.tick = end; Look(b);
        var ta = Trees(a);
        expect(ta.Length).toBeGreaterThan(0);
        expect(Trees(b)).toBe(ta);
    }

    [Fact(DisplayName = "growth > growth values are the same after a save and load, record included")]
    public void save_and_load()
    {
        var s = Worldgen.createWorld(SEED, SIZE);
        var start = s.survivors[0].pos;
        s.tick = TickAt(80.25);
        Look(s);
        Growth.clear(s, start.x + 4, start.y + 4);
        Growth.clear(s, start.x - 3, start.y + 2);
        s.tick = TickAt(150.25);
        var tiles = Enumerable.Range(0, 400).Select(i => (x: start.x - 10 + i % 20, y: start.y - 10 + i / 20)).ToList();
        var before = tiles.Select(t => Growth.cover(s, t.x, t.y)).ToList();
        var back = Savefile.loadFile(Savefile.toSaveFile(s)).state;
        expect(back.cleared.Count).toBe(2);
        back.tick = s.tick;
        for (var i = 0; i < tiles.Count; i++) expect(Growth.cover(back, tiles[i].x, tiles[i].y) == before[i]).toBe(true);
        // A save from before the record existed loads with none.
        expect(Savefile.loadFile(Savefile.toSaveFile(Worldgen.createWorld(SEED, SIZE))).state.cleared.Count).toBe(0);
    }

    [Fact(DisplayName = "growth > the world as made, played a while on day 1: nothing recorded, no tree planted, chunks as generated")]
    public void day_one_unchanged()
    {
        var s = Worldgen.createWorld(SEED, SIZE);
        for (var i = 0; i < 300; i++) Tick.tick(s);
        expect(s.cleared.Count).toBe(0);
        expect(Trees(s)).toBe("");
        var l = Worldgen.layout(SEED, SIZE, s.settings.map);
        var made = Worldgen.makeChunk(s, l, 3, 3);
        expect(made.furniture.Count(f => f.kind == "tree" && f.id.StartsWith("g"))).toBe(0);
    }

    [Fact(DisplayName = "growth > a tile cleared before its sapling is solid starts that sapling again")]
    public void cleared_sapling_restarts()
    {
        var s = Worldgen.createWorld(SEED, SIZE);
        var (x, y, kind) = Candidates(s, 60)[0];
        Growth.candidate(s, x, y, kind, out var start, out var span);
        var t = Growth.solidTick(s, x, y, start, span);
        s.tick = t - 100; Look(s);
        Growth.clear(s, x, y);
        var later = Growth.solidTick(s, x, y, start, span);
        expect(later).toBeGreaterThan(t);
        s.tick = t; Look(s);
        expect(FurnitureMod.furnitureAt(s, x, y)).toBeNull();
        s.tick = later; Look(s);
        expect(FurnitureMod.furnitureAt(s, x, y)?.kind).toBe("tree");
    }

    [Fact(DisplayName = "growth > a chopped tree's wood still grows back every three days")]
    public void wood_regrows()
    {
        var s = Worldgen.createWorld(SEED, SIZE);
        var tree = s.furniture.First(f => f.kind == "tree");
        tree.loot = 0;
        s.tick = (int)FurnitureMod.TREE_REGROW_TICKS;
        Growth.update(s);
        expect(tree.loot).toBe(1);
    }
}
