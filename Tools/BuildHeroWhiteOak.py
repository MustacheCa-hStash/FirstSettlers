"""Build a mature, open-grown white oak master with individually meshed leaves."""

import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeech import Beech
from BuildWoodlandTrees import UP, along, grass_material, horizontal, material, preview_scene, render


def smooth_path(points, steps):
    return [point for index, (start, end) in enumerate(zip(points, points[1:]))
            for point in curve_segment(points, index, start, end, steps)] + [points[-1]]


def curve_segment(points, index, start, end, steps):
    before = points[max(0, index - 1)]
    after = points[min(len(points) - 1, index + 2)]
    tangent_start = .5 * (end - before)
    tangent_end = .5 * (after - start)
    for i in range(steps):
        t = i / steps
        yield (start.lerp(end, t * t * (3 - 2 * t)) + tangent_start * t * (1 - t) ** 2
               - tangent_end * t * t * (1 - t))


class WhiteOak(Beech):
    def __init__(self, rows):
        super().__init__(rows)
        self.rng = random.Random(300417)

    def shoot(self, anchor, bearing, reach, rise, count, spray_center):
        rng = self.rng
        direction = horizontal(bearing)
        lateral = horizontal(bearing + math.pi / 2)
        tip = anchor + direction * reach + UP * rise
        bend = UP * (reach * rng.uniform(.08, .25)) + lateral * reach * rng.uniform(-.18, .18)
        path = [anchor.lerp(tip, t) + bend * math.sin(math.pi * t) for t in (0, .31, .65, 1)]
        base_radius = max(.0017, reach * .010)
        self.wood_tube(path, [base_radius * r for r in (1, .70, .43, .16)], 5)
        phase = rng.randrange(2)
        length_scale = rng.uniform(.87, 1.16)
        inclination = rng.uniform(-.49, .61)
        for i in range(count):
            # Alternate nodes tighten toward the shoot tip.
            fraction = 1 - .89 * (1 - (i + rng.uniform(.22, .72)) / count) ** 1.22
            point = along(path, fraction)
            side = 1 if (i + phase) % 2 else -1
            heading = horizontal(bearing + side * rng.uniform(.49, 1.51))
            heading += UP * (inclination + rng.uniform(-.57, .73))
            crown_normal = (point - spray_center + UP * .50).normalized()
            self.leaf(point, heading, crown_normal, length_scale * rng.uniform(.160, .270) * (1 - .13 * fraction),
                      width_ratio=(.56, .72), roll=1.24)
        self.leaf(tip, direction + UP * rng.uniform(-.2, .46), (tip - spray_center + UP * .5).normalized(),
                  length_scale * rng.uniform(.141, .211), width_ratio=(.56, .72), roll=1.24)

    def spray(self, anchor, bearing, reach, rise, radius, fullness):
        rng = self.rng
        direction = horizontal(bearing)
        across = horizontal(bearing + math.pi / 2)
        sweep = reach * rng.uniform(-.26, .26)
        spine = [anchor + direction * reach * t + UP * (rise * t + reach * .14 * math.sin(math.pi * t))
                 + across * sweep * t * t for t in (0, .18, .4, .65, .85, 1)]
        self.wood_tube(spine, [radius * r for r in (1, .83, .59, .37, .21, .075)], 8)
        center = spine[3] + UP * .15
        count = max(4, round((reach * 1.96 + 2.6) * fullness))
        phase = rng.randrange(2)
        for i in range(count):
            fraction = .18 + .79 * (i + rng.uniform(.14, .84)) / count
            start = along(spine, fraction)
            side = -1 if (i + phase) % 2 else 1
            spread = bearing + side * rng.uniform(.52, 1.53)
            size = min(1.22, reach * rng.uniform(.32, .57)) * (1 - fraction * .33)
            lift = size * rng.uniform(-.50, .88)
            arm = horizontal(spread)
            tip = start + arm * size + UP * lift
            elbow = start.lerp(tip, .54) + UP * size * .13
            r = max(.0025, radius * rng.uniform(.24, .42) * (1 - fraction * .48))
            self.wood_tube((start, elbow, tip), (r, r * .61, r * .19), 6)
            for j in range(rng.choice((1, 2, 3, 3))):
                position = along((start, elbow, tip), min(.99, (j + 1) * rng.uniform(.23, .30)))
                shoot_heading = spread + (-1 if j % 2 else 1) * rng.uniform(.34, 1.47)
                shoot_size = rng.uniform(.28, .58) * min(1.3, .81 + size * .36)
                self.shoot(position, shoot_heading, shoot_size, shoot_size * rng.uniform(-.46, .76),
                           rng.randrange(8, 13), center)
            self.shoot(tip, spread + rng.uniform(-.4, .4), rng.uniform(.31, .62), rng.uniform(-.16, .30),
                       rng.randrange(9, 14), center)
        self.shoot(spine[-1], bearing, rng.uniform(.39, .69), rng.uniform(-.18, .4), 12, center)

    def scaffold(self, anchor, degrees, reach, lift, sweep, radius, fullness):
        rng = self.rng
        bearing = math.radians(degrees)
        forward, lateral = horizontal(bearing), horizontal(bearing + math.pi / 2)
        # The heavy limb rises from its collar, spreads, then curls toward the light.
        knots = [(0, 0, 0), (.14, .27, -.12), (.35, .43, -.16), (.63, .59, .24), (.86, .82, .68), (1, 1, 1)]
        limb = smooth_path([anchor + forward * (reach * t) + UP * (lift * h) + lateral * (sweep * w)
                            for t, h, w in knots], 4)
        self.wood_tube(limb, [max(.021, radius * (1 - t / len(limb)) ** 1.09)
                              * (1 + .19 * math.exp(-t * .65)) for t in range(len(limb))],
                       23 if radius >= .25 else 15)
        count = round(11 + reach * .79)
        for i in range(count):
            fraction = .25 + .72 * (i + rng.uniform(.08, .87)) / count
            start = along(limb, fraction)
            side = -1 if (i + int(degrees)) % 2 else 1
            angle = bearing + side * rng.uniform(.44, 1.47) + .28 * sweep / reach
            length = rng.uniform(1.62, 3.0) * (1 - fraction * .26) * min(1.13, .72 + .053 * reach)
            # Upper and lower fans overlap irregularly within each branching system.
            rise = length * rng.uniform(-.59, 1.23) + (1 - fraction) * .55
            branch_radius = max(.017, radius * .37 * (1 - fraction * .74))
            self.spray(start, angle, length, rise, branch_radius, fullness * rng.uniform(.65, 1.27))
            if i in (4, 9, 13):
                self.spray(start, angle - side * rng.uniform(.55, 1.1), length * rng.uniform(.55, .89),
                           rise - length * rng.uniform(.34, .68), branch_radius * .68, fullness * .68)
        self.spray(limb[-1], bearing + rng.uniform(-.4, .4), rng.uniform(.77, 1.42),
                   rng.uniform(.22, .87), .022, fullness)

    def crown(self):
        bole = [
            ((0, 0, -.27), .97), ((.01, -.02, .20), .84), ((.07, -.10, .72), .64),
            ((.14, -.13, 1.67), .54), ((.05, -.14, 2.73), .51), ((-.19, -.10, 4.01), .52),
            ((-.40, .16, 5.17), .45), ((-.55, .47, 6.64), .38), ((-.44, .69, 8.40), .33),
            ((-.05, .73, 10.12), .28), ((.22, .57, 12.55), .23), ((.53, .72, 15.18), .16),
            ((.98, .97, 17.65), .095), ((1.26, 1.01, 19.27), .039), ((1.43, 1.13, 20.1), .017),
        ]
        centers = [Vector(point) for point, _ in bole]
        path = smooth_path(centers, 4)
        radii = [a * (1 - t / 4) + b * t / 4 for (_, a), (_, b) in zip(bole, bole[1:]) for t in range(4)]
        self.wood_tube(path, radii + [bole[-1][1]], 38)
        for degrees, reach, width, knee in (
                (8, 1.31, .29, .28), (72, 1.45, .25, .12), (141, 1.14, .28, .19),
                (198, 1.64, .37, .26), (257, 1.21, .20, .13), (319, 1.79, .36, .24)):
            direction = horizontal(math.radians(degrees))
            points = (UP * (.70 + knee), direction * (reach * .35) + UP * .22,
                      direction * (reach * .71) + UP * .035, direction * reach - UP * .18)
            self.wood_tube(smooth_path(list(points), 4),
                           [width * (1 - .89 * i / 12) ** 1.35 for i in range(13)], 17)

        # Scaffold placements are hand-composed. Long lower limbs are interleaved with
        # rising forks and a less regular, broken outline in the upper canopy.
        branches = (
            (4, 201, 8.8, 4.08, -.86, .40, .96),
            (5, 337, 8.9, 5.0, 1.18, .39, 1.13),
            (6, 86, 8.0, 5.05, -1.16, .36, 1.03),
            (6, 158, 6.3, 6.0, .82, .25, .96),
            (7, 257, 7.9, 4.5, 1.18, .29, 1.05),
            (7, 44, 7.7, 5.0, -.82, .30, .93),
            (8, 126, 8.15, 5.3, .98, .28, 1.00),
            (8, 310, 7.8, 6.9, -.61, .27, 1.06),
            (9, 12, 8.25, 5.55, .87, .24, 1.04),
            (9, 219, 6.05, 6.6, -.76, .24, 1.05),
            (10, 169, 7.85, 5.32, -.79, .21, 1.13),
            (10, 68, 6.75, 6.05, -.88, .21, 1.06),
            (10, 280, 6.3, 4.9, 1.15, .19, .90),
            (11, 102, 6.00, 5.5, .62, .17, 1.06),
            (11, 232, 5.31, 5.50, .60, .16, 1.12),
            (11, 341, 5.82, 4.03, -.66, .17, 1.05),
            (12, 158, 3.94, 4.33, -.53, .10, 1.02),
            (12, 292, 4.10, 3.69, .64, .11, 1.07),
            (13, 15, 2.27, 2.95, .46, .079, .91),
            (13, 204, 2.53, 2.45, -.51, .086, .89),
        )
        for parent, angle, reach, lift, sweep, radius, fullness in branches:
            self.scaffold(centers[parent], angle, reach, lift, sweep, radius, fullness)
        self.spray(centers[-1], .65, 1.27, 1.1, .018, .88)


def leaf_material(maps, name="White oak leaf"):
    result = material(name, maps / "LeafAlbedo.png", foliage=True, normal=maps / "LeafNormal.png")
    nodes, links = result.node_tree.nodes, result.node_tree.links
    base = nodes.get("Principled BSDF")
    base.inputs["Roughness"].default_value = .59
    base.inputs["Specular IOR Level"].default_value = .21
    base.inputs["Subsurface Weight"].default_value = 0
    color = base.inputs["Base Color"].links[0].from_socket
    alpha = base.inputs["Alpha"].links[0].from_socket
    links.remove(base.inputs["Alpha"].links[0])
    transmission = nodes.new("ShaderNodeBsdfTranslucent")
    links.new(color, transmission.inputs["Color"])
    leaf_scatter = nodes.new("ShaderNodeMixShader")
    leaf_scatter.inputs[0].default_value = .17
    links.new(base.outputs[0], leaf_scatter.inputs[1])
    links.new(transmission.outputs[0], leaf_scatter.inputs[2])
    surface = nodes.new("ShaderNodeMixShader")
    links.new(alpha, surface.inputs[0])
    links.new(nodes.new("ShaderNodeBsdfTransparent").outputs[0], surface.inputs[1])
    links.new(leaf_scatter.outputs[0], surface.inputs[2])
    links.new(surface.outputs[0], nodes.get("Material Output").inputs["Surface"])
    return result


def main():
    root, output = [Path(arg).resolve() for arg in sys.argv[sys.argv.index("--") + 1:][:2]]
    output.mkdir(parents=True, exist_ok=True)
    maps = root / "Maps"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = WhiteOak(json.loads((maps / "LeafRows.json").read_text()))
    tree.crown()
    print(f"Placed {len(tree.surfaces):,} individual leaves", flush=True)
    bark = material("White oak bark", maps / "BarkAlbedo.png", normal=maps / "BarkNormal.png")
    roughness = bark.node_tree.nodes.new("ShaderNodeTexImage")
    roughness.image = bpy.data.images.load(str(maps / "BarkRoughness.png"))
    roughness.image.colorspace_settings.name = "Non-Color"
    roughness.image.pack()
    bark.node_tree.links.new(roughness.outputs["Color"], bark.node_tree.nodes["Principled BSDF"].inputs["Roughness"])
    wood, wood_tris = tree.wood.finish("Bark", bark)
    leaves, leaf_tris = tree.leaves.finish("Foliage", leaf_material(maps), tree.foliage_normals)
    ambient = tree.bake_ambient(leaves)
    bpy.context.view_layer.objects.active = wood
    for part in (wood, leaves):
        part.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(root / "HeroWhiteOak.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(output / "HeroWhiteOak.fbx"), use_selection=True,
                             object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_anim=False,
                             path_mode="STRIP", apply_scale_options="FBX_SCALE_UNITS")
    stats = {"leaves": len(tree.surfaces), "leafTriangles": leaf_tris, "barkTriangles": wood_tris,
             "heightMeters": round(max(p.z for p in tree.leaves.vertices), 2),
             "crownSpanXYMeters": [round(max(p[i] for p in tree.leaves.vertices)
                                        - min(p[i] for p in tree.leaves.vertices), 2) for i in (0, 1)],
             "leafSkyVisibilityMinMeanMax": [round(v, 4) for v in ambient]}
    (root / "Geometry.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
    print("HERO WHITE OAK", stats, flush=True)
    if "--blender-preview" in sys.argv:
        scene, camera = preview_scene((1040, 900), 24)
        bpy.ops.mesh.primitive_plane_add(size=380)
        bpy.context.object.data.materials.append(grass_material())
        render(scene, camera, (24, -34, 5), (0, 0, 10), output / "blender-front.png")


if __name__ == "__main__":
    main()
