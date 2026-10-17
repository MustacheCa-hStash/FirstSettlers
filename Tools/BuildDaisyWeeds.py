"""Blender authoring for the replacement painted daisy clump. Run Blender --background --python this file."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/DaisyWeeds';OUT=ROOT/'Assets/Models/Grass+Flowers'
ART.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
image=bpy.data.images.load(str(ROOT/'Assets/Textures/Flowers/T_DaisyWeeds_Painted_Atlas.png'));image.pack()
w,h=image.size
pixels=list(image.pixels)
# Alpha analysis only: the atlas is used intact. Locate its two isolated silhouettes for precise UV islands.
def region(x0,x1):
    xs=[];ys=[]
    for y in range(h):
        for x in range(int(w*x0),int(w*x1)):
            if pixels[(y*w+x)*4+3]>.5:xs.append(x);ys.append(y)
    return ((min(xs)-4)/w,(min(ys)-4)/h,(max(xs)+5)/w,(max(ys)+5)/h)
flower_uv=(35/1254,223/1254,805/1254,964/1254) # Original approved atlas mapping.
# Retain the approved leaf mapping; this revision only changes flower-head geometry.
leaf_uv=(0.6786283891547049, 0.037480063795853266, 0.9776714513556619, 0.9593301435406698)
mat=bpy.data.materials.new('Painted Daisy Weeds');mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=1;bsdf.inputs['Specular IOR Level'].default_value=0
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
mat.surface_render_method='DITHERED'
metadata=mat.node_tree.nodes.new('ShaderNodeVertexColor');metadata.layer_name='Color'
mix=mat.node_tree.nodes.new('ShaderNodeMixRGB');mix.inputs[1].default_value=(.28,.43,.09,1)
mat.node_tree.links.new(metadata.outputs['Alpha'],mix.inputs[0]);mat.node_tree.links.new(tex.outputs['Color'],mix.inputs[2]);mat.node_tree.links.new(mix.outputs[0],bsdf.inputs['Base Color'])
mul=mat.node_tree.nodes.new('ShaderNodeMath');mul.operation='MULTIPLY';mat.node_tree.links.new(tex.outputs['Alpha'],mul.inputs[0]);mat.node_tree.links.new(metadata.outputs['Alpha'],mul.inputs[1])
inv=mat.node_tree.nodes.new('ShaderNodeMath');inv.operation='SUBTRACT';inv.inputs[0].default_value=1;mat.node_tree.links.new(metadata.outputs['Alpha'],inv.inputs[1])
add=mat.node_tree.nodes.new('ShaderNodeMath');add.operation='ADD';mat.node_tree.links.new(mul.outputs[0],add.inputs[0]);mat.node_tree.links.new(inv.outputs[0],add.inputs[1]);mat.node_tree.links.new(add.outputs[0],bsdf.inputs['Alpha'])
vertices=[];uvs=[];colors=[];faces=[];groups={}
def vertex(p,uv,color):
    vertices.append(tuple(p));uvs.append(uv);colors.append(color);return len(vertices)-1
def mapuv(rect,u,v):return (rect[0]+(rect[2]-rect[0])*u,rect[1]+(rect[3]-rect[1])*v)
def stem(root,tip,radius):
    axis=(tip-root).normalized();side=axis.cross(Vector((0,1,0))).normalized();other=axis.cross(side).normalized()
    rings=[]
    for t in (0,.5,1):
        centre=root.lerp(tip,t)+Vector((math.sin(t*math.pi)*.018,0,0));ring=[]
        for k in range(3):
            a=k*math.tau/3;ring.append(vertex(centre+(side*math.cos(a)+other*math.sin(a))*radius*(1-t*.38),(.5,.5),(0,1,0,0)))
        rings.append(ring)
    for a,b in zip(rings,rings[1:]):
        for k in range(3):faces.extend(((a[k],a[(k+1)%3],b[(k+1)%3]),(a[k],b[(k+1)%3],b[k])))
def leaf(base,angle,length,width,lift=.2):
    direction=Vector((math.cos(angle),math.sin(angle),lift)).normalized();side=Vector((-math.sin(angle),math.cos(angle),0))
    # Tight, gently folded leaf strips. Four triangles rather than a whole-plant card.
    positions=[base,base+direction*length*.45-side*width*.5,base+direction*length*.45+side*width*.5,
               base+direction*length*.78-side*width*.40+Vector((0,0,.008)),base+direction*length*.78+side*width*.40+Vector((0,0,.008)),base+direction*length]
    uv=[(.5,0),(0,.42),(1,.42),(0,.76),(1,.76),(.5,1)]
    ids=[vertex(p,mapuv(leaf_uv,*u),(0,.92,0,1)) for p,u in zip(positions,uv)]
    faces.extend(((ids[0],ids[2],ids[1]),(ids[1],ids[2],ids[4]),(ids[1],ids[4],ids[3]),(ids[3],ids[4],ids[5])))
def bloom(tip,radius,tilt,yaw):
    normal=Vector((math.sin(tilt)*math.cos(yaw),math.sin(tilt)*math.sin(yaw),math.cos(tilt)))
    side=normal.cross(Vector((0,1,0))).normalized();other=normal.cross(side).normalized()
    # Restore the original almost-flat twelve-segment head.
    centre=vertex(tip+normal*.006,mapuv(flower_uv,.5,.5),(1,1,0,1));ring=[]
    for k in range(12):
        a=k*math.tau/12
        ring.append(vertex(tip+(side*math.cos(a)+other*math.sin(a))*radius,
            mapuv(flower_uv,.5+math.cos(a)*.54,.5+math.sin(a)*.54),(1,1,0,1)))
    for k in range(12):faces.append((centre,ring[k],ring[(k+1)%12]))
    # Small opaque calyx underneath: the flower is readable from below, with no duplicated petal sheet.
    bottom=vertex(tip-normal*.024,(.5,.5),(0,.85,0,0));calyx=[]
    for k in range(5):
        a=k*math.tau/5;calyx.append(vertex(tip+(side*math.cos(a)+other*math.sin(a))*.03,(.5,.5),(0,.85,0,0)))
    for k in range(5):faces.append((bottom,calyx[(k+1)%5],calyx[k]))
for i,(x,y,height,lean,radius) in enumerate([(-.19,-.07,1.18,.08,.17),(.19,.04,.86,-.07,.145),(.015,.18,1.06,.055,.16)]):
    start=len(faces);root=Vector((x,y,0));tip=Vector((x+lean,y+.035,height))
    stem(root,tip,.009)
    for j in range(2):leaf(root.lerp(tip,.26+j*.25),i*2.2+j*2.7,.31-j*.035,.10-j*.012,.32)
    bloom(tip,radius,.18+i*.13,i*2.3)
    groups['FlowerStem'+str(i+1)]=(start,len(faces))
for i in range(2):leaf(Vector((-.01,.04,.012)),i*math.pi+.6,.30,.11,.12)
mesh=bpy.data.meshes.new('DaisyWeeds_Painted');mesh.from_pydata(vertices,[],faces);mesh.update();mesh.materials.append(mat)
layer=mesh.uv_layers.new(name='UVMap');color=mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
for p in mesh.polygons:
    p.use_smooth=True
    for loop in p.loop_indices:
        vi=mesh.loops[loop].vertex_index;layer.data[loop].uv=uvs[vi];color.data[loop].color=colors[vi]
obj=bpy.data.objects.new('daisyWeeds',mesh);bpy.context.collection.objects.link(obj);obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.fbx(filepath=str(OUT/'DaisyWeeds_Painted.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False,mesh_smooth_type='OFF')
# JSON is explicitly baked into Unity Y-up. Runtime extraction therefore needs no child/prefab transform.
def vector(p):return dict(x=p[0],y=p[2],z=p[1])
data=dict(name='DaisyWeeds_Painted',positions=[vector(p) for p in vertices],normals=[vector(v.normal) for v in mesh.vertices],
    uvs=[dict(x=u[0],y=u[1]) for u in uvs],colors=[dict(r=c[0],g=c[1],b=c[2],a=c[3]) for c in colors],
    triangles=[i for f in faces for i in (f[0],f[2],f[1])])
(OUT/'DaisyWeeds_Painted.json').write_text(json.dumps(data,separators=(',',':')))
report=dict(headDepthAlongNormalRange=[.006,.006],vertices=len(vertices),triangles=len(faces),flowerHeads=3,leaves=8,materialCount=1,sourceDimensions=list(obj.dimensions),runtimeScaleRange=[.4*1.283,.5*1.283],flowerUV=flower_uv,leafUV=leaf_uv)
(ART/'mesh-report.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'DaisyWeeds_Painted.blend'))
print('DAISY_BLENDER_REPORT',json.dumps(report))
