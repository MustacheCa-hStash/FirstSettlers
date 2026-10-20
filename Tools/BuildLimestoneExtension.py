"""Author L04-L09 and bake minimal solids onto a separate shared 2K map pair.

Blender --background --factory-startup --python Tools/BuildLimestoneExtension.py
Editable stones stay in the .blend; runtime exports retain the pier, slabs, footing and real doorway aperture.
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
ART = ROOT / 'ArtReferences/LimestoneBuildKit/Extension'
OUT = ROOT / 'Assets/Models/Buildings/Limestone'
TEX = ROOT / 'Assets/Textures/Buildings/Limestone'
SIZE, BORDER, GUTTER, DENSITY = 2048, 72, 16, 128
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



# Low geometry is also the exact occupied/collision geometry. Input uses Unity XYZ.
configs = [
    ('L04', [((-.25, 0, -.25), (.25, 3, .25))]),
    ('L05', [((0, -.25, 0), (4, 0, 4))]),
    ('L06', [((0, -.5, -.5), (4, 0, .5))]),
    ('L07', [((0, 0, -.25), (4, 3, .25))]),
    ('L08', [((0, 0, -.25), (4, .5, .25))]),
    ('L09', [((0, 0, -.25), (1.375, 2.25, .25)),
             ((2.625, 0, -.25), (4, 2.25, .25)),
             ((0, 2.25, -.25), (4, 3, .25))]),
]
wash = stone_material('Warm limewash', 'E7DEC8', False)
wash_joint = stone_material('Limewashed joints', 'D9CFB8', True)
groups = []
for index, (tag, solids) in enumerate(configs):
    rng = random.Random(2040 + index)
    high = []
    def add(name, lo, hi, mat=None, bevel=.008):
        obj = box(tag + '_' + name, lo, hi, editable, mat or rng.choice(palette), bevel)
        high.append(obj)
        return obj
    if tag == 'L04':
        add('Core', (-.22, .003, -.22), (.22, 2.997, .22), mortar, 0)
        for row in range(8):
            add('PierCourse' + str(row), (-.25, row * .375 + (.004 if row else 0), -.25),
                (.25, (row + 1) * .375 - (.004 if row < 7 else 0), .25), bevel=.008)
    elif tag == 'L05':
        add('Bedding', (.002, -.25, .002), (3.998, -.11, 3.998), mortar, 0)
        for row in range(4):
            edges = [0, 1, 2, 3, 4] if row % 2 == 0 else [0, .5, 1.5, 2.5, 3.5, 4]
            for col, (a, b) in enumerate(zip(edges[:-1], edges[1:])):
                add(f'Slab_{row}_{col}', (a + (.006 if a else 0), -.13, row + (.006 if row else 0)),
                    (b - (.006 if b < 4 else 0), 0, row + 1 - (.006 if row < 3 else 0)), bevel=.006)
    elif tag == 'L06':
        add('Core', (.003, -.497, -.475), (3.997, -.003, .475), mortar, 0)
        for row in range(2):
            for col in range(8):
                add(f'Footing_{row}_{col}', (col * .5 + (.005 if col else 0), -.5 + row * .25 + (.005 if row else 0), -.5),
                    ((col + 1) * .5 - (.005 if col < 7 else 0), -.5 + (row + 1) * .25 - (.005 if row == 0 else 0), .5), bevel=.012)
    elif tag == 'L07':
        add('LimewashedCore', (.002, .002, -.248), (3.998, 2.998, .248), wash_joint, 0)
        for row in range(9):
            count = 7 + row % 2
            widths = [rng.uniform(.45, .7) for _ in range(count)]
            x = 0
            for col, w in enumerate(widths):
                end = x + w * 4 / sum(widths) if col < count - 1 else 4
                obj = add(f'WashedCourse_{row}_{col}', (x + (.002 if col else 0), row / 3 + (.002 if row else 0), -.25),
                    (end - (.002 if col < count - 1 else 0), (row + 1) / 3 - (.002 if row < 8 else 0), .25),
                    wash if rng.random() < .92 else rng.choice(palette), bevel=.002)
                x = end
    elif tag == 'L08':
        add('Core', (.002, .002, -.235), (3.998, .498, .235), mortar, 0)
        for row in range(2):
            edges = [0, 1, 2, 3, 4] if row == 0 else [0, .5, 1.5, 2.5, 3.5, 4]
            for col, (a, b) in enumerate(zip(edges[:-1], edges[1:])):
                add(f'Plinth_{row}_{col}', (a + (.004 if a else 0), row * .25 + (.004 if row else 0), -.25),
                    (b - (.004 if b < 4 else 0), (row + 1) * .25 - (.004 if row == 0 else 0), .25))
    else:
        for side, (a, b) in enumerate([(0, 1.375), (2.625, 4)]):
            add('JambCore' + str(side), (a + .002, .002, -.235), (b - .002, 2.248, .235), mortar, 0)
            for row in range(6):
                add(f'Jamb_{side}_{row}', (a, row * .375 + (.004 if row else 0), -.25),
                    (b, (row + 1) * .375 - (.004 if row < 5 else 0), .25))
        # One actual solid 1.75 m lintel, seated 0.25 m into each jamb.
        add('SingleLintel', (1.125, 2.25, -.25), (2.875, 2.75, .25), bevel=.008)
        add('UpperCore', (.002, 2.252, -.235), (3.998, 2.998, .235), mortar, 0)
        for side, (a, b) in enumerate([(0, 1.125), (2.875, 4)]):
            add('Shoulder' + str(side), (a, 2.25, -.25), (b, 2.75, .25))
        for col in range(4):
            add('HeadCourse' + str(col), (col + (.004 if col else 0), 2.754, -.25),
                (col + 1 - (.004 if col < 3 else 0), 3, .25))
    for obj in high:
        obj.location.x = index * 12
    groups.append({'tag': tag, 'high': high, 'offset': index * 12, 'boxes': solids, 'low': []})

requests = []
for group in groups:
    for part, (lo, hi) in enumerate(group['boxes']):
        x, y, z = [b-a for a, b in zip(lo, hi)]
        for face, width, height in [('front', x, y), ('back', x, y), ('left', z, y),
                                     ('right', z, y), ('top', x, z), ('bottom', x, z)]:
            density = 192 if group['tag'] == 'L05' and face == 'top' else 64 if group['tag'] == 'L05' and face == 'bottom' else DENSITY
            requests.append((math.ceil(width * density), math.ceil(height * density), group['tag'], part, face))
free, rects = [(BORDER, BORDER, SIZE - 2 * BORDER, SIZE - 2 * BORDER)], {}
for width, height, tag, part, face in sorted(requests, key=lambda r: r[0] * r[1], reverse=True):
    w, h = width + 2 * GUTTER, height + 2 * GUTTER
    options = [(min(fw - w, fh - h), max(fw - w, fh - h), x, y)
               for x, y, fw, fh in free if w <= fw and h <= fh]
    assert options, ('Atlas does not fit', tag, part, face)
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
    rects[(tag, part, face)] = (x + GUTTER, y + GUTTER, width, height)

material = bpy.data.materials.new('Limestone shared baked atlas')
material.use_nodes = True
target = material.node_tree.nodes.new('ShaderNodeTexImage')
material.node_tree.nodes.active = target



def map_uv(obj, group, part, variant):
    lo, hi = group['boxes'][part]
    global_lo = [min(a[i] for a,b in group['boxes']) for i in range(3)]
    global_hi = [max(b[i] for a,b in group['boxes']) for i in range(3)]
    source_part = part
    if variant in (1,2):
        for i,(a,b) in enumerate(group['boxes']):
            if abs(a[0] - (global_lo[0]+global_hi[0]-hi[0]))<1e-6 and abs(b[0]-(global_lo[0]+global_hi[0]-lo[0]))<1e-6:
                source_part=i
                break
    a0, b0 = group['boxes'][source_part]
    for face in obj.data.polygons:
        for li in face.loop_indices:
            x,z,y = obj.data.vertices[obj.data.loops[li].vertex_index].co
            nx,nz,ny = face.normal
            if variant in (1,2): x,nx = global_lo[0]+global_hi[0]-x,-nx
            if variant == 2: z,nz = global_lo[2]+global_hi[2]-z,-nz
            if abs(nz)>.5: key,u,v=('front' if nz<0 else 'back'),(x-a0[0])/(b0[0]-a0[0]),(y-a0[1])/(b0[1]-a0[1])
            elif abs(nx)>.5: key,u,v=('left' if nx<0 else 'right'),(z-a0[2])/(b0[2]-a0[2]),(y-a0[1])/(b0[1]-a0[1])
            else: key,u,v=('bottom' if ny<0 else 'top'),(x-a0[0])/(b0[0]-a0[0]),(z-a0[2])/(b0[2]-a0[2])
            a,b,w,h=rects[(group['tag'],source_part,key)]
            obj.data.uv_layers.active.data[li].uv=((a+u*w)/SIZE,(b+v*h)/SIZE)

for group in groups:
    for part,(lo,hi) in enumerate(group['boxes']):
        obj=box(group['tag']+'_Runtime'+str(part),lo,hi,exports,material)
        obj.location.x=group['offset']
        group['low'].append(obj)
        map_uv(obj,group,part,0)
        obj.hide_set(True)
        obj.hide_render=True

bpy.ops.object.select_all(action='DESELECT')
copies=[]
for group in groups:
    for source in group['low']:
        obj=source.copy();obj.data=source.data.copy();exports.objects.link(obj)
        obj.hide_set(False);obj.hide_render=False;obj.select_set(True);copies.append(obj)
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
for image, filename in [(colour, 'LimestoneExtension_ColourAO.png'), (normal, 'LimestoneExtension_Normal.png')]:
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

report={'atlasSize':SIZE,'pixelsPerMetre':DENSITY,'floorTopPixelsPerMetre':192,'gutterPixels':GUTTER,
        'colour':'Unlit colour RGB; AO alpha','normals':'Tangent-space +Y','models':[],
        'atlasIslands':[{'tag':tag,'part':part,'face':face,'rectPixels':rect} for (tag,part,face),rect in rects.items()]}
for group in groups:
    files=[]
    for variant in range(3):
        bpy.ops.object.select_all(action='DESELECT')
        copies=[]
        for part,obj in enumerate(group['low']):
            map_uv(obj,group,part,variant)
            clone=obj.copy();clone.data=obj.data.copy();exports.objects.link(clone)
            clone.location=(0,0,0);clone.hide_render=False;clone.hide_set(False);clone.select_set(True);copies.append(clone)
        bpy.context.view_layer.objects.active=copies[0]
        if len(copies)>1: bpy.ops.object.join()
        clone=bpy.context.object
        clone.data.calc_loop_triangles()
        assert len(clone.data.loop_triangles)==12*len(group['boxes'])
        clone.name=group['tag']+'_Baked'+(f'_UV{variant}' if variant else '')
        filename=clone.name+'.fbx'
        bpy.ops.export_scene.fbx(filepath=str(OUT/filename),use_selection=True,object_types={'MESH'},
            axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
            bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=True,path_mode='STRIP',bake_anim=False)
        files.append(filename);bpy.data.objects.remove(clone,do_unlink=True)
    for part,obj in enumerate(group['low']):
        map_uv(obj,group,part,0)
        obj.location.x=group['offset']
        obj.hide_render=False;obj.hide_set(False)
    for obj in group['high']:
        obj.hide_render=True;obj.hide_set(True)
    report['models'].append({'tag':group['tag'],'solids':group['boxes'],'runtimeTriangles':12*len(group['boxes']),
                             'sourceComponents':len(group['high']),'uvVariants':3,'files':files})

scene.world=bpy.data.worlds.new('Limestone extension preview')
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.25,.27,.29,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
scene.view_settings.view_transform='Standard'
scene.render.resolution_x,scene.render.resolution_y=1600,1000
scene.render.resolution_percentage=100
lamp_data=bpy.data.lights.new('Soft limestone light','AREA');lamp_data.energy=1800;lamp_data.size=9
lamp=bpy.data.objects.new('Soft limestone light',lamp_data);scene.collection.objects.link(lamp)
camera_data=bpy.data.cameras.new('L04-L09 preview');camera=bpy.data.objects.new('L04-L09 preview',camera_data)
scene.collection.objects.link(camera);scene.camera=camera;camera_data.type='ORTHO'
for group in groups:
    for other in groups:
        for obj in other['low']:obj.hide_render=other!=group
    lo=Vector(tuple(min(a[i] for a,b in group['boxes']) for i in range(3)))
    hi=Vector(tuple(max(b[i] for a,b in group['boxes']) for i in range(3)))
    centre=Vector(((lo.x+hi.x)/2+group['offset'],(lo.z+hi.z)/2,(lo.y+hi.y)/2))
    lamp.location=centre+Vector((0,-5,8));lamp.rotation_euler=(centre-lamp.location).to_track_quat('-Z','Y').to_euler()
    for suffix,direction in [('front',Vector((5,-9,5))),('back-and-underside',Vector((-5,9,-4)))]:
        camera.location=centre+direction;camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
        camera_data.ortho_scale=max(hi-lo)*1.45
        scene.render.filepath=str(ART/(group['tag']+'-'+suffix+'.png'))
        bpy.ops.render.render(write_still=True)
# Leave a compact two-row arrangement for interactive authoring.
for i,group in enumerate(groups):
    offset=Vector(((i%3)*6,(i//3)*5,0))
    for obj in group['low']+group['high']:obj.location=offset
    for obj in group['low']:obj.hide_render=False
camera.location=(16,-20,17);camera.rotation_euler=(Vector((7,3,1))-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.ortho_scale=21
lamp.location=(7,-5,12);lamp.rotation_euler=(Vector((7,3,0))-lamp.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ART/'limestone-extension-overview.png')
bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'LimestoneExtension.blend'))
report['sourceSHA256']=hashlib.sha256((ART/'LimestoneExtension.blend').read_bytes()).hexdigest()
(ART/'bake-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LIMESTONE_EXTENSION_BLENDER_PASS',json.dumps(report['models']),flush=True)

