// Ported from tests/jobs.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class JobsTest
{

    /// <summary>Ticks until `done` holds, checking invariants every tick. Fails after `max`.</summary>
    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 600)
    {
        for (var t = 0; t < max; t++)
        {
            if (Js.T(done(state)))
            {
                return t;
            }
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(Js.Json_stringify(state.survivors.map((s) => s.note)))}");
    }

    public static SimState setup(List<string> map, JsRecord<double>? priorities = default)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        if (Js.T(priorities))
        {
            foreach (var s in Js.Iter(state.survivors))
            {
                /*TODO Object.assign*/Js.Object_assign(s.priorities, priorities);
            }
        }
        return state;
    }

    /// <summary>build jobs</summary>
    public class D001_build_jobs
    {

        [Fact(DisplayName = "build jobs > hauls wood to a wall blueprint, then builds it")]
        public void T002_hauls_wood_to_a_wall_blueprint_then_builds_it()
        {
            var state = setup(["s.w....."], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(6), y = Js.I(0), structure = "wall" });
            runUntil(state, (s) => s.blueprints.length == 0);
            expect(Grids.getTile(state.grid, 6, 0)).toBe("wall");
            expect(state.items).toEqual(Js.Arr([]));
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
        }

        [Fact(DisplayName = "build jobs > builds doors the same way")]
        public void T003_builds_doors_the_same_way()
        {
            var state = setup(["s.w...", "......"], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(5), y = Js.I(1), structure = "door" });
            runUntil(state, (s) => Grids.getTile(s.grid, 5, 1) == "door");
        }

        [Fact(DisplayName = "build jobs > removing a wall gives the wood back")]
        public void T004_removing_a_wall_gives_the_wood_back()
        {
            var state = setup(["s..#.."], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(3), y = Js.I(0) });
            runUntil(state, (s) => Grids.getTile(s.grid, 3, 0) == "floor");
            expect(state.items.map((i) => Js.Arr([i.kind, i.pos]))).toEqual(Js.Arr([Js.Arr(["wood", Js.Obj(("x", (object?)(3)), ("y", (object?)(0)))])]));
        }

        [Fact(DisplayName = "build jobs > evicts a survivor standing on the tile when the wall completes")]
        public void T005_evicts_a_survivor_standing_on_the_tile_when_the_wall_complet()
        {
            var state = setup(["s.w.s."], new() { ["clean"] = 0 });
            Js.NN(Js.Get(state.survivors, 1)).priorities = new(JobTypes.NO_JOBS) {  };
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(4), y = Js.I(0), structure = "wall" });
            runUntil(state, (s) => Grids.getTile(s.grid, 4, 0) == "wall");
            expect(Js.NN(Js.Get(state.survivors, 1)).pos).not.toEqual(Js.Obj(("x", (object?)(4)), ("y", (object?)(0))));
        }
    }

    /// <summary>Wraps one row of floor in a walled room with a door, surrounded by outdoor floor.</summary>
    public static List<string> inRoom(string row)
    {
        var w = row.length;
        return (List<string>)[".".repeat(w + 4), $".{Js.Str("#".repeat(w + 2))}.", $".#{Js.Str(row)}#.", $".#D{Js.Str("#".repeat(w))}.", ".".repeat(w + 4)];
    }

    /// <summary>clean jobs</summary>
    public class D006_clean_jobs
    {

        [Fact(DisplayName = "clean jobs > cleans dirty tiles indoors")]
        public void T007_cleans_dirty_tiles_indoors()
        {
            var state = setup(inRoom("s..,.."), new() { ["build"] = 0, ["haul"] = 0 });
            runUntil(state, (s) => !Grids.isDirty(s.grid, 5, 2));
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toBe("cleaned (5, 2)");
        }

        [Fact(DisplayName = "clean jobs > ignores dirt outdoors")]
        public void T008_ignores_dirt_outdoors()
        {
            var state = setup(["s..,.."], new() { ["build"] = 0, ["haul"] = 0 });
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(Grids.isDirty(state.grid, 3, 0)).toBe(true);
            expect(state.jobs).toEqual(Js.Arr([]));
        }
    }

    /// <summary>job selection</summary>
    public class D009_job_selection
    {

        [Fact(DisplayName = "job selection > two survivors never reserve the same job")]
        public void T010_two_survivors_never_reserve_the_same_job()
        {
            var state = setup(inRoom("s....,....s"));
            Tick.tick(state);
            var holders = state.survivors.filter((s) => Js.T(s.jobId));
            expect(holders).toHaveLength(1);
            expect(Js.NN(Js.Get(state.jobs, 0)).reservedBy).toBe(Js.NN(Js.Get(holders, 0)).id);
        }

        [Fact(DisplayName = "job selection > picks the highest-priority job type first")]
        public void T011_picks_the_highest_priority_job_type_first()
        {
            var state = setup(inRoom("s,.w.."), new() { ["guard"] = 3, ["build"] = 3, ["farm"] = 3, ["haul"] = 3, ["scavenge"] = 3, ["clean"] = 1 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(7), y = Js.I(2), structure = "wall" });
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toMatch(new JsRegExp("^took clean job", ""));
        }

        [Fact(DisplayName = "job selection > breaks priority ties by job type order: build, haul, clean")]
        public void T012_breaks_priority_ties_by_job_type_order_build_haul_clean()
        {
            var state = setup(inRoom("s,.w.."));
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(7), y = Js.I(2), structure = "wall" });
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toMatch(new JsRegExp("^took haul job", ""));
        }

        [Fact(DisplayName = "job selection > never takes a job type set to off")]
        public void T013_never_takes_a_job_type_set_to_off()
        {
            var state = setup(inRoom("s.,."), new() { ["clean"] = 0 });
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
                expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            }
            expect(Grids.isDirty(state.grid, 4, 2)).toBe(true);
            expect(JobChoice.explainIdle(state, Js.NN(Js.Get(state.survivors, 0)))).toBe("no open jobs of enabled types");
        }

        [Fact(DisplayName = "job selection > takes the nearest job of the chosen type")]
        public void T014_takes_the_nearest_job_of_the_chosen_type()
        {
            var state = setup(inRoom("..,..s.,."));
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toBe("took clean job at (9, 2): priority 3, nearest of 2 open");
        }

        [Fact(DisplayName = "job selection > explains why haul jobs are waiting")]
        public void T015_explains_why_haul_jobs_are_waiting()
        {
            var state = setup(["s....."], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(4), y = Js.I(0), structure = "wall" });
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            expect(JobChoice.explainIdle(state, Js.NN(Js.Get(state.survivors, 0)))).toBe("1 haul job(s) waiting for wood");
        }

        [Fact(DisplayName = "job selection > skips unreachable wood until a path opens")]
        public void T016_skips_unreachable_wood_until_a_path_opens()
        {
            List<string> map = ["s.....###", "......#w#", "......###", "........."];
            var state = setup(map, new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(3), y = Js.I(2), structure = "wall" });
            for (var i = 0; i < 30; i++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.survivors, 0)).jobId).toBeNull();
            expect(JobChoice.explainIdle(state, Js.NN(Js.Get(state.survivors, 0)))).toBe("1 open job(s), none reachable right now");
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(7), y = Js.I(2) });
            runUntil(state, (s) => Grids.getTile(s.grid, 3, 2) == "wall");
        }
    }

    /// <summary>invalid-job recovery</summary>
    public class D017_invalid_job_recovery
    {

        [Fact(DisplayName = "invalid-job recovery > drops the wood and goes idle when a designation is cancelled mid-haul")]
        public void T018_drops_the_wood_and_goes_idle_when_a_designation_is_cancelled()
        {
            var state = setup(["s.w........"], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(10), y = Js.I(0), structure = "wall" });
            runUntil(state, (s_1) => Js.NN(Js.Get(s_1.survivors, 0)).carrying != null);
            Commands.applyCommand(state, new Command { type = "cancelDesignation", x = Js.I(10), y = Js.I(0) });
            Tick.tick(state);
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(s.carrying).toBeNull();
            expect(s.jobId).toBeNull();
            expect(state.items).toHaveLength(1);
            expect(Js.NN(Js.Get(state.items, 0)).pos).not.toBeNull();
            expect(Js.NN(Js.Get(state.items, 0)).reservedBy).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "invalid-job recovery > refunds delivered wood when an instant wall replaces a blueprint")]
        public void T019_refunds_delivered_wood_when_an_instant_wall_replaces_a_bluep()
        {
            var state = setup(["s.w....."], new() { ["build"] = 0, ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(6), y = Js.I(0), structure = "wall" });
            runUntil(state, (s) => Js.Get(s.blueprints, 0)?.delivered == true);
            expect(state.items).toEqual(Js.Arr([]));
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(6), y = Js.I(0) });
            Tick.tick(state);
            expect(state.blueprints).toEqual(Js.Arr([]));
            expect(state.items).toHaveLength(1);
        }

        [Fact(DisplayName = "invalid-job recovery > a direct order overrides work and the survivor drops what they carry")]
        public void T020_a_direct_order_overrides_work_and_the_survivor_drops_what_th()
        {
            var state = setup(["s.w........", "..........."], new() { ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(10), y = Js.I(0), structure = "wall" });
            runUntil(state, (s_2) => Js.NN(Js.Get(s_2.survivors, 0)).carrying != null);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = "s1", x = Js.I(0), y = Js.I(1) });
            var s = Js.NN(Js.Get(state.survivors, 0));
            expect(s.carrying).toBeNull();
            expect(s.jobId).toBeNull();
            expect(s.goal).toEqual(Js.Obj(("x", (object?)(0)), ("y", (object?)(1))));
            expect(Js.NN(Js.Get(state.jobs, 0)).reservedBy).toBeNull();
        }

        [Fact(DisplayName = "invalid-job recovery > gives up a build when it gets walled off, and resumes when reopened")]
        public void T021_gives_up_a_build_when_it_gets_walled_off_and_resumes_when_re()
        {
            List<string> map = ["s...#...", "....#...", "....#..."];
            var state = setup(map, new() { ["haul"] = 0, ["clean"] = 0 });
            Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(4), y = Js.I(1) });
            runUntil(state, (s) => Js.NN(Js.Get(s.survivors, 0)).jobId != null);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(1) });
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(1) });
            runUntil(state, (s) => Js.NN(Js.Get(s.survivors, 0)).jobId == null, 50);
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(1), y = Js.I(0) });
            runUntil(state, (s) => Grids.getTile(s.grid, 4, 1) == "floor");
        }
    }

    /// <summary>designation commands</summary>
    public class D022_designation_commands
    {

        [Fact(DisplayName = "designation commands > only designates builds on empty floor")]
        public void T023_only_designates_builds_on_empty_floor()
        {
            var state = setup([".#"]);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(1), y = Js.I(0), structure = "wall" }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(0), y = Js.I(0), structure = "wall" })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(0), y = Js.I(0), structure = "wall" })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(0), y = Js.I(0), structure = "door" })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(state.blueprints.map((b) => b.target)).toEqual(Js.Arr(["door"]));
        }

        [Fact(DisplayName = "designation commands > remove marks walls, cancels planned builds, and ignores bare floor")]
        public void T024_remove_marks_walls_cancels_planned_builds_and_ignores_bare_f()
        {
            var state = setup([".#."]);
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(0), y = Js.I(0), structure = "wall" });
            expect(Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(0), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(1), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(1), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(2), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(DebugText.dumpText(state)).toBe(".x.");
        }

        [Fact(DisplayName = "designation commands > validates priorities and logs only real changes")]
        public void T025_validates_priorities_and_logs_only_real_changes()
        {
            var state = setup(["s"]);
            expect(Commands.applyCommand(state, new Command { type = "setPriority", survivorId = "s1", jobType = "clean", priority = 9 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setPriority", survivorId = "s1", jobType = "nope", priority = 1 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setPriority", survivorId = "s9", jobType = "clean", priority = 1 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setPriority", survivorId = "s1", jobType = "clean", priority = 3 })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(Commands.applyCommand(state, new Command { type = "setPriority", survivorId = "s1", jobType = "clean", priority = 1 })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(state.commandLog).toHaveLength(1);
        }

        [Fact(DisplayName = "designation commands > scenario maps can hold wood, dirt, and blueprints")]
        public void T026_scenario_maps_can_hold_wood_dirt_and_blueprints()
        {
            var state = setup(["sw,+d"]);
            expect(DebugText.dumpText(state)).toBe("sw,+d");
            expect(state.items).toHaveLength(1);
            expect(state.blueprints.map((b) => b.target)).toEqual(Js.Arr(["wall", "door"]));
        }
    }
}
