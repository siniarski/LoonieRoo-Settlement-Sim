// The game (src/app.ts, src/ui/loop.ts): runs the C# sim (cs/Sim) at a fixed timestep, draws it, shows the HUD,
// menu and map (Ui/), and turns input into sim commands. The sim is only ever changed through
// Commands.applyCommand and Tick.step (rule 5); the debug flags' hand-set scenes go through Sim.Staging.
//
// The class is in parts, one concern each (all in App/):
//   Game.cs         this file: what the game is made of (_Ready), the frame (_Process: ticks, then the views), _Draw
//   GameArgs.cs     the command line: which world to start, the debug flags, the view to start with
//   GameSession.cs  which game is running: new, load, save, import, bug report, the menu's actions
//   GameInput.cs    keys and mouse into commands (first person and the Colony tab)
//   GameShots.cs    --shot: pictures of the running game
//   GameText.cs     --trace, --textmap: the same run as text
//   SelfTest/       --selftest: the game driven by real input events, checked
//
// The views (2026-11-08): playing a survivor, you see through their eyes (World3D/, first person:
// the mouse looks, WASD walks the way you face, Tab the inventory). M is the map screen, top-down,
// with two tabs: Map (Ui/WorldMap) and Colony (the colony's management, drawn in 2D top-down by
// Render/). With nobody under your control, the Colony view is the game. No isometric view.

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

public partial class Game : Node2D, IGameFiles, IMenuActions
{
    // --- the loop ---
    const double MS_PER_TICK = 1000.0 / Tick.TICKS_PER_SECOND;
    /// <summary>Frames longer than this (window in background, breakpoint) are clamped.</summary>
    const double MAX_FRAME_MS = 250;
    /// <summary>Never run more than this many ticks in one frame, to avoid a spiral of death.</summary>
    const int MAX_TICKS_PER_FRAME = 40;
    /// <summary>Screen pixels per second for keyboard panning.</summary>
    const double KEY_PAN_PX_PER_SEC = 600;
    /// <summary>First person: directions the aim snaps to (1.4 degrees), and the walk (5.6 degrees).</summary>
    const int FP_AIM_STEPS = 256, FP_STEER_STEPS = 64;
    /// <summary>First person: radians of look per pixel of mouse travel.</summary>
    const double LOOK_PER_PX = 0.0025;
    /// <summary>First person: you use what you look at on the ground this near (m), else what's in front of you.</summary>
    const double USE_REACH = 1.6, LOOK_REACH = 40;
    /// <summary>A right-button press that moves less than this is a click, not a pan.</summary>
    const double CLICK_SLOP_PX = 4;

    public SimState state = null!;
    /// <summary>Where a bug report replays from: the state at the start of play (or the last dawn), packed (Savefile.bugStart).</summary>
    Savefile.BugStart initial = null!;
    readonly View view = new();
    double accumulatorMs;
    double alpha;
    int lastAutosaveDawn;

    Ctx ctx = null!;
    Painter painter = null!;
    Hud hud = null!;
    InventoryScreen inventory = null!;
    Menu menu = null!;
    WorldMap worldMap = null!;
    ScreenTabs tabs = null!;
    World3D.FirstPerson firstPerson = null!;
    World3D.Fires fires = null!;
    /// <summary>Last frame was first person (to pick up the survivor's aim on the way in).</summary>
    bool wasFirstPerson;
    bool selftest;
    /// <summary>--look=degrees (0 east, 90 south) and --pitch=degrees: where first person looks at the start.</summary>
    double? lookArg, pitchArg;
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
    double fps, tickMs, simMs, drawMs, worstTickMs, fpMs;

    /// <summary>Frames drawn (the self-test and --shot count them).</summary>
    int frame;

    public override void _Ready()
    {
        // The sim formats numbers JS's way; keep the UI (and CSS colour strings) culture-free too.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var args = ParseArgs();
        if (args.TryGetValue("shot", out var shot)) shotPath = shot;
        if (args.TryGetValue("frames", out var f)) shotFrames = int.Parse(f);
        // A --selftest or --shot with assets not imported would throw in _Ready: say so and quit.
        if ((args.ContainsKey("selftest") || shotPath != null) && SelfTestQuitOnUnimported()) return;
        // The shot is taken from the tree's frame signal, not from _Process: a frame that throws (or a _Ready that
        // does) still gets its picture and quits, instead of waiting out the caller's timeout.
        if (shotPath != null) GetTree().ProcessFrame += ShotFrame;

        BuildViews();
        StartFromArgs(args); // GameArgs.cs
    }

    /// <summary>What the game is made of: the 2D painter (the Colony tab), first person, and the UI over them.</summary>
    void BuildViews()
    {
        ctx = new Ctx(this);
        painter = new Painter(ctx);
        firstPerson = new World3D.FirstPerson { Visible = false };
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        AddChild(firstPerson);
        fires = new World3D.Fires();
        firstPerson.AddChild(fires);
        var layer = new CanvasLayer();
        AddChild(layer);
        hud = new Hud(view, this);
        layer.AddChild(hud);
        inventory = new InventoryScreen(view, (cmd) => Commands.applyCommand(state, cmd));
        layer.AddChild(inventory);
        worldMap = new WorldMap(view, Command);
        layer.AddChild(worldMap);
        tabs = new ScreenTabs(view);
        layer.AddChild(tabs);
        menu = new Menu(view, this);
        layer.AddChild(menu);
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
        WatchMaking();

        // Fixed timestep: speed changes how many ticks run per frame, never the tick length (rule 6).
        // No ticks while the menu or the map is up, or while someone has to carry on after you die.
        int ticks = 0;
        if (shotAt != null) AdvanceToShotTick(); // --shot-at: the sim only moves to the next listed tick, a tick at a time
        else if (!view.halted && !AwaitingHeir())
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
            initial = Savefile.restartLog(state);
        }

        UpdateInput(frameMs);
        if (view.selectedId != null && !state.survivors.Exists((x) => x.id == view.selectedId)) view.selectedId = null;
        alpha = shotAt != null ? 1 : Math.Min(1, accumulatorMs / MS_PER_TICK); // a --shot-at picture shows the tick itself, not a blend
        var you = ControlMod.controlled(state);
        // First person unless the M screen is up (the menu goes over it).
        bool fpScene = you != null && !view.mScreen(true);
        view.firstPerson = fpScene && view.menu == null;
        if (!view.inventoryAllowed(you != null)) view.showInventory = false;
        bool lookSet = false;
        if (fpScene && !wasFirstPerson)
        {
            // Look the way they were aiming.
            var d = state.control!.aim ?? state.control.facing;
            lookSet = lookArg != null;
            view.yaw = lookArg ?? Math.Atan2(d.y, d.x);
            view.pitch = pitchArg ?? 0;
            lookArg = pitchArg = null;
        }
        wasFirstPerson = fpScene;
        firstPerson.Visible = fpScene;
        if (fpScene)
        {
            var fsw = Stopwatch.StartNew();
            // In a car the view turns with it (and faces forward as you get in, unless --look said where to look).
            view.yaw = (view.yaw + firstPerson.CarTurn(state, view.yaw, delta, !lookSet)) % (2 * Math.PI);
            firstPerson.Update(state, alpha, view.yaw, view.pitch, delta);
            fires.Update(state, firstPerson.camera.GlobalPosition, delta);
            fpMs = fpMs * 0.9 + fsw.Elapsed.TotalMilliseconds * 0.1;
            // The top-down camera keeps up with you, so the Colony tab opens where you are.
            var w = worldPos(you!, alpha);
            view.camera = new Cam(w.x, w.y, view.camera.zoom);
        }
        bool capture = view.firstPerson && !view.showInventory && (selftest || GetWindow().HasFocus());
        var mode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != mode) Input.MouseMode = mode;
        QueueRedraw();
        string perf = $"fps            {fps:0}\ntick ms        {tickMs:0.00} (worst lately {worstTickMs:0.0})\nsim ms/frame   {simMs:0.00}\ndraw ms/frame  {drawMs:0.00}"
            + (fpScene ? $"\n3D ms/frame    {fpMs:0.00} · {firstPerson.Modelled} people as models · {firstPerson.ChunkCount} chunks · {firstPerson.TreeCount} trees\nrender         cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()):0.00} ms · gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):0.00} ms" : "");
        hud.Update(state, perf);
        inventory.Update(state);
        worldMap.Update(state);
        tabs.Update(state);

        frame++;
    }

    public override void _Draw()
    {
        var sw = Stopwatch.StartNew();
        ctx.Begin();
        if (firstPerson.Visible) return; // the 3D view is the picture
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
}
