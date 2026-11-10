// The tree benchmark (docs/trees.md, "Benchmark"): first person in the middle of a forest that covers the
// whole world, the camera turning a full circle at eye height, and what a frame costs as the trees are drawn
// in different ways. A world is generated (seed 42, 256 tiles), then every open tile of grass, dirt, field
// or forest becomes forest ground with a tree on DENSITY of them (Worldgen's own share), so all the chunks in
// range are forest: the worst case. The sim isn't ticked (noon, still). Each run is
//   mode   lods      the game's: near / mid / far LODs by range (0-24, 24-44, 44+ m), near and mid cast shadows
//          noshadow  the same, the mid LOD casting no shadows
//          near      the near LOD everywhere (what no LOD costs)
//          mid, far  the mid or far LOD everywhere
//   @R     the trees' chunk radius (3 is the game's: 7 x 7 chunks, ~56 m; the world mesh stays at 3)
// Reported: frame ms (mean, p95), render CPU and GPU ms, draw calls, primitives, trees in range. Vsync off.
//
//   Godot --path godot res://Labs/TreeBench.tscn -- [--runs=lods@3,near@3,noshadow@3,lods@5,near@5,lods@7]
//         [--frames=360] [--out=bench.md]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public partial class TreeBench : Node3D
{
    const double DENSITY = 0.3;
    const int Warmup = 150;
    static readonly HashSet<string> OPEN = new() { "grass", "dirt", "field", "forest" };

    readonly List<(string mode, int radius)> runs = new();
    readonly StringBuilder report = new();
    readonly List<double> frameMs = new(), renderCpu = new(), renderGpu = new();
    int run = -1, frame, measureFrames = 360;
    long drawCalls, primitives;
    int trees;
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
        foreach (var r in (args.GetValueOrDefault("runs") ?? "lods@3,near@3,noshadow@3,mid@3,far@3,lods@5,near@5,lods@7").Split(','))
        {
            var p = r.Split('@');
            runs.Add((p[0], p.Length > 1 ? int.Parse(p[1]) : 3));
        }
        if (args.TryGetValue("frames", out var f)) measureFrames = int.Parse(f);
        args.TryGetValue("out", out outPath);

        state = Forest();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        var size = GetViewport().GetVisibleRect().Size;
        report.AppendLine($"Tree bench, {measureFrames} frames a run (a full turn), {size.X}x{size.Y}, Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine();
        report.AppendLine("| mode | radius | trees | frame ms | p95 | render cpu | render gpu | draw calls | primitives |");
        report.AppendLine("|---|--:|--:|--:|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        NextRun();
    }

    /// <summary>A world that's all forest round you, the survivor you control in the middle of it.</summary>
    static SimState Forest()
    {
        var settings = new Settings { start = "solo", zombies = Difficulty.NORMAL.zombies, weather = Difficulty.NORMAL.weather, progression = Difficulty.NORMAL.progression, winter = Difficulty.NORMAL.winter, dayScale = Difficulty.NORMAL.dayScale, map = Difficulty.NORMAL.map };
        var s = Worldgen.createWorld(42, 256, settings);
        var g = s.grid;
        var you = ControlMod.controlled(s)!;
        int cx = g.width / 2, cy = g.height / 2;
        you.pos = new Point(cx, cy);
        you.at = Motion.centre(you.pos);
        you.was = Motion.centre(you.pos);
        var have = new HashSet<int>();
        foreach (var fu in s.furniture) have.Add(fu.y * g.width + fu.x);
        int forest = Array.IndexOf(Grids.GROUNDS, "forest");
        for (int y = 0; y < g.height; y++)
            for (int x = 0; x < g.width; x++)
            {
                int i = y * g.width + x;
                string ground = Grids.groundAt(g, i) >= 0 && Grids.groundAt(g, i) < Grids.GROUNDS.Length ? Grids.GROUNDS[Grids.groundAt(g, i)] : "grass";
                if (Grids.tileAt(g, i) != "floor" || !OPEN.Contains(ground) || have.Contains(i)) continue;
                Grids.setGround(g, i, forest);
                if (Math.Abs(x - cx) <= 1 && Math.Abs(y - cy) <= 1) continue;
                uint h = unchecked((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u);
                h ^= h >> 15; h = unchecked(h * 0x2C1B3C6Du); h ^= h >> 12;
                if ((h & 0xFFFF) / 65536.0 < DENSITY) FurnitureMod.addFurniture(s, "tree", x, y);
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
            if (outPath != null) System.IO.File.WriteAllText(outPath, report.ToString());
            GetTree().Quit();
            return;
        }
        var (mode, radius) = runs[run];
        Trees.BenchRadius = radius;
        Trees.BenchLod = mode switch { "near" => 0, "mid" => 1, "far" => 2, _ => -1 };
        Trees.BenchMidShadows = mode != "noshadow";
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
        double yaw = frame < Warmup ? 0 : Math.Tau * (frame - Warmup) / measureFrames;
        fp.Update(state, 1, yaw, 0, delta);
        ulong now = Godot.Time.GetTicksUsec();
        if (frame > Warmup && lastUsec != 0)
        {
            frameMs.Add((now - lastUsec) / 1000.0);
            var vp = GetViewport().GetViewportRid();
            renderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            renderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            drawCalls = Math.Max(drawCalls, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            primitives = Math.Max(primitives, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            trees = fp.TreeCount;
        }
        lastUsec = now;
        frame++;
        if (frame > Warmup + measureFrames)
        {
            var (mode, radius) = runs[run];
            var sorted = frameMs.OrderBy(x => x).ToList();
            string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string line = $"| {mode} | {radius} | {trees} | {F(frameMs.Average())} | {F(sorted[(int)(sorted.Count * 0.95)])} | {F(renderCpu.Average())} | {F(renderGpu.Average())} | {drawCalls} | {primitives} |";
            report.AppendLine(line);
            GD.Print(line);
            NextRun();
        }
    }
}
