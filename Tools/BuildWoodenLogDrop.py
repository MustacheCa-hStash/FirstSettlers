"""Build the simple ordinary-wood item model with Blender 5.0.

Run: blender --background --python Tools/BuildWoodenLogDrop.py
Uses the existing meadow-timber atlas unchanged. No scene/gameplay edits.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/ResourceDrops/WoodenLog'
OUT = ROOT / 'Assets/Models/Resources'
for folder in (ART, OUT):
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
regions = json.loads((ROOT / 'ArtReferences/IronAgeWood/Textures/atlas-layout.json').read_text())['regions']
texture = ROOT / 'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png'
mat = bpy.data.materials.new('WoodenLog_MeadowTimber')
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = 1
bsdf.inputs['Specular IOR Level'].default_value = 0
tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(str(texture))
mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])

# Horizontal X axis, one mildly irregular twelve-sided section. A narrow
# bark rim separates the painted cut face from each bevelled outer edge.
N = 12
rings = [(-.38,.87),(-.38,1.0),(-.35,1.035),(.01,1.015),(.35,.94),(.38,.92),(.38,.80)]
verts, faces, uv_faces, tags = [], [], [], []
for k,(x,scale) in enumerate(rings):
    for j in range(N):
        a = 2*math.pi*j/N + math.pi/12
        irregular = 1 + .035*math.sin(a*3+.8) + .018*math.cos(a*5)
        r = .155*scale*irregular
        verts.append((x, math.cos(a)*r + .006*math.sin(k*.55), math.sin(a)*r*.94))
base_z = min(v[2] for v in verts)
verts = [(x,y,z-base_z) for x,y,z in verts]

def add(face, uvs, tag):
    faces.append(face)
    uv_faces.append(uvs)
    tags.append(tag)

d = regions['D']['uv_texel_centres']
for k in range(len(rings)-1):
    for j in range(N):
        jj = (j+1)%N
        face = (k*N+j,k*N+jj,(k+1)*N+jj,(k+1)*N+j)
        if k in (0,5):
            # Quiet dark end of the existing bark strip for the bark collar.
            uvs = [(u,d[1]+.025+(v%N)/N*.053) for u,v in [( .976,j),(.976,j+1),(.986,j+1),(.986,j)]]
        else:
            # A short crop stretches the broad painted plates lengthwise,
            # avoiding a busy succession of narrow transverse bark marks.
            u0 = .10 + (rings[k][0]+.38)/.76*.32
            u1 = .10 + (rings[k+1][0]+.38)/.76*.32
            v0 = d[1]+.004+j/N*(d[3]-d[1]-.008)
            v1 = d[1]+.004+(j+1)/N*(d[3]-d[1]-.008)
            uvs = [(u0,v0),(u0,v1),(u1,v1),(u1,v0)]
        add(face,uvs,'D')
for k,tag,reverse in [(0,'E',True),(6,'F',False)]:
    bounds = regions[tag]['uv_texel_centres']
    center_uv = ((bounds[0]+bounds[2])/2,(bounds[1]+bounds[3])/2)
    center = len(verts)
    verts.append((rings[k][0], .006*math.sin(k*.55), -base_z))
    def cap_uv(j):
        a=2*math.pi*j/N+math.pi/12
        return (center_uv[0]+math.cos(a)*(bounds[2]-bounds[0])*.48,
                center_uv[1]+math.sin(a)*(bounds[3]-bounds[1])*.48)
    for j in range(N):
        jj=(j+1)%N
        face=(center,k*N+jj,k*N+j) if reverse else (center,k*N+j,k*N+jj)
        uvs=(center_uv,cap_uv(jj),cap_uv(j)) if reverse else (center_uv,cap_uv(j),cap_uv(jj))
        add(face,uvs,tag)

mesh = bpy.data.meshes.new('WoodenLogDrop_Mesh')
mesh.from_pydata(verts, [], faces)
mesh.update()
obj = bpy.data.objects.new('WoodenLogDrop',mesh)
scene.collection.objects.link(obj)
mesh.materials.append(mat)
uv = mesh.uv_layers.new(name='UVMap')
for poly,points,tag in zip(mesh.polygons,uv_faces,tags):
    poly.use_smooth = tag=='D' and poly.index//N not in (0,5)
    for idx,point in zip(poly.loop_indices,points):
        uv.data[idx].uv = point
obj['resource'] = 'Ordinary wood'
obj['pivot'] = 'Ground center; Blender Z up / Unity Y up'
obj['style'] = 'Matte painted meadow timber; simple silhouette; shared existing atlas'
obj['nominal_length_m'] = .76
bpy.context.view_layer.objects.active=obj
obj.select_set(True)

# Geometry and atlas checks, before exporting any deliverable.
bm=bmesh.new()
bm.from_mesh(mesh)
assert all(e.is_manifold for e in bm.edges), 'Mesh must be closed and manifold'
assert len(bm.verts)-len(bm.edges)+len(bm.faces)==2
assert bm.calc_volume(signed=True)>0, 'Winding must face outward'
bm.free()
for points,tag in zip(uv_faces,tags):
    u0,v0,u1,v1=regions[tag]['uv_texel_centres']
    assert all(u0<=u<=u1 and v0<=v<=v1 for u,v in points), 'UV outside padded content'
mesh.calc_loop_triangles()
triangles=len(mesh.loop_triangles)
assert triangles==168
fbx = OUT/'WoodenLogDrop.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},
    axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,
    path_mode='STRIP',bake_anim=False)
bpy.ops.export_scene.gltf(filepath=str(ART/'WoodenLogDrop.glb'),export_format='GLB',
    use_selection=True,export_yup=True,export_materials='EXPORT',export_texcoords=True)

# Round trip the FBX in an independent collection, then remove only the import.
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(fbx))
imported=list(set(bpy.data.objects)-before)
export_meshes=[o for o in imported if o.type=='MESH']
assert len(export_meshes)==1
check=export_meshes[0]
check.data.calc_loop_triangles()
assert len(check.data.loop_triangles)==triangles
assert len(check.data.materials)==1
original_bounds=[max(v.co[i] for v in mesh.vertices)-min(v.co[i] for v in mesh.vertices) for i in range(3)]
points=[check.matrix_world@v.co for v in check.data.vertices]
roundtrip_bounds=[max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]
assert all(abs(a-b)<1e-5 for a,b in zip(original_bounds,roundtrip_bounds)), 'FBX metre scale mismatch'
for o in imported:
    bpy.data.objects.remove(o,do_unlink=True)
report={'triangles':triangles,'vertices':len(mesh.vertices),'materials':1,
    'bounds_blender_xyz_m':original_bounds,'bounds_unity_xyz_m':[original_bounds[0],original_bounds[2],original_bounds[1]],
    'atlas_regions':['D: bark','E: left cut','F: right cut'],
    'texture':str(texture.relative_to(ROOT)).replace('\\','/'),
    'checks':{'closed_manifold':True,'outward_winding':True,'padded_atlas_UVs':True,
              'FBX_roundtrip_triangles':True,'FBX_roundtrip_metres':True},
    'unity_runtime_validation':False}
(ART/'model-report.json').write_text(json.dumps(report,indent=2)+'\n')

# All following objects belong to the editable preview, never the exports.
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1200
scene.render.resolution_y=850
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.world=bpy.data.worlds.new('Soft daylight')
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.73,.8,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
scene.view_settings.view_transform='Standard'
scene.view_settings.look='None'

def flat_mat(name,color):
    m=bpy.data.materials.new(name)
    m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=1
    p.inputs['Specular IOR Level'].default_value=0
    return m

bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.002))
ground=bpy.context.object
ground.name='PREVIEW_Ground'
ground.data.materials.append(flat_mat('Warm neutral studio',(.21,.235,.22)))
target=Vector((0,0,.145))
for name,location,power,size in [('Key',(-1.2,-1.5,2.5),110,2),('Fill',(.8,1.5,1.6),45,2)]:
    bpy.ops.object.light_add(type='AREA',location=location)
    light=bpy.context.object
    light.name='PREVIEW_'+name
    light.data.energy=power
    light.data.shape='DISK'
    light.data.size=size
    light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(-1.25,-1.6,1.05))
cam=bpy.context.object
cam.name='PREVIEW_Camera'
cam.data.type='ORTHO'
cam.data.ortho_scale=1.12
scene.camera=cam
def render(name,location,scale=1.12):
    cam.location=location
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale
    scene.render.filepath=str(ART/name)
    bpy.ops.render.render(write_still=True)
render('wooden-log-preview.png',(-1.25,-1.6,1.05))
render('wooden-log-reverse.png',(1.35,-1.5,.85))
render('wooden-log-side.png',(0,-2,.75))
ground.hide_render=True
scene.render.film_transparent=True
scene.render.resolution_x=512
scene.render.resolution_y=512
render('wooden-log-icon.png',(-1.25,-1.6,1.05),.96)
ground.hide_render=False
scene.render.film_transparent=False
scene.render.resolution_x=1200
scene.render.resolution_y=850
scene.render.filepath=str(ART/'wooden-log-preview.png')
cam.data.ortho_scale=1.12
tex.image.pack()
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active=obj
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'WoodenLogDrop.blend'))
print('WOODEN_LOG_VALIDATED '+json.dumps(report))
