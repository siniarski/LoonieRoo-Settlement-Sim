// Ported from tests/fog.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class FogTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 2000)
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
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}\n{Js.Str(state.survivors.map((s) => s.note).join("\n"))}");
    }

    /// <summary>A long open strip, dark except where the survivor first stands.</summary>
    public static SimState dark(List<string> map, JsRecord<double>? jobs = null)
    {
        jobs ??= new() {  };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 2, map = map });
        state.grid.explored.fill(0);
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        Tick.tick(state);
        return state;
    }

    public static readonly List<string> STRIP = ["s" + ".".repeat(59), ".".repeat(60), ".".repeat(60)];

    /// <summary>fog of war</summary>
    public class D001_fog_of_war
    {

        [Fact(DisplayName = "fog of war > survivors see what is within sight, and walking reveals more")]
        public void T002_survivors_see_what_is_within_sight_and_walking_reveals_more()
        {
            var state = dark(STRIP);
            expect(Fog.isExplored(state, Sight.SIGHT_RADIUS, 0)).toBe(true);
            expect(Fog.isExplored(state, Sight.SIGHT_RADIUS + 1, 0)).toBe(false);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = Js.NN(Js.Get(state.survivors, 0)).id, x = Js.I(30), y = Js.I(1) });
            runUntil(state, (_) => (Js.NN(Js.Get(state.survivors, 0)).pos.x == 30) && (Js.NN(Js.Get(state.survivors, 0)).pos.y == 1));
            expect(Fog.isExplored(state, 30 + Sight.SIGHT_RADIUS, 1)).toBe(true);
            expect(Fog.isExplored(state, 59, 0)).toBe(false);
        }

        [Fact(DisplayName = "fog of war > nothing can be planned on ground nobody has seen")]
        public void T003_nothing_can_be_planned_on_ground_nobody_has_seen()
        {
            var state = dark(STRIP);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(40), y = Js.I(1), structure = "wall" })).toEqual(Js.Obj(("ok", (object?)(false)), ("reason", (object?)("(40, 1) is unexplored"))));
            expect(Commands.applyCommand(state, new Command { type = "designateFarm", x = Js.I(40), y = Js.I(1) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(3), y = Js.I(1), structure = "wall" }).ok).toBe(true);
        }
    }

    /// <summary>exploring</summary>
    public class D004_exploring
    {

        [Fact(DisplayName = "exploring > a scout sweeps a dark box until nothing in it is left unseen, then the order is done")]
        public void T005_a_scout_sweeps_a_dark_box_until_nothing_in_it_is_left_unseen()
        {
            var state = dark(STRIP, new() { ["scout"] = 3 });
            expect(Commands.applyCommand(state, new Command { type = "designateExplore", x0 = Js.I(59), y0 = Js.I(2), x1 = Js.I(40), y1 = Js.I(0) }).ok).toBe(true);
            expect(state.exploreAreas).toMatchObject(Js.Arr([Js.Obj(("x0", (object?)(40)), ("y0", (object?)(0)), ("x1", (object?)(59)), ("y1", (object?)(2)))]));
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).note?.startsWith("took scout job") == true, 10);
            runUntil(state, (_) => state.exploreAreas.length == 0);
            for (var x = 40; x < 60; x++)
            {
                expect(Fog.isExplored(state, x, 1)).toBe(true);
            }
            Tick.tick(state);
            expect(state.jobs).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "exploring > a box that is all explored already is refused")]
        public void T006_a_box_that_is_all_explored_already_is_refused()
        {
            var state = dark(STRIP);
            expect(Commands.applyCommand(state, new Command { type = "designateExplore", x0 = Js.I(0), y0 = Js.I(0), x1 = Js.I(3), y1 = Js.I(2) })).toEqual(Js.Obj(("ok", (object?)(false)), ("reason", (object?)("already explored"))));
        }

        [Fact(DisplayName = "exploring > dark ground nobody can reach is left alone, and nobody gets stuck on it")]
        public void T007_dark_ground_nobody_can_reach_is_left_alone_and_nobody_gets_s()
        {
            var state = dark(["s.........#" + ".".repeat(20), "..........#" + ".".repeat(20)], new() { ["scout"] = 3 });
            Commands.applyCommand(state, new Command { type = "designateExplore", x0 = Js.I(20), y0 = Js.I(0), x1 = Js.I(30), y1 = Js.I(1) });
            for (var i = 0; i < 100; i++)
            {
                Tick.tick(state);
            }
            expect(state.jobs).toMatchObject(Js.Arr([Js.Obj(("type", (object?)("scout")), ("reservedBy", (object?)(null)))]));
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }
    }

    /// <summary>scouting through the day</summary>
    public class D008_scouting_through_the_day
    {

        [Fact(DisplayName = "scouting through the day > scouts stop at ...:00 and head home, and go out again in the morning")]
        public void T009_scouts_stop_at_00_and_head_home_and_go_out_again_in_the_morn()
        {
            var state = dark(STRIP, new() { ["scout"] = 3 });
            state.tick = Js.I(((double)((Fog.SCOUT_HOME_HOUR - 6)) * (Time.TICKS_PER_HOUR)) - 30);
            Commands.applyCommand(state, new Command { type = "designateExplore", x0 = Js.I(40), y0 = Js.I(0), x1 = Js.I(59), y1 = Js.I(2) });
            var s = Js.NN(Js.Get(state.survivors, 0));
            runUntil(state, (_) => s.jobId != null, 20);
            runUntil(state, (_) => s.note == "heading home before dark", 100);
            expect(s.jobId).toBeNull();
            for (var i = 0; i < 100; i++)
            {
                Tick.tick(state);
            }
            expect(s.jobId).toBeNull();
            state.tick = Js.I(Time.DAY_LENGTH);
            runUntil(state, (_) => s.jobId != null, 50);
            expect(Fog.scoutingOver(state.tick)).toBe(false);
        }

        [Fact(DisplayName = "scouting through the day > a newly seen crate marks its ruin, once, and the mark goes when the crates are empty")]
        public void T010_a_newly_seen_crate_marks_its_ruin_once_and_the_mark_goes_whe()
        {
            var state = dark([(("s" + ".".repeat(19)) + "c.c") + ".".repeat(7)], new() { ["scavenge"] = 3 });
            expect(state.pointsOfInterest).toEqual(Js.Arr([]));
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = Js.NN(Js.Get(state.survivors, 0)).id, x = Js.I(16), y = Js.I(0) });
            runUntil(state, (_) => state.pointsOfInterest.length > 0);
            expect(state.pointsOfInterest).toMatchObject(Js.Arr([Js.Obj(("x", (object?)(20)), ("y", (object?)(0)), ("label", (object?)("ruin with crates")))]));
            expect(Js.NN(state.events.at(-1)).message).toBe("Found a ruin with crates at (20, 0)");
            runUntil(state, (_) => Fog.isExplored(state, 22, 0));
            expect(state.pointsOfInterest).toHaveLength(1);
            foreach (var f in Js.Iter(state.furniture))
            {
                f.loot = 0;
            }
            for (var i = 0; i < 60; i++)
            {
                Tick.tick(state);
            }
            expect(state.pointsOfInterest).toEqual(Js.Arr([]));
        }
    }
}
