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
    cols = 7
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
    for name, fn in KINDS.items():
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
