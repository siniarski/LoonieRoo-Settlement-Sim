// Tiles, walls, doors (grid.ts). The grid is row-major lists of plain values, so it serialises as the
// TypeScript sim's JSON does. Walls and doors are tiles, or stand on the border between two tiles
// (edges: each tile owns its north and west edge, Edges[i * 2] and Edges[i * 2 + 1]).
using System.Runtime.CompilerServices;

namespace LoonieRoo.Sim;

/// <summary>A tile position (or any x, y pair). A class, as in the TypeScript (objects shared by reference).</summary>
public sealed class Point
{
    public int x;
    public int y;
    public Point() { }
    public Point(int x, int y) { this.x = x; this.y = y; }
    public Point Copy() => new(x, y);
    public override string ToString() => $"({x}, {y})";
}

public sealed class Grid
{
    public int width;
    public int height;
    /// <summary>Row-major: index = y * width + x. "floor", "wall", "door", "water" or "barricade".</summary>
    public List<string> tiles = new();
    /// <summary>1 where a floor tile is dirty, else 0.</summary>
    public List<int> dirt = new();
    /// <summary>Damage taken by the wall or door on each tile (breach). 0 on floor.</summary>
    public List<double> damage = new();
    /// <summary>Index into GROUNDS per tile (visual).</summary>
    public List<int> ground = new();
    /// <summary>1 where some survivor has seen the tile (fog).</summary>
    public List<int> explored = new();
    public List<int> dirtList = new();
    public List<int> damageList = new();
    public List<int> locked = new();
    public List<int> reinforced = new();
    public List<int> open = new();
    public List<int> passing = new();
    /// <summary>Per tile, its north edge then its west edge: EDGE_NONE, EDGE_WALL, EDGE_DOOR...</summary>
    public List<int> edges = new();
    public List<int> edgeOpen = new();
    public List<int> edgePassing = new();
    /// <summary>Damage on edge walls and doors, by edge index (as a string key, like the TypeScript record).</summary>
    public Dictionary<string, double> edgeDamage = new();
    public List<int> edgeLocked = new();
    public List<int> edgeBoarded = new();
}

public static class Grids
{
    public const string FLOOR = "floor", WALL = "wall", DOOR = "door", WATER = "water", BARRICADE = "barricade";

    /// <summary>Appended only: saves store the index.</summary>
    public static readonly string[] GROUNDS = { "grass", "dirt", "road", "forest", "bridge", "field", "paving", "boards", "tiles", "concrete" };

    public const int EDGE_NONE = 0;
    public const int EDGE_WALL = 1;
    public const int EDGE_DOOR = 2;
    /// <summary>Planks across a border: you and colonists climb over; the dead and raiders can't.</summary>
    public const int EDGE_BARRICADE = 3;
    /// <summary>Glass: blocks walking and bites, not sight.</summary>
    public const int EDGE_WINDOW = 4;
    public const int EDGE_WINDOW_BROKEN = 5;

    /// <summary>Side of a reachability chunk, in tiles.</summary>
    public const int REACH_CHUNK = 32;
    const int MAX_REACH_LOG = 100000;
    /// <summary>More changed tiles than this, and rooms are worked out in full.</summary>
    const int MAX_TRACKED = 2048;

    public static bool IsWindow(int kind) => kind == EDGE_WINDOW || kind == EDGE_WINDOW_BROKEN;

    public static Grid CreateGrid(int width, int height, string fill = FLOOR)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"createGrid: invalid size {width}x{height}");
        int n = width * height;
        return new Grid
        {
            width = width,
            height = height,
            tiles = Enumerable.Repeat(fill, n).ToList(),
            dirt = Enumerable.Repeat(0, n).ToList(),
            damage = Enumerable.Repeat(0.0, n).ToList(),
            ground = Enumerable.Repeat(0, n).ToList(),
            explored = Enumerable.Repeat(1, n).ToList(),
            edges = Enumerable.Repeat(EDGE_NONE, n * 2).ToList(),
        };
    }

    /// <summary>The index of a tile's north ("n") or west ("w") edge.</summary>
    public static int EdgeIndex(Grid grid, int x, int y, string side) => (y * grid.width + x) * 2 + (side == "n" ? 0 : 1);

    /// <summary>The edge between two tiles side by side, or -1 if they aren't.</summary>
    public static int EdgeBetween(Grid grid, int ax, int ay, int bx, int by)
    {
        if (!InBounds(grid, ax, ay) || !InBounds(grid, bx, by)) return -1;
        if (ax == bx && by == ay - 1) return EdgeIndex(grid, ax, ay, "n");
        if (ax == bx && by == ay + 1) return EdgeIndex(grid, bx, by, "n");
        if (ay == by && bx == ax - 1) return EdgeIndex(grid, ax, ay, "w");
        if (ay == by && bx == ax + 1) return EdgeIndex(grid, bx, by, "w");
        return -1;
    }

    /// <summary>What stands on an edge (EDGE_NONE for -1).</summary>
    public static int EdgeKind(Grid grid, int e) => e < 0 || e >= grid.edges.Count ? EDGE_NONE : grid.edges[e];

    /// <summary>Puts a wall, a door or nothing on a tile's north or west edge.</summary>
    public static bool SetEdge(Grid grid, int x, int y, string side, int kind)
    {
        if (!InBounds(grid, x, y)) return false;
        int e = EdgeIndex(grid, x, y, side);
        if (grid.edges[e] == kind) return false;
        grid.edges[e] = kind;
        NoteEdgeChange(grid, e);
        Drop(grid.edgeOpen, e); // a new door starts shut
        Drop(grid.edgePassing, e);
        grid.edgeDamage.Remove(e.ToString()); // and undamaged
        Drop(grid.edgeLocked, e); // and unlocked
        Drop(grid.edgeBoarded, e); // and unboarded
        return true;
    }

    // --- Changes, for rooms and reachability: derived data outside sim state (a loaded grid has none) ---

    static readonly ConditionalWeakTable<Grid, List<int>> changes = new();

    sealed class ReachLog
    {
        public int start;
        public List<int> chunks = new();
    }

    static readonly ConditionalWeakTable<Grid, ReachLog> reachLogs = new();

    /// <summary>A tile's wall, door, lock or edge changed.</summary>
    public static void NoteChange(Grid grid, int i)
    {
        if (changes.TryGetValue(grid, out var list) && list.Count <= MAX_TRACKED) list.Add(i);
        if (reachLogs.TryGetValue(grid, out var log))
        {
            int x = i % grid.width;
            log.chunks.Add((int)Math.Floor((double)(i - x) / grid.width / REACH_CHUNK) * (int)Math.Ceiling((double)grid.width / REACH_CHUNK) + (int)Math.Floor((double)x / REACH_CHUNK));
            if (log.chunks.Count > MAX_REACH_LOG)
            {
                log.start += log.chunks.Count;
                log.chunks = new List<int>();
            }
        }
    }

    /// <summary>Where the reachability log is now (starting it, if it wasn't).</summary>
    public static int ReachCursor(Grid grid)
    {
        if (!reachLogs.TryGetValue(grid, out var log))
        {
            log = new ReachLog();
            reachLogs.Add(grid, log);
        }
        return log.start + log.chunks.Count;
    }

    /// <summary>The chunks changed since `cursor` (repeats possible), or null if the log doesn't go back that far.</summary>
    public static List<int>? ReachChangesSince(Grid grid, int cursor)
    {
        if (!reachLogs.TryGetValue(grid, out var log) || cursor < log.start) return null;
        return log.chunks.GetRange(cursor - log.start, log.chunks.Count - (cursor - log.start));
    }

    /// <summary>An edge changed: both its tiles.</summary>
    public static void NoteEdgeChange(Grid grid, int e)
    {
        int tile = e / 2;
        NoteChange(grid, tile);
        NoteChange(grid, e % 2 == 0 ? tile - grid.width : tile - 1);
    }

    /// <summary>The tiles changed since the last call (and starts recording); null if unknown or too many.</summary>
    public static List<int>? TakeChanges(Grid grid)
    {
        changes.TryGetValue(grid, out var list);
        changes.AddOrUpdate(grid, new List<int>());
        return list != null && list.Count <= MAX_TRACKED ? list : null;
    }

    public static bool EdgeIsOpen(Grid grid, int e) => grid.edgeOpen.Contains(e);

    public static bool InBounds(Grid grid, int x, int y) => x >= 0 && y >= 0 && x < grid.width && y < grid.height;

    public static int TileIndex(Grid grid, int x, int y) => y * grid.width + x;

    /// <summary>The tile, or null when out of bounds.</summary>
    public static string? GetTile(Grid grid, int x, int y) => InBounds(grid, x, y) ? grid.tiles[TileIndex(grid, x, y)] : null;

    /// <summary>Sets a tile. True if it changed. Callers that change walkability bump walkabilityVersion.</summary>
    public static bool SetTile(Grid grid, int x, int y, string tile)
    {
        if (!InBounds(grid, x, y)) return false;
        int i = TileIndex(grid, x, y);
        if (grid.tiles[i] == tile) return false;
        grid.tiles[i] = tile;
        NoteChange(grid, i);
        if (tile != FLOOR && grid.dirt[i] == 1)
        {
            grid.dirt[i] = 0;
            Drop(grid.dirtList, i);
        }
        SetDamage(grid, i, 0); // a new (or removed) structure starts undamaged
        Drop(grid.locked, i); // and unlocked
        Drop(grid.reinforced, i); // and plain
        Drop(grid.open, i); // and shut
        Drop(grid.passing, i);
        return true;
    }

    public static bool IsDirty(Grid grid, int x, int y) => InBounds(grid, x, y) && grid.dirt[TileIndex(grid, x, y)] == 1;

    /// <summary>Marks a floor tile dirty or clean. True if it changed. Non-floor tiles can't get dirty.</summary>
    public static bool SetDirty(Grid grid, int x, int y, bool dirty)
    {
        if (GetTile(grid, x, y) != FLOOR && dirty) return false;
        if (IsDirty(grid, x, y) == dirty || !InBounds(grid, x, y)) return false;
        int i = TileIndex(grid, x, y);
        grid.dirt[i] = dirty ? 1 : 0;
        if (dirty) grid.dirtList.Add(i);
        else Drop(grid.dirtList, i);
        return true;
    }

    /// <summary>Sets the damage on a tile index, keeping damageList in step.</summary>
    public static void SetDamage(Grid grid, int i, double value)
    {
        double was = grid.damage[i];
        if (was == value) return;
        grid.damage[i] = value;
        if (was == 0) grid.damageList.Add(i);
        else if (value == 0) Drop(grid.damageList, i);
    }

    static void Drop(List<int> list, int i)
    {
        int at = list.IndexOf(i);
        if (at >= 0) list.RemoveAt(at);
    }

    /// <summary>Doors and barricades are walkable (for colonists).</summary>
    public static bool IsWalkableTile(string tile) => tile == FLOOR || tile == DOOR || tile == BARRICADE;

    public static bool IsLocked(Grid grid, int x, int y) => InBounds(grid, x, y) && grid.locked.Contains(TileIndex(grid, x, y));

    /// <summary>A door standing open.</summary>
    public static bool IsOpen(Grid grid, int x, int y) => InBounds(grid, x, y) && grid.open.Contains(TileIndex(grid, x, y));

    public static bool IsWalkable(Grid grid, int x, int y)
    {
        var tile = GetTile(grid, x, y);
        return tile != null && IsWalkableTile(tile);
    }

    /// <summary>In-bounds 4-way neighbours in a fixed order: N, E, S, W.</summary>
    public static List<Point> Neighbours4(Grid grid, int x, int y)
    {
        var out_ = new List<Point>(4);
        if (InBounds(grid, x, y - 1)) out_.Add(new Point(x, y - 1));
        if (InBounds(grid, x + 1, y)) out_.Add(new Point(x + 1, y));
        if (InBounds(grid, x, y + 1)) out_.Add(new Point(x, y + 1));
        if (InBounds(grid, x - 1, y)) out_.Add(new Point(x - 1, y));
        return out_;
    }
}
