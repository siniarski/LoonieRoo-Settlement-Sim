// Spatial search helpers (space.ts).
namespace LoonieRoo.Sim;

public static class Space
{
    sealed class Scratch
    {
        public int size;
        public uint gen;
        public uint[] seen = Array.Empty<uint>();
        public int[] queue = Array.Empty<int>();
    }

    /// <summary>NearestTile's working arrays, by nesting depth (accept may search too), for the map size last used.</summary>
    static readonly List<Scratch?> pool = new();
    static int depth;

    static Scratch GetScratch(int d, int size)
    {
        while (pool.Count <= d) pool.Add(null);
        var sc = pool[d];
        if (sc == null || sc.size != size || sc.gen >= 0xfffffff0)
        {
            sc = new Scratch { size = size, gen = 0, seen = new uint[size], queue = new int[size] };
            pool[d] = sc;
        }
        return sc;
    }

    /// <summary>
    /// Nearest tile to `from` (by walking distance, BFS in N, E, S, W order) that `accept` likes. Searches
    /// through walkable tiles; `from` itself may be unwalkable. Null if none in the first `limit` tiles searched.
    /// </summary>
    public static Point? NearestTile(Grid grid, Point from, Func<int, int, bool> accept, double limit = double.PositiveInfinity)
    {
        if (!Grids.InBounds(grid, from.x, from.y)) return null;
        int width = grid.width;
        int size = width * grid.height;
        var sc = GetScratch(depth, size);
        depth++;
        try
        {
            uint gen = ++sc.gen;
            var seen = sc.seen;
            var queue = sc.queue;
            int tail = 0;
            int start = from.y * width + from.x;
            queue[tail++] = start;
            seen[start] = gen;
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
                    if (!Grids.IsWalkable(grid, nx, ny)) continue;
                    int n = ny * width + nx;
                    if (seen[n] == gen) continue;
                    seen[n] = gen;
                    queue[tail++] = n;
                }
            }
            return null;
        }
        finally
        {
            depth--;
        }
    }

    public static int Manhattan(Point a, Point b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    public static bool SamePoint(Point a, Point b) => a.x == b.x && a.y == b.y;
}
