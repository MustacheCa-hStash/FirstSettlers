"""SS13 and SS15-SS24, with short wall/coping and a coping turn. Independent bake.

Shares the original kit's editable sandstone authoring helpers and palette without
executing or regenerating the original assets. Runtime shells omit internal faces.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
base = (ROOT / 'Tools/BuildSandstoneKit.py').read_text()
prefix = base.split('descriptions = [', 1)[0]
prefix = prefix.replace("ArtReferences/SandstoneBuildKit/Models", "ArtReferences/SandstoneBuildKit/Extension")
prefix = prefix.replace("Buildings/Sandstone'", "Buildings/SandstoneExtension'")
exec(compile(prefix, str(ROOT / 'Tools/BuildSandstoneKit.py'), 'exec'))
rng.seed(30124)

def low_prism(poly, a, b, axis='z'):
    v, f = prism_data(poly, a, b)
    if axis == 'x': v = [(z,y,x) for x,y,z in v]
    return add_low(v, f)

def convex_prism(poly, a, b, axis='z'):
    v, f = prism_data(poly,a,b)
    if axis == 'x': v = [(z,y,x) for x,y,z in v]
    hull(v,f)

def high_prism(name,poly,a,b,axis='z',bevel=.008):
    v,f=prism_data(poly,a,b)
    if axis=='x':v=[(z,y,x) for x,y,z in v]
    ob=make(current['tag']+'_'+name,v,f,highs,rng.choice(palette),bevel)
    current['high'].append(ob)
    return ob

def begin(tag,slug,name,kind,size,minimum,description,**extra):
    start(tag,slug,name,kind,size,minimum,description)
    current.update(extra)

def ring_box(width,height,depth,rim,bottom=None):
    # Coordinates are X/Z in a horizontal loop, extruded up Y.
    outer=rect(0,0,width,depth);inside=rect(rim,rim,width-rim,depth-rim)
    v=[(x,y,z) for y,loop in [(0,outer),(height,outer),(height,inside),
                             (0 if bottom is None else bottom,inside)] for x,z in loop]
    faces=[]
    for i in range(4):
        j=(i+1)%4
        faces += [(i,i+4,j+4,j),(i+4,i+8,j+8,j+4),(i+8,i+12,j+12,j+8)]
        if bottom is None:faces.append((i,i+12,j+12,j))
    if bottom is not None:faces += [(0,1,2,3),(12,13,14,15)]
    add_low(v,faces)
    if bottom is not None:hull(*box_data((0,0,0),(width,bottom,depth)))
    for lo,hi in [((0,bottom or 0,0),(rim,height,depth)),((width-rim,bottom or 0,0),(width,height,depth)),
                  ((rim,bottom or 0,0),(width-rim,height,rim)),((rim,bottom or 0,depth-rim),(width-rim,height,depth))]:hull(*box_data(lo,hi))

# SS13, plus its catalogued short length.
for width,tag,slug in [(4,'SS13','ss13-low-sandstone-wall'),(2,'SS13S','ss13-short-sandstone-wall')]:
    begin(tag,slug,'SS13 '+('low' if width==4 else 'short')+' sandstone court wall',0,[width,1.25,.5],[0,0,-.25],
          f'A {width:g} m sandstone court wall, 1.25 m tall. Add SS15 coping for a 1.5 m top.',ground=True)
    data=box_data((0,0,-.25),(width,1.25,.25));add_low(*data);hull(*data)
    masonry(width,1.25)

# Eight alternating long/short capstones per full length. One connected shell.
for length,tag,slug in [(4,'SS15','ss15-alternating-sandstone-coping'),(2,'SS15S','ss15-short-sandstone-coping')]:
    begin(tag,slug,'SS15 '+('alternating' if length==4 else 'short alternating')+' sandstone coping',0,
          [length,.25,.5],[0,0,-.25],f'A {length:g} m coping with alternating taller and flatter caps. Adds 0.25 m above a wall.')
    intervals=[]
    for i in range(int(length)):
        intervals += [(i,i+.25,.25),(i+.25,i+1,.1875)]
    profile=[(0,0),(length,0)]
    for a,b,h in reversed(intervals):profile += [(b,h),(a,h)]
    # Remove consecutive duplicates only; vertical changes define the silhouette.
    clean=[]
    for p in profile:
        if not clean or p!=clean[-1]:clean.append(p)
    low_prism(clean,-.25,.25)
    for i,(a,b,h) in enumerate(intervals):
        hull(*box_data((a,0,-.25),(b,h,.25)))
        high_box('Cap%d'%i,(a+(0 if i==0 else .004),0,-.25),(b-(0 if i==len(intervals)-1 else .004),h,.25),bevel=.007)

begin('SS15C','ss15-sandstone-coping-turn','SS15 sandstone coping turn',0,[1,.25,1],[0,0,-.25],
      'A plain 90-degree coping return. Finished end faces join straight runs; 0.5 m-wide arms.')
plan=[(0,-.25),(1,-.25),(1,.75),(.5,.75),(.5,.25),(0,.25)]
v,f=prism_data(plan,0,.25);v=[(x,z,y) for x,y,z in v];add_low(v,f)
for lo,hi in [((0,0,-.25),(1,.25,.25)),((.5,0,.25),(1,.25,.75))]:hull(*box_data(lo,hi))
ob=make('SS15C_cut_corner_stone',v,f,highs,palette[1],.008);current['high'].append(ob)

begin('SS16','ss16-sandstone-gatepost','SS16 sandstone court gatepost',3,[.75,1.5,.75],[-.375,0,-.375],
      'A broad 1.5 m sandstone gatepost with a plain chamfered cap. Gate leaf separate.',ground=True)
v=[(x,y,z) for y,half in [(0,.375),(1.375,.375),(1.5,.3125)] for x,z in [(-half,-half),(half,-half),(half,half),(-half,half)]]
f=[(0,1,2,3),(8,9,10,11)]+[(i+4*r,(i+1)%4+4*r,(i+1)%4+4*(r+1),i+4*(r+1)) for r in range(2) for i in range(4)]
add_low(v,f);hull(v,f);core()
for row in range(5):high_box('PierCourse%d'%row,(-.375,row*.25+.004,-.375),(.375,(row+1)*.25-.004,.375))
high_box('CapBase',(-.375,1.25,-.375),(.375,1.375,.375),bevel=.006)
ob=make('SS16_weather_cap',[(x,y,z) for x,y,z in v[4:]],[(0,1,2,3),(4,5,6,7)]+[(i,(i+1)%4,(i+1)%4+4,i+4) for i in range(4)],highs,palette[4],.006)
current['high'].append(ob)

begin('SS17','ss17-sandstone-slab-bench','SS17 sandstone slab bench',7,[2,.5,.5],[0,0,0],
      'A broad stone seat on two low feet. Open space remains between the supports.')
profile=[(0,0),(.25,0),(.25,.375),(1.75,.375),(1.75,0),(2,0),(2,.5),(0,.5)]
low_prism(profile,0,.5)
for lo,hi in [((0,0,0),(.25,.375,.5)),((1.75,0,0),(2,.375,.5)),((0,.375,0),(2,.5,.5))]:
    hull(*box_data(lo,hi));high_box('BenchStone',lo,hi)

begin('SS18','ss18-carved-sandstone-trough','SS18 carved sandstone trough',7,[2,.5,.75],[0,0,0],
      'A hollow stone trough with a 1.75 by 0.5 m basin, 0.25 m deep. No water simulation.')
ring_box(2,.5,.75,.125,.25)
ob=current['low'].copy();ob.data=ob.data.copy();highs.objects.link(ob);ob.name='SS18_editable_carved_trough'
ob.data.materials.clear();ob.data.materials.append(palette[1]);current['high'].append(ob)
bpy.context.view_layer.objects.active=ob;mod=ob.modifiers.new('Shallow carved rims','BEVEL');mod.width=.007;mod.segments=1;bpy.ops.object.modifier_apply(modifier=mod.name)

for tag,slug,name,rise,run,count in [('SS19','ss19-sandstone-threshold-steps','threshold steps',.5,1,2),
                                   ('SS20','ss20-half-storey-sandstone-stair','half-storey stair',1.5,2,8)]:
    begin(tag,slug,tag+' sandstone '+name,4,[1.5,rise,run],[0,0,0],
          f'Solid sandstone steps: 1.5 m wide, {rise:g} m rise over {run:g} m run. Continue from either end.',steps=count)
    tread=run/count;riser=rise/count
    profile=[(0,0),(run,0),(run,rise)]
    for i in range(count-1,-1,-1):
        profile.append((i*tread,(i+1)*riser))
        if i:profile.append((i*tread,i*riser))
    low_prism(profile,0,1.5,'x');core()
    for i in range(count):
        hull(*box_data((0,0,i*tread),(1.5,(i+1)*riser,(i+1)*tread)))
        high_box('Tread%d'%i,(0,0,i*tread+.003),(1.5,(i+1)*riser,(i+1)*tread-.003))

begin('SS21','ss21-battered-sandstone-wall','SS21 battered sandstone terrace wall',0,[4,1.5,.75],[0,0,-.375],
      'A 4 m terrace wall with a battered face, 0.75 m base and 0.5 m top. Does not alter terrain.',ground=True)
profile=[(-.375,0),(.375,0),(.125,1.5),(-.375,1.5)]
low_prism(profile,0,4,'x');convex_prism(profile,0,4,'x');core()
for row in range(6):
    y0=row*.25+.004;y1=(row+1)*.25-.004
    poly=[(-.375,y0),(.375-y0/6,y0),(.375-y1/6,y1),(-.375,y1)]
    for col in range(6):high_prism('Retaining%d_%d'%(row,col),poly,col*4/6+.005,(col+1)*4/6-.005,'x')

begin('SS22','ss22-open-sandstone-drain','SS22 open sandstone drain',7,[2,.25,.5],[0,0,0],
      'A 2 m open channel with a 0.25 m clear width and 0.125 m depth. Both ends stay open; no fluid simulation.')
profile=[(0,0),(.5,0),(.5,.25),(.375,.25),(.375,.125),(.125,.125),(.125,.25),(0,.25)]
low_prism(profile,0,2,'x')
for lo,hi in [((0,0,0),(2,.125,.5)),((0,.125,0),(2,.25,.125)),((0,.125,.375),(2,.25,.5))]:hull(*box_data(lo,hi))
high_prism('CarvedChannel',profile,0,2,'x',.004)

begin('SS23','ss23-sandstone-corbel-shelf','SS23 sandstone corbel shelf',7,[1,.5,.75],[0,0,-.25],
      'A built-in stone shelf projecting 0.5 m. Two corbel tails seat 0.25 m into compatible solid masonry.',wallMounted=True)
# Low boxes are united to omit hidden seat/corbel interfaces before triangulation.
parts=[]
seat=make('Shelf seat',*box_data((0,.375,0),(1,.5,.5)),lows,runtime);parts.append(seat)
profile=[(-.25,0),(.125,0),(.125,.125),(.25,.125),(.25,.25),(.375,.25),(.375,.375),(-.25,.375)]
for a,b in [(.125,.375),(.625,.875)]:
    v,f=prism_data(profile,a,b);v=[(z,y,x) for x,y,z in v]
    ob=make('Shelf corbel',v,f,lows,runtime);parts.append(ob)
    for lo,hi in [((a,0,-.25),(b,.125,.125)),((a,.125,-.25),(b,.25,.25)),((a,.25,-.25),(b,.375,.375))]:hull(*box_data(lo,hi))
    high_prism('SteppedCorbel',profile,a,b,'x')
high_box('ShelfSlab',(0,.375,0),(1,.5,.5));hull(*box_data((0,.375,0),(1,.5,.5)))
for ob in parts[1:]:
    cut_high(parts[0],[(v.co.x,v.co.z,v.co.y) for v in ob.data.vertices],[tuple(p.vertices) for p in ob.data.polygons],operation='UNION')
    bpy.data.objects.remove(ob,do_unlink=True)
bm=bmesh.new();bm.from_mesh(parts[0].data)
bmesh.ops.dissolve_limit(bm,angle_limit=.0001,verts=list(bm.verts),edges=list(bm.edges),use_dissolve_boundaries=False)
bmesh.ops.recalc_face_normals(bm,faces=bm.faces);assert all(e.is_manifold for e in bm.edges)
bm.to_mesh(parts[0].data);bm.free();current['low']=parts[0]

begin('SS24','ss24-sandstone-hearth-curb','SS24 sandstone hearth curb',7,[1.5,.25,1.5],[0,0,0],
      'A square stone curb around a clear 1 m centre. No floor slab, fire, cooking or chimney included.')
ring_box(1.5,.25,1.5,.25)
for i,(lo,hi) in enumerate([((0,0,0),(.25,.25,1.5)),((1.25,0,0),(1.5,.25,1.5)),((.25,0,0),(1.25,.25,.25)),((.25,0,1.25),(1.25,.25,1.5))]):
    high_box('HearthCurb%d'%i,lo,hi)

tail = base[base.index('# Atlas:'):]
tail = tail.replace('Sandstone_ColourAO.png','SandstoneExtension_ColourAO.png').replace('Sandstone_Normal.png','SandstoneExtension_Normal.png')
tail = tail.replace('sandstone-blender-overview.png','extension-blender-overview.png').replace('SandstoneBuildKit.blend','SandstoneExtension.blend')
tail = tail.replace('sandstone-models.json','sandstone-extension-models.json')
tail = tail.replace("Vector((8,5,1))", "Vector((8,7,1))").replace('camera.data.ortho_scale=29','camera.data.ortho_scale=32')
exec(compile(tail, str(ROOT / 'Tools/BuildSandstoneKit.py'), 'exec'))
