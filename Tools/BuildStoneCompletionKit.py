"""Stone completion: editable stonework, real apertures, low solids and a shared material bake."""
import bpy,bmesh,math,json,hashlib,random
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/StoneBuildKit/Completion'
OUT=ROOT/'Assets/Models/Buildings/StoneCompletion'
TEX=ROOT/'Assets/Textures/Buildings/StoneCompletion'
for p in (ART,OUT,TEX):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.render.engine='CYCLES'
scene.cycles.samples=4;scene.render.threads_mode='FIXED';scene.render.threads=12
scene.view_settings.view_transform='Standard'
highs=bpy.data.collections.new('Editable individual rubble and timber');scene.collection.children.link(highs)
lows=bpy.data.collections.new('Runtime closed solids');scene.collection.children.link(lows)
source=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png'))
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
palette=[mat('Ordinary rubble '+str(i),(v,v,v*.98)) for i,v in enumerate([.91,.97,1.02,.94,1.00,1.05])]
mortar=mat('Recessed warm joint fill',(.21,.20,.18),True)
runtime=mat('Stone completion shared baked material',(1,1,1),True)
target=runtime.node_tree.nodes.new('ShaderNodeTexImage');runtime.node_tree.nodes.active=target
rng=random.Random(1824)
current=None;groups=[]
def make(name,verts,faces,collection,material,bevel=0):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in verts],[],faces)
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj);mesh.materials.append(material)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    if bevel:
        mod=obj.modifiers.new('Shallow hand-cut edges','BEVEL');mod.width=bevel;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    if collection==lows:bmesh.ops.triangulate(bm,faces=list(bm.faces))
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
    n=len(poly);verts=[(x,y,z) for z in (z0,z1) for x,y in poly]
    if current and current.get('axis')=='x':verts=[(z,y,x) for x,y,z in verts]
    return make(name,verts,
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
# S-family parts share the established stone baking and export workflow.
import sys
sys.path.insert(0,str(ROOT/'Tools'))

wood_image=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png'))
wood=bpy.data.materials.new('Shared meadow timber source');wood.use_nodes=True
bs=wood.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=1;bs.inputs['Specular IOR Level'].default_value=0
tx=wood.node_tree.nodes.new('ShaderNodeTexImage');tx.image=wood_image;wood.node_tree.links.new(tx.outputs['Color'],bs.inputs['Base Color'])
import sys
sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
def timber(poly,z0,z1,grain=0):
    ob=high(poly,z0,z1,.004,wood);coords=[Vector((v.co.x,v.co.z,v.co.y)) for v in ob.data.vertices]
    lo=[min(v[a] for v in coords) for a in range(3)];hi=[max(v[a] for v in coords) for a in range(3)]
    for face in ob.data.polygons:
        normal=Vector((face.normal.x,face.normal.z,face.normal.y))
        for li in face.loop_indices:ob.data.uv_layers.active.data[li].uv=sample(coords[ob.data.loops[li].vertex_index],normal,lo,hi,grain,len(current['high'])*7919)
    return ob
def rubble(polys,width,height,z0=-.25,z1=.25):
    for poly in polys:
        core=inset(poly,.008)
        if core:high(core,z0+.018,z1-.018,0,mortar)
    y=0;row=0
    while y<height-.001:
        top=min(height,y+rng.uniform(.27,.44));x=0
        while x<width-.001:
            end=min(width,x+rng.uniform(.38,.85))
            a=x+(.007 if x else 0);b=end-(.007 if end<width else 0)
            c=y+(.007 if y else 0);d=top-(.007 if top<height else 0)
            # Broad clipped corners break the rectangular coursed-wall silhouette.
            cut=min(.06,(b-a)*.14,(d-c)*.18)
            block=[(a+cut,c),(b-cut,c),(b,c+cut),(b,d-cut),(b-cut,d),(a+cut,d),(a,d-cut),(a,c+cut)]
            for boundary in polys:
                piece=clip(block,boundary)
                if piece:high(piece,z0,z1,.008)
            x=end
        y=top;row+=1
def boxlow(x0,y0,z0,x1,y1,z1):return low(rect(x0,y0,x1,y1),z0,z1)
def boxhigh(x0,y0,z0,x1,y1,z1,material=None):return high(rect(x0,y0,x1,y1),z0,z1,.008,material)

start('S01','s01-rubble-wall','S01 rubble wall',[4,3,.5],[0,0,-.25]);low(rect(0,0,4,3));rubble([rect(0,0,4,3)],4,3)
start('S05','s05-rubble-footing','S05 rubble footing',[4,.5,4],[0,-.5,0]);boxlow(0,-.5,0,4,0,4)
# Closed bedding with large top stones. Front/rear lower courses and side faces remain visible.
for x in range(5):
    for z in range(5):boxhigh(x*.8+.005,-.5,z*.8+.005,(x+1)*.8-.005,0,(z+1)*.8-.005)
start('S08','s08-narrow-rubble-wall','S08 narrow rubble wall',[2,3,.5],[0,0,-.25]);low(rect(0,0,2,3));rubble([rect(0,0,2,3)],2,3)

def opening(tag,slug,name,x0,x1,sill,head):
    start(tag,slug,name,[4,3,.5],[0,0,-.25]);current['opening']=[x0,sill,x1,head]
    pieces=[]
    if sill>0:pieces.append(rect(0,0,4,sill))
    pieces.extend([rect(0,sill,x0,head),rect(x1,sill,4,head),rect(0,head,x0-.25,head+.25),rect(x1+.25,head,4,head+.25),rect(0,head+.25,4,3)])
    for poly in pieces:low(poly)
    rubble(pieces,4,3)
    lintel=rect(x0-.25,head,x1+.25,head+.25);low(lintel);timber(lintel,-.25,.25)
opening('S09','s09-timber-lintel-doorway','S09 timber-lintel doorway',1.375,2.625,0,2.25)
opening('S11','s11-open-window-wall','S11 open window wall',1.5,2.5,1.25,2)

for tag,slug,name,h in [('S10','s10-static-plank-door','S10 static plank door',2.2),('S12G','s12-static-yard-gate','S12 static yard gate',1.15)]:
    start(tag,slug,name,[1.25,math.ceil(h*4)/4,.25],[0,0,0]);current['staticLeaf']=True
    low(rect(0,0,1.2,h),0,.08)
    for i in range(6):timber(rect(i*.2+(0 if i==0 else .002),0,(i+1)*.2-(0 if i==5 else .002),h),0,.055,1)
    for y in (.18,h-.30):timber(rect(0,y,1.2,y+.12),.055,.08)

for tag,slug,name,poly,slope in [('S15','s15-stone-gable-rising','S15 stone half-gable',[(0,0),(2,0),(2,2)],'rising'),('S15F','s15-stone-gable-falling','S15 stone half-gable falling',[(0,0),(2,0),(0,2)],'falling')]:
    start(tag,slug,name,[2,2,.5],[0,0,-.25]);current['slope']=slope;low(poly);rubble([poly],2,2)
start('S16','s16-timber-wall-plate','S16 timber wall plate',[4,.25,.5],[0,-.25,-.25]);low(rect(0,-.25,4,0));timber(rect(0,-.25,4,0),-.25,.25)

for tag,slug,label,rise,run,count in [('S17','s17-threshold-steps','S17 threshold steps',.5,1,2),('S18','s18-half-storey-stair','S18 half-storey stone stair',1.5,2,8),('S19','s19-full-storey-stair','S19 full-storey stone stair',3,4,16)]:
    start(tag,slug,label,[1.25,rise,run],[0,0,0]);current['axis']='x';current['steps']=count
    tread=run/count;riser=rise/count;profile=[(0,0),(run,0),(run,rise)]
    for i in range(count-1,-1,-1):
        profile.append((i*tread,(i+1)*riser))
        if i:profile.append((i*tread,i*riser))
    low(profile,0,1.25)
    rubble([rect(i*tread,0,(i+1)*tread,(i+1)*riser) for i in range(count)],run,rise,0,1.25)
start('S20','s20-stone-landing','S20 stone landing',[1.25,.25,1.25],[0,-.25,0]);low(rect(0,-.25,1.25,0),0,1.25)
for x in range(2):
    for z in range(2):boxhigh(x*.625+.005,-.25,z*.625+.005,(x+1)*.625-.005,0,(z+1)*.625-.005)
start('S21','s21-stepped-stone-guard','S21 stepped stone stair guard',[.5,2.5,2],[0,0,0]);current['axis']='x';current['guard']=True
profile=[(0,0),(2,0),(2,2.5)]
for i in range(7,-1,-1):
    profile.append((i*.25,1+(i+1)*.1875))
    if i:profile.append((i*.25,1+i*.1875))
low(profile,0,.5);rubble([rect(i*.25,0,(i+1)*.25,1+(i+1)*.1875) for i in range(8)],2,2.5,0,.5)
start('S23','s23-rubble-retaining-wall','S23 rubble retaining wall',[4,1.5,.75],[0,0,0]);current['axis']='x'
profile=[(0,0),(.75,0),(.5,1.5),(0,1.5)];low(profile,0,4)
for row in range(5):
    y0=row*.3;y1=(row+1)*.3
    poly=[(0,y0+.004),(.75-y0/6,y0+.004),(.75-y1/6,y1-.004),(0,y1-.004)]
    for i in range(6):high(poly,i*4/6+.004,(i+1)*4/6-.004,.008)
start('S24','s24-rubble-buttress','S24 rubble buttress',[1,3,1],[0,0,0]);current['axis']='x'
for i in range(3):
    low(rect(0,i,1-i*.25,i+1),0,1)
    for row in range(3):
        for x in range(2):high(rect(.004,i+row/3+.004,1-i*.25-.004,i+(row+1)/3-.004),x*.5+.004,(x+1)*.5-.004,.008)

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
    image=bpy.data.images.new('Stone completion '+kind,width=2048,height=2048,alpha=True);image.colorspace_settings.name=space;target.image=image
    print('STONE COMPLETION BAKE',kind,flush=True);bpy.ops.object.bake(type=kind);return image
colour=bake('DIFFUSE','sRGB');normal=bake('NORMAL','Non-Color');ao=bake('AO','Non-Color')
p=np.empty(2048*2048*4,dtype=np.float32);q=np.empty_like(p);colour.pixels.foreach_get(p);ao.pixels.foreach_get(q);p[3::4]=np.clip(q[::4],0,1);colour.pixels.foreach_set(p)
for image,name in [(colour,'StoneCompletion_ColourAO.png'),(normal,'StoneCompletion_Normal.png')]:image.filepath_raw=str(TEX/name);image.file_format='PNG';image.save()
bpy.data.objects.remove(bake_obj,do_unlink=True)
bpy.data.objects.remove(combined_source,do_unlink=True)
# Reload saved maps so native previews and packed source use the same pixels
# that Unity imports, including the freshly packed AO alpha.
colour=bpy.data.images.load(str(TEX/'StoneCompletion_ColourAO.png'),check_existing=False)
colour.alpha_mode='CHANNEL_PACKED'
normal=bpy.data.images.load(str(TEX/'StoneCompletion_Normal.png'),check_existing=False);normal.colorspace_settings.name='Non-Color'
target.image=colour;nodes=runtime.node_tree.nodes;links=runtime.node_tree.links
links.new(target.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
nn=nodes.new('ShaderNodeTexImage');nn.image=normal;nm=nodes.new('ShaderNodeNormalMap');links.new(nn.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
manifest=[]
for g in groups:
    solids=[]
    for obj in g['low']:
        obj.data.calc_loop_triangles();solids.append(dict(vertices=[dict(x=v.co.x,y=v.co.z,z=v.co.y) for v in obj.data.vertices],triangles=[i for t in obj.data.loop_triangles for i in t.vertices]))
    if g.get('steps') or g.get('guard') or g['tag']=='S24':
        solids=[]
        count=g.get('steps',8 if g.get('guard') else 3)
        for i in range(count):
            if g.get('steps') or g.get('guard'):
                z0=i*g['size'][2]/count;z1=(i+1)*g['size'][2]/count;y0=0;y1=(1+(i+1)*.1875) if g.get('guard') else (i+1)*g['size'][1]/count
            else:z0=0;z1=1-i*.25;y0=i;y1=i+1
            vv=[dict(x=x,y=y,z=z) for z in (z0,z1) for y in (y0,y1) for x in (0,g['size'][0])]
            tt=[0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3]
            solids.append(dict(vertices=vv,triangles=tt))
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
    for obj in g['low']:obj.location=((len(manifest)-1)%5*6,(len(manifest)-1)//5*6,0);obj.hide_render=False
scene.world=bpy.data.worlds.new('Ordinary stone review');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.35,.35,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.object.light_add(type='AREA',location=(5,-6,12));lamp=bpy.context.object;lamp.data.energy=2000;lamp.data.size=8
lamp.rotation_euler=(Vector((13,8,1))-lamp.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(40,-39,32));camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=43
camera.rotation_euler=(Vector((13,8,1))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=2400;scene.render.resolution_y=1600;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(ART/'stone-completion-blender-overview.png');bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
blend=ART/'StoneCompletionKit.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
(ART/'stone-completion-models.json').write_text(json.dumps(dict(parts=manifest,atlasSize=2048,blend_sha256=hashlib.sha256(blend.read_bytes()).hexdigest()),indent=2)+'\n')
print('STONE COMPLETION SOURCE PASS',[(p['tag'],p['triangles']) for p in manifest],flush=True)
