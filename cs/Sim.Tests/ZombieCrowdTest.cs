// ZombieCrowd.cs answers "how many of the dead are near here" from buckets made once a tick, kept per list and made
// again when the list is added to, taken from or replaced. Each of those is a way to answer from stale buckets, and
// nothing compared the fast answer with the plain count.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class ZombieCrowdTest
{
    const int SIZE = 24;

    static SimState yard()
    {
        var rows = new List<string>();
        for (int y = 0; y < SIZE; y++) rows.Add(y == 0 ? "s" + new string('.', SIZE - 1) : new string('.', SIZE));
        return ScenarioMod.loadScenario(new Scenario { seed = 1, map = rows });
    }

    /// <summary>The plain count: everyone whose tile is within r of p, each way.</summary>
    static int plain(SimState state, Point p, double r) =>
        state.zombies.Count((z) => Math.Max(Math.Abs(z.pos.x - p.x), Math.Abs(z.pos.y - p.y)) <= r);

    static void same(SimState state, string when)
    {
        for (int k = 0; k < 40; k++)
        {
            var p = new Point((int)(Hash.hashRoll(k, 1) * SIZE), (int)(Hash.hashRoll(k, 2) * SIZE));
            foreach (double r in new double[] { 0, 1, 3, 8 })
                expect(Zombies.zombiesWithin(state, p, r), $"{when}: within {r} of ({p.x}, {p.y})").toBe(plain(state, p, r));
        }
    }

    [Fact(DisplayName = "zombiesWithin > counts as a plain count does, through adding, removing, moving and a new list")]
    public void AsThePlainCount()
    {
        var state = yard();
        same(state, "nobody");
        for (int k = 0; k < 70; k++)
            Zombies.addZombie(state, 1 + (int)(Hash.hashRoll(k, 3) * (SIZE - 1)), 1 + (int)(Hash.hashRoll(k, 4) * (SIZE - 1)));
        same(state, "seventy");
        // Taken from in place: the buckets are made again.
        for (int k = 0; k < 10; k++) Zombies.removeZombie(state, state.zombies[(int)(Hash.hashRoll(k, 5) * state.zombies.Count)]);
        same(state, "ten removed");
        // Added to after a count, in the same tick.
        Zombies.addZombie(state, 5, 5);
        Zombies.addZombie(state, 5, 5);
        same(state, "two added");
        // Moved a tile since the buckets were made (the same tick): each is counted where it stands now.
        foreach (var z in state.zombies.Take(20)) z.pos = new Point(Math.Min(SIZE - 1, z.pos.x + 1), z.pos.y);
        same(state, "twenty moved");
        // A new list (as at dawn, and when one arrives from a herd).
        state.zombies = state.zombies.Where((z, i) => i % 2 == 0).ToList();
        same(state, "a new list");
        // A later tick.
        state.tick += 1;
        state.zombies[0].pos = new Point(SIZE - 1, SIZE - 1);
        same(state, "the next tick");
    }
}
