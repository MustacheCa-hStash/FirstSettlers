"""Blender source authoring. Preserve legacy meshes; add the approved full-span kit."""
import bpy,json,hashlib,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
OUT=ROOT/'Assets/Models/Buildings/Wood/Modular';OUT.mkdir(parents=True,exist_ok=True)
FLOORS=ROOT/'Assets/Models/Buildings/Wood/Floors'
REPORT=ROOT/'.utmp/modular-kit';REPORT.mkdir(parents=True,exist_ok=True)
def signature(o):
 return hashlib.sha256(json.dumps({'matrix':[list(r) for r in o.matrix_world],'verts':[list(v.co) for v in o.data.vertices],
 'faces':[list(p.vertices) for p in o.data.polygons],'uv':[[list(t.uv) for t in l.data] for l in o.data.uv_layers],
 'materials':[m.name if m else None for m in o.data.materials]},sort_keys=True).encode()).hexdigest()
before={o.name:signature(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('Modular_')}
for c in list(bpy.data.collections):
 if c.name.startswith('Modular kit'):
  for o in list(c.objects):bpy.data.objects.remove(o,do_unlink=True)
  bpy.data.collections.remove(c)
editable=bpy.data.collections.new('Modular kit — editable');bpy.context.scene.collection.children.link(editable)
exports=bpy.data.collections.new('Modular kit — exports and UV variants');bpy.context.scene.collection.children.link(exports)
wood=bpy.data.materials.get('Modular_Wood_Matte') or bpy.data.materials['Material.001'].copy();wood.name='Modular_Wood_Matte'
image=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-albedo.png'),check_existing=True)
def clean_material(mat,alpha=False):
 mat.use_nodes=True;nodes=mat.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial');bsdf=nodes.new('ShaderNodeBsdfPrincipled');tex=nodes.new('ShaderNodeTexImage');tex.image=image
 mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']);mat.node_tree.links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
 bsdf.inputs['Roughness'].default_value=.95;bsdf.inputs['Specular IOR Level'].default_value=.08
 if alpha:mat.node_tree.links.new(tex.outputs['Alpha'],bsdf.inputs['Alpha'])
 mat.surface_render_method='DITHERED';mat.use_backface_culling=False
clean_material(wood)
wattle=bpy.data.materials.get('Modular_Wattle_Matte') or wood.copy();wattle.name='Modular_Wattle_Matte';clean_material(wattle,True)
def unity(v):return Vector((v.x,v.z,v.y))
def assign_uv(obj,grain,seed,bounds=None):
 m=obj.data;coords=[unity(v.co) for v in m.vertices]
 lo,hi=bounds or ([min(v[a] for v in coords) for a in range(3)],[max(v[a] for v in coords) for a in range(3)])
 layer=m.uv_layers.active or m.uv_layers.new(name='UVMap')
 for poly in m.polygons:
  normal=unity(poly.normal)
  for li,index in zip(poly.loop_indices,poly.vertices):layer.data[li].uv=sample(coords[index],normal,lo,hi,grain,seed)
 obj['grain_axis_unity']=grain;obj['uv_seed']=seed;obj['uv_reference']=json.dumps([lo,hi])
def box(name,lo,hi,grain,seed,reference=None):
 vs=[(x,y,z) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])]
 fs=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
 m=bpy.data.meshes.new(name);m.from_pydata([(x,z,y) for x,y,z in vs],[],[tuple(reversed(f)) for f in fs]);m.update();m.materials.append(wood)
 o=bpy.data.objects.new(name,m);editable.objects.link(o);assign_uv(o,grain,seed,reference);return o
def source_parts(name,tag):
 src=bpy.data.objects[name].data;todo=set(range(len(src.vertices)));adj={i:set() for i in todo}
 for e in src.edges:a,b=e.vertices;adj[a].add(b);adj[b].add(a)
 result=[]
 while todo:
  visit=[todo.pop()];ids=set(visit)
  while visit:
   for i in adj[visit.pop()]:
    if i in todo:todo.remove(i);ids.add(i);visit.append(i)
  order=sorted(ids);mapping={a:i for i,a in enumerate(order)};polys=[p for p in src.polygons if p.vertices[0] in ids]
  coords=[(src.vertices[i].co.x*4/3.5,src.vertices[i].co.y-.125,src.vertices[i].co.z*3/2.75) for i in order]
  m=bpy.data.meshes.new(tag);m.from_pydata(coords,[],[[mapping[i] for i in p.vertices] for p in polys]);m.update()
  o=bpy.data.objects.new('Modular_'+tag+'_'+str(len(result)),m);editable.objects.link(o)
  sheet=all(src.uv_layers.active.data[i].uv.x>.50 and src.uv_layers.active.data[i].uv.y<.50 for p in polys for i in p.loop_indices)
  m.materials.append(wattle if tag=='WattleWall' else wood)
  if sheet:
   layer=m.uv_layers.new(name='UVMap')
   for new,old in zip(m.polygons,polys):
    for ni,oi in zip(new.loop_indices,old.loop_indices):layer.data[ni].uv=src.uv_layers.active.data[oi].uv
   o['preserved_wattle_uv']=True
  else:
   vv=[unity(v.co) for v in m.vertices];lo=[min(v[a] for v in vv) for a in range(3)];hi=[max(v[a] for v in vv) for a in range(3)]
   assign_uv(o,max(range(3),key=lambda a:hi[a]-lo[a]),100+len(result))
  for new,old in zip(m.polygons,polys):new.use_smooth=old.use_smooth
  result.append(o)
 return result
def export(objects,name,path,variant=0):
 bpy.ops.object.select_all(action='DESELECT');copies=[]
 for original in objects:
  o=original.copy();o.data=original.data.copy();exports.objects.link(o);o.location=(0,0,0);o.hide_set(False);o.hide_render=False
  if variant and 'uv_seed' in o:
   assign_uv(o,int(o['grain_axis_unity']),int(o['uv_seed'])+variant*104729,json.loads(o['uv_reference']))
  o.select_set(True);copies.append(o)
 bpy.context.view_layer.objects.active=copies[0]
 if len(copies)>1:bpy.ops.object.join()
 joined=bpy.context.object;joined.name='Modular_'+name+('_UV'+str(variant) if variant else '')+'_Export'
 bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,
  apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
 joined.data.calc_loop_triangles();joined.hide_set(True);joined.hide_render=True
 models.append({'name':name,'variant':variant,'path':str(path.relative_to(ROOT)),'triangles':len(joined.data.loop_triangles),'parts':len(objects)})
def subtract(lo,hi,cut):
 x0=max(lo[0],cut[0]);x1=min(hi[0],cut[2]);z0=max(lo[2],cut[1]);z1=min(hi[2],cut[3])
 if x1<=x0 or z1<=z0:return [(lo,hi)]
 return [(a,b) for a,b in [((lo[0],lo[1],lo[2]),(x0,hi[1],hi[2])),((x1,lo[1],lo[2]),hi),
 ((x0,lo[1],lo[2]),(x1,hi[1],z0)),((x0,lo[1],z1),(x1,hi[1],hi[2]))] if b[0]-a[0]>1e-5 and b[2]-a[2]>1e-5]
models=[]
wall=source_parts('Wood_SplitPlankWall','SplitPlankWall');woven=source_parts('Wattle_Wall','WattleWall')
post=[box('Modular_BayPost',(-.13,0,-.13),(.13,3.003,.13),1,9000)] # 3 mm visual cap relief; logical/collider height stays 3 m.
export(wall,'SplitPlankWall',OUT/'SplitPlankWall_4.00Wx3.00Hx0.25D.fbx');export(woven,'WattleWall',OUT/'WattleWall_4.00Wx3.00Hx0.25D.fbx')
for variant in range(3):export(post,'BayPost',OUT/('BayPost_0.26Wx3.00Hx0.26D'+('_UV'+str(variant) if variant else '')+'.fbx'),variant)
blueprint=json.loads((FLOORS/'floor-framing-blueprint.json').read_text());parts=blueprint['parts']
for i,p in enumerate(parts):p['uvSeed']=1000+i
floors=[]
for name,cut in [('TimberFloor',None),('TimberFloorStairwell',(.75,.5,3,4))]:
 objects=[];trims=[((.625,-.25,.5),(.75,0,4),2),((3,-.25,.5),(3.125,0,4),2),((.75,-.25,.375),(3,0,.5),0)] if cut else []
 for p in parts:
  remaining=subtract(p['min'],p['max'],cut) if cut else [(p['min'],p['max'])]
  for a,b,g in trims:remaining=[r for lo,hi in remaining for r in subtract(lo,hi,(a[0],a[2],b[0],b[2]))]
  for a,b in remaining:objects.append(box('Modular_'+name+'_'+str(len(objects)),a,b,p['grainAxis'],p['uvSeed'],(p['min'],p['max'])))
 for i,(a,b,g) in enumerate(trims):objects.append(box('Modular_'+name+'_Trimmer'+str(i),a,b,g,2000+i))
 for variant in range(3):export(objects,name,FLOORS/(name+'_4.00Wx0.25Hx4.00D'+('_UV'+str(variant) if variant else '')+'.fbx'),variant)
 floors.append(objects)
for objects,x in [(wall,0),(woven,6),(post,11),(floors[0],15),(floors[1],21)]:
 for o in objects:o.location=(x,22,0)
assert {o.name:signature(o) for o in bpy.data.objects if o.name in before}==before
exports.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
for o in wall:o.select_set(True)
bpy.context.view_layer.objects.active=wall[0]
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.region_3d.view_location=Vector((12,24,1.5));area.spaces.active.region_3d.view_distance=22
(FLOORS/'floor-framing-blueprint.json').write_text(json.dumps(blueprint,indent=2),encoding='utf-8')
(REPORT/'blender-models.json').write_text(json.dumps({'models':models,'legacy_objects_preserved':list(before)},indent=2),encoding='utf-8')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('MODULAR BLENDER AUTHORING PASS',json.dumps(models))
