// JavaScript's typed arrays (Int32Array, Float32Array, Uint8Array...): a fixed-length array of one element type,
// read and written as JS numbers (doubles). A write converts as JS does: ToInt32 for Int32Array, wrapping for
// the unsigned and smaller kinds, rounding to the nearest float for Float32Array. Reads give doubles back.
// So the translated sim indexes and assigns them exactly as the TypeScript does, with no casts.
// JSON: as JSON.stringify writes a typed array, an object of index keys ({"0": 1, "1": 2}).
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoonieRoo.Sim;

public abstract class TypedArray<T> : IEnumerable<double> where T : struct
{
    public readonly T[] a;

    protected TypedArray(int n) => a = new T[n];
    protected TypedArray(T[] items) => a = items;

    protected abstract T To(double v);
    protected abstract double From(T v);

    public int length => a.Length;

    public double this[int i]
    {
        get => (uint)i < (uint)a.Length ? From(a[i]) : double.NaN;
        set { if ((uint)i < (uint)a.Length) a[i] = To(value); }
    }

    public double this[double i]
    {
        get => i >= 0 && i < a.Length && i == Math.Floor(i) ? From(a[(int)i]) : double.NaN;
        set { if (i >= 0 && i < a.Length && i == Math.Floor(i)) a[(int)i] = To(value); }
    }

    public int indexOf(double v)
    {
        for (int i = 0; i < a.Length; i++) if (From(a[i]) == v) return i;
        return -1;
    }

    public bool includes(double v) => indexOf(v) >= 0;

    /// <summary>A typed array sorts numerically (NaN last), in place.</summary>
    public TypedArray<T> sort()
    {
        var vs = this.ToArray();
        Array.Sort(vs, (x, y) => double.IsNaN(x) ? (double.IsNaN(y) ? 0 : 1) : double.IsNaN(y) ? -1 : x.CompareTo(y));
        for (int i = 0; i < vs.Length; i++) this[i] = vs[i];
        return this;
    }

    public IEnumerator<double> GetEnumerator()
    {
        for (int i = 0; i < a.Length; i++) yield return From(a[i]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class TypedConv
{
    /// <summary>A length as JS takes one (a whole, non-negative number).</summary>
    public static int Len(double n) => n >= 0 && n == Math.Floor(n) && n <= int.MaxValue ? (int)n : throw new ArgumentOutOfRangeException(nameof(n), "Invalid typed array length");
}

[JsonConverter(typeof(TypedArrayJson<Int32Array>))]
public sealed class Int32Array : TypedArray<int>
{
    public Int32Array(double n) : base(TypedConv.Len(n)) { }
    public Int32Array(IEnumerable<double> xs) : base(xs.Select(Js.ToInt32).ToArray()) { }
    protected override int To(double v) => Js.ToInt32(v);
    protected override double From(int v) => v;
    public Int32Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Int32Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Uint32Array>))]
public sealed class Uint32Array : TypedArray<uint>
{
    public Uint32Array(double n) : base(TypedConv.Len(n)) { }
    public Uint32Array(IEnumerable<double> xs) : base(xs.Select(Js.ToUint32).ToArray()) { }
    protected override uint To(double v) => Js.ToUint32(v);
    protected override double From(uint v) => v;
    public Uint32Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Uint32Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Uint16Array>))]
public sealed class Uint16Array : TypedArray<ushort>
{
    public Uint16Array(double n) : base(TypedConv.Len(n)) { }
    public Uint16Array(IEnumerable<double> xs) : base(xs.Select(v => (ushort)Js.ToUint32(v)).ToArray()) { }
    protected override ushort To(double v) => (ushort)Js.ToUint32(v);
    protected override double From(ushort v) => v;
    public Uint16Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Uint16Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Uint8Array>))]
public sealed class Uint8Array : TypedArray<byte>
{
    public Uint8Array(double n) : base(TypedConv.Len(n)) { }
    public Uint8Array(IEnumerable<double> xs) : base(xs.Select(v => (byte)Js.ToUint32(v)).ToArray()) { }
    protected override byte To(double v) => (byte)Js.ToUint32(v);
    protected override double From(byte v) => v;
    public Uint8Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Uint8Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Int8Array>))]
public sealed class Int8Array : TypedArray<sbyte>
{
    public Int8Array(double n) : base(TypedConv.Len(n)) { }
    public Int8Array(IEnumerable<double> xs) : base(xs.Select(v => (sbyte)Js.ToInt32(v)).ToArray()) { }
    protected override sbyte To(double v) => (sbyte)Js.ToInt32(v);
    protected override double From(sbyte v) => v;
    public Int8Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Int8Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Float32Array>))]
public sealed class Float32Array : TypedArray<float>
{
    public Float32Array(double n) : base(TypedConv.Len(n)) { }
    public Float32Array(IEnumerable<double> xs) : base(xs.Select(v => (float)v).ToArray()) { }
    protected override float To(double v) => (float)v;
    protected override double From(float v) => v;
    public Float32Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, To(v), start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Float32Array slice() => new(this);
}

[JsonConverter(typeof(TypedArrayJson<Float64Array>))]
public sealed class Float64Array : TypedArray<double>
{
    public Float64Array(double n) : base(TypedConv.Len(n)) { }
    public Float64Array(IEnumerable<double> xs) : base(xs.ToArray()) { }
    protected override double To(double v) => v;
    protected override double From(double v) => v;
    public Float64Array fill(double v, int start = 0, int end = int.MaxValue) { Array.Fill(a, v, start, Math.Min(end, a.Length) - start); return this; }
    public void set(IEnumerable<double> src, int offset = 0) { foreach (var v in src) this[offset++] = v; }
    public Float64Array slice() => new(this);
}

/// <summary>A typed array in JSON as JSON.stringify writes one: {"0": v, "1": v, ...}.</summary>
public sealed class TypedArrayJson<A> : JsonConverter<A> where A : class, IEnumerable<double>
{
    public override A Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = new List<double>();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("a typed array is an object of index keys");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            reader.Read();
            values.Add(reader.GetDouble());
        }
        return (A)Activator.CreateInstance(typeof(A), (IEnumerable<double>)values)!;
    }

    public override void Write(Utf8JsonWriter writer, A value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        int i = 0;
        foreach (var v in value)
        {
            writer.WritePropertyName((i++).ToString(System.Globalization.CultureInfo.InvariantCulture));
            JsonSerializer.Serialize(writer, v, options);
        }
        writer.WriteEndObject();
    }
}
