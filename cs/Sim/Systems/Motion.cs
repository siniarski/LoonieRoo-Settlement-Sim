// Ported from src/sim/motion.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Motion: where movers are, finer than a tile. The world stays on the grid (walls, doors,
// furniture, rooms, paths); movers (survivors, zombies, dogs, animals, residents) also have a
// precise position, `at`, in SUB units per tile (fixed point: integers, so replays, saves and a
// future port stay exact; no floats that differ between engines).
//   at    the mover's centre now, in SUB units (a tile's centre is tile * SUB + HALF)
//   was   at as it was when this tick began, so the renderer can blend between the two
//   pos   the tile under `at` (most of the sim only asks which tile someone is on)
// Movers stepping tile to tile (a Movement in progress) get `at` from the step (syncStep).
// Free movers (moving under their own steering: control.ts, zombies.ts) own `at`, and their
// `pos` follows it; if other code moves them by tile (an eviction), `at` snaps to follow.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Motion
{

    public const int SUB = 256;

    public static readonly double HALF = ((double)(SUB) / (2));

    /// <summary>A mover's body: a circle this wide (radius), for walls and for pushing apart.</summary>
    public const int BODY_RADIUS = 77;

    /// <summary>A melee swing (yours, or a guard's) reaches this far, centre to centre: 1.25 tiles.</summary>
    public const int SWING_REACH = 320;

    /// <summary>A blow or a bite reaches this far, centre to centre: orthogonal neighbours' centres are 1 tile apart, diagonal ones 1.41.</summary>
    public const int REACH = 282;

    /// <summary>Within reach, centre to centre. Given the grid, not through a wall: an edge wall or shut door between their tiles (grid.ts) stops a blow or a bite (pathfinding.ts openBetween).</summary>
    public static bool inReach(IHasAt a, IHasAt b, double reach = REACH, Grid? grid = default)
    {
        return (distance2(a.at, b.at) <= ((double)(reach) * (reach))) && (!Js.T(grid) || Pathfinding.openBetween(grid, tileOf(a.at), tileOf(b.at)));
    }

    /// <summary>The nearest of `things` within `reach` of `from` and inside the arc in front (within 60 degrees of `facing` either way). Integer maths: dot &gt; 0 and 4 * dot^2 &gt;= |facing|^2 * |d|^2 (cos 60 = 1/2).</summary>
    public static T? nearestInArc<T>(List<T> things, Point from, Point facing, double reach = SWING_REACH, Grid? grid = default) where T : class, IHasAt
    {
        var f2 = ((double)(facing.x) * (facing.x)) + ((double)(facing.y) * (facing.y));
        T? best = null;
        var bestD2 = double.PositiveInfinity;
        foreach (var t in Js.Iter(things))
        {
            var dx = t.at.x - from.x;
            var dy = t.at.y - from.y;
            var d2 = ((double)(dx) * (dx)) + ((double)(dy) * (dy));
            if ((d2 > ((double)(reach) * (reach))) || (d2 >= bestD2))
            {
                continue;
            }
            var dot = ((double)(dx) * (facing.x)) + ((double)(dy) * (facing.y));
            if ((d2 > 0) && ((dot <= 0) || (((double)(((double)(4) * (dot))) * (dot)) < ((double)(f2) * (d2)))))
            {
                continue;
            }
            if (Js.T(grid) && !Pathfinding.openBetween(grid, tileOf(from), tileOf(t.at)))
            {
                continue;
            }
            best = t;
            bestD2 = d2;
        }
        return best;
    }

    public static Point centre(Point p)
    {
        return new Point { x = Js.I(((double)(p.x) * (SUB)) + HALF), y = Js.I(((double)(p.y) * (SUB)) + HALF) };
    }

    public static Point tileOf(Point at)
    {
        return new Point { x = Js.I(JsMath.floor(((double)(at.x) / (SUB)))), y = Js.I(JsMath.floor(((double)(at.y) / (SUB)))) };
    }

    /// <summary>A new mover's position fields, on a tile.</summary>
    public static PlacedAtResult placedAt(Point tile)
    {
        return new PlacedAtResult { at = centre(tile), was = centre(tile) };
    }

    /// <summary>Put a mover on a tile now (teleports: spawning, evictions).</summary>
    public static void place(Body e, Point tile)
    {
        e.pos = new Point { x = Js.I(tile.x), y = Js.I(tile.y) };
        e.move = null;
        e.at = centre(tile);
        e.was = centre(tile);
    }

    /// <summary>`at` from a tile step in progress (or the tile's centre when standing). Integer maths.</summary>
    public static void syncStep(Body e)
    {
        var m = e.move;
        if (!Js.T(m))
        {
            e.at = centre(e.pos);
            return;
        }
        var from = centre(m.from);
        var to = centre(m.to);
        e.at = new Point { x = Js.I(from.x + JsMath.trunc(((double)(((double)((to.x - from.x)) * (m.progress))) / (m.duration)))), y = Js.I(from.y + JsMath.trunc(((double)(((double)((to.y - from.y)) * (m.progress))) / (m.duration)))) };
    }

    /// <summary>For a free mover: if someone moved it by tile, follow. `slack` tiles of difference are allowed (a zombie pushed in a crowd can be a tile off until its next move catches its pos up).</summary>
    public static void followTile(Body e, double slack = 0)
    {
        var t = tileOf(e.at);
        if (JsMath.max(JsMath.abs(t.x - e.pos.x), JsMath.abs(t.y - e.pos.y)) > slack)
        {
            e.at = centre(e.pos);
        }
    }

    public static double distance2(Point a, Point b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return ((double)(dx) * (dx)) + ((double)(dy) * (dy));
    }

    /// <summary>Can a body of BODY_RADIUS stand with its centre here (every tile it overlaps passable)?</summary>
    public static bool fits(Grid grid, Point at, string mover, double r = BODY_RADIUS)
    {
        var x0 = JsMath.floor(((double)((at.x - r)) / (SUB)));
        var x1 = JsMath.floor(((double)((at.x + r)) / (SUB)));
        var y0 = JsMath.floor(((double)((at.y - r)) / (SUB)));
        var y1 = JsMath.floor(((double)((at.y + r)) / (SUB)));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if (!Grids.inBounds(grid, x, y) || !Pathfinding.isPassable(grid, x, y, mover))
                {
                    return false;
                }
            }
        }
        for (var y = y0; y <= (y1 + 1); y++)
        {
            for (var x = x0; x <= (x1 + 1); x++)
            {
                if (!Grids.inBounds(grid, x, y))
                {
                    continue;
                }
                if (((y > 0) && !Pathfinding.crossable(grid, x, y, x, y - 1, mover)) && touches(at, r, ((double)(x) * (SUB)), ((double)(y) * (SUB)), ((double)((x + 1)) * (SUB)), ((double)(y) * (SUB))))
                {
                    return false;
                }
                if (((x > 0) && !Pathfinding.crossable(grid, x, y, x - 1, y, mover)) && touches(at, r, ((double)(x) * (SUB)), ((double)(y) * (SUB)), ((double)(x) * (SUB)), ((double)((y + 1)) * (SUB))))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Does a circle at `c` of radius r reach an axis-aligned segment (x0, y0)-(x1, y1)? Integers.</summary>
    public static bool touches(Point c, double r, int x0, int y0, int x1, int y1)
    {
        var px = JsMath.max(x0, JsMath.min(x1, c.x));
        var py = JsMath.max(y0, JsMath.min(y1, c.y));
        return ((JsMath.pow((c.x - px), 2)) + (JsMath.pow((c.y - py), 2))) < ((double)(r) * (r));
    }
    public static bool touches(Point c, double r, double x0, double y0, double x1, double y1) => touches(c, r, Js.I(x0), Js.I(y0), Js.I(x1), Js.I(y1));

    /// <summary>Move a body by (dx, dy) SUB units, stopping at walls: each axis on its own, so it slides along a wall rather than sticking to it. Big moves go in steps no longer than a radius, so nothing tunnels through a wall. Returns the new centre.</summary>
    public static Point slide(Grid grid, Point at, int dx, int dy, string mover, double r = BODY_RADIUS)
    {
        double x = at.x;
        double y = at.y;
        var steps = JsMath.max(1, JsMath.ceil(((double)(JsMath.max(JsMath.abs(dx), JsMath.abs(dy))) / (r))));
        for (var i = 1; i <= steps; i++)
        {
            var sx = JsMath.trunc(((double)(((double)(dx) * (i))) / (steps))) - JsMath.trunc(((double)(((double)(dx) * ((i - 1)))) / (steps)));
            var sy = JsMath.trunc(((double)(((double)(dy) * (i))) / (steps))) - JsMath.trunc(((double)(((double)(dy) * ((i - 1)))) / (steps)));
            if ((sx != 0) && fits(grid, new Point { x = Js.I(x + sx), y = Js.I(y) }, mover, r))
            {
                x += sx;
            }
            if ((sy != 0) && fits(grid, new Point { x = Js.I(x), y = Js.I(y + sy) }, mover, r))
            {
                y += sy;
            }
        }
        return new Point { x = Js.I(x), y = Js.I(y) };
    }
    public static Point slide(Grid grid, Point at, double dx, double dy, string mover, double r = BODY_RADIUS) => slide(grid, at, Js.I(dx), Js.I(dy), mover, r);

    /// <summary>A vector of length `len` (SUB units) towards (dx, dy), in integers. (0, 0) stays (0, 0).</summary>
    public static Point toward(int dx, int dy, double len)
    {
        var d = JsMath.sqrt(((double)(dx) * (dx)) + ((double)(dy) * (dy)));
        if (d == 0)
        {
            return new Point { x = Js.I(0), y = Js.I(0) };
        }
        return new Point { x = Js.I(JsMath.round(((double)(((double)(dx) * (len))) / (d)))), y = Js.I(JsMath.round(((double)(((double)(dy) * (len))) / (d)))) };
    }
    public static Point toward(double dx, double dy, double len) => toward(Js.I(dx), Js.I(dy), len);

    /// <summary>Is a body in a broken window's frame (its circle on the line)? Climbing through is slow (control.ts).</summary>
    public static bool inWindowFrame(Grid grid, Point at, double r = BODY_RADIUS)
    {
        var x0 = JsMath.floor(((double)((at.x - r)) / (SUB)));
        var x1 = JsMath.floor(((double)((at.x + r)) / (SUB)));
        var y0 = JsMath.floor(((double)((at.y - r)) / (SUB)));
        var y1 = JsMath.floor(((double)((at.y + r)) / (SUB)));
        for (var y = y0; y <= (y1 + 1); y++)
        {
            for (var x = x0; x <= (x1 + 1); x++)
            {
                if (!Grids.inBounds(grid, x, y))
                {
                    continue;
                }
                var i = ((double)((((double)(y) * (grid.width)) + x)) * (2));
                if ((Js.Get(grid.edges, i) == Grids.EDGE_WINDOW_BROKEN) && touches(at, r, ((double)(x) * (SUB)), ((double)(y) * (SUB)), ((double)((x + 1)) * (SUB)), ((double)(y) * (SUB))))
                {
                    return true;
                }
                if ((Js.Get(grid.edges, i + 1) == Grids.EDGE_WINDOW_BROKEN) && touches(at, r, ((double)(x) * (SUB)), ((double)(y) * (SUB)), ((double)(x) * (SUB)), ((double)((y + 1)) * (SUB))))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
