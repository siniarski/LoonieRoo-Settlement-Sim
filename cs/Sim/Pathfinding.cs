// A* on the grid, 4-way, Manhattan heuristic; reachability labels; straightening (pathfinding.ts).
// Deterministic: ties broken by lower h, then insertion order; neighbours expanded N, E, S, W.
// Movers: "survivor" (colonists: any door), "free" (you: doors open), "zombie", "breacher" (batters
// through anything but water), "raider" (unlocked doors), "crawler" (under barricades), "vehicle".
using System.Runtime.CompilerServices;
using static LoonieRoo.Sim.Grids;

namespace LoonieRoo.Sim;

public static partial class Pathfinding
{
    public const string SURVIVOR = "survivor", FREE = "free", ZOMBIE = "zombie", BREACHER = "breacher", RAIDER = "raider", CRAWLER = "crawler", VEHICLE = "vehicle";

    public static bool isPassable(Grid grid, int x, int y, string mover = SURVIVOR)
    {
        if (mover == BREACHER) return inBounds(grid, x, y) && getTile(grid, x, y) != WATER;
        var tile = getTile(grid, x, y);
        bool openDoor = tile == DOOR && isOpen(grid, x, y);
        if (mover == CRAWLER) return tile == FLOOR || tile == BARRICADE || openDoor;
        if (mover == VEHICLE) return tile == FLOOR;
        if (mover == RAIDER) return tile == FLOOR || (tile == DOOR && !isLocked(grid, x, y));
        if (mover == SURVIVOR) return isWalkable(grid, x, y);
        if (mover == FREE) return tile == FLOOR || tile == BARRICADE || openDoor;
        return tile == FLOOR || openDoor;
    }

    /// <summary>Can this mover cross from a tile to the one beside it (edge walls, doors, barricades, windows)?</summary>
    public static bool crossable(Grid grid, int ax, int ay, int bx, int by, string mover = SURVIVOR)
    {
        int e = edgeBetween(grid, ax, ay, bx, by);
        if (e < 0) return true;
        int kind = grid.edges[e];
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

    /// <summary>Shortest path to whichever goal is nearest. Unwalkable goals are ignored.</summary>
    public static List<Point>? findPathToAny(Grid grid, Point start, IReadOnlyList<Point> goals, string mover = SURVIVOR)
    {
        if (!inBounds(grid, start.x, start.y)) return null;
        var targets = goals.Where(g => isPassable(grid, g.x, g.y, mover)).ToList();
        if (targets.Count == 0) return null;
        if (targets.Any(g => g.x == start.x && g.y == start.y)) return new List<Point>();

        int width = grid.width;
        int size = width * grid.height;
        int startIdx = start.y * width + start.x;
        var goalSet = new HashSet<int>(targets.Select(g => g.y * width + g.x));

        var sc = ScratchFor(size);
        uint gen = sc.gen;
        var seen = sc.seen;
        var gRaw = sc.g;
        var cameFrom = sc.from;
        var closed = sc.closed;
        double GOf(int i) => seen[i] == gen ? gRaw[i] : double.PositiveInfinity;
        var open = new MinHeap();

        double H(int idx)
        {
            int x = idx % width;
            int y = idx / width;
            double best = double.PositiveInfinity;
            foreach (var g in targets) best = Math.Min(best, Math.Abs(x - g.x) + Math.Abs(y - g.y));
            return best;
        }

        seen[startIdx] = gen;
        gRaw[startIdx] = 0;
        cameFrom[startIdx] = -1;
        open.Push(startIdx, H(startIdx), H(startIdx));

        while (open.Count > 0)
        {
            int current = open.Pop();
            if (goalSet.Contains(current)) return Reconstruct(cameFrom, current, width);
            if (closed[current] == gen) continue;
            closed[current] = gen;
            int cx = current % width;
            int cy = current / width;
            double g = GOf(current) + 1;
            foreach (var dir in DIRS)
            {
                int nx = cx + dir.x;
                int ny = cy + dir.y;
                if (!isPassable(grid, nx, ny, mover) || !crossable(grid, cx, cy, nx, ny, mover)) continue;
                int next = ny * width + nx;
                if (closed[next] == gen || g >= GOf(next)) continue;
                seen[next] = gen;
                gRaw[next] = g;
                cameFrom[next] = current;
                double hn = H(next);
                open.Push(next, g + hn, hn);
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
        public int[] local = Array.Empty<int>();
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
    static readonly string[] DOOR_MOVERS = { ZOMBIE, FREE, CRAWLER };

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

    static int BaseLabel(Reach r, int width, int i)
    {
        int l = r.local[i];
        return l < 0 ? -1 : r.baseRoot[r.offsets[ChunkOf(r, width, i)] + l];
    }

    sealed class Rules
    {
        public required Func<int, int, bool> pass;
        public required Func<int, int, int, int, int, bool> across;
    }

    static Rules MakeRules(Grid grid, string mover)
    {
        bool shut = DOOR_MOVERS.Contains(mover);
        return new Rules
        {
            pass = (x, y) => isPassable(grid, x, y, mover) && !(shut && grid.tiles[y * grid.width + x] == DOOR),
            across = (e, ax, ay, bx, by) =>
            {
                int kind = e >= 0 && e < grid.edges.Count ? grid.edges[e] : 0;
                if (kind == 0) return true;
                return !(shut && kind == EDGE_DOOR) && crossable(grid, ax, ay, bx, by, mover);
            },
        };
    }

    static void FloodChunk(Grid grid, Reach r, int c, Rules rule, int[] stack)
    {
        int width = grid.width, height = grid.height;
        int x0 = c % r.cw * REACH_CHUNK;
        int y0 = c / r.cw * REACH_CHUNK;
        int x1 = Math.Min(width, x0 + REACH_CHUNK);
        int y1 = Math.Min(height, y0 + REACH_CHUNK);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) r.local[y * width + x] = rule.pass(x, y) ? -2 : -1;
        int next = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int start = y * width + x;
                if (r.local[start] != -2) continue;
                r.local[start] = next;
                int top = 0;
                stack[top++] = start;
                while (top > 0)
                {
                    int cur = stack[--top];
                    int cx = cur % width;
                    int cy = (cur - cx) / width;
                    if (cy > y0)
                    {
                        int n = cur - width;
                        if (r.local[n] == -2 && rule.across(cur * 2, cx, cy, cx, cy - 1)) { r.local[n] = next; stack[top++] = n; }
                    }
                    if (cy < y1 - 1)
                    {
                        int n = cur + width;
                        if (r.local[n] == -2 && rule.across(n * 2, cx, cy, cx, cy + 1)) { r.local[n] = next; stack[top++] = n; }
                    }
                    if (cx > x0)
                    {
                        int n = cur - 1;
                        if (r.local[n] == -2 && rule.across(cur * 2 + 1, cx, cy, cx - 1, cy)) { r.local[n] = next; stack[top++] = n; }
                    }
                    if (cx < x1 - 1)
                    {
                        int n = cur + 1;
                        if (r.local[n] == -2 && rule.across(n * 2 + 1, cx, cy, cx + 1, cy)) { r.local[n] = next; stack[top++] = n; }
                    }
                }
                next++;
            }
        }
        r.counts[c] = next;
    }

    static void BorderLinks(Grid grid, Reach r, int c, Rules rule)
    {
        int width = grid.width, height = grid.height;
        int x0 = c % r.cw * REACH_CHUNK;
        int y0 = c / r.cw * REACH_CHUNK;
        int x1 = Math.Min(width, x0 + REACH_CHUNK);
        int y1 = Math.Min(height, y0 + REACH_CHUNK);
        var east = new List<int>();
        var south = new List<int>();
        var seen = new HashSet<int>();
        if (x1 < width)
        {
            for (int y = y0; y < y1; y++)
            {
                int b = y * width + x1;
                int la = r.local[b - 1], lb = r.local[b];
                if (la < 0 || lb < 0 || seen.Contains(la * 2048 + lb) || !rule.across(b * 2 + 1, x1, y, x1 - 1, y)) continue;
                seen.Add(la * 2048 + lb);
                east.Add(la);
                east.Add(lb);
            }
        }
        seen.Clear();
        if (y1 < height)
        {
            for (int x = x0; x < x1; x++)
            {
                int b = y1 * width + x;
                int la = r.local[b - width], lb = r.local[b];
                if (la < 0 || lb < 0 || seen.Contains(la * 2048 + lb) || !rule.across(b * 2, x, y1, x, y1 - 1)) continue;
                seen.Add(la * 2048 + lb);
                south.Add(la);
                south.Add(lb);
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
        var doorTiles = grid.open.Where(i => grid.tiles[i] == DOOR && isPassable(grid, i % width, i / width, mover)).ToList();
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
        int LabelAt(int i) => r.local[i] >= 0 ? BaseLabel(r, width, i) : r.doorIds.TryGetValue(i, out int d) ? d : -1;
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
                if (!inBounds(grid, nx, ny) || !isPassable(grid, nx, ny, mover) || !crossable(grid, x, y, nx, ny, mover)) continue;
                Unite(r.doorIds[i], LabelAt(ny * width + nx));
            }
        }
        foreach (int e in grid.edgeOpen)
        {
            if (grid.edges[e] != EDGE_DOOR) continue;
            int i = e / 2;
            int x = i % width, y = i / width;
            int ox = e % 2 == 0 ? x : x - 1;
            int oy = e % 2 == 0 ? y - 1 : y;
            if (!inBounds(grid, ox, oy) || !isPassable(grid, x, y, mover) || !isPassable(grid, ox, oy, mover)) continue;
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
                    version = version, cursor = cursor, cw = cw, ch = ch, local = new int[width * height], counts = new int[cw * ch], offsets = new int[cw * ch],
                    east = new List<int>[cw * ch], south = new List<int>[cw * ch],
                };
                perMover[mover] = r;
                for (int c = 0; c < cw * ch; c++) FloodChunk(grid, r, c, rule, stack);
                for (int c = 0; c < cw * ch; c++) BorderLinks(grid, r, c, rule);
            }
            else
            {
                var dirty = new HashSet<int>(changed);
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
            int l = reach.local[i] >= 0 ? BaseLabel(reach, width, i) : reach.doorIds.TryGetValue(i, out int d) ? d : -1;
            return l < 0 ? -1 : reach.root[l];
        };
    }

    static readonly string[] MOVERS = { SURVIVOR, FREE, ZOMBIE, BREACHER, RAIDER, CRAWLER };

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

    sealed class AStarScratch
    {
        public int size;
        public uint gen;
        public uint[] seen = Array.Empty<uint>();
        public double[] g = Array.Empty<double>();
        public int[] from = Array.Empty<int>();
        public uint[] closed = Array.Empty<uint>();
    }

    static AStarScratch? scratch;

    static AStarScratch ScratchFor(int size)
    {
        if (scratch == null || scratch.size != size || scratch.gen >= 0xfffffff0)
            scratch = new AStarScratch { size = size, gen = 0, seen = new uint[size], g = new double[size], from = new int[size], closed = new uint[size] };
        scratch.gen += 1;
        return scratch;
    }

    static List<Point> Reconstruct(int[] cameFrom, int end, int width)
    {
        var path = new List<Point>();
        for (int idx = end; cameFrom[idx] != -1; idx = cameFrom[idx]) path.Add(new Point(idx % width, idx / width));
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

    /// <summary>A four-way path straightened for walking (eight-way lines over open floor). Same ends as the path.</summary>
    public static List<Point> straighten(Grid grid, Point start, IReadOnlyList<Point> path)
    {
        if (path.Count < 2) return path.ToList();
        var pts = new List<Point> { start };
        pts.AddRange(path);
        var out_ = new List<Point>();
        int i = 0;
        while (i < pts.Count - 1)
        {
            int best = i + 1;
            var bestLine = new List<Point> { pts[i + 1] };
            for (int j = Math.Min(pts.Count - 1, i + STRAIGHTEN_LOOKAHEAD); j > i + 1; j--)
            {
                var line = StraightLine(grid, pts[i], pts[j]);
                if (line != null)
                {
                    best = j;
                    bestLine = line;
                    break;
                }
            }
            out_.AddRange(bestLine);
            i = best;
        }
        return out_;
    }

    static List<Point>? StraightLine(Grid grid, Point a, Point b)
    {
        var tiles = new List<Point>();
        int dx = Math.Abs(b.x - a.x), dy = Math.Abs(b.y - a.y);
        int sx = Math.Sign(b.x - a.x), sy = Math.Sign(b.y - a.y);
        int x = a.x, y = a.y;
        int err = dx - dy;
        bool Open(int px, int py) => inBounds(grid, px, py) && grid.tiles[py * grid.width + px] == FLOOR && isPassable(grid, px, py, SURVIVOR);
        bool Door(int px, int py) => grid.tiles[py * grid.width + px] == DOOR;
        bool Bare(int ax, int ay, int bx, int by)
        {
            int e = edgeBetween(grid, ax, ay, bx, by);
            // grid.edges[-1] is undefined in the TypeScript, which is not EDGE_NONE.
            return e >= 0 && grid.edges[e] == EDGE_NONE;
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
            if (!diagonal && (EdgeKindOf(grid, x, y, nx, ny) == EDGE_DOOR || EdgeKindOf(grid, x, y, nx, ny) == EDGE_BARRICADE) && (nx != b.x || ny != b.y)) return null;
            if (!diagonal && Door(nx, ny) && (nx != b.x || ny != b.y)) return null;
            tiles.Add(new Point(nx, ny));
            x = nx;
            y = ny;
        }
        return tiles;
    }

    /// <summary>Just the first straight line of Straighten, then the rest of the path as it was.</summary>
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

    static int EdgeKindOf(Grid grid, int ax, int ay, int bx, int by)
    {
        int e = edgeBetween(grid, ax, ay, bx, by);
        return e < 0 ? EDGE_NONE : grid.edges[e];
    }

    /// <summary>Nothing solid between two tiles at most a step apart (for blows and bites).</summary>
    public static bool openBetween(Grid grid, Point a, Point b)
    {
        int dx = b.x - a.x, dy = b.y - a.y;
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return true;
        bool Open(int px, int py, int qx, int qy)
        {
            int e = edgeBetween(grid, px, py, qx, qy);
            if (e < 0) return true;
            int kind = grid.edges[e];
            return kind == EDGE_NONE || kind == EDGE_BARRICADE || (kind == EDGE_DOOR && edgeIsOpen(grid, e)) || (kind == EDGE_WINDOW_BROKEN && !grid.edgeBoarded.Contains(e));
        }
        if (dx == 0 || dy == 0) return Open(a.x, a.y, b.x, b.y);
        return (Open(a.x, a.y, b.x, a.y) && Open(b.x, a.y, b.x, b.y)) || (Open(a.x, a.y, a.x, b.y) && Open(a.x, b.y, b.x, b.y));
    }

    /// <summary>Does sight pass this edge? Not a wall, a shut door or boards.</summary>
    public static bool seesThrough(Grid grid, int e)
    {
        if (e < 0) return true;
        int kind = e < grid.edges.Count ? grid.edges[e] : EDGE_NONE;
        if (kind == EDGE_WALL) return false;
        if (kind == EDGE_DOOR) return edgeIsOpen(grid, e);
        if (kind == EDGE_WINDOW || kind == EDGE_WINDOW_BROKEN) return !grid.edgeBoarded.Contains(e);
        return true;
    }

    /// <summary>A step to a tile beside it, straight or diagonal (either way round), this mover could walk.</summary>
    public static bool walksAcross(Grid grid, Point a, Point b, string mover)
    {
        if (a.x == b.x || a.y == b.y) return crossable(grid, a.x, a.y, b.x, b.y, mover);
        return (crossable(grid, a.x, a.y, b.x, a.y, mover) && crossable(grid, b.x, a.y, b.x, b.y, mover)) || (crossable(grid, a.x, a.y, a.x, b.y, mover) && crossable(grid, a.x, b.y, b.x, b.y, mover));
    }
}
