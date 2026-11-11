// Ported from tests/succession.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class SuccessionTest
{

    public static Survivor dies(SimState state)
    {
        var you = Js.NN(state.survivors.find((s) => s.id == Js.NN(state.control).survivorId));
        you.health = 0;
        Tick.tick(state);
        return you;
    }

    /// <summary>the death rule</summary>
    public class D001_the_death_rule
    {

        [Fact(DisplayName = "the death rule > your death asks who carries on; someone resolved enough can")]
        public void T002_your_death_asks_who_carries_on_someone_resolved_enough_can()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s.s"] });
            var (you, steady, shaky) = state.survivors;
            steady.resolve = 70;
            shaky.resolve = SuccessionMod.STEP_UP - 1;
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
            dies(state);
            expect(state.succession?.name).toBe(you.name);
            expect(state.control).toBeNull();
            expect(SuccessionMod.heirs(state).map((s) => s.id)).toEqual(Js.Arr([steady.id]));
            expect(Commands.applyCommand(state, new Command { type = "succeed", choice = shaky.id }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "succeed", choice = steady.id })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            expect(Js.NN(state.control).survivorId).toBe(steady.id);
            expect(state.succession).toBeNull();
        }

        [Fact(DisplayName = "the death rule > or start anew, far off, alone and kitted out")]
        public void T003_or_start_anew_far_off_alone_and_kitted_out()
        {
            var state = Worldgen.createWorld(7, 256, Js.With(Js.CopyAs<Settings>(Difficulty.NORMAL), __s0 => { __s0.start = "solo"; }));
            var you = Js.NN(Js.Get(state.survivors, 0));
            var at = Js.Clone(you.pos);
            dies(state);
            expect(state.succession).not.toBeNull();
            expect(Commands.applyCommand(state, new Command { type = "succeed", choice = "anew" })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            var fresh = Js.NN(state.survivors.find((s) => s.id == Js.NN(state.control).survivorId));
            expect(JsMath.max(JsMath.abs(fresh.pos.x - at.x), JsMath.abs(fresh.pos.y - at.y))).toBeGreaterThan(30);
            expect(fresh.weapon).not.toBeNull();
        }

        [Fact(DisplayName = "the death rule > the death says when it was, and what they had killed (the death screen)")]
        public void T006_the_death_says_when_and_what_they_had_killed()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s"] });
            var (you, _) = state.survivors;
            you.kills = 3;
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
            state.tick = 777;
            dies(state);
            expect(state.succession.tick).toBeGreaterThan(776);
            expect(state.succession.kills).toBe(3);
        }

        [Fact(DisplayName = "alerts > the time to night is hours and minutes, not a raw double")]
        public void T007_the_time_to_night_is_hours_and_minutes()
        {
            expect(Alerts.untilText(2.25)).toBe("2h 15m");
            expect(Alerts.untilText(3)).toBe("3h 00m");
            expect(Alerts.untilText(0.5)).toBe("30m");
            expect(Alerts.untilText(2.9999999999999996)).toBe("3h 00m");
            expect(Alerts.untilText(1.0 / 3)).toBe("20m");
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s"] });
            state.tick = Time.TICKS_PER_HOUR * 12 + 37; // 18:02 on day 1 (the clock starts at 06:00)
            var text = Alerts.alerts(state).map((a) => a.text).find((t) => t.StartsWith("Night in"));
            expect(text).not.toBeNull();
            expect(System.Text.RegularExpressions.Regex.IsMatch(text, @"^Night in (\d+h \d\dm|\d+m): about")).toBe(true);
        }

        [Fact(DisplayName = "the death rule > or just watch the colony")]
        public void T004_or_just_watch_the_colony()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s"] });
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            dies(state);
            expect(Commands.applyCommand(state, new Command { type = "succeed", choice = "none" })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            expect(state.control).toBeNull();
            expect(state.succession).toBeNull();
        }

        [Fact(DisplayName = "the death rule > losing someone you were not controlling asks nothing")]
        public void T005_losing_someone_you_were_not_controlling_asks_nothing()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s"] });
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            Js.NN(Js.Get(state.survivors, 1)).health = 0;
            Tick.tick(state);
            expect(state.succession).toBeNull();
        }
    }
}
