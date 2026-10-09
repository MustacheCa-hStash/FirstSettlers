"""Repair the user's remaining roof in the open Blender source; export one option."""
import bpy, bmesh, math, json, hashlib
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/Models/Buildings/Wood/Roofs'; OUT.mkdir(parents=True,exist_ok=True)
REPORT=ROOT/'.utmp/roof'; REPORT.mkdir(parents=True,exist_ok=True)
def fingerprint(o):
    data={'matrix':[list(r) for r in o.matrix_world], 'materials':[m.name if m else None for m in o.data.materials],
          'vertices':[list(v.co) for v in o.data.vertices], 'faces':[list(p.vertices) for p in o.data.polygons],
          'uv':[[list(d.uv) for d in layer.data] for layer in o.data.uv_layers]}
    return hashlib.sha256(json.dumps(data,sort_keys=True).encode()).hexdigest()
original={o.name:fingerprint(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('Roof_')}
thatch=bpy.data.objects.get('Roof_Single_Thatch')
if not thatch: raise RuntimeError('Expected the remaining Roof_Single_Thatch; do not regenerate deleted variants.')
parts=[thatch]+[bpy.data.objects['Roof_Single_'+name] for name in ('EaveRail','Rafter_0','Rafter_1','Rafter_2','WovenSubstrate')]
wattle_before=fingerprint(parts[-1])
material=thatch.data.materials[0]

# A genuine rhombal prism: four metres along X, 45-degree faces, .30 m
# perpendicular thickness. Vertical closed eave/ridge cuts; no waviness/laps.
half=math.sqrt(2)*.30
verts=[(x,z,z+.12+h) for h in (0,half) for z in (-.25,2) for x in (0,4)]
faces=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
mesh=bpy.data.meshes.new('Roof closed thatch prism — 8 vertices')
mesh.from_pydata(verts,[],faces);mesh.update()
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
mesh.materials.append(material);uv=mesh.uv_layers.new(name='UVMap')
umin,umax=8/2048,2040/2048;vmin,vmax=8/2048,1016/2048
for p in mesh.polygons:
    normal=p.normal
    for li in p.loop_indices:
        v=mesh.vertices[mesh.loops[li].vertex_index].co
        layer=(v.z-v.y-.12)/half
        if abs(normal.x)>.9:
            u=umin+(umax-umin)*(.35+layer*.30/4);t=(v.y+.25)/2.5
        elif abs(normal.y)>.9:
            u=umin+(umax-umin)*v.x/4;t=(.02 if v.y<0 else .87)+layer*.30/(2.5*math.sqrt(2))
        else:
            # Same fibre direction/density; two repeats packed in the atlas.
            u=umin+(umax-umin)*v.x/4;t=(v.y+.25)/2.5
        uv.data[li].uv=(u,vmin+(vmax-vmin)*t)
    p.use_smooth=False
thatch.data=mesh;thatch.matrix_world.identity()

# Use each beam's own length and transverse axes, not world Y/Z projections.
wood_report=[]
for o in parts[1:-1]:
    m=o.data;vs=[v.co for v in m.vertices]
    a=sum(vs[:4],Vector())/4;b=sum(vs[4:],Vector())/4;axis=(b-a).normalized()
    layer=m.uv_layers.active
    for p in m.polygons:
        cap=abs(p.normal.dot(axis))>.9
        edges=[(vs[p.vertices[(j+1)%len(p.vertices)]]-vs[p.vertices[j]]) for j in range(len(p.vertices))]
        transverse=min(edges,key=lambda e:abs(e.normalized().dot(axis))).normalized() if not cap else edges[0].normalized()
        if cap:
            uaxis=edges[0].normalized();vaxis=p.normal.cross(uaxis).normalized()
            coords=[(vs[i].dot(uaxis),vs[i].dot(vaxis)) for i in p.vertices];rect=(.01,.64,.12,.75)
        else:
            coords=[(vs[i].dot(axis),vs[i].dot(transverse)) for i in p.vertices];rect=(.01,.88,.485,.925)
        lo=[min(c[j] for c in coords) for j in (0,1)];hi=[max(c[j] for c in coords) for j in (0,1)]
        if min(hi[j]-lo[j] for j in (0,1))<1e-6:raise RuntimeError('Collapsed beam face '+o.name)
        for li,c in zip(p.loop_indices,coords):
            layer.data[li].uv=(rect[0]+(rect[2]-rect[0])*(c[0]-lo[0])/(hi[0]-lo[0]),rect[1]+(rect[3]-rect[1])*(c[1]-lo[1])/(hi[1]-lo[1]))
    wood_report.append({'object':o.name,'faces':len(m.polygons),'all_faces_unwrapped':True})

edges={tuple(sorted(e.vertices)):0 for e in mesh.edges}
for p in mesh.polygons:
    ids=list(p.vertices)
    for i,j in zip(ids,ids[1:]+ids[:1]):edges[tuple(sorted((i,j)))]+=1
    coords=[uv.data[i].uv for i in p.loop_indices]
    area=abs(sum(coords[j].x*coords[(j+1)%len(coords)].y-coords[(j+1)%len(coords)].x*coords[j].y for j in range(len(coords))))/2
    if area<1e-6:raise RuntimeError('Collapsed thatch UV face')
assert len(mesh.vertices)==8 and len(mesh.polygons)==6 and all(n==2 for n in edges.values())
assert fingerprint(parts[-1])==wattle_before
assert {o.name:fingerprint(o) for o in bpy.data.objects if o.name in original}==original

for o in list(bpy.data.objects):
    if o.name.startswith('Roof_') and o.name.endswith('_Export'):bpy.data.objects.remove(o,do_unlink=True)
exports=bpy.data.collections.get('Thatch Roof — joined exports (hidden)')
if not exports:
    exports=bpy.data.collections.new('Thatch Roof — joined exports (hidden)');bpy.context.scene.collection.children.link(exports)
exports.hide_render=False;exports.hide_viewport=False
bpy.ops.object.select_all(action='DESELECT')
duplicates=[]
for p in parts:
    o=p.copy();o.data=p.data.copy();exports.objects.link(o);o.hide_set(False);o.select_set(True);duplicates.append(o)
bpy.context.view_layer.objects.active=duplicates[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='Roof_Thatch_Export'
joined.data.materials.clear();joined.data.materials.append(material)
for p in joined.data.polygons:p.material_index=0
# Native +X/+Y construction coordinates; Unity's importer bakes conversion.
file=OUT/'ThatchRoofPanel_4.0Wx2.0Hx2.0D.fbx'
bpy.ops.export_scene.fbx(filepath=str(file),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',global_scale=1,
 apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',
 use_tspace=False,add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
joined.data.calc_loop_triangles();triangles=len(joined.data.loop_triangles)
joined.hide_set(True);joined.hide_render=True;exports.hide_render=True
bpy.ops.object.select_all(action='DESELECT');thatch.select_set(True);bpy.context.view_layer.objects.active=thatch
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
report={'thatch_vertices':8,'thatch_faces':6,'thatch_triangles':12,'closed_manifold':True,'all_thatch_uv_faces_have_area':True,
 'total_triangles':triangles,'wood_uvs':wood_report,'wattle_unchanged':True,'non_roof_objects_unchanged':True,'export':str(file)}
(REPORT/'repair-report.json').write_text(json.dumps(report,indent=2));print('ROOF REPAIR PASS',json.dumps(report))
