// First person (the game's main view, 2026-11-08): the world in 3D round the survivor you control,
// seen from their eyes. Reads state only (rule 5): the camera's yaw and pitch are view state, and
// the game turns them into `aim` and `steer` commands. Chunks of the world (WorldMesh) are built
// near you and rebuilt only when what they show changes, a few a frame at most (rule 14).
// People are the character models (People.cs), the nearest MAX_MODELS of them; the rest, animals
// and cars are drawn by Bodies.cs, things on the ground by Items.cs, what you hold by Held.cs.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;

namespace LoonieRoo.Game.World3D;

public sealed partial class FirstPerson : Node3D
{
    /// <summary>Chunks this far (in chunks) from yours are kept built; 3 is 48-56 m.</summary>
    const int RADIUS = 3;
    /// <summary>Chunk meshes built per frame at most.</summary>
    const int BUILDS_PER_FRAME = 4;
    /// <summary>Bodies further than this (m) aren't drawn.</summary>
    const float BODY_RANGE = 48;
    public const float EYE = 1.65f, EYE_SNEAK = 1.1f, EYE_ASLEEP = 0.35f, EYE_CAR = 1.15f;
    /// <summary>Vertical field of view (degrees), as the look lab's.</summary>
    const float FOV = 60;

    public readonly Camera3D camera = new() { Fov = FOV, Near = 0.05f, Far = 120 };
    readonly DirectionalLight3D sun = new() { ShadowEnabled = true, DirectionalShadowMaxDistance = 40 };
    readonly OmniLight3D near = new() { OmniRange = 7, LightEnergy = 0, ShadowEnabled = false };
    readonly Godot.Environment env = new();
    readonly StandardMaterial3D solid = new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };
    readonly StandardMaterial3D glass = new() { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.1f, Metallic = 0.3f };
    readonly WorldMesh.Lookups look = new();
    readonly Dictionary<(int, int), (MeshInstance3D node, long sig)> chunks = new();
    readonly Bodies bodies;
    readonly ItemModels items;
    readonly Held held;
    readonly People people;
    readonly Trees trees;
    readonly FarField far;
    readonly Roofs roofs;
    /// <summary>The dead's moans and blows landing, positional (godot/Audio).</summary>
    public readonly Audio.Sounds sounds = new();
    /// <summary>How many people are drawn as models this frame (the debug panel).</summary>
    public int Modelled { get; private set; }
    /// <summary>When it last changed: walkability, open doors and windows, boards, furniture, what's indoors.</summary>
    (int, int, int, int, int, int, int) worldKey = (-1, 0, 0, 0, 0, 0, 0);
    int sinceCheck;
    public int ChunkCount => chunks.Count;
    public int TreeCount => trees.Count;
    public FarField Far => far;
    public int ItemCount => items.Count;
    /// <summary>--eye: look from here (x, z; y metres over the ground there) instead of your eyes, to see a place from outside or above (a debug view).</summary>
    public Vector3? EyeAt;

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
        held = new Held(camera, items);
        people = new People(this);
        trees = new Trees(this);
        far = new FarField(this, trees);
        roofs = new Roofs(this);
        AddChild(sounds);
        look.indoor = roofs.Indoor;
    }

    /// <summary>Where the eye is (m) and which way it looks, from the controlled survivor.</summary>
    /// <summary>Where the eye is (m: on the ground under you, as it is there) and which way it looks. Update eases the ground's height.</summary>
    public static (Vector3 eye, bool inCar, double carYaw) Eye(SimState state, Survivor you, double alpha)
    {
        Terrain.Use(state);
        var car = Vehicles.vehicleOf(state, you);
        if (car != null)
        {
            var c = worldPos(car.at, car.was, alpha);
            var (hx, hy) = (Js.Get(Vehicles.HEADING_DIRS, car.heading)![0], Js.Get(Vehicles.HEADING_DIRS, car.heading)![1]);
            return (new Vector3((float)c.x, Terrain.Height((float)c.x, (float)c.y) + EYE_CAR, (float)c.y), true, Math.Atan2(hy, hx));
        }
        var p = worldPos(you, alpha);
        float h = you.sleep?.asleep == true ? EYE_ASLEEP : state.control?.sneaking == true ? EYE_SNEAK : EYE;
        return (new Vector3((float)p.x, Terrain.Height((float)p.x, (float)p.y) + h, (float)p.y), false, 0);
    }

    /// <summary>The ground under the eye, eased: walking over a rise or a kerb lifts the view smoothly.</summary>
    float eyeGround = float.NaN;
    /// <summary>How fast the eye's ground catches up (per second), and a jump past which it snaps (a teleport, a load).</summary>
    const float EYE_EASE = 9, EYE_SNAP = 1.5f;

    /// <summary>A grid direction (yaw: 0 east, +pi/2 south) and pitch (up positive) as a look vector.</summary>
    public static Vector3 LookDir(double yaw, double pitch) =>
        new((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Sin(yaw) * Math.Cos(pitch)));

    public void Update(SimState state, double alpha, double yaw, double pitch, double delta)
    {
        var you = ControlMod.controlled(state);
        if (you == null) return;
        Terrain.Use(state);
        var (eye, inCar, _) = Eye(state, you, alpha);
        float ground = Terrain.Height(eye.X, eye.Z);
        if (float.IsNaN(eyeGround) || Math.Abs(ground - eyeGround) > EYE_SNAP) eyeGround = ground;
        else eyeGround += (ground - eyeGround) * (float)Math.Min(1, delta * EYE_EASE);
        eye.Y += eyeGround - ground;
        if (EyeAt is { } at) eye = at with { Y = Terrain.Height(at.X, at.Z) + at.Y };
        camera.Position = eye;
        camera.LookAt(eye + LookDir(yaw, pitch), Vector3.Up);
        roofs.Update(state, eye, RADIUS);
        Light(state, eye, delta);
        Chunks(state, eye);
        trees.Update(state, eye, look);
        // The far field steps aside where the chunks are (the square they're kept in); with it on, the view and the fog reach its range.
        int hx = (int)Math.Floor(eye.X / WorldMesh.CHUNK), hz = (int)Math.Floor(eye.Z / WorldMesh.CHUNK);
        far.Update(state, eye, new Vector4((hx - RADIUS) * WorldMesh.CHUNK, (hz - RADIUS) * WorldMesh.CHUNK, (hx + RADIUS + 1) * WorldMesh.CHUNK, (hz + RADIUS + 1) * WorldMesh.CHUNK));
        camera.Far = FarField.Range > 0 ? FarField.Range + 64 : 120;
        env.FogDensity = FarField.Range > 0 ? 3.2f / FarField.Range : 0.018f;
        var modelled = people.Update(state, alpha, eye, you.id, delta, GetViewport().GetVisibleRect().Size.Y);
        Modelled = modelled.Count;
        people.UpdateYou(state, you, alpha, yaw, delta, !inCar, EyeAt != null, held.RightHand, held.LeftHand);
        bodies.Update(state, alpha, eye, you.id, modelled);
        items.Update(state, eye);
        held.Update(state, you, alpha, delta, inCar || EyeAt != null);
        sounds.Update(state, alpha, you);
    }

    /// <summary>How much of the open sky's ambient light reaches you: less under a roof, eased so doorways don't flicker.</summary>
    float ambient = 1;

    void Light(SimState state, Vector3 eye, double delta)
    {
        double dark = Sim.Time.darkness(state.tick);
        float day = (float)(1 - dark);
        // The sun goes round with the hour; at night a faint moon from the same side.
        double hour = Sim.Time.hourOf(state.tick);
        float elev = (float)Math.Max(12, 60 - Math.Abs(hour - 13) * 6);
        sun.RotationDegrees = new Vector3(-elev, (float)(hour * 15), 0);
        sun.LightEnergy = 0.08f + 1.1f * day;
        sun.LightColor = new Color(1, 0.95f, 0.86f).Lerp(new Color(0.55f, 0.62f, 0.85f), (float)dark);
        var sky = new Color(0.52f, 0.56f, 0.58f).Lerp(new Color(0.02f, 0.025f, 0.04f), (float)dark);
        if (state.weather != "clear") sky = sky.Darkened(0.25f);
        env.BackgroundColor = sky;
        env.FogLightColor = sky;
        env.AmbientLightColor = new Color(0.55f, 0.58f, 0.6f).Lerp(new Color(0.08f, 0.09f, 0.13f), (float)dark);
        int tile = (int)Math.Floor(eye.Z) * state.grid.width + (int)Math.Floor(eye.X);
        float want = roofs.Indoor.Contains(tile) ? 0.45f : 1;
        ambient += (want - ambient) * (float)Math.Min(1, delta * 3);
        env.AmbientLightEnergy = 0.5f * ambient;
        // What you can make out round you in the dark.
        near.LightEnergy = (float)(0.5 * dark);
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
        for (int y = Math.Max(0, ccy - RADIUS); y <= Math.Min(maxY, ccy + RADIUS); y++)
            for (int x = Math.Max(0, ccx - RADIUS); x <= Math.Min(maxX, ccx + RADIUS); x++)
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
            long sig = WorldMesh.Signature(state, x, y, look);
            if (have && c.sig == sig) continue;
            var (mesh, groundMesh) = WorldMesh.Build(state, x, y, look, solid, glass);
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
            if (Math.Max(Math.Abs(k.Item1 - ccx), Math.Abs(k.Item2 - ccy)) > RADIUS + 1) drop.Add(k);
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
