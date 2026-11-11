// The self-test's steps for E and Space at things (2026-11-11, the player's first playtest; InteractSteps is called from
// SelfTest.cs after the roof's): walked up to with W, looked at, E gets you into a parked car from any side (W still
// held too), U and I open its boot and bonnet from behind and in front (the boot's contents on the inventory screen),
// E opens a drawer looked at straight on or stood over, takes a rag off the floor; walking, a broken window and a
// yard fence stop you, and Space vaults them with the body and the eye going up and over. The fixtures (a car, a
// dresser, a tin, the window and fence edges) are set straight on the state on open ground near you, and taken away.

using System;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    async Task InteractSteps()
    {
        var you = ControlMod.controlled(state)!;
        var (pos, at) = (you.pos, you.at);
        double yaw0 = view.yaw, pitch0 = view.pitch;
        bool paused = view.paused;
        if (OpenGround(you.pos, 15, 11) is not { } o) { Check("Open ground near you for the interaction fixtures", false); return; }
        view.paused = false;
        int cx = o.x + 7, cy = o.y + 5;

        // A sedan along the row, heading east; you walk up to it from each side, look at its middle, and press E.
        var car = Staging.car(state, cx, cy, 0);
        car.lights = false;
        var mid = car.at;
        foreach (var (name, fx, fy, keepW) in new[] {
            ("its left side, W still held", 0.0, -3.0, true), ("its right side", 0.3, 3.0, false), ("behind it", -4.5, 0.2, false),
            ("in front of it", 4.5, -0.2, false), ("its back corner", -3.2, -2.2, false), ("its front corner", 3.0, 2.4, false) })
        {
            await WalkUpTo(mid.x / (double)Motion.SUB + fx, mid.y / (double)Motion.SUB + fy, mid, -0.3, keepW);
            await Tap(Godot.Key.E);
            string started = state.control!.busy?.what ?? "-";
            for (int i = 0; i < 400 && Vehicles.vehicleOf(state, you) == null; i++) await Frames(1);
            SendKey(Godot.Key.W, false);
            Check($"E at a parked car from {name} gets you in", Vehicles.vehicleOf(state, you) == car,
                $"started {started}, note {you.note}, {Vehicles.gap(car, you.at):0} SUB from the body");
            if (Vehicles.vehicleOf(state, you) != null)
            {
                await Tap(Godot.Key.E);
                for (int i = 0; i < 400 && Vehicles.vehicleOf(state, you) != null; i++) await Frames(1);
            }
            await Frames(3);
        }

        // The boot from behind: U opens it and the inventory shows what is in it; E there opens it too. I, in front: the bonnet.
        await WalkUpTo(mid.x / (double)Motion.SUB - 4.5, mid.y / (double)Motion.SUB, mid, -0.3, false);
        var tin = Items.spawnItem(state, "food", you.pos.x, you.pos.y);
        Commands.applyCommand(state, new Command { type = "take", itemId = tin.id });
        var stashed = Commands.applyCommand(state, new Command { type = "stash", itemId = tin.id, containerId = car.id });
        await Tap(Godot.Key.U);
        for (int i = 0; i < 200 && !car.bootOpen; i++) await Frames(1);
        await Frames(4);
        Check("U behind a car opens its boot, and the inventory screen shows what's in it", car.bootOpen && view.showInventory && view.openContainer == car.id && stashed.ok,
            $"boot {(car.bootOpen ? "open" : "shut")}, screen {(view.showInventory ? "up on " + (view.openContainer ?? "none") : "down")}, stashed {stashed.ok} {stashed.reason}");
        await Tap(Godot.Key.Tab);
        await Frames(2);
        await Tap(Godot.Key.E);
        await Frames(4);
        Check("E behind a car with its boot open opens the boot (not the driver's door)", view.showInventory && view.openContainer == car.id && state.control!.busy?.what != "getIn",
            $"screen {(view.showInventory ? "up on " + (view.openContainer ?? "none") : "down")}, busy {state.control!.busy?.what ?? "-"}");
        if (view.showInventory) await Tap(Godot.Key.Tab);
        await Tap(Godot.Key.U);
        for (int i = 0; i < 200 && car.bootOpen; i++) await Frames(1);
        await WalkUpTo(mid.x / (double)Motion.SUB + 4.5, mid.y / (double)Motion.SUB, mid, -0.3, false);
        await Tap(Godot.Key.I);
        for (int i = 0; i < 200 && !car.hoodOpen; i++) await Frames(1);
        Check("I in front of a car lifts its bonnet", car.hoodOpen, $"note {you.note}, {Vehicles.gap(car, you.at):0} SUB from the body at {you.at.x},{you.at.y}, car at {car.at.x},{car.at.y}");
        car.hoodOpen = false;
        Vehicles.remove(state, car);
        await Frames(2);

        // A searched dresser: E opens it looked at straight on from the tile in front, from close up, and stood over its tile.
        var dresser = FurnitureMod.addFurniture(state, "crate", cx, cy);
        dresser.look = "dresser";
        dresser.loot = 0;
        var drawer = Motion.centre(new Point(cx, cy));
        foreach (var (name, fy, pitch) in new[] { ("from the tile in front, looking straight at it", 1.0, -0.15), ("from close up, looking at its top", 0.62, -0.6),
            ("standing over its tile", 0.35, -0.9), ("from a tile off to the side", 1.05, -0.35) })
        {
            double fx = name.Contains("side") ? 1.0 : 0;
            Staging.place(you, cx, cy);
            you.at = new Point { x = Js.I(drawer.x + fx * Motion.SUB), y = Js.I(drawer.y + fy * Motion.SUB) };
            you.was = you.at;
            you.pos = Motion.tileOf(you.at);
            view.yaw = Math.Atan2(drawer.y - you.at.y, drawer.x - you.at.x);
            view.pitch = pitch;
            await Frames(3);
            await Tap(Godot.Key.E);
            await Frames(3);
            Check($"E at a searched dresser {name} opens it", view.showInventory && view.openContainer == dresser.id, $"screen {(view.showInventory ? "up on " + (view.openContainer ?? "none") : "down")}, note {you.note}");
            if (view.showInventory) await Tap(Godot.Key.Tab);
        }
        FurnitureMod.removeFurniture(state, dresser);

        // A rag on the floor in front of you: looked at, E takes it (E on food eats it).
        var floorTin = Items.spawnItem(state, "cloth", cx, cy);
        Staging.place(you, cx, cy + 1);
        view.yaw = -Math.PI / 2;
        view.pitch = -0.75;
        await Frames(3);
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("E at cloth on the floor in front of you takes it", floorTin.carriedBy == you.id, $"note {you.note}");

        // A broken window, then a yard fence (planks: a barricade edge), on the west side of (cx, cy): W stops you at
        // it; Space vaults you over, the body and the eye going up and coming down on the far side.
        foreach (var (name, kind) in new[] { ("a broken window", Grids.EDGE_WINDOW_BROKEN), ("a yard fence", Grids.EDGE_BARRICADE) })
        {
            Grids.setEdge(state.grid, cx, cy, "w", kind);
            state.walkabilityVersion++;
            Staging.place(you, cx - 2, cy);
            // LEAP_SHOT=prefix (People.Leap.cs's pictures of your first two leaps): watched from the side, so you look
            // north and strafe east with D (the camera looks the way you do), the eye 4 m south of the window.
            bool side = OS.GetEnvironment("LEAP_SHOT") is { Length: > 0 };
            var walk = side ? Godot.Key.D : Godot.Key.W;
            view.yaw = side ? -Math.PI / 2 : 0;
            view.pitch = 0;
            if (side) firstPerson.EyeAt = new Godot.Vector3(cx - 0.2f, 1.2f, cy + 4.5f);
            await Frames(3);
            SendKey(walk, true);
            for (int t0 = state.tick, i = 0; state.tick < t0 + 40 && i < 3000; i++) await Frames(1);   // 4 s: two tiles and more
            int stoppedX = you.at.x;
            bool stopped = you.pos.x == cx - 1 && you.at.x <= cx * Motion.SUB - Motion.BODY_RADIUS;
            var ppl = firstPerson.People;
            float eye0 = World3D.FirstPerson.Eye(state, you, 1).eye.Y, eyeTop = eye0, bodyTop = 0;
            await Tap(Godot.Key.Space);
            string leap = state.control!.busy?.what ?? "-";
            for (int i = 0; i < 120 && Leaps.airborne(state.control!); i++)
            {
                await Frames(1);
                eyeTop = Math.Max(eyeTop, World3D.FirstPerson.Eye(state, you, 0.5).eye.Y);
                bodyTop = Math.Max(bodyTop, ppl.YourLift);
            }
            SendKey(walk, false);
            await Frames(3);
            Check($"Walking, {name} stops you; Space vaults it, the eye and your body going up and over", stopped && leap == "vault" && you.pos.x >= cx && eyeTop > eye0 + 0.2f && bodyTop > 0.3f,
                $"stopped at x {stoppedX} ({you.pos.x}), {leap}, eye up {eyeTop - eye0:0.00} m, body up {bodyTop:0.00} m");
            firstPerson.EyeAt = null;
            Grids.setEdge(state.grid, cx, cy, "w", Grids.EDGE_NONE);
            state.walkabilityVersion++;
        }

        Staging.place(you, pos.x, pos.y);
        you.at = at;
        you.was = at;
        view.yaw = yaw0;
        view.pitch = pitch0;
        view.paused = paused;
        await Frames(3);
    }

    /// <summary>Put you at (x, y) (tiles), looking at `target`, then hold W until you stop against it (or 6 s). W stays
    /// held if `keepW`.</summary>
    async Task WalkUpTo(double x, double y, Point target, double pitch, bool keepW)
    {
        var you = ControlMod.controlled(state)!;
        var at = new Point { x = Js.I(Math.Round(x * Motion.SUB)), y = Js.I(Math.Round(y * Motion.SUB)) };
        Staging.place(you, Motion.tileOf(at).x, Motion.tileOf(at).y);
        you.at = at;
        you.was = at;
        view.yaw = Math.Atan2(target.y - at.y, target.x - at.x);
        view.pitch = pitch;
        await Frames(2);
        SendKey(Godot.Key.W, true);
        // Until you haven't moved for 5 ticks (frames are faster than ticks), or 6 s.
        int still = 0, tick = state.tick;
        var was = you.at;
        for (int i = 0; i < 2000 && still < 5 && state.tick < tick + 60; i++)
        {
            int t = state.tick;
            await Frames(1);
            if (state.tick == t) continue;
            view.yaw = Math.Atan2(target.y - you.at.y, target.x - you.at.x);
            still = (you.at.x == was.x && you.at.y == was.y) ? still + 1 : 0;
            was = you.at;
        }
        if (!keepW) SendKey(Godot.Key.W, false);
        await Frames(3);
    }

    /// <summary>The top-left of a w x h patch of open ground near `from` (floor you can walk, no furniture, cars,
    /// items, edges or water), or null.</summary>
    Point? OpenGround(Point from, int w, int h)
    {
        var g = state.grid;
        bool open(int x0, int y0)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    if (!Grids.inBounds(g, x, y) || !Pathfinding.isPassable(g, x, y, Pathfinding.FREE) || Grids.getTile(g, x, y) != Grids.FLOOR
                        || FurnitureMod.furnitureAt(state, x, y) != null || Items.itemAt(state, x, y) != null || Grids.isParked(g, x, y)) return false;
                    if (Grids.edgeKind(g, Grids.edgeIndex(g, x, y, "n")) != Grids.EDGE_NONE || Grids.edgeKind(g, Grids.edgeIndex(g, x, y, "w")) != Grids.EDGE_NONE) return false;
                }
            return true;
        }
        for (int r = 0; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (open(from.x + dx, from.y + dy)) return new Point(from.x + dx, from.y + dy);
                }
        return null;
    }
}
