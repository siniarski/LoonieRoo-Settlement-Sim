// vitest's expect, for the tests translated from the TypeScript's tests/*.test.ts and those written since.
// Same names and meanings: toBe is JS's Object.is (numbers by value, whatever their C# type; objects by reference);
// toEqual compares as JSON (as the TypeScript's state would serialise: a missing field and null the same);
// toMatchObject checks a subset. A failure throws with vitest's message plus the test's own (expect(x, msg)).
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCaseOrderer("LoonieRoo.Sim.Tests.InOrder", "Sim.Tests")] // the sim has module state (the clock's scale, caches); vitest isolates each file

namespace LoonieRoo.Sim.Tests;

public static class Vitest
{
    public static Expectation expect(object? actual, string? message = null) => new(actual, message, false);

    /// <summary>expect.arrayContaining([...]): a list holding (at least) these, in any order.</summary>
    public static Matcher arrayContaining(object? items) => new("arrayContaining", items);
    public static Matcher objectContaining(object? fields) => new("objectContaining", fields);
    public static Matcher stringMatching(object? pattern) => new("stringMatching", pattern);

    /// <summary>A value as JSON, as the TypeScript would see it: tuples as arrays, records and classes as objects.</summary>
    public static JsonNode? ToJs(object? x)
    {
        switch (x)
        {
            case null: return null;
            case JsonNode n: return n.DeepClone();
            case JsonElement je: return JsonNode.Parse(je.GetRawText());
            case string s: return JsonValue.Create(s);
            case bool b: return JsonValue.Create(b);
            case double or float or int or long or uint or short or byte or sbyte or ushort: return JsonValue.Create(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture));
            case Matcher m: return JsonValue.Create($"<{m.kind}>");
            case ITuple t:
            {
                var a = new JsonArray();
                for (int i = 0; i < t.Length; i++) a.Add(ToJs(t[i]));
                return a;
            }
            case IDictionary d:
            {
                var o = new JsonObject();
                foreach (DictionaryEntry kv in d) o[Convert.ToString(kv.Key, System.Globalization.CultureInfo.InvariantCulture)!] = ToJs(kv.Value);
                return o;
            }
            case System.Collections.IEnumerable s when x.GetType().IsGenericType && x.GetType().GetGenericTypeDefinition() == typeof(JsSet<>):
            {
                // A Set compares without order (vitest's toEqual): sorted.
                var items = s.Cast<object?>().Select(ToJs).OrderBy(n => n?.ToJsonString() ?? "", StringComparer.Ordinal).ToArray();
                return new JsonArray(items);
            }
            case IEnumerable e:
            {
                var a = new JsonArray();
                foreach (var v in e) a.Add(ToJs(v));
                return a;
            }
            default:
                return JsonSerializer.SerializeToNode(x, x.GetType(), StateJson.Options);
        }
    }

    /// <summary>JSON equality as toEqual: objects key by key (missing and null the same), arrays item by item, numbers by value.</summary>
    public static bool DeepEqual(JsonNode? a, JsonNode? b)
    {
        // A field one side lacks (JS's undefined, which toEqual ignores) matches the other side's empty value:
        // C# classes can't leave a field out, so where the TypeScript object has no field the C# one has a default.
        static bool Empty(JsonNode? n) => n is null
            || (n is JsonValue v && ((v.TryGetValue<string>(out var s) && s.Length == 0) || (v.TryGetValue<bool>(out var bl) && !bl) || (v.TryGetValue<double>(out var d) && d == 0)))
            || (n is JsonArray arr && arr.Count == 0)
            || (n is JsonObject o && o.Count == 0);
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(kv => kv.Key).Union(ob.Select(kv => kv.Key)))
            {
                bool hasA = oa.TryGetPropertyValue(key, out var va);
                bool hasB = ob.TryGetPropertyValue(key, out var vb);
                if (hasA && hasB ? !DeepEqual(va, vb) : !Empty(hasA ? va : vb)) return false;
            }
            return true;
        }
        if (a is JsonArray xa && b is JsonArray xb)
        {
            if (xa.Count != xb.Count) return false;
            for (int i = 0; i < xa.Count; i++) if (!DeepEqual(xa[i], xb[i])) return false;
            return true;
        }
        return StateJson.Diff(a, b, 1).Count == 0;
    }

    /// <summary>toMatchObject: every field of want is in got, recursively (arrays item by item, the same length).</summary>
    public static bool Subset(JsonNode? got, JsonNode? want)
    {
        if (want is JsonObject wo)
        {
            if (got is not JsonObject go) return false;
            foreach (var (k, v) in wo)
            {
                go.TryGetPropertyValue(k, out var gv);
                if (!Subset(gv, v)) return false;
            }
            return true;
        }
        if (want is JsonArray wa)
        {
            if (got is not JsonArray ga || ga.Count != wa.Count) return false;
            for (int i = 0; i < wa.Count; i++) if (!Subset(ga[i], wa[i])) return false;
            return true;
        }
        return DeepEqual(got, want);
    }
}

/// <summary>An asymmetric matcher (expect.arrayContaining and the like), used inside toEqual.</summary>
public sealed record Matcher(string kind, object? arg)
{
    public bool Matches(object? actual)
    {
        switch (kind)
        {
            case "arrayContaining":
            {
                var got = Vitest.ToJs(actual) as JsonArray;
                var want = Vitest.ToJs(arg) as JsonArray;
                return got != null && want != null && want.All(w => got.Any(g => Vitest.DeepEqual(g, w)));
            }
            case "objectContaining":
                return Vitest.Subset(Vitest.ToJs(actual), Vitest.ToJs(arg));
            case "stringMatching":
                return actual is string s && (arg is JsRegExp r ? r.test(s) : arg is string p && s.Contains(p, StringComparison.Ordinal));
            default:
                return false;
        }
    }
}

public sealed class Expectation(object? actualIn, string? message, bool negated)
{
    // A value read from JSON (a loose record's field) is a JsonElement: as JSON nodes, so lists have a length and compare.
    readonly object? actual = actualIn is JsonElement je ? JsonNode.Parse(je.GetRawText()) : actualIn;
    /// <summary>expect(x).not</summary>
    public Expectation not => new(actual, message, !negated);

    void Check(bool ok, string what)
    {
        if (ok == negated) throw new Xunit.Sdk.XunitException($"expected {Show(actual)} {(negated ? "not " : "")}{what}{(message != null ? "\n" + message : "")}");
    }

    static string Show(object? x)
    {
        try { return Vitest.ToJs(x)?.ToJsonString() ?? "null"; }
        catch { return x?.ToString() ?? "null"; }
    }

    static bool Num(object? x, out double d)
    {
        d = x switch { double v => v, float v => v, int v => v, long v => v, uint v => v, short v => v, byte v => v, _ => double.NaN };
        return x is double or float or int or long or uint or short or byte;
    }

    public void toBe(object? want)
    {
        bool ok = Num(actual, out var a) && Num(want, out var b)
            ? (a == b && (a != 0 || 1 / a == 1 / b)) || (double.IsNaN(a) && double.IsNaN(b)) // Object.is
            : actual is string sa && want is string sb ? sa == sb
            : actual is bool ba && want is bool bb ? ba == bb
            : ReferenceEquals(actual, want) || (actual is ValueType && Equals(actual, want));
        Check(ok, $"to be {Show(want)}");
    }

    public void toEqual(object? want) => Check(Equal(actual, want), $"to equal {Show(want)}");
    public void toStrictEqual(object? want) => toEqual(want);

    static bool Equal(object? got, object? want)
    {
        if (want is Matcher m) return m.Matches(got);
        // Matchers nested in a literal: compare item by item.
        if (want is JsLooseList wl && Vitest.ToJs(got) is JsonArray ga)
        {
            if (ga.Count != wl.Count) return false;
            var items = got is IEnumerable e && got is not string ? e.Cast<object?>().ToList() : null;
            for (int i = 0; i < wl.Count; i++) if (!Equal(items != null ? items[i] : ga[i], wl[i])) return false;
            return true;
        }
        return Vitest.DeepEqual(Vitest.ToJs(got), Vitest.ToJs(want));
    }

    public void toMatchObject(object? want) => Check(Vitest.Subset(Vitest.ToJs(actual), Vitest.ToJs(want)), $"to match object {Show(want)}");
    public void toBeTypeOf(string type) => Check(type switch
    {
        "function" => actual is Delegate,
        "number" => Num(actual, out _),
        "string" => actual is string,
        "boolean" => actual is bool,
        "object" => actual is not null and not Delegate and not string and not bool && !Num(actual, out _),
        "undefined" => actual is null,
        _ => false,
    }, $"to be of type {type}");

    public void toBeNull() => Check(actual is null, "to be null");
    public void toBeUndefined() => Check(actual is null, "to be undefined");
    public void toBeDefined() => Check(actual is not null, "to be defined");
    public void toBeTruthy() => Check(Truthy(actual), "to be truthy");
    public void toBeFalsy() => Check(!Truthy(actual), "to be falsy");
    public void toBeNaN() => Check(Num(actual, out var a) && double.IsNaN(a), "to be NaN");

    static bool Truthy(object? x) => x switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        _ when Num(x, out var d) => d != 0 && !double.IsNaN(d),
        _ => true,
    };

    double N(object? x, string what) => Num(x, out var d) ? d : throw new Xunit.Sdk.XunitException($"{what}: {Show(x)} is not a number");
    public void toBeGreaterThan(object? n) => Check(N(actual, "actual") > N(n, "expected"), $"to be greater than {Show(n)}");
    public void toBeGreaterThanOrEqual(object? n) => Check(N(actual, "actual") >= N(n, "expected"), $"to be at least {Show(n)}");
    public void toBeLessThan(object? n) => Check(N(actual, "actual") < N(n, "expected"), $"to be less than {Show(n)}");
    public void toBeLessThanOrEqual(object? n) => Check(N(actual, "actual") <= N(n, "expected"), $"to be at most {Show(n)}");
    public void toBeCloseTo(object? n, double digits = 2) => Check(Math.Abs(N(actual, "actual") - N(n, "expected")) < Math.Pow(10, -digits) / 2, $"to be close to {Show(n)}");

    public void toHaveLength(object? n)
    {
        double len = actual switch
        {
            string s => s.Length,
            ICollection c => c.Count,
            IEnumerable e => e.Cast<object?>().Count(),
            _ => double.NaN,
        };
        Check(len == N(n, "length"), $"to have length {Show(n)} (has {len})");
    }

    public void toContain(object? x)
    {
        bool ok = actual is string s ? x is string sx && s.Contains(sx, StringComparison.Ordinal)
            : actual is IEnumerable e && e.Cast<object?>().Any(v => Js.Eq(v, x));
        Check(ok, $"to contain {Show(x)}");
    }

    public void toContainEqual(object? x) => Check(actual is IEnumerable e && e.Cast<object?>().Any(v => Equal(v, x)), $"to contain an item equal to {Show(x)}");

    public void toMatch(object? pattern)
    {
        bool ok = actual is string s && (pattern is JsRegExp r ? r.test(s) : pattern is string p && s.Contains(p, StringComparison.Ordinal));
        Check(ok, $"to match {pattern}");
    }

    public void toThrow(object? what = null)
    {
        bool threw = false;
        string? msg = null;
        if (actual is Action a)
        {
            try { a(); }
            catch (Exception e) { threw = true; msg = e.Message; }
        }
        else if (actual is Delegate d)
        {
            try { d.DynamicInvoke(); }
            catch (System.Reflection.TargetInvocationException e) { threw = true; msg = e.InnerException?.Message; }
        }
        bool ok = threw && (what is null || (what is string w ? msg?.Contains(w, StringComparison.Ordinal) == true : what is JsRegExp r ? r.test(msg ?? "") : true));
        Check(ok, $"to throw{(what is null ? "" : " " + what)}");
    }
}

/// <summary>Runs a class's facts in the order they're written (their translated names are numbered: T001_..., T002_...).</summary>
public sealed class InOrder : Xunit.Sdk.ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : Xunit.Abstractions.ITestCase
        => testCases.OrderBy(c => c.TestMethod.Method.Name, StringComparer.Ordinal);
}
