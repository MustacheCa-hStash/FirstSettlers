"""Author a full-storey stair and segment handrail without changing existing Blender objects."""
import bpy,bmesh,json,hashlib,math,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
OUT=ROOT/'Assets/Models/Buildings/Wood/Stairs';OUT.mkdir(parents=True,exist_ok=True)
REPORT=ROOT/'ArtReferences/StairExtensionKit';REPORT.mkdir(parents=True,exist_ok=True)
def signature(o):
    return hashlib.sha256(json.dumps({'matrix':[list(r) for r in o.matrix_world],'v':[list(v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons],'uv':[[list(t.uv) for t in l.data] for l in o.data.uv_layers]},sort_keys=True).encode()).hexdigest()
before={o.name:signature(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('StairKit_')}
for c in list(bpy.data.collections):
    if c.name.startswith('Stair extension kit'):
        for o in list(c.objects):bpy.data.objects.remove(o,do_unlink=True)
        bpy.data.collections.remove(c)
editable=bpy.data.collections.new('Stair extension kit — editable');bpy.context.scene.collection.children.link(editable)
exports=bpy.data.collections.new('Stair extension kit — exports (hidden)');bpy.context.scene.collection.children.link(exports)
wood=bpy.data.materials['Modular_Wood_Matte'];manifest={'models':[],'existingObjectsPreserved':len(before)}
def uv(obj,seed,diagonal=False):
    mesh=obj.data
    def projection(v):
        p=(v.x,v.z,v.y)
        return (p[0],.8*p[1]-.6*p[2],.6*p[1]+.8*p[2]) if diagonal else p
    vs=[projection(v.co) for v in mesh.vertices];lo=[min(p[a] for p in vs) for a in range(3)];hi=[max(p[a] for p in vs) for a in range(3)]
    grain=2 if diagonal else max(range(3),key=lambda a:hi[a]-lo[a]);layer=mesh.uv_layers.active or mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        n=projection(poly.normal)
        for li in poly.loop_indices:layer.data[li].uv=sample(vs[mesh.loops[li].vertex_index],n,lo,hi,grain,seed)
def object_mesh(name,vs,faces,seed,diagonal=False):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in vs],[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free();mesh.materials.append(wood)
    obj=bpy.data.objects.new('StairKit_'+name,mesh);editable.objects.link(obj);uv(obj,seed,diagonal);return obj
src=bpy.data.objects['HalfFlightStair'].data;todo=set(range(len(src.vertices)));adj={i:set() for i in todo}
for edge in src.edges:a,b=edge.vertices;adj[a].add(b);adj[b].add(a)
components=[]
while todo:
    visit=[todo.pop()];ids=set(visit)
    while visit:
        for i in adj[visit.pop()]:
            if i in todo:todo.remove(i);ids.add(i);visit.append(i)
    components.append(sorted(ids))
full=[]
for flight in range(2):
    for i,ids in enumerate(components):
        mapping={a:j for j,a in enumerate(ids)};polys=[p for p in src.polygons if p.vertices[0] in ids]
        vertices=[(src.vertices[a].co.x,src.vertices[a].co.z+flight*1.5,src.vertices[a].co.y+flight*2) for a in ids]
        span=[max(v[a] for v in vertices)-min(v[a] for v in vertices) for a in range(3)]
        full.append(object_mesh('W22_'+str(flight)+'_'+str(i),vertices,[[mapping[a] for a in p.vertices] for p in polys],8100+flight*113+i,span[2]>1.8 and span[1]>1))
faces=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
def box(name,lo,hi,seed):return object_mesh(name,[(x,y,z) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])],faces,seed)
rail=[box('W24_LowPost',(.0625,0,0),(.1875,1,.125),9100),box('W24_HighPost',(.0625,1.5,1.875),(.1875,2.5,2),9101)]
rail.append(object_mesh('W24_SlopedRail',[(x,1+.75*z+h,z) for h in (-.1,0) for z in (0,2) for x in (.0625,.1875)],faces,9102,True))
def export(objects,tag,file,position):
    bpy.ops.object.select_all(action='DESELECT');copies=[];triangles=0
    for o in objects:
        o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
        for t in o.data.loop_triangles:
            a,b,c=[o.data.uv_layers.active.data[i].uv for i in t.loops]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(o.name,t.index)
            for v in (a,b,c):assert 0<=v.x<=1 and 0<=v.y<=1,(o.name,list(v))
        obj=o.copy();obj.data=o.data.copy();exports.objects.link(obj);obj.select_set(True);copies.append(obj)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='StairKit_'+tag+'_Export'
    bpy.ops.export_scene.fbx(filepath=str(OUT/file),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
    joined.hide_set(True);joined.hide_render=True
    for o in objects:o.location=position
    manifest['models'].append({'tag':tag,'file':file,'triangles':triangles,'parts':len(objects),'allFaceUVsValid':True})
export(full,'W22','W22_FullStoreyStair_1.25Wx3Hx4D.fbx',(0,40,0));export(rail,'W24','W24_StairHandrail_0.25Wx2.5Hx2D.fbx',(4,40,0))
assert {o.name:signature(o) for o in bpy.data.objects if o.name in before}==before
exports.hide_render=True;bpy.ops.object.select_all(action='DESELECT')
for o in full:o.select_set(True)
bpy.context.view_layer.objects.active=full[0]
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
(REPORT/'blender-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print('STAIR EXTENSION BLENDER PASS',json.dumps(manifest))
