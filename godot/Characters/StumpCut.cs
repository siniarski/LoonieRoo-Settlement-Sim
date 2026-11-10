// Garments and bodies that end at a stump (docs/characters.md "Missing limbs"): the sim's lostLimbs
// ("armLeft:elbow", "legRight:knee"; Wounds.lostLimb) cut a character's skinned meshes at load, here, not
// in Blender. Why here: the game's characters are whole skinned meshes per LOD (the look models, clothes
// painted into the body's map or as garment shells over it), so a Blender variant per side and level would
// multiply every subject, face, outfit and LOD file by 15 limb combinations; a cut by plane and bone weights
// works on any skinned mesh (body, garment, a future modelled garment) without a re-export, and is made once
// per mesh and cut set (cached).
//
// How a cut goes, per surface (in the mesh's bind space: the rig rests in a T-pose):
//   - the plane: across the limb a little below the joint (a stump: 7 cm of forearm, 10 cm of shin), its
//     normal along the bone below (elbow to wrist, knee to ankle);
//   - triangles with a vertex weighted to the limb are clipped by it (new vertices on the cut edges, every
//     attribute interpolated: positions, normals, UVs, blend shapes); the rest of the body is never touched
//     (a leg's plane would also cross the other leg: it isn't weighted to this one);
//   - what's left of the limb is weighted to the bone above the cut only (the stump moves with the upper
//     arm or thigh, whatever the clip does with the lost forearm or shin);
//   - each cut edge loop (found by position, so UV seams don't break it) is closed by a cap built out from
//     the loop's own vertices, so there is no gap, taking their UVs, so it is the colour of what was cut:
//     a sleeve's cloth, a trouser leg's denim, or skin.
// The caps, by who and where:
//   living, arm   the sleeve's end folded and pinned: a rounded end, the empty sleeve folded back up the back
//                 of the arm as a flat tongue, a safety pin through it (a small metal surface)
//   living, leg   the trouser leg knotted: a rounded end, a knot, a short tail hanging from it
//   the dead      torn: the loop's edge ragged in tatters, raw flesh inside (a dark red surface)
//
//   var cut = StumpCut.Cut(mesh, skin, skeleton, new[] { "armLeft:elbow" }, dead: false);  // null if nothing to cut
//   StumpCut.Apply(characterRoot, lostLimbs, dead)   every skinned mesh under the root (not hair: attach it after)

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LoonieRoo.Game.Characters;

public static class StumpCut
{
    /// <summary>How far below the joint the cut is (m): the stump's length.</summary>
    static readonly Dictionary<string, double> BELOW = new() { ["elbow"] = 0.07, ["knee"] = 0.10, ["wrist"] = -0.02, ["ankle"] = -0.03 };

    static readonly Dictionary<(ulong, string), ArrayMesh?> cache = new();
    static StandardMaterial3D? flesh, metal;

    static StandardMaterial3D Flesh() => flesh ??= new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.05f, 0.04f), Roughness = 0.55f };
    static StandardMaterial3D Metal() => metal ??= new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.62f, 0.60f), Metallic = 0.9f, Roughness = 0.35f };

    /// <summary>A lost limb's cut: which side's bones, the plane, and the bone the stump keeps.</summary>
    sealed class Plane
    {
        public string Limb = "";            // "arm" or "leg"
        public Vector3 Point, Dir, Back;    // Dir: along the lost part (outward); Back: the body's back (-Z), across the limb
        public HashSet<int> LimbBinds = new(), Removed = new();
        public int Keep = -1;
    }

    /// <summary>Cuts every skinned mesh under root (the character's own pieces: call before attaching hair) for
    /// these lost limbs; returns how many meshes changed.</summary>
    public static int Apply(Node root, IReadOnlyCollection<string> lostLimbs, bool dead)
    {
        if (lostLimbs.Count == 0) return 0;
        var skel = FindSkeleton(root);
        if (skel == null) return 0;
        int n = 0;
        foreach (var mi in MeshesUnder(root).ToList())
        {
            if (mi.Mesh is not ArrayMesh am || mi.Skin == null) continue;
            var cut = Cut(am, mi.Skin, skel, lostLimbs, dead);
            if (cut == null) continue;
            mi.Mesh = cut;
            n++;
        }
        return n;
    }

    /// <summary>The mesh cut for these lost limbs, or null when none of them touches it. Cached per mesh, limbs and dead.</summary>
    public static ArrayMesh? Cut(ArrayMesh mesh, Skin skin, Skeleton3D skel, IReadOnlyCollection<string> lostLimbs, bool dead)
    {
        string key = string.Join(",", lostLimbs.OrderBy((l) => l, StringComparer.Ordinal)) + (dead ? "/dead" : "");
        var ck = (mesh.GetInstanceId(), key);
        if (cache.TryGetValue(ck, out var hit)) return hit;
        var planes = lostLimbs.Select((l) => MakePlane(l, skin, skel)).Where((p) => p != null).Select((p) => p!).ToList();
        ArrayMesh? result = planes.Count == 0 ? null : Build(mesh, planes, dead);
        cache[ck] = result;
        return result;
    }

    // ------------------------------------------------------------------ the plane

    static Plane? MakePlane(string lost, Skin skin, Skeleton3D skel)
    {
        var parts = lost.Split(':');
        if (parts.Length != 2 || !BELOW.TryGetValue(parts[1], out double below)) return null;
        string limb = parts[0].StartsWith("arm") ? "arm" : parts[0].StartsWith("leg") ? "leg" : "";
        string side = parts[0].EndsWith("Left") ? "Left" : parts[0].EndsWith("Right") ? "Right" : "";
        if (limb == "" || side == "") return null;
        // The joint the cut is under, the bone below it (lost), and the next joint down (the direction).
        (string top, string joint, string next) = (limb, parts[1]) switch
        {
            ("arm", "elbow") => ("UpperArm", "LowerArm", "Hand"),
            ("arm", "wrist") => ("LowerArm", "Hand", "Hand"),
            ("leg", "knee") => ("UpperLeg", "LowerLeg", "Foot"),
            ("leg", "ankle") => ("LowerLeg", "Foot", "Foot"),
            _ => ("", "", ""),
        };
        if (top == "") return null;
        var binds = new Dictionary<string, int>();
        var rest = new Dictionary<string, Vector3>();
        for (int i = 0; i < skin.GetBindCount(); i++)
        {
            string name = skin.GetBindName(i).ToString();
            if (name == "" && skin.GetBindBone(i) >= 0) name = skel.GetBoneName(skin.GetBindBone(i));
            binds[name] = i;
            rest[name] = skin.GetBindPose(i).AffineInverse().Origin;  // the bone's rest origin in mesh space
        }
        string Top = side + top, Joint = side + joint, Next = side + next;
        if (!binds.ContainsKey(Top) || !rest.ContainsKey(Joint)) return null;
        var p = new Plane { Limb = limb, Keep = binds[Top] };
        // The limb: the top bone and everything under it; removed: the joint's bone and everything under it.
        int topBone = skel.FindBone(Top), jointBone = skel.FindBone(Joint);
        for (int b = 0; b < skel.GetBoneCount(); b++)
        {
            if (!binds.TryGetValue(skel.GetBoneName(b), out int bind)) continue;
            if (IsUnder(skel, b, topBone)) p.LimbBinds.Add(bind);
            if (IsUnder(skel, b, jointBone)) p.Removed.Add(bind);
        }
        Vector3 j = rest[Joint];
        Vector3 dir;
        if (Next != Joint && rest.TryGetValue(Next, out var nx)) dir = (nx - j).Normalized();
        else dir = (j - rest[Top]).Normalized();  // a wrist or ankle: along the bone above
        p.Dir = dir;
        p.Point = j + dir * (float)below;
        // The back, across the limb: -Z (the character faces +Z) made square to the limb.
        var back = new Vector3(0, 0, -1);
        p.Back = (back - dir * back.Dot(dir)).Normalized();
        if (p.Back.LengthSquared() < 0.5f) p.Back = new Vector3(0, 1, 0);
        return p;
    }

    static bool IsUnder(Skeleton3D skel, int bone, int ancestor)
    {
        for (int b = bone; b >= 0; b = skel.GetBoneParent(b))
            if (b == ancestor) return true;
        return false;
    }

    // ------------------------------------------------------------------ the cut

    /// <summary>A surface as plain lists: float channels (position, normal, tangent, UVs, colour, blend shapes)
    /// that interpolate, and the bone indices that don't.</summary>
    sealed class Surf
    {
        public List<Vector3> pos = new(), nrm = new();
        public List<float> tan = new();          // 4 a vertex
        public List<Vector2> uv = new(), uv2 = new();
        public List<Color> col = new();
        public List<int> bones = new();          // k a vertex
        public List<float> weights = new();      // k a vertex
        public int k = 4;
        public List<List<Vector3>> bsPos = new(), bsNrm = new();
        public List<List<float>> bsTan = new();
        public bool bsDelta;                     // blend shapes stored as offsets (else as whole positions)
        public List<int> idx = new();
        public bool hasNrm, hasTan, hasUv, hasUv2, hasCol;
        /// <summary>+1 if this mesh's front faces have (b-a)x(c-a) along their normal, -1 if against it (read off the mesh).</summary>
        public float front = -1;
        public int Count => pos.Count;

        /// <summary>A triangle wound to face along outward, by this mesh's own convention.</summary>
        public void Tri(int a, int b, int c, Vector3 outward)
        {
            var g = (pos[b] - pos[a]).Cross(pos[c] - pos[a]);
            if (g.Dot(outward) * front < 0) (b, c) = (c, b);
            idx.Add(a); idx.Add(b); idx.Add(c);
        }

        public int Lerp(int a, int b, float t, int bonesFrom)
        {
            pos.Add(pos[a].Lerp(pos[b], t));
            if (hasNrm) nrm.Add(nrm[a].Lerp(nrm[b], t).Normalized());
            if (hasTan) { for (int c = 0; c < 3; c++) tan.Add(Mathf.Lerp(tan[a * 4 + c], tan[b * 4 + c], t)); tan.Add(tan[a * 4 + 3]); }
            if (hasUv) uv.Add(uv[a].Lerp(uv[b], t));
            if (hasUv2) uv2.Add(uv2[a].Lerp(uv2[b], t));
            if (hasCol) col.Add(col[a].Lerp(col[b], t));
            for (int c = 0; c < k; c++) { bones.Add(bones[bonesFrom * k + c]); weights.Add(weights[bonesFrom * k + c]); }
            for (int s = 0; s < bsPos.Count; s++)
            {
                bsPos[s].Add(bsPos[s][a].Lerp(bsPos[s][b], t));
                if (bsNrm[s].Count > 0) bsNrm[s].Add(bsNrm[s][a].Lerp(bsNrm[s][b], t));
                if (bsTan[s].Count > 0) for (int c = 0; c < 4; c++) bsTan[s].Add(Mathf.Lerp(bsTan[s][a * 4 + c], bsTan[s][b * 4 + c], t));
            }
            return pos.Count - 1;
        }

        /// <summary>A new vertex copying source v's UVs and colour, at p with normal n, on one bone.</summary>
        public int Copy(int v, Vector3 p, Vector3 n, int bone)
        {
            pos.Add(p);
            if (hasNrm) nrm.Add(n);
            if (hasTan)
            {
                var t = n.Cross(Mathf.Abs(n.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
                tan.Add(t.X); tan.Add(t.Y); tan.Add(t.Z); tan.Add(1);
            }
            if (hasUv) uv.Add(uv[v]);
            if (hasUv2) uv2.Add(uv2[v]);
            if (hasCol) col.Add(col[v]);
            for (int c = 0; c < k; c++) { bones.Add(c == 0 ? bone : 0); weights.Add(c == 0 ? 1 : 0); }
            for (int s = 0; s < bsPos.Count; s++)
            {
                bsPos[s].Add(bsDelta ? Vector3.Zero : p);
                if (bsNrm[s].Count > 0) bsNrm[s].Add(bsDelta ? Vector3.Zero : n);
                if (bsTan[s].Count > 0) for (int c = 0; c < 4; c++) bsTan[s].Add(bsDelta || !hasTan ? 0 : tan[^(4 - c)]);
            }
            return pos.Count - 1;
        }
    }

    /// <summary>Geometry for a surface of its own (flesh, the pin): triangles already wound.</summary>
    sealed record Part(Vector3[] p, Vector3[] n, int[] i, int bone, int k);

    static ArrayMesh? Build(ArrayMesh mesh, List<Plane> planes, bool dead)
    {
        var outMesh = new ArrayMesh { BlendShapeMode = mesh.BlendShapeMode };
        for (int b = 0; b < mesh.GetBlendShapeCount(); b++) outMesh.AddBlendShape(mesh.GetBlendShapeName(b));
        bool changed = false;
        var extraFlesh = new List<Part>();
        var extraMetal = new List<Part>();
        bool delta = false;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var sf = Read(mesh, s, arrays);
            if (s == 0) delta = sf.bsDelta;
            bool touched = false;
            foreach (var pl in planes)
            {
                var loops = Clip(sf, pl, out bool hit);
                if (!hit) continue;
                touched = true;
                Reweight(sf, pl);
                foreach (var loop in loops)
                    Cap(sf, pl, loop, dead, extraFlesh, extraMetal);
            }
            changed |= touched;
            var fmt = mesh.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
            outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Write(sf), BlendArrays(sf), null, fmt);
            outMesh.SurfaceSetMaterial(outMesh.GetSurfaceCount() - 1, mesh.SurfaceGetMaterial(s));
        }
        if (!changed) return null;
        AddExtra(outMesh, extraFlesh, Flesh(), delta);
        AddExtra(outMesh, extraMetal, Metal(), delta);
        return outMesh;
    }

    static Surf Read(ArrayMesh mesh, int s, Godot.Collections.Array a)
    {
        var sf = new Surf();
        sf.pos = a[(int)Mesh.ArrayType.Vertex].AsVector3Array().ToList();
        int n = sf.pos.Count;
        if (a[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil) { sf.nrm = a[(int)Mesh.ArrayType.Normal].AsVector3Array().ToList(); sf.hasNrm = sf.nrm.Count == n; }
        if (a[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil) { sf.tan = a[(int)Mesh.ArrayType.Tangent].AsFloat32Array().ToList(); sf.hasTan = sf.tan.Count == n * 4; }
        if (a[(int)Mesh.ArrayType.TexUV].VariantType != Variant.Type.Nil) { sf.uv = a[(int)Mesh.ArrayType.TexUV].AsVector2Array().ToList(); sf.hasUv = sf.uv.Count == n; }
        if (a[(int)Mesh.ArrayType.TexUV2].VariantType != Variant.Type.Nil) { sf.uv2 = a[(int)Mesh.ArrayType.TexUV2].AsVector2Array().ToList(); sf.hasUv2 = sf.uv2.Count == n; }
        if (a[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil) { sf.col = a[(int)Mesh.ArrayType.Color].AsColorArray().ToList(); sf.hasCol = sf.col.Count == n; }
        sf.bones = a[(int)Mesh.ArrayType.Bones].AsInt32Array().ToList();
        sf.weights = a[(int)Mesh.ArrayType.Weights].AsFloat32Array().ToList();
        sf.k = n > 0 ? Math.Max(1, sf.bones.Count / n) : 4;
        var idx = a[(int)Mesh.ArrayType.Index].AsInt32Array();
        sf.idx = idx.Length > 0 ? idx.ToList() : Enumerable.Range(0, n).ToList();
        var bs = mesh.SurfaceGetBlendShapeArrays(s);
        double absDiff = 0, absVal = 0;
        foreach (var shape in bs)
        {
            var p = shape[(int)Mesh.ArrayType.Vertex].AsVector3Array().ToList();
            sf.bsPos.Add(p);
            sf.bsNrm.Add(shape[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil ? shape[(int)Mesh.ArrayType.Normal].AsVector3Array().ToList() : new());
            sf.bsTan.Add(shape[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil ? shape[(int)Mesh.ArrayType.Tangent].AsFloat32Array().ToList() : new());
            for (int v = 0; v < Math.Min(n, p.Count); v += 7) { absDiff += (p[v] - sf.pos[v]).Length(); absVal += p[v].Length(); }
        }
        sf.bsDelta = absVal < absDiff;  // offsets are small next to whole positions (the body is a metre tall)
        if (sf.hasNrm)
        {
            double vote = 0;
            for (int t = 0; t + 2 < sf.idx.Count; t += 3 * 17)
            {
                int i0 = sf.idx[t], i1 = sf.idx[t + 1], i2 = sf.idx[t + 2];
                vote += (sf.pos[i1] - sf.pos[i0]).Cross(sf.pos[i2] - sf.pos[i0]).Dot(sf.nrm[i0] + sf.nrm[i1] + sf.nrm[i2]);
            }
            sf.front = vote >= 0 ? 1 : -1;
        }
        return sf;
    }

    static Godot.Collections.Array Write(Surf sf)
    {
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        a[(int)Mesh.ArrayType.Vertex] = sf.pos.ToArray();
        if (sf.hasNrm) a[(int)Mesh.ArrayType.Normal] = sf.nrm.ToArray();
        if (sf.hasTan) a[(int)Mesh.ArrayType.Tangent] = sf.tan.ToArray();
        if (sf.hasUv) a[(int)Mesh.ArrayType.TexUV] = sf.uv.ToArray();
        if (sf.hasUv2) a[(int)Mesh.ArrayType.TexUV2] = sf.uv2.ToArray();
        if (sf.hasCol) a[(int)Mesh.ArrayType.Color] = sf.col.ToArray();
        a[(int)Mesh.ArrayType.Bones] = sf.bones.ToArray();
        a[(int)Mesh.ArrayType.Weights] = sf.weights.ToArray();
        a[(int)Mesh.ArrayType.Index] = sf.idx.ToArray();
        return a;
    }

    static Godot.Collections.Array<Godot.Collections.Array> BlendArrays(Surf sf)
    {
        var list = new Godot.Collections.Array<Godot.Collections.Array>();
        for (int s = 0; s < sf.bsPos.Count; s++)
        {
            var a = new Godot.Collections.Array();
            a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = sf.bsPos[s].ToArray();
            if (sf.bsNrm[s].Count == sf.Count) a[(int)Mesh.ArrayType.Normal] = sf.bsNrm[s].ToArray();
            if (sf.bsTan[s].Count == sf.Count * 4) a[(int)Mesh.ArrayType.Tangent] = sf.bsTan[s].ToArray();
            list.Add(a);
        }
        return list;
    }

    static float LimbWeight(Surf sf, int v, HashSet<int> binds)
    {
        float w = 0;
        for (int c = 0; c < sf.k; c++)
            if (binds.Contains(sf.bones[v * sf.k + c])) w += sf.weights[v * sf.k + c];
        return w;
    }

    /// <summary>Clips the surface's triangles that belong to the limb by the plane (keeps the body's side).
    /// Returns the cut's edge loops (vertex indices on the plane, in order).</summary>
    static List<List<int>> Clip(Surf sf, Plane pl, out bool hit)
    {
        hit = false;
        int n = sf.Count;
        var d = new float[n];
        var limb = new bool[n];
        for (int v = 0; v < n; v++)
        {
            d[v] = (sf.pos[v] - pl.Point).Dot(pl.Dir);
            limb[v] = LimbWeight(sf, v, pl.LimbBinds) > 0.5f;
        }
        var edgeVert = new Dictionary<(int, int), int>();
        int OnEdge(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (edgeVert.TryGetValue(key, out int nv)) return nv;
            float t = d[a] / (d[a] - d[b]);
            nv = sf.Lerp(a, b, t, d[a] <= 0 ? a : b);
            edgeVert[key] = nv;
            return nv;
        }
        var outIdx = new List<int>(sf.idx.Count);
        var ringEdges = new List<(int a, int b)>();
        for (int t = 0; t + 2 < sf.idx.Count; t += 3)
        {
            int a = sf.idx[t], b = sf.idx[t + 1], c = sf.idx[t + 2];
            bool inLimb = limb[a] || limb[b] || limb[c];
            bool outA = d[a] > 0, outB = d[b] > 0, outC = d[c] > 0;
            if (!inLimb || (!outA && !outB && !outC)) { outIdx.Add(a); outIdx.Add(b); outIdx.Add(c); continue; }
            hit = true;
            if (outA && outB && outC) continue;
            // Sutherland-Hodgman on one triangle: the kept polygon, and the edge it has on the plane.
            var poly = new List<int>(4);
            int enter = -1, exit = -1;
            int[] tri = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                int p = tri[i], q = tri[(i + 1) % 3];
                bool pin = d[p] <= 0, qin = d[q] <= 0;
                if (pin) poly.Add(p);
                if (pin != qin)
                {
                    int nv = OnEdge(p, q);
                    poly.Add(nv);
                    if (pin) exit = nv; else enter = nv;
                }
            }
            for (int i = 1; i + 1 < poly.Count; i++) { outIdx.Add(poly[0]); outIdx.Add(poly[i]); outIdx.Add(poly[i + 1]); }
            if (exit >= 0 && enter >= 0) ringEdges.Add((exit, enter));
        }
        sf.idx = outIdx;
        return hit ? Loops(sf, ringEdges) : new();
    }

    /// <summary>The cut edges chained into loops by position (a UV seam splits a vertex in two; its halves
    /// are the same point of the loop).</summary>
    static List<List<int>> Loops(Surf sf, List<(int a, int b)> edges)
    {
        var keyOf = new Dictionary<(long, long, long), int>();
        int Key(int v)
        {
            var p = sf.pos[v];
            var k = ((long)Math.Round(p.X * 20000), (long)Math.Round(p.Y * 20000), (long)Math.Round(p.Z * 20000));
            if (!keyOf.TryGetValue(k, out int id)) keyOf[k] = id = keyOf.Count;
            return id;
        }
        var next = new Dictionary<int, int>();
        var rep = new Dictionary<int, int>();
        foreach (var (a, b) in edges)
        {
            int ka = Key(a), kb = Key(b);
            if (ka == kb) continue;
            next[ka] = kb;
            rep.TryAdd(ka, a);
            rep.TryAdd(kb, b);
        }
        var loops = new List<List<int>>();
        var seen = new HashSet<int>();
        foreach (int start in next.Keys.OrderBy((k) => k))
        {
            if (seen.Contains(start)) continue;
            var loop = new List<int>();
            int at = start;
            while (!seen.Contains(at))
            {
                seen.Add(at);
                loop.Add(rep[at]);
                if (!next.TryGetValue(at, out at)) break;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>What's left of the limb follows the bone above the cut: weights on the lost bones go to it.</summary>
    static void Reweight(Surf sf, Plane pl)
    {
        for (int v = 0; v < sf.Count; v++)
        {
            int o = v * sf.k;
            bool any = false;
            for (int c = 0; c < sf.k; c++)
                if (sf.weights[o + c] > 0 && pl.Removed.Contains(sf.bones[o + c])) { sf.bones[o + c] = pl.Keep; any = true; }
            if (!any) continue;
            // Merge the slots that now name the same bone.
            for (int c = 0; c < sf.k; c++)
                for (int e = c + 1; e < sf.k; e++)
                    if (sf.bones[o + e] == sf.bones[o + c] && sf.weights[o + e] > 0)
                    {
                        sf.weights[o + c] += sf.weights[o + e];
                        sf.weights[o + e] = 0;
                        sf.bones[o + e] = 0;
                    }
        }
    }

    // ------------------------------------------------------------------ the caps

    static float Hash01(int a, int b)
    {
        uint h = (uint)a * 2654435761u ^ (uint)b * 2246822519u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        return (h & 0xffff) / 65535f;
    }

    /// <summary>Closes one cut loop: a rounded end grown from the loop's own vertices, then the pinned fold,
    /// the knot or the torn edge.</summary>
    static void Cap(Surf sf, Plane pl, List<int> loop, bool dead,
        List<Part> fleshOut, List<Part> metalOut)
    {
        int m = loop.Count;
        var c = Vector3.Zero;
        foreach (int v in loop) c += sf.pos[v];
        c /= m;
        float r = loop.Average((v) => (sf.pos[v] - c).Length());
        if (r < 0.004f) return;
        // Order the loop counter-clockwise seen from outside (from +Dir), so faces wind the same way every time.
        float turn = 0;
        for (int i = 0; i < m; i++) turn += (sf.pos[loop[i]] - c).Cross(sf.pos[loop[(i + 1) % m]] - c).Dot(pl.Dir);
        if (turn < 0) loop.Reverse();
        var dir = pl.Dir;
        int bone = pl.Keep;

        void Tri(int a, int b, int e) => sf.Tri(a, b, e, sf.hasNrm ? sf.nrm[a] + sf.nrm[b] + sf.nrm[e] : sf.pos[a] - c + dir * r);
        // Rows: a ring each, from the loop to the end, each vertex copying its loop vertex's UV.
        List<int> Row(Func<int, Vector3> at, Func<int, Vector3> normal)
        {
            var row = new List<int>(m);
            for (int i = 0; i < m; i++) row.Add(sf.Copy(loop[i], at(i), normal(i), bone));
            return row;
        }
        void Band(List<int> lo, List<int> hi)
        {
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                Tri(lo[i], hi[j], hi[i]);
                Tri(lo[i], lo[j], hi[j]);
            }
        }

        if (dead)
        {
            // Torn: tatters hanging past the cut (cloth or skin, as cut), the raw end set back inside.
            var tip = Row((i) =>
            {
                var o = sf.pos[loop[i]] - c;
                float len = 0.003f + 0.05f * Mathf.Pow(Hash01(i, 11), 2.2f) * (pl.Limb == "leg" ? 1.3f : 0.35f);
                return c + o * (0.92f + 0.12f * Hash01(i, 5)) + dir * len;
            }, (i) => (sf.pos[loop[i]] - c).Normalized());
            var start = Row((i) => sf.pos[loop[i]], (i) => (sf.pos[loop[i]] - c).Normalized());
            Band(start, tip);
            // The raw end: a low dome of flesh just inside the loop.
            var fp = new List<Vector3>(); var fn = new List<Vector3>(); var fi = new List<int>();
            for (int i = 0; i < m; i++) { var o = sf.pos[loop[i]] - c; fp.Add(c + o * 0.9f - dir * 0.004f); fn.Add((o.Normalized() + dir).Normalized()); }
            fp.Add(c + dir * (r * 0.4f)); fn.Add(dir);
            for (int i = 0; i < m; i++) Wound(fp, fi, m, i, (i + 1) % m, dir, sf.front);
            fleshOut.Add(new Part(fp.ToArray(), fn.ToArray(), fi.ToArray(), bone, sf.k));
            return;
        }

        // The rounded end: rows closing in over a quarter circle, the end flattened to 0.75 of the radius.
        const int rows = 3;
        float depth = r * 0.75f;
        var prev = Row((i) => sf.pos[loop[i]], (i) => (sf.pos[loop[i]] - c).Normalized());
        for (int k = 1; k <= rows; k++)
        {
            float th = k / (float)(rows + 1) * Mathf.Pi / 2;
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            var row = Row((i) => c + (sf.pos[loop[i]] - c) * cs + dir * depth * sn,
                          (i) => ((sf.pos[loop[i]] - c).Normalized() * cs + dir * sn).Normalized());
            Band(prev, row);
            prev = row;
        }
        // Its middle takes the UV of the loop's vertex nearest the back (the fold's side: what shows).
        int backV = loop.OrderByDescending((v) => (sf.pos[v] - c).Dot(pl.Back)).First();
        int apex = sf.Copy(backV, c + dir * depth, dir, bone);
        for (int i = 0; i < m; i++) Tri(prev[i], apex, prev[(i + 1) % m]);

        var side = pl.Back.Cross(dir).Normalized();
        if (pl.Limb == "arm")
        {
            // Pinned: the empty sleeve folded back up the back of the arm, lying on it, a pin through its top.
            var top = Flap(sf, pl, loop, c, depth, bone, 0.12f);
            if (top is var (pinAt, across, outward))
            {
                var mp = new List<Vector3>(); var mn = new List<Vector3>(); var mi = new List<int>();
                BoxRaw(mp, mn, mi, pinAt - across * 0.016f + outward * 0.003f, pinAt + across * 0.016f + outward * 0.003f, dir * 0.0035f, outward * 0.0025f, sf.front);
                metalOut.Add(new Part(mp.ToArray(), mn.ToArray(), mi.ToArray(), bone, sf.k));
            }
        }
        else
        {
            // Knotted: a knot past the end, a short tail hanging on from it.
            var kc = c + dir * (depth + r * 0.45f);
            Blob(sf, backV, bone, kc, dir, side, r * 0.8f, r * 0.55f);
            var t0 = kc + dir * (r * 0.4f);
            var t1 = t0 + dir * 0.10f + pl.Back * 0.015f;
            Box(sf, backV, bone, t0, t1, side * (r * 1.0f), pl.Back * 0.012f, taper: 0.6f);
        }
    }

    /// <summary>The pinned sleeve's fold: the back half of the cut loop carried from the rounded end back up the
    /// arm, a thin shell lying on the sleeve (outer and inner layers, closed at its edges), narrowing as it goes.
    /// Returns where its pin goes (on the top's middle), the way across it and its outward normal.</summary>
    static (Vector3, Vector3, Vector3)? Flap(Surf sf, Plane pl, List<int> loop, Vector3 c, float depth, int bone, float len)
    {
        var dir = pl.Dir;
        var side = pl.Back.Cross(dir).Normalized();
        // The back arc: loop vertices within 60 degrees of the back, ordered across.
        var arc = loop.Select((v) => (v, o: sf.pos[v] - c))
            .Select((x) => (x.v, x.o, ang: Mathf.Atan2(x.o.Dot(side), x.o.Dot(pl.Back))))
            .Where((x) => Mathf.Abs(x.ang) < Mathf.DegToRad(60)).OrderBy((x) => x.ang).ToList();
        if (arc.Count < 3) return null;
        const int rows = 5;
        float start = depth * 0.8f;
        var outer = new List<List<int>>(); var inner = new List<List<int>>();
        for (int k = 0; k <= rows; k++)
        {
            float along = start - (start + len) * k / rows;
            // Round the end, the sleeve's own radius above it; narrower towards the top.
            float shrink = along > 0 ? Mathf.Sqrt(Mathf.Max(0.05f, 1 - (along / depth) * (along / depth))) : 1;
            float narrow = 1 - 0.35f * k / rows;
            var ro = new List<int>(); var ri = new List<int>();
            foreach (var (v, o, ang) in arc)
            {
                float a = ang * narrow;
                var u = (pl.Back * Mathf.Cos(a) + side * Mathf.Sin(a)).Normalized();
                float rad = o.Length() * shrink;
                ro.Add(sf.Copy(v, c + dir * along + u * (rad + 0.008f), u, bone));
                ri.Add(sf.Copy(v, c + dir * along + u * (rad + 0.003f), -u, bone));
            }
            outer.Add(ro); inner.Add(ri);
        }
        int n = arc.Count;
        for (int k = 0; k < rows; k++)
            for (int i = 0; i + 1 < n; i++)
            {
                var u = sf.pos[outer[k][i]] - (c + dir * dir.Dot(sf.pos[outer[k][i]] - c));
                sf.Tri(outer[k][i], outer[k][i + 1], outer[k + 1][i + 1], u);
                sf.Tri(outer[k][i], outer[k + 1][i + 1], outer[k + 1][i], u);
                sf.Tri(inner[k][i], inner[k][i + 1], inner[k + 1][i + 1], -u);
                sf.Tri(inner[k][i], inner[k + 1][i + 1], inner[k + 1][i], -u);
            }
        // The edges: down each side, and across the top and the fold at the end.
        for (int k = 0; k < rows; k++)
        {
            foreach (var (i, sgn) in new[] { (0, -1f), (n - 1, 1f) })
            {
                var o = side * sgn;
                sf.Tri(outer[k][i], inner[k][i], inner[k + 1][i], o);
                sf.Tri(outer[k][i], inner[k + 1][i], outer[k + 1][i], o);
            }
        }
        foreach (var (k, o) in new[] { (0, dir), (rows, -dir) })
            for (int i = 0; i + 1 < n; i++)
            {
                sf.Tri(outer[k][i], outer[k][i + 1], inner[k][i + 1], o);
                sf.Tri(outer[k][i], inner[k][i + 1], inner[k][i], o);
            }
        int mid = n / 2;
        int pinRow = rows - 1;
        var at = sf.pos[outer[pinRow][mid]];
        var outward = (sf.pos[outer[pinRow][mid]] - sf.pos[inner[pinRow][mid]]).Normalized();
        var across = (sf.pos[outer[pinRow][Math.Min(n - 1, mid + 1)]] - sf.pos[outer[pinRow][Math.Max(0, mid - 1)]]).Normalized();
        return (at, across, outward);
    }

    /// <summary>A flat strip from a to b (width along w, thickness along t), narrowing to taper at b, on the
    /// surface's own material (the UV of vertex v).</summary>
    static void Box(Surf sf, int v, int bone, Vector3 a, Vector3 b, Vector3 w, Vector3 t, float taper)
    {
        var p = new List<Vector3>(); var n = new List<Vector3>(); var i = new List<int>();
        BoxRaw(p, n, i, a, b, w, t, sf.front, taper);
        int base0 = sf.Count;
        for (int q = 0; q < p.Count; q++) sf.Copy(v, p[q], n[q], bone);
        foreach (int q in i) sf.idx.Add(base0 + q);
    }

    /// <summary>A box from a to b, half-width w and half-thickness t (b's end scaled by taper), as separate
    /// faces (flat normals), wound to face out.</summary>
    static void BoxRaw(List<Vector3> p, List<Vector3> n, List<int> idx, Vector3 a, Vector3 b, Vector3 w, Vector3 t, float front, float taper = 1)
    {
        Vector3[] c =
        {
            a - w - t, a + w - t, a + w + t, a - w + t,
            b - w * taper - t, b + w * taper - t, b + w * taper + t, b - w * taper + t,
        };
        int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 } };
        var mid = (a + b) / 2;
        foreach (var f in faces)
        {
            var fc = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) / 4;
            var fn = (c[f[1]] - c[f[0]]).Cross(c[f[2]] - c[f[0]]).Normalized();
            if (fn.Dot(fc - mid) < 0) fn = -fn;
            int o = p.Count;
            foreach (int q in f) { p.Add(c[q]); n.Add(fn); }
            Wound(p, idx, o, o + 1, o + 2, fn, front);
            Wound(p, idx, o, o + 2, o + 3, fn, front);
        }
    }

    /// <summary>A triangle into idx, wound to face along outward by the mesh's convention (front).</summary>
    static void Wound(List<Vector3> p, List<int> idx, int a, int b, int c, Vector3 outward, float front)
    {
        var g = (p[b] - p[a]).Cross(p[c] - p[a]);
        if (g.Dot(outward) * front < 0) (b, c) = (c, b);
        idx.Add(a); idx.Add(b); idx.Add(c);
    }

    /// <summary>A lumpy ellipsoid (the knot): radius across, half-length along dir.</summary>
    static void Blob(Surf sf, int v, int bone, Vector3 c, Vector3 dir, Vector3 side, float across, float along)
    {
        const int segs = 8, rings = 5;
        var up = dir.Cross(side).Normalized();
        int base0 = sf.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = r / (float)rings * Mathf.Pi;
            for (int s = 0; s < segs; s++)
            {
                float th = s / (float)segs * Mathf.Tau;
                float lump = 1 + 0.18f * (Hash01(r * 31 + s, 7) - 0.5f) * (r > 0 && r < rings ? 1 : 0);
                var radial = side * Mathf.Cos(th) + up * Mathf.Sin(th);
                var p = c + radial * (across * Mathf.Sin(phi) * lump) - dir * (along * Mathf.Cos(phi));
                sf.Copy(v, p, (p - c).Normalized(), bone);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = base0 + r * segs + s, b = base0 + r * segs + (s + 1) % segs;
                int cA = a + segs, cB = b + segs;
                var outward = (sf.pos[a] + sf.pos[cB]) / 2 - c;
                if (r > 0) sf.Tri(a, b, cA, outward);  // the first ring is one point: skip its zero-area halves
                if (r < rings - 1) sf.Tri(b, cB, cA, outward);
            }
    }

    static void AddExtra(ArrayMesh mesh, List<Part> parts, Material mat, bool delta)
    {
        if (parts.Count == 0) return;
        var p = new List<Vector3>(); var n = new List<Vector3>(); var idx = new List<int>(); var bones = new List<int>(); var w = new List<float>();
        int k = parts[0].k;
        foreach (var part in parts)
        {
            int o = p.Count;
            p.AddRange(part.p);
            n.AddRange(part.n);
            foreach (int q in part.i) idx.Add(o + q);
            for (int v = 0; v < part.p.Length; v++)
                for (int c = 0; c < k; c++) { bones.Add(c == 0 ? part.bone : 0); w.Add(c == 0 ? 1 : 0); }
        }
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        a[(int)Mesh.ArrayType.Vertex] = p.ToArray();
        a[(int)Mesh.ArrayType.Normal] = n.ToArray();
        a[(int)Mesh.ArrayType.Bones] = bones.ToArray();
        a[(int)Mesh.ArrayType.Weights] = w.ToArray();
        a[(int)Mesh.ArrayType.Index] = idx.ToArray();
        // A surface's blend shapes must match the mesh's count: none move here.
        var bs = new Godot.Collections.Array<Godot.Collections.Array>();
        for (int s = 0; s < mesh.GetBlendShapeCount(); s++)
        {
            var b = new Godot.Collections.Array();
            b.Resize((int)Mesh.ArrayType.Max);
            b[(int)Mesh.ArrayType.Vertex] = delta ? new Vector3[p.Count] : p.ToArray();
            b[(int)Mesh.ArrayType.Normal] = delta ? new Vector3[p.Count] : n.ToArray();
            bs.Add(b);
        }
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a, bs, null, k == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
    }

    // ------------------------------------------------------------------ helpers

    static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
            if (FindSkeleton(c) is Skeleton3D f) return f;
        return null;
    }

    static IEnumerable<MeshInstance3D> MeshesUnder(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var f in MeshesUnder(c)) yield return f;
    }
}
