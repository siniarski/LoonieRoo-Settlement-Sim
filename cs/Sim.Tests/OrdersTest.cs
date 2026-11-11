// Ported from tests/orders.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class OrdersTest
{

    public static void run(SimState state, double ticks)
    {
        for (var t = 0; t < ticks; t++)
        {
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
    }

    public static void cmd(SimState state, Command c)
    {
        { expect(Commands.applyCommand(state, c), Js.Json_stringify(c)).toMatchObject(Js.Obj(("ok", (object?)(true)))); return; }
    }

    public static double gap(Point a, Point b)
    {
        return JsMath.max(JsMath.abs(a.x - b.x), JsMath.abs(a.y - b.y));
    }

    /// <summary>group orders</summary>
    public class D001_group_orders
    {

        [Fact(DisplayName = "group orders > followers keep up with you, a few tiles behind, and leave colony work alone")]
        public void T002_followers_keep_up_with_you_a_few_tiles_behind_and_leave_colo()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["ss........................", "w.+......................."] });
            var (you, them) = state.survivors;
            cmd(state, new Command { type = "takeControl", survivorId = you.id });
            cmd(state, new Command { type = "shout", order = "follow" });
            expect(them.order).toEqual(Js.Obj(("kind", (object?)("follow"))));
            cmd(state, new Command { type = "steer", dx = 1, dy = 0 });
            run(state, 80);
            cmd(state, new Command { type = "steer", dx = 0, dy = 0 });
            run(state, 40);
            expect(you.pos.x).toBeGreaterThan(15);
            expect(gap(you.pos, them.pos)).toBeLessThanOrEqual(Orders.FOLLOW_GAP);
            expect(state.blueprints).toHaveLength(1);
        }

        [Fact(DisplayName = "group orders > hold keeps them at a spot; settle sends them back to work")]
        public void T003_hold_keeps_them_at_a_spot_settle_sends_them_back_to_work()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.........", "w.+......."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "setOrder", survivorId = s.id, order = "hold", x = Js.I(8), y = Js.I(0) });
            run(state, 60);
            expect(s.pos).toEqual(Js.Obj(("x", (object?)(8)), ("y", (object?)(0))));
            expect(state.blueprints).toHaveLength(1);
            cmd(state, new Command { type = "setOrder", survivorId = s.id, order = "settle" });
            run(state, 200);
            expect(state.blueprints).toHaveLength(0);
        }

        [Fact(DisplayName = "group orders > under an order they still fight a zombie that comes close")]
        public void T004_under_an_order_they_still_fight_a_zombie_that_comes_close()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s..."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            cmd(state, new Command { type = "setOrder", survivorId = s.id, order = "hold" });
            Commands.applyCommand(state, new Command { type = "spawnZombie", x = Js.I(3), y = Js.I(0) });
            Js.NN(Js.Get(state.zombies, 0)).kind = "static";
            run(state, 150);
            expect(state.zombies).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "group orders > loot: they search every crate in the room, then come back to following")]
        public void T005_loot_they_search_every_crate_in_the_room_then_come_back_to_f()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["ss.......", "..#####..", "..#c.c#..", "..#...#..", "..##D##..", "........."] });
            var (you, them) = state.survivors;
            cmd(state, new Command { type = "takeControl", survivorId = you.id });
            cmd(state, new Command { type = "shout", order = "loot", x = Js.I(4), y = Js.I(3) });
            expect(them.order?.kind).toBe("loot");
            object loot()
            {
                return state.furniture.filter((f) => f.kind == "crate").reduce((n, f) => n + f.loot, 0.0);
            }
            var before = loot();
            var items = state.items.length;
            run(state, 3000);
            expect(loot()).toBe(0);
            expect(state.items.length).toBeGreaterThan(items + ((double)(before) / (2)));
            expect(them.order).toEqual(Js.Obj(("kind", (object?)("follow"))));
        }

        [Fact(DisplayName = "group orders > a follower falling behind hurries")]
        public void T006_a_follower_falling_behind_hurries()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.........s"] });
            var (you, them) = state.survivors;
            cmd(state, new Command { type = "takeControl", survivorId = you.id });
            expect(Survivors.stepPace(state, them, new Point { x = Js.I(9), y = Js.I(0) })).toBe(4);
            cmd(state, new Command { type = "setOrder", survivorId = them.id, order = "follow" });
            expect(Orders.hurries(state, them)).toBe(true);
            expect(Survivors.stepPace(state, them, new Point { x = Js.I(9), y = Js.I(0) })).toBe(3);
        }

        [Fact(DisplayName = "group orders > a follower standing in front of you steps aside, and you hold fire if they are in the way")]
        public void T007_a_follower_standing_in_front_of_you_steps_aside_and_you_hold()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", "s.s.z", "....."] });
            var (you, them) = state.survivors;
            Js.NN(Js.Get(state.zombies, 0)).kind = "static";
            cmd(state, new Command { type = "takeControl", survivorId = you.id });
            var gun = Commands.applyCommand(state, new Command { type = "spawnWeapon", x = Js.I(0), y = Js.I(0) });
            expect(gun.ok).toBe(true);
            var item = Js.NN(state.items.find((i) => i.kind == "weapon"));
            item.kind = "gun";
            item.wear = 6;
            item.pos = null;
            item.carriedBy = you.id;
            you.weapon = item.id;
            Js.NN(state.control).facing = new Point { x = Js.I(1), y = Js.I(0) };
            var r = Commands.applyCommand(state, new Command { type = "swing", x = Js.I(4), y = Js.I(1) });
            expect(r.ok).toBe(true);
            expect(you.note).toMatch(new JsRegExp("in the way", ""));
            expect(Js.NN(Js.Get(state.zombies, 0)).health).toBe(Js.NN(Js.Get(state.zombies, 0)).maxHealth);
            cmd(state, new Command { type = "setOrder", survivorId = them.id, order = "follow" });
            run(state, 10);
            expect(them.pos.y).not.toBe(1);
        }

        [Fact(DisplayName = "group orders > people taken in while you are out come with you")]
        public void T008_people_taken_in_while_you_are_out_come_with_you()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s..v"] });
            cmd(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            cmd(state, new Command { type = "acceptWanderer", wandererId = Js.NN(Js.Get(state.wanderers, 0)).id });
            expect(Js.NN(Js.Get(state.survivors, 1)).order).toEqual(Js.Obj(("kind", (object?)("follow"))));
        }
    }
}
