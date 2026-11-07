// The game (src/app.ts, src/ui/input.ts, src/ui/loop.ts): runs the C# sim (cs/Sim) at a fixed
// timestep, draws it (Render/), shows the HUD, menu and map (Ui/), and turns input into sim
// commands. The sim is only ever changed through Commands.applyCommand and Tick.step (rule 5).
//
// Command line (after `--`), to skip the menu:
//   --seed=N --size=N --start=solo|group   a new world (a solo start, 128 tiles, a random seed by default)
//   --load=path.json                       a save file instead
//   --ticks=N                              run N ticks before showing it
//   --shot=path.png --frames=N             save a screenshot after N frames and quit
//   --zoom=Z --paused --debug --map --overview --build=category   the view to start with

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Game.Ui;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game;

public partial class Game : Node2D, IGameFiles, IMenuActions
{
    // --- the loop (loop.ts) ---
    const double MS_PER_TICK = 1000.0 / Tick.TICKS_PER_SECOND;
    /// <summary>Frames longer than this (window in background, breakpoint) are clamped.</summary>
    const double MAX_FRAME_MS = 250;
    /// <summary>Never run more than this many ticks in one frame, to avoid a spiral of death.</summary>
    const int MAX_TICKS_PER_FRAME = 40;
    /// <summary>Screen pixels per second for keyboard panning.</summary>
    const double KEY_PAN_PX_PER_SEC = 600;
    /// <summary>Directions the mouse aim snaps to (keeps the command log small).</summary>
    const int AIM_STEPS = 32;
    /// <summary>A right-button press that moves less than this is a click, not a pan.</summary>
    const double CLICK_SLOP_PX = 4;

    public SimState state = null!;
    /// <summary>Where play started (a new world or a loaded save): bug reports replay from here.</summary>
    SimState initial = null!;
    readonly View view = new();
    double accumulatorMs;
    double alpha;
    int lastAutosaveDawn;
    bool pausedUnderMenu;

    Ctx ctx = null!;
    Painter painter = null!;
    Hud hud = null!;
    Menu menu = null!;
    WorldMap worldMap = null!;
    FileDialog? fileDialog;

    // input
    Vector2? mouse;
    Vector2? panning;
    (Vector2 start, double travelled)? rightPress;
    Point? painting;
    /// <summary>Shift state captured when the drag started, so a drag doesn't change mode halfway.</summary>
    bool paintInstant;
    /// <summary>Edge tools paint along borders: the last edge painted, and where the drag was in the world.</summary>
    (double wx, double wy, string last)? edgePainting;

    // perf (ms, smoothed), for the debug panel
    double fps, tickMs, simMs, drawMs, worstTickMs;

    // command line
    string? shotPath;
    int shotFrames = 30;
    int frame;

    public override void _Ready()
    {
        // The sim formats numbers JS's way; keep the UI (and CSS colour strings) culture-free too.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        ctx = new Ctx(this);
        painter = new Painter(ctx);
        var layer = new CanvasLayer();
        AddChild(layer);
        hud = new Hud(view, this);
        layer.AddChild(hud);
        worldMap = new WorldMap(view, Command);
        layer.AddChild(worldMap);
        menu = new Menu(view, this);
        layer.AddChild(menu);

        var args = ParseArgs();
        if (args.TryGetValue("shot", out var shot)) shotPath = shot;
        if (args.TryGetValue("frames", out var f)) shotFrames = int.Parse(f);
        bool selftest = args.ContainsKey("selftest");
        if (selftest)
        {
            Saves.DIR = "user://selftest-saves";
            if (!args.ContainsKey("seed")) args["seed"] = "42";
        }
        bool direct = args.ContainsKey("seed") || args.ContainsKey("start") || args.ContainsKey("size") || args.ContainsKey("load") || shotPath != null;
        double seed = args.TryGetValue("seed", out var s) ? double.Parse(s) : RandomSeed();
        double size = args.TryGetValue("size", out var z) ? double.Parse(z) : Worldgen.WORLD_SIZE;
        var settings = CopySettings(Difficulty.NORMAL);
        settings.start = args.GetValueOrDefault("start", "solo");
        if (args.TryGetValue("load", out var load)) Replace(Savefile.loadFile(System.IO.File.ReadAllText(load)), "the file");
        else Replace(new LoadResult { ok = true, state = Worldgen.createWorld(seed, size, settings), kind = "save" }, $"a new {size}×{size} world");
        if (args.TryGetValue("zoom", out var zoom)) view.camera.zoom = double.Parse(zoom);
        if (args.TryGetValue("ticks", out var t)) Tick.step(state, int.Parse(t));
        view.showDebug = args.ContainsKey("debug");
        view.showMap = args.ContainsKey("map");
        view.showOverview = args.ContainsKey("overview");
        if (args.TryGetValue("build", out var cat)) Tools.chooseCategory(view, cat == "" ? "structure" : cat);
        if (args.TryGetValue("menu", out var m)) view.menu = m == "" ? "main" : m;
        if (direct)
        {
            view.inGame = true;
            view.paused = args.ContainsKey("paused");
            if (state.control != null) view.Toast("Alone. WASD to walk, click to swing, right-click or E to use. Space to pause.");
            if (selftest) _ = SelfTest();
        }
        else
        {
            // On starting: the menu, over a world to look at.
            view.inGame = false;
            view.menu = "main";
            view.paused = true;
        }
    }

    static double RandomSeed() => Math.Floor(Random.Shared.NextDouble() * 1e9);

    static Settings CopySettings(Settings s) => new() { start = s.start, zombies = s.zombies, weather = s.weather, progression = s.progression, winter = s.winter, dayScale = s.dayScale, map = s.map };

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

    /// <summary>Replace the running game (a save, an import, a new world) and start the bug-report log afresh. Returns whether it did.</summary>
    bool Replace(LoadResult result, string from)
    {
        if (!result.ok || result.state == null)
        {
            view.Toast($"Couldn't load {from}: {result.error}");
            return false;
        }
        state = result.state;
        initial = States.cloneState(state);
        Time.setDayScale(state.settings.dayScale); // the clock reads right while paused
        view.camera = centredOn(state.grid.width, state.grid.height, view.camera.zoom == 0 ? 1 : view.camera.zoom);
        view.paused = true; // look before you play
        Pathfinding.warmReach(state.grid, state.walkabilityVersion); // while paused, not mid-play
        lastAutosaveDawn = Dawns(state.tick);
        view.Toast($"Loaded {from}{(result.kind == "bug" ? " (bug report, replayed)" : "")}. Paused: Space to play.");
        view.selectedId = state.control?.survivorId;
        return true;
    }

    /// <summary>Into the game, off the menu.</summary>
    void Play()
    {
        view.menu = null;
        view.inGame = true;
    }

    void ToggleMenu()
    {
        if (view.menu == null)
        {
            pausedUnderMenu = view.paused;
            view.paused = true;
            view.menu = "main";
        }
        else if (view.inGame)
        {
            view.menu = null;
            view.paused = pausedUnderMenu;
        }
        menu.Refresh();
    }

    // --- what the HUD and the menu ask of the game ---

    public bool Command(Command cmd)
    {
        var result = Commands.applyCommand(state, cmd);
        if (cmd.type == "takeControl" && result.ok) view.selectedId = cmd.survivorId ?? view.selectedId;
        return result.ok;
    }

    public void OpenMenu() => ToggleMenu();

    public void NewGamePlus()
    {
        // The browser confirms first; the helicopter panel's button is the confirmation here.
        Replace(new LoadResult { ok = true, state = Escape.newGamePlus(state, RandomSeed()), kind = "save" }, "a new world (new game+)");
        view.selectedId = state.control?.survivorId;
    }

    public void Resume() => ToggleMenu();

    public void NewGame(double size, Settings settings, double? seed, Character you)
    {
        var world = Worldgen.createWorld(seed ?? RandomSeed(), size, settings, you);
        if (!Replace(new LoadResult { ok = true, state = world, kind = "save" }, $"a new {size}×{size} world")) return;
        Play();
        if (settings.start == "solo") view.Toast("Alone. WASD to walk, click to swing, right-click or E to use. Space to play.");
    }

    public void Arena(string kind)
    {
        if (!Replace(new LoadResult { ok = true, state = LoonieRoo.Sim.Arena.createArena(RandomSeed(), kind), kind = "save" }, kind == "one" ? "a debug arena (one zombie)" : "a debug arena (a crowd)")) return;
        Play();
    }

    public void Load(string slot)
    {
        if (Replace(Saves.loadFromSlot(slot), slot == Saves.AUTO_SLOT ? "the autosave" : $"slot {slot}")) Play();
    }

    public void ImportFile()
    {
        fileDialog?.QueueFree();
        fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Filters = new[] { "*.json ; Saves and bug reports" }, CurrentDir = Saves.ExportDir() };
        fileDialog.FileSelected += (path) =>
        {
            if (Replace(Savefile.loadFile(System.IO.File.ReadAllText(path)), "the file")) Play();
        };
        AddChild(fileDialog);
        fileDialog.PopupCentered();
    }

    public void Save(string slot) => view.Toast(Saves.saveToSlot(slot, state) ?? $"Saved to slot {slot}.");

    public void ExportSave()
    {
        var path = System.IO.Path.Combine(Saves.ExportDir(), $"loonieroo-{Saves.fileStamp(state)}.json");
        System.IO.File.WriteAllText(path, Savefile.toSaveFile(state));
        view.Toast($"Saved to {path}");
    }

    public void ReportBug()
    {
        var path = System.IO.Path.Combine(Saves.ExportDir(), $"loonieroo-bug-{Saves.fileStamp(state)}.json");
        System.IO.File.WriteAllText(path, Savefile.toBugReport(initial, state));
        view.Toast($"Bug report written to {path}: it replays the game from where you started to now.");
    }

    // --- the frame ---

    /// <summary>Autosave at every dawn (06:00), when the night's zombies have gone. Tick 0 is 06:00.</summary>
    static int Dawns(int tick) => tick / Time.DAY_LENGTH;

    Vport Vp
    {
        get
        {
            var r = GetViewportRect().Size;
            return new Vport(r.X, r.Y);
        }
    }

    public override void _Process(double delta)
    {
        double frameMs = delta * 1000;
        fps = fps * 0.9 + 1000 / Math.Max(frameMs, 1) * 0.1;

        // Fixed timestep: speed changes how many ticks run per frame, never the tick length (rule 6).
        int ticks = 0;
        if (!view.paused)
        {
            accumulatorMs += Math.Min(frameMs, MAX_FRAME_MS) * view.speed;
            ticks = (int)Math.Floor(accumulatorMs / MS_PER_TICK);
            if (ticks > MAX_TICKS_PER_FRAME) { ticks = MAX_TICKS_PER_FRAME; accumulatorMs = 0; }
            else accumulatorMs -= ticks * MS_PER_TICK;
        }
        var sw = Stopwatch.StartNew();
        if (ticks > 0) Tick.step(state, ticks);
        double ms = sw.Elapsed.TotalMilliseconds;
        simMs = simMs * 0.9 + ms * 0.1;
        if (ticks > 0)
        {
            tickMs = tickMs * 0.9 + ms / ticks * 0.1;
            worstTickMs = Math.Max(worstTickMs * 0.999, ms / ticks);
        }
        if (Dawns(state.tick) != lastAutosaveDawn)
        {
            lastAutosaveDawn = Dawns(state.tick);
            if (Saves.saveToSlot(Saves.AUTO_SLOT, state) == null) view.Toast($"Autosaved at dawn, day {Time.dayOf(state.tick)}.");
            // Start the command log afresh from here, so it doesn't grow all game: a bug report then replays from this dawn.
            state.commandLog = new();
            initial = States.cloneState(state);
        }

        UpdateInput(frameMs);
        if (view.selectedId != null && !state.survivors.Exists((x) => x.id == view.selectedId)) view.selectedId = null;
        alpha = Math.Min(1, accumulatorMs / MS_PER_TICK);
        // The camera follows whoever you're steering.
        var you = ControlMod.controlled(state);
        if (you != null)
        {
            var w = worldPos(you, alpha);
            view.camera = new Cam(w.x, w.y, view.camera.zoom);
        }
        QueueRedraw();
        string perf = $"fps            {fps:0}\ntick ms        {tickMs:0.00} (worst lately {worstTickMs:0.0})\nsim ms/frame   {simMs:0.00}\ndraw ms/frame  {drawMs:0.00}";
        hud.Update(state, perf);
        worldMap.Update(state);

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
        painter.draw(state, view.camera, Vp, new DrawOptions
        {
            hover = view.hover,
            hoverEdge = view.tool != null ? view.hoverEdge : null,
            tool = view.tool,
            alpha = alpha,
            selectedId = view.selectedId,
            showDebug = view.showDebug,
            areaDrag = view.areaDrag,
        });
        drawMs = drawMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
    }

    // --- input (input.ts) ---------------------------------------------------------------------------

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
                var w = screenToWorld(view.camera, Vp, m.X, m.Y);
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
        // panByScreen moves the world with the drag; keys move the camera, so negate.
        view.camera = panByScreen(view.camera, -dx * distance, -dy * distance);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            OnKey(k);
            return;
        }
        if (view.menu != null || view.showMap) return;
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
        if (state.control != null)
        {
            Command(new Command { type = "swing", x = tile.x, y = tile.y });
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
        if (state.control != null)
        {
            Command(new Command { type = "use", x = tile.x, y = tile.y });
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
        if (view.showMap)
        {
            if (key is Key.Escape or Key.M) view.showMap = false;
            return;
        }
        var c = state.control;
        if (key == Key.Escape && view.tool == null && !view.buildOpen && view.selectedId == null && !view.showOverview)
        {
            ToggleMenu();
            return;
        }
        // Playing one survivor: the colony's keys (build, priorities, overview) bring its management out first.
        if (view.survivorMode(c != null) && key is Key.B or Key.P or Key.O) view.colonyMode = true;
        if (key == Key.G && c != null)
        {
            view.toggleColonyMode();
            return;
        }
        if (Tools.handleBuildKey(view, key, k.ShiftPressed))
        {
            if (view.tool == null) painting = null;
            return;
        }
        switch (key)
        {
            case Key.Space: view.paused = !view.paused; return;
            case Key.Quoteleft: view.showDebug = !view.showDebug; return;
            case Key.P: view.showPriorities = !view.showPriorities; return;
            case Key.M: view.showMap = !view.showMap; return;
            case Key.N: view.showMinimap = !view.showMinimap; return;
            case Key.O: view.showOverview = !view.showOverview; return;
            case Key.K: view.showKeys = !view.showKeys; return;
            case Key.Home: view.camera = centredOn(state.grid.width, state.grid.height, view.camera.zoom); return;
            case Key.F:
                if (c != null) Command(new Command { type = "takeControl", survivorId = null });
                else if (view.selectedId != null) Command(new Command { type = "takeControl", survivorId = view.selectedId });
                return;
        }
        var you = ControlMod.controlled(state);
        if (c == null || you == null) return;
        var at = you.pos;
        switch (key)
        {
            case >= Key.Key1 and <= Key.Key9:
            {
                // Quick slots: the bag's first nine items (inventory.ts).
                int slot = (int)(key - Key.Key1);
                if (slot < you.pack.Count) Command(new Command { type = "useItem", itemId = you.pack[slot] });
                break;
            }
            case Key.R: Command(new Command { type = "work", x = at.x + c.facing.x, y = at.y + c.facing.y }); break;
            case Key.L: if (view.hover != null) Command(new Command { type = "shout", order = "loot", x = view.hover.x, y = view.hover.y }); break;
            case Key.C: Command(new Command { type = "sneak", on = c.sneaking != true }); break;
            case Key.T: Command(new Command { type = "talk" }); break;
            case Key.V:
            case Key.H: Command(new Command { type = "shout", order = key == Key.V ? "follow" : "hold" }); break;
            case Key.E:
                // The tile in front, else the one underfoot.
                if (!Command(new Command { type = "use", x = at.x + c.facing.x, y = at.y + c.facing.y })) Command(new Command { type = "use", x = at.x, y = at.y });
                break;
        }
    }
}
