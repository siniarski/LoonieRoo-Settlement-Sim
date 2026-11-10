// The world's picture (WorldPreview): nature as NatureMod has it, the plan drawn over it, the same on any thread,
// and a save's picture read from the head of the file.
#nullable disable warnings
using System.Text;
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class WorldPreviewTest
{
    [Fact(DisplayName = "world preview > nature is NatureMod's, a pixel a tile on a small world")]
    public void nature_matches()
    {
        var n = NatureMod.natureFor(7, 128, "mainland");
        var img = WorldPreview.nature(n, WorldPreview.sideFor(128));
        expect(img.Length).toBe(128 * 128);
        int wrong = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                byte want = NatureMod.isWater(n, x, y) ? WorldPreview.WATER : NatureMod.isForest(n, x, y) ? WorldPreview.FOREST : WorldPreview.LAND;
                if (img[y * 128 + x] != want) wrong++;
            }
        expect(wrong).toBe(0);
    }

    [Fact(DisplayName = "world preview > a quarantine zone shows its fence all round")]
    public void quarantine_fence()
    {
        var img = WorldPreview.nature(NatureMod.natureFor(3, 512, "quarantine"), WorldPreview.sideFor(512));
        int px = WorldPreview.PX;
        // The fence is two tiles in from the edge (tiles 2 and 509: pixels 1 and 254), along the middle of every side.
        int near = 2 * px / 512, far = 509 * px / 512;
        expect(img[(px / 2) * px + near]).toBe(WorldPreview.FENCE);
        expect(img[(px / 2) * px + far]).toBe(WorldPreview.FENCE);
        expect(img[near * px + px / 2]).toBe(WorldPreview.FENCE);
        expect(img[far * px + px / 2]).toBe(WorldPreview.FENCE);
        expect(img[(px / 2) * px + px / 2]).not.toBe(WorldPreview.FENCE);
    }

    [Fact(DisplayName = "world preview > the plan is drawn: every street, towns, lots and roads")]
    public void plan_drawn()
    {
        var r = new WorldRegions(42, 256, "mainland");
        var img = WorldPreview.build(r);
        var p = r.Region(0, 0);
        expect(p.streets.Count > 0).toBe(true);
        foreach (var s in p.streets)
            for (int y = s.y0; y <= s.y1; y++)
                for (int x = s.x0; x <= s.x1; x++) expect(img[y * 256 + x]).toBe(WorldPreview.STREET);
        var counts = new int[9];
        foreach (var k in img) counts[k]++;
        expect(counts[WorldPreview.ROAD] > 0).toBe(true);
        expect(counts[WorldPreview.BUILT] > 0).toBe(true);
    }

    [Fact(DisplayName = "world preview > a save carries its picture at the head of the file")]
    public void save_preview()
    {
        var state = Worldgen.createWorld(9, 256, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), (s) => s.start = "solo"));
        var text = Savefile.toSaveFile(state);
        expect(text.IndexOf("\"preview\"") < text.IndexOf("\"state\"")).toBe(true);
        var p = Savefile.readPreview(Encoding.UTF8.GetBytes(text));
        expect(p.seed).toBe(9);
        expect(p.size).toBe(256);
        expect(p.map).toBe("mainland");
        expect(p.px).toBe(256);
        expect(WorldPreview.exploredOf(p).SequenceEqual(WorldPreview.explored(state))).toBe(true);
        expect(WorldPreview.exploredOf(p).Count((b) => b == 1) > 0).toBe(true);
        var you = state.survivors[0].pos;
        expect(p.you.x).toBe(you.x);
        expect(p.you.y).toBe(you.y);
        // The file still loads as before.
        var loaded = Savefile.loadFile(text);
        expect(loaded.ok).toBe(true);
        expect(loaded.state.world.seed).toBe(9);
    }

    [Fact(DisplayName = "world preview > none from a hand-made map, a bug report or something else")]
    public void no_preview()
    {
        var map = States.createState(new NewStateOptions { seed = 1, width = 20, height = 20 });
        expect(Savefile.readPreview(Encoding.UTF8.GetBytes(Savefile.toSaveFile(map)))).toBe(null);
        var world = Worldgen.createWorld(2, 128);
        expect(Savefile.readPreview(Encoding.UTF8.GetBytes(Savefile.toBugReport(world, world)))).toBe(null);
        expect(Savefile.readPreview(Encoding.UTF8.GetBytes("not json"))).toBe(null);
        expect(Savefile.readPreview(Encoding.UTF8.GetBytes("[1, 2]"))).toBe(null);
    }
}

// Their own classes so ./check.sh can run them in other processes beside the one above (each takes seconds: a picture of a
// big world samples its land, and a region's plan is about a second).
public class WorldPreviewThreadTest
{
    [Fact(DisplayName = "world preview > the same picture on a worker thread, in stages, and stoppable")]
    public async System.Threading.Tasks.Task same_on_a_thread()
    {
        // 1088: 2 x 2 regions as 1280 was, the east and south ones 64 tiles wide (a full region's plan is about a second).
        // The worker starts first and this thread builds its own meanwhile (two second-long builds side by side: the Terrain
        // cache they share is built under a lock, and each has its own WorldRegions).
        var stages = new List<string>();
        var worker = System.Threading.Tasks.Task.Run(() => WorldPreview.build(new WorldRegions(11, 1088, "mainland"), (_, what) => stages.Add(what)));
        var here = WorldPreview.build(new WorldRegions(11, 1088, "mainland"));
        var there = await worker;
        expect(there.SequenceEqual(here)).toBe(true);
        expect(stages[0]).toBe("the land");
        expect(stages[1]).toBe("towns");
        expect(stages[^1]).toBe("done");
        expect(stages.Count).toBe(2 + 4); // 1088: 2 x 2 regions, every one planned
        // Stopping is asked before any region is touched, whatever the size (a build of the big one costs a second and a half).
        expect(WorldPreview.build(new WorldRegions(11, 256, "mainland"), null, () => true)).toBe(null);
        var asked = 0;
        expect(WorldPreview.build(new WorldRegions(11, 256, "mainland"), null, () => ++asked == 2)).toBe(null); // after the first region's towns
        expect(asked).toBe(2);
    }
}

public class WorldPreviewBigTest
{
    [Fact(DisplayName = "world preview > a big world shows every region's towns, nearest the start first, and plans none")]
    public void big_world_towns_only()
    {
        var r = new WorldRegions(5, 2112, "mainland"); // 3 x 3 regions (the last 64 tiles wide: towns are sited in each, a quarter second a full one)
        expect(WorldPreview.plansRegions(r)).toBe(false);
        expect(WorldPreview.regionOrder(r)[0]).toEqual(r.RegionOf(r.nature.start.x, r.nature.start.y));
        var stages = new List<string>();
        var img = WorldPreview.build(r, (_, what) => stages.Add(what));
        expect(string.Join(", ", stages)).toBe("the land, towns (6 of 9 regions), done");
        expect(img.Count((k) => k == WorldPreview.TOWN) > 0).toBe(true);
        expect(img.Count((k) => k == WorldPreview.ROAD || k == WorldPreview.STREET)).toBe(0);
        for (int ry = 0; ry < r.side; ry++)
            for (int rx = 0; rx < r.side; rx++) expect(r.IsPlanned(rx, ry)).toBe(false);
    }

    [Fact(DisplayName = "world preview > a coarse pixel is the most of nine samples across it")]
    public void coarse_pixels_vote()
    {
        var n = NatureMod.natureFor(5, 2048, "mainland");
        var img = WorldPreview.nature(n, WorldPreview.PX); // 8 tiles a pixel
        int x0 = 100 * 8, y0 = 37 * 8;
        var votes = new int[3];
        for (int b = 0; b < 3; b++)
            for (int a = 0; a < 3; a++)
            {
                int x = x0 + (2 * a + 1) * 8 / 6, y = y0 + (2 * b + 1) * 8 / 6;
                votes[NatureMod.isWater(n, x, y) ? 2 : NatureMod.isForest(n, x, y) ? 1 : 0]++;
            }
        expect(votes[img[37 * WorldPreview.PX + 100]] >= votes.Max()).toBe(true);
    }
}
