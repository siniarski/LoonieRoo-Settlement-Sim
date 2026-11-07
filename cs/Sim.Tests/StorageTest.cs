// Ported from tests/storage.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class StorageTest
{

    public static void runUntil(SimState state, Func<SimState, bool> done, double max = 800)
    {
        for (var t = 0; t < max; t++)
        {
            if (Js.T(done(state)))
            {
                return;
            }
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(state.survivors.map((s) => s.note).join("\n"))}");
    }

    public static readonly List<string> MAP = ["................", ".######.######..", ".#....#.#....#..", ".#....#.#....#..", ".##D###.###D##..", "................", "..s....www......", "................"];

    public static readonly ScenarioRoomsItem STORAGE = new ScenarioRoomsItem { x = Js.I(3), y = Js.I(2), purpose = "storage" };

    public static readonly ScenarioRoomsItem HOUSING = new ScenarioRoomsItem { x = Js.I(10), y = Js.I(2), purpose = "housing" };

    public static SimState setup(List<ScenarioRoomsItem> rooms, List<string>? map = null)
    {
        map ??= MAP;
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map, rooms = rooms });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities["clean"] = 0;
        }
        return state;
    }

    public static int inStorage(SimState state)
    {
        return state.items.filter((i) => Js.T(i.pos) && (Rooms.roomAt(state, i.pos.x, i.pos.y) == Rooms.roomAt(state, STORAGE.x, STORAGE.y))).length;
    }

    /// <summary>storage rooms</summary>
    public class D001_storage_rooms
    {

        [Fact(DisplayName = "storage rooms > loose wood gets hauled into a complete storage room")]
        public void T002_loose_wood_gets_hauled_into_a_complete_storage_room()
        {
            var state = setup([STORAGE]);
            expect(Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0))).complete).toBe(true);
            runUntil(state, (s) => inStorage(s) == 3);
            expect(state.jobs.filter((__f0) => Jobs.isStoreJob(__f0))).toEqual(Js.Arr([]));
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toMatch(new JsRegExp("^stored wood at", ""));
        }

        [Fact(DisplayName = "storage rooms > an incomplete storage room takes nothing")]
        public void T003_an_incomplete_storage_room_takes_nothing()
        {
            var map = MAP.map((row, y) => (y == 4) ? row.replace(".##D###", ".######") : row);
            var state = setup([STORAGE], map);
            expect(Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0))).complete).toBe(false);
            for (var i = 0; i < 100; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toEqual(Js.Arr([]));
            expect(inStorage(state)).toBe(0);
        }

        [Fact(DisplayName = "storage rooms > two haulers never aim for the same storage tile")]
        public void T004_two_haulers_never_aim_for_the_same_storage_tile()
        {
            var map = MAP.map((row, y) => (y == 6) ? "..s.s..www......" : row);
            var state = setup([STORAGE], map);
            for (var i = 0; i < 300; i++)
            {
                Tick.tick(state);
                var dests = state.jobs.filter((j) => Js.T(Js.T(j.reservedBy) && Js.T(j.dest))).map((j) => $"{Js.Str(Js.NN(j.dest).x)},{Js.Str(Js.NN(j.dest).y)}");
                expect(Js.Set(dests).size).toBe(dests.length);
            }
            expect(inStorage(state)).toBe(3);
        }

        [Fact(DisplayName = "storage rooms > builders take wood back out of storage")]
        public void T005_builders_take_wood_back_out_of_storage()
        {
            var state = setup([STORAGE]);
            runUntil(state, (s) => inStorage(s) == 3);
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(12), y = Js.I(6), structure = "wall" });
            runUntil(state, (s) => Js.Get(s.grid.tiles, ((double)(6) * (s.grid.width)) + 12) == "wall");
            expect(inStorage(state)).toBe(2);
        }

        [Fact(DisplayName = "storage rooms > if storage stops being complete mid-haul, the hauler drops the wood and stops")]
        public void T006_if_storage_stops_being_complete_mid_haul_the_hauler_drops_th()
        {
            var state = setup([STORAGE]);
            runUntil(state, (s_1) => Js.NN(Js.Get(s_1.survivors, 0)).carrying != null);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(3), y = Js.I(4) });
            Tick.tick(state);
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(s.carrying).toBeNull();
            expect(s.jobId).toBeNull();
            expect(state.jobs).toEqual(Js.Arr([]));
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>beds and housing</summary>
    public class D007_beds_and_housing
    {

        [Fact(DisplayName = "beds and housing > a bed gets built from a designation and completes the housing")]
        public void T008_a_bed_gets_built_from_a_designation_and_completes_the_housin()
        {
            var state = setup([HOUSING]);
            RoomStatus status()
            {
                return Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0)));
            }
            expect(status().requirements.find((r) => !r.met)?.label).toBe("at least 1 bed");
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(10), y = Js.I(2), structure = "bed" });
            runUntil(state, (s) => s.furniture.length == 1);
            expect(status()).toMatchObject(Js.Obj(("complete", (object?)(true)), ("beds", (object?)(1))));
            expect(Js.Missing(Js.NN(Js.Get(DebugText.dumpText(state).split("\n"), 2)), 10) ? null : (object?)(Js.Get(Js.NN(Js.Get(DebugText.dumpText(state).split("\n"), 2)), 10))).toBe("b");
        }

        [Fact(DisplayName = "beds and housing > removing a bed gives its wood back")]
        public void T009_removing_a_bed_gives_its_wood_back()
        {
            var map = MAP.map((row, y) => (y == 2) ? ".#....#.#.b..#.." : row);
            var state = setup([HOUSING], map);
            Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(10), y = Js.I(2) });
            runUntil(state, (s) => s.furniture.length == 0);
            expect(state.items).toHaveLength(4);
        }

        [Fact(DisplayName = "beds and housing > beds only go on empty floor, and items are never dropped on them")]
        public void T010_beds_only_go_on_empty_floor_and_items_are_never_dropped_on_t()
        {
            var map = MAP.map((row, y) => (y == 2) ? ".#....#.#.b..#.." : row);
            var state = setup([], map);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(10), y = Js.I(2), structure = "wall" }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(1), y = Js.I(1), structure = "bed" }).ok).toBe(false);
            expect(Rooms.labelAt(state, 10, 2)).toBeGreaterThanOrEqual(0);
        }
    }
}
