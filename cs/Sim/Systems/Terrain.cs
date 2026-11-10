// The land's large-scale shape (2026-11-10): what makes a generated world vary over hundreds of tiles, where nature's
// per-tile noise (NatureMod) only varies over tens. Made once per world from the seed (seed, size, map), cached, never
// saved, like the plan. On a lattice of points CELL tiles apart (bilinear between them):
//
//   elevation   fBm (four octaves, 640 tiles down to 80), its coordinates warped so ridges and valleys wander. A broad
//               valley follows natureFor's river line (48-72 tiles from the start, as the one river always ran), and
//               springs on that line near the start (SPRING) start a river there: the start's water, as before. The
//               start stands on a broad rise, so no lake lies against it (its roads would have to cross it).
//   moisture    fBm at 480 tiles, as its rank in the world (so a small world isn't all dry or all wet): wetter
//               land is more wooded and feeds more water to its rivers.
//   settlement  fBm at 600 tiles: where people settled (Towns siting), and where they didn't (wild country, cabins):
//               the least settled WILD_SHARE of the land (wildAt, per world) has no towns.
//
// Water from the elevation (priority flood: the lattice drains from the map's edge and the sea inward, every point to
// the neighbour it was reached from, so every point drains out, and a hollow fills to its rim):
//   lakes       where the filled level stands over the ground by more than lakeAt (set so LAKE_SHARE of the land is
//               lake, if its hollows are deep enough), the shore roughened by fBm;
//   rivers      where the water draining through a point (rain over its catchment, more where it's wet) reaches
//               RIVER_FLOW: a curve through the lattice points (each nudged by a hash, so they don't run in lines), from
//               between a point and the one upstream of it to between it and the one below; a tributary joins the
//               middle of the curve it flows into; a river reaching the edge or the sea runs on to it. Wider
//               downstream (log of the flow), RIVER_MIN to RIVER_MAX tiles.
//
// Per tile, `riverDistance` looks up the river curves through a bucket grid (BUCKET tiles: only the curves near it),
// the point first warped a little so banks aren't smooth. Pure and thread-safe: built under a lock the first time any
// thread asks, immutable after.
using System.Text.Json.Serialization;
#nullable disable warnings
namespace LoonieRoo.Sim;

public partial class Nature
{
    /// <summary>This world's Terrain once asked for (Terrain.Of; derived, never saved).</summary>
    [JsonIgnore] public Terrain terrain { get; set; }
}

public sealed class Terrain
{
    /// <summary>Tiles between lattice points.</summary>
    public const int CELL = 16;
    /// <summary>The bucket grid for river curves, tiles a side.</summary>
    public const int BUCKET = 64;
    /// <summary>The share of lattice points a world's lakes cover: the depth that makes a lake is set per world to it
    /// (the depth of hollows varies a lot from seed to seed), but never shallower than LAKE_MIN.</summary>
    public const double LAKE_SHARE = 0.06, LAKE_MIN = 0.04;
    /// <summary>Water standing this much over the ground (of elevation's 0..1) is a lake, in this world.</summary>
    public readonly double lakeAt;
    /// <summary>The share of the land that's wild country (no towns), and the settlement under which it is, in this world.</summary>
    public const double WILD_SHARE = 0.25;
    public readonly double wildAt;
    /// <summary>The flow (lattice points of rain, ~CELL² tiles each) at which a stream shows as a river.</summary>
    public const double RIVER_FLOW = 220;
    public const double RIVER_MIN = 2, RIVER_MAX = 9;
    /// <summary>How far riverDistance looks past a bank (tiles): beyond it, this.</summary>
    public const double FAR = 8;

    public readonly double seed;
    public readonly int size;
    /// <summary>Lattice points a side (the last at or past the map's far edge).</summary>
    public readonly int W;
    readonly float[] elev, depth, moist, settle, flow;
    readonly int[] down;
    readonly int BW;
    readonly Seg[][] buckets;

    readonly record struct Seg(double ax, double ay, double bx, double by, double half);

    static readonly object gate = new();
    static readonly List<(string key, Terrain t)> cache = new();
    const int KEEP = 4;

    /// <summary>A world's terrain, made the first time it's asked for.</summary>
    public static Terrain Of(Nature n)
    {
        if (n.terrain is { } mine) return mine;
        string key = $"{Js.Str(n.seed)}|{Js.Str(n.size)}|{n.map}|{Js.Str(n.riverBase)}|{n.riverAlongX}";
        lock (gate)
        {
            foreach (var (k, t) in cache)
                if (k == key) return n.terrain = t;
            var made = new Terrain(n);
            cache.Add((key, made));
            if (cache.Count > KEEP) cache.RemoveAt(0);
            return n.terrain = made;
        }
    }

    Terrain(Nature n)
    {
        seed = n.seed;
        size = (int)n.size;
        W = size / CELL + 2;
        int N = W * W;
        elev = new float[N];
        moist = new float[N];
        settle = new float[N];
        for (int j = 0; j < W; j++)
            for (int i = 0; i < W; i++)
            {
                double x = i * CELL, y = j * CELL;
                int at = j * W + i;
                elev[at] = (float)Elevation(n, x, y);
                moist[at] = (float)Stretch(NatureMod.fbm(seed, x, y, 480, 0x5e20, 3), 2.2);
                settle[at] = (float)Stretch(NatureMod.fbm(seed, x, y, 600, 0x5e30, 3), 2.4);
            }
        RankInPlace(moist);
        // Priority flood from the edge (and the sea): the lowest reached point first, ties by index.
        var filled = new float[N];
        down = new int[N];
        var seen = new bool[N];
        var order = new List<int>(N);
        var open = new PriorityQueue<int, (float, int)>();
        for (int j = 0; j < W; j++)
            for (int i = 0; i < W; i++)
            {
                int at = j * W + i;
                down[at] = -1;
                bool edge = i == 0 || j == 0 || i == W - 1 || j == W - 1;
                if (!edge && !NatureMod.isSea(n, i * CELL, j * CELL)) continue;
                seen[at] = true;
                filled[at] = elev[at];
                open.Enqueue(at, (filled[at], at));
            }
        ReadOnlySpan<(int, int)> around = [(0, -1), (-1, 0), (1, 0), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];
        while (open.TryDequeue(out int c, out _))
        {
            order.Add(c);
            int ci = c % W, cj = c / W;
            foreach (var (dx, dy) in around)
            {
                int i = ci + dx, j = cj + dy;
                if (i < 0 || j < 0 || i >= W || j >= W) continue;
                int nb = j * W + i;
                if (seen[nb]) continue;
                seen[nb] = true;
                filled[nb] = Math.Max(elev[nb], filled[c] + 1e-6f);
                down[nb] = c;
                open.Enqueue(nb, (filled[nb], nb));
            }
        }
        depth = new float[N];
        for (int k = 0; k < N; k++) depth[k] = filled[k] - elev[k];
        var sorted = (float[])depth.Clone();
        Array.Sort(sorted);
        lakeAt = Math.Max(LAKE_MIN, sorted[Math.Clamp((int)((1 - LAKE_SHARE) * N), 0, N - 1)]);
        var bySettled = (float[])settle.Clone();
        Array.Sort(bySettled);
        wildAt = bySettled[Math.Clamp((int)(WILD_SHARE * N), 0, N - 1)];
        // Flow: each point's rain, passed downhill, last reached first.
        flow = new float[N];
        var springs = Springs(n);
        for (int k = order.Count - 1; k >= 0; k--)
        {
            int c = order[k];
            flow[c] += 0.5f + moist[c] + (springs.Contains(c) ? SPRING : 0);
            if (down[c] >= 0) flow[down[c]] += flow[c];
        }
        BW = (size + BUCKET - 1) / BUCKET;
        buckets = LayRivers(N);
    }

    /// <summary>Water added at each lattice point on the main river's line near the start: enough that a river runs from
    /// there whatever the catchment (the start's water, as the one river always was).</summary>
    public const float SPRING = (float)RIVER_FLOW;
    /// <summary>How far along the main river's line (tiles each way of the start) the springs are.</summary>
    public const int SPRING_REACH = 96;

    /// <summary>The lattice points nearest natureFor's river line within SPRING_REACH of the start, along it (two in from
    /// the edge at least).</summary>
    HashSet<int> Springs(Nature n)
    {
        var at = new HashSet<int>();
        double s0 = n.riverAlongX ? n.start.x : n.start.y;
        for (double along = s0 - SPRING_REACH; along <= s0 + SPRING_REACH; along += CELL)
        {
            double across = n.riverBase + 28 * (NatureMod.noiseD(n.seed, along, 0, 40, 6) - 0.5);
            double x = n.riverAlongX ? along : across, y = n.riverAlongX ? across : along;
            // Kept two points in from the edge (an edge point is an outlet: nothing would flow from it), so a small
            // world whose river line runs off the map still has its river, just inside.
            int lo = Math.Min(2, (W - 1) / 2), hi = Math.Max(lo, W - 3);
            int i = Math.Clamp((int)Math.Round(x / CELL), lo, hi), j = Math.Clamp((int)Math.Round(y / CELL), lo, hi);
            at.Add(j * W + i);
        }
        return at;
    }

    /// <summary>Each value replaced by its rank among them, 0..1 (ties by index): the same spread in every world.</summary>
    static void RankInPlace(float[] f)
    {
        var idx = new int[f.Length];
        for (int k = 0; k < idx.Length; k++) idx[k] = k;
        Array.Sort(idx, (a, b) => f[a] != f[b] ? f[a].CompareTo(f[b]) : a.CompareTo(b));
        var ranked = new float[f.Length];
        for (int k = 0; k < idx.Length; k++) ranked[idx[k]] = idx.Length == 1 ? 0.5f : (float)k / (idx.Length - 1);
        Array.Copy(ranked, f, f.Length);
    }

    /// <summary>0.5 + (v - 0.5) x k, kept in 0..1: fBm crowds round the middle.</summary>
    static double Stretch(double v, double k) => Math.Clamp(0.5 + (v - 0.5) * k, 0, 1);

    static double Elevation(Nature n, double x, double y)
    {
        double wx = x + 160 * (NatureMod.noiseD(n.seed, x, y, 300, 0x5e01) - 0.5) * 2;
        double wy = y + 160 * (NatureMod.noiseD(n.seed, x, y, 300, 0x5e02) - 0.5) * 2;
        double e = Stretch(NatureMod.fbm(n.seed, wx, wy, 640, 0x5e10, 4), 2);
        // The main river's valley (natureFor's line, as the one river ran before).
        double along = n.riverAlongX ? x : y, across = n.riverAlongX ? y : x;
        double centre = n.riverBase + 28 * (NatureMod.noiseD(n.seed, along, 0, 40, 6) - 0.5);
        double d = (across - centre) / 110;
        // The start stands on a rise (never in a hollow: no lake round it).
        double sx = (x - n.start.x) / 140, sy = (y - n.start.y) / 140;
        return e - 0.25 / (1 + d * d) + 0.3 / (1 + sx * sx + sy * sy);
    }

    // --- rivers ---

    bool IsRiver(int c) => flow[c] >= RIVER_FLOW;

    double Width(int c) => Math.Min(RIVER_MAX, RIVER_MIN + 1.4 * Math.Log2(flow[c] / RIVER_FLOW + 1) * 1.6);

    (double x, double y) Node(int c)
    {
        int i = c % W, j = c / W;
        double jx = (NatureMod.roll(seed, i, j, 0x5e60) - 0.5) * CELL * 0.7, jy = (NatureMod.roll(seed, i, j, 0x5e61) - 0.5) * CELL * 0.7;
        return (i * CELL + jx, j * CELL + jy);
    }

    static (double, double) Mid((double x, double y) a, (double x, double y) b) => ((a.x + b.x) / 2, (a.y + b.y) / 2);

    Seg[][] LayRivers(int N)
    {
        // Each river point's main upstream: the river point flowing into it with the most flow (ties: lowest index).
        var main = new int[N];
        Array.Fill(main, -1);
        for (int c = 0; c < N; c++)
        {
            int d = down[c];
            if (d < 0 || !IsRiver(c)) continue;
            if (main[d] < 0 || flow[c] > flow[main[d]]) main[d] = c;
        }
        var lists = new List<Seg>[BW * BW];
        void Add(double ax, double ay, double bx, double by, double w)
        {
            double half = w / 2, reach = half + FAR + 3;
            int b0x = (int)Math.Floor((Math.Min(ax, bx) - reach) / BUCKET), b1x = (int)Math.Floor((Math.Max(ax, bx) + reach) / BUCKET);
            int b0y = (int)Math.Floor((Math.Min(ay, by) - reach) / BUCKET), b1y = (int)Math.Floor((Math.Max(ay, by) + reach) / BUCKET);
            for (int by_ = Math.Max(0, b0y); by_ <= Math.Min(BW - 1, b1y); by_++)
                for (int bx_ = Math.Max(0, b0x); bx_ <= Math.Min(BW - 1, b1x); bx_++)
                    (lists[by_ * BW + bx_] ??= new()).Add(new Seg(ax, ay, bx, by, half));
        }
        (double, double) Bez((double x, double y) a, (double x, double y) p, (double x, double y) b, double t) =>
            ((1 - t) * (1 - t) * a.x + 2 * (1 - t) * t * p.x + t * t * b.x, (1 - t) * (1 - t) * a.y + 2 * (1 - t) * t * p.y + t * t * b.y);
        for (int c = 0; c < N; c++)
        {
            int d = down[c];
            if (d < 0 || !IsRiver(c)) continue;
            var pc = Node(c);
            var start = main[c] >= 0 ? Mid(Node(main[c]), pc) : pc;
            var end = Mid(pc, Node(d));
            double w = Width(c);
            var prev = start;
            for (int k = 1; k <= 4; k++)
            {
                var q = Bez(start, pc, end, k / 4.0);
                Add(prev.Item1, prev.Item2, q.Item1, q.Item2, w);
                prev = q;
            }
            // Into the edge or the sea; or, as a tributary, on to the middle of the curve it joins.
            if (down[d] < 0) Add(end.Item1, end.Item2, Node(d).x, Node(d).y, w);
            else if (main[d] != c && main[d] >= 0)
            {
                var pd = Node(d);
                var joinAt = Bez(Mid(Node(main[d]), pd), pd, Mid(pd, Node(down[d])), 0.5);
                Add(end.Item1, end.Item2, joinAt.Item1, joinAt.Item2, w);
            }
        }
        var out_ = new Seg[BW * BW][];
        for (int k = 0; k < out_.Length; k++) out_[k] = lists[k]?.ToArray() ?? [];
        return out_;
    }

    /// <summary>Tiles from (x, y) to the nearest river's bank: 0 in the water, FAR at most.</summary>
    public double riverDistance(int x, int y)
    {
        double qx = x + 0.5 + 2.5 * (NatureMod.noise(seed, x, y, 9, 0x5e40) - 0.5) * 2;
        double qy = y + 0.5 + 2.5 * (NatureMod.noise(seed, x, y, 9, 0x5e41) - 0.5) * 2;
        int bx = (int)Math.Floor(qx / BUCKET), by = (int)Math.Floor(qy / BUCKET);
        if (bx < 0 || by < 0 || bx >= BW || by >= BW) return FAR;
        double best = FAR;
        foreach (var s in buckets[by * BW + bx])
        {
            double dx = s.bx - s.ax, dy = s.by - s.ay, len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(((qx - s.ax) * dx + (qy - s.ay) * dy) / len2, 0, 1);
            double ex = s.ax + t * dx - qx, ey = s.ay + t * dy - qy;
            double d = JsMath.sqrt(ex * ex + ey * ey) - s.half;
            if (d < best) best = d;
        }
        return Math.Max(0, best);
    }

    // --- the fields, between lattice points ---

    double Lerp(float[] f, double x, double y)
    {
        double gx = Math.Clamp(x / CELL, 0, W - 1.001), gy = Math.Clamp(y / CELL, 0, W - 1.001);
        int i = (int)gx, j = (int)gy;
        double fx = gx - i, fy = gy - j;
        int at = j * W + i;
        double top = f[at] + (f[at + 1] - f[at]) * fx, bottom = f[at + W] + (f[at + W + 1] - f[at + W]) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>The ground's height, 0..1 (low to high), at a tile.</summary>
    public double elevation(double x, double y) => Lerp(elev, x, y);
    /// <summary>How wet the land is, 0..1.</summary>
    public double moisture(double x, double y) => Lerp(moist, x, y);
    /// <summary>How settled the land is, 0..1 (towns stand where it's high).</summary>
    public double settlement(double x, double y) => Lerp(settle, x, y);
    /// <summary>How far water stands over the ground here (0 off a lake).</summary>
    public double lakeDepth(double x, double y) => Lerp(depth, x, y);

    /// <summary>A lake on this tile: the standing water, its shore roughened a little.</summary>
    public bool isLake(int x, int y) => lakeDepth(x + 0.5, y + 0.5) + SHORE * (NatureMod.fbm(seed, x, y, 24, 0x5e50, 3) - 0.5) * 2 > lakeAt;

    /// <summary>How much the shore wanders (of lake depth): bays and points rather than the lattice's smooth curves.</summary>
    public const double SHORE = 0.03;

    /// <summary>The lattice point lattice point k drains into (k = j * W + i), or -1 at an outlet (the edge, the sea).</summary>
    public int downOf(int k) => down[k];

    /// <summary>The water draining through the lattice point nearest (x, y) (the tools, the tests).</summary>
    public double flowAt(double x, double y)
    {
        int i = Math.Clamp((int)Math.Round(x / CELL), 0, W - 1), j = Math.Clamp((int)Math.Round(y / CELL), 0, W - 1);
        return flow[j * W + i];
    }
}
