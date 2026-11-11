// The far field: the world past first person's built chunks, out to Range metres, drawn cheaply so you can
// see a long way. Reads state only (rule 5).
//
// The world is cut into REGION x REGION-tile regions; each within range is one mesh and a few MultiMeshes:
//   - the ground on a STEP-tile grid (corner heights from Terrain, vertex colours by ground, water at its
//     level), a little under the near ground so the two never fight; each corner the mean of the sixteen tiles
//     about it and the water's edge cut where half of them are water, so it is as smooth as the near ground's
//     edges, coarser (2026-11-12);
//   - country roads as the near view's ribbons, coarser (Roads: the plan's lines, a strip a piece every 4 m, a
//     disc at a junction, a deck over water; their lane lines in a mesh seen to LINES_TO), the road's tiles
//     drawn as the ground it runs over;
//   - buildings as blocks: floor tiles (boards, tiles, concrete) merged into rectangles, walls to the
//     eaves and a flat roof in a roof colour (by a hash of the block);
//   - trees as the far LOD (Trees.TreeMesh(s, 2): 10-86 triangles), one MultiMesh per species, no shadows,
//     out to TreeTier metres; past that each tree is a crown of 8 triangles (Crowns: a diamond the far LOD's
//     height, width and colours) baked into one mesh for the region, so a far region is two draw calls, not
//     seven. The two swap by visibility range (to the region's middle), so moving needs no rebuild.
// Where first person's chunks are built (a square round you, the "hole"), the far field steps aside: its
// ground and blocks discard their pixels there and its trees collapse to nothing, all in the shaders, so
// moving needs no rebuild. Regions are built nearest first, BUILD_MS a frame at most (rule 14), and rebuilt when the
// world's walls or furniture change (checked rarely: far things change little).
//
// Land not generated yet (the sim makes chunks as survivors come near) is drawn from the world plan, which is
// pure from the seed: the ground by Worldgen.generateChunk's own rules (PlanGround), buildings from the lots'
// footprints (Lots.layoutLot), trees by its own roll. When a chunk is generated, the region is built again from
// the grid (its signature counts the generated chunks).
//
// Range 0 is off (first person's ~56 m and its fog, as before).

using System;
using System.Collections.Generic;
using Godot;
using LoonieRoo.Game.Render;
using LoonieRoo.Sim;

namespace LoonieRoo.Game.World3D;

public sealed class FarField
{
    public const int REGION = 64;
    const int STEP = 4;
    const float SINK = 0.08f;
    /// <summary>Building regions stops for the frame once it has spent this long (ms; at least one is built).</summary>
    const double BUILD_MS = 4;
    const int CHECK_EVERY = 300;
    /// <summary>The far roads: a piece's length (m), how far over the far ground they lie and their lines over them,
    /// and how far their lines are seen (m, to the region's middle: past it a line is under a pixel or two).</summary>
    const float ROAD_STEP = 4, ROAD_LIFT = 0.14f, LINE_UP = 0.03f, LINES_TO = 140;
    static readonly int ROAD = Array.IndexOf(Grids.GROUNDS, "road"), BRIDGE = Array.IndexOf(Grids.GROUNDS, "bridge");
    /// <summary>How far the far field draws (m); 0 is off. The game's option --view=M; the draw bench's knob.</summary>
    public static float Range;
    /// <summary>Draw the far trees (the options' Far trees: Off).</summary>
    public static bool DrawTrees = true;
    /// <summary>Past this (m, to a region's middle) far trees are crowns, not the far LOD: 0 is crowns everywhere
    /// (the options' Far trees: Simple), infinity the far LOD everywhere (the bench's :lod2).</summary>
    public static float TreeTier = TREE_TIER;
    public const float TREE_TIER = 350;

    static readonly HashSet<string> FLOORS = new() { "boards", "tiles", "concrete" };
    static readonly Color[] ROOFS = { new(0.33f, 0.27f, 0.24f), new(0.28f, 0.29f, 0.3f), new(0.4f, 0.36f, 0.32f), new(0.24f, 0.22f, 0.22f), new(0.42f, 0.28f, 0.22f) };

    const string GROUND_SHADER = @"
shader_type spatial;
render_mode cull_back, specular_disabled;
uniform vec4 hole;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    if (wpos.x > hole.x && wpos.x < hole.z && wpos.z > hole.y && wpos.z < hole.w) discard;
    ALBEDO = COLOR.rgb;
    ROUGHNESS = 0.95;
}";

    const string TREE_SHADER = @"
shader_type spatial;
render_mode cull_back, specular_disabled;
uniform vec4 hole;
varying vec3 tint;
void vertex() {
    vec3 o = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    if (o.x > hole.x && o.x < hole.z && o.z > hole.y && o.z < hole.w) VERTEX = vec3(0.0);
    tint = INSTANCE_CUSTOM.rgb;
}
void fragment() {
    ALBEDO = COLOR.rgb * tint;
    ROUGHNESS = 0.95;
}";

    readonly Node3D parent;
    readonly Trees trees;
    readonly ShaderMaterial groundMat = new() { Shader = new Shader { Code = GROUND_SHADER } };
    readonly ShaderMaterial treeMat = new() { Shader = new Shader { Code = TREE_SHADER } };
    readonly Dictionary<(int, int), Built> regions = new();
    /// <summary>A region as built: its node, signature, trees, triangles (ground and blocks; its trees as the far LOD, as crowns), and the tree options it was built with.</summary>
    readonly record struct Built(Node3D node, long sig, int trees, int tris, int lodTris, int crownTris, float tier, bool drawTrees);
    readonly Dictionary<(int, int), List<Furniture>> treesByRegion = new();
    int frame, furnitureCount = -1;
    long version = -1;

    /// <summary>Regions in range not built yet (the bench waits for 0).</summary>
    public int Pending { get; private set; }
    public int Regions => regions.Count;
    public int TreeCount { get; private set; }
    public int Triangles { get; private set; }
    /// <summary>Time spent building regions so far (ms).</summary>
    public double BuildMs { get; private set; }

    public FarField(Node3D parent, Trees trees)
    {
        this.parent = parent;
        this.trees = trees;
    }

    /// <summary>The hole: first person's chunks (in tiles: x0, z0, x1, z1), where the far field doesn't draw.</summary>
    public void Update(SimState state, Vector3 eye, Vector4 hole)
    {
        groundMat.SetShaderParameter("hole", hole);
        treeMat.SetShaderParameter("hole", hole);
        if (Range <= 0)
        {
            if (regions.Count > 0) Clear();
            return;
        }
        var g = state.grid;
        bool check = ++frame % CHECK_EVERY == 0;
        if (state.furniture.Count != furnitureCount || (check && state.walkabilityVersion != version))
        {
            furnitureCount = state.furniture.Count;
            treesByRegion.Clear();
            foreach (var f in state.furniture)
            {
                if (f.kind != "tree") continue;
                var k = (f.x / REGION, f.y / REGION);
                if (!treesByRegion.TryGetValue(k, out var l)) treesByRegion[k] = l = new();
                l.Add(f);
            }
        }
        version = state.walkabilityVersion;
        int maxX = (g.width - 1) / REGION, maxY = (g.height - 1) / REGION;
        var want = new List<(float d, int x, int y)>();
        int r = (int)Math.Ceiling(Range / REGION) + 1, cx = (int)(eye.X / REGION), cy = (int)(eye.Z / REGION);
        for (int y = Math.Max(0, cy - r); y <= Math.Min(maxY, cy + r); y++)
            for (int x = Math.Max(0, cx - r); x <= Math.Min(maxX, cx + r); x++)
            {
                float d = Distance(eye, x, y);
                if (d <= Range) want.Add((d, x, y));
            }
        want.Sort((a, b) => a.d.CompareTo(b.d));
        int built = 0, pending = 0;
        double spent = 0;
        foreach (var (_, x, y) in want)
        {
            bool have = regions.TryGetValue((x, y), out var c);
            // The options changed what the trees are: build it again (nearest first, in the frame's budget).
            bool stale = have && (c.tier != TreeTier || c.drawTrees != DrawTrees);
            if (have && !check && !stale) continue;
            long sig = Signature(state, x, y);
            if (have && c.sig == sig && !stale) continue;
            if (built > 0 && spent >= BUILD_MS) { pending++; continue; }
            ulong t0 = Godot.Time.GetTicksUsec();
            if (have) c.node.QueueFree();
            regions[(x, y)] = Build(state, x, y) with { sig = sig };
            double ms = (Godot.Time.GetTicksUsec() - t0) / 1000.0;
            BuildMs += ms;
            spent += ms;
            built++;
        }
        Pending = pending;
        var drop = new List<(int, int)>();
        foreach (var (k, c) in regions)
            if (Distance(eye, k.Item1, k.Item2) > Range + REGION) drop.Add(k);
        foreach (var k in drop)
        {
            regions[k].node.QueueFree();
            regions.Remove(k);
        }
        int tc = 0, tt = 0;
        foreach (var (k, c) in regions)
        {
            tc += c.trees;
            tt += c.tris + (Middle(eye, k.Item1, k.Item2) < c.tier ? c.lodTris : c.crownTris);
        }
        TreeCount = tc;
        Triangles = tt;
    }

    void Clear()
    {
        foreach (var c in regions.Values) c.node.QueueFree();
        regions.Clear();
        Pending = TreeCount = Triangles = 0;
    }

    /// <summary>The distance (m, on the ground) from the eye to the nearest point of a region.</summary>
    static float Distance(Vector3 eye, int rx, int ry)
    {
        float x0 = rx * REGION, z0 = ry * REGION;
        float dx = Math.Max(0, Math.Max(x0 - eye.X, eye.X - (x0 + REGION)));
        float dz = Math.Max(0, Math.Max(z0 - eye.Z, eye.Z - (z0 + REGION)));
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The distance (m, on the ground) from the eye to a region's middle, about what its visibility ranges measure.</summary>
    static float Middle(Vector3 eye, int rx, int ry)
    {
        float dx = (rx + 0.5f) * REGION - eye.X, dz = (ry + 0.5f) * REGION - eye.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    long Signature(SimState state, int rx, int ry)
    {
        long h = 17 + (treesByRegion.TryGetValue((rx, ry), out var l) ? l.Count : 0);
        for (int cy = ry * REGION / Worldgen.CHUNK; cy < (ry + 1) * REGION / Worldgen.CHUNK; cy++)
            for (int cx = rx * REGION / Worldgen.CHUNK; cx < (rx + 1) * REGION / Worldgen.CHUNK; cx++)
                h = h * 3 + (Generated(state, cx * Worldgen.CHUNK, cy * Worldgen.CHUNK) ? 1 : 0);
        h = h * 5 + (Ready(state, rx * REGION, ry * REGION) ? 1 : 0);
        // Walls are what change a far region's look; their count over the region is enough to notice.
        var g = state.grid;
        int x0 = rx * REGION, y0 = ry * REGION;
        for (int y = y0; y < Math.Min(g.height, y0 + REGION); y += 2)
            for (int x = x0; x < Math.Min(g.width, x0 + REGION); x += 2)
                h = h * 31 + Grids.groundAt(g, y * g.width + x);
        return h;
    }

    Built Build(SimState state, int rx, int ry)
    {
        var g = state.grid;
        var node = new Node3D();
        parent.AddChild(node);
        int x0 = rx * REGION, y0 = ry * REGION, x1 = Math.Min(g.width, x0 + REGION), y1 = Math.Min(g.height, y0 + REGION);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int tris = 0;

        // Ground: a STEP grid of corner heights. Each corner is the mean of the sixteen tiles about it (as the near
        // ground's smooth edges count them: WorldMesh.Edges), so kinds of ground fade into each other along the
        // lines the near view draws, not in squares; a corner is water if half of them are, and a triangle with
        // water and land corners is cut where that share is a half (Banked: a bank as smooth as the near one, four
        // times coarser). A country road's tiles count as the ground it runs over (the road is drawn on it: Roads)
        // and a bridge's as water (its deck is drawn over it).
        bool ready = Ready(state, x0, y0);
        const int M = 2;
        int tw = x1 - x0 + 2 * M;
        var tiles = new (bool water, Color c)[tw * (y1 - y0 + 2 * M)];
        // What the plan makes of each tile not generated yet (PlanGround's: the trees below read it too).
        var planned = new (bool water, int ground, double wooded)[tiles.Length];
        for (int ty = 0; ty < y1 - y0 + 2 * M; ty++)
            for (int tx = 0; tx < tw; tx++)
            {
                int ax = x0 - M + tx, ay = y0 - M + ty;
                bool inside = ax >= x0 && ay >= y0 && ax < x1 && ay < y1;
                var (water, ground) = FarTile(state, ax, ay, ready && (inside || Ready(state, ax, ay)), out planned[ty * tw + tx]);
                tiles[ty * tw + tx] = (water, water ? WaterColour : GroundColour(ground));
            }
        int cw = (x1 - x0 + STEP - 1) / STEP + 1;
        var corners = new (Vector3 p, Color c, float wet)[cw * ((y1 - y0 + STEP - 1) / STEP + 1)];
        (Vector3 p, Color c, float wet) V(int x, int y)
        {
            int k = ((y - y0 + STEP - 1) / STEP) * cw + (x - x0 + STEP - 1) / STEP;
            if (corners[k].wet > 0) return corners[k];
            Color sum = new(0, 0, 0, 0);
            int wet = 0, land = 0;
            for (int dy = -2; dy < 2; dy++)
                for (int dx = -2; dx < 2; dx++)
                {
                    var t = tiles[(y + dy - y0 + M) * tw + (x + dx - x0 + M)];
                    if (t.water) wet++;
                    else { sum += t.c; land++; }
                }
            var c = land > 0 ? sum / land : GroundColour(0);
            float n = ((x * 73856093) ^ (y * 19349663)) % 1000 / 1000f;
            c = c.Lightened(0.06f + Math.Abs(n) * 0.035f);
            bool water = wet >= 8;
            float h = Terrain.Coarse(x, y, water);
            // (wet + 1, so a corner made is never 0: 0 is "not made yet".)
            return corners[k] = (new Vector3(x, h - SINK, y), c, wet + 1);
        }
        // The near water is a see-through sheet (58%) over the river bed: the far water is the two mixed, so they meet without a line.
        var waterC = WaterColour.Lightened(0.08f).Lerp(WorldLook.RiverBed.colour.Lightened(0.07f), 0.42f);
        for (int y = y0; y < y1; y += STEP)
            for (int x = x0; x < x1; x += STEP)
            {
                int xs = Math.Min(x + STEP, g.width), ys = Math.Min(y + STEP, g.height);
                var a = V(x, y); var b = V(xs, y); var c = V(xs, ys); var d = V(x, ys);
                var nrm = (b.p - a.p).Cross(c.p - a.p).Normalized();
                if (nrm.Y < 0) nrm = -nrm;
                st.SetNormal(nrm);
                tris += Banked(st, a, b, c, waterC) + Banked(st, a, c, d, waterC);
            }

        // What the buildings here are made of, as the near view has them (WorldLook.Of, Roofs.RoofOf): each plan's
        // rectangle with its walls' and its roof's colour. A floor that is no plan's is the old grey.
        var made = new List<(int x, int y, int w, int h, Color wall, Color roof)>();
        var lay = Ready(state, x0, y0) ? Plan(state) : null;
        if (lay != null)
        {
            var seen = new HashSet<double>();
            for (int cy = y0 / Worldplan.PLAN_CHUNK; cy <= (y1 - 1) / Worldplan.PLAN_CHUNK; cy++)
                for (int cx = x0 / Worldplan.PLAN_CHUNK; cx <= (x1 - 1) / Worldplan.PLAN_CHUNK; cx++)
                    foreach (var id in lay.regions.LotsInChunk(cx, cy))
                    {
                        if (!seen.Add(id)) continue;
                        var lot = lay.regions.Lot(id);
                        var placed = Lots.layoutLot(lay.seed, lot).buildings;
                        for (int bi = 0; bi < placed.Count; bi++)
                        {
                            var p = placed[bi];
                            bool turned = p.facing == "east" || p.facing == "west";
                            double roll = Roofs.Roll(lay.seed, lot, bi);
                            made.Add((p.ox, p.oy, turned ? p.plan.h : p.plan.w, turned ? p.plan.w : p.plan.h,
                                WorldLook.Of(p.plan.kind, p.plan.style, roll).outside.colour, Roofs.RoofOf(p.plan.style == "trailer" ? "trailer" : p.plan.kind, roll).colour));
                        }
                    }
        }
        (Color wall, Color roof) Made(int x, int y)
        {
            foreach (var m in made) if (x >= m.x && y >= m.y && x < m.x + m.w && y < m.y + m.h) return (m.wall, m.roof);
            uint hh = unchecked((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u);
            hh ^= hh >> 15;
            return (WorldMesh.WALL, ROOFS[hh % ROOFS.Length]);
        }

        // Buildings: floor tiles merged into rectangles (greedy), each a block to the eaves with a flat roof.
        int w = x1 - x0, hgt = y1 - y0;
        var taken = new bool[w * hgt];
        bool Floor(int x, int y)
        {
            int i = y * g.width + x;
            if (Grids.tileAt(g, i) == "water") return false;
            int gi = Grids.groundAt(g, i);
            return gi >= 0 && gi < Grids.GROUNDS.Length && FLOORS.Contains(Grids.GROUNDS[gi]);
        }
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                if (taken[y * w + x] || !Floor(x0 + x, y0 + y)) continue;
                int ex = x;
                while (ex + 1 < w && !taken[y * w + ex + 1] && Floor(x0 + ex + 1, y0 + y)) ex++;
                int ey = y;
                while (ey + 1 < hgt)
                {
                    bool full = true;
                    for (int k = x; k <= ex && full; k++) full = !taken[(ey + 1) * w + k] && Floor(x0 + k, y0 + ey + 1);
                    if (!full) break;
                    ey++;
                }
                for (int yy = y; yy <= ey; yy++)
                    for (int xx = x; xx <= ex; xx++) taken[yy * w + xx] = true;
                float foot = Terrain.Coarse(x0 + x, y0 + y, false);
                var (wall, roof) = Made(x0 + x, y0 + y);
                Block(st, new Vector3(x0 + x, foot - 0.3f, y0 + y), new Vector3(x0 + ex + 1, foot + WorldMesh.WALL_H + 0.25f, y0 + ey + 1), wall, roof);
                tris += 10;
            }
        // Buildings on land not generated yet: the lots' footprints (Lots.layoutLot), each once, those whose middle is
        // in this region and in a chunk not generated (the generated ones are in the grid's floors above).
        foreach (var m in made)
        {
            int mx = m.x + m.w / 2, my = m.y + m.h / 2;
            if (mx < x0 || my < y0 || mx >= x1 || my >= y1 || Generated(state, mx, my)) continue;
            float foot = Terrain.Coarse(m.x, m.y, false);
            Block(st, new Vector3(m.x, foot - 0.3f, m.y), new Vector3(m.x + m.w, foot + WorldMesh.WALL_H + 0.25f, m.y + m.h), m.wall, m.roof);
            tris += 10;
        }
        // Country roads: the plan's lines as strips (Roads), their lane lines in a mesh of their own seen only near.
        var ls = new SurfaceTool();
        ls.Begin(Mesh.PrimitiveType.Triangles);
        int lineTris = 0;
        if (ready && Plan(state) is { } lay3) tris += Roads(st, ls, g, lay3.regions, x0, y0, x1, y1, ref lineTris);
        var mesh = st.Commit();
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, groundMat);
        node.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (lineTris > 0)
        {
            var lm = ls.Commit();
            lm.SurfaceSetMaterial(0, groundMat);
            node.AddChild(new MeshInstance3D { Mesh = lm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = LINES_TO });
            tris += lineTris;
        }

        // Trees: the far LOD, one MultiMesh per species, placed as Trees places them, to TreeTier; crowns past it.
        int treeCount = 0, lodTris = 0, crownTris = 0;
        if (DrawTrees)
        {
            var bySpecies = new List<(Transform3D t, Color c)>[Trees.SpeciesNames.Count];
            for (int s = 0; s < bySpecies.Length; s++) bySpecies[s] = new();
            if (treesByRegion.TryGetValue((rx, ry), out var list))
                foreach (var f in list)
                {
                    if (!Generated(state, f.x, f.y)) continue;
                    var (s, t, c) = Trees.Placement(g, f.x, f.y, coarse: true);
                    bySpecies[s].Add((t, c));
                }
            // Land not generated yet: the trees generateChunk would plant (open ground off roads, lots and the start's clearing, a roll under NatureMod.treeChance).
            if (Plan(state) is { } lay2 && Ready(state, x0, y0))
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (Generated(state, x, y) || Worldgen.isRoad(lay2, x, y) || Worldgen.onLot(lay2, x, y) || NatureMod.nearStart(lay2, x, y, NatureMod.START_CLEAR_RADIUS)) continue;
                        var (wet, _, wooded) = planned[(y - y0 + M) * tw + (x - x0 + M)];
                        if (wet || double.IsNaN(wooded) || NatureMod.roll(lay2.seed, x, y, 12) >= NatureMod.treeChanceAt(wooded)) continue;
                        var (s, t, c) = Trees.Placement(x, y, true, true);
                        bySpecies[s].Add((t, c));
                    }
            for (int s = 0; s < bySpecies.Length; s++) treeCount += bySpecies[s].Count;
            if (TreeTier > 0)
                for (int s = 0; s < bySpecies.Length; s++)
                {
                    if (bySpecies[s].Count == 0) continue;
                    var mesh2 = trees.TreeMesh(s, 2);
                    var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh2, InstanceCount = bySpecies[s].Count };
                    for (int k = 0; k < bySpecies[s].Count; k++)
                    {
                        mm.SetInstanceTransform(k, bySpecies[s][k].t);
                        mm.SetInstanceCustomData(k, bySpecies[s][k].c);
                    }
                    var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = treeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    if (!float.IsPositiveInfinity(TreeTier)) inst.VisibilityRangeEnd = TreeTier;
                    node.AddChild(inst);
                    lodTris += bySpecies[s].Count * TrisOf(mesh2);
                }
            if (!float.IsPositiveInfinity(TreeTier) && treeCount > 0)
            {
                var crowns = Crowns();
                int n = 0;
                for (int s = 0; s < bySpecies.Length; s++) n += bySpecies[s].Count * crowns[s].v.Length;
                var verts = new Vector3[n];
                var nrms = new Vector3[n];
                var cols = new Color[n];
                int at = 0;
                for (int s = 0; s < bySpecies.Length; s++)
                {
                    var cr = crowns[s];
                    foreach (var (t, tint) in bySpecies[s])
                        for (int k = 0; k < cr.v.Length; k++, at++)
                        {
                            verts[at] = t * cr.v[k];
                            nrms[at] = (t.Basis * cr.n[k]).Normalized();
                            cols[at] = new Color(cr.c[k].R * tint.R, cr.c[k].G * tint.G, cr.c[k].B * tint.B);
                        }
                }
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = verts;
                arrays[(int)Mesh.ArrayType.Normal] = nrms;
                arrays[(int)Mesh.ArrayType.Color] = cols;
                var cm = new ArrayMesh();
                cm.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                cm.SurfaceSetMaterial(0, groundMat);
                node.AddChild(new MeshInstance3D { Mesh = cm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeBegin = TreeTier });
                crownTris = n / 3;
            }
        }
        return new Built(node, 0, treeCount, tris, lodTris, crownTris, TreeTier, DrawTrees);
    }

    /// <summary>A ground triangle, cut where its corners' share of water is a half: the water's side in the water's
    /// colour, the land's in its corners' colours (a point on the cut takes its land end's). Triangles drawn.</summary>
    static int Banked(SurfaceTool st, (Vector3 p, Color c, float wet) a, (Vector3 p, Color c, float wet) b, (Vector3 p, Color c, float wet) c, Color water)
    {
        // Front faces wind clockwise seen from above (as WorldMesh's).
        if ((b.p - a.p).Cross(c.p - a.p).Y > 0) (b, c) = (c, b);
        var v = new[] { a, b, c };
        var f = new float[3];
        int wetCorners = 0;
        for (int k = 0; k < 3; k++) { f[k] = (v[k].wet - 1) / 16f - 0.5f; if (f[k] >= 0) wetCorners++; }
        if (wetCorners == 0 || wetCorners == 3)
        {
            for (int k = 0; k < 3; k++) { st.SetColor(wetCorners == 3 ? water : v[k].c); st.AddVertex(v[k].p); }
            return 1;
        }
        var wetPoly = new List<Vector3>(4);
        var dry = new List<(Vector3 p, Color c)>(4);
        for (int k = 0; k < 3; k++)
        {
            int e = (k + 1) % 3;
            if (f[k] >= 0) wetPoly.Add(v[k].p); else dry.Add((v[k].p, v[k].c));
            if ((f[k] >= 0) != (f[e] >= 0))
            {
                var p = v[k].p.Lerp(v[e].p, f[k] / (f[k] - f[e]));
                wetPoly.Add(p);
                dry.Add((p, f[k] >= 0 ? v[e].c : v[k].c));
            }
        }
        int n = 0;
        for (int k = 1; k + 1 < wetPoly.Count; k++, n++)
        {
            st.SetColor(water); st.AddVertex(wetPoly[0]); st.AddVertex(wetPoly[k]); st.AddVertex(wetPoly[k + 1]);
        }
        for (int k = 1; k + 1 < dry.Count; k++, n++)
        {
            st.SetColor(dry[0].c); st.AddVertex(dry[0].p); st.SetColor(dry[k].c); st.AddVertex(dry[k].p); st.SetColor(dry[k + 1].c); st.AddVertex(dry[k + 1].p);
        }
        return n;
    }

    /// <summary>A tile as the far ground draws it: water (a bridge's too) and its ground (a country road's: the ground it runs over).</summary>
    (bool water, int ground) FarTile(SimState state, int x, int y, bool ready, out (bool water, int ground, double wooded) plan)
    {
        var g = state.grid;
        int tx = Math.Clamp(x, 0, g.width - 1), ty = Math.Clamp(y, 0, g.height - 1), i = ty * g.width + tx;
        bool water;
        int ground;
        plan = (false, 0, double.NaN);
        if (Generated(state, tx, ty)) { water = Grids.tileAt(g, i) == "water"; ground = Grids.groundAt(g, i); }
        else (water, ground, _) = plan = PlanGround(state, tx, ty, ready);
        if (ground == BRIDGE) return (true, 0);
        if (ground == ROAD && !water && Country(state, tx, ty))
        {
            var (w, under, _) = PlanGround(state, tx, ty, false);
            return (w, under);
        }
        return (water, ground);
    }

    /// <summary>A road tile that is a country road's, not a town street's (by the plan; a region not planned yet: country).</summary>
    bool Country(SimState state, int x, int y)
    {
        var r = Plan(state)?.regions;
        if (r == null) return false;
        var (rx, ry) = r.RegionOf(x, y);
        return !r.IsPlanned(rx, ry) || r.RoadAt(x, y) == Worldplan.ROAD;
    }

    readonly Dictionary<(int, int), RoadNet> nets = new();
    WorldRegions? netsFor;

    /// <summary>The road net of the plan region holding tile (x, y) (kept: all its far regions share it; made again
    /// while a neighbour it reaches into isn't planned).</summary>
    RoadNet NetOf(WorldRegions regions, int x, int y)
    {
        if (!ReferenceEquals(netsFor, regions)) { nets.Clear(); netsFor = regions; }
        var k = regions.RegionOf(x, y);
        if (nets.TryGetValue(k, out var hit)) return hit;
        var w = regions.WindowOf(k.rx, k.ry);
        var net = RoadNet.Gather(regions, w.x0 - 8, w.y0 - 8, w.x1 + 8, w.y1 + 8, ask: false);
        if (!net.Missing) nets[k] = net;
        return net;
    }

    /// <summary>The far ground's height at a point (on its STEP grid's triangles, split as Build splits them), over land or water.</summary>
    static float FarHeight(Grid g, float px, float pz, bool water)
    {
        int gx = (int)Math.Floor(px / STEP) * STEP, gz = (int)Math.Floor(pz / STEP) * STEP;
        float u = (px - gx) / STEP, v = (pz - gz) / STEP;
        float C(int x, int z) => Terrain.Coarse(Math.Clamp(x, 0, g.width), Math.Clamp(z, 0, g.height), water);
        float a = C(gx, gz), b = C(gx + STEP, gz), c = C(gx + STEP, gz + STEP), d = C(gx, gz + STEP);
        return (u >= v ? a + u * (b - a) + v * (c - b) : a + v * (d - a) + u * (c - d)) - SINK;
    }

    /// <summary>The country roads whose line runs through this region (the plan's lines: RoadNet), as the near view
    /// draws them but coarser: a strip ROAD_REACH each side, ROAD_STEP m a piece, on the far ground's heights; a
    /// disc where roads meet; over water a deck at the bridge's height (Heights.DECK over the surface). Their edge
    /// and centre lines go in `lines` (seen to LINES_TO: past that they would be under a pixel). Triangles drawn
    /// (the strips'; the lines' are added to lineTris).</summary>
    int Roads(SurfaceTool st, SurfaceTool lines, Grid g, WorldRegions regions, int x0, int y0, int x1, int y1, ref int lineTris)
    {
        var net = NetOf(regions, x0, y0);
        float H = RoadRibbons.HALF;
        var asphalt = GroundColour(ROAD).Lightened(0.1f);
        var deck = GroundColour(BRIDGE).Lightened(0.08f);
        var line = RoadRibbons.LINE;
        int tris = 0, lt = 0;
        bool Mine(Vector2 p) => p.X >= x0 && p.X < x1 && p.Y >= y0 && p.Y < y1;
        // What the line runs over: 0 none (a street, off the plan), 1 a country road, 2 water (a bridge).
        int Kind(Vector2 p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            if (x < 0 || y < 0 || x >= g.width || y >= g.height || regions.RoadAt(x, y) != Worldplan.ROAD) return 0;
            return NatureMod.isWater(regions.nature, x, y) ? 2 : 1;
        }
        void Quad(SurfaceTool s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            s.SetColor(col);
            s.SetNormal(Vector3.Up);
            RoadRibbons.Tri(s, a, b, c);
            RoadRibbons.Tri(s, a, c, d);
        }
        static Vector3 P(Vector2 p, Vector2 n, float o, float h) { var q = p + n * o; return new Vector3(q.X, h, q.Y); }
        for (int li = 0; li < net.lines.Count; li++)
        {
            var pts = RoadNet.Resample(net.lines[li], ROAD_STEP);
            float along = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], mid = (a + b) / 2;
                float len = a.DistanceTo(b), from = along;
                along += len;
                if (!Mine(mid) || len < 1e-4f) continue;
                int kind = Kind(mid);
                if (kind == 0) continue;
                Vector2 na = RoadNet.Across(pts, i), nb = RoadNet.Across(pts, i + 1);
                bool wet = kind == 2;
                float lift = wet ? (float)Heights.DECK + SINK : ROAD_LIFT;
                float Ht(Vector2 q) => FarHeight(g, q.X, q.Y, wet) + lift;
                // A deck is flat across (at its line's height); a road lies on the ground at its edges and middle.
                float ha = Ht(a), hb = Ht(b);
                Vector3 l0 = P(a, na, -H, wet ? ha : Ht(a - na * H)), r0 = P(a, na, H, wet ? ha : Ht(a + na * H));
                Vector3 r1 = P(b, nb, H, wet ? hb : Ht(b + nb * H)), l1 = P(b, nb, -H, wet ? hb : Ht(b - nb * H));
                Vector3 m0 = P(a, na, 0, ha), m1 = P(b, nb, 0, hb);
                Quad(st, l0, m0, m1, l1, wet ? deck : asphalt);
                Quad(st, m0, r0, r1, m1, wet ? deck : asphalt);
                tris += 4;
                if (wet) continue;
                var across = (na + nb).Normalized();
                bool Clear(float o) => net.DistOther(li, mid + across * o) > H - 0.05f && !net.NearJoin(mid + across * o, H);
                foreach (var (o0, o1, dashed) in new[] { (-H + 0.24f, -H + 0.36f, false), (H - 0.36f, H - 0.24f, false), (-0.08f, 0.08f, true) })
                {
                    float e = (o0 + o1) / 2;
                    if (!Clear(e)) continue;
                    float h0 = Ht(a + na * e) + LINE_UP, h1 = Ht(b + nb * e) + LINE_UP;
                    // The centre line two metres in four, as near: the piece split at its dashes' ends.
                    for (float s0 = from; s0 < from + len - 1e-3f;)
                    {
                        float s1 = dashed ? Math.Min(from + len, (float)Math.Floor(s0 / 2) * 2 + 2) : from + len;
                        if (!dashed || (int)Math.Floor(s0 / 2) % 2 == 0)
                        {
                            float t0 = (s0 - from) / len, t1 = (s1 - from) / len;
                            Vector2 pa = a.Lerp(b, t0), pb = a.Lerp(b, t1), qa = na.Lerp(nb, t0), qb = na.Lerp(nb, t1);
                            float ga = Mathf.Lerp(h0, h1, t0), gb = Mathf.Lerp(h0, h1, t1);
                            Quad(lines, P(pa, qa, o0, ga), P(pa, qa, o1, ga), P(pb, qb, o1, gb), P(pb, qb, o0, gb), line);
                            lt += 2;
                        }
                        s0 = s1;
                    }
                }
            }
        }
        var look = new Look(asphalt, Surfaces.None);
        foreach (var j in net.joins)
            if (Mine(j) && Kind(j) == 1) { RoadRibbons.Disc(st, j, H, look, q => FarHeight(g, q.X, q.Y, false) + ROAD_LIFT, 10, uv: false); tris += 10; }
        lineTris += lt;
        return tris;
    }

    (Vector3[] v, Vector3[] n, Color[] c)[]? crowns;

    /// <summary>Each species' crown: a diamond (a point at the foliage's base, a ring of four at its widest, a tip)
    /// as tall and wide as its far LOD's foliage, in the foliage's colour: 8 triangles, for trees a few pixels
    /// tall (the trunk under it would be a pixel).</summary>
    (Vector3[] v, Vector3[] n, Color[] c)[] Crowns()
    {
        if (crowns != null) return crowns;
        crowns = new (Vector3[], Vector3[], Color[])[Trees.SpeciesNames.Count];
        for (int s = 0; s < crowns.Length; s++)
        {
            var m = trees.TreeMesh(s, 2);
            var pts = new List<(Vector3 p, Color c)>();
            for (int k = 0; k < m.GetSurfaceCount(); k++)
            {
                var arr = m.SurfaceGetArrays(k);
                var vs = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var cs = arr[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil ? arr[(int)Mesh.ArrayType.Color].AsColorArray() : null;
                for (int i = 0; i < vs.Length; i++) pts.Add((vs[i], cs != null && i < cs.Length ? cs[i] : new Color(0.25f, 0.35f, 0.2f)));
            }
            // The foliage (the green points; a snag has none, so all of it): its top, base, widest radius (the
            // 90th percentile) and the height that's at, and its mean colour.
            var leaves = pts.FindAll(p => p.c.G > p.c.R * 1.05f);
            if (leaves.Count < 3) leaves = pts;
            float top = 0.1f;
            Color leaf = new(0, 0, 0, 0);
            var ys = new List<float>();
            var rings = new List<(float r, float y)>();
            foreach (var (p, c) in leaves)
            {
                top = Math.Max(top, p.Y);
                leaf += c;
                ys.Add(p.Y);
                rings.Add((new Vector2(p.X, p.Z).Length(), p.Y));
            }
            leaf /= leaves.Count;
            ys.Sort();
            rings.Sort((a, b) => a.r.CompareTo(b.r));
            float baseY = ys[(int)(ys.Count * 0.05f)];
            var (r, wy) = rings[(int)(rings.Count * 0.9f)];
            wy = Math.Clamp(wy, baseY + (top - baseY) * 0.2f, baseY + (top - baseY) * 0.6f);
            var foot = new Vector3(0, baseY, 0);
            var tip = new Vector3(0, top, 0);
            var ring = new Vector3[4];
            for (int i = 0; i < 4; i++) ring[i] = new Vector3(MathF.Cos(i * MathF.PI / 2) * r, wy, MathF.Sin(i * MathF.PI / 2) * r);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var col = new List<Color>();
            void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                // Outward from the trunk's axis, front faces wound as Block winds them.
                var mid = (a + b + c) / 3;
                if ((b - a).Cross(c - a).Dot(new Vector3(mid.X, 0, mid.Z)) > 0) { (b, c) = (c, b); (cb, cc) = (cc, cb); }
                foreach (var (p, pc) in new[] { (a, ca), (b, cb), (c, cc) })
                {
                    v.Add(p);
                    // Smooth, so the crown lights as a mass: the ring out from the axis and a little up, the foot down, the tip up.
                    n.Add(p == foot ? Vector3.Down : p == tip ? Vector3.Up : (new Vector3(p.X, 0, p.Z).Normalized() + Vector3.Up * 0.35f).Normalized());
                    col.Add(pc);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                Tri(foot, a, b, leaf.Darkened(0.3f), leaf, leaf);
                Tri(tip, b, a, leaf, leaf, leaf);
            }
            crowns[s] = (v.ToArray(), n.ToArray(), col.ToArray());
        }
        return crowns;
    }

    Layout? layout;
    WorldInfo? layoutFor;

    /// <summary>The world plan as Worldgen lays it out (cached per world), or null on a hand-made map.</summary>
    Layout? Plan(SimState state)
    {
        if (state.world == null) return null;
        if (!ReferenceEquals(layoutFor, state.world)) { layout = Worldgen.layout(state.world.seed, state.world.size, state.settings.map); layoutFor = state.world; }
        return layout;
    }

    /// <summary>Whether the plan of the region holding tile (x, y) is made; if not, it's asked for on a worker
    /// (WorldRegions.PlanSoon), so the far field never stalls a frame making one (~1 s each at 1024).</summary>
    bool Ready(SimState state, int x, int y)
    {
        var r = Plan(state)?.regions;
        if (r == null) return true;
        var (rx, ry) = r.RegionOf(x, y);
        if (r.IsPlanned(rx, ry)) return true;
        r.PlanSoon(rx, ry);
        return false;
    }

    /// <summary>Whether the chunk holding tile (x, y) has been generated (a hand-made map: all of it).</summary>
    static bool Generated(SimState state, int x, int y)
    {
        var w = state.world;
        if (w == null) return true;
        int chunks = (int)w.chunks, cx = x / Worldgen.CHUNK, cy = y / Worldgen.CHUNK;
        return cx < 0 || cy < 0 || cx >= chunks || cy >= chunks || Js.Get(w.generated, (double)cy * chunks + cx) == 1;
    }

    /// <summary>What Worldgen.drawTerrain would make a tile (water, and its ground index), with a lot's yard as
    /// Lots.layoutLot draws it: the far field's look at land not generated yet. And the woodedness of open ground, for
    /// its tree (NaN elsewhere: a road, a lot, water, the fence).</summary>
    (bool water, int ground, double wooded) PlanGround(SimState state, int x, int y, bool planned)
    {
        var l = Plan(state)!;
        int grass = 0;
        const double NONE = double.NaN;
        if (Worldgen.fenceAt(l, x, y) != null) return (false, grass, NONE);
        // A region whose plan isn't made yet (being made on a worker: Ready): nature alone, no roads or lots.
        bool road = planned && Worldgen.isRoad(l, x, y);
        bool water = NatureMod.isWater(l, x, y);
        if (road) return (false, Array.IndexOf(Grids.GROUNDS, water ? "bridge" : "road"), NONE);
        if (water) return (true, grass, NONE);
        if (planned && l.regions.IsSidewalk(x, y)) return (false, Array.IndexOf(Grids.GROUNDS, "paving"), NONE);
        if (planned && Worldgen.onLot(l, x, y))
        {
            var lot = l.regions.LotAt(x, y);
            if (lot != null)
            {
                string? yard = Lots.layoutLot(l.seed, lot).yard?.Invoke(x, y);
                int gi = yard == null ? -1 : Array.IndexOf(Grids.GROUNDS, yard);
                return (false, gi >= 0 ? gi : grass, NONE);
            }
            return (false, grass, NONE);
        }
        double w = NatureMod.woodedness(l, x, y);
        if (w > NatureMod.FOREST_AT) return (false, Worldgen.FOREST, w);
        if (NatureMod.isDirt(l, x, y)) return (false, Array.IndexOf(Grids.GROUNDS, "dirt"), w);
        return (false, grass, w);
    }

    static readonly Color WaterColour = Ctx.Css(COLOURS.tiles["water"]);
    static Color[]? groundColours;

    static Color GroundColour(int gi)
    {
        groundColours ??= Array.ConvertAll(Grids.GROUNDS, WorldLook.GroundColour);
        return gi >= 0 && gi < groundColours.Length ? groundColours[gi] : groundColours[0];
    }

    readonly Dictionary<Mesh, int> trisOf = new();

    int TrisOf(Mesh m)
    {
        if (trisOf.TryGetValue(m, out var n)) return n;
        n = 0;
        for (int s = 0; s < m.GetSurfaceCount(); s++)
        {
            var arrays = m.SurfaceGetArrays(s);
            var idx = arrays[(int)Mesh.ArrayType.Index];
            n += (idx.VariantType != Variant.Type.Nil ? idx.AsInt32Array().Length : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length) / 3;
        }
        return trisOf[m] = n;
    }

    /// <summary>A block: four walls and a roof (no floor), walls in one colour, the roof in another.</summary>
    static void Block(SurfaceTool st, Vector3 lo, Vector3 hi, Color wall, Color roof)
    {
        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            if ((b - a).Cross(c - a).Dot(n) > 0) (b, d) = (d, b);
            st.SetNormal(n);
            st.SetColor(col);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        var w = wall.Darkened(0.08f);
        Q(new(lo.X, hi.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z), Vector3.Up, roof);
        Q(new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z), Vector3.Forward, w);
        Q(new(hi.X, lo.Y, hi.Z), new(lo.X, lo.Y, hi.Z), new(lo.X, hi.Y, hi.Z), new(hi.X, hi.Y, hi.Z), Vector3.Back, w);
        Q(new(lo.X, lo.Y, hi.Z), new(lo.X, lo.Y, lo.Z), new(lo.X, hi.Y, lo.Z), new(lo.X, hi.Y, hi.Z), Vector3.Left, w);
        Q(new(hi.X, lo.Y, lo.Z), new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(hi.X, hi.Y, lo.Z), Vector3.Right, w);
    }
}
