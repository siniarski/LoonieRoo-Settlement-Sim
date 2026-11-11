// Trunks: a tree (furniture, kind "tree") is solid where its trunk stands, a circle in its tile, to movers
// with a body: you and the dead (free movers), colonists and survivors on tile paths, and cars. The trunk is
// nudged off the tile's centre by a hash of the tile (no RNG, stable across saves); the game draws the tree
// there (godot/World3D/Trees.cs reads trunkAt), so what you bump is what you see. Integer maths in SUB units (rule 10).
//   pushOut   a body overlapping a trunk is moved out along the line from the trunk's centre, so a
//             body walking into one at an angle slides round it
//               free movers   Motion.slide calls it each step (walls first, then trunks)
//               tile walkers  Motion.syncStep calls it on the step's straight line each tick: `at` goes round
//                             the trunk while the step's time, `pos` and the path stay the tile's (tree tiles still
//                             pass in pathfinding), so jobs, arrival times and the tile logic are untouched; a
//                             moved-out spot that wouldn't fit (a wall, a shut door) is not used: the line stands
//               cars          Vehicles.move uses the slide above with the car's radius
// Trees are looked up by tile (FurnitureMod.furnitureAt), only the tiles the body could touch (rule 14).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Trunks
{
    /// <summary>A trunk's radius, SUB units: 0.2 tile (the models' trunks are 0.19-0.23 m in radius at the foot, times a size of 0.7-1.2).</summary>
    public const int RADIUS = 51;

    /// <summary>A trunk's centre is at most this far (SUB units, each axis) off its tile's centre: 0.12 tile.</summary>
    public const int JITTER = 31;

    /// <summary>A hash of a tile and a salt (the trees' drawing uses it for species, size, turn and tint too).</summary>
    public static uint hash(int x, int y, uint salt)
    {
        uint h = unchecked((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ salt * 0xcb1ab31fu);
        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15; h = unchecked(h * 0x27d4eb2du); h ^= h >> 16;
        return h;
    }

    /// <summary>The offset of a tree's trunk from its tile's centre along one axis (salt 4: x, 5: y), in [-JITTER, JITTER].</summary>
    public static int offset(int x, int y, uint salt) => (int)(((long)(hash(x, y, salt) & 0xFFFFFF) * (2 * JITTER + 1)) >> 24) - JITTER;

    /// <summary>The centre of the trunk of a tree on tile (x, y), SUB units.</summary>
    public static Point trunkAt(int x, int y) => new Point { x = x * Motion.SUB + Motion.SUB / 2 + offset(x, y, 4), y = y * Motion.SUB + Motion.SUB / 2 + offset(x, y, 5) };

    /// <summary>Is there a tree on this tile?</summary>
    public static bool treeAt(SimState state, int x, int y) => FurnitureMod.furnitureAt(state, x, y)?.kind == "tree";

    /// <summary>A body of radius r at `at`, moved out of any trunk it overlaps. `dx, dy` is the move that brought it there: head on into a trunk's centre (no side to slide to) it turns left, so nothing sticks dead on.</summary>
    public static Point pushOut(SimState state, Point at, double r, int dx = 0, int dy = 0)
    {
        var reach = r + RADIUS;
        double x = at.x, y = at.y;
        var x0 = (int)JsMath.floor((x - reach) / Motion.SUB);
        var x1 = (int)JsMath.floor((x + reach) / Motion.SUB);
        var y0 = (int)JsMath.floor((y - reach) / Motion.SUB);
        var y1 = (int)JsMath.floor((y + reach) / Motion.SUB);
        for (var ty = y0; ty <= y1; ty++)
        {
            for (var tx = x0; tx <= x1; tx++)
            {
                if (!treeAt(state, tx, ty)) continue;
                var c = trunkAt(tx, ty);
                double ox = x - c.x, oy = y - c.y;
                if (ox * ox + oy * oy >= reach * reach) continue;
                // Dead on (the offset along the move): slide to the left of the move instead.
                if ((ox * dy - oy * dx == 0) && (dx != 0 || dy != 0)) { ox += dy; oy -= dx; }
                if (ox == 0 && oy == 0) oy = -1;
                var v = Motion.toward(Js.I(ox), Js.I(oy), reach);
                x = c.x + v.x;
                y = c.y + v.y;
            }
        }
        return new Point { x = Js.I(x), y = Js.I(y) };
    }

    /// <summary>Is a body at `at` against the trunk on this tile (within `slack` of touching)? For path followers whose waypoint is a tree's tile: that's as near as they get.</summary>
    public static bool against(SimState state, Point at, Point tile, double r, double slack)
    {
        if (!treeAt(state, tile.x, tile.y)) return false;
        var reach = r + RADIUS + slack;
        return Motion.distance2(at, trunkAt(tile.x, tile.y)) <= reach * reach;
    }
}
