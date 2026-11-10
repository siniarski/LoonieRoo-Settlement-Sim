# Derived clips (2026-11-10, the animation pass): new clips made from the retargeted library
# (godot/assets/characters/clips_ual.glb) and the mannequin's pose numbers (build_test_mannequin.py), baked in
# Blender so the game plays them on its AnimationPlayer at no extra cost per character:
#
#   blender --background --factory-startup --python art/scripts/derive_clips.py -- \
#       --src godot/assets/characters/clips_ual.glb --glb godot/assets/characters/clips_more.glb [--only walk,jog]
#
# Writes clips_more.glb (our rig, the merged mannequin as the skin Godot needs, the clips) and clips_more.json
# beside it: each clip's length, whether it loops, and for locomotion its ground speed (m/s at rate 1, measured
# here from the planted foot or hand) and its stride.
#
# The tools (all in world space on our rig, then turned back into each bone's local rotation):
#   stride warping  a gait's planted foot travels back under the hips at the clip's speed; scaling every foot's
#                   forward offset from the hips by k makes the same cadence cover k times the ground. The legs
#                   are solved again by two-bone IK (the knee in the clip's own knee plane), the feet keep the
#                   clip's angle, and the hips drop where a longer reach needs it. So a slow walk, a brisk walk,
#                   an easy jog and a run come from the library's walk, jog and sprint with their feet still planted.
#   layers          extra world rotations on a bone and everything below it (a lean, a hunch, a lolling head, a
#                   flinch), arm poses relative to the chest, hands placed by arm IK (to the mouth, a sill).
#   from numbers    whole-body poses from build_test_mannequin's pose numbers (lying, prone, crouched), keyed
#                   through in time, with hands and feet put where they must be by IK (a crawl's planted hands).
# Each recipe below is one clip. Nothing here moves the root: the sim moves characters (validate.py).

import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_test_mannequin as btm  # noqa: E402
import garments  # noqa: E402
import rig  # noqa: E402
import validate  # noqa: E402

FPS = btm.FPS
UP, FWD, LEFT = Vector((0, 0, 1)), Vector((0, -1, 0)), Vector((1, 0, 0))  # Blender: Z up, the character faces -Y
SIDES = ("Left", "Right")


# ---------------------------------------------------------------- the rig and poses

class Rig:
    """Our armature: bone order (parents first), parents, rest matrices (world), bone lengths."""

    def __init__(self, obj):
        self.obj = obj
        w = obj.matrix_world
        self.order = [b.name for b in obj.data.bones]
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in obj.data.bones}
        self.rest = {b.name: w @ b.matrix_local for b in obj.data.bones}
        self.rest_rel = {n: (self.rest[p].inverted() @ self.rest[n]) if p else self.rest[n] for n, p in self.parent.items()}
        self.children = {n: [c for c in self.order if self.parent[c] == n] for n in self.order}
        self.length = {b.name: b.length for b in obj.data.bones}

    def head_rest(self, n):
        return self.rest[n].translation.copy()

    def descendants(self, n):
        out, todo = [], [n]
        while todo:
            x = todo.pop()
            out.append(x)
            todo.extend(self.children[x])
        return out


class Pose:
    """A pose as each bone's local matrix (rest-relative: the pose bone's matrix_basis), the hips also placed."""

    def __init__(self, rig_, local=None):
        self.rig = rig_
        self.local = local or {n: Matrix.Identity(4) for n in rig_.order}
        self._w = None

    def copy(self):
        return Pose(self.rig, {n: m.copy() for n, m in self.local.items()})

    def world(self):
        if self._w is None:
            r, w = self.rig, {}
            for n in r.order:
                p = r.parent[n]
                w[n] = (w[p] if p else Matrix.Identity(4)) @ r.rest_rel[n] @ self.local[n]
            self._w = w
        return self._w

    def dirty(self):
        self._w = None

    def head(self, n):
        return self.world()[n].translation.copy()

    def rot(self, n):
        return self.world()[n].to_quaternion()

    def set_world_rot(self, n, q):
        """Turn bone n so its world rotation is q (its children come along)."""
        r = self.rig
        p = r.parent[n]
        base = (self.world()[p] if p else Matrix.Identity(4)) @ r.rest_rel[n]
        want = q.to_matrix().to_4x4()
        want.translation = base.translation
        loc = self.local[n].translation.copy() if n == "Hips" else None
        m = base.inverted() @ want
        if loc is not None:
            m.translation = loc
        self.local[n] = m
        self.dirty()

    def turn(self, n, q):
        """A world rotation q added to bone n (about its head; its children come along)."""
        self.set_world_rot(n, q @ self.rot(n))

    def move_hips(self, offset):
        """The hips (and all the body) moved by a world offset."""
        r = self.rig
        base = r.rest_rel["Hips"] if not r.parent["Hips"] else self.world()[r.parent["Hips"]] @ r.rest_rel["Hips"]
        cur = self.world()["Hips"]
        want = cur.copy()
        want.translation = cur.translation + offset
        self.local["Hips"] = base.inverted() @ want
        self.dirty()

    def point(self, n, d, q0=None):
        """Swing bone n (least rotation from q0, else its current rotation) so its axis (+Y) points along d."""
        q = q0 if q0 is not None else self.rot(n)
        y = q @ Vector((0, 1, 0))
        self.set_world_rot(n, y.rotation_difference(d.normalized()) @ q)


def sample(r, src, act):
    """The source clip as our poses, one per frame (both ends): each bone's world rotation relative to its rest
    applied to ours (the library is on our rig, so this only undoes the importer's bone rolls); the hips placed."""
    assign(src, act)
    start, end = (int(round(f)) for f in act.frame_range)
    sw = src.matrix_world
    s_rest = {b.name: (sw @ b.matrix_local) for b in src.data.bones}
    out = []
    for f in range(start, end + 1):
        bpy.context.scene.frame_set(f)
        p = Pose(r)
        for n in r.order:
            if n not in src.pose.bones:
                continue
            ws = sw @ src.pose.bones[n].matrix
            delta = ws.to_3x3().normalized() @ s_rest[n].to_3x3().normalized().inverted()
            q = (delta @ r.rest[n].to_3x3().normalized()).to_quaternion()
            p.set_world_rot(n, q)
            if n == "Hips":
                p.move_hips(ws.translation - p.head("Hips"))
        out.append(p)
    return out


def foot_fix(r, walk):
    """The library's feet, retargeted, point ~30 deg further down than ours (its foot bone runs steeper from the
    ankle to its ball): standing flat, the ball of our foot sat ~5 cm under the ground. The turn (in each foot's own
    frame) that puts the ankle-to-ball line of the library's standing foot back on our rest's."""
    fix = {}
    for s in SIDES:
        # Mid-stance in the walk: the frames with the ankle lowest, the middle one (the foot flat on the ground).
        lo = min(p.head(s + "Foot").z for p in walk)
        flat = [i for i, p in enumerate(walk) if p.head(s + "Foot").z < lo + 0.003]
        idle = walk[flat[len(flat) // 2]]
        a, b = idle.head(s + "Foot"), idle.head(s + "Toes")
        ra, rb = r.head_rest(s + "Foot"), r.head_rest(s + "Toes")
        turn = (b - a).rotation_difference(rb - ra)
        q = idle.rot(s + "Foot")
        fix[s] = q.inverted() @ turn @ q
        print(f"  foot fix {s}: {math.degrees(turn.angle):.1f} deg")
    return fix


def fix_feet(p, fix):
    """Each foot turned by its fix, the toes kept as the clip has them (in the world: flat on the ground)."""
    for s in SIDES:
        toes = p.rot(s + "Toes")
        p.set_world_rot(s + "Foot", p.rot(s + "Foot") @ fix[s])
        p.set_world_rot(s + "Toes", toes)


def assign(obj, act):
    ad = obj.animation_data or obj.animation_data_create()
    ad.action = act
    if hasattr(ad, "action_slot") and act.slots and ad.action_slot is None:
        ad.action_slot = act.slots[0]


def key(r, name, frames):
    """Key the poses as a clip (one key a frame; only Hips translates)."""
    obj = r.obj
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    assign(obj, act)
    prev = {}
    for f, p in enumerate(frames):
        for n in r.order:
            pb = obj.pose.bones[n]
            m = p.local[n]
            q = m.to_quaternion()
            if n in prev and prev[n].dot(q) < 0:
                q.negate()
            prev[n] = q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if n == "Hips":
                pb.location = m.translation
                pb.keyframe_insert("location", frame=f)
    return act


# ---------------------------------------------------------------- IK

def leg_lengths(r, s):
    return ((r.head_rest(s + "LowerLeg") - r.head_rest(s + "UpperLeg")).length,
            (r.head_rest(s + "Foot") - r.head_rest(s + "LowerLeg")).length)


def arm_lengths(r, s):
    return ((r.head_rest(s + "LowerArm") - r.head_rest(s + "UpperArm")).length,
            (r.head_rest(s + "Hand") - r.head_rest(s + "LowerArm")).length)


def two_bone(p, upper, lower, end, target, pole, l1, l2, end_rot=None):
    """Bend upper and lower so end's head reaches target (as near as the limb allows), the middle joint
    towards pole (a direction); end keeps end_rot (world) if given, else its world rotation before."""
    keep = end_rot if end_rot is not None else p.rot(end)
    h = p.head(upper)
    d = target - h
    dist = max(1e-4, min(d.length, (l1 + l2) * 0.9995, ))
    dn = d.normalized()
    pv = pole - dn * pole.dot(dn)
    if pv.length < 1e-6:
        pv = FWD - dn * FWD.dot(dn)
    pn = pv.normalized()
    cos_a = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
    sin_a = math.sqrt(max(0.0, 1 - cos_a * cos_a))
    mid = h + dn * (l1 * cos_a) + pn * (l1 * sin_a)
    p.point(upper, mid - h)
    p.point(lower, h + dn * dist - mid)
    p.set_world_rot(end, keep)
    return (d.length - (l1 + l2)) if d.length > l1 + l2 else 0.0


def knee_dir(p, s):
    """The way the knee bends now (from the hip-ankle line towards the knee), or forward if the leg is straight."""
    h, k, a = p.head(s + "UpperLeg"), p.head(s + "LowerLeg"), p.head(s + "Foot")
    axis = (a - h).normalized()
    v = (k - h) - axis * (k - h).dot(axis)
    return v.normalized() if v.length > 0.01 else FWD.copy()


def place_foot(r, p, s, target, foot_rot=None, pole=None):
    l1, l2 = leg_lengths(r, s)
    return two_bone(p, s + "UpperLeg", s + "LowerLeg", s + "Foot", target, pole if pole is not None else knee_dir(p, s), l1, l2, foot_rot)


def place_hand(r, p, s, target, hand_rot=None, pole=None):
    """The wrist (the hand bone's head) to target; the elbow towards pole (default: down and out)."""
    l1, l2 = arm_lengths(r, s)
    out = LEFT if s == "Left" else -LEFT
    return two_bone(p, s + "UpperArm", s + "LowerArm", s + "Hand", target,
                    pole if pole is not None else (-UP * 0.8 + out * 0.6 + -FWD * 0.3), l1, l2, hand_rot)


# ---------------------------------------------------------------- measuring

ANKLE_UP, BALL_UP = 0.09, 0.02  # the rig's ankle and ball (the toes bone's head) over the sole (rig.py)


def contact(r, frames, bones=("LeftFoot", "RightFoot"), tips=("LeftToes", "RightToes"), ups=(ANKLE_UP, BALL_UP), band=0.025):
    """A gait's ground speed (m/s at rate 1) and stride (m a cycle), from the planted limb. Each frame a limb's
    contact point is the lower of its heel (the ankle's sole) and its ball; it's down within band of the ground. The speed is how far that point travels back over the frames it's down, over their time, all windows."""
    dist, time = 0.0, 0
    for b, t in zip(bones, tips):
        heel = [p.head(b) - Vector((0, 0, ups[0])) for p in frames]
        ball = [(p.head(t) - Vector((0, 0, ups[1]))) if t else h for p, h in zip(frames, heel)]
        lower = [0 if h.z <= c.z else 1 for h, c in zip(heel, ball)]
        pts = [(heel, ball)[w][i] for i, w in enumerate(lower)]
        lo = 0.0  # the ground (the rig's soles rest on z = 0), as AnimTest.cs measures the drawn feet
        for i in range(1, len(frames)):
            if pts[i].z < lo + band and pts[i - 1].z < lo + band:
                # The same point both frames; rolling from heel to ball, the ball's (it's the one that stays).
                which = lower[i]
                pair = (heel, ball)[which]
                dist += (pair[i] - pair[i - 1]).dot(-FWD)
                time += 1
    if time == 0:
        return 0.0, 0.0
    v = dist / time * FPS
    return v, v * (len(frames) - 1) / FPS


# ---------------------------------------------------------------- stride warping

def blur(xs, n=2):
    """A cyclic box blur, n each side, twice."""
    for _ in range(2):
        m = len(xs)
        xs = [sum(xs[(i + j) % m] for j in range(-n, n + 1)) / (2 * n + 1) for i in range(m)]
    return xs


def scale_rot(q_mean, q, k):
    """q's turn away from q_mean, times k (k < 1 calmer, > 1 bigger)."""
    d = q_mean.inverted() @ q
    axis, ang = d.to_axis_angle()
    if ang > math.pi:
        ang -= 2 * math.pi
    return q_mean @ Quaternion(axis, ang * k)


def mean_rot(qs):
    acc = Vector((0, 0, 0, 0))
    ref = qs[0]
    for q in qs:
        v = Vector(q)
        acc += v if q.dot(ref) >= 0 else -v
    return Quaternion(acc.normalized())


def scale_swing(frames, bones, k):
    """Each bone's local rotation away from its clip mean, times k (arms swinging less, or more)."""
    if k == 1:
        return
    for n in bones:
        qs = [p.local[n].to_quaternion() for p in frames]
        mq = mean_rot(qs)
        for p, q in zip(frames, qs):
            m = scale_rot(mq, q, k).to_matrix().to_4x4()
            m.translation = p.local[n].translation
            p.local[n] = m
            p.dirty()


def warp(r, frames, k=1.0, bob=1.0, lift=1.0, arm=1.0, spread=0.0, drop=0.0, lean=0.0, planted=True, forefoot=False):
    """Stride warping: each ankle's forward offset from the hips times k, its lift times `lift`, the hips' bob
    times `bob`, lowered by `drop` and where a longer reach needs it; the legs solved again; the arms' swing times
    `arm`; the feet `spread` further apart (m, each); the upper body leaned forward `lean` degrees; then planted."""
    frames = [p.copy() for p in frames]
    scale_swing(frames, [s + b for s in SIDES for b in ("UpperArm", "LowerArm")], arm)
    feet_floor = {s: min(p.head(s + "Foot").z for p in frames) for s in SIDES}
    hips_mean = sum(p.head("Hips").z for p in frames) / len(frames)
    # Where each ankle goes, and the knee planes and foot angles of the clip.
    goals, poles, rots = [], [], []
    for p in frames:
        c = p.head("Hips")
        g = {}
        for s in SIDES:
            a = p.head(s + "Foot")
            side = 1 if s == "Left" else -1
            ls = lift[s] if isinstance(lift, dict) else lift
            g[s] = Vector((a.x + side * spread, c.y + k * (a.y - c.y), feet_floor[s] + ls * (a.z - feet_floor[s])))
        goals.append(g)
        poles.append({s: knee_dir(p, s) for s in SIDES})
        rots.append({s: p.rot(s + "Foot") for s in SIDES})
    # The hips: bob scaled about its mean, lowered by `drop`.
    for p in frames:
        p.move_hips(Vector((0, 0, (hips_mean + bob * (p.head("Hips").z - hips_mean) - drop) - p.head("Hips").z)))
        if lean:
            p.turn("Spine", Quaternion(LEFT, math.radians(lean)))
    solve_legs(r, frames, goals, poles, rots, "warp")
    frames[-1] = frames[0].copy()  # both ends the same pose (the loop's seam)
    return plant(r, frames, forefoot=forefoot) if planted else frames


def solve_legs(r, frames, goals, poles, rots, what="", most_drop=1.0):
    """Each leg to its goal (ankle) by IK, the knee in its pole's plane, the foot at its rotation; the hips dropped
    first where a leg couldn't reach (smoothed over a few frames, as a body bobs)."""
    need = []
    for p, g in zip(frames, goals):
        n = 0.0
        for s in SIDES:
            l1, l2 = leg_lengths(r, s)
            lm = (l1 + l2) * 0.985
            d = g[s] - p.head(s + "UpperLeg")
            flat = d.x * d.x + d.y * d.y
            n = max(n, (-d.z - math.sqrt(lm * lm - flat)) if flat < lm * lm else -d.z)
        need.append(max(0.0, n))
    need = [min(most_drop, x) for x in blur([max(need[(i + j) % len(need)] for j in range(-2, 3)) for i in range(len(need))])]
    print(f"  {what} {NOW[0]}: hips dropped up to {max(need) * 100:.1f} cm")
    worst = 0.0
    for p, g, nd, pl, fr in zip(frames, goals, need, poles, rots):
        if nd > 0:
            p.move_hips(Vector((0, 0, -nd)))
        for s in SIDES:
            worst = max(worst, place_foot(r, p, s, g[s], fr[s], pl[s]))
    if worst > 0.005:
        print(f"  {what}: a leg short by {worst * 100:.1f} cm ({NOW[0]})")


NOW = [""]  # the clip being made (for messages)
HOLD_MOST = 0.08  # ...and moved across at most this far (m): a foot swinging in low is let land a little long
HOLD = 0.035  # a foot this far over the band (m) still isn't let move across: it lands and lifts straight


def plant(r, frames, band=0.025, ease=3, limbs=None, speed=None, forefoot=False):
    """Bake-time foot locking: in each spell a foot is down (its heel or ball sole within band of the ground), the
    point it stands on is pinned to the ground and travels back at the clip's speed (the median of how fast the
    planted points moved), the leg solved again to put it there; the pin eases in over `ease` frames before the
    spell and out over `ease` frames after it. A heel-to-ball roll carries on from where the ball is, so nothing jumps. Returns new
    frames; the loop's seam stays closed. `limbs` (default the feet): (end bone, tip bone, end up, tip up).
    `forefoot`: a run, on the balls of the feet: the ball is the point pinned all through (the heel's brief touch
    in a fast roll from heel to ball moved the foot ~13 cm between them)."""
    limbs = limbs or [(s + "Foot", s + "Toes", ANKLE_UP, BALL_UP) for s in SIDES]
    frames = [p.copy() for p in frames]
    n = len(frames) - 1
    pts = {}
    for end, tip, up_e, up_t in limbs:
        heel = [frames[i].head(end) - UP * up_e for i in range(n)]
        ball = [frames[i].head(tip) - UP * up_t for i in range(n)]
        which = [1 if forefoot else 0 if heel[i].z <= ball[i].z else 1 for i in range(n)]
        pts[end] = (heel, ball, which, [min(heel[i].z, ball[i].z) < band for i in range(n)],
                    [min(heel[i].z, ball[i].z) < band + HOLD for i in range(n)])
    if speed is None:
        v = []
        for heel, ball, which, down, _ in pts.values():  # the speed from the frames truly down
            for i in range(n):
                j = (i + 1) % n
                if down[i] and down[j] and which[i] == which[j]:
                    pair = (heel, ball)[which[i]]
                    v.append((pair[j] - pair[i]).dot(-FWD) * FPS)
        v = sorted(x for x in v if x > 0.05)  # a foot dragged forward along the ground isn't the ground's pace
        speed = v[len(v) // 2] if v else 0.0
    step = Vector((0, speed / FPS, 0))  # the ground's travel a frame, in place (back: +Y)
    shift = {end: [Vector((0, 0, 0))] * n for end in pts}
    for end, (heel, ball, which, on, down) in pts.items():
        if all(down) or not any(down):
            continue
        start = next(i for i in range(n) if down[i] and not down[i - 1])
        i = start
        for _ in range(n):
            if down[i] and not down[i - 1]:
                spell = []
                j = i
                while down[j % n] and len(spell) < n:
                    spell.append(j % n)
                    j += 1
                # Pinned from the first frame truly down (the landing), forward through the spell at the ground's
                # pace (re-based where the roll moves from heel to ball), and backward over the frames just before
                # it on the same line: the foot comes straight down onto the spot it stands on.
                ks = [k for k, f in enumerate(spell) if on[f]]
                if not ks:
                    i = (i + 1) % n
                    continue
                k0 = ks[0]
                ds = {}
                pin = None
                for k in range(k0, len(spell)):
                    f = spell[k]
                    cur = (heel, ball)[which[f]][f]
                    if pin is None:
                        pin = Vector((cur.x, cur.y, 0))
                    elif which[f] != which[spell[k - 1]]:
                        pin = Vector((cur.x, cur.y, 0)) + Vector((ds[k - 1].x, ds[k - 1].y, 0))
                    else:
                        pin = pin + step
                    ds[k] = pin - cur
                p0 = (heel, ball)[which[spell[k0]]][spell[k0]]
                for k in range(k0 - 1, -1, -1):
                    f = spell[k]
                    cur = (heel, ball)[which[spell[k0]]][f]
                    ds[k] = Vector((p0.x, p0.y, 0)) - step * (k0 - k) - cur
                for k, f in enumerate(spell):
                    d = ds[k]
                    if not on[f]:
                        # Just over the ground (landing, lifting): held across, a little at most, its height the clip's.
                        d.z *= max(0.0, 1 - ((heel, ball)[which[f]][f].z - band) / HOLD)
                        flat = Vector((d.x, d.y, 0))
                        if flat.length > HOLD_MOST:
                            d = flat * (HOLD_MOST / flat.length) + Vector((0, 0, d.z))
                    shift[end][f] = d
                first, last = shift[end][spell[0]], shift[end][spell[-1]]
                # Held the whole spell; eased in over the frames before it and out over the frames after (the
                # foot in the air by then), so it lands and lifts without a jump and doesn't scuff forward.
                for m in range(ease):
                    a, b = (spell[0] - 1 - m) % n, (spell[-1] + 1 + m) % n
                    if not down[a]:
                        shift[end][a] = shift[end][a] + first * ((ease - m) / (ease + 1))
                    if not down[b]:
                        shift[end][b] = shift[end][b] + last * ((ease - m) / (ease + 1))
            i = (i + 1) % n
    goals, poles, rots, toes = [], [], [], []
    for i in range(n):
        p = frames[i]
        g, rt = {}, {}
        for s in SIDES:
            a = p.head(s + "Foot") + shift.get(s + "Foot", [Vector()] * n)[i]
            q = p.rot(s + "Foot")
            if forefoot:
                # On the ball: the heel kept off the ground (the foot pitched about the ball as far as it needs).
                b = p.head(s + "Toes") + shift.get(s + "Foot", [Vector()] * n)[i]
                u = a - b
                want = ANKLE_UP - BALL_UP + 0.005
                if u.z < want and u.length > want:
                    axis = UP.cross(u).normalized()
                    th = math.asin(want / u.length) - math.asin(max(-1.0, min(1.0, u.z / u.length)))
                    turn = Quaternion(axis, th)
                    if (turn @ u).z < u.z:
                        turn = Quaternion(axis, -th)
                    a = b + turn @ u
                    q = turn @ q
            g[s], rt[s] = a, q
        goals.append(g)
        rots.append(rt)
        poles.append({s: knee_dir(p, s) for s in SIDES})
        toes.append({s: p.rot(s + "Toes") for s in SIDES})
    solve_legs(r, frames[:n], goals, poles, rots, "plant", most_drop=0.04)
    for p, t in zip(frames[:n], toes):
        for s in SIDES:
            p.set_world_rot(s + "Toes", t[s])
    frames[-1] = frames[0].copy()
    return frames


# ---------------------------------------------------------------- recipes

def clip_info(name, frames, loop, speed=None, extra=None):
    d = {"name": name, "frames": len(frames) - 1, "seconds": round((len(frames) - 1) / FPS, 3), "loop": loop}
    if speed is not None:
        d["speed"] = round(speed[0], 3)
        d["stride"] = round(speed[1], 3)
    if extra:
        d.update(extra)
    return d


def locomotion(r, lib, only):
    """The living's gaits: a slow walk, the walk, a brisk walk, an easy jog, a run; sneaking; a shuffle on the spot."""
    walk = lib("ual_walk")
    jog = lib("ual_jog_fwd")
    sprint = lib("ual_sprint")
    crouch = lib("ual_crouch_fwd")
    recipes = [
        # name, source, warp args
        ("walk_slow", walk, dict(k=0.72, bob=0.6, lift=0.8, arm=0.6)),
        ("walk", walk, dict(k=1.0)),
        ("walk_brisk", walk, dict(k=1.25, bob=1.1, lift=1.1, arm=1.2, lean=3)),
        ("jog", jog, dict(k=0.40, bob=0.6, lift=0.6, arm=0.75)),
        ("run", sprint, dict(k=0.52, bob=0.85, lift=0.8, arm=0.9, lean=-12, forefoot=True)),
        ("sneak", crouch, dict(k=1.25, lift=1.9, arm=0.8)),
        ("shuffle", walk, dict(k=0.0, bob=0.3, lift=0.35, arm=0.15)),
    ]
    out = []
    for name, src, kw in recipes:
        if only and name not in only:
            continue
        NOW[0] = name
        frames = warp(r, src, **kw)
        out.append((name, frames, True, contact(r, frames)))
    # Standing: the library's idles with the feet fixed (flat on the ground).
    for name, src in (("idle", "ual_idle"), ("crouch_idle", "ual_crouch_idle")):
        if not only or name in only:
            out.append((name, [p.copy() for p in lib(src)], True, None))
    return out


# ---------------------------------------------------------------- layers

def from_numbers(r, nums):
    """A pose from build_test_mannequin's pose numbers (its world rotations from the rest, its hips offset)."""
    full = {**btm.REST_POSE, **nums}
    w = btm.pose_world(full)
    p = Pose(r)
    for n in r.order:
        if n in w:
            p.set_world_rot(n, w[n] @ r.rest[n].to_quaternion())
    p.move_hips(Vector((0, full["back"], full["bob"])))
    return p


ARM_BONES = ("Shoulder", "UpperArm", "LowerArm", "Hand")


def arms_to(r, p, nums, w=1.0, sides=SIDES):
    """The arms put in a pose-number arm pose, relative to the chest as it is in p; w blends from p's own."""
    t = from_numbers(r, nums)
    turn = p.rot("UpperChest") @ t.rot("UpperChest").inverted()
    for s in sides:
        for b in ARM_BONES:
            n = s + b
            q0, q1 = p.rot(n), turn @ t.rot(n)
            if q0.dot(q1) < 0:
                q1.negate()  # the short way round
            p.set_world_rot(n, q0.slerp(q1, w))


def roll(deg):
    """A turn about the body's forward line (positive: the top to its left)."""
    return Quaternion(FWD, math.radians(-deg))


def pitch(deg):
    """A turn about the body's left-right line (positive: the top forward)."""
    return Quaternion(LEFT, math.radians(deg))


def yaw(deg):
    return Quaternion(UP, math.radians(deg))


def full_numbers(nums):
    """Pose numbers with the rest's defaults and the per-side arm numbers (lower_Left...) filled from the shared ones."""
    d = {**btm.REST_POSE, **nums}
    for k in ("lower", "elbow"):
        for side in SIDES:
            d.setdefault(f"{k}_{side}", d[k])
    return d


def through_numbers(r, frames, *keys):
    """A clip from pose-number keys (t, numbers), eased between them (smoothstep, as btm.through), one pose a frame."""
    keys = [(t, full_numbers(k)) for t, k in keys]
    return [from_numbers(r, keyed(keys, i / frames)) for i in range(frames + 1)]


# ---------------------------------------------------------------- the dead

# Arms hanging slack, and reaching for you (pose numbers: `lower` takes the arm down from the T-pose, `arm` swings
# it forward from there, `elbow` bends it).
HANG = {"arm_Left": 6, "arm_Right": 10, "lower": 82, "elbow": 14, "shrug": -4}
REACH = {"arm_Left": 72, "arm_Right": 60, "lower": 86, "elbow_Left": 22, "elbow_Right": 34}


def loll(p, t, i, amount=1.0, lean=0.0):
    """The head hanging and lolling (a slow roll and nod at phase t), the upper body leaned `lean` degrees."""
    if lean:
        p.turn("Chest", pitch(lean * 0.4))
        p.turn("UpperChest", pitch(lean * 0.3))
    c = 2 * math.pi * t
    p.turn("Neck", pitch(12 * amount))
    p.turn("Head", roll((9 + 5 * math.sin(c + i)) * amount) @ pitch(-10 * amount - 0.7 * lean + 4 * math.sin(2 * c)))


def dead(r, lib, only):
    """The dead's gaits (a few, picked per body by a hash: Gaits.cs), its idle, its run, the crawl, rising."""
    walk, jog, idle = lib("ual_walk"), lib("ual_jog_fwd"), lib("ual_idle")
    out = []

    def wanted(name):
        return not only or name in only

    def make(name, frames, loop=True, speed=True):
        out.append((name, frames, loop, contact(r, frames) if speed else None))

    def gait(name, src, kw, arms, arm_w, lean, lol=1.0, side=None):
        if not wanted(name):
            return
        NOW[0] = name
        frames = warp(r, src, **kw)
        n = len(frames) - 1
        for i, p in enumerate(frames):
            t = i / n
            arms_to(r, p, arms, arm_w, sides=SIDES if side is None else (side,))
            loll(p, t, 0.0 if side is None else 1.3, lol, lean)
        frames[-1] = frames[0].copy()
        make(name, frames)

    # The shamble: short of a walk, feet barely lifted (the right scuffs), leaning in, arms hanging, head lolling.
    gait("dead_shamble", walk, dict(k=1.05, bob=0.6, lift={"Left": 0.75, "Right": 0.6}, arm=1.0, lean=10, spread=0.03), HANG, 0.8, 8)
    # The lurch: shorter heavier steps, wide, bent forward, arms up and reaching.
    gait("dead_lurch", walk, dict(k=0.85, bob=1.4, lift=0.75, lean=18, spread=0.07, drop=0.03), REACH, 0.9, 10, 0.6)
    # A dragged leg (each side): it barely leaves the ground, the body leaning off it, that side's arm dead.
    for bad, good in (("Left", "Right"), ("Right", "Left")):
        gait(f"dead_drag_{bad[0].lower()}", walk, dict(k=1.0, bob=0.8, lift={bad: 0.45, good: 0.85}, lean=8, spread=0.02), HANG, 1.0, 6, 1.2, side=bad)
    # The runner: an easy jog bent double, arms out in front.
    gait("dead_run", jog, dict(k=0.45, bob=0.8, lift=0.7, arm=1.2, lean=20), REACH, 0.6, 6, 0.5)
    # Standing: the library's idle hunched, the arms slack, the head lolling.
    if wanted("dead_idle"):
        frames = [p.copy() for p in idle]
        n = len(frames) - 1
        for i, p in enumerate(frames):
            t = i / n
            p.turn("Spine", pitch(12) @ roll(3 * math.sin(2 * math.pi * t)))
            arms_to(r, p, HANG, 0.85)
            loll(p, t, 0.5)
        frames[-1] = frames[0].copy()
        make("dead_idle", frames, speed=False)
    if wanted("dead_crawl"):
        NOW[0] = "dead_crawl"
        make("dead_crawl", crawl(r), speed=False)
    # Lying where they fell (the dormant), and rising: on the back or face down.
    lie_back = {**btm.LYING, "head_yaw": 25, "lower": 70, "elbow": 20}
    lie_front = {"pitch": -90, "bob": -0.88, "back": 0.1, "foot_follow": 1, "look_up": 0, "head_yaw": 70,
                 "lower": 78, "arm_Left": 25, "arm_Right": -10, "elbow": 25, "leg_out_Left": 6, "leg_out_Right": 10,
                 "leg_roll_Left": 20, "leg_roll_Right": 10, "knee_Left": 6, "knee_Right": 25}
    stand = {"lean": 14, "look_up": 8, **HANG, "knee_Left": 8, "knee_Right": 8, "bob": -0.02}
    half = {**stand, "lean": 30, "knee_Left": 25, "knee_Right": 25, "bob": -0.1}
    sit = {**btm.SITTING_BACK, "arm_Left": 20, "arm_Right": -35, "lean": 35}
    kneel = {"pitch": -10, "lean": 30, "bob": -0.42, "back": 0.05, "leg_Left": 90, "knee_Left": 95,
             "leg_Right": 10, "knee_Right": 120, "foot_follow": 0.5, "arm_Left": 40, "arm_Right": 20, "lower": 80, "elbow": 20}
    if wanted("dead_lie_back"):
        make("dead_lie_back", through_numbers(r, 60, (0, lie_back), (1, lie_back)), speed=False)
    if wanted("dead_lie_front"):
        make("dead_lie_front", through_numbers(r, 60, (0, lie_front), (1, lie_front)), speed=False)
    if wanted("dead_rise_back"):
        make("dead_rise_back", through_numbers(r, 48, (0, lie_back), (0.3, sit), (0.62, kneel), (0.85, half), (1, stand)), loop=False, speed=False)
    if wanted("dead_rise_front"):
        make("dead_rise_front", through_numbers(r, 48, (0, lie_front), (0.3, btm.ALL_FOURS), (0.62, kneel), (0.85, half), (1, stand)), loop=False, speed=False)
    return out


CRAWL_SPEED, CRAWL_FRAMES = 1.0, 30  # m/s at rate 1, and the cycle (two pulls)


def crawl(r):
    """A crawler (the legs gone at the knee) dragging itself on its hands: face down, the chest up, each hand reaching
    ahead, planted on the ground and pulled back under the shoulder at the crawl's pace (so it doesn't slide), then
    lifted and reached forward again, the other half a cycle later; the shoulders roll with the pulls, the legs drag."""
    n = CRAWL_FRAMES
    T = n / FPS
    frames = []
    for i in range(n + 1):
        t = i / n
        p = from_numbers(r, {**btm.ZOMBIE_PRONE, "lean": -14, "look_up": 40, "bob": -0.80})
        c = 2 * math.pi * t
        p.turn("Spine", roll(7 * math.sin(c)))
        p.turn("Head", roll(-5 * math.sin(c)) @ pitch(3 * math.sin(2 * c)))
        for s, off in (("Left", 0.0), ("Right", 0.5)):
            u = (t + off) % 1.0
            sh = p.head(s + "UpperArm")
            out = 1 if s == "Left" else -1
            front, back = sh.y - 0.48, sh.y - 0.48 + CRAWL_SPEED * T / 2
            x = sh.x + out * 0.06
            if u < 0.5:  # planted, pulled back at the ground's pace
                y, z = front + (back - front) * (u / 0.5), 0.045
            else:  # lifted and reached ahead
                v = (u - 0.5) / 0.5
                e = v * v * (3 - 2 * v)
                y, z = back + (front - back) * e, 0.045 + 0.14 * math.sin(math.pi * v)
            place_hand(r, p, s, Vector((x, y, z)), pole=Vector((out * 0.8, 0.3, 0.6)))
        frames.append(p)
    frames[-1] = frames[0].copy()
    return frames


# ---------------------------------------------------------------- reactions and actions

def keyed(keys, t):
    """Numbers at phase t from (t, {name: value}) keys, eased between (missing names are 0)."""
    names = set()
    for _, d in keys:
        names |= set(d)
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t <= t1:
            u = (t - t0) / (t1 - t0) if t1 > t0 else 1.0
            u = u * u * (3 - 2 * u)
            return {n: a.get(n, 0.0) + (b.get(n, 0.0) - a.get(n, 0.0)) * u for n in names}
    return {n: keys[-1][1].get(n, 0.0) for n in names}


BODY_TURNS = ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head")


def turns(p, k):
    """Extra turns from keyed numbers: <bone>_pitch, <bone>_roll, <bone>_yaw (degrees), parents first."""
    for b in BODY_TURNS:
        q = yaw(k.get(b + "_yaw", 0.0)) @ roll(k.get(b + "_roll", 0.0)) @ pitch(k.get(b + "_pitch", 0.0))
        if q.angle > 1e-5:
            p.turn(b, q)


def hold_feet(r, frames, base):
    """The feet back where the base frames have them (after the hips moved or the body turned above them)."""
    goals = [{s: b.head(s + "Foot") for s in SIDES} for b in base]
    rots = [{s: b.rot(s + "Foot") for s in SIDES} for b in base]
    poles = [{s: knee_dir(b, s) for s in SIDES} for b in base]
    solve_legs(r, frames, goals, poles, rots, "hold", most_drop=0.0)


def reaction(r, base, frames, keys, arms=None):
    """A one-shot over `frames` frames of a looping base: keyed turns (turns()), the hips moved (hips_x/y/z, m),
    the arms blended to a pose by arm_w; the feet kept planted."""
    out = []
    src = [base[i % (len(base) - 1)].copy() for i in range(frames + 1)]
    for i, p0 in enumerate(src):
        t = i / frames
        k = keyed(keys, t)
        p = p0.copy()
        p.move_hips(Vector((k.get("hips_x", 0.0), k.get("hips_y", 0.0), k.get("hips_z", 0.0))))
        turns(p, k)
        if arms is not None and k.get("arm_w", 0.0) > 0:
            arms_to(r, p, arms, k["arm_w"])
        if abs(k.get("arms_pitch", 0.0)) > 1e-3:  # the arms kept on a target while the body pitches
            for side in SIDES:
                p.turn(side + "UpperArm", pitch(k["arms_pitch"]))
        out.append(p)
    hold_feet(r, out, src)
    return out


def hands_path(r, frames, path, sides=SIDES, pole=None):
    """Hands put by IK where path(p, side, t) says (a point, or None for the clip's own), each frame."""
    n = len(frames) - 1
    for i, p in enumerate(frames):
        for s in sides:
            at = path(p, s, i / n)
            if at is not None:
                place_hand(r, p, s, at, pole=pole)


FLINCH = {"arm_Left": 35, "arm_Right": 35, "lower": 45, "elbow": 88}


def reactions(r, lib, only):
    """Hits from four sides and the dead's lunge (on the dead's idle), a startle (on the living's), a fall face down,
    and the living's busy actions: eating, drinking, treating a wound, searching, foraging, cooking."""
    out = []

    def wanted(name):
        return not only or name in only

    def make(name, frames, loop=False):
        out.append((name, frames, loop, None))

    dead_idle = None
    for name, fr, _, _ in RESULTS:
        if name == "dead_idle":
            dead_idle = fr
    idle, crouch = lib("ual_idle"), lib("ual_crouch_idle")
    dead_base = dead_idle or idle
    # Struck (the sim's stagger, 0.6 s): the body thrown away from the blow, the head lagging, the arms flung,
    # the hips knocked a few cm, then an overshoot back and a settle. The side is where the blow came from.
    blows = {
        "front": {"Spine_pitch": -16, "Chest_pitch": -8, "Head_pitch": -24, "hips_y": 0.07},
        "back": {"Spine_pitch": 18, "Chest_pitch": 6, "Head_pitch": -18, "hips_y": -0.07},
        "left": {"Spine_roll": -14, "Chest_roll": -6, "Head_roll": 20, "Spine_yaw": 10, "hips_x": -0.06},
        "right": {"Spine_roll": 14, "Chest_roll": 6, "Head_roll": -20, "Spine_yaw": -10, "hips_x": 0.06},
    }
    for side, hit in blows.items():
        name = f"dead_hit_{side}"
        if not wanted(name):
            continue
        NOW[0] = name
        over = {k: -0.3 * v for k, v in hit.items()}
        make(name, reaction(r, dead_base, 18, [(0, {}), (0.14, {**hit, "arm_w": 0.55}), (0.42, {**over, "arm_w": 0.25}),
                                               (0.7, {k: 0.1 * v for k, v in hit.items()}), (1, {})], REACH))
    # The dead's lunge (from the windup's start, 1 s: the sim's attack every second, the bite 0.4 s in): arms up and
    # drawn back, then thrown forward with the body, the head craning in to bite, and back.
    if wanted("dead_lunge"):
        NOW[0] = "dead_lunge"
        make("dead_lunge", reaction(r, dead_base, 30, [
            (0, {}), (0.3, {"Spine_pitch": -4, "hips_y": 0.04, "arm_w": 0.9}), (0.4, {"Spine_pitch": -6, "hips_y": 0.05, "arm_w": 1.0}),
            (0.52, {"Spine_pitch": 26, "Chest_pitch": 10, "Head_pitch": -24, "hips_y": -0.10, "arm_w": 1.0, "arms_pitch": -40}),
            (0.62, {"Spine_pitch": 22, "Chest_pitch": 8, "Head_pitch": -18, "hips_y": -0.09, "arm_w": 0.9, "arms_pitch": -34}),
            (1, {})], {**REACH, "arm_Left": 88, "arm_Right": 84, "elbow_Left": 8, "elbow_Right": 12}))
    # Startled (the sim's startledAt, 1 s): shoulders and head jerk back, the forearms up to guard, held, let go.
    if wanted("flinch"):
        NOW[0] = "flinch"
        make("flinch", reaction(r, idle, 30, [(0, {}), (0.08, {"Spine_pitch": -9, "Head_pitch": -16, "hips_y": 0.03, "arm_w": 1.0}),
                                              (0.4, {"Spine_pitch": -5, "Head_pitch": -8, "hips_y": 0.02, "arm_w": 0.85}), (1, {})], FLINCH))
    # Falling face down (a blow from behind at the end): knees go, onto the hands, flat.
    if wanted("dead_fall_front"):
        stand = {"lean": 14, "look_up": 8, **HANG, "knee_Left": 8, "knee_Right": 8, "bob": -0.02}
        kneel = {"pitch": -10, "lean": 40, "bob": -0.42, "back": 0.05, "leg_Left": 90, "knee_Left": 95,
                 "leg_Right": 80, "knee_Right": 110, "foot_follow": 0.5, "arm_Left": 60, "arm_Right": 50, "lower": 85, "elbow": 10}
        flat = {"pitch": -90, "bob": -0.88, "back": 0.1, "foot_follow": 1, "head_yaw": 60, "lower": 70,
                "arm_Left": 60, "arm_Right": 20, "elbow": 30, "leg_out_Left": 8, "leg_out_Right": 4, "knee_Left": 10, "knee_Right": 20}
        make("dead_fall_front", through_numbers(r, 36, (0, stand), (0.35, kneel), (0.6, {**btm.ALL_FOURS, "arm_Left": 90, "arm_Right": 90}), (0.82, flat), (1, flat)))

    # The living's busy actions, looping while the sim's action runs.
    def mouth(p, out=0.0):
        h = p.head("Head")
        return h + FWD * 0.11 + UP * 0.02 + LEFT * out

    if wanted("eat"):
        # Bites: the right hand from the chest to the mouth and back, the head meeting it; the left holds the food low.
        fr = [idle[i % (len(idle) - 1)].copy() for i in range(61)]
        for i, p in enumerate(fr):
            t = i / 60
            p.turn("Head", pitch(8 + 6 * max(0.0, math.sin(2 * math.pi * (t * 2 - 0.1)))))

        def path(p, s, t):
            u = (t * 2) % 1.0
            chest = p.head("UpperChest") + FWD * 0.28 + UP * 0.02
            if s == "Left":
                return chest + LEFT * 0.06 - UP * 0.08
            m = mouth(p, -0.02)
            w = keyed([(0, {"w": 0}), (0.3, {"w": 1}), (0.55, {"w": 1}), (0.85, {"w": 0}), (1, {"w": 0})], u)["w"]
            return chest - LEFT * 0.04 + (m - chest + LEFT * 0.04) * w
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("eat", fr, True)
    if wanted("drink"):
        # Down on the haunches at the water: both hands cupped in it, up to the mouth, down again.
        fr = [crouch[i % (len(crouch) - 1)].copy() for i in range(61)]
        for i, p in enumerate(fr):
            u = (i / 60 * 1.5) % 1.0
            p.turn("Spine", pitch(18 * keyed([(0, {"a": 1}), (0.3, {"a": 1}), (0.6, {"a": 0}), (0.85, {"a": 0}), (1, {"a": 1})], u)["a"]))

        def path(p, s, t):
            u = (t * 1.5) % 1.0
            side = LEFT * (0.04 if s == "Left" else -0.04)
            low = Vector((p.head("Hips").x, p.head("Hips").y, 0.07)) + FWD * 0.42 + side
            m = mouth(p) + side - UP * 0.05
            w = keyed([(0, {"w": 0}), (0.3, {"w": 0}), (0.6, {"w": 1}), (0.85, {"w": 1}), (1, {"w": 0})], u)["w"]
            return low + (m - low) * w
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("drink", fr, True)
    if wanted("treat"):
        # Crouched over the left shin, both hands winding a bandage round it.
        fr = [crouch[i % (len(crouch) - 1)].copy() for i in range(61)]
        for p in fr:
            p.turn("Spine", pitch(16))
            p.turn("Head", pitch(18))

        def path(p, s, t):
            shin = (p.head("LeftLowerLeg") * 0.6 + p.head("LeftFoot") * 0.4)
            c = 2 * math.pi * (t * 2 + (0.5 if s == "Right" else 0))
            return shin + FWD * (0.07 + 0.05 * math.sin(c)) + LEFT * (0.06 * math.cos(c) + (0.05 if s == "Left" else -0.05)) + UP * 0.02
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("treat", fr, True)
    if wanted("search"):
        # Bent into a container at waist height, the hands rummaging in turn.
        fr = [idle[i % (len(idle) - 1)].copy() for i in range(61)]
        for i, p in enumerate(fr):
            p.turn("Spine", pitch(24 + 3 * math.sin(2 * math.pi * i / 60)))
            p.turn("Head", pitch(14))

        def path(p, s, t):
            c = 2 * math.pi * (t * 2 + (0.5 if s == "Right" else 0))
            base = Vector((0, 0, 0.78)) + FWD * 0.5 + LEFT * (0.12 if s == "Left" else -0.12)
            return base + FWD * 0.08 * math.sin(c) + UP * 0.06 * max(0.0, math.cos(c)) + LEFT * 0.04 * math.cos(c)
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("search", fr, True)
    if wanted("forage"):
        # Crouched, picking at the ground ahead with one hand then the other, the head following.
        fr = [crouch[i % (len(crouch) - 1)].copy() for i in range(61)]
        for i, p in enumerate(fr):
            p.turn("Spine", pitch(20))
            p.turn("Head", pitch(20) @ yaw(10 * math.sin(2 * math.pi * i / 60)))

        def path(p, s, t):
            u = (t * 2 + (0.5 if s == "Right" else 0)) % 1.0
            side = LEFT * (0.16 if s == "Left" else -0.16)
            ground = Vector((0, 0, 0.06)) + FWD * 0.48 + side
            knee = p.head(s + "LowerLeg") + FWD * 0.12 + UP * 0.05
            w = keyed([(0, {"w": 0}), (0.35, {"w": 1}), (0.55, {"w": 1}), (0.9, {"w": 0}), (1, {"w": 0})], u)["w"]
            return knee + (ground - knee) * w
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("forage", fr, True)
    if wanted("cook"):
        # On the haunches at a fire: the right hand stirring ahead, the left on its knee.
        fr = [crouch[i % (len(crouch) - 1)].copy() for i in range(61)]
        for p in fr:
            p.turn("Spine", pitch(12))
            p.turn("Head", pitch(16))

        def path(p, s, t):
            if s == "Left":
                return p.head("LeftLowerLeg") + UP * 0.06 + FWD * 0.02
            c = 2 * math.pi * t * 2
            return Vector((0, 0, 0.32)) + FWD * (0.50 + 0.07 * math.sin(c)) - LEFT * (0.05 + 0.07 * math.cos(c))
        hands_path(r, fr, path)
        fr[-1] = fr[0].copy()
        make("cook", fr, True)
    return out


RESULTS = []  # what the recipes made so far (a later recipe can build on an earlier one's clip)
RECIPES = [locomotion, dead, reactions]


# ---------------------------------------------------------------- leaps

# Arms for a jump: forearms up and forward a little, held in (the library's jump flings them wide and up).
JUMP_ARMS = {"arm_Left": 30, "arm_Right": 30, "lower": 72, "elbow": 70, "shrug": 4}


def leaps(r, lib, only):
    """The library's jump clips with the arms tamed (held in and forward, the clip's own swing kept a quarter), and a
    vault: a tuck with the legs swung to the left over the edge, the right hand going down for it (HandIK puts it
    there in the game), the body leaning over; both played by the leap's progress (People.Leap.cs)."""
    out = []
    for name, src in (("jump_start", "ual_jump_start"), ("jump_air", "ual_jump"), ("jump_land", "ual_jump_land")):
        if only and name not in only:
            continue
        fr = [p.copy() for p in lib(src)]
        for p in fr:
            arms_to(r, p, JUMP_ARMS, 0.75)

        def spread(frames):
            return (max((p.head("LeftHand") - p.head("RightHand")).length for p in frames),
                    max(max(p.head("LeftHand").z, p.head("RightHand").z) - p.head("Head").z for p in frames))
        (a0, h0), (a1, h1) = spread(lib(src)), spread(fr)
        print(f"  {name}: hands at most {a0:.2f} -> {a1:.2f} m apart, at most {h0:+.2f} -> {h1:+.2f} m over the head")
        out.append((name, fr, name == "jump_air", None))
    if not only or "vault" in only:
        stand = {"lower": 75, "elbow": 20}
        crouch = {"leg_Left": 40, "leg_Right": 40, "knee_Left": 70, "knee_Right": 70, "lean": 30, "bob": -0.2,
                  "arm_Left": 50, "arm_Right": 70, "lower": 80, "elbow": 20}
        # Over: the hips turned and tipped, both knees drawn up and swung out to the left, the right arm down to the
        # edge, the left out for balance; then the legs come down and through.
        tuck = {"pitch": -15, "lean": 35, "bob": 0.05, "leg_Left": 95, "leg_Right": 85, "knee_Left": 120, "knee_Right": 110,
                "leg_out_Left": 35, "leg_out_Right": 5, "leg_roll_Left": 30, "leg_roll_Right": 35, "foot_follow": 0.6,
                "arm_Right": 75, "lower_Right": 85, "elbow_Right": 5, "arm_Left": 20, "lower_Left": 30, "elbow_Left": 30, "head_yaw": -10}
        reach = {**tuck, "leg_Left": 60, "leg_Right": 75, "knee_Left": 60, "knee_Right": 80, "leg_out_Left": 15, "lean": 20, "pitch": -5}
        land = {"leg_Left": 35, "leg_Right": 20, "knee_Left": 55, "knee_Right": 40, "lean": 18, "bob": -0.12,
                "arm_Left": 25, "arm_Right": 30, "lower": 70, "elbow": 40}
        fr = through_numbers(r, 24, (0, stand), (0.2, crouch), (0.5, tuck), (0.75, reach), (1, land))
        n = len(fr) - 1
        for i, p in enumerate(fr):
            t = i / n
            k = keyed([(0, {"Hips_yaw": 0}), (0.5, {"Hips_yaw": 28}), (0.8, {"Hips_yaw": 12}), (1, {"Hips_yaw": 0})], t)
            turns(p, k)
        out.append(("vault", fr, False, None))
    return out


RECIPES.append(leaps)




def main():
    a = btm.args()
    btm.clear_scene()
    dst = rig.build_armature()
    body = btm.build_mesh(dst)
    for o in body:
        garments.rest_uv(o)
    for pb in dst.pose.bones:
        pb.rotation_mode = "QUATERNION"
    r = Rig(dst)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(a["src"]))
    src_objs = [o for o in bpy.data.objects if o not in before]
    src = next(o for o in src_objs if o.type == "ARMATURE")
    for t in (src.animation_data.nla_tracks if src.animation_data else []):
        t.mute = True
    cache = {}

    raw = {}

    def lib(name):
        if name not in cache:
            if "fix" not in raw:
                raw["fix"] = foot_fix(r, sample(r, src, bpy.data.actions["ual_walk"]))
            cache[name] = sample(r, src, bpy.data.actions[name])
            for p in cache[name]:
                fix_feet(p, raw["fix"])
        return cache[name]

    for name in (a["measure"].split(",") if "measure" in a else []):  # the library's own, for comparison
        v, stride = contact(r, lib(name))
        print(f"library {name:20s} {(len(lib(name)) - 1) / FPS:5.2f} s  {v:5.2f} m/s, stride {stride:.2f} m")
    only = set(a["only"].split(",")) if "only" in a else None
    src_actions = set(bpy.data.actions)
    clips = []
    for recipe in RECIPES:
        for name, frames, loop, speed in recipe(r, lib, only):
            RESULTS.append((name, frames, loop, speed))
            key(r, name, frames)
            info = clip_info(name, frames, loop, speed if speed and speed[0] else None)
            clips.append(info)
            sp = f" {info['speed']:5.2f} m/s, stride {info['stride']:.2f} m" if "speed" in info else ""
            print(f"clip {name:20s} {info['frames']:4d} frames {info['seconds']:5.2f} s{sp}")
    for act in src_actions:
        bpy.data.actions.remove(act)
    for o in src_objs:
        bpy.data.objects.remove(o)
    for pb in dst.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    assign(dst, bpy.data.actions[clips[0]["name"]])
    btm.merge_body(body)
    problems = validate.validate_scene(budget_tris=8000)
    for p in problems:
        print("VALIDATE:", p)
    if problems:
        sys.exit(1)
    btm.export_glb(os.path.abspath(a["glb"]), [o for o in bpy.data.objects if o.type == "MESH"])
    with open(os.path.splitext(os.path.abspath(a["glb"]))[0] + ".json", "w", encoding="utf-8") as f:
        json.dump({"from": os.path.basename(a["src"]), "fps": FPS, "clips": clips}, f, indent=2)
    print(f"derive_clips: {len(clips)} clips -> {a['glb']}")


if __name__ == "__main__":
    main()
