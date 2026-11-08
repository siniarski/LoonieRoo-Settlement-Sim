# Garments with volume for the look-development survivor (build_lookdev.py --clothes): each is its own
# mesh, a shell taken from the body where the garment covers it, smoothed to drape (cloth bridges the
# body's dips instead of following them), stood off the skin by the garment's own thickness profile
# (loose at a jacket's hem, tight at a hoodie's ribbed cuffs, wider down a loose jeans leg), plus pieces a
# shell can't make: a stand collar, a hood lying down on the back, a pouch pocket, drawstrings. The
# hems get a rim (Solidify, rim only) and the material is two-sided, so an edge shows its thickness and
# the inside of a cuff isn't a hole. The body under a garment is deleted (it never shows, and it can't
# poke through). Weights come from the body by nearest-surface transfer.
#
# Masks, thickness and colour are numpy functions of the rest (T-pose) position, like paint.py.
# Blender space: Z up, the character faces -Y, its left is +X.

import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import paint as P
from paint import fbm, mix, rgb, sstep


def _parts(p):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    arm = sstep(0.19, 0.21, ax) * sstep(1.33, 1.36, z)
    neck = sstep(0.072, 0.060, ax) * sstep(1.415, 1.435, z)  # the neck, left to the collar
    return x, y, z, ax, arm, neck


def _torso(z, ax, arm, neck, hem):
    return sstep(hem - 0.01, hem + 0.01, z) * sstep(1.56, 1.54, z) * (1 - arm) * (1 - neck) * (ax < 0.26)


# ---------------------------------------------------------------- the garments

def jacket_mask(p):
    x, y, z, ax, arm, neck = _parts(p)
    return np.clip(_torso(z, ax, arm, neck, 0.80) + arm * sstep(0.745, 0.735, ax), 0, 1)


def jacket_lift(p):
    x, y, z, ax, arm, neck = _parts(p)
    torso = 0.011 + 0.014 * sstep(0.95, 0.80, z)                          # the hem stands off the hips
    sleeve = 0.009 + 0.005 * sstep(0.55, 0.73, ax)                        # a loose cuff
    lift = torso * (1 - arm) + sleeve * arm
    front = y < -0.02
    pocket = np.zeros(len(p))
    for sx in (1, -1):
        pocket += sstep(0.004, 0.0, np.maximum(np.abs(x - sx * 0.085) - 0.035, np.abs(z - 1.27) - 0.04)) * front
        pocket += sstep(0.004, 0.0, np.maximum(np.abs(x - sx * 0.10) - 0.05, np.abs(z - 0.92) - 0.06)) * front
    zip_groove = sstep(0.006, 0.002, ax) * front * (1 - arm)
    return lift + 0.003 * pocket * (1 - arm) - 0.0015 * zip_groove


def jacket_cover(p):
    x, y, z, ax, arm, neck = _parts(p)
    return np.clip(sstep(0.83, 0.85, z) * sstep(1.40, 1.38, z) * (1 - arm) * (ax < 0.24) + arm * sstep(0.71, 0.70, ax), 0, 1)


def hoodie_mask(p):
    x, y, z, ax, arm, neck = _parts(p)
    return np.clip(_torso(z, ax, arm, neck, 0.86) + arm * sstep(0.750, 0.740, ax), 0, 1)


def hoodie_lift(p):
    x, y, z, ax, arm, neck = _parts(p)
    torso = 0.016 - 0.006 * sstep(0.92, 0.89, z) + 0.004 * sstep(0.89, 0.93, z) * sstep(1.0, 0.94, z)  # rib, bunched above
    sleeve = 0.013 - 0.007 * sstep(0.70, 0.715, ax) + 0.004 * sstep(0.62, 0.69, ax) * sstep(0.715, 0.70, ax)
    return torso * (1 - arm) + sleeve * arm


def hoodie_cover(p):
    x, y, z, ax, arm, neck = _parts(p)
    return np.clip(sstep(0.89, 0.91, z) * sstep(1.40, 1.38, z) * (1 - arm) * (ax < 0.24) + arm * sstep(0.72, 0.71, ax), 0, 1)


def jeans_mask(p):
    x, y, z, ax, arm, neck = _parts(p)
    return sstep(0.04, 0.05, z) * sstep(1.0, 0.99, z) * (ax < 0.22)


def jeans_lift(p):
    x, y, z, ax, arm, neck = _parts(p)
    lift = 0.006 + 0.013 * sstep(0.75, 0.10, z)                           # loose, straight down the leg
    lift += 0.004 * sstep(0.09, 0.08, z)                                   # a turn-up at the hem
    lift += 0.003 * sstep(0.955, 0.965, z) * sstep(0.995, 0.985, z)        # the belt
    return lift


def jeans_cover(p):
    x, y, z, ax, arm, neck = _parts(p)
    return sstep(0.09, 0.11, z) * sstep(0.975, 0.965, z) * (ax < 0.21)


# ---------------------------------------------------------------- extra pieces (bmesh, Blender space)

def _ring_band(bm, rings, segs=28, gap_deg=0.0):
    """A band through rings of (z, rx, ry, cx, cy), open at the front by gap_deg either side."""
    loops = []
    for (z, rx, ry, cx, cy) in rings:
        loop = []
        for i in range(segs + 1):
            a = math.radians(gap_deg) + (2 * math.pi - 2 * math.radians(gap_deg)) * i / segs  # from the front round
            loop.append(bm.verts.new((cx + rx * math.sin(a), cy - ry * math.cos(a), z)))
        loops.append(loop)
    for la, lb in zip(loops, loops[1:]):
        for i in range(segs if gap_deg > 0 else segs):
            bm.faces.new((la[i], la[i + 1], lb[i + 1], lb[i]))


def _tube(bm, pts, r, segs=6):
    """A thin tube along a polyline (a drawstring)."""
    rings = []
    for i, c in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = d.cross(Vector((1, 0, 0))).normalized()
        up = d.cross(side)
        rings.append([bm.verts.new(c + (side * math.cos(2 * math.pi * k / segs) + up * math.sin(2 * math.pi * k / segs)) * r) for k in range(segs)])
    for ra, rb in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((ra[k], ra[(k + 1) % segs], rb[(k + 1) % segs], rb[k]))


def _ellipsoid(bm, c, half, segs=16, rings=10):
    ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    bmesh.ops.scale(bm, vec=Vector(half), verts=ret["verts"])
    bmesh.ops.translate(bm, vec=Vector(c), verts=ret["verts"])


def jacket_extras(bm):
    # A stand collar, open at the zip.
    _ring_band(bm, [(1.405, 0.090, 0.082, 0, 0.012), (1.450, 0.080, 0.074, 0, 0.014), (1.495, 0.074, 0.070, 0, 0.018)], gap_deg=14)


def hoodie_extras(bm):
    # The hood, down: a thick roll round the back and sides of the neck, its cloth lying on the upper back.
    segs, tube = 26, 10
    rings = []
    for i in range(segs + 1):
        th = math.radians(-125 + 250 * i / segs)          # 0 = straight back
        cx, cy = 0.098 * math.sin(th), 0.022 + 0.092 * math.cos(th)
        out = Vector((math.sin(th), math.cos(th), 0))
        size = 1.0 - 0.45 * (abs(th) / math.radians(125)) ** 2  # thinner where it comes round to the front
        c = Vector((cx, cy, 1.478 - 0.012 * abs(th) / math.radians(125)))
        rings.append([bm.verts.new(c + out * 0.030 * size * math.cos(2 * math.pi * k / tube)
                                   + Vector((0, 0, 0.026 * size * math.sin(2 * math.pi * k / tube)))) for k in range(tube)])
    for ra, rb in zip(rings, rings[1:]):
        for k in range(tube):
            bm.faces.new((ra[k], ra[(k + 1) % tube], rb[(k + 1) % tube], rb[k]))
    _ellipsoid(bm, (0, 0.112, 1.335), (0.115, 0.026, 0.095))
    # The pouch pocket.
    _ellipsoid(bm, (0, -0.108, 1.02), (0.095, 0.007, 0.048), segs=20, rings=8)
    # Drawstrings.
    for sx in (1, -1):
        _tube(bm, [Vector((sx * 0.024, -0.072, 1.420)), Vector((sx * 0.027, -0.118, 1.350)), Vector((sx * 0.030, -0.138, 1.270))], 0.0032)


def jeans_extras(bm):
    pass


GARMENTS = {
    "jacket": {"mask": jacket_mask, "lift": jacket_lift, "cover": jacket_cover, "extras": jacket_extras, "tris": 6000},
    "hoodie": {"mask": hoodie_mask, "lift": hoodie_lift, "cover": hoodie_cover, "extras": hoodie_extras, "tris": 7000},
    "jeans": {"mask": jeans_mask, "lift": jeans_lift, "cover": jeans_cover, "extras": jeans_extras, "tris": 4500},
}
OUTFITS = {"jacket": ["jacket", "jeans"], "hoodie": ["hoodie", "jeans"]}


# ---------------------------------------------------------------- building

def _np_verts(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def _np_normals(obj):
    n = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertex_normals.foreach_get("vector", n)
    return n.reshape(-1, 3)


def _keep_faces(obj, keep_vert):
    """Delete every face with a vertex not kept."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    gone = [f for f in bm.faces if not all(keep_vert[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()


def shell(skin_obj, name):
    """The garment's high-poly shell from the (closed, unlifted) skin: cut to the mask, draped, lifted,
    with its extra pieces."""
    g = GARMENTS[name]
    obj = skin_obj.copy()
    obj.data = skin_obj.data.copy()
    obj.name = obj.data.name = name + "_high"
    bpy.context.scene.collection.objects.link(obj)
    _keep_faces(obj, g["mask"](_np_verts(obj)) > 0.5)
    rest = _np_verts(obj)  # the lift is a function of where the cloth sits on the body, before draping
    m = obj.modifiers.new("Drape", "SMOOTH")
    m.factor, m.iterations = 0.9, 14
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)
    obj.data.update()
    co, n = _np_verts(obj), _np_normals(obj)
    obj.data.vertices.foreach_set("co", (co + n * g["lift"](rest)[:, None]).ravel())
    obj.data.update()
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    g["extras"](bm)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def strip_covered(body, names):
    """Delete the body where every garment's cover says it's hidden."""
    co = _np_verts(body)
    covered = np.zeros(len(co))
    for name in names:
        covered = np.maximum(covered, GARMENTS[name]["cover"](co))
    _keep_faces(body, covered < 0.5)


def rim(obj, thickness=0.005):
    """Thickness at the hems: Solidify's rim only (the material is two-sided)."""
    m = obj.modifiers.new("Rim", "SOLIDIFY")
    m.thickness, m.offset, m.use_rim, m.use_rim_only = thickness, -1.0, True, True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)


# ---------------------------------------------------------------- relief and colour (numpy)

def detail(name, p):
    """Fold relief for the high-poly shell (metres along the normal)."""
    x, y, z, ax, arm, neck = _parts(p)
    crumple = (fbm(p, 50, 3) - 0.5) * 0.0018
    if name == "jeans":
        stack = sstep(0.30, 0.08, z) * (np.sin(z * 180 + fbm(p, 12, 2) * 8) * 0.0035)       # stacking at the ankle
        knee = sstep(0.08, 0.0, np.abs(z - 0.53)) * (np.sin(z * 240 + fbm(p, 20, 2) * 6) * 0.002)
        seam = sstep(0.004, 0.0, np.abs(ax - 0.163)) * 0.0012
        return stack + knee + seam + (fbm(p * np.array([1, 1, 3]), 9, 3) - 0.5) * 0.004 + crumple
    big = 0.006 if name == "hoodie" else 0.004
    torso = (fbm(p * np.array([1.0, 1.0, 3.0]), 7, 3) - 0.5) * big
    sleeve = (fbm(p * np.array([3.0, 1.0, 1.0]), 8, 3) - 0.5) * big * (1 + 1.5 * sstep(0.08, 0.0, np.abs(ax - 0.48)))
    rib = 0.0
    if name == "hoodie":
        bands = sstep(0.92, 0.89, z) * (1 - arm) + sstep(0.70, 0.715, ax) * arm
        rib = bands * np.sin((x * 1.0 + y) * 900) * 0.0006
    return torso * (1 - arm) + sleeve * arm + rib + crumple


def albedo(name, p, ao):
    """Colour per texel (sRGB as displayed), shading from the baked AO."""
    x, y, z, ax, arm, neck = _parts(p)
    n1, n2, n3 = fbm(p, 14, 4), fbm(p + 5.1, 40, 3), fbm(p + 9.7, 140, 2)
    front = y < -0.02
    if name == "jacket":
        col = rgb(0.34, 0.31, 0.21) * (0.86 + 0.24 * n1[:, None]) * (0.94 + 0.12 * n3[:, None])
        col = mix(col, col * 0.78, sstep(0.84, 0.82, z) * (1 - arm) + sstep(0.70, 0.72, ax) * arm + (z > 1.40) * (ax < 0.1))  # hem, cuffs, collar
        zipper = sstep(0.006, 0.003, ax) * front * (1 - arm) * (z < 1.50)
        col = mix(col, rgb(0.30, 0.30, 0.29) * (0.8 + 0.4 * (np.sin(z * 1400) > 0))[:, None], zipper)
        seams = np.zeros(len(p))
        for sx in (1, -1):
            for cx, cz, hw, hh in ((0.085, 1.27, 0.035, 0.04), (0.10, 0.92, 0.05, 0.06)):
                d = np.maximum(np.abs(x - sx * cx) - hw, np.abs(z - cz) - hh)
                seams += sstep(0.0025, 0.0005, np.abs(d)) * front
        col = mix(col, col * 0.6, np.clip(seams, 0, 1) * (1 - arm))
        wear = sstep(0.62, 0.8, n2) * 0.3
        col = mix(col, col * 1.25 + 0.04, wear)
    elif name == "hoodie":
        marl = 0.82 + 0.36 * fbm(p * np.array([1, 1, 2]), 260, 2)
        col = rgb(0.24, 0.24, 0.25) * marl[:, None] * (0.92 + 0.16 * n1[:, None])
        strings = np.zeros(len(p))
        for sx in (1, -1):
            strings += sstep(0.008, 0.004, np.abs(x - sx * 0.027)) * (y < -0.07) * sstep(1.26, 1.27, z) * sstep(1.43, 1.42, z) * sstep(-0.11, -0.13, y + (z - 1.27) * 0.4)
        col = mix(col, rgb(0.74, 0.71, 0.64), np.clip(strings, 0, 1))
        hood = sstep(1.41, 1.43, z) * (y > -0.06)
        col = mix(col, col * 1.12, hood * 0.5)
    else:  # jeans
        col = rgb(0.20, 0.25, 0.35) * (0.82 + 0.34 * fbm(p * np.array([1, 1, 4]), 22, 3)[:, None]) * (0.92 + 0.16 * n3[:, None])
        col = mix(col, col * 1.55 + 0.05, sstep(0.06, 0.0, np.abs(z - 0.55)) * 0.55 * front)                    # faded knees
        col = mix(col, col * 1.4 + 0.04, sstep(0.07, 0.0, np.abs(z - 0.86)) * front * 0.4)                       # and thighs
        col = mix(col, rgb(0.45, 0.48, 0.55), sstep(0.09, 0.08, z) * 0.5)                                         # the turn-up's inside
        col = mix(col, rgb(0.62, 0.50, 0.30), sstep(0.0022, 0.0, np.abs(ax - 0.163)) * 0.7)                      # stitching
        belt = sstep(0.958, 0.962, z) * sstep(0.992, 0.988, z)
        col = mix(col, rgb(0.17, 0.11, 0.07) * (0.85 + 0.3 * n2[:, None]), belt)
        col = mix(col, rgb(0.50, 0.48, 0.42), belt * sstep(0.022, 0.018, ax) * front)                            # the buckle
    dirt = sstep(0.45, 0.75, fbm(p, 8, 4)) * sstep(0.9, 0.0, z) * 0.5
    col = mix(col, rgb(0.19, 0.15, 0.11), dirt)
    col = col * (0.60 + 0.40 * ao)[:, None]
    return np.clip(col, 0, 1)
