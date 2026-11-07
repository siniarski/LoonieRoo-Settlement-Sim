// Ported from tests/visitors.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class VisitorsTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 3000)
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

    public static double at(double day, double hour)
    {
        return ((double)((day - 1)) * (Time.DAY_LENGTH)) + ((double)((hour - 6)) * (Time.TICKS_PER_HOUR));
    }

    public static SimState setup(List<string> map)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 3, map = map });
        foreach (var s in Js.Iter(state.survivors))
        {
            s.priorities = new(JobTypes.NO_JOBS) {  };
        }
        return state;
    }

    /// <summary>traders</summary>
    public class D001_traders
    {

        [Fact(DisplayName = "traders > come on day ... at noon with three offers, trade, and leave at dusk")]
        public void T002_come_on_day_at_noon_with_three_offers_trade_and_leave_at_dus()
        {
            var state = setup(["s.ffffffff", "wwwwwwww.."]);
            state.tick = Js.I(at(Visitors.TRADER_FIRST_DAY, Visitors.TRADER_HOUR));
            Tick.tick(state);
            expect(state.traders).toHaveLength(1);
            var t = Js.NN(Js.Get(state.traders, 0));
            expect(t.offers).toHaveLength(3);
            var i = t.offers.findIndex((o_1) => (o_1.want == "food") || (o_1.want == "wood"));
            var o = Js.NN(Js.Get(t.offers, i));
            var before = state.items.filter((it) => it.kind == o.give).length;
            var want = state.items.filter((it) => it.kind == o.want).length;
            expect(Commands.applyCommand(state, new Command { type = "trade", traderId = t.id, offer = i }).ok).toBe(true);
            expect(state.items.filter((it) => it.kind == o.want).length).toBe((want - o.wantCount) + ((o.give == o.want) ? o.giveCount : 0));
            expect(state.items.filter((it) => it.kind == o.give).length).toBe((before + o.giveCount) - ((o.give == o.want) ? o.wantCount : 0));
            runUntil(state, (_) => state.traders.length == 0, Time.DAY_LENGTH);
        }

        [Fact(DisplayName = "traders > refuses a trade you can't pay for")]
        public void T003_refuses_a_trade_you_can_t_pay_for()
        {
            var state = setup(["s........."]);
            state.traders.push(new() { id = "t1", x = Js.I(5), y = Js.I(0), leavesTick = 1000000000.0, offers = [new() { give = "weapon", giveCount = 1, want = "food", wantCount = 4 }], from = null, traded = false });
            expect(Commands.applyCommand(state, new Command { type = "trade", traderId = "t1", offer = 0 }).ok).toBe(false);
        }
    }

    /// <summary>raiders and locks</summary>
    public class D004_raiders_and_locks
    {

        public static readonly List<string> HUT = ["r..........", "...#####...", "...#.s.#...", "...##D##...", "..........."];

        public static SimState night(bool locked)
        {
            var state = setup(HUT.map((row) => row.replace("r", ".")));
            state.tick = Js.I(at(1, 23) + 1);
            if (locked)
            {
                Commands.applyCommand(state, new Command { type = "toggleLock", x = Js.I(5), y = Js.I(3) });
            }
            Zombies.addZombie(state, 0, 0, Visitors.RAIDER_HEALTH, "raider");
            return state;
        }

        [Fact(DisplayName = "raiders and locks > raiders walk in through an unlocked door")]
        public void T005_raiders_walk_in_through_an_unlocked_door()
        {
            var state = night(false);
            runUntil(state, (_) => Js.NN(Js.Get(state.survivors, 0)).health < 1000, 400);
            expect(Js.Missing(state.grid.tiles, ((double)(3) * (11)) + 5) ? null : (object?)(Js.Get(state.grid.tiles, ((double)(3) * (11)) + 5))).toBe("door");
            expect(Js.Missing(state.grid.damage, ((double)(3) * (11)) + 5) ? null : (object?)(Js.Get(state.grid.damage, ((double)(3) * (11)) + 5))).toBe(0);
            expect(Js.NN(Js.Get(state.survivors, 0)).infection).toBeNull();
        }

        [Fact(DisplayName = "raiders and locks > a locked door must be broken down")]
        public void T006_a_locked_door_must_be_broken_down()
        {
            var state = night(true);
            runUntil(state, (_) => Js.NN(Js.Get(state.grid.damage, ((double)(3) * (11)) + 5)) > 0, 400);
            expect(Js.NN(Js.Get(state.survivors, 0)).health).toBe(1000);
        }

        [Fact(DisplayName = "raiders and locks > raiders leave at dawn; only doors can be locked")]
        public void T007_raiders_leave_at_dawn_only_doors_can_be_locked()
        {
            var state = night(false);
            runUntil(state, (_) => state.zombies.length == 0, Time.DAY_LENGTH);
            expect(Commands.applyCommand(state, new Command { type = "toggleLock", x = Js.I(0), y = Js.I(0) }).ok).toBe(false);
        }
    }
}
