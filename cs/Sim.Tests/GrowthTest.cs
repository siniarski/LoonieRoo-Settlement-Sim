// Growth (Growth.cs): foliage as a pure function of the tile and the growing days since it was cleared, plus a sparse record.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class GrowthTest
{
    /// <summary>The tick that day-count d (days since the start of day 1; 0.25 is the game's start) falls on at day scale 1.</summary>
    static int TickAt(double d) => (int)Math.Round(d * Time.DAY_LENGTH - Time.START_HOUR * Time.TICKS_PER_HOUR);

    static SimState Meadow()
    {
        var row = new string('.', 24);
        return ScenarioMod.loadScenario(new Scenario { seed = 1, map = Enumerable.Repeat(row, 24).ToList() });
    }

    [Fact(DisplayName = "growth > the calendar: a growing day in spring and summer, a third in autumn, none in winter")]
    public void calendar()
    {
        var s = Meadow();
        expect(Growth.G(s, 0)).toBe(0);
        expect(Growth.G(s, TickAt(5.25))).toBeCloseTo(5, 3);
        expect(Growth.G(s, TickAt(10.25))).toBeCloseTo(9.8375, 3);   // a quarter day of autumn in it
        expect(Growth.G(s, TickAt(15.25))).toBeCloseTo(11.5, 3);
        // Winter: nothing, day 16 to day 20.
        expect(Growth.G(s, TickAt(19.9))).toBeCloseTo(Growth.G(s, TickAt(15.25)), 6);
        expect(Growth.G(s, TickAt(17.5))).toBe(Growth.G(s, TickAt(19.5)));
        // And spring again.
        expect(Growth.G(s, TickAt(25.25))).toBeCloseTo(16.75, 3);
        var last = -1.0;
        for (var t = 0; t < 50000; t += 997) { var g = Growth.G(s, t); expect(g).toBeGreaterThanOrEqual(last); last = g; }
    }

    [Fact(DisplayName = "growth > tickWhen is the first tick a growth total is reached")]
    public void tick_when()
    {
        var s = Meadow();
        foreach (var g in new[] { 0.5, 3.0, 11.75, 12.0, 40.5, 137.2, 400.0 })
        {
            var t = Growth.tickWhen(s, g);
            expect(Growth.G(s, t)).toBeGreaterThanOrEqual(g);
            expect(Growth.G(s, t - 1)).toBeLessThan(g);
        }
    }

    [Fact(DisplayName = "growth > a new world's first day is bare: nothing grown, nothing recorded")]
    public void day_zero()
    {
        var s = Meadow();
        for (var y = 0; y < 24; y++)
            for (var x = 0; x < 24; x++)
            {
                var c = Growth.cover(s, x, y);
                expect(c.grass + c.weeds + c.bush + c.crack + c.creep).toBe(0);
                expect(c.sapling).toBeLessThan(0.0001);
            }
        Growth.clear(s, 3, 3);   // at tick 0 "cleared" is how every tile begins: nothing to record
        expect(Growth.recordSize(s)).toBe(0);
    }

    [Fact(DisplayName = "growth > grass comes in over a week, weeds over a month, and tiles give the same answers in any order")]
    public void stages_and_order()
    {
        var s = Meadow();
        s.tick = TickAt(8.25);   // a week on, 7 growing days
        var week = Growth.cover(s, 5, 5);
        expect(week.grass).toBeGreaterThan(0.4);
        expect(week.weeds).toBe(0);
        s.tick = TickAt(30.25);
        var month = Growth.cover(s, 5, 5);
        expect(month.grass).toBe(1);
        expect(month.weeds).toBeGreaterThan(0.1);
        s.tick = TickAt(330.25);
        var year = Growth.cover(s, 5, 5);
        expect(year.weeds).toBe(1);
        var tiles = Enumerable.Range(0, 24 * 24).Select(i => (x: i % 24, y: i / 24)).ToList();
        var fwd = tiles.Select(t => Growth.cover(s, t.x, t.y)).ToList();
        var back = Enumerable.Reverse(tiles).Select(t => Growth.cover(s, t.x, t.y)).Reverse().ToList();
        for (var i = 0; i < fwd.Count; i++) expect(fwd[i] == back[i]).toBe(true);
    }

    [Fact(DisplayName = "growth > nothing grows in winter")]
    public void winter()
    {
        var s = Meadow();
        s.tick = TickAt(16.3);   // the first of winter
        var a = Growth.cover(s, 7, 7);
        s.tick = TickAt(20.2);
        var b = Growth.cover(s, 7, 7);
        expect(a == b).toBe(true);
        s.tick = TickAt(36.25);
        expect(Growth.cover(s, 7, 7).weeds).toBeGreaterThan(a.weeds);
    }

    [Fact(DisplayName = "growth > a cleared tile regrows from its clearing tick, its neighbours don't")]
    public void cleared_regrows()
    {
        var s = Meadow();
        s.tick = TickAt(60.25);   // three whole years on: the same point of the year as the start
        expect(Growth.cover(s, 10, 10).grass).toBe(1);
        Growth.clear(s, 10, 10);
        expect(Growth.recordSize(s)).toBe(1);
        expect(s.cleared[Js.Str(Grids.tileIndex(s.grid, 10, 10))]).toBe(s.tick);
        expect(Growth.cover(s, 10, 10).grass).toBe(0);
        expect(Growth.cover(s, 11, 10).grass).toBe(1);
        // A week later it is where an untouched tile was a week into the world.
        s.tick = TickAt(67.25);
        var fresh = Meadow();
        fresh.tick = TickAt(7.25);
        expect(Growth.cover(s, 10, 10).grass).toBeCloseTo(Growth.cover(fresh, 10, 10).grass, 6);
    }

    [Fact(DisplayName = "growth > the record drops a tile once it has regrown, and the tile reads the same either side")]
    public void record_expires()
    {
        var s = Meadow();
        s.tick = TickAt(40.25);
        Growth.clear(s, 4, 4);
        Growth.clear(s, 5, 4);
        expect(Growth.recordSize(s)).toBe(2);
        s.tick = TickAt(120.25);
        Growth.expire(s);
        expect(Growth.recordSize(s)).toBe(2);   // 80 days of growth isn't every stage
        s.tick = TickAt(40.25 + 20 * 40);       // 40 game years on
        var before = Growth.cover(s, 4, 4);
        Growth.expire(s);
        expect(Growth.recordSize(s)).toBe(0);
        expect(Growth.cover(s, 4, 4) == before).toBe(true);
        expect(before.weeds).toBe(1);
        expect(before.grass).toBe(1);
    }

    [Fact(DisplayName = "growth > roads crack late and then grass creeps in from their edges")]
    public void roads()
    {
        var s = Meadow();
        var road = Array.IndexOf(Grids.GROUNDS, "road");
        for (var x = 0; x < 24; x++) for (var y = 8; y < 13; y++) s.grid.ground[Grids.tileIndex(s.grid, x, y)] = road;
        s.tick = TickAt(30.25);
        expect(Growth.cover(s, 6, 10).crack).toBe(0);
        s.tick = TickAt(360.25);   // about a year
        var edge = Growth.cover(s, 6, 8);
        var middle = Growth.cover(s, 6, 10);
        expect(middle.kind).toBe(GrowthKind.Road);
        expect(middle.crack).toBeGreaterThan(0.2);
        expect(middle.grass).toBe(0);
        expect(edge.creep).toBeGreaterThanOrEqual(middle.creep);
    }

    [Fact(DisplayName = "growth > foraging in overgrown ground finds more")]
    public void forage_bonus()
    {
        var s = Meadow();
        s.tick = 1;
        expect(Growth.forageBonus(s, 5, 5)).toBeCloseTo(1, 1);
        s.tick = TickAt(300.25);
        expect(Growth.forageBonus(s, 5, 5)).toBeGreaterThan(1.3);
    }

    [Fact(DisplayName = "growth > a wall put up clears its tile; a farm plot keeps it bare")]
    public void commands_clear()
    {
        var s = Meadow();
        s.tick = TickAt(60.25);
        Commands.setStructure(s, 3, 3, "wall");
        expect(Growth.recordSize(s)).toBe(1);
        Farming.addPlot(s, 6, 6);
        expect(Growth.cover(s, 6, 6).grass).toBe(0);
    }
}
