// The grid functions taking JavaScript numbers (doubles), as the translated sim calls them with values from
// Math.floor and the like. A fractional coordinate behaves as in the TypeScript: inBounds is false for it
// (Number.isInteger), so it's off the map; whole ones go to the int versions.
namespace LoonieRoo.Sim;

public static partial class Grids
{
    static bool Whole(double v) => Math.Floor(v) == v && !double.IsInfinity(v);

    public static bool inBounds(Grid grid, double x, double y) => Whole(x) && Whole(y) && inBounds(grid, (int)x, (int)y);
    public static string? getTile(Grid grid, double x, double y) => inBounds(grid, x, y) ? getTile(grid, (int)x, (int)y) : null;
    public static bool setTile(Grid grid, double x, double y, string tile) => inBounds(grid, x, y) && setTile(grid, (int)x, (int)y, tile);
    public static bool isWalkable(Grid grid, double x, double y) => inBounds(grid, x, y) && isWalkable(grid, (int)x, (int)y);
    public static bool isOpen(Grid grid, double x, double y) => inBounds(grid, x, y) && isOpen(grid, (int)x, (int)y);
    public static bool isLocked(Grid grid, double x, double y) => inBounds(grid, x, y) && isLocked(grid, (int)x, (int)y);
    public static bool isDirty(Grid grid, double x, double y) => inBounds(grid, x, y) && isDirty(grid, (int)x, (int)y);
    public static bool setDirty(Grid grid, double x, double y, bool dirty) => inBounds(grid, x, y) && setDirty(grid, (int)x, (int)y, dirty);
    public static int tileIndex(Grid grid, double x, double y) => (int)(y * grid.width + x);
    public static int edgeIndex(Grid grid, double x, double y, string side) => edgeIndex(grid, (int)x, (int)y, side);
    public static int edgeBetween(Grid grid, double ax, double ay, double bx, double by) => inBounds(grid, ax, ay) && inBounds(grid, bx, by) ? edgeBetween(grid, (int)ax, (int)ay, (int)bx, (int)by) : -1;
    public static bool setEdge(Grid grid, double x, double y, string side, int kind) => inBounds(grid, x, y) && setEdge(grid, (int)x, (int)y, side, kind);
    public static List<Point> neighbours4(Grid grid, double x, double y) => neighbours4(grid, (int)x, (int)y);
    public static void noteChange(Grid grid, double i) => noteChange(grid, (int)i);
    public static void setDamage(Grid grid, double i, double value) => setDamage(grid, (int)i, value);
}

public static partial class Pathfinding
{
    public static bool isPassable(Grid grid, double x, double y, string mover = SURVIVOR) => Grids.inBounds(grid, x, y) ? isPassable(grid, (int)x, (int)y, mover) : mover != BREACHER && isPassable(grid, -1, -1, mover);
    public static bool crossable(Grid grid, double ax, double ay, double bx, double by, string mover = SURVIVOR) => crossable(grid, (int)ax, (int)ay, (int)bx, (int)by, mover);
}
