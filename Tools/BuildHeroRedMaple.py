"""Build an upright red maple with irregular forks and opposite summer leaves."""

import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeech import Beech
from BuildHeroWhiteOak import leaf_material, smooth_path
from BuildWoodlandTrees import UP, horizontal, material


def point_at(path, fraction):
    segment = min(int(fraction * (len(path) - 1)), len(path) - 2)
    return path[segment].lerp(path[segment + 1], fraction * (len(path) - 1) - segment)


class RedMaple(Beech):
    def __init__(self, rows):
        super().__init__(rows)
        self.rng = random.Random(842401)

    def shoot(self, anchor, bearing, reach, rise, pairs, spray_center):
        rng = self.rng
        forward = horizontal(bearing)
        across = horizontal(bearing + math.pi / 2)
        tip = anchor + forward * reach + UP * rise
        bow = UP * reach * rng.uniform(.08, .24) + across * reach * rng.uniform(-.19, .19)
        points = [anchor.lerp(tip, t) + bow * math.sin(math.pi * t) for t in (0, .32, .68, 1)]
        radius = max(.0019, reach * .011)
        self.wood_tube(points, [radius * r for r in (1, .72, .42, .16)], 5)
        size = rng.uniform(.83, 1.18)
        for i in range(pairs):
            f = .15 + .82 * ((i + rng.uniform(.21, .80)) / pairs) ** .83
            node = point_at(points, f)
            angle = rng.uniform(.65, 1.50)
            pitch = rng.uniform(-.37, .57)
            for side in (-1, 1):
                # Both petioles attach at the same node and open to opposite sides.
                direction = horizontal(bearing + side * angle + rng.uniform(-.24, .24))
                direction += UP * (pitch + rng.uniform(-.36, .33))
                normal = (node - spray_center + UP * .39).normalized()
                self.leaf(node, direction, normal, rng.uniform(.103, .181) * size * (1 - f * .14),
                          width_ratio=(.83, 1.08), roll=1.07)
        self.leaf(tip, forward + UP * rng.uniform(.12, .64),
                  (tip - spray_center + UP * .39).normalized(),
                  size * rng.uniform(.103, .148), width_ratio=(.83, 1.04), roll=1.01)

    def fan(self, anchor, bearing, reach, rise, radius, density):
        rng = self.rng
        forward = horizontal(bearing)
        across = horizontal(bearing + math.pi / 2)
        curve = rng.uniform(-.24, .24) * reach
        spine = [anchor + forward * (t * reach) + UP * (rise * t + reach * .16 * math.sin(math.pi * t))
                 + across * curve * t * t for t in (0, .22, .47, .74, 1)]
        self.wood_tube(spine, [radius * r for r in (1, .79, .53, .29, .095)], 7)
        center = spine[2] + UP * .21
        pairs = max(4, round((3.62 + reach * 1.36) * density))
        for i in range(pairs):
            f = .14 + .80 * (i + rng.uniform(.13, .86)) / pairs
            node = point_at(spine, f)
            angle = rng.uniform(.59, 1.31)
            for side in (-1, 1):
                twig_bearing = bearing + angle * side + rng.uniform(-.29, .29)
                forward_twig = horizontal(twig_bearing)
                size = min(.90, reach * rng.uniform(.29, .55)) * (1 - f * .37)
                lift = size * rng.uniform(-.45, .86)
                tip = node + forward_twig * size + UP * lift
                elbow = node.lerp(tip, .52) + UP * size * .13
                radius_twig = max(.0027, radius * rng.uniform(.24, .39) * (1 - f * .47))
                self.wood_tube((node, elbow, tip), (radius_twig, radius_twig * .62, radius_twig * .19), 6)
                self.shoot(elbow, twig_bearing - side * rng.uniform(.41, 1.14),
                           max(.25, size * rng.uniform(.49, .82)), size * rng.uniform(-.39, .56),
                           rng.randrange(4, 7), center)
                self.shoot(tip, twig_bearing + rng.uniform(-.57, .57),
                           max(.28, size * rng.uniform(.52, .89)), size * rng.uniform(-.25, .62),
                           rng.randrange(4, 7), center)
                if i % 2 == 0:
                    self.shoot(elbow.lerp(tip, .62), twig_bearing + side * rng.uniform(.42, 1.23),
                               max(.22, size * rng.uniform(.34, .65)), size * rng.uniform(-.46, .48), 4, center)
        self.shoot(spine[-1], bearing + rng.uniform(-.25, .25), rng.uniform(.31, .52),
                   rng.uniform(-.14, .25), 6, center)

    def scaffold(self, anchor, bearing_degrees, reach, lift, drift, radius, density):
        rng = self.rng
        bearing = math.radians(bearing_degrees)
        direction = horizontal(bearing)
        across = horizontal(bearing + math.pi / 2)
        knots = ((0, 0, 0), (.14, .24, -.05), (.36, .49, -.14), (.62, .67, .21), (.85, .83, .58), (1, 1, 1))
        path = smooth_path([anchor + direction * (reach * t) + UP * (lift * z) + across * (drift * w)
                            for t, z, w in knots], 4)
        self.wood_tube(path, [max(.014, radius * (1 - i / len(path)) ** 1.15
                                * (1 + .16 * math.exp(-i * .78))) for i in range(len(path))],
                       19 if radius >= .20 else 13)
        nodes = round(4.6 + reach * .65)
        for i in range(nodes):
            f = .24 + .74 * (i + rng.uniform(.10, .89)) / nodes
            start = point_at(path, f)
            angle = rng.uniform(.48, 1.36)
            for side in (-1, 1):
                length = rng.uniform(1.10, 2.20) * min(1.12, .72 + reach * .10) * (1 - f * .24)
                rise = length * rng.uniform(-.37, .93) + .29 * (1-f)
                fan_radius = max(.014, radius * .34 * (1 - f * .68))
                self.fan(start, bearing + angle * side + rng.uniform(-.29, .29), length, rise,
                         fan_radius, density * rng.uniform(.67, 1.18))
        self.fan(path[-1], bearing + rng.uniform(-.37, .37), rng.uniform(.63, 1.17),
                 rng.uniform(.07, .64), .017, density)

    def leader(self, coordinates):
        points = [Vector(point) for point, _ in coordinates]
        path = smooth_path(points, 4)
        radii = [a * (1 - i / 4) + b * i / 4 for (_, a), (_, b) in zip(coordinates, coordinates[1:])
                 for i in range(4)] + [coordinates[-1][1]]
        self.wood_tube(path, radii, 28 if radii[0] > .45 else 19)
        return points

    def crown(self):
        main = self.leader((
            ((0, 0, -.22), .68), ((.02, -.01, .29), .56), ((.13, -.06, .89), .46),
            ((.20, -.12, 2.06), .40), ((.14, -.14, 3.54), .37), ((.07, .02, 4.95), .36),
            ((.30, .11, 6.56), .31), ((.68, .13, 8.46), .27), ((.86, .32, 10.49), .22),
            ((.62, .58, 12.43), .17), ((.46, .85, 14.48), .116), ((.66, 1.07, 16.21), .067),
            ((.89, 1.18, 17.76), .023),
        ))
        west = self.leader((
            ((.07, .02, 4.95), .237), ((-.44, .16, 5.90), .218), ((-1.08, .25, 7.20), .170),
            ((-1.63, .44, 9.17), .135), ((-2.25, .22, 11.34), .102), ((-2.69, .16, 13.41), .067),
            ((-2.92, -.14, 15.35), .020),
        ))
        south = self.leader((
            ((.30, .11, 6.56), .203), ((.43, -.43, 7.34), .177), ((.78, -1.07, 8.49), .147),
            ((1.18, -1.69, 10.02), .104), ((1.59, -2.01, 12.22), .069), ((2.10, -2.37, 14.38), .019),
        ))
        for heading, length, radius in ((6, 1.11, .22), (71, .86, .17), (137, 1.13, .23),
                                        (206, 1.20, .25), (267, .92, .18), (322, 1.27, .20)):
            direction = horizontal(math.radians(heading))
            points = smooth_path([UP * .69, direction * length * .34 + UP * .26,
                                  direction * length * .68 + UP * .04, direction * length - UP * .14], 3)
            self.wood_tube(points, [radius * (1 - .92 * i / 9) ** 1.32 for i in range(10)], 12)
        branches = (
            (main[5], 36, 4.38, 2.08, .64, .210, .97),
            (main[6], 103, 4.45, 2.34, -.68, .211, .99),
            (main[6], 320, 4.23, 2.84, .66, .190, .96),
            (west[1], 200, 4.63, 2.38, .60, .174, 1.00),
            (west[2], 150, 3.96, 3.04, -.71, .151, .98),
            (west[3], 230, 3.51, 2.53, .58, .128, 1.04),
            (south[2], 258, 3.42, 2.93, -.58, .142, 1.03),
            (south[2], 310, 3.64, 2.47, .61, .129, .96),
            (main[7], 56, 4.20, 3.28, .53, .156, 1.04),
            (main[8], 116, 3.78, 3.49, -.45, .138, 1.01),
            (main[8], 9, 4.10, 2.98, -.57, .140, .97),
            (west[4], 175, 3.05, 3.50, -.33, .094, 1.00),
            (west[4], 94, 2.66, 3.09, .43, .095, .96),
            (west[5], 244, 2.46, 2.43, .39, .063, .93),
            (south[4], 295, 2.67, 2.93, -.42, .064, 1.06),
            (south[4], 230, 2.38, 2.84, .40, .057, .98),
            (main[9], 77, 3.41, 3.52, .49, .098, 1.00),
            (main[9], 347, 3.33, 3.65, .39, .090, 1.02),
            (main[10], 141, 2.61, 2.97, -.39, .063, .96),
            (main[10], 230, 2.18, 2.89, -.34, .064, .93),
            (main[11], 23, 1.73, 1.87, .20, .042, .99),
            (main[11], 283, 1.68, 2.01, .21, .040, 1.01),
        )
        for branch in branches:
            self.scaffold(*branch)
        for index, tip in enumerate((main[-1], west[-1], south[-1])):
            self.fan(tip, .48 + index * 2.14, .92, .73, .018, .92)


def build_master(tree_type, name, species):
    root, output = [Path(arg).resolve() for arg in sys.argv[sys.argv.index("--") + 1:][:2]]
    output.mkdir(parents=True, exist_ok=True)
    maps = root / "Maps"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = tree_type(json.loads((maps / "LeafRows.json").read_text()))
    tree.crown()
    print(f"Placed {len(tree.surfaces):,} individual leaves", flush=True)
    bark = material(f"{species} bark", maps / "BarkAlbedo.png", normal=maps / "BarkNormal.png")
    roughness = bark.node_tree.nodes.new("ShaderNodeTexImage")
    roughness.image = bpy.data.images.load(str(maps / "BarkRoughness.png"))
    roughness.image.colorspace_settings.name = "Non-Color"
    roughness.image.pack()
    bark.node_tree.links.new(roughness.outputs["Color"], bark.node_tree.nodes["Principled BSDF"].inputs["Roughness"])
    wood, wood_tris = tree.wood.finish("Bark", bark)
    foliage, leaf_tris = tree.leaves.finish("Foliage", leaf_material(maps, f"{species} leaf"), tree.foliage_normals)
    ambient = tree.bake_ambient(foliage)
    bpy.context.view_layer.objects.active = wood
    for part in (wood, foliage):
        part.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(root / f"{name}.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(output / f"{name}.fbx"), use_selection=True,
                             object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_anim=False,
                             path_mode="STRIP", apply_scale_options="FBX_SCALE_UNITS")
    stats = {"leaves": len(tree.surfaces), "leafTriangles": leaf_tris, "barkTriangles": wood_tris,
             "heightMeters": round(max(point.z for point in tree.leaves.vertices), 2),
             "crownSpanXYMeters": [round(max(p[axis] for p in tree.leaves.vertices)
                                        - min(p[axis] for p in tree.leaves.vertices), 2) for axis in (0, 1)],
             "leafSkyVisibilityMinMeanMax": [round(value, 4) for value in ambient]}
    (root / "Geometry.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
    print(name, stats, flush=True)


if __name__ == "__main__":
    build_master(RedMaple, "HeroRedMaple", "Red maple")
