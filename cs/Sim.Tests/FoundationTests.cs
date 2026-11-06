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
        var a = new Holder { rng = Rng.SeedRng(42) };
        var b = new Holder { rng = Rng.SeedRng(42) };
        var seqA = Enumerable.Range(0, 20).Select(_ => Rng.Random(a)).ToList();
        var seqB = Enumerable.Range(0, 20).Select(_ => Rng.Random(b)).ToList();
        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void DifferentSeedsDifferentSequences() => Assert.NotEqual(Rng.Random(new Holder { rng = Rng.SeedRng(1) }), Rng.Random(new Holder { rng = Rng.SeedRng(2) }));

    [Fact]
    public void PureAtTheStepLevel() => Assert.Equal(Rng.Mulberry32(123), Rng.Mulberry32(123));

    [Fact]
    public void StateStaysUint32AndValuesInRange()
    {
        var holder = new Holder { rng = Rng.SeedRng(-7) };
        for (int i = 0; i < 1000; i++)
        {
            double v = Rng.Random(holder);
            Assert.InRange(v, 0, 0.9999999999);
            Assert.Equal(Math.Floor(holder.rng), holder.rng);
            Assert.InRange(holder.rng, 0, 0xffffffff);
        }
    }

    [Fact]
    public void ResumesIdenticallyAfterJsonRoundTrip()
    {
        var holder = new Holder { rng = Rng.SeedRng(99) };
        Rng.Random(holder);
        Rng.Random(holder);
        var restored = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(holder))!;
        Assert.Equal(Rng.Random(holder), Rng.Random(restored));
    }

    [Fact]
    public void RandomIntStaysInRangeAndCoversIt()
    {
        var holder = new Holder { rng = Rng.SeedRng(5) };
        var seen = new SortedSet<int>();
        for (int i = 0; i < 500; i++)
        {
            int n = Rng.RandomInt(holder, 3, 7);
            Assert.InRange(n, 3, 6);
            seen.Add(n);
        }
        Assert.Equal(new[] { 3, 4, 5, 6 }, seen);
    }

    [Fact]
    public void RandomIntRejectsEmptyRanges() => Assert.Throws<InvalidOperationException>(() => Rng.RandomInt(new Holder { rng = 1 }, 4, 4));

    [Fact]
    public void PickReturnsAnElementAndRejectsEmpty()
    {
        var holder = new Holder { rng = Rng.SeedRng(8) };
        Assert.Contains(Rng.Pick(holder, new[] { "a", "b", "c" }), new[] { "a", "b", "c" });
        Assert.Throws<InvalidOperationException>(() => Rng.Pick(holder, Array.Empty<string>()));
    }
}

public class TimeTests
{
    /// <summary>The tick at which a given day and hour is reached (the game starts at 06:00 on day 1).</summary>
    static int At(int day, double hour) => (int)((day - 1) * Time.DAY_LENGTH + (hour - 6) * Time.TICKS_PER_HOUR);

    [Fact]
    public void StartsAt0600OnDay1()
    {
        Assert.Equal(1, Time.DayOf(0));
        Assert.Equal(6, Time.HourOf(0));
        Assert.Equal("06:00", Time.ClockText(0));
    }

    [Fact]
    public void AdvancesAnHourPerTicksPerHourAndRollsOverAtMidnight()
    {
        Assert.Equal("13:30", Time.ClockText(At(1, 13.5)));
        Assert.Equal(1, Time.DayOf(At(1, 23.99)));
        Assert.Equal(2, Time.DayOf(At(1, 24)));
        Assert.Equal("00:00", Time.ClockText(At(1, 24)));
        Assert.Equal(3, Time.DayOf(At(3, 12)));
    }

    [Fact]
    public void NightIs2100To0500()
    {
        Assert.False(Time.IsNight(At(1, 20.99)));
        Assert.True(Time.IsNight(At(1, 21)));
        Assert.True(Time.IsNight(At(2, 4.99)));
        Assert.False(Time.IsNight(At(2, 5)));
    }

    [Fact]
    public void DarknessFadesInOverDuskAndOutOverDawn()
    {
        Assert.Equal(0, Time.Darkness(At(1, 12)));
        Assert.Equal(0, Time.Darkness(At(1, 19)));
        Assert.Equal(0.5, Time.Darkness(At(1, 20)), 5);
        Assert.Equal(1, Time.Darkness(At(1, 22)));
        Assert.Equal(0.5, Time.Darkness(At(2, 6)), 5);
        Assert.Equal(0, Time.Darkness(At(2, 7)));
    }

    [Fact]
    public void NumbersNightsFrom1AcrossMidnight()
    {
        Assert.Null(Time.NightNumber(At(1, 12)));
        Assert.Equal(1, Time.NightNumber(At(1, 21)));
        Assert.Equal(1, Time.NightNumber(At(2, 3)));
        Assert.Equal(2, Time.NightNumber(At(2, 21.5)));
        Assert.Equal(2, Time.NightNumber(At(3, 4.9)));
    }

    [Fact]
    public void CountsTicksIntoTheNightFrom2100()
    {
        Assert.Equal(8 * Time.TICKS_PER_HOUR, Time.NIGHT_TICKS);
        Assert.Equal(0, Time.TicksIntoNight(At(1, 21)));
        Assert.Equal((int)(2.5 * Time.TICKS_PER_HOUR), Time.TicksIntoNight(At(1, 23.5)));
        Assert.Equal(7 * Time.TICKS_PER_HOUR, Time.TicksIntoNight(At(2, 4)));
    }
}

public class LookupTests
{
    sealed class Thing : IHasId
    {
        public string id { get; init; } = "";
    }

    [Fact]
    public void AlwaysAnswersAsAScanWould()
    {
        var list = new List<Thing>();
        int next = 0;
        for (int round = 0; round < 3000; round++)
        {
            double r = Hash.HashRoll(round, 1);
            if (r < 0.4) list.Add(new Thing { id = $"x{next++}" });
            else if (r < 0.6 && list.Count > 0) list.RemoveAt((int)Math.Floor(Hash.HashRoll(round, 2) * list.Count));
            string id = $"x{(int)Math.Floor(Hash.HashRoll(round, 3) * (next + 2))}";
            Assert.Same(list.FirstOrDefault(x => x.id == id), Lookup.FindById(list, id));
        }
    }
}

public class GridTests
{
    [Fact]
    public void CreatesAGridFilledWithFloor() => Assert.Equal(Enumerable.Repeat("floor", 6), Grids.CreateGrid(3, 2).tiles);

    [Fact]
    public void RejectsInvalidSizes() => Assert.Throws<ArgumentException>(() => Grids.CreateGrid(0, 5));

    [Fact]
    public void ChecksBounds()
    {
        var grid = Grids.CreateGrid(4, 3);
        Assert.True(Grids.InBounds(grid, 0, 0));
        Assert.True(Grids.InBounds(grid, 3, 2));
        Assert.False(Grids.InBounds(grid, 4, 0));
        Assert.False(Grids.InBounds(grid, -1, 0));
    }

    [Fact]
    public void GetsAndSetsTilesRowMajor()
    {
        var grid = Grids.CreateGrid(4, 3);
        Assert.True(Grids.SetTile(grid, 2, 1, "wall"));
        Assert.Equal("wall", Grids.GetTile(grid, 2, 1));
        Assert.Equal("wall", grid.tiles[1 * 4 + 2]);
        Assert.Null(Grids.GetTile(grid, 9, 9));
    }

    [Fact]
    public void ReportsWhetherSetTileChangedAnything()
    {
        var grid = Grids.CreateGrid(2, 2);
        Assert.False(Grids.SetTile(grid, 0, 0, "floor"));
        Assert.True(Grids.SetTile(grid, 0, 0, "door"));
        Assert.False(Grids.SetTile(grid, 0, 0, "door"));
        Assert.False(Grids.SetTile(grid, 5, 5, "wall"));
    }

    [Fact]
    public void FloorAndDoorsWalkableWallsAndOutOfBoundsNot()
    {
        var grid = Grids.CreateGrid(3, 1);
        Grids.SetTile(grid, 1, 0, "wall");
        Grids.SetTile(grid, 2, 0, "door");
        Assert.True(Grids.IsWalkable(grid, 0, 0));
        Assert.False(Grids.IsWalkable(grid, 1, 0));
        Assert.True(Grids.IsWalkable(grid, 2, 0));
        Assert.False(Grids.IsWalkable(grid, 3, 0));
    }

    [Fact]
    public void ListsInBoundsNeighboursInNESWOrder()
    {
        var grid = Grids.CreateGrid(3, 3);
        Assert.Equal(new[] { "(1, 0)", "(2, 1)", "(1, 2)", "(0, 1)" }, Grids.Neighbours4(grid, 1, 1).Select(p => p.ToString()));
        Assert.Equal(new[] { "(1, 0)", "(0, 1)" }, Grids.Neighbours4(grid, 0, 0).Select(p => p.ToString()));
    }
}
