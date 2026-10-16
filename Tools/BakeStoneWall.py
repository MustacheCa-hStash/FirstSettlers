"""Bake the existing S02 masonry onto one closed 12-triangle box.

Blender --background --python Tools/BakeStoneWall.py
The original StoneBuildKit.blend remains the high-detail authoring source.
Colour has no directional lighting; alpha stores AO, and normals are tangent-space.
"""
import hashlib
import json
from pathlib import Path

import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ArtReferences/StoneBuildKit/Models/StoneBuildKit.blend'
ART = ROOT / 'ArtReferences/StoneBuildKit/BakedWall'
OUT = ROOT / 'Assets/Models/Buildings/Stone'
TEX = ROOT / 'Assets/Textures/Buildings/Stone'
SIZE = 2048
for folder in (ART, OUT, TEX):
    folder.mkdir(parents=True, exist_ok=True)
source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.render.threads_mode = 'FIXED'
scene.render.threads = 12
high = [o for o in bpy.data.objects if o.type == 'MESH' and
        (o.name.startswith('S02_Course') or o.name == 'S02_RecessedPacking')]
assert len(high) == 68, len(high)
for obj in bpy.data.objects:
    obj.hide_render = obj not in high
    obj.hide_set(obj not in high)

# Unity XYZ -> Blender XZY, as in the original kit exporter.
unity = [(x, y, z) for z in (-.25, .25) for y in (0, 3) for x in (0, 4)]
faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4),
         (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
data = bpy.data.meshes.new('S02 baked wall - six flat faces')
data.from_pydata([(x, z, y) for x, y, z in unity], [], faces)
bm = bmesh.new()
bm.from_mesh(data)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(data)
bm.free()
low = bpy.data.objects.new('S02_BakedWall', data)
scene.collection.objects.link(low)
uv = data.uv_layers.new(name='UVMap')
# Independent padded islands at a consistent 232 pixels/metre on every face.
rects = {'front': (80, 1120, 928, 696), 'back': (1040, 1120, 928, 696),
         'top': (80, 880, 928, 116), 'bottom': (1040, 880, 928, 116),
         'left': (80, 80, 116, 696), 'right': (240, 80, 116, 696)}


def map_uv(variant):
    for face in data.polygons:
        for li in face.loop_indices:
            x, z, y = data.vertices[data.loops[li].vertex_index].co
            nx, nz, ny = face.normal
            # Whole-wall reflections keep course heights and all corner mappings coherent.
            if variant in (1, 2):
                x, nx = 4 - x, -nx
            if variant == 2:
                z, nz = -z, -nz
            if abs(nz) > .5:
                key, u, v = ('front' if nz < 0 else 'back'), x / 4, y / 3
            elif abs(nx) > .5:
                key, u, v = ('left' if nx < 0 else 'right'), (z + .25) / .5, y / 3
            else:
                key, u, v = ('bottom' if ny < 0 else 'top'), x / 4, (z + .25) / .5
            a, b, w, h = rects[key]
            uv.data[li].uv = ((a + u * w) / SIZE, (b + v * h) / SIZE)


map_uv(0)
material = bpy.data.materials.new('S02 bake target')
material.use_nodes = True
low.data.materials.append(material)
target = material.node_tree.nodes.new('ShaderNodeTexImage')
material.node_tree.nodes.active = target
low.hide_render = False
low.hide_set(False)
bpy.ops.object.select_all(action='DESELECT')
for obj in high + [low]:
    obj.select_set(True)
bpy.context.view_layer.objects.active = low
scene.render.bake.use_selected_to_active = True
scene.render.bake.cage_extrusion = .06
scene.render.bake.max_ray_distance = .18
scene.render.bake.margin = 16
scene.render.bake.use_pass_direct = False
scene.render.bake.use_pass_indirect = False
scene.render.bake.use_pass_color = True


def bake(name, kind, colourspace):
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=True)
    image.colorspace_settings.name = colourspace
    image.generated_color = (.5, .5, 1, 1) if kind == 'NORMAL' else (.3, .3, .3, 1)
    target.image = image
    print('BAKE_START', kind, flush=True)
    bpy.ops.object.bake(type=kind)
    print('BAKE_DONE', kind, flush=True)
    return image


colour = bake('S02 colour - no light', 'DIFFUSE', 'sRGB')
normal = bake('S02 tangent normals', 'NORMAL', 'Non-Color')
ao = bake('S02 occlusion', 'AO', 'Non-Color')
pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
occlusion = np.empty_like(pixels)
colour.pixels.foreach_get(pixels)
ao.pixels.foreach_get(occlusion)
pixels[3::4] = np.clip(occlusion[0::4], 0, 1)
colour.pixels.foreach_set(pixels)
for image, filename in [(colour, 'S02_BakedWall_ColourAO.png'), (normal, 'S02_BakedWall_Normal.png')]:
    image.filepath_raw = str(TEX / filename)
    image.file_format = 'PNG'
    image.save()

exports = []
for variant in range(3):
    map_uv(variant)
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    low.name = 'S02_BakedWall' + ('_UV' + str(variant) if variant else '')
    file = OUT / ('S02_BakedCoursedWall_4x3x0.50' + ('_UV' + str(variant) if variant else '') + '.fbx')
    bpy.ops.export_scene.fbx(filepath=str(file), use_selection=True, object_types={'MESH'},
        axis_forward='Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
        use_tspace=True, path_mode='STRIP', bake_anim=False)
    exports.append(str(file.relative_to(ROOT)))
data.calc_loop_triangles()
assert len(data.loop_triangles) == 12
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == source_hash
report = {'source': str(SOURCE.relative_to(ROOT)), 'sourceSHA256': source_hash,
          'sourceTriangles': 2960, 'runtimeTriangles': 12, 'closedBox': True,
          'boundsMetres': [4, 3, .5], 'atlasSize': SIZE, 'pixelsPerMetre': 232,
          'colour': 'unlit diffuse RGB, ambient occlusion in alpha',
          'normals': 'tangent-space +Y', 'uvVariants': 3, 'atlasRectsPixels': rects,
          'exports': exports}
(ART / 'bake-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('S02_BAKE_PASS', json.dumps(report), flush=True)
