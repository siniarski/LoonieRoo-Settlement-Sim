// Reading and writing the sim state as JSON, the same shape as the TypeScript's JSON.stringify(state).
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LoonieRoo.Sim;

public static class StateJson
{
    /// <summary>Public fields, names as written (they're the TypeScript names), nulls left out (as undefined is in JSON.stringify).
    /// Locked at once, with the default (reflection) resolver, so the save writer can read the members' order (GetTypeInfo).</summary>
    public static readonly JsonSerializerOptions Options = Locked(new()
    {
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    });

    static JsonSerializerOptions Locked(JsonSerializerOptions o)
    {
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    public static SimState Read(string json) => JsonSerializer.Deserialize<SimState>(json, Options) ?? throw new InvalidDataException("not a sim state");

    public static string Write(SimState state) => JsonSerializer.Serialize(state, Options);

    /// <summary>Deep copy through JSON (as cloneState does).</summary>
    public static SimState Clone(SimState state) => Read(Write(state));

    /// <summary>
    /// Where two JSON documents differ, as paths (up to `max`): objects key by key (a missing key and null are the
    /// same, as undefined and a missing key are in JSON.stringify), arrays item by item, numbers by value.
    /// </summary>
    public static List<string> Diff(JsonNode? a, JsonNode? b, int max = 20)
    {
        var out_ = new List<string>();
        Walk(a, b, "$", out_, max);
        return out_;
    }

    static void Walk(JsonNode? a, JsonNode? b, string path, List<string> out_, int max)
    {
        if (out_.Count >= max) return;
        if (a is null || b is null)
        {
            if (!(a is null && b is null)) out_.Add($"{path}: {Show(a)} vs {Show(b)}");
            return;
        }
        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
                foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                {
                    // The command log holds union-typed commands whose unset fields C# writes as defaults: not compared.
                    if (path == "$" && key == "commandLog") continue;
                    oa.TryGetPropertyValue(key, out var va);
                    ob.TryGetPropertyValue(key, out var vb);
                    Walk(va, vb, $"{path}.{key}", out_, max);
                }
                return;
            case JsonArray aa when b is JsonArray ab:
                if (aa.Count != ab.Count) out_.Add($"{path}: {aa.Count} items vs {ab.Count}");
                for (int i = 0; i < Math.Min(aa.Count, ab.Count); i++) Walk(aa[i], ab[i], $"{path}[{i}]", out_, max);
                return;
            case JsonValue va2 when b is JsonValue vb2:
                if (va2.GetValueKind() == JsonValueKind.Number && vb2.GetValueKind() == JsonValueKind.Number)
                {
                    if (va2.GetValue<double>() != vb2.GetValue<double>()) out_.Add($"{path}: {va2} vs {vb2}");
                    return;
                }
                if (va2.ToJsonString() != vb2.ToJsonString()) out_.Add($"{path}: {va2.ToJsonString()} vs {vb2.ToJsonString()}");
                return;
            default:
                out_.Add($"{path}: {Show(a)} vs {Show(b)}");
                return;
        }
    }

    static string Show(JsonNode? n)
    {
        string s = n?.ToJsonString() ?? "null";
        return s.Length > 80 ? s[..80] + "..." : s;
    }
}
