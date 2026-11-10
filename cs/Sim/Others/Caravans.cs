// Ported from src/sim/caravans.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Caravans: what settlements send each other (and you) travels the map.
//
// A caravan is a marker, like a herd (herds.ts): a few people and a cart, a tile every
// CARAVAN_STEP ticks in a straight line from where it set out to where it's going. Food a
// fed neighbour sends a starving one (colonies.ts), allies' trade runs, and the thanks for an
// errand or a radio request (errands.ts, radio.ts) all go by caravan now, not in an instant.
//   the road   every hour on the road there's a chance (AMBUSH_CHANCE, more as the days go on
//              and with more of the dead about) that the dead catch it: one of its people is
//              lost, and with them a share of the load. Lose them all and the caravan is gone,
//              and what's left of the load lies in the road (items, if that ground exists yet).
//   arriving   at a settlement, the food goes into its stores (and the two warm to each other);
//              at your place, the goods are set down where it stops.
// Shown on the map and the minimap where explored; drawn in the world where you can see.
// Rolls are hashes of its id and the tick, not the sim RNG.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Caravans
{

    public const int CARAVAN_STEP = 6;

    public const int CARAVAN_PEOPLE = 3;

    /// <summary>Chance an hour on the road that the dead catch it (grows with the days and the zombie setting).</summary>
    public const double AMBUSH_CHANCE = 0.03;

    public static List<Caravan> caravansOf(SimState state)
    {
        return state.caravans ?? [];
    }

    /// <summary>Sends a caravan from a settlement to another settlement, or to you ('you': to `dest`).</summary>
    public static Caravan? sendCaravan(SimState state, string fromId, string to, Point dest, double food, List<string>? goods = null)
    {
        goods ??= new();
        var from = Settlements.getSettlement(state, fromId);
        if (!Js.T(from))
        {
            return null;
        }
        state.caravans ??= [];
        Caravan c = new Caravan { id = $"c{Js.Str(state.nextId++)}", from = fromId, to = to, x = Js.I(from.x), y = Js.I(from.y), dest = Js.Clone(dest), people = CARAVAN_PEOPLE, food = food, goods = (goods).ToList(), nextStepTick = state.tick + CARAVAN_STEP };
        state.caravans.push(c);
        return c;
    }

    /// <summary>Every tick: caravans move on, run into trouble, and arrive.</summary>
    public static void updateCaravans(SimState state)
    {
        var list = caravansOf(state);
        if (list.length == 0)
        {
            return;
        }
        foreach (var c in Js.Iter((list).ToList()))
        {
            if (((state.tick % Time.TICKS_PER_HOUR) == 0) && ambushed(state, c))
            {
                continue;
            }
            if (state.tick < c.nextStepTick)
            {
                continue;
            }
            c.nextStepTick = state.tick + CARAVAN_STEP;
            c.x = Js.I(c.x + JsMath.sign(c.dest.x - c.x));
            c.y = Js.I(c.y + JsMath.sign(c.dest.y - c.y));
            if ((c.x == c.dest.x) && (c.y == c.dest.y))
            {
                arrive(state, c);
            }
        }
    }

    /// <summary>The dead catch up with it: a person and a share of the load lost; all of them, and it's gone.</summary>
    public static bool ambushed(SimState state, Caravan c)
    {
        var chance = ((double)(((double)(((double)(AMBUSH_CHANCE) * ((1 + ((double)(Time.dayOf(state.tick)) / (20)))))) * (state.settings.zombies))) / (100));
        if (Hash.hashRoll(Hash.idNumber(c.id), state.tick, 0xca2) >= chance)
        {
            return false;
        }
        var share = ((double)(1) / (c.people));
        c.people -= 1;
        c.food = JsMath.floor(((double)(c.food) * ((1 - share))));
        c.goods = c.goods.slice(0, JsMath.floor(((double)(c.goods.length) * ((1 - share)))));
        var from = Settlements.getSettlement(state, c.from);
        if (Js.T(from))
        {
            from.people = JsMath.max(0, from.people - 1);
        }
        var heard = (c.to == "you") || (Js.T(from) && !Settlements.isHostile(from));
        if (c.people > 0)
        {
            if (Js.T(heard))
            {
                Events.logEvent(state, $"The dead caught {Js.Str(from?.name ?? "a")} caravan near ({Js.Str(c.x)}, {Js.Str(c.y)}): one of them lost");
            }
            return false;
        }
        if (Js.T(heard))
        {
            Events.logEvent(state, $"{Js.Str(from?.name ?? "A")} caravan never made it: the dead got them all near ({Js.Str(c.x)}, {Js.Str(c.y)})");
        }
        var spot = Grids.inBounds(state.grid, c.x, c.y) ? Space.nearestTile(state.grid, c, (x, y) => Grids.isWalkable(state.grid, x, y), 50) : null;
        if (Js.T(spot))
        {
            foreach (var kind in Js.Iter(c.goods))
            {
                Items.spawnItem(state, kind, spot.x, spot.y);
            }
            for (var k = 0; k < JsMath.min(5, c.food); k++)
            {
                Items.spawnItem(state, "food", spot.x, spot.y);
            }
        }
        Js.NN(state.caravans).splice(Js.NN(state.caravans).indexOf(c), 1);
        return true;
    }

    public static void arrive(SimState state, Caravan c)
    {
        Js.NN(state.caravans).splice(Js.NN(state.caravans).indexOf(c), 1);
        var from = Settlements.getSettlement(state, c.from);
        if (c.to == "you")
        {
            var spot = Space.nearestTile(state.grid, c, (x, y) => Grids.isWalkable(state.grid, x, y), 200) ?? c;
            foreach (var kind in Js.Iter(c.goods))
            {
                Items.spawnItem(state, kind, spot.x, spot.y);
            }
            for (var k = 0; k < c.food; k++)
            {
                Items.spawnItem(state, "food", spot.x, spot.y);
            }
            var what = (Js.ListOf((c.food > 0) ? ((List<string>)[$"{Js.Str(c.food)} food"]) : ((List<string>)[]), c.goods).join(", ") is var __or0 && Js.T(__or0) ? __or0 : "nothing left");
            Events.logEvent(state, $"A caravan from {Js.Str(from?.name ?? "the neighbours")} reached you: {Js.Str(what)}");
            return;
        }
        var to = Settlements.getSettlement(state, c.to);
        if (!Js.T(to))
        {
            return;
        }
        to.food += c.food;
        if (Js.T(from))
        {
            Settlements.tieSettlements(from, to, 10);
        }
        if (!Settlements.isHostile(to))
        {
            Events.logEvent(state, $"A caravan from {Js.Str(from?.name ?? "somewhere")} reached {Js.Str(to.name)} with {Js.Str(c.food)} food");
        }
    }
}
