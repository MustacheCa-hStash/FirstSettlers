"""Editable fibre-bound torch and pegged wall holder; one atlas, one submesh per exported model."""
import bpy,bmesh,math,json
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/Torch';OUT=ROOT/'Assets/Models/Buildings/Lighting';TEX=ROOT/'Assets/Textures/Buildings/Lighting'
for p in [ART,OUT,TEX]:p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
editable=bpy.data.collections.new('Editable torch and holder');scene.collection.children.link(editable)
exports=bpy.data.collections.new('Unity exports');scene.collection.children.link(exports)
# Painted grain, rough plant cord and charred fuel occupy separate UV columns.
size=512
u,v=np.meshgrid(np.linspace(0,1,size),np.linspace(0,1,size))
rng=np.random.default_rng(1207);noise=rng.random((size,size))-.5
grain=np.sin(u*270+np.sin(v*19)*2)+.35*np.sin(u*850+v*7)
wood=np.stack([.34+grain*.025+noise*.035,.215+grain*.019+noise*.025,.105+grain*.012+noise*.02],axis=-1)
braid=np.sin(v*250+u*170)*np.sin(v*250-u*170)
cord=np.stack([.42+braid*.065,.335+braid*.055,.205+braid*.04],axis=-1)
crack=np.clip(np.sin(u*190+np.sin(v*31)*2)*6,0,1)
coal=np.stack([.065+noise*.025+crack*.025,.045+noise*.02+crack*.018,.03+noise*.015+crack*.013],axis=-1)
rgb=np.where((u<.5)[...,None],wood,np.where((u<.75)[...,None],cord,coal))
rgba=np.concatenate([np.clip(rgb,0,1),np.ones((size,size,1))],axis=-1).astype(np.float32)
image=bpy.data.images.new('Torch wood cord and charcoal atlas',width=size,height=size,alpha=True)
image.pixels.foreach_set(rgba.ravel());image.filepath_raw=str(TEX/'TorchAtlas.png');image.file_format='PNG';image.save();image.pack()
material=bpy.data.materials.new('Torch matte atlas');material.use_nodes=True
nodes=material.node_tree.nodes;bs=nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=1;bs.inputs['Specular IOR Level'].default_value=0
tex=nodes.new('ShaderNodeTexImage');tex.image=image;material.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
parts=[]
def mesh(name,verts,faces,zone='wood',bevel=0):
    data=bpy.data.meshes.new(name);data.from_pydata([(x,z,y) for x,y,z in verts],[],faces);data.update()
    obj=bpy.data.objects.new(name,data);editable.objects.link(obj);bpy.context.view_layer.objects.active=obj
    if bevel:
        modifier=obj.modifiers.new('Hewn edge bevel','BEVEL');modifier.width=bevel;modifier.segments=1;bpy.ops.object.modifier_apply(modifier=modifier.name)
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(data);bm.free();data.update()
    data.materials.append(material);uv=data.uv_layers.new(name='UVMap')
    points=[Vector((p.co.x,p.co.z,p.co.y)) for p in data.vertices]
    lo=Vector(tuple(min(p[a] for p in points) for a in range(3)));hi=Vector(tuple(max(p[a] for p in points) for a in range(3)))
    u0,u1={'wood':(.015,.485),'cord':(.515,.735),'coal':(.765,.985)}[zone]
    for polygon in data.polygons:
        normal=Vector((polygon.normal.x,polygon.normal.z,polygon.normal.y));dominant=max(range(3),key=lambda a:abs(normal[a]))
        a,b=[axis for axis in range(3) if axis!=dominant]
        if a==1:a,b=b,a
        for loop in polygon.loop_indices:
            p=points[data.loops[loop].vertex_index]
            uv.data[loop].uv=(u0+(u1-u0)*(p[a]-lo[a])/max(hi[a]-lo[a],1e-6),.025+.95*(p[b]-lo[b])/max(hi[b]-lo[b],1e-6))
    obj['atlas_zone']=zone;parts.append(obj);return obj
def box(name,lo,hi,zone='wood'):
    vs=[(lo[0],lo[1],lo[2]),(hi[0],lo[1],lo[2]),(hi[0],hi[1],lo[2]),(lo[0],hi[1],lo[2]),(lo[0],lo[1],hi[2]),(hi[0],lo[1],hi[2]),(hi[0],hi[1],hi[2]),(lo[0],hi[1],hi[2])]
    return mesh(name,vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)],zone,.002)
def rod(name,a,b,radius=.025,sides=8,zone='wood',top=None):
    a,b=Vector(a),Vector(b);axis=(b-a).normalized();tangent=axis.cross(Vector((0,1,0)))
    if tangent.length<.01:tangent=axis.cross(Vector((1,0,0)))
    tangent.normalize();other=axis.cross(tangent)
    vs=[tuple(center+r*(math.cos(i*math.tau/sides)*tangent+math.sin(i*math.tau/sides)*other)) for center,r in [(a,radius),(b,top or radius)] for i in range(sides)]
    return mesh(name,vs,[tuple(range(sides-1,-1,-1)),tuple(range(sides,2*sides))]+[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],zone)
def helix(name,center,y0,y1,radius,turns,tube=.004):
    steps=int(turns*12);sides=5;vs=[]
    for k in range(steps+1):
        t=k/steps;angle=t*math.tau*turns;c=Vector((center[0]+radius*math.cos(angle),y0+(y1-y0)*t,center[1]+radius*math.sin(angle)))
        radial=Vector((math.cos(angle),0,math.sin(angle)))
        for j in range(sides):vs.append(tuple(c+tube*(math.cos(j*math.tau/sides)*radial+math.sin(j*math.tau/sides)*Vector((0,1,0)))))
    fs=[(k*sides+j,k*sides+(j+1)%sides,(k+1)*sides+(j+1)%sides,(k+1)*sides+j) for k in range(steps) for j in range(sides)]
    return mesh(name,vs,fs,'cord')
# Shaft is upright; keeping a fixed head position allows cached shadows during flicker.
rod('Split ash handle',(0,.015,.32),(0,.665,.32),.027,9,top=.033)
for i in range(5):
    angle=i*math.tau/5
    rod('Fuel splint %02d'%i,(.022*math.cos(angle),.60,.32+.022*math.sin(angle)),(.047*math.cos(angle),.745-(i%3)*.014,.32+.047*math.sin(angle)),.018,5,'coal',.023)
helix('Plant fibre fuel binding',(0,.32),.585,.69,.052,7,.0045)
helix('Grip lashing',(0,.32),.16,.245,.03,7,.0035)
torch=list(parts)
box('Pegged wall backplate',(-.08,.14,0),(.08,.49,.038))
box('Lower holder arm',(-.07,.265,.038),(.07,.305,.34))
rod('Lower diagonal brace',(0,.155,.035),(0,.275,.275),.016,6)
rod('Upper left locating peg',(-.057,.295,.32),(-.057,.36,.32),.013,7)
rod('Upper right locating peg',(.057,.295,.32),(.057,.36,.32),.013,7)
rod('Upper wall peg',(-.025,.425,.038),(-.025,.425,.06),.011,7)
rod('Lower wall peg',(.025,.21,.038),(.025,.21,.06),.011,7)
manifest={'texture':'Assets/Textures/Buildings/Lighting/TorchAtlas.png','emitter':[0,.91,.32],'flame_anchor':[0,.715,.32],'models':[]}
def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for src in objects:
        obj=src.copy();obj.data=src.data.copy();exports.objects.link(obj);obj.select_set(True);copies.append(obj)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name=name
    tris=sum(len(p.vertices)-2 for p in joined.data.polygons)
    points=[(p.co.x,p.co.z,p.co.y) for p in joined.data.vertices]
    lo=[min(p[a] for p in points) for a in range(3)];hi=[max(p[a] for p in points) for a in range(3)]
    uv=joined.data.uv_layers.active
    for p in joined.data.polygons:
        coords=[uv.data[i].uv for i in p.loop_indices]
        for j in range(1,len(coords)-1):
            a,b,c=coords[0],coords[j],coords[j+1]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(name,p.index)
    path=OUT/(name+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
    joined.hide_render=True;joined.hide_set(True)
    manifest['models'].append({'file':path.name,'triangles':tris,'parts':len(objects),'minimum':lo,'maximum':hi})
export('MountedTorch',parts);export('HandTorch',torch)
exports.hide_render=True
# A neutral studio proof of the authored solid model; runtime flame is validated in Unity.
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.world=bpy.data.worlds.new('Torch studio');scene.world.color=(.3,.3,.3)
bpy.ops.object.light_add(type='AREA',location=(2,2,3));light=bpy.context.object;light.data.energy=300;light.data.size=3
bpy.ops.object.light_add(type='AREA',location=(-1,1,1));bpy.context.object.data.energy=80;bpy.context.object.data.size=2
bpy.ops.object.camera_add(location=(1.2,1.7,1.2));camera=bpy.context.object;camera.rotation_euler=(Vector((0,.18,.4))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.05;scene.camera=camera
scene.render.film_transparent=True;scene.render.filepath=str(ART/'torch-model.png');bpy.ops.render.render(write_still=True)
(ART/'torch-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(ART/'TorchKit.blend'))
print('TORCH MODEL PASS',json.dumps(manifest['models']))
