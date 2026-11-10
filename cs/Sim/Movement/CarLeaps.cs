// Leaps and parked cars (part of Leaps, 2026-11-11): over a car, onto it, standing on it, and down off it.
//
//   over     steering into a parked car whose body is within CAR_REACH ahead (your centre to its body, on the
//            nearest of the four sides of your way), Space takes you over it to the far side, when a body fits there:
//              a slide   over the bonnet or the boot (the ends, below the windows)       SLIDE_TICKS, SLIDE_COST
//              the bed   over a pickup's bed (behind its cab)                             BED_TICKS, BED_COST
//              the roof  over any of the cabin: side to side through the middle, or
//                        nose to tail                                                     ROOF_TICKS, ROOF_COST
//            and no faster than OVER_PACE a tick, so nose to tail takes longer. The landing is the first spot past
//            the far side (CLEAR beyond it, then LAND_STEP at a time for LAND_SEARCH) where a body fits on the ground,
//            clear of every parked car and trunk, with no edge of any kind between the car and it.
//   onto     standing still facing one, Space climbs onto it (CLIMB_TICKS, CLIMB_COST), CLIMB_IN inside its body;
//            steering into one whose far side has no room does the same. "Over" needs a way in; "onto" doesn't.
//   on top   control.onCar is the car's id (plain state, saved). You walk within its body: your centre inside it,
//            no wall, door or window within your body (Motion.fits as a car's, which ignores the parked tiles).
//            Walking off its edge steps you down (DOWN_STEP_TICKS, nothing spent) where a body fits below, else you
//            stop at the edge and slide along it. Space jumps down on your way (DOWN_TICKS, JUMP_COST) where a body
//            fits, else jumps where you stand. Vehicles.pushOut is for walking on the ground: on top it isn't called.
//   kept     settle(), at the start of each controlled tick: the car gone or driven, you in a car (getting in from
//            its roof) or off its body, and you are put where a body fits (the ground under you, else the nearest
//            tile clear of everything). Letting go of control does the same (letGo). A loaded save is on the roof
//            it was saved on: onCar and your position are in it.
//   heights  height() is your feet over the ground, for the view: a car's top where you are on it (the tops below,
//            from the models' sizes: art/scripts/build_furniture.py car, truck), and a car leap's arc between
//            the heights it was given at take-off. Nothing in the sim reads it.
//   the dead reach you by distance as ever (Motion.REACH, centre to centre; height doesn't count): from beside a car
//            they reach you on its roof. A roof is a lookout, not a refuge.
// A car leap's busy.targetId is "x,y,car,fromCm,toCm,peakCm": the landing (SUB), the car, and the view's heights,
// fixed at take-off. Integers throughout, so it replays exactly from the command log.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Leaps
{
    public const int SLIDE_TICKS = 8, BED_TICKS = 10, ROOF_TICKS = 12, CLIMB_TICKS = 8, DOWN_TICKS = 6, DOWN_STEP_TICKS = 4;
    public const int SLIDE_COST = 10, BED_COST = 12, ROOF_COST = 14, CLIMB_COST = 10;

    /// <summary>How near (SUB, your centre to the body) a car must be ahead: a swing's reach. Walking into a car's end
    /// stops you at the tile under it, which can be most of a tile short of its body, and a step short of that.</summary>
    public const int CAR_REACH = 320;

    /// <summary>Over a car, no faster than this (SUB a tick): a crossing nose to tail takes longer than side to side.</summary>
    public const int OVER_PACE = 64;

    /// <summary>Climbing on, you land this far inside the body's edge (SUB), or at the middle of your way over it if that is nearer.</summary>
    public const int CLIMB_IN = 96;

    /// <summary>The landing past a car: looked for this far beyond CLEAR (SUB), LAND_STEP at a time.</summary>
    public const int LAND_SEARCH = 128, LAND_STEP = 16;

    /// <summary>The ray along your way into and over a car is looked at this often (SUB).</summary>
    const int RAY_STEP = 8;

    /// <summary>A car leap's targetId has this many parts.</summary>
    const int CAR_PARTS = 6;

    /// <summary>A car's top along its length: (SUB ahead of its centre, cm over the ground), tail to nose, straight between.
    /// The sedan: the boot and bonnet at 0.78 m, the roof at 1.28 m. The pickup: the bed's floor at 1 m, the cab's roof at
    /// 1.75 m, the bonnet at 1.25 m.</summary>
    static readonly int[][] SEDAN_TOP = [[-538, 78], [-346, 78], [-282, 128], [166, 128], [230, 78], [538, 78]];
    static readonly int[][] TRUCK_TOP = [[-640, 100], [-40, 100], [-10, 175], [343, 175], [378, 125], [640, 125]];

    /// <summary>The cabin's span along the car (SUB, its glass included): a way over that crosses any of it is over the roof.</summary>
    static (int from, int to) cabin(Vehicle v) => v.kind == "truck" ? (-40, 378) : (-346, 230);

    /// <summary>The car you are standing on, if any (it may have gone: settle() sorts that out).</summary>
    public static Vehicle? carOf(SimState state, Control? c) => c?.onCar == null ? null : Vehicles.vehiclesOf(state).find((v) => v.id == c.onCar);

    static Vehicle? carById(SimState state, string id) => Vehicles.vehiclesOf(state).find((v) => v.id == id);

    /// <summary>`from` moved k SUB along a direction (any length; a unit four-way one moves exactly k).</summary>
    static Point step(Point from, Point d, int k)
    {
        var o = Motion.toward(d.x, d.y, k);
        return new Point { x = Js.I(from.x + o.x), y = Js.I(from.y + o.y) };
    }

    /// <summary>How far ahead of a car's centre a point is, along its heading (SUB).</summary>
    static double alongOf(Vehicle v, Point p)
    {
        var d = Js.NN(Js.Get(Vehicles.HEADING_DIRS, Js.I(v.heading)));
        return ((double)(p.x - v.at.x) * d[0] + (double)(p.y - v.at.y) * d[1]) / 256;
    }

    /// <summary>The car's top (cm over the ground) this far along it.</summary>
    static double topCm(Vehicle v, double along)
    {
        var top = v.kind == "truck" ? TRUCK_TOP : SEDAN_TOP;
        if (along <= top[0][0]) return top[0][1];
        for (int i = 1; i < top.Length; i++)
        {
            if (along > top[i][0]) continue;
            double t = (along - top[i - 1][0]) / (top[i][0] - top[i - 1][0]);
            return top[i - 1][1] + (top[i][1] - top[i - 1][1]) * t;
        }
        return top[^1][1];
    }

    /// <summary>The car's top under a point (m over the ground): what you stand on there.</summary>
    public static double topAt(Vehicle v, Point p) => topCm(v, alongOf(v, p)) / 100;

    /// <summary>The highest of the car's top between two points along it (cm).</summary>
    static double highest(Vehicle v, double lo, double hi)
    {
        double best = JsMath.max(topCm(v, lo), topCm(v, hi));
        foreach (var p in v.kind == "truck" ? TRUCK_TOP : SEDAN_TOP)
        {
            if (p[0] > lo && p[0] < hi) best = JsMath.max(best, p[1]);
        }
        return best;
    }

    /// <summary>Does a body fit on the ground here: no wall, edge or water (as you walk), and clear of every parked car's body and every trunk?</summary>
    public static bool standable(SimState state, Point p)
    {
        if (!Motion.fits(state.grid, p, Pathfinding.FREE)) return false;
        var o = Vehicles.pushOut(state, p, Motion.BODY_RADIUS);
        if (o.x != p.x || o.y != p.y) return false;
        var t = Trunks.pushOut(state, p, Motion.BODY_RADIUS);
        return t.x == p.x && t.y == p.y;
    }

    /// <summary>Can you stand on this car here: your centre inside its body, no wall, door or window within your body?</summary>
    public static bool onTopFits(SimState state, Vehicle v, Point p) => Vehicles.gap(v, p) == 0 && Motion.fits(state.grid, p, Pathfinding.VEHICLE);

    /// <summary>No edge of any kind (wall, door, window, barricade) on the straight line between two points.</summary>
    static bool lineClear(Grid grid, Point a, Point b)
    {
        double len = JsMath.sqrt(Motion.distance2(a, b));
        int n = Js.I(JsMath.max(1, JsMath.ceil(len / 32)));
        var was = Motion.tileOf(a);
        for (int i = 1; i <= n; i++)
        {
            var p = new Point { x = Js.I(a.x + JsMath.trunc((double)(b.x - a.x) * i / n)), y = Js.I(a.y + JsMath.trunc((double)(b.y - a.y) * i / n)) };
            var t = Motion.tileOf(p);
            if (t.x != was.x || t.y != was.y)
            {
                bool open(int px, int py, int qx, int qy) => Grids.edgeKindBetween(grid, px, py, qx, qy) == Grids.EDGE_NONE;
                bool ok = (t.x == was.x || t.y == was.y)
                    ? open(was.x, was.y, t.x, t.y)
                    : (open(was.x, was.y, t.x, was.y) && open(t.x, was.y, t.x, t.y)) || (open(was.x, was.y, was.x, t.y) && open(was.x, t.y, t.x, t.y));
                if (!ok) return false;
                was = t;
            }
        }
        return true;
    }

    /// <summary>How far along a direction from a point inside a car's body its edge is (SUB: the last point inside, to RAY_STEP).</summary>
    static int exitAlong(Vehicle v, Point from, Point d)
    {
        int k = 0, max = 2 * Vehicles.shapeOf(v).halfLength + RAY_STEP;
        while (k < max && Vehicles.gap(v, step(from, d, k + RAY_STEP)) == 0) k += RAY_STEP;
        return k;
    }

    /// <summary>The first spot past a car on the way d from `at` (`edge`: SUB along d to the last point inside its body)
    /// where a body fits on the ground, with no edge between the car and it; null if none within LAND_SEARCH.</summary>
    static Point? groundPast(SimState state, Point at, Point d, int edge)
    {
        var from = step(at, d, edge);
        int first = edge + RAY_STEP + CLEAR;
        for (int k = first; k <= first + LAND_SEARCH; k += LAND_STEP)
        {
            var p = step(at, d, k);
            if (!lineClear(state.grid, from, p)) return null;
            if (standable(state, p)) return p;
        }
        return null;
    }

    /// <summary>The nearest parked car your way d (a four-way unit) goes into within CAR_REACH, with nothing standing
    /// between: the car, and how far along d its body begins and ends (SUB).</summary>
    public static (Vehicle? car, int enter, int leave) carAhead(SimState state, Point at, Point d)
    {
        Vehicle? best = null;
        int bestIn = int.MaxValue, bestOut = 0;
        foreach (var v in Vehicles.vehiclesOf(state))
        {
            if (!Vehicles.isParked(v)) continue;
            double far = Vehicles.shapeOf(v).halfLength + CAR_REACH;
            if (Motion.distance2(v.at, at) > far * far) continue;
            int enter = -1;
            for (int k = RAY_STEP; k <= CAR_REACH; k += RAY_STEP)
            {
                if (Vehicles.gap(v, step(at, d, k)) == 0) { enter = k; break; }
            }
            if (enter < 0 || enter >= bestIn || !lineClear(state.grid, at, step(at, d, enter))) continue;
            best = v;
            bestIn = enter;
            bestOut = enter + exitAlong(v, step(at, d, enter), d);
        }
        return (best, best == null ? 0 : bestIn, bestOut);
    }

    /// <summary>Space with a car: down off the one you're on, or over or onto the one ahead. True if it started a leap.</summary>
    static bool carLeap(SimState state, Survivor s, Control c, Point? ahead, bool steering)
    {
        var on = carOf(state, c);
        if (on != null)
        {
            offCar(state, s, c, on, ahead);
            return true;
        }
        if (ahead == null) return false;
        var (v, enter, leave) = carAhead(state, s.at, ahead);
        if (v == null) return false;
        if (steering && over(state, s, c, v, ahead, enter, leave)) return true;
        if (climb(state, s, c, v, ahead, enter, leave)) return true;
        return !steering && over(state, s, c, v, ahead, enter, leave);
    }

    /// <summary>The busy action of a car leap, from where you are to `land`.</summary>
    static void start(Control c, string what, Point land, Vehicle v, double fromCm, double toCm, double peakCm, int ticks)
    {
        string cm(double h) => Js.Str(JsMath.round(h));
        c.busy = new ControlBusy { what = what, targetId = $"{land.x},{land.y},{v.id},{cm(fromCm)},{cm(toCm)},{cm(peakCm)}", progress = 0, duration = ticks };
    }

    /// <summary>Over a car to the far side, if a body fits there. True if it started.</summary>
    static bool over(SimState state, Survivor s, Control c, Vehicle v, Point d, int enter, int leave)
    {
        var land = groundPast(state, s.at, d, leave);
        if (land == null) return false;
        double a0 = alongOf(v, step(s.at, d, enter)), a1 = alongOf(v, step(s.at, d, leave));
        double lo = JsMath.min(a0, a1), hi = JsMath.max(a0, a1);
        var (cab0, cab1) = cabin(v);
        string part = (hi >= cab0 && lo <= cab1) ? "roof" : (v.kind == "truck" && hi < cab0) ? "bed" : (lo > cab1 ? "bonnet" : "boot");
        int ticks = part == "roof" ? ROOF_TICKS : part == "bed" ? BED_TICKS : SLIDE_TICKS;
        ticks = Js.I(JsMath.max(ticks, JsMath.ceil(JsMath.sqrt(Motion.distance2(s.at, land)) / OVER_PACE)));
        BodyMod.exert(s, part == "roof" ? ROOF_COST : part == "bed" ? BED_COST : SLIDE_COST);
        start(c, "over", land, v, 0, 0, highest(v, lo, hi) + 10, ticks);
        s.note = part == "roof" ? "scrambled over the roof" : part == "bed" ? "climbed over the bed" : $"slid over the {part}";
        return true;
    }

    /// <summary>Up onto a car, CLIMB_IN inside its body on your way (or the middle of the way over it), if you'd fit there. True if it started.</summary>
    static bool climb(SimState state, Survivor s, Control c, Vehicle v, Point d, int enter, int leave)
    {
        int mid = Js.I(JsMath.floor((double)(enter + leave) / 2));
        var at = step(s.at, d, JsMath.min(enter + CLIMB_IN, mid));
        if (!onTopFits(state, v, at))
        {
            at = step(s.at, d, mid);
            if (!onTopFits(state, v, at)) return false;
        }
        double to = topAt(v, at) * 100;
        BodyMod.exert(s, CLIMB_COST);
        start(c, "climb", at, v, 0, to, to + 15, CLIMB_TICKS);
        s.note = v.kind == "truck" ? "climbed onto the truck" : "climbed onto the car";
        return true;
    }

    /// <summary>Space on top of a car: down on your way where a body fits, else a jump where you stand.</summary>
    static void offCar(SimState state, Survivor s, Control c, Vehicle v, Point? d)
    {
        double from = topAt(v, s.at) * 100;
        BodyMod.exert(s, JUMP_COST);
        var land = d == null ? null : groundPast(state, s.at, d, exitAlong(v, s.at, d));
        if (land != null)
        {
            start(c, "down", land, v, from, 0, from + 10, DOWN_TICKS);
            s.note = "jumped down";
            return;
        }
        start(c, "jump", s.at, v, from, from, from + JUMP_HEIGHT * 100, JUMP_TICKS);
        s.note = "jumped";
    }

    /// <summary>A step on top of a car (`by`: the step, SUB): within its body; off its edge, a step down where a body fits
    /// below, else along the edge an axis at a time. Your tile is the one under you, the car's.</summary>
    public static void walkOnTop(SimState state, Survivor s, Control c, Vehicle v, Point by)
    {
        var to = new Point { x = Js.I(s.at.x + by.x), y = Js.I(s.at.y + by.y) };
        if (onTopFits(state, v, to)) s.at = to;
        else
        {
            if (Vehicles.gap(v, to) > 0)
            {
                var land = groundPast(state, s.at, by, exitAlong(v, s.at, by));
                if (land != null)
                {
                    double from = topAt(v, s.at) * 100;
                    start(c, "down", land, v, from, 0, from + 5, DOWN_STEP_TICKS);
                    s.note = "stepped down";
                    return;
                }
            }
            var x = new Point { x = to.x, y = s.at.y };
            if (by.x != 0 && onTopFits(state, v, x)) s.at = x;
            var y = new Point { x = s.at.x, y = to.y };
            if (by.y != 0 && onTopFits(state, v, y)) s.at = y;
        }
        s.pos = Motion.tileOf(s.at);
    }

    /// <summary>A car leap's end: on top of the car (a climb, or a jump on its roof) if it is still there to stand on,
    /// else on the ground where a body fits.</summary>
    static void landed(SimState state, Survivor s, Control c, string what, string carId)
    {
        var v = carById(state, carId);
        if ((what == "climb" || what == "jump") && v != null && Vehicles.isParked(v) && onTopFits(state, v, s.at))
        {
            c.onCar = v.id;
            s.pos = Motion.tileOf(s.at);
            return;
        }
        c.onCar = null;
        drop(state, s);
    }

    /// <summary>Somewhere a body fits on the ground: where you are, else the nearest tile clear of everything.</summary>
    static void drop(SimState state, Survivor s)
    {
        if (standable(state, s.at))
        {
            s.pos = Motion.tileOf(s.at);
            return;
        }
        var to = Space.nearestTile(state.grid, Motion.tileOf(s.at), (x, y) => standable(state, Motion.centre(new Point { x = x, y = y })), 400);
        if (to != null) Motion.place(s, to);
    }

    /// <summary>The start of a controlled tick: still on the car you stand on? Gone, driven, you in a car or off its
    /// body, and you're on the ground where a body fits (in the air, the landing sees to it).</summary>
    public static void settle(SimState state, Survivor s, Control c)
    {
        if (c.onCar == null) return;
        var v = carOf(state, c);
        bool inCar = Vehicles.vehicleOf(state, s) != null;
        if (v != null && Vehicles.isParked(v) && !inCar && (airborne(c) || Vehicles.gap(v, s.at) == 0)) return;
        c.onCar = null;
        if (!inCar && !airborne(c)) drop(state, s);
    }

    /// <summary>Control is about to end or pass to someone else: whoever you were is put on the ground, off any car
    /// and out of any leap.</summary>
    public static void letGo(SimState state)
    {
        var c = state.control;
        var s = ControlMod.controlled(state);
        if (c == null || s == null || (c.onCar == null && !airborne(c))) return;
        c.onCar = null;
        if (airborne(c)) c.busy = null;
        if (Vehicles.vehicleOf(state, s) == null) drop(state, s);
    }

    /// <summary>Put someone on top of a car (a debug scene's, a test's): at `at`, else its middle. False if no body fits there.</summary>
    public static bool standOn(SimState state, Survivor s, Vehicle v, Point? at = null)
    {
        var c = state.control;
        var p = at ?? Js.CopyAs<Point>(v.at);
        if (c == null || c.survivorId != s.id || !Vehicles.isParked(v) || !onTopFits(state, v, p)) return false;
        s.at = p;
        s.was = Js.CopyAs<Point>(p);
        s.pos = Motion.tileOf(p);
        s.move = null;
        c.onCar = v.id;
        return true;
    }

    /// <summary>Your feet over the ground (m) for the view, at the renderer's blend `alpha` into the tick: a leap's arc
    /// (a car leap's between the heights it was given at take-off, and never inside the car it is over), the top of the
    /// car you stand on, else 0. Only the controlled survivor leaves the ground.</summary>
    public static double height(SimState state, Survivor s, double alpha)
    {
        var c = state.control;
        if (c == null || c.survivorId != s.id || Vehicles.vehicleOf(state, s) != null) return 0;
        var b = c.busy;
        var here = new Point { x = Js.I(JsMath.round(s.was.x + (s.at.x - s.was.x) * alpha)), y = Js.I(JsMath.round(s.was.y + (s.at.y - s.was.y) * alpha)) };
        if (b != null && isLeap(b.what))
        {
            var parts = b.targetId.Split(',');
            if (parts.Length < CAR_PARTS) return lift(b, alpha);
            double t = b.duration < 2 ? 1 : JsMath.max(0, JsMath.min(1, (b.progress - 1 + alpha) / (b.duration - 1)));
            double from = Js.Num(parts[3]) / 100, to = Js.Num(parts[4]) / 100, peak = Js.Num(parts[5]) / 100;
            double line = from + (to - from) * t;
            double h = line + (peak - line) * JsMath.sqrt(JsMath.max(0, JsMath.sin(JsMath.PI * t)));
            var v = carById(state, parts[2]);
            if (v != null && Vehicles.gap(v, here) == 0) h = JsMath.max(h, topAt(v, here));
            return h;
        }
        var on = carOf(state, c);
        return on != null && Vehicles.gap(on, here) == 0 ? topAt(on, here) : 0;
    }
}
