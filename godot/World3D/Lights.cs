// The world's lights in first person (2026-11-10). Reads state only (rule 5): the sim says what is lit (Sim.Lights:
// rooms behind switches, porches, street lamps, headlights, your flashlight, stoves, heaters, fires); this draws it.
//
//   real lights   the nearest Budget sources within RANGE m get a Godot light (an OmniLight3D, or a SpotLight3D for
//                 street lamps, headlights and the flashlight); the nearest Shadowed of them cast shadows (so a room's
//                 light doesn't show through its walls where you are), the rest don't. Budget and Shadowed were set by
//                 measuring (log/2026-11-10-05-lighting.md: frame times at 0, 8, 16, 32 lights in a town at night).
//   glow          every source in RANGE, and the porches, street lamps and lit rooms out to FAR m, get a cheap glow
//                 (an additive billboard, one MultiMesh) and lit rooms' windows a warm pane facing out (another), so
//                 the far view has its lights without any light's cost.
//   the flashlight  hangs off the camera and follows the look (pitch too); the sim's beam follows the aim.
// The sources are fetched again when the tick changes (near you) or every FAR_EVERY s (far off); a frame only moves
// and fades the lights it has (rule 14).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using static LoonieRoo.Game.Render.CameraMath;
using SimLights = LoonieRoo.Sim.Lights;

namespace LoonieRoo.Game.World3D;

public sealed class WorldLights
{
    /// <summary>Real lights at most (the nearest sources), and how many of them cast shadows. --lights=N --light-shadows=K.</summary>
    public static int Budget = 24, Shadowed = 4;
    /// <summary>Sources within this (m) are looked at for real lights and glows; beyond, out to FAR, glows only.</summary>
    const float RANGE = 48, FAR = 320;
    const double FAR_EVERY = 2;
    const int MAX_GLOWS = 768, MAX_PANES = 512;

    /// <summary>How each kind looks: height (m over its floor), colour, energy, range factor (of its radius in tiles = m), spot angle (0: omni), glow size.</summary>
    readonly record struct Look(float h, Color c, float energy, float range, float angle, float glow);
    static readonly Dictionary<string, Look> LOOKS = new()
    {
        [SimLights.ROOM] = new(WorldMesh.WALL_H - 0.12f, new Color(1f, 0.86f, 0.66f), 1.6f, 1.25f, 0, 0),
        [SimLights.PORCH] = new(2.25f, new Color(1f, 0.82f, 0.55f), 1.4f, 1.3f, 0, 0.22f),
        [SimLights.STREET] = new(4.6f, new Color(1f, 0.72f, 0.42f), 3.2f, 1.45f, 62, 0.38f),
        [SimLights.LAMP] = new(1.7f, new Color(1f, 0.88f, 0.7f), 1.6f, 1.3f, 0, 0.22f),
        [SimLights.HEADLIGHTS] = new(0.75f, new Color(0.95f, 0.96f, 1f), 7f, 1.35f, 30, 0.16f),
        [SimLights.FLASHLIGHT] = new(0, new Color(1f, 0.97f, 0.9f), 2.4f, 1.6f, 24, 0),
        [SimLights.STOVE] = new(0.85f, new Color(1f, 0.55f, 0.25f), 0.8f, 1.4f, 0, 0.12f),
        [SimLights.HEATER] = new(1.0f, new Color(1f, 0.5f, 0.2f), 2.2f, 1.3f, 0, 0.22f),
        [SimLights.FIRE] = new(0.9f, new Color(1f, 0.5f, 0.18f), 3.2f, 1.5f, 0, 0.45f),
    };

    readonly Node3D parent;
    readonly Camera3D camera;
    readonly List<OmniLight3D> omnis = new();
    readonly List<SpotLight3D> spots = new();
    readonly SpotLight3D torch;
    readonly MultiMeshInstance3D glows, panes, posts;
    /// <summary>Street lamps' posts near you, by tile: which way their arm reaches (to the street).</summary>
    readonly Dictionary<(int, int), Vector3> arms = new();
    Vector3 postsAt = new(float.MaxValue, 0, 0);
    const float POST_H = 4.7f, ARM = 1.1f;
    const int MAX_POSTS = 256;
    List<SimLights.Source> nearList = new(), farList = new();
    int fetchedTick = -1;
    double farAge = FAR_EVERY;
    Vector3 farAt;
    double clock;
    /// <summary>Real lights lit this frame, and how many with shadows (the bench at the end of this file).</summary>
    public int Lit { get; private set; }
    public int Shadows { get; private set; }
    /// <summary>Your flashlight's beam is on (the self-test).</summary>
    public bool TorchOn => torch.Visible;

    public WorldLights(Node3D parent, Camera3D camera)
    {
        this.parent = parent;
        this.camera = camera;
        var l = LOOKS[SimLights.FLASHLIGHT];
        torch = new SpotLight3D { LightColor = l.c, LightEnergy = l.energy, SpotRange = (float)(SimLights.RADIUS[SimLights.FLASHLIGHT] * l.range), SpotAngle = l.angle, SpotAngleAttenuation = 3f, SpotAttenuation = 1.3f, ShadowEnabled = true, Visible = false, Position = new Vector3(0.18f, -0.22f, 0) };
        camera.AddChild(torch);
        glows = MakeMulti(Glow(), MAX_GLOWS, true);
        panes = MakeMulti(Pane(), MAX_PANES, false);
        posts = MakeMulti(Post(), MAX_POSTS, false);
        posts.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(glows);
        parent.AddChild(panes);
        parent.AddChild(posts);
    }

    static MultiMeshInstance3D MakeMulti(Mesh mesh, int count, bool colours)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colours, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = 0 };
        return new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>A soft additive dot that faces the camera: the bulb seen from anywhere.</summary>
    static Mesh Glow()
    {
        var grad = new Gradient();
        grad.SetColor(0, new Color(1, 1, 1, 1));
        grad.SetColor(1, new Color(1, 1, 1, 0));
        grad.AddPoint(0.25f, new Color(1, 1, 1, 0.55f));
        var tex = new GradientTexture2D { Gradient = grad, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64 };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, AlbedoTexture = tex, VertexColorUseAsAlbedo = true,
            NoDepthTest = false, DisableReceiveShadows = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new QuadMesh { Size = Vector2.One, Material = mat };
    }

    /// <summary>A warm pane behind a lit room's window, facing out (from inside it's culled).</summary>
    static Mesh Pane()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.62f, 0.43f, 0.24f), CullMode = BaseMaterial3D.CullModeEnum.Back, DisableReceiveShadows = true,
        };
        return new QuadMesh { Size = new Vector2(0.88f, WorldMeshWindow), Material = mat };
    }

    /// <summary>A street lamp: a post, an arm reaching out along -Z, a head under its end.</summary>
    static Mesh Post()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void box(Vector3 a, Vector3 b)
        {
            var c = new[] { a, new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), b, new(a.X, b.Y, b.Z) };
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 } };
            foreach (var f in faces)
            {
                var n = (c[f[1]] - c[f[0]]).Cross(c[f[2]] - c[f[0]]).Normalized();
                foreach (var i in new[] { f[0], f[1], f[2], f[0], f[2], f[3] }) { st.SetNormal(-n); st.AddVertex(c[i]); }
            }
        }
        box(new(-0.07f, 0, -0.07f), new(0.07f, POST_H, 0.07f));
        box(new(-0.04f, POST_H - 0.1f, -ARM - 0.1f), new(0.04f, POST_H - 0.02f, 0.04f));
        box(new(-0.16f, POST_H - 0.22f, -ARM - 0.3f), new(0.16f, POST_H - 0.08f, -ARM + 0.12f));
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.17f), Roughness = 0.6f, Metallic = 0.4f });
        return mesh;
    }

    /// <summary>The posts within RANGE, again when you've moved a way.</summary>
    void Posts(SimState state, Vector3 eye)
    {
        if (postsAt.DistanceTo(eye) < 8) return;
        postsAt = eye;
        arms.Clear();
        var mm = posts.Multimesh;
        int n = 0;
        foreach (var (x, y, side) in SimLights.streetPosts(state, eye.X, eye.Z, RANGE))
        {
            if (n >= MAX_POSTS) break;
            var d = side switch { 0 => Vector3.Back, 1 => Vector3.Left, 2 => Vector3.Forward, _ => Vector3.Right };
            // On the tile's street-side edge, the arm out over the street.
            var foot = new Vector3(x + 0.5f + d.X * 0.35f, 0, y + 0.5f + d.Z * 0.35f);
            foot.Y = Terrain.Height(foot.X, foot.Z);
            mm.SetInstanceTransform(n++, new Transform3D(Basis.LookingAt(d, Vector3.Up), foot));
            arms[(x, y)] = foot + d * ARM;
        }
        mm.VisibleInstanceCount = n;
    }

    const float WorldMeshWindow = 1.2f; // LINTEL_H - SILL_H in WorldMesh
    const float SILL = 0.9f;

    public void Update(SimState state, Vector3 eye, double alpha, double delta, bool inCar)
    {
        clock += delta;
        if (benchRuns != null) BenchStep(delta);
        bool off = Budget < 0; // --lights=-1: none of it (before and after pictures)
        glows.Visible = panes.Visible = posts.Visible = !off;
        if (off) { foreach (var l in omnis) l.Visible = false; foreach (var l in spots) l.Visible = false; torch.Visible = false; return; }
        if (state.tick != fetchedTick)
        {
            fetchedTick = state.tick;
            nearList = SimLights.near(state, eye.X, eye.Z, RANGE);
        }
        farAge += delta;
        if (farAge >= FAR_EVERY || farAt.DistanceTo(eye) > 16)
        {
            farAge = 0;
            farAt = eye;
            farList = FarField.Range > RANGE && Sim.Time.isNight(state.tick) ? SimLights.near(state, eye.X, eye.Z, Math.Min(FAR, FarField.Range), 600) : new();
        }
        Posts(state, eye);
        Real(state, eye, alpha);
        Torch(state, inCar);
        Glowing(state, eye);
    }

    /// <summary>Where a source's light sits in the world (m), and which way a beam points.</summary>
    (Vector3 at, Vector3 dir) Place(SimState state, SimLights.Source s, double alpha)
    {
        var look = LOOKS[s.kind];
        if (s.kind == SimLights.HEADLIGHTS)
        {
            var car = Vehicles.vehiclesOf(state).Find((v) => v.id == s.id);
            if (car != null)
            {
                var c = worldPos(car.at, car.was, alpha);
                var d = new Vector3((float)s.dx, -0.08f, (float)s.dy).Normalized();
                var front = new Vector3((float)c.x, 0, (float)c.y) + new Vector3((float)s.dx, 0, (float)s.dy) * 1.9f;
                return (front with { Y = Terrain.Height(front.X, front.Z) + look.h }, d);
            }
        }
        int tx = (int)Math.Floor(s.x), ty = (int)Math.Floor(s.y);
        if (s.kind == SimLights.STREET && arms.TryGetValue((tx, ty), out var arm)) return (arm with { Y = arm.Y + POST_H - 0.25f }, Vector3.Down);
        float floor = s.kind == SimLights.ROOM ? Terrain.Floor(s.ax, s.ay) : Terrain.Height((float)s.x, (float)s.y);
        return (new Vector3((float)s.x, floor + look.h, (float)s.y), Vector3.Down);
    }

    /// <summary>The real lights: the nearest Budget sources. Shadows go first to the light of the space you're in, then to
    /// lights of other spaces (other rooms, outdoors from in, a room from outside) whose reach comes near you: without a
    /// shadow those would show through the walls between. One of those left without a shadow isn't lit at all.</summary>
    void Real(SimState state, Vector3 eye, double alpha)
    {
        int omni = 0, spot = 0, shadows = 0, used = 0;
        bool shadowsOn = FirstPerson.ShadowDistance > 0;
        var here = SimLights.spaceOf(state, (int)Math.Floor(eye.X), (int)Math.Floor(eye.Z));
        var picked = new List<(SimLights.Source s, Vector3 at, Vector3 dir, float range, int rank)>();
        foreach (var s in nearList)
        {
            if (picked.Count >= Budget) break;
            if (s.kind == SimLights.FLASHLIGHT) continue; // the camera's own
            var look = LOOKS[s.kind];
            var (at, dir) = Place(state, s, alpha);
            float range = (float)(s.radius * look.range) + (s.kind == SimLights.ROOM ? 0.6f : 0);
            bool reaches = at.DistanceTo(eye) < range + 1.5f;
            int rank = s.space == here ? 0 : reaches ? 1 : 2;
            picked.Add((s, at, dir, range, rank));
        }
        var order = new List<int>();
        for (int i = 0; i < picked.Count; i++) order.Add(i);
        order.Sort((a, b) => picked[a].rank != picked[b].rank ? picked[a].rank.CompareTo(picked[b].rank) : a.CompareTo(b));
        var shadowed = new HashSet<int>();
        if (shadowsOn) foreach (var i in order) if (shadowed.Count < Shadowed && picked[i].rank < 2) shadowed.Add(i);
        if (shadowsOn) foreach (var i in order) if (shadowed.Count < Shadowed) shadowed.Add(i);
        for (int i = 0; i < picked.Count; i++)
        {
            var (s, at, dir, range, rank) = picked[i];
            bool shadow = shadowed.Contains(i);
            if (rank == 1 && !shadow) continue; // it would only show through a wall
            var look = LOOKS[s.kind];
            float energy = look.energy * Flicker(s);
            if (look.angle > 0)
            {
                var l = Spot(spot++);
                l.Position = at;
                if (dir.Cross(Vector3.Up).LengthSquared() < 1e-6f) l.LookAt(at + Vector3.Down, Vector3.Forward);
                else l.LookAt(at + dir, Vector3.Up);
                l.LightColor = look.c; l.LightEnergy = energy; l.SpotRange = range; l.SpotAngle = look.angle; l.ShadowEnabled = shadow;
                l.Visible = true;
            }
            else
            {
                var l = Omni(omni++);
                l.Position = at;
                l.LightColor = look.c; l.LightEnergy = energy; l.OmniRange = range; l.ShadowEnabled = shadow;
                l.Visible = true;
            }
            if (shadow) shadows++;
            used++;
        }
        for (int i = omni; i < omnis.Count; i++) omnis[i].Visible = false;
        for (int i = spot; i < spots.Count; i++) spots[i].Visible = false;
        Lit = used;
        Shadows = shadows;
    }

    float Flicker(SimLights.Source s)
    {
        if (s.kind is not (SimLights.FIRE or SimLights.HEATER or SimLights.STOVE)) return 1;
        double k = IdHash.Hash01(s.id, 5) * 97;
        return (float)(0.82 + 0.1 * Math.Sin(clock * 9.1 + k) + 0.08 * Math.Sin(clock * 23.7 + k * 1.7));
    }

    OmniLight3D Omni(int i)
    {
        while (omnis.Count <= i)
        {
            var l = new OmniLight3D { OmniAttenuation = 1.4f, ShadowBias = 0.02f, ShadowNormalBias = 1.2f, OmniShadowMode = OmniLight3D.ShadowMode.Cube };
            parent.AddChild(l);
            omnis.Add(l);
        }
        return omnis[i];
    }

    SpotLight3D Spot(int i)
    {
        while (spots.Count <= i)
        {
            var l = new SpotLight3D { SpotAttenuation = 1.2f, SpotAngleAttenuation = 1.4f, ShadowBias = 0.02f, ShadowNormalBias = 1.2f };
            parent.AddChild(l);
            spots.Add(l);
        }
        return spots[i];
    }

    /// <summary>Your flashlight: on the camera, the way you look (a sim beam is the way you aim, the same yaw).</summary>
    void Torch(SimState state, bool inCar)
    {
        var you = ControlMod.controlled(state);
        torch.Visible = you != null && !inCar && SimLights.flashlightOf(state, you)?.lit == true;
        torch.ShadowEnabled = FirstPerson.ShadowDistance > 0;
    }

    void Glowing(SimState state, Vector3 eye)
    {
        var mm = glows.Multimesh;
        var pm = panes.Multimesh;
        int n = 0, p = 0;
        var done = new HashSet<string>();
        void glow(SimLights.Source s)
        {
            if (n >= MAX_GLOWS || !done.Add(s.id)) return;
            var look = LOOKS[s.kind];
            if (look.glow <= 0) return;
            var (at, dir) = Place(state, s, 0);
            if (s.kind == SimLights.ROOM) { Windows(state, s, ref p, pm); return; }
            if (s.kind == SimLights.HEADLIGHTS)
            {
                // Two lamps either side of the car's nose.
                var side = new Vector3(-dir.Z, 0, dir.X).Normalized() * 0.65f;
                Put(mm, n++, at + side, look.glow, look.c);
                if (n < MAX_GLOWS) Put(mm, n++, at - side, look.glow, look.c);
                return;
            }
            float size = look.glow * (s.kind == SimLights.FIRE ? Flicker(s) : 1);
            Put(mm, n++, at, size, look.c * 0.75f);
        }
        foreach (var s in nearList) glow(s);
        foreach (var s in farList) glow(s);
        mm.VisibleInstanceCount = n;
        pm.VisibleInstanceCount = p;
    }

    static void Put(MultiMesh mm, int i, Vector3 at, float size, Color c)
    {
        mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), at));
        mm.SetInstanceColor(i, c);
    }

    /// <summary>A pane behind each window in a lit room's walls, facing out.</summary>
    void Windows(SimState state, SimLights.Source s, ref int p, MultiMesh pm)
    {
        var g = state.grid;
        float floor = Terrain.Floor(s.ax, s.ay);
        for (int y = s.y0; y <= s.y1; y++)
            for (int x = s.x0; x <= s.x1; x++)
            {
                if (SimLights.roomAt(state, x, y)?.key != s.space) continue;
                foreach (var (dx, dy) in DIR4)
                {
                    if (p >= MAX_PANES) return;
                    int e = Grids.edgeBetween(g, x, y, x + dx, y + dy);
                    if (!Grids.isWindow(Grids.edgeKind(g, e))) continue;
                    if (SimLights.roomAt(state, x + dx, y + dy)?.key == s.space) continue;
                    // The wall's plane between the tiles, the pane a little inside it, facing out (+dx, +dy).
                    var c = new Vector3(x + 0.5f + dx * 0.44f, floor + SILL + WorldMeshWindow / 2, y + 0.5f + dy * 0.44f);
                    var basis = Basis.LookingAt(new Vector3(-dx, 0, -dy), Vector3.Up);
                    pm.SetInstanceTransform(p++, new Transform3D(basis, c));
                }
            }
    }

    static readonly (int, int)[] DIR4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    // --- the light bench (--lightbench=0,8,16,32 or N:K for K shadowed): frame times with each budget, then quit ---

    static List<(int n, int k)>? benchRuns;
    int benchRun = -1, benchFrame;
    double benchFrameMs, benchGpu, benchCpu;
    readonly List<string> benchOut = new();
    const int BENCH_WARM = 90, BENCH_FRAMES = 300;

    public static void Bench(string runs)
    {
        benchRuns = new();
        foreach (var r in runs.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var nk = r.Split(':');
            int n = int.Parse(nk[0]);
            benchRuns.Add((n, nk.Length > 1 ? int.Parse(nk[1]) : Math.Min(n, Shadowed)));
        }
    }

    void BenchStep(double delta)
    {
        var vp = parent.GetViewport().GetViewportRid();
        if (benchRun < 0)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            RenderingServer.ViewportSetMeasureRenderTime(vp, true);
            benchOut.Add($"lightbench: {nearList.Count} sources within {RANGE} m, {farList.Count} out to {FAR} m");
            benchOut.Add("| lights | shadowed | lit | frame ms | GPU ms | CPU render ms |");
            benchOut.Add("|---|---|---|---|---|---|");
            Next();
            return;
        }
        benchFrame++;
        if (benchFrame <= BENCH_WARM) return;
        benchFrameMs += delta * 1000;
        benchGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        benchCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
        if (benchFrame < BENCH_WARM + BENCH_FRAMES) return;
        var (n, k) = benchRuns![benchRun];
        double f = BENCH_FRAMES;
        benchOut.Add($"| {n} | {k} | {Lit} ({Shadows} shadowed) | {benchFrameMs / f:0.00} | {benchGpu / f:0.00} | {benchCpu / f:0.00} |");
        Next();
    }

    void Next()
    {
        benchRun++;
        benchFrame = 0;
        benchFrameMs = benchGpu = benchCpu = 0;
        if (benchRun >= benchRuns!.Count)
        {
            foreach (var l in benchOut) GD.Print(l);
            benchRuns = null;
            parent.GetTree().Quit();
            return;
        }
        (Budget, Shadowed) = benchRuns[benchRun];
    }
}
