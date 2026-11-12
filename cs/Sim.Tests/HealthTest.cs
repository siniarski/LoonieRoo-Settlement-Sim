// Ported from tests/health.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class HealthTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 3000) => TestKit.runUntil(state, done, max);

    public static SimState setup(List<string> map)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) {  };
        }
        return state;
    }

    /// <summary>health and death</summary>
    public class D001_health_and_death
    {

        [Fact(DisplayName = "health and death > starving costs health every tick")]
        public void T002_starving_costs_health_every_tick()
        {
            var state = setup(["s..."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = s.id, satiety = 0 });
            for (var i = 0; i < 10; i++)
            {
                Tick.tick(state);
            }
            expect(s.health).toBe(Health.HEALTH_MAX - ((double)(10) * (Health.STARVE_DAMAGE)));
        }

        [Fact(DisplayName = "health and death > a starving survivor dies, is removed cleanly, and the death is logged")]
        public void T003_a_starving_survivor_dies_is_removed_cleanly_and_the_death_is()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.w.....b", "........."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) { ["haul"] = 3 };
            Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(6), y = Js.I(1), structure = "wall" });
            runUntil(state, (_) => s.carrying != null);
            Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = s.id, satiety = 0 });
            Commands.applyCommand(state, new Command { type = "setHealth", survivorId = s.id, health = 5 });
            runUntil(state, (st) => st.survivors.length == 0, 20);
            expect(state.events.map((e) => e.message)).toEqual(Js.Arr(["Ash died of starvation"]));
            expect(state.items.every((i) => ((i.carriedBy == null) && (i.reservedBy == null)) && (i.pos != null))).toBe(true);
            expect(state.jobs.every((j) => j.reservedBy == null)).toBe(true);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "health and death > a death frees a bed and lets the next survivor in the list still update that tick")]
        public void T004_a_death_frees_a_bed_and_lets_the_next_survivor_in_the_list_s()
        {
            var state = setup(["s.b...s"]);
            var (a, b) = state.survivors;
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = Js.NN(a).id, rest = Rest.TIRED - 1 });
            runUntil(state, (_) => Js.NN(a).sleep?.asleep == true);
            Commands.applyCommand(state, new Command { type = "setHealth", survivorId = Js.NN(a).id, health = 1 });
            Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = Js.NN(a).id, satiety = 0 });
            var restBefore = Js.NN(b).rest;
            Tick.tick(state);
            expect(state.survivors).toEqual(Js.Arr([b]));
            expect(Js.NN(b).rest).toBe(restBefore - 1);
            expect(Js.NN(Js.Get(state.furniture, 0)).occupant).toBeNull();
            expect(state.events.at(-1)?.message).toBe("Ash died of starvation");
        }

        [Fact(DisplayName = "health and death > a hurt worker with no bandage about asks again later, not every tick")]
        public void T004b_a_hurt_worker_with_no_bandage_asks_again_later()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s....", "....."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.health = Health.HEALTH_MAX / 2;
            s.jobId = "no-such-job";
            expect(Medicine.remediesFor(s).length).toBeGreaterThan(0);
            Needs.interruptForNeeds(state, s);
            expect(s.nextTreatTick).toBe(state.tick + Health.TREAT_RETRY_TICKS);
            expect(s.jobId).toBe("no-such-job");
        }

        [Fact(DisplayName = "health and death > health 0 from anything else counts as injuries")]
        public void T005_health_0_from_anything_else_counts_as_injuries()
        {
            var state = setup(["s"]);
            Commands.applyCommand(state, new Command { type = "setHealth", survivorId = "s1", health = 0 });
            Tick.tick(state);
            expect(state.survivors).toEqual(Js.Arr([]));
            expect(state.events.at(-1)?.message).toBe("Ash died of injuries");
        }

        [Fact(DisplayName = "health and death > heals while asleep, and slowly while awake and fed")]
        public void T006_heals_while_asleep_and_slowly_while_awake_and_fed()
        {
            var state = setup(["s.b"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "setHealth", survivorId = s.id, health = 500 });
            for (var i = 0; i < 50; i++)
            {
                Tick.tick(state);
            }
            expect(s.health).toBe(510);
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = Rest.TIRED - 1 });
            runUntil(state, (_) => s.sleep?.asleep == true);
            var asleepAt = s.health;
            for (var i = 0; i < 20; i++)
            {
                Tick.tick(state);
            }
            expect(s.health).toBe(asleepAt + 20);
        }
    }

    /// <summary>sleep follows the clock</summary>
    public class D007_sleep_follows_the_clock
    {

        [Fact(DisplayName = "sleep follows the clock > at night survivors go to bed below 75% rest instead of 25%")]
        public void T008_at_night_survivors_go_to_bed_below_75_rest_instead_of_25()
        {
            expect(Rest.NIGHT_TIRED).toBeGreaterThan(Rest.TIRED);
            var state = setup(["s.b"]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = 1200 });
            Tick.tick(state);
            expect(s.sleep).toBeNull();
            state.tick = Js.I(Time.DAY_LENGTH - ((double)(2) * (Time.TICKS_PER_HOUR)));
            expect(Time.isNight(state.tick)).toBe(true);
            Commands.applyCommand(state, new Command { type = "setRest", survivorId = s.id, rest = 1200 });
            Tick.tick(state);
            expect(s.sleep).not.toBeNull();
        }
    }
}
