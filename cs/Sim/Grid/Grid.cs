// Tiles, walls, doors (grid.ts). The grid is row-major lists of plain values, so it serialises as the
// TypeScript sim's JSON does. Walls and doors are tiles, or stand on the border between two tiles
// (edges: each tile owns its north and west edge, Edges[i * 2] and Edges[i * 2 + 1]).
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LoonieRoo.Sim;

/// <summary>A tile position (or any x, y pair). A class, as in the TypeScript (objects shared by reference).</summary>
public sealed class Point
{
    public int x { get; set; }
    public int y { get; set; }
    public Point() { }
    public Point(int x, int y) { this.x = x; this.y = y; }
    public Point Copy() => new(x, y);
    public override string ToString() => $"({x}, {y})";
}

/// <summary>
/// A point with fractional coordinates: a road's centre line (roads.ts RoadPoint), where the smoothed curve
/// falls between tiles. TypeScript has one number type, so its Point carries these too; here they'd truncate.
/// </summary>
public sealed class PointD
{
    public double x { get; set; }
    public double y { get; set; }
    public static implicit operator PointD(Point p) => p is null ? null! : new PointD { x = p.x, y = p.y };
    public override string ToString() => $"({x}, {y})";
}

/// <summary>Things with an at (a precise position: motion.ts), a pos (the tile under them), or an id.</summary>
public interface IHasAt
{
    Point at { get; set; }
}

public interface IHasPos
{
    Point pos { get; set; }
}

/// <summary>A body (survivor, zombie, animal, dog, resident): an id, the tile under it, and where exactly it is.</summary>
public interface IBody : IHasId, IHasAt, IHasPos
{
}

public sealed class Grid : IJsonOnDeserialized
{
    public int width;
    public int height;
    /// <summary>The map tile of this grid's corner: 0 for the world's grid; a chunk's corner for the small grid a chunk
    /// is generated into (Worldgen.makeChunk), so generation writes in map coordinates. Grids' helpers (tileIndex,
    /// inBounds, edgeIndex) take it into account; nothing else meets such a grid. Not saved.</summary>
    [JsonIgnore] public int ox, oy;
    // The six per-tile layers, each in 32 x 32 chunks allocated where something differs from the layer's fill
    // (GridLayers.cs; docs/worldplan-regions.md "The second job"). Not public: Grids' accessors are the way in
    // (getTile, tileAt, groundAt, setTile...). In JSON they are the flat row-major arrays they always were.
    /// <summary>"floor", "wall", "door", "water" or "barricade" (as codes into Grids.tileNames).</summary>
    [JsonInclude, JsonPropertyName("tiles")] internal TileLayer tileLayer = new();
    /// <summary>1 where a floor tile is dirty, else 0.</summary>
    [JsonInclude, JsonPropertyName("dirt")] internal ByteLayer dirtLayer = new();
    /// <summary>Damage taken by the wall or door on each tile (breach). 0 on floor.</summary>
    [JsonInclude, JsonPropertyName("damage")] internal DoubleLayer damageLayer = new();
    /// <summary>Index into GROUNDS per tile (visual).</summary>
    [JsonInclude, JsonPropertyName("ground")] internal ByteLayer groundLayer = new();
    /// <summary>1 where some survivor has seen the tile (fog).</summary>
    [JsonInclude, JsonPropertyName("explored")] internal ByteLayer exploredLayer = new();
    public List<int> dirtList = new();
    public List<int> damageList = new();
    public List<int> locked = new();
    public List<int> reinforced = new();
    public List<int> open = new();
    public List<int> passing = new();
    /// <summary>Per tile, its north edge then its west edge: EDGE_NONE, EDGE_WALL, EDGE_DOOR...</summary>
    [JsonInclude, JsonPropertyName("edges")] internal EdgeLayer edgeLayer = new();
    public List<int> edgeOpen = new();
    public List<int> edgePassing = new();
    /// <summary>Damage on edge walls and doors, by edge index (as a string key, like the TypeScript record).</summary>
    public Dictionary<string, double> edgeDamage = new();
    public List<int> edgeLocked = new();
    public List<int> edgeBoarded = new();
    /// <summary>1 where a parked car's body is over the tile's centre (Vehicles.park): nobody on foot can stand there.
    /// Not saved: it is what state.vehicles says, laid in again when a state is read (VehicleParking.cs).</summary>
    [JsonIgnore] internal ByteLayer parkedLayer = new();
    /// <summary>How many tiles are marked in it (none: nobody need ask).</summary>
    [JsonIgnore] internal int parkedCount;

    /// <summary>Sizes the layers to the grid (what JSON gave them is cut into chunks).</summary>
    internal void Shape()
    {
        tileLayer.Shape(width, height);
        dirtLayer.Shape(width, height);
        damageLayer.Shape(width, height);
        groundLayer.Shape(width, height);
        exploredLayer.Shape(width, height);
        edgeLayer.Shape(width, height);
        parkedLayer.Shape(width, height);
        parkedCount = 0;
    }

    void IJsonOnDeserialized.OnDeserialized() => Shape();
}

public static partial class Grids
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
    /// <summary>More changed tiles than this, and rooms are worked out in full (from the tiles with something on them:
    /// Rooms.computeRooms). It was 2048 while a full pass was a flood of the whole map and an update copied it.</summary>
    const int MAX_TRACKED = 65536;

    public static bool isWindow(int kind) => kind == EDGE_WINDOW || kind == EDGE_WINDOW_BROKEN;

    public static Grid createGrid(int width, int height, string fill = FLOOR)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"createGrid: invalid size {width}x{height}");
        var grid = new Grid { width = width, height = height };
        grid.Shape();
        // Every tile `fill` and seen: no chunk is allocated until something differs.
        grid.tileLayer.Fill(tileCode(fill));
        grid.exploredLayer.Fill(1);
        return grid;
    }

    // --- Tile kinds as codes: a layer holds a byte a tile; the names are these five and whatever else a state held ---

    internal const int TILE_FLOOR = 0, TILE_WALL = 1, TILE_DOOR = 2, TILE_WATER = 3, TILE_BARRICADE = 4;

    /// <summary>A tile's kind as its code (TILE_FLOOR...), by its place in the grid's own layers (x, y from the grid's
    /// corner, on the grid): for inner loops that would otherwise compare names.</summary>
    internal static int tileCodeAt(Grid grid, int x, int y) => grid.tileLayer.Get(x, y);

    /// <summary>The tiles with something on them: not floor, or with something on their north or west edge, as tile
    /// indexes, chunk by chunk (a chunk with nothing allocated in tiles or edges is skipped whole). What rooms and
    /// reachability need to look at: the rest of the map is bare.</summary>
    internal static void structureTiles(Grid grid, List<int> into)
    {
        var tiles = grid.tileLayer;
        var edges = grid.edgeLayer;
        bool every = tiles.fill != TILE_FLOOR || edges.fill != EDGE_NONE;
        const int SIDE = GridLayer<byte>.SIDE, SHIFT = GridLayer<byte>.SHIFT;
        for (int ci = 0; ci < tiles.chunks.Length; ci++)
        {
            var t = tiles.chunks[ci];
            var e = edges.chunks[ci];
            if (t == null && e == null && !every) continue;
            int x0 = ci % tiles.cw << SHIFT, y0 = ci / tiles.cw << SHIFT;
            int w = Math.Min(SIDE, grid.width - x0), h = Math.Min(SIDE, grid.height - y0);
            for (int ly = 0; ly < h; ly++)
                for (int lx = 0; lx < w; lx++)
                {
                    int l = (ly << SHIFT) | lx;
                    int tile = t == null ? tiles.fill : t[l];
                    int edge = e == null ? edges.fill : e[2 * l] | e[2 * l + 1];
                    if (tile != TILE_FLOOR || edge != EDGE_NONE) into.Add((y0 + ly) * grid.width + x0 + lx);
                }
        }
    }

    /// <summary>Whether a chunk (cy * ceil(width / 32) + cx, as reachability numbers them) is bare land: nothing
    /// allocated in its tiles or edges, so every tile is floor with nothing on its edges.</summary>
    internal static bool bareChunk(Grid grid, int c) =>
        grid.tileLayer.chunks[c] == null && grid.edgeLayer.chunks[c] == null && grid.parkedLayer.chunks[c] == null && grid.tileLayer.fill == TILE_FLOOR && grid.edgeLayer.fill == EDGE_NONE;

    /// <summary>Is a parked car over this tile (VehicleParking.cs)?</summary>
    public static bool isParked(Grid grid, int x, int y) => grid.parkedCount > 0 && inBounds(grid, x, y) && grid.parkedLayer.Get(x - grid.ox, y - grid.oy) != 0;

    /// <summary>Mark or clear a tile (by index) as under a parked car. True if it changed (noted for rooms and reachability).</summary>
    public static bool setParked(Grid grid, int i, bool on)
    {
        if ((grid.parkedLayer.At(i) != 0) == on) return false;
        grid.parkedLayer.Put(i, on ? (byte)1 : (byte)0);
        grid.parkedCount += on ? 1 : -1;
        noteChange(grid, i);
        return true;
    }

    /// <summary>No tile is under a parked car (each one that was is noted as changed).</summary>
    public static void clearParked(Grid grid)
    {
        if (grid.parkedCount == 0) return;
        // Only the chunks that ever held a mark (a big world's layer is mostly not there).
        var layer = grid.parkedLayer;
        const int N = GridLayer<byte>.SIDE;
        for (int c = 0; c < layer.chunks.Length && grid.parkedCount > 0; c++)
        {
            var chunk = layer.chunks[c];
            if (chunk == null) continue;
            int x0 = c % layer.cw * N, y0 = c / layer.cw * N;
            for (int k = 0; k < chunk.Length; k++)
            {
                int x = x0 + k % N, y = y0 + k / N;
                if (chunk[k] != 0 && x < grid.width && y < grid.height) setParked(grid, y * grid.width + x, false);
            }
        }
    }

    /// <summary>A chunk's own edges (two a tile: north, west; a tile at (ly * 32 + lx)), or null when it holds none
    /// and every edge there is `fill`. For reachability's flood of one chunk.</summary>
    internal static byte[]? edgeChunk(Grid grid, int c, out int fill)
    {
        fill = grid.edgeLayer.fill;
        return grid.edgeLayer.chunks[c];
    }

    /// <summary>A tile code's name. Replaced (never changed in place) when a new name is met.</summary>
    internal static string[] tileNames = [FLOOR, WALL, DOOR, WATER, BARRICADE];

    /// <summary>The code of a tile's name: the five kinds, or any other string, given the next code when first met.</summary>
    internal static byte tileCode(string tile)
    {
        switch (tile)
        {
            case FLOOR: return 0;
            case WALL: return 1;
            case DOOR: return 2;
            case WATER: return 3;
            case BARRICADE: return 4;
        }
        if (tile == null) throw new ArgumentNullException(nameof(tile), "a tile is a string");
        lock (typeof(Grids))
        {
            var names = tileNames;
            int at = Array.IndexOf(names, tile);
            if (at >= 0) return (byte)at;
            if (names.Length >= 256) throw new InvalidOperationException("more than 256 kinds of tile");
            tileNames = [.. names, tile];
            return (byte)names.Length;
        }
    }

    /// <summary>A ground, edge kind or flag as the byte a layer holds: a whole number from 0 to 255, or it throws.</summary>
    internal static byte small(double value)
    {
        if (!(value >= 0 && value <= 255) || value != Math.Floor(value)) throw new InvalidDataException($"a grid layer holds whole numbers from 0 to 255, not {value}");
        return (byte)value;
    }

    /// <summary>The index of a tile's north ("n") or west ("w") edge.</summary>
    public static int edgeIndex(Grid grid, int x, int y, string side) => ((y - grid.oy) * grid.width + (x - grid.ox)) * 2 + (side == "n" ? 0 : 1);

    /// <summary>The edge between two tiles side by side, or -1 if they aren't.</summary>
    public static int edgeBetween(Grid grid, int ax, int ay, int bx, int by)
    {
        if (!inBounds(grid, ax, ay) || !inBounds(grid, bx, by)) return -1;
        if (ax == bx && by == ay - 1) return edgeIndex(grid, ax, ay, "n");
        if (ax == bx && by == ay + 1) return edgeIndex(grid, bx, by, "n");
        if (ay == by && bx == ax - 1) return edgeIndex(grid, ax, ay, "w");
        if (ay == by && bx == ax + 1) return edgeIndex(grid, bx, by, "w");
        return -1;
    }

    /// <summary>The edge between two tiles side by side and what stands on it, read by tile (no index to split):
    /// -1 and EDGE_NONE if they aren't side by side or either is off the grid.</summary>
    public static int edgeBetween(Grid grid, int ax, int ay, int bx, int by, out int kind)
    {
        kind = EDGE_NONE;
        if (!inBounds(grid, ax, ay) || !inBounds(grid, bx, by)) return -1;
        int x, y, side;
        if (ax == bx && by == ay - 1) { x = ax; y = ay; side = 0; }
        else if (ax == bx && by == ay + 1) { x = bx; y = by; side = 0; }
        else if (ay == by && bx == ax - 1) { x = ax; y = ay; side = 1; }
        else if (ay == by && bx == ax + 1) { x = bx; y = by; side = 1; }
        else return -1;
        x -= grid.ox;
        y -= grid.oy;
        kind = grid.edgeLayer.Get2(x, y, side);
        return (y * grid.width + x) * 2 + side;
    }

    /// <summary>What stands between two tiles side by side (EDGE_NONE if they aren't, or either is off the grid).</summary>
    public static int edgeKindBetween(Grid grid, int ax, int ay, int bx, int by)
    {
        edgeBetween(grid, ax, ay, bx, by, out int kind);
        return kind;
    }

    /// <summary>What stands on a tile's north (side 0) or west (side 1) edge; EDGE_NONE off the grid.</summary>
    public static int edgeAt(Grid grid, int x, int y, int side) => inBounds(grid, x, y) ? grid.edgeLayer.Get2(x - grid.ox, y - grid.oy, side) : EDGE_NONE;

    /// <summary>What stands on an edge (EDGE_NONE for -1).</summary>
    public static int edgeKind(Grid grid, int e) => (uint)e < (uint)(2 * grid.edgeLayer.count) ? grid.edgeLayer.At2(e) : EDGE_NONE;

    /// <summary>What stands on a tile's north (side 0) or west (1) edge; the tile must be on the grid.</summary>
    internal static int edgeOf(Grid grid, int x, int y, int side) => grid.edgeLayer.Get2(x - grid.ox, y - grid.oy, side);

    /// <summary>Puts a wall, a door or nothing on a tile's north or west edge.</summary>
    public static bool setEdge(Grid grid, int x, int y, string side, int kind)
    {
        if (!inBounds(grid, x, y)) return false;
        int e = edgeIndex(grid, x, y, side);
        if (grid.edgeLayer.At2(e) == kind) return false;
        grid.edgeLayer.Put2(e, small(kind));
        noteEdgeChange(grid, e);
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
    public static void noteChange(Grid grid, int i)
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
    public static int reachCursor(Grid grid)
    {
        if (!reachLogs.TryGetValue(grid, out var log))
        {
            log = new ReachLog();
            reachLogs.Add(grid, log);
        }
        return log.start + log.chunks.Count;
    }

    /// <summary>The chunks changed since `cursor` (repeats possible), or null if the log doesn't go back that far.</summary>
    public static List<int>? reachChangesSince(Grid grid, int cursor)
    {
        if (!reachLogs.TryGetValue(grid, out var log) || cursor < log.start) return null;
        return log.chunks.GetRange(cursor - log.start, log.chunks.Count - (cursor - log.start));
    }

    /// <summary>An edge changed: both its tiles.</summary>
    public static void noteEdgeChange(Grid grid, int e)
    {
        int tile = e / 2;
        noteChange(grid, tile);
        noteChange(grid, e % 2 == 0 ? tile - grid.width : tile - 1);
    }

    /// <summary>The tiles changed since the last call (and starts recording); null if unknown or too many.</summary>
    public static List<int>? takeChanges(Grid grid)
    {
        changes.TryGetValue(grid, out var list);
        changes.AddOrUpdate(grid, new List<int>());
        return list != null && list.Count <= MAX_TRACKED ? list : null;
    }

    public static bool edgeIsOpen(Grid grid, int e) => grid.edgeOpen.Contains(e);

    public static bool inBounds(Grid grid, int x, int y) => x >= grid.ox && y >= grid.oy && x < grid.ox + grid.width && y < grid.oy + grid.height;

    public static int tileIndex(Grid grid, int x, int y) => (y - grid.oy) * grid.width + (x - grid.ox);

    /// <summary>The tile, or null when out of bounds.</summary>
    public static string? getTile(Grid grid, int x, int y) => inBounds(grid, x, y) ? tileNames[grid.tileLayer.Get(x - grid.ox, y - grid.oy)] : null;

    /// <summary>Sets a tile. True if it changed. Callers that change walkability bump walkabilityVersion.</summary>
    public static bool setTile(Grid grid, int x, int y, string tile)
    {
        if (!inBounds(grid, x, y)) return false;
        int i = tileIndex(grid, x, y), lx = x - grid.ox, ly = y - grid.oy;
        byte code = tileCode(tile);
        if (grid.tileLayer.Get(lx, ly) == code) return false;
        grid.tileLayer.Set(lx, ly, code);
        noteChange(grid, i);
        if (tile != FLOOR && grid.dirtLayer.Get(lx, ly) == 1)
        {
            grid.dirtLayer.Set(lx, ly, 0);
            Drop(grid.dirtList, i);
        }
        setDamage(grid, i, 0); // a new (or removed) structure starts undamaged
        Drop(grid.locked, i); // and unlocked
        Drop(grid.reinforced, i); // and plain
        Drop(grid.open, i); // and shut
        Drop(grid.passing, i);
        return true;
    }

    public static bool isDirty(Grid grid, int x, int y) => inBounds(grid, x, y) && grid.dirtLayer.Get(x - grid.ox, y - grid.oy) == 1;

    /// <summary>Marks a floor tile dirty or clean. True if it changed. Non-floor tiles can't get dirty.</summary>
    public static bool setDirty(Grid grid, int x, int y, bool dirty)
    {
        if (getTile(grid, x, y) != FLOOR && dirty) return false;
        if (isDirty(grid, x, y) == dirty || !inBounds(grid, x, y)) return false;
        int i = tileIndex(grid, x, y);
        grid.dirtLayer.Put(i, dirty ? (byte)1 : (byte)0);
        if (dirty) grid.dirtList.Add(i);
        else Drop(grid.dirtList, i);
        return true;
    }

    /// <summary>Sets the damage on a tile index, keeping damageList in step.</summary>
    public static void setDamage(Grid grid, int i, double value)
    {
        if ((uint)i >= (uint)grid.damageLayer.count) throw new ArgumentOutOfRangeException(nameof(i), $"tile index {i} is off the grid");
        double was = grid.damageLayer.At(i);
        if (was == value) return;
        grid.damageLayer.Put(i, value);
        if (was == 0) grid.damageList.Add(i);
        else if (value == 0) Drop(grid.damageList, i);
    }

    // --- The layers, by tile index (i = y * width + x; an edge: 2 * i, + 1 for the west one) or by tile ---
    // An index off the grid reads as a JS array does past its end (no tile, 0); a write there throws.

    /// <summary>How many tiles the grid has.</summary>
    public static int tileCount(Grid grid) => grid.width * grid.height;

    /// <summary>The tile at an index, or null off the grid.</summary>
    public static string? tileAt(Grid grid, int i) => (uint)i < (uint)grid.tileLayer.count ? tileNames[grid.tileLayer.At(i)] : null;

    /// <summary>Sets a tile and nothing else (no change noted, no dirt, damage, lock or door state dropped): for
    /// generation, loading and tests. setTile is the one for play.</summary>
    public static void putTile(Grid grid, int i, string tile) => grid.tileLayer.Put(i, tileCode(tile));

    /// <summary>The ground (an index into GROUNDS) at a tile index; 0 off the grid.</summary>
    public static int groundAt(Grid grid, int i) => (uint)i < (uint)grid.groundLayer.count ? grid.groundLayer.At(i) : 0;

    /// <summary>The ground at a tile; 0 off the grid.</summary>
    public static int groundAt(Grid grid, int x, int y) => inBounds(grid, x, y) ? grid.groundLayer.Get(x - grid.ox, y - grid.oy) : 0;

    public static void setGround(Grid grid, int i, int ground) => grid.groundLayer.Put(i, small(ground));

    public static void setGround(Grid grid, int x, int y, int ground) => grid.groundLayer.Put(tileIndex(grid, x, y), small(ground));

    /// <summary>Every tile's ground set to one value.</summary>
    public static void fillGround(Grid grid, int ground) => grid.groundLayer.Fill(small(ground));

    /// <summary>1 where the tile at an index has been seen, else 0 (0 off the grid).</summary>
    public static int exploredAt(Grid grid, int i) => (uint)i < (uint)grid.exploredLayer.count ? grid.exploredLayer.At(i) : 0;

    /// <summary>Whether a tile has been seen; false off the grid.</summary>
    public static bool isExplored(Grid grid, int x, int y) => inBounds(grid, x, y) && grid.exploredLayer.Get(x - grid.ox, y - grid.oy) == 1;

    public static void setExplored(Grid grid, int i, int seen) => grid.exploredLayer.Put(i, small(seen));

    /// <summary>Every tile seen (1) or unseen (0).</summary>
    public static void fillExplored(Grid grid, int seen) => grid.exploredLayer.Fill(small(seen));

    /// <summary>Whether any tile of the grid is still unseen.</summary>
    public static bool anyUnexplored(Grid grid) => grid.exploredLayer.Has(0);

    /// <summary>1 where the tile at an index is dirty, else 0.</summary>
    public static int dirtAt(Grid grid, int i) => (uint)i < (uint)grid.dirtLayer.count ? grid.dirtLayer.At(i) : 0;

    /// <summary>Sets a tile's dirt and nothing else (dirtList is not kept in step): for tests. setDirty is the one for play.</summary>
    public static void putDirt(Grid grid, int i, int dirt) => grid.dirtLayer.Put(i, small(dirt));

    /// <summary>The damage on the wall or door at a tile index; 0 off the grid.</summary>
    public static double damageAt(Grid grid, int i) => (uint)i < (uint)grid.damageLayer.count ? grid.damageLayer.At(i) : 0;

    /// <summary>Sets an edge and nothing else (no change noted, no door state dropped): for generation, loading, breach
    /// (which notes the change itself) and tests. setEdge is the one for play.</summary>
    public static void putEdge(Grid grid, int e, int kind) => grid.edgeLayer.Put2(e, small(kind));

    /// <summary>How much of the grid is held in memory: the chunk slots allocated in each layer (of `slots` each),
    /// and their bytes with the slot arrays. For the debug panel, the harness and tests: not for the sim to act on.</summary>
    public sealed record Memory(int slots, int tiles, int ground, int explored, int dirt, int damage, int edges, long bytes);

    public static Memory memory(Grid grid)
    {
        const int n = GridLayer<byte>.SIDE * GridLayer<byte>.SIDE, header = 24;
        int t = grid.tileLayer.AllocatedCount(), g = grid.groundLayer.AllocatedCount(), x = grid.exploredLayer.AllocatedCount();
        int d = grid.dirtLayer.AllocatedCount(), m = grid.damageLayer.AllocatedCount(), e = grid.edgeLayer.AllocatedCount();
        int slots = grid.tileLayer.chunks.Length;
        long bytes = 6L * (8L * slots + header) + (long)(t + g + x + d) * (n + header) + (long)m * (8 * n + header) + (long)e * (2 * n + header);
        return new Memory(slots, t, g, x, d, m, e, bytes);
    }

    /// <summary>Whether anything stands on any edge of the grid.</summary>
    public static bool anyEdges(Grid grid) => grid.edgeLayer.AnyNot(0, 0, grid.width, grid.height, EDGE_NONE);

    /// <summary>The tile indexes where a layer holds something other than 0, ascending: from the chunks that hold
    /// anything (every chunk, if the layer's fill isn't 0).</summary>
    static List<int> NonZero<T>(GridLayer<T> layer) where T : unmanaged, IEquatable<T>
    {
        const int SIDE = GridLayer<byte>.SIDE;
        var out_ = new List<int>();
        bool every = !layer.fill.Equals(default);
        for (int ci = 0; ci < layer.chunks.Length; ci++)
        {
            var c = layer.chunks[ci];
            if (c == null && !every) continue;
            int x0 = ci % layer.cw * SIDE, y0 = ci / layer.cw * SIDE;
            int w = Math.Min(SIDE, layer.width - x0), h = Math.Min(SIDE, layer.height - y0);
            for (int ly = 0; ly < h; ly++)
                for (int lx = 0; lx < w; lx++)
                    if (!(c == null ? layer.fill : c[ly * SIDE + lx]).Equals(default)) out_.Add((y0 + ly) * layer.width + x0 + lx);
        }
        out_.Sort();
        return out_;
    }

    /// <summary>The tiles the dirt layer marks, ascending (dirtList should hold the same).</summary>
    public static List<int> dirtyTiles(Grid grid) => NonZero(grid.dirtLayer);

    /// <summary>The tiles with damage on them, ascending (damageList should hold the same).</summary>
    public static List<int> damagedTiles(Grid grid) => NonZero(grid.damageLayer);

    // Whole layers as flat lists (copies), row-major as they are in JSON: for tests, dumps and the text map. They cost
    // the whole map: never in a tick.

    public static List<string> allTiles(Grid grid)
    {
        var names = tileNames;
        var out_ = new List<string>(grid.tileLayer.count);
        foreach (byte code in grid.tileLayer.ToFlat()) out_.Add(names[code]);
        return out_;
    }

    static List<int> Ints(byte[] flat)
    {
        var out_ = new List<int>(flat.Length);
        foreach (byte v in flat) out_.Add(v);
        return out_;
    }

    public static List<int> allGround(Grid grid) => Ints(grid.groundLayer.ToFlat());
    public static List<int> allExplored(Grid grid) => Ints(grid.exploredLayer.ToFlat());
    public static List<int> allDirt(Grid grid) => Ints(grid.dirtLayer.ToFlat());
    public static List<double> allDamage(Grid grid) => grid.damageLayer.ToFlat().ToList();
    /// <summary>Two a tile: its north edge, then its west.</summary>
    public static List<int> allEdges(Grid grid) => Ints(grid.edgeLayer.ToFlat());

    static void Drop(List<int> list, int i)
    {
        int at = list.IndexOf(i);
        if (at >= 0) list.RemoveAt(at);
    }

    /// <summary>Doors and barricades are walkable (for colonists).</summary>
    public static bool isWalkableTile(string tile) => tile == FLOOR || tile == DOOR || tile == BARRICADE;

    public static bool isLocked(Grid grid, int x, int y) => inBounds(grid, x, y) && grid.locked.Contains(tileIndex(grid, x, y));

    /// <summary>A door standing open.</summary>
    public static bool isOpen(Grid grid, int x, int y) => inBounds(grid, x, y) && grid.open.Contains(tileIndex(grid, x, y));

    public static bool isWalkable(Grid grid, int x, int y)
    {
        if (!inBounds(grid, x, y)) return false;
        // isWalkableTile, by the tile's code.
        int code = grid.tileLayer.Get(x - grid.ox, y - grid.oy);
        return code == TILE_FLOOR || code == TILE_DOOR || code == TILE_BARRICADE;
    }

    /// <summary>In-bounds 4-way neighbours in a fixed order: N, E, S, W.</summary>
    public static List<Point> neighbours4(Grid grid, int x, int y)
    {
        var out_ = new List<Point>(4);
        if (inBounds(grid, x, y - 1)) out_.Add(new Point(x, y - 1));
        if (inBounds(grid, x + 1, y)) out_.Add(new Point(x + 1, y));
        if (inBounds(grid, x, y + 1)) out_.Add(new Point(x, y + 1));
        if (inBounds(grid, x - 1, y)) out_.Add(new Point(x - 1, y));
        return out_;
    }
}
