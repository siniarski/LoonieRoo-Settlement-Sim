"""The world's detail textures: grain and scale for the flat colours, one small tileable picture per material.

    blender --background --factory-startup --python art/scripts/build_surfaces.py -- [--out godot/assets/surfaces] [--sheet experiments/textures/kit.png]

(Blender only for its Python and numpy: any Python with numpy runs it.) Writes <name>.png (256 x 256, RGB) for every
layer in LAYERS and layers.json (the order, and how many metres a repeat covers), which godot/World3D/Surfaces.cs
reads into one texture array. Seeded by the layer's name: the same files every run.

A detail texture is not a colour: it is a multiplier round 1, stored halved (a texel of 0.5 leaves the colour as it
is, 1.0 doubles it). Every channel of every layer averages 0.5 exactly, so a surface keeps its colour, and seen from
far off (the small mipmaps, the far view) it is the flat colour it was. Up in the picture is up on a wall; boards,
courses and shingle rows run across it; ribs, furrows and strands run down it. docs/textures.md has the rest.

To add a layer: write its function, add a line to LAYERS (at the end: models carry layer numbers), run this. To
replace one with something better (a photo, a painting): any tileable 256 x 256 picture, then `--renormalise` puts
its channels' means back on 0.5 without touching the layers this script makes.
"""

import json
import os
import struct
import sys
import zlib

import numpy as np

N = 256
TAU = 2 * np.pi

# --- the kit: tileable noise and shapes on an N x N square ---------------------------------------------------------

U, V = np.meshgrid((np.arange(N) + 0.5) / N, (np.arange(N) + 0.5) / N)  # 0..1 across and down


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def lattice(r, cx, cy):
    """Value noise on a wrapping lattice of cx by cy cells, 0..1."""
    g = r.random((cy, cx))
    x, y = U[0] * cx, V[:, 0] * cy
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = x - x0, y - y0
    fx, fy = (fx * fx * (3 - 2 * fx))[None, :], (fy * fy * (3 - 2 * fy))[:, None]
    x1, y1 = (x0 + 1) % cx, (y0 + 1) % cy
    a, b, c, d = g[np.ix_(y0, x0)], g[np.ix_(y0, x1)], g[np.ix_(y1, x0)], g[np.ix_(y1, x1)]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(r, cx, cy, octaves, gain=0.5):
    """Layered value noise round 0 (about -0.5..0.5); cx != cy gives streaks."""
    out, amp, total = 0, 1.0, 0.0
    for o in range(octaves):
        out = out + amp * lattice(r, min(cx << o, N // 2), min(cy << o, N // 2))
        total += amp
        amp *= gain
    return out / total - 0.5


def blur(a, sigma):
    """A wrapping Gaussian blur."""
    f = np.fft.fftfreq(N)
    g = np.exp(-2 * np.pi ** 2 * sigma ** 2 * (f[None, :] ** 2 + f[:, None] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def grain(r, sigma=0.6):
    """Fine speckle round 0, about -0.5..0.5."""
    a = blur(r.random((N, N)) - 0.5, sigma)
    return a / (np.abs(a).max() * 2)


def voronoi(r, g, jitter=1.0):
    """Cells on a jittered wrapping g x g grid: distance to the nearest point and the next (in cells), and the cell's number."""
    px, py = (r.random((g, g)) - 0.5) * jitter + 0.5, (r.random((g, g)) - 0.5) * jitter + 0.5
    x, y = U * g, V * g
    cx, cy = np.floor(x).astype(int), np.floor(y).astype(int)
    f1, f2, ident = np.full((N, N), 1e9), np.full((N, N), 1e9), np.zeros((N, N), int)
    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            nx, ny = cx + ox, cy + oy
            wx, wy = nx % g, ny % g
            d = np.hypot(x - (nx + px[wy, wx]), y - (ny + py[wy, wx]))
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            ident = np.where(closer, wy * g + wx, ident)
            f1 = np.where(closer, d, f1)
    return f1, f2, ident


def bond(cols, rows, shift=0.5):
    """A running bond of cols x rows blocks: each pixel's block number, and its distance in pixels to the block's
    edge across and down."""
    ry = V * rows
    row = np.floor(ry).astype(int)
    fx = U * cols + shift * (row % 2)
    col = np.floor(fx).astype(int) % cols
    lx, ly = fx - np.floor(fx), ry - row
    return row * cols + col, np.minimum(lx, 1 - lx) * N / cols, np.minimum(ly, 1 - ly) * N / rows


def strokes(r, lum, x, y, ang, length, tone, tip, extra=None, extra_val=None):
    """Short straight strokes painted in order onto lum (wrapping): each from (x, y) along ang, its tone running
    to tone + tip at the far end. `extra` is a second picture given extra_val along each stroke."""
    steps = int(np.ceil(length.max())) + 1
    t = np.linspace(0, 1, steps)[None, :]
    px = np.rint(x[:, None] + np.cos(ang)[:, None] * length[:, None] * t).astype(int) % N
    py = np.rint(y[:, None] + np.sin(ang)[:, None] * length[:, None] * t).astype(int) % N
    lum[py.ravel(), px.ravel()] = (tone[:, None] + tip * (t - 0.4)).ravel()
    if extra is not None:
        extra[py.ravel(), px.ravel()] = np.repeat(extra_val, steps)


def colour(lum, warm=0.0, green=0.0):
    """A multiplier picture from a lightness round 0, pushed warm (red up, blue down) or green."""
    return np.stack([1 + lum + warm, 1 + lum + green, 1 + lum - warm], axis=-1)


# --- the layers ----------------------------------------------------------------------------------------------------

def grass(r):
    lum = fbm(r, 4, 4, 4) * 0.30 - 0.17          # the thatch between the blades
    warm = np.zeros((N, N))
    g, per = 18, 15                               # tufts on a jittered grid, blades fanning out of each
    cx = ((np.arange(g)[None, :] + r.random((g, g))) * N / g).ravel()
    cy = ((np.arange(g)[:, None] + r.random((g, g))) * N / g).ravel()
    n = g * g * per
    x = np.repeat(cx, per) + r.normal(0, 2.2, n)
    y = np.repeat(cy, per) + r.normal(0, 2.2, n)
    order = r.permutation(n)
    x, y = x[order], y[order]
    tone = (r.random(n) - 0.5) * 0.40
    strokes(r, lum, x, y, r.random(n) * TAU, 5 + r.random(n) * 9, tone, 0.62, warm, (r.random(n) - 0.35) * 0.12)
    lum, warm = blur(lum, 0.55), blur(warm, 0.55)
    return colour(lum, warm + lum * 0.08, lum * 0.10 + warm * 0.4)


def litter(r):
    lum = fbm(r, 5, 5, 4) * 0.24 - 0.12           # dark soil under the leaves
    warm = np.zeros((N, N))
    k = np.arange(-9, 10)
    gx, gy = np.meshgrid(k, k)
    for _ in range(620):
        cx, cy, ang = r.integers(0, N), r.integers(0, N), r.random() * TAU
        a, b = 4 + r.random() * 4.5, 1.8 + r.random() * 2.2
        tone, w = -0.10 + r.random() * 0.42, 0.02 + r.random() * 0.13
        xr = gx * np.cos(ang) + gy * np.sin(ang)
        yr = -gx * np.sin(ang) + gy * np.cos(ang)
        inside = (xr / a) ** 2 + (yr / b) ** 2 < 1
        rib = np.abs(yr) < 0.6
        ys, xs = (cy + gy[inside]) % N, (cx + gx[inside]) % N
        lum[ys, xs] = tone - 0.09 * rib[inside] + 0.05 * (yr[inside] / b)
        warm[ys, xs] = w
    n = 46                                         # twigs
    strokes(r, lum, r.random(n) * N, r.random(n) * N, r.random(n) * TAU, 12 + r.random(n) * 26, np.full(n, -0.3), 0.0)
    lum = blur(lum, 0.5)
    return colour(lum, blur(warm, 0.5), 0.0)


def earth(r):
    lum = fbm(r, 3, 3, 5) * 0.42 + grain(r, 0.7) * 0.22
    f1, f2, _ = voronoi(r, 20)
    lum -= 0.17 * (1 - smoothstep(0.0, 0.14, f2 - f1))          # the edges of clods
    p1, _, ident = voronoi(r, 13, 0.9)                           # pebbles in a third of the cells
    g = 13 * 13
    radius = np.where(r.random(g) < 0.34, 0.07 + r.random(g) * 0.13, 0)[ident]
    stone = p1 < radius
    tone = (0.12 + r.random(g) * 0.22)[ident]
    lum = np.where(stone, tone - 0.16 * smoothstep(0.5, 1.0, p1 / np.maximum(radius, 1e-6)), lum)
    return colour(lum, np.where(stone, -0.02, lum * 0.14), 0.0)


def gravel(r):
    g = 38
    f1, f2, ident = voronoi(r, g)
    edge = smoothstep(0.03, 0.2, f2 - f1)
    tone = ((r.random(g * g) - 0.5) * 0.5)[ident]
    lum = tone * edge - 0.36 * (1 - edge) + grain(r, 0.6) * 0.07 + 0.06 * (0.5 - f1)
    return colour(lum, ((r.random(g * g) - 0.5) * 0.10)[ident] * edge, 0.0)


def asphalt(r):
    lum = grain(r, 0.55) * 0.26 + fbm(r, 3, 3, 3) * 0.12
    g = 64                                                       # aggregate: a light chip in a third of 4 px cells
    f1, _, ident = voronoi(r, g)
    radius = np.where(r.random(g * g) < 0.3, 0.16 + r.random(g * g) * 0.3, 0)[ident]
    tone = (0.10 + r.random(g * g) * 0.30)[ident]
    lum = np.where(f1 < radius, tone, lum)
    return colour(blur(lum, 0.4), 0.0, 0.0)


def paving(r):
    ident, dx, dy = bond(4, 4)
    d = np.minimum(dx, dy)
    joint = 1 - smoothstep(0.7, 2.0, d)
    tone = ((r.random(16) - 0.5) * 0.15)[ident]
    lum = tone + grain(r, 0.6) * 0.09 + fbm(r, 6, 6, 3) * 0.10 - 0.05 * smoothstep(7.0, 2.0, d)
    lum = lum * (1 - joint) - 0.34 * joint
    return colour(lum, joint * 0.03, 0.0)


def concrete(r):
    lum = fbm(r, 3, 3, 5) * 0.16 + grain(r, 0.6) * 0.05 + fbm(r, 2, 24, 2) * 0.04
    f1, _, ident = voronoi(r, 24)                                # pores
    g = 24 * 24
    radius = np.where(r.random(g) < 0.22, 0.05 + r.random(g) * 0.09, 0)[ident]
    lum -= 0.2 * (f1 < radius)
    edge = np.minimum(np.minimum(U, 1 - U), np.minimum(V, 1 - V)) * N    # a joint every repeat: the pour's panels
    lum -= 0.13 * (1 - smoothstep(0.4, 1.7, edge))
    return colour(lum, lum * 0.05, 0.0)


def plaster(r):
    lum = fbm(r, 3, 3, 4) * 0.10 + grain(r, 0.7) * 0.045 + fbm(r, 20, 5, 2) * 0.03
    stain = smoothstep(0.08, 0.3, fbm(r, 2, 2, 3))               # a few damp patches
    lum -= 0.05 * stain
    return colour(lum, lum * 0.10 + 0.012 * stain, 0.0)


def brick(r):
    ident, dx, dy = bond(8, 24)
    mortar = 1 - smoothstep(0.55, 1.5, np.minimum(dx, dy))
    n = 8 * 24
    tone = (r.random(n) - 0.5) * 0.26 - 0.26 * (r.random(n) < 0.1)       # one in ten burnt dark
    lum = tone[ident] + grain(r, 0.6) * 0.10 + fbm(r, 12, 12, 2) * 0.07
    warm = ((r.random(n) - 0.5) * 0.08)[ident]
    face = colour(lum, warm, 0.0)
    joint = np.array([1.32, 1.72, 1.9])[None, None, :] * (1 + grain(r, 0.8)[..., None] * 0.12)   # grey over a red base
    return face * (1 - mortar[..., None]) + joint * mortar[..., None]


def boards(r, count, gap, gap_depth, tone_range, grain_depth, lap):
    """Boards running across, `count` down the picture, each with its own tone, grain and butt joints."""
    h = N / count
    row = np.floor(V * count).astype(int)
    ly = V * count - row                                          # 0 the board's top edge, 1 its bottom
    lum = np.zeros((N, N))
    streaks = fbm(r, 3, 64, 3) + 0.5 * fbm(r, 8, 128, 2)
    shift = r.integers(0, N, count)
    joints = [np.sort(r.random(2 if r.random() < 0.5 else 1)) for _ in range(count)]
    tone = (r.random(count) - 0.5) * tone_range
    x = np.arange(N)
    for b in range(count):
        rows = np.where(row[:, 0] == b)[0]
        board = np.roll(streaks[rows], shift[b], axis=1) * grain_depth + tone[b]
        for j in joints[b]:
            jx = int(j * N)
            board[:, (x >= jx)] += (r.random() - 0.5) * tone_range * 0.8       # the next board along
            board[:, jx % N] -= gap_depth * 0.8
            for ny in (0.25, 0.75):                                            # its nails
                yy = int(ny * len(rows))
                board[yy, (jx + 3) % N] -= 0.3
                board[yy, (jx - 3) % N] -= 0.3
        lum[rows] = board
    edge = np.minimum(ly, 1 - ly) * h
    lum = lum * smoothstep(0, gap, edge) - gap_depth * (1 - smoothstep(0, gap, edge))
    lum += lap * (smoothstep(0.35, 0.0, ly) - 1.3 * smoothstep(0.75, 1.0, ly))  # a lit top edge, a shaded bottom one
    return colour(lum + grain(r, 0.5) * 0.03, lum * 0.12, 0.0)


def planks(r):
    return boards(r, 12, 1.3, 0.5, 0.22, 0.26, 0.07)


def floorboards(r):
    return boards(r, 16, 0.9, 0.3, 0.14, 0.17, 0.0)


def tiles(r):
    ident, dx, dy = bond(6, 6, 0.0)
    d = np.minimum(dx, dy)
    grout = 1 - smoothstep(0.5, 1.6, d)
    row, col = ident // 6, ident % 6
    tone = ((r.random(36) - 0.5) * 0.08)[ident] + 0.04 * (((row + col) % 2) - 0.5)
    lum = tone + grain(r, 0.6) * 0.05 + fbm(r, 4, 4, 3) * 0.05 + 0.05 * smoothstep(4.0, 1.5, d)
    lum = lum * (1 - grout) - 0.36 * grout
    return colour(lum, grout * 0.04, 0.0)


def shingles(r):
    rows, cols = 16, 8
    ident, dx, _ = bond(cols, rows)
    ly = V * rows - np.floor(V * rows)                            # 0 the course's top, 1 its butt edge (down the roof)
    tone = ((r.random(rows * cols) - 0.5) * 0.13)[ident]
    lum = tone + grain(r, 0.5) * 0.16 + fbm(r, 3, 3, 3) * 0.07
    lum += 0.07 * smoothstep(0.5, 0.0, ly) - 0.38 * smoothstep(0.8, 1.0, ly)   # the next course's shadow
    lum -= 0.26 * (1 - smoothstep(0.3, 1.2, dx))                               # the slots between tabs
    return colour(lum, lum * 0.04, 0.0)


def sheetmetal(r):
    ribs = 16
    lum = 0.17 * np.cos(U * ribs * TAU) + grain(r, 0.6) * 0.04
    lum -= 0.16 * (1 - smoothstep(0.3, 1.4, np.minimum(U, 1 - U) * N))         # where sheets overlap
    lap = np.abs(V - 0.5) * N
    lum -= 0.14 * (1 - smoothstep(0.4, 1.5, lap))
    crest = np.cos(U * ribs * TAU) > 0.7
    lum -= 0.3 * (crest & (np.abs(lap - 5) < 1.2) & ((np.floor(U * ribs) % 2) == 0))   # fixings
    rust = smoothstep(0.06, 0.3, fbm(r, 20, 3, 3) + 0.4 * fbm(r, 3, 3, 2))     # streaks running down
    lum -= 0.10 * rust
    return colour(lum, 0.10 * rust, 0.0)


def metal(r):
    lum = fbm(r, 3, 3, 3) * 0.06 + grain(r, 0.5) * 0.035
    n = 34                                                        # scratches
    scratch = np.zeros((N, N))
    strokes(r, scratch, r.random(n) * N, r.random(n) * N, r.random(n) * TAU, 6 + r.random(n) * 22, 0.06 + r.random(n) * 0.08, 0.0)
    f1, _, ident = voronoi(r, 12)                                 # chips down to the primer and rust
    radius = np.where(r.random(144) < 0.16, 0.05 + r.random(144) * 0.08, 0)[ident]
    chip = f1 < radius
    lum = np.where(chip, -0.22, lum + blur(scratch, 0.4))
    return colour(lum, 0.10 * chip, 0.0)


def rubber(r):
    lum = grain(r, 0.6) * 0.10 + fbm(r, 4, 4, 3) * 0.06
    lum += 0.07 * np.sign(np.cos(V * 32 * TAU)) * smoothstep(0.0, 0.5, np.abs(np.cos(V * 32 * TAU)))   # ribs across
    return colour(lum, 0.0, 0.0)


def cloth(r):
    px, py = np.floor(U * N), np.floor(V * N)
    over = ((px // 4 + py // 4) % 2) == 0
    lum = 0.11 * np.where(over, np.cos((px % 4 - 1.5) * TAU / 4), np.cos((py % 4 - 1.5) * TAU / 4))
    lum -= 0.05 * ((px % 4 == 0) | (py % 4 == 0))
    lum += fbm(r, 4, 4, 4) * 0.12 + fbm(r, 2, 40, 2) * 0.05 + fbm(r, 40, 2, 2) * 0.05   # wear, uneven threads
    return colour(blur(lum, 0.35), lum * 0.05, 0.0)


def grime(r):
    lum = fbm(r, 3, 3, 4) * 0.20 + fbm(r, 40, 3, 3) * 0.16 + grain(r, 0.8) * 0.05      # smears, and runs down it
    return colour(lum, -lum * 0.06, 0.0)


def bark(r):
    ridges = np.abs(fbm(r, 20, 3, 3)) * 2                         # plates between furrows that run down the trunk
    lum = 0.36 * (smoothstep(0.0, 0.22, ridges) - 0.7) + fbm(r, 40, 10, 2) * 0.12 + grain(r, 0.6) * 0.07
    return colour(lum, lum * 0.10, 0.0)


def fur(r):
    lum = fbm(r, 96, 7, 2) * 0.30 + fbm(r, 48, 4, 2) * 0.16 + fbm(r, 5, 5, 3) * 0.12    # strands, clumps
    return colour(lum, lum * 0.12, 0.0)


# Name, the metres one repeat covers, what it is made for. The order is the layer number: add at the end.
LAYERS = [
    ("grass", 2.0, grass, "tufts of blades; any ground that grows"),
    ("litter", 2.0, litter, "fallen leaves and twigs on dark soil; the forest floor"),
    ("earth", 2.0, earth, "bare soil with clods and pebbles; dirt, fields, beds"),
    ("gravel", 1.0, gravel, "small stones; tracks, flat roofs, rubble"),
    ("asphalt", 2.0, asphalt, "fine speckle with light chips; roads"),
    ("paving", 2.0, paving, "50 cm slabs in a running bond; pavements, yards"),
    ("concrete", 2.0, concrete, "mottled, pored, a joint every 2 m; floors, walls, kerbs, footings"),
    ("plaster", 2.0, plaster, "faint trowel marks and damp; indoor walls, ceilings, render"),
    ("brick", 2.0, brick, "25 x 8 cm bricks, lighter mortar; made for a brick-red base"),
    ("planks", 2.0, planks, "17 cm boards across, lapped; siding, doors (turned), barricades, fences"),
    ("floorboards", 2.0, floorboards, "12.5 cm boards across, tight; wooden floors, furniture tops"),
    ("tiles", 2.0, tiles, "33 cm floor tiles, dark grout, a faint chequer; kitchens, shops"),
    ("shingles", 2.0, shingles, "12.5 cm courses of tabs, butt edges down the picture; pitched roofs"),
    ("sheetmetal", 2.0, sheetmetal, "corrugated sheet, ribs down the picture, rust runs; barn roofs, sheds"),
    ("metal", 1.0, metal, "painted metal: faint mottle, scratches, chips; cars, appliances, tools"),
    ("rubber", 0.5, rubber, "matt grain, ribs across; tyres, soles, mats, hoses"),
    ("cloth", 0.5, cloth, "a coarse plain weave, worn; clothes, upholstery, tarpaulins, bedding"),
    ("grime", 2.0, grime, "smears and runs; glass, and anything left out"),
    ("bark", 1.0, bark, "plates and furrows down the picture; trunks, logs, stumps"),
    ("fur", 0.5, fur, "strands down the picture, clumped; animals' coats, hair, hides"),
]


# --- files ---------------------------------------------------------------------------------------------------------

def normalise(m):
    """A multiplier picture as bytes, halved, each channel's mean on 0.5 exactly (as near as bytes allow)."""
    out = np.empty((N, N, 3))
    for c in range(3):
        ch = m[..., c] / m[..., c].mean() * 0.5
        for _ in range(12):
            ch = np.clip(ch, 0, 1)
            ch += 0.5 - ch.mean()
        out[..., c] = np.clip(ch, 0, 1)
    return np.clip(np.rint(out * 255), 0, 255).astype(np.uint8)


def write_png(path, rgb):
    h, w, _ = rgb.shape

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def read_png(path):
    """An 8-bit RGB or RGBA PNG, not interlaced (what this script and most paint programs write), as RGB."""
    data = open(path, "rb").read()
    at, chunks, idat = 8, {}, b""
    while at < len(data):
        size, kind = struct.unpack(">I4s", data[at:at + 8])
        body = data[at + 8:at + 8 + size]
        if kind == b"IDAT":
            idat += body
        else:
            chunks[kind] = body
        at += 12 + size
    w, h, depth, ctype, _, _, lace = struct.unpack(">IIBBBBB", chunks[b"IHDR"])
    if depth != 8 or ctype not in (2, 6) or lace:
        raise SystemExit(f"{path}: 8-bit RGB or RGBA without interlacing, please")
    bpp = 3 if ctype == 2 else 4
    raw = np.frombuffer(zlib.decompress(idat), np.uint8).reshape(h, 1 + w * bpp)
    out = np.zeros((h, w * bpp), np.int32)
    for y in range(h):
        kind, line = raw[y, 0], raw[y, 1:].astype(np.int32)
        up = out[y - 1] if y else np.zeros(w * bpp, np.int32)
        if kind in (0, 2):
            out[y] = (line + (up if kind == 2 else 0)) & 255
            continue
        for x in range(w * bpp):
            left = out[y, x - bpp] if x >= bpp else 0
            corner = up[x - bpp] if x >= bpp else 0
            if kind == 1:
                pred = left
            elif kind == 3:
                pred = (left + up[x]) >> 1
            else:
                p = left + up[x] - corner
                pa, pb, pc = abs(p - left), abs(p - up[x]), abs(p - corner)
                pred = left if pa <= pb and pa <= pc else up[x] if pb <= pc else corner
            out[y, x] = (line[x] + pred) & 255
    return out.reshape(h, w, bpp)[..., :3].astype(np.uint8)


def sheet(path, pictures):
    """Every layer side by side, each tiled 2 x 2 at half size so seams would show, on the grey a colour would be."""
    cols = 5
    rows = (len(pictures) + cols - 1) // cols
    out = np.full((rows * (N + 8) + 8, cols * (N + 8) + 8, 3), 40, np.uint8)
    for i, p in enumerate(pictures):
        half = ((p[0::2, 0::2].astype(int) + p[1::2, 0::2] + p[0::2, 1::2] + p[1::2, 1::2]) // 4).astype(np.uint8)
        y, x = 8 + (i // cols) * (N + 8), 8 + (i % cols) * (N + 8)
        out[y:y + N, x:x + N] = np.tile(half, (2, 2, 1))
    write_png(path, out)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    out = os.path.join(root, "godot", "assets", "surfaces")
    sheet_path, renormalise = None, False
    for i, a in enumerate(args):
        if a == "--out":
            out = args[i + 1]
        if a == "--sheet":
            sheet_path = args[i + 1]
        if a == "--renormalise":
            renormalise = True
    os.makedirs(out, exist_ok=True)
    pictures = []
    for name, metres, make, _ in LAYERS:
        path = os.path.join(out, name + ".png")
        if renormalise and os.path.exists(path):
            rgb = read_png(path)
            if rgb.shape != (N, N, 3):
                raise SystemExit(f"{path}: {N} x {N}, please")
            rgb = normalise(rgb.astype(float) / 127.5)
        else:
            rgb = normalise(make(np.random.default_rng(zlib.crc32(name.encode()))))
        write_png(path, rgb)
        pictures.append(rgb)
        means = rgb.reshape(-1, 3).mean(axis=0)
        print(f"{name:12} mean {means[0]:.1f} {means[1]:.1f} {means[2]:.1f}  spread {rgb.std() / 127.5:.3f}")
    with open(os.path.join(out, "layers.json"), "w", newline="\n") as f:
        json.dump({"size": N, "layers": [{"name": n, "metres": m, "for": what} for n, m, _, what in LAYERS]}, f, indent=1)
        f.write("\n")
    if sheet_path:
        sheet(sheet_path, pictures)
    print(f"{len(LAYERS)} layers in {out}")


if __name__ == "__main__":
    main()
