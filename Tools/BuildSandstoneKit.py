"""SS01-SS11: editable sandstone, one baked atlas, minimal closed runtime shells.

Blender --background --factory-startup --python-exit-code 1 --python Tools/BuildSandstoneKit.py
Unity XYZ inputs; Blender XZY. No existing source assets are modified.
"""
import bpy, bmesh, math, json, random, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/SandstoneBuildKit/Models'
OUT = ROOT / 'Assets/Models/Buildings/Sandstone'
TEX = ROOT / 'Assets/Textures/Buildings/Sandstone'
for folder in (ART, OUT, TEX):
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.render.engine = 'CYCLES'
scene.cycles.samples = 12
scene.render.threads_mode = 'FIXED'
scene.render.threads = 12
scene.view_settings.view_transform = 'Standard'
highs = bpy.data.collections.new('Editable sandstone blocks and slabs')
lows = bpy.data.collections.new('Minimal runtime meshes')
scene.collection.children.link(highs)
scene.collection.children.link(lows)

def linear(c):
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4

def rgb(code):
    return tuple(linear(int(code[i:i+2], 16) / 255) for i in (0, 2, 4))

def material(name, colour, texture=True):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n, l = m.node_tree.nodes, m.node_tree.links
    s = n.get('Principled BSDF')
    s.inputs['Base Color'].default_value = (*rgb(colour), 1)
    s.inputs['Roughness'].default_value = 1
    s.inputs['Specular IOR Level'].default_value = 0
    if texture:
        coord = n.new('ShaderNodeTexCoord')
        noise = n.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = 3.5
        noise.inputs['Detail'].default_value = 2
        l.new(coord.outputs['Object'], noise.inputs['Vector'])
        ramp = n.new('ShaderNodeValToRGB')
        base = rgb(colour)
        ramp.color_ramp.elements[0].color = tuple(c * .86 for c in base) + (1,)
        ramp.color_ramp.elements[1].color = tuple(min(1, c * 1.10) for c in base) + (1,)
        l.new(noise.outputs['Fac'], ramp.inputs[0])
        l.new(ramp.outputs['Color'], s.inputs['Base Color'])
        # Gentle sedimentary grain, not painted directional illumination.
        mapping = n.new('ShaderNodeVectorMath'); mapping.operation = 'MULTIPLY'
        mapping.inputs[1].default_value = (2, 2, 22)
        l.new(coord.outputs['Object'], mapping.inputs[0])
        grain = n.new('ShaderNodeTexNoise'); grain.inputs['Scale'].default_value = 5
        grain.inputs['Detail'].default_value = 2
        l.new(mapping.outputs[0], grain.inputs['Vector'])
        bump = n.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .22
        bump.inputs['Distance'].default_value = .008
        l.new(grain.outputs['Fac'], bump.inputs['Height'])
        l.new(bump.outputs['Normal'], s.inputs['Normal'])
    return m

palette = [material('Apricot sandstone ' + c, c) for c in
           ['C88750', 'CE9059', 'D39962', 'BF7E4C', 'DBA46D', 'C58A55']]
joint = material('Warm recessed sandstone bedding', '997452', False)
runtime = material('Sandstone baked atlas', 'FFFFFF', False)
target = runtime.node_tree.nodes.new('ShaderNodeTexImage')
runtime.node_tree.nodes.active = target
groups = []
current = None
rng = random.Random(30110)

def make(name, vertices, faces, collection, mat, bevel=0):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(x, z, y) for x, y, z in vertices], [], faces)
    ob = bpy.data.objects.new(name, mesh)
    collection.objects.link(ob)
    mesh.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    if bevel:
        mod = ob.modifiers.new('Shallow cut edges', 'BEVEL')
        mod.width = bevel; mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges), name
    assert bm.calc_volume(signed=True) > 1e-8, name
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    ob.location.x = current['offset']
    return ob

def box_data(lo, hi):
    v = [(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    return v, [(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]

def prism_data(poly, z0=-.25, z1=.25):
    n = len(poly)
    return ([(x,y,z) for z in (z0,z1) for x,y in poly],
            [tuple(range(n-1,-1,-1)),tuple(range(n,n*2))] +
            [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)])

def rect(a,b,c,d):
    return [(a,b),(c,b),(c,d),(a,d)]

def add_low(vertices, faces):
    ob = make(current['tag'], vertices, faces, lows, runtime)
    current['low'] = ob
    return ob

def hull(vertices, faces):
    # Collision is separately decomposed into convex solids; render seams omitted.
    ob = make('Collision temporary', vertices, faces, lows, joint)
    ob.data.calc_loop_triangles()
    current['solids'].append(dict(vertices=[dict(x=v.co.x,y=v.co.z,z=v.co.y) for v in ob.data.vertices],
                                  triangles=[i for t in ob.data.loop_triangles for i in t.vertices]))
    bpy.data.objects.remove(ob, do_unlink=True)

def high_box(name, lo, hi, mat=None, bevel=.009):
    ob = make(current['tag']+'_'+name, *box_data(lo,hi), highs, mat or rng.choice(palette), bevel)
    current['high'].append(ob)
    return ob

def start(tag, slug, name, kind, size, minimum, description, slope=''):
    global current
    current = dict(tag=tag,slug=slug,name=name,kind=kind,size=size,minimum=minimum,
                   description=description,slope=slope,offset=len(groups)*7,high=[],solids=[])
    groups.append(current)

def core():
    ob = current['low'].copy(); ob.data = ob.data.copy()
    highs.objects.link(ob); ob.name = current['tag']+'_RecessedBedding'
    ob.data.materials.clear(); ob.data.materials.append(joint)
    # Inset along averaged normals to let stone gaps bake below face level.
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.normal_update()
    for v in bm.verts:
        direction = sum((f.normal for f in v.link_faces), Vector()).normalized()
        v.co -= direction * .025
    bm.to_mesh(ob.data); bm.free()
    current['high'].append(ob)

def cut_high(ob, vertices, faces, operation='DIFFERENCE'):
    cutter = make('Temporary cutting volume', vertices, faces, highs, joint)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    mod = ob.modifiers.new('Actual aperture cut', 'BOOLEAN')
    mod.operation = operation; mod.solver = 'EXACT'; mod.object = cutter
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

def masonry(width, height, depth=.5, dressed=False, blocks=None, cutter=None, triangle=None):
    core()
    if blocks is None:
        blocks=[]; step=.5 if dressed else .375
        rows=round(height/step)
        edges_y=[height*i/rows for i in range(rows+1)]
        for row,(bottom,top) in enumerate(zip(edges_y,edges_y[1:])):
            step_x=1 if dressed else .72
            edges=sorted(set([0,width]+[max(0,min(width,i*step_x+(step_x*.5 if row%2 else 0)))
                          for i in range(-1,math.ceil(width/step_x)+2)]))
            blocks.extend((a,bottom,b,top) for a,b in zip(edges,edges[1:]) if b-a>.03)
    for index,(a,b,c,d) in enumerate(blocks):
        gap=.007 if dressed else .009
        lo=(a+(gap if a>0 else 0),b+(gap if b>0 else 0),-depth/2)
        hi=(c-(gap if c<width else 0),d-(gap if d<height else 0),depth/2)
        ob=high_box('Stone_%03d'%index,lo,hi,bevel=.008 if dressed else .014)
        if cutter:
            cut_high(ob,*cutter)
        if triangle:
            cut_high(ob,*prism_data(triangle,-depth,depth),operation='INTERSECT')
        if not ob.data.polygons:
            current['high'].remove(ob); bpy.data.objects.remove(ob,do_unlink=True)

descriptions = [
 'Warm orange sandstone in rough bedded courses. A 4 m span, 3 m tall and 0.5 m thick.',
 'Dressed apricot sandstone blocks with restrained tooling. A 4 m span and 3 m storey.',
 'A 2 m sandstone wall with full-size dressed blocks. Matches the 3 m storey.',
 'A plain square sandstone pier, 0.5 m wide and 3 m tall.',
 'Broad cleft sandstone paving. A 4 m square panel, 0.25 m deep below its top.',
 'A 4 by 1 m sandstone footing strip, 0.5 m deep below its top datum.',
 'Sandstone doorway with a single lintel and a clear 1.25 by 2.25 m opening. No door leaf.',
 'An unglazed window: 0.5 by 1 m outside, splayed to 1 m wide inside. Sill at 1.25 m.',
 'An interior storage recess, 1 by 0.75 m and 0.25 m deep, with a solid 0.25 m backing.',
 'Two broad upright sandstone slabs form a 2 m wide, 1.5 m high indoor divider.',
 'A triangular sandstone half-gable with 2 m run and rise. Flip with R for the other slope.'
]
slugs=['ss01-bedded-sandstone','ss02-dressed-sandstone','ss03-narrow-sandstone',
       'ss04-sandstone-pier','ss05-sandstone-paving','ss06-sandstone-footing',
       'ss07-sandstone-doorway','ss08-splayed-sandstone-window','ss09-sandstone-niche',
       'ss10-slab-partition','ss11-sandstone-gable-rising']
names=['SS01 bedded sandstone wall','SS02 dressed sandstone wall','SS03 narrow sandstone wall',
       'SS04 sandstone pier','SS05 sandstone paving','SS06 sandstone strip footing',
       'SS07 sandstone lintel doorway','SS08 splayed sandstone window','SS09 sandstone storage niche',
       'SS10 upright slab partition','SS11 sandstone half-gable']

for index in range(11):
    size=([2,3,.5] if index==2 else [.5,3,.5] if index==3 else [4,.25,4] if index==4 else
          [4,.5,1] if index==5 else [2,1.5,.25] if index==9 else [2,2,.5] if index==10 else [4,3,.5])
    minimum=([-.25,0,-.25] if index==3 else [0,-.25,0] if index==4 else
             [0,-.5,-.5] if index==5 else [0,0,-size[2]/2])
    kind=3 if index==3 else 1 if index==4 else 2 if index==5 else 0
    start('SS%02d'%(index+1),slugs[index],names[index],kind,size,minimum,descriptions[index],
          'rising' if index==10 else '')
    w,h,d=size
    if index in (0,1,2,3,4,5,9):
        data=box_data(minimum,[minimum[a]+size[a] for a in range(3)])
        add_low(*data); hull(*data)
        if index<3:
            masonry(w,h,d,index!=0)
        elif index==3:
            core()
            for row in range(8):
                high_box('PierCourse%d'%row,(-.25,row*.375+(.006 if row else 0),-.25),
                         (.25,(row+1)*.375-(.006 if row<7 else 0),.25))
        elif index==4:
            core()
            for row in range(5):
                edges=sorted(set([0,4]+[max(0,min(4,i*.8+(.4 if row%2 else 0))) for i in range(7)]))
                for col,(a,b) in enumerate(zip(edges,edges[1:])):
                    if b-a>.02: high_box('Flag%d_%d'%(row,col),(a+.006,-.25,row*.8+.006),(b-.006,0,(row+1)*.8-.006))
        elif index==5:
            core()
            for row in range(2):
                for col in range(6):
                    a=col*4/6; b=(col+1)*4/6
                    high_box('Footing%d_%d'%(row,col),(a+.006,-.5+row*.25+.005,-.5),(b-.006,-.5+(row+1)*.25-.005,.5))
        else:
            core()
            for slab in range(2):
                high_box('UprightSlab%d'%slab,(slab+.003,0,-.125),(slab+.997,1.5,.125),bevel=.012)
    elif index==6:
        poly=[(0,0),(1.375,0),(1.375,2.25),(2.625,2.25),(2.625,0),(4,0),(4,3),(0,3)]
        add_low(*prism_data(poly))
        for lo,hi in [((0,0,-.25),(1.375,2.25,.25)),((2.625,0,-.25),(4,2.25,.25)),((0,2.25,-.25),(4,3,.25))]:hull(*box_data(lo,hi))
        blocks=[]
        for row in range(6):
            bottom=row*.375; top=bottom+.375
            for a,c in [(0,.6875),(.6875,1.375),(2.625,3.3125),(3.3125,4)]:blocks.append((a,bottom,c,top))
        blocks.extend([(0,2.25,1.125,2.75),(1.125,2.25,2.875,2.75),(2.875,2.25,4,2.75)])
        blocks.extend([(i,2.75,i+1,3) for i in range(4)])
        masonry(4,3,blocks=blocks,dressed=True)
        current['opening']=[1.375,0,2.625,2.25]
    elif index==7:
        # Four annular strips join different inside/outside aperture widths.
        outer=rect(0,0,4,3); front=rect(1.75,1.25,2.25,2.25); back=rect(1.5,1.25,2.5,2.25)
        verts=[(x,y,z) for z,loop in [(-.25,outer),(.25,outer),(-.25,front),(.25,back)] for x,y in loop]
        faces=[]
        for i in range(4):
            j=(i+1)%4
            faces.extend([(i,j,j+8,i+8),(i+4,i+12,j+12,j+4),(i,i+4,j+4,j),(i+8,j+8,j+12,i+12)])
        add_low(verts,faces)
        hull(*box_data((0,0,-.25),(4,1.25,.25)))
        hull(*box_data((0,2.25,-.25),(4,3,.25)))
        for left in (True,False):
            hv=[(x,y,z) for z in (-.25,.25) for y in (1.25,2.25)
                for x in ((0,1.75-.5*(z+.25)) if left else (2.25+.5*(z+.25),4))]
            hull(hv,box_data((0,0,0),(1,1,1))[1])
        # x = 1.75/2.25 at exterior -0.25, x = 1.50/2.50 at interior +0.25.
        cutter=([(1.75-.5*(z+.25),y,z) if side==0 else (2.25+.5*(z+.25),y,z)
                 for z in (-1,1) for y in (1.25,2.25) for side in (0,1)],box_data((0,0,0),(1,1,1))[1])
        blocks=[(i,yy,i+1,yy+.375) for yy in (0,.375,.75) for i in range(4)]
        blocks.extend([(0,1.125,4,1.25)])
        blocks.extend([(a,y,c,y+.5) for y in (1.25,1.75) for a,c in [(0,1),(1,1.75),(2.25,3),(3,4)]])
        blocks.extend([(0,2.25,1.25,2.5),(1.25,2.25,2.75,2.5),(2.75,2.25,4,2.5)])
        blocks.extend([(i,2.5,i+1,3) for i in range(4)])
        # Jamb solids extend across the widest inner aperture before cutting.
        blocks=[(a,b,2 if c==1.75 else c,d0) if a<2 else (2 if a==2.25 else a,b,c,d0) for a,b,c,d0 in blocks]
        masonry(4,3,dressed=True,blocks=blocks,cutter=cutter)
        current['opening']=[1.5,1.25,2.5,2.25]
    elif index==8:
        outer=rect(0,0,4,3); inner=rect(1.5,1,2.5,1.75)
        verts=[(x,y,z) for z,loop in [(-.25,outer),(.25,outer),(.25,inner),(0,inner)] for x,y in loop]
        faces=[(0,1,2,3),(12,13,14,15)]
        for i in range(4):
            j=(i+1)%4
            faces.extend([(i,i+4,j+4,j),(i+4,i+8,j+8,j+4),(i+8,i+12,j+12,j+8)])
        add_low(verts,faces)
        for lo,hi in [((0,0,-.25),(4,3,0)),((0,0,0),(1.5,3,.25)),((2.5,0,0),(4,3,.25)),
                      ((1.5,0,0),(2.5,1,.25)),((1.5,1.75,0),(2.5,3,.25))]:hull(*box_data(lo,hi))
        cutter=box_data((1.5,1,0),(2.5,1.75,1))
        blocks=[]
        for bottom,top in [(0,.5),(.5,1),(1,1.375),(1.375,1.75),(2,2.5),(2.5,3)]:
            for i in range(4):blocks.append((i,bottom,i+1,top))
        blocks.extend([(0,1.75,1.25,2),(1.25,1.75,2.75,2),(2.75,1.75,4,2)])
        masonry(4,3,dressed=True,blocks=blocks,cutter=cutter)
    else:
        poly=[(0,0),(2,0),(2,2)]
        add_low(*prism_data(poly));hull(*prism_data(poly));masonry(2,2,triangle=poly)

# Falling gable shares the same UV content via a full X reflection.
source=groups[-1]
start('SS11F','ss11-sandstone-gable-falling',names[10],0,[2,2,.5],[0,0,-.25],descriptions[10],'falling')
poly=[(0,0),(2,0),(0,2)]
add_low(*prism_data(poly));hull(*prism_data(poly));masonry(2,2,triangle=poly)

# Atlas: planar islands with physical area weighting, shared across every piece.
bpy.ops.object.select_all(action='DESELECT')
for g in groups:g['low'].select_set(True)
bpy.context.view_layer.objects.active=groups[0]['low']
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.006,area_weight=0,correct_aspect=True,scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True,scale=True,margin=.007)
bpy.ops.object.mode_set(mode='OBJECT')

def combine(objects,collection):
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for ob in objects:
        clone=ob.copy();clone.data=ob.data.copy();collection.objects.link(clone)
        clone.select_set(True);copies.append(clone);ob.hide_render=True
    bpy.context.view_layer.objects.active=copies[0]
    bpy.ops.object.join()
    return bpy.context.object

bake_object=combine([g['low'] for g in groups],lows)
source_object=combine([o for g in groups for o in g['high']],highs)
bake_object.select_set(True);bpy.context.view_layer.objects.active=bake_object
scene.render.bake.use_selected_to_active=True
scene.render.bake.cage_extrusion=.04
scene.render.bake.max_ray_distance=.16
scene.render.bake.margin=12
scene.render.bake.use_pass_direct=False
scene.render.bake.use_pass_indirect=False
scene.render.bake.use_pass_color=True
SIZE=2048

def bake(kind,space):
    image=bpy.data.images.new('Sandstone '+kind,width=SIZE,height=SIZE,alpha=True)
    image.colorspace_settings.name=space;target.image=image
    print('SANDSTONE BAKE',kind,flush=True)
    bpy.ops.object.bake(type=kind)
    return image

colour=bake('DIFFUSE','sRGB');normal=bake('NORMAL','Non-Color');ao=bake('AO','Non-Color')
p=np.empty(SIZE*SIZE*4,dtype=np.float32);q=np.empty_like(p)
colour.pixels.foreach_get(p);ao.pixels.foreach_get(q);p[3::4]=np.clip(q[::4],0,1);colour.pixels.foreach_set(p)
for im,name in [(colour,'Sandstone_ColourAO.png'),(normal,'Sandstone_Normal.png')]:
    im.filepath_raw=str(TEX/name);im.file_format='PNG';im.save()
bpy.data.objects.remove(bake_object,do_unlink=True);bpy.data.objects.remove(source_object,do_unlink=True)
colour=bpy.data.images.load(str(TEX/'Sandstone_ColourAO.png'),check_existing=False);colour.alpha_mode='CHANNEL_PACKED'
normal=bpy.data.images.load(str(TEX/'Sandstone_Normal.png'),check_existing=False);normal.colorspace_settings.name='Non-Color'
target.image=colour
n,l=runtime.node_tree.nodes,runtime.node_tree.links
l.new(target.outputs['Color'],n.get('Principled BSDF').inputs['Base Color'])
nn=n.new('ShaderNodeTexImage');nn.image=normal;nm=n.new('ShaderNodeNormalMap')
l.new(nn.outputs['Color'],nm.inputs['Color']);l.new(nm.outputs['Normal'],n.get('Principled BSDF').inputs['Normal'])

manifest=[]
for i,g in enumerate(groups):
    info={k:v for k,v in g.items() if k not in ('high','low','offset')}
    ob=g['low'];ob.hide_render=False
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    ob.data.calc_loop_triangles();info['triangles']=len(ob.data.loop_triangles)
    for t in ob.data.loop_triangles:
        uv=ob.data.uv_layers.active.data
        a,b,c=[uv[li].uv for li in t.loops]
        assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(g['tag'],'Collapsed UV')
    ob.location=(0,0,0);ob.name=g['slug']
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    info['file']=g['tag']+'.fbx'
    bpy.ops.export_scene.fbx(filepath=str(OUT/info['file']),use_selection=True,object_types={'MESH'},
        axis_forward='Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,
        mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
    info['sha256']=hashlib.sha256((OUT/info['file']).read_bytes()).hexdigest()
    manifest.append(info)
    ob.location=(i%4*5.5,i//4*5.5,0)
    for hi in g['high']:hi.hide_render=True;hi.hide_set(True)

scene.world=bpy.data.worlds.new('Neutral component review');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.45,.45,.45,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.object.light_add(type='AREA',location=(5,-8,15));lamp=bpy.context.object
lamp.data.energy=2100;lamp.data.size=8
lamp.rotation_euler=(Vector((8,5,1))-lamp.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(29,-30,27));camera=bpy.context.object
scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=29
camera.rotation_euler=(Vector((8,5,1))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=2000;scene.render.resolution_y=1400;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ART/'sandstone-blender-overview.png')
bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
blend=ART/'SandstoneBuildKit.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
report=dict(parts=manifest,atlasSize=SIZE,blend_sha256=hashlib.sha256(blend.read_bytes()).hexdigest(),
            trianglesSelectable=sum(p['triangles'] for p in manifest if p['slope']!='falling'),
            palette=['#C88750','#DBA46D','#E9BB86','#B46F43','#997452'])
(ART/'sandstone-models.json').write_text(json.dumps(report,indent=2)+'\n')
print('SANDSTONE SOURCE PASS',[(p['tag'],p['triangles']) for p in manifest],flush=True)
