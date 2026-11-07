// Ported from tests/threats.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ThreatsTest
{

    /// <summary>zombie kinds</summary>
    public class D001_zombie_kinds
    {

        [Fact(DisplayName = "zombie kinds > none before their night; a spread of them later, with their own health")]
        public void T002_none_before_their_night_a_spread_of_them_later_with_their_ow()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["."] });
            var early = Js.ArrayFrom(200, (int _i) =>     {
        var z = Zombies.addZombie(state, 0, 0, 200, "static");
        ZombieKinds.giveVariant(z, 1);
        return z.variant;
    });
            expect(Js.Set(early)).toEqual(Js.Set(["walker"]));
            var late = Js.ArrayFrom(400, (int _i) =>     {
        var z = Zombies.addZombie(state, 0, 0, 200, "static");
        ZombieKinds.giveVariant(z, 10);
        return z;
    });
            var kinds = Js.Set(late.map((z) => z.variant));
            expect(kinds).toEqual(Js.Set(["walker", "runner", "screamer", "brute", "crawler"]));
            var brute = Js.NN(late.find((z) => z.variant == "brute"));
            expect(brute.maxHealth).toBe(((double)(((double)(200) * (ZombieKinds.VARIANTS["brute"].healthPct))) / (100)));
        }

        [Fact(DisplayName = "zombie kinds > crawlers get under barricades; walkers do not")]
        public void T003_crawlers_get_under_barricades_walkers_do_not()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".=."] });
            var z = Zombies.addZombie(state, 0, 0, 200, "static");
            expect(Pathfinding.isPassable(state.grid, 1, 0, Zombies.moverOf(z))).toBe(false);
            z.variant = "crawler";
            expect(Pathfinding.isPassable(state.grid, 1, 0, Zombies.moverOf(z))).toBe(true);
        }

        [Fact(DisplayName = "zombie kinds > a screamer screams when it spots someone, waking the sleepers round it")]
        public void T004_a_screamer_screams_when_it_spots_someone_waking_the_sleepers()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s" + ".".repeat(29)] });
            Js.NN(Js.Get(state.survivors, 0)).priorities["guard"] = 0;
            var z = Zombies.addZombie(state, 10, 0, 200, "static");
            z.variant = "screamer";
            state.dormant = [new() { id = "d1", x = Js.I(29), y = Js.I(0) }];
            Tick.tick(state);
            expect(z.screamed).toBe(true);
            expect(state.dormant).toEqual(Js.Arr([]));
        }
    }

    /// <summary>weather events</summary>
    public class D005_weather_events
    {

        [Fact(DisplayName = "weather events > the top of the dawn roll is an event; a blizzard is cold by day")]
        public void T006_the_top_of_the_dawn_roll_is_an_event_a_blizzard_is_cold_by_d()
        {
            expect(Seasons.EVENT_CHANCE).toBeGreaterThan(0);
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            state.tick = Js.I(((double)(Time.DAY_LENGTH) / (2)));
            expect(Seasons.isCold(state, s)).toBe(false);
            state.weather = "blizzard";
            expect(Seasons.isCold(state, s)).toBe(true);
            var year = ScenarioMod.loadScenario(new Scenario { seed = 5, map = ["..."] });
            var seen = new JsSet<string>();
            for (var d = 1; d < ((double)(((double)(4) * (Seasons.SEASON_DAYS))) * (3)); d++)
            {
                year.tick = Js.I(((double)(d) * (Time.DAY_LENGTH)));
                Tick.tick(year);
                seen.add(year.weather);
            }
            expect(Js.ListOf(seen).some((w) => ((w == "storm") || (w == "heatwave")) || (w == "blizzard"))).toBe(true);
        }
    }

    /// <summary>the storyteller</summary>
    public class D007_the_storyteller
    {

        [Fact(DisplayName = "the storyteller > eases off after deaths, and leans on a rich colony")]
        public void T008_eases_off_after_deaths_and_leans_on_a_rich_colony()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s" + "m".repeat(29), "m".repeat(30), "m".repeat(30), "m".repeat(30)] });
            expect(Director.threatScale(state)).toBeCloseTo(1.1);
            for (var i = 0; i < 5; i++)
            {
                Events.logEvent(state, $"Bea died of testing");
            }
            expect(Director.threatScale(state)).toBe(Director.MIN_THREAT);
        }

        [Fact(DisplayName = "the storyteller > leaves a cache when the colony is on its knees")]
        public void T009_leaves_a_cache_when_the_colony_is_on_its_knees()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s....."] });
            state.tick = Js.I(((double)(2) * (Time.DAY_LENGTH)));
            Events.logEvent(state, "Bea died of testing");
            Events.logEvent(state, "Cal died of testing");
            Director.updateDirector(state);
            expect(state.items.filter((i) => i.kind == "food").length).toBeGreaterThan(0);
        }
    }
}
