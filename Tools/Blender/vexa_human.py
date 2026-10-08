"""
Realistic human base for the VEXA characters (v3).

The body is MakeHuman's base mesh (CC0, see data/MAKEHUMAN_CC0.md) shaped with its macro targets into a fit
young adult male, then posed into the game's aiming pose:

  * a temporary rig (17 game bones + 15 finger bones per hand) is fitted to MakeHuman's joint markers and
    skinned with automatic (heat) weights,
  * the torso blades slightly (left shoulder forward), both arms are solved with two-bone IK so the wrists land
    on the rifle grip / handguard, and the fingers curl around the weapon (index finger straight on the right
    hand, along the trigger guard),
  * the pose is applied to the mesh, which becomes the rest pose of the exported character.

Returns the posed body plus the joint positions the final (game) armature is built from.
Blender space: forward -Y, right -X, up +Z, meters.
"""
import math
import os
import re

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data", "makehuman_cc0.npz")
SKIN_TEX = os.path.join(HERE, "data", "mh_skin_young_male.png")

HEIGHT = 1.80          # barefoot; boots add the rest (the hitboxes assume ~1.83 m)
SOLE = 0.028           # body is lifted by the boot sole thickness

# an athletic young man (the bulk comes from the clothes and the gear, not from the body)
DEFAULT_SHAPE = {
    "macrodetails/caucasian-male-young": 1.0,
    "macrodetails/universal-male-young-averagemuscle-averageweight": 0.5,
    "macrodetails/universal-male-young-maxmuscle-averageweight": 0.5,
    "macrodetails/proportions/male-young-averagemuscle-averageweight-idealproportions": 0.5,
    "macrodetails/proportions/male-young-maxmuscle-averageweight-idealproportions": 0.5,
    "macrodetails/height/male-young-maxmuscle-averageweight-maxheight": 0.25,
    "measure/measure-shoulder-increase": 0.3, "measure/measure-neckcirc-increase": 0.35,
    "head/head-square": 0.35, "chin/chin-width-max": 0.3, "chin/chin-prominent-more": 0.2,
    "neck/neck-scale-horiz-more": 0.3, "neck/neck-scale-depth-more": 0.2,
    "torso/torso-muscle-pectoral-incr": 0.2, "torso/torso-muscle-dorsi-incr": 0.3,
    "stomach/stomach-pregnant-decr": 0.3,
}

GAME_BONES = ["Hips", "Spine", "Chest", "Neck", "Head",
              "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperArm", "LeftLowerArm", "LeftHand",
              "RightUpperLeg", "RightLowerLeg", "RightFoot", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot"]
PARENT = {"Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck",
          "RightUpperArm": "Chest", "RightLowerArm": "RightUpperArm", "RightHand": "RightLowerArm",
          "LeftUpperArm": "Chest", "LeftLowerArm": "LeftUpperArm", "LeftHand": "LeftLowerArm",
          "RightUpperLeg": "Hips", "RightLowerLeg": "RightUpperLeg", "RightFoot": "RightLowerLeg",
          "LeftUpperLeg": "Hips", "LeftLowerLeg": "LeftUpperLeg", "LeftFoot": "LeftLowerLeg"}

# aiming pose: grip points (the weapon's grip origin and its handguard "Support" point, ~0.28 m apart),
# hand frames as (knuckle axis index->pinky, palm normal), elbow pole points. The hands sit around the arm
# capsules of Core/Combat/Hitboxes.cs.
POSE = {
    "chest_twist": 24.0,       # degrees, left shoulder forward
    "chest_lean": 9.0,         # degrees forward
    "Right": dict(grip=(-0.1, -0.29, 1.24), across=(0.0, 0.3, -1.0), palm=(1.0, 0.0, 0.1), pole=(-0.6, 0.25, 0.7)),
    "Left": dict(grip=(-0.1, -0.57, 1.285), across=(0.0, 1.0, 0.1), palm=(-0.45, 0.0, 1.0), pole=(0.6, -0.1, 0.6)),
}
# finger curl in degrees (base, middle, tip) per finger 1=thumb .. 5=pinky
CURL = {
    "Right": {1: (10, 25, 20), 2: (18, 22, 12), 3: (62, 78, 42), 4: (68, 80, 45), 5: (74, 80, 45)},
    "Left": {1: (20, 30, 25), 2: (62, 72, 40), 3: (66, 76, 44), 4: (70, 78, 46), 5: (72, 80, 46)},
}


def _load():
    return np.load(DATA)


def _to_blender(p):
    p = np.asarray(p, dtype=np.float64)
    return np.stack([p[..., 0], -p[..., 2], p[..., 1]], axis=-1)


def build_body(shape=None, name="Body"):
    """Shaped MakeHuman mesh (body + eyeballs) in the A-pose, scaled to HEIGHT, feet at SOLE. Returns (obj, joint fn)."""
    d = _load()
    v = d["verts"].astype(np.float64).copy()
    for t, w in (shape or DEFAULT_SHAPE).items():
        v[d[f"target:{t}:idx"]] += d[f"target:{t}:delta"].astype(np.float64) * 1e-3 * w
    quads, qmat, quv, uvs = d["quads"], d["quad_mat"], d["quad_uvs"], d["uvs"]
    vb = _to_blender(v)
    body_idx = np.unique(quads[qmat == 0])
    zmin, zmax = vb[body_idx, 2].min(), vb[body_idx, 2].max()
    s = HEIGHT / (zmax - zmin)
    vb = (vb - np.array([0, 0, zmin])) * s + np.array([0, 0, SOLE])

    used = np.unique(quads)
    remap = -np.ones(len(vb), dtype=np.int64)
    remap[used] = np.arange(len(used))
    faces, face_uv = [], []
    for q, fu in zip(quads, quv):
        if q[3] == q[2]:
            faces.append([remap[q[0]], remap[q[1]], remap[q[2]]])
            face_uv.append(fu[:3])
        else:
            faces.append([remap[i] for i in q])
            face_uv.append(fu)
    me = bpy.data.meshes.new(name)
    me.from_pydata(vb[used].tolist(), [], faces)
    me.update()
    uvl = me.uv_layers.new(name="mh")
    loop_uv = np.concatenate([uvs[np.asarray(fu)] for fu in face_uv])
    uvl.data.foreach_set("uv", loop_uv.ravel())
    me.polygons.foreach_set("material_index", qmat.astype(np.int32))
    for p in me.polygons:
        p.use_smooth = True
    _refine_eyes(me)
    # rest (A-pose) coordinates travel with the vertices through posing: clothing regions are defined on them
    rest = np.zeros(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", rest)
    me.attributes.new("rest", "FLOAT_VECTOR", "POINT").data.foreach_set("vector", rest)
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)

    def joint(bone, end="head"):
        return Vector(vb[d[f"joint:{bone}:{end}"]].mean(0))
    return o, joint


def _refine_eyes(me):
    """Rounds MakeHuman's low-poly helper eyeballs and stores an "iris" attribute (1 at the pupil, looking forward)
    that the eye shader turns into sclera / iris / pupil."""
    bm = bmesh.new()
    bm.from_mesh(me)
    edges = list({e for f in bm.faces if f.material_index in (1, 2) for e in f.edges})
    if edges:
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=2, use_grid_fill=True, smooth=1.0)
    bm.verts.index_update()
    vals = np.full(len(bm.verts), -1.0, dtype=np.float32)
    for mat_i in (1, 2):
        verts = list({v for f in bm.faces if f.material_index == mat_i for v in f.verts})
        if not verts:
            continue
        c = sum((v.co for v in verts), Vector()) / len(verts)
        r = sum((v.co - c).length for v in verts) / len(verts)
        for v in verts:
            d = (v.co - c).normalized()
            v.co = c + d * r
            vals[v.index] = d.dot(Vector((0, -1, 0)))
    bm.to_mesh(me)
    bm.free()
    me.attributes.new("iris", "FLOAT", "POINT").data.foreach_set("value", vals)
    for p in me.polygons:
        p.use_smooth = True


# ---------------------------------------------------------------- temporary rig

def _rig_layout(joint):
    """bone -> (head, tail, parent) in the A-pose, from the MakeHuman joint markers."""
    J = joint
    hipc = (J("upperleg01.L") + J("upperleg01.R")) / 2
    L = {
        "Hips": (Vector((0, hipc.y, hipc.z)), J("spine03"), None),
        "Spine": (J("spine03"), J("spine01"), "Hips"),
        "Chest": (J("spine01"), J("neck01"), "Spine"),
        "Neck": (J("neck01"), J("head"), "Chest"),
        "Head": (J("head"), J("head", "tail"), "Neck"),
    }
    for side, s in (("Right", "R"), ("Left", "L")):
        L[side + "UpperArm"] = (J(f"upperarm01.{s}"), J(f"lowerarm01.{s}"), "Chest")
        L[side + "LowerArm"] = (J(f"lowerarm01.{s}"), J(f"wrist.{s}"), side + "UpperArm")
        L[side + "Hand"] = (J(f"wrist.{s}"), J(f"finger3-1.{s}"), side + "LowerArm")
        L[side + "UpperLeg"] = (J(f"upperleg01.{s}"), J(f"lowerleg01.{s}"), "Hips")
        L[side + "LowerLeg"] = (J(f"lowerleg01.{s}"), J(f"foot.{s}"), side + "UpperLeg")
        L[side + "Foot"] = (J(f"foot.{s}"), J(f"toe3-1.{s}"), side + "LowerLeg")
        for f in range(1, 6):
            parent = side + "Hand"
            for k in range(1, 4):
                bn = f"{side}F{f}{k}"
                L[bn] = (J(f"finger{f}-{k}.{s}"), J(f"finger{f}-{k}.{s}", "tail"), parent)
                parent = bn
    return L


def _make_armature(name, layout):
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
        if parent:
            e.parent = eb[parent]
            e.use_connect = False
        eb[b] = e
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def _skin(mesh, armature):
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")


def _rotate_bone(arm, name, q, pivot=None):
    """Rotates a posed bone (and so its children) by world quaternion q about its head (or pivot)."""
    pb = arm.pose.bones[name]
    M = pb.matrix.copy()
    p = pivot if pivot is not None else M.to_translation()
    R = Matrix.Translation(p) @ q.to_matrix().to_4x4() @ Matrix.Translation(-p)
    pb.matrix = R @ M
    bpy.context.view_layer.update()


def _aim_bone(arm, name, target_dir):
    pb = arm.pose.bones[name]
    cur = (pb.tail - pb.head).normalized()
    _rotate_bone(arm, name, cur.rotation_difference(Vector(target_dir).normalized()))


def _ik(arm, side, wrist, pole):
    ua, la = arm.pose.bones[side + "UpperArm"], arm.pose.bones[side + "LowerArm"]
    S = ua.head.copy()
    a = (ua.tail - ua.head).length
    b = (la.tail - la.head).length
    W = Vector(wrist)
    d = min((W - S).length, (a + b) * 0.999)
    u = (W - S).normalized()
    pv = Vector(pole) - S
    v = (pv - u * pv.dot(u)).normalized()
    cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
    E = S + a * (cos_a * u + math.sqrt(1 - cos_a * cos_a) * v)
    _aim_bone(arm, side + "UpperArm", E - S)
    _aim_bone(arm, side + "LowerArm", (S + u * d) - arm.pose.bones[side + "LowerArm"].head)


def _hand_frame(arm, side):
    """(direction wrist->knuckles, knuckle axis index->pinky) of the posed hand."""
    pb = arm.pose.bones
    dir_ = (pb[side + "Hand"].tail - pb[side + "Hand"].head).normalized()
    across = (pb[side + "F51"].head - pb[side + "F21"].head)
    across = (across - dir_ * across.dot(dir_)).normalized()
    return dir_, across


def _frame(across, normal):
    """Orthonormal frame (as a rotation matrix) with columns: hand direction, knuckle axis, palm normal."""
    n = Vector(normal).normalized()
    c = Vector(across)
    c = (c - n * c.dot(n)).normalized()
    d = c.cross(n).normalized()
    return Matrix((d, c, n)).transposed()


def _hand_frame_now(arm, side, palm_n):
    d, across = _hand_frame(arm, side)
    n = palm_n - d * palm_n.dot(d) - across * palm_n.dot(across)
    return _frame(across, n)


def _curl_fingers(arm, side, palm_normal):
    from mathutils import Quaternion
    # close the spread: index, ring and pinky turn toward the middle finger (about the palm normal)
    mid = (arm.pose.bones[f"{side}F31"].tail - arm.pose.bones[f"{side}F31"].head).normalized()
    for f in (2, 4, 5):
        pb = arm.pose.bones[f"{side}F{f}1"]
        d = (pb.tail - pb.head).normalized()
        d_p = (d - palm_normal * d.dot(palm_normal)).normalized()
        m_p = (mid - palm_normal * mid.dot(palm_normal)).normalized()
        ang = d_p.angle(m_p) * 0.65
        axis = d_p.cross(m_p)
        if axis.length > 1e-6:
            _rotate_bone(arm, f"{side}F{f}1", Quaternion(axis.normalized(), ang))
    for f, angles in CURL[side].items():
        for k, deg in enumerate(angles, start=1):
            pb = arm.pose.bones[f"{side}F{f}{k}"]
            d = (pb.tail - pb.head).normalized()
            if f == 1:
                # the thumb wraps toward the fingers, around the palm's long axis
                hand_d = (arm.pose.bones[side + "Hand"].tail - arm.pose.bones[side + "Hand"].head).normalized()
                axis = hand_d if side == "Left" else -hand_d
            else:
                axis = d.cross(palm_normal).normalized()
            _rotate_bone(arm, f"{side}F{f}{k}", Quaternion(axis, math.radians(deg)))


def pose_body(body, joint):
    """Poses the body into the aiming pose (applied to the mesh). Returns the posed game-bone layout."""
    layout = _rig_layout(joint)
    rig = _make_armature("TempRig", layout)
    _skin(body, rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    from mathutils import Quaternion
    pb = rig.pose.bones
    # palm normals in the A-pose (point from the back of the hand into the palm)
    palm = {}
    for side in ("Right", "Left"):
        d, across = _hand_frame(rig, side)
        n = d.cross(across).normalized()
        # the thumb tip of a relaxed hand sits on the palm side of the hand's plane
        thumb = rig.pose.bones[side + "F13"].tail - rig.pose.bones[side + "Hand"].head
        palm[side] = n if n.dot(thumb) > 0 else -n
    # stance: chest leans forward a little and blades (left shoulder forward); head stays level, facing forward
    chest_pivot = pb["Spine"].head.copy()
    _rotate_bone(rig, "Spine", Quaternion((1, 0, 0), math.radians(-POSE["chest_lean"] * 0.4)), chest_pivot)
    _rotate_bone(rig, "Chest", Quaternion((1, 0, 0), math.radians(-POSE["chest_lean"] * 0.6)))
    _rotate_bone(rig, "Chest", Quaternion((0, 0, 1), math.radians(-POSE["chest_twist"])))
    _rotate_bone(rig, "Neck", Quaternion((0, 0, 1), math.radians(POSE["chest_twist"] * 0.55)))
    _rotate_bone(rig, "Neck", Quaternion((1, 0, 0), math.radians(POSE["chest_lean"] * 0.7)))
    _rotate_bone(rig, "Head", Quaternion((0, 0, 1), math.radians(POSE["chest_twist"] * 0.45)))
    _rotate_bone(rig, "Head", Quaternion((1, 0, 0), math.radians(POSE["chest_lean"] * 0.4)))
    # legs: slightly apart, knees soft
    for side, sx in (("Right", -1), ("Left", 1)):
        _aim_bone(rig, side + "UpperLeg", Vector((0.03 * sx, -0.02, -1)))
        _aim_bone(rig, side + "LowerLeg", Vector((0.0, 0.03, -1)))
        _aim_bone(rig, side + "Foot", Vector((0.12 * sx, -1, -0.45)))
    from mathutils import Quaternion
    for side in ("Right", "Left"):
        cfg = POSE[side]
        h = rig.pose.bones[side + "Hand"]
        rest_q = h.matrix.to_quaternion()
        # rotation that brings the hand's current frame onto the target frame
        cur = _hand_frame_now(rig, side, palm[side])
        tgt = _frame(cfg["across"], cfg["palm"])
        delta = (tgt @ cur.transposed()).to_quaternion()
        # grip point relative to the wrist: palm center, a little in front of the palm
        g_rest = h.head + (h.tail - h.head) * 0.55 + palm[side] * 0.024
        off = delta @ (g_rest - h.head)
        _ik(rig, side, Vector(cfg["grip"]) - off, cfg["pole"])
        # absolute hand orientation, half of the twist taken up by the forearm (less candy-wrapping at the wrist)
        cur_q = rig.pose.bones[side + "Hand"].matrix.to_quaternion()
        want_q = delta @ rest_q
        fix = want_q @ cur_q.inverted()
        fa = rig.pose.bones[side + "LowerArm"]
        axis = (fa.tail - fa.head).normalized()
        sw_axis = Vector(fix.axis)
        twist = Quaternion(axis, fix.angle * sw_axis.dot(axis) * 0.5)
        _rotate_bone(rig, side + "LowerArm", twist)
        cur_q = rig.pose.bones[side + "Hand"].matrix.to_quaternion()
        _rotate_bone(rig, side + "Hand", want_q @ cur_q.inverted())
        _curl_fingers(rig, side, (delta @ palm[side]).normalized())
    bpy.context.view_layer.update()
    posed = {b: (rig.pose.bones[b].head.copy(), rig.pose.bones[b].tail.copy()) for b in GAME_BONES}
    # grip points: between palm and fingers
    for side in ("Right", "Left"):
        posed[side + "Grip"] = (Vector(POSE[side]["grip"]), None)
    bpy.ops.object.mode_set(mode="OBJECT")
    # bake the pose into the mesh
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    mod = next(m for m in body.modifiers if m.type == "ARMATURE")
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # the posed feet rest on the boot sole
    lift = SOLE - min(v.co.z for v in body.data.vertices if v.co.z < 0.3)
    for v in body.data.vertices:
        v.co.z += lift
    for k, (h, t) in list(posed.items()):
        posed[k] = (h + Vector((0, 0, lift)), (t + Vector((0, 0, lift))) if t is not None else None)
    # remember which game bone dominates each vertex (clothing regions use it)
    dom = np.zeros(len(body.data.vertices), dtype=np.int32) - 1
    gidx = {g.index: g.name for g in body.vertex_groups}
    best = np.zeros(len(body.data.vertices))
    for v in body.data.vertices:
        for g in v.groups:
            n = gidx[g.group]
            if g.weight > best[v.index]:
                best[v.index] = g.weight
                m = re.match(r"(Right|Left)F\d\d$", n)
                base = m.group(1) + "Hand" if m else n
                dom[v.index] = GAME_BONES.index(base) if base in GAME_BONES else -1
    attr = body.data.attributes.new("bone", "INT", "POINT")
    attr.data.foreach_set("value", dom)
    body.parent = None
    body.vertex_groups.clear()
    bpy.data.objects.remove(rig)
    return posed


def game_layout(posed):
    """Final armature layout {bone: (head, tail, parent)} from the posed joints (bone names as in vexa_characters.BONES)."""
    out = {}
    for b in GAME_BONES:
        h, t = posed[b]
        out[b] = (tuple(h), tuple(t), PARENT.get(b))
    return out


def rest_landmarks(joint):
    """A-pose landmarks (same space as the "rest" attribute)."""
    L = {"neck": joint("neck01"), "head": joint("head"), "top": joint("head", "tail"), "waist": joint("spine03"),
         "chest": joint("spine01"), "jaw": joint("jaw")}
    for side, s in (("r", "R"), ("l", "L")):
        for k, j in (("eye", "eye"), ("shoulder", "upperarm01"), ("elbow", "lowerarm01"), ("wrist", "wrist"),
                     ("hip", "upperleg01"), ("knee", "lowerleg01"), ("ankle", "foot"), ("toe", "toe3-1")):
            L[k + "_" + side] = joint(f"{j}.{s}")
    return L
