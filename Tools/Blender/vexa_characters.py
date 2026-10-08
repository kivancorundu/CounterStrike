"""
VEXA characters (v3): realistic bodies with fitted clothing and gear, rigged, animated, with secondary motion,
plus first-person arms.

    python vexa_characters.py --out ../../Game/Assets/Vexa/Resources/Models [--textures] [--preview sheet.png]

    Characters/<faction>.fbx          PC: ~80k triangles, 4K textures (JPEG albedo/normal + PNG mask)
    Characters/Mobile/<faction>.fbx   mobile: ~10k triangles, 1K textures
    Arms/<faction>.fbx                first-person forearms: objects "RightArm" and "LeftArm", the grip point at each origin

Pipeline (see vexa_human, vexa_outfit, vexa_factions): MakeHuman CC0 body shaped into a heavily built soldier and
posed into the aiming pose -> clothing and gear built on it as a high-poly source (~0.5M triangles) -> game meshes
decimated from it -> weights transferred from the automatically weighted body -> every texture baked from the
high-poly source onto the game mesh (cloth folds, seams, webbing, wear and dirt end up in the maps).

The rest pose IS the aiming pose the server's hitboxes assume (Core/Combat/Hitboxes.cs): ~1.83 m tall with boots,
head center ~1.69 m, arms forward holding a rifle. Bones: the 17 game bones (names below), "RightGrip"/"LeftGrip"
(weapon attachment points, no skin) and "Jiggle_*" bones for loose parts, swung by Client/Art/SpringBones.cs.
"""
import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import numpy as np
from mathutils import Euler, Vector

from vexa_common import reset_scene, tri_count

FPS = 24
FACTIONS = ("akinci", "muhafiz")
LOD = {"pc": dict(tris=80000, tex=4096, jpeg=True, normal=True), "mobile": dict(tris=10000, tex=1024, jpeg=False, normal=False)}
ARM_TRIS = 16000
ARM_TEX = 2048


def build_armature(name, layout):
    """layout: bone -> (head, tail, parent)."""
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for b, (h, t, parent) in layout.items():
        e = arm.edit_bones.new(b)
        e.head, e.tail = Vector(h), Vector(t)
        e.roll = 0
        e.use_deform = not b.endswith("Grip")
        if parent:
            e.parent = eb[parent]
            e.use_connect = False
        eb[b] = e
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in obj.pose.bones:
        pb.rotation_mode = "XYZ"
    return obj


# ---------------------------------------------------------------- animation

def _key(arm, frame, rots, hips_offset=(0, 0, 0)):
    """rots: bone -> (x, y, z) degrees in the bone's local space."""
    for pb in arm.pose.bones:
        r = rots.get(pb.name, (0, 0, 0))
        pb.rotation_euler = Euler([math.radians(v) for v in r], "XYZ")
        pb.keyframe_insert("rotation_euler", frame=frame)
    hips = arm.pose.bones["Hips"]
    hips.location = hips_offset
    hips.keyframe_insert("location", frame=frame)


def _action(arm, name, keys, loop=True):
    """keys: list of (time seconds, rots, hips_offset)."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    for t, rots, off in keys:
        _key(arm, 1 + round(t * FPS), rots, off)
    if loop:
        t, rots, off = keys[0]
        _key(arm, 1 + round(keys[-1][0] * FPS + (keys[1][0] - keys[0][0]) * FPS), rots, off)
    return act


def leg_cycle(swing, knee, bob, lean, duration, crouch=0.0):
    """Two-beat walk/run cycle. Bone-local X rotation swings legs forward (+) / back (-)."""
    keys = []
    crouch_thigh, crouch_knee, crouch_drop = 70 * crouch, -110 * crouch, -0.36 * crouch
    for i, ph in enumerate((0, 0.25, 0.5, 0.75)):
        s = math.sin(ph * 2 * math.pi)
        c = math.cos(ph * 2 * math.pi)
        rots = {
            "RightUpperLeg": (crouch_thigh + swing * s, 0, 0),
            "LeftUpperLeg": (crouch_thigh - swing * s, 0, 0),
            "RightLowerLeg": (crouch_knee - knee * max(0, -c) - 8, 0, 0),
            "LeftLowerLeg": (crouch_knee - knee * max(0, c) - 8, 0, 0),
            "RightFoot": (-crouch_thigh * 0.5, 0, 0), "LeftFoot": (-crouch_thigh * 0.5, 0, 0),
            "Spine": (lean + 10 * crouch, 4 * s, 0),
            "Chest": (0, -6 * s * (swing / 35), 0),
        }
        drop = crouch_drop - bob * abs(math.cos(ph * 2 * math.pi))
        keys.append((ph * duration, rots, (0, drop, 0)))
    return keys


def make_clips(arm):
    clips = []
    # idle: breathing
    clips.append(_action(arm, "idle", [
        (0.0, {"Chest": (0, 0, 0), "RightLowerLeg": (-6, 0, 0), "LeftLowerLeg": (-6, 0, 0)}, (0, 0, 0)),
        (1.0, {"Chest": (1.5, 0, 0), "Spine": (1, 0, 0), "RightLowerLeg": (-6, 0, 0), "LeftLowerLeg": (-6, 0, 0)}, (0, -0.008, 0)),
    ]))
    clips.append(_action(arm, "walk", leg_cycle(22, 25, 0.015, 2, 0.9)))
    clips.append(_action(arm, "run", leg_cycle(38, 70, 0.04, 6, 0.62)))
    crouch_rots = {"RightUpperLeg": (70, 0, 0), "LeftUpperLeg": (70, 0, 0), "RightLowerLeg": (-110, 0, 0), "LeftLowerLeg": (-110, 0, 0),
                   "RightFoot": (-35, 0, 0), "LeftFoot": (-35, 0, 0), "Spine": (10, 0, 0)}
    clips.append(_action(arm, "crouch", [(0.0, crouch_rots, (0, -0.36, 0)), (1.0, dict(crouch_rots, Chest=(1.5, 0, 0)), (0, -0.365, 0))]))
    clips.append(_action(arm, "crouch_walk", leg_cycle(20, 20, 0.01, 2, 1.0, crouch=1.0)))
    jump = {"RightUpperLeg": (45, 0, 0), "LeftUpperLeg": (25, 0, 0), "RightLowerLeg": (-80, 0, 0), "LeftLowerLeg": (-60, 0, 0), "Spine": (6, 0, 0)}
    clips.append(_action(arm, "jump", [(0.0, jump, (0, 0, 0)), (0.5, jump, (0, 0, 0))], loop=False))
    # death: knees give, falls backwards
    clips.append(_action(arm, "death", [
        (0.0, {}, (0, 0, 0)),
        (0.25, {"RightLowerLeg": (-60, 0, 0), "LeftLowerLeg": (-50, 0, 0), "RightUpperLeg": (40, 0, 0), "LeftUpperLeg": (30, 0, 0), "Spine": (-10, 0, 0)}, (0, -0.35, 0)),
        (0.7, {"Hips": (-80, 0, 0), "RightUpperLeg": (70, 0, 0), "LeftUpperLeg": (60, 0, 0), "RightLowerLeg": (-20, 0, 0), "LeftLowerLeg": (-30, 0, 0),
               "Spine": (-15, 0, 0), "Chest": (-10, 0, 0), "Head": (-20, 0, 15),
               "RightUpperArm": (-30, 0, -30), "LeftUpperArm": (-30, 0, 30)}, (0, -0.8, 0.25)),
        (1.0, {"Hips": (-90, 0, 0), "RightUpperLeg": (75, 0, 0), "LeftUpperLeg": (65, 0, 0), "RightLowerLeg": (-15, 0, 0), "LeftLowerLeg": (-25, 0, 0),
               "Spine": (-10, 0, 0), "Head": (-15, 0, 20), "RightUpperArm": (-40, 0, -40), "LeftUpperArm": (-40, 0, 40)}, (0, -0.84, 0.3)),
    ], loop=False))
    arm.animation_data.action = None
    return clips



# ---------------------------------------------------------------- assembly

def _select(objs, active):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def _apply_mods(o):
    _select([o], o)
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def duplicate(o, name):
    c = o.copy()
    c.data = o.data.copy()
    c.name = name
    c.data.name = name
    bpy.context.collection.objects.link(c)
    return c


def decimate(o, tris):
    """Collapse decimation to ~tris triangles; vertices in the "detail" group (small hard parts) are protected."""
    cur = tri_count(o)
    if cur > tris:
        tri = o.modifiers.new("Tri", "TRIANGULATE")
        d = o.modifiers.new("Decimate", "DECIMATE")
        d.ratio = tris / cur
        d.use_collapse_triangulate = True
        if o.vertex_groups.get("detail") is not None:
            d.vertex_group = "detail"
            d.invert_vertex_group = True
            d.vertex_group_factor = 1.0
        _apply_mods(o)
    if o.vertex_groups.get("detail") is not None:
        o.vertex_groups.remove(o.vertex_groups["detail"])
    for p in o.data.polygons:
        p.use_smooth = True
    return o


def fix_layers(low, gap=0.0025):
    """Decimation moves surfaces by a few millimeters, enough for stacked layers (shirt / vest / pouch) to cut into
    each other. Each part (by its "layer" index) is pushed back above everything worn below it."""
    import bmesh
    from mathutils.bvhtree import BVHTree
    a = low.data.attributes.get("layer")
    if a is None:
        return
    layer = np.zeros(len(low.data.vertices), dtype=np.int32)
    a.data.foreach_get("value", layer)
    bm = bmesh.new()
    bm.from_mesh(low.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    face_layer = np.array([min(layer[v.index] for v in f.verts) for f in bm.faces])
    verts = [v.co.copy() for v in bm.verts]
    order = sorted(set(layer.tolist()))
    for L in order[1:]:
        below = [f for f in bm.faces if face_layer[f.index] < L]
        if not below:
            continue
        polys = [[v.index for v in f.verts] for f in below]
        tree = BVHTree.FromPolygons(verts, polys)
        for v in bm.verts:
            if layer[v.index] != L:
                continue
            loc, nrm, _, dist = tree.find_nearest(v.co, 0.03)
            if loc is None:
                continue
            h = (v.co - loc).dot(nrm)
            if h < gap and h > -0.02:
                v.co = v.co + nrm * (gap - h)
        verts = [v.co.copy() for v in bm.verts]
    bm.to_mesh(low.data)
    bm.free()


def weight_body(full, arm):
    """Automatic (heat) weights of the full posed body for the deforming game bones."""
    for g in list(full.vertex_groups):
        full.vertex_groups.remove(g)
    saved = {}
    for b in arm.data.bones:
        saved[b.name] = b.use_deform
        if b.name.startswith("Jiggle_") or b.name.endswith("Grip"):
            b.use_deform = False
    _select([full, arm], arm)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for b in arm.data.bones:
        b.use_deform = saved[b.name]
    full.parent = None
    for m in list(full.modifiers):
        full.modifiers.remove(m)


def skin_low(low, full, arm):
    """Weights for a game mesh: transferred from the weighted body (nearest surface), then the Jiggle_* groups the
    loose parts carry take their share."""
    jig = {g.name: g.index for g in low.vertex_groups if g.name.startswith("Jiggle_")}
    jw = np.zeros((len(low.data.vertices), max(1, len(jig))))
    names = list(jig)
    for v in low.data.vertices:
        for g in v.groups:
            gn = low.vertex_groups[g.group].name
            if gn in jig:
                jw[v.index, names.index(gn)] = g.weight
    for g in [g for g in low.vertex_groups if not g.name.startswith("Jiggle_")]:
        low.vertex_groups.remove(g)
    dt = low.modifiers.new("DT", "DATA_TRANSFER")
    dt.object = full
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    dt.layers_vgroup_select_src = "ALL"
    dt.layers_vgroup_select_dst = "NAME"
    _select([low], low)
    bpy.ops.object.datalayout_transfer(modifier=dt.name)
    bpy.ops.object.modifier_apply(modifier=dt.name)
    # loose parts: the jiggle bone gets its weight, the body bones share the rest
    if jig:
        body_groups = [g for g in low.vertex_groups if not g.name.startswith("Jiggle_")]
        for v in low.data.vertices:
            wj = jw[v.index].sum()
            if wj <= 0:
                continue
            k = max(0.0, 1.0 - wj)
            for g in v.groups:
                gn = low.vertex_groups[g.group].name
                if not gn.startswith("Jiggle_"):
                    g.weight *= k
    low.parent = arm
    m = low.modifiers.new("Armature", "ARMATURE")
    m.object = arm
    # limit to 4 influences (Unity's default skin quality)
    _select([low], low)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)


def export_character(path, armature, body):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    _select([armature, body], armature)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
        bake_space_transform=False, object_types={"MESH", "ARMATURE"}, mesh_smooth_type="FACE", use_mesh_modifiers=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0)


# first-person forearm directions (wrist -> elbow) in weapon space: down and back, out of the bottom corners of
# the screen (the third-person pose holds the rifle at the chest; the view model holds it just under the eye)
FP_FOREARM = {"Right": (-0.32, 0.6, -0.73), "Left": (0.42, 0.42, -0.8)}


def bend_forearm(o, wrist, elbow, target_dir, blend=0.07):
    """Swings the forearm (and the bit of upper arm) about the wrist so it points along target_dir; the hand
    stays where it grips the weapon. The rotation fades in over `blend` meters above the wrist."""
    from mathutils import Quaternion
    w = Vector(wrist)
    cur = (Vector(elbow) - w).normalized()
    q = cur.rotation_difference(Vector(target_dir).normalized())
    axis, angle = q.axis, q.angle
    for v in o.data.vertices:
        t = (v.co - w).dot(cur)
        if t <= 0:
            continue
        k = min(1.0, t / blend)
        k = k * k * (3 - 2 * k)
        v.co = w + Quaternion(axis, angle * k) @ (v.co - w)


def build_arms(faction, high, posed, out, textures):
    """First-person forearms cut from the high-poly character: sleeve, glove (and skin) from above the elbow to the
    fingertips. Each object's origin is the grip point of that hand; forward is -Y like the weapons."""
    import vexa_textures
    from vexa_common import export_fbx
    arms = []
    attr = high.data.attributes.get("arm")
    arm_face = np.zeros(len(high.data.polygons), dtype=np.int32)
    if attr is not None:
        attr.data.foreach_get("value", arm_face)
    centers = np.zeros(len(high.data.polygons) * 3)
    high.data.polygons.foreach_get("center", centers)
    centers = centers.reshape(-1, 3)
    for side, name in (("Right", "RightArm"), ("Left", "LeftArm")):
        sh, el = posed[side + "UpperArm"][0], posed[side + "LowerArm"][0]
        ht = posed[side + "Hand"][1]
        a = el + (sh - el).normalized() * 0.045
        b = ht + (ht - posed[side + "Hand"][0]).normalized() * 0.07
        ab = np.array((b - a)[:])
        t = np.clip(((centers - np.array(a[:])) @ ab) / (ab @ ab), 0, 1)
        d = np.linalg.norm(centers - (np.array(a[:]) + t[:, None] * ab), axis=1)
        keep = (arm_face == 1) & (d < 0.11) & (((centers - np.array(a[:])) @ ab) > 0)
        src = duplicate(high, name + "_high")
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(src.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep[f.index]], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(src.data)
        bm.free()
        bend_forearm(src, posed[side + "Hand"][0], el, FP_FOREARM[side])
        low = decimate(duplicate(src, name), ARM_TRIS)
        for vg in list(low.vertex_groups):
            low.vertex_groups.remove(vg)
        # the arm's islands (sleeve, glove, skin) refill their own texture
        import vexa_uv
        vexa_uv.pack(low)
        if textures:
            vexa_textures.bake_model(low, os.path.join(out, "Arms", "Textures"), f"{faction}_{name}", ARM_TEX, normal=True,
                                     scale_hint=1.6, samples=6, source=src, extrusion=0.008, jpeg=True, keep_uv=True)
        grip = posed[side + "Grip"][0]
        low.data.transform(__import__("mathutils").Matrix.Translation(-grip))
        low.location = (0, 0, 0)
        bpy.data.objects.remove(src)
        arms.append(low)
    for o in arms:
        for uvl in [u for u in o.data.uv_layers if u.name != "atlas"]:
            o.data.uv_layers.remove(uvl)
    export_fbx(os.path.join(out, "Arms", faction + ".fbx"), arms)
    return arms


def build_character(faction, out, textures):
    import vexa_factions
    import vexa_textures
    reset_scene()
    high, layout, full, posed, jiggles = vexa_factions.build(faction)
    layout = dict(layout)
    for side in ("Right", "Left"):
        g = posed[side + "Grip"][0]
        layout[side + "Grip"] = (tuple(g), tuple(g + Vector((0, -0.06, 0))), side + "Hand")
    for name, root, tip, parent, _ in jiggles:
        layout[name] = (tuple(root), tuple(tip), parent)
    arm = build_armature(faction, layout)
    weight_body(full, arm)
    report = {}
    lows = {}
    for level, cfg in LOD.items():
        low = decimate(duplicate(high, faction + "_body"), cfg["tris"])
        fix_layers(low)
        skin_low(low, full, arm)
        if textures:
            sub = "Characters" if level == "pc" else os.path.join("Characters", "Mobile")
            vexa_textures.bake_model(low, os.path.join(out, sub, "Textures"), faction, cfg["tex"], normal=cfg["normal"],
                                     scale_hint=1.6, samples=6 if level == "pc" else 4, source=high, extrusion=0.01,
                                     jpeg=cfg["jpeg"], keep_uv=True)
        lows[level] = low
        report[level] = tri_count(low)
    arms = build_arms(faction, high, posed, out, textures)
    bpy.data.objects.remove(high)
    full.hide_viewport = True
    make_clips(arm)
    for level, low in lows.items():
        sub = "Characters" if level == "pc" else os.path.join("Characters", "Mobile")
        # unlink the other LOD so it isn't exported
        export_character(os.path.join(out, sub, faction + ".fbx"), arm, low)
    print(f"{faction}: pc {report['pc']} tris, mobile {report['mobile']} tris, {len(jiggles)} jiggle bones, "
          f"{len(bpy.data.actions)} clips, arms {[tri_count(a) for a in arms]}", flush=True)
    return arm, lows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", default="", help="render a preview sheet of the exported, textured characters")
    ap.add_argument("--textures", action="store_true", help="bake PBR textures (slow: 4K on the CPU)")
    ap.add_argument("--only", default="")
    ap.add_argument("--arms-only", action="store_true", help="rebuild only the first-person arms")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    factions = [f for f in args.only.split(",") if f] or list(FACTIONS)
    for faction in factions:
        if args.arms_only:
            import vexa_factions
            reset_scene()
            high, layout, full, posed, jiggles = vexa_factions.build(faction)
            build_arms(faction, high, posed, args.out, args.textures)
            continue
        build_character(faction, args.out, args.textures)
    if args.preview and not args.arms_only:
        import vexa_preview
        vexa_preview.textured_character_sheet(os.path.join(args.out, "Characters"), factions, args.preview)


if __name__ == "__main__":
    main()
