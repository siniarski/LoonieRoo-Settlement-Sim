// The item benchmark (2026-11-09, the LOD question): first person in a field strewn with things, the camera
// turning a full circle at eye height, and what a frame costs as more are drawn. A world is generated (seed
// 42, 256 tiles) and every open tile within 60 m of the middle gets an item of a kind in turn (all 21 kinds,
// so every MultiMesh is in use: the worst case for draw calls). The sim isn't ticked (noon, still). Each run is
//   mode   all       the game's models (variants and all)
//          box       every item drawn as the one grey box (what a crude far LOD would cost at most)
//          none      nothing drawn (the scene without items, to subtract)
//   @N     the most drawn at once (the game's cap is 300, ItemModels.MAX_ITEMS)
// Reported: frame ms (mean, p95), render CPU and GPU ms, draw calls, primitives, items drawn. Vsync off.
//
//   Godot --path godot res://Labs/ItemBench.tscn -- [--runs=none@0,all@300,box@300,all@1000,all@3000]
//         [--frames=360] [--out=bench.md]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public partial class ItemBench : Node3D
{
    const int Warmup = 150;
    const int SPREAD = 60;
    static readonly HashSet<string> OPEN = new() { "grass", "dirt", "field", "forest" };
    static readonly string[] KINDS = ["wood", "food", "meal", "bandage", "medicine", "medkit", "weapon", "cloth", "coat", "armour", "boots",
        "gun", "ammo", "corpse", "book", "cart", "axe", "hammer", "sample", "scrap", "fuel", "knife", "hatchet", "machete", "crowbar", "bat", "pipe", "sledge", "spear", "plank", "pistol", "revolver", "shotgun", "rifle", "crossbow", "ammo38", "shells", "ammo308", "bolts"];

    readonly List<(string mode, int max)> runs = new();
    readonly StringBuilder report = new();
    readonly List<double> frameMs = new(), renderCpu = new(), renderGpu = new();
    int run = -1, frame, measureFrames = 360, drawn;
    long drawCalls, primitives;
    ulong lastUsec;
    string? outPath;
    SimState state = null!;
    FirstPerson? fp;

    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        foreach (var r in (args.GetValueOrDefault("runs") ?? "none@0,all@300,box@300,all@1000,box@1000,all@3000,box@3000").Split(','))
        {
            var p = r.Split('@');
            runs.Add((p[0], p.Length > 1 ? int.Parse(p[1]) : 300));
        }
        if (args.TryGetValue("frames", out var f)) measureFrames = int.Parse(f);
        args.TryGetValue("out", out outPath);

        state = Strewn(out int count);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        var size = GetViewport().GetVisibleRect().Size;
        report.AppendLine($"Item bench, {count} items within {SPREAD} m, {measureFrames} frames a run (a full turn), {size.X}x{size.Y}, Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine();
        report.AppendLine("| mode | cap | drawn | frame ms | p95 | render cpu | render gpu | draw calls | primitives |");
        report.AppendLine("|---|--:|--:|--:|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        NextRun();
    }

    /// <summary>A world with an item on every open tile near the middle, where the survivor you control stands.</summary>
    static SimState Strewn(out int count)
    {
        var settings = new Settings { start = "solo", zombies = Difficulty.NORMAL.zombies, weather = Difficulty.NORMAL.weather, progression = Difficulty.NORMAL.progression, winter = Difficulty.NORMAL.winter, dayScale = Difficulty.NORMAL.dayScale, map = Difficulty.NORMAL.map };
        var s = Worldgen.createWorld(42, 256, settings);
        var g = s.grid;
        var you = ControlMod.controlled(s)!;
        int cx = g.width / 2, cy = g.height / 2;
        you.pos = new Point(cx, cy);
        you.at = Motion.centre(you.pos);
        you.was = Motion.centre(you.pos);
        s.items.Clear();
        count = 0;
        for (int y = cy - SPREAD; y <= cy + SPREAD; y++)
            for (int x = cx - SPREAD; x <= cx + SPREAD; x++)
            {
                if (x < 0 || y < 0 || x >= g.width || y >= g.height) continue;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > SPREAD * SPREAD || (x == cx && y == cy)) continue;
                int i = y * g.width + x;
                string ground = Grids.groundAt(g, i) >= 0 && Grids.groundAt(g, i) < Grids.GROUNDS.Length ? Grids.GROUNDS[Grids.groundAt(g, i)] : "grass";
                if (Grids.tileAt(g, i) != "floor" || !OPEN.Contains(ground)) continue;
                s.items.Add(new Item { id = $"b{count}", kind = KINDS[count % KINDS.Length], pos = new Point(x, y) });
                count++;
            }
        s.tick = 6 * Sim.Time.TICKS_PER_HOUR; // noon
        return s;
    }

    void NextRun()
    {
        fp?.QueueFree();
        fp = null;
        run++;
        if (run >= runs.Count)
        {
            ItemModels.BenchMax = -1;
            ItemModels.BenchBox = false;
            if (outPath != null) System.IO.File.WriteAllText(outPath, report.ToString());
            GetTree().Quit();
            return;
        }
        var (mode, max) = runs[run];
        ItemModels.BenchMax = mode == "none" ? 0 : max;
        ItemModels.BenchBox = mode == "box";
        fp = new FirstPerson();
        AddChild(fp);
        fp.camera.MakeCurrent();
        frame = 0;
        frameMs.Clear(); renderCpu.Clear(); renderGpu.Clear();
        drawCalls = primitives = 0;
        lastUsec = 0;
    }

    public override void _Process(double delta)
    {
        if (fp == null) return;
        // Looking a little down, as you do walking: the ground and what's on it fill the view.
        double yaw = frame < Warmup ? 0 : Math.Tau * (frame - Warmup) / measureFrames;
        fp.Update(state, 1, yaw, -0.15, delta);
        ulong now = Godot.Time.GetTicksUsec();
        if (frame > Warmup && lastUsec != 0)
        {
            frameMs.Add((now - lastUsec) / 1000.0);
            var vp = GetViewport().GetViewportRid();
            renderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            renderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            drawCalls = Math.Max(drawCalls, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            primitives = Math.Max(primitives, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            drawn = fp.ItemCount;
        }
        lastUsec = now;
        frame++;
        if (frame > Warmup + measureFrames)
        {
            var (mode, max) = runs[run];
            var sorted = frameMs.OrderBy(x => x).ToList();
            string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string line = $"| {mode} | {max} | {drawn} | {F(frameMs.Average())} | {F(sorted[(int)(sorted.Count * 0.95)])} | {F(renderCpu.Average())} | {F(renderGpu.Average())} | {drawCalls} | {primitives} |";
            report.AppendLine(line);
            GD.Print(line);
            NextRun();
        }
    }
}
