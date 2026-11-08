// The dormant dead (state.dormant: bare positions) bucketed by place, so "which are near here" costs the
// cells round a point, not the whole list. Herds.wakeDormant (every 10 ticks, per survivor), Noise (every
// noise) and the first-person view (drawing them before they wake) ask through here.
//
// The index is a cache, never saved: rebuilt from the list when it's a different list (a load, a filter)
// or its length isn't what the index last saw, and kept up as it goes by add/remove below (every place
// in the sim that adds or takes away a dormant zombie goes through them). Answers come back in the
// list's order (each entry keeps the sequence it was added in; the list only ever grows at the end
// and loses entries from anywhere, so that order is the list's), so waking through the index does
// exactly what walking the whole list did (rule 9).
#nullable disable warnings
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static class Dormant
{
    /// <summary>The side of a cell (tiles).</summary>
    public const int CELL = 16;

    sealed class Index
    {
        public int count;
        public long next;
        public readonly Dictionary<long, List<(long seq, DormantZombie d)>> cells = new();
    }

    static readonly ConditionalWeakTable<List<DormantZombie>, Index> indexes = new();

    static long key(double x, double y) => ((long)JsMath.floor(y / CELL) << 32) ^ (uint)(long)JsMath.floor(x / CELL);

    static Index of(List<DormantZombie> list)
    {
        if (indexes.TryGetValue(list, out var ix) && ix.count == list.Count) return ix;
        ix = new Index();
        foreach (var d in list) put(ix, d);
        ix.count = list.Count;
        indexes.AddOrUpdate(list, ix);
        return ix;
    }

    static void put(Index ix, DormantZombie d)
    {
        var k = key(d.x, d.y);
        if (!ix.cells.TryGetValue(k, out var cell)) ix.cells[k] = cell = new();
        cell.Add((ix.next++, d));
    }

    /// <summary>A new dormant zombie, at the end of the list.</summary>
    public static void add(SimState state, DormantZombie d)
    {
        var ix = of(state.dormant);
        state.dormant.push(d);
        put(ix, d);
        ix.count = state.dormant.Count;
    }

    /// <summary>Takes one away (it woke, or left with an expedition).</summary>
    public static void remove(SimState state, DormantZombie d)
    {
        var ix = of(state.dormant);
        var i = state.dormant.indexOf(d);
        if (i < 0) return;
        state.dormant.splice(i, 1);
        if (ix.cells.TryGetValue(key(d.x, d.y), out var cell))
        {
            var j = cell.FindIndex((e) => ReferenceEquals(e.d, d));
            if (j >= 0) cell.RemoveAt(j);
            if (cell.Count == 0) ix.cells.Remove(key(d.x, d.y));
        }
        ix.count = state.dormant.Count;
    }

    /// <summary>The dormant within `r` of (x, y), Manhattan or (manhattan false) each way, in the list's order.</summary>
    public static List<DormantZombie> near(SimState state, double x, double y, double r, bool manhattan = true)
    {
        var ix = of(state.dormant);
        var found = new List<(long seq, DormantZombie d)>();
        long cx0 = (long)JsMath.floor((x - r) / CELL), cx1 = (long)JsMath.floor((x + r) / CELL);
        long cy0 = (long)JsMath.floor((y - r) / CELL), cy1 = (long)JsMath.floor((y + r) / CELL);
        for (var cy = cy0; cy <= cy1; cy++)
            for (var cx = cx0; cx <= cx1; cx++)
            {
                if (!ix.cells.TryGetValue((cy << 32) ^ (uint)cx, out var cell)) continue;
                foreach (var e in cell)
                {
                    double dx = JsMath.abs(e.d.x - x), dy = JsMath.abs(e.d.y - y);
                    if (manhattan ? dx + dy <= r : dx <= r && dy <= r) found.Add(e);
                }
            }
        found.Sort((a, b) => a.seq.CompareTo(b.seq));
        return found.ConvertAll((e) => e.d);
    }

    /// <summary>The dormant within `r` (Manhattan) of any of the points, each once, in the list's order.</summary>
    public static List<DormantZombie> nearAny(SimState state, IEnumerable<Point> points, double r)
    {
        var ix = of(state.dormant);
        if (ix.count == 0) return [];
        var seen = new HashSet<DormantZombie>(ReferenceEqualityComparer.Instance);
        var found = new List<(long seq, DormantZombie d)>();
        foreach (var p in points)
        {
            long cx0 = (long)JsMath.floor((p.x - r) / CELL), cx1 = (long)JsMath.floor((p.x + r) / CELL);
            long cy0 = (long)JsMath.floor((p.y - r) / CELL), cy1 = (long)JsMath.floor((p.y + r) / CELL);
            for (var cy = cy0; cy <= cy1; cy++)
                for (var cx = cx0; cx <= cx1; cx++)
                {
                    if (!ix.cells.TryGetValue((cy << 32) ^ (uint)cx, out var cell)) continue;
                    foreach (var e in cell)
                        if (JsMath.abs(e.d.x - p.x) + JsMath.abs(e.d.y - p.y) <= r && seen.Add(e.d)) found.Add(e);
                }
        }
        found.Sort((a, b) => a.seq.CompareTo(b.seq));
        return found.ConvertAll((e) => e.d);
    }
}
