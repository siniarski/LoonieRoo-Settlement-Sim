// Ported from tests/wanderers.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WanderersTest
{

    /// <summary>The tick at which a given day and hour begin (tick 0 is day 1, 06:00).</summary>
    public static double at(double day, double hour)
    {
        return ((double)((day - 1)) * (Time.DAY_LENGTH)) + ((double)((hour - Time.START_HOUR)) * (Time.TICKS_PER_HOUR));
    }

    public static readonly List<string> OPEN = ["........", "...s....", "........", "........"];

    public static void runTo(SimState state, double target)
    {
        while (state.tick < target)
        {
            Tick.tick(state);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    public static SimState setup(List<string>? map = null, double seed = 3)
    {
        map ??= OPEN;
        var state = ScenarioMod.loadScenario(new Scenario { seed = seed, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.DEFAULT_PRIORITIES) { ["build"] = 0, ["clean"] = 0 };
        }
        return state;
    }

    public static /*TODO return type*/object lastEvent(SimState state)
    {
        return state.events.at(-1)?.message;
    }

    /// <summary>wanderers</summary>
    public class D001_wanderers
    {

        [Fact(DisplayName = "wanderers > nobody turns up on day 1")]
        public void T002_nobody_turns_up_on_day_1()
        {
            var state = setup();
            state.tick = Js.I(at(1, Wanderers.ARRIVAL_HOUR) - 1);
            runTo(state, at(1, Wanderers.ARRIVAL_HOUR) + 5);
            expect(state.wanderers).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "wanderers > from day 2, a wanderer arrives at 10:00 on an outdoor edge tile and asks to join")]
        public void T003_from_day_2_a_wanderer_arrives_at_10_00_on_an_outdoor_edge_ti()
        {
            var state = setup();
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR) - 1);
            runTo(state, at(2, Wanderers.ARRIVAL_HOUR) + 1);
            expect(state.wanderers).toHaveLength(1);
            var w = Js.NN(Js.Get(state.wanderers, 0));
            expect((((w.x == 0) || (w.y == 0)) || (w.x == 7)) || (w.y == 3)).toBe(true);
            expect(lastEvent(state)).toBe($"{Js.Str(w.name)}, a wanderer, is waiting at ({Js.Str(w.x)}, {Js.Str(w.y)}): take them in?");
        }

        [Fact(DisplayName = "wanderers > arrives on edge floor, never in a wall")]
        public void T004_arrives_on_edge_floor_never_in_a_wall()
        {
            List<string> map = ["#####...", "#..s#...", "#...#...", "#####..."];
            foreach (var seed in Js.Iter((List<double>)[1, 2, 3, 4, 5, 6]))
            {
                var state = setup(map, seed);
                state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
                Tick.tick(state);
                var w = Js.NN(Js.Get(state.wanderers, 0));
                expect(w.x, $"seed {Js.Str(seed)}").toBeGreaterThanOrEqual(5);
            }
        }

        [Fact(DisplayName = "wanderers > the same seed brings the same wanderer to the same spot")]
        public void T005_the_same_seed_brings_the_same_wanderer_to_the_same_spot()
        {
            /*TODO return type*/object where()
            {
                var state = setup();
                state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
                Tick.tick(state);
                return Js.Json_stringify(state.wanderers);
            }
            expect(where()).toBe(where());
        }

        [Fact(DisplayName = "wanderers > a second wanderer comes at 14:00 only if there is a spare bed")]
        public void T006_a_second_wanderer_comes_at_14_00_only_if_there_is_a_spare_be()
        {
            int count(List<string> map)
            {
                var state = setup(map);
                state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
                Tick.tick(state);
                Commands.applyCommand(state, new Command { type = "dismissWanderer", wandererId = Js.NN(Js.Get(state.wanderers, 0)).id });
                state.tick = Js.I(at(2, Wanderers.SECOND_ARRIVAL_HOUR));
                Tick.tick(state);
                return state.wanderers.length;
            }
            expect(count(OPEN)).toBe(0);
            expect(count(["........", ".b.s.b..", "........", "........"])).toBe(1);
        }

        [Fact(DisplayName = "wanderers > taking a wanderer in makes them a survivor where they stood")]
        public void T007_taking_a_wanderer_in_makes_them_a_survivor_where_they_stood()
        {
            var state = setup();
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
            Tick.tick(state);
            var w = Js.NN(Js.Get(state.wanderers, 0));
            expect(Commands.applyCommand(state, new Command { type = "acceptWanderer", wandererId = w.id }).ok).toBe(true);
            expect(state.wanderers).toEqual(Js.Arr([]));
            var joined = Js.NN(state.survivors.at(-1));
            expect(joined.name).toBe(w.name);
            expect(joined.pos).toEqual(Js.Obj(("x", (object?)(w.x)), ("y", (object?)(w.y))));
            expect(joined.priorities).toEqual(JobTypes.DEFAULT_PRIORITIES);
            expect(lastEvent(state)).toBe($"{Js.Str(w.name)} joined the colony");
            expect(state.commandLog.at(-1)?.command).toEqual(Js.Obj(("type", (object?)("acceptWanderer")), ("wandererId", (object?)(w.id))));
        }

        [Fact(DisplayName = "wanderers > turning a wanderer away removes them")]
        public void T008_turning_a_wanderer_away_removes_them()
        {
            var state = setup();
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
            Tick.tick(state);
            var w = Js.NN(Js.Get(state.wanderers, 0));
            Commands.applyCommand(state, new Command { type = "dismissWanderer", wandererId = w.id });
            expect(state.wanderers).toEqual(Js.Arr([]));
            expect(state.survivors).toHaveLength(1);
            expect(lastEvent(state)).toBe($"{Js.Str(w.name)} was turned away");
        }

        [Fact(DisplayName = "wanderers > commands for a wanderer who is not there are rejected")]
        public void T009_commands_for_a_wanderer_who_is_not_there_are_rejected()
        {
            var state = setup();
            expect(Commands.applyCommand(state, new Command { type = "acceptWanderer", wandererId = "v99" }).ok).toBe(false);
            expect(state.survivors).toHaveLength(1);
        }

        [Fact(DisplayName = "wanderers > an ignored wanderer leaves after their patience runs out")]
        public void T010_an_ignored_wanderer_leaves_after_their_patience_runs_out()
        {
            var state = setup();
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
            Tick.tick(state);
            var name = Js.NN(Js.Get(state.wanderers, 0)).name;
            runTo(state, at(2, Wanderers.ARRIVAL_HOUR) + Wanderers.WANDERER_PATIENCE);
            expect(state.wanderers).toHaveLength(1);
            Tick.tick(state);
            expect(state.wanderers).toEqual(Js.Arr([]));
            expect(lastEvent(state)).toBe($"{Js.Str(name)} gave up waiting and left");
        }

        [Fact(DisplayName = "wanderers > wanderers still waiting at nightfall leave")]
        public void T011_wanderers_still_waiting_at_nightfall_leave()
        {
            var state = setup(["v.......", "...s...."]);
            Js.NN(Js.Get(state.wanderers, 0)).leavesTick = JsNumber.MAX_SAFE_INTEGER;
            state.tick = Js.I(at(1, 21) - 1);
            Tick.tick(state);
            expect(state.wanderers).toHaveLength(1);
            Tick.tick(state);
            expect(state.wanderers).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "wanderers > a wall placed on a wanderer moves them aside")]
        public void T012_a_wall_placed_on_a_wanderer_moves_them_aside()
        {
            var state = setup(["v.......", "...s...."]);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) });
            Tick.tick(state);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(Js.Missing(state.wanderers, 0) ? null : (object?)(Js.Get(state.wanderers, 0))).toMatchObject(Js.Obj(("x", (object?)(1)), ("y", (object?)(0))));
        }

        [Fact(DisplayName = "wanderers > names keep coming in order, so a joiner never reuses a starting name")]
        public void T013_names_keep_coming_in_order_so_a_joiner_never_reuses_a_starti()
        {
            var state = setup(["s.s.....", "........"]);
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR));
            Tick.tick(state);
            List<string> names = [.. state.survivors.map((s) => s.name), Js.NN(Js.Get(state.wanderers, 0)).name];
            expect(Js.Set(names).size).toBe(3);
        }
    }

    /// <summary>wanderers on a partly explored map</summary>
    public class D014_wanderers_on_a_partly_explored_map
    {

        [Fact(DisplayName = "wanderers on a partly explored map > turn up at the edge of explored ground, a short walk away, not at the map edge")]
        public void T015_turn_up_at_the_edge_of_explored_ground_a_short_walk_away_not()
        {
            var map = Js.ArrayFrom(80, (int y) => (y == 40) ? ((".".repeat(40) + "s") + ".".repeat(39)) : ".".repeat(80));
            var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map });
            foreach (var s in Js.Iter(state.survivors))
            {
                s.priorities = new(JobTypes.DEFAULT_PRIORITIES) { ["build"] = 0, ["clean"] = 0 };
            }
            Grids.fillExplored(state.grid, 0);
            state.tick = Js.I(at(2, Wanderers.ARRIVAL_HOUR) - 1);
            Tick.tick(state);
            Tick.tick(state);
            var w = Js.NN(Js.Get(state.wanderers, 0));
            bool seen(double x, double y)
            {
                return Grids.exploredAt(state.grid, ((double)(y) * (80)) + x) == 1;
            }
            expect(seen(w.x, w.y)).toBe(true);
            expect(Js.Arr([seen(w.x + 1, w.y), seen(w.x - 1, w.y), seen(w.x, w.y + 1), seen(w.x, w.y - 1)])).toContain(false);
            expect((((w.x == 0) || (w.y == 0)) || (w.x == 79)) || (w.y == 79)).toBe(false);
        }
    }
}
