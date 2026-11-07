"""Build editable forest-study trees and export their bark/foliage parts.

blender -b --factory-startup --python Tools/BuildForestVariety.py -- MODEL_DIR SOURCE_DIR TEXTURE_DIR
"""

import math
import random
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


def horizontal(angle):
    return Vector((math.cos(angle), math.sin(angle), 0))


UP = Vector((0, 0, 1))


class MeshPart:
    def __init__(self):
        self.positions = []
        self.faces = []
        self.uvs = []

    def add_face(self, face, uv):
        self.faces.append(face)
        self.uvs.extend(uv)

    def object(self, name, material, height, smooth):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([p * height for p in self.positions], [], self.faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="UVMap")
        uv.data.foreach_set("uv", [v for xy in self.uvs for v in xy])
        for face in mesh.polygons:
            face.use_smooth = smooth
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj


class Tree:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.bark = MeshPart()
        self.leaves = MeshPart()

    def limb(self, points, radii, sides):
        first = len(self.bark.positions)
        frame = None
        twist = self.rng.uniform(0, math.tau)
        v = 0
        for index, (p, radius) in enumerate(zip(points, radii)):
            tangent = (points[min(index + 1, len(points) - 1)] - points[max(0, index - 1)]).normalized()
            if frame is None:
                frame = tangent.cross(Vector((0, 1, 0)))
            frame = (frame - tangent * frame.dot(tangent)).normalized()
            side = tangent.cross(frame)
            for j in range(sides):
                angle = j * math.tau / sides + twist
                ridge = 1 + .09 * math.sin(angle * 3 + twist) + .035 * math.sin(angle * 5 - twist)
                self.bark.positions.append(p + radius * ridge * (frame * math.cos(angle) + side * math.sin(angle)))
            if index == 0:
                continue
            next_v = v + (p - points[index - 1]).length * 2.1
            base = first + (index - 1) * sides
            for j in range(sides):
                k = (j + 1) % sides
                self.bark.add_face((base+j, base+k, base+sides+k, base+sides+j),
                                   ((j/sides, v), ((j+1)/sides, v), ((j+1)/sides, next_v), (j/sides, next_v)))
            v = next_v
        end = first + (len(points) - 1) * sides
        self.bark.add_face(tuple(end + j for j in range(sides)), tuple((.5, v) for _ in range(sides)))

    def card(self, center, across, up, width, height, uv=(0, 0, 1, 1)):
        first = len(self.leaves.positions)
        self.leaves.positions.extend(center + x*width*across + y*height*up
                                     for x, y in ((-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5)))
        u0, v0, u1, v1 = uv
        self.leaves.add_face(tuple(range(first, first+4)), ((u0,v0),(u1,v0),(u1,v1),(u0,v1)))

    def spray(self, center, direction, count, spread, size, droop=False):
        side = Vector((-direction.y, direction.x, 0))
        rng = self.rng
        for i in range(count):
            length = rng.uniform(-.63, 1) * spread
            width = rng.uniform(-.8, .8) * spread
            arc = rng.uniform(-.24, .24) * spread
            anchor = center + direction*length + side*width + UP*arc
            azimuth = math.atan2(direction.y, direction.x) + rng.uniform(-1.4, 1.4) + (i % 2)*.78
            horizontal_axis = horizontal(azimuth)
            cross = Vector((-horizontal_axis.y, horizontal_axis.x, 0))
            extent = size * rng.uniform(.65, 1.2)
            if droop:
                # The authored willow card has its stems at the top edge.
                # Place them along the arch and hang the foliage below it.
                long_axis = (UP + horizontal_axis*rng.uniform(-.33, .38)).normalized()
                anchor -= long_axis * extent * .36
                self.card(anchor, cross, long_axis, extent*.62, extent, (.13,.075,.84,.94))
            else:
                tilt = rng.uniform(.20, 1.18)
                along = (horizontal_axis*math.cos(tilt) + UP*math.sin(tilt)).normalized()
                self.card(anchor, cross, along, extent*rng.uniform(.78, 1), extent)


def oak(tree):
    rng = tree.rng
    trunk = [Vector(p) for p in ((0,0,0),(.015,.004,.14),(.029,.01,.27),(.019,.018,.40),
                                (-.024,.034,.56),(-.054,.06,.73),(-.082,.091,.87),(-.09,.09,.94))]
    tree.limb(trunk, [.078,.057,.048,.038,.030,.021,.009,.002], 10)
    for i, (angle, level, reach, lift) in enumerate((
            (.08,2,.56,.31), (2.71,2,.55,.33), (4.21,3,.54,.36), (1.23,3,.48,.37),
            (5.50,4,.54,.24), (3.39,4,.45,.30), (2.15,5,.34,.23), (.49,5,.28,.22))):
        outward = horizontal(angle)
        side = horizontal(angle+math.pi/2)
        base = trunk[level]
        elbow = base + outward*(reach*.35) + UP*(lift*.53) + side*rng.uniform(-.047,.047)
        middle = base + outward*(reach*.72) + UP*(lift*.78) + side*rng.uniform(-.065,.065)
        tip = base + outward*reach + UP*lift + side*rng.uniform(-.06,.06)
        radius = .041 - i*.0032
        tree.limb([base,base.lerp(elbow,.45),elbow,middle,tip], [radius,radius*.83,radius*.63,radius*.35,.003], 7)
        for j in range(4):
            root = elbow.lerp(middle, .19 + .22*j)
            spoke = horizontal(angle + (-1 if j%2 else 1)*rng.uniform(.42,1.15))
            length = rng.uniform(.12,.24)*(1-.045*i)
            end = root + spoke*length + UP*rng.uniform(.075,.165)
            tree.limb([root,root.lerp(end,.52)-UP*.016,end], [.013-i*.0007,.0075,.0018], 4)
            tree.spray(end, spoke, 10, .085, .171)
        tree.spray(tip, outward, 18, .13, .17)
        # Upright secondary shoots tie the lower sprays into the larger crown.
        if i < 6:
            origin = elbow.lerp(middle, .3)
            direction = horizontal(angle + .54)
            end = origin + direction*rng.uniform(.13,.22) + UP*rng.uniform(.16,.23)
            tree.limb([origin,origin.lerp(end,.48),end],[.012,.006,.0014],5)
            tree.spray(end,direction,16,.10,.157)
    tree.spray(trunk[-2], horizontal(2.2), 24, .15, .163)


def beech(tree):
    rng = tree.rng
    trunk = [Vector((.04*z*z, -.085*z*z, z)) for z in (0,.12,.26,.41,.56,.72,.85,.96,1)]
    tree.limb(trunk, [.055,.034,.029,.024,.019,.014,.009,.005,.0015], 9)
    for i in range(13):
        z = .37 + .045*i + rng.uniform(-.026,.018)
        base = Vector((.04*z*z,-.085*z*z,z))
        angle = i*2.39996 + rng.uniform(-.31,.31)
        direction = horizontal(angle)
        reach = (.34 - .016*i)*rng.uniform(.79,1.17)
        lift = rng.uniform(.075,.15) * (1 - .034*i)
        tip = base + direction*reach + UP*lift
        shelf = base.lerp(tip,.67) - UP*.024
        radius = .020 - .0009*i
        tree.limb([base,base.lerp(shelf,.5),shelf,tip], [radius,radius*.73,radius*.42,.0017], 6)
        for j in range(3):
            root = base.lerp(shelf,.60+.19*j)
            spoke = horizontal(angle+(-1 if j%2 else 1)*rng.uniform(.46,1.28))
            end = root + spoke*reach*rng.uniform(.4,.65) + UP*rng.uniform(.041,.087)
            tree.limb([root,root.lerp(end,.53),end], [radius*.51,radius*.31,.0015], 4)
            tree.spray(end,spoke,10,.066,.116)
        tree.spray(tip,direction,16,.090,.121)
    tree.spray(trunk[-2],horizontal(1.4),24,.085,.113)


def willow(tree):
    rng = tree.rng
    trunk = [Vector(p) for p in ((0,0,0),(-.029,0,.10),(-.045,.007,.23),(-.024,.02,.36),(.015,.03,.49))]
    tree.limb(trunk,[.086,.064,.052,.036,.010], 10)
    for i, (angle, reach, height) in enumerate(((.12,.53,.77),(1.06,.52,.86),(2.05,.51,.72),
            (2.91,.59,.81),(3.9,.55,.86),(4.75,.62,.72),(5.65,.55,.80),(2.49,.32,.99))):
        base = trunk[1+i%3]
        outward = horizontal(angle)
        arch = outward*reach*.56 + UP*height
        bend = outward*reach*.86 + UP*(height-.025)
        tip = outward*reach + UP*(height-.19)
        tree.limb([base,base.lerp(arch,.52),arch,bend,tip],[.037,.028,.014,.008,.002],7)
        tree.spray(arch+UP*.085,outward,13,.093,.207,droop=True)
        for j in range(5):
            root = arch.lerp(bend,.08+.21*j)
            spoke = horizontal(angle + (-1 if j%2 else 1)*rng.uniform(.35,1.1))
            end = root + spoke*rng.uniform(.09,.22) - UP*rng.uniform(.07,.19)
            tree.limb([root,root.lerp(end,.53)+UP*.035,end],[.012,.007,.0015],4)
            tree.spray(root.lerp(end,.71),spoke,9,.071,rng.uniform(.235,.346),droop=True)
        tree.spray(tip,outward,13,.08,.275,droop=True)


def beech_textures(folder):
    size = 512
    y, x = np.mgrid[0:1:complex(size), 0:1:complex(size)].astype(np.float32)
    rgba = np.ones((size,size,4),dtype=np.float32)
    rgba[:,:,3] = 0
    rng = random.Random(6507)
    for j in range(5):
        for i in range(5):
            cx, cy = .105+i*.195+rng.uniform(-.025,.025), .105+j*.195+rng.uniform(-.029,.029)
            angle = rng.uniform(-math.pi,math.pi)
            u = ((x-cx)*math.cos(angle)+(y-cy)*math.sin(angle))/rng.uniform(.056,.084)
            v = (-(x-cx)*math.sin(angle)+(y-cy)*math.cos(angle))/rng.uniform(.025,.044)
            edge = np.abs(u)**1.65 + v*v*np.clip(1.0+.17*u,.74,1.26)
            alpha = np.clip((1-edge)*17,0,1)
            use = alpha > rgba[:,:,3]
            shade = .91 + .06*u - .08*np.abs(v) + .042*np.exp(-np.abs(v)*25)
            rgba[:,:,:3][use] = np.clip(shade[use,None],.65,1)
            rgba[:,:,3][use] = alpha[use]
    save_image(folder/"T_BeechLeafCard.png",rgba)
    mottling = .035*np.sin(math.tau*(x*4+y*.72)+.4*np.sin(y*29)) + .03*np.cos(math.tau*(x*9-y*3.2))
    mottling += .017*np.sin(math.tau*(x*31+y*2.4)) + .01*np.cos(math.tau*(x*78-y*42))
    gray = .48 + mottling
    bark = np.ones_like(rgba)
    bark[:,:,:3] = np.stack((gray*.96,gray*.99,gray*.97),axis=-1)
    save_image(folder/"T_BeechBark.png",bark)


def save_image(path, pixels):
    image = bpy.data.images.new(path.stem, width=pixels.shape[1], height=pixels.shape[0], alpha=True)
    image.pixels.foreach_set(pixels.reshape(-1))
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()


def material(name, file, alpha):
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    nodes = result.node_tree.nodes
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(file), check_existing=True)
    texture.image.pack()
    if alpha:
        color = nodes.new("ShaderNodeMixRGB")
        color.blend_type = "MULTIPLY"
        color.inputs[0].default_value = 1
        color.inputs[2].default_value = (.24,.39,.12,1)
        result.node_tree.links.new(texture.outputs["Color"],color.inputs[1])
        result.node_tree.links.new(color.outputs[0],nodes.get("Principled BSDF").inputs["Base Color"])
        result.node_tree.links.new(texture.outputs["Alpha"],nodes.get("Principled BSDF").inputs["Alpha"])
    else:
        result.node_tree.links.new(texture.outputs["Color"],nodes.get("Principled BSDF").inputs["Base Color"])
    return result


def build(name, form, seed, height, bark, leaf, models, sources):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = Tree(seed)
    form(tree)
    parts = (tree.bark.object("Bark",material(name+" Bark",bark,False),height,True),
             tree.leaves.object("Foliage",material(name+" Foliage",leaf,True),height,False))
    bpy.context.view_layer.objects.active = parts[0]
    for part in parts:
        part.select_set(True)
    bpy.context.scene.name = name
    bpy.ops.wm.save_as_mainfile(filepath=str(sources/(name+".blend")), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(models/(name+".fbx")), use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="STRIP",
                             apply_scale_options="FBX_SCALE_UNITS")
    print(name, [(p.name,sum(len(f.vertices)-2 for f in p.data.polygons)) for p in parts], flush=True)


if __name__ == "__main__":
    models, sources, textures = (Path(p).resolve() for p in sys.argv[sys.argv.index("--")+1:])
    for path in (models,sources,textures):
        path.mkdir(parents=True,exist_ok=True)
    inputs = Path.cwd()/"Assets/Textures/Trees"
    beech_textures(textures)
    build("Beech_Stand_v01",beech,637,16.6,textures/"T_BeechBark.png",textures/"T_BeechLeafCard.png",models,sources)
    build("Oak_OpenCrown_v05",oak,824,10.65,inputs/"Bark/T_WhiteOakBark_Stylized.png",inputs/"Oak/T_OakLeafCard.png",models,sources)
    build("Willow_River_v01",willow,671,9.65,inputs/"Bark/OakBarkTextureSeamless.png",inputs/"leafClumpAtlas_Willow.png",models,sources)
