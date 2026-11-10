// Ported from tests/grid.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class GridTest
{

    /// <summary>grid</summary>
    public class D001_grid
    {

        [Fact(DisplayName = "grid > creates a grid filled with floor")]
        public void T002_creates_a_grid_filled_with_floor()
        {
            var grid = Grids.createGrid(3, 2);
            expect(Grids.allTiles(grid)).toEqual(Js.Arr(["floor", "floor", "floor", "floor", "floor", "floor"]));
        }

        [Fact(DisplayName = "grid > rejects invalid sizes")]
        public void T003_rejects_invalid_sizes()
        {
            expect(() => Grids.createGrid(0, 5)).toThrow();
            expect(() => Grids.createGrid(2.5, 5)).toThrow();
        }

        [Fact(DisplayName = "grid > checks bounds, including non-integers")]
        public void T004_checks_bounds_including_non_integers()
        {
            var grid = Grids.createGrid(4, 3);
            expect(Grids.inBounds(grid, 0, 0)).toBe(true);
            expect(Grids.inBounds(grid, 3, 2)).toBe(true);
            expect(Grids.inBounds(grid, 4, 0)).toBe(false);
            expect(Grids.inBounds(grid, -1, 0)).toBe(false);
            expect(Grids.inBounds(grid, 1.5, 0)).toBe(false);
        }

        [Fact(DisplayName = "grid > gets and sets tiles row-major")]
        public void T005_gets_and_sets_tiles_row_major()
        {
            var grid = Grids.createGrid(4, 3);
            expect(Grids.setTile(grid, 2, 1, "wall")).toBe(true);
            expect(Grids.getTile(grid, 2, 1)).toBe("wall");
            expect((object?)(Grids.tileAt(grid, ((double)(1) * (4)) + 2))).toBe("wall");
            expect(Grids.getTile(grid, 9, 9)).toBeUndefined();
        }

        [Fact(DisplayName = "grid > reports whether setTile changed anything")]
        public void T006_reports_whether_setTile_changed_anything()
        {
            var grid = Grids.createGrid(2, 2);
            expect(Grids.setTile(grid, 0, 0, "floor")).toBe(false);
            expect(Grids.setTile(grid, 0, 0, "door")).toBe(true);
            expect(Grids.setTile(grid, 0, 0, "door")).toBe(false);
            expect(Grids.setTile(grid, 5, 5, "wall")).toBe(false);
        }

        [Fact(DisplayName = "grid > treats floor and doors as walkable, walls and out-of-bounds as not")]
        public void T007_treats_floor_and_doors_as_walkable_walls_and_out_of_bounds_a()
        {
            var grid = Grids.createGrid(3, 1);
            Grids.setTile(grid, 1, 0, "wall");
            Grids.setTile(grid, 2, 0, "door");
            expect(Grids.isWalkable(grid, 0, 0)).toBe(true);
            expect(Grids.isWalkable(grid, 1, 0)).toBe(false);
            expect(Grids.isWalkable(grid, 2, 0)).toBe(true);
            expect(Grids.isWalkable(grid, 3, 0)).toBe(false);
        }

        [Fact(DisplayName = "grid > lists in-bounds neighbours in N, E, S, W order")]
        public void T008_lists_in_bounds_neighbours_in_N_E_S_W_order()
        {
            var grid = Grids.createGrid(3, 3);
            expect(Grids.neighbours4(grid, 1, 1)).toEqual(Js.Arr([Js.Obj(("x", (object?)(1)), ("y", (object?)(0))), Js.Obj(("x", (object?)(2)), ("y", (object?)(1))), Js.Obj(("x", (object?)(1)), ("y", (object?)(2))), Js.Obj(("x", (object?)(0)), ("y", (object?)(1)))]));
            expect(Grids.neighbours4(grid, 0, 0)).toEqual(Js.Arr([Js.Obj(("x", (object?)(1)), ("y", (object?)(0))), Js.Obj(("x", (object?)(0)), ("y", (object?)(1)))]));
        }
    }
}
