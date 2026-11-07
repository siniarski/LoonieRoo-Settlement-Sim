// Ported from tests/forage.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ForageTest
{

    public static readonly List<string> MAP = Js.ArrayFrom(9, (int y) => (y == 4) ? "....s...." : ".........");

    public static HungryResult hungry()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = MAP });
        state.tick = Js.I(((double)(Time.DAY_LENGTH) / (2)));
        var s = Js.NN(Js.Get(state.survivors, 0));
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
        {
            s.priorities[Js.Idx(k)] = 0;
        }
        s.satiety = Food.HUNGRY - 10;
        return new HungryResult { state = state, s = s };
    }

    /// <summary>colonists forage</summary>
    public class D001_colonists_forage
    {

        [Fact(DisplayName = "colonists forage > hungry, with no food anywhere, they forage until something turns up, and eat it")]
        public void T002_hungry_with_no_food_anywhere_they_forage_until_something_tur()
        {
            var __d0 = hungry();
            var state = __d0.state;
            var s = __d0.s;
            var foraged = false;
            for (var t = 0; (t < 3000) && (s.satiety < Food.HUNGRY); t++)
            {
                Tick.tick(state);
                if (Js.T(s.forage))
                {
                    foraged = true;
                }
            }
            expect(foraged).toBe(true);
            expect(s.satiety).toBeGreaterThanOrEqual(Food.HUNGRY);
            expect(Js.ObjectKeys(state.foraged).length).toBeGreaterThan(0);
        }

        [Fact(DisplayName = "colonists forage > not with food to go to, and not at night")]
        public void T003_not_with_food_to_go_to_and_not_at_night()
        {
            var fed = hungry();
            Items.spawnItem(fed.state, "food", 0, 0);
            Tick.tick(fed.state);
            expect(fed.s.forage ?? null).toBeNull();
            var dark = hungry();
            while (!Time.isNight(dark.state.tick))
            {
                dark.state.tick = Js.I(dark.state.tick + 10);
            }
            for (var t = 0; t < 20; t++)
            {
                Tick.tick(dark.state);
            }
            expect(dark.s.forage ?? null).toBeNull();
        }
    }
}
