"""Generate separate broad grass tuft FBXs with straight upright cards.

Run: blender --background --python ArtReferences/GrassTuftBroad_Generate.py
The near mesh has three upright cards (18 triangles); the mid mesh has two
upright cards (8 triangles). They pair with T_GrassTuftBroad_Cutout.png.
"""

from pathlib import Path
import math

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
MODEL_DIR = ROOT / "Assets" / "Models" / "Grass+Flowers"


def add_card(vertices, faces, uv_faces, angle, near, mirror):
    sideways = Vector((-math.sin(angle), math.cos(angle), 0.0))
    rows = [
        (-0.004, 0.490, 0.0),
        (0.121, 0.505, 0.33),
        (0.245, 0.500, 0.66),
        (0.370, 0.485, 1.0),
    ] if near else [
        (-0.004, 0.490, 0.0),
        (0.183, 0.505, 0.5),
        (0.370, 0.485, 1.0),
    ]
    first = len(vertices)
    for height, width, _v in rows:
        center = Vector((0.0, 0.0, height))
        vertices.append(tuple(center - sideways * width * 0.5))
        vertices.append(tuple(center + sideways * width * 0.5))
    for row in range(len(rows) - 1):
        a = first + row * 2
        b = a + 1
        c = a + 2
        d = a + 3
        faces.extend(((a, b, c), (b, d, c)))
        low_v = rows[row][2]
        high_v = rows[row + 1][2]
        left, right = (1.0, 0.0) if mirror else (0.0, 1.0)
        uv_faces.extend((
            ((left, low_v), (right, low_v), (left, high_v)),
            ((right, low_v), (right, high_v), (left, high_v)),
        ))


def build(name, near):
    vertices, faces, uv_faces = [], [], []
    add_card(vertices, faces, uv_faces, 0.0, near, False)
    add_card(vertices, faces, uv_faces, math.pi * (2.0 / 3.0 if near else 0.5), near, True)
    if near:
        add_card(vertices, faces, uv_faces, math.pi * 4.0 / 3.0, near, False)

    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon, uv_face in zip(mesh.polygons, uv_faces):
        for loop_index, uv in zip(polygon.loop_indices, uv_face):
            uv_layer.data[loop_index].uv = uv
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=str(MODEL_DIR / (name + ".fbx")),
        use_selection=True,
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
    )
    print(name, "vertices", len(vertices), "triangles", len(faces))
    return obj


bpy.ops.wm.read_factory_settings(use_empty=True)
build("GrassTuftBroad_LOD0", near=True)
far = build("GrassTuftBroad_LOD1", near=False)
far.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "ArtReferences" / "GrassTuftBroad_Source.blend"))
