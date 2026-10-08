"""Contact-sheet renders of the generated models (Cycles on the CPU, works headless)."""
import math
import os
import bpy
from mathutils import Euler, Vector

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
    sc.view_settings.look = "None"


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
        if not os.path.exists(path) and suffix.endswith(".png"):
            path = path[:-4] + ".jpg"
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


def textured_weapon_sheet(model_dir, ids, path, cols=5, width=2400):
    """Three-quarter renders of the exported, textured weapons. The grid is laid out in the camera's view plane and
    every weapon is scaled to fill its cell, so the sheet frames itself for any number of columns."""
    reset_scene()
    rot = Euler((math.radians(77), 0, math.radians(-97)))  # from the right side, slightly above and in front
    M = rot.to_matrix()
    R, U, F = M.col[0], M.col[1], -M.col[2]
    cell_w, cell_h = 1.6, 0.95
    rows = (len(ids) + cols - 1) // cols
    for i, wid in enumerate(ids):
        r, c = divmod(i, cols)
        roots, new = import_fbx(os.path.join(model_dir, wid + ".fbx"))
        meshes = [o for o in new if o.type == "MESH"]
        for o in meshes:
            _textured(o, os.path.join(model_dir, "Textures", wid))
        bpy.context.view_layer.update()
        def extent():
            pts = [o.matrix_world @ Vector(v) for o in meshes for v in o.bound_box]
            er = max(p.dot(R) for p in pts) - min(p.dot(R) for p in pts)
            eu = max(p.dot(U) for p in pts) - min(p.dot(U) for p in pts)
            return pts, er, eu
        pts, er, eu = extent()
        k = min(cell_w * 0.84 / max(er, 1e-4), cell_h * 0.62 / max(eu, 1e-4))
        for root in roots:
            root.scale = tuple(x * k for x in root.scale)
        bpy.context.view_layer.update()
        pts, er, eu = extent()
        center = sum(pts, Vector()) / len(pts)
        target = R * (c * cell_w) - U * (r * cell_h - cell_h * 0.06)
        for root in roots:
            root.location = root.location + (target - center)
        _label(wid, target - U * (cell_h * 0.44) - R * (cell_w * 0.42), 0.075, rot)
    cam = bpy.data.cameras.new("Cam")
    cam.type = "ORTHO"
    W, H = cols * cell_w, rows * cell_h
    cam.ortho_scale = max(W, H)
    co = bpy.data.objects.new("Cam", cam)
    co.rotation_euler = rot
    co.location = R * ((cols - 1) * cell_w / 2) - U * ((rows - 1) * cell_h / 2) - F * 10
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _light((math.radians(45), math.radians(15), math.radians(-60)), 4.0)
    _light((math.radians(-50), 0, math.radians(130)), 1.5)
    _setup_render(path, width, int(width * H / W), 48)
    bpy.ops.render.render(write_still=True)
    print("preview written to", path)


def textured_character_sheet(model_dir, factions, path):
    """Exported, textured characters: full body front and back (3/4) and a head close-up for each faction."""
    reset_scene()
    groups = {}
    for i, f in enumerate(factions):
        roots, new = import_fbx(os.path.join(model_dir, f + ".fbx"))
        mesh = next(o for o in new if o.type == "MESH")
        _textured(mesh, os.path.join(model_dir, "Textures", f))
        for o in new:
            if o.type == "ARMATURE":
                # the rest pose is the aiming pose
                o.animation_data_clear()
                o.data.pose_position = "REST"
        for root in roots:
            root.location = (0, -i * 1.6, 0)
        groups[f] = new
    _light((math.radians(48), math.radians(12), math.radians(-55)), 3.2)
    _light((math.radians(-45), 0, math.radians(135)), 1.4)
    _light((math.radians(80), 0, math.radians(30)), 0.8)
    # floor: grounds the characters with their contact shadows
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, 0))
    floor = bpy.context.active_object
    fm = bpy.data.materials.new("Floor")
    fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.16, 0.16, 0.17, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
    floor.data.materials.append(fm)
    cam = bpy.data.cameras.new("Cam")
    co = bpy.data.objects.new("Cam", cam)
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    _setup_render(path, 1000, 1250, 48)
    sc = bpy.context.scene
    world = sc.world.node_tree.nodes.get("Background")
    world.inputs["Color"].default_value = (0.11, 0.12, 0.14, 1)
    base, ext = os.path.splitext(path)
    shots = []
    for i, f in enumerate(factions):
        y = -i * 1.6
        for tag, loc, tgt, lens in (("front", (-1.9, y - 3.2, 1.25), (0, y - 0.05, 0.98), 42),
                                    ("back", (2.2, y + 3.0, 1.35), (0, y, 0.98), 42),
                                    ("head", (-0.45, y - 0.85, 1.75), (0, y - 0.03, 1.66), 70)):
            co.location = loc
            d = Vector(tgt) - Vector(loc)
            co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
            cam.lens = lens
            # keep the other factions out of frame
            for g, objs in groups.items():
                for o in objs:
                    o.hide_render = g != f
            sc.render.filepath = f"{base}_{f}_{tag}.png"
            bpy.ops.render.render(write_still=True)
            shots.append(sc.render.filepath)
    try:
        from PIL import Image
        ims = [Image.open(p_).convert("RGB") for p_ in shots]
        w, h = ims[0].size
        cols = 3
        rows = (len(ims) + cols - 1) // cols
        sheet = Image.new("RGB", (w * cols, h * rows))
        for k, im in enumerate(ims):
            sheet.paste(im, ((k % cols) * w, (k // cols) * h))
        sheet = sheet.resize((w * cols // 2, h * rows // 2))
        sheet.save(path, quality=90)
    except ImportError:
        pass
    print("preview written to", path)
