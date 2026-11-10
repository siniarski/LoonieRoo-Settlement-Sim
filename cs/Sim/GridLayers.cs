// The grid's per-tile layers, in chunks (2026-11-10; docs/worldplan-regions.md "The second job").
//
// A layer is an array of chunk slots, each null or an array of 32 x 32 tiles' values (two a tile for edges), and one
// fill value: a null slot reads as the fill. A slot is allocated on the first write of a value that isn't the fill, so
// memory follows what has been generated and changed, not the size of the map. Whether a slot is allocated is never
// observable: only values are. A chunk on the map's east or south edge is a whole array too; the part past the edge
// holds the fill and is never read or written.
//
// Only Grids (Grid.cs) and the save code use these. In JSON a layer is the flat row-major array it always was: it is
// read into `flat` and cut into chunks once the grid knows its size (Grid.OnDeserialized).
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoonieRoo.Sim;

public class GridLayer<T> where T : unmanaged
{
    /// <summary>A chunk's side in tiles (Worldgen.CHUNK and Grids.REACH_CHUNK are the same 32).</summary>
    public const int SIDE = 32, SHIFT = 5, MASK = SIDE - 1;

    /// <summary>Values a tile: 1, or 2 for edges (north, then west).</summary>
    internal readonly int per;
    internal int width, height;
    /// <summary>Chunk columns and rows, and width * height.</summary>
    internal int cw, ch, count;
    /// <summary>By chunk (cy * cw + cx): null where every tile holds `fill`.</summary>
    internal T[]?[] chunks = [];
    internal T fill;
    /// <summary>The layer as JSON had it, until Shape cuts it into chunks.</summary>
    internal T[]? flat;

    internal GridLayer(int per, T fill)
    {
        this.per = per;
        this.fill = fill;
    }

    /// <summary>The same value to the bit (so -0 and 0 are two values, and NaN is itself).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool Same(T a, T b) => typeof(T) == typeof(double)
        ? BitConverter.DoubleToInt64Bits(Unsafe.As<T, double>(ref a)) == BitConverter.DoubleToInt64Bits(Unsafe.As<T, double>(ref b))
        : Unsafe.As<T, byte>(ref a) == Unsafe.As<T, byte>(ref b);

    /// <summary>Sizes the layer (dropping what it held), then lays in what JSON gave, if anything.</summary>
    internal void Shape(int width, int height)
    {
        this.width = width;
        this.height = height;
        cw = (width + MASK) >> SHIFT;
        ch = (height + MASK) >> SHIFT;
        count = width * height;
        chunks = new T[]?[Math.Max(0, cw * ch)];
        if (flat is { } values)
        {
            flat = null;
            Load(values);
        }
    }

    /// <summary>The whole layer from a flat row-major array (short ones leave the rest at the fill).</summary>
    internal void Load(ReadOnlySpan<T> values)
    {
        Array.Clear(chunks);
        int row = width * per;
        for (int y = 0; y < height && y * row < values.Length; y++)
            WriteRow(0, y, values.Slice(y * row, Math.Min(row, values.Length - y * row)));
    }

    T[] Allocate(int c)
    {
        var a = new T[SIDE * SIDE * per];
        if (!Same(fill, default)) Array.Fill(a, fill);
        chunks[c] = a;
        return a;
    }

    // --- One value. x, y are within the grid (the caller checks); per == 1 for these two ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T Get(int x, int y)
    {
        var c = chunks[(y >> SHIFT) * cw + (x >> SHIFT)];
        return c == null ? fill : c[((y & MASK) << SHIFT) | (x & MASK)];
    }

    internal void Set(int x, int y, T value)
    {
        int ci = (y >> SHIFT) * cw + (x >> SHIFT);
        var c = chunks[ci];
        if (c == null)
        {
            if (Same(value, fill)) return;
            c = Allocate(ci);
        }
        c[((y & MASK) << SHIFT) | (x & MASK)] = value;
    }

    /// <summary>By tile index: the caller checks it's on the grid.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T At(int i)
    {
        int y = i / width;
        return Get(i - y * width, y);
    }

    internal void Put(int i, T value)
    {
        if ((uint)i >= (uint)count) throw new ArgumentOutOfRangeException(nameof(i), $"tile index {i} is off the {width} x {height} grid");
        int y = i / width;
        Set(i - y * width, y, value);
    }

    // --- Two values a tile (edges): k is 2 * tile index + side ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T At2(int k)
    {
        int i = k >> 1, y = i / width, x = i - y * width;
        var c = chunks[(y >> SHIFT) * cw + (x >> SHIFT)];
        return c == null ? fill : c[((((y & MASK) << SHIFT) | (x & MASK)) << 1) | (k & 1)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T Get2(int x, int y, int side)
    {
        var c = chunks[(y >> SHIFT) * cw + (x >> SHIFT)];
        return c == null ? fill : c[((((y & MASK) << SHIFT) | (x & MASK)) << 1) | side];
    }

    internal void Put2(int k, T value)
    {
        if ((uint)k >= (uint)(2 * count)) throw new ArgumentOutOfRangeException(nameof(k), $"edge index {k} is off the {width} x {height} grid");
        int i = k >> 1, y = i / width, x = i - y * width;
        int ci = (y >> SHIFT) * cw + (x >> SHIFT);
        var c = chunks[ci];
        if (c == null)
        {
            if (Same(value, fill)) return;
            c = Allocate(ci);
        }
        c[((((y & MASK) << SHIFT) | (x & MASK)) << 1) | (k & 1)] = value;
    }

    // --- Rows and whole layers ---

    /// <summary>Part of a row into `to` (to.Length / per tiles from x, y): chunk by chunk, no per-tile lookups.</summary>
    internal void ReadRow(int x, int y, Span<T> to)
    {
        int n = to.Length / per, row = (y >> SHIFT) * cw, at = ((y & MASK) << SHIFT) * per;
        for (int done = 0; done < n;)
        {
            int lx = (x + done) & MASK, run = Math.Min(SIDE - lx, n - done);
            var c = chunks[row + ((x + done) >> SHIFT)];
            var part = to.Slice(done * per, run * per);
            if (c == null) part.Fill(fill);
            else c.AsSpan(at + lx * per, run * per).CopyTo(part);
            done += run;
        }
    }

    /// <summary>Part of a row from `from`: slots are allocated only where a value isn't the fill.</summary>
    internal void WriteRow(int x, int y, ReadOnlySpan<T> from)
    {
        int n = from.Length / per, row = (y >> SHIFT) * cw, at = ((y & MASK) << SHIFT) * per;
        for (int done = 0; done < n;)
        {
            int lx = (x + done) & MASK, run = Math.Min(SIDE - lx, n - done);
            int ci = row + ((x + done) >> SHIFT);
            var part = from.Slice(done * per, run * per);
            var c = chunks[ci];
            if (c == null && !All(part, fill)) c = Allocate(ci);
            if (c != null) part.CopyTo(c.AsSpan(at + lx * per, run * per));
            done += run;
        }
    }

    static bool All(ReadOnlySpan<T> values, T value)
    {
        foreach (var v in values) if (!Same(v, value)) return false;
        return true;
    }

    /// <summary>The whole layer as a flat row-major array.</summary>
    internal T[] ToFlat()
    {
        var a = new T[(long)count * per];
        int row = width * per;
        for (int y = 0; y < height; y++) ReadRow(0, y, a.AsSpan(y * row, row));
        return a;
    }

    /// <summary>Every tile set to one value: the fill changes and every slot goes.</summary>
    internal void Fill(T value)
    {
        fill = value;
        Array.Clear(chunks);
    }

    /// <summary>Whether a chunk slot holds anything (its values may still all be the fill).</summary>
    internal bool Allocated(int cx, int cy) => chunks[cy * cw + cx] != null;

    /// <summary>Whether any tile of a rectangle differs from `value`. Null slots are answered by the fill.</summary>
    internal bool AnyNot(int x0, int y0, int w, int h, T value)
    {
        for (int cy = y0 >> SHIFT; cy <= (y0 + h - 1) >> SHIFT; cy++)
            for (int cx = x0 >> SHIFT; cx <= (x0 + w - 1) >> SHIFT; cx++)
            {
                var c = chunks[cy * cw + cx];
                if (c == null)
                {
                    if (!Same(fill, value)) return true;
                    continue;
                }
                int xa = Math.Max(x0, cx << SHIFT), xb = Math.Min(x0 + w, (cx + 1) << SHIFT);
                int ya = Math.Max(y0, cy << SHIFT), yb = Math.Min(y0 + h, (cy + 1) << SHIFT);
                for (int y = ya; y < yb; y++)
                    if (!All(c.AsSpan((((y & MASK) << SHIFT) | (xa & MASK)) * per, (xb - xa) * per), value)) return true;
            }
        return false;
    }

    /// <summary>Another layer's whole extent laid into this one with its corner at x0, y0 (a made chunk into the world).</summary>
    internal void CopyFrom(GridLayer<T> other, int x0, int y0)
    {
        Span<T> row = stackalloc T[SIDE * per];
        for (int y = 0; y < other.height; y++)
            for (int x = 0; x < other.width; x += SIDE)
            {
                var part = row[..(Math.Min(SIDE, other.width - x) * per)];
                other.ReadRow(x, y, part);
                WriteRow(x0 + x, y0 + y, part);
            }
    }

    /// <summary>Whether a rectangle here (at x0, y0) holds what another layer holds (at ox0, oy0). At most a chunk wide.</summary>
    internal bool SameAs(GridLayer<T> other, int x0, int y0, int ox0, int oy0, int w, int h)
    {
        Span<T> a = stackalloc T[SIDE * per], b = stackalloc T[SIDE * per];
        a = a[..(w * per)];
        b = b[..(w * per)];
        for (int y = 0; y < h; y++)
        {
            ReadRow(x0, y0 + y, a);
            other.ReadRow(ox0, oy0 + y, b);
            for (int k = 0; k < a.Length; k++) if (!Same(a[k], b[k])) return false;
        }
        return true;
    }

    /// <summary>Whether any tile of the grid holds `value`.</summary>
    internal bool Has(T value)
    {
        bool filled = Same(fill, value);
        for (int ci = 0; ci < chunks.Length; ci++)
        {
            var c = chunks[ci];
            if (c == null)
            {
                if (filled) return true;
                continue;
            }
            int cx = ci % cw, cy = ci / cw;
            int w = Math.Min(SIDE, width - (cx << SHIFT)), h = Math.Min(SIDE, height - (cy << SHIFT));
            for (int y = 0; y < h; y++)
                foreach (var v in c.AsSpan((y << SHIFT) * per, w * per)) if (Same(v, value)) return true;
        }
        return false;
    }

    /// <summary>How many chunk slots are allocated (for memory reports).</summary>
    internal int AllocatedCount()
    {
        int n = 0;
        foreach (var c in chunks) if (c != null) n++;
        return n;
    }
}

/// <summary>Tile kinds, as codes into Grids' table of names. JSON: the names.</summary>
[JsonConverter(typeof(TileLayerJson))]
public sealed class TileLayer : GridLayer<byte>
{
    public TileLayer() : base(1, 0) { }
}

/// <summary>Small whole numbers (ground, explored, dirt). JSON: numbers.</summary>
[JsonConverter(typeof(ByteLayerJson))]
public sealed class ByteLayer : GridLayer<byte>
{
    public ByteLayer() : base(1, 0) { }
}

/// <summary>Two edge kinds a tile (north, west). JSON: numbers, two a tile.</summary>
[JsonConverter(typeof(EdgeLayerJson))]
public sealed class EdgeLayer : GridLayer<byte>
{
    public EdgeLayer() : base(2, 0) { }
}

/// <summary>Damage. JSON: numbers.</summary>
[JsonConverter(typeof(DoubleLayerJson))]
public sealed class DoubleLayer : GridLayer<double>
{
    public DoubleLayer() : base(1, 0) { }
}

// The layers in JSON: flat arrays, as the lists they replaced were written.

sealed class TileLayerJson : JsonConverter<TileLayer>
{
    public override TileLayer Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a grid layer is an array");
        var values = new List<byte>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            values.Add(Grids.tileCode(reader.GetString() ?? throw new JsonException("a tile is a string")));
        return new TileLayer { flat = values.ToArray() };
    }

    public override void Write(Utf8JsonWriter writer, TileLayer layer, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        Span<byte> row = stackalloc byte[SIDE_ROW];
        var names = Grids.tileNames;
        for (int y = 0; y < layer.height; y++)
            for (int x = 0; x < layer.width; x += SIDE_ROW)
            {
                var part = row[..Math.Min(SIDE_ROW, layer.width - x)];
                layer.ReadRow(x, y, part);
                foreach (byte code in part) writer.WriteStringValue(names[code]);
            }
        writer.WriteEndArray();
    }

    internal const int SIDE_ROW = 256;
}

class ByteLayerJson<L> : JsonConverter<L> where L : GridLayer<byte>, new()
{
    public override L Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a grid layer is an array");
        var values = new List<byte>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) values.Add(Grids.small(reader.GetDouble()));
        return new L { flat = values.ToArray() };
    }

    public override void Write(Utf8JsonWriter writer, L layer, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        Span<byte> row = stackalloc byte[TileLayerJson.SIDE_ROW];
        int width = layer.width * layer.per;
        for (int y = 0; y < layer.height; y++)
            for (int x = 0; x < width; x += row.Length)
            {
                var part = row[..Math.Min(row.Length, width - x)];
                layer.ReadRow(x / layer.per, y, part);
                foreach (byte v in part) writer.WriteNumberValue(v);
            }
        writer.WriteEndArray();
    }
}

sealed class ByteLayerJson : ByteLayerJson<ByteLayer> { }

sealed class EdgeLayerJson : ByteLayerJson<EdgeLayer> { }

sealed class DoubleLayerJson : JsonConverter<DoubleLayer>
{
    public override DoubleLayer Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("a grid layer is an array");
        var values = new List<double>();
        // A number, or NaN or an infinity as its named string (the options allow them).
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) values.Add(JsonSerializer.Deserialize<double>(ref reader, options));
        return new DoubleLayer { flat = values.ToArray() };
    }

    public override void Write(Utf8JsonWriter writer, DoubleLayer layer, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        Span<double> row = stackalloc double[TileLayerJson.SIDE_ROW];
        for (int y = 0; y < layer.height; y++)
            for (int x = 0; x < layer.width; x += row.Length)
            {
                var part = row[..Math.Min(row.Length, layer.width - x)];
                layer.ReadRow(x, y, part);
                foreach (double v in part) SaveWriter.Double(writer, v);
            }
        writer.WriteEndArray();
    }
}
