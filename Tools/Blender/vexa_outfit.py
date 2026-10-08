"""
Clothing and gear fitted onto the posed vexa_human body. Everything here is the HIGH-poly source: garments are
cut from the body surface (by the A-pose "rest" coordinates and the dominant bone), inflated, relaxed, kept above
the skin and given cloth folds (elbows, armpits, waist, blousing above the boots) and seams; hard gear (plates,
pouches, helmet, boots soles) is built conforming to the garment surfaces with ray casts. The game meshes are
decimated from this and the textures are baked from it (vexa_characters).

Blender space: forward -Y, right -X, up +Z, meters.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

import vexa_human as H
from vexa_common import custom_mat, mat

BONE = {n: i for i, n in enumerate(H.GAME_BONES)}

# fabric subdivision of the high-poly garments (2 = ~4 mm edges on the torso)
SUB = {"level": 2}


# ---------------------------------------------------------------- mesh utilities

def _attr(me, name):
    a = me.attributes[name]
    n = len(a.data)
    if a.data_type == "FLOAT_VECTOR":
        buf = np.zeros(n * 3, dtype=np.float32)
        a.data.foreach_get("vector", buf)
        return buf.reshape(n, 3)
    buf = np.zeros(n, dtype=np.int32 if a.data_type == "INT" else np.float32)
    a.data.foreach_get("value", buf)
    return buf


def _link(name, me):
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return o


def _apply(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def _bvh(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.transform(o.matrix_world)
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


def bvh_of(objs):
    bm = bmesh.new()
    for o in objs:
        tmp = bmesh.new()
        tmp.from_mesh(o.data)
        tmp.transform(o.matrix_world)
        me = bpy.data.meshes.new("_tmp")
        tmp.to_mesh(me)
        tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    t = BVHTree.FromBMesh(bm)
    bm.free()
    return t


def straighten_boundary(bm, iters=12):
    """Relaxes open edges along themselves: a cut that follows the mesh grid in steps becomes a smooth hem line."""
    bverts = [v for v in bm.verts if v.is_boundary]
    for _ in range(iters):
        new = []
        for v in bverts:
            nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(nb) == 2:
                new.append((v, v.co * 0.5 + (nb[0].co + nb[1].co) * 0.25))
        for v, c in new:
            v.co = c


def extract(src, vmask, name, material, straighten=12):
    """New object from the faces of `src` whose vertices are all in vmask (bool array)."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.verts.ensure_lookup_table()
    kill = [f for f in bm.faces if not all(vmask[v.index] for v in f.verts) or f.material_index != 0]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    straighten_boundary(bm, straighten)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(material)
    for p in me.polygons:
        p.material_index = 0
        p.use_smooth = True
    return _link(name, me)


def subdivide(o, levels):
    if levels <= 0:
        return
    m = o.modifiers.new("Sub", "SUBSURF")
    m.levels = m.render_levels = levels
    m.boundary_smooth = "PRESERVE_CORNERS"
    _apply(o)


def _boundary_rings(bm, limit):
    """Edge-hop distance of every vertex from the nearest open edge (capped at limit)."""
    dist = {v.index: limit for v in bm.verts}
    front = [v for v in bm.verts if v.is_boundary]
    for v in front:
        dist[v.index] = 0
    d = 0
    while front and d < limit:
        d += 1
        nxt = []
        for v in front:
            for e in v.link_edges:
                w = e.other_vert(v)
                if dist[w.index] > d:
                    dist[w.index] = d
                    nxt.append(w)
        front = nxt
    return dist


CLEARANCE = {"bvh": None}


def inflate(o, offset_fn, relax=4, keep=None, min_gap=0.004, hug=0, clearance=True):
    """Moves vertices out along their normals by offset_fn(co, rest) and relaxes the surface (removes anatomy),
    then keeps every vertex at least min_gap above the `keep` surface (a BVHTree).
    Where another part of the body is close in front of a vertex (inner thighs, armpits, arm against the
    torso) the offset is limited to a fraction of that gap, so neighboring tubes don't grow into each other."""
    me = o.data
    rest = _attr(me, "rest") if "rest" in me.attributes else None
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    ring = _boundary_rings(bm, hug)
    clear = CLEARANCE["bvh"] if clearance else None
    moves = []
    for v in bm.verts:
        r = Vector(rest[v.index]) if rest is not None else v.co
        # hems, cuffs and collars hug the skin: the offset fades in over `hug` edge rings from the open edge
        k = 1.0 if hug <= 0 else 0.35 + 0.65 * min(1.0, ring[v.index] / hug)
        d = offset_fn(v.co, r) * k
        if clear is not None and d > 0.004:
            loc, nrm, _, dist = clear.ray_cast(v.co + v.normal * 0.004, v.normal, 0.15)
            if loc is not None:
                d = min(d, max(0.002, (dist + 0.004) * 0.35))
        moves.append(v.normal * d)
    for v, mv in zip(bm.verts, moves):
        v.co = v.co + mv
    taubin(bm, relax)
    if keep is not None:
        _keep_above(bm, keep, min_gap)
    bm.to_mesh(me)
    bm.free()


def taubin(bm, iters, lam=0.55, mu=-0.58):
    """Taubin smoothing: removes bumps (anatomy under cloth) without shrinking the surface. Boundaries stay."""
    if iters <= 0:
        return
    bm.verts.ensure_lookup_table()
    n = len(bm.verts)
    P = np.array([v.co[:] for v in bm.verts])
    E = np.array([(e.verts[0].index, e.verts[1].index) for e in bm.edges], dtype=np.int64)
    fixed = np.array([v.is_boundary for v in bm.verts])
    deg = np.zeros(n)
    np.add.at(deg, E[:, 0], 1)
    np.add.at(deg, E[:, 1], 1)
    deg = np.maximum(deg, 1)[:, None]
    for _ in range(iters):
        for f in (lam, mu):
            S = np.zeros_like(P)
            np.add.at(S, E[:, 0], P[E[:, 1]])
            np.add.at(S, E[:, 1], P[E[:, 0]])
            D = S / deg - P
            D[fixed] = 0
            P = P + f * D
    for v, p in zip(bm.verts, P):
        v.co = p


def _keep_above(bm, keep, min_gap):
    """Pushes vertices out to at least min_gap above the `keep` surface. keep may be a list of BVH trees (layers,
    inner first): each layer is enforced on its own, so a point between two layers can't hide under the outer one."""
    if isinstance(keep, (list, tuple)):
        for k in keep:
            _keep_above(bm, k, min_gap)
        return
    for v in bm.verts:
        # only nearby surfaces matter: a layer that ends somewhere else (a shirt hem far above the knees) must not
        # pull the fabric toward it
        loc, nrm, _, dist = keep.find_nearest(v.co, 0.04)
        if loc is None:
            continue
        h = (v.co - loc).dot(nrm)
        if h < min_gap:
            v.co = v.co + nrm * (min_gap - h)


def displace(o, fn, keep=None, min_gap=0.002):
    """v += normal * fn(co, normal, rest)."""
    me = o.data
    rest = _attr(me, "rest") if "rest" in me.attributes else None
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    moves = []
    for v in bm.verts:
        r = Vector(rest[v.index]) if rest is not None else v.co
        moves.append(v.normal * fn(v.co, v.normal, r))
    for v, d in zip(bm.verts, moves):
        v.co += d
    if keep is not None:
        _keep_above(bm, keep, min_gap)
    bm.to_mesh(me)
    bm.free()


def thicken(o, t, rim=True, offset=-1.0):
    m = o.modifiers.new("Solid", "SOLIDIFY")
    m.thickness = t
    m.offset = offset
    m.use_rim = rim
    m.use_even_offset = False
    _apply(o)


def shade(o):
    for p in o.data.polygons:
        p.use_smooth = True


# ---------------------------------------------------------------- cloth folds

def _seed(s):
    return Vector((s * 1.37, s * 2.11, s * 0.73))


def folds_ring(co, a, b, t0, half, wavelength, amp, inner=None, seed=0.0):
    """Folds that ring a limb (axis a->b) around axial position t0 (meters from a), within +-half."""
    ab = b - a
    L = ab.length
    if L < 1e-6:
        return 0.0
    u = ab / L
    t = (co - a).dot(u)
    dt = t - t0
    if abs(dt) > half:
        return 0.0
    radial = co - a - u * t
    rl = radial.length
    rdir = radial / rl if rl > 1e-6 else Vector((0, 0, 1))
    fall = 0.5 + 0.5 * math.cos(math.pi * dt / half)
    side = 1.0
    if inner is not None:
        side = 0.25 + 0.75 * max(0.0, rdir.dot(inner)) ** 0.7
    ph = noise.noise(co * 22 + _seed(seed)) * 2.2 + noise.noise(co * 7 + _seed(seed + 3)) * 1.5
    w = abs(math.sin(math.pi * dt / wavelength + ph))
    return amp * fall * side * (w - 0.55)


def folds_dir(co, center, direction, across, half_len, half_width, wavelength, amp, seed=0.0):
    """Parallel folds running along `direction`, stacked across `across` (e.g. diagonal pull folds)."""
    d = co - center
    s = d.dot(direction)
    w = d.dot(across)
    if abs(s) > half_len or abs(w) > half_width:
        return 0.0
    fall = (0.5 + 0.5 * math.cos(math.pi * s / half_len)) * (0.5 + 0.5 * math.cos(math.pi * w / half_width))
    ph = noise.noise(co * 18 + _seed(seed)) * 1.8
    return amp * fall * (abs(math.sin(math.pi * w / wavelength + ph)) - 0.55)


def groove(co, a, b, t0, width, depth):
    """A seam: narrow groove ringing the axis a->b at t0."""
    ab = b - a
    u = ab.normalized()
    dt = (co - a).dot(u) - t0
    if abs(dt) > width:
        return 0.0
    return -depth * (0.5 + 0.5 * math.cos(math.pi * dt / width))


def fabric_noise(co, amp, scale=9.0, seed=0.0):
    return amp * noise.fractal(co * scale + _seed(seed), 0.6, 2.0, 3)


# ---------------------------------------------------------------- oriented hard gear

def frame_from(normal, up):
    n = Vector(normal).normalized()
    u = Vector(up)
    u = (u - n * u.dot(n)).normalized()
    x = u.cross(n).normalized()
    return Matrix((x, n, u)).transposed()  # columns: width, thickness (out), height


def obox(name, center, normal, up, size, material, round_=0.3, sub=2, taper=0.0):
    """Soft box oriented on a surface: size = (width, thickness, height); its back face sits at `center`."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = name
    me = o.data
    for v in me.vertices:
        x, y, z = v.co
        x *= size[0]
        y = (y + 0.5) * size[1]
        z *= size[2]
        if taper:
            x *= 1.0 - taper * (y / size[1])
        v.co = (x, y, z)
    R = frame_from(normal, up)
    o.matrix_world = Matrix.Translation(Vector(center)) @ R.to_4x4()
    bv = o.modifiers.new("Bevel", "BEVEL")
    bv.width = min(size) * round_
    bv.segments = 3
    if sub:
        ss = o.modifiers.new("Sub", "SUBSURF")
        ss.levels = ss.render_levels = sub
    me.materials.append(material)
    _apply(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade(o)
    return o


def ocyl(name, a, b, r, material, verts=24, bevel=0.3, sub=1):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length)
    o = bpy.context.active_object
    o.name = name
    o.location = (a + b) / 2
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bv = o.modifiers.new("Bevel", "BEVEL")
    bv.width = r * bevel
    bv.segments = 3
    bv.limit_method = "ANGLE"
    if sub:
        ss = o.modifiers.new("Sub", "SUBSURF")
        ss.levels = ss.render_levels = sub
    o.data.materials.append(material)
    _apply(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    shade(o)
    return o


def tube_path(name, pts, r, material, closed=False):
    """Round tube swept along a point path (edge binding, cables)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = 3
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for p, q in zip(sp.points, pts):
        p.co = (q.x, q.y, q.z, 1.0)
    sp.use_cyclic_u = closed
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    o.data.materials.clear()
    o.data.materials.append(material)
    shade(o)
    return o


def hit(bvh, origin, direction):
    loc, nrm, _, _ = bvh.ray_cast(Vector(origin), Vector(direction).normalized(), 2.0)
    return loc, nrm


# ---------------------------------------------------------------- outfit builder

class Outfit:
    def __init__(self, body, posed, rest_lm, mats):
        self.body = body
        self.P = {k: v[0] for k, v in posed.items()}
        self.T = {k: v[1] for k, v in posed.items() if v[1] is not None}
        self.R = rest_lm
        self.m = mats
        self.parts = []
        self.jiggles = []
        self.covered = np.zeros(len(body.data.vertices), dtype=bool)
        me = body.data
        self.rest = _attr(me, "rest")
        self.bone = _attr(me, "bone")
        self.co = np.array([v.co[:] for v in me.vertices])
        self.skin = _bvh(body)
        CLEARANCE["bvh"] = self.skin
        # chest frame (posed): up along the chest, right toward the character's right shoulder
        up = (self.T["Chest"] - self.P["Chest"]).normalized()
        r = (self.P["RightUpperArm"] - self.P["LeftUpperArm"])
        r = (r - up * r.dot(up)).normalized()
        self.cu, self.cr = up, r
        self.cf = up.cross(r).normalized()

    # -- masks on the body (rest space) --
    def _edges(self):
        if not hasattr(self, "_E"):
            E = np.zeros(len(self.body.data.edges) * 2, dtype=np.int64)
            self.body.data.edges.foreach_get("vertices", E)
            self._E = E.reshape(-1, 2)
        return self._E

    def close_mask(self, m, rings=2):
        """Morphological closing (dilate, then erode) over the mesh: fills pinholes in a garment region, whose
        edges would otherwise be pinned in the cloth solve and pull fins out of the fabric."""
        E = self._edges()
        m = m.copy()
        for _ in range(rings):
            grow = m.copy()
            grow[E[m[E[:, 0]], 1]] = True
            grow[E[m[E[:, 1]], 0]] = True
            m = grow
        for _ in range(rings):
            shrink = m.copy()
            shrink[E[~m[E[:, 0]], 1]] = False
            shrink[E[~m[E[:, 1]], 0]] = False
            m = shrink
        return m

    def bones(self, *names):
        idx = [BONE[n] for n in names]
        return np.isin(self.bone, idx)

    def rz(self):
        return self.rest[:, 2]

    def near_rest(self, point, radius):
        return np.linalg.norm(self.rest - np.asarray(point), axis=1) < radius

    def along(self, a, b):
        """Rest-space projection (meters from a) of every vertex on the axis a->b."""
        a, b = np.asarray(a), np.asarray(b)
        u = (b - a) / np.linalg.norm(b - a)
        return (self.rest - a) @ u

    def cover(self, vmask, erode=1):
        """Marks body vertices hidden under a garment (shrunk by `erode` rings so no gap opens at the edges)."""
        m = vmask.copy()
        if erode:
            bm = bmesh.new()
            bm.from_mesh(self.body.data)
            bm.verts.ensure_lookup_table()
            for _ in range(erode):
                edge = np.zeros_like(m)
                for v in bm.verts:
                    if m[v.index] and any(not m[e.other_vert(v).index] for e in v.link_edges):
                        edge[v.index] = True
                m &= ~edge
            bm.free()
        self.covered |= m

    def add(self, o):
        shade(o)
        self.parts.append(o)
        return o

    def jiggle(self, objs, name, root, tip, parent, stiffness=0.5):
        """Marks hanging parts for secondary motion: a "Jiggle_<name>" bone from root to tip (simulated in the game by
        Client/Art/SpringBones.cs) with weights growing from the root to the tip."""
        root, tip = Vector(root), Vector(tip)
        ax = tip - root
        L2 = max(ax.length_squared, 1e-8)
        for o in objs:
            vg = o.vertex_groups.new(name="Jiggle_" + name)
            for v in o.data.vertices:
                t = max(0.0, min(1.0, (v.co - root).dot(ax) / L2))
                vg.add([v.index], t ** 0.8, "REPLACE")
        self.jiggles.append(("Jiggle_" + name, root, tip, parent, stiffness))

    # ------------------------------------------------------------ garments

    def garment(self, name, vmask, material, offset, relax=4, sub=None, folds=None, thick=0.0025, min_gap=0.003, erode=1, keep=None, over=(), hug=3,
                drape=None, clearance=True):
        """over: garments this one is worn on top of (it stays above them as well as the skin).
        drape: cloth-simulate the garment onto the body (dict of vexa_cloth.drape settings); its open edges are pinned."""
        if over:
            keep = [self.skin] + [_bvh(g) for g in over]
        vmask = self.close_mask(vmask) & (self.bone >= 0)
        o = extract(self.body, vmask, name, material)
        inflate(o, offset if callable(offset) else (lambda co, r, k=offset: k), relax=relax, keep=keep or self.skin, min_gap=min_gap, hug=hug,
                clearance=clearance)
        levels = SUB["level"] if sub is None else sub
        if drape is not None:
            import vexa_cloth
            subdivide(o, 1)
            settings = dict(drape)
            rings = settings.pop("rings", 1)
            pins = vexa_cloth.boundary_pins(o, rings)
            vexa_cloth.drape(o, [self.body] + list(over), pins, **settings)
            subdivide(o, max(0, levels - 1))
        else:
            subdivide(o, levels)
        inflate(o, lambda co, r: 0.0, relax=1 if drape is None else 0, keep=keep or self.skin, min_gap=min_gap)
        if folds:
            displace(o, folds, keep=keep or self.skin, min_gap=min_gap * 0.6)
        if thick:
            thicken(o, thick)
        self.cover(vmask, erode)
        return self.add(o)

    def arm_folds(self, co, n, r, amp=1.0, seed=0.0):
        d = 0.0
        for side in ("Right", "Left"):
            sh, el, wr = self.P[side + "UpperArm"], self.P[side + "LowerArm"], self.P[side + "Hand"]
            inner = ((sh - el).normalized() + (wr - el).normalized()).normalized()
            d += folds_ring(co, sh, wr, (el - sh).length, 0.09, 0.022, 0.006 * amp, inner, seed)
            # bunching above the cuff
            d += folds_ring(co, el, wr, (wr - el).length - 0.06, 0.05, 0.016, 0.0035 * amp, None, seed + 1)
            # armpit pull folds toward the chest
            d += folds_ring(co, sh, el, 0.05, 0.06, 0.03, 0.004 * amp, None, seed + 2)
            # shoulder seam
            d += groove(co, sh, el, 0.035, 0.004, 0.0012)
        return d

    def torso_folds(self, co, n, r, amp=1.0, seed=0.0):
        d = 0.0
        # diagonal pull from each armpit toward the sternum (arms forward)
        for side, s in (("Right", 1), ("Left", -1)):
            ap = self.P[side + "UpperArm"] - self.cu * 0.09 + self.cf * 0.02
            target = self.P["Chest"] + self.cf * 0.1 + self.cu * 0.05
            dirn = (target - ap).normalized()
            across = dirn.cross(self.cf).normalized()
            d += folds_dir(co, (ap + target) / 2, dirn, across, 0.13, 0.08, 0.035, 0.004 * amp, seed + s)
        # blousing above the belt
        waist = self.P["Spine"] + self.cu * -0.02
        d += folds_ring(co, waist - self.cu * 0.1, waist + self.cu * 0.25, 0.12, 0.07, 0.03, 0.005 * amp, None, seed + 5)
        return d

    def leg_folds(self, co, n, r, amp=1.0, seed=0.0, boot_top=0.24):
        d = 0.0
        for side in ("Right", "Left"):
            hip, kn, an = self.P[side + "UpperLeg"], self.P[side + "LowerLeg"], self.P[side + "Foot"]
            # stacking above the boots (bloused trousers)
            d += folds_ring(co, an, kn, boot_top - an.z + 0.04, 0.07, 0.025, 0.007 * amp, None, seed)
            # behind the knee and at the crotch
            back = Vector((0, 1, 0))
            d += folds_ring(co, hip, an, (kn - hip).length, 0.07, 0.03, 0.005 * amp, back, seed + 1)
            d += folds_ring(co, hip, kn, 0.06, 0.06, 0.035, 0.004 * amp, None, seed + 2)
            # outer seam
            out = Vector((-1 if side == "Right" else 1, 0, 0))
            rad = (co - hip)
            if rad.normalized().dot(out) > 0.97:
                d -= 0.001
        return d

    # ------------------------------------------------------------ pieces

    def shirt(self, material, offset=0.008, collar=True, loose=1.0, sleeve_end=0.045, over=(), hem=None, rolled=False, drape=None):
        """Shirt / jacket. rolled: sleeves rolled up to below the elbow (sleeve_end is then measured from the wrist)."""
        R = self.R
        z = self.rz()
        m = self.bones("Hips", "Spine", "Chest", "Neck", "RightUpperArm", "RightLowerArm", "LeftUpperArm", "LeftLowerArm")
        m &= z < R["neck"][2] + (0.035 if collar else 0.0)
        m &= z > (R["hip_r"][2] - 0.02 if hem is None else hem)
        for s in "rl":
            near_wrist = self.along(R["wrist_" + s], R["elbow_" + s]) < sleeve_end
            arm = self.bones("RightLowerArm" if s == "r" else "LeftLowerArm")
            m &= ~(arm & near_wrist)
        def off(co, r):
            k = offset * loose
            # looser at the belly and the sleeves, tighter over the shoulders
            if r.z < R["waist"][2] + 0.05:
                k += 0.006 * loose
            elif r.z > R["chest"][2]:
                k += 0.003 * loose
            return k
        fold = lambda co, n, r: self.arm_folds(co, n, r, 0.8 * loose) + self.torso_folds(co, n, r, 0.8 * loose) + fabric_noise(co, 0.0006 * loose, 6.0)
        if drape is not None:
            # the solver makes the drape folds; only light compression folds at the joints are added on top
            fold = lambda co, n, r: 0.45 * (self.arm_folds(co, n, r, loose) + self.torso_folds(co, n, r, loose)) + fabric_noise(co, 0.0003 * loose, 6.0)
        sh = self.garment("Shirt", m, material, off, relax=int(8 + 22 * (loose - 1.0) / 0.35) if drape is None else 4, folds=fold, thick=0.003,
                          over=over, drape=drape)
        if rolled:
            for s_ in "rl":
                wr, el = np.asarray(R["wrist_" + s_]), np.asarray(R["elbow_" + s_])
                u = (el - wr) / np.linalg.norm(el - wr)
                bone = "RightLowerArm" if s_ == "r" else "LeftLowerArm"
                def cuff(co, rr, wr=wr, u=u):
                    t = float((np.asarray(rr[:]) - wr) @ u)
                    return sleeve_end - 0.004 < t < sleeve_end + 0.055
                self.band_rest(("RightCuff" if s_ == "r" else "LeftCuff"), material, sh, cuff, 0.004, 0.008, relax=4,
                               bones=(bone, "RightUpperArm" if s_ == "r" else "LeftUpperArm"))
        return sh

    def pants(self, material, offset=0.009, boot_top=0.25, over=(), drape=None):
        R = self.R
        z = self.rz()
        m = self.bones("Hips", "Spine", "RightUpperLeg", "LeftUpperLeg", "RightLowerLeg", "LeftLowerLeg")
        m &= z < R["waist"][2] + 0.035
        m &= z > R["ankle_r"][2] + (boot_top - 0.17)
        def off(co, r):
            k = offset
            if r.z < R["knee_r"][2] + 0.1:
                k += 0.01  # bloused above the boots
            return k
        fold = lambda co, n, r: self.leg_folds(co, n, r, 0.85, boot_top=boot_top) + fabric_noise(co, 0.0007, 6.0, seed=2)
        if drape is not None:
            fold = lambda co, n, r: 0.45 * self.leg_folds(co, n, r, 0.85, boot_top=boot_top) + fabric_noise(co, 0.0003, 6.0, seed=2)
        return self.garment("Pants", m, material, off, relax=7 if drape is None else 4, folds=fold, thick=0.003, min_gap=0.004, over=over,
                            drape=drape)

    def gloves(self, material, cuff=0.07):
        R = self.R
        m = self.bones("RightHand", "LeftHand")
        for s in "rl":
            arm = self.bones("RightLowerArm" if s == "r" else "LeftLowerArm")
            m |= arm & (self.along(R["wrist_" + s], R["elbow_" + s]) < cuff)
        fold = lambda co, n, r: fabric_noise(co, 0.0005, 30, seed=4)
        g = self.garment("Gloves", m, material, 0.0022, relax=1, sub=1, folds=fold, thick=0.0015, min_gap=0.0016, erode=0)
        return g

    def boots(self, material, sole_mat, top=0.25):
        R = self.R
        z = self.rz()
        m = self.bones("RightFoot", "LeftFoot") | (self.bones("RightLowerLeg", "LeftLowerLeg") & (z < R["ankle_r"][2] + top - 0.07))
        ank_r, toe_r = Vector(R["ankle_r"]), Vector(R["toe_r"])
        fwd = (toe_r - ank_r)
        fwd.z = 0
        fwd.normalize()

        b = self.boot_shell(m, material)
        # flat walking surface: the boot bottom becomes the top of the sole
        for v in b.data.vertices:
            if v.co.z < H.SOLE + 0.012:
                v.co.z = max(v.co.z, H.SOLE)
        # rubber soles from each footprint
        for side in ("Right", "Left"):
            fx = self.P[side + "Foot"].x
            pts = [v.co.copy() for v in b.data.vertices if v.co.z < H.SOLE + 0.02 and (v.co.x - fx) * (1 if fx > 0 else -1) > -0.09]
            pts = [p for p in pts if abs(p.x - fx) < 0.09]
            if len(pts) < 8:
                continue
            self.add(self.sole(side + "Sole", pts, sole_mat))
            self.laces(side, b)
        return b

    def boot_shell(self, m, material, gap=0.007):
        """Boots built from the convex hull of each foot and ankle, remeshed into a clean, even surface: a smooth
        leather shell with a roomy toe box and a flat sole line (no toes, no arch showing), open at the top."""
        m = self.close_mask(m) & (self.bone >= 0)
        top_z = self.co[m][:, 2].max()
        shells = []
        for sgn in (-1, 1):
            side = m & (np.sign(self.co[:, 0]) == sgn)
            ank_z = self.P["RightFoot" if sgn < 0 else "LeftFoot"].z
            # two hulls fused by the remesh: the foot (instep, toe box, heel) and the shaft around the ankle
            bm = bmesh.new()
            for part in (side & (self.co[:, 2] < ank_z + 0.015), side & (self.co[:, 2] > ank_z - 0.035)):
                sub = bmesh.new()
                for p in self.co[part]:
                    sub.verts.new(p)
                res = bmesh.ops.convex_hull(sub, input=sub.verts, use_existing_faces=False)
                bmesh.ops.delete(sub, geom=[g for g in res["geom_interior"] if isinstance(g, bmesh.types.BMVert)], context="VERTS")
                bmesh.ops.delete(sub, geom=[v for v in sub.verts if not v.link_faces], context="VERTS")
                bmesh.ops.recalc_face_normals(sub, faces=sub.faces)
                tmp = bpy.data.meshes.new("_hull")
                sub.to_mesh(tmp)
                sub.free()
                bm.from_mesh(tmp)
                bpy.data.meshes.remove(tmp)
            me = bpy.data.meshes.new("BootHull")
            bm.to_mesh(me)
            bm.free()
            o = _link("BootHull", me)
            rm = o.modifiers.new("Remesh", "REMESH")
            rm.mode = "VOXEL"
            rm.voxel_size = 0.0045
            _apply(o)
            bm = bmesh.new()
            bm.from_mesh(o.data)
            taubin(bm, 6)
            bm.normal_update()
            c = sum((v.co for v in bm.verts), Vector()) / max(1, len(bm.verts))
            for v in bm.verts:
                n = v.normal if (v.co - c).dot(v.normal) >= 0 else -v.normal
                v.co = v.co + n * gap
            # open the top of the shaft; flat sole line
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z > top_z], context="FACES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            for v in bm.verts:
                if v.co.z < H.SOLE + 0.004:
                    v.co.z = H.SOLE
            straighten_boundary(bm, 6)
            bm.to_mesh(o.data)
            bm.free()
            o.data.materials.clear()
            o.data.materials.append(material)
            shells.append(o)
        bpy.ops.object.select_all(action="DESELECT")
        for o in shells:
            o.select_set(True)
        bpy.context.view_layer.objects.active = shells[0]
        bpy.ops.object.join()
        o = bpy.context.active_object
        o.name = "Boots"
        o.data.name = "Boots"
        inflate(o, lambda co, r: 0.0, relax=0, keep=self.skin, min_gap=0.005, clearance=False)
        displace(o, lambda co, n, r: (fabric_noise(co, 0.0008, 14, 7) if co.z > 0.1 else 0.0) +
                 folds_ring(co, self.P["RightFoot"] if co.x < 0 else self.P["LeftFoot"],
                            self.P["RightLowerLeg"] if co.x < 0 else self.P["LeftLowerLeg"], 0.03, 0.03, 0.012, 0.0015))
        thicken(o, 0.0035)
        self.cover(m, erode=1)
        return self.add(o)

    def sole(self, name, pts, material):
        from mathutils.geometry import convex_hull_2d
        xy = [(p.x, p.y) for p in pts]
        hull = convex_hull_2d(xy)
        ring = [Vector((xy[i][0], xy[i][1])) for i in hull]
        c = sum(ring, Vector((0, 0))) / len(ring)
        ring = [c + (p - c) * 1.04 + (p - c).normalized() * 0.004 for p in ring]
        bm = bmesh.new()
        bottom = [bm.verts.new((p.x, p.y, 0.0)) for p in ring]
        top = [bm.verts.new((p.x, p.y, H.SOLE + 0.006)) for p in ring]
        bm.faces.new(list(reversed(bottom)))
        bm.faces.new(top)
        n = len(ring)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = _link(name, me)
        me.materials.append(material)
        bv = o.modifiers.new("Bevel", "BEVEL")
        bv.width = 0.006
        bv.segments = 3
        rm = o.modifiers.new("Tri", "TRIANGULATE")
        _apply(o)
        sub = o.modifiers.new("Sub", "SUBSURF")
        sub.levels = 1
        _apply(o)
        # heel step and tread ridges
        for v in o.data.vertices:
            if v.co.z < 0.004:
                ridge = 0.5 + 0.5 * math.sin(v.co.y * 260)
                v.co.z = 0.0015 * ridge
        return o

    def laces(self, side, boot):
        ank, toe = self.P[side + "Foot"], self.T[side + "Foot"]
        bvh = _bvh(boot)
        for i in range(6):
            t = 0.12 + i * 0.12
            p = ank + (toe - ank) * t * 0.85
            p.z = max(p.z, 0.05) + 0.06 - i * 0.008
            loc, nrm = hit(bvh, p + Vector((0, 0, 0.2)), Vector((0, 0, -1)))
            if loc is None:
                continue
            fwd = (toe - ank).normalized()
            self.add(obox(f"{side}Lace{i}", loc - nrm * 0.001, nrm, fwd, (0.034, 0.004, 0.006), self.m["sole"], 0.45, 1))

    def belt(self, material, buckle_mat, z_off=0.0, over=()):
        R = self.R
        z = self.rz()
        m = self.bones("Hips", "Spine") & (np.abs(z - (R["waist"][2] - 0.035 + z_off)) < 0.023)
        b = extract(self.body, m, "Belt", material)
        subdivide(b, 1)
        inflate(b, lambda co, r: 0.02, relax=3, keep=[self.skin] + [_bvh(g) for g in over] if over else self.skin, min_gap=0.006 if over else 0.019)
        thicken(b, 0.005, offset=0.0)
        self.add(b)
        bvh = _bvh(b)
        c = self.P["Hips"] + Vector((0, -0.3, 0.05 + z_off))
        loc, nrm = hit(bvh, c, Vector((0, 1, 0)))
        if loc:
            self.add(obox("Buckle", loc, nrm, (0, 0, 1), (0.055, 0.008, 0.04), buckle_mat, 0.2, 1))
            self.add(obox("BuckleBar", loc + nrm * 0.008, nrm, (0, 0, 1), (0.012, 0.004, 0.034), buckle_mat, 0.3, 1))
        return b

    # ------------------------------------------------------------ head gear

    def face_mask(self):
        R = self.R
        return self.bones("Head", "Neck")

    def balaclava(self, material, holes="eyes"):
        """Knit balaclava over head and neck. holes: "eyes" (two eye holes) or "face" (an oval opening, worn under a mask)."""
        R = self.R
        z = self.rz()
        rest = self.rest
        m = self.bones("Head", "Neck") & (z > R["neck"][2] - 0.045)
        eye_z = (R["eye_r"][2] + R["eye_l"][2]) / 2
        eye_y = (R["eye_r"][1] + R["eye_l"][1]) / 2
        front = rest[:, 1] < eye_y + 0.02
        if holes == "eyes":
            opening = np.zeros(len(rest), dtype=bool)
            for e in (R["eye_r"], R["eye_l"]):
                ex = ((rest[:, 0] - e[0]) / 0.026) ** 2 + ((rest[:, 2] - (e[2] + 0.002)) / 0.0155) ** 2
                opening |= (ex < 1.0) & front
        else:
            opening = (((rest[:, 0]) / 0.07) ** 2 + ((rest[:, 2] - (eye_z - 0.035)) / 0.075) ** 2 < 1.0) & front
        m &= ~opening
        fold = lambda co, n, r: fabric_noise(co, 0.0006, 16, 9) + (folds_ring(co, self.P["Neck"], self.P["Head"], 0.04, 0.05, 0.02, 0.0035)
                                                                   if r.z < R["head"][2] + 0.01 else 0.0)
        return self.garment("Balaclava", m, material, 0.0032, relax=2, sub=1, folds=fold, thick=0.002, min_gap=0.0028, erode=1, hug=0)

    def face_shell(self, name, material, face_mask, keep, pad=0.012, thickness=0.006):
        """Smooth molded shell over the face: an ellipsoid fitted to the face region (cheeks to nose tip, brow to
        chin), front half only, kept clear of the skin."""
        hu = (self.T["Head"] - self.P["Head"]).normalized()
        hf = Vector((0, -1, 0))
        hf = (hf - hu * hf.dot(hu)).normalized()
        hr = hf.cross(hu).normalized()
        Rm = Matrix((hr, hf, hu)).transposed()
        o = self.P["Head"]
        loc = (self.co[face_mask] - np.array(o[:])) @ np.array(Rm)
        z0 = (loc[:, 2].min() + loc[:, 2].max()) / 2
        zr = (loc[:, 2].max() - loc[:, 2].min()) / 2 + pad
        y0 = np.percentile(loc[:, 1], 4)
        yr = loc[:, 1].max() - y0 + pad
        xr = np.abs(loc[:, 0]).max() + pad
        c = o + Rm @ Vector((0.0, y0, z0))
        bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48, radius=1.0)
        sh = bpy.context.active_object
        sh.name = name
        bm = bmesh.new()
        bm.from_mesh(sh.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < 0.02 or v.co.z > 0.93], context="VERTS")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        straighten_boundary(bm, 4)
        for v in bm.verts:
            u = v.co.copy()
            v.co = c + Rm @ Vector((u.x * xr, u.y * yr, u.z * zr))
        _keep_above(bm, keep, 0.008)
        # the pushes leave dents where the face was close: relax them, then a light final clearance
        taubin(bm, 10)
        _keep_above(bm, keep, 0.004)
        bm.to_mesh(sh.data)
        bm.free()
        sh.data.materials.clear()
        sh.data.materials.append(material)
        thicken(sh, thickness, offset=1.0)
        ss = sh.modifiers.new("Sub", "SUBSURF")
        ss.levels = 1
        _apply(sh)
        return self.add(sh)

    def gas_mask(self, rubber, lens_mat, metal, strap, under=None):
        """Full-face respirator: molded rubber face piece, two round eye lenses with rims, a side filter canister,
        a front voicemitter and a head harness."""
        R = self.R
        rest = self.rest
        eye_z = (R["eye_r"][2] + R["eye_l"][2]) / 2
        eye_y = (R["eye_r"][1] + R["eye_l"][1]) / 2
        face = self.bones("Head", "Neck") & (rest[:, 1] < eye_y + 0.03) & (rest[:, 2] > eye_z - 0.13) & (rest[:, 2] < eye_z + 0.045) \
            & (np.abs(rest[:, 0]) < 0.074)
        keep = [self.skin] + ([_bvh(under)] if under is not None else [])
        mk = self.face_shell("MaskBody", rubber, face, keep, pad=0.02, thickness=0.006)
        # only the skin well inside the mask's rim is hidden (the rim must not show a gap into the head)
        self.cover(face, erode=3)
        ms = _bvh(mk)
        hu = (self.T["Head"] - self.P["Head"]).normalized()
        hf = Vector((0, -1, 0))
        hf = (hf - hu * hf.dot(hu)).normalized()
        hr = hf.cross(hu).normalized()
        eyes = {}
        for mi in (1, 2):
            pts = [p.center for p in self.body.data.polygons if p.material_index == mi]
            if pts:
                eyes[mi] = sum(pts, Vector()) / len(pts)
        for mi, c in eyes.items():
            side = (c - (eyes[1] + eyes[2]) / 2).normalized() if len(eyes) == 2 else hr
            dirn = (hf + side * 0.25).normalized()
            loc, nrm = hit(ms, c + dirn * 0.12, -dirn)
            if loc is None:
                continue
            self.add(ocyl(f"MaskLens{mi}", loc - dirn * 0.006, loc + dirn * 0.004, 0.0245, lens_mat, 40, 0.15, 1))
            self.add(ocyl(f"LensRim{mi}", loc - dirn * 0.002, loc + dirn * 0.009, 0.029, metal, 40, 0.25, 1))
            self.add(ocyl(f"LensRimIn{mi}", loc + dirn * 0.0085, loc + dirn * 0.0105, 0.0255, metal, 40, 0.3, 0))
        mouth = (eyes[1] + eyes[2]) / 2 - hu * 0.075 if len(eyes) == 2 else self.P["Head"] + hf * 0.1
        # voicemitter
        loc, nrm = hit(ms, mouth + hf * 0.15, -hf)
        if loc:
            self.add(ocyl("Voicemitter", loc - hf * 0.004, loc + hf * 0.016, 0.022, rubber, 32, 0.25, 1))
            self.add(ocyl("VoiceGrill", loc + hf * 0.016, loc + hf * 0.021, 0.016, metal, 32, 0.3, 0))
            for k in range(4):
                self.add(obox(f"GrillSlot{k}", loc + hf * 0.021 + hu * (-0.009 + k * 0.006), hf, hu, (0.022, 0.002, 0.0025), rubber, 0.3, 0))
        # filter canister on the left cheek, angled forward and down
        fdir = (hf * 0.55 - hr * 0.75 - hu * 0.25).normalized()
        loc, nrm = hit(ms, mouth + fdir * 0.2, -fdir)
        if loc:
            a = loc - fdir * 0.004
            self.add(ocyl("FilterMount", a, a + fdir * 0.014, 0.026, rubber, 32, 0.3, 1))
            b = a + fdir * 0.014
            self.add(ocyl("Filter", b, b + fdir * 0.045, 0.04, self.m["olive"] if "olive" in self.m else metal, 48, 0.12, 1))
            for k in range(3):
                t = 0.008 + k * 0.012
                self.add(ocyl(f"FilterRib{k}", b + fdir * t, b + fdir * (t + 0.004), 0.0415, metal, 48, 0.3, 0))
            self.add(ocyl("FilterCap", b + fdir * 0.045, b + fdir * 0.05, 0.034, metal, 48, 0.3, 0))
        # head harness: lower and upper straps around the back of the head, a crown pad where they meet
        center, Rm, radii, ez, el = self.head_frame()
        for name, zc, w in (("HarnessLow", ez - 0.28, 0.07), ("HarnessMid", ez + 0.1, 0.075), ("HarnessHigh", ez + 0.55, 0.08)):
            self.head_shell(name, strap, 0.009, lambda x, y, z, zc=zc, w=w: y < 0.42 and abs(z - zc - 0.25 * y) < w, 0.0025, sub=0, min_gap=0.007)
        p, n = self.head_point(0.0, -1.0, ez + 0.3, 0.012)
        self.add(obox("HarnessPad", p, n, hu, (0.07, 0.006, 0.08), strap, 0.4, 1))
        for s_ in (1, -1):
            for zc in (ez - 0.2, ez + 0.2):
                p, n = self.head_point(s_ * 0.8, 0.55, zc, 0.018)
                self.add(obox(f"HarnessBuckle{s_}{zc:.2f}", p, n, hf, (0.022, 0.005, 0.016), metal, 0.3, 0))
        return mk

    # ------------------------------------------------------------ head frame & shells

    def head_frame(self):
        """Head ellipsoid fitted to the posed skull: (center, rotation (columns right, forward, up), radii, eye height in unit coords)."""
        if hasattr(self, "_hf"):
            return self._hf
        hu = (self.T["Head"] - self.P["Head"]).normalized()
        hf = Vector((0, -1, 0))
        hf = (hf - hu * hf.dot(hu)).normalized()
        hr = hf.cross(hu).normalized()          # character's right (-X)
        Rm = Matrix((hr, hf, hu)).transposed()
        skull = self.bones("Head")
        o = self.P["Head"]
        loc = (self.co[skull] - np.array(o[:])) @ np.array(Rm)
        eyes = [p.center.copy() for p in self.body.data.polygons if p.material_index in (1, 2)]
        e = Rm.transposed() @ (sum(eyes, Vector()) / max(1, len(eyes)) - o)
        cz = e.z + 0.015
        band = np.abs(loc[:, 2] - (cz + 0.04)) < 0.015
        rx = np.percentile(np.abs(loc[band, 0]), 96)
        yf, yb = np.percentile(loc[band, 1], 99), np.percentile(loc[band, 1], 1)
        rz = loc[:, 2].max() - cz
        c_loc = Vector((0.0, (yf + yb) / 2, cz))
        radii = Vector((rx, (yf - yb) / 2, rz))
        center = o + Rm @ c_loc
        el = e - c_loc
        self._hf = (center, Rm, radii, el.z / radii.z, el)
        return self._hf

    def head_shell(self, name, material, pad, keep, thickness, sub=1, seg=96, out=True, min_gap=None):
        """Ellipsoid shell around the head; keep(x, y, z) works on unit-sphere coordinates (x right, y forward, z up)."""
        center, Rm, radii, _, _ = self.head_frame()
        bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1.0)
        o = bpy.context.active_object
        o.name = name
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep(*v.co)], context="VERTS")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        straighten_boundary(bm, 6)
        for v in bm.verts:
            u = v.co.copy()
            v.co = center + Rm @ Vector((u.x * (radii.x + pad), u.y * (radii.y + pad), u.z * (radii.z + pad)))
        _keep_above(bm, self.skin, min_gap if min_gap is not None else pad * 0.7)
        bm.to_mesh(o.data)
        bm.free()
        o.data.materials.clear()
        o.data.materials.append(material)
        if thickness:
            thicken(o, thickness, offset=1.0 if out else -1.0)
        if sub:
            ss = o.modifiers.new("Sub", "SUBSURF")
            ss.levels = sub
            _apply(o)
        return self.add(o)

    def head_point(self, x, y, z, pad):
        center, Rm, radii, _, _ = self.head_frame()
        u = Vector((x, y, z)).normalized()
        p = center + Rm @ Vector((u.x * (radii.x + pad), u.y * (radii.y + pad), u.z * (radii.z + pad)))
        n = (Rm @ Vector((u.x / (radii.x + pad) ** 2, u.y / (radii.y + pad) ** 2, u.z / (radii.z + pad) ** 2))).normalized()
        return p, n

    def helmet(self, shell_mat, detail_mat):
        center, Rm, radii, ez, _ = self.head_frame()
        hr, hf, hu = Rm.col[0].to_3d(), Rm.col[1].to_3d(), Rm.col[2].to_3d()

        def keep(x, y, z):
            h = math.hypot(x, y)
            fy = y / h if h > 1e-6 else 0.0
            # brow line at the front, high cut over the ears, lower at the nape
            cut = ez + 0.3 + (0.04 * fy if fy > 0 else 0.6 * fy)
            ear = abs(x) / max(h, 1e-6) > 0.8 and -0.45 < fy < 0.55 and z < ez + 0.62
            return z > cut and not ear
        shell = self.head_shell("Helmet", shell_mat, 0.026, keep, 0.009, sub=1, min_gap=0.018)
        R = self.R
        eye_z = (R["eye_r"][2] + R["eye_l"][2]) / 2
        self.cover(self.bones("Head") & (self.rest[:, 2] > eye_z + 0.06), erode=0)
        # rim: a rubber edge trim
        bvh = _bvh(shell)
        for s in (1, -1):
            p, n = self.head_point(s * 1.0, 0.05, ez + 0.75, 0.035)
            self.add(obox(f"Rail{s}", p - n * 0.004, n, hu, (0.12, 0.009, 0.024), detail_mat, 0.25, 1))
            for j in range(5):
                self.add(obox(f"RailSlot{s}{j}", p + n * 0.005 + hf * (-0.044 + j * 0.022), n, hu, (0.012, 0.003, 0.008), detail_mat, 0.3, 0))
        p, n = self.head_point(0.0, 1.0, ez + 0.95, 0.035)
        self.add(obox("Shroud", p - n * 0.004, n, hu, (0.055, 0.011, 0.035), detail_mat, 0.25, 1))
        self.add(obox("ShroudPlate", p + n * 0.007 - hu * 0.006, n, hu, (0.032, 0.006, 0.016), self.m["metal"], 0.3, 1))
        for k, (dir_, size, up) in enumerate((((0, -0.1, 1), (0.09, 0.003, 0.06), hf), ((0.8, 0.2, 0.7), (0.05, 0.003, 0.04), hu),
                                             ((-0.8, 0.2, 0.7), (0.05, 0.003, 0.04), hu), ((0, -1, 0.6), (0.08, 0.003, 0.045), hu))):
            p, n = self.head_point(*dir_, 0.035)
            self.add(obox(f"Velcro{k}", p - n * 0.002, n, up, size, self.m["strap"], 0.2, 1))
        # retention: straps from the shell edge behind the ears down to the chin
        chin, _ = self.head_point(0.0, 0.75, -0.85, 0.005)
        for s in (1, -1):
            top, _ = self.head_point(s * 0.95, -0.05, ez + 0.2, 0.03)
            self.add(ocyl(f"ChinStrap{s}", top, chin + hr * s * 0.035, 0.0035, self.m["strap"], 8, 0.2, 0))
        return shell

    def glasses(self, lens_mat, frame_mat):
        center, Rm, radii, ez, el = self.head_frame()

        def lens(x, y, z):
            return y > 0.42 and abs(z - ez) < 0.17
        self.head_shell("Lens", lens_mat, 0.022, lens, 0.0018, sub=1, min_gap=0.014)

        def frame(x, y, z):
            return y > 0.4 and ez + 0.12 < z < ez + 0.2
        self.head_shell("Frame", frame_mat, 0.024, frame, 0.004, sub=1, min_gap=0.016)

        def temple(x, y, z):
            return 0.0 < abs(y) < 0.45 and y > -0.2 and abs(z - ez - 0.14) < 0.03 and abs(x) > 0.85
        self.head_shell("Temples", frame_mat, 0.008, temple, 0.003, sub=0, min_gap=0.006)
        return True

    def goggles(self, lens_mat, frame_mat, strap_mat):
        center, Rm, radii, ez, el = self.head_frame()

        def frame(x, y, z):
            return y > 0.36 and abs(z - ez) < 0.24
        self.head_shell("GoggleFrame", frame_mat, 0.02, frame, 0.016, sub=1, min_gap=0.012)

        def lens(x, y, z):
            return y > 0.42 and abs(z - ez) < 0.18
        self.head_shell("GoggleLens", lens_mat, 0.037, lens, 0.002, sub=1, min_gap=0.03)

        def strap(x, y, z):
            return y < 0.45 and abs(z - ez) < 0.09
        self.head_shell("GoggleStrap", strap_mat, 0.008, strap, 0.002, sub=0, min_gap=0.006)
        return True

    def cap(self, material):
        center, Rm, radii, ez, el = self.head_frame()
        hf, hu = Rm.col[1].to_3d(), Rm.col[2].to_3d()

        def crown(x, y, z):
            return z > ez + 0.36 - 0.15 * min(0.0, y)
        o = self.head_shell("Cap", material, 0.012, crown, 0.003, sub=1, min_gap=0.01)
        displace(o, lambda co, n, r: fabric_noise(co, 0.0005, 18, 11))
        # brim: a half-ellipse plate from the crown's front edge, curved down at the sides
        hr = Rm.col[0].to_3d()
        base, _ = self.head_point(0.0, 1.0, ez + 0.36, 0.012)
        bm = bmesh.new()
        nu, nw = 16, 6
        grid = []
        for j in range(nw + 1):
            row = []
            w = j / nw
            for i in range(nu + 1):
                u = -1 + 2 * i / nu
                reach = 0.072 * math.sqrt(max(0.0, 1 - u * u)) * w
                p = base + hr * (u * 0.088) + hf * (reach - 0.012 * (1 - u * u)) - hu * (0.018 * u * u + 0.012 * w)
                row.append(bm.verts.new(p))
            grid.append(row)
        for j in range(nw):
            for i in range(nu):
                bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1]))
        me = bpy.data.meshes.new("Brim")
        bm.to_mesh(me)
        bm.free()
        b = _link("Brim", me)
        me.materials.append(material)
        thicken(b, 0.005, offset=0.0)
        subdivide(b, 1)
        self.add(b)
        p, n = self.head_point(0.0, 0.0, 1.0, 0.014)
        self.add(obox("CapButton", p, n, hf, (0.016, 0.005, 0.016), material, 0.5, 1))
        return o

    def earpro(self, cup_mat, band_mat):
        head_c = (self.P["Head"] + self.T["Head"]) / 2
        hu = (self.T["Head"] - self.P["Head"]).normalized()
        hf = Vector((0, -1, 0))
        hr = -hu.cross(hf).normalized()
        for s in (1, -1):
            side = hr * s
            loc, nrm = hit(self.skin, head_c + side * 0.3 - hu * 0.035 + hf * 0.005, -side)
            if not loc:
                continue
            self.add(obox(f"EarCup{s}", loc + nrm * 0.012, nrm, hu, (0.065, 0.03, 0.08), cup_mat, 0.42, 2, taper=0.25))
            self.add(obox(f"EarCushion{s}", loc + nrm * 0.004, nrm, hu, (0.06, 0.012, 0.075), band_mat, 0.45, 1))
            if s == -1:  # mic boom on the left
                a = loc + nrm * 0.03 + hf * 0.01
                self.add(ocyl("Boom", a, a + hf * 0.09 - hu * 0.045 + hr * 0.04, 0.003, cup_mat, 8, 0.2, 0))
        return True

    def scarf_tail(self, material, scarf):
        """The loose end of the shemagh hanging down the chest (a cloth strip with folds)."""
        surf = _bvh(scarf)
        top, n0 = self.chest_point(-0.06, 0.13, surf)
        if top is None:
            return None
        bm = bmesh.new()
        nu, nv = 8, 18
        rows = []
        for j in range(nv + 1):
            t = j / nv
            row = []
            for i in range(nu + 1):
                u = i / nu - 0.5
                width = 0.11 * (1 - 0.35 * t)
                p = top + self.cr * (u * width + 0.02 * t) - self.cu * (0.27 * t) + self.cf * (0.012 + 0.03 * t)
                p += self.cf * (0.006 * math.sin(u * 9 + t * 4) + 0.004 * noise.noise(p * 40))
                row.append(bm.verts.new(p))
            rows.append(row)
        for j in range(nv):
            for i in range(nu):
                bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
        me = bpy.data.meshes.new("ScarfTail")
        bm.to_mesh(me)
        bm.free()
        o = _link("ScarfTail", me)
        me.materials.append(material)
        # keep it outside the chest gear / jacket
        bmo = bmesh.new()
        bmo.from_mesh(me)
        _keep_above(bmo, bvh_of([self.body] + [p for p in self.parts if p.name.startswith(("Shirt", "ChestRig", "Harness", "RigPouch"))]), 0.01)
        bmo.to_mesh(me)
        bmo.free()
        thicken(o, 0.003, offset=0.0)
        subdivide(o, 1)
        self.add(o)
        self.jiggle([o], "ScarfTail", top, top - self.cu * 0.27 + self.cf * 0.04, "Chest", 0.4)
        return o

    def scarf(self, material):
        """Shemagh wrapped loosely around the neck (rolled folds)."""
        R = self.R
        z = self.rz()
        m = self.bones("Neck", "Chest", "Head") & (z > R["neck"][2] - 0.035) & (z < R["neck"][2] + 0.05) & (np.abs(self.rest[:, 0]) < 0.11)
        fold = lambda co, n, r: folds_ring(co, self.P["Neck"] - self.cu * 0.05, self.P["Neck"] + self.cu * 0.1, 0.07, 0.07, 0.03, 0.0045, None, 13) + fabric_noise(co, 0.0004, 6, 14)
        return self.garment("Scarf", m, material, 0.028, relax=10, sub=2, folds=fold, thick=0.003, min_gap=0.022, erode=0)

    # ------------------------------------------------------------ torso gear

    def chest_point(self, u, v, surface, out=0.0):
        """Point on `surface` (BVH) at chest-frame coordinates u (to the character's right), v (up from the chest bone head)."""
        c = self.P["Chest"] + self.cr * u + self.cu * v
        loc, nrm = hit(surface, c + self.cf * 0.4, -self.cf)
        if loc is None:
            return None, None
        return loc + nrm * out, nrm

    def back_point(self, u, v, surface, out=0.0):
        c = self.P["Chest"] + self.cr * u + self.cu * v
        loc, nrm = hit(surface, c - self.cf * 0.4, self.cf)
        if loc is None:
            return None, None
        return loc + nrm * out, nrm

    def panel(self, name, material, surface, u0, u1, v0, v1, gap, thickness, back=False, res=14, round_=0.012, flatten=0.6, shape="plate", trim=None):
        """Rigid conforming panel (plate bag, chest rig): a grid ray-cast onto the surface, flattened and given thickness."""
        bm = bmesh.new()
        grid = []
        pts = []
        for j in range(res + 1):
            row = []
            for i in range(res + 1):
                u = u0 + (u1 - u0) * i / res
                v = v0 + (v1 - v0) * j / res
                loc, nrm = (self.back_point if back else self.chest_point)(u, v, surface)
                if loc is None:
                    loc = self.P["Chest"] + self.cr * u + self.cu * v + (self.cf * (-0.12 if back else 0.12))
                pts.append(loc)
                row.append(loc)
            grid.append(row)
        # flatten toward a best-fit plane (plates are stiff)
        P = np.array([p[:] for p in pts])
        c = P.mean(0)
        _, _, vt = np.linalg.svd(P - c)
        nrm = Vector(vt[2])
        if nrm.dot(self.cf * (-1 if back else 1)) < 0:
            nrm = -nrm
        top_out = 0.0
        for row in grid:
            for p in row:
                top_out = max(top_out, (p - Vector(c)).dot(nrm))
        verts = []
        for j, row in enumerate(grid):
            vr = []
            for i, p in enumerate(row):
                h = (p - Vector(c)).dot(nrm)
                q = p + nrm * ((top_out - h) * flatten)
                vr.append(bm.verts.new(q + nrm * gap))
            verts.append(vr)
        cut = 0.32 if shape == "plate" else 0.0
        for j in range(res):
            for i in range(res):
                # shooter's cut: the top corners are trimmed diagonally
                uu = abs((i + 0.5) / res - 0.5) * 2
                vv = (j + 0.5) / res
                if cut and vv > 1 - cut and uu > 1 - cut + (vv - (1 - cut)) * 0.0 and (uu - (1 - cut)) + (vv - (1 - cut)) > cut * 0.9:
                    continue
                f = (verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i])
                bm.faces.new(f if back else tuple(reversed(f)))
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        straighten_boundary(bm, 3)
        outline = self._outline(bm)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = _link(name, me)
        me.materials.append(material)
        so = o.modifiers.new("Solid", "SOLIDIFY")
        so.thickness = thickness
        so.offset = 1.0
        so.use_rim = True
        bv = o.modifiers.new("Bevel", "BEVEL")
        bv.width = round_
        bv.segments = 3
        bv.limit_method = "ANGLE"
        ss = o.modifiers.new("Sub", "SUBSURF")
        ss.levels = 1
        _apply(o)
        self.add(o)
        if trim is not None and outline:
            # sewn edge binding around the front face of the panel
            nrm_v = Vector(nrm)
            pts = [p + nrm_v * (thickness + 0.001) for p in outline]
            self.add(tube_path(name + "Binding", pts, 0.0045, trim, closed=True))
        return o

    @staticmethod
    def _outline(bm):
        """Ordered boundary loop (positions) of a bmesh with one open border."""
        edges = [e for e in bm.edges if e.is_boundary]
        if not edges:
            return []
        nxt = {}
        for e in edges:
            a, b = e.verts
            nxt.setdefault(a, []).append(b)
            nxt.setdefault(b, []).append(a)
        start = edges[0].verts[0]
        loop, prev, cur = [start], None, start
        for _ in range(len(edges)):
            cands = [w for w in nxt[cur] if w is not prev]
            if not cands or cands[0] is start:
                break
            prev, cur = cur, cands[0]
            loop.append(cur)
        return [v.co.copy() for v in loop]

    def band(self, name, material, surface_obj, vmask_fn, gap, thickness, relax=3, bones=None, smooth_base=False):
        """Strap-like band cut from a garment by a predicate on its vertices (posed coordinates), optionally
        limited to vertices that follow the given bones. smooth_base: cut it from the (smooth) body instead and lift
        it above the garment, so webbing straps don't follow the cloth folds."""
        src = self.body if smooth_base else surface_obj
        m = np.array([vmask_fn(v.co) for v in src.data.vertices])
        if bones is not None and "bone" in src.data.attributes:
            m &= np.isin(_attr(src.data, "bone"), [BONE[b] for b in bones])
        o = extract(src, m, name, material, straighten=30)
        if smooth_base:
            subdivide(o, 1)
            inflate(o, lambda co, r: 0.02, relax=relax + 10, keep=[self.skin, _bvh(surface_obj)], min_gap=gap)
        else:
            inflate(o, lambda co, r: gap, relax=relax + 6, keep=_bvh(src), min_gap=gap * 0.9)
        thicken(o, thickness, offset=1.0)
        return self.add(o)

    def band_rest(self, name, material, surface_obj, pred, gap, thickness, relax=3, bones=None):
        """Like band(), with a predicate on (posed co, rest co)."""
        src = surface_obj
        rest = _attr(src.data, "rest")
        m = np.array([pred(v.co, Vector(rest[v.index])) for v in src.data.vertices])
        if bones is not None and "bone" in src.data.attributes:
            m &= np.isin(_attr(src.data, "bone"), [BONE[b] for b in bones])
        o = extract(src, m, name, material)
        inflate(o, lambda co, r: gap, relax=relax + 4, keep=_bvh(src), min_gap=gap * 0.9)
        displace(o, lambda co, n, r: 0.002 * math.sin(co.length * 900) + fabric_noise(co, 0.001, 30))
        thicken(o, thickness, offset=1.0)
        subdivide(o, 1)
        return self.add(o)

    def shoulder_pads(self, shirt, material, strap):
        surf_obj = shirt
        for side, sgn in (("Right", 1), ("Left", -1)):
            cap = self.P[side + "UpperArm"] + self.cu * 0.035 + self.cr * (0.03 * sgn)
            pad = self.band(side + "ShoulderPad", material, surf_obj, lambda co, c=cap: (co - c).length < 0.075, 0.01, 0.014, relax=4,
                            bones=("Chest", side + "UpperArm"), smooth_base=True)
            loc, nrm = hit(_bvh(pad), cap + self.cu * 0.3, -self.cu)
            if loc:
                self.add(obox(side + "PadStrap", loc, nrm, self.cf, (0.02, 0.004, 0.11), strap, 0.3, 1))

    def drop_leg_holster(self, pants, platform_mat, holster_mat, strap):
        surf = _bvh(pants)
        hip, kn = self.P["RightUpperLeg"], self.P["RightLowerLeg"]
        mid = hip + (kn - hip) * 0.42
        loc, nrm = hit(surf, mid + Vector((-0.35, -0.03, 0)), Vector((1, 0.08, 0)))
        if loc is None:
            return
        up = (hip - kn).normalized()
        plat = self.add(obox("HolsterPlatform", loc, nrm, up, (0.1, 0.012, 0.17), platform_mat, 0.25, 2))
        body = self.add(obox("Holster", loc + nrm * 0.012 + up * 0.01, nrm, up, (0.065, 0.045, 0.185), holster_mat, 0.22, 2, taper=0.15))
        grip = self.add(obox("HolsterPistol", loc + nrm * 0.03 + up * 0.11 + Vector((0, 0.01, 0)), nrm, (up + Vector((0, 0.35, 0))).normalized(),
                             (0.032, 0.03, 0.07), holster_mat, 0.3, 1))
        hood = self.add(obox("HolsterHood", loc + nrm * 0.05 + up * 0.095, nrm, up, (0.05, 0.014, 0.05), holster_mat, 0.3, 1))
        straps = []
        for k, zz in enumerate((loc.z + 0.03, loc.z - 0.06)):
            st = self.band(f"LegStrap{k}", strap, pants, lambda co, zz=zz: abs(co.z - zz) < 0.012 and co.x < -0.02, 0.004, 0.003, relax=1)
            straps.append(st)
        top = loc + up * 0.09
        belt = loc + up * 0.24 + nrm * 0.01
        hanger = self.add(obox("HolsterHanger", (top + belt) / 2 + nrm * 0.004, nrm, up, (0.035, 0.004, (belt - top).length), strap, 0.3, 1))
        self.jiggle([plat, body, grip, hood], "Holster", top, loc - up * 0.09, "RightUpperLeg", 0.8)

    def belt_holster(self, belt, holster_mat):
        surf = _bvh(belt)
        hip = self.P["RightUpperLeg"]
        loc, nrm = hit(surf, hip + Vector((-0.4, 0.0, 0.08)), Vector((1, 0, 0)))
        if loc is None:
            return
        up = Vector((0, 0.15, 1)).normalized()
        self.add(obox("BeltHolster", loc - up * 0.07, nrm, up, (0.06, 0.04, 0.17), holster_mat, 0.22, 2, taper=0.12))
        self.add(obox("BeltHolsterPistol", loc + nrm * 0.02 + up * 0.03, nrm, (up + Vector((0, 0.5, 0))).normalized(), (0.03, 0.028, 0.065), holster_mat, 0.3, 1))
        self.add(obox("BeltHolsterLoop", loc - nrm * 0.002, nrm, up, (0.045, 0.006, 0.05), holster_mat, 0.3, 1))

    def hanging_strip(self, name, material, top, down, across, length, width, parent, out=None, stiffness=0.4, keep=None):
        """A loose cloth strip (rag, strap end) hanging from `top`; simulated in the game as a jiggle bone."""
        bm = bmesh.new()
        nu, nv = 5, 14
        out = out if out is not None else down.cross(across).normalized()
        rows = []
        for j in range(nv + 1):
            t = j / nv
            row = []
            for i in range(nu + 1):
                u = i / nu - 0.5
                p = top + across * (u * width * (1 - 0.2 * t)) + down * (length * t) + out * (0.008 * t)
                p += out * (0.004 * math.sin(u * 8 + t * 5) + 0.003 * noise.noise(p * 50))
                row.append(bm.verts.new(p))
            rows.append(row)
        for j in range(nv):
            for i in range(nu):
                bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
        if keep is not None:
            _keep_above(bm, keep, 0.006)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = _link(name, me)
        me.materials.append(material)
        thicken(o, 0.003, offset=0.0)
        subdivide(o, 1)
        self.add(o)
        self.jiggle([o], name, top, top + down * length, parent, stiffness)
        return o

    def molle(self, name, center, nrm, up, width, rows, material):
        """MOLLE webbing rows on a panel."""
        x = up.cross(nrm).normalized()
        for k in range(rows):
            c = center + up * (k * 0.026)
            self.add(obox(f"{name}{k}", c, nrm, up, (width, 0.003, 0.016), material, 0.2, 0))

    def pouch(self, name, center, nrm, up, size, material, flap_mat, strap=True):
        p = self.add(obox(name, center, nrm, up, size, material, 0.22, 2))
        w, t, h = size
        x = up.cross(nrm).normalized()
        self.add(obox(name + "Flap", center + nrm * (t * 0.55) + up * (h * 0.36), nrm, up, (w * 1.03, t * 0.5, h * 0.3), flap_mat, 0.3, 1))
        if strap:
            self.add(obox(name + "Tab", center + nrm * (t + 0.0005) + up * (h * 0.12), nrm, up, (w * 0.18, 0.003, h * 0.42), flap_mat, 0.3, 1))
        return p

    def plate_carrier(self, shirt, gear, strap, accent, polymer, style="ct"):
        """style "ct": full loadout (mag pouches, admin pouch, radio + antenna, dump pouch, MOLLE);
        "t": slick carrier with two small pouches and loose strap ends."""
        surf = _bvh(shirt)
        front = self.panel("FrontPlate", gear, surf, -0.15, 0.15, -0.19, 0.12, 0.006, 0.032, trim=strap)
        back = self.panel("BackPlate", gear, surf, -0.155, 0.155, -0.17, 0.14, 0.006, 0.03, back=True, trim=strap)
        # cummerbund: band around the lower ribs
        cf, cu, cr, c0 = self.cf, self.cu, self.cr, self.P["Chest"]
        def cumm(co):
            d = co - c0
            v = d.dot(cu)
            return -0.19 < v < -0.06 and abs(d.dot(cr)) > 0.1
        cb = self.band("Cummerbund", gear, shirt, cumm, 0.006, 0.012, bones=("Hips", "Spine", "Chest"), smooth_base=True)
        # shoulder straps over the trapezius
        def straps(co):
            d = co - c0
            return 0.07 < abs(d.dot(cr)) < 0.125 and 0.06 < d.dot(cu) < 0.21
        self.band("ShoulderStraps", gear, shirt, straps, 0.005, 0.008, bones=("Spine", "Chest", "Neck"), smooth_base=True)
        fs = _bvh(front)
        if style == "t":
            for k, u in enumerate((-0.055, 0.055)):
                loc, nrm = self.chest_point(u, -0.13, fs)
                if loc:
                    self.pouch(f"SmallPouch{k}", loc, nrm, cu, (0.06, 0.03, 0.085), gear, strap)
            loc, nrm = self.chest_point(0.0, -0.05, fs)
            if loc:
                self.molle("FrontMolle", loc, nrm, cu, 0.22, 3, strap)
            loc, nrm = self.chest_point(0.035, 0.065, fs)
            if loc:
                self.add(obox("Patch", loc + nrm * 0.0005, nrm, cu, (0.07, 0.002, 0.045), accent, 0.2, 1))
            # cummerbund strap ends hang loose at the sides
            pts = [v.co.copy() for v in cb.data.vertices]
            for s_ in (1, -1):
                side = max(pts, key=lambda q: (q - c0).dot(cr) * s_ - abs((q - c0).dot(cu) + 0.13) * 2)
                out = (cr * s_ + cf * 0.2).normalized()
                self.hanging_strip(f"StrapEnd{'R' if s_ > 0 else 'L'}", strap, side + out * 0.006, -cu, cf, 0.11, 0.024, "Chest", out=out, stiffness=0.55)
            for s_ in (1, -1):
                loc, nrm = self.chest_point(0.16 * s_, -0.12, _bvh(shirt), 0.014)
                if loc:
                    self.add(obox(f"QuickRelease{s_}", loc, nrm, cu, (0.03, 0.01, 0.045), polymer, 0.25, 1))
            return front
        # three rifle mag pouches + admin pouch + MOLLE
        for k, u in enumerate((-0.085, 0.0, 0.085)):
            loc, nrm = self.chest_point(u, -0.115, fs)
            if loc:
                self.pouch(f"MagPouch{k}", loc, nrm, cu, (0.074, 0.038, 0.12), gear, strap)
        loc, nrm = self.chest_point(0.0, 0.03, fs)
        if loc:
            self.pouch("AdminPouch", loc, nrm, cu, (0.17, 0.022, 0.085), gear, strap, strap=False)
            self.add(obox("Patch", loc + nrm * 0.0225 + cu * 0.005 - cr * 0.04, nrm, cu, (0.06, 0.002, 0.038), accent, 0.2, 1))
            self.add(obox("NameTape", loc + nrm * 0.0225 + cu * 0.005 + cr * 0.045, nrm, cu, (0.06, 0.002, 0.018), strap, 0.2, 1))
        loc, nrm = self.chest_point(0.0, 0.09, fs)
        if loc:
            self.molle("FrontMolle", loc - cu * 0.0, nrm, cu, 0.2, 1, strap)
        bs = _bvh(back)
        loc, nrm = self.back_point(-0.06, -0.02, bs)
        if loc:
            self.add(obox("RadioPouch", loc, nrm, cu, (0.07, 0.05, 0.16), gear, 0.22, 2))
            self.add(obox("Radio", loc + nrm * 0.05 + cu * 0.07, nrm, cu, (0.05, 0.03, 0.05), polymer, 0.2, 1))
            a0, a1 = loc + nrm * 0.03 + cu * 0.1, loc + nrm * 0.02 + cu * 0.34 - cr * 0.02
            ant = self.add(ocyl("Antenna", a0, a1, 0.004, polymer, 10, 0.2, 0))
            self.jiggle([ant], "Antenna", a0, a1, "Chest", 0.35)
        # dump pouch hanging off the left hip
        loc, nrm = hit(self.skin, self.P["Hips"] + Vector((0.45, 0.15, -0.06)), Vector((-1, -0.3, 0)))
        if loc:
            loc = loc + nrm * 0.03
            dp = self.add(obox("DumpPouch", loc - Vector((0, 0, 0.07)), nrm, Vector((0, 0, 1)), (0.12, 0.05, 0.16), gear, 0.35, 2, taper=0.2))
            for v in dp.data.vertices:
                v.co += Vector((0, 0, -0.02)) * max(0.0, (loc.z - v.co.z) / 0.15) * (0.5 + 0.5 * math.sin(v.co.x * 90))
            self.jiggle([dp], "DumpPouch", loc + Vector((0, 0, 0.01)), loc - Vector((0, 0, 0.16)), "Hips", 0.45)
        loc, nrm = self.back_point(0.05, 0.0, bs)
        if loc:
            self.molle("BackMolle", loc - cu * 0.07, nrm, cu, 0.11, 5, strap)
        # quick-release buckles on the cummerbund
        for s in (1, -1):
            loc, nrm = self.chest_point(0.16 * s, -0.12, _bvh(shirt), 0.014)
            if loc:
                self.add(obox(f"QuickRelease{s}", loc, nrm, cu, (0.03, 0.01, 0.045), polymer, 0.25, 1))
        return front

    def chest_rig(self, jacket, gear, strap, polymer):
        surf = _bvh(jacket)
        rig = self.panel("ChestRig", gear, surf, -0.15, 0.15, -0.21, -0.07, 0.006, 0.016, res=12)
        rs = _bvh(rig)
        cu, cr = self.cu, self.cr
        for k, u in enumerate((-0.105, -0.035, 0.035, 0.105)):
            loc, nrm = self.chest_point(u, -0.14, rs)
            if loc:
                self.pouch(f"RigPouch{k}", loc, nrm, cu, (0.064, 0.036, 0.125), gear, strap)
        # X harness: two diagonal straps over the shoulders
        c0 = self.P["Chest"]
        def harness(co):
            d = co - c0
            u, v = d.dot(cr), d.dot(cu)
            if v < -0.06:
                return False
            return abs(abs(u) - (0.11 - 0.25 * max(0.0, v - 0.1))) < 0.022 if v > 0.1 else 0.085 < abs(u) < 0.13
        self.band("Harness", strap, jacket, harness, 0.006, 0.005, bones=("Hips", "Spine", "Chest", "Neck"))
        loc, nrm = self.chest_point(0.17, -0.2, surf, 0.008)
        if loc:
            self.add(obox("RigBuckle", loc, nrm, cu, (0.03, 0.008, 0.04), polymer, 0.25, 1))
        return rig

    def knee_pads(self, pants, pad_mat, strap_mat):
        surf = _bvh(pants)
        for side in ("Right", "Left"):
            kn = self.P[side + "LowerLeg"]
            loc, nrm = hit(surf, kn + Vector((0, -0.3, 0.0)), Vector((0, 1, 0)))
            if not loc:
                continue
            self.add(obox(side + "KneePad", loc, nrm, Vector((0, 0, 1)), (0.095, 0.03, 0.13), pad_mat, 0.45, 2, taper=0.2))
            for dz in (0.045, -0.05):
                ring_m = lambda co, kz=kn.z + dz, kx=kn.x: abs(co.z - kz) < 0.012 and abs(co.x - kx) < 0.12
                self.band(side + "KneeStrap" + str(dz), strap_mat, pants, ring_m, 0.004, 0.003, relax=1)

    def holster(self, pants, mat_, polymer):
        surf = _bvh(pants)
        hip = self.P["RightUpperLeg"]
        loc, nrm = hit(surf, hip + Vector((-0.3, -0.02, -0.12)), Vector((1, 0, 0)))
        if loc:
            self.add(obox("Holster", loc, nrm, Vector((0, 0.15, 1)), (0.06, 0.04, 0.17), polymer, 0.25, 2))
            self.add(obox("HolsterGrip", loc + nrm * 0.02 + Vector((0, 0.02, 0.1)), nrm, Vector((0, 0.4, 1)), (0.03, 0.028, 0.06), polymer, 0.3, 1))
            self.add(obox("LegStrap", loc - nrm * 0.0 + Vector((0, 0, -0.05)), nrm, Vector((0, 0, 1)), (0.075, 0.006, 0.03), mat_, 0.3, 1))

    def cargo_pockets(self, pants, material):
        surf = _bvh(pants)
        for side, s in (("Right", -1), ("Left", 1)):
            th = self.P[side + "UpperLeg"] + (self.P[side + "LowerLeg"] - self.P[side + "UpperLeg"]) * 0.48
            loc, nrm = hit(surf, th + Vector((0.3 * s, -0.01, 0)), Vector((-s, 0, 0)))
            if loc:
                self.pouch(side + "Cargo", loc - nrm * 0.002, nrm, Vector((0, 0, 1)), (0.12, 0.022, 0.15), material, material, strap=False)

    def sling_bag(self, jacket, material, strap):
        surf = _bvh(jacket)
        loc, nrm = hit(surf, self.P["Hips"] + Vector((-0.12, 0.35, 0.06)), Vector((0.2, -1, 0)))
        if loc:
            bag = self.add(obox("SlingBag", loc, nrm, Vector((0, 0, 1)), (0.16, 0.08, 0.18), material, 0.3, 2))
            flap = self.add(obox("SlingFlap", loc + nrm * 0.06 + Vector((0, 0, 0.06)), nrm, Vector((0, 0, 1)), (0.165, 0.025, 0.07), strap, 0.3, 1))
            self.jiggle([bag, flap], "SlingBag", loc + Vector((0, 0, 0.1)), loc - Vector((0, 0, 0.1)), "Hips", 0.5)

    def collar(self, shirt, material):
        c0 = self.P["Neck"]
        def ring_(co):
            d = co - c0
            return -0.04 < d.dot(self.cu) < 0.02
        self.band("Collar", material, shirt, ring_, 0.004, 0.004, relax=2, bones=("Hips", "Spine", "Chest", "Neck"))

    def zipper(self, shirt, metal):
        surf = _bvh(shirt)
        for k in range(14):
            v = -0.2 + k * 0.022
            loc, nrm = self.chest_point(0.0, v, surf)
            if loc:
                self.add(obox(f"Zip{k}", loc - nrm * 0.0005, nrm, self.cu, (0.008, 0.0025, 0.016), metal, 0.3, 0))
        loc, nrm = self.chest_point(0.0, 0.1, surf)
        if loc:
            self.add(obox("ZipPull", loc + nrm * 0.002, nrm, self.cu, (0.009, 0.004, 0.025), metal, 0.3, 1))

    def knuckles(self, gloves, mat_):
        surf = _bvh(gloves)
        for side in ("Right", "Left"):
            h0, h1 = self.P[side + "Hand"], self.T[side + "Hand"]
            c = h0 + (h1 - h0) * 0.85
            # the back of the hand: away from the grip point
            g = self.P[side + "Grip"]
            out = (c - g)
            out = (out - (h1 - h0).normalized() * out.dot((h1 - h0).normalized())).normalized()
            loc, nrm = hit(surf, c + out * 0.2, -out)
            if loc:
                self.add(obox(side + "KnucklePad", loc, nrm, (h1 - h0), (0.07, 0.008, 0.03), mat_, 0.4, 1))
            # wrist strap
            wr = h0
            loc, nrm = hit(surf, wr + out * 0.2 - (h1 - h0).normalized() * 0.01, -out)
            if loc:
                self.add(obox(side + "WristTab", loc, nrm, (h1 - h0), (0.045, 0.004, 0.025), mat_, 0.3, 1))

    def finish_body(self):
        """Deletes body faces hidden under garments. Returns the visible body."""
        bm = bmesh.new()
        bm.from_mesh(self.body.data)
        bm.verts.ensure_lookup_table()
        kill = [f for f in bm.faces if all(self.covered[v.index] for v in f.verts)]
        bmesh.ops.delete(bm, geom=kill, context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(self.body.data)
        bm.free()
        return self.body
