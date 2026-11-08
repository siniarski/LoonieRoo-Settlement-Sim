// The character lab, part four: hand IK (HandIK.cs) with weapons and a door.
//
// Weapons are test props built here from boxes (real ones come from Blender later), each with
// grips (see HandIK) and a hold: where it sits in the character's space, aimed by the aim pitch
// (turned about the shoulders). The arms come off the clip onto the grips; the legs keep walking.
//   pistol  both hands: the right on the grip, the left wrapped under it
//   rifle   butt in the right shoulder, right hand on the pistol grip, left under the handguard
//   knife   right hand only, blade up and forward (the left arm stays the clip's)
//   bat     both hands on the handle, bat up and back over the right shoulder, ready
// Guns have two holds: ready (low, at the chest) and aimed at eye level (the sights on the line
// from the eye along the aim: the right eye for the rifle, between the eyes for the pistol held out
// in both hands). Raising blends one into the other over the raise time. Aimed, the pitch is shared
// by the spine (60%), the neck and the head, and the rifle gets a cheek weld (the head tipped down
// and onto the stock). This runs inside the skeleton's update (AimPose, before the arm IK), so the
// weapon sits on this frame's body: its slump, its breathing, its aim. Ready holds ride the upper
// chest the same way.
// The door: a 90 cm door in a frame; the character stands at its anchor, reaches the lever,
// presses it, pushes the door open, and lets go. Two approaches (docs/characters.md section 5):
// quietly (slow reach, slow push to 60 deg) and fast (quick reach, a shove to 85 deg).
//
//   --weapon=pistol|rifle|knife|bat --aim=DEG --raised (aimed at eye level) --door=quiet|fast --nohandik
//   --handtest=DIR   every weapon standing and walking at three aim pitches, and both door
//                    approaches; prints each hand's distance from its grip and saves screenshots.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    static readonly string[] Weapons = { "none", "pistol", "rifle", "knife", "bat" };
    HandIK? handIk;
    readonly Dictionary<string, Node3D> weaponNodes = new();
    string weapon = "none";
    double aimPitch, weaponWeight, aimAmount, raiseTime = 0.3;
    bool handIkOn = true, raised;
    readonly Dictionary<string, Transform3D> readyLocal = new();  // ready holds in the upper chest's space
    CheckBox raisedBox = null!;
    Vector3 aimEye;
    // Eyes in the head bone's space (the mannequin's face: art/scripts/build_test_mannequin.py).
    static readonly Vector3 RightEye = new(-0.036f, 0.118f, 0.094f), EyesMid = new(0, 0.118f, 0.094f);
    OptionButton weaponPick = null!;

    // The door and its opening.
    Node3D? doorRoot, doorLeaf, doorLever, doorGrip;
    string? doorApproach;  // null = not opening
    double doorTime, doorAngle, leverAngle, doorHand;
    string doorPhase = "";
    static readonly Vector3 DoorHinge = new(0.45f, 0, 1.0f);
    // The anchor: 45 cm short of the door, the lever ahead and to the right (the character's right is -x).
    static readonly Vector3 DoorAnchor = new(-0.15f, 0, 0.55f);

    void SetupHands(Dictionary<string, string> args)
    {
        var skel = Find<Skeleton3D>(actor)!;
        skel.AddChild(new CallbackModifier { Name = "AimPose", Run = AimPose });  // before the arm IK
        handIk = new HandIK(actor, skel, this);
        foreach (var w in Weapons) if (w != "none") weaponNodes[w] = BuildWeapon(w);
        if (args.TryGetValue("weapon", out var w2)) weapon = w2;
        if (args.TryGetValue("aim", out var a)) aimPitch = Num(a);
        if (args.ContainsKey("nohandik")) handIkOn = false;
        if (args.ContainsKey("raised")) { raised = true; aimAmount = 1; }
        if (args.TryGetValue("door", out var d)) StartDoor(d);
    }

    void BuildHandsTab(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Weapon", CustomMinimumSize = new Vector2(60, 0) });
        weaponPick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var w in Weapons) weaponPick.AddItem(w);
        weaponPick.Selected = Math.Max(0, Array.IndexOf(Weapons, weapon));
        weaponPick.ItemSelected += v => weapon = Weapons[(int)v];
        row.AddChild(weaponPick);
        var ikBox = new CheckBox { Text = "Hand IK", ButtonPressed = handIkOn };
        ikBox.Toggled += on => handIkOn = on;
        row.AddChild(ikBox);
        box.AddChild(row);
        raisedBox = new CheckBox { Text = "Aim at eye level (F)", ButtonPressed = raised };
        raisedBox.Toggled += on => raised = on;
        box.AddChild(raisedBox);
        Slider(box, "Aim pitch °", -45, 45, 1, aimPitch, v => aimPitch = v);
        Slider(box, "Raise time s", 0.05, 1, 0.01, raiseTime, v => raiseTime = v);
        BuildActionRow(box);
        var door = new HBoxContainer();
        door.AddChild(Btn("Door: quietly", () => StartDoor("quiet")));
        door.AddChild(Btn("Door: fast", () => StartDoor("fast")));
        door.AddChild(Btn("No door", EndDoor));
        box.AddChild(door);
        box.AddChild(new Label { Text = "The door stands at the origin: the character\nwalks to it, reaches, presses, pushes, lets go.", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    // ------------------------------------------------------------------ props

    static MeshInstance3D Part(Mesh mesh, Vector3 at, Color color, Basis? basis = null) => new()
    {
        Mesh = mesh,
        Transform = new Transform3D(basis ?? Basis.Identity, at),
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.7f },
    };

    /// <summary>A grip marker: origin at the palm's centre, X the palm's normal, Y the fingers.</summary>
    static Node3D GripAt(string name, Vector3 at, Vector3 palm, Vector3 fingers)
    {
        var f = fingers.Normalized();
        var p = (palm - f * palm.Dot(f)).Normalized();
        return new Node3D { Name = name, Transform = new Transform3D(new Basis(p, f, p.Cross(f)), at) };
    }

    Node3D BuildWeapon(string kind)
    {
        var w = new Node3D { Name = "weapon_" + kind, Visible = false };
        var metal = new Color(0.18f, 0.18f, 0.2f);
        var wood = new Color(0.45f, 0.3f, 0.16f);
        switch (kind)
        {
            case "pistol":
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.03f, 0.035f, 0.19f) }, new Vector3(0, 0.045f, 0.06f), metal));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.03f, 0.11f, 0.045f) }, new Vector3(0, -0.035f, -0.005f), metal, new Basis(Vector3.Right, Mathf.DegToRad(-15))));
                w.AddChild(GripAt("grip_right", new Vector3(-0.025f, -0.03f, -0.005f), Vector3.Right, new Vector3(0, -0.25f, 1)));
                w.AddChild(GripAt("grip_left", new Vector3(0.03f, -0.045f, 0.0f), Vector3.Left, new Vector3(0, -0.35f, 1)));
                break;
            case "rifle":
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.04f, 0.12f, 0.26f) }, new Vector3(0, -0.02f, 0.13f), wood));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.05f, 0.08f, 0.36f) }, new Vector3(0, 0.01f, 0.44f), metal));
                w.AddChild(Part(new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.26f, RadialSegments = 8 }, new Vector3(0, 0.02f, 0.75f), metal, new Basis(Vector3.Right, Mathf.Pi / 2)));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.03f, 0.12f, 0.05f) }, new Vector3(0, -0.08f, 0.42f), metal));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.03f, 0.09f, 0.04f) }, new Vector3(0, -0.06f, 0.30f), metal, new Basis(Vector3.Right, Mathf.DegToRad(-15))));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.02f, 0.025f, 0.015f) }, new Vector3(0, 0.07f, RifleSight.Z), metal));  // rear sight
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.006f, 0.04f, 0.01f) }, new Vector3(0, 0.05f, 0.84f), metal));  // front sight
                w.AddChild(GripAt("grip_right", new Vector3(-0.025f, -0.05f, 0.30f), Vector3.Right, new Vector3(0, -0.2f, 1)));
                w.AddChild(GripAt("grip_left", new Vector3(0, -0.05f, 0.48f), Vector3.Up, new Vector3(-1, 0, 0.3f)));  // the handguard, in an adult's reach
                break;
            case "knife":
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.025f, 0.028f, 0.11f) }, new Vector3(0, 0, 0.055f), wood));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.05f, 0.012f, 0.012f) }, new Vector3(0, 0, 0.115f), metal));
                w.AddChild(Part(new BoxMesh { Size = new Vector3(0.004f, 0.03f, 0.16f) }, new Vector3(0, 0, 0.2f), new Color(0.7f, 0.7f, 0.72f)));
                w.AddChild(GripAt("grip_right", new Vector3(-0.015f, 0, 0.055f), Vector3.Right, Vector3.Down));
                break;
            case "bat":
                w.AddChild(Part(new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.014f, Height = 0.84f, RadialSegments = 10 }, new Vector3(0, 0, 0.42f), wood, new Basis(Vector3.Right, Mathf.Pi / 2)));
                w.AddChild(Part(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.015f, RadialSegments = 10 }, new Vector3(0, 0, 0.005f), wood, new Basis(Vector3.Right, Mathf.Pi / 2)));
                w.AddChild(GripAt("grip_left", new Vector3(0, -0.02f, 0.06f), Vector3.Up, Vector3.Right));
                w.AddChild(GripAt("grip_right", new Vector3(0, 0.02f, 0.15f), Vector3.Down, Vector3.Left));
                break;
        }
        actor.AddChild(w);
        return w;
    }

    // Rear sights (the point the eye looks over), in the weapon's space.
    static readonly Vector3 RifleSight = new(0, 0.085f, 0.28f), PistolSight = new(0, 0.07f, -0.02f);

    /// <summary>The weapon's ready hold in the character's space at an aim pitch (up positive).</summary>
    Transform3D Hold(string kind, double pitch)
    {
        Vector3 at, dir, up = Vector3.Up;
        switch (kind)
        {
            case "pistol": at = new Vector3(-0.03f, 1.36f, 0.42f); dir = Vector3.Back; break;
            case "rifle": at = new Vector3(-0.10f, 1.40f, 0.06f); dir = Vector3.Back; break;
            case "knife": at = new Vector3(-0.24f, 1.05f, 0.33f); dir = new Vector3(0, 0.87f, 0.5f); up = new Vector3(0, 0.5f, -0.87f); break;
            default: at = new Vector3(-0.08f, 1.2f, 0.28f); dir = new Vector3(-0.3f, 0.8f, -0.5f); up = Vector3.Back; break;  // bat, over the right shoulder
        }
        dir = dir.Normalized();
        var y = (up - dir * up.Dot(dir)).Normalized();
        var basis = new Basis(y.Cross(dir), y, dir);
        var hold = new Transform3D(basis, at);
        if (kind is "pistol" or "rifle")
        {
            // Aim: turn about the shoulders (up is a turn about -x for a character facing +z).
            var pivot = new Vector3(0, 1.42f, 0);
            var turn = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-(float)pitch)), Vector3.Zero);
            hold = new Transform3D(Basis.Identity, pivot) * turn * new Transform3D(Basis.Identity, -pivot) * hold;
        }
        return hold;
    }

    void BuildDoor()
    {
        doorRoot?.QueueFree();
        doorRoot = new Node3D { Name = "Door" };
        AddChild(doorRoot);
        var frame = new Color(0.35f, 0.3f, 0.25f);
        doorRoot.AddChild(Part(new BoxMesh { Size = new Vector3(0.08f, 2.1f, 0.12f) }, new Vector3(0.5f, 1.05f, 1.0f), frame));
        doorRoot.AddChild(Part(new BoxMesh { Size = new Vector3(0.08f, 2.1f, 0.12f) }, new Vector3(-0.5f, 1.05f, 1.0f), frame));
        doorRoot.AddChild(Part(new BoxMesh { Size = new Vector3(1.08f, 0.08f, 0.12f) }, new Vector3(0, 2.1f, 1.0f), frame));
        doorLeaf = new Node3D { Name = "Leaf", Position = DoorHinge };
        doorRoot.AddChild(doorLeaf);
        doorLeaf.AddChild(Part(new BoxMesh { Size = new Vector3(0.9f, 2.02f, 0.04f) }, new Vector3(-0.45f, 1.01f, 0), new Color(0.55f, 0.45f, 0.32f)));
        // The lever on the character's (-z) side, pivoting on its rose near the leaf's free edge.
        doorLever = new Node3D { Name = "Lever", Position = new Vector3(-0.82f, 1.0f, -0.045f) };
        doorLeaf.AddChild(doorLever);
        doorLever.AddChild(Part(new BoxMesh { Size = new Vector3(0.12f, 0.02f, 0.02f) }, new Vector3(0.06f, 0, -0.02f), new Color(0.7f, 0.65f, 0.45f)));
        doorLever.AddChild(Part(new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.01f }, Vector3.Zero, new Color(0.7f, 0.65f, 0.45f), new Basis(Vector3.Right, Mathf.Pi / 2)));
        doorGrip = GripAt("grip_handle", new Vector3(0.07f, 0.02f, -0.025f), Vector3.Down, new Vector3(0, -0.3f, 1));
        doorLever.AddChild(doorGrip);
    }

    void StartDoor(string approach)
    {
        if (doorRoot == null) BuildDoor();
        doorApproach = approach;
        doorTime = doorAngle = leverAngle = doorHand = 0;
        weapon = "none";
        if (weaponPick != null) weaponPick.Selected = 0;
        targetSpeed = speed = 0;
        move = false;
        heading = Math.PI / 2;  // facing +z, the door
        testPosition = DoorAnchor;
    }

    void EndDoor()
    {
        doorApproach = null;
        doorRoot?.QueueFree();
        doorRoot = null;
        doorGrip = null;
        testPosition = null;
    }

    static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    void UpdateDoor(double delta)
    {
        if (doorApproach == null || doorLeaf == null) return;
        bool quiet = doorApproach == "quiet";
        double reach = quiet ? 0.9 : 0.3, press = quiet ? 0.5 : 0.15, push = quiet ? 2.4 : 0.45;
        double open = quiet ? 60 : 85, letGo = quiet ? 30 : 14, release = quiet ? 0.5 : 0.15;
        double step = quiet ? 0.35 : 0.15;  // the character follows the door in while pushing
        doorTime += delta;
        double t = doorTime;
        if (t < reach) { doorPhase = "reach"; doorHand = Smooth(t / reach); }
        else if (t < reach + press) { doorPhase = "press"; doorHand = 1; leverAngle = 40 * Smooth((t - reach) / press); }
        else
        {
            double u = (t - reach - press) / push;
            doorAngle = open * Smooth(u);
            double z = DoorAnchor.Z + step * Smooth(Math.Min(1, u * 1.6));
            speed = targetSpeed = Math.Max(0, (z - testPosition!.Value.Z) / Math.Max(delta, 1e-3));
            testPosition = new Vector3(DoorAnchor.X, 0, (float)z);
            leverAngle = 40 * (1 - Smooth((doorAngle - letGo * 0.6) / (letGo * 0.4)));
            if (doorAngle < letGo) { doorPhase = "push"; doorHand = 1; }
            else { doorPhase = u >= 1 ? "open" : "let go"; doorHand = Math.Max(0, doorHand - delta / release); }
            if (u >= 1) speed = targetSpeed = 0;
        }
        doorLeaf.Rotation = new Vector3(0, Mathf.DegToRad((float)doorAngle), 0);  // swings away, towards +z
        doorLever!.Rotation = new Vector3(0, 0, -Mathf.DegToRad((float)leverAngle));  // pressed down
    }

    void UpdateHands(double delta, string state)
    {
        if (handIk == null) return;
        UpdateDoor(delta);
        UpdateAction(delta);
        bool upright = state == "upright";
        foreach (var (kind, node) in weaponNodes) node.Visible = kind == weapon;
        double wantWeapon = weapon != "none" && upright ? 1 : 0;
        weaponWeight = Approach(weaponWeight, wantWeapon, delta / 0.25);
        bool gun = weapon is "pistol" or "rifle";
        aimAmount = Approach(aimAmount, raised && gun && upright ? 1 : 0, delta / Math.Max(raiseTime, 0.01));
        handIk.Enabled = handIkOn;
        if (CarHands()) { }  // the car's handles and wheel (LabCar.cs)
        else if (doorApproach != null && doorGrip != null)
        {
            handIk.Grip[0] = null; handIk.Weight[0] = 0;
            handIk.Grip[1] = doorGrip; handIk.Weight[1] = (float)doorHand;
        }
        else if (weapon != "none")
        {
            var w = weaponNodes[weapon];
            handIk.Grip[0] = ActionLeftGrip() ?? w.GetNodeOrNull<Node3D>("grip_left");
            handIk.Grip[1] = w.GetNodeOrNull<Node3D>("grip_right");
            handIk.Weight[0] = handIk.Weight[1] = (float)Smooth(weaponWeight);
        }
        else { handIk.Grip[0] = handIk.Grip[1] = null; handIk.Weight[0] = handIk.Weight[1] = 0; }
        handIk.Update();
    }

    string HandReadout() =>
        doorApproach != null ? $"door     {doorApproach}: {doorPhase} ({doorAngle:0} deg)" :
        weapon != "none" ? $"weapon   {weapon}, {(aimAmount > 0.99 ? "aimed" : aimAmount > 0.01 ? "raising" : "ready")}, pitch {aimPitch:0} deg" : "";

    /// <summary>Inside the skeleton's update, before the arm IK: share the aim pitch over the spine
    /// and head, place the weapon on this frame's body, then the hands' targets on its grips.</summary>
    void AimPose(Skeleton3D skel)
    {
        if (handIk == null) return;
        if (weapon == "none" || !weaponNodes.TryGetValue(weapon, out var node)) { handIk.PlaceTargets(); return; }
        var xf = skel.GlobalTransform;
        double a = Smooth(aimAmount);
        var charBasis = actor.GlobalBasis.Orthonormalized();
        if (a > 0.001)
        {
            float pitch = Mathf.DegToRad((float)aimPitch) * (float)a;
            var left = (xf.Basis.Inverse() * (charBasis * Vector3.Right)).Normalized();
            var forward = (xf.Basis.Inverse() * (charBasis * Vector3.Back)).Normalized();
            foreach (var (bone, share) in new[] { ("Spine", 0.2f), ("Chest", 0.2f), ("UpperChest", 0.2f), ("Neck", 0.15f), ("Head", 0.25f) })
                Turn(skel, bone, left, -pitch * share);  // a turn about +x (the character's left) by -a looks up by a
            if (weapon == "rifle")
            {
                Turn(skel, "Head", forward, Mathf.DegToRad(14) * (float)a);  // cheek onto the stock (the right)
                Turn(skel, "Head", left, Mathf.DegToRad(8) * (float)a);      // and down to the sights
            }
        }
        ActionBody(skel, xf, charBasis);  // a swing's or stab's twist (LabActions.cs)
        int uc = skel.FindBone("UpperChest");
        if (!readyLocal.TryGetValue(weapon, out var local))
        {
            var ucRest = actor.GlobalTransform.AffineInverse() * xf * skel.GetBoneGlobalRest(uc);
            readyLocal[weapon] = local = ucRest.AffineInverse() * Hold(weapon, 0);
        }
        var hold = xf * skel.GetBoneGlobalPose(uc) * local;
        if (a > 0.001) hold = hold.InterpolateWith(AimedHold(skel, xf, charBasis), (float)a);
        hold = ActionHold(hold);  // a swing, stab, recoil or reload (LabActions.cs)
        node.GlobalTransform = hold;
        handIk.PlaceTargets();
    }

    /// <summary>Turn a bone about an axis given in the skeleton's space (its children follow).</summary>
    static void Turn(Skeleton3D skel, string name, Vector3 axis, float angle)
    {
        int b = skel.FindBone(name);
        var g = skel.GetBoneGlobalPose(b).Basis.Orthonormalized();
        var local = (g.Inverse() * axis).Normalized();
        skel.SetBonePoseRotation(b, skel.GetBonePoseRotation(b) * new Quaternion(local, angle));
    }

    /// <summary>The gun at eye level: its rear sight on the line from the eye along the aim.</summary>
    Transform3D AimedHold(Skeleton3D skel, Transform3D xf, Basis charBasis)
    {
        var head = xf * skel.GetBoneGlobalPose(skel.FindBone("Head"));
        var aim = (charBasis * (new Basis(Vector3.Right, -Mathf.DegToRad((float)aimPitch)) * Vector3.Back)).Normalized();
        var y = (Vector3.Up - aim * Vector3.Up.Dot(aim)).Normalized();
        var basis = new Basis(y.Cross(aim), y, aim);
        bool rifle = weapon == "rifle";
        var eye = head * (rifle ? RightEye : EyesMid);
        aimEye = eye;  // the eye as drawn this frame (Skeleton3D restores its poses after the modifiers)
        var sight = rifle ? RifleSight : PistolSight;
        float ahead = rifle ? 0.17f : 0.42f;  // eye relief (the eye over the stock's comb, the butt at the shoulder); the pistol out at arms' length
        return new Transform3D(basis, eye + aim * ahead - basis * sight);
    }

    /// <summary>How far the weapon's sight line passes from the eye it's aimed with, in metres.</summary>
    float SightError()
    {
        if (weapon is not ("pistol" or "rifle") || !weaponNodes.TryGetValue(weapon, out var node)) return -1;
        var eye = aimEye;
        var t = node.GlobalTransform;
        var from = t * (weapon == "rifle" ? RifleSight : PistolSight);
        var dir = t.Basis.Z.Normalized();
        var v = eye - from;
        return (v - dir * v.Dot(dir)).Length();
    }

    // ------------------------------------------------------------------ the hand test

    async Task RunHandTest(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        report.AppendLine("Hand IK test: each hand's wrist distance from where its grip puts it, in cm (mean / worst over 60 frames, once the hold has settled). - = that hand is free.");
        report.AppendLine();
        report.AppendLine("Aimed guns also give the sight error: how far the sight line passes from the aiming eye, in cm.");
        report.AppendLine();
        report.AppendLine("| case | left mean | left worst | right mean | right worst | sight worst |");
        report.AppendLine("|---|--:|--:|--:|--:|--:|");
        GD.Print(report.ToString().TrimEnd());
        move = false;
        follow = true;
        worldCam = false;
        camYaw = 35; camPitch = 12; camSize = 2.3; camFocusY = 1.1f;
        foreach (var kind in new[] { "pistol", "rifle", "knife", "bat" })
            foreach (bool walk in new[] { false, true })
                foreach (var (up, pitch) in kind is "pistol" or "rifle"
                    ? new[] { (false, 0.0), (true, -30.0), (true, 0.0), (true, 30.0) } : new[] { (false, 0.0) })
                {
                    weapon = kind;
                    aimPitch = pitch;
                    raised = up;
                    targetSpeed = speed = walk ? 1.37 : 0;
                    camYaw = 35; camPitch = 12;
                    await Frames(45);
                    string name = $"{kind}-{(walk ? "walk" : "stand")}-{(up ? $"aimed{pitch:0}" : "ready")}";
                    report.AppendLine(await HandRow(name, 60));
                    if (pitch == 0 || (up && pitch == 30 && !walk)) await Shot(System.IO.Path.Combine(dir, name + ".png"));
                    if (up && pitch == 0 && !walk)
                    {
                        camYaw = 270; camPitch = 4;  // from its right: the eye on the sights
                        await Frames(3);
                        await Shot(System.IO.Path.Combine(dir, name + "-side.png"));
                    }
                }
        // Weapon actions (LabActions.cs): each hand's error over the whole action, and a shot mid-way.
        foreach (var (kind, act, aimed, shotAt) in new[] { ("bat", "swing", false, 0.55), ("knife", "stab", false, 0.5), ("pistol", "fire", true, 0.12),
                     ("pistol", "reload", false, 0.47), ("rifle", "fire", true, 0.12), ("rifle", "reload", false, 0.47) })
        {
            weapon = kind;
            raised = aimed;
            aimPitch = 0;
            targetSpeed = speed = 0;
            camYaw = 35; camPitch = 12;
            await Frames(45);
            StartAction(act);
            var e = new List<float>[] { new(), new() };
            bool shot = false;
            while (action != null)
            {
                await Frames(1);
                for (int k = 0; k < 2; k++) { float v = handIk!.Error(k); if (v >= 0) e[k].Add(v); }
                if (!shot && actionP >= shotAt) { shot = true; await Shot(System.IO.Path.Combine(dir, $"{kind}-{act}.png")); }
            }
            string F(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture);
            string Cells(List<float> l) => l.Count == 0 ? "- | -" : $"{F(Average(l))} | {F(Max(l))}";
            string line = $"| {kind}-{act} | {Cells(e[0])} | {Cells(e[1])} | |";
            report.AppendLine(line);
            GD.Print(line);
        }
        raised = false;
        weapon = "none";
        camYaw = 300; camPitch = 15; camSize = 3; camFocusY = 1.0f;
        foreach (var approach in new[] { "quiet", "fast" })
        {
            StartDoor(approach);
            double reach = approach == "quiet" ? 0.9 : 0.3;
            await Seconds(reach + 0.05);
            // While the hand is on the lever: the press and the push until it lets go.
            var errors = new List<float>();
            bool shot = false;
            while (doorPhase is "press" or "push")
            {
                await Frames(1);
                float e = handIk!.Error(1);
                if (e >= 0) errors.Add(e);
                if (!shot && doorPhase == "push" && doorAngle > 15) { shot = true; await Shot(System.IO.Path.Combine(dir, $"door-{approach}.png")); }
            }
            string F(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture);
            string line = errors.Count == 0 ? $"| door-{approach} | - | - | (no frames) | | |" : $"| door-{approach} | - | - | {F(Average(errors))} | {F(Max(errors))} | |";
            report.AppendLine(line);
            GD.Print(line);
            await Seconds(1.0);
            await Shot(System.IO.Path.Combine(dir, $"door-{approach}-open.png"));
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "handtest.md"), report.ToString());
        GD.Print("handtest: done");
        GetTree().Quit();
    }

    async Task<string> HandRow(string name, int frames)
    {
        var e = new List<float>[] { new(), new() };
        double sight = -1;
        for (int i = 0; i < frames; i++)
        {
            await Frames(1);
            for (int k = 0; k < 2; k++) { float v = handIk!.Error(k); if (v >= 0) e[k].Add(v); }
            if (raised && aimAmount > 0.99) sight = Math.Max(sight, SightError());
        }
        string F(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture);
        string Cells(List<float> l) => l.Count == 0 ? "- | -" : $"{F(Average(l))} | {F(Max(l))}";
        string line = $"| {name} | {Cells(e[0])} | {Cells(e[1])} | {(sight < 0 ? "" : F(sight))} |";
        GD.Print(line);
        return line;
    }

    static double Average(List<float> l) { double s = 0; foreach (var v in l) s += v; return s / l.Count; }
    static double Max(List<float> l) { double m = 0; foreach (var v in l) m = Math.Max(m, v); return m; }

    async Task Seconds(double s)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(s * 1000);
        while (Time.GetTicksMsec() < end) await Frames(1);
    }
}
