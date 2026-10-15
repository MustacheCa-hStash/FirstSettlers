"""Render temporary material proof forms; never edits source textures or game assets."""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector
import numpy as np

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[2]
LAYOUT=json.loads((OUT/'atlas-layout.json').read_text(encoding='utf-8-sig'))
REG={r['id']:r for r in LAYOUT['regions']}
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1800
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='Standard'
scene.view_settings.look='None'
scene.world=bpy.data.worlds.new('Soft studio')
scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.61,.58,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7

def material(name,path):
    m=bpy.data.materials.new(name)
    m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Roughness'].default_value=1
    bs.inputs['Specular IOR Level'].default_value=0
    tex=m.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=bpy.data.images.load(str(path),check_existing=True)
    tex.interpolation='Linear'
    m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    return m

atlas=material('Household atlas / opaque albedo',OUT/'household-trim-albedo.png')
wood=material('Existing Meadow Timber',ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png')
stone=material('Existing Painted Stone',ROOT/'Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png')
faces_checked=0

def uvpoint(reg,u,v):
    a,b,c,d=REG[reg]['uv_texel_centres']
    return (a+u*(c-a),b+v*(d-b))

def mesh(name,verts,faces,uvs,reg,mat=atlas):
    global faces_checked
    me=bpy.data.meshes.new(name)
    me.from_pydata(verts,[],faces)
    me.update()
    ob=bpy.data.objects.new(name,me)
    scene.collection.objects.link(ob)
    me.materials.append(mat)
    layer=me.uv_layers.new()
    for poly,coords in zip(me.polygons,uvs):
        for li,(u,v) in zip(poly.loop_indices,coords):
            layer.data[li].uv=uvpoint(reg,u,v) if reg else (u,v)
        if reg:
            a,b,c,d=REG[reg]['uv_texel_centres']
            assert all(a-1e-7<=layer.data[i].uv.x<=c+1e-7 and b-1e-7<=layer.data[i].uv.y<=d+1e-7 for i in poly.loop_indices)
            faces_checked+=1
    return ob

def lathe(name,center,profile,regions,repeat_u=1):
    verts=[]; faces=[];uvs=[]; mats=[]
    N=96
    for radius,z in profile:
        for i in range(N):
            angle=i*2*math.pi/N
            verts.append((center[0]+radius*math.cos(angle),center[1]+radius*math.sin(angle),z+center[2]))
    # A separate mesh per zone keeps each face wholly inside one atlas region.
    for j in range(len(profile)-1):
        f=[];uv=[]
        for i in range(N):
            f.append((j*N+i,j*N+(i+1)%N,(j+1)*N+(i+1)%N,(j+1)*N+i))
            period=N//repeat_u
            u0=(i%period)/period
            u1=(i%period+1)/period
            uv.append([(u0,0),(u1,0),(u1,1),(u0,1)])
        ob=mesh(name+' zone '+str(j),verts,f,uv,regions[j])
        for p in ob.data.polygons:p.use_smooth=True
    return

def box(name,loc,size,mat,reg=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    ob=bpy.context.object;ob.name=name;ob.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    ob.data.materials.append(mat)
    layer=ob.data.uv_layers.active
    for poly in ob.data.polygons:
        for li,uv in zip(poly.loop_indices,[(0,0),(1,0),(1,1),(0,1)]):
            layer.data[li].uv=uvpoint(reg,*uv) if reg else (.05+uv[0]*.7,.89+uv[1]*.06)
    bevel=ob.modifiers.new('Soft silhouette edge','BEVEL')
    bevel.width=.008;bevel.segments=1
    ob.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return ob

# Back row: wicker cylinder with a real cavity, two clay vessels, a hanging textile.
lathe('Wicker basket',(-1.28,.12,0),[(.20,.01),(.26,.52),(.27,.56),(.25,.58),(.235,.54),(.18,.035),(.01,.035)],['A','F','F','F','A','A'],repeat_u=2)
lathe('Decorated clay jar',(-.46,.20,0),[(.10,.01),(.24,.13),(.27,.33),(.24,.47),(.20,.53),(.13,.58),(.135,.62),(.115,.62),(.11,.57),(.18,.50),(.22,.30),(.08,.04)],['D','D','D','G','D','D','D','D','D','D','D'])
lathe('Cooking bowl',(-.45,-.45,.01),[(.08,0),(.16,.08),(.18,.18),(.16,.18),(.14,.08),(.07,.025)],['I','I','D','I','I'])

# Cloth is subdivided into bounded repeated UV sections; no whole-atlas Repeat.
verts=[];faces=[];uv=[]
nx,ny=12,12
for j in range(ny+1):
    for i in range(nx+1):
        x=.28+.96*i/nx
        y=.29+.025*math.sin(i/nx*math.pi*5)
        z=.15+.95*j/ny
        verts.append((x,y,z))
for j in range(ny):
    for i in range(nx):
        faces.append((j*(nx+1)+i,j*(nx+1)+i+1,(j+1)*(nx+1)+i+1,(j+1)*(nx+1)+i))
        u0=(i%6)/6;v0=(j%3)/3
        uv.append([(u0,v0),(u0+1/6,v0),(u0+1/6,v0+1/3),(u0,v0+1/3)])
mesh('Plain wool hanging / two by four repeats',verts,faces,uv,'C')
mesh('Wool border',[ (.28,.255,.15),(1.24,.255,.15),(1.24,.255,.30),(.28,.255,.30)],[(0,1,2,3)],[[(0,0),(1,0),(1,1),(0,1)]],'E')
box('Timber suspension bar',(.76,.31,1.13),(1.10,.05,.05),wood)
box('Timber left support',(.25,.37,.55),(.045,.055,1.15),wood)
box('Timber right support',(1.28,.37,.55),(.045,.055,1.15),wood)

# Foreground: rush mat, a bone comb and leaf forms, plus existing timber and stone.
mesh('Rush mat',[(-1.65,-.78,.008),(-.85,-.78,.008),(-.85,-.35,.008),(-1.65,-.35,.008)],[(0,1,2,3)],[[(0,0),(1,0),(1,1),(0,1)]],'B')
box('Bone comb handle',(.34,-.58,.028),(.28,.07,.025),atlas,'H')
for i in range(7):box('Bone tooth '+str(i),(.24+i*.032,-.65,.028),(.021,.09,.025),atlas,'H')
for i in range(6):
    x=.73+i*.055;y=-.54+.055*math.sin(i)
    ob=mesh('Opaque modeled herb leaf '+str(i),[(x,y,.02),(x+.07,y-.05,.03),(x+.15,y,.035),(x+.065,y+.05,.025)],[(0,1,2,3)],[[(.1,.5),(.5,.1),(.9,.5),(.5,.9)]],'K')
box('Existing wood sample',(-1.16,.70,.10),(.65,.22,.20),wood)
box('Existing stone sample',(-.33,.70,.12),(.42,.32,.24),stone)
# Stone sample needs all-image UVs, rather than wood-strip UVs.
stoneob=bpy.data.objects.get('Existing stone sample')
for poly in stoneob.data.polygons:
    for li,uv in zip(poly.loop_indices,[(.1,.1),(.9,.1),(.9,.9),(.1,.9)]):stoneob.data.uv_layers.active.data[li].uv=uv

ground=bpy.data.materials.new('Muted studio floor')
ground.diffuse_color=(.20,.25,.22,1)
box('Studio floor',(0,0,-.06),(200,200,.1),ground)
for pos,energy,size in [((0,-3,5),450,5),((-3,1,3),150,4)]:
    bpy.ops.object.light_add(type='AREA',location=pos)
    l=bpy.context.object;l.data.energy=energy;l.data.size=size
    l.rotation_euler=(Vector((0,0,.35))-l.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(2.8,-5.8,3.4))
camera=bpy.context.object
camera.rotation_euler=(Vector((-.1,.05,.48))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO';camera.data.ortho_scale=4.35;scene.camera=camera

# Source verification reads pixels only. No bitmap is changed.
im=bpy.data.images.load(str(OUT/'household-trim-albedo.png'),check_existing=True)
p=np.empty(len(im.pixels),dtype=np.float32);im.pixels.foreach_get(p)
p=p.reshape((im.size[1],im.size[0],4))
assert im.size[0]==1254 and im.size[1]==1254
assert float(p[:,:,3].min())==1.0
edge_report={}
for r in LAYOUT['regions']:
    x0,y0,x1,y1=r['safe_content_pixels_top_left']
    tile=p[1254-y1:1254-y0,x0:x1,:3]
    edge_report[r['id']]={'u_edge_mean_abs_difference_0_1':round(float(np.abs(tile[:,0]-tile[:,-1]).mean()),5),
                          'v_edge_mean_abs_difference_0_1':round(float(np.abs(tile[0]-tile[-1]).mean()),5)}
report={'source_size':list(im.size[:]),'source_sha256':hashlib.sha256((OUT/'household-trim-albedo.png').read_bytes()).hexdigest(),
        'opaque':True,'new_texture_count':1,'preview_faces_with_checked_atlas_uvs':faces_checked,
        'edges':edge_report,'extruded_gutters':False,'source_pixels_modified':False,
        'scope':'Temporary material proof meshes only; no furnishing/game asset implementation or Unity verification.'}
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(OUT/'material-proof.png')
bpy.ops.render.render(write_still=True)
for image in bpy.data.images:
    if image.source=='FILE' and image.filepath:
        image.filepath=bpy.path.relpath(image.filepath,start=str(OUT))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'material-proof.blend'))
print('HOUSEHOLD_MATERIAL_PROOF',json.dumps(report))

