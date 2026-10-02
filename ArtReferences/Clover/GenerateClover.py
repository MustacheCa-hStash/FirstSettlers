"""Author clover leaflets, combine heads into clumps, export FBX and Unity mesh data.

Run Blender 5 in background with --python ArtReferences/Clover/GenerateClover.py.
The JSON payload has baked Unity Y-up coordinates: no FBX transform dependency.
"""
from pathlib import Path
import json
import math
import random
import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Models/Grass+Flowers'
ART = ROOT / 'ArtReferences/Clover'
AUTHOR_SCALE = 2.0  # Runtime scales geometry by uniformScale (.4-.6), not worldScale.
TEXTURE = ROOT / 'Assets/Textures/Flowers/T_CloverLeaf_v02.png'

bpy.ops.wm.read_factory_settings(use_empty=True)
image = bpy.data.images.load(str(TEXTURE))
material = bpy.data.materials.new('CloverLeaf_v02')
material.use_nodes = True
material.surface_render_method = 'DITHERED'
nodes = material.node_tree.nodes
links = material.node_tree.links
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = .86
bsdf.inputs['Specular IOR Level'].default_value = .18
tex = nodes.new('ShaderNodeTexImage'); tex.image = image
color = nodes.new('ShaderNodeVertexColor'); color.layer_name = 'Color'
mix = nodes.new('ShaderNodeMixRGB'); mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1
links.new(tex.outputs['Color'], mix.inputs[1]); links.new(color.outputs['Color'], mix.inputs[2])
select = nodes.new('ShaderNodeMixRGB')
links.new(color.outputs['Alpha'], select.inputs[0]); links.new(color.outputs['Color'], select.inputs[1])
links.new(mix.outputs[0], select.inputs[2]); links.new(select.outputs[0], bsdf.inputs['Base Color'])
alpha = nodes.new('ShaderNodeMath'); alpha.operation = 'MULTIPLY'
links.new(tex.outputs['Alpha'], alpha.inputs[0]); links.new(color.outputs['Alpha'], alpha.inputs[1])
inv = nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
links.new(color.outputs['Alpha'], inv.inputs[1])
add = nodes.new('ShaderNodeMath'); add.operation = 'ADD'
links.new(alpha.outputs[0], add.inputs[0]); links.new(inv.outputs[0], add.inputs[1])
links.new(add.outputs[0], bsdf.inputs['Alpha'])

# An octagonal envelope follows the leaf alpha, preserving the shallow apex notch
# in the texture. Every leaf uses the same direct, undistorted UV coordinates.
OUTLINE = [(.49,.070), (.80,.23), (.91,.47), (.84,.74),
           (.68,.93), (.31,.93), (.14,.74), (.075,.47), (.17,.23)]
report = []

class Geometry:
    def __init__(self):
        self.positions=[]; self.uvs=[]; self.colors=[]; self.triangles=[]
    def vertex(self, position, uv, tint):
        self.positions.append(tuple(position)); self.uvs.append(uv); self.colors.append(tint)
        return len(self.positions)-1
    def stem(self, root, tip, radius=.001, tint=(.25,.37,.075,0)):
        axis = (tip-root).normalized()
        side = axis.cross(Vector((1,0,0))).normalized()
        other = axis.cross(side).normalized()
        start=len(self.positions)
        for center in (root, tip):
            for k in range(3):
                a=k*math.tau/3
                self.vertex(center+(side*math.cos(a)+other*math.sin(a))*radius,(.5,.4),tint)
        for k in range(3):
            n=(k+1)%3
            self.triangles.extend([(start+k,start+n,start+3+k),(start+n,start+3+n,start+3+k)])
    def leaf(self, hub, angle, length, width, tilt, roll, tint):
        along=Vector((math.cos(angle),math.sin(angle),0))
        cross=Vector((-math.sin(angle),math.cos(angle),0))
        ids=[]
        for u,v in [(.49,.52)]+OUTLINE:
            t=(v-.07)/.86
            x=(u-.49)/.82*width
            # A cupped surface, with a raised midrib and gently drooping tip.
            height=math.sin(t*math.pi)*length*.075 - abs(x)*.12 + t*tilt + x*roll
            ids.append(self.vertex(hub+along*(t*length+.002)+cross*x+Vector((0,0,height)),(u,v),tint))
        for k in range(len(OUTLINE)):
            self.triangles.append((ids[0],ids[1+k],ids[1+(k+1)%len(OUTLINE)]))
    def head(self, root, height, yaw, size, rng):
        hub=root+Vector((rng.uniform(-.013,.013),rng.uniform(-.013,.013),height))
        self.stem(root,hub)
        green=rng.uniform(.79,1.14)
        tint=(green*rng.uniform(.80,.89),green*1.10,rng.uniform(.73,.85)*green,1)
        for k in range(3):
            angle=yaw+k*math.tau/3+rng.uniform(-.16,.16)
            length=size*rng.uniform(.94,1.07)
            self.leaf(hub,angle,length,length*rng.uniform(.88,1.06),rng.uniform(-.015,.018),rng.uniform(-.18,.18),tint)
    def bloom(self, root, rng):
        center=root+Vector((.015,-.008,rng.uniform(.13,.17)))
        self.stem(root,center,.0012)
        # Cluster of tiny geometric florets: a readable white-clover pompom.
        for j in range(9):
            a=j*2.399963
            z=-.002+(.016*j/8)
            radius=.011*math.sqrt(max(0,1-((z-.005)/.011)**2))
            point=center+Vector((math.cos(a)*radius,math.sin(a)*radius,z))
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.006,location=point)
            sphere=bpy.context.object
            start=len(self.positions)
            tint=(rng.uniform(.84,.97),rng.uniform(.86,.98),rng.uniform(.72,.89),0)
            for vertex in sphere.data.vertices:
                self.vertex(sphere.matrix_world @ vertex.co,(.5,.4),tint)
            for face in sphere.data.polygons:
                self.triangles.append(tuple(start+i for i in face.vertices))
            bpy.data.objects.remove(sphere,do_unlink=True)

def build(name,count,rx,ry,seed,flowers=0):
    rng=random.Random(seed); geo=Geometry()
    # Phyllotactic placement plus jitter avoids concentric rings and rows.
    positions=[]
    for i in range(count):
        a=i*2.399963+seed*.17
        r=math.sqrt((i+.4)/count)
        irregular=1+.13*math.sin(3*a+.7)+.10*math.cos(5*a)
        x=math.cos(a)*r*rx*irregular+rng.uniform(-.018,.018)
        y=math.sin(a)*r*ry*irregular+rng.uniform(-.018,.018)
        if count==1: x=y=0
        root=Vector((x,y,-.003)); positions.append(root)
        height=rng.uniform(.035,.105)*(1-.12*r)
        size=rng.uniform(.040,.063)*(1-.12*r)
        geo.head(root,height,rng.uniform(0,math.tau),size,rng)
    for root in rng.sample(positions,flowers): geo.bloom(root,rng)
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(geo.positions,[],geo.triangles); mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    colors=mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
    for i,c in enumerate(geo.colors): colors.data[i].color=c
    for loop in mesh.loops: uv.data[loop.index].uv=geo.uvs[loop.vertex_index]
    # Leaf midrib normals are smooth; stems remain intentionally low-poly.
    for poly in mesh.polygons: poly.use_smooth=True
    mesh.materials.append(material)
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
    # Export scene-compatible authoring units. Unity mesh data is authoritative.
    obj.scale=(AUTHOR_SCALE,)*3
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
        axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,
        use_mesh_modifiers=True,mesh_smooth_type='FACE',colors_type='LINEAR')
    obj.scale=(1,1,1)
    bpy.context.view_layer.update()
    normals=[tuple(v.normal) for v in mesh.vertices]
    data={'name':name,'positions':[{'x':x*AUTHOR_SCALE,'y':z*AUTHOR_SCALE,'z':-y*AUTHOR_SCALE} for x,y,z in geo.positions],
          'normals':[{'x':x,'y':z,'z':-y} for x,y,z in normals],
          'uvs':[{'x':u,'y':v} for u,v in geo.uvs],
          'colors':[{'r':r,'g':g,'b':b,'a':a} for r,g,b,a in geo.colors],
          'triangles':[i for face in geo.triangles for i in face]}
    (OUT/(name+'.json')).write_text(json.dumps(data,separators=(',',':')))
    report.append({'name':name,'heads':count,'blooms':flowers,'vertices':len(geo.positions),
                   'triangles':len(geo.triangles),'preview_dimensions_m':[round(v,4) for v in obj.dimensions],
                   'author_scale':AUTHOR_SCALE,'submeshes':1})
    return obj

objects=[build('CloverClump_v02_A',34,.34,.30,251),
         build('CloverClump_v02_B_Flowering',38,.37,.32,651,3),
         build('CloverClump_v02_Edge',19,.27,.21,932),
         build('CloverSingle_v02',1,0,0,817)]
(ART/'MeshReport.json').write_text(json.dumps(report,indent=2))
for i,obj in enumerate(objects): obj.location.x=i*1.2
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Clover_v02_Source.blend'))
print('CLOVER_MESH_REPORT',json.dumps(report))

# Reproducible studio render of the source mesh and an overlapping patch.
for obj in objects: obj.hide_render=True
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1400; scene.render.resolution_y=1000
scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
world=bpy.data.worlds.new('Soft daylight'); world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.73,.84,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.55; scene.world=world
bpy.ops.object.light_add(type='AREA',location=(-1,-1,3)); sun=bpy.context.object
sun.data.energy=170; sun.data.shape='DISK'; sun.data.size=2
sun.rotation_euler=(Vector((0,0,0))-sun.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.007)); ground=bpy.context.object
groundmat=bpy.data.materials.new('Muted meadow ground'); groundmat.use_nodes=True
groundbsdf=groundmat.node_tree.nodes.get('Principled BSDF'); groundbsdf.inputs['Roughness'].default_value=.96
noise=groundmat.node_tree.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=24
ramp=groundmat.node_tree.nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].color=(.065,.081,.037,1); ramp.color_ramp.elements[1].color=(.18,.20,.095,1)
groundmat.node_tree.links.new(noise.outputs['Fac'],ramp.inputs[0]); groundmat.node_tree.links.new(ramp.outputs[0],groundbsdf.inputs['Base Color'])
ground.data.materials.append(groundmat)
bpy.ops.object.camera_add(); camera=bpy.context.object; scene.camera=camera; camera.data.lens=48
def render(filename,eye,target):
    camera.location=eye; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(ART/filename); bpy.ops.render.render(write_still=True)
main=objects[0]; main.location=(0,0,0); main.hide_render=False
render('Clover_v02_Studio.png',(.82,-.91,.70),(0,0,.045))
main.hide_render=True
rng=random.Random(441)
patch=[]
for i in range(28):
    src=objects[1] if i%7==0 else objects[0] if i%3 else objects[2]
    obj=bpy.data.objects.new('Patch_'+str(i),src.data); bpy.context.collection.objects.link(obj)
    a=i*2.399963; r=math.sqrt(i/28)
    obj.location=(math.cos(a)*r*1.2,math.sin(a)*r*.83,0)
    obj.rotation_euler.z=rng.uniform(0,math.tau); obj.scale=(rng.uniform(.82,1.10),)*3
    patch.append(obj)
render('Clover_v02_Patch.png',(2.3,-2.85,2.45),(0,0,.04))
print('CLOVER_SOURCE_RENDERS_COMPLETE')
