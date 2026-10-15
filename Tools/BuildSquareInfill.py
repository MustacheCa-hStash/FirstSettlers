"""Author only the square infill collection; preserve all other Blender mesh data."""
import bpy,bmesh,sys,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
OUT=ROOT/'Assets/Models/Buildings/Wood/Modular'
REPORT=ROOT/'ArtReferences/SquareInfill';REPORT.mkdir(parents=True,exist_ok=True)
def signature(o):
    return hashlib.sha256(json.dumps({'matrix':[list(r) for r in o.matrix_world],
        'vertices':[list(v.co) for v in o.data.vertices], 'faces':[list(p.vertices) for p in o.data.polygons],
        'uv':[[list(t.uv) for t in layer.data] for layer in o.data.uv_layers],
        'materials':[m.name if m else None for m in o.data.materials]},sort_keys=True).encode()).hexdigest()
before={o.name:signature(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('SquareInfill_')}
for c in list(bpy.data.collections):
    if c.name.startswith('Square infill kit'):
        for o in list(c.objects):bpy.data.objects.remove(o,do_unlink=True)
        bpy.data.collections.remove(c)
editable=bpy.data.collections.new('Square infill kit — editable');bpy.context.scene.collection.children.link(editable)
exports=bpy.data.collections.new('Square infill kit — exports (hidden)');bpy.context.scene.collection.children.link(exports)
wood=bpy.data.materials['Modular_Wood_Matte']
def box(name,lo,hi,grain,seed):
    # Unity logical XYZ -> Blender XZY; normals recalculated after axis conversion.
    points=[(hi[0] if i&1 else lo[0],hi[1] if i&2 else lo[1],hi[2] if i&4 else lo[2]) for i in range(8)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in points],[],
        [(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)])
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free();mesh.update()
    mesh.materials.append(wood);uv=mesh.uv_layers.new(name='UVMap')
    for p in mesh.polygons:
        normal=(p.normal.x,p.normal.z,p.normal.y)
        for li in p.loop_indices:uv.data[li].uv=sample(points[mesh.loops[li].vertex_index],normal,lo,hi,grain,seed)
    o=bpy.data.objects.new('SquareInfill_'+name,mesh);editable.objects.link(o);o['uv_seed']=seed;o['grain_axis']=grain
    return o
boards=[box('Board_'+str(i),(0 if i==0 else i*.25+.006,0,-.125),(2 if i==7 else (i+1)*.25-.006,2,.025),1,8500+i*17) for i in range(8)]
rails=[box('Rail_'+str(i),(0,y,.025),(2,y+.125,.125),0,8700+i*17) for i,y in enumerate((.125,.9375,1.75))]
post=[box('JunctionPost',(-.13,0,-.13),(.13,2.003,.13),1,8900)]
manifest={'models':[],'preservedExistingObjects':len(before),'dimensions':[2,2,.25],'atlas':'existing ordinary wood trim'}
def export(objects,file):
    triangles=0
    for o in objects:
        mesh=o.data;mesh.calc_loop_triangles();triangles+=len(mesh.loop_triangles)
        bm=bmesh.new();bm.from_mesh(mesh);assert all(e.is_manifold for e in bm.edges),o.name;bm.free()
        for t in mesh.loop_triangles:
            a,b,c=[mesh.uv_layers.active.data[i].uv for i in t.loops]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,o.name
            assert all(0<=v<=1 for uv in (a,b,c) for v in uv),o.name
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for src in objects:
        o=src.copy();o.data=src.data.copy();exports.objects.link(o);o.select_set(True);copies.append(o)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='SquareInfill_'+file.stem
    bpy.ops.export_scene.fbx(filepath=str(file),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
    joined.hide_set(True);joined.hide_render=True
    manifest['models'].append({'file':file.name,'components':len(objects),'triangles':triangles,'closedComponents':True,'allFaceUVsValid':True})
export(boards+rails,OUT/'SquareInfill_2x2x0.25.fbx');export(post,OUT/'SquareInfill_JunctionPost_2m.fbx')
for o in boards+rails:o.location=(0,46,0)
for o in post:o.location=(3,46,0)
assert {o.name:signature(o) for o in bpy.data.objects if o.name in before}==before
exports.hide_render=True;bpy.ops.object.select_all(action='DESELECT')
for o in boards+rails:o.select_set(True)
bpy.context.view_layer.objects.active=boards[0]
(REPORT/'blender-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('SQUARE INFILL BLENDER PASS',json.dumps(manifest))
