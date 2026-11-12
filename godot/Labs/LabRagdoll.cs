// The character lab, part eight: ragdoll (docs/characters.md section 6). Cosmetic, as the plan says:
// the sim decides "knocked down at tick T, lies at (x, y), gets up at T2"; the renderer plays the
// ragdoll between and blends into a get-up clip that ends where the body is.
//
// The ragdoll is defined once, on the profile's bone names (so every character on the shared rig has
// it): 12 bodies, hands and feet folded into the forearms and shins.
//   Hips (box), Spine (capsule, to UpperChest), UpperChest (box), Head (sphere)
//   Upper/LowerArm, Upper/LowerLeg (capsules)
// Joints: cones with swing and twist limits (spine, neck, shoulders, hips), hinges for elbows and knees.
// Masses roughly a 75 kg adult's. The bodies sit on their own collision layer (2), colliding with the
// ground (1) and each other; the foot IK's ground rays look at layer 1 only, so they never hit the
// character's own shins (the bodies exist, following the clip, even when not simulating).
//
//   knock down   every body simulates, an impulse at the chest (backwards or forwards); the
//                simulator's influence is 1, so the clip underneath doesn't show
//   hit          a partial ragdoll: the upper body only (spine up, arms, head) at influence 0.6, an
//                impulse at the chest, for 0.6 s, then eased out: a hit reaction over the clip
//   get up       once the body has come to rest (or on demand): face up -> the lying clip then
//                get_up; face down -> prone then prone_up. The root moves to under the hips, turned
//                so the clip's body lies along the ragdoll's, and the body blends from the pose it lay
//                in (captured in world space, the physics stopped) into the clip over 0.4 s. (Not the
//                simulator's influence: its pose is relative to the skeleton, so moving the root while
//                it still had a say dragged the body a metre and more.)
//
//   --ragdoll=back|front|hit [--ragat=SECONDS] (hold the camera there for a screenshot) --noautoup
//   --ragtest=DIR   knock down both ways, get up, a hit; settle time, which way up, the head's
//                   largest jump in a frame while blending (a pop), screenshots

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    public const uint RagdollLayer = 2;  // collision layer bit (value 2 = layer 2)

    // bone, the bone its body reaches to (length), shape, radius or half-size, mass, joint, swing (or hinge max), twist (or hinge min)
    static readonly (string bone, string tip, char shape, float r, float mass, char joint, float a, float b)[] RagBodies =
    {
        ("Hips", "Spine", 'b', 0.15f, 11, '-', 0, 0),
        ("Spine", "UpperChest", 'c', 0.11f, 10, 'c', 20, 15),
        ("UpperChest", "Neck", 'b', 0.16f, 15, 'c', 20, 15),
        ("Head", "", 's', 0.11f, 5, 'c', 40, 45),
        ("LeftUpperArm", "LeftLowerArm", 'c', 0.045f, 2.2f, 'c', 80, 40),
        ("LeftLowerArm", "LeftHand", 'c', 0.04f, 1.6f, 'h', 140, 0),
        ("RightUpperArm", "RightLowerArm", 'c', 0.045f, 2.2f, 'c', 80, 40),
        ("RightLowerArm", "RightHand", 'c', 0.04f, 1.6f, 'h', 140, 0),
        ("LeftUpperLeg", "LeftLowerLeg", 'c', 0.065f, 8, 'c', 55, 20),
        ("LeftLowerLeg", "LeftFoot", 'c', 0.05f, 4.5f, 'h', 0, -140),
        ("RightUpperLeg", "RightLowerLeg", 'c', 0.065f, 8, 'c', 55, 20),
        ("RightLowerLeg", "RightFoot", 'c', 0.05f, 4.5f, 'h', 0, -140),
    };
    static readonly string[] UpperBody = { "Spine", "UpperChest", "Head", "LeftUpperArm", "LeftLowerArm", "RightUpperArm", "RightLowerArm" };

    PhysicalBoneSimulator3D? ragSim;
    readonly Dictionary<string, PhysicalBone3D> ragBones = new();
    string ragState = "";         // "", "down", "hit", "getup"
    double ragTime, ragImpulse = 45, ragHold = -1;
    bool ragAutoUp = true, ragFaceUp;
    Vector3 ragLastHead, ragHeadDrawn;
    bool ragForward, ragCaptureReq;
    Transform3D[]? ragCaptured;  // every bone's world pose as the body lay, captured when getting up
    float ragBlendW;             // 1 = the captured pose, 0 = the clip
    public double RagPop;          // the head's largest jump in one frame during the get-up blend (m)
    public double RagSettled;      // seconds from the knock to rest
    // Where the head lies from the hips in the lying and prone clips, in the character's space (set up once).
    Vector3 lieHeadDir, proneHeadDir;

    void SetupRagdoll(Dictionary<string, string> args)
    {
        var skel = Find<Skeleton3D>(actor)!;
        ragSim = new PhysicalBoneSimulator3D { Name = "Ragdoll", Active = false, Influence = 1 };
        skel.AddChild(ragSim);  // last in the stack: when it's on, it has the final say
        int headBone = skel.FindBone("Head");
        // Keep the body in one piece: the simulator writes positions too, and joints stretch under an
        // impulse (the partial hit pulled the chest 20 cm off the hips). On our rig only the hips ever
        // translate (no clip moves another bone), so every other bone gets its rest offset back and keeps
        // the ragdoll's rotation.
        skel.AddChild(new CallbackModifier { Name = "RagJoin", Run = sk =>
        {
            if (ragState is not ("down" or "hit" or "capture")) return;
            int hips = sk.FindBone("Hips");
            for (int b = 0; b < sk.GetBoneCount(); b++)
                if (b != hips) sk.SetBonePosePosition(b, sk.GetBoneRest(b).Origin);
        } });
        // The get-up blend: from the captured lying pose (world space) into the clip, by ragBlendW.
        skel.AddChild(new CallbackModifier { Name = "RagBlend", Run = RagBlend });
        // Last: the head as drawn this frame (Skeleton3D restores its poses after the modifiers), and
        // the capture of the whole pose when getting up.
        skel.AddChild(new CallbackModifier { Name = "RagProbe", Run = sk =>
        {
            ragHeadDrawn = sk.GlobalTransform * sk.GetBoneGlobalPose(headBone).Origin;
            if (!ragCaptureReq) return;
            var cap = new Transform3D[sk.GetBoneCount()];
            for (int b = 0; b < cap.Length; b++) cap[b] = sk.GlobalTransform * sk.GetBoneGlobalPose(b);
            ragCaptured = cap;
            ragCaptureReq = false;
        } });
        foreach (var (bone, tip, shape, r, mass, joint, a, b) in RagBodies)
        {
            int id = skel.FindBone(bone);
            var rest = skel.GetBoneGlobalRest(id);
            float len = tip == "" ? 0.2f : rest.Origin.DistanceTo(skel.GetBoneGlobalRest(skel.FindBone(tip)).Origin);
            var pb = new PhysicalBone3D { Name = "Rag" + bone, Mass = mass, Friction = 0.8f, LinearDamp = 0.1f, AngularDamp = 1.5f };
            pb.Set("bone_name", bone);
            pb.CollisionLayer = RagdollLayer;
            pb.CollisionMask = 1 | RagdollLayer;
            Shape3D s = shape switch
            {
                'b' => new BoxShape3D { Size = bone == "Hips" ? new Vector3(0.32f, 0.2f, 0.2f) : new Vector3(0.34f, len, 0.22f) },
                's' => new SphereShape3D { Radius = r },
                _ => new CapsuleShape3D { Radius = r, Height = Math.Max(len, 2 * r + 0.01f) },
            };
            pb.AddChild(new CollisionShape3D { Shape = s });
            // The body centred on its segment (along the bone's +Y, which points at its child).
            float half = bone == "Hips" ? 0.02f : len / 2;
            pb.BodyOffset = new Transform3D(Basis.Identity, new Vector3(0, half, 0));
            // The joint at the bone's head. Hinges: knees about the character's left-right axis; elbows (the
            // arm along left-right in the T-pose, palm down) about the vertical, so the forearm folds forward.
            var hingeAxis = bone.Contains("Arm") ? Vector3.Up : Vector3.Right;
            var lateral = (rest.Basis.Orthonormalized().Inverse() * hingeAxis).Normalized();
            var y = Vector3.Up;
            var z = (lateral - y * lateral.Dot(y)).Normalized();
            var jb = new Basis(y.Cross(z), y, z);
            pb.JointType = joint switch { 'c' => PhysicalBone3D.JointTypeEnum.Cone, 'h' => PhysicalBone3D.JointTypeEnum.Hinge, _ => PhysicalBone3D.JointTypeEnum.None };
            pb.JointOffset = new Transform3D(joint == 'h' ? jb : Basis.Identity, new Vector3(0, -half, 0));
            if (joint == 'c')
            {
                pb.Set("joint_constraints/swing_span", a);
                pb.Set("joint_constraints/twist_span", b);
                pb.Set("joint_constraints/softness", 0.8f);
                pb.Set("joint_constraints/relaxation", 1.0f);
            }
            else if (joint == 'h')
            {
                pb.Set("joint_constraints/angular_limit_enabled", true);
                pb.Set("joint_constraints/angular_limit_upper", a);
                pb.Set("joint_constraints/angular_limit_lower", b);
                pb.Set("joint_constraints/angular_limit_relaxation", 1.0f);
            }
            ragSim.AddChild(pb);
            ragBones[bone] = pb;
        }
        (lieHeadDir, proneHeadDir) = (ClipHeadDir("lie_idle"), ClipHeadDir("prone_idle"));
        if (args.ContainsKey("noautoup")) ragAutoUp = false;
        if (args.TryGetValue("ragat", out var h)) ragHold = Num(h);
        if (args.TryGetValue("ragdoll", out var mode)) CallDeferred(nameof(StartRagdollDeferred), mode);
        if (args.TryGetValue("ragtest", out var dir)) _ = RunRagTest(dir);
    }

    void StartRagdollDeferred(string mode) { if (mode == "hit") RagHit(); else Knock(mode == "front"); }

    /// <summary>Hips to head in a clip's first frame, flat on the ground, in the character's space.</summary>
    Vector3 ClipHeadDir(string clip)
    {
        var tmp = GD.Load<PackedScene>(Mannequin).Instantiate<Node3D>();
        AddChild(tmp);
        var player = Find<AnimationPlayer>(tmp)!;
        player.Play(clip);
        player.Seek(0, true);
        var sk = Find<Skeleton3D>(tmp)!;
        var hips = sk.GetBoneGlobalPose(sk.FindBone("Hips")).Origin;
        var head = sk.GetBoneGlobalPose(sk.FindBone("Head")).Origin;
        var toChar = tmp.GlobalTransform.AffineInverse() * sk.GlobalTransform;  // the skeleton in the character's space
        var d = toChar.Basis * (head - hips);
        tmp.QueueFree();
        d.Y = 0;
        return d.Normalized();
    }

    void BuildRagdollTab(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(Btn("Knock back", () => Knock(false)));
        row.AddChild(Btn("Knock forward", () => Knock(true)));
        row.AddChild(Btn("Hit", RagHit));
        row.AddChild(Btn("Get up", RagGetUp));
        box.AddChild(row);
        Slider(box, "Impulse N·s", 0, 150, 1, ragImpulse, v => ragImpulse = v);
        var auto = new CheckBox { Text = "Get up by itself once at rest", ButtonPressed = ragAutoUp };
        auto.Toggled += on => ragAutoUp = on;
        box.AddChild(auto);
        box.AddChild(new Label { Text = "Cosmetic: the sim decides down, where and for how\nlong; the ragdoll plays between, then blends into\nthe get-up clip from wherever the body ended.", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    bool RagdollOn => ragState != "";

    void ResetStanding()
    {
        EndCar();
        EndDoor();
        weapon = "none";
        move = false;
        targetSpeed = speed = 0;
        targetCrouch = crouch = 0;
        pose = Pose.Stand;
        playback.Start("upright");
    }

    void Knock(bool forward)
    {
        if (ragSim == null) return;
        if (ragState != "") StopRagdoll();
        ResetStanding();
        // Arm first: the tree must have drawn the standing pose before the bodies take it (a knock
        // straight after a get-up started from the lying pose).
        ragState = "arming";
        ragForward = forward;
        ragTime = 0;
    }

    void KnockNow()
    {
        bool forward = ragForward;
        ragState = "down";
        ragTime = 0;
        RagSettled = -1;
        ragSim!.Active = true;
        ragSim.Influence = 1;
        ragSim.PhysicalBonesStartSimulation();
        var dir = actor.GlobalBasis.Z.Normalized() * (forward ? 1 : -1);  // the character faces +z
        ragBones["UpperChest"].ApplyCentralImpulse(dir * (float)ragImpulse * 0.7f);
        ragBones["Head"].ApplyCentralImpulse(dir * (float)ragImpulse * 0.15f);
        ragBones["Hips"].ApplyCentralImpulse(-dir * (float)ragImpulse * 0.15f);  // the feet go the other way
    }

    void RagHit()
    {
        if (ragSim == null) return;
        if (ragState != "") StopRagdoll();
        ragState = "hit";
        ragTime = 0;
        ragSim.Active = true;
        ragSim.Influence = 0.6f;
        ragSim.PhysicalBonesStartSimulation(new Godot.Collections.Array<StringName>(UpperBody.Select(b => new StringName(b))));
        var dir = -actor.GlobalBasis.Z.Normalized();  // hit from the front, the body thrown back
        ragBones["UpperChest"].ApplyCentralImpulse(dir * (float)ragImpulse * 0.35f + Vector3.Right * 3);
    }

    /// <summary>Getting up, step one: capture the pose as drawn (the next skeleton update), then stop the
    /// physics. The simulator's pose is relative to the skeleton: moving the root while it still has a say
    /// drags the body.</summary>
    void RagGetUp()
    {
        if (ragState != "down") return;
        ragCaptured = null;
        ragCaptureReq = true;
        ragState = "capture";
    }

    /// <summary>Step two: from the captured pose, which way up and where the clip's body must lie; move the root there.</summary>
    void RagPlaceForClip()
    {
        var skel = Find<Skeleton3D>(actor)!;
        var hipsT = ragCaptured![skel.FindBone("Hips")];
        // Which way up: the pelvis's forward (the character's +z at rest) pointing up means on its back.
        var restHips = skel.GetBoneGlobalRest(skel.FindBone("Hips")).Basis.Orthonormalized();
        // The body's front (the character's +z) in the hips bone's rest frame; the skeleton's space isn't the character's.
        var charToSkel = skel.GlobalBasis.Orthonormalized().Inverse() * actor.GlobalBasis.Orthonormalized();
        var fwdLocal = restHips.Inverse() * (charToSkel * Vector3.Back);
        var front = (hipsT.Basis.Orthonormalized() * fwdLocal).Normalized();
        ragFaceUp = front.Y > 0;
        // The root under the hips, turned so the clip's body lies along the ragdoll's (hips -> head).
        var head = ragCaptured[skel.FindBone("Head")].Origin;
        var along = head - hipsT.Origin;
        along.Y = 0;
        along = along.Normalized();
        var clipDir = ragFaceUp ? lieHeadDir : proneHeadDir;
        float yaw = Mathf.Atan2(along.X, along.Z) - Mathf.Atan2(clipDir.X, clipDir.Z);  // world yaw of the character's +z
        // The clip's hips sit off the root; put the root where the clip's hips land on the ragdoll's.
        var basis = new Basis(Vector3.Up, yaw);
        var hipsInClip = ClipHipsOffset(ragFaceUp ? "lie_idle" : "prone_idle");
        var root = hipsT.Origin - basis * hipsInClip;
        if (trace) GD.Print($"ragdoll: up faceUp={ragFaceUp} along {along} clipDir {clipDir} yaw {Mathf.RadToDeg(yaw):0} hipsInClip {hipsInClip} root {root} hips {hipsT.Origin}");
        root.Y = 0;
        testPosition = root;
        heading = Math.PI / 2 - yaw;  // the lab's heading: facing (cos h, 0, sin h)
        pose = ragFaceUp ? Pose.Rest : Pose.Prone;
        playback.Start(NodeOf(pose));
        ragSim!.PhysicalBonesStopSimulation();
        ragSim.Active = false;
        ragBlendW = 1;
        ragState = "getup";
        ragTime = 0;
        RagPop = 0;
        ragLastHead = ragHeadDrawn;
    }

    /// <summary>The get-up blend, inside the skeleton's update: each bone from the captured world pose
    /// (in this frame's skeleton space) to the clip's, parents first.</summary>
    void RagBlend(Skeleton3D sk)
    {
        if (ragBlendW <= 0.001f || ragCaptured == null) return;
        int n = sk.GetBoneCount();
        var inv = sk.GlobalTransform.AffineInverse();
        var anim = new Transform3D[n];
        for (int b = 0; b < n; b++) anim[b] = sk.GetBoneGlobalPose(b);
        foreach (int b in BoneOrder(sk))
        {
            var cap = inv * ragCaptured[b];
            var a = anim[b];
            var q = a.Basis.Orthonormalized().GetRotationQuaternion().Slerp(cap.Basis.Orthonormalized().GetRotationQuaternion(), ragBlendW);
            sk.SetBoneGlobalPose(b, new Transform3D(new Basis(q), a.Origin.Lerp(cap.Origin, ragBlendW)));
        }
    }

    List<int>? boneOrder;
    List<int> BoneOrder(Skeleton3D sk)
    {
        if (boneOrder != null) return boneOrder;
        var order = new List<int>();
        void Add(int b) { order.Add(b); foreach (int c in sk.GetBoneChildren(b)) Add(c); }
        foreach (int r in sk.GetParentlessBones()) Add(r);
        return boneOrder = order;
    }

    readonly Dictionary<string, Vector3> clipHips = new();

    Vector3 ClipHipsOffset(string clip)
    {
        if (clipHips.TryGetValue(clip, out var v)) return v;
        var tmp = GD.Load<PackedScene>(Mannequin).Instantiate<Node3D>();
        AddChild(tmp);
        var player = Find<AnimationPlayer>(tmp)!;
        player.Play(clip);
        player.Seek(0, true);
        var sk = Find<Skeleton3D>(tmp)!;
        v = tmp.GlobalTransform.AffineInverse() * sk.GlobalTransform * sk.GetBoneGlobalPose(sk.FindBone("Hips")).Origin;
        v.Y = 0;
        tmp.QueueFree();
        return clipHips[clip] = v;
    }

    void StopRagdoll()
    {
        ragSim?.PhysicalBonesStopSimulation();
        if (ragSim != null) { ragSim.Active = false; ragSim.Influence = 1; }
        ragState = "";
    }

    /// <summary>Each frame: rest detection, the hit's ease-out, the get-up blend.</summary>
    void UpdateRagdoll(double delta)
    {
        if (ragSim == null || ragState == "") return;
        ragTime += delta;
        speed = targetSpeed = 0;
        if (ragState == "arming") { if (ragTime > 0.1) KnockNow(); return; }
        if (ragState == "capture") { if (ragCaptured != null) RagPlaceForClip(); return; }
        if (ragState == "down")
        {
            float v = ragBones.Values.Max(b => b.LinearVelocity.Length());
            if (trace && (int)(ragTime * 4) != (int)((ragTime - delta) * 4))
                GD.Print($"ragdoll: t={ragTime:0.00} hips {ragBones["Hips"].GlobalPosition} head {ragBones["Head"].GlobalPosition} vmax {v:0.00} sim {ragSim.IsSimulatingPhysics()}");
            if (RagSettled < 0 && ragTime > 0.5 && v < 0.05f) RagSettled = ragTime;
            if (ragAutoUp && ragHold < 0 && RagSettled >= 0 && ragTime > RagSettled + 0.8) RagGetUp();
        }
        else if (ragState == "hit")
        {
            if (ragTime > 0.6) ragSim.Influence = (float)Math.Max(0, 0.6 * (1 - (ragTime - 0.6) / 0.4));
            if (ragTime > 1.0) StopRagdoll();
        }
        else if (ragState == "getup")
        {
            const double blend = 0.4;
            ragBlendW = (float)Math.Max(0, 1 - Smooth(ragTime / blend));
            var head = ragHeadDrawn;  // last frame's, as drawn
            if (trace) GD.Print($"ragdoll: getup t={ragTime:0.000} blend {ragBlendW:0.00} head {head} state {playback.GetCurrentNode()}");
            RagPop = Math.Max(RagPop, head.DistanceTo(ragLastHead));
            ragLastHead = head;
            if (ragTime > blend + 0.25)  // lying for a moment, then up
            {
                pose = Pose.Stand;
                ragBlendW = 0;
                ragState = "";
            }
        }
    }

    string RagdollReadout() => ragState switch
    {
        "arming" => "ragdoll  ...",
        "down" => $"ragdoll  down {ragTime:0.0} s" + (RagSettled >= 0 ? $", at rest after {RagSettled:0.0} s" : ""),
        "hit" => $"ragdoll  hit (upper body), influence {ragSim!.Influence:0.00}",
        "capture" => "ragdoll  ...",
        "getup" => $"ragdoll  blending out ({(ragFaceUp ? "on its back" : "face down")}), {ragBlendW:0.00}",
        _ => "",
    };

    // ------------------------------------------------------------------ the ragdoll test

    async Task RunRagTest(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        void Line(string l) { report.AppendLine(l); GD.Print(l); }
        Line("Ragdoll test: knocked down backwards and forwards, then up; a hit (upper body). Times in s, distances in cm.");
        Line("");
        Line("Settle: from the knock until every body moves under 5 cm/s. Up: which way up it was read as. Pop: the head's largest jump in one frame while the ragdoll blends into the clip (a smooth blend at 60 fps moves it a few cm a frame).");
        Line("");
        Line("| case | settle | lies | pop | head after the blend |");
        Line("|---|--:|---|--:|--:|");
        worldCam = true;
        follow = false;
        camYaw = 60; camPitch = 30; camSize = 4.0; camFocusY = 0.4f;
        move = false;
        testPosition = Vector3.Zero;
        heading = Math.PI / 2;
        await Frames(10);
        foreach (bool forward in new[] { false, true })
        {
            string name = forward ? "forward" : "back";
            ragAutoUp = false;
            Knock(forward);
            await Seconds(0.35);
            await Shot(System.IO.Path.Combine(dir, $"{name}-falling.png"));
            while (ragState == "arming" || (RagSettled < 0 && ragTime < 6)) await Frames(1);
            await Seconds(0.3);
            await Shot(System.IO.Path.Combine(dir, $"{name}-down.png"));
            RagGetUp();
            while (ragState == "capture") await Frames(1);
            bool faceUp = ragFaceUp;  // read once the captured pose has decided it
            while (ragState == "getup") await Frames(1);
            double pop = RagPop;
            var headY = ragHeadDrawn.Y;
            await Shot(System.IO.Path.Combine(dir, $"{name}-blended.png"));
            await Seconds(0.8);
            await Shot(System.IO.Path.Combine(dir, $"{name}-getting-up.png"));
            Line($"| knocked {name} | {RagSettled.ToString("0.0", CultureInfo.InvariantCulture)} | {(faceUp ? "on its back" : "face down")} | {(pop * 100).ToString("0.0", CultureInfo.InvariantCulture)} | {(headY * 100).ToString("0", CultureInfo.InvariantCulture)} |");
            await Seconds(3.5);  // the get-up clip runs out
            testPosition = Vector3.Zero;
            heading = Math.PI / 2;
            await Frames(10);
        }
        RagHit();
        await Seconds(0.25);
        await Shot(System.IO.Path.Combine(dir, "hit.png"));
        while (ragState == "hit") await Frames(1);
        Line("| hit (upper body, 1 s) | - | standing | - | - |");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ragtest.md"), report.ToString());
        GD.Print("ragtest: done");
        GetTree().Quit();
    }
}
