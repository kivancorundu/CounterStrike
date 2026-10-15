"""
VEXA character animation (v2): procedural, physically motivated clips for the third-person characters.

    python vexa_anim.py --out ../../Game/Assets/Vexa/Resources/Models [--preview previews/]

    Characters/Anims/<faction>.fbx   the game armature (identical rest pose to Characters/<faction>.fbx) with clips:
        idle, crouch                                   loops, breathing, weight shifts, glances
        walk_f walk_b walk_l walk_r                    2.8 m/s (CS walk with a rifle), one cycle = two steps
        run_f  run_b  run_l  run_r                     5.5 m/s
        crouch_walk_f / _b / _l / _r                   1.9 m/s
        jump                                           push-off, tuck, airborne (held at the end)
        death                                          hit, knees give, twist, fall, settle
    Every locomotion clip of one gait has the same length and the right foot strikes at time 0, so the game blends
    the four directions with a shared phase (Client/PlayerAvatar.cs).

How it works: each frame is described as a pose (pelvis transform, rotations of the spine chain, a weapon transform,
foot targets); a small solver turns it into bone rotations:
  * legs: two-bone IK from the hip joints to ankle targets, knees toward the feet's direction; the hips drop by
    themselves if a leg would overstretch
  * feet: a gait generator plants each foot on the ground for its stance phase (it moves back at exactly the clip's
    speed, so nothing slides in the game), rolls heel -> flat -> toe, and swings it forward on an eased arc
  * pelvis: bob, lateral weight shift, yaw with the stepping leg, drop on the swing side; spine and chest
    counter-rotate so the shoulders (and the gun) stay on target
  * arms: the weapon gets its own motion (bob, sway, cant, lag) relative to the chest and BOTH hands are solved onto
    it with IK, so the rifle moves with the body and the hands never leave it; the elbows pump with it
  * head: stabilised in world space (eyes level and forward), with a little lag and nod
  * secondary: breathing, micro sway and glances in idles, compression on landings
Blender space: forward -Y, right -X, up +Z, meters. The rest pose is the aiming pose (vexa_human.pose_body).
"""
import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
FACTIONS = ("akinci", "muhafiz")

UP = Vector((0, 0, 1))
FWD = Vector((0, -1, 0))
RIGHT = Vector((-1, 0, 0))


# ------------------------------------------------------------------ math helpers

def rot_x(deg):
    return Matrix.Rotation(math.radians(deg), 3, "X")


def rot_y(deg):
    return Matrix.Rotation(math.radians(deg), 3, "Y")


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 3, "Z")


def pyr(pitch=0.0, yaw=0.0, roll=0.0):
    """Body-style rotation: pitch forward (+) about the left axis, yaw left (+) about up, roll (+ = left side
    down) about the back axis. Applied roll, then pitch, then yaw."""
    return rot_z(yaw) @ rot_x(pitch) @ rot_y(roll)


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def smoother(x):
    x = min(1.0, max(0.0, x))
    return x * x * x * (x * (6 * x - 15) + 10)


def lerp(a, b, t):
    return a + (b - a) * t


def wrap(x):
    return x - math.floor(x)


def frame_of(direction, hinge):
    """Orthonormal frame (columns: direction, hinge, direction x hinge)."""
    d = direction.normalized()
    h = (hinge - d * hinge.dot(d)).normalized()
    return Matrix((d, h, d.cross(h))).transposed()


def keyed(points, t, ease=smooth):
    """Piecewise interpolation through [(time, value), ...] (values: float, Vector or tuples), eased per segment."""
    if t <= points[0][0]:
        return points[0][1]
    for (t0, v0), (t1, v1) in zip(points, points[1:]):
        if t <= t1:
            k = ease((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple):
                return tuple(a + (b - a) * k for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * k
    return points[-1][1]


# ------------------------------------------------------------------ rig

class Rig:
    """Rest data of the game armature and the pose solver (bone world transforms -> local basis)."""

    def __init__(self, arm):
        self.arm = arm
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
        self.order = []
        seen = set()

        def visit(b):
            if b.name in seen:
                return
            if b.parent:
                visit(b.parent)
            seen.add(b.name)
            self.order.append(b.name)
        for b in arm.data.bones:
            visit(b)
        self.R0 = {n: m.to_3x3() for n, m in self.rest.items()}
        self.H0 = {n: m.to_translation() for n, m in self.rest.items()}
        self.T0 = {b.name: b.tail_local.copy() for b in arm.data.bones}
        self.thigh = (self.H0["RightLowerLeg"] - self.H0["RightUpperLeg"]).length
        self.shin = (self.H0["RightFoot"] - self.H0["RightLowerLeg"]).length
        self.ankle_h = self.H0["RightFoot"].z

    def child_offset(self, b):
        p = self.parent[b]
        return self.rest[p].inverted() @ self.rest[b].to_translation()

    def basis(self, world):
        """world: bone -> 4x4 armature-space matrix (every bone). Returns bone -> 4x4 pose basis."""
        out = {}
        for b in self.order:
            p = self.parent[b]
            if p is None:
                out[b] = self.rest[b].inverted() @ world[b]
            else:
                rel = self.rest[p].inverted() @ self.rest[b]
                out[b] = rel.inverted() @ world[p].inverted() @ world[b]
        return out


class Pose:
    """One frame being built: world rotations of bones, assembled parent before child."""

    def __init__(self, rig):
        self.rig = rig
        self.W = {}

    def delta(self, b):
        """World rotation of bone b relative to its rest orientation."""
        return self.W[b].to_3x3() @ self.rig.R0[b].inverted()

    def head(self, b):
        p = self.rig.parent[b]
        return self.W[p] @ self.rig.child_offset(b)

    def put(self, b, world_rot, head=None):
        h = head if head is not None else self.head(b)
        m = world_rot.to_4x4()
        m.translation = h
        self.W[b] = m

    def fk(self, b, local_delta=None):
        """Bone follows its parent, plus a delta rotation expressed in rest (body) axes about its head."""
        p = self.rig.parent[b]
        d = local_delta if local_delta is not None else Matrix.Identity(3)
        self.put(b, self.delta(p) @ d @ self.rig.R0[b])

    def world_delta(self, b, delta):
        """Bone takes an absolute world rotation delta (relative to rest)."""
        self.put(b, delta @ self.rig.R0[b])


def two_bone(root, target, la, lb, pole):
    """Two-bone chain from root to target (clamped), bending toward the pole point. Returns (joint, reached end,
    hinge axis); the hinge is v x u (v: toward the pole, u: root -> target), the same convention for the rest pose."""
    d = target - root
    dist = min(d.length, (la + lb) * 0.9995)
    u = d.normalized()
    pv = pole - root
    v = pv - u * pv.dot(u)
    if v.length < 1e-6:
        v = UP.cross(u)
    v.normalize()
    cos_a = max(-1.0, min(1.0, (la * la + dist * dist - lb * lb) / (2 * la * dist)))
    joint = root + la * (cos_a * u + math.sqrt(max(0.0, 1 - cos_a * cos_a)) * v)
    return joint, root + u * dist, v.cross(u).normalized()


def solve_chain(pose, upper, lower, end_target, pole, hinge_rest):
    """Two-bone IK that keeps each bone's twist: the rest hinge axis maps onto the solved bend plane's."""
    rig = pose.rig
    end = pose_child_end(rig, lower)
    S = pose.head(upper)
    la = (rig.H0[lower] - rig.H0[upper]).length
    lb = (rig.H0[end] - rig.H0[lower]).length
    E, reached, hinge = two_bone(S, end_target, la, lb, pole)
    d_up0 = rig.H0[lower] - rig.H0[upper]
    d_lo0 = rig.H0[end] - rig.H0[lower]
    pose.put(upper, frame_of(E - S, hinge) @ frame_of(d_up0, hinge_rest).transposed() @ rig.R0[upper], S)
    pose.put(lower, frame_of(reached - E, hinge) @ frame_of(d_lo0, hinge_rest).transposed() @ rig.R0[lower], E)
    return reached


def pose_child_end(rig, lower):
    """The joint at the end of a lower limb bone (foot / hand head)."""
    return {"RightLowerLeg": "RightFoot", "LeftLowerLeg": "LeftFoot",
            "RightLowerArm": "RightHand", "LeftLowerArm": "LeftHand"}[lower]


# ------------------------------------------------------------------ body model

class Body:
    """Everything a frame needs, with defaults = the rest (aiming) pose."""

    def __init__(self):
        self.hips = Vector((0, 0, 0))           # pelvis offset from rest
        self.pelvis = Matrix.Identity(3)        # pelvis rotation (world)
        self.spine = Matrix.Identity(3)         # deltas in body axes
        self.chest = Matrix.Identity(3)
        self.neck = Matrix.Identity(3)
        self.head_world = None                  # absolute head delta (stabilised); None = follow the neck
        self.head = Matrix.Identity(3)          # extra local head delta (glances, nods)
        self.weapon_pos = Vector((0, 0, 0))     # weapon offset in chest rest space, about the right grip
        self.weapon_rot = Matrix.Identity(3)
        self.arms_free = None                   # {bone: delta} FK arms instead of the weapon IK (death)
        self.feet = {}                          # side -> (ankle target, foot world delta)
        self.knee_dir = {}                      # side -> knee pole direction
        self.legs_free = None                   # {bone: delta} FK legs (jump tuck, death)


def apply_body(rig, body):
    """Solves a Body into world matrices for every bone."""
    pose = Pose(rig)
    W = pose.W
    # pelvis
    hips_head = rig.H0["Hips"] + body.hips
    pose.put("Hips", body.pelvis @ rig.R0["Hips"], hips_head)
    pose.fk("Spine", body.spine)
    pose.fk("Chest", body.chest)
    pose.fk("Neck", body.neck)
    if body.head_world is not None:
        pose.world_delta("Head", body.head_world @ body.head)
    else:
        pose.fk("Head", body.head)

    # weapon + arms
    chest_delta = W["Chest"] @ rig.rest["Chest"].inverted()
    pivot = rig.H0["RightGrip"]
    wo = Matrix.Translation(pivot + body.weapon_pos) @ body.weapon_rot.to_4x4() @ Matrix.Translation(-pivot)
    weapon = chest_delta @ wo
    for side in ("Right", "Left"):
        ua, la, hand = side + "UpperArm", side + "LowerArm", side + "Hand"
        if body.arms_free is not None:
            pose.fk(ua, body.arms_free.get(ua))
            pose.fk(la, body.arms_free.get(la))
            pose.fk(hand, body.arms_free.get(hand))
        else:
            wrist = weapon @ rig.H0[hand]
            elbow_rest = rig.H0[la]
            pole = chest_delta @ (elbow_rest + (elbow_rest - (rig.H0[ua] + rig.H0[hand]) * 0.5) * 2.0)
            # rest hinge in the v x u convention (v: toward the elbow, u: shoulder -> wrist)
            u0 = (rig.H0[hand] - rig.H0[ua]).normalized()
            v0 = rig.H0[la] - rig.H0[ua]
            h0 = (v0 - u0 * v0.dot(u0)).normalized().cross(u0).normalized()
            solve_chain(pose, ua, la, wrist, pole, h0)
            pose.put(hand, weapon.to_3x3() @ rig.R0[hand])
        grip = side + "Grip"
        if grip in rig.rest:
            pose.fk(grip)

    # legs
    pelvis_fwd = body.pelvis @ FWD
    for side in ("Right", "Left"):
        ul, ll, foot = side + "UpperLeg", side + "LowerLeg", side + "Foot"
        if body.legs_free is not None and side in body.legs_free:
            fr = body.legs_free[side]
            pose.fk(ul, fr.get(ul))
            pose.fk(ll, fr.get(ll))
            pose.fk(foot, fr.get(foot))
            continue
        ankle, foot_delta = body.feet[side]
        hip = pose.head(ul)
        kd = body.knee_dir.get(side, pelvis_fwd)
        pole = (hip + ankle) * 0.5 + kd * 0.6
        h0 = FWD.cross(rig.H0[ll] - rig.H0[ul]).normalized()     # = v x u for a knee bending forward
        solve_chain(pose, ul, ll, ankle, pole, h0)
        pose.put(foot, foot_delta @ rig.R0[foot])
    return W


# ------------------------------------------------------------------ feet & gait

class FootModel:
    """Contact geometry of a foot (boot sole on the ground at z = 0)."""

    def __init__(self, rig, side):
        a = rig.H0[side + "Foot"]
        t = rig.T0[side + "Foot"]
        self.side = side
        self.ankle = a.copy()
        self.h = a.z                                        # ankle height over the ground
        self.heel = Vector((a.x, a.y + 0.07, 0.0)) - Vector((a.x, a.y, a.z))      # from the ankle
        self.ball = Vector((t.x, t.y, 0.0)) - Vector((a.x, a.y, a.z))

    def ankle_at(self, ground, yaw_deg, roll_deg, lift=0.0):
        """Ankle position and foot rotation for a foot whose flat-footed ankle would sit over `ground` (a point on
        the floor), turned by yaw, rolled by roll (+ = heel up, pivoting on the ball; - = toes up, on the heel)."""
        R_yaw = rot_z(yaw_deg)
        flat_ankle = Vector((ground.x, ground.y, self.h + lift))
        heel = flat_ankle + R_yaw @ self.heel
        ball = flat_ankle + R_yaw @ self.ball
        side_axis = R_yaw @ Vector((1, 0, 0))
        R_roll = Matrix.Rotation(math.radians(roll_deg), 3, side_axis)
        pivot = ball if roll_deg > 0 else heel
        ankle = pivot + R_roll @ (flat_ankle - pivot)
        return ankle, R_roll @ R_yaw


class Gait:
    """Two-step locomotion cycle along a ground direction (character space)."""

    def __init__(self, speed, cycle, duty, direction, lift, width=1.0, heel=14.0, toe=26.0, swing_toe=-6.0,
                 bob=0.02, run=False, hips_drop=0.0, pelvis_yaw=5.0, pelvis_roll=3.0, sway=0.025, lean=3.0,
                 face=0.0, crouch=0.0, stance_shift=0.0, kick=0.0, swing_roll=None):
        self.v = speed
        self.T = cycle
        self.duty = duty
        self.dir = Vector((direction[0], direction[1], 0)).normalized()
        self.lift = lift
        self.width = width
        self.heel = heel
        self.toe = toe
        self.swing_toe = swing_toe
        self.bob = bob
        self.run = run
        self.hips_drop = hips_drop
        self.pelvis_yaw = pelvis_yaw
        self.pelvis_roll = pelvis_roll
        self.sway = sway
        self.lean = lean
        self.face = face            # pelvis / feet turned toward the motion (strafes), degrees
        self.crouch = crouch
        self.stance_shift = stance_shift    # the stance happens this far behind the hips (runners land under the body)
        self.kick = kick                    # heel kick: the swing foot keeps going back and up before coming through
        self.swing_roll = swing_roll        # foot roll keys over the swing [(u, deg)], None = walking profile

    @property
    def travel(self):
        return self.v * self.T * self.duty

    def foot_phase(self, t, side):
        return wrap(t / self.T + (0.0 if side == "Right" else 0.5))

    def roll(self, ph):
        """Foot roll over the cycle (degrees, + heel up). Backward motion rolls the other way."""
        d = self.duty
        if ph < d:
            s = ph / d
            r = keyed([(0.0, -self.heel), (0.16, 0.0), (0.62, 0.0), (1.0, self.toe)], s)
        else:
            u = (ph - d) / (1 - d)
            prof = self.swing_roll or [(0.0, self.toe), (0.35, self.swing_toe), (0.8, -self.heel * 0.9), (1.0, -self.heel)]
            r = keyed(prof, u)
        back = -self.dir.dot(FWD)          # 1 moving backward
        side_amount = abs(self.dir.x)
        if back > 0.5:
            r = -r * 0.7
        return r * (1.0 - 0.55 * side_amount)

    def ground_point(self, base, ph):
        """Where the (flat) foot's ankle projects on the floor, and its lift."""
        d, L = self.duty, self.travel
        base = base - self.dir * self.stance_shift
        if ph < d:
            s = ph / d
            p = base + self.dir * (L * 0.5 - L * s)
            lift = 0.0
        else:
            u = (ph - d) / (1 - d)
            k = smoother(u)
            back = self.kick * math.sin(math.pi * min(1.0, u / 0.7)) * (1.0 - u)
            p = base + self.dir * (-L * 0.5 + L * k - back)
            # lift peaks early in the swing (knee drive / heel kick), the foot lands gently
            peak = 0.42 if self.kick else 0.5
            uu = u * 0.5 / peak if u < peak else 0.5 + (u - peak) * 0.5 / (1 - peak)
            lift = self.lift * math.sin(math.pi * min(1.0, uu * 1.06)) ** 1.3
        return p, lift


def locomotion_frame(rig, g, t, feet, extra=None):
    """Builds the Body for gait g at time t (seconds)."""
    b = Body()
    x = t / g.T
    two = 2 * math.pi
    # ---- pelvis
    face = g.face
    yaw = face + g.pelvis_yaw * math.cos(two * x)                     # right hip forward at the right strike
    roll = g.pelvis_roll * math.cos(two * (x - g.duty * 0.5))         # swing-side hip drops at mid-stance
    mid = two * 2 * (x - g.duty * 0.5)
    if g.run:
        bob = -g.bob * math.cos(mid)                                  # compressed at mid-stance, up in flight
    else:
        bob = g.bob * math.cos(mid)                                   # vaulting over the stance leg
    surge = 0.012 * math.sin(mid) * (1.6 if g.run else 1.0)
    sway = -g.sway * math.cos(two * (x - g.duty * 0.5))                # toward the stance foot (right = -X)
    b.pelvis = rot_z(yaw) @ rot_x(g.lean * 0.5 + (1.5 if g.run else 0.6) * math.cos(mid)) @ rot_y(roll)
    b.hips = Vector((sway, 0, bob - g.hips_drop)) + g.dir * surge
    # ---- spine: lean, counter-rotation so the chest keeps facing the target
    lean = g.lean + g.crouch * 12.0
    b.spine = pyr(pitch=lean * 0.55 - 0.6 * math.cos(mid), yaw=-(yaw - face) * 0.55 - face * 0.55, roll=-roll * 0.6)
    b.chest = pyr(pitch=lean * 0.45, yaw=-(yaw - face) * 0.55 - face * 0.45, roll=-roll * 0.5)
    # ---- head: steady, eyes level; a small nod after each footfall
    nod = (2.2 if g.run else 1.0) * math.sin(mid - 0.9)
    b.neck = pyr(pitch=-lean * 0.35)
    b.head_world = rot_z(0.8 * math.sin(two * x + 0.6)) @ rot_x(nod * 0.6)
    # ---- weapon: bob and sway behind the body, a little cant
    amp = 1.0 if not g.run else 2.4
    b.weapon_pos = Vector((0.006 * amp * math.sin(two * x - 0.5),
                           0.004 * amp * math.sin(mid - 1.0) + (0.025 if g.run else 0.0),
                           -0.006 * amp * math.cos(mid - 0.7) - (0.02 if g.run else 0.0)))
    b.weapon_rot = pyr(pitch=-(1.2 * amp) * math.cos(mid - 0.8) + (6.0 if g.run else 0.0),
                       yaw=1.2 * amp * math.sin(two * x - 0.6),
                       roll=(2.0 * amp) * math.sin(two * x - 0.4) + (5.0 if g.run else 0.0))
    # ---- feet
    foot_yaw = face
    for side, fm in feet.items():
        ph = g.foot_phase(t, side)
        stance = rot_z(face) @ Vector((fm.ankle.x * g.width, 0.0, 0.0))
        base = Vector((0, rig.H0["Hips"].y, 0)) + stance
        gp, lift = g.ground_point(base, ph)
        toe_out = 4.0 if side == "Left" else -4.0          # toes turned out a little
        ankle, fd = fm.ankle_at(gp, foot_yaw + toe_out, g.roll(ph), lift)
        b.feet[side] = (ankle, fd)
        b.knee_dir[side] = (rot_z(foot_yaw) @ FWD + UP * 0.1 * g.crouch).normalized()
    if extra:
        extra(b, t)
    return b


def settle_hips(rig, body, iterations=3):
    """Lowers the pelvis until neither leg overstretches (the knees then bend to absorb it)."""
    reach = (rig.thigh + rig.shin) * 0.985
    for _ in range(iterations):
        need = 0.0
        for side in ("Right", "Left"):
            if side not in body.feet:
                continue
            hip = rig.H0["Hips"] + body.hips + body.pelvis @ (rig.H0[side + "UpperLeg"] - rig.H0["Hips"])
            ankle = body.feet[side][0]
            d = (ankle - hip).length
            if d > reach:
                horiz = Vector((ankle.x - hip.x, ankle.y - hip.y, 0)).length
                z_ok = math.sqrt(max(0.0, reach * reach - horiz * horiz))
                need = max(need, (hip.z - ankle.z) - z_ok)
        if need <= 1e-4:
            break
        body.hips.z -= need


# ------------------------------------------------------------------ clips

GAITS = {
    # name: (speed m/s, frames per cycle, duty, lift, bob, lean, hips drop, run?)
    "walk": dict(speed=2.8, frames=22, duty=0.56, lift=0.09, bob=0.018, lean=4.0, hips_drop=0.05, run=False,
                 pelvis_yaw=6.0, pelvis_roll=3.5, sway=0.022, heel=16.0, toe=28.0, stance_shift=0.04),
    "run": dict(speed=5.5, frames=21, duty=0.28, lift=0.3, bob=0.032, lean=11.0, hips_drop=0.03, run=True,
                pelvis_yaw=9.0, pelvis_roll=4.5, sway=0.012, heel=6.0, toe=38.0, stance_shift=0.14, kick=0.22,
                swing_roll=[(0.0, 38.0), (0.3, 72.0), (0.6, 20.0), (0.85, -6.0), (1.0, -6.0)]),
    "crouch_walk": dict(speed=1.9, frames=24, duty=0.62, lift=0.07, bob=0.012, lean=6.0, hips_drop=0.36, run=False,
                        pelvis_yaw=5.0, pelvis_roll=2.5, sway=0.03, heel=10.0, toe=22.0, crouch=1.0),
}
DIRECTIONS = {"f": (0, -1), "b": (0, 1), "l": (1, 0), "r": (-1, 0)}
# pelvis / feet turn toward a sideways motion (the chest turns back to the front)
FACE = {"walk": 38.0, "run": 52.0, "crouch_walk": 36.0}


def make_gait(name, d):
    cfg = dict(GAITS[name])
    frames = cfg.pop("frames")
    dx, dy = DIRECTIONS[d]
    face = 0.0
    if d == "l":
        face = FACE[name]
    elif d == "r":
        face = -FACE[name]
    if d == "b":
        cfg["lean"] = cfg["lean"] * 0.35
        cfg["lift"] *= 0.8
    if d in ("l", "r"):
        cfg["pelvis_yaw"] *= 0.6
    width = 1.0 if d in ("f", "b") else 1.15
    return Gait(cycle=frames / FPS, direction=(dx, dy), face=face, width=width, **cfg), frames


def loop_noise(t, period, seeds):
    """Smooth periodic noise: a few harmonics of the loop frequency (seamless)."""
    w = 2 * math.pi / period
    return sum(a * math.sin(w * k * t + p) for k, a, p in seeds)


def idle_frame(rig, feet, t, period, crouch=0.0):
    b = Body()
    # weight on one leg, then the other, once per loop; breathing 3 times per loop
    shift = math.sin(2 * math.pi * t / period)
    breath = math.sin(2 * math.pi * 3 * t / period)
    micro = lambda s: loop_noise(t, period, s)
    b.hips = Vector((0.022 * shift * (1 - 0.5 * crouch), 0.004 * micro([(2, 1, 0.3)]),
                     -0.006 * abs(shift) - 0.002 * breath - 0.4 * crouch))
    b.pelvis = rot_y(-2.2 * shift * (1 - 0.6 * crouch)) @ rot_z(1.5 * micro([(1, 1, 1.1), (2, 0.4, 0.2)])) @ rot_x(4 * crouch)
    lean = 3.0 + 13.0 * crouch
    b.spine = pyr(pitch=lean * 0.55 - 0.5 * breath, roll=1.4 * shift)
    b.chest = pyr(pitch=lean * 0.45 - 0.9 * breath, yaw=0.8 * micro([(1, 1, 2.0), (3, 0.3, 0.5)]), roll=0.8 * shift)
    b.neck = pyr(pitch=-lean * 0.4 + 0.3 * breath)
    # glances: a slow look left, back, a quick check right, settle
    glance = keyed([(0.0, 0.0), (0.18, 0.0), (0.3, 9.0), (0.42, 9.0), (0.52, 0.0), (0.68, -6.0), (0.76, -6.0),
                    (0.86, 0.0), (1.0, 0.0)], (t % period) / period)
    b.head_world = rot_z(glance) @ rot_x(0.6 * breath + 1.2 * micro([(1, 1, 0.4), (2, 0.5, 1.7)]))
    b.weapon_pos = Vector((0.004 * micro([(1, 1, 0.9), (2, 0.5, 0.1)]), 0.0,
                           0.004 * breath + 0.003 * micro([(1, 1, 2.2)])))
    b.weapon_rot = pyr(pitch=0.8 * breath + 0.6 * micro([(2, 1, 1.3)]), yaw=0.7 * micro([(1, 1, 0.3), (3, 0.4, 1.0)]),
                       roll=1.2 * micro([(1, 1, 2.6)]) + 2.0 * crouch)
    for side, fm in feet.items():
        ground = Vector((fm.ankle.x * (1.0 + 0.25 * crouch), fm.ankle.y + (0.06 if side == "Left" else -0.03) * crouch, 0))
        yaw = (6.0 if side == "Left" else -6.0) + (14.0 if side == "Left" else 0.0) * crouch
        heel_up = 0.0
        if crouch:
            heel_up = 0.0
        ankle, fd = fm.ankle_at(ground, yaw, heel_up)
        b.feet[side] = (ankle, fd)
        out = -1 if side == "Right" else 1
        b.knee_dir[side] = (FWD + Vector((0.25 * out * (1 + crouch), 0, 0)) + UP * 0.2 * crouch).normalized()
    return b


def jump_frame(rig, feet, t):
    """Push-off -> tuck -> airborne, held. 0.75 s."""
    b = Body()
    # body arc relative to the game's root (which follows the real jump): legs fold up under the hips
    crouch = keyed([(0.0, 0.10), (0.07, 0.0), (0.12, -0.02), (0.3, 0.04), (0.75, 0.02)], t)
    b.hips = Vector((0, 0.0, -crouch))
    b.pelvis = rot_x(keyed([(0.0, 6.0), (0.08, -2.0), (0.3, 8.0), (0.75, 5.0)], t))
    lean = keyed([(0.0, 10.0), (0.08, 2.0), (0.3, 9.0), (0.75, 6.0)], t)
    b.spine = pyr(pitch=lean * 0.55)
    b.chest = pyr(pitch=lean * 0.45)
    b.neck = pyr(pitch=-lean * 0.4)
    b.head_world = rot_x(keyed([(0.0, 3.0), (0.1, -4.0), (0.3, 2.0), (0.75, 0.0)], t))
    # the gun rises a little with the push and settles in the air
    b.weapon_pos = Vector((0, keyed([(0.0, 0.0), (0.1, 0.01), (0.75, 0.005)], t), keyed([(0.0, -0.01), (0.1, 0.03), (0.35, 0.0), (0.75, 0.005)], t)))
    b.weapon_rot = pyr(pitch=keyed([(0.0, -2.0), (0.1, 5.0), (0.35, -1.0), (0.75, 1.0)], t),
                       roll=keyed([(0.0, 0.0), (0.12, -4.0), (0.75, -2.0)], t))
    # legs: ankle targets under the hips, rising (tuck) and reaching down again; feet pointed in the air
    for side, fm in feet.items():
        lead = side == "Right"
        tuck = keyed([(0.0, 0.0), (0.08, 0.02), (0.28, 0.30 if lead else 0.24), (0.5, 0.22 if lead else 0.15), (0.75, 0.12)], t)
        fwd = keyed([(0.0, 0.0), (0.28, 0.12 if lead else -0.02), (0.75, 0.06 if lead else -0.04)], t)
        roll = keyed([(0.0, 25.0), (0.08, 40.0), (0.3, 15.0), (0.75, 22.0)], t)
        ground = Vector((fm.ankle.x, fm.ankle.y - fwd, 0))
        ankle, fd = fm.ankle_at(ground, -4.0 if side == "Right" else 4.0, roll, tuck)
        b.feet[side] = (ankle, fd)
        out = -1 if side == "Right" else 1
        b.knee_dir[side] = (FWD + Vector((0.15 * out, 0, 0))).normalized()
    return b


DEATH_LEN = 1.4
# clearance of each bone's joints over the floor (half the body's thickness there)
CLEAR = {"Hips": 0.12, "Spine": 0.13, "Chest": 0.14, "Neck": 0.09, "Head": 0.1, "RightFoot": 0.05, "LeftFoot": 0.05,
         "RightHand": 0.04, "LeftHand": 0.04}


def death_frame(rig, feet, t):
    """Hit (head and chest snap back) -> knees give -> twist and fall back-right -> impact and a small rebound."""
    b = Body()
    b.ground_clamp = t > 0.45
    k = keyed
    # pelvis travels down and back; tips backward as the body falls
    drop = k([(0.0, 0.0), (0.12, 0.03), (0.42, 0.32), (0.78, 0.86), (0.86, 0.9), (0.95, 0.875), (1.1, 0.885), (1.4, 0.885)], t)
    back = k([(0.0, 0.0), (0.12, 0.04), (0.42, 0.12), (0.78, 0.30), (1.4, 0.32)], t)
    side = k([(0.0, 0.0), (0.42, -0.03), (0.78, -0.09), (1.4, -0.1)], t)
    b.hips = Vector((side, back, -drop))
    tilt = k([(0.0, 0.0), (0.12, -6.0), (0.42, -18.0), (0.78, -78.0), (0.86, -86.0), (0.95, -82.0), (1.1, -85.0), (1.4, -85.0)], t)
    twist = k([(0.0, 0.0), (0.12, 4.0), (0.42, -14.0), (0.78, -26.0), (1.4, -28.0)], t)
    roll = k([(0.0, 0.0), (0.42, 8.0), (0.78, 14.0), (1.4, 15.0)], t)
    b.pelvis = rot_z(twist) @ rot_x(tilt) @ rot_y(roll)
    # the hit: chest and head thrown back, then the spine folds forward as the knees go
    sp = k([(0.0, 0.0), (0.08, -14.0), (0.2, -6.0), (0.42, 14.0), (0.78, 4.0), (0.86, -6.0), (0.95, 2.0), (1.4, 0.0)], t)
    b.spine = pyr(pitch=sp * 0.5, yaw=k([(0.0, 0.0), (0.42, 10.0), (1.4, 8.0)], t), roll=k([(0.0, 0.0), (0.78, -6.0), (1.4, -5.0)], t))
    b.chest = pyr(pitch=sp * 0.5, yaw=k([(0.0, 0.0), (0.1, 6.0), (0.42, 8.0), (1.4, 6.0)], t))
    b.neck = pyr(pitch=k([(0.0, 0.0), (0.08, -22.0), (0.3, 10.0), (0.78, 4.0), (0.86, -14.0), (1.0, 6.0), (1.4, 8.0)], t))
    b.head = pyr(pitch=k([(0.0, 0.0), (0.08, -18.0), (0.3, 6.0), (0.86, -10.0), (1.0, 4.0), (1.4, 5.0)], t),
                 yaw=k([(0.0, 0.0), (0.42, 12.0), (0.86, 30.0), (1.4, 32.0)], t),
                 roll=k([(0.0, 0.0), (0.86, 10.0), (1.4, 12.0)], t))
    # arms let go of the weapon (the game hides it) and drop, then flop out on the floor.
    # Signs for limbs: + pitch swings a limb that points down backward / one that points forward downward.
    ra = k([(0.0, 0.0), (0.18, 1.0), (1.4, 1.0)], t)
    fl = k([(0.0, 0.0), (0.7, 0.0), (0.86, 1.0), (0.95, 0.8), (1.1, 0.9), (1.4, 0.9)], t)
    b.arms_free = {
        # lying on the back, "down along the body" is + pitch: the arms end up on the floor beside the torso
        "RightUpperArm": pyr(pitch=55 * ra + 38 * fl, yaw=-10 * ra - 15 * fl, roll=-15 * ra - 30 * fl),
        "RightLowerArm": pyr(pitch=-25 * ra + 12 * fl, roll=-10 * ra),
        "RightHand": pyr(pitch=25 * ra, roll=-20 * ra),
        "LeftUpperArm": pyr(pitch=45 * ra + 45 * fl, yaw=25 * ra + 10 * fl, roll=20 * ra + 35 * fl),
        "LeftLowerArm": pyr(pitch=-35 * ra + 15 * fl, roll=10 * ra),
        "LeftHand": pyr(pitch=20 * ra, roll=25 * ra),
    }
    # legs: feet stay planted while the knees give, then they come off the ground with the fall
    if t < 0.5:
        for s, fm in feet.items():
            ground = Vector((fm.ankle.x, fm.ankle.y + (0.06 if s == "Right" else 0.0) * smooth(t / 0.4), 0))
            heel_up = k([(0.0, 0.0), (0.42, 12.0 if s == "Right" else 6.0)], t)
            ankle, fd = fm.ankle_at(ground, -6.0 if s == "Right" else 8.0, heel_up)
            b.feet[s] = (ankle, fd)
            out = -1 if s == "Right" else 1
            b.knee_dir[s] = (FWD + Vector((0.35 * out, 0, 0))).normalized()
    else:
        u = smooth((t - 0.5) / 0.36)
        bounce = k([(0.78, 0.0), (0.86, 1.0), (0.95, 0.7), (1.1, 0.85), (1.4, 0.85)], t)
        # hip flexion is - pitch (the thigh points down), knee flexion + pitch
        b.legs_free = {
            "Right": {"RightUpperLeg": pyr(pitch=-lerp(55, 8, u) + 6 * bounce, roll=lerp(-4, -12, u)),
                      "RightLowerLeg": pyr(pitch=lerp(75, 14, u) - 6 * bounce),
                      "RightFoot": pyr(pitch=lerp(-10, 20, u))},
            "Left": {"LeftUpperLeg": pyr(pitch=-lerp(48, 40, u) + 8 * bounce, yaw=lerp(0, 12, u), roll=lerp(6, 22, u)),
                     "LeftLowerLeg": pyr(pitch=lerp(65, 92, u) - 10 * bounce),
                     "LeftFoot": pyr(pitch=lerp(-8, 25, u))},
        }
    return b


# ------------------------------------------------------------------ baking

def bake_clip(rig, name, frames, frame_fn, loop=True):
    """frames: number of intervals; frame_fn(t) -> Body. Keys every frame (exact curves)."""
    arm = rig.arm
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    prev = {}
    for f in range(frames + 1):
        t = f / FPS
        if loop and f == frames:
            t = 0.0
        body = frame_fn(t)
        if body.feet:
            settle_hips(rig, body)
        W = apply_body(rig, body)
        if getattr(body, "ground_clamp", False):
            # nothing goes into the floor: joints keep a body-thickness clearance, the pelvis rises to make it
            low = min(min(W[n].to_translation().z - CLEAR.get(n, 0.06), (W[n] @ Vector((0, (rig.T0[n] - rig.H0[n]).length, 0))).z - CLEAR.get(n, 0.06))
                      for n in W if not n.endswith("Grip"))
            if low < 0:
                body.hips.z -= low
                W = apply_body(rig, body)
        B = rig.basis(W)
        for bname in rig.order:
            pb = arm.pose.bones[bname]
            q = B[bname].to_quaternion()
            if bname in prev and prev[bname].dot(q) < 0:
                q.negate()
            prev[bname] = q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=1 + f)
            if bname == "Hips":
                pb.location = B[bname].to_translation()
                pb.keyframe_insert("location", frame=1 + f)
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    return act


def _fcurves(act):
    """F-curves of an action (Blender 4.4+ keeps them in layers / slots / channelbags)."""
    if hasattr(act, "fcurves") and len(getattr(act, "fcurves", [])):
        return list(act.fcurves)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                out.extend(bag.fcurves)
    return out


def make_all(rig):
    feet = {s: FootModel(rig, s) for s in ("Right", "Left")}
    clips = []
    clips.append(bake_clip(rig, "idle", 120, lambda t: idle_frame(rig, feet, t, 4.0)))
    clips.append(bake_clip(rig, "crouch", 120, lambda t: idle_frame(rig, feet, t, 4.0, crouch=1.0)))
    for gname in GAITS:
        for d in DIRECTIONS:
            g, frames = make_gait(gname, d)
            clips.append(bake_clip(rig, f"{gname}_{d}", frames, lambda t, g=g: locomotion_frame(rig, g, t, feet)))
    clips.append(bake_clip(rig, "jump", int(0.75 * FPS), lambda t: jump_frame(rig, feet, t), loop=False))
    clips.append(bake_clip(rig, "death", int(DEATH_LEN * FPS), lambda t: death_frame(rig, feet, t), loop=False))
    rig.arm.animation_data.action = None
    return clips


# ------------------------------------------------------------------ armature + export

def build_rig(name):
    import vexa_human as H
    from vexa_characters import build_armature
    body, joint = H.build_body()
    posed = H.pose_body(body, joint)
    bpy.data.objects.remove(body)
    layout = dict(H.game_layout(posed))
    for side in ("Right", "Left"):
        g = posed[side + "Grip"][0]
        layout[side + "Grip"] = (tuple(g), tuple(g + Vector((0, -0.06, 0))), side + "Hand")
    arm = build_armature(name, layout)
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


def export_anims(path, arm):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # the file's default pose must be the rest pose (it is the bind pose for importers; no mesh carries one here)
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    # key times are written with the scene frame rate (Blender's default is 24)
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.render.fps_base = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
        bake_space_transform=False, object_types={"ARMATURE"}, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, bake_anim_step=1.0)


def main():
    from vexa_common import reset_scene
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    factions = [f for f in args.only.split(",") if f] or list(FACTIONS)
    for faction in factions:
        reset_scene()
        arm = build_rig(faction)
        rig = Rig(arm)
        clips = make_all(rig)
        export_anims(os.path.join(args.out, "Characters", "Anims", faction + ".fbx"), arm)
        print(f"{faction}: {len(clips)} clips", flush=True)


if __name__ == "__main__":
    main()
