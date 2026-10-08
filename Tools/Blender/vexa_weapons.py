"""
VEXA weapon models, generated from parameters (original designs; real-world archetypes only for proportions).

    python vexa_weapons.py --out ../../Game/Assets/Vexa/Resources/Models [--only ak47,awp] [--preview sheet.png]

Writes Weapons/<id>.fbx (PC) and Weapons/Mobile/<id>.fbx (low poly), and with --textures the baked PBR maps
(PC: 4K long guns / 2K sidearms, JPEG albedo + normal, PNG mask). Every weapon has its origin at the
firing hand and these empties, used by the game for effects and hands:
    Muzzle   barrel end (tracers, muzzle flash)
    Support  where the off hand holds the weapon
    Eject    ejection port
"""
import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from vexa_common import (DETAIL, set_detail, reset_scene, box, cyl, sphere, torus, prism, empty, join, export_fbx,
                         clear_objects, tri_count, mat, cut, PALETTE)

PALETTE.setdefault("Dot", ((0.92, 0.9, 0.82), 0.0, 0.4))
import bmesh
from mathutils import Vector

R = -1  # "right" is -X in Blender space


def side_prism(name, profile_yz, width, material, x=0.0, bevel=True, round_=None):
    """Extrude a side silhouette [(y, z), ...] across X (centered on x) — stocks, grips, frames.
    round_: molded / carved furniture: wide rounded edges (PC adds a subdivision pass for smooth curvature)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    left = [bm.verts.new((x - width / 2, y, z)) for y, z in profile_yz]
    right = [bm.verts.new((x + width / 2, y, z)) for y, z in profile_yz]
    n = len(profile_yz)
    bm.faces.new(left)
    bm.faces.new(list(reversed(right)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((left[i], left[j], right[j], right[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    from vexa_common import _finish
    if round_ is None or not DETAIL["fine"]:
        return _finish(o, material, bevel)
    o = _finish(o, material, True, bevel_width=min(round_, width * 0.45))
    o.modifiers["Bevel"].segments = 4
    o.modifiers["Bevel"].angle_limit = math.radians(30)
    sub = o.modifiers.new("Sub", "SUBSURF")
    sub.levels = sub.render_levels = 1
    return o


# ============================================================ part builders

def grip(parts, angle=18, length=0.105, width=0.03, depth=0.045, material="Grip", y=0.0):
    a = math.radians(angle)
    # profile in (y, z): top at origin, slanting back as it goes down
    top_f, top_b = y - depth * 0.45, y + depth * 0.55
    dy = math.tan(a) * length
    prof = [(top_f, 0.005), (top_b, 0.005), (top_b + dy, -length), (top_f + dy + 0.006, -length - 0.004)]
    if DETAIL["fine"]:
        # finger grooves up the front strap
        fb, ft = Vector((top_f + dy + 0.006, -length - 0.004)), Vector((top_f, 0.005))
        for k in range(1, 8):
            t = k / 8
            q = fb + (ft - fb) * t
            prof.append((q.x + (0.0035 if k % 2 == 0 else -0.0012), q.y))
    parts.append(side_prism("Grip", prof, width, material, round_=width * 0.4))
    if DETAIL["fine"]:
        # magazine base / grip cap
        parts.append(box("GripCap", (0, y + dy + 0.002, -length - 0.004), (width * 1.08, depth * 1.12, 0.007), material))
    # trigger guard + trigger
    if DETAIL["small_parts"]:
        parts.append(box("TriggerGuard", (0, y - 0.045, -0.018), (0.008, 0.05, 0.005), "Metal"))
        parts.append(box("TriggerGuardFront", (0, y - 0.068, -0.006), (0.008, 0.005, 0.026), "Metal"))
        parts.append(box("Trigger", (0, y - 0.04, -0.006), (0.004, 0.005, 0.02), "Steel", rot=(math.radians(-15), 0, 0)))


def receiver(parts, front, back, h, w, bore, material):
    o = box("Receiver", (0, (back - front) / 2, bore - h * 0.12), (w, front + back, h), material,
            bevel_width=0.003 if DETAIL["fine"] else None)
    parts.append(o)
    return o


def receiver_detail(recv, parts, rf, rb, rh, rw, bore, ejection=True):
    """PC: real ejection port with the bolt inside, upper/lower split line, a magwell flare."""
    if not DETAIL["fine"]:
        return
    zc = bore - rh * 0.12
    cutters = []
    if ejection:
        cutters.append(box("PortCut", (R * rw / 2, -rf * 0.2, bore + 0.006), (0.012, 0.052, 0.017), "Polymer", bevel=False))
        parts.append(box("Bolt", (R * (rw / 2 - 0.0045), -rf * 0.2, bore + 0.006), (0.004, 0.05, 0.013), "Steel", bevel_width=0.001))
        for i in range(3):
            parts.append(box(f"BoltLug{i}", (R * (rw / 2 - 0.0028), -rf * 0.2 - 0.016 + i * 0.016, bore + 0.006), (0.0015, 0.004, 0.011), "Metal", bevel=False))
    for s_ in (-1, 1):
        cutters.append(box(f"Split{s_}", (s_ * rw / 2, (rb - rf) / 2, zc - rh * 0.06), (0.002, rf + rb + 0.02, 0.0012), "Polymer", bevel=False))
        # lightening cuts along the lower receiver
        cutters.append(box(f"Flute{s_}", (s_ * rw / 2, rb * 0.45, zc - rh * 0.3), (0.0024, rb * 0.5, rh * 0.12), "Polymer", bevel=False))
    cut(recv, cutters)


def barrel(parts, y0, length, r, bore, material="Metal"):
    parts.append(cyl("Barrel", (0, y0, bore), (0, y0 - length, bore), r, material))
    return y0 - length


def muzzle(parts, kind, y, bore, r, length=0.05):
    if kind == "brake" and DETAIL["fine"]:
        b = cyl("Brake", (0, y, bore), (0, y - length, bore), r * 1.8, "Metal")
        cs = []
        for i in range(3):
            yy = y - length * (0.25 + 0.25 * i)
            cs.append(box(f"BrakeSlot{i}", (0, yy, bore + r * 0.15), (r * 5.0, 0.0055, r * 1.2), "Polymer", bevel=False))
        for i in range(2):
            cs.append(cyl(f"BrakeTop{i}", (0, y - length * (0.35 + 0.3 * i), bore), (0, y - length * (0.35 + 0.3 * i), bore + r * 3), r * 0.35, "Polymer", verts=16, bevel=False))
        cs.append(cyl("BrakeBore", (0, y + 0.01, bore), (0, y - length - 0.01, bore), r * 0.75, "Polymer", verts=24, bevel=False))
        parts.append(cut(b, cs))
        return y - length
    if kind == "flash" and DETAIL["fine"]:
        f = cyl("Flash", (0, y, bore), (0, y - length, bore), r * 1.5, "Metal", r2=r * 1.7)
        cs = [cyl("FlashBore", (0, y + 0.01, bore), (0, y - length - 0.01, bore), r * 0.95, "Polymer", verts=24, bevel=False)]
        for k in range(5):
            a = k * 2 * math.pi / 5
            c = Vector((math.cos(a) * r * 1.6, y - length * 0.72, bore + math.sin(a) * r * 1.6))
            cs.append(box(f"Prong{k}", tuple(c), (r * 0.9, length * 0.62, r * 0.9), "Polymer", bevel=False, rot=(0, -a, 0)))
        parts.append(cut(f, cs))
        return y - length
    if kind == "suppressor" and DETAIL["fine"]:
        parts.append(cyl("Suppressor", (0, y + 0.02, bore), (0, y - length, bore), r * 2.4, "Polymer"))
        for i in range(4):
            yy = y + 0.012 - i * 0.006
            parts.append(cyl(f"SupKnurl{i}", (0, yy, bore), (0, yy - 0.003, bore), r * 2.5, "Metal"))
        cap = cyl("SuppressorCap", (0, y - length, bore), (0, y - length - 0.006, bore), r * 2.2, "Metal")
        parts.append(cut(cap, [cyl("CapBore", (0, y - length + 0.01, bore), (0, y - length - 0.02, bore), r * 0.8, "Polymer", verts=24, bevel=False)]))
        return y - length - 0.006
    if kind == "brake":
        parts.append(cyl("Brake", (0, y, bore), (0, y - length, bore), r * 1.8, "Metal"))
        if DETAIL["small_parts"]:
            for i in range(3):
                yy = y - length * (0.25 + 0.25 * i)
                parts.append(box(f"BrakePort{i}", (0, yy, bore), (r * 4.0, 0.006, r * 1.4), "Polymer", bevel=False))
        return y - length
    if kind == "flash":
        parts.append(cyl("Flash", (0, y, bore), (0, y - length, bore), r * 1.5, "Metal", r2=r * 1.7))
        return y - length
    if kind == "suppressor":
        parts.append(cyl("Suppressor", (0, y + 0.02, bore), (0, y - length, bore), r * 2.4, "Polymer"))
        parts.append(cyl("SuppressorCap", (0, y - length, bore), (0, y - length - 0.006, bore), r * 2.2, "Metal"))
        return y - length - 0.006
    return y


def handguard(parts, kind, y0, length, bore, size, material):
    w, h = size
    y1 = y0 - length
    if kind == "round":
        parts.append(cyl("Handguard", (0, y0, bore - 0.004), (0, y1, bore - 0.004), w / 2, material))
        if DETAIL["small_parts"]:
            n = int(length / 0.022)
            for i in range(n):
                yy = y0 - 0.012 - i * 0.022
                parts.append(box(f"Rib{i}", (0, yy, bore - 0.004), (w * 1.04, 0.006, h * 1.02), material))
    elif kind == "vent" and DETAIL["fine"]:
        hg = box("Handguard", (0, (y0 + y1) / 2, bore - 0.006), (w, length, h), material)
        cs = []
        n = int(length / 0.026)
        for i in range(n):
            yy = y0 - 0.018 - i * 0.026
            for s_ in (-1, 1):
                cs.append(box(f"Vent{i}{s_}", (s_ * w / 2, yy, bore - 0.004), (0.012, 0.014, h * 0.42), "Polymer", bevel=False))
        parts.append(cut(hg, cs))
    elif kind == "vent":
        parts.append(box("Handguard", (0, (y0 + y1) / 2, bore - 0.006), (w, length, h), material))
        if DETAIL["small_parts"]:
            n = int(length / 0.03)
            for i in range(n):
                yy = y0 - 0.02 - i * 0.03
                for s in (-1, 1):
                    parts.append(box(f"Vent{i}{s}", (s * w / 2, yy, bore - 0.004), (0.004, 0.016, h * 0.4), "Polymer", bevel=False))
    elif kind == "wood":
        prof = [(y0, bore + h * 0.35), (y1 + 0.01, bore + h * 0.3), (y1, bore - h * 0.2), (y1 + 0.02, bore - h * 0.62), (y0, bore - h * 0.65)]
        parts.append(side_prism("Handguard", prof, w, material, round_=w * 0.35))
        if DETAIL["fine"]:
            # retainer bands at both ends, finger groove along the side
            for k, yy in enumerate((y0 - 0.006, y1 + 0.012)):
                parts.append(box(f"HgBand{k}", (0, yy, bore - h * 0.15), (w * 1.06, 0.01, h * 1.02), "Metal", bevel_width=0.002))
    else:  # box / rail
        hg = box("Handguard", (0, (y0 + y1) / 2, bore - 0.004), (w, length, h), material)
        if kind == "rail" and DETAIL["fine"]:
            # M-LOK style slots down both sides
            cs = []
            n = max(1, int((length - 0.02) / 0.034))
            for i in range(n):
                yy = y0 - 0.02 - i * 0.034
                for s_ in (-1, 1):
                    cs.append(box(f"Mlok{i}{s_}", (s_ * w / 2, yy, bore - 0.01), (0.008, 0.022, 0.0075), "Polymer", bevel_width=0.003))
            hg = cut(hg, cs)
        parts.append(hg)
        if kind == "rail" and DETAIL["small_parts"]:
            for s in (-1, 1):
                parts.append(box(f"SideRail{s}", (s * (w / 2 + 0.003), (y0 + y1) / 2, bore - 0.004), (0.006, length * 0.9, 0.01), "Metal"))
    return y1


def top_rail(parts, y0, y1, z, w=0.022):
    parts.append(box("TopRail", (0, (y0 + y1) / 2, z + 0.004), (w, abs(y1 - y0), 0.008), "Metal"))
    if DETAIL["small_parts"]:
        n = int(abs(y1 - y0) / 0.012)
        for i in range(n):
            yy = min(y0, y1) + 0.006 + i * 0.012
            parts.append(box(f"RailSlot{i}", (0, yy, z + 0.0085), (w * 1.02, 0.004, 0.002), "Polymer", bevel=False))


def iron_sights(parts, y_front, y_rear, z):
    if not DETAIL["fine"]:
        parts.append(box("FrontSight", (0, y_front, z + 0.018), (0.006, 0.006, 0.03), "Metal"))
        parts.append(box("RearSight", (0, y_rear, z + 0.012), (0.018, 0.012, 0.018), "Metal"))
        return
    # front: base, post with protective ears
    parts.append(box("FrontBase", (0, y_front, z + 0.006), (0.018, 0.014, 0.012), "Metal"))
    parts.append(box("FrontPost", (0, y_front, z + 0.022), (0.0032, 0.0032, 0.022), "Steel", bevel_width=0.0008))
    for s_ in (-1, 1):
        parts.append(box(f"FrontEar{s_}", (s_ * 0.0075, y_front, z + 0.02), (0.003, 0.011, 0.026), "Metal", bevel_width=0.001))
    # rear: base, aperture ring between two ears
    parts.append(box("RearBase", (0, y_rear, z + 0.005), (0.024, 0.018, 0.01), "Metal"))
    parts.append(torus("Aperture", (0, y_rear, z + 0.019), 0.0045, 0.0016, "Steel", rot=(math.radians(90), 0, 0)))
    parts.append(box("ApertureStem", (0, y_rear, z + 0.0115), (0.003, 0.004, 0.006), "Steel", bevel=False))
    for s_ in (-1, 1):
        parts.append(box(f"RearEar{s_}", (s_ * 0.0095, y_rear, z + 0.017), (0.0035, 0.014, 0.022), "Metal", bevel_width=0.001))
    parts.append(cyl("WindageKnob", (R * 0.013, y_rear, z + 0.006), (R * 0.019, y_rear, z + 0.006), 0.004, "Steel", verts=16))


def carry_handle(parts, y0, y1, z, h=0.035):
    parts.append(box("CarryTop", (0, (y0 + y1) / 2, z + h), (0.018, abs(y1 - y0), 0.012), "Polymer"))
    for i, yy in enumerate((y0, y1)):
        parts.append(box(f"CarryPost{i}", (0, yy, z + h / 2), (0.016, 0.02, h), "Polymer"))


def scope(parts, y_center, z, length, r, objective=1.35):
    y0, y1 = y_center + length / 2, y_center - length / 2
    parts.append(cyl("ScopeTube", (0, y0 - 0.03, z), (0, y1 + 0.04, z), r, "Metal"))
    parts.append(cyl("ScopeOcular", (0, y0, z), (0, y0 - 0.035, z), r * 1.25, "Metal", r2=r))
    parts.append(cyl("ScopeObjective", (0, y1 + 0.05, z), (0, y1, z), r, "Metal", r2=r * objective))
    parts.append(cyl("LensFront", (0, y1 + 0.001, z), (0, y1 - 0.0015, z), r * objective * 0.92, "Lens", bevel=False))
    parts.append(cyl("LensRear", (0, y0 + 0.0015, z), (0, y0 - 0.001, z), r * 1.15, "Lens", bevel=False))
    parts.append(cyl("Turret", (0, y_center, z), (0, y_center, z + r * 1.6), r * 0.45, "Metal"))
    parts.append(cyl("TurretSide", (0, y_center, z), (R * r * 1.6, y_center, z), r * 0.4, "Metal"))
    if not DETAIL["fine"]:
        for i, yy in enumerate((y_center + length * 0.25, y_center - length * 0.25)):
            parts.append(box(f"ScopeMount{i}", (0, yy, z - r * 1.1), (0.02, 0.014, r * 1.6), "Metal"))
        return
    # rings: clamp ring around the tube, base on the rail, two cross screws each
    for i, yy in enumerate((y_center + length * 0.25, y_center - length * 0.25)):
        parts.append(cyl(f"Ring{i}", (0, yy + 0.008, z), (0, yy - 0.008, z), r * 1.22, "Metal"))
        parts.append(box(f"RingBase{i}", (0, yy, z - r * 1.35), (0.022, 0.016, r * 1.0), "Metal"))
        for s_ in (-1, 1):
            parts.append(cyl(f"RingScrew{i}{s_}", (s_ * r * 1.15, yy + s_ * 0.004, z + r * 0.55), (s_ * r * 1.35, yy + s_ * 0.004, z + r * 0.6), 0.0022, "Steel", verts=12))
        parts.append(cyl(f"RingNut{i}", (R * 0.011, yy, z - r * 1.5), (R * 0.017, yy, z - r * 1.5), 0.0045, "Steel", verts=6))
    # knurled turret caps, parallax knob, eyepiece rubber, sunshade lip
    for k in range(5):
        zz = z + r * 1.6 - 0.002 - k * 0.0028
        parts.append(cyl(f"TurretKnurl{k}", (0, y_center, zz), (0, y_center, zz - 0.0016), r * 0.5, "Steel", verts=24))
    for k in range(4):
        xx = R * (r * 1.6 - 0.002 - k * 0.0028)
        parts.append(cyl(f"SideKnurl{k}", (xx, y_center, z), (xx - R * 0.0016, y_center, z), r * 0.45, "Steel", verts=24))
    parts.append(cyl("Parallax", (0, y_center - 0.02, z), (-R * r * 1.5, y_center - 0.02, z), r * 0.55, "Metal"))
    parts.append(torus("EyeRubber", (0, y0 - 0.006, z), r * 1.22, r * 0.12, "Grip", rot=(math.radians(90), 0, 0)))
    parts.append(torus("Sunshade", (0, y1 + 0.004, z), r * objective * 0.98, r * 0.08, "Metal", rot=(math.radians(90), 0, 0)))
    parts.append(cyl("PowerRing", (0, y0 - 0.04, z), (0, y0 - 0.052, z), r * 1.08, "Grip", verts=48))


def magazine(parts, kind, y, z, length, angle=8, material="Metal", w=0.024, d=0.06):
    a = math.radians(angle)
    if kind == "straight":
        # slight forward cant: the bottom of the magazine sits ahead of the top
        cy, cz = y - math.sin(a) * length / 2, z - math.cos(a) * length / 2
        parts.append(box("Mag", (0, cy, cz), (w, d, length), material, rot=(-a, 0, 0)))
        parts.append(box("MagBase", (0, y - math.sin(a) * length, z - math.cos(a) * length), (w * 1.15, d * 1.08, 0.008), material, rot=(-a, 0, 0)))
        if DETAIL["fine"] and length > 0.08:
            for s_ in (-1, 1):
                for k, off in enumerate((-0.22, 0.22)):
                    yy = cy + d * off * math.cos(a)
                    zz = cz + d * off * math.sin(a) * 0
                    parts.append(box(f"MagRib{s_}{k}", (s_ * (w / 2 + 0.0006), yy, zz), (0.0016, 0.007, length * 0.72), material, bevel_width=0.0006, rot=(-a, 0, 0)))
            # witness holes on the right side
            for k in range(3):
                t = 0.25 + 0.18 * k
                yy, zz = y - math.sin(a) * length * t, z - math.cos(a) * length * t
                parts.append(cyl(f"Witness{k}", (R * (w / 2 + 0.0016), yy + d * 0.3, zz), (R * (w / 2 - 0.0005), yy + d * 0.3, zz), 0.0022, "Polymer", verts=12, bevel=False))
    elif kind == "curved":
        segs = 5
        seg_len = length / segs
        cy, cz, ang = y, z, a
        for i in range(segs):
            ny, nz = cy - math.sin(ang) * seg_len, cz - math.cos(ang) * seg_len
            parts.append(box(f"Mag{i}", (0, (cy + ny) / 2, (cz + nz) / 2), (w, d, seg_len * 1.1), material, rot=(-ang, 0, 0)))
            if DETAIL["fine"]:
                for s_ in (-1, 1):
                    parts.append(box(f"MagRib{i}{s_}", (s_ * (w / 2 + 0.0006), (cy + ny) / 2 + d * 0.18 * math.cos(ang), (cz + nz) / 2 - d * 0.18 * math.sin(ang)),
                                     (0.0016, 0.008, seg_len * 1.05), material, bevel_width=0.0006, rot=(-ang, 0, 0)))
            cy, cz = ny, nz
            ang += math.radians(8)   # curves forward, like real curved magazines
        parts.append(box("MagBase", (0, cy, cz), (w * 1.15, d * 1.05, 0.008), material, rot=(-(ang - math.radians(8)), 0, 0)))
    elif kind == "drum":
        parts.append(cyl("Drum", (-0.05, y, z - 0.06), (0.05, y, z - 0.06), 0.065, material))
    elif kind == "box":
        parts.append(box("AmmoBox", (R * -0.01, y, z - 0.07), (0.07, 0.12, 0.11), material))
    elif kind == "top":
        parts.append(box("TopMag", (0, y, z + 0.035), (0.05, length, 0.022), "Glass"))
    elif kind == "helical":
        parts.append(cyl("Helical", (0, y, z - 0.035), (0, y - length, z - 0.035), 0.03, material))


def stock(parts, kind, y0, bore, length, material, drop=0.04):
    y1 = y0 + length
    if kind == "fixed":
        prof = [(y0, bore + 0.02), (y1, bore - drop + 0.005), (y1, bore - drop - 0.11), (y1 - 0.03, bore - drop - 0.115), (y0, bore - 0.05)]
        parts.append(side_prism("Stock", prof, 0.042, material, round_=0.014))
        parts.append(box("ButtPad", (0, y1 + 0.006, bore - drop - 0.052), (0.045, 0.012, 0.13), "Grip"))
        if DETAIL["fine"]:
            parts.append(box("ButtPlate", (0, y1 + 0.0135, bore - drop - 0.052), (0.043, 0.003, 0.125), "Metal"))
            for k, dz in enumerate((-0.015, -0.09)):
                parts.append(cyl(f"ButtScrew{k}", (0, y1 + 0.014, bore - drop + dz), (0, y1 + 0.017, bore - drop + dz), 0.0035, "Steel", verts=12))
            parts.append(torus("SlingSwivel", (0, y1 - 0.06, bore - drop - 0.118), 0.008, 0.0016, "Steel", rot=(0, math.radians(90), 0)))
    elif kind == "thumbhole":
        prof = [(y0, bore + 0.03), (y1, bore - drop + 0.02), (y1, bore - drop - 0.13), (y0 + length * 0.55, bore - drop - 0.11),
                (y0 + length * 0.42, bore - 0.06), (y0 + length * 0.25, bore - 0.055), (y0 + 0.02, bore - 0.13), (y0 - 0.02, bore - 0.11), (y0, bore - 0.04)]
        parts.append(side_prism("Stock", prof, 0.045, material, round_=0.014))
        parts.append(box("ButtPad", (0, y1 + 0.008, bore - drop - 0.055), (0.048, 0.016, 0.15), "Grip"))
        parts.append(box("CheekRest", (0, y0 + length * 0.7, bore - drop + 0.03), (0.04, length * 0.4, 0.02), material))
    elif kind == "collapsible":
        parts.append(cyl("BufferTube", (0, y0, bore - 0.005), (0, y1 - 0.02, bore - 0.005), 0.015, "Metal"))
        prof = [(y1 - 0.09, bore + 0.012), (y1, bore + 0.012), (y1, bore - 0.11), (y1 - 0.03, bore - 0.11), (y1 - 0.09, bore - 0.035)]
        st = side_prism("Stock", prof, 0.04, material, round_=0.01)
        if DETAIL["fine"]:
            # sling slot through the stock, a castle nut and the adjustment holes under the buffer tube
            st = cut(st, [box("SlingSlot", (0, y1 - 0.03, bore - 0.085), (0.06, 0.01, 0.022), "Polymer", bevel_width=0.004)])
            parts.append(cyl("CastleNut", (0, y0 + 0.002, bore - 0.005), (0, y0 + 0.012, bore - 0.005), 0.018, "Steel"))
            for k in range(5):
                yy = y0 + 0.05 + k * 0.022
                parts.append(cyl(f"AdjHole{k}", (0, yy, bore - 0.019), (0, yy, bore - 0.0215), 0.003, "Polymer", verts=12, bevel=False))
        parts.append(st)
    elif kind == "skeleton":
        parts.append(box("StockTop", (0, (y0 + y1) / 2, bore), (0.012, length, 0.012), "Metal"))
        parts.append(box("StockBottom", (0, (y0 + y1) / 2 + 0.02, bore - 0.075), (0.012, length - 0.04, 0.012), "Metal", rot=(math.radians(-6), 0, 0)))
        parts.append(box("StockButt", (0, y1, bore - 0.045), (0.035, 0.014, 0.12), "Grip"))
    elif kind == "wire":
        for s in (-1, 1):
            parts.append(cyl(f"Wire{s}", (s * 0.012, y0, bore - 0.01), (s * 0.012, y1, bore - 0.02), 0.0035, "Metal"))
        parts.append(box("WireButt", (0, y1, bore - 0.04), (0.035, 0.008, 0.07), "Metal"))
    elif kind == "folded":
        parts.append(box("FoldedStock", (R * -0.03, y0 - length * 0.4, bore - 0.01), (0.012, length * 0.8, 0.05), material))


def bolt_handle(parts, y, z):
    parts.append(cyl("BoltArm", (0, y, z), (R * 0.045, y + 0.01, z - 0.01), 0.004, "Steel"))
    parts.append(sphere("BoltKnob", (R * 0.048, y + 0.012, z - 0.012), 0.009, "Steel"))


def foregrip(parts, y, z):
    parts.append(cyl("Foregrip", (0, y, z), (0, y + 0.01, z - 0.09), 0.015, "Grip"))


def bipod(parts, y, z):
    for s in (-1, 1):
        parts.append(cyl(f"Bipod{s}", (s * 0.012, y, z - 0.01), (s * 0.02, y - 0.18, z - 0.025), 0.005, "Metal"))


def fine_rifle(parts, rf, rb, rh, rw, bore, mag_y, mag_kind):
    """PC-only details that make close-up first-person views read as a real object."""
    if not DETAIL["fine"]:
        return
    side = R * (rw / 2 + 0.0015)
    zc = bore - rh * 0.12
    # receiver pins and screws
    for i, (yy, zz) in enumerate(((-rf * 0.6, zc - rh * 0.25), (rb * 0.3, zc - rh * 0.28), (rb * 0.75, zc + rh * 0.1), (-rf * 0.15, zc + rh * 0.2))):
        parts.append(cyl(f"Pin{i}", (side, yy, zz), (side * 1.06, yy, zz), 0.0032, "Steel", verts=12))
    # selector lever and magazine release
    parts.append(box("Selector", (side * 1.02, rb * 0.15, zc - rh * 0.05), (0.004, 0.022, 0.007), "Steel", rot=(math.radians(-20), 0, 0)))
    parts.append(box("MagRelease", (side * 1.02, mag_y + 0.03, zc - rh * 0.32), (0.004, 0.012, 0.01), "Steel"))
    # grip texture: shallow ribs down both sides
    for i in range(6):
        z = -0.02 - i * 0.013
        for s in (-1, 1):
            parts.append(box(f"GripRib{i}{s}", (s * 0.0158, 0.006 + i * 0.0024, z), (0.0016, 0.034, 0.0045), "Grip", bevel=False))
    # sling loops front and back
    parts.append(torus("SlingRear", (0, rb + 0.01, zc - rh * 0.4), 0.008, 0.0016, "Steel", rot=(0, math.radians(90), 0)))
    # magazine ribs
    if mag_kind in ("straight", "curved"):
        for i in range(3):
            parts.append(box(f"MagRib{i}", (R * 0.0125, mag_y - 0.004 * i, zc - rh * 0.55 - 0.03 - i * 0.035), (0.0016, 0.05, 0.004), "Polymer", bevel=False))


def fine_pistol(parts, slide, bore):
    if not DETAIL["fine"]:
        return
    sl, sh, sw = slide
    front = sl * 0.72
    side = R * (sw / 2 + 0.001)
    parts.append(cyl("TakedownPin", (side, -front * 0.35, bore - sh * 0.62), (side * 1.08, -front * 0.35, bore - sh * 0.62), 0.0028, "Steel", verts=12))
    parts.append(box("SlideStop", (side * 1.03, -front * 0.15, bore - sh * 0.35), (0.003, 0.02, 0.005), "Steel"))
    parts.append(box("MagButton", (side * 1.03, -0.005, -0.012), (0.003, 0.009, 0.009), "Steel"))
    for i in range(7):
        for s in (-1, 1):
            parts.append(box(f"Stipple{i}{s}", (s * 0.0145, 0.02 + i * 0.002, -0.02 - i * 0.01), (0.0014, 0.03, 0.004), "Grip", bevel=False))


# ============================================================ weapon definitions

def rifle(p):
    parts = []
    bore = p.get("bore", 0.045)
    rf, rb, rh, rw = p["recv"]
    body = p.get("body", "Metal")
    recv = receiver(parts, rf, rb, rh, rw, bore, body)
    grip(parts, p.get("grip_angle", 18), p.get("grip_len", 0.1), material=p.get("grip_mat", "Grip"))
    receiver_detail(recv, parts, rf, rb, rh, rw, bore, p.get("ejection", True))
    y = -rf
    hg_kind, hg_len, hg_size, hg_mat = p.get("hg", ("round", 0.2, (0.05, 0.05), "Polymer"))
    y_hg_end = handguard(parts, hg_kind, y, hg_len, bore, hg_size, hg_mat) if hg_len > 0 else y
    blen, br = p.get("barrel", (0.3, 0.009))
    y_end = barrel(parts, y, blen, br, bore)
    mk = p.get("muzzle", ("flash", 0.045))
    y_end = muzzle(parts, mk[0], y_end, bore, br, mk[1])
    top = p.get("top", "rail")
    top_z = bore - rh * 0.12 + rh / 2
    if top == "rail":
        top_rail(parts, -rf, rb * 0.9, top_z)
        if p.get("hg_rail"):
            top_rail(parts, y, y_hg_end + 0.01, bore - 0.004 + hg_size[1] / 2 - 0.004)
        if DETAIL["fine"] and p.get("charging"):
            # AR-style upper: forward assist, charging handle latch, brass deflector, delta ring, takedown pins
            parts.append(cyl("ForwardAssist", (R * (rw / 2 - 0.002), rb * 0.25, bore + 0.006), (R * (rw / 2 + 0.012), rb * 0.32, bore + 0.006), 0.0065, "Metal", verts=24))
            parts.append(box("Deflector", (R * (rw / 2 + 0.003), rb * 0.05, bore + 0.012), (0.008, 0.016, 0.012), body))
            parts.append(box("ChargeLatch", (0, rb + 0.004, top_z - 0.002), (0.03, 0.012, 0.008), "Metal"))
            parts.append(torus("DeltaRing", (0, -rf - 0.004, bore - 0.004), hg_size[0] * 0.53, 0.004, "Metal", rot=(math.radians(90), 0, 0)))
            for k, yy in enumerate((-rf * 0.7, rb * 0.8)):
                parts.append(cyl(f"TakedownPin{k}", (R * (rw / 2 - 0.001), yy, bore - rh * 0.4), (R * (rw / 2 + 0.0025), yy, bore - rh * 0.4), 0.0035, "Steel", verts=16))
            mag_y0 = p.get("mag", ("straight", 0.18, -0.07, 8, "Metal"))[2]
            parts.append(box("MagwellFlare", (0, mag_y0, bore - rh * 0.62), (rw * 1.12, 0.075, 0.012), body))
    elif top == "carry":
        carry_handle(parts, rb * 0.6, -rf - p.get("carry_ext", 0.0), top_z, p.get("carry_h", 0.04))
    elif top == "gas":
        parts.append(cyl("GasTube", (0, -rf, bore + 0.022), (0, y_hg_end - 0.03, bore + 0.022), 0.009, "Metal"))
        parts.append(box("DustCover", (0, rb * 0.3, top_z + 0.004), (rw * 0.92, rb * 0.6 + rf * 0.7, 0.012), body))
        if DETAIL["fine"]:
            # stamped dust cover ribs, rear sight block with leaf, gas block, front sight tower, cleaning rod, rivets
            for k in range(3):
                parts.append(box(f"CoverRib{k}", (0, rb * 0.55 + k * 0.012 - 0.012, top_z + 0.0105), (rw * 0.8, 0.004, 0.002), body, bevel_width=0.0008))
            parts.append(box("RearSightBlock", (0, -rf + 0.012, top_z + 0.004), (rw * 0.8, 0.03, 0.022), "Metal"))
            parts.append(box("SightLeaf", (0, -rf + 0.022, top_z + 0.016), (rw * 0.5, 0.05, 0.004), "Metal", rot=(math.radians(-4), 0, 0)))
            gy = y_hg_end - 0.02
            parts.append(box("GasBlock", (0, gy, bore + 0.012), (0.026, 0.03, 0.036), "Metal"))
            fy = y_end + p.get("muzzle", ("flash", 0.045))[1] + 0.03
            parts.append(box("FrontSightBase", (0, fy, bore + 0.01), (0.022, 0.024, 0.034), "Metal"))
            parts.append(cyl("FrontHood", (0, fy + 0.006, bore + 0.034), (0, fy - 0.006, bore + 0.034), 0.0095, "Metal", verts=24))
            parts.append(box("FrontPostAK", (0, fy, bore + 0.034), (0.003, 0.003, 0.016), "Steel", bevel=False))
            parts.append(cyl("CleaningRod", (0, -rf - 0.01, bore - 0.017), (0, y_end + 0.06, bore - 0.017), 0.003, "Steel", verts=12))
            for k in range(4):
                for s_ in (-1, 1):
                    yy = -rf + 0.012 + k * 0.012 if k < 2 else rb * 0.9 - (k - 2) * 0.012
                    parts.append(cyl(f"Rivet{k}{s_}", (s_ * (rw / 2 - 0.0005), yy, bore - rh * 0.3), (s_ * (rw / 2 + 0.0012), yy, bore - rh * 0.3), 0.0022, "Steel", verts=12))
            parts.append(box("LongSelector", (R * (rw / 2 + 0.0012), rb * 0.35, bore - rh * 0.05), (0.0025, 0.085, 0.012), "Metal", rot=(math.radians(-6), 0, 0)))
    if p.get("sights", True) and not p.get("scope"):
        iron_sights(parts, y_hg_end - 0.01, rb * 0.5, top_z + (0.008 if top == "rail" else 0))
    if p.get("scope"):
        sl, sr = p["scope"]
        scope(parts, p.get("scope_y", rb * 0.1 - rf * 0.3), top_z + 0.045, sl, sr, p.get("objective", 1.35))
    mag_kind, mag_len, mag_y, mag_angle, mag_mat = p.get("mag", ("straight", 0.18, -0.07, 8, "Metal"))
    if mag_kind != "none":
        magazine(parts, mag_kind, mag_y, bore - rh * 0.55, mag_len, mag_angle, mag_mat, w=p.get("mag_w", 0.024), d=p.get("mag_d", 0.06))
    sk, sl_, smat = p.get("stock", ("fixed", 0.28, "Polymer"))
    if sk != "none":
        stock(parts, sk, rb, bore, sl_, smat, p.get("drop", 0.04))
    if p.get("bolt"):
        bolt_handle(parts, rb * 0.4, bore)
    if p.get("charging"):
        parts.append(box("ChargingHandle", (R * (rw / 2 + 0.008), -rf * 0.3, bore + 0.01), (0.016, 0.012, 0.01), "Steel"))
    if p.get("foregrip"):
        foregrip(parts, y - hg_len * 0.6, bore - hg_size[1] / 2)
    if p.get("bipod"):
        bipod(parts, y_hg_end + 0.02, bore - 0.02)
    if p.get("pump"):
        parts.append(cyl("Tube", (0, -rf, bore - 0.03), (0, y_end + 0.08, bore - 0.03), 0.014, "Metal"))
        parts.append(cyl("Pump", (0, y - 0.04, bore - 0.03), (0, y - 0.04 - p["pump"], bore - 0.03), 0.022, "Polymer"))
    if p.get("ejection", True) and DETAIL["small_parts"] and not DETAIL["fine"]:
        parts.append(box("Port", (R * rw / 2, -rf * 0.2, bore + 0.006), (0.004, 0.05, 0.016), "Polymer", bevel=False))
    fine_rifle(parts, rf, rb, rh, rw, bore, mag_y, mag_kind)
    support_y = p.get("support_y", y - hg_len * 0.55 if hg_len > 0 else y - 0.05)
    return parts, (0, y_end, bore), (0, support_y, bore - 0.02), (R * rw / 2, -rf * 0.2, bore + 0.006)


def bullpup(p):
    """Magazine behind the grip, receiver forms the stock (FAMAS / AUG / P90 style)."""
    parts = []
    bore = p.get("bore", 0.05)
    body = p.get("body", "Polymer")
    length, h, w = p["body_dims"]
    y0 = -p.get("front", 0.2)
    y1 = y0 + length
    prof = [(y0, bore + h * 0.4), (y1 - 0.06, bore + h * 0.45), (y1, bore + h * 0.3), (y1, bore - h * 0.75),
            (y1 - 0.12, bore - h * 0.75), (y1 - 0.15, bore - h * 0.5), (0.03, bore - h * 0.45), (y0, bore - h * 0.4)]
    parts.append(side_prism("Body", prof, w, body, round_=w * 0.3))
    parts.append(box("ButtPad", (0, y1 + 0.006, bore - h * 0.2), (w * 1.05, 0.012, h * 1.1), "Grip"))
    grip(parts, p.get("grip_angle", 12), 0.1)
    blen, br = p.get("barrel", (0.25, 0.009))
    y_end = barrel(parts, y0, blen, br, bore)
    mk = p.get("muzzle", ("flash", 0.04))
    y_end = muzzle(parts, mk[0], y_end, bore, br, mk[1])
    if p.get("carry"):
        carry_handle(parts, y1 - 0.12, y0 + 0.01, bore + h * 0.42, 0.045)
    if p.get("scope"):
        sl, sr = p["scope"]
        scope(parts, y0 + length * 0.35, bore + h * 0.4 + 0.03, sl, sr, 1.15)
    if p.get("mag_top"):
        magazine(parts, "top", y0 + length * 0.45, bore + h * 0.28, length * 0.6)
    else:
        magazine(parts, "straight", p.get("mag_y", 0.09), bore - h * 0.6, p.get("mag_len", 0.16), 4, p.get("mag_mat", "Metal"))
    if p.get("foregrip"):
        foregrip(parts, y0 + 0.04, bore - h * 0.4)
    if p.get("thumbhole"):
        parts.append(box("Thumbhole", (0, -0.02, bore - h * 0.3), (w * 1.02, 0.07, h * 0.25), "Polymer", bevel=False))
    support_y = y0 + 0.04
    return parts, (0, y_end, bore), (0, support_y, bore - h * 0.4), (R * w / 2, y1 - 0.15, bore)


def pistol(p):
    parts = []
    bore = p.get("bore", 0.032)
    sl, sh, sw = p["slide"]
    front = sl * 0.72
    slide_mat = p.get("slide_mat", "Metal")
    slide = box("Slide", (0, sl / 2 - front, bore + 0.002), (sw, sl, sh), slide_mat, bevel_width=0.003)
    if DETAIL["fine"]:
        cs = []
        for i in range(9):
            for s_ in (-1, 1):
                cs.append(box(f"SerCut{i}{s_}", (s_ * sw / 2, sl - front - 0.01 - i * 0.0042, bore + 0.002), (0.0024, 0.0016, sh * 0.72), "Polymer", bevel=False,
                              rot=(math.radians(-8), 0, 0)))
        for i in range(4):
            for s_ in (-1, 1):
                cs.append(box(f"FrontSer{i}{s_}", (s_ * sw / 2, -front + 0.018 + i * 0.0042, bore + 0.002), (0.0024, 0.0016, sh * 0.6), "Polymer", bevel=False))
        # ejection port on top, the barrel hood shows through it
        cs.append(box("EjectCut", (R * 0.003, -front * 0.15, bore + sh * 0.5 + 0.002), (sw * 0.75, 0.034, sh * 0.5), "Polymer", bevel_width=0.001))
        slide = cut(slide, cs)
        parts.append(box("BarrelHood", (R * 0.002, -front * 0.15, bore + sh * 0.18), (sw * 0.55, 0.036, 0.012), "Steel", bevel_width=0.001))
        # sights with dots
        parts.append(box("FrontSightP", (0, -front + 0.007, bore + sh / 2 + 0.005), (0.0035, 0.005, 0.007), "Metal", bevel_width=0.0008))
        parts.append(cyl("FrontDot", (0, -front + 0.0045, bore + sh / 2 + 0.006), (0, -front + 0.0042, bore + sh / 2 + 0.006), 0.0012, "Dot", verts=12, bevel=False))
        rear_y = sl - front - 0.008
        for s_ in (-1, 1):
            parts.append(box(f"RearSightP{s_}", (s_ * 0.0042, rear_y, bore + sh / 2 + 0.0045), (0.0045, 0.006, 0.006), "Metal", bevel_width=0.0008))
            parts.append(cyl(f"RearDot{s_}", (s_ * 0.0042, rear_y - 0.0032, bore + sh / 2 + 0.005), (s_ * 0.0042, rear_y - 0.0035, bore + sh / 2 + 0.005), 0.0011, "Dot", verts=12, bevel=False))
    parts.append(slide)
    if DETAIL["small_parts"] and not DETAIL["fine"]:
        for i in range(5):
            parts.append(box(f"Serration{i}", (0, sl - front - 0.012 - i * 0.006, bore + 0.002), (sw * 1.03, 0.002, sh * 0.7), "Polymer", bevel=False))
        parts.append(box("PFront", (0, -front + 0.006, bore + sh / 2 + 0.003), (0.003, 0.004, 0.005), "Metal"))
        parts.append(box("PRear", (0, sl - front - 0.008, bore + sh / 2 + 0.003), (0.012, 0.005, 0.005), "Metal"))
    frame_mat = p.get("frame_mat", "Polymer")
    frame = box("Frame", (0, -front * 0.45, bore - sh * 0.62), (sw * 0.95, front * 1.1, sh * 0.55), frame_mat)
    if DETAIL["fine"]:
        # accessory rail slots under the dust cover
        frame = cut(frame, [box(f"RailSlot{i}", (0, -front * 0.75 + i * 0.011, bore - sh * 0.9), (sw * 1.2, 0.004, 0.004), "Polymer", bevel=False) for i in range(3)])
    parts.append(frame)
    grip(parts, p.get("grip_angle", 20), p.get("grip_len", 0.085), 0.028, 0.04, frame_mat if p.get("grip_same") else "Grip", y=0.015)
    parts.append(cyl("BarrelTip", (0, -front, bore + 0.002), (0, -front - 0.004, bore + 0.002), p.get("bore_r", 0.0065), "Steel"))
    y_end = -front - 0.004
    if p.get("suppressor"):
        y_end = muzzle(parts, "suppressor", y_end, bore + 0.002, 0.0065, p["suppressor"])
    if p.get("hammer") and DETAIL["small_parts"]:
        parts.append(box("Hammer", (0, sl - front + 0.004, bore - 0.004), (0.006, 0.01, 0.012), "Steel"))
    if p.get("mag_ext"):
        parts.append(box("MagExt", (0, 0.038, -0.1), (0.026, 0.035, 0.03), "Metal"))
    fine_pistol(parts, (sl, sh, sw), bore)
    return parts, (0, y_end, bore + 0.002), (0, 0.02, -0.06), (R * sw / 2, -0.01, bore + 0.006)


def revolver(p):
    parts = []
    bore = 0.035
    parts.append(box("Frame", (0, -0.02, bore - 0.004), (0.026, 0.09, 0.04), "Steel"))
    parts.append(cyl("Cylinder", (0, -0.005, bore - 0.012), (0, -0.05, bore - 0.012), 0.02, "Steel"))
    if DETAIL["small_parts"]:
        for i in range(6):
            a = i * math.pi / 3
            x, z = math.cos(a) * 0.012, math.sin(a) * 0.012
            parts.append(cyl(f"Chamber{i}", (x, -0.004, bore - 0.012 + z), (x, -0.051, bore - 0.012 + z), 0.0035, "Polymer", bevel=False))
    parts.append(cyl("Barrel", (0, -0.05, bore + 0.004), (0, -0.2, bore + 0.004), 0.008, "Steel"))
    parts.append(box("Rib", (0, -0.125, bore + 0.013), (0.008, 0.15, 0.006), "Steel"))
    parts.append(box("Hammer", (0, 0.03, bore + 0.006), (0.008, 0.012, 0.018), "Steel", rot=(math.radians(25), 0, 0)))
    grip(parts, 28, 0.09, 0.03, 0.04, "Wood", y=0.018)
    return parts, (0, -0.2, bore + 0.004), (0, 0.02, -0.06), (R * 0.013, -0.03, bore)


def tec9(p):
    parts = []
    bore = 0.04
    parts.append(box("Receiver", (0, -0.03, bore), (0.032, 0.17, 0.045), "Metal"))
    parts.append(cyl("BarrelShroud", (0, -0.115, bore + 0.005), (0, -0.2, bore + 0.005), 0.013, "Metal"))
    if DETAIL["small_parts"]:
        for i in range(4):
            parts.append(box(f"Hole{i}", (0, -0.13 - i * 0.018, bore + 0.005), (0.03, 0.008, 0.008), "Polymer", bevel=False))
    grip(parts, 12, 0.095, 0.03, 0.045, "Polymer", y=0.03)
    magazine(parts, "straight", -0.08, bore - 0.02, 0.17, 2, "Metal", w=0.022, d=0.03)
    return parts, (0, -0.2, bore + 0.005), (0, -0.08, -0.05), (R * 0.016, -0.04, bore + 0.01)


def knife(p):
    parts = []
    parts.append(cyl("Handle", (0, 0.03, 0), (0, -0.075, 0), 0.015, "Grip"))
    if DETAIL["small_parts"]:
        for i in range(5):
            parts.append(cyl(f"GripRing{i}", (0, 0.01 - i * 0.018, 0), (0, 0.004 - i * 0.018, 0), 0.0165, "Polymer"))
    parts.append(box("Guard", (0, -0.08, 0.002), (0.012, 0.008, 0.05), "Steel"))
    # blade: side profile with a clip point
    prof = [(-0.084, 0.018), (-0.2, 0.018), (-0.245, 0.0), (-0.23, -0.012), (-0.084, -0.016)]
    parts.append(side_prism("Blade", prof, 0.005, "Steel"))
    parts.append(box("Spine", (0, -0.15, 0.016), (0.0055, 0.13, 0.004), "Metal"))
    return parts, (0, -0.245, 0), (0, 0, 0), (0, 0, 0)


def taser(p):
    parts = []
    parts.append(box("Body", (0, -0.04, 0.03), (0.032, 0.12, 0.04), "Tan"))
    parts.append(box("Head", (0, -0.105, 0.03), (0.036, 0.02, 0.046), "Accent"))
    grip(parts, 15, 0.085, 0.028, 0.04, "Polymer", y=0.01)
    return parts, (0, -0.115, 0.03), (0, 0.02, -0.06), (0, 0, 0)


# ---- grenades, C4, kit ----

def grenade(kind):
    parts = []
    if kind == "he":
        parts.append(sphere("Body", (0, 0, 0), 0.032, "Olive", scale=(1, 1, 1.1)))
        parts.append(cyl("Fuze", (0, 0, 0.03), (0, 0, 0.05), 0.011, "Steel"))
    elif kind == "flash":
        parts.append(cyl("Body", (0, 0, -0.045), (0, 0, 0.04), 0.024, "Steel"))
        if DETAIL["small_parts"]:
            for i in range(6):
                a = i * math.pi / 3
                parts.append(box(f"Hole{i}", (math.cos(a) * 0.024, math.sin(a) * 0.024, 0.0), (0.008, 0.008, 0.05), "Polymer", bevel=False))
        parts.append(cyl("Fuze", (0, 0, 0.04), (0, 0, 0.058), 0.011, "Steel"))
    elif kind == "smoke":
        parts.append(cyl("Body", (0, 0, -0.055), (0, 0, 0.045), 0.029, "Olive"))
        parts.append(cyl("Band", (0, 0, 0.0), (0, 0, 0.012), 0.0295, "Accent"))
        parts.append(cyl("Fuze", (0, 0, 0.045), (0, 0, 0.063), 0.011, "Steel"))
    elif kind == "molotov":
        parts.append(cyl("Bottle", (0, 0, -0.08), (0, 0, 0.03), 0.032, "Glass"))
        parts.append(cyl("Shoulder", (0, 0, 0.03), (0, 0, 0.06), 0.032, "Glass", r2=0.012))
        parts.append(cyl("Neck", (0, 0, 0.06), (0, 0, 0.1), 0.012, "Glass"))
        parts.append(cyl("Rag", (0, 0, 0.09), (0.01, 0.0, 0.14), 0.014, "Cloth", r2=0.008))
        return parts, (0, 0, 0.14), (0, 0, 0), (0, 0, 0)
    elif kind == "incendiary":
        parts.append(cyl("Body", (0, 0, -0.055), (0, 0, 0.045), 0.028, "Red"))
        parts.append(cyl("Band", (0, 0, -0.01), (0, 0, 0.0), 0.0285, "Metal"))
        parts.append(cyl("Fuze", (0, 0, 0.045), (0, 0, 0.063), 0.011, "Steel"))
    elif kind == "decoy":
        parts.append(cyl("Body", (0, 0, -0.04), (0, 0, 0.035), 0.022, "Tan"))
        parts.append(cyl("Fuze", (0, 0, 0.035), (0, 0, 0.05), 0.01, "Steel"))
    # spoon lever + ring for all but the molotov
    parts.append(box("Spoon", (R * 0.014, 0, 0.03), (0.006, 0.014, 0.06), "Steel", rot=(0, math.radians(12), 0)))
    if DETAIL["small_parts"]:
        parts.append(torus("Ring", (0.016, 0, 0.055), 0.01, 0.0018, "Steel", rot=(math.radians(90), 0, 0)))
    return parts, (0, 0, 0.06), (0, 0, 0), (0, 0, 0)


def c4(p):
    parts = []
    for i, x in enumerate((-0.045, 0.0, 0.045)):
        parts.append(box(f"Brick{i}", (x, 0, 0.0), (0.042, 0.16, 0.04), "Tan"))
    parts.append(box("Tape0", (0, 0.05, 0.0), (0.14, 0.02, 0.043), "Polymer"))
    parts.append(box("Tape1", (0, -0.05, 0.0), (0.14, 0.02, 0.043), "Polymer"))
    parts.append(box("Device", (0, -0.005, 0.03), (0.09, 0.08, 0.02), "Metal"))
    parts.append(box("Screen", (0, -0.025, 0.041), (0.06, 0.022, 0.003), "Screen", bevel=False))
    if DETAIL["small_parts"]:
        for r in range(3):
            for c in range(3):
                parts.append(box(f"Key{r}{c}", (-0.02 + c * 0.02, 0.01 + r * 0.012, 0.042), (0.014, 0.008, 0.004), "Polymer"))
        parts.append(cyl("Antenna", (0.035, 0.03, 0.04), (0.035, 0.03, 0.09), 0.002, "Steel"))
        parts.append(sphere("Led", (0.035, -0.035, 0.042), 0.004, "Red"))
    return parts, (0, 0, 0), (0, 0, 0), (0, 0, 0)


def kit(p):
    parts = []
    parts.append(box("Pouch", (0, 0, 0), (0.12, 0.05, 0.08), "Olive"))
    parts.append(box("Flap", (0, 0.0, 0.042), (0.122, 0.052, 0.01), "Olive"))
    parts.append(box("Cutter", (0.04, -0.02, 0.07), (0.012, 0.01, 0.06), "Red"))
    parts.append(box("Probe", (-0.03, -0.02, 0.07), (0.008, 0.008, 0.07), "Steel"))
    return parts, (0, 0, 0), (0, 0, 0), (0, 0, 0)


# id -> (builder, params). Proportions follow the real archetypes; every model is an original VEXA design.
WEAPONS = {
    # ---------------- pistols ----------------
    "glock": (pistol, dict(slide=(0.18, 0.03, 0.026))),
    "usp": (pistol, dict(slide=(0.19, 0.032, 0.027), suppressor=0.14, hammer=True)),
    "p250": (pistol, dict(slide=(0.175, 0.031, 0.026), slide_mat="Steel", hammer=True)),
    "elite": (pistol, dict(slide=(0.2, 0.031, 0.026), slide_mat="Steel", frame_mat="Metal", hammer=True)),
    "fiveseven": (pistol, dict(slide=(0.2, 0.032, 0.026), frame_mat="Tan")),
    "cz75": (pistol, dict(slide=(0.19, 0.03, 0.026), frame_mat="Metal", hammer=True, mag_ext=False)),
    "deagle": (pistol, dict(slide=(0.26, 0.04, 0.032), slide_mat="Steel", frame_mat="Metal", bore=0.04, bore_r=0.01, grip_len=0.095, hammer=True)),
    "r8": (revolver, {}),
    "tec9": (tec9, {}),
    "taser": (taser, {}),
    # ---------------- smgs ----------------
    "mac10": (rifle, dict(recv=(0.09, 0.15, 0.06, 0.05), bore=0.04, barrel=(0.06, 0.008), hg=("box", 0.0, (0.05, 0.05), "Metal"),
                          muzzle=("none", 0), mag=("none", 0, 0, 0, "Metal"), stock=("wire", 0.2, "Metal"), top="none", grip_angle=8,
                          support_y=-0.06, sights=True)),
    "mp9": (rifle, dict(recv=(0.12, 0.13, 0.055, 0.042), bore=0.04, barrel=(0.06, 0.008), hg=("box", 0.06, (0.044, 0.045), "Polymer"),
                        muzzle=("flash", 0.02), mag=("straight", 0.12, 0.0, 6, "Polymer"), stock=("folded", 0.2, "Polymer"), foregrip=True, body="Polymer")),
    "mp7": (rifle, dict(recv=(0.12, 0.15, 0.06, 0.04), bore=0.042, barrel=(0.07, 0.008), hg=("rail", 0.08, (0.045, 0.045), "Polymer"),
                        muzzle=("flash", 0.03), mag=("straight", 0.11, 0.0, 4, "Polymer"), stock=("collapsible", 0.16, "Polymer"), foregrip=True, body="Polymer")),
    "mp5sd": (rifle, dict(recv=(0.16, 0.12, 0.06, 0.04), bore=0.045, barrel=(0.05, 0.009), hg=("round", 0.12, (0.05, 0.05), "Polymer"),
                          muzzle=("suppressor", 0.2), mag=("curved", 0.17, -0.09, 10, "Metal"), stock=("fixed", 0.24, "Polymer"), top="none")),
    "ump45": (rifle, dict(recv=(0.16, 0.13, 0.065, 0.046), bore=0.045, barrel=(0.1, 0.009), hg=("vent", 0.12, (0.05, 0.055), "Polymer"),
                          mag=("straight", 0.17, -0.08, 2, "Polymer"), stock=("skeleton", 0.25, "Polymer"), body="Polymer")),
    "p90": (bullpup, dict(body_dims=(0.5, 0.08, 0.055), front=0.22, mag_top=True, barrel=(0.03, 0.008), muzzle=("flash", 0.02), thumbhole=True, body="Olive")),
    "bizon": (rifle, dict(recv=(0.16, 0.14, 0.06, 0.045), bore=0.045, barrel=(0.17, 0.009), hg=("wood", 0.12, (0.045, 0.05), "Polymer"),
                          mag=("helical", 0.25, -0.16, 0, "Polymer"), stock=("skeleton", 0.24, "Metal"), top="gas")),
    # ---------------- heavy ----------------
    "nova": (rifle, dict(recv=(0.12, 0.16, 0.06, 0.042), bore=0.045, barrel=(0.42, 0.011), hg=("box", 0.0, (0.04, 0.04), "Polymer"),
                         muzzle=("none", 0), mag=("none", 0, 0, 0, "Metal"), stock=("fixed", 0.3, "Polymer"), pump=0.13, top="rail", support_y=-0.2)),
    "xm1014": (rifle, dict(recv=(0.14, 0.16, 0.065, 0.044), bore=0.045, barrel=(0.42, 0.011), hg=("box", 0.18, (0.05, 0.05), "Polymer"),
                           muzzle=("none", 0), mag=("none", 0, 0, 0, "Metal"), stock=("collapsible", 0.28, "Polymer"), top="rail", pump=0.01)),
    "sawedoff": (rifle, dict(recv=(0.12, 0.12, 0.06, 0.042), bore=0.045, barrel=(0.2, 0.012), hg=("wood", 0.12, (0.05, 0.05), "Wood"),
                             muzzle=("none", 0), mag=("none", 0, 0, 0, "Metal"), stock=("none", 0, "Wood"), grip_mat="Wood", top="none")),
    "mag7": (rifle, dict(recv=(0.12, 0.08, 0.075, 0.05), bore=0.05, barrel=(0.24, 0.012), hg=("box", 0.14, (0.055, 0.06), "Polymer"),
                         muzzle=("none", 0), mag=("straight", 0.09, 0.0, 15, "Metal"), stock=("collapsible", 0.2, "Polymer"), top="rail", body="Polymer")),
    "m249": (rifle, dict(recv=(0.2, 0.18, 0.09, 0.07), bore=0.05, barrel=(0.38, 0.012), hg=("vent", 0.14, (0.06, 0.06), "Polymer"),
                         muzzle=("flash", 0.05), mag=("box", 0, -0.1, 0, "Olive"), stock=("fixed", 0.26, "Polymer"), bipod=True, top="rail")),
    "negev": (rifle, dict(recv=(0.22, 0.16, 0.085, 0.066), bore=0.05, barrel=(0.4, 0.012), hg=("vent", 0.16, (0.058, 0.06), "Polymer"),
                          muzzle=("brake", 0.05), mag=("drum", 0, -0.1, 0, "Olive"), stock=("skeleton", 0.26, "Metal"), bipod=True, top="rail")),
    # ---------------- rifles ----------------
    "galil": (rifle, dict(recv=(0.16, 0.14, 0.065, 0.046), barrel=(0.4, 0.009), hg=("vent", 0.2, (0.05, 0.055), "Polymer"), top="gas",
                          mag=("curved", 0.2, -0.08, 6, "Metal"), stock=("skeleton", 0.26, "Metal"), muzzle=("flash", 0.04))),
    "famas": (bullpup, dict(body_dims=(0.62, 0.075, 0.05), front=0.24, carry=True, barrel=(0.22, 0.009), muzzle=("flash", 0.04),
                            mag_y=0.09, mag_len=0.16, body="Polymer")),
    "ak47": (rifle, dict(recv=(0.16, 0.16, 0.065, 0.046), barrel=(0.42, 0.009), hg=("wood", 0.22, (0.05, 0.055), "Wood"), top="gas",
                         mag=("curved", 0.23, -0.08, 8, "Metal"), stock=("fixed", 0.28, "Wood"), grip_mat="WoodDark", muzzle=("brake", 0.035),
                         drop=0.05)),
    "m4a4": (rifle, dict(recv=(0.12, 0.16, 0.07, 0.044), barrel=(0.37, 0.009), hg=("round", 0.2, (0.056, 0.056), "Polymer"), top="rail",
                         hg_rail=True, mag=("straight", 0.18, -0.065, 7, "Metal"), stock=("collapsible", 0.26, "Polymer"), charging=True,
                         muzzle=("flash", 0.05))),
    "m4a1s": (rifle, dict(recv=(0.12, 0.16, 0.07, 0.044), barrel=(0.27, 0.009), hg=("rail", 0.17, (0.052, 0.055), "Polymer"), top="rail",
                          mag=("straight", 0.18, -0.065, 7, "Metal"), stock=("collapsible", 0.26, "Polymer"), charging=True,
                          muzzle=("suppressor", 0.17))),
    "ssg08": (rifle, dict(recv=(0.12, 0.16, 0.055, 0.04), barrel=(0.5, 0.009), hg=("box", 0.18, (0.045, 0.045), "Olive"),
                          scope=(0.3, 0.017), mag=("straight", 0.06, -0.04, 0, "Polymer"), stock=("thumbhole", 0.3, "Olive"),
                          bolt=True, muzzle=("none", 0), top="none")),
    "sg553": (rifle, dict(recv=(0.16, 0.15, 0.07, 0.048), barrel=(0.3, 0.009), hg=("vent", 0.2, (0.054, 0.058), "Polymer"), top="rail",
                          scope=(0.16, 0.016), mag=("curved", 0.2, -0.08, 6, "Tan"), stock=("skeleton", 0.27, "Polymer"), muzzle=("flash", 0.045))),
    "aug": (bullpup, dict(body_dims=(0.62, 0.085, 0.056), front=0.25, scope=(0.15, 0.02), barrel=(0.25, 0.009), muzzle=("flash", 0.045),
                          foregrip=True, mag_y=0.08, mag_len=0.16, mag_mat="Glass", body="Olive")),
    "awp": (rifle, dict(recv=(0.14, 0.2, 0.065, 0.05), barrel=(0.56, 0.011), hg=("box", 0.24, (0.06, 0.055), "Olive"),
                        scope=(0.36, 0.02), objective=1.5, mag=("straight", 0.07, -0.05, 0, "Metal"), stock=("thumbhole", 0.34, "Olive"),
                        bolt=True, muzzle=("brake", 0.06), top="none", bipod=True)),
    "g3sg1": (rifle, dict(recv=(0.2, 0.18, 0.07, 0.05), barrel=(0.4, 0.01), hg=("vent", 0.22, (0.055, 0.06), "Polymer"), top="rail",
                          scope=(0.3, 0.019), mag=("straight", 0.15, -0.09, 4, "Metal"), stock=("fixed", 0.3, "Polymer"), muzzle=("flash", 0.05))),
    "scar20": (rifle, dict(recv=(0.2, 0.17, 0.075, 0.05), barrel=(0.38, 0.01), hg=("rail", 0.22, (0.056, 0.06), "Tan"), top="rail",
                           scope=(0.3, 0.019), mag=("straight", 0.14, -0.09, 4, "Metal"), stock=("collapsible", 0.27, "Tan"), body="Tan",
                           muzzle=("brake", 0.05), bipod=True)),
    # ---------------- melee & equipment ----------------
    "knife": (knife, {}),
    "he": (lambda p: grenade("he"), {}),
    "flash": (lambda p: grenade("flash"), {}),
    "smoke": (lambda p: grenade("smoke"), {}),
    "molotov": (lambda p: grenade("molotov"), {}),
    "incendiary": (lambda p: grenade("incendiary"), {}),
    "decoy": (lambda p: grenade("decoy"), {}),
    "c4": (c4, {}),
    "kit": (kit, {}),
}


SMALL_ITEMS = ("he", "flash", "smoke", "molotov", "incendiary", "decoy", "kit", "c4")
SIDEARMS = ("glock", "usp", "p250", "elite", "fiveseven", "cz75", "deagle", "r8", "tec9", "taser", "knife")


def texture_size(wid, level):
    """Same texel density everywhere: long guns 4K, sidearms and the knife 2K, grenades and gear 1K (PC);
    a quarter of that on mobile."""
    if wid in SMALL_ITEMS:
        size = 1024
    elif wid in SIDEARMS:
        size = 2048
    else:
        size = 4096
    return size if level == "pc" else max(256, size // 4)


def build(wid):
    fn, params = WEAPONS[wid]
    parts, muzzle_p, support_p, eject_p = fn(params)
    o = join(parts, wid)
    empty("Muzzle", muzzle_p, o)
    empty("Support", support_p, o)
    empty("Eject", eject_p, o)
    return o


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    ap.add_argument("--preview", default="")
    ap.add_argument("--textures", action="store_true", help="bake PBR textures (4K for long guns: a few minutes each)")
    ap.add_argument("--levels", default="pc,mobile")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    ids = [w for w in args.only.split(",") if w] or list(WEAPONS.keys())
    report = []
    for level, sub in (("pc", "Weapons"), ("mobile", os.path.join("Weapons", "Mobile"))):
        if level not in args.levels.split(","):
            continue
        set_detail(level)
        for wid in ids:
            reset_scene()
            o = build(wid)
            if args.textures:
                import vexa_textures
                size = texture_size(wid, level)
                small = wid in SMALL_ITEMS
                vexa_textures.bake_model(o, os.path.join(args.out, sub, "Textures"), wid, size, normal=level == "pc",
                                         scale_hint=0.6 if small else 1.0, samples=8, jpeg=level == "pc")
            export_fbx(os.path.join(args.out, sub, wid + ".fbx"), [o])
            report.append((level, wid, tri_count(o)))
            print(f"{level:7} {wid:12} {tri_count(o):6} tris", flush=True)
    for level, wid, tris in report:
        print(f"{level:7} {wid:12} {tris:6} tris")
    if args.preview:
        import vexa_preview
        vexa_preview.weapon_sheet(ids, args.preview)


if __name__ == "__main__":
    main()
