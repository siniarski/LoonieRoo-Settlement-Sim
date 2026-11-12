// Ported from tests/water.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class WaterTest
{


    public static SimState setup(List<string> map, JsRecord<double>? jobs = null, List<ScenarioRoomsItem>? rooms = null)
    {
        jobs ??= new() {  };
        rooms ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 11, map = map, rooms = rooms });
        state.harsh = true;
        TestKit.setJobs(state, jobs);
        return state;
    }

    /// <summary>thirst</summary>
    public class D001_thirst
    {

        [Fact(DisplayName = "thirst > only drains in a harsh game")]
        public void T002_only_drains_in_a_harsh_game()
        {
            var state = setup(["s.."]);
            state.harsh = false;
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.survivors, 0)).hydration).toBe(Water.HYDRATION_MAX);
        }

        [Fact(DisplayName = "thirst > a thirsty survivor drops their job and drinks at a well before the river")]
        public void T003_a_thirsty_survivor_drops_their_job_and_drinks_at_a_well_befo()
        {
            var state = setup(["%.......s.....W", "..............."], new() { ["clean"] = 3 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.hydration = Water.THIRSTY - 1;
            runUntil(state, (_) => s.hydration >= (Water.HYDRATION_MAX - 1), 400);
            expect(s.pos.x).toBeGreaterThan(10);
        }

        [Fact(DisplayName = "thirst > drinks from the river when there is nothing cleaner")]
        public void T004_drinks_from_the_river_when_there_is_nothing_cleaner()
        {
            var state = setup(["%.....s"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.hydration = 10;
            runUntil(state, (_) => s.hydration >= (Water.HYDRATION_MAX - 1), 400);
            expect(s.pos.x).toBe(1);
        }

        [Fact(DisplayName = "thirst > rain collectors fill in the rain and run dry when drunk from")]
        public void T005_rain_collectors_fill_in_the_rain_and_run_dry_when_drunk_from()
        {
            var state = setup(["s.R"]);
            var c = Js.NN(Js.Get(state.furniture, 0));
            expect(c.loot).toBe(0);
            state.weather = "rain";
            state.tick = Js.I(((double)(Water.COLLECTOR_FILL_EVERY) * (10)) - 1);
            for (var i = 0; i < (((double)(Water.COLLECTOR_FILL_EVERY) * (2)) + 1); i++)
            {
                Tick.tick(state);
            }
            expect(c.loot).toBe(2);
            state.weather = "clear";
            Js.NN(Js.Get(state.survivors, 0)).hydration = 1;
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).hydration > 1, 200);
            expect(c.loot).toBe(1);
        }

        [Fact(DisplayName = "thirst > a new world camp has a rain collector with water in it")]
        public void T006_a_new_world_camp_has_a_rain_collector_with_water_in_it()
        {
            var state = Worldgen.createWorld(3, 64);
            expect(state.harsh).toBe(true);
            expect(state.furniture.find((f) => f.kind == "collector")?.loot).toBe(Worldgen.START_WATER);
        }
    }

    /// <summary>spoilage</summary>
    public class D007_spoilage
    {

        [Fact(DisplayName = "spoilage > food rots in time in a harsh game, twice as slow in storage, not in winter")]
        public void T008_food_rots_in_time_in_a_harsh_game_twice_as_slow_in_storage_n()
        {
            List<string> map = ["f.......", "####D###", "#f.....#", "#......#", "########"];
            var state = setup(map, new() {  }, [new() { x = Js.I(1), y = Js.I(3), purpose = "storage" }]);
            state.survivors = [];
            for (var i = 0; i < (((double)(Power.FOOD_SHELF) / (Power.SPOIL_CHECK_EVERY)) + 1); i++)
            {
                state.tick = Js.I(((double)((i + 1)) * (Power.SPOIL_CHECK_EVERY)));
                Tick.tick(state);
            }
            expect(state.items.map((i) => i.pos)).toEqual(Js.Arr([Js.Obj(("x", (object?)(1)), ("y", (object?)(2)))]));
            var winter = setup(["f.."]);
            winter.tick = Js.I(((double)(((double)(3) * (Seasons.SEASON_DAYS))) * (Time.DAY_LENGTH)));
            for (var i = 0; i < 10; i++)
            {
                winter.tick = Js.I(winter.tick + (Power.SPOIL_CHECK_EVERY - 1));
                Tick.tick(winter);
            }
            expect(Js.NN(Js.Get(winter.items, 0)).wear).toBe(0);
        }

        [Fact(DisplayName = "spoilage > a powered fridge keeps food round it")]
        public void T009_a_powered_fridge_keeps_food_round_it()
        {
            var state = setup(["f.F.E", "w...."]);
            state.survivors = [];
            Js.NN(state.furniture.find((f) => f.kind == "generator")).stored = 2;
            for (var i = 0; i < 20; i++)
            {
                state.tick = Js.I(((double)((i + 1)) * (Power.SPOIL_CHECK_EVERY)));
                Tick.tick(state);
            }
            expect(Js.NN(state.items.find((i) => i.kind == "food")).wear).toBe(0);
        }
    }

    /// <summary>power</summary>
    public class D010_power
    {

        [Fact(DisplayName = "power > haulers fuel a generator; it burns wood while lamps need it at night")]
        public void T011_haulers_fuel_a_generator_it_burns_wood_while_lamps_need_it_a()
        {
            var state = setup(["s.E.l", "wwww.", "ww..."], new() { ["haul"] = 3 });
            var g = Js.NN(state.furniture.find((f) => f.kind == "generator"));
            runUntil(state, (_) => g.stored == Power.FUEL_MAX, 2000);
            expect(Power.isPowered(state, new Point { x = Js.I(4), y = Js.I(0) })).toBe(true);
            expect(Power.litLamps(state)).toEqual(Js.Arr([]));
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
            state.tick = Js.I(((double)(15) * (Time.TICKS_PER_HOUR)) + 1);
            state.zombiesTonight = 0;
            expect(Power.litLamps(state)).toHaveLength(1);
            for (var i = 0; i < Power.FUEL_TICKS; i++)
            {
                Tick.tick(state);
            }
            expect(g.stored).toBe(Power.FUEL_MAX - 1);
        }

        [Fact(DisplayName = "power > night zombies keep away from a lit lamp")]
        public void T012_night_zombies_keep_away_from_a_lit_lamp()
        {
            int arrivals(double fuel)
            {
                var state = setup([(("s" + ".".repeat(19)) + "l.E") + ".".repeat(7)]);
                state.harsh = false;
                Js.NN(state.furniture.find((f) => f.kind == "generator")).stored = fuel;
                state.tick = Js.I(((double)(15) * (Time.TICKS_PER_HOUR)));
                for (var i = 0; i < ((double)(9) * (Time.TICKS_PER_HOUR)); i++)
                {
                    Tick.tick(state);
                }
                return state.events.filter((e) => e.message.includes("appeared")).length;
            }
            expect(20 - Power.LAMP_DETER).toBeLessThan(8);
            expect(arrivals(0)).toBeGreaterThan(0);
            expect(arrivals(Power.FUEL_MAX)).toBe(0);
        }
    }
}
