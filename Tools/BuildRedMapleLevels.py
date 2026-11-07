"""Build Red Maple render meshes from the verified v03 .blend, using Blender in background mode.

blender -b --factory-startup --disable-autoexec --python Tools/BuildRedMapleLevels.py -- SOURCE OUTPUT
"""

from pathlib import Path
import sys

import bpy
import numpy as np


def leaf_cards(mesh):
    # The source contains disconnected four-vertex leaf cards, each with two faces.
    neighbors = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbors[a].append(b)
        neighbors[b].append(a)
    unseen = set(range(len(neighbors)))
    cards = []
    while unseen:
        vertices = [min(unseen)]
        unseen.remove(vertices[0])
        for vertex in vertices:
            for neighbor in neighbors[vertex]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    vertices.append(neighbor)
        cards.append(sorted(vertices))
    assert all(len(card) == 4 for card in cards), "Expected the v03 leaf-card topology"
    return np.asarray(cards)


def reduced_leaves(source, count, max_growth):
    mesh = source.data
    cards = leaf_cards(mesh)
    points = np.asarray([vertex.co[:] for vertex in mesh.vertices], dtype=float)[cards]
    centers = points.mean(axis=1)
    normal = np.cross(points[:, 1] - points[:, 0], points[:, 2] - points[:, 0])
    length = np.linalg.norm(normal, axis=1)
    assert np.all(length > 0)
    normal /= length[:, None]
    # Allocate samples across space, card size and viewing direction. This
    # preserves separate canopy lobes and avoids thinning an entire facing.
    normalized = (centers - centers.mean(axis=0)) / np.ptp(centers, axis=0)
    distances = np.square(normalized[:, None] - normalized).sum(axis=2)
    distances += 0.025 * (1 - np.square(normal @ normal.T))
    weight = np.sqrt(length / np.median(length)).clip(0.6, 1.6)
    chosen = []
    closest = np.full(len(cards), np.inf)
    index = int(np.argmax(np.linalg.norm(normalized, axis=1) * weight))
    for _ in range(count):
        chosen.append(index)
        closest = np.minimum(closest, distances[index])
        index = int(np.argmax(closest * weight))
    assigned = distances[chosen].argmin(axis=0)
    # Compensate projected leaf coverage, with a tighter cap around the outline.
    assigned_area = np.bincount(assigned, weights=length, minlength=count)
    growth = np.sqrt(assigned_area / length[chosen]).clip(1, max_growth)
    interior = (1 - 0.55 * np.max(np.abs(normalized[chosen]), axis=1) * 2).clip(0.35, 1)
    growth = 1 + (growth - 1) * interior
    retained = dict(zip(chosen, growth))
    vertices = []
    original_vertices = []
    remap = {}
    card_for_vertex = {}
    for index, card in enumerate(cards):
        for vertex in card:
            card_for_vertex[int(vertex)] = index
    for index in chosen:
        factor, center = retained[index], centers[index]
        for vertex in cards[index]:
            remap[int(vertex)] = len(vertices)
            original_vertices.append(int(vertex))
            vertices.append(center + (np.asarray(mesh.vertices[int(vertex)].co) - center) * factor)
    polygons = [poly for poly in mesh.polygons if card_for_vertex[poly.vertices[0]] in retained]
    assert len(polygons) == count * 2
    reduced = bpy.data.meshes.new("Foliage")
    reduced.from_pydata(vertices, [], [[remap[v] for v in poly.vertices] for poly in polygons])
    reduced.update()
    original_loops = []
    for dst, src in zip(reduced.polygons, polygons):
        dst.use_smooth = src.use_smooth
        original_loops.extend(src.loop_indices)
    for original in mesh.uv_layers:
        uv = reduced.uv_layers.new(name=original.name)
        for new_loop, old_loop in enumerate(original_loops):
            uv.data[new_loop].uv = original.data[old_loop].uv
    for original in mesh.color_attributes:
        colors = reduced.color_attributes.new(name=original.name, type=original.data_type, domain=original.domain)
        indices = original_vertices if original.domain == "POINT" else original_loops
        for new_index, old_index in enumerate(indices):
            colors.data[new_index].color = original.data[old_index].color
    if mesh.has_custom_normals:
        reduced.normals_split_custom_set([mesh.corner_normals[i].vector for i in original_loops])
    obj = bpy.data.objects.new("Foliage", reduced)
    bpy.context.collection.objects.link(obj)
    obj.matrix_world = source.matrix_world.copy()
    return obj


def reduced_bark(source, fraction):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = "Bark"
    bpy.context.collection.objects.link(obj)
    modifier = obj.modifiers.new("Bark reduction", "DECIMATE")
    modifier.ratio = fraction
    modifier.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def build(source, output):
    output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    meshes = {obj.name: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}
    leaves = meshes["redmapleleafcard_1"]
    bark = meshes["sugar_maple_bark_v01_0"]
    for level, bark_ratio, cards, growth in ((1, 0.53, 320, 1.28), (2, 0.24, 176, 1.55)):
        parts = (reduced_bark(bark, bark_ratio), reduced_leaves(leaves, cards, growth))
        bpy.ops.object.select_all(action="DESELECT")
        for part in parts:
            part.select_set(True)
        path = output / f"RedMaple_v03_LOD{level}.fbx"
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"MESH"},
                                 axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="STRIP",
                                 apply_scale_options="FBX_SCALE_UNITS")
        print(path.name, [(part.name, sum(poly.loop_total - 2 for poly in part.data.polygons))
                          for part in parts], flush=True)
        for part in parts:
            bpy.data.objects.remove(part, do_unlink=True)


if __name__ == "__main__":
    source_file, output_folder = map(Path, sys.argv[sys.argv.index("--") + 1:])
    build(source_file.resolve(), output_folder.resolve())
