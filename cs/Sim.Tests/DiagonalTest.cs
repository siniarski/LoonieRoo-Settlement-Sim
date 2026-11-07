// Ported from tests/diagonal.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class DiagonalTest
{

    /// <summary>Ticks for the survivor to walk to (x, y), and every tile they stood on.</summary>
    public static WalkToResult walkTo(List<string> map, int x, int y)
    {
        SimState state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        var s = Js.NN(Js.Get(state.survivors, 0));
        foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
        {
            s.priorities[Js.Idx(k)] = 0;
        }
        Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(x), y = Js.I(y) });
        List<string> tiles = [];
        double ticks = 0;
        while ((Js.T(((s.pos.x != x) || (s.pos.y != y)) || Js.T(s.move))) && (ticks < 400))
        {
            Tick.tick(state);
            ticks++;
            tiles.push($"{Js.Str(s.pos.x)},{Js.Str(s.pos.y)}");
        }
        return new WalkToResult { ticks = ticks, tiles = tiles };
    }
    public static WalkToResult walkTo(List<string> map, double x, double y) => walkTo(map, Js.I(x), Js.I(y));

    /// <summary>colonists walk diagonally</summary>
    public class D001_colonists_walk_diagonally
    {

        [Fact(DisplayName = "colonists walk diagonally > across open ground they cut corners: a diagonal is quicker than going round")]
        public void T002_across_open_ground_they_cut_corners_a_diagonal_is_quicker_th()
        {
            var open = walkTo(["s....", ".....", ".....", ".....", "....."], 4, 4);
            var diag = ((double)(4) * (JsMath.round(((double)(((double)(4) * (1414))) / (1000)))));
            expect(open.ticks).toBeLessThan(((double)(8) * (4)));
            expect(open.ticks).toBeLessThanOrEqual(diag + 2);
        }

        [Fact(DisplayName = "colonists walk diagonally > never cut a wall's corner")]
        public void T003_never_cut_a_wall_s_corner()
        {
            var __d0 = walkTo(["s....", ".#...", "....."], 2, 2);
            var tiles = __d0.tiles;
            foreach (var t in Js.Iter(tiles))
            {
                expect(t).not.toBe("1,1");
            }
            var steps = tiles.filter((t, i) => t != Js.Get(tiles, i - 1));
            for (var i = 1; i < steps.length; i++)
            {
                var (ax, ay) = Js.NN(Js.Get(steps, i - 1)).split(",").map((__v) => Js.Num(__v));
                var (bx, by) = Js.NN(Js.Get(steps, i)).split(",").map((__v) => Js.Num(__v));
                var past = ((((ax == 0) && (ay == 1)) && (bx == 1)) && (by == 2)) || ((((ax == 1) && (ay == 0)) && (bx == 2)) && (by == 1));
                expect(past).toBe(false);
            }
        }
    }
}
