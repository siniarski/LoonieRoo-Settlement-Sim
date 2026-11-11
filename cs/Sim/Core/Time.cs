// World clock, derived purely from state.tick. A day is DAY_LENGTH ticks; the game starts at
// START_HOUR. Day length is a world setting (settings.dayScale): tick() sets the scale each tick, and
// modules whose values derive from the day register with OnClock to recompute them.
namespace LoonieRoo.Sim;

public static partial class Time
{
    /// <summary>Ticks per in-game hour at day scale 1. 200 ticks = 20 s at 1x.</summary>
    public const int BASE_TICKS_PER_HOUR = 200;
    public const int HOURS_PER_DAY = 24;
    public const int START_HOUR = 6;
    /// <summary>Night is from NIGHT_START (inclusive) to NIGHT_END (exclusive), wrapping midnight.</summary>
    public const int NIGHT_START = 21;
    public const int NIGHT_END = 5;
    /// <summary>Hours over which it gets dark (before NIGHT_START) and light (after NIGHT_END).</summary>
    public const int TWILIGHT_HOURS = 2;

    static int scale = 1;
    static readonly List<Action> listeners = new();

    /// <summary>Ticks per in-game hour (scaled).</summary>
    public static int TICKS_PER_HOUR { get; private set; } = BASE_TICKS_PER_HOUR;
    /// <summary>4800 ticks at scale 1.</summary>
    public static int DAY_LENGTH { get; private set; } = BASE_TICKS_PER_HOUR * HOURS_PER_DAY;
    /// <summary>Ticks in one night (21:00 to 05:00).</summary>
    public static int NIGHT_TICKS { get; private set; } = (HOURS_PER_DAY - NIGHT_START + NIGHT_END) * BASE_TICKS_PER_HOUR;

    /// <summary>Recompute something derived from the day now, and whenever the scale changes.</summary>
    public static void onClock(Action recompute)
    {
        listeners.Add(recompute);
        recompute();
    }

    public static int dayScale() => scale;

    public static void setDayScale(int n)
    {
        if (n == scale) return;
        scale = n;
        TICKS_PER_HOUR = BASE_TICKS_PER_HOUR * n;
        DAY_LENGTH = TICKS_PER_HOUR * HOURS_PER_DAY;
        NIGHT_TICKS = (HOURS_PER_DAY - NIGHT_START + NIGHT_END) * TICKS_PER_HOUR;
        foreach (var f in listeners) f();
    }

    /// <summary>Things that run at a rate per tick (needs, crops, work) run on one tick in every `scale`.</summary>
    public static bool slowTick(int tick) => tick % scale == 0;

    static int ClockTicks(int tick) => tick + START_HOUR * TICKS_PER_HOUR;

    /// <summary>Ticks since midnight, 0 &lt;= t &lt; DAY_LENGTH.</summary>
    public static int tickOfDay(int tick) => ClockTicks(tick) % DAY_LENGTH;

    /// <summary>Day number, starting at 1.</summary>
    public static int dayOf(int tick) => (int)Math.Floor((double)ClockTicks(tick) / DAY_LENGTH) + 1;

    /// <summary>Fractional hour of the day, 0 &lt;= h &lt; 24.</summary>
    public static double hourOf(int tick) => (double)(ClockTicks(tick) % DAY_LENGTH) / TICKS_PER_HOUR;

    public static bool isNight(int tick)
    {
        double h = hourOf(tick);
        return h >= NIGHT_START || h < NIGHT_END;
    }

    /// <summary>The night in progress (1 = the first night, starting 21:00 on day 1), or null by day.</summary>
    public static int? nightNumber(int tick)
    {
        if (!isNight(tick)) return null;
        return hourOf(tick) >= NIGHT_START ? dayOf(tick) : dayOf(tick) - 1;
    }

    /// <summary>Ticks since the current night began at 21:00. Only meaningful at night.</summary>
    public static int ticksIntoNight(int tick)
    {
        double h = hourOf(tick);
        double hoursIn = h >= NIGHT_START ? h - NIGHT_START : h + HOURS_PER_DAY - NIGHT_START;
        return (int)Js.Round(hoursIn * TICKS_PER_HOUR);
    }

    /// <summary>0 in full day, 1 in full night, ramping linearly through dusk and dawn. For rendering.</summary>
    public static double darkness(int tick)
    {
        double h = hourOf(tick);
        if (isNight(tick)) return 1;
        double duskStart = NIGHT_START - TWILIGHT_HOURS;
        if (h >= duskStart) return (h - duskStart) / TWILIGHT_HOURS;
        if (h < NIGHT_END + TWILIGHT_HOURS) return 1 - (h - NIGHT_END) / TWILIGHT_HOURS;
        return 0;
    }

    /// <summary>"06:30"</summary>
    public static string clockText(int tick)
    {
        double h = hourOf(tick);
        int hours = (int)Math.Floor(h);
        int minutes = (int)Math.Floor((h - hours) * 60);
        return $"{hours:00}:{minutes:00}";
    }
}
