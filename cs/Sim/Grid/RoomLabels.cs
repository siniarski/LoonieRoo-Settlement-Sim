// Rooms at any map size (2026-11-10, the grid in chunks): the labels in chunks, and rooms worked out from the tiles
// that have something on them instead of by flooding the whole map.
//
// A room is a region of floor no bigger than MAX_ROOM_TILES that touches neither the map's edge nor water, so it has
// a wall, door or edge somewhere on its border. Bare land (floor, no edges) is outdoors without anyone looking at it:
//   - RoomLabels holds a label a tile in 32 x 32 chunks, allocated when a label there is first written. A tile in a
//     chunk never written reads as OUTDOORS if it is floor and NOT_FLOOR if it isn't.
//   - computeRooms seeds the same bounded flood updateRooms uses from every tile with something on it (and its four
//     neighbours): the rooms of a bare map (none) updated by "everything that isn't bare changed". Its cost follows
//     what has been built or generated, not the map's area.
//   - updateRooms takes a copy of the old labels that shares every chunk it doesn't write (the old map is left as it
//     was), so a change costs what it touches. Before, it copied a label for every tile of the map at every change:
//     400 MB at 10,000.
// computeRoomsWhole is the plain flood of the whole map, kept as the definition the tests hold the others to.
#nullable disable warnings
using System.Collections;

namespace LoonieRoo.Sim;

/// <summary>A room label a tile (a room's index, Rooms.OUTDOORS or Rooms.NOT_FLOOR), by tile index.</summary>
public sealed class RoomLabels : IEnumerable<double>
{
    const int SHIFT = 5, SIDE = 32, MASK = SIDE - 1;
    const int OUTDOORS = (int)Rooms.OUTDOORS, NOT_FLOOR = (int)Rooms.NOT_FLOOR;

    readonly Grid grid;
    readonly int width, height, cw, bw;
    /// <summary>Blocks of 32 x 32 chunks, each chunk 32 x 32 labels; null where nothing was ever written.</summary>
    readonly int[]?[]?[] blocks;
    /// <summary>The blocks and chunks this map made itself (the rest it shares with the map it was copied from).</summary>
    readonly bool[]?[] own;

    public RoomLabels(Grid grid)
    {
        this.grid = grid;
        width = grid.width;
        height = grid.height;
        cw = (width + MASK) >> SHIFT;
        bw = (cw + MASK) >> SHIFT;
        int bh = (((height + MASK) >> SHIFT) + MASK) >> SHIFT;
        blocks = new int[]?[]?[Math.Max(1, bw * bh)];
        own = new bool[]?[blocks.Length];
    }

    RoomLabels(RoomLabels from)
    {
        grid = from.grid;
        width = from.width;
        height = from.height;
        cw = from.cw;
        bw = from.bw;
        blocks = (int[]?[]?[])from.blocks.Clone();
        own = new bool[]?[blocks.Length];
    }

    /// <summary>A copy to write to: it shares every chunk with this one until it writes there.</summary>
    internal RoomLabels Copy() => new(this);

    public int length => width * height;

    /// <summary>The label at a tile index (NOT_FLOOR off the grid).</summary>
    public int at(int i)
    {
        if ((uint)i >= (uint)(width * height)) return NOT_FLOOR;
        int y = i / width;
        return at(i - y * width, y);
    }

    /// <summary>The label at a tile on the grid.</summary>
    public int at(int x, int y)
    {
        int cx = x >> SHIFT, cy = y >> SHIFT;
        var c = blocks[(cy >> SHIFT) * bw + (cx >> SHIFT)]?[((cy & MASK) << SHIFT) | (cx & MASK)];
        if (c != null) return c[((y & MASK) << SHIFT) | (x & MASK)];
        return Grids.tileCodeAt(grid, x, y) == Grids.TILE_FLOOR ? OUTDOORS : NOT_FLOOR;
    }

    public double this[int i] => at(i);

    internal void set(int i, int label)
    {
        int y = i / width, x = i - y * width, cx = x >> SHIFT, cy = y >> SHIFT;
        int b = (cy >> SHIFT) * bw + (cx >> SHIFT), k = ((cy & MASK) << SHIFT) | (cx & MASK);
        var mine = own[b];
        if (mine == null)
        {
            blocks[b] = blocks[b] is { } shared ? (int[]?[])shared.Clone() : new int[]?[SIDE * SIDE];
            mine = own[b] = new bool[SIDE * SIDE];
        }
        var chunks = blocks[b]!;
        if (!mine[k])
        {
            chunks[k] = chunks[k] is { } shared ? (int[])shared.Clone() : Fresh(cx, cy);
            mine[k] = true;
        }
        chunks[k]![((y & MASK) << SHIFT) | (x & MASK)] = label;
    }

    /// <summary>A chunk's labels as they read before anything is written: floor outdoors, the rest not floor.</summary>
    int[] Fresh(int cx, int cy)
    {
        var c = new int[SIDE * SIDE];
        Array.Fill(c, NOT_FLOOR);
        int x0 = cx << SHIFT, y0 = cy << SHIFT;
        for (int y = y0; y < Math.Min(height, y0 + SIDE); y++)
            for (int x = x0; x < Math.Min(width, x0 + SIDE); x++)
                if (Grids.tileCodeAt(grid, x, y) == Grids.TILE_FLOOR) c[((y & MASK) << SHIFT) | (x & MASK)] = OUTDOORS;
        return c;
    }

    /// <summary>Every label in tile order (the whole map: for tests and the text map).</summary>
    public IEnumerator<double> GetEnumerator()
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) yield return at(x, y);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static partial class Rooms
{
    const int UNSEEN = -3, FRESH = -4;

    /// <summary>The rooms of a grid, from nothing: the same as computeRoomsWhole, at the cost of what stands on the map.</summary>
    public static RoomMap computeRooms(Grid grid)
    {
        var changed = new List<int>();
        Grids.structureTiles(grid, changed);
        return Rework(grid, new RoomLabels(grid), [], changed);
    }

    /// <summary>Rooms after some tiles changed: the rooms touching a changed tile (or its neighbours) are taken apart, and their tiles and the changed ones flooded again; a flood may run into outdoor tiles (a new wall can close off part of the outdoors), and stops as outdoors once it's bigger than a room can be or reaches the map's edge or water. Rooms are then ordered as computeRooms orders them (by their first tile), so the result is the same as working it all out again.</summary>
    public static RoomMap updateRooms(Grid grid, RoomMap old, List<double> changed)
    {
        var tiles = new List<int>(changed.Count);
        foreach (double c in changed)
            if (c >= 0 && c < Grids.tileCount(grid)) tiles.Add((int)c);
        return Rework(grid, old.labels.Copy(), old.rooms, tiles);
    }

    static RoomMap Rework(Grid grid, RoomLabels labels, List<Room> oldRooms, List<int> changed)
    {
        int width = grid.width, height = grid.height;
        // The rooms a change touches (in the order met), and every tile to look at again.
        var removed = new HashSet<int>();
        var removedInOrder = new List<int>();
        var seeds = new List<int>(changed.Count * 5);
        void Seed(int j)
        {
            int label = labels.at(j);
            if (label >= 0 && removed.Add(label)) removedInOrder.Add(label);
            seeds.Add(j);
        }
        foreach (int c in changed)
        {
            int x = c % width, y = c / width;
            Seed(c);
            if (y > 0) Seed(c - width);
            if (x < width - 1) Seed(c + 1);
            if (y < height - 1) Seed(c + width);
            if (x > 0) Seed(c - 1);
        }
        foreach (int r in removedInOrder)
            foreach (double t in oldRooms[r].tiles) seeds.Add((int)t);
        foreach (int j in seeds)
            labels.set(j, Grids.tileCodeAt(grid, j % width, j / width) == Grids.TILE_FLOOR ? UNSEEN : (int)NOT_FLOOR);

        var fresh = new List<Room>();
        var region = new List<int>();
        var inRegion = new HashSet<int>();
        var doorSet = new HashSet<int>();
        foreach (int start in seeds)
        {
            if (labels.at(start) != UNSEEN) continue;
            region.Clear();
            inRegion.Clear();
            doorSet.Clear();
            region.Add(start);
            inRegion.Add(start);
            bool outdoors = false;
            for (int head = 0; head < region.Count; head++)
            {
                if (region.Count > MAX_ROOM_TILES)
                {
                    outdoors = true;
                    break;
                }
                int i = region[head], x = i % width, y = i / width;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) outdoors = true;
                // North, east, south, west.
                for (int d = 0; d < 4; d++)
                {
                    int nx = d == 1 ? x + 1 : d == 3 ? x - 1 : x, ny = d == 0 ? y - 1 : d == 2 ? y + 1 : y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int n = ny * width + nx;
                    Grids.edgeBetween(grid, x, y, nx, ny, out int edge);
                    if (edge == Grids.EDGE_WALL || edge == Grids.EDGE_BARRICADE || edge == Grids.EDGE_FENCE || Grids.isWindow(edge)) continue;
                    if (edge == Grids.EDGE_DOOR)
                    {
                        doorSet.Add(n);
                        continue;
                    }
                    int tile = Grids.tileCodeAt(grid, nx, ny);
                    if (tile == Grids.TILE_DOOR) doorSet.Add(n);
                    if (tile == Grids.TILE_WATER) outdoors = true;
                    if (tile != Grids.TILE_FLOOR || inRegion.Contains(n)) continue;
                    int label = labels.at(nx, ny);
                    if (label != UNSEEN && label != (int)OUTDOORS) continue;
                    inRegion.Add(n);
                    region.Add(n);
                }
            }
            if (outdoors)
            {
                foreach (int i in region) labels.set(i, (int)OUTDOORS);
                continue;
            }
            region.Sort();
            double minX = width, minY = height, maxX = 0, maxY = 0;
            var tiles = new List<double>(region.Count);
            foreach (int i in region)
            {
                labels.set(i, FRESH);
                int x = i % width, y = i / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                tiles.Add(i);
            }
            var doorTiles = doorSet.ToList();
            doorTiles.Sort();
            var doors = new List<Point>(doorTiles.Count);
            foreach (int i in doorTiles) doors.Add(new Point(i % width, i / width));
            fresh.Add(new Room { index = -1, tiles = tiles, doors = doors, minX = minX, minY = minY, maxX = maxX, maxY = maxY });
        }

        // The rooms kept and the new ones, in the order of their first tile; each room's labels are its place in it.
        var rooms = new List<Room>(oldRooms.Count + fresh.Count);
        foreach (var r in oldRooms) if (!removed.Contains((int)r.index)) rooms.Add(r);
        rooms.AddRange(fresh);
        rooms.Sort((a, b) => a.tiles[0].CompareTo(b.tiles[0]));
        var out_ = new List<Room>(rooms.Count);
        for (int index = 0; index < rooms.Count; index++)
        {
            var r = rooms[index];
            // A kept room whose place hasn't moved is the same room: its labels are already right.
            bool moved = r.index != index;
            var room = Js.Clone(r);
            room.index = index;
            out_.Add(room);
            if (!moved) continue;
            foreach (double i in room.tiles) labels.set((int)i, index);
        }
        return new RoomMap { labels = labels, rooms = out_ };
    }

    /// <summary>The definition: every region of floor flooded to its end, over the whole map. Only for tests (it costs
    /// the map's area; computeRooms and updateRooms give the same rooms and labels).</summary>
    public static RoomMap computeRoomsWhole(Grid grid)
    {
        int width = grid.width, height = grid.height;
        var labels = new int[width * height];
        for (int i = 0; i < labels.Length; i++) labels[i] = Grids.tileCodeAt(grid, i % width, i / width) == Grids.TILE_FLOOR ? UNSEEN : (int)NOT_FLOOR;
        var rooms = new List<Room>();
        for (int start = 0; start < labels.Length; start++)
        {
            if (labels[start] != UNSEEN) continue;
            var region = new List<int> { start };
            var doorSet = new HashSet<int>();
            labels[start] = FRESH;
            bool touchesEdge = false;
            for (int head = 0; head < region.Count; head++)
            {
                int i = region[head], x = i % width, y = i / width;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) touchesEdge = true;
                for (int d = 0; d < 4; d++)
                {
                    int nx = d == 1 ? x + 1 : d == 3 ? x - 1 : x, ny = d == 0 ? y - 1 : d == 2 ? y + 1 : y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int n = ny * width + nx;
                    Grids.edgeBetween(grid, x, y, nx, ny, out int edge);
                    if (edge == Grids.EDGE_WALL || edge == Grids.EDGE_BARRICADE || edge == Grids.EDGE_FENCE || Grids.isWindow(edge)) continue;
                    if (edge == Grids.EDGE_DOOR)
                    {
                        doorSet.Add(n);
                        continue;
                    }
                    int tile = Grids.tileCodeAt(grid, nx, ny);
                    if (tile == Grids.TILE_DOOR) doorSet.Add(n);
                    if (tile == Grids.TILE_WATER) touchesEdge = true;
                    if (labels[n] != UNSEEN) continue;
                    labels[n] = FRESH;
                    region.Add(n);
                }
            }
            if (touchesEdge || region.Count > MAX_ROOM_TILES)
            {
                foreach (int i in region) labels[i] = (int)OUTDOORS;
                continue;
            }
            int index = rooms.Count;
            double minX = width, minY = height, maxX = 0, maxY = 0;
            region.Sort();
            var tiles = new List<double>(region.Count);
            foreach (int i in region)
            {
                labels[i] = index;
                int x = i % width, y = i / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                tiles.Add(i);
            }
            var doorTiles = doorSet.ToList();
            doorTiles.Sort();
            var doors = new List<Point>(doorTiles.Count);
            foreach (int i in doorTiles) doors.Add(new Point(i % width, i / width));
            rooms.Add(new Room { index = index, tiles = tiles, doors = doors, minX = minX, minY = minY, maxX = maxX, maxY = maxY });
        }
        var chunked = new RoomLabels(grid);
        for (int i = 0; i < labels.Length; i++)
            if (chunked.at(i) != labels[i]) chunked.set(i, labels[i]);
        return new RoomMap { labels = chunked, rooms = rooms };
    }
}
