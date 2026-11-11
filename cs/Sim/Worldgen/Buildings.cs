// Building plans (2026-11-10, the buildings session: docs/buildings.md). Ported from src/sim/buildings.ts at first;
// rewritten so buildings read as real places.
//
// A plan is drawn in its own coordinates with the front (the side facing the street) at the bottom (y = h - 1), then
// stamped onto the world turned to face its street, and maybe mirrored (Lots.transform). Plans are pure (a function of
// a roll), so tests check every one.
//
// A plan is drafted as rooms of floor cells (rectangles, joined into L shapes where a room needs it) with walls on
// the edges between them, never on cells: a room's floor runs to its walls (Draft). Doors and windows are edges too.
// Every cell of the plan's rectangle is a room's floor or outside (an L-shaped house leaves a corner outside); the
// rectangle is the building's footprint for the ground's levelling (Heights: flat over it, an apron round it).
//
// Kinds (Buildings*.cs): dwellings (kind "house", by style: cottage, bungalow, family house, farmhouse, cabin, trailer:
// BuildingsHouses.cs), shops, a bar or diner, a workshop, an office, a petrol station (BuildingsTrade.cs), civic
// buildings round a corridor (police, clinic, school, offices), a church, warehouses, factories, barns and sheds
// (BuildingsCivic.cs). Rooms set the floor (boards, tiles, concrete) and what's in the containers (Loot.cs).
//
// Furniture is placed by function (against walls, in runs, beds with room round them) and never where it would block
// a doorway or the way between a room's doors (Draft.put checks every placement); tall pieces stay off windows.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Buildings
{
    public const int OUTSIDE = 0;

    public const int FLOOR = 1;

    /// <summary>Containers whose look is a container (the rest are decor).</summary>
    public static bool isContainerLook(string look)
    {
        return (FurnitureMod.CONTAINER_LOOKS).includes(look);
    }

    public static double cellAt(BuildingsPlan p, int x, int y)
    {
        return x < 0 || y < 0 || x >= p.w || y >= p.h ? OUTSIDE : p.cells[y * p.w + x];
    }
    public static double cellAt(BuildingsPlan p, double x, double y) => cellAt(p, Js.I(x), Js.I(y));

    /// <summary>The room at a plan cell, -1 outside.</summary>
    public static int roomAt(BuildingsPlan p, int x, int y) => x < 0 || y < 0 || x >= p.w || y >= p.h ? -1 : p.roomOf[y * p.w + x];

    public static double between(double r, double lo, double hi)
    {
        return lo + JsMath.floor(((double)(r) * (((hi - lo) + 1))));
    }

    /// <summary>A roll's pick of n (0..n-1).</summary>
    public static int pick(double r, int n) => Math.Min(n - 1, (int)Math.Floor(r * n));

    // Directions: 0 north (towards the back, y - 1), 1 east, 2 south (towards the front), 3 west.
    public static readonly int[] DX = { 0, 1, 0, -1 };
    public static readonly int[] DY = { -1, 0, 1, 0 };

    /// <summary>Pieces too tall to stand in front of a window.</summary>
    public static readonly HashSet<string> TALL = new() { "wardrobe", "bookcase", "shelf", "icebox", "locker", "machine", "pallet" };

    /// <summary>How a room's outer walls get windows: "none", "one" (a single, small: bathrooms), "some" (one a wall,
    /// more on a long one), "many" (a pair in the middle and more along), "front" (every outer edge of the front wall
    /// but the doors: a shopfront), "high" (sparse: every fourth edge, halls and barns).</summary>
    public static readonly JsRecord<string> WINDOWS = new()
    {
        ["living"] = "many", ["kitchen"] = "some", ["bedroom"] = "some", ["bathroom"] = "one", ["utility"] = "one", ["hall"] = "none",
        ["closet"] = "none", ["garage"] = "one", ["shop"] = "front", ["store"] = "none", ["toilet"] = "one", ["office"] = "some",
        ["bar"] = "front", ["workshop"] = "high", ["lobby"] = "front", ["corridor"] = "none", ["classroom"] = "many", ["ward"] = "some",
        ["exam"] = "one", ["cells"] = "none", ["cell"] = "none", ["armoury"] = "none", ["lockers"] = "none", ["nave"] = "some", ["vestry"] = "one",
        ["hall-big"] = "high", ["barn"] = "none", ["shed"] = "none", ["cabin"] = "many", ["lean-to"] = "none", ["porch"] = "none",
    };

    /// <summary>A plan being drafted: rooms of cells, door and window edges, furniture. finish() makes the plan.</summary>
    public sealed class Draft
    {
        public int w, h;
        public string kind, style = "";
        public double sleepers;
        public int[] room;
        public string[] ground;
        public readonly List<string> kinds = new();
        public readonly List<string> grounds = new();
        /// <summary>Per cell and side (cell * 4 + direction): a door, a window.</summary>
        public bool[] door, window;
        /// <summary>Per cell: furniture on it; kept clear (beside a door).</summary>
        public bool[] taken, keep;
        public readonly List<PlanFurniture> furniture = new();
        public PlanEdge front;

        public Draft(int w, int h, string kind, double sleepers)
        {
            this.w = w; this.h = h; this.kind = kind; this.sleepers = sleepers;
            room = new int[w * h];
            Array.Fill(room, -1);
            ground = new string[w * h];
            door = new bool[w * h * 4];
            window = new bool[w * h * 4];
            taken = new bool[w * h];
            keep = new bool[w * h];
        }

        public bool inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
        public int at(int x, int y) => inside(x, y) ? room[y * w + x] : -1;

        /// <summary>A new room over a rectangle (inclusive); cells already in another room move to this one.</summary>
        public int add(string kind, string floor, int x0, int y0, int x1, int y1)
        {
            kinds.Add(kind);
            grounds.Add(floor);
            int r = kinds.Count - 1;
            extend(r, x0, y0, x1, y1);
            return r;
        }

        public void extend(int r, int x0, int y0, int x1, int y1)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
                {
                    room[y * w + x] = r;
                    ground[y * w + x] = grounds[r];
                }
        }

        /// <summary>Floors part of a room with another ground (a kitchen's tiles inside an open-plan room).</summary>
        public void floorAs(int x0, int y0, int x1, int y1, string g)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(h - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
                    if (room[y * w + x] >= 0) ground[y * w + x] = g;
        }

        /// <summary>Whether a side of a cell is a wall (another room or outside) with no door in it.</summary>
        public bool wall(int x, int y, int d) => at(x, y) >= 0 && at(x + DX[d], y + DY[d]) != at(x, y) && !door[(y * w + x) * 4 + d];

        /// <summary>A door through side d of cell (x, y); the cells either side are kept clear.</summary>
        public void putDoor(int x, int y, int d)
        {
            int nx = x + DX[d], ny = y + DY[d];
            door[(y * w + x) * 4 + d] = true;
            keep[y * w + x] = true;
            if (inside(nx, ny))
            {
                door[(ny * w + nx) * 4 + (d + 2) % 4] = true;
                if (room[ny * w + nx] >= 0) keep[ny * w + nx] = true;
            }
        }

        /// <summary>The cells of room a with a side on room b (b = -1: outside), as (x, y, side).</summary>
        public List<(int x, int y, int d)> shared(int a, int b)
        {
            var list = new List<(int, int, int)>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (room[y * w + x] != a) continue;
                    for (int d = 0; d < 4; d++)
                        if (at(x + DX[d], y + DY[d]) == b) list.Add((x, y, d));
                }
            return list;
        }

        /// <summary>A door between rooms a and b (b = -1: outside, on side `side` if given), in the middle of their shared
        /// wall or where the roll puts it, away from a wall's ends. False if they don't meet.</summary>
        public bool join(int a, int b, double roll = 0.5, int side = -1, int width = 1)
        {
            var all = shared(a, b);
            if (side >= 0) all = all.FindAll(c => c.d == side);
            if (all.Count == 0) return false;
            // Prefer spots whose neighbours along the wall share it too (not at a wall's end or a corner).
            bool along((int x, int y, int d) c, int k)
            {
                int ax = c.d % 2 == 0 ? k : 0, ay = c.d % 2 == 0 ? 0 : k;
                return all.Exists(o => o.x == c.x + ax && o.y == c.y + ay && o.d == c.d);
            }
            var good = width > 1 ? all.FindAll(c => Enumerable.Range(1, width - 1).All(k => along(c, k)))
                : all.FindAll(c => along(c, -1) && along(c, 1));
            var from = good.Count > 0 ? good : all;
            var spot = from[pick(roll, from.Count)];
            for (int k = 0; k < width; k++)
            {
                int ax = spot.d % 2 == 0 ? k : 0, ay = spot.d % 2 == 0 ? 0 : k;
                if (all.Exists(o => o.x == spot.x + ax && o.y == spot.y + ay && o.d == spot.d)) putDoor(spot.x + ax, spot.y + ay, spot.d);
            }
            return true;
        }

        /// <summary>The front door: an outer door on the front (south) side of room a.</summary>
        public bool frontDoor(int a, double roll = 0.5, int width = 1)
        {
            if (!join(a, -1, roll, 2, width)) return false;
            for (int x = 0; x < w; x++)
                for (int y = h - 1; y >= 0; y--)
                    if (room[y * w + x] == a && door[(y * w + x) * 4 + 2] && at(x, y + 1) < 0)
                    {
                        front ??= new PlanEdge { ax = x, ay = y, bx = x, by = y + 1, door = true };
                        return true;
                    }
            return true;
        }

        // --- windows ---

        /// <summary>Windows in every room's outer walls by its kind (WINDOWS), spaced along each wall, never beside a door,
        /// never at a wall's very end on a long wall. (A window too near a neighbour's wall is taken out later, on the
        /// lot: Lots.shutSides.)</summary>
        public void windows()
        {
            for (int r = 0; r < kinds.Count; r++)
            {
                string how = Js.Get(WINDOWS, kinds[r]) ?? "some";
                if (how == "none") continue;
                var runs = new List<List<(int x, int y, int d)>>();
                for (int d = 0; d < 4; d++)
                {
                    // Runs of this room's outer edges facing d, cell by cell along the wall.
                    var cells = shared(r, -1).FindAll(c => c.d == d);
                    List<(int, int, int)> run = null;
                    (int x, int y, int d) last = (-9, -9, -1);
                    foreach (var c in cells.OrderBy(c => d % 2 == 0 ? c.y * w + c.x : c.x * h + c.y))
                    {
                        bool next = d % 2 == 0 ? c.y == last.y && c.x == last.x + 1 : c.x == last.x && c.y == last.y + 1;
                        if (run == null || !next) runs.Add(run = new());
                        run.Add(c);
                        last = c;
                    }
                }
                if (how == "one")
                {
                    var longest = runs.OrderByDescending(x => x.Count).FirstOrDefault();
                    if (longest != null) glaze(longest, longest.Count / 2);
                    continue;
                }
                foreach (var run in runs)
                {
                    int n = run.Count;
                    if (how == "front")
                    {
                        // A shopfront: glass the whole front; the side walls are for shelving.
                        if (run[0].d == 2) for (int i = 0; i < n; i++) glaze(run, i);
                        continue;
                    }
                    if (how == "high") { for (int i = 2; i < n - 1; i += 4) glaze(run, i); continue; }
                    if (n <= 2) { if (how == "many" || n == 2) glaze(run, n - 1); continue; }
                    if (how == "many" && n >= 4)
                    {
                        glaze(run, n / 2 - 1);
                        glaze(run, n / 2);
                        for (int i = 1; i < n / 2 - 2; i += 3) { glaze(run, i); glaze(run, n - 1 - i); }
                        continue;
                    }
                    if (n <= 5) { glaze(run, n / 2); continue; }
                    for (int i = 1 + (n - 2) % 3 / 2; i < n - 1; i += 3) glaze(run, i);
                }
                // A room that should have windows but got none (every spot beside a door): the first spot that takes one.
                bool any = runs.Exists(run => run.Exists(c => window[(c.y * w + c.x) * 4 + c.d]));
                foreach (var run in runs)
                    for (int i = 0; i < run.Count && !any; i++) any = glaze(run, i);
            }
        }

        bool glaze(List<(int x, int y, int d)> run, int i)
        {
            if (i < 0 || i >= run.Count) return false;
            var (x, y, d) = run[i];
            int k = (y * w + x) * 4 + d;
            if (door[k]) return false;
            // Not right beside a door in the same wall.
            int ax = d % 2 == 0 ? 1 : 0, ay = d % 2 == 0 ? 0 : 1;
            foreach (int s in new[] { -1, 1 })
            {
                int nx = x + ax * s, ny = y + ay * s;
                if (inside(nx, ny) && door[(ny * w + nx) * 4 + d]) return false;
            }
            window[k] = true;
            return true;
        }

        // --- furniture ---

        public bool free(int x, int y) => at(x, y) >= 0 && !taken[y * w + x] && !keep[y * w + x];

        /// <summary>The sides of a cell that are walls (a window counts: furniture backs onto it).</summary>
        public int walls(int x, int y, bool noWindows = false)
        {
            int n = 0;
            for (int d = 0; d < 4; d++) if (wall(x, y, d) && !(noWindows && window[(y * w + x) * 4 + d])) n |= 1 << d;
            return n;
        }

        public List<(int x, int y)> cellsOf(int r)
        {
            var list = new List<(int, int)>();
            for (int i = 0; i < w * h; i++) if (room[i] == r) list.Add((i % w, i / w));
            return list;
        }

        /// <summary>Puts a piece on a cell if it's free and the room stays usable: every cell beside the room's doors
        /// still joined by free floor, and every container in the room still beside that free floor.</summary>
        public bool put(int x, int y, string look, string table)
        {
            if (!free(x, y)) return false;
            if (TALL.Contains(look))
                for (int d = 0; d < 4; d++) if (window[(y * w + x) * 4 + d]) return false;
            taken[y * w + x] = true;
            furniture.Add(new PlanFurniture { x = x, y = y, look = look, table = isContainerLook(look) ? table : null });
            if (!usable(at(x, y)))
            {
                taken[y * w + x] = false;
                furniture.RemoveAt(furniture.Count - 1);
                return false;
            }
            return true;
        }

        bool usable(int r)
        {
            var cells = cellsOf(r);
            var open = cells.FindAll(c => !taken[c.y * w + c.x]);
            if (open.Count == 0) return false;
            int si = open.FindIndex(c => keep[c.y * w + c.x]);
            var start = open[Math.Max(0, si)];
            var seen = new bool[w * h];
            var stack = new Stack<(int x, int y)>();
            stack.Push(start);
            seen[start.y * w + start.x] = true;
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (at(nx, ny) != r || taken[ny * w + nx] || seen[ny * w + nx] || wall(x, y, d)) continue;
                    seen[ny * w + nx] = true;
                    stack.Push((nx, ny));
                }
            }
            foreach (var c in cells)
            {
                int i = c.y * w + c.x;
                if (keep[i] && !taken[i] && !seen[i]) return false;
            }
            foreach (var f in furniture)
            {
                if (f.table == null || at(f.x, f.y) != r) continue;
                bool reach = false;
                for (int d = 0; d < 4 && !reach; d++)
                {
                    int nx = f.x + DX[d], ny = f.y + DY[d];
                    reach = at(nx, ny) == r && seen[ny * w + nx] && !wall(f.x, f.y, d);
                }
                if (!reach) return false;
            }
            return true;
        }

        /// <summary>Against a wall of the room: the free wall cells in reading order, starting where the roll says.</summary>
        public bool againstWall(int r, string look, string table, double roll, Func<int, int, bool> where = null)
        {
            var spots = cellsOf(r).FindAll(c => walls(c.x, c.y) != 0 && (where == null || where(c.x, c.y)));
            return tryAll(spots, roll, look, table);
        }

        /// <summary>In a corner of the room (two walls).</summary>
        public bool inCorner(int r, string look, string table, double roll)
        {
            var spots = cellsOf(r).FindAll(c => { int m = walls(c.x, c.y); return (m & 5) != 0 && (m & 10) != 0; });
            return tryAll(spots, roll, look, table) || againstWall(r, look, table, roll);
        }

        /// <summary>Away from the walls, nearest the room's middle.</summary>
        public bool inMiddle(int r, string look, string table)
        {
            var cells = cellsOf(r);
            if (cells.Count == 0) return false;
            double cx = cells.Average(c => c.x), cy = cells.Average(c => c.y);
            var spots = cells.FindAll(c => walls(c.x, c.y) == 0).OrderBy(c => (c.x - cx) * (c.x - cx) + (c.y - cy) * (c.y - cy)).ThenBy(c => c.y).ThenBy(c => c.x).ToList();
            foreach (var c in spots) if (put(c.x, c.y, look, table)) return true;
            return false;
        }

        bool tryAll(List<(int x, int y)> spots, double roll, string look, string table)
        {
            int start = spots.Count == 0 ? 0 : pick(roll, spots.Count);
            for (int i = 0; i < spots.Count; i++)
            {
                var c = spots[(start + i) % spots.Count];
                if (put(c.x, c.y, look, table)) return true;
            }
            return false;
        }

        /// <summary>A bed with its head to a wall (one wall only: not in a corner), the floor at its foot and one side
        /// clear to walk round, and a bedside table beside it. Falls back to any wall.</summary>
        public bool bed(int r, double roll, string nightstand = "nightstand", string table = "bedroom")
        {
            var spots = new List<(int x, int y, int d)>();
            foreach (var c in cellsOf(r))
            {
                int m = walls(c.x, c.y);
                for (int d = 0; d < 4; d++)
                {
                    if (m != 1 << d) continue;
                    int fx = c.x - DX[d], fy = c.y - DY[d];
                    if (!free(fx, fy) && !(at(fx, fy) == r && keep[fy * w + fx])) continue;
                    spots.Add((c.x, c.y, d));
                }
            }
            int start = spots.Count == 0 ? 0 : pick(roll, spots.Count);
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[(start + i) % spots.Count];
                if (!put(s.x, s.y, "bed", null)) continue;
                if (nightstand != null)
                {
                    int ax = s.d % 2 == 0 ? 1 : 0, ay = s.d % 2 == 0 ? 0 : 1;
                    // One side for the nightstand, the other left to walk round.
                    foreach (int k in roll < 0.5 ? new[] { 1, -1 } : new[] { -1, 1 })
                    {
                        int nx = s.x + ax * k, ny = s.y + ay * k, ox = s.x - ax * k, oy = s.y - ay * k;
                        if (at(nx, ny) == r && wall(nx, ny, s.d) && free(ox, oy) && put(nx, ny, nightstand, table)) break;
                    }
                }
                return true;
            }
            return againstWall(r, "bed", null, roll);
        }

        /// <summary>Pieces side by side along one wall (a kitchen's counters, a shop's shelves): the longest free run
        /// of wall cells facing one way, starting with the side `prefer` if it has room. Returns how many were placed.</summary>
        public int run(int r, IList<(string look, string table)> pieces, double roll, int prefer = -1)
        {
            var best = new List<(int x, int y)>();
            for (int k = 0; k < 4; k++)
            {
                int d = prefer >= 0 ? (prefer + k) % 4 : (pick(roll, 4) + k) % 4;
                var cells = cellsOf(r).FindAll(c => wall(c.x, c.y, d) && !window[(c.y * w + c.x) * 4 + d] && free(c.x, c.y));
                cells = cells.OrderBy(c => d % 2 == 0 ? c.y * w + c.x : c.x * h + c.y).ToList();
                var cur = new List<(int x, int y)>();
                foreach (var c in cells)
                {
                    if (cur.Count > 0)
                    {
                        var l = cur[^1];
                        bool next = d % 2 == 0 ? c.y == l.y && c.x == l.x + 1 : c.x == l.x && c.y == l.y + 1;
                        if (!next) cur = new();
                    }
                    cur.Add(c);
                    if (cur.Count > best.Count) best = new(cur);
                }
                if (prefer >= 0 && best.Count >= pieces.Count) break;
            }
            int placed = 0, i = 0;
            foreach (var c in best)
            {
                if (i >= pieces.Count) break;
                if (put(c.x, c.y, pieces[i].look, pieces[i].table)) { placed++; i++; }
            }
            for (; i < pieces.Count; i++) if (againstWall(r, pieces[i].look, pieces[i].table, roll + 0.37 * i - Math.Floor(roll + 0.37 * i))) placed++;
            return placed;
        }

        /// <summary>One piece facing another across the room: on the far side, in line with it (a sofa facing the
        /// television), against the wall there if there's one, else two tiles off.</summary>
        public bool facing(int r, (int x, int y) what, string look, string table)
        {
            int m = walls(what.x, what.y);
            for (int d = 0; d < 4; d++)
            {
                if ((m & (1 << d)) == 0) continue;
                // Walk away from that wall across the room.
                int x = what.x, y = what.y, steps = 0;
                while (at(x - DX[d], y - DY[d]) == r && !wall(x, y, (d + 2) % 4)) { x -= DX[d]; y -= DY[d]; steps++; }
                for (int back = 0; back <= steps - 2; back++)
                {
                    int px = x + DX[d] * back, py = y + DY[d] * back;
                    if (put(px, py, look, table)) return true;
                }
            }
            return false;
        }

        public (int x, int y) last => furniture.Count == 0 ? (-1, -1) : (furniture[^1].x, furniture[^1].y);

        /// <summary>The plan: cells, ground, edges (walls, doors, windows), furniture, rooms.</summary>
        public BuildingsPlan finish()
        {
            var p = new BuildingsPlan
            {
                w = w, h = h, kind = kind, style = style, sleepers = sleepers,
                cells = Js.Filled<double>(w * h, OUTSIDE), ground = new List<string>(ground),
                furniture = new List<PlanFurniture>(furniture), roomOf = new List<int>(room), rooms = new List<string>(kinds), front = front,
            };
            for (int i = 0; i < w * h; i++) if (room[i] >= 0) p.cells[i] = FLOOR;
            // An edge wherever a room meets another room or the outside: from the cell whose neighbour is north or west
            // for two rooms (listed once), from the room's cell for the outside.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = room[y * w + x];
                    if (a < 0) continue;
                    foreach (int d in new[] { 0, 3, 2, 1 })
                    {
                        int nx = x + DX[d], ny = y + DY[d];
                        int b = at(nx, ny);
                        if (b == a || (b >= 0 && (d == 1 || d == 2))) continue;
                        int k = (y * w + x) * 4 + d;
                        p.edges.Add(new PlanEdge { ax = x, ay = y, bx = nx, by = ny, door = door[k], window = window[k] && b < 0 ? true : null });
                    }
                }
            return p;
        }
    }

}

public static partial class Buildings
{
    /// <summary>Leaves out the windows on a plan's west (plan-left) and east sides whose outside is fewer than two tiles
    /// from the lot's edge (`left`, `right`: the lot's clear tiles beyond the plan's rectangle on each side).</summary>
    public static void shutSides(BuildingsPlan p, int left, int right)
    {
        foreach (var e in p.edges)
        {
            if (e.window != true) continue;
            if (e.bx == e.ax - 1 && e.ax + left < 2) e.window = null;
            if (e.bx == e.ax + 1 && p.w - 1 - e.ax + right < 2) e.window = null;
        }
    }
}

public static partial class Buildings
{
    /// <summary>Letters for furniture in ascii() (decor upper case where it would clash).</summary>
    public static readonly JsRecord<string> GLYPHS = new()
    {
        ["bed"] = "B", ["nightstand"] = "n", ["wardrobe"] = "W", ["dresser"] = "w", ["counter"] = "c", ["sink"] = "s", ["oven"] = "O",
        ["icebox"] = "F", ["table"] = "T", ["bin"] = "x", ["tv"] = "V", ["sofa"] = "S", ["armchair"] = "a", ["shelf"] = "h", ["bookcase"] = "k",
        ["toilet"] = "t", ["bath"] = "U", ["shower"] = "u", ["cabinet"] = "m", ["washer"] = "M", ["toolbox"] = "X", ["car"] = "C",
        ["register"] = "$", ["pallet"] = "p", ["crate"] = "r", ["desk"] = "d", ["locker"] = "l", ["cot"] = "b", ["pew"] = "=", ["altar"] = "A",
        ["hay"] = "H", ["sack"] = "q", ["tractor"] = "Y", ["machine"] = "Z", ["bench"] = "_",
    };

    /// <summary>A plan as text, two characters a cell: walls - and |, doors ~ and :, windows o,
    /// furniture by GLYPHS, empty floor '.', outside blank. Rows from the back (top) to the front (bottom).</summary>
    public static string ascii(BuildingsPlan p)
    {
        int W = 2 * p.w + 1, H = 2 * p.h + 1;
        var g = new char[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) g[y, x] = ' ';
        for (int y = 0; y < p.h; y++)
            for (int x = 0; x < p.w; x++)
                if (cellAt(p, x, y) == FLOOR) g[2 * y + 1, 2 * x + 1] = '.';
        foreach (var f in p.furniture) g[2 * f.y + 1, 2 * f.x + 1] = (Js.Get(GLYPHS, f.look) ?? "?")[0];
        foreach (var e in p.edges)
        {
            int ex = e.ax + e.bx + 1, ey = e.ay + e.by + 1;
            bool vertical = e.ay == e.by;
            g[ey, ex] = e.door ? (vertical ? ':' : '~') : e.window == true ? 'o' : vertical ? '|' : '-';
        }
        for (int y = 0; y < H; y += 2)
            for (int x = 0; x < W; x += 2)
            {
                bool n = y > 0 && g[y - 1, x] is '|' or 'o' or ':', s = y < H - 1 && g[y + 1, x] is '|' or 'o' or ':';
                bool w = x > 0 && g[y, x - 1] is '-' or 'o' or '~', e = x < W - 1 && g[y, x + 1] is '-' or 'o' or '~';
                if (n || s || w || e) g[y, x] = '+';
            }
        var sb = new System.Text.StringBuilder();
        sb.Append($"{p.kind} ({p.style}) {p.w} x {p.h}: {string.Join(", ", p.rooms)}\n");
        for (int y = 0; y < H; y++)
        {
            var row = new char[W];
            for (int x = 0; x < W; x++) row[x] = g[y, x];
            sb.Append(new string(row).TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
