"""Inspect existing FBX UVs and render texture swaps without changing source meshes."""
import bpy, json, math, hashlib, sys
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).resolve().parent
MODELS = ROOT / 'Assets/Models/Buildings/Wood'
REGIONS = json.loads((OUT.parent/'atlas-layout.json').read_text())['regions']
bpy.ops.wm.read_factory_settings(use_empty=True)
report = {'sourceMeshEdits': False, 'uvEdits': False, 'models': []}
def load(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    return [o for o in set(bpy.data.objects)-before if o.type=='MESH']
for path in ([] if '--additional-samples' in sys.argv else sorted(MODELS.rglob('*.fbx'))):
    if 'Roofs' in path.parts: continue  # Roof uses its separate thatch atlas.
    obs = load(path)
    counts = dict.fromkeys(REGIONS, 0)
    unmatched = 0
    exceptions = []
    digest = hashlib.sha256()
    for obj in obs:
        uv = obj.data.uv_layers.active
        for poly in obj.data.polygons:
            points = [tuple(uv.data[i].uv) for i in poly.loop_indices]
            digest.update(repr(points).encode())
            matching = []
            for name, reg in REGIONS.items():
                u0,v0,u1,v1=reg['uv_texel_centres']
                # Full-width strips intentionally support global U Repeat, including 0..1 and offsets.
                repeat_u=reg.get('repeat')=='U'
                if all((repeat_u or u0-1e-6<=u<=u1+1e-6) and v0-1e-6<=v<=v1+1e-6 for u,v in points): matching.append(name)
            if len(matching)==1: counts[matching[0]]+=1
            else:
                unmatched+=1
                exceptions.append({'object':obj.name,'polygon':poly.index,
                    'uvBounds':[min(u for u,v in points),min(v for u,v in points),
                                max(u for u,v in points),max(v for u,v in points)]})
    report['models'].append({'file':str(path.relative_to(ROOT)), 'facesByContentRegion':counts,
                             'facesOutsideContentBounds':unmatched, 'exceptions':exceptions, 'uvSHA256':digest.hexdigest()})
    for obj in list(bpy.data.objects): bpy.data.objects.remove(obj, do_unlink=True)
if '--additional-samples' not in sys.argv:
    (OUT/'uv-validation.json').write_text(json.dumps(report,indent=2))
if '--audit-only' in sys.argv:
    print('UV_AUDIT_TOTAL',len(report['models']),sum(m['facesOutsideContentBounds'] for m in report['models']))
    sys.exit(0)

# Three copies of the EXACT same imported mesh; only the image input changes.
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1800;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('PreviewWorld');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.40,.52,.63,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.8
scene.view_settings.view_transform='Standard'
scene.view_settings.look='None'
scene.render.image_settings.file_format='PNG'
def material(name,path):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    nodes=mat.node_tree.nodes;bsdf=nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value=1
    bsdf.inputs['Specular IOR Level'].default_value=0
    tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(path),check_existing=True)
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    return mat
texroot=ROOT/'Assets/Textures/Buildings/Wood'
variants=[('CURRENT', 'ordinary-wood-trim-albedo.png'),('SOFT HEWN OAK','ordinary-wood-trim-soft-hewn-oak.png'),
          ('WEATHERED WOODLAND','ordinary-wood-trim-weathered-woodland.png')]
if '--additional-samples' in sys.argv:
    variants=[('SOFT HEWN OAK','ordinary-wood-trim-soft-hewn-oak.png'),
              ('GOLDEN OAK','ordinary-wood-trim-golden-oak.png'),
              ('MEADOW TIMBER','ordinary-wood-trim-meadow-timber.png')]
wall=MODELS/'Modular/SplitPlankWall_4.00Wx3.00Hx0.25D.fbx'
for i,(label,file) in enumerate(variants):
    mat=material(label,texroot/file)
    obs=load(wall)
    for o in obs:
        o.location.x+=i*5
        o.data.materials.clear();o.data.materials.append(mat)
    # Text is preview annotation only.
    cu=bpy.data.curves.new(label,'FONT');cu.body=label;cu.align_x='CENTER';cu.size=.25
    text=bpy.data.objects.new(label,cu);scene.collection.objects.link(text)
    text.location=(i*5+2,-.25,3.4);text.rotation_euler=(math.pi/2,0,0)
    tm=bpy.data.materials.get('Labels')
    if not tm:
        tm=bpy.data.materials.new('Labels');tm.diffuse_color=(.04,.07,.08,1)
    cu.materials.append(tm)
bpy.ops.mesh.primitive_plane_add(size=200, location=(5,0,-.03))
ground=bpy.context.object;gm=bpy.data.materials.new('Ground');gm.diffuse_color=(.19,.30,.14,1);ground.data.materials.append(gm)
bpy.ops.object.light_add(type='AREA',location=(4,-6,10));light=bpy.context.object
light.data.energy=1600;light.data.shape='DISK';light.data.size=8
light.rotation_euler=(Vector((6,0,1))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(9,-17,7));cam=bpy.context.object
cam.rotation_euler=(Vector((7,0,1.9))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=16;scene.camera=cam
additional='--additional-samples' in sys.argv
scene.render.filepath=str(OUT/('additional-wall-texture-swap.png' if additional else 'existing-wall-texture-swap.png'))
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('additional-texture-swap-preview.blend' if additional else 'texture-swap-preview.blend')))
if additional:
    print('Additional samples rendered; existing UV audit retained unchanged.')
else:
    print('UV_AUDIT_TOTAL',len(report['models']),sum(m['facesOutsideContentBounds'] for m in report['models']))
