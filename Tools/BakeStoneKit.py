"""Bake S03/S04/S06/S07/S12 onto minimal solids and one shared 2K texture pair.

Blender --background --python Tools/BakeStoneKit.py
Original .blend files and detailed exports are retained as authoring sources.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/StoneBuildKit/BakedKit'
OUT = ROOT / 'Assets/Models/Buildings/Stone'
TEX = ROOT / 'Assets/Textures/Buildings/Stone'
SIZE, BORDER, GUTTER = 2048, 72, 16
for directory in (ART, OUT, TEX):
    directory.mkdir(parents=True, exist_ok=True)
sources = [ROOT / 'ArtReferences/StoneBuildKit/Models/StoneBuildKit.blend',
           ROOT / 'ArtReferences/StoneBuildKit/Boundary/StoneBoundaryKit.blend']
hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.render.threads_mode = 'FIXED'
scene.render.threads = 12
configs = [
    ('S03', 0, ('S03_Course', 'S03_RecessedPacking'), 716, 12,
     [((-.25, 0, -.25), (.25, 3, .25))]),
    ('S04', 0, ('S04_Flagstone', 'S04_PackedBedding'), 2168, 12,
     [((0, -.25, 0), (4, 0, 4))]),
    ('S06', 0, ('S06_Capstone',), 264, 72, None),
    ('S07', 1, ('S07_Packing', 'S07_Rubble'), 852, 12,
     [((0, 0, -.25), (4, 1.25, .25))]),
    ('S12', 1, ('S12_LeftWing_', 'S12_RightWing_'), 584, 24,
     [((0, 0, -.25), (1.375, 1.25, .25)), ((2.625, 0, -.25), (4, 1.25, .25))]),
]
groups, boxes = [], []
for index, (tag, library, prefixes, before, after, bounds) in enumerate(configs):
    with bpy.data.libraries.load(str(sources[library]), link=False) as (src, dst):
        dst.objects = [name for name in src.objects if name.startswith(prefixes) and '_Export' not in name]
    high = [o for o in dst.objects if o is not None and o.type == 'MESH']
    assert high, tag
    for obj in high:
        scene.collection.objects.link(obj)
        obj.location = (index * 12, 0, 0)
        obj.hide_set(False)
        obj.hide_render = False
        if library == 1 and '_Rubble_' in obj.name:
            # The boundary source has perfectly sharp extruded front edges. Their vertical
            # faces have zero projected width in a normal bake. A tiny bake-only bevel
            # supplies readable joint normals without adding any runtime geometry.
            bpy.context.view_layer.objects.active = obj
            obj.select_set(True)
            modifier = obj.modifiers.new('Bake-only readable joint edge', 'BEVEL')
            modifier.width = .008
            modifier.segments = 1
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            obj.select_set(False)
    if bounds is None:
        # Keep the real gaps between the six capstones, removing their bevel geometry only.
        bounds = []
        for obj in sorted(high, key=lambda o: o.name):
            points = [(v.co.x, v.co.z, v.co.y) for v in obj.data.vertices]
            bounds.append((tuple(min(p[a] for p in points) for a in range(3)),
                           tuple(max(p[a] for p in points) for a in range(3))))
        assert len(bounds) == 6
    groups.append({'tag': tag, 'high': high, 'low': [], 'bounds': bounds,
                   'before': before, 'after': after, 'offset': index * 12})
    for part, (lo, hi) in enumerate(bounds):
        boxes.append({'tag': tag, 'part': part, 'lo': lo, 'hi': hi})

# MaxRects packing, with padded islands and a border compatible with stone UV validation.
requests = []
for box in boxes:
    x, y, z = [b - a for a, b in zip(box['lo'], box['hi'])]
    for face, w, h in [('front', x, y), ('back', x, y), ('left', z, y),
                       ('right', z, y), ('top', x, z), ('bottom', x, z)]:
        density = 224 if box['tag'] == 'S04' and face == 'top' else 64 if box['tag'] == 'S04' and face == 'bottom' else 192
        requests.append((math.ceil(w * density), math.ceil(h * density), box['tag'], box['part'], face, density))
free = [(BORDER, BORDER, SIZE - 2 * BORDER, SIZE - 2 * BORDER)]
rects = {}
for width, height, tag, part, face, density in sorted(requests, key=lambda r: max(r[:2]), reverse=True):
    w, h = width + 2 * GUTTER, height + 2 * GUTTER
    options = [(min(fw - w, fh - h), max(fw - w, fh - h), x, y)
               for x, y, fw, fh in free if w <= fw and h <= fh]
    assert options, ('Atlas did not fit', tag, part, face)
    _, _, x, y = min(options)
    split = []
    for fx, fy, fw, fh in free:
        if x >= fx + fw or x + w <= fx or y >= fy + fh or y + h <= fy:
            split.append((fx, fy, fw, fh))
            continue
        if x > fx: split.append((fx, fy, x - fx, fh))
        if x + w < fx + fw: split.append((x + w, fy, fx + fw - x - w, fh))
        if y > fy: split.append((fx, fy, fw, y - fy))
        if y + h < fy + fh: split.append((fx, y + h, fw, fy + fh - y - h))
    free = [a for n, a in enumerate(split) if not any(
        n != m and a[0] >= b[0] and a[1] >= b[1] and a[0] + a[2] <= b[0] + b[2]
        and a[1] + a[3] <= b[1] + b[3] and (a != b or n > m) for m, b in enumerate(split))]
    rects[(tag, part, face)] = (x + GUTTER, y + GUTTER, width, height, density)

material = bpy.data.materials.new('Baked stone kit shared atlas')
material.use_nodes = True
target = material.node_tree.nodes.new('ShaderNodeTexImage')
material.node_tree.nodes.active = target


def map_uv(obj, box, variant):
    lo, hi = box['lo'], box['hi']
    layer = obj.data.uv_layers.active
    for face in obj.data.polygons:
        for li in face.loop_indices:
            x, z, y = obj.data.vertices[obj.data.loops[li].vertex_index].co
            nx, nz, ny = face.normal
            if variant in (1, 2): x, nx = lo[0] + hi[0] - x, -nx
            if variant == 2: z, nz = lo[2] + hi[2] - z, -nz
            if abs(nz) > .5:
                key, u, v = ('front' if nz < 0 else 'back'), (x - lo[0]) / (hi[0] - lo[0]), (y - lo[1]) / (hi[1] - lo[1])
            elif abs(nx) > .5:
                key, u, v = ('left' if nx < 0 else 'right'), (z - lo[2]) / (hi[2] - lo[2]), (y - lo[1]) / (hi[1] - lo[1])
            else:
                key, u, v = ('bottom' if ny < 0 else 'top'), (x - lo[0]) / (hi[0] - lo[0]), (z - lo[2]) / (hi[2] - lo[2])
            a, b, w, h, _ = rects[(box['tag'], box['part'], key)]
            layer.data[li].uv = ((a + u * w) / SIZE, (b + v * h) / SIZE)


for box in boxes:
    lo, hi = box['lo'], box['hi']
    verts = [(x, z, y) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    data = bpy.data.meshes.new(box['tag'] + '_Solid' + str(box['part']))
    data.from_pydata(verts, [], faces)
    bm = bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True) > 0
    bm.to_mesh(data); bm.free(); data.update()
    data.materials.append(material); data.uv_layers.new(name='UVMap')
    obj = bpy.data.objects.new(data.name, data); scene.collection.objects.link(obj)
    group = next(g for g in groups if g['tag'] == box['tag'])
    obj.location = (group['offset'], 0, 0)
    group['low'].append((obj, box)); map_uv(obj, box, 0)
    obj.hide_render = True; obj.hide_set(True)

bpy.ops.object.select_all(action='DESELECT')
copies = []
for group in groups:
    for obj, _ in group['low']:
        copy = obj.copy(); copy.data = obj.data.copy(); scene.collection.objects.link(copy)
        copy.hide_render = False; copy.hide_set(False); copy.select_set(True); copies.append(copy)
bpy.context.view_layer.objects.active = copies[0]
bpy.ops.object.join()
low = bpy.context.object
for group in groups:
    for obj in group['high']: obj.select_set(True)
bpy.context.view_layer.objects.active = low
scene.render.bake.use_selected_to_active = True
scene.render.bake.cage_extrusion = .075
scene.render.bake.max_ray_distance = .20
scene.render.bake.margin = GUTTER
scene.render.bake.use_pass_direct = False
scene.render.bake.use_pass_indirect = False
scene.render.bake.use_pass_color = True


def bake(kind, space):
    image = bpy.data.images.new('StoneKit_' + kind, width=SIZE, height=SIZE, alpha=True)
    image.colorspace_settings.name = space
    image.generated_color = (.5, .5, 1, 1) if kind == 'NORMAL' else (.3, .3, .3, 1)
    target.image = image
    print('BAKE_START', kind, flush=True)
    bpy.ops.object.bake(type=kind)
    print('BAKE_DONE', kind, flush=True)
    return image


colour = bake('DIFFUSE', 'sRGB')
normal = bake('NORMAL', 'Non-Color')
ao = bake('AO', 'Non-Color')
pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32); occlusion = np.empty_like(pixels)
colour.pixels.foreach_get(pixels); ao.pixels.foreach_get(occlusion)
pixels[3::4] = np.clip(occlusion[0::4], 0, 1); colour.pixels.foreach_set(pixels)
for image, filename in [(colour, 'BakedStoneKit_ColourAO.png'), (normal, 'BakedStoneKit_Normal.png')]:
    image.filepath_raw = str(TEX / filename); image.file_format = 'PNG'; image.save()

report = {'sourceSHA256': hashes, 'atlasSize': SIZE, 'sharedTexturePairs': 1,
          'colour': 'unlit RGB, AO in alpha', 'normals': 'tangent-space +Y',
          'boundaryBakeOnlyBevelMetres': .008, 'models': []}
for group in groups:
    files = []
    for variant in range(3):
        bpy.ops.object.select_all(action='DESELECT'); copies = []
        for obj, box in group['low']:
            map_uv(obj, box, variant)
            copy = obj.copy(); copy.data = obj.data.copy(); scene.collection.objects.link(copy)
            copy.location = (0, 0, 0); copy.hide_render = False; copy.hide_set(False); copy.select_set(True); copies.append(copy)
        bpy.context.view_layer.objects.active = copies[0]
        if len(copies) > 1: bpy.ops.object.join()
        joined = bpy.context.object
        joined.name = group['tag'] + '_Baked' + ('_UV' + str(variant) if variant else '')
        joined.data.calc_loop_triangles()
        assert len(joined.data.loop_triangles) == group['after']
        filename = group['tag'] + '_Baked' + ('_UV' + str(variant) if variant else '') + '.fbx'
        bpy.ops.export_scene.fbx(filepath=str(OUT / filename), use_selection=True, object_types={'MESH'},
            axis_forward='Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
            use_tspace=True, path_mode='STRIP', bake_anim=False)
        files.append(filename); joined.hide_render = True; joined.hide_set(True)
    report['models'].append({'tag': group['tag'], 'before': group['before'], 'after': group['after'],
                             'solids': len(group['bounds']), 'bounds': group['bounds'], 'files': files})
for p in sources: assert hashlib.sha256(p.read_bytes()).hexdigest() == hashes[str(p.relative_to(ROOT))]
report['atlasIslands'] = [{'tag': k[0], 'solid': k[1], 'face': k[2], 'rectPixels': v[:4], 'pixelsPerMetre': v[4]}
                          for k, v in rects.items()]
(ART / 'bake-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('STONE_KIT_BAKE_PASS', json.dumps(report['models']), flush=True)
