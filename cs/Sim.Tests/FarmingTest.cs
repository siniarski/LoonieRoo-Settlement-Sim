// Ported from tests/farming.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class FarmingTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 4000)
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

    /// <summary>Farmers only (and haulers when asked), so other work doesn't interfere.</summary>
    public static SimState setup(List<string> map, bool haul = false)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 2, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { __r0["farm"] = 3; Js.MergeInto(__r0, Js.T(haul) ? new() { ["haul"] = 3 } : new() {  }); });
        }
        return state;
    }

    /// <summary>farming</summary>
    public class D001_farming
    {

        [Fact(DisplayName = "farming > plots go on bare floor only; Cancel removes them")]
        public void T002_plots_go_on_bare_floor_only_Cancel_removes_them()
        {
            var state = setup([".#c."]);
            CommandResult farm(double x)
            {
                return Commands.applyCommand(state, new Command { type = "designateFarm", x = Js.I(x), y = Js.I(0) });
            }
            expect(farm(0)).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(true))));
            expect(farm(0)).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(farm(1)).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(farm(2)).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(DebugText.dumpText(state)).toBe("~#c.");
            Commands.applyCommand(state, new Command { type = "cancelDesignation", x = Js.I(0), y = Js.I(0) });
            expect(state.farm).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "farming > a plot is sown, grows, is harvested into food, and gets sown again")]
        public void T003_a_plot_is_sown_grows_is_harvested_into_food_and_gets_sown_ag()
        {
            var state = setup(["s..~"]);
            var plot = Js.NN(Js.Get(state.farm, 0));
            runUntil(state, (_) => plot.planted);
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toBe("sowed (3, 0)");
            var sownAt = state.tick;
            runUntil(state, (_) => Farming.isRipe(plot));
            expect(state.tick - sownAt).toBe(Farming.GROW_TICKS);
            runUntil(state, (_) => state.items.length == 1);
            expect(Js.Missing(state.items, 0) ? null : (object?)(Js.Get(state.items, 0))).toMatchObject(Js.Obj(("kind", (object?)("food"))));
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toBe("harvested food at (3, 0)");
            runUntil(state, (_) => plot.planted, Farming.FARM_WORK + 100);
        }

        [Fact(DisplayName = "farming > crops only grow once sown")]
        public void T004_crops_only_grow_once_sown()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 2, map = ["~"] });
            for (var i = 0; i < 100; i++)
            {
                Tick.tick(state);
            }
            expect(Js.Missing(state.farm, 0) ? null : (object?)(Js.Get(state.farm, 0))).toMatchObject(Js.Obj(("planted", (object?)(false)), ("growth", (object?)(0))));
        }

        [Fact(DisplayName = "farming > building on a plot destroys it")]
        public void T005_building_on_a_plot_destroys_it()
        {
            var state = setup(["s.~~"]);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(3), y = Js.I(0) });
            Tick.tick(state);
            expect(state.farm.map((p) => p.x)).toEqual(Js.Arr([2]));
        }

        [Fact(DisplayName = "farming > two farmers never tend the same plot")]
        public void T006_two_farmers_never_tend_the_same_plot()
        {
            var state = setup(["s..~..s"]);
            for (var i = 0; i < 40; i++)
            {
                Tick.tick(state);
                expect(state.survivors.filter((s) => Js.T(s.jobId)).length).toBeLessThanOrEqual(1);
            }
        }

        [Fact(DisplayName = "farming > three survivors with 12 plots live through three days on what they grow")]
        public void T007_three_survivors_with_12_plots_live_through_three_days_on_wha()
        {
            string inner(string row)
            {
                return $"#{Js.Str(row)}#";
            }
            List<string> map = ["#".repeat(16), inner(".............."), inner("..~~~~~~......"), inner("..~~~~~~......"), inner(".............."), inner("...s..s..s...."), inner("...........fff"), "#".repeat(16)];
            var state = setup(map);
            for (var i = 0; i < ((double)(3) * (Time.DAY_LENGTH)); i++)
            {
                Tick.tick(state);
            }
            expect(state.events.filter((e) => e.message.includes("died"))).toEqual(Js.Arr([]));
            expect(state.survivors).toHaveLength(3);
        }
    }
}
