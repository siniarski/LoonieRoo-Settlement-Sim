// Ported from tests/policies.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class PoliciesTest
{

    /// <summary>policies</summary>
    public class D001_policies
    {

        [Fact(DisplayName = "policies > rationing makes food last longer, at a cost in mood")]
        public void T002_rationing_makes_food_last_longer_at_a_cost_in_mood()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) {  };
            var before = Mood.mood(state, s);
            expect(Commands.applyCommand(state, new Command { type = "setPolicy", policy = "rationing", on = true }).ok).toBe(true);
            expect(Mood.mood(state, s)).toBeLessThan(before);
            s.satiety = Food.SATIETY_MAX;
            for (var i = 0; i < 400; i++)
            {
                Tick.tick(state);
            }
            expect(Food.SATIETY_MAX - s.satiety).toBe(300);
            // 300 drained in 400 ticks: food lasts 400/300, a third longer, and the policy's text says so
            expect(Js.Get(Policies.POLICY_INFO, "rationing")).toContain("a third longer");
        }

        [Fact(DisplayName = "policies > curfew: no outdoor work at night but guarding")]
        public void T003_curfew_no_outdoor_work_at_night_but_guarding()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.c"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) { ["scavenge"] = 3 };
            Commands.applyCommand(state, new Command { type = "designateScavenge", x = Js.I(2), y = Js.I(0) });
            Jobs.reconcileJobs(state);
            state.tick = Js.I(((double)(17) * (Time.TICKS_PER_HOUR)));
            state.zombiesTonight = 0;
            expect(JobChoice.chooseJob(state, s)).not.toBeNull();
            Commands.applyCommand(state, new Command { type = "setPolicy", policy = "curfew", on = true });
            expect(JobChoice.chooseJob(state, s)).toBeNull();
        }

        [Fact(DisplayName = "policies > quarantine sends the infected to bed")]
        public void T004_quarantine_sends_the_infected_to_bed()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.infection = 10;
            expect(Needs.wantsSleep(state, s)).toBe(false);
            Commands.applyCommand(state, new Command { type = "setPolicy", policy = "quarantine", on = true });
            expect(Needs.wantsSleep(state, s)).toBe(true);
        }
    }

    /// <summary>the leader</summary>
    public class D005_the_leader
    {

        [Fact(DisplayName = "the leader > is whoever the others think best of, and their death hits everyone")]
        public void T006_is_whoever_the_others_think_best_of_and_their_death_hits_eve()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["sss"] });
            var (a, b, c) = state.survivors;
            expect(Policies.chooseLeader(state)).toBeNull();
            Js.NN(b).opinions[Js.Idx(Js.NN(a).id)] = Policies.LEADER_MIN + 10;
            Js.NN(c).opinions[Js.Idx(Js.NN(a).id)] = Policies.LEADER_MIN + 10;
            expect(Policies.chooseLeader(state)).toBe(a);
            state.tick = Js.I(Time.DAY_LENGTH);
            Tick.tick(state);
            expect(state.leaderId).toBe(Js.NN(a).id);
            Needs.killSurvivor(state, Js.NN(a), "testing");
            expect(Js.NN(b).thoughts.some((t) => t.kind == "lostLeader")).toBe(true);
            expect(state.leaderId).toBeNull();
        }
    }

    /// <summary>wounds and amputation</summary>
    public class D007_wounds_and_amputation
    {

        [Fact(DisplayName = "wounds and amputation > a bleeding wound costs health until bandaged")]
        public void T008_a_bleeding_wound_costs_health_until_bandaged()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.bleeding = true;
            s.bleedUntil = 1000000000.0;
            var h = s.health;
            for (var t = 0; t < ((double)(Wounds.BLEED_EVERY) * (4)); t++)
            {
                state.tick = Js.I(t);
                Wounds.updateWounds(state, s);
            }
            expect(s.health).toBe(h - 4);
        }

        [Fact(DisplayName = "wounds and amputation > an amputation in time cures the infection, for good and at a price")]
        public void T009_an_amputation_in_time_cures_the_infection_for_good_and_at_a_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.infection = 10;
            s.bitten = "arm";
            expect(Wounds.amputate(state, s)).toBeNull();
            expect(s.infection).toBeNull();
            expect(Skills.hasTrait(s, "lostArm")).toBe(true);
            s.infection = Wounds.amputateWindow() + 1;
            s.bitten = "leg";
            expect(Wounds.amputate(state, s)).toMatch(new JsRegExp("too late", ""));
        }
    }
}
