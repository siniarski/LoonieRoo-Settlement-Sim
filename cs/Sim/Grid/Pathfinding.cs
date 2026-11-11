// A* on the grid, 4-way, Manhattan heuristic; reachability labels; straightening (pathfinding.ts).
// Deterministic: ties broken by lower h, then insertion order; neighbours expanded N, E, S, W.
// Movers: "survivor" (colonists: any door), "free" (you: doors open), "zombie", "breacher" (batters
// through anything but water), "raider" (unlocked doors), "crawler" (under barricades), "vehicle",
// "animal" (wild animals: open floor only; through open doors and broken windows, never onto a door's tile).
// The dead's paths (not their bodies) also go over parked cars, and a "runner"'s over barricades, at a cost:
// Crossings.cs (the hooks here: A*'s step, reachability's rules, the open doors' joins).
using System.Runtime.CompilerServices;
using static LoonieRoo.Sim.Grids;

namespace LoonieRoo.Sim;

public static partial class Pathfinding
{
    public const string SURVIVOR = "survivor", FREE = "free", ZOMBIE = "zombie", BREACHER = "breacher", RAIDER = "raider", CRAWLER = "crawler", VEHICLE = "vehicle", ANIMAL = "animal";

    /// <summary>The mover a runner's paths are planned for: a zombie that vaults barricades (Crossings.cs). Its body
    /// is moved as a zombie: isPassable and crossable know no "runner", so it gets the last line of each, the dead's.</summary>
    public const string RUNNER = "runner";

    public static bool isPassable(Grid grid, int x, int y, string mover = SURVIVOR)
    {
        // A parked car is in everyone's way but another car's (cars meet body to body: VehicleBody.cs).
        if (grid.parkedCount > 0 && mover != VEHICLE && Grids.isParked(grid, x, y)) return false;
        if (mover == BREACHER) return inBounds(grid, x, y) && getTile(grid, x, y) != WATER;
        var tile = getTile(grid, x, y);
        bool openDoor = tile == DOOR && isOpen(grid, x, y);
        if (mover == CRAWLER) return tile == FLOOR || tile == BARRICADE || openDoor;
        if (mover == VEHICLE || mover == ANIMAL) return tile == FLOOR;
        if (mover == RAIDER) return tile == FLOOR || (tile == DOOR && !isLocked(grid, x, y));
        if (mover == SURVIVOR) return isWalkable(grid, x, y);
        if (mover == FREE) return tile == FLOOR || tile == BARRICADE || openDoor;
        return tile == FLOOR || openDoor; // the dead ("zombie", and a "runner"'s body)
    }

    /// <summary>Can this mover cross from a tile to the one beside it (edge walls, doors, barricades, windows)?</summary>
    public static bool crossable(Grid grid, int ax, int ay, int bx, int by, string mover = SURVIVOR)
    {
        int e = edgeBetween(grid, ax, ay, bx, by, out int kind);
        if (e < 0) return true;
        if (kind == EDGE_NONE || mover == BREACHER) return true;
        if (kind == EDGE_WALL || mover == VEHICLE) return false;
        if (kind == EDGE_DOOR) return mover == SURVIVOR || (mover == RAIDER && !grid.edgeLocked.Contains(e)) || edgeIsOpen(grid, e);
        if (kind == EDGE_BARRICADE) return mover == SURVIVOR || mover == FREE || mover == CRAWLER;
        if (kind == EDGE_WINDOW || grid.edgeBoarded.Contains(e)) return false;
        if (kind == EDGE_WINDOW_BROKEN) return mover != SURVIVOR;
        return true;
    }

    public static readonly List<Point> DIRS = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    /// <summary>Path from start to goal, excluding start, including goal; [] if already there; null if unreachable.</summary>
    public static List<Point>? findPath(Grid grid, Point start, Point goal) => findPathToAny(grid, start, new[] { goal });

    /// <summary>Shortest path to whichever goal is nearest. Unwalkable goals are ignored. `maxExpand`: give up (null) after expanding this many tiles (a bounded search, for movers that only look nearby).
    /// `overCar` above 0: a tile under a parked car may be crossed, at that much more than a step (animals: AnimalCars.cs).</summary>
    public static List<Point>? findPathToAny(Grid grid, Point start, IReadOnlyList<Point> goals, string mover = SURVIVOR, int maxExpand = int.MaxValue, double overCar = 0)
    {
        if (!inBounds(grid, start.x, start.y)) return null;
        var targets = goals.Where(g => isPassable(grid, g.x, g.y, mover)).ToList();
        if (targets.Count == 0) return null;
        if (targets.Any(g => g.x == start.x && g.y == start.y)) return new List<Point>();

        int width = grid.width;
        int startIdx = start.y * width + start.x;
        var goalSet = new HashSet<int>(targets.Select(g => g.y * width + g.x));

        var sc = ScratchFor(grid);
        uint gen = sc.gen;
        var open = new MinHeap();
        bool crosses = Crossings.plans(mover);

        double H(int idx)
        {
            int x = idx % width;
            int y = idx / width;
            double best = double.PositiveInfinity;
            foreach (var g in targets) best = Math.Min(best, Math.Abs(x - g.x) + Math.Abs(y - g.y));
            return best;
        }

        ref var first = ref sc.At(start.x, start.y);
        first.seen = gen;
        first.g = 0;
        first.from = -1;
        open.Push(startIdx, H(startIdx), H(startIdx));

        while (open.Count > 0)
        {
            int current = open.Pop();
            if (goalSet.Contains(current)) return Reconstruct(sc, current, width);
            int cx = current % width;
            int cy = current / width;
            ref var here = ref sc.At(cx, cy);
            if (here.closed == gen) continue;
            here.closed = gen;
            if (--maxExpand < 0) return null;
            double g0 = here.seen == gen ? here.g : double.PositiveInfinity;
            foreach (var dir in DIRS)
            {
                int nx = cx + dir.x;
                int ny = cy + dir.y;
                double gn = g0 + 1;
                // An animal may plan under or over a parked car, at a cost by kind (AnimalCars.cs).
                bool free = (isPassable(grid, nx, ny, mover) || AnimalCars.underCar(grid, nx, ny, overCar, ref gn)) && crossable(grid, cx, cy, nx, ny, mover);
                if (!free)
                {
                    // The dead get over some of what stops a body, at a cost in steps (Crossings.cs).
                    double over = crosses ? Crossings.pathCost(grid, cx, cy, nx, ny, mover) : 0;
                    if (over <= 0) continue;
                    gn = g0 + over;
                }
                int next = ny * width + nx;
                ref var there = ref sc.At(nx, ny);
                if (there.closed == gen || gn >= (there.seen == gen ? there.g : double.PositiveInfinity)) continue;
                there.seen = gen;
                there.g = gn;
                there.from = current;
                double hn = H(next);
                open.Push(next, gn + hn, hn);
            }
        }
        return null;
    }

    // --- Reachability: chunked component labels per grid and mover (a derived cache; never changes an answer) ---

    sealed class Reach
    {
        public int version;
        public int cursor;
        public int cw, ch;
        /// <summary>Per chunk, each tile's component within the chunk (-1 where it can't stand); null for a bare chunk
        /// (floor, no edges): every tile is component 0, and nobody flooded it.</summary>
        public short[]?[] local = Array.Empty<short[]?>();
        public int[] counts = Array.Empty<int>();
        public int[] offsets = Array.Empty<int>();
        public List<int>[] east = Array.Empty<List<int>>();
        public List<int>[] south = Array.Empty<List<int>>();
        public int[] baseRoot = Array.Empty<int>();
        public int count;
        public string doors = "";
        public int[] root = Array.Empty<int>();
        public Dictionary<int, int> doorIds = new();
    }

    static readonly ConditionalWeakTable<Grid, Dictionary<string, Reach>> componentCache = new();
    static readonly string[] DOOR_MOVERS = { ZOMBIE, FREE, CRAWLER, RUNNER };

    static string OpenDoors(Grid grid, string mover)
    {
        if (!DOOR_MOVERS.Contains(mover)) return "";
        return string.Join(",", grid.open) + "|" + string.Join(",", grid.edgeOpen);
    }

    static int ChunkOf(Reach r, int width, int i)
    {
        int x = i % width;
        return (i - x) / width / REACH_CHUNK * r.cw + x / REACH_CHUNK;
    }

    /// <summary>A tile's component within its chunk.</summary>
    static int Local(Reach r, int width, int i)
    {
        int x = i % width, y = (i - x) / width;
        var a = r.local[y / REACH_CHUNK * r.cw + x / REACH_CHUNK];
        return a == null ? 0 : a[(y % REACH_CHUNK) * REACH_CHUNK + x % REACH_CHUNK];
    }

    static int BaseLabel(Reach r, int width, int i)
    {
        int l = Local(r, width, i);
        return l < 0 ? -1 : r.baseRoot[r.offsets[ChunkOf(r, width, i)] + l];
    }

    sealed class Rules
    {
        public required Func<int, int, bool> pass;
        /// <summary>Whether the mover crosses an edge with something on it (its kind, never EDGE_NONE: that is open to
        /// all and the callers don't ask), between two tiles.</summary>
        public required Func<int, int, int, int, int, bool> across;
    }

    /// <summary>A tile this mover's paths may be on: one its body can stand on, or one it crosses (Crossings.cs).</summary>
    static bool Stands(Grid grid, int x, int y, string mover) => isPassable(grid, x, y, mover) || Crossings.standsOn(grid, x, y, mover);

    static Rules MakeRules(Grid grid, string mover)
    {
        bool shut = DOOR_MOVERS.Contains(mover);
        // The dead's paths go over what their bodies can't walk through (Crossings.cs): reachable all the same.
        if (Crossings.plans(mover)) return new Rules
        {
            pass = (x, y) => Stands(grid, x, y, mover) && !(shut && getTile(grid, x, y) == DOOR),
            across = (kind, ax, ay, bx, by) => !(shut && kind == EDGE_DOOR) && (crossable(grid, ax, ay, bx, by, mover) || Crossings.vaults(kind, mover)),
        };
        return new Rules
        {
            pass = (x, y) => isPassable(grid, x, y, mover) && !(shut && getTile(grid, x, y) == DOOR),
            across = (kind, ax, ay, bx, by) => !(shut && kind == EDGE_DOOR) && crossable(grid, ax, ay, bx, by, mover),
        };
    }

    static void FloodChunk(Grid grid, Reach r, int c, Rules rule, int[] stack)
    {
        // Bare land: one component, whoever walks. Not flooded, nothing allocated: most of a big world.
        if (Grids.bareChunk(grid, c))
        {
            r.local[c] = null;
            r.counts[c] = 1;
            return;
        }
        const int N = REACH_CHUNK;
        int width = grid.width, height = grid.height;
        int x0 = c % r.cw * N;
        int y0 = c / r.cw * N;
        int w = Math.Min(width, x0 + N) - x0;
        int h = Math.Min(height, y0 + N) - y0;
        // A chunk has at most 1,024 components: a short a tile (2 KB a chunk a mover).
        var local = r.local[c] ??= new short[N * N];
        // The chunk's own edges (two a tile: north, west), or none at all: a tile's neighbours within the chunk are read
        // straight from them, and an edge with nothing on it is crossed without asking.
        var edges = Grids.edgeChunk(grid, c, out int noEdges);
        for (int ly = 0; ly < h; ly++) for (int lx = 0; lx < w; lx++) local[ly * N + lx] = (short)(rule.pass(x0 + lx, y0 + ly) ? -2 : -1);
        short next = 0;
        for (int sy = 0; sy < h; sy++)
        {
            for (int sx = 0; sx < w; sx++)
            {
                int start = sy * N + sx;
                if (local[start] != -2) continue;
                local[start] = next;
                int top = 0;
                stack[top++] = start;
                while (top > 0)
                {
                    int cur = stack[--top];
                    int lx = cur % N, ly = cur / N;
                    int cx = x0 + lx, cy = y0 + ly;
                    // North: this tile's north edge. South: the next row's north edge. West: this tile's west edge. East:
                    // the next tile's west edge.
                    if (ly > 0)
                    {
                        int n = cur - N, kind = edges == null ? noEdges : edges[2 * cur];
                        if (local[n] == -2 && (kind == EDGE_NONE || rule.across(kind, cx, cy, cx, cy - 1))) { local[n] = next; stack[top++] = n; }
                    }
                    if (ly < h - 1)
                    {
                        int n = cur + N, kind = edges == null ? noEdges : edges[2 * n];
                        if (local[n] == -2 && (kind == EDGE_NONE || rule.across(kind, cx, cy, cx, cy + 1))) { local[n] = next; stack[top++] = n; }
                    }
                    if (lx > 0)
                    {
                        int n = cur - 1, kind = edges == null ? noEdges : edges[2 * cur + 1];
                        if (local[n] == -2 && (kind == EDGE_NONE || rule.across(kind, cx, cy, cx - 1, cy))) { local[n] = next; stack[top++] = n; }
                    }
                    if (lx < w - 1)
                    {
                        int n = cur + 1, kind = edges == null ? noEdges : edges[2 * n + 1];
                        if (local[n] == -2 && (kind == EDGE_NONE || rule.across(kind, cx, cy, cx + 1, cy))) { local[n] = next; stack[top++] = n; }
                    }
                }
                next++;
            }
        }
        r.counts[c] = next;
    }

    /// <summary>The links between two bare chunks: their one component each, joined (shared, never changed).</summary>
    static readonly List<int> BARE_LINK = [0, 0];
    static readonly List<int> NO_LINK = [];

    static void BorderLinks(Grid grid, Reach r, int c, Rules rule)
    {
        int width = grid.width, height = grid.height;
        int x0 = c % r.cw * REACH_CHUNK;
        int y0 = c / r.cw * REACH_CHUNK;
        int x1 = Math.Min(width, x0 + REACH_CHUNK);
        int y1 = Math.Min(height, y0 + REACH_CHUNK);
        // Two bare chunks side by side: nothing stands between them (an edge there would be the far chunk's).
        const int N = REACH_CHUNK;
        var mine = r.local[c];
        bool bare = mine == null;
        List<int> east = NO_LINK, south = NO_LINK;
        var seen = new HashSet<int>();
        if (x1 < width)
        {
            if (bare && r.local[c + 1] == null) east = BARE_LINK;
            else
            {
                east = new List<int>();
                var theirs = r.local[c + 1];
                for (int y = y0; y < y1; y++)
                {
                    int la = mine == null ? 0 : mine[(y - y0) * N + (N - 1)], lb = theirs == null ? 0 : theirs[(y - y0) * N];
                    if (la < 0 || lb < 0 || seen.Contains(la * 2048 + lb)) continue;
                    int kind = Grids.edgeOf(grid, grid.ox + x1, grid.oy + y, 1); // the far tile's west edge
                    if (kind != EDGE_NONE && !rule.across(kind, x1, y, x1 - 1, y)) continue;
                    seen.Add(la * 2048 + lb);
                    east.Add(la);
                    east.Add(lb);
                }
            }
        }
        seen.Clear();
        if (y1 < height)
        {
            if (bare && r.local[c + r.cw] == null) south = BARE_LINK;
            else
            {
                south = new List<int>();
                var theirs = r.local[c + r.cw];
                for (int x = x0; x < x1; x++)
                {
                    int la = mine == null ? 0 : mine[(N - 1) * N + (x - x0)], lb = theirs == null ? 0 : theirs[x - x0];
                    if (la < 0 || lb < 0 || seen.Contains(la * 2048 + lb)) continue;
                    int kind = Grids.edgeOf(grid, grid.ox + x, grid.oy + y1, 0); // the far tile's north edge
                    if (kind != EDGE_NONE && !rule.across(kind, x, y1, x, y1 - 1)) continue;
                    seen.Add(la * 2048 + lb);
                    south.Add(la);
                    south.Add(lb);
                }
            }
        }
        r.east[c] = east;
        r.south[c] = south;
    }

    static void JoinChunks(Reach r)
    {
        int chunks = r.cw * r.ch;
        int total = 0;
        for (int c = 0; c < chunks; c++)
        {
            r.offsets[c] = total;
            total += r.counts[c];
        }
        r.count = total;
        var parent = new int[total];
        for (int k = 0; k < total; k++) parent[k] = k;
        int Find(int k)
        {
            while (parent[k] != k)
            {
                parent[k] = parent[parent[k]];
                k = parent[k];
            }
            return k;
        }
        void Unite(int ga, int gb)
        {
            int ra = Find(ga), rb = Find(gb);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
        for (int c = 0; c < chunks; c++)
        {
            var east = r.east[c];
            var south = r.south[c];
            int o = r.offsets[c];
            for (int k = 0; k < east.Count; k += 2) Unite(o + east[k], r.offsets[c + 1] + east[k + 1]);
            for (int k = 0; k < south.Count; k += 2) Unite(o + south[k], r.offsets[c + r.cw] + south[k + 1]);
        }
        for (int k = 0; k < total; k++) parent[k] = Find(k);
        r.baseRoot = parent;
    }

    static void Join(Grid grid, Reach r, string mover)
    {
        int width = grid.width;
        var doorTiles = grid.open.Where(i => tileAt(grid, i) == DOOR && isPassable(grid, i % width, i / width, mover)).ToList();
        r.doorIds = new Dictionary<int, int>();
        for (int k = 0; k < doorTiles.Count; k++) r.doorIds[doorTiles[k]] = r.count + k;
        var root = new int[r.count + doorTiles.Count];
        for (int k = 0; k < root.Length; k++) root[k] = k;
        int Find(int k)
        {
            while (root[k] != k)
            {
                root[k] = root[root[k]];
                k = root[k];
            }
            return k;
        }
        int LabelAt(int i) => Local(r, width, i) >= 0 ? BaseLabel(r, width, i) : r.doorIds.TryGetValue(i, out int d) ? d : -1;
        void Unite(int a, int b)
        {
            if (a < 0 || b < 0) return;
            int ra = Find(a), rb = Find(b);
            if (ra != rb) root[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
        foreach (int i in doorTiles)
        {
            int x = i % width, y = i / width;
            foreach (var d in DIRS)
            {
                int nx = x + d.x, ny = y + d.y;
                if (!inBounds(grid, nx, ny) || !Stands(grid, nx, ny, mover) || !crossable(grid, x, y, nx, ny, mover)) continue;
                Unite(r.doorIds[i], LabelAt(ny * width + nx));
            }
        }
        foreach (int e in grid.edgeOpen)
        {
            if (edgeKind(grid, e) != EDGE_DOOR) continue;
            int i = e / 2;
            int x = i % width, y = i / width;
            int ox = e % 2 == 0 ? x : x - 1;
            int oy = e % 2 == 0 ? y - 1 : y;
            if (!inBounds(grid, ox, oy) || !Stands(grid, x, y, mover) || !Stands(grid, ox, oy, mover)) continue;
            Unite(LabelAt(i), LabelAt(oy * width + ox));
        }
        for (int k = 0; k < root.Length; k++) root[k] = Find(k);
        r.root = root;
    }

    static Func<int, int> Components(Grid grid, int version, string mover)
    {
        var perMover = componentCache.GetOrCreateValue(grid);
        bool doorMover = DOOR_MOVERS.Contains(mover);
        int width = grid.width, height = grid.height;
        perMover.TryGetValue(mover, out var r);
        if (r == null || r.version != version)
        {
            var changed = r != null ? reachChangesSince(grid, r.cursor) : null;
            int cursor = reachCursor(grid);
            var rule = MakeRules(grid, mover);
            var stack = new int[REACH_CHUNK * REACH_CHUNK];
            if (r == null || changed == null)
            {
                int cw = (width + REACH_CHUNK - 1) / REACH_CHUNK;
                int ch = (height + REACH_CHUNK - 1) / REACH_CHUNK;
                r = new Reach
                {
                    version = version, cursor = cursor, cw = cw, ch = ch, local = new short[]?[cw * ch], counts = new int[cw * ch], offsets = new int[cw * ch],
                    east = new List<int>[cw * ch], south = new List<int>[cw * ch],
                };
                perMover[mover] = r;
                for (int c = 0; c < cw * ch; c++) FloodChunk(grid, r, c, rule, stack);
                for (int c = 0; c < cw * ch; c++) BorderLinks(grid, r, c, rule);
            }
            else
            {
                var dirty = new HashSet<int>(changed);
                dirty.RemoveWhere(c => c < 0 || c >= r.cw * r.ch); // a change logged past the map (JS's typed arrays ignore writes out of range)
                foreach (int c in dirty) FloodChunk(grid, r, c, rule, stack);
                var relink = new HashSet<int>();
                foreach (int c in dirty)
                {
                    relink.Add(c);
                    if (c % r.cw > 0) relink.Add(c - 1);
                    if (c >= r.cw) relink.Add(c - r.cw);
                }
                foreach (int c in relink) BorderLinks(grid, r, c, rule);
                r.version = version;
                r.cursor = cursor;
            }
            JoinChunks(r);
            if (doorMover) Join(grid, r, mover);
            r.doors = OpenDoors(grid, mover);
        }
        else if (doorMover)
        {
            string doors = OpenDoors(grid, mover);
            if (r.doors != doors)
            {
                Join(grid, r, mover);
                r.doors = doors;
            }
        }
        var reach = r;
        if (!doorMover) return i => BaseLabel(reach, width, i);
        return i =>
        {
            int l = Local(reach, width, i) >= 0 ? BaseLabel(reach, width, i) : reach.doorIds.TryGetValue(i, out int d) ? d : -1;
            return l < 0 ? -1 : reach.root[l];
        };
    }

    static readonly string[] MOVERS = { SURVIVOR, FREE, ZOMBIE, BREACHER, RAIDER, CRAWLER, RUNNER };

    /// <summary>Builds every mover's reachability now (derived data only), so the first question in play doesn't pay for it.</summary>
    public static void warmReach(Grid grid, int version)
    {
        foreach (var m in MOVERS) Components(grid, version, m);
    }

    /// <summary>True if any passable goal is reachable from `from` for this mover. `version` is the state's walkabilityVersion.</summary>
    public static bool canReach(Grid grid, int version, Point from, IReadOnlyList<Point> goals, string mover = SURVIVOR)
    {
        if (!inBounds(grid, from.x, from.y)) return false;
        var label = Components(grid, version, mover);
        bool Passable(Point p) => isPassable(grid, p.x, p.y, mover);
        int width = grid.width;
        IEnumerable<Point> starts = Passable(from)
            ? new[] { from }
            : DIRS.Select(d => new Point(from.x + d.x, from.y + d.y)).Where(p => Passable(p) && crossable(grid, from.x, from.y, p.x, p.y, mover));
        var startLabels = new HashSet<int>(starts.Select(p => label(p.y * width + p.x)));
        return goals.Any(g => Passable(g) && startLabels.Contains(label(g.y * width + g.x)));
    }

    /// <summary>A search's working data for one tile: valid when its stamp is the search's (`gen`).</summary>
    struct AStarCell
    {
        public uint seen, closed;
        public int from;
        public double g;
    }

    /// <summary>A*'s per-tile data, in 32 x 32 chunks allocated where a search first goes: a search costs what it
    /// visits, whatever the map's size (it was four arrays the size of the map: 2 GB at 10,000). Kept between searches
    /// (stamped, never cleared); dropped whole when it has grown past MAX_SCRATCH_CHUNKS, at the start of a search.</summary>
    sealed class AStarScratch
    {
        public int width, height, cw;
        public uint gen;
        public int allocated;
        public AStarCell[]?[] chunks = [];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref AStarCell At(int x, int y)
        {
            int c = (y >> 5) * cw + (x >> 5);
            var cells = chunks[c] ?? Allocate(c);
            return ref cells[((y & 31) << 5) | (x & 31)];
        }

        AStarCell[] Allocate(int c)
        {
            allocated++;
            return chunks[c] = new AStarCell[REACH_CHUNK * REACH_CHUNK];
        }
    }

    /// <summary>About 100 MB of search data (24 bytes a tile).</summary>
    const int MAX_SCRATCH_CHUNKS = 4096;

    static AStarScratch? scratch;

    static AStarScratch ScratchFor(Grid grid)
    {
        if (scratch == null || scratch.width != grid.width || scratch.height != grid.height || scratch.gen >= 0xfffffff0 || scratch.allocated > MAX_SCRATCH_CHUNKS)
        {
            int cw = (grid.width + REACH_CHUNK - 1) / REACH_CHUNK, ch = (grid.height + REACH_CHUNK - 1) / REACH_CHUNK;
            scratch = new AStarScratch { width = grid.width, height = grid.height, cw = cw, gen = 0, chunks = new AStarCell[]?[cw * ch] };
        }
        scratch.gen += 1;
        return scratch;
    }

    static List<Point> Reconstruct(AStarScratch sc, int end, int width)
    {
        var path = new List<Point>();
        for (int idx = end; ; )
        {
            int x = idx % width, y = idx / width;
            int from = sc.At(x, y).from;
            if (from == -1) break;
            path.Add(new Point(x, y));
            idx = from;
        }
        path.Reverse();
        return path;
    }

    /// <summary>Binary min-heap ordered by (f, h, insertion order). Duplicates allowed; stale entries skipped via closed.</summary>
    sealed class MinHeap
    {
        struct Item
        {
            public int idx;
            public double f, h;
            public int seq;
        }

        readonly List<Item> items = new();
        int seq;
        public int Count => items.Count;

        public void Push(int idx, double f, double h)
        {
            items.Add(new Item { idx = idx, f = f, h = h, seq = seq++ });
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Less(i, parent)) break;
                Swap(i, parent);
                i = parent;
            }
        }

        public int Pop()
        {
            var top = items[0];
            var last = items[^1];
            items.RemoveAt(items.Count - 1);
            if (items.Count > 0)
            {
                items[0] = last;
                int i = 0;
                for (;;)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < items.Count && Less(l, smallest)) smallest = l;
                    if (r < items.Count && Less(r, smallest)) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
            }
            return top.idx;
        }

        bool Less(int a, int b)
        {
            var x = items[a];
            var y = items[b];
            return x.f != y.f ? x.f < y.f : x.h != y.h ? x.h < y.h : x.seq < y.seq;
        }

        void Swap(int a, int b) => (items[a], items[b]) = (items[b], items[a]);
    }

    // --- Straightening ---

    const int STRAIGHTEN_LOOKAHEAD = 24;

    static List<Point>? StraightLine(Grid grid, Point a, Point b)
    {
        var tiles = new List<Point>();
        int dx = Math.Abs(b.x - a.x), dy = Math.Abs(b.y - a.y);
        int sx = Math.Sign(b.x - a.x), sy = Math.Sign(b.y - a.y);
        int x = a.x, y = a.y;
        int err = dx - dy;
        bool Open(int px, int py) => getTile(grid, px, py) == FLOOR && isPassable(grid, px, py, SURVIVOR);
        bool Door(int px, int py) => getTile(grid, px, py) == DOOR;
        bool Bare(int ax, int ay, int bx, int by)
        {
            int e = edgeBetween(grid, ax, ay, bx, by, out int kind);
            // grid.edges[-1] is undefined in the TypeScript, which is not EDGE_NONE.
            return e >= 0 && kind == EDGE_NONE;
        }
        while (x != b.x || y != b.y)
        {
            int e2 = 2 * err;
            int nx = x, ny = y;
            if (e2 > -dy) { err -= dy; nx += sx; }
            if (e2 < dx) { err += dx; ny += sy; }
            bool diagonal = nx != x && ny != y;
            if (diagonal)
            {
                if (!Open(nx, y) || !Open(x, ny) || !Open(nx, ny) || Door(x, y)) return null;
                if (!Bare(x, y, nx, y) || !Bare(x, y, x, ny) || !Bare(nx, y, nx, ny) || !Bare(x, ny, nx, ny)) return null;
            }
            else if (!isPassable(grid, nx, ny, SURVIVOR) || !crossable(grid, x, y, nx, ny, SURVIVOR)) return null;
            if (!diagonal && (edgeKindBetween(grid, x, y, nx, ny) == EDGE_DOOR || edgeKindBetween(grid, x, y, nx, ny) == EDGE_BARRICADE) && (nx != b.x || ny != b.y)) return null;
            if (!diagonal && Door(nx, ny) && (nx != b.x || ny != b.y)) return null;
            tiles.Add(new Point(nx, ny));
            x = nx;
            y = ny;
        }
        return tiles;
    }

    /// <summary>A four-way path with its start straightened for walking: the longest first stretch (up to
    /// STRAIGHTEN_LOOKAHEAD tiles) that is one eight-way line over open floor, then the rest of the path as it was.</summary>
    public static List<Point> straightenAhead(Grid grid, Point start, IReadOnlyList<Point> path)
    {
        if (path.Count < 2) return path.ToList();
        for (int j = Math.Min(path.Count - 1, STRAIGHTEN_LOOKAHEAD - 1); j >= 1; j--)
        {
            var line = StraightLine(grid, start, path[j]);
            if (line != null)
            {
                line.AddRange(path.Skip(j + 1));
                return line;
            }
        }
        return path.ToList();
    }

    /// <summary>Nothing solid between two tiles at most a step apart (for blows and bites).</summary>
    public static bool openBetween(Grid grid, Point a, Point b)
    {
        int dx = b.x - a.x, dy = b.y - a.y;
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return true;
        bool Open(int px, int py, int qx, int qy)
        {
            int e = edgeBetween(grid, px, py, qx, qy, out int kind);
            if (e < 0) return true;
            return kind == EDGE_NONE || kind == EDGE_BARRICADE || (kind == EDGE_DOOR && edgeIsOpen(grid, e)) || (kind == EDGE_WINDOW_BROKEN && !grid.edgeBoarded.Contains(e));
        }
        if (dx == 0 || dy == 0) return Open(a.x, a.y, b.x, b.y);
        return (Open(a.x, a.y, b.x, a.y) && Open(b.x, a.y, b.x, b.y)) || (Open(a.x, a.y, a.x, b.y) && Open(a.x, b.y, b.x, b.y));
    }

    /// <summary>Does sight pass between two tiles side by side? seesThrough of the edge between them, read by tile.</summary>
    public static bool seesBetween(Grid grid, int ax, int ay, int bx, int by)
    {
        int e = edgeBetween(grid, ax, ay, bx, by, out int kind);
        if (e < 0) return true;
        if (kind == EDGE_WALL) return false;
        if (kind == EDGE_DOOR) return edgeIsOpen(grid, e);
        if (kind == EDGE_WINDOW || kind == EDGE_WINDOW_BROKEN) return !grid.edgeBoarded.Contains(e);
        return true;
    }

    /// <summary>Does sight pass this edge? Not a wall, a shut door or boards.</summary>
    public static bool seesThrough(Grid grid, int e)
    {
        if (e < 0) return true;
        int kind = edgeKind(grid, e);
        if (kind == EDGE_WALL) return false;
        if (kind == EDGE_DOOR) return edgeIsOpen(grid, e);
        if (kind == EDGE_WINDOW || kind == EDGE_WINDOW_BROKEN) return !grid.edgeBoarded.Contains(e);
        return true;
    }
}
