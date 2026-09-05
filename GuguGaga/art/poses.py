"""Check skinning and culling bounds at the walking/flight extremes; render each pose."""
import math
import json
from pathlib import Path
import bpy
from mathutils import Matrix, Vector

scene = bpy.data.scenes["GuguGaga Studio"]
bpy.context.window.scene = scene
rig, obj = scene.objects["GuguGaga rig"], scene.objects["GuguGaga mesh"]
out = Path(__file__).resolve().parent
info = json.loads((out.parent / "model/model.json").read_text(encoding="utf-8"))
names = [b["name"] for b in info["bones"]]
C = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
inverse = C.inverted()
pivots = [C @ rig.data.bones[n].head_local for n in names]
T = Matrix.Translation


def rotate(degrees, axis):
    return Matrix.Rotation(math.radians(degrees), 4, axis)


bind = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
original = [v.co.copy() for v in bind.vertices]
weights = [[(names.index(obj.vertex_groups[g.group].name), g.weight) for g in v.groups] for v in bind.vertices]
lo = [min(v[a] for v in original)-info["boundsPadding"] for a in range(3)]
hi = [max(v[a] for v in original)+info["boundsPadding"] for a in range(3)]
reports = []
try:
    for title, stride, air in (("walk-left", 1, 0), ("walk-right", -1, 0), ("flight", 0, 1)):
        signals = {"Constant": 1, "Stride": stride, "Step": abs(stride),
                   "LeftStep": max(0, stride), "RightStep": max(0, -stride), "Air": air, "Sail": 0}
        matrices = []
        for i, bone in enumerate(info["bones"]):
            position, rotation = Vector(), Vector()
            for motion in info["motions"]:
                if motion["target"] == bone["name"]:
                    position += Vector(motion.get("position", (0, 0, 0))) * signals[motion["signal"]]
                    rotation += Vector(motion.get("rotation", (0, 0, 0))) * signals[motion["signal"]]
            parent = bone["parent"]
            local = T(pivots[i]-(pivots[parent] if parent >= 0 else Vector())+position)
            # Unity Euler applies Z, X, Y.
            local = local @ rotate(rotation.y, "Y") @ rotate(rotation.x, "X") @ rotate(rotation.z, "Z")
            matrices.append(matrices[parent] @ local if parent >= 0 else local)
        deform = [inverse @ matrices[i] @ T(-pivots[i]) @ C for i in range(len(names))]
        for i, name in enumerate(names):
            rig.pose.bones[name].matrix = deform[i] @ rig.data.bones[name].matrix_local
            bpy.context.view_layer.update()
        posed = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        error = max((sum(((deform[g] @ original[i])*w for g, w in weights[i]), Vector())-v.co).length
                    for i, v in enumerate(posed.vertices))
        assert error < 1e-4, (title, error)
        assert all(lo[a] <= v.co[a] <= hi[a] for v in posed.vertices for a in range(3)), title+" culling bounds"
        reports.append({"pose": title, "skin_error": error})
        bpy.data.meshes.remove(posed)
        scene.render.filepath = str(out/(title+".png"))
        bpy.ops.render.render(write_still=True)
finally:
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    bpy.data.meshes.remove(bind)
    scene.render.filepath = str(out/"portrait.png")
print({"poses": reports})
