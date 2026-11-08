// The character lab, part two: ground to stand on (flat, a sidewalk kerb, a hill of any slope, stairs),
// foot IK on it (FootIK.cs), and the IK test.
//
// The ground has colliders, so the feet can ray-cast to it. The kerb is 15 cm (a street kerb),
// its edge along x at z = 1, the sidewalk on the +z side; the hill rises towards +z from z = 1; the
// stairs (18 cm rise, 28 cm going: a house stair) climb towards +z from z = 1, 3 m wide, eight steps
// to a landing.
//
//   --ground=flat|sidewalk|hill|stairs --slope=DEG (hill, default 25) --noik
//   --iktest=DIR   stand and walk the character on the kerb, the hill and the stairs, facing round,
//                  with the IK on and off; print each foot's sole error (how far it floats, or sinks
//                  when negative) and save a screenshot per case into DIR (plus DIR/iktest.md).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    enum Ground { Flat, Sidewalk, Hill, Stairs }
    Ground ground = Ground.Flat;
    double slopeDeg = 25;
    Node3D? groundNode;
    FootIK? footIk;
    bool ikOn = true, worldCam;
    float camFocusY = 0.8f, rootY;
    const float KerbHeight = 0.15f, FeatureStart = 1, Rise = 0.18f, Going = 0.28f;
    const int Steps = 8;

    void SetupIk(Dictionary<string, string> args)
    {
        if (args.TryGetValue("ground", out var g)) ground = g switch { "sidewalk" => Ground.Sidewalk, "hill" => Ground.Hill, "stairs" => Ground.Stairs, _ => Ground.Flat };
        if (args.TryGetValue("slope", out var sl)) slopeDeg = Num(sl);
        if (args.ContainsKey("noik")) ikOn = false;
        BuildGround();
        footIk = new FootIK(actor, Find<Skeleton3D>(actor)!, this);
    }

    void BuildGround()
    {
        groundNode?.QueueFree();
        groundNode = new Node3D { Name = "Ground" };
        AddChild(groundNode);
        Box(new Vector3(200, 1, 200), new Transform3D(Basis.Identity, new Vector3(0, -0.5f, 0)), null);  // the floor, collision only
        if (ground == Ground.Sidewalk)
            Box(new Vector3(12, KerbHeight, 4), new Transform3D(Basis.Identity, new Vector3(0, KerbHeight / 2, FeatureStart + 2)), new Color(0.42f, 0.42f, 0.4f));
        else if (ground == Ground.Hill)
        {
            float a = Mathf.DegToRad((float)slopeDeg), length = 12;
            var basis = new Basis(Vector3.Right, -a);  // top face tilts up towards +z
            var uphill = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
            var normal = basis * Vector3.Up;
            var topCentre = new Vector3(0, 0, FeatureStart) + uphill * (length / 2);
            Box(new Vector3(12, 1, length), new Transform3D(basis, topCentre - normal * 0.5f), new Color(0.27f, 0.32f, 0.22f));
        }
        else if (ground == Ground.Stairs)
            for (int i = 0; i <= Steps; i++)
            {
                float h = (i + 1) * Rise, depth = i == Steps ? 2 : Going;  // the last is the landing
                var shade = i % 2 == 0 ? 0.5f : 0.45f;
                Box(new Vector3(3, h, depth), new Transform3D(Basis.Identity, new Vector3(0, h / 2, FeatureStart + i * Going + depth / 2)), new Color(shade, shade * 0.9f, shade * 0.8f));
            }
    }

    void Box(Vector3 size, Transform3D at, Color? color)
    {
        var body = new StaticBody3D { Transform = at };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        if (color is Color c)
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = c, Roughness = 1 } });
        groundNode!.AddChild(body);
    }

    /// <summary>The character's root height: the ground under its centre, smoothed (a step up the kerb takes a moment).</summary>
    float RootHeight(Vector3 at, double delta, bool snap = false)
    {
        var (y, _) = FootIK.Ground(actor, new Vector3(at.X, rootY, at.Z));
        rootY = snap ? y : Mathf.Lerp(rootY, y, 1 - (float)Math.Exp(-12 * delta));
        return rootY;
    }

    void UpdateIk(string state)
    {
        // Off while the car's seat has the legs (LabCar.cs: its own leg IK onto the pedals).
        if (footIk != null) footIk.Enabled = ikOn && state == "upright" && !(carActive && seat != null && seat.LegW > 0) && !RagdollOn;
    }

    // ------------------------------------------------------------------ the IK test

    record Case(string Name, Ground Ground, double Facing, bool Walk);

    /// <summary>Facing in degrees from "uphill / onto the sidewalk" (+z), clockwise seen from above.</summary>
    static double HeadingFor(double facing) => Mathf.DegToRad(90 - facing);

    async Task RunIkTest(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var cases = new List<Case>();
        foreach (var g in new[] { Ground.Sidewalk, Ground.Hill, Ground.Stairs })
        {
            foreach (var f in g == Ground.Stairs ? new[] { 0, 90, 180, 270 } : new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                cases.Add(new Case($"{g}-stand-{f}".ToLowerInvariant(), g, f, false));
            foreach (var f in new[] { 0, 90, 180 }) cases.Add(new Case($"{g}-walk-{f}".ToLowerInvariant(), g, f, true));
        }
        var report = new StringBuilder();
        report.AppendLine($"Foot IK test: kerb {KerbHeight * 100:0} cm, hill {slopeDeg:0} deg, stairs {Rise * 100:0} cm rise x {Going * 100:0} cm going (standing on a riser line). Sole error in cm (+ floats, - sinks): the ankle's height over the ground under it, less the ankle height on that slope.");
        report.AppendLine("Facing: degrees clockwise from uphill / onto the sidewalk. Stand: mean of each foot. Walk: the planted (lower) foot over two cycles, mean and worst.");
        report.AppendLine();
        report.AppendLine("| case | IK | left | right | walk mean | walk worst sink | walk worst float |");
        report.AppendLine("|---|---|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        move = false;
        follow = true;
        worldCam = true;
        camPitch = 6;
        camSize = 2.6;
        camFocusY = 0.45f;
        camYaw = 90;  // from +x, looking along the kerb edge / across the hill: the step and the slope in profile
        foreach (var c in cases)
        {
            if (ground != c.Ground) { ground = c.Ground; BuildGround(); await Frames(3); }
            heading = HeadingFor(c.Facing);
            testPosition = c.Ground switch
            {
                Ground.Hill => new Vector3(0, 0, 3.5f),
                Ground.Stairs => new Vector3(0, 0, FeatureStart + 3 * Going + 0.02f),  // just over the 4th riser
                _ => new Vector3(0, 0, FeatureStart),
            };
            targetSpeed = speed = c.Walk ? 1.37 : 0;
            foreach (bool on in new[] { true, false })
            {
                ikOn = on;
                footIk!.Enabled = on;
                RootHeight(testPosition.Value, 0, snap: true);
                await Frames(40);
                var (l, r, mean, sink, flt) = await Measure(c.Walk ? 130 : 30);
                string F(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture);
                string line = c.Walk
                    ? $"| {c.Name} | {(on ? "on" : "off")} | | | {F(mean)} | {F(sink)} | {F(flt)} |"
                    : $"| {c.Name} | {(on ? "on" : "off")} | {F(l)} | {F(r)} | | | |";
                report.AppendLine(line);
                GD.Print(line);
                if (on) await Shot(System.IO.Path.Combine(dir, c.Name + ".png"));
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "iktest.md"), report.ToString());
        GD.Print("iktest: done");
        GetTree().Quit();
    }

    Vector3? testPosition;

    async Task<(double left, double right, double mean, double sink, double flt)> Measure(int frames)
    {
        var skel = Find<Skeleton3D>(actor)!;
        int[] feet = { skel.FindBone("LeftFoot"), skel.FindBone("RightFoot") };
        double l = 0, r = 0, sum = 0, sink = 0, flt = double.MinValue;
        for (int i = 0; i < frames; i++)
        {
            await Frames(1);
            var e = new double[2];
            for (int k = 0; k < 2; k++)
            {
                // With the IK on, the ground it aimed at in the same frame (the foot slides in place,
                // so a ray a frame later can land on the next step).
                var f = footIk!.Feet[k];
                var ankle = ikOn ? f.FinalAnkle : (skel.GlobalTransform * skel.GetBoneGlobalPose(feet[k])).Origin;
                var (gy, n) = ikOn ? (f.GroundY, f.GroundNormal) : FootIK.Ground(actor, ankle);
                e[k] = ankle.Y - gy - FootIK.AnkleHeight / Math.Max(0.5f, n.Y);
            }
            l += e[0]; r += e[1];
            double planted = Math.Min(e[0], e[1]);
            sum += planted;
            sink = Math.Min(sink, planted);
            flt = Math.Max(flt, planted);
        }
        return (l / frames, r / frames, sum / frames, sink, flt);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task Shot(string path)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }
}
