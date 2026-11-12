// Ported from tests/fire.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
using static LoonieRoo.Sim.Tests.TestKit;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class FireTest
{


    public static SimState setup(List<string> map, JsRecord<double>? jobs = null, List<ScenarioRoomsItem>? rooms = null)
    {
        jobs ??= new() {  };
        rooms ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 9, map = map, rooms = rooms });
        TestKit.setJobs(state, jobs);
        return state;
    }

    /// <summary>fire</summary>
    public class D001_fire
    {

        [Fact(DisplayName = "fire > burns a wall down to floor if nobody puts it out, and spreads along walls")]
        public void T002_burns_a_wall_down_to_floor_if_nobody_puts_it_out_and_spreads()
        {
            var state = setup(["s.......", "..####..", "........"]);
            expect(FireMod.burnable(state, 2, 1)).toBe(true);
            FireMod.ignite(state, 2, 1);
            runUntil(state, (_) => Grids.getTile(state.grid, 2, 1) == "floor", FireMod.FIRE_BURN + 5);
            expect(state.events.some((e) => e.message.includes("burned down"))).toBe(true);
            expect((state.fires.length > 0) || (Grids.getTile(state.grid, 3, 1) == "floor")).toBe(true);
        }

        [Fact(DisplayName = "fire > hurts survivors and zombies standing in it")]
        public void T003_hurts_survivors_and_zombies_standing_in_it()
        {
            var state = setup(["s.."]);
            var s = Js.NN(Js.Get(state.survivors, 0));
            var z = Zombies.addZombie(state, 2, 0, 200, "static");
            FireMod.ignite(state, 0, 0);
            FireMod.ignite(state, 2, 0);
            var zh = z.health;
            Tick.tick(state);
            expect(s.health).toBeLessThan(Health.HEALTH_MAX);
            expect(z.health).toBeLessThan(zh);
        }

        [Fact(DisplayName = "fire > firefighters put a fire out")]
        public void T004_firefighters_put_a_fire_out()
        {
            var state = setup(["s......", "...#..."], new() { ["firefight"] = 3 });
            state.settings.wildfire = 0; // a hand-made map is all grass: this is the old rule, a lone wall (wildfire has its own tests)
            FireMod.ignite(state, 3, 1);
            runUntil(state, (_) => state.fires.length == 0, 400);
            expect(Grids.getTile(state.grid, 3, 1)).toBe("wall");
            expect(Js.NN(Js.Get(state.survivors, 0)).note).toMatch(new JsRegExp("^put out a fire", ""));
        }

        [Fact(DisplayName = "fire > the Burn order sets explored ground alight, not water")]
        public void T005_the_Burn_order_sets_explored_ground_alight_not_water()
        {
            var state = setup(["s..%"]);
            expect(Commands.applyCommand(state, new Command { type = "ignite", x = Js.I(2), y = Js.I(0) }).ok).toBe(true);
            expect(state.fires).toHaveLength(1);
            expect(Commands.applyCommand(state, new Command { type = "ignite", x = Js.I(3), y = Js.I(0) })).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
        }
    }

    /// <summary>corpses</summary>
    public class D006_corpses
    {

        [Fact(DisplayName = "corpses > only in a harsh game: the dead stay where they fall")]
        public void T007_only_in_a_harsh_game_the_dead_stay_where_they_fall()
        {
            var mild = setup(["ss."]);
            Needs.killSurvivor(mild, Js.NN(Js.Get(mild.survivors, 0)), "testing");
            expect(mild.items).toEqual(Js.Arr([]));
            var harsh = setup(["ss."]);
            harsh.harsh = true;
            Needs.killSurvivor(harsh, Js.NN(Js.Get(harsh.survivors, 0)), "testing");
            var z = Zombies.addZombie(harsh, 2, 0, 10, "static");
            Zombies.damageZombie(harsh, z, 50, Js.NN(Js.Get(harsh.survivors, 0)));
            expect(harsh.items.filter((i) => i.kind == "corpse").map((i) => Js.Arr([i.kind, i.tag]))).toEqual(Js.Arr([Js.Arr(["corpse", "Ash"]), Js.Arr(["corpse", null])]));
        }

        [Fact(DisplayName = "corpses > a rotting corpse sickens those nearby; medicine cures it")]
        public void T008_a_rotting_corpse_sickens_those_nearby_medicine_cures_it()
        {
            var state = setup(["s.&.", "W..."]);
            state.harsh = true;
            var s = Js.NN(Js.Get(state.survivors, 0));
            Js.NN(Js.Get(state.items, 0)).wear = -Corpses.CORPSE_ROT;
            Js.NN(Js.Get(state.items, 0)).tag = "Bea";
            runUntil(state, (_) => Js.Run(() => { s.satiety = 3000; return s.sick != null; }), ((double)(200) * (Corpses.CORPSE_CHECK_EVERY)));
            expect(s.thoughts.some((t) => t.kind == "stench")).toBe(true);
            expect(s.sick).toBeLessThanOrEqual(Corpses.SICK_TICKS);
            state.items.push(new() { id = "med", kind = "medicine", pos = new Point { x = Js.I(3), y = Js.I(1) }, carriedBy = null, reservedBy = null, wear = 0, tag = null });
            runUntil(state, (_) => s.sick == null, 400);
        }

        [Fact(DisplayName = "corpses > haulers carry the dead to a graveyard, where they stop rotting")]
        public void T009_haulers_carry_the_dead_to_a_graveyard_where_they_stop_rottin()
        {
            List<string> map = ["s.&.....", "###D####", "#......#", "#......#", "########"];
            var state = setup(map, new() { ["haul"] = 3 }, [new() { x = Js.I(1), y = Js.I(2), purpose = "graveyard" }]);
            state.harsh = true;
            runUntil(state, (_) => (Js.NN(Js.Get(state.items, 0)).pos != null) && (Js.NN(Js.Get(state.items, 0)).pos.y >= 2), 1500);
        }
    }
}
