"""Blender: author S02/S03/S04/S06, complete UV0, three stable UV layouts, FBXs and previews.

Run Blender --background --factory-startup --python Tools/BuildStoneKit.py.
The editable source is independent of the existing wooden source. No scene or wood asset is edited.
"""
import bpy
import bmesh
import hashlib
import json
import math
import random
import shutil
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/StoneBuildKit/Models'
OUT = ROOT / 'Assets/Models/Buildings/Stone'
TEX = ROOT / 'Assets/Textures/Buildings/Stone'
for folder in (ART, OUT, TEX):
    folder.mkdir(parents=True, exist_ok=True)
source_texture = ROOT / 'Assets/Textures/Rock/Presets/PaintedStone_Albedo.png'
texture = TEX / 'PaintedStone_Albedo.png'
shutil.copyfile(source_texture, texture)  # Exact approved albedo; no labels, seams or new image synthesis.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
editable = bpy.data.collections.new('Stone kit — editable individual stones')
exports = bpy.data.collections.new('Stone kit — exports and UV variants')
bpy.context.scene.collection.children.link(editable)
bpy.context.scene.collection.children.link(exports)
image = bpy.data.images.load(str(texture), check_existing=True)
image.pack()
material = bpy.data.materials.new('Ordinary stone — painted matte UV0')
material.use_nodes = True
nodes = material.node_tree.nodes
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = .95
bsdf.inputs['Specular IOR Level'].default_value = .08
tex = nodes.new('ShaderNodeTexImage')
tex.image = image
uv_node = nodes.new('ShaderNodeUVMap')
uv_node.uv_map = 'UVMap'
material.node_tree.links.new(uv_node.outputs['UV'], tex.inputs['Vector'])
material.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])


def uv_map(obj, seed, variant=0):
    """Per-face planar projection, constant physical scale within each face; all bevel/hidden faces too."""
    mesh = obj.data
    uv = mesh.uv_layers.active or mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        rng = random.Random(seed * 10007 + face.index * 193 + variant * 104729)
        dominant = max(range(3), key=lambda a: abs(face.normal[a]))
        a, b = [axis for axis in range(3) if axis != dominant]
        if rng.random() > .5:
            a, b = b, a
        points = [mesh.vertices[i].co for i in face.vertices]
        lo = [min(p[axis] for p in points) for axis in (a, b)]
        hi = [max(p[axis] for p in points) for axis in (a, b)]
        # No stretch: same UV units/metre on both axes. Vary crop, orientation and physical scale.
        scale = min(.24 + rng.random() * .12, .82 / max(hi[0] - lo[0], hi[1] - lo[1], .001))
        width, height = [(hi[i] - lo[i]) * scale for i in (0, 1)]
        offset = [.035 + rng.random() * (.93 - span) for span in (width, height)]
        flip = [rng.random() > .5, rng.random() > .5]
        for li in face.loop_indices:
            p = mesh.vertices[mesh.loops[li].vertex_index].co
            values = [p[axis] - lo[i] for i, axis in enumerate((a, b))]
            uv.data[li].uv = [offset[i] + ((hi[i] - lo[i] - values[i]) if flip[i] else values[i]) * scale for i in (0, 1)]
    obj['uv_seed'] = seed
    obj['uv_layout'] = variant


def mesh_object(name, vertices, faces, seed, bevel=.012):
    mesh = bpy.data.meshes.new(name)
    # Author in Unity XYZ, then change to Blender XZY. Recalculate winding after reflection.
    mesh.from_pydata([(x, z, y) for x, y, z in vertices], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    editable.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if bevel:
        mod = obj.modifiers.new('Broad hand-worked edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    obj.data.materials.clear()
    obj.data.materials.append(material)
    uv_map(obj, seed)
    obj.select_set(False)
    return obj


def box(name, lo, hi, seed, bevel=.012):
    v = [(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    return mesh_object(name, v, [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)], seed, bevel)


def wall():
    parts = [box('S02_RecessedPacking', (.006, .006, -.225), (3.994, 2.994, .225), 20000, 0)]
    rng = random.Random(202)
    rows = [0] + [i / 3 + rng.uniform(-.025, .025) for i in range(1, 9)] + [3]
    for row in range(9):
        widths = [rng.uniform(.46, .72) for _ in range(7 if row % 2 == 0 else 8)]
        total = sum(widths)
        edges = [0]
        for value in widths:
            edges.append(edges[-1] + value * 4 / total)
        edges[-1] = 4
        for col in range(len(widths)):
            x0, x1 = edges[col], edges[col + 1]
            y0, y1 = rows[row], rows[row + 1]
            # Small mortar gaps, broad planar faces and shallow bevels; no procedural rocky micro-noise.
            lo = (x0 + (.005 if col else 0), y0 + (.004 if row else 0), -.25 + rng.uniform(0, .008))
            hi = (x1 - (.005 if col < len(widths) - 1 else 0), y1 - (.004 if row < 8 else 0), .25 - rng.uniform(0, .008))
            if col == 0:
                lo = (lo[0], lo[1], -.25)
                hi = (hi[0], hi[1], .25)
            parts.append(box(f'S02_Course{row:02}_Stone{col:02}', lo, hi, 21000 + row * 31 + col, rng.uniform(.009, .018)))
    return parts


def pier():
    parts = [box('S03_RecessedPacking', (-.225, .006, -.225), (.225, 2.994, .225), 30000, 0)]
    for row in range(8):
        y0, y1 = row * .375 + (.004 if row else 0), (row + 1) * .375 - (.004 if row < 7 else 0)
        for half in range(2):
            lo, hi = [-.25, y0, -.25], [.25, y1, .25]
            axis = 0 if row % 2 == 0 else 2
            lo[axis], hi[axis] = (-.25, -.004) if half == 0 else (.004, .25)
            parts.append(box(f'S03_Course{row:02}_Stone{half}', lo, hi, 31000 + row * 17 + half, .013))
    return parts


def clip(poly, a, b, c):
    result = []
    for p, q in zip(poly, poly[1:] + poly[:1]):
        dp, dq = a * p[0] + b * p[1] - c, a * q[0] + b * q[1] - c
        if dp <= 1e-9:
            result.append(p)
        if (dp <= 0) != (dq <= 0):
            t = dp / (dp - dq)
            result.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return result


def floor():
    parts = [box('S04_PackedBedding', (.006, -.25, .006), (3.994, -.195, 3.994), 40000, 0)]
    rng = random.Random(404)
    seeds = [((x + .5 + rng.uniform(-.28, .28)) * 4 / 6, (z + .5 + rng.uniform(-.28, .28)) * 4 / 6) for z in range(6) for x in range(6)]
    for i, p in enumerate(seeds):
        poly = [(0, 0), (4, 0), (4, 4), (0, 4)]
        for j, q in enumerate(seeds):
            if i != j:
                poly = clip(poly, q[0] - p[0], q[1] - p[1], (q[0] ** 2 + q[1] ** 2 - p[0] ** 2 - p[1] ** 2) / 2)
        centre = [sum(v[a] for v in poly) / len(poly) for a in (0, 1)]
        poly = [tuple(v[a] if abs(v[a]) < 1e-7 or abs(v[a] - 4) < 1e-7 else centre[a] + (v[a] - centre[a]) * .985 for a in (0, 1)) for v in poly]
        n = len(poly)
        vertices = [(x, y, z) for y in (-.20, 0) for x, z in poly]
        faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))] + [(k, (k + 1) % n, (k + 1) % n + n, k + n) for k in range(n)]
        parts.append(mesh_object(f'S04_Flagstone{i:02}', vertices, faces, 41000 + i * 19, .008))
    return parts


def cap():
    parts = []
    widths = [.58, .69, .63, .72, .66, .72]
    x = 0
    for i, width in enumerate(widths):
        x1 = x + width
        parts.append(box(f'S06_Capstone{i:02}', (x + (.004 if i else 0), 0, -.25), (x1 - (.004 if i < 5 else 0), .25, .25), 61000 + i * 29, .013))
        x = x1
    return parts


def inspect(objects):
    report = {'components': len(objects), 'triangles': 0, 'faces': 0, 'uvTrianglesChecked': 0, 'closedComponents': True, 'allFaceUVsValid': True}
    crops = set()
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        assert all(e.is_manifold for e in bm.edges), obj.name
        assert bm.calc_volume(signed=True) > 0, obj.name
        bm.free()
        report['faces'] += len(mesh.polygons)
        report['triangles'] += len(mesh.loop_triangles)
        uv = mesh.uv_layers.active
        assert uv is not None and len(uv.data) == len(mesh.loops), obj.name
        for tri in mesh.loop_triangles:
            a, b, c = [uv.data[index].uv for index in tri.loops]
            area = abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x))
            assert area > 1e-12, (obj.name, tri.index, 'collapsed UV')
            for p in (a, b, c):
                assert .034 <= p.x <= .966 and .034 <= p.y <= .966, (obj.name, list(p))
            report['uvTrianglesChecked'] += 1
        crops.add(tuple(round(v, 5) for li in mesh.polygons[0].loop_indices for v in uv.data[li].uv))
    report['distinctComponentCrops'] = len(crops)
    assert len(crops) == len(objects), 'Repeated component UV crops'
    return report


def export(parts, tag, filename, offset):
    base = inspect(parts)
    base['tag'], base['files'] = tag, []
    for variant in range(3):
        bpy.ops.object.select_all(action='DESELECT')
        copies = []
        for src in parts:
            obj = src.copy()
            obj.data = src.data.copy()
            exports.objects.link(obj)
            uv_map(obj, int(src['uv_seed']), variant)
            obj.select_set(True)
            copies.append(obj)
        inspect(copies)
        bpy.context.view_layer.objects.active = copies[0]
        bpy.ops.object.join()
        joined = bpy.context.object
        joined.name = tag + '_Export' + (f'_UV{variant}' if variant else '')
        file = filename + (f'_UV{variant}' if variant else '') + '.fbx'
        bpy.ops.export_scene.fbx(filepath=str(OUT / file), use_selection=True, object_types={'MESH'}, axis_forward='Z', axis_up='Y', apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False, use_tspace=False, path_mode='STRIP', bake_anim=False)
        base['files'].append(file)
        joined.hide_set(True)
        joined.hide_render = True
    for obj in parts:
        obj.location = offset
    return base


models = []
groups = []
for tag, maker, file, offset in [('S02', wall, 'S02_CoursedRubbleWall_4x3x0.50', (0, 0, 0)),
    ('S03', pier, 'S03_StonePier_0.50x3x0.50', (5, 0, 0)),
    ('S04', floor, 'S04_FlagstoneFloor_4x0.25x4', (7, 0, .25)),
    ('S06', cap, 'S06_WallCap_4x0.25x0.50', (0, -2, 0))]:
    objects = maker()
    models.append(export(objects, tag, file, offset))
    groups.append((tag, objects))
exports.hide_render = True
bpy.ops.object.select_all(action='DESELECT')
for obj in groups[0][1]:
    obj.select_set(True)
bpy.context.view_layer.objects.active = groups[0][1][0]
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.shading.type = 'MATERIAL'
            area.spaces.active.region_3d.view_location = Vector((5, 0, 1.25))
            area.spaces.active.region_3d.view_distance = 15
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.render.resolution_x = 1600
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.world.color = (.32, .32, .32)
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'Medium High Contrast' if 'Medium High Contrast' in [i.name for i in scene.view_settings.bl_rna.properties['look'].enum_items] else 'None'
camera_data = bpy.data.cameras.new('Stone preview camera')
camera = bpy.data.objects.new('Stone preview camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
lamp_data = bpy.data.lights.new('Broad preview light', 'AREA')
lamp_data.energy, lamp_data.shape, lamp_data.size = 2200, 'DISK', 10
lamp = bpy.data.objects.new('Broad preview light', lamp_data)
scene.collection.objects.link(lamp)
lamp.location = (0, -5, 9)
lamp.rotation_euler = (Vector((4, 0, 1)) - lamp.location).to_track_quat('-Z', 'Y').to_euler()
camera.location = (13, -17, 12)
camera.rotation_euler = (Vector((5, 0, 1.2)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.ortho_scale = 14.5
scene.render.filepath = str(ART / 'stone-kit-overview.png')
bpy.ops.render.render(write_still=True)
for tag, objects in groups:
    for _, other in groups:
        for obj in other:
            obj.hide_render = obj not in objects
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    lo = Vector(tuple(min(p[a] for p in points) for a in range(3)))
    hi = Vector(tuple(max(p[a] for p in points) for a in range(3)))
    centre = (lo + hi) / 2
    for suffix, direction in [('front', Vector((6, -9, 6))), ('back-underside', Vector((-6, 9, -5)))]:
        camera.location = centre + direction
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.ortho_scale = max(hi - lo) * 1.65
        scene.render.filepath = str(ART / f'{tag}-{suffix}.png')
        bpy.ops.render.render(write_still=True)
for _, objects in groups:
    for obj in objects:
        obj.hide_render = False
camera.location = (13, -17, 12)
camera.rotation_euler = (Vector((5, 0, 1.2)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.ortho_scale = 14.5
manifest = {'models': models, 'texture': str(texture.relative_to(ROOT)), 'textureSHA256': hashlib.sha256(texture.read_bytes()).hexdigest(),
    'source': 'ArtReferences/StoneBuildKit/Models/StoneBuildKit.blend', 'uvMethod': 'Per-face planar projection; equal scale on both projected axes, seeded offsets/rotation/mirroring; three layout variants',
    'allFacesIncludingHiddenAndBevels': True}
(ART / 'blender-models.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ART / 'StoneBuildKit.blend'))
print('STONE KIT BLENDER PASS', json.dumps(manifest))
