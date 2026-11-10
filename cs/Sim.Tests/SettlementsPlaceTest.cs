// Where the settlements are put when a world is made (Settlements.placeSettlements).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SettlementsPlaceTest
{
    // placeSettlements needs only the grid's size, the world's seed and size, and the map kind (it reads the nature the
    // same way createWorld's does), so most cases place on a bare state at the world's real start: no chunks generated
    // (createWorld on all 96 took 10 s). A few go through createWorld whole.
    static SimState Bare(int seed, int size, string map)
    {
        var state = States.createState(new NewStateOptions { seed = seed, width = size, height = size });
        state.settings = Js.Clone(Difficulty.NORMAL);
        state.settings.map = map;
        state.world = new WorldInfo { seed = seed, size = size, chunks = Math.Ceiling(size / (double)Worldgen.CHUNK) };
        Settlements.placeSettlements(state, Worldgen.layout(seed, size, map).start, seed);
        return state;
    }

    static void CheckOnLand(SimState state, string map, int size, int seed, string how)
    {
        var nature = Worldplan.regions(seed, size, map).nature;
        expect(state.settlements.Count).toBe(3);
        foreach (var s in state.settlements)
        {
            string where = $"{how} {map} {size} seed {seed}: {s.name} at ({s.x}, {s.y})";
            expect(NatureMod.isWater(nature, s.x, s.y) ? where + " is in water" : "").toBe("");
            expect(NatureMod.onFenceLine(nature, s.x, s.y) ? where + " is on the fence" : "").toBe("");
            bool clear = s.x >= Settlements.EDGE_MARGIN && s.y >= Settlements.EDGE_MARGIN && s.x < size - Settlements.EDGE_MARGIN && s.y < size - Settlements.EDGE_MARGIN;
            expect(clear ? "" : where + " is at the edge").toBe("");
        }
    }

    [Fact(DisplayName = "settlements > every one stands on land, clear of the edge and the fence, on every kind of map")]
    public void on_land()
    {
        // 2026-11-09: island maps put some out at sea (the map overviews showed them in the water).
        foreach (var map in new[] { "mainland", "island", "islands", "quarantine" })
            foreach (var size in new[] { 128, 256, 512 })
                for (int seed = 1; seed <= 8; seed++) CheckOnLand(Bare(seed, size, map), map, size, seed, "placed");
    }

    [Fact(DisplayName = "settlements > a whole generated world puts them on land too")]
    public void on_land_in_a_whole_world()
    {
        foreach (var map in new[] { "mainland", "island", "islands", "quarantine" })
            for (int seed = 1; seed <= 2; seed++)
            {
                var settings = Js.Clone(Difficulty.NORMAL);
                settings.map = map;
                var state = Worldgen.createWorld(seed, 128, settings);
                CheckOnLand(state, map, 128, seed, "created");
                // And the bare state above is the same placement (so the 96 cheap cases stand for whole worlds).
                var bare = Bare(seed, 128, map);
                for (int i = 0; i < 3; i++)
                    expect($"{map} seed {seed} #{i}: ({state.settlements[i].x}, {state.settlements[i].y})").toBe($"{map} seed {seed} #{i}: ({bare.settlements[i].x}, {bare.settlements[i].y})");
            }
    }
}
