// Ported from tests/scavenge.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ScavengeTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 3000) => TestKit.runUntil(state, done, max);

    /// <summary>Survivors only scavenge (and haul, when asked), so other jobs don't interfere.</summary>
    public static SimState setup(List<string> map, List<ScenarioRoomsItem>? rooms = null, double haul = 0)
    {
        rooms ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 7, map = map, rooms = rooms });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) { ["scavenge"] = 3, ["haul"] = Js.Num(haul) };
        }
        return state;
    }

    /// <summary>Marks every crate on the map, like painting the Scavenge tool over it.</summary>
    public static void markAll(SimState state)
    {
        foreach (var f in Js.Iter(state.furniture))
        {
            Commands.applyCommand(state, new Command { type = "designateScavenge", x = Js.I(f.x), y = Js.I(f.y) });
        }
    }

    public static List<Furniture> crates(SimState state)
    {
        return state.furniture.filter((f) => f.kind == "crate");
    }

    /// <summary>scavenging</summary>
    public class D001_scavenging
    {

        [Fact(DisplayName = "scavenging > crates do nothing until marked")]
        public void T002_crates_do_nothing_until_marked()
        {
            var state = setup(["s...c"]);
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toEqual(Js.Arr([]));
            expect(Js.NN(Js.Get(crates(state), 0)).loot).toBe(FurnitureMod.CRATE_LOOT);
        }

        [Fact(DisplayName = "scavenging > a marked crate is searched from next to it and yields one find per search")]
        public void T003_a_marked_crate_is_searched_from_next_to_it_and_yields_one_fi()
        {
            var state = setup(["s...c"]);
            markAll(state);
            Tick.tick(state);
            expect(state.jobs.map((j) => j.type)).toEqual(Js.Arr(["scavenge"]));
            runUntil(state, (st) => st.items.length == 1);
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(3)), ("y", (object?)(0))));
            expect(s.note).toMatch(new JsRegExp("^scavenged \\w+ from \\(4, 0\\), 4 left$", ""));
            expect(Js.NN(Js.Get(crates(state), 0)).loot).toBe(FurnitureMod.CRATE_LOOT - 1);
        }

        [Fact(DisplayName = "scavenging > keeps searching over time until the crate is empty, then unmarks it")]
        public void T004_keeps_searching_over_time_until_the_crate_is_empty_then_unma()
        {
            var state = setup(["s...c", "....."]);
            markAll(state);
            var ticks = runUntil(state, (st) => Js.NN(Js.Get(crates(st), 0)).loot == 0);
            expect(ticks).toBeGreaterThanOrEqual(((double)(FurnitureMod.CRATE_LOOT) * (Jobs.SCAVENGE_WORK)));
            expect(state.items).toHaveLength(FurnitureMod.CRATE_LOOT);
            expect(Js.NN(Js.Get(crates(state), 0)).scavenge).toBe(false);
            for (var i = 0; i < 20; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "scavenging > finds come from the scavenge table, rolled with the sim RNG (same seed, same finds)")]
        public void T005_finds_come_from_the_scavenge_table_rolled_with_the_sim_RNG_s()
        {
            List<string> run()
            {
                var state = setup(["s..c", "....", "..c."]);
                markAll(state);
                runUntil(state, (st) => crates(st).every((c) => c.loot == 0));
                return state.items.map((i) => i.kind);
            }
            var kinds = run();
            expect(kinds).toHaveLength(((double)(2) * (FurnitureMod.CRATE_LOOT)));
            var table = Jobs.SCAVENGE_TABLE.map((__p0) =>
        {
            var k = __p0.Item1;
            return k;
        });
            expect(kinds.every((k) => table.includes(k))).toBe(true);
            expect(Js.Set(kinds).size).toBeGreaterThanOrEqual(3);
            expect(run()).toEqual(kinds);
        }

        [Fact(DisplayName = "scavenging > painting over an area marks every crate in it; empty crates and beds are ignored")]
        public void T006_painting_over_an_area_marks_every_crate_in_it_empty_crates_a()
        {
            var state = setup(["c.c.b", ".....", "s...."]);
            Js.NN(Js.Get(crates(state), 1)).loot = 0;
            for (var x = 0; x < 5; x++)
            {
                Commands.applyCommand(state, new Command { type = "designateScavenge", x = Js.I(x), y = Js.I(0) });
            }
            expect(state.furniture.map((f) => f.scavenge)).toEqual(Js.Arr([true, false, false]));
            expect(state.commandLog).toHaveLength(1);
        }

        [Fact(DisplayName = "scavenging > two survivors never search the same crate at once")]
        public void T007_two_survivors_never_search_the_same_crate_at_once()
        {
            var state = setup(["s..c..s", "......."]);
            markAll(state);
            for (var i = 0; i < 300; i++)
            {
                Tick.tick(state);
                var searching = state.survivors.filter((s) => Js.T(s.jobId)).length;
                expect(searching).toBeLessThanOrEqual(1);
            }
        }

        [Fact(DisplayName = "scavenging > cancel unmarks a crate and stops the search")]
        public void T008_cancel_unmarks_a_crate_and_stops_the_search()
        {
            var state = setup(["s...c"]);
            markAll(state);
            runUntil(state, (st) => Js.NN(Js.Get(st.survivors, 0)).jobId != null);
            Commands.applyCommand(state, new Command { type = "cancelDesignation", x = Js.I(4), y = Js.I(0) });
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            expect(state.jobs).toEqual(Js.Arr([]));
            expect(Js.NN(Js.Get(crates(state), 0)).scavenge).toBe(false);
        }

        [Fact(DisplayName = "scavenging > finds get hauled back to storage")]
        public void T009_finds_get_hauled_back_to_storage()
        {
            List<string> map = ["................", ".#####..........", ".#...#.......c..", ".#...#..........", ".##D##..........", "s..............."];
            var state = setup(map, [new() { x = Js.I(2), y = Js.I(2), purpose = "storage" }], 3);
            Js.NN(Js.Get(state.survivors, 0)).resolve = 100;
            markAll(state);
            var storageRoom = Js.NN(Rooms.roomAt(state, 2, 2));
            runUntil(state, (st) => (Js.NN(Js.Get(crates(st), 0)).loot == 0) && st.items.every((i) => Js.T(Js.T(i.pos) ? (Rooms.roomAt(st, i.pos.x, i.pos.y) == storageRoom) : (i.carriedBy != null))));
            expect(state.items.length).toBeGreaterThanOrEqual(FurnitureMod.CRATE_LOOT - 1);
        }

        [Fact(DisplayName = "scavenging > removing a crate gives wood back")]
        public void T010_removing_a_crate_gives_wood_back()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 7, map = ["s..c"] });
            foreach (var s in Js.Iter(state.survivors))
            {
                s.priorities = new(JobTypes.NO_JOBS) { ["build"] = 3 };
            }
            Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(3), y = Js.I(0) });
            runUntil(state, (st) => st.furniture.length == 0);
            expect(state.items.map((i) => i.kind)).toEqual(Js.Arr(["wood"]));
        }
    }
}
