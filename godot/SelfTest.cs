// `--selftest`: drives the running game with real input events (through Godot's input pipeline,
// so the HUD's buttons and the game's handlers both see them) and checks what they did: walking,
// a jump with Space, the map and the menu pausing the game, the Options page (SelfTestOptions.cs), the build bar and a wall placed with the mouse, the map, the menu, a HUD button, a
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

    /// <summary>Before the self-test builds anything: every asset Godot imports (res://**/*.import) has its imported
    /// file in .godot/imported. A fresh checkout has none, and a model that won't load throws in _Ready, so the
    /// test would never start; this says so and quits instead. Returns whether it quit.</summary>
    bool SelfTestQuitOnUnimported()
    {
        var missing = new List<string>();
        FindUnimported("res://", missing);
        if (missing.Count == 0) return false;
        GD.Print($"FAIL  Assets imported  ({missing.Count} not imported, first {string.Join(", ", missing.Take(3))})");
        GD.Print($"{(shotPath != null ? "shot" : "selftest")}: FAILED, assets not imported: run `godot --headless --path godot --import` once (./check.sh --all does)");
        // _Ready stops here: nothing is built, so nothing may run or draw until the quit lands.
        SetProcess(false);
        SetProcessInput(false);
        SetProcessUnhandledInput(false);
        Visible = false;
        GetTree().Quit(1);
        return true;
    }

    static void FindUnimported(string dir, List<string> missing)
    {
        foreach (var sub in DirAccess.GetDirectoriesAt(dir))
            if (!sub.StartsWith('.')) FindUnimported(dir + sub + "/", missing);
        foreach (var f in DirAccess.GetFilesAt(dir))
        {
            if (!f.EndsWith(".import")) continue;
            var cfg = new ConfigFile();
            if (cfg.Load(dir + f) != Error.Ok || !cfg.HasSection("remap")) continue;
            if (cfg.HasSectionKey("remap", "valid") && !(bool)cfg.GetValue("remap", "valid")) continue;
            // One path per platform's format (path.s3tc, path.etc2): any one imported will do.
            var paths = cfg.GetSectionKeys("remap").Where(k => k == "path" || k.StartsWith("path.")).Select(k => (string)cfg.GetValue("remap", k)).ToList();
            if (paths.Count > 0 && !paths.Any(FileAccess.FileExists)) missing.Add(dir + f[..^".import".Length]);
        }
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

    /// <summary>Runs the test; a step that throws fails it and quits (instead of leaving the window up until check.sh's timeout).</summary>
    async Task SelfTest()
    {
        try { await SelfTestSteps(); }
        catch (Exception ex)
        {
            GD.PrintErr(ex.ToString());
            GD.Print($"selftest: FAILED, a step threw: {ex.GetType().Name}: {ex.Message}");
            GetTree().Quit(failures.Count + 1);
        }
    }

    /// <summary>A 2048 world: its plan is made on a worker thread (the game keeps drawing frames meanwhile), then the
    /// world arrives and you're on your survivor in first person.</summary>
    async Task BigNewGame()
    {
        int before = state.grid.width;
        var settings = CopySettings(Difficulty.NORMAL);
        settings.start = "solo";
        ulong t0 = Godot.Time.GetTicksMsec();
        NewGame(2048, settings, 42, new Character { name = "Ash", job = 0, traits = new() });
        await Frames(2);
        Check("A 2048 world's plan is made on a thread (frames go on meanwhile)", making != null && state.grid.width == before, $"making {making != null}, width {state.grid.width}");
        int frames = 0;
        while (making != null && frames < 3000) { await Frames(1); frames++; }
        Check("Then the 2048 world arrives, first person on your survivor", state.grid.width == 2048 && view.firstPerson,
            $"width {state.grid.width}, {frames} frames, {(Godot.Time.GetTicksMsec() - t0) / 1000.0:0.0} s");
    }

    async Task SelfTestSteps()
    {
        selfTesting = true;
        // Godot merges mouse motions queued in one frame into one event, keeping the first one's device:
        // a real movement could ride in on the test's event past DropForeignMouse. One event each, then.
        Input.UseAccumulatedInput = false;
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
        // A stray real movement in the same frame (device 0, as the real mouse's) must change nothing: with
        // input accumulation on, Godot would merge it into the test's event and turn you 0.02 too far.
        Input.ParseInputEvent(new InputEventMouseMotion { Device = TEST_DEVICE, Relative = new Vector2((float)(Math.PI / 2 / LOOK_PER_PX), 0) });
        Input.ParseInputEvent(new InputEventMouseMotion { Device = 0, Relative = new Vector2(8, 0) });
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
        await SheetSteps();
        await InventorySteps();

        // Space jumps (no pause key now): you leave the ground, the eye rises and comes back down, and the game plays on.
        you = ControlMod.controlled(state)!;
        float ground = World3D.FirstPerson.Eye(state, you, 1).eye.Y, top = ground;
        int tick = state.tick;
        // Your body goes up with the eye (People.Leap.cs): its lift each frame is the eye's over its standing height, its
        // feet leave the ground and come back to it; and the jump is heard (Yours.cs).
        var ppl = firstPerson.People;
        float feet0 = ppl.YourFeetUp, bodyTop = 0, feetTop = 0, apart = 0;
        var yours = firstPerson.sounds.yours;
        int pushes = yours.Pushes, landings = yours.Landings;
        void Watch()
        {
            top = Math.Max(top, World3D.FirstPerson.Eye(state, ControlMod.controlled(state)!, 0.5).eye.Y);
            bodyTop = Math.Max(bodyTop, ppl.YourLift);
            feetTop = Math.Max(feetTop, ppl.YourFeetUp - feet0);
            apart = Math.Max(apart, Math.Abs(firstPerson.EyeUp - World3D.FirstPerson.EYE - ppl.YourLift));
        }
        await Tap(Godot.Key.Space);
        string leap = state.control!.busy?.what ?? "none";
        for (int i = 0; i < 120 && Leaps.airborne(state.control!); i++) { await Frames(1); Watch(); }
        // Landed: the landing's knee bend and the blend back to standing (People.LAND_FOR, the 0.25 s cross-fade).
        for (int i = 0; i < 40; i++) { await Frames(1); Watch(); }
        float after = World3D.FirstPerson.Eye(state, ControlMod.controlled(state)!, 1).eye.Y;
        Check("Space jumps: the eye rises and lands, and the game plays on", Leaps.isLeap(leap) && top > ground + 0.1f && Math.Abs(after - ground) < 0.05f && !view.halted && state.tick > tick,
            $"{leap}, eye {ground:0.00} -> top {top:0.00} -> {after:0.00}, tick {tick} -> {state.tick}");
        Check("Your body leaps with the eye: lifted as high, its feet off the ground and back on it", bodyTop > 0.2f && apart < 0.03f && feetTop > 0.15f && Math.Abs(ppl.YourLift) < 0.01f && Math.Abs(ppl.YourFeetUp - feet0) < 0.05f,
            $"body up {bodyTop:0.00} m, most apart from the eye {apart:0.000} m, feet up {feetTop:0.00} m (standing {feet0:0.00}), after {ppl.YourLift:0.00} / feet {ppl.YourFeetUp - feet0:0.00}");
        Check("A jump is heard going up and landing, on the ground under you", yours.Pushes > pushes && yours.Landings > landings && yours.LandSurface.StartsWith("step_"),
            $"push-offs {pushes} -> {yours.Pushes}, landings {landings} -> {yours.Landings}, on {yours.LandSurface}");
        await HeldSteps();

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
        Check("Clicking 2x sets the speed", two != null && view.speed == 2 && !view.halted, $"speed {view.speed}, halted {view.halted}");
        view.speed = 1;

        // Esc in the Colony tab: back to first person.
        await Tap(Godot.Key.Escape);
        await Frames(1);
        Check("Esc in the Colony tab goes back to first person", view.firstPerson && !view.colonyMode);

        // M opens the map; its Colony tab switches over; M goes back to first person.
        await Tap(Godot.Key.M);
        await Frames(1);
        Check("M opens the map", view.showMap && worldMap.Visible && tabs.Visible);
        await StopsTicks("The map pauses the game");
        var colonyTab = FindButton(tabs, "Colony");
        if (colonyTab != null) await Click(colonyTab.GetGlobalRect().GetCenter());
        await Frames(1);
        Check("The Colony tab shows the colony", !view.showMap && view.colonyMode && !view.firstPerson && !worldMap.Visible);
        await Tap(Godot.Key.M);
        await Frames(1);
        Check("M goes back to first person", view.firstPerson && !view.showMap && !view.colonyMode);
        await PlaysOn("Closing the map plays on");

        // Esc opens the menu; Save to slot 3 from it; Resume.
        await Tap(Godot.Key.Escape);
        await Frames(2);
        Check("Esc opens the menu", view.menu == "main" && menu.Visible, view.menu ?? "none");
        await StopsTicks("The menu pauses the game");
        // The volume slider sets the master bus (and is put back as it was: it's the player's setting).
        var volume = FindNamed<HSlider>(menu, "Volume");
        float had = Audio.Volume.Level;
        if (volume != null) volume.Value = 40;
        Check("The menu's volume slider sets the volume", volume != null && Math.Abs(Audio.Volume.Level - 0.4f) < 0.01f && Math.Abs(AudioServer.GetBusVolumeDb(0) - Mathf.LinearToDb(0.16f)) < 0.1f, $"level {Audio.Volume.Level:0.00}, bus {AudioServer.GetBusVolumeDb(0):0.0} dB");
        Audio.Volume.Set(had);
        await OptionsSteps();
        var slot = FindButton(menu, "3");
        if (slot != null) await Click(slot.GetGlobalRect().GetCenter());
        Check("Save to slot 3", Saves.slotLabel("3") != null);
        int savedTick = state.tick;
        var resume = FindButton(menu, "Resume");
        if (resume != null) await Click(resume.GetGlobalRect().GetCenter());
        await Frames(2);
        Check("Resume closes the menu", view.menu == null && !menu.Visible);
        await PlaysOn("Closing the menu plays on");

        // Play on a little, then load slot 3 back: the clock goes back to when it was saved.
        await Frames(20);
        // The tick read as it loads (Load is synchronous): loads play at once, so a tick can come in the next frames.
        Load("3");
        // The clock as loaded (Load is synchronous): a slow frame after it may already run a tick.
        int loadedTick = state.tick;
        await Frames(2);
        Check("Load slot 3 brings the save back", loadedTick == savedTick, $"tick {loadedTick}, saved at {savedTick}");
        await LoadScreenPicture();

        await SoundTest();

        // New game from the menu, from the Colony tab of this one: a group start (the default
        // preset), then a solo one. Each lands in first person on your survivor, playing at once.
        await NewGameFromMenu("Survivor", "group");
        await NewGameFromMenu("Apocalypse", "solo");
        await BigNewGame();

        GD.Print(failures.Count == 0 ? "selftest: all passed" : $"selftest: {failures.Count} FAILED: {string.Join(", ", failures)}");
        GetTree().Quit(failures.Count);
    }

    /// <summary>B (the Colony tab), the HUD's Menu button, New game, a preset, Next, Start: checks you're in first person on your survivor.</summary>
    /// <summary>What you hold: a club in your right hand, a click swings it, a rifle takes both hands.
    /// The items are put in your hands directly (setup, like the sound test's zombie), then taken away.</summary>
    async Task HeldSteps()
    {
        var you = ControlMod.controlled(state)!;
        var held = firstPerson.Held;
        string? had = you.weapon;
        Item Arm(string kind)
        {
            var item = Sim.Items.spawnItem(state, kind, you.pos.x, you.pos.y);
            item.pos = null;
            item.carriedBy = you.id;
            you.weapon = item.id;
            return item;
        }
        var club = Arm("weapon");
        await Frames(30);
        Check("A club in hand is drawn in your right hand, one-handed", held.Right.StartsWith("weapon") && held.RightHand && !held.TwoHanded, $"right '{held.Right}', two-handed {held.TwoHanded}");
        var rest = held.RightAt;
        double swung = state.control!.swungTick ?? -1;
        await Click(GetViewport().GetVisibleRect().GetCenter());
        float far = 0;
        for (int i = 0; i < 8; i++) { await Frames(1); far = Math.Max(far, held.RightAt.DistanceTo(rest)); }
        Check("A click swings the club: the hand moves through the blow", (state.control!.swungTick ?? -1) > swung && far > 0.15f, $"swungTick {swung} -> {state.control!.swungTick}, moved {far:0.00} m");
        for (int i = 0; i < 40 && held.RightAt.DistanceTo(rest) > 0.03f; i++) await Frames(1);
        Check("And comes back to rest", held.RightAt.DistanceTo(rest) < 0.03f, $"{held.RightAt.DistanceTo(rest):0.000} m off");
        Sim.Items.removeItem(state, club);
        var gun = Arm("gun");
        await Frames(30);
        Check("A rifle is held in both hands", held.Right.StartsWith("gun") && held.TwoHanded && held.RightHand && held.LeftHand, $"right '{held.Right}', two-handed {held.TwoHanded}");
        Sim.Items.removeItem(state, gun);
        you.weapon = had;
        await Frames(2);
    }

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
            if (text == "New game" && start == "group")
            {
                var shown = await PictureDone();
                Check("The New game screen draws the world's picture (land, towns, roads)", shown != null && shown.IsVisibleInTree() && shown.Stage == "done", $"stage '{shown?.Stage}'");
                if (OS.GetEnvironment("SELFTEST_SHOT") is { Length: > 0 } newShot)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng($"{newShot}-new.png");
                }
            }
        }
        // Start shows the making screen first, then makes the world.
        for (int f = 0; f < 120 && view.menu != null; f++) await Frames(1);
        await Frames(3);
        var you = ControlMod.controlled(state);
        Check($"New game ({start}) from the menu is first person on your survivor",
            state.settings.start == start && you != null && you.id == state.survivors[0].id && view.menu == null && !view.colonyMode && !view.buildOpen && view.firstPerson && firstPerson.Visible,
            $"start {state.settings.start}, playing {you?.id ?? "nobody"}, menu {view.menu ?? "none"}, colony {view.colonyMode}, first person {view.firstPerson}");
        // SELFTEST_SHOT=prefix: save what the new start looks like (prefix-group.png, prefix-solo.png).
        if (OS.GetEnvironment("SELFTEST_SHOT") is { Length: > 0 } shot)
        {
            await Frames(5);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{shot}-{start}.png");
        }
        int tick = state.tick;
        await Frames(10);
        Check($"New game ({start}) plays at once, no banner", !view.halted && state.tick > tick && FindLabel(hud, "PAUSED") == null, $"tick {tick} -> {state.tick}");
    }

    /// <summary>The menu's world picture once it has finished drawing (or after 20 s), or null if the menu has none.</summary>
    async System.Threading.Tasks.Task<WorldPreviewView?> PictureDone()
    {
        WorldPreviewView? p = null;
        for (int f = 0; f < 1200; f++)
        {
            p = menu.FindChild("WorldPreview", true, false) as WorldPreviewView;
            if (p != null && p.Stage == "done") break;
            await Frames(1);
        }
        return p;
    }

    /// <summary>The Load screen shows the picture of the newest save (slot 3, just saved: a generated world).</summary>
    async Task LoadScreenPicture()
    {
        view.menu = "load";
        menu.Refresh();
        await Frames(2);
        var shown = await PictureDone();
        Check("The Load screen draws the newest save's world", shown != null && shown.IsVisibleInTree() && shown.Stage == "done", $"stage '{shown?.Stage}'");
        if (OS.GetEnvironment("SELFTEST_SHOT") is { Length: > 0 } shot)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{shot}-load.png");
        }
        view.menu = null;
        menu.Refresh();
        await Frames(2);
    }

    /// <summary>No ticks go by over a few frames (the map or the menu is up).</summary>
    async Task StopsTicks(string what)
    {
        int tick = state.tick;
        await Frames(10);
        Check(what, view.halted && state.tick == tick, $"tick {tick} -> {state.tick}");
    }

    /// <summary>Ticks go by again.</summary>
    async Task PlaysOn(string what)
    {
        int tick = state.tick;
        await Frames(10);
        Check(what, !view.halted && state.tick > tick, $"tick {tick} -> {state.tick}");
    }

    /// <summary>Sound: the made sounds aren't silent (written to user://sounds-preview to listen to), and a zombie
    /// put beside you snarls when it sees you, and a blow on it and the killing blow sound from where it is.</summary>
    async Task SoundTest()
    {
        var sounds = firstPerson.sounds;
        DirAccess.MakeDirRecursiveAbsolute("user://sounds-preview");
        sounds.bank.Wait();
        var levels = new List<string>();
        bool loud = true;
        foreach (var kind in Audio.Bank.KINDS.Select((k) => k.name))
        {
            var set = sounds.Kind(kind);
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] is not AudioStreamWav w) continue;
                w.SaveToWav($"user://sounds-preview/{kind}_{i}.wav");
                var data = w.Data;
                var f = new float[data.Length / 2];
                for (int j = 0; j < f.Length; j++) f[j] = (short)(data[2 * j] | (data[2 * j + 1] << 8)) / 32768f;
                double rms = Audio.Synth.Rms(f);
                loud &= rms > 0.05;
                if (i == 0) levels.Add($"{kind} {f.Length / (double)Audio.Synth.RATE:0.0}s rms {rms:0.00}");
            }
        }
        Check("The made sounds aren't silent", loud, string.Join(", ", levels));

        view.paused = false;
        var you = ControlMod.controlled(state)!;
        int notice0 = sounds.Played["notice"], hit0 = sounds.Played["hit"], kill0 = sounds.Played["kill"];
        // Two tiles the way you look, on floor.
        var dir = state.control!.aim ?? state.control.facing;
        var spot = Space.nearestTile(state.grid, new Point { x = you.pos.x + Math.Sign(dir.x) * 2, y = you.pos.y + Math.Sign(dir.y) * 2 }, (x, y) => Grids.getTile(state.grid, x, y) == "floor" && (x != you.pos.x || y != you.pos.y));
        var z = Zombies.addZombie(state, spot!.x, spot.y, 100, "static");
        for (int i = 0; i < 120 && z.targetId != you.id; i++) await Frames(1);
        await Frames(2);
        var zAt = new Vector3(z.at.x / (float)Motion.SUB, 0, z.at.y / (float)Motion.SUB);
        var heard = new Vector3(sounds.LastAt.X, 0, sounds.LastAt.Z);
        Check("A zombie that sees you snarls, from where it is", z.targetId == you.id && sounds.Played["notice"] > notice0 && heard.DistanceTo(zAt) < 1.5f, $"target {z.targetId ?? "none"}, notices {sounds.Played["notice"] - notice0}, {heard.DistanceTo(zAt):0.00} m off");
        Zombies.damageZombie(state, z, 1, you);
        await Frames(2);
        Check("A blow landing on it thuds", sounds.Played["hit"] > hit0);
        Zombies.damageZombie(state, z, 1000, you);
        await Frames(2);
        Check("The killing blow thuds where it stood", sounds.Played["kill"] > kill0, $"kills heard {sounds.Played["kill"] - kill0}");
        // One that comes into being already after you (it woke, say, and saw you in the same tick) snarls too.
        view.paused = true;
        int notice1 = sounds.Played["notice"];
        var z2 = Zombies.addZombie(state, spot.x, spot.y, 100, "static");
        z2.targetId = you.id;
        await Frames(3);
        Check("A zombie that appears already after you snarls", sounds.Played["notice"] > notice1, $"notices {sounds.Played["notice"] - notice1}");
        Zombies.damageZombie(state, z2, 1000, you);
        await Frames(2);
        await SoundSteps();
    }

    static T? FindNamed<T>(Node root, string name) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T t && child.Name == name) return t;
            var found = FindNamed<T>(child, name);
            if (found != null) return found;
        }
        return null;
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
