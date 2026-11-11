// Ported from src/sim/forage.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Foraging for colonists: going hungry with no food to go to, they look for some themselves.
//
// A colonist with no job, by day, when the colony has less than a meal each lying about (free food,
// reachable or not; generated worlds only) or they're hungry with none to reach, walks to the nearest open
// grass or old field (outdoors, not picked over today: Handiwork.cs canForage) within
// FORAGE_SEARCH tiles and forages there for FORAGE_TICKS (on slowTick ticks, like work). What
// turns up is dropped at their feet, and they eat it as they would any food (Needs.cs). One who
// is hungry (below FORAGE_HUNGER) drops their job to do it (Needs.cs interruptForNeeds).
// It's subsistence, not stockpiling: they stop once there's a meal each, and a tile gives once a day.
// Not at night, and not in winter. When there's nowhere to go they look again after
// FORAGE_RETRY_TICKS (survivor.nextForageTick).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Forage
{

    /// <summary>How far (tiles searched) a colonist looks for somewhere to forage.</summary>
    public const int FORAGE_SEARCH = 1500;

    public const int FORAGE_RETRY_TICKS = 200;

    /// <summary>Below this (hungry), a colonist with no food to go to drops their job to forage by day.</summary>
    public static readonly double FORAGE_HUNGER = Food.HUNGRY;

    /// <summary>Is it a time anyone can forage at all (by day, not in winter)?</summary>
    public static bool forageTime(SimState state)
    {
        return !Time.isNight(state.tick) && (Seasons.seasonOf(state.tick) != "winter");
    }

    /// <summary>Where this colonist can go to forage: the nearest open grass or field not picked over today, or null.</summary>
    public static Point? spot(SimState state, Survivor s)
    {
        var __d0 = state;
        var grid = __d0.grid;
        var today = Time.dayOf(state.tick);
        bool open(double x, double y)
        {
            var g = Js.Get(Grids.GROUNDS, Js.NN(Grids.groundAt(grid, Grids.tileIndex(grid, x, y))));
            return ((((g == "grass") || (g == "field")) && Grids.isWalkable(grid, x, y)) && (Js.Get(state.foraged, Js.Str(Grids.tileIndex(grid, x, y))) != today)) && !Rooms.isIndoors(state, x, y);
        }
        var p = Space.nearestTile(grid, s.pos, (__f0, __f1) => open(__f0, __f1), FORAGE_SEARCH);
        return (Js.T(p) && Pathfinding.canReach(grid, state.walkabilityVersion, s.pos, [p])) ? p : null;
    }

    /// <summary>Runs with the other needs, while standing still. Returns true if foraging took this tick. `hasFood`: is there food they could go and eat (then they don't forage)?</summary>
    public static bool updateForage(SimState state, Survivor s, Func<bool> hasFood)
    {
        if (Js.T(s.sleep))
        {
            return false;
        }
        var f = s.forage;
        if (Js.T(f))
        {
            if (!forageTime(state))
            {
                s.forage = null;
                return false;
            }
            if (!Space.samePoint(s.pos, f))
            {
                var path = Pathfinding.findPathToAny(state.grid, s.pos, [f]);
                if (!Js.T(path) || (path.length == 0))
                {
                    s.forage = null;
                    return false;
                }
                Survivors.walk(s, path, state.walkabilityVersion);
                s.note = $"going to forage at ({Js.Str(f.x)}, {Js.Str(f.y)})";
                return true;
            }
            if (Handiwork.canForage(state, s) != null)
            {
                s.forage = null;
                return false;
            }
            if (Time.slowTick(state.tick))
            {
                f.progress += 1;
            }
            s.note = "foraging";
            if (f.progress < Handiwork.FORAGE_TICKS)
            {
                return true;
            }
            s.note = Handiwork.finishForage(state, s);
            s.forage = null;
            return true;
        }
        if (Js.T((Js.T(Js.T(s.jobId) || !forageTime(state))) || (state.tick < (Js.Q(s.nextForageTick, 0)))))
        {
            return false;
        }
        var @short = state.harsh && (state.items.filter((i) => (((i.kind == "food") || (i.kind == "meal")) && (i.pos != null)) && (i.reservedBy == null)).length < state.survivors.length);
        if (Js.T(!Js.T(@short) && (Js.T((s.satiety >= Food.HUNGRY) || Js.T(hasFood())))))
        {
            return false;
        }
        var p = spot(state, s);
        if (!Js.T(p))
        {
            s.nextForageTick = state.tick + FORAGE_RETRY_TICKS;
            return false;
        }
        s.forage = new SurvivorForage { x = Js.I(p.x), y = Js.I(p.y), progress = 0 };
        s.note = $"going to forage at ({Js.Str(p.x)}, {Js.Str(p.y)})";
        return true;
    }
}
