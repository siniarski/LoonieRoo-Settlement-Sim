// A file of low-poly models (one mesh per name, vertex colours with metalness in the alpha: art/scripts/lowpoly.py)
// and the drawing of many of them: one MultiMesh per model, filled from a list of transforms.
//
// What draws with it says where things stand; this only loads and batches. Use: Begin(), Add(model, transform) for
// each thing, End(). A model not added this time draws nothing. Fields, Sites, Aircraft, Cars and Others' props use
// it; the items and the furniture (Items.cs, Furniture.cs) came first and have the same code of their own.

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.World3D;

public sealed class ModelSet
{
    const string SHADER = @"
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
void fragment() {
    ALBEDO = COLOR.rgb;
    METALLIC = COLOR.a * 0.7;
    ROUGHNESS = mix(0.9, 0.45, COLOR.a);
}";

    static ShaderMaterial? shared;

    readonly Node3D parent;
    readonly Dictionary<string, Mesh> meshes = new();
    readonly Dictionary<string, MultiMeshInstance3D> nodes = new();
    readonly Dictionary<string, List<Transform3D>> batch = new();

    /// <summary>Things drawn by the last End (the debug panel, the self-test).</summary>
    public int Count { get; private set; }

    public ModelSet(Node3D parent, string path, bool shadows = true)
    {
        this.parent = parent;
        this.shadows = shadows;
        shared ??= new ShaderMaterial { Shader = new Shader { Code = SHADER } };
        var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        Collect(scene);
        scene.Free();
    }

    readonly bool shadows;

    void Collect(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) mi.Mesh.SurfaceSetMaterial(i, shared);
            meshes[n.Name] = mi.Mesh;
        }
        foreach (var c in n.GetChildren()) Collect(c);
    }

    public bool Has(string model) => meshes.ContainsKey(model);

    /// <summary>A model's mesh, for something drawn on its own node (a rotor that spins).</summary>
    public Mesh MeshOf(string model) => meshes[model];

    public void Begin()
    {
        foreach (var list in batch.Values) list.Clear();
    }

    public void Add(string model, Transform3D at)
    {
        if (!meshes.ContainsKey(model)) return;
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
                node = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = meshes[model] } };
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
