// Space.nearestTile: the search behind "put them on the nearest tile they can stand on" (getting out of a car, a
// car parking on someone, coming down off a roof, an expedition coming home). It had no test of its own.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class SpaceTest
{
    static Grid grid(params string[] rows) => ScenarioMod.loadScenario(new Scenario { seed = 1, map = rows.ToList() }).grid;

    static string at(Point? p) => p == null ? "none" : $"{p.x},{p.y}";

    [Fact(DisplayName = "nearestTile > the nearest by steps, and of equals the first in north, east, south, west order")]
    public void NearestAndOrder()
    {
        var g = grid(".....", ".....", ".....", ".....", ".....");
        var from = new Point(2, 2);
        // Itself, if it will do.
        expect(at(Space.nearestTile(g, from, (x, y) => true))).toBe("2,2");
        // One step beats two.
        expect(at(Space.nearestTile(g, from, (x, y) => (x == 2 && y == 0) || (x == 3 && y == 2)))).toBe("3,2");
        // Two steps each way: north is looked at first, then east, south, west.
        expect(at(Space.nearestTile(g, from, (x, y) => (x == 2 && y == 0) || (x == 2 && y == 4)))).toBe("2,0");
        expect(at(Space.nearestTile(g, from, (x, y) => (x == 0 && y == 2) || (x == 4 && y == 2)))).toBe("4,2");
        expect(at(Space.nearestTile(g, from, (x, y) => (x == 0 && y == 2) || (x == 2 && y == 4)))).toBe("2,4");
    }

    [Fact(DisplayName = "nearestTile > goes through walkable tiles only, gives up at the limit, and may start in a wall")]
    public void WallsAndLimit()
    {
        // A wall across the row: what is beyond it is not found.
        var row = grid("..#..");
        expect(at(Space.nearestTile(row, new Point(0, 0), (x, y) => x == 4))).toBe("none");
        // Round the wall's end it is.
        var round = grid("..#..", ".....");
        expect(at(Space.nearestTile(round, new Point(0, 0), (x, y) => x == 4 && y == 0))).toBe("4,0");
        // Nothing wanted anywhere: none; and none within a limit too small to reach it.
        var open = grid(".......", ".......", ".......");
        expect(at(Space.nearestTile(open, new Point(0, 0), (x, y) => false))).toBe("none");
        expect(at(Space.nearestTile(open, new Point(0, 0), (x, y) => x == 6 && y == 2, 5))).toBe("none");
        expect(at(Space.nearestTile(open, new Point(0, 0), (x, y) => x == 6 && y == 2))).toBe("6,2");
        // From inside a wall: the search still sets out.
        var wall = grid(".#.", "...");
        expect(at(Space.nearestTile(wall, new Point(1, 0), (x, y) => Grids.isWalkable(wall, x, y)))).toBe("2,0");
    }

    [Fact(DisplayName = "nearestTile > a test that searches too gets its own answer and doesn't spoil the outer search")]
    public void Nested()
    {
        var g = grid(".......", ".......", ".......", ".......", ".......");
        // The nearest tile to (0, 0) whose own nearest tile in column 6 is on row 3 or below: (0, 3), three steps south.
        var found = Space.nearestTile(g, new Point(0, 0), (x, y) =>
        {
            var inner = Space.nearestTile(g, new Point(x, y), (ix, iy) => ix == 6);
            return inner != null && inner.y >= 3;
        });
        expect(at(found)).toBe("0,3");
    }
}
