# The animals for first person (deer, rabbit, wolf, bear, dog; species in animals.py), all by script:
#
#   blender --background --factory-startup --python art/scripts/build_animals.py -- \
#       --out godot/assets/characters/animals [--kinds deer,wolf] [--preview DIR] [--blend art/blend/animals.blend]
#
# Per kind: a quadruped rig (Root at the floor; Hips, Spine, Neck, Head, Jaw for the biters, a tail,
# three bones a leg), a body of metaball masses over it (as sculpt.py's), voxel-remeshed, smoothed
# and decimated into an LOD ladder (animals.py `lods`), skinned by heat weights (the jaw by hand:
# everything under the mouth's line), painted in vertex colours (no textures: a coat is broad
# colour), and clips made in place (no root motion: the sim moves animals): idle, walk, run, and
# attack for wolves, bears and dogs. It writes <kind>.glb and <kind>.json (the clips' lengths, the
# ground speed each clip was made at, the height) to --out: the renderer (godot/World3D/Animals.cs)
# picks a clip by the sim's speed and scales its rate by that speed.
#
# Clips are leg swings in world terms (degrees about the armature's axes, relative to the rest), the
# stance phase sweeping the foot back at a steady rate, so a clip's ground speed is the stride over
# the stance time: speed = 2 L sin(swing) / (duty * cycle), L the leg's length.

import json
import math
import os
import sys
import time

import bmesh
import bpy
from mathutils import Euler, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import animals  # noqa: E402
import sculpt  # noqa: E402
from animals import V, smooth01  # noqa: E402

FPS = 30
LEG_BONES = {"front": ("UpperArm", "Forearm", "FrontFoot"), "hind": ("Thigh", "Shin", "HindFoot")}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {}
    for i in range(0, len(a) - 1, 2):
        out[a[i].lstrip("-")] = a[i + 1]
    return out


def log(t0, msg):
    print(f"[{time.time() - t0:6.1f}s] {msg}", flush=True)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


def shoulder(spec):
    return spec["front"]["pts"][0][1]


# ---------------------------------------------------------------- rig

def bone_list(spec):
    sp = spec["spine"]

    def P(p):
        return V(0, *p)

    s = shoulder(spec)
    bones = [("Root", V(0, 0, 0), V(0, 0.2 * s, 0), None),
             ("Hips", P(sp["pelvis"]), P(sp["mid"]), "Root"),
             ("Spine", P(sp["mid"]), P(sp["withers"]), "Hips"),
             ("Neck", P(sp["neck"]), P(sp["head"]), "Spine"),
             ("Head", P(sp["head"]), P(sp["snout"]), "Neck")]
    if spec["jaw"]:
        bones.append(("Jaw", P(spec["jaw"][0]), P(spec["jaw"][1]), "Head"))
    tail = spec["tail"]
    for i in range(len(tail) - 1):
        bones.append((f"Tail{i + 1}", P(tail[i]), P(tail[i + 1]), "Hips" if i == 0 else f"Tail{i}"))
    for sign, suf in ((1, "L"), (-1, "R")):
        for leg, parent in (("front", "Spine"), ("hind", "Hips")):
            L = spec[leg]
            pts = [V(sign * L["side"], *p) for p in L["pts"]]
            names = LEG_BONES[leg]
            for i, n in enumerate(names):
                bones.append((f"{n}.{suf}", pts[i], pts[i + 1], parent if i == 0 else f"{names[i - 1]}.{suf}"))
    return bones


def build_rig(spec):
    data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in bone_list(spec):
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = head, tail
        if parent:
            eb.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    data.bones["Root"].use_deform = False
    return arm


def segments(arm):
    """Each deforming bone's rest segment, armature space."""
    return {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones if b.use_deform}


def seg_dist(p, a, b):
    d = b - a
    t = max(0.0, min(1.0, (p - a).dot(d) / max(d.length_squared, 1e-12)))
    return (p - (a + d * t)).length


# ---------------------------------------------------------------- body

def balls(spec):
    b = sculpt.Balls()
    for m in spec["masses"]:
        kind = m[0]
        if kind == "e":
            s, f, u, hs, hf, hu = m[1:7]
            tilt = m[7] if len(m) > 7 else 0
            rot = Euler((math.radians(tilt), 0, 0)).to_quaternion()
            for sx in ((1, -1) if s else (1,)):
                b.ellipsoid(V(sx * s, f, u), (hs, hf, hu), rot=rot)
        elif kind == "t":
            s1, f1, u1, s2, f2, u2, r1, r2 = m[1:9]
            for sx in ((1, -1) if (s1 or s2) else (1,)):
                b.taper(V(sx * s1, f1, u1), V(sx * s2, f2, u2), r1, r2, steps=5)
        elif kind in ("c", "n"):
            s1, f1, u1, s2, f2, u2, r = m[1:8]
            for sx in ((1, -1) if (s1 or s2) else (1,)):
                b.capsule(V(sx * s1, f1, u1), V(sx * s2, f2, u2), r, neg=kind == "n")
    for leg in ("front", "hind"):
        L = spec[leg]
        for sx in (1, -1):
            pts = [V(sx * L["side"], *p) for p in L["pts"]]
            for i in range(3):
                b.taper(pts[i], pts[i + 1], L["r"][i], L["r"][i + 1], steps=2)
    return b


def build_body(spec, t0):
    obj = balls(spec).build(spec["kind"] + "_hi", spec["res"])
    log(t0, f"metaballs: {len(obj.data.polygons)} faces")
    sculpt.remesh(obj, spec["voxel"])
    sculpt.smooth(obj, 0.5, 4)
    log(t0, f"remeshed: {len(obj.data.polygons)} faces")
    return obj


def copy_obj(obj, name):
    c = obj.copy()
    c.data = obj.data.copy()
    c.name = c.data.name = name
    bpy.context.scene.collection.objects.link(c)
    return c


def sphere_obj(name, c, r, group, segs=8):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=max(4, segs - 2), radius=r)
    bmesh.ops.translate(bm, vec=c, verts=ret["verts"])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.vertex_groups.new(name=group).add(list(range(len(me.vertices))), 1.0, "REPLACE")
    return o


def antler_obj(name, spec, tris_scale):
    """Tapered tubes along each branch (both sides), rigid on the head."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    segs = 6 if tris_scale > 0.5 else 4
    for branch in spec["antlers"]:
        for sx in (1, -1):
            pts = [V(sx * p[0], p[1], p[2]) for p in branch]
            n = len(pts) - 1
            for i in range(n):
                r1 = 0.016 - 0.009 * i / n if branch is spec["antlers"][0] else 0.009 - 0.004 * i / n
                r2 = 0.016 - 0.009 * (i + 1) / n if branch is spec["antlers"][0] else 0.009 - 0.004 * (i + 1) / n
                d = pts[i + 1] - pts[i]
                ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r1, radius2=max(r2, 0.003), depth=d.length)
                rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
                from mathutils import Matrix
                bmesh.ops.transform(bm, matrix=Matrix.Translation((pts[i] + pts[i + 1]) / 2) @ rot, verts=ret["verts"])
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.vertex_groups.new(name="Head").add(list(range(len(me.vertices))), 1.0, "REPLACE")
    return o


def bind(obj, arm):
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def heat_weights(obj, arm):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def join_into(target, others):
    bpy.ops.object.select_all(action="DESELECT")
    for o in others:
        o.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()


def jaw_weights(obj, spec):
    """Everything forward of the hinge and under the mouth's line moves with the jaw (heat weights
    would split the muzzle by distance, not by the mouth)."""
    if not spec["jaw"]:
        return
    (hf, hu), (cf, cu) = spec["jaw"]
    nf, nu = spec["spine"]["snout"]
    s = shoulder(spec)
    # The mouth's line runs from just in front of the hinge to under the nose.
    a, b = Vector((hf + 0.03 * s, hu)), Vector((nf, (nu + cu) / 2 + 0.004 * s))
    d = (b - a).normalized()
    nrm = Vector((-d.y, d.x))  # up from the line
    jaw = obj.vertex_groups.get("Jaw") or obj.vertex_groups.new(name="Jaw")
    groups = {g.index: g for g in obj.vertex_groups}
    for v in obj.data.vertices:
        p = Vector((-v.co.y, v.co.z))
        if abs(v.co.x) > 0.12 * s or p.x < hf - 0.02 * s:
            continue
        below = -(p - a).dot(nrm)
        if below < -0.004 * s or (p - a).dot(d) > (b - a).length + 0.1 * s:
            continue
        # Only the head's own vertices (not the throat): those weighted mostly to Head or Jaw.
        top = max(v.groups, key=lambda g: g.weight, default=None)
        if top is None or groups[top.group].name not in ("Head", "Jaw"):
            continue
        w = smooth01((p.x - (hf - 0.01 * s)) / (0.05 * s)) * smooth01((below + 0.004 * s) / (0.006 * s))
        for g in v.groups:
            if g.group != jaw.index:
                groups[g.group].add([v.index], g.weight * (1 - w), "REPLACE")
        jaw.add([v.index], w, "REPLACE")


def fill_unweighted(obj, arm):
    segs = segments(arm)
    for v in obj.data.vertices:
        if sum(g.weight for g in v.groups) < 1e-4:
            name = min(segs, key=lambda n: seg_dist(v.co, *segs[n]))
            (obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)).add([v.index], 1.0, "REPLACE")


def tidy_weights(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=0.01)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def material(spec):
    mat = bpy.data.materials.new(spec["kind"])
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    nt.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.92
    return mat


def paint_obj(obj, spec, eye_verts=0, antler=False):
    me = obj.data
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "POINT")
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = me.color_attributes.active_color_index
    names = {g.index: g.name for g in obj.vertex_groups}
    for v in me.vertices:
        if antler:
            base = smooth01((v.co.z - 1.42) / 0.25)
            c = animals.mix((0.30, 0.25, 0.19), (0.62, 0.56, 0.46), base)
        elif v.index >= len(me.vertices) - eye_verts:
            c = (0.035, 0.03, 0.028)
        else:
            top = max(v.groups, key=lambda g: g.weight, default=None)
            c = animals.paint(spec, v.co, names[top.group] if top else "Spine")
        attr.data[v.index].color_srgb = (*c, 1.0)


def eye_spots(spec, hi):
    """Each eye's centre: on the head's surface nearest the spec's point, sunk 60% of its radius."""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromObject(hi, bpy.context.evaluated_depsgraph_get())
    out = []
    for c, r in spec["eyes"]:
        p, n, _, _ = tree.find_nearest(V(*c))
        out.append((p - n * r * 0.6, r))
    return out


def make_lods(spec, hi, arm, t0):
    mat = material(spec)
    out = []
    spots = eye_spots(spec, hi)
    for i, tris in enumerate(spec["lods"]):
        obj = copy_obj(hi, f"{spec['kind']}_lod{i}")
        segs = (8, 6, 4)[min(i, 2)]
        eye_tris = 2 * len(spots) * (segs * (max(4, segs - 2) - 2) * 2 + 2 * segs)
        got = sculpt.decimate(obj, tris - eye_tris)
        heat_weights(obj, arm)
        jaw_weights(obj, spec)
        # Eyes: half sunk into the head, rigid on it.
        eyes = []
        for c, r in spots:
            for sx in (1, -1):
                eyes.append(sphere_obj("eye", Vector((sx * abs(c.x), c.y, c.z)), r, "Head", segs))
        eye_verts = sum(len(e.data.vertices) for e in eyes)
        join_into(obj, eyes)
        fill_unweighted(obj, arm)
        tidy_weights(obj)
        paint_obj(obj, spec, eye_verts)
        obj.data.materials.clear()
        obj.data.materials.append(mat)
        for p in obj.data.polygons:
            p.use_smooth = True
        out.append(obj)
        log(t0, f"{obj.name}: {got + eye_tris} triangles")
        if spec.get("antlers") and i < 2:
            a = antler_obj(f"{spec['kind']}_antlers_lod{i}", spec, 1.0 if i == 0 else 0.4)
            bind(a, arm)
            paint_obj(a, spec, antler=True)
            a.data.materials.append(mat)
            out.append(a)
    return out


# ---------------------------------------------------------------- clips

def pose_clear(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)


def apply_pose(arm, pose):
    """pose: bone -> (rx, ry, rz) degrees about the armature's axes, relative to the rest; "loc": the
    hips' offset (side, forward, up) in metres."""
    for name, rot in pose.items():
        if name == "loc":
            pb = arm.pose.bones["Hips"]
            pb.location = pb.bone.matrix_local.to_3x3().inverted() @ V(*rot)
            continue
        pb = arm.pose.bones.get(name)
        if pb is None:
            continue
        rest = pb.bone.matrix_local.to_quaternion()
        q = Euler(tuple(math.radians(x) for x in rot)).to_quaternion()
        pb.rotation_quaternion = rest.inverted() @ q @ rest


def add(pose, name, rx=0.0, ry=0.0, rz=0.0):
    x, y, z = pose.get(name, (0.0, 0.0, 0.0))
    pose[name] = (x + rx, y + ry, z + rz)


def leg_cycle(phase, duty, amp):
    """(angle forward in degrees, swing lift 0..1) at a phase of the cycle, 0 = touch-down."""
    phase %= 1.0
    if phase < duty:
        return amp * (1 - 2 * phase / duty), 0.0
    u = (phase - duty) / (1 - duty)
    return amp * (-1 + 2 * smooth01(u)), math.sin(math.pi * u)


PHASES = {
    "walk": {("hind", "L"): 0.0, ("front", "L"): 0.25, ("hind", "R"): 0.5, ("front", "R"): 0.75},
    "canter": {("hind", "L"): 0.0, ("hind", "R"): 0.12, ("front", "L"): 0.5, ("front", "R"): 0.62},
    "bound": {("hind", "L"): 0.0, ("hind", "R"): 0.04, ("front", "L"): 0.5, ("front", "R"): 0.55},
    "hop": {("hind", "L"): 0.0, ("hind", "R"): 0.0, ("front", "L"): 0.45, ("front", "R"): 0.5},
}


def leg_len(spec, leg):
    return spec[leg]["pts"][0][1]


def gait_numbers(spec, g):
    """The swing of each leg and the clip's ground speed (m/s): both legs cover one stride."""
    stride = 2 * leg_len(spec, "hind") * math.sin(math.radians(g["swing"]))
    amp = {"hind": g["swing"],
           "front": math.degrees(math.asin(min(0.95, stride / (2 * leg_len(spec, "front")))))}
    frames = round(g["cycle"] * FPS)
    return amp, stride / (g["duty"] * frames / FPS), frames


def gait_pose(spec, g, t):
    amp, _, _ = gait_numbers(spec, g)
    s = shoulder(spec)
    gait = g.get("gait", "walk")
    pose = {}
    for (leg, suf), ph in PHASES[gait].items():
        ang, lift = leg_cycle(t - ph, g["duty"], amp[leg])
        lift *= g["lift"]
        a, b, c = LEG_BONES[leg]
        add(pose, f"{a}.{suf}", -ang)
        if leg == "hind":
            add(pose, f"{b}.{suf}", 38 * lift)
            add(pose, f"{c}.{suf}", -45 * lift)
        else:
            add(pose, f"{b}.{suf}", -18 * lift)
            add(pose, f"{c}.{suf}", 75 * lift)
    w = 2 * math.pi * t
    if gait == "walk":
        pose["loc"] = (0, 0, g["bob"] * math.cos(2 * w))
        add(pose, "Spine", 0, 0, 3 * math.sin(w))
        add(pose, "Neck", -3 * math.cos(2 * w), 0, -2 * math.sin(w))
        add(pose, "Head", 2 * math.cos(2 * w))
    else:
        flex = 8 if gait in ("canter", "bound") else 12
        pose["loc"] = (0, 0, g["bob"] * math.sin(w + 0.6))
        add(pose, "Hips", flex * math.sin(w))
        add(pose, "Spine", -flex * math.sin(w))
        add(pose, "Neck", 8 * math.sin(w + 0.4))
        add(pose, "Head", -5 * math.sin(w + 0.4))
    for i, tb in enumerate(n for n in ("Tail1", "Tail2") if n in [b.name for b in bpy.data.objects["Armature"].data.bones]):
        add(pose, tb, (6 if gait != "walk" else 2) * math.sin(w - 0.8 * (i + 1)), 0, 6 * math.sin(w - 0.6 * (i + 1)))
    _ = s
    return pose


def idle_pose(spec, t):
    w = 2 * math.pi * t
    pose = {}
    breath = math.sin(3 * w)
    add(pose, "Spine", 0.8 * breath)
    add(pose, "Hips", -0.4 * breath)
    look = math.sin(w) * 0.6 + math.sin(2 * w + 1.0) * 0.4
    add(pose, "Neck", 3 * math.sin(2 * w + 0.3) + (6 if spec["kind"] in ("wolf", "dog", "bear") else 0), 0, 14 * look)
    add(pose, "Head", -2 * math.sin(2 * w + 0.3) - (6 if spec["kind"] in ("wolf", "dog", "bear") else 0), 0, 6 * look)
    add(pose, "Tail1", 0, 0, 5 * math.sin(2 * w))
    add(pose, "Tail2", 0, 0, 6 * math.sin(2 * w - 0.8))
    return pose


def attack_keys(spec):
    s = shoulder(spec)
    if spec["attack"] == "lunge":
        crouch = {"loc": (0, -0.06 * s, -0.07 * s), "Neck": (12, 0, 0), "Spine": (4, 0, 0)}
        lunge = {"loc": (0, 0.14 * s, 0.03 * s), "Neck": (-16, 0, 0), "Head": (14, 0, 0), "Jaw": (34, 0, 0), "Spine": (-6, 0, 0)}
        snap = {"loc": (0, 0.11 * s, 0.01 * s), "Neck": (-10, 0, 0), "Head": (12, 0, 0), "Jaw": (0, 0, 0)}
        for suf in ("L", "R"):
            crouch |= {f"Thigh.{suf}": (-14, 0, 0), f"Shin.{suf}": (22, 0, 0), f"HindFoot.{suf}": (-22, 0, 0), f"UpperArm.{suf}": (10, 0, 0), f"Forearm.{suf}": (-10, 0, 0)}
            lunge |= {f"Thigh.{suf}": (22, 0, 0), f"Shin.{suf}": (-6, 0, 0), f"UpperArm.{suf}": (-34, 0, 0), f"Forearm.{suf}": (-18, 0, 0), f"FrontFoot.{suf}": (30, 0, 0)}
            snap |= {f"Thigh.{suf}": (16, 0, 0), f"UpperArm.{suf}": (-20, 0, 0), f"FrontFoot.{suf}": (10, 0, 0)}
        return 0.8, [(0.0, {}), (0.30, crouch), (0.48, lunge), (0.60, snap), (1.0, {})]
    # A bear's maul: up on its hind legs, a forepaw raking down, the jaws in.
    rear = {"loc": (0, -0.05 * s, 0.10 * s), "Hips": (-22, 0, 0), "Thigh.L": (22, 0, 0), "Thigh.R": (22, 0, 0),
            "UpperArm.R": (-70, 0, 0), "Forearm.R": (-35, 0, 0), "FrontFoot.R": (-10, 0, 0),
            "UpperArm.L": (-25, 0, 0), "Forearm.L": (-20, 0, 0), "Neck": (-10, 0, 0), "Jaw": (14, 0, 0)}
    rake = {"loc": (0, 0.10 * s, 0.03 * s), "Hips": (-6, 0, 0), "Thigh.L": (8, 0, 0), "Thigh.R": (8, 0, 0),
            "UpperArm.R": (18, 0, 0), "Forearm.R": (0, 0, 0), "FrontFoot.R": (25, 0, 0),
            "UpperArm.L": (-10, 0, 0), "Neck": (10, 0, 0), "Head": (10, 0, 0), "Jaw": (34, 0, 0)}
    bite = dict(rake) | {"Jaw": (4, 0, 0), "Neck": (6, 0, 0)}
    return 1.0, [(0.0, {}), (0.35, rear), (0.55, rake), (0.68, bite), (1.0, {})]


def keyed(keys, t):
    for (ta, a), (tb, b) in zip(keys, keys[1:]):
        if ta <= t <= tb:
            u = smooth01((t - ta) / (tb - ta))
            pose = {}
            for n in set(a) | set(b):
                va, vb = a.get(n, (0, 0, 0)), b.get(n, (0, 0, 0))
                pose[n] = tuple(x + (y - x) * u for x, y in zip(va, vb))
            return pose
    return dict(keys[-1][1])


def make_clip(arm, name, frames, pose_at):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    animated = [b.name for b in arm.data.bones if b.use_deform]
    for f in range(frames + 1):
        pose_clear(arm)
        apply_pose(arm, pose_at(f / frames))
        for n in animated:
            arm.pose.bones[n].keyframe_insert("rotation_quaternion", frame=f)
        arm.pose.bones["Hips"].keyframe_insert("location", frame=f)
    return act


def make_clips(spec, arm):
    info = {}
    idle_frames = 4 * FPS
    make_clip(arm, "idle", idle_frames, lambda t: idle_pose(spec, t))
    info["idle"] = dict(length=idle_frames / FPS, speed=0.0)
    for name in ("walk", "run"):
        g = spec["gaits"][name]
        _, speed, frames = gait_numbers(spec, g)
        make_clip(arm, name, frames, lambda t, g=g: gait_pose(spec, g, t))
        info[name] = dict(length=frames / FPS, speed=round(speed, 3))
    if spec["attack"]:
        length, keys = attack_keys(spec)
        frames = round(length * FPS)
        make_clip(arm, "attack", frames, lambda t: keyed(keys, t))
        info["attack"] = dict(length=frames / FPS, speed=0.0)
    arm.animation_data.action = bpy.data.actions["idle"]
    pose_clear(arm)
    return info


# ---------------------------------------------------------------- checks, export, preview

def check(objs, arm, spec):
    """The rules the humanoid validator holds characters to, for a quadruped: at most four weights a
    vertex, normalised, none unweighted; the budget; the clips; nothing moves Root (no root motion)."""
    problems = []
    for o in objs:
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        for v in o.data.vertices:
            ws = [g.weight for g in v.groups if g.weight > 0]
            if not ws:
                problems.append(f"{o.name}: vertex {v.index} unweighted")
                break
            if len(ws) > 4 or abs(sum(ws) - 1) > 0.01:
                problems.append(f"{o.name}: vertex {v.index} has {len(ws)} weights summing {sum(ws):.3f}")
                break
        lod = int(o.name.rsplit("lod", 1)[1])
        if "antlers" not in o.name and tris > spec["lods"][lod] * 1.15:
            problems.append(f"{o.name}: {tris} triangles over {spec['lods'][lod]}")
    want = {"idle", "walk", "run"} | ({"attack"} if spec["attack"] else set())
    have = {a.name for a in bpy.data.actions}
    if want - have:
        problems.append(f"clips missing: {sorted(want - have)}")
    for a in bpy.data.actions:
        for fc in getattr(a, "fcurves", []) or []:
            if 'pose.bones["Root"]' in fc.data_path:
                problems.append(f"{a.name}: Root is animated")
    return problems


def export_glb(path, objs, arm):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs + [arm]:
        o.select_set(True)
    kw = dict(filepath=path, export_format="GLB", export_yup=True, use_selection=True,
              export_animations=True, export_animation_mode="ACTIONS",
              export_force_sampling=True, export_frame_range=False,
              export_optimize_animation_size=True, export_optimize_animation_keep_anim_armature=False,
              export_skins=True, export_all_influences=False, export_def_bones=True,
              export_rest_position_armature=True, export_apply=False, export_cameras=False, export_lights=False,
              export_materials="EXPORT")
    try:
        bpy.ops.export_scene.gltf(**kw, export_vertex_color="ACTIVE")
    except TypeError:
        bpy.ops.export_scene.gltf(**kw, export_colors=True)


def render_preview(spec, objs, arm, base):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    scene.world = scene.world or bpy.data.worlds.new("World")
    lod0 = [o for o in objs if o.name.endswith("lod0")]
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o not in lod0
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    s = shoulder(spec)
    lo = Vector([min(v.co[i] for o in lod0 for v in o.data.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for o in lod0 for v in o.data.vertices) for i in range(3)])
    mid = (lo + hi) / 2
    size = max(hi.y - lo.y, hi.z - lo.z) * 1.25
    shots = [("side", (1, 0, 0), "idle", 0), ("front", (0, -1, 0.1), "idle", 0),
             ("three", (0.7, -0.7, 0.45), "idle", 0), ("back", (-0.6, 0.8, 0.3), "idle", 0),
             ("walk", (1, 0, 0), "walk", 0.3), ("run", (1, 0, 0), "run", 0.25)]
    if spec["attack"]:
        shots.append(("attack", (1, 0, 0), "attack", 0.5))
        shots.append(("attack_front", (0.5, -1, 0.15), "attack", 0.5))
    for name, d, clip, t in shots:
        act = bpy.data.actions[clip]
        arm.animation_data.action = act
        scene.frame_set(int(round(act.frame_range[1] * t)))
        cam.location = mid + Vector(d).normalized() * 6
        cam.data.ortho_scale = size
        cam.rotation_euler = (mid - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base}_{name}.png"
        bpy.ops.render.render(write_still=True)
    # The LOD ladder side by side.
    arm.animation_data.action = bpy.data.actions["idle"]
    scene.frame_set(0)
    bpy.data.objects.remove(cam)


def build(spec, out_dir, preview, t0):
    clear_scene()
    kind = spec["kind"]
    arm = build_rig(spec)
    hi = build_body(spec, t0)
    objs = make_lods(spec, hi, arm, t0)
    bpy.data.objects.remove(hi)
    info = make_clips(spec, arm)
    problems = check(objs, arm, spec)
    for p in problems:
        print("PROBLEM", p)
    log(t0, f"{kind}: clips {info}")
    glb = os.path.join(out_dir, f"{kind}.glb")
    export_glb(glb, objs, arm)
    lod0 = [o for o in objs if o.name == f"{kind}_lod0"][0]
    zs = [v.co.z for v in lod0.data.vertices]
    ys = [v.co.y for v in lod0.data.vertices]
    meta = dict(kind=kind, height=round(max(zs), 3), length=round(max(ys) - min(ys), 3),
                lods=[sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if "antlers" not in o.name],
                clips=info)
    with open(os.path.join(out_dir, f"{kind}.json"), "w") as f:
        json.dump(meta, f, indent=1)
    log(t0, f"exported {glb} ({os.path.getsize(glb) / 1e6:.2f} MB)")
    if preview:
        os.makedirs(preview, exist_ok=True)
        render_preview(spec, objs, arm, os.path.join(preview, kind))
    return problems


def main():
    a = args()
    t0 = time.time()
    out_dir = os.path.abspath(a.get("out", "godot/assets/characters/animals"))
    kinds = a.get("kinds", ",".join(animals.SPECIES)).split(",")
    preview = a.get("preview")
    problems = []
    for k in kinds:
        problems += build(animals.SPECIES[k], out_dir, preview, t0)
        if a.get("blend"):
            os.makedirs(os.path.dirname(os.path.abspath(a["blend"])), exist_ok=True)
            bpy.ops.wm.save_as_mainfile(filepath=a["blend"].replace(".blend", f"_{k}.blend"), compress=True)
    print("PROBLEMS:", len(problems))


if __name__ == "__main__":
    main()
