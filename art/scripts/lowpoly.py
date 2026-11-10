# The low-poly kit the model scripts share: boxes, cylinders and cones gathered as faces with a colour and a
# metalness each, written out as one mesh per model with vertex colours (metalness in the alpha), and exported as
# one glTF. The game replaces the material with a shader that reads the vertex colour (godot/World3D/ModelSet.cs).
#
# Used by build_world.py. build_items.py and build_furniture.py have their own copies of the same helpers (they
# came first, and a rebuild of their models must not change): fold them in here when either is next rebuilt.
#
# Blender space: Z up, a model rests on z = 0, centred on the origin, real sizes in metres (1 tile = 1 m).
# No randomness: a rebuild gives the same models.

import math
import os

import bpy
from mathutils import Matrix, Vector


def lin(r, g, b):
    """An sRGB colour (0-255) as linear (the colour attribute is linear)."""
    return tuple((c / 255) ** 2.2 for c in (r, g, b))


def mul(c, k):
    return tuple(max(0.0, min(1.0, v * k)) for v in c)


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
