// First person (the game's main view, 2026-11-08): the world in 3D round the survivor you control,
// seen from their eyes. Reads state only (rule 5): the camera's yaw and pitch are view state, and
// the game turns them into `aim` and `steer` commands. Chunks of the world (WorldMesh) are built
// near you and rebuilt only when what they show changes, a few a frame at most (rule 14).
// People are the character models (People.cs), the nearest MAX_MODELS of them, and the rest capsules (Bodies.cs,
// which also drives the animals' models: Animals.cs); cars are Cars.cs, things on the ground Items.cs, what you
// hold Held.cs.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed partial class FirstPerson : Node3D
{
    /// <summary>Chunks this far (in chunks) from yours are kept built (with their trees, furniture and roofs); 3 is
    /// 48-56 m. The options' Detail distance (Ui/Graphics.cs).</summary>
    public static int Radius = 3;
    /// <summary>How far the sun's shadows reach (m); 0 is none. The options' Shadows.</summary>
    public static float ShadowDistance = 40;
    /// <summary>Chunk meshes built per frame at most.</summary>
    const int BUILDS_PER_FRAME = 4;
    // Chunk build timing (Stopwatch ticks), printed by the light bench: how many built, their total and worst, and the signature checks.
    public static long BuildTicks, Builds, WorstBuild, SigTicks, Sigs;
    /// <summary>The chunk bench (after the light bench): every chunk in range built REPS times over, warm, and where the time went.</summary>
    public string ChunkBench(SimState state)
    {
        const int REPS = 3;
        System.Array.Clear(WorldMesh.PartTicks);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int n = 0;
        for (int r = 0; r < REPS; r++)
            foreach (var k in chunks.Keys) { var (a, b) = WorldMesh.Build(state, k.Item1, k.Item2, look, solid, glass); a.Dispose(); b.Dispose(); n++; }
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double all = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * f / n;
        var p = WorldMesh.PartTicks;
        return $"chunk bench: {n / REPS} chunks x {REPS}, {all:0.00} ms each: tile loop {p[1] * f / n:0.00} (its ground {p[0] * f / n:0.00}), furniture {p[2] * f / n:0.00}, commits {p[3] * f / n:0.00}; in the ground: banks {p[4] * f / n:0.00}, edges {p[5] * f / n:0.00}";
    }

    public static string ChunkStats()
    {
        double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return $"chunks: {Builds} built, {(Builds > 0 ? BuildTicks * f / Builds : 0):0.00} ms each, worst {WorstBuild * f:0.00} ms; {Sigs} signatures, {(Sigs > 0 ? SigTicks * f / Sigs : 0):0.000} ms each";
    }
    public const float EYE = 1.65f, EYE_SNEAK = 1.1f, EYE_ASLEEP = 0.35f;
    /// <summary>Vertical field of view (degrees), as the look lab's.</summary>
    const float FOV = 60;

    // --- the scene: camera, sky and the world's chunks ---
    public readonly Camera3D camera = new() { Fov = FOV, Near = 0.05f, Far = 120 };
    readonly DirectionalLight3D sun = new() { ShadowEnabled = true, DirectionalShadowMaxDistance = 40 };
    readonly OmniLight3D near = new() { OmniRange = 7, LightEnergy = 0, ShadowEnabled = false };
    readonly Godot.Environment env = new();
    /// <summary>The chunks' two materials: vertex colours with a detail layer per face (WorldLook.cs).</summary>
    readonly ShaderMaterial solid = WorldLook.Solid, glass = WorldLook.Glass;
    readonly WorldMesh.Lookups look = new();
    readonly Dictionary<(int, int), (MeshInstance3D node, long sig)> chunks = new();
    /// <summary>When it last changed: walkability, open doors and windows, boards, furniture, what's indoors.</summary>
    (int, int, int, int, int, int, int) worldKey = (-1, 0, 0, 0, 0, 0, 0);
    int sinceCheck;

    // --- the renderers, each a part of the world; Update calls them in a fixed order (below) ---
    // The land: roofs and what's indoors, country roads, trees, furniture with a model, the far view, the weather.
    readonly Roofs roofs;
    readonly RoadRibbons ribbons;
    readonly Trees trees;
    readonly FurnitureModels furniture;
    readonly FarField far;
    /// <summary>Rain, snow, the snow layer and the overcast sky (Weather.cs).</summary>
    readonly WeatherLayer weather = new();
    // Who is about: others' stand-ins, the character models, the capsules (and the animals).
    readonly Others others;
    readonly People people;
    readonly Bodies bodies;
    // Things: cars and the cockpit of yours, farm plots, building sites, the helicopter, items on the ground.
    readonly Cars cars;
    readonly Cockpit cockpit;
    readonly Fields fields;
    readonly Sites sites;
    readonly Aircraft aircraft;
    readonly ItemModels items;
    // You: what you hold, your arms swimming. Then the world's lights and its sounds.
    readonly Held held;
    readonly SwimHands swimHands;
    readonly WorldLights lights;
    /// <summary>The dead's moans and blows landing, positional (godot/Audio).</summary>
    public readonly Audio.Sounds sounds = new();

    // --- read from outside: the debug panel, the benches and the self-test ---
    public RoadRibbons Ribbons => ribbons;
    public SwimHands SwimHands => swimHands;
    public Held Held => held;
    /// <summary>The people drawn (the self-test reads their footfalls).</summary>
    public People People => people;
    /// <summary>The world's lights (the debug panel, the light bench).</summary>
    public WorldLights Lights => lights;
    /// <summary>The cockpit of the car you drive (the self-test reads its instruments).</summary>
    public Cockpit Cockpit => cockpit;
    public FarField Far => far;
    /// <summary>How many people are drawn as models this frame (the debug panel).</summary>
    public int Modelled { get; private set; }
    public int ChunkCount => chunks.Count;
    public int TreeCount => trees.Count;
    public int ItemCount => items.Count;
    /// <summary>What the newer renderers drew (the self-test): farm plots, building-site pieces, cars, others' props and people, the helicopter.</summary>
    public int PlotCount => fields.Count;
    public int SiteCount => sites.Count;
    public int CarCount => cars.Count;
    public int OtherProps => others.Props;
    public int OtherPeople => others.Shown.Count;
    public bool HelicopterShown => aircraft.Shown;

    /// <summary>Before Update each frame: how far the car you're in turned (its heading eased), for the view to turn with
    /// it, so you keep looking out of the windscreen as you steer. Getting in, with `faceForward`, it is the turn that
    /// faces you forward.</summary>
    public double CarTurn(SimState state, double viewYaw, double delta, bool faceForward)
    {
        var you = ControlMod.controlled(state);
        return cockpit.Track(you != null ? Vehicles.vehicleOf(state, you) : null, viewYaw, delta, faceForward);
    }
    /// <summary>--eye: look from here (x, z; y metres over the ground there) instead of your eyes, to see a place from outside or above (a debug view).</summary>
    public Vector3? EyeAt;
    /// <summary>The eye over the (eased) ground under it this frame (m): EYE standing, more in a leap (the self-test compares your body's lift).</summary>
    public float EyeUp { get; private set; }

    public FirstPerson()
    {
        AddChild(camera);
        camera.AddChild(near);
        AddChild(sun);
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.FogEnabled = true;
        env.FogDensity = 0.018f;
        env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        AddChild(new WorldEnvironment { Environment = env });
        bodies = new Bodies(this);
        items = new ItemModels(this);
        furniture = new FurnitureModels(this);
        held = new Held(camera, items);
        swimHands = new SwimHands(camera, items);
        ribbons = new RoadRibbons(this, solid);
        people = new People(this) { Items = items };
        trees = new Trees(this);
        far = new FarField(this, trees);
        roofs = new Roofs(this);
        lights = new WorldLights(this, camera);
        fields = new Fields(this);
        sites = new Sites(this);
        aircraft = new Aircraft(this);
        cars = new Cars(this);
        others = new Others(this);
        cockpit = new Cockpit(this);
        AddChild(sounds);
        AddChild(weather);
        look.indoor = roofs.Indoor;
        look.styles = roofs.Styles;
    }

    /// <summary>Where the eye is (m: on the ground under you, as it is there) and which way it looks, from the controlled
    /// survivor. Update eases the ground's height. In a car the eye is the driver's (Cockpit.Seat: ahead of the car's centre, to its left), on the car's heading
    /// or `carYaw` if given (the view's eased heading, so the seat doesn't jump between the sim's 32 headings).</summary>
    public static (Vector3 eye, bool inCar, double carYaw) Eye(SimState state, Survivor you, double alpha, double? carYaw = null)
    {
        Terrain.Use(state);
        var car = Vehicles.vehicleOf(state, you);
        if (car != null)
        {
            var c = worldPos(car.at, car.was, alpha);
            double a = carYaw ?? Cockpit.YawOf(car);
            var (ahead, up, left) = Cockpit.Seat(car);
            // Ahead is the heading; the car's left is a quarter turn anticlockwise of it (the grid's y runs south).
            double fx = Math.Cos(a), fy = Math.Sin(a);
            return (new Vector3((float)(c.x + fx * ahead + fy * left), Terrain.Height((float)c.x, (float)c.y) + up, (float)(c.y + fy * ahead - fx * left)), true, a);
        }
        var p = worldPos(you, alpha);
        float h = you.sleep?.asleep == true || state.control?.underCar != null ? EYE_ASLEEP : state.control?.sneaking == true ? EYE_SNEAK : EYE;  // under a pickup: lying (VehicleHide.cs)
        h += (float)Leaps.height(state, you, alpha); // a leap up and down over its ticks, or the top of the car you stand on
        return (new Vector3((float)p.x, Terrain.Height((float)p.x, (float)p.y) + h, (float)p.y), false, 0);
    }

    /// <summary>Swimming: the eye this far over the water's surface (m), and how fast it settles there (per second).</summary>
    public const float SWIM_EYE = 0.14f, SINK_EASE = 3;
    /// <summary>How far under standing height the eye (and your body with it) is, swimming (m, 0 or less; eased).</summary>
    float sink;

    /// <summary>The ground under the eye, eased: walking over a rise or a kerb lifts the view smoothly.</summary>
    float eyeGround = float.NaN;
    /// <summary>How fast the eye's ground catches up (per second), and a jump past which it snaps (a teleport, a load).</summary>
    const float EYE_EASE = 9, EYE_SNAP = 1.5f;

    /// <summary>A grid direction (yaw: 0 east, +pi/2 south) and pitch (up positive) as a look vector.</summary>
    public static Vector3 LookDir(double yaw, double pitch) =>
        new((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Sin(yaw) * Math.Cos(pitch)));

    /// <summary>A frame: `delta` the frame's time (the light's and the weather's eases), `world` the world's (0 while it is
    /// held: the menu, the map, --paused; the frame's if not given), which everything that moves runs on. The steps in
    /// order, each needing what the ones before it set: the eye; roofs (what's indoors) and the light; the land (roads
    /// before the chunks, which ask whether roads are ribbons); the weather (after the light and the fog, which it dims);
    /// who is about (others before the people they stand in for, the models before the capsules for the rest); things;
    /// you; the world's lights and its sounds.</summary>
    public void Update(SimState state, double alpha, double yaw, double pitch, double delta, double? worldDelta = null)
    {
        double world = worldDelta ?? delta;
        var you = ControlMod.controlled(state);
        if (you == null) return;
        Terrain.Use(state);
        var car = Vehicles.vehicleOf(state, you);
        var (eye, inCar) = PlaceEye(state, you, car, alpha, yaw, pitch, delta);
        roofs.Update(state, eye, Radius);
        sun.ShadowEnabled = ShadowDistance > 0;
        if (ShadowDistance > 0) sun.DirectionalShadowMaxDistance = ShadowDistance;
        Light(state, eye, delta);
        Land(state, eye);
        weather.Held = world <= 0;
        weather.Update(state, eye, delta, roofs.Indoor, sun, env); // overcast and fog on bad days, rain, snow (Weather.cs)
        // Who is about: wanderers, traders, prisoners, caravans and runs stand in for People and Bodies.
        others.Update(state, eye, world);
        var modelled = people.Update(state, alpha, eye, you.id, world, GetViewport().GetVisibleRect().Size.Y, others.Shown);
        Modelled = modelled.Count;
        people.UpdateYou(state, you, alpha, yaw, world, !inCar, EyeAt != null, held.RightHand, held.LeftHand);
        bodies.Update(state, alpha, eye, you.id, modelled, others.Shown, world);
        // Things. The car you sit in is the cockpit round you; seen from outside (--eye) it is a car like the others.
        bool inside = car != null && EyeAt == null;
        cars.Update(state, alpha, eye, inside ? car : null);
        cockpit.Update(state, car, camera.Position, alpha, delta, inside); // its needles ease to the car as it is (held still, it stays)
        fields.Update(state, eye);
        sites.Update(state, eye);
        aircraft.Update(state, world);
        items.Update(state, eye);
        // You: nothing in hand in a car, under a pickup or seen from outside.
        held.Update(state, you, alpha, world, inCar || EyeAt != null || state.control?.underCar != null);
        swimHands.Update(state, you, world, inCar || EyeAt != null);
        lights.Update(state, eye, alpha, world, inCar);
        sounds.Update(state, alpha, you, people.Steps);
    }

    /// <summary>The frame's first step: the camera at your eye, looking along yaw and pitch. The ground under it is
    /// eased (a kerb or a rise lifts the view smoothly), swimming sinks it to the surface, --eye moves it. Returns the
    /// eye and whether you are in a car.</summary>
    (Vector3 eye, bool inCar) PlaceEye(SimState state, Survivor you, Vehicle? car, double alpha, double yaw, double pitch, double delta)
    {
        var (eye, inCar, _) = Eye(state, you, alpha, car != null && cockpit.Following(car) ? cockpit.Yaw : null);
        // Out of your depth (Swim.cs) the eye sits just over the water, not standing height over the bed; eased in and out.
        float sinkTo = !inCar && Swim.swimming(state, you) ? Math.Min(0, Terrain.Water(eye.X, eye.Z) + SWIM_EYE - eye.Y) : 0;
        sink += (sinkTo - sink) * (float)Math.Min(1, delta * SINK_EASE);
        float ground = Terrain.Height(eye.X, eye.Z);
        if (float.IsNaN(eyeGround) || Math.Abs(ground - eyeGround) > EYE_SNAP) eyeGround = ground;
        else eyeGround += (ground - eyeGround) * (float)Math.Min(1, delta * EYE_EASE);
        eye.Y += eyeGround - ground + sink;
        people.YourSink = sink;
        if (EyeAt is { } at) eye = at with { Y = Terrain.Height(at.X, at.Z) + at.Y };
        camera.Position = eye;
        EyeUp = eye.Y - eyeGround;
        camera.LookAt(eye + LookDir(yaw, pitch), Vector3.Up);
        return (eye, inCar);
    }

    /// <summary>The land round the eye: country roads, the chunks, their trees and furniture, the far view past them,
    /// and how far the camera and the fog reach.</summary>
    void Land(SimState state, Vector3 eye)
    {
        // The far field steps aside where the chunks are (the square they're kept in); with it on, the view and the fog reach its range.
        int hx = (int)Math.Floor(eye.X / WorldMesh.CHUNK), hz = (int)Math.Floor(eye.Z / WorldMesh.CHUNK);
        var hole = new Vector4((hx - Radius) * WorldMesh.CHUNK, (hz - Radius) * WorldMesh.CHUNK, (hx + Radius + 1) * WorldMesh.CHUNK, (hz + Radius + 1) * WorldMesh.CHUNK);
        // Before the chunks: it says whether country roads are ribbons (WorldMesh.Ribboned). In the hole only while the far field draws past it.
        ribbons.Update(state, eye, FarField.Range > 0 ? hole : null);
        Chunks(state, eye);
        trees.Update(state, eye, look);
        furniture.Update(state, eye, look);
        far.Update(state, eye, hole);
        camera.Far = FarField.Range > 0 ? FarField.Range + 64 : Math.Max(120, (Radius + 1) * WorldMesh.CHUNK * 1.6f);
        env.FogDensity = FarField.Range > 0 ? 3.2f / FarField.Range : 0.018f;
    }

    /// <summary>How much of the open sky's ambient light reaches you: less under a roof, eased so doorways don't flicker.</summary>
    float ambient = 1;

    void Light(SimState state, Vector3 eye, double delta)
    {
        double dark = Sim.Time.darkness(state.tick);
        // The sim's twilight is two hours of straight ramp, which reads as night for most of it: the light holds
        // its day a while into dawn and dusk (the square), and warms toward the horizon. The sim's clock is untouched.
        double lit = dark * dark;
        float day = (float)(1 - lit);
        float low = (float)Math.Clamp(Math.Sin(Math.Min(1, dark) * Math.PI), 0, 1); // 0 by day and at night, 1 mid-twilight
        // The sun goes round with the hour; at night a faint moon from the same side.
        double hour = Sim.Time.hourOf(state.tick);
        float elev = (float)Math.Max(16, 60 - Math.Abs(hour - 13) * 6);
        sun.RotationDegrees = new Vector3(-elev, (float)(hour * 15), 0);
        sun.LightEnergy = 0.08f + 1.1f * day;
        sun.LightColor = new Color(1, 0.95f, 0.86f).Lerp(new Color(1, 0.82f, 0.62f), low * 0.7f).Lerp(new Color(0.55f, 0.62f, 0.85f), (float)lit);
        var sky = new Color(0.52f, 0.56f, 0.58f).Lerp(new Color(0.02f, 0.025f, 0.04f), (float)lit);
        env.BackgroundColor = sky;
        env.FogLightColor = sky;
        env.AmbientLightColor = new Color(0.55f, 0.58f, 0.6f).Lerp(new Color(0.045f, 0.05f, 0.075f), (float)lit);
        int tile = (int)Math.Floor(eye.Z) * state.grid.width + (int)Math.Floor(eye.X);
        // Under a roof the sun doesn't reach you, so the sky's light through the windows is all a room has by day:
        // most of it (0.45 left a room at noon too dark to read its walls or what you hold).
        float want = roofs.Indoor.Contains(tile) ? 0.8f : 1;
        ambient += (want - ambient) * (float)Math.Min(1, delta * 3);
        env.AmbientLightEnergy = 0.5f * ambient;
        // What you can make out round you in the dark (faint: the world's lights and your flashlight do the rest).
        near.LightEnergy = (float)(0.22 * dark);
        near.OmniRange = 5;
    }

    void Chunks(SimState state, Vector3 eye)
    {
        var g = state.grid;
        var key = (state.walkabilityVersion, Sum(g.edgeOpen), Sum(g.open), g.edgeBoarded.Count, state.furniture.Count, g.edgeLocked.Count + g.locked.Count, roofs.Version);
        bool changed = key != worldKey || ++sinceCheck >= 120;
        if (changed)
        {
            worldKey = key;
            sinceCheck = 0;
            look.Refresh(state);
        }
        int ccx = (int)Math.Floor(eye.X / WorldMesh.CHUNK), ccy = (int)Math.Floor(eye.Z / WorldMesh.CHUNK);
        int maxX = (g.width - 1) / WorldMesh.CHUNK, maxY = (g.height - 1) / WorldMesh.CHUNK;
        // Nearest first, so what you're standing in is built before the distance.
        var want = new List<(int d, int x, int y)>();
        for (int y = Math.Max(0, ccy - Radius); y <= Math.Min(maxY, ccy + Radius); y++)
            for (int x = Math.Max(0, ccx - Radius); x <= Math.Min(maxX, ccx + Radius); x++)
                want.Add((Math.Max(Math.Abs(x - ccx), Math.Abs(y - ccy)), x, y));
        want.Sort((a, b) => a.d.CompareTo(b.d));
        int built = 0;
        foreach (var (_, x, y) in want)
        {
            bool have = chunks.TryGetValue((x, y), out var c);
            if (have && !changed) continue;
            if (built >= BUILDS_PER_FRAME)
            {
                if (changed) sinceCheck = 120; // look at the rest next frame
                break;
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            long sig = WorldMesh.Signature(state, x, y, look);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            SigTicks += t1 - t0; Sigs++;
            if (have && c.sig == sig) continue;
            var (mesh, groundMesh) = WorldMesh.Build(state, x, y, look, solid, glass);
            long took = System.Diagnostics.Stopwatch.GetTimestamp() - t1;
            BuildTicks += took; Builds++; WorstBuild = Math.Max(WorstBuild, took);
            if (!have)
            {
                c.node = new MeshInstance3D();
                AddChild(c.node);
                c.node.AddChild(new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            c.node.Mesh = mesh;
            c.node.GetChild<MeshInstance3D>(0).Mesh = groundMesh;
            chunks[(x, y)] = (c.node, sig);
            built++;
        }
        // Drop chunks well out of range.
        var drop = new List<(int, int)>();
        foreach (var (k, c) in chunks)
            if (Math.Max(Math.Abs(k.Item1 - ccx), Math.Abs(k.Item2 - ccy)) > Radius + 1) drop.Add(k);
        foreach (var k in drop)
        {
            chunks[k].node.QueueFree();
            chunks.Remove(k);
        }
    }

    static int Sum(List<int> l)
    {
        int s = l.Count;
        foreach (var v in l) s = s * 31 + v;
        return s;
    }

    /// <summary>The ground point the eye looks at, within `reach` metres along the ground, or null: the look
    /// marched out in short steps until it goes under the ground (Terrain).</summary>
    public static Vector2? GroundAt(Vector3 eye, double yaw, double pitch, double reach)
    {
        if (pitch > -0.05) return null;
        double drop = Math.Tan(-pitch), cos = Math.Cos(yaw), sin = Math.Sin(yaw);
        const double STEP = 0.05;
        for (double along = STEP; along <= reach; along += STEP)
        {
            float x = (float)(eye.X + cos * along), z = (float)(eye.Z + sin * along);
            if (eye.Y - drop * along <= Terrain.Height(x, z)) return new Vector2(x, z);
        }
        return null;
    }
}
