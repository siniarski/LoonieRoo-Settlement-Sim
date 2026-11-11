// Leaps (Space in first person): jump, vault or hop, whichever is in front of you. A short busy
// action under direct control (control.busy.what: jump | vault | hop, and over | climb | down at a
// parked car: CarLeaps.cs) that carries you in a
// straight line for a few ticks:
//   vault  through a broken window, or over a barricade, on the tile edge just ahead (within
//          LEAP_REACH of it): VAULT_TICKS, where climbing through a frame goes at a crawl
//   hop    over one tile of water (a ditch, a stream's edge) to the ground beyond
//   jump   anywhere else: up and down, carried on the way you were going at your pace then
//          (a run's jump goes further); walls still stop you
// "Ahead" is the way you steer, else the way you look, to the nearest of the four sides.
// The landing is fixed at take-off (busy.targetId "x,y", SUB units) and checked to fit you; each
// tick moves you the rest of the way over the ticks left: integers, so it replays exactly.
// Leaps are movement, so they count ticks (seconds whatever the day length), not slow ticks.
// In the air: steering waits for the landing; swinging, using and work are refused. Your tile
// (pos) follows you only onto ground you could stand on, or into water you could wade (not the water under a hop).
// In deep water Space is a stroke instead (Swim.cs: busy "stroke", carried like a jump, at the water's level).
// Each costs endurance; winded, you can't. The ground has no heights that block, so a step up is
// just a jump (the renderer's ground eases the view up a kerb or a rise).
// A parked car is the one thing you get over or onto (2026-11-11): over it, onto it, standing on it
// and down off it are CarLeaps.cs (part of Leaps), tried before the vault and the hop.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Leaps
{
    public const int JUMP_TICKS = 6, VAULT_TICKS = 4, HOP_TICKS = 6;
    public const int JUMP_COST = 4, VAULT_COST = 6, HOP_COST = 6;

    /// <summary>How near (SUB, centre to the near side) the window, barricade or water must be: a little over half a tile.</summary>
    public const int LEAP_REACH = 160;

    /// <summary>Land this far past the far side (SUB): your body clear of it.</summary>
    public const int CLEAR = Motion.BODY_RADIUS + 8;

    /// <summary>Highest point of each (metres over the ground, for the renderer's arc).</summary>
    public const double JUMP_HEIGHT = 0.35, VAULT_HEIGHT = 0.55, HOP_HEIGHT = 0.45;

    public static bool isLeap(string? what) => what is "jump" or "vault" or "hop" or "over" or "climb" or "down" or "stroke";

    public static bool airborne(Control c) => isLeap(c?.busy?.what);

    /// <summary>Leap: sets the busy action and a note. Returns null, or why not.</summary>
    public static string? leap(SimState state, Survivor s)
    {
        var c = Js.NN(state.control);
        if (Js.T(Vehicles.vehicleOf(state, s))) return "not in a car";
        if (Js.T(Vehicles.underOf(state, s))) return "not under a car";
        if (airborne(c)) return "already in the air";
        if (Swim.swimming(state, s)) return Swim.stroke(state, s, c);  // out of your depth, Space is a stroke (Swim.cs)
        if (BodyMod.isWinded(s)) return "too winded to jump";
        bool steering = c.dx != 0 || c.dy != 0;
        var ahead = side(steering ? new Point { x = Js.I(c.dx), y = Js.I(c.dy) } : ControlMod.swingDir(c));
        if (carLeap(state, s, c, ahead, steering)) return null;
        string what = "jump";
        Point land = null;
        string note = "jumped";
        if (ahead != null)
        {
            var (v, kind) = vaultLanding(state.grid, s.at, s.pos, ahead);
            if (v != null) { what = "vault"; land = v; note = kind == Grids.EDGE_BARRICADE ? "vaulted the barricade" : "climbed through the window"; }
            else if ((land = hopLanding(state.grid, s.at, s.pos, ahead)) != null) { what = "hop"; note = "hopped over the water"; }
        }
        if (land == null)
        {
            // On the way you were going, at your pace then, for as long as you're up.
            var run = c.running == true && c.sneaking != true;
            var v = steering ? Motion.toward(c.dx, c.dy, ControlMod.stepLength(state, s, c, run) * JUMP_TICKS) : new Point();
            land = new Point { x = Js.I(s.at.x + v.x), y = Js.I(s.at.y + v.y) };
        }
        int ticks = what == "vault" ? VAULT_TICKS : what == "hop" ? HOP_TICKS : JUMP_TICKS;
        BodyMod.exert(s, what == "vault" ? VAULT_COST : what == "hop" ? HOP_COST : JUMP_COST);
        c.busy = new ControlBusy { what = what, targetId = $"{land.x},{land.y}", progress = 0, duration = ticks };
        s.note = note;
        return null;
    }

    /// <summary>The nearest of the four sides to a direction, or null for none.</summary>
    public static Point? side(Point d)
    {
        if (d == null || (d.x == 0 && d.y == 0)) return null;
        return JsMath.abs(d.x) >= JsMath.abs(d.y) ? new Point { x = Js.I(JsMath.sign(d.x)), y = 0 } : new Point { x = 0, y = Js.I(JsMath.sign(d.y)) };
    }

    /// <summary>How far (SUB) from `at` to the side of tile `pos` facing `d`.</summary>
    static double toSide(Point at, Point pos, Point d)
    {
        if (d.x > 0) return (double)(pos.x + 1) * Motion.SUB - at.x;
        if (d.x < 0) return at.x - (double)pos.x * Motion.SUB;
        if (d.y > 0) return (double)(pos.y + 1) * Motion.SUB - at.y;
        return at.y - (double)pos.y * Motion.SUB;
    }

    /// <summary>Where you'd land `past` SUB beyond the side of tile `far` nearest you (going `d`), level with where you are but inside that tile; null if you wouldn't fit there.</summary>
    static Point? landPast(Grid grid, Point at, Point far, Point d)
    {
        int r = Motion.BODY_RADIUS;
        Point land;
        if (d.x != 0)
        {
            double line = d.x > 0 ? (double)far.x * Motion.SUB : (double)(far.x + 1) * Motion.SUB;
            double y = JsMath.max((double)far.y * Motion.SUB + r, JsMath.min((double)(far.y + 1) * Motion.SUB - r, at.y));
            land = new Point { x = Js.I(line + d.x * CLEAR), y = Js.I(y) };
        }
        else
        {
            double line = d.y > 0 ? (double)far.y * Motion.SUB : (double)(far.y + 1) * Motion.SUB;
            double x = JsMath.max((double)far.x * Motion.SUB + r, JsMath.min((double)(far.x + 1) * Motion.SUB - r, at.x));
            land = new Point { x = Js.I(x), y = Js.I(line + d.y * CLEAR) };
        }
        return Motion.fits(grid, land, Pathfinding.FREE) ? land : null;
    }

    /// <summary>A vault: a broken (unboarded) window or a barricade on the edge just ahead. The landing and the edge's kind, or (null, 0).</summary>
    public static (Point? land, int kind) vaultLanding(Grid grid, Point at, Point pos, Point d)
    {
        int nx = pos.x + d.x, ny = pos.y + d.y;
        if (!Grids.inBounds(grid, nx, ny)) return (null, 0);
        int e = Grids.edgeBetween(grid, pos.x, pos.y, nx, ny);
        int kind = Grids.edgeKind(grid, e);
        bool low = (kind == Grids.EDGE_WINDOW_BROKEN && !grid.edgeBoarded.Contains(e)) || kind == Grids.EDGE_BARRICADE;
        if (!low) return (null, 0);
        var near = toSide(at, pos, d);
        if (near < 0 || near > LEAP_REACH) return (null, 0);
        // Landing just past the edge: the edge's line is the near side of the tile beyond.
        return (landPast(grid, at, new Point { x = nx, y = ny }, d), kind);
    }

    /// <summary>A hop: one tile of water just ahead, open ground beyond it, nothing standing on the edges between. The landing, or null.</summary>
    public static Point? hopLanding(Grid grid, Point at, Point pos, Point d)
    {
        int nx = pos.x + d.x, ny = pos.y + d.y, fx = nx + d.x, fy = ny + d.y;
        if (!Grids.inBounds(grid, fx, fy) || Grids.getTile(grid, nx, ny) != Grids.WATER) return null;
        if (!Pathfinding.isPassable(grid, fx, fy, Pathfinding.FREE)) return null;
        if (Grids.edgeKind(grid, Grids.edgeBetween(grid, pos.x, pos.y, nx, ny)) != Grids.EDGE_NONE || Grids.edgeKind(grid, Grids.edgeBetween(grid, nx, ny, fx, fy)) != Grids.EDGE_NONE) return null;
        var near = toSide(at, pos, d);
        if (near < 0 || near > LEAP_REACH) return null;
        return landPast(grid, at, new Point { x = fx, y = fy }, d);
    }

    /// <summary>A tick in the air: a share of the way to the landing (all of what's left on the last tick).</summary>
    public static void tick(SimState state, Survivor s, Control c)
    {
        var b = Js.NN(c.busy);
        b.progress += 1;
        var xy = b.targetId.Split(',');
        bool car = xy.Length >= CAR_PARTS;
        double tx = double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), ty = double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
        double left = JsMath.max(1, b.duration - b.progress + 1);
        var dx = JsMath.trunc((tx - s.at.x) / left);
        var dy = JsMath.trunc((ty - s.at.y) / left);
        if ((b.what == "jump" || b.what == "stroke") && !car)
        {
            var was = s.at;
            s.at = Motion.slide(state.grid, s.at, dx, dy, ControlMod.walkMover(state.grid, s.at));
            // A parked car stops a jump at its body, as it stops walking (VehicleParking.cs pushOut).
            var off = Vehicles.pushOut(state, s.at, Motion.BODY_RADIUS);
            if (off.x != s.at.x || off.y != s.at.y) s.at = Motion.fits(state.grid, off, Pathfinding.SWIMMER) ? off : was;
            s.at = FurnitureBody.stop(state, was, s.at, Motion.BODY_RADIUS);  // furniture stops a jump as it stops a walk
        }
        else
        {
            // Over the sill, the water or a car: the landing was checked to fit at take-off.
            s.at = new Point { x = Js.I(s.at.x + dx), y = Js.I(s.at.y + dy) };
        }
        var tile = Motion.tileOf(s.at);
        if ((tile.x != s.pos.x || tile.y != s.pos.y) && Pathfinding.isPassable(state.grid, tile.x, tile.y, b.what == "hop" ? Pathfinding.FREE : Pathfinding.SWIMMER))
        {
            s.pos = tile;
            Survivors.enterTile(state, s);
        }
        if (b.progress >= b.duration)
        {
            c.busy = null;
            if (car) landed(state, s, c, b.what, xy[2]);
        }
    }

    /// <summary>Your feet over the ground (metres) at a moment of the leap: an arc, up and down over the ticks the renderer sees it (busy is gone on the landing tick). `alpha` is the renderer's blend into the tick, 0..1.</summary>
    public static double lift(ControlBusy? b, double alpha)
    {
        if (b == null || !isLeap(b.what) || b.what == "stroke" || b.duration < 2) return 0;
        double t = (b.progress - 1 + alpha) / (b.duration - 1);
        if (t <= 0 || t >= 1) return 0;
        double h = b.what == "vault" ? VAULT_HEIGHT : b.what == "hop" ? HOP_HEIGHT : JUMP_HEIGHT;
        return 4 * h * t * (1 - t);
    }
}
