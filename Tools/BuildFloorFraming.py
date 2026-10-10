"""Blender authoring: preserve existing pieces, add editable floor/frame kit."""
import bpy,json,hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/Models/Buildings/Wood/Floors';OUT.mkdir(parents=True,exist_ok=True)
REPORT=ROOT/'.utmp/framing';REPORT.mkdir(parents=True,exist_ok=True)
def signature(o):
 return hashlib.sha256(json.dumps({'matrix':[list(r) for r in o.matrix_world],'verts':[list(v.co) for v in o.data.vertices],
 'faces':[list(p.vertices) for p in o.data.polygons],'uv':[[list(t.uv) for t in l.data] for l in o.data.uv_layers],
 'materials':[m.name if m else None for m in o.data.materials]},sort_keys=True).encode()).hexdigest()
before={o.name:signature(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('Framing_')}
for c in list(bpy.data.collections):
 if c.name.startswith('Floor and framing'):
  for o in list(c.objects):bpy.data.objects.remove(o,do_unlink=True)
  bpy.data.collections.remove(c)
coll=bpy.data.collections.new('Floor and framing — editable');bpy.context.scene.collection.children.link(coll)
exports=bpy.data.collections.new('Floor and framing — joined exports');bpy.context.scene.collection.children.link(exports)
mat=bpy.data.materials.get('Framing_Matte')
if not mat:
 mat=bpy.data.materials['Material.001'].copy();mat.name='Framing_Matte'
node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
node.inputs['Roughness'].default_value=.95;node.inputs['Specular IOR Level'].default_value=.08
def box(name,lo,hi,grain):
 vs=[(x,y,z) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])]
 fs=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
 uv=[]
 for f in fs:
  n=(Vector(vs[f[1]])-Vector(vs[f[0]])).cross(Vector(vs[f[2]])-Vector(vs[f[0]])).normalized()
  cap=abs(n[grain])>.9;axes=[i for i in range(3) if abs(n[i])<.5]
  a=axes[0] if cap else grain;b=next(i for i in axes if i!=a)
  rect=(.01,.26,.24,.49) if cap else (.01,.77,.99,.85)
  uv.append([(rect[0]+(rect[2]-rect[0])*(vs[i][a]-lo[a])/(hi[a]-lo[a]),rect[1]+(rect[3]-rect[1])*(vs[i][b]-lo[b])/(hi[b]-lo[b])) for i in f])
 mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in vs],[],[list(reversed(f)) for f in fs]);mesh.materials.append(mat);mesh.update()
 layer=mesh.uv_layers.new(name='UVMap')
 for p,u in zip(mesh.polygons,uv):
  for li,t in zip(p.loop_indices,reversed(u)):layer.data[li].uv=t
 obj=bpy.data.objects.new(name,mesh);coll.objects.link(obj);return obj
def subtract(lo,hi,cut):
 x0=max(lo[0],cut[0]);x1=min(hi[0],cut[2]);z0=max(lo[2],cut[1]);z1=min(hi[2],cut[3])
 if x1<=x0 or z1<=z0:return [(lo,hi)]
 result=[]
 for a,b in [((lo[0],lo[1],lo[2]),(x0,hi[1],hi[2])),((x1,lo[1],lo[2]),hi),
             ((x0,lo[1],lo[2]),(x1,hi[1],z0)),((x0,lo[1],z1),(x1,hi[1],hi[2]))]:
  if b[0]-a[0]>1e-5 and b[2]-a[2]>1e-5:result.append((a,b))
 return result
parts=[]
def part(lo,hi,grain):parts.append({'min':lo,'max':hi,'grainAxis':grain})
# Rim spans between the four quarter-metre corner caps.
part((.25,-.25,0),(3.75,0,.25),0);part((.25,-.25,3.75),(3.75,0,4),0)
part((0,-.25,.25),(.25,0,3.75),2);part((3.75,-.25,.25),(4,0,3.75),2)
for x in (0,3.75):
 for z in (0,3.75):part((x,-.25,z),(x+.25,0,z+.25),1)
for i in range(14):
 x=.25+i*.25;part((x+.003,-.05,.25),(x+.247,0,3.75),2)
for z in (.55,1.25,1.95,2.65,3.35):part((.25,-.25,z-.06),(3.75,-.05,z+.06),0)
manifest={'parts':parts,'opening':{'minX':.75,'minZ':.5,'maxX':3,'maxZ':4},'models':[]}
layouts=[('TimberFloor',parts,None,0),('TimberFloorStairwell',parts,(.75,.5,3,4),6),
 ('RimBeam',[{'min':(0,-.25,0),'max':(3.5,0,.25),'grainAxis':0}],None,12),
 ('CornerBearingCap',[{'min':(0,-.25,0),'max':(.25,0,.25),'grainAxis':1}],None,17)]
for tag,items,cut,display in layouts:
 objects=[];n=0
 trimmers=[((.625,-.25,.5),(.75,0,4),2),((3,-.25,.5),(3.125,0,4),2),((.75,-.25,.375),(3,0,.5),0)] if cut else []
 for p in items:
  remaining=subtract(p['min'],p['max'],cut) if cut else [(p['min'],p['max'])]
  for tlo,thi,_ in trimmers:
   remaining=[r for lo,hi in remaining for r in subtract(lo,hi,(tlo[0],tlo[2],thi[0],thi[2]))]
  for lo,hi in remaining:
   objects.append(box('Framing_'+tag+'_'+str(n),lo,hi,p['grainAxis']));n+=1
 if cut:
  # Trimmers stay outside the clear opening. No threshold across its exit.
  for lo,hi,axis in trimmers:
   objects.append(box('Framing_'+tag+'_Trimmer_'+str(n),lo,hi,axis));n+=1
 bpy.ops.object.select_all(action='DESELECT');duplicates=[]
 for p in objects:
  o=p.copy();o.data=p.data.copy();exports.objects.link(o);o.select_set(True);duplicates.append(o)
 bpy.context.view_layer.objects.active=duplicates[0]
 if len(duplicates)>1:bpy.ops.object.join()
 joined=bpy.context.object;joined.name='Framing_'+tag+'_Export'
 joined.data.materials.clear();joined.data.materials.append(mat)
 for p in joined.data.polygons:p.material_index=0
 size=(4,.25,4) if tag.startswith('TimberFloor') else (3.5,.25,.25) if tag=='RimBeam' else (.25,.25,.25)
 file=f'{tag}_{size[0]:.2f}Wx{size[1]:.2f}Hx{size[2]:.2f}D.fbx'
 bpy.ops.export_scene.fbx(filepath=str(OUT/file),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',
  apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,
  use_tspace=False,path_mode='STRIP',bake_anim=False)
 joined.data.calc_loop_triangles();manifest['models'].append({'name':tag,'file':file,'triangles':len(joined.data.loop_triangles),'parts':n})
 joined.hide_set(True);joined.hide_render=True
 for p in objects:p.location=(display,12,0)
assert {o.name:signature(o) for o in bpy.data.objects if o.name in before}==before
exports.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
active=bpy.data.objects['Framing_TimberFloor_0'];active.select_set(True);bpy.context.view_layer.objects.active=active
(OUT/'floor-framing-blueprint.json').write_text(json.dumps(manifest,indent=2))
(REPORT/'blender-report.json').write_text(json.dumps({'models':manifest['models'],'existing_objects_preserved':list(before)},indent=2))
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('FLOOR FRAMING BLENDER PASS',json.dumps(manifest['models']))
