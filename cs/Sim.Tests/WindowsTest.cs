// Ported from tests/windows.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WindowsTest
{

    /// <summary>A room inside (1, 1)..(3, 3), walled on its edges, a window in the middle of its east wall.</summary>
    public static List<ScenarioEdgesItem> room(string window = "window")
    {
        List<ScenarioEdgesItem> edges = [];
        object add(double x, double y, string side, string kind = "wall")
        {
            return edges.push(new() { x = Js.I(x), y = Js.I(y), side = side, kind = kind });
        }
        for (var x = 1; x <= 3; x++)
        {
            add(x, 1, "n");
            add(x, 4, "n");
        }
        for (var y = 1; y <= 3; y++)
        {
            add(1, y, "w");
            add(4, y, "w", (y == 2) ? window : "wall");
        }
        return edges;
    }

    public static readonly List<string> MAP = [".......", ".......", ".......", ".......", "......."];

    /// <summary>windows</summary>
    public class D001_windows
    {

        [Fact(DisplayName = "windows > a swing at the glass in front of you breaks it")]
        public void T002_a_swing_at_the_glass_in_front_of_you_breaks_it()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? "...s..." : r), edges = room() });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            var pane = Grids.edgeBetween(state.grid, 3, 2, 4, 2);
            expect(Commands.applyCommand(state, new Command { type = "swing", x = Js.I(4), y = Js.I(2) }).ok).toBe(true);
            expect(Js.Missing(Grids.allEdges(state.grid), pane) ? null : (object?)(Grids.edgeKind(state.grid, pane))).toBe(Grids.EDGE_WINDOW_BROKEN);
        }

        [Fact(DisplayName = "windows > glass stops everyone walking; broken, the dead climb through, colonists use doors, you vault it")]
        public void T003_glass_stops_everyone_walking_broken_you_and_the_dead_climb_t()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room() });
            foreach (var m in Js.Iter((List<string>)["survivor", "free", "zombie", "raider"]))
            {
                expect(Pathfinding.crossable(state.grid, 3, 2, 4, 2, m), m).toBe(false);
            }
            expect(Pathfinding.crossable(state.grid, 3, 2, 4, 2, "breacher")).toBe(true);
            var broken = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room("brokenWindow") });
            expect(Pathfinding.crossable(broken.grid, 3, 2, 4, 2, "free")).toBe(false);   // Space vaults it (LeapsTest)
            expect(Pathfinding.crossable(broken.grid, 3, 2, 4, 2, "animal")).toBe(true);
            expect(Pathfinding.crossable(broken.grid, 3, 2, 4, 2, "zombie")).toBe(true);
            expect(Pathfinding.crossable(broken.grid, 3, 2, 4, 2, "survivor")).toBe(false);
        }

        [Fact(DisplayName = "windows > you see and shoot through glass, but not through boards; nothing bites through glass")]
        public void T004_you_see_and_shoot_through_glass_but_not_through_boards_nothi()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room() });
            var pane = Grids.edgeBetween(state.grid, 3, 2, 4, 2);
            expect(Pathfinding.seesThrough(state.grid, pane)).toBe(true);
            expect(Sight.clearShot(state.grid, new Point { x = Js.I(2), y = Js.I(2) }, new Point { x = Js.I(6), y = Js.I(2) })).toBe(true);
            expect(Sight.clearShot(state.grid, new Point { x = Js.I(2), y = Js.I(1) }, new Point { x = Js.I(6), y = Js.I(1) })).toBe(false);
            expect(Pathfinding.openBetween(state.grid, new Point { x = Js.I(3), y = Js.I(2) }, new Point { x = Js.I(4), y = Js.I(2) })).toBe(false);
            state.grid.edgeBoarded.push(pane);
            expect(Pathfinding.seesThrough(state.grid, pane)).toBe(false);
        }

        [Fact(DisplayName = "windows > the dead smash the glass to get at someone inside, then climb in")]
        public void T005_the_dead_smash_the_glass_to_get_at_someone_inside_then_climb()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? "..s...z" : r), edges = room() });
            var s = Js.NN(Js.Get(state.survivors, 0));
            foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
            {
                s.priorities[Js.Idx(k)] = 0;
            }
            Js.NN(Js.Get(state.zombies, 0)).kind = "static";
            var pane = Grids.edgeBetween(state.grid, 3, 2, 4, 2);
            double t = 0;
            for (; (t < 600) && (Grids.edgeKind(state.grid, pane) == Grids.EDGE_WINDOW); t++)
            {
                Tick.tick(state);
            }
            expect(Js.Missing(Grids.allEdges(state.grid), pane) ? null : (object?)(Grids.edgeKind(state.grid, pane))).toBe(Grids.EDGE_WINDOW_BROKEN);
            expect(state.events.some((e) => e.message.includes("smashed the window"))).toBe(true);
            for (var k = 0; (Js.T((k < 200) && Js.T(Js.Get(state.zombies, 0)))) && (Js.Get(state.zombies, 0).pos.x > 3); k++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.zombies, 0)).pos.x).toBeLessThanOrEqual(3);
        }

        [Fact(DisplayName = "windows > you smash a window by using it")]
        public void T006_you_smash_a_window_by_using_it()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? "...s..." : r), edges = room() });
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(4), y = Js.I(2) }).ok).toBe(true);
            expect(Js.Missing(Grids.allEdges(state.grid), Grids.edgeBetween(state.grid, 3, 2, 4, 2)) ? null : (object?)(Grids.edgeKind(state.grid, Grids.edgeBetween(state.grid, 3, 2, 4, 2)))).toBe(Grids.EDGE_WINDOW_BROKEN);
        }

        [Fact(DisplayName = "windows > boarded up, nobody gets through until the boards are torn off, then the glass")]
        public void T007_boarded_up_nobody_gets_through_until_the_boards_are_torn_off()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room() });
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(4), y = Js.I(2), structure = "barricade", side = "w" }).ok).toBe(true);
            var bp = Js.NN(Js.Get(state.blueprints, 0));
            bp.delivered = true;
            bp.workDone = bp.workRequired;
            Designations.completeBlueprint(state, bp);
            var pane = Grids.edgeBetween(state.grid, 3, 2, 4, 2);
            expect(state.grid.edgeBoarded).toContain(pane);
            expect(Js.Missing(Grids.allEdges(state.grid), pane) ? null : (object?)(Grids.edgeKind(state.grid, pane))).toBe(Grids.EDGE_WINDOW);
            expect(Breach.damageEdge(state, pane, 399)).toBe(false);
            expect(Breach.damageEdge(state, pane, 1)).toBe(true);
            expect(state.grid.edgeBoarded).not.toContain(pane);
            expect(Js.Missing(Grids.allEdges(state.grid), pane) ? null : (object?)(Grids.edgeKind(state.grid, pane))).toBe(Grids.EDGE_WINDOW);
            expect(Breach.damageEdge(state, pane, 100)).toBe(true);
            expect(Js.Missing(Grids.allEdges(state.grid), pane) ? null : (object?)(Grids.edgeKind(state.grid, pane))).toBe(Grids.EDGE_WINDOW_BROKEN);
        }

        [Fact(DisplayName = "windows > walking, you stop at a broken window; through a doorway you walk on")]
        public void T008_walking_you_stop_at_a_broken_window()
        {
            double through(string kind)
            {
                var edges = (kind == "none") ? room().filter((e) => !((e.x == 4) && (e.y == 2))) : room(kind);
                var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? "...s..." : r), edges = edges });
                var s = Js.NN(Js.Get(state.survivors, 0));
                Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
                s.at = Motion.centre(new Point { x = Js.I(3), y = Js.I(2) });
                Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
                double t = 0;
                for (; (t < 100) && (s.pos.x < 5); t++)
                {
                    Tick.tick(state);
                }
                return t;
            }
            expect(through("none")).toBeLessThan(100);
            expect(through("brokenWindow")).toBe(100);
        }

        [Fact(DisplayName = "windows > houses and shops in a generated town have windows, never beside a door")]
        public void T009_houses_and_shops_in_a_generated_town_have_windows_never_besi()
        {
            var state = Worldgen.createWorld(5, 256);
            for (var y = 0; y < 256; y += Worldgen.CHUNK)
            {
                for (var x = 0; x < 256; x += Worldgen.CHUNK)
                {
                    Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
            var windows = Grids.allEdges(state.grid).filter((k) => (k == Grids.EDGE_WINDOW) || (k == Grids.EDGE_WINDOW_BROKEN)).length;
            expect(windows).toBeGreaterThan(50);
            expect(Grids.allEdges(state.grid).filter((k) => k == Grids.EDGE_WINDOW_BROKEN).length).toBeGreaterThan(0);
        }
    }
}
