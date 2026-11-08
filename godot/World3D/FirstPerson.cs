// First person (the game's main view, 2026-11-08): the world in 3D round the survivor you control,
// seen from their eyes. Reads state only (rule 5): the camera's yaw and pitch are view state, and
// the game turns them into `aim` and `steer` commands. Chunks of the world (WorldMesh) are built
// near you and rebuilt only when what they show changes, a few a frame at most (rule 14).
// People are the character models (People.cs), the nearest MAX_MODELS of them; the rest, animals,
// cars and things on the ground are placeholders (Bodies.cs).

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
    readonly People people;
    /// <summary>How many people are drawn as models this frame (the debug panel).</summary>
    public int Modelled { get; private set; }
    /// <summary>When it last changed: walkability, open doors and windows, boards, furniture.</summary>
    (int, int, int, int, int, int) worldKey = (-1, 0, 0, 0, 0, 0);
    int sinceCheck;
    public int ChunkCount => chunks.Count;

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
        people = new People(this);
    }

    /// <summary>Where the eye is (m) and which way it looks, from the controlled survivor.</summary>
    public static (Vector3 eye, bool inCar, double carYaw) Eye(SimState state, Survivor you, double alpha)
    {
        var car = Vehicles.vehicleOf(state, you);
        if (car != null)
        {
            var c = worldPos(car.at, car.was, alpha);
            var (hx, hy) = (Js.Get(Vehicles.HEADING_DIRS, car.heading)![0], Js.Get(Vehicles.HEADING_DIRS, car.heading)![1]);
            return (new Vector3((float)c.x, EYE_CAR, (float)c.y), true, Math.Atan2(hy, hx));
        }
        var p = worldPos(you, alpha);
        float h = you.sleep?.asleep == true ? EYE_ASLEEP : state.control?.sneaking == true ? EYE_SNEAK : EYE;
        return (new Vector3((float)p.x, h, (float)p.y), false, 0);
    }

    /// <summary>A grid direction (yaw: 0 east, +pi/2 south) and pitch (up positive) as a look vector.</summary>
    public static Vector3 LookDir(double yaw, double pitch) =>
        new((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Sin(yaw) * Math.Cos(pitch)));

    public void Update(SimState state, double alpha, double yaw, double pitch, double delta)
    {
        var you = ControlMod.controlled(state);
        if (you == null) return;
        var (eye, _, _) = Eye(state, you, alpha);
        camera.Position = eye;
        camera.LookAt(eye + LookDir(yaw, pitch), Vector3.Up);
        Light(state);
        Chunks(state, eye);
        var modelled = people.Update(state, alpha, eye, you.id, delta, GetViewport().GetVisibleRect().Size.Y);
        Modelled = modelled.Count;
        bodies.Update(state, alpha, eye, you.id, modelled);
    }

    void Light(SimState state)
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
        env.AmbientLightEnergy = 0.5f;
        // What you can make out round you in the dark.
        near.LightEnergy = (float)(0.5 * dark);
    }

    void Chunks(SimState state, Vector3 eye)
    {
        var g = state.grid;
        var key = (state.walkabilityVersion, Sum(g.edgeOpen), Sum(g.open), g.edgeBoarded.Count, state.furniture.Count, g.edgeLocked.Count + g.locked.Count);
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
            var mesh = WorldMesh.Build(state, x, y, look, solid, glass);
            if (have) c.node.Mesh = mesh;
            else
            {
                c.node = new MeshInstance3D();
                AddChild(c.node);
                c.node.Mesh = mesh;
            }
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

    /// <summary>The ground point the eye looks at, within `reach` metres along the ground, or null.</summary>
    public static Vector2? GroundAt(Vector3 eye, double yaw, double pitch, double reach)
    {
        if (pitch > -0.05) return null;
        double along = eye.Y / Math.Tan(-pitch);
        if (along > reach) return null;
        return new Vector2((float)(eye.X + Math.Cos(yaw) * along), (float)(eye.Z + Math.Sin(yaw) * along));
    }
}
