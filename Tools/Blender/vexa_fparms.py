"""
First-person arms (v2): made for the view model, not cut from the third-person character.

    python vexa_fparms.py --out ../../Game/Assets/Vexa/Resources/Models [--textures] [--only akinci] [--holds knife]

    Arms/<faction>_<hold>.fbx    objects "RightArm" and "LeftArm" in weapon space, each with its origin at the point
                                 the game puts it on (RightArm: the weapon's grip origin, LeftArm: its "Support" point)
    Arms/Textures/<faction>_albedo / _mask / _normal   shared by both arms and every hold (same mesh, same UVs)

How: the MakeHuman body (vexa_human) is skinned to a rig with finger bones; only the arms are kept, subdivided and
"dressed" by pushing the surface out where the sleeve and the glove are (no separate garment shells, so no open
edges or gaps up close). For each hold the camera is put where the game's view model has it (Client/ViewModel.cs
base offsets), the arms are solved with two-bone IK from the body's real shoulders to the hand targets on the
weapon, the hands are turned into the hold's frame and the fingers curled; the pose is then baked into the mesh.
Blender space: forward -Y, right -X, up +Z, meters.
"""
import argparse
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bmesh  # noqa: E402 (after bpy)
import numpy as np
from mathutils import Matrix, Quaternion, Vector

import vexa_human as H
from vexa_common import export_fbx, reset_scene, tri_count

FACTIONS = ("akinci", "muhafiz")
ARM_TEX = 2048

M_U2B = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0)))  # Unity camera space -> Blender


def unity_pos(v):
    return M_U2B @ Vector(v)


def unity_rot(ex, ey, ez):
    """Unity Euler (degrees, applied Z, X, Y) as a Blender-space rotation matrix."""
    qx = Quaternion((1, 0, 0), math.radians(ex))
    qy = Quaternion((0, 1, 0), math.radians(ey))
    qz = Quaternion((0, 0, 1), math.radians(ez))
    return M_U2B @ (qy @ qx @ qz).to_matrix() @ M_U2B.inverted()


# Holds. view: the weapon pivot in camera space, as Client/ViewModel.cs places it (Unity units and Euler degrees).
# Hands: grip = the point on the weapon (weapon space) the hand's grip point goes to; anchor = the weapon point the
# game attaches the arm to (right: the origin; left: the weapon's "Support" empty, nominal position); across = knuckle
# axis index -> pinky; palm = palm normal (both weapon space); along / depth = the hand's grip point (fraction of the
# way wrist -> knuckles, distance in front of the palm); curl = (base, middle, tip) degrees per finger 1 = thumb ..
# 5 = pinky; pole = elbow direction from the shoulder (camera space: x right, y up, z forward, Unity-like);
# camera_back = how far behind the eyes the camera sits for this hold (see pose_hold).
RIFLE_RIGHT = dict(grip=(0, 0, 0), across=(0.0, 0.3, -1.0), palm=(1.0, 0.0, 0.1), pole=(1.0, -1.0, -0.2),
                   curl={1: (10, 25, 20), 2: (18, 22, 12), 3: (62, 78, 42), 4: (68, 80, 45), 5: (74, 80, 45)})
HOLDS = {
    "rifle": dict(
        view=((0.11, -0.125, 0.27), (0, 0, 0)),
        right=RIFLE_RIGHT,
        left=dict(grip=(0.0, -0.28, 0.025), anchor=(0.0, -0.28, 0.025), across=(0.0, 1.0, 0.1), palm=(-0.45, 0.0, 1.0),
                  pole=(-0.6, -1.0, 0.0),
                  curl={1: (20, 30, 25), 2: (62, 72, 40), 3: (66, 76, 44), 4: (70, 78, 46), 5: (72, 80, 46)})),
    "pistol": dict(
        view=((0.1, -0.12, 0.3), (0, 0, 0)), camera_back=0.14,
        right=dict(RIFLE_RIGHT, grip=(0.0, 0.004, -0.035)),
        # support hand cups the firing hand: palm on the left of the grip, fingers wrapped around the front
        left=dict(grip=(0.012, 0.02, -0.06), anchor=(0.0, 0.02, -0.06), across=(-0.1, 0.6, -1.0),
                  palm=(-1.0, 0.0, 0.15),
                  pole=(-0.4, -1.0, 0.1),
                  curl={1: (25, 20, 15), 2: (58, 62, 35), 3: (60, 66, 38), 4: (64, 70, 40), 5: (66, 72, 42)})),
    "knife": dict(
        view=((0.14, -0.15, 0.27), (-18, -19, 0)), camera_back=0.06,
        # a fist around the handle, blade forward-left-up, back of the hand toward the camera; the handle runs
        # through the fist near the knuckles
        right=dict(grip=(0.0, -0.01, 0.0), across=(0.0, 1.0, 0.0), palm=(0.5, 0.0, -1.0), pole=(1.0, -1.0, -0.2),
                   along=0.85, depth=0.02,
                   curl={1: (40, 40, 30), 2: (85, 100, 62), 3: (88, 100, 64), 4: (90, 100, 64), 5: (92, 100, 62)})),
    "grenade": dict(
        view=((0.13, -0.15, 0.28), (0, 0, 0)), camera_back=0.06,
        # fingers round the body, the spoon under the palm
        right=dict(grip=(0.0, 0.0, 0.0), across=(0.0, 0.25, -1.0), palm=(1.0, 0.0, 0.0), pole=(1.0, -1.0, -0.2),
                   curl={1: (25, 30, 25), 2: (45, 50, 30), 3: (48, 52, 32), 4: (50, 55, 34), 5: (52, 58, 36)})),
    "c4": dict(
        view=((0.02, -0.2, 0.33), (0, 0, 0)), camera_back=0.1,
        # carried flat on the right palm
        right=dict(grip=(0.0, 0.0, -0.01), across=(1.0, 0.25, 0.0), palm=(0.0, 0.0, 1.0), pole=(1.0, -1.0, -0.4),
                   curl={1: (10, 10, 5), 2: (20, 25, 15), 3: (22, 28, 16), 4: (25, 30, 18), 5: (28, 32, 20)})),
}

CAMERA_BACK = 0.24
CAMERA_UP = 0.0

# sleeve and glove layout along the arm, t = 0 at the wrist, 1 at the elbow, > 1 on the upper arm
GLOVE_CUFF_END = 0.16
FACTION_LOOK = {
    "akinci": dict(sleeve="rolled", sleeve_start=0.58),   # flannel rolled up to below the elbow, bare forearm
    "muhafiz": dict(sleeve="full", sleeve_start=0.17),    # uniform sleeve over the glove cuff
}


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _materials(faction):
    import vexa_factions as F
    m = F.materials(faction)
    return {k: m[k] for k in ("skin", "jacket", "gloves")}


def build_rest_arms(faction):
    """Arms of the shaped body in the A-pose, skinned to the temporary rig (with finger bones), subdivided and
    dressed. Returns (mesh object, rig object, palm normals per side, position between the eyes)."""
    body, joint = H.build_body()
    lm = H.rest_landmarks(joint)
    eye = (lm["eye_r"] + lm["eye_l"]) / 2
    layout = H._rig_layout(joint)
    rig = H._make_armature("FPRig", layout)
    H._skin(body, rig)

    # keep the arms: vertices dominated by forearm, hand and finger bones, plus the lower part of the upper arm
    gname = {g.index: g.name for g in body.vertex_groups}
    keep = np.zeros(len(body.data.vertices), dtype=bool)
    for v in body.data.vertices:
        best, bn = 0.0, ""
        for g in v.groups:
            if g.weight > best:
                best, bn = g.weight, gname[g.group]
        side = "Right" if bn.startswith("Right") else "Left" if bn.startswith("Left") else None
        if side is None:
            continue
        if bn in (side + "LowerArm", side + "Hand") or re.match(side + r"F\d\d$", bn):
            keep[v.index] = True
        elif bn == side + "UpperArm":
            sh, el = Vector(layout[side + "UpperArm"][0]), Vector(layout[side + "UpperArm"][1])
            t = (v.co - sh).dot(el - sh) / (el - sh).length_squared
            keep[v.index] = t > 0.3
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(keep[v.index] for v in f.verts) or f.material_index != 0],
                     context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # close the cut at the upper arm (it can show at the edge of the screen)
    boundary = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    bm.to_mesh(body.data)
    bm.free()

    # smoother surface: one level of subdivision (weights and UVs are interpolated)
    mod_arm = next(m for m in body.modifiers if m.type == "ARMATURE")
    body.modifiers.remove(mod_arm)
    sub = body.modifiers.new("Sub", "SUBSURF")
    sub.levels = 1
    sub.render_levels = 1
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=sub.name)

    _dress(body, layout, faction)
    body.name = "Arms_" + faction
    for p in body.data.polygons:
        p.use_smooth = True
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    body.parent = rig

    # palm normals in the A-pose (from the back of the hand into the palm), as vexa_human.pose_body
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    palm = {}
    for side in ("Right", "Left"):
        d, across = H._hand_frame(rig, side)
        n = d.cross(across).normalized()
        thumb = rig.pose.bones[side + "F13"].tail - rig.pose.bones[side + "Hand"].head
        palm[side] = n if n.dot(thumb) > 0 else -n
    bpy.ops.object.mode_set(mode="OBJECT")
    return body, rig, palm, eye


def _arm_param(co, layout, side):
    """t along the arm: 0 wrist, 1 elbow, 2 shoulder (piecewise)."""
    wr = Vector(layout[side + "LowerArm"][1])
    el = Vector(layout[side + "LowerArm"][0])
    sh = Vector(layout[side + "UpperArm"][0])
    fa = el - wr
    t = (co - wr).dot(fa) / fa.length_squared
    if t > 1:
        ua = sh - el
        t = 1 + (co - el).dot(ua) / ua.length_squared
    return t


def _dress(o, layout, faction):
    """Glove, glove cuff and sleeve as surface offsets along the normals; assigns the materials per face."""
    look = FACTION_LOOK[faction]
    mats = _materials(faction)
    me = o.data
    me.materials.clear()
    for k in ("skin", "jacket", "gloves"):        # material index = region: 0 skin, 1 sleeve, 2 glove
        me.materials.append(mats[k])
    n = len(me.vertices)
    co = np.zeros(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nor = np.zeros(n * 3)
    me.vertices.foreach_get("normal", nor)
    nor = nor.reshape(-1, 3)
    gname = {g.index: g.name for g in o.vertex_groups}
    side_of = np.zeros(n, dtype=np.int8)       # 1 right, -1 left
    in_hand = np.zeros(n, dtype=np.float64)    # weight of hand + finger bones
    is_finger = np.zeros(n, dtype=np.float64)
    for v in me.vertices:
        wr = wl = 0.0
        for g in v.groups:
            nm = gname[g.group]
            if nm.startswith("Right"):
                wr += g.weight
            elif nm.startswith("Left"):
                wl += g.weight
            finger = re.match(r"(Right|Left)F(\d)\d$", nm)
            if nm.endswith("Hand") or finger:
                in_hand[v.index] += g.weight
            if finger and finger.group(2) != "1":
                is_finger[v.index] += g.weight
        side_of[v.index] = 1 if wr >= wl else -1
    t = np.array([_arm_param(Vector(co[i]), layout, "Right" if side_of[i] > 0 else "Left") for i in range(n)])
    t = np.where(in_hand > 0.5, np.minimum(t, -0.01), t)

    # --- offsets (meters) ---
    glove = 1.0 - smoothstep(GLOVE_CUFF_END - 0.015, GLOVE_CUFF_END + 0.01, t)          # 1 on hand and cuff
    off = glove * (0.0016 + 0.0004 * (1 - is_finger))
    cuff = smoothstep(-0.02, 0.03, t) * glove                                            # the cuff band over the wrist
    off += cuff * 0.0045
    ang = np.arctan2(co[:, 2], co[:, 0])   # around the arm (rough, for fold variation)
    if look["sleeve"] == "full":
        s0 = look["sleeve_start"]
        sleeve = smoothstep(s0 - 0.01, s0 + 0.025, t)
        # loose, irregular creases (no even rings)
        wave = (np.sin(t * 19.0 + ang * 2.0 + 1.4 * np.sin(ang * 3.0 + t * 5.0)) * 0.6
                + np.sin(t * 31.0 - ang * 3.0 + 0.8) * 0.4)
        folds = 0.0013 * wave * smoothstep(s0 + 0.05, s0 + 0.3, t)
        lip = np.exp(-((t - (s0 + 0.03)) / 0.025) ** 2) * 0.003                         # hem
        off = np.maximum(off, sleeve * (0.011 + folds + lip))
        region = np.where(t < GLOVE_CUFF_END, 2, np.where(t < s0 + 0.008, 2, 1))
    else:
        s0 = look["sleeve_start"]
        roll_w = 0.17
        roll = np.clip((t - s0) / roll_w, 0, 1)
        bulge = np.where((t >= s0) & (t <= s0 + roll_w), np.sin(roll * math.pi) ** 0.6, 0.0)
        above = smoothstep(s0 + roll_w * 0.6, s0 + roll_w, t)
        sleeve_off = bulge * 0.019 + above * 0.012 + 0.0015 * np.sin(t * 30 + ang * 2) * above
        off = np.maximum(off, np.where(t >= s0 - 0.005, sleeve_off + 0.004 * smoothstep(s0 - 0.005, s0 + 0.01, t), 0))
        region = np.where(t < GLOVE_CUFF_END, 2, np.where(t < s0, 0, 1))
    me.attributes.new("side", "INT", "POINT").data.foreach_set("value", side_of.astype(np.int32))
    co = co + nor * off[:, None]
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    # faces take the material of the majority of their vertices
    for p in me.polygons:
        r = [region[i] for i in p.vertices]
        p.material_index = max(set(r), key=r.count)


def pose_hold(body, rig, palm, hold, eye):
    """Poses the arms for one hold and returns {side: weapon-space mesh object} (origins at the hold's anchors)."""
    cfg = HOLDS[hold]
    pos, euler = cfg["view"]
    # the camera sits behind the anatomical eyes, so the shoulders are a little ahead of it: like every FPS view
    # model, otherwise the arms can't reach a rifle's handguard at the distance the game draws it
    cam = eye + Vector((0, cfg.get("camera_back", CAMERA_BACK), CAMERA_UP))
    O = cam + unity_pos(pos)                 # weapon origin in body space (camera = body orientation)
    Rw = unity_rot(*euler)                   # weapon orientation in body space
    W = lambda p: O + Rw @ Vector(p)
    Wd = lambda d: (Rw @ Vector(d)).normalized()

    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    sides = [("Right", cfg["right"])] + ([("Left", cfg["left"])] if cfg.get("left") else [])
    for side, hc in sides:
        h = rig.pose.bones[side + "Hand"]
        rest_q = h.matrix.to_quaternion()
        cur = H._hand_frame_now(rig, side, palm[side])
        tgt = H._frame(Wd(hc["across"]), Wd(hc["palm"]))
        delta = (tgt @ cur.transposed()).to_quaternion()
        g_rest = h.head + (h.tail - h.head) * hc.get("along", 0.55) + palm[side] * hc.get("depth", 0.024)
        off = delta @ (g_rest - h.head)
        shoulder = rig.pose.bones[side + "UpperArm"].head.copy()
        pole = shoulder + unity_pos(hc["pole"]).normalized() * 0.5
        wrist_target = W(hc["grip"]) - off
        H._ik(rig, side, wrist_target, pole)
        miss = (rig.pose.bones[side + "Hand"].head - wrist_target).length
        if miss > 0.005:
            print(f"  {hold} {side}: hand misses its target by {miss * 100:.1f} cm", flush=True)
        cur_q = rig.pose.bones[side + "Hand"].matrix.to_quaternion()
        want_q = delta @ rest_q
        fix = want_q @ cur_q.inverted()
        fa = rig.pose.bones[side + "LowerArm"]
        axis = (fa.tail - fa.head).normalized()
        twist = Quaternion(axis, fix.angle * Vector(fix.axis).dot(axis) * 0.5)
        H._rotate_bone(rig, side + "LowerArm", twist)
        cur_q = rig.pose.bones[side + "Hand"].matrix.to_quaternion()
        H._rotate_bone(rig, side + "Hand", want_q @ cur_q.inverted())
        saved = H.CURL[side]
        H.CURL[side] = hc["curl"]
        try:
            H._curl_fingers(rig, side, (delta @ palm[side]).normalized())
        finally:
            H.CURL[side] = saved
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()

    # bake the pose into a copy, move it to weapon space and split the sides
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(body.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    to_ws = Rw.inverted().to_4x4() @ Matrix.Translation(-O)
    me.transform(to_ws)
    out = {}
    for side, hc in sides:
        sm = me.copy()
        bm = bmesh.new()
        bm.from_mesh(sm)
        lay = bm.verts.layers.int.get("side")
        want = 1 if side == "Right" else -1
        drop = [f for f in bm.faces if f.verts[0][lay] != want]
        bmesh.ops.delete(bm, geom=drop, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(sm)
        bm.free()
        # the game puts RightArm on the weapon's origin and LeftArm on the weapon's "Support" point
        sm.transform(Matrix.Translation(-Vector(hc.get("anchor", (0, 0, 0)))))
        o = bpy.data.objects.new(side + "Arm", sm)
        bpy.context.collection.objects.link(o)
        out[side] = o
    bpy.data.meshes.remove(me)
    return out


def build(faction, out, textures, holds):
    reset_scene()
    body, rig, palm, eye = build_rest_arms(faction)
    if textures:
        _bake(body, faction, out)
    for hold in holds:
        objs = pose_hold(body, rig, palm, hold, eye)
        parts = list(objs.values())
        for o in parts:
            for uvl in [u for u in o.data.uv_layers if u.name != "atlas"]:
                o.data.uv_layers.remove(uvl)
        export_fbx(os.path.join(out, "Arms", f"{faction}_{hold}.fbx"), parts)
        print(f"{faction:8} {hold:8} " + "  ".join(f"{o.name} {tri_count(o)} tris" for o in parts), flush=True)
        for o in parts:
            bpy.data.objects.remove(o)


def _bake(body, faction, out):
    """One texture set for both arms (they share the mesh; every hold reuses it)."""
    import vexa_textures
    vexa_textures.bake_model(body, os.path.join(out, "Arms", "Textures"), faction, ARM_TEX, normal=True, scale_hint=1.6,
                             samples=8, jpeg=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--only", default="")
    ap.add_argument("--holds", default="")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    factions = [f for f in args.only.split(",") if f] or list(FACTIONS)
    holds = [h for h in args.holds.split(",") if h] or list(HOLDS)
    for faction in factions:
        build(faction, args.out, args.textures, holds)


if __name__ == "__main__":
    main()
