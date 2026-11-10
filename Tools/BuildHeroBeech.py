"""Build and preview an open-grown American beech with individually meshed leaves."""

import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildWoodlandTrees import Mesh, UP, along, grass_material, horizontal, material, preview_scene, render


HEIGHT = 11.6


def oct_normal(normal):
    x, y, z = normal / sum(abs(n) for n in normal)
    if z < 0:
        x, y = (1-abs(y))*math.copysign(1, x), (1-abs(x))*math.copysign(1, y)
    return x*.5+.5, y*.5+.5


class Beech:
    def __init__(self, rows):
        self.rng = random.Random(776412)
        self.wood, self.leaves = Mesh(), Mesh()
        self.rows = rows
        self.surfaces = []
        self.foliage_normals = []

    def wood_tube(self, points, radii, sides):
        start = len(self.wood.vertices)
        twist = self.rng.uniform(0, math.tau)
        v = self.rng.random()
        u = self.rng.random()
        repeats = max(1, round(radii[0] * 3.8))
        frame = None
        for i, (p, radius) in enumerate(zip(points, radii)):
            axis = (points[min(i+1, len(points)-1)] - points[max(0, i-1)]).normalized()
            if frame is None:
                frame = axis.cross(UP)
                if frame.length < .02:
                    frame = axis.cross(Vector((1,0,0)))
            frame = (frame - axis * frame.dot(axis)).normalized()
            other = axis.cross(frame)
            for j in range(sides):
                a = math.tau * j / sides
                flare = 1 + .072*math.cos(a*3+twist) + .032*math.sin(a*5-twist)
                self.wood.vertices.append(p + radius*flare*(frame*math.cos(a)+other*math.sin(a)))
            if i == 0:
                continue
            next_v = v + (p-points[i-1]).length / 1.52
            base = start + sides*(i-1)
            for j in range(sides):
                k = (j+1)%sides
                lo, hi = u + repeats*j/sides, u + repeats*(j+1)/sides
                self.wood.add((base+j,base+k,base+sides+k,base+sides+j),
                              ((lo,v),(hi,v),(hi,next_v),(lo,next_v)))
            v = next_v
        tip = start + (len(points)-1)*sides
        self.wood.add(tuple(tip+j for j in range(sides)), ((u,v),)*sides)

    def leaf(self, anchor, axis, crown_normal, length, width_ratio=(.46, .61), roll=.89):
        rng = self.rng
        axis = axis.normalized()
        across = axis.cross(UP).normalized()
        across = Quaternion(axis, rng.uniform(-roll, roll)) @ across
        normal = across.cross(axis)
        width = length*rng.uniform(*width_ratio)
        bend = length*rng.uniform(.06,.25)
        cup = length*rng.uniform(.085,.19)
        drift = length*rng.uniform(-.12,.12)
        layout = self.rows[rng.randrange(len(self.rows))]
        start = len(self.leaves.vertices)
        packed = oct_normal(crown_normal)
        for t, v, sides, u in layout:
            middle = anchor + axis*(t*length) + normal*(bend*math.sin(math.pi*t) - bend*.47*t*t)
            middle += across * (drift*t*t)
            for edge in sides:
                self.leaves.vertices.append(middle + across*(width*edge) - normal*(cup*abs(edge)*math.sin(math.pi*t)))
                self.foliage_normals.append(packed)
        for i, ((_, v0, _, u0), (_, v1, _, u1)) in enumerate(zip(layout, layout[1:])):
            row = start+3*i
            for j in range(2):
                self.leaves.add((row+j,row+j+1,row+j+4,row+j+3),
                                ((u0[j],v0),(u0[j+1],v0),(u1[j+1],v1),(u1[j],v1)))
        self.surfaces.append((start, len(self.leaves.vertices), anchor + axis*length*.51))

    def twig(self, start, heading, length, climb, count, group_center):
        rng = self.rng
        sideways = heading.cross(UP)
        end = start + heading*length + UP*climb
        sweep = length*rng.uniform(-.15,.15)
        path = [start.lerp(end,t)+math.sin(math.pi*t)*(UP*length*.15+sideways*sweep)
                for t in (0,.33,.66,1)]
        radius = length*.0095
        self.wood_tube(path, (radius,radius*.72,radius*.43,max(.00065,radius*.13)), 5)
        phase = rng.randrange(2)
        for i in range(count):
            fraction = (.22 + .76*(i+.35)/count + rng.uniform(-.016,.016))
            anchor = along(path,fraction)
            direction = Quaternion(UP, (-1 if (phase+i)%2 else 1)*rng.uniform(.56,1.55)) @ heading
            direction += UP*rng.uniform(-.78,.91)
            crown_normal = (anchor-group_center + UP*.22).normalized()
            self.leaf(anchor,direction,crown_normal,rng.uniform(.097,.163)*(1-fraction*.16))
        self.leaf(end,heading+UP*.16,(end-group_center+UP*.22).normalized(),rng.uniform(.081,.126))

    def crown(self):
        rng = self.rng
        trunk = []
        radii = []
        for i in range(27):
            t=i/26
            trunk.append(Vector((.38*t*t+.13*math.sin(5.2*t), -.44*t*t+.105*math.sin(7.4*t), HEIGHT*t-.09)))
            radii.append(max(.013,.247*(1-t)**1.18*(1+.63*math.exp(-t*34))))
        self.wood_tube(trunk,radii,17)
        for i in range(5):
            d=horizontal(i*math.tau/5+rng.uniform(-.34,.34))
            span=rng.uniform(.39,.72)
            self.wood_tube((UP*.27,d*span*.46+UP*.08,d*span-UP*.13),
                           (.115,.082,.027), 8)

        for i in range(34):
            t = .16 + .80 * (i+rng.uniform(-.28,1.28))/34
            p = along(trunk,t)
            # Irregular gaps between fork systems leave the central structure visible.
            a = .56 + i*2.39996 + rng.uniform(-.43,.43)
            heading = horizontal(a)
            side = horizontal(a+math.pi/2)
            profile = max(.07, math.sin(math.pi*((t-.04)/.96)))**.66
            reach = 4.12*profile*(.94+.18*math.cos(a-1.3))*rng.uniform(.72,1.14)
            lift = min((1-t)*HEIGHT*.94, rng.uniform(.25,1.30)+t*.54)
            if i<5:
                lift *= .75
            curve = reach*rng.uniform(-.23,.27)
            branch = [p+heading*reach*s+UP*lift*k+side*curve*s*s
                      for s,k in ((0,0),(.15,.24),(.37,.51),(.64,.82),(.87,1),(1,.96))]
            radius = max(.018,.097*(1-t)**.83*rng.uniform(.75,1.35))
            self.wood_tube(branch,tuple(radius*s for s in (1,.84,.63,.42,.22,.09)), 10)
            lateral_count = 9 + int(reach*1.30)
            for j in range(lateral_count):
                f = .19 + .78*(j+rng.uniform(.15,.9))/lateral_count
                start = along(branch,f)
                side_sign = -1 if (i+j)%2 else 1
                spread = horizontal(a+side_sign*rng.uniform(.49,1.41))
                reach_lateral = min(1.44,max(.32,reach*rng.uniform(.26,.44))) * (1-f*.29)
                rise = reach_lateral*rng.uniform(-.33,.86)
                # Distal shoots spread in overlapping fans at different heights.
                curve_lateral = (start,start+spread*reach_lateral*.41+UP*rise*.29,
                                 start+spread*reach_lateral+UP*rise)
                r = max(.0035,radius*.33*(1-f*.64))
                self.wood_tube(curve_lateral,(r,r*.57,r*.17),6)
                center = curve_lateral[1] + spread*.24 + UP*.12
                for k in range(6):
                    u = .15 + .78*(k+rng.uniform(.06,.9))/6
                    fork = along(curve_lateral,u)
                    heading_twig = Quaternion(UP, (-1 if (j+k)%2 else 1)*rng.uniform(.29,1.45)) @ spread
                    size = min(.66, max(.25, reach_lateral*rng.uniform(.43,.92)))
                    climb = size*rng.uniform(-.50,1.02)
                    self.twig(fork,heading_twig,size,climb,rng.randrange(10,15),center)
                    if k in (1,3,5) and (f > .35 or j%2 == 0):
                        # Upper and lower shoots give the fans volume and overlapping shadows.
                        upper_fork = fork + heading_twig * (size*.54) + UP*(climb*.55+size*.12)
                        off_axis = Quaternion(UP, side_sign*rng.uniform(.5,1.45)) @ heading_twig
                        self.twig(upper_fork,off_axis,size*rng.uniform(.64,1.02),
                                  size*rng.uniform(-.49,.81),rng.randrange(8,13),center)
                self.twig(curve_lateral[-1],spread,rng.uniform(.24,.47),rng.uniform(-.12,.22),11,center)
            self.twig(branch[-1],heading,rng.uniform(.25,.48),rng.uniform(-.07,.26),10,branch[-2])

    def bake_ambient(self, leaf_object):
        # Upper-hemisphere sky visibility, baked per leaf. Direct shadows stay dynamic.
        foliage = BVHTree.FromPolygons(self.leaves.vertices, self.leaves.faces)
        wood = BVHTree.FromPolygons(self.wood.vertices, self.wood.faces)
        directions = []
        for i in range(12):
            z = .12 + .85*(i+.5)/12
            directions.append(horizontal(i*2.399963)*math.sqrt(1-z*z)+UP*z)
        colors = [1.] * (4 * len(self.leaves.vertices))
        factors = []
        for start, end, center in self.surfaces:
            visible = 0
            for d in directions:
                origin = center+d*.073
                if foliage.ray_cast(origin,d,3.6)[0] is None and wood.ray_cast(origin,d,3.6)[0] is None:
                    visible += 1
            ambient = .43 + .57*(visible/len(directions))**.80
            factors.append(ambient)
            colors[start*4+3:end*4:4] = [ambient]*(end-start)
        leaf_object.data.color_attributes.new(name="LeafSkyVisibility",type="FLOAT_COLOR",domain="POINT").data.foreach_set("color",colors)
        return min(factors), sum(factors)/len(factors), max(factors)


def main():
    root, preview = map(Path, sys.argv[sys.argv.index("--")+1:][:2])
    root, preview = root.resolve(), preview.resolve()
    maps = root/"Maps"
    preview.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = Beech(json.loads((maps/"LeafRows.json").read_text()))
    tree.crown()
    bark_mat = material("American beech bark",maps/"BarkAlbedo.png",normal=maps/"BarkNormal.png")
    bark_nodes = bark_mat.node_tree.nodes
    roughness = bark_nodes.new("ShaderNodeTexImage")
    roughness.image = bpy.data.images.load(str(maps/"BarkRoughness.png"))
    roughness.image.colorspace_settings.name = "Non-Color"
    roughness.image.pack()
    bark_mat.node_tree.links.new(roughness.outputs["Color"],bark_nodes.get("Principled BSDF").inputs["Roughness"])
    leaves_mat = material("American beech leaf",maps/"LeafAlbedo.png",foliage=True,normal=maps/"LeafNormal.png")
    nodes, links = leaves_mat.node_tree.nodes, leaves_mat.node_tree.links
    base = nodes.get("Principled BSDF")
    base.inputs["Roughness"].default_value = .58
    base.inputs["Specular IOR Level"].default_value = .23
    base.inputs["Subsurface Weight"].default_value = 0
    texture = base.inputs["Base Color"].links[0].from_socket
    alpha = base.inputs["Alpha"].links[0].from_socket
    transmitted = nodes.new("ShaderNodeBsdfTranslucent")
    links.new(texture,transmitted.inputs["Color"])
    leaf_scatter = nodes.new("ShaderNodeMixShader")
    leaf_scatter.inputs[0].default_value = .19
    links.new(base.outputs[0],leaf_scatter.inputs[1])
    links.new(transmitted.outputs[0],leaf_scatter.inputs[2])
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    surface = nodes.new("ShaderNodeMixShader")
    links.new(alpha,surface.inputs[0])
    links.new(transparent.outputs[0],surface.inputs[1])
    links.new(leaf_scatter.outputs[0],surface.inputs[2])
    links.new(surface.outputs[0],nodes.get("Material Output").inputs["Surface"])
    links.remove(base.inputs["Alpha"].links[0])
    wood, wood_triangles = tree.wood.finish("Bark",bark_mat)
    leaves, leaf_triangles = tree.leaves.finish("Foliage",leaves_mat,tree.foliage_normals)
    ambient = tree.bake_ambient(leaves)
    bpy.context.view_layer.objects.active = wood
    for part in (wood,leaves):
        part.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(root/"HeroAmericanBeech.blend"),compress=True)
    bpy.ops.export_scene.fbx(filepath=str(root/"HeroAmericanBeech.fbx"),use_selection=True,
                             object_types={"MESH"},axis_forward="-Z",axis_up="Y",bake_anim=False,
                             path_mode="STRIP",apply_scale_options="FBX_SCALE_UNITS")
    stats = {"leaves": len(tree.surfaces), "leafTriangles":leaf_triangles,"barkTriangles":wood_triangles,
             "heightMeters":round(max(p.z for p in tree.leaves.vertices),2),
             "leafSkyVisibilityMinMeanMax":[round(v,4) for v in ambient]}
    (root/"Geometry.json").write_text(json.dumps(stats,indent=2)+"\n",encoding="utf-8")
    print("HERO BEECH",stats,flush=True)
    scene,camera = preview_scene((920,920),24)
    scene.view_settings.exposure = .08
    sun = bpy.data.objects["Afternoon Sun"]
    sun.rotation_euler = Vector((-.61,.78,-1.1)).to_track_quat("-Z","Y").to_euler()
    bpy.ops.mesh.primitive_plane_add(size=280)
    bpy.context.object.data.materials.append(grass_material())
    render(scene,camera,(14.9,-15.6,4.2),(0,0,5.6),preview/"front.png")
    render(scene,camera,(-14.8,13.8,4.8),(0,0,5.7),preview/"backlit.png")
    scene.render.resolution_x,scene.render.resolution_y=1100,830
    render(scene,camera,(5.1,-6.8,2.6),(.13,.02,4.8),preview/"bark-and-branches.png")


if __name__ == "__main__":
    main()
