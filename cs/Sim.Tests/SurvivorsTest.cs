// Ported from tests/survivors.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class SurvivorsTest
{

    /// <summary>Movement tests switch jobs off, so survivors don't wander off to clean tracked dirt.</summary>
    public static OnlyResult only(List<string> map)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 0, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) {  };
        }
        var survivor = Js.NN(Js.Get(state.survivors, 0));
        return new OnlyResult { state = state, survivor = () => Js.NN(Js.Get(state.survivors, 0)), id = survivor.id };
    }

    /// <summary>survivors</summary>
    public class D001_survivors
    {

        [Fact(DisplayName = "survivors > spawn from scenario maps with ids in reading order")]
        public void T002_spawn_from_scenario_maps_with_ids_in_reading_order()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 0, map = ["s..", "..s"] });
            expect(state.survivors.map((s) => Js.Arr([s.id, s.pos]))).toEqual(Js.Arr([Js.Arr(["s1", Js.Obj(("x", (object?)(0)), ("y", (object?)(0)))]), Js.Arr(["s2", Js.Obj(("x", (object?)(2)), ("y", (object?)(1)))])]));
            expect(DebugText.dumpText(state)).toBe("s..\n..s");
        }

        [Fact(DisplayName = "survivors > walk one tile every TICKS_PER_TILE ticks and stop at the goal")]
        public void T003_walk_one_tile_every_TICKS_PER_TILE_ticks_and_stop_at_the_goa()
        {
            var __d0 = only(["s...."]);
            var state = __d0.state;
            var survivor = __d0.survivor;
            var id = __d0.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(4), y = Js.I(0) });
            Tick.step(state, ((double)(4) * (Survivors.TICKS_PER_TILE)));
            expect(survivor().pos).toEqual(Js.Obj(("x", (object?)(3)), ("y", (object?)(0))));
            Tick.step(state, 1);
            expect(survivor().pos).toEqual(Js.Obj(("x", (object?)(4)), ("y", (object?)(0))));
            expect(survivor().goal).toBeNull();
            expect(survivor().move).toBeNull();
            expect(survivor().note).toBe("arrived at (4, 0)");
        }

        [Fact(DisplayName = "survivors > interpolation data is consistent while moving")]
        public void T004_interpolation_data_is_consistent_while_moving()
        {
            var __d1 = only(["s.."]);
            var state = __d1.state;
            var survivor = __d1.survivor;
            var id = __d1.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(2), y = Js.I(0) });
            Tick.step(state, 2);
            var move = Js.NN(survivor().move);
            expect(move.from).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(0))));
            expect(move.to).toEqual(Js.Obj(("x", (object?)(1)), ("y", (object?)(0))));
            expect(move.progress).toBe(1);
            expect(move.duration).toBe(Survivors.TICKS_PER_TILE);
        }

        [Fact(DisplayName = "survivors > repaths when a wall is placed across the route")]
        public void T005_repaths_when_a_wall_is_placed_across_the_route()
        {
            List<string> map = ["s.......", "........"];
            var __d2 = only(map);
            var state = __d2.state;
            var survivor = __d2.survivor;
            var id = __d2.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(7), y = Js.I(0) });
            Tick.step(state, 3);
            expect(Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(4), y = Js.I(0) }).ok).toBe(true);
            var notes = new JsSet<string>();
            for (var i = 0; Js.T((i < 200) && Js.T(survivor().goal)); i++)
            {
                Tick.step(state, 1);
                notes.add(survivor().note ?? "");
                expect(Grids.isWalkable(state.grid, survivor().pos.x, survivor().pos.y), DebugText.dumpText(state)).toBe(true);
            }
            expect(survivor().pos, DebugText.dumpText(state)).toEqual(Js.Obj(("x", (object?)(7)), ("y", (object?)(0))));
            expect(Js.ListOf(notes).some((n) => n.startsWith("walls changed"))).toBe(true);
        }

        [Fact(DisplayName = "survivors > gives up with a reason when the goal gets walled off, instead of getting stuck")]
        public void T006_gives_up_with_a_reason_when_the_goal_gets_walled_off_instead()
        {
            List<string> map = ["s.....", "....#."];
            var __d3 = only(map);
            var state = __d3.state;
            var survivor = __d3.survivor;
            var id = __d3.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(5), y = Js.I(1) });
            Tick.step(state, 2);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(5), y = Js.I(0) });
            Tick.step(state, ((double)(3) * (Survivors.TICKS_PER_TILE)));
            expect(survivor().goal).toBeNull();
            expect(survivor().note).toBe("no path to (5, 1)");
            Tick.step(state, Survivors.TICKS_PER_TILE);
            expect(survivor().move).toBeNull();
        }

        [Fact(DisplayName = "survivors > finishes the current step before following a new order")]
        public void T007_finishes_the_current_step_before_following_a_new_order()
        {
            var __d4 = only(["..s.."]);
            var state = __d4.state;
            var survivor = __d4.survivor;
            var id = __d4.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(4), y = Js.I(0) });
            Tick.step(state, 2);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(0), y = Js.I(0) });
            for (var i = 0; Js.T((i < 100) && Js.T(survivor().goal)); i++)
            {
                Tick.step(state, 1);
            }
            expect(survivor().pos).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(0))));
        }

        [Fact(DisplayName = "survivors > rejects walls on occupied tiles, including the tile being stepped onto")]
        public void T008_rejects_walls_on_occupied_tiles_including_the_tile_being_ste()
        {
            var __d5 = only(["s.."]);
            var state = __d5.state;
            var id = __d5.id;
            expect(Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(false)), ("reason", (object?)("(0, 0) is occupied"))));
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(2), y = Js.I(0) });
            Tick.step(state, 1);
            expect(Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(0) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "placeDoor", x = Js.I(1), y = Js.I(0) }).ok).toBe(true);
        }

        [Fact(DisplayName = "survivors > rejects bad move and spawn commands")]
        public void T009_rejects_bad_move_and_spawn_commands()
        {
            var __d6 = only(["s.#"]);
            var state = __d6.state;
            var id = __d6.id;
            expect(Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(2), y = Js.I(0) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = "nope", x = Js.I(1), y = Js.I(0) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = Js.I(0), y = Js.I(0) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = Js.I(2), y = Js.I(0) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = Js.I(1), y = Js.I(0) }).ok).toBe(true);
            expect(state.survivors.map((s) => s.id)).toEqual(Js.Arr(["s1", "s2"]));
        }

        [Fact(DisplayName = "survivors > does not log a repeated order for the same goal")]
        public void T010_does_not_log_a_repeated_order_for_the_same_goal()
        {
            var __d7 = only(["s.."]);
            var state = __d7.state;
            var id = __d7.id;
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(2), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = id, x = Js.I(2), y = Js.I(0) });
            expect(state.commandLog).toHaveLength(1);
        }
    }
}
