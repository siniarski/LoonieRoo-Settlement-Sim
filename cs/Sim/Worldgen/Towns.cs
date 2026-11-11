// Ported from src/sim/towns.ts (TypeScript, retired 2026-11-08: git tag ts-final). Hand-maintained C# from here.
// Towns: where they stand and the streets they grow. Laid out over the natural world
// (NatureMod.cs) once per world, as plain data (Worldplan.cs caches it).
//
//   siting   towns go where the land suits them: dry, open ground (little forest), near water
//            (a river or lake just outside the town), away from the map's edge, the start
//            clearing and each other (farmland between). Biggest first; villages fill in. Only on
//            settled land (Terrain.settlement over wild()), the more settled the better: towns gather
//            where people did, and wild country lies between (2026-11-10).
//   outline  a town's streets reach out as far as a lumpy outline (noise round its circle, REACH),
//            not a circle: it straggles out one way and stops short another.
//   streets  each town grows from its centre: a main street one way and a cross street the
//            other, then side streets branching off them at uneven spacing (wide enough for a
//            lot each side and room between), each as long as the town's circle allows. Streets
//            stop short of water and of the start clearing. All are axis-aligned.
//   widths   (2026-11-11, the player's "two lanes, each fits a car") a street's asphalt is STREET tiles across from
//            its line (x0..x1, y0..y1: its low edge): a parking strip (PARKING) along the low side, then two lanes of
//            3 m (CARRIAGEWAY); a paved sidewalk (SIDEWALK) each side, which the lots face. Country roads leave a
//            main street from the middle of its carriageway (laneCentre).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Towns
{

    /// <summary>A street's asphalt, across (tiles): the parking strip and the carriageway.</summary>
    public const int STREET = PARKING + CARRIAGEWAY;

    /// <summary>The strip along a street's low side (north of an east-west street, west of a north-south one) where
    /// cars are parked at the kerb (Lots.parkCar), out of the lanes.</summary>
    public const int PARKING = 2;

    /// <summary>Two lanes of 3 m: each takes a pickup (1.9 m) with room to spare.</summary>
    public const int CARRIAGEWAY = 6;

    /// <summary>The paved sidewalk each side of a street (the plan's road mask 3; Heights sinks the asphalt a kerb's
    /// height under it).</summary>
    public const int SIDEWALK = 2;

    /// <summary>The middle of a street's carriageway, across from its line (tiles): where a country road's centre
    /// line meets a main street's end, so its lanes run on into the street's.</summary>
    public const int LANE_CENTRE = PARKING + CARRIAGEWAY / 2;

    /// <summary>A street shorter than this isn't laid.</summary>
    public const int STREET_MIN = 12;

    /// <summary>A street stops before it would run this close (clear tiles) beside another: room for the sidewalks and
    /// something between.</summary>
    public const int STREET_APART = 12;

    /// <summary>How much a site's score gains for settled land (x settlement - 0.5).</summary>
    public const double SETTLED_PULL = 0.6;

    /// <summary>How settled the land at a site is (Terrain.settlement).</summary>
    public static double settled(Nature n, double x, double y) => Terrain.Of(n).settlement(x, y);

    /// <summary>Land settled less than this has no towns (Terrain.wildAt: the world's least settled quarter).</summary>
    public static double wild(Nature n) => Terrain.Of(n).wildAt;

    /// <summary>How far a town's streets reach at (x, y), as a share of its radius: 0.75 to 1.3 by noise at about half
    /// its radius, so its edge wanders.</summary>
    public static double reach(Nature n, Town t, double x, double y) => 0.75 + 0.55 * NatureMod.noiseD(n.seed, x, y, Math.Max(8, t.radius * 0.6), 0x7210 + t.index);

    /// <summary>Side streets branch this far apart, at least (a lot's depth each side and a verge).</summary>
    public const int STREET_GAP = 34;

    public static readonly JsRecord<List<double>> RADII = new() { ["village"] = [26, 36], ["town"] = [44, 60], ["city"] = [68, 86] };

    /// <summary>How many towns, and how big, for a map this size.</summary>
    public static List<string> townKinds(double seed, double size)
    {
        var count = (size <= 128) ? 1 : (JsMath.max(1, JsMath.round(((double)(((double)(size) * (size))) / (((double)(170) * (170)))))) + ((NatureMod.roll(seed, size, 0, 0x7100) < 0.5) ? 1 : 0));
        List<string> kinds = [];
        for (var i = 0; i < count; i++)
        {
            var r = NatureMod.roll(seed, i, 1, 0x7101);
            kinds.push(((size >= 384) && (r < 0.2)) ? "city" : ((r < 0.6) ? "town" : "village"));
        }
        JsRecord<double> order = new() { ["city"] = 0, ["town"] = 1, ["village"] = 2 };
        return kinds.sort((a, b) => Js.Get(order, a) - Js.Get(order, b));
    }

    /// <summary>A coarse map of the land (every 4 tiles): 0 open, 1 forest, 2 water.</summary>
    public static Uint8Array landGrid(Nature n) => landGrid(n, Win.Whole(n.size));

    /// <summary>The same over a window's cells (row by row from its first cell; 4-tile cells, as the road cells).</summary>
    public static Uint8Array landGrid(Nature n, Win w)
    {
        double N = w.KW, H = w.KH;
        var g = new Uint8Array(((double)(N) * (H)));
        for (var j = 0; j < H; j++)
        {
            for (var i = 0; i < N; i++)
            {
                var x = ((double)(i + w.Kx0) * (4)) + 2;
                var y = ((double)(j + w.Ky0) * (4)) + 2;
                g[Js.Idx(((double)(j) * (N)) + i)] = NatureMod.isWater(n, x, y) ? 2 : (NatureMod.isForest(n, x, y) ? 1 : 0);
            }
        }
        return g;
    }

    /// <summary>Kept this far (tiles, beyond its radius) from a border shared with another region: two neighbours' towns
    /// then stand at least r1 + r2 + 48 apart (the rule within a region) without either knowing the other's.</summary>
    public const int REGION_MARGIN = 27;

    /// <summary>The kinds of town a region holds (biggest first): one a 170 x 170 tiles of its area, and a roll for one
    /// more; over the whole map, townKinds. Each region rolls its own, its towns' indexes from index x 1000.</summary>
    public static List<string> townKindsFor(double seed, double size, Win w)
    {
        if (w.index == 0 && w.W == (int)size && w.H == (int)size) return townKinds(seed, size);
        var count = JsMath.round(((double)w.W * w.H) / (170.0 * 170)) + ((NatureMod.roll(seed, size, w.index, 0x7100) < 0.5) ? 1 : 0);
        List<string> kinds = [];
        for (var i = 0; i < count; i++)
        {
            var r = NatureMod.roll(seed, w.index * 1000 + i, 1, 0x7101);
            kinds.push(((size >= 384) && (r < 0.2)) ? "city" : ((r < 0.6) ? "town" : "village"));
        }
        JsRecord<double> order = new() { ["city"] = 0, ["town"] = 1, ["village"] = 2 };
        return kinds.sort((a, b) => Js.Get(order, a) - Js.Get(order, b));
    }

    /// <summary>Where the towns go (biggest first): the best-scoring site for each on an 8-tile grid, dry and open,
    /// near water, clear of the start and of the towns already placed.
    ///
    /// Cheap a site (2026-11-09, docs/worldgen-speed.md; the loop is still towns x sites, which is why plans are
    /// made by region: docs/worldplan-regions.md): it was every town x every site x a disc of the
    /// land grid (14 s at 2048, hours at 10,000). Now each site's counts (inside the town's circle: cells, dry, wooded;
    /// in the ring round it: cells, wet) come from row prefix sums over the land grid (a disc is a stack of row spans),
    /// and the towns placed so far are found through a coarse grid of cells. The counts are the same integers and the
    /// sites are scored in the same order with the same formula, so the towns are the same (TownsSpeedTest compares it
    /// with the original).</summary>
    public static List<Town> siteTowns(Nature n, Uint8Array land) => siteTowns(n, land, Win.Whole(n.size));

    /// <summary>The same inside a window (its land grid from landGrid with it): sites only inside it, REGION_MARGIN
    /// further from a border shared with another region; over the whole map, the above.</summary>
    public static List<Town> siteTowns(Nature n, Uint8Array land, Win w)
    {
        int size = (int)n.size;
        // N: the window's width in cells (the map's, over the whole map); H its height; KX0, KY0 its first cell.
        int N = w.KW, H = w.KH, KX0 = w.Kx0, KY0 = w.Ky0;
        int baseIndex = w.index * 1000;
        List<Town> towns = [];
        var kinds = townKindsFor(n.seed, n.size, w);
        // Prefix sums per row of the land grid: dry (not water), wooded (forest), wet (water).
        var dryP = new int[H * (N + 1)];
        var woodP = new int[H * (N + 1)];
        var wetP = new int[H * (N + 1)];
        for (int j = 0; j < H; j++)
            for (int k = 0; k < N; k++)
            {
                int c = land.a[j * N + k];
                int at = j * (N + 1) + k;
                dryP[at + 1] = dryP[at] + (c != 2 ? 1 : 0);
                woodP[at + 1] = woodP[at] + (c == 1 ? 1 : 0);
                wetP[at + 1] = wetP[at] + (c == 2 ? 1 : 0);
            }
        // Rows and columns in map cells (as the original counted them); the sums are kept from the window's first cell.
        int Sum(int[] p, int j, int k0, int k1) => k1 < k0 ? 0 : p[(j - KY0) * (N + 1) + (k1 - KX0) + 1] - p[(j - KY0) * (N + 1) + (k0 - KX0)];
        // Placed towns by 64-tile cell: the exclusion reaches the radii of both towns plus 48.
        const int CELL = 64;
        int cells = size / CELL + 1;
        var placed = new List<Town>?[cells * cells];
        double biggest = 0;
        for (int i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            var (lo, hi) = Js.Get(RADII, kind);
            var radius = JsMath.min(JsMath.floor(((double)(n.size) * (0.19))), lo + JsMath.floor(((double)(NatureMod.roll(n.seed, baseIndex + i, 2, 0x7102)) * (((hi - lo) + 1)))));
            int r = (int)radius, reach = r + 14, margin = r + 3;
            int mL = w.edgeL ? margin : r + REGION_MARGIN, mT = w.edgeT ? margin : r + REGION_MARGIN;
            int mR = w.edgeR ? margin : r + REGION_MARGIN, mB = w.edgeB ? margin : r + REGION_MARGIN;
            long r2 = (long)r * r, reach2 = (long)reach * reach;
            int spanCells = (int)Math.Ceiling((radius + biggest + 48) / CELL) + 1;
            SiteTownsBest? best = null;
            for (var y = w.y0 + mT; y < (w.y1 - mB); y += 8)
            {
                for (var x = w.x0 + mL; x < (w.x1 - mR); x += 8)
                {
                    if (JsMath.sqrt((JsMath.pow((x - n.start.x), 2)) + (JsMath.pow((y - n.start.y), 2))) < ((radius + NatureMod.START_CLEAR_RADIUS) + 6))
                    {
                        continue;
                    }
                    if (Crowded(placed, cells, CELL, spanCells, x, y, radius))
                    {
                        continue;
                    }
                    // The rows of the box the original scanned, each as spans: inside the circle, and the ring out to the reach.
                    int inside = 0, dry = 0, wooded = 0, around = 0, wet = 0;
                    int j0 = Math.Max(KY0, (int)Math.Floor((y - reach) / 4.0)), j1 = Math.Min(KY0 + H - 1, (int)Math.Floor((y + reach) / 4.0));
                    int kb0 = Math.Max(KX0, (int)Math.Floor((x - reach) / 4.0)), kb1 = Math.Min(KX0 + N - 1, (int)Math.Floor((x + reach) / 4.0));
                    for (int j = j0; j <= j1; j++)
                    {
                        long dy = 4L * j + 2 - y, dy2 = dy * dy;
                        if (dy2 > reach2) continue;
                        var (ro0, ro1) = Span(x, ISqrt(reach2 - dy2), kb0, kb1);
                        int roCells = Math.Max(0, ro1 - ro0 + 1), roWet = Sum(wetP, j, ro0, ro1);
                        if (dy2 <= r2)
                        {
                            var (in0, in1) = Span(x, ISqrt(r2 - dy2), kb0, kb1);
                            int inCells = Math.Max(0, in1 - in0 + 1);
                            inside += inCells;
                            dry += Sum(dryP, j, in0, in1);
                            wooded += Sum(woodP, j, in0, in1);
                            around += roCells - inCells;
                            wet += roWet - Sum(wetP, j, in0, in1);
                        }
                        else
                        {
                            around += roCells;
                            wet += roWet;
                        }
                    }
                    var dryShare = ((double)(dry) / (JsMath.max(1, inside)));
                    if (dryShare < 0.9)
                    {
                        continue;
                    }
                    var wetShare = ((double)(wet) / (JsMath.max(1, around)));
                    var settled = Towns.settled(n, x, y);
                    if (settled < Towns.wild(n))
                    {
                        continue;
                    }
                    var score = ((dryShare - ((double)(((double)(wooded) / (JsMath.max(1, inside)))) * (0.6))) + (((wetShare > 0.02) && (wetShare < 0.3)) ? 0.3 : 0)) + ((double)(NatureMod.roll(n.seed, x, y, 0x7103)) * (0.15)) + Towns.SETTLED_PULL * (settled - 0.5);
                    if ((!Js.T(best) || (score > best.score)) && !NatureMod.isWater(n, x, y))   // (a town's own centre is dry: a pond there left larger towns' streets nowhere to cross; tested last, it is dear)
                    {
                        best = new SiteTownsBest { x = Js.I(x), y = Js.I(y), score = score };
                    }
                }
            }
            if (Js.T(best))
            {
                var town = new Town { x = Js.I(best.x), y = Js.I(best.y), radius = radius, kind = kind, eastWest = (NatureMod.hash(n.seed, best.x, best.y, 0x7104) % 2) == 0, index = baseIndex + towns.Count };
                towns.push(town);
                int at = Math.Min(cells - 1, town.y / CELL) * cells + Math.Min(cells - 1, town.x / CELL);
                (placed[at] ??= new()).Add(town);
                biggest = Math.Max(biggest, radius);
            }
        }
        return towns;
    }

    /// <summary>Whether a placed town is too near (x, y) for a town of this radius: the original's test, over the cells round it.</summary>
    static bool Crowded(List<Town>?[] placed, int cells, int cell, int span, int x, int y, double radius)
    {
        int cx = Math.Min(cells - 1, x / cell), cy = Math.Min(cells - 1, y / cell);
        for (int gy = Math.Max(0, cy - span); gy <= Math.Min(cells - 1, cy + span); gy++)
            for (int gx = Math.Max(0, cx - span); gx <= Math.Min(cells - 1, cx + span); gx++)
                if (placed[gy * cells + gx] is { } list)
                    foreach (var t in list)
                        if (JsMath.sqrt((JsMath.pow((x - t.x), 2)) + (JsMath.pow((y - t.y), 2))) < ((radius + t.radius) + 48)) return true;
        return false;
    }

    /// <summary>The cells k (centres 4k + 2) with |4k + 2 - x| at most a, clipped to [k0, k1] (empty: hi under lo).</summary>
    static (int lo, int hi) Span(int x, long a, int k0, int k1)
    {
        int lo = (int)Math.Ceiling((x - 2 - a) / 4.0), hi = (int)Math.Floor((x - 2 + a) / 4.0);
        return (Math.Max(k0, lo), Math.Min(k1, hi));
    }

    /// <summary>The largest integer whose square is at most v (v at least 0).</summary>
    static long ISqrt(long v)
    {
        long s = (long)Math.Sqrt(v);
        while (s * s > v) s--;
        while ((s + 1) * (s + 1) <= v) s++;
        return s;
    }

    /// <summary>Where a town's streets cross: the corner of its centre square (STREET by STREET: the main street's and
    /// the cross street's shared asphalt). The town's own point if the square and its sidewalks are clear there (dry,
    /// on the map, off the start's clearing), else the nearest clear spot, a few tiles at a time out to ANCHOR_REACH
    /// (a pond at the very centre no longer leaves a town in pieces: streets are twelve tiles across with their
    /// sidewalks, where they were two). Null if there is none: a town without streets.</summary>
    public static Point? anchor(Nature n, Town t)
    {
        bool clear(double x, double y)
        {
            for (var o = 0; o < STREET; o++)
            {
                if (blocked(n, x + o, y, 0, 1) || blocked(n, x, y + o, 1, 0))
                {
                    return false;
                }
            }
            return true;
        }
        for (var ring = 0; ring <= ANCHOR_REACH; ring += 3)
        {
            for (var dy = -ring; dy <= ring; dy += 3)
            {
                for (var dx = -ring; dx <= ring; dx += 3)
                {
                    if ((JsMath.max(JsMath.abs(dx), JsMath.abs(dy)) == ring) && clear(t.x + dx, t.y + dy))
                    {
                        return new Point { x = Js.I(t.x + dx), y = Js.I(t.y + dy) };
                    }
                }
            }
        }
        return null;
    }

    /// <summary>How far from a town's point its streets' crossing may be moved (tiles).</summary>
    public const int ANCHOR_REACH = 15;

    // Whether a street's cross-section at (x, y) (its line; the asphalt and sidewalks lie across, along (px, py))
    // is off the map, in water or in the start's clearing: every second tile across, and both outer edges.
    static bool blocked(Nature n, double x, double y, double px, double py)
    {
        int lo = 3 + SIDEWALK, hi = (int)n.size - 4 - STREET - SIDEWALK;
        if ((((x < lo) || (y < lo)) || (x >= hi)) || (y >= hi))
        {
            return true;
        }
        for (var o = -SIDEWALK; o < STREET + SIDEWALK; o += (o + 2 < STREET + SIDEWALK) ? 2 : 1)
        {
            var ox = x + ((double)(px) * (o));
            var oy = y + ((double)(py) * (o));
            if (NatureMod.isWater(n, ox, oy) || NatureMod.nearStart(n, ox, oy, NatureMod.START_CLEAR_RADIUS + 3))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Streets for a town: main and cross streets through its centre square (anchor), then side streets off them.</summary>
    public static List<Street> growStreets(Nature n, Town t, double ti)
    {
        List<Street> streets = [];
        var c = anchor(n, t);
        if (c == null)
        {
            return streets;
        }
        double r(double k)
        {
            return NatureMod.roll(n.seed, t.x, t.y, 0x7200 + k);
        }
        bool blocked(double x, double y, double px, double py) => Towns.blocked(n, x, y, px, py);
        var laid = new JsSet<double>();
        bool isStreet(double x, double y)
        {
            return laid.has(((double)(y) * (n.size)) + x);
        }
        double run(double x, double y, double dx, double dy, double len, bool main, double least = STREET_MIN)
        {
            var (px, py) = (JsMath.abs(dy), JsMath.abs(dx));
            // A street's leading edge at (sx, sy): any of its tiles across already a street.
            bool meets(double sx, double sy)
            {
                for (var o = 0; o < STREET; o++)
                {
                    if (isStreet(sx + ((double)(px) * (o)), sy + ((double)(py) * (o))))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (blocked(x, y, px, py) || meets(x, y))
            {
                return 0;
            }
            bool crowded(double sx, double sy)
            {
                for (var o = 1; o <= STREET_APART; o++)
                {
                    if (isStreet(sx + ((double)(px) * ((STREET - 1) + o)), sy + ((double)(py) * ((STREET - 1) + o))) || isStreet(sx - ((double)(px) * (o)), sy - ((double)(py) * (o))))
                    {
                        return true;
                    }
                }
                return false;
            }
            double steps = 0;
            while (steps < len)
            {
                var nx = x + ((double)(dx) * ((steps + 1)));
                var ny = y + ((double)(dy) * ((steps + 1)));
                if (blocked(nx, ny, px, py) || (JsMath.sqrt((JsMath.pow((nx - t.x), 2)) + (JsMath.pow((ny - t.y), 2))) > ((double)(t.radius) * reach(n, t, nx, ny))))
                {
                    break;
                }
                if (meets(nx, ny))
                {
                    steps++;
                    break;
                }
                if ((steps >= 2) && crowded(nx, ny))
                {
                    break;
                }
                steps++;
            }
            if (steps < least)
            {
                return 0;
            }
            var x1 = x + ((double)(dx) * (steps));
            var y1 = y + ((double)(dy) * (steps));
            Street s = new Street { x0 = Js.I(JsMath.min(x, x1)), y0 = Js.I(JsMath.min(y, y1)), x1 = Js.I(JsMath.max(x, x1)), y1 = Js.I(JsMath.max(y, y1)), eastWest = dy == 0, main = main, town = ti };
            streets.push(s);
            for (var yy = s.y0; yy <= (s.y1 + (s.eastWest ? (STREET - 1) : 0)); yy++)
            {
                for (var xx = s.x0; xx <= (s.x1 + (s.eastWest ? 0 : (STREET - 1))); xx++)
                {
                    laid.add(((double)(yy) * (n.size)) + xx);
                }
            }
            return steps;
        }
        var levels = (t.kind == "village") ? 1 : 2;
        var odds = (t.kind == "village") ? 0.55 : 0.75;
        var k = 10;
        void grow(double x, double y, double dx, double dy, double len, double level, double least = STREET_MIN)
        {
            var steps = run(x, y, dx, dy, len, level == 0, least);
            if ((steps == 0) || (level >= levels))
            {
                return;
            }
            var (px, py) = (JsMath.abs(dy), JsMath.abs(dx));
            double off = (STREET_GAP - 8) + JsMath.floor(((double)(r(k++)) * (7)));
            while (off <= (steps - STREET))
            {
                var bx = x + ((double)(dx) * (off));
                var by = y + ((double)(dy) * (off));
                foreach (var side in Js.Iter((List<double>)[1, -1]))
                {
                    if (r(k++) > odds)
                    {
                        continue;
                    }
                    var gap = (side == 1) ? STREET : -1;
                    grow(bx + ((double)(px) * (gap)), by + ((double)(py) * (gap)), ((double)(px) * (side)), ((double)(py) * (side)), JsMath.floor(((double)(t.radius) * ((0.5 + ((double)(r(k++)) * (0.6)))))), level + 1);
                }
                off += STREET_GAP + JsMath.floor(((double)(r(k++)) * (6)));
            }
        }
        var __c0 = t.eastWest; var ax = __c0 ? 1 : 0; var ay = __c0 ? 0 : 1;
        var __c1 = t.eastWest; var cx = __c1 ? 0 : 1; var cy = __c1 ? 1 : 0;
        // The main street's two halves: one on from the centre square, the other back from the square's far edge and
        // through it, laid if it gets no further than the square itself (the anchor says the square is clear): so the
        // square is always there for the other three arms to meet.
        grow(c.x + ((double)(ax) * (STREET)), c.y + ((double)(ay) * (STREET)), ax, ay, JsMath.floor(((double)(t.radius) * ((0.9 + ((double)(r(1)) * (0.2)))))), 0);
        grow(c.x + ((double)(ax) * ((STREET - 1))), c.y + ((double)(ay) * ((STREET - 1))), -ax, -ay, JsMath.floor(((double)(t.radius) * ((0.9 + ((double)(r(2)) * (0.2)))))) + (STREET - 1), 0, STREET - 1);
        grow(c.x + ((double)(cx) * (STREET)), c.y + ((double)(cy) * (STREET)), cx, cy, JsMath.floor(((double)(t.radius) * ((0.6 + ((double)(r(3)) * (0.4)))))), 0);
        grow(c.x - cx, c.y - cy, -cx, -cy, JsMath.floor(((double)(t.radius) * ((0.6 + ((double)(r(4)) * (0.4)))))), 0);
        return streets;
    }
}
