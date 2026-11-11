# Furniture in first person: one low-poly model per furniture kind and look, all by script, run headless
# (~2 s):
#
#   "$B" --background --factory-startup --python art/scripts/build_furniture.py -- \
#       --glb godot/assets/furniture/furniture.glb [--preview lineup.png]
#
# What the sim has (cs/Sim/Items/FurnitureMod.cs, Designations.FURNITURE_TARGETS, ScenarioMod.SIMPLE_FURNITURE,
# Buildings.cs, Lots.cs):
#   kinds: bed, table, workbench, stove, trap, tower, well, collector, generator, lamp, fridge, heater, coop,
#     radio, lab, beacon, turret, lure, pit, truck, crate (trees are Trees.cs's);
#   a crate's look (CONTAINER_LOOKS): counter, icebox, shelf, wardrobe, dresser, cabinet, toolbox, locker,
#     desk, pallet, sack, crate, sink, bin, nightstand, bookcase;
#   a decor's look (DECOR_LOOKS): sofa, bed, toilet, bath, table, oven, tv, car, pew, altar, hay, tractor, pump,
#     bench, cot, register, machine, silo, shower, armchair, washer.
# A model is named after its kind, or its look for a crate or decor with one (a decor bed is the bed, a
# decor table the table). Variants are "<name>_v1", "_v2"..., picked in the game by a hash of the id.
#
# Like build_items.py: boxes, cylinders and wedges, flat shaded, vertex colours (one material the game
# replaces: godot/World3D/Furniture.cs) with metalness in the vertex alpha.
#
# Blender space: Z up, resting on z = 0, the footprint centred on the origin, the FRONT towards -Y (in
# Godot +Z): what stands against a wall has its back to +Y, and the game turns it so its back is to the
# wall. Width along X. Real sizes in metres (1 tile = 1 m): a bed, a sofa or a car overhangs its tile.
# No randomness: a rebuild gives the same models.

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
# What each colour is made of (its detail layer) and mul, which keeps it, come from the shared kit.
from lowpoly import mat, mul, plain, write_surfaces  # noqa: E402


def lin(r, g, b):
    """An sRGB colour (0-255) as linear (the colour attribute is linear)."""
    return tuple((c / 255) ** 2.2 for c in (r, g, b))


# --- the palette: faded, a world gone a few years without paint ---

# A colour made of something wears that detail (lowpoly.MATERIALS); a plain one with metalness wears painted metal,
# without none (paint, lights, labels).
WOOD = mat("wood", lin(140, 100, 62))
WOOD_D = mat("wood", lin(98, 68, 42))
WOOD_L = mat("wood", lin(178, 140, 96))
PINE = mat("wood", lin(190, 156, 108))
OAK = mat("wood", lin(120, 84, 52))
STEEL = mat("metal", lin(150, 152, 156))
STEEL_D = mat("metal", lin(78, 80, 86))
GREY = lin(122, 124, 122)
RUST = mat("metal", lin(124, 70, 42))
BLACK = lin(30, 30, 32)
WHITE = lin(222, 220, 212)
ENAMEL = mat("metal", lin(208, 206, 196))
CREAM = lin(216, 204, 172)
RED = lin(160, 36, 30)
ORANGE = lin(204, 116, 34)
YELLOW = lin(196, 160, 40)
BRASS = lin(180, 146, 70)
OLIVE = mat("cloth", lin(86, 92, 58))
GREEN = lin(60, 96, 62)
BLUE = lin(52, 78, 120)
TARP = mat("cloth", lin(66, 86, 96))
ROPE = mat("fine", lin(170, 150, 104))
CONCRETE = mat("concrete", lin(140, 138, 130))
BRICK = mat("brick", lin(128, 64, 48))
STONE = mat("concrete", lin(118, 114, 104))
SHEET = mat("cloth", lin(196, 190, 176))
GLASS = plain(lin(70, 90, 100))
DIRT = mat("earth", lin(84, 66, 48))
HAY = mat("straw", lin(196, 168, 92))
RUBBER = mat("rubber", BLACK)
CARD = lin(168, 132, 86)


class Geo:
    """Faces with a colour and a metalness each, gathered as plain lists."""

    def __init__(self):
        self.v = []
        self.f = []  # (vertex indices, colour, metal)

    def add(self, verts, faces, colours, metal, m):
        base = len(self.v)
        self.v.extend(m @ Vector(p) for p in verts)
        for k, f in enumerate(faces):
            c = colours[k] if isinstance(colours, list) else colours
            self.f.append(([base + i for i in f], c, metal))

    def tris(self):
        return sum(len(i) - 2 for i, _, _ in self.f)


def place(loc=(0, 0, 0), rz=0.0, ry=0.0, rx=0.0):
    """A transform: rotate about X, then Y, then Z (degrees), then move."""
    return (Matrix.Translation(loc) @ Matrix.Rotation(math.radians(rz), 4, "Z")
            @ Matrix.Rotation(math.radians(ry), 4, "Y") @ Matrix.Rotation(math.radians(rx), 4, "X"))


def box(g, size, loc=(0, 0, 0), col=WOOD, metal=0.0, rz=0.0, ry=0.0, rx=0.0, top=None, taper=1.0, front=None):
    """A box of size (x, y, z) whose bottom centre is at loc; `top` colours its top face, `front` its -Y face,
    `taper` shrinks the top's x and y."""
    sx, sy, sz = (s / 2 for s in size)
    tx, ty = sx * taper, sy * taper
    v = [(-sx, -sy, 0), (sx, -sy, 0), (sx, sy, 0), (-sx, sy, 0),
         (-tx, -ty, 2 * sz), (tx, -ty, 2 * sz), (tx, ty, 2 * sz), (-tx, ty, 2 * sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    cols = [mul(col, 0.8), top or col, front or col, mul(col, 0.92), mul(col, 0.96), mul(col, 0.92)]
    g.add(v, f, cols, metal, place(loc, rz, ry, rx))


def cyl(g, r, length, loc=(0, 0, 0), col=STEEL, metal=0.0, seg=8, axis="z", rz=0.0, ry=0.0, rx=0.0,
        caps=None, r2=None):
    """A cylinder (or cone, with r2) of radius r: along Z standing on loc, or along X (axis "x") or Y
    (axis "y") lying with its axis at loc's height, centred on loc. `caps` colours both ends."""
    r2 = r if r2 is None else r2
    v, f, cols = [], [], []
    for k in range(seg):
        a = 2 * math.pi * (k + 0.5) / seg
        v.append((r * math.cos(a), r * math.sin(a), 0))
        v.append((r2 * math.cos(a), r2 * math.sin(a), length))
    for k in range(seg):
        a0, b0, a1, b1 = 2 * k, 2 * k + 1, 2 * ((k + 1) % seg), 2 * ((k + 1) % seg) + 1
        f.append((a0, a1, b1, b0))
        cols.append(mul(col, 0.86 + 0.14 * math.cos(2 * math.pi * (k + 1) / seg - 0.8)))
    f.append(tuple(2 * k + 1 for k in range(seg)))
    f.append(tuple(2 * k for k in reversed(range(seg))))
    cols += [caps or col, mul(caps or col, 0.8)]
    m = place(loc, rz, ry, rx)
    if axis == "x":
        m = m @ Matrix.Rotation(math.radians(90), 4, "Y") @ Matrix.Translation((0, 0, -length / 2))
    elif axis == "y":
        m = m @ Matrix.Rotation(math.radians(-90), 4, "X") @ Matrix.Translation((0, 0, -length / 2))
    g.add(v, f, cols, metal, m)


def legs(g, w, d, h, t=0.05, col=WOOD_D, inset=0.04, metal=0.0):
    """Four square legs under a top of w x d, h high."""
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (t, t, h), (sx * (w / 2 - inset - t / 2), sy * (d / 2 - inset - t / 2), 0), col, metal=metal)


def doors(g, w, h, z, y, n, col, handle=STEEL, gap=0.012, metal=0.0, knob_z=None):
    """n door (or drawer) faces across a front of width w at depth y (the front plane), from z to z + h."""
    dw = w / n
    for k in range(n):
        x = -w / 2 + dw * (k + 0.5)
        box(g, (dw - gap, 0.015, h - gap), (x, y - 0.0075, z + gap / 2), col, metal=metal)
        hx = x + (dw * 0.32 if n > 1 and k % 2 == 0 else -dw * 0.32) if n > 1 else x + dw * 0.35
        kz = knob_z if knob_z is not None else z + h * 0.5
        box(g, (0.02, 0.02, 0.1 if h > 0.3 else 0.02), (hx, y - 0.02, kz - 0.05 if h > 0.3 else kz), handle, metal=1)


def drawers(g, w, h, z, y, n, col, handle=BRASS):
    """n drawers stacked from z to z + h across a front of width w at depth y."""
    dh = h / n
    for k in range(n):
        box(g, (w - 0.02, 0.015, dh - 0.012), (0, y - 0.0075, z + dh * k + 0.006), col)
        box(g, (0.12, 0.02, 0.018), (0, y - 0.02, z + dh * (k + 0.5)), handle, metal=1)


def wheel(g, r, w, loc, col=RUBBER, hub=STEEL, seg=10):
    cyl(g, r, w, loc, col, seg=seg, axis="x", caps=mul(col, 1.4))
    cyl(g, r * 0.5, w + 0.01, loc, hub, metal=1, seg=seg, axis="x")


# --- sleeping ---

def bed(g):
    """A single bed, head to the wall (+Y): a wooden frame and headboard, mattress, pillow, a blanket half off."""
    W, L = 0.95, 1.9
    box(g, (W, L, 0.22), (0, 0, 0.12), WOOD)
    legs(g, W, L, 0.14, 0.06, WOOD_D, 0.0)
    box(g, (W + 0.04, 0.05, 0.95), (0, L / 2, 0), WOOD_D, top=WOOD)
    box(g, (W + 0.04, 0.05, 0.5), (0, -L / 2, 0), WOOD_D, top=WOOD)
    box(g, (W - 0.06, L - 0.1, 0.18), (0, 0, 0.34), SHEET, taper=0.98)
    box(g, (0.62, 0.36, 0.12), (0, L / 2 - 0.26, 0.52), mat("cloth", WHITE), taper=0.85)
    box(g, (W - 0.02, 1.1, 0.06), (0, -0.33, 0.5), mat("cloth", lin(96, 70, 62)), rx=-2)
    box(g, (W + 0.02, 0.4, 0.05), (0, -0.62, 0.53), mat("cloth", lin(110, 82, 70)), rz=8)


def bed_v1(g):
    """An iron-framed bed with a grey wool blanket and a bare striped mattress showing."""
    W, L = 0.95, 1.9
    for sy in (-1, 1):
        h = 1.0 if sy > 0 else 0.7
        for sx in (-1, 1):
            cyl(g, 0.02, h, (sx * W / 2, sy * L / 2, 0), STEEL_D, metal=0.8, seg=6)
        box(g, (W, 0.03, 0.03), (0, sy * L / 2, h - 0.03), STEEL_D, metal=0.8)
        box(g, (W, 0.03, 0.03), (0, sy * L / 2, 0.3), STEEL_D, metal=0.8)
        for k in range(5):
            cyl(g, 0.008, h - 0.33, (-W / 2 + W * (k + 1) / 6, sy * L / 2, 0.32), STEEL_D, metal=0.8, seg=4)
    for sx in (-1, 1):
        box(g, (0.03, L, 0.04), (sx * W / 2, 0, 0.3), STEEL_D, metal=0.8)
    box(g, (W - 0.04, L - 0.06, 0.16), (0, 0, 0.32), mat("cloth", lin(170, 160, 140)), top=mat("cloth", lin(184, 176, 150)))
    box(g, (W, 1.2, 0.05), (0, -0.3, 0.47), mat("cloth", lin(100, 102, 98)))
    box(g, (0.6, 0.34, 0.11), (0, L / 2 - 0.25, 0.48), mat("cloth", CREAM), taper=0.85)


def cot(g):
    """A folding camp cot: a green canvas sling on an X-legged aluminium frame."""
    W, L = 0.7, 1.9
    for sx in (-1, 1):
        box(g, (0.03, L, 0.03), (sx * W / 2, 0, 0.38), STEEL, metal=1)
    for y in (-0.8, 0.0, 0.8):
        for a in (35, -35):
            box(g, (W, 0.025, 0.025), (0, y, 0.2), STEEL, metal=1, rx=a, ry=0)
        box(g, (0.025, 0.025, 0.42), (-W / 2, y, 0), STEEL, metal=1, rx=30)
        box(g, (0.025, 0.025, 0.42), (W / 2, y, 0), STEEL, metal=1, rx=-30)
    box(g, (W - 0.02, L - 0.04, 0.02), (0, 0, 0.38), OLIVE)
    box(g, (0.5, 0.7, 0.09), (0.02, -0.3, 0.4), mat("cloth", lin(80, 60, 50)), rz=4, taper=0.9)


# --- tables and seats ---

def table(g):
    """A kitchen table: a plain wooden top on four legs, a chair pushed under each long side."""
    W, D, H = 1.2, 0.8, 0.75
    box(g, (W, D, 0.04), (0, 0, H - 0.04), WOOD, top=WOOD_L)
    legs(g, W, D, H - 0.04, 0.05, WOOD_D)
    box(g, (W - 0.12, 0.03, 0.08), (0, D / 2 - 0.06, H - 0.12), WOOD_D)
    box(g, (W - 0.12, 0.03, 0.08), (0, -D / 2 + 0.06, H - 0.12), WOOD_D)
    chair(g, (-0.25, -0.5, 0), 0)
    chair(g, (0.28, 0.48, 0), 180)
    # A mug and a plate left on it.
    cyl(g, 0.04, 0.09, (0.3, -0.1, H), WHITE, seg=8)
    cyl(g, 0.11, 0.015, (-0.2, 0.1, H), CREAM, seg=10)


def chair(g, loc, rz, col=WOOD_D):
    sub = Geo()
    box(sub, (0.42, 0.42, 0.04), (0, 0, 0.43), col, top=WOOD)
    legs(sub, 0.42, 0.42, 0.43, 0.035, col, 0.0)
    for sx in (-1, 1):
        box(sub, (0.035, 0.035, 0.45), (sx * 0.19, -0.19, 0.47), col)
    box(sub, (0.42, 0.03, 0.14), (0, -0.19, 0.76), col)
    m = place(loc, rz)
    base = len(g.v)
    g.v.extend(m @ v for v in sub.v)
    g.f.extend(([base + i for i in idx], c, mt) for idx, c, mt in sub.f)


def table_v1(g):
    """A round dining table on a pedestal, with two chairs."""
    cyl(g, 0.5, 0.04, (0, 0, 0.71), WOOD, seg=14, caps=WOOD_L)
    cyl(g, 0.06, 0.71, (0, 0, 0), WOOD_D, seg=8)
    for a in (0, 120, 240):
        box(g, (0.5, 0.06, 0.05), (0.18 * math.cos(math.radians(a)), 0.18 * math.sin(math.radians(a)), 0), WOOD_D, rz=a)
    chair(g, (0, -0.55, 0), 0, OAK)
    chair(g, (0.5, 0.25, 0), 115, OAK)


def bench(g):
    """A park bench: wooden slats on cast-iron ends."""
    W = 1.5
    for sx in (-1, 1):
        box(g, (0.05, 0.5, 0.42), (sx * (W / 2 - 0.1), 0, 0), BLACK, metal=0.7, taper=0.94)
        box(g, (0.05, 0.05, 0.42), (sx * (W / 2 - 0.1), 0.2, 0.42), BLACK, metal=0.7, rx=-12)
        box(g, (0.05, 0.4, 0.04), (sx * (W / 2 - 0.1), -0.02, 0.64), BLACK, metal=0.7)
    for k in range(4):
        box(g, (W, 0.1, 0.03), (0, -0.18 + 0.12 * k, 0.42), [WOOD, WOOD_L][k % 2])
    for k in range(3):
        box(g, (W, 0.025, 0.09), (0, 0.24 + 0.02 * k, 0.55 + 0.12 * k), [WOOD, WOOD_L][k % 2], rx=-12)


def pew(g):
    """A church pew, one metre of it: pews in a row of tiles join up into one long one."""
    W = 1.0
    box(g, (W, 0.4, 0.05), (0, -0.04, 0.42), OAK, top=WOOD)
    box(g, (W, 0.05, 0.55), (0, 0.18, 0.42), OAK, rx=-8, top=WOOD)
    box(g, (W, 0.04, 0.4), (0, 0.1, 0.0), WOOD_D)
    for sx in (-1, 1):
        box(g, (0.05, 0.45, 0.42), (sx * (W / 2 - 0.15), 0, 0), WOOD_D)
    # The book rail on the back.
    box(g, (W, 0.12, 0.03), (0, 0.29, 0.72), OAK)


def sofa(g, col=mat("cloth", lin(92, 84, 96))):
    """A three-seat sofa against the wall: a base, three seat cushions, a back and arms."""
    W, D = 1.9, 0.85
    box(g, (W, D, 0.25), (0, 0, 0.08), mul(col, 0.85))
    for sx in (-1, 1):
        box(g, (0.05, 0.05, 0.08), (sx * (W / 2 - 0.08), -D / 2 + 0.08, 0), WOOD_D)
        box(g, (0.05, 0.05, 0.08), (sx * (W / 2 - 0.08), D / 2 - 0.08, 0), WOOD_D)
        box(g, (0.18, D, 0.38), (sx * (W / 2 - 0.09), 0, 0.25), col, top=mul(col, 1.1), taper=0.97)
    for k in range(3):
        box(g, ((W - 0.36) / 3 - 0.02, D - 0.25, 0.15), (-0.51 + 0.51 * k, -0.11, 0.33), col, top=mul(col, 1.12), taper=0.96)
        box(g, ((W - 0.36) / 3 - 0.02, 0.2, 0.45), (-0.51 + 0.51 * k, D / 2 - 0.16, 0.33), mul(col, 1.05), rx=-10, taper=0.94)
    box(g, (W, 0.15, 0.55), (0, D / 2 - 0.075, 0.25), mul(col, 0.9))


def sofa_v1(g):
    """The same sofa in worn brown leather, a blanket thrown over one end."""
    sofa(g, mat("leather", lin(96, 62, 40)))
    box(g, (0.6, 0.75, 0.03), (0.55, -0.05, 0.48), mat("cloth", lin(140, 120, 70)), rz=6)


def armchair(g, col=mat("cloth", lin(120, 96, 64))):
    """An armchair: base, seat cushion, back and two arms."""
    W, D = 0.85, 0.85
    box(g, (W, D, 0.25), (0, 0, 0.08), mul(col, 0.85))
    legs(g, W, D, 0.08, 0.05, WOOD_D, 0.04)
    for sx in (-1, 1):
        box(g, (0.16, D, 0.36), (sx * (W / 2 - 0.08), 0, 0.25), col, top=mul(col, 1.1), taper=0.96)
    box(g, (W - 0.32, D - 0.22, 0.14), (0, -0.1, 0.33), col, top=mul(col, 1.12), taper=0.96)
    box(g, (W - 0.32, 0.18, 0.5), (0, D / 2 - 0.15, 0.33), mul(col, 1.04), rx=-10, taper=0.92)
    box(g, (W, 0.14, 0.62), (0, D / 2 - 0.07, 0.25), mul(col, 0.9), taper=0.97)


def armchair_v1(g):
    """A faded green armchair."""
    armchair(g, mat("cloth", lin(78, 96, 72)))


# --- storage ---

def crate(g):
    """A slatted wooden crate with a lid."""
    W, D, H = 0.7, 0.55, 0.5
    box(g, (W - 0.02, D - 0.02, H - 0.02), (0, 0, 0.01), WOOD_D)
    for k in range(3):
        z = 0.02 + k * 0.16
        box(g, (W, 0.02, 0.13), (0, -D / 2 + 0.01, z), [PINE, WOOD_L][k % 2])
        box(g, (W, 0.02, 0.13), (0, D / 2 - 0.01, z), [WOOD_L, PINE][k % 2])
        box(g, (0.02, D, 0.13), (-W / 2 + 0.01, 0, z), [PINE, WOOD_L][k % 2])
        box(g, (0.02, D, 0.13), (W / 2 - 0.01, 0, z), [WOOD_L, PINE][k % 2])
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (0.05, 0.05, H), (sx * (W / 2 - 0.005), sy * (D / 2 - 0.005), 0), WOOD)
    box(g, (W + 0.02, D + 0.02, 0.03), (0, 0, H), PINE, top=WOOD_L)
    box(g, (0.6, 0.004, 0.08), (0, -D / 2 - 0.012, 0.22), lin(40, 40, 40))


def crate_v1(g):
    """Two cardboard boxes stacked, the top one turned and its flaps open."""
    c = mat("paper", CARD)
    box(g, (0.6, 0.45, 0.4), (0, 0, 0), c, top=mul(c, 1.06))
    box(g, (0.6, 0.06, 0.004), (0, 0, 0.4), lin(190, 170, 120))
    box(g, (0.48, 0.38, 0.32), (0.04, 0.0, 0.4), mul(c, 0.94), rz=12, top=mul(c, 0.6))
    box(g, (0.48, 0.16, 0.006), (0.0, -0.22, 0.7), c, rz=12, rx=-55)
    box(g, (0.48, 0.16, 0.006), (0.08, 0.22, 0.7), c, rz=12, rx=55)


def crate_v2(g):
    """A green steel ammo-style chest with stencilled lid and handles."""
    c = lin(70, 82, 56)
    box(g, (0.8, 0.45, 0.4), (0, 0, 0), c, metal=0.4, top=mul(c, 1.1))
    box(g, (0.82, 0.47, 0.05), (0, 0, 0.36), mul(c, 0.85), metal=0.4)
    for sx in (-1, 1):
        box(g, (0.03, 0.12, 0.03), (sx * 0.41, 0, 0.25), BLACK, metal=1)
    box(g, (0.4, 0.004, 0.08), (0, -0.226, 0.18), lin(210, 200, 160))
    for sx in (-1, 1):
        box(g, (0.05, 0.02, 0.08), (sx * 0.25, -0.235, 0.33), STEEL_D, metal=1)


def counter(g, body=mat("painted", lin(196, 186, 160)), top=lin(120, 120, 112)):
    """A kitchen counter: a base cabinet with two doors, a plinth, a worktop overhanging the front."""
    W, D, H = 1.0, 0.6, 0.9
    box(g, (W, D - 0.06, 0.1), (0, 0.03, 0), BLACK)
    box(g, (W, D - 0.02, H - 0.14), (0, 0.01, 0.1), body)
    doors(g, W - 0.02, H - 0.36, 0.11, -D / 2 + 0.02, 2, body, STEEL, knob_z=H - 0.4)
    drawers(g, W - 0.02, 0.18, H - 0.25, -D / 2 + 0.02, 1, mul(body, 0.97), STEEL)
    box(g, (W, D, 0.04), (0, 0, H - 0.04), top, top=mul(top, 1.1))
    # A kettle, a jar.
    cyl(g, 0.08, 0.18, (0.3, 0.1, H), STEEL, metal=1, seg=8, r2=0.06)
    cyl(g, 0.05, 0.14, (-0.32, 0.15, H), GLASS, seg=8, caps=RED)


def counter_v1(g):
    """A shop counter: a varnished front panel, a glass-topped display."""
    W, D, H = 1.0, 0.6, 0.95
    box(g, (W, D, H - 0.04), (0, 0, 0), OAK)
    for k in range(3):
        box(g, (0.02, 0.02, H - 0.2), (-W / 2 + W * (k + 0.5) / 3 + 0.15, -D / 2 - 0.005, 0.1), WOOD_D)
    box(g, (W, 0.02, 0.06), (0, -D / 2 - 0.005, 0.0), BLACK)
    box(g, (W + 0.04, D + 0.04, 0.04), (0, 0, H - 0.04), WOOD_D, top=lin(110, 122, 124))
    box(g, (0.3, 0.2, 0.06), (0.25, 0.05, H), lin(160, 40, 36), top=WHITE)


def icebox(g):
    """A kitchen fridge-freezer: almond enamel, the freezer door above, chrome handles."""
    W, D, H = 0.7, 0.68, 1.75
    c = mat("metal", lin(214, 200, 168))
    box(g, (W, D, H), (0, 0, 0), c, top=mul(c, 1.03))
    box(g, (W - 0.01, 0.03, 0.55), (0, -D / 2 - 0.01, H - 0.57), mul(c, 1.03))
    box(g, (W - 0.01, 0.03, 1.1), (0, -D / 2 - 0.01, 0.06), mul(c, 1.03))
    box(g, (0.03, 0.04, 0.3), (W / 2 - 0.07, -D / 2 - 0.04, H - 0.48), STEEL, metal=1)
    box(g, (0.03, 0.04, 0.45), (W / 2 - 0.07, -D / 2 - 0.04, 0.6), STEEL, metal=1)
    box(g, (W - 0.01, 0.03, 0.06), (0, -D / 2 - 0.01, 0.0), BLACK)
    # Magnets and a curled note.
    box(g, (0.1, 0.005, 0.14), (-0.15, -D / 2 - 0.03, 1.1), WHITE, rz=0)


def fridge(g):
    """A built fridge (the colony's: wired to the generator): a plain white upright with one door, an extension lead off the back."""
    W, D, H = 0.6, 0.62, 1.5
    paint = mat("metal", WHITE)
    box(g, (W, D, H), (0, 0, 0.05), paint, top=mul(paint, 1.04))
    legs(g, W, D, 0.05, 0.04, BLACK, 0.03)
    box(g, (W - 0.01, 0.03, H - 0.04), (0, -D / 2 - 0.01, 0.07), mul(paint, 1.02))
    box(g, (0.03, 0.04, 0.5), (-W / 2 + 0.07, -D / 2 - 0.04, 0.8), STEEL_D, metal=1)
    box(g, (0.4, 0.05, 0.4), (0, D / 2 + 0.03, 0.15), BLACK, metal=0.6)
    box(g, (0.02, 0.4, 0.02), (0.2, D / 2 + 0.22, 0.0), ORANGE)


def shelf(g):
    """Grey steel shelving: four shelves on angle posts, tins and boxes on them."""
    W, D, H = 1.0, 0.42, 1.85
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (0.035, 0.035, H), (sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), 0), GREY, metal=0.8)
    for k, z in enumerate((0.1, 0.55, 1.0, 1.45, 1.82)):
        box(g, (W, D, 0.025), (0, 0, z), GREY, metal=0.6, top=mul(GREY, 1.08))
    stock = [(0.1, (-0.3, 0, 0.125), (0.16, 0.24, 0.2), mat("paper", lin(150, 120, 80))),
             (0.1, (0.05, 0, 0.125), (0.3, 0.3, 0.26), mat("paper", CARD)),
             (0.55, (-0.25, 0.0, 0.575), (0.22, 0.22, 0.2), lin(60, 80, 120)),
             (1.0, (0.2, 0.02, 1.025), (0.36, 0.28, 0.18), lin(150, 46, 40)),
             (1.45, (-0.2, 0.02, 1.475), (0.3, 0.3, 0.24), mat("paper", CARD))]
    for _, loc, size, c in stock:
        box(g, size, loc, c)
    for k in range(4):
        cyl(g, 0.045, 0.12, (0.15 + 0.1 * k, -0.08, 0.575), [RED, GREEN, ORANGE, RED][k], seg=8, caps=STEEL)
    for k in range(3):
        cyl(g, 0.04, 0.2, (-0.38 + 0.09 * k, 0.0, 1.025), [WHITE, BLUE, YELLOW][k], seg=8)


def shelf_v1(g):
    """A wooden shop shelf: a back panel and four shelves, jars and packets, half emptied."""
    W, D, H = 1.0, 0.38, 1.8
    box(g, (W, 0.03, H), (0, D / 2 - 0.015, 0), WOOD)
    for sx in (-1, 1):
        box(g, (0.03, D, H), (sx * (W / 2 - 0.015), 0, 0), WOOD_D)
    for z in (0.08, 0.5, 0.92, 1.34, 1.77):
        box(g, (W - 0.06, D - 0.03, 0.03), (0, -0.015, z), WOOD_L)
    box(g, (W, 0.03, 0.08), (0, -D / 2 + 0.015, 0), WOOD_D)
    for k in range(5):
        cyl(g, 0.04, 0.14, (-0.38 + 0.1 * k, -0.04, 0.53), GLASS, seg=8, caps=[RED, BRASS, RED, BRASS, RED][k])
    for k in range(3):
        box(g, (0.12, 0.08, 0.2), (0.1 + 0.13 * k, 0.0, 0.95), [YELLOW, BLUE, lin(196, 100, 60)][k])
    box(g, (0.22, 0.25, 0.14), (-0.25, 0.0, 1.37), mat("paper", CARD), rz=10)


def wardrobe(g):
    """A tall wooden wardrobe: two doors, brass handles, a cornice, a suitcase on top."""
    W, D, H = 1.0, 0.58, 1.95
    box(g, (W, D, H - 0.08), (0, 0, 0.08), OAK)
    box(g, (W - 0.04, D - 0.04, 0.08), (0, 0, 0), WOOD_D)
    box(g, (W + 0.06, D + 0.04, 0.06), (0, -0.01, H - 0.06), WOOD_D, top=OAK)
    doors(g, W - 0.04, H - 0.32, 0.18, -D / 2, 2, mul(OAK, 1.08), BRASS)
    drawers(g, W - 0.04, 0.12, 0.08, -D / 2, 1, mul(OAK, 0.95))
    box(g, (0.6, 0.4, 0.2), (0.05, 0.02, H), mat("leather", lin(80, 50, 40)), rz=-4)


def dresser(g):
    """A chest of drawers: four drawers, brass pulls, a mirror on the wall side and a lamp on top."""
    W, D, H = 0.9, 0.46, 0.85
    box(g, (W, D, H - 0.06), (0, 0, 0.06), WOOD)
    legs(g, W, D, 0.06, 0.05, WOOD_D, 0.0)
    drawers(g, W - 0.04, H - 0.12, 0.08, -D / 2, 4, mul(WOOD, 1.1))
    box(g, (W + 0.02, D + 0.02, 0.03), (0, 0, H - 0.03), WOOD_D, top=WOOD)
    box(g, (0.6, 0.03, 0.6), (0, D / 2 - 0.03, H), WOOD_D)
    box(g, (0.52, 0.01, 0.52), (0, D / 2 - 0.05, H + 0.04), plain(lin(130, 150, 160)), metal=1)
    cyl(g, 0.06, 0.04, (-0.32, -0.05, H), BRASS, metal=1, seg=8)
    cyl(g, 0.012, 0.25, (-0.32, -0.05, H + 0.04), BRASS, metal=1, seg=6)
    cyl(g, 0.12, 0.16, (-0.32, -0.05, H + 0.24), CREAM, seg=8, r2=0.07)


def nightstand(g):
    """A bedside table: a drawer over an open shelf, a lamp and a book on top."""
    W, D, H = 0.45, 0.4, 0.55
    box(g, (W, D, 0.03), (0, 0, H - 0.03), WOOD_D, top=WOOD)
    for sx in (-1, 1):
        box(g, (0.03, D, H - 0.03), (sx * (W / 2 - 0.015), 0, 0), WOOD)
    box(g, (W, 0.02, H - 0.03), (0, D / 2 - 0.01, 0), WOOD_D)
    box(g, (W - 0.06, D, 0.02), (0, 0, 0.06), WOOD)
    box(g, (W - 0.06, D - 0.02, 0.02), (0, 0, 0.32), WOOD)
    drawers(g, W - 0.06, 0.16, 0.34, -D / 2, 1, mul(WOOD, 1.1))
    cyl(g, 0.06, 0.03, (0.08, 0.06, H), CREAM, seg=8)
    cyl(g, 0.01, 0.2, (0.08, 0.06, H + 0.03), BRASS, metal=1, seg=6)
    cyl(g, 0.11, 0.14, (0.08, 0.06, H + 0.2), lin(200, 180, 140), seg=8, r2=0.07)
    box(g, (0.15, 0.2, 0.03), (-0.1, -0.05, H), lin(40, 60, 100), rz=15)


def bookcase(g):
    """A wooden bookcase: five shelves of books in faded spines, some fallen over."""
    W, D, H = 0.9, 0.3, 1.9
    box(g, (W, 0.02, H), (0, D / 2 - 0.01, 0), WOOD_D)
    for sx in (-1, 1):
        box(g, (0.025, D, H), (sx * (W / 2 - 0.0125), 0, 0), WOOD)
    zs = (0.06, 0.42, 0.78, 1.14, 1.5, 1.88)
    for z in zs:
        box(g, (W - 0.05, D - 0.02, 0.025), (0, -0.01, z - 0.025 if z > 1.8 else z), WOOD)
    spines = [lin(120, 40, 36), lin(40, 60, 100), lin(70, 90, 60), lin(160, 140, 90), lin(60, 50, 60), lin(150, 90, 50)]
    for s, z in enumerate(zs[:5]):
        x = -W / 2 + 0.04
        k = s * 3
        while x < W / 2 - 0.12 - (0.2 if s % 2 else 0):
            w = 0.03 + 0.015 * ((k * 7) % 3)
            h = 0.22 + 0.03 * ((k * 5) % 4)
            box(g, (w, 0.2, h), (x + w / 2, -0.02, z + 0.025), spines[k % len(spines)])
            x += w + 0.004
            k += 1
        if s % 2:
            box(g, (0.22, 0.17, 0.035), (W / 2 - 0.15, -0.02, z + 0.025), spines[(k + 2) % len(spines)])


def cabinet(g):
    """A four-drawer steel filing cabinet."""
    W, D, H = 0.47, 0.62, 1.32
    c = lin(120, 126, 120)
    box(g, (W, D, H), (0, 0, 0), c, metal=0.5, top=mul(c, 1.06))
    for k in range(4):
        z = 0.03 + k * 0.32
        box(g, (W - 0.03, 0.015, 0.3), (0, -D / 2 - 0.0075, z), mul(c, 1.06), metal=0.5)
        box(g, (0.14, 0.03, 0.02), (0, -D / 2 - 0.02, z + 0.2), STEEL, metal=1)
        box(g, (0.08, 0.005, 0.04), (0, -D / 2 - 0.017, z + 0.24), WHITE)
    # One drawer pulled out.
    box(g, (W - 0.05, 0.25, 0.26), (0, -D / 2 - 0.13, 0.67), mul(c, 0.95), metal=0.5, top=lin(200, 190, 160))


def cabinet_v1(g):
    """A two-door steel cupboard (a clinic's or an office's), one door ajar."""
    W, D, H = 0.8, 0.45, 1.8
    c = lin(196, 196, 188)
    box(g, (W, D, H), (0, 0, 0), c, metal=0.4)
    box(g, (W / 2 - 0.01, 0.02, H - 0.06), (-W / 4, -D / 2 - 0.01, 0.03), mul(c, 1.03), metal=0.4)
    box(g, (W / 2 - 0.01, 0.02, H - 0.06), (W / 2 - 0.1, -D / 2 - 0.2, 0.03), mul(c, 1.03), metal=0.4, rz=-60)
    box(g, (W / 2 - 0.04, 0.02, H - 0.1), (W / 4, -D / 2 + 0.02, 0.05), mul(c, 0.5))
    for z in (0.45, 0.9, 1.35):
        box(g, (W / 2 - 0.05, D - 0.06, 0.02), (W / 4, 0, z), c, metal=0.4)
    box(g, (0.02, 0.03, 0.15), (-0.06, -D / 2 - 0.03, 0.9), STEEL_D, metal=1)
    box(g, (0.18, 0.18, 0.12), (0.2, 0.0, 0.92), WHITE)
    box(g, (0.06, 0.005, 0.02), (0.2, -0.091, 0.97), RED)


def toolbox(g):
    """A red rolling tool chest: six drawers, a lid, side handle, castors."""
    W, D, H = 0.7, 0.46, 1.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(g, 0.04, 0.04, (sx * 0.28, sy * 0.17, 0.04), BLACK, seg=6, axis="x")
    box(g, (W, D, H - 0.08), (0, 0, 0.08), RED, metal=0.4)
    drawers(g, W - 0.04, H - 0.32, 0.1, -D / 2, 6, mul(RED, 1.12), STEEL)
    box(g, (W + 0.02, D + 0.02, 0.18), (0, 0, H - 0.04), mul(RED, 0.9), metal=0.4, top=mul(RED, 1.1))
    box(g, (0.03, 0.3, 0.03), (W / 2 + 0.04, 0, H - 0.25), STEEL, metal=1)
    box(g, (0.25, 0.06, 0.04), (-0.1, 0.05, H + 0.14), STEEL_D, metal=1, rz=20)


def locker(g):
    """Two steel lockers side by side: vents at the top, one door dented, a padlock."""
    W, D, H = 0.8, 0.5, 1.85
    c = lin(84, 104, 120)
    box(g, (W, D, H), (0, 0, 0), c, metal=0.5, top=mul(c, 1.05))
    for k, sx in enumerate((-1, 1)):
        x = sx * W / 4
        box(g, (W / 2 - 0.02, 0.015, H - 0.08), (x, -D / 2 - 0.0075, 0.04), mul(c, 1.08 - 0.08 * k), metal=0.5, rx=-1 * k)
        for v in range(5):
            box(g, (0.18, 0.005, 0.012), (x, -D / 2 - 0.017, H - 0.3 + 0.03 * v), BLACK)
        box(g, (0.02, 0.03, 0.12), (x + sx * -0.13, -D / 2 - 0.02, 0.95), STEEL, metal=1)
        box(g, (0.08, 0.005, 0.03), (x, -D / 2 - 0.017, H - 0.12), WHITE)
    box(g, (0.03, 0.02, 0.04), (0.08, -D / 2 - 0.04, 0.9), BRASS, metal=1)


def desk(g):
    """An office desk: a top on a drawer pedestal and a panel, an old beige monitor and a keyboard."""
    W, D, H = 1.2, 0.65, 0.75
    c = mat("wood", lin(150, 136, 110))
    box(g, (W, D, 0.03), (0, 0, H - 0.03), c, top=mul(c, 1.08))
    box(g, (0.42, D - 0.04, H - 0.03), (W / 2 - 0.23, 0, 0), mul(c, 0.92))
    drawers(g, 0.4, H - 0.08, 0.02, -D / 2 + 0.02, 3, mul(c, 1.0), STEEL)
    box(g, (0.03, D - 0.04, H - 0.03), (-W / 2 + 0.03, 0, 0), mul(c, 0.92))
    box(g, (W - 0.4, 0.02, 0.45), (-0.2, D / 2 - 0.06, 0.25), mul(c, 0.85))
    m = mat("plastic", lin(200, 192, 170))
    box(g, (0.38, 0.38, 0.33), (-0.15, 0.1, H + 0.05), m, taper=0.82, front=mul(m, 1.03))
    box(g, (0.3, 0.01, 0.24), (-0.15, -0.095, H + 0.09), plain(lin(40, 46, 44)), metal=0.6)
    box(g, (0.16, 0.16, 0.05), (-0.15, 0.1, H), m)
    box(g, (0.45, 0.16, 0.025), (-0.1, -0.22, H), m, top=mul(m, 0.85))
    chair(g, (-0.1, -0.6, 0), 0, lin(60, 60, 64))


def pallet(g):
    """A wooden pallet stacked with shrink-wrapped boxes."""
    W = 1.0
    for y in (-0.42, 0, 0.42):
        box(g, (W, 0.1, 0.08), (0, y, 0), WOOD)
    for k in range(5):
        box(g, (W, 0.14, 0.022), (0, -0.43 + 0.215 * k, 0.08), PINE)
    c = mat("paper", CARD)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (0.44, 0.44, 0.35), (sx * 0.23, sy * 0.23, 0.1), c, top=mul(c, 1.05))
    for sx in (-1, 1):
        box(g, (0.44, 0.9, 0.32), (sx * 0.23, 0.0, 0.45), mul(c, 0.94), top=mul(c, 1.02))
    box(g, (0.94, 0.94, 0.68), (0, 0, 0.1), plain(lin(200, 210, 214)), metal=0.2, taper=1.0)


def sack(g):
    """Three sacks of grain or feed, slumped against each other."""
    for k, (x, y, rz, rx) in enumerate(((-0.2, 0.05, 10, 0), (0.22, 0.1, -12, 0), (0.0, -0.1, 80, 0))):
        c = mat("cloth", [lin(176, 156, 112), lin(160, 140, 100), lin(184, 168, 128)][k])
        z = 0.0 if k < 2 else 0.25
        box(g, (0.42, 0.6, 0.26), (x, y, z), c, rz=rz, rx=rx, taper=0.8, top=mul(c, 1.05))
        box(g, (0.24, 0.06, 0.08), (x, y, z + 0.24), mul(c, 0.85), rz=rz)


def sink(g):
    """A sink unit: a cabinet, a steel drainer top with a basin, a swan-neck tap."""
    W, D, H = 0.8, 0.6, 0.9
    c = mat("painted", lin(190, 186, 170))
    box(g, (W, D - 0.06, 0.1), (0, 0.03, 0), BLACK)
    box(g, (W, D - 0.02, H - 0.14), (0, 0.01, 0.1), c)
    doors(g, W - 0.02, H - 0.16, 0.11, -D / 2 + 0.02, 2, c, STEEL, knob_z=H - 0.2)
    box(g, (W, D, 0.04), (0, 0, H - 0.04), STEEL, metal=1, top=lin(180, 182, 186))
    box(g, (0.42, 0.36, 0.005), (-0.12, -0.02, H), lin(70, 72, 76), metal=1)
    cyl(g, 0.015, 0.25, (-0.12, 0.22, H), STEEL, metal=1, seg=6)
    box(g, (0.03, 0.15, 0.03), (-0.12, 0.15, H + 0.23), STEEL, metal=1)
    for sx in (-1, 1):
        cyl(g, 0.02, 0.04, (-0.12 + sx * 0.08, 0.24, H), STEEL, metal=1, seg=6)


def bin(g):
    """A wheelie bin: a tapered green body, a hinged lid, two wheels at the back."""
    c = mat("plastic", lin(46, 74, 52))
    box(g, (0.56, 0.6, 0.95), (0, 0, 0.03), c, taper=0.9, metal=0.1)
    box(g, (0.6, 0.66, 0.05), (0, 0.02, 0.98), mul(c, 0.9), top=mul(c, 1.1))
    box(g, (0.5, 0.06, 0.04), (0, 0.33, 0.92), BLACK)
    for sx in (-1, 1):
        wheel(g, 0.1, 0.05, (sx * 0.24, 0.28, 0.1))
    box(g, (0.3, 0.005, 0.25), (0, -0.28, 0.4), mul(c, 1.15), rx=4)


# --- the bathroom ---

def toilet(g):
    """A toilet: a cistern against the wall, the bowl on its pedestal, the seat down."""
    box(g, (0.42, 0.18, 0.38), (0, 0.24, 0.42), ENAMEL, top=mul(ENAMEL, 1.04))
    box(g, (0.44, 0.2, 0.03), (0, 0.24, 0.8), ENAMEL)
    box(g, (0.05, 0.03, 0.02), (0.15, 0.13, 0.75), STEEL, metal=1)
    cyl(g, 0.12, 0.32, (0, 0.06, 0), ENAMEL, seg=10, r2=0.17)
    box(g, (0.34, 0.2, 0.32), (0, 0.18, 0), ENAMEL)
    cyl(g, 0.19, 0.06, (0, -0.02, 0.32), ENAMEL, seg=12, caps=lin(160, 150, 120))
    cyl(g, 0.19, 0.025, (0, -0.02, 0.38), WHITE, seg=12)


def bath(g):
    """A bath along the wall: an enamel tub with a panel, taps at one end, a curtain rail over it."""
    W, D, H = 1.65, 0.72, 0.55
    box(g, (W, D, H), (0, 0, 0), ENAMEL, top=mul(ENAMEL, 1.05))
    box(g, (W - 0.12, D - 0.12, 0.005), (0, 0, H), lin(150, 156, 158), metal=0.3)
    box(g, (W - 0.24, D - 0.2, 0.004), (0.02, 0, H - 0.003), lin(110, 120, 124), metal=0.3)
    cyl(g, 0.018, 0.15, (-W / 2 + 0.12, 0.2, H), STEEL, metal=1, seg=6)
    box(g, (0.025, 0.12, 0.025), (-W / 2 + 0.12, 0.13, H + 0.13), STEEL, metal=1)
    for sx in (-1, 1):
        cyl(g, 0.03, 0.03, (-W / 2 + 0.06, 0.2 + sx * 0.0, H + 0.08), STEEL, metal=1, seg=6, axis="x")
    # A rail along the front at head height and a pale curtain bunched at one end.
    box(g, (W, 0.02, 0.02), (0, -D / 2 + 0.03, 1.9), STEEL, metal=1)
    box(g, (0.25, 0.08, 1.2), (W / 2 - 0.15, -D / 2 + 0.03, 0.7), mat("cloth", lin(170, 190, 196)), taper=1.0)


def shower(g):
    """A shower: a tray, a frosted screen down each side, a riser and head on the wall."""
    S = 0.85
    box(g, (S, S, 0.08), (0, 0, 0), ENAMEL, top=mul(ENAMEL, 0.92))
    box(g, (0.08, 0.08, 0.005), (0, 0, 0.08), STEEL_D, metal=1)
    for sx in (-1, 1):
        box(g, (0.02, S, 1.9), (sx * (S / 2 - 0.01), 0, 0.08), plain(lin(150, 166, 170)), metal=0.2)
        box(g, (0.03, 0.03, 1.9), (sx * (S / 2 - 0.01), -S / 2 + 0.015, 0.08), STEEL, metal=1)
    box(g, (S, 0.03, 0.03), (0, -S / 2 + 0.015, 1.97), STEEL, metal=1)
    box(g, (0.04, 0.03, 1.2), (0.1, S / 2 - 0.03, 0.85), STEEL, metal=1)
    cyl(g, 0.07, 0.02, (0.1, S / 2 - 0.12, 2.0), STEEL, metal=1, seg=8)
    box(g, (0.03, 0.12, 0.03), (0.1, S / 2 - 0.08, 2.0), STEEL, metal=1)
    box(g, (0.12, 0.05, 0.16), (-0.18, S / 2 - 0.04, 1.1), WHITE)


def washer(g):
    """A front-loading washing machine: a white box, a round glass door, a control strip."""
    W, D, H = 0.6, 0.6, 0.85
    box(g, (W, D, H), (0, 0, 0), WHITE, top=mul(WHITE, 1.03))
    box(g, (W - 0.01, 0.015, 0.12), (0, -D / 2 - 0.006, H - 0.13), mul(WHITE, 0.95))
    box(g, (0.15, 0.01, 0.06), (-0.18, -D / 2 - 0.012, H - 0.1), STEEL, metal=1)
    cyl(g, 0.03, 0.025, (0.18, -D / 2 - 0.01, H - 0.07), STEEL, metal=1, seg=8, axis="y")
    cyl(g, 0.19, 0.04, (0, -D / 2 - 0.02, 0.4), STEEL, metal=1, seg=14, axis="y", caps=lin(170, 172, 176))
    cyl(g, 0.14, 0.05, (0, -D / 2 - 0.025, 0.4), GLASS, metal=0.5, seg=14, axis="y", caps=plain(lin(56, 70, 78)))


# --- the kitchen and living room ---

def oven(g):
    """A kitchen range: white enamel, four rings on a black hob, an oven door with a window, knobs."""
    W, D, H = 0.6, 0.6, 0.9
    box(g, (W, D, H), (0, 0, 0), ENAMEL, top=BLACK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            cyl(g, 0.08, 0.012, (sx * 0.14, sy * 0.13, H), STEEL_D, metal=1, seg=10, caps=lin(56, 56, 58))
    box(g, (W, 0.05, 0.12), (0, D / 2 - 0.025, H), ENAMEL)
    box(g, (W - 0.04, 0.02, 0.55), (0, -D / 2 - 0.01, 0.1), mul(ENAMEL, 1.03))
    box(g, (0.36, 0.01, 0.24), (0, -D / 2 - 0.02, 0.3), plain(lin(36, 34, 32)), metal=0.5)
    box(g, (0.45, 0.04, 0.025), (0, -D / 2 - 0.05, 0.6), STEEL, metal=1)
    for k in range(4):
        cyl(g, 0.02, 0.03, (-0.2 + k * 0.13, -D / 2 - 0.015, 0.78), BLACK, seg=8, axis="y")
    box(g, (W - 0.04, 0.015, 0.06), (0, -D / 2 - 0.008, 0.02), BLACK)
    # A pan left on the hob.
    cyl(g, 0.11, 0.07, (0.14, -0.13, H + 0.012), STEEL_D, metal=1, seg=10)
    box(g, (0.2, 0.025, 0.02), (0.14, -0.33, H + 0.06), BLACK)


def stove(g):
    """A built wood stove: a black cast-iron box on legs, a fire door with a dim glow, the flue going up."""
    W, D, H = 0.6, 0.5, 0.75
    legs(g, W, D, 0.15, 0.05, BLACK, 0.02, metal=0.8)
    box(g, (W, D, H - 0.15), (0, 0, 0.15), lin(42, 42, 44), metal=0.6, top=lin(56, 56, 58))
    box(g, (0.32, 0.03, 0.26), (0, -D / 2 - 0.01, 0.3), lin(34, 34, 36), metal=0.6)
    box(g, (0.24, 0.01, 0.14), (0, -D / 2 - 0.025, 0.36), lin(170, 70, 20))
    box(g, (0.06, 0.04, 0.025), (0.12, -D / 2 - 0.04, 0.43), STEEL_D, metal=1)
    cyl(g, 0.075, 1.6, (0, 0.1, H), lin(52, 52, 54), metal=0.6, seg=8)
    box(g, (W + 0.4, D + 0.3, 0.03), (0, 0.0, 0), BRICK, top=mul(BRICK, 1.1))
    cyl(g, 0.12, 0.12, (0.15, -0.05, H), STEEL_D, metal=1, seg=8, caps=lin(60, 60, 62))


def tv(g):
    """A low TV cabinet with a fat old television on it, a dead remote."""
    W, D = 1.0, 0.45
    box(g, (W, D, 0.48), (0, 0, 0), OAK, top=mul(OAK, 1.1))
    box(g, (W - 0.06, 0.02, 0.36), (0, -D / 2, 0.06), lin(40, 40, 40))
    for k in range(2):
        box(g, (0.3, 0.02, 0.05), (-0.25 + 0.5 * k, -D / 2 - 0.01, 0.3), BLACK)
    c = lin(46, 44, 42)
    box(g, (0.64, 0.42, 0.48), (0, 0.02, 0.48), c, taper=0.86, front=c)
    box(g, (0.54, 0.02, 0.4), (0, -0.19, 0.51), plain(lin(30, 38, 40)), metal=0.7)
    box(g, (0.62, 0.02, 0.46), (0, -0.2, 0.49), c)
    box(g, (0.54, 0.025, 0.4), (0, -0.21, 0.51), plain(lin(36, 46, 50)), metal=0.7)
    box(g, (0.17, 0.05, 0.02), (0.32, -0.12, 0.48), BLACK, rz=-20)


def register(g):
    """A shop's till point: a short counter with a cash register, a scanner, the drawer hanging open."""
    W, D, H = 1.0, 0.6, 0.9
    box(g, (W, D, H - 0.04), (0, 0, 0), lin(96, 96, 100))
    box(g, (W + 0.02, D + 0.02, 0.04), (0, 0, H - 0.04), lin(62, 62, 64), top=lin(80, 80, 84))
    c = lin(160, 160, 150)
    box(g, (0.4, 0.38, 0.12), (-0.1, 0.0, H), c)
    box(g, (0.38, 0.26, 0.1), (-0.1, 0.04, H + 0.12), c, taper=0.8, top=mul(c, 0.8))
    box(g, (0.36, 0.3, 0.06), (-0.1, -0.24, H + 0.02), c, top=lin(60, 52, 40))
    box(g, (0.18, 0.04, 0.12), (-0.1, 0.12, H + 0.2), BLACK, rx=-20)
    box(g, (0.16, 0.12, 0.05), (0.3, 0.05, H), BLACK)


def machine(g):
    """A factory machine: a pillar drill or press on a cast base, a motor housing, a guard and a control box."""
    c = lin(74, 96, 84)
    box(g, (0.9, 0.7, 0.12), (0, 0, 0), mul(c, 0.8), metal=0.5)
    box(g, (0.5, 0.5, 0.8), (0, 0.1, 0.12), c, metal=0.5, taper=0.9)
    box(g, (0.25, 0.25, 0.75), (0, 0.15, 0.92), c, metal=0.5)
    box(g, (0.4, 0.7, 0.28), (0, 0.0, 1.55), c, metal=0.5, top=mul(c, 1.1))
    cyl(g, 0.14, 0.32, (0, 0.38, 1.69), mul(c, 0.8), metal=0.5, seg=8, axis="x")
    cyl(g, 0.04, 0.25, (0, -0.22, 1.3), STEEL, metal=1, seg=6)
    box(g, (0.4, 0.3, 0.04), (0, -0.2, 0.9), STEEL_D, metal=1)
    box(g, (0.22, 0.12, 0.3), (0.38, 0.0, 1.0), lin(150, 140, 60), metal=0.4)
    cyl(g, 0.03, 0.03, (0.38, -0.06, 1.15), RED, seg=8, axis="y")
    for sx in (-1, 1):
        box(g, (0.04, 0.04, 0.04), (sx * 0.4, -0.3, 0.12), BLACK, metal=1)
    box(g, (0.9, 0.04, 0.003), (0, -0.37, 0.003), YELLOW)


def altar(g):
    """A church altar: a stone table under a white cloth, two candlesticks and a plain wooden cross."""
    W, D, H = 1.4, 0.7, 1.0
    box(g, (W, D, H - 0.06), (0, 0, 0), STONE, top=mul(STONE, 1.08))
    box(g, (W + 0.08, D + 0.08, 0.06), (0, 0, H - 0.06), mul(STONE, 1.1))
    box(g, (W + 0.1, D + 0.1, 0.01), (0, 0, H), WHITE)
    box(g, (0.5, 0.01, 0.6), (0, -D / 2 - 0.055, H - 0.6), mat("cloth", lin(120, 30, 40)))
    for sx in (-1, 1):
        cyl(g, 0.05, 0.03, (sx * 0.5, 0.1, H + 0.01), BRASS, metal=1, seg=8)
        cyl(g, 0.015, 0.25, (sx * 0.5, 0.1, H + 0.04), BRASS, metal=1, seg=6)
        cyl(g, 0.02, 0.18, (sx * 0.5, 0.1, H + 0.29), CREAM, seg=6)
    box(g, (0.05, 0.05, 0.75), (0, 0.15, H + 0.01), WOOD_D)
    box(g, (0.4, 0.05, 0.05), (0, 0.15, H + 0.5), WOOD_D)


# --- outside: the yard, the farm, the street ---

def car(g, body=mat("body", lin(110, 40, 36))):
    """A dead sedan along its length (X, real size): a body, the cabin, glass, four flat-ish wheels, rust."""
    L, W = 4.2, 1.7
    box(g, (L, W, 0.5), (0, 0, 0.28), body, metal=0.6, taper=0.99, top=mul(body, 1.1))
    box(g, (0.4, W - 0.06, 0.12), (L / 2 - 0.2, 0, 0.66), body, metal=0.6, ry=8)
    box(g, (2.1, W - 0.16, 0.5), (-0.25, 0, 0.78), body, metal=0.6, taper=0.86, top=mul(body, 1.1))
    for sy in (-1, 1):
        box(g, (1.75, 0.02, 0.36), (-0.25, sy * (W / 2 - 0.1), 0.84), GLASS, metal=0.7, rx=sy * 9)
    box(g, (0.02, W - 0.3, 0.38), (0.82, 0, 0.82), GLASS, metal=0.7, ry=-38)
    box(g, (0.02, W - 0.3, 0.36), (-1.32, 0, 0.82), GLASS, metal=0.7, ry=34)
    for sx in (-1, 1):
        for sy in (-1, 1):
            wheel(g, 0.31, 0.2, (sx * 1.3, sy * (W / 2 - 0.08), 0.29))
        box(g, (0.05, W, 0.14), (sx * L / 2, 0, 0.26), STEEL, metal=1)
    for sy in (-1, 1):
        box(g, (0.02, 0.2, 0.1), (L / 2 + 0.01, sy * 0.6, 0.55), CREAM)
        box(g, (0.02, 0.2, 0.08), (-L / 2 - 0.01, sy * 0.6, 0.56), RED)
    box(g, (0.9, 0.02, 0.2), (0.6, -W / 2 - 0.003, 0.36), RUST, metal=0.3)


def car_v1(g):
    """The same car in faded blue."""
    car(g, mat("body", lin(62, 86, 112)))


def truck(g):
    """A pickup truck along X: a tall cab, a flat bed with low sides, big wheels, a bull bar."""
    L, W = 5.0, 1.9
    c = mat("body", lin(70, 82, 56))
    box(g, (L, W, 0.55), (0, 0, 0.45), c, metal=0.5, top=mul(c, 1.1))
    box(g, (1.2, W, 0.25), (1.85, 0, 1.0), c, metal=0.5, ry=6)
    box(g, (1.5, W - 0.1, 0.75), (0.65, 0, 1.0), c, metal=0.5, taper=0.92, top=mul(c, 1.1))
    for sy in (-1, 1):
        box(g, (1.2, 0.02, 0.45), (0.65, sy * (W / 2 - 0.06), 1.15), GLASS, metal=0.7, rx=sy * 5)
    box(g, (0.02, W - 0.3, 0.5), (1.43, 0, 1.12), GLASS, metal=0.7, ry=-22)
    for sy in (-1, 1):
        box(g, (2.1, 0.05, 0.4), (-1.4, sy * (W / 2 - 0.025), 1.0), mul(c, 0.9), metal=0.5)
    box(g, (0.05, W, 0.4), (-2.48, 0, 1.0), mul(c, 0.9), metal=0.5)
    box(g, (2.0, W - 0.12, 0.01), (-1.4, 0, 1.0), lin(60, 58, 52), metal=0.6)
    for sx in (-1.55, 1.6):
        for sy in (-1, 1):
            wheel(g, 0.42, 0.28, (sx, sy * (W / 2 - 0.05), 0.42))
    box(g, (0.1, W - 0.2, 0.5), (L / 2 + 0.06, 0, 0.35), BLACK, metal=0.8)
    # Jerry cans and a tarp bundle in the bed.
    box(g, (0.35, 0.18, 0.45), (-2.1, 0.6, 1.0), RED, metal=0.5)
    box(g, (0.9, 0.7, 0.3), (-1.2, -0.3, 1.0), TARP, taper=0.85)


def tractor(g):
    """A farm tractor along X: big rear wheels, small front ones, the bonnet, a seat, a roll bar and exhaust."""
    c = mat("body", lin(140, 50, 36))
    box(g, (1.6, 0.6, 0.6), (0.55, 0, 0.55), c, metal=0.5, top=mul(c, 1.1))
    box(g, (0.9, 0.9, 0.5), (-0.6, 0, 0.5), mul(c, 0.9), metal=0.5)
    for sy in (-1, 1):
        wheel(g, 0.75, 0.4, (-0.7, sy * 0.82, 0.75), seg=14)
        box(g, (1.0, 0.45, 0.04), (-0.7, sy * 0.82, 1.5), c, metal=0.5)
        wheel(g, 0.4, 0.22, (1.05, sy * 0.6, 0.4))
    box(g, (0.4, 0.45, 0.1), (-0.75, 0, 1.0), BLACK)
    box(g, (0.08, 0.45, 0.35), (-0.95, 0, 1.08), BLACK)
    for sy in (-1, 1):
        box(g, (0.05, 0.05, 1.3), (-1.0, sy * 0.5, 1.0), BLACK, metal=0.8)
    box(g, (0.05, 1.05, 0.05), (-1.0, 0, 2.28), BLACK, metal=0.8)
    cyl(g, 0.04, 0.9, (0.75, 0.2, 1.1), BLACK, metal=0.8, seg=6)
    cyl(g, 0.02, 0.35, (-0.4, 0, 1.0), BLACK, metal=0.8, seg=6, ry=-30)
    cyl(g, 0.14, 0.03, (-0.25, 0, 1.3), BLACK, seg=10, ry=-30)
    box(g, (0.1, 0.62, 0.3), (1.38, 0, 0.6), lin(40, 40, 40), metal=0.6)


def hay(g):
    """Two square bales of hay, one on the other, twine round them."""
    for k, (z, rz) in enumerate(((0, 0), (0.4, 6))):
        c = [HAY, mul(HAY, 0.92)][k]
        box(g, (0.95, 0.48, 0.4), (0, 0.0, z), c, rz=rz, top=mul(c, 1.06))
        for x in (-0.25, 0.25):
            box(g, (0.02, 0.5, 0.41), (x, 0, z), mat("fine", lin(160, 120, 60)), rz=rz)
    box(g, (0.3, 0.2, 0.06), (0.4, -0.4, 0), mul(HAY, 0.85), rz=30, taper=0.6)


def silo(g):
    """A grain silo (2 x 2 tiles, drawn once, on the group's middle): a steel drum, a cone roof, a ladder."""
    R = 1.0
    cyl(g, R, 0.3, (0, 0, 0), CONCRETE, seg=16)
    for k in range(5):
        cyl(g, R - 0.02, 1.0, (0, 0, 0.3 + k * 1.0), [lin(170, 172, 168), lin(150, 152, 150)][k % 2], metal=0.7, seg=16)
    cyl(g, R + 0.03, 0.9, (0, 0, 5.3), lin(160, 160, 156), metal=0.7, seg=16, r2=0.15)
    cyl(g, 0.18, 0.2, (0, 0, 6.15), STEEL_D, metal=0.7, seg=8)
    for sx in (-1, 1):
        box(g, (0.03, 0.03, 5.6), (sx * 0.2, -R - 0.08, 0.2), STEEL_D, metal=1)
    for k in range(18):
        box(g, (0.4, 0.02, 0.02), (0, -R - 0.08, 0.5 + 0.3 * k), STEEL_D, metal=1)
    box(g, (0.6, 0.2, 0.3), (0.5, -R + 0.1, 0.3), RUST, metal=0.4, rz=-30, taper=0.7)


def pump(g):
    """A fuel pump on its island: the cabinet, the display, a hose looped up to the nozzle."""
    box(g, (0.9, 0.6, 0.15), (0, 0, 0), CONCRETE, top=mul(CONCRETE, 1.1))
    c = lin(200, 196, 186)
    box(g, (0.55, 0.38, 1.25), (0, 0, 0.15), c, metal=0.3)
    box(g, (0.57, 0.4, 0.2), (0, 0, 1.4), RED, metal=0.3, top=mul(RED, 1.1))
    box(g, (0.38, 0.01, 0.28), (0, -0.195, 0.95), plain(lin(30, 36, 34)), metal=0.6)
    for k in range(3):
        box(g, (0.06, 0.012, 0.03), (-0.12 + 0.08 * k, -0.2, 1.12 - 0.08 * k), lin(160, 150, 90))
    box(g, (0.1, 0.06, 0.2), (0.33, 0, 0.75), BLACK)
    cyl(g, 0.02, 0.9, (0.33, -0.08, 0.15), BLACK, seg=6, rx=10)
    box(g, (0.05, 0.16, 0.06), (0.34, 0.02, 0.84), BLACK, rx=30)
    for sx in (-1, 1):
        cyl(g, 0.06, 0.9, (sx * 0.4, -0.25, 0.15), YELLOW, metal=0.3, seg=8)


def well(g):
    """A well: a stone ring, two posts and a little roof, a windlass with rope and bucket."""
    for k in range(3):
        cyl(g, 0.48 - 0.005 * k, 0.22, (0, 0, k * 0.22), [STONE, mul(STONE, 0.9), mul(STONE, 1.05)][k], seg=12)
    cyl(g, 0.38, 0.01, (0, 0, 0.66), lin(20, 24, 26), seg=12)
    for sx in (-1, 1):
        box(g, (0.08, 0.08, 1.6), (sx * 0.44, 0, 0), WOOD_D)
    for sy in (-1, 1):
        box(g, (1.1, 0.5, 0.025), (0, sy * 0.22, 1.66), WOOD, rx=sy * 32, top=WOOD_L)
    cyl(g, 0.05, 0.86, (0, 0, 1.15), WOOD, seg=6, axis="x", caps=WOOD_D)
    cyl(g, 0.055, 0.25, (0, 0, 1.15), ROPE, seg=6, axis="x")
    box(g, (0.02, 0.05, 0.2), (0.5, 0.0, 1.0), STEEL_D, metal=1)
    box(g, (0.15, 0.02, 0.02), (0.57, 0.0, 1.0), STEEL_D, metal=1)
    cyl(g, 0.006, 0.4, (0, 0.04, 0.72), ROPE, seg=4)
    cyl(g, 0.1, 0.18, (0.0, 0.04, 0.6), STEEL_D, metal=1, seg=8, r2=0.12)


def collector(g):
    """A rain collector: a blue drum under a tarp funnel slung between four posts."""
    cyl(g, 0.3, 0.9, (0, 0, 0.12), BLUE, metal=0.2, seg=10, caps=mul(BLUE, 0.8))
    for k in (0.3, 0.6):
        cyl(g, 0.305, 0.03, (0, 0, 0.12 + k), mul(BLUE, 0.85), metal=0.2, seg=10)
    box(g, (0.7, 0.7, 0.12), (0, 0, 0), WOOD, top=WOOD_L)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (0.05, 0.05, 1.6), (sx * 0.42, sy * 0.42, 0), WOOD_D)
    # The tarp: four sloping panels down to a hole over the drum.
    for a in range(4):
        m = (a * 90)
        box(g, (0.9, 0.45, 0.01), (0.22 * math.cos(math.radians(m)), 0.22 * math.sin(math.radians(m)), 1.32),
            TARP, rz=m + 90, rx=-28)
    cyl(g, 0.04, 0.3, (0, 0, 1.02), BLACK, seg=6)
    cyl(g, 0.03, 0.12, (0.3, 0, 0.25), STEEL, metal=1, seg=6, axis="x")


def generator(g):
    """A portable generator: a yellow engine and fuel tank in a black tube frame, a pull cord, sockets."""
    W, D = 0.7, 0.5
    for sy in (-1, 1):
        box(g, (W, 0.03, 0.03), (0, sy * D / 2, 0.0), BLACK, metal=0.8)
        box(g, (W, 0.03, 0.03), (0, sy * D / 2, 0.55), BLACK, metal=0.8)
        for sx in (-1, 1):
            box(g, (0.03, 0.03, 0.58), (sx * W / 2, sy * D / 2, 0), BLACK, metal=0.8)
    box(g, (0.4, 0.38, 0.3), (-0.08, 0, 0.05), lin(60, 60, 62), metal=0.6)
    cyl(g, 0.12, 0.25, (0.2, 0, 0.2), STEEL_D, metal=0.8, seg=8, axis="x")
    box(g, (0.6, 0.42, 0.2), (0, 0, 0.36), YELLOW, metal=0.3, top=mul(YELLOW, 1.1), taper=0.92)
    cyl(g, 0.05, 0.04, (-0.15, 0, 0.56), BLACK, seg=8)
    box(g, (0.18, 0.02, 0.12), (0.2, -D / 2 + 0.03, 0.18), lin(40, 40, 40))
    for k in range(2):
        box(g, (0.04, 0.01, 0.04), (0.15 + k * 0.08, -D / 2 + 0.02, 0.22), ORANGE)
    box(g, (0.05, 0.03, 0.03), (-0.32, -0.1, 0.25), RED)
    box(g, (0.02, 0.6, 0.02), (0.2, -0.55, 0.0), ORANGE)


def lamp(g):
    """A built lamp: a work light on a pole on a cross foot, its cable off to the generator."""
    for a in (0, 90):
        box(g, (0.6, 0.06, 0.05), (0, 0, 0), WOOD_D, rz=a)
    cyl(g, 0.03, 1.9, (0, 0, 0.05), STEEL_D, metal=1, seg=6)
    box(g, (0.24, 0.06, 0.06), (0, 0, 1.85), STEEL_D, metal=1)
    box(g, (0.22, 0.14, 0.18), (0, -0.07, 1.82), lin(180, 150, 40), metal=0.4, rx=20)
    box(g, (0.18, 0.01, 0.14), (0, -0.15, 1.82), lin(250, 240, 200), rx=20)
    cyl(g, 0.012, 1.4, (0.05, 0.05, 0.4), BLACK, seg=4)
    box(g, (0.02, 0.8, 0.02), (0.05, 0.4, 0.0), BLACK)


def heater(g):
    """A paraffin heater: a round drum with a grille, a glowing window, a carry handle."""
    c = lin(120, 120, 110)
    cyl(g, 0.24, 0.1, (0, 0, 0), BLACK, metal=0.6, seg=12)
    cyl(g, 0.21, 0.42, (0, 0, 0.1), c, metal=0.6, seg=12)
    cyl(g, 0.215, 0.06, (0, 0, 0.52), BLACK, metal=0.6, seg=12, r2=0.18)
    box(g, (0.18, 0.02, 0.12), (0, -0.205, 0.22), lin(200, 90, 30))
    box(g, (0.2, 0.02, 0.15), (0, -0.21, 0.21), BLACK, metal=0.6, taper=1.0)
    for k in range(4):
        box(g, (0.01, 0.022, 0.15), (-0.06 + 0.04 * k, -0.215, 0.21), BLACK)
    for sx in (-1, 1):
        box(g, (0.015, 0.015, 0.12), (sx * 0.18, 0, 0.58), STEEL, metal=1)
    box(g, (0.38, 0.015, 0.015), (0, 0, 0.7), STEEL, metal=1)


def coop(g):
    """A chicken coop: a little house on legs, a sloped tin roof, a ramp and a nest box at the side."""
    W, D = 0.95, 0.75
    legs(g, W, D, 0.4, 0.05, WOOD_D, 0.02)
    box(g, (W, D, 0.55), (0, 0, 0.4), WOOD, top=WOOD_L)
    for k in range(6):
        box(g, (0.005, D + 0.005, 0.5), (-W / 2 + 0.15 * (k + 0.5), 0, 0.42), WOOD_D)
    box(g, (W + 0.15, D + 0.2, 0.03), (0, 0, 1.0), STEEL, metal=0.6, rx=-10)
    box(g, (0.25, 0.02, 0.25), (-0.15, -D / 2 - 0.01, 0.5), lin(30, 26, 22))
    box(g, (0.22, 0.6, 0.02), (-0.15, -D / 2 - 0.25, 0.22), PINE, rx=-38)
    box(g, (0.3, 0.4, 0.3), (W / 2 + 0.15, 0, 0.5), WOOD_D, top=STEEL)


def radio(g):
    """A radio set up on a table: the transceiver, a microphone, a car battery and a mast with its wire."""
    W, D, H = 0.8, 0.5, 0.75
    box(g, (W, D, 0.04), (0, 0, H - 0.04), WOOD, top=WOOD_L)
    legs(g, W, D, H - 0.04, 0.04, WOOD_D)
    c = lin(64, 70, 62)
    box(g, (0.4, 0.3, 0.16), (-0.08, 0.05, H), c, metal=0.5)
    box(g, (0.3, 0.01, 0.06), (-0.12, -0.105, H + 0.08), lin(140, 120, 60))
    for k in range(3):
        cyl(g, 0.018, 0.02, (0.02 + 0.04 * k, -0.105, H + 0.04), BLACK, seg=6, axis="y")
    box(g, (0.05, 0.03, 0.08), (0.25, -0.08, H), BLACK)
    box(g, (0.25, 0.17, 0.2), (0.0, 0.05, 0.0), BLACK, top=lin(60, 60, 64))
    cyl(g, 0.02, 2.4, (W / 2 - 0.05, D / 2 - 0.05, 0), STEEL, metal=1, seg=6)
    box(g, (0.6, 0.02, 0.02), (W / 2 - 0.05, D / 2 - 0.05, 2.3), STEEL, metal=1)
    box(g, (0.01, 0.01, 1.6), (W / 2 - 0.05, D / 2 - 0.08, H + 0.1), BLACK, ry=-8)


def lab(g):
    """A lab bench: a steel-topped table, a microscope, a rack of tubes, flasks, a cool box for samples."""
    W, D, H = 1.2, 0.65, 0.9
    box(g, (W, D, 0.04), (0, 0, H - 0.04), STEEL, metal=1, top=lin(190, 192, 194))
    legs(g, W, D, H - 0.04, 0.04, STEEL_D, metal=1)
    box(g, (W - 0.1, D - 0.1, 0.02), (0, 0, 0.2), STEEL_D, metal=1)
    c = lin(220, 220, 214)
    box(g, (0.16, 0.2, 0.04), (-0.3, 0.05, H), c)
    box(g, (0.05, 0.05, 0.3), (-0.3, 0.12, H + 0.04), c, rx=-10)
    cyl(g, 0.025, 0.14, (-0.3, 0.05, H + 0.2), BLACK, seg=6, rx=-25)
    box(g, (0.25, 0.08, 0.06), (0.1, 0.15, H), lin(200, 200, 200))
    for k in range(5):
        cyl(g, 0.01, 0.1, (0.0 + k * 0.05, 0.15, H + 0.03), [RED, lin(120, 170, 100), WHITE, lin(120, 170, 100), RED][k], seg=5)
    for k, x in enumerate((0.35, 0.45)):
        cyl(g, 0.06, 0.07, (x, -0.1, H), GLASS, seg=8, r2=0.02)
        cyl(g, 0.02, 0.08, (x, -0.1, H + 0.07), GLASS, seg=6)
    box(g, (0.45, 0.32, 0.3), (0.25, 0.0, 0.22), lin(220, 220, 214), top=lin(70, 120, 160))


def tower(g):
    """A watchtower over its tile: four legs, braces, a plank platform at 2 m with a rail, a ladder in front."""
    S, P = 0.9, 2.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(g, (0.1, 0.1, P + 1.0), (sx * S / 2, sy * S / 2, 0), WOOD_D)
    for sy in (-1, 1):
        box(g, (1.2, 0.05, 0.08), (0, sy * S / 2, 1.0), WOOD, ry=-55)
    for sx in (-1, 1):
        box(g, (0.05, 1.2, 0.08), (sx * S / 2, 0, 1.0), WOOD, rx=55)
    for k in range(6):
        box(g, (S + 0.2, 0.18, 0.04), (0, -S / 2 - 0.05 + 0.2 * k, P), [WOOD_L, PINE][k % 2])
    for sy in (-1, 1):
        box(g, (S + 0.1, 0.05, 0.06), (0, sy * S / 2, P + 0.95), WOOD)
        box(g, (S + 0.1, 0.05, 0.05), (0, sy * S / 2, P + 0.5), WOOD)
    box(g, (0.05, S + 0.1, 0.06), (S / 2, 0, P + 0.95), WOOD)
    box(g, (0.05, S + 0.1, 0.05), (S / 2, 0, P + 0.5), WOOD)
    box(g, (0.05, S + 0.1, 0.06), (-S / 2, 0, P + 0.95), WOOD)
    for sx in (-1, 1):
        box(g, (0.05, 0.05, P + 0.2), (sx * 0.2, -S / 2 - 0.15, 0), WOOD, rx=-4)
    for k in range(7):
        box(g, (0.45, 0.04, 0.04), (0, -S / 2 - 0.15 + 0.0, 0.3 + 0.28 * k), WOOD_L)


def turret(g):
    """A turret: a ring of sandbags round a tripod-mounted rifle with a scrap shield."""
    for k in range(10):
        a = 2 * math.pi * k / 10
        if 3 <= k <= 6:
            continue  # the open back
        box(g, (0.32, 0.2, 0.15), (0.38 * math.cos(a), 0.38 * math.sin(a), 0), mat("cloth", lin(140, 126, 92)), rz=math.degrees(a) + 90, taper=0.85)
        box(g, (0.3, 0.18, 0.14), (0.38 * math.cos(a + 0.3), 0.38 * math.sin(a + 0.3), 0.15), mat("cloth", lin(150, 136, 100)), rz=math.degrees(a) + 90, taper=0.85)
    for a in (90, 210, 330):
        box(g, (0.03, 0.03, 0.85), (0.15 * math.cos(math.radians(a)), 0.15 * math.sin(math.radians(a)), 0), STEEL_D, metal=1,
            rz=a - 90, rx=12)
    box(g, (0.1, 0.1, 0.1), (0, 0, 0.82), STEEL_D, metal=1)
    box(g, (0.08, 0.7, 0.08), (0, -0.15, 0.92), BLACK, metal=1)
    cyl(g, 0.018, 0.6, (0, -0.75, 0.96), BLACK, metal=1, seg=6, axis="y")
    box(g, (0.5, 0.03, 0.35), (0, -0.35, 0.82), RUST, metal=0.6)


def lure(g):
    """A noise lure: a stake with a battery alarm on it, tin cans strung on wire to rattle."""
    box(g, (0.06, 0.06, 1.3), (0, 0, 0), WOOD_D, taper=0.9)
    box(g, (0.18, 0.1, 0.14), (0, -0.06, 1.0), lin(120, 110, 40), metal=0.3)
    cyl(g, 0.05, 0.04, (0, -0.12, 1.07), BLACK, seg=8, axis="y")
    box(g, (0.12, 0.06, 0.1), (0, 0.06, 0.82), BLACK)
    for k, (x, z) in enumerate(((-0.25, 0.75), (-0.4, 0.62), (0.25, 0.7), (0.4, 0.58))):
        cyl(g, 0.04, 0.1, (x, 0, z), STEEL, metal=1, seg=6)
    box(g, (0.9, 0.006, 0.006), (0, 0, 0.85), STEEL_D, metal=1)


def beacon(g):
    """A rescue beacon: a tripod mast with a red strobe on top, a battery box and a flag."""
    for a in (90, 210, 330):
        box(g, (0.04, 0.04, 2.3), (0.35 * math.cos(math.radians(a)), 0.35 * math.sin(math.radians(a)), 0), STEEL_D, metal=1,
            rz=a - 90, rx=-9)
    cyl(g, 0.03, 2.4, (0, 0, 0), STEEL, metal=1, seg=6)
    cyl(g, 0.09, 0.18, (0, 0, 2.4), lin(220, 40, 30), seg=8, caps=BLACK)
    box(g, (0.35, 0.25, 0.25), (0.15, -0.2, 0), lin(60, 60, 60), metal=0.4)
    box(g, (0.02, 0.4, 0.25), (0, 0.2, 1.9), mat("fine", lin(230, 120, 40)))


def trap(g):
    """A spike trap: a plank frame studded with sharpened stakes and nails, flush with the ground."""
    box(g, (0.95, 0.95, 0.04), (0, 0, 0), WOOD, top=WOOD_D)
    for sx in (-1, 1):
        box(g, (0.95, 0.08, 0.06), (0, sx * 0.44, 0), WOOD_D)
        box(g, (0.08, 0.95, 0.06), (sx * 0.44, 0, 0), WOOD_D)
    for i in range(4):
        for j in range(4):
            x, y = -0.3 + 0.2 * i, -0.3 + 0.2 * j
            box(g, (0.05, 0.05, 0.18), (x, y, 0.04), PINE, taper=0.15, rz=(i * 37 + j * 23) % 50)
    for i in range(3):
        for j in range(3):
            box(g, (0.008, 0.008, 0.08), (-0.2 + 0.2 * i, -0.2 + 0.2 * j, 0.04), STEEL, metal=1)


def pit(g):
    """A spiked pit: a black hole with a ring of thrown-up dirt, stakes standing up out of it."""
    box(g, (0.9, 0.9, 0.012), (0, 0, 0), mat("earth", lin(16, 14, 12)))
    for k in range(4):
        rz = k * 90
        dx, dy = math.cos(math.radians(rz)), math.sin(math.radians(rz))
        box(g, (1.05, 0.12, 0.1), (dx * 0.5, dy * 0.5, 0), DIRT, rz=rz + 90, taper=0.6, top=mul(DIRT, 1.1))
    for i, (x, y) in enumerate(((-0.25, -0.2), (0.0, -0.28), (0.25, -0.15), (-0.2, 0.15), (0.1, 0.05), (0.28, 0.25),
                                (-0.05, 0.3), (-0.32, 0.0))):
        box(g, (0.05, 0.05, 0.3), (x, y, -0.1), PINE, taper=0.1, rx=(i * 13) % 18 - 9, ry=(i * 7) % 16 - 8)


def workbench(g):
    """A workbench: a thick top on a frame, a vice, a lower shelf, a pegboard of tools on the wall side."""
    W, D, H = 1.4, 0.7, 0.9
    box(g, (W, D, 0.07), (0, 0, H - 0.07), WOOD, top=WOOD_L)
    legs(g, W, D, H - 0.07, 0.08, WOOD_D, 0.02)
    box(g, (W - 0.1, D - 0.1, 0.03), (0, 0, 0.2), PINE)
    box(g, (W, 0.03, 0.75), (0, D / 2 - 0.015, H), mat("wood", lin(150, 130, 96)))
    box(g, (0.18, 0.12, 0.1), (W / 2 - 0.2, -D / 2 + 0.02, H), STEEL_D, metal=1)
    cyl(g, 0.012, 0.2, (W / 2 - 0.2, -D / 2 - 0.1, H + 0.05), STEEL, metal=1, seg=6, axis="y")
    # Tools on the board and on the top.
    box(g, (0.03, 0.02, 0.3), (-0.4, D / 2 - 0.04, H + 0.3), WOOD_D)
    box(g, (0.12, 0.02, 0.04), (-0.4, D / 2 - 0.04, H + 0.58), STEEL_D, metal=1)
    box(g, (0.03, 0.02, 0.35), (-0.2, D / 2 - 0.04, H + 0.25), RED)
    box(g, (0.3, 0.02, 0.1), (0.15, D / 2 - 0.04, H + 0.45), STEEL, metal=1)
    box(g, (0.3, 0.12, 0.012), (-0.2, -0.05, H), STEEL, metal=1, rz=30)
    box(g, (0.35, 0.2, 0.12), (0.15, 0.1, H), RED, metal=0.4)
    box(g, (0.1, 0.4, 0.02), (-0.5, 0.0, 0.23), WOOD_L, rz=10)


# Each furniture kind or look and its model (a decor bed or table is the kind's).
KINDS = {
    "bed": bed, "cot": cot, "table": table, "bench": bench, "pew": pew, "sofa": sofa, "armchair": armchair,
    "crate": crate, "counter": counter, "icebox": icebox, "fridge": fridge, "shelf": shelf, "wardrobe": wardrobe,
    "dresser": dresser, "nightstand": nightstand, "bookcase": bookcase, "cabinet": cabinet, "toolbox": toolbox,
    "locker": locker, "desk": desk, "pallet": pallet, "sack": sack, "sink": sink, "bin": bin,
    "toilet": toilet, "bath": bath, "shower": shower, "washer": washer,
    "oven": oven, "stove": stove, "tv": tv, "register": register, "machine": machine, "altar": altar,
    "car": car, "truck": truck, "tractor": tractor, "hay": hay, "silo": silo, "pump": pump,
    "well": well, "collector": collector, "generator": generator, "lamp": lamp, "heater": heater, "coop": coop,
    "radio": radio, "lab": lab, "tower": tower, "turret": turret, "lure": lure, "beacon": beacon,
    "trap": trap, "pit": pit, "workbench": workbench,
}

VARIANTS = {
    "bed": [bed_v1], "table": [table_v1], "sofa": [sofa_v1], "armchair": [armchair_v1],
    "crate": [crate_v1, crate_v2], "counter": [counter_v1], "shelf": [shelf_v1], "cabinet": [cabinet_v1],
    "car": [car_v1],
}


# --- out to Blender, the glTF and a preview ---

def material():
    mat = bpy.data.materials.new("furniture")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs[0], bsdf.inputs[0])
    return mat


def to_object(name, g, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in g.v], [], [list(i) for i, _, _ in g.f])
    me.update()
    me.materials.append(mat)
    col = me.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
    k = 0
    for idx, c, metal in g.f:
        for _ in idx:
            col.data[k].color = (c[0], c[1], c[2], metal)
            k += 1
    write_surfaces(me, g.f)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def preview(path, objs):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    cols = 11
    x = 0.0
    row_x = [0.0] * 20
    for k, ob in enumerate(objs):
        r = k // cols
        size = max(ob.dimensions.x, 1.0)
        ob.location = (row_x[r] + size / 2, r * 2.6, 0)
        row_x[r] += size + 0.5
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (9.5, -11.0, 13.0)
    cam.rotation_euler = (math.radians(48), 0, 0)
    cam.data.lens = 22
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    glb = opts.get("glb", "godot/assets/furniture/furniture.glb")
    os.makedirs(os.path.dirname(os.path.abspath(glb)), exist_ok=True)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    mat = material()
    objs = []
    models = []
    only = opts.get("only")
    for name, fn in KINDS.items():
        if only and name not in only.split(","):
            continue
        models.append((name, fn))
        models += [(f"{name}_v{k + 1}", v) for k, v in enumerate(VARIANTS.get(name, []))]
    total = 0
    for name, fn in models:
        g = Geo()
        fn(g)
        objs.append(to_object(name, g, mat))
        total += g.tris()
        print(f"FURNITURE {name}: {g.tris()} triangles")
    print(f"FURNITURE {len(models)} models, {total} triangles")
    bpy.ops.export_scene.gltf(filepath=os.path.abspath(glb), export_format="GLB", use_selection=False,
                              export_vertex_color="ACTIVE", export_normals=True, export_materials="EXPORT",
                              export_apply=True)
    print(f"FURNITURE glb -> {glb}")
    if "preview" in opts:
        preview(os.path.abspath(opts["preview"]), objs)


main()
