# The animals (the sim's Animals.cs and Dogs.cs kinds): deer, rabbit, wolf, bear and dog, as data the
# build (build_animals.py) turns into low-poly models on a simple quadruped rig. Each species is:
#
#   spine   points on the midline, (forward, up) in metres: pelvis, mid, withers, neck, head, snout
#   jaw     (hinge, chin) for the ones that bite (wolf, bear, dog); the mouth is cut open along it
#   tail    two or three points down the tail
#   front, hind   per leg: how far out to the side, four joints (shoulder or hip, elbow or knee,
#           wrist or hock, toe) and a radius at each
#   masses  metaball masses over that frame (torso, haunches, head...), mirrored when off the midline
#   eyes, antlers   rigid pieces on the head
#   paint   vertex colours, a function of the rest position and the nearest bone
#   gaits   the clips' timing (cycle length, duty factor, leg swing), from which the speed each clip
#           was made at follows (the renderer scales playback to the sim's speed by it)
#
# Real sizes, 1 tile = 1 m: a white-tailed deer ~0.95 m at the shoulder, a cottontail ~0.4 m long,
# a grey wolf ~0.8 m at the shoulder, a black bear ~0.9 m, a medium dog ~0.55 m. Grounded and muted
# ("Zomboid-plus"): believable proportions, worn natural coats, nothing cartoon.
#
# Blender space: Z up, the animal faces -Y, its left is +X. V(side, forward, up) converts.

import math

from mathutils import Vector, noise


def V(side, fwd, up):
    return Vector((side, -fwd, up))


def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = min(1.0, max(0.0, t))
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def mottle(co, scale, amount):
    """A soft fur-like variation, -amount..amount."""
    return amount * noise.noise(co * scale)


def shade(c, k):
    return tuple(min(1.0, max(0.0, x * k)) for x in c)


# ---------------------------------------------------------------- the species

WOLF = dict(
    kind="wolf",
    lods=[5000, 1600, 500],
    res=0.012, voxel=0.009,
    spine=dict(pelvis=(-0.36, 0.66), mid=(-0.02, 0.67), withers=(0.27, 0.74), neck=(0.38, 0.73),
               head=(0.60, 0.86), snout=(0.85, 0.775)),
    jaw=((0.62, 0.805), (0.82, 0.748)),
    tail=[(-0.45, 0.66), (-0.60, 0.53), (-0.70, 0.34)],
    front=dict(side=0.075, pts=[(0.28, 0.57), (0.21, 0.39), (0.24, 0.11), (0.30, 0.025)], r=[0.07, 0.044, 0.027, 0.026]),
    hind=dict(side=0.085, pts=[(-0.34, 0.60), (-0.22, 0.41), (-0.40, 0.15), (-0.35, 0.025)], r=[0.085, 0.05, 0.026, 0.026]),
    masses=[
        ("e", 0, 0.16, 0.56, 0.13, 0.20, 0.165),      # chest, deep
        ("e", 0, -0.08, 0.60, 0.108, 0.19, 0.105),      # waist, tucked up
        ("e", 0, -0.30, 0.61, 0.105, 0.12, 0.115),     # haunch
        ("e", 0.08, -0.29, 0.52, 0.05, 0.10, 0.12),    # thigh
        ("e", 0.07, 0.25, 0.55, 0.05, 0.09, 0.13),     # shoulder
        ("e", 0, 0.25, 0.67, 0.085, 0.12, 0.08),       # withers
        ("t", 0, 0.33, 0.66, 0, 0.56, 0.82, 0.125, 0.09),  # neck and ruff
        ("e", 0, 0.42, 0.62, 0.10, 0.08, 0.12),        # throat ruff
        ("e", 0, 0.60, 0.865, 0.082, 0.09, 0.075),     # cranium
        ("e", 0.042, 0.63, 0.82, 0.042, 0.055, 0.045),   # cheek
        ("t", 0, 0.64, 0.83, 0, 0.845, 0.783, 0.05, 0.03),  # muzzle
        ("t", 0, 0.62, 0.785, 0, 0.815, 0.752, 0.034, 0.018),  # lower jaw
        ("n", 0, 0.70, 0.788, 0, 0.90, 0.763, 0.0075),  # the mouth's line
        ("e", 0.045, 0.565, 0.96, 0.016, 0.03, 0.056, -12),  # ear
        ("e", 0.075, 0.31, 0.025, 0.026, 0.04, 0.022),  # front paw
        ("e", 0.085, -0.34, 0.025, 0.026, 0.042, 0.022),  # hind paw
        ("t", 0, -0.44, 0.66, 0, -0.59, 0.53, 0.04, 0.058),  # tail, bushy
        ("t", 0, -0.59, 0.53, 0, -0.71, 0.33, 0.058, 0.028),
    ],
    eyes=[((0.037, 0.685, 0.872), 0.010)],
    nose=(0.86, 0.79),
    gaits=dict(
        walk=dict(cycle=0.9, duty=0.65, swing=24, lift=1.0, bob=0.012),
        run=dict(cycle=0.6, duty=0.40, swing=32, lift=1.2, bob=0.03, gait="canter"),
    ),
    attack="lunge",
)

DEER = dict(
    kind="deer",
    lods=[5000, 1600, 500],
    res=0.013, voxel=0.009,
    spine=dict(pelvis=(-0.42, 0.90), mid=(-0.05, 0.88), withers=(0.28, 0.96), neck=(0.38, 0.98),
               head=(0.56, 1.30), snout=(0.76, 1.21)),
    jaw=None,
    tail=[(-0.52, 0.89), (-0.60, 0.80)],
    front=dict(side=0.07, pts=[(0.30, 0.78), (0.24, 0.56), (0.27, 0.30), (0.30, 0.03)], r=[0.062, 0.034, 0.019, 0.017]),
    hind=dict(side=0.08, pts=[(-0.40, 0.83), (-0.28, 0.58), (-0.46, 0.36), (-0.42, 0.03)], r=[0.075, 0.040, 0.019, 0.017]),
    masses=[
        ("e", 0, 0.20, 0.79, 0.125, 0.20, 0.17),       # chest
        ("e", 0, -0.10, 0.80, 0.135, 0.24, 0.15),      # belly
        ("e", 0, -0.36, 0.84, 0.115, 0.13, 0.13),      # haunch
        ("e", 0.075, -0.34, 0.75, 0.05, 0.10, 0.14),   # thigh
        ("e", 0.068, 0.27, 0.76, 0.05, 0.09, 0.13),    # shoulder
        ("t", 0, 0.26, 0.88, 0, 0.53, 1.25, 0.11, 0.06),  # neck
        ("e", 0, 0.56, 1.305, 0.06, 0.078, 0.062),    # cranium
        ("t", 0, 0.59, 1.28, 0, 0.765, 1.215, 0.047, 0.03),  # muzzle
        ("e", 0.075, 0.535, 1.38, 0.034, 0.012, 0.062, 0),  # ear, out to the side
        ("e", 0.07, 0.30, 0.03, 0.018, 0.028, 0.03),   # front hoof
        ("e", 0.08, -0.42, 0.03, 0.018, 0.028, 0.03),  # hind hoof
        ("e", 0, -0.53, 0.86, 0.04, 0.025, 0.06, 15),      # tail
    ],
    eyes=[((0.045, 0.61, 1.325), 0.012)],
    nose=(0.775, 1.225),
    # A buck's antlers: a main beam up, back and out, curving forward, with tines off it.
    antlers=[
        [(0.03, 0.57, 1.42), (0.07, 0.54, 1.52), (0.12, 0.53, 1.60), (0.15, 0.59, 1.67), (0.13, 0.69, 1.71)],
        [(0.10, 0.53, 1.575), (0.10, 0.56, 1.70)],
        [(0.143, 0.585, 1.665), (0.15, 0.61, 1.78)],
        [(0.06, 0.545, 1.50), (0.055, 0.60, 1.55)],
    ],
    gaits=dict(
        walk=dict(cycle=1.0, duty=0.65, swing=22, lift=1.0, bob=0.01),
        run=dict(cycle=0.6, duty=0.38, swing=28, lift=1.3, bob=0.05, gait="bound"),
    ),
    attack=None,
)

RABBIT = dict(
    kind="rabbit",
    lods=[1800, 600, 200],
    res=0.005, voxel=0.0038,
    spine=dict(pelvis=(-0.10, 0.13), mid=(-0.01, 0.155), withers=(0.07, 0.15), neck=(0.10, 0.15),
               head=(0.15, 0.205), snout=(0.21, 0.185)),
    jaw=None,
    tail=[(-0.14, 0.135), (-0.165, 0.15)],
    front=dict(side=0.028, pts=[(0.085, 0.10), (0.075, 0.06), (0.095, 0.022), (0.115, 0.008)], r=[0.018, 0.011, 0.008, 0.008]),
    hind=dict(side=0.042, pts=[(-0.075, 0.11), (-0.03, 0.065), (-0.105, 0.03), (-0.01, 0.01)], r=[0.032, 0.018, 0.012, 0.011]),
    masses=[
        ("e", 0, -0.04, 0.135, 0.062, 0.105, 0.072),   # body, hunched
        ("e", 0, 0.07, 0.12, 0.048, 0.055, 0.058),     # chest
        ("e", 0.035, -0.075, 0.095, 0.03, 0.06, 0.055),  # haunch
        ("e", 0, 0.15, 0.205, 0.034, 0.048, 0.038),    # head
        ("e", 0, 0.19, 0.188, 0.024, 0.03, 0.024),     # muzzle
        ("e", 0.017, 0.125, 0.29, 0.011, 0.02, 0.062, -18),  # ear, laid back a little
        ("e", 0.042, -0.06, 0.012, 0.014, 0.05, 0.01),  # hind foot, long and flat
        ("e", 0, -0.155, 0.15, 0.024, 0.022, 0.024),   # tail puff
    ],
    eyes=[((0.027, 0.168, 0.217), 0.0075)],
    nose=(0.215, 0.19),
    gaits=dict(
        walk=dict(cycle=0.5, duty=0.4, swing=25, lift=0.8, bob=0.015, gait="hop"),
        run=dict(cycle=0.3, duty=0.25, swing=45, lift=1.2, bob=0.04, gait="hop"),
    ),
    attack=None,
)

BEAR = dict(
    kind="bear",
    lods=[5000, 1600, 500],
    res=0.016, voxel=0.011,
    spine=dict(pelvis=(-0.45, 0.84), mid=(-0.05, 0.84), withers=(0.30, 0.92), neck=(0.42, 0.86),
               head=(0.62, 0.84), snout=(0.88, 0.765)),
    jaw=((0.66, 0.775), (0.84, 0.712)),
    tail=[(-0.58, 0.80), (-0.64, 0.75)],
    front=dict(side=0.13, pts=[(0.32, 0.66), (0.25, 0.42), (0.29, 0.13), (0.37, 0.04)], r=[0.11, 0.08, 0.064, 0.055]),
    hind=dict(side=0.14, pts=[(-0.42, 0.70), (-0.30, 0.44), (-0.45, 0.13), (-0.30, 0.04)], r=[0.11, 0.075, 0.055, 0.05]),
    masses=[
        ("e", 0, -0.05, 0.68, 0.23, 0.48, 0.25),       # body
        ("e", 0, 0.22, 0.63, 0.21, 0.22, 0.26),        # chest
        ("e", 0, 0.22, 0.86, 0.17, 0.20, 0.12),        # shoulder hump
        ("e", 0, -0.40, 0.73, 0.21, 0.18, 0.19),       # rump
        ("e", 0.13, -0.36, 0.58, 0.08, 0.13, 0.17),    # thigh
        ("e", 0.12, 0.30, 0.55, 0.08, 0.12, 0.17),     # shoulder
        ("t", 0, 0.36, 0.80, 0, 0.58, 0.82, 0.16, 0.125),  # neck
        ("e", 0, 0.61, 0.845, 0.13, 0.13, 0.115),    # head
        ("e", 0.065, 0.64, 0.81, 0.065, 0.065, 0.065),     # cheek
        ("t", 0, 0.68, 0.81, 0, 0.88, 0.772, 0.065, 0.042),  # muzzle
        ("t", 0, 0.66, 0.755, 0, 0.835, 0.72, 0.048, 0.028),  # lower jaw
        ("n", 0, 0.75, 0.765, 0, 0.93, 0.738, 0.009),  # the mouth's line
        ("e", 0.085, 0.58, 0.965, 0.038, 0.02, 0.04, -8),  # ear, round
        ("e", 0.13, 0.37, 0.04, 0.055, 0.07, 0.04),    # front paw
        ("e", 0.14, -0.33, 0.04, 0.055, 0.10, 0.04),   # hind paw, flat (plantigrade)
    ],
    eyes=[((0.055, 0.72, 0.875), 0.010)],
    nose=(0.89, 0.785),
    gaits=dict(
        walk=dict(cycle=1.2, duty=0.70, swing=20, lift=0.8, bob=0.02),
        run=dict(cycle=0.7, duty=0.40, swing=28, lift=1.1, bob=0.05, gait="canter"),
    ),
    attack="maul",
)


def scaled(spec, k, **over):
    """A species as another's frame scaled by k (the dog from the wolf)."""
    def s2(p):
        return (p[0] * k, p[1] * k)

    out = dict(spec)
    out["spine"] = {n: s2(p) for n, p in spec["spine"].items()}
    out["jaw"] = tuple(s2(p) for p in spec["jaw"]) if spec["jaw"] else None
    out["tail"] = [s2(p) for p in spec["tail"]]
    for leg in ("front", "hind"):
        L = spec[leg]
        out[leg] = dict(side=L["side"] * k, pts=[s2(p) for p in L["pts"]], r=[r * k for r in L["r"]])
    masses = []
    for m in spec["masses"]:
        if m[0] == "e":
            masses.append(("e", *[x * k for x in m[1:7]], *m[7:]))
        elif m[0] == "t":
            masses.append(("t", *[x * k for x in m[1:9]]))
        else:
            masses.append((m[0], *[x * k for x in m[1:8]]))
    out["masses"] = masses
    out["eyes"] = [(tuple(x * k for x in c), r * k) for c, r in spec["eyes"]]
    out["nose"] = s2(spec["nose"])
    out["res"], out["voxel"] = spec["res"] * k, spec["voxel"] * k
    out.update(over)
    return out


DOG = scaled(WOLF, 0.70, kind="dog")
# A mutt: a shorter muzzle, ears that fold over, a tail carried up, a slimmer ruff.
DOG["spine"]["snout"] = (0.56, 0.54)
DOG["jaw"] = ((0.43, 0.56), (0.555, 0.525))
DOG["tail"] = [(-0.315, 0.465), (-0.40, 0.50), (-0.45, 0.56)]
DOG["masses"] = [m for m in DOG["masses"] if not (m[0] == "e" and m[7:8] == (-12,))]  # wolf ears out
DOG["masses"] = [m for m in DOG["masses"] if not (m[0] == "t" and m[2] < -0.3)]       # wolf tail out
DOG["masses"] = [m for m in DOG["masses"] if not (m[0] in ("t", "n") and m[2] > 0.43 and m[3] < 0.6)]  # muzzle, jaw, mouth
DOG["masses"] = [m for m in DOG["masses"] if not (m[0] == "e" and abs(m[2] - 0.294) < 0.001)]  # throat ruff
DOG["masses"] += [
    ("t", 0, 0.44, 0.58, 0, 0.565, 0.548, 0.033, 0.022),   # muzzle
    ("t", 0, 0.43, 0.548, 0, 0.55, 0.527, 0.026, 0.015),   # lower jaw
    ("n", 0, 0.48, 0.551, 0, 0.62, 0.537, 0.0055),         # the mouth's line
    ("e", 0.05, 0.41, 0.63, 0.012, 0.03, 0.045, 25),       # ear, folded forward and down
    ("t", 0, -0.31, 0.465, 0, -0.40, 0.50, 0.024, 0.02),   # tail, carried up
    ("t", 0, -0.40, 0.50, 0, -0.45, 0.56, 0.02, 0.012),
]
DOG["eyes"] = [((0.027, 0.47, 0.61), 0.0075)]
DOG["nose"] = (0.568, 0.555)
DOG["gaits"] = dict(
    walk=dict(cycle=0.7, duty=0.62, swing=24, lift=1.0, bob=0.01),
    run=dict(cycle=0.42, duty=0.38, swing=32, lift=1.2, bob=0.03, gait="canter"),
)

SPECIES = {s["kind"]: s for s in (DEER, RABBIT, WOLF, BEAR, DOG)}


# ---------------------------------------------------------------- paint

def body_line(spec, fwd):
    """The height of the back's line at a forward position (pelvis -> mid -> withers)."""
    sp = spec["spine"]
    pts = sorted([sp["pelvis"], sp["mid"], sp["withers"]])
    if fwd <= pts[0][0]:
        return pts[0][1]
    for (f0, u0), (f1, u1) in zip(pts, pts[1:]):
        if fwd <= f1:
            return u0 + (u1 - u0) * (fwd - f0) / (f1 - f0)
    return pts[-1][1]


def paint(spec, co, bone):
    """Vertex colour (sRGB 0..1) at a rest position, given the nearest bone's name."""
    kind = spec["kind"]
    fwd, up, side = -co.y, co.z, abs(co.x)
    back = body_line(spec, fwd)
    sp = spec["spine"]
    hx, hu = sp["head"]
    nose_f, nose_u = spec["nose"]
    to_nose = (Vector((0, fwd, up)) - Vector((0, nose_f, nose_u))).length
    leg = bone.startswith(("Thigh", "Shin", "HindFoot", "UpperArm", "Forearm", "FrontFoot"))
    foot = bone.startswith(("HindFoot", "FrontFoot"))
    head = bone in ("Head", "Jaw")
    tail = bone.startswith("Tail")
    size = spec["front"]["pts"][0][1]  # shoulder joint height: a scale for distances
    m = mottle(co, 9.0 / size, 0.10) + mottle(co, 30.0 / size, 0.05)

    if kind in ("wolf", "dog"):
        if kind == "wolf":
            coat, pale, dark = (0.42, 0.39, 0.35), (0.70, 0.66, 0.58), (0.17, 0.15, 0.14)
        else:
            coat, pale, dark = (0.36, 0.24, 0.15), (0.62, 0.50, 0.36), (0.14, 0.10, 0.08)
        c = coat
        # A darker saddle along the back, pale below (belly, inside the legs, throat, cheeks).
        c = mix(c, dark, smooth01((up - (back - 0.06 * size)) / (0.10 * size)) * 0.55)
        below = smooth01(((back - 0.22 * size) - up) / (0.12 * size))
        c = mix(c, pale, below * (0.9 if not leg else 0.5))
        if leg:
            c = mix(c, pale, 0.45 * smooth01((0.35 * size - up) / (0.3 * size)))
        if head:
            c = mix(c, pale, smooth01((hu - 0.02 * size - up) / (0.05 * size)) * 0.8)  # cheeks and lower jaw
            c = mix(c, dark, smooth01((up - (hu + 0.06 * size)) / (0.04 * size)) * 0.7)  # ear backs and crown
        if tail:
            c = mix(c, dark, smooth01((-fwd - (-spec["tail"][-1][0] - 0.12 * size)) / (0.06 * size)))
        if foot:
            c = shade(c, 0.85)
        c = mix(c, (0.05, 0.045, 0.045), smooth01((0.05 * size - to_nose) / (0.02 * size)))  # nose
    elif kind == "deer":
        coat, pale, dark = (0.39, 0.30, 0.22), (0.74, 0.71, 0.65), (0.08, 0.07, 0.06)
        c = coat
        c = mix(c, shade(coat, 0.8), smooth01((up - (back - 0.04)) / 0.06) * 0.5)
        c = mix(c, pale, smooth01(((back - 0.20) - up) / 0.06) * (0.9 if not leg else 0.25))
        if bone.startswith("Tail"):
            c = mix(c, pale, smooth01((0.86 - up) / 0.05))  # white under the tail
        if head:
            c = mix(c, pale, smooth01((hu - 0.07 - up) / 0.03) * 0.8)  # the throat patch and chin
            ring = abs((Vector((0, fwd, up)) - Vector((0, nose_f - 0.06, nose_u + 0.01))).length - 0.035)
            c = mix(c, pale, smooth01((0.012 - ring) / 0.008) * 0.6)   # a pale band behind the nose
        if foot:
            c = mix(c, dark, smooth01((0.06 - up) / 0.03))  # hooves
        c = mix(c, (0.05, 0.045, 0.045), smooth01((0.045 - to_nose) / 0.018))
    elif kind == "rabbit":
        coat, pale = (0.44, 0.38, 0.31), (0.78, 0.75, 0.70)
        c = coat
        c = mix(c, shade(coat, 0.82), smooth01((up - (back - 0.01)) / 0.02) * 0.6)
        c = mix(c, pale, smooth01(((back - 0.06) - up) / 0.03) * 0.8)
        if tail:
            c = pale
        if head and up > hu + 0.05:
            c = mix(c, (0.20, 0.17, 0.14), smooth01((up - 0.32) / 0.02))  # ear tips
        c = mix(c, (0.25, 0.20, 0.19), smooth01((0.012 - to_nose) / 0.006))
    else:  # bear
        coat, muzzle = (0.11, 0.09, 0.08), (0.45, 0.35, 0.25)
        c = coat
        if head:
            c = mix(c, muzzle, smooth01((fwd - (hx + 0.10)) / 0.05) * 0.9)
        c = mix(c, (0.05, 0.045, 0.045), smooth01((0.05 - to_nose) / 0.02))
        if foot:
            c = shade(c, 0.8)
    return tuple(min(1.0, max(0.0, x * (1 + m))) for x in c)
