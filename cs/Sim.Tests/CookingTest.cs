// Ported from tests/cooking.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class CookingTest
{


    public static SimState setup(List<string> map, JsRecord<double>? jobs = null)
    {
        jobs ??= new() { ["cook"] = 3 };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = map });
        TestKit.setJobs(state, jobs);
        return state;
    }

    public static int count(SimState state, string kind)
    {
        return state.items.filter((i) => i.kind == kind).length;
    }

    /// <summary>cooking</summary>
    public class D001_cooking
    {

        [Fact(DisplayName = "cooking > a cook carries raw food to the stove and makes hot meals, up to two per survivor")]
        public void T002_a_cook_carries_raw_food_to_the_stove_and_makes_hot_meals_up_()
        {
            var state = setup(["s.o.ffff", "........"]);
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).note?.startsWith("cooked a hot meal") == true);
            expect(state.jobs.some((j) => j.type == "cook") || (count(state, "meal") == 1)).toBe(true);
            runUntil(state, (_) => count(state, "meal") == Crafting.MEALS_PER_SURVIVOR);
            for (var i = 0; i < 150; i++)
            {
                Tick.tick(state);
            }
            expect(count(state, "meal")).toBe(Crafting.MEALS_PER_SURVIVOR);
            expect(count(state, "food")).toBe(4 - Crafting.MEALS_PER_SURVIVOR);
        }

        [Fact(DisplayName = "cooking > a hungry survivor picks a hot meal over raw food: it fills more and lifts the mood")]
        public void T003_a_hungry_survivor_picks_a_hot_meal_over_raw_food_it_fills_mo()
        {
            var state = setup(["f...s...h"], new() {  });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "setSatiety", survivorId = s.id, satiety = Food.HUNGRY - 1 });
            runUntil(state, (_) => (s.meal == null) && (s.satiety > Food.HUNGRY));
            expect(count(state, "meal")).toBe(0);
            expect(count(state, "food")).toBe(1);
            expect(s.satiety).toBeGreaterThan((Food.HUNGRY - 1) + Food.MEAL_VALUE);
            expect(s.thoughts.map((t) => t.kind)).toContain("hotMeal");
        }

        [Fact(DisplayName = "cooking > a removed stove gives its food back")]
        public void T004_a_removed_stove_gives_its_food_back()
        {
            var state = setup(["s.o.ff", "......"]);
            var stove = Js.NN(Js.Get(state.furniture, 0));
            runUntil(state, (_) => stove.stored == 1);
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(2), y = Js.I(0) });
            Tick.tick(state);
            expect(count(state, "food")).toBe(2);
            expect(Invariants.checkInvariants(state)).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "cooking > survivors build stoves")]
        public void T005_survivors_build_stoves()
        {
            var state = setup(["O..", "s.w"], new() { ["build"] = 3, ["haul"] = 3 });
            runUntil(state, (_) => state.furniture.some((f) => f.kind == "stove"));
        }
    }
}
