// Trees and undergrowth in first person (docs/trees.md). Reads state only (rule 5).
//
// The models are art/scripts/build_trees.py's (godot/assets/trees/trees.glb): five species (spruce, pine,
// broadleaf, birch, a dead snag), each in three LODs, and two undergrowth pieces (bush, fern), in vertex
// colours with alpha-cut leaf cards near. Drawn by chunk (WorldMesh.CHUNK tiles), one MultiMesh per chunk,
// species and LOD; the LOD is the node's visibility range (to the chunk's centre), so a chunk swaps whole.
//
// A tree is a sim `tree` (furniture): the trunk stands in its tile (you and the dead collide with the
// tile), so it's jittered at most TRUNK_JITTER off the centre and the thickest trunk fits. Which species,
// its size, turn and tint come from a hash of the tile (stable, no RNG), with stands: a patch of a few
// tiles leans to one species. Forest ground with no tree gets low undergrowth (ferns and bushes, under
// knee height, walked through), purely visual. Everything sways a little in a shader (the wind's the
// same for all; each tree has its own phase).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class Trees
{
    const int CHUNK = WorldMesh.CHUNK;
    /// <summary>Chunks this far (in chunks) from yours have trees (FirstPerson's chunk radius).</summary>
    const int RADIUS = 3;
    const int BUILDS_PER_FRAME = 4;
    /// <summary>Frames between looks at whether a built chunk's trees changed.</summary>
    const int CHECK_EVERY = 30;
    const float TRUNK_JITTER = 0.12f;

    static readonly string[] SPECIES = { "spruce", "pine", "broadleaf", "birch", "snag" };
    static readonly string[] UNDER = { "bush", "fern" };
    /// <summary>How likely each species is: in the forest, and elsewhere (yards, verges).</summary>
    static readonly double[] FOREST_MIX = { 0.36, 0.24, 0.2, 0.14, 0.06 };
    static readonly double[] OPEN_MIX = { 0.12, 0.05, 0.5, 0.25, 0.08 };
    /// <summary>Sizes (scale of the model) each species is drawn at, smallest to largest.</summary>
    static readonly (float lo, float hi)[] SIZE = { (0.8f, 1.2f), (0.7f, 1.15f), (0.7f, 1.15f), (0.7f, 1.1f), (0.7f, 1.2f) };
    /// <summary>The LODs' visibility ranges (m from the camera to a chunk's centre): near, mid, far.</summary>
    static readonly (float begin, float end)[] LOD_RANGE = { (0, 24), (24, 44), (44, 0) };
    const float LOD_MARGIN = 2f;
    const float UNDER_RANGE = 30f;
    /// <summary>Of forest tiles without a tree: this share has a fern, then this a bush.</summary>
    const double FERN = 0.26, BUSH = 0.1;

    static readonly int FOREST = Array.IndexOf(Grids.GROUNDS, "forest");

    readonly Node3D parent;
    readonly Mesh[,] trees = new Mesh[SPECIES.Length, 3];
    readonly Mesh[] under = new Mesh[UNDER.Length];
    readonly Dictionary<(int, int), (long sig, List<Node3D> nodes, int count)> chunks = new();
    int frame;

    /// <summary>Trees in the chunks built (the debug panel).</summary>
    public int Count { get; private set; }

    public Trees(Node3D parent)
    {
        this.parent = parent;
        var scene = GD.Load<PackedScene>("res://assets/trees/trees.glb").Instantiate<Node3D>();
        var byName = new Dictionary<string, Mesh>();
        Collect(scene, byName);
        scene.Free();
        var tex = GD.Load<Texture2D>("res://assets/trees/leaves.png");
        var mats = new Dictionary<string, Material>
        {
            ["bark"] = Material(false, false, true, 0, tex),
            ["foliage"] = Material(false, false, false, 0.025f, tex),
            ["cards"] = Material(true, true, false, 0.04f, tex),
        };
        for (int s = 0; s < SPECIES.Length; s++)
            for (int l = 0; l < 3; l++) trees[s, l] = Dressed(byName[$"{SPECIES[s]}_{l}"], mats);
        for (int u = 0; u < UNDER.Length; u++) under[u] = Dressed(byName[$"{UNDER[u]}_0"], mats);
    }

    static void Collect(Node n, Dictionary<string, Mesh> into)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null) into[n.Name] = mi.Mesh;
        foreach (var c in n.GetChildren()) Collect(c, into);
    }

    /// <summary>The model with our materials in place of the file's (picked by the file's material names).</summary>
    static Mesh Dressed(Mesh m, Dictionary<string, Material> mats)
    {
        for (int i = 0; i < m.GetSurfaceCount(); i++)
        {
            string name = m.SurfaceGetMaterial(i)?.ResourceName ?? "bark";
            foreach (var (k, mat) in mats)
                if (name.StartsWith(k)) { m.SurfaceSetMaterial(i, mat); break; }
        }
        return m;
    }

    static ShaderMaterial Material(bool cards, bool textured, bool bark, float flutter, Texture2D tex)
    {
        var sh = new Shader { Code = SHADER.Replace("CULL", cards ? "cull_disabled" : "cull_back").Replace("TEXTURED", textured ? "1" : "0").Replace("BARKED", bark ? "1" : "0") };
        var m = new ShaderMaterial { Shader = sh };
        m.SetShaderParameter("leaves", tex);
        m.SetShaderParameter("flutter", flutter);
        return m;
    }

    public void Update(SimState state, Vector3 eye, WorldMesh.Lookups look)
    {
        var g = state.grid;
        int ccx = (int)Math.Floor(eye.X / CHUNK), ccy = (int)Math.Floor(eye.Z / CHUNK);
        int maxX = (g.width - 1) / CHUNK, maxY = (g.height - 1) / CHUNK;
        bool check = ++frame % CHECK_EVERY == 0;
        var want = new List<(int d, int x, int y)>();
        for (int y = Math.Max(0, ccy - RADIUS); y <= Math.Min(maxY, ccy + RADIUS); y++)
            for (int x = Math.Max(0, ccx - RADIUS); x <= Math.Min(maxX, ccx + RADIUS); x++)
                want.Add((Math.Max(Math.Abs(x - ccx), Math.Abs(y - ccy)), x, y));
        want.Sort((a, b) => a.d.CompareTo(b.d));
        int built = 0;
        foreach (var (_, x, y) in want)
        {
            bool have = chunks.TryGetValue((x, y), out var c);
            if (have && !check) continue;
            if (built >= BUILDS_PER_FRAME) break;
            long sig = Signature(state, x, y, look);
            if (have && c.sig == sig) continue;
            if (have) foreach (var n in c.nodes) n.QueueFree();
            var (nodes, count) = Build(state, x, y, look);
            chunks[(x, y)] = (sig, nodes, count);
            built++;
        }
        var drop = new List<(int, int)>();
        foreach (var (k, c) in chunks)
            if (Math.Max(Math.Abs(k.Item1 - ccx), Math.Abs(k.Item2 - ccy)) > RADIUS + 1) drop.Add(k);
        foreach (var k in drop)
        {
            foreach (var n in chunks[k].nodes) n.QueueFree();
            chunks.Remove(k);
        }
        int total = 0;
        foreach (var c in chunks.Values) total += c.count;
        Count = total;
    }

    /// <summary>What a chunk's trees depend on: its tree tiles and its forest ground.</summary>
    static long Signature(SimState state, int cx, int cy, WorldMesh.Lookups look)
    {
        var g = state.grid;
        long h = 23;
        if (look.furniture.TryGetValue((cx, cy), out var list))
            foreach (var f in list)
                if (f.kind == "tree") h = h * 31 + f.x * 7919 + f.y;
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                if (g.ground[i] == FOREST && g.tiles[i] == "floor") h = h * 31 + i;
            }
        return h;
    }

    (List<Node3D>, int) Build(SimState state, int cx, int cy, WorldMesh.Lookups look)
    {
        var g = state.grid;
        var treeAt = new List<(int species, Transform3D t, Color custom)>[SPECIES.Length];
        var underAt = new List<(Transform3D t, Color custom)>[UNDER.Length];
        for (int s = 0; s < SPECIES.Length; s++) treeAt[s] = new();
        for (int u = 0; u < UNDER.Length; u++) underAt[u] = new();
        var taken = new HashSet<int>();
        int count = 0;
        if (look.furniture.TryGetValue((cx, cy), out var list))
            foreach (var f in list)
            {
                if (f.kind != "tree") continue;
                int i = f.y * g.width + f.x;
                taken.Add(i);
                bool forest = g.ground[i] == FOREST;
                int s = Species(f.x, f.y, forest);
                var (lo, hi) = SIZE[s];
                float size = lo + (hi - lo) * U(f.x, f.y, 3);
                float px = f.x + 0.5f + (U(f.x, f.y, 4) - 0.5f) * 2 * TRUNK_JITTER;
                float pz = f.y + 0.5f + (U(f.x, f.y, 5) - 0.5f) * 2 * TRUNK_JITTER;
                treeAt[s].Add((s, Place(px, pz, U(f.x, f.y, 6) * Mathf.Tau, size), Tint(f.x, f.y)));
                count++;
            }
        int x0 = cx * CHUNK, y0 = cy * CHUNK;
        for (int y = y0; y < Math.Min(g.height, y0 + CHUNK); y++)
            for (int x = x0; x < Math.Min(g.width, x0 + CHUNK); x++)
            {
                int i = y * g.width + x;
                if (g.ground[i] != FOREST || g.tiles[i] != "floor" || taken.Contains(i)) continue;
                double r = U(x, y, 11);
                int u = r < FERN ? 1 : r < FERN + BUSH ? 0 : -1;
                if (u < 0) continue;
                float px = x + 0.2f + 0.6f * U(x, y, 12), pz = y + 0.2f + 0.6f * U(x, y, 13);
                underAt[u].Add((Place(px, pz, U(x, y, 14) * Mathf.Tau, (u == 1 ? 0.5f : 0.7f) + 0.45f * U(x, y, 15)), Tint(x, y)));
            }
        var nodes = new List<Node3D>();
        for (int s = 0; s < SPECIES.Length; s++)
        {
            if (treeAt[s].Count == 0) continue;
            for (int l = 0; l < 3; l++)
            {
                var items = new List<(Transform3D, Color)>();
                foreach (var (_, t, c) in treeAt[s]) items.Add((t, c));
                var node = Instances(trees[s, l], items);
                var (begin, end) = LOD_RANGE[l];
                node.VisibilityRangeBegin = begin;
                node.VisibilityRangeBeginMargin = begin > 0 ? LOD_MARGIN : 0;
                node.VisibilityRangeEnd = end;
                node.VisibilityRangeEndMargin = end > 0 ? LOD_MARGIN : 0;
                node.CastShadow = l < 2 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                nodes.Add(node);
            }
        }
        for (int u = 0; u < UNDER.Length; u++)
        {
            if (underAt[u].Count == 0) continue;
            var node = Instances(under[u], underAt[u]);
            node.VisibilityRangeEnd = UNDER_RANGE;
            node.VisibilityRangeEndMargin = LOD_MARGIN;
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            nodes.Add(node);
        }
        return (nodes, count);
    }

    MultiMeshInstance3D Instances(Mesh mesh, List<(Transform3D t, Color custom)> items)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = items.Count };
        for (int k = 0; k < items.Count; k++)
        {
            mm.SetInstanceTransform(k, items[k].t);
            mm.SetInstanceCustomData(k, items[k].custom);
        }
        var node = new MultiMeshInstance3D { Multimesh = mm };
        parent.AddChild(node);
        return node;
    }

    static Transform3D Place(float x, float z, float yaw, float size) =>
        new(new Basis(Vector3.Up, yaw).Scaled(new Vector3(size, size, size)), new Vector3(x, Terrain.Height(x, z), z));

    /// <summary>A tree's tint (rgb, about 1) and its sway phase (a, 0..1).</summary>
    static Color Tint(int x, int y)
    {
        float v = 0.88f + 0.22f * U(x, y, 7), warm = (U(x, y, 8) - 0.4f) * 0.12f;
        return new Color(v * (1 + warm), v, v * (1 - warm * 1.5f), U(x, y, 9));
    }

    /// <summary>The species at a tree tile: a stand (a patch of about 6 x 6 tiles, its edges ragged) leans to
    /// one species; the rest are mixed by the ground's odds.</summary>
    static int Species(int x, int y, bool forest)
    {
        var mix = forest ? FOREST_MIX : OPEN_MIX;
        int bx = (int)Math.Floor((x + 3 * U(y, 0, 21)) / 6), by = (int)Math.Floor((y + 3 * U(x, 0, 22)) / 6);
        double pick = U(x, y, 2) < 0.6 ? U(bx, by, 1) : U(x, y, 1);
        for (int s = 0; s < mix.Length; s++)
        {
            pick -= mix[s];
            if (pick < 0) return s;
        }
        return 0;
    }

    /// <summary>A stateless hash of a tile and a salt, 0..1 (the renderer's own; the sim's rolls aren't touched).</summary>
    static float U(int x, int y, uint salt)
    {
        uint h = unchecked((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ salt * 0xcb1ab31fu);
        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15; h = unchecked(h * 0x27d4eb2du); h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    // The trees' shader: vertex colours (the model's) times the instance's tint, the leaf atlas on cards
    // (alpha scissor), normals as the model has them (out of the crown, so a crown lights as a mass; on
    // cards both sides keep the outward normal), and sway: the whole tree bends with the height above its
    // foot, slow and gentle, leaves and crown masses flutter a little on top.
    const string SHADER = @"
shader_type spatial;
render_mode CULL, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
uniform sampler2D leaves : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float flutter = 0.0;
uniform vec3 wind = vec3(0.8, 0.0, 0.6);
varying vec3 v_nrm;
varying vec3 v_tint;
varying vec3 v_obj;

float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float noise3(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
    v_tint = INSTANCE_CUSTOM.rgb;
    v_obj = VERTEX;
    float phase = INSTANCE_CUSTOM.a * 6.2831;
    float h = max(VERTEX.y, 0.0) / 10.0;
    float b = h * h;
    float gust = sin(TIME * 0.6 + phase) * 0.55 + sin(TIME * 1.7 + phase * 1.3) * 0.2 + 0.3;
    vec3 w = normalize((vec4(wind, 0.0) * MODEL_MATRIX).xyz);
    VERTEX += w * gust * 0.14 * b;
    if (flutter > 0.0) {
        float f = sin(TIME * 3.7 + phase * 3.0 + dot(VERTEX, vec3(2.1, 1.7, 2.9)));
        VERTEX += NORMAL * f * flutter * min(h * 3.0, 1.0);
    }
    v_nrm = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
    vec3 c = COLOR.rgb * v_tint;
#if TEXTURED
    vec4 t = texture(leaves, UV);
    c *= t.rgb * 1.25;
    ALPHA = t.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
#endif
#if BARKED
    // Furrows: noise stretched up the trunk, darker in the cracks.
    float n = noise3(v_obj * vec3(14.0, 1.6, 14.0)) * 0.65 + noise3(v_obj * vec3(40.0, 5.0, 40.0)) * 0.35;
    c *= 0.62 + 0.55 * smoothstep(0.2, 0.75, n);
#endif
    ALBEDO = c;
    NORMAL = normalize(v_nrm);
    ROUGHNESS = 0.92;
    SPECULAR = 0.25;
}
";
}
