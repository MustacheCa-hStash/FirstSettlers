"""Build a mature Norway spruce with solid needles and hanging branchlets."""

import json
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeech import Beech
from BuildHeroWhiteOak import smooth_path
from BuildWoodlandTrees import Mesh, UP, horizontal, material


GOLDEN_ANGLE = math.pi * (3 - math.sqrt(5))
HEIGHT = 22.4


def path_position(points, t):
    segment = min(int(t * (len(points) - 1)), len(points) - 2)
    return points[segment].lerp(points[segment + 1], t * (len(points) - 1) - segment)


def normalize(rows):
    return rows / np.maximum(1e-7, np.linalg.norm(rows, axis=1, keepdims=True))


class NorwaySpruce(Beech):
    def __init__(self):
        super().__init__(())
        self.rng = random.Random(268152)
        self.shoots = []
        self.cones = Mesh()
        self.branch_count = 0

    def twig(self, start, bearing, reach, rise, fresh):
        rng = self.rng
        direction = horizontal(bearing)
        tip = start + direction * reach + UP * rise
        elbow = start.lerp(tip, .53) + direction * reach * .08 + UP * reach * .12
        radius = max(.0013, reach * .015)
        self.wood_tube((start, elbow, tip), (radius, radius * .53, radius * .15), 3)
        self.shoots.append((tuple(start), tuple(elbow), tuple(tip), rng.random(), fresh))

    def branchlet(self, start, bearing, reach, drop, freshness, radius):
        rng = self.rng
        direction = horizontal(bearing)
        side = horizontal(bearing + math.pi / 2)
        drift = side * reach * rng.uniform(-.26, .27)
        points = [start + direction * (reach * t) - UP * drop * (t ** 1.38)
                  + drift * t * t for t in (0, .18, .43, .70, 1)]
        self.wood_tube(points, [radius * r for r in (1, .78, .54, .29, .09)], 6)
        # Younger portions of the lateral itself carry needles between its offshoots.
        self.shoots.append((tuple(points[2]), tuple(points[3]), tuple(points[4]),
                            (bearing / math.tau) % 1, freshness))
        count = max(5, round(7 + reach * 3.2 + drop * 1.7))
        phase = rng.randrange(2)
        for i in range(count):
            f = .10 + .89 * (i + rng.uniform(.04, .94)) / count
            fork = path_position(points, f)
            sign = 1 if (i + phase) % 2 else -1
            spread = bearing + sign * rng.uniform(.44, 1.37)
            twig_len = min(.39, reach * .27 + .16) * rng.uniform(.61, 1.38) * (1 - .28 * f)
            twig_rise = twig_len * rng.uniform(-.82, .32) - drop * .16
            self.twig(fork, spread, twig_len, twig_rise, freshness * rng.uniform(.74, 1.2))
            if i % 3 == 1:
                self.twig(fork, bearing - sign * rng.uniform(.32, .87), twig_len * .72,
                          twig_rise * .7 + twig_len * .16, freshness)
        self.twig(points[-1], bearing, min(.33, reach * .37 + .11), -drop * .22 + .056, freshness + .09)

    def scaffold(self, anchor, bearing, reach, lift, sag, radius, fullness):
        rng = self.rng
        self.branch_count += 1
        direction = horizontal(bearing)
        sweep = horizontal(bearing + math.pi / 2) * reach * rng.uniform(-.19, .19)
        shape = ((0, 0), (.17, -.19 * sag), (.39, -.74 * sag), (.63, -sag), (.84, -.35 * sag), (1, lift))
        points = smooth_path([anchor + direction * (reach * x) + sweep * x * x + UP * z for x, z in shape], 3)
        self.wood_tube(points, [max(.003, radius * (1 - .958 * i / 15) ** 1.24)
                                for i in range(16)], 13 if radius > .08 else 9)
        count = max(5, round((8 + reach * 4.4) * fullness))
        freshness = rng.uniform(.12, .41)
        for i in range(count):
            f = .14 + .85 * (i + rng.uniform(.07, .92)) / count
            fork = path_position(points, f)
            sign = 1 if i % 2 == 0 else -1
            heading = bearing + sign * rng.uniform(.53, 1.39)
            shoulder = math.sin(math.pi * min(.99, f * .94)) ** .65
            size = max(.13, reach * (.31 * shoulder + .05)) * rng.uniform(.65, 1.26)
            # Long, flexible laterals hang under mature scaffolds. Tips are shorter and turn up.
            drop = max(-.16, size * rng.uniform(.21, .96) * (1 - f * .48) - lift * f * .31)
            branch_radius = max(.0038, radius * rng.uniform(.18, .29) * (1 - f * .66))
            self.branchlet(fork, heading, size, drop, freshness + f * .18, branch_radius)
            if i % 4 == 1 and reach > 1.6:
                self.branchlet(fork, bearing - sign * rng.uniform(.45, .92), size * rng.uniform(.54, .77),
                               drop * rng.uniform(.86, 1.44), freshness, branch_radius * .72)
        self.branchlet(points[-1], bearing + rng.uniform(-.32, .32), max(.17, reach * .11),
                       -.14 * reach, freshness + .15, max(.004, radius * .10))
        if 13.8 < anchor.z < 20.5 and rng.random() < .55:
            for _ in range(rng.choice((1, 1, 2, 3))):
                f = rng.uniform(.77, .97)
                stem = path_position(points, f)
                cone = stem - UP * .025 + horizontal(bearing + math.pi / 2) * rng.uniform(-.044, .044)
                self.add_cone(cone, rng.uniform(.105, .167), rng.uniform(.018, .024))
                self.wood_tube((stem, cone), (.0039, .0025), 5)

    def add_cone(self, start, height, width):
        # Offset rows of papery overlapping scales on a tapered cone.
        rng = self.rng
        axis = Vector((rng.uniform(-.18, .18), rng.uniform(-.18, .18), -1)).normalized()
        side = axis.cross(Vector((1, 0, 0))).normalized()
        other = axis.cross(side)
        for i in range(58):
            t = (i + .7) / 58
            angle = GOLDEN_ANGLE * i
            out = side * math.cos(angle) + other * math.sin(angle)
            cross = axis.cross(out)
            radius = width * (.16 + .87 * math.sin(math.pi * t) ** .66) * (1 - .23 * t)
            scale = min(width * .53, radius * .64)
            ridge = start + axis * (height * t) + out * radius
            idx = len(self.cones.vertices)
            self.cones.vertices.extend((ridge - axis * scale * .6 - cross * scale * .43 - out * scale * .34,
                                        ridge - axis * scale * .6 + cross * scale * .43 - out * scale * .34,
                                        ridge + cross * scale * .62, ridge + axis * scale * .77 + out * scale * .26,
                                        ridge - cross * scale * .62, ridge + out * scale * .29))
            for a, b in zip(range(5), (1, 2, 3, 4, 0)):
                self.cones.add((idx+a, idx+b, idx+5), ((0, 0), (1, 0), (.5, 1)))

    def crown(self):
        rng = self.rng
        levels = [i / 65 for i in range(66)]
        trunk = [Vector((.20 * t * t + .085 * math.sin(t * 6),
                         .27 * t ** 1.4 - .082 * math.sin(t * 10), HEIGHT * t - .19))
                 for t in levels]
        radii = [max(.011, .376 * (1-t) ** 1.24 * (1 + .62 * math.exp(-t * 41)))
                 for t in levels]
        self.wood_tube(trunk, radii, 32)
        for i in range(6):
            d = horizontal(i * math.tau / 6 + rng.uniform(-.33, .33))
            reach = rng.uniform(.65, 1.1)
            points = smooth_path([UP * .53, d * reach * .38 + UP * .18, d * reach * .72 + UP * .015,
                                  d * reach - UP * .16], 3)
            self.wood_tube(points, [.15 * (1 - .88 * j / 9) for j in range(10)], 10)
        for i in range(7):
            t = rng.uniform(.066, .137)
            p = path_position(trunk, t)
            direction = horizontal(i * GOLDEN_ANGLE + rng.uniform(-.4, .4))
            size = rng.uniform(.31, 1.12)
            self.wood_tube((p, p + direction * size * .48 - UP * size * .11, p + direction * size - UP * size * .24),
                           (.039, .020, .005), 8)

        levels = (3.0, 3.8, 4.85, 5.78, 6.9, 7.76, 8.72, 9.82, 10.83, 11.91, 12.83,
                  13.9, 14.85, 15.79, 16.69, 17.67, 18.58, 19.36, 20.12, 20.83, 21.46)
        for tier, height in enumerate(levels):
            f = (height - levels[0]) / (HEIGHT - levels[0])
            count = rng.choice((4, 5, 5, 6)) if f < .74 else rng.choice((3, 4, 4))
            clock = tier * GOLDEN_ANGLE + rng.uniform(-.44, .44)
            for j in range(count):
                angle = clock + math.tau * j / count + rng.uniform(-.26, .26)
                z = height + rng.uniform(-.29, .24) * (1 - f * .61)
                anchor = path_position(trunk, (z+.19) / HEIGHT)
                reach = (5.15 * (1 - f) ** 1.02 + .17) * rng.uniform(.68, 1.17)
                if (tier + j * 3) % 11 == 3:
                    reach *= .68
                lift = reach * rng.uniform(.010, .135) + f * .37
                sag = reach * rng.uniform(.07, .15) * (1 - .58 * f)
                radius = max(.016, .162 * (1-f) ** 1.11 * rng.uniform(.72, 1.2))
                self.scaffold(anchor, angle, reach, lift, sag, radius, rng.uniform(.81, 1.12))
        for i in range(15):
            t = i / 15
            angle = i * GOLDEN_ANGLE
            self.twig(path_position(trunk, .942 + .057*t), angle, .36 * (1 - .81*t), .12 + .17*t, .58)


def needle_distribution(shoots, rng):
    shoots = np.asarray(shoots, dtype=object)
    start, elbow, tip = [np.asarray(shoots[:, i].tolist(), np.float32) for i in range(3)]
    length = np.linalg.norm(elbow-start, axis=1) + np.linalg.norm(tip-elbow, axis=1)
    counts = np.maximum(17, np.rint(length * rng.uniform(128, 182, len(length))).astype(np.int32))
    return shoots, start, elbow, tip, counts


def build_needles(shoots):
    rng = np.random.default_rng(74086)
    shoots, start, elbow, tip, counts = needle_distribution(shoots, rng)
    shoot = np.repeat(np.arange(len(counts)), counts)
    total = len(shoot)
    local_index = np.arange(total) - np.repeat(np.cumsum(counts)-counts, counts)
    t = ((local_index + rng.uniform(.15, .86, total)) / counts[shoot]).astype(np.float32)
    head, bend, end = start[shoot], elbow[shoot], tip[shoot]
    first = t < .53
    centers = np.where(first[:, None], head + (bend-head)*(t/.53)[:, None],
                       bend + (end-bend)*((t-.53)/.47)[:, None])
    forward = normalize(np.where(first[:, None], bend-head, end-bend))
    across = normalize(np.cross(forward, (0, 0, 1)))
    upper = np.cross(across, forward)
    phi = local_index * GOLDEN_ANGLE + np.asarray(shoots[:, 3], dtype=np.float32)[shoot] * math.tau
    radial = across * np.cos(phi)[:, None] + upper * np.sin(phi)[:, None]
    direction = normalize(radial + forward * rng.uniform(.05, .80, (total, 1)))
    # Broaden slender needles for readable coverage at gameplay distances.
    blade_length = rng.uniform(.034, .066, (total, 1)) * (1 - .22 * t[:, None] ** 5)
    radius = rng.uniform(.00250, .00470, (total, 1))
    width = normalize(np.cross(direction, forward)) * radius
    thickness = np.cross(direction, width) * .73
    # The four-sided base is seated in the woody shoot, so it needs no cap.
    base = centers + radial * (.0008 + .0013*(1-t))[:, None]
    needle_tip = base + direction * blade_length + forward * blade_length * .16
    vertices = np.stack((base+width, base+thickness, base-width, base-thickness, needle_tip), axis=1).astype(np.float32)
    np.rint(vertices * 32768, out=vertices)
    vertices /= 32768
    age = np.asarray(shoots[:, 4], dtype=np.float32)[shoot] * .68 + .32 * t
    age = np.rint(np.clip(age + rng.uniform(-.22, .22, total), .025, .96) * 64).astype(np.float32) / 64
    return vertices, age, sky_visibility((base + needle_tip) * .5)


def sky_visibility(positions):
    # Integrate local needle density in several upward directions, keeping direct shadows dynamic.
    grid_step = .24
    cells = np.floor((positions - positions.min(axis=0)) / grid_step).astype(np.int32) + 1
    shape = cells.max(axis=0) + 2
    occupancy = np.zeros(tuple(shape), dtype=np.float32)
    np.add.at(occupancy, tuple(cells.T), 1)
    occupancy = 1 - np.exp(-occupancy / 41)
    visibility = np.zeros(len(cells), np.float32)
    for ray in range(10):
        vertical = .21 + .74 * (ray+.5) / 10
        horizontal_length = math.sqrt(1 - vertical * vertical)
        direction = np.array((horizontal_length * math.cos(ray*GOLDEN_ANGLE),
                              horizontal_length * math.sin(ray*GOLDEN_ANGLE), vertical))
        opacity = np.zeros(len(cells), np.float32)
        for distance in (1.6, 2.8, 4.2, 5.9, 8.0, 11.1, 15.7):
            sample = np.clip(cells + np.rint(direction * distance).astype(np.int32), 0, shape - 1)
            opacity += occupancy[tuple(sample.T)]
        visibility += np.exp(-opacity * .69)
    return .43 + .57 * visibility / 10


def needle_mesh(vertices, age, sky, needles_mat):
    count = len(vertices)
    tris = np.array([(4, 0, 1), (4, 1, 2), (4, 2, 3), (4, 3, 0)], dtype=np.int32)
    mesh = bpy.data.meshes.new("Spruce needles")
    mesh.vertices.add(5 * count)
    mesh.vertices.foreach_set("co", vertices.ravel())
    mesh.loops.add(12 * count)
    indices = np.arange(count, dtype=np.int32)[:, None, None] * 5 + tris
    mesh.loops.foreach_set("vertex_index", indices.ravel())
    mesh.polygons.add(4 * count)
    mesh.polygons.foreach_set("loop_start", np.arange(0, 12*count, 3, dtype=np.int32))
    mesh.polygons.foreach_set("loop_total", np.full(4*count, 3, dtype=np.int32))
    mesh.polygons.foreach_set("use_smooth", np.ones(4*count, dtype=bool))
    mesh.update(calc_edges=True)
    uv = np.empty((count, 12, 2), dtype=np.float32)
    uv[:, :, 0] = np.where(tris.ravel() == 4, .982, .014)
    uv[:, :, 1] = age[:, None]
    layer = mesh.uv_layers.new(name="UVMap")
    layer.data.foreach_set("uv", uv.ravel())
    color = np.ones((count, 5, 4), dtype=np.float32)
    color[:, :, 3] = sky[:, None]
    mesh.color_attributes.new(name="LeafSkyVisibility", type="BYTE_COLOR", domain="POINT").data.foreach_set(
        "color", color.ravel())
    mesh.materials.append(needles_mat)
    obj = bpy.data.objects.new("Foliage", mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def main():
    root, output = [Path(arg).resolve() for arg in sys.argv[sys.argv.index("--") + 1:][:2]]
    output.mkdir(parents=True, exist_ok=True)
    maps = root / "Maps"
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tree = NorwaySpruce()
    tree.crown()
    print(f"Generated {tree.branch_count} scaffold branches and {len(tree.shoots):,} fine shoots", flush=True)
    bark = material("Norway spruce bark", maps / "BarkAlbedo.png", normal=maps / "BarkNormal.png")
    roughness = bark.node_tree.nodes.new("ShaderNodeTexImage")
    roughness.image = bpy.data.images.load(str(maps / "BarkRoughness.png"))
    roughness.image.colorspace_settings.name = "Non-Color"
    roughness.image.pack()
    bark.node_tree.links.new(roughness.outputs["Color"], bark.node_tree.nodes["Principled BSDF"].inputs["Roughness"])
    wood, wood_tris = tree.wood.finish("Bark", bark)
    needles = material("Norway spruce needles", maps / "NeedleAlbedo.png")
    bsdf = needles.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = .55
    bsdf.inputs["Specular IOR Level"].default_value = .22
    vertices, age, sky = build_needles(tree.shoots)
    print(f"Built {len(age):,} solid needles", flush=True)
    foliage = needle_mesh(vertices, age, sky, needles)
    cone_mat = bpy.data.materials.new("Weathered spruce cones")
    cone_mat.use_nodes = True
    cone_bsdf = cone_mat.node_tree.nodes["Principled BSDF"]
    cone_bsdf.inputs["Base Color"].default_value = (.115, .064, .034, 1)
    cone_bsdf.inputs["Roughness"].default_value = .86
    cones, cone_tris = tree.cones.finish("Cones", cone_mat)
    bpy.context.view_layer.objects.active = wood
    for part in (wood, foliage, cones):
        part.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(root / "HeroNorwaySpruce.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(output / "HeroNorwaySpruce.fbx"), use_selection=True,
                             object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_anim=False,
                             path_mode="STRIP", apply_scale_options="FBX_SCALE_UNITS")
    stats = {"needles": len(age), "needleTriangles": len(age) * 4, "barkTriangles": wood_tris,
             "coneTriangles": cone_tris, "heightMeters": round(float(vertices[:, :, 2].max()), 2),
             "crownSpanXYMeters": [round(float(v), 2) for v in np.ptp(vertices.reshape(-1, 3), axis=0)[:2]],
             "needleSkyVisibilityMinMeanMax": [round(float(v), 4) for v in (sky.min(), sky.mean(), sky.max())]}
    (root / "Geometry.json").write_text(json.dumps(stats, indent=2) + "\n", encoding="utf-8")
    print("HERO NORWAY SPRUCE", stats, flush=True)


if __name__ == "__main__":
    main()
