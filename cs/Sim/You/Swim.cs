// Swim (2026-11-11): you in the water. Water is a tile kind nobody else stands on (Pathfinding.isPassable refuses
// it for every mover: colonists, the dead, animals and dogs keep to the land, so paths, hordes and reachability are
// as they were). You, the controlled survivor, walk into it: your body moves as the mover "swimmer" (Pathfinding:
// the free mover's rules, and water), and your tile (pos) follows you into it.
// How deep, by the distance from the bank (tiles only: nothing in the tick reads the ground's heights):
//   shallow  a water tile with land among its eight neighbours (land: on the map, not water, not a bridge's deck):
//            you wade, WADE_PCT of your pace; running, swinging, using and working as on land.
//   deep     further out: you swim, SWIM_PCT of your pace, no running, SWIM_COST endurance a tick paddling (as
//            running costs), TREAD_EVERY ticks a point treading water, no endurance back. Winded you struggle:
//            STRUGGLE_PCT of your pace, and DROWN_DAMAGE health every DROWN_EVERY ticks until you reach the shallows.
//            Swinging, shooting, reloading, using and working are refused (Commands.cs: "swimming").
//   Space    in deep water, a stroke (a short busy action like a leap, Leaps.cs: busy "stroke"): STROKE_TICKS that
//            carry you STROKE_LENGTH the way you steer, else the way you look, for STROKE_COST. Further and much
//            cheaper than paddling: a swimmer crosses a wide river on strokes, a paddler arrives winded.
// The water soaks you (wetness to the top at once in deep water, up to WADE_WET wading) and chills you (body heat
// falls in the water whatever the weather, faster in deep water and in the cold): soak, from BodyMod.updateBody,
// on slow ticks like the rain's. What you carry stays as it is (nothing in the sim gets wet but bodies).
// The Heights' river bed (shore corners just under the surface, deepening away from the bank) agrees with this:
// a tile touching the bank is under half a metre, the next is over that. The renderer draws your eye from it.
// Letting go of control in the water puts you on the nearest land (ashore): colonists don't swim.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Swim
{
    public const int DRY = 0, SHALLOW = 1, DEEP = 2;

    /// <summary>Your pace (percent of walking) wading, swimming, and swimming winded.</summary>
    public const int WADE_PCT = 60, SWIM_PCT = 40, STRUGGLE_PCT = 20;

    /// <summary>Endurance paddling, a tick (running's cost); treading water, a point every TREAD_EVERY ticks.</summary>
    public const int SWIM_COST = 1, TREAD_EVERY = 4;

    /// <summary>Winded in deep water: this much health (of Health.HEALTH_MAX) every DROWN_EVERY ticks.</summary>
    public const int DROWN_DAMAGE = 30, DROWN_EVERY = 10;

    /// <summary>A stroke: this many ticks, this far (SUB: a tile and a quarter), this much endurance.</summary>
    public const int STROKE_TICKS = 10, STROKE_LENGTH = 320, STROKE_COST = 2;

    /// <summary>Wading soaks you this far (BodyMod: over SOAKED, under DRENCHED), WADE_SOAK a slow tick.</summary>
    public const int WADE_WET = 55, WADE_SOAK = 3;

    /// <summary>Body heat falls a point every this many slow ticks in deep water, and wading.</summary>
    public const int DEEP_CHILL_EVERY = 2, WADE_CHILL_EVERY = 4;

    public const string DROWNING = "drowning";

    /// <summary>How deep the water is at a tile: DRY (not water, or off the map), SHALLOW (land beside it), DEEP.</summary>
    public static int depth(Grid grid, int x, int y)
    {
        if (!Grids.inBounds(grid, x, y) || Grids.getTile(grid, x, y) != Grids.WATER) return DRY;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && land(grid, x + dx, y + dy)) return SHALLOW;
        return DEEP;
    }

    public static int depth(Grid grid, Point p) => depth(grid, p.x, p.y);

    /// <summary>Ground you could climb out onto: on the map, not water, not a bridge's deck (it stands over the water).</summary>
    static bool land(Grid grid, int x, int y)
    {
        if (!Grids.inBounds(grid, x, y) || Grids.getTile(grid, x, y) == Grids.WATER) return false;
        int g = Grids.groundAt(grid, x, y);
        return g >= Grids.GROUNDS.Length || Grids.GROUNDS[g] != "bridge";
    }

    public static bool inWater(SimState state, Survivor s) => s != null && depth(state.grid, s.pos) != DRY;

    /// <summary>Out of your depth: swimming, not wading.</summary>
    public static bool swimming(SimState state, Survivor s) => s != null && depth(state.grid, s.pos) == DEEP;

    /// <summary>Your pace in the water, percent of walking (100 on land).</summary>
    public static int pacePct(SimState state, Survivor s)
    {
        int d = depth(state.grid, s.pos);
        if (d == DRY) return 100;
        if (d == SHALLOW) return WADE_PCT;
        return BodyMod.isWinded(s) ? STRUGGLE_PCT : SWIM_PCT;
    }

    /// <summary>Once a tick for the controlled survivor in deep water: paddling and treading cost endurance; winded, the water does harm.</summary>
    public static void tick(SimState state, Survivor s, Control c)
    {
        if (!swimming(state, s)) return;
        bool paddling = (c.dx != 0 || c.dy != 0) && !Leaps.airborne(c);
        if (paddling) BodyMod.exert(s, SWIM_COST);
        else if (state.tick % TREAD_EVERY == 0) BodyMod.exert(s, 1);
        if (BodyMod.isWinded(s))
        {
            s.note = "struggling in the water";
            if (state.tick % DROWN_EVERY == 0)
            {
                s.health = JsMath.max(0, s.health - DROWN_DAMAGE);
                s.lastHurtBy = DROWNING;
            }
        }
    }

    /// <summary>Space in deep water: a stroke the way you steer, else the way you look. Returns null, or why not.</summary>
    public static string? stroke(SimState state, Survivor s, Control c)
    {
        if (BodyMod.isWinded(s)) return "too winded to swim a stroke";
        bool steering = c.dx != 0 || c.dy != 0;
        var d = steering ? new Point { x = Js.I(c.dx), y = Js.I(c.dy) } : ControlMod.swingDir(c);
        var v = Motion.toward(d.x, d.y, STROKE_LENGTH);
        BodyMod.exert(s, STROKE_COST);
        c.busy = new ControlBusy { what = "stroke", targetId = $"{s.at.x + v.x},{s.at.y + v.y}", progress = 0, duration = STROKE_TICKS };
        s.note = "swam a stroke";
        return null;
    }

    /// <summary>On slow ticks, for anyone in the water (BodyMod.updateBody): soaked and chilled. True if in water (no drying, no warming).</summary>
    public static bool soak(SimState state, Survivor s)
    {
        int d = depth(state.grid, s.pos);
        if (d == DRY) return false;
        if (d == DEEP) s.wetness = BodyMod.BODY_MAX;
        else if (s.wetness < WADE_WET) s.wetness = JsMath.min(WADE_WET, s.wetness + WADE_SOAK);
        if (state.tick % (d == DEEP ? DEEP_CHILL_EVERY : WADE_CHILL_EVERY) == 0) s.bodyHeat = BodyMod.clamp(s.bodyHeat - 1);
        return true;
    }

    /// <summary>Control is ending: whoever you were, if in the water, is put on the nearest land (colonists don't swim).</summary>
    public static void ashore(SimState state)
    {
        var s = ControlMod.controlled(state);
        if (s == null || !inWater(state, s) || Vehicles.vehicleOf(state, s) != null) return;
        var to = nearestLand(state.grid, s.pos, ASHORE_REACH);
        if (to != null) Motion.place(s, to);
    }

    /// <summary>How far (tiles, a square round you) ashore looks for land.</summary>
    public const int ASHORE_REACH = 64;

    /// <summary>The walkable tile nearest `from` (straight-line, over the water: a ring at a time, the first of the nearest in row order), or null within `reach`.</summary>
    public static Point? nearestLand(Grid grid, Point from, int reach)
    {
        for (int r = 1; r <= reach; r++)
        {
            Point best = null;
            double bestD2 = double.PositiveInfinity;
            for (int y = from.y - r; y <= from.y + r; y++)
                for (int x = from.x - r; x <= from.x + r; x++)
                {
                    if (JsMath.max(JsMath.abs(x - from.x), JsMath.abs(y - from.y)) != r || !Grids.isWalkable(grid, x, y)) continue;
                    double d2 = (double)(x - from.x) * (x - from.x) + (double)(y - from.y) * (y - from.y);
                    if (d2 < bestD2) { bestD2 = d2; best = new Point { x = x, y = y }; }
                }
            if (best != null) return best;
        }
        return null;
    }
}
