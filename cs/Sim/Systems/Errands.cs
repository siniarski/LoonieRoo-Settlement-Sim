// Ported from src/sim/errands.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Errands: neighbours asking you for help, out in the world.
//
// Talk to one of a settlement's people in person (residents.ts meetResident) when it thinks
// well enough of you (not hostile) and has nothing open with you, and some days they ask:
//   nest     the dead have gathered near X (NEST_SIZE of them, asleep on their feet: static):
//            kill them all.
//   missing  one of theirs went out and hasn't come back: find them, near X (anyone of yours
//            coming within FOUND_REACH tiles of the spot finds them, and they go home).
// The spot is ERRAND_MIN..ERRAND_MAX tiles from the settlement. On the map it's marked, with
// what it is. Done within ERRAND_DAYS: they think a lot more of you (ERRAND_REP), and a
// caravan (caravans.ts) brings the thanks to your place. Not done in time: they think a little
// less of you (LAPSED_REP). One errand open per settlement at a time.
// Rolls are hashes of the settlement, the day and the person, not the sim RNG.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Errands
{

    public const double ASK_CHANCE = 0.5;

    public const int ERRAND_DAYS = 4;

    public const int ERRAND_MIN = 18;

    public const int ERRAND_MAX = 36;

    public const int NEST_SIZE = 4;

    public const int FOUND_REACH = 2;

    public const int ERRAND_REP = 25;

    public const double LAPSED_REP = -5;

    public static readonly List<string> THANKS = ["medicine", "ammo", "coat", "bandage", "fuel", "book"];

    public static readonly List<string> MISSING = ["Rafe", "Dot", "Kit", "Jonah", "Mae", "Tobias", "Wren", "Ines"];

    public static List<Errand> errandsOf(SimState state)
    {
        return state.errands ?? [];
    }

    /// <summary>When you talk with one of their people: maybe they ask you for something. Returns what they said, or null.</summary>
    public static string? maybeAsk(SimState state, Settlement n, double salt)
    {
        if (Settlements.isHostile(n) || errandsOf(state).some((e_1) => e_1.settlementId == n.id))
        {
            return null;
        }
        double roll(double k)
        {
            return Hash.hashRoll(Hash.idNumber(n.id), Time.dayOf(state.tick), salt, k);
        }
        if (roll(0) >= ASK_CHANCE)
        {
            return null;
        }
        var angle = ((double)(((double)(roll(1)) * (JsMath.PI))) * (2));
        var dist = ERRAND_MIN + JsMath.floor(((double)(roll(2)) * ((ERRAND_MAX - ERRAND_MIN))));
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        Point want = new Point { x = Js.I(JsMath.max(2, JsMath.min(width - 3, JsMath.round(n.x + ((double)(JsMath.cos(angle)) * (dist)))))), y = Js.I(JsMath.max(2, JsMath.min(height - 3, JsMath.round(n.y + ((double)(JsMath.sin(angle)) * (dist)))))) };
        Worldgen.ensureGeneratedAround(state, want);
        var spot = Space.nearestTile(state.grid, want, (x, y) => Grids.getTile(state.grid, x, y) == "floor", 400);
        if (!Js.T(spot))
        {
            return null;
        }
        state.errands ??= [];
        var kind = (roll(3) < 0.5) ? "nest" : "missing";
        Errand e = new Errand { id = $"e{Js.Str(state.nextId++)}", settlementId = n.id, kind = kind, x = Js.I(spot.x), y = Js.I(spot.y), zombieIds = [], who = Js.NN(Js.Get(MISSING, JsMath.floor(((double)(roll(4)) * (MISSING.length))))), until = state.tick + ((double)(ERRAND_DAYS) * (Time.DAY_LENGTH)) };
        if (kind == "nest")
        {
            for (var k = 0; k < NEST_SIZE; k++)
            {
                var p = Space.nearestTile(state.grid, new Point { x = Js.I(spot.x + (k % 2)), y = Js.I(spot.y + JsMath.floor(((double)(k) / (2)))) }, (x, y) => (Grids.getTile(state.grid, x, y) == "floor") && !state.zombies.some((z) => (z.pos.x == x) && (z.pos.y == y)), 100);
                if (Js.T(p))
                {
                    e.zombieIds.push(Zombies.addZombie(state, p.x, p.y, kind: "static").id);
                }
            }
        }
        state.errands.push(e);
        var said = (kind == "nest") ? $"the dead have gathered near ({Js.Str(spot.x)}, {Js.Str(spot.y)}): would you clear them out?" : $"{Js.Str(e.who)} went out and never came back. Last seen near ({Js.Str(spot.x)}, {Js.Str(spot.y)})";
        Events.logEvent(state, $"{Js.Str(n.name)} asks: {Js.Str(said)} (within {Js.Str(ERRAND_DAYS)} days)");
        return said;
    }

    /// <summary>Every tick: errands done, or run out.</summary>
    public static void updateErrands(SimState state)
    {
        var list = errandsOf(state);
        if (list.length == 0)
        {
            return;
        }
        foreach (var e in Js.Iter((list).ToList()))
        {
            var n = Settlements.getSettlement(state, e.settlementId);
            if (!Js.T(n) || (n.people <= 0))
            {
                list.splice(list.indexOf(e), 1);
                continue;
            }
            if (done(state, e))
            {
                list.splice(list.indexOf(e), 1);
                reward(state, e, n);
            }
            else if (state.tick >= e.until)
            {
                list.splice(list.indexOf(e), 1);
                Settlements.adjustReputation(state, n, LAPSED_REP);
                Events.logEvent(state, $"{Js.Str(n.name)} gave up waiting: {Js.Str((e.kind == "nest") ? Js.Str("the dead near them are still there") : Js.Str($"nobody found {Js.Str(e.who)}"))}");
            }
        }
    }

    public static bool done(SimState state, Errand e)
    {
        if (e.kind == "nest")
        {
            return (e.zombieIds.length > 0) && !state.zombies.some((z) => e.zombieIds.includes(z.id));
        }
        return state.survivors.some((s) => JsMath.max(JsMath.abs(s.pos.x - e.x), JsMath.abs(s.pos.y - e.y)) <= FOUND_REACH);
    }

    public static void reward(SimState state, Errand e, Settlement n)
    {
        Settlements.adjustReputation(state, n, ERRAND_REP);
        if (e.kind == "missing")
        {
            n.people += 1;
        }
        double roll(double k)
        {
            return Hash.hashRoll(Hash.idNumber(e.id), k);
        }
        List<string> goods = [Js.NN(Js.Get(THANKS, JsMath.floor(((double)(roll(1)) * (THANKS.length))))), Js.NN(Js.Get(THANKS, JsMath.floor(((double)(roll(2)) * (THANKS.length)))))];
        Point home = Js.Get(state.survivors, 0)?.pos ?? new Point { x = Js.I(n.x), y = Js.I(n.y) };
        Caravans.sendCaravan(state, n.id, "you", home, 3, goods);
        Events.logEvent(state, (e.kind == "nest") ? $"The dead near {Js.Str(n.name)} are gone. They're sending thanks by caravan" : $"You found {Js.Str(e.who)}, and sent them home to {Js.Str(n.name)}. They're sending thanks by caravan");
    }
}
