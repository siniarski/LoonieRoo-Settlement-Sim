# The things of the world that had no model in first person (2026-11-11): farm plots and their crops by stage,
# building sites, the helicopter, a caravan's handcart and a trader's goods. All by script, run headless (~2 s):
#
#   "$B" --background --factory-startup --python art/scripts/build_world.py -- \
#       --glb godot/assets/world/world.glb [--preview lineup.png]
#
# What the sim has, and the model each is drawn with (godot/World3D/Fields.cs, Sites.cs, Aircraft.cs, Others.cs):
#   a farm plot (Farming.cs)   soil when nothing is planted; <crop>_1 sprouting, <crop>_2 growing, <crop>_3 ripe,
#                              for potato, wheat and herb (each on its tilled bed)
#   a blueprint (Designations) site (a tile pegged out: furniture, a floor), site_edge (an edge pegged out), then as
#                              the work goes on frame (a wall or barricade), frame_door (a door), joists (a floor);
#                              planks once the material is delivered
#   the helicopter (Timeline)  heli, and heli_rotor apart so the game can spin it
#   a caravan (Caravans.cs)    wagon: a loaded handcart; a trader (Visitors.cs): stall, goods laid out on a blanket
#
# Like the furniture: boxes, cylinders and cones, flat shaded, vertex colours with metalness in the alpha (lowpoly.py).
# Blender space: Z up, resting on z = 0, centred on the origin, real sizes in metres. What lies on a tile fits in
# 1 x 1 m. What has a front (the helicopter, the handcart) points along +X, as the cars do. An edge piece (site_edge,
# frame, frame_door) runs along X, centred, so the game lays it along a tile's border. No randomness.

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lowpoly import box, build, cyl, lin, mat, mul, plain  # noqa: E402

EARTH = mat("earth", lin(52, 40, 31))
EARTH_L = mat("earth", lin(66, 51, 39))
LEAF = lin(62, 108, 52)
LEAF_D = lin(46, 84, 44)
LEAF_L = lin(104, 146, 70)
STRAW = lin(206, 176, 92)
STRAW_D = lin(176, 142, 66)
WHEAT_G = lin(120, 150, 70)
BLOOM = lin(150, 110, 190)
PETAL = lin(232, 228, 214)
WOOD = mat("rough", lin(170, 134, 90))
WOOD_D = mat("rough", lin(120, 90, 58))
TAPE = lin(214, 96, 40)
STEEL_D = mat("metal", lin(70, 72, 78))
BLACK = lin(28, 28, 30)
GLASS = plain(lin(60, 84, 96))
OLIVE = mat("body", lin(84, 92, 62))
OLIVE_D = mat("body", lin(60, 68, 46))
SACK = mat("cloth", lin(176, 156, 112))
TARP = mat("cloth", lin(66, 86, 96))
RED = lin(160, 40, 34)
CLOTH = mat("cloth", lin(128, 70, 60))
TIN = mat("metal", lin(170, 172, 168))


# --- farm plots ---

def soil(g):
    """A tilled bed: four ridges of dark earth along X, a lighter crest on each."""
    for k in range(4):
        box(g, (0.94, 0.19, 0.07), (0, -0.36 + 0.24 * k, 0), EARTH, taper=0.72, top=EARTH_L)


def leaf(g, x, y, z, length, width, turn, tilt, col):
    """A leaf from (x, y, z): a blade tapering to a point, leaning `tilt` degrees out the way `turn` (degrees) points."""
    box(g, (width, width * 0.3, length), (x, y, z), col, rz=turn, ry=tilt, taper=0.15, top=mul(col, 1.15))


def potato_plant(g, x, y, s, flower):
    n = 4 if s < 0.5 else 6
    for k in range(n):
        turn = k * 360 / n + (x * 170 + y * 250) % 60
        leaf(g, x, y, 0.06, 0.26 * s, 0.085 * s + 0.01, turn, 38 if s > 0.5 else 22, LEAF_D if k % 2 else LEAF)
    leaf(g, x, y, 0.06, 0.2 * s, 0.07 * s + 0.01, 30, 4, LEAF_L)
    if flower:
        box(g, (0.035, 0.035, 0.012), (x + 0.02, y - 0.01, 0.06 + 0.2 * s), PETAL, top=lin(226, 196, 70))


def potato(s, flower=False):
    def make(g):
        soil(g)
        for row in (-0.24, 0.24):
            for k in range(3):
                potato_plant(g, -0.3 + 0.3 * k, row, s * (0.92 + 0.08 * ((k + (row > 0)) % 2)), flower and k != 1)
    return make


def wheat_tuft(g, x, y, h, col, ear):
    for k in range(3):
        turn = k * 120 + (x * 310 + y * 190) % 90
        tilt = 4 + 3 * k
        box(g, (0.014, 0.014, h), (x, y, 0.06), col, rz=turn, ry=tilt, taper=0.6)
        if ear:
            t, a = math.radians(tilt), math.radians(turn)
            box(g, (0.03, 0.03, 0.09), (x + h * math.sin(t) * math.cos(a), y + h * math.sin(t) * math.sin(a), 0.06 + h * math.cos(t) - 0.01),
                STRAW, rz=turn, ry=tilt + 14, taper=0.4, top=STRAW_D)


def wheat(h, col, ear=False):
    def make(g):
        soil(g)
        for r in range(4):
            for k in range(5):
                wheat_tuft(g, -0.38 + 0.19 * k + (0.05 if r % 2 else 0), -0.36 + 0.24 * r, h * (0.94 + 0.06 * ((k + r) % 3)), col, ear)
    return make


def herb(s, bloom=False):
    def make(g):
        soil(g)
        for r in range(4):
            for k in range(3):
                x, y = -0.3 + 0.3 * k + (0.08 if r % 2 else 0) - 0.04, -0.36 + 0.24 * r
                cyl(g, 0.085 * s + 0.015, 0.11 * s + 0.02, (x, y, 0.06), LEAF if (k + r) % 2 else LEAF_L, seg=6, r2=0.035 * s, caps=LEAF_L)
                if bloom:
                    for a in (20, 140, 260):
                        box(g, (0.022, 0.022, 0.05), (x + 0.03 * math.cos(math.radians(a)), y + 0.03 * math.sin(math.radians(a)), 0.06 + 0.11 * s),
                            BLOOM, taper=0.5, top=mul(BLOOM, 1.2))
    return make


# --- building sites ---

def stake(g, x, y, h=0.5):
    box(g, (0.04, 0.04, h), (x, y, 0), WOOD, taper=0.85, top=TAPE)


def site(g):
    """A tile pegged out: a stake at each corner and tape strung between them."""
    c = 0.44
    for sx in (-1, 1):
        for sy in (-1, 1):
            stake(g, sx * c, sy * c)
    for s in (-1, 1):
        box(g, (2 * c, 0.012, 0.03), (0, s * c, 0.4), TAPE)
        box(g, (0.012, 2 * c, 0.03), (s * c, 0, 0.4), TAPE)


def site_edge(g):
    """A border pegged out: three stakes along X and tape between them."""
    for x in (-0.46, 0, 0.46):
        stake(g, x, 0)
    box(g, (0.92, 0.012, 0.03), (0, 0, 0.4), TAPE)


def frame(g):
    """A stud wall going up along X: a sole plate, four studs, a top plate, a brace across."""
    box(g, (1.0, 0.09, 0.045), (0, 0, 0), WOOD_D)
    for x in (-0.4775, -0.16, 0.16, 0.4775):
        box(g, (0.045, 0.09, 2.36), (x, 0, 0.045), WOOD, top=WOOD_D)
    box(g, (1.0, 0.09, 0.045), (0, 0, 2.405), WOOD_D)
    box(g, (0.045, 0.02, 1.2), (-0.3, -0.055, 0.5), mul(WOOD, 0.9), ry=38)


def frame_door(g):
    """A doorway framed along X: full studs at the ends, a header at door height, short studs over it."""
    for x in (-0.4775, 0.4775):
        box(g, (0.045, 0.09, 2.36), (x, 0, 0.045), WOOD, top=WOOD_D)
        box(g, (0.045, 0.09, 2.05), (x * 0.9, 0, 0), mul(WOOD, 0.94))
    box(g, (0.91, 0.09, 0.09), (0, 0, 2.05), WOOD_D)
    for x in (-0.2, 0.2):
        box(g, (0.045, 0.09, 0.265), (x, 0, 2.14), WOOD)
    box(g, (1.0, 0.09, 0.045), (0, 0, 2.405), WOOD_D)


def joists(g):
    """A floor being laid: three joists along X and the first boards across them."""
    for y in (-0.36, 0, 0.36):
        box(g, (0.98, 0.07, 0.06), (0, y, 0), WOOD_D)
    for k, x in enumerate((-0.4, -0.24, -0.08)):
        box(g, (0.15, 0.96, 0.022), (x, 0, 0.06), WOOD if k % 2 else mul(WOOD, 1.08))


def planks(g):
    """The material delivered: a short stack of planks, the top ones askew, and a box of nails."""
    for k in range(5):
        box(g, (0.9, 0.13, 0.03), (0, -0.07 + 0.14 * (k % 2) - (0.02 if k > 2 else 0), 0.03 * (k // 2)), WOOD if k % 2 else mul(WOOD, 1.1),
            rz=(0, 0, 3, -5, 9)[k])
    box(g, (0.12, 0.09, 0.06), (0.3, 0.3, 0), mat("wood", lin(110, 96, 70)), top=STEEL_D)


# --- the helicopter (nose towards +X) ---

def heli(g):
    """A utility helicopter: a cabin with a glazed nose, a tail boom with a fin and a tail rotor, skids."""
    body = OLIVE
    box(g, (3.0, 1.7, 1.6), (0.0, 0, 0.55), body, metal=0.4, taper=0.82, top=OLIVE_D)
    box(g, (1.3, 1.5, 1.2), (1.9, 0, 0.6), body, metal=0.4, taper=0.55, top=GLASS)
    box(g, (0.03, 1.2, 0.8), (2.36, 0, 0.95), GLASS, metal=0.7, ry=-28)
    for sy in (-1, 1):
        box(g, (1.5, 0.02, 0.6), (0.6, sy * 0.8, 1.2), GLASS, metal=0.7, rx=sy * 8)
        box(g, (0.9, 0.03, 1.0), (-0.6, sy * 0.86, 0.7), OLIVE_D, metal=0.4)
    box(g, (1.2, 1.0, 0.45), (-0.2, 0, 2.05), OLIVE_D, metal=0.5, taper=0.7)
    cyl(g, 0.09, 0.32, (0, 0, 2.45), STEEL_D, metal=1, seg=8)
    box(g, (4.2, 0.34, 0.34), (-3.4, 0, 1.45), body, metal=0.4, taper=0.6)
    box(g, (0.7, 0.07, 1.3), (-5.2, 0, 1.4), OLIVE_D, metal=0.4, ry=-18, taper=0.6)
    box(g, (0.9, 1.3, 0.05), (-4.9, 0, 1.6), OLIVE_D, metal=0.4)
    cyl(g, 0.06, 0.14, (-5.35, 0.1, 2.25), STEEL_D, metal=1, seg=6, axis="y")
    box(g, (0.09, 0.02, 1.3), (-5.35, 0.18, 1.6), BLACK)
    box(g, (1.3, 0.02, 0.09), (-5.35, 0.18, 2.205), BLACK)
    for sy in (-1, 1):
        cyl(g, 0.045, 3.4, (0.2, sy * 0.95, 0.05), STEEL_D, metal=1, seg=6, axis="x")
        for x in (-0.7, 1.0):
            box(g, (0.06, 0.06, 0.62), (x, sy * 0.86, 0.06), STEEL_D, metal=1, rx=-sy * 12)
    box(g, (0.5, 1.72, 0.25), (-0.3, 0, 0.75), RED, metal=0.3)


def heli_rotor(g):
    """The main rotor about the origin (the game spins it): a hub and two long blades, drooping a little."""
    cyl(g, 0.16, 0.1, (0, 0, -0.02), STEEL_D, metal=1, seg=8)
    for s in (-1, 1):
        box(g, (4.9, 0.32, 0.035), (s * 2.55, 0, 0.02), BLACK, metal=0.3, ry=s * 1.6, top=lin(52, 52, 56))
        box(g, (0.5, 0.33, 0.036), (s * 4.75, 0, 0.02 - 0.133), lin(200, 170, 40), ry=s * 1.6)


# --- others' things ---

def wagon(g):
    """A caravan's handcart (front and shafts towards +X): a plank bed on two big wheels, loaded with sacks, a
    crate and a rolled tarp, lashed down."""
    box(g, (1.4, 0.82, 0.05), (0, 0, 0.5), WOOD_D)
    for sy in (-1, 1):
        box(g, (1.4, 0.04, 0.22), (0, sy * 0.39, 0.55), WOOD, top=WOOD_D)
        cyl(g, 0.42, 0.07, (-0.05, sy * 0.47, 0.42), WOOD_D, seg=12, axis="y", caps=WOOD)
        cyl(g, 0.08, 0.1, (-0.05, sy * 0.47, 0.42), STEEL_D, metal=1, seg=8, axis="y")
        box(g, (1.0, 0.045, 0.045), (1.15, sy * 0.33, 0.5), WOOD, ry=6)
    box(g, (0.04, 0.82, 0.22), (-0.68, 0, 0.55), WOOD)
    box(g, (0.045, 0.7, 0.045), (1.62, 0, 0.44), WOOD)
    box(g, (0.06, 0.06, 0.5), (0.62, 0, 0.0), WOOD_D)
    box(g, (0.55, 0.36, 0.26), (-0.32, -0.16, 0.55), SACK, taper=0.82, rz=6)
    box(g, (0.5, 0.34, 0.24), (-0.3, 0.2, 0.55), mul(SACK, 0.9), taper=0.8, rz=-8)
    box(g, (0.5, 0.34, 0.2), (-0.3, 0.02, 0.8), SACK, taper=0.8, rz=14)
    box(g, (0.42, 0.42, 0.36), (0.36, -0.12, 0.55), WOOD, top=mul(WOOD, 1.1))
    cyl(g, 0.1, 0.7, (0.3, 0.26, 0.65), TARP, seg=8, axis="x", caps=mul(TARP, 0.8))
    box(g, (0.02, 0.86, 0.012), (-0.3, 0, 1.0), mat("fine", lin(170, 150, 104)))


def stall(g):
    """A trader's goods on a blanket: tins, a bottle or two, a crate, folded cloth; the pack they came in beside it."""
    box(g, (1.0, 0.7, 0.012), (0, 0, 0), CLOTH, top=mul(CLOTH, 1.1))
    box(g, (0.96, 0.05, 0.013), (0, -0.3, 0), mat("cloth", lin(196, 176, 120)))
    for k in range(4):
        cyl(g, 0.038, 0.1, (-0.36 + 0.11 * k, -0.14, 0.012), TIN if k % 2 else lin(150, 60, 50), metal=0.8, seg=8, caps=TIN)
    for x in (0.2, 0.3):
        cyl(g, 0.03, 0.17, (x, -0.16, 0.012), lin(60, 96, 62), metal=0.3, seg=6)
        cyl(g, 0.012, 0.06, (x, -0.16, 0.18), lin(60, 96, 62), metal=0.3, seg=6)
    box(g, (0.3, 0.22, 0.16), (-0.28, 0.14, 0.012), WOOD, top=WOOD_D)
    box(g, (0.24, 0.2, 0.05), (0.1, 0.14, 0.012), mat("cloth", lin(96, 92, 110)))
    box(g, (0.24, 0.2, 0.05), (0.1, 0.14, 0.062), mat("cloth", lin(150, 140, 110)), rz=6)
    box(g, (0.34, 0.24, 0.5), (0.62, 0.3, 0), mat("cloth", OLIVE), taper=0.85, top=mat("cloth", OLIVE_D))
    box(g, (0.3, 0.06, 0.2), (0.62, 0.16, 0.08), mat("cloth", OLIVE_D))
    cyl(g, 0.07, 0.36, (0.62, 0.3, 0.57), TARP, seg=8, axis="x")


MODELS = [
    ("soil", soil),
    ("potato_1", potato(0.35)), ("potato_2", potato(0.75)), ("potato_3", potato(1.0, flower=True)),
    ("wheat_1", wheat(0.16, WHEAT_G)), ("wheat_2", wheat(0.5, WHEAT_G)), ("wheat_3", wheat(0.78, STRAW_D, ear=True)),
    ("herb_1", herb(0.35)), ("herb_2", herb(0.8)), ("herb_3", herb(1.1, bloom=True)),
    ("site", site), ("site_edge", site_edge), ("frame", frame), ("frame_door", frame_door), ("joists", joists),
    ("planks", planks),
    ("heli", heli), ("heli_rotor", heli_rotor),
    ("wagon", wagon), ("stall", stall),
]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    build(MODELS, "WORLD", opts.get("glb", "godot/assets/world/world.glb"), opts.get("preview"))


main()
