// A file of low-poly models (one mesh per name, vertex colours with metalness in the alpha: art/scripts/lowpoly.py)
// and the drawing of many of them: one MultiMesh per model, filled from a list of transforms.
//
// What draws with it says where things stand; this only loads and batches. Use: Begin(), Add(model, transform) for
// each thing, End(). A model not added this time draws nothing. The items and furniture (Items.cs, Furniture.cs),
// Fields, Sites, Aircraft, Cars and Others' props use it. Variants: a file's "crate", "crate_v1", "crate_v2" are one
// kind's models, and Pick chooses one by a hash of the thing's id.
// The shader lays the detail kit over the colour (docs/textures.md): each face's layer is in the model's UV, laid by
// its UV2 in metres, so it stays on the thing when it drives, is carried or is turned.

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using static LoonieRoo.Game.IdHash;

namespace LoonieRoo.Game.World3D;

public sealed class ModelSet
{
    const string SHADER = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
#include ""res://assets/shaders/surface_detail.gdshaderinc""
#include ""res://assets/shaders/snow_cover.gdshaderinc""
void vertex() {
    SURFACE_BY_OBJECT
}
void fragment() {
    ALBEDO = snowy(COLOR.rgb * SURFACE_DETAIL, (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    METALLIC = COLOR.a * $metal;
    ROUGHNESS = mix($dull, $shiny, COLOR.a);
}";

    /// <summary>One material for each shader text (snow or none, metal and roughness), shared by every set using it.</summary>
    static readonly Dictionary<string, ShaderMaterial> materials = new();

    readonly Node3D parent;
    readonly Dictionary<string, Mesh> meshes = new();
    readonly Dictionary<string, MultiMeshInstance3D> nodes = new();
    readonly Dictionary<string, List<Transform3D>> batch = new();

    /// <summary>Things drawn by the last End (the debug panel, the self-test).</summary>
    public int Count { get; private set; }

    /// <summary>The models in the file at `path`. `snow`: snow on what faces up (outdoors), or none (a cabin's inside);
    /// `metal` how metallic the metal parts (the colour's alpha) are, and the roughness from none of it (`dull`) to
    /// all of it (`shiny`).</summary>
    public ModelSet(Node3D parent, string path, bool shadows = true, bool snow = true, float metal = 0.7f, float dull = 0.9f, float shiny = 0.45f)
    {
        this.parent = parent;
        this.shadows = shadows;
        string N(float v) => v.ToString(CultureInfo.InvariantCulture);
        string text = SHADER.Replace("$metal", N(metal)).Replace("$dull", N(dull)).Replace("$shiny", N(shiny));
        if (!snow) text = text.Replace("snowy(COLOR.rgb * SURFACE_DETAIL, (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz)", "COLOR.rgb * SURFACE_DETAIL");
        if (!materials.TryGetValue(text, out var m)) materials[text] = m = new ShaderMaterial { Shader = Surfaces.Shader(text) };
        material = m;
        var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        Collect(scene);
        scene.Free();
        foreach (var name in meshes.Keys)
        {
            int v = name.LastIndexOf("_v", StringComparison.Ordinal);
            if (v <= 0 || !int.TryParse(name.AsSpan(v + 2), out _)) continue;
            string kind = name[..v];
            if (!meshes.ContainsKey(kind)) continue;
            if (!variants.TryGetValue(kind, out var list)) variants[kind] = list = new() { kind };
            list.Add(name);
        }
        foreach (var list in variants.Values) list.Sort(string.CompareOrdinal);
    }

    readonly bool shadows;
    readonly ShaderMaterial material;
    /// <summary>A kind's models: its own name first, then its variants in order.</summary>
    readonly Dictionary<string, List<string>> variants = new();
    /// <summary>What model "" draws (none if null): the items' grey box for a kind with no model.</summary>
    public Mesh? Fallback;

    void Collect(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) mi.Mesh.SurfaceSetMaterial(i, material);
            meshes[n.Name] = mi.Mesh;
        }
        foreach (var c in n.GetChildren()) Collect(c);
    }

    public bool Has(string model) => meshes.ContainsKey(model);

    /// <summary>The file's model names.</summary>
    public IEnumerable<string> Names => meshes.Keys;

    /// <summary>A model's mesh, for something drawn on its own node (a rotor that spins).</summary>
    public Mesh MeshOf(string model) => meshes[model];

    /// <summary>A model's mesh, or null if the file has none of that name.</summary>
    public Mesh? MeshOrNull(string model) => meshes.GetValueOrDefault(model);

    /// <summary>The model a thing of this kind is drawn with: the kind's, or one of its variants by the id's hash
    /// (`variant` instead, if given); "" if the file has none.</summary>
    public string Pick(string kind, string id, int? variant = null)
    {
        if (!meshes.ContainsKey(kind)) return "";
        if (!variants.TryGetValue(kind, out var list)) return kind;
        int k = variant ?? (int)(Mix(Fnv(id)) % (uint)list.Count);
        return list[k % list.Count];
    }

    public void Begin()
    {
        foreach (var list in batch.Values) list.Clear();
    }

    public void Add(string model, Transform3D at)
    {
        if (!meshes.ContainsKey(model) && !(model == "" && Fallback != null)) return;
        if (!batch.TryGetValue(model, out var list)) batch[model] = list = new();
        list.Add(at);
    }

    /// <summary>Draw what was added since Begin. `bounds` is where they all are (for culling: instances move, the node doesn't).</summary>
    public void End(Aabb bounds)
    {
        int count = 0;
        foreach (var (model, list) in batch)
        {
            if (!nodes.TryGetValue(model, out var node))
            {
                if (list.Count == 0) continue;
                node = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = model == "" ? Fallback : meshes[model] } };
                node.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                parent.AddChild(node);
                nodes[model] = node;
            }
            var mm = node.Multimesh;
            if (mm.InstanceCount < list.Count) mm.InstanceCount = Math.Max(list.Count, mm.InstanceCount * 2);
            mm.VisibleInstanceCount = list.Count;
            for (int k = 0; k < list.Count; k++) mm.SetInstanceTransform(k, list[k]);
            node.CustomAabb = bounds;
            count += list.Count;
        }
        Count = count;
    }

    /// <summary>A box round a point, wide enough for everything drawn near you.</summary>
    public static Aabb Around(Vector3 eye, float reach) => new(new Vector3(eye.X - reach, -60, eye.Z - reach), new Vector3(2 * reach, 260, 2 * reach));

    /// <summary>Standing at (x, z) on the ground height y, turned so the model's +X (its front, or its length) points
    /// along the grid direction (dx, dy): 1, 0 is east, 0, 1 south.</summary>
    public static Transform3D Facing(float x, float y, float z, double dx, double dy) =>
        new(new Basis(Vector3.Up, (float)-Math.Atan2(dy, dx)), new Vector3(x, y, z));
}
