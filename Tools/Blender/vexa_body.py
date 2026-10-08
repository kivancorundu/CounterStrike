"""
Character bodies v2: one continuous organic mesh (skin modifier + subdivision), weighted to the
skeleton automatically, with layered gear on top. Detail scales with the platform:

    mobile  ~ CS 1.6 silhouette density but smooth (no hard polygon corners)
    pc      ~ CS2-style density: smooth body, sculpted-looking clothing, detailed gear
"""
import math
import bpy
import bmesh
from mathutils import Vector

from vexa_common import custom_mat, mat

LEVEL = {"body": 2, "gear": 1, "cyl": 24, "detail": True}


def set_level(level):
    if level == "mobile":
        LEVEL.update(body=1, gear=0, cyl=10, detail=False)
    else:
        LEVEL.update(body=2, gear=1, cyl=24, detail=True)


# skeleton for the skin modifier: name -> (position, (radius a, radius b)). Blender space: forward -Y, right -X.
def skeleton():
    P = {
        "pelvis": ((0, 0.005, 0.97), (0.165, 0.115)),
        "waist": ((0, 0.0, 1.07), (0.15, 0.105)),
        "belly": ((0, -0.005, 1.17), (0.16, 0.112)),
        "chest": ((0, 0.0, 1.29), (0.185, 0.125)),
        "upperchest": ((0, 0.005, 1.41), (0.19, 0.115)),
        "neck": ((0, -0.005, 1.5), (0.066, 0.068)),
        "neck2": ((0, -0.015, 1.565), (0.064, 0.066)),
        "jaw": ((0, -0.045, 1.625), (0.08, 0.082)),
        "head": ((0, -0.035, 1.7), (0.1, 0.11)),
        "headtop": ((0, -0.03, 1.785), (0.08, 0.085)),
    }
    for s, side in ((-1, "r"), (1, "l")):
        if side == "r":
            arm = [("sh", (-0.2, 0.005, 1.43), (0.07, 0.07)), ("bicep", (-0.225, -0.06, 1.32), (0.064, 0.064)),
                   ("elb", (-0.24, -0.12, 1.2), (0.052, 0.052)), ("fore", (-0.16, -0.22, 1.195), (0.05, 0.046)),
                   ("wr", (-0.078, -0.305, 1.19), (0.033, 0.03)), ("palm", (-0.055, -0.355, 1.19), (0.044, 0.024)),
                   ("fing", (-0.035, -0.405, 1.178), (0.038, 0.019))]
        else:
            arm = [("sh", (0.2, 0.005, 1.43), (0.07, 0.07)), ("bicep", (0.205, -0.12, 1.33), (0.064, 0.064)),
                   ("elb", (0.19, -0.24, 1.23), (0.052, 0.052)), ("fore", (0.115, -0.375, 1.235), (0.05, 0.046)),
                   ("wr", (0.05, -0.48, 1.24), (0.033, 0.03)), ("palm", (0.033, -0.53, 1.24), (0.044, 0.024)),
                   ("fing", (0.018, -0.58, 1.232), (0.038, 0.019))]
        for n, p, r in arm:
            P[side + n] = (p, r)
        x = 0.1 * s
        leg = [("hip", (x, 0.0, 0.92), (0.095, 0.1)), ("thigh", (x, -0.008, 0.72), (0.086, 0.09)),
               ("knee", (x, -0.016, 0.52), (0.062, 0.066)), ("calf", (x, 0.008, 0.36), (0.063, 0.068)),
               ("ank", (x, 0.02, 0.15), (0.047, 0.05)), ("heel", (x, 0.045, 0.06), (0.05, 0.055)),
               ("ball", (x, -0.075, 0.05), (0.055, 0.046)), ("toe", (x, -0.14, 0.045), (0.046, 0.036))]
        for n, p, r in leg:
            P[side + n] = (p, r)
    for k in ("neck2", "jaw", "head", "headtop"):
        P.pop(k)
    E = [("pelvis", "waist"), ("waist", "belly"), ("belly", "chest"), ("chest", "upperchest"), ("upperchest", "neck")]
    for side in "rl":
        E += [("upperchest", side + "sh"), (side + "sh", side + "bicep"), (side + "bicep", side + "elb"), (side + "elb", side + "fore"),
              (side + "fore", side + "wr"), (side + "wr", side + "palm"), (side + "palm", side + "fing"),
              ("pelvis", side + "hip"), (side + "hip", side + "thigh"), (side + "thigh", side + "knee"), (side + "knee", side + "calf"),
              (side + "calf", side + "ank"), (side + "ank", side + "heel"), (side + "heel", side + "ball"), (side + "ball", side + "toe")]
    return P, E


def _apply_all(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def skin_body():
    P, E = skeleton()
    names = list(P)
    me = bpy.data.meshes.new("body")
    me.from_pydata([P[n][0] for n in names], [(names.index(a), names.index(b)) for a, b in E], [])
    o = bpy.data.objects.new("body", me)
    bpy.context.collection.objects.link(o)
    sk = o.modifiers.new("Skin", "SKIN")
    sk.use_smooth_shade = True
    sk.branch_smoothing = 0.7
    for i, n in enumerate(names):
        me.skin_vertices[0].data[i].radius = P[n][1]
    me.skin_vertices[0].data[names.index("pelvis")].use_root = True
    ss = o.modifiers.new("Sub", "SUBSURF")
    ss.levels = ss.render_levels = LEVEL["body"]
    _apply_all(o)
    return o, P


def _seg_dist(p, a, b):
    a, b = Vector(a), Vector(b)
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


def paint_regions(o, P, mats):
    """Assign clothing materials per face: head/face, collar, jacket, sleeves, gloves, pants, boots."""
    keys = ["skin", "mask", "jacket", "gloves", "pants", "boots"]
    o.data.materials.clear()
    for k in keys:
        o.data.materials.append(mats[k])
    idx = {k: i for i, k in enumerate(keys)}
    for poly in o.data.polygons:
        c = poly.center
        region = "jacket"
        if c.z < 0.205 and c.z < 0.5:
            region = "boots"
        elif c.z < 1.0:
            region = "pants"
        else:
            # arms by distance to the arm chains
            for side in "rl":
                d_hand = min(_seg_dist(c, P[side + "wr"][0], P[side + "palm"][0]), _seg_dist(c, P[side + "palm"][0], P[side + "fing"][0]))
                if d_hand < 0.05:
                    region = "gloves"
            if region == "jacket" and c.z > 1.585:
                region = mats.get("face_region", "mask")
            elif region == "jacket" and c.z > 1.49 and abs(c.x) < 0.09:
                region = "mask" if mats.get("neck_mask") else "jacket"
        poly.material_index = idx[region]


# ---------------------------------------------------------------- gear helpers

def soft_box(name, center, size, material, round_=0.25, rot=(0, 0, 0), sub=None):
    """Rounded box: bevel + subdivision gives a molded / sewn look without hard polygon corners."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bv = o.modifiers.new("Bevel", "BEVEL")
    bv.width = min(size) * round_
    bv.segments = 2 if (LEVEL["gear"] if sub is None else sub) > 0 else 3
    lv = LEVEL["gear"] if sub is None else min(sub, LEVEL["gear"])
    if lv > 0:
        ss = o.modifiers.new("Sub", "SUBSURF")
        ss.levels = ss.render_levels = lv
    o.data.materials.append(material)
    _apply_all(o)
    bpy.ops.object.shade_smooth()
    return o


def tube(name, a, b, r, material, r2=None, verts=None):
    a, b = Vector(a), Vector(b)
    d = b - a
    verts = verts or LEVEL["cyl"]
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=d.length)
    o = bpy.context.active_object
    o.name = name
    o.location = (a + b) / 2
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bv = o.modifiers.new("Bevel", "BEVEL")
    bv.width = min(r, r2 or r) * 0.25
    bv.segments = 2
    o.data.materials.append(material)
    _apply_all(o)
    bpy.ops.object.shade_smooth()
    return o


def ring(name, center, major, minor, material, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=max(16, LEVEL["cyl"]),
                                     minor_segments=max(6, LEVEL["cyl"] // 3), location=center, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return o


def blob(name, center, radius, material, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=max(12, LEVEL["cyl"]), ring_count=max(8, LEVEL["cyl"] // 2), radius=radius, location=center)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    bpy.ops.object.shade_smooth()
    return o


def shell(name, center, radius, material, scale, cut_z, thickness=0.008):
    """Helmet-like shell: sphere cut below cut_z (world), given thickness."""
    o = blob(name, center, radius, material, scale)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    geom = [v for v in bm.verts if v.co.z + center[2] < cut_z]
    bmesh.ops.delete(bm, geom=geom, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    so = o.modifiers.new("Solid", "SOLIDIFY")
    so.thickness = thickness
    so.offset = 1
    if LEVEL["gear"] > 0:
        ss = o.modifiers.new("Sub", "SUBSURF")
        ss.levels = ss.render_levels = 1
    _apply_all(o)
    bpy.ops.object.shade_smooth()
    return o


def sculpt_head(m, faction):
    """Head from fused volumes (skull, jaw, cheeks, nose, ears, neck): voxel remesh, smooth, decimate."""
    pieces = [
        blob("Skull", (0, -0.022, 1.715), 1.0, m["skin"], scale=(0.098, 0.116, 0.112)),
        blob("Face", (0, -0.058, 1.648), 1.0, m["skin"], scale=(0.074, 0.082, 0.07)),
        blob("Chin", (0, -0.09, 1.6), 1.0, m["skin"], scale=(0.04, 0.035, 0.03)),
        tube("NeckCore", (0, -0.005, 1.47), (0, -0.03, 1.64), 0.064, m["skin"], verts=16),
        blob("Nose", (0, -0.128, 1.665), 1.0, m["skin"], scale=(0.016, 0.022, 0.03)),
        blob("Brow", (0, -0.1, 1.715), 1.0, m["skin"], scale=(0.075, 0.03, 0.022)),
    ]
    for sx in (-1, 1):
        pieces.append(blob(f"Cheek{sx}", (0.045 * sx, -0.09, 1.65), 1.0, m["skin"], scale=(0.03, 0.03, 0.03)))
        pieces.append(blob(f"Ear{sx}", (0.097 * sx, -0.02, 1.69), 1.0, m["skin"], scale=(0.012, 0.024, 0.032)))
    bpy.ops.object.select_all(action="DESELECT")
    for p_ in pieces:
        p_.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    h = bpy.context.active_object
    h.name = "Head"
    rm = h.modifiers.new("Remesh", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = 0.004 if LEVEL["detail"] else 0.009
    sm = h.modifiers.new("Smooth", "CORRECTIVE_SMOOTH")
    sm.iterations = 6
    _apply_all(h)
    dec = h.modifiers.new("Decimate", "DECIMATE")
    target = 4000 if LEVEL["detail"] else 700
    tris = sum(len(p_.vertices) - 2 for p_ in h.data.polygons)
    dec.ratio = min(1.0, target / max(tris, 1))
    _apply_all(h)
    bpy.ops.object.shade_smooth()
    # materials: skin on the face for the defenders, balaclava over everything for the attackers
    h.data.materials.clear()
    h.data.materials.append(m["skin"])
    h.data.materials.append(m["mask"])
    for poly in h.data.polygons:
        c = poly.center
        if faction == "akinci":
            poly.material_index = 1
        else:
            poly.material_index = 1 if c.z < 1.585 else 0   # neck gaiter below the chin
    # neck follows the neck bone, the rest the head
    vg_h = h.vertex_groups.new(name="Head")
    vg_n = h.vertex_groups.new(name="Neck")
    for v in h.data.vertices:
        t = max(0.0, min(1.0, (v.co.z - 1.54) / 0.06))
        if t > 0:
            vg_h.add([v.index], t, "REPLACE")
        if t < 1:
            vg_n.add([v.index], 1 - t, "REPLACE")
    return h


def rigid(o, bone):
    """Gear follows exactly one bone."""
    vg = o.vertex_groups.new(name=bone)
    vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    return o


# ---------------------------------------------------------------- factions

def faction_materials(faction, colors):
    p = faction.capitalize()
    M = lambda key, rgb, metal=0.0, rough=0.85: custom_mat(f"{p}_{key}", rgb, metal, rough)
    return {
        "skin": M("skin", (0.62, 0.46, 0.36), 0, 0.6),
        "mask": M("mask", colors["mask"]),
        "jacket": M("jacket", colors["top"]),
        "gloves": M("gloves", colors["gloves"], 0, 0.7),
        "pants": M("pants", colors["pants"]),
        "boots": M("boots", colors["boots"], 0, 0.6),
        "gear": M("gear", colors["gear"]),
        "accent": M("accent", colors["accent"], 0, 0.5),
        "strap": M("strap", tuple(c * 0.6 for c in colors["gear"])),
        "metal": mat("Metal"),
        "lens": mat("Lens"),
        "polymer": mat("Polymer"),
        "sole": M("sole", (0.05, 0.05, 0.05), 0, 0.8),
    }


def gear_common(m, parts):
    """Belt, boots soles and laces, cuffs, pockets: shared by both factions."""
    parts.append(rigid(ring("Belt", (0, 0.003, 1.035), 0.158, 0.02, m["strap"], scale=(1.0, 0.74, 1.0)), "Hips"))
    parts.append(rigid(soft_box("Buckle", (0, -0.12, 1.03), (0.06, 0.012, 0.04), m["metal"], 0.2), "Hips"))
    for s, side in ((-1, "Right"), (1, "Left")):
        x = 0.1 * s
        parts.append(rigid(soft_box(f"{side}Sole", (x, -0.045, 0.016), (0.1, 0.24, 0.03), m["sole"], 0.35), f"{side}Foot"))
        if LEVEL["detail"]:
            for i in range(4):
                parts.append(rigid(soft_box(f"{side}Lace{i}", (x, -0.03 - i * 0.022, 0.12 - i * 0.01), (0.05, 0.008, 0.008), m["sole"], 0.3, sub=1), f"{side}Foot"))
            parts.append(rigid(ring(f"{side}Cuff", (x, 0.02, 0.2), 0.058, 0.012, m["pants"]), f"{side}LowerLeg"))
        parts.append(rigid(soft_box(f"{side}CargoPocket", (x + 0.078 * s, -0.01, 0.7), (0.035, 0.12, 0.14), m["pants"], 0.45), f"{side}UpperLeg"))
        if LEVEL["detail"]:
            parts.append(rigid(soft_box(f"{side}PocketFlap", (x + 0.093 * s, -0.01, 0.775), (0.025, 0.135, 0.03), m["pants"], 0.3), f"{side}UpperLeg"))


def gear_akinci(m, parts):
    # cap, goggles, scarf, chest rig, sling bag, jacket details
    cap = blob("Cap", (0, -0.025, 1.745), 0.108, m["gear"], scale=(0.97, 1.03, 0.62))
    parts.append(rigid(cap, "Head"))
    parts.append(rigid(soft_box("Brim", (0, -0.14, 1.745), (0.15, 0.1, 0.012), m["gear"], 0.3, rot=(math.radians(-10), 0, 0)), "Head"))
    parts.append(rigid(soft_box("Goggles", (0, -0.115, 1.69), (0.17, 0.035, 0.05), m["lens"], 0.35), "Head"))
    parts.append(rigid(soft_box("GoggleFrame", (0, -0.105, 1.69), (0.18, 0.03, 0.058), m["polymer"], 0.35), "Head"))
    parts.append(rigid(ring("GoggleStrap", (0, -0.03, 1.69), 0.1, 0.009, m["accent"], scale=(1.0, 1.0, 0.6)), "Head"))
    parts.append(rigid(ring("Scarf", (0, -0.008, 1.52), 0.07, 0.03, m["mask"], scale=(1.12, 1.0, 1.0)), "Neck"))
    if LEVEL["detail"]:
        parts.append(rigid(soft_box("ScarfTail", (0.03, 0.08, 1.45), (0.07, 0.03, 0.14), m["mask"], 0.4, rot=(0, math.radians(10), 0)), "Chest"))
    # chest rig
    parts.append(rigid(soft_box("ChestRig", (0, -0.13, 1.2), (0.32, 0.06, 0.14), m["gear"], 0.2), "Chest"))
    for i, x in enumerate((-0.1, 0.0, 0.1)):
        parts.append(rigid(soft_box(f"RigPouch{i}", (x, -0.165, 1.205), (0.085, 0.04, 0.12), m["gear"], 0.25), "Chest"))
        if LEVEL["detail"]:
            parts.append(rigid(soft_box(f"RigFlap{i}", (x, -0.188, 1.255), (0.088, 0.012, 0.035), m["strap"], 0.3), "Chest"))
    for s in (-1, 1):
        parts.append(rigid(soft_box(f"RigStrap{s}", (0.11 * s, -0.04, 1.36), (0.04, 0.2, 0.012), m["strap"], 0.3, rot=(math.radians(60), 0, 0)), "Chest"))
    parts.append(rigid(soft_box("SlingBag", (-0.17, 0.12, 1.07), (0.08, 0.11, 0.17), m["gear"], 0.3), "Hips"))
    # jacket: collar, zipper, shoulder stripes (amber) and elbow patches
    parts.append(rigid(ring("Collar", (0, 0.0, 1.49), 0.085, 0.022, m["jacket"], scale=(1.2, 1.05, 1.0)), "Chest"))
    if LEVEL["detail"]:
        parts.append(rigid(soft_box("Zipper", (0, -0.12, 1.33), (0.01, 0.01, 0.2), m["metal"], 0.3, sub=1), "Chest"))
    for s, side in ((-1, "Right"), (1, "Left")):
        parts.append(rigid(soft_box(f"{side}Stripe", (0.205 * s, -0.01, 1.4), (0.012, 0.11, 0.028), m["accent"], 0.3), "Chest"))


def gear_muhafiz(m, parts):
    # helmet with rails and NVG mount, visor, plate carrier, pouches, radio, knee pads, holster
    parts.append(rigid(shell("Helmet", (0, -0.03, 1.705), 0.128, m["gear"], (0.95, 1.05, 0.95), 1.655, 0.01), "Head"))
    parts.append(rigid(soft_box("Visor", (0, -0.13, 1.69), (0.17, 0.025, 0.065), m["lens"], 0.35), "Head"))
    parts.append(rigid(soft_box("VisorRim", (0, -0.125, 1.725), (0.18, 0.03, 0.012), m["polymer"], 0.3), "Head"))
    for s in (-1, 1):
        parts.append(rigid(soft_box(f"Rail{s}", (0.125 * s, -0.03, 1.695), (0.014, 0.12, 0.026), m["polymer"], 0.25), "Head"))
        if LEVEL["detail"]:
            parts.append(rigid(blob(f"EarCup{s}", (0.1 * s, -0.02, 1.665), 0.035, m["polymer"], scale=(0.6, 1, 1)), "Head"))
    parts.append(rigid(soft_box("NVGMount", (0, -0.135, 1.79), (0.05, 0.03, 0.045), m["polymer"], 0.25), "Head"))
    # plate carrier
    parts.append(rigid(soft_box("FrontPlate", (0, -0.122, 1.28), (0.29, 0.045, 0.3), m["gear"], 0.3), "Chest"))
    parts.append(rigid(soft_box("BackPlate", (0, 0.128, 1.3), (0.29, 0.045, 0.3), m["gear"], 0.3), "Chest"))
    for s in (-1, 1):
        parts.append(rigid(soft_box(f"Cummerbund{s}", (0.17 * s, 0.0, 1.21), (0.04, 0.25, 0.13), m["gear"], 0.25), "Chest"))
        parts.append(rigid(soft_box(f"ShoulderStrap{s}", (0.11 * s, 0.0, 1.46), (0.05, 0.27, 0.02), m["gear"], 0.3), "Chest"))
    for i, x in enumerate((-0.1, 0.0, 0.1)):
        parts.append(rigid(soft_box(f"MagPouch{i}", (x, -0.165, 1.19), (0.08, 0.04, 0.13), m["gear"], 0.22), "Chest"))
        if LEVEL["detail"]:
            parts.append(rigid(soft_box(f"MagFlap{i}", (x, -0.188, 1.245), (0.082, 0.012, 0.035), m["strap"], 0.3), "Chest"))
    parts.append(rigid(soft_box("Patch", (0.1, -0.152, 1.37), (0.07, 0.005, 0.045), m["accent"], 0.25, sub=1), "Chest"))
    parts.append(rigid(soft_box("Radio", (0.14, 0.17, 1.3), (0.06, 0.05, 0.17), m["polymer"], 0.2), "Chest"))
    if LEVEL["detail"]:
        parts.append(rigid(tube("Antenna", (0.15, 0.17, 1.38), (0.155, 0.18, 1.6), 0.004, m["polymer"], verts=8), "Chest"))
        parts.append(rigid(soft_box("Backpanel", (0, 0.17, 1.27), (0.24, 0.04, 0.24), m["strap"], 0.2), "Chest"))
    for s, side in ((-1, "Right"), (1, "Left")):
        x = 0.1 * s
        parts.append(rigid(soft_box(f"{side}KneePad", (x, -0.07, 0.52), (0.11, 0.05, 0.13), m["polymer"], 0.35), f"{side}LowerLeg"))
        parts.append(rigid(soft_box(f"{side}ShoulderPad", (0.215 * s, -0.01, 1.41), (0.07, 0.13, 0.1), m["gear"], 0.35), "Chest"))
    parts.append(rigid(soft_box("Holster", (-0.17, -0.0, 0.8), (0.05, 0.1, 0.18), m["polymer"], 0.25), "RightUpperLeg"))
    parts.append(rigid(ring("Collar", (0, 0.0, 1.49), 0.08, 0.02, m["jacket"], scale=(1.2, 1.05, 1.0)), "Chest"))


def gloves(m, parts, P):
    """Knuckle pads on the gloves (PC only)."""
    if not LEVEL["detail"]:
        return
    for side, bone in (("r", "RightHand"), ("l", "LeftHand")):
        c = (Vector(P[side + "palm"][0]) + Vector(P[side + "fing"][0])) / 2 + Vector((0, 0, 0.022))
        parts.append(rigid(soft_box(side + "Knuckle", tuple(c), (0.05, 0.03, 0.012), m["polymer"], 0.35, sub=1), bone))


def face(m, parts, faction):
    if faction == "muhafiz":
        # nose and ears on the visible lower face
        parts.append(rigid(blob("Nose", (0, -0.13, 1.66), 0.018, m["skin"], scale=(0.8, 1.0, 1.3)), "Head"))
    if LEVEL["detail"]:
        for s in (-1, 1):
            parts.append(rigid(blob(f"Ear{s}", (0.098 * s, -0.025, 1.69), 0.022, m["skin" if faction == "muhafiz" else "mask"], scale=(0.4, 0.8, 1.1)), "Head"))


def build_body_v2(faction, colors, armature):
    m = faction_materials(faction, colors)
    body, P = skin_body()
    regions = dict(m)
    regions["face_region"] = "skin" if faction == "muhafiz" else "mask"
    regions["neck_mask"] = faction == "akinci"
    paint_regions(body, P, regions)
    # automatic weights for the continuous body (smooth bending at every joint)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    parts = []
    gear_common(m, parts)
    (gear_akinci if faction == "akinci" else gear_muhafiz)(m, parts)
    gloves(m, parts, P)
    parts.append(sculpt_head(m, faction))
    # join the gear into the body: vertex groups merge by bone name
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = faction + "_body"
    bpy.ops.object.shade_smooth()
    return body
