// Growth (2026-11-10): the world overgrows as the days pass, computed, not stored.
//
//   How overgrown a tile is = a pure function of the seed, the tile's position, what its ground is (Kind: yard,
//   verge, meadow, field, dirt, forest floor, road, pavement, a ruin's floor) and how many GROWING days have passed
//   since the tile was last cleared (never cleared: since the world began, when everything is as generated).
//   Growing days: a spring or summer day counts 1, an autumn day 0.35, a winter day 0 (SEASON_RATE; half again in a
//   nuclear winter). Nothing iterates the map each tick: a tile's cover is asked for when something needs it
//   (Growth.cover; the game's drawer, forage), and the answer is the same whatever order tiles are asked in and
//   across a save and load.
//
//   The only saved state is `state.cleared`: a sparse record, tile index -> the tick the tile was cleared
//   (Growth.clear: built on, dug, cut, burnt out). From that tick it regrows from nothing. An entry is dropped once
//   its tile has regrown to the end of every stage (expire, once a day), when "since the world began" gives the same
//   answer, so the record holds only tiles cleared within the last ~year and a half of game time.
//
//   Rates. L = a tile's growing days since its clearing x its own pace (0.75..1.25: a hash and a patchy noise). A season
//   is SEASON_DAYS (5) game days, so a game year is 20 days and 11.75 growing days, 0.59 a day on average: L 10 is
//   about game day 10, L 30 day 50, L 100 day 170, L 200 day 340, L 350 day 600 (from day 1, in spring).
//     yard grass          grass long 0.5..9 (a week)         weeds 8..36 (a month)        bushes 36..110 (months), a third of the tiles
//     verge, meadow       the same, sooner (verge) or later (meadow): Profile
//     garden, field       grass and weeds fastest (3..20), bushes 25..80, saplings come up (4% of tiles)
//     forest floor        undergrowth 10..60, bushes 40..130, saplings (5% of tiles) thicken the woods
//     saplings            appear at L 30..90 and take 60..140 more to full size; a sim tree from 40% of that
//     road, pavement      cracks 60..200 (a year), grass creeping in from the edges 130..350, along the middle 300..600
//   Saplings on forest floor, fields and gardens, and at the edges of woods (NatureMod.woodedness) are the one rule
//   for trees coming back: a candidate tile (a stateless hash) has a maturity tick, and at it the sim gets a real
//   `tree` (FurnitureMod furniture: choppable, solid via Trunks), planted by Growth.update for the chunks round the
//   survivors (an event per chunk plan, not a sweep), or when such a chunk is first looked at, so the tick a tree
//   stands from is a function of the tile alone: nothing about who is near or which chunk was made first matters.
//   (The wood a chopped tree grows back, every TREE_REGROW_TICKS, lives here too: regrowWood.)
//
// Everything is derived data (the chunk plans are a cache); rule 14: bounded work per tick, no map sweeps.
#nullable disable warnings
namespace LoonieRoo.Sim;

public enum GrowthKind { None, Yard, Verge, Meadow, Field, Dirt, Forest, Road, Hard, Floor }

/// <summary>What grows on a tile now, each 0..1 (sapling: the sapling's progress, or -1 if there is none).</summary>
public readonly record struct Cover(GrowthKind kind, double grass, double weeds, double bush, double crack, double creep, double sapling)
{
    public static readonly Cover NONE = new(GrowthKind.None, 0, 0, 0, 0, 0, -1);

    /// <summary>How thick the cover is, 0..1: what could hide a walker or hold forage. Grass counts a little, weeds and bushes more.</summary>
    public double level => Math.Min(1, 0.35 * grass + 0.75 * weeds + 1.0 * bush);

    /// <summary>The height of the growth, metres (grass to the knee, weeds to the waist, bushes to the head).</summary>
    public double height => Math.Max(Math.Max(0.45 * grass, 0.9 * weeds), 1.6 * bush);
}

public static class Growth
{
    // ---------- the calendar ----------

    /// <summary>Growing days per game day: spring, summer, autumn, winter.</summary>
    public static readonly double[] SEASON_RATE = { 1.0, 1.0, 0.35, 0.0 };

    static double Days(int tick) => ((double)tick + (double)Time.START_HOUR * Time.TICKS_PER_HOUR) / Time.DAY_LENGTH;

    /// <summary>Growing days from the start of day 1 to day-count d (a fractional count of game days), by season.</summary>
    static double Cum(double d)
    {
        int s = Seasons.SEASON_DAYS;
        double year = 4.0 * s, perYear = 0;
        foreach (var r in SEASON_RATE) perYear += s * r;
        double yrs = Math.Floor(d / year), rem = d - yrs * year, g = yrs * perYear;
        for (int i = 0; i < 4; i++) g += Math.Clamp(rem - i * s, 0, s) * SEASON_RATE[i];
        return g;
    }

    static double Scale(SimState state) => state.settings.winter ? 0.5 : 1.0;

    /// <summary>Growing days elapsed at a tick since the world began (0 at tick 0). Non-decreasing; flat through winter.</summary>
    public static double G(SimState state, int tick) => (Cum(Days(tick)) - Cum(Days(0))) * Scale(state);

    /// <summary>The first tick at which G reaches g.</summary>
    public static int tickWhen(SimState state, double g)
    {
        if (g <= 0) return 0;
        int s = Seasons.SEASON_DAYS;
        double year = 4.0 * s, perYear = 0;
        foreach (var r in SEASON_RATE) perYear += s * r;
        double c = g / Scale(state) + Cum(Days(0));
        double yrs = Math.Floor(c / perYear), rem = c - yrs * perYear, d = (yrs + 1) * year;
        for (int i = 0; i < 4; i++)
        {
            double cap = s * SEASON_RATE[i];
            if (SEASON_RATE[i] > 0 && rem <= cap) { d = yrs * year + i * s + rem / SEASON_RATE[i]; break; }
            rem -= cap;
        }
        double t = Math.Ceiling(d * Time.DAY_LENGTH - (double)Time.START_HOUR * Time.TICKS_PER_HOUR);
        int tick = (int)Math.Max(0, Math.Min(int.MaxValue - 1, t));
        while (G(state, tick) < g) tick++;
        while (tick > 0 && G(state, tick - 1) >= g) tick--;
        return tick;
    }

    // ---------- the record ----------

    /// <summary>A tile was cleared (built on, dug, cut, burnt out): it regrows from nothing from this tick. Always this way.</summary>
    public static void clear(SimState state, int x, int y)
    {
        if (!Grids.inBounds(state.grid, x, y) || state.tick <= 0) return;   // at tick 0 cleared is how every tile begins
        state.cleared[Js.Str(Grids.tileIndex(state.grid, x, y))] = state.tick;
        var ix = Ix(state);
        ix.version++;
        ix.requeue(state, x, y);
    }
    public static void clear(SimState state, double x, double y) => clear(state, Js.I(x), Js.I(y));

    /// <summary>The tick a tile's growth started from: its clearing, or 0.</summary>
    public static int epochOf(SimState state, int idx)
    {
        if (state.cleared.Count == 0) return 0;
        return state.cleared.TryGetValue(Js.Str(idx), out var t) ? (int)t : 0;
    }

    /// <summary>The record as it stands: tiles with their clearing ticks (for tests and the debug panel).</summary>
    public static int recordSize(SimState state) => state.cleared.Count;

    /// <summary>Changes whenever a tile is cleared, the record is trimmed or a tree is planted: the drawer rebuilds on it.</summary>
    public static int version(SimState state) => Ix(state).version;

    // ---------- the tile ----------

    static uint Mix(uint a, uint b, uint c, uint d)
    {
        uint h = unchecked(a * 0x9e3779b1u ^ b * 0x85ebca6bu ^ c * 0xc2b2ae35u ^ d * 0x27d4eb2fu);
        h ^= h >> 15; h = unchecked(h * 0x2c1b3c6du); h ^= h >> 12; h = unchecked(h * 0x297a2d39u); h ^= h >> 15;
        return h;
    }

    static double seedOf(SimState state) => state.world?.seed ?? 0;

    /// <summary>A stateless roll in [0, 1) of the world's seed, a tile and a salt.</summary>
    public static double roll(SimState state, int x, int y, uint salt) =>
        (Mix(unchecked((uint)(long)seedOf(state)), unchecked((uint)x), unchecked((uint)y), salt) & 0xFFFFFF) / 16777216.0;

    const uint S_RATE = 0x6a01, S_PATCH = 0x6a02, S_BUSH = 0x6a03, S_SAPLING = 0x6a04, S_START = 0x6a05, S_LEN = 0x6a06, S_CREEP = 0x6a07, S_TUFT = 0x6a08;

    /// <summary>This tile's own pace, 0.75..1.25: half a hash of the tile, half a patchy noise (so growth comes in patches).</summary>
    public static double paceOf(SimState state, int x, int y)
    {
        double patch = NatureMod.noise(seedOf(state), x, y, 7, S_PATCH);
        return 0.75 + 0.5 * (0.5 * roll(state, x, y, S_RATE) + 0.5 * patch);
    }

    static readonly int G_GRASS = Array.IndexOf(Grids.GROUNDS, "grass"), G_DIRT = Array.IndexOf(Grids.GROUNDS, "dirt"), G_ROAD = Array.IndexOf(Grids.GROUNDS, "road"),
        G_FOREST = Array.IndexOf(Grids.GROUNDS, "forest"), G_BRIDGE = Array.IndexOf(Grids.GROUNDS, "bridge"), G_FIELD = Array.IndexOf(Grids.GROUNDS, "field"),
        G_PAVING = Array.IndexOf(Grids.GROUNDS, "paving"), G_BOARDS = Array.IndexOf(Grids.GROUNDS, "boards"), G_TILES = Array.IndexOf(Grids.GROUNDS, "tiles"),
        G_CONCRETE = Array.IndexOf(Grids.GROUNDS, "concrete");

    static int groundAt(Grid grid, int x, int y) => Grids.inBounds(grid, x, y) ? Grids.groundAt(grid, Grids.tileIndex(grid, x, y)) : -1;

    static bool isRoadish(int g) => g == G_ROAD || g == G_BRIDGE || g == G_PAVING;

    /// <summary>What the ground here is, as far as growth goes (None: water, walls, doors, bridges, indoor floors, farm plots).</summary>
    public static GrowthKind kindAt(SimState state, int x, int y)
    {
        var grid = state.grid;
        if (!Grids.inBounds(grid, x, y)) return GrowthKind.None;
        int i = Grids.tileIndex(grid, x, y);
        if (Grids.tileAt(grid, i) != Grids.FLOOR) return GrowthKind.None;
        int g = Grids.groundAt(grid, i);
        if (state.farm.Count > 0 && Farming.plotAt(state, x, y) != null) return GrowthKind.None;
        if (g == G_FOREST) return GrowthKind.Forest;
        if (g == G_ROAD) return GrowthKind.Road;
        if (g == G_BRIDGE) return GrowthKind.None;
        if (g == G_PAVING) return GrowthKind.Hard;
        if (g == G_BOARDS || g == G_TILES || g == G_CONCRETE)
        {
            // Floors: indoors (a complete room) nothing grows; open to the sky, a yard of concrete is pavement and the rest a ruin's floor.
            if (Rooms.isIndoors(state, x, y)) return GrowthKind.None;
            return g == G_CONCRETE ? GrowthKind.Hard : GrowthKind.Floor;
        }
        if (g == G_FIELD) return GrowthKind.Field;
        var ix = Ix(state);
        bool lot = ix.regions != null && ix.regions.OnLot(x, y);
        if (g == G_DIRT) return lot ? GrowthKind.Field : GrowthKind.Dirt;
        if (g != G_GRASS) return GrowthKind.Meadow;
        if (lot) return GrowthKind.Yard;
        return nearRoad(state, ix, x, y) ? GrowthKind.Verge : GrowthKind.Meadow;
    }

    static bool nearRoad(SimState state, GrowthIndex ix, int x, int y)
    {
        if (ix.regions != null)
        {
            var r = ix.regions;
            return r.IsRoad(x - 1, y) || r.IsRoad(x + 1, y) || r.IsRoad(x, y - 1) || r.IsRoad(x, y + 1) || r.IsRoad(x - 2, y) || r.IsRoad(x + 2, y) || r.IsRoad(x, y - 2) || r.IsRoad(x, y + 2);
        }
        var grid = state.grid;
        return isRoadish(groundAt(grid, x - 1, y)) || isRoadish(groundAt(grid, x + 1, y)) || isRoadish(groundAt(grid, x, y - 1)) || isRoadish(groundAt(grid, x, y + 1));
    }

    /// <summary>A road, pavement or floor tile beside one that isn't: where grass creeps in from.</summary>
    static bool onEdge(SimState state, int x, int y)
    {
        var grid = state.grid;
        int n = groundAt(grid, x, y - 1), e = groundAt(grid, x + 1, y), s = groundAt(grid, x, y + 1), w = groundAt(grid, x - 1, y);
        return (n >= 0 && !isRoadish(n)) || (e >= 0 && !isRoadish(e)) || (s >= 0 && !isRoadish(s)) || (w >= 0 && !isRoadish(w));
    }

    static double ramp(double l, double from, double to) => l <= from ? 0 : l >= to ? 1 : (l - from) / (to - from);

    /// <summary>Per kind: grass from..to, weeds from..to, bushes from..to and the share of tiles that can have a bush.</summary>
    static (double g0, double g1, double w0, double w1, double b0, double b1, double bushy) Profile(GrowthKind k) => k switch
    {
        GrowthKind.Yard => (0.5, 9, 8, 36, 36, 110, 0.35),
        GrowthKind.Verge => (0.5, 7, 6, 28, 34, 100, 0.25),
        GrowthKind.Meadow => (0.5, 11, 14, 50, 50, 130, 0.2),
        GrowthKind.Field => (0.3, 5, 3, 20, 25, 80, 0.4),
        GrowthKind.Dirt => (6, 30, 6, 30, 60, 140, 0.15),
        GrowthKind.Forest => (1, 1, 10, 60, 40, 130, 0.3),
        _ => (1, 1, 1, 1, 1, 1, 0),
    };

    /// <summary>How far a tile has grown since it was cleared, in L units (growing days x its pace).</summary>
    public static double levelAt(SimState state, int x, int y, double gNow)
    {
        int idx = Grids.tileIndex(state.grid, x, y);
        double d = gNow - G(state, epochOf(state, idx));
        return d <= 0 ? 0 : d * paceOf(state, x, y);
    }

    /// <summary>What grows on a tile now.</summary>
    public static Cover cover(SimState state, int x, int y) => coverWith(state, x, y, G(state, state.tick));

    /// <summary>The same, given G(now) (for a batch of tiles: the drawer asks a few thousand at once).</summary>
    public static Cover coverWith(SimState state, int x, int y, double gNow)
    {
        var kind = kindAt(state, x, y);
        return coverFor(state, x, y, kind, levelAt(state, x, y, gNow));
    }

    static Cover coverFor(SimState state, int x, int y, GrowthKind kind, double l)
    {
        if (kind == GrowthKind.None) return Cover.NONE;
        if (kind == GrowthKind.Road || kind == GrowthKind.Hard || kind == GrowthKind.Floor)
        {
            var (c0, c1, e0, e1, m0, m1, scale) = kind switch
            {
                GrowthKind.Road => (60.0, 200.0, 130.0, 350.0, 300.0, 600.0, 1.0),
                GrowthKind.Hard => (50.0, 160.0, 110.0, 320.0, 250.0, 500.0, 0.8),
                _ => (90.0, 220.0, 200.0, 450.0, 200.0, 450.0, 0.5),
            };
            double mask = 0.4 + 0.6 * roll(state, x, y, S_CREEP);
            double creep = (onEdge(state, x, y) ? ramp(l, e0, e1) : 0.4 * ramp(l, m0, m1)) * mask * scale;
            return new Cover(kind, 0, 0, 0, ramp(l, c0, c1), creep, -1);
        }
        var (g0, g1, w0, w1, b0, b1, bushy) = Profile(kind);
        double grass = g1 > g0 ? ramp(l, g0, g1) : 0;
        if (kind == GrowthKind.Dirt) grass *= 0.5;
        double weeds = ramp(l, w0, w1);
        double bush = roll(state, x, y, S_BUSH) < bushy ? ramp(l, b0, b1) : 0;
        double sap = -1;
        if (candidate(state, x, y, kind, out var s0, out var len)) sap = l < s0 ? -1 : Math.Min(1, (l - s0) / len);
        return new Cover(kind, grass, weeds, bush, 0, 0, sap);
    }

    /// <summary>A tuft's own roll for the drawer (a hash of the tile): 0..1.</summary>
    public static double tuftRoll(SimState state, int x, int y, uint k) => roll(state, x, y, S_TUFT + k * 16);

    /// <summary>Foraging in overgrown ground finds more: 1 on a bare lawn, up to 1.5 in weeds and bushes.</summary>
    public static double forageBonus(SimState state, int x, int y)
    {
        if (state.tick == 0) return 1;
        return 1 + 0.5 * cover(state, x, y).level;
    }

    // ---------- saplings ----------

    /// <summary>The share of a sapling's span (0..1) at which it is a sim tree: from here you bump into it and can chop it.</summary>
    public const double SOLID_AT = 0.4;

    /// <summary>The size a growing tree is drawn at, as a share of its full size, at progress p (0..1).</summary>
    public static double treeScale(double p) => 0.1 + 0.9 * Math.Clamp(p, 0, 1);

    /// <summary>Is this tile a place a sapling comes up (a hash of the tile, by its kind and, at a wood's edge, how wooded the land is)? Its start (L) and how long it takes to reach full size.</summary>
    public static bool candidate(SimState state, int x, int y, GrowthKind kind, out double start, out double span)
    {
        start = span = 0;
        double r = roll(state, x, y, S_SAPLING);
        if (r >= 0.06) return false;
        double p;
        switch (kind)
        {
            case GrowthKind.Forest: p = 0.05; break;
            case GrowthKind.Field: p = 0.04; break;
            case GrowthKind.Verge: p = 0.002; break;
            case GrowthKind.Meadow:
            case GrowthKind.Dirt:
                {
                    var ix = Ix(state);
                    if (ix.nature == null) return false;
                    double w = NatureMod.woodedness(ix.nature, x, y);
                    p = 0.06 * Math.Clamp((w - 0.38) / 0.22, 0, 1);
                    break;
                }
            default: return false;
        }
        if (r >= p) return false;
        if (state.world != null && NatureMod.nearStart(Ix(state).nature, x, y, NatureMod.START_CLEAR_RADIUS)) return false;
        start = 30 + 60 * roll(state, x, y, S_START);
        span = 60 + 80 * roll(state, x, y, S_LEN);
        return true;
    }

    /// <summary>The tick a candidate tile's sapling is solid (a sim tree), counting from its clearing.</summary>
    public static int solidTick(SimState state, int x, int y, double start, double span)
    {
        int epoch = epochOf(state, Grids.tileIndex(state.grid, x, y));
        double need = (start + SOLID_AT * span) / paceOf(state, x, y);
        return tickWhen(state, G(state, epoch) + need);
    }

    /// <summary>A growing tree's (or sapling's) progress at a tile, 0..1, drawn by Trees.cs; 1 for a tile that isn't a candidate.</summary>
    public static double treeProgress(SimState state, int x, int y)
    {
        var kind = kindAt(state, x, y);
        if (kind == GrowthKind.None) return 1;
        if (!candidate(state, x, y, kind, out var s0, out var len)) return 1;
        double l = levelAt(state, x, y, G(state, state.tick));
        return Math.Clamp((l - s0) / len, 0, 1);
    }

    // ---------- the world's trees coming back ----------

    sealed class Plan
    {
        public int cx, cy, index;
        public readonly SortedSet<(int tick, int y, int x)> due = new();
        public readonly Dictionary<int, int> tickOf = new();
        public void set(int x, int y, int tick)
        {
            int key = y * 100003 + x;
            if (tickOf.TryGetValue(key, out var old)) due.Remove((old, y, x));
            tickOf[key] = tick;
            due.Add((tick, y, x));
        }
        public void drop(int x, int y)
        {
            int key = y * 100003 + x;
            if (tickOf.TryGetValue(key, out var old)) { due.Remove((old, y, x)); tickOf.Remove(key); }
        }
    }

    sealed class GrowthIndex
    {
        public WorldRegions regions;
        public Nature nature;
        public int version;
        public readonly SortedDictionary<int, Plan> plans = new();
        public int lastScan = -1000;
        public int chunksWide;

        public void requeue(SimState state, int x, int y)
        {
            if (!plans.TryGetValue(chunkIndex(x, y), out var plan)) return;
            var kind = kindAt(state, x, y);
            if (kind != GrowthKind.None && candidate(state, x, y, kind, out var s0, out var len)) plan.set(x, y, solidTick(state, x, y, s0, len));
            else plan.drop(x, y);
        }

        int chunkIndex(int x, int y) => (y / Worldgen.CHUNK) * chunksWide + (x / Worldgen.CHUNK);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SimState, GrowthIndex> indexes = new();

    static GrowthIndex Ix(SimState state)
    {
        if (indexes.TryGetValue(state, out var ix)) return ix;
        ix = new GrowthIndex { chunksWide = (state.grid.width + Worldgen.CHUNK - 1) / Worldgen.CHUNK };
        if (state.world != null)
        {
            ix.regions = Worldplan.regions(state.world.seed, state.world.size, state.settings.map);
            ix.nature = NatureMod.natureFor(state.world.seed, state.world.size, state.settings.map);
        }
        indexes.Add(state, ix);
        return ix;
    }

    /// <summary>How often (ticks) the chunks round the survivors are looked for new ones.</summary>
    const int SCAN_EVERY = 20;

    /// <summary>Called by Farming.updateFarm on slow ticks: plants the saplings that have grown solid, tops up wood, trims the record at dawn.</summary>
    public static void update(SimState state)
    {
        var ix = Ix(state);
        if (state.tick - ix.lastScan >= SCAN_EVERY || state.tick < ix.lastScan)
        {
            ix.lastScan = state.tick;
            scan(state, ix);
        }
        plant(state, ix);
        if (state.tick % FurnitureMod.TREE_REGROW_TICKS == 0) regrowWood(state);
        if (state.tick > 0 && state.tick % Time.DAY_LENGTH == 0) expire(state);
    }

    /// <summary>Chunks to keep plans for: those the survivors can see made (a hand-made map: all of it). Others lose theirs (made again if a survivor comes back).</summary>
    static void scan(SimState state, GrowthIndex ix)
    {
        var want = new SortedSet<int>();
        int cw = ix.chunksWide, ch = (state.grid.height + Worldgen.CHUNK - 1) / Worldgen.CHUNK;
        if (state.world == null)
        {
            for (int c = 0; c < cw * ch; c++) want.Add(c);
        }
        else
        {
            foreach (var s in state.survivors)
            {
                int pcx = s.pos.x / Worldgen.CHUNK, pcy = s.pos.y / Worldgen.CHUNK;
                for (int cy = Math.Max(0, pcy - Worldgen.GENERATE_RADIUS); cy <= Math.Min(ch - 1, pcy + Worldgen.GENERATE_RADIUS); cy++)
                    for (int cx = Math.Max(0, pcx - Worldgen.GENERATE_RADIUS); cx <= Math.Min(cw - 1, pcx + Worldgen.GENERATE_RADIUS); cx++)
                        if (Js.Get(state.world.generated, (double)cy * state.world.chunks + cx) == 1) want.Add(cy * cw + cx);
            }
        }
        foreach (var k in ix.plans.Keys.Where((k) => !want.Contains(k)).ToList()) ix.plans.Remove(k);
        // A few plans a look (a plan is a millisecond or two): the rest the next slow tick, nearest the start of the list first.
        int made = 0;
        foreach (var c in want)
        {
            if (ix.plans.ContainsKey(c)) continue;
            if (made++ >= PLANS_PER_LOOK) { ix.lastScan = state.tick - SCAN_EVERY; return; }
            ix.plans[c] = makePlan(state, ix, c % cw, c / cw);
        }
    }

    /// <summary>Chunk plans made in one look at the survivors' surroundings at most (rule 14: bound the work a tick does).</summary>
    const int PLANS_PER_LOOK = 2;

    /// <summary>A chunk's candidates: every tile whose hash says a sapling comes up, each with the tick it is solid.</summary>
    static Plan makePlan(SimState state, GrowthIndex ix, int cx, int cy)
    {
        var plan = new Plan { cx = cx, cy = cy, index = cy * ix.chunksWide + cx };
        int x0 = cx * Worldgen.CHUNK, y0 = cy * Worldgen.CHUNK;
        int x1 = Math.Min(state.grid.width, x0 + Worldgen.CHUNK), y1 = Math.Min(state.grid.height, y0 + Worldgen.CHUNK);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (roll(state, x, y, S_SAPLING) >= 0.06) continue;
                int gr = groundAt(state.grid, x, y);
                if (gr == G_BOARDS || gr == G_TILES || gr == G_CONCRETE || isRoadish(gr)) continue;   // never a sapling's ground (and a floor would ask Rooms)
                var kind = kindAt(state, x, y);
                if (kind == GrowthKind.None) continue;
                if (candidate(state, x, y, kind, out var s0, out var len)) plan.set(x, y, solidTick(state, x, y, s0, len));
            }
        return plan;
    }

    /// <summary>Plants what is due: in order of tick, then row, then column, whatever the chunk order, so two runs (and a run and its reload) agree.</summary>
    static void plant(SimState state, GrowthIndex ix)
    {
        List<(int tick, int y, int x, Plan plan)> ready = null;
        foreach (var plan in ix.plans.Values)
            while (plan.due.Count > 0 && plan.due.Min.tick <= state.tick)
            {
                var m = plan.due.Min;
                plan.due.Remove(m);
                plan.tickOf.Remove(m.y * 100003 + m.x);
                (ready ??= new()).Add((m.tick, m.y, m.x, plan));
            }
        if (ready == null) return;
        ready.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        foreach (var (_, y, x, plan) in ready)
        {
            var kind = kindAt(state, x, y);
            if (kind == GrowthKind.None || !candidate(state, x, y, kind, out var s0, out var len)) continue;
            int when = solidTick(state, x, y, s0, len);
            if (when > state.tick) { plan.set(x, y, when); continue; }   // cleared since: it grows again from then
            if (FurnitureMod.furnitureAt(state, x, y) != null) continue;
            FurnitureMod.adopt(state, new Furniture { id = $"g{x}.{y}", kind = "tree", x = x, y = y, occupant = null, owner = null, loot = FurnitureMod.TREE_WOOD, scavenge = false, stored = 0, storedKind = null, look = null, table = null });
            ix.version++;
        }
    }

    /// <summary>A chopped tree grows a piece of wood back each TREE_REGROW_TICKS, up to TREE_WOOD (the old FurnitureMod.regrowTrees, over trees only).</summary>
    public static void regrowWood(SimState state)
    {
        foreach (var f in FurnitureMod.ofKind(state, "tree"))
            if (f.loot < FurnitureMod.TREE_WOOD) f.loot += 1;
    }

    /// <summary>Drops the record's entries whose tiles have grown through every stage (SAT growing days at the slowest pace): what the entry would say, "since the world began" says too.</summary>
    public static void expire(SimState state)
    {
        if (state.cleared.Count == 0) return;
        double gNow = G(state, state.tick);
        var gone = new List<string>();
        foreach (var key in state.cleared.Keys.OrderBy((k) => int.Parse(k)))
        {
            int idx = int.Parse(key);
            int x = idx % state.grid.width, y = idx / state.grid.width;
            var kind = kindAt(state, x, y);
            double sat = (kind == GrowthKind.Road || kind == GrowthKind.Hard || kind == GrowthKind.Floor) ? SAT_HARD : SAT_SOFT;
            if (gNow - G(state, (int)state.cleared[key]) >= sat / 0.75) gone.Add(key);
        }
        foreach (var k in gone) state.cleared.Remove(k);
        if (gone.Count > 0) Ix(state).version++;
    }

    /// <summary>L at which every stage is done: grass, weeds, bushes and saplings (60+140+30+60 at most); roads and floors.</summary>
    public const double SAT_SOFT = 240, SAT_HARD = 600;
}
