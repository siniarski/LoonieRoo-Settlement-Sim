// A car's heights (part of Vehicles; 2026-11-11): its top along its length and the room under it, from its model
// (art/scripts/build_furniture.py: car, truck). VehicleBody.cs is the same car seen from above.
//
//                 sedan                    pickup
//   underside     0.28 m                   0.45 m
//   tail          the boot, 0.78 m         the bed's floor, 1.0 m
//   cabin         the roof, 1.28 m         the cab's roof, 1.75 m
//   nose          the bonnet, 0.78 m       the bonnet, 1.25 m
//
// Whoever gets on or under a car reads it here: you (CarLeaps.cs), the dead (Crossings.cs), animals (AnimalCars.cs).
// Places along a car are SUB ahead of its centre, on its heading (alongOf). The top has two readings, each as its
// reader had it before the three were brought together (2026-11-11, the quality pass):
//   topCm      sloped across the glass (straight between TOP's points): what you stand on and look from, so the
//              view doesn't jump as you walk from the bonnet up to the roof
//   partTopCm  flat, a part at a time, stepping at the glass: what the dead and animals are drawn on
// Of all this only the cabin's span decides anything in the sim (a leap that crosses any of it is over the roof:
// CarLeaps.cs); the heights are for the view and for what is drawn.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>A car's top along its length: (SUB ahead of its centre, cm over the ground), tail to nose, straight between.
    /// The sedan: the boot and bonnet at 0.78 m, the roof at 1.28 m. The pickup: the bed's floor at 1 m, the cab's roof at
    /// 1.75 m, the bonnet at 1.25 m.</summary>
    static readonly int[][] SEDAN_TOP = [[-538, 78], [-346, 78], [-282, 128], [166, 128], [230, 78], [538, 78]];
    static readonly int[][] TRUCK_TOP = [[-640, 100], [-40, 100], [-10, 175], [343, 175], [378, 125], [640, 125]];

    static int[][] topOf(Vehicle v) => v.kind == "truck" ? TRUCK_TOP : SEDAN_TOP;

    /// <summary>How far ahead of a car's centre a point is, along its heading (SUB).</summary>
    public static double alongOf(Vehicle v, Point p)
    {
        var d = Js.NN(Js.Get(HEADING_DIRS, Js.I(v.heading)));
        return ((double)(p.x - v.at.x) * d[0] + (double)(p.y - v.at.y) * d[1]) / 256;
    }

    /// <summary>The cabin's span along the car (SUB, its glass included): a way over that crosses any of it is over the roof.</summary>
    public static (int from, int to) cabin(Vehicle v) => v.kind == "truck" ? (-40, 378) : (-346, 230);

    /// <summary>The car's top (cm over the ground) this far along it, sloped across the glass.</summary>
    public static double topCm(Vehicle v, double along)
    {
        var top = topOf(v);
        if (along <= top[0][0]) return top[0][1];
        for (int i = 1; i < top.Length; i++)
        {
            if (along > top[i][0]) continue;
            double t = (along - top[i - 1][0]) / (top[i][0] - top[i - 1][0]);
            return top[i - 1][1] + (top[i][1] - top[i - 1][1]) * t;
        }
        return top[^1][1];
    }

    /// <summary>The car's top under a point (m over the ground), sloped across the glass: what you stand on there.</summary>
    public static double topAt(Vehicle v, Point p) => topCm(v, alongOf(v, p)) / 100;

    /// <summary>The highest of the car's top between two places along it (cm).</summary>
    public static double highestCm(Vehicle v, double lo, double hi)
    {
        double best = JsMath.max(topCm(v, lo), topCm(v, hi));
        foreach (var p in topOf(v))
        {
            if (p[0] > lo && p[0] < hi) best = JsMath.max(best, p[1]);
        }
        return best;
    }

    /// <summary>The top of the part of the car this far along it (cm), flat: the sedan's boot, cabin or bonnet; the
    /// pickup's bed, cab or bonnet. It steps where the glass is, a little inside the cabin's span.</summary>
    public static double partTopCm(Vehicle v, double along)
    {
        if (v.kind == "truck") return along > 358 ? 125 : along >= -26 ? 175 : 100;
        return along >= -333 && along <= 205 ? 128 : 78;
    }

    /// <summary>The room under a car (cm): its underside over the ground.</summary>
    public static double undersideCm(Vehicle v) => v.kind == "truck" ? 45 : 28;
}
