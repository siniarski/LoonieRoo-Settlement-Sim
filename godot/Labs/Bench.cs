// The animation benchmark (docs/characters.md, "LOD"): how many animated characters a frame can
// hold, by how they're animated and how they're built. Each run spawns N mannequins in a grid,
// warms up, then measures frames:
//   anim  tree       AnimationTree per character (a blend space of idle/walk/jog/run, sync, time scale)
//         player     AnimationPlayer playing walk
//         throttled  AnimationPlayer advanced by hand every 4th frame (a quarter of them each frame)
//         everyN     the same every Nth frame (every2 = a 30 fps update at 60 fps, every3 = 20 fps; throttled = every4)
//         none       skinned meshes in their rest pose: the cost of drawing alone
//   mesh   regions    the 26-mesh body (regions and stump caps; 14 drawn): the near-LOD build
//          merged     one mesh: the mid/far-LOD build (the outfit merged)
// Reported: frame ms (mean, p95), render CPU and GPU ms (the viewport's measured times), draw calls. Vsync off, no shadows.
// Headless (--headless) measures the CPU side only: animation without drawing.
//
//   Godot --path godot res://Labs/Bench.tscn -- [--counts=100,500,1000,2000]
//         [--anims=tree,player,throttled,none] [--meshes=regions,merged] [--frames=120] [--out=bench.md]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Bench : Node3D
{
    static readonly Dictionary<string, string> Meshes = new()
    {
        ["regions"] = "res://assets/characters/test_mannequin.glb",
        ["merged"] = "res://assets/characters/test_mannequin_merged.glb",
    };
    const int Warmup = 30;
    int throttle = 4;

    readonly List<(string mesh, string anim, int count)> runs = new();
    readonly StringBuilder report = new();
    int run = -1, framesLeft, measureFrames = 120;
    readonly List<double> frameMs = new(), renderCpu = new(), renderGpu = new();
    long drawCalls;
    ulong lastUsec;
    string? outPath;
    bool headless;

    readonly List<Node3D> actors = new();
    readonly List<AnimationPlayer> throttled = new();
    int throttlePhase;
    Camera3D cam = null!;
    AnimationNodeBlendTree? treeRoot;

    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        var counts = (args.GetValueOrDefault("counts") ?? "100,500,1000,2000").Split(',').Select(int.Parse).ToArray();
        var anims = (args.GetValueOrDefault("anims") ?? "tree,player,throttled,none").Split(',');
        var meshes = (args.GetValueOrDefault("meshes") ?? "regions,merged").Split(',');
        if (args.TryGetValue("frames", out var f)) measureFrames = int.Parse(f);
        args.TryGetValue("out", out outPath);
        headless = DisplayServer.GetName() == "headless";
        foreach (var m in meshes) foreach (var a in anims) foreach (var c in counts) runs.Add((m, a, c));

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        AddChild(new DirectionalLight3D { ShadowEnabled = false, RotationDegrees = new Vector3(-55, 30, 0) });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.5f) } });
        cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Far = 500 };
        AddChild(cam);

        report.AppendLine($"Animation bench ({(headless ? "headless: CPU only" : "windowed")}), {measureFrames} frames per run, Godot {Engine.GetVersionInfo()["string"]}, {OS.GetProcessorName()}, {RenderingServer.GetVideoAdapterName()}");
        report.AppendLine();
        report.AppendLine("| mesh | anim | count | frame ms | p95 | render cpu | render gpu | draw calls |");
        report.AppendLine("|---|---|--:|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        NextRun();
    }

    void NextRun()
    {
        foreach (var a in actors) a.QueueFree();
        actors.Clear();
        throttled.Clear();
        run++;
        if (run >= runs.Count)
        {
            if (outPath != null) System.IO.File.WriteAllText(outPath, report.ToString());
            GD.Print("bench: done");
            GetTree().Quit();
            return;
        }
        var (mesh, anim, count) = runs[run];
        throttle = anim.StartsWith("every") ? int.Parse(anim[5..]) : 4;
        throttlePhase = 0;
        var scene = GD.Load<PackedScene>(Meshes[mesh]);
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        const float spacing = 1.2f;
        for (int i = 0; i < count; i++)
        {
            var a = scene.Instantiate<Node3D>();
            AddChild(a);
            a.Position = new Vector3((i % side - side / 2f) * spacing, 0, (i / side - side / 2f) * spacing);
            foreach (var m in FindAll<MeshInstance3D>(a))
                if (m.Name.ToString().StartsWith("stump_")) m.Visible = false;
            Animate(a, anim, i);
            actors.Add(a);
        }
        float extent = side * spacing;
        cam.Size = extent * 1.1f;
        cam.Position = new Vector3(extent, extent * 1.1f, extent);
        cam.LookAt(Vector3.Zero, Vector3.Up);
        framesLeft = Warmup + measureFrames;
        frameMs.Clear(); renderCpu.Clear(); renderGpu.Clear();
        drawCalls = 0;
        lastUsec = 0;
    }

    void Animate(Node3D actor, string anim, int i)
    {
        var player = FindAll<AnimationPlayer>(actor).First();
        var lib = player.GetAnimationLibrary("");
        foreach (var n in new[] { "idle", "walk", "jog", "run" }) lib.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        double phase = (i * 0.37) % 1.0;  // so they aren't in step
        switch (anim)
        {
            case "player":
                player.Play("walk");
                player.Seek(phase, true);
                break;
            case "throttled":
            case var e when e.StartsWith("every"):
                player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
                player.Play("walk");
                player.Seek(phase, true);
                throttled.Add(player);
                break;
            case "tree":
                treeRoot ??= MakeTreeRoot();
                var tree = new AnimationTree { TreeRoot = treeRoot };
                tree.AddAnimationLibrary("", lib);
                actor.AddChild(tree);
                tree.Active = true;
                tree.Set("parameters/space/blend_position", (float)(0.5 + 2.5 * ((i * 0.61) % 1.0)));
                tree.Set("parameters/rate/scale", 1.0);
                break;
        }
    }

    static AnimationNodeBlendTree MakeTreeRoot()
    {
        var space = new AnimationNodeBlendSpace1D { MinSpace = 0, MaxSpace = 3.5f, Sync = true };
        foreach (var (clip, x) in new[] { ("idle", 0f), ("walk", 1.37f), ("jog", 2.4f), ("run", 3.5f) })
            space.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, x);
        var root = new AnimationNodeBlendTree();
        root.AddNode("space", space);
        root.AddNode("rate", new AnimationNodeTimeScale());
        root.ConnectNode("rate", 0, "space");
        root.ConnectNode("output", 0, "rate");
        return root;
    }

    public override void _Process(double delta)
    {
        if (run >= runs.Count || run < 0) return;
        // Throttled players: a quarter each frame, each advanced by four frames' worth.
        for (int i = throttlePhase; i < throttled.Count; i += throttle) throttled[i].Advance(delta * throttle);
        throttlePhase = (throttlePhase + 1) % throttle;

        ulong now = Time.GetTicksUsec();
        framesLeft--;
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
            var (mesh, anim, count) = runs[run];
            var sorted = frameMs.OrderBy(x => x).ToList();
            string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string line = $"| {mesh} | {anim} | {count} | {F(frameMs.Average())} | {F(sorted[(int)(sorted.Count * 0.95)])} | " +
                          (headless ? "- | - | - |" : $"{F(renderCpu.Average())} | {F(renderGpu.Average())} | {drawCalls} |");
            report.AppendLine(line);
            GD.Print(line);
            NextRun();
        }
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var x in FindAll<T>(c)) yield return x;
    }
}
