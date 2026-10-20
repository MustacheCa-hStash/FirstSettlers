"""Author L01-L03 and bake all six faces onto shared 2K colour/AO + normal maps.

Blender --background --factory-startup --python Tools/BuildLimestoneKit.py
Editable stones stay in the .blend; runtime exports are three closed 12-triangle walls.
"""
import hashlib
import json
import math
import random
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/LimestoneBuildKit/Models'
OUT = ROOT / 'Assets/Models/Buildings/Limestone'
TEX = ROOT / 'Assets/Textures/Buildings/Limestone'
SIZE, BORDER, GUTTER, DENSITY = 2048, 72, 16, 160
for folder in (ART, OUT, TEX):
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.render.threads_mode = 'FIXED'
scene.render.threads = 12
editable = bpy.data.collections.new('Limestone - editable individual stones')
exports = bpy.data.collections.new('Limestone - runtime solids and UV variants')
scene.collection.children.link(editable)
scene.collection.children.link(exports)


def linear(c):
    return c / 12.92 if c < .04045 else ((c + .055) / 1.055) ** 2.4


def rgba(hexcode):
    return tuple(linear(int(hexcode[i:i + 2], 16) / 255) for i in (0, 2, 4)) + (1,)


def stone_material(name, colour, plain=False):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    surface = nodes.get('Principled BSDF')
    surface.inputs['Roughness'].default_value = .95
    surface.inputs['Specular IOR Level'].default_value = 0
    base = rgba(colour)
    if plain:
        surface.inputs['Base Color'].default_value = base
    else:
        # Sample the approved joint-free F region through shader coordinates, not
        # the labelled guide or its masonry bands. Preserve broad painted colour.
        texture = nodes.new('ShaderNodeTexImage')
        texture.image = bpy.data.images.load(str(ROOT / 'ArtReferences/LimestoneBuildKit/Textures/limestone-timber-trim-source.png'), check_existing=True)
        texture.projection = 'FLAT'
        coords = nodes.new('ShaderNodeTexCoord')
        scale = nodes.new('ShaderNodeVectorMath'); scale.operation = 'MULTIPLY'
        scale.inputs[1].default_value = (.20, .20, .20)
        offset = nodes.new('ShaderNodeVectorMath'); offset.operation = 'ADD'
        offset.inputs[1].default_value = (.02, .02, .02)
        links.new(coords.outputs['UV'], scale.inputs[0])
        links.new(scale.outputs[0], offset.inputs[0])
        links.new(offset.outputs[0], texture.inputs['Vector'])
        tint = nodes.new('ShaderNodeMixRGB'); tint.blend_type = 'MULTIPLY'
        tint.inputs[0].default_value = 1
        reference = rgba('D2C8B0')
        tint.inputs[2].default_value = tuple(base[i] / reference[i] for i in range(3)) + (1,)
        links.new(texture.outputs['Color'], tint.inputs[1])
        links.new(tint.outputs[0], surface.inputs['Base Color'])
    return material


palette = [stone_material('Limestone cream ' + str(i), c) for i, c in enumerate(
    ['D5CBB2', 'CEC3A8', 'DBD1B9', 'C8BEA6', 'D2C8B0', 'D8CDB3'])]
mortar = stone_material('Recessed warm beige joint fill', 'ABA18A', True)


def box(name, lo, hi, collection, material, bevel=0):
    # Input is Unity XYZ; Blender is XZY. Explicit normals avoid reflection winding errors.
    vertices = [(x, z, y) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4),
             (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    mesh.materials.append(material)
    if bevel:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        modifier = obj.modifiers.new('Shallow stone edge', 'BEVEL')
        modifier.width, modifier.segments = bevel, 1
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True) > 0, name
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    layer = obj.data.uv_layers.new(name='UVMap')
    for face in obj.data.polygons:
        axes = sorted(range(3), key=lambda a: abs(face.normal[a]))[:2]
        for li in face.loop_indices:
            co = obj.data.vertices[obj.data.loops[li].vertex_index].co
            layer.data[li].uv = (.08 + co[axes[0]] * .20, .08 + co[axes[1]] * .20)
    obj.data.calc_loop_triangles()
    for triangle in obj.data.loop_triangles:
        a, b, c = [layer.data[li].uv for li in triangle.loops]
        assert abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) > 1e-13, (name, 'Collapsed source UV')
    return obj


groups = []
for index, (tag, width, dressed) in enumerate([('L01', 4, False), ('L02', 4, True), ('L03', 2, True)]):
    rng = random.Random(1700 + index)
    high = [box(tag + '_RecessedCore', (.002, .002, -.215), (width - .002, 2.998, .215), editable, mortar)]
    count = 6 if dressed else 9
    rows = [0] + [(i * 3 / count + (0 if dressed else rng.uniform(-.035, .035))) for i in range(1, count)] + [3]
    for row in range(count):
        if dressed:
            edges = list(range(width + 1)) if row % 2 == 0 else [0] + [i + .5 for i in range(width)] + [width]
        else:
            lengths = [rng.uniform(.43, .73) for _ in range(7 + row % 2)]
            edges = [0]
            for length in lengths:
                edges.append(edges[-1] + length * width / sum(lengths))
            edges[-1] = width
        gap = .006 if dressed else .009
        for col, (x0, x1) in enumerate(zip(edges[:-1], edges[1:])):
            lo = (x0 + (gap if col else 0), rows[row] + (gap if row else 0), -.25 + (0 if dressed else rng.uniform(0, .005)))
            hi = (x1 - (gap if col < len(edges) - 2 else 0), rows[row + 1] - (gap if row < count - 1 else 0), .25 - (0 if dressed else rng.uniform(0, .005)))
            high.append(box(f'{tag}_Course{row:02}_Stone{col:02}', lo, hi, editable,
                            palette[rng.randrange(len(palette))], .008 if dressed else rng.uniform(.013, .023)))
    for obj in high:
        obj.location.x = index * 10
    groups.append({'tag': tag, 'width': width, 'high': high, 'offset': index * 10})

# Independent six-face islands at the same physical scale for both wall spans.
requests = []
for group in groups:
    w = group['width']
    for face, width, height in [('front', w, 3), ('back', w, 3), ('left', .5, 3),
                                ('right', .5, 3), ('top', w, .5), ('bottom', w, .5)]:
        requests.append((round(width * DENSITY), round(height * DENSITY), group['tag'], face))
free, rects = [(BORDER, BORDER, SIZE - 2 * BORDER, SIZE - 2 * BORDER)], {}
for width, height, tag, face in sorted(requests, key=lambda r: r[0] * r[1], reverse=True):
    w, h = width + 2 * GUTTER, height + 2 * GUTTER
    options = [(min(fw - w, fh - h), max(fw - w, fh - h), x, y)
               for x, y, fw, fh in free if w <= fw and h <= fh]
    assert options, ('Atlas does not fit', tag, face)
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
    free = [a for n, a in enumerate(split) if not any(n != m and a[0] >= b[0] and a[1] >= b[1]
            and a[0] + a[2] <= b[0] + b[2] and a[1] + a[3] <= b[1] + b[3]
            and (a != b or n > m) for m, b in enumerate(split))]
    rects[(tag, face)] = (x + GUTTER, y + GUTTER, width, height)

material = bpy.data.materials.new('Limestone shared baked atlas')
material.use_nodes = True
target = material.node_tree.nodes.new('ShaderNodeTexImage')
material.node_tree.nodes.active = target


def map_uv(obj, group, variant):
    width = group['width']
    for face in obj.data.polygons:
        for li in face.loop_indices:
            x, z, y = obj.data.vertices[obj.data.loops[li].vertex_index].co
            nx, nz, ny = face.normal
            if variant in (1, 2): x, nx = width - x, -nx
            if variant == 2: z, nz = -z, -nz
            if abs(nz) > .5: key, u, v = ('front' if nz < 0 else 'back'), x / width, y / 3
            elif abs(nx) > .5: key, u, v = ('left' if nx < 0 else 'right'), (z + .25) / .5, y / 3
            else: key, u, v = ('bottom' if ny < 0 else 'top'), x / width, (z + .25) / .5
            a, b, w, h = rects[(group['tag'], key)]
            obj.data.uv_layers.active.data[li].uv = ((a + u * w) / SIZE, (b + v * h) / SIZE)


for group in groups:
    obj = box(group['tag'] + '_Runtime', (0, 0, -.25), (group['width'], 3, .25), exports, material)
    obj.location.x = group['offset']
    group['low'] = obj
    map_uv(obj, group, 0)
    obj.hide_set(True)
    obj.hide_render = True

bpy.ops.object.select_all(action='DESELECT')
copies = []
for group in groups:
    obj = group['low'].copy()
    obj.data = obj.data.copy()
    exports.objects.link(obj)
    obj.hide_set(False)
    obj.hide_render = False
    obj.select_set(True)
    copies.append(obj)
bpy.context.view_layer.objects.active = copies[0]
bpy.ops.object.join()
low = bpy.context.object
for group in groups:
    for obj in group['high']:
        obj.select_set(True)
bpy.context.view_layer.objects.active = low
scene.render.bake.use_selected_to_active = True
scene.render.bake.cage_extrusion = .07
scene.render.bake.max_ray_distance = .18
scene.render.bake.margin = GUTTER
scene.render.bake.use_pass_direct = False
scene.render.bake.use_pass_indirect = False
scene.render.bake.use_pass_color = True


def bake(kind, space):
    image = bpy.data.images.new('Limestone_' + kind, width=SIZE, height=SIZE, alpha=True)
    image.colorspace_settings.name = space
    image.generated_color = (.5, .5, 1, 1) if kind == 'NORMAL' else (.4, .35, .25, 1)
    target.image = image
    print('LIMESTONE_BAKE_START', kind, flush=True)
    bpy.ops.object.bake(type=kind)
    return image


colour = bake('DIFFUSE', 'sRGB')
normal = bake('NORMAL', 'Non-Color')
ao = bake('AO', 'Non-Color')
pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
occlusion = np.empty_like(pixels)
colour.pixels.foreach_get(pixels)
ao.pixels.foreach_get(occlusion)
pixels[3::4] = np.clip(occlusion[0::4], 0, 1)
colour.pixels.foreach_set(pixels)
for image, filename in [(colour, 'Limestone_ColourAO.png'), (normal, 'Limestone_Normal.png')]:
    image.filepath_raw = str(TEX / filename)
    image.file_format = 'PNG'
    image.save()
bpy.data.objects.remove(low, do_unlink=True)
nodes, links = material.node_tree.nodes, material.node_tree.links
links.new(target.outputs['Color'], nodes.get('Principled BSDF').inputs['Base Color'])
target.image = colour
normal_node = nodes.new('ShaderNodeTexImage')
normal_node.image = normal
normal_map = nodes.new('ShaderNodeNormalMap')
links.new(normal_node.outputs['Color'], normal_map.inputs['Color'])
links.new(normal_map.outputs['Normal'], nodes.get('Principled BSDF').inputs['Normal'])
nodes.get('Principled BSDF').inputs['Roughness'].default_value = .95
nodes.get('Principled BSDF').inputs['Specular IOR Level'].default_value = 0
report = {'atlasSize': SIZE, 'pixelsPerMetre': DENSITY, 'gutterPixels': GUTTER,
          'colour': 'Unlit colour RGB; ambient occlusion alpha', 'normals': 'Tangent-space +Y',
          'models': [], 'atlasIslands': [{'tag': tag, 'face': face, 'rectPixels': rect}
                                        for (tag, face), rect in rects.items()]}
for group in groups:
    files = []
    for variant in range(3):
        obj = group['low']
        map_uv(obj, group, variant)
        obj.data.calc_loop_triangles()
        assert len(obj.data.loop_triangles) == 12
        bpy.ops.object.select_all(action='DESELECT')
        clone = obj.copy()
        clone.data = obj.data.copy()
        exports.objects.link(clone)
        clone.location = (0, 0, 0)
        clone.hide_render = False
        clone.hide_set(False)
        clone.select_set(True)
        bpy.context.view_layer.objects.active = clone
        filename = group['tag'] + '_BakedWall' + (f'_UV{variant}' if variant else '') + '.fbx'
        bpy.ops.export_scene.fbx(filepath=str(OUT / filename), use_selection=True, object_types={'MESH'},
            axis_forward='Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
            use_tspace=True, path_mode='STRIP', bake_anim=False)
        files.append(filename)
        bpy.data.objects.remove(clone, do_unlink=True)
    map_uv(group['low'], group, 0)
    report['models'].append({'tag': group['tag'], 'sizeMetres': [group['width'], 3, .5],
        'sourceStones': len(group['high']) - 1, 'runtimeTriangles': 12, 'uvVariants': 3, 'files': files})

# Save a clean editable scene with both detailed and final bake sources; pack map dependencies.
for group in groups:
    for obj in group['high']:
        obj.hide_render = True
        obj.hide_set(True)
    group['low'].hide_render = False
    group['low'].hide_set(False)
    group['low'].location.x = {'L01': 0, 'L02': 5, 'L03': 10}[group['tag']]
scene.world = bpy.data.worlds.new('Limestone preview world')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.25, .27, .29, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .5
scene.view_settings.view_transform = 'Standard'
scene.render.resolution_x, scene.render.resolution_y = 1600, 900
scene.render.resolution_percentage = 100
lamp_data = bpy.data.lights.new('Broad soft preview light', 'AREA')
lamp_data.energy, lamp_data.size = 1800, 9
lamp = bpy.data.objects.new('Broad soft preview light', lamp_data)
scene.collection.objects.link(lamp)
lamp.location = (5, -5, 9)
lamp.rotation_euler = (Vector((5, 0, 1.5)) - lamp.location).to_track_quat('-Z', 'Y').to_euler()
camera_data = bpy.data.cameras.new('Limestone catalog preview')
camera = bpy.data.objects.new('Limestone catalog preview', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 14
camera.location = (14, -19, 10)
camera.rotation_euler = (Vector((6, 0, 1.5)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = str(ART / 'limestone-kit-overview.png')
bpy.ops.render.render(write_still=True)
for group in groups:
    for other in groups:
        other['low'].hide_render = other != group
    centre = group['low'].location + Vector((group['width'] / 2, 0, 1.5))
    for suffix, direction in [('front', Vector((5, -9, 4))), ('back-and-underside', Vector((-5, 9, -3)))]:
        camera.location = centre + direction
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.ortho_scale = max(group['width'], 3) * 1.35
        scene.render.filepath = str(ART / (group['tag'] + '-' + suffix + '.png'))
        bpy.ops.render.render(write_still=True)
for group in groups:
    group['low'].hide_render = False
    for obj in group['high']:
        obj.location.x = group['low'].location.x
camera.location = (14, -19, 10)
camera.rotation_euler = (Vector((6, 0, 1.5)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.ortho_scale = 14
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ART / 'LimestoneBuildKit.blend'))
report['sourceSHA256'] = hashlib.sha256((ART / 'LimestoneBuildKit.blend').read_bytes()).hexdigest()
(ART / 'bake-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LIMESTONE_BLENDER_PASS', json.dumps(report['models']), flush=True)
