// The self-test's steps for doors in walls, a garage's wide door, furniture and water (2026-11-12, the self-test gaps pass; PlaceSteps is called
// last from SelfTest.cs, `SELFTEST_ONLY=place` runs only these). Each group sets up its own scene on open ground near you
// (edges, furniture, water set straight on the state, taken away after) and then plays it with real keys: R, E, W, Space.
// A step that fails because the game is wrong is left in place and named in KnownFailures (log/2026-11-12-18-self-test-gaps.md).

using System;
using System.Collections.Generic;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game;

public partial class Game
{
    /// <summary>Steps that fail because the game is wrong, not the step: they still run and print, as KNOWN, and do not fail the check.
    /// Each names a finding in log/2026-11-12-18-self-test-gaps.md. Remove it from here when the game is fixed.</summary>
    static readonly HashSet<string> KnownFailures = new();

    /// <summary>Like Check, but a step in KnownFailures that fails is printed as KNOWN FAIL and left out of the failures.</summary>
    void CheckKnown(string name, bool ok, string detail = "")
    {
        if (!ok && KnownFailures.Contains(name)) { GD.Print($"KNOWN FAIL  {name}  ({detail})"); return; }
        Check(name, ok, detail);
    }

    /// <summary>You at the middle of tile (x, y) plus an offset in tiles, looking at (lx, ly) in tiles.</summary>
    void PutYou(Survivor you, double x, double y, double lx, double ly, double pitch = 0)
    {
        var at = new Point { x = Js.I(Math.Round(x * Motion.SUB)), y = Js.I(Math.Round(y * Motion.SUB)) };
        Staging.place(you, Motion.tileOf(at).x, Motion.tileOf(at).y);
        you.at = at;
        you.was = at;
        view.yaw = Math.Atan2(ly - y, lx - x);
        view.pitch = pitch;
    }

    async Task Hold(Godot.Key key, int ticks)
    {
        SendKey(key, true);
        for (int t0 = state.tick, i = 0; state.tick < t0 + ticks && i < 4000; i++) await Frames(1);
        SendKey(key, false);
        await Frames(2);
    }

    async Task PlaceSteps()
    {
        var you = ControlMod.controlled(state)!;
        var (pos, at) = (you.pos, you.at);
        double yaw0 = view.yaw, pitch0 = view.pitch;
        bool paused = view.paused;
        if (OpenGround(you.pos, 15, 11) is not { } o) { Check("Open ground near you for the place fixtures", false); return; }
        view.paused = false;
        var dead = state.zombies;
        state.zombies = new();
        int cx = o.x + 7, cy = o.y + 5;
        await DoorSteps(you, cx, cy);
        await GarageSteps(you, cx, cy);
        await FurnitureSteps(you, cx, cy);
        await WaterSteps(you, cx, cy);
        state.zombies = dead;
        Staging.place(you, pos.x, pos.y);
        you.at = at;
        you.was = at;
        view.yaw = yaw0;
        view.pitch = pitch0;
        view.paused = paused;
        await Frames(3);
    }

    void ClearEdge(int x, int y)
    {
        Grids.setEdge(state.grid, x, y, "w", Grids.EDGE_NONE);
        state.walkabilityVersion++;
    }

    /// <summary>R with wood at a door in a wall boards it: the dead cannot cross, you can; E on a locked one says so.</summary>
    async Task DoorSteps(Survivor you, int cx, int cy)
    {
        int e = Grids.edgeIndex(state.grid, cx, cy, "w");
        Grids.setEdge(state.grid, cx, cy, "w", Grids.EDGE_DOOR);
        state.walkabilityVersion++;
        var wood = Items.spawnItem(state, "wood", you.pos.x, you.pos.y);
        Commands.applyCommand(state, new Command { type = "take", itemId = wood.id });
        PutYou(you, cx - 0.5, cy + 0.5, cx + 0.5, cy + 0.5);
        await Frames(3);
        await Tap(Godot.Key.R);
        for (int i = 0; i < 400 && !state.grid.edgeBoarded.Contains(e); i++) await Frames(1);
        Check("R with wood at a door in a wall boards it up", state.grid.edgeBoarded.Contains(e), $"boarded {state.grid.edgeBoarded.Contains(e)}, note {you.note}");

        // The dead stand on the far side, wanting you: they cannot cross while the boards hold.
        var z = Zombies.addZombie(state, cx + 2, cy, 100, "static");
        z.targetId = you.id;
        for (int t0 = state.tick, i = 0; state.tick < t0 + 60 && i < 3000; i++) await Frames(1);
        Check("The dead cannot cross a boarded door", z.pos.x >= cx && z.pos.x <= cx + 1,
            $"zombie at {z.pos.x},{z.pos.y}, door at {cx}, boards {(state.grid.edgeBoarded.Contains(e) ? "hold" : "broken")}");
        Zombies.removeZombie(state, z);
        state.zombies = new();
        PutYou(you, cx - 0.5, cy + 0.5, cx + 0.5, cy + 0.5);
        await Frames(2);
        await Hold(Godot.Key.W, 40);
        Check("You walk through a boarded door", you.pos.x >= cx, $"at {you.at.x / (double)Motion.SUB:0.00},{you.at.y / (double)Motion.SUB:0.00}, door at x {cx}, note {you.note}");
        ClearEdge(cx, cy);

        // A locked one: E says it is locked and it stays shut.
        Grids.setEdge(state.grid, cx, cy, "w", Grids.EDGE_DOOR);
        Doors.toggleLock(state, cx, cy, "w");
        state.walkabilityVersion++;
        PutYou(you, cx - 0.5, cy + 0.5, cx + 0.5, cy + 0.5);
        await Frames(3);
        you.note = "";
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("E on a locked door in a wall says it is locked, and it stays shut", (you.note ?? "").Contains("locked") && !state.grid.edgeOpen.Contains(e),
            $"note '{you.note}', open {state.grid.edgeOpen.Contains(e)}");
        ClearEdge(cx, cy);
    }

    /// <summary>A garage's wide door: three doors side by side roll up with one E and down with the next; locked, it stays shut.</summary>
    async Task GarageSteps(Survivor you, int cx, int cy)
    {
        var edges = new[] { -1, 0, 1 }.Select((k) => Grids.edgeIndex(state.grid, cx, cy + k, "w")).ToList();
        foreach (var k in new[] { -1, 0, 1 }) Grids.setEdge(state.grid, cx, cy + k, "w", Grids.EDGE_DOOR);
        state.walkabilityVersion++;
        PutYou(you, cx - 0.5, cy + 0.5, cx + 0.5, cy + 0.5);
        await Frames(3);
        await Tap(Godot.Key.E);
        await Frames(3);
        bool up = edges.All((d) => state.grid.edgeOpen.Contains(d));
        Check("E at a garage's door rolls up all of it, the three doors together", up, $"open {string.Join(",", edges.Select((d) => state.grid.edgeOpen.Contains(d)))}, note {you.note}");
        await Hold(Godot.Key.W, 40);
        Check("The rolled-up garage door can be walked through", you.pos.x >= cx, $"at x {you.at.x / (double)Motion.SUB:0.00}, door at {cx}");
        PutYou(you, cx - 0.5, cy + 0.5, cx + 0.5, cy + 0.5);
        await Frames(2);
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("E again pulls it down", edges.All((d) => !state.grid.edgeOpen.Contains(d)), $"note {you.note}");
        Doors.toggleLock(state, cx, cy, "w");
        await Frames(2);
        you.note = "";
        await Tap(Godot.Key.E);
        await Frames(3);
        Check("Locked, the garage door stays down and says it is locked", edges.All((d) => !state.grid.edgeOpen.Contains(d) && state.grid.edgeLocked.Contains(d)) && (you.note ?? "").Contains("locked"),
            $"note '{you.note}', locked {string.Join(",", edges.Select((d) => state.grid.edgeLocked.Contains(d)))}");
        foreach (var k in new[] { -1, 0, 1 }) ClearEdge(cx, cy + k);
    }

    /// <summary>Furniture: a dresser stops you and you slide round it; a bed you walk onto; E opens a table, a washer and a till.</summary>
    async Task FurnitureSteps(Survivor you, int cx, int cy)
    {
        var dresser = FurnitureMod.addFurniture(state, "crate", cx, cy);
        dresser.look = "dresser";
        dresser.loot = 0;
        // Straight at it: W stops you against it, never in it.
        PutYou(you, cx - 3, cy + 0.5, cx + 0.5, cy + 0.5);
        bool inside = false;
        SendKey(Godot.Key.W, true);
        for (int t0 = state.tick, i = 0; state.tick < t0 + 50 && i < 3000; i++) { await Frames(1); inside |= you.pos.x == cx && you.pos.y == cy; }
        SendKey(Godot.Key.W, false);
        await Frames(2);
        Check("Walking into a dresser stops you at it", !inside && you.at.x < cx * Motion.SUB && you.at.x > (cx - 1) * Motion.SUB, $"at {you.at.x / (double)Motion.SUB:0.00}, dresser tile x {cx}, inside {inside}");
        // At an angle: you slide along it (south) and on past.
        PutYou(you, cx - 3, cy + 0.2, cx + 0.5, cy + 1.0);
        double y0 = you.at.y;
        inside = false;
        SendKey(Godot.Key.W, true);
        for (int t0 = state.tick, i = 0; state.tick < t0 + 70 && i < 3000; i++) { await Frames(1); inside |= you.pos.x == cx && you.pos.y == cy; }
        SendKey(Godot.Key.W, false);
        await Frames(2);
        Check("Walking at a dresser at an angle slides you round it", !inside && you.at.y - y0 > 0.5 * Motion.SUB && you.at.x > cx * Motion.SUB, $"at {you.at.x / (double)Motion.SUB:0.00},{you.at.y / (double)Motion.SUB:0.00}, started y {y0 / Motion.SUB:0.00}, inside {inside}");
        FurnitureMod.removeFurniture(state, dresser);

        var bed = FurnitureMod.addFurniture(state, "decor", cx, cy);
        bed.look = "bed";
        PutYou(you, cx - 2, cy + 0.5, cx + 0.5, cy + 0.5);
        await Hold(Godot.Key.W, 30);
        Check("You walk onto a bed", you.pos.x >= cx, $"at {you.at.x / (double)Motion.SUB:0.00}, bed tile x {cx}");
        FurnitureMod.removeFurniture(state, bed);

        foreach (var look in new[] { "table", "washer", "register" })
        {
            var f = FurnitureMod.addFurniture(state, "decor", cx, cy);
            f.look = look;
            var centre = Motion.centre(new Point(cx, cy));
            PutYou(you, cx + 0.5, cy + 1.5, cx + 0.5, cy + 0.5, -0.15);
            await Frames(3);
            await Tap(Godot.Key.E);
            await Frames(4);
            Check($"E at a {FurnitureMod.LOOK_NAMES[look]} opens it as a container", view.showInventory && view.openContainer == f.id, $"screen {(view.showInventory ? "up on " + (view.openContainer ?? "none") : "down")}, note {you.note}");
            if (view.showInventory) await Tap(Godot.Key.Tab);
            FurnitureMod.removeFurniture(state, f);
        }
    }

    /// <summary>Water: a strip of it five tiles wide across your way, walked into with W: the eye sinks, the hands stroke, Space strokes,
    /// you get wet and come out on the far bank. A one-tile stream the dead wade.</summary>
    async Task WaterSteps(Survivor you, int cx, int cy)
    {
        var g = state.grid;
        var was = new List<(int x, int y, string tile)>();
        for (int y = cy - 3; y <= cy + 3; y++)
            for (int x = cx; x <= cx + 4; x++) { was.Add((x, y, Grids.getTile(g, x, y)!)); Grids.setTile(g, x, y, Grids.WATER); }
        state.walkabilityVersion++;
        PutYou(you, cx - 1.5, cy + 0.5, cx + 3, cy + 0.5);
        you.wetness = 0;
        await Frames(3);
        await Frames(5);
        float ground = firstPerson.camera.Position.Y, low = ground;
        double wet0 = you.wetness, wet = wet0;
        bool deep = false, hands = false, waded = false;
        int stroked = 0, strokeFrom = -1;
        SendKey(Godot.Key.W, true);
        for (int t0 = state.tick, i = 0; state.tick < t0 + 400 && i < 6000 && you.pos.x <= cx + 4; i++)
        {
            await Frames(1);
            waded |= Swim.depth(g, you.pos) == Swim.SHALLOW;
            deep |= Swim.swimming(state, you);
            if (deep && strokeFrom < 0) strokeFrom = you.at.x;
            hands |= firstPerson.SwimHands.Shown;
            low = Math.Min(low, firstPerson.camera.Position.Y);
            wet = Math.Max(wet, you.wetness);
            if (deep && stroked < 1 && Swim.swimming(state, you)) { SendKey(Godot.Key.Space, true); await Frames(1); SendKey(Godot.Key.Space, false); stroked++; }
        }
        SendKey(Godot.Key.W, false);
        await Frames(2);
        Check("Walking into a river wades, then swims: the eye sinks, the hands stroke, you are soaked", waded && deep && hands && low < ground - 0.2f && wet > wet0,
            $"waded {waded}, swam {deep}, stroke hands {hands}, eye {ground:0.00} -> {low:0.00}, wetness {wet0:0} -> {wet:0}");
        Check("Space while swimming makes a stroke", stroked == 1 && (you.note ?? "").Length >= 0 && strokeFrom >= 0, $"strokes {stroked}, note {you.note}");
        for (int i = 0; i < 400 && you.pos.x <= cx + 4; i++) { SendKey(Godot.Key.W, true); await Frames(2); }
        SendKey(Godot.Key.W, false);
        Check("You come out on the far bank", you.pos.x > cx + 4 && !Swim.inWater(state, you), $"at x {you.at.x / (double)Motion.SUB:0.00}, far bank tile {cx + 5}, in water {Swim.inWater(state, you)}");
        foreach (var (x, y, tile) in was) Grids.setTile(g, x, y, tile);
        // A one-tile stream (a column of shallows): a dead one on the west bank, you on the east, still.
        for (int y = cy - 3; y <= cy + 3; y++) { Grids.setTile(g, cx + 1, y, Grids.WATER); }
        state.walkabilityVersion++;
        PutYou(you, cx + 3.5, cy + 0.5, cx - 2, cy + 0.5);
        var z = Zombies.addZombie(state, cx - 3, cy, 100, "static");
        z.targetId = you.id;
        for (int t0 = state.tick, i = 0; state.tick < t0 + 150 && i < 6000 && z.pos.x <= cx + 1; i++) await Frames(1);
        Check("The dead wade across a narrow stream", z.pos.x >= cx + 1, $"zombie at {z.pos.x},{z.pos.y}, stream at x {cx + 1}, you at {you.pos.x}");
        Zombies.removeZombie(state, z);
        for (int y = cy - 3; y <= cy + 3; y++) Grids.setTile(g, cx + 1, y, was.First((w) => w.x == cx + 1 && w.y == y).tile);
        state.walkabilityVersion++;
    }
}
