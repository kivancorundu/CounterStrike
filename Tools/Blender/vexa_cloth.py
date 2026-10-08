"""
Cloth draping for the garments (Blender's cloth solver, headless): a garment is cut a size too big, its edges
(collar, cuffs, waistband, hems) are pinned, and gravity lets it settle onto the posed body. Fabric rests on
the shoulders, the hips and the tops of the limbs, hangs below them and gathers in folds, instead of
floating around the body like an inflated shell.
"""
import bpy
import numpy as np


def _collider(o, thickness, friction):
    m = o.modifiers.get("Collision") or o.modifiers.new("Collision", "COLLISION")
    c = o.collision
    c.thickness_outer = thickness
    c.thickness_inner = 0.02
    c.cloth_friction = friction
    c.damping = 0.6
    return m


def pin_group(o, weights, name="pin"):
    g = o.vertex_groups.get(name) or o.vertex_groups.new(name=name)
    for i, w in enumerate(weights):
        if w > 0:
            g.add([i], float(w), "REPLACE")
    return name


def boundary_pins(o, rings=1):
    """Weights 1 on the open edges of a garment (and `rings` edge rings inward, fading)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    w = np.zeros(len(bm.verts))
    front = [v for v in bm.verts if v.is_boundary]
    for v in front:
        w[v.index] = 1.0
    for r in range(rings):
        nxt = []
        for v in front:
            for e in v.link_edges:
                u = e.other_vert(v)
                if w[u.index] == 0:
                    w[u.index] = 1.0 - (r + 1) / (rings + 1)
                    nxt.append(u)
        front = nxt
    bm.free()
    return w


def drape(o, colliders, pins=None, frames=36, stiffness=18.0, bending=0.8, mass=0.25, shrink=0.0,
          thickness=0.0045, friction=6.0, quality=6, self_collision=False):
    """Simulates `o` falling onto `colliders` for `frames` frames and applies the result."""
    sc = bpy.context.scene
    sc.use_gravity = True
    sc.gravity = (0.0, 0.0, -9.81)
    for c in colliders:
        _collider(c, thickness, friction)
    m = o.modifiers.new("Cloth", "CLOTH")
    s = m.settings
    s.quality = quality
    s.mass = mass
    s.tension_stiffness = stiffness
    s.compression_stiffness = stiffness
    s.shear_stiffness = stiffness * 0.4
    s.bending_stiffness = bending
    s.tension_damping = 5.0
    s.compression_damping = 5.0
    s.air_damping = 1.5
    s.shrink_min = shrink
    if pins is not None:
        s.vertex_group_mass = pin_group(o, pins)
        s.pin_stiffness = 1.0
    cs = m.collision_settings
    cs.use_collision = True
    cs.collision_quality = 4
    cs.distance_min = thickness
    cs.use_self_collision = self_collision
    if self_collision:
        # excess fabric folds over itself instead of collapsing into thin fins
        cs.self_distance_min = 0.004
        cs.self_friction = 5.0
    m.point_cache.frame_start = 1
    m.point_cache.frame_end = frames
    sc.frame_start = 1
    sc.frame_end = frames
    for f in range(1, frames + 1):
        sc.frame_set(f)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)
    sc.frame_set(1)
    for c in colliders:
        mc = c.modifiers.get("Collision")
        if mc is not None:
            c.modifiers.remove(mc)
    if o.vertex_groups.get("pin") is not None:
        o.vertex_groups.remove(o.vertex_groups["pin"])
    return o
