// Ported from tests/rng.test.ts, tests/time.test.ts, tests/lookup.test.ts and tests/grid.test.ts.
// (Grid's "non-integer" cases don't arise in C#: coordinates are ints.)
using System.Text.Json;

namespace LoonieRoo.Sim.Tests;

sealed class Holder : IRngHolder
{
    public double rng { get; set; }
}

public class RngTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new Holder { rng = Rng.seedRng(42) };
        var b = new Holder { rng = Rng.seedRng(42) };
        var seqA = Enumerable.Range(0, 20).Select(_ => Rng.random(a)).ToList();
        var seqB = Enumerable.Range(0, 20).Select(_ => Rng.random(b)).ToList();
        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void DifferentSeedsDifferentSequences() => Assert.NotEqual(Rng.random(new Holder { rng = Rng.seedRng(1) }), Rng.random(new Holder { rng = Rng.seedRng(2) }));

    [Fact]
    public void PureAtTheStepLevel() => Assert.Equal(Rng.mulberry32(123), Rng.mulberry32(123));

    [Fact]
    public void StateStaysUint32AndValuesInRange()
    {
        var holder = new Holder { rng = Rng.seedRng(-7) };
        for (int i = 0; i < 1000; i++)
        {
            double v = Rng.random(holder);
            Assert.InRange(v, 0, 0.9999999999);
            Assert.Equal(Math.Floor(holder.rng), holder.rng);
            Assert.InRange(holder.rng, 0, 0xffffffff);
        }
    }

    [Fact]
    public void ResumesIdenticallyAfterJsonRoundTrip()
    {
        var holder = new Holder { rng = Rng.seedRng(99) };
        Rng.random(holder);
        Rng.random(holder);
        var restored = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(holder))!;
        Assert.Equal(Rng.random(holder), Rng.random(restored));
    }

    [Fact]
    public void RandomIntStaysInRangeAndCoversIt()
    {
        var holder = new Holder { rng = Rng.seedRng(5) };
        var seen = new SortedSet<int>();
        for (int i = 0; i < 500; i++)
        {
            int n = Rng.randomInt(holder, 3, 7);
            Assert.InRange(n, 3, 6);
            seen.Add(n);
        }
        Assert.Equal(new[] { 3, 4, 5, 6 }, seen);
    }

    [Fact]
    public void RandomIntRejectsEmptyRanges() => Assert.Throws<InvalidOperationException>(() => Rng.randomInt(new Holder { rng = 1 }, 4, 4));

    [Fact]
    public void PickReturnsAnElementAndRejectsEmpty()
    {
        var holder = new Holder { rng = Rng.seedRng(8) };
        Assert.Contains(Rng.pick(holder, new[] { "a", "b", "c" }), new[] { "a", "b", "c" });
        Assert.Throws<InvalidOperationException>(() => Rng.pick(holder, Array.Empty<string>()));
    }
}

public class TimeTests
{
    /// <summary>The tick at which a given day and hour is reached (the game starts at 06:00 on day 1).</summary>
    static int At(int day, double hour) => (int)((day - 1) * Time.DAY_LENGTH + (hour - 6) * Time.TICKS_PER_HOUR);

    [Fact]
    public void StartsAt0600OnDay1()
    {
        Assert.Equal(1, Time.dayOf(0));
        Assert.Equal(6, Time.hourOf(0));
        Assert.Equal("06:00", Time.clockText(0));
    }

    [Fact]
    public void AdvancesAnHourPerTicksPerHourAndRollsOverAtMidnight()
    {
        Assert.Equal("13:30", Time.clockText(At(1, 13.5)));
        Assert.Equal(1, Time.dayOf(At(1, 23.99)));
        Assert.Equal(2, Time.dayOf(At(1, 24)));
        Assert.Equal("00:00", Time.clockText(At(1, 24)));
        Assert.Equal(3, Time.dayOf(At(3, 12)));
    }

    [Fact]
    public void NightIs2100To0500()
    {
        Assert.False(Time.isNight(At(1, 20.99)));
        Assert.True(Time.isNight(At(1, 21)));
        Assert.True(Time.isNight(At(2, 4.99)));
        Assert.False(Time.isNight(At(2, 5)));
    }

    [Fact]
    public void DarknessFadesInOverDuskAndOutOverDawn()
    {
        Assert.Equal(0, Time.darkness(At(1, 12)));
        Assert.Equal(0, Time.darkness(At(1, 19)));
        Assert.Equal(0.5, Time.darkness(At(1, 20)), 5);
        Assert.Equal(1, Time.darkness(At(1, 22)));
        Assert.Equal(0.5, Time.darkness(At(2, 6)), 5);
        Assert.Equal(0, Time.darkness(At(2, 7)));
    }

    [Fact]
    public void NumbersNightsFrom1AcrossMidnight()
    {
        Assert.Null(Time.nightNumber(At(1, 12)));
        Assert.Equal(1, Time.nightNumber(At(1, 21)));
        Assert.Equal(1, Time.nightNumber(At(2, 3)));
        Assert.Equal(2, Time.nightNumber(At(2, 21.5)));
        Assert.Equal(2, Time.nightNumber(At(3, 4.9)));
    }

    [Fact]
    public void CountsTicksIntoTheNightFrom2100()
    {
        Assert.Equal(8 * Time.TICKS_PER_HOUR, Time.NIGHT_TICKS);
        Assert.Equal(0, Time.ticksIntoNight(At(1, 21)));
        Assert.Equal((int)(2.5 * Time.TICKS_PER_HOUR), Time.ticksIntoNight(At(1, 23.5)));
        Assert.Equal(7 * Time.TICKS_PER_HOUR, Time.ticksIntoNight(At(2, 4)));
    }
}

public class LookupTests
{
    sealed class Thing : IHasId
    {
        public string id { get; set; } = "";
    }

    [Fact]
    public void AlwaysAnswersAsAScanWould()
    {
        var list = new List<Thing>();
        int next = 0;
        for (int round = 0; round < 3000; round++)
        {
            double r = Hash.hashRoll(round, 1);
            if (r < 0.4) list.Add(new Thing { id = $"x{next++}" });
            else if (r < 0.6 && list.Count > 0) list.RemoveAt((int)Math.Floor(Hash.hashRoll(round, 2) * list.Count));
            string id = $"x{(int)Math.Floor(Hash.hashRoll(round, 3) * (next + 2))}";
            Assert.Same(list.FirstOrDefault(x => x.id == id), Lookup.findById(list, id));
        }
    }
}

public class GridTests
{
    [Fact]
    public void CreatesAGridFilledWithFloor() => Assert.Equal(Enumerable.Repeat("floor", 6), Grids.createGrid(3, 2).tiles);

    [Fact]
    public void RejectsInvalidSizes() => Assert.Throws<ArgumentException>(() => Grids.createGrid(0, 5));

    [Fact]
    public void ChecksBounds()
    {
        var grid = Grids.createGrid(4, 3);
        Assert.True(Grids.inBounds(grid, 0, 0));
        Assert.True(Grids.inBounds(grid, 3, 2));
        Assert.False(Grids.inBounds(grid, 4, 0));
        Assert.False(Grids.inBounds(grid, -1, 0));
    }

    [Fact]
    public void GetsAndSetsTilesRowMajor()
    {
        var grid = Grids.createGrid(4, 3);
        Assert.True(Grids.setTile(grid, 2, 1, "wall"));
        Assert.Equal("wall", Grids.getTile(grid, 2, 1));
        Assert.Equal("wall", grid.tiles[1 * 4 + 2]);
        Assert.Null(Grids.getTile(grid, 9, 9));
    }

    [Fact]
    public void ReportsWhetherSetTileChangedAnything()
    {
        var grid = Grids.createGrid(2, 2);
        Assert.False(Grids.setTile(grid, 0, 0, "floor"));
        Assert.True(Grids.setTile(grid, 0, 0, "door"));
        Assert.False(Grids.setTile(grid, 0, 0, "door"));
        Assert.False(Grids.setTile(grid, 5, 5, "wall"));
    }

    [Fact]
    public void FloorAndDoorsWalkableWallsAndOutOfBoundsNot()
    {
        var grid = Grids.createGrid(3, 1);
        Grids.setTile(grid, 1, 0, "wall");
        Grids.setTile(grid, 2, 0, "door");
        Assert.True(Grids.isWalkable(grid, 0, 0));
        Assert.False(Grids.isWalkable(grid, 1, 0));
        Assert.True(Grids.isWalkable(grid, 2, 0));
        Assert.False(Grids.isWalkable(grid, 3, 0));
    }

    [Fact]
    public void ListsInBoundsNeighboursInNESWOrder()
    {
        var grid = Grids.createGrid(3, 3);
        Assert.Equal(new[] { "(1, 0)", "(2, 1)", "(1, 2)", "(0, 1)" }, Grids.neighbours4(grid, 1, 1).Select(p => p.ToString()));
        Assert.Equal(new[] { "(1, 0)", "(0, 1)" }, Grids.neighbours4(grid, 0, 0).Select(p => p.ToString()));
    }
}
