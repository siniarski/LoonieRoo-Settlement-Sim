# Trees and undergrowth for first person (docs/trees.md), all by script, run headless:
#
#   "$B" --background --factory-startup --python art/scripts/build_trees.py -- \
#       --glb godot/assets/trees/trees.glb --tex godot/assets/trees/leaves.png [--preview lineup.png]
#
# Five tree species (spruce, pine, broadleaf, birch, a dead snag), each in three LODs (_0 near, _1 mid,
# _2 far), and two undergrowth pieces (bush, fern), one LOD. Low-poly modelled trunks and crowns in vertex
# colours, with alpha-cut leaf cards on the near LODs only (one 1024 atlas, four tiles: broadleaf, needles,
# fern, birch). Foliage normals point out of the crown (a soft, volume-lit crown rather than lit facets);
# the game (godot/World3D/Trees.cs) reads them that way and sways everything above the ground.
#
# Three materials, by name, that the game replaces with its own shaders: bark, foliage (solid crown
# masses), cards (the atlas, alpha scissor, double sided). Blender space: Z up, the tree's foot at the
# origin. Everything comes from a fixed seed per species, so a rebuild gives the same trees.

import math
import os
import random
import struct
import sys
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Vector, noise

BARK, FOLIAGE, CARDS = 0, 1, 2
TILE_BROAD, TILE_NEEDLE, TILE_FERN, TILE_BIRCH = 0, 1, 2, 3
UP = Vector((0, 0, 1))


def lin(r, g, b):
    """An sRGB colour as linear (the colour attribute is linear)."""
    return tuple(c ** 2.2 for c in (r, g, b))


def mul(c, k):
    return tuple(max(0.0, min(1.0, v * k)) for v in c)


def mix(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


# --- geometry collected as plain lists, turned into a mesh at the end ---

class Geo:
    def __init__(self):
        self.v = []      # positions
        self.f = []      # (vertex indices, material)
        self.col = []    # per corner: colour
        self.uv = []     # per corner: uv
        self.nrm = []    # per corner: a normal, or None for the smooth one

    def vert(self, co):
        self.v.append(Vector(co))
        return len(self.v) - 1

    def face(self, idx, mat, cols, uvs=None, nrms=None):
        self.f.append((tuple(idx), mat))
        for k in range(len(idx)):
            self.col.append(cols[k] if isinstance(cols, list) else cols)
            self.uv.append(uvs[k] if uvs else (0.0, 0.0))
            self.nrm.append(nrms[k] if nrms else None)

    def tris(self):
        return sum(len(i) - 2 for i, _ in self.f)


def frame(d):
    """Two unit vectors across direction d, u x w = d."""
    d = d.normalized()
    ref = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    u = d.cross(ref).normalized()
    w = d.cross(u).normalized()
    # u x w = d: d x u = w, so u x (d x u) = d (u . u) - u (u . d) = d.
    return u, w


def tube(g, pts, radii, segs, colour, cap=True, jag=0.0, rng=None):
    """A bark tube along pts (list of Vector) with a radius at each; colour(t, angle) -> rgb, t along 0..1."""
    rings = []
    n = len(pts)
    for k in range(n):
        d = (pts[min(k + 1, n - 1)] - pts[max(k - 1, 0)])
        u, w = frame(d)
        ring = []
        for j in range(segs):
            a = 2 * math.pi * j / segs
            r = radii[k] * (1 + (noise.noise(pts[k] * 3.1 + Vector((j, 0, 0))) * 0.12 if radii[k] > 0.05 else 0))
            p = pts[k] + (u * math.cos(a) + w * math.sin(a)) * r
            if jag and k == n - 1 and rng:
                p = p + d.normalized() * (rng.random() - 0.3) * jag
            ring.append((g.vert(p), a))
        rings.append(ring)
    for k in range(n - 1):
        t0, t1 = k / (n - 1), (k + 1) / (n - 1)
        for j in range(segs):
            j1 = (j + 1) % segs
            a, b = rings[k][j], rings[k][j1]
            c, d_ = rings[k + 1][j1], rings[k + 1][j]
            g.face([a[0], b[0], c[0], d_[0]], BARK,
                   [colour(t0, a[1]), colour(t0, b[1]), colour(t1, c[1]), colour(t1, d_[1])])
    if cap and radii[-1] > 0.004:
        top = rings[-1]
        g.face([v for v, _ in top], BARK, [mul(colour(1, a), 0.8) for _, a in top])


def icosphere(subdiv):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    vs = [v.co.copy() for v in bm.verts]
    fs = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return vs, fs


ICO = {s: icosphere(s) for s in (1, 2)}
# Crown masses: an icosphere of 80 triangles near and mid, 20 far.
SUB = (2, 2, 1)


def blob(g, centre, radius, squash, subdiv, colour, crown, seed, bumps=0.22):
    """A crown mass: a bumped, flat-bottomed sphere; normals out of the crown (crown = its centre)."""
    vs, fs = ICO[subdiv]
    base = len(g.v)
    off = Vector((seed * 7.3, seed * 1.9, seed * 3.7))
    for v in vs:
        p = Vector((v.x * squash[0], v.y * squash[1], v.z * squash[2]))
        p *= 1 + noise.noise(v * 1.7 + off) * bumps
        if p.z < 0:
            p.z *= 0.55
        g.v.append(centre + p * radius)
    for f in fs:
        cols, nrms = [], []
        for i in f:
            p = g.v[base + i]
            local = (p - centre) / radius
            out = (p - crown)
            out = out.normalized() if out.length > 1e-4 else UP
            n = (local.normalized() * 0.45 + out * 0.55 + UP * 0.15).normalized()
            nrms.append(tuple(n))
            # Darker underneath and towards the crown's heart: cheap ambient occlusion.
            under = max(0.0, min(1.0, local.z * 0.5 + 0.5))
            heart = max(0.0, min(1.0, (p - crown).length / max(radius * 2.2, 0.5)))
            cols.append(mul(colour, 0.5 + 0.3 * under + 0.3 * heart))
        g.face([base + i for i in f], FOLIAGE, cols, nrms=nrms)


def tile_uv(tile):
    col, row = tile % 2, tile // 2
    u0, v0 = col * 0.5, 1 - (row + 1) * 0.5
    return u0, v0


def card(g, base, up, right, length, width, tile, colour, crown, bend=0.0):
    """A leaf card from base along up (the tile's twig foot at its bottom), width along right; optionally bent
    in two segments (bend: how far the tip sags, as a fraction of the length)."""
    u0, v0 = tile_uv(tile)
    up = up.normalized()
    right = right.normalized()
    sag = up.cross(right).normalized()
    hw = right * (width / 2)
    pts = [base, base + up * (length * 0.5) - sag * (length * bend * 0.3), base + up * length - sag * (length * bend)]
    rows = []
    for k, p in enumerate(pts):
        t = k / 2
        rows.append((g.vert(p - hw), g.vert(p + hw), t))
    for k in range(2):
        (a, b, ta), (c, d, tb) = rows[k], rows[k + 1]
        idx = [a, b, d, c]
        uvs = [(u0 + 0.003, v0 + ta * 0.497), (u0 + 0.497, v0 + ta * 0.497), (u0 + 0.497, v0 + tb * 0.497), (u0 + 0.003, v0 + tb * 0.497)]
        nrms, cols = [], []
        for i in idx:
            out = g.v[i] - crown
            out = out.normalized() if out.length > 1e-4 else UP
            nrms.append(tuple((out + UP * 0.25).normalized()))
            cols.append(mul(colour, 0.75 + 0.25 * (ta if i in (a, b) else tb)))
        g.face(idx, CARDS, cols, uvs, nrms)


def on_shell(rng, centre, radii, lo=0.55, hi=0.95, zmin=-0.6):
    """A random point inside an ellipsoid's shell (radii x, y, z), not too far down."""
    while True:
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)))
        if d.length < 1e-3:
            continue
        d.normalize()
        if d.z < zmin:
            continue
        s = lo + (hi - lo) * rng.random()
        return centre + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * s, d


def trunk_line(h, n, lean, seed, wobble=0.05):
    pts = []
    for k in range(n + 1):
        t = k / n
        off = Vector((noise.noise(Vector((seed, 0.3, t * 2.2))), noise.noise(Vector((0.7, seed, t * 2.2))), 0)) * wobble
        pts.append(Vector((lean[0] * t * t, lean[1] * t * t, 0)) + off * (0.3 + t) + Vector((0, 0, h * t)))
    return pts


def bark_colour(base, dark, rng, mottle=0.12):
    def c(t, a):
        k = 1 + (noise.noise(Vector((t * 9.0, math.cos(a) * 2, math.sin(a) * 2))) * mottle)
        foot = max(0.0, 1 - t * 6)  # wet and dirty at the foot
        return mul(mix(base, dark, foot), k)
    return c


# --- species ---

SPRUCE_BARK, SPRUCE_DARK = lin(0.26, 0.22, 0.19), lin(0.15, 0.13, 0.11)
SPRUCE_NEEDLE = lin(0.16, 0.23, 0.19)
PINE_BARK, PINE_NEEDLE = lin(0.33, 0.25, 0.19), lin(0.22, 0.28, 0.18)
BROAD_BARK, BROAD_LEAF, BROAD_TIRED = lin(0.27, 0.25, 0.21), lin(0.29, 0.35, 0.19), lin(0.38, 0.36, 0.19)
BIRCH_BARK, BIRCH_MARK, BIRCH_LEAF = lin(0.70, 0.68, 0.63), lin(0.14, 0.13, 0.12), lin(0.36, 0.41, 0.21)
DEAD_BARK, DEAD_DARK = lin(0.40, 0.38, 0.35), lin(0.22, 0.21, 0.19)
BUSH_LEAF, FERN_LEAF = lin(0.25, 0.31, 0.17), lin(0.29, 0.36, 0.19)
STICK = lin(0.30, 0.28, 0.25)


def spruce(lod):
    rng = random.Random(11)
    g = Geo()
    H = 12.0
    segs = (8, 6, 4)[lod]
    tube(g, trunk_line(H, (6, 3, 1)[lod], (0.1, 0.05), 1.0, 0.03), [0.17 * (1 - t) ** 0.9 + 0.02 for t in [k / (6, 3, 1)[lod] for k in range((6, 3, 1)[lod] + 1)]],
         segs, bark_colour(SPRUCE_BARK, SPRUCE_DARK, rng), cap=False)
    tiers, m = (13, 7, 6)[lod], (18, 10, 6)[lod]
    for k in range(tiers):
        t = k / (tiers - 1)
        y = 3.0 + t * (H - 3.3)
        r = (0.25 + 1.3 * (1 - t) ** 1.1) * (0.88 + 0.24 * rng.random()) * (1.12 if lod == 2 else 1)
        # Far LODs have fewer tiers: thicker ones, so they overlap and don't read as a pagoda.
        thick = (1.0, 1.9, 2.4)[lod]
        droop = (0.2 + 0.3 * r) * (1.0, 1.35, 1.5)[lod]
        shade = 0.75 + 0.25 * t
        top = g.vert((0, 0, y + (0.2 + 0.15 * r) * thick))
        bot = g.vert((0, 0, y - 0.08))
        rim = []
        spin = rng.random() * math.pi
        for j in range(m):
            a = spin + 2 * math.pi * j / m
            rr = r * (1.0 if j % 2 == 0 else 0.7) * (0.85 + 0.3 * rng.random())
            z = y - droop * (rr / r) + (rng.random() - 0.5) * 0.12
            rim.append((g.vert((math.cos(a) * rr, math.sin(a) * rr, z)), a, rr))
        c_out, c_in = mul(SPRUCE_NEEDLE, shade), mul(SPRUCE_NEEDLE, shade * 0.6)
        for j in range(m):
            (i0, a0, r0), (i1, a1, r1) = rim[j], rim[(j + 1) % m]
            n0 = tuple(Vector((math.cos(a0) * 0.8, math.sin(a0) * 0.8, 0.6)).normalized())
            n1 = tuple(Vector((math.cos(a1) * 0.8, math.sin(a1) * 0.8, 0.6)).normalized())
            g.face([top, i0, i1], FOLIAGE, [c_in, c_out, c_out], nrms=[(0, 0, 1), n0, n1])
            d0 = tuple(Vector((math.cos(a0), math.sin(a0), 0.1)).normalized())
            d1 = tuple(Vector((math.cos(a1), math.sin(a1), 0.1)).normalized())
            g.face([bot, i1, i0], FOLIAGE, [mul(c_in, 0.9), mul(c_out, 0.75), mul(c_out, 0.75)], nrms=[(0, 0, -1), d1, d0])
        if lod == 0:
            # Needle sprays out past the long spokes: a soft edge to the tier.
            for j in range(0, m, 2):
                _, a, rr = rim[j]
                out = Vector((math.cos(a), math.sin(a), 0))
                side = Vector((-math.sin(a), math.cos(a), 0))
                base = out * (rr * 0.45) + Vector((0, 0, y - droop * 0.5))
                card(g, base, out + Vector((0, 0, -0.35)), side, rr * 0.55 + 0.25, 0.8, TILE_NEEDLE, mul(SPRUCE_NEEDLE, shade * 1.1), Vector((0, 0, y)), bend=0.25)
    # The leader at the top.
    tip0, tip1 = Vector((0.1, 0.05, H - 0.4)), Vector((0.1, 0.05, H + 0.5))
    tube(g, [tip0, tip1], [0.18, 0.0], max(segs - 2, 3), lambda t, a: mul(SPRUCE_NEEDLE, 0.9), cap=False)
    if lod == 0:
        # Dead lower branches, bare grey sticks below the crown.
        for k in range(7):
            a = rng.random() * math.tau
            h = 1.2 + rng.random() * 1.6
            d = Vector((math.cos(a), math.sin(a), -0.25 - rng.random() * 0.3)).normalized()
            p0 = Vector((0, 0, h)) + d * 0.18
            tube(g, [p0, p0 + d * (0.25 + rng.random() * 0.3)], [0.03, 0.008], 3, lambda t, a: STICK, cap=False)
    return g


def pine(lod):
    rng = random.Random(23)
    g = Geo()
    H = 14.0
    n = (7, 4, 1)[lod]
    pts = trunk_line(H, n, (0.45, -0.2), 2.0, 0.12)
    tube(g, pts, [0.19 * (1 - k / n) ** 0.7 + 0.035 for k in range(n + 1)], (8, 6, 4)[lod], bark_colour(PINE_BARK, SPRUCE_DARK, rng, 0.18))
    crown = Vector((pts[-1].x * 0.8, pts[-1].y * 0.8, H - 2.2))
    clumps = (8, 4, 2)[lod]
    for k in range(clumps):
        t = k / max(clumps - 1, 1)
        h = H - 4.8 + t * 4.6 if clumps > 1 else H - 1.5
        a = k * 2.4 + rng.random() * 0.6
        dist = (1.0 + rng.random() * 1.1) * (1 - t * 0.6) if lod < 2 else 0.4 * k
        on = Vector((pts[-1].x * h / H, pts[-1].y * h / H, h))
        c = on + Vector((math.cos(a) * dist, math.sin(a) * dist, 0.3))
        r = (0.9 + rng.random() * 0.5) * (1.25 if lod == 1 else 1.6 if lod == 2 else 1.0)
        if lod < 2 and dist > 0.3:
            tube(g, [on - Vector((0, 0, 0.3)), c], [0.07, 0.03], 4, lambda t, a: PINE_BARK, cap=False)
        tone = mul(mix(PINE_NEEDLE, BROAD_TIRED, rng.random() * 0.25), 0.9 + rng.random() * 0.2)
        blob(g, c, r, (1.0, 1.0, 0.72), SUB[lod], tone, crown, k + 1)
        if lod == 0:
            for q in range(5):
                b = rng.random() * math.tau
                out = Vector((math.cos(b), math.sin(b), -0.2 + rng.random() * 0.5)).normalized()
                base = c + out * r * 0.55
                card(g, base, out, out.cross(UP).normalized() if abs(out.z) < 0.95 else Vector((1, 0, 0)), 0.9, 0.8, TILE_NEEDLE, mul(tone, 1.1), crown, bend=0.2)
    if lod == 0:
        for k in range(9):
            a = rng.random() * math.tau
            h = 2.5 + rng.random() * 6.0
            on = Vector((pts[-1].x * h / H, pts[-1].y * h / H, h))
            d = Vector((math.cos(a), math.sin(a), -0.1 + rng.random() * 0.3)).normalized()
            tube(g, [on, on + d * (0.25 + rng.random() * 0.35)], [0.035, 0.012], 3, lambda t, a: STICK, cap=True)
    return g


def broadleaf(lod):
    rng = random.Random(37)
    g = Geo()
    split = 3.0
    n = (4, 2, 1)[lod]
    pts = trunk_line(split + 0.4, n, (0.15, 0.1), 3.0, 0.05)
    tube(g, pts, [0.22 - 0.07 * k / n for k in range(n + 1)], (8, 6, 4)[lod], bark_colour(BROAD_BARK, SPRUCE_DARK, rng, 0.2))
    crown = Vector((0.15, 0.1, 6.8))
    radii = (3.0, 3.0, 2.4)
    limbs = (4, 3, 0)[lod]
    ends = []
    for k in range(limbs):
        a = k * math.tau / limbs + rng.random() * 0.7
        end = Vector((math.cos(a) * (1.3 + rng.random() * 0.8), math.sin(a) * (1.3 + rng.random() * 0.8), 5.6 + rng.random() * 1.6))
        mid = (pts[-1] + end) / 2 + Vector((0, 0, 0.3))
        tube(g, [pts[-1] - Vector((0, 0, 0.3)), mid, end], [0.12, 0.08, 0.05], (6, 5, 4)[lod], bark_colour(BROAD_BARK, SPRUCE_DARK, rng, 0.2), cap=False)
        ends.append(end)
    if lod == 0:
        # Twigs off the limbs into the crown.
        for e in ends:
            for q in range(2):
                a = rng.random() * math.tau
                d = Vector((math.cos(a), math.sin(a), 0.6)).normalized()
                tube(g, [e, e + d * (1.0 + rng.random() * 0.6)], [0.05, 0.012], 3, lambda t, a: BROAD_BARK, cap=False)
    blobs = (11, 5, 2)[lod]
    centres = []
    for k in range(blobs):
        if lod == 2:
            c = crown + Vector(((k - 0.5) * 1.2, (0.5 - k) * 0.6, k * 0.5 - 0.2))
            r = 2.5
        else:
            c, _ = on_shell(rng, crown, radii, 0.45, 0.8, -0.5)
            r = (1.1 + rng.random() * 0.5) * (1.35 if lod == 1 else 1.0)
        tone = mul(mix(BROAD_LEAF, BROAD_TIRED, rng.random() * 0.5), 0.9 + rng.random() * 0.2)
        blob(g, c, r, (1.0, 1.0, 0.8), SUB[lod], tone, crown, k + 5)
        centres.append((c, r, tone))
    if lod == 0:
        for k in range(64):
            c, r, tone = centres[k % len(centres)]
            p, d = on_shell(rng, c, (r, r, r * 0.8), 0.75, 0.95, -0.7)
            out = (p - crown).normalized()
            side = out.cross(UP).normalized() if abs(out.z) < 0.95 else Vector((1, 0, 0))
            tilt = (side * (rng.random() - 0.5) * 0.8)
            card(g, p - out * 0.3, out + tilt, side, 1.0 + rng.random() * 0.4, 0.95, TILE_BROAD, mul(tone, 1.05), crown, bend=0.15)
    return g


def birch(lod):
    rng = random.Random(41)
    g = Geo()
    H = 11.0
    n = (14, 6, 2)[lod]
    pts = trunk_line(H, n, (-0.35, 0.25), 4.0, 0.06)
    marks = [rng.random() < 0.3 for _ in range(n + 1)]

    def bark(t, a):
        k = int(round(t * n))
        foot = max(0.0, 1 - t * 9)
        c = BIRCH_MARK if (marks[k] and math.sin(a * 3 + k) > 0.2 and lod < 2) else BIRCH_BARK
        return mul(mix(c, mul(BIRCH_MARK, 1.6), foot), 1 + noise.noise(Vector((t * 13, a, 0))) * 0.08)
    tube(g, pts, [0.11 * (1 - k / n) ** 0.6 + 0.02 for k in range(n + 1)], (7, 5, 4)[lod], bark, cap=False)
    crown = Vector((pts[-1].x * 0.6, pts[-1].y * 0.6, 7.6))
    radii = (1.7, 1.7, 3.2)
    if lod < 2:
        for k in range((6, 3)[lod]):
            h = 4.5 + k * 1.0
            t = h / H
            on = pts[0].lerp(pts[-1], t)
            a = k * 2.1
            d = Vector((math.cos(a), math.sin(a), 1.2)).normalized()
            tube(g, [on, on + d * 1.5], [0.04, 0.012], 3, lambda t, a: BIRCH_BARK, cap=False)
    blobs = (8, 4, 2)[lod]
    centres = []
    for k in range(blobs):
        if lod == 2:
            c = crown + Vector((0, 0, (k - 0.5) * 2.0))
            r = 1.6
        else:
            c, _ = on_shell(rng, crown, radii, 0.3, 0.75, -0.8)
            r = (0.7 + rng.random() * 0.35) * (1.4 if lod == 1 else 1.0)
        tone = mul(mix(BIRCH_LEAF, BROAD_TIRED, rng.random() * 0.4), 0.9 + rng.random() * 0.2)
        blob(g, c, r, (1.0, 1.0, 1.1), SUB[lod], tone, crown, k + 9, 0.3)
        centres.append((c, r, tone))
    if lod == 0:
        for k in range(48):
            c, r, tone = centres[k % len(centres)]
            p, d = on_shell(rng, c, (r, r, r), 0.7, 0.95, -0.9)
            out = (p - crown)
            out.z *= 0.3
            out = out.normalized()
            side = out.cross(UP).normalized() if abs(out.z) < 0.95 else Vector((1, 0, 0))
            card(g, p - out * 0.2, out + Vector((0, 0, -0.5)), side, 0.9 + rng.random() * 0.4, 0.75, TILE_BIRCH, mul(tone, 1.1), crown, bend=0.35)
    return g


def snag(lod):
    rng = random.Random(53)
    g = Geo()
    H = 7.5
    n = (6, 3, 1)[lod]
    pts = trunk_line(H, n, (0.3, 0.2), 5.0, 0.08)
    tube(g, pts, [0.2 - 0.1 * k / n for k in range(n + 1)], (8, 6, 4)[lod], bark_colour(DEAD_BARK, DEAD_DARK, rng, 0.25), cap=True, jag=0.5, rng=rng)
    limbs = (7, 3, 0)[lod]
    for k in range(limbs):
        h = 2.6 + rng.random() * 4.2
        on = pts[0].lerp(pts[-1], h / H)
        a = k * 2.3 + rng.random()
        d = Vector((math.cos(a), math.sin(a), 0.5 + rng.random() * 0.6)).normalized()
        L = 0.6 + rng.random() * 1.6
        mid = on + d * L * 0.55 + Vector((0, 0, 0.1))
        tube(g, [on, mid, on + d * L + Vector((0, 0, 0.25))], [0.08, 0.05, 0.02], (5, 4, 3)[lod], bark_colour(DEAD_BARK, DEAD_DARK, rng, 0.25), cap=True)
    return g


def bush(lod):
    rng = random.Random(67)
    g = Geo()
    crown = Vector((0, 0, 0.35))
    centres = []
    for k in range(4):
        a = k * 1.7
        c = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.3 + rng.random() * 0.25))
        r = 0.35 + rng.random() * 0.2
        tone = mul(mix(BUSH_LEAF, BROAD_TIRED, rng.random() * 0.5), 0.9 + rng.random() * 0.2)
        blob(g, c, r, (1.0, 1.0, 0.8), SUB[lod], tone, crown, k + 13, 0.3)
        centres.append((c, r, tone))
    for k in range(16):
        c, r, tone = centres[k % 4]
        p, d = on_shell(rng, c, (r, r, r), 0.6, 0.9, -0.3)
        out = (p - crown).normalized()
        side = out.cross(UP).normalized() if abs(out.z) < 0.95 else Vector((1, 0, 0))
        card(g, p - out * 0.1, out + Vector((0, 0, 0.3)), side, 0.5 + rng.random() * 0.2, 0.5, TILE_BROAD, mul(tone, 1.05), crown, bend=0.1)
    return g


def fern(lod):
    rng = random.Random(79)
    g = Geo()
    crown = Vector((0, 0, -0.2))
    for k in range(9):
        a = k * math.tau / 9 + rng.random() * 0.4
        out = Vector((math.cos(a), math.sin(a), 0))
        up = (out + Vector((0, 0, 0.9 + rng.random() * 0.5))).normalized()
        side = Vector((-math.sin(a), math.cos(a), 0))
        tone = mul(mix(FERN_LEAF, BROAD_TIRED, rng.random() * 0.6), 0.85 + rng.random() * 0.25)
        card(g, Vector((0, 0, 0.02)) + out * 0.05, up, side, 0.7 + rng.random() * 0.3, 0.32, TILE_FERN, tone, crown, bend=0.55)
    return g


SPECIES = {"spruce": spruce, "pine": pine, "broadleaf": broadleaf, "birch": birch, "snag": snag}
UNDER = {"bush": bush, "fern": fern}


# --- into Blender ---

def materials():
    out = []
    for name in ("bark", "foliage", "cards"):
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        ca = nt.nodes.new("ShaderNodeVertexColor")
        ca.layer_name = "Col"
        nt.links.new(ca.outputs[0], bsdf.inputs[0])
        out.append(mat)
    return out


def to_object(name, g, mats):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in g.v], [], [list(i) for i, _ in g.f])
    me.update()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", [m for _, m in g.f])
    col = me.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
    uv = me.uv_layers.new(name="UVMap")
    # from_pydata keeps our faces' corner order.
    for i, c in enumerate(g.col):
        col.data[i].color = (c[0], c[1], c[2], 1.0)
        uv.data[i].uv = g.uv[i]
    me.shade_smooth()
    me.update()
    smooth = [tuple(cn.vector) for cn in me.corner_normals]
    me.normals_split_custom_set([g.nrm[i] if g.nrm[i] is not None else smooth[i] for i in range(len(g.nrm))])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# --- the leaf atlas (1024, four 512 tiles), painted with numpy ---

S = 512


def lens(img, cx, cy, ang, length, width, val, tint, shape=0.8, ovate=False):
    """A leaf from its stalk at (cx, cy) along ang (radians, 0 = up the image), length and width in pixels."""
    du, dv = math.sin(ang), -math.cos(ang)  # up the image is -y
    ru, rv = -dv, du
    r = int(length) + 2
    x0, x1 = max(0, int(cx - r)), min(img.shape[1], int(cx + r))
    y0, y1 = max(0, int(cy - r)), min(img.shape[0], int(cy + r))
    if x0 >= x1 or y0 >= y1:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1]
    px, py = xs - cx, ys - cy
    u = px * du + py * dv
    v = px * ru + py * rv
    t = np.clip(u / length, 0, 1)
    prof = np.sin(np.pi * (t ** (0.7 if ovate else 1.0))) ** shape
    m = (u >= 0) & (u <= length) & (np.abs(v) <= width / 2 * prof)
    shade = val * (0.82 + 0.18 * (1 - np.abs(v) / (width / 2 + 1e-3)))
    shade = np.where(np.abs(v) < 1.2, shade * 0.78, shade)
    for ch in range(3):
        img[y0:y1, x0:x1, ch] = np.where(m, shade * tint[ch], img[y0:y1, x0:x1, ch])
    img[y0:y1, x0:x1, 3] = np.where(m, 1.0, img[y0:y1, x0:x1, 3])


def stroke(img, a, b, w, val):
    n = int(max(abs(b[0] - a[0]), abs(b[1] - a[1]))) + 1
    for k in range(n + 1):
        t = k / n
        x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
        rw = w * (1 - 0.5 * t)
        x0, x1 = max(0, int(x - rw - 1)), min(img.shape[1], int(x + rw + 2))
        y0, y1 = max(0, int(y - rw - 1)), min(img.shape[0], int(y + rw + 2))
        ys, xs = np.mgrid[y0:y1, x0:x1]
        m = (xs - x) ** 2 + (ys - y) ** 2 <= rw * rw
        for ch in range(3):
            img[y0:y1, x0:x1, ch] = np.where(m, val, img[y0:y1, x0:x1, ch])
        img[y0:y1, x0:x1, 3] = np.where(m, 1.0, img[y0:y1, x0:x1, 3])


def tint(rng, spread=0.08):
    y = rng.random()
    return (1 + spread * (y - 0.3), 1.0, 1 - spread * 1.5 * y)


def broad_tile(rng, small=False):
    img = np.zeros((S, S, 4))
    twigs = []
    main = [(S / 2, S - 4), (S / 2 + 20, S * 0.55), (S / 2 - 10, 40)]
    stroke(img, main[0], main[1], 6, 0.35)
    stroke(img, main[1], main[2], 4, 0.35)
    twigs += [main[0], main[1], main[2]]
    for k in range(5):
        t = 0.2 + 0.15 * k
        p = (main[0][0] + (main[2][0] - main[0][0]) * t, main[0][1] + (main[2][1] - main[0][1]) * t)
        side = -1 if k % 2 else 1
        q = (p[0] + side * (110 + rng.random() * 60), p[1] - 50 - rng.random() * 60)
        stroke(img, p, q, 3, 0.35)
        twigs.append((p, q))
    count = 70 if small else 46
    for k in range(count):
        seg = twigs[3 + k % 5] if k % 3 else (main[0], main[2])
        t = 0.25 + rng.random() * 0.75
        p = (seg[0][0] + (seg[1][0] - seg[0][0]) * t, seg[0][1] + (seg[1][1] - seg[0][1]) * t)
        ang = math.atan2(seg[1][0] - seg[0][0], -(seg[1][1] - seg[0][1])) + (rng.random() - 0.5) * 2.2
        L = (38 + rng.random() * 20) if small else (70 + rng.random() * 45)
        W = L * (0.62 if small else 0.48)
        lens(img, p[0], p[1], ang, L, W, 0.62 + rng.random() * 0.38, tint(rng, 0.12), 0.9 if small else 0.8, ovate=small)
    return img


def needle_tile(rng):
    img = np.zeros((S, S, 4))
    main = ((S / 2, S - 4), (S / 2, 30))
    stroke(img, main[0], main[1], 4, 0.3)
    twigs = [main]
    for k in range(9):
        t = 0.12 + 0.09 * k
        p = (S / 2, S - 4 - (S - 34) * t)
        for side in (-1, 1):
            q = (p[0] + side * (150 - 80 * t + rng.random() * 30), p[1] - 70 - rng.random() * 40)
            stroke(img, p, q, 2.5, 0.3)
            twigs.append((p, q))
    for (a, b) in twigs:
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
        for s in np.arange(5, L, 3.0):
            p = (a[0] + d[0] * s, a[1] + d[1] * s)
            for side in (-1, 1):
                ang = math.atan2(d[0], -d[1]) + side * (0.75 + rng.random() * 0.3)
                n = 12 + rng.random() * 10 * (1 - s / L)
                q = (p[0] + math.sin(ang) * n, p[1] - math.cos(ang) * n)
                v = 0.6 + rng.random() * 0.3
                stroke(img, p, q, 2.0, v)
    return img


def fern_tile(rng):
    img = np.zeros((S, S, 4))
    pts = [(S / 2 + 18 * math.sin(t * 2.2), S - 4 - (S - 20) * t) for t in np.linspace(0, 1, 40)]
    for a, b in zip(pts, pts[1:]):
        stroke(img, a, b, 3.5, 0.4)
    for k in range(3, 38, 2):
        t = k / 40
        p = pts[k]
        L = 190 * math.sin(math.pi * (0.15 + 0.85 * t)) ** 0.8 * (1 - 0.3 * t) + 12
        for side in (-1, 1):
            ang = side * (1.25 - 0.35 * t)
            lens(img, p[0], p[1], ang, L, L * 0.2, 0.6 + rng.random() * 0.35, tint(rng, 0.1), 0.6)
    return img


def png(path, img):
    h, w, _ = img.shape
    data = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(kind, body):
        c = struct.pack(">I", len(body)) + kind + body
        return c + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def atlas(path):
    rng = random.Random(97)
    full = np.zeros((2 * S, 2 * S, 4))
    tiles = [broad_tile(rng), needle_tile(rng), fern_tile(rng), broad_tile(rng, small=True)]
    for t, img in enumerate(tiles):
        col, row = t % 2, t // 2
        full[row * S:(row + 1) * S, col * S:(col + 1) * S] = img
    # Bleed the leaves' colour into the clear texels, so mipmaps don't darken the edges.
    a = full[..., 3:4]
    mean = (full[..., :3] * a).sum(axis=(0, 1)) / max(a.sum(), 1)
    full[..., :3] = np.where(a > 0, full[..., :3], mean)
    png(path, full)


def preview(path, objs):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x, scene.render.resolution_y = 1600, 700
    for k, ob in enumerate(objs):
        ob.location = (k * 6.5, 0, 0)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    mid = (len(objs) - 1) * 6.5 / 2
    cam.location = (mid, -48, 7)
    cam.rotation_euler = (math.radians(84), 0, 0)
    cam.data.lens = 30
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    glb = opts.get("glb", "godot/assets/trees/trees.glb")
    tex = opts.get("tex", "godot/assets/trees/leaves.png")
    os.makedirs(os.path.dirname(os.path.abspath(glb)), exist_ok=True)
    mats = materials()
    objs, near = [], []
    for name, fn in SPECIES.items():
        for lod in range(3):
            g = fn(lod)
            ob = to_object(f"{name}_{lod}", g, mats)
            objs.append(ob)
            if lod == 0:
                near.append(ob)
            print(f"TREE {name}_{lod}: {g.tris()} triangles")
    for name, fn in UNDER.items():
        g = fn(0)
        ob = to_object(f"{name}_0", g, mats)
        objs.append(ob)
        near.append(ob)
        print(f"TREE {name}_0: {g.tris()} triangles")
    atlas(tex)
    print(f"TREE atlas -> {tex}")
    bpy.ops.export_scene.gltf(filepath=os.path.abspath(glb), export_format="GLB", use_selection=False,
                              export_vertex_color="ACTIVE", export_normals=True, export_materials="EXPORT",
                              export_apply=True)
    print(f"TREE glb -> {glb}")
    if "preview" in opts:
        for ob in objs:
            ob.hide_render = ob not in near
        preview(os.path.abspath(opts["preview"]), near)


main()
