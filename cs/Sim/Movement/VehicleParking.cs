// Parked cars are in the way (part of Vehicles; 2026-11-11): the tiles under a car nobody is driving can't be
// walked on, by people, the dead or animals, so paths go round it and anyone walking into it stops at it.
//
//   which tiles   those whose centre is inside the car's body (VehicleBody.cs): the row it stands along, five tiles
//                 for a sedan; a car on the slant covers a stair of them. A tile it only overhangs stays open (a
//                 street three tiles wide with a car down its middle can still be walked either side).
//   where kept    Grid.parkedLayer, a byte a tile, which Pathfinding.isPassable asks for every mover but a car
//                 (cars meet each other body to body). It is not saved: it is exactly what state.vehicles says, and
//                 is laid in again whenever a state is read (SimState.OnDeserialized), so a loaded game and one
//                 that ran on agree.
//   kept right    park() when a car is put down or its driver gets out; unpark() before one is driven off or
//                 taken on an expedition. Both bump walkabilityVersion (paths made before are stale) and note
//                 the change for reachability. Anything that moves a parked car any other way must unpark it
//                 first and park it after, or call remask().
//   you           on foot you stop at the car's body, not only at its tiles (pushOut, from ControlMod's walking):
//                 the body is wider than the row under it, and from your eyes the difference shows. The dead and
//                 animals stop at the tiles: their paths are made of tiles, and must agree with what stops them.
//   who was there someone standing where a car comes to rest is moved to the nearest open tile (Motion.place): a
//                 tile walker whose tile is under it, and a free mover (you, the dead, animals, dogs) whose body
//                 so much as overlaps a tile under it, since a body in something solid can't take a step.
// A car being driven is not in the way of anyone: the dead come at its driver as they always did.
#nullable disable warnings
namespace LoonieRoo.Sim;

public static partial class Vehicles
{
    /// <summary>Nobody at the wheel (nor away on an expedition): it stands where it is.</summary>
    public static bool isParked(Vehicle v) => !Js.T(v.driverId);

    /// <summary>The tiles (as tile indices of this grid) whose centre is inside a car's body, in row order.</summary>
    public static List<int> tilesUnder(Grid grid, Vehicle v)
    {
        var shape = shapeOf(v);
        var body = circles(shape, v.at, v.heading);
        double r2 = (double)shape.halfWidth * shape.halfWidth;
        int x0 = (int)JsMath.floor((double)(v.at.x - shape.halfLength) / Motion.SUB), x1 = (int)JsMath.floor((double)(v.at.x + shape.halfLength) / Motion.SUB);
        int y0 = (int)JsMath.floor((double)(v.at.y - shape.halfLength) / Motion.SUB), y1 = (int)JsMath.floor((double)(v.at.y + shape.halfLength) / Motion.SUB);
        var tiles = new List<int>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!Grids.inBounds(grid, x, y)) continue;
                var centre = Motion.centre(new Point { x = x, y = y });
                foreach (var c in body)
                {
                    if (Motion.distance2(c, centre) <= r2)
                    {
                        tiles.Add(Grids.tileIndex(grid, x, y));
                        break;
                    }
                }
            }
        }
        return tiles;
    }

    /// <summary>The parked car over a tile (its body over the tile's centre: one of its tilesUnder), or null.</summary>
    public static Vehicle? parkedOver(SimState state, int x, int y)
    {
        if (!Grids.isParked(state.grid, x, y)) return null;
        var centre = Motion.centre(new Point(x, y));
        foreach (var v in vehiclesOf(state))
        {
            if (!isParked(v)) continue;
            double far = shapeOf(v).halfLength + Motion.SUB;
            if (Motion.distance2(v.at, centre) > far * far) continue;
            if (gap(v, centre) == 0) return v;
        }
        return null;
    }

    /// <summary>Mark the tiles under a parked car. True if any changed. No version bump, nobody moved: for a car
    /// taken into the world with its chunk, or laid in again from a state just read.</summary>
    public static bool mark(Grid grid, Vehicle v)
    {
        bool changed = false;
        foreach (var i in tilesUnder(grid, v)) changed |= Grids.setParked(grid, i, true);
        return changed;
    }

    /// <summary>A car has come to rest (put down, or its driver got out): its tiles are in the way from now, and
    /// whoever stood on them is moved off.</summary>
    public static void park(SimState state, Vehicle v)
    {
        if (!mark(state.grid, v)) return;
        state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
        evict(state, v);
    }

    /// <summary>A car is about to be driven or taken away: its tiles are free again (but for any another parked car is over).</summary>
    public static void unpark(SimState state, Vehicle v)
    {
        var mine = tilesUnder(state.grid, v);
        if (mine.Count == 0) return;
        // What other parked cars near enough to share a tile still cover.
        var kept = new HashSet<int>();
        double far = shapeOf(v).halfLength + TRUCK.halfLength + Motion.SUB;
        foreach (var other in vehiclesOf(state))
        {
            if (other == v || !isParked(other) || Motion.distance2(other.at, v.at) > far * far) continue;
            foreach (var i in tilesUnder(state.grid, other)) kept.Add(i);
        }
        bool changed = false;
        foreach (var i in mine)
        {
            if (!kept.Contains(i)) changed |= Grids.setParked(state.grid, i, false);
        }
        if (changed) state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
    }

    /// <summary>The whole layer again from the cars as they are: after a state is read (no bump: nothing was planned
    /// on it yet), or with `bump` after cars were moved by hand (a test, a debug scene).</summary>
    public static void remask(SimState state, bool bump = false)
    {
        var grid = state.grid;
        Grids.clearParked(grid);
        foreach (var v in vehiclesOf(state))
        {
            if (isParked(v)) mark(grid, v);
        }
        if (bump) state.walkabilityVersion = Js.I(state.walkabilityVersion + 1);
    }

    /// <summary>Take a car out of the world (a debug scene's, a test's): its tiles freed, then gone from the list.</summary>
    public static void remove(SimState state, Vehicle v)
    {
        if (isParked(v)) unpark(state, v);
        state.vehicles?.Remove(v);
    }

    /// <summary>A body of radius r at `at`, moved out of any parked car's body it overlaps (the body: a strip as wide
    /// as the car with round ends, wider than the tiles under it), straight away from the car's middle line. For
    /// you on foot (ControlMod): the dead and animals stop at the tiles, which is what their paths are made of.</summary>
    public static Point pushOut(SimState state, Point at, int r)
    {
        var grid = state.grid;
        if (grid.parkedCount == 0) return at;
        // Nothing to do unless a tile under a parked car is within a tile of the body's box.
        int reach = r + Motion.SUB;
        int x0 = (int)JsMath.floor((double)(at.x - reach) / Motion.SUB), x1 = (int)JsMath.floor((double)(at.x + reach) / Motion.SUB);
        int y0 = (int)JsMath.floor((double)(at.y - reach) / Motion.SUB), y1 = (int)JsMath.floor((double)(at.y + reach) / Motion.SUB);
        bool near = false;
        for (int y = y0; y <= y1 && !near; y++)
        {
            for (int x = x0; x <= x1 && !near; x++) near = Grids.isParked(grid, x, y);
        }
        if (!near) return at;
        double px = at.x, py = at.y;
        foreach (var v in vehiclesOf(state))
        {
            if (!isParked(v)) continue;
            var shape = shapeOf(v);
            double far = shape.halfLength + r;
            if (Motion.distance2(v.at, at) > far * far) continue;
            // The car's middle line, from its tail circle's centre to its nose circle's.
            var body = circles(shape, v.at, v.heading);
            Point a = body[0], b = body[^1];
            double lx = b.x - a.x, ly = b.y - a.y;
            double t = JsMath.max(0, JsMath.min(1, ((px - a.x) * lx + (py - a.y) * ly) / (lx * lx + ly * ly)));
            double cx = JsMath.round(a.x + lx * t), cy = JsMath.round(a.y + ly * t);
            double ox = px - cx, oy = py - cy;
            double keep = shape.halfWidth + r;
            if (ox * ox + oy * oy >= keep * keep) continue;
            if (ox == 0 && oy == 0) continue; // on the line itself: the tiles keep anyone off it
            var o = Motion.toward(Js.I(ox), Js.I(oy), keep);
            px = cx + o.x;
            py = cy + o.y;
        }
        return new Point { x = Js.I(px), y = Js.I(py) };
    }

    /// <summary>Does a body (a circle of radius r) reach into a tile a parked car is over?</summary>
    public static bool overlapsParked(Grid grid, Point at, int r = Motion.BODY_RADIUS)
    {
        if (grid.parkedCount == 0) return false;
        int x0 = (int)JsMath.floor((double)(at.x - r) / Motion.SUB), x1 = (int)JsMath.floor((double)(at.x + r) / Motion.SUB);
        int y0 = (int)JsMath.floor((double)(at.y - r) / Motion.SUB), y1 = (int)JsMath.floor((double)(at.y + r) / Motion.SUB);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (Grids.isParked(grid, x, y) && touchesTile(at, r, x, y)) return true;
            }
        }
        return false;
    }

    /// <summary>Whoever is where a car has come to rest goes to the nearest tile they can stand on clear of it: a
    /// tile walker whose tile it is over, a free mover whose body overlaps a tile it is over.</summary>
    static void evict(SimState state, Vehicle v)
    {
        var grid = state.grid;
        double near = shapeOf(v).halfLength + 2 * Motion.SUB;
        bool open(int x, int y) => Pathfinding.isPassable(grid, x, y, Pathfinding.FREE) && !overlapsParked(grid, Motion.centre(new Point { x = x, y = y }));
        void move(Body e, bool free)
        {
            if (Motion.distance2(e.at, v.at) > near * near) return;
            if (!(free ? overlapsParked(grid, e.at) : Grids.isParked(grid, e.pos.x, e.pos.y))) return;
            var to = Space.nearestTile(grid, e.pos, open, 400);
            if (to != null) Motion.place(e, to);
        }
        var you = ControlMod.controlled(state);
        foreach (var s in state.survivors)
        {
            if (vehicleOf(state, s) == null) move(s, s == you);
        }
        foreach (var z in state.zombies) move(z, true);
        foreach (var d in state.dogs) move(d, true);
        foreach (var a in state.animals) move(a, true);
        foreach (var r in state.residents) move(r, false);
    }
}
