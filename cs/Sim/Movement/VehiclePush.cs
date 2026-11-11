// Pushing a car (part of Vehicles; 2026-11-12): on foot against a parked car nobody is in, the push key held
// (the `push` command: J in the game) rolls it the way you walk.
//
//   which way   along its length only, as its wheels roll: you must be walking into it, within a turn of 60
//               degrees of its length (from behind or in front); pushing its side does nothing.
//   how fast    PUSH_STEP a tick (about a metre a second), you walking behind it at its pace; PUSH_COST endurance a
//               tick, as running costs; winded, it doesn't move.
//   stopped     by what stops a driven car (advance: walls, doors, windows, barricades, furniture, trunks, other
//               cars), as much of the step as fits. Not by the dead or animals: whoever is on its new tiles is
//               moved off them, as when a car is parked (not you: you are behind it).
//   its tiles   moved with it (unmarked, moved, marked): the walkability bump only on a tick its tile set changes.
//   slope       not felt: the ground's height (Heights.cs) is read by nothing in the tick yet (the player's call), so
//               a car pushes the same up a hill as down it.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>How far a push rolls a car in a tick (SUB): about a metre a second at ten ticks a second.</summary>
    public const int PUSH_STEP = 25;

    /// <summary>Endurance a tick of pushing (as running).</summary>
    public const int PUSH_COST = BodyMod.RUN_TICK_COST;

    /// <summary>You push a car whose body is this close to your centre (SUB): at its ends the tiles under it stop
    /// you up to half a tile short of the body.</summary>
    public const int PUSH_REACH = Motion.BODY_RADIUS + Motion.SUB / 2 + 16;

    /// <summary>Push the parked car you are walking into, along its length. Returns how far it rolled (SUB), or null
    /// if there was nothing to push (no car, not into it, not along it, winded, or something in its way).</summary>
    public static Point? push(SimState state, Survivor s, int dx, int dy)
    {
        if ((dx == 0 && dy == 0) || BodyMod.isWinded(s)) return null;
        var v = vehicleNear(state, s.at, PUSH_REACH);
        if (v == null || !isParked(v)) return null;
        // Walking into it: its middle is ahead of you.
        if ((double)(v.at.x - s.at.x) * dx + (double)(v.at.y - s.at.y) * dy <= 0) return null;
        var (hx, hy) = Js.NN(Js.Get(HEADING_DIRS, Js.I(v.heading)));
        double dot = (double)dx * hx + (double)dy * hy;
        double len = JsMath.sqrt((double)dx * dx + (double)dy * dy) * 256;
        if (JsMath.abs(dot) * 2 < len) return null;  // more across it than along it
        double sign = JsMath.sign(dot);
        int mx = Js.I(JsMath.round(sign * hx * PUSH_STEP / 256)), my = Js.I(JsMath.round(sign * hy * PUSH_STEP / 256));
        var before = tilesUnder(state.grid, v);
        unmark(state, v);
        var to = advance(state, v, mx, my);
        Point rolled = new Point { x = Js.I(to.x - v.at.x), y = Js.I(to.y - v.at.y) };
        v.at = to;
        v.pos = Motion.tileOf(to);
        mark(state.grid, v);
        var after = tilesUnder(state.grid, v);
        if (!sameTiles(before, after))
        {
            state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
            evict(state, v, s);
        }
        if (rolled.x == 0 && rolled.y == 0) return null;
        BodyMod.exert(s, PUSH_COST);
        return rolled;
    }

    static bool sameTiles(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
