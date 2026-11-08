// The character lab, part seven: a car to get into and out of (docs/characters.md section 5,
// interactions and anchors). A mock-up built from boxes to real proportions (a 4.2 m compact,
// left-hand drive: the driver sits on the car's left, +x), with a driver's door hinged at its front,
// a seat, a steering wheel and pedals. Getting in is one action with progress 0..1 (5 s), the way
// the sim would run it; getting out plays the same choreography backwards (its own walking path).
//
//   reach and open   left hand on the outer handle, the door pulled to 25 deg while stepping back;
//                    it swings on to 42
//   to the seat      walk round the door into the opening, turn your back to the seat
//   sit              hips down and back onto the seat's edge (the body blends into the retargeted
//                    ual_driving clip); feet stay planted outside
//   swivel           hips slide onto the seat and turn to face forward; the legs swing in over the
//                    sill (the right first) to the pedals; the right hand takes the wheel
//   close            lean out, the left hand takes the inner pull, pulls the door shut, then the wheel
// Seated: the point between the hip joints on the seat's H-point (the hip joint, as car designers
// place it), the back reclined against the seatback; hands on the wheel at 9 and 3 (the wheel turns
// with the steering and the hands with it); the right foot on the accelerator or the brake (pressed
// by the inputs), the left on the footrest.
// How: a SeatPose modifier first in the skeleton's stack (hips to the seat, recline, a lean to
// reach the door), the legs' own TwoBoneIK3D and a PedalAlign (feet tilted to the pedal), then the
// existing hand IK with grips on the handles and the wheel. The foot IK (ground) is off meanwhile.
//
//   --car=in|out|seated   start with a car: getting in, getting out, or sitting in it
//   --carat=P             hold the action at progress P (0..1) for a screenshot
//   --steer=DEG --throttle=0..1 --brake=0..1 --roof (show the roof: hidden by default to see in)
//   --cartest=DIR         get in, sit (steer, brake), get out: hands, feet, hips and back against
//                         their targets, and screenshots

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace LoonieRoo.Game.Characters;

public partial class Lab
{
    // The car's frame is the world's (the car stands at the origin): +z its front, +x its left.
    static readonly Vector3 HPoint = new(0.37f, 0.55f, -0.10f);       // the driver's hip joint, seated
    const float FloorY = 0.30f, SillY = 0.40f, SideX = 0.88f, DoorLength = 1.15f, DoorOpen = 42;  // open enough to get in, near enough to reach back and shut
    static readonly Vector3 CarDoorHinge = new(SideX, 0, 0.72f);
    static readonly Vector3 WheelCentre = new(0.37f, 0.96f, 0.28f);
    const float WheelRadius = 0.19f, WheelTilt = 25, Recline = 18;     // seatback and wheel, degrees from vertical
    // Pedals: where the heel rests on the floor and the foot's pitch (toes up), released and pressed.
    static readonly (Vector3 heel, float up, float down) Accel = (new(0.24f, FloorY, 0.50f), 42, 28),
        Brake = (new(0.38f, FloorY + 0.03f, 0.47f), 52, 38), FootRest = (new(0.55f, FloorY, 0.44f), 34, 34);

    Node3D? car, carDoor, carWheelSpin, carWheelHands, carRoof;
    Node3D? gripOuter, gripInner, gripWheelL, gripWheelR;
    readonly MeshInstance3D?[] pedalPads = new MeshInstance3D?[3];
    SeatRig? seat;
    double carP;          // the action's progress: 0 standing outside, door shut; 1 seated, door shut
    int carDir;           // +1 getting in, -1 getting out, 0 still
    double carDuration = 5, carHoldAt = -1;
    bool carActive, carRoofShown;
    double steer, throttle, brakeIn, rightOnBrake;
    string carPhase = "";
    Vector3 carLastRoot;

    // ------------------------------------------------------------------ the rig: seat, legs, feet

    sealed class SeatRig
    {
        public CallbackModifier Pose = null!;
        public TwoBoneIK3D Legs = null!;
        public CallbackModifier Align = null!;
        public readonly Node3D[] Target = new Node3D[2], Pole = new Node3D[2];
        public readonly int[] Foot = new int[2], Thigh = new int[2];
        public int Hips;
        public float Sit, LegW, Lean, ReclineW;   // weights this frame
        public Vector3 SeatAt;                     // world: where the hip joints go
        public float Yaw;                          // the body's facing on the seat (car frame, deg)
        public readonly float[] Pitch = new float[2];  // each foot's toes-up pitch (deg)
        public readonly Vector3[] FinalAnkle = new Vector3[2];
        public Vector3 FinalHipMid, FinalChest;
        public Basis FinalHips;
    }

    void SetupCar(Dictionary<string, string> args)
    {
        if (args.ContainsKey("roof")) carRoofShown = true;
        if (args.TryGetValue("steer", out var s)) steer = Num(s);
        if (args.TryGetValue("throttle", out var t)) throttle = Num(t);
        if (args.TryGetValue("brake", out var b)) brakeIn = Num(b);
        if (args.TryGetValue("car", out var mode))
        {
            if (mode == "seated") { StartCar(1, 0); }
            else StartCar(mode == "out" ? 1 : 0, mode == "out" ? -1 : 1);
        }
        if (args.TryGetValue("carat", out var at)) { carHoldAt = Num(at); carP = carHoldAt; carDir = 0; }
        if (args.TryGetValue("cartest", out var dir)) _ = RunCarTest(dir);
    }

    void BuildCarTab(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddChild(Btn("Get in", () => StartCar(carActive ? carP : 0, 1)));
        row.AddChild(Btn("Get out", () => StartCar(carActive ? carP : 1, -1)));
        row.AddChild(Btn("Sit in it", () => StartCar(1, 0)));
        row.AddChild(Btn("No car", EndCar));
        box.AddChild(row);
        Slider(box, "Action time s", 1.5, 12, 0.1, carDuration, v => carDuration = v);
        Slider(box, "Scrub (holds)", 0, 1, 0.005, 0, v => { if (!carActive) StartCar(v, 0); carHoldAt = v; carP = v; carDir = 0; });
        Slider(box, "Steer °", -90, 90, 1, steer, v => steer = v);
        Slider(box, "Throttle", 0, 1, 0.01, throttle, v => throttle = v);
        Slider(box, "Brake", 0, 1, 0.01, brakeIn, v => brakeIn = v);
        var roof = new CheckBox { Text = "Roof", ButtonPressed = carRoofShown };
        roof.Toggled += on => { carRoofShown = on; if (carRoof != null) carRoof.Visible = on; };
        box.AddChild(roof);
        box.AddChild(new Label { Text = "Left-hand drive; the roof is hidden to see in.\nScrub holds the action at a progress.", Modulate = new Color(1, 1, 1, 0.6f) });
    }

    void StartCar(double p, int dir)
    {
        if (car == null) BuildCar();
        EndDoor();
        weapon = "none";
        if (weaponPick != null) weaponPick.Selected = 0;
        pose = Pose.Stand;
        targetCrouch = crouch = 0;
        move = false;
        carActive = true;
        carP = p;
        carDir = dir;
        carHoldAt = -1;
        carLastRoot = CarRoot(p, dir >= 0 ? 1 : -1).pos;
    }

    void EndCar()
    {
        carActive = false;
        carDir = 0;
        car?.QueueFree();
        car = null;
        gripOuter = gripInner = gripWheelL = gripWheelR = null;
        testPosition = null;
        if (seat != null) seat.Sit = seat.LegW = seat.Lean = seat.ReclineW = 0;
        PlayLibrary(null);
        speed = targetSpeed = 0;
    }

    // ------------------------------------------------------------------ the car (boxes)

    void BuildCar()
    {
        car = new Node3D { Name = "Car" };
        AddChild(car);
        var paint = new Color(0.32f, 0.36f, 0.33f);
        var trim = new Color(0.12f, 0.12f, 0.13f);
        var seatC = new Color(0.22f, 0.2f, 0.18f);
        void Box(Node3D parent, Vector3 size, Vector3 at, Color c, Basis? b = null) =>
            parent.AddChild(Part(new BoxMesh { Size = size }, at, c, b));
        // Body: floor pan, the front (bonnet and engine bay), the rear, the far (passenger) side.
        Box(car, new Vector3(1.76f, 0.10f, 4.2f), new Vector3(0, FloorY - 0.05f, 0), trim);
        Box(car, new Vector3(1.76f, 0.62f, 1.38f), new Vector3(0, 0.62f, 1.41f), paint);           // front, to the A-pillar
        Box(car, new Vector3(1.76f, 0.70f, 1.62f), new Vector3(0, 0.62f, -1.29f), paint);          // rear, from the B-pillar
        Box(car, new Vector3(0.06f, 0.62f, 1.15f), new Vector3(-SideX, 0.68f, 0.14f), paint);      // passenger door
        Box(car, new Vector3(0.10f, SillY - 0.22f, 1.15f), new Vector3(SideX - 0.02f, (SillY + 0.22f) / 2, 0.14f), paint);  // driver's sill
        foreach (var (x, z) in new[] { (0.78f, 1.35f), (-0.78f, 1.35f), (0.78f, -1.35f), (-0.78f, -1.35f) })
            car.AddChild(Part(new CylinderMesh { TopRadius = 0.31f, BottomRadius = 0.31f, Height = 0.2f }, new Vector3(x, 0.31f, z), trim, new Basis(Vector3.Forward, Mathf.Pi / 2)));
        // Dash and the inside of the front.
        Box(car, new Vector3(1.6f, 0.30f, 0.30f), new Vector3(0, 0.98f, 0.78f), trim);
        // Pillars and the roof (the roof hides, to see in).
        carRoof = new Node3D { Name = "Roof", Visible = carRoofShown };
        car.AddChild(carRoof);
        Box(carRoof, new Vector3(1.6f, 0.06f, 1.75f), new Vector3(0, 1.43f, -0.02f), paint);
        foreach (float x in new[] { SideX - 0.04f, -SideX + 0.04f })
        {
            Box(carRoof, new Vector3(0.06f, 0.62f, 0.07f), new Vector3(x, 1.18f, 0.78f), paint, new Basis(Vector3.Right, Mathf.DegToRad(-40)));  // A
            Box(carRoof, new Vector3(0.06f, 0.5f, 0.10f), new Vector3(x, 1.18f, -0.45f), paint);                                              // B
        }
        // The driver's seat: cushion (its top 9 cm under the H-point) and the seatback, reclined.
        var cushionTop = HPoint.Y - 0.09f;
        Box(car, new Vector3(0.50f, cushionTop - FloorY, 0.52f), new Vector3(HPoint.X, (cushionTop + FloorY) / 2, HPoint.Z + 0.06f), seatC);
        var back = new Node3D { Position = new Vector3(HPoint.X, cushionTop, HPoint.Z - 0.18f), Rotation = new Vector3(Mathf.DegToRad(-Recline), 0, 0) };
        car.AddChild(back);
        Box(back, new Vector3(0.50f, 0.62f, 0.10f), new Vector3(0, 0.33f, -0.05f), seatC);
        Box(back, new Vector3(0.28f, 0.18f, 0.10f), new Vector3(0, 0.78f, -0.05f), seatC);  // headrest
        // The passenger seat, for the look.
        Box(car, new Vector3(0.50f, cushionTop - FloorY, 0.52f), new Vector3(-HPoint.X, (cushionTop + FloorY) / 2, HPoint.Z + 0.06f), seatC);
        // Steering wheel: column, rim and a spoke, tilted with its top towards the driver.
        var wheel = new Node3D { Position = WheelCentre, Basis = new Basis(Vector3.Right, Mathf.DegToRad(-WheelTilt)) };
        car.AddChild(wheel);
        Box(wheel, new Vector3(0.05f, 0.05f, 0.35f), new Vector3(0, 0, 0.19f), trim);
        carWheelSpin = new Node3D { Name = "WheelSpin" };
        wheel.AddChild(carWheelSpin);
        carWheelSpin.AddChild(Part(new TorusMesh { InnerRadius = WheelRadius - 0.017f, OuterRadius = WheelRadius + 0.017f, Rings = 24, RingSegments = 8 },
            Vector3.Zero, trim, new Basis(Vector3.Right, Mathf.Pi / 2)));
        Box(carWheelSpin, new Vector3(0.36f, 0.04f, 0.02f), Vector3.Zero, trim);
        Box(carWheelSpin, new Vector3(0.04f, 0.17f, 0.02f), new Vector3(0, -0.09f, 0), trim);
        // Grips at 9 and 3: the palm towards the wheel's centre, the fingers wrapping forward.
        gripWheelL = GripAt("grip_wheel_left", new Vector3(WheelRadius + 0.012f, 0.02f, -0.01f), Vector3.Left, new Vector3(0, -0.2f, 1));
        gripWheelR = GripAt("grip_wheel_right", new Vector3(-WheelRadius - 0.012f, 0.02f, -0.01f), Vector3.Right, new Vector3(0, -0.2f, 1));
        carWheelHands = new Node3D { Name = "WheelHands" };
        wheel.AddChild(carWheelHands);
        carWheelHands.AddChild(gripWheelL);
        carWheelHands.AddChild(gripWheelR);
        // Pedals (pads drawn where the ball of the foot lands; see UpdatePedals).
        for (int i = 0; i < 3; i++)
        {
            pedalPads[i] = Part(new BoxMesh { Size = new Vector3(i == 1 ? 0.11f : 0.07f, 0.015f, i == 2 ? 0.20f : 0.10f) }, Vector3.Zero, i == 2 ? trim : new Color(0.3f, 0.3f, 0.32f));
            car.AddChild(pedalPads[i]);
        }
        // The driver's door, on its hinge at the front: lower panel, window frame, handles.
        carDoor = new Node3D { Name = "DriverDoor", Position = CarDoorHinge };
        car.AddChild(carDoor);
        Box(carDoor, new Vector3(0.06f, 0.58f, DoorLength), new Vector3(0, SillY + 0.29f, -DoorLength / 2), paint);
        Box(carDoor, new Vector3(0.04f, 0.04f, DoorLength - 0.1f), new Vector3(-0.01f, 1.38f, -DoorLength / 2 - 0.05f), paint);  // window top
        Box(carDoor, new Vector3(0.04f, 0.42f, 0.05f), new Vector3(-0.01f, 1.17f, -DoorLength + 0.03f), paint);                // window rear
        Box(carDoor, new Vector3(0.05f, 0.03f, 0.16f), new Vector3(0.045f, 0.88f, -0.98f), trim);    // outer handle
        Box(carDoor, new Vector3(0.10f, 0.04f, 0.30f), new Vector3(-0.07f, 0.74f, -0.72f), trim);    // armrest and inner pull
        // Outer handle: fingers in under it from below, palm up. Inner pull: palm on it, towards the door.
        gripOuter = GripAt("grip_door_outer", new Vector3(0.06f, 0.855f, -0.98f), Vector3.Up, Vector3.Left);
        gripInner = GripAt("grip_door_inner", new Vector3(-0.15f, 0.78f, -0.78f), Vector3.Right, new Vector3(0, -0.3f, 1));
        carDoor.AddChild(gripOuter);
        carDoor.AddChild(gripInner);
        if (seat == null) BuildSeatRig();
    }

    void BuildSeatRig()
    {
        var skel = Find<Skeleton3D>(actor)!;
        seat = new SeatRig { Hips = skel.FindBone("Hips") };
        seat.Pose = new CallbackModifier { Name = "SeatPose", Run = SeatPose };
        seat.Legs = new TwoBoneIK3D { Name = "SeatLegIK", Active = true, Influence = 0 };
        seat.Legs.SetSettingCount(2);
        seat.Align = new CallbackModifier { Name = "PedalAlign", Run = PedalAlign };
        for (int i = 0; i < 2; i++)
        {
            string s = i == 0 ? "Left" : "Right";
            seat.Target[i] = new Node3D { Name = $"SeatFoot{s}" };
            seat.Pole[i] = new Node3D { Name = $"SeatKnee{s}" };
            AddChild(seat.Target[i]);
            AddChild(seat.Pole[i]);
            seat.Legs.SetRootBoneName(i, s + "UpperLeg");
            seat.Legs.SetMiddleBoneName(i, s + "LowerLeg");
            seat.Legs.SetEndBoneName(i, s + "Foot");
            seat.Foot[i] = skel.FindBone(s + "Foot");
            seat.Thigh[i] = skel.FindBone(s + "UpperLeg");
        }
        // First in the stack, before the foot IK, the aim and the arm IK: the arms reach from the seated body.
        skel.AddChild(seat.Pose);
        skel.AddChild(seat.Legs);
        skel.AddChild(seat.Align);
        skel.MoveChild(seat.Pose, 0);
        skel.MoveChild(seat.Legs, 1);
        skel.MoveChild(seat.Align, 2);
        for (int i = 0; i < 2; i++)
        {
            seat.Legs.SetTargetNode(i, seat.Legs.GetPathTo(seat.Target[i]));
            seat.Legs.SetPoleNode(i, seat.Legs.GetPathTo(seat.Pole[i]));
        }
    }

    // ------------------------------------------------------------------ the choreography

    // Keyframes over progress, eased between keys (each key a stop: smoothstep).
    static double Keys(double p, params (double p, double v)[] keys)
    {
        if (p <= keys[0].p) return keys[0].v;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].p)
            {
                var (p0, v0) = keys[i - 1];
                var (p1, v1) = keys[i];
                return v0 + (v1 - v0) * Smooth((p - p0) / Math.Max(p1 - p0, 1e-6));
            }
        return keys[^1].v;
    }

    static Vector3 Keys3(double p, params (double p, Vector3 v)[] keys)
    {
        if (p <= keys[0].p) return keys[0].v;
        for (int i = 1; i < keys.Length; i++)
            if (p <= keys[i].p)
            {
                var (p0, v0) = keys[i - 1];
                var (p1, v1) = keys[i];
                return v0.Lerp(v1, (float)Smooth((p - p0) / Math.Max(p1 - p0, 1e-6)));
            }
        return keys[^1].v;
    }

    static readonly Vector3 AtHandle = new(1.22f, 0, -0.40f), BackedOff = new(1.68f, 0, -0.50f), Round = new(1.45f, 0, -0.45f), BySeat = new(1.18f, 0, -0.05f);
    static readonly Vector3 SeatEdge = new(0.66f, HPoint.Y, -0.08f);

    /// <summary>Where the character's root stands and which way it faces (car frame, yaw: 0 = +z, 90 = +x).</summary>
    static (Vector3 pos, double yaw) CarRoot(double p, int dir)
    {
        // Pulling the door: step back and out with the handle's arc, to just past the door's rear edge,
        // turned to the handle; then round to the seat on the inside of the open door.
        var pos = Keys3(p, (0, AtHandle), (0.08, AtHandle), (0.20, BackedOff), (0.28, BackedOff), (0.34, Round), (0.42, BySeat), (0.47, BySeat),
            (0.60, new Vector3(SeatEdge.X, 0, SeatEdge.Z)), (0.80, new Vector3(HPoint.X, 0, HPoint.Z)));
        // Getting in walks forward from the door to the seat; getting out walks forward from the seat to the door.
        double yaw = dir >= 0
            ? Keys(p, (0, -90), (0.08, -90), (0.20, -45), (0.28, -45), (0.32, -75), (0.37, -35), (0.42, -30), (0.47, 90), (0.60, 90), (0.80, 0))
            : Keys(p, (0, -90), (0.08, -90), (0.20, -45), (0.28, -45), (0.32, 102), (0.37, 146), (0.42, 146), (0.47, 90), (0.60, 90), (0.80, 0));
        return (pos, yaw);
    }

    static string PhaseAt(double p) =>
        p < 0.08 ? "reach the handle" : p < 0.25 ? "open the door" : p < 0.45 ? "to the seat" : p < 0.60 ? "sit" :
        p < 0.80 ? "swivel, legs in" : p < 0.95 ? "close the door" : p < 1 ? "hands to the wheel" : "seated";

    // The same stretches of progress, named as getting out goes through them (backwards).
    static string PhaseOut(double p) =>
        p > 0.95 ? "hands off the wheel" : p > 0.80 ? "open the door" : p > 0.60 ? "swivel, legs out" : p > 0.45 ? "stand up" :
        p > 0.25 ? "out round the door" : p > 0.08 ? "shut the door" : p > 0 ? "let go" : "standing";

    void UpdateCar(double delta)
    {
        if (!carActive || car == null || seat == null) return;
        if (carHoldAt >= 0) carP = carHoldAt;
        else if (carDir != 0)
        {
            carP = Math.Clamp(carP + carDir * delta / carDuration, 0, 1);
            if (carP is 0 or 1) carDir = 0;
        }
        double p = carP;
        int dir = carDir == 0 ? (p > 0.5 ? 1 : -1) : carDir;
        carPhase = carDir < 0 ? PhaseOut(p) : PhaseAt(p);

        // The door, the wheel, the pedals.
        double door = Keys(p, (0, 0), (0.08, 0), (0.20, 25), (0.30, DoorOpen), (0.84, DoorOpen), (0.92, 0));  // pulled to 25, swings on
        carDoor!.Rotation = new Vector3(0, -Mathf.DegToRad((float)door), 0);  // swings out, to +x (a turn about +y by +a would swing it in)
        // The rim turns by the steering; the hands slide round it (push-pull), so they turn by only half of it
        // and the far hand stays in reach.
        float wheelOn = (float)Smooth((p - 0.95) / 0.05);
        carWheelSpin!.Rotation = new Vector3(0, 0, Mathf.DegToRad((float)steer * wheelOn));
        carWheelHands!.Rotation = new Vector3(0, 0, Mathf.DegToRad((float)steer * 0.5f * wheelOn));

        // The root: where it stands and faces; its speed drives the walk while standing.
        var (pos, yaw) = CarRoot(p, dir);
        double moved = pos.DistanceTo(carLastRoot) / Math.Max(delta, 1e-3);
        carLastRoot = pos;
        bool standing = p < 0.45;
        speed = targetSpeed = standing && carHoldAt < 0 ? Math.Min(moved, 2) : 0;
        testPosition = pos;
        heading = Math.PI / 2 - Mathf.DegToRad((float)yaw);

        // Weights: sit (hips to the seat), legs (feet to their targets), body (the driving clip), lean.
        seat.Sit = (float)Keys(p, (0.45, 0), (0.60, 1));
        seat.LegW = (float)Keys(p, (0.45, 0), (0.52, 1));
        seat.ReclineW = (float)Keys(p, (0.60, 0), (0.80, 1));
        seat.Lean = (float)Keys(p, (0.78, 0), (0.85, 1), (0.92, 1), (0.96, 0));
        seat.SeatAt = Keys3(p, (0.45, SeatEdge), (0.60, SeatEdge), (0.80, HPoint));
        seat.Yaw = (float)yaw;
        LibraryBlend("ual_driving", Keys(p, (0.45, 0), (0.60, 1)));

        // Feet: planted outside by the seat, then over the sill to the pedals (the right first).
        rightOnBrake = Approach(rightOnBrake, brakeIn > 0.01 ? 1 : 0, delta / 0.25);
        var planted = new[] { BySeat + new Vector3(0.03f, FootIK.AnkleHeight, -0.11f), BySeat + new Vector3(0.03f, FootIK.AnkleHeight, 0.11f) };
        var (rTarget, rPitch) = OnPedal(rightOnBrake < 0.5 ? Accel : Brake, rightOnBrake < 0.5 ? throttle : brakeIn);
        if (rightOnBrake is > 0 and < 1)  // moving between the pedals: a small lift on the way
        {
            var (a, pa) = OnPedal(Accel, throttle);
            var (b, pb) = OnPedal(Brake, brakeIn);
            float u = (float)Smooth(rightOnBrake);
            rTarget = a.Lerp(b, u) + Vector3.Up * 0.05f * Mathf.Sin(Mathf.Pi * u);
            rPitch = Mathf.Lerp(pa, pb, u);
        }
        var (lTarget, lPitch) = OnPedal(FootRest, 0);
        SwingFoot(1, planted[1], rTarget, rPitch, p, 0.62, 0.74);
        SwingFoot(0, planted[0], lTarget, lPitch, p, 0.66, 0.80);
        // Knees: ahead and up of the hips (outward a little).
        var bodyBasis = new Basis(Vector3.Up, Mathf.DegToRad(seat.Yaw));
        for (int i = 0; i < 2; i++)
            seat.Pole[i].GlobalPosition = seat.SeatAt + bodyBasis * new Vector3(i == 0 ? 0.25f : -0.25f, 0.6f, 1.0f);
        seat.Legs.Influence = seat.LegW;
        seat.Pose.Active = seat.Legs.Active = seat.Align.Active = seat.Sit > 0 || seat.LegW > 0;
        UpdatePedals();
    }

    /// <summary>A foot on a pedal: the ankle (world) and the foot's toes-up pitch, the pedal pressed by amount.</summary>
    static (Vector3 ankle, float pitch) OnPedal((Vector3 heel, float up, float down) pedal, double amount)
    {
        float a = Mathf.Lerp(pedal.up, pedal.down, (float)amount);
        float r = Mathf.DegToRad(a);
        // The ankle turns about the heel: 9 cm above it and 5 cm ahead, flat.
        var offset = new Vector3(0, 0.09f * Mathf.Cos(r) + 0.05f * Mathf.Sin(r), -0.09f * Mathf.Sin(r) + 0.05f * Mathf.Cos(r));
        return (pedal.heel + offset, a);
    }

    void SwingFoot(int i, Vector3 from, Vector3 to, float pitch, double p, double start, double end)
    {
        double u = Smooth((p - start) / (end - start));
        var at = from.Lerp(to, (float)u) + Vector3.Up * 0.42f * Mathf.Sin(Mathf.Pi * (float)u);  // over the sill
        seat!.Target[i].GlobalPosition = at;
        seat.Pitch[i] = (float)(pitch * u);
    }

    void UpdatePedals()
    {
        for (int i = 0; i < 3; i++)
        {
            var pedal = i == 0 ? Accel : i == 1 ? Brake : FootRest;
            double amount = i == 0 ? (rightOnBrake < 0.5 ? throttle : 0) : i == 1 ? (rightOnBrake >= 0.5 ? brakeIn : 0) : 0;
            float a = Mathf.DegToRad(Mathf.Lerp(pedal.up, pedal.down, (float)amount));
            var along = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
            float mid = i == 2 ? 0.10f : 0.16f;  // the ball of the foot (the footrest is longer)
            pedalPads[i]!.Position = pedal.heel + along * mid - new Basis(Vector3.Right, -a) * new Vector3(0, 0.012f, 0);
            pedalPads[i]!.Basis = new Basis(Vector3.Right, -a);
        }
    }

    /// <summary>The hands' grips and weights while getting in, seated, or getting out; true when the car has them.</summary>
    bool CarHands()
    {
        if (!carActive || handIk == null || car == null) return false;
        double p = carP;
        double outer = Keys(p, (0, 0), (0.06, 1), (0.17, 1), (0.22, 0));  // lets go at ~22 deg
        double inner = Keys(p, (0.80, 0), (0.85, 1), (0.92, 1), (0.95, 0));
        double wheelL = Keys(p, (0.95, 0), (1.0, 1));
        double wheelR = Keys(p, (0.66, 0), (0.74, 1));
        (handIk.Grip[0], handIk.Weight[0]) = p < 0.4 ? (gripOuter, (float)outer) : p < 0.945 ? (gripInner, (float)inner) : (gripWheelL, (float)wheelL);
        (handIk.Grip[1], handIk.Weight[1]) = (gripWheelR, (float)wheelR);
        return true;
    }

    // ------------------------------------------------------------------ inside the skeleton's update

    /// <summary>First modifier: the hips onto the seat (the point between the hip joints on SeatAt),
    /// facing the seat's way and reclined; a lean out to the left for the door.</summary>
    void SeatPose(Skeleton3D skel)
    {
        var r = seat!;
        var xf = skel.GlobalTransform;
        var inv = xf.AffineInverse();
        if (r.Sit > 0.001f)
        {
            var hips = skel.GetBoneGlobalPose(r.Hips);
            var mid = (skel.GetBoneGlobalPose(r.Thigh[0]).Origin + skel.GetBoneGlobalPose(r.Thigh[1]).Origin) / 2;
            // The clip's pelvis turned to the seat's facing (the root may still be turning) and reclined.
            var rootYaw = new Basis(Vector3.Up, Mathf.DegToRad(r.Yaw));
            var charBasis = actor.GlobalBasis.Orthonormalized();
            var turn = inv.Basis * rootYaw * charBasis.Inverse() * xf.Basis;  // skeleton space: the root's facing -> the seat's
            var left = (inv.Basis * (rootYaw * Vector3.Right)).Normalized();
            var recline = new Basis(left, -Mathf.DegToRad(Recline + 6) * r.ReclineW);  // the clip's pelvis sits forward of the seatback's angle
            var basis = (recline * turn * hips.Basis).Orthonormalized();
            var offset = recline * turn * (hips.Origin - mid);  // the hips bone from the hip joints, turned as the pelvis is
            var want = new Transform3D(basis, inv * r.SeatAt + offset);
            var blended = new Transform3D(
                new Basis(hips.Basis.GetRotationQuaternion().Slerp(basis.GetRotationQuaternion(), r.Sit)),
                hips.Origin.Lerp(want.Origin, r.Sit));
            skel.SetBoneGlobalPose(r.Hips, blended);
        }
        if (r.Sit > 0.001f)
        {
            // Eyes on the road: the recline tips the head back; the neck and head take it out again.
            int head = skel.FindBone("Head");
            var restB = skel.GetBoneGlobalRest(head).Basis.Orthonormalized();
            var fwdSkel = (inv.Basis * (actor.GlobalBasis.Orthonormalized() * Vector3.Back)).Normalized();
            var local = restB.Inverse() * fwdSkel;  // the face's forward in the head bone's frame
            var now = (xf.Basis * skel.GetBoneGlobalPose(head).Basis.Orthonormalized() * local).Normalized();
            float pitch = Mathf.Asin(Math.Clamp(now.Y, -1, 1)) * r.Sit;  // up positive
            var left = (inv.Basis * (new Basis(Vector3.Up, Mathf.DegToRad(r.Yaw)) * Vector3.Right)).Normalized();
            Turn(skel, "Neck", left, pitch * 0.4f);  // a turn about the left axis by +a tips the face down by a
            Turn(skel, "Head", left, pitch * 0.6f);
        }
        if (r.Lean > 0.001f)
        {
            var forward = (inv.Basis * (new Basis(Vector3.Up, Mathf.DegToRad(r.Yaw)) * Vector3.Back)).Normalized();
            Turn(skel, "Spine", forward, -Mathf.DegToRad(12) * r.Lean);  // a turn about forward by -a tips the top to the left
            Turn(skel, "Chest", forward, -Mathf.DegToRad(12) * r.Lean);
        }
    }

    /// <summary>After the legs' IK: each foot pitched toes-up by its pedal's angle (flat outside), facing the body's way.</summary>
    void PedalAlign(Skeleton3D skel)
    {
        var r = seat!;
        var xf = skel.GlobalTransform;
        var inv = xf.AffineInverse();
        var yaw = new Basis(Vector3.Up, Mathf.DegToRad(r.Yaw));
        var charBasis = actor.GlobalBasis.Orthonormalized();
        for (int i = 0; i < 2; i++)
        {
            var pose = skel.GetBoneGlobalPose(r.Foot[i]);
            if (r.LegW > 0.001f)
            {
                // The rest foot in the character's space, turned to the body's facing, pitched up.
                var rest = charBasis.Inverse() * xf.Basis * skel.GetBoneGlobalRest(r.Foot[i]).Basis.Orthonormalized();
                var pitch = new Basis(yaw * Vector3.Right, -Mathf.DegToRad(r.Pitch[i]));
                var want = (inv.Basis * pitch * yaw * rest).Orthonormalized();
                var now = pose.Basis.Orthonormalized();
                var basis = new Basis(now.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), r.LegW));
                skel.SetBoneGlobalPose(r.Foot[i], new Transform3D(basis, pose.Origin));
            }
            r.FinalAnkle[i] = xf * pose.Origin;
        }
        r.FinalHipMid = xf * ((skel.GetBoneGlobalPose(r.Thigh[0]).Origin + skel.GetBoneGlobalPose(r.Thigh[1]).Origin) / 2);
        r.FinalChest = xf * skel.GetBoneGlobalPose(skel.FindBone("UpperChest")).Origin;
        r.FinalHips = (xf.Basis * skel.GetBoneGlobalPose(r.Hips).Basis).Orthonormalized();
    }

    string CarReadout()
    {
        if (!carActive || seat == null) return "";
        var s = $"car      {carPhase}, {carP:0.00}";
        if (carP >= 0.999)
        {
            var (g, back) = SeatErrors();
            s += $"\nseat     hips {g * 100:0.0} cm off, back gap {back * 100:0.0} cm";
        }
        return s;
    }

    /// <summary>Seated: how far the hip joints are from the H-point, and the gap between the upper back and the seatback (m).</summary>
    (float hips, float back) SeatErrors()
    {
        var r = seat!;
        float hips = r.FinalHipMid.DistanceTo(HPoint);
        // The seatback's front plane: through the cushion's rear top edge, tilted back by Recline.
        var n = new Basis(Vector3.Right, -Mathf.DegToRad(Recline)) * Vector3.Back;  // its normal, towards the driver
        var p0 = new Vector3(HPoint.X, HPoint.Y - 0.09f, HPoint.Z - 0.18f) - new Basis(Vector3.Right, -Mathf.DegToRad(Recline)) * new Vector3(0, 0, 0.0f);
        float chestToPlane = (r.FinalChest - p0).Dot(n);
        return (hips, chestToPlane - TorsoHalfDepth);
    }

    // The mannequin's back from the UpperChest bone (its torso's half depth there): art/scripts/build_test_mannequin.py.
    const float TorsoHalfDepth = 0.11f;

    // ------------------------------------------------------------------ the car test

    async Task RunCarTest(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        void Line(string l) { report.AppendLine(l); GD.Print(l); }
        Line("Car test: getting in, sitting (steering, braking), getting out. Distances in cm.");
        Line("");
        Line("Rows getting in and out are by progress (0 outside, 1 seated), named as getting in goes through it; getting out runs it backwards.");
        Line("");
        Line("Hands: wrist from where its grip puts it (mean / worst while the hand is on). Feet: ankle from its IK target. Hips: the point between the hip joints from the seat's H-point. Back: the upper back's gap to the seatback (negative = into it).");
        Line("");
        Line("| case | left hand | right hand | left foot | right foot | hips | back |");
        Line("|---|--:|--:|--:|--:|--:|--:|");
        worldCam = true;
        follow = false;
        camYaw = 70; camPitch = 30; camSize = 3.6; camFocusY = 0.7f;
        StartCar(0, 1);
        var shots = new[] { 0.06, 0.18, 0.40, 0.54, 0.70, 0.88 };
        int shot = 0;
        var byPhase = new SortedDictionary<string, List<float>[]>();
        while (carP < 1)
        {
            await Frames(1);
            PhaseErrors(byPhase);
            if (shot < shots.Length && carP >= shots[shot]) await Shot(System.IO.Path.Combine(dir, $"in-{shots[shot++] * 100:00}.png"));
        }
        foreach (var (ph, h) in byPhase) Line(Row("in: " + ph, h, null));
        await Frames(20);
        foreach (var (name, st, th, br) in new[] { ("seated", 0.0, 0.0, 0.0), ("seated, throttle 1", 0.0, 1.0, 0.0), ("seated, brake 1", 0.0, 0.0, 1.0), ("seated, steer -60", -60.0, 0.3, 0.0), ("seated, steer +60", 60.0, 0.3, 0.0) })
        {
            steer = st; throttle = th; brakeIn = br;
            await Seconds(0.5);
            var h = new[] { new List<float>(), new List<float>() };
            var f = new[] { new List<float>(), new List<float>() };
            var hip = new List<float>();
            var back = new List<float>();
            for (int i = 0; i < 30; i++)
            {
                await Frames(1);
                for (int k = 0; k < 2; k++)
                {
                    float e = handIk!.Error(k); if (e >= 0) h[k].Add(e);
                    f[k].Add(seat!.FinalAnkle[k].DistanceTo(seat.Target[k].GlobalPosition));
                }
                var (g, b) = SeatErrors();
                hip.Add(g); back.Add(b);
            }
            Line(Row(name, h, f, hip, back));
            if (name is "seated" or "seated, steer -60" or "seated, brake 1")
            {
                await Shot(System.IO.Path.Combine(dir, name.Replace(", ", "-").Replace(" ", "") + ".png"));
                if (name == "seated")
                {
                    camYaw = 90; camPitch = 4; camSize = 2.2; camFocusY = 0.75f;  // from the left, door shut: through the window
                    carRoof!.Visible = false;
                    await Frames(3);
                    await Shot(System.IO.Path.Combine(dir, "seated-side.png"));
                    carDoor!.Visible = false;
                    await Frames(3);
                    await Shot(System.IO.Path.Combine(dir, "seated-side-nodoor.png"));
                    carDoor.Visible = true;
                    camYaw = 70; camPitch = 30; camSize = 3.6; camFocusY = 0.7f;
                }
            }
        }
        steer = throttle = brakeIn = 0;
        StartCar(1, -1);
        shot = 0;
        var outShots = new[] { 0.88, 0.70, 0.54, 0.40, 0.18 };
        byPhase = new SortedDictionary<string, List<float>[]>();
        while (carP > 0)
        {
            await Frames(1);
            PhaseErrors(byPhase);
            if (shot < outShots.Length && carP <= outShots[shot]) await Shot(System.IO.Path.Combine(dir, $"out-{outShots[shot++] * 100:00}.png"));
        }
        foreach (var (ph, h) in byPhase) Line(Row("out: " + ph, h, null));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "cartest.md"), report.ToString());
        GD.Print("cartest: done");
        GetTree().Quit();
    }

    /// <summary>Each hand's error this frame (while on its grip), filed under the phase and the grip.</summary>
    void PhaseErrors(SortedDictionary<string, List<float>[]> into)
    {
        for (int k = 0; k < 2; k++)
        {
            float e = handIk!.Error(k);
            if (e < 0) continue;
            string key = $"{carP:0.0}0 {PhaseAt(carP)}";
            if (!into.TryGetValue(key, out var l)) into[key] = l = new[] { new List<float>(), new List<float>() };
            l[k].Add(e);
        }
    }

    static string Row(string name, List<float>[] hands, List<float>[]? feet, List<float>? hips = null, List<float>? back = null)
    {
        string F(double v) => (v * 100).ToString("0.0", CultureInfo.InvariantCulture);
        string C(List<float>? l) => l == null || l.Count == 0 ? "-" : $"{F(Average(l))} / {F(Max(l))}";
        string B(List<float>? l) => l == null || l.Count == 0 ? "-" : F(Average(l));
        return $"| {name} | {C(hands[0])} | {C(hands[1])} | {C(feet?[0])} | {C(feet?[1])} | {C(hips)} | {B(back)} |";
    }
}
