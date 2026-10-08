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
