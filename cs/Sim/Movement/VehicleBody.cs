// A car's body (part of Vehicles): the room it takes up, the size it is drawn at.
//
// Until 2026-11-11 a car was one circle 0.43 m in radius, whatever was drawn. Now it is as long and as wide as its
// model (godot/World3D/Cars.cs: the sedan 4.2 x 1.7 m, the pickup 5 x 1.9 m), as a row of circles along its length,
// each as wide as the car: Motion.fits already knows how a circle meets walls, edges and the map's rim, so the body
// is solid wherever any of its circles would be.
//
//   solid to it   walls and edge walls, doors, windows, barricades (Motion.fits, mover "vehicle"); furniture, as its
//                 whole tile (all but traps and pits); a tree, as its trunk (Trunks.cs); other cars' bodies.
//                 Not people or the dead: it runs those down (Vehicles.move).
//   moving        advance() (for Vehicles.move) tries the whole step, then each axis on its own (it slides along a wall), then as
//                 much of the step as fits (its bumper ends up at the wall)
//   turning       about the rear axle, as a car does: the nose swings, the tail stays, so a car alongside a wall can
//                 steer away from it. A turn that would put the body into something isn't made.
//   stuck         a car that doesn't fit where it stands (an old save's, parked by its old circle) may still move
//                 and turn, as long as no more of it ends up in something solid: it can get itself out.
//   near          gap() is how far a point is from the body, not from its centre: getting in, the boot and the
//                 bonnet (VehicleActions.cs) and "the car beside you" measure that. What is in the boot is at
//                 the boot (bootTile: the back of the body), so you reach it from behind the car.
// Integer maths in SUB units (rule 10): circle centres are the centre plus a whole offset along a heading.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>A body: half its length and half its width (SUB), and how far ahead of its centre the driver sits.</summary>
    public readonly record struct Shape(int halfLength, int halfWidth, int seat);

    /// <summary>The sedan: 4.2 x 1.7 m. The pickup: 5 x 1.9 m, its cab well forward.</summary>
    public static readonly Shape CAR = new(538, 218, 26), TRUCK = new(640, 243, 166);

    /// <summary>The rear axle is this share of the half length behind the centre: a turn pivots there.</summary>
    public const double AXLE = 0.62;

    public static Shape shapeOf(string? kind) => kind == "truck" ? TRUCK : CAR;

    public static Shape shapeOf(Vehicle v) => shapeOf(v.kind);

    /// <summary>The centres of the body's circles (each of radius shape.halfWidth), tail first, for a car centred
    /// at `at` on a heading. Spaced no further apart than a radius, so nothing as wide as a wall's end slips between.</summary>
    public static List<Point> circles(Shape shape, Point at, double heading)
    {
        var d = Js.NN(Js.Get(HEADING_DIRS, Js.I(heading)));
        int r = shape.halfWidth;
        int span = 2 * (shape.halfLength - r);
        int n = (int)Math.Ceiling((double)span / r) + 1;
        var list = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            double along = JsMath.trunc((double)span * i / (n - 1)) - (shape.halfLength - r);
            list.Add(new Point { x = Js.I(at.x + JsMath.trunc(d[0] * along / 256)), y = Js.I(at.y + JsMath.trunc(d[1] * along / 256)) });
        }
        return list;
    }

    /// <summary>How many of a body's circles would be in something solid, for a car centred at `at` on a heading
    /// (`self`: the car asking, left out of the other cars; null for one not in the world yet).</summary>
    public static int stuck(SimState state, Shape shape, Point at, double heading, Vehicle? self)
    {
        int n = 0;
        foreach (var c in circles(shape, at, heading))
        {
            if (solid(state, c, shape.halfWidth, self)) n++;
        }
        return n;
    }

    /// <summary>Does a car's whole body fit here, on this heading?</summary>
    public static bool fitsAt(SimState state, Shape shape, Point at, double heading, Vehicle? self = null) => stuck(state, shape, at, heading, self) == 0;

    /// <summary>Is a circle of the body in a wall, a piece of furniture's tile, a tree's trunk or another car?</summary>
    static bool solid(SimState state, Point c, int r, Vehicle? self)
    {
        if (!Motion.fits(state.grid, c, Pathfinding.VEHICLE, r)) return true;
        int x0 = (int)JsMath.floor((double)(c.x - r - Trunks.RADIUS) / Motion.SUB), x1 = (int)JsMath.floor((double)(c.x + r + Trunks.RADIUS) / Motion.SUB);
        int y0 = (int)JsMath.floor((double)(c.y - r - Trunks.RADIUS) / Motion.SUB), y1 = (int)JsMath.floor((double)(c.y + r + Trunks.RADIUS) / Motion.SUB);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var f = FurnitureMod.furnitureAt(state, x, y);
                if (f == null || f.kind == "trap" || f.kind == "pit") continue;
                if (f.kind == "tree")
                {
                    double reach = r + Trunks.RADIUS;
                    if (Motion.distance2(c, Trunks.trunkAt(x, y)) < reach * reach) return true;
                }
                else if (touchesTile(c, r, x, y)) return true;
            }
        }
        foreach (var other in vehiclesOf(state))
        {
            if (other == self) continue;
            var os = shapeOf(other);
            double far = r + os.halfLength;
            if (Motion.distance2(c, other.at) >= far * far) continue;
            double touch = r + os.halfWidth;
            foreach (var oc in circles(os, other.at, other.heading))
            {
                if (Motion.distance2(c, oc) < touch * touch) return true;
            }
        }
        return false;
    }

    /// <summary>Does a circle reach into a tile's square?</summary>
    static bool touchesTile(Point c, int r, int x, int y)
    {
        double px = JsMath.max(x * Motion.SUB, JsMath.min((x + 1) * Motion.SUB, c.x));
        double py = JsMath.max(y * Motion.SUB, JsMath.min((y + 1) * Motion.SUB, c.y));
        return (c.x - px) * (c.x - px) + (c.y - py) * (c.y - py) < (double)r * r;
    }

    /// <summary>May the car be here on this heading: it fits, or (already in something: `before` of its circles)
    /// no more of it is in something than was.</summary>
    static bool allowed(SimState state, Vehicle v, Point at, double heading, int before)
    {
        int n = stuck(state, shapeOf(v), at, heading, v);
        return n == 0 || (before > 0 && n <= before);
    }

    /// <summary>Move the body by (dx, dy), stopping at what is solid: the whole step if it fits, else each axis on
    /// its own, so it slides along a wall. Big moves go in steps no longer than its half width. Returns the new centre.</summary>
    public static Point advance(SimState state, Vehicle v, int dx, int dy)
    {
        var shape = shapeOf(v);
        double x = v.at.x, y = v.at.y;
        int before = stuck(state, shape, v.at, v.heading, v);
        var steps = JsMath.max(1, JsMath.ceil((double)JsMath.max(JsMath.abs(dx), JsMath.abs(dy)) / shape.halfWidth));
        for (var i = 1; i <= steps; i++)
        {
            var sx = JsMath.trunc((double)dx * i / steps) - JsMath.trunc((double)dx * (i - 1) / steps);
            var sy = JsMath.trunc((double)dy * i / steps) - JsMath.trunc((double)dy * (i - 1) / steps);
            if (allowed(state, v, new Point { x = Js.I(x + sx), y = Js.I(y + sy) }, v.heading, before))
            {
                x += sx;
                y += sy;
            }
            else
            {
                bool moved = false;
                if ((sx != 0) && allowed(state, v, new Point { x = Js.I(x + sx), y = Js.I(y) }, v.heading, before)) { x += sx; moved = true; }
                if ((sy != 0) && allowed(state, v, new Point { x = Js.I(x), y = Js.I(y + sy) }, v.heading, before)) { y += sy; moved = true; }
                if (!moved)
                {
                    // Up to what is in the way: the largest part of the step that fits, by halving (six looks: to
                    // 1/64 of the step), so the bumper ends at the wall and not a step short of it.
                    double lo = 0, hi = 1;
                    for (int k = 0; k < 6; k++)
                    {
                        double mid = (lo + hi) / 2;
                        if (allowed(state, v, new Point { x = Js.I(x + JsMath.trunc(sx * mid)), y = Js.I(y + JsMath.trunc(sy * mid)) }, v.heading, before)) lo = mid;
                        else hi = mid;
                    }
                    x += JsMath.trunc(sx * lo);
                    y += JsMath.trunc(sy * lo);
                }
            }
            if (before > 0) before = stuck(state, shape, new Point { x = Js.I(x), y = Js.I(y) }, v.heading, v);
        }
        return new Point { x = Js.I(x), y = Js.I(y) };
    }

    /// <summary>Turn one heading step (`by`: -1 left, 1 right): about the rear axle if the body fits there, else
    /// where it stands, else not at all. Returns whether it turned.</summary>
    public static bool turn(SimState state, Vehicle v, double by)
    {
        var shape = shapeOf(v);
        var to = ((v.heading + by) + HEADINGS) % HEADINGS;
        int before = stuck(state, shape, v.at, v.heading, v);
        var a = Js.NN(Js.Get(HEADING_DIRS, Js.I(v.heading)));
        var b = Js.NN(Js.Get(HEADING_DIRS, Js.I(to)));
        double axle = JsMath.trunc(shape.halfLength * AXLE);
        // The axle stays put: the centre goes from ahead of it on the old heading to ahead of it on the new.
        var pivoted = new Point { x = Js.I(v.at.x + JsMath.trunc((b[0] - a[0]) * axle / 256)), y = Js.I(v.at.y + JsMath.trunc((b[1] - a[1]) * axle / 256)) };
        if (allowed(state, v, pivoted, to, before))
        {
            v.at = pivoted;
            v.pos = Motion.tileOf(pivoted);
        }
        else if (!allowed(state, v, v.at, to, before))
        {
            return false;
        }
        v.heading = to;
        return true;
    }

    /// <summary>How far a point is from the car's body (SUB; 0 inside it).</summary>
    public static double gap(Vehicle v, Point p)
    {
        var shape = shapeOf(v);
        double best = double.PositiveInfinity;
        foreach (var c in circles(shape, v.at, v.heading))
        {
            best = JsMath.min(best, JsMath.sqrt(Motion.distance2(c, p)));
        }
        return JsMath.max(0, best - shape.halfWidth);
    }

    /// <summary>The tile the boot is over (the back half-metre of the body): where what is in it is, for reaching it.
    /// A car is too long for its boot to be within a hand's reach of its middle.</summary>
    public static Point bootTile(Vehicle v)
    {
        var d = Js.NN(Js.Get(HEADING_DIRS, Js.I(v.heading)));
        double along = -(shapeOf(v).halfLength - Motion.SUB / 2);
        return Motion.tileOf(new Point { x = Js.I(v.at.x + JsMath.trunc(d[0] * along / 256)), y = Js.I(v.at.y + JsMath.trunc(d[1] * along / 256)) });
    }

    /// <summary>Is anything of the car's body within `reach` of a point?</summary>
    public static bool within(Vehicle v, Point p, double reach) => gap(v, p) <= reach;
}
