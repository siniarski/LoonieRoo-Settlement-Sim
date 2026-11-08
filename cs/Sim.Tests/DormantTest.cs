// The dormant dead's index (Dormant.cs): waking through it does what walking the whole list did.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class DormantTest
{
    /// <summary>A field with two survivors and 400 dormant scattered in no spatial order.</summary>
    static SimState scattered()
    {
        var map = Js.ArrayFrom(60, (int y) => y == 20 ? "s" + ".".repeat(39) + "s" + ".".repeat(79) : ".".repeat(120));
        var state = ScenarioMod.loadScenario(new Scenario { seed = 5, map = map });
        state.dormant = [];
        for (var i = 0; i < 400; i++)
            state.dormant.Add(new DormantZombie { id = $"d{i}", x = (int)JsMath.floor(Hash.hashRoll(i, 1) * 120), y = (int)JsMath.floor(Hash.hashRoll(i, 2) * 60) });
        return state;
    }

    static List<string> brute(SimState state, Func<Point, bool> near) => state.dormant.Where((d) => near(d)).Select((d) => d.id).ToList();

    [Fact(DisplayName = "dormant index > wakes the ones near survivors in the list's order, as walking the list did")]
    public void WakesInListOrder()
    {
        var state = scattered();
        var near = (Point d) => state.survivors.Any((s) => Space.manhattan(s.pos, d) <= Herds.WAKE_RADIUS);
        var want = brute(state, near);
        var positions = want.Select((id) => state.dormant.Find((d) => d.id == id)).Select((d) => $"{d.x},{d.y}").ToList();
        var left = state.dormant.Where((d) => !near(d)).Select((d) => d.id).ToList();
        expect(want.Count).toBeGreaterThan(3);
        Herds.wakeDormant(state);
        // Each woken one gets up where it lay (open field), in the order the list had them.
        expect(state.zombies.Select((z) => $"{z.pos.x},{z.pos.y}").ToList()).toEqual(positions);
        expect(state.dormant.Select((d) => d.id).ToList()).toEqual(left);
    }

    [Fact(DisplayName = "dormant index > a noise wakes the same ones as a walk of the list")]
    public void NoiseWakesSame()
    {
        var state = scattered();
        var p = new Point(70, 30);
        var r = Js.Get(Noise.NOISE, "generator"); // 12: fewer than MAX_ZOMBIES
        var want = brute(state, (d) => Space.manhattan(d, p) <= r);
        expect(want.Count).toBeGreaterThan(3);
        expect(Noise.wakeDormantNear(state, p, r)).toBe(want.Count);
        expect(state.dormant.Any((d) => Space.manhattan(d, p) <= r)).toBe(false);
    }

    [Fact(DisplayName = "dormant index > follows the list when it's edited directly or replaced")]
    public void FollowsEdits()
    {
        var state = scattered();
        string ids(List<DormantZombie> l) => string.Join(" ", l.Select((d) => d.id));
        var all = (Point d) => Space.manhattan(d, new Point(60, 30)) <= 25;
        expect(ids(Dormant.near(state, 60, 30, 25))).toBe(string.Join(" ", brute(state, all)));
        state.dormant.Add(new DormantZombie { id = "late", x = 60, y = 30 });
        expect(ids(Dormant.near(state, 60, 30, 0))).toBe("late");
        Dormant.remove(state, state.dormant[^1]);
        Dormant.add(state, new DormantZombie { id = "later", x = 61, y = 30 });
        expect(ids(Dormant.near(state, 61, 30, 0))).toBe("later");
        expect(ids(Dormant.near(state, 60, 30, 25))).toBe(string.Join(" ", brute(state, all)));
        state.dormant = state.dormant.Where((d) => d.x < 60).ToList();
        expect(ids(Dormant.near(state, 60, 30, 25))).toBe(string.Join(" ", brute(state, all)));
        expect(Dormant.near(state, 61, 30, 0).Count).toBe(0);
        // Each way (not Manhattan), as the first-person view asks.
        expect(ids(Dormant.near(state, 30, 30, 8, false))).toBe(string.Join(" ", brute(state, (d) => JsMath.abs(d.x - 30) <= 8 && JsMath.abs(d.y - 30) <= 8)));
    }
}
