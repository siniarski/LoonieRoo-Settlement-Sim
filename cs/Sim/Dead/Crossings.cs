// Crossings (2026-11-11): the dead getting over, or under, what a body can't walk through.
//
// A parked car stops every body on foot (VehicleParking.cs) and a barricade stops the dead (Pathfinding.cs).
// Both are low, so the dead may cross them, slowly, where battering or going round was all they had:
//
//   what        who                       how long (a leg: one tile to the next)
//   parked car  walker, screamer, raider  climbs over, CLIMB_TICKS (2 s)
//               runner                    climbs over, 14 ticks; brute, 27 (CLIMB_TICKS by its pace)
//               crawler                   under a pickup (0.45 m clear: VehicleHeights.cs), UNDER_TICKS (1 s);
//                                         over a sedan (0.28 m clear: too low), hauled by its arms, HAUL_TICKS (4 s)
//   barricade   runner                    vaults it (the edge, or the tile), VAULT_TICKS (0.8 s)
//               crawler                   under, as it always went (a plain step: Pathfinding's "crawler")
//               the rest                  batter it, as before (Breach.cs)
//               the rest, in a crowd      PILE_UP or more pressed at one stretch (within a tile of it, counted from the
//                                         crowd's buckets: ZombieCrowd.cs): the ones behind climb over the ones in front,
//                                         one leg over the edge, PILE_TICKS (2 s); fewer than that only batter (pileOver)
//   wall, door, window, boards            battered, as before; a broken window is climbed through (Zombies.climbing)
//   water                                 never; pits and traps catch whoever steps on them; furniture never stopped them
// So over a car's width is two legs (onto it, off it: 4 s for a walker), and along a sedan parked in a gate six.
//
// Bodies and paths. What stops a body is unchanged (Pathfinding.isPassable and crossable are false at a car for
// everyone on foot), so nothing walks, slides or is pushed through a car. Only the dead's PATHS know better:
// A*, reachability and the horde's flow field let them onto a tile they can cross at a cost in steps (CAR_COST
// to climb onto each tile of a car, VAULT_COST over a barricade: pathCost, standsOn, vaults), so they go round
// when round is short and over when it is much shorter or the only way. A runner's paths are planned for the
// mover RUNNER (Pathfinding.cs: a zombie that vaults barricades); its body is a zombie's.
//
// A crossing is plain state on the zombie (Zombie.crossing: what, from, to, ticks left, of), one leg at a time,
// each a straight line between two points a tile apart moved by whole SUB units, so it saves and replays exactly.
//   begin     where its next step is blocked (Zombies.stepBlocked) and can be crossed: the way over is followed
//             first, along its path (a horde's member: down its field) to the first ground beyond, and the
//             crossing is not started unless a body fits there
//   tick      moves it along the leg; on it the zombie neither bites nor steers, is not pushed about by the crowd,
//             and a blow holds it where it is (no shove). At a leg's end on the car, the next leg follows the
//             path; if the way on has gone, it gets down wherever a body fits, or waits (PERCH_TICKS) and looks again
//   moved     anything else that moves it (a car parking where it is) ends the crossing where it was put
// No queue: several may be on one car at once (they are pushed apart again on the ground).
// lift() is how high its feet are, for the renderer (the car's bonnet, roof or bed under it; a vault's arc).
#nullable disable warnings
namespace LoonieRoo.Sim;

public static class Crossings
{
    public const string CLIMB = "climb", UNDER = "under", VAULT = "vault", DOWN = "down", PILE = "pile";

    /// <summary>A barricade edge is climbed (over the bodies pressed at it) once this many of the dead stand within a
    /// tile of the one at it, itself counted: a stretch three edges long with two rows of them at it. Fewer batter.</summary>
    public const int PILE_UP = 6;

    /// <summary>Ticks to climb over the pile and the barricade's edge (one leg, ground to ground).</summary>
    public const int PILE_TICKS = 20;

    /// <summary>For the renderer (metres): the top of a pile-up's climb, on the backs of the ones at the barricade.</summary>
    public const double PILE_HEIGHT = 1.1;

    /// <summary>Knocked off a car or a barricade by a blow (knockOff), it lies where it fell this long before it
    /// gets up and tries again: no biting, no moving, and a blow lands as on one unaware of you (ControlMod).</summary>
    public const int DOWN_TICKS = 30;

    /// <summary>Ticks a leg (one tile to the next): a walker over a car (others by their pace), a crawler hauling
    /// itself over a sedan, a crawler under a pickup, a runner over a barricade.</summary>
    public const int CLIMB_TICKS = 20, HAUL_TICKS = 40, UNDER_TICKS = 10, VAULT_TICKS = 8;

    /// <summary>What a path pays, in steps, to climb onto a tile of a car, and to vault a barricade (the edge, or
    /// onto the tile): more than the time is worth, so the dead keep to the ground while round is near.</summary>
    public const int CAR_COST = 12, VAULT_COST = 3;

    /// <summary>A car thumps (a body coming onto it, across it, off it: carClimbed) no more often than this: a crowd
    /// going over at once is one din, and the noise's cost (a pass over the dead) stays bounded per car.</summary>
    public const int THUMP_GAP = 5;

    /// <summary>On a car with no way on or down, it waits this long before looking again.</summary>
    public const int PERCH_TICKS = 10;

    /// <summary>A way over is followed no further than this many tiles before it is given up.</summary>
    public const int MAX_ROUTE = 24;

    /// <summary>For the renderer (metres): a vault's highest point, a barricade's top, and how much of a leg the
    /// climb up (or down) takes: the rest is spent moving across the top.</summary>
    public const double VAULT_HEIGHT = 0.6, BARRICADE_HEIGHT = 0.9, RISE = 0.4;

    // --- paths (Pathfinding.cs, Hordes.cs) ---

    /// <summary>Do this mover's paths cross things? The dead's movers, and the breacher they fall back on.</summary>
    public static bool plans(string mover) =>
        mover == Pathfinding.ZOMBIE || mover == Pathfinding.RUNNER || mover == Pathfinding.CRAWLER || mover == Pathfinding.RAIDER || mover == Pathfinding.BREACHER;

    /// <summary>The mover a zombie's paths are planned for (its body moves as Zombies.moverOf).</summary>
    public static string planner(Zombie z) => vaulter(z) ? Pathfinding.RUNNER : Zombies.moverOf(z);

    /// <summary>Runners vault barricades.</summary>
    public static bool vaulter(Zombie z) => z.variant == "runner" && z.kind != "raider";

    /// <summary>A tile no body can stand on that this mover's paths may still go over: under a parked car, or
    /// (a runner) a barricade.</summary>
    public static bool standsOn(Grid grid, int x, int y, string mover)
    {
        // (Asked of every wall a search or a flood meets: the cheap looks first.)
        if (Grids.isParked(grid, x, y)) return plans(mover) && Grids.getTile(grid, x, y) == Grids.FLOOR;
        return mover == Pathfinding.RUNNER && Grids.getTile(grid, x, y) == Grids.BARRICADE;
    }

    /// <summary>An edge that stops this mover's body but not its paths: a barricade, to a runner.</summary>
    public static bool vaults(int edgeKind, string mover) => edgeKind == Grids.EDGE_BARRICADE && mover == Pathfinding.RUNNER;

    /// <summary>What a path pays (in steps, in place of the one) to go from tile a into its neighbour b where a
    /// body of this mover can't simply walk; 0 where it can't be crossed either.</summary>
    public static double pathCost(Grid grid, int ax, int ay, int bx, int by, string mover)
    {
        // (Asked at every wall a search meets: nothing but a car is crossed by any mover but a runner.)
        if (mover != Pathfinding.RUNNER && !Grids.isParked(grid, bx, by)) return 0;
        if (!plans(mover)) return 0;
        double cost = 0;
        if (!Pathfinding.crossable(grid, ax, ay, bx, by, mover))
        {
            if (!vaults(Grids.edgeKindBetween(grid, ax, ay, bx, by), mover)) return 0;
            cost = VAULT_COST;
        }
        if (!Pathfinding.isPassable(grid, bx, by, mover))
        {
            if (!standsOn(grid, bx, by, mover)) return 0;
            cost += Grids.isParked(grid, bx, by) ? CAR_COST : VAULT_COST;
        }
        return JsMath.max(1, cost);
    }

    // --- bodies (Zombies.cs) ---

    /// <summary>Is this tile something this zombie crosses and can't stand on: under a parked car, or (a runner)
    /// a barricade? standsOn, for the mover its paths are planned for.</summary>
    public static bool obstacle(Grid grid, int x, int y, Zombie z) => standsOn(grid, x, y, planner(z));

    /// <summary>How this zombie gets from tile a to its neighbour b where that is a crossing (climb, under, vault);
    /// null where it is an ordinary step, or no step at all: something else in the way, or no room for a body on
    /// the ground it would come down on.</summary>
    public static string? way(SimState state, Zombie z, Point a, Point b)
    {
        var grid = state.grid;
        if (Space.manhattan(a, b) != 1 || !Grids.inBounds(grid, b.x, b.y)) return null;
        string body = Zombies.moverOf(z);
        bool overEdge = false;
        if (!Pathfinding.crossable(grid, a.x, a.y, b.x, b.y, body))
        {
            if (!vaulter(z) || Grids.edgeKindBetween(grid, a.x, a.y, b.x, b.y) != Grids.EDGE_BARRICADE) return null;
            overEdge = true;
        }
        bool onA = obstacle(grid, a.x, a.y, z), onB = obstacle(grid, b.x, b.y, z);
        if (!onB && (!Pathfinding.isPassable(grid, b.x, b.y, body) || !Motion.fits(grid, Motion.centre(b), body))) return null;
        if (!onA && !onB && !overEdge) return null;
        var car = Grids.isParked(grid, b.x, b.y) ? b : Grids.isParked(grid, a.x, a.y) ? a : null;
        if (car == null) return VAULT;
        return z.variant == "crawler" && Vehicles.parkedOver(state, car.x, car.y)?.kind == "truck" ? UNDER : CLIMB;
    }

    /// <summary>How many ticks a leg takes this zombie.</summary>
    public static int legTicks(Zombie z, string what)
    {
        if (what == VAULT) return VAULT_TICKS;
        if (what == UNDER) return UNDER_TICKS;
        if (z.variant == "crawler") return HAUL_TICKS;
        double pace = z.kind == "raider" ? Zombies.ZOMBIE_TICKS_PER_TILE : Js.Get(ZombieKinds.VARIANTS, z.variant).ticksPerTile;
        return Js.I(JsMath.ceil(CLIMB_TICKS * pace / Zombies.ZOMBIE_TICKS_PER_TILE));
    }

    /// <summary>The zombie's step to `next` is blocked: cross it, if it is something this one crosses and there is
    /// ground for a body beyond. True if a crossing began.</summary>
    public static bool begin(SimState state, Zombie z, Point next)
    {
        if (z.crossing != null) return true;
        var what = way(state, z, z.pos, next);
        if (what == null) return false;
        var route = routeOver(state, z, next, out int used);
        if (route == null) return false;
        if (used < route.Count)
        {
            // Not all of it was on its path (a horde's member has one step at a time): the way over is its path now.
            route.AddRange(z.path.slice(used));
            z.path = route;
        }
        startLeg(state, z, next, what);
        return true;
    }

    /// <summary>For a scene set by hand (Staging.cs): the zombie this many ticks into the crossing its path
    /// starts with, as if it had begun it that long ago. False if its path doesn't start with one.</summary>
    public static bool stage(SimState state, Zombie z, int ticks)
    {
        var next = Js.Get(z.path, 0);
        if (next == null || !begin(state, z, next)) return false;
        for (int t = 0; t < ticks && z.crossing != null; t++) tick(state, z);
        z.was = z.at.Copy();
        return true;
    }

    /// <summary>The tiles from `next` over whatever is in the way to the first ground beyond (the last of the list),
    /// along the zombie's path as far as that says, then down its horde's field or straight on. Null if there is
    /// no such way. `used`: how many of them were the head of its path.</summary>
    static List<Point>? routeOver(SimState state, Zombie z, Point next, out int used)
    {
        var grid = state.grid;
        var horde = Hordes.getHorde(state, z.hordeId);
        bool onPath = z.path.Count > 0 && Space.samePoint(z.path[0], next);
        used = onPath ? 1 : 0;
        var route = new List<Point> { next };
        Point prev = z.pos, cur = next;
        for (int n = 0; n < MAX_ROUTE; n++)
        {
            if (!obstacle(grid, cur.x, cur.y, z)) return route;
            Point? on = null;
            if (onPath && used < z.path.Count)
            {
                on = z.path[used];
                if (way(state, z, cur, on) == null) return null;
                used++;
            }
            else
            {
                onPath = false;
                var down = horde != null ? Hordes.hordeStepFrom(state, cur, horde) : null;
                var straight = new Point(cur.x + (cur.x - prev.x), cur.y + (cur.y - prev.y));
                if (down != null && !Space.samePoint(down, prev) && way(state, z, cur, down) != null) on = down;
                else if (way(state, z, cur, straight) != null) on = straight;
                else return null;
            }
            route.Add(on);
            prev = cur;
            cur = on;
        }
        return null;
    }

    static void startLeg(SimState state, Zombie z, Point to, string what)
    {
        if (what == CLIMB)
        {
            var car = Vehicles.parkedOver(state, to.x, to.y) ?? Vehicles.parkedOver(state, z.pos.x, z.pos.y);
            if (car != null) carClimbed(state, car, z);
        }
        int n = legTicks(z, what);
        z.crossing = new Crossing { what = what, from = z.at.Copy(), to = Motion.centre(to), ticks = n, of = n };
    }

    /// <summary>Where the leg has it after `done` of its ticks.</summary>
    static Point spot(Crossing c, int done) => new Point(
        Js.I(c.from.x + JsMath.trunc((double)(c.to.x - c.from.x) * done / c.of)),
        Js.I(c.from.y + JsMath.trunc((double)(c.to.y - c.from.y) * done / c.of)));

    /// <summary>A tick of a crossing. True while it is on its way (the tick is spent: no biting, no steering).</summary>
    public static bool tick(SimState state, Zombie z)
    {
        var c = z.crossing;
        if (c == null) return false;
        var due = spot(c, c.of - c.ticks);
        if (z.at.x != due.x || z.at.y != due.y)
        {
            // Something else moved it (a car came to rest where it was): the crossing is over where it was put.
            abandon(state, z);
            return false;
        }
        c.ticks -= 1;
        z.at = spot(c, c.of - c.ticks);
        if (Zombies.enter(state, z)) return true; // a trap on the ground it came down on
        if (c.ticks <= 0) advance(state, z);
        return true;
    }

    /// <summary>A leg is done: on the ground the crossing is over; still on the car (or the barricade), the next leg.</summary>
    static void advance(SimState state, Zombie z)
    {
        var grid = state.grid;
        var c = z.crossing;
        var here = z.pos;
        if (c.what == DOWN) { z.crossing = null; z.nextThinkTick = state.tick; return; } // up again, and it thinks
        if (!obstacle(grid, here.x, here.y, z))
        {
            z.crossing = null;
            // What it stood on has gone from under it (the car driven off) and left it somewhere a body can't be.
            if (!Motion.fits(grid, z.at, Zombies.moverOf(z))) abandon(state, z);
            return;
        }
        while (z.path.Count > 0 && Space.samePoint(z.path[0], here)) z.path.shift();
        var next = Js.Get(z.path, 0);
        var what = next != null ? way(state, z, here, next) : null;
        if (what == null)
        {
            // The way on has gone: down wherever there is ground for a body, straight on first.
            z.path = [];
            var from = Motion.tileOf(c.from);
            var ahead = new Point(here.x + (here.x - from.x), here.y + (here.y - from.y));
            next = null;
            foreach (var p in new List<Point> { ahead, new(here.x, here.y - 1), new(here.x + 1, here.y), new(here.x, here.y + 1), new(here.x - 1, here.y) })
            {
                if (Space.samePoint(p, here) || !Grids.inBounds(grid, p.x, p.y) || obstacle(grid, p.x, p.y, z)) continue;
                what = way(state, z, here, p);
                if (what == null) continue;
                next = p;
                break;
            }
        }
        if (next == null)
        {
            // Nowhere to go: it stays up there and looks again shortly.
            z.crossing = new Crossing { what = c.what, from = z.at.Copy(), to = z.at.Copy(), ticks = PERCH_TICKS, of = PERCH_TICKS };
            return;
        }
        startLeg(state, z, next, what);
    }

    /// <summary>The crossing is off: the zombie stays where it is if a body fits there, else goes to the nearest
    /// tile it can stand on.</summary>
    static void abandon(SimState state, Zombie z)
    {
        var grid = state.grid;
        z.crossing = null;
        z.path = [];
        string body = Zombies.moverOf(z);
        if (Motion.fits(grid, z.at, body)) return;
        var to = Space.nearestTile(grid, z.pos, (x, y) => Pathfinding.isPassable(grid, x, y, body), 400);
        if (to != null) Motion.place(z, to);
    }

    // --- pile-ups at a barricade (Zombies.stepBlocked, when it would strike it) ---

    /// <summary>The zombie at a barricade's edge it can't vault, on its way across it (its path, a horde's member's
    /// field: the way it would batter through), with enough of the dead pressed round it: over it, on the ones in
    /// front. Asked only when it would strike (once in ZOMBIE_ATTACK_EVERY), and the count is the crowd's buckets'
    /// (zombiesWithin: a few dozen cells), so a crowd at a fence costs no scan. Paths, reachability and the horde's
    /// field are unchanged: a pile-up only crosses where the path already goes (through the barricade, battering),
    /// faster than battering would. True if it began.</summary>
    public static bool pileOver(SimState state, Zombie z, Point next)
    {
        if (z.crossing != null || z.kind == "raider" || vaulter(z) || z.variant == "crawler") return false;
        var grid = state.grid;
        var a = z.pos;
        if (Space.manhattan(a, next) != 1 || Grids.edgeKindBetween(grid, a.x, a.y, next.x, next.y) != Grids.EDGE_BARRICADE) return false;
        string body = Zombies.moverOf(z);
        if (!Pathfinding.isPassable(grid, next.x, next.y, body) || !Motion.fits(grid, Motion.centre(next), body)) return false;
        if (Zombies.zombiesWithin(state, a, 1) < PILE_UP) return false;
        z.crossing = new Crossing { what = PILE, from = z.at.Copy(), to = Motion.centre(next), ticks = PILE_TICKS, of = PILE_TICKS };
        return true;
    }

    // --- the noise of it ---

    /// <summary>One of the dead starts a leg onto, across or off a parked car: a thump ("thump", 10 tiles: louder than
    /// you walking, 4, quieter than a shot, 22 and up) that draws the idle dead and wakes the dormant (Noise.cs),
    /// at most once every THUMP_GAP ticks a car. `car.climbed` is the tick of the last thump.
    /// The place for anything else a climbed car does (its alarm: Vehicle*.cs) is here.</summary>
    public static void carClimbed(SimState state, Vehicle car, Zombie z)
    {
        if (car.climbed != null && state.tick - car.climbed < THUMP_GAP) return;
        car.climbed = state.tick;
        Noise.makeNoise(state, car.pos, "thump");
    }

    // --- knocked off (Zombies.knockBack) ---

    /// <summary>On the ground where it was knocked off something, not yet up.</summary>
    public static bool isDown(Zombie z) => z.crossing?.what == DOWN;

    /// <summary>A blow (or a shove, or a car) on one of the dead part-way over: it falls back the way it came, to the
    /// first ground a body fits behind the leg it was on (else the nearest), and lies there DOWN_TICKS. Already
    /// down, a blow leaves it lying as it is. Its path is dropped: up again, it plans (and climbs) afresh.</summary>
    public static void knockOff(SimState state, Zombie z)
    {
        var c = z.crossing;
        if (c == null || c.what == DOWN) return;
        var grid = state.grid;
        string body = Zombies.moverOf(z);
        var from = Motion.tileOf(c.from);
        var to = Motion.tileOf(c.to);
        int dx = Js.I(JsMath.sign(from.x - to.x)), dy = Js.I(JsMath.sign(from.y - to.y));
        bool ground(int x, int y) => Grids.inBounds(grid, x, y) && !obstacle(grid, x, y, z) && Pathfinding.isPassable(grid, x, y, body)
            && Motion.fits(grid, Motion.centre(new Point(x, y)), body);
        Point? land = null;
        var t = from;
        for (int n = 0; n < 4 && land == null && (n == 0 || dx != 0 || dy != 0); n++)
        {
            if (ground(t.x, t.y)) land = t;
            else t = new Point(t.x + dx, t.y + dy);
        }
        land ??= Space.nearestTile(grid, z.pos, ground, 400);
        z.crossing = null;
        z.path = [];
        if (land == null) return;
        Motion.place(z, land);
        z.crossing = new Crossing { what = DOWN, from = z.at.Copy(), to = z.at.Copy(), ticks = DOWN_TICKS, of = DOWN_TICKS };
    }

    // --- for the renderer ---

    /// <summary>How high (metres) the top of what is under a point is, for this zombie: the part of a car it is on
    /// (its bonnet, roof or bed: VehicleHeights.cs), a barricade's top, else the ground.</summary>
    static double top(SimState state, Zombie z, Point at)
    {
        var t = Motion.tileOf(at);
        var v = Vehicles.parkedOver(state, t.x, t.y);
        if (v == null) return obstacle(state.grid, t.x, t.y, z) ? BARRICADE_HEIGHT : 0;
        return Vehicles.partTopCm(v, Vehicles.alongOf(v, at)) / 100;
    }

    /// <summary>A crossing zombie's feet over the ground (metres) at a moment of the tick (`alpha`: the renderer's
    /// blend into it, 0..1): up onto the car in the first part of a leg, across its top, down in the last part
    /// of the leg that leaves it; an arc for a vault over a barricade's edge; nothing for a crawler underneath.</summary>
    public static double lift(SimState state, Zombie z, double alpha)
    {
        var c = z.crossing;
        if (c == null || c.what == UNDER || c.what == DOWN || c.of <= 0) return 0;
        double t = JsMath.max(0, JsMath.min(1, (c.of - c.ticks - 1 + alpha) / c.of));
        if (c.what == PILE) return 4 * PILE_HEIGHT * t * (1 - t);
        double h0 = top(state, z, c.from), h1 = top(state, z, c.to);
        if (h0 == 0 && h1 == 0) return 4 * VAULT_HEIGHT * t * (1 - t);
        if (h1 > h0) return h0 + (h1 - h0) * JsMath.min(1, t / RISE);
        if (h1 < h0) return h1 + (h0 - h1) * JsMath.min(1, (1 - t) / RISE);
        return h0;
    }
}
