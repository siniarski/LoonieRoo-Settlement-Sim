# The asset validator: what every character file must satisfy before it's exported to Godot
# (docs/characters.md, "AI-assisted production"). Run on a .blend:
#
#   blender art/blend/x.blend --background --python art/scripts/validate.py
#
# or import it and call validate_scene() from a build script. Returns a list of problems
# (empty = good); the command-line form exits 1 when there are any.

import math
import os
import re
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402

MAX_INFLUENCES = 4
WEIGHT_SUM_TOLERANCE = 0.01
ANGLE_TOLERANCE_DEG = 1.0
CLIP_NAME = re.compile(r"^[a-z][a-z0-9_]*$")
# Bones that may carry translation keys (everything else is rotation only: no root motion,
# no stretching). Hips moves for bob and crouch; Root never moves (the sim moves characters).
MAY_TRANSLATE = {"Hips"}


def _is_identity(obj):
    return (obj.location.length < 1e-5 and max(abs(a) for a in obj.rotation_euler) < 1e-5
            and all(abs(s - 1) < 1e-5 for s in obj.scale))


def check_armature(arm, problems):
    profile = rig.load_profile()
    want = {b["name"]: b["parent"] for b in profile["bones"]}
    have = {b.name: (b.parent.name if b.parent else "") for b in arm.data.bones}
    missing = sorted(set(want) - set(have))
    extra = sorted(set(have) - set(want))
    if missing:
        problems.append(f"{arm.name}: missing profile bones {missing}")
    if extra:
        problems.append(f"{arm.name}: bones not in the humanoid profile {extra} (sockets are data, not bones)")
    for n, p in want.items():
        if n in have and have[n] != p:
            problems.append(f"{arm.name}: {n} parent is '{have[n]}', profile says '{p}'")
    # Rest orientations must be the profile's (positions may differ: proportions are ours).
    g = rig.profile_globals(profile)
    for b in arm.data.bones:
        if b.name not in g:
            continue
        expected = (rig.G2B @ g[b.name][0]).to_quaternion()
        actual = (arm.matrix_world.to_3x3() @ b.matrix_local.to_3x3()).to_quaternion()
        angle = math.degrees(expected.rotation_difference(actual).angle)
        angle = min(angle, 360 - angle)  # q and -q are the same rotation
        if angle > ANGLE_TOLERANCE_DEG:
            problems.append(f"{arm.name}: {b.name} rest is {angle:.1f} deg off the profile's orientation")
    if not _is_identity(arm):
        problems.append(f"{arm.name}: object transform not applied (location/rotation/scale)")
    for b in arm.data.bones:
        if not b.use_deform:
            problems.append(f"{arm.name}: {b.name} is not a deform bone (it would be dropped on export)")


def check_mesh(obj, arm, budget_tris, problems):
    bone_names = {b.name for b in arm.data.bones}
    mods = [m for m in obj.modifiers if m.type == "ARMATURE"]
    if len(mods) != 1 or mods[0].object != arm:
        problems.append(f"{obj.name}: needs exactly one Armature modifier on {arm.name}")
    if obj.parent != arm:
        problems.append(f"{obj.name}: parent should be {arm.name}")
    if not _is_identity(obj):
        problems.append(f"{obj.name}: object transform not applied")
    group_bone = {g.index: g.name for g in obj.vertex_groups}
    stray = sorted(n for n in group_bone.values() if n not in bone_names)
    if stray:
        problems.append(f"{obj.name}: vertex groups with no bone {stray}")
    unweighted = over = bad_sum = 0
    for v in obj.data.vertices:
        ws = [g.weight for g in v.groups if g.weight > 1e-6 and group_bone.get(g.group) in bone_names]
        if not ws:
            unweighted += 1
        elif len(ws) > MAX_INFLUENCES:
            over += 1
        elif abs(sum(ws) - 1) > WEIGHT_SUM_TOLERANCE:
            bad_sum += 1
    if unweighted:
        problems.append(f"{obj.name}: {unweighted} vertices with no bone weight")
    if over:
        problems.append(f"{obj.name}: {over} vertices with more than {MAX_INFLUENCES} influences")
    if bad_sum:
        problems.append(f"{obj.name}: {bad_sum} vertices whose weights don't sum to 1 (normalise)")
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if budget_tris and tris > budget_tris:
        problems.append(f"{obj.name}: {tris} triangles, over the {budget_tris} budget")
    if not obj.data.materials:
        problems.append(f"{obj.name}: no material")


def check_actions(problems):
    for act in bpy.data.actions:
        if not CLIP_NAME.match(act.name):
            problems.append(f"clip '{act.name}': names are lower_snake_case (set_verb_variant)")
        for fc in _fcurves(act):
            m = re.match(r'pose\.bones\["([^"]+)"\]\.(\w+)', fc.data_path)
            if not m:
                continue
            bone, prop = m.groups()
            if prop == "location" and bone not in MAY_TRANSLATE:
                if any(abs(k.co[1]) > 1e-6 for k in fc.keyframe_points):
                    problems.append(f"clip '{act.name}': {bone} has translation keys (only {sorted(MAY_TRANSLATE)} may)")
            if prop == "scale" and any(abs(k.co[1] - 1) > 1e-6 for k in fc.keyframe_points):
                problems.append(f"clip '{act.name}': {bone} is scaled")


def _fcurves(act):
    """F-curves of an action, layered (Blender 4.4+) or legacy."""
    if hasattr(act, "layers") and act.layers:
        for layer in act.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    yield from bag.fcurves
        return
    yield from getattr(act, "fcurves", [])


def validate_scene(budget_tris=8000):
    problems = []
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if len(arms) != 1:
        return [f"expected one armature, found {len(arms)}"]
    arm = arms[0]
    check_armature(arm, problems)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        problems.append("no meshes")
    for m in meshes:
        check_mesh(m, arm, budget_tris, problems)
    check_actions(problems)
    return problems


if __name__ == "__main__":
    found = validate_scene()
    for p in found:
        print("VALIDATE:", p)
    print("validate:", "ok" if not found else f"{len(found)} problem(s)")
    sys.exit(1 if found else 0)
