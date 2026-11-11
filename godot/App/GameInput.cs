// Input into commands (src/ui/input.ts; Game is partial: App/Game.cs lists the parts). Nothing here changes the
// sim except through Command (Commands.applyCommand): keys and the mouse become commands, or change the view.
//
// First person (playing a survivor): the mouse looks, WASD walks the way you face (a "steer" command when the
// direction changes, an "aim" when the look does), click swings or fires, E or right-click uses what you look at.
// The Colony tab (top-down): the keys pan, the mouse paints with the build tool in hand, selects, or sends someone.
// The keys are listed for the player in docs/controls.md.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game
{
    /// <summary>--hold: keys held down from the start (w, a, s, d, shift, space), for screenshots of walking and jumping.</summary>
    string[] holdKeys = Array.Empty<string>();

    Point TileAt(Vector2 p) => screenToTile(view.camera, Vp, p.X, p.Y);
    bool OnMap(Point t) => t.x >= 0 && t.y >= 0 && t.x < state.grid.width && t.y < state.grid.height;

    (int x, int y, string side) EdgeAt(Vector2 p)
    {
        var w = screenToWorld(view.camera, Vp, p.X, p.Y);
        return Tools.edgeNear(w.x, w.y);
    }

    void ApplyEdge((int x, int y, string side) e)
    {
        if (view.tool == null) return;
        string key = $"{e.x},{e.y},{e.side}";
        if (edgePainting is { } ep)
        {
            if (ep.last == key) return;
            edgePainting = (ep.wx, ep.wy, key);
        }
        var cmd = Tools.getTool(view.tool).command(e.x, e.y, paintInstant, e.side);
        if (cmd != null) Command(cmd);
    }

    void Apply(Point tile)
    {
        if (view.tool == null || !OnMap(tile)) return;
        var tool = Tools.getTool(view.tool);
        if (tool.furnish)
        {
            // The planner only proposes; its commands go through the normal command path.
            var plan = Planner.planFurnish(state, tile.x, tile.y);
            foreach (var cmd in plan.commands) Commands.applyCommand(state, cmd);
            view.Toast(string.Join(" ", plan.notes));
        }
        else
        {
            var cmd = tool.command(tile.x, tile.y, paintInstant, null);
            if (cmd != null) Command(cmd);
        }
    }

    void UpdateInput(double frameMs)
    {
        if (view.menu != null || view.showMap) return;
        int dx = 0, dy = 0;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) dy -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) dy += 1;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) dx -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) dx += 1;
        bool shift = Input.IsKeyPressed(Key.Shift);
        // --hold=w,d: those keys held down from the start (a debug option, to see walking in a --shot).
        foreach (var k in holdKeys)
        {
            if (k == "w") dy -= 1; else if (k == "s") dy += 1; else if (k == "a") dx -= 1; else if (k == "d") dx += 1; else if (k == "shift") shift = true;
        }
        var c = state.control;
        // Under direct control the keys walk, and the sim only hears about it when they change.
        if (view.firstPerson)
        {
            // --hold=space: jump again on each landing (to catch a leap in a --shot).
            if (holdKeys.Contains("space") && c != null && !Leaps.airborne(c) && Vehicles.vehicleOf(state, ControlMod.controlled(state)!) == null) Command(new Command { type = "jump" });
            FirstPersonInput(c!, dx, -dy, shift);
            return;
        }
        // Playing someone, in the Colony tab: they stand still, and the keys move the view.
        if (c != null && (c.dx != 0 || c.dy != 0)) Command(new Command { type = "steer", dx = 0, dy = 0 });
        if (dx == 0 && dy == 0) return;
        double distance = KEY_PAN_PX_PER_SEC * frameMs / 1000;
        // panByScreen moves the world with the drag; keys move the camera, so negate.
        view.camera = panByScreen(view.camera, -dx * distance, -dy * distance);
    }

    /// <summary>First person: strafe and forward (-1, 0, 1) walk the way you face; in a car they drive it.</summary>
    void FirstPersonInput(Sim.Control c, int strafe, int forward, bool shift)
    {
        var you = ControlMod.controlled(state);
        if (you == null) return;
        bool inCar = Vehicles.vehicleOf(state, you) != null;
        int dx, dy;
        if (inCar) (dx, dy) = (strafe, -forward);
        else if (strafe == 0 && forward == 0) (dx, dy) = (0, 0);
        else
        {
            // Forward is the yaw; right of it a quarter turn clockwise (the grid's y runs south).
            double fx = Math.Cos(view.yaw), fy = Math.Sin(view.yaw);
            double mx = fx * forward - fy * strafe, my = fy * forward + fx * strafe;
            double a = Math.Round(Math.Atan2(my, mx) / (2 * Math.PI) * FP_STEER_STEPS) * 2 * Math.PI / FP_STEER_STEPS;
            (dx, dy) = ((int)Math.Round(Math.Cos(a) * ControlMod.AIM_MAX), (int)Math.Round(Math.Sin(a) * ControlMod.AIM_MAX));
        }
        bool run = shift && (dx != 0 || dy != 0);
        if (c.dx != dx || c.dy != dy || (c.running == true) != run) Command(new Command { type = "steer", dx = dx, dy = dy, run = run });
        if (inCar) return;
        double step = Math.Round(view.yaw / (2 * Math.PI) * FP_AIM_STEPS) * 2 * Math.PI / FP_AIM_STEPS;
        int x = (int)Math.Round(Math.Cos(step) * ControlMod.AIM_MAX), y = (int)Math.Round(Math.Sin(step) * ControlMod.AIM_MAX);
        if (c.aim == null || c.aim.x != x || c.aim.y != y) Command(new Command { type = "aim", x = x, y = y });
    }

    /// <summary>First person: the tile just in front of you, the way you look.</summary>
    Point FrontTile(Survivor you)
    {
        var (eye, _, _) = World3D.FirstPerson.Eye(state, you, alpha);
        foreach (double d in new[] { 0.8, 1.2 })
        {
            var t = new Point((int)Math.Floor(eye.X + Math.Cos(view.yaw) * d), (int)Math.Floor(eye.Z + Math.Sin(view.yaw) * d));
            if (t.x != you.pos.x || t.y != you.pos.y) return t;
        }
        return you.pos;
    }

    /// <summary>First person: the ground tile under the crosshair, within reach (m), or null.</summary>
    Point? LookedAt(Survivor you, double reach)
    {
        var (eye, _, _) = World3D.FirstPerson.Eye(state, you, alpha);
        var g = World3D.FirstPerson.GroundAt(eye, view.yaw, view.pitch, reach);
        return g is { } p ? new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y)) : null;
    }

    /// <summary>First person takes the mouse before the HUD does: it looks, swings and uses.</summary>
    public override void _Input(InputEvent e)
    {
        if (DropForeignMouse(e)) return;
        if (!view.firstPerson || view.showInventory || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (e is InputEventMouseMotion mm)
        {
            view.yaw = (view.yaw + mm.Relative.X * LOOK_PER_PX) % (2 * Math.PI);
            view.pitch = Math.Clamp(view.pitch - mm.Relative.Y * LOOK_PER_PX, -1.45, 1.45);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton mb && mb.Pressed)
        {
            var you = ControlMod.controlled(state);
            if (you != null && mb.ButtonIndex == MouseButton.Left)
            {
                if (Weapons.loadedGun(state, you) != null)
                {
                    // A gun with rounds fires along exactly where you look (a direction, fine to 1/4096).
                    int ax = (int)Math.Round(Math.Cos(view.yaw) * Shooting.AIM_VECTOR_MAX), ay = (int)Math.Round(Math.Sin(view.yaw) * Shooting.AIM_VECTOR_MAX);
                    Command(new Command { type = "fire", x = ax, y = ay });
                }
                else
                {
                    var t = FrontTile(you);
                    Command(new Command { type = "swing", x = t.x, y = t.y });
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right) UseAhead();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            OnKey(k);
            return;
        }
        if (view.menu != null || view.showMap || view.firstPerson) return;
        if (e is InputEventMouseMotion mm) OnMouseMove(mm.Position);
        else if (e is InputEventMouseButton mb)
        {
            mouse = mb.Position;
            if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                view.camera = zoomAt(view.camera, Vp, mb.Position.X, mb.Position.Y, mb.ButtonIndex == MouseButton.WheelUp ? 1.15 : 1 / 1.15);
                view.hover = TileAt(mb.Position);
            }
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) OnLeftDown(TileAt(mb.Position), mb.ShiftPressed);
                else OnLeftUp();
            }
            else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                if (mb.Pressed)
                {
                    panning = mb.Position;
                    if (mb.ButtonIndex == MouseButton.Right) rightPress = (mb.Position, 0);
                }
                else
                {
                    if (mb.ButtonIndex == MouseButton.Right && rightPress is { } rp && rp.travelled < CLICK_SLOP_PX) OnRightClick(TileAt(rp.start));
                    panning = null;
                    rightPress = null;
                }
            }
        }
    }

    void OnMouseMove(Vector2 p)
    {
        mouse = p;
        var tile = TileAt(p);
        view.hover = tile;
        bool edgeTool = view.tool != null && Tools.getTool(view.tool).edge;
        view.hoverEdge = edgeTool ? EdgeAt(p) : null;
        if (edgePainting is { } ep && edgeTool)
        {
            // Along the drag in quarter-tile steps, so a quick stroke doesn't skip a border.
            var w = screenToWorld(view.camera, Vp, p.X, p.Y);
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(w.x - ep.wx), Math.Abs(w.y - ep.wy)) * 4));
            for (int i = 1; i <= steps; i++)
                ApplyEdge(Tools.edgeNear(ep.wx + (w.x - ep.wx) * i / steps, ep.wy + (w.y - ep.wy) * i / steps));
            edgePainting = (w.x, w.y, edgePainting!.Value.last);
        }
        if (view.areaDrag is { } box) view.areaDrag = (box.from, tile);
        if (painting != null && (tile.x != painting.x || tile.y != painting.y))
        {
            // Skip the first point: it was painted on the previous event.
            var line = Tools.tilesOnLine(painting, tile);
            for (int i = 1; i < line.Count; i++) Apply(line[i]);
            painting = tile;
        }
        if (panning is { } from)
        {
            var d = p - from;
            if (rightPress is { } rp) rightPress = (rp.start, rp.travelled + d.Length());
            view.camera = panByScreen(view.camera, d.X, d.Y);
            panning = p;
        }
    }

    void OnLeftDown(Point tile, bool shift)
    {
        if (view.tool != null && Tools.getTool(view.tool).area)
        {
            view.areaDrag = (tile, tile);
            return;
        }
        if (view.tool != null)
        {
            paintInstant = shift;
            if (Tools.getTool(view.tool).edge && mouse is { } m)
            {
                var w = screenToWorld(view.camera, Vp, m.X, m.Y);
                edgePainting = (w.x, w.y, "");
                ApplyEdge(EdgeAt(m));
                return;
            }
            Apply(tile);
            if (Tools.getTool(view.tool).paints) painting = tile;
            return;
        }
        // Select mode: pick a survivor; with someone picked, a click on a bed gives it to them; anywhere else clears the selection.
        var who = Survivors.survivorAt(state, tile.x, tile.y)?.id;
        var f = who == null ? FurnitureMod.furnitureAt(state, tile.x, tile.y) : null;
        if (who == null && f?.kind == "bed" && view.selectedId != null) Command(new Command { type = "assignBed", bedId = f.id, survivorId = view.selectedId });
        else view.selectedId = who;
    }

    void OnLeftUp()
    {
        if (view.areaDrag is { } box)
        {
            view.areaDrag = null;
            var tool = view.tool != null ? Tools.getTool(view.tool) : null;
            if (tool?.boxCommand != null) Command(tool.boxCommand(box.from, box.to));
            else foreach (var t in Tools.tilesInBox(box.from, box.to)) Apply(t);
        }
        painting = null;
        edgePainting = null;
    }

    void OnRightClick(Point tile)
    {
        if (view.tool != null)
        {
            Tools.disarm(view);
            return;
        }
        if (view.selectedId != null && OnMap(tile)) Command(new Command { type = "moveSurvivor", survivorId = view.selectedId, x = tile.x, y = tile.y });
    }

    void OnKey(InputEventKey k)
    {
        var key = k.PhysicalKeycode;
        // The menu takes the keyboard while it's up; Escape closes it once there's a game under it.
        if (view.menu != null)
        {
            if (key == Key.Escape && view.inGame) ToggleMenu();
            return;
        }
        var c = state.control;
        bool controlling = ControlMod.controlled(state) != null;
        if (view.showMap)
        {
            // M or Escape: back to first person, or (playing nobody) to the colony view.
            if (key is Key.Escape or Key.M)
            {
                if (controlling) view.closeMScreen();
                else view.showMap = false;
            }
            return;
        }
        if (view.firstPerson)
        {
            switch (key)
            {
                case Key.Space: if (controlling) Command(new Command { type = "jump" }); return; // jump, vault or hop (Leaps.cs)
                case Key.Tab: view.showInventory = !view.showInventory; return;
                case Key.Escape when view.showInventory: view.showInventory = false; return;
                case Key.Escape: ToggleMenu(); return;
                case Key.M: view.openTab("map"); return;
                // The colony's keys (build, priorities, overview) open its tab first.
                case Key.B or Key.P or Key.O: view.openTab("colony"); view.firstPerson = false; break;
            }
        }
        else if (key == Key.M)
        {
            if (controlling) view.closeMScreen();
            else view.showMap = true;
            return;
        }
        // The Colony tab: Tab opens your bag (unless the build bar has Tab), Esc shuts it.
        if (!view.firstPerson && controlling && ((key == Key.Tab && !view.buildOpen) || (key == Key.Escape && view.showInventory)))
        {
            view.showInventory = key == Key.Tab && !view.showInventory;
            return;
        }
        if (key == Key.Escape && !view.firstPerson && view.tool == null && !view.buildOpen && !view.showOverview && (controlling || view.selectedId == null))
        {
            // The Colony tab: back to first person; playing nobody, the menu.
            if (controlling) view.closeMScreen();
            else ToggleMenu();
            return;
        }
        if (!view.firstPerson && Tools.handleBuildKey(view, key, k.ShiftPressed))
        {
            if (view.tool == null) painting = null;
            return;
        }
        switch (key)
        {
            case Key.Quoteleft: view.showDebug = !view.showDebug; return;
            case Key.P: view.showPriorities = !view.showPriorities; return;
            case Key.N: view.showMinimap = !view.showMinimap; return;
            case Key.O: view.showOverview = !view.showOverview; return;
            case Key.K: view.showKeys = !view.showKeys; return;
            // Speed up or down a step (1x, 2x, 4x), as the HUD's buttons.
            case Key.Equal or Key.KpAdd: view.speed = view.speed == 1 ? 2 : 4; view.paused = false; return;
            case Key.Minus or Key.KpSubtract: view.speed = view.speed == 4 ? 2 : 1; return;
            case Key.Home: view.camera = centredOn(state.grid.width, state.grid.height, view.camera.zoom); return;
            case Key.F:
                // In the Colony tab, F on someone else plays them instead; otherwise it lets go (or takes the selected).
                if (c != null && !view.firstPerson && view.selectedId != null && view.selectedId != c.survivorId) Command(new Command { type = "takeControl", survivorId = view.selectedId });
                else if (c != null) Command(new Command { type = "takeControl", survivorId = null });
                else if (view.selectedId != null) Command(new Command { type = "takeControl", survivorId = view.selectedId });
                return;
        }
        var you = ControlMod.controlled(state);
        if (c == null || you == null || !view.firstPerson) return;
        var at = you.pos;
        switch (key)
        {
            case >= Key.Key1 and <= Key.Key9:
            {
                // Quick slots: survivor.quick, assigned on the inventory screen (InventoryGrid.cs quickItem).
                int slot = (int)(key - Key.Key1);
                if (Inventory.quickItem(state, you, slot) is { } quick) Command(new Command { type = "useItem", itemId = quick.id });
                break;
            }
            case Key.R: Command(new Command { type = "work", x = at.x + c.facing.x, y = at.y + c.facing.y }); break;
            case Key.X: Command(new Command { type = "reload" }); break;          // the gun in hand, from your bag (a round at a time for a revolver, shotgun, rifle)
            case Key.Q: Command(new Command { type = "switchWeapon" }); break;    // the next weapon in your bag
            case Key.L:
            {
                // "Loot that room": the room where you look.
                var t = LookedAt(you, LOOK_REACH) ?? FrontTile(you);
                Command(new Command { type = "shout", order = "loot", x = t.x, y = t.y });
                break;
            }
            case Key.C: Command(new Command { type = "sneak", on = c.sneaking != true }); break;
            case Key.T: Command(new Command { type = "talk" }); break;
            case Key.U: Command(new Command { type = "boot" }); break;   // the boot of the car beside you (VehicleActions.cs)
            case Key.I: Command(new Command { type = "hood" }); break;   // its bonnet
            case Key.V:
            case Key.H: Command(new Command { type = "shout", order = key == Key.V ? "follow" : "hold" }); break;
            case Key.E: UseAhead(); break;
            case Key.G: LightKey(you); break;   // your flashlight; in a car, its headlights (App/GameInput.cs)
            case Key.Z: SwitchKey(you); break;  // the room's light switch (X is reload)
        }
    }

    /// <summary>G: your flashlight, or in a car its headlights.</summary>
    void LightKey(Survivor you) =>
        Command(new Command { type = Vehicles.vehicleOf(state, you) != null ? "headlights" : "flashlight" });

    /// <summary>Z: the switch of the room you're in, else of the room past the door in front of you.</summary>
    void SwitchKey(Survivor you)
    {
        var t = LoonieRoo.Sim.Lights.roomAt(state, you.pos.x, you.pos.y) != null ? you.pos : FrontTile(you);
        Command(new Command { type = "lightSwitch", x = t.x, y = t.y });
    }
}
