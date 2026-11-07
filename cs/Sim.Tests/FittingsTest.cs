// Ported from tests/fittings.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class FittingsTest
{

    /// <summary>You (in control) at (1, 1) with a fitting of this look at (2, 1); the mains on or off.</summary>
    public static SimState withFitting(string look, bool mains)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", ".s...", "....."] });
        state.timeline = new Timeline { waterOffDay = mains ? 99 : 0, powerOffDay = mains ? 99 : 0, helicopterDay = 0, heli = null, alarms = [] };
        FurnitureMod.addFurniture(state, "decor", 2, 1).look = look;
        var s = Js.NN(Js.Get(state.survivors, 0));
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
        {
            s.priorities[Js.Idx(k)] = 0;
        }
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        return state;
    }

    /// <summary>a house's fittings</summary>
    public class D001_a_house_s_fittings
    {

        [Fact(DisplayName = "a house's fittings > an oven with the power on cooks food from your bag into a hot meal")]
        public void T002_an_oven_with_the_power_on_cooks_food_from_your_bag_into_a_ho()
        {
            var state = withFitting("oven", true);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var food = Items.spawnItem(state, "food", 1, 1);
            Inventory.stow(state, s, food);
            Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(1) });
            expect(Js.NN(state.control).busy?.what).toBe("cook");
            for (var k = 0; k <= ControlMod.COOK_TICKS; k++)
            {
                Tick.tick(state);
            }
            expect(Items.getItem(state, food.id)?.kind).toBe("meal");
            expect(s.pack).toContain(food.id);
        }

        [Fact(DisplayName = "a house's fittings > without the power the oven is dead")]
        public void T003_without_the_power_the_oven_is_dead()
        {
            var state = withFitting("oven", false);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Inventory.stow(state, s, Items.spawnItem(state, "food", 1, 1));
            Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(1) });
            expect(Js.NN(state.control).busy).toBeNull();
        }

        [Fact(DisplayName = "a house's fittings > you can sleep in a house's bed, and on a sofa (more slowly)")]
        public void T004_you_can_sleep_in_a_house_s_bed_and_on_a_sofa_more_slowly()
        {
            double rested(string look)
            {
                var state = withFitting(look, false);
                var s = Js.NN(Js.Get(state.survivors, 0));
                s.rest = 100;
                Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(1) });
                Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(1) });
                expect(Js.NN(state.control).busy?.what, look).toBe("sleep");
                for (var k = 0; k < 200; k++)
                {
                    Tick.tick(state);
                }
                return JsMath.min(Rest.REST_MAX, s.rest);
            }
            var bed = rested("bed");
            var sofa = rested("sofa");
            expect(sofa).toBeGreaterThan(100);
            expect(bed).toBeGreaterThan(sofa);
        }
    }
}
