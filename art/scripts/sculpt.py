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
# Head bone at 1.56 m and the crown at 1.75. Proportions from average adult male faces (composites and
# anthropometric means, 2026-11-08): pupils 63 mm apart, eye openings ~28 x 10 mm, the nose 50 mm from
# its root to its base and 35 mm across the wings, the mouth 52 mm wide and 22 mm under the nose,
# 48 mm from the mouth to the chin; a fairly upright forehead, cheekbones that barely show from the front.
HEAD_C = Vector((0, 0.012, 1.655))      # the cranium's centre
EYE_L = Vector((0.0315, -0.074, 1.646))  # eyeball centres (left = +X); the cornea's front about flush with the lids
EYE_R = Vector((-0.0315, -0.074, 1.646))
EYE_RADIUS = 0.0120
MOUTH = Vector((0, -0.081, 1.574))      # the middle of the lips' meeting line, at the slit's floor (the lips stand ~1 cm in front)
MOUTH_HALF = 0.026                      # half the mouth's width
NOSE_TIP = Vector((0, -0.114, 1.606))
SUBNASALE_Z = 1.596                     # where the nose meets the upper lip
JAW_HINGE = Vector((0, -0.002, 1.612))  # the jaw turns about the X axis through here
CHIN_Z = 1.526


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
    b.ellipsoid(c, (0.074, 0.095, 0.095))                                          # cranium
    b.ellipsoid((0, -0.036, 1.690), (0.058, 0.050, 0.042))                        # forehead (upright)
    b.ellipsoid((0, -0.036, 1.616), (0.060, 0.052, 0.050))                        # face
    b.ellipsoid((0, -0.044, CHIN_Z + 0.034), (0.048, 0.046, 0.028))               # jaw
    b.ellipsoid((0, -0.068, CHIN_Z + 0.020), (0.026, 0.016, 0.020))               # chin
    for sx in (1, -1):
        b.ellipsoid((sx * 0.050, -0.040, 1.630), (0.018, 0.028, 0.017))           # cheekbone mass
        b.ellipsoid((sx * 0.048, 0.000, CHIN_Z + 0.050), (0.015, 0.036, 0.030))   # jaw angle
        b.ellipsoid((sx * 0.074, 0.008, 1.632), (0.0055, 0.013, 0.029), stiff=3)  # ear


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


def smooth_head(obj, repeat=24):
    """Soften the head alone (where its masses meet they leave creases down the cheeks), before the
    face's features go in."""
    g = obj.vertex_groups.new(name="_head")
    for v in obj.data.vertices:
        w = smooth01((v.co.z - 1.50) / 0.04)
        if w > 0:
            g.add([v.index], w, "REPLACE")
    m = obj.modifiers.new("SmoothHead", "SMOOTH")
    m.factor, m.iterations, m.vertex_group = 0.5, repeat, "_head"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=m.name)
    if obj.vertex_groups.get("_head"):
        obj.vertex_groups.remove(obj.vertex_groups["_head"])


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
    ez = EYE_L.z
    fwd = Vector((0, -1, 0))
    g = _gauss
    front = smooth01((-co.y - 0.03) / 0.03)  # only the front of the head
    # Brow ridge: low and soft over the eyes; the root of the nose (nasion) a touch back between them.
    off += fwd * 0.004 * g((z - (ez + 0.019)) ** 2, 0.008) * g(x * x, 0.040) * front
    off += -fwd * 0.003 * g(x * x + (z - (ez + 0.006)) ** 2, 0.009) * front
    # Nose: from the nasion (about eye level) straight down and out to the tip, then back in under it to
    # where it meets the lip. Narrow along the bridge, widest at the tip.
    root = ez + 0.010
    if SUBNASALE_Z - 0.004 < z < root:
        t = (root - z) / (root - NOSE_TIP.z)  # 0 at the root, 1 at the tip
        if t <= 1:
            height, width = 0.003 + 0.016 * t ** 1.15, 0.0048 + 0.0040 * t
        else:  # under the tip: the columella, back to the lip
            u = smooth01((z - (SUBNASALE_Z - 0.004)) / (NOSE_TIP.z - SUBNASALE_Z + 0.004))
            height, width = 0.019 * u, 0.0088
        off += fwd * height * g(x * x, width) * front
    for sx in (1, -1):
        # The wings either side of the tip (35 mm across), with the nostrils under them.
        wing = Vector((sx * 0.0135, 0, NOSE_TIP.z - 0.005))
        off += fwd * 0.0065 * g((x - wing.x) ** 2 + (z - wing.z) ** 2, 0.0068) * front
        off += -fwd * 0.0025 * g((x - sx * 0.0075) ** 2 + (z - (SUBNASALE_Z + 0.002)) ** 2, 0.0035) * front
        # The orbit: a soft hollow under the brow, above the upper lid, and a faint one under the eye.
        eye = EYE_L if sx > 0 else EYE_R
        off += -fwd * 0.0022 * g((x - eye.x) ** 2 * 0.6 + (z - (ez + 0.011)) ** 2, 0.010) * front
        off += -fwd * 0.0010 * g((x - eye.x * 1.05) ** 2 * 0.5 + (z - (ez - 0.012)) ** 2, 0.008) * front
        # Cheekbones: barely there from the front; a soft fold from the nose wing to the mouth corner.
        off += fwd * 0.002 * g((x - sx * 0.050) ** 2 + (z - (ez - 0.020)) ** 2, 0.020) * front
        t = min(1.0, max(0.0, (wing.z - z) / (wing.z - MOUTH.z)))
        fold_x = sx * (0.019 + 0.012 * t)
        if MOUTH.z - 0.008 < z < wing.z:
            off += -fwd * 0.0012 * g((x - fold_x) ** 2, 0.0025) * front
    # Lips: the upper and lower lip forward of the mouth line (the lower a little fuller), the line
    # itself pushed in, the corners tucked in, a groove under the lower lip, the chin forward.
    across = g(x * x, MOUTH_HALF * 0.8)
    off += fwd * 0.0045 * g((z - (MOUTH.z + 0.0055)) ** 2, 0.0050) * across * front
    off += fwd * 0.0055 * g((z - (MOUTH.z - 0.0065)) ** 2, 0.0055) * across * front
    off += -fwd * 0.007 * g((z - MOUTH.z) ** 2, 0.0020) * g(x * x, MOUTH_HALF) * front
    for sx in (1, -1):
        off += -fwd * 0.003 * g((x - sx * MOUTH_HALF) ** 2 + (z - MOUTH.z) ** 2, 0.004) * front
    off += fwd * 0.0015 * g(x * x + (z - (MOUTH.z + 0.012)) ** 2, 0.004) * front  # the philtrum
    off += -fwd * 0.003 * g(x * x + (z - (MOUTH.z - 0.019)) ** 2, 0.009) * front
    off += fwd * 0.002 * g(x * x + (z - (CHIN_Z + 0.016)) ** 2, 0.020) * front
    return off + lid_offset(co)


def eye_opening(co, sx, eye):
    """Where a point sits against an eye's opening: q < 1 inside the almond (28 x 9 mm, the outer
    corner a little higher), 1 on its edge, growing outwards."""
    lat = (co.x - eye.x) * sx  # + towards the outer corner
    half_w = 0.0140
    dz = co.z - eye.z - 0.0012 * lat / half_w
    half_h = 0.0050 if dz > 0 else 0.0042  # the upper lid covers the top of the iris
    return math.hypot(lat / half_w, dz / half_h)


def lid_offset(co):
    """Eyelids: the skin stays on the face's surface (the eyeball sits just behind it, its front about
    flush with the lids' margins); inside the almond of the opening it's pushed back so the eye shows;
    round the opening the lids' margins curve in a little onto the eye."""
    for sx, eye in ((1, EYE_L), (-1, EYE_R)):
        if abs(co.x - eye.x) > 0.03 or abs(co.z - eye.z) > 0.025 or co.y > eye.y + 0.004:
            continue
        q = eye_opening(co, sx, eye)
        if q < 1:
            return Vector((0, 0.008 * smooth01((1 - q) / 0.25 + 0.2), 0))
        return Vector((0, 0.0028 * math.exp(-((q - 1) / 0.7) ** 2), 0))
    return Vector()


# Zombie face variants (the look lab's face candidates): extra brushes over face_offset. Each keeps the
# jaw_open and angry keys (they're offsets of whatever rest shape the variant leaves).
#   slack    newly dead, mostly intact (face_offset alone)
#   gaunt    wasted: hollow cheeks and temples, deep sockets, the lips shrunk back off the teeth
#   bloated  swollen: puffy cheeks and lids, a heavy jaw and chin, thick lips
#   torn     a wound has taken the left side of the lower lip and cheek: the teeth show through
#   feral    a heavy brow, a long wide jaw, the nose flattened
FACE_VARIANTS = ("slack", "gaunt", "bloated", "torn", "feral")
TORN_C = Vector((0.026, -0.080, 1.560))  # the wound's centre (the subject's left, under the mouth corner)


def variant_offset(co, variant):
    off = Vector()
    if variant == "slack" or co.z < 1.46 or co.y > 0.03:
        return off
    x, z = co.x, co.z
    fwd = Vector((0, -1, 0))
    front = smooth01((-co.y - 0.02) / 0.03)
    mz, ez = MOUTH.z, EYE_L.z
    g = _gauss
    if variant == "gaunt":
        for sx in (1, -1):
            off += (Vector((-sx * 0.004, 0.009, 0))) * g((x - sx * 0.042) ** 2 + (z - (mz + 0.014)) ** 2, 0.017) * front
            off += Vector((-sx * 0.005, 0, 0)) * g((x - sx * 0.066) ** 2 + (z - (ez + 0.025)) ** 2, 0.020) * smooth01((-co.y + 0.02) / 0.04)
            eye = EYE_L if sx > 0 else EYE_R
            d = co - eye
            off += -fwd * 0.006 * g(d.x * d.x + d.z * d.z, 0.017) * front
            off += Vector((-sx * 0.002, 0, 0)) * g((x - sx * 0.013) ** 2 + (z - (NOSE_TIP.z - 0.006)) ** 2, 0.008) * front
        band = g(x * x, MOUTH_HALF * 0.9)
        off += Vector((0, 0.007, 0.003)) * g((z - (mz + 0.007)) ** 2, 0.006) * band * front   # upper lip back and up
        off += Vector((0, 0.007, -0.004)) * g((z - (mz - 0.008)) ** 2, 0.006) * band * front  # lower lip back and down
    elif variant == "bloated":
        for sx in (1, -1):
            off += Vector((sx * 0.009, -0.007, -0.002)) * g((x - sx * 0.045) ** 2 + (z - (mz + 0.004)) ** 2, 0.028) * front
            eye = EYE_L if sx > 0 else EYE_R
            ring = abs((Vector((co.x - eye.x, 0, co.z - eye.z))).length - 0.016)
            off += fwd * 0.005 * g(ring * ring, 0.006) * front                                  # swollen lids
            off += Vector((sx * 0.004, 0, 0)) * g((x - sx * 0.013) ** 2 + (z - (NOSE_TIP.z - 0.006)) ** 2, 0.008) * front
        off += Vector((0, -0.007, -0.008)) * g(x * x / 1.5 + (z - (CHIN_Z - 0.008)) ** 2 + (co.y + 0.055) ** 2, 0.026)  # double chin
        off += fwd * 0.003 * g((z - mz) ** 2, 0.010) * g(x * x, MOUTH_HALF) * front
    elif variant == "torn":
        d = Vector(((x - TORN_C.x) / 1.3, 0, (z - TORN_C.z) / 0.9)).length
        off += Vector((0, 0.013, 0)) * g(d * d, 0.013) * front
        lip = g((z - (mz - 0.007)) ** 2, 0.006) * smooth01((x + 0.004) / 0.008) * g((x - 0.016) ** 2, 0.016)
        off += Vector((0, 0.011, -0.003)) * lip * front
    elif variant == "feral":
        off += Vector((0, -0.006, -0.003)) * g((z - (ez + 0.020)) ** 2, 0.009) * g(x * x, 0.045) * front
        for sx in (1, -1):
            off += Vector((sx * 0.006, 0, -0.004)) * g((x - sx * 0.045) ** 2 + (z - (CHIN_Z + 0.04)) ** 2, 0.022)
            off += Vector((sx * 0.003, 0.002, 0)) * g((x - sx * 0.011) ** 2 + (z - (NOSE_TIP.z - 0.004)) ** 2, 0.010) * front
        off += Vector((0, -0.002, -0.008)) * g(x * x + (z - (CHIN_Z + 0.01)) ** 2, 0.020) * front
        off += Vector((0, 0.005, 0.002)) * g(x * x + (z - NOSE_TIP.z) ** 2, 0.010) * front  # the tip flattened
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
    # Between the corners the lips are cut apart (make_skin.py): the lower lip goes with the jaw whole, the
    # upper stays. Beyond the corners the skin is whole, so the turn fades in across the mouth line.
    split = smooth01((abs(co.x) - MOUTH_HALF * 0.85) / (MOUTH_HALF * 0.3))
    sharp = 1.0 if co.z < MOUTH.z else 0.0
    soft = smooth01((MOUTH.z + 0.002 - co.z) / 0.006)
    below = sharp + (soft - sharp) * split
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
        inner = Vector((sx * 0.014, -0.094, ez + 0.020))  # on the face as it is (make_skin: the stored skin)
        off += Vector((-sx * 0.004, -0.002, -0.008)) * falloff((co - inner).length, 0.020)
        outer = Vector((sx * 0.044, -0.082, ez + 0.022))
        off += Vector((0, 0, 0.002)) * falloff((co - outer).length, 0.015)
        cheek = Vector((sx * 0.038, -0.088, ez - 0.024))
        off += Vector((0, -0.002, 0.004)) * falloff((co - cheek).length, 0.020)
        nose_side = Vector((sx * 0.012, -0.104, ez - 0.022))
        off += Vector((sx * 0.001, 0, 0.004)) * falloff((co - nose_side).length, 0.012)
        corner = Vector((sx * MOUTH_HALF, -0.088, mz))
        off += Vector((sx * 0.004, 0.004, -0.002)) * falloff((co - corner).length, 0.012)
        lip = Vector((sx * 0.011, -0.099, mz + 0.007))  # the upper lip either side of the middle: the canines show
        off += Vector((0, -0.001, 0.007)) * falloff((co - lip).length, 0.012)
    lower = Vector((0, -0.099, mz - 0.007))
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


def cut_mouth(obj):
    """Open the lips: delete the thin band of faces at the slit's floor, so the jaw key parts the lips
    instead of stretching skin (the mouth's inside is a cavity added with the teeth)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    gone = [f for f in bm.faces
            if (c := f.calc_center_median()) is not None and abs(c.z - MOUTH.z) < 0.0026
            and abs(c.x) < MOUTH_HALF * 0.92 and MOUTH.y - 0.008 < c.y < MOUTH.y + 0.02]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    return len(gone)


MOUTH_BAG_C = Vector((0, MOUTH.y + 0.030, MOUTH.z - 0.006))
MOUTH_BAG_HALF = (0.027, 0.022, 0.021)


# ---------------------------------------------------------------- eyes and teeth (added after decimating)

def teeth_boxes():
    """The teeth: (row, centre, full size, turn about Z) for an arch of small boxes behind the lips. The
    paint picks teeth out by these exact boxes (a looser region painted the skin round the mouth white)."""
    out = []
    for row, z in (("upper", MOUTH.z + 0.0045), ("lower", MOUTH.z - 0.0045)):
        for k in range(-3, 4):
            ang = k * 0.32
            c = Vector((math.sin(ang) * 0.020, MOUTH.y + 0.002 + (1 - math.cos(ang)) * 0.020, z))
            out.append((row, c, (0.0058, 0.0040, 0.0075 if abs(k) < 2 else 0.0068), ang))
    return out


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
        # The mouth's inside, seen through the parted lips: a dark cavity behind the teeth.
        ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
        bmesh.ops.scale(bm, vec=Vector(MOUTH_BAG_HALF), verts=ret["verts"])
        bmesh.ops.translate(bm, vec=MOUTH_BAG_C, verts=ret["verts"])
        for row, c, size, ang in teeth_boxes():
            ret = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=Vector(size), verts=ret["verts"])
            bmesh.ops.rotate(bm, cent=Vector(), matrix=Matrix.Rotation(-ang, 3, "Z"), verts=ret["verts"])
            bmesh.ops.translate(bm, vec=c, verts=ret["verts"])
            if row == "lower":
                lower += ret["verts"]
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.verts.index_update()
    lower_idx = [v.index for v in lower if v.is_valid]
    bm.to_mesh(obj.data)
    bm.free()
    obj["lower_teeth"] = lower_idx
    return n0
