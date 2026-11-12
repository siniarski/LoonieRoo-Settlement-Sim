// Vertex animation textures (docs/characters.md section 7, the far / crowd tier): a character's clips
// baked once into a texture of vertex positions (one row per frame, one texel per vertex), drawn by
// a MultiMesh with no skeleton at all: one draw call per outfit for a whole horde.
//
// Bake: the merged mesh of an outfit (OutfitMerge.For: one surface, the skin's bind poses), a skeleton
// playing each clip, sampled at Fps; each vertex skinned on the CPU (the sum over its bones of
// weight x bone pose x bind pose) into the character's space. Clips are stacked rows in one texture.
// The drawn mesh is the same vertex order with the dye resolved (CUSTOM0: linear rgb, garment seed;
// skin's a = 0, as character_merged.gdshader).
// Per instance (vat.gdshader): CUSTOM x = clip start row x 1024 + frame count, y = phase 0..1,
// z = playback speed, w = pattern seed; COLOR = dirt, blood, torn, wet.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static LoonieRoo.Game.Nodes;

namespace LoonieRoo.Game.Characters;

public sealed class VatBake
{
    public ArrayMesh Mesh = null!;
    public ImageTexture Positions = null!;
    public readonly Dictionary<string, (int start, int frames)> Clips = new();
    public int Vertices, Rows;
    public double BakeMs;
    public ShaderMaterial Material = null!;

    /// <summary>The instance custom data for a clip at a phase and speed, with a pattern seed.</summary>
    public Color Custom(string clip, float phase, float speed, float seed)
    {
        var (start, frames) = Clips[clip];
        return new Color(start * 1024 + frames, phase, speed, seed);
    }
}

public static class Vat
{
    public const float Fps = 30;
    const string Mannequin = "res://assets/characters/test_mannequin_merged.glb";

    public static VatBake Bake(Node host, Outfit outfit, IReadOnlyList<string> clips)
    {
        ulong t0 = Time.GetTicksUsec();
        var src = OutfitMerge.For(outfit, new HashSet<string>());
        var skin = OutfitMerge.Skin;
        var a = src.SurfaceGetArrays(0);
        var verts = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var bones = a[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights = a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var custom = a[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
        int nv = verts.Length;

        var actor = GD.Load<PackedScene>(Mannequin).Instantiate<Node3D>();
        host.AddChild(actor);
        var skel = Find<Skeleton3D>(actor)!;
        var player = Find<AnimationPlayer>(actor)!;
        var toChar = actor.GlobalTransform.AffineInverse() * skel.GlobalTransform;  // skeleton space -> the character's
        // Skin binds -> skeleton bones, by name.
        var bindBone = new int[skin.GetBindCount()];
        var bindPose = new Transform3D[skin.GetBindCount()];
        for (int i = 0; i < bindBone.Length; i++) { bindBone[i] = skel.FindBone(skin.GetBindName(i)); bindPose[i] = skin.GetBindPose(i); }

        var bake = new VatBake { Vertices = nv };
        var rows = new List<Vector3[]>();
        foreach (var clip in clips)
        {
            var anim = player.GetAnimation(clip);
            int frames = Math.Max(1, (int)Math.Round(anim.Length * Fps));
            bake.Clips[clip] = (rows.Count, frames);
            player.Play(clip);
            var m = new Transform3D[bindBone.Length];
            for (int f = 0; f < frames; f++)
            {
                player.Seek(f / Fps, true);
                for (int i = 0; i < m.Length; i++) m[i] = toChar * skel.GetBoneGlobalPose(bindBone[i]) * bindPose[i];
                var row = new Vector3[nv];
                for (int v = 0; v < nv; v++)
                {
                    var p = Vector3.Zero;
                    for (int k = 0; k < 4; k++)
                    {
                        float w = weights[v * 4 + k];
                        if (w > 0) p += (m[bones[v * 4 + k]] * verts[v]) * w;
                    }
                    row[v] = p;
                }
                rows.Add(row);
            }
        }
        actor.QueueFree();

        // Positions: RGBA float, nv x rows.
        bake.Rows = rows.Count;
        var data = new byte[nv * rows.Count * 16];
        for (int r = 0; r < rows.Count; r++)
            for (int v = 0; v < nv; v++)
            {
                int o = (r * nv + v) * 16;
                BitConverter.TryWriteBytes(new Span<byte>(data, o, 4), rows[r][v].X);
                BitConverter.TryWriteBytes(new Span<byte>(data, o + 4, 4), rows[r][v].Y);
                BitConverter.TryWriteBytes(new Span<byte>(data, o + 8, 4), rows[r][v].Z);
            }
        bake.Positions = ImageTexture.CreateFromImage(Image.CreateFromData(nv, rows.Count, false, Image.Format.Rgbaf, data));

        // The drawn mesh: the same vertices (positions come from the texture), the dye resolved.
        var dyed = (float[])custom.Clone();
        for (int v = 0; v < nv; v++)
        {
            float code = custom[v * 4 + 3];
            if (code <= 0) continue;  // skin: its colour is already there
            int slot = (int)Math.Round(custom[v * 4]);
            var c = outfit.Worn.TryGetValue(Garments.Slots[slot], out var id) ? outfit.ColorOf(id).SrgbToLinear() : Colors.White;
            dyed[v * 4] = c.R; dyed[v * 4 + 1] = c.G; dyed[v * 4 + 2] = c.B;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = a[(int)Mesh.ArrayType.TexUV];
        arrays[(int)Mesh.ArrayType.Custom0] = dyed;
        arrays[(int)Mesh.ArrayType.Index] = a[(int)Mesh.ArrayType.Index];
        var mesh = new ArrayMesh();
        var format = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        mesh.CustomAabb = new Aabb(new Vector3(-1.2f, -0.2f, -1.2f), new Vector3(2.4f, 2.4f, 2.4f));  // the texture moves the vertices
        bake.Mesh = mesh;
        bake.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Characters/vat.gdshader") };
        bake.Material.SetShaderParameter("vat", bake.Positions);
        bake.Material.SetShaderParameter("fps", Fps);
        bake.BakeMs = (Time.GetTicksUsec() - t0) / 1000.0;
        return bake;
    }
}
