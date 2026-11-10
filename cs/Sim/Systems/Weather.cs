// Temperature (2026-11-10): degrees Celsius for any place and tick, derived, never stored. The one number the
// weather's other effects (snow falling and melting: Snow.cs) read, built to agree exactly with what the sim
// already calls cold and hot (Climate.coldFor, Climate.scorchingFor), so those rules can be read off it.
//
//   temp = C + amp * bracket(hour) + (night ? -STEP : +STEP) + event
//
//   C        the temperature at dusk and dawn (21:00 and 05:00, where the sim's night begins and ends): by climate
//            region and season (BASE), pinned to 0 where the old rule flips between day and night (a cold region's
//            autumn, a mild winter, a nuclear winter), clear of 0 (above it, or below) where the old rule never flips.
//   bracket  a curve that is 0 at 21:00 and 05:00, at most 0 in between (the night: coldest at 03:00, -0.5) and at
//            least 0 by day (warmest at 14:30, +1.5). So whatever the day's amplitude, the night is on the cold
//            side of C and the day on the warm side: the weather can stretch the day, not move the crossing.
//   STEP     the sim's night is sharp (isNight), so the temperature steps by 2 * STEP degrees at 21:00 and 05:00:
//            cold is "below FREEZING" exactly when the old rule says cold, on every tick, in every combination.
//   amp      SWING, varied by day between 0.8 and 1.2 (a hash of the day, not the RNG), halved under cloud (rain,
//            snow, storm, blizzard): overcast days have a narrower range.
//   event    a blizzard -14 (cold anywhere, as the rule says: Seasons.isCold), a heatwave up to +6 (scaled by the
//            day's curve, so +6 at the 14:30 peak and nothing at night). Rain, snow and storm
//            do not cool the air: that would move the crossing and so the rules (rain only narrows the range).
//
// `scorching` (the hot south's summer days) is "30 degrees or more" on any day without a heatwave.
// Indoors is never colder than INDOORS_MIN. Hand-made maps have one climate, mild.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Weather
{
    /// <summary>Cold is below this.</summary>
    public const double FREEZING = 0;

    /// <summary>The hot south's summer days are this or warmer.</summary>
    public const double SCORCHING = 30;

    /// <summary>The day's swing at its widest is 1.2 times this: the night's trough is -0.5 amp, the afternoon's peak 1.5 amp.</summary>
    public const double SWING = 6;

    /// <summary>Half the step at dusk and dawn.</summary>
    public const double STEP = 0.5;

    public const double BLIZZARD = -14;

    /// <summary>At the afternoon peak; less as the day's curve is lower, none at night.</summary>
    public const double HEATWAVE = 6;

    /// <summary>An enclosed room is never colder than this.</summary>
    public const double INDOORS_MIN = 8;

    public static readonly string[] CLIMATES = ["cold", "mild", "hot"];

    /// <summary>The temperature at dusk and dawn, by climate and season (spring, summer, autumn, winter). A nuclear winter pins the cold and mild regions' non-winter seasons to 0.</summary>
    public static double dawnDusk(string climate, string season, bool nuclearWinter)
    {
        var s = season switch { "spring" => 0, "summer" => 1, "autumn" => 2, _ => 3 };
        double c = climate switch
        {
            "cold" => s switch { 0 => 6, 1 => 13, 2 => 0, _ => -14 },
            "hot" => s switch { 0 => 18, 1 => 30, 2 => 16, _ => 5 },
            _ => s switch { 0 => 9, 1 => 17, 2 => 8, _ => 0 },
        };
        if (nuclearWinter && climate != "hot" && season != "winter")
        {
            c = 0;
        }
        return c;
    }

    /// <summary>The day's shape: 0 at 21:00 and 05:00, between -0.5 and 0 through the night (trough 03:00), between 0 and 1.5 by day (peak 14:30).</summary>
    public static double bracket(double hour)
    {
        if (hour >= Time.NIGHT_START || hour < Time.NIGHT_END)
        {
            var u = ((hour - Time.NIGHT_START + 24) % 24) / (24 - Time.NIGHT_START + Time.NIGHT_END);
            return -0.5 * Math.Sin(Math.PI * Math.Pow(u, 2.41));
        }
        var v = (hour - Time.NIGHT_END) / (Time.NIGHT_START - Time.NIGHT_END);
        return 1.5 * Math.Sin(Math.PI * Math.Pow(v, 1.33));
    }

    public static bool cloudy(string weather) => (weather == "rain") || (weather == "snow") || (weather == "storm") || (weather == "blizzard");

    /// <summary>The day's amplitude (a weather day runs from 06:00, as the dawn roll does).</summary>
    public static double amplitude(int tick, string weather)
    {
        var day = Math.Floor((double)tick / Time.DAY_LENGTH);
        return SWING * (0.8 + (0.4 * Hash.hashRoll(day, 0x7e))) * (cloudy(weather) ? 0.5 : 1);
    }

    /// <summary>Outdoors, in `climate`, at `tick`, with `weather` the day's weather.</summary>
    public static double temperatureOf(string climate, string season, int tick, string weather, bool nuclearWinter)
    {
        var night = Time.isNight(tick);
        var t = dawnDusk(climate, season, nuclearWinter) + (amplitude(tick, weather) * bracket(Time.hourOf(tick))) + (night ? -STEP : STEP);
        if (weather == "blizzard")
        {
            t += BLIZZARD;
        }
        else if (weather == "heatwave")
        {
            t += HEATWAVE * Math.Max(0, bracket(Time.hourOf(tick))) / 1.5;  // by day only: dusk, dawn and the night keep the rules' crossing
        }
        return t;
    }

    /// <summary>Outdoors in a climate region now (the day's weather is state.weather).</summary>
    public static double temperatureIn(SimState state, string climate)
    {
        return temperatureOf(climate, Seasons.seasonOf(state.tick), state.tick, state.weather, state.settings.winter);
    }

    /// <summary>Outdoors at a tile now.</summary>
    public static double temperature(SimState state, int x, int y) => temperatureIn(state, Climate.climateAt(state, x, y));
    public static double temperature(SimState state, double x, double y) => temperature(state, Js.I(x), Js.I(y));

    /// <summary>Where someone stands: outdoors, or in an enclosed room (never under INDOORS_MIN).</summary>
    public static double temperatureAt(SimState state, int x, int y)
    {
        var t = temperature(state, x, y);
        return Rooms.isIndoors(state, x, y) ? Math.Max(t, INDOORS_MIN) : t;
    }
    public static double temperatureAt(SimState state, double x, double y) => temperatureAt(state, Js.I(x), Js.I(y));

    /// <summary>The cold rule, read off the temperature (outdoors).</summary>
    public static bool isFreezing(double t) => t < FREEZING;

    /// <summary>A table of typical clear-day temperatures: each season, each region, at the hours given.</summary>
    public static string table(int[] hours, string weather = "clear")
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("season  region ").Append(string.Join("", hours.Select(h => $"{h:00}:00".PadLeft(7)))).Append('\n');
        var day = 0;
        foreach (var season in Seasons.SEASONS)
        {
            foreach (var climate in CLIMATES)
            {
                sb.Append(season.PadRight(8)).Append(climate.PadRight(7));
                foreach (var h in hours)
                {
                    // The same weather day for every hour: the third day of the season, hour h of it (tick 0 is 06:00).
                    var tick = (int)(((Seasons.SEASON_DAYS * Array.IndexOf(Seasons.SEASONS.ToArray(), season)) + 2) * (double)Time.DAY_LENGTH) + ((h - Time.START_HOUR + 24) % 24 * Time.TICKS_PER_HOUR);
                    sb.Append(temperatureOf(climate, season, tick, weather, false).ToString("0.0").PadLeft(7));
                }
                sb.Append('\n');
            }
            day++;
        }
        return sb.ToString();
    }
}
