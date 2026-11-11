// World lighting (2026-11-10): what gives light, what is lit, and the switches. Pure, read by the sim and the renderer.
//
//   rooms       every room of a generated building (a plan's room: Buildings.toEdges, turned onto the map), and every
//               room you wall in yourself (Rooms), has a ceiling light behind a switch. It lights the whole room, and
//               only that room, while it's switched on and powered: the mains (TimelineMod.powerOn) or a generator in
//               reach (Power.isPowered at the room's anchor tile). Default: a quarter of homes (LEFT_ON, by a hash of
//               the lot and building) had one room's light left on, the room picked by a hash; every other switch is
//               off. `state.lightSwitches` keeps only the switches that differ from that default, keyed by the room's
//               anchor tile "x,y" (a plan room's first tile; a walled-in room's lowest tile index).
//   the switch  command lightSwitch {x, y} (a tile of the room) for the survivor you control, standing in the room
//               or just outside one of its doors (Commands; in the command log, so replays have it).
//   porches     a light by the front door of each house, shop and civic building (Buildings.WINDOWED kinds), on at
//               night while the mains are on; PORCH_WORKING of them work (a hash).
//   streets     lamps along town streets every STREET_SPACING tiles (a hash shifts them along and picks the side),
//               on at night while the mains are on; STREET_WORKING of them work.
//   lamps       the lamps you build (Power): at night, powered by a generator.
//   headlights  a car's lights (Vehicle.lights, command headlights for its driver): lit while the tank has fuel and
//               the car isn't wrecked (the sim has no battery: fuel stands in for a running engine; they burn none).
//   flashlight  an item (kind "flashlight"): switched on (Item.lit, command flashlight or using it from the bag) and
//               in the bag of the survivor you control, it throws a beam the way they aim. No batteries: the sim
//               has none, so it never runs flat. Found in some containers you search (FLASHLIGHT_FIND, by a hash).
//   fire        a built stove while someone cooks at it, a heater (brazier) while it burns (Power.updateHeaters' cold
//               nights), and every burning tile (state.fires).
// What is lit: by day, everything. At night a point is lit by a source within its radius (beams: within the cone)
// in the same space: the same room for anything indoors, the open air with a clear sight line for anything outdoors,
// so light never goes through a wall. `level` is the strongest source there (strength x (1 - d / 2r)); a point is
// lit at LIT or more. near() lists the sources lit now round a point, nearest first, at most MAX_NEAR, from the
// lots and streets of the plan chunks in reach and the sparse lists in state: its cost follows the radius, not the
// map. Effects (unchanged from the mains light it replaces): Sight (night sight is day sight in a lit room), guards
// hit harder in light (Power.inLight), turrets see what's lit, and the night's arrivals come in the dark.
#nullable disable warnings
using System.Runtime.CompilerServices;
namespace LoonieRoo.Sim;

public static partial class Lights
{
    public const string ROOM = "room", PORCH = "porch", STREET = "street", LAMP = "lamp", HEADLIGHTS = "headlights",
        FLASHLIGHT = "flashlight", STOVE = "stove", HEATER = "heater", FIRE = "fire";

    /// <summary>A point is lit at this level or more.</summary>
    public const double LIT = 0.25;
    /// <summary>The longest reach of any source (headlights): near() looks this much further than asked.</summary>
    public const int REACH = 14;
    /// <summary>near() lists at most this many sources.</summary>
    public const int MAX_NEAR = 64;
    /// <summary>A home's chance of having one room's light left on.</summary>
    public const double LEFT_ON = 0.25;
    public const int STREET_SPACING = 12;
    public const double STREET_WORKING = 0.8;
    public const double PORCH_WORKING = 0.85;
    /// <summary>The kinds of building with a light over the front door (the lived-in and the public ones).</summary>
    public static readonly List<string> PORCH_KINDS = ["house", "grocery", "pharmacy", "hardware", "bar", "diner", "station", "office", "police", "clinic", "school", "church"];
    /// <summary>Chance a container you search (of FLASHLIGHT_TABLES) holds a flashlight too.</summary>
    public const double FLASHLIGHT_FIND = 0.12;
    public static readonly List<string> FLASHLIGHT_TABLES = ["kitchen", "garage", "hardware", "police", "station", "shed", "barn", "warehouse", "factory"];

    /// <summary>Radius in tiles and strength (0..1) by kind; beams' cone as the cosine of the half-angle.</summary>
    public static readonly JsRecord<double> RADIUS = new() { [PORCH] = 3.5, [STREET] = 6, [LAMP] = Power.LAMP_LIGHT, [HEADLIGHTS] = 14, [FLASHLIGHT] = 9, [STOVE] = 2.5, [HEATER] = Power.HEAT_RADIUS, [FIRE] = 3.5 };
    public static readonly JsRecord<double> STRENGTH = new() { [ROOM] = 1, [PORCH] = 0.7, [STREET] = 0.9, [LAMP] = 1, [HEADLIGHTS] = 1, [FLASHLIGHT] = 0.9, [STOVE] = 0.6, [HEATER] = 0.8, [FIRE] = 1 };
    public const double HEADLIGHT_CONE = 0.87, FLASHLIGHT_CONE = 0.94;

    /// <summary>A light lit now: where (tile units, the centre of a tile is +0.5), how far, how strong, its beam, and the
    /// space it lights ("out", or a room's key). Rooms carry their bounds. Derived each time, never saved.</summary>
    public sealed class Source
    {
        public string kind = "";
        public string id = "";
        public double x, y, radius, strength;
        /// <summary>A beam's direction (unit), or 0, 0.</summary>
        public double dx, dy, cone;
        public string space = OUT;
        public int x0, y0, x1, y1;
        /// <summary>A room's anchor tile (its switch's key).</summary>
        public int ax, ay;
        public double dist;
    }

    /// <summary>How lit a point is and by what (null: daylight or nothing).</summary>
    public sealed class Lit
    {
        public double level;
        public Source? by;
        public bool day;
    }

    public const string OUT = "out";

    // --- rooms ---

    /// <summary>A room with a light: its key (the anchor "x,y"), tiles' bounds and centre, and its default.</summary>
    public sealed class RoomLight
    {
        public string key = "";
        public int ax, ay, x0, y0, x1, y1;
        public double cx, cy;
        public bool generated, defaultOn;
        public string building = "";
    }

    sealed class PlanRooms
    {
        public readonly Dictionary<long, int> at = new();
        public readonly List<RoomLight> rooms = new();
        /// <summary>The front door's outside tile, and whether its light works (-1: no porch light).</summary>
        public int px = -1, py = -1;
        public bool porch;
    }

    /// <summary>Each building's rooms, kept while its placement is (looked up only, never walked: rule 9).</summary>
    static readonly ConditionalWeakTable<Placement, PlanRooms> planCache = new();

    static long Key(int x, int y) => ((long)y << 32) | (uint)x;
    public static string keyOf(int x, int y) => $"{Js.Str(x)},{Js.Str(y)}";

    static PlanRooms planRooms(double seed, Lot lot, Placement p, int bi)
    {
        if (planCache.TryGetValue(p, out var hit)) return hit;
        var made = makePlanRooms(seed, lot, p, bi);
        planCache.AddOrUpdate(p, made);
        return made;
    }

    static PlanRooms makePlanRooms(double seed, Lot lot, Placement p, int bi)
    {
        var plan = p.plan;
        int w = plan.w, h = plan.h;
        var blocked = new HashSet<long>();
        foreach (var e in plan.edges)
        {
            blocked.Add((long)(e.ay * w + e.ax) << 32 | (uint)(e.by * w + e.bx));
            blocked.Add((long)(e.by * w + e.bx) << 32 | (uint)(e.ay * w + e.ax));
        }
        bool inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && Buildings.cellAt(plan, x, y) != Buildings.OUTSIDE;
        var room = new int[w * h];
        Array.Fill(room, -1);
        var @out = new PlanRooms();
        var cells = new List<List<int>>();
        for (int i = 0; i < w * h; i++)
        {
            if (room[i] != -1 || !inside(i % w, i / w)) continue;
            int r = cells.Count;
            var list = new List<int> { i };
            room[i] = r;
            for (int q = 0; q < list.Count; q++)
            {
                int j = list[q], x = j % w, y = j / w;
                foreach (var (dx, dy) in DIR4)
                {
                    int nx = x + dx, ny = y + dy, n = ny * w + nx;
                    if (!inside(nx, ny) || room[n] != -1 || blocked.Contains((long)j << 32 | (uint)n)) continue;
                    room[n] = r;
                    list.Add(n);
                }
            }
            cells.Add(list);
        }
        foreach (var list in cells)
        {
            var rl = new RoomLight { generated = true, building = plan.kind, x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue };
            double sx = 0, sy = 0;
            bool first = true;
            foreach (int i in list)
            {
                var t = Lots.transform(plan, i % w, i / w, p.facing, p.mirror);
                int mx = p.ox + t.x, my = p.oy + t.y;
                if (first || my < rl.ay || (my == rl.ay && mx < rl.ax)) { rl.ax = mx; rl.ay = my; first = false; }
                rl.x0 = Math.Min(rl.x0, mx); rl.y0 = Math.Min(rl.y0, my); rl.x1 = Math.Max(rl.x1, mx); rl.y1 = Math.Max(rl.y1, my);
                sx += mx + 0.5; sy += my + 0.5;
                @out.at[Key(mx, my)] = @out.rooms.Count;
            }
            rl.cx = sx / list.Count;
            rl.cy = sy / list.Count;
            rl.key = keyOf(rl.ax, rl.ay);
            @out.rooms.Add(rl);
        }
        if (plan.kind == "house" && @out.rooms.Count > 0 && Hash.hashRoll(seed, lot.id, bi, 0x11a7) < LEFT_ON)
            @out.rooms[Math.Min(@out.rooms.Count - 1, (int)Math.Floor(Hash.hashRoll(seed, lot.id, bi, 0x11a8) * @out.rooms.Count))].defaultOn = true;
        // The front door: the outer door furthest to the front (the plan's bottom), its outside cell.
        if (PORCH_KINDS.includes(plan.kind))
        {
            int best = -1;
            foreach (var e in plan.edges)
            {
                if (!e.door) continue;
                bool aIn = inside(e.ax, e.ay), bIn = inside(e.bx, e.by);
                if (aIn == bIn) continue;
                var (ox, oy) = aIn ? (e.bx, e.by) : (e.ax, e.ay);
                if (oy <= best) continue;
                best = oy;
                var t = Lots.transform(plan, ox, oy, p.facing, p.mirror);
                @out.px = p.ox + t.x;
                @out.py = p.oy + t.y;
            }
            @out.porch = best >= 0 && Hash.hashRoll(seed, lot.id, bi, 0x9071) < PORCH_WORKING;
        }
        return @out;
    }

    static readonly (int, int)[] DIR4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    static WorldRegions? regionsOf(SimState state) =>
        state.world == null ? null : Worldplan.regions(state.world.seed, state.world.size, state.settings.map);

    /// <summary>The rooms of building `bi` on a lot (tests and tools).</summary>
    public static List<RoomLight> buildingRooms(SimState state, Lot lot, int bi) =>
        planRooms(state.world.seed, lot, Lots.layoutLot(state.world.seed, lot).buildings[bi], bi).rooms;

    /// <summary>The generated building's room at a tile, or null.</summary>
    public static RoomLight? planRoomAt(SimState state, int x, int y)
    {
        var regions = regionsOf(state);
        if (regions == null || x < 0 || y < 0 || x >= state.world.size || y >= state.world.size) return null;
        var lot = regions.LotAt(x, y);
        if (lot == null) return null;
        var layout = Lots.layoutLot(state.world.seed, lot);
        for (int bi = 0; bi < layout.buildings.Count; bi++)
        {
            var pr = planRooms(state.world.seed, lot, layout.buildings[bi], bi);
            if (pr.at.TryGetValue(Key(x, y), out var r)) return pr.rooms[r];
        }
        return null;
    }

    /// <summary>The room with a light at a tile: a generated building's, else one walled in (Rooms), else null (outdoors).</summary>
    public static RoomLight? roomAt(SimState state, int x, int y)
    {
        var plan = planRoomAt(state, x, y);
        if (plan != null) return plan;
        var room = Rooms.roomAt(state, x, y);
        if (room == null || room.tiles.length == 0) return null;
        int w = state.grid.width, anchor = (int)room.tiles[0];
        return new RoomLight { key = keyOf(anchor % w, anchor / w), ax = anchor % w, ay = anchor / w, x0 = (int)room.minX, y0 = (int)room.minY, x1 = (int)room.maxX, y1 = (int)room.maxY, cx = (room.minX + room.maxX + 1) / 2, cy = (room.minY + room.maxY + 1) / 2 };
    }
    public static RoomLight? roomAt(SimState state, double x, double y) => roomAt(state, Js.I(x), Js.I(y));

    /// <summary>The space a tile is in: its room's key, or OUT.</summary>
    public static string spaceOf(SimState state, int x, int y) => roomAt(state, x, y)?.key ?? OUT;

    /// <summary>Is this room's switch on (its default unless switched)?</summary>
    public static bool switchedOn(SimState state, RoomLight r)
    {
        var s = state.lightSwitches;
        return s != null && s.TryGetValue(r.key, out var on) ? on : r.defaultOn;
    }

    /// <summary>Has this room power: the mains, or a generator in reach of its anchor?</summary>
    public static bool roomPowered(SimState state, RoomLight r) =>
        TimelineMod.powerOn(state) || Power.isPowered(state, new Point { x = r.ax, y = r.ay });

    /// <summary>At night, is the point in a room whose light is on (what the mains' light used to be: Sight)?</summary>
    public static bool roomLit(SimState state, Point p)
    {
        if (!Time.isNight(state.tick)) return false;
        var r = roomAt(state, p.x, p.y);
        return r != null && switchedOn(state, r) && roomPowered(state, r);
    }

    /// <summary>Flips the switch of the room at (x, y), for survivor s in it or at its door. Null if done, else why not.</summary>
    public static string? toggleSwitch(SimState state, Survivor s, int x, int y)
    {
        var r = roomAt(state, x, y);
        if (r == null) return "no light switch there";
        if (!reachesSwitch(state, s.pos, r)) return "not in that room";
        bool on = !switchedOn(state, r);
        state.lightSwitches ??= new JsRecord<bool>();
        if (on == r.defaultOn) state.lightSwitches.Remove(r.key);
        else state.lightSwitches[r.key] = on;
        if (state.lightSwitches.Count == 0) state.lightSwitches = null;
        s.note = !on ? "switched the light off" : roomPowered(state, r) ? "switched the light on" : "switched the light on: no power";
        return null;
    }

    /// <summary>In the room, or on a tile next to one of its tiles through a door (an edge door, or a door tile).</summary>
    public static bool reachesSwitch(SimState state, Point p, RoomLight r)
    {
        if (roomAt(state, p.x, p.y)?.key == r.key) return true;
        var g = state.grid;
        foreach (var (dx, dy) in DIR4)
        {
            int nx = p.x + dx, ny = p.y + dy;
            if (roomAt(state, nx, ny)?.key != r.key) continue;
            if (Grids.edgeKind(g, Grids.edgeBetween(g, p.x, p.y, nx, ny)) == Grids.EDGE_DOOR || Grids.getTile(g, p.x, p.y) == Grids.DOOR) return true;
        }
        return false;
    }

    // --- streets ---

    sealed class StreetLamps
    {
        public readonly Dictionary<long, List<(int x, int y, bool works, int side)>> byChunk = new();
    }

    static readonly ConditionalWeakTable<WorldPlan, StreetLamps> lampCache = new();

    /// <summary>A plan's street lamps, by plan chunk: every STREET_SPACING along each street, shifted and sided by a
    /// hash, on the first tile off the street that isn't road, nor in a building; STREET_WORKING of them work.</summary>
    static StreetLamps lampsOf(double seed, WorldRegions regions, WorldPlan plan)
    {
        if (lampCache.TryGetValue(plan, out var hit)) return hit;
        var made = new StreetLamps();
        var w = plan.window;
        foreach (var st in plan.streets)
        {
            double along = st.eastWest ? st.x1 - st.x0 : st.y1 - st.y0;
            double shift = JsMath.floor(Hash.hashRoll(seed, st.x0, st.y0, 0x1a3b) * STREET_SPACING);
            for (double k = shift; k <= along; k += STREET_SPACING)
            {
                bool works = Hash.hashRoll(seed, st.x0, st.y0, k, 0x1a3c) < STREET_WORKING;
                bool far = Hash.hashRoll(seed, st.x0, st.y0, k, 0x1a3d) < 0.5;
                int x = st.eastWest ? st.x0 + (int)k : (far ? st.x0 + Towns.STREET : st.x0 - 1);
                int y = st.eastWest ? (far ? st.y0 + Towns.STREET : st.y0 - 1) : st.y0 + (int)k;
                if (x < w.x0 || y < w.y0 || x >= w.x1 || y >= w.y1) continue;
                // On the sidewalk at the kerb (or whatever is beside the street there), never on asphalt.
                if (plan.roadMask.a[(y - w.y0) * w.W + (x - w.x0)] is Worldplan.ROAD or Worldplan.STREET) continue;
                long c = Key(x / Worldplan.PLAN_CHUNK, y / Worldplan.PLAN_CHUNK);
                if (!made.byChunk.TryGetValue(c, out var list)) made.byChunk[c] = list = new();
                list.Add((x, y, works, st.eastWest ? (far ? 2 : 0) : (far ? 1 : 3)));
            }
        }
        lampCache.AddOrUpdate(plan, made);
        return made;
    }

    /// <summary>Every street lamp's post within `radius` of (x, y), lit or not, working or not: its tile and the side of
    /// the street it stands on (0 north of it, 1 east, 2 south, 3 west), for the renderer's posts.</summary>
    public static List<(int x, int y, int side)> streetPosts(SimState state, double x, double y, double radius)
    {
        var @out = new List<(int, int, int)>();
        var regions = regionsOf(state);
        if (regions == null) return @out;
        int size = (int)state.world.size, C = Worldplan.PLAN_CHUNK, chunks = (size + C - 1) / C;
        for (int cy = Math.Max(0, (int)Math.Floor((y - radius) / C)); cy <= Math.Min(chunks - 1, (int)Math.Floor((y + radius) / C)); cy++)
            for (int cx = Math.Max(0, (int)Math.Floor((x - radius) / C)); cx <= Math.Min(chunks - 1, (int)Math.Floor((x + radius) / C)); cx++)
            {
                var plan = regions.RegionAt(cx * C, cy * C);
                if (!lampsOf(state.world.seed, regions, plan).byChunk.TryGetValue(Key(cx, cy), out var lamps)) continue;
                foreach (var (lx, ly, _, side) in lamps)
                    if (Math.Abs(lx + 0.5 - x) <= radius && Math.Abs(ly + 0.5 - y) <= radius) @out.Add((lx, ly, side));
            }
        return @out;
    }

    // --- the sources ---

    /// <summary>The lights lit now within `radius` tiles of (x, y) (tile units; a source counts by its own point),
    /// nearest first (then by id), at most `max` (MAX_NEAR). Bounded: plan chunks in reach, and the sparse lists in state.</summary>
    public static List<Source> near(SimState state, double x, double y, double radius, int max = MAX_NEAR)
    {
        var found = new List<Source>();
        bool night = Time.isNight(state.tick);
        void add(Source s)
        {
            double dx = s.x - x, dy = s.y - y;
            s.dist = JsMath.sqrt(dx * dx + dy * dy);
            if (s.dist <= radius) found.Add(s);
        }
        Source point(string kind, string id, double px, double py, string space) => new Source
        { kind = kind, id = id, x = px, y = py, radius = RADIUS[kind], strength = STRENGTH[kind], space = space };
        if (night) generated(state, x, y, radius, add, point);
        if (night)
        {
            // Rooms you walled in, switched on (sparse: only switched rooms are in the record).
            if (state.lightSwitches != null)
                foreach (var key in state.lightSwitches.Keys.OrderBy((k) => k, StringComparer.Ordinal).ToList())
                {
                    if (!state.lightSwitches[key]) continue;
                    var parts = key.Split(',');
                    int ax = int.Parse(parts[0]), ay = int.Parse(parts[1]);
                    var r = roomAt(state, ax, ay);
                    if (r == null || r.generated || r.key != key || !roomPowered(state, r)) continue;
                    add(roomSource(r));
                }
            foreach (var l in FurnitureMod.ofKind(state, "lamp"))
                if (Power.isPowered(state, l)) add(point(LAMP, l.id, l.x + 0.5, l.y + 0.5, spaceOf(state, l.x, l.y)));
        }
        foreach (var s in FurnitureMod.ofKind(state, "stove"))
            if (stoveLit(state, s)) add(point(STOVE, s.id, s.x + 0.5, s.y + 0.5, spaceOf(state, s.x, s.y)));
        foreach (var h in FurnitureMod.ofKind(state, "heater"))
            if (heaterLit(state, h)) add(point(HEATER, h.id, h.x + 0.5, h.y + 0.5, spaceOf(state, h.x, h.y)));
        foreach (var f in state.fires)
            if (Math.Abs(f.x + 0.5 - x) <= radius && Math.Abs(f.y + 0.5 - y) <= radius)
                add(point(FIRE, $"fire{Js.Str(f.x)},{Js.Str(f.y)}", f.x + 0.5, f.y + 0.5, spaceOf(state, f.x, f.y)));
        if (night)
        {
            foreach (var v in Vehicles.vehiclesOf(state))
            {
                if (!headlightsLit(v)) continue;
                var d = Js.Get(Vehicles.HEADING_DIRS, v.heading);
                double hx = d[0] / 256.0, hy = d[1] / 256.0;
                double cx = (double)v.at.x / Motion.SUB, cy = (double)v.at.y / Motion.SUB;
                // From the front of the body (VehicleBody.cs), not a metre ahead of its centre.
                double nose = (double)Vehicles.shapeOf(v).halfLength / Motion.SUB;
                var s = point(HEADLIGHTS, v.id, cx + hx * nose, cy + hy * nose, OUT);
                s.dx = hx; s.dy = hy; s.cone = HEADLIGHT_CONE;
                add(s);
            }
        }
        var beam = flashlightBeam(state);
        if (beam != null) add(beam);
        found.Sort((a, b) => a.dist != b.dist ? a.dist.CompareTo(b.dist) : string.CompareOrdinal(a.id, b.id));
        if (found.Count > max) found.RemoveRange(max, found.Count - max);
        return found;
    }

    /// <summary>Generated rooms switched on and powered, porches and street lamps (with the mains on), in the plan chunks in reach.</summary>
    static void generated(SimState state, double x, double y, double radius, Action<Source> add, Func<string, string, double, double, string, Source> point)
    {
        var regions = regionsOf(state);
        if (regions == null) return;
        double seed = state.world.seed;
        int size = (int)state.world.size, C = Worldplan.PLAN_CHUNK, chunks = (size + C - 1) / C;
        bool mains = TimelineMod.powerOn(state);
        int cx0 = Math.Max(0, (int)Math.Floor((x - radius) / C)), cx1 = Math.Min(chunks - 1, (int)Math.Floor((x + radius) / C));
        int cy0 = Math.Max(0, (int)Math.Floor((y - radius) / C)), cy1 = Math.Min(chunks - 1, (int)Math.Floor((y + radius) / C));
        var seen = new HashSet<double>();
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                foreach (var id in regions.LotsInChunk(cx, cy))
                {
                    if (!seen.Add(id)) continue;
                    var lot = regions.Lot(id);
                    var layout = Lots.layoutLot(seed, lot);
                    for (int bi = 0; bi < layout.buildings.Count; bi++)
                    {
                        var pr = planRooms(seed, lot, layout.buildings[bi], bi);
                        foreach (var r in pr.rooms)
                            if (switchedOn(state, r) && roomPowered(state, r)) add(roomSource(r));
                        if (mains && pr.porch)
                            add(point(PORCH, $"porch{Js.Str(pr.px)},{Js.Str(pr.py)}", pr.px + 0.5, pr.py + 0.5, OUT));
                    }
                }
                if (!mains) continue;
                var plan = regions.RegionAt(cx * C, cy * C);
                if (lampsOf(seed, regions, plan).byChunk.TryGetValue(Key(cx, cy), out var lamps))
                    foreach (var (lx, ly, works, _) in lamps)
                        if (works) add(point(STREET, $"street{Js.Str(lx)},{Js.Str(ly)}", lx + 0.5, ly + 0.5, OUT));
            }
    }

    static Source roomSource(RoomLight r) => new Source
    {
        kind = ROOM, id = $"room{r.key}", x = r.cx, y = r.cy, strength = STRENGTH[ROOM], space = r.key,
        radius = JsMath.sqrt(Math.Pow(r.x1 - r.x0 + 1, 2) + Math.Pow(r.y1 - r.y0 + 1, 2)) / 2,
        x0 = r.x0, y0 = r.y0, x1 = r.x1, y1 = r.y1, ax = r.ax, ay = r.ay,
    };

    /// <summary>At night with the mains off, does a room light switched on in reach of this generator draw on it?</summary>
    public static bool loadsGenerator(SimState state, Furniture g)
    {
        if (g.stored <= 0 || !Time.isNight(state.tick) || TimelineMod.powerOn(state)) return false;
        return near(state, g.x + 0.5, g.y + 0.5, Power.POWER_RADIUS + 16).some((s) => s.kind == ROOM && Space.manhattan(g, new Point { x = s.ax, y = s.ay }) <= Power.POWER_RADIUS);
    }

    /// <summary>A built stove is lit while someone works a cooking job at it.</summary>
    public static bool stoveLit(SimState state, Furniture s) =>
        state.jobs.some((j) => j.type == "cook" && j.furnitureId == s.id && j.reservedBy != null && j.progress > 0);

    /// <summary>A heater (brazier) is lit while it burns: fuel in it on a night heaters burn (Power.heaterNight).</summary>
    public static bool heaterLit(SimState state, Furniture h) => h.stored > 0 && Power.heaterNight(state);

    public static bool headlightsLit(Vehicle v) => v.lights == true && v.fuel > 0 && v.health > 0;

    // --- the flashlight ---

    /// <summary>The flashlight in the bag of the survivor you control, the first one switched on, else the first one.</summary>
    public static Item? flashlightOf(SimState state, Survivor s)
    {
        Item? first = null;
        foreach (var id in s.pack)
        {
            var i = Items.getItem(state, id);
            if (i?.kind != FLASHLIGHT) continue;
            if (i.lit == true) return i;
            first ??= i;
        }
        return first;
    }

    /// <summary>Switches a flashlight on or off. Null if done, else why not.</summary>
    public static string? toggleFlashlight(SimState state, Survivor s, Item? item = null)
    {
        item ??= flashlightOf(state, s);
        if (item == null || item.kind != FLASHLIGHT || !s.pack.includes(item.id)) return "no flashlight in your bag";
        item.lit = item.lit == true ? null : true;
        s.note = item.lit == true ? "switched the flashlight on" : "switched the flashlight off";
        return null;
    }

    /// <summary>The beam of the flashlight of the survivor you control, if it's on: from their centre, the way they aim.</summary>
    public static Source? flashlightBeam(SimState state)
    {
        var you = ControlMod.controlled(state);
        var c = state.control;
        if (you == null || c == null || Vehicles.vehicleOf(state, you) != null) return null;
        var f = flashlightOf(state, you);
        if (f?.lit != true) return null;
        var d = c.aim ?? c.facing;
        double len = JsMath.sqrt((double)d.x * d.x + (double)d.y * d.y);
        if (len == 0) return null;
        double px = (double)you.at.x / Motion.SUB, py = (double)you.at.y / Motion.SUB;
        return new Source
        {
            kind = FLASHLIGHT, id = f.id, x = px, y = py, radius = RADIUS[FLASHLIGHT], strength = STRENGTH[FLASHLIGHT],
            dx = d.x / len, dy = d.y / len, cone = FLASHLIGHT_CONE, space = spaceOf(state, you.pos.x, you.pos.y),
        };
    }

    /// <summary>A flashlight in a container you search, sometimes (a hash of the container: no RNG drawn).</summary>
    public static bool flashlightIn(Furniture crate) =>
        crate.table != null && FLASHLIGHT_TABLES.includes(crate.table) && Hash.hashRoll(Hash.idNumber(crate.id), 0xf1a5) < FLASHLIGHT_FIND;

    // --- what is lit ---

    /// <summary>How much `s` lights tile (x, y) (whose space is `space`): 0 if it doesn't reach.</summary>
    public static double levelFrom(SimState state, Source s, int x, int y, string space)
    {
        if (s.space != space) return 0;
        if (s.kind == ROOM) return s.strength;
        double px = x + 0.5 - s.x, py = y + 0.5 - s.y;
        double d = JsMath.sqrt(px * px + py * py);
        if (d > s.radius) return 0;
        if (s.cone > 0 && d > 0.75 && (px * s.dx + py * s.dy) / d < s.cone) return 0;
        if (space == OUT && d >= 1.5)
        {
            var from = new Point { x = (int)Math.Floor(s.x), y = (int)Math.Floor(s.y) };
            if (!Sight.sightLine(state.grid, from, new Point { x = x, y = y })) return 0;
        }
        return s.strength * (1 - d / (2 * s.radius));
    }

    /// <summary>How lit tile p is, and by what: daylight by day; at night the strongest source there.</summary>
    public static Lit lightAt(SimState state, Point p)
    {
        if (!Time.isNight(state.tick)) return new Lit { level = 1, day = true };
        return lightFrom(state, near(state, p.x + 0.5, p.y + 0.5, REACH), p.x, p.y);
    }

    /// <summary>The strongest of these sources at tile (x, y) (for callers that ask about many tiles near one place).</summary>
    public static Lit lightFrom(SimState state, List<Source> sources, int x, int y)
    {
        var best = new Lit();
        if (sources.Count == 0) return best;
        var space = spaceOf(state, x, y);
        foreach (var s in sources)
        {
            double l = levelFrom(state, s, x, y, space);
            if (l > best.level) { best.level = l; best.by = s; }
        }
        return best;
    }

    public static bool isLit(SimState state, Point p) => lightAt(state, p).level >= LIT;
}
