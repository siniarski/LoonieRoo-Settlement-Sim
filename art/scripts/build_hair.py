# Hair styles and hats as their own meshes (hair.py), each exported alone on the shared rig so any style goes
# on any body or outfit: the look lab (godot/Labs/LookLab.tscn) attaches them to a character's
# skeleton and tints them. Each is a LOD ladder (the plus look's ratios) with a 512 grey colour map and a
# normal map baked from a high-poly version with strand grooves. Styles with "cards" also get strand cards
# along the hairline (near LODs only): small cut-out quads with a strand texture, for wispy edges.
#
#   blender --background --factory-startup --python art/scripts/build_hair.py -- \
#       --out godot/assets/characters/look/hair [--only crop]
#
# Writes <out>/hair_<style>.glb (meshes hair_lod0..5, cards_lod0..1), hair_<style>_hat.glb (the style as
# worn under a hat: flattened where the hat covers it, no fringe cards) and hat_<hat>.glb (hat_lod0..5).

import math
import os
import random
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Vector, kdtree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_lookdev as L  # noqa: E402
import clothes  # noqa: E402
import hair as H  # noqa: E402
import rig  # noqa: E402
import sculpt  # noqa: E402
import validate  # noqa: E402

RATIOS = [1, 0.4, 0.2, 0.1, 0.04, 0.02]
TEX = 512
CARD_LODS = 2


def border_of(obj):
    counts = {}
    for poly in obj.data.polygons:
        for k in poly.edge_keys:
            counts[k] = counts.get(k, 0) + 1
    return sorted({i for k, c in counts.items() if c == 1 for i in k})


def shell(closed, name, spec, lift):
    """The high-poly shell from the closed skin: cut to the mask, draped, lifted (tapering towards the skin
    at its edge), its extra pieces, strand relief."""
    obj = L.copy_obj(closed, f"{name}_high")
    clothes._keep_faces(obj, spec["mask"](L.verts(obj)) > 0.5)
    rest = L.verts(obj)
    m = obj.modifiers.new("Drape", "SMOOTH")
    m.factor, m.iterations = 0.8, spec["drape"]
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)
    # Thin towards the skin at the edge (a hairline, round the ears, the nape): the lift tapers to 30% over
    # 12 mm from the shell's boundary, so there's no step to see and no rim needed (and never to nothing:
    # a shell on the skin's surface would flicker through it).
    co = L.verts(obj)
    border = border_of(obj)
    taper = np.ones(len(co))
    if border and not name.startswith("long"):  # long hair's curtain grows out of its edge: no thinning
        tree = kdtree.KDTree(len(border))
        for i in border:
            tree.insert(co[i], i)
        tree.balance()
        taper = np.array([0.3 + 0.7 * sculpt.smooth01(tree.find(c)[2] / 0.012) for c in co])
    L.set_verts(obj, co + L.normals(obj) * (lift(rest) * taper)[:, None])
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    spec["extras"](bm)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    for p in obj.data.polygons:
        p.use_smooth = True
    L.set_verts(obj, L.verts(obj) + L.normals(obj) * H.detail(name.split("_")[0], L.verts(obj))[:, None])
    return obj


# ---------------------------------------------------------------- strand cards

# How long a style's cards are (x the 14-26 mm base), and how dark (the matted hair is dirtier).
CARD_LENGTH = {"crop": 0.55, "receding": 0.5, "sidepart": 0.8, "long": 1.0, "ponytail": 0.6, "matted": 0.8}
CARD_SHADE = {"matted": 0.55}


def card_material(style):
    name = f"hair_cards_{style}"
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    tex = H.card_texture(256)
    tex[..., :3] *= CARD_SHADE.get(style, 1.0)
    img = bpy.data.images.new(name, 256, 256, alpha=True)
    img.pixels.foreach_set(tex[::-1].astype(np.float32).ravel())  # Blender's rows run bottom-up
    img.file_format = "PNG"
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = False
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.6
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    clip = nt.nodes.new("ShaderNodeMath")  # Round: the glTF exporter writes alphaMode MASK (cut-out)
    clip.operation = "ROUND"
    nt.links.new(t.outputs["Alpha"], clip.inputs[0])
    nt.links.new(clip.outputs[0], bsdf.inputs["Alpha"])
    return mat


def make_cards(hi, style):
    """Cut-out strand cards along the shell's front edge (the hairline over the forehead and temples; all
    round a matted style's patches), each a bent quad from just inside the edge out over the skin."""
    rnd = random.Random(11)
    co, n = L.verts(hi), L.normals(hi)
    border = border_of(hi)
    if style != "matted":
        border = [i for i in border if co[i][1] < -0.02 and co[i][2] > sculpt.EYE_L.z + 0.02]
    else:  # round the patches, but not on the crown (a card there stands up off the head)
        border = [i for i in border if co[i][2] < 1.715]
    picked = []
    for i in sorted(border, key=lambda i: (co[i][0], co[i][2])):
        p = Vector(co[i])
        if all((p - q).length > 0.0045 for q, _ in picked):
            picked.append((p, Vector(n[i])))
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for p, nrm in picked:
        base = Vector((rnd.uniform(-0.35, 0.35), -0.45, -1)) if style != "matted" else Vector((rnd.uniform(-0.4, 0.4), 0, -1))
        d = (base - nrm * base.dot(nrm)).normalized()
        side = nrm.cross(d).normalized()
        # Rooted well inside the hair (so a card grows out of it, not off the skin), out past the edge.
        length, w = rnd.uniform(0.014, 0.026) * CARD_LENGTH.get(style, 1.0), rnd.uniform(0.010, 0.014)
        root, mid, tip = p + nrm * 0.0030 - d * 0.009, p + nrm * 0.0030 + d * (length * 0.4), p + nrm * 0.0025 + d * length
        ring = [(root, w / 2, 0.0), (mid, w * 0.45, 0.5), (tip, w * 0.3, 1.0)]
        vs = [(bm.verts.new(c - side * hw), bm.verts.new(c + side * hw), v) for c, hw, v in ring]
        k = rnd.randrange(4)
        for (a0, a1, v0), (b0, b1, v1) in zip(vs, vs[1:]):
            f = bm.faces.new((a0, a1, b1, b0))
            for loop, (u, v) in zip(f.loops, ((k / 4, v0), ((k + 1) / 4, v0), ((k + 1) / 4, v1), (k / 4, v1))):
                loop[uv].uv = (u, v)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("cards_lod0")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("cards_lod0", me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(card_material(style))
    return obj, len(picked)


# ---------------------------------------------------------------- one style (or hat)

def build(name, spec, lift, closed, src, arm, out_dir, t0, cards, prefix="hair"):
    hi = shell(closed, name, spec, lift)
    low = L.copy_obj(hi, f"{prefix}_lod0")
    sculpt.decimate(low, spec["tris"])
    L.unwrap([low])
    pos_img = L.new_image("pos", TEX, True)
    L.bake([low], pos_img, "EMIT", emit="position")
    ao_img = L.new_image("ao", TEX, True)
    L.bake([low], ao_img, "AO", samples=32)
    pos, ao = L.pixels(pos_img), L.pixels(ao_img)[:, 0].astype(np.float64)
    col = np.zeros((len(pos), 4), np.float32)
    col[:, 3] = 1
    idx = np.nonzero(pos[:, 3] > 0.5)[0]
    col[idx, :3] = H.albedo(name.split("_")[0], pos[idx, :3].astype(np.float64) * 2 - np.array([1, 1, 0]), ao[idx])
    albedo = L.new_image(f"{name}", TEX, False)
    albedo.pixels.foreach_set(col.ravel())
    normal = L.new_image(f"{name}_normal", TEX, True)
    L.bake([low], normal, "NORMAL", high=hi)
    for im in (albedo, normal):  # JPEG out (export_image_format AUTO keeps each image's own format)
        im.file_format = "JPEG"
    mat = L.final_material(f"{prefix}_{name}", albedo, normal, True)
    low.data.materials.clear()
    low.data.materials.append(mat)
    tris0 = L.tris(low)
    lods, problems = [low], []
    for i in range(1, len(RATIOS)):
        lod = L.copy_obj(low, f"{prefix}_lod{i}")
        sculpt.decimate(lod, max(60, int(tris0 * RATIOS[i])))
        lod.data.validate()
        lods.append(lod)
    for i, lod in enumerate(lods):
        if name == "long":  # only long hair has free ends to show a thickness
            clothes.rim(lod, 0.003)
        L.transfer(lod, src, arm)
        validate.check_mesh(lod, arm, int(max(tris0 * RATIOS[i], 60) * 2) + 200, problems)  # the rim adds two a boundary edge
    extra = []
    n_cards = 0
    if cards:
        c0, n_cards = make_cards(hi, name)
        extra = [c0] + [L.copy_obj(c0, f"cards_lod{i}") for i in range(1, CARD_LODS)]
        for c in extra:
            L.transfer(c, src, arm)
            validate.check_mesh(c, arm, 4 * n_cards + 10, problems)
    for p in problems:
        print("VALIDATE:", p)
    path = os.path.join(out_dir, f"{prefix}_{name}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    for o in lods + extra + [arm]:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, use_selection=True, export_animations=False,
        export_skins=True, export_all_influences=False, export_def_bones=False, export_morph=False,
        export_rest_position_armature=True, export_apply=False,
        export_image_format="JPEG", export_jpeg_quality=88, export_cameras=False, export_lights=False)  # the exporter keeps an image with alpha (the cards) PNG
    L.log(t0, f"{prefix}_{name}: {' / '.join(str(L.tris(l)) for l in lods)} triangles"
              + (f", {n_cards} cards" if cards else "") + f", {os.path.getsize(path) / 1e6:.2f} MB")
    for o in lods + extra + [hi]:
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
    only = a.get("only")
    for style, spec in H.STYLES.items():
        if only and only != style:
            continue
        problems += build(style, spec, spec["lift"], closed, src, arm, out_dir, t0, spec.get("cards", False))
        if style != "beard":
            under = (lambda f: (lambda p: H.under_hat(p, f(p))))(spec["lift"])
            problems += build(f"{style}_hat", spec, under, closed, src, arm, out_dir, t0, False)
    for hat, spec in H.HATS.items():
        if only and only != hat:
            continue
        problems += build(hat, spec, spec["lift"], closed, src, arm, out_dir, t0, False, prefix="hat")
    L.log(t0, "done" if not problems else f"{len(problems)} problem(s)")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
