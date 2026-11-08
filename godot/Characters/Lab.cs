// The character lab (docs/characters.md): a floor with a 1 m grid (a sim tile), a light, a camera,
// and one character driven through an AnimationTree by a panel of controls, to try transitions:
// idle -> walk -> jog -> run, crouching, going prone and crawling (a survivor's army crawl, or a
// crawling zombie's arm drag), and lying on the back to rest or sleep.
//
// Movement is code-driven as in the game: clips play in place and the lab moves the character
// (round a circle when "Move" is on), so foot sliding shows up here first. The tree:
//   state machine: upright -> lie_down -> lying -> get_up -> upright   (rest: on the back)
//                  upright -> go_prone -> prone -> prone_up -> upright (stance: face down)
//   upright: BlendSpace2D (x = speed m/s, y = crouch 0..1) of idle, walk, jog, run, crouch_idle,
//            crouch_walk, then a TimeScale set to the cycle rate for the speed (RATES below).
//   prone:   BlendSpace2D (x = speed, y = crawl style: 0 army crawl, 1 zombie) of prone_idle,
//            prone_crawl, zombie_crawl, and its TimeScale.
// Changing stance waits until the character has stopped and stood straight (the transition
// clips start from standing), then travels the state machine. Over the whole state machine sit
// two "stump" layers (Blend2 filtered to one arm's bones): an arm that has lost its hand or
// forearm is held in close whatever the rest of the body does.
//
// Missing limbs (docs/characters.md): the skeleton never changes; the body is region meshes, and
// a cut hides the regions below it and shows a stump cap. No legs means crawling (no crutch or
// hopping clips yet): one leg lost, the army crawl; both, the zombie's arm drag.
// Every locomotion clip is one gait cycle in the same phase (art/scripts/build_test_mannequin.py),
// so blending two of them keeps the feet together; the rate sets the cadence, the speed the stride.
//
// It also checks the character's skeleton against Godot's SkeletonProfileHumanoid.
//
//   Godot --path godot res://Characters/Lab.tscn [-- options]
//   --check                 print the skeleton check and quit (exit code 1 on a mismatch)
//   --shot=out.png --frames=N   save a screenshot after N frames and quit
//   --lose=armLeft:elbow,legRight:knee   (arm cuts hand|elbow|shoulder, leg cuts foot|knee|hip)
//   --speed=M --crouch=0..1 --stance=rest|prone --style=0..1 --view=iso|side|front|top --still   start that way
//   --tab=Move|Body|Clothes|Ground|Crowd|Hands --zoom=SIZE --novsync (frame times uncapped)
//   --demo                  run the demo (every transition in turn, looping); --trace prints the state twice a second
//
// Keys: 1-4 idle/walk/jog/run, C crouch, X prone, Z rest on the back, right-drag orbit, wheel zoom.

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab : Node3D
{
    const string Mannequin = "res://assets/characters/test_mannequin.glb";

    // Cut levels per limb: 0 whole, then each level hides one more region from the end.
    static readonly string[] ArmCuts = { "whole", "hand", "elbow", "shoulder" };
    static readonly string[] LegCuts = { "whole", "foot", "knee", "hip" };
    static readonly string[] ArmRegions = { "hand", "forearm", "upperarm" }, ArmStumps = { "wrist", "elbow", "shoulder" };
    static readonly string[] LegRegions = { "foot", "shin", "thigh" }, LegStumps = { "ankle", "knee", "hip" };
    static readonly string[] Sides = { "Left", "Right" };
    readonly int[] armCut = new int[2], legCut = new int[2];
    readonly OptionButton[] armPick = new OptionButton[2], legPick = new OptionButton[2];
    static readonly string[] Looping = { "idle", "walk", "jog", "run", "crouch_idle", "crouch_walk", "lie_idle", "prone_idle", "prone_crawl", "zombie_crawl",
        "breath_shallow", "breath_heavy", "shiver", "scan" };

    // Gait cycles per second at a speed (m/s), standing and crouched; linear in between.
    static readonly (double speed, double rate)[] StandRates = { (0, 0.5), (1.37, 0.94), (2.4, 1.25), (3.5, 1.5) };
    static readonly (double speed, double rate)[] CrouchRates = { (0, 0.5), (1.0, 0.9) };
    static readonly (double speed, double rate)[] ProneRates = { (0, 0.4), (0.4, 0.5) };
    const double MaxSpeed = 3.5, MaxCrouchSpeed = 1.0, MaxCrawlSpeed = 0.4, CircleRadius = 3;

    // The stance asked for, and the state machine node it rests in.
    enum Pose { Stand, Prone, Rest }
    static string NodeOf(Pose p) => p switch { Pose.Prone => "prone", Pose.Rest => "lying", _ => "upright" };

    Node3D actor = null!;
    AnimationTree tree = null!;
    AnimationNodeStateMachinePlayback playback = null!;
    readonly List<AnimationNodeStateMachineTransition> transitions = new();

    // Controls (targets) and the character's actual state, which follows them over time.
    double targetSpeed, targetCrouch, crawlStyle, accel = 4, crouchTime = 0.35;
    double speed, crouch, angle, heading;
    Pose pose = Pose.Stand;
    bool move = true, follow = true;
    Label readout = null!;

    // Camera: orbit round a target (the character when following).
    Camera3D cam = null!;
    double camYaw = 45, camPitch = 35, camSize = 6;
    bool orbiting;

    string? shotPath;
    int shotFrames = 45, frame;
    bool demo, trace;
    double demoTime, traceTime;

    // The demo: (at seconds, target speed, crouch, pose, crawl style), looping.
    static readonly (double at, double speed, double crouch, Pose pose, double style)[] Demo =
    {
        (0, 0, 0, Pose.Stand, 0), (1.5, 1.37, 0, Pose.Stand, 0), (4, 2.4, 0, Pose.Stand, 0), (6.5, MaxSpeed, 0, Pose.Stand, 0),
        (9, 0, 0, Pose.Stand, 0), (10.5, 0, 1, Pose.Stand, 0), (11.5, 1.0, 1, Pose.Stand, 0), (14, 0, 1, Pose.Stand, 0),
        (15, 0, 0, Pose.Prone, 0), (17.5, MaxCrawlSpeed, 0, Pose.Prone, 0), (21.5, MaxCrawlSpeed, 0, Pose.Prone, 1),
        (25.5, 0, 0, Pose.Prone, 1), (26.5, 0, 0, Pose.Rest, 0), (32, 0, 0, Pose.Stand, 0), (36, 0, 0, Pose.Stand, 0),
    };

    public override void _Ready()
    {
        var args = ParseArgs();
        args.TryGetValue("shot", out shotPath);
        if (args.TryGetValue("frames", out var f)) shotFrames = int.Parse(f);

        BuildStage();
        actor = GD.Load<PackedScene>(Mannequin).Instantiate<Node3D>();
        AddChild(actor);
        bool ok = CheckSkeleton(actor);
        if (args.ContainsKey("check"))
        {
            GD.Print(ok ? "lab: check passed" : "lab: check FAILED");
            GetTree().Quit(ok ? 0 : 1);
            return;
        }
        BuildTree();
        SetupIk(args);
        SetupHands(args);
        SetupBody(args);
        SetupClothes(args);
        if (args.TryGetValue("zoom", out var zm)) camSize = Num(zm);
        SetupCrowd(args);
        BuildPanel();
        if (args.TryGetValue("tab", out var tabName))
            for (int i = 0; i < tabs.GetTabCount(); i++) if (tabs.GetTabTitle(i) == tabName) tabs.CurrentTab = i;
        if (args.ContainsKey("novsync")) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0; }

        if (args.TryGetValue("speed", out var s)) speed = targetSpeed = Num(s);
        if (args.TryGetValue("crouch", out var c)) crouch = targetCrouch = Num(c);
        if (args.ContainsKey("still")) move = false;
        demo = args.ContainsKey("demo");
        trace = args.ContainsKey("trace");
        if (args.TryGetValue("view", out var v)) SetView(v);
        if (args.TryGetValue("style", out var sy)) crawlStyle = Num(sy);
        if (args.TryGetValue("lose", out var lose))
            foreach (var cut in lose.Split(','))
            {
                var kv = cut.Split(':');
                int side = kv[0].EndsWith("Right") ? 1 : 0;
                if (kv[0].StartsWith("arm")) armCut[side] = Math.Max(0, Array.IndexOf(ArmCuts, kv[1]));
                else legCut[side] = Math.Max(0, Array.IndexOf(LegCuts, kv[1]));
            }
        ApplyLimbs();
        if (args.TryGetValue("stance", out var st) && st is "rest" or "prone")
        {
            pose = st == "rest" ? Pose.Rest : Pose.Prone;
            playback.Start(NodeOf(pose));
            if (pose == Pose.Prone) speed = Math.Min(targetSpeed, MaxCrawlSpeed);
        }
        SyncPanel();
        if (args.TryGetValue("iktest", out var dir)) _ = RunIkTest(dir);
        if (args.TryGetValue("handtest", out var hdir)) _ = RunHandTest(hdir);
    }

    // ------------------------------------------------------------------ the animation tree

    void BuildTree()
    {
        var player = Find<AnimationPlayer>(actor)!;
        var lib = player.GetAnimationLibrary("");
        foreach (var name in lib.GetAnimationList())
            lib.GetAnimation(name).LoopMode = Array.IndexOf(Looping, (string)name) >= 0
                ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;

        var space = new AnimationNodeBlendSpace2D { MinSpace = Vector2.Zero, MaxSpace = new Vector2((float)MaxSpeed, 1), Sync = true };
        void Point(string clip, double x, double y) => space.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, new Vector2((float)x, (float)y));
        Point("idle", 0, 0);
        Point("walk", 1.37, 0);
        Point("jog", 2.4, 0);
        Point("run", MaxSpeed, 0);
        Point("crouch_idle", 0, 1);
        Point("crouch_walk", MaxCrouchSpeed, 1);

        var crawlSpace = new AnimationNodeBlendSpace2D { MinSpace = Vector2.Zero, MaxSpace = new Vector2((float)MaxCrawlSpeed, 1), Sync = true };
        foreach (var (clip, x, y) in new[] { ("prone_idle", 0.0, 0.0), ("prone_crawl", MaxCrawlSpeed, 0.0), ("prone_idle", 0.0, 1.0), ("zombie_crawl", MaxCrawlSpeed, 1.0) })
            crawlSpace.AddBlendPoint(new AnimationNodeAnimation { Animation = clip }, new Vector2((float)x, (float)y));
        var prone = new AnimationNodeBlendTree();
        prone.AddNode("space", crawlSpace);
        prone.AddNode("rate", new AnimationNodeTimeScale());
        prone.ConnectNode("rate", 0, "space");
        prone.ConnectNode("output", 0, "rate");

        var upright = new AnimationNodeBlendTree();
        upright.AddNode("space", space);
        upright.AddNode("rate", new AnimationNodeTimeScale());
        upright.ConnectNode("rate", 0, "space");
        upright.ConnectNode("output", 0, "rate");

        var sm = new AnimationNodeStateMachine();
        sm.AddNode("upright", upright);
        sm.AddNode("lie_down", new AnimationNodeAnimation { Animation = "lie_down" });
        sm.AddNode("lying", new AnimationNodeAnimation { Animation = "lie_idle" });
        sm.AddNode("get_up", new AnimationNodeAnimation { Animation = "get_up" });
        sm.AddNode("go_prone", new AnimationNodeAnimation { Animation = "go_prone" });
        sm.AddNode("prone", prone);
        sm.AddNode("prone_up", new AnimationNodeAnimation { Animation = "prone_up" });
        Link(sm, "Start", "upright", atEnd: false, auto: true);
        Link(sm, "upright", "lie_down", atEnd: false, auto: false);
        Link(sm, "lie_down", "lying", atEnd: true, auto: true);
        Link(sm, "lying", "get_up", atEnd: false, auto: false);
        Link(sm, "get_up", "upright", atEnd: true, auto: true);
        Link(sm, "upright", "go_prone", atEnd: false, auto: false);
        Link(sm, "go_prone", "prone", atEnd: true, auto: true);
        Link(sm, "prone", "prone_up", atEnd: false, auto: false);
        Link(sm, "prone_up", "upright", atEnd: true, auto: true);

        // The skeleton's track path ("Armature/Skeleton3D"), for the layer filters.
        var track = lib.GetAnimation("idle").TrackGetPath(0).ToString();
        string skelPath = track[..track.IndexOf(':')];
        var root = new AnimationNodeBlendTree();
        root.AddNode("sm", sm);
        root.ConnectNode("output", 0, BuildLayers(root, skelPath));  // arm holds, stumps, states (LabBody.cs)

        tree = new AnimationTree { TreeRoot = root };
        tree.AddAnimationLibrary("", lib);
        actor.AddChild(tree);  // root_node ".." = the character, where the clips' track paths start
        tree.Active = true;
        playback = (AnimationNodeStateMachinePlayback)tree.Get("parameters/sm/playback");
        SetXfade(0.25);
    }

    IEnumerable<string> BonesStartingWith(string prefix)
    {
        var skel = Find<Skeleton3D>(actor)!;
        for (int i = 0; i < skel.GetBoneCount(); i++)
            if (skel.GetBoneName(i).StartsWith(prefix)) yield return skel.GetBoneName(i);
    }

    void Link(AnimationNodeStateMachine sm, string from, string to, bool atEnd, bool auto)
    {
        var t = new AnimationNodeStateMachineTransition
        {
            SwitchMode = atEnd ? AnimationNodeStateMachineTransition.SwitchModeEnum.AtEnd : AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
            AdvanceMode = auto ? AnimationNodeStateMachineTransition.AdvanceModeEnum.Auto : AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled,
        };
        sm.AddTransition(from, to, t);
        if (from != "Start") transitions.Add(t);
    }

    void SetXfade(double seconds)
    {
        foreach (var t in transitions) t.XfadeTime = (float)seconds;
    }

    static double Lerp(IReadOnlyList<(double x, double y)> table, double x)
    {
        if (x <= table[0].x) return table[0].y;
        for (int i = 1; i < table.Count; i++)
            if (x <= table[i].x)
            {
                var (x0, y0) = table[i - 1];
                var (x1, y1) = table[i];
                return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
            }
        return table[^1].y;
    }

    static double CycleRate(double speed, double crouch) =>
        Lerp(StandRates, speed) * (1 - crouch) + Lerp(CrouchRates, Math.Min(speed, MaxCrouchSpeed)) * crouch;

    // ------------------------------------------------------------------ each frame

    public override void _Process(double delta)
    {
        if (tree == null) return;
        if (demo) RunDemo(delta);
        if (LegsLost() > 0 && pose == Pose.Stand) { pose = Pose.Prone; SyncPanel(); }  // can't stand: crawl
        string state = playback.GetCurrentNode();
        string goal = NodeOf(pose);
        bool moving = state is "upright" or "prone";  // the states you can move in

        // The character follows the controls: speed by acceleration, crouch over crouchTime.
        // A stance change first stops and straightens up (the transition clips start standing).
        bool changing = state != goal;
        double cap = (state == "prone" ? MaxCrawlSpeed : MaxSpeed + (MaxCrouchSpeed - MaxSpeed) * crouch) * SpeedCapScale();
        double want = changing || !moving ? 0 : Math.Min(targetSpeed, cap);
        speed = Approach(speed, want, accel * AccelScale() * delta);
        crouch = Approach(crouch, pose == Pose.Stand && state == "upright" ? targetCrouch : 0, delta / Math.Max(crouchTime * CrouchTimeScale(), 0.01));
        if (state == "upright") speed = Math.Min(speed, (MaxSpeed + (MaxCrouchSpeed - MaxSpeed) * crouch) * SpeedCapScale());

        bool settled = !moving || (speed < 0.05 && crouch < 0.05);
        if (changing && settled && travelling != goal) { playback.Travel(goal); travelling = goal; }
        if (!changing) travelling = null;

        double rate = state == "prone" ? Lerp(ProneRates, speed) : CycleRate(speed, crouch);
        double style = LegsLost() == 2 ? 1 : crawlStyle;
        tree.Set("parameters/sm/upright/space/blend_position", new Vector2((float)speed, (float)crouch));
        tree.Set("parameters/sm/upright/rate/scale", CycleRate(speed, crouch));
        tree.Set("parameters/sm/prone/space/blend_position", new Vector2((float)speed, (float)style));
        tree.Set("parameters/sm/prone/rate/scale", Lerp(ProneRates, speed));
        for (int i = 0; i < 2; i++)  // hold a stump in close (a hand or forearm gone; a whole arm shows nothing)
            tree.Set($"parameters/stump{Sides[i]}/blend_amount", armCut[i] is 1 or 2 ? 1.0 : 0.0);

        if (move && speed > 0)
        {
            angle += speed / CircleRadius * delta;
            heading = angle + Math.PI / 2;
        }
        var pos = testPosition ?? (move ? new Vector3((float)(Math.Cos(angle) * CircleRadius), 0, (float)(Math.Sin(angle) * CircleRadius)) : Vector3.Zero);
        if (!move && testPosition == null) heading = 0;
        pos.Y = RootHeight(pos, delta);
        actor.Position = pos;
        UpdateIk(state);
        UpdateBody(delta);
        UpdateHands(delta, state);
        UpdateCrowd(delta);
        actor.Basis = Basis.LookingAt(new Vector3((float)Math.Cos(heading), 0, (float)Math.Sin(heading)), Vector3.Up, useModelFront: true);
        PlaceCamera();

        readout.Text = $"state    {state}\nspeed    {speed:0.00} m/s ({Gait(state, speed, crouch, LegsLost() == 2 ? 1 : crawlStyle)})\ncrouch   {crouch:0.00}\n" +
                       $"cadence  {rate:0.00} cycles/s\nstride   {(speed > 0.01 ? speed / rate : 0):0.00} m/cycle\n" +
                       $"fps      {Engine.GetFramesPerSecond():0}" + (HandReadout() is { Length: > 0 } h ? "\n" + h : "") + (BodyReadout() is { Length: > 0 } b ? "\n" + b : "") + (ClothesReadout() is { Length: > 0 } c2 ? "\n" + c2 : "");

        traceTime += delta;
        if (trace && traceTime >= 0.5)
        {
            traceTime = 0;
            GD.Print($"lab: t={Time.GetTicksMsec() / 1000.0:0.0} state={state} speed={speed:0.00} crouch={crouch:0.00} pose={pose}");
        }

        frame++;
        if (shotPath != null && frame == shotFrames) _ = SaveShot();
    }

    void RunDemo(double delta)
    {
        demoTime = (demoTime + delta) % Demo[^1].at;
        var step = Demo[0];
        foreach (var d in Demo) if (d.at <= demoTime) step = d;
        bool changed = targetSpeed != step.speed || targetCrouch != step.crouch || pose != step.pose || crawlStyle != step.style;
        (targetSpeed, targetCrouch, pose, crawlStyle) = (step.speed, step.crouch, step.pose, step.style);
        if (changed) SyncPanel();
    }

    static string Gait(string state, double speed, double crouch, double style) =>
        state == "prone" ? (speed < 0.05 ? "prone" : style > 0.5 ? "zombie crawl" : "army crawl") :
        state != "upright" ? state :
        crouch > 0.5 ? (speed < 0.1 ? "crouched" : "crouch walk") :
        speed < 0.1 ? "idle" : speed < 1.9 ? "walk" : speed < 2.95 ? "jog" : "run";

    static double Approach(double v, double target, double step) =>
        v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    // ------------------------------------------------------------------ the panel

    HSlider speedSlider = null!, crouchSlider = null!, styleSlider = null!;
    CheckBox moveBox = null!, demoBox = null!;
    Button proneButton = null!, restButton = null!;
    string? travelling;

    void BuildPanel()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(12, 12) };
        layer.AddChild(panel);
        var outer = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddChild(new MarginContainer().Also(m => { foreach (var side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + side, 8); m.AddChild(outer); }));
        outer.AddChild(new Label { Text = "CHARACTER LAB" });
        tabs = new TabContainer();
        outer.AddChild(tabs);
        VBoxContainer Tab(string name)
        {
            var t = new VBoxContainer { Name = name };
            tabs.AddChild(t);
            return t;
        }

        var box = Tab("Move");
        var gaits = new HBoxContainer();
        foreach (var (name, v) in new[] { ("Idle", 0.0), ("Walk", 1.37), ("Jog", 2.4), ("Run", MaxSpeed) })
            gaits.AddChild(Btn(name, () => { targetSpeed = v; SyncPanel(); }));
        box.AddChild(gaits);
        speedSlider = Slider(box, "Speed m/s", 0, MaxSpeed, 0.01, targetSpeed, v => targetSpeed = v);
        Slider(box, "Accel m/s²", 0.5, 20, 0.1, accel, v => accel = v);
        crouchSlider = Slider(box, "Crouch", 0, 1, 0.01, targetCrouch, v => targetCrouch = v);
        Slider(box, "Crouch time s", 0.05, 1.5, 0.01, crouchTime, v => crouchTime = v);
        var stance = new HBoxContainer();
        stance.AddChild(Btn("Crouch (C)", ToggleCrouch));
        proneButton = Btn("Prone (X)", () => TogglePose(Pose.Prone));
        stance.AddChild(proneButton);
        restButton = Btn("Rest on back (Z)", () => TogglePose(Pose.Rest));
        stance.AddChild(restButton);
        box.AddChild(stance);
        styleSlider = Slider(box, "Crawl: army→zombie", 0, 1, 0.01, crawlStyle, v => crawlStyle = v);
        Slider(box, "Crossfade s", 0, 1, 0.01, 0.25, SetXfade);
        Slider(box, "Slow motion", 0.05, 1.5, 0.05, 1, v => Engine.TimeScale = v);
        var toggles = new HBoxContainer();
        moveBox = new CheckBox { Text = "Move", ButtonPressed = move };
        moveBox.Toggled += on => move = on;
        toggles.AddChild(moveBox);
        demoBox = new CheckBox { Text = "Demo", ButtonPressed = demo };
        demoBox.Toggled += on => { demo = on; demoTime = 0; };
        toggles.AddChild(demoBox);
        box.AddChild(toggles);

        box = Tab("Body");
        BuildBodyTab(box);
        box.AddChild(new Label { Text = "Missing limbs" });
        var limbs = new GridContainer { Columns = 2 };
        for (int i = 0; i < 2; i++)
        {
            int side = i;
            armPick[i] = Pick(limbs, Sides[i] + " arm", ArmCuts, armCut[i], v => { armCut[side] = v; ApplyLimbs(); });
        }
        for (int i = 0; i < 2; i++)
        {
            int side = i;
            legPick[i] = Pick(limbs, Sides[i] + " leg", LegCuts, legCut[i], v => { legCut[side] = v; ApplyLimbs(); });
        }
        box.AddChild(limbs);

        box = Tab("Ground");
        var groundRow = new HBoxContainer();
        groundRow.AddChild(new Label { Text = "Ground", CustomMinimumSize = new Vector2(60, 0) });
        groundPick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var g in new[] { "flat", "sidewalk", "hill", "stairs" }) groundPick.AddItem(g);
        groundPick.Selected = (int)ground;
        groundPick.ItemSelected += v => { ground = (Ground)(int)v; BuildGround(); };
        groundRow.AddChild(groundPick);
        var ikBox = new CheckBox { Text = "Foot IK", ButtonPressed = ikOn };
        ikBox.Toggled += on => ikOn = on;
        groundRow.AddChild(ikBox);
        box.AddChild(groundRow);
        Slider(box, "Hill slope °", 5, 40, 1, slopeDeg, v => { slopeDeg = v; if (ground == Ground.Hill) BuildGround(); });
        box.AddChild(new Label { Text = "Kerb 15 cm; stairs 18 cm rise, 28 cm going.\nThe character walks round a 3 m circle across them.", Modulate = new Color(1, 1, 1, 0.6f) });

        BuildClothesTab(Tab("Clothes"));
        BuildCrowdTab(Tab("Crowd"));
        BuildHandsTab(Tab("Hands"));

        // Always shown: the camera and the readout.
        var camRow = new HBoxContainer();
        var followBox = new CheckBox { Text = "Follow", ButtonPressed = follow };
        followBox.Toggled += on => follow = on;
        camRow.AddChild(followBox);
        foreach (var v in new[] { "iso", "side", "front", "top" })
            camRow.AddChild(Btn(v, () => { worldCam = false; SetView(v); }));
        outer.AddChild(camRow);
        Slider(outer, "Zoom", 1.5, 40, 0.1, camSize, v => camSize = v);
        readout = new Label();
        readout.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Consolas", "Courier New", "monospace" } });
        readout.AddThemeFontSizeOverride("font_size", 13);
        outer.AddChild(readout);
        outer.AddChild(new Label { Text = "1-4 speeds, C crouch, X prone, Z rest on back\nright-drag orbit, wheel zoom", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    OptionButton groundPick = null!;
    TabContainer tabs = null!;

    readonly List<(HSlider slider, Label value)> sliders = new();

    HSlider Slider(VBoxContainer box, string name, double min, double max, double step, double value, Action<double> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(140, 0) });
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0) };
        var label = new Label { Text = value.ToString("0.00", CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(44, 0) };
        s.ValueChanged += v => { label.Text = v.ToString("0.00", CultureInfo.InvariantCulture); changed(v); };
        row.AddChild(s);
        row.AddChild(label);
        box.AddChild(row);
        sliders.Add((s, label));
        return s;
    }

    static OptionButton Pick(GridContainer grid, string name, string[] items, int selected, Action<int> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(80, 0) });
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(80, 0) };
        foreach (var it in items) pick.AddItem(it);
        pick.Selected = selected;
        pick.ItemSelected += v => changed((int)v);
        row.AddChild(pick);
        grid.AddChild(row);
        return pick;
    }

    int LegsLost() => (legCut[0] > 0 ? 1 : 0) + (legCut[1] > 0 ? 1 : 0);

    /// <summary>Show the regions above each cut and the stump cap at it; hide the rest.</summary>
    void ApplyLimbs()
    {
        for (int i = 0; i < 2; i++)
        {
            Cut(Sides[i], ArmRegions, ArmStumps, armCut[i]);
            Cut(Sides[i], LegRegions, LegStumps, legCut[i]);
            armPick[i]?.Select(armCut[i]);
            legPick[i]?.Select(legCut[i]);
        }
        ApplyClothes();
    }

    void Cut(string side, string[] regions, string[] stumps, int level)
    {
        for (int r = 0; r < regions.Length; r++)  // regions[0] is the end of the limb (hand, foot)
        {
            if (actor.FindChild(regions[r] + "_" + side, true, false) is Node3D region) region.Visible = r >= level;
            if (actor.FindChild("stump_" + stumps[r] + "_" + side, true, false) is Node3D cap) cap.Visible = r == level - 1;
        }
    }

    static Button Btn(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    void SyncPanel()
    {
        speedSlider.SetValueNoSignal(targetSpeed);
        crouchSlider.SetValueNoSignal(targetCrouch);
        styleSlider.SetValueNoSignal(crawlStyle);
        foreach (var (s, l) in sliders) l.Text = s.Value.ToString("0.00", CultureInfo.InvariantCulture);
        moveBox.SetPressedNoSignal(move);
        demoBox.SetPressedNoSignal(demo);
        proneButton.Text = pose == Pose.Prone ? "Stand up (X)" : "Prone (X)";
        restButton.Text = pose == Pose.Rest ? "Get up (Z)" : "Rest on back (Z)";
    }

    void ToggleCrouch() { targetCrouch = targetCrouch > 0.5 ? 0 : 1; SyncPanel(); }
    void TogglePose(Pose p) { pose = pose == p ? Pose.Stand : p; SyncPanel(); }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Key1: targetSpeed = 0; break;
                case Key.Key2: targetSpeed = 1.37; break;
                case Key.Key3: targetSpeed = 2.4; break;
                case Key.Key4: targetSpeed = MaxSpeed; break;
                case Key.C: ToggleCrouch(); break;
                case Key.X: TogglePose(Pose.Prone); break;
                case Key.Z: TogglePose(Pose.Rest); break;
                case Key.F: raised = !raised; raisedBox.SetPressedNoSignal(raised); break;
                default: return;
            }
            SyncPanel();
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) orbiting = mb.Pressed;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) camSize = Math.Max(1.5, camSize * 0.9);
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) camSize = Math.Min(40, camSize * 1.1);
        }
        else if (e is InputEventMouseMotion mm && orbiting)
        {
            camYaw -= mm.Relative.X * 0.3;
            camPitch = Math.Clamp(camPitch + mm.Relative.Y * 0.3, 2, 89);
        }
    }

    // ------------------------------------------------------------------ stage and camera

    void SetView(string v)
    {
        // Yaw is relative to the character's facing when the camera follows (side = its left side).
        (camYaw, camPitch) = v switch { "side" => (90.0, 5.0), "front" => (0.0, 5.0), "top" => (45.0, 89.0), "face" => (15.0, 3.0), _ => (45.0, 35.0) };
        worldCam = false;
        if (v == "face") { camSize = 0.55; camFocusY = 1.67f; follow = true; }
        else if (camFocusY > 1.5f) { camFocusY = 0.8f; camSize = 6; }
    }

    void PlaceCamera()
    {
        var target = follow ? actor.Position : Vector3.Zero;
        double yaw = Mathf.DegToRad(camYaw) + (follow && !worldCam ? Math.PI / 2 - heading : 0);
        double pitch = Mathf.DegToRad(camPitch);
        var dir = new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Cos(yaw) * Math.Cos(pitch)));
        var focus = target + new Vector3(0, camFocusY, 0);
        cam.Size = (float)camSize;
        cam.Position = focus + dir * 80;  // far back: an ortho view's depth is free, and nothing in view is behind it
        cam.LookAt(focus, Math.Abs(camPitch) > 88 ? Vector3.Forward : Vector3.Up);
    }

    void BuildStage()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.05f, 0.06f, 0.055f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.6f),
                AmbientLightEnergy = 0.45f,
            },
        });
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.0f };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-55, 30, 0);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(120, 120) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.24f, 0.2f), Roughness = 1 },
        });
        AddChild(GridLines(60));
        cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = (float)camSize, Far = 300 };
        AddChild(cam);
    }

    static MeshInstance3D GridLines(int half)
    {
        var im = new ImmediateMesh();
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = -half; i <= half; i++)
        {
            im.SurfaceAddVertex(new Vector3(i, 0.002f, -half)); im.SurfaceAddVertex(new Vector3(i, 0.002f, half));
            im.SurfaceAddVertex(new Vector3(-half, 0.002f, i)); im.SurfaceAddVertex(new Vector3(half, 0.002f, i));
        }
        im.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = im,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.35f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
    }

    // ------------------------------------------------------------------ the skeleton check

    /// <summary>The skeleton has every profile bone, with the profile's parents and rest orientations.</summary>
    static bool CheckSkeleton(Node actor)
    {
        var skel = Find<Skeleton3D>(actor);
        if (skel == null) { GD.PrintErr("lab: no Skeleton3D"); return false; }
        var profile = new SkeletonProfileHumanoid();
        var profileGlobal = new Dictionary<string, Basis>();
        var problems = new List<string>();
        double worst = 0;
        for (int i = 0; i < profile.BoneSize; i++)
        {
            string name = profile.GetBoneName(i), parent = profile.GetBoneParent(i);
            var basis = profile.GetReferencePose(i).Basis;
            if (parent != "") basis = profileGlobal[parent] * basis;
            profileGlobal[name] = basis;
            int b = skel.FindBone(name);
            if (b < 0) { problems.Add($"missing {name}"); continue; }
            int p = skel.GetBoneParent(b);
            string have = p < 0 ? "" : skel.GetBoneName(p);
            if (have != parent) problems.Add($"{name}: parent {have}, profile {parent}");
            var rest = skel.GetBoneGlobalRest(b).Basis.Orthonormalized().GetRotationQuaternion();
            double deg = Mathf.RadToDeg(rest.AngleTo(basis.GetRotationQuaternion()));
            worst = Math.Max(worst, deg);
            if (deg > 1) problems.Add($"{name}: rest {deg:0.0} deg off the profile");
        }
        if (skel.GetBoneCount() != profile.BoneSize) problems.Add($"{skel.GetBoneCount()} bones, profile has {profile.BoneSize}");
        float headY = skel.GetBoneGlobalRest(skel.FindBone("Head")).Origin.Y;
        foreach (var pr in problems) GD.PrintErr("lab: " + pr);
        GD.Print($"lab: skeleton {skel.GetBoneCount()} bones, worst rest angle {worst:0.00} deg, head at {headY:0.00} m, {problems.Count} problem(s)");
        return problems.Count == 0;
    }

    // ------------------------------------------------------------------ helpers

    static T? Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (Find<T>(c) is T found) return found;
        return null;
    }

    static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    async System.Threading.Tasks.Task SaveShot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var err = GetViewport().GetTexture().GetImage().SavePng(shotPath!);
        GD.Print(err == Error.Ok ? $"shot: {shotPath}" : $"shot failed: {err}");
        GetTree().Quit();
    }

    static Dictionary<string, string> ParseArgs()
    {
        var args = new Dictionary<string, string>();
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        return args;
    }
}

static class NodeExt
{
    public static T Also<T>(this T node, Action<T> f) where T : Node { f(node); return node; }
}
