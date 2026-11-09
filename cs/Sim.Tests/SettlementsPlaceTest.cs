// Where the settlements are put when a world is made (Settlements.placeSettlements).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SettlementsPlaceTest
{
    [Fact(DisplayName = "settlements > every one stands on land, clear of the edge and the fence, on every kind of map")]
    public void on_land()
    {
        // 2026-11-09: island maps put some out at sea (the map overviews showed them in the water).
        foreach (var map in new[] { "mainland", "island", "islands", "quarantine" })
            foreach (var size in new[] { 128, 256, 512 })
                for (int seed = 1; seed <= 8; seed++)
                {
                    var settings = Js.Clone(Difficulty.NORMAL);
                    settings.map = map;
                    var state = Worldgen.createWorld(seed, size, settings);
                    var nature = Worldplan.planWorld(seed, size, map).nature;
                    expect(state.settlements.Count).toBe(3);
                    foreach (var s in state.settlements)
                    {
                        string where = $"{map} {size} seed {seed}: {s.name} at ({s.x}, {s.y})";
                        expect(NatureMod.isWater(nature, s.x, s.y) ? where + " is in water" : "").toBe("");
                        expect(NatureMod.onFenceLine(nature, s.x, s.y) ? where + " is on the fence" : "").toBe("");
                        bool clear = s.x >= Settlements.EDGE_MARGIN && s.y >= Settlements.EDGE_MARGIN && s.x < size - Settlements.EDGE_MARGIN && s.y < size - Settlements.EDGE_MARGIN;
                        expect(clear ? "" : where + " is at the edge").toBe("");
                    }
                }
    }
}
