"""Blender offline selected-to-active bake; preserves all detailed authoring sources.
blender --background --python Tools/BakeTorchAndBed.py
"""
import bpy, bmesh, math, json, hashlib, re
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/BakedFurnishings'; ART.mkdir(parents=True,exist_ok=True)
sources=[ROOT/'ArtReferences/Torch/TorchKit.blend',ROOT/'ArtReferences/IronAgeFurniture/Models/FurnitureKit.blend']
hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}

def bake(kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.render.threads_mode='FIXED';scene.render.threads=12
    torch=kind=='MountedTorch';size=512 if torch else 1024;density=384 if torch else 160;gap=6
    with bpy.data.libraries.load(str(sources[0 if torch else 1]),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if (not n.startswith(('Mounted','Hand')) if torch else n.startswith('F05 '))]
    high=[o for o in dst.objects if o is not None and o.type=='MESH']
    assert len(high)==(15 if torch else 64),(kind,[(o.name) for o in high])
    for o in high:
        scene.collection.objects.link(o);o.location=(0,0,0);o.hide_set(False);o.hide_render=False
        # Resolve external authoring images against this checkout.
        for mat in o.data.materials:
            if mat and mat.use_nodes:
                for n in mat.node_tree.nodes:
                    if n.type=='TEX_IMAGE' and n.image:
                        p=next(iter((ROOT/'Assets/Textures/Buildings').rglob(Path(n.image.filepath).name)),None)
                        if p:n.image.filepath=str(p);n.image.reload()
    verts=[];faces=[]
    def add(v,f,closed=True):
        data=bpy.data.meshes.new('part');data.from_pydata([(p[0],p[2],p[1]) for p in v],[],f)
        if closed:
            bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(data);bm.free()
        offset=len(verts);verts.extend([tuple(v.co) for v in data.vertices]);faces.extend([tuple(offset+i for i in p.vertices) for p in data.polygons]);bpy.data.meshes.remove(data)
    def box(lo,hi):
        v=[(x,y,z) for z in (lo[2],hi[2]) for y in (lo[1],hi[1]) for x in (lo[0],hi[0])]
        add(v,[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)])
    def rod(a,b,r,n,top=None):
        a,b=Vector(a),Vector(b);axis=(b-a).normalized();t=axis.cross(Vector((0,1,0)))
        if t.length<.01:t=Vector((1,0,0))
        t.normalize();other=axis.cross(t)
        v=[tuple(c+(t*math.cos(i*math.tau/n)+other*math.sin(i*math.tau/n))*rad) for c,rad in [(a,r),(b,top or r)] for i in range(n)]
        add(v,[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)])
    if torch:
        rod((0,.015,.32),(0,.665,.32),.027,6,.033)
        for i in range(5):
            a=i*math.tau/5;rod((.022*math.cos(a),.60,.32+.022*math.sin(a)),(.047*math.cos(a),.745-(i%3)*.014,.32+.047*math.sin(a)),.018,5,.023)
        rod((0,.5805,.32),(0,.6945,.32),.0565,8)
        rod((0,.1565,.32),(0,.2485,.32),.0335,8)
        box((-.08,.14,0),(.08,.49,.038));box((-.07,.265,.038),(.07,.305,.34))
        rod((0,.155,.035),(0,.275,.275),.016,6)
        for x in [-.057,.057]:rod((x,.295,.32),(x,.36,.32),.013,6)
        for x,y in [(-.025,.425),(.025,.21)]:rod((x,y,.038),(x,y,.06),.011,6)
    else:
        for x in [-.95,.95]:
            for z in [-.475,.475]:box((x-.05,0,z-.025),(x+.05,.5,z+.025))
        for z in [-.475,.475]:box((-.9,.36,z-.025),(.9,.48,z+.025))
        for x in [-.95,.95]:box((x-.05,.36,-.45),(x+.05,.48,.45))
        # Separate upward/downward quads give physical holes and both underside views.
        # The 6 mm separation keeps the two woven courses distinct without closed tubes.
        for i in range(19):
            z=-.435+i*.87/18
            for sign in [-1,1]:
                y=.463+sign*.003
                add([(-.95,y,z-.006),(.95,y,z-.006),(.95,y,z+.006),(-.95,y,z+.006)],[(0,1,2,3) if sign==1 else (3,2,1,0)],False)
        for i in range(37):
            x=-.89+i*1.78/36
            for sign in [-1,1]:
                y=.476+sign*.003
                add([(x-.006,y,-.48),(x+.006,y,-.48),(x+.006,y,.48),(x-.006,y,.48)],[(0,1,2,3) if sign==1 else (3,2,1,0)],False)
    data=bpy.data.meshes.new(kind+'_Baked');data.from_pydata(verts,[],faces);data.update()
    low=bpy.data.objects.new(data.name,data);scene.collection.objects.link(low)
    uv=data.uv_layers.new(name='UVMap');requests=[];coords={}
    for p in data.polygons:
        origin=data.vertices[p.vertices[0]].co;t=(data.vertices[p.vertices[1]].co-origin).normalized();b=p.normal.cross(t).normalized()
        points=[((data.vertices[data.loops[i].vertex_index].co-origin).dot(t),(data.vertices[data.loops[i].vertex_index].co-origin).dot(b)) for i in p.loop_indices]
        mins=[min(v[a] for v in points) for a in range(2)];ext=[max(v[a] for v in points)-mins[a] for a in range(2)]
        coords[p.index]=[( (v[0]-mins[0])/ext[0],(v[1]-mins[1])/ext[1]) for v in points]
        requests.append((max(2,math.ceil(ext[0]*density)),max(2,math.ceil(ext[1]*density)),p.index))
    free=[(gap,gap,size-2*gap,size-2*gap)]
    for width,height,index in sorted(requests,key=lambda r:max(r[:2]),reverse=True):
        w,h=width+2*gap,height+2*gap
        options=[(min(fw-w,fh-h),max(fw-w,fh-h),x,y) for x,y,fw,fh in free if w<=fw and h<=fh]
        assert options,('Atlas overflow',kind,width,height)
        _,_,x,y=min(options);split=[]
        for fx,fy,fw,fh in free:
            if x>=fx+fw or x+w<=fx or y>=fy+fh or y+h<=fy:split.append((fx,fy,fw,fh));continue
            if x>fx:split.append((fx,fy,x-fx,fh))
            if x+w<fx+fw:split.append((x+w,fy,fx+fw-x-w,fh))
            if y>fy:split.append((fx,fy,fw,y-fy))
            if y+h<fy+fh:split.append((fx,y+h,fw,fy+fh-y-h))
        free=[a for n,a in enumerate(split) if not any(n!=m and a[0]>=b[0] and a[1]>=b[1] and a[0]+a[2]<=b[0]+b[2] and a[1]+a[3]<=b[1]+b[3] and (a!=b or n>m) for m,b in enumerate(split))]
        for li,(u,v) in zip(data.polygons[index].loop_indices,coords[index]):uv.data[li].uv=((x+gap+u*width)/size,(y+gap+v*height)/size)
    mat=bpy.data.materials.new(kind+' Bake');mat.use_nodes=True;data.materials.append(mat)
    target=mat.node_tree.nodes.new('ShaderNodeTexImage');mat.node_tree.nodes.active=target
    bpy.ops.object.select_all(action='DESELECT')
    for o in high:o.select_set(True)
    low.select_set(True);bpy.context.view_layer.objects.active=low
    scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.018 if torch else .008
    scene.render.bake.max_ray_distance=.08 if torch else .02;scene.render.bake.margin=gap
    scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
    images={}
    for channel in ['DIFFUSE','NORMAL','AO']:
        image=bpy.data.images.new(kind+channel,width=size,height=size,alpha=True)
        image.colorspace_settings.name='sRGB' if channel=='DIFFUSE' else 'Non-Color'
        image.generated_color=(.5,.5,1,1) if channel=='NORMAL' else (.3,.3,.3,1)
        target.image=image;print('BAKE',kind,channel,flush=True);bpy.ops.object.bake(type=channel);images[channel]=image
    pixels=np.empty(size*size*4,dtype=np.float32);ao=np.empty_like(pixels)
    images['DIFFUSE'].pixels.foreach_get(pixels);images['AO'].pixels.foreach_get(ao);pixels[3::4]=np.clip(ao[0::4],0,1);images['DIFFUSE'].pixels.foreach_set(pixels)
    folder='Lighting' if torch else 'Furniture';out=ROOT/'Assets/Models/Buildings'/folder;tex=ROOT/'Assets/Textures/Buildings'/folder
    out.mkdir(parents=True,exist_ok=True);tex.mkdir(parents=True,exist_ok=True)
    for channel,suffix in [('DIFFUSE','ColourAO'),('NORMAL','Normal')]:
        image=images[channel];image.filepath_raw=str(tex/(kind+'_Baked_'+suffix+'.png'));image.file_format='PNG';image.save()
    bpy.ops.object.select_all(action='DESELECT');low.select_set(True);bpy.context.view_layer.objects.active=low
    # Triangulate before export so the tangent bake and Unity agree on diagonal orientation.
    mod=low.modifiers.new('Export triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.export_scene.fbx(filepath=str(out/(kind+'_Baked.fbx')),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=True,path_mode='STRIP',bake_anim=False)
    data=low.data;data.calc_loop_triangles();count=len(data.loop_triangles);assert count==(280 if torch else 320),count
    return dict(piece=kind,before=1996 if torch else 1472,after=count,atlas=size)
report={'sources':hashes,'models':[bake('MountedTorch'),bake('SleepingPlatform')]}
for p in sources:assert hashlib.sha256(p.read_bytes()).hexdigest()==hashes[str(p.relative_to(ROOT))]
(ART/'bake-report.json').write_text(json.dumps(report,indent=2))
print('FURNISHING_BAKE_PASS',json.dumps(report),flush=True)

