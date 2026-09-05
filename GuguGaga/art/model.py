"""Prepare ReedMan's Cute Gugugaga for DSP. Run with Blender 5.2 Python."""
import math
from pathlib import Path
import bmesh
import bpy
from mathutils import Matrix, Vector

OUT = Path(__file__).resolve().parent
scene = bpy.data.scenes.get("GuguGaga Studio") or bpy.data.scenes.new("GuguGaga Studio")
bpy.context.window.scene = scene
for obj in list(scene.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
for collection in list(scene.collection.children):
    bpy.data.collections.remove(collection)

bpy.ops.import_scene.gltf(filepath=str(OUT / "source.glb"))
obj = next(o for o in scene.objects if o.type == "MESH" and o.name.startswith("tripo_node"))
source_rig = next(o for o in scene.objects if o.type == "ARMATURE")
transform = Matrix.Scale(3.0, 4) @ Matrix.Rotation(-math.pi / 2, 4, "Z")
names = ("Torso", "Head", "LeftWing", "RightWing", "LeftFoot", "RightFoot")
sources = ("Hip", "Head", "R_Upperarm", "L_Upperarm", "R_Calf", "L_Calf")
pivots = [transform @ source_rig.matrix_world @ source_rig.data.bones[n].head_local for n in sources]

deps = bpy.context.evaluated_depsgraph_get()
data = bpy.data.meshes.new_from_object(obj.evaluated_get(deps))
data.transform(transform @ obj.matrix_world)
image = next(n.image for n in obj.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE")
assert image.packed_file is not None
for item in list(scene.objects):
    bpy.data.objects.remove(item, do_unlink=True)

character = bpy.data.collections.new("GuguGaga | Character")
scene.collection.children.link(character)
obj = bpy.data.objects.new("GuguGaga mesh", data)
character.objects.link(obj)
obj.vertex_groups.clear()

# Weld the GLB's per-triangle duplicates while retaining per-corner UVs.
bm = bmesh.new()
bm.from_mesh(data)
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
bm.to_mesh(data)
bm.free()
for polygon in data.polygons:
    polygon.use_smooth = True
data.normals_split_custom_set([(0, 0, 0)] * len(data.loops))
sub = obj.modifiers.new("Smooth silhouette", "SUBSURF")
sub.levels = sub.render_levels = 1
sub.uv_smooth = "PRESERVE_BOUNDARIES"

material = bpy.data.materials.new("GuguGaga texture")
p = material.node_tree.nodes.get("Principled BSDF")
p.inputs["Roughness"].default_value = .9
p.inputs["Specular IOR Level"].default_value = 0
tex = material.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = image
material.node_tree.links.new(tex.outputs["Color"], p.inputs["Base Color"])
# The source is unlit; retain a texture fill while allowing dynamic light and shadows.
material.node_tree.links.new(tex.outputs["Color"], p.inputs["Emission Color"])
p.inputs["Emission Strength"].default_value = .28
data.materials.clear()
data.materials.append(material)

armature = bpy.data.armatures.new("GuguGaga skeleton")
rig = bpy.data.objects.new("GuguGaga rig", armature)
character.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
tails = ((0, 0, 1.40), (0, 0, 2.85), (-.71, .075, .56),
         (.71, .075, .56), (-.26, -.34, .08), (.26, -.34, .08))
for i, (name, pivot) in enumerate(zip(names, pivots)):
    bone = armature.edit_bones.new(name)
    bone.head = pivot
    bone.tail = tails[i]
    if i in (1, 2, 3):
        bone.parent = armature.edit_bones["Torso"]
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
assert tuple(g.name for g in obj.vertex_groups) == names


def smooth_weight(t):
    t = max(0.0, min(1.0, t))
    return t*t*(3-2*t)


# Heat weights follow the connected costume; keep the collar and central belly
# on the torso when flippers rise. Thresholds are specific to this three-unit mesh.
for vertex in data.vertices:
    row = {g.group: g.weight for g in vertex.groups}
    x, y, z = vertex.co
    wing = smooth_weight((abs(x)-.33)/.15) * (1-smooth_weight((z-1.15)/.17))
    torso = row.get(0, 0) if row else 1.0  # The detached buckle has no heat weights.
    for group in (2, 3):
        weight = row.get(group, 0)
        torso += weight*(1-wing)
        obj.vertex_groups[group].add([vertex.index], weight*wing, "REPLACE")
    obj.vertex_groups[0].add([vertex.index], torso, "REPLACE")
bpy.context.view_layer.objects.active = obj
bpy.ops.object.vertex_group_limit_total(limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
studio = bpy.data.collections.new("Studio")
scene.collection.children.link(studio)


def aim(item, target):
    item.rotation_euler = (Vector(target) - item.location).to_track_quat("-Z", "Y").to_euler()


for name, location, energy, size in (("Key", (-3, -4, 6), 350, 4),
                                   ("Fill", (3, -1, 3), 80, 3)):
    light = bpy.data.lights.new(name, "AREA")
    light.energy, light.size = energy, size
    item = bpy.data.objects.new(name, light)
    studio.objects.link(item)
    item.location = location
    aim(item, (0, 0, 1.5))

ground = bpy.data.meshes.new("Ground")
ground.from_pydata([(-200, -200, -.012), (200, -200, -.012),
                   (200, 200, -.012), (-200, 200, -.012)], [], [(0, 1, 2, 3)])
floor = bpy.data.objects.new("Ground", ground)
studio.objects.link(floor)
mat = bpy.data.materials.new("Studio grey")
mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = (.18, .18, .18, 1)
mat.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .85
ground.materials.append(mat)
camera = bpy.data.objects.new("Portrait", bpy.data.cameras.new("Portrait"))
studio.objects.link(camera)
camera.location = (3, -9, 4)
camera.data.type = "ORTHO"
camera.data.ortho_scale = 3.65
aim(camera, (0, 0, 1.48))
scene.camera = camera
scene.world = bpy.data.worlds.new("Studio world")
scene.world.use_nodes = True
scene.world.node_tree.nodes.get("Background").inputs[0].default_value = (.18, .20, .24, 1)
scene.world.node_tree.nodes.get("Background").inputs[1].default_value = .35
scene.render.engine = "CYCLES"
scene.cycles.samples = 64
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 900, 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.render.filepath = str(OUT / "portrait.png")
scene["source"] = "https://sketchfab.com/3d-models/cute-gugugaga-741280967ece40e395a70070d8b31132"
scene["credit"] = "Cute Gugugaga by ReedMan, CC BY 4.0"
image.pack()
for area in bpy.context.screen.areas:
    if area.type == "VIEW_3D":
        area.spaces.active.region_3d.view_perspective = "CAMERA"
        area.spaces.active.shading.type = "MATERIAL"
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "GuguGaga.blend"), compress=True)
print({"file": str(OUT / "GuguGaga.blend"), "base_vertices": len(data.vertices)})
