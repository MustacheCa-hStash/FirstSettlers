"""Author W05/W06/W07/W08/W12 in an independent editable Blender source."""
import bpy, bmesh, json, sys, hashlib
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
ART=ROOT/'ArtReferences/IronAgeWood/Extension'
OUT=ROOT/'Assets/Models/Buildings/Wood/Extension'
ART.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
editable=bpy.data.collections.new('Wood extension — editable members');scene.collection.children.link(editable)
exports=bpy.data.collections.new('Wood extension — exports');scene.collection.children.link(exports)
image=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png'));image.pack()
def material(name,alpha=False):
 m=bpy.data.materials.new(name);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=1;bs.inputs['Specular IOR Level'].default_value=0
 tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
 m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
 if alpha:m.node_tree.links.new(tex.outputs['Alpha'],bs.inputs['Alpha'])
 m.surface_render_method='DITHERED';m.use_backface_culling=False
 return m
wood=material('Meadow timber — shared trim');wattle=material('Woven fence — shared cutout trim',True)
serial=0
def box(name,lo,hi,grain,mat=wood):
 global serial
 serial+=1
 vs=[(x,z,y) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])]
 fs=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],[tuple(reversed(f)) for f in fs]);me.update()
 ob=bpy.data.objects.new(name,me);editable.objects.link(ob);bpy.context.view_layer.objects.active=ob
 bevel=ob.modifiers.new('Hewn edge','BEVEL');bevel.width=.004;bevel.segments=1;bpy.ops.object.modifier_apply(modifier=bevel.name)
 bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free();me.update()
 me.materials.append(mat);uv=me.uv_layers.new(name='UVMap')
 for poly in me.polygons:
  n=Vector((poly.normal.x,poly.normal.z,poly.normal.y))
  for li in poly.loop_indices:
   p=me.vertices[me.loops[li].vertex_index].co
   uv.data[li].uv=sample((p.x,p.z,p.y),n,lo,hi,grain,serial*7919)
 return ob
def woven_sheet():
 # Front and rear surfaces keep the cutout weave separated from the timber rails.
 me=bpy.data.meshes.new('W12 broad weave')
 me.from_pydata([(0.125,z,y) for z in (-.045,.045) for y in (.125,1.125)]+[(3.875,z,y) for z in (-.045,.045) for y in (.125,1.125)],[],[(0,4,5,1),(2,3,7,6)])
 me.update();bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free()
 uv=me.uv_layers.new(name='UVMap')
 for poly in me.polygons:
  for li in poly.loop_indices:
   p=me.vertices[me.loops[li].vertex_index].co
   uv.data[li].uv=(.51+(p.x-.125)/3.75*.48,.015+(p.z-.125)*.30)
 me.materials.append(wattle);ob=bpy.data.objects.new('W12 broad weave',me);editable.objects.link(ob);return ob
groups=[]
def group(tag,slug,name,size,minimum,parts,solids=None):
 groups.append(dict(tag=tag,slug=slug,name=name,size=size,minimum=minimum,objects=parts,solids=solids or []))
parts=[];solids=[]
for z in (0,.9375,1.875,2.8125,3.75):
 lo=(0,-.5,z);hi=(4,-.25,z+.25);parts.append(box('W05 lower sleeper',lo,hi,0));solids.append(dict(minimum=lo,maximum=hi))
for x in (0,.9375,1.875,2.8125,3.75):
 lo=(x,-.25,0);hi=(x+.25,0,4);parts.append(box('W05 upper sleeper',lo,hi,2));solids.append(dict(minimum=lo,maximum=hi))
group('W05','w05-sleeper-footing','W05 sleeper footing',(4,.5,4),(0,-.5,0),parts,solids)
group('W06','w06-horizontal-beam','W06 horizontal beam',(4,.25,.25),(0,-.25,-.125),[box('W06 hewn beam',(0,-.25,-.125),(4,0,.125),0)])
for tag,slug,label,width,height in [('W07','w07-low-plank-wall','W07 low plank wall',4,1.25),('W08','w08-narrow-wall','W08 narrow wall',2,3)]:
 parts=[];count=int(width/.25)
 for i in range(count):
  x0=i*.25+(0 if i==0 else .002);x1=(i+1)*.25-(0 if i==count-1 else .002)
  parts.append(box(tag+' riven board '+str(i),(x0,0,-.075),(x1,height,.075),1))
 for y in ([.20,.925] if height<2 else [.25,1.40,2.60]):
  parts.append(box(tag+' front rail',(0,y,-.125),(width,y+.125,-.075),0))
  parts.append(box(tag+' rear rail',(0,y,.075),(width,y+.125,.125),0))
 group(tag,slug,label,(width,height,.25),(0,0,-.125),parts)
parts=[woven_sheet()]
for x in (0,.96875,1.9375,2.90625,3.875):parts.append(box('W12 stake',(x,0,-.125),(x+.125,1.25,.125),1,wattle))
for y in (.125,1.0):parts.append(box('W12 rail',(0,y,-.11),(4,y+.125,-.045),0,wattle))
group('W12','w12-wattle-fence','W12 wattle fence',(4,1.25,.25),(0,0,-.125),parts)
group('Cover','low-junction-cover','Low junction cover',(.26,1.253,.26),(-.13,0,-.13),[box('Low shared junction post',(-.13,0,-.13),(.13,1.253,.13),1)])
manifest=[]
for index,g in enumerate(groups):
 bpy.ops.object.select_all(action='DESELECT');copies=[]
 for original in g['objects']:
  ob=original.copy();ob.data=original.data.copy();exports.objects.link(ob);ob.select_set(True);copies.append(ob)
 bpy.context.view_layer.objects.active=copies[0]
 if len(copies)>1:bpy.ops.object.join()
 joined=bpy.context.object;joined.name=g['tag']+'_Export'
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 bpy.ops.export_scene.fbx(filepath=str(OUT/(g['tag']+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
 joined.data.calc_loop_triangles()
 manifest.append({k:v for k,v in g.items() if k!='objects'}|dict(file=g['tag']+'.fbx',triangles=len(joined.data.loop_triangles)))
 joined.hide_render=True;joined.hide_set(True)
 for ob in g['objects']:ob.location=(index*5.5,0,0)
exports.hide_render=True
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=8
scene.world=bpy.data.worlds.new('Review world');scene.world.color=(.35,.35,.35)
light_data=bpy.data.lights.new('Large softbox','AREA');light_data.energy=2200;light_data.shape='DISK';light_data.size=12
light=bpy.data.objects.new('Large softbox',light_data);scene.collection.objects.link(light);light.location=(10,-6,12)
camera_data=bpy.data.cameras.new('Review camera');camera=bpy.data.objects.new('Review camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
camera_data.type='ORTHO';scene.render.image_settings.file_format='PNG';scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
def render(filename,eye,at,scale,width=1400,height=900):
 camera.location=eye;camera.rotation_euler=(Vector(at)-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.ortho_scale=scale
 scene.render.resolution_x=width;scene.render.resolution_y=height;scene.render.filepath=str(ART/filename);bpy.ops.render.render(write_still=True)
for g in groups:g['objects'][0].select_set(False)
for index,g in enumerate(groups[:5]):
 for other in groups:
  for ob in other['objects']:ob.hide_render=other is not g
 centre=index*5.5
 render(g['tag']+'-blender.png',(centre+6,-8,5),(centre+1.5,1,1),6.5)
 for ob in g['objects']:ob.hide_render=True
for g in groups:
 for ob in g['objects']:ob.hide_render=False
render('wood-extension-blender-overview.png',(15,-27,20),(12,0,1),31,2200,900)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'WoodExtensionKit.blend'))
(ART/'wood-extension-models.json').write_text(json.dumps(dict(parts=manifest,blend_sha256=hashlib.sha256((ART/'WoodExtensionKit.blend').read_bytes()).hexdigest()),indent=2))
print('WOOD EXTENSION AUTHORING PASS')
