# The look-development body (docs/characters.md "The look"): one continuous adult form, sculpted by
# script, so the look lab can compare triangle budgets and texture sizes on a real silhouette rather
# than the segmented mannequin. Not final art: a believable blockout to decide budgets on, and a base
# a person can sculpt over later.
#
# How: the body is metaballs (Blender's implicit surfaces: overlapping fields blend smoothly, which is
# what makes shoulders, hips and the neck read as one skin), converted to a mesh and voxel-remeshed to
# even topology. The face is pushed into that mesh afterwards with "brushes" (pure functions of the rest
# position: sockets, the mouth line, lips, nostrils), so the same brushes make the expression shape keys.
#
# Blender space: metres, Z up, the character faces -Y, its left is +X. The rig is rig.py's (1.75 m).

import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

# A lone metaball's visible radius is 0.575 x its radius at stiffness 2, threshold 0.6 (measured).
MB_SCALE = 0.575

# Face landmarks (Blender space), used by the brushes, the paint and the shape keys. The rig puts the
# Head bone at 1.56 m and the crown at 1.75: eyes at ~1.645, the mouth at ~1.565, the chin at 1.52.
HEAD_C = Vector((0, 0.008, 1.640))      # the cranium's centre
EYE_L = Vector((0.031, -0.062, 1.646))  # eyeball centres (left = +X)
EYE_R = Vector((-0.031, -0.062, 1.646))
EYE_RADIUS = 0.0122
MOUTH = Vector((0, -0.088, 1.566))      # the middle of the lips' meeting line
MOUTH_HALF = 0.023                      # half the mouth's width
NOSE_TIP = Vector((0, -0.110, 1.598))
JAW_HINGE = Vector((0, -0.002, 1.605))  # the jaw turns about the X axis through here
CHIN_Z = 1.522
HEAD_TOP = 1.750


class Balls:
    """Metaball elements, gathered then built into one object."""

    def __init__(self):
        self.items = []

    def ball(self, c, r, neg=False, stiff=2.0):
        self.items.append(("BALL", Vector(c), r / MB_SCALE, None, Quaternion(), neg, stiff))

    def ellipsoid(self, c, half, rot=None, neg=False, stiff=2.0):
        m = max(half)
        self.items.append(("ELLIPSOID", Vector(c), m / MB_SCALE, tuple(h / m for h in half), rot or Quaternion(), neg, stiff))

    def capsule(self, a, b, r, neg=False, stiff=2.0):
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = Vector((1, 0, 0)).rotation_difference(d.normalized())
        self.items.append(("CAPSULE", (a + b) / 2, r / MB_SCALE, (d.length / 2 / (r / MB_SCALE),), rot, neg, stiff))

    def taper(self, a, b, r1, r2, steps=4):
        """A tapering limb: overlapping capsules, each a step thinner."""
        a, b = Vector(a), Vector(b)
        for i in range(steps):
            t0, t1 = i / steps, (i + 1) / steps
            r = r1 + (r2 - r1) * (t0 + t1) / 2
            self.capsule(a.lerp(b, t0), a.lerp(b, t1), r)

    def build(self, name, resolution):
        mb = bpy.data.metaballs.new(name)
        mb.resolution = mb.render_resolution = resolution
        mb.threshold = 0.6
        for kind, c, R, size, rot, neg, stiff in self.items:
            e = mb.elements.new(type=kind)
            e.co, e.radius, e.rotation, e.use_negative, e.stiffness = c, R, rot, neg, stiff
            if kind == "ELLIPSOID":
                e.size_x, e.size_y, e.size_z = size
            elif kind == "CAPSULE":
                e.size_x = size[0] * R
        obj = bpy.data.objects.new(name, mb)
        bpy.context.scene.collection.objects.link(obj)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
        bpy.data.objects.remove(obj)
        bpy.data.metaballs.remove(mb)
        out = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(out)
        return out


def body_balls():
    """The adult form in the T-pose of rig.py: arms out along X at shoulder height, legs straight.
    One capsule per limb segment (chains of overlapping capsules add their fields and bead), the
    muscles as ellipsoids over them."""
    b = Balls()
    # Torso: pelvis, belly, ribcage, upper chest and the shoulder girdle.
    b.ellipsoid((0, 0.010, 0.975), (0.138, 0.098, 0.100))
    b.ellipsoid((0, 0.000, 1.110), (0.128, 0.098, 0.100))
    b.ellipsoid((0, 0.000, 1.255), (0.160, 0.115, 0.120))
    b.ellipsoid((0, 0.008, 1.360), (0.165, 0.090, 0.070))
    b.capsule((-0.165, 0.014, 1.428), (0.165, 0.014, 1.428), 0.045)       # trapezius and collarbones
    for sx in (1, -1):
        b.ellipsoid((sx * 0.072, -0.068, 1.300), (0.062, 0.022, 0.050))  # pectoral
        b.ellipsoid((sx * 0.068, 0.068, 0.925), (0.068, 0.045, 0.070))  # buttock
        b.ellipsoid((sx * 0.105, 0.040, 1.250), (0.050, 0.050, 0.110))  # lats
        b.ellipsoid((sx * 0.205, 0.0, 1.418), (0.060, 0.060, 0.058))     # deltoid
    b.capsule((0, 0.012, 1.40), (0, 0.020, 1.555), 0.050)                 # neck
    for sx in (1, -1):
        b.capsule((sx * 0.20, 0, 1.430), (sx * 0.47, 0, 1.430), 0.045)
        b.ellipsoid((sx * 0.31, -0.008, 1.428), (0.080, 0.044, 0.046))  # bicep and tricep
        b.capsule((sx * 0.49, 0, 1.430), (sx * 0.74, 0, 1.430), 0.031)
        b.ellipsoid((sx * 0.56, 0, 1.432), (0.080, 0.045, 0.039))       # forearm muscle
        b.ellipsoid((sx * 0.805, 0, 1.428), (0.050, 0.044, 0.016))      # palm
        b.ellipsoid((sx * 0.885, 0.002, 1.426), (0.048, 0.042, 0.011))  # fingers together
        b.capsule((sx * 0.775, -0.032, 1.424), (sx * 0.820, -0.062, 1.420), 0.011)  # thumb
        b.capsule((sx * 0.090, 0.005, 0.930), (sx * 0.090, 0, 0.540), 0.062)
        b.ellipsoid((sx * 0.094, -0.008, 0.780), (0.080, 0.078, 0.145))  # quadriceps and hamstrings
        b.capsule((sx * 0.090, 0, 0.520), (sx * 0.090, 0.004, 0.090), 0.036)
        b.ellipsoid((sx * 0.090, 0.022, 0.390), (0.044, 0.042, 0.095))  # calf
        b.ellipsoid((sx * 0.090, -0.050, 0.040), (0.042, 0.118, 0.032))  # foot
    head_balls(b)
    return b


def head_balls(b):
    """The head's masses only (cranium, face, jaw, ears): the features are brushes (face_offset)."""
    c = HEAD_C
    b.ellipsoid(c, (0.074, 0.096, 0.100))                                          # cranium
    b.ellipsoid((0, c.y - 0.040, c.z - 0.045), (0.060, 0.060, 0.060))             # face
    b.ellipsoid((0, c.y - 0.050, CHIN_Z + 0.030), (0.045, 0.042, 0.030))          # jaw and chin
    for sx in (1, -1):
        b.ellipsoid((sx * 0.046, c.y - 0.020, CHIN_Z + 0.045), (0.018, 0.040, 0.030))  # jaw angle
        b.ellipsoid((sx * 0.075, c.y + 0.004, EYE_L.z - 0.010), (0.006, 0.015, 0.027), stiff=3)  # ear


def remesh(obj, voxel):
    """Even topology from the metaball shell (the shell's triangles are long and uneven)."""
    m = obj.modifiers.new("Remesh", "REMESH")
    m.mode, m.voxel_size, m.use_smooth_shade = "VOXEL", voxel, True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)


def smooth(obj, factor=0.5, repeat=8):
    """Soften the creases where the masses meet."""
    m = obj.modifiers.new("Smooth", "SMOOTH")
    m.factor, m.iterations = factor, repeat
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)


def decimate(obj, tris):
    """Collapse to about this many triangles (UVs and vertex groups carry over)."""
    have = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if have > tris:
        m = obj.modifiers.new("Decimate", "DECIMATE")
        m.decimate_type, m.ratio, m.use_collapse_triangulate = "COLLAPSE", tris / have, True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=m.name)
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ---------------------------------------------------------------- brushes

def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def falloff(d, r):
    """1 at the centre, 0 at r and beyond, smooth."""
    return smooth01(1 - d / r) if d < r else 0.0


def _gauss(d2, w):
    return math.exp(-d2 / (w * w))


def face_offset(co):
    """The face pushed into the head's masses (a vector to add to a rest position): brow ridge, nose,
    cheekbones, eye sockets, lips and the line between them, nostrils, the hollows under the cheeks."""
    off = Vector()
    if co.z < 1.49 or co.y > 0.0:
        return off
    x, z = co.x, co.z
    fwd = Vector((0, -1, 0))
    front = smooth01((-co.y - 0.03) / 0.03)  # only the front of the head
    # Brow ridge: a soft bar over the eyes, strongest over their inner halves.
    off += fwd * 0.008 * _gauss((z - (EYE_L.z + 0.022)) ** 2, 0.008) * _gauss(x * x, 0.045) * front
    # Nose: bridge growing to the tip, then tucked in to the base; wider at the tip and wings.
    if NOSE_TIP.z - 0.020 < z < EYE_L.z + 0.012:
        t = (EYE_L.z + 0.012 - z) / (EYE_L.z + 0.012 - NOSE_TIP.z)  # 0 between the eyes, 1 at the tip
        height = 0.006 + 0.018 * min(t, 1) ** 1.3 if t <= 1 else 0.024 * smooth01((NOSE_TIP.z - 0.020 - z) / -0.020)
        width = 0.006 + 0.008 * min(t, 1.2)
        off += fwd * height * _gauss(x * x, width) * front
    for sx in (1, -1):
        # Nose wings either side of the tip.
        off += fwd * 0.006 * _gauss((x - sx * 0.013) ** 2 + (z - (NOSE_TIP.z - 0.006)) ** 2, 0.007) * front
        # Cheekbones, and under them a hollow (deeper in the dead).
        off += fwd * 0.006 * _gauss((x - sx * 0.046) ** 2 + (z - (EYE_L.z - 0.030)) ** 2, 0.016) * front
        off += -fwd * 0.004 * _gauss((x - sx * 0.040) ** 2 + (z - (MOUTH.z + 0.010)) ** 2, 0.012) * front
        # Eye sockets: back towards the eyeball, more above (the brow overhangs).
        eye = EYE_L if sx > 0 else EYE_R
        d = co - eye
        off += -fwd * 0.015 * _gauss(d.x * d.x + (d.z * 1.2) ** 2, 0.016) * front
        # Nostrils.
        off += -fwd * 0.003 * _gauss((x - sx * 0.007) ** 2 + (z - (NOSE_TIP.z - 0.014)) ** 2, 0.0035) * front
    # Lips: the upper and lower lip forward, the meeting line pushed in, a groove under the lower lip.
    across = _gauss(x * x, MOUTH_HALF * 0.85)
    off += fwd * 0.006 * _gauss((z - (MOUTH.z + 0.006)) ** 2, 0.006) * across * front
    off += fwd * 0.006 * _gauss((z - (MOUTH.z - 0.007)) ** 2, 0.006) * across * front
    off += -fwd * 0.008 * _gauss((z - MOUTH.z) ** 2, 0.0022) * _gauss(x * x, MOUTH_HALF) * front
    off += -fwd * 0.003 * _gauss(x * x + (z - (MOUTH.z - 0.020)) ** 2, 0.009) * front
    # Chin forward a touch.
    off += fwd * 0.005 * _gauss(x * x + (z - (CHIN_Z + 0.012)) ** 2, 0.016) * front
    return off


def apply_offsets(obj, fn):
    for v in obj.data.vertices:
        v.co += fn(v.co)


# ---------------------------------------------------------------- expressions (shape keys)

def jaw_turn(co, angle_deg, weight):
    """A point turned about the jaw hinge (opening the mouth), by weight."""
    if weight <= 0:
        return Vector()
    a = math.radians(angle_deg) * weight
    rel = co - JAW_HINGE
    rot = Matrix.Rotation(-a, 3, "X")  # chin down and back
    return rot @ rel - rel


def jaw_weight(co):
    """How much of the jaw's turn a point takes: the chin and lower lip all of it, the cheeks and
    under the jaw part of it, nothing above the mouth line or behind the ears."""
    if co.y > 0.03 or co.z > MOUTH.z + 0.002 or co.z < 1.44:
        return 0.0
    below = smooth01((MOUTH.z + 0.002 - co.z) / 0.006)
    front = smooth01((0.02 - co.y) / 0.05)
    neck = smooth01((co.z - 1.47) / 0.05)  # the throat stretches, the neck below stays
    return below * front * neck


def angry_offset(co):
    """The snarl: brows down and in, nose wrinkled, the upper lip pulled up off the teeth, the mouth's
    corners back, cheeks up into a squint. Added to jaw_open for an attacking face."""
    off = Vector()
    if co.z < 1.50 or co.y > 0.0:
        return off
    ez, mz = EYE_L.z, MOUTH.z
    for sx in (1, -1):
        inner = Vector((sx * 0.014, -0.086, ez + 0.024))
        off += Vector((-sx * 0.004, -0.002, -0.008)) * falloff((co - inner).length, 0.020)
        outer = Vector((sx * 0.046, -0.072, ez + 0.026))
        off += Vector((0, 0, 0.002)) * falloff((co - outer).length, 0.015)
        cheek = Vector((sx * 0.040, -0.080, ez - 0.026))
        off += Vector((0, -0.002, 0.004)) * falloff((co - cheek).length, 0.020)
        nose_side = Vector((sx * 0.012, -0.098, ez - 0.022))
        off += Vector((sx * 0.001, 0, 0.004)) * falloff((co - nose_side).length, 0.012)
        corner = Vector((sx * MOUTH_HALF, -0.082, mz))
        off += Vector((sx * 0.004, 0.004, -0.002)) * falloff((co - corner).length, 0.012)
        lip = Vector((sx * 0.011, -0.094, mz + 0.008))  # the upper lip either side of the middle: the canines show
        off += Vector((0, -0.001, 0.007)) * falloff((co - lip).length, 0.012)
    lower = Vector((0, -0.092, mz - 0.008))
    off += Vector((0, -0.002, -0.004)) * falloff((co - lower).length, 0.014)
    return off


def add_face_keys(obj, jaw_deg=24):
    """Blend shapes "angry" and "jaw_open" (Godot blend shapes of the same names). Neutral, the dead's
    slack face, is the basis with a little jaw_open (the lab mixes it)."""
    me = obj.data
    if me.shape_keys is None:
        obj.shape_key_add(name="Basis")
    rest = [v.co.copy() for v in me.vertices]
    is_lower_teeth = set(obj.get("lower_teeth", []))
    jaw = obj.shape_key_add(name="jaw_open", from_mix=False)
    for i, c in enumerate(rest):
        w = 1.0 if i in is_lower_teeth else jaw_weight(c)
        jaw.data[i].co = c + jaw_turn(c, jaw_deg, w)
    ang = obj.shape_key_add(name="angry", from_mix=False)
    for i, c in enumerate(rest):
        ang.data[i].co = c + (Vector() if i in is_lower_teeth else angry_offset(c))


# ---------------------------------------------------------------- eyes and teeth (added after decimating)

def add_eyes_and_teeth(obj, segs=12, rings=8, teeth=True):
    """Eyeballs (in the sockets) and two rows of teeth (behind the lips), joined into the body mesh so
    they share its atlas. Remembers the lower teeth's vertex indices (for the jaw key) as a property."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    n0 = len(bm.verts)
    for eye in (EYE_L, EYE_R):
        ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=EYE_RADIUS)
        bmesh.ops.translate(bm, vec=eye, verts=ret["verts"])
    lower = []
    if teeth:
        for row, z, sign in (("upper", MOUTH.z + 0.0045, 1), ("lower", MOUTH.z - 0.0045, -1)):
            # An arch of small boxes following the lips' curve.
            for k in range(-3, 4):
                ang = k * 0.32
                x = math.sin(ang) * 0.020
                y = MOUTH.y + 0.010 + (1 - math.cos(ang)) * 0.020
                ret = bmesh.ops.create_cube(bm, size=1.0)
                size = (0.0058, 0.0040, 0.0075 if abs(k) < 2 else 0.0068)
                bmesh.ops.scale(bm, vec=Vector(size), verts=ret["verts"])
                bmesh.ops.rotate(bm, cent=Vector(), matrix=Matrix.Rotation(-ang, 3, "Z"), verts=ret["verts"])
                bmesh.ops.translate(bm, vec=Vector((x, y, z)), verts=ret["verts"])
                if row == "lower":
                    lower += ret["verts"]
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.verts.index_update()
    lower_idx = [v.index for v in lower if v.is_valid]
    bm.to_mesh(obj.data)
    bm.free()
    obj["lower_teeth"] = lower_idx
    return n0
