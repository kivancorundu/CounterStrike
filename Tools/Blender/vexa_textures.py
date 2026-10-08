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
    ("_eye", "eye"), ("_rubber", "rubber"), ("_belt", "leather"), ("_holster", "polymer"),
    ("_jacket", "cloth"), ("_pants", "cloth"), ("_mask", "knit"), ("_scarf", "cloth"), ("_rag", "cloth"),
    ("_gear", "nylon"), ("_strap", "nylon"), ("_accent", "nylon"),
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

    camo = src.get("camo")
    if camo:
        # multicam-like: base plus three layers of distorted blobs
        cols = [tuple(c) for c in camo]
        base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.2), cols[0], tuple(c * 1.1 for c in cols[0]))
        for k, c in enumerate(cols[1:]):
            n = noise((3.2 + k * 1.7) / scale_hint, 5, 0.62)
            n.inputs["Distortion"].default_value = 1.2 + k * 0.4
            n.noise_dimensions = "4D" if False else "3D"
            L.new(tc.outputs["Object"], n.inputs["Vector"])
            m_ = math("GREATER_THAN", n.outputs["Fac"], 0.56 + 0.03 * k)
            base = mix_rgb(m_, base, c)

    plaid = src.get("plaid")
    if plaid:
        # tartan flannel along the garment's body UVs: dark bands both ways, thin accent threads, twill grain
        uvn = N.new("ShaderNodeUVMap")
        uvn.uv_map = "mh"
        sep = N.new("ShaderNodeSeparateXYZ")
        L.new(uvn.outputs[0], sep.inputs[0])
        cols = [tuple(c) for c in plaid]
        def bands(coord, freq, width, offset=0.0):
            f = math("FRACT", math("ADD", math("MULTIPLY", coord, freq, clamp=False), offset, clamp=False), None, clamp=False)
            return math("LESS_THAN", math("ABSOLUTE", math("SUBTRACT", f, 0.5, clamp=False), None, clamp=False), width)
        U, V = sep.outputs["X"], sep.outputs["Y"]
        F = 22.0
        du, dv = bands(U, F, 0.17), bands(V, F, 0.17)
        tu, tv = bands(U, F, 0.035, 0.5), bands(V, F, 0.035, 0.5)
        base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.25), cols[0], tuple(c * 0.92 for c in cols[0]))
        base = mix_rgb(math("MULTIPLY", du, 0.72), base, cols[1])
        base = mix_rgb(math("MULTIPLY", dv, 0.72), base, cols[1])
        base = mix_rgb(math("MULTIPLY", math("MULTIPLY", du, dv), 0.6), base, tuple(c * 0.6 for c in cols[1]))
        base = mix_rgb(math("MULTIPLY", math("MAXIMUM", tu, tv), 0.8), base, cols[2])
        twill = N.new("ShaderNodeTexWave")
        twill.wave_type = "BANDS"
        twill.inputs["Scale"].default_value = 900
        L.new(uvn.outputs[0], twill.inputs["Vector"])
        base = mix_rgb(math("MULTIPLY", twill.outputs["Fac"], 0.12), base, tuple(c * 0.7 for c in cols[0]))

    if kind == "knit":
        # rib knit: vertical ribs along the body UVs plus stitch rows
        uvn = N.new("ShaderNodeUVMap")
        uvn.uv_map = "mh"
        rib = N.new("ShaderNodeTexWave")
        rib.bands_direction = "X"
        rib.inputs["Scale"].default_value = 520
        rib.inputs["Distortion"].default_value = 0.6
        L.new(uvn.outputs[0], rib.inputs["Vector"])
        st = N.new("ShaderNodeTexWave")
        st.bands_direction = "Y"
        st.inputs["Scale"].default_value = 1400
        L.new(uvn.outputs[0], st.inputs["Vector"])
        knit = math("MULTIPLY", rib.outputs["Fac"], math("ADD", math("MULTIPLY", st.outputs["Fac"], 0.5), 0.5))
        base = mix_rgb(math("MULTIPLY", knit, 0.35), base, tuple(c * 0.55 for c in col))
        fuzz = noise(2500, 2, 0.6)
        base = mix_rgb(math("MULTIPLY", fuzz.outputs["Fac"], 0.15), base, tuple(min(1, c * 1.4) for c in col))
        r = (min(1.0, rough + 0.05),) * 3
        bump_h = math("ADD", math("MULTIPLY", knit, 0.7), math("MULTIPLY", fuzz.outputs["Fac"], 0.1))
        metal_out = (0.0, 0.0, 0.0)
    elif kind == "eye":
        a = N.new("ShaderNodeAttribute")
        a.attribute_name = "iris"
        ir = a.outputs["Fac"]
        veins = noise(300, 4, 0.7)
        sclera = mix_rgb(math("MULTIPLY", veins.outputs["Fac"], 0.25), (0.78, 0.74, 0.7), (0.7, 0.42, 0.38))
        fibers = N.new("ShaderNodeTexWave")
        fibers.wave_type = "RINGS"
        fibers.inputs["Scale"].default_value = 300
        fibers.inputs["Distortion"].default_value = 12
        L.new(tc.outputs["Object"], fibers.inputs["Vector"])
        iris_c = mix_rgb(math("MULTIPLY", fibers.outputs["Fac"], 0.5), (0.22, 0.13, 0.06), (0.4, 0.27, 0.12))
        base = mix_rgb(math("GREATER_THAN", ir, 0.86), sclera, iris_c)
        base = mix_rgb(math("GREATER_THAN", ir, 0.965), base, (0.01, 0.01, 0.01))
        # limbal ring
        ring_ = math("MULTIPLY", math("GREATER_THAN", ir, 0.84), math("LESS_THAN", ir, 0.875))
        base = mix_rgb(math("MULTIPLY", ring_, 0.7), base, (0.05, 0.04, 0.03))
        r = (0.06, 0.06, 0.06)
        bump_h = None
        metal_out = (0.0, 0.0, 0.0)
    elif kind == "metal":
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
        pores = noise(900, 2, 0.5)
        tex_path = src.get("mh_texture")
        if tex_path:
            img = N.new("ShaderNodeTexImage")
            img.image = bpy.data.images.load(tex_path, check_existing=True)
            img.interpolation = "Cubic"
            uvn = N.new("ShaderNodeUVMap")
            uvn.uv_map = "mh"
            L.new(uvn.outputs[0], img.inputs[0])
            # weathered, slightly tanned
            base = mix_rgb(0.25, img.outputs["Color"], (0.55, 0.38, 0.28), "MULTIPLY")
            base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.3), base, (0.5, 0.3, 0.24))
        else:
            base = mix_rgb(math("MULTIPLY", var.outputs["Fac"], 0.25), base, (col[0] * 1.08, col[1] * 0.92, col[2] * 0.9))
        def attr(name):
            a = N.new("ShaderNodeAttribute")
            a.attribute_name = name
            return a.outputs["Fac"]
        speck = noise(1400, 2, 0.6)
        stub = math("MULTIPLY", attr("beard"), math("ADD", math("MULTIPLY", speck.outputs["Fac"], 0.8), 0.25))
        base = mix_rgb(math("MULTIPLY", stub, 0.75), base, (0.08, 0.07, 0.065))
        strands = N.new("ShaderNodeTexWave")
        strands.inputs["Scale"].default_value = 160
        strands.inputs["Distortion"].default_value = 18
        L.new(tc.outputs["Object"], strands.inputs["Vector"])
        hair = math("MULTIPLY", attr("hair"), math("ADD", math("MULTIPLY", strands.outputs["Fac"], 0.35), 0.65))
        base = mix_rgb(hair, base, (0.045, 0.035, 0.028))
        base = mix_rgb(math("MULTIPLY", attr("brow"), 0.85), base, (0.05, 0.035, 0.025))
        r = (0.48, 0.48, 0.48)
        bump_h = math("ADD", math("MULTIPLY", pores.outputs["Fac"], 0.12), math("MULTIPLY", hair, 0.2))
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
    if kind in ("cloth", "nylon", "leather", "rubber"):
        # dust collects low on the legs and boots
        sep = N.new("ShaderNodeSeparateXYZ")
        L.new(geo.outputs["Position"], sep.inputs[0])
        dust = math("MULTIPLY", math("SUBTRACT", 1.0, math("MULTIPLY", sep.outputs["Z"], 2.2)), math("ADD", math("MULTIPLY", breakup.outputs["Fac"], 0.7), 0.1))
        base = mix_rgb(math("MULTIPLY", dust, (0.4 if kind in ("cloth", "nylon") else 0.18) if scale_hint > 1.2 else 0.0), base, (0.42, 0.36, 0.28))
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


def _unwrap(obj, boost=None):
    """Smart UV project. boost(face_center) -> scale factor gives chosen regions (faces) more texels."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if obj.data.uv_layers.get("atlas") is None:
        obj.data.uv_layers.new(name="atlas")
    obj.data.uv_layers.active = obj.data.uv_layers["atlas"]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.003, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    if boost is not None:
        import bmesh
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        uv = bm.loops.layers.uv["atlas"]
        bm.faces.ensure_lookup_table()
        # islands: flood fill over shared UV edges
        seen = set()
        for f in bm.faces:
            if f.index in seen:
                continue
            k = boost(f.calc_center_median())
            stack, island = [f], []
            seen.add(f.index)
            while stack:
                g = stack.pop()
                island.append(g)
                for l in g.loops:
                    for e_f in l.edge.link_faces:
                        if e_f.index in seen:
                            continue
                        # same island if the UVs of the shared edge match
                        a0, a1 = l[uv].uv, l.link_loop_next[uv].uv
                        ok = False
                        for l2 in e_f.loops:
                            if l2.edge == l.edge:
                                b0, b1 = l2[uv].uv, l2.link_loop_next[uv].uv
                                ok = ((a0 - b1).length < 1e-5 and (a1 - b0).length < 1e-5) or ((a0 - b0).length < 1e-5 and (a1 - b1).length < 1e-5)
                        if ok:
                            seen.add(e_f.index)
                            stack.append(e_f)
            if k != 1.0:
                for g in island:
                    for l in g.loops:
                        l[uv].uv *= k
        bm.to_mesh(obj.data)
        bm.free()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")


def _image(name, size, non_color, alpha=False):
    img = bpy.data.images.new(name, size, size, alpha=alpha, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img


def _save(img, path, jpeg=False, quality=92):
    """PNG, or JPEG through Pillow (much smaller for 4K color/normal maps)."""
    if not jpeg:
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        return path
    from PIL import Image
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3]
    Image.fromarray((np.clip(px, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path, quality=quality, subsampling=0)
    return path


def bake_model(obj, out_dir, name, size, normal=True, scale_hint=1.0, samples=12, source=None, extrusion=0.012,
               jpeg=False, boost=None, keep_uv=False):
    """Bakes the textures for `obj` and gives it one textured material. Returns written paths.

    source: a high-poly object with the original materials: everything (color, roughness, metal, normals) is
    baked from it onto obj's new UVs (selected-to-active). Without it obj bakes from its own materials.
    jpeg: write albedo and normal as high-quality JPEG (4K sources stay small); the mask is always PNG (alpha)."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    if not keep_uv:
        _unwrap(obj, boost)
    obj.data.uv_layers.active = obj.data.uv_layers["atlas"]
    obj.data.uv_layers["atlas"].active_render = True
    src_obj = source or obj
    if src_obj.data.uv_layers.get("atlas") is None:
        src_obj.data.uv_layers.new(name="atlas")
    if source is not None:
        source.data.uv_layers.active = source.data.uv_layers["atlas"]

    # swap in procedural materials on the source (restored afterwards, so the same source can be baked again)
    originals = list(src_obj.data.materials)
    procs = []
    done = {}
    for i, src in enumerate(list(src_obj.data.materials)):
        if src is None:
            continue
        if src.name not in done:
            done[src.name] = worn_material(src, kind_of(src.name), scale_hint)
        pm, bsdf, out = done[src.name]
        src_obj.data.materials[i] = pm
        if (pm, bsdf, out) not in procs:
            procs.append((pm, bsdf, out))
    # bake target nodes live in the target's materials
    if source is not None:
        tgt = bpy.data.materials.new(name + "_bake")
        tgt.use_nodes = True
        for i in range(len(obj.data.materials)):
            obj.data.materials[i] = tgt
        if not obj.data.materials:
            obj.data.materials.append(tgt)
        targets = [tgt]
    else:
        targets = [pm for pm, _, _ in procs]

    def set_target(img):
        for t in targets:
            nodes = t.node_tree.nodes
            n = nodes.get("BakeTarget") or nodes.new("ShaderNodeTexImage")
            n.name = "BakeTarget"
            n.image = img
            nodes.active = n

    bake = sc.render.bake
    bake.margin = max(4, size // 128)
    bake.use_selected_to_active = source is not None
    bake.cage_extrusion = extrusion
    bake.max_ray_distance = extrusion * 2.5
    bpy.ops.object.select_all(action="DESELECT")
    if source is not None:
        source.hide_render = False
        source.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    kw = dict(use_clear=True, margin=bake.margin, use_selected_to_active=source is not None,
              cage_extrusion=extrusion, max_ray_distance=extrusion * 2.5)
    written = []
    os.makedirs(out_dir, exist_ok=True)

    albedo = _image(name + "_albedo", size, False)
    set_target(albedo)
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, **kw)
    roughness = _image(name + "_rough", size, True)
    set_target(roughness)
    sc.cycles.samples = max(2, samples // 3)
    bpy.ops.object.bake(type="ROUGHNESS", **kw)
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
    bpy.ops.object.bake(type="EMIT", **kw)
    for nt, old, out in saved:
        nt.links.new(old, out.inputs[0])
    sc.cycles.samples = samples
    normal_img = None
    if normal:
        normal_img = _image(name + "_normal", size, True)
        set_target(normal_img)
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", **kw)

    ext = ".jpg" if jpeg else ".png"
    written.append(_save(albedo, os.path.join(out_dir, name + "_albedo" + ext), jpeg))
    w = h = size
    r = np.array(roughness.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., 0]
    mtl = np.array(metal.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., 0]
    mask = np.zeros((h, w, 4), dtype=np.float32)
    mask[..., 0] = mtl
    mask[..., 1] = 1.0
    mask[..., 3] = 1.0 - r
    mask_img = _image(name + "_mask", size, True, alpha=True)
    mask_img.pixels[:] = mask.ravel()
    written.append(_save(mask_img, os.path.join(out_dir, name + "_mask.png")))
    if normal_img is not None:
        written.append(_save(normal_img, os.path.join(out_dir, name + "_normal" + ext), jpeg))

    if source is not None:
        for i, m_ in enumerate(originals):
            source.data.materials[i] = m_
    # one simple material for export (albedo on the base color): the game assigns all maps at runtime
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
    # the export keeps only the atlas UVs
    for uvl in [u for u in obj.data.uv_layers if u.name != "atlas"]:
        obj.data.uv_layers.remove(uvl)
    bake.use_selected_to_active = False
    return written
