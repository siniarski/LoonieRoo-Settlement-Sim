// E (or right-click) in first person: use what the crosshair is on, within arm's reach (2026-11-11, after the
// player's first playtest found E missing drawers and cars).
//
// The look is followed out from the eye in short steps, up and down as well as across, for USE_REACH metres along
// the ground; each tile it passes over is a candidate, nearest first. It stops at the first of: the ground (that
// tile), a parked car's body under its roof (that tile), an edge (a wall, a door, a window, a fence: the tile beyond,
// so `use` works the door or smashes the glass), a wall tile. A candidate is offered to the inventory screen (a
// searched container opens on it) and then to the sim's `use` (Commands: the first that does something wins; a
// refused `use` changes nothing, and is not logged). After those, the tile just in front by the way you face, then the
// one under you (where a drawer you stand over opens, and E on bare ground forages or sleeps, as before). A car with
// its boot open, you behind it, opens the boot on the inventory screen before any of it.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    /// <summary>E or right-click in first person: an open boot behind you, else what the crosshair is on within
    /// reach, else what's in front, else underfoot.</summary>
    void UseAhead()
    {
        var you = ControlMod.controlled(state);
        if (you == null) return;
        var at = you.pos;
        if (Vehicles.vehicleOf(state, you) != null) { Command(new Command { type = "car" }); return; }
        if (OpenBootBeside(you)) return;
        // A car within reach that you face: in you get, whatever tile the crosshair is on.
        if (Vehicles.vehicleNear(state, you.at) is { driverId: null } car)
        {
            var look = World3D.FirstPerson.LookDir(view.yaw, view.pitch);
            double dx = car.at.x - you.at.x, dy = car.at.y - you.at.y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 0 && (look.X * dx + look.Z * dy) / d > 0.2 && Command(new Command { type = "car" })) { view.Toast("getting in", 1200); return; }
        }
        foreach (var t in UseTiles(you))
        {
            if (!ControlMod.nextTo(at, t) || Space.samePoint(at, t)) continue;
            if (inventory.OpenAt(state, t) || Command(new Command { type = "use", x = t.x, y = t.y })) return;
        }
        var front = FrontTile(you);
        if (inventory.OpenAt(state, front) || Command(new Command { type = "use", x = front.x, y = front.y })) return;
        if (!inventory.OpenAt(state, at)) Command(new Command { type = "use", x = at.x, y = at.y });
    }

    /// <summary>The boot of the car beside you, open, with you at it (its back tile beside you, nearer you than its
    /// driver's door): the inventory screen on it. Returns whether it did.</summary>
    bool OpenBootBeside(Survivor you)
    {
        var car = Vehicles.vehicleNear(state, you.at);
        if (car == null || !car.bootOpen || car.driverId != null || !Commands.besideYou(you, Vehicles.bootTile(car))) return false;
        var boot = Vehicles.anchorAt(car, "boot").at;
        var door = Vehicles.anchorAt(car, "driverDoor").at;
        if (Motion.distance2(you.at, boot) > Motion.distance2(you.at, door)) return false;
        view.openContainer = car.id;
        view.showInventory = true;
        return true;
    }

    /// <summary>The tiles the look passes over within reach, nearest first, the one you stand on included (UseAhead
    /// skips it): see the top of the file.</summary>
    List<Point> UseTiles(Survivor you)
    {
        var tiles = new List<Point>();
        var (eye, _, _) = World3D.FirstPerson.Eye(state, you, alpha);
        var dir = World3D.FirstPerson.LookDir(view.yaw, view.pitch);
        double flat = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        if (flat < 1e-4) return tiles;
        const double STEP = 0.04;
        var grid = state.grid;
        var last = new Point((int)Math.Floor(eye.X), (int)Math.Floor(eye.Z));
        tiles.Add(last);
        // Along the ground USE_REACH at most: STEP of 3D distance moves flat * STEP across it.
        for (double d = STEP; d * flat <= USE_REACH; d += STEP)
        {
            float x = eye.X + dir.X * (float)d, y = eye.Y + dir.Y * (float)d, z = eye.Z + dir.Z * (float)d;
            var t = new Point((int)Math.Floor(x), (int)Math.Floor(z));
            bool stop = false;
            if (t.x != last.x || t.y != last.y)
            {
                if (!Grids.inBounds(grid, t.x, t.y)) break;
                tiles.Add(t);
                // An edge between (a wall, a door, a window, a fence), or a wall tile: that's as far as the hand goes.
                int dx = Math.Sign(t.x - last.x), dy = Math.Sign(t.y - last.y);
                bool edge = (dx != 0 && Grids.edgeKindBetween(grid, last.x, last.y, last.x + dx, last.y) != Grids.EDGE_NONE)
                    || (dy != 0 && Grids.edgeKindBetween(grid, last.x, last.y, last.x, last.y + dy) != Grids.EDGE_NONE);
                stop = edge || Grids.getTile(grid, t.x, t.y) == Grids.WALL;
                last = t;
            }
            if (stop || y <= World3D.Terrain.Height(x, z) || InCarBody(x, y, z)) break;
        }
        return tiles;
    }

    /// <summary>Is a point (m) inside a car's body, under its roof?</summary>
    bool InCarBody(float x, float y, float z)
    {
        var p = new Point((int)Math.Round(x * Motion.SUB), (int)Math.Round(z * Motion.SUB));
        foreach (var v in Vehicles.vehiclesOf(state))
            if (Vehicles.gap(v, p) <= 0 && y - World3D.Terrain.Height(x, z) <= Vehicles.topAt(v, p) + 0.05) return true;
        return false;
    }
}
