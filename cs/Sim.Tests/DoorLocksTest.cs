// Locks on doors (2026-11-12, Doors.cs; from the second quality pass's list): a locked edge door stays shut to
// E as a locked tile door does, a wide door locks as one, and a tile door is not locked on someone standing in it.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class DoorLocksTest
{
    [Fact(DisplayName = "door locks > a locked edge door does not open, and opens again unlocked")]
    public void Locked_edge_door()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...", "...."] });
        var g = state.grid;
        Grids.setEdge(g, 1, 0, "w", Grids.EDGE_DOOR);
        int e = Grids.edgeBetween(g, 0, 0, 1, 0);
        expect(Commands.applyCommand(state, new Command { type = "toggleLock", x = 1, y = 0, side = "w" }).ok).toBe(true);
        expect(g.edgeLocked.includes(e)).toBe(true);
        expect(Doors.toggleEdgeDoor(state, new Point(0, 0), new Point(1, 0))).toBe("it's locked");
        expect(Grids.edgeIsOpen(g, e)).toBe(false);
        Commands.applyCommand(state, new Command { type = "toggleLock", x = 1, y = 0, side = "w" });
        expect(Doors.toggleEdgeDoor(state, new Point(0, 0), new Point(1, 0))).toBe("opened the door");
        expect(Grids.edgeIsOpen(g, e)).toBe(true);
        // Locking an open one shuts it.
        Commands.applyCommand(state, new Command { type = "toggleLock", x = 1, y = 0, side = "w" });
        expect(Grids.edgeIsOpen(g, e)).toBe(false);
    }

    [Fact(DisplayName = "door locks > a wide door locks and unlocks as one")]
    public void Wide_door()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s......", "......."] });
        var g = state.grid;
        for (int x = 1; x <= 3; x++) Grids.setEdge(g, x, 1, "n", Grids.EDGE_DOOR);
        int E(int x) => Grids.edgeBetween(g, x, 0, x, 1);
        Doors.toggleEdgeDoor(state, new Point(2, 0), new Point(2, 1));
        Commands.applyCommand(state, new Command { type = "toggleLock", x = 3, y = 1, side = "n" });
        foreach (var x in new[] { 1, 2, 3 })
        {
            expect(g.edgeLocked.includes(E(x)), $"door {x} locked").toBe(true);
            expect(Grids.edgeIsOpen(g, E(x)), $"door {x} open").toBe(false);
        }
        expect(Doors.toggleEdgeDoor(state, new Point(1, 0), new Point(1, 1))).toBe("it's locked");
        Commands.applyCommand(state, new Command { type = "toggleLock", x = 1, y = 1, side = "n" });
        foreach (var x in new[] { 1, 2, 3 }) expect(g.edgeLocked.includes(E(x)), $"door {x} locked").toBe(false);
    }

    [Fact(DisplayName = "door locks > a tile door is not locked on someone standing in it")]
    public void Someone_in_the_doorway()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...", "...."] });
        var g = state.grid;
        Grids.setTile(g, 0, 0, "door");
        g.open.push(0);
        var r = Commands.applyCommand(state, new Command { type = "toggleLock", x = 0, y = 0 });
        expect(r.ok).toBe(false);
        expect(r.reason).toBe("someone is in the doorway");
        expect(g.locked.includes(0)).toBe(false);
        expect(g.open.includes(0)).toBe(true);
        // Stepped out of it, it locks.
        var s = state.survivors[0];
        s.pos = new Point(1, 1);
        s.at = Motion.centre(s.pos);
        expect(Commands.applyCommand(state, new Command { type = "toggleLock", x = 0, y = 0 }).ok).toBe(true);
        expect(g.locked.includes(0)).toBe(true);
    }
}
