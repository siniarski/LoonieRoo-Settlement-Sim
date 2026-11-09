// Ported from src/sim/fire.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Fire.
//
// A fire sits on a tile and burns for a while: FIRE_BURN ticks on anything
// that burns (walls, doors, barricades and all furniture are wood), less on
// bare ground. Rain and snow burn it down twice as fast. While it burns it
// hurts anyone standing in it, survivor or zombie, and every SPREAD_EVERY
// ticks may catch on each burnable neighbour (a hash roll, not the sim RNG).
// When it burns out, what it was on is gone: a wall becomes floor, furniture
// and items are ash.
//
// Fires start from a stove now and then, from raiders' firebombs, or on
// purpose (the Burn order). Survivors with firefighting on put them out
// (jobs.ts).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class FireMod
{

    public const int FIRE_BURN = 300;

    public const int GROUND_BURN = 100;

    public const int SPREAD_EVERY = 20;

    public const double SPREAD_CHANCE = 0.2;

    /// <summary>Health lost per tick standing in a fire.</summary>
    public const int FIRE_DAMAGE = 5;

    /// <summary>Ticks of work to put a fire out.</summary>
    public const int DOUSE_WORK = 30;

    /// <summary>Chance a hot meal sets the stove's tile alight.</summary>
    public const double STOVE_FIRE_CHANCE = 0.02;

    /// <summary>Chance a raider's blow on a wall or door is a firebomb instead.</summary>
    public const double FIREBOMB_CHANCE = 0.05;

    /// <summary>Safety cap (not a balance lever).</summary>
    public const int MAX_FIRES = 150;

    public static Fire? fireAt(SimState state, int x, int y)
    {
        return state.fires.find((f) => (f.x == x) && (f.y == y));
    }
    public static Fire? fireAt(SimState state, double x, double y) => fireAt(state, Js.I(x), Js.I(y));

    /// <summary>Can a fire here keep going (and catch from a neighbour)?</summary>
    public static bool burnable(SimState state, int x, int y)
    {
        var t = Grids.getTile(state.grid, x, y);
        if (Defences.isReinforced(state, x, y))
        {
            return false;
        }
        return (((t == "wall") || (t == "door")) || (t == "barricade")) || ((t == "floor") && (FurnitureMod.furnitureAt(state, x, y) != null));
    }
    public static bool burnable(SimState state, double x, double y) => burnable(state, Js.I(x), Js.I(y));

    /// <summary>Sets a tile alight. Returns false if it can't burn (water, already alight, too many fires).</summary>
    public static bool ignite(SimState state, int x, int y)
    {
        if (Js.T((Js.T((!Grids.inBounds(state.grid, x, y) || (Grids.getTile(state.grid, x, y) == "water")) || Js.T(fireAt(state, x, y)))) || (state.fires.length >= MAX_FIRES)))
        {
            return false;
        }
        state.fires.push(new() { x = Js.I(x), y = Js.I(y), left = burnable(state, x, y) ? FIRE_BURN : GROUND_BURN });
        return true;
    }
    public static bool ignite(SimState state, double x, double y) => ignite(state, Js.I(x), Js.I(y));

    public static void douse(SimState state, Fire f)
    {
        var i = state.fires.indexOf(f);
        if (i >= 0)
        {
            state.fires.splice(i, 1);
        }
    }

    public static void updateFires(SimState state)
    {
        if (state.fires.length == 0)
        {
            return;
        }
        var wet = Seasons.isWet(state.weather);
        foreach (var f in Js.Iter((state.fires).ToList()))
        {
            foreach (var s in Js.Iter(state.survivors))
            {
                if ((s.pos.x != f.x) || (s.pos.y != f.y))
                {
                    continue;
                }
                s.health = JsMath.max(0, s.health - FIRE_DAMAGE);
                s.lastHurtBy = "burns";
                s.hurtTick = state.tick;
            }
            foreach (var z in Js.Iter((state.zombies).ToList()))
            {
                if ((z.pos.x != f.x) || (z.pos.y != f.y))
                {
                    continue;
                }
                z.health = JsMath.max(0, z.health - FIRE_DAMAGE);
                if (z.health > 0)
                {
                    continue;
                }
                Zombies.removeZombie(state, z);
                Corpses.leaveCorpse(state, z.pos, null);
            }
            if (((state.tick % SPREAD_EVERY) == 0) && burnable(state, f.x, f.y))
            {
                foreach (var n in Js.Iter(Grids.neighbours4(state.grid, f.x, f.y)))
                {
                    var chance = (state.weather == "heatwave") ? ((double)(2) * (SPREAD_CHANCE)) : SPREAD_CHANCE;
                    if ((burnable(state, n.x, n.y) && !Js.T(fireAt(state, n.x, n.y))) && (Hash.hashRoll(n.x, n.y, state.tick) < chance))
                    {
                        ignite(state, n.x, n.y);
                    }
                }
            }
            f.left -= wet ? 2 : 1;
            if (f.left <= 0)
            {
                burnOut(state, f);
            }
        }
    }

    public static void burnOut(SimState state, Fire f)
    {
        douse(state, f);
        var furniture = FurnitureMod.furnitureAt(state, f.x, f.y);
        var what = furniture?.kind ?? Grids.getTile(state.grid, f.x, f.y);
        if (Js.T(furniture))
        {
            furniture.stored = 0;
            FurnitureMod.removeFurniture(state, furniture);
        }
        var t = Grids.getTile(state.grid, f.x, f.y);
        if (((t == "wall") || (t == "door")) || (t == "barricade"))
        {
            Grids.setTile(state.grid, f.x, f.y, "floor");
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        }
        var item = Items.itemAt(state, f.x, f.y);
        if (Js.T(item))
        {
            Items.removeItem(state, item);
        }
        if (what != "floor")
        {
            Events.logEvent(state, $"A {Js.Str(what)} at ({Js.Str(f.x)}, {Js.Str(f.y)}) burned down");
        }
    }
}
