// The mid tier's dressed character (docs/characters.md section 7, "Dress the mid tier"): what an
// outfit shows (Dresser.Plan: bare body regions and garment pieces on view, cut limbs taken away)
// merged into one skinned mesh with one surface, drawn with one material (character_merged.gdshader).
//
//   - Sources: the body regions of test_mannequin.glb and the garment pieces of garments.glb, read
//     and decoded once. Every piece is skinned to the same rig; bone indices are remapped by bind
//     name onto one skin (the body's), so the merged mesh plays on any instance of the skeleton.
//   - The mesh holds only what shows, so it's cached by that (pieces and cuts). Colours aren't in
//     it: per vertex (CUSTOM0, floats) is the garment's slot and pattern seed (or, for skin, its
//     colour); the character's seven slot colours, its pattern seed and its dirt, blood, torn and
//     wet are three instance values on the one mesh. So a crowd in random dyes shares a mesh per
//     combination of garments. (Not vertex COLOR: Godot stores it in 8 bits, converted from sRGB.)
//   - No face shape keys and no stump caps in the merge (mid range shows neither).

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LoonieRoo.Game.Characters;

public static class OutfitMerge
{
    const string BodyGlb = "res://assets/characters/test_mannequin.glb";

    // One surface of a source mesh, decoded (bones already remapped to the shared skin).
    sealed record Part(Vector3[] Verts, Vector3[] Normals, Vector2[] Uvs, int[] Bones, float[] Weights, int[] Index, Color? OwnColor);

    static Dictionary<string, List<Part>>? sources;  // body region or garment mesh name -> its surfaces
    static Skin? skin;
    static ShaderMaterial? material;
    static readonly Dictionary<string, ArrayMesh> cache = new();

    public static int Cached => cache.Count;
    public static int Merges;
    public static double MergeMs, LoadMs;  // time spent merging, and reading the sources once
    public static ShaderMaterial Material => material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Characters/character_merged.gdshader") };
    public static Skin Skin { get { Load(); return skin!; } }

    static void Load()
    {
        if (sources != null) return;
        ulong t0 = Time.GetTicksUsec();
        sources = new();
        var bodyScene = GD.Load<PackedScene>(BodyGlb).Instantiate<Node3D>();
        var torso = (MeshInstance3D)bodyScene.FindChild("torso", true, false);
        skin = torso.Skin;  // every body region shares the armature's one skin
        var index = new Dictionary<string, int>();
        for (int i = 0; i < skin.GetBindCount(); i++) index[skin.GetBindName(i)] = i;
        void Add(MeshInstance3D m)
        {
            var s = m.Skin;
            var map = new int[s.GetBindCount()];
            for (int i = 0; i < map.Length; i++)
            {
                string name = s.GetBindName(i);
                if (!index.TryGetValue(name, out map[i])) throw new InvalidOperationException($"merge: {m.Name} binds {name}, not in the body's skin");
                var a = s.GetBindPose(i);
                var b = skin.GetBindPose(map[i]);
                if (a.Origin.DistanceTo(b.Origin) > 1e-3f || !a.Basis.IsEqualApprox(b.Basis))
                    GD.PrintErr($"merge: {m.Name} bind pose of {name} differs from the body's");
            }
            var parts = new List<Part>();
            for (int s2 = 0; s2 < m.Mesh.GetSurfaceCount(); s2++)
            {
                var arr = m.Mesh.SurfaceGetArrays(s2);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var uv = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var b = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var w = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = b.Length / v.Length;  // 4 (or 8) influences per vertex: keep 4
                var bones = new int[v.Length * 4];
                var weights = new float[v.Length * 4];
                for (int i = 0; i < v.Length; i++)
                    for (int k = 0; k < 4 && k < per; k++)
                    {
                        bones[i * 4 + k] = map[b[i * per + k]];
                        weights[i * 4 + k] = w[i * per + k];
                    }
                // A surface with its own material (the face's features) keeps that colour, as skin.
                Color? own = s2 > 0 && m.Mesh.SurfaceGetMaterial(s2) is BaseMaterial3D bm ? bm.AlbedoColor : null;
                parts.Add(new Part(v, arr[(int)Mesh.ArrayType.Normal].AsVector3Array(), uv.Length == v.Length ? uv : new Vector2[v.Length],
                    bones, weights, arr[(int)Mesh.ArrayType.Index].AsInt32Array(), own));
            }
            sources[m.Name] = parts;
        }
        foreach (var r in Dresser.BodyRegions)
            if (bodyScene.FindChild(r, true, false) is MeshInstance3D m) Add(m);
        var clothes = Garments.Scene.Instantiate<Node3D>();
        foreach (var m in All(clothes)) Add(m);
        clothes.Free();
        bodyScene.Free();
        LoadMs = (Time.GetTicksUsec() - t0) / 1000.0;
    }

    static IEnumerable<MeshInstance3D> All(Node n)
    {
        if (n is MeshInstance3D m) yield return m;
        foreach (var c in n.GetChildren())
            foreach (var x in All(c)) yield return x;
    }

    /// <summary>The merged mesh for an outfit (cached by the pieces it shows).</summary>
    public static ArrayMesh For(Outfit o, ISet<string> cut)
    {
        Load();
        var (bare, shown) = Dresser.Plan(o, cut);
        var parts = bare.Select(r => (mesh: r, slot: -1, seed: 0))
            .Concat(shown.Select(x => (mesh: x.p.Mesh, slot: Array.IndexOf(Garments.Slots, x.g.Slot), seed: Dresser.GarmentSeed(x.g.Id))))
            .Where(x => sources!.ContainsKey(x.mesh)).ToList();
        string key = string.Join("|", parts.Select(x => x.mesh));
        if (cache.TryGetValue(key, out var hit)) return hit;
        ulong t0 = Time.GetTicksUsec();
        var mesh = Merge(parts);
        MergeMs += (Time.GetTicksUsec() - t0) / 1000.0;
        Merges++;
        cache[key] = mesh;
        return mesh;
    }

    static ArrayMesh Merge(List<(string mesh, int slot, int seed)> parts)
    {
        var surfaces = parts.SelectMany(p => sources![p.mesh].Select(s => (s, p.slot, p.seed))).ToList();
        int nv = surfaces.Sum(x => x.s.Verts.Length), ni = surfaces.Sum(x => x.s.Index.Length);
        var verts = new Vector3[nv];
        var normals = new Vector3[nv];
        var uvs = new Vector2[nv];
        var custom = new float[nv * 4];
        var bones = new int[nv * 4];
        var weights = new float[nv * 4];
        var indices = new int[ni];
        int at = 0, ai = 0;
        var skinLinear = Garments.Skin.SrgbToLinear();
        foreach (var (s, slot, seed) in surfaces)
        {
            int n = s.Verts.Length;
            Array.Copy(s.Verts, 0, verts, at, n);
            Array.Copy(s.Normals, 0, normals, at, n);
            Array.Copy(s.Uvs, 0, uvs, at, n);
            Array.Copy(s.Bones, 0, bones, at * 4, n * 4);
            Array.Copy(s.Weights, 0, weights, at * 4, n * 4);
            for (int i = 0; i < s.Index.Length; i++) indices[ai + i] = at + s.Index[i];
            // CUSTOM0: skin (and the face) = (r, g, b, 0) in linear colour; a garment =
            // (slot, 0, 0, (seed + 1) / 98), its colour coming from the character's slot colours.
            Vector4 c;
            if (slot >= 0 && s.OwnColor == null) c = new Vector4(slot, 0, 0, (seed + 1) / 98f);
            else
            {
                var k = s.OwnColor?.SrgbToLinear() ?? skinLinear;
                c = new Vector4(k.R, k.G, k.B, 0);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (at + i) * 4;
                custom[j] = c.X; custom[j + 1] = c.Y; custom[j + 2] = c.Z; custom[j + 3] = c.W;
            }
            at += n;
            ai += s.Index.Length;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Custom0] = custom;
        arrays[(int)Mesh.ArrayType.Bones] = bones;
        arrays[(int)Mesh.ArrayType.Weights] = weights;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        var format = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        return mesh;
    }

    /// <summary>A colour as one float: 8-bit sRGB r, g, b in 24 bits (exact in a float).</summary>
    static float Pack(Color c) => c.R8 * 65536f + c.G8 * 256f + c.B8;

    /// <summary>Put an outfit on a mid-tier character (its one MeshInstance3D on the shared skeleton).</summary>
    public static void Dress(MeshInstance3D body, Outfit o, ISet<string> cut, float seed)
    {
        body.Mesh = For(o, cut);
        body.Skin = Skin;
        body.MaterialOverride = Material;
        var dye = Garments.Slots.Select(slot => o.Worn.TryGetValue(slot, out var id) && Garments.Defs.ContainsKey(id) ? Pack(o.ColorOf(id)) : 0f).ToArray();
        body.SetInstanceShaderParameter("dyes_a", new Vector4(dye[0], dye[1], dye[2], dye[3]));
        body.SetInstanceShaderParameter("dyes_b", new Vector4(dye[4], dye[5], dye[6], seed));
        body.SetInstanceShaderParameter("wear", new Vector4((float)o.Dirt, (float)o.Blood, (float)o.Torn, (float)o.Wet));
    }

    public static int Triangles(ArrayMesh m) => Enumerable.Range(0, m.GetSurfaceCount()).Sum(s => m.SurfaceGetArrayIndexLen(s) / 3);
}
