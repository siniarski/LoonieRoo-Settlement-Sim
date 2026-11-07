// JavaScript's library, as the translated sim (cs/tools/ts2cs.mjs) uses it: array and string methods on
// List<T> and string (C# 14 extension members, so the code reads as the TypeScript did), Math, Number,
// truthiness, String(x), Map and Set that keep insertion order, a Record that answers a missing key with
// the default, and a stable sort. Behaviour matches JavaScript where the sim depends on it.
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static class JsListExtensions
{
    extension(List<int> a)
    {
        /// <summary>indexOf/includes with a JS number, on a list of whole numbers.</summary>
        public int indexOf(double x) => x == Math.Floor(x) && x >= int.MinValue && x <= int.MaxValue ? a.IndexOf((int)x) : -1;
        public bool includes(double x) => a.indexOf(x) >= 0;
    }

    extension<T>(List<T> a)
    {
        public int length
        {
            get => a.Count;
            set
            {
                if (value < a.Count) a.RemoveRange(value, a.Count - value);
                else while (a.Count < value) a.Add(default!);
            }
        }

        public int push(T x)
        {
            a.Add(x);
            return a.Count;
        }

        public int push(T x, T y)
        {
            a.Add(x);
            a.Add(y);
            return a.Count;
        }

        public int push(T x, T y, T z)
        {
            a.Add(x);
            a.Add(y);
            a.Add(z);
            return a.Count;
        }

        /// <summary>push(...xs)</summary>
        public int pushAll(IEnumerable<T> xs)
        {
            a.AddRange(xs.ToList());
            return a.Count;
        }

        public T pop()
        {
            if (a.Count == 0) return default!;
            var x = a[^1];
            a.RemoveAt(a.Count - 1);
            return x;
        }

        public T shift()
        {
            if (a.Count == 0) return default!;
            var x = a[0];
            a.RemoveAt(0);
            return x;
        }

        public int unshift(T x)
        {
            a.Insert(0, x);
            return a.Count;
        }

        public int unshiftAll(IEnumerable<T> xs)
        {
            a.InsertRange(0, xs.ToList());
            return a.Count;
        }

        public bool includes(T x) => a.Contains(x);
        public int indexOf(T x) => a.IndexOf(x);
        public int lastIndexOf(T x) => a.LastIndexOf(x);

        public T find(Func<T, bool> f)
        {
            foreach (var x in a) if (f(x)) return x;
            return default!;
        }

        public T find(Func<T, int, bool> f)
        {
            for (int i = 0; i < a.Count; i++) if (f(a[i], i)) return a[i];
            return default!;
        }

        public T findLast(Func<T, bool> f)
        {
            for (int i = a.Count - 1; i >= 0; i--) if (f(a[i])) return a[i];
            return default!;
        }

        public int findIndex(Func<T, bool> f)
        {
            for (int i = 0; i < a.Count; i++) if (f(a[i])) return i;
            return -1;
        }

        public List<T> filter(Func<T, bool> f)
        {
            var o = new List<T>();
            foreach (var x in a) if (f(x)) o.Add(x);
            return o;
        }

        public List<T> filter(Func<T, int, bool> f)
        {
            var o = new List<T>();
            for (int i = 0; i < a.Count; i++) if (f(a[i], i)) o.Add(a[i]);
            return o;
        }

        public List<U> map<U>(Func<T, U> f)
        {
            var o = new List<U>(a.Count);
            foreach (var x in a) o.Add(f(x));
            return o;
        }

        public List<U> map<U>(Func<T, int, U> f)
        {
            var o = new List<U>(a.Count);
            for (int i = 0; i < a.Count; i++) o.Add(f(a[i], i));
            return o;
        }

        public List<U> flatMap<U>(Func<T, IEnumerable<U>> f)
        {
            var o = new List<U>();
            foreach (var x in a) o.AddRange(f(x));
            return o;
        }

        public bool some(Func<T, bool> f)
        {
            foreach (var x in a) if (f(x)) return true;
            return false;
        }

        public bool some(Func<T, int, bool> f)
        {
            for (int i = 0; i < a.Count; i++) if (f(a[i], i)) return true;
            return false;
        }

        public bool every(Func<T, int, bool> f)
        {
            for (int i = 0; i < a.Count; i++) if (!f(a[i], i)) return false;
            return true;
        }

        public bool every(Func<T, bool> f)
        {
            foreach (var x in a) if (!f(x)) return false;
            return true;
        }

        public void forEach(Action<T> f)
        {
            foreach (var x in a.ToList()) f(x);
        }

        public void forEach(Action<T, int> f)
        {
            var copy = a.ToList();
            for (int i = 0; i < copy.Count; i++) f(copy[i], i);
        }

        /// <summary>reduce with no initial value: the first item starts it (as JS; an empty list throws).</summary>
        public T reduce(Func<T, T, T> f)
        {
            if (a.Count == 0) throw new InvalidOperationException("Reduce of empty array with no initial value");
            var acc = a[0];
            for (int i = 1; i < a.Count; i++) acc = f(acc, a[i]);
            return acc;
        }

        public U reduce<U>(Func<U, T, U> f, U init)
        {
            var acc = init;
            foreach (var x in a) acc = f(acc, x);
            return acc;
        }

        /// <summary>slice with JS numbers (whole, or a fraction truncated as JS does).</summary>
        public List<T> slice(double start, double? end = null) => a.slice((int)Math.Truncate(start), end is double ev ? (int)Math.Truncate(ev) : null);

        /// <summary>slice(start, end): negative indices count from the end, as in JS.</summary>
        public List<T> slice(int start = 0, int? end = null)
        {
            int n = a.Count;
            int s = start < 0 ? Math.Max(0, n + start) : Math.Min(start, n);
            int e = end is int ev ? (ev < 0 ? Math.Max(0, n + ev) : Math.Min(ev, n)) : n;
            return e > s ? a.GetRange(s, e - s) : new List<T>();
        }

        public List<T> splice(int start, int deleteCount, params ReadOnlySpan<T> items)
        {
            int n = a.Count;
            int s = start < 0 ? Math.Max(0, n + start) : Math.Min(start, n);
            int d = Math.Max(0, Math.Min(deleteCount, n - s));
            var removed = a.GetRange(s, d);
            a.RemoveRange(s, d);
            a.InsertRange(s, items.ToArray());
            return removed;
        }

        public List<T> splice(int start)
        {
            int s = start < 0 ? Math.Max(0, a.Count + start) : Math.Min(start, a.Count);
            var removed = a.GetRange(s, a.Count - s);
            a.RemoveRange(s, a.Count - s);
            return removed;
        }

        /// <summary>A stable sort in place by a JS comparator (negative, zero, positive), as Array.prototype.sort.</summary>
        public List<T> sort(Func<T, T, double> cmp)
        {
            var sorted = a.Select((x, i) => (x, i)).OrderBy(p => p, Comparer<(T x, int i)>.Create((p, q) =>
            {
                double c = cmp(p.x, q.x);
                return c < 0 ? -1 : c > 0 ? 1 : p.i.CompareTo(q.i);
            })).Select(p => p.x).ToList();
            for (int i = 0; i < sorted.Count; i++) a[i] = sorted[i];
            return a;
        }

        /// <summary>sort() with no comparator: by String(x), as JS does (numbers too).</summary>
        public List<T> sort()
        {
            var sorted = a.Select((x, i) => (x, i, s: Js.Str(x))).OrderBy(p => p.s, StringComparer.Ordinal).ThenBy(p => p.i).Select(p => p.x).ToList();
            for (int i = 0; i < sorted.Count; i++) a[i] = sorted[i];
            return a;
        }

        public List<T> reverse()
        {
            a.Reverse();
            return a;
        }

        public List<T> fill(T v)
        {
            for (int i = 0; i < a.Count; i++) a[i] = v;
            return a;
        }

        public string join(string sep = ",") => string.Join(sep, a.Select(x => x is null ? "" : Js.Str(x)));

        public List<T> concat(IEnumerable<T> more)
        {
            var o = new List<T>(a);
            o.AddRange(more);
            return o;
        }

        public T at(int i)
        {
            int k = i < 0 ? a.Count + i : i;
            return k >= 0 && k < a.Count ? a[k] : default!;
        }

        public List<(int, T)> entries() => a.Select((x, i) => (i, x)).ToList();
    }

    extension<T>(T[] a)
    {
        public int length => a.Length;
        /// <summary>typedArray.set(source, offset)</summary>
        public void set(T[] source, int offset = 0) => Array.Copy(source, 0, a, offset, source.Length);
        public bool includes(T x) => Array.IndexOf(a, x) >= 0;
        public int indexOf(T x) => Array.IndexOf(a, x);
        public T[] fill(T v)
        {
            Array.Fill(a, v);
            return a;
        }
    }
}

public static class JsStringExtensions
{
    extension(string s)
    {
        public int length => s.Length;
        public bool includes(string x) => s.Contains(x, StringComparison.Ordinal);
        public bool startsWith(string x) => s.StartsWith(x, StringComparison.Ordinal);
        public bool endsWith(string x) => s.EndsWith(x, StringComparison.Ordinal);
        public int indexOf(string x) => s.IndexOf(x, StringComparison.Ordinal);
        public string slice(int start, int? end = null)
        {
            int n = s.Length;
            int a = start < 0 ? Math.Max(0, n + start) : Math.Min(start, n);
            int b = end is int e ? (e < 0 ? Math.Max(0, n + e) : Math.Min(e, n)) : n;
            return b > a ? s.Substring(a, b - a) : "";
        }
        public string substring(int start, int? end = null) => s.slice(Math.Max(0, start), end is int e ? Math.Max(0, e) : null);
        public List<string> split(string sep) => s.Split(sep).ToList();
        public string padStart(int n, string pad = " ")
        {
            var o = s;
            while (o.Length < n) o = pad + o;
            return o.Length > n && s.Length < n ? o.Substring(o.Length - n) : o;
        }
        public string padEnd(int n, string pad = " ")
        {
            var o = s;
            while (o.Length < n) o += pad;
            return o.Length > n && s.Length < n ? o.Substring(0, n) : o;
        }
        public string toUpperCase() => s.ToUpperInvariant();
        public string toLowerCase() => s.ToLowerInvariant();
        public string trim() => s.Trim();
        public string charAt(int i) => i >= 0 && i < s.Length ? s[i].ToString() : "";
        /// <summary>A string pattern: the first occurrence only, as in JS.</summary>
        public string replace(string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + to + s.Substring(i + from.Length);
        }
        public string replaceAll(string from, string to) => s.Replace(from, to, StringComparison.Ordinal);
        public string repeat(int n) => string.Concat(Enumerable.Repeat(s, Math.Max(0, n)));
        public string at(int i)
        {
            int k = i < 0 ? s.Length + i : i;
            return k >= 0 && k < s.Length ? s[k].ToString() : null!;
        }
    }

    extension(double x)
    {
        /// <summary>Number.prototype.toFixed.</summary>
        public string toFixed(int digits) => Js.ToFixed(x, digits);
    }

    extension(int x)
    {
        public string toFixed(int digits) => Js.ToFixed(x, digits);
    }
}

public static class JsDeconstruct
{
    // var (a, b) = list; foreach (var (a, b) in lists): as JS array destructuring.
    public static void Deconstruct<T>(this List<T> l, out T a, out T b)
    {
        a = l.Count > 0 ? l[0] : default!;
        b = l.Count > 1 ? l[1] : default!;
    }

    public static void Deconstruct<T>(this List<T> l, out T a, out T b, out T c)
    {
        a = l.Count > 0 ? l[0] : default!;
        b = l.Count > 1 ? l[1] : default!;
        c = l.Count > 2 ? l[2] : default!;
    }

    public static void Deconstruct<T>(this List<T> l, out T a, out T b, out T c, out T d)
    {
        a = l.Count > 0 ? l[0] : default!;
        b = l.Count > 1 ? l[1] : default!;
        c = l.Count > 2 ? l[2] : default!;
        d = l.Count > 3 ? l[3] : default!;
    }
}

public static class JsMath
{
    public const double PI = Math.PI;
    public const double SQRT2 = 1.4142135623730951;
    public const double SQRT1_2 = 0.7071067811865476;
    public const double E = Math.E;
    public const double LN2 = 0.6931471805599453;
    public const double LN10 = 2.302585092994046;
    public static double floor(double x) => Math.Floor(x);
    public static int floor(int x) => x;
    public static double ceil(double x) => Math.Ceiling(x);
    public static int ceil(int x) => x;
    /// <summary>Math.round: halves round up (towards +infinity).</summary>
    public static double round(double x) => Math.Floor(x + 0.5);
    public static int round(int x) => x;
    public static double trunc(double x) => Math.Truncate(x);
    public static int trunc(int x) => x;
    public static double abs(double x) => Math.Abs(x);
    public static int abs(int x) => Math.Abs(x);
    public static double sign(double x) => double.IsNaN(x) ? double.NaN : x > 0 ? 1 : x < 0 ? -1 : x;
    public static int sign(int x) => Math.Sign(x);
    public static double sqrt(double x) => Math.Sqrt(x);
    public static double pow(double x, double y) => Math.Pow(x, y);
    public static double cos(double x) => Math.Cos(x);
    public static double sin(double x) => Math.Sin(x);
    public static double atan2(double y, double x) => Math.Atan2(y, x);
    public static double log(double x) => Math.Log(x);
    public static double exp(double x) => Math.Exp(x);
    public static int imul(int a, int b) => unchecked(a * b);
    public static int imul(double a, double b) => unchecked(Js.ToInt32(a) * Js.ToInt32(b));
    public static int min(int a, int b) => Math.Min(a, b);
    public static int max(int a, int b) => Math.Max(a, b);
    public static int min(int a, int b, int c) => Math.Min(a, Math.Min(b, c));
    public static int max(int a, int b, int c) => Math.Max(a, Math.Max(b, c));
    /// <summary>Math.min: NaN if any is NaN, Infinity for none.</summary>
    public static double min(params ReadOnlySpan<double> xs)
    {
        double m = double.PositiveInfinity;
        foreach (var x in xs)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x < m) m = x;
        }
        return m;
    }
    public static double max(params ReadOnlySpan<double> xs)
    {
        double m = double.NegativeInfinity;
        foreach (var x in xs)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x > m) m = x;
        }
        return m;
    }
    public static double min(IEnumerable<double> xs) => min(xs.ToArray().AsSpan());
    public static double max(IEnumerable<double> xs) => max(xs.ToArray().AsSpan());
}

public static class JsNumber
{
    public const double MAX_SAFE_INTEGER = 9007199254740991;
    public const double POSITIVE_INFINITY = double.PositiveInfinity;
    public static bool isInteger(double x) => double.IsFinite(x) && Math.Floor(x) == x;
    public static bool isFinite(double x) => double.IsFinite(x);
    public static bool isNaN(double x) => double.IsNaN(x);
}

/// <summary>A Record&lt;string, V&gt;: a dictionary whose missing keys read as the default, as undefined reads in JS.</summary>
public class JsRecord<V> : Dictionary<string, V>
{
    public JsRecord() : base(StringComparer.Ordinal) { }
    public JsRecord(IEnumerable<KeyValuePair<string, V>> items) : base(StringComparer.Ordinal)
    {
        foreach (var kv in items) this[kv.Key] = kv.Value;
    }

    public new V this[string key]
    {
        get => TryGetValue(key, out var v) ? v : default!;
        set => base[key] = value;
    }

    public bool has(string key) => ContainsKey(key);
}

/// <summary>JavaScript's Map: insertion-ordered.</summary>
public class JsMap<K, V> where K : notnull
{
    readonly Dictionary<K, LinkedListNode<(K key, V value)>> index = new();
    readonly LinkedList<(K key, V value)> order = new();

    public JsMap() { }
    public JsMap(IEnumerable<(K, V)> items)
    {
        foreach (var (k, v) in items) set(k, v);
    }

    public int size => index.Count;
    public bool has(K k) => index.ContainsKey(k);
    public V get(K k) => index.TryGetValue(k, out var n) ? n.Value.value : default!;
    public JsMap<K, V> set(K k, V v)
    {
        if (index.TryGetValue(k, out var n)) n.Value = (k, v);
        else index[k] = order.AddLast((k, v));
        return this;
    }
    public bool delete(K k)
    {
        if (!index.Remove(k, out var n)) return false;
        order.Remove(n);
        return true;
    }
    public void clear()
    {
        index.Clear();
        order.Clear();
    }
    public List<K> keys() => order.Select(p => p.key).ToList();
    public List<V> values() => order.Select(p => p.value).ToList();
    public List<(K, V)> entries() => order.Select(p => (p.key, p.value)).ToList();
    public void forEach(Action<V, K> f)
    {
        foreach (var (k, v) in entries()) f(v, k);
    }
}

/// <summary>JavaScript's Set: insertion-ordered.</summary>
public class JsSet<T> : IEnumerable<T> where T : notnull
{
    readonly Dictionary<T, LinkedListNode<T>> index = new();
    readonly LinkedList<T> order = new();

    public JsSet() { }
    public JsSet(IEnumerable<T> items)
    {
        foreach (var x in items) add(x);
    }

    public int size => index.Count;
    public bool has(T x) => index.ContainsKey(x);
    public JsSet<T> add(T x)
    {
        if (!index.ContainsKey(x)) index[x] = order.AddLast(x);
        return this;
    }
    public bool delete(T x)
    {
        if (!index.Remove(x, out var n)) return false;
        order.Remove(n);
        return true;
    }
    public void clear()
    {
        index.Clear();
        order.Clear();
    }
    public List<T> values() => order.ToList();
    public IEnumerator<T> GetEnumerator() => order.ToList().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

public static partial class Js
{
    static Js()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // --- Truthiness (if (x), !x, x && y): by static type, as JS does at run time. ---
    public static bool T(bool b) => b;
    public static bool T(bool? b) => b == true;
    public static bool T(double d) => d != 0 && !double.IsNaN(d);
    public static bool T(double? d) => d is double v && T(v);
    public static bool T(int i) => i != 0;
    public static bool T(int? i) => i is int v && v != 0;
    public static bool T(string? s) => !string.IsNullOrEmpty(s);
    public static bool T(object? o) => o switch
    {
        null => false,
        bool b => b,
        double d => T(d),
        int i => i != 0,
        string s => s.Length > 0,
        _ => true,
    };

    /// <summary>x ?? n: a value that can't be null in C# stands as it is.</summary>
    public static double Q(double a, double b) => a;
    public static double Q(double? a, double b) => a ?? b;
    public static int Q(int a, int b) => a;
    public static int Q(int? a, int b) => a ?? b;

    /// <summary>a || b used as a value: a if truthy, else b.</summary>
    public static V Or<V>(V a, V b) => T((object?)a) ? a : b;

    /// <summary>x! in TypeScript: for a nullable value type, its value.</summary>
    public static V NN<V>(V? x) where V : struct => x!.Value;
    public static V NN<V>(V? x) where V : class => x!;
    public static double NN(double x) => x;
    public static int NN(int x) => x;
    public static bool NN(bool x) => x;

    // --- Numbers as strings (String(x), template literals): JS's formatting. ---
    public static string Str(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";
        double abs = Math.Abs(d);
        if (abs >= 1e21 || abs < 1e-6)
        {
            string e = d.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "e+").Replace("E-", "e-");
            return e;
        }
        return d.ToString("R", CultureInfo.InvariantCulture);
    }
    public static string Str(int i) => i.ToString(CultureInfo.InvariantCulture);
    public static string Str(long i) => i.ToString(CultureInfo.InvariantCulture);
    public static string Str(bool b) => b ? "true" : "false";
    public static string Str(string? s) => s ?? "null";
    public static string Str(double? d) => d is double v ? Str(v) : "null";
    public static string Str(int? d) => d is int v ? Str(v) : "null";
    public static string Str<V>(V x) => x switch
    {
        null => "null",
        double d => Str(d),
        int i => Str(i),
        bool b => Str(b),
        string s => s,
        _ => x.ToString() ?? "",
    };

    public static string ToFixed(double x, int digits) => Math.Round(x, digits, MidpointRounding.AwayFromZero).ToString("F" + digits, CultureInfo.InvariantCulture);

    /// <summary>x >>> n</summary>
    public static int Ushr(int x, int n) => (int)((uint)x >> n);
    public static double Ushr(double x, int n) => (double)(ToUint32(x) >> n);

    /// <summary>Array.from({ length: n }, (_, i) => f(i)).</summary>
    public static List<V> ArrayFrom<V>(int n, Func<int, V> f)
    {
        var o = new List<V>(n);
        for (int i = 0; i < n; i++) o.Add(f(i));
        return o;
    }

    public static JsSet<V> Set<V>(IEnumerable<V> items) where V : notnull => new(items);

    static readonly System.Reflection.MethodInfo memberwiseClone = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    /// <summary>{ ...x }: a shallow copy (fields copied, what they point to shared).</summary>
    public static V Clone<V>(V x) where V : class => (V)memberwiseClone.Invoke(x, null)!;

    /// <summary>{ ...x, a: 1 }: the copy, then the fields set.</summary>
    public static V With<V>(V x, Action<V> set)
    {
        set(x);
        return x;
    }

    /// <summary>new Map(list.map((x) => [key, value])).</summary>
    public static JsMap<K, V> MapOf<T, K, V>(IEnumerable<T> list, Func<T, K> key, Func<T, V> value) where K : notnull
    {
        var m = new JsMap<K, V>();
        foreach (var x in list) m.set(key(x), value(x));
        return m;
    }

    /// <summary>obj[key] for a field named at run time (s[slot]).</summary>
    public static V GetProp<V>(object o, string key) => (V)(o.GetType().GetProperty(key)?.GetValue(o) ?? default(V))!;

    /// <summary>obj[key] = v for a field named at run time.</summary>
    public static void SetProp(object o, string key, object? v) => o.GetType().GetProperty(key)!.SetValue(o, v);

    /// <summary>typeof x.</summary>
    public static string TypeOf(object? x) => x switch
    {
        null => "undefined",
        double or int or long or float => "number",
        string => "string",
        bool => "boolean",
        Delegate => "function",
        _ => "object",
    };

    /// <summary>'key' in obj: a record's key, or a set field of an object.</summary>
    public static bool In(string key, object? o)
    {
        if (o is null) return false;
        if (o is System.Collections.IDictionary d) return d.Contains(key);
        var prop = o.GetType().GetProperty(key);
        return prop != null && prop.GetValue(o) != null;
    }

    /// <summary>JSON.stringify, for comparing (as the sim uses it) and for logs.</summary>
    public static string Json_stringify(object? x) => System.Text.Json.JsonSerializer.Serialize(x, StateJson.Options);

    /// <summary>Object.assign(target, source): source's set fields copied onto target.</summary>
    public static V Object_assign<V>(V target, object? source) where V : class
    {
        if (source is null) return target;
        foreach (var sp in source.GetType().GetProperties())
        {
            var v = sp.GetValue(source);
            if (v is null) continue;
            var tp = target.GetType().GetProperty(sp.Name);
            if (tp != null && tp.CanWrite) tp.SetValue(target, v);
        }
        return target;
    }

    /// <summary>Object.fromEntries.</summary>
    public static JsRecord<V> Object_fromEntries<V>(IEnumerable<(string, V)> entries)
    {
        var r = new JsRecord<V>();
        foreach (var (k, v) in entries) r[k] = v;
        return r;
    }

    /// <summary>A whole-number field (a coordinate) set from a number: as JS stores it, when it's whole (Math.floor and friends return doubles).</summary>
    public static int I(int i) => i;
    public static int I(double d) => (int)d;
    public static int? I(int? i) => i;
    public static int? I(double? d) => d is double v ? (int)v : null;

    /// <summary>A computed index (list[i], record[key]): a whole number for lists, as JS arrays read them.</summary>
    public static int Idx(int i) => i;
    public static int Idx(double d) => (int)d;
    public static string Idx(string s) => s;

    /// <summary>for...of over a list: by index, reading the length each step (so items added during the loop are seen, as in JS).</summary>
    public static IEnumerable<V> Iter<V>(List<V> list)
    {
        for (int i = 0; i < list.Count; i++) yield return list[i];
    }

    public static IEnumerable<V> Iter<V>(IEnumerable<V> xs) => xs;
    public static IEnumerable<string> Iter(string s) => s.Select(c => c.ToString());

    /// <summary>Number(x).</summary>
    public static double Num(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (string.IsNullOrWhiteSpace(s) ? 0 : double.NaN);
    public static double Num(double d) => d;
    public static double Num(bool b) => b ? 1 : 0;

    public static List<string> ObjectKeys<V>(Dictionary<string, V> r) => r.Keys.ToList();
    public static List<V> ObjectValues<V>(Dictionary<string, V> r) => r.Values.ToList();
    public static List<(string, V)> ObjectEntries<V>(Dictionary<string, V> r) => r.Select(kv => (kv.Key, kv.Value)).ToList();
}
