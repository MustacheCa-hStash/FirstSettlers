"""Generate the oak crown and reduce the active standing tree meshes in Blender.

blender -b --factory-startup --disable-autoexec --python Tools/BuildStandingTreeLevels.py -- SOURCE_DIRECTORY OUTPUT_DIRECTORY OAK_BLEND
Use --crown-only to preview the new full-detail oak before reducing it.
"""

import argparse
from pathlib import Path
import sys

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildRedMapleLevels import reduced_bark, reduced_leaves


# Keep needle coverage on spruce, where bark accounts for most triangles.
SOURCES = {
    "SugarMaple": ("TreeLOD/SugarMapleLOD/SugarMaple_LOD0_P2962_B2100_L864_v04.blend",
                   "sugar_maple_bark_v01_0", "sugarmapleleafcard_1", 248, 142),
    "Birch": ("TreeLOD/BirchLOD/Birch_LOD0_P2640_B1800_L800_v04.blend",
              "birchbarktextureseamless_0", "leafclumpatlas_birch_1", 230, 132),
    "Spruce": ("TreeLOD/SpruceLOD/Spruce_LOD0_v06.blend",
               "sprucebark_0", "leafcard_spruce_01_1", 145, 102),
    "WhitePine": ("Trees/Tree_007_WhitePine.blend",
                  "pinebarktextureseamless_0", "leafclumpatlas_whitepine_1", 288, 168),
}
OAK_SOURCE = "TreeLOD/OakLOD/Oak_LOD0_P2461_B1800_L530_v03.blend"


def open_blend(path):
    bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)


def export(parts, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="STRIP",
                             apply_scale_options="FBX_SCALE_UNITS")
    print(path.name, [(p.name, sum(face.loop_total - 2 for face in p.data.polygons)) for p in parts],
          flush=True)


def branch_components(bark):
    points = np.array([(bark.matrix_world @ v.co)[:] for v in bark.data.vertices])
    neighbors = [[] for _ in points]
    for edge in bark.data.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    remaining = set(range(len(points)))
    while remaining:
        component = [min(remaining)]
        remaining.remove(component[0])
        for v in component:
            for n in neighbors[v]:
                if n in remaining:
                    remaining.remove(n)
                    component.append(n)
        if len(component) >= 8:
            yield points[component]


def oak_foliage(bark, material):
    random = np.random.default_rng(4017)
    sprays = []
    for branch in branch_components(bark):
        # Bias the distal end toward the outer and upper crown. The corresponding
        # inner point keeps the sprays following the bearing of the real branch.
        score = np.linalg.norm(branch[:, :2], axis=1) + .48 * branch[:, 2]
        ranked = branch[np.argsort(score)]
        inner = ranked[:max(2, len(ranked) // 5)].mean(axis=0)
        tip = ranked[-max(2, len(ranked) // 6):].mean(axis=0)
        if tip[2] < 3.3:
            continue
        bearing = tip[:2] - inner[:2]
        if np.linalg.norm(bearing) < .18:
            bearing = tip[:2]
        if np.linalg.norm(bearing) < .01:
            bearing = np.array([1., 0.])
        direction = bearing / np.linalg.norm(bearing)
        size = np.linalg.norm(tip - inner)
        sprays.append((tip, direction, min(size, 4.2)))
        if size > 2.0:
            # Secondary spray inside the main one bridges the gaps between tips.
            shoulder = tip * .65 + inner * .35
            shoulder[2] = max(3.45, shoulder[2])
            sprays.append((shoulder, direction, min(size * .55, 2.0)))

    # A pair of asymmetrical upper sprays softens the join between the tall leaders.
    sprays += [(np.array([.15, .1, 9.85]), np.array([.8, .6]), 2.4),
               (np.array([-1.05, -.9, 8.65]), np.array([-.6, -.8]), 1.9)]
    weights = np.array([.6 + np.sqrt(size) for _, _, size in sprays])
    budgets = np.floor(weights / weights.sum() * 640).astype(int)
    budgets[np.argsort(-weights)[:640 - budgets.sum()]] += 1
    vertices, faces, normals, uv_by_vertex = [], [], [], []
    for (tip, direction, size), count in zip(sprays, budgets):
        sideways = np.array([-direction[1], direction[0]])
        for _ in range(count):
            along = random.uniform(-1.0, 1.0)
            across = random.uniform(-1.0, 1.0) * np.sqrt(1 - along ** 2)
            center = tip.copy()
            center[:2] += direction * (.28 + along * (.80 + .17 * size))
            center[:2] += sideways * across * (.75 + .13 * size)
            center[2] += .58 + .22 * along - .30 * across ** 2 + random.uniform(-.55, .55)
            # Break up the roof line. Some near-vertical sprays fill a view
            # from below, while broad oblique sprays give the crown its tiers.
            azimuth = random.uniform(0, 2 * np.pi)
            rise = random.uniform(.29, .92)
            normal = np.array([np.cos(azimuth) * np.sqrt(1 - rise * rise),
                               np.sin(azimuth) * np.sqrt(1 - rise * rise), rise])
            side = np.array([-np.sin(azimuth), np.cos(azimuth), 0.])
            vertical = np.cross(normal, side)
            roll = random.uniform(-.75, .75)
            u = side * np.cos(roll) + vertical * np.sin(roll)
            v = -side * np.sin(roll) + vertical * np.cos(roll)
            half_width = random.uniform(.80, 1.11)
            half_height = half_width * random.uniform(.72, .99)
            start = len(vertices)
            for s, t in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                vertices.append(center + s * half_width * u + t * half_height * v)
                uv_by_vertex.append(((s + 1) * .5, (t + 1) * .5))
                normals.append(normal)
            faces.extend(((start, start + 1, start + 2), (start, start + 2, start + 3)))

    mesh = bpy.data.meshes.new("Layered oak crown")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        uv.data[loop.index].uv = uv_by_vertex[loop.vertex_index]
    mesh.normals_split_custom_set_from_vertices(normals)
    mesh.materials.append(material)
    obj = bpy.data.objects.new("Foliage", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def build_crown(source, output, editable):
    open_blend(source / OAK_SOURCE)
    bark = bpy.data.objects["white_oak_bark_v01_0"]
    foliage = bpy.data.objects["oakleafcard.png_1"]
    new_foliage = oak_foliage(bark, foliage.data.materials[0])
    bpy.data.objects.remove(foliage, do_unlink=True)
    bark.name = "Bark"
    export((bark, new_foliage), output / "OakCrown_v04_LOD0.fbx")
    editable.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(editable))


def build_levels(bark, leaf, output, name, counts):
    for level, bark_ratio, count, growth in zip((1, 2), (.52, .23), counts, (1.29, 1.56)):
        parts = (reduced_bark(bark, bark_ratio), reduced_leaves(leaf, count, growth))
        export(parts, output / f"{name}_LOD{level}.fbx")
        for part in parts:
            bpy.data.objects.remove(part, do_unlink=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("oak_blend", type=Path)
    parser.add_argument("--crown-only", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    output = args.output.resolve()
    if args.crown_only:
        build_crown(args.source.resolve(), output, args.oak_blend.resolve())
    else:
        for species, (path, bark, foliage, mid, far) in SOURCES.items():
            open_blend(args.source.resolve() / path)
            build_levels(bpy.data.objects[bark], bpy.data.objects[foliage], output, species, (mid, far))
        open_blend(args.oak_blend.resolve())
        build_levels(bpy.data.objects["Bark"], bpy.data.objects["Foliage"], output, "OakCrown_v04", (366, 209))
