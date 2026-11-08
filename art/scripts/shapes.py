# Mesh primitives for scripted assets (the mannequin, its garments): capped tapered cylinders,
# boxes, spheres, flat-colour materials. Blender space: Z up, the character faces -Y.

import bmesh
import bpy
from mathutils import Matrix, Vector


def cone(bm, a, b, r1, r2, segments=12, flat=1.0, flat_axis="y"):
    """A capped tapered cylinder from a to b (Blender space). flat < 1 squashes it along flat_axis (front-back by default)."""
    d = b - a
    ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r1, radius2=r2, depth=d.length)
    verts = ret["verts"]
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    if flat != 1.0:
        c = (a + b) / 2
        for v in verts:
            setattr(v.co, flat_axis, getattr(c, flat_axis) + (getattr(v.co, flat_axis) - getattr(c, flat_axis)) * flat)
    return verts


def box(bm, c, size):
    ret = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=ret["verts"])
    bmesh.ops.translate(bm, vec=c, verts=ret["verts"])
    return ret["verts"]


def sphere(bm, c, r, segs=10, rings=6, squash=None):
    ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r)
    if squash:
        bmesh.ops.scale(bm, vec=Vector(squash), verts=ret["verts"])
    bmesh.ops.translate(bm, vec=c, verts=ret["verts"])
    return ret["verts"]


def material(name, rgb):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1)
        bsdf.inputs["Roughness"].default_value = 0.8
    return mat
