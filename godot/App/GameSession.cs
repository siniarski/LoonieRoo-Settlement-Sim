// Which game is running (Game is partial: App/Game.cs lists the parts): replacing it with a new world, a save, an
// import or an arena; saving and exporting; the bug report; and what the HUD and the menu ask of the game
// (IMenuActions, IGameFiles). A big world's plan is made on a worker thread, behind a note.

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
    const string SOLO_TOAST = "Alone. The mouse looks, WASD walks, click to swing, right-click or E to use. Tab: inventory. M: the map and the colony.";
    const string GROUP_TOAST = "You and two others. The mouse looks, WASD walks, click to swing, right-click or E to use. Tab: inventory. M: the map and the colony (F there plays someone else).";

    static double RandomSeed() => Math.Floor(Random.Shared.NextDouble() * 1e9);

    static Settings CopySettings(Settings s) => new() { start = s.start, zombies = s.zombies, weather = s.weather, progression = s.progression, winter = s.winter, dayScale = s.dayScale, wildfire = s.wildfire, map = s.map };

    /// <summary>Replace the running game (a save, an import, a new world) and start the bug-report log afresh. Returns whether it did.</summary>
    bool Replace(LoadResult result, string from)
    {
        if (!result.ok || result.state == null)
        {
            view.Toast($"Couldn't load {from}: {result.error}");
            return false;
        }
        state = result.state;
        initial = Savefile.bugStart(state);
        Time.setDayScale(state.settings.dayScale); // the clock reads right before the first tick
        view.camera = centredOn(state.grid.width, state.grid.height, view.camera.zoom == 0 ? 1 : view.camera.zoom);
        view.paused = false;
        Pathfinding.warmReach(state.grid, state.walkabilityVersion); // now, not mid-play
        lastAutosaveDawn = Dawns(state.tick);
        view.Toast($"Loaded {from}{(result.kind == "bug" ? " (bug report, replayed)" : "")}.");
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
        // The menu holds the world still while it's up (view.halted).
        if (view.menu == null) view.menu = "main";
        else if (view.inGame) view.menu = null;
        menu.Refresh();
    }

    // --- what the HUD and the menu ask of the game ---

    public bool Command(Command cmd)
    {
        var result = Commands.applyCommand(state, cmd);
        if (cmd.type == "takeControl" && result.ok) view.selectedId = cmd.survivorId ?? view.selectedId;
        // Work refused (R: nothing to repair or build there, too far): the sim changes nothing, so say why here.
        if (cmd.type is "work" or "quick" or "holster" && !result.ok && result.reason != null) view.Toast(result.reason, 2500);
        return result.ok;
    }

    public void OpenMenu() => ToggleMenu();

    /// <summary>The one you played died: the death screen is up (Ui/DeathScreen.cs), first person is off and the world is held until you choose.</summary>
    bool DeathUp => DeathScreen.Wanted(state, view);
    bool AwaitingHeir() => state.succession != null;

    public void NewGamePlus()
    {
        // The browser confirms first; the helicopter panel's button is the confirmation here.
        Replace(new LoadResult { ok = true, state = Escape.newGamePlus(state, RandomSeed()), kind = "save" }, "a new world (new game+)");
        view.selectedId = state.control?.survivorId;
    }

    public void Resume() => ToggleMenu();

    /// <summary>A big world's plan being made on a worker thread (Worldplan.makePlan is pure), and what to make with it.</summary>
    (System.Threading.Tasks.Task<WorldRegions> task, double seed, double size, Settings settings, Character you, ulong started, int told)? making;
    /// <summary>Worlds bigger than this make their plan on a worker thread, behind a "making the world" note; smaller ones take milliseconds.</summary>
    const double PLAN_ON_A_THREAD_OVER = 512;

    public bool Making => making != null;

    public void NewGame(double size, Settings settings, double? seed, Character you)
    {
        if (making != null) return; // one at a time
        double s = seed ?? RandomSeed();
        if (size > PLAN_ON_A_THREAD_OVER)
        {
            // The regions the menu's picture began planning, if it handed them over (Worldplan.remember), else new ones.
            var regions = Worldplan.regions(s, size, settings.map);
            var task = System.Threading.Tasks.Task.Run(() => Worldplan.planStart(regions));
            making = (task, s, size, settings, you, Godot.Time.GetTicksMsec(), 0);
            view.Toast($"Making a {size}×{size} world...");
            return;
        }
        FinishNewGame(s, size, settings, you);
    }

    /// <summary>Each frame while a big world's plan is being made: a note every few seconds, and the world once it's done.</summary>
    void WatchMaking()
    {
        if (making is not { } m) return;
        if (!m.task.IsCompleted)
        {
            int secs = (int)((Godot.Time.GetTicksMsec() - m.started) / 1000);
            if (secs >= m.told + 3) { view.Toast($"Making a {m.size}×{m.size} world: towns and roads ({secs} s)..."); making = m with { told = secs }; }
            return;
        }
        making = null;
        if (m.task.IsFaulted) { view.Toast($"Couldn't make the world: {m.task.Exception?.GetBaseException().Message}"); return; }
        Worldplan.remember(m.seed, m.size, m.settings.map, m.task.Result);
        FinishNewGame(m.seed, m.size, m.settings, m.you);
    }

    void FinishNewGame(double seed, double size, Settings settings, Character you)
    {
        var world = Worldgen.createWorld(seed, size, settings, you);
        if (!Replace(new LoadResult { ok = true, state = world, kind = "save" }, $"a new {size}×{size} world")) return;
        Play();
        // Every start is first person on your survivor: a group start plays the one you made (the
        // first; the other two are colonists). Through a command, so a bug report replays it.
        if (state.control == null && state.survivors.Count > 0) Command(new Command { type = "takeControl", survivorId = state.survivors[0].id });
        // The old game's M screen, tool or inventory don't carry over (the HUD only shuts them when
        // whom you play changes, and a new world's first survivor can have the old one's id).
        view.closeMScreen();
        view.showInventory = false;
        view.Toast(settings.start == "solo" ? SOLO_TOAST : GROUP_TOAST);
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
}
