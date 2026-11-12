using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.World3D;

/// <summary>SurfaceTool's calls the chunks and roofs use (SetColor, SetUV, SetUV2, SetNormal, AddVertex, Commit), kept in
/// C# lists and handed to Godot once per surface. SurfaceTool is a call into the engine for every attribute of every
/// vertex: four a vertex, most of a chunk's build (2026-11-12, log/2026-11-12-17-performance.md). The same meaning:
/// each Set holds for the vertices after it, and an attribute set once is on every vertex (the ones before it get the
/// default: zero, a colour of opaque black).</summary>
public sealed class MeshBuf
{
    readonly List<Vector3> verts = new(), normals = new();
    readonly List<Color> colours = new();
    readonly List<Vector2> uvs = new(), uv2s = new();
    bool hasNormal, hasColour, hasUV, hasUV2;
    Vector3 normal;
    Color colour = new(0, 0, 0, 1);
    Vector2 uv, uv2;

    /// <summary>For SurfaceTool's shape: triangles only.</summary>
    public void Begin(Mesh.PrimitiveType _) { }

    public void SetNormal(Vector3 n) { normal = n; hasNormal = true; }
    public void SetColor(Color c) { colour = c; hasColour = true; }
    public void SetUV(Vector2 v) { uv = v; hasUV = true; }
    public void SetUV2(Vector2 v) { uv2 = v; hasUV2 = true; }

    public void AddVertex(Vector3 v)
    {
        verts.Add(v);
        normals.Add(normal);
        colours.Add(colour);
        uvs.Add(uv);
        uv2s.Add(uv2);
    }

    /// <summary>The vertices as a surface on a new mesh, or added to `to`; no surface if there are none.</summary>
    public ArrayMesh Commit(ArrayMesh? to = null)
    {
        var mesh = to ?? new ArrayMesh();
        if (verts.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        if (hasNormal) arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        if (hasColour) arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        if (hasUV) arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        if (hasUV2) arrays[(int)Mesh.ArrayType.TexUV2] = uv2s.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
