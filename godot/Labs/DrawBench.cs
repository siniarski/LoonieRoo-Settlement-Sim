// The draw-distance benchmark: a big generated world (seed 42, 2048 tiles = 2 km a side by default; --lazy for
// only the start generated, as a game has it, the far field drawing the rest from the world plan), first
// person in the middle, and what a frame costs as the far field (FarField.cs) draws further out. The sim
// isn't ticked (noon, still). Each run is
//   R          the far field's range in metres (0: off, the near chunks and their fog), the far trees as
//              the game has them (the far LOD to FarField.TREE_TIER, crowns past it)
//   R:lod2     the far LOD all the way out (as before the crowns)
//   R:simple   crowns all the way (the options' Far trees: Simple)
//   R:notrees  the same without the far trees (what the trees cost)
// A run waits for the far field to be built (Pending 0), takes two shots (eye height looking at the town
// centre, and 60 m up), then turns a full circle at eye height. Reported: regions, far triangles and trees,
// the build time, frame ms (mean, p95), render CPU and GPU ms, draw calls, primitives. Vsync off.
//
//   Godot --path godot res://Labs/DrawBench.tscn -- [--size=2048] [--runs=0,250,500,1000,1000:lod2,1000:simple,1000:notrees]
//         [--frames=360] [--out=DIR]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public partial class DrawBench : Node3D
{
    const int Settle = 30, MaxWarmup = 4000;

    readonly List<(float range, string trees)> runs = new();
    readonly StringBuilder report = new();
    readonly List<double> frameMs = new(), renderCpu = new(), renderGpu = new();
    int run = -1, frame, measureFrames = 360, size = 2048, phase;
    long drawCalls, primitives;
    ulong lastUsec;
    string outDir = "";
    SimState state = null!;
    FirstPerson? fp;
    Vector2 town;
    double genMs;
    /// <summary>--lazy: only the start generated, as in a game (the far field draws the rest from the world plan).</summary>
    bool lazy;

    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        foreach (var r in (args.GetValueOrDefault("runs") ?? "0,250,500,1000,1000:lod2,1000:simple,1000:notrees").Split(','))
        {
            var p = r.Split(':');
            runs.Add((float.Parse(p[0], CultureInfo.InvariantCulture), p.Length > 1 ? p[1] : "tiered"));
        }
        if (args.TryGetValue("frames", out var f)) measureFrames = int.Parse(f);
        if (args.TryGetValue("size", out var sz)) size = int.Parse(sz);
        outDir = args.GetValueOrDefault("out") ?? ProjectSettings.GlobalizePath("user://drawbench");
        DirAccess.MakeDirRecursiveAbsolute(outDir);

        ulong t0 = Godot.Time.GetTicksUsec();
        var settings = new Settings { start = "solo", zombies = Difficulty.NORMAL.zombies, weather = Difficulty.NORMAL.weather, progression = Difficulty.NORMAL.progression, winter = Difficulty.NORMAL.winter, dayScale = Difficulty.NORMAL.dayScale, map = Difficulty.NORMAL.map };
        state = Worldgen.createWorld(42, size, settings);
        // The sim generates chunks as survivors come near; the bench wants all of it (as the harness's generateAll).
        lazy = args.ContainsKey("lazy");
        if (!lazy)
            for (int y = 0; y < size; y += Worldgen.CHUNK)
                for (int x = 0; x < size; x += Worldgen.CHUNK) Worldgen.ensureGeneratedAround(state, new Point(x, y));
        genMs = (Godot.Time.GetTicksUsec() - t0) / 1000.0;
        var g = state.grid;
        var you = ControlMod.controlled(state)!;
        // Stand in the open near the middle; look towards the most built-up region (the town).
        int cx = g.width / 2, cy = g.height / 2;
        // Open ground for 4 tiles round (no building, no tree), within 200 tiles east of the middle.
        bool Open(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int i = (y + dy) * g.width + x + dx;
                    if (Grids.tileAt(g, i) != "floor" || Grids.GROUNDS[Grids.groundAt(g, i)] is not ("grass" or "road" or "field" or "dirt")) return false;
                }
            return true;
        }
        var trees = new HashSet<int>(state.furniture.Select(fu => fu.y * g.width + fu.x));
        for (int r = 0; r < 200; r++)
            if (Open(cx + r, cy) && !trees.Contains(cy * g.width + cx + r)) { cx += r; break; }
        you.pos = new Point(cx, cy);
        you.at = Motion.centre(you.pos);
        you.was = Motion.centre(you.pos);
        town = Town(g, cx, cy);
        state.tick = 6 * Sim.Time.TICKS_PER_HOUR; // noon

        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        report.AppendLine($"Draw bench: a {size} world (seed 42, {(lazy ? "only the start generated (--lazy)" : "every chunk generated")} in {genMs / 1000:0.0} s, {state.furniture.Count(fu => fu.kind == "tree"):N0} trees), first person at ({cx}, {cy}), noon; {measureFrames} frames a run (a full turn), 1280x720, Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine();
        report.AppendLine("| range m | far trees | regions | far tris | far trees drawn | build ms | frame ms | p95 | render cpu | render gpu | draw calls | primitives |");
        report.AppendLine("|--:|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        NextRun();
    }

    /// <summary>The middle of the 128-tile square with the most building floor within 900 m (where to look).</summary>
    static Vector2 Town(Grid g, int cx, int cy)
    {
        var best = (n: -1, x: cx, y: cy);
        for (int by = 0; by < g.height; by += 128)
            for (int bx = 0; bx < g.width; bx += 128)
            {
                double d = Math.Sqrt(Math.Pow(bx + 64 - cx, 2) + Math.Pow(by + 64 - cy, 2));
                if (d > 900 || d < 150) continue;
                int n = 0;
                for (int y = by; y < Math.Min(g.height, by + 128); y += 2)
                    for (int x = bx; x < Math.Min(g.width, bx + 128); x += 2)
                        if (Grids.GROUNDS[Grids.groundAt(g, y * g.width + x)] is "boards" or "tiles") n++;
                if (n > best.n) best = (n, bx + 64, by + 64);
            }
        return new Vector2(best.x, best.y);
    }

    void NextRun()
    {
        fp?.QueueFree();
        fp = null;
        run++;
        if (run >= runs.Count)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "bench.md"), report.ToString());
            GD.Print($"draw bench: {outDir}/bench.md");
            GetTree().Quit();
            return;
        }
        var (range, trees) = runs[run];
        FarField.Range = range;
        FarField.DrawTrees = trees != "notrees";
        FarField.TreeTier = trees switch { "simple" => 0, "lod2" => float.PositiveInfinity, _ => FarField.TREE_TIER };
        fp = new FirstPerson();
        AddChild(fp);
        fp.camera.MakeCurrent();
        frame = phase = 0;
        frameMs.Clear(); renderCpu.Clear(); renderGpu.Clear();
        drawCalls = primitives = 0;
        lastUsec = 0;
    }

    public override void _Process(double delta)
    {
        if (fp == null) return;
        var you = ControlMod.controlled(state)!;
        var eye = new Vector2((float)you.at.x / Motion.SUB, (float)you.at.y / Motion.SUB);
        double toTown = Math.Atan2(town.Y - eye.Y, town.X - eye.X);
        frame++;
        switch (phase)
        {
            case 0: // build: look at the town till the far field is done
                fp.EyeAt = null;
                fp.Update(state, 1, toTown, -0.02, delta);
                if (frame % 50 == 0) GD.Print($"  building: frame {frame}, regions {fp.Far.Regions}, pending {fp.Far.Pending}, build {fp.Far.BuildMs:0} ms, road ribbons {fp.Ribbons.BuildMs:0.0} ms ({fp.Ribbons.Drawn:0} m)");
                if ((frame > Settle && fp.Far.Pending == 0) || frame > MaxWarmup) { phase = 1; frame = 0; }
                return;
            case 1: // the eye-height shot
                fp.Update(state, 1, toTown, -0.02, delta);
                if (frame == 8) Shot("eye");
                if (frame == 9) { phase = 2; frame = 0; }
                return;
            case 2: // 60 m up, looking down a little towards the town
                fp.EyeAt = new Vector3(eye.X, 60, eye.Y);
                fp.Update(state, 1, toTown, -0.22, delta);
                if (frame == 30) Shot("high");
                if (frame == 31) { fp.EyeAt = null; phase = 3; frame = 0; }
                return;
        }
        // Measure: a full turn at eye height.
        double yaw = toTown + Math.Tau * frame / measureFrames;
        fp.Update(state, 1, yaw, -0.02, delta);
        ulong now = Godot.Time.GetTicksUsec();
        if (frame > Settle && lastUsec != 0)
        {
            frameMs.Add((now - lastUsec) / 1000.0);
            var vp = GetViewport().GetViewportRid();
            renderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            renderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            drawCalls = Math.Max(drawCalls, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            primitives = Math.Max(primitives, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        }
        lastUsec = now;
        if (frame > Settle + measureFrames)
        {
            var (range, trees) = runs[run];
            var sorted = frameMs.OrderBy(x => x).ToList();
            string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            var far = fp.Far;
            string line = $"| {range} | {trees} | {far.Regions} | {far.Triangles:N0} | {far.TreeCount:N0} | {far.BuildMs:0} | {F(frameMs.Average())} | {F(sorted[(int)(sorted.Count * 0.95)])} | {F(renderCpu.Average())} | {F(renderGpu.Average())} | {drawCalls} | {primitives:N0} |";
            report.AppendLine(line);
            GD.Print(line);
            NextRun();
        }
    }

    void Shot(string what)
    {
        var (range, trees) = runs[run];
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(outDir, $"view_{range}{(trees == "tiered" ? "" : "_" + trees)}_{what}.png"));
    }
}
