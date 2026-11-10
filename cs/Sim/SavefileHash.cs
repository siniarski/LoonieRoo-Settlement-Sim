// A chunk's hash as generation made it (2026-11-10, schema 78): WorldInfo.made. Saving compares each generated chunk's
// hash now with it and makes the chunk again (to find what differs) only where they differ; before, every generated
// chunk was made again at every save (5.6 s for a fully explored 2048).
//
// The hash covers what a chunk save compares against generation: the chunk's tiles, ground and edges, and its
// furniture and dormant (those named after it, "f{cx}.{cy}.{k}", in list order): every field each has in JSON. It's 64 bits, as 16
// hex digits; a collision would save a changed chunk as unchanged, at odds of about 1 in 2^64 a changed chunk.
// It's made when a chunk is generated (Worldgen.generateChunk) and again when a save is loaded (from the chunks
// loading makes anyway), so it always matches this build's generation; a schema-77 save gets its hashes that way.
using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LoonieRoo.Sim;

public static partial class SavefileChunks
{
    /// <summary>A 64-bit hash, fed values in order (a multiply-rotate mix, finished as MurmurHash3's fmix64).</summary>
    struct Hasher
    {
        ulong h;

        public void Add(ulong v) => h = BitOperations.RotateLeft(h ^ (v * 0x9E3779B97F4A7C15UL), 29) * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;

        public void Add(string? s)
        {
            if (s == null) { Add(ulong.MaxValue); return; }
            Add((ulong)s.Length);
            foreach (char ch in s) Add(ch);
        }

        public void Add(ReadOnlySpan<byte> bytes)
        {
            Add((ulong)bytes.Length);
            int i = 0;
            for (; i + 8 <= bytes.Length; i += 8) Add(MemoryMarshal.Read<ulong>(bytes[i..]));
            ulong tail = 0;
            for (int k = 0; i < bytes.Length; i++, k += 8) tail |= (ulong)bytes[i] << k;
            Add(tail);
        }

        public readonly ulong Value
        {
            get
            {
                ulong x = h;
                x ^= x >> 33; x *= 0xFF51AFD7ED558CCDUL;
                x ^= x >> 33; x *= 0xC4CEB9FE1A85EC53UL;
                return x ^ (x >> 33);
            }
        }
    }

    /// <summary>A piece's JSON, for hashing and comparing: written into `buffer` (cleared first).</summary>
    static ReadOnlySpan<byte> Json<T>(ArrayBufferWriter<byte> buffer, T value)
    {
        buffer.ResetWrittenCount();
        using (var w = SaveWriter.Writer(buffer)) JsonSerializer.Serialize(w, value, StateJson.Options);
        return buffer.WrittenSpan;
    }

    /// <summary>A chunk's tiles, ground and edges in the world's grid (or a chunk's own), x0, y0 its corner on the map.</summary>
    static ulong GridHash(Grid g, int x0, int y0, int w, int h)
    {
        var hs = new Hasher();
        hs.Add((ulong)w);
        hs.Add((ulong)h);
        // A tile's kind hashed once per string (a grid holds a handful of them, shared), by reference.
        var kinds = new (string? s, ulong code)[8];
        int next = 0;
        ulong Code(string? t)
        {
            foreach (var (s, code) in kinds) if (s != null && ReferenceEquals(s, t)) return code;
            var k = new Hasher();
            k.Add(t);
            kinds[next++ % kinds.Length] = (t, k.Value);
            return k.Value;
        }
        for (int ly = 0; ly < h; ly++)
            for (int lx = 0; lx < w; lx++)
            {
                int i = (y0 + ly - g.oy) * g.width + (x0 + lx - g.ox);
                hs.Add(Code(g.tiles[i]));
                hs.Add((ulong)(uint)g.ground[i] << 32 | (uint)g.edges[2 * i]);
                hs.Add((uint)g.edges[2 * i + 1]);
            }
        return hs.Value;
    }

    /// <summary>A piece of furniture into a hash: from its fields when Furniture has just the save row's
    /// (Savefile.furnitureFieldsOnly), else from its JSON.</summary>
    static void AddPiece(ref Hasher hs, Furniture f, ArrayBufferWriter<byte> buffer)
    {
        if (!Savefile.furnitureFieldsOnly)
        {
            hs.Add(Json(buffer, f));
            return;
        }
        hs.Add(f.id);
        hs.Add(f.kind);
        hs.Add((ulong)(uint)f.x << 32 | (uint)f.y);
        hs.Add(f.occupant);
        hs.Add(f.owner);
        hs.Add((ulong)BitConverter.DoubleToInt64Bits(f.loot));
        hs.Add(f.scavenge ? 1UL : 0UL);
        hs.Add((ulong)BitConverter.DoubleToInt64Bits(f.stored));
        hs.Add(f.storedKind);
        hs.Add(f.look);
        hs.Add(f.table);
    }

    static string Final(ulong grid, int furniture, ulong furnitureHash, int dormant, ulong dormantHash)
    {
        var hs = new Hasher();
        hs.Add(grid);
        hs.Add((ulong)furniture);
        hs.Add(furnitureHash);
        hs.Add((ulong)dormant);
        hs.Add(dormantHash);
        return hs.Value.ToString("x16");
    }

    /// <summary>A chunk's hash as made (c: Worldgen.makeChunk's state for it).</summary>
    public static string madeHash(SimState c)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        var g = c.grid;
        var f = new Hasher();
        foreach (var piece in c.furniture) AddPiece(ref f, piece, buffer);
        var d = new Hasher();
        foreach (var z in c.dormant) d.Add(Json(buffer, z));
        return Final(GridHash(g, g.ox, g.oy, g.width, g.height), c.furniture.Count, f.Value, c.dormant.Count, d.Value);
    }

    /// <summary>Notes a chunk's hash as made in the world (Worldgen.generateChunk calls it with what makeChunk made).</summary>
    public static void noteMade(SimState state, SimState c, int cx, int cy)
    {
        if (state.world is not { } world) return;
        world.made[Js.Str(cy * (int)world.chunks + cx)] = madeHash(c);
    }

    /// <summary>Which generated chunks are as made, by index: their hash now is the one noted when they were made.
    /// Unknown (no hash noted) counts as changed.</summary>
    static bool[] AsMade(SimState state, ArrayBufferWriter<byte> buffer)
    {
        var g = state.grid;
        var world = state.world!;
        int n = (int)world.chunks, size = g.width;
        var asMade = new bool[n * n];
        var f = new Hasher[n * n];
        var d = new Hasher[n * n];
        var fCount = new int[n * n];
        var dCount = new int[n * n];
        bool Generated(int index) => Js.Get(world.generated, index) == 1 && world.made.ContainsKey(Js.Str(index));
        int ChunkOf(string id)
        {
            if (ParseId(id) is not { } p || p.cx >= n || p.cy >= n) return -1;
            int index = p.cy * n + p.cx;
            return Generated(index) ? index : -1;
        }
        foreach (var piece in state.furniture)
            if (ChunkOf(piece.id) is var i and >= 0) { AddPiece(ref f[i], piece, buffer); fCount[i]++; }
        foreach (var z in state.dormant)
            if (ChunkOf(z.id) is var i and >= 0) { d[i].Add(Json(buffer, z)); dCount[i]++; }
        for (int index = 0; index < n * n; index++)
        {
            if (!Generated(index)) continue;
            int x0 = index % n * CHUNK, y0 = index / n * CHUNK;
            var now = Final(GridHash(g, x0, y0, Math.Min(CHUNK, size - x0), Math.Min(CHUNK, size - y0)), fCount[index], f[index].Value, dCount[index], d[index].Value);
            asMade[index] = now == world.made[Js.Str(index)];
        }
        return asMade;
    }

    /// <summary>The generated chunks a save makes again to compare, by index: those no longer as made, or with no hash noted.</summary>
    public static List<int> changed(SimState state)
    {
        var asMade = AsMade(state, new ArrayBufferWriter<byte>(256));
        var out_ = new List<int>();
        for (int i = 0; i < asMade.Length; i++)
            if (Js.Get(state.world!.generated, i) == 1 && !asMade[i]) out_.Add(i);
        return out_;
    }
}
