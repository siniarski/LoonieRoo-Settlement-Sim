// Furniture is in your way on foot (2026-11-12; the player walked through dressers). Each solid piece is a box in
// the middle of its tile, HALF to a side, and your body (a circle) stops at it and slides along it.
//   who       only you, under control (ControlMod's walking and Leaps' jump). Colonists, the dead and animals walk
//             tile paths and chases that were made with furniture open, and rooms are furnished on that footing: a
//             chair in a corridor must not seal it for them. For you there is always the way you came.
//   what      everything but what you get into or walk over: beds, cots, sofas and baths (E puts you on their
//             tile), traps and pits (you step on them), and trees (their trunks are Trunks.cs's).
//   never stuck  a step that takes you no deeper into a box is always allowed, so standing in one (a save from
//             before this, a piece built where you stand, getting out of bed beside a nightstand) you walk out.
// Integers in doubles; no state, no rolls. Cost: the tiles your body's box touches, a lookup each (rule 14).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class FurnitureBody
{
    /// <summary>Half the side of a piece's box (SUB): 0.75 m across, so you pass beside it within its own tile's
    /// neighbours but not through it.</summary>
    public const int HALF = 96;

    static readonly JsSet<string> OPEN_KINDS = new(["tree", "bed", "trap", "pit", "lure"]);
    static readonly JsSet<string> OPEN_LOOKS = new(["bed", "cot", "sofa", "bath", "armchair", "bench", "pew"]);

    /// <summary>Does this piece stop you? Not what you lie or sit in, step on, or a tree (Trunks.cs).</summary>
    public static bool solid(Furniture f) => f != null && !OPEN_KINDS.has(f.kind) && !(f.kind == "decor" && f.look != null && OPEN_LOOKS.has(f.look));

    /// <summary>How far (squared, SUB) a circle's centre is from the nearest point of the box on a tile; 0 inside it.</summary>
    static double apart2(Point at, int tx, int ty)
    {
        double cx = (double)tx * Motion.SUB + Motion.SUB / 2, cy = (double)ty * Motion.SUB + Motion.SUB / 2;
        double dx = JsMath.max(0, JsMath.abs(at.x - cx) - HALF), dy = JsMath.max(0, JsMath.abs(at.y - cy) - HALF);
        return dx * dx + dy * dy;
    }

    /// <summary>The solid piece a body of radius r at `at` is in, with how deep (SUB; the deepest), or null.</summary>
    public static (Furniture f, double depth)? inside(SimState state, Point at, int r)
    {
        int x0 = (int)JsMath.floor((double)(at.x - r) / Motion.SUB), x1 = (int)JsMath.floor((double)(at.x + r) / Motion.SUB);
        int y0 = (int)JsMath.floor((double)(at.y - r) / Motion.SUB), y1 = (int)JsMath.floor((double)(at.y + r) / Motion.SUB);
        (Furniture f, double depth)? worst = null;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var f = FurnitureMod.furnitureAt(state, x, y);
                if (!solid(f)) continue;
                double d2 = apart2(at, x, y);
                if (d2 >= (double)r * r) continue;
                double depth = r - JsMath.sqrt(d2);
                if (worst == null || depth > worst.Value.depth) worst = (f, depth);
            }
        }
        return worst;
    }

    /// <summary>Where a step from `from` to `to` ends with furniture in the way: `to` if it is clear of every piece
    /// or no deeper into one than `from` was; else the step along one axis that is; else `from`.</summary>
    public static Point stop(SimState state, Point from, Point to, int r)
    {
        if (state.furniture.length == 0) return to;
        double was = inside(state, from, r)?.depth ?? 0;
        bool fine(Point p) => (inside(state, p, r)?.depth ?? 0) <= was;
        if (fine(to)) return to;
        var alongX = new Point { x = to.x, y = from.y };
        var alongY = new Point { x = from.x, y = to.y };
        // The longer part of the step first, so you slide the way you were mostly going.
        bool xFirst = JsMath.abs(to.x - from.x) >= JsMath.abs(to.y - from.y);
        var first = xFirst ? alongX : alongY;
        var second = xFirst ? alongY : alongX;
        if (fine(first)) return first;
        if (fine(second)) return second;
        return from;
    }
}
