// Ported from tests/doors.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class DoorsTest
{

    /// <summary>doors that open and close</summary>
    public class D001_doors_that_open_and_close
    {

        [Fact(DisplayName = "doors that open and close > closed: nobody you steer and no zombie gets through; open: both do")]
        public void T002_closed_nobody_you_steer_and_no_zombie_gets_through_open_both()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.D.."] });
            expect(Pathfinding.isPassable(state.grid, 2, 0, "free")).toBe(false);
            expect(Pathfinding.isPassable(state.grid, 2, 0, "zombie")).toBe(false);
            expect(Pathfinding.isPassable(state.grid, 2, 0, "survivor")).toBe(true);
            Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(0) });
            expect(Grids.isOpen(state.grid, 2, 0)).toBe(true);
            expect(Pathfinding.isPassable(state.grid, 2, 0, "free")).toBe(true);
            expect(Pathfinding.isPassable(state.grid, 2, 0, "zombie")).toBe(true);
        }

        [Fact(DisplayName = "doors that open and close > you open a door by using it, walk through, and it stays as you left it")]
        public void T003_you_open_a_door_by_using_it_walk_through_and_it_stays_as_you()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = [".sD.."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            Commands.applyCommand(state, new Command { type = "takeControl", survivorId = s.id });
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            for (var t = 0; t < 12; t++)
            {
                Tick.tick(state);
            }
            expect(s.pos.x).toBe(1);
            Commands.applyCommand(state, new Command { type = "steer", dx = 0, dy = 0 });
            expect(Commands.applyCommand(state, new Command { type = "use", x = Js.I(2), y = Js.I(0) })).toMatchObject(Js.Obj(("ok", (object?)(true))));
            expect(Grids.isOpen(state.grid, 2, 0)).toBe(true);
            Commands.applyCommand(state, new Command { type = "steer", dx = 1, dy = 0 });
            for (var t = 0; t < 12; t++)
            {
                Tick.tick(state);
            }
            expect(s.pos.x).toBeGreaterThanOrEqual(3);
            expect(Grids.isOpen(state.grid, 2, 0)).toBe(true);
        }

        [Fact(DisplayName = "doors that open and close > a locked door will not open; nobody closes a door on someone standing in it")]
        public void T004_a_locked_door_will_not_open_nobody_closes_a_door_on_someone_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.D.."] });
            Commands.applyCommand(state, new Command { type = "toggleLock", x = Js.I(2), y = Js.I(0) });
            expect(Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(0) }).ok).toBe(false);
            Commands.applyCommand(state, new Command { type = "toggleLock", x = Js.I(2), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(0) });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.pos = new Point { x = Js.I(2), y = Js.I(0) };
            s.at = Motion.centre(s.pos);
            expect(Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(0) }).ok).toBe(false);
        }

        [Fact(DisplayName = "doors that open and close > a zombie walks in through a door left open, and not through a shut one")]
        public void T005_a_zombie_walks_in_through_a_door_left_open_and_not_through_a()
        {
            double run(bool open)
            {
                var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["#####", "#s..#", "##D##", ".....", "..z.."] });
                Js.NN(Js.Get(state.zombies, 0)).kind = "static";
                Js.NN(Js.Get(state.survivors, 0)).priorities = new(JobTypes.NO_JOBS) {  };
                if (open)
                {
                    Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(2) });
                }
                for (var t = 0; t < 60; t++)
                {
                    Tick.tick(state);
                }
                return Js.NN(Js.Get(state.survivors, 0)).health;
            }
            expect(run(true)).toBeLessThan(1000);
            expect(run(false)).toBe(1000);
        }
    }

    /// <summary>colonists and doors</summary>
    public class D006_colonists_and_doors
    {

        [Fact(DisplayName = "colonists and doors > a colonist opens a closed door as they step in, and it shuts behind them")]
        public void T007_a_colonist_opens_a_closed_door_as_they_step_in_and_it_shuts_()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.D..."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) {  };
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(5), y = Js.I(0) });
            var sawOpen = false;
            for (var t = 0; t < 40; t++)
            {
                Tick.tick(state);
                if ((s.pos.x == 2) || (s.move?.to.x == 2))
                {
                    sawOpen = (Js.T(sawOpen) ? sawOpen : Grids.isOpen(state.grid, 2, 0));
                }
            }
            expect(s.pos.x).toBe(5);
            expect(sawOpen).toBe(true);
            expect(Grids.isOpen(state.grid, 2, 0)).toBe(false);
        }

        [Fact(DisplayName = "colonists and doors > a door you opened yourself stays open when a colonist goes through")]
        public void T008_a_door_you_opened_yourself_stays_open_when_a_colonist_goes_t()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 1, map = ["s.D..."] });
            var s = Js.NN(Js.Get(state.survivors, 0));
            s.priorities = new(JobTypes.NO_JOBS) {  };
            Commands.applyCommand(state, new Command { type = "toggleDoor", x = Js.I(2), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "moveSurvivor", survivorId = s.id, x = Js.I(5), y = Js.I(0) });
            for (var t = 0; t < 40; t++)
            {
                Tick.tick(state);
            }
            expect(Grids.isOpen(state.grid, 2, 0)).toBe(true);
        }
    }

    /// <summary>debug arenas</summary>
    public class D009_debug_arenas
    {

        [Fact(DisplayName = "debug arenas > one zombie, or a crowd, near you from the start, staying past dawn")]
        public void T010_one_zombie_or_a_crowd_near_you_from_the_start_staying_past_d()
        {
            var one = Arena.createArena(5, "one");
            expect(one.zombies).toHaveLength(1);
            expect(one.control).not.toBeNull();
            var crowd = Arena.createArena(5, "crowd");
            expect(crowd.zombies.length).toBeGreaterThan(((double)(Arena.CROWD_SIZE) / (2)));
            foreach (var z in Js.Iter(crowd.zombies))
            {
                expect(z.kind).toBe("static");
            }
            expect(crowd.settlements).toEqual(Js.Arr([]));
        }
    }
}
