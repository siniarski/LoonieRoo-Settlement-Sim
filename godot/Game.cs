// The game: runs the C# sim (cs/Sim) at a fixed timestep, draws it (Render/), and turns input into
// sim commands (src/app.ts, src/ui/input.ts, src/ui/loop.ts). The sim is never mutated from here
// except through Commands.applyCommand and Tick.step.
//
// Command line (after `--`):
//   --seed=N --size=N --start=solo|group   a new world (default: a solo start, 128 tiles, a random seed)
//   --ticks=N                              run N ticks before showing it
//   --shot=path.png --frames=N             save a screenshot after N frames and quit
//   --zoom=Z                               the camera's starting zoom

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game : Node2D
{
    // --- the loop (loop.ts) ---
    const double MS_PER_TICK = 1000.0 / Tick.TICKS_PER_SECOND;
    /// <summary>Frames longer than this (window in background, breakpoint) are clamped.</summary>
    const double MAX_FRAME_MS = 250;
    /// <summary>Never run more than this many ticks in one frame, to avoid a spiral of death.</summary>
    const int MAX_TICKS_PER_FRAME = 40;
    static readonly int[] SPEEDS = { 1, 2, 4 };
    /// <summary>Screen pixels per second for keyboard panning.</summary>
    const double KEY_PAN_PX_PER_SEC = 600;
    /// <summary>Directions the mouse aim snaps to (keeps the command log small).</summary>
    const int AIM_STEPS = 32;
    /// <summary>A right-button press that moves less than this is a click, not a pan.</summary>
    const double CLICK_SLOP_PX = 4;

    public SimState state = null!;
    Cam camera;
    int speed = 1;
    bool paused;
    bool showDebug;
    string? selectedId;
    Point? hover;
    double accumulatorMs;
    double alpha;

    Ctx ctx = null!;
    Painter painter = null!;
    Label hud = null!;
    Label toastLabel = null!;
    double toastUntil;

    Vector2? mouse;
    Vector2? panning;
    (Vector2 start, double travelled)? rightPress;

    // perf (ms, smoothed), for the HUD
    double fps, tickMs, simMs, drawMs;

    // command line
    string? shotPath;
    int shotFrames = 30;
    int frame;

    public override void _Ready()
    {
        // The sim formats numbers JS's way; keep the UI (and CSS colour strings) culture-free too.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var args = ParseArgs();
        double seed = args.TryGetValue("seed", out var s) ? double.Parse(s) : Math.Floor(Random.Shared.NextDouble() * 1e9);
        double size = args.TryGetValue("size", out var z) ? double.Parse(z) : 128;
        var n = Difficulty.NORMAL;
        var settings = new Settings { start = args.GetValueOrDefault("start", "solo"), zombies = n.zombies, weather = n.weather, progression = n.progression, winter = n.winter, dayScale = n.dayScale, map = n.map };
        if (args.TryGetValue("shot", out var shot)) shotPath = shot;
        if (args.TryGetValue("frames", out var f)) shotFrames = int.Parse(f);

        ctx = new Ctx(this);
        painter = new Painter(ctx);
        BuildHud();

        NewGame(Worldgen.createWorld(seed, size, settings));
        if (args.TryGetValue("zoom", out var zoom)) camera.zoom = double.Parse(zoom);
        if (args.TryGetValue("ticks", out var t)) Tick.step(state, int.Parse(t));
        paused = shotPath == null && args.ContainsKey("paused");
        if (settings.start == "solo") Toast("Alone. WASD to walk, click to swing, right-click or E to use. Space to pause.");
    }

    static Dictionary<string, string> ParseArgs()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        return args;
    }

    void NewGame(SimState world)
    {
        state = world;
        Time.setDayScale(state.settings.dayScale);
        camera = centredOn(state.grid.width, state.grid.height, camera.zoom == 0 ? 1 : camera.zoom);
        Pathfinding.warmReach(state.grid, state.walkabilityVersion);
        selectedId = state.control?.survivorId;
    }

    void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        hud = new Label { Position = new Vector2(10, 8) };
        hud.AddThemeColorOverride("font_color", Ctx.Css(COLOURS.label));
        hud.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        hud.AddThemeConstantOverride("shadow_offset_x", 1);
        hud.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(hud);
        toastLabel = new Label { Position = new Vector2(10, 690) };
        toastLabel.AddThemeColorOverride("font_color", Ctx.Css(COLOURS.controlled));
        toastLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        toastLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        toastLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(toastLabel);
    }

    void Toast(string text)
    {
        toastLabel.Text = text;
        toastUntil = Godot.Time.GetTicksMsec() + 6000;
    }

    Vport Vp
    {
        get
        {
            var r = GetViewportRect().Size;
            return new Vport(r.X, r.Y);
        }
    }

    bool Command(Command cmd)
    {
        var result = Commands.applyCommand(state, cmd);
        if (cmd.type == "takeControl" && result.ok) selectedId = cmd.survivorId ?? selectedId;
        return result.ok;
    }

    public override void _Process(double delta)
    {
        double frameMs = delta * 1000;
        fps = fps * 0.9 + 1000 / Math.Max(frameMs, 1) * 0.1;

        // Fixed timestep: speed changes how many ticks run per frame, never the tick length (rule 6).
        int ticks = 0;
        if (!paused)
        {
            accumulatorMs += Math.Min(frameMs, MAX_FRAME_MS) * speed;
            ticks = (int)Math.Floor(accumulatorMs / MS_PER_TICK);
            if (ticks > MAX_TICKS_PER_FRAME) { ticks = MAX_TICKS_PER_FRAME; accumulatorMs = 0; }
            else accumulatorMs -= ticks * MS_PER_TICK;
        }
        var sw = Stopwatch.StartNew();
        if (ticks > 0) Tick.step(state, ticks);
        double ms = sw.Elapsed.TotalMilliseconds;
        simMs = simMs * 0.9 + ms * 0.1;
        if (ticks > 0) tickMs = tickMs * 0.9 + ms / ticks * 0.1;

        UpdateInput(frameMs);
        if (selectedId != null && !state.survivors.Exists((x) => x.id == selectedId)) selectedId = null;
        alpha = Math.Min(1, accumulatorMs / MS_PER_TICK);
        // The camera follows whoever you're steering.
        var you = ControlMod.controlled(state);
        if (you != null)
        {
            var w = worldPos(you, alpha);
            camera.x = w.x;
            camera.y = w.y;
        }
        QueueRedraw();
        UpdateHud(you);

        frame++;
        if (shotPath != null && frame == shotFrames) _ = SaveShot();
    }

    async System.Threading.Tasks.Task SaveShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(shotPath!);
        GD.Print(err == Error.Ok ? $"shot: {shotPath}" : $"shot failed: {err}");
        GetTree().Quit();
    }

    public override void _Draw()
    {
        var sw = Stopwatch.StartNew();
        ctx.Begin();
        painter.draw(state, camera, Vp, new DrawOptions { hover = hover, tool = null, alpha = alpha, selectedId = selectedId, showDebug = showDebug });
        drawMs = drawMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
    }

    void UpdateHud(Survivor? you)
    {
        var lines = new List<string>
        {
            $"Day {Time.dayOf(state.tick)}  {Time.clockText(state.tick)}  {state.weather}   {(paused ? "paused" : $"{speed}x")}",
        };
        if (you != null)
            lines.Add($"{you.name}   health {you.health / Health.HEALTH_MAX:P0}  food {you.satiety / Food.SATIETY_MAX:P0}  rest {you.rest / Rest.REST_MAX:P0}");
        lines.Add($"{state.survivors.Count} survivors  {state.zombies.Count} zombies");
        if (showDebug) lines.Add($"fps {fps:0}  tick {tickMs:0.00} ms  sim {simMs:0.00} ms  draw {drawMs:0.00} ms");
        hud.Text = string.Join("\n", lines);
        if (Godot.Time.GetTicksMsec() > toastUntil) toastLabel.Text = "";
        toastLabel.Position = new Vector2(10, (float)Vp.height - 30);
    }

    // --- input (input.ts) ---------------------------------------------------------------------------

    Point TileAt(Vector2 p) => screenToTile(camera, Vp, p.X, p.Y);

    void UpdateInput(double frameMs)
    {
        int dx = 0, dy = 0;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) dy -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) dy += 1;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) dx -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) dx += 1;
        bool shift = Input.IsKeyPressed(Key.Shift);
        // Under direct control the keys walk, and the sim only hears about it when they change.
        var c = state.control;
        if (c != null)
        {
            bool run = shift && (dx != 0 || dy != 0);
            if (c.dx != dx || c.dy != dy || (c.running == true) != run) Command(new Command { type = "steer", dx = dx, dy = dy, run = run });
            // Look at the mouse: the direction from you to it, in AIM_STEPS steps round, sent only when it changes.
            var at = ControlMod.controlled(state)?.at;
            if (mouse is { } m && at != null)
            {
                var w = screenToWorld(camera, Vp, m.X, m.Y);
                double ax = w.x * Motion.SUB - at.x, ay = w.y * Motion.SUB - at.y;
                if (ax * ax + ay * ay > Math.Pow(Motion.SUB / 4.0, 2))
                {
                    double step = Math.Round(Math.Atan2(ay, ax) / (2 * Math.PI) * AIM_STEPS);
                    double a = step * 2 * Math.PI / AIM_STEPS;
                    int x = (int)Math.Round(Math.Cos(a) * ControlMod.AIM_MAX), y = (int)Math.Round(Math.Sin(a) * ControlMod.AIM_MAX);
                    if (c.aim == null || c.aim.x != x || c.aim.y != y) Command(new Command { type = "aim", x = x, y = y });
                }
            }
            return;
        }
        if (dx == 0 && dy == 0) return;
        double distance = KEY_PAN_PX_PER_SEC * frameMs / 1000;
        camera = panByScreen(camera, -dx * distance, -dy * distance);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            mouse = mm.Position;
            hover = TileAt(mm.Position);
            if (panning is { } from)
            {
                var d = mm.Position - from;
                camera = panByScreen(camera, d.X, d.Y);
                panning = mm.Position;
                if (rightPress is { } rp) rightPress = (rp.start, rp.travelled + d.Length());
            }
        }
        else if (e is InputEventMouseButton mb)
        {
            mouse = mb.Position;
            if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                camera = zoomAt(camera, Vp, mb.Position.X, mb.Position.Y, mb.ButtonIndex == MouseButton.WheelUp ? 1.15 : 1 / 1.15);
                hover = TileAt(mb.Position);
            }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) OnLeftDown(TileAt(mb.Position));
            else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                if (mb.Pressed)
                {
                    panning = mb.Position;
                    if (mb.ButtonIndex == MouseButton.Right) rightPress = (mb.Position, 0);
                }
                else
                {
                    if (mb.ButtonIndex == MouseButton.Right && rightPress is { } rp && rp.travelled < CLICK_SLOP_PX) OnRightClick(TileAt(mb.Position));
                    panning = null;
                    rightPress = null;
                }
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo) OnKey(k);
    }

    void OnLeftDown(Point tile)
    {
        if (state.control != null)
        {
            Command(new Command { type = "swing", x = tile.x, y = tile.y });
            return;
        }
        // Select mode: pick a survivor; with someone picked, a click on a bed gives it to them; anywhere else clears the selection.
        var who = Survivors.survivorAt(state, tile.x, tile.y)?.id;
        var f = who == null ? FurnitureMod.furnitureAt(state, tile.x, tile.y) : null;
        if (who == null && f?.kind == "bed" && selectedId != null) Command(new Command { type = "assignBed", bedId = f.id, survivorId = selectedId });
        else selectedId = who;
    }

    void OnRightClick(Point tile)
    {
        if (state.control != null)
        {
            Command(new Command { type = "use", x = tile.x, y = tile.y });
            return;
        }
        if (selectedId != null && tile.x >= 0 && tile.y >= 0 && tile.x < state.grid.width && tile.y < state.grid.height)
            Command(new Command { type = "moveSurvivor", survivorId = selectedId, x = tile.x, y = tile.y });
    }

    void OnKey(InputEventKey k)
    {
        var c = state.control;
        var you = ControlMod.controlled(state);
        switch (k.PhysicalKeycode)
        {
            case Key.Space:
                paused = !paused;
                return;
            case Key.Quoteleft:
                showDebug = !showDebug;
                return;
            case Key.Equal:
            case Key.KpAdd:
                speed = SPEEDS[Math.Min(SPEEDS.Length - 1, Array.IndexOf(SPEEDS, speed) + 1)];
                return;
            case Key.Minus:
            case Key.KpSubtract:
                speed = SPEEDS[Math.Max(0, Array.IndexOf(SPEEDS, speed) - 1)];
                return;
            case Key.Home:
                camera = centredOn(state.grid.width, state.grid.height, camera.zoom);
                return;
            case Key.F:
                if (c != null) Command(new Command { type = "takeControl", survivorId = null });
                else if (selectedId != null) Command(new Command { type = "takeControl", survivorId = selectedId });
                return;
        }
        if (c == null || you == null) return;
        var at = you.pos;
        switch (k.PhysicalKeycode)
        {
            case >= Key.Key1 and <= Key.Key9:
            {
                // Quick slots: the bag's first nine items (inventory.ts).
                int slot = (int)(k.PhysicalKeycode - Key.Key1);
                if (slot < you.pack.Count) Command(new Command { type = "useItem", itemId = you.pack[slot] });
                break;
            }
            case Key.R:
                Command(new Command { type = "work", x = at.x + c.facing.x, y = at.y + c.facing.y });
                break;
            case Key.L:
                if (hover != null) Command(new Command { type = "shout", order = "loot", x = hover.x, y = hover.y });
                break;
            case Key.C:
                Command(new Command { type = "sneak", on = c.sneaking != true });
                break;
            case Key.T:
                Command(new Command { type = "talk" });
                break;
            case Key.V:
            case Key.H:
                Command(new Command { type = "shout", order = k.PhysicalKeycode == Key.V ? "follow" : "hold" });
                break;
            case Key.E:
                // The tile in front, else the one underfoot.
                if (!Command(new Command { type = "use", x = at.x + c.facing.x, y = at.y + c.facing.y })) Command(new Command { type = "use", x = at.x, y = at.y });
                break;
        }
    }
}
