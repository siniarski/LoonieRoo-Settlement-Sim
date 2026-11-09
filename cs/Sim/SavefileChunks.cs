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
// Loading makes every generated chunk again, lays the differences over it, and rebuilds the lists from the runs.
// Hand-made maps (no world) are saved whole, as before.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoonieRoo.Sim;

public static partial class SavefileChunks
{
    const int CHUNK = Worldgen.CHUNK;
    static readonly Regex Generated = new(@"^[fdv](\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    /// <summary>The layers a chunk's difference can hold, and what a bare (never generated) tile has in each.</summary>
    static readonly string[] LAYERS = ["tiles", "ground", "edges", "dirt", "damage", "explored"];

    sealed class Made
    {
        public readonly Dictionary<int, SimState> chunks = new();
        public Layout layout = null!;

        public SimState Get(SimState state, int index)
        {
            if (chunks.TryGetValue(index, out var c)) return c;
            int n = (int)state.world!.chunks;
            c = Worldgen.makeChunk(state, layout, index % n, index / n);
            chunks[index] = c;
            return c;
        }
    }

    /// <summary>A number from JSON, whether parsed from text or made in memory (an int or a double).</summary>
    static double Num(JsonNode? n)
    {
        var v = n!.AsValue();
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return v.GetValue<double>();
    }

    static (int cx, int cy, int k)? ParseId(string id)
    {
        var m = Generated.Match(id);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
    }

    /// <summary>A chunk's values of one layer, row by row: from the world's grid (g) or a chunk's own (c, or null for bare land).</summary>
    static List<JsonNode?> Layer(Grid g, string layer, int x0, int y0, int w, int h)
    {
        var out_ = new List<JsonNode?>(layer == "edges" ? 2 * w * h : w * h);
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
            {
                int i = (y0 + ly - g.oy) * g.width + (x0 + lx - g.ox);
                switch (layer)
                {
                    case "tiles": out_.Add(JsonValue.Create(g.tiles[i])); break;
                    case "ground": out_.Add(JsonValue.Create(g.ground[i])); break;
                    case "dirt": out_.Add(JsonValue.Create(g.dirt[i])); break;
                    case "damage": out_.Add(JsonValue.Create(g.damage[i])); break;
                    case "explored": out_.Add(JsonValue.Create(g.explored[i])); break;
                    case "edges": out_.Add(JsonValue.Create(g.edges[2 * i])); out_.Add(JsonValue.Create(g.edges[2 * i + 1])); break;
                }
            }
        return out_;
    }

    /// <summary>Whether a chunk's layer in the world's grid differs from its chunk's own grid (c), or from bare land
    /// (c null): straight on the values, so a big world's save doesn't make a node per tile to find out.</summary>
    static bool Differs(Grid g, Grid? c, string layer, int x0, int y0, int w, int h)
    {
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
            {
                int i = (y0 + ly) * g.width + x0 + lx;
                int j = c == null ? -1 : (y0 + ly - c.oy) * c.width + (x0 + lx - c.ox);
                switch (layer)
                {
                    case "tiles": if (g.tiles[i] != (c == null ? Grids.FLOOR : c.tiles[j])) return true; break;
                    case "ground": if (g.ground[i] != (c == null ? 0 : c.ground[j])) return true; break;
                    case "edges":
                        if (g.edges[2 * i] != (c == null ? Grids.EDGE_NONE : c.edges[2 * j])) return true;
                        if (g.edges[2 * i + 1] != (c == null ? Grids.EDGE_NONE : c.edges[2 * j + 1])) return true;
                        break;
                    case "dirt": if (g.dirt[i] != 0) return true; break;
                    case "damage": if (g.damage[i] != 0) return true; break;
                    case "explored": if (g.explored[i] != 0) return true; break;
                }
            }
        return false;
    }

    /// <summary>A world state as a save's JSON: see the file's head. `furnitureRow` packs one piece as the save's row.</summary>
    public static JsonObject pack(SimState state, Func<JsonObject, JsonNode> furnitureRow)
    {
        var g = state.grid;
        var world = state.world!;
        int size = g.width, n = (int)world.chunks;
        var made = new Made { layout = Worldgen.layout(world.seed, world.size, state.settings.map) };

        // The state without the big parts: swapped out for the serialisation and put back.
        var (tiles, ground, edges, dirt, damage, explored) = (g.tiles, g.ground, g.edges, g.dirt, g.damage, g.explored);
        var (furniture, dormant) = (state.furniture, state.dormant);
        JsonObject o;
        try
        {
            (g.tiles, g.ground, g.edges, g.dirt, g.damage, g.explored) = (new(), new(), new(), new(), new(), new());
            (state.furniture, state.dormant) = (new(), new());
            o = JsonSerializer.SerializeToNode(state, StateJson.Options)!.AsObject();
        }
        finally
        {
            (g.tiles, g.ground, g.edges, g.dirt, g.damage, g.explored) = (tiles, ground, edges, dirt, damage, explored);
            (state.furniture, state.dormant) = (furniture, dormant);
        }
        o["grid"]!["chunked"] = true;

        // The grid: each chunk's layers that differ from what generation (or bare land) gives.
        var chunks = new JsonArray();
        for (int cy = 0; cy < n; cy++)
            for (int cx = 0; cx < n; cx++)
            {
                int index = cy * n + cx;
                int x0 = cx * CHUNK, y0 = cy * CHUNK, w = Math.Min(CHUNK, size - x0), h = Math.Min(CHUNK, size - y0);
                bool generated = Js.Get(world.generated, index) == 1;
                JsonObject? entry = null;
                foreach (var layer in LAYERS)
                {
                    // dirt, damage and what's been seen: generation leaves them all 0, so bare land is the baseline either way.
                    var baseline = generated && layer is "tiles" or "ground" or "edges" ? made.Get(state, index).grid : null;
                    if (!Differs(g, baseline, layer, x0, y0, w, h)) continue;
                    entry ??= new JsonObject { ["c"] = index };
                    entry[layer] = Savefile.runLengthEncode(new JsonArray(Layer(g, layer, x0, y0, w, h).ToArray()));
                }
                if (entry != null) chunks.Add(entry);
            }
        o["chunks"] = chunks;

        // Furniture and the dormant: runs of pieces as their chunk made them, the rest in full.
        o["furniture"] = Runs(state, made, state.furniture, (f) => f.id, (c) => c.furniture,
            (f) => furnitureRow(JsonSerializer.SerializeToNode(f, StateJson.Options)!.AsObject()));
        o["dormant"] = Runs(state, made, state.dormant, (d) => d.id, (c) => c.dormant,
            (d) => JsonSerializer.SerializeToNode(d, StateJson.Options)!);
        return o;
    }

    static JsonArray Runs<T>(SimState state, Made made, List<T> list, Func<T, string> idOf, Func<SimState, List<T>> madeOf, Func<T, JsonNode> full)
    {
        var out_ = new JsonArray();
        JsonObject? run = null;
        int runChunk = -1, runNext = -1;
        int n = (int)state.world!.chunks;
        foreach (var e in list)
        {
            bool pristine = false;
            int index = -1, k = -1;
            if (ParseId(idOf(e)) is { } id && id.cx < n && id.cy < n && Js.Get(state.world.generated, id.cy * n + id.cx) == 1)
            {
                index = id.cy * n + id.cx;
                k = id.k;
                var theirs = madeOf(made.Get(state, index));
                pristine = k < theirs.Count && JsonNode.DeepEquals(full(e), full(theirs[k]));
            }
            if (!pristine)
            {
                run = null;
                out_.Add(full(e));
                continue;
            }
            if (run != null && runChunk == index && runNext == k)
            {
                run["n"] = (int)Num(run["n"]) + 1;
                runNext++;
                continue;
            }
            run = new JsonObject { ["c"] = index, ["k"] = k, ["n"] = 1 };
            runChunk = index;
            runNext = k + 1;
            out_.Add(run);
        }
        return out_;
    }

    /// <summary>Whether a packed state is in this form.</summary>
    public static bool IsChunked(JsonObject o) => o["grid"] is JsonObject g && g["chunked"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>Undoes pack. `furnitureOf` turns the save's rows back into furniture objects.</summary>
    public static SimState unpack(JsonObject packed, Func<JsonArray, JsonArray> furnitureOf)
    {
        var o = packed.DeepClone().AsObject();
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
        var made = new Made { layout = Worldgen.layout(state.world.seed, state.world.size, state.settings.map) };
        for (int index = 0; index < n * n; index++)
        {
            if (Js.Get(state.world.generated, index) != 1) continue;
            var c = made.Get(state, index).grid;
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
                var values = Savefile.runLengthDecode(enc);
                int v = 0;
                for (int ly = 0; ly < h; ly++)
                    for (int lx = 0; lx < w; lx++)
                    {
                        int i = (y0 + ly) * size + x0 + lx;
                        switch (layer)
                        {
                            case "tiles": g.tiles[i] = values[v++]!.GetValue<string>(); break;
                            case "ground": g.ground[i] = (int)Num(values[v++]); break;
                            case "dirt": g.dirt[i] = (int)Num(values[v++]); break;
                            case "damage": g.damage[i] = Num(values[v++]); break;
                            case "explored": g.explored[i] = (int)Num(values[v++]); break;
                            case "edges": g.edges[2 * i] = (int)Num(values[v++]); g.edges[2 * i + 1] = (int)Num(values[v++]); break;
                        }
                    }
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
