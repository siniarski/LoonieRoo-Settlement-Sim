// Ported from tests/planner.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class PlannerTest
{

    /// <summary>A 6x3 room (18 tiles: capacity 4 beds) with a door at the bottom, and survivors outside.</summary>
    public static readonly List<string> HOUSE = ["..........", ".########.", ".#......#.", ".#......#.", ".#......#.", ".###D####.", "..s.s.....", ".........."];

    public static SimState setup(List<string>? map = null, List<ScenarioRoomsItem>? rooms = null)
    {
        map ??= HOUSE;
        rooms ??= [new() { x = Js.I(3), y = Js.I(3), purpose = "housing" }];
        return ScenarioMod.loadScenario(new Scenario { seed = 4, map = map, rooms = rooms });
    }

    public static List<string> beds(List<Command> cmds)
    {
        return cmds.map((c) => ((c.type == "designateBuild") && (c.structure == "bed")) ? $"{Js.Str(c.x)},{Js.Str(c.y)}" : "other");
    }

    /// <summary>furnish planner</summary>
    public class D001_furnish_planner
    {

        [Fact(DisplayName = "furnish planner > explains when there is no room, or no purpose")]
        public void T002_explains_when_there_is_no_room_or_no_purpose()
        {
            var state = setup(HOUSE, []);
            expect(Planner.planFurnish(state, 0, 0)).toEqual(Js.Obj(("commands", (object?)(Js.Arr([]))), ("notes", (object?)(Js.Arr(["Not an enclosed room: close it in with walls and a door first."])))));
            expect(Planner.planFurnish(state, 3, 3).notes).toEqual(Js.Arr(["This room has no purpose yet: give it one with a Rooms tool first."]));
        }

        [Fact(DisplayName = "furnish planner > plans one bed per survivor, corners first, never next to the door")]
        public void T003_plans_one_bed_per_survivor_corners_first_never_next_to_the_d()
        {
            var state = setup();
            var plan = Planner.planFurnish(state, 3, 3);
            expect(beds(plan.commands)).toEqual(Js.Arr(["2,2", "7,2"]));
            expect(plan.notes).toEqual(Js.Arr(["Planned 2 beds (room holds 4, colony of 2)."]));
            for (var i = 0; i < 4; i++)
            {
                Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = Js.I(5 + i), y = Js.I(7) });
            }
            var full = beds(Planner.planFurnish(state, 3, 3).commands);
            expect(full).toHaveLength(4);
            expect(full).not.toContain("4,4");
        }

        [Fact(DisplayName = "furnish planner > skips tiles with items, furniture, plans, or plots")]
        public void T004_skips_tiles_with_items_furniture_plans_or_plots()
        {
            var map = HOUSE.map((row, y) => (y == 2) ? ".#w....~#." : row);
            var state = setup(map);
            expect(beds(Planner.planFurnish(state, 3, 3).commands)).toEqual(Js.Arr(["2,4", "7,4"]));
        }

        [Fact(DisplayName = "furnish planner > counts beds already here and in other housing")]
        public void T005_counts_beds_already_here_and_in_other_housing()
        {
            List<string> map = [.. HOUSE.slice(0, 7), ".####.....", ".#bb#.....", ".##D#.....", ".........."];
            var state = setup(map, [new() { x = Js.I(3), y = Js.I(3), purpose = "housing" }, new() { x = Js.I(2), y = Js.I(8), purpose = "housing" }]);
            Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = Js.I(8), y = Js.I(7) });
            expect(beds(Planner.planFurnish(state, 3, 3).commands)).toEqual(Js.Arr(["2,2"]));
        }

        [Fact(DisplayName = "furnish planner > planning twice adds nothing once the beds are planned")]
        public void T006_planning_twice_adds_nothing_once_the_beds_are_planned()
        {
            var state = setup();
            foreach (var c in Js.Iter(Planner.planFurnish(state, 3, 3).commands))
            {
                Commands.applyCommand(state, c);
            }
            expect(Planner.planFurnish(state, 3, 3)).toEqual(Js.Obj(("commands", (object?)(Js.Arr([]))), ("notes", (object?)(Js.Arr(["Already has 2 beds: enough for the colony."])))));
        }

        [Fact(DisplayName = "furnish planner > storage needs nothing; a missing door is explained")]
        public void T007_storage_needs_nothing_a_missing_door_is_explained()
        {
            var storage = setup(HOUSE, [new() { x = Js.I(3), y = Js.I(3), purpose = "storage" }]);
            expect(Planner.planFurnish(storage, 3, 3)).toEqual(Js.Obj(("commands", (object?)(Js.Arr([]))), ("notes", (object?)(Js.Arr(["Storage needs no furniture yet."])))));
            var noDoor = setup(HOUSE.map((row, y) => (y == 5) ? ".########." : row));
            var plan = Planner.planFurnish(noDoor, 3, 3);
            expect(Js.Missing(plan.notes, 0) ? null : (object?)(Js.Get(plan.notes, 0))).toBe("Needs a door: build one in its wall.");
            expect(plan.commands).toHaveLength(2);
        }

        [Fact(DisplayName = "furnish planner > a mess hall gets a table per 4 survivors, in the open, tables apart")]
        public void T008_a_mess_hall_gets_a_table_per_4_survivors_in_the_open_tables_()
        {
            var hall = setup(HOUSE, [new() { x = Js.I(3), y = Js.I(3), purpose = "messhall" }]);
            List<string> tables(SimState state)
            {
                return Planner.planFurnish(state, 3, 3).commands.map((c) => ((c.type == "designateBuild") && (c.structure == "table")) ? $"{Js.Str(c.x)},{Js.Str(c.y)}" : "other");
            }
            expect(tables(hall)).toEqual(Js.Arr(["3,3"]));
            for (var i = 0; i < 3; i++)
            {
                Commands.applyCommand(hall, new Command { type = "spawnSurvivor", x = Js.I(6 + i), y = Js.I(7) });
            }
            expect(tables(hall)).toEqual(Js.Arr(["3,3", "6,3"]));
            foreach (var c in Js.Iter(Planner.planFurnish(hall, 3, 3).commands))
            {
                Commands.applyCommand(hall, c);
            }
            expect(Planner.planFurnish(hall, 3, 3).notes).toEqual(Js.Arr(["Already has 2 tables: enough seats for the colony."]));
        }

        [Fact(DisplayName = "furnish planner > is pure: planning never changes state")]
        public void T009_is_pure_planning_never_changes_state()
        {
            var state = setup();
            var before = Js.Json_stringify(state);
            Planner.planFurnish(state, 3, 3);
            expect(Js.Json_stringify(state)).toBe(before);
        }

        [Fact(DisplayName = "furnish planner > end to end: survivors build the planned beds and the housing is complete")]
        public void T010_end_to_end_survivors_build_the_planned_beds_and_the_housing_()
        {
            var map = HOUSE.map((row, y) => (y == 7) ? "..wwww...." : row);
            SimState state = setup(map);
            foreach (var s in Js.Iter(state.survivors))
            {
                s.priorities = new(JobTypes.NO_JOBS) { ["build"] = 3, ["haul"] = 3 };
            }
            foreach (var c in Js.Iter(Planner.planFurnish(state, 3, 3).commands))
            {
                Commands.applyCommand(state, c);
            }
            for (var t = 0; (t < 2000) && (state.furniture.length < 2); t++)
            {
                Tick.tick(state);
                expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
            }
            expect(state.furniture.map((f) => $"{Js.Str(f.x)},{Js.Str(f.y)}").sort()).toEqual(Js.Arr(["2,2", "7,2"]));
            expect(Rooms.designationStatus(state, Js.NN(Js.Get(state.roomDesignations, 0))).complete).toBe(true);
        }
    }
}
