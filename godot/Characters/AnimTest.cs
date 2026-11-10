// The world's animation test (2026-11-10, the animation pass): a pz character model played the way People.cs plays
// it in first person (an AnimationPlayer per model, the clip and rate picked from the speed), moved along the
// ground at a speed, its drawn feet measured. Headless works (no picture needed):
//
//   Godot --headless --path godot res://Characters/AnimTest.tscn -- --out=DIR [--logic=before|after] [--cases=...]
//
// For each case, every step: how far the planted foot slides along the ground while it's down (cm; the contact
// point is the lower of the heel and the ball of the foot, down within 2.5 cm of the ground), and on slopes how far
// the planted sole is off the ground (+ floating, - sunk; with the foot IK the game gives the near ones, and
// without). `before` is People.cs's logic before this pass (the library's walk, jog and sprint at rates
// against assumed speeds, clamped); `after` is Gaits.cs. Results to DIR/animtest-<logic>.md.
// --calibrate prints each derived locomotion clip's ground speed as drawn here (the planted foot's travel at
// rate 1, in place), to check clips_more.json against.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using LoonieRoo.Game.World3D;

namespace LoonieRoo.Game.Characters;

public partial class AnimTest : Node3D
{
    /// <summary>One run: a name, the speed over time (m/s), the ground's slope (degrees, uphill along the walk),
    /// how long, which part is measured (from, to: seconds), and whether the near tier's foot IK is on.</summary>
    sealed record Case(string name, Func<double, double> speed, double slope, double length, double from, double to, bool ik, bool dead = false, string stance = "stand", bool running = false, double turn = 0, string variant = "walker", double h = 0, string? clip = null, char oneLeg = ' ', bool armLost = false, bool look = false);

    const double DT = 1.0 / 60;
    const float HEEL_UP = 0.09f, BALL_UP = 0.02f, DOWN = 0.025f, WRIST_UP = 0.04f;

    Node3D model = null!;
    Skeleton3D skel = null!;
    AnimationPlayer player = null!;
    FootIK? footIk;
    Probe probe = null!;
    string logic = "after", outDir = "";
    readonly List<Case> cases = new();
    int caseIndex = -1;
    double t, travelled, eased, yaw, since;
    string clip = "";
    // Per foot: down last frame, its contact point (0 heel, 1 ball), where, the current step's slide.
    readonly bool[] wasDown = new bool[2];
    readonly int[] wasPoint = new int[2];
    readonly Vector3[] last = new Vector3[2];
    readonly double[] stepSlide = new double[2];
    readonly List<double> slides = new();
    double stumpWorst, faceSum; int faceN;
    Vector3 lastHeel, lastBall;
    double soleSum, soleWorstSink; int soleN;
    readonly StringBuilder report = new();
    bool calibrate;

    /// <summary>Records the drawn feet after every modifier (the last in the stack): Skeleton3D puts its poses back after them.</summary>
    public partial class Probe : SkeletonModifier3D
    {
        public readonly Vector3[] Ankle = new Vector3[2], Ball = new Vector3[2], Wrist = new Vector3[2];
        /// <summary>The left upper arm's line in the chest's frame (the chest's turn from its rest undone).</summary>
        public Vector3 ArmInChest;
        /// <summary>The angle (deg) between the face and LookAt, if set.</summary>
        public Vector3? LookAt;
        public float FaceOff;
        int head = -1;
        int chest = -1, arm = -1;
        int[] feet = { -1, -1 }, toes = { -1, -1 }, hands = { -1, -1 };
        public bool Fresh;
        public override void _ProcessModificationWithDelta(double delta)
        {
            var sk = GetSkeleton();
            if (sk == null) return;
            if (feet[0] < 0) { feet = new[] { sk.FindBone("LeftFoot"), sk.FindBone("RightFoot") }; toes = new[] { sk.FindBone("LeftToes"), sk.FindBone("RightToes") }; hands = new[] { sk.FindBone("LeftHand"), sk.FindBone("RightHand") }; }
            var xf = sk.GlobalTransform;
            for (int k = 0; k < 2; k++) { Ankle[k] = xf * sk.GetBoneGlobalPose(feet[k]).Origin; Ball[k] = xf * sk.GetBoneGlobalPose(toes[k]).Origin; Wrist[k] = xf * sk.GetBoneGlobalPose(hands[k]).Origin; }
            if (chest < 0) { chest = sk.FindBone("UpperChest"); arm = sk.FindBone("LeftUpperArm"); }
            var turn = sk.GetBoneGlobalPose(chest).Basis.Orthonormalized() * sk.GetBoneGlobalRest(chest).Basis.Orthonormalized().Inverse();
            ArmInChest = (turn.Inverse() * sk.GetBoneGlobalPose(arm).Basis.Y).Normalized();
            if (LookAt is Vector3 la)
            {
                if (head < 0) head = sk.FindBone("Head");
                var hp = sk.GetBoneGlobalPose(head);
                var face = xf.Basis * (hp.Basis.Orthonormalized() * sk.GetBoneGlobalRest(head).Basis.Orthonormalized().Inverse() * Vector3.Back);
                var to = la - xf * hp.Origin;
                FaceOff = Mathf.RadToDeg(new Vector3(face.X, 0, face.Z).AngleTo(new Vector3(to.X, 0, to.Z)));
            }
            Fresh = true;
        }
    }

    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        logic = args.GetValueOrDefault("logic") ?? "after";
        outDir = args.GetValueOrDefault("out") ?? "";
        calibrate = args.ContainsKey("calibrate");
        Build(args.GetValueOrDefault("model") ?? "res://assets/characters/look/look_pz_survivor.glb");
        var sx = skel.GlobalTransform;
        GD.Print($"animtest: rest ankle {(sx * skel.GetBoneGlobalRest(skel.FindBone("LeftFoot"))).Origin} ball {(sx * skel.GetBoneGlobalRest(skel.FindBone("LeftToes"))).Origin} hips {(sx * skel.GetBoneGlobalRest(skel.FindBone("Hips"))).Origin}");
        if (calibrate) { Calibrate(); GetTree().Quit(); return; }
        if (args.ContainsKey("noik")) ikNear = 0;
        if (args.TryGetValue("bench", out var bn)) { StartBench(int.Parse(bn), int.Parse(args.GetValueOrDefault("frames") ?? "600"), args.GetValueOrDefault("model")); return; }
        if (args.TryGetValue("strip", out var strip))
        {
            var sp = strip.Split(',');
            stripN = int.Parse(sp[0]);
            stripEvery = sp.Length > 1 ? int.Parse(sp[1]) : 5;
            Scenery();
        }
        bool ik = logic == "after";
        double Const(double v, double _) => v;
        foreach (double v in new[] { 0.8, 1.4, 2.2, 2.5, 3.5, 4.4, 5.0 })
            cases.Add(new Case($"flat {v.ToString("0.0", CultureInfo.InvariantCulture)} m/s", (x) => v, 0, 5, 1.5, 5, false));
        foreach (bool on in ik ? new[] { false, true } : new[] { false })
        {
            string tag = on ? ", foot IK" : "";
            cases.Add(new Case("stopping from 2.5" + tag, (x) => x < 2 ? 2.5 : 0, 0, 3.5, 2, 3.5, on));
            cases.Add(new Case("starting to 2.5" + tag, (x) => x < 1 ? 0 : 2.5, 0, 2.5, 1, 2.0, on));
            cases.Add(new Case("turning 90 deg on the spot" + tag, (x) => 0, 0, 2.5, 1, 2.5, on, turn: Math.PI / 2));
        }
        cases.Add(new Case("sneaking 1.25", (x) => 1.25, 0, 5, 1.5, 5, false, stance: "sneak"));
        // The dead at the sim's speeds (ZombieKinds: a walker 1.68 m/s, a brute 1.25, a runner 2.5, a crawler 1.45);
        // h picks the walker's gait as a hash of its id would.
        foreach (var (gait, h) in new[] { ("shamble", 0.1), ("lurch", 0.5), ("dragging left", 0.7), ("dragging right", 0.9) })
            cases.Add(new Case($"dead walker 1.68 ({gait})", (x) => 1.68, 0, 5, 1.5, 5, false, dead: true, h: h));
        cases.Add(new Case("dead brute 1.25", (x) => 1.25, 0, 5, 1.5, 5, false, dead: true, variant: "brute"));
        cases.Add(new Case("dead runner 2.5", (x) => 2.5, 0, 5, 1.5, 5, false, dead: true, variant: "runner"));
        // Missing limbs: a leg lost at the knee (the sim moves them at 2 m/s: 5 ticks a tile), only the good foot
        // measured; an arm lost at the elbow, walking: how far its stump strays from held (the stump column).
        cases.Add(new Case("left leg lost, 2.0", (x) => 2.0, 0, 5, 1.5, 5, false, oneLeg: 'l'));
        cases.Add(new Case("left leg lost, standing", (x) => 0, 0, 4, 1.5, 4, false, oneLeg: 'l'));
        cases.Add(new Case("left arm lost, walking 1.4", (x) => 1.4, 0, 5, 1.5, 5, false, armLost: true));
        // One of the dead standing, what it's after 2 m to its left: how far its face is turned from it (HeadLook.cs).
        cases.Add(new Case("dead idle, its target to its left", (x) => 0, 0, 3, 1.5, 3, false, dead: true, look: true));
        cases.Add(new Case("dead crawler 1.45 (hands)", (x) => 1.45, 0, 5, 1.5, 5, false, dead: true, stance: "crawl", variant: "crawler"));
        foreach (double slope in new[] { 8.0, -8.0 })
        {
            cases.Add(new Case($"{(slope > 0 ? "up" : "down")} {Math.Abs(slope)} deg at 2.5", (x) => 2.5, slope, 5, 1.5, 5, false));
            if (ik) cases.Add(new Case($"{(slope > 0 ? "up" : "down")} {Math.Abs(slope)} deg at 2.5, foot IK", (x) => 2.5, slope, 5, 1.5, 5, true));
        }
        if (args.TryGetValue("clips", out var named))
        {
            // Each clip once, in place: a strip across its length (--strip=N: EVERY is worked out from it).
            cases.Clear();
            foreach (var cl in named.Split(','))
            {
                var a = player.GetAnimation(cl);
                double len = a?.Length ?? 1;
                cases.Add(new Case(cl.Replace("/", "-"), (x) => 0, 0, len + 0.5, 0.25, len + 0.25, false, clip: cl));
            }
            if (stripN > 0) stripEvery = -1;
        }
        if (args.TryGetValue("cases", out var only)) cases.RemoveAll((c) => !only.Split(',').Any((o) => c.name.Contains(o)));
        report.AppendLine($"Animation test, logic `{logic}`: a pz survivor model as People.cs plays it, moved along the ground. Slide: how far the planted foot travels along the ground while down, per step (cm). Sole: the planted sole's height over the ground (cm, mean; worst sink).");
        report.AppendLine();
        report.AppendLine("| case | clips | steps | slide mean | slide worst | sole mean | worst sink |");
        report.AppendLine("|---|---|--:|--:|--:|--:|--:|");
        NextCase();
    }

    void Build(string path)
    {
        model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        AddChild(model);
        skel = Find<Skeleton3D>(model)!;
        player = new AnimationPlayer { CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
        model.AddChild(player);
        foreach (var (lib, file) in new[] { ("", "test_mannequin"), ("ual", "clips_ual"), ("more", "clips_more") })
        {
            if (!ResourceLoader.Exists($"res://assets/characters/{file}.glb")) continue;
            var n = GD.Load<PackedScene>($"res://assets/characters/{file}.glb").Instantiate<Node3D>();
            var l = Find<AnimationPlayer>(n)!.GetAnimationLibrary("");
            foreach (var name in l.GetAnimationList()) if (lib != "more" || Gaits.Looping(name)) l.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            player.AddAnimationLibrary(lib, l);
            if (lib == "more") Gaits.Measure(l, "more/", Find<Skeleton3D>(n)!, Gaits.ToScene(n, Find<Skeleton3D>(n)!));
            n.Free();
        }
        footIk = new FootIK(model, skel, this) { GroundAt = (p) => (Ground(p.X, p.Z), GroundNormal()) };
        footIk.Enabled = false;
        skel.AddChild(probe = new Probe { Name = "Probe" });
    }

    // --strip=N,EVERY (with a window): N pictures of each case, EVERY frames apart (60 a second), from the side,
    // the camera following, a 25 cm checker on the ground (to see a foot slide), into DIR/strip-<case>.png.
    int stripN, stripEvery = 5, captured, frame;
    Image? stripImage;
    Camera3D? cam;
    MeshInstance3D? ground;
    const int STRIP_W = 300, STRIP_H = 360;

    void Scenery()
    {
        AddChild(cam = new Camera3D { Fov = 40 });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), ShadowEnabled = true });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.55f, 0.6f, 0.65f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.6f) } });
        var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                img.SetPixel(x, y, ((x / 32) + (y / 32)) % 2 == 0 ? new Color(0.42f, 0.40f, 0.36f) : new Color(0.30f, 0.29f, 0.27f));
        var mat = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), Uv1Scale = new Vector3(400, 400, 1), TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 1 };
        AddChild(ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(100, 100) }, MaterialOverride = mat });
    }

    void Picture(Case c)
    {
        if (cam == null) return;
        var at = model.Position;
        cam.Position = at + new Vector3(3.2f, 1.0f, 0);
        cam.LookAt(at + new Vector3(0, 0.85f, 0), Vector3.Up);
        if (ground != null) { ground.Rotation = new Vector3(-(float)(slopeDeg * Math.PI / 180), 0, 0); }
        frame++;
        int every = stripEvery > 0 ? stripEvery : Math.Max(1, (int)((c.to - c.from) / DT / stripN));
        if (t < c.from || captured >= stripN || frame % every != 0) return;
        var shot = GetViewport().GetTexture().GetImage();
        int w = shot.GetWidth(), h = shot.GetHeight();
        // The middle of the view, a column round the body.
        int cw = Math.Min(w, h * STRIP_W / STRIP_H);
        var part = shot.GetRegion(new Rect2I((w - cw) / 2, 0, cw, h));
        part.Resize(STRIP_W, STRIP_H);
        stripImage ??= Image.CreateEmpty(STRIP_W * stripN, STRIP_H, false, part.GetFormat());
        stripImage.BlitRect(part, new Rect2I(0, 0, STRIP_W, STRIP_H), new Vector2I(captured * STRIP_W, 0));
        captured++;
    }

    void SaveStrip(Case c)
    {
        if (stripImage == null || outDir == "") return;
        System.IO.Directory.CreateDirectory(outDir);
        string file = System.IO.Path.Combine(outDir, "strip-" + string.Concat(c.name.Select((ch) => char.IsLetterOrDigit(ch) ? ch : '-')).Replace("--", "-") + ".png");
        stripImage.SavePng(file);
        GD.Print($"animtest: strip {file}");
        stripImage = null;
    }

    // --bench=N [--frames=F]: N models as People.cs draws them (one AnimationPlayer each, on the game's process
    // callback), walking on the spot at speeds from 0.8 to 4.4 m/s, clips picked every frame by the logic; with
    // `after`, the nearest IK_NEAR have the foot IK on (People.cs's near tier). The frame's mean and p95 (ms), and the
    // process time (the pick, the players, the skeletons), from the window (vsync off: --novsync is Godot's own flag).
    sealed class Bot { public Node3D root = null!; public AnimationPlayer player = null!; public string clip = ""; public double eased, since, v; public FootIK? ik; }
    readonly List<Bot> bots = new();
    int benchFrames, benchLeft, benchWarm = 60;
    readonly List<double> benchMs = new();
    ulong lastUs;
    bool benching;
    const int IK_NEAR = 6;
    int ikNear = IK_NEAR;

    void StartBench(int n, int frames, string? modelPath)
    {
        model.Visible = false;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        AddChild(cam = new Camera3D { Fov = 60, Position = new Vector3(6, 14, -6) });
        cam.LookAt(new Vector3(6, 0, 6), Vector3.Up);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0) });
        for (int i = 0; i < n; i++)
        {
            var root = GD.Load<PackedScene>(modelPath ?? "res://assets/characters/look/look_pz_survivor.glb").Instantiate<Node3D>();
            AddChild(root);
            root.Position = new Vector3((i % 8) * 1.6f, 0, (i / 8) * 1.6f);
            var pl = new AnimationPlayer();
            root.AddChild(pl);
            foreach (var lib in player.GetAnimationLibraryList()) pl.AddAnimationLibrary(lib, player.GetAnimationLibrary(lib));
            var b = new Bot { root = root, player = pl, v = 0.8 + 3.6 * (i % 9) / 8.0 };
            if (logic == "after" && i < ikNear)
            {
                b.ik = new FootIK(root, Find<Skeleton3D>(root)!, this) { GroundAt = (q) => (0f, Vector3.Up) };
                b.ik.Enabled = true;
            }
            bots.Add(b);
        }
        benchFrames = benchLeft = frames;
        benching = true;
    }

    void BenchFrame(double delta)
    {
        foreach (var b in bots)
        {
            string want; float rate; double blend = 0.25;
            if (logic == "before")
            {
                b.eased += (b.v - b.eased) * Math.Min(1, delta * 8);
                static float Rate(double s, double cs) => (float)Math.Clamp(s / cs, 0.5, 1.6);
                (want, rate) = b.eased < 0.2 ? ("ual/ual_idle", 1f) : b.eased < 2.2 ? ("ual/ual_walk", Rate(b.eased, 1.4)) : b.eased < 3.8 ? ("ual/ual_jog_fwd", Rate(b.eased, 3.0)) : ("ual/ual_sprint", Rate(b.eased, 5.0));
            }
            else
            {
                var p = People.LivingClip(ref b.eased, ref b.since, b.v, delta, "stand", false, b.clip, 0);
                (want, rate, blend) = (p.clip, p.rate, p.blend);
            }
            if (want != b.clip) { b.player.Play(want, b.clip == "" ? 0 : blend); b.clip = want; }
            b.player.SpeedScale = rate;
        }
        ulong now = Godot.Time.GetTicksUsec();
        if (lastUs != 0 && benchWarm-- <= 0) { benchMs.Add((now - lastUs) / 1000.0); benchLeft--; }
        lastUs = now;
        if (benchLeft > 0) return;
        benchMs.Sort();
        string line = $"bench: logic {logic}, {bots.Count} models ({(logic == "after" ? Math.Min(ikNear, bots.Count) : 0)} with foot IK), {benchFrames} frames: mean {benchMs.Average():0.00} ms, p95 {benchMs[(int)(benchMs.Count * 0.95)]:0.00} ms";
        GD.Print(line);
        if (outDir != "") System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "bench-world.md"), line + System.Environment.NewLine);
        GetTree().Quit();
    }

    double slopeDeg;
    float Ground(float x, float z) => (float)(Math.Tan(slopeDeg * Math.PI / 180) * z);
    Vector3 GroundNormal() => new Vector3(0, 1, -(float)Math.Tan(slopeDeg * Math.PI / 180)).Normalized();

    void NextCase()
    {
        if (caseIndex >= 0) { Finish(cases[caseIndex]); SaveStrip(cases[caseIndex]); }
        captured = frame = 0;
        caseIndex++;
        if (caseIndex >= cases.Count)
        {
            GD.Print("animtest: done");
            if (outDir != "")
            {
                System.IO.Directory.CreateDirectory(outDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"animtest-{logic}.md"), report.ToString());
            }
            GetTree().Quit();
            return;
        }
        var c = cases[caseIndex];
        slopeDeg = c.slope;
        t = travelled = eased = yaw = 0;
        clip = "";
        clipsUsed.Clear();
        slides.Clear();
        soleSum = soleWorstSink = 0; soleN = 0;
        stumpWorst = 0;
        faceSum = 0; faceN = 0;
        probe.LookAt = c.look ? new Vector3(2, 1.55f, 0) : null;
        var hl = skel.GetNodeOrNull<HeadLook>("HeadLook");
        if (c.look && logic == "after" && hl == null)
        {
            skel.AddChild(hl = new HeadLook { Name = "HeadLook", Target = new Vector3(2, 1.55f, 0), Weight = 1 });
            skel.MoveChild(hl, probe.GetIndex());
        }
        if (!c.look && hl != null) { skel.RemoveChild(hl); hl.QueueFree(); }
        // The stump hold for a lost arm (after: StumpHold.cs, as People.cs puts it on such a model; before: none).
        var hold = skel.GetNodeOrNull<StumpHold>("StumpHold");
        if (c.armLost && logic == "after" && hold == null)
        {
            skel.AddChild(hold = new StumpHold { Name = "StumpHold", Left = true });
            skel.MoveChild(hold, probe.GetIndex());  // before the probe, which reads the pose as drawn
        }
        if (!c.armLost && hold != null) { skel.RemoveChild(hold); hold.QueueFree(); }
        for (int k = 0; k < 2; k++) { wasDown[k] = false; stepSlide[k] = 0; }
        footIk!.Enabled = false;
        footIk.Enabled = c.ik;
        model.Position = new Vector3(0, Ground(0, 0), 0);
        model.Rotation = Vector3.Zero;
        player.Stop();
        probe.Fresh = false;
    }

    readonly SortedSet<string> clipsUsed = new();

    public override void _Process(double delta)
    {
        if (benching) { BenchFrame(delta); return; }
        if (caseIndex < 0 || caseIndex >= cases.Count || calibrate) return;
        var c = cases[caseIndex];
        if (probe.Fresh && t > c.from && t <= c.to) Measure();
        Picture(c);
        probe.Fresh = false;
        if (t >= c.length) { NextCase(); return; }
        // The world's tick: the root moves at the speed (along +Z, the model's front), the clip follows.
        double v = c.speed(t);
        travelled += v * DT;
        if (c.turn != 0) yaw = c.turn * Math.Clamp((t - c.from) / 0.5, 0, 1);
        model.Position = new Vector3(0, Ground(0, (float)travelled), (float)travelled);
        model.Rotation = new Vector3(0, (float)yaw, 0);
        double turning = c.turn != 0 && t > c.from && t < c.from + 0.5 ? c.turn / 0.5 : 0;
        Step(c, v, turning);
        player.Advance(DT);
        t += DT;
    }

    /// <summary>Picks and plays the clip for this frame, as People.cs does with the chosen logic.</summary>
    void Step(Case c, double v, double turning)
    {
        string want; float rate; double blend;
        if (c.clip != null)
        {
            (want, rate, blend) = (c.clip, t < c.from ? 0f : 1f, 0);
            if (t < c.from) player.Play(c.clip);
        }
        else if (c.dead)
        {
            eased += (v - eased) * Math.Min(1, DT * 8);
            if (logic == "before")
                (want, rate) = c.stance == "crawl" ? ("zombie_crawl", 1f) : eased < 0.2 ? ("ual/ual_idle", 0.6f) : ("ual/ual_walk", (float)Math.Clamp(eased / 1.0, 0.5, 1.6));
            else
            {
                var g = c.stance == "crawl" ? Gaits.Crawl(eased) : Gaits.Dead(c.variant, c.h, eased);
                (want, rate) = (g.clip, g.rate);
            }
            blend = 0.25;
        }
        else if (logic == "before")
        {
            // People.cs before this pass: the speed eased (8 a second), the library clips by fixed limits, rates
            // against assumed clip speeds (walk 1.4, jog 3.0, sprint 5.0, crouch 1.0), clamped to 0.5..1.6.
            eased += (v - eased) * Math.Min(1, DT * 8);
            static float Rate(double s, double cs) => (float)Math.Clamp(s / cs, 0.5, 1.6);
            (want, rate) = eased < 0.2 ? ("ual/ual_idle", 1f)
                : c.stance == "sneak" ? ("ual/ual_crouch_fwd", Rate(eased, 1.0))
                : eased < 2.2 ? ("ual/ual_walk", Rate(eased, 1.4))
                : eased < 3.8 ? ("ual/ual_jog_fwd", Rate(eased, 3.0))
                : ("ual/ual_sprint", Rate(eased, 5.0));
            blend = 0.25;
        }
        else
        {
            var p = People.LivingClip(ref eased, ref since, v, DT, c.stance, c.running, clip, turning, c.oneLeg);
            (want, rate, blend) = (p.clip, p.rate, p.blend);
        }
        if (!player.HasAnimation(want)) want = "ual/ual_idle";
        if (want != clip)
        {
            player.Play(want, clip == "" ? 0 : blend);
            clip = want;
        }
        if (t > c.from) clipsUsed.Add(want.Split('/')[^1]);
        player.SpeedScale = rate;
        if (footIk != null) footIk.Steps = want is "more/idle" or "more/crouch_idle" or "more/shuffle";

    }

    void Measure()
    {
        bool hands = cases[caseIndex].stance == "crawl";
        if (cases[caseIndex].look) { faceSum += probe.FaceOff; faceN++; }
        if (cases[caseIndex].armLost)
        {
            float a = Mathf.RadToDeg(probe.ArmInChest.AngleTo(new Vector3(0.30f, -0.93f, 0.20f).Normalized()));
            stumpWorst = Math.Max(stumpWorst, a);
        }
        for (int k = 0; k < 2; k++)
        {
            if (cases[caseIndex].oneLeg == 'l' && k == 0 || cases[caseIndex].oneLeg == 'r' && k == 1) continue;  // the lost one
            // A crawl's planted limbs are its hands: the wrist WRIST_UP over the ground when the palm is on it.
            var heel = hands ? probe.Wrist[k] - new Vector3(0, WRIST_UP, 0) : probe.Ankle[k] - new Vector3(0, HEEL_UP, 0);
            var ball = hands ? heel : probe.Ball[k] - new Vector3(0, BALL_UP, 0);
            float hh = heel.Y - Ground(heel.X, heel.Z), hb = ball.Y - Ground(ball.X, ball.Z);
            int point = hh <= hb ? 0 : 1;
            var at = point == 0 ? heel : ball;
            float h = Math.Min(hh, hb);
            bool down = h < DOWN;
            if (k == 0 && OS.GetEnvironment("ANIMTRACE") != "")
                GD.Print($"trace t {t:0.000} heel h {hh * 100:0.0} vz {(heel.Z - lastHeel.Z) / DT:0.00} ball h {hb * 100:0.0} vz {(ball.Z - lastBall.Z) / DT:0.00} {(down ? "DOWN" : "")} {(point == 0 ? "heel" : "ball")}");
            if (k == 0) { lastHeel = heel; lastBall = ball; }
            if (down)
            {
                if (wasDown[k] && wasPoint[k] == point) stepSlide[k] += new Vector2(at.X - last[k].X, at.Z - last[k].Z).Length();
                soleSum += h; soleN++;
                soleWorstSink = Math.Min(soleWorstSink, h);
            }
            else if (wasDown[k]) { slides.Add(stepSlide[k]); stepSlide[k] = 0; }
            wasDown[k] = down;
            wasPoint[k] = point;
            last[k] = at;
        }
    }

    void Finish(Case c)
    {
        for (int k = 0; k < 2; k++) if (wasDown[k] && stepSlide[k] > 0) slides.Add(stepSlide[k]);
        string F(double x) => (x * 100).ToString("0.0", CultureInfo.InvariantCulture);
        string stump = c.armLost ? $" (the stump strays up to {stumpWorst:0} deg from held)" : c.look ? $" (its face {faceSum / Math.Max(1, faceN):0} deg off it)" : "";
        string line = $"| {c.name}{stump} | {string.Join(", ", clipsUsed)} | {slides.Count} | {F(slides.Count > 0 ? slides.Average() : 0)} | {F(slides.Count > 0 ? slides.Max() : 0)} | {F(soleN > 0 ? soleSum / soleN : 0)} | {F(soleWorstSink)} |";
        report.AppendLine(line);
        GD.Print(line);
    }

    /// <summary>Each derived gait's ground speed as drawn: in place at rate 1, the planted point's mean speed back.</summary>
    void Calibrate()
    {
        var lib = player.GetAnimationLibrary("more");
        if (lib == null) { GD.Print("calibrate: no clips_more"); return; }
        foreach (var name in lib.GetAnimationList())
        {
            var a = lib.GetAnimation(name);
            double dist = 0; int n = 0;
            var lastP = new Vector3[2]; var lastPt = new int[2]; var lastDown = new bool[2];
            var ankles = new[] { skel.FindBone("LeftFoot"), skel.FindBone("RightFoot") };
            var toes = new[] { skel.FindBone("LeftToes"), skel.FindBone("RightToes") };
            player.Play("more/" + name);
            int steps = (int)Math.Round(a.Length / DT);
            for (int i = 0; i <= steps; i++)
            {
                player.Seek(i * DT, true);
                for (int k = 0; k < 2; k++)
                {
                    var heel = skel.GetBoneGlobalPose(ankles[k]).Origin - new Vector3(0, HEEL_UP, 0);
                    var ball = skel.GetBoneGlobalPose(toes[k]).Origin - new Vector3(0, BALL_UP, 0);
                    int pt = heel.Y <= ball.Y ? 0 : 1;
                    var at = pt == 0 ? heel : ball;
                    bool down = at.Y < DOWN;
                    if (down && lastDown[k] && lastPt[k] == pt) { dist += lastP[k].Z - at.Z; n++; }
                    lastDown[k] = down; lastPt[k] = pt; lastP[k] = at;
                }
            }
            GD.Print($"calibrate: more/{name} {a.Length:0.000}s ground speed {(n > 0 ? dist / (n * DT) : 0):0.000} m/s (Gaits.Measure {Gaits.SpeedOf("more/" + name):0.000})");
        }
    }

    static T? Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T f) return f;
        return null;
    }
}
