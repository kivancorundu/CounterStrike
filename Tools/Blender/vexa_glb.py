"""
Web preview of a character: the mobile model with its animations (Characters/Anims) and a weapon on the right grip,
as one .glb (one glTF animation per clip). Used for the phone animation viewer.

    python vexa_glb.py <faction> <weapon> <out.glb>        e.g. akinci ak47 akinci.glb
"""
import sys, os
import bpy
from mathutils import Matrix
faction, weapon, out = sys.argv[1], sys.argv[2], sys.argv[3]
M = "/home/user/CounterStrike/Game/Assets/Vexa/Resources/Models"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 30; bpy.context.scene.render.fps_base = 1.0
def imp(p):
    before = set(bpy.data.objects); acts = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=p, axis_forward="-Z", axis_up="Y")
    return [o for o in bpy.data.objects if o not in before], [a for a in bpy.data.actions if a not in acts]
chars, old = imp(f"{M}/Characters/Mobile/{faction}.fbx")
for a in old: bpy.data.actions.remove(a)
arm = next(o for o in chars if o.type == "ARMATURE")
body = next(o for o in chars if o.type == "MESH")
def texmat(name, path, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = rough
    if os.path.exists(path):
        n = m.node_tree.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(path)
        m.node_tree.links.new(n.outputs[0], b.inputs[0])
    return m
body.data.materials.clear(); body.data.materials.append(texmat("body", f"{M}/Characters/Mobile/Textures/{faction}_albedo.png"))
aobjs, acts = imp(f"{M}/Characters/Anims/{faction}.fbx")
for o in aobjs: bpy.data.objects.remove(o)
# weapon on the right grip, as the game places it
wobjs, wold = imp(f"{M}/Weapons/Mobile/{weapon}.fbx")
for a in wold: bpy.data.actions.remove(a)
wm = texmat("weapon", f"{M}/Weapons/Mobile/Textures/{weapon}_albedo.png", 0.5)
for o in wobjs:
    if o.type == "MESH": o.data.materials.clear(); o.data.materials.append(wm)
for o in [o for o in wobjs if o.type == "EMPTY"]: bpy.data.objects.remove(o)
wobjs = [o for o in bpy.data.objects if o.type == "MESH" and o not in (body,)]
bpy.context.view_layer.update()
grip = arm.data.bones["RightGrip"]
rest = arm.matrix_world @ grip.matrix_local
for o in wobjs:
    if o.parent is not None: continue
    mw = o.matrix_world.copy()
    o.parent = arm; o.parent_type = "BONE"; o.parent_bone = "RightGrip"
    o.matrix_parent_inverse = (arm.matrix_world @ grip.matrix_local @ Matrix.Translation((0, grip.length, 0))).inverted()
    o.matrix_basis = Matrix.Translation(rest.to_translation()) @ mw
# one NLA track per clip -> one glTF animation each
arm.animation_data_create()
for a in sorted(acts, key=lambda a: a.name):
    name = a.name.split("|")[-1]
    tr = arm.animation_data.nla_tracks.new(); tr.name = name
    st = tr.strips.new(name, int(a.frame_range[0]), a)
    if hasattr(st, "action_slot") and a.slots: st.action_slot = a.slots[0]
    tr.mute = False
arm.animation_data.action = None
bpy.context.scene.render.fps = 30; bpy.context.scene.render.fps_base = 1.0
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=True,
                          export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_frame_step=1,
                          export_image_format="JPEG", export_jpeg_quality=85, export_yup=True, export_apply=False,
                          export_def_bones=False, export_optimize_animation_size=True)
