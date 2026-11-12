"""Build and render the approved woodland edge models in Blender.

blender -b --python Tools/BuildWoodlandEdgeHighPoly.py -- ArtSource/WoodlandEdgeHighPoly
"""
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildWoodlandTrees import GOLDEN_ANGLE, UP, Mesh, Tree, along, horizontal, material, preview_scene, render


FORMS = {
    "LowBranchedWhiteOak": (16.0, 7.0, .135, .53, 1, 38, .27, "WhiteOak"),
    "MultistemEdgeBeechV2": (6.5, 2.95, .070, .095, 3, 42, .155, "AmericanBeech"),
    "HazelThicket": (2.4, 1.57, .06, .037, 8, 38, .119, "AmericanBeech"),
}


def leaf_material(name, kind):
    mat = bpy.data.materials.new(name + " | living foliage")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = .72
    shader.inputs["Specular IOR Level"].default_value = .24
    shader.inputs["Subsurface Weight"].default_value = .035
    shader.inputs["Subsurface Radius"].default_value = (.26, .7, .16)
    shader.inputs["Subsurface Scale"].default_value = .014
    uv = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 16
    noise.inputs["Detail"].default_value = 3
    links.new(uv.outputs["UV"], noise.inputs["Vector"])
    variation = nodes.new("ShaderNodeAttribute")
    variation.attribute_name = "LeafVariation"
    mix = nodes.new("ShaderNodeMixRGB")
    mix.inputs[0].default_value = .62
    links.new(noise.outputs["Fac"], mix.inputs[1])
    links.new(variation.outputs["Fac"], mix.inputs[2])
    palette = nodes.new("ShaderNodeValToRGB")
    palette.color_ramp.elements[0].color = ((.019, .047, .011, 1), (.017, .044, .011, 1), (.012, .033, .010, 1))[kind]
    palette.color_ramp.elements[1].color = ((.102, .176, .035, 1), (.088, .16, .032, 1), (.064, .125, .030, 1))[kind]
    links.new(mix.outputs[0], palette.inputs[0])

    split = nodes.new("ShaderNodeSeparateXYZ")
    links.new(uv.outputs["UV"], split.inputs[0])

    def op(operation, a, b=None):
        node = nodes.new("ShaderNodeMath")
        node.operation = operation
        for i, value in enumerate((a, b)):
            if value is None:
                continue
            if isinstance(value, (float, int)):
                node.inputs[i].default_value = value
            else:
                links.new(value, node.inputs[i])
        return node.outputs[0]

    across = op("ABSOLUTE", op("SUBTRACT", split.outputs["X"], .5))
    midrib = op("LESS_THAN", across, .014)
    diagonal = op("ADD", split.outputs["Y"], op("MULTIPLY", across, -.60))
    vein = op("LESS_THAN", op("FRACT", op("MULTIPLY", diagonal, 8.0)), .034)
    veins = op("MAXIMUM", midrib, op("MULTIPLY", vein, .45))
    color = nodes.new("ShaderNodeMixRGB")
    links.new(op("MULTIPLY", veins, .36), color.inputs[0])
    links.new(palette.outputs["Color"], color.inputs[1])
    color.inputs[2].default_value = (.17, .23, .064, 1)
    links.new(color.outputs[0], shader.inputs["Base Color"])
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = .18
    bump.inputs["Distance"].default_value = .0015
    links.new(op("ADD", veins, noise.outputs["Fac"]), bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    return mat


class Specimen:
    tube = Tree.tube

    def __init__(self, name, kind):
        self.name, self.kind = name, kind
        self.h, self.w, self.first, self.r, self.stems, self.count, self.leaf_size, self.bark = FORMS[name]
        self.rng = random.Random((917, 1738, 4315)[kind])
        self.wood, self.foliage = Mesh(), Mesh()
        self.tones = []

    def leaf(self, point, axis, length, roll):
        axis = axis.normalized()
        side = axis.cross(UP)
        if side.length < .05:
            side = axis.cross(Vector((0, 1, 0)))
        side = Quaternion(axis, roll) @ side.normalized()
        normal = side.cross(axis)
        start = len(self.foliage.vertices)
        tone = self.rng.uniform(.15, .95)
        width = length * (.36, .29, .39)[self.kind]
        steps = 10 if self.kind == 0 else 6
        for row in range(steps + 1):
            t = row / steps
            profile = max(.018, math.sin(math.pi * t) ** (.70 if self.kind == 0 else .86))
            if self.kind == 0:
                profile *= .76 + .24 * math.sin(row * math.pi * .86 + .5)
            else:
                profile *= .90 + .10 * (-1) ** row
            center = point + axis * (length * t) + normal * (length * (.14 * t * (1 - t) - .13 * t * t))
            for u in (0, .5, 1):
                edge = (u - .5) * 2
                self.foliage.vertices.append(center + side * (edge * width * profile) -
                    normal * (abs(edge) * width * profile * .18))
                self.tones.append(tone)
            if row:
                a = start + (row - 1) * 3
                last_t = (row - 1) / steps
                for col in range(2):
                    u = col * .5
                    self.foliage.add((a + col, a + col + 1, a + col + 4, a + col + 3),
                                     ((u, last_t), (u + .5, last_t), (u + .5, t), (u, t)))

    def shoot(self, point, direction, length, count):
        rng = self.rng
        direction = direction.normalized()
        tip = point + direction * length + UP * length * rng.uniform(-.24, .13)
        path = [point, point.lerp(tip, .52) + UP * length * .08, tip]
        self.tube(path, [length * .007, length * .0035, length * .0013], 5)
        for i in range(count):
            t = (i + .55) / count
            root = along(path, t)
            yaw = math.atan2(direction.y, direction.x)
            outward = horizontal(yaw + (-1) ** i * rng.uniform(.55, 1.44)) + UP * rng.uniform(-.52, .35)
            self.leaf(root, outward, self.leaf_size * rng.uniform(.73, 1.27) * (1 - .26 * t),
                      rng.uniform(-.85, .85))

    def bough(self, point, angle, reach, rise, radius):
        rng = self.rng
        heading, across = horizontal(angle), horizontal(angle + math.pi / 2)
        curve = across * reach * rng.uniform(-.26, .26)
        points = [point, point + heading * reach * .32 + UP * rise * .50,
                  point + heading * reach * .70 + curve * .48 + UP * rise * .87,
                  point + heading * reach + curve + UP * rise]
        self.tube(points, [radius, radius * .72, radius * .40, radius * .075], 10)
        pairs = (8, 6, 5)[self.kind]
        for i in range(pairs):
            t = .18 + .79 * i / (pairs - 1)
            for sign in (-1, 1):
                root = along(points, min(.99, t + rng.uniform(-.032, .032)))
                side = horizontal(angle + sign * rng.uniform(.46, 1.40))
                length = reach * rng.uniform(.30, .54) * (1 - .51 * t)
                fork = root + side * length * .51 + UP * length * .18
                end = root + side * length + UP * length * rng.uniform(-.12, .38)
                self.tube([root, fork, end], [radius * .31, radius * .17, radius * .035], 7)
                for j in range((7, 6, 5)[self.kind]):
                    twig = along([root, fork, end], .14 + .13 * j)
                    direction = horizontal(angle + sign * rng.uniform(.06, 2.65)) + UP * rng.uniform(-.9, .8)
                    shoot_length = max(self.leaf_size * 1.25, length * rng.uniform(.30, .68))
                    self.shoot(twig, direction, shoot_length, (10, 9, 8)[self.kind])
                # Fork into volumetric clusters instead of leaving foliage
                # arranged along a single horizontal sheet of twigs.
                for j in range(3):
                    twig = along([root, fork, end], .27 + .29 * j)
                    fan = horizontal(angle + sign * rng.uniform(.3, 1.9))
                    size = max(self.leaf_size * 1.9, length * rng.uniform(.42, .85))
                    tilt = rng.uniform(.58, 1.22) if (i + j) % 3 else rng.uniform(-.75, -.42)
                    crown = [twig, twig + fan * size * .32 + UP * (size * tilt * .45),
                             twig + fan * size * .60 + UP * (size * tilt)]
                    self.tube(crown, [size * .013, size * .007, size * .002], 5)
                    for k in range(5):
                        direction = horizontal(angle + sign * rng.uniform(-.6, 2.9)) + UP * rng.uniform(-1.2, 1.35)
                        self.shoot(along(crown, .19 + .19 * k), direction,
                                   size * rng.uniform(.47, .80), 8)
        for j in range(4):
            self.shoot(along(points, .72 + j * .075),
                       heading.lerp(across * (-1) ** j, .38) + UP * rng.uniform(-.4, 1.1),
                       max(self.leaf_size * 1.7, reach * .24), 10)

    def build(self):
        rng = self.rng
        azimuth = rng.uniform(0, math.tau)
        stems = []
        for i in range(self.stems):
            direction = horizontal(i * GOLDEN_ANGLE + azimuth)
            height = self.h * (1 if i == 0 else rng.uniform(.64, .95))
            offset = (0, 1.15, 1.0)[self.kind] * (1 if i else .40)
            base = direction * self.r * (i != 0)
            points = [base + UP * (-.025 + height * t) + direction * offset * t ** 1.25
                      + horizontal(azimuth + .8 + i) * height * .045 * math.sin(t * 3.1)
                      for t in (0, .07, .23, .42, .62, .82, 1)]
            self.tube(points, [self.r * v for v in (1.48, 1.05, .82, .57, .35, .19, .025)], 20, root=True)
            stems.append(points)
        for i in range(self.count):
            age = (i + rng.uniform(.18, .82)) / self.count
            if self.kind == 1:
                # Each stem has its own crowded branch groups, with adjacent
                # stems offset in height and azimuth so the crowns interlock.
                per_stem = self.count // self.stems
                slot = i // self.stems
                age = (slot + rng.uniform(-.6, 1.25)) / per_stem
                age = min(.96, max(.025, age))
            t = self.first + age * (.96 - self.first)
            stem = stems[i % self.stems]
            stem_z = [v.z for v in stem]
            level = t * stem_z[-1]
            upper = next(j for j in range(1, len(stem)) if stem_z[j] >= level)
            root = stem[upper - 1].lerp(stem[upper], (level - stem_z[upper - 1]) / (stem_z[upper] - stem_z[upper - 1]))
            angle = i * GOLDEN_ANGLE + azimuth + rng.uniform(-.36, .36)
            if self.kind == 1:
                angle = azimuth + (i % self.stems) * 2.05 + (slot % 4) * 1.58 + rng.uniform(-.82, .82)
            envelope = max(.20, math.sin(math.pi * (.20 + .73 * age)) ** (.54 if self.kind == 0 else .76))
            if self.kind == 0 and age < .32:
                envelope = max(envelope, .89 + .08 * math.sin(2.3 * angle))
            reach = self.w * envelope * rng.uniform(.64, 1.04)
            rise = self.h * rng.uniform(.035, .17) * (1 - age * .60)
            if self.kind == 1:
                reach *= .80 if slot % 3 == 1 else 1.02
                rise = self.h * rng.uniform(-.055, .19)
            if self.kind != 0 and i < self.count * .21:
                rise *= -.28
            self.bough(root, angle, reach, rise, self.r * (1 - t) ** .72 * rng.uniform(.22, .48))

        for i, stem in enumerate(stems):
            for j in range(5 if self.kind < 2 else 3):
                tip = stem[-1].lerp(stem[-2], .24 * j)
                self.bough(tip, azimuth + (i * 3 + j) * GOLDEN_ANGLE,
                           self.w * rng.uniform(.17, .35), -self.h * rng.uniform(.015, .055),
                           self.r * .12)

        if self.kind == 0:
            for i in range(7):
                direction = horizontal(i * GOLDEN_ANGLE)
                self.tube([direction * .34 + UP * .57, direction * .78 + UP * .22,
                           direction * rng.uniform(1.1, 1.45) + UP * .03],
                          [.25, .15, .025], 10, root=True)


def build(name, kind, output):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    model = Specimen(name, kind)
    model.build()
    textures = Path(__file__).resolve().parents[1] / "Assets/Textures/Trees/Woodland"
    bark = material(name + " | bark", textures / (model.bark + "_Bark.png"))
    bark_nodes = bark.node_tree.nodes
    bark_links = bark.node_tree.links
    bark_color = next(node for node in bark_nodes if node.type == "TEX_IMAGE")
    bark_bump = bark_nodes.new("ShaderNodeBump")
    bark_bump.inputs["Strength"].default_value = .28
    bark_bump.inputs["Distance"].default_value = .022 if kind == 0 else .007
    bark_links.new(bark_color.outputs["Color"], bark_bump.inputs["Height"])
    bark_links.new(bark_bump.outputs["Normal"], bark_nodes.get("Principled BSDF").inputs["Normal"])
    wood, wood_tris = model.wood.finish("Branch and trunk geometry", bark)
    foliage, leaf_tris = model.foliage.finish("Individual curved leaf geometry", leaf_material(name, kind))
    tones = foliage.data.attributes.new("LeafVariation", "FLOAT", "POINT")
    tones.data.foreach_set("value", model.tones)
    bounds = [Vector(p) for obj in (wood, foliage) for p in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in bounds) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in bounds) for i in range(3)))
    height = high.z
    center = (high + low) * .5
    span = max(height, high.x - low.x, high.y - low.y)
    scene, camera = preview_scene((900, 900), 22)
    scene.name = name
    scene.render.image_settings.file_format = "PNG"
    scene.world.node_tree.nodes.get("Background").inputs["Color"].default_value = (.62, .68, .76, 1)
    scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value = .47
    bpy.data.lights["Afternoon Sun"].energy = 2.25
    bpy.data.lights["Afternoon Sun"].color = (1, .96, .87)
    bpy.ops.mesh.primitive_plane_add(size=span * 140, location=(0, 0, -.055))
    stage = bpy.context.object
    stage.name = "Preview ground"
    gray = bpy.data.materials.new("Matte gray preview ground")
    gray.diffuse_color = (.29, .30, .30, 1)
    stage.data.materials.append(gray)
    fill_data = bpy.data.lights.new("Soft front fill", "AREA")
    fill_data.energy = span * span * 10
    fill_data.shape = "DISK"
    fill_data.size = span * .95
    fill = bpy.data.objects.new(fill_data.name, fill_data)
    scene.collection.objects.link(fill)
    fill.location = (-span * .61, -span * .94, height * .68)
    fill.rotation_euler = (Vector((0, 0, height * .46)) - fill.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = span * 1.38
    target = Vector((center.x, center.y, height * .48))
    distance = span * 2.8
    views = (
        (Vector((distance * .16, -distance, height * .63)), "Front"),
        (Vector((-distance * .82, -distance * .56, height * .82)), "ThreeQuarter"),
    )
    position = views[0][0]
    camera.location = position
    camera.rotation_euler = (target - position).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = foliage
    foliage.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output / (name + ".blend")), compress=True)
    leaf_count = len(model.tones) // (33 if kind == 0 else 21)
    print(f"MODEL {name}: {height:.2f} m tall; {wood_tris + leaf_tris:,} triangles; {leaf_count:,} leaves", flush=True)
    for position, suffix in views:
        render(scene, camera, position, target, output / (name + "-" + suffix + ".png"))


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:]
    output = Path(args[0]).resolve()
    output.mkdir(parents=True, exist_ok=True)
    for kind, name in enumerate(FORMS):
        if len(args) == 1 or name in args[1:]:
            build(name, kind, output)
