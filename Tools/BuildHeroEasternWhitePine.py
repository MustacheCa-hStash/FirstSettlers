"""Build a mature Eastern White Pine with individually meshed five-needle bundles."""

import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroNorwaySpruce import GOLDEN_ANGLE, NorwaySpruce, normalize, path_position, sky_visibility
from BuildHeroWhiteOak import smooth_path
from BuildWoodlandTrees import UP, horizontal, material


HEIGHT = 26.1


class EasternWhitePine(NorwaySpruce):
    def __init__(self):
        super().__init__()
        self.rng = random.Random(925702)

    def scaffold(self, anchor, bearing, reach, lift, sag, radius, fullness):
        rng = self.rng
        self.branch_count += 1
        direction = horizontal(bearing)
        sweep = horizontal(bearing + math.pi / 2) * reach * rng.uniform(-.24, .24)
        shape = ((0, 0), (.16, -.28 * sag), (.40, -.88 * sag), (.62, -sag),
                 (.82, .18 * lift), (1, lift))
        points = smooth_path([anchor + direction * (reach * t) + sweep * t * t + UP * z
                              for t, z in shape], 4)
        self.wood_tube(points, [max(.003, radius * (1 - .98 * i / 20) ** 1.18)
                                for i in range(21)], 17 if radius > .10 else 11)
        count = max(4, round((6.2 + reach * 2.0) * fullness))
        freshness = rng.uniform(.15, .44)
        for i in range(count):
            f = .25 + .74 * (i + rng.uniform(.10, .93)) / count
            start = path_position(points, f)
            sign = -1 if i % 2 else 1
            heading = bearing + sign * rng.uniform(.39, 1.18)
            size = (.38 + reach * rng.uniform(.14, .33)) * (1 - f * .29)
            rise = size * rng.uniform(-.22, .63)
            branch_radius = max(.004, radius * rng.uniform(.16, .29) * (1 - f * .63))
            self.pine_spray(start, heading, size, rise, branch_radius, freshness)
            if i % 3 != 0:
                self.pine_spray(start, bearing - sign * rng.uniform(.47, .98), size * rng.uniform(.56, .86),
                                rise * .70 - size * .16, branch_radius * .66, freshness + .06)
        self.pine_spray(points[-1], bearing, .27 + reach * .09, .12 + lift * .20, .008, freshness + .13)
        if anchor.z > HEIGHT * .66 and rng.random() < .70:
            for _ in range(rng.choice((1, 1, 2))):
                stem = path_position(points, rng.uniform(.76, .95))
                cone = stem - UP * .036 + horizontal(bearing + math.pi / 2) * rng.uniform(-.049, .049)
                self.add_cone(cone, rng.uniform(.112, .172), rng.uniform(.018, .025))
                self.wood_tube((stem, cone), (.0042, .0029), 5)

    def pine_spray(self, start, bearing, reach, rise, radius, freshness):
        rng = self.rng
        heading = horizontal(bearing)
        side = horizontal(bearing + math.pi / 2)
        tip = start + heading * reach + UP * rise + side * reach * rng.uniform(-.22, .22)
        points = [start.lerp(tip, t) + UP * reach * .14 * math.sin(math.pi * t)
                  for t in (0, .23, .48, .73, 1)]
        self.wood_tube(points, [radius * value for value in (1, .75, .5, .25, .08)], 7)
        count = max(4, round(6 + reach * 2.0))
        for i in range(count):
            f = .28 + .71 * (i + rng.uniform(.13, .87)) / count
            sign = -1 if i % 2 else 1
            angle = bearing + sign * rng.uniform(.34, 1.17)
            span = rng.uniform(.20, .46) * min(1.23, .73 + reach * .32)
            self.twig(path_position(points, f), angle, span, span * rng.uniform(-.35, .82), freshness)
        self.twig(points[-1], bearing + rng.uniform(-.28, .28), rng.uniform(.22, .39),
                  rng.uniform(.05, .20), freshness + .18)

    def crown(self):
        rng = self.rng
        levels = [i / 71 for i in range(72)]
        trunk = [Vector((.81 * t ** 3 + .21 * math.sin(t * 7.8),
                         -.61 * t ** 2 + .17 * math.sin(t * 9.3), HEIGHT * t - .23)) for t in levels]
        radii = [max(.013, .51 * (1 - t) ** 1.23 * (1 + .69 * math.exp(-t * 35))) for t in levels]
        self.wood_tube(trunk, radii, 34)
        for i in range(7):
            d = horizontal(i * math.tau / 7 + rng.uniform(-.36, .36))
            reach = rng.uniform(.83, 1.48)
            root = smooth_path((UP * .64, d * reach * .33 + UP * .26,
                                d * reach * .71 + UP * .034, d * reach - UP * .16), 3)
            self.wood_tube(root, [.215 * (1 - .94 * j / 9) ** 1.32 for j in range(10)], 13)
        for i in range(9):
            p = path_position(trunk, (.99 + i * .72) / HEIGHT)
            d = horizontal(i * GOLDEN_ANGLE) * rng.uniform(.26, .85)
            self.wood_tube((p, p + d * .57 + UP * .02, p + d + UP * .13), (.062, .036, .013), 9)

        # Lower tier gaps and a wide, irregular shoulder are characteristic of an older crown.
        heights = (7.7, 9.75, 11.38, 13.15, 14.88, 16.42, 17.83, 19.12, 20.50,
                   21.57, 22.61, 23.46, 24.25, 24.96, 25.49)
        for tier, height in enumerate(heights):
            f = (height - heights[0]) / (HEIGHT - heights[0])
            count = rng.choice((3, 3, 4)) if tier < 3 else rng.choice((3, 4, 4, 5))
            clock = .76 + tier * GOLDEN_ANGLE + rng.uniform(-.34, .34)
            for j in range(count):
                angle = clock + j * math.tau / count + rng.uniform(-.28, .28)
                z = height + rng.uniform(-.37, .39) * (1 - .63 * f)
                anchor = path_position(trunk, (z + .23) / HEIGHT)
                spread = (1 - f) ** .62 * (.66 + .44 * math.sin(math.pi * f ** .65))
                reach = (.19 + 6.10 * spread) * rng.uniform(.65, 1.20) * (1 + .17 * math.cos(angle - 2.0))
                lift = reach * rng.uniform(.06, .27) + f * rng.uniform(.20, .67)
                sag = reach * rng.uniform(.08, .16) * (1 - f * .48)
                radius = max(.014, .161 * (1 - f) ** 1.1 * rng.uniform(.65, 1.27))
                self.scaffold(anchor, angle, reach, lift, sag, radius, rng.uniform(.85, 1.14))
        for i in range(12):
            t = i / 12
            self.twig(path_position(trunk, .949 + t * .051), i * GOLDEN_ANGLE,
                      .32 * (1 - .80 * t), .08 + .16 * t, .66)


def needle_distribution(shoots, rng):
    shoots = np.asarray(shoots, dtype=object)
    start, bend, end = [np.asarray(shoots[:, i].tolist(), dtype=np.float32) for i in range(3)]
    length = np.linalg.norm(bend - start, axis=1) + np.linalg.norm(end - bend, axis=1)
    counts = np.maximum(7, np.rint(length * rng.uniform(39, 59, len(shoots))).astype(np.int32))
    return shoots, start, bend, end, counts


def build_needles(shoots):
    rng = np.random.default_rng(506972)
    shoots, start, bend, end, counts = needle_distribution(shoots, rng)
    shoot = np.repeat(np.arange(len(counts)), counts)
    bundles = len(shoot)
    local = np.arange(bundles) - np.repeat(np.cumsum(counts) - counts, counts)
    t = .14 + .86 * (local + rng.uniform(.12, .86, bundles)) / counts[shoot]
    first = t < .53
    base = np.where(first[:, None], start[shoot] + (bend - start)[shoot] * (t / .53)[:, None],
                    bend[shoot] + (end - bend)[shoot] * ((t - .53) / .47)[:, None])
    axis = normalize(np.where(first[:, None], (bend - start)[shoot], (end - bend)[shoot]))
    side = normalize(np.cross(axis, (0, 0, 1)))
    cross = np.cross(axis, side)
    angle = local * GOLDEN_ANGLE + np.asarray(shoots[:, 3], dtype=np.float32)[shoot] * math.tau
    out = np.cos(angle)[:, None] * side + np.sin(angle)[:, None] * cross
    base += out * .0022
    bundle_axis = normalize(out * rng.uniform(.73, 1.08, (bundles, 1)) + axis * rng.uniform(.57, 1.11, (bundles, 1)))
    fan = normalize(np.cross(bundle_axis, axis))
    second = np.cross(bundle_axis, fan)
    phases = np.arange(5)[None, :] * math.tau / 5 + rng.uniform(0, math.tau, (bundles, 1))
    spread = (fan[:, None] * np.cos(phases)[:, :, None] + second[:, None] * np.sin(phases)[:, :, None]).reshape(-1, 3)
    stem = np.repeat(bundle_axis, 5, axis=0)
    count = len(stem)
    direction = normalize(stem + spread * rng.uniform(.19, .69, (count, 1)))
    blade_length = rng.uniform(.074, .137, (count, 1))
    width = rng.uniform(.00135, .00295, (count, 1))
    origin = np.repeat(base, 5, axis=0) + spread * .00074
    sag = np.array([0, 0, -.14])
    elbow = origin + blade_length * (direction * .51 + sag * .23)
    tip = origin + blade_length * (direction + sag)
    across = normalize(np.cross(direction, spread))
    vertical = np.cross(direction, across)
    rings = [across * math.cos(a) + vertical * math.sin(a) for a in (0, math.tau / 3, 2 * math.tau / 3)]
    vertices = np.stack([origin + width * ring for ring in rings] +
                        [elbow + width * .69 * ring for ring in rings] + [tip], axis=1).astype(np.float32)
    np.rint(vertices * 32768, out=vertices)
    vertices /= 32768
    age = np.repeat(np.asarray(shoots[:, 4], dtype=np.float32)[shoot] * .72 + .28 * t, 5)
    age = np.rint(np.clip(age + rng.uniform(-.17, .26, count), .02, .98) * 64).astype(np.float32) / 64
    return vertices, age, sky_visibility((elbow + tip) * .5)


def needle_mesh(vertices, age, sky, needles):
    count, vertices_per_needle, _ = vertices.shape
    faces = np.array(((0, 1, 4), (0, 4, 3), (1, 2, 5), (1, 5, 4), (2, 0, 3), (2, 3, 5),
                      (3, 4, 6), (4, 5, 6), (5, 3, 6)), dtype=np.int32)
    polygon_count = count * len(faces)
    mesh = bpy.data.meshes.new("Eastern white pine needles")
    mesh.vertices.add(count * vertices_per_needle)
    mesh.vertices.foreach_set("co", vertices.ravel())
    mesh.loops.add(polygon_count * 3)
    indices = np.arange(count, dtype=np.int32)[:, None, None] * vertices_per_needle + faces
    mesh.loops.foreach_set("vertex_index", indices.ravel())
    mesh.polygons.add(polygon_count)
    mesh.polygons.foreach_set("loop_start", np.arange(0, polygon_count * 3, 3, dtype=np.int32))
    mesh.polygons.foreach_set("loop_total", np.full(polygon_count, 3, dtype=np.int32))
    mesh.polygons.foreach_set("use_smooth", np.ones(polygon_count, dtype=bool))
    mesh.update(calc_edges=True)
    uv = np.empty((count, faces.size, 2), dtype=np.float32)
    uv[:, :, 0] = np.array((.012, .012, .012, .515, .515, .515, .986))[faces.ravel()]
    uv[:, :, 1] = age[:, None]
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uv.ravel())
    color = np.ones((count, vertices_per_needle, 4), dtype=np.float32)
    color[:, :, 3] = sky[:, None]
    mesh.color_attributes.new(name="LeafSkyVisibility", type="BYTE_COLOR", domain="POINT").data.foreach_set(
        "color", color.ravel())
    mesh.materials.append(needles)
    obj = bpy.data.objects.new("Foliage", mesh)
    bpy.context.collection.objects.link(obj)
    return obj, polygon_count


def main():
    root, output = [Path(arg).resolve() for arg in sys.argv[sys.argv.index("--") + 1:][:2]]
    output.mkdir(parents=True, exist_ok=True)
    maps = root / "Maps"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = EasternWhitePine()
    tree.crown()
    bark = material("Eastern white pine bark", maps / "BarkAlbedo.png", normal=maps / "BarkNormal.png")
    roughness = bark.node_tree.nodes.new("ShaderNodeTexImage")
    roughness.image = bpy.data.images.load(str(maps / "BarkRoughness.png"))
    roughness.image.colorspace_settings.name = "Non-Color"
    roughness.image.pack()
    bark.node_tree.links.new(roughness.outputs["Color"], bark.node_tree.nodes["Principled BSDF"].inputs["Roughness"])
    wood, wood_triangles = tree.wood.finish("Bark", bark)
    needles = material("Eastern white pine needles", maps / "NeedleAlbedo.png")
    needles.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = .59
    vertices, age, sky = build_needles(tree.shoots)
    foliage, needle_triangles = needle_mesh(vertices, age, sky, needles)
    cone_material = bpy.data.materials.new("Eastern white pine cone")
    cone_material.use_nodes = True
    cone_surface = cone_material.node_tree.nodes["Principled BSDF"]
    cone_surface.inputs["Base Color"].default_value = (.145, .092, .055, 1)
    cone_surface.inputs["Roughness"].default_value = .84
    cones, cone_triangles = tree.cones.finish("Cones", cone_material)
    bpy.context.view_layer.objects.active = wood
    for part in (wood, foliage, cones):
        part.select_set(True)
    name = "HeroEasternWhitePine"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(root / f"{name}.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(output / f"{name}.fbx"), use_selection=True,
                             object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_anim=False,
                             path_mode="STRIP", apply_scale_options="FBX_SCALE_UNITS")
    stats = {"needles": len(age), "fascicles": len(age) // 5, "needleTriangles": needle_triangles,
             "barkTriangles": wood_triangles, "coneTriangles": cone_triangles,
             "heightMeters": round(float(vertices[:, :, 2].max()), 2),
             "crownSpanXYMeters": [round(float(v), 2) for v in np.ptp(vertices.reshape(-1, 3), axis=0)[:2]],
             "needleSkyVisibilityMinMeanMax": [round(float(v), 4) for v in (sky.min(), sky.mean(), sky.max())]}
    (root / "Geometry.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
    print(name, stats, flush=True)


if __name__ == "__main__":
    main()
