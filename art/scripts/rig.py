# The shared humanoid rig, built from Godot's own SkeletonProfileHumanoid (humanoid_profile.json,
# dumped from Godot 4.6 by dump_profile.gd). Our rig uses the profile's bone names, parents and
# rest *orientations* exactly (each bone's +Y points at its child, as in the profile), so Godot's
# retargeting maps it with no BoneMap guesswork; only the proportions (positions) are ours.
#
# Axes: Godot is Y up, a character faces +Z, its left is +X. Blender is Z up, faces -Y.
# The glTF exporter converts Blender -> Godot, so a Godot vector (x, y, z) is Blender (x, -z, y).

import json
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE = os.path.join(HERE, "humanoid_profile.json")

# Godot -> Blender for directions and positions.
G2B = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))

# Our proportions (Godot space, metres): a 1.75 m adult in a T-pose. Left side only; the
# right side mirrors x. Bones not listed (eyes, jaw, fingers) take the profile's offset from
# their parent, scaled by FINE_SCALE.
HEADS = {
    "Root": (0, 0, 0),
    "Hips": (0, 0.98, 0),
    "Spine": (0, 1.06, 0),
    "Chest": (0, 1.18, 0),
    "UpperChest": (0, 1.30, 0),
    "Neck": (0, 1.47, 0),
    "Head": (0, 1.56, 0),
    "LeftShoulder": (0.03, 1.43, 0),
    "LeftUpperArm": (0.18, 1.43, 0),
    "LeftLowerArm": (0.48, 1.43, 0),  # upper arm 0.30, forearm 0.28: an adult's reach (a rifle's foregrip)
    "LeftHand": (0.76, 1.43, 0),
    "LeftUpperLeg": (0.09, 0.95, 0),
    "LeftLowerLeg": (0.09, 0.53, 0),
    "LeftFoot": (0.09, 0.09, 0),
    "LeftToes": (0.09, 0.02, 0.13),
}
FINE_SCALE = 0.9
# Bone length when a bone has no child to reach (Blender bones need a tail).
END_LENGTH = {"Head": 0.19, "LeftHand": 0.1, "LeftToes": 0.07, "Root": 0.1}


def load_profile():
    with open(PROFILE, encoding="utf-8") as f:
        return json.load(f)


def _basis(b):
    m = b["basis"]  # axis vectors x, y, z
    return Matrix(((m[0], m[3], m[6]), (m[1], m[4], m[7]), (m[2], m[5], m[8])))


def profile_globals(profile):
    """Each bone's global rest (basis, origin) in the profile, Godot space."""
    out = {}
    for b in profile["bones"]:  # parents come before children
        basis, origin = _basis(b), Vector(b["origin"])
        p = b["parent"]
        if p:
            pb, po = out[p]
            basis, origin = pb @ basis, po + pb @ origin
        out[b["name"]] = (basis, origin)
    return out


def _mirror_name(name):
    if name.startswith("Right"):
        return "Left" + name[5:]
    return None


def our_heads(profile, globals_):
    heads = {}
    for b in profile["bones"]:
        n = b["name"]
        if n in HEADS:
            heads[n] = Vector(HEADS[n])
            continue
        left = _mirror_name(n)
        if left and left in HEADS:
            x, y, z = HEADS[left]
            heads[n] = Vector((-x, y, z))
            continue
        # Fine bones: the profile's offset from the parent, in the parent's global frame.
        pb, _ = globals_[b["parent"]]
        heads[n] = heads[b["parent"]] + pb @ (Vector(b["origin"]) * FINE_SCALE)
    return heads


def build_armature(name="Armature"):
    """Create the rig in edit mode from the profile. Returns the armature object."""
    profile = load_profile()
    g = profile_globals(profile)
    heads = our_heads(profile, g)
    children = {}
    for b in profile["bones"]:
        children.setdefault(b["parent"], []).append(b["name"])

    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    for b in profile["bones"]:
        n = b["name"]
        basis, _ = g[n]
        y_axis = basis.col[1].normalized()
        # Length: to the child the bone points at (the profile's tail), else a fixed end length.
        tail_child = b["tail"] or (children.get(n) or [None])[0]
        length = None
        if tail_child and tail_child in heads:
            length = (heads[tail_child] - heads[n]).dot(y_axis)
        if not length or length < 0.01:
            key = n if n in END_LENGTH else (_mirror_name(n) or "")
            length = END_LENGTH.get(key, 0.03)
        eb = arm.edit_bones.new(n)
        m = (G2B @ basis).to_4x4()
        m.translation = G2B @ heads[n]
        eb.head, eb.tail = (0, 0, 0), (0, length, 0)  # length first: matrix keeps it
        eb.matrix = m
        if b["parent"]:
            eb.parent = arm.edit_bones[b["parent"]]
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj
