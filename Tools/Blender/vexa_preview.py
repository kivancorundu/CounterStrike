"""Contact-sheet renders of the generated models (Cycles on the CPU, works headless)."""
import math
import os
import bpy
from mathutils import Vector

from vexa_common import reset_scene, set_detail


def _setup_render(path, width, height, samples=24):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    sc.render.resolution_x = width
    sc.render.resolution_y = height
    sc.render.film_transparent = False
    sc.render.filepath = path
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.055, 0.065, 0.085, 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.view_settings.view_transform = "Standard"


def _light(rot, energy):
    l = bpy.data.lights.new("Sun", "SUN")
    l.energy = energy
    o = bpy.data.objects.new("Sun", l)
    o.rotation_euler = rot
    bpy.context.collection.objects.link(o)


def _label(text, loc, size, rot):
    cu = bpy.data.curves.new(text, "FONT")
    cu.body = text.upper()
    cu.size = size
    o = bpy.data.objects.new("lbl_" + text, cu)
    o.location = loc
    o.rotation_euler = rot
    bpy.context.collection.objects.link(o)
    m = bpy.data.materials.new("Label")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.84, 1.0, 0.24, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (0.84, 1.0, 0.24, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 1.5
    cu.materials.append(m)


def weapon_sheet(ids, path, cols=6):
    import vexa_weapons
    reset_scene()
    set_detail("pc")
    cell_w, cell_h = 1.45, 0.62
    rows = (len(ids) + cols - 1) // cols
    side_rot = (math.radians(90), 0, math.radians(-90))  # text facing a camera that looks along +X
    for i, wid in enumerate(ids):
        r, c = divmod(i, cols)
        o = vexa_weapons.build(wid)
        bb = [o.matrix_world @ Vector(v) for v in o.bound_box]
        cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
        cz = (min(v.z for v in bb) + max(v.z for v in bb)) / 2
        size = max(max(v.y for v in bb) - min(v.y for v in bb), 0.3)
        k = min(1.0, 1.2 / size) if size > 1.2 else 1.0
        o.scale = (k, k, k)
        # columns run toward -Y (screen right), rows downward
        o.location = (0, -c * cell_w - (-cy * k), -r * cell_h - cz * k)
        _label(wid, (-0.2, -c * cell_w + 0.62, -r * cell_h - 0.25), 0.07, side_rot)
    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    cam.ortho_scale = max(cols * cell_w, rows * cell_h * 1.5) + 0.3
    co = bpy.data.objects.new("Cam", cam)
    co.location = (-10, -(cols - 1) * cell_w / 2, -(rows - 1) * cell_h / 2)
    co.rotation_euler = (math.radians(90), 0, math.radians(-90))
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _light((math.radians(50), math.radians(10), math.radians(-60)), 3.5)
    _light((math.radians(-40), math.radians(0), math.radians(120)), 1.2)
    w = 2400
    _setup_render(path, w, int(w * rows * cell_h * 1.5 / (cols * cell_w)) + 60)
    bpy.ops.render.render(write_still=True)
    print("preview written to", path)


def character_sheet(builders, path):
    """builders: list of (name, fn) that create a character at the origin and return its root."""
    reset_scene()
    set_detail("pc")
    for i, (name, fn) in enumerate(builders):
        root = fn()
        root.location = (0, -i * 1.4, 0)
        # three-quarter view: rotate the character, not the camera
        root.rotation_euler = (0, 0, math.radians(-35))
        _label(name, (-0.5, -i * 1.4 + 0.45, -0.25), 0.12, (math.radians(90), 0, math.radians(-90)))
    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    cam.ortho_scale = max(2.6, len(builders) * 1.45)
    co = bpy.data.objects.new("Cam", cam)
    co.location = (-10, -(len(builders) - 1) * 0.7, 0.95)
    co.rotation_euler = (math.radians(90), 0, math.radians(-90))
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _light((math.radians(50), math.radians(10), math.radians(-60)), 3.5)
    _light((math.radians(-40), 0, math.radians(120)), 1.2)
    _setup_render(path, 1400, 1100, 32)
    bpy.ops.render.render(write_still=True)
    print("preview written to", path)


def _textured(obj, tex_base, normal=True):
    """Material from baked textures (<tex_base>_albedo/_mask/_normal.png) for preview renders."""
    m = bpy.data.materials.new(os.path.basename(tex_base))
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    def img(suffix, non_color):
        path = tex_base + suffix
        if not os.path.exists(path):
            return None
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(path)
        if non_color:
            n.image.colorspace_settings.name = "Non-Color"
        return n
    a = img("_albedo.png", False)
    if a:
        nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    k = img("_mask.png", True)
    if k:
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(k.outputs["Color"], sep.inputs[0])
        nt.links.new(sep.outputs[0], bsdf.inputs["Metallic"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(k.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nrm = img("_normal.png", True) if normal else None
    if nrm:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs[0], bsdf.inputs["Normal"])
    for o in [obj] + list(obj.children_recursive):
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(m)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    return roots, new


def textured_weapon_sheet(model_dir, ids, path, cols=5, persp=True):
    """Three-quarter renders of the exported, textured weapons."""
    reset_scene()
    cell_w, cell_h = 1.5, 0.7
    rows = (len(ids) + cols - 1) // cols
    for i, wid in enumerate(ids):
        r, c = divmod(i, cols)
        roots, new = import_fbx(os.path.join(model_dir, wid + ".fbx"))
        mesh = next(o for o in new if o.type == "MESH")
        _textured(mesh, os.path.join(model_dir, "Textures", wid))
        root = roots[0]
        bb = [mesh.matrix_world @ Vector(v) for v in mesh.bound_box]
        size = max(max(v.y for v in bb) - min(v.y for v in bb), max(v.x for v in bb) - min(v.x for v in bb), 0.25)
        k = 1.15 / size
        root.scale = tuple(s * k for s in root.scale)
        bpy.context.view_layer.update()
        bb = [mesh.matrix_world @ Vector(v) for v in mesh.bound_box]
        cx = sum(v.x for v in bb) / 8; cy = sum(v.y for v in bb) / 8; cz = sum(v.z for v in bb) / 8
        root.location = (root.location.x - cx, root.location.y - cy - c * cell_w, root.location.z - cz - r * cell_h)
        _label(wid, (-0.3, -c * cell_w + 0.55, -r * cell_h - 0.3), 0.06, (math.radians(90), 0, math.radians(-90)))
    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    cam.ortho_scale = max(cols * cell_w, rows * cell_h * 1.6) + 0.2
    co = bpy.data.objects.new("Cam", cam)
    # slightly from above and in front: shows the side and top of each weapon
    co.location = (-10, -(cols - 1) * cell_w / 2 - 1.2, -(rows - 1) * cell_h / 2 + 2.4)
    co.rotation_euler = (math.radians(77), 0, math.radians(-97))
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _light((math.radians(45), math.radians(15), math.radians(-60)), 4.0)
    _light((math.radians(-50), 0, math.radians(130)), 1.5)
    w = 2400
    _setup_render(path, w, int(w * rows * cell_h * 1.6 / (cols * cell_w)) + 80, 48)
    bpy.ops.render.render(write_still=True)
    print("preview written to", path)


def textured_character_sheet(model_dir, factions, path):
    reset_scene()
    for i, f in enumerate(factions):
        roots, new = import_fbx(os.path.join(model_dir, f + ".fbx"))
        mesh = next(o for o in new if o.type == "MESH")
        _textured(mesh, os.path.join(model_dir, "Textures", f))
        root = roots[0]
        root.location = (0, -i * 1.3, 0)
        root.rotation_euler = (root.rotation_euler.x, root.rotation_euler.y, math.radians(-30))
    cam = bpy.data.cameras.new("Cam")
    cam.lens = 60
    co = bpy.data.objects.new("Cam", cam)
    co.location = (-6.4, -(len(factions) - 1) * 0.65 - 0.8, 1.35)
    co.rotation_euler = (math.radians(86), 0, math.radians(-97))
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _light((math.radians(45), math.radians(15), math.radians(-60)), 4.0)
    _light((math.radians(-50), 0, math.radians(130)), 1.5)
    _setup_render(path, 1600, 1200, 64)
    bpy.ops.render.render(write_still=True)
    print("preview written to", path)
