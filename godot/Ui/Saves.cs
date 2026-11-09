// Saving (src/ui/saves.ts): slots on disk under user://saves (Godot's per-user data folder), and
// files anywhere. The file formats themselves live in the sim (Savefile.cs, pure, tested).

using System;
using Godot;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.Ui;

public static class Saves
{
    public static readonly string[] SLOTS = { "1", "2", "3" };
    /// <summary>Written at every dawn; loadable like a slot, never saved to by hand.</summary>
    public const string AUTO_SLOT = "auto";

    /// <summary>Where slots live (the self-test points it elsewhere, so it never touches your saves).</summary>
    public static string DIR = "user://saves";
    static string DataPath(string slot) => $"{DIR}/slot-{slot}.json";
    static string MetaPath(string slot) => $"{DIR}/slot-{slot}.meta";

    /// <summary>When a slot was last saved to (unix seconds), or 0 if never.</summary>
    public static long slotTime(string slot)
    {
        var path = ProjectSettings.GlobalizePath(DataPath(slot));
        return System.IO.File.Exists(path) ? new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds() : 0;
    }

    /// <summary>One-line description of a slot's save, or null if empty.</summary>
    public static string? slotLabel(string slot)
    {
        var path = ProjectSettings.GlobalizePath(MetaPath(slot));
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
    }

    /// <summary>The newest save (a slot or the autosave), or null if there are none.</summary>
    public static string? newestSave()
    {
        string? best = null;
        long bestTime = -1;
        foreach (var s in new[] { SLOTS[0], SLOTS[1], SLOTS[2], AUTO_SLOT })
        {
            if (slotLabel(s) == null) continue;
            long t = slotTime(s);
            if (t > bestTime) { best = s; bestTime = t; }
        }
        return best;
    }

    /// <summary>Returns an error message, or null on success.</summary>
    public static string? saveToSlot(string slot, SimState state)
    {
        string label = $"Day {Time.dayOf(state.tick)} {Time.clockText(state.tick)} · {state.survivors.Count} survivor{(state.survivors.Count == 1 ? "" : "s")} · saved {DateTime.Now:g}";
        try
        {
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(DIR));
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(DataPath(slot)), Savefile.toSaveFile(state));
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(MetaPath(slot)), label);
            return null;
        }
        catch (Exception err)
        {
            return $"couldn't save: {err.Message}";
        }
    }

    public static LoadResult loadFromSlot(string slot)
    {
        var path = ProjectSettings.GlobalizePath(DataPath(slot));
        if (!System.IO.File.Exists(path)) return new LoadResult { ok = false, error = "that slot is empty" };
        try
        {
            return Savefile.loadFile(System.IO.File.ReadAllText(path));
        }
        catch (Exception err)
        {
            return new LoadResult { ok = false, error = $"couldn't read the save: {err.Message}" };
        }
    }

    /// <summary>A slot's picture of its world (from the head of the save: Savefile.readPreview), or null.</summary>
    public static SavePreview? slotPreview(string slot)
    {
        var path = ProjectSettings.GlobalizePath(DataPath(slot));
        try
        {
            return System.IO.File.Exists(path) ? Savefile.readPreview(System.IO.File.ReadAllBytes(path)) : null;
        }
        catch (System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>Filename-safe stamp like "day3-1030".</summary>
    public static string fileStamp(SimState state) => $"day{Time.dayOf(state.tick)}-{Time.clockText(state.tick).Replace(":", "")}";

    /// <summary>Where exported saves and bug reports go: the user's Documents/LoonieRoo folder.</summary>
    public static string ExportDir()
    {
        var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "LoonieRoo");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }
}
