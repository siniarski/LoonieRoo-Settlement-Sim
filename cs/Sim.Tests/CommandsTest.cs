// Ported from tests/commands.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class CommandsTest
{

    public static SimState fresh()
    {
        return States.createState(new NewStateOptions { seed = 1, width = Js.I(5), height = Js.I(3) });
    }

    /// <summary>commands</summary>
    public class D001_commands
    {

        [Fact(DisplayName = "commands > places walls and doors and clears tiles")]
        public void T002_places_walls_and_doors_and_clears_tiles()
        {
            var state = fresh();
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(1) });
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(1) });
            Commands.applyCommand(state, new Command { type = "placeDoor", x = Js.I(2), y = Js.I(1) });
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(3), y = Js.I(1) });
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(0), y = Js.I(1) });
            expect(DebugText.dumpText(state)).toBe(new List<string> { ".....", ".#D#.", "....." }.join("\n"));
        }

        [Fact(DisplayName = "commands > bumps walkabilityVersion only when a tile actually changes")]
        public void T003_bumps_walkabilityVersion_only_when_a_tile_actually_changes()
        {
            var state = fresh();
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(1) });
            expect(state.walkabilityVersion).toBe(1);
            var again = Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(1), y = Js.I(1) });
            expect(again).toEqual(Js.Obj(("ok", (object?)(true)), ("changed", (object?)(false))));
            expect(state.walkabilityVersion).toBe(1);
            Commands.applyCommand(state, new Command { type = "placeDoor", x = Js.I(1), y = Js.I(1) });
            expect(state.walkabilityVersion).toBe(2);
        }

        [Fact(DisplayName = "commands > rejects out-of-bounds commands without touching state")]
        public void T004_rejects_out_of_bounds_commands_without_touching_state()
        {
            var state = fresh();
            var result = Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(5), y = Js.I(0) });
            expect(result.ok).toBe(false);
            expect(state.walkabilityVersion).toBe(0);
            expect(state.commandLog).toEqual(Js.Arr([]));
        }

        [Fact(DisplayName = "commands > logs accepted commands with the tick they were applied at")]
        public void T005_logs_accepted_commands_with_the_tick_they_were_applied_at()
        {
            var state = fresh();
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) });
            Tick.step(state, 3);
            Commands.applyCommand(state, new Command { type = "placeDoor", x = Js.I(1), y = Js.I(0) });
            expect(state.commandLog).toEqual(Js.Arr([Js.Obj(("tick", (object?)(0)), ("command", (object?)(Js.Obj(("type", (object?)("placeWall")), ("x", (object?)(0)), ("y", (object?)(0)))))), Js.Obj(("tick", (object?)(3)), ("command", (object?)(Js.Obj(("type", (object?)("placeDoor")), ("x", (object?)(1)), ("y", (object?)(0))))))]));
        }

        [Fact(DisplayName = "commands > does not log no-op commands")]
        public void T006_does_not_log_no_op_commands()
        {
            var state = fresh();
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) });
            Commands.applyCommand(state, new Command { type = "clearTile", x = Js.I(1), y = Js.I(0) });
            expect(state.commandLog).toHaveLength(1);
        }

        [Fact(DisplayName = "commands > logs a copy, so mutating the caller object does not rewrite history")]
        public void T007_logs_a_copy_so_mutating_the_caller_object_does_not_rewrite_h()
        {
            var state = fresh();
            Command cmd = new Command { type = "placeWall", x = Js.I(0), y = Js.I(0) };
            Commands.applyCommand(state, cmd);
            cmd.x = Js.I(4);
            expect(Js.Get(state.commandLog, 0)?.command).toEqual(Js.Obj(("type", (object?)("placeWall")), ("x", (object?)(0)), ("y", (object?)(0))));
        }

        [Fact(DisplayName = "commands > spawns animals and dogs on open ground, and nothing else")]
        public void T008_spawns_animals_and_dogs_on_open_ground()
        {
            var state = fresh();
            int x = 0;
            foreach (var kind in new[] { "deer", "rabbit", "wolf", "bear", "dog" })
                expect(Commands.applyCommand(state, new Command { type = "spawnAnimal", kind = kind, x = Js.I(x++), y = Js.I(0) }).changed).toBe(true);
            expect(state.animals.map((a) => a.kind).join(",")).toBe("deer,rabbit,wolf,bear");
            expect(state.dogs).toHaveLength(1);
            Commands.applyCommand(state, new Command { type = "placeWall", x = Js.I(0), y = Js.I(2) });
            expect(Commands.applyCommand(state, new Command { type = "spawnAnimal", kind = "wolf", x = Js.I(0), y = Js.I(2) }).ok).toBe(false);
            expect(Commands.applyCommand(state, new Command { type = "spawnAnimal", kind = "dragon", x = Js.I(1), y = Js.I(2) }).ok).toBe(false);
            expect(state.animals).toHaveLength(4);
        }
    }
}
