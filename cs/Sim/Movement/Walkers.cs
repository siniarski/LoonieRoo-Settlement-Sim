// Walkers: animals (Animals.cs) and dogs (Dogs.cs) as free movers, like you and the dead (Motion.cs,
// rule 10). They own `at` (SUB units) and `pos` is the tile under it; nothing steps them tile by tile.
//   walkTo  heads straight at a goal point (sliding along walls) while the straight way is open
//           (Motion.walkLine), and by an A* path only where it isn't: the path is followed smoothed
//           (straight at the furthest of the next LOOKAHEAD waypoints it can walk to), as the dead's.
//   legs    a walk to a point a few tiles off at any angle (wanderLeg: grazing, roaming) or away from a
//           threat (fleeLeg: the straight line away first, then up to 90 degrees either side), picked
//           only where the straight way is open, so they never need A*.
// Budget (rule 14): A* plans are bounded (PLAN_EXPAND tiles) and capped per tick (a WalkBudget each
// for animals and dogs), and a walker that planned waits REPLAN ticks before planning again.
// Parked cars (AnimalCars.cs): plans may cross one at a cost by kind, the crossing starts where the
// path's next waypoint is under it, and a flee leg may be picked across one (a rabbit's cover, a
// deer cornered).
// Integer maths (in doubles) throughout; directions from a fixed table, rolls from Hash (no RNG).
#nullable disable warnings
namespace LoonieRoo.Sim;

/// <summary>A free mover that walks to goals: an animal or a dog.</summary>
public interface IWalker : Body, IHasId
{
    List<Point> path { get; set; }
    /// <summary>No A* plan before this tick.</summary>
    double nextPlan { get; set; }
    /// <summary>Going under or over a parked car (AnimalCars.cs).</summary>
    CarCrossing? cross { get; set; }
    /// <summary>No plans over cars before this tick (a crossing was refused).</summary>
    double? noCrossUntil { get; set; }
}

public partial class Animal : IWalker { }

public partial class Dog : IWalker
{
    double IWalker.nextPlan { get => nextThinkTick; set => nextThinkTick = value; }
}

/// <summary>How many A* plans are left this tick.</summary>
public sealed class WalkBudget
{
    public int plans;
}

public static class Walkers
{
    /// <summary>A waypoint (or a goal) this near, SUB units, counts as reached.</summary>
    public const int ARRIVED = 32;

    /// <summary>Following a path, head for the furthest of this many waypoints ahead it can walk straight to.</summary>
    public const int LOOKAHEAD = 8;

    /// <summary>An A* plan gives up after expanding this many tiles (animals and dogs only look nearby).</summary>
    public const int PLAN_EXPAND = 1500;

    /// <summary>Ticks between A* plans for one walker.</summary>
    public const int REPLAN = 15;

    /// <summary>Twelve directions, 30 degrees apart, as unit vectors times 1024 (east first, then clockwise on the grid: y is down).</summary>
    static readonly int[][] DIRS =
    [
        [1024, 0], [887, 512], [512, 887], [0, 1024], [-512, 887], [-887, 512],
        [-1024, 0], [-887, -512], [-512, -887], [0, -1024], [512, -887], [887, -512],
    ];

    /// <summary>Turns tried in order when fleeing, in 30-degree steps off straight away.</summary>
    static readonly int[] FLEE_TURNS = [0, 1, -1, 2, -2, 3, -3];

    /// <summary>The speed, SUB units a tick, of a walker taking `ticksPerTile` ticks a tile.</summary>
    public static double speedOf(double ticksPerTile)
    {
        return JsMath.floor((double)(Motion.SUB) / ticksPerTile);
    }

    /// <summary>Walk towards `goal` (SUB units) by up to `speed`, stopping `stop` short of it (centre to centre). True once it's there.</summary>
    /// `overCars`: a goal straight across a parked car (a leg picked over one) is walked to straight, over or under it.
    public static bool walkTo(SimState state, IWalker w, Point goal, double speed, string mover, WalkBudget budget, double stop = 0, bool overCars = false)
    {
        var grid = state.grid;
        var left = JsMath.sqrt(Motion.distance2(goal, w.at)) - stop;
        if (left <= ((stop > 0) ? 0 : ARRIVED))
        {
            w.path = [];
            return true;
        }
        var step = JsMath.min(speed, JsMath.floor(left));
        if (step < 1)
        {
            w.path = [];
            return true;
        }
        if (Motion.walkLine(grid, w.at, goal, mover, stop > 0))
        {
            var v = Motion.toward(goal.x - w.at.x, goal.y - w.at.y, step);
            var to = Motion.slide(grid, w.at, v.x, v.y, mover);
            // Straight on only if it gets on: the line is open but the body is wider than it, and one caught on a
            // corner (a wall's, a car's) slides sideways into it for ever. Then it's the path's way round.
            var gained = JsMath.sqrt(Motion.distance2(goal, w.at)) - JsMath.sqrt(Motion.distance2(goal, to));
            if (((to.x != w.at.x) || (to.y != w.at.y)) && ((gained * 4) >= step))
            {
                w.at = to;
                w.pos = Motion.tileOf(to);
                w.path = [];
                return (left - step) <= ((stop > 0) ? 0 : ARRIVED);
            }
        }
        else if (overCars && AnimalCars.straightOver(state, w, goal, step, mover))
        {
            return false;
        }
        followPath(state, w, Motion.tileOf(goal), speed, mover, budget);
        return false;
    }

    /// <summary>By the tile path (planned when there's none or it leads elsewhere, budget allowing): straight at the furthest waypoint ahead it can walk to.</summary>
    static void followPath(SimState state, IWalker w, Point goalTile, double speed, string mover, WalkBudget budget)
    {
        var grid = state.grid;
        var end = Js.Get(w.path, w.path.length - 1);
        if (((w.path.length == 0) || (Space.manhattan(end, goalTile) > 1)) && (state.tick >= w.nextPlan) && (budget.plans > 0))
        {
            budget.plans--;
            w.nextPlan = state.tick + REPLAN;
            List<Point> goals = [goalTile, .. Grids.neighbours4(grid, goalTile.x, goalTile.y)];
            w.path = Pathfinding.findPathToAny(grid, w.pos, goals, mover, PLAN_EXPAND, AnimalCars.planCost(state, w)) ?? [];
        }
        while ((w.path.length > 0) && Space.samePoint(Js.NN(Js.Get(w.path, 0)), w.pos) && (Motion.distance2(w.at, Motion.centre(w.pos)) <= ((double)(ARRIVED) * (ARRIVED))))
        {
            w.path.shift();
        }
        for (var j = JsMath.min(LOOKAHEAD, w.path.length) - 1; j >= 1; j--)
        {
            if (Motion.walkLine(grid, w.at, Motion.centre(Js.NN(Js.Get(w.path, j))), mover, false))
            {
                w.path = w.path.slice(j);
                break;
            }
        }
        if (w.path.length == 0)
        {
            return;
        }
        if (AnimalCars.start(state, w, mover))
        {
            return;
        }
        var aim = Motion.centre(Js.NN(Js.Get(w.path, 0)));
        var d = JsMath.sqrt(Motion.distance2(aim, w.at));
        var v = Motion.toward(aim.x - w.at.x, aim.y - w.at.y, JsMath.min(speed, JsMath.floor(d)));
        var to = Motion.slide(grid, w.at, v.x, v.y, mover);
        if ((to.x == w.at.x) && (to.y == w.at.y))
        {
            w.path = [];
            return;
        }
        w.at = to;
        w.pos = Motion.tileOf(to);
        if (Motion.distance2(to, aim) <= ((double)(ARRIVED) * (ARRIVED)))
        {
            w.path.shift();
        }
    }

    /// <summary>A point `len` SUB units off in the direction (dx, dy) (one of DIRS, or any other), if it can walk
    /// straight there and stand there. `overCars`: or across a parked car (to the first place past it, half a tile at
    /// a time, where it can stand).</summary>
    static Point? legTo(SimState state, Point at, int dx, int dy, double len, string mover, bool overCars = false)
    {
        for (var extra = 0; extra <= (overCars ? 4 : 0); extra++)
        {
            var v = Motion.toward(dx, dy, len + extra * Motion.HALF);
            var goal = new Point { x = Js.I(at.x + v.x), y = Js.I(at.y + v.y) };
            var tile = Motion.tileOf(goal);
            if (!Grids.inBounds(state.grid, tile.x, tile.y) || !Motion.fits(state.grid, goal, mover))
            {
                continue;
            }
            if (Motion.walkLine(state.grid, at, goal, mover, false))
            {
                return goal;
            }
            if (overCars && AnimalCars.lineOver(state.grid, at, goal, mover))
            {
                return goal;
            }
        }
        return null;
    }

    /// <summary>A wander point 1 to 3 tiles off at an angle from the hash (a roll of the id and tick), trying a quarter turn round if that way is blocked; or null.</summary>
    public static Point? wanderLeg(SimState state, IWalker w, string mover)
    {
        var id = Hash.idNumber(w.id);
        var k = Js.I(JsMath.floor(Hash.hashRoll(id, state.tick, 0x3a1) * 12));
        var len = Motion.SUB + JsMath.floor(Hash.hashRoll(id, state.tick, 0x3a2) * 2 * Motion.SUB);
        for (var turn = 0; turn < 4; turn++)
        {
            var dir = DIRS[(k + (turn * 3)) % 12];
            var goal = legTo(state, w.at, dir[0], dir[1], len, mover);
            if (Js.T(goal))
            {
                return goal;
            }
        }
        return null;
    }

    /// <summary>A point `tiles` away from `from` (SUB units): straight away if it can walk there, else up to 90 degrees off either side; half as far if none; or null.</summary>
    /// Across a parked car too (AnimalCars.cs): at once for a rabbit (cover) or a deer (a bound), else only when
    /// no other way is open.
    public static Point? fleeLeg(SimState state, IWalker w, Point from, double tiles, string mover)
    {
        bool cars = AnimalCars.planCost(state, w) > 0;
        bool cover = cars && AnimalCars.fleesAcross(w);
        return fleeLeg(state, w, from, tiles, mover, cover) ?? ((cars && !cover) ? fleeLeg(state, w, from, tiles, mover, true) : null);
    }

    static Point? fleeLeg(SimState state, IWalker w, Point from, double tiles, string mover, bool overCars)
    {
        double ax = w.at.x - from.x;
        double ay = w.at.y - from.y;
        if ((ax == 0) && (ay == 0))
        {
            var dir = DIRS[Js.I(JsMath.floor(Hash.hashRoll(Hash.idNumber(w.id), state.tick, 0x3a3) * 12))];
            ax = dir[0];
            ay = dir[1];
        }
        foreach (var len in new[] { tiles * Motion.SUB, JsMath.max(1, JsMath.floor(tiles / 2)) * Motion.SUB })
        {
            foreach (var turn in FLEE_TURNS)
            {
                var r = DIRS[(turn + 12) % 12];
                // Rotate (ax, ay) by the turn: x' = x cos - y sin, y' = x sin + y cos (cos, sin times 1024).
                var rx = ((double)(ax) * r[0]) - ((double)(ay) * r[1]);
                var ry = ((double)(ax) * r[1]) + ((double)(ay) * r[0]);
                var d = JsMath.sqrt((rx * rx) + (ry * ry));
                var goal = legTo(state, w.at, Js.I(JsMath.round((rx * 1024) / d)), Js.I(JsMath.round((ry * 1024) / d)), len, mover, overCars);
                if (Js.T(goal))
                {
                    return goal;
                }
            }
        }
        return null;
    }
}
