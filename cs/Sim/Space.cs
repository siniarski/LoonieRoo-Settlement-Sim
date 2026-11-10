// Spatial search helpers (space.ts).
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
        public bool Mark(int x, int y)
        {
            int c = (y >> 5) * cw + (x >> 5);
            var a = seen[c];
            if (a == null)
            {
                a = seen[c] = new uint[32 * 32];
                allocated++;
            }
            ref uint stamp = ref a[((y & 31) << 5) | (x & 31)];
            if (stamp == gen) return false;
            stamp = gen;
            return true;
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
            int cw = (grid.width + 31) >> 5, ch = (grid.height + 31) >> 5;
            sc = new Scratch { width = grid.width, height = grid.height, cw = cw, gen = 0, seen = new uint[]?[cw * ch], queue = sc?.queue ?? new int[1024] };
            pool[d] = sc;
        }
        return sc;
    }

    /// <summary>
    /// Nearest tile to `from` (by walking distance, BFS in N, E, S, W order) that `accept` likes. Searches
    /// through walkable tiles; `from` itself may be unwalkable. Null if none in the first `limit` tiles searched.
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
            int tail = 0;
            int start = from.y * width + from.x;
            sc.queue[tail++] = start;
            sc.Mark(from.x, from.y);
            for (int head = 0; head < tail && head < limit; head++)
            {
                int i = sc.queue[head];
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
                    if (tail == sc.queue.Length) Array.Resize(ref sc.queue, tail * 2);
                    sc.queue[tail++] = ny * width + nx;
                }
            }
            return null;
        }
        finally
        {
            depth--;
        }
    }

    public static int manhattan(Point a, Point b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    public static bool samePoint(Point a, Point b) => a.x == b.x && a.y == b.y;
}
