// Ported from tests/sight.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class SightTest
{

    /// <summary>A room inside (1, 1)..(3, 3), walled on its edges, glass in the middle of its east wall, a door south.</summary>
    public static List<ScenarioEdgesItem> room()
    {
        List<ScenarioEdgesItem> edges = [];
        /*TODO return type*/object add(double x, double y, string side, string kind = "wall")
        {
            return edges.push(new() { x = Js.I(x), y = Js.I(y), side = side, kind = kind });
        }
        for (var x = 1; x <= 3; x++)
        {
            add(x, 1, "n");
            add(x, 4, "n", (x == 2) ? "door" : "wall");
        }
        for (var y = 1; y <= 3; y++)
        {
            add(1, y, "w");
            add(4, y, "w", (y == 2) ? "window" : "wall");
        }
        return edges;
    }

    public static readonly string ROW = ".".repeat(20);

    public static readonly List<string> MAP = Js.ArrayFrom(8, (int _i) => ROW);

    public static readonly double NOON = ((double)(Time.DAY_LENGTH) / (2));

    public static void quiet(SimState state)
    {
        foreach (var s in Js.Iter(state.survivors))
        {
            foreach (var k in Js.Iter(Js.ObjectKeys(s.priorities)))
            {
                s.priorities[Js.Idx(k)] = 0;
            }
        }
    }

    /// <summary>line of sight</summary>
    public class D001_line_of_sight
    {

        [Fact(DisplayName = "line of sight > walls and shut doors block it, glass does not, boards do")]
        public void T002_walls_and_shut_doors_block_it_glass_does_not_boards_do()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room() });
            var g = state.grid;
            expect(Sight.sightLine(g, new Point { x = Js.I(2), y = Js.I(2) }, new Point { x = Js.I(8), y = Js.I(2) })).toBe(true);
            expect(Sight.sightLine(g, new Point { x = Js.I(2), y = Js.I(1) }, new Point { x = Js.I(8), y = Js.I(1) })).toBe(false);
            expect(Sight.sightLine(g, new Point { x = Js.I(2), y = Js.I(3) }, new Point { x = Js.I(2), y = Js.I(7) })).toBe(false);
            g.edgeOpen.push(Grids.edgeBetween(g, 2, 3, 2, 4));
            expect(Sight.sightLine(g, new Point { x = Js.I(2), y = Js.I(3) }, new Point { x = Js.I(2), y = Js.I(7) })).toBe(true);
            g.edgeBoarded.push(Grids.edgeBetween(g, 3, 2, 4, 2));
            expect(Sight.sightLine(g, new Point { x = Js.I(2), y = Js.I(2) }, new Point { x = Js.I(8), y = Js.I(2) })).toBe(false);
        }

        [Fact(DisplayName = "line of sight > a wall tile hides what is behind it, and is itself seen")]
        public void T003_a_wall_tile_hides_what_is_behind_it_and_is_itself_seen()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".....", "..#..", "....."] });
            expect(Sight.sightLine(state.grid, new Point { x = Js.I(0), y = Js.I(1) }, new Point { x = Js.I(2), y = Js.I(1) })).toBe(true);
            expect(Sight.sightLine(state.grid, new Point { x = Js.I(0), y = Js.I(1) }, new Point { x = Js.I(4), y = Js.I(1) })).toBe(false);
        }

        [Fact(DisplayName = "line of sight > only what is in view is explored, and less of it at night")]
        public void T004_only_what_is_in_view_is_explored_and_less_of_it_at_night()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? ("..s" + r.slice(3)) : r), edges = room() });
            quiet(state);
            state.tick = Js.I(NOON);
            Grids.fillExplored(state.grid, 0);
            Tick.tick(state);
            expect(Fog.isExplored(state, 7, 2)).toBe(true);
            expect(Fog.isExplored(state, 2, 0)).toBe(false);
            expect(Fog.isExplored(state, 2, 6)).toBe(false);
            var night = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? ("s" + r.slice(1)) : r) });
            quiet(night);
            while (!Time.isNight(night.tick))
            {
                night.tick = Js.I(night.tick + 10);
            }
            Grids.fillExplored(night.grid, 0);
            Tick.tick(night);
            expect(Fog.isExplored(night, Sight.NIGHT_SIGHT, 2)).toBe(true);
            expect(Fog.isExplored(night, Sight.NIGHT_SIGHT + 1, 2)).toBe(false);
            expect(Sight.NIGHT_SIGHT).toBeLessThan(Sight.SIGHT_RADIUS);
        }

        [Fact(DisplayName = "line of sight > what you see now is your view alone while you are in control")]
        public void T005_what_you_see_now_is_your_view_alone_while_you_are_in_control()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 2) ? ("..s" + r.slice(3)) : ((y == 7) ? ("s" + r.slice(1)) : r)), edges = room() });
            state.tick = Js.I(NOON);
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = Js.NN(Js.Get(state.survivors, 0)).id });
            var seen = Sight.visibleNow(state);
            double at(double x, double y)
            {
                return Js.Get(seen, ((double)(y) * (state.grid.width)) + x);
            }
            expect(at(7, 2)).toBe(1);
            expect(at(0, 7)).toBe(0);
        }
    }

    /// <summary>stealth</summary>
    public class D006_stealth
    {

        [Fact(DisplayName = "stealth > the dead see you through glass but not through a wall, and only as far as they see")]
        public void T007_the_dead_see_you_through_glass_but_not_through_a_wall_and_on()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP, edges = room() });
            state.tick = Js.I(NOON);
            expect(Sight.zombieSees(state, new Point { x = Js.I(9), y = Js.I(2) }, new Point { x = Js.I(2), y = Js.I(2) })).toBe(true);
            expect(Sight.zombieSees(state, new Point { x = Js.I(9), y = Js.I(1) }, new Point { x = Js.I(2), y = Js.I(1) })).toBe(false);
            expect(Sight.zombieSees(state, new Point { x = Js.I(19), y = Js.I(7) }, new Point { x = Js.I((19 - Sight.ZOMBIE_SIGHT) - 1), y = Js.I(7) })).toBe(false);
            expect(Sight.zombieSees(state, new Point { x = Js.I(19), y = Js.I(7) }, new Point { x = Js.I((19 - Sight.ZOMBIE_SIGHT) + 1), y = Js.I(7) })).toBe(true);
            expect(Sight.zombieSees(state, new Point { x = Js.I(19), y = Js.I(7) }, new Point { x = Js.I((19 - Sight.ZOMBIE_SIGHT) + 1), y = Js.I(7) }, true)).toBe(false);
        }

        [Fact(DisplayName = "stealth > someone keeping still behind a wall goes unnoticed; walking past, they are heard")]
        public void T008_someone_keeping_still_behind_a_wall_goes_unnoticed_walking_p()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 1) ? ("..s" + r.slice(3)) : r), edges = room() });
            quiet(state);
            state.tick = Js.I(NOON);
            var z = Zombies.addZombie(state, 5, 0, 200, "static");
            for (var i = 0; i < 30; i++)
            {
                Tick.tick(state);
            }
            expect(z.targetId).toBeNull();
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 1 });
            expect(Sight.heardFrom(state, s)).toBe(Sight.WALK_HEARD);
            for (var i = 0; (i < 30) && (z.targetId == null); i++)
            {
                Tick.tick(state);
            }
            expect(z.targetId).toBe(s.id);
        }

        [Fact(DisplayName = "stealth > sneaking is slower and quieter; running is louder")]
        public void T009_sneaking_is_slower_and_quieter_running_is_louder()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 5) ? ("s" + r.slice(1)) : r) });
            quiet(state);
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0, run = true });
            expect(Sight.heardFrom(state, s)).toBe(Sight.RUN_HEARD);
            expect(Commands.applyCommand(state, new Command { type = "sneak", on = true }).ok).toBe(true);
            expect(Sight.heardFrom(state, s)).toBe(Sight.SNEAK_HEARD);
            int walked(bool sneak)
            {
                var st = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 5) ? ("s" + r.slice(1)) : r) });
                quiet(st);
                var me = Js.NN(Js.Get(st.survivors, 0));
                Commands.applyCommand(st, new Command { type = "takeControl", survivorId = me.id });
                me.at = Motion.centre(new Point { x = Js.I(0), y = Js.I(5) });
                Commands.applyCommand(st, new Command { type = "sneak", on = sneak });
                Commands.applyCommand(st, new Command { type = "steer", dx = 1, dy = 0 });
                for (var i = 0; i < 40; i++)
                {
                    Tick.tick(st);
                }
                return me.at.x;
            }
            expect(walked(true)).toBeLessThan(walked(false));
        }

        [Fact(DisplayName = "stealth > a blow to one of the dead that never saw you coming does far more")]
        public void T010_a_blow_to_one_of_the_dead_that_never_saw_you_coming_does_far()
        {
            double blow(bool aware)
            {
                var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = MAP.map((r, y) => (y == 5) ? ("....s" + r.slice(5)) : r) });
                quiet(state);
                state.tick = Js.I(NOON);
                var s = Js.NN(Js.Get(state.survivors, 0));
                Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
                var z = Zombies.addZombie(state, 5, 5, 10000, "static");
                z.targetId = aware ? s.id : null;
                z.nextThinkTick = state.tick + 1000;
                Commands.applyCommand(state, new Command { type = "swing", x = Js.I(5), y = Js.I(5) });
                return 10000 - z.health;
            }
            var sneak = blow(false);
            var open = blow(true);
            expect(open).toBeGreaterThan(0);
            expect(sneak).toBe(JsMath.round(((double)(((double)(open) * (ControlMod.SNEAK_ATTACK_PCT))) / (100))));
        }
    }
}
