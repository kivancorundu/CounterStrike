"""
Shared helpers for the VEXA model generators (run with Blender's Python module: `pip install bpy`).

Modeling convention (Blender space), chosen so the default FBX export lands correctly in Unity:
    forward = -Y   (becomes Unity +Z)
    up      = +Z   (becomes Unity +Y)
    right   = -X   (becomes Unity +X)
Units are meters. Weapons have their origin at the firing hand (grip); characters at the feet.
"""
import math
import os
import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

# Level of detail: "pc" (smooth, more bevels) or "mobile" (low poly, but still beveled so it isn't blocky)
DETAIL = {"segments": 3, "bevel": 0.002, "cyl": 36, "small_parts": True, "fine": True}


def set_detail(level):
    # mobile: mid poly (CS 1.6-like density with rounded edges); pc: CS2-style density with fine details
    if level == "mobile":
        DETAIL.update(segments=2, bevel=0.0025, cyl=18, small_parts=True, fine=False)
    else:
        DETAIL.update(segments=3, bevel=0.002, cyl=36, small_parts=True, fine=True)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for m in list(bpy.data.materials):
        bpy.data.materials.remove(m)


# ---------------------------------------------------------------- materials

PALETTE = {
    # name: (rgb, metallic, roughness)
    "Metal": ((0.10, 0.10, 0.11), 0.85, 0.42),
    "Steel": ((0.32, 0.33, 0.35), 0.9, 0.32),
    "Polymer": ((0.045, 0.047, 0.05), 0.0, 0.72),
    "Tan": ((0.46, 0.37, 0.25), 0.0, 0.7),
    "Olive": ((0.20, 0.23, 0.15), 0.0, 0.7),
    "Wood": ((0.36, 0.17, 0.07), 0.0, 0.55),
    "WoodDark": ((0.22, 0.11, 0.05), 0.0, 0.55),
    "Accent": ((0.84, 1.0, 0.24), 0.0, 0.5),       # VEXA lime, used sparingly
    "Lens": ((0.05, 0.12, 0.2), 0.3, 0.05),
    "Grip": ((0.07, 0.07, 0.075), 0.0, 0.9),
    "Glass": ((0.35, 0.55, 0.32), 0.0, 0.1),
    "Cloth": ((0.62, 0.55, 0.42), 0.0, 0.95),
    "Red": ((0.55, 0.06, 0.05), 0.0, 0.5),
    "Screen": ((0.12, 0.35, 0.14), 0.0, 0.3),
}


def mat(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    rgb, metal, rough = PALETTE[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1)
    return m


def custom_mat(name, rgb, metal=0.0, rough=0.6):
    PALETTE[name] = (rgb, metal, rough)
    return mat(name)


# ---------------------------------------------------------------- primitives

def _finish(obj, material, bevel=True, bevel_width=None):
    obj.data.materials.clear()
    obj.data.materials.append(mat(material) if isinstance(material, str) else material)
    if bevel:
        w = bevel_width if bevel_width is not None else DETAIL["bevel"]
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = w
        mod.segments = DETAIL["segments"]
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(35)
        mod.harden_normals = False
    return obj


def box(name, center, size, material, bevel=True, bevel_width=None, rot=(0, 0, 0)):
    """Axis-aligned (optionally rotated) box. center/size in Blender space (x, y, z)."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, material, bevel, bevel_width)


def cyl(name, a, b, r, material, r2=None, verts=None, bevel=True, cap=True):
    """Cylinder (or cone if r2 is given) from point a to point b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    length = d.length
    verts = verts or DETAIL["cyl"]
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=length, end_fill_type="NGON" if cap else "NOTHING")
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=length, end_fill_type="NGON" if cap else "NOTHING")
    o = bpy.context.active_object
    o.name = name
    o.location = (a + b) / 2
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return _finish(o, material, bevel, min(DETAIL["bevel"], r * 0.3))


def sphere(name, center, radius, material, scale=(1, 1, 1), segments=None):
    seg = segments or max(8, DETAIL["cyl"])
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=max(6, seg // 2), radius=radius, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.shade_smooth()
    return _finish(o, material, bevel=False)


def torus(name, center, major, minor, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=max(12, DETAIL["cyl"]),
                                     minor_segments=max(6, DETAIL["cyl"] // 2), location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    return _finish(o, material, bevel=False)


def prism(name, profile, y0, y1, material, bevel=True):
    """Extrude a 2D profile [(x, z), ...] (counter-clockwise seen from +Y) along Y from y0 to y1."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    front = [bm.verts.new((x, y0, z)) for x, z in profile]
    back = [bm.verts.new((x, y1, z)) for x, z in profile]
    n = len(profile)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    return _finish(o, material, bevel)


def empty(name, pos, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = 0.03
    e.location = pos
    bpy.context.collection.objects.link(e)
    if parent is not None:
        e.parent = parent
    return e


# ---------------------------------------------------------------- assembly & export

def apply_modifiers(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=m.name)
        except RuntimeError:
            o.modifiers.remove(m)


def join(objs, name):
    """Applies modifiers, joins the parts into one mesh and smooths it by angle (keeps hard edges crisp)."""
    objs = [o for o in objs if o is not None]
    for o in objs:
        apply_modifiers(o)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    except Exception:
        bpy.ops.object.shade_smooth()
    # weighted normals make bevelled hard-surface parts read well even at low poly counts
    wn = o.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    apply_modifiers(o)
    return o


def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def export_fbx(path, objects):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
        for c in o.children_recursive:
            c.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        object_types={"MESH", "EMPTY", "ARMATURE"},
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
    )


def clear_objects():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.curves):
        for b in list(block):
            if b.users == 0:
                block.remove(b)


def cut(target, cutters):
    """Boolean difference: real holes, slots and ports (exact solver). The cutters are consumed."""
    cutters = [c for c in cutters if c is not None]
    if not cutters:
        return target
    col = bpy.data.collections.new("_cutters")
    bpy.context.scene.collection.children.link(col)
    for c in cutters:
        apply_modifiers(c)
        for uc in list(c.users_collection):
            uc.objects.unlink(c)
        col.objects.link(c)
    m = target.modifiers.new("Cut", "BOOLEAN")
    m.operation = "DIFFERENCE"
    m.solver = "EXACT"
    m.operand_type = "COLLECTION"
    m.collection = col
    apply_modifiers(target)
    for c in list(col.objects):
        bpy.data.objects.remove(c)
    bpy.data.collections.remove(col)
    return target
