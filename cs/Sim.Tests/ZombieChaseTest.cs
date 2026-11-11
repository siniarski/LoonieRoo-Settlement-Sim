// Zombie chasing (Zombies.cs chase): with a clear, walkable line to you a zombie comes straight at
// where you are, not tile centre to tile centre; with something in the way it follows its path,
// smoothed (it heads for the furthest waypoint it can walk straight to).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ZombieChaseTest
{
    /// <summary>You (controlled, standing still) at a tile, and a static zombie already after you at another. Returns the state, you and it.</summary>
    static (SimState, Survivor, Zombie) chase(List<string> map, Point you, Point zombie)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map });
        Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = you.x, y = you.y });
        var s = Js.NN(Js.Get(state.survivors, 0));
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
        Commands.applyCommand(state, new Command { type = "spawnZombie", x = zombie.x, y = zombie.y });
        var z = Js.NN(Js.Get(state.zombies, 0));
        z.kind = "static";
        z.targetId = s.id;
        return (state, s, z);
    }

    /// <summary>Run until the zombie is within reach of you (or `max` ticks), recording where it was each tick.</summary>
    static List<Point> track(SimState state, Survivor s, Zombie z, int max)
    {
        List<Point> at = [z.at];
        for (var t = 0; t < max && !Motion.inReach(s, z, Motion.REACH, state.grid); t++)
        {
            Tick.tick(state);
            at.push(z.at);
            expect(Motion.fits(state.grid, z.at, "zombie"), DebugText.dumpText(state)).toBe(true);
        }
        expect(Motion.inReach(s, z, Motion.REACH, state.grid), DebugText.dumpText(state)).toBe(true);
        return at;
    }

    static double length(List<Point> at)
    {
        double sum = 0;
        for (var i = 1; i < at.length; i++) sum += JsMath.sqrt(Motion.distance2(at[i - 1], at[i]));
        return sum;
    }

    static List<string> open(int w, int h) => Js.ArrayFrom(h, (int _i) => ".".repeat(w));

    public class D001_zombie_chase
    {
        [Fact(DisplayName = "zombie chase > in the open, a zombie comes straight at you at any angle, not along the grid")]
        public void T001_straight_in_the_open()
        {
            var (state, s, z) = chase(open(20, 10), new Point(16, 7), new Point(2, 2));
            var start = z.at;
            var at = track(state, s, z, 600);
            // Every point on its track is near the line from where it started to you.
            var lx = s.at.x - start.x;
            var ly = s.at.y - start.y;
            var len = JsMath.sqrt(((double)lx * lx) + ((double)ly * ly));
            double worst = 0;
            foreach (var p in at)
            {
                var cross = JsMath.abs(((double)(p.x - start.x) * ly) - ((double)(p.y - start.y) * lx));
                worst = JsMath.max(worst, cross / len);
            }
            expect(worst).toBeLessThan(Motion.SUB / 8);
            // And it didn't walk further than the straight line.
            expect(length(at)).toBeLessThan(len);
        }

        [Fact(DisplayName = "zombie chase > a wall between: it goes round the end, cutting the corner, not tile by tile")]
        public void T002_round_a_wall()
        {
            // A wall from the top down to row 8 at x = 10; you behind it, the zombie on the other side.
            var map = open(24, 14);
            for (var y = 0; y <= 8; y++) map[y] = map[y].Substring(0, 10) + "#" + map[y].Substring(11);
            var (state, s, z) = chase(map, new Point(18, 2), new Point(3, 3));
            var start = z.at;
            var at = track(state, s, z, 900);
            // The shortest way round: to the wall's end (its corner at x 10..11, y 9) and on to you.
            var corner = new Point(11 * Motion.SUB, 9 * Motion.SUB);
            var shortest = JsMath.sqrt(Motion.distance2(start, corner)) + JsMath.sqrt(Motion.distance2(corner, s.at)) - Motion.REACH;
            expect(length(at)).toBeLessThan(shortest * 1.12);
            // It never went through the wall.
            expect(at.every((p) => Motion.tileOf(p).x != 10 || Motion.tileOf(p).y > 8)).toBe(true);
        }

        [Fact(DisplayName = "zombie chase > a window between: it doesn't come straight through the glass, it batters or goes round")]
        public void T003_window_blocks_the_straight_line()
        {
            // You inside a hut with a window on its west side, the zombie outside to the west.
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = open(16, 9) });
            for (var y = 2; y <= 6; y++)
            {
                for (var x = 8; x <= 12; x++)
                {
                    if (x == 8 || x == 12 || y == 2 || y == 6) Grids.setTile(state.grid, x, y, "wall");
                }
            }
            Grids.setTile(state.grid, 8, 4, "floor");
            Grids.setEdge(state.grid, 9, 4, "w", Grids.EDGE_WINDOW);
            var pane = Grids.edgeBetween(state.grid, 8, 4, 9, 4);
            state.walkabilityVersion += 1;
            Commands.applyCommand(state, new Command { type = "spawnSurvivor", x = 10, y = 4 });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "spawnZombie", x = 2, y = 4 });
            var z = Js.NN(Js.Get(state.zombies, 0));
            z.kind = "static";
            z.targetId = s.id;
            for (var t = 0; t < 200 && Grids.edgeKind(state.grid, pane) == Grids.EDGE_WINDOW; t++)
            {
                Tick.tick(state);
                if (Grids.edgeKind(state.grid, pane) == Grids.EDGE_WINDOW) expect(Motion.tileOf(z.at).x, DebugText.dumpText(state)).toBeLessThanOrEqual(8);
            }
            // It got to the window (and is at it, or has broken it).
            expect(Motion.tileOf(z.at).x).toBeGreaterThanOrEqual(7);
        }
    }
}
