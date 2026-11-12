// The dead in a crowd (part of Zombies): who is near whom, without asking everyone.
//   zombiesWithin   how many of the dead stand near a point (the dead bucketed by tile once a tick)
//   survivorsNear   who is within a bite of a zombie (survivors by tile, once a tick)
//   pushApart       bodies closer than CROWD move apart, in a fixed order
//   ZombieBuckets   the dead by tile, which the first and the last share
// All of it is made again from the lists each tick: nothing here is saved, and the dictionaries are only looked up,
// never walked (rule 9).
#nullable disable warnings
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

public static partial class Zombies
{
    /// <summary>What zombiesWithin keeps for a list of zombies: how many times it has been added to or taken from in
    /// place, and its buckets. A derived cache, never saved, held weakly by the list: the dead are a new list each
    /// dawn and whenever one arrives from a herd, and a list the state has dropped takes its notes with it.</summary>
    sealed class ListNotes
    {
        public double edits;
        public BucketsEntry? buckets;
    }

    static readonly ConditionalWeakTable<List<Zombie>, ListNotes> listNotes = new();

    public static void edited(List<Zombie> list) => listNotes.GetOrCreateValue(list).edits += 1;

    /// <summary>How many zombies stand within `r` tiles (each way) of a point. The dead are bucketed by tile once a tick (and again if any are added or removed), and every one found is checked where it stands now, searching two tiles further than asked: a zombie knocked back since the buckets were made (it can't move further within the tick) is still counted exactly. (Every survivor counting every zombie for panic was hundreds of thousands of checks a tick with a big horde.)</summary>
    public static double zombiesWithin(SimState state, Point p, double r)
    {
        var list = state.zombies;
        var __d0 = state.grid;
        var width = __d0.width;
        var notes = listNotes.GetOrCreateValue(list);
        var edit = notes.edits;
        var b = notes.buckets;
        if ((b == null) || (b.tick != state.tick) || (b.edit != edit))
        {
            b = new BucketsEntry { tick = state.tick, edit = edit, cells = new ZombieBuckets(list, width) };
            notes.buckets = b;
        }
        double n = 0;
        var reach = r + 2;
        for (var y = p.y - reach; y <= (p.y + reach); y++)
        {
            for (var x = p.x - reach; x <= (p.x + reach); x++)
            {
                if ((x < 0) || (x >= width))
                {
                    continue;
                }
                var cells = b.cells;
                for (var i = cells.first(((double)(y) * (width)) + x); i >= 0; i = cells.next[i])
                {
                    var z = cells.zs[i];
                    if (Space.chebyshev(z.pos, p) <= r)
                    {
                        n++;
                    }
                }
            }
        }
        return n;
    }

    /// <summary>Survivors by the tile under them, once a tick, to find who's within a bite of a zombie without checking every survivor for every zombie. A bite reaches REACH (1.1 tiles) centre to centre, so anyone in reach is within 2 tiles each way. Answers in list order. Nobody moves while the dead do; if the list changes meanwhile (someone captured), it falls back to the whole list.</summary>
    public static Func<Point, List<Survivor>> survivorsNear(SimState state)
    {
        var list = state.survivors;
        var length = list.length;
        var __d2 = state.grid;
        var width = __d2.width;
        // Plain dictionaries: only looked up, never iterated.
        var order = new Dictionary<Survivor, double>(length);
        var cells = new Dictionary<double, List<Survivor>>(length);
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            order[s] = i;
            var t = Motion.tileOf(s.at);
            var k = ((double)(t.y) * (width)) + t.x;
            if (cells.TryGetValue(k, out var at))
            {
                at.push(s);
            }
            else
            {
                cells[k] = [s];
            }
        }
        Comparison<Survivor> byOrder = (a, b) => order[a].CompareTo(order[b]);
        // Every tile within 2 (each way) of a bucket, so a tile with nobody near costs one lookup, not 25. A bucket's
        // key k is found from tile (tx, ty) exactly when (k mod width, k div width) is within 2 of it each way. Only
        // worth making when the dead outnumber the buckets (it costs 25 entries a bucket); null: look every time.
        HashSet<long>? near = state.zombies.Count > cells.Count ? new HashSet<long>(cells.Count * 25) : null;
        if (near != null) foreach (var k in cells.Keys)
        {
            var ky = JsMath.floor(k / width);
            int kx = (int)(k - ky * width);
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    near.Add(tileKey(kx + dx, (int)ky + dy));
                }
            }
        }
        // One list for every call, emptied each time: read it before the next call.
        List<Survivor> found = [];
        return (at) =>
        {
            if ((state.survivors != list) || (list.length != length))
            {
                return list;
            }
            Point t = new(Js.I(JsMath.floor(((double)(at.x) / (Motion.SUB)))), Js.I(JsMath.floor(((double)(at.y) / (Motion.SUB))))); // Motion.tileOf
            found.Clear();
            if ((near != null) && !near.Contains(tileKey(t.x, t.y)))
            {
                return found;
            }
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    var x = t.x + dx;
                    if ((x < 0) || (x >= width))
                    {
                        continue;
                    }
                    if (cells.TryGetValue(((double)((t.y + dy)) * (width)) + x, out var c))
                    {
                        found.pushAll(c);
                    }
                }
            }
            if (found.length > 1)
            {
                // By place in the list: distinct whole numbers, so any sort gives the one order.
                found.Sort(byOrder);
            }
            return found;
        };
    }

    static long tileKey(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>Crowds: zombies closer than CROWD push apart (half each, along the line between them), in a fixed order, sliding along walls. Only neighbours in nearby tiles are compared.</summary>
    public static void pushApart(SimState state)
    {
        var __d4 = state.grid;
        var width = __d4.width;
        var height = __d4.height;
        var buckets = new ZombieBuckets(state.zombies, width);
        // The others by tile, and (othersAround) each listed under every in-grid tile within 1 of the tile its key names:
        // the ones a zombie on an in-grid tile meets, in one lookup instead of nine (sorted by order after, as before).
        var others = new Dictionary<double, List<PushApartOthers>>();
        // Worth making only when the dead outnumber the others (it costs nine entries for each); null: look every time.
        var othersAround = state.zombies.Count > (state.survivors.Count + state.residents.Count + state.dogs.Count + state.animals.Count) ? new Dictionary<double, List<PushApartOthers>>() : null;
        double order = 0;
        foreach (var list in new List<IEnumerable<Body>> { state.survivors, state.residents, state.dogs, state.animals })
        {
            foreach (var s in Js.Iter(list))
            {
                var k = ((double)(s.pos.y) * (width)) + s.pos.x;
                PushApartOthers entry = new PushApartOthers { s = s, order = order++ };
                if (others.TryGetValue(k, out var at))
                {
                    at.push(entry);
                }
                else
                {
                    others[k] = [entry];
                }
                if (othersAround == null)
                {
                    continue;
                }
                // The tile its key names (its own, unless it stands off the grid's sides).
                var ky = JsMath.floor(k / width);
                var kx = k - ky * width;
                if ((ky < 0) || (ky >= height))
                {
                    continue;
                }
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = kx + dx;
                        var ny = ky + dy;
                        if ((((nx < 0) || (ny < 0)) || (nx >= width)) || (ny >= height))
                        {
                            continue;
                        }
                        var nk = ((double)(ny) * (width)) + nx;
                        if (othersAround.TryGetValue(nk, out var around))
                        {
                            around.Add(entry);
                        }
                        else
                        {
                            othersAround[nk] = [entry];
                        }
                    }
                }
            }
        }
        List<PushApartOthers> near = []; // one list, emptied for each zombie
        foreach (var z in Js.Iter(state.zombies))
        {
            if (z.crossing != null)
            {
                continue; // on a car or over a barricade (Crossings.cs): the crowd doesn't move it
            }
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = z.pos.x + dx;
                    var ny = z.pos.y + dy;
                    if ((((nx < 0) || (ny < 0)) || (nx >= width)) || (ny >= height))
                    {
                        continue;
                    }
                    for (var i = buckets.first(((double)(ny) * (width)) + nx); i >= 0; i = buckets.next[i])
                    {
                        var o = buckets.zs[i];
                        if ((o == z) || (string.CompareOrdinal(o.id, z.id) < 0) || (o.crossing != null))
                        {
                            continue;
                        }
                        var d2 = Motion.distance2(z.at, o.at);
                        if (d2 >= ((double)(CROWD) * (CROWD)))
                        {
                            continue;
                        }
                        var d = JsMath.sqrt(d2);
                        var ux = (d == 0) ? 1 : ((double)((o.at.x - z.at.x)) / (d));
                        var uy = (d == 0) ? 0 : ((double)((o.at.y - z.at.y)) / (d));
                        var half = JsMath.ceil(((double)((CROWD - d)) / (2)));
                        o.at = Motion.slide(state, o.at, JsMath.round(((double)(ux) * (half))), JsMath.round(((double)(uy) * (half))), moverOf(o));
                        z.at = Motion.slide(state, z.at, -JsMath.round(((double)(ux) * (half))), -JsMath.round(((double)(uy) * (half))), moverOf(z));
                    }
                }
            }
            near.Clear();
            if ((othersAround != null) && (z.pos.x >= 0) && (z.pos.y >= 0) && (z.pos.x < width) && (z.pos.y < height))
            {
                if (othersAround.TryGetValue(((double)(z.pos.y) * (width)) + z.pos.x, out var around))
                {
                    near.AddRange(around);
                }
            }
            else
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = z.pos.x + dx;
                        var ny = z.pos.y + dy;
                        if ((((nx < 0) || (ny < 0)) || (nx >= width)) || (ny >= height))
                        {
                            continue;
                        }
                        if (others.TryGetValue(((double)(ny) * (width)) + nx, out var at))
                        {
                            near.pushAll(at);
                        }
                    }
                }
            }
            if (near.length > 1)
            {
                // Distinct orders: any sort gives the one order.
                near.Sort((a, b) => a.order.CompareTo(b.order));
            }
            foreach (var __o5 in Js.Iter(near))
            {
                var s = __o5.s;
                var d2 = Motion.distance2(z.at, s.at);
                if (d2 >= ((double)(CROWD) * (CROWD)))
                {
                    continue;
                }
                var d = JsMath.sqrt(d2);
                var ux = (d == 0) ? 1 : ((double)((z.at.x - s.at.x)) / (d));
                var uy = (d == 0) ? 0 : ((double)((z.at.y - s.at.y)) / (d));
                z.at = Motion.slide(state, z.at, JsMath.round(((double)(ux) * ((CROWD - d)))), JsMath.round(((double)(uy) * ((CROWD - d)))), moverOf(z));
            }
        }
    }
}

/// <summary>The dead by tile (key y * width + x, where each stood when made), in list order within a tile: a copy of
/// the list, and for each a link to the next in its tile (-1 at the end), so no list per tile. The tiles' first links
/// are in one open-addressed table on the whole-number keys (ints) (at most a quarter full, probed in turn): a dictionary of
/// doubles cost three times as much to make and twice as much to ask (2026-11-12, crowd probe).</summary>
public sealed class ZombieBuckets
{
    public readonly Zombie[] zs;
    public readonly int[] next;
    readonly int[] keys; // y * width + x fits an int up to a 46,000-tile square world
    readonly int[] heads; // the first in the slot's tile, -1 for an empty slot
    readonly int mask;

    public ZombieBuckets(List<Zombie> list, int width)
    {
        zs = list.ToArray();
        next = new int[zs.Length];
        var cap = 16;
        while (cap < zs.Length * 4)
        {
            cap <<= 1;
        }
        keys = new int[cap];
        heads = new int[cap];
        Array.Fill(heads, -1);
        mask = cap - 1;
        // Backwards, each put in front of its tile's chain: the chains run in list order.
        for (var i = zs.Length - 1; i >= 0; i--)
        {
            var z = zs[i];
            var k = (z.pos.y * width) + z.pos.x;
            var s = slot(k);
            if (heads[s] < 0)
            {
                keys[s] = k;
            }
            next[i] = heads[s];
            heads[s] = i;
        }
    }

    /// <summary>The key's slot: where it is, or the empty slot where it would go.</summary>
    int slot(int k)
    {
        var s = (int)(unchecked((uint)k * 0x9E3779B1u) >> 8) & mask;
        while ((heads[s] >= 0) && (keys[s] != k))
        {
            s = (s + 1) & mask;
        }
        return s;
    }

    /// <summary>The first in this tile's chain (index into zs), or -1.</summary>
    public int first(double k) => heads[slot((int)k)];
}
