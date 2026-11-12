// JavaScript's library, as the sim translated from TypeScript uses it: array and string methods on
// List<T> and string (C# 14 extension members, so the code reads as the TypeScript did), Math, Number,
// truthiness, String(x), Map and Set that keep insertion order, a Record that answers a missing key with
// the default, and a stable sort. Behaviour matches JavaScript where the sim depends on it.
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static class JsListExtensions
{
    extension(JsSet<int> a)
    {
        /// <summary>has with a JS number: only a whole one can be in it.</summary>
        public bool has(double x) => x == Math.Floor(x) && x >= int.MinValue && x <= int.MaxValue && a.has((int)x);
    }

    extension<T>(HashSet<T> a)
    {
        /// <summary>A Set used only for membership, as a plain HashSet (no order to keep).</summary>
        public bool has(T x) => a.Contains(x);
    }

    extension(List<string> a)
    {
        /// <summary>includes with a value of unknown type: only a string can be in it.</summary>
        public bool includes(object? x) => x is string s && a.Contains(s);
    }

    extension(List<int> a)
    {
        /// <summary>indexOf/includes with a JS number, on a list of whole numbers.</summary>
        public int indexOf(double x) => x == Math.Floor(x) && x >= int.MinValue && x <= int.MaxValue ? a.IndexOf((int)x) : -1;
        public bool includes(double x) => a.indexOf(x) >= 0;
        public int indexOf(int? x) => x is int v ? a.IndexOf(v) : -1;
        /// <summary>push of a whole number TypeScript narrowed to present.</summary>
        public int push(int? x) { a.Add(x!.Value); return a.Count; }
        /// <summary>The same with a JS number (an index made by multiplying, which is a double: no 32-bit overflow).</summary>
        public int indexOf(double? x) => x is double v ? a.indexOf(v) : -1;
        public int push(double x) { a.Add(checked((int)x)); return a.Count; }
        public int push(double? x) => a.push(x!.Value);
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
            // Another list (or array) straight in; anything lazy (it may read `a`) or `a` itself copied first.
            if (xs is ICollection<T> c && !ReferenceEquals(c, a)) a.AddRange(c);
            else a.AddRange(xs.ToList());
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

        public List<U> flatMap<U>(Func<T, int, IEnumerable<U>> f)
        {
            var o = new List<U>();
            for (int i = 0; i < a.Count; i++) o.AddRange(f(a[i], i));
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

        /// <summary>splice at a JS number (an index made by multiplying is a double).</summary>
        public List<T> splice(double start, int deleteCount) => a.splice((int)start, deleteCount);
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
            if (a.Count < 2) return a;
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
        /// <summary>a.localeCompare(b): a culture-aware order (ICU's, as in Node), as -1, 0 or 1.</summary>
        public int localeCompare(string b) => Math.Sign(string.Compare(s, b, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CompareOptions.None));

        public string slice(int start, int? end = null)
        {
            int n = s.Length;
            int a = start < 0 ? Math.Max(0, n + start) : Math.Min(start, n);
            int b = end is int e ? (e < 0 ? Math.Max(0, n + e) : Math.Min(e, n)) : n;
            return b > a ? s.Substring(a, b - a) : "";
        }
        public string substring(int start, int? end = null) => s.slice(Math.Max(0, start), end is int e ? Math.Max(0, e) : null);
        public List<string> split(string sep) => sep.Length == 0 ? s.Select(c => c.ToString()).ToList() : s.Split(sep).ToList();
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
        public string trimEnd() => s.TrimEnd();
        public string trimStart() => s.TrimStart();
        public string charAt(int i) => i >= 0 && i < s.Length ? s[i].ToString() : "";
        /// <summary>A string pattern: the first occurrence only, as in JS.</summary>
        public string replace(string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + to + s.Substring(i + from.Length);
        }
        public string replaceAll(string from, string to) => s.Replace(from, to, StringComparison.Ordinal);
        public string repeat(double n) => s.repeat((int)n);
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
    /// <summary>Math.hypot: as JS computes it (the square root of the sum of squares).</summary>
    public static double hypot(params double[] xs) => Math.Sqrt(xs.Sum(x => x * x));
    /// <summary>Math.min/max of fields TypeScript narrowed to present (a command's x0, x1); missing is NaN, as undefined gives.</summary>
    public static double min(int? a, int? b) => a is int x && b is int y ? Math.Min(x, y) : double.NaN;
    public static double max(int? a, int? b) => a is int x && b is int y ? Math.Max(x, y) : double.NaN;
    public static double min(IEnumerable<int> xs) => xs.Any() ? xs.Min() : double.PositiveInfinity;
    public static double max(IEnumerable<int> xs) => xs.Any() ? xs.Max() : double.NegativeInfinity;
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
    /// <summary>V8 gives x ** 2 as exactly x * x; .NET's Math.Pow(x, 2) can differ in the last bit (road edges moved a tile).</summary>
    public static double pow(double x, double y) => y == 2 ? x * x : Math.Pow(x, y);
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

/// <summary>String.fromCharCode and the like.</summary>
public static class JsString
{
    public static string fromCharCode(params double[] codes) => new(codes.Select(c => (char)(ushort)Js.ToUint32(c)).ToArray());
}

public static class JsNumber
{
    public const double MAX_SAFE_INTEGER = 9007199254740991;
    public const double POSITIVE_INFINITY = double.PositiveInfinity;
    public static bool isInteger(double x) => double.IsFinite(x) && Math.Floor(x) == x;
    public static bool isInteger(object? x) => x is double or int or float or long or uint && isInteger(Convert.ToDouble(x, CultureInfo.InvariantCulture));
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
        set { if (holes && !ContainsKey(key)) squeeze(); base[key] = value; }
    }

    public bool has(string key) => ContainsKey(key);

    // A Dictionary puts the next new key in the slot a removed one left, so it would enumerate in the middle, not
    // last as in JS, and a save (written without the hole) would play on in another order. A removal marks a hole;
    // the next new key first squeezes the entries back into order (once per batch of removals).
    bool holes;

    public new bool Remove(string key)
    {
        if (!base.Remove(key)) return false;
        holes = Count > 0;
        return true;
    }

    public new void Add(string key, V value)
    {
        if (holes) squeeze();
        base.Add(key, value);
    }

    public new void Clear()
    {
        base.Clear();
        holes = false;
    }

    void squeeze()
    {
        var kept = this.ToArray();
        base.Clear();
        foreach (var kv in kept) base.Add(kv.Key, kv.Value);
        holes = false;
    }
}

/// <summary>JavaScript's Map: insertion-ordered.</summary>
/// <summary>A JS Map/Set key: any value, null too (C# dictionaries take no null key).</summary>
readonly record struct JsKey<K>(K? Value);

public class JsMap<K, V> where K : notnull
{
    // Entries in insertion order in arrays (a deleted one leaves a hole, squeezed out when the arrays fill), and an
    // index from key to slot: no allocation per entry (it was a linked list).
    readonly Dictionary<JsKey<K>, int> index = new();
    K[] ks = [];
    V[] vs = [];
    bool[] used = [];
    int n;

    public JsMap() { }
    public JsMap(IEnumerable<(K, V)> items)
    {
        foreach (var (k, v) in items) set(k, v);
    }

    public int size => index.Count;
    public bool has(K k) => index.ContainsKey(new(k));
    public V get(K k) => index.TryGetValue(new(k), out var i) ? vs[i] : default!;
    public JsMap<K, V> set(K k, V v)
    {
        if (index.TryGetValue(new(k), out var i))
        {
            ks[i] = k;
            vs[i] = v;
            return this;
        }
        if (n == ks.Length) Grow();
        ks[n] = k;
        vs[n] = v;
        used[n] = true;
        index[new(k)] = n++;
        return this;
    }
    public bool delete(K k)
    {
        if (!index.Remove(new(k), out var i)) return false;
        ks[i] = default!;
        vs[i] = default!;
        used[i] = false;
        return true;
    }
    public void clear()
    {
        index.Clear();
        Array.Clear(ks, 0, n);
        Array.Clear(vs, 0, n);
        Array.Clear(used, 0, n);
        n = 0;
    }

    /// <summary>Full: squeeze out the holes if they're at least half, else double.</summary>
    void Grow()
    {
        if (index.Count * 2 <= n && n > 0)
        {
            int w = 0;
            for (int r = 0; r < n; r++)
            {
                if (!used[r]) continue;
                ks[w] = ks[r];
                vs[w] = vs[r];
                used[w] = true;
                index[new(ks[w])] = w;
                w++;
            }
            Array.Clear(ks, w, n - w);
            Array.Clear(vs, w, n - w);
            Array.Clear(used, w, n - w);
            n = w;
            if (n < ks.Length) return;
        }
        int cap = Math.Max(4, ks.Length * 2);
        Array.Resize(ref ks, cap);
        Array.Resize(ref vs, cap);
        Array.Resize(ref used, cap);
    }

    public List<K> keys()
    {
        var o = new List<K>(index.Count);
        for (int i = 0; i < n; i++) if (used[i]) o.Add(ks[i]);
        return o;
    }
    public List<V> values()
    {
        var o = new List<V>(index.Count);
        for (int i = 0; i < n; i++) if (used[i]) o.Add(vs[i]);
        return o;
    }
    public List<(K, V)> entries()
    {
        var o = new List<(K, V)>(index.Count);
        for (int i = 0; i < n; i++) if (used[i]) o.Add((ks[i], vs[i]));
        return o;
    }
    public void forEach(Action<V, K> f)
    {
        foreach (var (k, v) in entries()) f(v, k);
    }
}

/// <summary>JavaScript's Set: insertion-ordered.</summary>
public class JsSet<T> : IEnumerable<T> where T : notnull
{
    // As JsMap: items in insertion order in an array with holes, and an index from item to slot.
    readonly Dictionary<JsKey<T>, int> index = new();
    T[] xs = [];
    bool[] used = [];
    int n;

    public JsSet() { }
    public JsSet(IEnumerable<T> items)
    {
        foreach (var x in items) add(x);
    }

    public int size => index.Count;
    public bool has(T x) => index.ContainsKey(new(x));
    public JsSet<T> add(T x)
    {
        if (index.ContainsKey(new(x))) return this;
        if (n == xs.Length) Grow();
        xs[n] = x;
        used[n] = true;
        index[new(x)] = n++;
        return this;
    }
    public bool delete(T x)
    {
        if (!index.Remove(new(x), out var i)) return false;
        xs[i] = default!;
        used[i] = false;
        return true;
    }
    public void clear()
    {
        index.Clear();
        Array.Clear(xs, 0, n);
        Array.Clear(used, 0, n);
        n = 0;
    }

    void Grow()
    {
        if (index.Count * 2 <= n && n > 0)
        {
            int w = 0;
            for (int r = 0; r < n; r++)
            {
                if (!used[r]) continue;
                xs[w] = xs[r];
                used[w] = true;
                index[new(xs[w])] = w;
                w++;
            }
            Array.Clear(xs, w, n - w);
            Array.Clear(used, w, n - w);
            n = w;
            if (n < xs.Length) return;
        }
        int cap = Math.Max(4, xs.Length * 2);
        Array.Resize(ref xs, cap);
        Array.Resize(ref used, cap);
    }

    /// <summary>The items in insertion order (a copy).</summary>
    public List<T> values()
    {
        var o = new List<T>(index.Count);
        for (int i = 0; i < n; i++) if (used[i]) o.Add(xs[i]);
        return o;
    }
    /// <summary>A copy's enumerator, as before: changes during a loop over the set aren't seen.</summary>
    public IEnumerator<T> GetEnumerator() => values().GetEnumerator();
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
    /// <summary>An environment variable or a default (the harness: `Number(process.env.X ?? 300)`).</summary>
    public static object Q(string? a, double b) => a ?? (object)b;
    public static int Q(int a, int b) => a;
    public static int Q(int? a, int b) => a ?? b;

    /// <summary>a[k] ?? b: b when a has no such key or index (JS's undefined) or holds null there.</summary>
    public static V At<V>(JsRecord<V> a, string k, V b) => a.TryGetValue(k, out var v) && v is not null ? v : b;
    public static V At<V>(JsRecord<V> a, double k, V b) => At(a, Str(k), b);
    public static V At<V>(V[] a, double i, V b) => i >= 0 && i < a.Length && i == Math.Floor(i) && a[(int)i] is V v ? v : b;
    public static V At<V>(List<V> a, double i, V b) => i >= 0 && i < a.Count && i == Math.Floor(i) && a[(int)i] is V v ? v : b;
    public static V At<K, V>(JsMap<K, V> a, K k, V b) where K : notnull => a.has(k) && a.get(k) is V v ? v : b;

    /// <summary>a || b used as a value: a if truthy, else b.</summary>
    public static V Or<V>(V a, V b) => T((object?)a) ? a : b;

    /// <summary>x! in TypeScript: for a nullable value type, its value.</summary>
    public static V NN<V>(V? x) where V : struct => x!.Value;
    public static V NN<V>(V x) => x!;
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

    /// <summary>
    /// Number.prototype.toFixed to the letter: the integer n nearest x * 10^digits on the double's exact value (ties
    /// up), so 0.85 (really 0.8499999...) gives "0.8" as in JS, where rounding a scaled value would give "0.9".
    /// </summary>
    public static string ToFixed(double x, int digits)
    {
        if (double.IsNaN(x)) return "NaN";
        if (Math.Abs(x) >= 1e21 || double.IsInfinity(x)) return Str(x);
        if (x < 0) return "-" + ToFixed(-x, digits);
        long bits = BitConverter.DoubleToInt64Bits(x);
        int exp = (int)((bits >> 52) & 0x7FF);
        long man = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp = 1; else man |= 1L << 52;
        exp -= 1075; // x = man * 2^exp
        var num = man * System.Numerics.BigInteger.Pow(10, digits);
        var den = System.Numerics.BigInteger.One;
        if (exp > 0) num <<= exp; else den <<= -exp;
        var n = System.Numerics.BigInteger.DivRem(num, den, out var rem);
        if (rem * 2 >= den) n += 1;
        string s = n.ToString().PadLeft(digits + 1, '0');
        return digits > 0 ? s[..^digits] + "." + s[^digits..] : s;
    }

    /// <summary>x >>> n</summary>
    public static double Ushr(int x, int n) => (double)((uint)x >> (n & 31));
    public static double Ushr(double x, double n) => (double)(ToUint32(x) >> (ToInt32(n) & 31));
    // The bitwise operators: on whole numbers as they are; on JS numbers through ToInt32, as JS does.
    public static int Shr(int x, int n) => x >> n;
    public static int Shr(double x, double n) => ToInt32(x) >> ToInt32(n);
    public static int Shl(int x, int n) => x << n;
    public static int Shl(double x, double n) => ToInt32(x) << ToInt32(n);
    public static int And(int x, int n) => x & n;
    public static int And(double x, double n) => ToInt32(x) & ToInt32(n);
    public static int BitOr(int x, int n) => x | n;
    public static int BitOr(double x, double n) => ToInt32(x) | ToInt32(n);
    public static int Xor(int x, int n) => x ^ n;
    public static int Xor(double x, double n) => ToInt32(x) ^ ToInt32(n);
    public static int BitNot(int x) => ~x;
    public static int BitNot(double x) => ~ToInt32(x);

    /// <summary>Array.from({ length: n }, (_, i) => f(i)).</summary>
    /// <summary>Array.from(items, f): f of each item (and its index).</summary>
    public static List<U> ArrayFromMap<T, U>(IEnumerable<T> items, Func<T, U> f) => items.Select(f).ToList();
    public static List<U> ArrayFromMap<T, U>(IEnumerable<T> items, Func<T, int, U> f) => items.Select(f).ToList();
    public static List<V> ArrayFrom<V>(double n, Func<int, V> f) => ArrayFrom((int)n, f);
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

    /// <summary>{ a, ...src }: src's set properties (those not left as a fresh one has them: JS's missing) copied over.</summary>
    public static void AssignSet<V>(V target, V? src) where V : class, new()
    {
        if (src == null) return;
        var fresh = new V();
        foreach (var p in typeof(V).GetProperties())
        {
            if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            var v = p.GetValue(src);
            if (!Equals(v, p.GetValue(fresh))) p.SetValue(target, v);
        }
    }

    /// <summary>{ ...x } going into another class: a shallow copy of the properties both have (by name).</summary>
    public static V CopyAs<V>(object x) where V : class, new()
    {
        if (x is V same) return Clone(same);
        var o = new V();
        foreach (var to in typeof(V).GetProperties())
        {
            var from = x.GetType().GetProperty(to.Name);
            if (from != null && to.CanWrite && to.PropertyType.IsAssignableFrom(from.PropertyType)) to.SetValue(o, from.GetValue(x));
            else if (from != null && to.CanWrite)
            {
                var v = from.GetValue(x);
                if (v != null) to.SetValue(o, Convert.ChangeType(v, Nullable.GetUnderlyingType(to.PropertyType) ?? to.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return o;
    }

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

    /// <summary>A file in the repository (scenarios/...): looked for from the working folder up.</summary>
    public static string RepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, relative);
            if (File.Exists(f)) return f;
        }
        throw new FileNotFoundException(relative);
    }

    /// <summary>JSON.parse(text) as T: read as T (or as JSON nodes).</summary>
    public static V JsonParse<V>(string text) => System.Text.Json.JsonSerializer.Deserialize<V>(text, StateJson.Options)!;

    /// <summary>JSON.parse(JSON.stringify(x)): a deep copy, through JSON.</summary>
    public static V DeepClone<V>(V x) => System.Text.Json.JsonSerializer.Deserialize<V>(System.Text.Json.JsonSerializer.Serialize(x, StateJson.Options), StateJson.Options)!;

    /// <summary>A list or object literal written as a loose JS value (the tests' expected values): any items, any fields.</summary>
    public static JsLooseList Arr(List<object?> items) => new(items);
    /// <summary>{ ...src, k: v } as a loose JS value: src's fields (in order), then these.</summary>
    public static JsRecord<object?> ObjFrom(object? src, params (string key, object? value)[] fields)
    {
        var o = new JsRecord<object?>(AsRecord(src));
        foreach (var (k, v) in fields) o[k] = v;
        return o;
    }

    public static JsRecord<object?> Obj(params (string key, object? value)[] fields)
    {
        var o = new JsRecord<object?>();
        foreach (var (k, v) in fields) o[k] = v;
        return o;
    }

    /// <summary>{ ...first, ...: a record copied from first, then the rest added.</summary>
    public static JsRecord<V> RecordFrom<V>(Dictionary<string, V> first, Action<JsRecord<V>> rest)
    {
        var r = new JsRecord<V>(first);
        rest(r);
        return r;
    }

    /// <summary>{ ...into, ...from }: from's entries copied in (later ones win).</summary>
    public static void MergeInto<V>(Dictionary<string, V> into, Dictionary<string, V>? from)
    {
        if (from == null) return;
        foreach (var kv in from) into[kv.Key] = kv.Value;
    }

    /// <summary>(() => { ... })(): a function run where it's written.</summary>
    public static V Run<V>(Func<V> f) => f();
    public static void Run(Action f) => f();

    /// <summary>new Array(n).fill(v).</summary>
    public static List<V> Filled<V>(double n, V v) => Enumerable.Repeat(v, (int)n).ToList();

    /// <summary>[...s] for a string: its characters, as strings.</summary>
    public static List<string> Chars(string s) => s.Select(c => c.ToString()).ToList();

    /// <summary>[a, b, ...c] as a list in its own right: the parts joined.</summary>
    public static List<V> ListOf<V>(params IEnumerable<V>[] parts) => parts.SelectMany(p => p).ToList();

    /// <summary>Object.assign(target, source): source's set fields copied onto target.</summary>
    public static V Object_assign<V>(V target, object? source) where V : class
    {
        if (source is null) return target;
        // Records (a priorities table): key by key.
        if (target is System.Collections.IDictionary td && source is System.Collections.IDictionary sd)
        {
            foreach (System.Collections.DictionaryEntry kv in sd) td[kv.Key] = kv.Value;
            return target;
        }
        foreach (var sp in source.GetType().GetProperties())
        {
            if (sp.GetIndexParameters().Length > 0) continue;
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
    /// <summary>a[i] read: what's there, or the default (JS's undefined) for an index outside the list or not whole.</summary>
    public static T Get<T>(List<T> a, double i) => i >= 0 && i < a.Count && i == Math.Floor(i) ? a[(int)i] : default!;
    public static T Get<T>(List<T> a, int i) => (uint)i < (uint)a.Count ? a[i] : default!;
    public static T Get<T>(T[] a, double i) => i >= 0 && i < a.Length && i == Math.Floor(i) ? a[(int)i] : default!;
    public static T Get<T>(IReadOnlyList<T> a, double i) => i >= 0 && i < a.Count && i == Math.Floor(i) ? a[(int)i] : default!;
    public static double Get<T>(TypedArray<T> a, double i) where T : struct => a[i];
    public static V Get<V>(Dictionary<string, V> r, string k) => r.TryGetValue(k, out var v) ? v : default!;
    public static V Get<V>(Dictionary<string, V> r, double k) => Get(r, Str(k));
    public static string? Get(string s, double i) => i >= 0 && i < s.Length && i == Math.Floor(i) ? s[(int)i].ToString() : null;

    /// <summary>a[k] === undefined: no such key, or past the list's end (or a null there).</summary>
    public static bool Missing<V>(Dictionary<string, V> r, string k) => !r.TryGetValue(k, out var v) || v is null;
    public static bool Missing<V>(Dictionary<string, V> r, double k) => Missing(r, Str(k));
    public static bool Missing<T>(List<T> a, double i) => !(i >= 0 && i < a.Count && i == Math.Floor(i)) || a[(int)i] is null;
    public static bool Missing<T>(T[] a, double i) => !(i >= 0 && i < a.Length && i == Math.Floor(i)) || a[(int)i] is null;
    public static bool Missing(string s, double i) => !(i >= 0 && i < s.Length && i == Math.Floor(i));
    public static bool Missing<T>(TypedArray<T> a, double i) where T : struct => !(i >= 0 && i < a.length && i == Math.Floor(i));

    public static int Idx(int i) => i;
    public static int Idx(double d) => (int)d;
    public static string Idx(string s) => s;

    /// <summary>for...of over a list: by index, reading the length each step (so items added during the loop are seen, as in JS).</summary>
    /// A struct, so a foreach over it allocates nothing (it was an iterator method: an object per loop).
    public static ListIter<V> Iter<V>(List<V> list) => new(list);

    public static IEnumerable<V> Iter<V>(IEnumerable<V> xs) => xs;
    /// <summary>for (const [k, v] of map): its entries, in insertion order.</summary>
    public static IEnumerable<(K, V)> Iter<K, V>(JsMap<K, V> m) where K : notnull => m.entries();
    public static IEnumerable<string> Iter(string s) => s.Select(c => c.ToString());

    /// <summary>Number(x).</summary>
    public static double Num(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (string.IsNullOrWhiteSpace(s) ? 0 : double.NaN);
    public static double Num(double d) => d;
    /// <summary>Number(x) for a value of unknown type.</summary>
    public static double Num(object? x) => x switch
    {
        null => 0,
        double d => d,
        int i => i,
        float f => f,
        long l => l,
        uint u => u,
        bool b => b ? 1 : 0,
        string s => Num(s),
        _ => double.NaN,
    };

    /// <summary>a === b for values of unknown type: numbers by value (whatever their C# type), strings by content, the rest by reference.</summary>
    public static bool Eq(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is double or int or float or long or uint && b is double or int or float or long or uint) return Num(a) == Num(b);
        if (a is string sa && b is string sb) return sa == sb;
        if (a is bool ba && b is bool bb) return ba == bb;
        return ReferenceEquals(a, b);
    }

    /// <summary>x as Record&lt;string, unknown&gt;: a record as it is, or an object's properties by name (a copy).</summary>
    /// <summary>x as Record&lt;string, V&gt;: a record of V as it is, or one made from its entries.</summary>
    public static JsRecord<V> AsRecord<V>(object? x)
    {
        if (x is JsRecord<V> r) return r;
        var o = new JsRecord<V>();
        foreach (var kv in AsRecord(x)) o[kv.Key] = (V)kv.Value!;
        return o;
    }

    public static JsRecord<object?> AsRecord(object? x)
    {
        if (x is JsRecord<object?> r) return r;
        var o = new JsRecord<object?>();
        if (x is System.Text.Json.Nodes.JsonObject jo)
        {
            foreach (var kv in jo) o[kv.Key] = kv.Value;
            return o;
        }
        if (x is System.Collections.IDictionary d)
        {
            foreach (System.Collections.DictionaryEntry kv in d) o[Convert.ToString(kv.Key, CultureInfo.InvariantCulture)!] = kv.Value;
            return o;
        }
        if (x is null || x is string || x.GetType().IsPrimitive) return o;
        foreach (var prop in x.GetType().GetProperties()) if (prop.GetIndexParameters().Length == 0) o[prop.Name] = prop.GetValue(x);
        return o;
    }
    public static double Num(bool b) => b ? 1 : 0;

    public static List<string> ObjectKeys<V>(Dictionary<string, V> r) => r.Keys.ToList();
    public static List<V> ObjectValues<V>(Dictionary<string, V> r) => r.Values.ToList();
    public static List<(string, V)> ObjectEntries<V>(Dictionary<string, V> r) => r.Select(kv => (kv.Key, kv.Value)).ToList();
}

/// <summary>A JS regular expression literal, on .NET's Regex: ECMAScript behaviour (ASCII \d, \w, \s) unless the
/// u flag asks for Unicode (\p{L}); g replaces every match, else only the first.</summary>
public sealed class JsRegExp
{
    public readonly System.Text.RegularExpressions.Regex re;
    public readonly bool global;

    public JsRegExp(string pattern, string flags)
    {
        global = flags.Contains('g');
        var opts = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (flags.Contains('i')) opts |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        if (flags.Contains('m')) opts |= System.Text.RegularExpressions.RegexOptions.Multiline;
        if (!flags.Contains('u') && !flags.Contains('i')) opts |= System.Text.RegularExpressions.RegexOptions.ECMAScript;
        re = new System.Text.RegularExpressions.Regex(pattern, opts);
    }

    public bool test(string s) => re.IsMatch(s);
}

public static class JsRegExpExtensions
{
    extension(string s)
    {
        public string replace(JsRegExp r, string to) => r.global ? r.re.Replace(s, to) : r.re.Replace(s, to, 1);
        public string replaceAll(JsRegExp r, string to) => r.re.Replace(s, to);
    }
}

/// <summary>A list literal written as a loose JS value (Js.Arr).</summary>
public sealed class JsLooseList(IEnumerable<object?> items) : List<object?>(items);

/// <summary>Js.Iter over a list: by index, reading the length each step (items added during the loop are seen, as in
/// JS; no version check). A struct with a struct enumerator, so a foreach over it allocates nothing.</summary>
public readonly struct ListIter<V>(List<V> list) : IEnumerable<V>
{
    public Enumerator GetEnumerator() => new(list);
    IEnumerator<V> IEnumerable<V>.GetEnumerator() => new Enumerator(list);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => new Enumerator(list);

    public struct Enumerator(List<V> list) : IEnumerator<V>
    {
        int i;
        V current = default!;
        public readonly V Current => current;
        readonly object? System.Collections.IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (i < list.Count)
            {
                current = list[i++];
                return true;
            }
            return false;
        }

        public void Reset() => i = 0;
        public readonly void Dispose() { }
    }
}
