# Make the stored base skin, art/models/base_skin.blend (docs/characters.md "The look"): the adult body
# and head as a kept model, the source the look builds load (build_lookdev.py) instead of generating a
# head each time. Run once to start it; after that the .blend is the source: it's edited by hand in
# Blender, or by Claude through the Blender MCP server or a small edit script, and saved.
#
#   blender --background --factory-startup --python art/scripts/make_skin.py -- [--out art/models/base_skin.blend]
#
# What it adds over the generated sculpt (sculpt.py): real openings. A generated face pushes vertices
# about on an even 3 mm mesh, so an 8 mm eye opening is three vertices tall and aliased. Here each eye
# opening and the mouth are cut as holes whose edges are snapped onto clean curves (the almond of the
# eye, the lips' meeting line), then each edge is rolled inward (a lid's thickness tucked against the
# eyeball; the inner lips running back towards the teeth). The body away from the head is thinned
# (weighted decimation) so the file stays small (no Git LFS: a few MB at most).
#
# The skin is object "skin": closed except for the eyes and the mouth, no rig, no eyeballs or teeth
# (the build adds those), in the rig's rest pose (rig.py), Blender space (Z up, facing -Y).

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sculpt as S  # noqa: E402


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return {a[i].lstrip("-"): a[i + 1] for i in range(0, len(a) - 1, 2)}


# ---------------------------------------------------------------- curves the openings snap to

def eye_curve(sx, eye, t):
    """A point on the eye's opening (Blender xz), t in 0..2pi from the outer corner, upper lid first."""
    half_w = 0.0140
    lat = half_w * math.cos(t)
    up = math.sin(t) > 0
    half_h = 0.0050 if up else 0.0042
    # The upper lid's highest point sits a little inward of the middle, the lower lid's lowest a little out.
    skew = -0.12 if up else 0.10
    dz = half_h * math.sin(t) * (1 + skew * math.cos(t)) + 0.0012 * lat / half_w
    return Vector((eye.x + sx * lat, 0, eye.z + dz))


def lip_line(x, upper):
    """The lips' meeting line: level, a hair apart so the mouth reads closed; the corners pinch."""
    half = S.MOUTH_HALF
    u = min(1.0, abs(x) / half)
    gap = 0.00012 * (1 - u ** 4)
    return S.MOUTH.z + (gap if upper else -gap)


# ---------------------------------------------------------------- cutting

def boundary_loops(bm):
    """Every loop of boundary edges, as ordered vertex lists."""
    edges = [e for e in bm.edges if e.is_boundary]
    seen, loops = set(), []
    for e0 in edges:
        if e0 in seen:
            continue
        loop, v, e = [e0.verts[0]], e0.verts[1], e0
        seen.add(e0)
        while v is not loop[0]:
            loop.append(v)
            nxt = [x for x in v.link_edges if x.is_boundary and x not in seen]
            if not nxt:
                break
            e = nxt[0]
            seen.add(e)
            v = e.other_vert(v)
        loops.append(loop)
    return loops


def relax(bm, verts, fixed, rounds=6, factor=0.5):
    """Laplacian relax of verts towards their neighbours' average, the fixed ones held."""
    for _ in range(rounds):
        new = {}
        for v in verts:
            if v in fixed:
                continue
            nb = [e.other_vert(v).co for e in v.link_edges]
            if nb:
                avg = sum(nb, Vector()) / len(nb)
                new[v] = v.co.lerp(avg, factor)
        for v, c in new.items():
            v.co = c


def ring_near(bm, centre, rx, rz):
    return [v for v in bm.verts if ((v.co.x - centre.x) / rx) ** 2 + ((v.co.z - centre.z) / rz) ** 2 < 1 and v.co.y < centre.y + 0.01]


def cut_eye(bm, sx, eye):
    # Delete what's inside the almond (the lids' generated push leaves it behind the eyeball anyway).
    inside = [f for f in bm.faces if S.eye_opening(f.calc_center_median(), sx, eye) < 1.0
              and abs(f.calc_center_median().x - eye.x) < 0.02 and f.calc_center_median().y < eye.y + 0.006]
    bmesh.ops.delete(bm, geom=inside, context="FACES")
    loop = max((l for l in boundary_loops(bm) if all(abs(v.co.x - eye.x) < 0.025 and abs(v.co.z - eye.z) < 0.015 for v in l)), key=len)
    # Snap the edge onto the curve: each boundary vertex to the curve point at its own angle round the eye.
    rl = S.EYE_RADIUS + 0.0009
    for v in loop:
        lat = (v.co.x - eye.x) * sx
        t = math.atan2((v.co.z - eye.z) / 0.004, lat / 0.014)
        p = eye_curve(sx, eye, t)
        r2 = (p.x - eye.x) ** 2 + (p.z - eye.z) ** 2
        v.co = Vector((p.x, eye.y - math.sqrt(max(rl * rl - r2, 1e-8)), p.z))
    near = ring_near(bm, eye, 0.024, 0.016)
    relax(bm, near, set(loop), rounds=8)
    # Roll the lid's edge in: a second loop 1.5 mm deeper, towards the eyeball's centre.
    ret = bmesh.ops.extrude_edge_only(bm, edges=[e for e in bm.edges if e.is_boundary and all(v in loop for v in e.verts)])
    for v in [x for x in ret["geom"] if isinstance(x, bmesh.types.BMVert)]:
        v.co = v.co + (eye - v.co).normalized() * 0.0016
    return len(loop)


def cut_mouth(bm):
    m = S.MOUTH
    band = [f for f in bm.faces
            if (c := f.calc_center_median()) is not None and abs(c.z - m.z) < 0.0026
            and abs(c.x) < S.MOUTH_HALF * 0.96 and m.y - 0.012 < c.y < m.y + 0.02]
    bmesh.ops.delete(bm, geom=band, context="FACES")
    loop = max((l for l in boundary_loops(bm) if all(abs(v.co.x) < 0.035 and abs(v.co.z - m.z) < 0.012 for v in l)), key=len)
    # Snap: the upper half of the edge onto the upper lip's line, the lower onto the lower's.
    for v in loop:
        x = max(-S.MOUTH_HALF, min(S.MOUTH_HALF, v.co.x * 1.04))
        v.co = Vector((x, v.co.y, lip_line(x, v.co.z > m.z)))
    near = [v for v in bm.verts if abs(v.co.x) < 0.040 and abs(v.co.z - m.z) < 0.016 and v.co.y < m.y + 0.01]
    relax(bm, near, set(loop), rounds=8)
    # The inner lips: the edge rolled back 6 mm towards the teeth.
    ret = bmesh.ops.extrude_edge_only(bm, edges=[e for e in bm.edges if e.is_boundary and all(v in loop for v in e.verts)])
    for v in [x for x in ret["geom"] if isinstance(x, bmesh.types.BMVert)]:
        v.co = v.co + Vector((0, 0.006, -0.0008 if v.co.z < m.z else 0.0008))
    return len(loop)


# ---------------------------------------------------------------- main

def main():
    a = args()
    out = os.path.abspath(a.get("out", os.path.join(os.path.dirname(__file__), "..", "models", "base_skin.blend")))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    o = S.body_balls().build("skin", 0.008)
    S.remesh(o, 0.003)
    S.smooth(o, 0.5, 6)
    S.smooth_head(o)
    S.apply_offsets(o, S.face_offset)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    counts = [cut_eye(bm, 1, S.EYE_L), cut_eye(bm, -1, S.EYE_R), cut_mouth(bm)]
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(o.data)
    bm.free()
    for p in o.data.polygons:
        p.use_smooth = True
    print("openings (edge vertices: left eye, right eye, mouth):", counts)
    # Thin the body, keep the head: weighted collapse.
    g = o.vertex_groups.new(name="keep")
    for v in o.data.vertices:
        g.add([v.index], S.smooth01((v.co.z - 1.42) / 0.06), "REPLACE")
    before = len(o.data.polygons)
    m = o.modifiers.new("Thin", "DECIMATE")
    m.decimate_type, m.ratio, m.vertex_group, m.vertex_group_factor = "COLLAPSE", 0.30, g.name, 10.0
    m.use_collapse_triangulate = True
    m.invert_vertex_group = True  # the weight marks what collapses: keep the head (weight 1) by inverting
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
    o.vertex_groups.remove(o.vertex_groups["keep"])
    o.data.validate()
    head = sum(1 for p in o.data.polygons if (o.data.vertices[p.vertices[0]].co.z > 1.50))
    print(f"triangles {before} -> {len(o.data.polygons)} (head {head})")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=out, compress=True)
    print(f"saved {out} ({os.path.getsize(out) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
