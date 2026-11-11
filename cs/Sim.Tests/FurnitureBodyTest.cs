// Furniture stops you on foot (2026-11-12, FurnitureBody.cs): a box in its tile; a bed doesn't; never stuck in one.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class FurnitureBodyTest
{
    static (SimState state, Survivor you) at(params string[] map)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = map.ToList() });
        var you = state.survivors[0];
        Commands.applyCommand(state, new Command { type = "takeControl", survivorId = you.id });
        return (state, you);
    }

    static void walk(SimState state, int dx, int dy, int ticks)
    {
        Commands.applyCommand(state, new Command { type = "steer", dx = dx, dy = dy });
        for (int i = 0; i < ticks; i++) Tick.tick(state);
        Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
    }

    [Fact(DisplayName = "furniture > a crate stops you at its side; you go round it")]
    public void Stops_you()
    {
        var (state, you) = at("s..c...", ".......");
        walk(state, 1, 0, 60);
        double edge = 3 * Motion.SUB + Motion.SUB / 2 - FurnitureBody.HALF; // the crate's near side
        expect(you.at.x).toBeLessThanOrEqual(edge - Motion.BODY_RADIUS + 1);
        expect(you.at.x).toBeGreaterThan(edge - Motion.BODY_RADIUS - 40);   // and right up to it
        expect(FurnitureBody.inside(state, you.at, Motion.BODY_RADIUS) == null).toBe(true);
        // Down and along: past it by the row below.
        walk(state, 1, 1, 30);
        walk(state, 1, 0, 60);
        expect(you.at.x).toBeGreaterThan(5 * Motion.SUB);
    }

    [Fact(DisplayName = "furniture > a bed is walked onto; and standing inside a piece you can always walk out")]
    public void Beds_and_never_stuck()
    {
        var (state, you) = at("s.b....", ".......");
        walk(state, 1, 0, 60);
        expect(you.at.x).toBeGreaterThan(3 * Motion.SUB); // through the bed's tile
        var (s2, y2) = at("s......");
        FurnitureMod.addFurniture(s2, "crate", 0, 0); // put down on top of you
        expect(FurnitureBody.inside(s2, y2.at, Motion.BODY_RADIUS) != null).toBe(true);
        walk(s2, 1, 0, 40);
        expect(FurnitureBody.inside(s2, y2.at, Motion.BODY_RADIUS) == null).toBe(true);
        expect(y2.at.x).toBeGreaterThan(Motion.SUB);
    }
}
