"""Build the layered roof from the six existing construction objects.

Blender --background --python Tools/BuildLayeredThatchRoof.py [-- source.blend]
Writes the repository's editable roof source; never saves over the shared source.
"""
import bpy, bmesh, math, json, random, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtReferences/ThatchRoof/Redesign'
ART.mkdir(parents=True, exist_ok=True)
SOURCE = ART / 'LayeredThatchRoof.blend'
args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
source = Path(args[0]) if args else SOURCE
if not source.exists():
    raise RuntimeError('Provide the existing BuildComponents.blend as the first argument on the first build.')
bpy.ops.wm.open_mainfile(filepath=str(source))
names = ['Roof_Single_'+n for n in ('Thatch','EaveRail','Rafter_0','Rafter_1','Rafter_2','WovenSubstrate')]
parts = [bpy.data.objects[n] for n in names]
material = parts[0].data.materials[0]
for obj in list(bpy.data.objects):
    if obj not in parts:
        bpy.data.objects.remove(obj, do_unlink=True)
for obj in parts:
    obj.hide_set(False)
    obj.hide_render = False
    for collection in obj.users_collection:
        collection.hide_render = False
        collection.hide_viewport = False

# Repoint the copied roof material, leaving the user's original .blend untouched.
material.use_nodes = True
nodes = material.node_tree.nodes
bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
for node in list(nodes):
    if node.type == 'TEX_IMAGE':
        nodes.remove(node)
image = bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Wood/thatch-roof-atlas.png'), check_existing=False)
tex = nodes.new('ShaderNodeTexImage'); tex.image = image
material.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
material.node_tree.links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
bsdf.inputs['Roughness'].default_value = .95

rng = random.Random(72341)
vertices, faces, mappings, smooth = [], [], [], []
fringe_faces = set()
U0, U1, V0, V1 = 8/2048, 2040/2048, 8/2048, 1016/2048

def piece(v, f, surface=False):
    start = len(vertices)
    vertices.extend(v)
    for face in f:
        face = tuple(start+i for i in face)
        faces.append(face)
        # Opaque faces are unwrapped after final triangulation; fringe cards
        # receive their explicit atlas rectangle before triangulation.
        mappings.append([(0,0)]*len(face))
        smooth.append(surface)

# Closed opaque inner shell, inset from the loose ends. Fibres carry the outer
# silhouette; the shell keeps the roof opaque and shadows watertight.
v = [(x,y,y+h) for h in (.12,.40) for y in (-.12,2) for x in (0,4)]
piece(v, [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)])

bundle_count = 0
for row in range(4):
    # Fixed X endpoints keep sideways repetition on the existing module.
    cuts = [0] + [i*.25+(rng.uniform(-.025,.025) if i<16 else 0) for i in range(1,16)] + [4]
    for col, (left,right) in enumerate(zip(cuts,cuts[1:])):
        tip = -.25+row*.5625+rng.uniform(-.055,.055)
        root = min(2, tip+.77+rng.uniform(-.025,.025))
        if row == 3: root = 2
        heights = [.424,.482,.515,.482,.424]
        v = []
        # A slightly rounded, gently fanned top. Root sinks into the next course.
        for j,t in enumerate((0,.52,1)):
            for k,s in enumerate((0,.22,.5,.78,1)):
                x = left+(right-left)*s
                y = tip+(root-tip)*t
                if j==0: y += rng.uniform(-.018,.018) if k not in (0,4) else 0
                h = .40+(heights[k]-.40)*(.50 if j==0 else .95 if j==1 else .85 if row==3 else .4)
                v.append((x,y,y+h))
        v.extend([(left,tip,tip+.40),(right,tip,tip+.40),(left,root,root+.40),(right,root,root+.40)])
        f = []
        for j in range(2):
            for k in range(4):
                a = j*5+k; f.append((a,a+1,a+6,a+5))
        f.extend([(4,3,2,1,0,15,16),(10,11,12,13,14,18,17),
                  (0,5,10,17,15),(4,16,18,14,9),(15,17,18,16)])
        piece(v,f,True)
        bundle_count += 1
        # Lifted, curved cutout strip: the same geometric principle observed in
        # Valheim, using our own artwork and substantially fuller course volume.
        # It covers the solid lip and extends below it as individual fine tips.
        card=[]
        for t in (0,1):
            for k,s in enumerate((0,.22,.5,.78,1)):
                x=left+(right-left)*s
                y=tip+(.17 if t else -.18)
                h=(heights[k]+.01) if t else heights[k]-.085
                card.append((x,y,y+h))
        first_face=len(faces)
        piece(card,[(k,k+1,k+6,k+5) for k in range(4)],True)
        for k in range(4):
            mappings[first_face+k]=[(.504+.488*((col%8)+(0,.22,.5,.78,1)[i%5])/8, .55 if i<5 else .97) for i in (k,k+1,k+6,k+5)]
            fringe_faces.add(first_face+k)
        # Three low-poly straw splinters per bunch, concentrated at the exposed
        # course lip. Closed triangular prisms have real depth in every pass.
        for stem in range(3):
            x = left+(right-left)*(.16+.32*stem)+rng.uniform(-.01,.01)
            y0 = tip-rng.uniform(.015,.065)
            y1 = tip+rng.uniform(.14,.26)
            h0 = rng.uniform(.36,.48)
            h1 = h0+.012
            w = rng.uniform(.006,.012)
            x1 = max(left+w,min(right-w,x+rng.uniform(-.025,.025)))
            v = [(x-w,y0,y0+h0),(x+w,y0,y0+h0),(x,y0,y0+h0+.014),
                 (x1-w,y1,y1+h1),(x1+w,y1,y1+h1),(x1,y1,y1+h1+.014)]
            piece(v,[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)])

mesh = bpy.data.meshes.new('Layered thatch - closed core, 64 bundles and 192 straw tips')
mesh.from_pydata(vertices,[],faces); mesh.update()
uv = mesh.uv_layers.new(name='UVMap')
for polygon,coords,use_smooth in zip(mesh.polygons,mappings,smooth):
    for li,coord in zip(polygon.loop_indices,coords): uv.data[li].uv = coord
    polygon.use_smooth = use_smooth
    polygon.material_index = 1 if polygon.index in fringe_faces else 0
bm = bmesh.new(); bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
# Opaque pieces are closed; only the explicitly authored fringe cards are open.
open_edges=sum(len(e.link_faces)==1 for e in bm.edges)
assert open_edges==64*10, f'Unexpected open edge count: {open_edges}'
bmesh.ops.triangulate(bm,faces=bm.faces[:])
bm.to_mesh(mesh); bm.free()
# Repair projection on the few triangles in the deliberately nonplanar lips.
# This is done on the final triangulation, so an n-gon cannot hide a UV fold.
for polygon in mesh.polygons:
    if polygon.material_index==0:
        n=polygon.normal
        for li in polygon.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co;h=p.z-p.y
            if abs(n.x)>.6: u=.10+(h-.12)/4;t=(p.y+.55)/2.75
            elif abs(n.dot(Vector((0,-1,1)).normalized()))>.75: u=p.x/4;t=(p.y+.55)/2.75
            else: u=p.x/4;t=.08+(h-.12)/3.54
            mesh.uv_layers.active.data[li].uv=(U0+(U1-U0)*u,V0+(V1-V0)*t)
    a,b,c = [mesh.uv_layers.active.data[i].uv for i in polygon.loop_indices]
    area = abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))
    if area < 1e-9:
        normal = polygon.normal
        axis = max(range(3),key=lambda i:abs(normal[i]))
        axes = [i for i in range(3) if i!=axis]
        for li in polygon.loop_indices:
            p = mesh.vertices[mesh.loops[li].vertex_index].co
            mesh.uv_layers.active.data[li].uv = (U0+(U1-U0)*(.15+p[axes[0]]/5), V0+(V1-V0)*(.15+p[axes[1]]/5))
    polygon.material_index=0
mesh.materials.append(material)
parts[0].data = mesh
parts[0].matrix_world.identity()
# Avoid smoothing the closed lips into their tops. Smooth only the broad strips.
for p in mesh.polygons:
    p.use_smooth = p.use_smooth and p.normal.z>.3 and p.normal.y<-.3

exports = bpy.data.collections.new('Layered thatch export')
bpy.context.scene.collection.children.link(exports)
bpy.ops.object.select_all(action='DESELECT')
copies = []
for part in parts:
    obj = part.copy(); obj.data = part.data.copy(); exports.objects.link(obj)
    obj.select_set(True); copies.append(obj)
bpy.context.view_layer.objects.active = copies[0]
bpy.ops.object.join(); joined = bpy.context.object; joined.name = 'Roof_Thatch_Export'
joined.data.materials.clear(); joined.data.materials.append(material)
for p in joined.data.polygons: p.material_index = 0
# Validate actual exported triangulation and UVs, not only authoring quads.
joined.data.calc_loop_triangles()
for tri in joined.data.loop_triangles:
    a,b,c = [joined.data.vertices[i].co for i in tri.vertices]
    assert (b-a).cross(c-a).length_squared > 1e-12, 'Degenerate triangle'
    a,b,c = [joined.data.uv_layers.active.data[i].uv for i in tri.loops]
    assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)) > 1e-10, f'Collapsed UV: face {tri.polygon_index}, vertices {[list(joined.data.vertices[i].co) for i in tri.vertices]}, uv {a,b,c}'
assert all(0<=c<=1 for v in joined.data.uv_layers.active.data for c in v.uv), 'UV escaped atlas'
OUT = ROOT/'Assets/Models/Buildings/Wood/Roofs/ThatchRoofPanel_4.0Wx2.0Hx2.0D.fbx'
bpy.ops.export_scene.fbx(filepath=str(OUT),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',global_scale=1,
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',
    use_tspace=False,add_leaf_bones=False,path_mode='STRIP',bake_anim=False)
report = dict(bundles=bundle_count,straw_tips=bundle_count*3,fringe_cards=64,triangles=len(joined.data.loop_triangles),
    closed_opaque_components=True,fringe_boundary_edges=open_edges,nondegenerate_triangles_and_uvs=True,material_slots=1,
    bounds_blender=[list(map(min,zip(*[v.co for v in joined.data.vertices]))),list(map(max,zip(*[v.co for v in joined.data.vertices])))])
(ART/'geometry-report.json').write_text(json.dumps(report,indent=2))
joined.hide_set(True); joined.hide_render=True
bpy.ops.object.select_all(action='DESELECT')
parts[0].select_set(True); bpy.context.view_layer.objects.active=parts[0]
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print('LAYERED THATCH PASS',json.dumps(report))
