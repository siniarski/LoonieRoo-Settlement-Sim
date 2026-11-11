// Ported from src/sim/seasons.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Seasons and weather.
//
// Seasons run SEASON_DAYS each, from spring. Crops grow at full speed in
// spring and summer, half speed in autumn, not at all in winter. Each dawn
// rolls the day's weather (sim RNG): rain (not in winter) makes crops grow
// half as fast again; anyone outdoors gets wet (BodyMod.cs); snow (in winter) just looks
// cold. Winter nights are cold outdoors: a slow loss of health, and the mood
// to match. Indoors (any enclosed room) is warm.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Seasons
{

    public const int SEASON_DAYS = 5;

    public static readonly List<string> SEASONS = ["spring", "summer", "autumn", "winter"];

    public const double RAIN_CHANCE = 0.3;

    public const double SNOW_CHANCE = 0.4;

    /// <summary>The top of the dawn roll is a weather event: a blizzard in winter, a heatwave in summer, a storm otherwise.</summary>
    public const double EVENT_CHANCE = 0.15;

    /// <summary>In a storm, the chance each hour that lightning strikes near the colony.</summary>
    public const double LIGHTNING_CHANCE = 0.08;

    /// <summary>Rain, snow, storms and blizzards wet things (crops, collectors, fires).</summary>
    public static bool isWet(string w)
    {
        return (((w == "rain") || (w == "snow")) || (w == "storm")) || (w == "blizzard");
    }

    public static string seasonOf(int tick)
    {
        return Js.NN(Js.Get(SEASONS, JsMath.floor(((double)((Time.dayOf(tick) - 1)) / (SEASON_DAYS))) % SEASONS.length));
    }
    public static string seasonOf(double tick) => seasonOf(Js.I(tick));

    /// <summary>How much a planted crop outdoors grows this tick: 0, 1 or 2 (Farming.updateFarm asks once, on a slow tick).</summary>
    public static double cropGrows(SimState state)
    {
        var season = seasonOf(state.tick);
        if (season == "winter")
        {
            return 0;
        }
        if (state.settings.winter && ((state.tick % 2) == 1))
        {
            return 0;
        }
        double growth = (season == "autumn") ? (((state.tick % 2) == 0) ? 1 : 0) : 1;
        if (((state.weather == "rain") || (state.weather == "storm")) && ((state.tick % 2) == 0))
        {
            growth += 1;
        }
        return growth;
    }

    /// <summary>Does lightning strike on `tick`? A pure read (the game's sound asks it too, for thunder).</summary>
    public static bool lightningAt(SimState state, int tick)
    {
        return ((state.weather == "storm") && ((tick % Time.TICKS_PER_HOUR) == 0)) && (Hash.hashRoll(tick, 0x57) < LIGHTNING_CHANCE);
    }

    /// <summary>Lightning strikes something that burns near a survivor (a hash picks what).</summary>
    public static void lightning(SimState state)
    {
        var s = Js.Get(state.survivors, JsMath.floor(((double)(Hash.hashRoll(state.tick, 1)) * (state.survivors.length))));
        if (!Js.T(s))
        {
            return;
        }
        var targets = state.furniture.filter((f) => ((JsMath.abs(f.x - s.pos.x) + JsMath.abs(f.y - s.pos.y)) <= 20) && !Rooms.isIndoors(state, f.x, f.y));
        var t = Js.Get(targets, JsMath.floor(((double)(Hash.hashRoll(state.tick, 2)) * (targets.length))));
        if (Js.T(t) && FireMod.ignite(state, t.x, t.y))
        {
            Events.logEvent(state, $"Lightning struck a {Js.Str(t.kind)} at ({Js.Str(t.x)}, {Js.Str(t.y)})!");
        }
    }

    /// <summary>A winter night (any night, in a nuclear winter), or a blizzard.</summary>
    public static bool coldOutside(SimState state)
    {
        return (((seasonOf(state.tick) == "winter") || state.settings.winter) && Time.isNight(state.tick)) || (state.weather == "blizzard");
    }

    /// <summary>Out of doors in the cold (a winter night, a cold region's autumn night or winter day, or a blizzard: Climate.cs) without a coat or a heater. Hand-made maps are all mild.</summary>
    public static bool isCold(SimState state, Survivor s)
    {
        var cold = Js.T(state.world) ? (Climate.regionCold(state, s.pos.x, s.pos.y) || (state.weather == "blizzard")) : coldOutside(state);
        return ((Js.T(cold) && !Rooms.isIndoors(state, s.pos.x, s.pos.y)) && !Js.T(Clothing.wearing(state, s, "coat"))) && !Power.isWarmed(state, s.pos);
    }

    /// <summary>Dawn's weather roll, and the weather's effect on people outdoors. Runs every tick.</summary>
    public static void updateWeather(SimState state)
    {
        if ((state.tick > 0) && ((state.tick % Time.DAY_LENGTH) == 0))
        {
            var season = seasonOf(state.tick);
            var winter = season == "winter";
            var roll = Rng.random(state);
            state.weather = (roll < (winter ? SNOW_CHANCE : RAIN_CHANCE)) ? (winter ? "snow" : "rain") : ((roll >= (1 - Difficulty.eventChance(state, EVENT_CHANCE))) ? (winter ? "blizzard" : ((season == "summer") ? "heatwave" : "storm")) : "clear");
            if (state.weather == "blizzard")
            {
                Events.logEvent(state, "A blizzard: anyone outdoors without a coat or a heater will freeze, day or night");
            }
            if (state.weather == "heatwave")
            {
                Events.logEvent(state, "A heatwave: thirst comes twice as fast, and food spoils quicker");
            }
            if (state.weather == "storm")
            {
                Events.logEvent(state, "A storm: lightning may set things alight");
            }
        }
        if (lightningAt(state, state.tick))
        {
            lightning(state);
        }
        var coldNight = coldOutside(state);
        var soaking = isWet(state.weather);
        foreach (var s in Js.Iter((state.survivors).ToList()))
        {
            if ((s.bodyHeat < BodyMod.FREEZING) && ((state.tick % Difficulty.coldEvery(state)) == 0))
            {
                s.health = JsMath.max(0, s.health - 1);
                s.lastHurtBy = "the cold";
            }
            if ((!coldNight && !soaking) || Rooms.isIndoors(state, s.pos.x, s.pos.y))
            {
                continue;
            }
            var coat = Clothing.wearing(state, s, "coat");
            if (Js.T(coat) && ((state.tick % Time.TICKS_PER_HOUR) == 0))
            {
                Clothing.wearOut(state, s, coat);
            }
        }
    }
}
