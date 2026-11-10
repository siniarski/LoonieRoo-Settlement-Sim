// Ported from tests/skills.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class SkillsTest
{

    public static double runUntil(SimState state, Func<SimState, bool> done, double max = 2000)
    {
        for (var t = 0; t < max; t++)
        {
            if (Js.T(done(state)))
            {
                return t;
            }
            Tick.tick(state);
            expect(Invariants.checkInvariants(state), DebugText.dumpText(state)).toEqual(Js.Arr([]));
        }
        throw new Exception($"condition not met after {Js.Str(max)} ticks\n{Js.Str(DebugText.dumpText(state))}");
    }

    /// <summary>Ticks for one survivor to remove a wall, with a given build xp and traits.</summary>
    public static RemoveWallResult removeWall(double xp, List<string>? traits = null)
    {
        traits ??= new();
        var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s#"] });
        var s = Js.NN(Js.Get(state.survivors, 0));
        s.priorities = new(JobTypes.NO_JOBS) { ["build"] = 3 };
        s.xp = new() { ["build"] = xp };
        s.traits = traits;
        Commands.applyCommand(state, new Command { type = "designateRemove", x = Js.I(1), y = Js.I(0) });
        return new RemoveWallResult { ticks = runUntil(state, (st) => Grids.tileAt(st.grid, 1) == "floor"), s = s };
    }

    /// <summary>skills</summary>
    public class D001_skills
    {

        [Fact(DisplayName = "skills > work is practice: xp builds up in the job done")]
        public void T002_work_is_practice_xp_builds_up_in_the_job_done()
        {
            var __d0 = removeWall(0);
            var s = __d0.s;
            expect(s.xp["build"]).toBeGreaterThan(15);
            expect(Skills.skillLevel(s, "build")).toBe(0);
        }

        [Fact(DisplayName = "skills > each level makes the work quicker, up to half as fast again")]
        public void T003_each_level_makes_the_work_quicker_up_to_half_as_fast_again()
        {
            var novice = removeWall(0).ticks;
            var master = removeWall(((double)(Skills.MAX_LEVEL) * (Skills.XP_PER_LEVEL))).ticks;
            expect(master).toBeLessThan(novice);
            expect(master).toBeGreaterThanOrEqual(JsMath.floor(((double)(((double)(novice) * (2))) / (3))) - 2);
        }
    }

    /// <summary>traits</summary>
    public class D004_traits
    {

        [Fact(DisplayName = "traits > are rolled from the id: the same arrival always brings the same, most bring one")]
        public void T005_are_rolled_from_the_id_the_same_arrival_always_brings_the_sa()
        {
            expect(Skills.rollTraits("v42")).toEqual(Skills.rollTraits("v42"));
            var ids = Js.ArrayFrom(200, (int i) => $"v{Js.Str(i)}");
            var withOne = ids.filter((id) => Skills.rollTraits(id).length == 1).length;
            expect(withOne).toBeGreaterThan(120);
            expect(withOne).toBeLessThan(180);
        }

        [Fact(DisplayName = "traits > lazy works slower; cheerful and gloomy move mood")]
        public void T006_lazy_works_slower_cheerful_and_gloomy_move_mood()
        {
            expect(removeWall(0, ["lazy"]).ticks).toBeGreaterThan(removeWall(0).ticks);
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.s"] });
            Js.NN(Js.Get(state.survivors, 0)).traits = ["cheerful"];
            Js.NN(Js.Get(state.survivors, 1)).traits = ["gloomy"];
            expect(state.survivors.map((s) => Mood.mood(state, s))).toEqual(Js.Arr([Mood.MOOD_BASE + 10, Mood.MOOD_BASE - 10]));
        }

        [Fact(DisplayName = "traits > quick walks a tile in fewer ticks; tough takes less from a zombie")]
        public void T007_quick_walks_a_tile_in_fewer_ticks_tough_takes_less_from_a_zo()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.....", "......"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) {  };
            s.traits = ["quick"];
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(5), y = Js.I(0) });
            Tick.tick(state);
            expect(s.move?.duration).toBe(Survivors.TICKS_PER_TILE - 1);
            var fight = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["zs"] });
            fight.tick = Js.I(((double)(17) * (Time.TICKS_PER_HOUR)) + 1);
            Js.NN(Js.Get(fight.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
            Js.NN(Js.Get(fight.survivors, 0)).traits = ["tough"];
            runUntil(fight, (_) => Js.NN(Js.Get(fight.survivors, 0)).health < 1000, 50);
            expect(1000 - Js.NN(Js.Get(fight.survivors, 0)).health).toBeGreaterThanOrEqual(JsMath.round(((double)(((double)(Zombies.ZOMBIE_DAMAGE) * (3))) / (4))) - 1);
            expect(1000 - Js.NN(Js.Get(fight.survivors, 0)).health).toBeLessThanOrEqual(JsMath.round(((double)(((double)(Zombies.ZOMBIE_DAMAGE) * (3))) / (4))));
        }

        [Fact(DisplayName = "traits > a wanderer's traits show before you decide, and come with them when taken in")]
        public void T008_a_wanderer_s_traits_show_before_you_decide_and_come_with_the()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["v.s"] });
            var w = Js.NN(Js.Get(state.wanderers, 0));
            w.traits = ["quick"];
            Commands.applyCommand(state, new Command { type = "acceptWanderer", wandererId = w.id });
            expect(Js.NN(state.survivors.at(-1)).traits).toEqual(Js.Arr(["quick"]));
        }
    }

    /// <summary>roads</summary>
    public class D009_roads
    {

        [Fact(DisplayName = "roads > are a tick quicker per tile, for everyone")]
        public void T010_are_a_tick_quicker_per_tile_for_everyone()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.....", "......"] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) {  };
            for (var x = 0; x < 6; x++)
            {
                Grids.setGround(state.grid, x, 2);
            }
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(5), y = Js.I(0) });
            Tick.tick(state);
            expect(s.move?.duration).toBe(Survivors.TICKS_PER_TILE - 1);
            s.traits = ["quick"];
            runUntil(state, (_) => (s.move?.progress == 0) && (s.move.from.x > 0));
            expect(s.move?.duration).toBe(Survivors.TICKS_PER_TILE - 2);
        }
    }
}
