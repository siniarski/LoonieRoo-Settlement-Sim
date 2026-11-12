// Ported from tests/messhall.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class MesshallTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 1500) => TestKit.runUntil(state, done, max);

    public static SimState setup(List<string> map, List<ScenarioRoomsItem>? rooms = null, JsRecord<double>? jobs = null)
    {
        rooms ??= new();
        jobs ??= new() {  };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = map, rooms = rooms });
        TestKit.setJobs(state, jobs);
        return state;
    }

    public static CommandResult hungry(SimState state, Survivor s)
    {
        return Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = s.id, satiety = Food.HUNGRY - 1 });
    }

    public static List<string> thoughtsOf(Survivor s)
    {
        return s.thoughts.map((t) => t.kind);
    }

    public static readonly List<string> HALL = ["#####", "#...#", "#.t.#", "#...#", "##D##", ".....", "f.s.."];

    public static readonly ScenarioRoomsItem MESS = new ScenarioRoomsItem { x = Js.I(1), y = Js.I(1), purpose = "messhall" };

    /// <summary>mess hall</summary>
    public class D001_mess_hall
    {

        [Fact(DisplayName = "mess hall > needs walls, a door, and a table")]
        public void T002_needs_walls_a_door_and_a_table()
        {
            var state = setup(["#####", "#...#", "#...#", "##D##", ".s..."], [new() { x = Js.I(1), y = Js.I(1), purpose = "messhall" }]);
            var status = Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0)));
            expect(status.requirements.filter((r) => !r.met).map((r) => r.label)).toEqual(Js.Arr(["at least 1 table"]));
            Commands.applyCommand(state, new Command { type = "placeTable", x = Js.I(2), y = Js.I(1) });
            expect(Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0))).complete).toBe(true);
        }

        [Fact(DisplayName = "mess hall > a hungry survivor carries food to a seat by the table and eats there")]
        public void T003_a_hungry_survivor_carries_food_to_a_seat_by_the_table_and_ea()
        {
            var state = setup(HALL, [MESS]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            hungry(state, s);
            Tick.tick(state);
            expect(s.meal?.seat).toEqual(Js.Obj(("x", (object?)(1)), ("y", (object?)(2))));
            runUntil(state, (_) => s.carrying != null);
            expect(s.note).toBe("taking food to the mess hall at (1, 2)");
            runUntil(state, (_) => s.meal == null);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(1)), ("y", (object?)(2))));
            expect(s.note).toBe("ate a meal at a table");
            expect(state.items).toEqual(Js.Arr([]));
            expect(thoughtsOf(s)).toEqual(Js.Arr(["ateAtTable"]));
        }

        [Fact(DisplayName = "mess hall > without a mess hall, food is eaten where it lies, and that is a little grim")]
        public void T004_without_a_mess_hall_food_is_eaten_where_it_lies_and_that_is_()
        {
            var state = setup(HALL);
            var s = Js.NN(Js.Get(state.survivors, 0));
            hungry(state, s);
            runUntil(state, (_) => s.note == "ate a meal");
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(6))));
            expect(thoughtsOf(s)).toEqual(Js.Arr(["ateWithoutTable"]));
        }

        [Fact(DisplayName = "mess hall > two hungry survivors never take the same seat")]
        public void T005_two_hungry_survivors_never_take_the_same_seat()
        {
            var state = setup(["#####", "#t..#", "###D#", "f...f", "s...s"], [new() { x = Js.I(2), y = Js.I(1), purpose = "messhall" }]);
            foreach (var s in Js.Iter(state.survivors))
            {
                hungry(state, s);
            }
            Tick.tick(state);
            expect(state.survivors.map((s) => s.meal?.seat ?? null).filter((p) => p != null)).toEqual(Js.Arr([Js.Obj(("x", (object?)(2)), ("y", (object?)(1)))]));
            runUntil(state, (_) => state.survivors.every((s) => (s.meal == null) && (s.satiety > Food.HUNGRY)));
            expect(state.survivors.map((s) => s.note).sort()).toEqual(Js.Arr(["ate a meal", "ate a meal at a table"]));
        }

        [Fact(DisplayName = "mess hall > an order on the way to the hall drops the food, still reserved by nobody")]
        public void T006_an_order_on_the_way_to_the_hall_drops_the_food_still_reserve()
        {
            var state = setup(HALL, [MESS]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            hungry(state, s);
            runUntil(state, (_) => s.carrying != null);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(4), y = Js.I(6) });
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
            expect(s.carrying).toBeNull();
            expect(Js.Missing(state.items, 0) ? null : (object?)(Js.Get(state.items, 0))).toMatchObject(Js.Obj(("carriedBy", (object?)(null)), ("reservedBy", (object?)(null)), ("pos", (object?)(s.pos))));
        }

        [Fact(DisplayName = "mess hall > if the table goes while they walk, they eat where they stop, without the table")]
        public void T007_if_the_table_goes_while_they_walk_they_eat_where_they_stop_w()
        {
            var state = setup(HALL, [MESS]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            hungry(state, s);
            runUntil(state, (_) => s.carrying != null);
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(2), y = Js.I(2) });
            runUntil(state, (_) => s.meal == null);
            expect(s.note).toBe("ate a meal");
            expect(thoughtsOf(s)).toEqual(Js.Arr(["ateWithoutTable"]));
        }

        [Fact(DisplayName = "mess hall > survivors build tables from wood like beds")]
        public void T008_survivors_build_tables_from_wood_like_beds()
        {
            var state = setup(["T..", "s.w"], [], new() { ["build"] = 3, ["haul"] = 3 });
            runUntil(state, (_) => state.furniture.some((f) => f.kind == "table"));
            expect(state.blueprints).toEqual(Js.Arr([]));
        }
    }

    /// <summary>mood</summary>
    public class D009_mood
    {

        [Fact(DisplayName = "mood > starts at the baseline and moves with thoughts")]
        public void T010_starts_at_the_baseline_and_moves_with_thoughts()
        {
            var state = setup(["s.."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(Mood.mood(state, s)).toBe(Mood.MOOD_BASE);
            Mood.addThought(state, s, "ateAtTable");
            Mood.addThought(state, s, "sleptOnFloor");
            expect(Mood.mood(state, s)).toBe((Mood.MOOD_BASE + 10) - 10);
            expect(Mood.moodFactors(state, s)).toEqual(Js.Arr([Js.Arr(["ate at a table", 10]), Js.Arr(["slept on the floor", -10])]));
        }

        [Fact(DisplayName = "mood > a repeated thought refreshes instead of stacking, and thoughts fade")]
        public void T011_a_repeated_thought_refreshes_instead_of_stacking_and_thought()
        {
            var state = setup(["s.."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Mood.addThought(state, s, "ateWithoutTable");
            state.tick = Js.I(state.tick + 100);
            Mood.addThought(state, s, "ateWithoutTable");
            expect(s.thoughts).toEqual(Js.Arr([Js.Obj(("kind", (object?)("ateWithoutTable")), ("until", (object?)(state.tick + Mood.THOUGHTS["ateWithoutTable"].duration)))]));
            state.tick = Js.I(state.tick + Mood.THOUGHTS["ateWithoutTable"].duration);
            Tick.tick(state);
            expect(s.thoughts).toEqual(Js.Arr([]));
            expect(Mood.mood(state, s)).toBe(Mood.MOOD_BASE);
        }

        [Fact(DisplayName = "mood > being hurt weighs on mood while it lasts")]
        public void T012_being_hurt_weighs_on_mood_while_it_lasts()
        {
            var state = setup(["s.."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "setHealth", survivorId = s.id, health = 450 });
            expect(Mood.mood(state, s)).toBe(Mood.MOOD_BASE - 10);
        }

        [Fact(DisplayName = "mood > everyone left remembers a death")]
        public void T013_everyone_left_remembers_a_death()
        {
            var state = setup(["s.s.s"]);
            Needs.killSurvivor(state, Js.NN(Js.Get(state.survivors, 0)), "a test");
            expect(state.survivors.map((__f0) => thoughtsOf(__f0))).toEqual(Js.Arr([Js.Arr(["sawDeath"]), Js.Arr(["sawDeath"])]));
        }

        [Fact(DisplayName = "mood > a full night in a bed cheers; on the floor it grates")]
        public void T014_a_full_night_in_a_bed_cheers_on_the_floor_it_grates()
        {
            List<string> sleep(List<string> map)
            {
                var state = setup(map);
                var s = Js.NN(Js.Get(state.survivors, 0));
                Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = Rest.TIRED - 1 });
                runUntil(state, (_) => s.sleep?.asleep == true);
                runUntil(state, (_) => s.note == "woke up rested", 3000);
                expect(s.rest).toBe(Rest.REST_MAX);
                return thoughtsOf(s);
            }
            expect(sleep(["s.b"])).toEqual(Js.Arr(["sleptInBed", "wellRested"]));
            expect(sleep(["s.."])).toEqual(Js.Arr(["sleptOnFloor"]));
        }

        [Fact(DisplayName = "mood > miserable survivors (mood under ...) work at 3/4 speed")]
        public void T015_miserable_survivors_mood_under_work_at_3_4_speed()
        {
            double timeToRemove(bool miserable)
            {
                var state = setup(["s#"], [], new() { ["build"] = 3 });
                var s = Js.NN(Js.Get(state.survivors, 0));
                if (miserable)
                {
                    Mood.addThought(state, s, "sawDeath");
                    Mood.addThought(state, s, "sleptOnFloor");
                }
                Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(1), y = Js.I(0) });
                return runUntil(state, (st) => Grids.tileAt(st.grid, 1) == "floor");
            }
            var content = timeToRemove(false);
            var miserable = timeToRemove(true);
            expect(miserable).toBeGreaterThan(content);
            expect(miserable).toBeLessThanOrEqual(JsMath.ceil(((double)(((double)(content) * (4))) / (3))) + 2);
        }
    }
}
