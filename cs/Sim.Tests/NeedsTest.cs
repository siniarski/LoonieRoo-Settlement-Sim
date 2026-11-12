// Ported from tests/needs.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class NeedsTest
{


    public static SimState setup(List<string> map)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) {  };
        }
        return state;
    }

    public static CommandResult tire(SimState state, Survivor s, double? rest = null)
    {
        rest ??= Rest.TIRED - 1;
        return Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = rest });
    }

    /// <summary>rest and sleep</summary>
    public class D001_rest_and_sleep
    {

        [Fact(DisplayName = "rest and sleep > starts staggered and drains by 1 per tick while awake")]
        public void T002_starts_staggered_and_drains_by_1_per_tick_while_awake()
        {
            expect(new List<double> { 0, 1, 2, 3, 4 }.map((__f0) => Rest.initialRest(__f0))).toEqual(Js.Arr([2000, 1700, 1400, 1100, 2000]));
            var state = setup(["s...."]);
            for (var i = 0; i < 10; i++)
            {
                Tick.tick(state);
            }
            expect(Js.NN(Js.Get(state.survivors, 0)).rest).toBe(Rest.REST_MAX - 10);
        }

        [Fact(DisplayName = "rest and sleep > a tired survivor walks to the nearest free bed, sleeps, and wakes rested")]
        public void T003_a_tired_survivor_walks_to_the_nearest_free_bed_sleeps_and_wa()
        {
            var state = setup(["s.....b..b"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            tire(state, s);
            Tick.tick(state);
            expect(s.note).toBe("going to bed at (6, 0)");
            expect(Js.NN(Js.Get(state.furniture, 0)).occupant).toBe(s.id);
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(6)), ("y", (object?)(0))));
            var before = s.rest;
            Tick.tick(state);
            expect(s.rest).toBe(before + Rest.BED_REGEN);
            runUntil(state, (_) => s.sleep == null);
            expect(s.rest).toBe(Rest.REST_MAX);
            expect(s.note).toBe("woke up rested");
            expect(Js.NN(Js.Get(state.furniture, 0)).occupant).toBeNull();
        }

        [Fact(DisplayName = "rest and sleep > with no free bed, sleeps on the floor and recovers more slowly")]
        public void T004_with_no_free_bed_sleeps_on_the_floor_and_recovers_more_slowl()
        {
            var state = setup(["s.b.s"]);
            var (a, b) = state.survivors;
            tire(state, a);
            tire(state, b);
            Tick.tick(state);
            expect(a.sleep?.bedId).toBe(Js.NN(Js.Get(state.furniture, 0)).id);
            expect(b.sleep).toEqual(Js.Obj(("bedId", (object?)(null)), ("asleep", (object?)(true))));
            expect(b.note).toBe("no free bed, asleep on the floor");
            var before = b.rest;
            Tick.tick(state);
            expect(b.rest).toBe(before + Rest.FLOOR_REGEN);
        }

        [Fact(DisplayName = "rest and sleep > two tired survivors never claim the same bed")]
        public void T005_two_tired_survivors_never_claim_the_same_bed()
        {
            var state = setup(["s..bb..s"]);
            foreach (var s in Js.Iter(state.survivors))
            {
                tire(state, s);
            }
            runUntil(state, (st) => st.survivors.every((s) => Js.T(s.sleep?.asleep)));
            var beds = state.survivors.map((s) => Js.NN(s.sleep).bedId);
            expect(Js.Set(beds).size).toBe(2);
            expect(beds).not.toContain(null);
        }

        [Fact(DisplayName = "rest and sleep > getting tired mid-job drops the job and the wood, then sleeps")]
        public void T006_getting_tired_mid_job_drops_the_job_and_the_wood_then_sleeps()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.w........b"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities["clean"] = 0;
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(9), y = Js.I(0), structure = "wall" });
            runUntil(state, (_) => s.carrying != null);
            tire(state, s);
            Tick.tick(state);
            expect(s.jobId).toBeNull();
            expect(s.carrying).toBeNull();
            expect(s.sleep).not.toBeNull();
            expect(Js.NN(Js.Get(state.jobs, 0)).reservedBy).toBeNull();
            expect(Js.NN(Js.Get(state.items, 0)).pos).not.toBeNull();
        }

        [Fact(DisplayName = "rest and sleep > an unreachable bed is skipped in favour of the floor")]
        public void T007_an_unreachable_bed_is_skipped_in_favour_of_the_floor()
        {
            var state = setup(["s..###", "...#b#", "...###"]);
            tire(state, Js.NN(Js.Get(state.survivors, 0)));
            Tick.tick(state);
            expect(Js.NN(Js.Get(state.survivors, 0)).sleep).toEqual(Js.Obj(("bedId", (object?)(null)), ("asleep", (object?)(true))));
        }

        [Fact(DisplayName = "rest and sleep > if the bed is removed mid-sleep, the survivor re-plans instead of getting stuck")]
        public void T008_if_the_bed_is_removed_mid_sleep_the_survivor_re_plans_instea()
        {
            var state = setup(["s.b...b"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            tire(state, s);
            runUntil(state, (_) => s.sleep?.asleep == true);
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(2), y = Js.I(0) });
            runUntil(state, (_) => (s.sleep?.asleep == true) && (s.sleep.bedId == Js.Get(state.furniture, 0)?.id));
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(6)), ("y", (object?)(0))));
        }

        [Fact(DisplayName = "rest and sleep > if the way to bed gets walled off, the survivor gives the bed up and sleeps on the floor")]
        public void T009_if_the_way_to_bed_gets_walled_off_the_survivor_gives_the_bed()
        {
            var state = setup(["s.......b"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            tire(state, s);
            runUntil(state, (_) => s.pos.x == 2);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(5), y = Js.I(0) });
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(Js.NN(s.sleep).bedId).toBeNull();
            expect(Js.NN(Js.Get(state.furniture, 0)).occupant).toBeNull();
        }

        [Fact(DisplayName = "rest and sleep > a direct order wakes a sleeper and frees the bed; still tired, they go back to bed after")]
        public void T010_a_direct_order_wakes_a_sleeper_and_frees_the_bed_still_tired()
        {
            var state = setup(["s.b...", "......"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            tire(state, s);
            runUntil(state, (_) => s.sleep?.asleep == true);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(5), y = Js.I(1) });
            expect(s.sleep).toBeNull();
            expect(Js.NN(Js.Get(state.furniture, 0)).occupant).toBeNull();
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(2)), ("y", (object?)(0))));
        }

        [Fact(DisplayName = "rest and sleep > setRest validates its input")]
        public void T011_setRest_validates_its_input()
        {
            var state = setup(["s"]);
            expect(Commands.applyCommand(state, new Command { type = "setRest", survivorId = "s1", rest = -1 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setRest", survivorId = "s1", rest = 2001 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setRest", survivorId = "s1", rest = 1.5 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setRest", survivorId = "s9", rest = 10 }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "setRest", survivorId = "s1", rest = 10 })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
        }
    }
}
