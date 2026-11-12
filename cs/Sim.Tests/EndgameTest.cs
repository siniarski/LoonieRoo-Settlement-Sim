// Ported from tests/endgame.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class EndgameTest
{


    /// <summary>the cure</summary>
    public class D001_the_cure
    {

        [Fact(DisplayName = "the cure > a researcher brings samples to a powered lab bench and studies them; enough and you win")]
        public void T002_a_researcher_brings_samples_to_a_powered_lab_bench_and_studi()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = ["s.1.3", "nn..E"] });
            Js.NN(state.furniture.find((f) => f.kind == "generator")).stored = 4;
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) { ["research"] = 1 };
            runUntil(state, (_) => state.endgame.cure == 2, 1500);
            expect(state.items.filter((i) => i.kind == "sample")).toEqual(Js.Arr([]));
            state.endgame.cure = EndgameMod.CURE_SAMPLES - 1;
            state.items.push(new() { id = "smp", kind = "sample", pos = new Point { x = Js.I(2), y = Js.I(1) }, carriedBy = null, reservedBy = null, wear = 0, tag = null });
            runUntil(state, (_) => state.endgame.won != null, 1500);
            expect(state.endgame.won).toBe("cure");
        }

        [Fact(DisplayName = "the cure > no power, no research")]
        public void T003_no_power_no_research()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = ["s.1..", "nn..."] });
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) { ["research"] = 1 };
            Tick.tick(state);
            expect(state.jobs.filter((j) => j.type == "research")).toEqual(Js.Arr([]));
        }
    }

    /// <summary>evacuation and sieges</summary>
    public class D004_evacuation_and_sieges
    {

        [Fact(DisplayName = "evacuation and sieges > a powered beacon and radio get through; the helicopter comes days later, after a siege night")]
        public void T005_a_powered_beacon_and_radio_get_through_the_helicopter_comes_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = ["s.3I.E"] });
            Js.NN(state.furniture.find((f) => f.kind == "generator")).stored = 4;
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
            Tick.tick(state);
            expect(state.endgame.evacuation).toBeNull();
            state.tick = Js.I(((double)((EndgameMod.EVAC_FROM_DAY - 1)) * (Time.DAY_LENGTH)));
            state.zombiesTonight = 0;
            Tick.tick(state);
            var evac = Js.NN(state.endgame.evacuation);
            expect(Time.dayOf(evac)).toBe(EndgameMod.EVAC_FROM_DAY + EndgameMod.EVAC_DAYS);
            expect(EndgameMod.siegeFactor(state, Time.dayOf(evac) - 1)).toBe(EndgameMod.EVAC_SIEGE);
            state.tick = Js.I(evac);
            state.zombies = [];
            Tick.tick(state);
            expect(state.endgame.won).toBe("evacuation");
        }

        [Fact(DisplayName = "evacuation and sieges > every few nights is a siege")]
        public void T006_every_few_nights_is_a_siege()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = ["s"] });
            expect(EndgameMod.siegeFactor(state, EndgameMod.SIEGE_EVERY)).toBe(1);
            expect(EndgameMod.siegeFactor(state, EndgameMod.FIRST_SIEGE)).toBe(EndgameMod.SIEGE_FACTOR);
            expect(EndgameMod.siegeFactor(state, EndgameMod.FIRST_SIEGE + 1)).toBe(1);
            expect(EndgameMod.daysToSiege(((double)((EndgameMod.FIRST_SIEGE - 1)) * (Time.DAY_LENGTH)))).toBe(0);
        }
    }
}
