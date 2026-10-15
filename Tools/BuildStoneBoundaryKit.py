"""Blender S07/S12 boundary extension. Leaves earlier stone/wood sources and exports intact.

Blender --background --factory-startup --python Tools/BuildStoneBoundaryKit.py
"""
import bpy
import bmesh
import json
import random
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/StoneBuildKit/Boundary'
OUT=ROOT/'Assets/Models/Buildings/Stone'
for path in (ART,OUT):path.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
editable=bpy.data.collections.new('Boundary walls — editable stones')
exports=bpy.data.collections.new('Boundary walls — exports and UV variants')
bpy.context.scene.collection.children.link(editable);bpy.context.scene.collection.children.link(exports)
image=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png'));image.pack()
material=bpy.data.materials.new('Ordinary stone — painted matte UV0');material.use_nodes=True
shader=material.node_tree.nodes.get('Principled BSDF');shader.inputs['Roughness'].default_value=.95;shader.inputs['Specular IOR Level'].default_value=.08
tex=material.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
uv_node=material.node_tree.nodes.new('ShaderNodeUVMap');uv_node.uv_map='UVMap'
material.node_tree.links.new(uv_node.outputs['UV'],tex.inputs['Vector']);material.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])

def map_uv(obj,seed,variant=0):
    mesh=obj.data;uv=mesh.uv_layers.active or mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        rng=random.Random(seed*10007+face.index*193+variant*104729)
        dominant=max(range(3),key=lambda a:abs(face.normal[a]));a,b=[axis for axis in range(3) if axis!=dominant]
        if rng.random()>.5:a,b=b,a
        points=[mesh.vertices[i].co for i in face.vertices]
        lo=[min(p[axis] for p in points) for axis in (a,b)];hi=[max(p[axis] for p in points) for axis in (a,b)]
        scale=min(.24+rng.random()*.12,.82/max(hi[0]-lo[0],hi[1]-lo[1],.001))
        spans=[(hi[i]-lo[i])*scale for i in (0,1)]
        offset=[.035+rng.random()*(.93-span) for span in spans];flip=[rng.random()>.5,rng.random()>.5]
        for li in face.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co
            values=[p[axis]-lo[i] for i,axis in enumerate((a,b))]
            uv.data[li].uv=[offset[i]+((hi[i]-lo[i]-values[i]) if flip[i] else values[i])*scale for i in (0,1)]
    obj['uv_seed']=seed;obj['uv_layout']=variant

def prism(name,poly,z0,z1,seed):
    n=len(poly);vertices=[(x,z,y) for z in (z0,z1) for x,y in poly]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free();mesh.update()
    mesh.materials.append(material);obj=bpy.data.objects.new(name,mesh);editable.objects.link(obj);map_uv(obj,seed);return obj

def section(tag,x0,x1,seed):
    rng=random.Random(seed);parts=[]
    # Core ends below the stone tops and inside the outside faces; no coplanar packing faces.
    parts.append(prism(tag+'_Packing',[(x0+.006,.006),(x1-.006,.006),(x1-.006,1.244),(x0+.006,1.244)],-.215,.215,seed))
    heights=[0]+[i*.3125+rng.uniform(-.025,.025) for i in range(1,4)]+[1.25]
    for row in range(4):
        count=7+(row%2) if x1-x0>3 else 3 if row%2==0 else 2
        widths=[rng.uniform(.45,.75) for _ in range(count)];total=sum(widths);edges=[x0]
        for width in widths:edges.append(edges[-1]+width*(x1-x0)/total)
        edges[-1]=x1
        for col in range(count):
            a=edges[col]+(.005 if col else 0);b=edges[col+1]-(.005 if col<count-1 else 0)
            c=heights[row]+(.004 if row else 0);d=heights[row+1]-(.004 if row<3 else 0)
            cuts=[rng.uniform(.018,.050) for _ in range(8)]
            poly=[(a+cuts[0],c),(b-cuts[1],c),(b,c+cuts[2]),(b,d-cuts[3]),(b-cuts[4],d),(a+cuts[5],d),(a,d-cuts[6]),(a,c+cuts[7])]
            parts.append(prism(f'{tag}_Rubble_{row:02}_{col:02}',poly,-.25,.25,seed+row*101+col+1))
    return parts

def inspect(objects):
    result={'components':len(objects),'triangles':0,'faces':0,'uvTrianglesChecked':0,'allFaceUVsValid':True,'closedComponents':True}
    crops=set()
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles();uv=mesh.uv_layers.active
        bm=bmesh.new();bm.from_mesh(mesh);assert all(e.is_manifold for e in bm.edges),obj.name;assert bm.calc_volume(signed=True)>0,obj.name;bm.free()
        assert len(uv.data)==len(mesh.loops),obj.name
        for tri in mesh.loop_triangles:
            a,b,c=[uv.data[i].uv for i in tri.loops]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(obj.name,tri.index)
            assert all(.034<=p.x<=.966 and .034<=p.y<=.966 for p in (a,b,c)),obj.name
            result['uvTrianglesChecked']+=1
        result['triangles']+=len(mesh.loop_triangles);result['faces']+=len(mesh.polygons)
        crops.add(tuple(round(v,5) for li in mesh.polygons[0].loop_indices for v in uv.data[li].uv))
    result['distinctComponentCrops']=len(crops);assert len(crops)==len(objects)
    return result

def export(parts,tag,file,offset):
    report=inspect(parts);report['tag']=tag;report['files']=[]
    for variant in range(3):
        bpy.ops.object.select_all(action='DESELECT');copies=[]
        for src in parts:
            obj=src.copy();obj.data=src.data.copy();exports.objects.link(obj);map_uv(obj,int(src['uv_seed']),variant);obj.select_set(True);copies.append(obj)
        inspect(copies);bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object
        joined.name=tag+'_Export'+(f'_UV{variant}' if variant else '')
        name=file+(f'_UV{variant}' if variant else '')+'.fbx'
        bpy.ops.export_scene.fbx(filepath=str(OUT/name),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
        joined.hide_set(True);joined.hide_render=True;report['files'].append(name)
    for obj in parts:obj.location=offset
    return report

low=section('S07',0,4,70000)
gate=section('S12_LeftWing',0,1.375,120000)+section('S12_RightWing',2.625,4,121000)
models=[export(low,'S07','S07_LowRubbleWall_4x1.25x0.50',(0,0,0)),export(gate,'S12','S12_EmptyGateBay_4x1.25x0.50',(0,3,0))]
exports.hide_render=True
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.world.color=(.32,.32,.32)
camera_data=bpy.data.cameras.new('Boundary preview');camera=bpy.data.objects.new('Boundary preview',camera_data);scene.collection.objects.link(camera);scene.camera=camera
camera_data.type='ORTHO';camera_data.ortho_scale=7.2
camera.location=(8,-10,7);camera.rotation_euler=(Vector((2,1.5,.5))-camera.location).to_track_quat('-Z','Y').to_euler()
light_data=bpy.data.lights.new('Soft painted stone preview','AREA');light_data.energy=1600;light_data.size=8
light=bpy.data.objects.new('Soft painted stone preview',light_data);scene.collection.objects.link(light);light.location=(0,-4,7)
light.rotation_euler=(Vector((2,1.5,.5))-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ART/'S07-S12-overview.png');bpy.ops.render.render(write_still=True)
for tag,parts,other in [('S07',low,gate),('S12',gate,low)]:
    for obj in other:obj.hide_render=True
    centre=Vector((2,0 if tag=='S07' else 3,.625))
    for suffix,direction in [('front',Vector((6,-9,5))),('back-underside',Vector((-6,9,-4)))]:
        camera.location=centre+direction;camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.ortho_scale=5.6
        scene.render.filepath=str(ART/f'{tag}-{suffix}.png');bpy.ops.render.render(write_still=True)
    for obj in other:obj.hide_render=False
camera.location=(8,-10,7);camera.rotation_euler=(Vector((2,1.5,.5))-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.ortho_scale=7.2
bpy.ops.object.select_all(action='DESELECT')
for obj in gate:obj.select_set(True)
bpy.context.view_layer.objects.active=gate[0]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.type='MATERIAL';area.spaces.active.region_3d.view_location=Vector((2,1.5,.625));area.spaces.active.region_3d.view_distance=9
manifest={'models':models,'gateOpening':{'minX':1.375,'maxX':2.625,'clearWidth':1.25,'clearHeight':1.25,'noGateOrWoodenParts':True},'allFacesIncludingHidden':True}
(ART/'blender-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(ART/'StoneBoundaryKit.blend'))
print('STONE BOUNDARY BLENDER PASS',json.dumps(manifest))
