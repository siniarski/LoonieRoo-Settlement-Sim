// Grass, weeds, bushes, saplings and the cracks and tufts on old roads, by growth stage (cs/Sim/Systems/Growth.cs).
// Reads state only (rule 5): every tile's cover is the sim's pure function of the tick, so nothing here is saved or
// stepped. Drawn by chunk (WorldMesh.CHUNK tiles) as MultiMeshes, never a node per tile or per tuft:
//   grass tufts and weeds  one MultiMesh each per chunk (a tuft is a handful of blade triangles, procedural)
//   bushes                 Trees' bush model, one MultiMesh per chunk
//   saplings               Trees' own tree models at a small scale, one MultiMesh per chunk and species
//   cracks                 flat dark strips on road, pavement and ruined floor, one MultiMesh per chunk
//   road tufts             the grass tuft again, leaning to the edges of the road
// The budget is fixed: the chunks within RADIUS of yours (25), at most MAX_TUFTS_PER_CHUNK tufts in each, drawn to
// RANGE metres; two chunks are built a frame at most, and a chunk is rebuilt only when what it shows changed (its
// signature: the growth quantised to a quarter of a growing day, the season, what stands in it). Everything stands
// at the height Terrain reports.
using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;
using Time = LoonieRoo.Sim.Time;

namespace LoonieRoo.Game.World3D;

public sealed class Undergrowth
{
    const int CHUNK = WorldMesh.CHUNK;
    /// <summary>Chunks this far from yours are drawn: 25 chunks of 16 x 16 tiles, ~40 m each way at most.</summary>
    public static int Radius = 2;
    const int BUILDS_PER_FRAME = 2;
    const int MAX_TUFTS_PER_CHUNK = 1800;
    /// <summary>How far (m) each kind is drawn, with a margin: grass the nearest, bushes and saplings further.</summary>
    const float GRASS_RANGE = 26, WEED_RANGE = 34, BUSH_RANGE = 44, CRACK_RANGE = 30, SAPLING_RANGE = 60, MARGIN = 3;
    /// <summary>Growth quantised to this many growing days for a chunk's signature (a quarter day: a few minutes at 1x).</summary>
    const double GROWTH_STEP = 0.25;

    readonly Node3D parent;
    readonly Trees trees;
    readonly Mesh grassMesh, weedMesh, crackMesh;
    readonly ShaderMaterial blades;
    readonly StandardMaterial3D crackMat;
    readonly Dictionary<(int, int), (long sig, List<Node3D> nodes, int count)> chunks = new();
    int frame;
    public int Count { get; private set; }
    /// <summary>Chunks rebuilt in the last second or so (the debug panel and the bench).</summary>
    public int Builds { get; private set; }
    public double BuildMs { get; private set; }

    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road"), PAVING = Array.IndexOf(Grids.GROUNDS, "paving"), BRIDGE = Array.IndexOf(Grids.GROUNDS, "bridge");

    public Undergrowth(Node3D parent, Trees trees)
    {
        this.parent = parent;
        this.trees = trees;
        grassMesh = Tuft(7, 0.62f, 0.035f, 0.18f, 0.26f, 0.9f);
        weedMesh = Weed();
        crackMesh = Crack();
        blades = new ShaderMaterial { Shader = new Shader { Code = BLADES } };
        crackMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex };
        grassMesh.SurfaceSetMaterial(0, blades);
        weedMesh.SurfaceSetMaterial(0, blades);
        crackMesh.SurfaceSetMaterial(0, crackMat);
    }

    // ---------- the meshes ----------

    static uint Seed = 12345;
    static float Rnd() { Seed = Seed * 1664525u + 1013904223u; return (Seed >> 8) / 16777216f; }

    /// <summary>A tuft of blades, 1 m tall and about 0.3 m across: each blade a triangle leaning out from the middle.</summary>
    static ArrayMesh Tuft(int blades, float lean, float width, float baseShade, float tipShade, float spread)
    {
        var v = new List<Vector3>(); var c = new List<Color>(); var n = new List<Vector3>();
        Seed = 4242;
        for (int b = 0; b < blades; b++)
        {
            float a = Rnd() * Mathf.Tau, r = 0.02f + 0.08f * Rnd() * spread, h = 0.65f + 0.35f * Rnd();
            float la = a + (Rnd() - 0.5f) * 1.2f, l = lean * (0.3f + 0.7f * Rnd()) * h;
            var bp = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            var side = new Vector3(-Mathf.Sin(la), 0, Mathf.Cos(la)) * width * (0.8f + 0.5f * Rnd());
            var tip = bp + new Vector3(Mathf.Cos(la) * l, h, Mathf.Sin(la) * l);
            var mid = bp + new Vector3(Mathf.Cos(la) * l * 0.25f, h * 0.5f, Mathf.Sin(la) * l * 0.25f);
            float s = 0.85f + 0.3f * Rnd();
            Color lo = new(baseShade * s, baseShade * s * 1.15f, baseShade * s * 0.6f), hi = new(tipShade * s * 1.05f, tipShade * s * 1.3f, tipShade * s * 0.7f);
            // Two triangles a blade: wide at the foot, a point at the top, a bend in the middle.
            v.Add(bp - side); v.Add(bp + side); v.Add(mid + side * 0.6f);
            v.Add(bp - side); v.Add(mid + side * 0.6f); v.Add(mid - side * 0.6f);
            v.Add(mid - side * 0.6f); v.Add(mid + side * 0.6f); v.Add(tip);
            for (int k = 0; k < 9; k++) { n.Add(Vector3.Up); }
            c.AddRange(new[] { lo, lo, Mix(lo, hi, 0.5f), lo, Mix(lo, hi, 0.5f), Mix(lo, hi, 0.5f), Mix(lo, hi, 0.5f), Mix(lo, hi, 0.5f), hi });
        }
        return Make(v, n, c);
    }

    static Color Mix(Color a, Color b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    /// <summary>A weed: broad leaves fanning out from a rosette and a stem or two with seed heads (1 m tall, ~0.5 m across).</summary>
    static ArrayMesh Weed()
    {
        var v = new List<Vector3>(); var c = new List<Color>(); var n = new List<Vector3>();
        Seed = 777;
        for (int b = 0; b < 6; b++)
        {
            float a = b * Mathf.Tau / 6 + Rnd() * 0.6f, h = 0.5f + 0.4f * Rnd(), l = 0.22f + 0.16f * Rnd(), w = 0.07f + 0.04f * Rnd();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var sideV = new Vector3(-dir.Z, 0, dir.X);
            var bp = dir * 0.03f; var mid = bp + dir * l * 0.6f + Vector3.Up * h * 0.55f; var tip = bp + dir * l + Vector3.Up * h;
            Color lo = new(0.12f, 0.17f, 0.07f), hi = new(0.30f, 0.38f, 0.14f);
            v.Add(bp); v.Add(mid - sideV * w); v.Add(mid + sideV * w);
            v.Add(mid - sideV * w); v.Add(tip); v.Add(mid + sideV * w);
            c.AddRange(new[] { lo, hi, hi, hi, Mix(hi, lo, 0.2f), hi });
            for (int k = 0; k < 6; k++) n.Add(Vector3.Up);
        }
        for (int s = 0; s < 3; s++)
        {
            float a = Rnd() * Mathf.Tau, r = 0.04f + 0.1f * Rnd(), h = 0.8f + 0.2f * Rnd();
            var bp = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); var top = bp + new Vector3(Mathf.Cos(a) * 0.05f, h, Mathf.Sin(a) * 0.05f);
            var side = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.012f;
            Color stem = new(0.20f, 0.24f, 0.10f), seed = new(0.36f, 0.30f, 0.16f);
            v.Add(bp - side); v.Add(bp + side); v.Add(top); c.AddRange(new[] { stem, stem, Mix(stem, seed, 0.6f) }); for (int k = 0; k < 3; k++) n.Add(Vector3.Up);
            // The seed head: a small diamond.
            var hd = top - new Vector3(0, 0.07f, 0); var hs = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.025f;
            v.Add(hd - hs); v.Add(hd + hs); v.Add(top + new Vector3(0, 0.05f, 0)); c.AddRange(new[] { seed, seed, seed }); for (int k = 0; k < 3; k++) n.Add(Vector3.Up);
        }
        return Make(v, n, c);
    }

    /// <summary>A crack in paving, 1 m long, flat: a zigzag strip with a branch, dark.</summary>
    static ArrayMesh Crack()
    {
        var v = new List<Vector3>(); var c = new List<Color>(); var n = new List<Vector3>();
        Seed = 99;
        void Strip(Vector3 from, Vector3 dir, int segs, float w0)
        {
            var p = from; var perp = new Vector3(-dir.Z, 0, dir.X);
            for (int s = 0; s < segs; s++)
            {
                float step = 1f / segs, w = w0 * (1 - 0.7f * s / segs);
                var q = p + dir * step + perp * (Rnd() - 0.5f) * 0.12f;
                var d = (q - p).Normalized(); var sd = new Vector3(-d.Z, 0, d.X) * w;
                v.Add(p - sd); v.Add(p + sd); v.Add(q + sd);
                v.Add(p - sd); v.Add(q + sd); v.Add(q - sd);
                var col = new Color(0.035f, 0.035f, 0.03f);
                for (int k = 0; k < 6; k++) { c.Add(col); n.Add(Vector3.Up); }
                p = q;
            }
        }
        Strip(new Vector3(-0.5f, 0, 0), Vector3.Right, 9, 0.022f);
        Strip(new Vector3(-0.1f, 0, 0.02f), new Vector3(0.45f, 0, 0.9f).Normalized(), 4, 0.014f);
        return Make(v, n, c);
    }

    static ArrayMesh Make(List<Vector3> v, List<Vector3> n, List<Color> c)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = n.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = c.ToArray();
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return m;
    }

    // ---------- the frame ----------

    public void Update(SimState state, Vector3 eye, WorldMesh.Lookups look)
    {
        var g = state.grid;
        int ccx = (int)Math.Floor(eye.X / CHUNK), ccy = (int)Math.Floor(eye.Z / CHUNK);
        int maxX = (g.width - 1) / CHUNK, maxY = (g.height - 1) / CHUNK;
        bool check = ++frame % 20 == 0;
        var want = new List<(int d, int x, int y)>();
        for (int y = Math.Max(0, ccy - Radius); y <= Math.Min(maxY, ccy + Radius); y++)
            for (int x = Math.Max(0, ccx - Radius); x <= Math.Min(maxX, ccx + Radius); x++)
                want.Add((Math.Max(Math.Abs(x - ccx), Math.Abs(y - ccy)), x, y));
        want.Sort((a, b) => a.d.CompareTo(b.d));
        int built = 0;
        double gNow = Growth.G(state, state.tick);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var (_, x, y) in want)
        {
            bool have = chunks.TryGetValue((x, y), out var c);
            if (have && !check) continue;
            if (built >= BUILDS_PER_FRAME) break;
            long sig = Signature(state, x, y, gNow, look);
            if (have && c.sig == sig) continue;
            if (have) foreach (var n in c.nodes) n.QueueFree();
            var (nodes, count) = Build(state, x, y, gNow, look);
            chunks[(x, y)] = (sig, nodes, count);
            built++;
        }
        if (built > 0) { BuildMs = BuildMs * 0.8 + (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / built * 0.2; Builds += built; }
        var drop = new List<(int, int)>();
        foreach (var (k, c) in chunks)
            if (Math.Max(Math.Abs(k.Item1 - ccx), Math.Abs(k.Item2 - ccy)) > Radius + 1) drop.Add(k);
        foreach (var k in drop) { foreach (var n in chunks[k].nodes) n.QueueFree(); chunks.Remove(k); }
        int total = 0;
        foreach (var c in chunks.Values) total += c.count;
        Count = total;
    }

    static long Signature(SimState state, int cx, int cy, double gNow, WorldMesh.Lookups look)
    {
        long h = 17;
        h = h * 31 + (long)Math.Floor(gNow / GROWTH_STEP);
        h = h * 31 + Growth.version(state);
        h = h * 31 + (int)Seasons.SEASONS.IndexOf(Seasons.seasonOf(state.tick));
        if (look.furniture.TryGetValue((cx, cy), out var list)) h = h * 31 + list.Count;
        return h;
    }

    /// <summary>The grass, weeds and road tufts' colours (tint, about 1) by season: green in spring and summer, going to straw in autumn and winter.</summary>
    static (Color grass, Color weed) Tints(SimState state)
    {
        switch (Seasons.seasonOf(state.tick))
        {
            case "autumn": return (new Color(1.15f, 0.95f, 0.62f), new Color(1.05f, 0.85f, 0.55f));
            case "winter": return (new Color(1.2f, 1.05f, 0.82f), new Color(1.05f, 0.9f, 0.7f));
            default: return (new Color(1f, 1f, 1f), new Color(1f, 1f, 1f));
        }
    }

    (List<Node3D>, int) Build(SimState state, int cx, int cy, double gNow, WorldMesh.Lookups look)
    {
        var g = state.grid;
        var grass = new List<float>(); var weeds = new List<float>(); var bushes = new List<float>(); var cracks = new List<float>();
        var saplings = new List<float>[5]; for (int i = 0; i < saplings.Length; i++) saplings[i] = new();
        var taken = new HashSet<int>();
        if (look.furniture.TryGetValue((cx, cy), out var list)) foreach (var f in list) taken.Add(f.y * g.width + f.x);
        var (grassTint, weedTint) = Tints(state);
        int x0 = cx * CHUNK, y0 = cy * CHUNK, budget = MAX_TUFTS_PER_CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                if (g.tiles[i] != Grids.FLOOR || look.indoor.Contains(i) || taken.Contains(i)) continue;
                var c = Growth.coverWith(state, x, y, gNow);
                if (c.kind == GrowthKind.None) continue;
                if (c.grass > 0 && budget > 0)
                {
                    int n = (int)(c.grass * 4.0 + R(state, x, y, 0));
                    for (int k = 0; k < n && budget > 0; k++, budget--)
                    {
                        float h = (0.09f + 0.40f * (float)c.grass) * (0.8f + 0.4f * R(state, x, y, 10 + k));
                        Add(grass, x + R(state, x, y, 20 + k), y + R(state, x, y, 30 + k), h, 0.8f + 0.6f * R(state, x, y, 40 + k), R(state, x, y, 50 + k), Jitter(grassTint, R(state, x, y, 60 + k)), R(state, x, y, 70 + k));
                    }
                }
                if (c.weeds > 0 && budget > 0)
                {
                    int n = (int)(c.weeds * 2.2 + R(state, x, y, 1));
                    var tint = c.kind == GrowthKind.Forest ? new Color(0.8f, 0.85f, 0.7f) : weedTint;
                    for (int k = 0; k < n && budget > 0; k++, budget--)
                    {
                        float h = (0.22f + 0.5f * (float)c.weeds) * (0.75f + 0.5f * R(state, x, y, 110 + k));
                        Add(weeds, x + R(state, x, y, 120 + k), y + R(state, x, y, 130 + k), h, 0.8f + 0.5f * R(state, x, y, 140 + k), R(state, x, y, 150 + k), Jitter(tint, R(state, x, y, 160 + k)), R(state, x, y, 170 + k));
                    }
                }
                if (c.bush > 0.12)
                {
                    float s = 0.3f + 0.9f * (float)c.bush;
                    Add(bushes, x + 0.2f + 0.6f * R(state, x, y, 210), y + 0.2f + 0.6f * R(state, x, y, 211), s, 0.9f + 0.2f * R(state, x, y, 212), R(state, x, y, 213), Jitter(new Color(1, 1, 1), R(state, x, y, 214)), R(state, x, y, 215));
                }
                if (c.sapling >= 0 && c.sapling < Growth.SOLID_AT)
                {
                    bool forest = g.ground[i] == Array.IndexOf(Grids.GROUNDS, "forest");
                    var (sp, t, custom) = Trees.Placement(x, y, forest, false, (float)Growth.treeScale(c.sapling));
                    AddTransform(saplings[sp], t, custom);
                }
                if (c.crack > 0 || c.creep > 0) RoadWear(state, g, x, y, c, cracks, grass, ref budget, grassTint);
            }
        var nodes = new List<Node3D>();
        int count = 0;
        void Node(Mesh mesh, List<float> data, float range, bool shadow = false)
        {
            if (data.Count == 0) return;
            nodes.Add(Instances(mesh, data, range));
            count += data.Count / 16;
        }
        Node(grassMesh, grass, GRASS_RANGE);
        Node(weedMesh, weeds, WEED_RANGE);
        Node(trees.UnderMesh(0), bushes, BUSH_RANGE);
        Node(crackMesh, cracks, CRACK_RANGE);
        for (int s = 0; s < saplings.Length; s++) Node(trees.TreeMesh(s, 1), saplings[s], SAPLING_RANGE);
        return (nodes, count);
    }

    /// <summary>Cracks on the tile, and tufts creeping in from the sides that meet something that isn't road.</summary>
    void RoadWear(SimState state, Grid g, int x, int y, Cover c, List<float> cracks, List<float> grass, ref int budget, Color tint)
    {
        int nCracks = (int)(c.crack * 1.6 + R(state, x, y, 300));
        for (int k = 0; k < nCracks; k++)
        {
            float px = x + 0.15f + 0.7f * R(state, x, y, 310 + k), pz = y + 0.15f + 0.7f * R(state, x, y, 320 + k);
            float yaw = R(state, x, y, 330 + k) * Mathf.Pi, len = 0.5f + 0.9f * R(state, x, y, 340 + k);
            var o = new Vector3(px, Terrain.Height(px, pz) + 0.02f, pz);
            AddTransform(cracks, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(len, 1, len)), o), new Color(1, 1, 1, 0));
        }
        int nTufts = (int)(c.creep * 3.2 + R(state, x, y, 400));
        // Which sides meet ground that isn't road: tufts crowd there.
        int sides = 0; Span<int> dirs = stackalloc int[4];
        if (Open(g, x, y - 1)) dirs[sides++] = 0;
        if (Open(g, x + 1, y)) dirs[sides++] = 1;
        if (Open(g, x, y + 1)) dirs[sides++] = 2;
        if (Open(g, x - 1, y)) dirs[sides++] = 3;
        for (int k = 0; k < nTufts && budget > 0; k++, budget--)
        {
            float u = R(state, x, y, 410 + k), px, pz;
            if (sides > 0 && R(state, x, y, 420 + k) < 0.85f)
            {
                float depth = 0.04f + 0.28f * R(state, x, y, 430 + k) * R(state, x, y, 431 + k);
                switch (dirs[(int)(R(state, x, y, 440 + k) * sides) % sides])
                {
                    case 0: px = x + u; pz = y + depth; break;
                    case 1: px = x + 1 - depth; pz = y + u; break;
                    case 2: px = x + u; pz = y + 1 - depth; break;
                    default: px = x + depth; pz = y + u; break;
                }
            }
            else { px = x + u; pz = y + R(state, x, y, 450 + k); }
            float h = (0.08f + 0.32f * (float)Math.Min(1, c.creep + 0.2)) * (0.7f + 0.6f * R(state, x, y, 460 + k));
            Add(grass, px, pz, h, 0.7f + 0.5f * R(state, x, y, 470 + k), R(state, x, y, 480 + k), Jitter(tint, R(state, x, y, 490 + k)), R(state, x, y, 495 + k));
        }
    }

    static bool Open(Grid g, int x, int y)
    {
        if (x < 0 || y < 0 || x >= g.width || y >= g.height) return false;
        int gr = g.ground[y * g.width + x];
        return gr != ROAD && gr != PAVING && gr != BRIDGE;
    }

    static float R(SimState state, int x, int y, uint k) => (float)Growth.tuftRoll(state, x, y, k);
    static float R(SimState state, int x, int y, int k) => R(state, x, y, (uint)k);

    static Color Jitter(Color c, float r) { float v = 0.82f + 0.36f * r; return new Color(c.R * v, c.G * v, c.B * v); }

    /// <summary>One instance's 16 floats: the transform as Godot's buffer wants it (three rows of four), then the custom data (tint rgb, sway phase).</summary>
    static void Add(List<float> data, float px, float pz, float height, float width, float yaw, Color tint, float phase)
    {
        var o = new Vector3(px, Terrain.Height(px, pz) - 0.01f, pz);
        AddTransform(data, new Transform3D(new Basis(Vector3.Up, yaw * Mathf.Tau).Scaled(new Vector3(width, height, width)), o), new Color(tint.R, tint.G, tint.B, phase));
    }

    static void AddTransform(List<float> data, Transform3D t, Color custom)
    {
        var b = t.Basis;
        data.Add(b.Column0.X); data.Add(b.Column1.X); data.Add(b.Column2.X); data.Add(t.Origin.X);
        data.Add(b.Column0.Y); data.Add(b.Column1.Y); data.Add(b.Column2.Y); data.Add(t.Origin.Y);
        data.Add(b.Column0.Z); data.Add(b.Column1.Z); data.Add(b.Column2.Z); data.Add(t.Origin.Z);
        data.Add(custom.R); data.Add(custom.G); data.Add(custom.B); data.Add(custom.A);
    }

    MultiMeshInstance3D Instances(Mesh mesh, List<float> data, float range)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = data.Count / 16 };
        mm.Buffer = data.ToArray();
        var node = new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        node.VisibilityRangeEnd = range;
        node.VisibilityRangeEndMargin = MARGIN;
        parent.AddChild(node);
        return node;
    }

    // Blades: vertex colours (base dark to tip light) times the instance's tint; sway grows with height; both sides lit.
    const string BLADES = @"
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_disabled;
uniform vec3 wind = vec3(0.8, 0.0, 0.6);
varying vec3 v_tint;
void vertex() {
    v_tint = INSTANCE_CUSTOM.rgb;
    float phase = INSTANCE_CUSTOM.a * 6.2831;
    float h = max(VERTEX.y, 0.0);
    float gust = sin(TIME * 1.1 + phase) * 0.5 + sin(TIME * 2.3 + phase * 1.7) * 0.2 + 0.3;
    vec3 w = normalize((vec4(wind, 0.0) * MODEL_MATRIX).xyz);
    VERTEX += w * gust * 0.07 * h * h;
    NORMAL = vec3(0.0, 1.0, 0.0);
}
void fragment() {
    ALBEDO = COLOR.rgb * v_tint;
    ROUGHNESS = 0.95;
}
";
}
