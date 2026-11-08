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
# jaw_open); expressions are near-tier only (the lower LODs carry none).
#
#   blender --background --factory-startup --python art/scripts/build_lookdev.py -- \
#       --subject zombie|survivor --out godot/assets/characters/look [--only plus|pz] [--tex 2048]
#       [--blend art/blend/look_zombie.blend] [--preview DIR]
#
# Writes <out>/look_<cand>_<subject>.glb and prints each LOD's triangles and the bake times.

import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paint  # noqa: E402
import rig  # noqa: E402
import sculpt  # noqa: E402
import validate  # noqa: E402

LADDER = {"plus": [30000, 12000, 6000, 3000, 1200, 600], "pz": [2500, 1200, 600]}
FACE_LODS = {"plus": 2, "pz": 1}
TEX = {"plus": 2048, "pz": 512}
EYE_SEGS = {"plus": (16, 10), "pz": (8, 6)}
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
    bpy.context.scene.collection.objects.link(new)
    return new


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def base_mesh(outfit):
    """The sculpt: metaballs -> 4 mm remesh -> smoothed -> the face pushed in -> cloth lifted off the skin."""
    o = sculpt.body_balls().build("base", 0.008)
    sculpt.remesh(o, 0.004)
    sculpt.smooth(o, 0.5, 6)
    sculpt.apply_offsets(o, sculpt.face_offset)
    co, n = verts(o), normals(o)
    set_verts(o, co + n * paint.cloth_lift(co, outfit)[:, None])
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def high_mesh(base, outfit, segs):
    hi = copy_obj(base, "high")
    co, n = verts(hi), normals(hi)
    set_verts(hi, co + n * paint.detail(co, outfit)[:, None])
    sculpt.add_eyes_and_teeth(hi, 24, 16)
    return hi


def unwrap(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---------------------------------------------------------------- baking

def new_image(name, size, data):
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=data)
    img.colorspace_settings.name = "Non-Color" if data else "sRGB"
    return img


def bake_material(obj, emit_position=False):
    """A material whose only job is baking: an image node to bake into, and (for the position bake)
    an emission of the rest position, encoded into 0..1 as (x+1, y+1, z) / 2."""
    mat = bpy.data.materials.new("bake")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    if emit_position:
        coord = nt.nodes.new("ShaderNodeTexCoord")
        add = nt.nodes.new("ShaderNodeVectorMath")
        add.operation = "MULTIPLY_ADD"
        add.inputs[1].default_value = (0.5, 0.5, 0.5)
        add.inputs[2].default_value = (0.5, 0.5, 0.0)
        nt.links.new(coord.outputs["Object"], add.inputs[0])
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(add.outputs[0], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    else:
        bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    nt.nodes.active = tex
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return tex


def bake(obj, img, kind, high=None, samples=1):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    tex = bake_material(obj, emit_position=kind == "EMIT")
    tex.image = img
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in (obj, high)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    if high:
        high.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bk = scene.render.bake
    bk.margin = 8
    bk.use_selected_to_active = high is not None
    bk.cage_extrusion = 0.012
    bk.max_ray_distance = 0.03
    bk.normal_space = "TANGENT"
    bpy.ops.object.bake(type=kind)


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(-1, 4)


def paint_albedo(low, high, outfit, size, style, t0):
    pos_img = new_image("pos", size, True)
    bake(low, pos_img, "EMIT")
    log(t0, "baked positions")
    ao_img = new_image("ao", size, True)
    bake(low, ao_img, "AO", samples=24 if style == "plus" else 48)
    log(t0, "baked AO")
    pos = pixels(pos_img)
    ao = pixels(ao_img)[:, 0].astype(np.float64)
    covered = pos[:, 3] > 0.5
    p = pos[covered, :3].astype(np.float64) * 2 - np.array([1, 1, 0])
    col = np.zeros((len(pos), 4), np.float32)
    col[:, 3] = 1
    chunk = 400000
    for i in range(0, len(p), chunk):
        idx = np.nonzero(covered)[0][i:i + chunk]
        col[idx, :3] = paint.albedo(p[i:i + chunk], ao[idx], outfit, style)
    img = new_image(f"albedo_{style}", size, False)
    img.pixels.foreach_set(col.ravel())
    log(t0, f"painted {covered.sum()} texels")
    bpy.data.images.remove(pos_img)
    bpy.data.images.remove(ao_img)
    return img


def final_material(name, albedo, normal):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
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

def is_head_part(co):
    """Eyeballs and teeth: rigid on the Head bone."""
    de = min((co - sculpt.EYE_L).length, (co - sculpt.EYE_R).length)
    if de < sculpt.EYE_RADIUS * 1.02:
        return True
    m = sculpt.MOUTH
    return abs(co.x) < 0.030 and abs(co.z - m.z) < 0.0095 and m.y + 0.004 < co.y < m.y + 0.04


def skin(obj, arm):
    saved = {b.name: b.use_deform for b in arm.data.bones}
    for b in arm.data.bones:
        b.use_deform = b.name in DEFORM
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = saved[b.name]
    head = obj.vertex_groups.get("Head") or obj.vertex_groups.new(name="Head")
    for v in obj.data.vertices:
        if is_head_part(v.co) or not v.groups:
            for g in list(v.groups):
                obj.vertex_groups[g.group].remove([v.index])
            head.add([v.index], 1.0, "REPLACE")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def lower_teeth(obj):
    m = sculpt.MOUTH
    return [v.index for v in obj.data.vertices
            if abs(v.co.x) < 0.030 and m.z - 0.0095 < v.co.z < m.z - 0.0002 and m.y + 0.004 < v.co.y < m.y + 0.04]


# ---------------------------------------------------------------- one candidate

def build_candidate(cand, subject, base, high, arm, out_dir, size, t0, preview):
    outfit = paint.OUTFITS[subject]
    ladder = LADDER[cand]
    low = copy_obj(base, f"{cand}_lod0")
    got = sculpt.decimate(low, ladder[0] - (900 if cand == "plus" else 300))  # room for eyes and teeth
    sculpt.add_eyes_and_teeth(low, *EYE_SEGS[cand])
    log(t0, f"{cand}: lod0 {tris(low)} triangles (body {got})")
    unwrap(low)
    albedo = paint_albedo(low, high, outfit, size, cand, t0)
    normal = None
    if cand == "plus":
        normal = new_image("normal", size, True)
        bake(low, normal, "NORMAL", high=high)
        log(t0, "baked the normal map")
    mat = final_material(f"look_{cand}_{subject}", albedo, normal)
    low.data.materials.clear()
    low.data.materials.append(mat)
    lods = [low]
    for i, target in enumerate(ladder[1:], 1):
        lod = copy_obj(low, f"{cand}_lod{i}")
        sculpt.decimate(lod, target)
        lod.data.validate()  # collapsing can leave degenerate faces (the glTF exporter warns)
        lods.append(lod)
    problems = []
    for i, lod in enumerate(lods):
        skin(lod, arm)
        if i < FACE_LODS[cand] and subject == "zombie":
            lod["lower_teeth"] = lower_teeth(lod)
            sculpt.add_face_keys(lod)
        validate.check_mesh(lod, arm, int(ladder[i] * 1.05), problems)
        log(t0, f"{cand}: {lod.name} {tris(lod)} triangles")
    for p in problems:
        print("VALIDATE:", p)

    # Images go out as JPEG (a 2048 colour map is ~1 MB, not ~6 as PNG); the normal map too.
    for img in (albedo, normal):
        if img:
            img.file_format = "JPEG"
    path = os.path.join(out_dir, f"look_{cand}_{subject}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    for o in lods + [arm]:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, use_selection=True, export_animations=False,
        export_skins=True, export_all_influences=False, export_def_bones=False, export_morph=True,
        export_morph_normal=False, export_rest_position_armature=True, export_apply=False,
        export_image_format="JPEG", export_jpeg_quality=88, export_cameras=False, export_lights=False)
    log(t0, f"exported {path} ({os.path.getsize(path) / 1e6:.1f} MB)")
    if preview:
        render_preview(lods[0], os.path.join(preview, f"look_{cand}_{subject}"))
    for o in lods:
        o.hide_set(True)
        o.hide_render = True
    return problems


def render_preview(obj, base):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = scene.render.resolution_y = 700
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o != obj
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    keys = obj.data.shape_keys
    shots = [("front", (0, -6, 0.95), (0, 0, 0.95), 2.0, 0, 0), ("back", (0, 6, 0.95), (0, 0, 0.95), 2.0, 0, 0),
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
    outfit = paint.OUTFITS[subject]
    base = base_mesh(outfit)
    log(t0, f"base {tris(base)} triangles")
    high = high_mesh(base, outfit, (24, 16))
    log(t0, f"high {tris(high)} triangles")
    problems = []
    for cand in (["plus", "pz"] if "only" not in a else [a["only"]]):
        size = int(a.get("tex", TEX[cand])) if cand == "plus" else TEX[cand]
        problems += build_candidate(cand, subject, base, high, arm, out_dir, size, t0, preview)
    if "blend" in a:
        os.makedirs(os.path.dirname(os.path.abspath(a["blend"])), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a["blend"]))
    log(t0, "done" if not problems else f"{len(problems)} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
