"""Run with Blender --background --python Tools/BuildRockPresets.py.
Editable metre-scale source, smooth export meshes and independent convex hulls.
The tile is painted procedurally, without photographic detail or baked lighting.
"""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, noise
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Models/Rocks/Presets'
ART = ROOT / 'ArtReferences/Rocks'
TEX = ROOT / 'Assets/Textures/Rock/Presets'
for p in (OUT, ART, TEX): p.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)

# Preserve the original periodic control tile: its alpha defines the approved snow pattern.
# PaintedStone_Albedo.png is separately authored; replacing albedo must not regenerate this mask.
size=512; pixels=[]
for y in range(size):
    v=2*math.pi*y/size
    for x in range(size):
        u=2*math.pi*x/size
        p=Vector((math.cos(u)*1.7, math.sin(u)*1.7+math.cos(v)*2.1, math.sin(v)*2.1))
        broad=noise.noise(p, noise_basis='PERLIN_ORIGINAL')
        mid=noise.noise(p*3.2, noise_basis='PERLIN_ORIGINAL')
        fine=noise.noise(p*15.0, noise_basis='PERLIN_ORIGINAL')
        vein=math.exp(-abs(mid+broad*.32)*30)*.065
        value=.40+broad*.085+mid*.055+fine*.022-vein
        pixels.extend((value*1.04, value*1.015, value*.975, .5+mid*.24+fine*.1))
img=bpy.data.images.new('WeatheredStone_ColorHeight', width=size,height=size,alpha=True)
img.pixels.foreach_set(pixels); img.filepath_raw=str(TEX/'WeatheredStone_ColorHeight.png');img.file_format='PNG';img.save()
mat=bpy.data.materials.new('Weathered stone');mat.use_nodes=True
nodes=mat.node_tree.nodes;links=mat.node_tree.links
bsdf=nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.94;bsdf.inputs['Specular IOR Level'].default_value=.12
painted_path=TEX/'PaintedStone_Albedo.png'
painted=bpy.data.images.load(str(painted_path),check_existing=True) if painted_path.exists() else img
painted.pack()
tex=nodes.new('ShaderNodeTexImage');tex.image=painted;tex.projection='BOX';tex.projection_blend=.28
coord=nodes.new('ShaderNodeTexCoord');links.new(coord.outputs['Generated'],tex.inputs['Vector']);links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=0;bump.inputs['Distance'].default_value=.08
links.new(tex.outputs['Alpha'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
presets=[('Pebble',(.65,.50,.36),3,41),('Fieldstone',(1.05,.76,.65),3,83),('FlatStone',(1.45,1.00,.47),3,126),
         ('RoundBoulder',(2.8,2.35,2.15),4,173),('SplitBoulder',(3.65,2.6,2.65),4,219),('LongBoulder',(4.2,2.25,1.8),4,267)]
report=[]
for i,(name,dims,sub,seed) in enumerate(presets):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1)
    obj=bpy.context.object;obj.name=name
    # Rounded block shapes, broad fractures and asymmetry retain a natural silhouette.
    for vert in obj.data.vertices:
        p=vert.co.copy(); offset=Vector((seed*.13,seed*.07,seed*.19))
        shape=1+noise.noise(p*1.9+offset)*.17+noise.noise(p*4.6+offset)*.035
        p=Vector(tuple(math.copysign(abs(t)**.83,t) for t in p))*shape
        p.x+=p.z*.13;p.y+=p.z*p.x*.08
        p.z=max(p.z,-.72) # Stable embedded base, origin on the ground.
        if name=='SplitBoulder':p.z-=max(0,.16-abs(p.x-.12))*.65
        vert.co=p
    if sub==4:
        dec=obj.modifiers.new('Silhouette budget','DECIMATE');dec.ratio=.60
        bpy.ops.object.modifier_apply(modifier=dec.name)
    for axis in range(3):
        lo=min(v.co[axis] for v in obj.data.vertices);hi=max(v.co[axis] for v in obj.data.vertices)
        for v in obj.data.vertices:v.co[axis]=(v.co[axis]-lo)/(hi-lo)*dims[axis]-(dims[axis]/2 if axis<2 else 0)
    for face in obj.data.polygons:face.use_smooth=True
    obj.data.materials.append(mat)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
    # Triplanar Unity materials need neither UV seams nor tangents. Keep the unwrap in the editable source.
    export=obj.copy();export.data=obj.data.copy();bpy.context.collection.objects.link(export);export.name=name+'_Export'
    for layer in list(export.data.uv_layers):export.data.uv_layers.remove(layer)
    bpy.ops.object.select_all(action='DESELECT');export.select_set(True);bpy.context.view_layer.objects.active=export
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False,mesh_smooth_type='OFF')
    lod=export.copy();lod.data=export.data.copy();bpy.context.collection.objects.link(lod);lod.name=name+'_LOD1'
    export.select_set(False);lod.select_set(True);bpy.context.view_layer.objects.active=lod
    dec=lod.modifiers.new('Distant silhouette budget','DECIMATE');dec.ratio=.35;bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'_LOD1.fbx')),use_selection=True,object_types={'MESH'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False,mesh_smooth_type='OFF')
    lod_triangles=sum(len(p.vertices)-2 for p in lod.data.polygons)
    bpy.data.objects.remove(export,do_unlink=True);bpy.data.objects.remove(lod,do_unlink=True)
    hull=obj.copy();hull.data=obj.data.copy();bpy.context.collection.objects.link(hull);hull.name=name+'_Hull'
    obj.select_set(False);hull.select_set(True);bpy.context.view_layer.objects.active=hull
    dec=hull.modifiers.new('Collision budget','DECIMATE');dec.ratio=min(1,70/len(hull.data.polygons));bpy.ops.object.modifier_apply(modifier=dec.name)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.convex_hull();bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'_Hull.fbx')),use_selection=True,object_types={'MESH'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False)
    report.append(dict(name=name,metres=list(dims),triangles=sum(len(p.vertices)-2 for p in obj.data.polygons),lodTriangles=lod_triangles,hullTriangles=sum(len(p.vertices)-2 for p in hull.data.polygons)))
    hull.hide_render=True;hull.hide_viewport=True;obj.location=(i%3*5,i//3*5,0);hull.location=obj.location
    hull.select_set(False)
img.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'RockPresets.blend'))
(ART/'mesh-report.json').write_text(json.dumps(report,indent=2))
# Neutral studio overview for checking silhouette and smooth shading.
bpy.ops.mesh.primitive_plane_add(size=200);plane=bpy.context.object
ground=bpy.data.materials.new('Ground');ground.diffuse_color=(.19,.23,.19,1);plane.data.materials.append(ground)
bpy.ops.object.light_add(type='AREA',location=(0,-4,13));bpy.context.object.data.energy=2200;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=9
bpy.ops.object.camera_add(location=(15,-19,18));cam=bpy.context.object;cam.rotation_euler=(Vector((5,2.5,.65))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=18;bpy.context.scene.camera=cam
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.world.color=(.35,.35,.35)
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.filepath=str(ART/'blender-presets.png');bpy.ops.render.render(write_still=True)
