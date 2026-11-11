// The dead and animals wade the shallows (2026-11-12): water with land beside it is theirs at half pace; deep water
// still stops them.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class WadingTest
{
    /// <summary>A field 20 x 5 with a stream of `wide` tiles running north-south from x = 8.</summary>
    static SimState stream(int wide)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = Enumerable.Repeat("s" + ".".repeat(19), 5).Select((r, i) => i == 2 ? r : ".".repeat(20)).ToList() });
        for (int y = 0; y < 5; y++) for (int x = 8; x < 8 + wide; x++) Grids.setTile(state.grid, x, y, "water");
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        return state;
    }

    [Fact(DisplayName = "wading > a stream two tiles wide is all shallows: the dead and animals may stand in it, crawlers and cars may not")]
    public void Shallows()
    {
        var g = stream(2).grid;
        foreach (var mover in new[] { Pathfinding.ZOMBIE, Pathfinding.RUNNER, Pathfinding.ANIMAL })
            expect(Pathfinding.isPassable(g, 8, 2, mover), mover).toBe(true);
        foreach (var mover in new[] { Pathfinding.CRAWLER, Pathfinding.VEHICLE, Pathfinding.SURVIVOR, Pathfinding.RAIDER })
            expect(Pathfinding.isPassable(g, 8, 2, mover), mover).toBe(false);
        expect(Pathfinding.findPathToAny(g, new Point { x = 12, y = 2 }, [new Point { x = 2, y = 2 }], Pathfinding.ZOMBIE) != null).toBe(true);
    }

    [Fact(DisplayName = "wading > a river three tiles wide has deep water in the middle, and nobody but you crosses it")]
    public void Deep()
    {
        var g = stream(3).grid;
        expect(Swim.depth(g, 9, 2)).toBe(Swim.DEEP);
        expect(Pathfinding.isPassable(g, 9, 2, Pathfinding.ZOMBIE)).toBe(false);
        expect(Pathfinding.isPassable(g, 9, 2, Pathfinding.ANIMAL)).toBe(false);
        expect(Pathfinding.findPathToAny(g, new Point { x = 14, y = 2 }, [new Point { x = 2, y = 2 }], Pathfinding.ZOMBIE)).toBeNull();
    }

    [Fact(DisplayName = "wading > one of the dead comes across the stream for you, slower in the water")]
    public void Comes_across()
    {
        var state = stream(2);
        var you = state.survivors[0];
        var z = Zombies.addZombie(state, 12, 2, 200, "static");
        z.targetId = you.id;
        expect(Swim.waded(state.grid, new Point { x = 8, y = 2 }, 40)).toBe(20);
        expect(Swim.waded(state.grid, new Point { x = 12, y = 2 }, 40)).toBe(40);
        bool wet = false;
        for (int t = 0; t < 600 && z.pos.x > 6 && state.zombies.includes(z); t++)
        {
            Tick.tick(state);
            wet |= Swim.depth(state.grid, z.pos) == Swim.SHALLOW;
        }
        expect(wet).toBe(true);
        expect(z.pos.x).toBeLessThanOrEqual(7);
    }
}
