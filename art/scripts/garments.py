# Garments for the shared rig (docs/characters.md "Clothing, hair, equipment"), built by script as
# shells a little outside the body, skinned to the same bones as the body under them (what a weight
# transfer from the body would give: here it's exact, as the shells follow the body's pieces).
#
# Each garment is split into the body's regions (torso, head, upperarm_*, forearm_*, hand_*,
# thigh_*, shin_*, foot_*, plus "back" for gear), one mesh per region, named <garment>__<region>.
# A piece is "full" when it covers its region entirely (the body there is hidden: no clipping, fewer
# triangles drawn) or partial (a short sleeve, a boot's shaft: the body stays). A piece also hides
# the lower layers' pieces in its region when it's full or "hides_lower" (a jacket over a shirt).
# Missing limbs hide garment pieces in the cut regions the same way they hide the body.
#
# The rules and the list go to garments.json for the game; the meshes to garments.glb (the rig
# and the garments, no clips: they play on the character's skeleton).
#
#   slot    layer  garment
#   shirt   1      shirt (short sleeves)
#   jacket  2      jacket (long sleeves, over a shirt)
#   legs    1      trousers, shorts
#   feet    2      boots (over trousers), shoes
#   hands   2      gloves
#   head    1      cap
#   back    3      backpack

import json
import os

import bmesh
import bpy
from mathutils import Vector

from shapes import box, cone, material, sphere

SIDES = ("Left", "Right")

GARMENTS = {
    "shirt":    {"slot": "shirt", "layer": 1, "color": (0.55, 0.6, 0.65)},
    "jacket":   {"slot": "jacket", "layer": 2, "color": (0.27, 0.31, 0.22)},
    "trousers": {"slot": "legs", "layer": 1, "color": (0.2, 0.25, 0.36)},
    "shorts":   {"slot": "legs", "layer": 1, "color": (0.42, 0.37, 0.27)},
    "boots":    {"slot": "feet", "layer": 2, "color": (0.26, 0.18, 0.12)},
    "shoes":    {"slot": "feet", "layer": 2, "color": (0.16, 0.16, 0.17)},
    "gloves":   {"slot": "hands", "layer": 2, "color": (0.2, 0.2, 0.2)},
    "cap":      {"slot": "head", "layer": 1, "color": (0.5, 0.13, 0.1)},
    "backpack": {"slot": "back", "layer": 3, "color": (0.3, 0.32, 0.2)},
}


def _along(a, b, t):
    return a + (b - a) * t


def build_garments(arm_obj):
    """Build every garment's region pieces; returns (objects, metadata for garments.json)."""
    bones = arm_obj.data.bones
    H = {b.name: arm_obj.matrix_world @ b.head_local for b in bones}
    T = {b.name: arm_obj.matrix_world @ b.tail_local for b in bones}
    pieces = {}  # (garment, region) -> [bmesh, [(verts, weights)], full, hides_lower]

    def piece(g, region, make, weights, full=False, hides_lower=None):
        key = (g, region)
        if key not in pieces:
            pieces[key] = [bmesh.new(), [], full, full if hides_lower is None else hides_lower]
        bm, parts = pieces[key][0], pieces[key][1]
        parts.append((make(bm), weights))

    fl = 0.64  # torso depth squash, as the body's (0.62) and a touch more
    hips_low = H["Hips"] - Vector((0, 0, 0.08))
    head_c = H["Head"] + Vector((0, 0, 0.1))

    # Shirt: the torso a centimetre out, short sleeves.
    piece("shirt", "torso", lambda bm: cone(bm, hips_low - Vector((0, 0, 0.02)), H["Spine"], 0.172, 0.162, flat=fl), {"Hips": 1})
    piece("shirt", "torso", lambda bm: cone(bm, H["Spine"], H["Chest"], 0.162, 0.162, flat=fl), {"Spine": 1})
    piece("shirt", "torso", lambda bm: cone(bm, H["Chest"], H["UpperChest"], 0.162, 0.182, flat=fl), {"Chest": 1})
    piece("shirt", "torso", lambda bm: cone(bm, H["UpperChest"], H["Neck"], 0.192, 0.128, flat=fl), {"UpperChest": 1})
    # Jacket: further out, long sleeves, a collar; over the shirt's torso (hides it).
    piece("jacket", "torso", lambda bm: cone(bm, hips_low - Vector((0, 0, 0.05)), H["Spine"], 0.19, 0.18, flat=0.68), {"Hips": 1}, hides_lower=True)
    piece("jacket", "torso", lambda bm: cone(bm, H["Spine"], H["Chest"], 0.18, 0.18, flat=0.68), {"Spine": 1}, hides_lower=True)
    piece("jacket", "torso", lambda bm: cone(bm, H["Chest"], H["UpperChest"], 0.18, 0.2, flat=0.68), {"Chest": 1}, hides_lower=True)
    piece("jacket", "torso", lambda bm: cone(bm, H["UpperChest"], H["Neck"] + Vector((0, 0, 0.03)), 0.21, 0.075, flat=0.7), {"UpperChest": 1}, hides_lower=True)
    # Trousers and shorts: a band round the hips, the legs.
    for g in ("trousers", "shorts"):
        piece(g, "torso", lambda bm: cone(bm, hips_low - Vector((0, 0, 0.03)), H["Hips"] + Vector((0, 0, 0.03)), 0.176, 0.17, flat=fl), {"Hips": 1})
    # Cap: a dome over the crown and a peak.
    def dome(bm):
        verts = sphere(bm, head_c + Vector((0, 0, 0.012)), 0.114, segs=12, rings=8)
        low = [v for v in verts if v.co.z < head_c.z + 0.03]
        bmesh.ops.delete(bm, geom=low, context="VERTS")
        return [v for v in verts if v.is_valid]
    piece("cap", "head", dome, {"Head": 1})
    piece("cap", "head", lambda bm: cone(bm, head_c + Vector((0, -0.06, 0.04)), head_c + Vector((0, -0.16, 0.035)), 0.075, 0.06, segments=10, flat=0.12, flat_axis="z"), {"Head": 1})
    # Backpack: a box on the back with a flap, straps over the shoulders.
    piece("backpack", "back", lambda bm: box(bm, H["Chest"] + Vector((0, 0.19, 0.06)), (0.3, 0.15, 0.4)), {"Chest": 1})
    piece("backpack", "back", lambda bm: box(bm, H["Chest"] + Vector((0, 0.2, 0.27)), (0.31, 0.16, 0.05)), {"Chest": 1})

    for s in SIDES:
        ua, la, hd = s + "UpperArm", s + "LowerArm", s + "Hand"
        ul, ll, ft, to = s + "UpperLeg", s + "LowerLeg", s + "Foot", s + "Toes"
        sx = 1 if s == "Left" else -1
        # Shirt: shoulder caps and short sleeves.
        piece("shirt", "torso", lambda bm, ua=ua: sphere(bm, H[ua], 0.07), {s + "Shoulder": 0.5, ua: 0.5})
        piece("shirt", "upperarm_" + s, lambda bm, ua=ua, la=la: cone(bm, H[ua], _along(H[ua], H[la], 0.55), 0.068, 0.062), {ua: 1})
        # Jacket: long sleeves (full), cuffs at the wrist.
        piece("jacket", "torso", lambda bm, ua=ua: sphere(bm, H[ua], 0.08), {s + "Shoulder": 0.5, ua: 0.5}, hides_lower=True)
        piece("jacket", "upperarm_" + s, lambda bm, ua=ua, la=la: cone(bm, H[ua], H[la], 0.076, 0.064), {ua: 1}, full=True)
        piece("jacket", "upperarm_" + s, lambda bm, la=la: sphere(bm, H[la], 0.064, segs=8), {ua: 0.5, la: 0.5}, full=True)
        piece("jacket", "forearm_" + s, lambda bm, la=la, hd=hd: cone(bm, H[la], H[hd] + (H[hd] - H[la]).normalized() * 0.01, 0.062, 0.052), {la: 1}, full=True)
        # Gloves: the hand and a cuff.
        hand_end = H[hd] + (T[hd] - H[hd]).normalized() * 0.18
        piece("gloves", "hand_" + s, lambda bm, hd=hd, e=hand_end: cone(bm, H[hd], e, 0.05, 0.035, segments=8, flat=0.55, flat_axis="z"), {hd: 1}, full=True)
        piece("gloves", "forearm_" + s, lambda bm, la=la, hd=hd: cone(bm, _along(H[la], H[hd], 0.82), H[hd], 0.05, 0.05, segments=8), {la: 1})
        # Trousers: hips, thighs and knees, shins to the ankle (full); shorts: half the thigh.
        piece("trousers", "torso", lambda bm, ul=ul: sphere(bm, H[ul], 0.095), {"Hips": 0.5, ul: 0.5})
        piece("trousers", "thigh_" + s, lambda bm, ul=ul, ll=ll: cone(bm, H[ul], H[ll], 0.092, 0.07), {ul: 1}, full=True)
        piece("trousers", "thigh_" + s, lambda bm, ll=ll: sphere(bm, H[ll], 0.071, segs=8), {ul: 0.5, ll: 0.5}, full=True)
        piece("trousers", "shin_" + s, lambda bm, ll=ll, ft=ft: cone(bm, H[ll], H[ft] + Vector((0, 0, -0.01)), 0.068, 0.058), {ll: 1}, full=True)
        piece("shorts", "torso", lambda bm, ul=ul: sphere(bm, H[ul], 0.097), {"Hips": 0.5, ul: 0.5})
        piece("shorts", "thigh_" + s, lambda bm, ul=ul, ll=ll: cone(bm, H[ul], _along(H[ul], H[ll], 0.5), 0.097, 0.088), {ul: 1})
        # Boots: the foot (full) and a shaft up the shin over the trousers; shoes: the foot.
        piece("boots", "foot_" + s, lambda bm, ft=ft, to=to: cone(bm, H[ft], H[to], 0.056, 0.05, segments=8), {ft: 1}, full=True)
        piece("boots", "foot_" + s, lambda bm, to=to: cone(bm, H[to], H[to] + Vector((0, -0.09, -0.005)), 0.05, 0.042, segments=8), {to: 1}, full=True)
        piece("boots", "shin_" + s, lambda bm, ll=ll, ft=ft: cone(bm, H[ft] + Vector((0, 0, -0.03)), _along(H[ft], H[ll], 0.35), 0.072, 0.076), {ll: 1})
        piece("shoes", "foot_" + s, lambda bm, ft=ft, to=to: cone(bm, H[ft] + Vector((0, 0, -0.01)), H[to], 0.053, 0.048, segments=8), {ft: 1}, full=True)
        piece("shoes", "foot_" + s, lambda bm, to=to: cone(bm, H[to], H[to] + Vector((0, -0.088, -0.005)), 0.047, 0.04, segments=8), {to: 1}, full=True)
        # Backpack straps over the shoulders.
        piece("backpack", "back", lambda bm, sx=sx: box(bm, Vector((sx * 0.1, 0.03, H["Neck"].z - 0.025)), (0.045, 0.3, 0.02)), {"UpperChest": 1})

    objs, meta = [], {}
    for (g, region), (bm, parts, full, hides_lower) in pieces.items():
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.verts.index_update()
        weights_of = {}
        for verts, weights in parts:
            for v in verts:
                if v.is_valid:
                    weights_of[v.index] = weights
        name = f"{g}__{region}"
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = True
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        for vi, weights in weights_of.items():
            for bone, w in weights.items():
                grp = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                grp.add([vi], w, "REPLACE")
        obj.parent = arm_obj
        obj.modifiers.new("Armature", "ARMATURE").object = arm_obj
        me.materials.append(material("Cloth_" + g, GARMENTS[g]["color"]))
        objs.append(obj)
        entry = meta.setdefault(g, {**{k: v for k, v in GARMENTS[g].items()}, "pieces": []})
        entry["pieces"].append({"mesh": name, "region": region, "full": full, "hides_lower": hides_lower})
    return objs, meta


def write_meta(meta, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"garments": meta}, f, indent=2)


def rest_uv(obj):
    """A UV map of rest-pose position (u: height up the body, v: round it), so a shader's noise
    (dirt, blood, holes) sticks to the cloth and skin as they move, and dirt can gather low."""
    me = obj.data
    uv = me.uv_layers.new(name="Rest")
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (co.z / 1.8, 0.5 + 0.7 * co.x + 0.5 * co.y)
