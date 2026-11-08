# Hair styles as their own meshes (hair.py), each exported alone on the shared rig so any style goes on any
# body or outfit: the look lab (godot/Characters/LookLab.tscn) attaches them to a character's skeleton
# and tints them. Each is a LOD ladder (the plus look's ratios) with a 1024 grey colour map and a normal
# map baked from a high-poly version with strand grooves.
#
#   blender --background --factory-startup --python art/scripts/build_hair.py -- \
#       --out godot/assets/characters/look/hair [--only crop] [--preview DIR]
#
# Writes <out>/hair_<style>.glb (meshes hair_lod0..5) for each style in hair.STYLES.

import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import kdtree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_lookdev as L  # noqa: E402
import clothes  # noqa: E402
import hair as H  # noqa: E402
import rig  # noqa: E402
import sculpt  # noqa: E402
import validate  # noqa: E402

RATIOS = [1, 0.4, 0.2, 0.1, 0.04, 0.02]
TEX = 1024


def shell(closed, style):
    """The style's high-poly shell from the closed skin: cut to where the hair grows, draped, lifted,
    its extra pieces, strand relief."""
    st = H.STYLES[style]
    obj = L.copy_obj(closed, f"{style}_high")
    clothes._keep_faces(obj, st["mask"](L.verts(obj)) > 0.5)
    rest = L.verts(obj)
    m = obj.modifiers.new("Drape", "SMOOTH")
    m.factor, m.iterations = 0.8, st["drape"]
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)
    # Thin towards the skin at the edge (a hairline, round the ears, the nape): the lift tapers to 30% over
    # 12 mm from the shell's boundary, so there's no step to see and no rim needed (and never to nothing:
    # a shell on the skin's surface would flicker through it).
    co = L.verts(obj)
    counts = {}
    for poly in obj.data.polygons:
        for k in poly.edge_keys:
            counts[k] = counts.get(k, 0) + 1
    border = sorted({i for k, c in counts.items() if c == 1 for i in k})
    tree = kdtree.KDTree(len(border))
    for i in border:
        tree.insert(co[i], i)
    tree.balance()
    taper = np.array([0.3 + 0.7 * sculpt.smooth01(tree.find(c)[2] / 0.012) for c in co]) if border else np.ones(len(co))
    if style == "long":  # its curtain grows out of the edge: no thinning there
        taper = np.ones(len(co))
    L.set_verts(obj, co + L.normals(obj) * (st["lift"](rest) * taper)[:, None])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    st["extras"](bm)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    for p in obj.data.polygons:
        p.use_smooth = True
    L.set_verts(obj, L.verts(obj) + L.normals(obj) * H.detail(style, L.verts(obj))[:, None])
    return obj


def build(style, closed, src, arm, out_dir, t0, preview):
    hi = shell(closed, style)
    low = L.copy_obj(hi, "hair_lod0")
    sculpt.decimate(low, H.STYLES[style]["tris"])
    L.unwrap([low])
    pos_img = L.new_image("pos", TEX, True)
    L.bake([low], pos_img, "EMIT", emit="position")
    ao_img = L.new_image("ao", TEX, True)
    L.bake([low], ao_img, "AO", samples=32)
    pos, ao = L.pixels(pos_img), L.pixels(ao_img)[:, 0].astype(np.float64)
    covered = pos[:, 3] > 0.5
    col = np.zeros((len(pos), 4), np.float32)
    col[:, 3] = 1
    idx = np.nonzero(covered)[0]
    col[idx, :3] = H.albedo(style, pos[idx, :3].astype(np.float64) * 2 - np.array([1, 1, 0]), ao[idx])
    albedo = L.new_image(f"hair_{style}", TEX, False)
    albedo.pixels.foreach_set(col.ravel())
    normal = L.new_image(f"hair_{style}_normal", TEX, True)
    L.bake([low], normal, "NORMAL", high=hi)
    mat = L.final_material(f"hair_{style}", albedo, normal, True)
    low.data.materials.clear()
    low.data.materials.append(mat)
    tris0 = L.tris(low)
    lods, problems = [low], []
    for i in range(1, len(RATIOS)):
        lod = L.copy_obj(low, f"hair_lod{i}")
        sculpt.decimate(lod, max(60, int(tris0 * RATIOS[i])))
        lod.data.validate()
        lods.append(lod)
    for i, lod in enumerate(lods):
        if style == "long":  # only long hair has free ends to show a thickness
            clothes.rim(lod, 0.003)
        L.transfer(lod, src, arm)
        validate.check_mesh(lod, arm, int(max(tris0 * RATIOS[i], 60) * 2) + 200, problems)  # the rim adds two a boundary edge
    for p in problems:
        print("VALIDATE:", p)
    path = os.path.join(out_dir, f"hair_{style}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    for o in lods + [arm]:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, use_selection=True, export_animations=False,
        export_skins=True, export_all_influences=False, export_def_bones=False, export_morph=False,
        export_rest_position_armature=True, export_apply=False,
        export_image_format="JPEG", export_jpeg_quality=88, export_cameras=False, export_lights=False)
    L.log(t0, f"{style}: {' / '.join(str(L.tris(l)) for l in lods)} triangles, {os.path.getsize(path) / 1e6:.2f} MB")
    for o in lods + [hi]:
        bpy.data.objects.remove(o)
    for im in (pos_img, ao_img):
        bpy.data.images.remove(im)
    return problems


def main():
    a = L.args()
    t0 = time.time()
    out_dir = os.path.abspath(a.get("out", "godot/assets/characters/look/hair"))
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = rig.build_armature()
    skin = L.load_skin("skin")
    closed = L.copy_obj(skin, "closed")
    bm = bmesh.new()
    bm.from_mesh(closed.data)
    bmesh.ops.holes_fill(bm, edges=bm.edges[:], sides=0)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(closed.data)
    bm.free()
    src = L.weight_source(closed, arm)
    for o in (skin, closed):
        o.hide_render = True
    problems = []
    for style in ([a["only"]] if "only" in a else list(H.STYLES)):
        problems += build(style, closed, src, arm, out_dir, t0, a.get("preview"))
    L.log(t0, "done" if not problems else f"{len(problems)} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
