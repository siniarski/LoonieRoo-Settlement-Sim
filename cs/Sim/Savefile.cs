// Save files and bug reports (savefile.ts), ported by hand: the TypeScript reshapes plain JSON objects, which here
// is done on System.Text.Json nodes. The files are the same as the TypeScript writes.
//
// A save is the whole state but its command log; a bug report is a starting state plus the commands applied since,
// so loading it replays the game to the moment it was filed. Files from a different schema version are refused.
// The grid's per-tile layers are run-length encoded ([value, count, ...], `rle: true`), and furniture goes out as rows
// of values in FURNITURE_FIELDS order with trailing defaults dropped. Files without either encoding still load.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoonieRoo.Sim;

public static class Savefile
{
    /// <summary>The grid layers worth compressing.</summary>
    static readonly string[] LAYERS = ["tiles", "ground", "explored", "dirt", "damage", "edges"];

    /// <summary>A furniture piece's fields, in the order addFurniture makes them, and what each is when new (the first four have none: a row always keeps them).</summary>
    static readonly string[] FURNITURE_FIELDS = ["id", "kind", "x", "y", "occupant", "owner", "loot", "scavenge", "stored", "storedKind", "look", "table"];
    static readonly JsonNode?[] FURNITURE_DEFAULTS = [null, null, null, null, null, null, 0, false, 0, null, null, null];

    /// <summary>[value, count, value, count, ...].</summary>
    public static JsonArray runLengthEncode(JsonArray values)
    {
        var out_ = new JsonArray();
        for (int i = 0; i < values.Count;)
        {
            int j = i + 1;
            while (j < values.Count && JsonNode.DeepEquals(values[j], values[i])) j++;
            out_.Add(values[i]?.DeepClone());
            out_.Add((double)(j - i)); // a JS number
            i = j;
        }
        return out_;
    }

    public static JsonArray runLengthDecode(JsonArray encoded)
    {
        var out_ = new JsonArray();
        for (int k = 0; k + 1 < encoded.Count; k += 2)
        {
            var c = encoded[k + 1]!.AsValue();
            double count = c.TryGetValue<double>(out var cd) ? cd : c.TryGetValue<int>(out var ci) ? ci : 0;
            for (int n = 0; n < count; n++) out_.Add(encoded[k]?.DeepClone());
        }
        return out_;
    }

    /// <summary>
    /// Furniture as rows: each piece's values in FURNITURE_FIELDS order, trailing defaults dropped (never below four).
    /// A piece whose keys aren't exactly those (in JSON, where a null field is left out: so any subset in that order) stays an object.
    /// </summary>
    static JsonObject packFurniture(JsonArray list)
    {
        var rows = new JsonArray();
        foreach (var node in list)
        {
            if (node is not JsonObject f || f.Any(p => Array.IndexOf(FURNITURE_FIELDS, p.Key) < 0))
            {
                rows.Add(node?.DeepClone());
                continue;
            }
            var row = new List<JsonNode?>();
            foreach (var k in FURNITURE_FIELDS) row.Add(f.TryGetPropertyValue(k, out var v) ? v?.DeepClone() : null);
            while (row.Count > 4 && JsonNode.DeepEquals(row[^1], FURNITURE_DEFAULTS[row.Count - 1])) row.RemoveAt(row.Count - 1);
            rows.Add(new JsonArray(row.ToArray()));
        }
        return new JsonObject { ["rows"] = rows };
    }

    static JsonArray unpackFurniture(JsonObject packed)
    {
        var out_ = new JsonArray();
        foreach (var r in packed["rows"]?.AsArray() ?? [])
        {
            if (r is not JsonArray row)
            {
                out_.Add(r?.DeepClone());
                continue;
            }
            var f = new JsonObject();
            for (int i = 0; i < FURNITURE_FIELDS.Length; i++)
            {
                if (i >= row.Count && i < 4) continue;
                f[FURNITURE_FIELDS[i]] = i < row.Count ? row[i]?.DeepClone() : FURNITURE_DEFAULTS[i]?.DeepClone();
            }
            out_.Add(f);
        }
        return out_;
    }

    static JsonObject ToNode(SimState state) => JsonSerializer.SerializeToNode(state, StateJson.Options)!.AsObject();

    /// <summary>The state as JSON with the grid's layers run-length encoded (marked `rle: true`) and the furniture as rows.</summary>
    static JsonObject pack(SimState state)
    {
        var o = ToNode(state);
        var grid = o["grid"]!.AsObject();
        grid["rle"] = true;
        foreach (var layer in LAYERS)
            if (grid[layer] is JsonArray a) grid[layer] = runLengthEncode(a);
        if (o["furniture"] is JsonArray furniture) o["furniture"] = packFurniture(furniture);
        return o;
    }

    /// <summary>Undoes pack (and passes unpacked states through).</summary>
    static SimState unpack(JsonNode packed)
    {
        var o = packed.DeepClone().AsObject();
        if (o["furniture"] is JsonObject rows) o["furniture"] = unpackFurniture(rows);
        if (o["grid"] is JsonObject grid && grid["rle"] is JsonValue rle && rle.TryGetValue<bool>(out var isRle) && isRle)
        {
            grid.Remove("rle");
            foreach (var layer in LAYERS)
                if (grid[layer] is JsonArray a) grid[layer] = runLengthDecode(a);
        }
        return o.Deserialize<SimState>(StateJson.Options) ?? throw new InvalidDataException("not a sim state");
    }

    public static string toSaveFile(SimState state)
    {
        var snapshot = pack(state);
        snapshot["commandLog"] = new JsonArray();
        var file = new JsonObject { ["format"] = "loonieroo-save", ["schemaVersion"] = States.SCHEMA_VERSION, ["state"] = snapshot };
        return file.ToJsonString(StateJson.Options);
    }

    /// <summary>`initial` is the state play started from; the report carries only the commands logged since then.</summary>
    public static string toBugReport(SimState initial, SimState state, string note = "")
    {
        var commands = JsonSerializer.SerializeToNode(state.commandLog.slice(initial.commandLog.length), StateJson.Options);
        var report = new JsonObject
        {
            ["format"] = "loonieroo-bug",
            ["schemaVersion"] = States.SCHEMA_VERSION,
            ["note"] = note,
            ["initial"] = pack(initial),
            ["commands"] = commands,
            ["tick"] = state.tick,
        };
        return report.ToJsonString(StateJson.Options);
    }

    static LoadResult Fail(string error) => new() { ok = false, error = error };

    /// <summary>Reads a save or a bug report (replaying the latter). Never throws.</summary>
    public static LoadResult loadFile(string text)
    {
        JsonNode? data;
        try
        {
            data = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return Fail("not a JSON file");
        }
        if (data is not JsonObject file || !file.ContainsKey("format")) return Fail("not a LoonieRoo file");
        var format = file["format"] is JsonValue fv && fv.TryGetValue<string>(out var fs) ? fs : null;
        if (format != "loonieroo-save" && format != "loonieroo-bug") return Fail("not a LoonieRoo file");
        var version = file["schemaVersion"] is JsonValue sv && sv.TryGetValue<double>(out var v) ? v : (double?)null;
        if (version != States.SCHEMA_VERSION)
            return Fail($"made by a different version of the game (schema {(file["schemaVersion"] is null ? "undefined" : file["schemaVersion"]!.ToJsonString())}, this is {Js.Str(States.SCHEMA_VERSION)})");
        if (format == "loonieroo-save")
        {
            if (file["state"] is not JsonObject s) return Fail("save has no state");
            try
            {
                return new() { ok = true, state = States.cloneState(unpack(s)), kind = "save" };
            }
            catch (Exception err) when (err is JsonException or InvalidDataException)
            {
                return Fail($"save did not load: {err.Message}");
            }
        }
        if (file["initial"] is not JsonObject init || file["commands"] is not JsonArray cmds || file["tick"] is not JsonValue tv || !tv.TryGetValue<double>(out var tick))
            return Fail("bug report is incomplete");
        try
        {
            var initial = States.cloneState(unpack(init));
            var log = cmds.Deserialize<List<LoggedCommand>>(StateJson.Options) ?? [];
            return new() { ok = true, state = Replay.replay(initial, log, tick), kind = "bug" };
        }
        catch (Exception err)
        {
            return Fail($"bug report did not replay: {err.Message}");
        }
    }
}
