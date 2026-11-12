// The ground's relief across chunk and region borders (Heights.relief, split from HeightsReliefTest so the check's
// shards balance: these two build a 2048-tile and a 1100-tile world, the slowest facts of the sim's tests).
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public class HeightsReliefBordersTest
{
    [Fact(DisplayName = "relief > continuous over chunk and region borders, and no steeper than a tenth anywhere")]
    public void relief_continuous()
    {
        var f = Heights.make(7, 2048, "mainland");
        double worst = 0;
        // Along x across the plan chunk borders (every 32) and the region border (1024), on several rows; and along y.
        foreach (int y in new[] { 100, 517, 1023, 1024, 1500 })
            for (int x = 0; x < 2047; x++)
            {
                double step = Math.Abs(Heights.relief(f, x + 1, y) - Heights.relief(f, x, y));
                worst = Math.Max(worst, step);
                if (x % 32 == 31 || x == 1023) expect(step).toBeLessThan(0.12);
            }
        foreach (int x in new[] { 100, 517, 1023, 1024, 1500 })
            for (int y = 0; y < 2047; y++) worst = Math.Max(worst, Math.Abs(Heights.relief(f, x, y + 1) - Heights.relief(f, x, y)));
        // The steepest the lattice allows: smoothstep's 1.5 x the biggest step of elevation between points, a fraction of RELIEF.
        expect(worst).toBeLessThan(0.12);
        // The start is the zero of the relief.
        var s = f.nature.start;
        expect(Heights.relief(f, s.x, s.y)).toBe(0);
    }

    [Fact(DisplayName = "relief > the ground is continuous across a region border too (a 1100-tile world: regions of 1024 and 76)")]
    public void ground_across_a_region_border()
    {
        var f = Heights.make(3, 1100, "mainland");
        double worst = 0;
        for (int y = 10; y < 1090; y += 5)
        {
            double across = Math.Abs(f.corner(1024, y) - f.corner(1023, y));
            worst = Math.Max(worst, across);
        }
        // No step at the border beyond what a bank or a building's ease makes anywhere (about half a metre a tile).
        expect(worst).toBeLessThan(0.5);
    }
}
