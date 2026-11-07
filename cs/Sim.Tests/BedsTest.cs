// Ported from tests/beds.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class BedsTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 1500)
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

    public static SimState setup(List<string> map, List<ScenarioRoomsItem>? rooms = null, JsRecord<double>? jobs = null)
    {
        rooms ??= new();
        jobs ??= new() {  };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 6, map = map, rooms = rooms });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        return state;
    }

    public static CommandResult tire(SimState state, Survivor s)
    {
        return Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = 0 });
    }

    public static void wakeUp(SimState state, Survivor s)
    {
        Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = 2000 });
        Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(s.pos.x), y = Js.I(s.pos.y + 1) });
    }

    /// <summary>bed ownership</summary>
    public class D001_bed_ownership
    {

        [Fact(DisplayName = "bed ownership > your first bed becomes yours, and you go back to it, not a nearer one")]
        public void T002_your_first_bed_becomes_yours_and_you_go_back_to_it_not_a_nea()
        {
            var state = setup(["b.....b", "......s", "......."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var (far, near) = state.furniture;
            tire(state, s);
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(Js.NN(near).owner).toBe(s.id);
            wakeUp(state, s);
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(0), y = Js.I(1) });
            runUntil(state, (_) => ((s.pos.x == 0) && (s.pos.y == 1)) && !Js.T(s.goal));
            tire(state, s);
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(Js.NN(s.sleep).bedId).toBe(Js.NN(near).id);
            expect(Js.NN(far).owner).toBeNull();
        }

        [Fact(DisplayName = "bed ownership > nobody sleeps in someone else's bed; with no other bed it's the floor")]
        public void T003_nobody_sleeps_in_someone_else_s_bed_with_no_other_bed_it_s_t()
        {
            var state = setup(["b..", "s.s"]);
            var (ash, bea) = state.survivors;
            Commands.applyCommand(state, new Command { type = "assignBed", bedId = Js.NN(Js.Get(state.furniture, 0)).id, survivorId = ash.id });
            tire(state, bea);
            runUntil(state, (_) => bea.sleep?.asleep == true);
            expect(Js.NN(bea.sleep).bedId).toBeNull();
        }

        [Fact(DisplayName = "bed ownership > a death frees the bed")]
        public void T004_a_death_frees_the_bed()
        {
            var state = setup(["b..", "s.s"]);
            var (ash, bea) = state.survivors;
            Commands.applyCommand(state, new Command { type = "assignBed", bedId = Js.NN(Js.Get(state.furniture, 0)).id, survivorId = ash.id });
            Needs.killSurvivor(state, ash, "a test");
            expect(Js.NN(Js.Get(state.furniture, 0)).owner).toBeNull();
            tire(state, bea);
            runUntil(state, (_) => bea.sleep?.asleep == true);
            expect(Js.NN(bea.sleep).bedId).toBe(Js.NN(Js.Get(state.furniture, 0)).id);
        }

        [Fact(DisplayName = "bed ownership > giving a bed away moves its sleeper out, and you only ever own one")]
        public void T005_giving_a_bed_away_moves_its_sleeper_out_and_you_only_ever_ow()
        {
            var state = setup(["b.b", "s.s"]);
            var (ash, bea) = state.survivors;
            var (first, second) = state.furniture;
            Commands.applyCommand(state, new Command { type = "assignBed", bedId = Js.NN(first).id, survivorId = ash.id });
            Commands.applyCommand(state, new Command { type = "assignBed", bedId = Js.NN(second).id, survivorId = ash.id });
            expect(Js.Arr([Js.NN(first).owner, Js.NN(second).owner])).toEqual(Js.Arr([null, ash.id]));
            tire(state, bea);
            runUntil(state, (_) => bea.sleep?.asleep == true);
            expect(Js.NN(bea.sleep).bedId).toBe(Js.NN(first).id);
            Commands.applyCommand(state, new Command { type = "assignBed", bedId = Js.NN(first).id, survivorId = ash.id });
            expect(bea.sleep).toBeNull();
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "bed ownership > infirmary beds stay shared")]
        public void T006_infirmary_beds_stay_shared()
        {
            var state = setup(["####.", "#b.#.", "#..#.", "##D#.", "...s."], [new() { x = Js.I(1), y = Js.I(2), purpose = "infirmary" }]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            tire(state, s);
            runUntil(state, (_) => s.sleep?.asleep == true);
            expect(Js.NN(s.sleep).bedId).toBe(Js.NN(Js.Get(state.furniture, 0)).id);
            expect(Js.NN(Js.Get(state.furniture, 0)).owner).toBeNull();
        }
    }

    /// <summary>barracks</summary>
    public class D007_barracks
    {

        public static readonly List<string> MAP = ["####........", "#b.#........", "#..#.....b..", "##D#........", "...s........", "............"];

        public static readonly ScenarioRoomsItem BARRACKS = new ScenarioRoomsItem { x = Js.I(1), y = Js.I(2), purpose = "barracks" };

        [Fact(DisplayName = "barracks > guards take barracks beds; others leave them be")]
        public void T008_guards_take_barracks_beds_others_leave_them_be()
        {
            var guard = setup(MAP, [BARRACKS], new() { ["guard"] = 3 });
            tire(guard, Js.NN(Js.Get(guard.survivors, 0)));
            runUntil(guard, (_) => Js.NN(Js.Get(guard.survivors, 0)).sleep != null);
            expect(Js.NN(Js.NN(Js.Get(guard.survivors, 0)).sleep).bedId).toBe(Js.NN(Js.Get(guard.furniture, 0)).id);
            var civilian = setup(MAP, [BARRACKS]);
            tire(civilian, Js.NN(Js.Get(civilian.survivors, 0)));
            runUntil(civilian, (_) => Js.NN(Js.Get(civilian.survivors, 0)).sleep != null);
            expect(Js.NN(Js.NN(Js.Get(civilian.survivors, 0)).sleep).bedId).toBe(Js.NN(Js.Get(civilian.furniture, 1)).id);
        }

        [Fact(DisplayName = "barracks > a guard asleep in the barracks wakes when a zombie comes near; elsewhere they sleep on")]
        public void T009_a_guard_asleep_in_the_barracks_wakes_when_a_zombie_comes_nea()
        {
            Survivor run(List<ScenarioRoomsItem> rooms)
            {
                var state = setup(MAP, rooms, new() { ["guard"] = 3 });
                var s = Js.NN(Js.Get(state.survivors, 0));
                tire(state, s);
                runUntil(state, (_) => s.sleep?.asleep == true);
                state.tick = Js.I(((double)(17) * (Time.TICKS_PER_HOUR)) + 1);
                Zombies.addZombie(state, 6, 5);
                Tick.tick(state);
                return s;
            }
            var inBarracks = run([BARRACKS]);
            expect(inBarracks.sleep).toBeNull();
            expect(inBarracks.note).toMatch(new JsRegExp("^(woken for guard duty|took guard job)", ""));
            expect(run([]).sleep?.asleep).toBe(true);
        }

        [Fact(DisplayName = "barracks > the planner gives every guard a bed, up to capacity")]
        public void T010_the_planner_gives_every_guard_a_bed_up_to_capacity()
        {
            var state = setup(["#####", "#...#", "#...#", "##D##", "s.s.s"], [new() { x = Js.I(1), y = Js.I(1), purpose = "barracks" }]);
            Js.NN(Js.Get(state.survivors, 0)).priorities["guard"] = 1;
            Js.NN(Js.Get(state.survivors, 1)).priorities["guard"] = 2;
            expect(Planner.planFurnish(state, 1, 1).commands).toHaveLength(1);
            expect(Js.Missing(Planner.planFurnish(state, 1, 1).notes, 0) ? null : (object?)(Js.Get(Planner.planFurnish(state, 1, 1).notes, 0))).toBe("Planned 1 bed for the guards (room holds 1, 2 guarding).");
        }
    }
}
