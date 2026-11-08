# Hair styles for the look-development characters (build_hair.py): each style its own mesh on the
# shared rig, built like the garments (clothes.py): a shell cut from the scalp where the hair grows,
# smoothed, stood off the skin by the style's thickness profile, plus pieces a shell can't make (a
# fringe's lip, long hair falling past the head, a beard on the jaw). Hair is painted grey: its colour is
# a tint per character in the game, so one mesh is every colour (docs/characters.md "Clothing, hair").
#
# Masks and thickness are numpy functions of the rest (T-pose) position, like paint.py; landmarks from
# sculpt.py. Blender space: Z up, the character faces -Y, its left is +X.

import math

import bmesh
import numpy as np
from mathutils import Vector

import sculpt as S
from paint import fbm, mix, rgb, sstep

EZ = S.EYE_L.z


def _parts(p):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    return x, y, z, np.abs(x)


def scalp(p, front=0.050, temple=0.0, nape=1.585, sideburn=True):
    """Where hair grows: above a hairline `front` over the eyes at the middle (curving back at the
    temples, recessed by `temple`), round above the ears, down to `nape` at the back."""
    x, y, z, ax = _parts(p)
    head = sstep(1.50, 1.52, z) * (ax < 0.11)
    # The front hairline over the forehead, rising towards the temples (more with a recession)...
    line = EZ + front + 0.010 * (ax / 0.06) ** 2 + temple * np.exp(-((ax - 0.040) / 0.014) ** 2)
    # ...and behind: above the ears at the sides, down to the nape at the back.
    back = nape + 0.070 * sstep(0.06, 0.0, y) * sstep(0.03, 0.06, ax)
    hairline = line + (back - line) * sstep(-0.045, -0.005, y)
    ears = sstep(0.064, 0.069, ax) * sstep(0.030, 0.025, np.abs(z - 1.630)) * sstep(0.020, 0.015, np.abs(y - 0.008))  # close round the ear
    m = head * sstep(hairline - 0.002, hairline + 0.003, z) * (1 - ears)
    if sideburn:  # down in front of each ear
        sb = sstep(0.010, 0.004, np.abs(y + 0.014)) * sstep(0.060, 0.066, ax) * sstep(1.600, 1.612, z) * sstep(1.675, 1.665, z)
        m = np.maximum(m, sb)
    return m


# ---------------------------------------------------------------- the styles

def buzz_mask(p):
    return scalp(p)


def buzz_lift(p):
    return np.full(len(p), 0.0025)


def crop_mask(p):
    return scalp(p, front=0.046)


def crop_lift(p):
    x, y, z, ax = _parts(p)
    top = sstep(1.68, 1.73, z)
    return 0.004 + 0.006 * top + 0.003 * sstep(-0.03, -0.07, y) * sstep(1.68, 1.70, z)  # a little lift at the front


def sidepart_mask(p):
    return scalp(p, front=0.044)


def sidepart_lift(p):
    """Swept from a part over the left eye: fuller on the right of it, the front lifted into a quiff."""
    x, y, z, ax = _parts(p)
    top = sstep(1.66, 1.72, z)
    part = 0.025
    side = sstep(part + 0.005, part - 0.03, x)  # the swept side (from the part across to the right)
    return 0.005 + top * (0.006 + 0.008 * side) + 0.006 * sstep(-0.03, -0.075, y) * sstep(1.68, 1.70, z) * side


def long_mask(p):
    return scalp(p, front=0.044, sideburn=False)


def long_lift(p):
    x, y, z, ax = _parts(p)
    return 0.007 + 0.006 * sstep(1.66, 1.72, z) + 0.004 * sstep(-0.01, 0.06, y)


def receding_mask(p):
    return scalp(p, front=0.060, temple=0.022)


def receding_lift(p):
    x, y, z, ax = _parts(p)
    crown = np.exp(-((x / 0.03) ** 2 + ((y - 0.03) / 0.035) ** 2)) * sstep(1.72, 1.745, z)
    return (0.0035 + 0.003 * sstep(1.66, 1.72, z)) * (1 - 0.7 * crown)  # thin at the crown


def beard_mask(p):
    """A short full beard: jaw, chin and cheeks below a line from the sideburn to the nose's wing, and a
    moustache; not the lips."""
    x, y, z, ax = _parts(p)
    face = (y < 0.02) * sstep(1.48, 1.50, z)
    cheek_line = 1.612 - 0.020 * sstep(0.02, 0.06, ax)
    lips = sstep(S.MOUTH_HALF * 1.05, S.MOUTH_HALF * 0.9, ax) * sstep(0.0085, 0.006, np.abs(z - S.MOUTH.z)) * (y < -0.06)
    mous = sstep(0.0, 0.002, z - (S.MOUTH.z + 0.006)) * sstep(S.SUBNASALE_Z - 0.001, S.SUBNASALE_Z - 0.004, z) * (ax < S.MOUTH_HALF * 1.1)
    under = sstep(1.50, 1.53, z) * sstep(0.065, 0.055, ax)
    jaw = sstep(cheek_line + 0.002, cheek_line - 0.002, z) * (ax < 0.072) * face
    return np.clip(np.maximum(jaw * (1 - lips), mous) * np.maximum(under, (z > 1.53).astype(float)), 0, 1)


def beard_lift(p):
    x, y, z, ax = _parts(p)
    return 0.004 + 0.002 * sstep(1.56, 1.52, z)


def _hang_long(bm):
    """Long hair falling past the head: the shell's edge behind the ears pulled down into a curtain to the
    shoulders, flaring out over the neck."""
    edges = [e for e in bm.edges if e.is_boundary and all(v.co.y > 0.012 and v.co.z < 1.66 for v in e.verts)]  # behind the ears only
    for k in range(4):
        ret = bmesh.ops.extrude_edge_only(bm, edges=edges)
        new_v = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMVert)]
        for v in new_v:
            r = Vector((v.co.x, v.co.y - 0.010, 0))
            out = r.normalized() if r.length > 1e-6 else Vector((0, 1, 0))
            v.co = v.co + Vector((0, 0, -0.040)) + out * (0.006 + 0.004 * k)
        edges = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary]


def _tapered_tube(bm, pts, radii, segs=10):
    """A tube along pts with a radius at each, capped at the start (in the head) and pinched at the end."""
    rings = []
    for i, c in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        up = d.cross(side)
        rings.append([bm.verts.new(c + (side * math.cos(2 * math.pi * k / segs) + up * math.sin(2 * math.pi * k / segs)) * radii[i])
                      for k in range(segs)])
    for ra, rb in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((ra[k], ra[(k + 1) % segs], rb[(k + 1) % segs], rb[k]))
    bm.faces.new(rings[0][::-1])


def _ponytail(bm):
    """Hair pulled back to a tie at the back of the head, the tail hanging down the back of the neck."""
    pts = [Vector((0, 0.098, 1.662)), Vector((0, 0.118, 1.652)), Vector((0, 0.128, 1.620)),
           Vector((0, 0.124, 1.575)), Vector((0, 0.110, 1.525)), Vector((0, 0.096, 1.480))]
    _tapered_tube(bm, pts, [0.014, 0.016, 0.019, 0.017, 0.012, 0.003])
    _tapered_tube(bm, [Vector((0, 0.104, 1.661)), Vector((0, 0.118, 1.655))], [0.0125, 0.0125], segs=10)  # the tie


def _no_extras(bm):
    pass


# ---------------------------------------------------------------- more styles: ponytail, matted (the dead's)

def ponytail_mask(p):
    return scalp(p, front=0.046, sideburn=False)


def ponytail_lift(p):
    x, y, z, ax = _parts(p)
    return 0.0028 + 0.0018 * sstep(1.66, 1.72, z)  # pulled tight back


def matted_mask(p):
    """The dead's: what's left, in clumps, with bare patches between."""
    keep = sstep(0.40, 0.46, fbm(p, 22, 3))
    return scalp(p, front=0.050) * keep


def matted_lift(p):
    x, y, z, ax = _parts(p)
    clumps = fbm(p * np.array([1.0, 1.0, 0.5]), 45, 3)
    return 0.003 + 0.007 * clumps + 0.003 * sstep(0.0, 0.08, y)  # longer and heavier at the back


# ---------------------------------------------------------------- the beanie (a hat), and hair under it

BRIM_Z = 1.660  # the beanie's lower edge at the front; it sits a little lower at the back


def beanie_mask(p):
    x, y, z, ax = _parts(p)
    edge = BRIM_Z - 0.030 * sstep(-0.02, 0.08, y)
    ears = sstep(0.064, 0.069, ax) * sstep(0.030, 0.025, np.abs(z - 1.630)) * sstep(0.020, 0.015, np.abs(y - 0.008))
    return sstep(edge - 0.002, edge + 0.002, z) * (1 - ears)


def beanie_lift(p):
    """Knit stands 6 mm off the head, the rolled brim 5 mm more."""
    x, y, z, ax = _parts(p)
    edge = BRIM_Z - 0.030 * sstep(-0.02, 0.08, y)
    return 0.006 + 0.005 * sstep(edge + 0.030, edge + 0.024, z)


def under_hat(p, lift):
    """A style worn under the beanie: flattened to 1.5 mm where the hat covers it; what shows below the
    brim (the sides and back, a long curtain, a tail) as it was."""
    x, y, z, ax = _parts(p)
    edge = BRIM_Z - 0.030 * sstep(-0.02, 0.08, y)
    covered = sstep(edge - 0.004, edge + 0.004, z)
    return lift * (1 - covered) + np.minimum(lift, 0.0015) * covered


STYLES = {
    "buzz": {"mask": buzz_mask, "lift": buzz_lift, "extras": _no_extras, "tris": 1500, "drape": 2},
    "crop": {"mask": crop_mask, "lift": crop_lift, "extras": _no_extras, "tris": 3000, "drape": 6},
    "sidepart": {"mask": sidepart_mask, "lift": sidepart_lift, "extras": _no_extras, "tris": 4000, "drape": 8},
    "long": {"mask": long_mask, "lift": long_lift, "extras": _hang_long, "tris": 6000, "drape": 8},
    "receding": {"mask": receding_mask, "lift": receding_lift, "extras": _no_extras, "tris": 3000, "drape": 6},
    "beard": {"mask": beard_mask, "lift": beard_lift, "extras": _no_extras, "tris": 2000, "drape": 3},
    "ponytail": {"mask": ponytail_mask, "lift": ponytail_lift, "extras": _ponytail, "tris": 3500, "drape": 6, "cards": True},
    "matted": {"mask": matted_mask, "lift": matted_lift, "extras": _no_extras, "tris": 3500, "drape": 4, "cards": True},
}
for _k in ("crop", "sidepart", "long", "receding"):
    STYLES[_k]["cards"] = True  # wispy strand cards along the front hairline
HATS = {"beanie": {"mask": beanie_mask, "lift": beanie_lift, "extras": _no_extras, "tris": 2500, "drape": 6}}


# ---------------------------------------------------------------- relief and colour (numpy)

def detail(style, p):
    """Strand relief for the high-poly shell: grooves running with the hair (down and back from the crown,
    swept on the sidepart), clumps."""
    x, y, z, ax = _parts(p)
    if style == "buzz":
        return (fbm(p, 400, 2) - 0.5) * 0.0004
    flow = np.stack([x * 6.0, y * 6.0, z * 0.8], axis=1)  # stretched along z: grooves run down the head
    if style == "sidepart":
        flow = np.stack([x * 0.9, y * 6.0, z * 6.0], axis=1)  # swept sideways
    grooves = (fbm(flow, 60, 3) - 0.5) * (0.0025 if style != "beard" else 0.0012)
    clumps = (fbm(p, 25, 2) - 0.5) * 0.002
    return grooves + clumps


def albedo(style, p, ao):
    """Grey (tinted per character in the game): strands lighter on the outside, darker at the roots and
    in the grooves, a soft sheen band."""
    x, y, z, ax = _parts(p)
    flow = np.stack([x * 6.0, y * 6.0, z * 0.8], axis=1)
    if style == "sidepart":
        flow = np.stack([x * 0.9, y * 6.0, z * 6.0], axis=1)
    if style == "beanie":  # knit: vertical ribs, a turned-up brim
        ribs = 0.5 + 0.5 * np.sin(np.arctan2(x, y) * 90)
        v = 0.55 + 0.25 * ribs + 0.1 * fbm(p, 200, 2)
        return np.clip(np.stack([v, v, v], axis=1) * (0.5 + 0.5 * ao)[:, None], 0, 1)
    strands = fbm(flow, 220, 3)
    v = 0.55 + 0.45 * strands
    if style == "matted":  # clotted, dirty
        v = v * (0.6 + 0.4 * fbm(p, 30, 3))
    v = v * (0.75 + 0.25 * fbm(p, 20, 2))
    col = np.stack([v, v, v], axis=1) * (0.45 + 0.55 * ao)[:, None]
    return np.clip(col, 0, 1)


# ---------------------------------------------------------------- strand cards (wispy edges)

def card_texture(size=256):
    """RGBA for the strand cards: four strips side by side (u 0..1 in quarters), each a bunch of tapering
    strands (alpha) growing from the root (v 0) to wispy tips (v 1); grey, tinted like the hair."""
    rng = np.random.default_rng(7)
    img = np.zeros((size, size, 4), np.float32)
    v = np.linspace(0, 1, size)[:, None]
    u = np.linspace(0, 1, size)[None, :]
    alpha = np.zeros((size, size))
    for strip in range(4):
        for _ in range(34):
            cu = (strip + rng.uniform(0.08, 0.92)) / 4
            length = rng.uniform(0.55, 1.0)
            width = rng.uniform(0.004, 0.008) * (1 - 0.7 * np.clip(v / length, 0, 1))
            wave = 0.006 * np.sin(v * rng.uniform(6, 12) + rng.uniform(0, 6))
            a = np.exp(-((u - cu - wave) / width) ** 2) * (v < length)
            alpha = np.maximum(alpha, a)
    shade = 0.75 + 0.2 * np.clip(1 - v, 0, 1)  # about the hair's own grey, so the tint matches
    img[..., 0] = img[..., 1] = img[..., 2] = shade * np.ones_like(u)
    img[..., 3] = np.clip(alpha * 1.6, 0, 1) * np.clip((1 - v) * 3, 0, 1) ** 0.5  # thinning to the tips
    return img
