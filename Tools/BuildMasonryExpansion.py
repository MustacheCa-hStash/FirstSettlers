"""Editable flat masonry decks, square infill, spacious decorative arches and braced timber frame."""
import bpy, bmesh, math, json, random, hashlib, sys
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/'ArtReferences/MasonryExpansion';OUT=ROOT/'Assets/Models/Buildings/MasonryExpansion';TEX=ROOT/'Assets/Textures/Buildings/MasonryExpansion'
for p in (ART,OUT,TEX):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.render.engine='CYCLES';scene.cycles.samples=4
scene.render.threads_mode='FIXED';scene.render.threads=12;scene.view_settings.view_transform='Standard'
highs=bpy.data.collections.new('Editable individual stones and framing');scene.collection.children.link(highs)
lows=bpy.data.collections.new('Runtime export geometry');scene.collection.children.link(lows)
stone=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png'))
lime=bpy.data.images.load(str(ROOT/'ArtReferences/LimestoneBuildKit/Textures/limestone-timber-trim-source.png'))
wood_image=bpy.data.images.load(str(ROOT/'Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png'))
sys.path.insert(0,str(ROOT/'Tools'));from BuildingTrimUV import sample
def material(name,image=None,tint=(1,1,1)):
    m=bpy.data.materials.new(name);m.use_nodes=True;n=m.node_tree.nodes;l=m.node_tree.links;s=n.get('Principled BSDF')
    s.inputs['Roughness'].default_value=1;s.inputs['Specular IOR Level'].default_value=0;s.inputs['Base Color'].default_value=(*tint,1)
    if image:
        tx=n.new('ShaderNodeTexImage');tx.image=image;mix=n.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=(*tint,1)
        l.new(tx.outputs['Color'],mix.inputs[1]);l.new(mix.outputs[0],s.inputs['Base Color'])
    return m
palettes={style:[material(style+' stone '+str(i),image,(v,v*.99,v*.97)) for i,v in enumerate([.92,.97,1,1.04])] for style,image in [('stone',stone),('limestone',lime)]}
mortars={'stone':material('Recessed stone joints',tint=(.23,.22,.20)),'limestone':material('Recessed limestone joints',tint=(.43,.40,.33))}
wood=material('Meadow timber source',wood_image);runtime=material('Masonry expansion baked surface')
target=runtime.node_tree.nodes.new('ShaderNodeTexImage');runtime.node_tree.nodes.active=target
rng=random.Random(4523);groups=[];current=None
def make(name,verts,faces,col,mat,bevel=0):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,z,y) for x,y,z in verts],[],faces)
    ob=bpy.data.objects.new(name,mesh);col.objects.link(ob);mesh.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    if bevel:
        mod=ob.modifiers.new('Shallow cut edges','BEVEL');mod.width=bevel;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    assert all(e.is_manifold for e in bm.edges) and bm.calc_volume(signed=True)>0,name
    if col==lows:bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(mesh);bm.free();mesh.update();uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        axes=sorted(range(3),key=lambda a:abs(face.normal[a]))[:2]
        for li in face.loop_indices:
            v=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=(.035+(v[axes[0]]%1)*.17,.035+(v[axes[1]]%1)*.17)
    ob.location.x=current['offset'];return ob
def prism(poly,z0,z1,col,mat,bevel=0,axis='z'):
    n=len(poly);verts=[(x,y,z) for z in (z0,z1) for x,y in poly]
    if axis=='x':verts=[(z,y,x) for x,y,z in verts]
    return make(current['name'],verts,[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],col,mat,bevel)
def rect(a,b,c,d):return [(a,b),(c,b),(c,d),(a,d)]
def low(poly,z0=None,z1=None,axis='z'):
    depth=current['size'][2];ob=prism(poly,-depth/2 if z0 is None else z0,depth/2 if z1 is None else z1,lows,runtime,axis=axis);current['low'].append(ob);return ob
def high(poly,z0=None,z1=None,mat=None,bevel=.007,axis='z'):
    depth=current['size'][2];ob=prism(poly,-depth/2 if z0 is None else z0,depth/2 if z1 is None else z1,highs,mat or rng.choice(palettes[current['style']]),bevel,axis);current['high'].append(ob);return ob
def clip(poly,bound):
    for a,b in zip(bound,bound[1:]+bound[:1]):
        output=[];cross=lambda p:(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
        for p,q in zip(poly,poly[1:]+poly[:1]):
            dp,dq=cross(p),cross(q)
            if dp>=-1e-8:output.append(p)
            if (dp>=0)!=(dq>=0):
                t=dp/(dp-dq);output.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
        poly=output
        if len(poly)<3:return []
    clean=[]
    for p in poly:
        if not clean or math.dist(p,clean[-1])>1e-6:clean.append(p)
    if len(clean)>1 and math.dist(clean[0],clean[-1])<1e-6:clean.pop()
    area=sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(clean,clean[1:]+clean[:1]))/2
    return clean if len(clean)>=3 and area>1e-7 else []
def masonry(polys,width,height):
    depth=current['size'][2]
    for poly in polys:high(poly,-depth/2+.018,depth/2-.018,mortars[current['style']],0)
    for row in range(math.ceil(height/.3)):
        y=row*.3;top=min(height,y+.3)
        edges=sorted(set([0,width]+[max(0,min(width,i*.66+(.33 if row%2 else 0))) for i in range(-1,math.ceil(width/.66)+1)]))
        for a,b in zip(edges,edges[1:]):
            if b-a<.025:continue
            box=rect(a+(.006 if a else 0),y+(.006 if y else 0),b-(.006 if b<width else 0),top-(.006 if top<height else 0))
            pieces=[cut for boundary in polys if (cut:=clip(box,boundary))]
            # Decomposition segments belong to collision, not to the visible stone bond.
            # Merge pieces of each individual block before beveling/baking its surface.
            bm=bmesh.new();cache={}
            for cut in pieces:
                verts=[]
                for x,y0 in cut:
                    key=(round(x,6),round(y0,6))
                    if key not in cache:cache[key]=bm.verts.new((x,y0,0))
                    verts.append(cache[key])
                if len(set(verts))>=3:bm.faces.new(verts)
            if bm.faces:
                bm.normal_update();bmesh.ops.dissolve_edges(bm,edges=[e for e in bm.edges if len(e.link_faces)==2],use_verts=True)
                for face in bm.faces:
                    if face.calc_area()>1e-6:high([(v.co.x,v.co.y) for v in face.verts])
            bm.free()
def start(slug,name,kind,style,size,minimum,description):
    global current
    current=dict(slug=slug,name=name,kind=kind,style=style,size=size,minimum=minimum,description=description,offset=len(groups)*7,low=[],high=[])
    groups.append(current)
def solids(objects):
    result=[]
    for ob in objects:
        ob.data.calc_loop_triangles();result.append(dict(vertices=[dict(x=v.co.x,y=v.co.z,z=v.co.y) for v in ob.data.vertices],triangles=[i for t in ob.data.loop_triangles for i in t.vertices]))
    return result
def arch(r,spring,sill=0,bare=False):
    n=12;cx=2;depth=current['size'][2];angles=[math.pi-i*math.pi/n for i in range(n+1)]
    arc=lambda radius:[(cx+radius*math.cos(a),spring+radius*math.sin(a)) for a in angles]
    inner=arc(r);outer=arc(r+.25)
    sides=[rect(0,sill,cx-r,spring),rect(cx+r,sill,4,spring)]
    if sill:sides.append(rect(0,0,4,sill))
    for poly in sides:low(poly)
    masonry(sides,4,spring)
    spandrels=[]
    if not bare:
        for poly in [rect(0,spring,cx-r,3),rect(cx+r,spring,4,3)]:low(poly)
        spandrels.extend([rect(0,spring,cx-r-.25,3),rect(cx+r+.25,spring,4,3)])
    for i in range(n):
        a,b=inner[i],inner[i+1];oa,ob=outer[i],outer[i+1]
        if bare:low([a,b,ob,oa])
        else:
            low([a,b,(b[0],3),(a[0],3)])
            spandrels.append([oa,ob,(ob[0],3),(oa[0],3)])
        # Individual radial voussoirs are retained in the native source and baked into each arch ring.
        high([a,b,ob,oa],bevel=.006)
    if not bare:masonry(spandrels,4,3)
    current['opening']=[cx-r,sill,cx+r,spring];current['arch']=[cx,r,spring,sill]
def broad_arch_wall():
    # Preserve the spacious opening inside the same rectangular envelope as a normal wall.
    half=1.75;rise=.5;spring=2.25;thickness=.25;radius=(half*half+rise*rise)/(2*rise);cy=spring+rise-radius
    height=current['size'][1]
    sides=[rect(0,0,.25,height),rect(3.75,0,4,height)]
    for poly in sides:low(poly)
    masonry(sides,4,height)
    points=[(.25+3.5*i/20,0) for i in range(21)]
    points=[(x,cy+math.sqrt(max(0,radius*radius-(x-2)**2))) for x,_ in points]
    spandrels=[]
    for a,b in zip(points,points[1:]):
        low([a,b,(b[0],height),(a[0],height)])
        outer_a=(a[0],a[1]+thickness);outer_b=(b[0],b[1]+thickness)
        high([a,b,outer_b,outer_a],bevel=.006)
        cap=[]
        for point in [outer_a,outer_b,(b[0],height),(a[0],height)]:
            if not cap or math.dist(point,cap[-1])>1e-6:cap.append(point)
        if len(cap)>1 and math.dist(cap[0],cap[-1])<1e-6:cap.pop()
        if len(cap)>=3:spandrels.append(cap)
    masonry(spandrels,4,height)
    current['opening']=[.25,0,3.75,spring];current['arch']=[2,half,spring,0,rise]
def flat_deck(depth,rows):
    low(rect(0,-.25,4,0),0,depth)
    for row in range(rows):
        edges=sorted(set([0,4]+[max(0,min(4,i*.8+(.4 if row%2 else 0))) for i in range(7)]))
        for a,b in zip(edges,edges[1:]):
            if b-a>.02:high(rect(a+.004,-.25,b-.004,0),row*depth/rows+.004,(row+1)*depth/rows-.004)
for style,depth in [('stone',.5),('limestone',.25)]:
    title=style.capitalize()
    start(style+'-square-infill',title+' square wall infill',0,style,[2,2,depth],[0,0,-depth/2],
        'A 2 m square masonry panel matching the half-gable rise. Fill wider gable ends, loft walls and short upper bays.')
    low(rect(0,0,2,2));masonry([rect(0,0,2,2)],2,2);current['infill']=True
    start(style+'-half-floor',title+' half floor',1,style,[4,.25,2],[0,-.25,0],
        'A 4 by 2 m slab, 0.25 m deep below its walking surface. Pair panels for a full bay or leave a clear stair opening.')
    flat_deck(2,3)
    start(style+'-slab-roof',title+' flat roof',1,style,[4,.25,4],[0,-.25,0],
        'A walkable 4 by 4 m flat masonry roof deck, 0.25 m deep below its top surface. Uses floor placement and stays level when tiled.')
    flat_deck(4,6)
    start(style+'-open-arch',title+' broad arch wall',0,style,[4,3,depth],[0,0,-depth/2],
        'A standard-height 4 by 3 m masonry wall with a 3.5 m wide opening, spring at 2.25 m and inner crown at 2.75 m. Solid masonry fills above the arch to the level wall top.')
    broad_arch_wall()
for slug,name,spring,sill in [('stone-arched-doorway','Stone arched doorway wall',1.5,0),('stone-arched-window','Stone arched window wall',1.75,1)]:
    start(slug,name,0,'stone',[4,3,.5],[0,0,-.25],'A full stone wall with a true semicircular arch and radial stone ring. Fixed empty opening; no door, shutter or glass.')
    arch(.75,spring,sill)
start('braced-timber-frame','Open braced timber frame',0,'wood',[4,3,.25],[0,0,-.125],
    'An open 4 m timber bay with two diagonal knee braces and a broad header. Clear central passage; no curved wooden arch or moving parts.')
brace=[(.13,2.07),(.27,1.93),(1.07,2.73),(.93,2.87)]
members=[(rect(0,0,.25,3),1),(rect(3.75,0,4,3),1),(rect(.25,2.75,3.75,3),0),
    (brace,1),([(4-x,y) for x,y in brace],1)]
for member_index,(poly,grain) in enumerate(members):
    low_ob=low(poly);low_ob.data.materials[0]=wood;high_ob=high(poly,mat=wood,bevel=.004)
    for ob in (low_ob,high_ob):
        along=Vector((1 if member_index==3 else -1,1,0)).normalized() if member_index>=3 else None
        across=Vector((along.y,-along.x,0)) if along else None
        project=lambda p:Vector((p.dot(across),p.dot(along),p.z)) if along else p
        coords=[project(Vector((v.co.x,v.co.z,v.co.y))) for v in ob.data.vertices]
        lo=[min(v[a] for v in coords) for a in range(3)];hi=[max(v[a] for v in coords) for a in range(3)]
        for face in ob.data.polygons:
            normal=project(Vector((face.normal.x,face.normal.z,face.normal.y)))
            for li in face.loop_indices:ob.data.uv_layers.active.data[li].uv=sample(coords[ob.data.loops[li].vertex_index],normal,lo,hi,grain,len(current['high'])*7919)
current['opening']=[1.05,0,2.95,2.70]
# A single packed UV atlas for all low geometry; high objects stay independently editable.
bpy.ops.object.select_all(action='DESELECT')
for g in groups:
    if g['style']!='wood':
        for ob in g['low']:ob.select_set(True)
bpy.context.view_layer.objects.active=groups[0]['low'][0];bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.01,area_weight=.15,correct_aspect=True,scale_to_bounds=True)
bpy.ops.uv.pack_islands(rotate=True,margin=.006);bpy.ops.object.mode_set(mode='OBJECT')
def combine(objects,collection):
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for ob in objects:
        clone=ob.copy();clone.data=ob.data.copy();collection.objects.link(clone);clone.select_set(True);copies.append(clone);ob.hide_render=True
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();return bpy.context.object
bake_object=combine([o for g in groups if g['style']!='wood' for o in g['low']],lows)
source_object=combine([o for g in groups if g['style']!='wood' for o in g['high']],highs)
bake_object.select_set(True);bpy.context.view_layer.objects.active=bake_object
scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.055;scene.render.bake.max_ray_distance=.14;scene.render.bake.margin=10
scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
def bake(kind,space):
    image=bpy.data.images.new('Masonry expansion '+kind,width=2048,height=2048,alpha=True);image.colorspace_settings.name=space;target.image=image
    print('MASONRY EXPANSION BAKE',kind,flush=True);bpy.ops.object.bake(type=kind);return image
colour=bake('DIFFUSE','sRGB');normal=bake('NORMAL','Non-Color');ao=bake('AO','Non-Color')
p=np.empty(2048*2048*4,dtype=np.float32);q=np.empty_like(p);colour.pixels.foreach_get(p);ao.pixels.foreach_get(q);p[3::4]=np.clip(q[::4],0,1);colour.pixels.foreach_set(p)
for image,name in [(colour,'MasonryExpansion_ColourAO.png'),(normal,'MasonryExpansion_Normal.png')]:image.filepath_raw=str(TEX/name);image.file_format='PNG';image.save()
bpy.data.objects.remove(bake_object,do_unlink=True);bpy.data.objects.remove(source_object,do_unlink=True)
colour=bpy.data.images.load(str(TEX/'MasonryExpansion_ColourAO.png'),check_existing=False);colour.alpha_mode='CHANNEL_PACKED'
normal=bpy.data.images.load(str(TEX/'MasonryExpansion_Normal.png'),check_existing=False);normal.colorspace_settings.name='Non-Color';target.image=colour
nodes=runtime.node_tree.nodes;links=runtime.node_tree.links;links.new(target.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
nn=nodes.new('ShaderNodeTexImage');nn.image=normal;nm=nodes.new('ShaderNodeNormalMap');links.new(nn.outputs['Color'],nm.inputs['Color']);links.new(nm.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
manifest=[]
for index,g in enumerate(groups):
    info={k:v for k,v in g.items() if k not in ('low','high','offset')};info['solids']=g.get('solids') or solids(g['low'])
    ob=combine(g['low'],lows);ob.location=(0,0,0);ob.name=g['slug'];ob.data.calc_loop_triangles();info['triangles']=len(ob.data.loop_triangles)
    info['file']=g['slug']+'.fbx';bpy.ops.export_scene.fbx(filepath=str(OUT/info['file']),use_selection=True,object_types={'MESH'},axis_forward='Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
    bpy.data.objects.remove(ob,do_unlink=True);manifest.append(info)
    for ob in g['high']:ob.hide_render=True;ob.hide_set(True)
    for ob in g['low']:ob.location=(index%4*6,index//4*6,0);ob.hide_render=False
scene.world=bpy.data.worlds.new('Masonry component review');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.38,.38,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
bpy.ops.object.light_add(type='AREA',location=(5,-8,14));lamp=bpy.context.object;lamp.data.energy=2200;lamp.data.size=8;lamp.rotation_euler=(Vector((9,6,1))-lamp.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(30,-31,26));camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=32;camera.rotation_euler=(Vector((10,6,1))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=2200;scene.render.resolution_y=1500;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ART/'masonry-expansion-blender.png');bpy.ops.render.render(write_still=True)
bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0;blend=ART/'MasonryExpansion.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
(ART/'masonry-expansion-models.json').write_text(json.dumps(dict(parts=manifest,atlasSize=2048,blend_sha256=hashlib.sha256(blend.read_bytes()).hexdigest()),indent=2)+'\n')
print('MASONRY EXPANSION SOURCE PASS',[(p['slug'],p['triangles']) for p in manifest],flush=True)
