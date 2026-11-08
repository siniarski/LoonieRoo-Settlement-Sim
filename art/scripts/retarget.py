# Retarget a third-party clip library onto our rig (docs/characters.md, "Where the clips come from"),
# baked in Blender so the result is on our exact skeleton, passes validate.py, and needs no import
# setup in Godot:
#
#   blender --background --factory-startup --python art/scripts/retarget.py -- \
#       --src <library.glb> --map art/scripts/bonemaps/ual.json --prefix ual \
#       --glb godot/assets/characters/clips_ual.glb [--blend art/blend/retarget_ual.blend]
#
# Libraries are downloads and stay out of git (CC0, but big and not ours to version); what's
# committed is this script, the bone map and the exported .glb (our rig, the merged mannequin as the
# skin Godot needs to make a Skeleton3D, and the clips), plus a .json beside it listing the clips.
#
# The transfer: each mapped bone takes the source bone's world rotation *relative to its rest*
# (both rigs rest in a T-pose, so "upper arm 40 degrees down" means the same on both), applied to our
# bone's rest, then swung to point along the source bone (where the rests differ, e.g. the shoulders,
# the rotation alone would carry the difference into the pose). Only Hips translates, placed so the
# point between our hip joints follows the source's, scaled by leg length. Unmapped bones (Root, eyes, jaw) stay at
# rest. Clips are resampled at FPS, one key per frame. Names become <prefix>_<lower_snake>, without
# a trailing "_loop" (the .json says which loop).

import fnmatch
import json
import os
import re
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_test_mannequin as btm  # noqa: E402
import garments  # noqa: E402
import rig  # noqa: E402
import validate  # noqa: E402

FPS = btm.FPS


def load_map(path):
    with open(path, encoding="utf-8") as f:
        m = json.load(f)
    bones = dict(m["bones"])
    for ours, theirs in m["bones"].items():  # mirror the left side
        if ours.startswith("Left"):
            bones["Right" + ours[4:]] = re.sub(r"\.L$", ".R", theirs).replace("Left", "Right")
    m["bones"] = bones
    return m


def clip_name(prefix, source):
    n = re.sub(r"_?Loop$", "", source)
    n = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", n).lower()
    n = re.sub(r"[^a-z0-9]+", "_", n).strip("_")
    return f"{prefix}_{n}"


def assign(obj, act):
    ad = obj.animation_data or obj.animation_data_create()
    ad.action = act
    if hasattr(ad, "action_slot") and act.slots and ad.action_slot is None:
        ad.action_slot = act.slots[0]


def import_source(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arms = [o for o in new if o.type == "ARMATURE"]
    if len(arms) != 1:
        sys.exit(f"retarget: expected one armature in {path}, found {len(arms)}")
    return arms[0], new


FEET_DST = ("LeftFoot", "RightFoot")


def low_foot(o, names, rest=False):
    """The lower ankle, world z (ankles, not toes: the rigs' toe bones differ in length)."""
    w = o.matrix_world
    return min((w @ (o.data.bones[n].head_local if rest else o.pose.bones[n].head)).z for n in names)


def bake(src, dst, bmap, src_act, name):
    """One clip: sample the source at every frame, key our rig. Returns (frames, seconds)."""
    assign(src, src_act)
    start, end = (int(round(f)) for f in src_act.frame_range)
    scene = bpy.context.scene
    sw, dw = src.matrix_world, dst.matrix_world
    src_rest = {b.name: (sw @ b.matrix_local).to_3x3().normalized() for b in src.data.bones}
    src_rest_inv = {n: m.inverted() for n, m in src_rest.items()}
    dst_rest = {b.name: (dw @ b.matrix_local) for b in dst.data.bones}
    order = [b.name for b in dst.data.bones]  # parents before children
    # The hips follow the point between the hip joints (not the hips bone's head: the two rigs put
    # it in different places relative to the hip joints, so a pelvis tilt would move the feet),
    # scaled by leg length (hip joint to ankle), so a bent knee lands the foot where it should.
    legs = ("LeftUpperLeg", "RightUpperLeg")
    feet = ("LeftFoot", "RightFoot")
    src_legs = [bmap["bones"][n] for n in legs]

    def mid(o, names, rest):
        w = o.matrix_world
        pts = [w @ (o.data.bones[n].head_local if rest else o.pose.bones[n].head) for n in names]
        return (pts[0] + pts[1]) / 2

    src_mid_rest = mid(src, src_legs, True)
    dst_mid_rest = mid(dst, legs, True)
    src_leg = src_mid_rest.z - mid(src, [bmap["bones"][n] for n in feet], True).z
    dst_leg = dst_mid_rest.z - mid(dst, feet, True).z
    scale = dst_leg / src_leg
    hips_to_mid = dst_mid_rest - dst_rest["Hips"].translation
    rest_gap = low_foot(dst, FEET_DST, True) - low_foot(src, [bmap["bones"][b] for b in FEET_DST], True) * scale

    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    assign(dst, act)
    drift = []
    prev = {}
    for f in range(start, end + 1):
        scene.frame_set(f)
        posed = {}  # our bones' world pose matrices this frame
        for n in order:
            b = dst.data.bones[n]
            pb = dst.pose.bones[n]
            parent_pose = posed[b.parent.name] if b.parent else dw
            parent_rest = dst_rest[b.parent.name] if b.parent else dw
            rest_rel = parent_rest.inverted() @ dst_rest[n]
            base = parent_pose @ rest_rel  # where the bone is with no rotation of its own
            theirs = bmap["bones"].get(n)
            if not theirs:
                posed[n] = base
                continue
            spb = src.pose.bones[theirs]
            world = (sw @ spb.matrix)
            delta = world.to_3x3().normalized() @ src_rest_inv[theirs]
            rot = delta @ dst_rest[n].to_3x3().normalized()
            # Then swing it (the least rotation) to point where the source bone points: where the
            # rests differ (the source's shoulders rest 31 deg back, ours straight out), the
            # rotation alone would carry the difference into the pose; the twist stays the rotation's.
            swing = rot.col[1].rotation_difference(world.to_3x3().normalized().col[1]).to_matrix()
            rot = swing @ rot
            want = rot.to_4x4()
            if n == "Hips":
                goal = dst_mid_rest + (mid(src, src_legs, False) - src_mid_rest) * scale
                want.translation = goal - delta @ hips_to_mid
            else:
                want.translation = base.translation
            local = base.inverted() @ want
            q = local.to_quaternion()
            if n in prev and prev[n].dot(q) < 0:
                q.negate()  # stay in one hemisphere, or the interpolation spins the long way
            prev[n] = q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f - start)
            if n == "Hips":
                pb.location = local.translation
                pb.keyframe_insert("location", frame=f - start)
            posed[n] = base @ local
        bpy.context.view_layer.update()
        drift.append(low_foot(dst, FEET_DST) - low_foot(src, [bmap["bones"][b] for b in FEET_DST]) * scale)
    # How far our lower ankle strays from the source's (scaled), less the rest difference:
    # sinking (negative) or floating (positive) that the retarget added, in cm.
    return end - start, (end - start) / FPS, (min(drift) - rest_gap) * 100, (max(drift) - rest_gap) * 100


def main():
    a = btm.args()
    for k in ("src", "map", "prefix", "glb"):
        if k not in a:
            sys.exit(f"retarget: --{k} is required")
    btm.clear_scene()
    bmap = load_map(a["map"])
    dst = rig.build_armature()
    body = btm.build_mesh(dst)
    for o in body:
        garments.rest_uv(o)  # as the mannequin build does (Godot wants UVs for tangents)
    src, src_objs = import_source(os.path.abspath(a["src"]))
    for pb in dst.pose.bones:
        pb.rotation_mode = "QUATERNION"
    missing = sorted({v for v in bmap["bones"].values() if v not in src.data.bones})
    if missing:
        sys.exit(f"retarget: source has no bones {missing}")

    clips = []
    source_actions = sorted((x for x in bpy.data.actions), key=lambda x: x.name)
    for act in source_actions:
        if any(fnmatch.fnmatch(act.name, p) for p in bmap.get("skip", [])):
            continue
        name = clip_name(a["prefix"], act.name)
        frames, seconds, sink, lift = bake(src, dst, bmap, act, name)
        clips.append({"name": name, "source": act.name, "frames": frames, "seconds": round(seconds, 3),
                      "loop": act.name.endswith("Loop")})
        print(f"clip {name:28s} <- {act.name:24s} {frames:4d} frames {seconds:5.2f} s  feet vs source {sink:+5.1f} .. {lift:+5.1f} cm")
    for pb in dst.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    assign(dst, bpy.data.actions[clips[0]["name"]])

    if "blend" in a:  # both rigs side by side, for a look in Blender (the source 1.2 m to our left)
        src.location.x = 1.2
        assign(src, bpy.data.actions[clips[0]["source"]])
        for act in source_actions:
            act.use_fake_user = True
        for o in body:
            o.hide_set(o.name.startswith("stump_"))
        os.makedirs(os.path.dirname(os.path.abspath(a["blend"])), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a["blend"]))
    for act in source_actions:
        bpy.data.actions.remove(act)
    for o in src_objs:
        bpy.data.objects.remove(o)

    btm.merge_body(body)
    problems = validate.validate_scene(budget_tris=8000)
    for p in problems:
        print("VALIDATE:", p)
    if problems:
        sys.exit(1)
    btm.export_glb(os.path.abspath(a["glb"]), [o for o in bpy.data.objects if o.type == "MESH"])
    meta = {"source": os.path.basename(a["src"]), "map": os.path.basename(a["map"]), "fps": FPS, "clips": clips}
    with open(os.path.splitext(os.path.abspath(a["glb"]))[0] + ".json", "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
    print(f"retarget: {len(clips)} clips -> {a['glb']}")


if __name__ == "__main__":
    main()
