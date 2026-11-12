// Ported from tests/savefile.test.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
#nullable disable warnings
using LoonieRoo.Sim;
using static LoonieRoo.Sim.Tests.Vitest;
namespace LoonieRoo.Sim.Tests.Ported;

public partial class SavefileTest
{

    public static readonly List<string> MAP = ["..........", ".####D###.", ".#..s...#.", ".########.", "w.f...c..."];

    public static PlayedResult played()
    {
        var initial = ScenarioMod.loadScenario(new Scenario { seed = 9, map = MAP });
        var state = States.cloneState(initial);
        Tick.step(state, 50);
        Commands.applyCommand(state, new Command { type = "designateBuild", x = Js.I(0), y = Js.I(0), structure = "wall" });
        Tick.step(state, 200);
        Commands.applyCommand(state, new Command { type = "designateScavenge", x = Js.I(6), y = Js.I(4) });
        Tick.step(state, 300);
        return new PlayedResult { initial = initial, state = state };
    }

    /// <summary>save files</summary>
    public class D001_save_files
    {

        [Fact(DisplayName = "save files > round-trip the whole state exactly, but for the command log, which a save leaves behind")]
        public void T002_round_trip_the_whole_state_exactly_but_for_the_command_log_w()
        {
            var __d0 = played();
            var state = __d0.state;
            expect(state.commandLog.length).toBeGreaterThan(0);
            var loaded = Savefile.loadFile(Savefile.toSaveFile(state));
            expect((loaded.ok ? (object?)(loaded.kind) : false)).toBe("save");
            if (!loaded.ok)
            {
                return;
            }
            expect(loaded.state.commandLog).toEqual(Js.Arr([]));
            expect(Js.Json_stringify(Js.ObjFrom(loaded.state, ("commandLog", (object?)(Js.Arr([])))))).toBe(Js.Json_stringify(Js.ObjFrom(state, ("commandLog", (object?)(Js.Arr([]))))));
            Tick.step(loaded.state, 300);
            Tick.step(state, 300);
            expect(Js.Json_stringify(Js.ObjFrom(loaded.state, ("commandLog", (object?)(Js.Arr([])))))).toBe(Js.Json_stringify(Js.ObjFrom(state, ("commandLog", (object?)(Js.Arr([]))))));
        }

        [Fact(DisplayName = "save files > refuse files from another schema version, junk, and other JSON")]
        public void T003_refuse_files_from_another_schema_version_junk_and_other_JSON()
        {
            var __d1 = played();
            var state = __d1.state;
            var old = Js.JsonParse<JsRecord<object?>>(Savefile.toSaveFile(state));
            old["schemaVersion"] = States.SCHEMA_VERSION - 1;
            var result = Savefile.loadFile(Js.Json_stringify(old));
            expect(result.ok).toBe(false);
            expect((!result.ok ? (object?)(result.error) : false)).toMatch(new JsRegExp("different version", ""));
            expect(Savefile.loadFile("not json")).toEqual(Js.Obj(("ok", (object?)(false)), ("error", (object?)("not a JSON file"))));
            expect(Savefile.loadFile("{\"hello\": 1}")).toEqual(Js.Obj(("ok", (object?)(false)), ("error", (object?)("not a LoonieRoo file"))));
        }
    }

    /// <summary>compact saves</summary>
    public class D004_compact_saves
    {

        [Fact(DisplayName = "compact saves > run-length encoding round-trips, and a world save is far smaller than the raw state")]
        public void T005_run_length_encoding_round_trips_and_a_world_save_is_far_smal()
        {
            expect(Savefile.runLengthDecode(Savefile.runLengthEncode(["a", "a", "b", "a"]))).toEqual(Js.Arr(["a", "a", "b", "a"]));
            expect(Savefile.runLengthEncode([0, 0, 0, 1])).toEqual(Js.Arr([0, 3, 1, 1]));
            var world = Worldgen.createWorld(5, 256);
            var text = Savefile.toSaveFile(world);
            expect(text.length).toBeLessThan(((double)(Js.Json_stringify(world).length) / (3)));
            var loaded = Savefile.loadFile(text);
            expect((loaded.ok ? (object?)(Js.Json_stringify(loaded.state)) : false)).toBe(Js.Json_stringify(world));
        }

        [Fact(DisplayName = "compact saves > still loads a save written without the encoding")]
        public void T006_still_loads_a_save_written_without_the_encoding()
        {
            var __d2 = played();
            var state = __d2.state;
            var plain = Js.Json_stringify(Js.Obj(("format", (object?)("loonieroo-save")), ("schemaVersion", (object?)(States.SCHEMA_VERSION)), ("state", (object?)(state))));
            var loaded = Savefile.loadFile(plain);
            expect((loaded.ok ? (object?)(Js.Json_stringify(loaded.state)) : false)).toBe(Js.Json_stringify(state));
        }
    }

    /// <summary>bug reports</summary>
    public class D007_bug_reports
    {

        [Fact(DisplayName = "bug reports > replay from the starting state to exactly the reported moment")]
        public void T008_replay_from_the_starting_state_to_exactly_the_reported_momen()
        {
            var __d3 = played();
            var initial = __d3.initial;
            var state = __d3.state;
            var loaded = Savefile.loadFile(Savefile.toBugReport(initial, state, "Ash stood still"));
            expect((loaded.ok ? (object?)(loaded.kind) : false)).toBe("bug");
            if (!loaded.ok)
            {
                return;
            }
            expect(Js.Json_stringify(loaded.state)).toBe(Js.Json_stringify(state));
        }

        [Fact(DisplayName = "bug reports > carry only the commands since the starting state (e.g. a save loaded mid-game)")]
        public void T009_carry_only_the_commands_since_the_starting_state_e_g_a_save_()
        {
            var __d4 = played();
            var midGame = __d4.state;
            var start = States.cloneState(midGame);
            Commands.applyCommand(midGame, new Command { type = "designateBuild", x = Js.I(9), y = Js.I(0), structure = "door" });
            Tick.step(midGame, 100);
            var report = Js.JsonParse<JsRecord<object?>>(Savefile.toBugReport(start, midGame));
            expect(report["commands"]).toHaveLength(1);
            var loaded = Savefile.loadFile(Js.Json_stringify(report));
            expect((loaded.ok ? (object?)(Js.Json_stringify(loaded.state)) : false)).toBe(Js.Json_stringify(midGame));
        }

        [Fact(DisplayName = "bug reports > a report whose log does not replay is refused, not half-loaded")]
        public void T010_a_report_whose_log_does_not_replay_is_refused_not_half_loade()
        {
            var __d5 = played();
            var initial = __d5.initial;
            var state = __d5.state;
            var report = Js.JsonParse<JsRecord<object?>>(Savefile.toBugReport(initial, state));
            report["commands"] = System.Text.Json.Nodes.JsonNode.Parse(((System.Text.Json.JsonElement)report["commands"]!).GetRawText())!.AsArray().Reverse().Select(n => n?.DeepClone()).ToList();
            var loaded = Savefile.loadFile(Js.Json_stringify(report));
            expect(loaded.ok).toBe(false);
        }
    }

    /// <summary>save files: furniture as rows</summary>
    public class D011_save_files_furniture_as_rows
    {

        [Fact(DisplayName = "save files: furniture as rows > a generated world full of furniture round-trips exactly, and much smaller than as objects")]
        public void T012_a_generated_world_full_of_furniture_round_trips_exactly_and_()
        {
            var state = Worldgen.createWorld(5, 128);
            for (var y = 0; y < 128; y += Worldgen.CHUNK)
            {
                for (var x = 0; x < 128; x += Worldgen.CHUNK)
                {
                    Worldgen.ensureGeneratedAround(state, new Point { x = Js.I(x), y = Js.I(y) });
                }
            }
            Js.NN(Js.Get(state.furniture, 0)).scavenge = true;
            var text = Savefile.toSaveFile(state);
            var loaded = Savefile.loadFile(text);
            expect(loaded.ok).toBe(true);
            if (!loaded.ok)
            {
                return;
            }
            expect(Js.Json_stringify(Js.ObjFrom(loaded.state, ("commandLog", (object?)(Js.Arr([])))))).toBe(Js.Json_stringify(Js.ObjFrom(state, ("commandLog", (object?)(Js.Arr([]))))));
            expect(text.length).toBeLessThan(Js.Json_stringify(state.furniture).length);
        }
    }

    /// <summary>records keep JS order</summary>
    public class D009_records_keep_js_order
    {
        [Fact(DisplayName = "records keep JS order > a key added after a removal comes last, as it will after a save and load")]
        public void T001_a_key_added_after_a_removal_comes_last()
        {
            var r = new JsRecord<double> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
            r.Remove("b");
            r["d"] = 4;
            expect(Js.ObjectKeys(r)).toEqual(Js.Arr(["a", "c", "d"]));
            var back = new JsRecord<double>(r);  // what a load builds: the entries in the file's order
            back["e"] = 5;
            r["e"] = 5;
            expect(Js.ObjectKeys(back)).toEqual(Js.ObjectKeys(r));
        }
    }
}
