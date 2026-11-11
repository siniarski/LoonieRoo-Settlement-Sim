// The car you sit in (part of Vehicles; 2026-11-12): its body is in the way, and the dead batter it, not you.
//
//   solid         a car with someone at the wheel (on the map: not away on an expedition) keeps the dead, animals
//                 and dogs out of its body: once a tick (updateVehicles), anyone whose body overlaps it is moved out
//                 straight away from its middle line, as pushOut stops you at a parked car. No tiles are marked: a
//                 driven car moves every tick, and marking would cost a walkability bump each tick. Their paths
//                 still go through it; they press against its side, which is where they want to be.
//   the cost      a list of the cars with a driver (almost always none, or yours), then a box test per mover per
//                 such car: nothing when nobody drives.
//   battering     one of the dead within a bite of the body (CABIN_REACH, from its centre to the body's side)
//                 strikes the car (BATTER_DAMAGE a blow), not the driver; once the car is a wreck (health 0) its
//                 blows reach the driver through it. A wolf or bear does the same. Out of the car, you are bitten
//                 as anyone.
//   ramming       a moving car still runs down what it hits (Vehicles.move), before anyone is moved out of it.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>A blow reaches a car's side from this far (SUB, from the attacker's centre): a bite's reach less a body.</summary>
    public const int CABIN_REACH = Motion.REACH - Motion.BODY_RADIUS;

    /// <summary>Someone at the wheel, here on the map (an expedition's driver is "x:..." and away).</summary>
    public static bool occupied(Vehicle v) => Js.T(v.driverId) && !v.driverId.startsWith("x:");

    /// <summary>The cars with someone in them, in list order (usually none, or yours).</summary>
    public static List<Vehicle> cabins(SimState state)
    {
        var list = new List<Vehicle>();
        foreach (var v in vehiclesOf(state))
        {
            if (occupied(v)) list.Add(v);
        }
        return list;
    }

    /// <summary>A body of radius r at (px, py) moved out of one car's body, straight away from its middle line;
    /// false if it doesn't overlap (or is on the line itself, with no way out to choose).</summary>
    static bool outOf(Vehicle v, ref double px, ref double py, int r)
    {
        var shape = shapeOf(v);
        double far = shape.halfLength + r;
        if (JsMath.abs(px - v.at.x) > far || JsMath.abs(py - v.at.y) > far) return false;
        var body = circles(shape, v.at, v.heading);
        Point a = body[0], b = body[^1];
        double lx = b.x - a.x, ly = b.y - a.y;
        double t = JsMath.max(0, JsMath.min(1, ((px - a.x) * lx + (py - a.y) * ly) / (lx * lx + ly * ly)));
        double cx = JsMath.round(a.x + lx * t), cy = JsMath.round(a.y + ly * t);
        double ox = px - cx, oy = py - cy;
        double keep = shape.halfWidth + r;
        if (ox * ox + oy * oy >= keep * keep) return false;
        if (ox == 0 && oy == 0) return false;
        var o = Motion.toward(Js.I(ox), Js.I(oy), keep);
        px = cx + o.x;
        py = cy + o.y;
        return true;
    }

    /// <summary>Move a free mover out of any of these cars' bodies, if it fits where that puts it (else it stays).</summary>
    public static void keepOut(SimState state, Body e, List<Vehicle> cars)
    {
        double px = e.at.x, py = e.at.y;
        bool moved = false;
        foreach (var v in cars) moved |= outOf(v, ref px, ref py, Motion.BODY_RADIUS);
        if (!moved) return;
        var to = new Point { x = Js.I(px), y = Js.I(py) };
        if (!Motion.fits(state.grid, to, Pathfinding.FREE)) return;
        e.at = to;
        e.pos = Motion.tileOf(to);
    }

    /// <summary>A box round every one of these cars, `margin` wider each way (SUB): x0, y0, x1, y1. One test against
    /// it is all most of the dead cost.</summary>
    public static (int x0, int y0, int x1, int y1) boxOf(List<Vehicle> cars, int margin)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var v in cars)
        {
            int far = shapeOf(v).halfLength + margin;
            x0 = Math.Min(x0, v.at.x - far); y0 = Math.Min(y0, v.at.y - far);
            x1 = Math.Max(x1, v.at.x + far); y1 = Math.Max(y1, v.at.y + far);
        }
        return (x0, y0, x1, y1);
    }

    static bool inBox((int x0, int y0, int x1, int y1) b, Point p) => p.x >= b.x0 && p.x <= b.x1 && p.y >= b.y0 && p.y <= b.y1;

    /// <summary>Once a tick: the dead, animals and dogs out of every car with someone in it.</summary>
    public static void keepOutOfCabins(SimState state)
    {
        var cars = cabins(state);
        if (cars.Count == 0) return;
        var box = boxOf(cars, Motion.BODY_RADIUS);
        foreach (var z in state.zombies)
        {
            if (z.crossing == null && inBox(box, z.at)) keepOut(state, z, cars);
        }
        foreach (var a in state.animals)
        {
            if (a.cross == null && inBox(box, a.at)) keepOut(state, a, cars);
        }
        foreach (var d in state.dogs)
        {
            if (inBox(box, d.at)) keepOut(state, d, cars);
        }
    }

    /// <summary>The car with someone in it whose side is within a blow of a point, or null.</summary>
    public static Vehicle? cabinBeside(List<Vehicle> cars, Point at, (int x0, int y0, int x1, int y1) box)
    {
        if (!inBox(box, at)) return null;
        foreach (var v in cars)
        {
            double far = shapeOf(v).halfLength + CABIN_REACH;
            if (JsMath.abs(at.x - v.at.x) > far || JsMath.abs(at.y - v.at.y) > far) continue;
            if (gap(v, at) <= CABIN_REACH) return v;
        }
        return null;
    }

    /// <summary>Is this survivor sitting in one of these cars?</summary>
    public static bool inCabin(List<Vehicle> cars, Survivor s)
    {
        foreach (var v in cars)
        {
            if (v.driverId == s.id) return true;
        }
        return false;
    }

    /// <summary>The driver of a car with someone in it.</summary>
    public static Survivor? driverOf(SimState state, Vehicle v) => occupied(v) ? state.survivors.find((s) => s.id == v.driverId) : null;

    /// <summary>A blow at a car with someone in it: the car takes it. False once it is a wreck (the blow is the driver's).</summary>
    public static bool strikeCabin(SimState state, Vehicle v)
    {
        if (v.health <= 0) return false;
        dent(state, v, BATTER_DAMAGE);
        return true;
    }
}
