"""
Procedural PBR texturing, baked to image files (Cycles, CPU, headless).

Every material on a generated model is swapped for a procedural "worn" version of itself, chosen by name:
metal gets edge wear and fine scratches, polymer a stipple grain, wood a grain, cloth a weave and creases,
everything a little grime in the crevices (ambient occlusion). That is baked into:

    <name>_albedo.png   base color (sRGB)
    <name>_mask.png     R = metallic, A = smoothness (Unity URP "Metallic Gloss Map" layout)
    <name>_normal.png   tangent-space normal map (PC only)

and the model ends up with a single material that references the albedo, so even a plain FBX import looks right.
"""
import os
import bpy
import numpy as np

KIND_RULES = [
    # (substring in material name, kind)
    ("Lens", "glass"), ("Glass", "glass"), ("Screen", "glass"),
    ("Steel", "metal"), ("Metal", "metal"),
    ("Wood", "wood"),
    ("Grip", "grip"), ("Polymer", "polymer"), ("Tan", "polymer"), ("Olive", "polymer"), ("Accent", "polymer"), ("Red", "polymer"),
    ("_skin", "skin"), ("_sole", "rubber"), ("_boots", "leather"), ("_gloves", "leather"),
    ("_jacket", "cloth"), ("_pants", "cloth"), ("_mask", "cloth"), ("_gear", "nylon"), ("_strap", "nylon"), ("_accent", "nylon"),
    ("Cloth", "cloth"),
]


def kind_of(name):
    for key, kind in KIND_RULES:
        if key in name:
            return kind
    return "polymer"


def _base_props(m):
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = tuple(bsdf.inputs["Base Color"].default_value)[:3]
    return col, bsdf.inputs["Metallic"].default_value, bsdf.inputs["Roughness"].default_value


def worn_material(src, kind, scale_hint=1.0):
    """Builds the procedural version of a material (same base color, metallic, roughness)."""
    col, metal, rough = _base_props(src)
    m = bpy.data.materials.new(src.name + "_proc")
    m.use_nodes = True
    nt = m.node_tree
    N, L = nt.nodes, nt.links
    for n in list(N):
        N.remove(n)
    out = N.new("ShaderNodeOutputMaterial")
    bsdf = N.new("ShaderNodeBsdfPrincipled")
    L.new(bsdf.outputs[0], out.inputs[0])
    tc = N.new("ShaderNodeTexCoord")

    def noise(scale, detail=4.0, rough_=0.55, src_vec=None):
        n = N.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = rough_
        L.new((src_vec or tc.outputs["Object"]), n.inputs["Vector"])
        return n

    def math(op, a, b=None, clamp=True):
        n = N.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def mix_rgb(fac, a, b, blend="MIX"):
        n = N.new("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = blend
        if isinstance(fac, (int, float)):
            n.inputs[0].default_value = fac
        else:
            L.new(fac, n.inputs[0])
        for sock, v in ((6, a), (7, b)):
            if isinstance(v, tuple):
                n.inputs[sock].default_value = (*v, 1)
            else:
                L.new(v, n.inputs[sock])
        return n.outputs[2]

    # cavity grime (AO) and edge mask (bevel normal vs. true normal)
    ao = N.new("ShaderNodeAmbientOcclusion")
    ao.samples = 8
    ao.inputs["Distance"].default_value = 0.03 * scale_hint
    geo = N.new("ShaderNodeNewGeometry")
    bev = N.new("ShaderNodeBevel")
    bev.samples = 8
    bev.inputs["Radius"].default_value = 0.0025 * scale_hint
    dot = N.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    L.new(bev.outputs[0], dot.inputs[0])
    L.new(geo.outputs["Normal"], dot.inputs[1])
    edge = math("MULTIPLY", math("SUBTRACT", 1.0, dot.outputs["Value"]), 9.0)
    grime = math("POWER", ao.outputs["AO"], 0.6)  # 0 in crevices, 1 open

    breakup = noise(40 / scale_hint, 6, 0.6)
    var = noise(6 / scale_hint, 3, 0.5)
    base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.35), col, tuple(min(1, c * 1.25) for c in col))
    bump_h = None

    if kind == "metal":
        wear = math("MULTIPLY", edge, math("ADD", breakup.outputs["Fac"], 0.1))
        wear = math("GREATER_THAN", wear, 0.45)
        base = mix_rgb(wear, base, (0.52, 0.52, 0.54))
        r = mix_rgb(wear, (rough, rough, rough), (0.22, 0.22, 0.22))
        scratches = noise(900 / scale_hint, 2, 0.3)
        bump_h = math("MULTIPLY", scratches.outputs["Fac"], 0.25)
        metal_out = mix_rgb(wear, (metal, metal, metal), (1.0, 1.0, 1.0))
    elif kind == "wood":
        wave = N.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "Z"
        wave.inputs["Scale"].default_value = 18 / scale_hint
        wave.inputs["Distortion"].default_value = 7
        wave.inputs["Detail"].default_value = 3
        L.new(tc.outputs["Object"], wave.inputs["Vector"])
        grain = math("MULTIPLY", wave.outputs["Fac"], 0.45)
        base = mix_rgb(grain, base, tuple(c * 0.55 for c in col))
        wear = math("GREATER_THAN", math("MULTIPLY", edge, breakup.outputs["Fac"]), 0.35)
        base = mix_rgb(wear, base, tuple(min(1, c * 1.6) for c in col))
        r = mix_rgb(grain, (rough, rough, rough), (rough + 0.15,) * 3)
        bump_h = math("MULTIPLY", wave.outputs["Fac"], 0.3)
        metal_out = (0.0, 0.0, 0.0)
    elif kind in ("cloth", "nylon"):
        weave_a = N.new("ShaderNodeTexWave")
        weave_a.inputs["Scale"].default_value = (260 if kind == "cloth" else 160) / scale_hint
        L.new(tc.outputs["Object"], weave_a.inputs["Vector"])
        weave_b = N.new("ShaderNodeTexWave")
        weave_b.bands_direction = "Y"
        weave_b.inputs["Scale"].default_value = (260 if kind == "cloth" else 160) / scale_hint
        L.new(tc.outputs["Object"], weave_b.inputs["Vector"])
        weave = math("MULTIPLY", math("ADD", weave_a.outputs["Fac"], weave_b.outputs["Fac"]), 0.5)
        creases = noise(14 / scale_hint, 5, 0.65)
        base = mix_rgb(math("MULTIPLY", weave, 0.12), base, tuple(c * 0.7 for c in col))
        base = mix_rgb(math("MULTIPLY", creases.outputs["Fac"], 0.25), base, tuple(c * 0.75 for c in col))
        r = (min(1, rough + 0.05),) * 3
        bump_h = math("ADD", math("MULTIPLY", weave, 0.15), math("MULTIPLY", creases.outputs["Fac"], 0.6))
        metal_out = (0.0, 0.0, 0.0)
    elif kind in ("leather", "rubber", "grip"):
        stip = noise(500 / scale_hint, 2, 0.4)
        base = mix_rgb(math("MULTIPLY", stip.outputs["Fac"], 0.25), base, tuple(c * 0.6 for c in col))
        r = (rough,) * 3
        bump_h = math("MULTIPLY", stip.outputs["Fac"], 0.5)
        metal_out = (0.0, 0.0, 0.0)
    elif kind == "skin":
        pores = noise(700, 2, 0.5)
        base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.25), base, (col[0] * 1.08, col[1] * 0.92, col[2] * 0.9))
        r = (0.55, 0.55, 0.55)
        bump_h = math("MULTIPLY", pores.outputs["Fac"], 0.08)
        metal_out = (0.0, 0.0, 0.0)
    elif kind == "glass":
        r = (0.05, 0.05, 0.05)
        metal_out = (0.3, 0.3, 0.3)
    else:  # polymer
        stip = noise(350 / scale_hint, 2, 0.4)
        wear = math("GREATER_THAN", math("MULTIPLY", edge, breakup.outputs["Fac"]), 0.5)
        base = mix_rgb(wear, base, tuple(min(1, c * 1.7 + 0.04) for c in col))
        r = mix_rgb(wear, (rough, rough, rough), (rough * 0.7,) * 3)
        bump_h = math("MULTIPLY", stip.outputs["Fac"], 0.2)
        metal_out = (0.0, 0.0, 0.0)

    # grime in crevices, a little dust overall
    base = mix_rgb(math("SUBTRACT", 1.0, grime), base, tuple(c * 0.45 for c in col), "MIX")
    L.new(base, bsdf.inputs["Base Color"])
    if isinstance(r, tuple):
        bsdf.inputs["Roughness"].default_value = r[0]
    else:
        L.new(r, bsdf.inputs["Roughness"])
    if bump_h is not None:
        bump = N.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.35
        bump.inputs["Distance"].default_value = 0.0006 * scale_hint
        L.new(bump_h, bump.inputs["Height"])
        L.new(bump.outputs[0], bsdf.inputs["Normal"])
    m["_metal"] = metal_out if isinstance(metal_out, tuple) else None
    m["_metal_node"] = 1 if not isinstance(metal_out, tuple) else 0
    if not isinstance(metal_out, tuple):
        # keep a handle to the metallic signal for the metal pass
        emit = N.new("ShaderNodeEmission")
        emit.name = "MetalSignal"
        L.new(metal_out, emit.inputs["Color"])
    else:
        bsdf.inputs["Metallic"].default_value = metal_out[0]
    return m, bsdf, out


def _unwrap(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.004, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


def _image(name, size, non_color, alpha=False):
    img = bpy.data.images.new(name, size, size, alpha=alpha, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def bake_model(obj, out_dir, name, size, normal=True, scale_hint=1.0, samples=12):
    """UV-unwraps, bakes the textures for `obj` and gives it one textured material. Returns written paths."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    _unwrap(obj)

    # swap in procedural materials, each with an image node (the bake target) selected
    originals = list(obj.data.materials)
    procs = []
    for i, src in enumerate(originals):
        if src is None:
            continue
        pm, bsdf, out = worn_material(src, kind_of(src.name), scale_hint)
        obj.data.materials[i] = pm
        procs.append((pm, bsdf, out))

    def set_target(img):
        for pm, _, _ in procs:
            nodes = pm.node_tree.nodes
            t = nodes.get("BakeTarget") or nodes.new("ShaderNodeTexImage")
            t.name = "BakeTarget"
            t.image = img
            nodes.active = t

    bake = sc.render.bake
    bake.margin = max(4, size // 128)
    written = []
    os.makedirs(out_dir, exist_ok=True)

    # albedo
    albedo = _image(name + "_albedo", size, False)
    set_target(albedo)
    bake.use_pass_direct = False
    bake.use_pass_indirect = False
    bake.use_pass_color = True
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=bake.margin)
    # roughness
    roughness = _image(name + "_rough", size, True)
    set_target(roughness)
    bpy.ops.object.bake(type="ROUGHNESS", use_clear=True, margin=bake.margin)
    # metallic: route each material's metallic signal into emission for one pass
    metal = _image(name + "_metal", size, True)
    set_target(metal)
    saved = []
    for pm, bsdf, out in procs:
        nt = pm.node_tree
        sig = nt.nodes.get("MetalSignal")
        emit = sig or nt.nodes.new("ShaderNodeEmission")
        if not sig:
            v = bsdf.inputs["Metallic"].default_value
            emit.inputs["Color"].default_value = (v, v, v, 1)
        old = out.inputs[0].links[0].from_socket
        nt.links.new(emit.outputs[0], out.inputs[0])
        saved.append((nt, old, out))
    bpy.ops.object.bake(type="EMIT", use_clear=True, margin=bake.margin)
    for nt, old, out in saved:
        nt.links.new(old, out.inputs[0])
    # normal
    normal_img = None
    if normal:
        normal_img = _image(name + "_normal", size, True)
        set_target(normal_img)
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_clear=True, margin=bake.margin)

    def save(img, path):
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        written.append(path)

    save(albedo, os.path.join(out_dir, name + "_albedo.png"))
    # mask: R metallic, G/B unused, A smoothness (1 - roughness)
    w = h = size
    r = np.array(roughness.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., 0]
    mtl = np.array(metal.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., 0]
    mask = np.zeros((h, w, 4), dtype=np.float32)
    mask[..., 0] = mtl
    mask[..., 1] = 1.0
    mask[..., 2] = 0.0
    mask[..., 3] = 1.0 - r
    mask_img = _image(name + "_mask", size, True, alpha=True)
    mask_img.pixels[:] = mask.ravel()
    save(mask_img, os.path.join(out_dir, name + "_mask.png"))
    if normal_img is not None:
        save(normal_img, os.path.join(out_dir, name + "_normal.png"))

    # one simple material for export (albedo on the base color) — the game assigns all maps at runtime
    final = bpy.data.materials.new(name)
    final.use_nodes = True
    fb = final.node_tree.nodes.get("Principled BSDF")
    tex = final.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = albedo
    final.node_tree.links.new(tex.outputs["Color"], fb.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(final)
    for poly in obj.data.polygons:
        poly.material_index = 0
    return written
