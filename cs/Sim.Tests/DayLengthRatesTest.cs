// Rates and durations follow the day's length (settings.dayScale; CLAUDE.md rules 12 and 13): at scale 2 a rate is
// half per tick and the same per day, a timed action takes twice the ticks, and scale 1 is as it always was.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests;

public class DayLengthRatesTest : IDisposable
{
    public void Dispose() => Time.setDayScale(1);

    static int fires(int scale, int ticks, double n)
    {
        Time.setDayScale(scale);
        var c = 0;
        for (var t = 0; t < ticks; t++) if (Time.slowEvery(t, n)) c++;
        return c;
    }

    [Fact(DisplayName = "day length rates > slowEvery is tick % n at scale 1, and as often per day at scale 2")]
    public void SlowEvery()
    {
        Time.setDayScale(1);
        for (var t = 0; t < 500; t++) expect(Time.slowEvery(t, 7)).toBe(t % 7 == 0);
        expect(fires(2, 2 * 4800, 200)).toBe(fires(1, 4800, 200));
        expect(fires(2, 4800, 200)).toBe(fires(1, 4800, 200) / 2);
        expect(fires(3, 3 * 4800, 1200)).toBe(fires(1, 4800, 1200));
    }

    static double endurance(int scale, int ticks)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
        Time.setDayScale(scale);
        var s = Js.NN(Js.Get(state.survivors, 0));
        s.endurance = 0;
        for (var t = 0; t < ticks; t++) { state.tick = t; BodyMod.updateBody(state, s); }
        return s.endurance;
    }

    [Fact(DisplayName = "day length rates > endurance comes back half as fast per tick at scale 2, as fast per day")]
    public void Endurance()
    {
        var one = endurance(1, 400);
        expect(one > 0).toBe(true);
        expect(endurance(2, 400)).toBe(one / 2);
        expect(endurance(2, 800)).toBe(one);
    }

    static double cropGrowth(int scale, int ticks, bool winter)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
        Time.setDayScale(scale);
        state.settings.winter = winter;
        double sum = 0;
        for (var t = 0; t < ticks; t++)
        {
            state.tick = t;
            if (Time.slowTick(t)) sum += Seasons.cropGrows(state);
        }
        return sum;
    }

    [Fact(DisplayName = "day length rates > the nuclear winter's half rate for crops holds at scale 2")]
    public void Crops()
    {
        expect(cropGrowth(1, 400, true)).toBe(200);
        expect(cropGrowth(2, 800, true)).toBe(200);
        expect(cropGrowth(2, 800, false)).toBe(cropGrowth(1, 400, false));
    }

    static double social(int scale, int ticks)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["ss."] });
        state.settings.dayScale = scale;
        foreach (var s in Js.Iter(state.survivors)) s.social = 10;
        for (var t = 0; t < ticks; t++) Tick.tick(state);
        return Js.NN(Js.Get(state.survivors, 0)).social;
    }

    [Fact(DisplayName = "day length rates > company lifts the social need half as fast per tick at scale 2")]
    public void Social()
    {
        var one = social(1, 400) - 10;
        expect(one > 0).toBe(true);
        expect(social(2, 400) - 10).toBe(one / 2);
        expect(social(2, 800) - 10).toBe(one);
    }

    static int mealTicks(int scale)
    {
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["sf."] });
        state.settings.dayScale = scale;
        var s = Js.NN(Js.Get(state.survivors, 0));
        s.satiety = 100;
        var eating = 0;
        for (var t = 0; t < 2000; t++)
        {
            Tick.tick(state);
            if (s.meal != null && s.meal.progress > 0) eating++;
            else if (eating > 0) break;
        }
        return eating;
    }

    [Fact(DisplayName = "day length rates > a survivor's meal takes twice the ticks at scale 2")]
    public void Meal()
    {
        var one = mealTicks(1);
        expect(one).toBe(Food.EAT_TICKS - 1);
        expect(mealTicks(2)).toBe(2 * Food.EAT_TICKS - 1);
    }
}
