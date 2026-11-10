// Saves of generated worlds keep only what changed (2026-11-09, schema 75; NOTES.md, docs/worldplan-regions.md).
//
// A chunk comes out of generation the same whenever it's made (Worldgen.makeChunk: from the seed, the plan and the
// world's settings, into its own small state), so the save doesn't hold it. It holds:
//   - the state without the grid's per-tile layers, furniture and the dormant (the grid's other lists, open doors,
//     locks, damage on edges..., stay in the state as they always were);
//   - "chunks": for each chunk where a layer differs from what it would be (made again if it was generated; bare land,
//     floor and grass and nothing seen, if not), that chunk's differing layers, run-length encoded, row by row;
//   - "furniture" and "dormant": the lists in their order, as runs of pieces still as their chunk made them
//     ({ c: chunk index, k: first, n: how many }, by the place in the chunk's own list), and everything else in full
//     (furniture as rows, as before).
// A generated chunk whose hash now is its hash as made (WorldInfo.made, schema 78: SavefileHash.cs) is known to be as
// made without making it again; only the others are made again to find what differs (2026-11-10).
// Loading makes every generated chunk again, lays the differences over it, and rebuilds the lists from the runs.
// Hand-made maps (no world) are saved whole, as before. The save is written straight to text (SaveWriter).
//
// For the grid in chunks (still to do): the save reads the world's grid only in Differs, WriteLayer and GridHash (a
// chunk's values, by map tile) and writes it only in unpack; each would read or write the chunk's own lists instead.
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoonieRoo.Sim;

public static partial class SavefileChunks
{
    const int CHUNK = Worldgen.CHUNK;

    /// <summary>The layers a chunk's difference can hold, and what a bare (never generated) tile has in each.</summary>
    static readonly string[] LAYERS = ["tiles", "ground", "edges", "dirt", "damage", "explored"];

    sealed class Made
    {
        public readonly Dictionary<int, SimState> chunks = new();
        Layout? layout;

        public SimState Get(SimState state, int index)
        {
            if (chunks.TryGetValue(index, out var c)) return c;
            var world = state.world!;
            int n = (int)world.chunks;
            layout ??= Worldgen.layout(world.seed, world.size, state.settings.map);
            c = Worldgen.makeChunk(state, layout, index % n, index / n);
            chunks[index] = c;
            return c;
        }
    }

    /// <summary>A number from JSON (Savefile.number).</summary>
    static double Num(JsonNode? n) => Savefile.number(n);

    /// <summary>A generated piece's chunk and place in it, from its id ("f{cx}.{cy}.{k}", "d...", "v..."); null for any other id.</summary>
    static (int cx, int cy, int k)? ParseId(string id)
    {
        if (id.Length < 6 || id[0] is not ('f' or 'd' or 'v')) return null;
        int at = 1;
        bool Number(out int value, bool last)
        {
            value = 0;
            int from = at;
            while (at < id.Length && id[at] is >= '0' and <= '9' && at - from < 9) value = value * 10 + (id[at++] - '0');
            if (at == from || (at < id.Length && id[at] is >= '0' and <= '9')) return false;
            if (last) return at == id.Length;
            if (at >= id.Length || id[at] != '.') return false;
            at++;
            return true;
        }
        if (!Number(out int cx, false) || !Number(out int cy, false) || !Number(out int k, true)) return null;
        return (cx, cy, k);
    }

    /// <summary>A chunk's values of one layer, row by row, from the world's grid, run-length encoded: an entry of a
    /// chunk save.</summary>
    static void WriteLayer(Utf8JsonWriter w, Grid g, string layer, int x0, int y0, int wd, int h, Scratch s)
    {
        int count = layer == "edges" ? 2 * wd * h : wd * h, v = 0;
        s.Fit(count);
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < wd; lx++)
            {
                int i = (y0 + ly) * g.width + x0 + lx;
                switch (layer)
                {
                    case "tiles": s.strings[v++] = g.tiles[i]; break;
                    case "ground": s.ints[v++] = g.ground[i]; break;
                    case "dirt": s.ints[v++] = g.dirt[i]; break;
                    case "damage": s.doubles[v++] = g.damage[i]; break;
                    case "explored": s.ints[v++] = g.explored[i]; break;
                    case "edges": s.ints[v++] = g.edges[2 * i]; s.ints[v++] = g.edges[2 * i + 1]; break;
                }
            }
        w.WritePropertyName(layer);
        switch (layer)
        {
            case "tiles": SaveWriter.Rle(w, s.strings.AsSpan(0, count)); break;
            case "damage": SaveWriter.Rle(w, s.doubles.AsSpan(0, count)); break;
            default: SaveWriter.Rle(w, s.ints.AsSpan(0, count)); break;
        }
    }

    /// <summary>A save's reusable buffers: a chunk layer's values, and two pieces' JSON to compare.</summary>
    sealed class Scratch
    {
        public int[] ints = [];
        public double[] doubles = [];
        public string[] strings = [];
        public readonly ArrayBufferWriter<byte> a = new(256), b = new(256);

        public void Fit(int count)
        {
            if (ints.Length >= count) return;
            ints = new int[count];
            doubles = new double[count];
            strings = new string[count];
        }
    }

    /// <summary>Whether a chunk's layer in the world's grid differs from its chunk's own grid (c), or from bare land
    /// (c null): straight on the values, a row at a time (most of a big world is bare and unseen: a vector scan).</summary>
    static bool Differs(Grid g, Grid? c, string layer, int x0, int y0, int w, int h)
    {
        for (int ly = 0; ly < h; ly++)
        {
            int i = (y0 + ly) * g.width + x0;
            int j = c == null ? -1 : (y0 + ly - c.oy) * c.width + (x0 - c.ox);
            bool differs = layer switch
            {
                "tiles" => c == null ? SaveWriter.Span(g.tiles).Slice(i, w).IndexOfAnyExcept(Grids.FLOOR) >= 0
                    : !SaveWriter.Span(g.tiles).Slice(i, w).SequenceEqual(SaveWriter.Span(c.tiles).Slice(j, w)),
                "ground" => c == null ? SaveWriter.Span(g.ground).Slice(i, w).IndexOfAnyExcept(0) >= 0
                    : !SaveWriter.Span(g.ground).Slice(i, w).SequenceEqual(SaveWriter.Span(c.ground).Slice(j, w)),
                "edges" => c == null ? SaveWriter.Span(g.edges).Slice(2 * i, 2 * w).IndexOfAnyExcept(Grids.EDGE_NONE) >= 0
                    : !SaveWriter.Span(g.edges).Slice(2 * i, 2 * w).SequenceEqual(SaveWriter.Span(c.edges).Slice(2 * j, 2 * w)),
                "dirt" => SaveWriter.Span(g.dirt).Slice(i, w).IndexOfAnyExcept(0) >= 0,
                "explored" => SaveWriter.Span(g.explored).Slice(i, w).IndexOfAnyExcept(0) >= 0,
                "damage" => NonZero(SaveWriter.Span(g.damage).Slice(i, w)),
                _ => false,
            };
            if (differs) return true;
        }
        return false;
    }

    /// <summary>Any value != 0 (NaN is; -0 isn't, as before).</summary>
    static bool NonZero(ReadOnlySpan<double> v)
    {
        foreach (var d in v) if (d != 0) return true;
        return false;
    }

    /// <summary>Writes a world state as a save's JSON (see the file's head), its command log left empty if `emptyLog`.
    /// `furnitureRow` writes one piece as the save's row.</summary>
    public static void write(Utf8JsonWriter w, SimState state, bool emptyLog, Action<Utf8JsonWriter, Furniture> furnitureRow)
    {
        var g = state.grid;
        var world = state.world!;
        int size = g.width, n = (int)world.chunks;
        var made = new Made();
        var scratch = new Scratch();
        // The chunks still as generation made them (their hash says so) aren't made again to compare.
        var asMade = AsMade(state, scratch.a);

        w.WriteStartObject();
        SaveWriter.Members(w, state, (name) =>
        {
            switch (name)
            {
                // The grid without its per-tile layers (in "chunks", below).
                case "grid":
                    w.WritePropertyName(name);
                    w.WriteStartObject();
                    SaveWriter.Members(w, g, (layer) =>
                    {
                        if (Array.IndexOf(LAYERS, layer) < 0) return false;
                        SaveWriter.EmptyArray(w, layer);
                        return true;
                    });
                    w.WriteBoolean("chunked", true);
                    w.WriteEndObject();
                    return true;
                // Furniture and the dormant: runs of pieces as their chunk made them, the rest in full.
                case "furniture":
                    w.WritePropertyName(name);
                    Runs(w, state, made, asMade, scratch, state.furniture, (f) => f.id, (c) => c.furniture, furnitureRow);
                    return true;
                case "dormant":
                    w.WritePropertyName(name);
                    Runs(w, state, made, asMade, scratch, state.dormant, (d) => d.id, (c) => c.dormant,
                        (w, d) => JsonSerializer.Serialize(w, d, StateJson.Options));
                    return true;
                case "commandLog" when emptyLog:
                    SaveWriter.EmptyArray(w, name);
                    return true;
            }
            return false;
        });

        // The grid: each chunk's layers that differ from what generation (or bare land) gives.
        w.WritePropertyName("chunks");
        w.WriteStartArray();
        for (int cy = 0; cy < n; cy++)
            for (int cx = 0; cx < n; cx++)
            {
                int index = cy * n + cx;
                int x0 = cx * CHUNK, y0 = cy * CHUNK, wd = Math.Min(CHUNK, size - x0), h = Math.Min(CHUNK, size - y0);
                bool generated = Js.Get(world.generated, index) == 1;
                bool open = false;
                foreach (var layer in LAYERS)
                {
                    // dirt, damage and what's been seen: generation leaves them all 0, so bare land is the baseline either way.
                    bool fromGeneration = generated && layer is "tiles" or "ground" or "edges";
                    if (fromGeneration && asMade[index]) continue;
                    var baseline = fromGeneration ? made.Get(state, index).grid : null;
                    if (!Differs(g, baseline, layer, x0, y0, wd, h)) continue;
                    if (!open)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("c", index);
                        open = true;
                    }
                    WriteLayer(w, g, layer, x0, y0, wd, h, scratch);
                }
                if (open) w.WriteEndObject();
            }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>A list as runs of pieces still as their chunk made them ({ c, k, n }) and the rest in full.</summary>
    static void Runs<T>(Utf8JsonWriter w, SimState state, Made made, bool[] asMade, Scratch scratch, List<T> list, Func<T, string> idOf,
        Func<SimState, List<T>> madeOf, Action<Utf8JsonWriter, T> full)
    {
        w.WriteStartArray();
        int runChunk = -1, runFirst = -1, runNext = -1;
        void Close()
        {
            if (runChunk < 0) return;
            w.WriteStartObject();
            w.WriteNumber("c", runChunk);
            w.WriteNumber("k", runFirst);
            w.WriteNumber("n", runNext - runFirst);
            w.WriteEndObject();
            runChunk = -1;
        }
        int n = (int)state.world!.chunks;
        foreach (var e in list)
        {
            bool pristine = false;
            int index = -1, k = -1;
            if (ParseId(idOf(e)) is { } id && id.cx < n && id.cy < n && Js.Get(state.world.generated, id.cy * n + id.cx) == 1)
            {
                index = id.cy * n + id.cx;
                k = id.k;
                if (asMade[index]) pristine = true; // the chunk's pieces are all as made, in their order
                else
                {
                    var theirs = madeOf(made.Get(state, index));
                    pristine = k < theirs.Count && Json(scratch.a, e).SequenceEqual(Json(scratch.b, theirs[k]));
                }
            }
            if (!pristine)
            {
                Close();
                full(w, e);
                continue;
            }
            if (runChunk == index && runNext == k)
            {
                runNext++;
                continue;
            }
            Close();
            (runChunk, runFirst, runNext) = (index, k, k + 1);
        }
        Close();
        w.WriteEndArray();
    }

    /// <summary>Whether a packed state is in this form.</summary>
    public static bool IsChunked(JsonObject o) => o["grid"] is JsonObject g && g["chunked"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>Undoes write. `furnitureOf` turns the save's rows back into furniture objects. Takes the node over: it's changed.</summary>
    public static SimState unpack(JsonObject o, Func<JsonArray, JsonArray> furnitureOf)
    {
        var chunks = o["chunks"]?.AsArray() ?? new JsonArray();
        var furnitureSeq = o["furniture"]?.AsArray() ?? new JsonArray();
        var dormantSeq = o["dormant"]?.AsArray() ?? new JsonArray();
        o.Remove("chunks");
        o["furniture"] = new JsonArray();
        o["dormant"] = new JsonArray();
        o["grid"]!.AsObject().Remove("chunked");
        var state = o.Deserialize<SimState>(StateJson.Options) ?? throw new InvalidDataException("not a sim state");
        var g = state.grid;
        int size = g.width, total = g.width * g.height, n = (int)state.world!.chunks;
        g.tiles = Enumerable.Repeat(Grids.FLOOR, total).ToList();
        g.ground = Enumerable.Repeat(0, total).ToList();
        g.edges = Enumerable.Repeat(Grids.EDGE_NONE, 2 * total).ToList();
        g.dirt = Enumerable.Repeat(0, total).ToList();
        g.damage = Enumerable.Repeat(0.0, total).ToList();
        g.explored = Enumerable.Repeat(0, total).ToList();

        // Every generated chunk made again, laid in.
        // Each one's hash as made noted (in the file's order; a schema-77 save has none, so they're added in index order).
        var made = new Made();
        for (int index = 0; index < n * n; index++)
        {
            if (Js.Get(state.world.generated, index) != 1) continue;
            var chunk = made.Get(state, index);
            state.world.made[Js.Str(index)] = madeHash(chunk);
            var c = chunk.grid;
            for (int i = 0; i < c.tiles.Count; i++)
            {
                int at = (c.oy + i / c.width) * size + c.ox + i % c.width;
                g.tiles[at] = c.tiles[i];
                g.ground[at] = c.ground[i];
                g.edges[2 * at] = c.edges[2 * i];
                g.edges[2 * at + 1] = c.edges[2 * i + 1];
            }
        }
        // The differences over it.
        foreach (var node in chunks)
        {
            var entry = node!.AsObject();
            int index = (int)Num(entry["c"]);
            int cx = index % n, cy = index / n, x0 = cx * CHUNK, y0 = cy * CHUNK, w = Math.Min(CHUNK, size - x0), h = Math.Min(CHUNK, size - y0);
            foreach (var layer in LAYERS)
            {
                if (entry[layer] is not JsonArray enc) continue;
                int v = 0;
                // The map tile of the chunk's v-th value (two a tile for edges: north, west).
                int At(int t) => t < w * h ? (y0 + t / w) * size + x0 + t % w : throw new InvalidDataException($"chunk {index}: {layer} runs past the chunk");
                Savefile.forRuns(enc, (value, count) =>
                {
                    switch (layer)
                    {
                        case "tiles": { var x = value?.GetValue<string>()!; for (int k = 0; k < count; k++) g.tiles[At(v++)] = x; break; }
                        case "ground": { int x = (int)Num(value); for (int k = 0; k < count; k++) g.ground[At(v++)] = x; break; }
                        case "dirt": { int x = (int)Num(value); for (int k = 0; k < count; k++) g.dirt[At(v++)] = x; break; }
                        case "damage": { double x = Num(value); for (int k = 0; k < count; k++) g.damage[At(v++)] = x; break; }
                        case "explored": { int x = (int)Num(value); for (int k = 0; k < count; k++) g.explored[At(v++)] = x; break; }
                        case "edges": { int x = (int)Num(value); for (int k = 0; k < count; k++, v++) g.edges[2 * At(v / 2) + v % 2] = x; break; }
                    }
                });
            }
        }

        // The lists from their runs.
        var furniture = new JsonArray();
        foreach (var item in furnitureSeq) if (item is not JsonObject r || !r.ContainsKey("c")) furniture.Add(item?.DeepClone());
        var rows = furnitureOf(furniture).Deserialize<List<Furniture>>(StateJson.Options) ?? [];
        int nextRow = 0;
        foreach (var item in furnitureSeq)
        {
            if (item is JsonObject r && r.ContainsKey("c"))
            {
                var theirs = made.Get(state, (int)Num(r["c"])).furniture;
                int k = (int)Num(r["k"]), count = (int)Num(r["n"]);
                for (int j = k; j < k + count; j++) state.furniture.Add(theirs[j]);
            }
            else state.furniture.Add(rows[nextRow++]);
        }
        foreach (var item in dormantSeq)
        {
            if (item is JsonObject r && r.ContainsKey("c"))
            {
                var theirs = made.Get(state, (int)Num(r["c"])).dormant;
                int k = (int)Num(r["k"]), count = (int)Num(r["n"]);
                for (int j = k; j < k + count; j++) state.dormant.Add(theirs[j]);
            }
            else state.dormant.Add(item.Deserialize<DormantZombie>(StateJson.Options)!);
        }
        return state;
    }
}
