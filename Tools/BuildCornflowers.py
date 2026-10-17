"""Blender --background --python Tools/BuildCornflowers.py. One instanced plant, one intact painted atlas."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/Cornflowers'
OUT = ROOT / 'Assets/Models/Grass+Flowers'
ART.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
image = bpy.data.images.load(str(ROOT / 'Assets/Textures/Flowers/T_Cornflower_Painted_Floret.png'))
image.pack()
w, h = image.size
pixels = list(image.pixels)
def region(x0, x1):
    xs, ys = [], []
    for y in range(h):
        for x in range(int(w*x0), int(w*x1)):
            if pixels[(y*w+x)*4+3] > .5:
                xs.append(x); ys.append(y)
    return ((min(xs)-3)/w, (min(ys)-3)/h, (max(xs)+4)/w, (max(ys)+4)/h)
petal_uv = region(0, 1)
mat = bpy.data.materials.new('Painted Blue Cornflower'); mat.use_nodes = True
nodes, links = mat.node_tree.nodes, mat.node_tree.links
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = 1; bsdf.inputs['Specular IOR Level'].default_value = 0
tex = nodes.new('ShaderNodeTexImage'); tex.image = image
meta = nodes.new('ShaderNodeVertexColor'); meta.layer_name = 'Color'
split = nodes.new('ShaderNodeSeparateColor'); links.new(meta.outputs['Color'], split.inputs[0])
solid = nodes.new('ShaderNodeMixRGB'); solid.inputs[1].default_value = (.28,.43,.09,1); solid.inputs[2].default_value = (.025,.035,.12,1)
links.new(split.outputs['Blue'], solid.inputs[0])
mix = nodes.new('ShaderNodeMixRGB'); links.new(meta.outputs['Alpha'], mix.inputs[0]); links.new(solid.outputs[0], mix.inputs[1]); links.new(tex.outputs['Color'], mix.inputs[2]); links.new(mix.outputs[0], bsdf.inputs['Base Color'])
alpha = nodes.new('ShaderNodeMixRGB'); alpha.inputs[1].default_value = (1,1,1,1)
links.new(meta.outputs['Alpha'], alpha.inputs[0]); links.new(tex.outputs['Alpha'], alpha.inputs[2]); links.new(alpha.outputs[0], bsdf.inputs['Alpha'])
mat.surface_render_method = 'DITHERED'
verts, uvs, colors, faces = [], [], [], []
def vertex(p, uv=(.5,.5), color=(0,1,0,0)):
    verts.append(tuple(p)); uvs.append(uv); colors.append(color); return len(verts)-1
def uv(rect, u, v):
    return (rect[0]+(rect[2]-rect[0])*u, rect[1]+(rect[3]-rect[1])*v)
root, tip = Vector((0,0,0)), Vector((.045,.025,1.12))
# Three-sided stem, three stations, tapered and gently bent. 12 triangles.
rings = []
for t in (0,.5,1):
    center = root.lerp(tip,t) + Vector((math.sin(t*math.pi)*.027,0,0))
    rings.append([vertex(center+Vector((math.cos(k*math.tau/3),math.sin(k*math.tau/3),0))*.035*(1-t*.38)) for k in range(3)])
for a,b in zip(rings,rings[1:]):
    for k in range(3): faces.extend(((a[k],a[(k+1)%3],b[(k+1)%3]),(a[k],b[(k+1)%3],b[k])))
# The approved leafless silhouette and 0.035 m stem radius match the poppy.
# Eight broad upward-flaring florets give the head vertical color; each retains its 6-vertex, 4-triangle strip.
normal = Vector((-.12,.16,1)).normalized()
side = normal.cross(Vector((0,1,0))).normalized(); other = normal.cross(side).normalized()
for k in range(8):
    angle = k*math.tau/8+.18
    forward = side*math.cos(angle)+other*math.sin(angle)
    lateral = -side*math.sin(angle)+other*math.cos(angle)
    ids = []
    for row,(radius,width,lift) in enumerate(((.018,.085,-.0125),(.105,.085,.0515),(.195,.085,.135))):
        for col in range(2):
            t = col*2-1
            p = tip+forward*radius+lateral*t*width+normal*(lift+(k%2)*.006)
            ids.append(vertex(p,uv(petal_uv,col,row*.5),(1,1,0,1)))
    for row in range(2):
        a,b,c,d = ids[row*2],ids[row*2+1],ids[row*2+2],ids[row*2+3]
        faces.extend(((a,b,d),(a,d,c)))
# A small solid indigo center keeps the bloom readable without stamens or seed detail.
center_top = vertex(tip+normal*.055,color=(0,1,1,0))
center_bottom = vertex(tip-normal*.005,color=(0,.9,1,0))
ring = [vertex(tip+normal*.022+(side*math.cos(k*math.tau/8)+other*math.sin(k*math.tau/8))*.052,color=(0,1,1,0)) for k in range(8)]
for k in range(8):faces.extend(((center_top,ring[k],ring[(k+1)%8]),(center_bottom,ring[(k+1)%8],ring[k])))
# A simple eight-sided green cup below the head. No branches, extending leaves or bract textures.
base = vertex(tip-normal*.055)
ring = [vertex(tip+(side*math.cos(k*math.tau/8)+other*math.sin(k*math.tau/8))*.058) for k in range(8)]
for k in range(8):faces.append((base,ring[(k+1)%8],ring[k]))
mesh = bpy.data.meshes.new('Cornflower_Painted'); mesh.from_pydata(verts,[],faces); mesh.update(); mesh.materials.append(mat)
uv_layer = mesh.uv_layers.new(name='UVMap'); color_layer = mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
for polygon in mesh.polygons:
    polygon.use_smooth = True
    for loop in polygon.loop_indices:
        vi = mesh.loops[loop].vertex_index; uv_layer.data[loop].uv = uvs[vi]; color_layer.data[loop].color = colors[vi]
obj = bpy.data.objects.new('Cornflower',mesh); bpy.context.collection.objects.link(obj); obj.select_set(True); bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=str(OUT/'Cornflower_Painted.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,add_leaf_bones=False,mesh_smooth_type='OFF')
def vector(p):return dict(x=p[0],y=p[2],z=p[1])
data = dict(name='Cornflower_Painted',positions=[vector(p) for p in verts],normals=[vector(v.normal) for v in mesh.vertices],
    uvs=[dict(x=u[0],y=u[1]) for u in uvs],colors=[dict(r=c[0],g=c[1],b=c[2],a=c[3]) for c in colors],
    triangles=[i for f in faces for i in (f[0],f[2],f[1])])
(OUT/'Cornflower_Painted.json').write_text(json.dumps(data,separators=(',',':')))
head_depth = max((Vector(p)-tip).dot(normal) for p,c in zip(verts,colors) if c[0]==1)-min((Vector(p)-tip).dot(normal) for p,c in zip(verts,colors) if c[0]==1)
report = dict(headDepthAlongNormal=head_depth,vertices=len(verts),triangles=len(faces),flowerHeads=1,outerFlorets=8,leaves=0,stemBaseRadius=.035,materials=1,sourceDimensions=list(obj.dimensions),runtimeScaleRange=[.46,.62],petalUV=petal_uv)
(ART/'mesh-report.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Cornflower_Painted.blend'))
print('CORNFLOWER_BLENDER_REPORT',json.dumps(report))
