"""Author W09/W11/W15 in the saved Blender source; preserve every existing mesh."""
import bpy,bmesh,json,sys,hashlib,math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'Tools'))
from BuildingTrimUV import sample
OUT=ROOT/'Assets/Models/Buildings/Wood/Modular';OUT.mkdir(parents=True,exist_ok=True)
REPORT=ROOT/'ArtReferences/OpeningWallKit';REPORT.mkdir(parents=True,exist_ok=True)
def signature(o):
    return hashlib.sha256(json.dumps({'matrix':[list(r) for r in o.matrix_world], 'vertices':[list(v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons], 'uv':[[list(t.uv) for t in layer.data] for layer in o.data.uv_layers],
        'materials':[m.name if m else None for m in o.data.materials]},sort_keys=True).encode()).hexdigest()
before={o.name:signature(o) for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('OpeningKit_')}
for c in list(bpy.data.collections):
    if c.name.startswith('Opening wall kit'):
        for o in list(c.objects):bpy.data.objects.remove(o,do_unlink=True)
        bpy.data.collections.remove(c)
editable=bpy.data.collections.new('Opening wall kit — editable');bpy.context.scene.collection.children.link(editable)
exports=bpy.data.collections.new('Opening wall kit — exports (hidden)');bpy.context.scene.collection.children.link(exports)
wood=bpy.data.materials['Modular_Wood_Matte']
manifest={'models':[],'preservedExistingObjects':len(before),'atlas':'ordinary-wood-trim-albedo.png'}

def clip(poly,a,b,c):
    """Keep a*x+b*y<=c. Convex polygon remains convex."""
    result=[]
    for p,q in zip(poly,poly[1:]+poly[:1]):
        dp=a*p[0]+b*p[1]-c;dq=a*q[0]+b*q[1]-c
        if dp<=1e-8:result.append(p)
        if (dp<=0)!=(dq<=0):
            t=dp/(dp-dq);result.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
    clean=[]
    for p in result:
        if not clean or sum((p[i]-clean[-1][i])**2 for i in (0,1))>1e-12:clean.append(p)
    if len(clean)>1 and sum((clean[0][i]-clean[-1][i])**2 for i in (0,1))<1e-12:clean.pop()
    return clean
def rect(x0,y0,x1,y1):return [(x0,y0),(x1,y0),(x1,y1),(x0,y1)]
def subtract_rect(box,hole):
    x0,y0,x1,y1=box;a,b,c,d=hole
    ix0=max(x0,a);iy0=max(y0,b);ix1=min(x1,c);iy1=min(y1,d)
    if ix1<=ix0 or iy1<=iy0:return [box]
    return [v for v in [(x0,y0,ix0,y1),(ix1,y0,x1,y1),(ix0,y0,ix1,iy0),(ix0,iy1,ix1,y1)] if v[2]-v[0]>1e-6 and v[3]-v[1]>1e-6]
def prism(name,poly,z0,z1,grain,seed,diagonal=False):
    if len(poly)<3:return None
    n=len(poly);verts=[(x,z,y) for z in (z0,z1) for x,y in poly]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    mesh.materials.append(wood);uv=mesh.uv_layers.new(name='UVMap')
    def projection(v):
        p=(v.x,v.z,v.y)
        return ((p[0]+p[1])/math.sqrt(2),(-p[0]+p[1])/math.sqrt(2),p[2]) if diagonal else p
    points=[projection(v.co) for v in mesh.vertices];lo=[min(p[a] for p in points) for a in range(3)];hi=[max(p[a] for p in points) for a in range(3)]
    for face in mesh.polygons:
        normal=projection(face.normal)
        if not diagonal and grain==1 and abs(normal[1])>.6:normal=(0,1,0)
        for li in face.loop_indices:uv.data[li].uv=sample(points[mesh.loops[li].vertex_index],normal,lo,hi,grain,seed)
    obj=bpy.data.objects.new('OpeningKit_'+name,mesh);editable.objects.link(obj);obj['uv_seed']=seed;obj['grain_axis']=grain
    return obj
def opening_wall(tag,hole):
    x0,y0,x1,y1=hole;w=.125;framed=(x0-w,max(0,y0-w),x1+w,y1+w);objects=[]
    for i in range(16):
        a=0 if i==0 else i*.25+.006;b=4 if i==15 else (i+1)*.25-.006
        for j,r in enumerate(subtract_rect((a,0,b,3),framed)):
            objects.append(prism(tag+'_Board_'+str(i)+'_'+str(j),rect(*r),-.125,.025,1,3000+i*13+j))
    for i,y in enumerate((.25,1.375,2.625)):
        for j,r in enumerate(subtract_rect((0,y,4,y+.125),framed)):
            objects.append(prism(tag+'_Rail_'+str(i)+'_'+str(j),rect(*r),.025,.125,0,4000+i*13+j))
    frames=[(x0-w,max(0,y0-w),x0,y1+w),(x1,max(0,y0-w),x1+w,y1+w),(x0,y1,x1,y1+w)]
    if y0>0:frames.append((x0,y0-w,x1,y0))
    for i,r in enumerate(frames):objects.append(prism(tag+'_Frame_'+str(i),rect(*r),-.125,.125,1 if i<2 else 0,5000+i))
    return objects
def gable():
    objects=[]
    for i in range(8):
        a=i*.25+.006;b=(i+1)*.25-.006
        p=clip(rect(a,.125,min(b,1.875),2),-1,1,-.125*math.sqrt(2))
        o=prism('W15_Rising_Board_'+str(i),p,-.125,.025,1,6000+i)
        if o:objects.append(o)
    bottom=clip(rect(0,0,2,.125),-1,1,0)
    diagonal=clip(clip(rect(0,0,2,2),-1,1,0),1,-1,.125*math.sqrt(2))
    peak=clip(rect(1.875,.125,2,2),-1,1,0)
    # The diagonal band owns its intersection with the base/peak framing.
    bottom=clip(bottom,-1,1,-.125*math.sqrt(2))
    peak=clip(peak,-1,1,-.125*math.sqrt(2))
    objects.append(prism('W15_Rising_SlopeRail',diagonal,-.125,.125,0,7000,True))
    if len(bottom)>2:objects.append(prism('W15_Rising_BaseRail',bottom,-.125,.125,0,7001))
    if len(peak)>2:objects.append(prism('W15_Rising_PeakRail',peak,-.125,.125,1,7002))
    return objects
def mirror(objects):
    result=[]
    for src in objects:
        obj=src.copy();obj.data=src.data.copy();obj.name=src.name.replace('Rising','Falling');editable.objects.link(obj)
        for v in obj.data.vertices:v.co.x=2-v.co.x
        bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(obj.data);bm.free();obj.data.update();result.append(obj)
    return result
def inspect(objects):
    triangles=0
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles();triangles+=len(mesh.loop_triangles)
        edges={tuple(sorted(e.vertices)):0 for e in mesh.edges}
        for p in mesh.polygons:
            for a,b in zip(list(p.vertices),list(p.vertices)[1:]+list(p.vertices)[:1]):edges[tuple(sorted((a,b)))]+=1
        assert all(n==2 for n in edges.values()),obj.name
        for t in mesh.loop_triangles:
            a,b,c=[mesh.uv_layers.active.data[i].uv for i in t.loops]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(obj.name,t.index)
            for uv in (a,b,c):assert 0<=uv.x<=1 and 0<=uv.y<=1,(obj.name,list(uv))
    return triangles
def export(objects,tag,file):
    triangles=inspect(objects);bpy.ops.object.select_all(action='DESELECT');copies=[]
    for src in objects:
        obj=src.copy();obj.data=src.data.copy();obj.location=(0,0,0);exports.objects.link(obj);obj.select_set(True);copies.append(obj)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='OpeningKit_'+tag+'_Export'
    bpy.ops.export_scene.fbx(filepath=str(OUT/file),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
    joined.hide_set(True);joined.hide_render=True
    manifest['models'].append({'tag':tag,'file':file,'parts':len(objects),'triangles':triangles,'closedComponents':True,'allFaceUVsValid':True})
door=opening_wall('W09',(1.375,0,2.625,2.25));window=opening_wall('W11',(1.5,1.25,2.5,2));rising=gable();falling=mirror(rising)
seam_rise=[prism('W15_Rising_RoofSeam',[(0,0),(2,2),(2,2.125),(0,.125)],-.125,.125,0,7200,True)]
seam_fall=mirror(seam_rise)
for objects,tag,file,x in [(door,'W09','W09_DoorwayWall_4x3x0.25.fbx',0),(window,'W11','W11_ShutterWall_4x3x0.25.fbx',6),
    (rising,'W15_Rising','W15_GableRising_2x2x0.25.fbx',12),(falling,'W15_Falling','W15_GableFalling_2x2x0.25.fbx',16)]:
    export(objects,tag,file)
    for o in objects:o.location=(x,30,0)
for objects,tag,file,x in [(seam_rise,'W15_Rising_Seam','W15_GableRising_RoofSeam.fbx',12),(seam_fall,'W15_Falling_Seam','W15_GableFalling_RoofSeam.fbx',16)]:
    export(objects,tag,file)
    for o in objects:o.location=(x,34,0)
assert {o.name:signature(o) for o in bpy.data.objects if o.name in before}==before
exports.hide_render=True;bpy.ops.object.select_all(action='DESELECT')
for o in door:o.select_set(True)
bpy.context.view_layer.objects.active=door[0]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':area.spaces.active.region_3d.view_location=Vector((9,30,1.5));area.spaces.active.region_3d.view_distance=20
(REPORT/'blender-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('OPENING WALL BLENDER PASS',json.dumps(manifest))
