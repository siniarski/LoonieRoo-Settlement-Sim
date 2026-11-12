// Ported from tests/defences2.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class Defences2Test
{


    public static readonly double NIGHT = ((double)(17) * (Time.TICKS_PER_HOUR)) + 1;

    /// <summary>reinforcing</summary>
    public class D001_reinforcing
    {

        [Fact(DisplayName = "reinforcing > builders reinforce a wall with scrap: it takes far more battering and does not burn")]
        public void T002_builders_reinforce_a_wall_with_scrap_it_takes_far_more_batte()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 6, map = ["s.....", "..#..5"] });
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) { ["build"] = 3, ["haul"] = 3 };
            expect(Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(2), y = Js.I(1), structure = "reinforce" }).ok).toBe(true);
            runUntil(state, (_) => Defences.isReinforced(state, 2, 1), 600);
            expect(state.items.some((i) => i.kind == "scrap")).toBe(false);
            expect(FireMod.burnable(state, 2, 1)).toBe(false);
            expect(Breach.damageStructure(state, 2, 1, Breach.STRUCTURE_HEALTH["wall"])).toBe(false);
            expect(Grids.getTile(state.grid, 2, 1)).toBe("wall");
        }
    }

    /// <summary>turrets and pits</summary>
    public class D003_turrets_and_pits
    {

        [Fact(DisplayName = "turrets and pits > a loaded turret shoots zombies in range")]
        public void T004_a_loaded_turret_shoots_zombies_in_range()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 6, map = ["6........"] });
            var t = Js.NN(Js.Get(state.furniture, 0));
            t.stored = 1;
            t.storedKind = "ammo";
            state.tick = Js.I(((double)(Defences.TURRET_EVERY) * (10)));
            Zombies.addZombie(state, 5, 0, 100, "static");
            for (var i = 0; i < (Defences.TURRET_EVERY + 1); i++)
            {
                Tick.tick(state);
            }
            expect(state.zombies).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "turrets and pits > a zombie stepping into a pit is held there")]
        public void T005_a_zombie_stepping_into_a_pit_is_held_there()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 6, map = ["s.0......."] });
            Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
            var z = Zombies.addZombie(state, 3, 0, 200, "static");
            runUntil(state, (_) => z.stuckUntil > 0, 100);
            var at = Js.Clone(z.pos);
            for (var i = 0; i < (Defences.PIT_TICKS - 5); i++)
            {
                Tick.tick(state);
            }
            expect(z.pos).toEqual(at);
        }
    }

    /// <summary>darkness</summary>
    public class D006_darkness
    {

        [Fact(DisplayName = "darkness > in a harsh game at night, only what someone can see can be fought")]
        public void T007_in_a_harsh_game_at_night_only_what_someone_can_see_can_be_fo()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 6, map = ["s" + ".".repeat(19)] });
            state.tick = Js.I(NIGHT);
            expect(Defences.seen(state, new Point { x = Js.I(15), y = Js.I(0) })).toBe(true);
            state.harsh = true;
            expect(Defences.seen(state, new Point { x = Js.I(Defences.NIGHT_SIGHT), y = Js.I(0) })).toBe(true);
            expect(Defences.seen(state, new Point { x = Js.I(15), y = Js.I(0) })).toBe(false);
        }
    }
}
