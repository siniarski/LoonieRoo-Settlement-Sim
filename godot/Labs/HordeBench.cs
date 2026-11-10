// The horde benchmark (docs/characters.md section 7, the far / crowd tier): thousands of characters
// animated by vertex animation textures (Vat.cs, vat.gdshader) on MultiMeshes, one per outfit variant.
// Four outfits are baked once (walk, idle, run, zombie_crawl); each run spreads N characters over a
// grid 0.9 m apart: most shambling (walk at 55%), some standing, crawling or running, every one at
// its own phase, with its own dirt, blood and tears. Reported: bake time and texture size, then per
// count frame ms (mean, p95), render CPU and GPU ms and draw calls. Vsync off; shadows off unless asked.
//
//   Godot --path godot res://Labs/HordeBench.tscn -- [--counts=5000,10000,20000] [--variants=4]
//         [--shadows] [--frames=180] [--out=horde.md] [--shot=out.png --zoom=12 (a close look, then quit)]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class HordeBench : Node3D
{
    const int Warmup = 40;
    static readonly string[] Clips = { "walk", "idle", "run", "zombie_crawl" };

    readonly List<int> counts = new();
    readonly List<VatBake> bakes = new();
    readonly List<MultiMeshInstance3D> drawn = new();
    readonly StringBuilder report = new();
    readonly List<double> frameMs = new(), renderCpu = new(), renderGpu = new();
    int run = -1, framesLeft, measureFrames = 180, variants = 4;
    long drawCalls;
    ulong lastUsec;
    string? outPath, shotPath;
    double zoom = -1;
    Camera3D cam = null!;

    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        counts.AddRange((args.GetValueOrDefault("counts") ?? "5000,10000,20000").Split(',').Select(int.Parse));
        if (args.TryGetValue("variants", out var v)) variants = int.Parse(v);
        if (args.TryGetValue("frames", out var f)) measureFrames = int.Parse(f);
        if (args.TryGetValue("zoom", out var z)) zoom = double.Parse(z, CultureInfo.InvariantCulture);
        args.TryGetValue("out", out outPath);
        args.TryGetValue("shot", out shotPath);
        bool shadows = args.ContainsKey("shadows");

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        AddChild(new DirectionalLight3D { ShadowEnabled = shadows, RotationDegrees = new Vector3(-55, 30, 0) });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.05f, 0.06f, 0.055f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.5f) } });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.2f), Roughness = 1 } });
        cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Far = 800 };
        AddChild(cam);

        // Bake the outfit variants (zombie-ish: worn everyday clothes).
        var rng = new RandomNumberGenerator { Seed = 77 };
        double bakeMs = 0;
        for (int i = 0; i < variants; i++)
        {
            var o = Outfit.Random(rng);
            var b = Vat.Bake(this, o, Clips);
            bakes.Add(b);
            bakeMs += b.BakeMs;
        }
        var b0 = bakes[0];
        report.AppendLine($"Horde bench (VAT on MultiMesh), {measureFrames} frames per run, shadows {(shadows ? "on" : "off")}, Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine();
        report.AppendLine($"Baked {variants} outfits ({string.Join(", ", bakes.Select(b => $"{b.Vertices} verts / {OutfitMerge.Triangles(b.Mesh)} tris"))}), clips {string.Join(", ", Clips.Select(c => $"{c} {b0.Clips[c].frames}"))} frames at {Vat.Fps} fps: {bakeMs:0} ms in all; textures {bakes.Sum(b => b.Vertices * b.Rows * 16) / 1048576.0:0.0} MB (RGBA float).");
        report.AppendLine();
        report.AppendLine("| count | frame ms | p95 | render cpu | render gpu | draw calls |");
        report.AppendLine("|--:|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        NextRun();
    }

    void NextRun()
    {
        foreach (var d in drawn) d.QueueFree();
        drawn.Clear();
        run++;
        if (run >= counts.Count)
        {
            if (outPath != null) System.IO.File.WriteAllText(outPath, report.ToString());
            GD.Print("horde: done");
            GetTree().Quit();
            return;
        }
        int count = counts[run];
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        const float spacing = 0.9f;
        var mms = bakes.Select(b => new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = b.Mesh,
        }).ToList();
        var per = Enumerable.Range(0, bakes.Count).Select(v => (count - v + bakes.Count - 1) / bakes.Count).ToArray();
        for (int v = 0; v < mms.Count; v++) mms[v].InstanceCount = per[v];
        var next = new int[bakes.Count];
        var h = new RandomNumberGenerator { Seed = 1234 };
        for (int i = 0; i < count; i++)
        {
            int v = i % bakes.Count;
            int k = next[v]++;
            var pos = new Vector3((i % side - side / 2f) * spacing + h.RandfRange(-0.25f, 0.25f), 0, (i / side - side / 2f) * spacing + h.RandfRange(-0.25f, 0.25f));
            var basis = new Basis(Vector3.Up, h.RandfRange(0, Mathf.Tau));
            mms[v].SetInstanceTransform(k, new Transform3D(basis, pos));
            float r = h.Randf();
            var (clip, speed) = r < 0.7f ? ("walk", 0.55f) : r < 0.85f ? ("idle", 1f) : r < 0.95f ? ("zombie_crawl", 1f) : ("run", 1f);
            mms[v].SetInstanceCustomData(k, bakes[v].Custom(clip, h.Randf(), speed * h.RandfRange(0.9f, 1.1f), h.RandiRange(0, 49)));
            mms[v].SetInstanceColor(k, new Color(h.RandfRange(0.3f, 0.9f), h.RandfRange(0, 0.6f), h.Randf() < 0.5f ? h.RandfRange(0, 0.5f) : 0, 0));
        }
        for (int v = 0; v < mms.Count; v++)
        {
            var mi = new MultiMeshInstance3D { Multimesh = mms[v], MaterialOverride = bakes[v].Material };
            AddChild(mi);
            drawn.Add(mi);
        }
        float extent = side * spacing;
        float size = zoom > 0 ? (float)zoom : extent * 1.1f;
        cam.Size = size;
        cam.Position = new Vector3(size, size * 1.1f, size);
        cam.LookAt(Vector3.Zero, Vector3.Up);
        framesLeft = Warmup + measureFrames;
        frameMs.Clear(); renderCpu.Clear(); renderGpu.Clear();
        drawCalls = 0;
        lastUsec = 0;
    }

    public override void _Process(double delta)
    {
        if (run < 0 || run >= counts.Count) return;
        ulong now = Time.GetTicksUsec();
        framesLeft--;
        if (shotPath != null && framesLeft == measureFrames)
        {
            var err = GetViewport().GetTexture().GetImage().SavePng(shotPath);
            GD.Print(err == Error.Ok ? $"shot: {shotPath}" : $"shot failed: {err}");
            GetTree().Quit();
            return;
        }
        if (framesLeft < measureFrames && lastUsec != 0)
        {
            frameMs.Add((now - lastUsec) / 1000.0);
            var vp = GetViewport().GetViewportRid();
            renderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            renderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            drawCalls = Math.Max(drawCalls, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        }
        lastUsec = now;
        if (framesLeft <= 0)
        {
            var sorted = frameMs.OrderBy(x => x).ToList();
            string F(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);
            string line = $"| {counts[run]} | {F(frameMs.Average())} | {F(sorted[(int)(sorted.Count * 0.95)])} | {F(renderCpu.Average())} | {F(renderGpu.Average())} | {drawCalls} |";
            report.AppendLine(line);
            GD.Print(line);
            NextRun();
        }
    }
}
