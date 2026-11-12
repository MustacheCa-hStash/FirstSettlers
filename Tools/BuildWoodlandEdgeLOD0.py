"""Bake the approved woodland edge masters into runtime-sized meshes and atlases.

Run with CPython (numpy, scipy, Pillow and numba), passing the Blender executable.
"""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, divide_fans, export, write_bark


# Source, leaf rows, spatial clusters, and maximum permitted triangle count.
FORMS = {
    "LowBranchedWhiteOak": (0, 11, 235, 5000, "WhiteOak"),
    "MultistemEdgeBeechV2": (1, 7, 235, 5000, "AmericanBeech"),
    "HazelThicket": (2, 7, 105, 2500, "AmericanBeech"),
}
SOURCE = ROOT / "ArtSource/WoodlandEdgeHighPoly"
OUTPUT = ROOT / "ArtSource/WoodlandEdgeLOD0"


def crown_sky_visibility(centers):
    from scipy.ndimage import gaussian_filter, map_coordinates

    cell = .22
    origin = centers.min(axis=0) - cell * 3
    points = (centers - origin) / cell
    bins = np.floor(points).astype(int)
    density = np.zeros(bins.max(axis=0) + 4, dtype=np.float32)
    np.add.at(density, tuple(bins.T), 1)
    density = gaussian_filter(density, sigma=1.)
    visibility = np.zeros(len(centers), dtype=np.float32)
    for ray in range(12):
        z = (ray + .5) / 12
        angle = ray * 2.39996323
        radial = np.sqrt(1 - z*z)
        direction = np.array([radial*np.cos(angle), radial*np.sin(angle), z])
        optical_depth = np.zeros_like(visibility)
        for step in range(2, int(np.linalg.norm(density.shape))):
            optical_depth += map_coordinates(density, (points + direction*step - .5).T,
                                             order=1, mode="constant", cval=0.)
        visibility += np.exp(-.34 * optical_depth)
    return .20 + .80 * visibility / 12


def extract(name, work):
    import bpy
    from BuildWoodlandEdgeHighPoly import Specimen
    from BuildWoodlandTrees import Tree

    kind, rows, clusters, _, _ = FORMS[name]

    class BranchLayout(Specimen):
        def __init__(self):
            super().__init__(name, kind)
            self.tubes = []
            self.leaf_count = 0

        def leaf(self, point, axis, length, roll):
            self.rng.uniform(.15, .95)
            self.leaf_count += 1

        def tube(self, points, radii, sides, root=False):
            if sides == 20:
                width = 10 if kind < 2 else 6
            elif sides == 10 and (kind < 2 or radii[0] >= .011):
                width = 4 if kind < 2 else 3
            else:
                width = 0
            if width:
                self.tubes.append((self.rng.getstate(), points, radii, width, root))
            # Match the source's RNG consumption even for discarded twigs.
            for _ in range(3):
                self.rng.random()

    model = BranchLayout()
    model.build()
    for state, points, radii, width, root in model.tubes:
        model.rng.setstate(state)
        Tree.tube(model, points, radii, width, root)
    write_bark(work, model.wood)

    mesh = bpy.data.objects["Individual curved leaf geometry"].data
    per_leaf = rows * 3
    quads = (rows - 1) * 2
    if len(mesh.vertices) != model.leaf_count * per_leaf or len(mesh.polygons) != model.leaf_count * quads:
        raise ValueError(f"{name}: geometry and replayed branch layout disagree")
    xyz = np.empty(len(mesh.vertices) * 3, np.float32)
    normals = np.empty_like(xyz)
    mesh.vertices.foreach_get("co", xyz)
    mesh.vertices.foreach_get("normal", normals)
    xyz = xyz.reshape(-1, per_leaf, 3)
    normals = normals.reshape(xyz.shape)
    centers = xyz.mean(axis=1)
    groups = divide_fans(np.arange(len(centers)), centers, clusters)
    order = np.concatenate(groups)
    boundaries = np.cumsum([0] + [len(group) for group in groups])

    # The source uses one identical UV grid for every individually modeled leaf.
    vertex_uv = np.array([(u, row / (rows - 1)) for row in range(rows) for u in (0., .5, 1.)], np.float32)
    vertex_ids = np.empty(len(mesh.loops), np.int32)
    mesh.loops.foreach_get("vertex_index", vertex_ids)
    faces = vertex_ids[:quads * 4].reshape(-1, 4)
    if not np.array_equal(vertex_ids.reshape(-1, quads, 4) -
                          (np.arange(len(centers), dtype=np.int32) * per_leaf)[:, None, None],
                          np.broadcast_to(faces, (len(centers), quads, 4))):
        raise ValueError(f"{name}: source leaf grids are no longer uniform")

    lo, hi = centers.min(axis=0), centers.max(axis=0)
    crown_normal = (centers - (lo + hi) * .5) / np.maximum((hi - lo) * .5, .1)
    crown_normal /= np.maximum(np.abs(crown_normal).sum(axis=1)[:, None], 1e-6)
    packed = crown_normal[:, :2].copy()
    below = crown_normal[:, 2] < 0
    packed[below] = (1 - np.abs(packed[below, ::-1])) * np.where(packed[below] < 0, -1, 1)
    tone = np.empty(len(mesh.vertices), np.float32)
    mesh.attributes["LeafVariation"].data.foreach_get("value", tone)
    variation = np.clip(tone[::per_leaf][order], 0., 1.)
    leaf_lighting = {"sky": variation}
    if name == "LowBranchedWhiteOak":
        leaf_lighting = {
            "sky": variation,
            "leaf_tone": .76 + .28 * variation,
        }
    np.savez(work / "Master.npz", xyz=xyz[order], normals=normals[order], faces=faces,
             vertex_uv=np.broadcast_to(vertex_uv, (len(centers), per_leaf, 2)),
             branches=boundaries, packed=(packed * .5 + .5)[order],
             **leaf_lighting)


def make_source_maps(work, name):
    from PIL import Image

    kind, _, _, _, bark = FORMS[name]
    maps = work / "Maps"
    maps.mkdir(exist_ok=True)
    shutil.copyfile(ROOT / "Assets/Textures/Trees/Woodland" / (bark + "_Bark.png"), maps / "BarkAlbedo.png")
    # Bake the master material's palette and vein pattern into leaf-space
    # before rasterizing the real leaf geometry into the crown atlas.
    rng = np.random.default_rng(827 + kind)
    size = 256
    y, x = np.mgrid[:size, :size] / (size - 1)
    across = np.abs(x - .5)
    midrib = across < .014
    vein = ((1 - y - across * .6) * 8 % 1) < .034
    veining = np.maximum(midrib, .45 * vein) * .36
    dark = np.array(((.019, .047, .011), (.017, .044, .011), (.012, .033, .010))[kind])
    light = np.array(((.102, .176, .035), (.088, .16, .032), (.064, .125, .030))[kind])
    noise = np.asarray(Image.fromarray(rng.integers(0, 256, (32, 32), dtype=np.uint8)).resize(
        (size, size), Image.Resampling.BICUBIC), dtype=np.float32) / 255
    linear = (dark + (.46 + noise[..., None] * .23) * (light - dark))
    linear = linear * (1 - veining[..., None]) + np.array((.17, .23, .064)) * veining[..., None]
    rgb = np.uint8(np.rint((1.055 * linear ** (1 / 2.4) - .055) * 255))
    Image.fromarray(np.dstack((rgb, np.full((size, size), 255, np.uint8)))).save(maps / "LeafAlbedo.png")

    with Image.open(maps / "BarkAlbedo.png") as image:
        height = np.asarray(image.convert("L"), np.float32) / 255.
    dy, dx = np.gradient(height)
    normal = np.dstack((-dx * 3, dy * 3, np.ones_like(height)))
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    Image.fromarray(np.uint8(np.rint((normal * .5 + .5) * 255))).save(maps / "BarkNormal.png")


def finish(name, work, output):
    import bpy
    from mathutils import Vector
    from BuildWoodlandTrees import render

    lod = name + "LOD0"
    export(work, output, source=work, name=lod, species=name, albedo="CrownAlbedoBase.png")
    preview_surface(bpy.data.objects["Foliage"].active_material, work / "CrownSurface.png")
    bpy.ops.wm.save_as_mainfile(filepath=str(output / (lod + ".blend")), compress=True)
    stats = json.loads((output / "Geometry.json").read_text())
    triangles = stats["barkTriangles"] + stats["leafTriangles"]
    kind, _, _, ceiling, _ = FORMS[name]
    if triangles > ceiling or (kind < 2 and triangles < 4000):
        raise ValueError(f"{name}: {triangles} triangles outside budget")
    print(f"LOD0 {name}: {triangles:,} triangles, {stats['cards']} cards", flush=True)

    # Use the master studio and camera for matched geometry comparisons.
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / (name + ".blend")), load_ui=False)
    masters = [bpy.data.objects[n] for n in ("Branch and trunk geometry", "Individual curved leaf geometry")]
    bounds = np.array([p for obj in masters for p in obj.bound_box])
    lo, hi = bounds.min(axis=0), bounds.max(axis=0)
    height = hi[2]
    span = max(height, hi[0] - lo[0], hi[1] - lo[1])
    for obj in masters:
        bpy.data.objects.remove(obj, do_unlink=True)
    with bpy.data.libraries.load(str(output / (lod + ".blend"))) as (_, target):
        target.objects = ["Bark", "Foliage"]
    for obj in target.objects:
        bpy.context.collection.objects.link(obj)
    scene = bpy.context.scene
    middle = (lo + hi) * .5
    target = Vector((middle[0], middle[1], height * .48))
    distance = span * 2.8
    for position, suffix in (
        ((distance * .16, -distance, height * .63), "Front"),
        ((-distance * .82, -distance * .56, height * .82), "ThreeQuarter"),
    ):
        render(scene, scene.camera, position, target, output / (lod + "-" + suffix + ".png"))


def preview_surface(material, atlas):
    """Preview the same packed XY normals that the standing-tree shader decodes."""
    import bpy

    nodes, links = material.node_tree.nodes, material.node_tree.links
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(atlas), check_existing=True)
    texture.image.colorspace_settings.name = "Non-Color"
    texture.image.pack()
    channels = nodes.new("ShaderNodeSeparateColor")
    links.new(texture.outputs["Color"], channels.inputs[0])

    def math_node(operation, first, second=None):
        node = nodes.new("ShaderNodeMath")
        node.operation = operation
        for i, operand in enumerate((first, second)):
            if isinstance(operand, (int, float)):
                node.inputs[i].default_value = operand
            elif operand is not None:
                links.new(operand, node.inputs[i])
        return node.outputs[0]

    red, green = channels.outputs["Red"], channels.outputs["Green"]
    x = math_node("SUBTRACT", math_node("MULTIPLY", red, 2.), 1.)
    y = math_node("SUBTRACT", math_node("MULTIPLY", green, 2.), 1.)
    length_sq = math_node("ADD", math_node("MULTIPLY", x, x), math_node("MULTIPLY", y, y))
    z = math_node("SQRT", math_node("MAXIMUM", .001, math_node("SUBTRACT", 1., length_sq)))
    combined = nodes.new("ShaderNodeCombineColor")
    for i, value in enumerate((red, green, math_node("ADD", .5, math_node("MULTIPLY", z, .5)))):
        links.new(value, combined.inputs[i])
    normal = nodes.new("ShaderNodeNormalMap")
    normal.inputs["Strength"].default_value = .76
    links.new(combined.outputs[0], normal.inputs["Color"])
    bsdf = nodes.get("Principled BSDF")
    links.new(normal.outputs[0], bsdf.inputs["Normal"])
    # Blender's studio preview approximates the runtime ambient occlusion.
    # Unity applies the same channel to indirect lighting in the leaf shader.
    albedo = bsdf.inputs["Base Color"].links[0].from_socket
    ao = nodes.new("ShaderNodeMixRGB")
    ao.blend_type = "MULTIPLY"
    ao.inputs[0].default_value = .60
    links.new(albedo, ao.inputs[1])
    links.new(channels.outputs["Blue"], ao.inputs[2])
    links.new(ao.outputs[0], bsdf.inputs["Base Color"])


def main():
    if "--" in sys.argv:
        phase, name, scratch, output = sys.argv[sys.argv.index("--") + 1:]
        (extract(name, Path(scratch)) if phase == "extract" else finish(name, Path(scratch), Path(output)))
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--model", choices=tuple(FORMS))
    parser.add_argument("--reuse-bake", action="store_true")
    args = parser.parse_args()
    command = [str(args.blender), "-b", "--disable-autoexec", "-t", "12", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve()), "--"]
    for name in ([args.model] if args.model else FORMS):
        work = ROOT / ".utmp/woodland-edge-lod0" / name
        output = OUTPUT / name
        for directory in (work, output):
            directory.mkdir(parents=True, exist_ok=True)
        if not args.reuse_bake:
            subprocess.run(command + [str(SOURCE / (name + ".blend"))] + script +
                           ["extract", name, str(work), str(output)], check=True)
            make_source_maps(work, name)
            if name == "LowBranchedWhiteOak":
                master = work / "Master.npz"
                with np.load(master) as source:
                    data = dict(source)
                data["sky"] = crown_sky_visibility(data["xyz"].mean(axis=1))
                np.savez(master, **data)
            bake(work, source=work, fans_per_branch=1, tile_size=128, crossed_sprays=True,
                 contact_radius=.07 if name == "HazelThicket" else .14, coverage_weighted_surface=True)
        subprocess.run(command + ["--factory-startup"] + script +
                       ["finish", name, str(work), str(output)], check=True)
        for filename in (name + "LOD0.fbx", "CrownAlbedo.png", "CrownAlbedoBase.png", "CrownSurface.png", "BarkAlbedo.png", "BarkNormal.png"):
            src = work / ("Maps/" + filename if filename.startswith("Bark") else filename)
            shutil.copyfile(src, output / filename)


if __name__ == "__main__":
    main()
