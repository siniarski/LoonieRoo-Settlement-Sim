# The low-poly kit the model scripts share: boxes, cylinders and cones gathered as faces with a colour and a
# metalness each, written out as one mesh per model with vertex colours (metalness in the alpha), and exported as
# one glTF. The game replaces the material with a shader that reads the vertex colour (godot/World3D/ModelSet.cs).
#
# A colour says what it is made of: `mat("wood", lin(...))` is a colour whose faces wear the kit's planks detail
# (docs/textures.md) at the fineness MATERIALS gives, and `mul` keeps it. A plain colour with some metalness wears
# painted metal; a plain colour without wears nothing, and `plain(c)` wears nothing whatever its metalness (glass,
# chrome, lamps, screens, a label). write_surfaces puts the layer in the mesh's UV map, the face's own coordinates
# in metres in a second.
#
# Used by build_world.py and build_cabins.py. build_items.py and build_furniture.py have their own copies of the
# shape helpers (they came first): they take mul, the materials and write_surfaces from here.
#
# Blender space: Z up, a model rests on z = 0, centred on the origin, real sizes in metres (1 tile = 1 m).
# No randomness: a rebuild gives the same models.

import json
import math
import os

import bpy
from mathutils import Matrix, Vector


def lin(r, g, b):
    """An sRGB colour (0-255) as linear (the colour attribute is linear)."""
    return tuple((c / 255) ** 2.2 for c in (r, g, b))


def mul(c, k):
    """A colour k times as bright (the same material)."""
    return like(c, tuple(max(0.0, min(1.0, v * k)) for v in c))


# --- what a colour is made of: the kit's detail layers (docs/textures.md) ---

def _layers():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "godot", "assets", "surfaces",
                        "layers.json")
    with open(path, encoding="utf-8") as f:
        return [layer["name"] for layer in json.load(f)["layers"]]


LAYERS = _layers()

# A material: the layer it wears and how fine, in repeats per metre (a layer's picture is 1 / its repeat in
# layers.json across; the walls wear them at 0.5, so planks at 1.2 are boards ~7 cm wide where siding's are 17 cm).
# Fine enough that grain reads at arm's length, coarse enough not to shimmer across the street (mipmaps flatten it).
MATERIALS = {
    "wood": ("planks", 1.2),          # furniture: chairs, frames, legs, slats, handles (boards ~7 cm)
    "boards": ("floorboards", 0.9),   # wide tops and panels: table tops, wardrobes, a bed's head (~9 cm)
    "rough": ("planks", 0.7),         # crates, pallets, sheds, coops, building sites (~12 cm)
    "bark": ("bark", 2.5),            # logs, firewood
    "metal": ("metal", 1.5),          # painted or bare metal: appliances, tools, frames, tins
    "body": ("metal", 0.8),           # a vehicle's paint
    "sheet": ("sheetmetal", 1.5),     # corrugated sheet: a coop's roof, a shed's wall
    "rubber": ("rubber", 3.0),        # tyres, soles, hoses, grips
    "plastic": ("plaster", 3.0),      # moulded plastic: a dashboard, a bin, a casing (a faint mottle)
    "cloth": ("cloth", 1.2),          # upholstery, bedding, sacks, tarpaulins, clothes (the weave's wear shows)
    "fine": ("cloth", 3.0),           # small cloth: bandages, gloves, a sleeve, rope
    "leather": ("cloth", 1.5),        # leather (no layer of its own: a weave's worn mottle)
    "painted": ("metal", 1.2),        # painted wood: kitchen units, cupboards (chips and scratches, no boards)
    "earth": ("earth", 1.5),          # tilled soil, a pit's dirt
    "straw": ("fur", 2.0),            # hay, straw, a sack's fibre (no layer of its own: strands)
    "concrete": ("concrete", 1.0),    # concrete and stone
    "brick": ("brick", 1.0),
    "paper": ("grime", 2.0),          # card and paper: boxes, books (a faint smear)
}
# Grain follows each face's longest side for these: boards run across the picture (along u), strands and ribs
# down it (along v). The rest lie level: up the picture is up a face that stands.
GRAIN_U = {"planks", "floorboards"}
GRAIN_V = {"bark", "fur", "sheetmetal"}
# Boards on furniture are long: along the grain the picture is drawn this much longer, so a board's butt joints
# (a third of the picture apart, made for floors and siding) fall ~70 cm apart instead of every ~28 cm across a
# plank or a chair's rail. The grain itself is streaks along the board, which stretch without looking stretched.
ALONG = 0.4


class Paint(tuple):
    """A linear RGB colour that says its layer ("" for none) and repeats per metre."""
    layer = None
    repeats = 0.0


def surface(c, layer, repeats=0.0):
    """Colour c wearing a layer (a name in layers.json, or "" for none) at `repeats` per metre."""
    assert layer == "" or layer in LAYERS, f"no layer {layer} in layers.json"
    p = Paint(tuple(c[:3]))
    p.layer, p.repeats = layer, repeats
    return p


def mat(name, c):
    """Colour c made of a material (MATERIALS)."""
    return surface(c, *MATERIALS[name])


def plain(c):
    """Colour c with no detail, metal or not: glass, chrome, a lamp, a screen, a label."""
    return surface(c, "")


def like(c, rgb):
    """rgb, made of what c is made of."""
    return surface(rgb, c.layer, c.repeats) if getattr(c, "layer", None) is not None else tuple(rgb)


def layer_of(c, metal):
    """(layer, repeats) for a face of colour c and metalness `metal`, or None."""
    layer = getattr(c, "layer", None)
    if layer is None:
        return MATERIALS["metal"] if metal > 0.05 else None
    return (layer, c.repeats) if layer else None


def _face_axes(pts, layer):
    """A face's own axes (u, v), unit vectors in its plane: grain along its longest side for the layers that have
    it, else v down a face that stands and u along X on one that lies."""
    n = Vector((0, 0, 0))
    for k in range(2, len(pts)):
        n += (pts[k - 1] - pts[0]).cross(pts[k] - pts[0])
    if n.length < 1e-12:
        return Vector((1, 0, 0)), Vector((0, 1, 0))
    n.normalize()
    if layer in GRAIN_U or layer in GRAIN_V:
        best, longest = -1.0, None
        for a in (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, -1))):
            t = a - n * a.dot(n)
            if t.length < 0.3:
                continue
            t.normalize()
            span = max(p.dot(t) for p in pts) - min(p.dot(t) for p in pts)
            if span > best + 1e-6:
                best, longest = span, t
        if layer in GRAIN_U:
            return longest * ALONG, n.cross(longest)
        return longest.cross(n), longest
    if abs(n.z) < 0.7:
        v = Vector((0, 0, -1)) + n * n.z
        v.normalize()
        return v.cross(n), v
    u = Vector((1, 0, 0)) - n * n.x
    u.normalize()
    return u, n.cross(u)


def write_surfaces(me, faces):
    """Each face's layer in the mesh's first UV map as (-(layer + 1), 1 - repeats): the game reads (-(layer + 1),
    repeats), the layer laid by UV2 (glTF flips V; docs/textures.md). The face's own coordinates in metres in the
    second, (u, 1 - v), so the game's UV2 is (u, v). A face with no layer gets (0, 1): the game reads 0, none.
    `faces` as Geo.f, (vertex indices, colour, metal), in the mesh's polygon order."""
    what = me.uv_layers.new(name="UVMap")
    where = me.uv_layers.new(name="UVMetres")
    for poly, (_, c, metal) in zip(me.polygons, faces):
        lr = layer_of(c, metal)
        pts = [me.vertices[i].co.copy() for i in poly.vertices]
        axes = _face_axes(pts, lr[0]) if lr else None
        for j, li in enumerate(poly.loop_indices):
            if axes:
                what.data[li].uv = (-(LAYERS.index(lr[0]) + 1.0), 1.0 - lr[1])
                where.data[li].uv = (pts[j].dot(axes[0]), 1.0 - pts[j].dot(axes[1]))
            else:
                what.data[li].uv = (0.0, 1.0)
                where.data[li].uv = (0.0, 1.0)


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


def box(g, size, loc=(0, 0, 0), col=(0.5, 0.5, 0.5), metal=0.0, rz=0.0, ry=0.0, rx=0.0, top=None, taper=1.0):
    """A box of size (x, y, z) whose bottom centre is at loc; `top` colours its top face, `taper` shrinks the
    top's x and y (0: a point, a spike or a leaf)."""
    sx, sy, sz = (s / 2 for s in size)
    tx, ty = sx * taper, sy * taper
    v = [(-sx, -sy, 0), (sx, -sy, 0), (sx, sy, 0), (-sx, sy, 0),
         (-tx, -ty, 2 * sz), (tx, -ty, 2 * sz), (tx, ty, 2 * sz), (-tx, ty, 2 * sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    cols = [mul(col, 0.8), top or col, col, mul(col, 0.92), mul(col, 0.96), mul(col, 0.92)]
    g.add(v, f, cols, metal, place(loc, rz, ry, rx))


def cyl(g, r, length, loc=(0, 0, 0), col=(0.5, 0.5, 0.5), metal=0.0, seg=8, axis="z", rz=0.0, ry=0.0, rx=0.0,
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


def beam(g, p0, p1, w, d=None, col=(0.5, 0.5, 0.5), metal=0.0):
    """A straight bar of cross-section w x d from p0 to p1 (a pillar, a spoke, a lever): a box along the line.
    Not for a line straight down (-Z): the turn that takes Z onto it is not defined."""
    d = w if d is None else d
    a, b = Vector(p0), Vector(p1)
    length = (b - a).length
    m = Matrix.Translation(a) @ Vector((0, 0, 1)).rotation_difference((b - a).normalized()).to_matrix().to_4x4()
    sx, sy = w / 2, d / 2
    v = [(-sx, -sy, 0), (sx, -sy, 0), (sx, sy, 0), (-sx, sy, 0),
         (-sx, -sy, length), (sx, -sy, length), (sx, sy, length), (-sx, sy, length)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    g.add(v, f, [mul(col, 0.8), col, col, mul(col, 0.92), mul(col, 0.96), mul(col, 0.92)], metal, m)


# --- out to Blender, the glTF and a preview ---

def material(name):
    mat = bpy.data.materials.new(name)
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


def build(models, label, glb, preview_path=None):
    """Make every (name, function) model, print its triangles, export them all as one glTF, and render a lineup."""
    os.makedirs(os.path.dirname(os.path.abspath(glb)), exist_ok=True)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    mat = material(label.lower())
    objs = []
    total = 0
    for name, fn in models:
        g = Geo()
        fn(g)
        objs.append(to_object(name, g, mat))
        total += g.tris()
        print(f"{label} {name}: {g.tris()} triangles")
    print(f"{label} {len(models)} models, {total} triangles")
    bpy.ops.export_scene.gltf(filepath=os.path.abspath(glb), export_format="GLB", use_selection=False,
                              export_vertex_color="ACTIVE", export_normals=True, export_materials="EXPORT",
                              export_apply=True)
    print(f"{label} glb -> {glb}")
    if preview_path:
        preview(os.path.abspath(preview_path), objs)


def preview(path, objs, cols=8):
    """The models in rows, seen from above and in front (Workbench, vertex colours)."""
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x, scene.render.resolution_y = 1920, 1080
    row_x = [0.0] * (len(objs) // cols + 1)
    for k, ob in enumerate(objs):
        r = k // cols
        size = max(ob.dimensions.x, 1.0)
        ob.location = (row_x[r] + size / 2, r * 3.0, 0)
        row_x[r] += size + 0.6
    width = max(row_x)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    cam.location = (width / 2, -width * 0.75 - 2, width * 0.7 + 2)
    cam.rotation_euler = (math.radians(50), 0, 0)
    cam.data.lens = 24
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
