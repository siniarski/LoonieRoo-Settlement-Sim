# Things lying on the ground in first person: one small low-poly model per item kind, all by script,
# run headless (~1 s):
#
#   "$B" --background --factory-startup --python art/scripts/build_items.py -- \
#       --glb godot/assets/items/items.glb [--preview lineup.png]
#
# The kinds are the sim's (cs/Sim/Systems/Inventory.cs ITEM_WEIGHT): wood, food, meal, bandage, medicine,
# weapon, cloth, coat, armour, boots, gun, ammo, corpse, book, cart, axe, hammer, medkit, sample, scrap,
# fuel. Each is a mesh object named after its kind, made of boxes, cylinders and wedges, flat shaded, in
# vertex colours (one material, "items", that the game replaces: godot/World3D/Items.cs). Vertex alpha
# carries how metallic a face is (0 cloth/wood/paper, 1 bare metal), for the game's shader.
#
# Blender space: Z up, the model's footprint centred on the origin and resting on z = 0, its length
# along X. Sizes are real (metres), a little generous for small things so they read at a few metres.
# No randomness: a rebuild gives the same models.

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector


def lin(r, g, b):
    """An sRGB colour (0-255) as linear (the colour attribute is linear)."""
    return tuple((c / 255) ** 2.2 for c in (r, g, b))


def mul(c, k):
    return tuple(max(0.0, min(1.0, v * k)) for v in c)


# --- the palette ---

WOOD = lin(150, 108, 66)
WOOD_D = lin(112, 78, 46)
WOOD_L = lin(186, 146, 98)
BARK = lin(92, 70, 50)
STEEL = lin(150, 152, 156)
STEEL_D = lin(78, 80, 86)
RUST = lin(128, 72, 42)
BLACK = lin(30, 30, 32)
WHITE = lin(228, 226, 218)
CREAM = lin(222, 212, 184)
RED = lin(178, 34, 30)
ORANGE = lin(214, 120, 30)
BRASS = lin(196, 156, 70)
OLIVE = lin(88, 94, 58)
TAN = lin(150, 120, 80)
LEATHER = lin(88, 56, 34)
BLUE = lin(52, 82, 130)
TARP = lin(70, 88, 96)
ROPE = lin(170, 150, 104)
GREEN = lin(70, 120, 60)


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


def box(g, size, loc=(0, 0, 0), col=WOOD, metal=0.0, rz=0.0, ry=0.0, rx=0.0, top=None, taper=1.0):
    """A box of size (x, y, z) whose bottom centre is at loc; `top` colours its top face, `taper` shrinks
    the top's x and y (a wedge-ish lid or a heel)."""
    sx, sy, sz = (s / 2 for s in size)
    tx, ty = sx * taper, sy * taper
    v = [(-sx, -sy, 0), (sx, -sy, 0), (sx, sy, 0), (-sx, sy, 0),
         (-tx, -ty, 2 * sz), (tx, -ty, 2 * sz), (tx, ty, 2 * sz), (-tx, ty, 2 * sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    cols = [mul(col, 0.8), top or col, col, mul(col, 0.92), col, mul(col, 0.92)]
    g.add(v, f, cols, metal, place(loc, rz, ry, rx))


def cyl(g, r, length, loc=(0, 0, 0), col=STEEL, metal=0.0, seg=8, axis="z", rz=0.0, ry=0.0, rx=0.0,
        caps=None, r2=None):
    """A cylinder (or cone, with r2) of radius r: along Z standing on loc, or along X lying with its axis
    at loc's height (axis "x"). `caps` colours both ends."""
    r2 = r if r2 is None else r2
    v, f, cols = [], [], []
    for k in range(seg):
        a = 2 * math.pi * (k + 0.5) / seg
        v.append((r * math.cos(a), r * math.sin(a), 0))
        v.append((r2 * math.cos(a), r2 * math.sin(a), length))
    for k in range(seg):
        a0, b0, a1, b1 = 2 * k, 2 * k + 1, 2 * ((k + 1) % seg), 2 * ((k + 1) % seg) + 1
        f.append((a0, a1, b1, b0))
        # A little shading round the side, so flat-lit sides still read as round.
        cols.append(mul(col, 0.86 + 0.14 * math.cos(2 * math.pi * (k + 1) / seg - 0.8)))
    f.append(tuple(2 * k + 1 for k in range(seg)))
    f.append(tuple(2 * k for k in reversed(range(seg))))
    cols += [caps or col, mul(caps or col, 0.8)]
    m = place(loc, rz, ry, rx)
    if axis == "x":
        m = m @ Matrix.Rotation(math.radians(90), 4, "Y") @ Matrix.Translation((0, 0, -length / 2))
    g.add(v, f, cols, metal, m)


# --- the kinds ---

def wood(g):
    """Three planks: two side by side, one across them."""
    for k, y in enumerate((-0.07, 0.07)):
        box(g, (0.9, 0.12, 0.03), (0, y, 0), [WOOD, WOOD_L][k], top=[WOOD_L, WOOD][k])
    box(g, (0.85, 0.12, 0.03), (0.05, 0, 0.03), WOOD_D, rz=28, top=WOOD)
    # End grain.
    for y in (-0.07, 0.07):
        box(g, (0.005, 0.12, 0.03), (0.4525, y, 0), CREAM)


def food(g):
    """Two tins (one standing, one on its side) and a box of crackers."""
    cyl(g, 0.042, 0.11, (0, -0.05, 0), RED, caps=STEEL, seg=10)
    cyl(g, 0.043, 0.012, (0, -0.05, 0.004), STEEL, seg=10)
    cyl(g, 0.043, 0.012, (0, -0.05, 0.094), STEEL, seg=10)
    cyl(g, 0.042, 0.11, (0.06, 0.07, 0.042), lin(40, 110, 70), caps=STEEL, seg=10, axis="x", rz=35)
    box(g, (0.16, 0.05, 0.2), (-0.1, 0.06, 0), lin(214, 172, 52), rz=-15, top=lin(190, 60, 40))


def meal(g):
    """A plate with a stew on it and a spoon."""
    cyl(g, 0.12, 0.015, (0, 0, 0), WHITE, seg=14, r2=0.13)
    cyl(g, 0.085, 0.03, (0, 0, 0.012), lin(140, 80, 36), seg=12, r2=0.05, caps=lin(160, 96, 44))
    box(g, (0.03, 0.03, 0.02), (0.02, 0.02, 0.035), ORANGE)
    box(g, (0.025, 0.02, 0.018), (-0.025, -0.01, 0.034), GREEN)
    box(g, (0.16, 0.012, 0.006), (0.04, -0.15, 0), STEEL, metal=1, rz=12)


def bandage(g):
    """A roll of gauze on its side with a strip unrolled, and a second smaller roll."""
    cyl(g, 0.04, 0.08, (0, 0, 0.04), WHITE, seg=10, axis="x", caps=CREAM)
    box(g, (0.18, 0.075, 0.003), (0.12, 0.0, 0), WHITE, rz=10)
    cyl(g, 0.028, 0.06, (-0.08, 0.08, 0.028), CREAM, seg=8, axis="x", rz=70, caps=WHITE)


def medicine(g):
    """An orange pill bottle with a white cap, a pill box, and a bottle on its side."""
    cyl(g, 0.03, 0.08, (0, 0, 0), ORANGE, seg=10)
    cyl(g, 0.033, 0.022, (0, 0, 0.08), WHITE, seg=10)
    box(g, (0.11, 0.06, 0.035), (0.08, 0.06, 0), WHITE, rz=20, top=WHITE)
    box(g, (0.07, 0.061, 0.012), (0.08, 0.06, 0.012), RED, rz=20)
    cyl(g, 0.026, 0.07, (-0.07, 0.06, 0.026), lin(196, 106, 30), seg=8, axis="x", rz=-40, caps=WHITE)


def medkit(g):
    """A red case with a white cross on the lid and a handle."""
    box(g, (0.32, 0.22, 0.1), (0, 0, 0), RED, taper=0.97)
    box(g, (0.33, 0.225, 0.012), (0, 0, 0.05), mul(RED, 0.7))
    box(g, (0.12, 0.035, 0.004), (0, 0, 0.097), WHITE)
    box(g, (0.035, 0.12, 0.004), (0, 0, 0.097), WHITE)
    box(g, (0.1, 0.02, 0.02), (0, -0.12, 0.06), BLACK)


def weapon(g):
    """A home-made club: a tapered wooden bat with nails through its head and a taped grip."""
    cyl(g, 0.022, 0.75, (0, 0, 0.035), WOOD_L, seg=8, axis="x", r2=0.04, caps=WOOD)
    cyl(g, 0.025, 0.16, (-0.29, 0, 0.035), BLACK, seg=8, axis="x")
    for x, a in ((0.2, 0), (0.25, 60), (0.3, 120), (0.22, 240), (0.28, 300), (0.33, 30)):
        box(g, (0.008, 0.008, 0.1), (x, 0, 0.035), STEEL, metal=1, rx=a)


def cloth(g):
    """A pile of folded cloth: three folds in different colours, a little askew."""
    box(g, (0.3, 0.24, 0.035), (0, 0, 0), BLUE, rz=4)
    box(g, (0.28, 0.22, 0.03), (0.01, 0.01, 0.035), lin(160, 150, 130), rz=-6)
    box(g, (0.26, 0.2, 0.03), (-0.01, 0, 0.065), lin(140, 60, 56), rz=10)


def coat(g):
    """A coat laid flat: a body, sleeves folded across, a collar and buttons."""
    c = lin(96, 84, 60)
    box(g, (0.48, 0.42, 0.05), (0, 0, 0), c)
    for y in (-0.12, 0.12):
        box(g, (0.1, 0.38, 0.035), (-0.02 + y * 0.3, y * 0.15, 0.05), mul(c, 0.9), rz=-70 if y < 0 else 70)
    box(g, (0.06, 0.3, 0.04), (0.23, 0, 0.03), mul(c, 0.75))
    for x in (-0.12, 0.0, 0.12):
        box(g, (0.02, 0.02, 0.008), (x, 0.0, 0.085), BLACK)


def armour(g):
    """A padded vest flat on the ground: quilted panels, shoulder straps, a buckle."""
    for k in range(4):
        box(g, (0.11, 0.44, 0.07), (-0.17 + k * 0.115, 0, 0), [OLIVE, mul(OLIVE, 0.85)][k % 2], taper=0.94)
    for y in (-0.14, 0.14):
        box(g, (0.18, 0.07, 0.03), (0.3, y, 0.02), mul(OLIVE, 0.7))
    box(g, (0.04, 0.05, 0.012), (0.0, 0.0, 0.07), STEEL_D, metal=1)


def boot(g, loc, rz):
    m = place(loc, rz)
    sub = Geo()
    box(sub, (0.28, 0.1, 0.03), (0.02, 0, 0), BLACK)
    box(sub, (0.26, 0.095, 0.07), (0.02, 0, 0.03), LEATHER, taper=0.9)
    box(sub, (0.11, 0.09, 0.16), (-0.07, 0, 0.08), mul(LEATHER, 0.9), top=mul(LEATHER, 0.6))
    box(sub, (0.08, 0.06, 0.004), (0.05, 0, 0.1), TAN)
    base = len(g.v)
    g.v.extend(m @ v for v in sub.v)
    g.f.extend(([base + i for i in idx], c, mt) for idx, c, mt in sub.f)


def boots(g):
    """A pair of work boots, one standing, one turned."""
    boot(g, (0, -0.07, 0), 0)
    boot(g, (0.03, 0.08, 0), 18)


def gun(g):
    """A hunting rifle on its side: a wooden stock and fore-end, a dark barrel, receiver and scope."""
    h = 0.03
    box(g, (0.32, 0.04, 0.09), (-0.33, 0, 0.0), WOOD, taper=0.85, top=WOOD_D)
    box(g, (0.1, 0.035, 0.05), (-0.13, 0, 0.02), WOOD_D)
    box(g, (0.18, 0.04, 0.05), (0.0, 0, 0.025), STEEL_D, metal=1)
    box(g, (0.3, 0.038, 0.035), (0.22, 0, 0.012), WOOD)
    cyl(g, 0.011, 0.5, (0.34, 0, h + 0.04), STEEL_D, metal=1, seg=6, axis="x")
    cyl(g, 0.017, 0.24, (0.03, 0, 0.11), BLACK, metal=1, seg=8, axis="x", caps=lin(60, 80, 110))
    box(g, (0.02, 0.01, 0.03), (-0.02, 0, 0.075), BLACK)
    box(g, (0.02, 0.01, 0.03), (0.08, 0, 0.075), BLACK)
    box(g, (0.03, 0.01, 0.05), (-0.06, 0.0, -0.0), BLACK)


def ammo(g):
    """A box of cartridges, open, and a few rounds spilled."""
    box(g, (0.11, 0.07, 0.05), (0, 0, 0), lin(70, 96, 60), top=lin(196, 168, 90))
    for k in range(4):
        x = -0.03 + 0.02 * k
        cyl(g, 0.006, 0.02, (x, -0.012, 0.05), BRASS, metal=1, seg=6)
        cyl(g, 0.006, 0.02, (x, 0.012, 0.05), BRASS, metal=1, seg=6)
    for k, (x, y, a) in enumerate(((0.1, 0.04, 20), (0.12, -0.03, 75), (0.08, -0.07, 140))):
        cyl(g, 0.006, 0.05, (x, y, 0.006), BRASS, metal=1, seg=6, axis="x", rz=a, caps=lin(150, 90, 50))


def corpse(g):
    """A body wrapped in a tarp, tied at the neck, waist and ankles."""
    # Head, shoulders, hips, legs, feet: tapered lumps along X.
    parts = [(-0.78, 0.2, 0.18, 0.2), (-0.5, 0.36, 0.42, 0.24), (-0.15, 0.32, 0.38, 0.22),
             (0.25, 0.4, 0.32, 0.18), (0.62, 0.3, 0.24, 0.15), (0.82, 0.1, 0.22, 0.2)]
    for k, (x, lx, wy, hz) in enumerate(parts):
        box(g, (lx, wy, hz), (x, 0, 0), [TARP, mul(TARP, 0.9)][k % 2], taper=0.82)
    for x in (-0.66, -0.0, 0.74):
        box(g, (0.025, 0.4, 0.23), (x, 0, -0.005), ROPE, taper=0.82)


def book(g):
    """A hardback lying open-ish: covers, pages, and another book on top, turned."""
    box(g, (0.24, 0.17, 0.006), (0, 0, 0), lin(110, 50, 120))
    box(g, (0.23, 0.16, 0.03), (0.005, 0, 0.006), CREAM)
    box(g, (0.24, 0.17, 0.006), (0, 0, 0.036), lin(110, 50, 120))
    box(g, (0.008, 0.17, 0.042), (-0.118, 0, 0), lin(80, 34, 90))
    box(g, (0.2, 0.14, 0.022), (0.02, 0.01, 0.042), lin(40, 80, 60), rz=24, top=lin(46, 92, 70))


def cart(g):
    """A wheelbarrow: a tray on two handles, one wheel at the front, two legs at the back."""
    tray = lin(68, 104, 70)
    # An open tray: a floor and four sides leaning out.
    box(g, (0.5, 0.4, 0.02), (0.08, 0, 0.3), mul(tray, 0.7))
    for sx in (-1, 1):
        box(g, (0.02, 0.44, 0.24), (0.08 + sx * 0.27, 0, 0.3), tray, ry=sx * 18)
        box(g, (0.6, 0.02, 0.24), (0.08, sx * 0.22, 0.3), mul(tray, 0.9), rx=-sx * 15)
    for y in (-0.2, 0.2):
        box(g, (1.1, 0.04, 0.04), (-0.05, y, 0.28), WOOD_D, ry=-4)
        box(g, (0.04, 0.04, 0.3), (-0.2, y, 0.0), WOOD_D)
        box(g, (0.14, 0.05, 0.05), (-0.62, y, 0.31), BLACK)
    cyl(g, 0.17, 0.08, (0.5, 0, 0.17), BLACK, seg=10, axis="x", rz=90, caps=STEEL_D)
    cyl(g, 0.012, 0.44, (0.5, 0, 0.17), STEEL, metal=1, seg=6, axis="x", rz=90)


def axe(g):
    """A felling axe lying on the ground: a long handle and a steel head."""
    cyl(g, 0.016, 0.72, (0, 0, 0.022), WOOD_L, seg=6, axis="x", caps=WOOD)
    box(g, (0.05, 0.035, 0.18), (0.33, 0, 0.022), STEEL, metal=1, rx=90, taper=1.0)
    box(g, (0.03, 0.006, 0.1), (0.33, -0.13, 0.022), lin(200, 204, 210), metal=1, rx=90)
    box(g, (0.05, 0.036, 0.06), (0.33, 0, 0.022), RED, rx=90)


def hammer(g):
    """A claw hammer."""
    cyl(g, 0.014, 0.32, (0, 0, 0.018), WOOD_L, seg=6, axis="x", caps=WOOD)
    cyl(g, 0.017, 0.1, (-0.1, 0, 0.018), BLACK, seg=6, axis="x")
    box(g, (0.03, 0.028, 0.06), (0.16, 0, 0.018), STEEL_D, metal=1, rx=90)
    cyl(g, 0.016, 0.04, (0.16, 0.05, 0.018), STEEL, metal=1, seg=8, axis="x", rz=90)
    box(g, (0.022, 0.012, 0.07), (0.16, -0.06, 0.018), STEEL_D, metal=1, rx=70)


def sample(g):
    """A specimen: a capped vial of something green, in a small white case with a blue lid."""
    box(g, (0.14, 0.1, 0.07), (0, 0, 0), WHITE, top=lin(50, 90, 170))
    box(g, (0.145, 0.105, 0.01), (0, 0, 0.055), lin(50, 90, 170))
    cyl(g, 0.012, 0.08, (0.07, 0.09, 0.012), lin(110, 190, 70), seg=6, axis="x", rz=30, caps=lin(80, 150, 40))
    cyl(g, 0.014, 0.02, (0.03, 0.068, 0.014), RED, seg=6, axis="x", rz=30)
    box(g, (0.05, 0.002, 0.03), (0, -0.051, 0.02), lin(220, 190, 40))


def scrap(g):
    """Scrap: a bent sheet, a pipe, a gear, a bracket."""
    box(g, (0.3, 0.2, 0.006), (0, 0, 0.02), RUST, metal=0.6, ry=8, rz=10)
    box(g, (0.14, 0.2, 0.006), (-0.2, 0.03, 0.0), mul(RUST, 0.9), metal=0.6, ry=-30, rz=10)
    cyl(g, 0.02, 0.36, (0.05, 0.13, 0.02), STEEL, metal=1, seg=8, axis="x", rz=-20, caps=BLACK)
    cyl(g, 0.05, 0.02, (0.14, -0.1, 0), STEEL_D, metal=1, seg=10)
    for k in range(8):
        a = k * 45
        box(g, (0.025, 0.02, 0.02), (0.14 + 0.055 * math.cos(math.radians(a)),
                                     -0.1 + 0.055 * math.sin(math.radians(a)), 0), STEEL_D, metal=1, rz=a)
    box(g, (0.12, 0.03, 0.006), (-0.08, -0.12, 0.0), STEEL, metal=1, rz=50)
    box(g, (0.006, 0.03, 0.05), (-0.04, -0.075, 0.0), STEEL, metal=1, rz=50)


def fuel(g):
    """A red jerry can standing up: a body with pressed ribs, a handle, a spout."""
    box(g, (0.34, 0.16, 0.42), (0, 0, 0), RED, taper=0.98, top=mul(RED, 0.9))
    box(g, (0.3, 0.165, 0.03), (0, 0, 0.12), mul(RED, 0.8))
    box(g, (0.3, 0.165, 0.03), (0, 0, 0.26), mul(RED, 0.8))
    for x in (-0.06, 0.06):
        box(g, (0.02, 0.03, 0.06), (x, 0, 0.42), mul(RED, 0.85))
    box(g, (0.18, 0.03, 0.025), (0, 0, 0.475), mul(RED, 0.85))
    cyl(g, 0.022, 0.05, (0.13, 0, 0.42), BLACK, seg=8)
    cyl(g, 0.024, 0.014, (0.13, 0, 0.47), STEEL_D, metal=1, seg=8)


KINDS = {
    "wood": wood, "food": food, "meal": meal, "bandage": bandage, "medicine": medicine,
    "medkit": medkit, "weapon": weapon, "cloth": cloth, "coat": coat, "armour": armour,
    "boots": boots, "gun": gun, "ammo": ammo, "corpse": corpse, "book": book, "cart": cart,
    "axe": axe, "hammer": hammer, "sample": sample, "scrap": scrap, "fuel": fuel,
}


# --- variants: a few more models for the commonest kinds, named "<kind>_v1", "<kind>_v2"... The game picks one
# per item by a hash of its id (render side only: the sim knows only the kind). Each keeps its kind's size
# and reads as the same kind at a glance. Weapons keep the club's grip (x -0.29, 3.5 cm up): held in hand,
# the game puts the hand there.

def food_v1(g):
    """A loaf of bread and a jar of preserves."""
    box(g, (0.24, 0.12, 0.08), (0, -0.04, 0), lin(170, 118, 62), taper=0.85, top=lin(140, 88, 42))
    box(g, (0.2, 0.1, 0.02), (0, -0.04, 0.075), lin(150, 100, 50), taper=0.7)
    cyl(g, 0.04, 0.09, (0.04, 0.09, 0), lin(120, 30, 46), seg=10)
    cyl(g, 0.042, 0.02, (0.04, 0.09, 0.09), lin(200, 190, 160), metal=0.5, seg=10)


def food_v2(g):
    """A sack of rice, tied at the neck, and a box of pasta beside it."""
    box(g, (0.2, 0.14, 0.16), (0, 0, 0), lin(196, 178, 140), taper=0.8, top=lin(176, 160, 120))
    box(g, (0.05, 0.05, 0.05), (0, 0, 0.16), lin(176, 160, 120), taper=0.6)
    box(g, (0.1, 0.004, 0.06), (0, -0.072, 0.04), lin(60, 100, 150))
    box(g, (0.2, 0.06, 0.04), (0.06, 0.12, 0), lin(40, 70, 140), rz=20, top=lin(220, 200, 60))


def food_v3(g):
    """Three short tins of beans, two side by side and one on them, and one more on its side."""
    blue = lin(46, 84, 150)
    for x, y, z in ((-0.042, 0, 0), (0.042, 0.01, 0), (0.0, 0.005, 0.072)):
        cyl(g, 0.04, 0.07, (x, y, z), blue, caps=STEEL, seg=10)
        cyl(g, 0.041, 0.02, (x, y, z + 0.025), WHITE, seg=10)
    cyl(g, 0.04, 0.07, (0.02, -0.11, 0.04), lin(170, 120, 40), caps=STEEL, seg=10, axis="x", rz=70)


def meal_v1(g):
    """A bowl of soup with a spoon in it and a crust of bread."""
    cyl(g, 0.06, 0.06, (0, 0, 0), lin(200, 196, 186), seg=12, r2=0.09)
    cyl(g, 0.082, 0.004, (0, 0, 0.05), lin(170, 110, 50), seg=12)
    box(g, (0.14, 0.012, 0.006), (0.05, 0.0, 0.07), STEEL, metal=1, ry=-25)
    box(g, (0.1, 0.06, 0.04), (0.06, -0.12, 0), lin(170, 118, 62), rz=30, taper=0.8)


def bandage_v1(g):
    """A box of plasters, open, a few spilled, and a roll of tape."""
    box(g, (0.12, 0.08, 0.04), (0, 0, 0), WHITE, top=lin(190, 40, 40))
    box(g, (0.12, 0.004, 0.03), (0, 0.04, 0.04), WHITE, rx=-40)
    for k in range(3):
        box(g, (0.07, 0.02, 0.002), (0.13 + 0.01 * k, -0.04 + 0.025 * k, 0), lin(220, 180, 140), rz=15 * k)
    cyl(g, 0.03, 0.025, (-0.1, 0.06, 0), WHITE, seg=10, caps=CREAM)


def medicine_v1(g):
    """A brown glass bottle of cough syrup and a blister pack's box."""
    cyl(g, 0.032, 0.11, (0, 0, 0), lin(90, 50, 20), seg=10)
    cyl(g, 0.016, 0.02, (0, 0, 0.11), lin(90, 50, 20), seg=8)
    cyl(g, 0.019, 0.02, (0, 0, 0.13), WHITE, seg=8)
    box(g, (0.05, 0.002, 0.05), (0, -0.032, 0.03), WHITE)
    box(g, (0.12, 0.07, 0.025), (0.08, 0.07, 0), lin(60, 140, 160), rz=-20, top=WHITE)


def medicine_v2(g):
    """A white tub of tablets with a blue label, and two loose strips."""
    cyl(g, 0.04, 0.09, (0, 0, 0), WHITE, seg=10)
    cyl(g, 0.041, 0.04, (0, 0, 0.025), lin(50, 90, 170), seg=10)
    cyl(g, 0.043, 0.02, (0, 0, 0.09), WHITE, seg=10)
    for k in range(2):
        box(g, (0.09, 0.04, 0.004), (0.09, -0.04 + 0.06 * k, 0.002 * k), STEEL, metal=0.8, rz=25 - 40 * k)


def weapon_v1(g):
    """A length of pipe with a taped grip and a bend near the end."""
    cyl(g, 0.02, 0.58, (0.0, 0, 0.035), STEEL_D, metal=1, seg=8, axis="x", caps=BLACK)
    cyl(g, 0.02, 0.18, (0.36, 0.03, 0.035), STEEL_D, metal=1, seg=8, axis="x", rz=20, caps=BLACK)
    cyl(g, 0.024, 0.16, (-0.29, 0, 0.035), lin(150, 150, 140), seg=8, axis="x")
    cyl(g, 0.026, 0.03, (0.29, 0.0, 0.035), STEEL, metal=1, seg=8, axis="x")


def weapon_v2(g):
    """A crowbar: a bar with a claw at one end and a flat tip at the other."""
    red = lin(150, 30, 26)
    cyl(g, 0.014, 0.7, (0.0, 0, 0.035), red, metal=0.6, seg=6, axis="x", caps=STEEL_D)
    box(g, (0.07, 0.03, 0.012), (0.36, 0, 0.03), STEEL_D, metal=1, ry=-50)
    box(g, (0.05, 0.034, 0.012), (0.39, 0, 0.075), STEEL_D, metal=1, ry=-120)
    box(g, (0.06, 0.028, 0.008), (-0.37, 0, 0.03), STEEL_D, metal=1, ry=12)


def cloth_v1(g):
    """A blanket rolled up and tied with string."""
    cyl(g, 0.07, 0.42, (0, 0, 0.07), lin(120, 40, 36), seg=10, axis="x", caps=lin(150, 60, 50))
    for x in (-0.12, 0.12):
        cyl(g, 0.072, 0.012, (x, 0, 0.07), ROPE, seg=10, axis="x")


def cloth_v2(g):
    """A heap of rags in different colours."""
    rags = ((0, 0, 0, 10, lin(140, 130, 110)), (0.06, 0.04, 0.02, -25, lin(70, 80, 100)),
            (-0.05, 0.03, 0.035, 50, lin(150, 140, 60)), (0.02, -0.05, 0.045, -70, lin(110, 60, 56)))
    for k, (x, y, z, a, c) in enumerate(rags):
        box(g, (0.2 - 0.02 * k, 0.12, 0.025), (x, y, z), c, rz=a, taper=0.8)


def coat_v1(g):
    """A navy anorak laid flat: its sleeves out, the hood at the top, the zip down the middle."""
    c = lin(46, 56, 80)
    box(g, (0.46, 0.4, 0.05), (0, 0, 0), c)
    for y in (-0.1, 0.1):
        box(g, (0.36, 0.09, 0.035), (0.02, y * 2.0, 0.0), mul(c, 0.9), rz=y * 60)
    box(g, (0.14, 0.2, 0.05), (0.29, 0, 0.0), mul(c, 0.8), taper=0.8)
    box(g, (0.44, 0.012, 0.004), (0, 0, 0.05), STEEL_D, metal=1)


def boots_v1(g):
    """A pair of green wellingtons standing side by side, one a little turned: a sole and toe, a tall leg."""
    green = lin(52, 74, 46)
    for (x, y, a) in ((0, -0.065, 0), (0.02, 0.07, 14)):
        sub = Geo()
        box(sub, (0.27, 0.1, 0.025), (0.02, 0, 0), BLACK)
        box(sub, (0.24, 0.095, 0.07), (0.03, 0, 0.025), mul(green, 0.85), taper=0.85)
        cyl(sub, 0.052, 0.32, (-0.05, 0, 0.025), green, seg=8, r2=0.056, caps=mul(green, 0.5))
        m = place((x, y, 0), a)
        base = len(g.v)
        g.v.extend(m @ v for v in sub.v)
        g.f.extend(([base + i for i in idx], c, mt) for idx, c, mt in sub.f)


def ammo_v1(g):
    """A box of shotgun shells, open, red shells standing in it and one spilled."""
    box(g, (0.12, 0.08, 0.05), (0, 0, 0), lin(190, 170, 120), top=lin(130, 40, 30))
    for k in range(3):
        for j in (-0.018, 0.018):
            cyl(g, 0.01, 0.03, (-0.03 + 0.03 * k, j, 0.05), lin(170, 36, 30), seg=6, caps=BRASS)
    cyl(g, 0.01, 0.07, (0.1, -0.06, 0.01), lin(170, 36, 30), seg=6, axis="x", rz=40, caps=BRASS)


def book_v1(g):
    """A paperback lying open, face down, its spine up like a little roof."""
    c = lin(170, 70, 40)
    box(g, (0.11, 0.17, 0.004), (-0.05, 0, 0.0), c, ry=-12)
    box(g, (0.11, 0.17, 0.004), (0.05, 0, 0.0), c, ry=12)
    box(g, (0.2, 0.16, 0.01), (0, 0, 0.0), CREAM)
    box(g, (0.012, 0.17, 0.006), (0, 0, 0.022), mul(c, 0.7))


def book_v2(g):
    """A stack of three books, each a little turned."""
    for k, (c, a) in enumerate(((lin(30, 60, 100), 0), (lin(130, 30, 30), 12), (lin(60, 60, 60), -8))):
        box(g, (0.23 - 0.02 * k, 0.16 - 0.01 * k, 0.035), (0, 0, 0.035 * k), c, rz=a, top=c)
        box(g, (0.005, 0.15 - 0.01 * k, 0.029), (0.11 - 0.01 * k, 0, 0.035 * k + 0.003), CREAM, rz=a)


def scrap_v1(g):
    """A hubcap and a coil of copper wire."""
    cyl(g, 0.17, 0.02, (0, 0, 0), STEEL, metal=1, seg=12, r2=0.12, caps=lin(180, 182, 186))
    cyl(g, 0.04, 0.025, (0, 0, 0.02), STEEL_D, metal=1, seg=8)
    for k in range(3):
        cyl(g, 0.08 - 0.008 * k, 0.008, (0.2, 0.1, 0.008 * k), lin(180, 100, 50), metal=1, seg=10)


def scrap_v2(g):
    """Two lengths of angle iron and loose bolts, rusted."""
    for k, a in enumerate((5, 35)):
        box(g, (0.45, 0.04, 0.004), (0, 0.05 * k, 0), RUST, metal=0.6, rz=a)
        box(g, (0.45, 0.004, 0.04), (0, 0.05 * k - 0.02, 0), mul(RUST, 0.85), metal=0.6, rz=a)
    for x, y in ((0.15, -0.1), (0.19, -0.07), (-0.12, -0.1)):
        cyl(g, 0.008, 0.04, (x, y, 0.008), STEEL_D, metal=1, seg=6, axis="x", rz=x * 300)
        cyl(g, 0.013, 0.008, (x, y, 0.0), STEEL_D, metal=1, seg=6)


def wood_v1(g):
    """Split firewood: three logs, two side by side and one across."""
    for y in (-0.06, 0.06):
        cyl(g, 0.055, 0.42, (0, y, 0.055), BARK, seg=6, axis="x", caps=WOOD_L)
    cyl(g, 0.05, 0.4, (0.02, 0, 0.15), BARK, seg=6, axis="x", rz=25, caps=WOOD_L)


def wood_v2(g):
    """Two planks nailed to crosspieces: a broken bit of pallet."""
    for y in (-0.08, 0.08):
        box(g, (0.7, 0.1, 0.02), (0, y, 0.025), WOOD_L, top=lin(196, 160, 110))
    box(g, (0.1, 0.32, 0.025), (-0.2, 0, 0), WOOD_D)
    box(g, (0.1, 0.2, 0.025), (0.22, -0.05, 0), WOOD_D, rz=10)


VARIANTS = {
    "food": [food_v1, food_v2, food_v3], "meal": [meal_v1], "bandage": [bandage_v1],
    "medicine": [medicine_v1, medicine_v2], "weapon": [weapon_v1, weapon_v2], "cloth": [cloth_v1, cloth_v2],
    "coat": [coat_v1], "boots": [boots_v1], "ammo": [ammo_v1], "book": [book_v1, book_v2],
    "scrap": [scrap_v1, scrap_v2], "wood": [wood_v1, wood_v2],
}


# --- the hand: a gloved fist round a handle and a sleeved forearm, for what you hold in first person
# (godot/World3D/Held.cs), a stand-in until the first-person arms rig (docs/characters.md). The handle runs
# along X through the origin (where an item's grip goes), the knuckles up (+Z); the forearm runs from the
# wrist out along -Y (in the game the mesh's +Z, towards you).

GLOVE = lin(62, 54, 44)
SLEEVE = lin(58, 64, 56)


def hand(g):
    box(g, (0.085, 0.07, 0.08), (0, 0, -0.04), GLOVE, taper=0.92, top=mul(GLOVE, 1.15))
    for k in range(4):
        box(g, (0.019, 0.012, 0.022), (-0.03 + 0.02 * k, 0.035, 0.0), mul(GLOVE, 0.8))
    box(g, (0.05, 0.022, 0.022), (0.02, 0.02, 0.025), mul(GLOVE, 1.08), rz=-15)
    cyl(g, 0.034, 0.08, (0, -0.03, 0), GLOVE, seg=8, rx=90, caps=GLOVE)
    cyl(g, 0.045, 0.32, (0, -0.1, 0), SLEEVE, seg=8, rx=90, r2=0.052, caps=mul(SLEEVE, 0.8))


# --- out to Blender, the glTF and a preview ---

def material():
    mat = bpy.data.materials.new("items")
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
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    cols = 9
    for k, ob in enumerate(objs):
        ob.location = ((k % cols) * 1.3, (k // cols) * 1.3, 0)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (3.9, -4.0, 6.5)
    cam.rotation_euler = (math.radians(50), 0, 0)
    cam.data.lens = 24
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    glb = opts.get("glb", "godot/assets/items/items.glb")
    os.makedirs(os.path.dirname(os.path.abspath(glb)), exist_ok=True)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    mat = material()
    objs = []
    models = []
    for name, fn in KINDS.items():
        models.append((name, fn))
        models += [(f"{name}_v{k + 1}", v) for k, v in enumerate(VARIANTS.get(name, []))]
    models.append(("hand", hand))
    for name, fn in models:
        g = Geo()
        fn(g)
        objs.append(to_object(name, g, mat))
        print(f"ITEM {name}: {g.tris()} triangles")
    bpy.ops.export_scene.gltf(filepath=os.path.abspath(glb), export_format="GLB", use_selection=False,
                              export_vertex_color="ACTIVE", export_normals=True, export_materials="EXPORT",
                              export_apply=True)
    print(f"ITEM glb -> {glb}")
    if "preview" in opts:
        preview(os.path.abspath(opts["preview"]), objs)


main()
