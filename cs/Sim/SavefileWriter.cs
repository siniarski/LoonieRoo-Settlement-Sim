// Writing saves straight to UTF-8 (2026-11-10): the state's members in the serializer's own order, each through
// JsonSerializer as before, but the big parts (the grid's per-tile layers, furniture, the dormant) written value by
// value, never as a tree of JSON nodes (a node a tile made a 512 save take ~1 s). The text is the same as the node
// version wrote: Savefile.cs and SavefileChunks.cs say what goes where.
using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LoonieRoo.Sim;

public static class SaveWriter
{
    /// <summary>A member as the serializer sees it: its JSON name, type and value.</summary>
    public sealed record Member(string Name, Type Type, Func<object, object?> Get);

    static readonly ConcurrentDictionary<Type, Member[]> props = new();

    /// <summary>A type's members as the serializer writes them, in its order (its metadata's getters are internal, so
    /// values are read through the property or field). [JsonIgnore] members are left out; other ignore conditions than
    /// the options' (nulls left out) aren't used in the state, and are refused here rather than written wrong.</summary>
    public static Member[] Props(Type t) => props.GetOrAdd(t, (type) => StateJson.Options.GetTypeInfo(type).Properties
        .Where((p) => p.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true) is not [JsonIgnoreAttribute ignore]
            || ignore.Condition != JsonIgnoreCondition.Always)
        .Select((p) =>
        {
            if (p.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true) is [JsonIgnoreAttribute other])
                throw new NotSupportedException($"{type.Name}.{p.Name}: [JsonIgnore(Condition = {other.Condition})] isn't written by SaveWriter");
            return new Member(p.Name, p.PropertyType, p.AttributeProvider switch
            {
                PropertyInfo pi => pi.GetValue,
                FieldInfo fi => fi.GetValue,
                _ => throw new NotSupportedException($"{type.Name}.{p.Name}: not a property or field"),
            });
        }).ToArray());

    public static Utf8JsonWriter Writer(IBufferWriter<byte> buffer) =>
        new(buffer, new JsonWriterOptions { Encoder = StateJson.Options.Encoder, Indented = false });

    /// <summary>Writes a JSON document with `write` and returns its text.</summary>
    public static string Text(Action<Utf8JsonWriter> write) => Encoding.UTF8.GetString(Written(write).WrittenSpan);

    /// <summary>Writes a JSON document with `write` and returns it as UTF-8.</summary>
    public static byte[] Bytes(Action<Utf8JsonWriter> write) => Written(write).WrittenSpan.ToArray();

    static ArrayBufferWriter<byte> Written(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(1 << 16);
        using (var w = Writer(buffer)) write(w);
        return buffer;
    }

    /// <summary>An object's members as JsonSerializer writes them (nulls left out), but those `custom` writes itself
    /// (it's asked of each member in turn, so it writes in place; true if it did).</summary>
    public static void Members(Utf8JsonWriter w, object obj, Func<string, bool>? custom = null)
    {
        foreach (var p in Props(obj.GetType()))
        {
            if (custom != null && custom(p.Name)) continue;
            var v = p.Get(obj);
            if (v == null) continue;
            w.WritePropertyName(p.Name);
            JsonSerializer.Serialize(w, v, p.Type, StateJson.Options);
        }
    }

    public static void EmptyArray(Utf8JsonWriter w, string name)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        w.WriteEndArray();
    }

    /// <summary>A number as the serializer writes it (NaN and the infinities as named strings).</summary>
    public static void Double(Utf8JsonWriter w, double d)
    {
        if (double.IsFinite(d)) w.WriteNumberValue(d);
        else JsonSerializer.Serialize(w, d, StateJson.Options);
    }

    // Run-length encoding ([value, count, ...]: Savefile.runLengthEncode's form), straight from the values.

    public static void Rle(Utf8JsonWriter w, ReadOnlySpan<int> v)
    {
        w.WriteStartArray();
        for (int i = 0; i < v.Length;)
        {
            int j = i + 1;
            while (j < v.Length && v[j] == v[i]) j++;
            w.WriteNumberValue(v[i]);
            w.WriteNumberValue(j - i);
            i = j;
        }
        w.WriteEndArray();
    }

    public static void Rle(Utf8JsonWriter w, ReadOnlySpan<double> v)
    {
        w.WriteStartArray();
        for (int i = 0; i < v.Length;)
        {
            int j = i + 1;
            // The same number to the bit (0 and -0 are two runs, as they're two texts).
            while (j < v.Length && BitConverter.DoubleToInt64Bits(v[j]) == BitConverter.DoubleToInt64Bits(v[i])) j++;
            Double(w, v[i]);
            w.WriteNumberValue(j - i);
            i = j;
        }
        w.WriteEndArray();
    }

    public static void Rle(Utf8JsonWriter w, ReadOnlySpan<string> v)
    {
        w.WriteStartArray();
        for (int i = 0; i < v.Length;)
        {
            int j = i + 1;
            while (j < v.Length && string.Equals(v[j], v[i], StringComparison.Ordinal)) j++;
            if (v[i] == null) w.WriteNullValue();
            else w.WriteStringValue(v[i]);
            w.WriteNumberValue(j - i);
            i = j;
        }
        w.WriteEndArray();
    }

    public static ReadOnlySpan<T> Span<T>(List<T> list) => CollectionsMarshal.AsSpan(list);
}
