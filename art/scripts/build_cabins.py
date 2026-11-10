# A car from the driver's seat (2026-11-11): what you see sitting in the car you drive. The cars' outsides are
# build_furniture.py's (car, car_v1, truck); seen from inside those are a shell with painted glass, so the car you
# sit in is drawn with one of these instead (godot/World3D/Cockpit.cs). All by script, run headless (~1 s):
#
#   "$B" --background --factory-startup --python art/scripts/build_cabins.py -- \
#       --glb godot/assets/cars/cabins.glb [--preview lineup.png]
#
#   cabin, cabin_v1   the sedan's inside (the red one and the blue one: the bonnet is the car's colour)
#   cabin_truck       the pickup's: the same cab moved forward and up, a higher bonnet, a wall and a window behind
#                     the seats, and the load bed beyond it
#   steeringwheel     about its own hub (the origin), its axis along +X, so the game tilts it and turns it
#
# What is in a cabin: the floor, the dashboard with a binnacle (the instruments are drawn by the game on a panel in
# it: Cockpit.cs), the steering column and stalks, a radio, vents and heater knobs, a glovebox, the centre console
# with the gear lever and handbrake, the seats, door panels with armrests, the pillars and roof lining, sun visors,
# the mirror, the scuttle with the wipers, the bonnet and the wing mirrors. No glass: you look out through nothing.
#
# The same space as the car's outside: Blender Z up, the car's centre on the ground at the origin, its front
# towards +X, its left (the driver's side) towards +Y. The driver's eye is at EYE; the sim seats the driver the same
# distance ahead of the centre (cs/Sim/Movement/VehicleBody.cs Shape.seat). No randomness.

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lowpoly import beam, box, build, cyl, lin, mul  # noqa: E402

TRIM = lin(96, 96, 100)
TRIM_L = lin(128, 128, 130)
PLASTIC = lin(50, 50, 54)
CARPET = lin(66, 62, 60)
SEAT = lin(96, 78, 62)
SEAT_D = lin(74, 60, 48)
LINING = lin(168, 162, 150)
STEEL = lin(150, 152, 156)
CHROME = lin(190, 192, 196)
MIRROR = lin(120, 140, 152)
DISPLAY = lin(40, 70, 52)
RED = lin(160, 40, 34)

# The sedan's driver's eye (m): the game puts the camera here (Cockpit.cs EYE), the pickup's is this plus its shift.
EYE = (0.04, 0.38, 1.08)
# Where the wheel's hub is, and the middle of the instrument panel (Cockpit.cs has the same numbers).
HUB = (0.54, 0.38, 0.845)
PANEL = (0.72, 0.38, 0.92)


def cabin(body, dx=0.0, dz=0.0, hw=0.74, back=-1.30, bonnet=1.14, bonnet_w=0.83, bench=True, bed=None):
    """A cabin: `body` the car's colour, (dx, dz) where it sits in the car (the sedan's is 0, 0), `hw` its half
    width inside, `back` how far behind the centre it ends, `bonnet` the bonnet's length and `bonnet_w` its half
    width, `bench` a back seat, `bed` the colour of a load bed behind (a pickup)."""

    def make(g):
        def P(x, y, z):
            return (x + dx, y, z + dz)

        floor = 0.30
        sill = 0.86
        roof = 1.22
        length = 0.92 - back

        # The floor, and the bulkhead and dashboard across the front of it.
        box(g, (length, 2 * hw + 0.1, 0.03), P((0.92 + back) / 2, 0, floor - 0.03), CARPET)
        box(g, (0.30, 2 * hw, sill - floor), P(0.77, 0, floor), TRIM, top=TRIM_L)
        box(g, (0.10, 2 * hw, 0.03), P(0.95, 0, sill - 0.05), PLASTIC)
        # The binnacle over the instruments: a hood, two cheeks, a back.
        box(g, (0.14, 0.46, 0.02), P(0.75, PANEL[1], 1.02), TRIM)
        for s in (-1, 1):
            box(g, (0.14, 0.02, 0.17), P(0.75, PANEL[1] + s * 0.225, sill), TRIM)
        box(g, (0.02, 0.46, 0.17), P(0.815, PANEL[1], sill), PLASTIC)
        # On the dashboard's face: a radio and its display, two centre vents, three heater knobs, a glovebox, the
        # vents at each end, an ashtray.
        face = 0.615
        box(g, (0.012, 0.21, 0.07), P(face, 0.0, 0.64), PLASTIC)
        box(g, (0.004, 0.10, 0.03), P(face - 0.008, 0.03, 0.66), DISPLAY)
        for k in range(2):
            cyl(g, 0.012, 0.02, P(face - 0.012, -0.07 + 0.035 * k, 0.675), CHROME, metal=1, seg=6, axis="x")
        for y in (-0.085, 0.085, -0.62, 0.64):
            box(g, (0.012, 0.13, 0.05), P(face, y, 0.775), PLASTIC)
            for k in range(3):
                box(g, (0.004, 0.12, 0.004), P(face - 0.008, y, 0.785 + 0.014 * k), TRIM_L)
        for k in range(3):
            cyl(g, 0.016, 0.022, P(face - 0.012, -0.06 + 0.06 * k, 0.585), PLASTIC, seg=8, axis="x", caps=CHROME)
        box(g, (0.012, 0.40, 0.17), P(face, -0.40, 0.50), TRIM_L)
        box(g, (0.012, 0.07, 0.015), P(face - 0.01, -0.40, 0.635), CHROME, metal=1)
        box(g, (0.012, 0.12, 0.04), P(face, 0.0, 0.52), TRIM_L)
        # The steering column from the dashboard to the hub, and the two stalks.
        beam(g, P(0.68, HUB[1], 0.76), P(HUB[0] + 0.02, HUB[1], HUB[2] - 0.01), 0.07, 0.07, PLASTIC)
        beam(g, P(0.60, HUB[1], 0.80), P(0.585, HUB[1] + 0.21, 0.835), 0.016, 0.016, PLASTIC)
        beam(g, P(0.60, HUB[1], 0.80), P(0.585, HUB[1] - 0.20, 0.825), 0.016, 0.016, PLASTIC)
        # The centre console: the gear lever in its gate, the handbrake.
        box(g, (0.62, 0.17, 0.20), P(0.30, 0, floor), TRIM, top=TRIM_L)
        box(g, (0.16, 0.08, 0.006), P(0.30, 0, floor + 0.20), CHROME, metal=1)
        beam(g, P(0.30, 0, floor + 0.20), P(0.25, 0.01, floor + 0.36), 0.016, 0.016, STEEL, metal=1)
        box(g, (0.045, 0.045, 0.05), P(0.245, 0.01, floor + 0.35), PLASTIC, taper=0.7)
        beam(g, P(0.02, 0, floor + 0.20), P(0.20, 0, floor + 0.27), 0.03, 0.03, PLASTIC)
        # The seats: yours and the passenger's.
        for y in (EYE[1], -EYE[1]):
            box(g, (0.50, 0.48, 0.13), P(0.04, y, floor + 0.08), SEAT, top=SEAT_D, ry=-4)
            box(g, (0.11, 0.46, 0.58), P(-0.22, y, floor + 0.14), SEAT, ry=-14, taper=0.9)
            box(g, (0.07, 0.22, 0.15), P(-0.345, y, floor + 0.74), SEAT_D, ry=-10, taper=0.85)
            box(g, (0.52, 0.06, 0.07), P(0.04, y + (0.27 if y > 0 else -0.27), floor + 0.02), PLASTIC)
        if bench:
            box(g, (0.50, 2 * hw - 0.08, 0.13), P(-0.85, 0, floor + 0.08), SEAT, top=SEAT_D, ry=-4)
            box(g, (0.11, 2 * hw - 0.08, 0.52), P(-1.12, 0, floor + 0.14), SEAT, ry=-16, taper=0.95)
            box(g, (0.24, 2 * hw, 0.02), P(-1.21, 0, sill - 0.02), TRIM_L)
        # The doors: a panel each side up to the window line, an armrest and a pull, a winder.
        for s in (-1, 1):
            y = s * (hw + 0.02)
            box(g, (length, 0.04, sill - floor), P((0.92 + back) / 2, y, floor), TRIM_L, top=TRIM)
            box(g, (0.34, 0.06, 0.035), P(0.12, y - s * 0.045, 0.62), TRIM)
            box(g, (0.10, 0.012, 0.02), P(0.36, y - s * 0.026, 0.74), CHROME, metal=1)
            cyl(g, 0.022, 0.03, P(0.30, y - s * 0.03, 0.52), CHROME, metal=1, seg=6, axis="y")
            # The pillars: at the windscreen, behind your shoulder, and at the back.
            top = s * (hw - 0.10)
            beam(g, P(0.92, s * hw, sill), P(0.42, top, roof), 0.07, 0.05, TRIM)
            beam(g, P(-0.26, s * hw, sill), P(-0.26, top, roof), 0.09, 0.05, TRIM)
            beam(g, P(back + 0.06, s * hw, sill), P(back + 0.16, top, roof), 0.10, 0.05, TRIM)
            # The rail along the roof's edge, and the wing mirror out past the pillar.
            box(g, (0.42 - back - 0.16, 0.05, 0.04), P((0.42 + back + 0.16) / 2, top, roof - 0.03), TRIM)
            box(g, (0.09, 0.15, 0.10), P(0.84, s * (hw + 0.17), sill + 0.01), body, metal=0.6)
            box(g, (0.004, 0.13, 0.08), P(0.792, s * (hw + 0.17), sill + 0.02), MIRROR, metal=0.8)
        # The roof lining, the rail over the windscreen, two sun visors, the mirror.
        box(g, (0.42 - back - 0.16, 2 * (hw - 0.10), 0.02), P((0.42 + back + 0.16) / 2, 0, roof), LINING)
        box(g, (0.05, 2 * (hw - 0.10), 0.05), P(0.42, 0, roof - 0.04), TRIM)
        for y in (EYE[1], -EYE[1]):
            box(g, (0.15, 0.34, 0.016), P(0.32, y, roof - 0.022), TRIM_L)
        beam(g, P(0.41, 0, roof - 0.03), P(0.40, 0, roof - 0.07), 0.02, 0.02, PLASTIC)
        box(g, (0.03, 0.23, 0.06), P(0.40, 0, roof - 0.13), PLASTIC)
        box(g, (0.004, 0.21, 0.045), P(0.383, 0, roof - 0.123), MIRROR, metal=0.8)
        # Past the windscreen: the scuttle, the wipers lying on it, the bonnet with a crease down its middle.
        box(g, (0.08, 2 * bonnet_w, 0.02), P(0.975, 0, 0.785), PLASTIC)
        for y, rz in ((0.36, 4), (-0.26, 4)):
            box(g, (0.016, 0.46, 0.012), P(0.955, y, 0.812), PLASTIC, rz=rz)
            cyl(g, 0.012, 0.02, P(0.975, y - 0.22, 0.80), PLASTIC, seg=6)
        box(g, (bonnet, 2 * bonnet_w, 0.05), P(1.0 + bonnet / 2, 0, 0.735), body, metal=0.6, ry=2.5, top=mul(body, 1.12))
        box(g, (bonnet - 0.1, 0.02, 0.008), P(1.0 + bonnet / 2, 0, 0.787), mul(body, 1.22), metal=0.6, ry=2.5)
        if bed is not None:
            # A pickup: a wall behind the seats with a window in it, and the load bed past that.
            box(g, (0.04, 2 * hw, sill - floor + 0.10), P(back + 0.02, 0, floor), TRIM_L, top=TRIM)
            box(g, (0.04, 2 * (hw - 0.10), 0.05), P(back + 0.10, 0, roof - 0.05), TRIM)
            for s in (-1, 1):
                box(g, (2.1, 0.05, 0.40), P(back - 1.07, s * (hw + 0.09), 0.58), bed, metal=0.5)
            box(g, (0.05, 2 * hw + 0.2, 0.40), P(back - 2.12, 0, 0.58), bed, metal=0.5)
            box(g, (2.1, 2 * hw + 0.14, 0.02), P(back - 1.07, 0, 0.57), lin(60, 58, 52), metal=0.6)

    return make


def steering_wheel(g):
    """The wheel about its hub: a rim of twenty-four bars 0.4 m across, three spokes, a horn pad, a mark at the top."""
    r, n = 0.20, 24
    pts = [(0.0, r * math.sin(2 * math.pi * k / n), r * math.cos(2 * math.pi * k / n)) for k in range(n + 1)]
    for k in range(n):
        beam(g, pts[k], pts[k + 1], 0.026, 0.026, TRIM_L if k in (0, n - 1) else PLASTIC)
    cyl(g, 0.045, 0.05, (0.0, 0, 0), PLASTIC, seg=10, axis="x", caps=TRIM)
    for y, z in ((r - 0.01, -0.02), (-(r - 0.01), -0.02), (0.0001, -(r - 0.01))):
        beam(g, (0.01, 0.0, 0.0), (0.0, y, z), 0.035, 0.014, TRIM)
    box(g, (0.012, 0.07, 0.04), (-0.03, 0, -0.02), TRIM_L)


RED_CAR = lin(110, 40, 36)
BLUE_CAR = lin(62, 86, 112)
OLIVE = lin(70, 82, 56)

MODELS = [
    ("cabin", cabin(RED_CAR)),
    ("cabin_v1", cabin(BLUE_CAR)),
    ("cabin_truck", cabin(OLIVE, dx=0.55, dz=0.42, hw=0.84, back=-0.62, bonnet=0.98, bonnet_w=0.93, bench=False, bed=mul(OLIVE, 0.9))),
    ("steeringwheel", steering_wheel),
]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {argv[i].lstrip("-"): argv[i + 1] for i in range(0, len(argv) - 1, 2)}
    build(MODELS, "CABIN", opts.get("glb", "godot/assets/cars/cabins.glb"), opts.get("preview"))


main()
