// Boarding up an edge door by hand (2026-11-12, Handiwork.cs): R with wood at a door in a wall nails boards across
// it. You and yours still get by; the dead and raiders batter the boards off before the door.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class BoardedDoorTest
{
    /// <summary>You at 0,0 with a plank in your bag, an edge door between you and 1,0.</summary>
    static (SimState state, Survivor you, int e) room()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s....", "w...."] });
        Grids.setEdge(state.grid, 1, 0, "w", Grids.EDGE_DOOR);
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        expect(Inventory.stow(state, you, state.items.find((i) => i.kind == "wood"))).toBeNull();
        return (state, you, Grids.edgeBetween(state.grid, 0, 0, 1, 0));
    }

    [Fact(DisplayName = "boarded door > R with wood at a door in a wall boards it up: you still get by, the dead don't")]
    public void Boards_up()
    {
        var (state, you, e) = room();
        var g = state.grid;
        expect(Grids.edgeKind(g, e)).toBe(Grids.EDGE_DOOR);
        expect(Commands.applyCommand(state, new Command { type = "work", x = 1, y = 0 }).ok).toBe(true);
        for (int i = 0; i < 40 && state.control.busy != null; i++) Tick.tick(state);
        expect(g.edgeBoarded.includes(e)).toBe(true);
        expect(state.items.some((i) => i.kind == "wood")).toBe(false); // the plank is in the door
        expect(Pathfinding.crossable(g, 0, 0, 1, 0, Pathfinding.FREE)).toBe(true);
        expect(Pathfinding.crossable(g, 0, 0, 1, 0, Pathfinding.SURVIVOR)).toBe(true);
        foreach (var mover in new[] { Pathfinding.ZOMBIE, Pathfinding.RAIDER, Pathfinding.CRAWLER, Pathfinding.ANIMAL })
            expect(Pathfinding.crossable(g, 1, 0, 0, 0, mover), mover).toBe(false);
        // Again, with no wood and with it boarded already.
        var again = Commands.applyCommand(state, new Command { type = "work", x = 1, y = 0 });
        expect(again.ok).toBe(false);
        expect(again.reason).toBe("already boarded up");
        // The boards take the battering first; the door is still there under them.
        expect(Breach.edgeHealth(g, e)).toBe(Breach.BOARDS_HEALTH);
        Breach.damageEdge(state, e, Breach.BOARDS_HEALTH);
        expect(g.edgeBoarded.includes(e)).toBe(false);
        expect(Grids.edgeKind(g, e)).toBe(Grids.EDGE_DOOR);
    }

    [Fact(DisplayName = "boarded door > without wood it says so, and nothing changes")]
    public void Needs_wood()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...."] });
        Grids.setEdge(state.grid, 1, 0, "w", Grids.EDGE_DOOR);
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = state.survivors[0].id });
        var r = Commands.applyCommand(state, new Command { type = "work", x = 1, y = 0 });
        expect(r.ok).toBe(false);
        expect(r.reason).toBe("you need wood to board up a door");
    }

    [Fact(DisplayName = "boarded door > one of the dead outside batters the boards rather than standing at them")]
    public void The_dead_batter_it()
    {
        var (state, you, e) = room();
        Commands.applyCommand(state, new Command { type = "work", x = 1, y = 0 });
        for (int i = 0; i < 40 && state.control.busy != null; i++) Tick.tick(state);
        // Up against it with somewhere to be on the far side: its step is blocked, and it hits the boards.
        var z = Zombies.addZombie(state, 1, 0, 200, "static");
        z.cooldown = 0;
        expect(Zombies.stepBlocked(state, z, new Point(0, 0))).toBe(true);
        expect(Breach.edgeDamage(state.grid, e)).toBeGreaterThan(0);
        expect(state.grid.edgeBoarded.includes(e)).toBe(true);
    }
}
