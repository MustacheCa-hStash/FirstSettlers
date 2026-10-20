"""Construct L25/L27/L28/L31/L33/L35, using the approved limestone/timber trim.

Blender --background --factory-startup --python-exit-code 1 --python Tools/BuildTimberInfillKit.py
Metres, Unity XYZ; all shingles, frames and braces remain real geometry.
"""
import hashlib
import json
import math
import random
import shutil
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/LimestoneBuildKit/TimberInfill'
OUT = ROOT / 'Assets/Models/Buildings/TimberInfill'
TEX = ROOT / 'Assets/Textures/Buildings/TimberInfill'
for folder in (ART, OUT, TEX):
    folder.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'ArtReferences/LimestoneBuildKit/Textures/limestone-timber-trim-source.png'
COLOUR = TEX / 'TimberInfill_Colour.png'
NORMAL = TEX / 'TimberInfill_Normal.png'
shutil.copy2(SOURCE, COLOUR)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.render.engine = 'CYCLES'
scene.cycles.samples = 16
scene.render.threads_mode = 'FIXED'
scene.render.threads = 12
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
colour = bpy.data.images.load(str(COLOUR))
material = bpy.data.materials.new('Approved limestone, hewn timber, lime daub and cleft shingles')
material.use_nodes = True
nodes, links = material.node_tree.nodes, material.node_tree.links
surface = nodes.get('Principled BSDF')
surface.inputs['Roughness'].default_value = .95
surface.inputs['Specular IOR Level'].default_value = 0
texture = nodes.new('ShaderNodeTexImage'); texture.image = colour
links.new(texture.outputs['Color'], surface.inputs['Base Color'])

# Bake a shallow tangent-space surface normal from the approved colour's grain.
# This is a Blender material bake; the colour source remains byte-for-byte intact.
bpy.ops.mesh.primitive_plane_add(size=2)
plane = bpy.context.object
plane.name = 'Trim surface normal bake plane'
plane.data.materials.append(material)
bump = nodes.new('ShaderNodeBump')
bump.inputs['Strength'].default_value = .17
bump.inputs['Distance'].default_value = .012
links.new(texture.outputs['Color'], bump.inputs['Height'])
links.new(bump.outputs['Normal'], surface.inputs['Normal'])
normal = bpy.data.images.new('Timber infill shallow grain normal', width=2048, height=2048, alpha=False)
normal.colorspace_settings.name = 'Non-Color'
target = nodes.new('ShaderNodeTexImage'); target.image = normal
nodes.active = target
scene.render.bake.use_selected_to_active = False
scene.render.bake.margin = 16
bpy.ops.object.bake(type='NORMAL')
normal.filepath_raw = str(NORMAL); normal.file_format = 'PNG'; normal.save()
links.remove(surface.inputs['Normal'].links[0])
normal_map = nodes.new('ShaderNodeNormalMap')
links.new(target.outputs['Color'], normal_map.inputs['Color'])
links.new(normal_map.outputs['Normal'], surface.inputs['Normal'])
bpy.data.objects.remove(plane, do_unlink=True)
collection = bpy.data.collections.new('Editable timber, daub panels and individually overlapping shingles')
scene.collection.children.link(collection)
groups = []
current = None
rng = random.Random(2533)


def uv_mesh(obj, kind, axis=0):
    """Grain follows each beam, rather than world axes. Sample inside atlas bands."""
    layer = obj.data.uv_layers.new(name='UVMap')
    points = [v.co for v in obj.data.vertices]
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    spans = hi - lo
    seed = rng.random()
    for face in obj.data.polygons:
        projection = sorted(range(3), key=lambda a: abs(face.normal[a]))[:2]
        if kind == 'wood' and axis in projection:
            projection = [axis, next(a for a in projection if a != axis)]
        for li in face.loop_indices:
            p = obj.data.vertices[obj.data.loops[li].vertex_index].co
            a, b = projection
            u = (p[a]-lo[a])/max(spans[a], 1e-6)
            v = (p[b]-lo[b])/max(spans[b], 1e-6)
            if kind == 'daub':
                uv = (.025 + .45*u, .30 + .21*v)
            elif kind == 'shingle':
                # One individual painted cleft board: avoid baked row shadows.
                x0 = [.512,.594,.674,.754,.836,.918][int(seed*6)%6]
                uv = (x0 + .065*u, .361 + .043*v)
            elif kind == 'wood' and axis not in projection:
                uv = (.275 + .19*u, .04 + .19*v)
            else:
                uv = (.025 + .94*u, .552 + .125*v)
            layer.data[li].uv = uv


def mesh(name, vertices, faces, kind='wood', axis=0, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata([(x,z,y) for x,y,z in vertices], [], faces)
    obj = bpy.data.objects.new(name, data); collection.objects.link(obj)
    data.materials.append(material)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    if bevel:
        mod = obj.modifiers.new('Small hand-hewn edge', 'BEVEL')
        mod.width, mod.segments = bevel, 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True) > 0, name
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    uv_mesh(obj, kind, axis)
    if current is not None: current['objects'].append(obj)
    return obj


FACES = [(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]


def box(name, lo, hi, kind='wood', bevel=.005):
    dims = [hi[i]-lo[i] for i in range(3)]
    unity_axis = max(range(3), key=lambda i: dims[i])
    return mesh(name, [(x,y,z) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])],
                FACES, kind, [0,2,1][unity_axis], bevel)


def prism(name, polygon, z0, z1, kind='wood', bevel=0):
    count = len(polygon)
    axis=2 if max(y for x,y in polygon)-min(y for x,y in polygon)>max(x for x,y in polygon)-min(x for x,y in polygon) else 0
    return mesh(name, [(x,y,z) for z in (z0,z1) for x,y in polygon],
                [tuple(range(count-1,-1,-1)),tuple(range(count,count*2))] +
                [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)], kind, axis, bevel)


def brace(name, a, b, width, z0, z1):
    # Ends are inset into their frame; no trim protrudes into an aperture.
    dx, dy = b[0]-a[0], b[1]-a[1]
    length = math.hypot(dx,dy); px,py = -dy/length*width/2,dx/length*width/2
    obj = prism(name, [(a[0]+px,a[1]+py),(b[0]+px,b[1]+py),(b[0]-px,b[1]-py),(a[0]-px,a[1]-py)],z0,z1,bevel=.003)
    # Rotate planar UV projection into the diagonal's actual long axis.
    layer = obj.data.uv_layers.active
    for face in obj.data.polygons:
        if abs(face.normal.y) < .5: continue
        for li in face.loop_indices:
            p = obj.data.vertices[obj.data.loops[li].vertex_index].co
            u = ((p.x-a[0])*dx+(p.z-a[1])*dy)/length/length
            v = ((p.x-a[0])*(-dy)+(p.z-a[1])*dx)/length/width + .5
            layer.data[li].uv = (.03+.93*u, .57+.085*v)
    return obj


def start(tag, slug, title, size, minimum, **extra):
    global current
    current = dict(tag=tag,slug=slug,name=title,size=size,minimum=minimum,objects=[],**extra)
    groups.append(current)


def wall(tag, opening=None):
    if opening:
        x0,y0,x1,y1=opening
        solids=[((0,0,-.085),(x0,3,.085)),((x1,0,-.085),(4,3,.085)),((x0,y1,-.085),(x1,3,.085))]
        if y0: solids.append(((x0,0,-.085),(x1,y0,.085)))
    else: solids=[((0,0,-.085),(4,3,.085))]
    for i,(lo,hi) in enumerate(solids):
        lo,hi=list(lo),list(hi)
        lo[0]=max(.25,lo[0]);hi[0]=min(3.75,hi[0]);lo[1]=max(.25,lo[1]);hi[1]=min(2.75,hi[1])
        if opening:
            if i==0:hi[0]-=.20
            if i==1:lo[0]+=.20
            if i==2:lo[1]+=.20
            if i==3:hi[1]-=.20
        box(tag+' recessed daub '+str(i),lo,hi,'daub',.002)
    for x in (0,3.75):box(tag+' full-depth end post',(x,0,-.125),(x+.25,3,.125))
    box(tag+' top plate',(.25,2.75,-.125),(3.75,3,.125))
    if not opening or opening[1]>0:
        box(tag+' sole plate',(.25,0,-.125),(3.75,.25,.125))
    else:
        box(tag+' left sole',(.25,0,-.125),(opening[0],.25,.125))
        box(tag+' right sole',(opening[2],0,-.125),(3.75,.25,.125))
    if not opening:
        box(tag+' centre stud',(1.875,.25,-.125),(2.125,2.75,.125))
        br=[((.34,.34),(1.79,2.66)),((2.21,2.66),(3.66,.34))]
    else:
        x0,y0,x1,y1=opening
        for a,b in [(x0-.20,x0),(x1,x1+.20)]:box(tag+' aperture jamb',(a,.25,-.125),(b,2.75,.125))
        box(tag+' lintel',(x0,y1,-.125),(x1,y1+.20,.125))
        if y0:box(tag+' sill',(x0,y0-.20,-.125),(x1,y0,.125))
        box(tag+' short head stud',(1.90,y1+.20,-.125),(2.10,2.75,.125))
        br=[((.34,.34),(x0-.28,2.65)),((x1+.28,2.65),(3.66,.34))]
    for side,z0,z1 in [('front',-.125,-.08),('back',.08,.125)]:
        for i,(a,b) in enumerate(br):brace(tag+' '+side+' knee '+str(i),a,b,.135,z0,z1)


start('L25','l25-framed-daub-wall','L25 framed daub wall',[4,3,.25],[0,0,-.125])
wall('L25')
start('L27','l27-framed-doorway','L27 framed doorway',[4,3,.25],[0,0,-.125],opening=[1.375,0,2.625,2.25])
wall('L27',current['opening'])
start('L28','l28-open-window-wall','L28 open window wall',[4,3,.25],[0,0,-.125],opening=[1.5,1,2.5,2])
wall('L28',current['opening'])
for falling in (False,True):
    tag='L31_Falling' if falling else 'L31'
    start(tag,'l31-framed-gable-falling' if falling else 'l31-framed-gable-rising',
          'L31 framed half-gable · falling' if falling else 'L31 framed half-gable · rising',[2,2,.25],[0,0,-.125],slope='falling' if falling else 'rising')
    # Frame strips are clipped within the triangle; no box fills the empty half.
    flip=lambda p:[(2-x,y) if falling else (x,y) for x,y in p]
    prism(tag+' inset lime daub',flip([(.48,.20),(1.8,.20),(1.8,1.52)]),-.085,.085,'daub')
    for label,p in [('sole',[(.28,0),(2,0),(2,.20),(.48,.20)]),
                    ('upright',[(1.8,.2),(2,.2),(2,1.72),(1.8,1.52)]),
                    ('rake',[(0,0),(2,2),(2,1.72),(.28,0)]),
                    ('stud',[(.90,.20),(1.06,.20),(1.06,.78),(.90,.62)])]:
        obj=prism(tag+' '+label,flip(p),-.125,.125,bevel=0 if label=='rake' else .003)
        if label=='rake':
            uv=obj.data.uv_layers.active
            for face in obj.data.polygons:
                if abs(face.normal.y)<.5:continue
                for li in face.loop_indices:
                    point=obj.data.vertices[obj.data.loops[li].vertex_index].co
                    x=2-point.x if falling else point.x
                    uv.data[li].uv=(.03+.93*(x+point.z)/4,.57+.085*(x-point.z)/.28)


def roof(tag,width,run,canopy=False):
    x0=-width/2 if canopy else 0
    height=lambda z:run-z if canopy else z
    # Entire underside is open between actual rafters and battens.
    for i in range(int(width)+1):
        x=x0+min(width-.16,max(.03,i-.065))
        vertices=[(a,height(z)+h,z) for z in (0,run) for h in (0,.13) for a in (x,x+.13)]
        mesh(tag+' rafter '+str(i),vertices,FACES)
    if not canopy:box(tag+' eave bearing',(0,0,0),(width,.13,.25))
    courses=7 if not canopy else 4
    exposure=(run+.22)/courses
    for row in range(courses):
        z=-.22+row*exposure
        # Thin battens sit within the slope envelope, not across attic space.
        zz=max(0,z+.16)
        mesh(tag+' batten '+str(row),[(x,height(zv)+h,zv) for zv in (zz,min(run,zz+.075)) for h in (.13,.175) for x in (x0,x0+width)],FACES)
    for row in range(-1,courses):
        z0=-.22+max(0,row)*exposure
        length=exposure*(1.72 if row>=0 else .95)
        z1=min(run,z0+length)
        count=14 if not canopy else 7
        edges=[x0]+[x0+width*(i+(.47 if row%2 else 0))/count for i in range(1,count)]+[x0+width]
        for col,(a,b) in enumerate(zip(edges[:-1],edges[1:])):
            gap=.0025
            a+=gap if col else 0;b-=gap if col<count-1 else 0
            # Uneven closed butt edges and tapered thickness survive every view.
            jitter=rng.uniform(-.025,.025) if row>=0 and row<courses-1 else 0
            front=max(-.22,z0+jitter)
            if canopy: front,z1local=run-z1,run-front
            else:z1local=z1
            zfront,zback=(front,z1local)
            polygon=[(a,zfront),(a+(b-a)*.42,zfront+rng.uniform(0,.018)),(b,zfront+.008),(b,zback),(a,zback)]
            countp=len(polygon)
            vertices=[]
            for top in (False,True):
                for x,z in polygon:
                    t=(z-(run-z0 if canopy else z0))*(-1 if canopy else 1)/max(length,.01)
                    # A second starter course gives the eave visible stacked thickness.
                    h=.19 + (.045 if row==-1 else .09*max(0,1-t)) + (.035 if top else 0)
                    vertices.append((x,height(z)+h,z))
            faces=[tuple(range(countp-1,-1,-1)),tuple(range(countp,countp*2))]+[(i,(i+1)%countp,(i+1)%countp+countp,i+countp) for i in range(countp)]
            obj=mesh(tag+f' cleft shingle course {row+1:02} board {col:02}',vertices,faces,'shingle')
            # Shingle grain runs downslope; all broad faces map the same individual board.
            uv=obj.data.uv_layers.active
            for face in obj.data.polygons:
                if face.index>1:continue
                for li in face.loop_indices:
                    p=obj.data.vertices[obj.data.loops[li].vertex_index].co
                    uv.data[li].uv=(.674+.065*(p.x-a)/(b-a), .361+.043*(p.y-zfront)/max(zback-zfront,.01))
    if canopy:
        # Butt joints at the wall posts avoid coplanar overlapping back faces.
        for a,b in [(-1,-.925),(-.775,.775),(.925,1)]:
            box(tag+' rear ledger',(a,.85,0),(b,1,.14))
        for x in (-.925,.775):
            box(tag+' wall upright',(x,-.75,0),(x+.15,1,.13))
            # A closed diagonal knee bracket in the YZ plane, outside the clear door.
            yz=[(-.70,.08),(.16,.88),(.28,.79),(-.52,.06)]
            vertices=[(a,y,z) for a in (x,x+.15) for y,z in yz]
            mesh(tag+' knee bracket',vertices,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])


start('L33','l33-layered-shingle-roof','L33 layered cleft-shingle roof',[4,2,2],[0,0,0])
roof('L33',4,2)
start('L35','l35-braced-door-canopy','L35 braced door canopy',[2,2.25,1.25],[-1,-.75,0])
roof('L35',2,1,True)


manifest=[]
for index,group in enumerate(groups):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in group['objects']:obj.select_set(True)
    bpy.context.view_layer.objects.active=group['objects'][0]
    # Individual members are retained in the native source, a joined copy is exported.
    bpy.ops.object.duplicate();bpy.ops.object.join()
    exported=bpy.context.object;exported.name=group['tag']+'_Runtime'
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    exported.data.calc_loop_triangles()
    uv=exported.data.uv_layers.active
    for triangle in exported.data.loop_triangles:
        a,b,c=[uv.data[li].uv for li in triangle.loops]
        assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12, (group['tag'],'collapsed UV')
    file=group['tag']+'.fbx'
    bpy.ops.export_scene.fbx(filepath=str(OUT/file),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,
        mesh_smooth_type='FACE',use_tspace=True,add_leaf_bones=False,bake_anim=False,axis_forward='Z',axis_up='Y',
        apply_unit_scale=True,bake_space_transform=True,path_mode='AUTO')
    vertices=[(p.co.x,p.co.z,p.co.y) for p in exported.data.vertices]
    info={k:v for k,v in group.items() if k!='objects'}
    info.update(file=file,triangles=len(exported.data.loop_triangles),members=len(group['objects']),
                visual_minimum=[min(p[i] for p in vertices) for i in range(3)],visual_maximum=[max(p[i] for p in vertices) for i in range(3)])
    manifest.append(info)
    bpy.data.objects.remove(exported,do_unlink=True)
    for obj in group['objects']:obj.location.x=index*6

world=bpy.data.worlds.new('Warm neutral review');scene.world=world
world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.34,.39,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.7
bpy.ops.object.light_add(type='AREA',location=(12,-8,14));bpy.context.object.data.energy=2400;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=8
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO'
def render(name,eye,at,scale,width=1600,height=1000):
    camera.location=eye;camera.rotation_euler=(Vector(at)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale;scene.render.resolution_x=width;scene.render.resolution_y=height;scene.render.resolution_percentage=100
    scene.render.filepath=str(ART/name);bpy.ops.render.render(write_still=True)

render('timber-infill-blender-overview.png',(22,-23,20),(18,0,1.3),43,2200,1000)
for index,group in enumerate(groups):
    for other in groups:
        for obj in other['objects']:obj.hide_render=other is not group
    centre=index*6+(0 if group['tag']=='L35' else 2 if group['tag']!='L31' and group['tag']!='L31_Falling' else 1)
    render(group['tag']+'-blender.png',(centre+5,8 if group['tag']=='L35' else -8,5),(centre,.5,1.2),5.5)
for group in groups:
    for obj in group['objects']:obj.hide_render=group['tag']!='L33'
render('L33-eave-depth.png',(35,-3,1.4),(32,.7,.9),4.4)
render('L33-underside.png',(31,4,-2),(32,1,1),5.6)
for group in groups:
    for obj in group['objects']:obj.hide_render=False
scene.render.film_transparent=False
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
blend=ART/'TimberInfillKit.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
report=dict(parts=manifest,selectable_parts=6,source_trim_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            blend_sha256=hashlib.sha256(blend.read_bytes()).hexdigest(),roof_covering='Cleft wood shingles with overlapping solid courses; no straw substitution',
            window='Clear 1 x 1 metre aperture, no shutter, glass or leaf',normal='Shallow tangent-space grain baked with Blender; live geometry supplies silhouette')
(ART/'timber-infill-models.json').write_text(json.dumps(report,indent=2)+'\n')
print('TIMBER INFILL SOURCE COMPLETE',[(p['tag'],p['triangles']) for p in manifest],flush=True)
