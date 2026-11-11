"""Which texels of a look model's painted map are cloth and which are shoe, for the detail kit (docs/textures.md,
docs/characters.md "Detail on bodies"): the people's and the dead's clothes take the kit's cloth layer, their shoes
and boots the rubber layer, laid by the model's own atlas UV (it moves with the skin, so the grain never swims);
skin, hair, eyes and teeth stay plain.

    blender --background --factory-startup --python art/scripts/build_detail_maps.py -- \
        [--dir godot/assets/characters/look] [--only look_pz_zombie,look_pz_survivor_jacket]

For every look_pz_*.glb in --dir: <file>_detail.png (the atlas's size, 8-bit RGB: red cloth, green shoe, blue 0) and
<file>_detail.json ({"metres_per_uv": how many metres of the body one unit of its UV covers, the area-weighted
mean over the near LOD's triangles}), beside the .glb. The game (Characters/Looks.cs) repeats the layers by that.

The regions are the paint's own (paint.regions over the rest position, as build_lookdev.py painted them): on the
body, the painted shirt and trousers are cloth (less the dead's holes, which show skin), the painted shoes shoe; a
garment piece (jacket, hoodie, jeans) is all cloth. Nothing is baked: each near-LOD triangle is laid into the atlas
by its UV with its rest positions, so this takes seconds and needs no rebuild of the characters.
"""

import glob
import json
import os
import struct
import sys
import zlib

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paint  # noqa: E402


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return {a[i].lstrip("-"): a[i + 1] for i in range(0, len(a) - 1, 2)}


def outfit_of(name):
    """The paint's outfit for a file (look_pz_<subject>[_<face>][_<clothes or outfit>])."""
    tags = name.split("_")[2:]
    subject = tags[0]
    o = dict(paint.OUTFITS[subject])
    for t in tags[1:]:
        o.update(paint.OUTFIT_VARIANTS.get(t, {}))
    return o


def triangles(obj):
    """The mesh's triangles: rest positions (N, 3, 3, Blender's Z-up, metres) and UVs (N, 3, 2)."""
    me = obj.data
    me.calc_loop_triangles()
    co = np.array([obj.matrix_world @ v.co for v in me.vertices])
    uv = np.array([d.uv[:] for d in me.uv_layers.active.data])
    tri_v = np.array([t.vertices[:] for t in me.loop_triangles])
    tri_l = np.array([t.loops[:] for t in me.loop_triangles])
    return co[tri_v], uv[tri_l]


def rasterise(pos, uv, size, piece, into_pos, into_piece):
    """Each triangle's pixels (centres inside it, in the image's rows top first as glTF reads it) get the rest
    position there and the piece's number."""
    px = np.stack([uv[:, :, 0] * size, (1 - uv[:, :, 1]) * size], axis=2)  # (N, 3, 2) in pixels
    for t in range(len(px)):
        a, b, c = px[t]
        x0, y0 = np.floor(np.minimum(np.minimum(a, b), c)).astype(int)
        x1, y1 = np.ceil(np.maximum(np.maximum(a, b), c)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        w0 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / d
        w1 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / d
        w2 = 1 - w0 - w1
        e = -0.02  # a hair over the edge, so neighbouring triangles leave no gap
        inside = (w0 >= e) & (w1 >= e) & (w2 >= e)
        if not inside.any():
            continue
        ys, xs = (gy[inside] - 0.5).astype(int), (gx[inside] - 0.5).astype(int)
        p = w0[inside, None] * pos[t, 0] + w1[inside, None] * pos[t, 1] + w2[inside, None] * pos[t, 2]
        into_pos[ys, xs] = p
        into_piece[ys, xs] = piece


def metres_per_uv(pos, uv):
    """sqrt(3D area / UV area), weighted by the 3D area: the atlas's scale in metres per UV unit."""
    a3 = 0.5 * np.linalg.norm(np.cross(pos[:, 1] - pos[:, 0], pos[:, 2] - pos[:, 0]), axis=1)
    d1, d2 = uv[:, 1] - uv[:, 0], uv[:, 2] - uv[:, 0]
    a2 = 0.5 * np.abs(d1[:, 0] * d2[:, 1] - d1[:, 1] * d2[:, 0])
    ok = a2 > 1e-10
    return float((np.sqrt(a3[ok] / a2[ok]) * a3[ok]).sum() / a3[ok].sum())


def bleed(img, have, steps=4):
    """Spread the map a few texels past each island's edge, so a filtered or mipmapped lookup at a seam keeps it."""
    m, have = img.copy(), have.copy()
    for _ in range(steps):
        grow = np.zeros_like(m)
        n = np.zeros(have.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            grow += np.roll(np.roll(m, dy, 0), dx, 1) * np.roll(np.roll(have, dy, 0), dx, 1)[:, :, None]
            n += np.roll(np.roll(have, dy, 0), dx, 1)
        fill = (~have) & (n > 0)
        m[fill] = grow[fill] / n[fill][:, None]
        have = have | fill
    return m


def write_png(path, rgb):
    rgb8 = (np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w = rgb8.shape[:2]
    raw = b"".join(b"\x00" + rgb8[r].tobytes() for r in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def build(path, size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    name = os.path.splitext(os.path.basename(path))[0]
    o = outfit_of(name)
    dead = o["dead"]
    pieces = sorted((ob for ob in bpy.data.objects if ob.type == "MESH" and ob.name.endswith("_lod0")), key=lambda ob: ob.name != "body_lod0")
    pos = np.zeros((size, size, 3))
    piece = np.full((size, size), -1, int)
    scale = []
    for k, ob in enumerate(pieces):
        p, uv = triangles(ob)
        rasterise(p, uv, size, k, pos, piece)
        scale.append((metres_per_uv(p, uv), len(p)))
    covered = piece >= 0
    out = np.zeros((size, size, 3), np.float32)
    for k, ob in enumerate(pieces):
        sel = piece == k
        if not sel.any():
            continue
        if ob.name == "body_lod0":
            p = pos[sel]
            m = paint.regions(p, o)
            cloth = np.clip(m["shirt"] + m["trousers"], 0, 1) * (1 - m["eye"])
            if dead:  # the torn shirt's holes show the skin under it
                cloth = cloth * (1 - paint.sstep(0.66, 0.68, paint.fbm(p + 3.3, 11, 4)) * m["shirt"])
            out[sel, 0] = cloth
            out[sel, 1] = m["shoes"] * (1 - m["eye"])
        else:
            out[sel, 0] = 1.0
    out = bleed(out, covered)
    base = os.path.splitext(path)[0]
    write_png(base + "_detail.png", out)
    mpu = sum(s * n for s, n in scale) / sum(n for _, n in scale)
    with open(base + "_detail.json", "w", encoding="utf-8") as f:
        json.dump({"metres_per_uv": round(mpu, 4)}, f)
        f.write("\n")
    lo, hi = pos[covered].min(0), pos[covered].max(0)
    print(f"{name}: {[ob.name for ob in pieces]} {covered.mean():.0%} covered, cloth {out[covered, 0].mean():.0%}, "
          f"shoe {out[covered, 1].mean():.0%}, {mpu:.3f} m a UV unit, rest bounds {np.round(lo, 2)} {np.round(hi, 2)}")


def main():
    a = args()
    d = a.get("dir", "godot/assets/characters/look")
    only = set(a["only"].split(",")) if "only" in a else None
    for path in sorted(glob.glob(os.path.join(d, "look_pz_*.glb"))):
        if only and os.path.splitext(os.path.basename(path))[0] not in only:
            continue
        build(path, int(a.get("size", 512)))


main()
