// Tall and low fences (2026-11-12): the barricade is the low fence (vaulted by you and runners, climbed by a pile-up,
// stepped over by colonists, gone under by crawlers); the tall fence (Grids.EDGE_FENCE) is over nobody's head: it is
// battered down like a wall, built from two wood, and generated yards keep the low one.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class FencesTest
{
    /// <summary>A yard 16 x 12 fenced across row 6 (every tile's north edge `kind`), you under control at (7, 2),
    /// `n` of the dead of `variant` south of it after you.</summary>
    static (SimState state, Survivor s, List<Zombie> dead) yard(int kind, int n, string variant = "walker")
    {
        var rows = new List<string>();
        for (int y = 0; y < 12; y++) rows.Add(y == 2 ? ".......s........" : "................");
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = rows });
        var s = state.survivors[0];
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities))) s.priorities[Js.Idx(k)] = 0;
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        for (int x = 0; x < 16; x++) Grids.setEdge(state.grid, x, 6, "n", kind);
        state.walkabilityVersion++;
        var dead = new List<Zombie>();
        for (int i = 0; i < n; i++)
        {
            var z = Zombies.addZombie(state, 4 + i % 8, 9 + i / 8, 200, "static");
            z.variant = variant;
            z.targetId = s.id;
            dead.Add(z);
        }
        return (state, s, dead);
    }

    [Fact(DisplayName = "fences > nobody gets over a tall fence: not you, colonists, runners, crawlers or a pile-up; the dead batter it")]
    public void Nobody_over_a_tall_fence()
    {
        var (state, s, _) = yard(Grids.EDGE_FENCE, 0);
        var g = state.grid;
        foreach (var mover in new[] { Pathfinding.SURVIVOR, Pathfinding.FREE, Pathfinding.ZOMBIE, Pathfinding.RUNNER, Pathfinding.CRAWLER, Pathfinding.RAIDER, Pathfinding.ANIMAL })
        {
            expect(Pathfinding.crossable(g, 7, 6, 7, 5, mover), mover).toBe(false);
            expect(Pathfinding.canReach(g, state.walkabilityVersion, new Point(7, 9), [new Point(7, 2)], mover), mover).toBe(false);
        }
        expect(Pathfinding.crossable(g, 7, 6, 7, 5, Pathfinding.BREACHER)).toBe(true);
        expect(Leaps.vaultLanding(g, Motion.centre(new Point(7, 6)), new Point(7, 6), new Point(0, -1)).land).toBe(null);
        expect(Pathfinding.openBetween(g, new Point(7, 6), new Point(7, 5))).toBe(false);
        expect(Breach.edgeHealth(g, Grids.edgeIndex(g, 7, 6, "n"))).toBeGreaterThan(Breach.STRUCTURE_HEALTH["barricade"]);
        // The low one, for comparison: vaulted by you, stepped over by colonists.
        Grids.setEdge(g, 3, 6, "n", Grids.EDGE_BARRICADE);
        expect(Leaps.vaultLanding(g, Motion.centre(new Point(3, 6)), new Point(3, 6), new Point(0, -1)).land).not.toBe(null);
        expect(Pathfinding.crossable(g, 3, 6, 3, 5, Pathfinding.SURVIVOR)).toBe(true);

        // A crowd of runners at a held tall fence: no vault, no pile-up, nobody over; let go, they batter it down.
        var (st, _, dead) = yard(Grids.EDGE_FENCE, 16, "runner");
        for (int i = 0; i < 4; i++) dead[i].variant = "walker";
        for (int t = 0; t < 200; t++)
        {
            Tick.tick(st);
            for (int x = 0; x < 16; x++) Breach.repairEdge(st, Grids.edgeIndex(st.grid, x, 6, "n"), 1000);
            foreach (var z in dead) expect(z.crossing, $"tick {t}").toBe(null);
            expect(Invariants.checkInvariants(st), $"tick {t}").toEqual(Js.Arr([]));
        }
        expect(dead.Count((z) => z.pos.y < 6)).toBe(0);
        bool broken = false;
        for (int t = 0; t < 600 && !broken; t++)
        {
            Tick.tick(st);
            for (int x = 0; x < 16 && !broken; x++) broken = Grids.edgeKindBetween(st.grid, x, 6, x, 5) != Grids.EDGE_FENCE;
        }
        expect(broken).toBe(true);
    }

    [Fact(DisplayName = "fences > a colonist builds a tall fence from two wood; cancelled half-way, the wood in comes back")]
    public void Built_from_two_wood()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.w.w..", ".......", "......."] });
        var s = state.survivors[0];
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities))) s.priorities[Js.Idx(k)] = 0;
        s.priorities["build"] = 1;
        s.priorities["haul"] = 2;
        expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = 3, y = 1, structure = "fence" }).ok).toBe(false);
        expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = 3, y = 1, structure = "fence", side = "n" }).ok).toBe(true);
        int wood() => state.items.Count((i) => i.kind == "wood");
        expect(wood()).toBe(2);
        var e = Grids.edgeBetween(state.grid, 3, 1, 3, 0);
        for (int t = 0; t < 900 && Grids.edgeKind(state.grid, e) != Grids.EDGE_FENCE; t++) Tick.tick(state);
        expect(Grids.edgeKind(state.grid, e)).toBe(Grids.EDGE_FENCE);
        expect(wood()).toBe(0);
        expect(state.blueprints).toHaveLength(0);

        // One load in, then cancelled: that one comes back.
        var bp = Designations.addBlueprint(state, 5, 1, "fence", "n");
        Designations.deliver(bp);
        expect(bp.delivered).toBe(false);
        expect(bp.got).toBe(1);
        Designations.cancelBlueprint(state, bp);
        expect(wood()).toBe(1);
        // A barricade's blueprint is as it was: one load, nothing counted.
        var low = Designations.addBlueprint(state, 6, 1, "barricade", "n");
        Designations.deliver(low);
        expect(low.delivered).toBe(true);
        expect(low.got).toBe(null);
    }

    [Fact(DisplayName = "fences > generated yards keep low fences: a generated town has barricade edges and no tall fence")]
    public void Generated_yards_are_low()
    {
        var state = Worldgen.createWorld(12, 128);
        for (int y = 0; y < 128; y += Worldgen.CHUNK)
            for (int x = 0; x < 128; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
        var kinds = Grids.allEdges(state.grid); // the kind of every edge
        expect(kinds.Count((k) => k == Grids.EDGE_BARRICADE)).toBeGreaterThan(0);
        expect(kinds.Count((k) => k == Grids.EDGE_FENCE)).toBe(0);
    }
}
