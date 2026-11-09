// Tree trunks are solid to free movers (Trunks.cs, Motion.slide with the state).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class TrunksTest
{
    static readonly double REACH = Motion.BODY_RADIUS + Trunks.RADIUS;

    static double clearance(Point at, Point trunk) => JsMath.sqrt(Motion.distance2(at, trunk)) - REACH;

    /// <summary>Walk a body from `from` by (dx, dy) per tick for n ticks, sliding; the closest it came to the trunk (negative: inside it).</summary>
    static (Point at, double closest) walk(SimState state, Point from, int dx, int dy, int n, Point trunk)
    {
        var at = from;
        var closest = double.PositiveInfinity;
        for (var i = 0; i < n; i++)
        {
            at = Motion.slide(state, at, dx, dy, Pathfinding.FREE);
            closest = JsMath.min(closest, clearance(at, trunk));
        }
        return (at, closest);
    }

    [Fact(DisplayName = "trunks > the trunk stands within JITTER of its tile's centre, the same every time")]
    public void within_jitter()
    {
        for (var x = -20; x < 20; x++)
            for (var y = 0; y < 20; y++)
            {
                var c = Trunks.trunkAt(x, y);
                expect(JsMath.abs(c.x - Motion.centre(new Point { x = x, y = y }).x)).toBeLessThanOrEqual(Trunks.JITTER);
                expect(JsMath.abs(c.y - Motion.centre(new Point { x = x, y = y }).y)).toBeLessThanOrEqual(Trunks.JITTER);
                expect(Trunks.trunkAt(x, y).x).toBe(c.x);
            }
    }

    [Fact(DisplayName = "trunks > walking straight into a tree never enters the trunk, and a glancing walk slides round it and on")]
    public void walk_into_a_tree()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".......", ".......", "...Y...", ".......", "......."] });
        var trunk = Trunks.trunkAt(3, 2);
        // Dead on at the trunk's centre, from two tiles west.
        var (at, closest) = walk(state, new Point { x = trunk.x - 512, y = trunk.y }, 20, 0, 60, trunk);
        expect(closest).toBeGreaterThanOrEqual(-1);
        // A glancing walk, a third of the way off its centre: round it and out the far side, near the line it was on.
        (at, closest) = walk(state, new Point { x = trunk.x - 512, y = trunk.y + 40 }, 20, 0, 60, trunk);
        expect(closest).toBeGreaterThanOrEqual(-1);
        expect(at.x).toBeGreaterThan(trunk.x + 256);
        expect(JsMath.abs(at.y - trunk.y)).toBeLessThanOrEqual(REACH + 2);
        // Without the state (walls only) it walks straight through, as before.
        var through = Motion.slide(state.grid, new Point { x = trunk.x - 512, y = trunk.y }, 1024, 0, Pathfinding.FREE);
        expect(through.y).toBe(trunk.y);
    }

    [Fact(DisplayName = "trunks > you pass between two trees with a tile between them")]
    public void between_two_trees()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", ".....", ".Y.Y.", ".....", "....."] });
        var start = Motion.centre(new Point { x = 2, y = 4 });
        var at = start;
        for (var i = 0; i < 60; i++)
        {
            at = Motion.slide(state, at, 0, -20, Pathfinding.FREE);
        }
        expect(Motion.tileOf(at).y).toBe(0);
        expect(clearance(at, Trunks.trunkAt(1, 2))).toBeGreaterThanOrEqual(-1);
    }

    [Fact(DisplayName = "trunks > you, steering into a tree, stop at its trunk; with the tree felled you walk on")]
    public void you_steer_into_a_tree()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s..Y...", "......."] });
        var you = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        you.at = new Point { x = 128, y = Trunks.trunkAt(3, 0).y };
        Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
        var trunk = Trunks.trunkAt(3, 0);
        var closest = double.PositiveInfinity;
        for (var t = 0; t < 60; t++)
        {
            Tick.tick(state);
            closest = JsMath.min(closest, clearance(you.at, trunk));
        }
        expect(closest).toBeGreaterThanOrEqual(-1);
        FurnitureMod.removeFurniture(state, Js.NN(FurnitureMod.furnitureAt(state, 3, 0)));
        for (var t = 0; t < 80; t++)
        {
            Tick.tick(state);
        }
        expect(you.pos.x).toBeGreaterThanOrEqual(4);
    }

    [Fact(DisplayName = "trunks > a zombie walking its path through a wood gets where it's going")]
    public void a_zombie_through_a_wood()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [
            "s.........",
            ".Y.Y.Y.Y..",
            "..Y.Y.Y.Y.",
            ".Y.Y.Y.Y..",
            "..Y.Y.Y.Y.",
            "..........",
        ] });
        var you = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        Commands.applyCommand(state, new Command { type = "spawnZombie", x = 9, y = 5 });
        var z = Js.NN(Js.Get(state.zombies, 0));
        z.kind = "static";
        var full = you.health;
        for (var t = 0; t < 600 && you.health >= full; t++)
        {
            Tick.tick(state);
            foreach (var f in Js.Iter(state.furniture))
                if (f.kind == "tree") expect(clearance(z.at, Trunks.trunkAt(f.x, f.y))).toBeGreaterThanOrEqual(-1);
        }
        expect(you.health).toBeLessThan(full);
    }
}
