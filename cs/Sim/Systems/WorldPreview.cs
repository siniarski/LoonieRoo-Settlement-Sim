// A small picture of a generated world (2026-11-10): what the New game screen shows as you pick a seed, a size and a
// map, and what the Load screen shows of a save. A square of PX x PX pixels (fewer on small worlds: a pixel is at
// least a tile), each one of the kinds below; the game colours them.
//
// Made in stages, cheapest first, so a picture can be shown at once and filled in (`build`, on a worker thread):
//   1. nature (NatureMod, per tile: at each pixel's middle, or the most of nine across it where a pixel is SAMPLE_AT
//      tiles or more, so a big world isn't static): land, forest, water, and a quarantine fence;
//   2. each region's towns (WorldRegions.TownsOf: sited, not planned), as discs, nearest the start first;
//   3. on worlds of up to 2 x 2 regions, every region's plan (WorldRegions.Region): lots (built, or fields), country
//      roads, town streets. A bigger world shows its towns only (a few planned regions would stand out as a square,
//      and a pixel is too coarse there for streets).
// Pure: the same picture from the same seed, size and map, made on any thread (WorldRegions is thread-safe for
// planning). Reads no sim state, so nothing in the game depends on it.
//
// A save carries what the picture needs of it (`ofSave`: SavePreview, beside the state in the file, not in it): the
// world, which pixels you've explored, and where you are.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class WorldPreview
{
    /// <summary>The picture's side in pixels, at most (a world smaller than this gets a pixel per tile).</summary>
    public const int PX = 256;

    public const byte LAND = 0, FOREST = 1, WATER = 2, TOWN = 3, ROAD = 4, STREET = 5, BUILT = 6, FIELD = 7, FENCE = 8;

    /// <summary>The picture's side for a world this size.</summary>
    public static int sideFor(double size) => (int)Math.Min(PX, size);

    /// <summary>The tiles pixel i covers along one axis: [lo, hi).</summary>
    static (int lo, int hi) Span(int i, int px, int size) => ((int)((long)i * size / px), (int)((long)(i + 1) * size / px));

    /// <summary>Tiles to a pixel from which nature is sampled nine times a pixel (the most common wins).</summary>
    public const int SAMPLE_AT = 4;

    static byte NatureAt(Nature n, int x, int y) => NatureMod.isWater(n, x, y) ? WATER : NatureMod.isForest(n, x, y) ? FOREST : LAND;

    /// <summary>Stage 1: the natural world, a pixel at a time.</summary>
    public static byte[] nature(Nature n, int px)
    {
        int size = (int)n.size;
        var img = new byte[px * px];
        bool many = size / px >= SAMPLE_AT;
        Span<int> votes = stackalloc int[3];
        for (int j = 0; j < px; j++)
        {
            var (y0, y1) = Span(j, px, size);
            int y = (y0 + y1) / 2;
            for (int i = 0; i < px; i++)
            {
                var (x0, x1) = Span(i, px, size);
                int x = (x0 + x1) / 2;
                byte k;
                if (!many) k = NatureAt(n, x, y);
                else
                {
                    votes.Clear();
                    for (int b = 0; b < 3; b++)
                        for (int a = 0; a < 3; a++) votes[NatureAt(n, x0 + (2 * a + 1) * (x1 - x0) / 6, y0 + (2 * b + 1) * (y1 - y0) / 6)]++;
                    // The most votes; a tie goes to water, then forest (what a coarse picture would lose).
                    k = votes[WATER] >= votes[FOREST] && votes[WATER] >= votes[LAND] ? WATER : votes[FOREST] >= votes[LAND] ? FOREST : LAND;
                    // A river narrower than a pixel still shows (a map's licence): within half a pixel of its middle.
                    if (k != WATER && !NatureMod.nearStart(n, x, y, NatureMod.START_CLEAR_RADIUS + 8) && Terrain.Of(n).riverDistance(x, y) < Math.Min((x1 - x0) / 2.0, Terrain.FAR - 1)) k = WATER;
                }
                if (n.map == "quarantine" && OnFence(size, x0, x1, y0, y1)) k = FENCE;
                img[j * px + i] = k;
            }
        }
        return img;
    }

    /// <summary>Whether a pixel's tiles cross the quarantine fence (NatureMod.onFenceLine: two in from the edge).</summary>
    static bool OnFence(int s, int x0, int x1, int y0, int y1)
    {
        bool Holds(int lo, int hi, int v) => lo <= v && v < hi;
        bool inX = x1 > 2 && x0 <= s - 3, inY = y1 > 2 && y0 <= s - 3;
        return (inY && (Holds(x0, x1, 2) || Holds(x0, x1, s - 3))) || (inX && (Holds(y0, y1, 2) || Holds(y0, y1, s - 3)));
    }

    /// <summary>Stage 2: towns as discs (on land: water stays water), their circles before their streets are known.</summary>
    public static void towns(byte[] img, int px, double size, IEnumerable<Town> towns)
    {
        double scale = px / size;
        foreach (var t in towns)
        {
            double cx = (t.x + 0.5) * scale, cy = (t.y + 0.5) * scale, r = Math.Max(1, t.radius * scale);
            for (int j = Math.Max(0, (int)(cy - r)); j <= Math.Min(px - 1, (int)(cy + r)); j++)
                for (int i = Math.Max(0, (int)(cx - r)); i <= Math.Min(px - 1, (int)(cx + r)); i++)
                {
                    double dx = i + 0.5 - cx, dy = j + 0.5 - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                    ref byte k = ref img[j * px + i];
                    if (k == LAND || k == FOREST) k = TOWN;
                }
        }
    }

    /// <summary>Stage 3: one region's plan over the picture: lots, then roads, then streets (each at least a pixel).</summary>
    public static void plan(byte[] img, int px, double size, WorldPlan p)
    {
        double scale = px / size;
        void Rect(double x, double y, double w, double h, byte k)
        {
            int i0 = (int)(x * scale), j0 = (int)(y * scale);
            int i1 = Math.Max(i0, (int)Math.Ceiling((x + w) * scale) - 1), j1 = Math.Max(j0, (int)Math.Ceiling((y + h) * scale) - 1);
            for (int j = Math.Max(0, j0); j <= Math.Min(px - 1, j1); j++)
                for (int i = Math.Max(0, i0); i <= Math.Min(px - 1, i1); i++) img[j * px + i] = k;
        }
        foreach (var l in p.lots) Rect(l.x, l.y, l.w, l.h, l.zone is "field" or "farm" ? FIELD : BUILT);
        foreach (var line in p.roads)
        {
            for (int s = 1; s < line.Count; s++)
            {
                PointD a = line[s - 1], b = line[s];
                double steps = Math.Max(1, Math.Ceiling(Math.Max(Math.Abs(b.x - a.x), Math.Abs(b.y - a.y)) * scale * 2));
                for (int q = 0; q <= steps; q++)
                {
                    int i = (int)((a.x + (b.x - a.x) * q / steps) * scale), j = (int)((a.y + (b.y - a.y) * q / steps) * scale);
                    if (i >= 0 && j >= 0 && i < px && j < px) img[j * px + i] = ROAD;
                }
            }
        }
        foreach (var s in p.streets)
        {
            int w = s.x1 - s.x0 + (s.eastWest ? 1 : Towns.STREET), h = s.y1 - s.y0 + (s.eastWest ? Towns.STREET : 1);
            Rect(s.x0, s.y0, w, h, STREET);
        }
    }

    /// <summary>The regions in the order the picture fills them: nearest the start's first (by centre, then index).</summary>
    public static List<(int rx, int ry)> regionOrder(WorldRegions r)
    {
        var s = r.nature.start;
        var all = new List<(int rx, int ry, double d)>();
        for (int ry = 0; ry < r.side; ry++)
            for (int rx = 0; rx < r.side; rx++)
            {
                double cx = (rx + 0.5) * WorldRegions.R, cy = (ry + 0.5) * WorldRegions.R;
                all.Add((rx, ry, (cx - s.x) * (cx - s.x) + (cy - s.y) * (cy - s.y)));
            }
        return all.OrderBy((a) => a.d).ThenBy((a) => a.ry * r.side + a.rx).Select((a) => (a.rx, a.ry)).ToList();
    }

    /// <summary>Whether the picture plans regions in full (stage 3): on worlds of up to 2 x 2 regions.</summary>
    public static bool plansRegions(WorldRegions r) => r.side <= 2;

    /// <summary>Regions' towns sited between pictures shown (a 10,000 world has a hundred regions: ~0.1 s each).</summary>
    const int TOWNS_PER_SHOW = 6;

    /// <summary>The whole picture, stage by stage: `shown` gets a copy after each (nature, then every region's towns or
    /// plan), and `stop` is asked between them (a newer picture is wanted). Returns the last, or null if stopped.</summary>
    public static byte[] build(WorldRegions r, Action<byte[], string> shown = null, Func<bool> stop = null)
    {
        int px = sideFor(r.size);
        var img = nature(r.nature, px);
        shown?.Invoke((byte[])img.Clone(), "the land");
        var order = regionOrder(r);
        // Towns everywhere first (cheap next to plans), then the plans (a second or so each), so the map has its towns early.
        for (int k = 0; k < order.Count; k++)
        {
            if (stop?.Invoke() == true) return null;
            towns(img, px, r.size, r.TownsOf(order[k].rx, order[k].ry));
            bool last = k == order.Count - 1;
            if (last || (k + 1) % TOWNS_PER_SHOW == 0) shown?.Invoke((byte[])img.Clone(), last ? (plansRegions(r) ? "towns" : "done") : $"towns ({k + 1} of {order.Count} regions)");
        }
        if (!plansRegions(r)) return img;
        int planned = 0, toPlan = order.Count;
        foreach (var (rx, ry) in order)
        {
            if (stop?.Invoke() == true) return null;
            plan(img, px, r.size, r.Region(rx, ry));
            planned++;
            shown?.Invoke((byte[])img.Clone(), planned < toPlan ? $"roads and lots ({planned} of {toPlan})" : "done");
        }
        return img;
    }

    /// <summary>Which pixels of the picture hold a tile someone has seen (1) or not (0).</summary>
    public static byte[] explored(SimState state)
    {
        var g = state.grid;
        int size = g.width, px = sideFor(size);
        var seen = new byte[px * px];
        for (int y = 0; y < g.height; y++)
        {
            int j = (int)((long)y * px / size), row = y * g.width;
            for (int x = 0; x < g.width; x++)
                if (g.explored[row + x] != 0) seen[j * px + (int)((long)x * px / size)] = 1;
        }
        return seen;
    }

    /// <summary>What a save's picture needs (null for a hand-made map: it has no world to draw).</summary>
    public static SavePreview ofSave(SimState state)
    {
        if (state.world == null) return null;
        var seen = explored(state);
        // Runs of 0s and 1s, alternating, starting with 0s.
        var runs = new List<int>();
        byte at = 0;
        int n = 0;
        foreach (var b in seen)
        {
            if (b == at) { n++; continue; }
            runs.Add(n);
            at = b;
            n = 1;
        }
        runs.Add(n);
        var you = state.control?.survivorId is { } id ? state.survivors.Find((s) => s.id == id) : state.survivors.FirstOrDefault();
        return new SavePreview
        {
            seed = state.world.seed, size = state.world.size, map = state.settings.map, px = sideFor(state.world.size), explored = runs,
            you = you == null ? null : new Point { x = you.pos.x, y = you.pos.y },
        };
    }

    /// <summary>A save preview's explored pixels, unpacked.</summary>
    public static byte[] exploredOf(SavePreview p)
    {
        var seen = new byte[p.px * p.px];
        int i = 0;
        byte v = 0;
        foreach (var run in p.explored)
        {
            for (int k = 0; k < run && i < seen.Length; k++) seen[i++] = v;
            v ^= 1;
        }
        return seen;
    }
}

/// <summary>What the Load screen draws of a save (WorldPreview.ofSave), kept beside the state in the file.</summary>
public sealed class SavePreview
{
    public double seed { get; set; }
    public double size { get; set; }
    public string map { get; set; } = "mainland";
    public int px { get; set; }
    /// <summary>Which pixels are explored: runs of 0s and 1s by turns, 0s first.</summary>
    public List<int> explored { get; set; } = new();
    public Point? you { get; set; }
}
