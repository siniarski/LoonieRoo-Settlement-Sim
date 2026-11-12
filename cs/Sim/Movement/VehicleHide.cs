// Under a pickup (part of Vehicles; 2026-11-12): crouched (C) beside a parked pickup, E gets you under it, out of
// sight. The sedan is too low (0.28 m under it, the pickup 0.45 m: VehicleHeights.cs).
//
//   going under   E at a parked pickup while sneaking (ControlMod.use): you lie under its middle (Control.underCar).
//                 The dead after you then that are within SEE_UNDER tiles and see you (Sight.zombieSees) saw you go
//                 under (Control.sawUnder): they keep after you. The rest of those after you lose you after
//                 LOSE_TICKS; none notice you from then (Zombies.notices, chaseTarget).
//   reached by    crawlers: they see under it and go under it (Crossings.cs). The others can't get at you: from
//                 beside the pickup its middle is out of a bite's reach, and over it they don't bite.
//   under it      no swinging or shooting, no jumping, no using; the eye is a lying one's. Any move key crawls you
//                 out on that side of it (the side nearest the way you steer: left, right, front or back), or the
//                 next nearest where there is room; nowhere free, you stay.
//   it goes       pushed or driven off, or gone: you are out where you lie.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>The dead after you that don't see you go under lose you after this many ticks.</summary>
    public const int LOSE_TICKS = 30;

    /// <summary>Within this many tiles (Manhattan), the dead after you see you go under, if they can see you at all.</summary>
    public const int SEE_UNDER = 10;

    /// <summary>Is there room under it for you (a pickup), and is it standing?</summary>
    public static bool roomUnder(Vehicle v) => v.kind == "truck" && isParked(v);

    /// <summary>The pickup this survivor is lying under (only the one you play), or null.</summary>
    public static Vehicle? underOf(SimState state, Survivor s)
    {
        var c = state.control;
        if (c?.underCar == null || c.survivorId != s.id) return null;
        return getVehicle(state, c.underCar);
    }

    /// <summary>Under it: you lie under its middle, and the dead after you that see you now saw you go.</summary>
    public static string hideUnder(SimState state, Survivor s, Vehicle v)
    {
        var c = Js.NN(state.control);
        var saw = new List<string>();
        foreach (var z in state.zombies)
        {
            if (z.targetId == s.id && Space.manhattan(z.pos, s.pos) <= SEE_UNDER && Sight.zombieSees(state, z.pos, s.pos, false)) saw.Add(z.id);
        }
        c.underCar = v.id;
        c.underTick = state.tick;
        c.sawUnder = saw;
        c.busy = null;
        s.move = null;
        s.at = Js.CopyAs<Point>(v.at);
        s.pos = Motion.tileOf(v.at);
        return "crawled under the pickup";
    }

    /// <summary>Is this one of the dead blind to you, under a pickup? Not a crawler (it looks under), not one that saw
    /// you go, and not one after you until it has lost you (LOSE_TICKS).</summary>
    public static bool unseenUnder(SimState state, Zombie z, Survivor s)
    {
        if (underOf(state, s) == null) return false;
        var c = state.control;
        if (z.variant == "crawler" || (c.sawUnder?.Contains(z.id) ?? false)) return false;
        return !(z.targetId == s.id && state.tick < (c.underTick ?? 0) + LOSE_TICKS);
    }

    /// <summary>Out from under: the pickup gone or moved, nothing to do but get up where you are.</summary>
    public static void outFromUnder(Control c)
    {
        c.underCar = null;
        c.underTick = null;
        c.sawUnder = null;
    }

    /// <summary>One tick under it: any move key crawls you out on that side (or the nearest side with room). True
    /// while you are under (nothing else of your tick happens).</summary>
    public static bool tickUnder(SimState state, Survivor s, Control c)
    {
        if (c.underCar == null) return false;
        var v = underOf(state, s);
        if (v == null || !isParked(v) || Motion.distance2(v.at, s.at) > (double)Motion.SUB * Motion.SUB)
        {
            outFromUnder(c);
            return false;
        }
        c.busy = null;
        if (c.dx == 0 && c.dy == 0) return true;
        var shape = shapeOf(v);
        var (hx, hy) = Js.NN(Js.Get(HEADING_DIRS, Js.I(v.heading)));
        // The four ways out, by how near each is to the way you steer: front, back, its left, its right.
        double along = shape.halfLength + Motion.BODY_RADIUS + 8, across = shape.halfWidth + Motion.BODY_RADIUS + 8;
        var ways = new List<(double score, Point at)>();
        void way(double ux, double uy, double far)
        {
            double score = (ux * c.dx + uy * c.dy);
            ways.Add((score, new Point { x = Js.I(v.at.x + JsMath.round(ux * far / 256)), y = Js.I(v.at.y + JsMath.round(uy * far / 256)) }));
        }
        way(hx, hy, along);
        way(-hx, -hy, along);
        way(hy, -hx, across);
        way(-hy, hx, across);
        // Best first; a stable order for ties (the list's).
        var order = ways.Select((w, i) => (w, i)).OrderByDescending((t) => t.w.score).ThenBy((t) => t.i).Select((t) => t.w.at).ToList();
        foreach (var to in order)
        {
            if (!Grids.inBounds(state.grid, Motion.tileOf(to).x, Motion.tileOf(to).y)) continue;
            if (!Motion.fits(state.grid, to, Pathfinding.FREE) || gap(v, to) < Motion.BODY_RADIUS) continue;
            s.at = to;
            s.pos = Motion.tileOf(to);
            outFromUnder(c);
            s.note = "crawled out from under the pickup";
            return true;
        }
        return true;
    }
}
