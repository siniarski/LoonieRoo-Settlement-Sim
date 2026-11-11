// Weather in first person (2026-11-10): rain and snow falling round the camera, a layer of snow on open ground
// that follows the sim's depth (Snow.cs) with trodden trails and car ruts, an overcast sky, and two global
// shader parameters others can read. Reads state only (rule 5).
//
//   falling   one GPUParticles3D for rain, one for snow, boxes that follow the camera (the particles stay in the
//             world, so you walk through them). Not emitting indoors or under a roof (Roofs.Indoor, the sim's
//             Rooms), and the ones already falling run out. A blizzard is more flakes and a hard wind.
//   the layer one mesh per WorldMesh.CHUNK tiles near you, built only when its tiles' depth changes (a signature of
//             the three region depths and the tracks in the chunk, a few chunks a frame at most) and none at all
//             while no snow lies. Each tile's thickness is the sim's depthAt; a corner takes the mean of the
//             snowy tiles round it, so a trodden lane has sloping sides; its normals come from those heights.
//             It sits on Terrain.Corner (the ground's relief). Trodden tiles are tinted darker, a car's tile is cut
//             into two ruts along its heading. Not on walls, water or under a ceiling.
//   sky       the sky, sun and fog follow the day's weather (Overcast), eased, through the one hook FirstPerson
//             calls after its own lighting: it scales what the lighting set, so lighting and weather don't fight.
//   globals   `snow_cover` (0 bare .. 1 deep snow, round you) and `weather_wet` (0 dry .. 1 raining), floats in
//             RenderingServer's global shader parameters. Nothing of the roofs' or the trees' materials is
//             touched: a material adopts them with `global uniform float snow_cover;` (godot/assets/shaders/
//             snow_cover.gdshaderinc has a helper that whitens an upward-facing surface by it).
// `WEATHER_STATS=1` prints the layer's cost (ms a frame, in this class's Update).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.World3D;

public sealed partial class WeatherLayer : Node3D
{
    const int CHUNK = WorldMesh.CHUNK;
    /// <summary>Snow-layer chunks built per frame at most.</summary>
    const int BUILDS_PER_FRAME = 2;
    /// <summary>Metres of snow in the layer per cm of the sim's depth (a compression: 80 cm is waist-high), and a lift over the ground so it never z-fights.</summary>
    const float METRES_PER_CM = 0.006f, LIFT = 0.012f;
    /// <summary>How far a wheel's rut is cut below its tile's snow (m).</summary>
    const float RUT_DEPTH = 0.025f;
    /// <summary>The cover (0..1) reaches full at this depth (cm).</summary>
    const double COVER_FULL = 12;
    static readonly Color SNOW = new(0.8f, 0.82f, 0.88f), TROD = new(0.5f, 0.54f, 0.62f), LANE = new(0.42f, 0.46f, 0.54f);
    /// <summary>A trodden tile's dip is drawn this many times as deep as the sim's, so a trail reads from a distance.</summary>
    const float CARVE_SHOW = 2.2f;

    readonly GpuParticles3D rain, snowfall;
    readonly Node3D layer = new();
    readonly Dictionary<(int, int), (MeshInstance3D node, long sig)> chunks = new();
    readonly StandardMaterial3D snowMat = new() { VertexColorUseAsAlbedo = true, Roughness = 0.85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    readonly bool stats = System.Environment.GetEnvironmentVariable("WEATHER_STATS") == "1";
    /// <summary>WEATHER_OFF=1: draw no weather at all (to measure its cost against it).</summary>
    readonly bool off = System.Environment.GetEnvironmentVariable("WEATHER_OFF") == "1";
    double renderCpu, renderGpu, frameMs, buildMs, worstBuild;
    int builds;
    float cloud, cover, wet;
    /// <summary>Region depths and the tracks per chunk, looked at every few frames.</summary>
    Dictionary<(int, int), long> trackSig = new();
    long depthSig;
    int frame;
    double ms;
    int measured;

    public WeatherLayer()
    {
        AddChild(layer);
        rain = Falling(true);
        snowfall = Falling(false);
        AddChild(rain);
        AddChild(snowfall);
        if (!declared)
        {
            declared = true;  // once a process (GlobalShaderParameterGetList is editor-only, so we cannot ask)
            RenderingServer.GlobalShaderParameterAdd("snow_cover", RenderingServer.GlobalShaderParameterType.Float, 0f);
            RenderingServer.GlobalShaderParameterAdd("weather_wet", RenderingServer.GlobalShaderParameterType.Float, 0f);
        }
    }

    static bool declared;

    /// <summary>A box of rain or snow that follows the camera. Rain: thin vertical streaks, fast. Snow: flakes, slow, drifting.</summary>
    static GpuParticles3D Falling(bool rain)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = rain ? new Vector3(15, 0.1f, 15) : new Vector3(14, 0.1f, 14),
            Direction = Vector3.Down,
            Spread = rain ? 2f : 25f,
            InitialVelocityMin = rain ? 10f : 0.6f,
            InitialVelocityMax = rain ? 12f : 1.4f,
            Gravity = rain ? new Vector3(0, -6, 0) : new Vector3(0, -0.6f, 0),
        };
        if (!rain)
        {
            pm.TurbulenceEnabled = true;
            pm.TurbulenceNoiseStrength = 0.8f;
            pm.TurbulenceNoiseScale = 2.5f;
            pm.TurbulenceInfluenceMin = 0.05f;
            pm.TurbulenceInfluenceMax = 0.2f;
        }
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = rain ? new Color(0.78f, 0.84f, 0.95f, 0.4f) : new Color(1, 1, 1, 0.9f),
            BillboardMode = rain ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
        };
        return new GpuParticles3D
        {
            Amount = rain ? 3200 : 7000,
            Lifetime = rain ? 0.9 : 9,
            ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = rain ? new Vector2(0.012f, 0.55f) : new Vector2(0.055f, 0.055f), Material = mat },
            LocalCoords = false,
            CustomAabb = new Aabb(new Vector3(-20, -14, -20), new Vector3(40, 28, 40)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
            Preprocess = rain ? 0.9 : 9,
        };
    }

    /// <summary>The one hook: called by FirstPerson after its lighting and its fog, once a frame.</summary>
    /// <summary>The world is held this frame (the menu, the map, --paused): rain and snow stop where they are.</summary>
    public bool Held;

    public void Update(SimState state, Vector3 eye, double delta, HashSet<int> indoor, DirectionalLight3D sun, Godot.Environment env)
    {
        var t0 = stats ? Stopwatch.GetTimestamp() : 0;
        if (off)
        {
            if (stats) Report(state, delta, 0);
            return;
        }
        frame++;
        var w = state.weather;
        int tile = (int)Math.Floor(eye.Z) * state.grid.width + (int)Math.Floor(eye.X);
        bool roofed = indoor.Contains(tile) || Rooms.isIndoors(state, (int)Math.Floor(eye.X), (int)Math.Floor(eye.Z));
        bool raining = w == "rain" || w == "storm";
        bool snowing = w == "snow" || w == "blizzard";
        // Rain and snow: a box over the camera, thicker in a storm or a blizzard.
        rain.GlobalPosition = eye + new Vector3(0, 7, 0);
        snowfall.GlobalPosition = eye + new Vector3(0, 6, 0);
        // Held (the world paused): the drops and flakes stop in the air, as everything else does.
        rain.SpeedScale = snowfall.SpeedScale = Held ? 0 : 1;
        rain.Emitting = raining && !roofed;
        rain.AmountRatio = w == "storm" ? 1f : 0.55f;
        snowfall.Emitting = snowing && !roofed;
        snowfall.AmountRatio = w == "blizzard" ? 1f : 0.4f;
        ((ParticleProcessMaterial)snowfall.ProcessMaterial).Gravity = w == "blizzard" ? new Vector3(5.5f, -1.4f, 1.5f) : new Vector3(0.4f, -0.6f, 0.2f);
        ((ParticleProcessMaterial)rain.ProcessMaterial).Gravity = w == "storm" ? new Vector3(4, -6, 1) : new Vector3(0.6f, -6, 0.2f);
        Overcast(w, delta, sun, env);
        Layer(state, eye, delta, indoor);
        if (stats) Report(state, delta, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
        if (stats && builds > 0 && frame % 120 == 0)
        {
            GD.Print($"weather: {builds} snow chunks built, {buildMs / builds:0.00} ms each, worst {worstBuild:0.00} ms");
            builds = 0;
            buildMs = worstBuild = 0;
        }
    }

    /// <summary>WEATHER_STATS=1: every 120 frames, this class's cost, and the viewport's render times.</summary>
    void Report(SimState state, double delta, double updateMs)
    {
        ms += updateMs;
        var vp = GetViewport().GetViewportRid();
        renderCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
        renderGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        frameMs += delta * 1000;
        if (++measured == 120)
        {
            GD.Print($"weather: {ms / measured:0.000} ms a frame in WeatherLayer.Update; frame {frameMs / measured:0.00} ms, render cpu {renderCpu / measured:0.00} gpu {renderGpu / measured:0.00} ms ({chunks.Count} snow chunks, {state.snow?.tracks.Count ?? 0} tracks, {state.weather}, off {off})");
            ms = renderCpu = renderGpu = frameMs = 0;
            measured = 0;
        }
    }

    // ---- sky ----

    /// <summary>How overcast each weather is (0 clear sky .. 1 solid dark cloud), and how much fog it brings.</summary>
    static (float cloud, float fog) Look(string weather) => weather switch
    {
        "rain" => (0.55f, 0.6f),
        "storm" => (0.9f, 1.2f),
        "snow" => (0.65f, 1.0f),
        "blizzard" => (0.95f, 3.0f),
        "heatwave" => (0f, 0.15f),
        _ => (0f, 0f),
    };

    float fogK;

    /// <summary>Overcast sky, dimmer sun, greyer ambient, nearer fog: eased towards the weather's look, applied on top of what the lighting set this frame.</summary>
    void Overcast(string weather, double delta, DirectionalLight3D sun, Godot.Environment env)
    {
        var (want, fog) = Look(weather);
        float k = (float)Math.Min(1, delta * 0.25);
        cloud += (want - cloud) * k;
        fogK += (fog - fogK) * k;
        if (cloud < 0.002f && Math.Abs(fogK) < 0.002f && cover <= 0.002f) return;
        sun.LightEnergy *= 1 - 0.6f * cloud;
        sun.LightColor = sun.LightColor.Lerp(new Color(0.8f, 0.84f, 0.9f), 0.6f * cloud);
        var sky = env.BackgroundColor;
        float l = sky.Luminance;
        var grey = new Color(l * 0.96f, l * 1.0f, l * 1.05f);
        env.BackgroundColor = sky.Lerp(grey, 0.85f * cloud).Darkened(0.12f * cloud);
        env.FogLightColor = env.BackgroundColor;
        env.AmbientLightColor = env.AmbientLightColor.Lerp(new Color(l * 0.9f + 0.1f, l * 0.95f + 0.1f, l * 1.0f + 0.12f), 0.5f * cloud);
        env.AmbientLightEnergy *= 1 - 0.15f * cloud + 0.35f * cover;  // a snowy ground bounces light back up
        env.FogDensity *= 1 + 1.8f * fogK;
    }

    // ---- the layer ----

    static float Thick(double cm) => cm < Snow.MIN_LYING ? 0f : (float)(cm * METRES_PER_CM);

    void Layer(SimState state, Vector3 eye, double delta, HashSet<int> indoor)
    {
        var sn = state.snow;
        bool any = sn != null && Snow.lying(sn);
        float wantWet = (state.weather == "rain" || state.weather == "storm") ? 1f : 0f;
        wet += (wantWet - wet) * (float)Math.Min(1, delta * 0.3);
        RenderingServer.GlobalShaderParameterSet("weather_wet", wet);
        if (!any)
        {
            cover += (0 - cover) * (float)Math.Min(1, delta * 0.5);
            RenderingServer.GlobalShaderParameterSet("snow_cover", cover);
            if (chunks.Count > 0) Clear();
            return;
        }
        int ex = (int)Math.Floor(eye.X), ez = (int)Math.Floor(eye.Z);
        var here = Math.Min(1, Snow.openDepth(state, ex, ez) / COVER_FULL);
        cover += ((float)here - cover) * (float)Math.Min(1, delta * 0.5);
        RenderingServer.GlobalShaderParameterSet("snow_cover", cover);
        // Every few frames: what the chunks' signatures hold.
        if (frame % 6 == 1) Gather(sn!);
        int ccx = (int)Math.Floor(eye.X / CHUNK), ccy = (int)Math.Floor(eye.Z / CHUNK);
        int r = Math.Min(FirstPerson.Radius, 3);
        int maxX = (state.grid.width - 1) / CHUNK, maxY = (state.grid.height - 1) / CHUNK;
        var want = new List<(int d, int x, int y)>();
        for (int y = Math.Max(0, ccy - r); y <= Math.Min(maxY, ccy + r); y++)
            for (int x = Math.Max(0, ccx - r); x <= Math.Min(maxX, ccx + r); x++)
                want.Add((Math.Max(Math.Abs(x - ccx), Math.Abs(y - ccy)), x, y));
        want.Sort((a, b) => a.d.CompareTo(b.d));
        int built = 0;
        foreach (var (_, x, y) in want)
        {
            long sig = depthSig * 31 + indoor.Count;
            if (trackSig.TryGetValue((x, y), out var ts)) sig = sig * 31 + ts;
            bool have = chunks.TryGetValue((x, y), out var c);
            if (have && c.sig == sig) continue;
            if (built >= BUILDS_PER_FRAME) break;
            var tb = stats ? Stopwatch.GetTimestamp() : 0;
            var mesh = Build(state, x, y, indoor);
            if (stats)
            {
                var took = (Stopwatch.GetTimestamp() - tb) * 1000.0 / Stopwatch.Frequency;
                builds++;
                buildMs += took;
                worstBuild = Math.Max(worstBuild, took);
            }
            if (!have)
            {
                c.node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                layer.AddChild(c.node);
            }
            c.node.Mesh = mesh;
            chunks[(x, y)] = (c.node, sig);
            built++;
        }
        var drop = new List<(int, int)>();
        foreach (var (k, c) in chunks)
            if (Math.Max(Math.Abs(k.Item1 - ccx), Math.Abs(k.Item2 - ccy)) > r + 1) drop.Add(k);
        foreach (var k in drop)
        {
            chunks[k].node.QueueFree();
            chunks.Remove(k);
        }
    }

    void Clear()
    {
        foreach (var (_, c) in chunks) c.node.QueueFree();
        chunks.Clear();
        trackSig.Clear();
    }

    /// <summary>The three regions' depths (to 2 cm) and, per chunk, a hash of its tracks (tile, depth to 0.5 cm, ruts).</summary>
    void Gather(SnowState sn)
    {
        depthSig = (long)(sn.depth[0] / 2) * 1009 + (long)(sn.depth[1] / 2) * 31 + (long)(sn.depth[2] / 2);
        trackSig = new();
        foreach (var t in sn.tracks)
        {
            var k = (t.x / CHUNK, t.y / CHUNK);
            // Order-free (a sum): a track's depth now, which moves as snow falls and melts.
            long h = (t.x * 73856093L) ^ (t.y * 19349663L) ^ ((long)(Snow.trackDepth(sn, t) * 2) * 83492791L) ^ (t.k * 2654435761L) ^ t.n;
            trackSig[k] = trackSig.GetValueOrDefault(k) + h;
        }
    }

    /// <summary>One chunk of snow: its tiles' thickness from the sim, joined at the corners.</summary>
    ArrayMesh Build(SimState state, int cx, int cy, HashSet<int> indoor)
    {
        var g = state.grid;
        int x0 = cx * CHUNK, y0 = cy * CHUNK, n = CHUNK + 2;
        // Tiles x0-1 .. x0+CHUNK (a ring round the chunk, so corners on its edge agree with its neighbours').
        var depth = new double[n, n];
        var snowable = new bool[n, n];
        var thick = new float[n, n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int x = x0 + i - 1, y = y0 + j - 1;
                if (x < 0 || y < 0 || x >= g.width || y >= g.height) continue;
                int idx = y * g.width + x;
                string? tile = Grids.tileAt(g, idx);
                if (tile == "wall" || tile == "water" || indoor.Contains(idx)) continue;
                snowable[i, j] = true;
                depth[i, j] = Snow.depthAt(state, x, y);
                float v = Thick(depth[i, j]);
                if (v > 0 && Snow.find(state.snow!, x, y) != null)
                {
                    // Trodden: how far under the snow beside it, exaggerated.
                    float o = Thick(Snow.openDepth(state, x, y));
                    v = Math.Max(0.004f, o - CARVE_SHOW * (o - v));
                }
                thick[i, j] = v;
            }
        // A corner's thickness: the mean of the snowy tiles that meet at it.
        var corner = new float[CHUNK + 1, CHUNK + 1];
        var height = new float[CHUNK + 1, CHUNK + 1];
        for (int j = 0; j <= CHUNK; j++)
            for (int i = 0; i <= CHUNK; i++)
            {
                float sum = 0;
                int cnt = 0;
                for (int dj = 0; dj < 2; dj++)
                    for (int di = 0; di < 2; di++)
                    {
                        int ti = i + di, tj = j + dj; // tile (x0 + i + di - 1, y0 + j + dj - 1): the four round corner (x0 + i, y0 + j)
                        if (!snowable[ti, tj]) continue;
                        sum += thick[ti, tj];
                        cnt++;
                    }
                corner[i, j] = cnt == 0 ? 0 : sum / cnt;
                height[i, j] = Terrain.Corner(x0 + i, y0 + j) + LIFT + corner[i, j];
            }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 P(int i, int j) => new(x0 + i, height[i, j], y0 + j);
        Vector3 Nrm(int i, int j)
        {
            int i0 = Math.Max(0, i - 1), i1 = Math.Min(CHUNK, i + 1), j0 = Math.Max(0, j - 1), j1 = Math.Min(CHUNK, j + 1);
            float dx = (height[i1, j] - height[i0, j]) / (i1 - i0), dz = (height[i, j1] - height[i, j0]) / (j1 - j0);
            return new Vector3(-dx, 1, -dz).Normalized();
        }
        void V(int i, int j, Color c) { st.SetColor(c); st.SetNormal(Nrm(i, j)); st.AddVertex(P(i, j)); }
        var ruts = new Dictionary<(int, int), SnowTrack>();
        var sn = state.snow!;
        foreach (var t in sn.tracks)
            if (t.k > 0 && t.x >= x0 && t.x < x0 + CHUNK && t.y >= y0 && t.y < y0 + CHUNK) ruts[(t.x, t.y)] = t;
        for (int j = 0; j < CHUNK; j++)
            for (int i = 0; i < CHUNK; i++)
            {
                double d = depth[i + 1, j + 1];
                if (d < Snow.MIN_LYING || !snowable[i + 1, j + 1]) continue;
                int x = x0 + i, y = y0 + j;
                double open = Snow.openDepth(state, x, y);
                float carved = open > 0 ? (float)Math.Clamp(1 - d / open, 0, 1) : 0;
                var tr = Snow.find(sn, x, y);
                var baseColour = SNOW.Lerp((tr != null && tr.n >= Snow.LANE_N) ? LANE : TROD, Math.Min(1f, carved * 2.6f));
                var cc = baseColour.Lightened(((x * 73856093) ^ (y * 19349663)) % 100 / 100f * 0.05f);
                if (ruts.TryGetValue((x, y), out var rut) && carved > 0.05f)
                {
                    Ruts(st, i, j, x0, y0, height, rut, cc);
                    continue;
                }
                // Front faces clockwise from above: the diagonal splits it the way the corners' heights run.
                V(i, j, cc); V(i + 1, j, cc); V(i + 1, j + 1, cc);
                V(i, j, cc); V(i + 1, j + 1, cc); V(i, j + 1, cc);
            }
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, snowMat);
        return mesh;
    }

    /// <summary>A car's tile: four strips along its heading, cut down along the lines a quarter in from each side (the wheels' ruts), darker.</summary>
    static void Ruts(SurfaceTool st, int i, int j, int x0, int y0, float[,] height, SnowTrack rut, Color c)
    {
        var dir = Vehicles.HEADING_DIRS[(rut.k - 1) % Vehicles.HEADING_DIRS.Count];
        bool alongX = Math.Abs(dir[0]) >= Math.Abs(dir[1]);
        Vector3 a = new(x0 + i, height[i, j], y0 + j), b = new(x0 + i + 1, height[i + 1, j], y0 + j);
        Vector3 d = new(x0 + i, height[i, j + 1], y0 + j + 1), e = new(x0 + i + 1, height[i + 1, j + 1], y0 + j + 1);
        // along: 0..1 down the heading; across: 0..1 over the width. Dips along the lines across = 1/4 and 3/4.
        Vector3 Pt(float along, float across, bool dip)
        {
            float u = alongX ? along : across, v = alongX ? across : along;
            var p = ((a * (1 - u)) + (b * u)) * (1 - v) + ((d * (1 - u)) + (e * u)) * v;
            return dip ? p - new Vector3(0, RUT_DEPTH, 0) : p;
        }
        var dark = c.Darkened(0.15f);
        void Tri(Vector3 p, Vector3 q, Vector3 r, Color col)
        {
            foreach (var v in new[] { p, q, r }) { st.SetColor(col); st.SetNormal(Vector3.Up); st.AddVertex(v); }
        }
        for (int s = 0; s < 4; s++)
        {
            float v0 = s * 0.25f, v1 = (s + 1) * 0.25f;
            bool dip0 = s == 1 || s == 3, dip1 = s == 0 || s == 2;
            var p00 = Pt(0, v0, dip0); var p10 = Pt(1, v0, dip0); var p01 = Pt(0, v1, dip1); var p11 = Pt(1, v1, dip1);
            var col = c;
            if (dip0 || dip1) col = dark;
            Tri(p00, p10, p11, col);
            Tri(p00, p11, p01, col);
        }
    }
}
