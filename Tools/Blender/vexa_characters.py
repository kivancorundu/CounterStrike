"""
VEXA characters: two original factions, rigged, with animation clips, plus first-person arms.

    python vexa_characters.py --out ../../Game/Assets/Vexa/Resources/Models [--preview sheet.png]

    Characters/<faction>.fbx         third-person body (armature + mesh + clips)
    Characters/Mobile/<faction>.fbx  low-poly version
    Arms/<faction>.fbx               first-person forearms: objects "RightArm" and "LeftArm", hand at each origin

Factions:  "akinci" (attackers, T)  sand jacket, balaclava and cap, amber details
           "muhafiz" (defenders, CT) navy uniform, helmet with visor, plate carrier, blue details

The rest pose IS the aiming pose the server's hitboxes assume (Core/Combat/Hitboxes.cs): 1.83 m tall,
head center at ~1.69 m, arms forward holding a rifle. Skinning is rigid (each part follows one bone),
which suits the stylized low-poly look and keeps hitboxes and visuals in agreement.
"""
import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector, Euler
from vexa_common import (DETAIL, set_detail, reset_scene, box, cyl, sphere, join, apply_modifiers, custom_mat, mat, tri_count)

FPS = 24

# bone: (head, tail, parent) in Blender space (forward -Y, right -X, up +Z)
BONES = {
    "Hips": ((0, 0, 0.96), (0, 0, 1.06), None),
    "Spine": ((0, 0, 1.06), (0, 0, 1.22), "Hips"),
    "Chest": ((0, 0, 1.22), (0, 0, 1.45), "Spine"),
    "Neck": ((0, -0.01, 1.48), (0, -0.02, 1.58), "Chest"),
    "Head": ((0, -0.02, 1.58), (0, -0.03, 1.82), "Neck"),
    "RightUpperArm": ((-0.2, 0.0, 1.43), (-0.24, -0.12, 1.2), "Chest"),
    "RightLowerArm": ((-0.24, -0.12, 1.2), (-0.06, -0.34, 1.19), "RightUpperArm"),
    "RightHand": ((-0.06, -0.34, 1.19), (-0.03, -0.42, 1.19), "RightLowerArm"),
    "LeftUpperArm": ((0.2, 0.0, 1.43), (0.19, -0.24, 1.23), "Chest"),
    "LeftLowerArm": ((0.19, -0.24, 1.23), (0.03, -0.53, 1.24), "LeftUpperArm"),
    "LeftHand": ((0.03, -0.53, 1.24), (0.0, -0.61, 1.24), "LeftLowerArm"),
    "RightUpperLeg": ((-0.1, 0, 0.94), (-0.1, -0.01, 0.52), "Hips"),
    "RightLowerLeg": ((-0.1, -0.01, 0.52), (-0.1, 0.02, 0.1), "RightUpperLeg"),
    "RightFoot": ((-0.1, 0.02, 0.1), (-0.1, -0.12, 0.03), "RightLowerLeg"),
    "LeftUpperLeg": ((0.1, 0, 0.94), (0.1, -0.01, 0.52), "Hips"),
    "LeftLowerLeg": ((0.1, -0.01, 0.52), (0.1, 0.02, 0.1), "LeftUpperLeg"),
    "LeftFoot": ((0.1, 0.02, 0.1), (0.1, -0.12, 0.03), "LeftLowerLeg"),
}

FACTIONS = {
    "akinci": dict(
        top=(0.52, 0.42, 0.29), pants=(0.25, 0.25, 0.2), boots=(0.22, 0.15, 0.09), gear=(0.18, 0.17, 0.13),
        gloves=(0.1, 0.1, 0.1), mask=(0.09, 0.09, 0.1), accent=(0.95, 0.64, 0.23), headgear="cap", vest=False),
    "muhafiz": dict(
        top=(0.12, 0.15, 0.22), pants=(0.15, 0.18, 0.25), boots=(0.07, 0.07, 0.08), gear=(0.2, 0.23, 0.28),
        gloves=(0.08, 0.08, 0.09), mask=(0.55, 0.42, 0.33), accent=(0.35, 0.69, 1.0), headgear="helmet", vest=True),
}


def tag(o, bone):
    """Rigid skinning: every vertex of this part follows one bone."""
    vg = o.vertex_groups.new(name=bone)
    vg.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    return o


def limb(name, a, b, r1, r2, material, bone, parts):
    parts.append(tag(_applied(cyl(name, a, b, r1, material, r2=r2)), bone))
    parts.append(tag(_applied(sphere(name + "J", a, r1 * 0.98, material, segments=max(8, DETAIL["cyl"] // 2 + 4))), bone))


def _applied(o):
    apply_modifiers(o)
    return o


def rounded(name, center, size, material, bone, parts, bevel=None, rot=(0, 0, 0)):
    o = box(name, center, size, material, bevel_width=bevel if bevel is not None else min(size) * 0.3, rot=rot)
    o.modifiers["Bevel"].segments = DETAIL["segments"] + 1
    apply_modifiers(o)
    parts.append(tag(o, bone))
    return o


def build_body(faction):
    f = FACTIONS[faction]
    p = faction.capitalize()
    M = lambda key, metal=0.0, rough=0.8: custom_mat(f"{p}_{key}", f[key], metal, rough)
    top, pants, boots, gear, gloves, mask, accent = M("top"), M("pants"), M("boots"), M("gear"), M("gloves"), M("mask"), M("accent", rough=0.5)
    parts = []
    # ---- legs ----
    for s, side in ((-1, "Right"), (1, "Left")):
        x = 0.1 * s
        limb(f"{side}Thigh", (x, 0, 0.92), (x, -0.01, 0.52), 0.088, 0.066, pants, f"{side}UpperLeg", parts)
        limb(f"{side}Shin", (x, -0.01, 0.52), (x, 0.02, 0.13), 0.064, 0.05, pants, f"{side}LowerLeg", parts)
        rounded(f"{side}Boot", (x, -0.03, 0.065), (0.11, 0.27, 0.13), boots, f"{side}Foot", parts, 0.03)
        rounded(f"{side}BootTop", (x, 0.02, 0.15), (0.115, 0.13, 0.1), boots, f"{side}LowerLeg", parts, 0.03)
        if f["vest"]:
            rounded(f"{side}KneePad", (x, -0.07, 0.52), (0.1, 0.05, 0.12), gear, f"{side}LowerLeg", parts, 0.02)
        else:
            rounded(f"{side}Pocket", (x + 0.06 * s, -0.01, 0.72), (0.03, 0.12, 0.13), pants, f"{side}UpperLeg", parts, 0.012)
    # ---- torso ----
    rounded("Pelvis", (0, 0.0, 0.98), (0.34, 0.22, 0.2), pants, "Hips", parts, 0.06)
    rounded("Belt", (0, 0.0, 1.06), (0.35, 0.225, 0.05), gear, "Hips", parts, 0.015)
    rounded("Belly", (0, 0.0, 1.16), (0.33, 0.21, 0.2), top, "Spine", parts, 0.07)
    rounded("Torso", (0, 0.0, 1.345), (0.4, 0.24, 0.31), top, "Chest", parts, 0.08)
    if f["vest"]:
        rounded("Plate", (0, -0.025, 1.28), (0.37, 0.24, 0.32), gear, "Chest", parts, 0.03)
        for i, x in enumerate((-0.1, 0.0, 0.1)):
            rounded(f"MagPouch{i}", (x, -0.15, 1.2), (0.08, 0.05, 0.12), gear, "Chest", parts, 0.012)
        rounded("Radio", (0.14, 0.13, 1.3), (0.06, 0.05, 0.16), mat("Polymer"), "Chest", parts, 0.01)
        rounded("Patch", (0.21, -0.02, 1.38), (0.005, 0.07, 0.05), accent, "Chest", parts, 0.002)
        rounded("Backpack", (0, 0.16, 1.3), (0.3, 0.1, 0.3), gear, "Chest", parts, 0.03)
    else:
        rounded("Collar", (0, 0.0, 1.47), (0.24, 0.18, 0.06), top, "Chest", parts, 0.025)
        rounded("ChestRig", (0, -0.13, 1.2), (0.3, 0.05, 0.12), gear, "Chest", parts, 0.015)
        rounded("Strap", (0.0, -0.125, 1.32), (0.05, 0.02, 0.33), gear, "Chest", parts, 0.008, rot=(0, math.radians(35), 0))
        rounded("Stripe", (0.205, -0.0, 1.4), (0.005, 0.12, 0.025), accent, "Chest", parts, 0.002)
        rounded("SlingBag", (-0.16, 0.13, 1.08), (0.08, 0.1, 0.16), gear, "Hips", parts, 0.02)
    # ---- arms (aiming pose) ----
    for side in ("Right", "Left"):
        ua, la, hand = BONES[f"{side}UpperArm"], BONES[f"{side}LowerArm"], BONES[f"{side}Hand"]
        limb(f"{side}Upper", ua[0], ua[1], 0.062, 0.052, top, f"{side}UpperArm", parts)
        limb(f"{side}Fore", la[0], la[1], 0.05, 0.042, top, f"{side}LowerArm", parts)
        h = Vector(hand[0]) + (Vector(hand[1]) - Vector(hand[0])) * 0.4
        rounded(f"{side}Glove", tuple(h), (0.075, 0.1, 0.05), gloves, f"{side}Hand", parts, 0.02)
        rounded(f"{side}Shoulder", (ua[0][0], 0.0, 1.42), (0.13, 0.15, 0.1), gear if f["vest"] else top, "Chest", parts, 0.04)
    # ---- neck & head (head center ~1.69 m, matches the head hitbox) ----
    limb("NeckPart", (0, -0.01, 1.49), (0, -0.02, 1.6), 0.068, 0.06, mask if not f["vest"] else top, "Neck", parts)
    head = _applied(sphere("HeadBall", (0, -0.035, 1.69), 0.11, mask, scale=(0.9, 0.98, 1.05)))
    parts.append(tag(head, "Head"))
    # jaw + chin, so the neck doesn't read as a stalk
    jaw = _applied(sphere("Jaw", (0, -0.055, 1.615), 0.078, mask, scale=(1.0, 1.05, 0.85)))
    parts.append(tag(jaw, "Head"))
    if f["vest"]:
        rounded("Collar", (0, 0.0, 1.49), (0.25, 0.19, 0.06), top, "Chest", parts, 0.025)
    if f["headgear"] == "helmet":
        helmet = _applied(sphere("Helmet", (0, -0.03, 1.72), 0.128, gear, scale=(0.95, 1.05, 0.85)))
        parts.append(tag(helmet, "Head"))
        rounded("Visor", (0, -0.135, 1.69), (0.17, 0.03, 0.07), mat("Lens"), "Head", parts, 0.012)
        rounded("HelmetRail", (0.12, -0.03, 1.7), (0.012, 0.12, 0.025), mat("Polymer"), "Head", parts, 0.004)
        rounded("NVGMount", (0, -0.13, 1.79), (0.05, 0.03, 0.04), mat("Polymer"), "Head", parts, 0.008)
    else:
        cap = _applied(sphere("Cap", (0, -0.025, 1.745), 0.112, gear, scale=(0.95, 1.0, 0.6)))
        parts.append(tag(cap, "Head"))
        rounded("Brim", (0, -0.135, 1.75), (0.15, 0.09, 0.012), gear, "Head", parts, 0.004, rot=(math.radians(-8), 0, 0))
        rounded("Goggles", (0, -0.12, 1.705), (0.17, 0.03, 0.045), mat("Lens"), "Head", parts, 0.012)
        rounded("GoggleStrap", (0, -0.03, 1.705), (0.205, 0.17, 0.02), accent, "Head", parts, 0.008)
    body = join(parts, faction + "_body")
    return body


def build_armature(name):
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for b, (h, t, parent) in BONES.items():
        e = arm.edit_bones.new(b)
        e.head, e.tail = h, t
        e.roll = 0
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

def build_character(faction):
    armature = build_armature(faction)
    body = build_body(faction)
    body.parent = armature
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = armature
    return armature, body


def export_character(path, armature, body):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
        bake_space_transform=False, object_types={"MESH", "ARMATURE"}, mesh_smooth_type="FACE", use_mesh_modifiers=False,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0)


def build_arms(faction):
    """First-person forearms. Each object's origin is the palm; the forearm runs back toward the camera."""
    f = FACTIONS[faction]
    p = faction.capitalize()
    top = custom_mat(f"{p}_top", f["top"], 0, 0.8)
    gloves = custom_mat(f"{p}_gloves", f["gloves"], 0, 0.8)
    accent = custom_mat(f"{p}_accent", f["accent"], 0, 0.5)
    objs = []
    for side, s in (("RightArm", -1), ("LeftArm", 1)):
        parts = []
        # forearm sleeve runs back (+Y), outward and down from the hand
        a, b = (0.0, 0.03, -0.01), (0.07 * s * -1 if side == "RightArm" else 0.05, 0.36, -0.12)
        parts.append(cyl(side + "Sleeve", a, b, 0.042, top, r2=0.05))
        parts.append(cyl(side + "Cuff", (0, 0.035, -0.012), (0, 0.06, -0.017), 0.046, accent))
        g = box(side + "Glove", (0, -0.015, 0.0), (0.075, 0.1, 0.045), gloves, bevel_width=0.016)
        g.modifiers["Bevel"].segments = 3
        parts.append(g)
        parts.append(box(side + "Thumb", (0.03 * -s, -0.04, 0.02), (0.022, 0.06, 0.022), gloves, bevel_width=0.008))
        o = join(parts, side)
        objs.append(o)
    return objs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", default="")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    for level, sub in (("pc", "Characters"), ("mobile", os.path.join("Characters", "Mobile"))):
        set_detail(level)
        for faction in FACTIONS:
            reset_scene()
            arm, body = build_character(faction)
            make_clips(arm)
            export_character(os.path.join(args.out, sub, faction + ".fbx"), arm, body)
            print(f"{level:7} {faction:8} {tri_count(body):6} tris, {len(bpy.data.actions)} clips")
    set_detail("pc")
    from vexa_common import export_fbx
    for faction in FACTIONS:
        reset_scene()
        objs = build_arms(faction)
        export_fbx(os.path.join(args.out, "Arms", faction + ".fbx"), objs)
    if args.preview:
        import vexa_preview
        def make(fac):
            def fn():
                a, b = build_character(fac)
                return a
            return fn
        vexa_preview.character_sheet([(f, make(f)) for f in FACTIONS], args.preview)


if __name__ == "__main__":
    main()
