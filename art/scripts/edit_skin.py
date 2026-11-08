# Edits to the stored base skin (art/models/base_skin.blend), for when they're done by script rather than
# by hand in Blender: each is a named function, applied to the file and recorded on the object (the
# "edits" property), so running it again skips what's done. The .blend stays the source; this file is
# its history of scripted changes.
#
#   blender art/models/base_skin.blend --background --python art/scripts/edit_skin.py -- --edits cheeks,lips
#   (no --edits: list them and what's applied)

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sculpt as S  # noqa: E402


def _neighbours(me):
    nb = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nb[a].append(b)
        nb[b].append(a)
    return nb


def _relax(me, weights, rounds):
    """Laplacian relax, each vertex by its weight (0 stays, 1 moves halfway to its neighbours' mean)."""
    nb = _neighbours(me)
    idx = [i for i, w in enumerate(weights) if w > 0]
    for _ in range(rounds):
        co = [v.co.copy() for v in me.vertices]
        for i in idx:
            if nb[i]:
                avg = sum((co[j] for j in nb[i]), Vector()) / len(nb[i])
                me.vertices[i].co = co[i].lerp(avg, 0.5 * weights[i])


def cheeks(o):
    """Soften where the face's front turns into its side, temple to jaw: the turn was sharp enough that
    the sun's shadow edge sat on it as a hard line down the face at 3/4."""
    me = o.data
    w = []
    for v in me.vertices:
        c = v.co
        ax = abs(c.x)
        band = S.smooth01(1 - abs(ax - 0.058) / 0.020)          # the turn, ~5.8 cm out from the middle
        height = S.smooth01((c.z - 1.515) / 0.02) * S.smooth01((1.705 - c.z) / 0.02)
        front = S.smooth01((-c.y - 0.015) / 0.02) * S.smooth01((c.y + 0.095) / 0.01)
        eye = min((c - S.EYE_L).length, (c - S.EYE_R).length)
        w.append(band * height * front * S.smooth01((eye - 0.020) / 0.01))  # not the eyes' edges
    _relax(me, w, 30)


def lips(o):
    """Fuller lips: the upper lip forward 1.5 mm with a soft Cupid's bow and a ridge at its border, the
    lower forward 2.2 mm and rounder; the corners stay tucked."""
    me = o.data
    m, half = S.MOUTH, S.MOUTH_HALF
    for v in me.vertices:
        c = v.co
        if abs(c.x) > half * 1.3 or abs(c.z - m.z) > 0.016 or c.y > m.y + 0.004:
            continue
        across = math.exp(-(c.x / (half * 0.75)) ** 2)
        if c.z >= m.z:
            u = c.z - m.z
            bow = 0.0012 * math.exp(-((abs(c.x) - 0.006) / 0.004) ** 2)  # the bow's two peaks
            vol = 0.0015 * math.exp(-((u - 0.0045) / 0.0035) ** 2)
            ridge = 0.0006 * math.exp(-((u - (0.0085 + bow)) / 0.0012) ** 2)
            v.co.y -= (vol + ridge) * across
        else:
            u = m.z - c.z
            vol = 0.0022 * math.exp(-((u - 0.0055) / 0.0040) ** 2)
            v.co.y -= vol * across
            v.co.z -= 0.0004 * math.exp(-((u - 0.004) / 0.003) ** 2) * across  # rounder, a touch fuller below


def lips2(o):
    """More: the lips another 1.2 / 1.6 mm forward, and the lips' cut edge smoothed along its length (its
    snapped vertices zigzagged a little in z)."""
    me = o.data
    m, half = S.MOUTH, S.MOUTH_HALF
    for v in me.vertices:
        c = v.co
        if abs(c.x) > half * 1.3 or abs(c.z - m.z) > 0.016 or c.y > m.y + 0.004:
            continue
        across = math.exp(-(c.x / (half * 0.75)) ** 2)
        u = abs(c.z - m.z)
        v.co.y -= (0.0012 if c.z >= m.z else 0.0016) * math.exp(-((u - 0.005) / 0.004) ** 2) * across
    # The boundary (the lips' edge and the inner lips' end): each vertex's z to the mean of its boundary
    # neighbours', a few times.
    count = {}
    for poly in me.polygons:
        for k in poly.edge_keys:
            count[k] = count.get(k, 0) + 1
    nb = {}
    for (a, b), n in count.items():
        if n == 1:
            for i, j in ((a, b), (b, a)):
                c = me.vertices[i].co
                if abs(c.x) < half * 1.1 and abs(c.z - m.z) < 0.01 and c.y < m.y + 0.02:
                    nb.setdefault(i, []).append(j)
    for _ in range(6):
        z = {i: me.vertices[i].co.z for i in nb}
        for i, js in nb.items():
            zs = [me.vertices[j].co.z for j in js]
            me.vertices[i].co.z = 0.5 * z[i] + 0.5 * sum(zs) / len(zs)


EDITS = {"cheeks": cheeks, "lips": lips, "lips2": lips2}


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = bpy.data.objects["skin"]
    done = list(o.get("edits", []))
    if "--edits" not in a:
        print("edits:", ", ".join(EDITS), "| applied:", ", ".join(done) or "none")
        return
    for name in a[a.index("--edits") + 1].split(","):
        if name in done:
            print(f"{name}: already applied")
            continue
        EDITS[name](o)
        done.append(name)
        print(f"{name}: applied")
    o["edits"] = done
    o.data.update()
    bpy.ops.wm.save_mainfile(compress=True)
    print("saved", bpy.data.filepath)


if __name__ == "__main__":
    main()
