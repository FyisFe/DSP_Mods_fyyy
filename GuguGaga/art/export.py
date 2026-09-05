"""Export the bundled Gugugaga model and render its serialized mesh."""
import gzip
import hashlib
import json
import runpy
import struct
from pathlib import Path
import bpy
from mathutils import Vector

OUT = Path(__file__).resolve().parent
scene = bpy.data.scenes["GuguGaga Studio"]
bpy.context.window.scene = scene
obj = scene.objects["GuguGaga mesh"]
export = runpy.run_path(str(OUT.parents[1] / "IcarusModelReplacement/tools/export_model.py"))["export_model"]
stats = export(obj, OUT.parent / "model", json.loads((OUT / "settings.json").read_text(encoding="utf-8")), True)
stats["source_sha256"] = hashlib.sha256((OUT / "source.glb").read_bytes()).hexdigest()
(OUT / "model-stats.json").write_text(json.dumps(stats, indent=2)+"\n", encoding="utf-8")
path = OUT.parent / "model/mesh.bin.gz"

# Render vertices, UVs and normals reconstructed from the serialized payload.
with gzip.open(path, "rb") as f:
    assert f.read(4) == b"IRMD"
    version, n, m = struct.unpack("<III", f.read(12))
    assert version == 1
    payload = [struct.unpack("<8f4B4f", f.read(52)) for _ in range(n)]
    faces = struct.unpack("<%dI" % m, f.read(m*4))
    assert not f.read(1)
mesh = bpy.data.meshes.new("Export preview")
mesh.from_pydata([(v[0], -v[2], v[1]) for v in payload], [],
                 [faces[i:i+3] for i in range(0, m, 3)])
for polygon in mesh.polygons:
    polygon.use_smooth = True
mesh.normals_split_custom_set_from_vertices([(v[3], -v[5], v[4]) for v in payload])
uv = mesh.uv_layers.new(name="UV")
for loop in mesh.loops:
    uv.data[loop.index].uv = payload[loop.vertex_index][6:8]
mesh.materials.append(obj.data.materials[0])
preview = bpy.data.objects.new("Export preview", mesh)
scene.collection.objects.link(preview)
obj.hide_render = True
saved = scene.camera.matrix_world.copy()
try:
    scene.render.filepath = str(OUT / "game-mesh.png")
    bpy.ops.render.render(write_still=True)
    for name, position in (("front", (0, -9, 2.7)), ("side", (9, 0, 2.7)), ("back", (0, 9, 2.7))):
        scene.camera.location = position
        scene.camera.rotation_euler = (Vector((0, 0, 1.48))-scene.camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(OUT / (name + ".png"))
        bpy.ops.render.render(write_still=True)
finally:
    obj.hide_render = False
    bpy.data.objects.remove(preview, do_unlink=True)
    bpy.data.meshes.remove(mesh)
    scene.camera.matrix_world = saved
    scene.render.filepath = str(OUT / "portrait.png")
print(stats)
