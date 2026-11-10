// Save files and bug reports (savefile.ts), ported by hand: the TypeScript reshapes plain JSON objects. The files are
// the same as the TypeScript writes.
//
// A save is the whole state but its command log; a bug report is a starting state plus the commands applied since,
// so loading it replays the game to the moment it was filed. Files from a different schema version are refused.
// The grid's per-tile layers are run-length encoded ([value, count, ...], `rle: true`), and furniture goes out as rows
// of values in FURNITURE_FIELDS order with trailing defaults dropped. Files without either encoding still load.
// Files are written straight to text (SaveWriter, 2026-11-10); reading still goes through JSON nodes.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoonieRoo.Sim;

public static class Savefile
{
    /// <summary>The oldest schema a file may have and still load: 77 is 78 without WorldInfo.made, which loading makes.</summary>
    public const int OLDEST_SCHEMA = 77;

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
    /// One piece as its row: its values in FURNITURE_FIELDS order, trailing defaults dropped (never below four). A piece
    /// whose keys aren't among those (in JSON, where a null field is left out: so any subset) stays an object. The
    /// format's definition on nodes; saves are written by writeFurnitureRow, which the tests hold to this.
    /// </summary>
    public static JsonNode? furnitureRow(JsonNode? node)
    {
        if (node is not JsonObject f || f.Any(p => Array.IndexOf(FURNITURE_FIELDS, p.Key) < 0)) return node?.DeepClone();
        var row = new List<JsonNode?>();
        foreach (var k in FURNITURE_FIELDS) row.Add(f.TryGetPropertyValue(k, out var v) ? v?.DeepClone() : null);
        while (row.Count > 4 && JsonNode.DeepEquals(row[^1], FURNITURE_DEFAULTS[row.Count - 1])) row.RemoveAt(row.Count - 1);
        return new JsonArray(row.ToArray());
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

    /// <summary>One piece as its row (see furnitureRow), written: the same text, from the piece's fields.</summary>
    public static void writeFurnitureRow(Utf8JsonWriter w, Furniture f)
    {
        // A field Furniture has beyond FURNITURE_FIELDS, set on this piece: an object, as furnitureRow leaves it.
        foreach (var p in ExtraFurnitureFields)
            if (p.Get(f) != null)
            {
                JsonSerializer.Serialize(w, f, StateJson.Options);
                return;
            }
        int last = 11;
        while (last >= 4 && last switch
        {
            4 => f.occupant == null,
            5 => f.owner == null,
            6 => f.loot == 0 && !double.IsNegative(f.loot),
            7 => !f.scavenge,
            8 => f.stored == 0 && !double.IsNegative(f.stored),
            9 => f.storedKind == null,
            10 => f.look == null,
            _ => f.table == null,
        }) last--;
        void Str(string? v) { if (v == null) w.WriteNullValue(); else w.WriteStringValue(v); }
        w.WriteStartArray();
        Str(f.id);
        Str(f.kind);
        w.WriteNumberValue(f.x);
        w.WriteNumberValue(f.y);
        if (last >= 4) Str(f.occupant);
        if (last >= 5) Str(f.owner);
        if (last >= 6) SaveWriter.Double(w, f.loot);
        if (last >= 7) w.WriteBooleanValue(f.scavenge);
        if (last >= 8) SaveWriter.Double(w, f.stored);
        if (last >= 9) Str(f.storedKind);
        if (last >= 10) Str(f.look);
        if (last >= 11) Str(f.table);
        w.WriteEndArray();
    }

    /// <summary>Furniture's serialised members that aren't FURNITURE_FIELDS (none, today).</summary>
    static readonly SaveWriter.Member[] ExtraFurnitureFields =
        SaveWriter.Props(typeof(Furniture)).Where(p => Array.IndexOf(FURNITURE_FIELDS, p.Name) < 0).ToArray();

    /// <summary>Whether Furniture's serialised members are FURNITURE_FIELDS and no more: a piece is then its fields
    /// (SavefileChunks hashes them straight; a field added to Furniture makes this false, and it hashes the JSON).</summary>
    public static readonly bool furnitureFieldsOnly = ExtraFurnitureFields.Length == 0 && SaveWriter.Props(typeof(Furniture)).Length == FURNITURE_FIELDS.Length;

    /// <summary>Writes the state as a save's JSON (its command log left empty if `emptyLog`): a generated world keeping
    /// only what changed (SavefileChunks); a hand-made map with the grid's layers run-length encoded (marked `rle: true`)
    /// and the furniture as rows.</summary>
    static void WriteState(Utf8JsonWriter w, SimState state, bool emptyLog)
    {
        if (state.world != null)
        {
            SavefileChunks.write(w, state, emptyLog, writeFurnitureRow);
            return;
        }
        w.WriteStartObject();
        SaveWriter.Members(w, state, (name) =>
        {
            switch (name)
            {
                case "grid":
                    var g = state.grid;
                    w.WritePropertyName(name);
                    w.WriteStartObject();
                    SaveWriter.Members(w, g, (layer) =>
                    {
                        switch (layer)
                        {
                            case "tiles": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.tiles)); return true;
                            case "ground": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.ground)); return true;
                            case "explored": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.explored)); return true;
                            case "dirt": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.dirt)); return true;
                            case "damage": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.damage)); return true;
                            case "edges": w.WritePropertyName(layer); SaveWriter.Rle(w, SaveWriter.Span(g.edges)); return true;
                        }
                        return false;
                    });
                    w.WriteBoolean("rle", true);
                    w.WriteEndObject();
                    return true;
                case "furniture":
                    w.WritePropertyName(name);
                    w.WriteStartObject();
                    w.WritePropertyName("rows");
                    w.WriteStartArray();
                    foreach (var f in state.furniture) writeFurnitureRow(w, f);
                    w.WriteEndArray();
                    w.WriteEndObject();
                    return true;
                case "commandLog" when emptyLog:
                    SaveWriter.EmptyArray(w, name);
                    return true;
            }
            return false;
        });
        w.WriteEndObject();
    }

    /// <summary>Undoes WriteState (and passes unpacked states through). Takes the node over: it's changed.</summary>
    static SimState unpack(JsonObject o)
    {
        if (SavefileChunks.IsChunked(o))
            return SavefileChunks.unpack(o, (rows) => unpackFurniture(new JsonObject { ["rows"] = rows.DeepClone() }));
        if (o["furniture"] is JsonObject rows) o["furniture"] = unpackFurniture(rows);
        // The run-length encoded layers are taken out and decoded straight into the grid's lists (not a node a tile).
        var encoded = new Dictionary<string, JsonArray>();
        if (o["grid"] is JsonObject grid && grid["rle"] is JsonValue rle && rle.TryGetValue<bool>(out var isRle) && isRle)
        {
            grid.Remove("rle");
            foreach (var layer in LAYERS)
                if (grid[layer] is JsonArray a)
                {
                    encoded[layer] = a;
                    grid[layer] = new JsonArray();
                }
        }
        var state = o.Deserialize<SimState>(StateJson.Options) ?? throw new InvalidDataException("not a sim state");
        var g = state.grid;
        foreach (var (layer, a) in encoded)
            switch (layer)
            {
                case "tiles": g.tiles = Decoded(a, (v) => v?.GetValue<string>()!); break;
                case "ground": g.ground = Decoded(a, (v) => (int)number(v)); break;
                case "explored": g.explored = Decoded(a, (v) => (int)number(v)); break;
                case "dirt": g.dirt = Decoded(a, (v) => (int)number(v)); break;
                case "damage": g.damage = Decoded(a, number); break;
                case "edges": g.edges = Decoded(a, (v) => (int)number(v)); break;
            }
        return state;
    }

    /// <summary>A number in a save: a JSON number, or NaN or an infinity as its named string.</summary>
    public static double number(JsonNode? n)
    {
        var v = n!.AsValue();
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return v.Deserialize<double>(StateJson.Options);
    }

    /// <summary>Each run of an encoded layer ([value, count, ...]), in order: its value and count.</summary>
    public static void forRuns(JsonArray encoded, Action<JsonNode?, int> run)
    {
        for (int k = 0; k + 1 < encoded.Count; k += 2) run(encoded[k], (int)number(encoded[k + 1]));
    }

    /// <summary>An encoded layer decoded into a list, each run's value converted once.</summary>
    static List<T> Decoded<T>(JsonArray encoded, Func<JsonNode?, T> value)
    {
        int total = 0;
        forRuns(encoded, (_, count) => total += Math.Max(0, count));
        var out_ = new List<T>(total);
        forRuns(encoded, (v, count) =>
        {
            var x = value(v);
            for (int n = 0; n < count; n++) out_.Add(x);
        });
        return out_;
    }

    public static string toSaveFile(SimState state) => SaveWriter.Text((w) =>
    {
        w.WriteStartObject();
        w.WriteString("format", "loonieroo-save");
        w.WriteNumber("schemaVersion", States.SCHEMA_VERSION);
        // The Load screen's picture of the world (WorldPreview), ahead of the state so readPreview stops before it.
        if (WorldPreview.ofSave(state) is { } preview)
        {
            w.WritePropertyName("preview");
            JsonSerializer.Serialize(w, preview, StateJson.Options);
        }
        w.WritePropertyName("state");
        WriteState(w, state, emptyLog: true);
        w.WriteEndObject();
    });

    /// <summary>A save's picture (WorldPreview.ofSave), read from the head of the file without the state; null if it has
    /// none (a hand-made map, a bug report, an older save) or isn't a save. Never throws.</summary>
    public static SavePreview? readPreview(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (name == "state") return null; // past where the preview goes
                if (name == "preview") return JsonSerializer.Deserialize<SavePreview>(ref reader, StateJson.Options);
                reader.Skip();
            }
            return null;
        }
        catch (Exception err) when (err is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Where a bug report starts: the state play started from, packed as a save packs it (a generated world as
    /// its changes: SavefileChunks), and how many commands its log held then. Kept instead of a copy of the state (at
    /// 10,000 a copy is the whole grid again; this is a fraction of a megabyte).</summary>
    public sealed class BugStart
    {
        internal readonly byte[] json;
        public readonly int commands;

        internal BugStart(byte[] json, int commands)
        {
            this.json = json;
            this.commands = commands;
        }

        /// <summary>The packed state's size in bytes.</summary>
        public int Length => json.Length;
    }

    /// <summary>The state as a bug report's start (taken when play starts, and again when the command log is cut).</summary>
    public static BugStart bugStart(SimState state) =>
        new(SaveWriter.Bytes((w) => WriteState(w, state, emptyLog: false)), state.commandLog.Count);

    /// <summary>`initial` is the state play started from; the report carries only the commands logged since then.</summary>
    public static string toBugReport(SimState initial, SimState state, string note = "") => toBugReport(bugStart(initial), state, note);

    public static string toBugReport(BugStart initial, SimState state, string note = "") => SaveWriter.Text((w) =>
    {
        w.WriteStartObject();
        w.WriteString("format", "loonieroo-bug");
        w.WriteNumber("schemaVersion", States.SCHEMA_VERSION);
        w.WriteString("note", note);
        w.WritePropertyName("initial");
        w.WriteRawValue(initial.json, skipInputValidation: true);
        w.WritePropertyName("commands");
        JsonSerializer.Serialize(w, state.commandLog.slice(initial.commands), StateJson.Options);
        w.WriteNumber("tick", state.tick);
        w.WriteEndObject();
    });

    static LoadResult Fail(string error) => new() { ok = false, error = error };

    /// <summary>A state from an older schema as this one has it: 77 lacks only WorldInfo.made, which unpack makes.</summary>
    static SimState Upgraded(SimState state, bool older)
    {
        if (older) state.schemaVersion = States.SCHEMA_VERSION;
        return state;
    }

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
        // An older schema loads only if the state inside says the same (a file relabelled by hand isn't that version).
        var inner = (format == "loonieroo-save" ? file["state"] : file["initial"]) is JsonObject so && so["schemaVersion"] is JsonValue iv
            && iv.TryGetValue<double>(out var ivd) ? ivd : (double?)null;
        bool older = version >= OLDEST_SCHEMA && version < States.SCHEMA_VERSION && inner == version;
        if (version != States.SCHEMA_VERSION && !older)
            return Fail($"made by a different version of the game (schema {(file["schemaVersion"] is null ? "undefined" : file["schemaVersion"]!.ToJsonString())}, this is {Js.Str(States.SCHEMA_VERSION)})");
        if (format == "loonieroo-save")
        {
            if (file["state"] is not JsonObject s) return Fail("save has no state");
            try
            {
                // unpack builds a state of its own (no copy needed: at 10,000 a copy through one JSON string doesn't fit).
                return new() { ok = true, state = Upgraded(unpack(s), older), kind = "save" };
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
            // unpack builds a state of its own, so it's played on in place (no copy: Replay.replayFrom).
            var initial = Upgraded(unpack(init), older);
            var log = cmds.Deserialize<List<LoggedCommand>>(StateJson.Options) ?? [];
            return new() { ok = true, state = Replay.replayFrom(initial, log, tick), kind = "bug" };
        }
        catch (Exception err)
        {
            return Fail($"bug report did not replay: {err.Message}");
        }
    }
}
