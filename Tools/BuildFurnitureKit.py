"""Build F01-F06 as editable Blender parts, complete atlas UVs and Unity-axis FBXs."""
import bpy,bmesh,math,json,hashlib,random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/IronAgeFurniture/Models'
OUT=ROOT/'Assets/Models/Buildings/Furniture'
ART.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
editable=bpy.data.collections.new('Furniture — editable individual parts')
exports=bpy.data.collections.new('Furniture — joined exports')
bpy.context.scene.collection.children.link(editable);bpy.context.scene.collection.children.link(exports)
texture=ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png'
image=bpy.data.images.load(str(texture));image.pack()
mat=bpy.data.materials.new('Meadow Timber — wood and cord UV0');mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=1;bs.inputs['Specular IOR Level'].default_value=0
node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
mat.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color'])
counter=0

def uv_map(obj,grain=0,rope=False):
    global counter
    counter+=1;rng=random.Random(counter*7919)
    m=obj.data;uv=m.uv_layers.new(name='UVMap')
    points=[Vector((v.co.x,v.co.z,v.co.y)) for v in m.vertices]
    lo=Vector(tuple(min(p[a] for p in points) for a in range(3)))
    hi=Vector(tuple(max(p[a] for p in points) for a in range(3)))
    for face in m.polygons:
        n=Vector((face.normal.x,face.normal.z,face.normal.y))
        dominant=max(range(3),key=lambda a:abs(n[a]))
        axes=[a for a in range(3) if a!=dominant]
        cap=dominant==grain
        if cap:
            a,b=axes
            scale=min(.32,.18/max(hi[a]-lo[a],hi[b]-lo[b],.001))
            ubase=(.02 if rng.random()<.5 else .27)+rng.random()*.012
            vbase=.274+rng.random()*.015
        else:
            a=grain;b=next(x for x in axes if x!=grain)
            span=min(.85,max(.025,(hi[a]-lo[a])/3))
            ubase=.025+rng.random()*(.94-span)
            vbase=(.891 if counter%2 else .766)+rng.random()*.004
        for li in face.loop_indices:
            p=points[m.loops[li].vertex_index]
            if cap:u,v=ubase+(p[a]-lo[a])*scale,vbase+(p[b]-lo[b])*scale
            else:
                u=ubase+(p[a]-lo[a])/max(hi[a]-lo[a],.00001)*span
                v=vbase+(p[b]-lo[b])*min(.2,.085/max(hi[b]-lo[b],.00001))
                if rope:u=.02+(p[a]-lo[a])/max(hi[a]-lo[a],.00001)*.45;v=.143+(p[b]-lo[b])/max(hi[b]-lo[b],.00001)*.08
            uv.data[li].uv=(u,v)
    obj['grain_axis_unity']=grain;obj['rope_uv']=rope

def mesh(name,verts,faces,grain=0,bevel=0,rope=False):
    me=bpy.data.meshes.new(name);me.from_pydata([(x,z,y) for x,y,z in verts],[],faces);me.update()
    ob=bpy.data.objects.new(name,me);editable.objects.link(ob)
    bpy.context.view_layer.objects.active=ob
    if bevel:
        mod=ob.modifiers.new('One-cut hewn edges','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free();me.update()
    me.materials.append(mat);uv_map(ob,grain,rope);return ob

def box(name,lo,hi,grain=0,bevel=.004):
    verts=[(lo[0],lo[1],lo[2]),(hi[0],lo[1],lo[2]),(hi[0],hi[1],lo[2]),(lo[0],hi[1],lo[2]),
           (lo[0],lo[1],hi[2]),(hi[0],lo[1],hi[2]),(hi[0],hi[1],hi[2]),(lo[0],hi[1],hi[2])]
    return mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)],grain,bevel)

def prism(name,poly,y0,y1,grain=0):
    n=len(poly);verts=[(x,y,z) for y in (y0,y1) for x,z in poly]
    return mesh(name,verts,[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],grain,.003)

def rod(name,a,b,radius=.015,sides=6,rope=False):
    a,b=Vector(a),Vector(b);axis=(b-a).normalized()
    tangent=axis.cross(Vector((0,1,0)))
    if tangent.length<.01:tangent=axis.cross(Vector((1,0,0)))
    tangent.normalize();other=axis.cross(tangent).normalized()
    vs=[tuple(center+radius*(math.cos(i*math.tau/sides)*tangent+math.sin(i*math.tau/sides)*other)) for center in (a,b) for i in range(sides)]
    fs=[tuple(range(sides-1,-1,-1)),tuple(range(sides,2*sides))]+[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    grain=max(range(3),key=lambda i:abs(axis[i]))
    return mesh(name,vs,fs,grain,rope=rope)

def leg(name,x,z,tx,tz,height,r=.036):
    sides=8
    vs=[(cx+r*math.cos(i*math.tau/sides),y,cz+r*math.sin(i*math.tau/sides)) for cx,cz,y in [(x,z,0),(tx,tz,height)] for i in range(sides)]
    return mesh(name,vs,[tuple(range(sides-1,-1,-1)),tuple(range(sides,2*sides))]+[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],1)

def clip(poly,zvalue,above):
    result=[]
    for p,q in zip(poly,poly[1:]+poly[:1]):
        dp=(p[1]-zvalue)*(1 if above else -1);dq=(q[1]-zvalue)*(1 if above else -1)
        if dp>=0:result.append(p)
        if (dp>=0)!=(dq>=0):
            t=dp/(dp-dq);result.append((p[0]+t*(q[0]-p[0]),zvalue))
    return result

def stool():
    circle=[(.23*math.cos(i*math.tau/16),.21*math.sin(i*math.tau/16)) for i in range(16)]
    parts=[]
    for i,(lo,hi) in enumerate([(-.21,-.072),(-.068,.068),(.072,.21)]):
        poly=clip(clip(circle,lo,True),hi,False)
        parts.append(prism('F01 split seat '+str(i),poly,.44,.5))
    for i,(x,z,tx,tz) in enumerate([(-.214,-.214,-.12,-.10),(.214,-.214,.12,-.10),(0,.214,0,.12)]):
        parts.append(leg('F01 splayed leg '+str(i),x,z,tx,tz,.46))
    return parts

def bench():
    p=[box('F02 seat board '+str(i),(-.75,.43,z),(.75,.5,z+.247)) for i,z in enumerate([-.25,.003])]
    for x in [-.56,.56]:
        p.append(box('F02 slab support', (x-.065,0,-.22),(x+.065,.45,.22),1))
        p.append(rod('F02 wooden through peg',(x-.083,.34,-.15),(x+.083,.34,-.15),.018,8))
    p.append(box('F02 stretcher',(-.62,.20,-.035),(.62,.27,.035)))
    return p

def table(tag,width,height):
    d=.75;top=height-.07;p=[]
    for i in range(4):
        z=-d/2+i*d/4;p.append(box(tag+' split top '+str(i),(-width/2,top,z),(width/2,height,z+d/4-(.003 if i<3 else 0))))
    lx=width/2-.125;lz=.265
    for x in [-lx,lx]:
        for z in [-lz,lz]:p.append(box(tag+' leg',(x-.045,0,z-.045),(x+.045,top+.01,z+.045),1))
        p.append(box(tag+' end rail',(x-.047,top-.105,-.31),(x+.047,top+.01,.31),2))
        p.append(rod(tag+' through peg',(x-.06,top-.05,-.20),(x+.06,top-.05,-.20),.015,8))
    p.append(box(tag+' pegged stretcher',(-lx-.03,.17,-.04),(lx+.03,.24,.04),0))
    for z in [-.28,.28]:p.append(box(tag+' long apron',(-lx,top-.095,z-.03),(lx,top,z+.03)))
    return p

def bed():
    p=[]
    for x in [-.95,.95]:
        for z in [-.475,.475]:p.append(box('F05 corner leg',(x-.05,0,z-.025),(x+.05,.5,z+.025),1,.003))
    for z in [-.475,.475]:p.append(box('F05 side rail',(-.9,.36,z-.025),(.9,.48,z+.025)))
    for x in [-.95,.95]:p.append(box('F05 end rail',(x-.05,.36,-.45),(x+.05,.48,.45),2))
    for i in range(19):
        z=-.435+i*.87/18;p.append(rod('F05 long cord '+str(i),(-.95,.463,z),(.95,.463,z),.006,6,True))
    for i in range(37):
        x=-.89+i*1.78/36;p.append(rod('F05 cross cord '+str(i),(x,.476,-.48),(x,.476,.48),.006,6,True))
    return p

def shelf():
    p=[box('F06 shelf board',(-.5,-.05,0),(.5,0,.25))]
    for x in [-.35,.35]:
        p.append(box('F06 back bracket',(x-.032,-.25,0),(x+.032,-.045,.045),1))
        # Diagonal pegged brace; four sides keep the silhouette simple.
        p.append(rod('F06 diagonal bracket',(x,-.217,.035),(x,-.066,.215),.026,4))
        p.append(rod('F06 bracket peg',(x-.045,-.10,.03),(x+.045,-.10,.03),.012,8))
    return p

def bounds_boxes(*rows):return [{'center':list(c),'size':list(s)} for c,s in rows]
spec=[
 ('F01','f01-three-leg-stool','Three-leg stool',[.5,.5,.5],[-.25,0,-.25],stool,
  bounds_boxes(((0,.47,0),(.46,.06,.42)),((-.167,.22,-.157),(.166,.44,.186)),((.167,.22,-.157),(.166,.44,.186)),((0,.22,.167),(.072,.44,.166))),
  [[-.214,0,-.214],[.214,0,-.214],[0,0,.214]]),
 ('F02','f02-plank-bench','Plank bench',[1.5,.5,.5],[-.75,0,-.25],bench,
  bounds_boxes(((0,.465,0),(1.5,.07,.5)),((-.56,.215,0),(.13,.43,.44)),((.56,.215,0),(.13,.43,.44)),((0,.235,0),(1.24,.07,.07))),
  [[x,0,z] for x in [-.56,.56] for z in [-.17,.17]]),
 ('F03','f03-pegged-table','Pegged table',[1.5,.75,.75],[-.75,0,-.375],lambda:table('F03',1.5,.75),None,
  [[x,0,z] for x in [-.625,.625] for z in [-.265,.265]]),
 ('F04','f04-low-work-table','Low work table',[1,.5,.75],[-.5,0,-.375],lambda:table('F04',1,.5),None,
  [[x,0,z] for x in [-.375,.375] for z in [-.265,.265]]),
 ('F05','f05-sleeping-platform','Sleeping platform',[2,.5,1],[-1,0,-.5],bed,None,
  [[x,0,z] for x in [-.95,.95] for z in [-.475,.475]]),
 ('F06','f06-wall-shelf','Wall shelf',[1,.25,.25],[-.5,-.25,0],shelf,
  bounds_boxes(((0,-.025,.125),(1,.05,.25)),((-.35,-.15,.025),(.07,.2,.05)),((.35,-.15,.025),(.07,.2,.05)),((-.35,-.14,.13),(.06,.17,.20)),((.35,-.14,.13),(.06,.17,.20))),
  [[-.35,-.11,0],[.35,-.11,0],[-.35,-.22,0],[.35,-.22,0]])]
manifest={'texture':str(texture.relative_to(ROOT)),'texture_sha256':hashlib.sha256(texture.read_bytes()).hexdigest(),'models':[]}
groups=[]
for k,(tag,slug,name,size,minimum,maker,colliders,points) in enumerate(spec):
    parts=maker()
    if colliders is None:
        colliders=[]
        if tag in ['F03','F04']:
            w,h,d=size;lx=w/2-.125
            colliders+=bounds_boxes(((0,h-.035,0),(w,.07,d)))
            colliders+=bounds_boxes(*[((x,(h-.07)/2,z),(.09,h-.07,.09)) for x in [-lx,lx] for z in [-.265,.265]])
            colliders+=bounds_boxes(((0,.205,0),(2*lx,.07,.08)))
        else:
            colliders+=bounds_boxes(((0,.465,0),(1.8,.042,.9)))
            colliders+=bounds_boxes(*[((x,.25,z),(.1,.5,.05)) for x in [-.95,.95] for z in [-.475,.475]])
            colliders+=bounds_boxes(*[((0,.42,z),(1.8,.12,.05)) for z in [-.475,.475]])
            colliders+=bounds_boxes(*[((x,.42,0),(.1,.12,.9)) for x in [-.95,.95]])
    bpy.ops.object.select_all(action='DESELECT')
    copies=[]
    for src in parts:
        ob=src.copy();ob.data=src.data.copy();exports.objects.link(ob);ob.select_set(True);copies.append(ob)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;joined.name=tag+'_'+name.replace(' ','')
    tris=0;uv=joined.data.uv_layers.active
    for face in joined.data.polygons:
        coords=[uv.data[i].uv for i in face.loop_indices]
        # Validate triangulated fan area, all bevel faces and cap faces included.
        for i in range(1,len(coords)-1):
            a,b,c=coords[0],coords[i],coords[i+1]
            assert abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))>1e-12,(tag,face.index)
        assert all(0<=p.x<=1 and 0<=p.y<=1 for p in coords)
        tris+=len(face.vertices)-2
    filename=tag+'_'+slug[4:].replace('-','_')+'.fbx'
    bpy.ops.export_scene.fbx(filepath=str(OUT/filename),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,use_tspace=False,path_mode='STRIP',bake_anim=False)
    joined.hide_set(True);joined.hide_render=True
    offset=Vector(((k%3)*2.5,(k//3)*2.1,0))
    for ob in parts:ob.location=offset
    groups.append((tag,parts,offset))
    manifest['models'].append({'tag':tag,'content_id':'build.furniture.'+slug,'slug':slug,'name':name,'file':filename,'size':size,'minimum':minimum,'triangles':tris,'parts':len(parts),'support_points':[dict(zip(['x','y','z'],point)) for point in points],'solid_boxes':colliders,'wall_mounted':tag=='F06'})
exports.hide_render=True
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1800;scene.render.resolution_y=1150;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.world=bpy.data.worlds.new('Furniture studio');scene.world.color=(.45,.45,.45)
bpy.ops.object.light_add(type='AREA',location=(2,-3,7));light=bpy.context.object;light.data.energy=1700;light.data.size=8
bpy.ops.object.camera_add(location=(7,-9,8));cam=bpy.context.object;cam.rotation_euler=(Vector((2.5,1,.2))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=8;scene.camera=cam
scene.render.filepath=str(ART/'furniture-kit-overview.png');bpy.ops.render.render(write_still=True)
(ART/'furniture-models.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
# Pack the approved image, keeping the source conveniently editable with each small component separate.
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'FurnitureKit.blend'))
print('FURNITURE_MODEL_PASS',json.dumps([{k:m[k] for k in ['tag','file','triangles','parts']} for m in manifest['models']]))

