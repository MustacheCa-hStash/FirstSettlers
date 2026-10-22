"""L10-L17: editable stonework, real apertures, low solids and a shared material bake."""
import bpy,bmesh,math,json,hashlib,random
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/LimestoneBuildKit/Courtyard'
OUT=ROOT/'Assets/Models/Buildings/LimestoneCourtyard'
TEX=ROOT/'Assets/Textures/Buildings/LimestoneCourtyard'
for p in (ART,OUT,TEX):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.render.engine='CYCLES'
scene.cycles.samples=4;scene.render.threads_mode='FIXED';scene.render.threads=12
scene.view_settings.view_transform='Standard'
highs=bpy.data.collections.new('Editable individual limestone blocks');scene.collection.children.link(highs)
lows=bpy.data.collections.new('Runtime closed solids');scene.collection.children.link(lows)
source=bpy.data.images.load(str(ROOT/'ArtReferences/LimestoneBuildKit/Textures/limestone-timber-trim-source.png'))
def mat(name,tint,plain=False):
    m=bpy.data.materials.new(name);m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;s=n.get('Principled BSDF')
    s.inputs['Roughness'].default_value=.95;s.inputs['Specular IOR Level'].default_value=0
    if plain:s.inputs['Base Color'].default_value=(*tint,1)
    else:
        tex=n.new('ShaderNodeTexImage');tex.image=source
        uv=n.new('ShaderNodeTexCoord');scale=n.new('ShaderNodeVectorMath');scale.operation='SCALE';scale.inputs[3].default_value=.20
        shift=n.new('ShaderNodeVectorMath');shift.operation='ADD';shift.inputs[1].default_value=(.02,.02,0)
        mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=(*tint,1)
        l.new(uv.outputs['UV'],scale.inputs[0]);l.new(scale.outputs[0],shift.inputs[0]);l.new(shift.outputs[0],tex.inputs['Vector'])
        l.new(tex.outputs['Color'],mix.inputs[1]);l.new(mix.outputs[0],s.inputs['Base Color'])
    return m
palette=[mat('Chalky warm limestone '+str(i),(v,v*.99,v*.96)) for i,v in enumerate([.91,.97,1.02,.94,1.00,1.05])]
mortar=mat('Recessed warm joint fill',(.42,.39,.31),True)
runtime=mat('L10-L17 shared baked material',(1,1,1),True)
target=runtime.node_tree.nodes.new('ShaderNodeTexImage');runtime.node_tree.nodes.active=target
rng=random.Random(1017)
current=None;groups=[]
def make(name,verts,faces,collection,material,bevel=0):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in verts],[],faces)
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj);mesh.materials.append(material)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    if bevel:
        mod=obj.modifiers.new('Shallow hand-cut edges','BEVEL');mod.width=bevel;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0,name
    bm.to_mesh(obj.data);bm.free();obj.data.update()
    uv=obj.data.uv_layers.new(name='UVMap')
    for face in obj.data.polygons:
        axes=sorted(range(3),key=lambda a:abs(face.normal[a]))[:2]
        for li in face.loop_indices:
            p=obj.data.vertices[obj.data.loops[li].vertex_index].co;uv.data[li].uv=(.08+.16*p[axes[0]],.08+.16*p[axes[1]])
    obj.location.x=current['offset'] if current else 0
    return obj
def prism(name,poly,z0,z1,col,material,bevel=0):
    n=len(poly);return make(name,[(x,y,z) for z in (z0,z1) for x,y in poly],
        [tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],col,material,bevel)
def rect(x0,y0,x1,y1):return [(x0,y0),(x1,y0),(x1,y1),(x0,y1)]
def area(poly):return sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(poly,poly[1:]+poly[:1]))/2
def inset(poly,distance):
    if area(poly)<0:poly=poly[::-1]
    result=poly[:]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        length=math.dist(a,b);cross=lambda p:(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])-distance*length
        output=[]
        for p,q in zip(result,result[1:]+result[:1]):
            dp,dq=cross(p),cross(q)
            if dp>=0:output.append(p)
            if (dp>=0)!=(dq>=0):
                t=dp/(dp-dq);output.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
        result=output
        if len(result)<3:return []
    return result
def clip(poly,bound):
    if area(bound)<0:bound=bound[::-1]
    for a,b in zip(bound,bound[1:]+bound[:1]):
        result=[]
        cross=lambda p:(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
        for p,q in zip(poly,poly[1:]+poly[:1]):
            dp,dq=cross(p),cross(q)
            if dp>=-1e-8:result.append(p)
            if (dp>=0)!=(dq>=0):
                t=dp/(dp-dq);result.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
        poly=result
        if len(poly)<3:return []
    clean=[]
    for p in poly:
        if not clean or math.dist(p,clean[-1])>1e-6:clean.append(p)
    if len(clean)>1 and math.dist(clean[0],clean[-1])<1e-6:clean.pop()
    return clean if len(clean)>=3 and abs(area(clean))>1e-6 else []
def start(tag,slug,name,size,minimum):
    global current
    current=dict(tag=tag,slug=slug,name=name,size=size,minimum=minimum,offset=len(groups)*8,low=[],high=[])
    groups.append(current)
def low(poly,z0=-.25,z1=.25,bevel=0):
    o=prism(current['tag']+' runtime solid',poly,z0,z1,lows,runtime,bevel);current['low'].append(o);return o
def high(poly,z0=-.25,z1=.25,bevel=.006,material=None):
    o=prism(current['tag']+' individual stone',poly,z0,z1,highs,material or rng.choice(palette),bevel);current['high'].append(o);return o
def masonry(polygons,width,height,z0=-.25,z1=.25,course=.25):
    # The small low-poly cuts remain a continuous recessed backing during baking.
    for poly in polygons:
        core=inset(poly,.012)
        if core:high(core,z0+.014,z1-.014,0,mortar)
    rows=math.ceil(height/course)
    for row in range(rows):
        y0=row*course;y1=min(height,(row+1)*course)
        edges=sorted(set([0,width]+[max(0,min(width,k*.64+(.32 if row%2 else 0))) for k in range(-1,math.ceil(width/.64)+1)]))
        for x0,x1 in zip(edges[:-1],edges[1:]):
            if x1-x0<.02:continue
            block=rect(x0+(.004 if x0 else 0),y0+(.004 if row else 0),x1-(.004 if x1<width else 0),y1-(.004 if y1<height else 0))
            pieces=[p for poly in polygons if (p:=clip(block,poly))]
            # Dissolve decomposition cuts inside a single block. Only real block
            # edges and the outer arch curve receive a chamfer, never pinstripes.
            bm=bmesh.new();cache={}
            for poly in pieces:
                verts=[]
                for x,y in poly:
                    key=(round(x,6),round(y,6))
                    if key not in cache:cache[key]=bm.verts.new((x,y,0))
                    verts.append(cache[key])
                if len(set(verts))>=3:bm.faces.new(verts)
            if bm.faces:
                bm.normal_update()
                bmesh.ops.dissolve_edges(bm,edges=[e for e in bm.edges if len(e.link_faces)==2],use_verts=True)
                for face in bm.faces:
                    if face.calc_area()>1e-6:high([(v.co.x,v.co.y) for v in face.verts],z0,z1)
            bm.free()
def arch(spring,bottom=0):
    # Twelve segments preserve a true crown vertex. Low solids are convex slabs,
    # rather than a box or convex collider spanning the opening.
    arc=[(2+.75*math.cos(math.pi-i*math.pi/12),spring+.75*math.sin(math.pi-i*math.pi/12)) for i in range(13)]
    polygons=[rect(0,0,1.25,3),rect(2.75,0,4,3)]
    if bottom:polygons.append(rect(1.25,0,2.75,bottom))
    polygons += [[a,b,(b[0],3),(a[0],3)] for a,b in zip(arc[:-1],arc[1:])]
    for p in polygons:low(p)
    # The ashlar stops at the outer ring, leaving radial voussoirs readable.
    outside=[rect(0,0,1,3),rect(3,0,4,3)]
    if bottom:outside.append(rect(1,0,3,bottom-.15))
    outer_arc=[(2+math.cos(math.pi-i*math.pi/12),spring+math.sin(math.pi-i*math.pi/12)) for i in range(13)]
    outside += [[a,b,(b[0],3),(a[0],3)] for a,b in zip(outer_arc[:-1],outer_arc[1:])]
    masonry(outside,4,3,course=.375 if not bottom else .25)
    # Proper solid jambs beneath a load-bearing radial ring.
    for a,b in [(1,1.25),(2.75,3)]:
        y=bottom
        while y<spring-.001:
            end=min(spring,y+.25);high(rect(a,y+.002,b,end-.002));y=end
    if bottom:high(rect(1,bottom-.15,3,bottom))
    for i in range(13):
        a=i*math.pi/13+.003;b=(i+1)*math.pi/13-.003
        aa=i*math.pi/13;bb=(i+1)*math.pi/13
        core=[(2+.765*math.cos(t),spring+.765*math.sin(t)) for t in (aa,(aa+bb)/2,bb)]+[(2+.985*math.cos(t),spring+.985*math.sin(t)) for t in (bb,(aa+bb)/2,aa)]
        high(core,-.235,.235,0,mortar)
        inner=[(2+.75*math.cos(t),spring+.75*math.sin(t)) for t in (a,(a+b)/2,b)]
        outer=[(2+math.cos(t),spring+math.sin(t)) for t in (b,(a+b)/2,a)]
        high(inner+outer,bevel=.004)
    current['opening']=[1.25,bottom,2.75,spring+.75];current['spring']=spring
start('L10','l10-arched-limestone-doorway','L10 arched limestone doorway',[4,3,.5],[0,0,-.25]);arch(1.5)
start('L11','l11-narrow-limestone-window','L11 narrow limestone window',[4,3,.5],[0,0,-.25])
polys=[rect(0,0,1.75,3),rect(2.25,0,4,3),rect(1.75,0,2.25,1.25),rect(1.75,2.25,2.25,3)]
for p in polys:low(p)
# Keep one visible stone lintel instead of several unsupported little blocks.
highpolys=[rect(0,0,1.75,2.25),rect(2.25,0,4,2.25),rect(1.75,0,2.25,1.25),rect(0,2.25,1.5,2.5),rect(2.5,2.25,4,2.5),rect(0,2.5,4,3)]
masonry(highpolys,4,3);high(rect(1.5,2.25,2.5,2.5));current['opening']=[1.75,1.25,2.25,2.25]
start('L12','l12-arched-limestone-window','L12 arched limestone window',[4,3,.5],[0,0,-.25]);arch(1.75,1)
for tag,width in [('L13',4),('L14',2)]:
    start(tag,tag.lower()+'-'+('low' if width==4 else 'short')+'-courtyard-wall',tag+' '+('low' if width==4 else 'short')+' courtyard wall',[width,1.25,.5],[0,0,-.25])
    p=rect(0,0,width,1.25);low(p);masonry([p],width,1.25)
start('L15','l15-chamfered-limestone-coping','L15 chamfered limestone coping',[4,.25,.5],[0,0,-.25])
low(rect(0,0,4,.25),bevel=.025)
for i in range(8):high(rect(i*.5+(.003 if i else 0),0,(i+1)*.5-(.003 if i<7 else 0),.25),bevel=.025)
start('L16','l16-limestone-courtyard-pier','L16 limestone courtyard pier',[.75,1.5,.75],[-.375,0,-.375])
low(rect(-.325,0,.325,1.25),-.325,.325);low(rect(-.375,1.25,.375,1.5),-.375,.375,.03)
high(rect(-.315,0,.315,1.25),-.315,.315,0,mortar)
for row in range(5):high(rect(-.325,row*.25+.003,.325,(row+1)*.25-.003),-.325,.325,.008)
high(rect(-.375,1.25,.375,1.5),-.375,.375,.03)
for falling in (False,True):
    start('L17_Falling' if falling else 'L17','l17-limestone-gable-'+('falling' if falling else 'rising'),'L17 limestone half-gable · '+('falling' if falling else 'rising'),[2,2,.5],[0,0,-.25])
    p=[(0,0),(2,0),(0,2)] if falling else [(0,0),(2,0),(2,2)];low(p);masonry([p],2,2)
    current['slope']='falling' if falling else 'rising'
# Join only the low meshes to pack UVs together; separate back into their original
# objects afterward. This leaves individual editable high stones in the .blend.
bpy.ops.object.select_all(action='DESELECT')
for g in groups:
    for obj in g['low']:obj.select_set(True)
bpy.context.view_layer.objects.active=groups[0]['low'][0]
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.012,area_weight=.15,correct_aspect=True,scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True,margin=.008)
bpy.ops.object.mode_set(mode='OBJECT')
# UV packing in multi-object edit mode produces a common atlas without merging.
copies=[]
for g in groups:
    for obj in g['low']:
        clone=obj.copy();clone.data=obj.data.copy();lows.objects.link(clone);clone.hide_render=False;copies.append(clone);obj.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
for obj in copies:obj.select_set(True)
bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();bake_obj=bpy.context.object
bpy.ops.object.select_all(action='DESELECT');source_copies=[]
for g in groups:
    for obj in g['high']:
        clone=obj.copy();clone.data=obj.data.copy();highs.objects.link(clone);clone.hide_render=False;clone.select_set(True);source_copies.append(clone);obj.hide_render=True
bpy.context.view_layer.objects.active=source_copies[0];bpy.ops.object.join();combined_source=bpy.context.object
bake_obj.select_set(True)
bpy.context.view_layer.objects.active=bake_obj
scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.065;scene.render.bake.max_ray_distance=.15
scene.render.bake.margin=12;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
def bake(kind,space):
    image=bpy.data.images.new('L10-L17 '+kind,width=2048,height=2048,alpha=True);image.colorspace_settings.name=space;target.image=image
    print('COURTYARD BAKE',kind,flush=True);bpy.ops.object.bake(type=kind);return image
colour=bake('DIFFUSE','sRGB');normal=bake('NORMAL','Non-Color');ao=bake('AO','Non-Color')
p=np.empty(2048*2048*4,dtype=np.float32);q=np.empty_like(p);colour.pixels.foreach_get(p);ao.pixels.foreach_get(q);p[3::4]=np.clip(q[::4],0,1);colour.pixels.foreach_set(p)
for image,name in [(colour,'Courtyard_ColourAO.png'),(normal,'Courtyard_Normal.png')]:image.filepath_raw=str(TEX/name);image.file_format='PNG';image.save()
bpy.data.objects.remove(bake_obj,do_unlink=True)
bpy.data.objects.remove(combined_source,do_unlink=True)
# Reload saved maps so native previews and packed source use the same pixels
# that Unity imports, including the freshly packed AO alpha.
colour=bpy.data.images.load(str(TEX/'Courtyard_ColourAO.png'),check_existing=False)
colour.alpha_mode='CHANNEL_PACKED'
normal=bpy.data.images.load(str(TEX/'Courtyard_Normal.png'),check_existing=False);normal.colorspace_settings.name='Non-Color'
target.image=colour;nodes=runtime.node_tree.nodes;links=runtime.node_tree.links
links.new(target.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
nn=nodes.new('ShaderNodeTexImage');nn.image=normal;nm=nodes.new('ShaderNodeNormalMap');links.new(nn.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
manifest=[]
for g in groups:
    solids=[]
    for obj in g['low']:
        obj.data.calc_loop_triangles();solids.append(dict(vertices=[dict(x=v.co.x,y=v.co.z,z=v.co.y) for v in obj.data.vertices],triangles=[i for t in obj.data.loop_triangles for i in t.vertices]))
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for obj in g['low']:
        clone=obj.copy();clone.data=obj.data.copy();lows.objects.link(clone);clone.location=(0,0,0);clone.select_set(True);copies.append(clone)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();obj=bpy.context.object;obj.name=g['tag']+'_Baked'
    obj.data.calc_loop_triangles();uv=obj.data.uv_layers.active
    for tri in obj.data.loop_triangles:
        a,b,c=[uv.data[i].uv for i in tri.loops];assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(g['tag'],'UV')
    filename=g['tag']+'.fbx'
    bpy.ops.export_scene.fbx(filepath=str(OUT/filename),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
    info={k:v for k,v in g.items() if k not in ('low','high','offset')};info.update(file=filename,triangles=len(obj.data.loop_triangles),solids=solids,sourceComponents=len(g['high']))
    manifest.append(info);bpy.data.objects.remove(obj,do_unlink=True)
    for obj in g['high']:obj.hide_render=True;obj.hide_set(True)
    for obj in g['low']:obj.location=((len(manifest)-1)%3*6,(len(manifest)-1)//3*5,0);obj.hide_render=False
scene.world=bpy.data.worlds.new('Limestone review');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.35,.35,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.object.light_add(type='AREA',location=(5,-6,12));lamp=bpy.context.object;lamp.data.energy=2000;lamp.data.size=8
lamp.rotation_euler=(Vector((7,4,1))-lamp.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(17,-23,19));camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=25
camera.rotation_euler=(Vector((7,4,1))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=1800;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(ART/'courtyard-blender-overview.png');bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
blend=ART/'LimestoneCourtyard.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
(ART/'courtyard-models.json').write_text(json.dumps(dict(parts=manifest,atlasSize=2048,blend_sha256=hashlib.sha256(blend.read_bytes()).hexdigest()),indent=2)+'\n')
print('COURTYARD SOURCE PASS',[(p['tag'],p['triangles']) for p in manifest],flush=True)
