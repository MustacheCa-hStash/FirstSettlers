"""Build summer woodland models and render the authored meshes in Blender.

blender -b --factory-startup --python Tools/BuildWoodlandTrees.py -- ArtSource/WoodlandTrees PREVIEW_DIR
"""
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


UP = Vector((0, 0, 1))
GOLDEN_ANGLE = math.pi * (3 - math.sqrt(5))

# Height, crown radius, lowest branches, trunk radius, branch count.
FORMS = {
    "RedMaple": (18, 6.2, .37, .38, 15),
    "SugarMaple": (22, 8.5, .33, .53, 17),
    "PaperBirch": (16, 4.1, .30, .21, 15),
    "QuakingAspen": (20, 4.5, .46, .24, 13),
    "AmericanBeech": (20, 7.6, .28, .43, 18),
    "WhiteOak": (20, 10.7, .24, .79, 18),
    "WeepingWillow": (15, 8.1, .23, .53, 17),
    "NorwaySpruce": (21, 5.3, .13, .32, 42),
    "EasternWhitePine": (25, 6.8, .37, .44, 32),
}


def horizontal(angle):
    return Vector((math.cos(angle), math.sin(angle), 0))


def along(points, fraction):
    pos = min(fraction, .99999) * (len(points) - 1)
    i = int(pos)
    return points[i].lerp(points[i+1],pos-i)


class Mesh:
    def __init__(self):
        self.vertices = []
        self.faces = []
        self.uv = []

    def add(self, face, uv):
        self.faces.append(face)
        self.uv.extend(uv)

    def finish(self, name, material, canopy_normals=None):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        layer = mesh.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", [value for uv in self.uv for value in uv])
        if canopy_normals is not None:
            layer = mesh.uv_layers.new(name="CanopyNormals")
            layer.data.foreach_set("uv", [value for loop in mesh.loops
                                         for value in canopy_normals[loop.vertex_index]])
        for face in mesh.polygons:
            face.use_smooth = True
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name,mesh)
        bpy.context.collection.objects.link(obj)
        mesh.calc_loop_triangles()
        return obj, len(mesh.loop_triangles)


class Tree:
    def __init__(self, name, index, footprints):
        self.name = name
        self.height,self.width,self.first,self.radius,self.branches = FORMS[name]
        self.wood, self.foliage = Mesh(), Mesh()
        self.rng = random.Random(917 + 101*index)
        self.footprints = footprints
        self.cards = [0,0,0,0]
        self.clumps = []

    def canopy_normals(self):
        def envelope(vertices):
            low = Vector(tuple(min(p[i] for p in vertices) for i in range(3)))
            high = Vector(tuple(max(p[i] for p in vertices) for i in range(3)))
            return (low + high) * .5, (high - low) * .5

        def outward(position, center, radius):
            delta = position - center
            return Vector(tuple(delta[i] / max(radius[i], .3) for i in range(3))).normalized()

        vertices = self.foliage.vertices
        crown_center, crown_radius = envelope(vertices)
        normals = []
        for start, end in zip([0] + self.clumps, self.clumps):
            clump = vertices[start:end]
            center, radius = envelope(clump)
            for position in clump:
                broad = outward(position, crown_center, crown_radius)
                local = outward(position, center, radius)
                normal = local.lerp(broad, .35)
                normal /= sum(abs(component) for component in normal)
                x, y, z = normal
                if z < 0:
                    x, y = (1 - abs(y)) * (1 if x >= 0 else -1), (1 - abs(x)) * (1 if y >= 0 else -1)
                normals.append((x * .5 + .5, y * .5 + .5))
        return normals

    def tube(self, points, radii, sides, root=False):
        first = len(self.wood.vertices)
        frame = None
        u_offset = self.rng.random()
        v = self.rng.random()
        twist = self.rng.random()*math.tau
        repeats = max(1, round(radii[0] * 3.4))
        for i, (p,radius) in enumerate(zip(points,radii)):
            tangent = (points[min(i+1,len(points)-1)] - points[max(0,i-1)]).normalized()
            if frame is None:
                frame = tangent.cross(UP)
                if frame.length < .05:
                    frame = tangent.cross(Vector((1,0,0)))
            frame = (frame - tangent*frame.dot(tangent)).normalized()
            other = tangent.cross(frame)
            for side in range(sides):
                angle = side*math.tau/sides+twist
                flare = .13 + (.15 if root and i == 0 else 0)
                ridges = 1+flare*math.sin(angle*3+twist)+.065*math.cos(angle*5-twist)
                self.wood.vertices.append(p+radius*ridges*(frame*math.cos(angle)+other*math.sin(angle)))
            if i == 0:
                continue
            v_next = v+(p-points[i-1]).length*.47
            row = first+(i-1)*sides
            for j in range(sides):
                k = (j+1)%sides
                u0,u1 = u_offset+j/sides*repeats,u_offset+(j+1)/sides*repeats
                self.wood.add((row+j,row+k,row+sides+k,row+sides+j),
                              ((u0,v),(u1,v),(u1,v_next),(u0,v_next)))
            v = v_next
        row = first+(len(points)-1)*sides
        self.wood.add(tuple(row+j for j in range(sides)), ((u_offset,v),)*sides)

    def spray(self, anchor, axis, length, width, roll, bend=.09):
        axis = axis.normalized()
        across = axis.cross(UP)
        if across.length < .15:
            across = axis.cross(Vector((0,1,0)))
        across = Quaternion(axis,roll) @ across.normalized()
        normal = across.cross(axis)
        index = self.rng.randrange(4)
        self.cards[index] += 1
        first = len(self.foliage.vertices)
        rows = self.footprints[index]["rows"]
        start, end = rows[-1][0], rows[0][0]
        for y, lo, hi in reversed(rows):
            t = (start-y)/max(.01,start-end)
            for x in (lo,hi):
                side = x-.5
                warp = length * bend * (math.sin(t*math.pi) - .8*abs(side))
                self.foliage.vertices.append(anchor+axis*(length*t)+across*(width*side)+normal*warp)
        uv = lambda x,y: ((index%2+x)*.5, 1-(index//2+y)*.5)
        for i,(a,b) in enumerate(zip(list(reversed(rows)),list(reversed(rows))[1:])):
            row = first+2*i
            self.foliage.add((row,row+1,row+3,row+2),(uv(a[1],a[0]),uv(a[2],a[0]),uv(b[2],b[0]),uv(b[1],b[0])))

    def trunk(self):
        rng,h,r = self.rng,self.height,self.radius
        offset = horizontal(rng.random()*math.tau)
        curve = horizontal(rng.random()*math.tau)
        points,radii = [],[]
        for i in range(13):
            t = i/12
            # A tapering central leader, with irregularity that increases above the buttress.
            center = offset*(h*.038*t*t) + curve*(math.sin(t*5.1)*h*.017*t)
            center.z = h*t - .09
            points.append(center)
            radii.append(max(.023,r*(1-t)**1.2*(1.52 if i==0 else 1.02+rng.random()*.07)))
        self.tube(points,radii,10,root=True)
        for n in range(5):
            d = horizontal(n*math.tau/5+rng.uniform(-.35,.35))
            tip = d*r*rng.uniform(1.7,2.7)-UP*.12
            self.tube([UP*(r*.48),tip*.51+UP*.09,tip],[r*.37,r*.21,.026],5)
        return points

    def hardwood(self):
        rng,h,w = self.rng,self.height,self.width
        trunk = self.trunk()
        shape = {
            "RedMaple": (.95,.28,.12), "SugarMaple": (.75,.33,.05),
            "PaperBirch": (1.12,.31,-.04), "QuakingAspen": (1.38,.29,.13),
            "AmericanBeech": (.90,.23,.12), "WhiteOak": (.62,.28,.15)
        }
        power,rise,tip_rise = shape[self.name]
        birch,oak,beech = self.name=="PaperBirch",self.name=="WhiteOak",self.name=="AmericanBeech"
        headings = rng.random()*math.tau
        if birch:
            d = horizontal(headings+.94)
            base = along(trunk,.16)
            second = [base,base+d*.74+UP*(h*.13),base+d*1.45+UP*(h*.32),
                      base+d*2.03+UP*(h*.50),base+d*2.35+UP*(h*.70)]
            self.tube(second,[.148,.124,.091,.058,.015],7)
        for i in range(self.branches):
            fraction = (i + rng.uniform(.22,.81))/self.branches
            t = self.first + fraction*(.95-self.first)
            p = along(trunk,t)
            if birch and i%3 == 1 and t < .84:
                p = along(second,(t-.16)/.70)
            angle = headings + i*GOLDEN_ANGLE+rng.uniform(-.31,.31)
            direction = horizontal(angle)
            side = horizontal(angle+math.pi/2)
            envelope = max(.12, math.sin(math.pi*(.20+.80*fraction))**power)
            reach = w*envelope*rng.uniform(.75,1.18)
            up = min((1-t)*h*.85,h*rise*rng.uniform(.52,1.03))
            if oak and i<5:
                up *= .69
            sweep = side*reach*rng.uniform(-.30,.31)
            points = [p, p+direction*(reach*.22)+UP*(up*.38), p+direction*(reach*.60)+UP*(up*.77)+sweep*.35,
                      p+direction*reach+UP*up+sweep]
            root_radius = self.radius*(1-t)**.71*rng.uniform(.28,.48)*(1.35 if oak and i<6 else 1)
            self.tube(points,[root_radius,root_radius*.78,root_radius*.45,.023],6)
            lateral_count = 5 if oak or beech else 4
            for j in range(lateral_count):
                fraction = .27 + j*(.58/(lateral_count-1)) + rng.uniform(-.04,.04)
                fork = along(points,fraction)
                sign = (-1)**(j+i)
                for arm in range(2 if j in (2,3) else 1):
                    sign = -sign if arm else sign
                    branch_dir = horizontal(angle+sign*rng.uniform(.47,1.39))
                    branch_len = min(w*.48,max(w*.15,reach*rng.uniform(.25,.48)))
                    if self.name == "SugarMaple" and j not in (0,2):
                        branch_len *= .78
                    lift = branch_len*(tip_rise+rng.uniform(.14,.69))
                    if birch and j>=2:
                        lift *= -.30
                    twig = [fork, fork+branch_dir*(branch_len*.45)+UP*(lift*.32),
                            fork+branch_dir*branch_len+UP*lift]
                    twig_radius = root_radius * (1.02-fraction) * .70
                    if j in (0,2):
                        self.tube(twig,[max(.027,twig_radius),max(.017,twig_radius*.58),.009],4)
                    for k in range(2):
                        attachment = along(twig,.36+k*.39)
                        axis = horizontal(angle+sign*(.31+k*.48)+rng.uniform(-.65,.6))
                        axis += UP*rng.uniform(-.14,.66)
                        size = min(w*.52, (h*.108+w*.13)*rng.uniform(.66,1.08))
                        self.spray(attachment,axis,size,size*rng.uniform(.70,.96),rng.uniform(-1.2,1.2))
                    tip = twig[-1] if j in (0,2) else along(twig,.74)
                    self.spray(tip,branch_dir+UP*rng.uniform(.15,.80),w*.29,w*.26,rng.uniform(-1.1,1.1))
            for j in range(3):
                axis = horizontal(angle+(j-1)*1.00)+UP*rng.uniform(.32,.87)
                self.spray(along(points,.72+.115*j),axis,w*.39,w*.33,rng.uniform(-1.0,1.0))
            self.clumps.append(len(self.foliage.vertices))
        # Small leader foliage closes the top without a spherical cap.
        for i in range(8):
            t = .84 + .019*i
            d = horizontal(headings+i*GOLDEN_ANGLE)
            size = w*.32*(1-(i/12))
            self.spray(along(trunk,t),d+UP*.47,size,size*.86,rng.uniform(-.9,.9))

    def conifer(self):
        rng,h,w = self.rng,self.height,self.width
        trunk = self.trunk()
        pine = self.name=="EasternWhitePine"
        tiers = 8 if pine else 9
        around = 4
        heading = rng.random()*math.tau
        for tier in range(tiers):
            f = tier/(tiers-1)
            for spoke in range(around):
                t = self.first+(.93-self.first)*f+rng.uniform(-.032,.019)
                p = along(trunk,t)
                angle = heading + tier*1.15 + spoke*math.tau/around + rng.uniform(-.38,.38)
                d = horizontal(angle)
                envelope = (.33+.79*math.sin(math.pi*(.14+.80*f))**.8) if pine else (1-f*.94)**.82
                reach = w*envelope*rng.uniform(.63,1.12)
                lift = reach*(rng.uniform(.13,.42) if pine else rng.uniform(-.08,.14))
                sag = reach*(.11 if pine else rng.uniform(.08,.23))
                points = [p, p+d*reach*.32-UP*sag*.3, p+d*reach*.71-UP*sag, p+d*reach+UP*lift]
                radius = self.radius*(1-t)*rng.uniform(.27,.48)
                self.tube(points,[radius,radius*.66,radius*.36,.014],4)
                if not pine:
                    size = max(.55,reach*.55)
                    self.spray(along(points,.19),d+UP*.27,size,size*.84,rng.uniform(-1.15,1.15))
                for j in range(3 if pine else 4):
                    q = .42+j*.18 if pine else .30+j*.17
                    base = along(points,q)
                    for sign in (-1,1):
                        side = horizontal(angle+sign*rng.uniform(.62,1.24))
                        length = min(w*.48,max(.46,reach*rng.uniform(.24,.52)))*(1-q*.28)
                        child = base+side*length+UP*length*(.27 if pine else -.19)
                        if j == 0 and spoke%2 == 0:
                            self.tube([base,child],[max(.019,radius*.29),.007],4)
                        axis = horizontal(angle+sign*rng.uniform(.49,1.03))+UP*rng.uniform(-.31,.31)
                        size = max(.58,length*rng.uniform(1.15,1.76))
                        self.spray(base.lerp(child,.35 if pine else .24),axis,size,size*(1.02 if pine else .95),rng.uniform(-1.1,1.1))
                for j in range(3):
                    axis = horizontal(angle+(j-1)*.61)+UP*(.31 if pine else .38)
                    size = max(.56,reach*(.35 if pine else .42))
                    self.spray(along(points,.70+j*.12),axis,size,size*.77,rng.uniform(-1.16,1.16))
                self.clumps.append(len(self.foliage.vertices))
        for j in range(12):
            axis = horizontal(j*GOLDEN_ANGLE) + UP*.8
            size = w*.24*(1-j*.047)
            self.spray(along(trunk,.86+j*.011),axis,size,size*.72,rng.uniform(-1.2,1.2))

    def willow(self):
        rng,h,w = self.rng,self.height,self.width
        trunk = self.trunk()
        for i in range(12):
            angle = i*GOLDEN_ANGLE+rng.uniform(-.24,.28)
            d = horizontal(angle)
            p = along(trunk,.19+i*.047)
            reach = w*rng.uniform(.67,1.06)*(1-.33*i/12)
            peak = h*(.63+.028*i)*rng.uniform(.88,1.10)
            points = [p,p+d*reach*.29+UP*((peak-p.z)*.67),
                      Vector((p.x,p.y,peak))+d*reach*.70,
                      Vector((p.x,p.y,peak-reach*.28))+d*reach]
            radius = self.radius*rng.uniform(.36,.64)*(1-i*.048)
            self.tube(points,[radius,radius*.79,radius*.35,.019],7)
            for j in range(5):
                fork = along(points,.45+j*.128)
                for sign in (-1,1):
                    side = horizontal(angle+sign*rng.uniform(.42,1.02))
                    size = w*rng.uniform(.20,.38)
                    twig = [fork,fork+side*size*.43+UP*.36,fork+side*size-UP*size*.36]
                    if j in (0,2,4):
                        self.tube(twig,[max(.024,radius*.34),.021,.009],5)
                    for k in range(2):
                        root = along(twig,.35+k*.54)
                        dangle = side*rng.uniform(.12,.49)-UP
                        length = h*rng.uniform(.18,.37)
                        self.spray(root,dangle,length,length*rng.uniform(.40,.56),rng.uniform(-1.5,1.5),.12)
                    self.spray(fork,side+UP*.09,size*1.15,size*.88,rng.uniform(-1.0,1.0))
            self.clumps.append(len(self.foliage.vertices))
        for j in range(12):
            d = horizontal(j*GOLDEN_ANGLE)
            self.spray(along(trunk,.84+j*.009),d-UP*.34,w*.34,w*.24,rng.uniform(-1.3,1.3))

    def build(self):
        if self.name == "WeepingWillow":
            self.willow()
        elif self.name in ("NorwaySpruce","EasternWhitePine"):
            self.conifer()
        else:
            self.hardwood()
        self.clumps.append(len(self.foliage.vertices))


def material(name, path, foliage=False, normal=None):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    nodes, links = result.node_tree.nodes,result.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = .82 if foliage else .90
    bsdf.inputs["Specular IOR Level"].default_value = .26 if foliage else .18
    color = nodes.new("ShaderNodeTexImage")
    color.image = bpy.data.images.load(str(path),check_existing=True)
    color.image.pack()
    links.new(color.outputs["Color"],bsdf.inputs["Base Color"])
    if foliage:
        cutoff = nodes.new("ShaderNodeMath")
        cutoff.operation = "GREATER_THAN"
        cutoff.inputs[1].default_value = .32
        links.new(color.outputs["Alpha"],cutoff.inputs[0])
        links.new(cutoff.outputs[0],bsdf.inputs["Alpha"])
        bsdf.inputs["Subsurface Weight"].default_value = .018
        bsdf.inputs["Subsurface Scale"].default_value = .08
        result.surface_render_method = "DITHERED"
    if normal:
        bump = nodes.new("ShaderNodeTexImage")
        bump.image = bpy.data.images.load(str(normal),check_existing=True)
        bump.image.colorspace_settings.name = "Non-Color"
        bump.image.pack()
        node = nodes.new("ShaderNodeNormalMap")
        node.inputs["Strength"].default_value = .66
        links.new(bump.outputs["Color"],node.inputs["Color"])
        links.new(node.outputs["Normal"],bsdf.inputs["Normal"])
    return result


def preview_scene(size, samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    preferences = bpy.context.preferences.addons["cycles"].preferences
    try:
        preferences.compute_device_type = "OPTIX"
        preferences.refresh_devices()
        if any(device.type == "OPTIX" for device in preferences.devices):
            for device in preferences.devices:
                device.use = device.type == "OPTIX"
            scene.cycles.device = "GPU"
    except RuntimeError:
        pass
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.cycles.transparent_max_bounces = 12
    scene.cycles.diffuse_bounces = 2
    scene.render.resolution_x,scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = .25
    scene.world = bpy.data.worlds.new("Clear summer daylight")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (.35,.49,.67,1)
    bg.inputs["Strength"].default_value = .62
    sun = bpy.data.lights.new("Afternoon Sun","SUN")
    sun.energy,sun.angle = 3.15,.11
    sun.color = (1,.895,.76)
    light = bpy.data.objects.new(sun.name,sun)
    scene.collection.objects.link(light)
    light.rotation_euler = (.58,-.45,-.41)
    camera = bpy.data.objects.new("Woodland Camera",bpy.data.cameras.new("Woodland Camera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 41
    camera.data.clip_end = 1800
    return scene,camera


def grass_material():
    mat = bpy.data.materials.new("Quiet meadow")
    mat.use_nodes = True
    n,l = mat.node_tree.nodes,mat.node_tree.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 1
    coord = n.new("ShaderNodeTexCoord")
    noise = n.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = .85
    noise.inputs["Detail"].default_value = 4
    ramp = n.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (.041,.060,.019,1)
    ramp.color_ramp.elements[1].color = (.165,.200,.063,1)
    l.new(coord.outputs["Object"],noise.inputs["Vector"])
    l.new(noise.outputs["Fac"],ramp.inputs[0])
    l.new(ramp.outputs["Color"],bsdf.inputs["Base Color"])
    return mat


def render(scene,camera,position,target,path):
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat("-Z","Y").to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def build(out, reviews, build_only=False):
    textures = out/"Textures"
    library = json.loads((textures/"FoliageFootprints.json").read_text())
    for folder in (out/"Blender",out/"FBX",reviews):
        folder.mkdir(parents=True,exist_ok=True)
    stats = {}
    for index,name in enumerate(FORMS):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        tree = Tree(name,index,library[name])
        tree.build()
        bark = material(name+" Bark",textures/(name+"_Bark.png"),normal=textures/(name+"_BarkNormal.png"))
        leaves = material(name+" Foliage",textures/(name+"_Foliage.png"),foliage=True)
        wood,wood_tris = tree.wood.finish("Bark",bark)
        foliage,leaf_tris = tree.foliage.finish("Foliage",leaves,tree.canopy_normals())
        bpy.context.view_layer.objects.active = wood
        for obj in (wood,foliage):
            obj.select_set(True)
        bpy.context.scene.name = name
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(out/"Blender"/(name+".blend")),compress=True)
        bpy.ops.export_scene.fbx(filepath=str(out/"FBX"/(name+".fbx")),use_selection=True,
                                 object_types={"MESH"},axis_forward="-Z",axis_up="Y",bake_anim=False,
                                 path_mode="STRIP",apply_scale_options="FBX_SCALE_UNITS")
        fit = sum(n*row["area"] for n,row in zip(tree.cards,tree.footprints))
        rectangle = sum(n*row["tightRectArea"] for n,row in zip(tree.cards,tree.footprints))
        opaque = sum(n*row["opaqueArea"] for n,row in zip(tree.cards,tree.footprints))
        stats[name] = {"heightMeters":round(max(v.z for v in tree.foliage.vertices),1),
                       "barkTriangles":wood_tris,"foliageTriangles":leaf_tris,"sprays":sum(tree.cards),
                       "areaSavedVsTightRect":round(1-fit/rectangle,3),
                       "alphaCoverageOfFittedArea":round(opaque/fit,3)}
        print(name,stats[name],flush=True)
        if not build_only:
            scene,camera = preview_scene((560,560),10)
            bpy.ops.mesh.primitive_plane_add(size=1000)
            bpy.context.object.data.materials.append(grass_material())
            h = stats[name]["heightMeters"]
            radius = max(h, tree.width*2.62)
            render(scene,camera,(radius*.58,-radius*1.31,max(1.8,h*.09)),(0,0,h*.47),reviews/(name+"-eye.png"))
            render(scene,camera,(-radius*1.10,-radius*.90,h*1.08),(0,0,h*.52),reviews/(name+"-high.png"))
            if name == "WhiteOak":
                scene.render.resolution_x,scene.render.resolution_y = 900,560
                render(scene,camera,(8,-15,7),(0,0,10.3),reviews/"WhiteOak-detail.png")
    (out/"Geometry.json").write_text(json.dumps(stats,indent=2))


def grove(out,reviews):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene,camera = preview_scene((1360,760),22)
    mat = grass_material()
    # Gently rising woodland behind a bright open field. Linked source meshes are used throughout.
    vertices, faces = [],[]
    height = lambda x,y: (.043*y + .12*max(0,y-55)
                          + 7.6*math.exp(-((x-34)**2+(y-134)**2)/8100)
                          + 1.9*math.sin(x*.024)*math.sin(y*.018))
    for j in range(61):
        for i in range(65):
            x,y = (i-32)*6,(j-8)*6
            vertices.append((x,y,height(x,y)))
            if i and j:
                a = j*65+i
                faces.append((a-66,a-65,a,a-1))
    mesh = bpy.data.meshes.new("Review terrain")
    mesh.from_pydata(vertices,[],faces)
    for face in mesh.polygons:
        face.use_smooth = True
    ground = bpy.data.objects.new(mesh.name,mesh)
    scene.collection.objects.link(ground)
    mesh.materials.append(mat)
    loaded = {}
    for name in FORMS:
        with bpy.data.libraries.load(str(out/"Blender"/(name+".blend")),link=False) as (source, target):
            target.objects = list(source.objects)
        loaded[name] = [obj.data for obj in target.objects]
    rng = random.Random(26933)
    names = list(FORMS)
    for row in range(11):
        for i in range(17):
            x = (i-8)*10 + rng.uniform(-4,4)
            y = 39+row*12 + rng.uniform(-5,5) + 5*math.sin(x*.11)
            if row == 0 and 3 < i < 8:
                continue
            species = rng.choice([names[(i//4+row)%len(names)],*names[:6],names[-1],names[-2]])
            scale = rng.uniform(.68,1.19)
            if row == 0 and rng.random()<.35:
                scale *= .6
            angle = rng.random()*math.tau
            for source in loaded[species]:
                obj = bpy.data.objects.new(species,source)
                scene.collection.objects.link(obj)
                obj.location = (x,y,height(x,y)-.18)
                obj.rotation_euler.z = angle
                obj.scale = (scale,)*3
    for species,x,y,scale in (("WeepingWillow",34,36,.98),("WhiteOak",-33,26,.93),("PaperBirch",-15,38,.76)):
        for source in loaded[species]:
            obj = bpy.data.objects.new(species,source)
            scene.collection.objects.link(obj)
            obj.location = (x,y,height(x,y)-.17)
            obj.scale = (scale,)*3
    camera.data.lens = 36
    render(scene,camera,(-13,-55,height(-13,-55)+1.8),(0,64,18),reviews/"WoodlandEdge.png")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--")+1:]
    output,reviews = (Path(value).resolve() for value in args[:2])
    if "grove" in args:
        grove(output,reviews)
    else:
        build(output,reviews,"models" in args)
