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


def _no_extras(bm):
    pass


STYLES = {
    "buzz": {"mask": buzz_mask, "lift": buzz_lift, "extras": _no_extras, "tris": 1500, "drape": 2},
    "crop": {"mask": crop_mask, "lift": crop_lift, "extras": _no_extras, "tris": 3000, "drape": 6},
    "sidepart": {"mask": sidepart_mask, "lift": sidepart_lift, "extras": _no_extras, "tris": 4000, "drape": 8},
    "long": {"mask": long_mask, "lift": long_lift, "extras": _hang_long, "tris": 6000, "drape": 8},
    "receding": {"mask": receding_mask, "lift": receding_lift, "extras": _no_extras, "tris": 3000, "drape": 6},
    "beard": {"mask": beard_mask, "lift": beard_lift, "extras": _no_extras, "tris": 2000, "drape": 3},
}


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
    strands = fbm(flow, 220, 3)
    v = 0.55 + 0.45 * strands
    v = v * (0.75 + 0.25 * fbm(p, 20, 2))
    col = np.stack([v, v, v], axis=1) * (0.45 + 0.55 * ao)[:, None]
    return np.clip(col, 0, 1)
