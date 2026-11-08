# The look-development characters (docs/characters.md "The look"): one sculpted adult (sculpt.py) as
# two candidate looks, each a ladder of LOD meshes on the shared rig, for the look lab
# (godot/Characters/LookLab.tscn) to compare at first-person distances and to profile:
#
#   plus  "Zomboid-plus": 30k / 12k / 6k / 3k / 1.2k / 600 triangles, a 2048 albedo and a normal map
#         baked from a high-poly version (cloth folds, hems, skin), face shape keys on the top two
#   pz    "Zomboid-like": 2.5k / 1.2k / 600 triangles, a 512 painted albedo with strong baked shading,
#         no normal map, face shape keys on the top one
#
# The zombie's face has two shape keys, "jaw_open" and "angry" (neutral is the basis with a little
# jaw_open); expressions are near-tier only (the lower LODs carry none). Face variants (--face, sculpt.py
# FACE_VARIANTS) change the head's shape and paint. Clothes with volume (--clothes jacket|hoodie,
# clothes.py) add garment meshes over the body and delete the body under them; every piece shares one
# texture atlas, and the ladder's triangle counts are for the whole character.
#
#   blender --background --factory-startup --python art/scripts/build_lookdev.py -- \
#       --subject zombie|survivor --out godot/assets/characters/look [--only plus|pz] [--tex 2048]
#       [--face feral|slack|gaunt|bloated|torn (feral unless said)] [--clothes jacket|hoodie]
#       [--blend art/blend/look_zombie.blend] [--preview DIR]
#
# Writes <out>/look_<cand>_<subject>[_<face or clothes>].glb, its meshes named <piece>_lod<i> (body,
# jacket, jeans...), and prints each LOD's triangles and the bake times.

import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import clothes  # noqa: E402
import paint  # noqa: E402
import rig  # noqa: E402
import sculpt  # noqa: E402
import validate  # noqa: E402

LADDER = {"plus": [30000, 12000, 6000, 3000, 1200, 600], "pz": [2500, 1200, 600]}
FACE_LODS = {"plus": 2, "pz": 1}
TEX = {"plus": 2048, "pz": 512}
EYE_SEGS = {"plus": (16, 10), "pz": (8, 6)}
EYES_TRIS = {"plus": 900, "pz": 300}
# Bones that get skin weights (auto weights): the body's chain, not the fingers, eyes or jaw (the
# hands are mittens and the face moves by shape keys). The other bones stay in the rig, unweighted.
DEFORM = {"Hips", "Spine", "Chest", "UpperChest", "Neck", "Head"} | {
    s + n for s in ("Left", "Right") for n in ("Shoulder", "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes")}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return {a[i].lstrip("-"): a[i + 1] for i in range(0, len(a) - 1, 2)}


def log(t0, *msg):
    print(f"[{time.time() - t0:6.1f}s]", *msg, flush=True)


# ---------------------------------------------------------------- meshes

def verts(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def normals(obj):
    me = obj.data
    n = np.empty(len(me.vertices) * 3)
    me.vertex_normals.foreach_get("vector", n)
    return n.reshape(-1, 3)


def set_verts(obj, co):
    obj.data.vertices.foreach_set("co", co.ravel())
    obj.data.update()


def copy_obj(obj, name):
    new = obj.copy()
    new.data = obj.data.copy()
    new.name = new.data.name = name
    new.modifiers.clear()
    bpy.context.scene.collection.objects.link(new)
    return new


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def base_mesh(outfit, garments):
    """The sculpt: metaballs -> 4 mm remesh -> smoothed -> the face pushed in (and its variant). Returns
    (base, closed): base has the mouth cut, the painted cloth lifted off the skin and the body under any
    garments deleted; closed is the whole skin before that (the weight source and the garments' source)."""
    o = sculpt.body_balls().build("base", 0.008)
    sculpt.remesh(o, 0.004)
    sculpt.smooth(o, 0.5, 6)
    face = outfit.get("face", "slack")
    sculpt.apply_offsets(o, lambda c: sculpt.face_offset(c) + sculpt.variant_offset(c, face))
    for p in o.data.polygons:
        p.use_smooth = True
    closed = copy_obj(o, "closed")
    if outfit["dead"]:  # the zombie's jaw opens (jaw_open); a survivor's mouth stays shut
        print(f"mouth cut: {sculpt.cut_mouth(o)} faces")
    co, n = verts(o), normals(o)
    set_verts(o, co + n * paint.cloth_lift(co, outfit)[:, None])
    if garments:
        clothes.strip_covered(o, garments)
    return o, closed


def high_mesh(base, outfit):
    hi = copy_obj(base, "high")
    co, n = verts(hi), normals(hi)
    set_verts(hi, co + n * paint.detail(co, outfit)[:, None])
    sculpt.add_eyes_and_teeth(hi, 24, 16)
    return hi


def garment_high(closed, name):
    hi = clothes.shell(closed, name)
    co, n = verts(hi), normals(hi)
    set_verts(hi, co + n * clothes.detail(name, co)[:, None])
    return hi


def unwrap(objs):
    """One atlas for every piece: the pieces unwrapped together (multi-object edit)."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---------------------------------------------------------------- baking

def new_image(name, size, data):
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=data)
    img.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return img


def bake_material(obj, img, emit=None):
    """A material whose only job is baking: an image node to bake into, and an emission: "position"
    (the rest position encoded into 0..1 as (x+1, y+1, z) / 2) or a constant colour (an id)."""
    mat = bpy.data.materials.new("bake")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    if emit is not None:
        em = nt.nodes.new("ShaderNodeEmission")
        if emit == "position":
            coord = nt.nodes.new("ShaderNodeTexCoord")
            add = nt.nodes.new("ShaderNodeVectorMath")
            add.operation = "MULTIPLY_ADD"
            add.inputs[1].default_value = (0.5, 0.5, 0.5)
            add.inputs[2].default_value = (0.5, 0.5, 0.0)
            nt.links.new(coord.outputs["Object"], add.inputs[0])
            nt.links.new(add.outputs[0], em.inputs["Color"])
        else:
            em.inputs["Color"].default_value = (*emit, 1)
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    else:
        bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    nt.nodes.active = tex
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def bake(objs, img, kind, high=None, samples=1, clear=True, emit=None):
    """Bake into img from every object in objs (or, with high, from high onto the one object)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    for i, o in enumerate(objs):
        bake_material(o, img, emit(i) if callable(emit) else emit)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in objs and o is not high
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    if high:
        high.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bk = scene.render.bake
    bk.margin = 8
    bk.use_clear = clear
    bk.use_selected_to_active = high is not None
    bk.cage_extrusion = 0.012
    bk.max_ray_distance = 0.03
    bk.normal_space = "TANGENT"
    bpy.ops.object.bake(type=kind)


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


def paint_atlas(pieces, outfit, size, style, t0):
    """Bake every piece's rest position, which piece each texel is, and the AO; then paint each texel
    (paint.py for the body, clothes.py for a garment)."""
    lows = [p["low"] for p in pieces]
    pos_img = new_image("pos", size, True)
    bake(lows, pos_img, "EMIT", emit="position")
    id_img = new_image("id", size, True)
    bake(lows, id_img, "EMIT", emit=lambda i: ((i + 1) / 8, 0, 0))
    ao_img = new_image("ao", size, True)
    bake(lows, ao_img, "AO", samples=24 if style == "plus" else 48)
    log(t0, "baked positions, ids and AO")
    pos = pixels(pos_img)
    ids = np.rint(pixels(id_img)[:, 0] * 8).astype(int) - 1
    ao = pixels(ao_img)[:, 0].astype(np.float64)
    covered = pos[:, 3] > 0.5
    col = np.zeros((len(pos), 4), np.float32)
    col[:, 3] = 1
    for k, piece in enumerate(pieces):
        idx = np.nonzero(covered & (ids == k))[0]
        for i in range(0, len(idx), 400000):
            part = idx[i:i + 400000]
            p = pos[part, :3].astype(np.float64) * 2 - np.array([1, 1, 0])
            col[part, :3] = paint.albedo(p, ao[part], outfit, style) if piece["name"] == "body" else clothes.albedo(piece["name"], p, ao[part])
    img = new_image(f"albedo_{style}", size, False)
    img.pixels.foreach_set(col.ravel())
    log(t0, f"painted {covered.sum()} texels")
    for im in (pos_img, id_img, ao_img):
        bpy.data.images.remove(im)
    return img


def final_material(name, albedo, normal, two_sided):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = not two_sided  # glTF doubleSided
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.85
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = albedo
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if normal:
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = normal
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


# ---------------------------------------------------------------- skinning and keys

def in_bag(co):
    c, h = sculpt.MOUTH_BAG_C, sculpt.MOUTH_BAG_HALF
    return Vector(((co.x - c.x) / h[0], (co.y - c.y) / h[1], (co.z - c.z) / h[2])).length < 1.05


def is_head_part(co):
    """Eyeballs, teeth and the mouth's cavity: rigid on the Head bone."""
    de = min((co - sculpt.EYE_L).length, (co - sculpt.EYE_R).length)
    if de < sculpt.EYE_RADIUS * 1.02 or in_bag(co):
        return True
    m = sculpt.MOUTH
    return abs(co.x) < 0.030 and abs(co.z - m.z) < 0.0095 and m.y - 0.003 < co.y < m.y + 0.04


def tidy_weights(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def weight_source(closed, arm):
    """Automatic (heat) weights need a closed surface: the whole skin, decimated, weighted once; every
    piece then takes its weights from it (transfer)."""
    src = copy_obj(closed, "weights")
    sculpt.decimate(src, 40000)
    saved = {b.name: b.use_deform for b in arm.data.bones}
    for b in arm.data.bones:
        b.use_deform = b.name in DEFORM
    bpy.ops.object.select_all(action="DESELECT")
    src.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = saved[b.name]
    src.hide_render = True
    return src


def transfer(obj, src, arm):
    """Weights from the source by the nearest surface; eyes, teeth and the mouth on the Head."""
    obj.vertex_groups.clear()
    m = obj.modifiers.new("Weights", "DATA_TRANSFER")
    m.object = src
    m.use_vert_data = True
    m.data_types_verts = {"VGROUP_WEIGHTS"}
    m.vert_mapping = "POLYINTERP_NEAREST"
    m.layers_vgroup_select_src = "ALL"
    m.layers_vgroup_select_dst = "NAME"
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.datalayout_transfer(modifier=m.name)
    bpy.ops.object.modifier_apply(modifier=m.name)
    head = obj.vertex_groups.get("Head") or obj.vertex_groups.new(name="Head")
    for v in obj.data.vertices:
        if is_head_part(v.co) or not any(g.weight > 1e-6 for g in v.groups):
            for g in list(v.groups):
                obj.vertex_groups[g.group].remove([v.index])
            head.add([v.index], 1.0, "REPLACE")
    obj.parent = arm
    obj.modifiers.new("Armature", "ARMATURE").object = arm
    tidy_weights(obj)


def lower_teeth(obj):
    m = sculpt.MOUTH
    return [v.index for v in obj.data.vertices
            if not in_bag(v.co) and abs(v.co.x) < 0.030 and m.z - 0.0095 < v.co.z < m.z - 0.0002 and m.y - 0.003 < v.co.y < m.y + 0.04]


# ---------------------------------------------------------------- one candidate

def build_candidate(cand, subject, base, high, garments, arm, src, out_dir, size, t0, preview, outfit):
    ladder = LADDER[cand]
    ratios = [t / ladder[0] for t in ladder]
    # The pieces: the body, then each garment (its high-poly shell made once, shared by both looks).
    pieces = [{"name": "body", "high": high, "two_sided": False}]
    pieces += [{"name": g, "high": hi, "two_sided": True} for g, hi in garments.items()]
    garment_tris = sum(clothes.GARMENTS[g]["tris"] for g in garments) * (1 if cand == "plus" else 0.15)
    for piece in pieces:
        low = copy_obj(base if piece["name"] == "body" else piece["high"], f"{piece['name']}_lod0")
        if piece["name"] == "body":
            target = ladder[0] - EYES_TRIS[cand] - garment_tris
            sculpt.decimate(low, target)
            sculpt.add_eyes_and_teeth(low, *EYE_SEGS[cand])
        else:
            target = clothes.GARMENTS[piece["name"]]["tris"] * (1 if cand == "plus" else 0.15)
            sculpt.decimate(low, target)
        piece["low"], piece["tris0"] = low, tris(low)
        log(t0, f"{cand}: {low.name} {tris(low)} triangles")
    unwrap([p["low"] for p in pieces])
    albedo = paint_atlas(pieces, outfit, size, cand, t0)
    normal = None
    if cand == "plus":
        normal = new_image("normal", size, True)
        for i, piece in enumerate(pieces):
            bake([piece["low"]], normal, "NORMAL", high=piece["high"], clear=i == 0)
        log(t0, "baked the normal map")
    mats = {two: final_material(f"look_{cand}_{subject}{'_cloth' if two else ''}", albedo, normal, two) for two in (False, True)}
    meshes, problems = [], []
    for piece in pieces:
        low = piece["low"]
        low.data.materials.clear()
        low.data.materials.append(mats[piece["two_sided"]])
        lods = [low]
        for i in range(1, len(ladder)):
            lod = copy_obj(low, f"{piece['name']}_lod{i}")
            sculpt.decimate(lod, max(60, int(piece["tris0"] * ratios[i])))
            lod.data.validate()  # collapsing can leave degenerate faces (the glTF exporter warns)
            lods.append(lod)
        for i, lod in enumerate(lods):
            if piece["two_sided"]:
                clothes.rim(lod)
            transfer(lod, src, arm)
            if piece["name"] == "body" and i < FACE_LODS[cand] and subject == "zombie":
                lod["lower_teeth"] = lower_teeth(lod)
                sculpt.add_face_keys(lod)
            validate.check_mesh(lod, arm, int(max(piece["tris0"] * ratios[i], 60) * 1.6) + 100, problems)
        meshes += lods
    for i in range(len(ladder)):
        log(t0, f"{cand}: lod{i} {sum(tris(m) for m in meshes if m.name.endswith(f'_lod{i}'))} triangles")
    for p in problems:
        print("VALIDATE:", p)

    # The default face (paint.OUTFITS) is the plain file; another face or a clothes set is a suffix.
    tag = outfit.get("face", "") if subject == "zombie" else outfit.get("clothes", "")
    tag = "" if tag in ("", paint.OUTFITS[subject].get("face", "")) else "_" + tag
    path = os.path.join(out_dir, f"look_{cand}_{subject}{tag}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes + [arm]:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, use_selection=True, export_animations=False,
        export_skins=True, export_all_influences=False, export_def_bones=False, export_morph=True,
        export_morph_normal=False, export_rest_position_armature=True, export_apply=False,
        # Images go out as JPEG (a 2048 colour map is ~1 MB, not ~6 as PNG); the normal map too.
        export_image_format="JPEG", export_jpeg_quality=88, export_cameras=False, export_lights=False)
    log(t0, f"exported {path} ({os.path.getsize(path) / 1e6:.1f} MB)")
    if preview:
        render_preview([m for m in meshes if m.name.endswith("_lod0")], os.path.splitext(os.path.join(preview, os.path.basename(path)))[0])
    for o in meshes + [p["low"] for p in pieces]:
        o.hide_set(True)
        o.hide_render = True
    return problems


def render_preview(objs, base):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 700
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in objs
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    keys = objs[0].data.shape_keys
    shots = [("front", (0, -6, 0.95), (0, 0, 0.95), 2.0, 0, 0), ("side", (6, -2, 0.95), (0, 0, 0.95), 2.0, 0, 0),
             ("face", (1.2, -2.8, 1.66), (0, 0, 1.62), 0.30, 0.15, 0)]
    if keys:
        shots.append(("angry", (1.2, -2.8, 1.66), (0, 0, 1.62), 0.30, 0.8, 1))
    for name, loc, tgt, size, jaw, angry in shots:
        if keys:
            keys.key_blocks["jaw_open"].value = jaw
            keys.key_blocks["angry"].value = angry
        cam.location = loc
        cam.data.ortho_scale = size
        cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base}_{name}.png"
        bpy.ops.render.render(write_still=True)
    if keys:
        keys.key_blocks["jaw_open"].value = keys.key_blocks["angry"].value = 0
    bpy.data.objects.remove(cam)


def main():
    a = args()
    t0 = time.time()
    subject = a.get("subject", "zombie")
    out_dir = os.path.abspath(a.get("out", "godot/assets/characters/look"))
    os.makedirs(out_dir, exist_ok=True)
    preview = os.path.abspath(a["preview"]) if "preview" in a else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = rig.build_armature()
    outfit = dict(paint.OUTFITS[subject])
    if "face" in a:
        outfit["face"] = a["face"]
    names = clothes.OUTFITS.get(a.get("clothes", ""), [])
    if names:
        outfit["clothes"] = a["clothes"]
    base, closed = base_mesh(outfit, names)
    src = weight_source(closed, arm)
    log(t0, f"base {tris(base)} triangles; weights from {tris(src)}")
    high = high_mesh(base, outfit)
    garments = {g: garment_high(closed, g) for g in names}
    for g, hi in garments.items():
        log(t0, f"{g}: high {tris(hi)} triangles")
    problems = []
    for cand in (["plus", "pz"] if "only" not in a else [a["only"]]):
        size = int(a.get("tex", TEX[cand])) if cand == "plus" else TEX[cand]
        problems += build_candidate(cand, subject, base, high, garments, arm, src, out_dir, size, t0, preview, outfit)
    if "blend" in a:
        os.makedirs(os.path.dirname(os.path.abspath(a["blend"])), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a["blend"]))
    log(t0, "done" if not problems else f"{len(problems)} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
