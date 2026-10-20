"""Blender 5: simplify the detailed roof and bake normals/AO to unique UV1.
Keeps LayeredThatchRoof.blend untouched; writes BakedThatchRoof.blend + runtime FBX.
"""
import bpy, bmesh, json, math
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/ThatchRoof/Redesign'
OUT=ART/'Baked';OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ART/'LayeredThatchRoof.blend'))
reference=bpy.data.objects['Roof_Thatch_Export']
bpy.ops.object.select_all(action='DESELECT');reference.hide_set(False);reference.select_set(True);bpy.context.view_layer.objects.active=reference
bpy.ops.export_scene.fbx(filepath=str(OUT/'DetailedReference.fbx'),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',global_scale=1,
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',
    use_tspace=False,add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
source=bpy.data.objects['Roof_Single_Thatch']
parts=[bpy.data.objects['Roof_Single_'+n] for n in ('EaveRail','Rafter_0','Rafter_1','Rafter_2','WovenSubstrate')]
for obj in list(bpy.data.objects):
    if obj not in [source]+parts:bpy.data.objects.remove(obj,do_unlink=True)
for obj in [source]+parts:
    obj.hide_set(False);obj.hide_render=False

# Separate the authored components by adjacency. The source generator leaves
# each closed bundle/stalk and each open fringe strip as its own component.
me=source.data
adj=[[] for _ in me.vertices]
for e in me.edges:
    a,b=e.vertices;adj[a].append(b);adj[b].append(a)
components=[];seen=set()
for i in range(len(adj)):
    if i in seen:continue
    stack=[i];seen.add(i);group=[]
    while stack:
        a=stack.pop();group.append(a)
        for b in adj[a]:
            if b not in seen:seen.add(b);stack.append(b)
    components.append(sorted(group))
assert {n:sum(len(c)==n for c in components) for n in (8,19,10,6)}=={8:1,19:64,10:64,6:192}
polys={}
for p in me.polygons:polys.setdefault(min(p.vertices),[]).append(p)

def subset(name,groups):
    ids=set(i for c in groups for i in c);ordered=sorted(ids);remap={v:i for i,v in enumerate(ordered)}
    faces=[p for p in me.polygons if p.vertices[0] in ids]
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([me.vertices[i].co for i in ordered],[],[[remap[i] for i in p.vertices] for p in faces]);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for p,old in zip(mesh.polygons,faces):
        p.use_smooth=old.use_smooth
        for li,oi in zip(p.loop_indices,old.loop_indices):uv.data[li].uv=me.uv_layers[0].data[oi].uv
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    return obj

opaque_groups=[c for c in components if len(c) in (8,19)]
low=subset('Roof_Baked_Opaque',opaque_groups)
high=subset('Roof_Bake_Source',[c for c in components if len(c)!=10])
bpy.ops.object.select_all(action='DESELECT');low.select_set(True);bpy.context.view_layer.objects.active=low
dec=low.modifiers.new('Retain course silhouette, simplify crowns','DECIMATE');dec.ratio=.40;dec.use_collapse_triangulate=True
bpy.ops.object.modifier_apply(modifier=dec.name)
for vertex in low.data.vertices:
    vertex.co.x=max(0,min(4,vertex.co.x));vertex.co.y=min(2,vertex.co.y)
low.data.update()
# Freeze final triangles before tangent-space baking.
tri=low.modifiers.new('Bake/export triangulation','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
low.data.uv_layers.new(name='BakeDetail')
low.data.uv_layers.active_index=1
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(68),island_margin=.015)
bpy.ops.object.mode_set(mode='OBJECT')
for uv in low.data.uv_layers[1].data:uv.uv=uv.uv*.94+Vector((.03,.03))

# Rebuild each five-column fringe strip with three columns. The central crown
# and exact perimeter endpoints survive; only intermediate bend vertices go.
v=[];f=[];uvs=[]
for comp in [c for c in components if len(c)==10]:
    ids=[comp[i] for i in (0,2,4,5,7,9)]
    start=len(v);v.extend(me.vertices[i].co.copy() for i in ids)
    lookup={me.loops[li].vertex_index:me.uv_layers[0].data[li].uv.copy() for p in me.polygons if p.vertices[0] in comp for li in p.loop_indices}
    f.extend([(start,start+1,start+4,start+3),(start+1,start+2,start+5,start+4)])
    uvs.extend([[lookup[i] for i in [ids[0],ids[1],ids[4],ids[3]]],[lookup[i] for i in [ids[1],ids[2],ids[5],ids[4]]]])
fringe_mesh=bpy.data.meshes.new('Simplified lifted fringe')
fringe_mesh.from_pydata(v,[],f);fringe_mesh.update();uv=fringe_mesh.uv_layers.new(name='UVMap');detail=fringe_mesh.uv_layers.new(name='BakeDetail')
for p,coords in zip(fringe_mesh.polygons,uvs):
    for li,c in zip(p.loop_indices,coords):uv.data[li].uv=c;detail.data[li].uv=(.01,.01)
fringe=bpy.data.objects.new('Roof_Baked_Fringe',fringe_mesh);bpy.context.collection.objects.link(fringe)

scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.render.bake.use_selected_to_active=True
scene.render.bake.cage_extrusion=.13;scene.render.bake.max_ray_distance=.28
scene.render.bake.margin=8
scene.render.bake.normal_space='OBJECT'
for obj in parts+[source,fringe]:obj.hide_render=True
normal=bpy.data.images.new('Roof object normal bake',width=1024,height=1024,alpha=False)
normal.colorspace_settings.name='Non-Color';normal.generated_color=(.5,.5,1,1)
ao=bpy.data.images.new('Roof short-range occlusion bake',width=1024,height=1024,alpha=False)
ao.colorspace_settings.name='Non-Color';ao.generated_color=(1,1,1,1)
target_mat=bpy.data.materials.new('Roof bake target');target_mat.use_nodes=True
target=target_mat.node_tree.nodes.new('ShaderNodeTexImage')
low.data.materials.clear();low.data.materials.append(target_mat)
high.data.materials.clear()
ao_mat=bpy.data.materials.new('Short-range local occlusion');ao_mat.use_nodes=True
nodes=ao_mat.node_tree.nodes;nodes.clear()
occlusion=nodes.new('ShaderNodeAmbientOcclusion');occlusion.inputs['Distance'].default_value=.18
emission=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial')
ao_mat.node_tree.links.new(occlusion.outputs['Color'],emission.inputs['Color']);ao_mat.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
high.data.materials.append(ao_mat)
bpy.ops.object.select_all(action='DESELECT');high.select_set(True);low.select_set(True);bpy.context.view_layer.objects.active=low
target.image=normal;target_mat.node_tree.nodes.active=target
bpy.ops.object.bake(type='NORMAL',use_clear=True)
normal.filepath_raw=str(OUT/'normal-bake.png');normal.file_format='PNG';normal.save()
target.image=ao
bpy.ops.object.bake(type='EMIT',use_clear=True)
ao.filepath_raw=str(OUT/'ao-bake.png');ao.file_format='PNG';ao.save()

# Pack an octahedral object normal + AO in one linear RGB texture. Flat corners are sampled
# by the reused timber, underside substrate and fringe; they remain unperturbed.
n=np.asarray(normal.pixels[:],dtype=np.float32).reshape(1024,1024,4)
a=np.asarray(ao.pixels[:],dtype=np.float32).reshape(1024,1024,4)
direction=(n[:,:,[0,2,1]]*2-1)
direction/=np.maximum(np.abs(direction).sum(axis=2,keepdims=True),1e-6)
octa=direction[:,:,:2].copy()
fold=(1-np.abs(octa[:,:,::-1]))*np.where(octa>=0,1,-1)
octa=np.where(direction[:,:,2:3]<0,fold,octa)
packed=np.ones_like(n);packed[:,:,0:2]=octa*.5+.5;packed[:,:,2]=a[:,:,0]
packed[:24,:,:]=(.5,.5,1,1);packed[-24:,:,:]=(.5,.5,1,1)
packed[:,:24,:]=(.5,.5,1,1);packed[:,-24:,:]=(.5,.5,1,1)
detail_image=bpy.data.images.new('Thatch baked normal XY and AO',width=1024,height=1024,alpha=True)
detail_image.colorspace_settings.name='Non-Color';detail_image.pixels.foreach_set(packed.ravel())
detail_image.filepath_raw=str(ROOT/'Assets/Textures/Buildings/Wood/thatch-roof-detail.png');detail_image.file_format='PNG';detail_image.save()
assert np.isfinite(packed).all()
assert float(np.std(packed[32:-32,32:-32,:2]))>.01,'Normal bake is flat'

# Blender review material uses the same UV1 bake. Keep the source atlas UV0.
material=source.data.materials[0].copy();material.name='Baked thatch review'
nodes=material.node_tree.nodes;links=material.node_tree.links
bsdf=next(node for node in nodes if node.type=='BSDF_PRINCIPLED')
base=next(link.from_socket for link in links if link.to_socket==bsdf.inputs['Base Color'])
coords=nodes.new('ShaderNodeUVMap');coords.uv_map='BakeDetail'
nt=nodes.new('ShaderNodeTexImage');nt.image=normal;links.new(coords.outputs['UV'],nt.inputs['Vector'])
nm=nodes.new('ShaderNodeNormalMap');nm.space='OBJECT';links.new(nt.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],bsdf.inputs['Normal'])
at=nodes.new('ShaderNodeTexImage');at.image=ao;links.new(coords.outputs['UV'],at.inputs['Vector'])
mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.55
links.new(base,mix.inputs[1]);links.new(at.outputs['Color'],mix.inputs[2]);links.new(mix.outputs[0],bsdf.inputs['Base Color'])
for obj in [low,fringe]+parts:
    if len(obj.data.uv_layers)<2:
        layer=obj.data.uv_layers.new(name='BakeDetail')
        for d in layer.data:d.uv=(.01,.01)
    obj.data.uv_layers.active_index=0
    obj.data.materials.clear();obj.data.materials.append(material)
    obj.hide_render=False
# The normal/AO corner is explicitly flat/white in the separate review images too.
for image,colour in [(normal,(.5,.5,1,1)),(ao,(1,1,1,1))]:
    pixels=np.asarray(image.pixels[:],dtype=np.float32).reshape(1024,1024,4)
    pixels[:24,:]=colour;pixels[-24:,:]=colour;pixels[:,:24]=colour;pixels[:,-24:]=colour
    image.pixels.foreach_set(pixels.ravel());image.save()
source.hide_render=True;source.hide_set(True);high.hide_render=True;high.hide_set(True)
bpy.ops.object.select_all(action='DESELECT')
copies=[]
for obj in [low,fringe]+parts:
    cp=obj.copy();cp.data=obj.data.copy();bpy.context.collection.objects.link(cp);cp.select_set(True);copies.append(cp)
bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name='Roof_Baked_Export'
joined.data.materials.clear();joined.data.materials.append(material)
for p in joined.data.polygons:p.material_index=0
joined.data.calc_loop_triangles()
for tri in joined.data.loop_triangles:
    a,b,c=[joined.data.vertices[i].co for i in tri.vertices]
    assert (b-a).cross(c-a).length_squared>1e-12
for layer in joined.data.uv_layers:
    assert all(0<=c<=1 for uv in layer.data for c in uv.uv)
fbx=ROOT/'Assets/Models/Buildings/Wood/Roofs/ThatchRoofPanel_4.0Wx2.0Hx2.0D.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',global_scale=1,
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',
    use_tspace=False,add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
report=dict(source_triangles=4286,triangles=len(joined.data.loop_triangles),removed_stalks=192,fringe_cards=64,fringe_quads_per_card=2,
    bake_size=[1024,1024],bake_channels='RG=octahedral Unity object normal, B=short-range AO',
    normal_xy_std=float(np.std(packed[32:-32,32:-32,:2])),ao_min=float(packed[:,:,2].min()),ao_mean=float(packed[:,:,2].mean()))
report['triangle_reduction_percent']=round(100*(1-report['triangles']/4286),1)
(OUT/'bake-report.json').write_text(json.dumps(report,indent=2))
joined.hide_render=True;joined.hide_set(True)
bpy.ops.object.select_all(action='DESELECT');low.select_set(True);bpy.context.view_layer.objects.active=low
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'BakedThatchRoof.blend'))
print('THATCH BAKE PASS',json.dumps(report))
