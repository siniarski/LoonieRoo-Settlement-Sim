# Phase 0 test character: a segmented mannequin on the shared rig, with locomotion, crouch and lying clips,
# all made by script, so the pipeline (build -> validate -> export -> Godot) can be proven before
# any real art exists. Not a real character: rigid segments, joint spheres split between bones.
#
#   blender --background --factory-startup --python art/scripts/build_test_mannequin.py -- \
#       --blend art/blend/test_mannequin.blend --glb godot/assets/characters/test_mannequin.glb \
#       [--glb_merged godot/assets/characters/test_mannequin_merged.glb] [--render preview.png]
#       [--garments godot/assets/characters/garments.glb]   (and garments.json beside it)
#
# Clips are made in place (no root motion: the sim moves characters). They're described as
# world-space rotations per bone (relative to the T-pose rest), converted to each bone's local
# rotation, so the numbers read as "leg forward 25 degrees", not as quaternions.

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import garments  # noqa: E402
import rig  # noqa: E402
import validate  # noqa: E402

FPS = 30


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {}
    for i in range(0, len(a) - 1, 2):
        out[a[i].lstrip("-")] = a[i + 1]
    return out


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


# ---------------------------------------------------------------- mesh

from shapes import box as _box, cone as _cone, material as _material, sphere as _sphere  # noqa: E402


# Body regions (docs/characters.md, "The base body" and "Missing limbs"): each is its own mesh
# under the one skeleton, so clothing can hide what it covers and a lost limb hides everything
# below its cut. A cut shows a stump cap (stump_<joint>_<side>, hidden until a limb is lost).
# Cuts on the mannequin are at the joints: wrist, elbow, shoulder; ankle, knee, hip.
ARM_REGIONS = ("upperarm", "forearm", "hand")
LEG_REGIONS = ("thigh", "shin", "foot")


def build_mesh(arm_obj):
    """The mannequin as region meshes plus stump caps. Returns the objects."""
    bones = arm_obj.data.bones
    H = {b.name: arm_obj.matrix_world @ b.head_local for b in bones}
    T = {b.name: arm_obj.matrix_world @ b.tail_local for b in bones}
    regions = {}  # name -> (bmesh, [(verts, weights, tag)])

    def piece(region, make, weights, tag=None):
        bm, pieces = regions.setdefault(region, (bmesh.new(), []))
        pieces.append((make(bm), weights, tag))

    torso_flat = 0.62
    piece("torso", lambda bm: _cone(bm, H["Hips"] - Vector((0, 0, 0.08)), H["Spine"], 0.16, 0.15, flat=torso_flat), {"Hips": 1})
    piece("torso", lambda bm: _cone(bm, H["Spine"], H["Chest"], 0.15, 0.15, flat=torso_flat), {"Spine": 1})
    piece("torso", lambda bm: _cone(bm, H["Chest"], H["UpperChest"], 0.15, 0.17, flat=torso_flat), {"Chest": 1})
    piece("torso", lambda bm: _cone(bm, H["UpperChest"], H["Neck"], 0.18, 0.12, flat=torso_flat), {"UpperChest": 1})
    piece("head", lambda bm: _cone(bm, H["Neck"], H["Head"], 0.05, 0.05, segments=8), {"Neck": 1})
    head_c = H["Head"] + Vector((0, 0, 0.1))
    piece("head", lambda bm: _sphere(bm, head_c, 0.105, segs=12, rings=8), {"Head": 1})
    # A nose, so facing is obvious in every view (the character faces -Y in Blender).
    piece("head", lambda bm: _cone(bm, head_c + Vector((0, -0.09, 0)), head_c + Vector((0, -0.14, -0.01)), 0.025, 0.005, segments=6), {"Head": 1})
    # The face, for expressions (shape keys in face_keys): eyes, lids over them, brows, a mouth.
    for sx in (1, -1):
        eye = head_c + Vector((sx * 0.036, -0.094, 0.018))
        piece("head", lambda bm, e=eye: _sphere(bm, e, 0.017, segs=8, rings=6), {"Head": 1}, "eye")
        piece("head", lambda bm, e=eye: _sphere(bm, e + Vector((0, -0.004, 0.018)), 0.021, segs=8, rings=6, squash=(1, 1, 0.45)), {"Head": 1}, "lid")
        piece("head", lambda bm, sx=sx: _box(bm, head_c + Vector((sx * 0.036, -0.1, 0.05)), (0.032, 0.008, 0.007)), {"Head": 1}, "brow")
    piece("head", lambda bm: _box(bm, head_c + Vector((0, -0.102, -0.038)), (0.042, 0.008, 0.008)), {"Head": 1}, "mouth")

    for s in ("Left", "Right"):
        ua, la, hd = s + "UpperArm", s + "LowerArm", s + "Hand"
        ul, ll, ft, to = s + "UpperLeg", s + "LowerLeg", s + "Foot", s + "Toes"
        piece("torso", lambda bm, ua=ua: _sphere(bm, H[ua], 0.06), {s + "Shoulder": 0.5, ua: 0.5})
        piece("upperarm_" + s, lambda bm, ua=ua, la=la: _cone(bm, H[ua], H[la], 0.055, 0.045), {ua: 1})
        piece("upperarm_" + s, lambda bm, la=la: _sphere(bm, H[la], 0.047, segs=8), {ua: 0.5, la: 0.5})
        piece("forearm_" + s, lambda bm, la=la, hd=hd: _cone(bm, H[la], H[hd], 0.045, 0.034), {la: 1})
        hand_end = H[hd] + (T[hd] - H[hd]).normalized() * 0.17
        # Flattened top to bottom: palm down in the T-pose, as on a real rig (HandIK assumes it).
        piece("hand_" + s, lambda bm, hd=hd, e=hand_end: _cone(bm, H[hd], e, 0.045, 0.03, segments=8, flat=0.5, flat_axis="z"), {hd: 1})
        piece("torso", lambda bm, ul=ul: _sphere(bm, H[ul], 0.085), {"Hips": 0.5, ul: 0.5})
        piece("thigh_" + s, lambda bm, ul=ul, ll=ll: _cone(bm, H[ul], H[ll], 0.078, 0.056), {ul: 1})
        piece("thigh_" + s, lambda bm, ll=ll: _sphere(bm, H[ll], 0.058, segs=8), {ul: 0.5, ll: 0.5})
        piece("shin_" + s, lambda bm, ll=ll, ft=ft: _cone(bm, H[ll], H[ft], 0.055, 0.042), {ll: 1})
        piece("shin_" + s, lambda bm, ft=ft: _sphere(bm, H[ft], 0.045, segs=8), {ll: 0.5, ft: 0.5})
        piece("foot_" + s, lambda bm, ft=ft, to=to: _cone(bm, H[ft], H[to], 0.045, 0.04, segments=8), {ft: 1})
        toe_end = H[to] + Vector((0, -0.08, 0))
        piece("foot_" + s, lambda bm, to=to, e=toe_end: _cone(bm, H[to], e, 0.04, 0.03, segments=8), {to: 1})
        # Stump caps: a little bigger than the joint they close, on the bone that remains.
        piece("stump_shoulder_" + s, lambda bm, ua=ua: _sphere(bm, H[ua], 0.064, segs=10, rings=6), {s + "Shoulder": 1})
        piece("stump_elbow_" + s, lambda bm, la=la: _sphere(bm, H[la], 0.052, segs=8), {ua: 1})
        piece("stump_wrist_" + s, lambda bm, hd=hd: _sphere(bm, H[hd], 0.04, segs=8), {la: 1})
        piece("stump_hip_" + s, lambda bm, ul=ul: _sphere(bm, H[ul] + Vector((0, 0, -0.03)), 0.088), {"Hips": 1})
        piece("stump_knee_" + s, lambda bm, ll=ll: _sphere(bm, H[ll], 0.064, segs=8), {ul: 1})
        piece("stump_ankle_" + s, lambda bm, ft=ft: _sphere(bm, H[ft], 0.05, segs=8), {ll: 1})

    skin = _material("Mannequin", (0.55, 0.53, 0.5))
    stump = _material("Stump", (0.45, 0.08, 0.06))
    face = _material("Face", (0.08, 0.07, 0.07))
    objs = []
    for name, (bm, pieces) in regions.items():
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.verts.index_update()
        weights_of, tags = {}, {}
        for verts, weights, tag in pieces:
            for v in verts:
                weights_of[v.index] = weights
                if tag:
                    tags.setdefault(tag, set()).add(v.index)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = True
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        for vi, weights in weights_of.items():
            for bone, w in weights.items():
                g = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                g.add([vi], w, "REPLACE")
        obj.parent = arm_obj
        obj.modifiers.new("Armature", "ARMATURE").object = arm_obj
        me.materials.append(stump if name.startswith("stump_") else skin)
        if tags:
            me.materials.append(face)
            dark = tags.get("eye", set()) | tags.get("brow", set()) | tags.get("mouth", set())
            for p in me.polygons:
                if all(v in dark for v in p.vertices):
                    p.material_index = 1
            face_keys(obj, tags, head_c)
        objs.append(obj)
    return objs


def face_keys(obj, tags, centre):
    """Expression shape keys (Godot blend shapes of the same names), each a small offset of
    some face parts: the survivor states mix them (godot/Characters/LabBody.cs)."""
    me = obj.data
    obj.shape_key_add(name="Basis")
    co = [v.co.copy() for v in me.vertices]

    def key(name, move):
        sk = obj.shape_key_add(name=name, from_mix=False)
        for i, c in enumerate(co):
            d = move(i, c)
            if d is not None:
                sk.data[i].co = c + Vector(d)

    lids, brows, mouth = tags["lid"], tags["brow"], tags["mouth"]
    m_centre = sum((co[i] for i in mouth), Vector()) / len(mouth)
    key("brows_up", lambda i, c: (0, 0, 0.012) if i in brows else None)
    # Inner ends down and in (fear and pain draw the brows together).
    key("brows_frown", lambda i, c: (-0.004 * (1 if c.x > centre.x else -1), 0, -0.009) if i in brows and abs(c.x - centre.x) < 0.036 else None)
    # Inner ends up and together: worry, fear (the panicked face).
    key("brows_worry", lambda i, c: (-0.004 * (1 if c.x > centre.x else -1), 0, 0.009) if i in brows and abs(c.x - centre.x) < 0.036 else None)
    key("eyes_wide", lambda i, c: (0, 0, 0.009) if i in lids else None)
    key("eyes_heavy", lambda i, c: (0, -0.002, -0.017) if i in lids else None)
    key("mouth_open", lambda i, c: (0, 0, -0.024) if i in mouth and c.z < m_centre.z else None)
    key("mouth_frown", lambda i, c: (0, 0, -0.009) if i in mouth and abs(c.x - m_centre.x) > 0.015 else None)
    key("mouth_tight", lambda i, c: ((m_centre.x - c.x) * 0.4, 0, (m_centre.z - c.z) * 0.5) if i in mouth else None)


def merge_body(objs):
    """The far-LOD shape of the same character: every region joined into one mesh, no stumps."""
    for o in objs:
        if o.name.startswith("stump_"):
            bpy.data.objects.remove(o)
    keep = [o for o in bpy.data.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in keep:
        o.select_set(True)
    # The head goes first: joining keeps the active object's shape keys (the face).
    keep.sort(key=lambda o: o.name != "head")
    bpy.context.view_layer.objects.active = keep[0]
    bpy.ops.object.join()
    keep[0].name = keep[0].data.name = "body"
    return keep[0]


# ---------------------------------------------------------------- clips

def rx(deg):
    return Quaternion((1, 0, 0), math.radians(deg))


def ry(deg):
    return Quaternion((0, 1, 0), math.radians(deg))


def rz(deg):
    return Quaternion((0, 0, 1), math.radians(deg))


# Sign conventions (Blender world, character faces -Y):
#   rx(-a) swings a hanging limb forward by a; rx(+a) leans the upper body forward by a.
#   ry(+a) lowers the left arm from the T-pose by a; ry(-a) lowers the right.
#   pitch tips the whole body back about the hips (90 = lying on the back, -90 = prone).
#   leg_out spreads a leg sideways, leg_roll turns the knee outwards (both positive = out);
#   look_up tips the head back (to look ahead when prone); foot_follow 1 = the foot keeps its
#   angle to the thigh instead of staying flat to the floor (prone and crawling).
# A pose is a dict of these numbers (degrees, metres); missing ones take REST_POSE's.
REST_POSE = {
    "leg_Left": 0, "leg_Right": 0, "knee_Left": 2, "knee_Right": 2, "foot_Left": 0, "foot_Right": 0,
    "arm_Left": 0, "arm_Right": 0, "elbow": 10, "lower": 75, "lean": 0, "pitch": 0,
    "leg_out_Left": 0, "leg_out_Right": 0, "leg_roll_Left": 0, "leg_roll_Right": 0, "look_up": 0, "foot_follow": 0,
    "shrug": 0, "head_yaw": 0,  # shoulders up (deg), head turned (deg, positive to its left)
    "bob": 0, "back": 0,  # hips offset: down (bob, metres, negative = lower) and backwards (back)
}


def pose_world(p):
    """World rotations (relative to rest) for every animated bone, from pose numbers."""
    body = rx(-p["pitch"])
    upper = body @ rx(p["lean"])
    yaw = p["head_yaw"]
    w = {"Hips": body, "Spine": upper,
         "Neck": upper @ rz(0.35 * yaw) @ rx(-0.4 * p["look_up"]), "Head": upper @ rz(yaw) @ rx(-p["look_up"]),
         "LeftShoulder": upper @ ry(-p["shrug"]), "RightShoulder": upper @ ry(p["shrug"])}
    for s, sign in (("Left", 1), ("Right", -1)):
        swing, knee = p["leg_" + s], p["knee_" + s]
        thigh = body @ ry(-sign * p["leg_out_" + s]) @ rx(-swing) @ rz(sign * p["leg_roll_" + s])
        w[s + "UpperLeg"] = thigh
        w[s + "LowerLeg"] = thigh @ rx(knee)
        flat = body @ rx(-p["foot_" + s])  # flat on the body's floor
        follow = thigh @ rx(-p["foot_" + s])
        w[s + "Foot"] = flat.slerp(follow, p["foot_follow"])
        arm, lower, elbow = p["arm_" + s], p.get("lower_" + s, p["lower"]), p.get("elbow_" + s, p["elbow"])
        w[s + "UpperArm"] = upper @ rx(-arm) @ ry(sign * lower)
        w[s + "LowerArm"] = upper @ rx(-(arm + elbow)) @ ry(sign * lower)
    return w


def apply_pose(arm_obj, world):
    """Set each pose bone's local rotation so its world rotation (vs rest) is world[bone]."""
    pbs = arm_obj.pose.bones

    def wrot(name):
        while name:
            if name in world:
                return world[name]
            parent = pbs[name].parent
            name = parent.name if parent else None
        return Quaternion()

    for pb in pbs:
        if pb.name not in world:
            continue
        parent_w = wrot(pb.parent.name) if pb.parent else Quaternion()
        q_rel = parent_w.inverted() @ world[pb.name]  # rotation vs the parent, in world axes
        rest = pb.bone.matrix_local.to_3x3().to_quaternion()
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = rest.inverted() @ q_rel @ rest


def set_hips_offset(arm_obj, offset):
    pb = arm_obj.pose.bones["Hips"]
    rest = pb.bone.matrix_local.to_3x3()
    pb.location = rest.inverted() @ Vector(offset)


# Only the bones the pose numbers move get tracks (rotation; Hips also location): 15 tracks per
# clip, not 112. Every track costs per character per frame (the anim bench: docs/characters.md).
ANIMATED = sorted(pose_world(REST_POSE))


def key_all(arm_obj, frame):
    for name in ANIMATED:
        pb = arm_obj.pose.bones[name]
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if name == "Hips":
            pb.keyframe_insert("location", frame=frame)


def make_clip(arm_obj, name, frames, steps, pose_at):
    """A clip sampled at steps+1 evenly spaced frames from pose_at(t), t in 0..1."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = act
    for i in range(steps + 1):
        t = i / steps
        p = {**REST_POSE, **pose_at(t)}
        for pb in arm_obj.pose.bones:
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = Quaternion()
            pb.location = (0, 0, 0)
        apply_pose(arm_obj, pose_world(p))
        set_hips_offset(arm_obj, (0, p["back"], p["bob"]))
        key_all(arm_obj, frames * t)
    return act


def blend(a, b, t):
    a, b = {**REST_POSE, **a}, {**REST_POSE, **b}
    return {k: a[k] + (b[k] - a[k]) * t for k in a}


def through(*keys):
    """A pose over t from (t, pose) keys, eased between them (a transition clip)."""
    def at(t):
        for (t0, p0), (t1, p1) in zip(keys, keys[1:]):
            if t <= t1:
                u = (t - t0) / (t1 - t0) if t1 > t0 else 1
                return blend(p0, p1, u * u * (3 - 2 * u))
        return {**REST_POSE, **keys[-1][1]}
    return at


# Crouch: thighs forward, shins back, feet flat, hips down; the numbers keep the feet near the floor.
CROUCH = {"leg_Left": 55, "leg_Right": 55, "knee_Left": 100, "knee_Right": 100, "lean": 28,
          "bob": -0.31, "lower": 70, "elbow": 35, "arm_Left": 15, "arm_Right": 15}
# Lying on the back, the body centred over the character's origin.
LYING = {"pitch": 90, "bob": -0.86, "back": 0.4, "lower": 82, "elbow": 5, "knee_Left": 4, "knee_Right": 12,
         "foot_Left": -40, "foot_Right": -40}
SITTING_BACK = {"pitch": 40, "bob": -0.72, "back": 0.2, "leg_Left": 55, "leg_Right": 50,
                "knee_Left": 75, "knee_Right": 85, "lean": 25, "lower": 70, "elbow": 30,
                "arm_Left": -30, "arm_Right": -30}


def gait(swing, knee, arm, elbow, lean, bob, base=None):
    """One locomotion cycle, t 0..1: the left leg passes under the body going forward at t=0.

    Every gait clip has the same length (one cycle) and the same phase, so the lab's blend space
    can mix them without the feet fighting; the cycle rate (cycles per second) is set at runtime
    from the speed.
    """
    base = {**REST_POSE, **(base or {})}
    standing = base["knee_Left"] < 50

    def at(t):
        c = 2 * math.pi * t
        s = math.sin(c)
        p = dict(base)
        p.update({
            "leg_Left": base["leg_Left"] + swing * s, "leg_Right": base["leg_Right"] - swing * s,
            "knee_Left": base["knee_Left"] + 4 + knee * max(0.0, math.cos(c)),
            "knee_Right": base["knee_Right"] + 4 + knee * max(0.0, -math.cos(c)),
            "arm_Left": base["arm_Left"] - arm * s, "arm_Right": base["arm_Right"] + arm * s,
            "elbow": elbow, "lean": lean,
            "bob": base["bob"] + bob * (math.cos(2 * c) - 1) / 2,
        })
        if standing:  # toe-off on the back foot
            p["foot_Left"], p["foot_Right"] = 0.3 * swing * max(0.0, -s), 0.3 * swing * max(0.0, s)
        return p
    return at


def breathing(base, lean=1.5, arm=2, bob=0.004):
    base = {**REST_POSE, **base}

    def at(t):
        b = math.sin(2 * math.pi * t)
        p = dict(base)
        p.update({"lean": base["lean"] + lean * b, "arm_Left": base["arm_Left"] + arm * b,
                  "arm_Right": base["arm_Right"] + arm * b, "bob": base["bob"] - bob * (b + 1)})
        return p
    return at


# Prone: face down, chest up on the elbows, forearms flat ahead, head up to look forward.
PRONE = {"pitch": -90, "lean": -18, "bob": -0.85, "back": 0.1, "look_up": 50, "foot_follow": 1,
         "arm_Left": 112, "arm_Right": 112, "lower": 62, "elbow": 68, "knee_Left": 4, "knee_Right": 4,
         "leg_out_Left": 8, "leg_out_Right": 8, "leg_roll_Left": 15, "leg_roll_Right": 15}
# On hands and knees, on the way down to prone or up from it.
ALL_FOURS = {"pitch": -55, "lean": 20, "bob": -0.55, "back": 0.2, "look_up": 30, "foot_follow": 1,
             "leg_Left": 95, "leg_Right": 95, "knee_Left": 95, "knee_Right": 95,
             "arm_Left": 75, "arm_Right": 75, "lower": 80, "elbow": 0}


def army_crawl(t):
    """Same-side arm and knee reach together, then pull; the other side the half-cycle after."""
    s = math.sin(2 * math.pi * t)
    p = dict(PRONE)
    for side, r in (("Left", (1 + s) / 2), ("Right", (1 - s) / 2)):
        p["arm_" + side] = 100 + 45 * r
        p["elbow_" + side] = 75 - 55 * r  # the reaching forearm stays flat on the floor
        p["leg_out_" + side] = 8 + 32 * r
        p["leg_roll_" + side] = 15 + 60 * r
        p["knee_" + side] = 4 + 80 * r
    p["back"] = 0.1 + 0.02 * s
    return p


# A crawling zombie: legs dead weight, straight arms reaching and dragging, head up and lolling.
ZOMBIE_PRONE = {**PRONE, "lean": -10, "look_up": 35, "elbow": 15, "arm_Left": 150, "arm_Right": 150,
                "leg_out_Left": 1, "leg_out_Right": 5, "leg_roll_Left": 20, "leg_roll_Right": 5,
                "knee_Left": 8, "knee_Right": 15}


def zombie_crawl(t):
    s = math.sin(2 * math.pi * t)
    p = dict(ZOMBIE_PRONE)
    for side, r in (("Left", (1 + s) / 2), ("Right", (1 - s) / 2)):
        p["arm_" + side] = 95 + 75 * r  # reach far ahead, then haul back under the chest
    p["elbow"] = 25
    p["lean"] = -10 + 6 * abs(s)
    p["look_up"] = 35 + 8 * s
    return p


# Additive clips (posture and breathing: Godot's Add2 adds their difference from the rig's rest, the
# T-pose) start from the rest itself: no arms lowered, no knees bent. Only the spine, shoulders,
# head (and for the tired, knees and hips) move.
ADD = {**REST_POSE, "knee_Left": 0, "knee_Right": 0, "elbow": 0, "lower": 0}


def add(**kw):
    return {**ADD, **kw}


def breath(lean, shrug, look=0.0, bob=0.0):
    def at(t):
        b = math.sin(2 * math.pi * t)
        return add(lean=lean * b, shrug=shrug * b, look_up=look * b, bob=bob * b)
    return at


def shiver(t):
    """A fast irregular tremble: two frequencies against each other."""
    a = math.sin(2 * math.pi * t) + 0.6 * math.sin(2 * math.pi * 3 * t + 1.3)
    return add(shrug=1.5 * a, lean=0.6 * a, head_yaw=1.2 * math.sin(2 * math.pi * 2 * t))


# Locomotion clips are all one cycle long (CYCLE frames); godot/Characters/Lab.cs sets the cycle
# rate from the speed (cycles per second, its RATES tables) and the stride follows.
CYCLE = 30
CLIPS = [
    ("idle", CYCLE, 8, breathing({})),
    ("walk", CYCLE, 16, gait(25, 40, 18, 14, 2, 0.03)),       # at 0.94 cycles/s: 1.37 m/s
    ("jog", CYCLE, 16, gait(34, 60, 28, 60, 6, 0.045)),       # at 1.25 cycles/s: 2.4 m/s
    ("run", CYCLE, 16, gait(42, 80, 36, 85, 11, 0.06)),       # at 1.5 cycles/s: 3.5 m/s
    ("crouch_idle", CYCLE, 8, breathing(CROUCH, lean=1, arm=1, bob=0.003)),
    ("crouch_walk", CYCLE, 16, gait(18, 25, 10, 35, 28, 0.015, base=CROUCH)),  # at 0.9 cycles/s: 1.0 m/s
    ("lie_idle", CYCLE * 2, 8, breathing(LYING, lean=0.8, arm=0, bob=0.0)),
    ("lie_down", 45, 24, through((0, {}), (0.35, CROUCH), (0.7, SITTING_BACK), (1, LYING))),
    ("get_up", 50, 24, through((0, LYING), (0.3, SITTING_BACK), (0.65, CROUCH), (1, {}))),
    ("prone_idle", CYCLE * 2, 8, breathing(PRONE, lean=1, arm=1, bob=0.0)),
    ("prone_crawl", CYCLE, 16, army_crawl),                    # at 0.5 cycles/s: 0.4 m/s
    ("zombie_crawl", CYCLE, 16, zombie_crawl),                 # at 0.45 cycles/s: 0.35 m/s
    ("go_prone", 40, 24, through((0, {}), (0.3, CROUCH), (0.6, ALL_FOURS), (1, PRONE))),
    ("prone_up", 45, 24, through((0, PRONE), (0.4, ALL_FOURS), (0.7, CROUCH), (1, {}))),
    # Survivor states (docs/characters.md "States"): postures added over every clip...
    ("posture_panic", CYCLE, 1, lambda t: add(shrug=9, lean=-3, look_up=4)),
    ("posture_tired", CYCLE, 1, lambda t: add(lean=11, look_up=-16, shrug=-5, knee_Left=7, knee_Right=7, bob=-0.025)),
    ("posture_cold", CYCLE, 1, lambda t: add(shrug=10, lean=7, look_up=-6)),
    ("posture_startle", CYCLE, 1, lambda t: add(shrug=14, lean=-7, look_up=14)),
    # ...breathing and the panicked glance round, added and looping (their rate set at runtime)...
    ("breath_shallow", CYCLE, 8, breath(1.2, 2)),
    ("breath_heavy", CYCLE, 8, breath(3.5, 6, look=3, bob=0.008)),
    ("shiver", CYCLE, 16, shiver),
    ("scan", CYCLE * 4, 24, through((0, ADD), (0.15, add(head_yaw=30)), (0.32, add(head_yaw=24, look_up=4)),
                                    (0.55, add(head_yaw=-34)), (0.78, add(head_yaw=-26, look_up=-3)), (1, ADD))),
    # ...and arm holds that replace the clip's arms (blended in by amount): stiff and close
    # (panic, cold), hanging slack (tired), thrown up (startle).
    ("arms_tucked", CYCLE, 1, lambda t: {"arm_Left": 10, "arm_Right": 10, "lower": 72, "elbow": 62}),
    ("arms_hang", CYCLE, 1, lambda t: {"lower": 84, "elbow": 4}),
    ("arms_flinch", CYCLE, 1, lambda t: {"arm_Left": 35, "arm_Right": 35, "lower": 45, "elbow": 88}),
    # The arm of a lost forearm or hand, held in close: layered over any clip on that arm's bones.
    ("stump_hold_left", CYCLE, 1, lambda t: {"arm_Left": 20, "lower_Left": 68, "elbow_Left": 75}),
    ("stump_hold_right", CYCLE, 1, lambda t: {"arm_Right": 20, "lower_Right": 68, "elbow_Right": 75}),
]


def make_clips(arm_obj):
    for name, frames, steps, pose_at in CLIPS:
        make_clip(arm_obj, name, frames, steps, pose_at)
    arm_obj.animation_data.action = bpy.data.actions["idle"]


# ---------------------------------------------------------------- export, preview

def export_glb(path, objects, animations=True):
    """Export the rig and these meshes (only)."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects + [o for o in bpy.data.objects if o.type == "ARMATURE"]:
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, use_selection=True,
        export_animations=animations, export_animation_mode="ACTIONS",
        export_force_sampling=True, export_frame_range=False,
        # Drop channels that never change (fingers, scale, most translations): the exporter
        # otherwise keeps all 56 bones x 3 channels in every clip, and each costs at runtime.
        export_optimize_animation_size=True, export_optimize_animation_keep_anim_armature=False,
        export_skins=True, export_all_influences=False,
        export_def_bones=False, export_rest_position_armature=True,
        export_apply=False, export_cameras=False, export_lights=False)


def render_preview(path, arm_obj):
    """Front, side and a walk pose, workbench, for a quick look."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 3.0
    base, ext = os.path.splitext(path)
    target = Vector((0, 0, 0.88))
    shots = [("front", (0, -6, 0.88), "idle", 0), ("side", (6, 0, 0.88), "walk", 8), ("iso", (5, -5, 5.6), "run", 5),
             ("crouch", (6, 0, 0.88), "crouch_walk", 8), ("lying", (5, -5, 5.6), "lie_idle", 0),
             ("getup", (6, 0, 0.88), "get_up", 25), ("prone", (5, -5, 5.6), "prone_crawl", 8),
             ("prone_side", (6, 0, 0.5), "prone_idle", 0), ("zcrawl", (5, -5, 5.6), "zombie_crawl", 8),
             ("allfours", (6, 0, 0.88), "go_prone", 24)]
    for name, loc, clip, frame in shots:
        arm_obj.animation_data.action = bpy.data.actions[clip]
        scene.frame_set(frame)
        cam.location = loc
        cam.rotation_euler = (target - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base}_{name}{ext}"
        bpy.ops.render.render(write_still=True)
    arm_obj.animation_data.action = bpy.data.actions["idle"]
    scene.frame_set(0)
    bpy.data.objects.remove(cam)


def main():
    a = args()
    clear_scene()
    arm = rig.build_armature()
    objs = build_mesh(arm)
    clothes, meta = garments.build_garments(arm)
    for o in objs + clothes:
        garments.rest_uv(o)
    make_clips(arm)
    problems = validate.validate_scene(budget_tris=8000)
    for p in problems:
        print("VALIDATE:", p)
    body_tris = sum(len(p.vertices) - 2 for o in objs if not o.name.startswith("stump_") for p in o.data.polygons)
    print(f"tris: {body_tris} (+ stumps)  meshes: {len(objs)}  bones: {len(arm.data.bones)}")
    if problems:
        sys.exit(1)
    if "blend" in a:
        os.makedirs(os.path.dirname(os.path.abspath(a["blend"])), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a["blend"]))
    if "render" in a:
        render_preview(os.path.abspath(a["render"]), arm)
    print(f"garments: {len(meta)} in {len(clothes)} region pieces, {sum(len(p.vertices) - 2 for o in clothes for p in o.data.polygons)} tris in all")
    if "glb" in a:
        export_glb(os.path.abspath(a["glb"]), objs)
        print("exported", a["glb"])
    if "garments" in a:
        export_glb(os.path.abspath(a["garments"]), clothes, animations=False)
        garments.write_meta(meta, os.path.splitext(os.path.abspath(a["garments"]))[0] + ".json")
        print("exported", a["garments"])
    if "glb_merged" in a:
        for o in clothes:
            bpy.data.objects.remove(o)
        merge_body(objs)
        export_glb(os.path.abspath(a["glb_merged"]), [o for o in bpy.data.objects if o.type == "MESH"])
        print("exported", a["glb_merged"])


if __name__ == "__main__":
    main()
