"""Bake split-plank detail onto one profiled panel and three simple rear rails.

Blender --background --python Tools/BakeSplitPlankWall.py
Preserves the selected wood trim regions for generated bearing timber.
"""
import hashlib
import json
from pathlib import Path
import re
import math

import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/Models/Buildings/Wood/Modular/SplitPlankWall_4.00Wx3.00Hx0.25D.fbx'
ART = ROOT / 'ArtReferences/ModularWoodKit/BakedWall'
OUT = ROOT / 'Assets/Models/Buildings/Wood/Modular/SplitPlankWall_Baked.fbx'
TEX = ROOT / 'Assets/Textures/Buildings/Wood'
SIZE, PADDING = 2048, 16
ART.mkdir(parents=True, exist_ok=True)
material_text = (ROOT / 'Assets/Materials/Buildings/Wood/SplitPlankWood.mat').read_text()
guid = re.search(r'_BaseMap:.*?guid: (\w+)', material_text, re.S).group(1)
texture = next(Path(str(p)[:-5]) for p in TEX.glob('*.png.meta') if 'guid: ' + guid in p.read_text())
signature = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in (SOURCE, texture)}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))
high = next(o for o in bpy.context.selected_objects if o.type == 'MESH')
bpy.context.view_layer.objects.active = high
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
high.name = 'Detailed wall bake source'
image = bpy.data.images.load(str(texture), check_existing=True)
wood = bpy.data.materials.new('Selected painted timber bake source'); wood.use_nodes = True
nodes = wood.node_tree.nodes; sample = nodes.new('ShaderNodeTexImage'); sample.image = image
wood.node_tree.links.new(sample.outputs['Color'], nodes.get('Principled BSDF').inputs['Base Color'])
high.data.materials.clear(); high.data.materials.append(wood)
for face in high.data.polygons: face.material_index = 0

# Connected source pieces establish the real board heights and rail bounds.
todo = set(range(len(high.data.vertices))); adjacency = {i: set() for i in todo}
for edge in high.data.edges:
    a, b = edge.vertices; adjacency[a].add(b); adjacency[b].add(a)
parts = []
while todo:
    ids = {todo.pop()}; visit = list(ids)
    while visit:
        for i in adjacency[visit.pop()]:
            if i in todo: todo.remove(i); ids.add(i); visit.append(i)
    points = [(v.co.x, v.co.z, v.co.y) for i in ids for v in [high.data.vertices[i]]]
    lo = tuple(round(min(p[a] for p in points), 5) for a in range(3))
    hi = tuple(round(max(p[a] for p in points), 5) for a in range(3))
    parts.append((lo, hi))
boards = sorted((a, b) for a, b in parts if b[0] - a[0] < .3)
rails = sorted((a, b) for a, b in parts if b[0] - a[0] > 3.9)
assert len(boards) == 16 and len(rails) == 3
segments = []
for lo, hi in boards:
    if segments and abs(segments[-1][2] - hi[1]) < 1e-5:
        segments[-1] = (segments[-1][0], hi[0], hi[1])
    else: segments.append((lo[0], hi[0], hi[1]))
outline = [(0, 0), (4, 0), (4, segments[-1][2])]
for index in range(len(segments) - 1, -1, -1):
    x0, x1, height = segments[index]
    outline.append((x0, height))
    if index: outline.append((x0, segments[index - 1][2]))
z0, z1 = min(a[2] for a, _ in boards), max(b[2] for _, b in boards)
count = len(outline)
vertices = [(x, y, z) for z in (z0, z1) for x, y in outline]
faces = [tuple(range(count - 1, -1, -1)), tuple(range(count, count * 2))]
faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]


def mesh(name, vertices, faces):
    data = bpy.data.meshes.new(name)
    data.from_pydata([(x, z, y) for x, y, z in vertices], [], faces)
    bm = bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True) > 0
    bm.to_mesh(data); bm.free(); data.update()
    obj = bpy.data.objects.new(name, data); bpy.context.scene.collection.objects.link(obj)
    return obj


lows = [mesh('Profiled plank panel', vertices, faces)]
for index, (lo, hi) in enumerate(rails):
    vs = [(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    fs = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    lows.append(mesh('Simple rear rail ' + str(index), vs, fs))

# Preserve grain V=.75..1 and end-grain U=0...5,V=.25...5 exactly for runtime bearing UVs.
# All baked islands occupy the other parts of the same atlas.
free = [(72, 72, 952, 440), (72, 1024, 952, 488), (1024, 72, 952, 1440)]
requests = []
for oi, obj in enumerate(lows):
    obj.data.uv_layers.new(name='UVMap')
    for face in obj.data.polygons:
        points = [(obj.data.vertices[i].co.x, obj.data.vertices[i].co.z, obj.data.vertices[i].co.y) for i in face.vertices]
        normal = (face.normal.x, face.normal.z, face.normal.y)
        dominant = max(range(3), key=lambda a: abs(normal[a])); a, b = [a for a in range(3) if a != dominant]
        lo = [min(p[axis] for p in points) for axis in (a, b)]
        span = [max(p[axis] for p in points) - lo[j] for j, axis in enumerate((a, b))]
        requests.append((max(4, math.ceil(span[0] * 192)), max(4, math.ceil(span[1] * 192)), oi, face.index, a, b, lo, span))
packed = []
for width, height, oi, fi, a, b, lo, span in sorted(requests, key=lambda r: max(r[:2]), reverse=True):
    candidates = []
    for rotate in (False, True):
        w, h = (height, width) if rotate else (width, height)
        w += 2 * PADDING; h += 2 * PADDING
        candidates += [(min(fw - w, fh - h), max(fw - w, fh - h), x, y, w, h, rotate)
                       for x, y, fw, fh in free if w <= fw and h <= fh]
    assert candidates, ('Atlas did not fit', width, height, oi, fi)
    _, _, x, y, w, h, rotate = min(candidates)
    split = []
    for fx, fy, fw, fh in free:
        if x >= fx + fw or x + w <= fx or y >= fy + fh or y + h <= fy:
            split.append((fx, fy, fw, fh)); continue
        if x > fx: split.append((fx, fy, x - fx, fh))
        if x + w < fx + fw: split.append((x + w, fy, fx + fw - x - w, fh))
        if y > fy: split.append((fx, fy, fw, y - fy))
        if y + h < fy + fh: split.append((fx, y + h, fw, fy + fh - y - h))
    free = [v for n, v in enumerate(split) if not any(n != m and v[0] >= q[0] and v[1] >= q[1]
            and v[0] + v[2] <= q[0] + q[2] and v[1] + v[3] <= q[1] + q[3] and (v != q or n > m) for m, q in enumerate(split))]
    obj = lows[oi]; face = obj.data.polygons[fi]
    for li in face.loop_indices:
        v = obj.data.vertices[obj.data.loops[li].vertex_index].co; p = (v.x, v.z, v.y)
        u, v = [(p[axis] - lo[j]) / span[j] for j, axis in enumerate((a, b))]
        if rotate: u, v = v, u
        obj.data.uv_layers.active.data[li].uv = ((x + PADDING + u * (w - 2 * PADDING)) / SIZE,
                                               (y + PADDING + v * (h - 2 * PADDING)) / SIZE)
    packed.append({'part': oi, 'face': fi, 'rect': [x, y, w, h]})

target_material = bpy.data.materials.new('Baked split plank target'); target_material.use_nodes = True
target = target_material.node_tree.nodes.new('ShaderNodeTexImage'); target_material.node_tree.nodes.active = target
bpy.ops.object.select_all(action='DESELECT')
for obj in lows: obj.data.materials.append(target_material); obj.select_set(True)
bpy.context.view_layer.objects.active = lows[0]; bpy.ops.object.join(); low = bpy.context.object
low.name = 'SplitPlankWall_Baked'
high.select_set(True); bpy.context.view_layer.objects.active = low
scene = bpy.context.scene; scene.render.engine = 'CYCLES'; scene.cycles.samples = 24
scene.cycles.device = 'CPU'; scene.render.threads_mode = 'FIXED'; scene.render.threads = 12
scene.render.bake.use_selected_to_active = True; scene.render.bake.cage_extrusion = .04
scene.render.bake.max_ray_distance = .13; scene.render.bake.margin = PADDING
scene.render.bake.use_pass_direct = False; scene.render.bake.use_pass_indirect = False; scene.render.bake.use_pass_color = True


def bake(kind, space):
    output = bpy.data.images.new('SplitPlank_' + kind, width=SIZE, height=SIZE, alpha=True)
    output.colorspace_settings.name = space; target.image = output
    print('BAKE_START', kind, flush=True); bpy.ops.object.bake(type=kind); print('BAKE_DONE', kind, flush=True)
    return output


colour = bake('DIFFUSE', 'sRGB'); normal = bake('NORMAL', 'Non-Color'); ao = bake('AO', 'Non-Color')
pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32); occ = np.empty_like(pixels); base = np.empty_like(pixels); norm = np.empty_like(pixels)
colour.pixels.foreach_get(pixels); ao.pixels.foreach_get(occ); image.pixels.foreach_get(base); normal.pixels.foreach_get(norm)
pixels = pixels.reshape(SIZE, SIZE, 4); occ = occ.reshape(SIZE, SIZE, 4)
base = base.reshape(SIZE, SIZE, 4); norm = norm.reshape(SIZE, SIZE, 4)
pixels[:, :, 3] = occ[:, :, 0]
for rows, cols in [(slice(1536, 2048), slice(0, 2048)), (slice(512, 1024), slice(0, 1024))]:
    pixels[rows, cols, :3] = base[rows, cols, :3]; pixels[rows, cols, 3] = 1
    norm[rows, cols, :] = (.5, .5, 1, 1)
colour.pixels.foreach_set(pixels.ravel()); normal.pixels.foreach_set(norm.ravel())
for output, filename in [(colour, 'SplitPlankWall_Baked_ColourAO.png'), (normal, 'SplitPlankWall_Baked_Normal.png')]:
    output.filepath_raw = str(TEX / filename); output.file_format = 'PNG'; output.save()
bpy.ops.object.select_all(action='DESELECT'); low.select_set(True); bpy.context.view_layer.objects.active = low
bpy.ops.export_scene.fbx(filepath=str(OUT), use_selection=True, object_types={'MESH'}, axis_forward='Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
    mesh_smooth_type='FACE', add_leaf_bones=False, use_tspace=True, path_mode='STRIP', bake_anim=False)
low.data.calc_loop_triangles(); triangles = len(low.data.loop_triangles)
assert triangles == 136, triangles
for name, expected in signature.items(): assert hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == expected
report = {'sourceSHA256': signature, 'before': 944, 'after': triangles, 'solids': 4,
          'boardTopSegments': segments, 'rails': rails, 'atlasSize': SIZE, 'pixelsPerMetre': 192,
          'retainedTrimRegions': [[0, 1536, 2048, 512], [0, 512, 1024, 512]],
          'colour': 'unlit RGB, AO in alpha', 'normal': 'tangent-space +Y', 'islands': packed}
(ART / 'bake-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('SPLIT_PLANK_BAKE_PASS', json.dumps({'before': 944, 'after': triangles, 'solids': 4}), flush=True)
