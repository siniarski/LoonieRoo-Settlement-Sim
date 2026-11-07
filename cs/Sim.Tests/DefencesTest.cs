// Ported from tests/defences.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class DefencesTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 2000)
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
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}");
    }

    public static SimState setup(List<string> map, JsRecord<double>? jobs = null)
    {
        jobs ??= new() {  };
        var state = ScenarioMod.loadScenario(new Scenario { seed = 4, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = Js.RecordFrom(JobTypes.NO_JOBS, __r0 => { Js.MergeInto(__r0, jobs); });
        }
        state.tick = Js.I(((double)(17) * (Time.TICKS_PER_HOUR)) + 1);
        return state;
    }

    /// <summary>spike traps</summary>
    public class D001_spike_traps
    {

        [Fact(DisplayName = "spike traps > hurt a zombie that steps on them, use up a charge, and survivors cross them safely")]
        public void T002_hurt_a_zombie_that_steps_on_them_use_up_a_charge_and_survivo()
        {
            var state = setup(["z..^..s"]);
            var trap = Js.NN(Js.Get(state.furniture, 0));
            runUntil(state, (_) => trap.loot < FurnitureMod.TRAP_CHARGES);
            expect(Js.NN(Js.Get(state.zombies, 0)).health).toBe(200 - Zombies.TRAP_DAMAGE);
            expect(Js.NN(Js.Get(state.survivors, 0)).health).toBe(1000);
        }

        [Fact(DisplayName = "spike traps > a spent trap goes, leaving a plan to rebuild it, and two hits kill a basic zombie")]
        public void T003_a_spent_trap_goes_leaving_a_plan_to_rebuild_it_and_two_hits_()
        {
            var state = setup(["....^....s"]);
            Js.NN(Js.Get(state.furniture, 0)).loot = 2;
            foreach (var x in Js.Iter((List<double>)[0, 1]))
            {
                Zombies.addZombie(state, x, 0);
            }
            runUntil(state, (_) => (state.blueprints.length > 0) || (state.zombies.length == 0));
            runUntil(state, (_) => state.blueprints.length > 0);
            expect(state.blueprints).toMatchObject(Js.Arr([Js.Obj(("x", (object?)(4)), ("y", (object?)(0)), ("target", (object?)("trap")))]));
            expect(state.furniture).toEqual(Js.Arr([]));
            expect(state.events.map((e) => e.message)).toContain("A spike trap at (4, 0) is spent");
        }
    }

    /// <summary>barricades</summary>
    public class D004_barricades
    {

        [Fact(DisplayName = "barricades > survivors walk through them; zombies must batter them down")]
        public void T005_survivors_walk_through_them_zombies_must_batter_them_down()
        {
            var state = setup(["z.....=.....", "######.#####", "......s.....".replace("s", ".")]);
            expect(Pathfinding.findPath(state.grid, new Point { x = Js.I(0), y = Js.I(0) }, new Point { x = Js.I(11), y = Js.I(0) })).not.toBeNull();
            var fenced = setup(["z..=..s", "###=###"]);
            runUntil(fenced, (_) => fenced.grid.damageList.length > 0);
            expect(Js.NN(Js.Get(fenced.grid.damage, 3)) + Js.NN(Js.Get(fenced.grid.damage, 7 + 3))).toBeGreaterThan(0);
            expect(Breach.STRUCTURE_HEALTH["barricade"]).toBeLessThan(Breach.STRUCTURE_HEALTH["wall"]);
            runUntil(fenced, (_) => fenced.blueprints.length > 0, 3000);
            expect(fenced.blueprints.map((b) => $"{Js.Str(b.target)}@{Js.Str(b.x)}")).toEqual(Js.Arr(["barricade@3"]));
        }

        [Fact(DisplayName = "barricades > are built from wood, quickly")]
        public void T006_are_built_from_wood_quickly()
        {
            var state = setup(["-..", "s.w"], new() { ["build"] = 3, ["haul"] = 3 });
            state.tick = Js.I(((double)(6) * (Time.TICKS_PER_HOUR)));
            runUntil(state, (_) => Js.Get(state.grid.tiles, 0) == "barricade");
        }
    }
}
