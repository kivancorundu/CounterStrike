"""
The two factions on the realistic body (v3): high-poly source meshes for vexa_characters.

    akinci   (attackers, T)   knit balaclava with eye holes, flannel shirt with rolled sleeves, slick light plate
                              carrier, khaki cargo trousers, leather belt with holster, a rag in the back pocket
    muhafiz  (defenders, CT)  hood + full-face respirator (twin lenses, side filter, harness), navy combat uniform,
                              plate carrier with pouches, radio and antenna, shoulder pads, knee pads,
                              drop-leg holster, dump pouch

Loose parts (antenna, strap ends, rag, dump pouch, holster) get "Jiggle_*" bones: Client/Art/SpringBones.cs
swings them in the game.
"""
import bpy
import numpy as np

import vexa_human as H
import vexa_outfit as O
from vexa_common import custom_mat, mat

FACTIONS = {
    "akinci": dict(
        jacket=(0.74, 0.73, 0.7), plaid=[(0.74, 0.73, 0.7), (0.24, 0.25, 0.27), (0.78, 0.5, 0.2)],
        pants=(0.33, 0.31, 0.21), gear=(0.68, 0.66, 0.6), strap=(0.46, 0.44, 0.38), boots=(0.3, 0.21, 0.13),
        gloves=(0.09, 0.09, 0.09), mask=(0.17, 0.12, 0.085), belt=(0.11, 0.08, 0.06), rag=(0.45, 0.12, 0.08),
        accent=(0.95, 0.64, 0.23), holster=(0.08, 0.075, 0.07)),
    "muhafiz": dict(
        jacket=(0.075, 0.095, 0.15), pants=(0.065, 0.08, 0.125), gear=(0.22, 0.25, 0.28), strap=(0.12, 0.13, 0.15),
        boots=(0.05, 0.05, 0.055), gloves=(0.06, 0.065, 0.08), mask=(0.045, 0.05, 0.06), belt=(0.1, 0.1, 0.11),
        accent=(0.35, 0.69, 1.0), holster=(0.07, 0.075, 0.08), rubber=(0.05, 0.05, 0.052), olive=(0.25, 0.26, 0.2)),
}


# cotton / ripstop drape (vexa_cloth.drape settings)
DRAPE = dict(frames=40, stiffness=18.0, bending=0.6, mass=0.25, rings=2)
# trousers: heavier twill, folds over itself
DRAPE_PANTS = dict(frames=40, stiffness=22.0, bending=4.0, mass=0.3, rings=2, self_collision=True)

# small round parts that read badly when decimated as hard as cloth (they keep ~10x the density)
DETAIL_PARTS = ("MaskLens", "LensRim", "Voice", "Grill", "Filter", "Buckle", "QuickRelease", "Antenna",
                # head layers are seen up close (and sit millimeters above the skin)
                "Balaclava", "MaskBody", "Harness")


def materials(faction):
    f = FACTIONS[faction]
    p = faction.capitalize()
    M = lambda key, rgb, metal=0.0, rough=0.85: custom_mat(f"{p}_{key}", rgb, metal, rough)
    m = {
        "skin": M("skin", (0.62, 0.47, 0.38), 0, 0.5),
        "eye": M("eye", (0.8, 0.8, 0.8), 0, 0.05),
        "jacket": M("jacket", f["jacket"]),
        "pants": M("pants", f["pants"]),
        "gear": M("gear", f["gear"], 0, 0.8),
        "strap": M("strap", f["strap"], 0, 0.8),
        "boots": M("boots", f["boots"], 0, 0.55),
        "gloves": M("gloves", f["gloves"], 0, 0.65),
        "mask": M("mask", f["mask"], 0, 0.95),
        "belt": M("belt", f["belt"], 0, 0.5),
        "holster": M("holster", f["holster"], 0, 0.55),
        "accent": M("accent", f["accent"], 0, 0.5),
        "sole": M("sole", (0.045, 0.043, 0.04), 0, 0.85),
        "metal": mat("Metal"),
        "lens": custom_mat(f"{p}_Lens", (0.03, 0.04, 0.05), 0.2, 0.05),
        "polymer": custom_mat(f"{p}_Polymer", (0.06, 0.062, 0.065), 0.0, 0.6),
    }
    if f.get("plaid"):
        m["jacket"]["plaid"] = [list(c) for c in f["plaid"]]
    if f.get("rag"):
        m["rag"] = M("rag", f["rag"], 0, 0.95)
    if f.get("rubber"):
        m["rubber"] = M("rubber", f["rubber"], 0, 0.6)
        m["olive"] = custom_mat(f"{p}_Metal_filter", f["olive"], 0.6, 0.5)
    m["skin"]["mh_texture"] = H.SKIN_TEX
    return m


def build(faction, shape=None):
    """Returns (high-poly mesh object, posed game-bone layout, full posed body for weight transfer)."""
    body, joint = H.build_body(shape)
    rest_lm = H.rest_landmarks(joint)
    posed = H.pose_body(body, joint)
    m = materials(faction)
    idx = np.zeros(len(body.data.polygons), dtype=np.int32)
    body.data.polygons.foreach_get("material_index", idx)
    body.data.materials.clear()
    for k in ("skin", "eye", "eye"):
        body.data.materials.append(m[k])
    body.data.polygons.foreach_set("material_index", idx)
    _stubble(body, rest_lm, faction)
    full = body.copy()
    full.data = body.data.copy()
    full.name = "BodyFull"
    bpy.context.collection.objects.link(full)
    full.hide_render = True

    o = O.Outfit(body, posed, rest_lm, m)
    if faction == "muhafiz":
        shirt = o.shirt(m["jacket"], offset=0.03, loose=1.0, drape=DRAPE, hem=rest_lm["waist"][2] - 0.035)
        pants = o.pants(m["pants"], offset=0.03, over=(shirt,), drape=DRAPE_PANTS)
        belt = o.belt(m["belt"], m["metal"], over=(shirt, pants))
        o.boots(m["boots"], m["sole"])
        gl = o.gloves(m["gloves"])
        o.knuckles(gl, m["polymer"])
        hood = o.balaclava(m["mask"], holes="face")
        o.gas_mask(m["rubber"], m["lens"], m["metal"], m["strap"], under=hood)
        o.plate_carrier(shirt, m["gear"], m["strap"], m["accent"], m["polymer"], style="ct")
        o.knee_pads(pants, m["polymer"], m["strap"])
        o.drop_leg_holster(pants, m["strap"], m["holster"], m["strap"])
        o.cargo_pockets(pants, m["pants"])
    else:
        shirt = o.shirt(m["jacket"], offset=0.032, loose=1.0, rolled=True, sleeve_end=0.16, collar=False, drape=DRAPE,
                        hem=rest_lm["waist"][2] - 0.035)
        pants = o.pants(m["pants"], offset=0.032, over=(shirt,), drape=DRAPE_PANTS)
        belt = o.belt(m["belt"], m["metal"], over=(shirt, pants))
        o.belt_holster(belt, m["holster"])
        o.boots(m["boots"], m["sole"])
        o.gloves(m["gloves"])
        o.balaclava(m["mask"], holes="eyes")
        o.plate_carrier(shirt, m["gear"], m["strap"], m["accent"], m["polymer"], style="t")
        o.cargo_pockets(pants, m["pants"])
        # a rag stuffed in the left back pocket
        hips = o.P["Hips"]
        loc, nrm = O.hit(O.bvh_of([pants]), hips + O.Vector((0.09, 0.4, -0.1)), O.Vector((0, -1, 0)))
        if loc:
            o.hanging_strip("Rag", m["rag"], loc + nrm * 0.006, O.Vector((0, 0.15, -1)).normalized(), O.Vector((1, 0, 0)), 0.2, 0.09, "Hips",
                            out=nrm, stiffness=0.35)
    visible = o.finish_body()
    parts = [visible] + o.parts
    # texture atlas on the parts (see vexa_uv): body UVs for the body and garments, smart project for gear
    import vexa_uv as UV
    for p in parts:
        UV.mark_inner(p, o.skin) if p is not visible else None
        UV.part_atlas(p, use_mh=p.data.uv_layers.get("mh") is not None)
        UV.separate_inner(p)
    # small hard parts the decimation must not erase (lenses, rims, buckles...): a "detail" vertex group
    for p in parts:
        if p.name.startswith(DETAIL_PARTS):
            g = p.vertex_groups.new(name="detail")
            g.add(list(range(len(p.data.vertices))), 0.5, "REPLACE")
    # layer order (creation order: later parts are worn on top) so the game meshes can be kept from
    # intersecting after decimation (vexa_characters.fix_layers)
    for i, p in enumerate(parts):
        a = p.data.attributes.new("layer", "INT", "POINT")
        a.data.foreach_set("value", np.full(len(p.data.vertices), i, dtype=np.int32))
    # faces the first-person arms are cut from (sleeves, gloves, glove details)
    for p in parts:
        a = p.data.attributes.new("arm", "INT", "FACE")
        if p.name.startswith(("Shirt", "Gloves", "RightKnuckle", "LeftKnuckle", "RightWrist", "LeftWrist")) or p is visible:
            a.data.foreach_set("value", np.ones(len(p.data.polygons), dtype=np.int32))
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = visible
    bpy.ops.object.join()
    high = bpy.context.active_object
    high.name = faction + "_high"
    UV.finish_atlas(high, boost=_boost(posed))
    return high, H.game_layout(posed), full, posed, o.jiggles


def _stubble(body, R, faction):
    """Per-vertex masks the skin shader reads: beard stubble, eyebrows, short hair on the scalp."""
    rest = O._attr(body.data, "rest")
    eye = (np.asarray(R["eye_r"]) + np.asarray(R["eye_l"])) / 2
    jaw = np.asarray(R["jaw"])
    n = len(rest)
    beard = np.zeros(n, np.float32)
    brow = np.zeros(n, np.float32)
    hair = np.zeros(n, np.float32)
    x, y, z = rest[:, 0], rest[:, 1], rest[:, 2]
    front = y < eye[1] + 0.06
    # stubble: below the cheekbones, on the jaw, chin and upper lip; thinner up the cheeks
    below = np.clip((eye[2] - 0.045 - z) / 0.03, 0, 1)
    neck_fade = np.clip((z - (jaw[2] - 0.075)) / 0.03, 0, 1)
    beard = below * neck_fade * (np.abs(x) < 0.085)
    # eyebrows: two arcs above the eyes
    for ex in (R["eye_r"], R["eye_l"]):
        dx = x - ex[0]
        arc = ex[2] + 0.017 + 0.0 - 1.2 * dx * dx
        brow = np.maximum(brow, np.clip(1 - np.abs(z - arc) / 0.0055, 0, 1) * (np.abs(dx) < 0.026) * front)
    # buzz cut: the scalp above the ears and forehead line
    hair = np.clip((z - (eye[2] + 0.055)) / 0.01, 0, 1) * np.clip((z - (eye[2] + 0.01)) / 0.01, 0, 1)
    hair = np.maximum(hair, (z > eye[2] - 0.02) * (y > eye[1] + 0.085) * (z > jaw[2] - 0.01))
    for name, arr in (("beard", beard), ("brow", brow), ("hair", hair)):
        a = body.data.attributes.new(name, "FLOAT", "POINT")
        a.data.foreach_set("value", arr.astype(np.float32))


def _boost(posed):
    """More texels for the head and the hands (seen up close)."""
    neck = posed["Neck"][0].z
    hands = [posed["RightGrip"][0], posed["LeftGrip"][0]]

    def f(c):
        if c.z > neck + 0.02:
            return 1.8
        if min((c - h).length for h in hands) < 0.12:
            return 1.35
        return 1.0
    return f
