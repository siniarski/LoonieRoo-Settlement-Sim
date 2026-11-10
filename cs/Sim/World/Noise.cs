// Ported from src/sim/noise.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Noise: building, fighting and gunfire carry. A weapon has its own radius (pistol 22 ... rifle 36, crossbow 4:
// WeaponTable.cs); "gunfire" below is a turret's and the old default.
//
// A noise of radius r (Manhattan) at a point wakes the dormant zombies within
// r, draws idle static and herd zombies within r to come and look, and turns
// any herd within HERD_HEARING x r towards it (on through the point and out
// the far side). Nothing is stored: a noise happens and its effects stay.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Noise
{

    /// <summary>How far each kind of noise carries.</summary>
    public static readonly JsRecord<double> NOISE = new() { ["building"] = 10, ["fighting"] = 6, ["gunfire"] = 30, ["generator"] = 12, ["scream"] = 30, ["lure"] = 25, ["glass"] = 15, ["alarm"] = 25, ["helicopter"] = 35, ["engine"] = 18 };

    /// <summary>Herds hear from this many times further than a noise's radius.</summary>
    public const int HERD_HEARING = 2;

    /// <summary>A noise of the kind's usual radius, or of `radius` (a weapon's own: WeaponTable.cs, a gun's report or a blow).</summary>
    public static void makeNoise(SimState state, Point p, string kind, double? radius = null)
    {
        var r = radius ?? Js.Get(NOISE, kind);
        var woke = wakeDormantNear(state, p, r);
        if (woke > 0)
        {
            Events.logEvent(state, $"The {Js.Str(kind)} at ({Js.Str(p.x)}, {Js.Str(p.y)}) woke {Js.Str((woke == 1) ? Js.Str("a zombie") : Js.Str($"{Js.Str(woke)} zombies"))}");
        }
        var __d0 = state.grid;
        var width = __d0.width;
        var height = __d0.height;
        foreach (var z in Js.Iter(state.zombies))
        {
            if (Js.T((Js.T((((z.kind != "static") && (z.kind != "herd")) || (z.targetId != null)) || Js.T(z.hordeId))) || (Space.manhattan(z.pos, p) > r)))
            {
                continue;
            }
            z.heading = (z.kind == "static") ? new Point { x = Js.I(p.x), y = Js.I(p.y) } : new Point { x = Js.I(clamp(((double)(2) * (p.x)) - z.pos.x, width)), y = Js.I(clamp(((double)(2) * (p.y)) - z.pos.y, height)) };
            z.path = [];
        }
        Hordes.hordesHear(state, p, r, (from) => new Point { x = Js.I(clamp(((double)(2) * (p.x)) - from.x, width)), y = Js.I(clamp(((double)(2) * (p.y)) - from.y, height)) });
        foreach (var h in Js.Iter(state.herds))
        {
            if (Space.manhattan(h, p) > ((double)(r) * (HERD_HEARING)))
            {
                continue;
            }
            Point to = new Point { x = Js.I(clamp(((double)(2) * (p.x)) - h.x, width)), y = Js.I(clamp(((double)(2) * (p.y)) - h.y, height)) };
            if ((to.x == h.to.x) && (to.y == h.to.y))
            {
                continue;
            }
            h.to = to;
            Events.logEvent(state, $"A herd of {Js.Str(h.size)} turned towards the {Js.Str(kind)} at ({Js.Str(p.x)}, {Js.Str(p.y)})");
        }
    }

    public static double clamp(double v, double size)
    {
        return JsMath.max(0, JsMath.min(size - 1, v));
    }

    /// <summary>Dormant zombies within r (Manhattan) of p get up as static zombies, in the list's order (Dormant.cs buckets them). Returns how many.</summary>
    public static double wakeDormantNear(SimState state, Point p0, double r)
    {
        double woke = 0;
        foreach (var d in Dormant.near(state, p0.x, p0.y, r))
        {
            if (state.zombies.length >= Zombies.MAX_ZOMBIES)
            {
                break;
            }
            Dormant.remove(state, d);
            var p = (Grids.getTile(state.grid, d.x, d.y) == "floor") ? d : Space.nearestTile(state.grid, d, (x, y) => Grids.getTile(state.grid, x, y) == "floor");
            if (!Js.T(p) || !Grids.inBounds(state.grid, p.x, p.y))
            {
                continue;
            }
            ZombieKinds.giveVariant(Zombies.addZombie(state, p.x, p.y, Zombies.zombieHealthForNight(Time.dayOf(state.tick)), "static"), Time.dayOf(state.tick));
            woke += 1;
        }
        return woke;
    }
}
