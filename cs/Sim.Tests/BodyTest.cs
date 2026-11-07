// Ported from tests/body.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class BodyTest
{

    /// <summary>the body</summary>
    public class D001_the_body
    {

        [Fact(DisplayName = "the body > swinging spends endurance; it comes back at rest")]
        public void T002_swinging_spends_endurance_it_comes_back_at_rest()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "swing", x = Js.I(1), y = Js.I(0) });
            expect(s.endurance).toBe(100 - BodyMod.SWING_COST);
            for (var t = 0; t < 100; t++)
            {
                Tick.tick(state);
            }
            expect(s.endurance).toBe(100);
        }

        [Fact(DisplayName = "the body > running is RUN_PCT of walking speed, costs endurance by the tick, and stops when winded")]
        public void T003_running_is_RUN_PCT_of_walking_speed_costs_endurance_by_the_t()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s........."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0, run = true });
            var pace = Survivors.stepPace(state, s, s.pos);
            var x = s.at.x;
            for (var t = 0; t < 6; t++)
            {
                Tick.tick(state);
            }
            expect(s.at.x - x).toBe(((double)(6) * (JsMath.round(((double)(((double)(JsMath.round(((double)(Motion.SUB) / (pace)))) * (BodyMod.RUN_PCT))) / (100))))));
            expect(s.endurance).toBeLessThanOrEqual((100 - ((double)(6) * (BodyMod.RUN_TICK_COST))) + 2);
            expect(s.endurance).toBeLessThan(100);
            s.endurance = BodyMod.WINDED - 1;
            x = s.at.x;
            Tick.tick(state);
            expect(s.at.x - x).toBe(JsMath.round(((double)(Motion.SUB) / (pace))));
        }

        [Fact(DisplayName = "the body > zombies close by bring panic, faster alone; it ebbs when they go")]
        public void T004_zombies_close_by_bring_panic_faster_alone_it_ebbs_when_they_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s......"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            for (var i = 0; i < 4; i++)
            {
                Commands.applyCommand(state, new Command { type = "spawnZombie", x = Js.I(3 + i), y = Js.I(0) });
            }
            foreach (var z in Js.Iter(state.zombies))
            {
                z.kind = "static";
            }
            state.tick = Js.I(10);
            BodyMod.updateBody(state, s);
            expect(s.panic).toBeGreaterThan(0);
            s.panic = BodyMod.PANICKING + 1;
            state.zombies = [];
            state.tick = Js.I(20);
            BodyMod.updateBody(state, s);
            expect(s.panic).toBe(BodyMod.PANICKING - 3);
        }

        [Fact(DisplayName = "the body > rain soaks you outdoors; a cold night drains body heat until the cold sets in")]
        public void T005_rain_soaks_you_outdoors_a_cold_night_drains_body_heat_until_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            state.weather = "rain";
            for (var t = 0; t < ((double)(2) * (Time.TICKS_PER_HOUR)); t++)
            {
                state.tick = Js.I(t);
                BodyMod.updateBody(state, s);
            }
            expect(s.wetness).toBeGreaterThan(50);
            expect(s.thoughts.some((t) => t.kind == "soaked")).toBe(true);
            state.weather = "blizzard";
            for (var t = 0; t < 80; t++)
            {
                state.tick = Js.I(state.tick + 1);
                BodyMod.updateBody(state, s);
            }
            expect(s.bodyHeat).toBeLessThan(BodyMod.COLD);
            expect(s.thoughts.some((t) => t.kind == "cold")).toBe(true);
        }
    }
}
