"""Extract the approved full-detail Blender meshes for Unity's HQ baker."""
import struct
import sys
from pathlib import Path

import bpy
import numpy as np

NAMES = ("LowBranchedWhiteOak", "MultistemEdgeBeechV2", "HazelThicket")


def extract(mesh, target):
    mesh.calc_loop_triangles()
    # Index by face corner to retain the wood UV seams.
    positions = np.empty(len(mesh.vertices) * 3, dtype="<f4")
    normals = np.empty_like(positions)
    vertex = np.empty(len(mesh.loops), dtype="<i4")
    uv = np.empty(len(mesh.loops) * 2, dtype="<f4")
    triangles = np.empty(len(mesh.loop_triangles) * 3, dtype="<i4")
    mesh.vertices.foreach_get("co", positions)
    mesh.vertices.foreach_get("normal", normals)
    mesh.loops.foreach_get("vertex_index", vertex)
    mesh.uv_layers[0].data.foreach_get("uv", uv)
    mesh.loop_triangles.foreach_get("loops", triangles)
    with target.open("wb") as handle:
        handle.write(struct.pack("<iii", len(mesh.vertices), len(vertex), len(triangles)))
        for array in (positions, normals, vertex, uv, triangles):
            array.tofile(handle)


if __name__ == "__main__":
    source, output = (Path(path).resolve() for path in sys.argv[sys.argv.index("--") + 1:])
    output.mkdir(parents=True, exist_ok=True)
    for name in NAMES:
        bpy.ops.wm.open_mainfile(filepath=str(source / (name + ".blend")), load_ui=False, use_scripts=False)
        for part, obj in (("Bark", "Branch and trunk geometry"), ("Foliage", "Individual curved leaf geometry")):
            extract(bpy.data.objects[obj].data, output / (name + part + ".bin"))
