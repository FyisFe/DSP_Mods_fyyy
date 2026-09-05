"""Export one atlas-textured mesh and its optional armature as an Icarus model pack."""
import argparse
import gzip
import json
import math
import struct
import sys
from pathlib import Path
import bpy
from mathutils import Matrix


def export_model(obj, directory, settings, double_sided=False):
    directory = Path(directory)
    if obj.type != "MESH" or len(obj.data.materials) != 1:
        raise ValueError("Select one mesh with one atlas material")
    bpy.context.view_layer.update()
    shader = obj.data.materials[0].node_tree.nodes.get("Principled BSDF")
    links = shader.inputs["Base Color"].links if shader else []
    if len(links) != 1 or links[0].from_node.type != "TEX_IMAGE":
        raise ValueError("Principled Base Color must link directly to the PNG atlas")
    image = links[0].from_node.image
    texture = bytes(image.packed_file.data) if image.packed_file else Path(bpy.path.abspath(image.filepath)).read_bytes()
    if texture[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("The atlas must be a PNG")
    rig = obj.find_armature()
    bones = []
    if rig:
        if any(b.matrix_basis != Matrix.Identity(4) for b in rig.pose.bones):
            raise ValueError("Export the armature in its rest pose")

        def add(bone):
            if bone.parent and bone.parent not in bones:
                add(bone.parent)
            if bone not in bones:
                bones.append(bone)

        for bone in rig.data.bones:
            add(bone)
    names = [b.name for b in bones] if rig else ["Body"]
    if not 1 <= len(names) <= 256 or "$root" in names:
        raise ValueError("Use 1..256 bones; $root is reserved")
    bone_indices = {name: i for i, name in enumerate(names)}
    group_indices = {g.index: bone_indices[g.name] for g in obj.vertex_groups if g.name in bone_indices}

    def unity(v):
        return v.x, v.z, -v.y

    info = dict(settings)
    info["format"] = 1
    info["bones"] = [{"name": b.name, "parent": bone_indices[b.parent.name] if b.parent else -1,
                      "position": unity(rig.matrix_world @ b.head_local)} for b in bones] if rig else [
                      {"name": "Body", "parent": -1, "position": [0, 0, 0]}]
    for key in ("name", "author", "license", "scale"):
        if key not in info:
            raise ValueError("Missing setting: " + key)
    for motion in info.get("motions", []):
        if motion["target"] not in names + ["$root"] or motion["signal"] not in (
                "Constant", "Stride", "Step", "LeftStep", "RightStep", "Air", "Sail"):
            raise ValueError("Invalid motion binding: " + str(motion))

    data = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    vertices, indices, remap = [], [], {}
    dropped = 0
    try:
        data.calc_loop_triangles()
        uv = data.uv_layers.active
        if uv is None:
            raise ValueError("Mesh requires an atlas UV map")
        weights = []
        for vertex in data.vertices:
            row = sorted(((group_indices[g.group], g.weight) for g in vertex.groups
                          if g.group in group_indices and g.weight > 0),
                         key=lambda x: -x[1]) if rig else [(0, 1)]
            dropped = max(dropped, sum(w for i, w in row[4:]))
            row = row[:4]
            total = sum(w for i, w in row)
            if total <= 0:
                raise ValueError("Unweighted vertex: " + str(vertex.index))
            weights.append([(i, w/total) for i, w in row] + [(0, 0)]*(4-len(row)))
        if dropped >= .02:
            raise ValueError("Limit the mesh to four bone influences before export")
        world = obj.matrix_world.copy()
        normal_matrix = world.to_3x3().inverted().transposed()
        mirrored = world.determinant() < 0
        for tri in data.loop_triangles:
            if tri.area <= 0:
                raise ValueError("Degenerate triangle")
            corners = list(zip(tri.vertices, tri.loops))
            if mirrored:
                corners.reverse()
            for index, loop in corners:
                normal = (normal_matrix @ data.corner_normals[loop].vector).normalized()
                coord = tuple(uv.data[loop].uv)
                key = index, coord, tuple(normal)
                if key not in remap:
                    remap[key] = len(vertices)
                    row = weights[index]
                    vertices.append((*unity(world @ data.vertices[index].co), *unity(normal), *coord,
                                     *(i for i, w in row), *(w for i, w in row)))
                indices.append(remap[key])
    finally:
        bpy.data.meshes.remove(data)
    if double_sided:
        count = len(vertices)
        vertices += [(*v[:3], *(-n for n in v[3:6]), *v[6:]) for v in vertices]
        front = indices[:]
        indices += [count+i for t in range(0, len(front), 3) for i in reversed(front[t:t+3])]
    if not 3 <= len(vertices) <= 250000 or not 3 <= len(indices) <= 1500000:
        raise ValueError("Mesh exceeds the model format limits")
    if not all(math.isfinite(v) for row in vertices for v in row):
        raise ValueError("Non-finite mesh value")
    if not all(0 <= v[6] <= 1 and 0 <= v[7] <= 1 for v in vertices):
        raise ValueError("Atlas UVs must be within 0..1")
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / "mesh.bin.gz"
    with path.open("wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as f:
        f.write(struct.pack("<4sIII", b"IRMD", 1, len(vertices), len(indices)))
        for vertex in vertices:
            f.write(struct.pack("<8f4B4f", *vertex))
        f.write(struct.pack("<%dI" % len(indices), *indices))
    (directory / "texture.png").write_bytes(texture)
    (directory / "model.json").write_text(json.dumps(info, ensure_ascii=False, indent=2, allow_nan=False)+"\n", encoding="utf-8")
    return {"vertices": len(vertices), "triangles": len(indices)//3, "bones": len(names),
            "draws": 1, "max_dropped_weight": dropped, "mesh_bytes": path.stat().st_size}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--object", required=True)
    parser.add_argument("--scene")
    parser.add_argument("--settings", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--double-sided", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    if args.scene:
        bpy.context.window.scene = bpy.data.scenes[args.scene]
    print(export_model(bpy.context.scene.objects[args.object], args.output,
                       json.loads(Path(args.settings).read_text(encoding="utf-8")), args.double_sided))
