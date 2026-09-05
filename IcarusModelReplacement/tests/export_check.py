"""Blender integration check: static and arbitrarily named/ordered rigs load in C#."""
import json
import runpy
import struct
import subprocess
import sys
import tempfile
from pathlib import Path
import gzip
import bpy
from mathutils import Matrix

root = Path(__file__).resolve().parents[1]
export = runpy.run_path(str(root / "tools/export_model.py"))["export_model"]
png, dll = map(Path, sys.argv[sys.argv.index("--")+1:])
scene = bpy.data.scenes.new("Export check")
bpy.context.window.scene = scene
mesh = bpy.data.meshes.new("Triangle")
mesh.from_pydata([(0, 0, 0), (1, 0, 0), (0, 0, 1)], [], [(0, 1, 2)])
mesh.uv_layers.new(name="UV")
obj = bpy.data.objects.new("Triangle", mesh)
scene.collection.objects.link(obj)
obj.matrix_world = Matrix.Translation((1, 2, 3)) @ Matrix.Diagonal((-1, 1, 1, 1))
material = bpy.data.materials.new("Atlas")
texture = material.node_tree.nodes.new("ShaderNodeTexImage")
texture.image = bpy.data.images.load(str(png.resolve()))
material.node_tree.links.new(texture.outputs["Color"], material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
mesh.materials.append(material)
settings = {"name": "Export check", "author": "Test", "license": "CC0", "scale": 1}
with tempfile.TemporaryDirectory(prefix="icarus-export-check-") as temporary:
    folder = Path(temporary)
    for skinned in (False, True):
        if skinned:
            rig = bpy.data.objects.new("Unrelated rig", bpy.data.armatures.new("Unrelated rig"))
            scene.collection.objects.link(rig)
            bpy.context.view_layer.objects.active = rig
            rig.select_set(True)
            bpy.ops.object.mode_set(mode="EDIT")
            fin = rig.data.edit_bones.new("Fin")
            base = rig.data.edit_bones.new("Base")
            mast = rig.data.edit_bones.new("Mast")
            for i, bone in enumerate((base, mast, fin)):
                bone.head, bone.tail = (0, 0, i*.3), (0, 0, i*.3+.2)
            fin.parent, mast.parent = mast, base
            bpy.ops.object.mode_set(mode="OBJECT")
            obj.vertex_groups.new(name="Fin").add([0, 1, 2], 1, "REPLACE")
            obj.modifiers.new("Skin", "ARMATURE").object = rig
            settings["motions"] = [{"target": "Fin", "signal": "Air", "rotation": [15, 0, 0]}]
        stats = export(obj, folder, settings)
        info = json.loads((folder / "model.json").read_text())
        assert [b["name"] for b in info["bones"]] == (["Base", "Mast", "Fin"] if skinned else ["Body"])
        payload = gzip.decompress((folder / "mesh.bin.gz").read_bytes())
        vertex = struct.unpack_from("<8f4B4f", payload, 16)
        assert vertex[8] == (2 if skinned else 0)
        assert max(abs(a-b) for a, b in zip(vertex[:3], (1, 4, -2))) < 1e-5, ("world transform and mirrored winding", vertex[:3])
        subprocess.run([str(root / "tests/bin/Release/net472/Checks.exe"), str(folder), str(dll.resolve())], check=True)
        print({"skinned": skinned, **stats})
print("PASS: generic Blender exporter -> external files -> actual C# loader")
