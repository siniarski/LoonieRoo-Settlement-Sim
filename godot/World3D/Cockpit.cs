// The car you are driving, from the driver's seat (2026-11-11): the cabin round you (art/scripts/build_cabins.py:
// the dashboard, seats, pillars, roof, the bonnet through the windscreen), the steering wheel turning as you
// steer, and the instruments on a panel in the binnacle (GaugeCluster.cs), reading the sim's car:
//
//   speedometer   the car's speed (SUB a tick, as km/h: 160 is 22.5)
//   fuel          the tank (Vehicles.FUEL_MAX is full); the FUEL lamp under 15%
//   temperature   its health as engine heat: a sound engine runs just under the middle, a wreck at H; cold when it
//                 isn't turning. The ENGINE lamp under half health
//   OIL, BATT     lit when the engine isn't turning (no fuel, or wrecked): the ignition is on and nothing drives them
//   BRAKE         lit standing still
//   LIGHTS        the headlights (Vehicle.lights), which also light the panel at night
//   P R N D       P standing, D forward, R backward, N rolling with a dead engine
//   tachometer    not in the sim: worked out here from the speed through three gears, so it climbs and drops as a
//                 car's does
//   odometer      not in the sim either: a mileage picked by the car's id, plus what you have driven it since you
//                 got in this session
//
// The cabin is put where the camera is (its eye point on the camera), turned to the car's heading eased between
// the sim's 32 headings, so it doesn't snap as the car turns. The car's outside (Cars.cs draws it for every other
// car) is here only as a shadow on the ground. Reads state only (rule 5): the eased heading, the wheel's turn, the
// engine speed and the trip are view state.

using System;
using Godot;
using LoonieRoo.Game.Characters;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Cockpit
{
    /// <summary>The sedan driver's eye in the car's own frame (m): ahead of its centre, over the ground, and to its
    /// right (so the driver's side, the left, is negative); the hub of the wheel; the middle of the instrument
    /// panel. build_cabins.py has the same numbers (EYE, HUB, PANEL; its y is this z, negated).</summary>
    static readonly Vector3 EYE = new(0.04f, 1.08f, -0.38f), HUB = new(0.54f, 0.845f, -0.38f), PANEL = new(0.72f, 0.92f, -0.38f);
    /// <summary>The pickup's cab is the same one, further forward and higher.</summary>
    static readonly Vector3 TRUCK = new(0.55f, 0.42f, 0);
    /// <summary>The panel's size (m: GaugeCluster's shape) and how far it leans back; how far the column leans.</summary>
    const float PANEL_W = 0.30f, PANEL_LEAN = 20, WHEEL_LEAN = 25;
    /// <summary>The wheel's turn held hard over (radians), and how fast it and the heading catch up (per second).</summary>
    const float LOCK = 1.5f, WHEEL_RATE = 5, HEADING_RATE = 9;

    readonly Node3D root = new() { Visible = false };
    readonly MeshInstance3D cabin = new(), wheel = new(), panel = new();
    readonly MeshInstance3D shadow = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly };
    readonly SubViewport viewport = new() { Size = new Vector2I(GaugeCluster.W, GaugeCluster.H), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
    readonly StandardMaterial3D glass;
    readonly ModelSet cabins, outsides;

    /// <summary>The instruments as they read now (the self-test).</summary>
    public readonly GaugeCluster Gauges = new();
    public bool Shown => root.Visible;

    string? carId;
    double yaw, steer, rpm, temp, trip;
    Vector2? last;

    public Cockpit(Node3D parent)
    {
        cabins = new ModelSet(parent, "res://assets/cars/cabins.glb", true, false);
        outsides = new ModelSet(parent, "res://assets/furniture/furniture.glb");
        wheel.Mesh = cabins.MeshOf("steeringwheel");
        viewport.AddChild(Gauges);
        root.AddChild(viewport);
        // The instruments' picture on a panel: it shows by daylight like anything else, and glows when it is lit.
        glass = new StandardMaterial3D { AlbedoTexture = viewport.GetTexture(), EmissionEnabled = true, EmissionTexture = viewport.GetTexture(), Roughness = 0.35f };
        panel.Mesh = new QuadMesh { Size = new Vector2(PANEL_W, PANEL_W * GaugeCluster.H / GaugeCluster.W) };
        panel.MaterialOverride = glass;
        // A quad faces +Z: turned to face back down the car, then leant back so its top is further from you.
        panel.Basis = new Basis(Vector3.Back, Mathf.DegToRad(-PANEL_LEAN)) * new Basis(Vector3.Up, -Mathf.Pi / 2);
        root.AddChild(cabin);
        root.AddChild(wheel);
        root.AddChild(panel);
        root.AddChild(shadow);
        parent.AddChild(root);
    }

    /// <summary>Where the driver's eye is in a car's own frame (m): ahead of its centre, over the ground, to its left.</summary>
    public static (float ahead, float up, float left) Seat(Vehicle v)
    {
        var e = EYE + (v.kind == "truck" ? TRUCK : Vector3.Zero);
        return (e.X, e.Y, -e.Z);
    }

    /// <summary>A car's heading as an angle (0 east, pi/2 south).</summary>
    public static double YawOf(Vehicle v) => v.heading / Vehicles.HEADINGS * Math.Tau;

    /// <summary>Follow the car you're in: the heading eased towards the sim's. Returns how far it turned this frame,
    /// for the view to turn with it (getting in, the turn that faces `viewYaw` forward); 0 when you're in no car.</summary>
    public double Track(Vehicle? car, double viewYaw, double delta, bool faceForward = true)
    {
        if (car == null) { carId = null; return 0; }
        double to = YawOf(car);
        if (carId != car.id)
        {
            carId = car.id;
            yaw = to;
            steer = rpm = trip = 0;
            temp = 0.1;
            last = null;
            return faceForward ? Mathf.AngleDifference((float)viewYaw, (float)to) : 0;
        }
        double before = yaw;
        yaw = Mathf.LerpAngle((float)yaw, (float)to, (float)Math.Min(1, delta * HEADING_RATE));
        return Mathf.AngleDifference((float)before, (float)yaw);
    }

    /// <summary>The eased heading of the car you're in (Track keeps it), and whether it is this car's.</summary>
    public double Yaw => yaw;
    public bool Following(Vehicle car) => carId == car.id;

    public void Update(SimState state, Vehicle? car, Vector3 eye, double alpha, double delta, bool show)
    {
        if (car == null || !show)
        {
            if (root.Visible) { root.Visible = false; viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; }
            return;
        }
        if (!root.Visible) { root.Visible = true; viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always; }
        bool truck = car.kind == "truck";
        var shift = truck ? TRUCK : Vector3.Zero;
        string outside = Cars.ModelOf(car);
        cabin.Mesh = cabins.MeshOf(truck ? "cabin_truck" : outside == "car_v1" ? "cabin_v1" : "cabin");
        shadow.Mesh = outsides.MeshOf(outside);
        // The cabin's eye point on the camera, the car's front (its +X) along the heading.
        var turn = new Basis(Vector3.Up, (float)-yaw);
        root.Transform = new Transform3D(turn, eye - turn * (EYE + shift));
        panel.Position = PANEL + shift;

        // The wheel: over as far as the key is held, the way the car turns (backwards, the car swings the other way but the wheel doesn't).
        double held = state.control?.dx ?? 0;
        steer += (held * LOCK - steer) * Math.Min(1, delta * WHEEL_RATE);
        wheel.Transform = new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(-WHEEL_LEAN)) * new Basis(Vector3.Right, (float)steer), HUB + shift);

        Read(state, car, alpha, delta);
        Gauges.QueueRedraw();
    }

    /// <summary>The instruments from the sim's car.</summary>
    void Read(SimState state, Vehicle car, double alpha, double delta)
    {
        var g = Gauges;
        bool runs = Vehicles.runs(car);
        double kmh = Math.Abs(car.speed) * Tick.TICKS_PER_SECOND / Motion.SUB * 3.6;
        g.Kmh = kmh;
        g.Fuel = car.fuel / Vehicles.FUEL_MAX;
        g.LowFuel = g.Fuel < 0.15;
        g.Engine = car.health < Vehicles.VEHICLE_HEALTH / 2;
        g.Oil = g.Battery = !runs;
        g.Brake = car.speed == 0;
        g.Lights = car.lights == true;
        g.Gear = car.speed == 0 ? 'P' : !runs ? 'N' : car.speed > 0 ? 'D' : 'R';
        // Engine heat: cold until it runs, then by its health (a sound one just under the middle, a wreck at the top).
        double heat = runs ? 0.42 + 0.5 * (1 - car.health / Vehicles.VEHICLE_HEALTH) : car.health <= 0 ? 0.96 : 0.08;
        temp += (heat - temp) * Math.Min(1, delta * 0.6);
        g.Temp = temp;
        // Engine speed: an idle, and the speed through three gears (a change at 8 and at 15 km/h).
        double want = !runs ? 0
            : car.speed < 0 ? 900 + kmh / 6.75 * 2200
            : kmh < 8 ? 900 + kmh / 8 * 2600
            : kmh < 15 ? 1700 + (kmh - 8) / 7 * 1900
            : 1900 + (kmh - 15) / 7.5 * 1700;
        rpm += (want - rpm) * Math.Min(1, delta * 7);
        g.Rpm = rpm;
        // The odometer: a mileage by the car's id, and the road since you got in.
        var at = new Vector2((float)car.at.x, (float)car.at.y) / Motion.SUB;
        if (last is { } was) trip += at.DistanceTo(was) / 1000;
        last = at;
        g.OdoKm = 20000 + Math.Floor(Looks.Hash01(car.id, 31) * 160000) + trip;
        // The panel is lit with the headlights; without them it has a little light of its own, as by day.
        glass.EmissionEnergyMultiplier = car.lights == true && runs ? 1.4f : 0.45f;
    }
}
