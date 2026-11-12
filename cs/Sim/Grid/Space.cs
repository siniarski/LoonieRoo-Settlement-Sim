// Spatial search helpers.
namespace LoonieRoo.Sim;

public static partial class Space
{
    /// <summary>NearestTile's working data: a stamp a tile, in 32 x 32 chunks allocated where a search first goes, and
    /// the queue, grown as a search needs. It was two arrays the size of the map (800 MB at 10,000).</summary>
    sealed class Scratch
    {
        public int width, height, cw;
        public uint gen;
        public int allocated;
        public uint[]?[] seen = [];
        public int[] queue = new int[1024];

        /// <summary>Marks a tile seen; false if it already was.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool Mark(int x, int y)
        {
            int c = (y >> GridChunk.SHIFT) * cw + (x >> GridChunk.SHIFT);
            var a = seen[c] ?? Allocate(c);
            ref uint stamp = ref a[((y & GridChunk.MASK) << GridChunk.SHIFT) | (x & GridChunk.MASK)];
            if (stamp == gen) return false;
            stamp = gen;
            return true;
        }

        uint[] Allocate(int c)
        {
            allocated++;
            return seen[c] = new uint[GridChunk.SIDE * GridChunk.SIDE];
        }
    }

    /// <summary>16 MB of stamps: more than that, and they're dropped at the start of the next search.</summary>
    const int MAX_SCRATCH_CHUNKS = 4096;

    /// <summary>NearestTile's working data, by nesting depth (accept may search too), for the map size last used.</summary>
    static readonly List<Scratch?> pool = new();
    static int depth;

    static Scratch GetScratch(int d, Grid grid)
    {
        while (pool.Count <= d) pool.Add(null);
        var sc = pool[d];
        if (sc == null || sc.width != grid.width || sc.height != grid.height || sc.gen >= 0xfffffff0 || sc.allocated > MAX_SCRATCH_CHUNKS)
        {
            int cw = (grid.width + GridChunk.MASK) >> GridChunk.SHIFT, ch = (grid.height + GridChunk.MASK) >> GridChunk.SHIFT;
            sc = new Scratch { width = grid.width, height = grid.height, cw = cw, gen = 0, seen = new uint[]?[cw * ch], queue = sc?.queue ?? new int[1024] };
            pool[d] = sc;
        }
        return sc;
    }

    /// <summary>
    /// Nearest tile to `from` (by steps over walkable tiles, BFS in N, E, S, W order) that `accept` likes. The
    /// search knows tiles only: edge walls and parked cars don't stop it (the caller's `accept` says what will do).
    /// `from` itself may be unwalkable. Null if none in the first `limit` tiles searched.
    /// </summary>
    public static Point? nearestTile(Grid grid, Point from, Func<int, int, bool> accept, double limit = double.PositiveInfinity)
    {
        if (!Grids.inBounds(grid, from.x, from.y)) return null;
        int width = grid.width;
        var sc = GetScratch(depth, grid);
        depth++;
        try
        {
            ++sc.gen;
            var queue = sc.queue;
            int tail = 0;
            int start = from.y * width + from.x;
            queue[tail++] = start;
            sc.Mark(from.x, from.y);
            for (int head = 0; head < tail && head < limit; head++)
            {
                int i = queue[head];
                int x = i % width;
                int y = (i - x) / width;
                if (accept(x, y)) return head == 0 ? from : new Point(x, y);
                // North, east, south, west.
                for (int d = 0; d < 4; d++)
                {
                    int nx = d == 1 ? x + 1 : d == 3 ? x - 1 : x;
                    int ny = d == 0 ? y - 1 : d == 2 ? y + 1 : y;
                    if (!Grids.isWalkable(grid, nx, ny)) continue;
                    if (!sc.Mark(nx, ny)) continue;
                    if (tail == queue.Length)
                    {
                        Array.Resize(ref queue, tail * 2);
                        sc.queue = queue;
                    }
                    queue[tail++] = ny * width + nx;
                }
            }
            return null;
        }
        finally
        {
            depth--;
        }
    }

    /// <summary>The first tile `accept` takes in square rings round p, the nearest ring first (`from` to `to` tiles out;
    /// ring 0 is p), each ring in row order (its top row first, west to east), or null. Bounds are accept's to check.
    /// (Swim.nearestLand wants the nearest in a ring by straight line instead, and Towns steps three tiles at a time.)</summary>
    public static Point? firstInRings(Point p, int from, int to, Func<int, int, bool> accept)
    {
        for (int r = from; r <= to; r++)
            for (int y = p.y - r; y <= p.y + r; y++)
                for (int x = p.x - r; x <= p.x + r; x++)
                    if (chebyshev(p, x, y) == r && accept(x, y)) return new Point { x = x, y = y };
        return null;
    }

    public static int manhattan(Point a, Point b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    /// <summary>Tiles apart counting a diagonal step as one: max(|dx|, |dy|). "Within r" of this is a square.</summary>
    public static int chebyshev(Point a, Point b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

    /// <summary><see cref="chebyshev(Point, Point)"/> to the tile (x, y).</summary>
    public static int chebyshev(Point a, int x, int y) => Math.Max(Math.Abs(a.x - x), Math.Abs(a.y - y));

    public static bool samePoint(Point a, Point b) => a.x == b.x && a.y == b.y;
}
