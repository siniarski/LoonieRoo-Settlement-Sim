// `--selftest`: drives the running game with real input events (through Godot's input pipeline,
// so the HUD's buttons and the game's handlers both see them) and checks what they did: walking,
// pausing, the build bar and a wall placed with the mouse, the map, the menu, a HUD button, a
// save to a slot and back, and new games (group and solo) from the menu landing in first person. Prints PASS/FAIL per step and quits with the number of failures as the
// exit code. The Godot side's equivalent of driving the browser game with Playwright.

using System;
using System.Collections.Generic;
using System.Linq;
using Task = System.Threading.Tasks.Task;
using Godot;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game;

public partial class Game
{
    readonly List<string> failures = new();
    /// <summary>The test's own mouse events carry this device id; while it runs, every other mouse event (the real cursor over the window) is dropped.</summary>
    const int TEST_DEVICE = 7777;
    bool selfTesting;

    /// <summary>While the test runs, drops the real cursor's events. Returns whether it did.</summary>
    bool DropForeignMouse(InputEvent e)
    {
        if (!selfTesting || e is not InputEventMouse || e.Device == TEST_DEVICE) return false;
        GetViewport().SetInputAsHandled();
        return true;
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static void SendKey(Key key, bool pressed, bool shift = false) =>
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed, ShiftPressed = shift });

    async Task Tap(Key key, bool shift = false)
    {
        SendKey(key, true, shift);
        await Frames(1);
        SendKey(key, false, shift);
        await Frames(1);
    }

    static void Mouse(Vector2 at, MouseButton button, bool pressed, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { Device = TEST_DEVICE, Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed, ShiftPressed = shift, ButtonMask = pressed ? (MouseButtonMask)(1 << ((int)button - 1)) : 0 });
    }

    async Task Click(Vector2 at, MouseButton button = MouseButton.Left, bool shift = false)
    {
        Mouse(at, button, true, shift);
        await Frames(1);
        Mouse(at, button, false, shift);
        await Frames(1);
    }

    Vector2 ScreenOf(double wx, double wy)
    {
        var p = worldToScreen(view.camera, Vp, wx, wy);
        return new Vector2((float)p.x, (float)p.y);
    }

    void Check(string name, bool ok, string detail = "")
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {name}{(detail != "" ? $"  ({detail})" : "")}");
        if (!ok) failures.Add(name);
    }

    async Task SelfTest()
    {
        selfTesting = true;
        await Frames(5);
        // First person: the 3D view is up, with the world built round you.
        Check("Playing someone is first person", view.firstPerson && firstPerson.Visible && firstPerson.ChunkCount > 0, $"chunks {firstPerson.ChunkCount}");
        // Walking: looking north, W held moves you north.
        view.paused = false;
        view.yaw = -Math.PI / 2;
        view.pitch = 0;
        var you = ControlMod.controlled(state)!;
        int y0 = you.at.y;
        SendKey(Godot.Key.W, true);
        await Frames(30);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        you = ControlMod.controlled(state)!;
        Check("Looking north, W walks north", you.at.y < y0, $"at.y {y0} -> {you.at.y}");
        // The mouse turns you: a quarter turn right, and the aim follows (east).
        Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Relative = new Vector2((float)(Math.PI / 2 / LOOK_PER_PX), 0) });
        await Frames(3);
        var aim = state.control!.aim;
        Check("The mouse turns you and your aim", aim != null && aim.x == ControlMod.AIM_MAX && aim.y == 0, $"yaw {view.yaw:0.00}, aim {aim?.x},{aim?.y}");
        int x0 = ControlMod.controlled(state)!.at.x;
        SendKey(Godot.Key.W, true);
        await Frames(20);
        SendKey(Godot.Key.W, false);
        await Frames(3);
        Check("Looking east, W walks east", ControlMod.controlled(state)!.at.x > x0, $"at.x {x0} -> {ControlMod.controlled(state)!.at.x}");
        // Tab: the inventory, with the mouse let go; Tab again puts it away.
        await Tap(Godot.Key.Tab);
        Check("Tab opens the inventory", view.showInventory && Input.MouseMode == Input.MouseModeEnum.Visible);
        await Tap(Godot.Key.Tab);
        Check("Tab closes it", !view.showInventory);

        // Space pauses; ticks stop.
        await Tap(Godot.Key.Space);
        int tick = state.tick;
        await Frames(10);
        Check("Space pauses", view.paused && state.tick == tick, $"tick {tick} -> {state.tick}");

        // B (in first person, opens the Colony tab) opens the build bar; 1 arms the wall; a shift-click builds it at once.
        await Tap(Godot.Key.B);
        Check("B opens the Colony tab and the build bar", view.buildOpen && view.colonyMode && !view.firstPerson && hud.buildBar.Visible);
        await Tap(Godot.Key.Key1);
        Check("1 arms the wall", view.tool == "wall", view.tool ?? "none");
        var at = ControlMod.controlled(state)!.pos;
        // The north border of the tile two to your right and two down, clicked just inside its top edge.
        int tx = at.x + 2, ty = at.y + 2, e = (ty * state.grid.width + tx) * 2;
        int before = state.grid.edges[e];
        await Click(ScreenOf(tx + 0.5, ty + 0.05), MouseButton.Left, shift: true);
        Check("Shift-click places an edge wall", before != Grids.EDGE_WALL && state.grid.edges[e] == Grids.EDGE_WALL, $"edge {before} -> {state.grid.edges[e]}");
        // A drag paints: designate a row of walls (blueprints), not instantly.
        int blueprints = state.blueprints.Count;
        var from = ScreenOf(tx + 0.4, ty + 3.02);
        Mouse(from, MouseButton.Left, true);
        await Frames(1);
        for (int i = 1; i <= 4; i++)
        {
            var p = ScreenOf(tx + 0.4 + i, ty + 3.02);
            Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left });
            await Frames(1);
        }
        Mouse(ScreenOf(tx + 4.4, ty + 3.02), MouseButton.Left, false);
        await Frames(1);
        // Five north borders, (tx..tx+4, ty+3): one wall blueprint each. (Off the tile centres, so the
        // quarter-tile steps never land exactly on a corner, where the west border ties with the north.)
        var made = state.blueprints.Skip(blueprints).Select((b) => $"{b.target}@{b.x},{b.y}{b.side}").ToList();
        bool row = Enumerable.Range(0, 5).All((i) => state.blueprints.Any((b) => b.target == "wall" && b.side == "n" && b.x == tx + i && b.y == ty + 3));
        Check("A drag designates a run of walls", row && made.Count == 5, string.Join(" ", made.Take(12)) + (made.Count > 12 ? $" … {made.Count} in all" : ""));
        await Tap(Godot.Key.Escape);
        Check("Esc puts the tool down", view.tool == null && view.buildOpen);
        await Tap(Godot.Key.Escape);
        Check("Esc again closes the build bar", !view.buildOpen && !hud.buildBar.Visible);

        // The HUD's 2x button (a real click on it) sets the speed and unpauses.
        var two = FindButton(hud, "2x");
        if (two != null) await Click(two.GetGlobalRect().GetCenter());
        Check("Clicking 2x sets the speed", two != null && view.speed == 2 && !view.paused, $"speed {view.speed}, paused {view.paused}");
        await Tap(Godot.Key.Space);

        // Esc in the Colony tab: back to first person.
        await Tap(Godot.Key.Escape);
        await Frames(1);
        Check("Esc in the Colony tab goes back to first person", view.firstPerson && !view.colonyMode);

        // M opens the map; its Colony tab switches over; M goes back to first person.
        await Tap(Godot.Key.M);
        await Frames(1);
        Check("M opens the map", view.showMap && worldMap.Visible && tabs.Visible);
        var colonyTab = FindButton(tabs, "Colony");
        if (colonyTab != null) await Click(colonyTab.GetGlobalRect().GetCenter());
        await Frames(1);
        Check("The Colony tab shows the colony", !view.showMap && view.colonyMode && !view.firstPerson && !worldMap.Visible);
        await Tap(Godot.Key.M);
        await Frames(1);
        Check("M goes back to first person", view.firstPerson && !view.showMap && !view.colonyMode);

        // Esc opens the menu; Save to slot 3 from it; Resume.
        await Tap(Godot.Key.Escape);
        await Frames(2);
        Check("Esc opens the menu", view.menu == "main" && menu.Visible, view.menu ?? "none");
        var slot = FindButton(menu, "3");
        if (slot != null) await Click(slot.GetGlobalRect().GetCenter());
        Check("Save to slot 3", Saves.slotLabel("3") != null);
        int savedTick = state.tick;
        var resume = FindButton(menu, "Resume");
        if (resume != null) await Click(resume.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Resume closes the menu", view.menu == null && !menu.Visible);

        // Play on a little, then load slot 3 back: the clock goes back to when it was saved.
        view.paused = false;
        await Frames(20);
        Load("3");
        await Frames(2);
        Check("Load slot 3 brings the save back", state.tick == savedTick, $"tick {state.tick}, saved at {savedTick}");

        // New game from the menu, from the Colony tab of this one: a group start (the default
        // preset), then a solo one. Each lands in first person on your survivor, paused, Space to play.
        await NewGameFromMenu("Survivor", "group");
        await NewGameFromMenu("Apocalypse", "solo");

        GD.Print(failures.Count == 0 ? "selftest: all passed" : $"selftest: {failures.Count} FAILED: {string.Join(", ", failures)}");
        GetTree().Quit(failures.Count);
    }

    /// <summary>B (the Colony tab), the HUD's Menu button, New game, a preset, Next, Start: checks you're in first person on your survivor.</summary>
    async Task NewGameFromMenu(string preset, string start)
    {
        await Tap(Godot.Key.B);
        var menuButton = FindButton(hud, "☰ Menu");
        if (menuButton != null) await Click(menuButton.GetGlobalRect().GetCenter());
        await Frames(2);
        foreach (var text in new[] { "New game", preset, "Next: who you are", "Start" })
        {
            var b = FindButton(menu, text, prefix: text == preset);
            if (b == null) { Check($"New game ({start}): the menu has \"{text}\"", false); return; }
            await Click(b.GetGlobalRect().GetCenter());
            await Frames(2);
        }
        await Frames(3);
        var you = ControlMod.controlled(state);
        Check($"New game ({start}) from the menu is first person on your survivor",
            state.settings.start == start && you != null && you.id == state.survivors[0].id && view.menu == null && !view.colonyMode && !view.buildOpen && view.firstPerson && firstPerson.Visible,
            $"start {state.settings.start}, playing {you?.id ?? "nobody"}, menu {view.menu ?? "none"}, colony {view.colonyMode}, first person {view.firstPerson}");
        Check($"New game ({start}) starts paused with the banner up", view.paused && FindLabel(hud, "PAUSED") != null);
        // SELFTEST_SHOT=prefix: save what the new start looks like (prefix-group.png, prefix-solo.png).
        if (OS.GetEnvironment("SELFTEST_SHOT") is { Length: > 0 } shot)
        {
            await Frames(5);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{shot}-{start}.png");
        }
        int tick = state.tick;
        await Tap(Godot.Key.Space);
        await Frames(10);
        Check($"New game ({start}): Space plays", !view.paused && state.tick > tick, $"tick {tick} -> {state.tick}");
        await Tap(Godot.Key.Space);
    }

    /// <summary>A visible label whose text starts with `text`.</summary>
    static Label? FindLabel(Node root, string text)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Label l && l.Text.StartsWith(text) && l.IsVisibleInTree()) return l;
            if (FindLabel(child, text) is { } found) return found;
        }
        return null;
    }

    static Button? FindButton(Node root, string text, bool prefix = false)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button b && (prefix ? b.Text.StartsWith(text) : b.Text == text) && b.IsVisibleInTree()) return b;
            var found = FindButton(child, text, prefix);
            if (found != null) return found;
        }
        return null;
    }
}
