"""
Texture atlas layout for the characters, made on the high-poly parts BEFORE they are joined and decimated (the
game meshes inherit it, so their UVs stay in a few large, clean islands):

  * the body and every garment cut from it reuse MakeHuman's body UV layout ("mh": few seams, artist-made),
  * hard gear is unwrapped part by part (smart project),
  * faces that look toward the body (the inside of a garment, the back of a pouch) get a small share of texels,
  * then all islands are scaled to the same texel density, the head and the hands get more, and everything is packed.
"""
import bmesh
import bpy
import numpy as np
from mathutils import Vector

ATLAS = "atlas"


def _select_only(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def part_atlas(o, use_mh):
    """Creates the "atlas" UV layer of one part."""
    me = o.data
    if me.uv_layers.get(ATLAS) is None:
        me.uv_layers.new(name=ATLAS)
    atlas = me.uv_layers[ATLAS]
    if use_mh and me.uv_layers.get("mh") is not None:
        buf = np.zeros(len(me.loops) * 2, dtype=np.float32)
        me.uv_layers["mh"].data.foreach_get("uv", buf)
        atlas.data.foreach_set("uv", buf)
        return
    me.uv_layers.active = atlas
    _select_only(o)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.002, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def separate_inner(o):
    """Moves the UVs of inner faces out of the way (they were copied from the outer surface by Solidify), so the
    inside of a garment becomes its own islands instead of overlapping the outside."""
    me = o.data
    a = me.attributes.get("inner")
    if a is None:
        return
    inner = np.zeros(len(me.polygons), dtype=np.int32)
    a.data.foreach_get("value", inner)
    if not inner.any():
        return
    starts = np.zeros(len(me.polygons), dtype=np.int32)
    totals = np.zeros(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("loop_start", starts)
    me.polygons.foreach_get("loop_total", totals)
    face_of_loop = np.repeat(np.arange(len(me.polygons)), totals)
    order = np.argsort(np.repeat(starts, totals) + np.concatenate([np.arange(t) for t in totals]))
    face_of_loop = face_of_loop[order]
    uv = np.zeros(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers[ATLAS].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    uv[inner[face_of_loop] == 1, 0] += 3.0
    me.uv_layers[ATLAS].data.foreach_set("uv", uv.ravel())


def mark_inner(o, skin_bvh, limb_bvh=None, limb_bones=(), on_limb=False):
    """Face attribute "inner" = 1 where the face looks toward the body (hidden side of a layer).
    skin_bvh: the skin without the arms. Faces worn on the arms (sleeves, gloves: most of their vertices follow
    `limb_bones`, or the whole part when on_limb) are judged against limb_bvh, the whole skin. Judging gear
    against the whole skin would hide the front of a vest wherever a forearm passes in front of it."""
    me = o.data
    limb = np.zeros(len(me.polygons), dtype=bool)
    if limb_bvh is not None:
        if on_limb:
            limb[:] = True
        elif limb_bones and me.attributes.get("bone") is not None:
            bone = np.zeros(len(me.vertices), dtype=np.int32)
            me.attributes["bone"].data.foreach_get("value", bone)
            on = np.isin(bone, limb_bones)
            for p in me.polygons:
                vs = p.vertices
                limb[p.index] = 2 * sum(1 for v in vs if on[v]) > len(vs)
    vals = np.zeros(len(me.polygons), dtype=np.int32)
    for p in me.polygons:
        loc, nrm, _, dist = (limb_bvh if limb[p.index] else skin_bvh).find_nearest(p.center)
        if loc is None:
            continue
        out = p.center - loc
        if out.length < 1e-6:
            out = nrm
        if p.normal.dot(out.normalized()) < -0.2:
            vals[p.index] = 1
    a = me.attributes.get("inner") or me.attributes.new("inner", "INT", "FACE")
    a.data.foreach_set("value", vals)


def _islands(bm, uv):
    """UV islands (lists of faces): faces connected through edges whose UVs match on both sides."""
    bm.faces.ensure_lookup_table()
    seen = np.zeros(len(bm.faces), dtype=bool)
    out = []
    for f in bm.faces:
        if seen[f.index]:
            continue
        seen[f.index] = True
        stack, island = [f], []
        while stack:
            g = stack.pop()
            island.append(g)
            for l in g.loops:
                a0, a1 = l[uv].uv, l.link_loop_next[uv].uv
                for l2 in l.edge.link_loops:
                    h = l2.face
                    if seen[h.index] or h is g:
                        continue
                    b0, b1 = l2[uv].uv, l2.link_loop_next[uv].uv
                    if ((a0 - b1).length < 1e-5 and (a1 - b0).length < 1e-5) or ((a0 - b0).length < 1e-5 and (a1 - b1).length < 1e-5):
                        seen[h.index] = True
                        stack.append(h)
        out.append(island)
    return out


def finish_atlas(o, boost=None, inner_scale=0.3, margin=0.0015):
    """Equal texel density for all islands, then boost(face center) / inner_scale, then pack into 0..1."""
    me = o.data
    me.uv_layers.active = me.uv_layers[ATLAS]
    _select_only(o)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode="OBJECT")
    inner = None
    if me.attributes.get("inner") is not None:
        inner = np.zeros(len(me.polygons), dtype=np.int32)
        me.attributes["inner"].data.foreach_get("value", inner)
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv[ATLAS]
    for island in _islands(bm, uv):
        k = 1.0
        if inner is not None and sum(inner[f.index] for f in island) > len(island) * 0.5:
            k = inner_scale
        elif boost is not None:
            c = sum((f.calc_center_median() for f in island), Vector()) / len(island)
            k = boost(c)
        if k == 1.0:
            continue
        cuv = sum((l[uv].uv for f in island for l in f.loops), Vector((0.0, 0.0))) / sum(len(f.loops) for f in island)
        for f in island:
            for l in f.loops:
                l[uv].uv = cuv + (l[uv].uv - cuv) * k
    bm.to_mesh(me)
    bm.free()
    pack(o, margin)


def pack(o, margin=0.0015):
    o.data.uv_layers.active = o.data.uv_layers[ATLAS]
    _select_only(o)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin, shape_method="CONCAVE")
    except TypeError:
        bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")
