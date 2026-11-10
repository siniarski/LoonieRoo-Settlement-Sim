// Animals and parked cars (2026-11-11): the small go under, the rest go over. A parked car's tiles stop every body
// on foot (VehicleParking.cs); an animal or a dog that wants to be on the far side crosses it instead, in one short
// straight move it can't steer out of, as your leaps are (Leaps.cs).
//
//   who, how   by the animal's size against the two cars (art/scripts/build_furniture.py: the sedan's underside is
//              0.28 m up, its roof 1.28 m; the pickup's underside 0.45 m, its bed 1.0 m, its cab 1.75 m):
//                        sedan               truck
//              rabbit    under (8 ticks)     under (7)       0.35 m tall: room under either
//              dog       over (9)            under (14)      0.67 m: a crawl under the pickup, a jump on the sedan
//              deer      leap (6)            leap (7)        1.43 m: clears either in one bound
//              wolf      over (10)           over (13)       1.0 m: scrambles up and down
//              bear      over (30)           over (40)       too big for under, too heavy to leap: a slow climb
//              The ticks are for the usual crossing, a car's width (two tiles, from the tile beside it to the tile
//              beyond); a longer one takes longer in proportion.
//   paths      A* may cross a tile under a parked car at a cost by kind (COST: tiles' worth, on top of the step),
//              so animals go round when round is short and over when it isn't (a car across a gap, a long car).
//              Walkers.followPath starts the crossing when the next waypoint is under a car; a leg picked straight
//              over one (a flee leg: a rabbit takes cover, a deer bounds, the rest only cornered) crosses where the
//              walk meets the car.
//   the far side  checked before it starts: the body fits there (no wall, no car, the map's rim, no trunk) and the
//              straight way is open but for cars (lineOver: no wall, no edge, no window between). Checked again each
//              tick (a car parked there, a wall built): if not, it stops and is put on the nearest tile it can stand.
//              One that can't cross (no far side) is refused, and plans without cars for REFUSED ticks: it goes
//              round, or stands where it is; it never presses against the car for ever.
//   crossing   plain state on the animal (`cross`, saved): from, to, ticks, done. Each tick moves it a share of
//              what is left (integers, as Leaps.tick), so a save in the middle reads back and plays on the same.
//              It does nothing else meanwhile: no biting, no turning back. Its tile (pos) is the tile under it.
//   under      an animal under a car is out of reach: no blow, bite or shot finds it (hidden: Shooting, Work's
//              hunt, a predator's choice of prey, a zombie's blow on a dog), and a predator after it loses it. A
//              rabbit under a car stays there while something it fears is near (HOLD ticks at most).
//   over       it is in plain sight and can be hit; it can't bite until it is down.
//   drawing    lift(): the animal's height over the ground (metres), for godot/World3D/Animals.cs: an arc for a
//              leap, up on the car and down for a climb, nothing under (drawn on the ground beneath the body,
//              crouched to the room there: room()).
// Rolls: none (the rules are fixed by kind and car). Cost: a crossing is a few fits and a short line a tick, for
// the few animals crossing; planning is A*'s own, a little more where cars are.
#nullable disable warnings
namespace LoonieRoo.Sim;

/// <summary>An animal going under or over a parked car: straight from `from` to `to` (SUB) over `ticks` ticks.</summary>
public sealed class Crossing
{
    /// <summary>"under", "over" (a climb or scramble) or "leap".</summary>
    public string how { get; set; } = "";
    public Point from { get; set; } = new();
    public Point to { get; set; } = new();
    public double ticks { get; set; }
    public double done { get; set; }
    /// <summary>How high the car is where it crosses (cm): over, the top of its climb or the bar its leap clears;
    /// under, the room beneath it.</summary>
    public double top { get; set; }
    /// <summary>Ticks a rabbit has stayed under the car, hiding.</summary>
    public double held { get; set; }
}

public partial class Animal
{
    /// <summary>Going under or over a parked car (AnimalCars.cs), or nothing.</summary>
    public Crossing? cross { get; set; }
    /// <summary>A crossing was refused: no plans over cars before this tick.</summary>
    public double? noCrossUntil { get; set; }
}

public partial class Dog
{
    /// <summary>Going under or over a parked car (AnimalCars.cs), or nothing.</summary>
    public Crossing? cross { get; set; }
    /// <summary>A crossing was refused: no plans over cars before this tick.</summary>
    public double? noCrossUntil { get; set; }
}

public static class AnimalCars
{
    public const string UNDER = "under", OVER = "over", LEAP = "leap";

    /// <summary>How a kind crosses a car and in how many ticks, for the usual crossing (a car's width: two tiles).</summary>
    public readonly record struct Way(string how, int ticks);

    /// <summary>The usual crossing's length (SUB): from the tile beside a car to the tile beyond it.</summary>
    public const int USUAL = 2 * Motion.SUB;

    /// <summary>A path's run under cars this many tiles at most (a car's width on the slant); longer isn't crossed.</summary>
    public const int MAX_RUN = 3;

    /// <summary>A crossing no longer than this (SUB), from where it starts.</summary>
    public const int MAX_SPAN = 4 * Motion.SUB;

    /// <summary>After a refusal, plans keep off cars this long.</summary>
    public const int REFUSED = 60;

    /// <summary>A rabbit stays hidden under a car this long at most.</summary>
    public const int HOLD = 150;

    /// <summary>What a tile under a car costs A* on top of the step, by kind (tiles' worth).</summary>
    static readonly JsRecord<double> COST = new() { ["rabbit"] = 1, ["deer"] = 2, ["dog"] = 3, ["wolf"] = 3, ["bear"] = 8 };

    /// <summary>The way a kind crosses the sedan ("car") or the pickup ("truck").</summary>
    public static Way wayOf(string kind, string? car)
    {
        bool truck = car == "truck";
        return kind switch
        {
            "rabbit" => new Way(UNDER, truck ? 7 : 8),
            "dog" => truck ? new Way(UNDER, 14) : new Way(OVER, 9),
            "deer" => new Way(LEAP, truck ? 7 : 6),
            "wolf" => new Way(OVER, truck ? 13 : 10),
            "bear" => new Way(OVER, truck ? 40 : 30),
            _ => new Way(OVER, 20),
        };
    }

    public static string kindOf(IWalker w) => w is Animal a ? a.kind : "dog";

    /// <summary>Flees across a car as soon as round it: a rabbit (a car is cover) and a deer (it bounds over what is
    /// in its way). The rest only when no other way is open.</summary>
    public static bool fleesAcross(IWalker w) => kindOf(w) is "rabbit" or "deer";

    /// <summary>The mover its body walks as: dogs as people do (any door), animals on open ground.</summary>
    public static string moverOf(IWalker w) => w is Dog ? Pathfinding.SURVIVOR : Pathfinding.ANIMAL;

    /// <summary>Under a car: out of reach of blows, bites and shots.</summary>
    public static bool hidden(IWalker w) => w?.cross?.how == UNDER;

    /// <summary>The animals not under a car (what a blow or a shot can find).</summary>
    public static List<Animal> inTheOpen(List<Animal> animals) => animals.Any(hidden) ? animals.filter((a) => !hidden(a)) : animals;

    /// <summary>What it costs this walker's A* to cross a tile under a car (0: it doesn't, lately refused).</summary>
    public static double planCost(SimState state, IWalker w)
    {
        if (state.grid.parkedCount == 0 || (w.noCrossUntil != null && state.tick < w.noCrossUntil)) return 0;
        return COST.ContainsKey(kindOf(w)) ? COST[kindOf(w)] : 0;
    }

    /// <summary>A* (Pathfinding.findPathToAny): a tile the mover can't stand on, but under a parked car and crossable
    /// at `cost` (above 0): adds the cost to `g` and says yes. Cars only stand on floor.</summary>
    public static bool underCar(Grid grid, int x, int y, double cost, ref double g)
    {
        if (cost <= 0 || !Grids.isParked(grid, x, y) || Grids.getTile(grid, x, y) != Grids.FLOOR) return false;
        g += cost;
        return true;
    }

    /// <summary>The parked car whose body is over a tile's centre, or null.</summary>
    public static Vehicle? carOver(SimState state, int x, int y)
    {
        var c = Motion.centre(new Point(x, y));
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            if (Vehicles.isParked(v) && Vehicles.gap(v, c) <= 0) return v;
        }
        return null;
    }

    /// <summary>Is the straight way from a to b open to this mover, but for tiles under parked cars (they are open
    /// here)? As Zombies.walkLine (the tiles the line passes, and the edges between; no broken window), cars or not.</summary>
    public static bool lineOver(Grid grid, Point a, Point b, string mover)
    {
        int x = (int)JsMath.floor((double)a.x / Motion.SUB), y = (int)JsMath.floor((double)a.y / Motion.SUB);
        int endX = (int)JsMath.floor((double)b.x / Motion.SUB), endY = (int)JsMath.floor((double)b.y / Motion.SUB);
        double dx = b.x - a.x, dy = b.y - a.y;
        int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0, sy = dy > 0 ? 1 : dy < 0 ? -1 : 0;
        bool open(int px, int py, int qx, int qy)
        {
            if (!Grids.inBounds(grid, qx, qy) || !Pathfinding.crossable(grid, px, py, qx, qy, mover) || Grids.edgeKindBetween(grid, px, py, qx, qy) == Grids.EDGE_WINDOW_BROKEN) return false;
            return Pathfinding.isPassable(grid, qx, qy, mover) || (Grids.isParked(grid, qx, qy) && Grids.getTile(grid, qx, qy) == Grids.FLOOR);
        }
        int guard = JsMath.abs(endX - x) + JsMath.abs(endY - y);
        while ((x != endX || y != endY) && guard-- >= 0)
        {
            double nx = sx == 0 ? double.PositiveInfinity : JsMath.abs((double)(sx > 0 ? x + 1 : x) * Motion.SUB - a.x) * JsMath.abs(dy);
            double ny = sy == 0 ? double.PositiveInfinity : JsMath.abs((double)(sy > 0 ? y + 1 : y) * Motion.SUB - a.y) * JsMath.abs(dx);
            if (nx < ny)
            {
                if (!open(x, y, x + sx, y)) return false;
                x += sx;
            }
            else if (ny < nx)
            {
                if (!open(x, y, x, y + sy)) return false;
                y += sy;
            }
            else
            {
                if (!open(x, y, x + sx, y) || !open(x, y, x, y + sy) || !open(x + sx, y, x + sx, y + sy) || !open(x, y + sy, x + sx, y + sy)) return false;
                x += sx;
                y += sy;
            }
        }
        return true;
    }

    /// <summary>Where it would come down: `land` if its body fits there clear of walls, cars and trunks (moved off a
    /// trunk if that fits), else null.</summary>
    static Point? clear(SimState state, Point land, string mover)
    {
        if (!Motion.fits(state.grid, land, mover)) return null;
        var o = Trunks.pushOut(state, land, Motion.BODY_RADIUS);
        if (o.x == land.x && o.y == land.y) return land;
        return Motion.fits(state.grid, o, mover) ? o : null;
    }

    /// <summary>Following a path whose next waypoint is under a car: cross to the first waypoint beyond the cars, if
    /// the way and the far side allow, else refuse (plans keep off cars a while). True either way: the walk is done
    /// for this tick.</summary>
    public static bool start(SimState state, IWalker w, string mover)
    {
        var grid = state.grid;
        var path = w.path;
        int k = 0;
        while (k < path.length && k <= MAX_RUN && Grids.isParked(grid, path[k].x, path[k].y)) k++;
        if (k == 0) return false;
        if (planCost(state, w) <= 0 || k >= path.length || k > MAX_RUN) return refuse(state, w);
        var car = carOver(state, path[0].x, path[0].y);
        var land = car == null ? null : clear(state, Motion.centre(path[k]), mover);
        if (land == null || Motion.distance2(land, w.at) > (double)MAX_SPAN * MAX_SPAN || !lineOver(grid, w.at, land, mover)) return refuse(state, w);
        begin(state, w, car, land);
        w.path = path.slice(k + 1);
        return true;
    }

    /// <summary>Walking straight at a goal across a car (a flee leg picked over one): up to the car, and then over or
    /// under it to the first place beyond where the body fits. False when it isn't only cars in the way (walls: the
    /// walker plans a path instead).</summary>
    public static bool straightOver(SimState state, IWalker w, Point goal, double step, string mover)
    {
        var grid = state.grid;
        if (planCost(state, w) <= 0 || !lineOver(grid, w.at, goal, mover)) return false;
        // March along the line in 32s: the first place the body doesn't fit (a car: the line is open but for cars),
        // then the first beyond where it does.
        var u = Motion.toward(goal.x - w.at.x, goal.y - w.at.y, 32);
        if (u.x == 0 && u.y == 0) return false;
        int blocked = -1;
        Point? land = null;
        for (int i = 1; i * 32 <= MAX_SPAN; i++)
        {
            var p = new Point(w.at.x + u.x * i, w.at.y + u.y * i);
            bool fits = Motion.fits(grid, p, mover);
            if (!fits && blocked < 0)
            {
                // Not a car's tile in the way (a wall's edge the line grazes): not ours to cross.
                if (!Vehicles.overlapsParked(grid, p)) return false;
                blocked = i;
            }
            else if (fits && blocked >= 0)
            {
                land = clear(state, p, mover);
                break;
            }
        }
        if (blocked < 0 || blocked * 32 > step + 32)
        {
            // Not at the car yet: on towards it.
            var v = Motion.toward(goal.x - w.at.x, goal.y - w.at.y, step);
            var to = Motion.slide(state, w.at, v.x, v.y, mover);
            if (to.x == w.at.x && to.y == w.at.y) return refuse(state, w);
            w.at = to;
            w.pos = Motion.tileOf(to);
            return true;
        }
        var touched = new Point(w.at.x + u.x * blocked, w.at.y + u.y * blocked);
        var car = carNear(state, touched);
        if (land == null || car == null) return refuse(state, w);
        begin(state, w, car, land);
        w.path = [];
        return true;
    }

    /// <summary>The parked car whose body is nearest a point (within a tile of it), or null.</summary>
    static Vehicle? carNear(SimState state, Point p)
    {
        Vehicle? best = null;
        double bestGap = Motion.SUB;
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            if (!Vehicles.isParked(v)) continue;
            double g = Vehicles.gap(v, p);
            if (g <= bestGap) { best = v; bestGap = g; }
        }
        return best;
    }

    /// <summary>Its crossing set, and its first tick taken (`land` checked by the caller: a debug scene's too).</summary>
    public static void begin(SimState state, IWalker w, Vehicle car, Point land)
    {
        var way = wayOf(kindOf(w), car.kind);
        double dist = JsMath.sqrt(Motion.distance2(w.at, land));
        double ticks = JsMath.max(2, JsMath.round(way.ticks * dist / USUAL));
        var mid = new Point(Js.I(JsMath.floor((w.at.x + (double)land.x) / 2)), Js.I(JsMath.floor((w.at.y + (double)land.y) / 2)));
        w.cross = new Crossing { how = way.how, from = w.at.Copy(), to = land.Copy(), ticks = ticks, done = 0, top = way.how == UNDER ? underside(car) : topAt(car, mid), held = 0 };
        step(state, w);
    }

    /// <summary>A plan over a car that can't be crossed: none for a while, and a new plan as soon as may be.</summary>
    static bool refuse(SimState state, IWalker w)
    {
        w.noCrossUntil = state.tick + REFUSED;
        w.path = [];
        w.nextPlan = state.tick;
        return true;
    }

    /// <summary>A tick of its crossing, if it is crossing (true: that is all it does this tick).</summary>
    public static bool step(SimState state, IWalker w)
    {
        var c = w.cross;
        if (c == null) return false;
        var grid = state.grid;
        var mover = moverOf(w);
        if (!Motion.fits(grid, c.to, mover) || !lineOver(grid, w.at, c.to, mover))
        {
            stop(state, w, mover);
            return true;
        }
        // A rabbit under a car stays there, halfway, while what it fears is near.
        if (c.how == UNDER && w is Animal a && a.kind == "rabbit" && c.done == JsMath.floor(c.ticks / 2) && c.done > 0 && c.held < HOLD && Animals.rabbitThreat(state, a) != null)
        {
            c.held += 1;
            return true;
        }
        c.done += 1;
        double left = JsMath.max(1, c.ticks - c.done + 1);
        var dx = JsMath.trunc((c.to.x - (double)w.at.x) / left);
        var dy = JsMath.trunc((c.to.y - (double)w.at.y) / left);
        w.at = new Point(Js.I(w.at.x + dx), Js.I(w.at.y + dy));
        w.pos = Motion.tileOf(w.at);
        if (c.done >= c.ticks)
        {
            w.at = c.to.Copy();
            w.pos = Motion.tileOf(w.at);
            w.cross = null;
        }
        return true;
    }

    /// <summary>Its crossing cut short (the far side is taken, or walled off): down where it is if it can stand there,
    /// else on the nearest tile it can.</summary>
    static void stop(SimState state, IWalker w, string mover)
    {
        var grid = state.grid;
        w.cross = null;
        w.path = [];
        if (Motion.fits(grid, w.at, mover)) return;
        var to = Space.nearestTile(grid, w.pos, (x, y) => Pathfinding.isPassable(grid, x, y, mover) && !Vehicles.overlapsParked(grid, Motion.centre(new Point(x, y))), 60);
        if (to != null) Motion.place(w, to);
    }

    /// <summary>How high a car is (cm) over a point of the ground under it, from its model: the sedan's roof over its
    /// cabin, its bonnet and boot lower; the pickup's cab, its bonnet, and the floor of its bed.</summary>
    public static double topAt(Vehicle v, Point p)
    {
        var d = Js.NN(Js.Get(Vehicles.HEADING_DIRS, Js.I(v.heading)));
        // Along the car from its centre, metres (a tile is a metre; HEADING_DIRS are 256 long).
        double along = ((p.x - (double)v.at.x) * d[0] + (p.y - (double)v.at.y) * d[1]) / 256 / Motion.SUB;
        if (v.kind == "truck") return along > 1.4 ? 125 : along > -0.1 ? 175 : 100;
        return along > -1.3 && along < 0.8 ? 128 : 78;
    }

    /// <summary>The room under a car (cm): its underside over the ground (the sedan 0.28 m, the pickup 0.45 m).</summary>
    public static double underside(Vehicle v) => v.kind == "truck" ? 45 : 28;

    /// <summary>The room it has under the car (metres), or null when it isn't under one: the renderer crouches it to fit.</summary>
    public static double? room(Crossing? c) => c?.how == UNDER ? c.top / 100 : null;

    /// <summary>The animal's height over the ground (metres) at a moment of its crossing, `alpha` into the tick (the
    /// renderer's blend). A leap is an arc clearing the car; a climb goes up on it, along and down; under is 0.</summary>
    public static double lift(Crossing? c, double alpha)
    {
        if (c == null || c.how == UNDER || c.ticks < 2) return 0;
        double t = (c.done - 1 + alpha) / (c.ticks - 1);
        if (t <= 0 || t >= 1) return 0;
        double top = c.top / 100;
        if (c.how == LEAP) return 4 * (top + 0.3) * t * (1 - t);
        return top * JsMath.min(1, JsMath.min(t, 1 - t) / 0.3);
    }
}
