// Doors side by side in one wall are one wide door (2026-11-12, Doors.cs): a garage's. They open and shut together.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class WideDoorTest
{
    [Fact(DisplayName = "wide door > three doors in a row open and shut as one; a door on its own is its own")]
    public void Together()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s......", "......."] });
        var g = state.grid;
        for (int x = 1; x <= 3; x++) Grids.setEdge(g, x, 1, "n", Grids.EDGE_DOOR);
        Grids.setEdge(g, 5, 1, "n", Grids.EDGE_DOOR);
        int E(int x) => Grids.edgeBetween(g, x, 0, x, 1);
        expect(Doors.wideDoor(g, E(2)).Count).toBe(3);
        expect(Doors.wideDoor(g, E(5)).Count).toBe(1);
        expect(Doors.toggleEdgeDoor(state, new Point(2, 0), new Point(2, 1))).toBe("rolled the wide door up");
        foreach (var x in new[] { 1, 2, 3 }) expect(Grids.edgeIsOpen(g, E(x)), $"door {x}").toBe(true);
        expect(Grids.edgeIsOpen(g, E(5))).toBe(false);
        expect(Doors.toggleEdgeDoor(state, new Point(1, 0), new Point(1, 1))).toBe("pulled the wide door down");
        foreach (var x in new[] { 1, 2, 3 }) expect(Grids.edgeIsOpen(g, E(x)), $"door {x}").toBe(false);
        expect(Doors.toggleEdgeDoor(state, new Point(5, 0), new Point(5, 1))).toBe("opened the door");
    }

    [Fact(DisplayName = "wide door > a town's garages have one: a run of two or three doors in a wall")]
    public void Generated()
    {
        var state = Worldgen.createWorld(42, 256);
        for (var y = 0; y < 256; y += Worldgen.CHUNK) for (var x = 0; x < 256; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point { x = x, y = y });
        var g = state.grid;
        int wide = 0;
        for (int e = 0; e < g.width * g.height * 2; e++)
        {
            if (Grids.edgeKind(g, e) != Grids.EDGE_DOOR) continue;
            var run = Doors.wideDoor(g, e);
            if (run.Count >= 2 && run.Min() == e) { wide++; expect(run.Select((d) => Grids.edgeIsOpen(g, d)).Distinct().Count(), "a wide door is open or shut as one").toBe(1); }
        }
        expect(wide).toBeGreaterThan(0);
    }
}
