// Ported from tests/scenario.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Left out (they use the browser UI):
//   scenarios > loads and round-trips every bundled scenario file (uses import.meta (Vite))
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class ScenarioTest
{
    public static readonly Scenario twoRooms = Js.JsonParse<Scenario>(File.ReadAllText(Js.RepoFile("scenarios/two-rooms.json")));

    /// <summary>scenarios</summary>
    public class D001_scenarios
    {

        [Fact(DisplayName = "scenarios > round-trips a map through load and dumpText")]
        public void T002_round_trips_a_map_through_load_and_dumpText()
        {
            var state = ScenarioMod.loadScenario(((Scenario)(twoRooms)));
            expect(DebugText.dumpText(state)).toBe(twoRooms.map.join("\n"));
        }

        // Left out: "scenarios > loads and round-trips every bundled scenario file" uses import.meta (Vite), from the browser UI.

        [Fact(DisplayName = "scenarios > reads tiles from the map legend")]
        public void T003_reads_tiles_from_the_map_legend()
        {
            var state = ScenarioMod.loadScenario(new Scenario { seed = 0, map = [".#", "D."] });
            expect(Grids.getTile(state.grid, 0, 0)).toBe("floor");
            expect(Grids.getTile(state.grid, 1, 0)).toBe("wall");
            expect(Grids.getTile(state.grid, 0, 1)).toBe("door");
        }

        [Fact(DisplayName = "scenarios > starts at tick 0 with an empty log and version 0")]
        public void T004_starts_at_tick_0_with_an_empty_log_and_version_0()
        {
            var state = ScenarioMod.loadScenario(((Scenario)(twoRooms)));
            expect(state.tick).toBe(0);
            expect(state.commandLog).toEqual(Js.Arr([]));
            expect(state.walkabilityVersion).toBe(0);
        }

        [Fact(DisplayName = "scenarios > rejects ragged rows and unknown characters with a location")]
        public void T005_rejects_ragged_rows_and_unknown_characters_with_a_location()
        {
            expect(() => ScenarioMod.loadScenario(new Scenario { seed = 0, map = ["...", ".."] })).toThrow(new JsRegExp("row 1", ""));
            expect(() => ScenarioMod.loadScenario(new Scenario { seed = 0, map = [".x."] })).toThrow(new JsRegExp("'x' at \\(1, 0\\)", ""));
            expect(() => ScenarioMod.loadScenario(new Scenario { seed = 0, map = [] })).toThrow(new JsRegExp("empty", ""));
        }

        [Fact(DisplayName = "scenarios > runs a scenario command log")]
        public void T006_runs_a_scenario_command_log()
        {
            Scenario scenario = new Scenario { seed = 0, map = ["...", "..."], commands = [new() { tick = 0, command = new() { type = "placeWall", x = Js.I(0), y = Js.I(0) } }, new() { tick = 5, command = new() { type = "placeDoor", x = Js.I(2), y = Js.I(1) } }] };
            var state = ScenarioMod.runScenario(scenario, 10);
            expect(state.tick).toBe(10);
            expect(DebugText.dumpText(state)).toBe(new List<string> { "#..", "..D" }.join("\n"));
        }
    }
}
