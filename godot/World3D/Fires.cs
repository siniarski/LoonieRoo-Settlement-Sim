// Fire in first person (2026-11-10, wildfire). Reads state only (rule 5); nothing here goes to the sim.
//
// What is drawn, all in a FIXED number of nodes however many tiles burn (the budgets below are the whole cost):
//   flames    one MultiMesh of three crossed flame cards, an emissive shader (additive, unshaded, no light node:
//             the lighting pass gives fires their glow from state.fires). Sized by what burns (FireMod.fuelAt):
//             grass a low flicker, undergrowth knee height, a tree three tongues up its trunk and crown, a wall or
//             furniture a column. A flame grows in over the first of its burn and dies back over the last.
//   smoke     one MultiMesh of columns: six camera-facing puffs each, rising and drifting on the wind in the
//             shader (no per-frame work here), darker for wood than for grass, orange-lit underneath at night.
//   embers    one MultiMesh of sparks: eight a source, rising and flickering out, in the shader.
//   scorch    one mesh of dark patches flush on the ground over every burnt tile near you (state.burnt), their edges
//             soft where burnt meets unburnt, fading over the days the ground takes to regrow.
//   charred   one MultiMesh of stumps and snags where a tree burnt (scar kind tree).
// Each MultiMesh draws the nearest sources up to its budget; the rest are not drawn at all, and a fire past
// DRAW metres isn't either. Fire sound belongs to a later pass.

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed partial class Fires : Node3D
{
    /// <summary>How far (m) fire is drawn: the fog's reach.</summary>
    const float DRAW = 48f;
    /// <summary>Instances each MultiMesh can show. The whole cost of any fire is these.</summary>
    public const int FLAMES = 360, SMOKE_SOURCES = 72, EMBER_SOURCES = 56, STUMPS = 900, SCAR_TILES = 5000;
    const int PUFFS = 6, SPARKS = 8;
    /// <summary>Metres a rebuilt scorch or stump set is good for before the eye moves on, and the least time between rebuilds.</summary>
    const float REBUILD_MOVE = 6f, REBUILD_EVERY = 0.25f;

    readonly MultiMesh flames = Instances(FlameMesh(), FLAMES);
    readonly MultiMesh smoke = Instances(PuffMesh(PUFFS, 1.0f), SMOKE_SOURCES);
    readonly MultiMesh embers = Instances(SparkMesh(), EMBER_SOURCES);
    readonly MultiMesh stumps = Instances(StumpMesh(), STUMPS);
    readonly MeshInstance3D scorch = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    readonly ShaderMaterial flameMat = Mat(FLAME_SHADER), smokeMat = Mat(SMOKE_SHADER), emberMat = Mat(EMBER_SHADER);
    readonly StandardMaterial3D scorchMat = new() { VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled };
    readonly StandardMaterial3D stumpMat = new() { VertexColorUseAsAlbedo = true, Roughness = 1f };
    float[] buffer = new float[16 * FLAMES];
    readonly List<(float d2, int i)> order = new();
    readonly Dictionary<string, (int x, int y)> parsed = new();
    Vector3 builtAt = new(float.NaN, 0, 0);
    long builtSig = -1;
    double sinceBuild = 1, clock;

    /// <summary>What was drawn last frame (the self-test and the debug panel).</summary>
    public int FlameCount { get; private set; }
    public int SmokeCount { get; private set; }
    public int EmberCount { get; private set; }
    public int ScorchTiles { get; private set; }
    public int StumpCount { get; private set; }
    /// <summary>CPU milliseconds of Update, smoothed over about ten frames (the self-test reports it).</summary>
    public double UpdateMs { get; private set; }
    /// <summary>Nodes this makes, always (nothing grows with the fire).</summary>
    public int NodeCount => GetChildCount();

    public Fires()
    {
        Name = "Fires";
        AddChild(MakeNode(flames, flameMat, false));
        AddChild(MakeNode(smoke, smokeMat, false));
        AddChild(MakeNode(embers, emberMat, false));
        AddChild(MakeNode(stumps, stumpMat, true));
        scorch.MaterialOverride = scorchMat;
        AddChild(scorch);
        flames.VisibleInstanceCount = 0;
        smoke.VisibleInstanceCount = 0;
        embers.VisibleInstanceCount = 0;
        stumps.VisibleInstanceCount = 0;
    }

    static MultiMeshInstance3D MakeNode(MultiMesh mm, Material mat, bool shadows) => new()
    {
        Multimesh = mm,
        MaterialOverride = mat,
        CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        ExtraCullMargin = 4096,
    };

    static ShaderMaterial Mat(string code) => new() { Shader = new Shader { Code = code } };

    static MultiMesh Instances(Mesh mesh, int n) =>
        new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = n };

    // ---- each frame ----

    public void Update(SimState state, Vector3 eye, double delta)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Terrain.Use(state);
        sinceBuild += delta;
        // The shaders' clock is the world's, not TIME: it stops while the world is held (delta 0), as everything does.
        clock = (clock + delta) % 3600;
        foreach (var mat in new[] { flameMat, smokeMat, emberMat }) mat.SetShaderParameter("clock", (float)clock);
        float day = 1f - (float)LoonieRoo.Sim.Time.darkness(state.tick);
        flameMat.SetShaderParameter("night", 1f - day);
        smokeMat.SetShaderParameter("day", day);
        emberMat.SetShaderParameter("night", 1f - day);
        scorchMat.AlbedoColor = new Color(0.25f + 0.75f * day, 0.25f + 0.75f * day, 0.25f + 0.75f * day);
        stumpMat.AlbedoColor = new Color(0.3f + 0.7f * day, 0.3f + 0.7f * day, 0.3f + 0.7f * day);
        Burning(state, eye);
        Scars(state, eye);
        UpdateMs = UpdateMs * 0.9 + timer.Elapsed.TotalMilliseconds * 0.1;
    }

    /// <summary>The fires near the eye, nearest first, each drawn by what it burns.</summary>
    void Burning(SimState state, Vector3 eye)
    {
        order.Clear();
        float r2 = DRAW * DRAW;
        for (int i = 0; i < state.fires.Count; i++)
        {
            var f = state.fires[i];
            float dx = f.x + 0.5f - eye.X, dz = f.y + 0.5f - eye.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 <= r2) order.Add((d2, i));
        }
        order.Sort((a, b) => a.d2.CompareTo(b.d2));
        int nf = 0, ns = 0, ne = 0;
        var fb = buffer;
        var sb = new float[16 * SMOKE_SOURCES];
        var eb = new float[16 * EMBER_SOURCES];
        foreach (var (_, i) in order)
        {
            if ((nf >= FLAMES - 3) && (ns >= SMOKE_SOURCES) && (ne >= EMBER_SOURCES)) break;
            var f = state.fires[i];
            var fuel = FireMod.fuelAt(state, f.x, f.y);
            float p = (float)Math.Clamp(f.left / FireMod.burnTicks(fuel), 0, 1);
            // In over the first of the burn, out over the last fifth.
            float life = Math.Clamp(Math.Min((1 - p) / 0.12f + 0.3f, p / 0.2f), 0, 1);
            if (life <= 0.02f) continue;
            float ux = U(f.x, f.y, 1), uz = U(f.x, f.y, 2), seed = U(f.x, f.y, 3);
            float cx = f.x + 0.5f + (ux - 0.5f) * 0.3f, cz = f.y + 0.5f + (uz - 0.5f) * 0.3f;
            float ground = Terrain.Height(cx, cz);
            float turn = U(f.x, f.y, 4) * Mathf.Tau;
            // The flames (tongues: height, width, how high up).
            int need = fuel switch { FireMod.Fuel.Tree => 3, _ => 1 };
            if (nf + need <= FLAMES)
            {
                switch (fuel)
                {
                    case FireMod.Fuel.Grass:
                        Put(fb, nf++, cx, ground, cz, 1.0f, 0.55f * life, turn, seed, 0, life);
                        break;
                    case FireMod.Fuel.Undergrowth:
                        Put(fb, nf++, cx, ground, cz, 1.2f, 0.95f * life, turn, seed, 1, life);
                        break;
                    case FireMod.Fuel.Tree:
                        Put(fb, nf++, cx, ground, cz, 1.0f, 1.7f * life, turn, seed, 2, life);
                        Put(fb, nf++, cx, ground + 1.5f * life, cz, 1.2f, 2.3f * life, turn + 1.1f, U(f.x, f.y, 5), 2, life);
                        Put(fb, nf++, cx, ground + 3.6f * life, cz, 1.5f, 2.8f * life, turn + 2.3f, U(f.x, f.y, 6), 2, life);
                        break;
                    default:
                        Put(fb, nf++, cx, ground, cz, 1.2f, 1.7f * life, turn, seed, 3, life);
                        break;
                }
            }
            // Smoke: wood and undergrowth always, grass for one tile in four.
            bool grass = fuel == FireMod.Fuel.Grass;
            if ((ns < SMOKE_SOURCES) && (!grass || (U(f.x, f.y, 7) < 0.25f)))
            {
                float size = grass ? 0.9f : fuel == FireMod.Fuel.Undergrowth ? 1.3f : fuel == FireMod.Fuel.Tree ? 2.2f : 1.7f;
                float rise = grass ? 3.5f : fuel == FireMod.Fuel.Undergrowth ? 5f : fuel == FireMod.Fuel.Tree ? 13f : 8f;
                float dark = grass ? 0.05f : fuel == FireMod.Fuel.Undergrowth ? 0.3f : fuel == FireMod.Fuel.Tree ? 0.75f : 0.95f;
                float baseY = ground + (fuel == FireMod.Fuel.Tree ? 4f * life : 0.4f);
                Put(sb, ns++, cx, baseY, cz, size, rise, 0, seed, dark, life * (grass ? 0.6f : 1f));
            }
            if ((ne < EMBER_SOURCES) && (!grass || (U(f.x, f.y, 8) < 0.15f)))
            {
                float height = grass ? 1.5f : fuel == FireMod.Fuel.Tree ? 6f : 3f;
                Put(eb, ne++, cx, ground + (fuel == FireMod.Fuel.Tree ? 2f * life : 0.2f), cz, grass ? 0.5f : 1.0f, height, 0, seed, 0, life);
            }
        }
        FlameCount = nf;
        SmokeCount = ns;
        EmberCount = ne;
        flames.Buffer = Slice(fb, nf, FLAMES);
        flames.VisibleInstanceCount = nf;
        smoke.Buffer = Slice(sb, ns, SMOKE_SOURCES);
        smoke.VisibleInstanceCount = ns;
        embers.Buffer = Slice(eb, ne, EMBER_SOURCES);
        embers.VisibleInstanceCount = ne;
    }

    /// <summary>A MultiMesh buffer must be its instance count long: the used part, the rest zeros.</summary>
    static float[] Slice(float[] buf, int used, int total)
    {
        if (used < total) Array.Clear(buf, used * 16, (total - used) * 16);
        return buf.Length == total * 16 ? buf : buf[..(total * 16)];
    }

    /// <summary>One instance: at (x, y, z), `w` wide, `h` high, turned `turn`; custom data seed, a kind, the life.</summary>
    static void Put(float[] b, int n, float x, float y, float z, float w, float h, float turn, float seed, float kind, float life)
    {
        int o = n * 16;
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        b[o + 0] = c * w; b[o + 1] = 0; b[o + 2] = s * w; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = h; b[o + 6] = 0; b[o + 7] = y;
        b[o + 8] = -s * w; b[o + 9] = 0; b[o + 10] = c * w; b[o + 11] = z;
        b[o + 12] = seed; b[o + 13] = kind; b[o + 14] = life; b[o + 15] = 0;
    }

    /// <summary>A stable number in [0, 1) from a tile and a salt (no RNG: the same fire looks the same each time).</summary>
    static float U(int x, int y, uint salt) => (Trunks.hash(x, y, 0xF1 + salt) & 0xFFFFFF) / 16777216f;

    // ---- scorched ground and what's left of the trees ----

    void Scars(SimState state, Vector3 eye)
    {
        long sig = (state.burnt.Count * 1000003L) + (state.tick / 600);
        bool moved = float.IsNaN(builtAt.X) || (eye - builtAt).LengthSquared() > REBUILD_MOVE * REBUILD_MOVE;
        if (((sig == builtSig) && !moved) || (sinceBuild < REBUILD_EVERY)) return;
        sinceBuild = 0;
        builtAt = eye;
        builtSig = sig;
        var tiles = new List<(float d2, int x, int y, int kind, float age)>();
        float r = DRAW - 4, r2 = r * r;
        float life = FireMod.SCAR_DAYS * LoonieRoo.Sim.Time.DAY_LENGTH;
        foreach (var kv in state.burnt)
        {
            if (!parsed.TryGetValue(kv.Key, out var xy))
            {
                var p = kv.Key.Split(',');
                xy = (int.Parse(p[0]), int.Parse(p[1]));
                parsed[kv.Key] = xy;
            }
            float dx = xy.x + 0.5f - eye.X, dz = xy.y + 0.5f - eye.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > r2) continue;
            if (!FireMod.scarOf(state, xy.x, xy.y, out int tick, out int kind)) continue; // regrown
            tiles.Add((d2, xy.x, xy.y, kind, Math.Max(0, (state.tick - tick) / life)));
        }
        if (tiles.Count > SCAR_TILES)
        {
            tiles.Sort((a, b) => a.d2.CompareTo(b.d2));
            tiles.RemoveRange(SCAR_TILES, tiles.Count - SCAR_TILES);
        }
        ScorchMesh(tiles, eye);
        // Stumps and snags: a tree that burnt leaves its trunk, black, in the tile the sim's trunk stood in.
        int ns = 0;
        var sb = new float[16 * STUMPS];
        foreach (var t in tiles)
        {
            if ((t.kind != FireMod.SCAR_TREE) || (ns >= STUMPS)) continue;
            // The tree itself: if one still stands here (it was doused, or regrown) nothing is left to show.
            if (FurnitureMod.furnitureAt(state, t.x, t.y)?.kind == "tree") continue;
            var c = Trunks.trunkAt(t.x, t.y);
            float x = c.x / (float)Motion.SUB, z = c.y / (float)Motion.SUB;
            bool snag = U(t.x, t.y, 20) < 0.22f;
            float h = snag ? 2.2f + 2.4f * U(t.x, t.y, 21) : 0.35f + 0.9f * U(t.x, t.y, 22);
            float w = snag ? 0.8f : 1.0f + 0.4f * U(t.x, t.y, 23);
            Put(sb, ns++, x, Terrain.Height(x, z) - 0.05f, z, w, h, U(t.x, t.y, 24) * Mathf.Tau, 0, 0, 1);
        }
        StumpCount = ns;
        stumps.Buffer = ns == 0 ? new float[16 * STUMPS] : Slice(sb, ns, STUMPS);
        stumps.VisibleInstanceCount = ns;
    }

    /// <summary>Dark patches flush on the ground, a vertex at each tile corner, its opacity the share of the four tiles
    /// round it that burnt: so a burn has soft edges and not a tile grid. Colour by age and kind.</summary>
    void ScorchMesh(List<(float d2, int x, int y, int kind, float age)> tiles, Vector3 eye)
    {
        ScorchTiles = tiles.Count;
        if (tiles.Count == 0)
        {
            scorch.Mesh = null;
            return;
        }
        var set = new HashSet<long>();
        foreach (var t in tiles) set.Add(((long)t.y << 32) | (uint)t.x);
        bool Burnt(int x, int y) => set.Contains(((long)y << 32) | (uint)x);
        var verts = new Vector3[tiles.Count * 4];
        var cols = new Color[tiles.Count * 4];
        var idx = new int[tiles.Count * 6];
        int n = 0;
        foreach (var t in tiles)
        {
            float age = t.age;
            // Black at first, then the grey-brown of old ash, then gone.
            var fresh = t.kind == FireMod.SCAR_OTHER ? new Color(0.16f, 0.155f, 0.15f) : new Color(0.03f, 0.027f, 0.024f);
            var old = new Color(0.2f, 0.17f, 0.12f);
            var col = fresh.Lerp(old, Math.Min(1, age * 1.6f));
            float alpha = 0.92f * (1 - age * age);
            float lift = 0.03f + 0.0016f * MathF.Sqrt(t.d2);
            for (int k = 0; k < 4; k++)
            {
                int cx = t.x + (k == 1 || k == 2 ? 1 : 0), cy = t.y + (k >= 2 ? 1 : 0);
                // Tiles sharing this corner: this one and up to three more.
                int share = 0;
                for (int dy = -1; dy <= 0; dy++) for (int dx = -1; dx <= 0; dx++) if (Burnt(cx + dx, cy + dy)) share++;
                float patchy = 0.8f + 0.2f * U(cx, cy, 30);
                verts[n * 4 + k] = new Vector3(cx, Terrain.Height(cx, cy) + lift, cy);
                cols[n * 4 + k] = new Color(col.R, col.G, col.B, alpha * share / 4f * patchy);
            }
            int b = n * 4;
            idx[n * 6 + 0] = b; idx[n * 6 + 1] = b + 1; idx[n * 6 + 2] = b + 2;
            idx[n * 6 + 3] = b; idx[n * 6 + 4] = b + 2; idx[n * 6 + 5] = b + 3;
            n++;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        scorch.Mesh = mesh;
    }

    // ---- meshes ----

    /// <summary>Three vertical cards one metre wide and high from y = 0, 60 degrees apart. UV: x across, y from 0 at the tip.</summary>
    static ArrayMesh FlameMesh()
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();
        for (int c = 0; c < 3; c++)
        {
            float a = c * Mathf.Pi / 3f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f;
            int b = v.Count;
            v.Add(-dir); uv.Add(new Vector2(0, 1));
            v.Add(dir); uv.Add(new Vector2(1, 1));
            v.Add(dir + Vector3.Up); uv.Add(new Vector2(1, 0));
            v.Add(-dir + Vector3.Up); uv.Add(new Vector2(0, 0));
            idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        return Build(v, uv, null, idx, new Aabb(new Vector3(-1, -0.2f, -1), new Vector3(2, 1.6f, 2)));
    }

    /// <summary>Camera-facing puffs: every corner of a puff sits at the origin, the shader moves them. The puff's phase is in the vertex colour's red, its corner in the UV.</summary>
    static ArrayMesh PuffMesh(int n, float unit)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var col = new List<Color>();
        var idx = new List<int>();
        for (int k = 0; k < n; k++)
        {
            int b = v.Count;
            var c = new Color((k + 0.5f) / n, 0, 0);
            for (int j = 0; j < 4; j++)
            {
                v.Add(Vector3.Zero);
                uv.Add(new Vector2(j == 1 || j == 2 ? 1 : 0, j >= 2 ? 1 : 0));
                col.Add(c);
            }
            idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        return Build(v, uv, col, idx, new Aabb(new Vector3(-6, 0, -6), new Vector3(12, 1.2f, 12)));
    }

    /// <summary>Sparks: the same trick, a phase and a second random number (green) a spark.</summary>
    static ArrayMesh SparkMesh()
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var col = new List<Color>();
        var idx = new List<int>();
        for (int k = 0; k < SPARKS; k++)
        {
            int b = v.Count;
            var c = new Color((k + 0.37f) / SPARKS, ((k * 0.6180339f) % 1f), 0);
            for (int j = 0; j < 4; j++)
            {
                v.Add(Vector3.Zero);
                uv.Add(new Vector2(j == 1 || j == 2 ? 1 : 0, j >= 2 ? 1 : 0));
                col.Add(c);
            }
            idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        return Build(v, uv, col, idx, new Aabb(new Vector3(-4, 0, -4), new Vector3(8, 1.2f, 8)));
    }

    /// <summary>A six-sided tapering trunk, a metre high, black with a greyer foot and a ragged top (vertex colours).</summary>
    static ArrayMesh StumpMesh()
    {
        const int SIDES = 6;
        var v = new List<Vector3>();
        var col = new List<Color>();
        var idx = new List<int>();
        for (int i = 0; i < SIDES; i++)
        {
            float a0 = i * Mathf.Tau / SIDES, a1 = (i + 1) * Mathf.Tau / SIDES;
            // Foot ring radius 0.15, top ring 0.09, the top's height jagged by side.
            float top = 1.0f - 0.18f * ((i * 7) % 3) / 2f;
            int b = v.Count;
            v.Add(new Vector3(Mathf.Cos(a0) * 0.15f, 0, Mathf.Sin(a0) * 0.15f));
            v.Add(new Vector3(Mathf.Cos(a1) * 0.15f, 0, Mathf.Sin(a1) * 0.15f));
            v.Add(new Vector3(Mathf.Cos(a1) * 0.09f, top, Mathf.Sin(a1) * 0.09f));
            v.Add(new Vector3(Mathf.Cos(a0) * 0.09f, top, Mathf.Sin(a0) * 0.09f));
            col.Add(new Color(0.12f, 0.1f, 0.09f)); col.Add(new Color(0.12f, 0.1f, 0.09f));
            col.Add(new Color(0.03f, 0.025f, 0.02f)); col.Add(new Color(0.03f, 0.025f, 0.02f));
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        // A blackened cap.
        int c = v.Count;
        v.Add(new Vector3(0, 0.95f, 0)); col.Add(new Color(0.02f, 0.02f, 0.02f));
        for (int i = 0; i < SIDES; i++)
        {
            float a = i * Mathf.Tau / SIDES;
            v.Add(new Vector3(Mathf.Cos(a) * 0.09f, 1.0f - 0.18f * ((i * 7) % 3) / 2f, Mathf.Sin(a) * 0.09f)); col.Add(new Color(0.02f, 0.02f, 0.02f));
        }
        for (int i = 0; i < SIDES; i++) idx.AddRange(new[] { c, c + 1 + i, c + 1 + (i + 1) % SIDES });
        return Build(v, null, col, idx, new Aabb(new Vector3(-0.2f, 0, -0.2f), new Vector3(0.4f, 1.05f, 0.4f)));
    }

    static ArrayMesh Build(List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> idx, Aabb aabb)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        if (uv != null) arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        if (col != null) arrays[(int)Mesh.ArrayType.Color] = col.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = aabb;
        return mesh;
    }

    // ---- shaders ----

    const string NOISE = @"
float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x), mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}
";

    /// <summary>Flames: a licking tongue narrowing to the tip, white-yellow in the heart, orange, then dark red; a flicker and a sway from the time and the instance's seed. Additive and unshaded: it glows at any hour, and no light node is involved.</summary>
    const string FLAME_SHADER = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform float night = 0.0;
uniform float clock = 0.0;
varying vec4 inst;
varying float near;
" + NOISE + @"
void vertex() {
    inst = INSTANCE_CUSTOM;
    near = smoothstep(1.2, 4.0, length(MODEL_MATRIX[3].xyz - INV_VIEW_MATRIX[3].xyz));
    float t = clock * (2.4 + inst.x * 1.6) + inst.x * 40.0;
    float h = 1.0 - UV.y;
    float sway = sin(t * 1.3) * 0.5 + sin(t * 2.3 + 1.7) * 0.5;
    VERTEX.x += sway * 0.16 * h * h * (1.0 + inst.y * 0.3);
    VERTEX.y *= 0.88 + 0.22 * (sin(t * 3.1) * 0.5 + 0.5);
}
void fragment() {
    float h = 1.0 - UV.y;
    float w = abs(UV.x - 0.5) * 2.0;
    float seed = inst.x * 31.0;
    vec2 q = vec2(UV.x * 2.6 + seed, h * 2.4 - clock * (1.7 + inst.x));
    float n = vnoise(q) * 0.62 + vnoise(q * 2.3 + 7.0) * 0.38;
    float width = mix(0.6, 0.04, pow(h, 0.7));
    float body = smoothstep(width + 0.1, width - 0.28, w + (n - 0.5) * 0.95);
    float tip = smoothstep(1.0, 0.45, h + (n - 0.5) * 0.3);
    float a = body * tip * (0.5 + 0.9 * n) * clamp(inst.z, 0.0, 1.0);
    float core = clamp((1.0 - w) * (1.0 - h * 1.1) * 1.5, 0.0, 1.0);
    vec3 cool = vec3(0.5, 0.07, 0.02);
    vec3 mid = vec3(1.0, 0.4, 0.06);
    vec3 hot = vec3(1.0, 0.86, 0.5);
    vec3 col = mix(cool, mid, smoothstep(0.0, 0.75, 1.0 - h * 0.95 - w * 0.4));
    col = mix(col, hot, smoothstep(0.3, 0.95, core));
    // Grass burns paler and quicker, wood and walls a deeper orange.
    col *= mix(vec3(1.0), vec3(1.0, 0.82, 0.7), clamp(inst.y * 0.3, 0.0, 1.0));
    ALBEDO = col * (0.95 + night * 0.7);
    ALPHA = clamp(a * (0.7 + night * 0.2) * near, 0.0, 1.0);
}
";

    /// <summary>Smoke: puffs rise from the instance's origin over its height (the instance's y scale), swell, drift on the wind and fade. Camera-facing, built in the vertex stage. Dark for wood, pale for grass; at night the base glows from the fire under it.</summary>
    const string SMOKE_SHADER = @"shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;
uniform float day = 1.0;
uniform float clock = 0.0;
uniform vec2 wind = vec2(0.9, 0.35);
varying vec4 inst;
varying float age;
varying float near;
" + NOISE + @"
void vertex() {
    inst = INSTANCE_CUSTOM;
    age = fract(clock * (0.085 + inst.x * 0.03) + COLOR.r + inst.x);
    float height = MODEL_MATRIX[1].y;
    vec3 base = MODEL_MATRIX[3].xyz;
    vec3 centre = base + vec3(0.0, height * age, 0.0);
    centre.xz += wind * age * age * height * 0.55 + vec2(sin(inst.x * 50.0 + age * 3.0), cos(inst.x * 40.0 + age * 2.5)) * 0.3 * age;
    float size = length(MODEL_MATRIX[0].xyz) * (0.4 + 1.7 * age);
    near = smoothstep(2.0, 7.0, length(centre - INV_VIEW_MATRIX[3].xyz));
    vec3 right = INV_VIEW_MATRIX[0].xyz;
    vec3 up = INV_VIEW_MATRIX[1].xyz;
    vec2 c = UV - 0.5;
    vec3 world = centre + (right * c.x - up * c.y) * size;
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}
void fragment() {
    vec2 c = UV - 0.5;
    float d = length(c) * 2.0;
    float n = vnoise(UV * 4.0 + inst.x * 20.0 + age * 2.0);
    float a = smoothstep(1.0, 0.15, d + (n - 0.5) * 0.55);
    a *= smoothstep(0.0, 0.1, age) * (1.0 - smoothstep(0.5, 1.0, age));
    vec3 pale = vec3(0.5, 0.48, 0.45);
    vec3 dark = vec3(0.09, 0.08, 0.075);
    vec3 col = mix(pale, dark, clamp(inst.y, 0.0, 1.0));
    col = mix(col, col * 1.5, age * 0.5);
    float light = 0.2 + 0.8 * day;
    // The fire's glow on the smoke just above it, strongest at night.
    vec3 glow = vec3(1.0, 0.45, 0.12) * (1.0 - age * 3.0 > 0.0 ? (1.0 - age * 3.0) : 0.0) * (1.0 - day) * 0.7;
    ALBEDO = col * light + glow;
    ALPHA = a * 0.32 * near * clamp(inst.z, 0.0, 1.0);
}
";

    /// <summary>Embers: bright specks leaving the fire, rising on their own slow arcs, flickering and fading as they go dark.</summary>
    const string EMBER_SHADER = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform float night = 0.0;
uniform float clock = 0.0;
uniform vec2 wind = vec2(0.9, 0.35);
varying float fade;
varying vec2 uvv;
void vertex() {
    float rnd = COLOR.g;
    float age = fract(clock * (0.3 + rnd * 0.35) + COLOR.r + INSTANCE_CUSTOM.x);
    float height = MODEL_MATRIX[1].y;
    float spread = length(MODEL_MATRIX[0].xyz);
    vec3 base = MODEL_MATRIX[3].xyz;
    vec3 p = base + vec3((rnd - 0.5) * spread * 2.0, height * (0.35 + rnd * 0.65) * age, (fract(rnd * 7.0) - 0.5) * spread * 2.0);
    p.xz += wind * age * age * height * 0.4 + vec2(sin(clock * 3.0 + rnd * 20.0), cos(clock * 2.4 + rnd * 17.0)) * 0.25 * age;
    float flick = 0.5 + 0.5 * sin(clock * (20.0 + rnd * 20.0) + rnd * 50.0);
    fade = (1.0 - age) * (0.4 + 0.6 * flick) * smoothstep(0.0, 0.05, age) * clamp(INSTANCE_CUSTOM.z, 0.0, 1.0);
    float size = 0.05 * (1.0 - age * 0.6);
    vec2 c = UV - 0.5;
    vec3 world = p + (INV_VIEW_MATRIX[0].xyz * c.x - INV_VIEW_MATRIX[1].xyz * c.y) * size;
    uvv = UV;
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}
void fragment() {
    float d = length(uvv - 0.5) * 2.0;
    float a = smoothstep(1.0, 0.0, d) * fade;
    ALBEDO = vec3(1.0, 0.42, 0.08) * (1.2 + night * 1.5);
    ALPHA = clamp(a, 0.0, 1.0);
}
";
}
