// The ground's height (2026-11-08): a deterministic height field over a generated world, in metres,
// derived from the seed and the world plan (Worldplan) like the plan itself: cached, never saved, so
// no schema change. Read-only for now: nothing in the tick reads it, so no sim outcome depends on it
// (movement, sight and water don't feel slopes yet: that needs the player's OK, after the big stress
// test). The 3D view stands everything on it; later it can feed line of sight, movement cost, water.
//
// Heights live on the tile corners (integer points; tile (x, y) spans x..x+1, y..y+1): `corner(cx, cy)`.
// Between corners the ground is bilinear (`at(x, y)`), so a mesh built on the corners matches it exactly.
// How a corner's height is made, in order:
//   1. relief(x, y): large-scale hills and valleys. 0 for now: they come later and add in here (and
//      the water level follows them, so rivers stay in their valleys).
//   2. rolling ground: three octaves of value noise (NatureMod.noise, a stateless hash): ~2.4 m over
//      ~56 m, ~0.6 m over ~21 m, ~0.2 m over ~8 m. Gentle: a few per cent, rarely 10.
//   3. towns: the rolling is damped (the big octave halved, the small ones gone) within a town's radius,
//      eased out over TOWN_EASE. Roads: the small octaves fade out near a road (ROAD_EASE), so roads
//      follow the big swell smoothly with a gentle shoulder. The start: flattened to one level round
//      the start shelter (START_FLAT, eased out).
//   4. water: the water level is the big octave (as damped) less WATER_DROP, so it is always under
//      the land beside it; land eases down to it over BANK tiles; shore corners sit just under the
//      water's surface and the bed deepens away from the shore. A road over water (a bridge) is a deck
//      DECK over the surface, its sides running down into the water.
//   5. buildings (each lot's placements, Lots.layoutLot): each sits on one level (the mean of steps
//      1-4 at its footprint's corners and centre), flat over its whole footprint rectangle, and the
//      ground eases to it over BUILDING_EASE round it (inverse-distance blended between neighbours,
//      so it is continuous and exact on every footprint).
// Bounded (rule 14): corners are computed a plan chunk (32 x 32 tiles) at a time, once, and cached
// (at most MAX_CHUNKS a world, oldest dropped). Water and road distances are a chamfer transform over
// the chunk and a margin, capped below the margin, so neighbouring chunks agree on shared corners.
// Pure: every number is a function of (seed, size, map, x, y), whatever order chunks are asked for in.
// Hand-made maps (no world plan) are flat: 0 everywhere.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Heights
{
    public const int CHUNK = Worldplan.PLAN_CHUNK;
    /// <summary>Rolling ground: amplitude (m, peak to peak at most) and scale (tiles) of each octave.</summary>
    public const double A1 = 2.4, S1 = 56, A2 = 0.6, S2 = 21, A3 = 0.2, S3 = 8;
    const int SALT1 = 0x4E01, SALT2 = 0x4E02, SALT3 = 0x4E03;
    public const double TOWN_EASE = 20;
    public const double ROAD_EASE = 4;
    public const double START_FLAT = NatureMod.START_CLEAR_RADIUS + 2, START_EASE = 12;
    /// <summary>The water's surface under the big octave (m): more than the small octaves can dip, so land is always above it.</summary>
    public const double WATER_DROP = 0.55;
    /// <summary>Land eases down to the water over this many tiles; it ends this far over the surface.</summary>
    public const double BANK = 8, BANK_TOP = 0.15;
    /// <summary>A shore corner (touching water and land) is this far under the surface; the bed goes down from it.</summary>
    public const double SHORE = 0.05, BED = 0.35, BED_STEP = 0.3, BED_MAX = 3;
    /// <summary>A road over water (a bridge: Worldgen's "bridge" ground) is a deck this far over the surface.</summary>
    public const double DECK = 0.35;
    public const double BUILDING_EASE = 4;
    /// <summary>Tiles round a chunk read for the distance transforms: more than BANK, so chunks agree.</summary>
    const int MARGIN = 10;
    public const int MAX_CHUNKS = 1024;

    /// <summary>A world's height field: its corners by plan chunk, filled in when first asked for.</summary>
    public sealed class Field
    {
        public double seed;
        public int size;
        public string map;
        public WorldPlan plan;
        /// <summary>Steps 1-4 (no buildings), and the final corners, per chunk: (CHUNK + 1)^2, row by row.</summary>
        internal readonly Dictionary<int, double[]> bare = new(), done = new();
        internal readonly List<int> bareOrder = new(), doneOrder = new();
        /// <summary>Each lot's buildings: footprint rectangle (x, y, w, h) and level.</summary>
        internal readonly Dictionary<int, List<double[]>> lotLevels = new();
        /// <summary>The plan's towns by cell, and the reach townness asks within (TownIndex).</summary>
        internal TownIndex townIndex;
        internal double townReach;
        internal readonly List<int> townScratch = new();

        /// <summary>The ground's height (m) at tile corner (cx, cy), clamped to the map.</summary>
        public double corner(int cx, int cy)
        {
            cx = cx < 0 ? 0 : cx > size ? size : cx;
            cy = cy < 0 ? 0 : cy > size ? size : cy;
            int kx = cx / CHUNK, ky = cy / CHUNK;
            // The last row and column of corners belong to the chunk before.
            if (kx * CHUNK == cx && kx > 0 && kx * CHUNK >= size) kx--;
            if (ky * CHUNK == cy && ky > 0 && ky * CHUNK >= size) ky--;
            var c = chunk(kx, ky);
            return c[(cy - ky * CHUNK) * (CHUNK + 1) + (cx - kx * CHUNK)];
        }

        /// <summary>The ground's height (m) at a point (tiles), bilinear between the corners.</summary>
        public double at(double x, double y)
        {
            double fx0 = JsMath.floor(x), fy0 = JsMath.floor(y);
            int x0 = (int)fx0, y0 = (int)fy0;
            double fx = x - fx0, fy = y - fy0;
            double a = corner(x0, y0), b = corner(x0 + 1, y0), c = corner(x0, y0 + 1), d = corner(x0 + 1, y0 + 1);
            double top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>The water's surface (m) at a point: where the water stands, wherever there is water.</summary>
        public double water(double x, double y)
        {
            double fx0 = JsMath.floor(x), fy0 = JsMath.floor(y);
            int x0 = (int)fx0, y0 = (int)fy0;
            double fx = x - fx0, fy = y - fy0;
            double a = waterAt(this, x0, y0), b = waterAt(this, x0 + 1, y0), c = waterAt(this, x0, y0 + 1), d = waterAt(this, x0 + 1, y0 + 1);
            double top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>A cheap approximation of a corner's height (m) for drawing far away (the renderer's far field):
        /// the rolling ground and the water level, without the banks, the roads' easing or the building levels, so
        /// it needs no chunk (no chamfer, no cache). Within a metre or so of corner(); never used by the sim.</summary>
        public double coarse(int x, int y, bool water)
        {
            double town = townness(this, x, y);
            if (water) return swell(this, x, y, town) - WATER_DROP - SHORE;
            double small = (octave(this, x, y, S2, SALT2, A2) + octave(this, x, y, S3, SALT3, A3)) * (1 - town);
            double h = swell(this, x, y, town) + small;
            var s = plan.nature.start;
            double ds = JsMath.sqrt((double)(x - s.x) * (x - s.x) + (double)(y - s.y) * (y - s.y));
            double flat = 1 - smoothstep((ds - START_FLAT) / START_EASE);
            if (flat > 0) h += (startLevel(this) - h) * flat;
            return h;
        }

        /// <summary>A tile's floor: the highest of its corners (one level under a building).</summary>
        public double floor(int x, int y) => JsMath.max(JsMath.max(corner(x, y), corner(x + 1, y)), JsMath.max(corner(x, y + 1), corner(x + 1, y + 1)));

        internal double[] chunk(int kx, int ky)
        {
            int key = ky * 65536 + kx;
            if (done.TryGetValue(key, out var hit)) return hit;
            var made = finish(this, kx, ky);
            remember(done, doneOrder, key, made);
            return made;
        }

        internal double[] bareChunk(int kx, int ky)
        {
            int key = ky * 65536 + kx;
            if (bare.TryGetValue(key, out var hit)) return hit;
            var made = bareCorners(this, kx, ky);
            remember(bare, bareOrder, key, made);
            return made;
        }

        /// <summary>Steps 1-4 at a corner, through the chunk caches.</summary>
        internal double bareCorner(int cx, int cy)
        {
            cx = cx < 0 ? 0 : cx > size ? size : cx;
            cy = cy < 0 ? 0 : cy > size ? size : cy;
            int kx = cx / CHUNK, ky = cy / CHUNK;
            if (kx * CHUNK == cx && kx > 0 && kx * CHUNK >= size) kx--;
            if (ky * CHUNK == cy && ky > 0 && ky * CHUNK >= size) ky--;
            return bareChunk(kx, ky)[(cy - ky * CHUNK) * (CHUNK + 1) + (cx - kx * CHUNK)];
        }

        static void remember(Dictionary<int, double[]> cache, List<int> order, int key, double[] value)
        {
            // Oldest out first (a list, so the order is the order asked: rule 9).
            if (order.Count >= MAX_CHUNKS)
            {
                cache.Remove(order[0]);
                order.RemoveAt(0);
            }
            cache[key] = value;
            order.Add(key);
        }
    }

    static readonly JsMap<string, Field> cache = new JsMap<string, Field>();

    /// <summary>The height field of a generated world (cached by seed, size and map, as the plan is).</summary>
    public static Field field(double seed, double size, string map)
    {
        var key = $"{Js.Str(seed)}|{Js.Str(size)}|{Js.Str(map)}";
        var hit = cache.get(key);
        if (Js.T(hit)) return hit;
        var f = make(seed, size, map);
        if (cache.size > 6) cache.delete(cache.keys()[0]);
        cache.set(key, f);
        return f;
    }

    /// <summary>A new, empty field (not cached): the tests use it to ask for chunks in another order.</summary>
    public static Field make(double seed, double size, string map) => new Field { seed = seed, size = (int)size, map = map, plan = Worldplan.planWorld(seed, size, map) };

    /// <summary>The state's height field, or null on a hand-made map (flat).</summary>
    public static Field of(SimState state) => state.world == null ? null : field(state.world.seed, state.world.size, state.settings.map);

    /// <summary>The ground's height (m) at a point (tiles) in this state's world: 0 on hand-made maps.</summary>
    public static double height(SimState state, double x, double y) => of(state)?.at(x, y) ?? 0;

    // --- steps 1-3: the rolling ground, as damped by towns, roads and the start ---

    /// <summary>Large-scale relief (m): hills and valleys. Flat for now; they go here.</summary>
    public static double relief(Field f, int x, int y) => 0;

    static double octave(Field f, int x, int y, double scale, int salt, double amp) => amp * (NatureMod.noise(f.seed, x, y, scale, salt) - 0.5);

    static double smoothstep(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    /// <summary>How much of a town is here: 1 within its radius, easing to 0 over TOWN_EASE.</summary>
    static double townness(Field f, int x, int y)
    {
        double most = 0;
        // Only towns within their radius and TOWN_EASE can count (the rest give 0): asked through the index.
        if (f.townIndex == null)
        {
            f.townIndex = new TownIndex(f.plan.towns, f.size);
            f.townReach = f.townIndex.Biggest + TOWN_EASE + 1;
        }
        foreach (var ti in f.townIndex.Near(x, y, f.townReach, f.townScratch))
        {
            var t = f.townIndex[ti];
            double d = JsMath.sqrt((double)(x - t.x) * (x - t.x) + (double)(y - t.y) * (y - t.y));
            double v = 1 - smoothstep((d - t.radius) / TOWN_EASE);
            if (v > most) most = v;
        }
        return most;
    }

    /// <summary>The big octave as towns damp it, with the relief: what the water level follows.</summary>
    static double swell(Field f, int x, int y, double town) => relief(f, x, y) + octave(f, x, y, S1, SALT1, A1) * (1 - 0.5 * town);

    /// <summary>The water's surface at a corner (m).</summary>
    static double waterAt(Field f, int x, int y) => swell(f, x, y, townness(f, x, y)) - WATER_DROP;

    /// <summary>The ground before water and buildings at a corner, given its distance to a road (tiles).</summary>
    static double rolling(Field f, int x, int y, double road)
    {
        double town = townness(f, x, y);
        double small = (octave(f, x, y, S2, SALT2, A2) + octave(f, x, y, S3, SALT3, A3)) * (1 - town) * smoothstep(road / ROAD_EASE);
        double h = swell(f, x, y, town) + small;
        var s = f.plan.nature.start;
        double ds = JsMath.sqrt((double)(x - s.x) * (x - s.x) + (double)(y - s.y) * (y - s.y));
        double flat = 1 - smoothstep((ds - START_FLAT) / START_EASE);
        if (flat > 0) h += (startLevel(f) - h) * flat;
        return h;
    }

    static double startLevel(Field f)
    {
        var s = f.plan.nature.start;
        return swell(f, s.x, s.y, townness(f, s.x, s.y));
    }

    // --- step 4: water, by chunk ---

    /// <summary>Water the ground eases down to: the plan's water, less roads over it (bridges) and fences.</summary>
    static bool wet(Field f, int x, int y)
    {
        if (x < 0 || y < 0 || x >= f.size || y >= f.size) return false;
        if (f.plan.roadMask[y * f.size + x] != 0) return false;
        if (NatureMod.onFenceLine(f.plan.nature, x, y)) return false;
        return NatureMod.isWater(f.plan.nature, x, y);
    }

    static double[] bareCorners(Field f, int kx, int ky)
    {
        int x0 = kx * CHUNK - MARGIN, y0 = ky * CHUNK - MARGIN;
        int n = CHUNK + 2 * MARGIN;
        var water = new bool[n * n];
        var bridge = new bool[n * n];
        var toWater = new double[n * n];
        var toLand = new double[n * n];
        var toRoad = new double[n * n];
        const double FAR = 1e9;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int x = x0 + i, y = y0 + j, k = j * n + i;
                bool w = wet(f, x, y);
                water[k] = w;
                toWater[k] = w ? 0 : FAR;
                toLand[k] = w ? FAR : 0;
                bool road = x >= 0 && y >= 0 && x < f.size && y < f.size && f.plan.roadMask[y * f.size + x] != 0;
                toRoad[k] = road ? 0 : FAR;
                bridge[k] = road && NatureMod.isWater(f.plan.nature, x, y);
            }
        chamfer(toWater, n);
        chamfer(toLand, n);
        chamfer(toRoad, n);
        var out_ = new double[(CHUNK + 1) * (CHUNK + 1)];
        for (int cy = 0; cy <= CHUNK; cy++)
            for (int cx = 0; cx <= CHUNK; cx++)
            {
                int x = kx * CHUNK + cx, y = ky * CHUNK + cy;
                // The four tiles round the corner, in the window: (i - 1, j - 1) .. (i, j).
                int i = cx + MARGIN, j = cy + MARGIN;
                double dw = FAR, dl = FAR, dr = FAR;
                int wetTiles = 0;
                bool onBridge = false;
                for (int dj = -1; dj <= 0; dj++)
                    for (int di = -1; di <= 0; di++)
                    {
                        int k = (j + dj) * n + (i + di);
                        if (water[k]) wetTiles++;
                        onBridge |= bridge[k];
                        dw = JsMath.min(dw, toWater[k]);
                        dl = JsMath.min(dl, toLand[k]);
                        dr = JsMath.min(dr, toRoad[k]);
                    }
                double surface = waterAt(f, x, y);
                double h;
                if (onBridge) h = surface + DECK;
                else if (wetTiles == 4) h = surface - SHORE - BED - BED_STEP * JsMath.min(BED_MAX, dl);
                else if (wetTiles > 0) h = surface - SHORE;
                else
                {
                    h = rolling(f, x, y, dr);
                    if (dw < BANK)
                    {
                        double top = surface + BANK_TOP;
                        h = top + (h - top) * smoothstep(dw / BANK);
                    }
                }
                out_[cy * (CHUNK + 1) + cx] = h;
            }
        return out_;
    }

    /// <summary>Two-pass chamfer distance (1 along, 1.414 across), in place, capped by what's in the window.</summary>
    static void chamfer(double[] d, int n)
    {
        const double D = 1.414;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                double v = d[k];
                if (i > 0) v = JsMath.min(v, d[k - 1] + 1);
                if (j > 0)
                {
                    v = JsMath.min(v, d[k - n] + 1);
                    if (i > 0) v = JsMath.min(v, d[k - n - 1] + D);
                    if (i < n - 1) v = JsMath.min(v, d[k - n + 1] + D);
                }
                d[k] = v;
            }
        for (int j = n - 1; j >= 0; j--)
            for (int i = n - 1; i >= 0; i--)
            {
                int k = j * n + i;
                double v = d[k];
                if (i < n - 1) v = JsMath.min(v, d[k + 1] + 1);
                if (j < n - 1)
                {
                    v = JsMath.min(v, d[k + n] + 1);
                    if (i < n - 1) v = JsMath.min(v, d[k + n + 1] + D);
                    if (i > 0) v = JsMath.min(v, d[k + n - 1] + D);
                }
                d[k] = v;
            }
    }

    // --- step 5: buildings ---

    /// <summary>A lot's buildings: footprint rectangle (x, y, w, h, in tiles) and the level each sits on.</summary>
    static List<double[]> levels(Field f, Lot lot)
    {
        if (f.lotLevels.TryGetValue((int)lot.id, out var hit)) return hit;
        var list = new List<double[]>();
        foreach (var p in Lots.layoutLot(f.seed, lot).buildings)
        {
            bool turned = p.facing == "east" || p.facing == "west";
            int w = turned ? p.plan.h : p.plan.w, h = turned ? p.plan.w : p.plan.h;
            int x = p.ox, y = p.oy;
            double level = (f.bareCorner(x, y) + f.bareCorner(x + w, y) + f.bareCorner(x, y + h) + f.bareCorner(x + w, y + h)
                + f.bareCorner(x + w / 2, y + h / 2)) / 5;
            list.Add([x, y, w, h, level]);
        }
        f.lotLevels[(int)lot.id] = list;
        return list;
    }

    static double[] finish(Field f, int kx, int ky)
    {
        var bare = f.bareChunk(kx, ky);
        var out_ = (double[])bare.Clone();
        // Buildings in this plan chunk and its neighbours (BUILDING_EASE < CHUNK), each once, in lot order.
        int chunks = (int)JsMath.ceil((double)f.size / CHUNK);
        var ids = new List<int>();
        var seen = new HashSet<int>();
        for (int py = ky - 1; py <= ky + 1; py++)
            for (int px = kx - 1; px <= kx + 1; px++)
            {
                if (px < 0 || py < 0 || px >= chunks || py >= chunks) continue;
                foreach (var id in f.plan.lotsByChunk[py * chunks + px])
                    if (seen.Add((int)id)) ids.Add((int)id);
            }
        ids.Sort();
        var rects = new List<double[]>();
        double x0 = kx * CHUNK - BUILDING_EASE, x1 = (kx + 1) * CHUNK + BUILDING_EASE, y0 = ky * CHUNK - BUILDING_EASE, y1 = (ky + 1) * CHUNK + BUILDING_EASE;
        foreach (var id in ids)
            foreach (var r in levels(f, f.plan.lots[id]))
                if (r[0] + r[2] >= x0 && r[0] <= x1 && r[1] + r[3] >= y0 && r[1] <= y1) rects.Add(r);
        if (rects.Count == 0) return out_;
        for (int cy = 0; cy <= CHUNK; cy++)
            for (int cx = 0; cx <= CHUNK; cx++)
            {
                int k = cy * (CHUNK + 1) + cx;
                int x = kx * CHUNK + cx, y = ky * CHUNK + cy;
                double most = 0, sumW = 0, sumWL = 0, inside = double.NaN;
                foreach (var r in rects)
                {
                    double dx = JsMath.max(0, JsMath.max(r[0] - x, x - (r[0] + r[2])));
                    double dy = JsMath.max(0, JsMath.max(r[1] - y, y - (r[1] + r[3])));
                    double d2 = dx * dx + dy * dy;
                    if (d2 == 0) { inside = r[4]; break; }
                    double d = JsMath.sqrt(d2);
                    if (d >= BUILDING_EASE) continue;
                    double w = 1 - smoothstep(d / BUILDING_EASE);
                    if (w > most) most = w;
                    sumW += w / d2;
                    sumWL += w / d2 * r[4];
                }
                // Water's corners stay the water's.
                bool shore = bare[k] <= waterAt(f, x, y) - SHORE + 1e-9;
                if (shore) continue;
                if (!double.IsNaN(inside)) out_[k] = inside;
                else if (sumW > 0) out_[k] = bare[k] + (sumWL / sumW - bare[k]) * most;
            }
        return out_;
    }
}
