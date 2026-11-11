// Kerbs (Heights step 6, 2026-11-11): a street's asphalt lies a kerb's height under its sidewalks; a car crawls on paving.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class KerbTest
{
    const int SIZE = 256;

    [Fact(DisplayName = "kerbs > street asphalt is sunk a kerb's height under its sidewalks, which stay at the ground's level")]
    public void sunk_asphalt()
    {
        foreach (var seed in new double[] { 1, 42 })
        {
            var p = Worldplan.planWorld(seed, SIZE, "mainland");
            var f = Heights.make(seed, SIZE, "mainland");
            int streets = 0, walks = 0, full = 0;
            for (int y = 0; y < SIZE; y++)
                for (int x = 0; x < SIZE; x++)
                {
                    int mask = (int)p.roadMask[y * SIZE + x];
                    expect(f.sunk(x, y), $"seed {seed} {x},{y} mask {mask}").toBe(mask == Worldplan.STREET);
                    double ground = f.at(x + 0.5, y + 0.5), stands = f.surface(x + 0.5, y + 0.5);
                    if (mask != Worldplan.STREET) { expect(stands).toBe(ground); if (mask == Worldplan.SIDEWALK) walks++; continue; }
                    streets++;
                    double drop = ground - stands;
                    expect(drop).toBeGreaterThanOrEqual(0);
                    expect(drop).toBeLessThanOrEqual(Heights.KERB + 1e-9);
                    if (drop > Heights.KERB - 1e-9) full++;
                }
            expect(streets).toBeGreaterThan(500);
            expect(walks).toBeGreaterThan(200);
            // Nearly all of it at the full kerb (the rest eases out toward a country road).
            expect((double)full / streets).toBeGreaterThan(0.7);
        }
    }

    [Fact(DisplayName = "kerbs > the kerb eases to nothing where a street meets a country road, and the asphalt has no step of its own")]
    public void eases_out()
    {
        var p = Worldplan.planWorld(42, SIZE, "mainland");
        var f = Heights.make(42, SIZE, "mainland");
        int joins = 0;
        for (int y = 1; y < SIZE - 1; y++)
            for (int x = 1; x < SIZE - 1; x++)
            {
                if (p.roadMask[y * SIZE + x] != Worldplan.STREET) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (p.roadMask[(y + dy) * SIZE + x + dx] != Worldplan.ROAD) continue;
                    joins++;
                    // The corners the two tiles share are not sunk at all: no step across the road.
                    int cx = dx > 0 ? x + 1 : x, cy = dy > 0 ? y + 1 : y;
                    expect(f.kerb(cx, cy)).toBe(0);
                    expect(f.kerb(cx + (dx == 0 ? 1 : 0), cy + (dy == 0 ? 1 : 0))).toBe(0);
                }
            }
        expect(joins).toBeGreaterThan(4);
        // In the middle of the town the kerb is whole.
        var c = Towns.anchor(p.nature, p.towns[0]);
        expect(f.kerb(c.x + 4, c.y + 4)).toBe(Heights.KERB);
        // A hand-made map has no kerbs.
        var flat = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s...."] });
        expect(Heights.surface(flat, 2.5, 0.5)).toBe(0);
    }

    [Fact(DisplayName = "kerbs > a car goes at a crawl on paving (a sidewalk), at full speed on the road")]
    public void crawl()
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.......", "........", "........", "........", "........", "........"] });
        var car = Vehicles.addVehicle(state, 4, 3);
        Grids.setGround(state.grid, Grids.tileIndex(state.grid, 4, 3), Array.IndexOf(Grids.GROUNDS, "road"));
        expect(Vehicles.topSpeed(state, car)).toBe(Vehicles.MAX_SPEED);
        Grids.setGround(state.grid, Grids.tileIndex(state.grid, 4, 3), Array.IndexOf(Grids.GROUNDS, "paving"));
        expect(Vehicles.topSpeed(state, car)).toBe(Vehicles.KERB_CRAWL);
        expect(Vehicles.KERB_CRAWL).toBeLessThan(Vehicles.OFFROAD_MAX / 2);
    }
}
