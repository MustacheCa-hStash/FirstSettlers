"""Bake the spruce master into fitted cards with packed needle normals and occlusion."""

import argparse
import math
from pathlib import Path
import subprocess
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, export, write_bark


NAME = "HeroNorwaySpruceLOD0"
SOURCE = ROOT / "ArtSource/HeroNorwaySpruce"
TARGET_TRIANGLES = 4880
VIEWS_PER_FAN = 3
TRIANGLES_PER_CARD = 4


def allocate_fans(branches, bark_triangles):
    needles = np.diff(branches)
    remaining = (TARGET_TRIANGLES - bark_triangles) // (VIEWS_PER_FAN * TRIANGLES_PER_CARD) - len(needles)
    if remaining < 0:
        raise ValueError("The woody mesh leaves too few triangles for the canopy.")
    weights = np.sqrt(needles)
    desired = remaining * weights / weights.sum()
    extra = desired.astype(int)
    ranked = np.argsort(-(desired - extra), kind="stable")
    extra[ranked[:remaining - extra.sum()]] += 1
    return 1 + extra


def extract(work):
    import bpy
    from mathutils import Vector
    from BuildHeroBeech import Beech
    from BuildHeroNorwaySpruce import NorwaySpruce, needle_distribution

    class BranchLayout(NorwaySpruce):
        def __init__(self):
            super().__init__()
            self.branches = []
            self.wood_layout = []
            self.cone_layout = []

        def scaffold(self, *args):
            self.branches.append(len(self.shoots))
            super().scaffold(*args)

        def wood_tube(self, points, radii, sides):
            rings, width = (), 0
            if sides == 32:
                rings, width = (0, 1, 2, 4, 7, 12, 18, 25, 33, 42, 52, 61, 65), 10
            elif sides == 10:
                rings, width = (0, 4, 9), 4
            elif sides in (9, 13):
                rings = (0, 8, 15) if (points[-1] - points[0]).length > 1.55 else (0, 15)
                width = 5 if radii[0] > .095 else 4
                if radii[0] > .086:
                    rings, width = (0, 3, 6, 10, 13, 15), 4
            if rings:
                self.wood_layout.append((self.rng.getstate(), [points[i] for i in rings],
                                         [radii[i] for i in rings], width))
            self.rng.uniform(0, math.tau)
            self.rng.random()
            self.rng.random()

        def add_cone(self, start, height, width):
            axis = Vector((self.rng.uniform(-.18, .18), self.rng.uniform(-.18, .18), -1)).normalized()
            self.cone_layout.append((start, height, width, axis))

    layout = BranchLayout()
    layout.crown()
    _, _, _, _, counts = needle_distribution(layout.shoots, np.random.default_rng(74086))
    cumulative = np.concatenate(([0], np.cumsum(counts)))
    branches = cumulative[layout.branches + [len(counts)]]
    for state, points, radii, width in layout.wood_layout:
        layout.rng.setstate(state)
        Beech.wood_tube(layout, points, radii, width)
    # Keep the largest cones in the shared woody draw.
    for start, height, width, axis in sorted(layout.cone_layout, key=lambda item: -item[1])[:8]:
        points = [start + axis * height * t for t in (0, .33, .78, 1)]
        Beech.wood_tube(layout, points, [width * r for r in (.26, .88, .63, .09)], 4)
    write_bark(work, layout.wood)
    bark_triangles = sum(len(face) - 2 for face in layout.wood.faces)
    fan_counts = allocate_fans(branches, bark_triangles)

    mesh = bpy.data.objects["Foliage"].data
    needle_count = int(branches[-1])
    if len(mesh.vertices) != needle_count * 5 or len(mesh.polygons) != needle_count * 4:
        raise ValueError("Saved needles no longer match the authored spruce branch layout.")
    xyz = np.empty((needle_count, 5, 3), np.float32)
    normals = np.empty_like(xyz)
    mesh.vertices.foreach_get("co", xyz.ravel())
    mesh.vertices.foreach_get("normal", normals.ravel())
    indices = np.empty((needle_count, 4, 3), np.int32)
    mesh.loops.foreach_get("vertex_index", indices.ravel())
    faces = indices[0].copy()
    if not np.array_equal(indices[-1] - 5 * (needle_count - 1), faces):
        raise ValueError("Saved needle topology differs from the template.")
    del indices
    colors = np.empty((needle_count, 5, 4), np.float32)
    mesh.color_attributes["LeafSkyVisibility"].data.foreach_get("color", colors.ravel())
    sky = colors[:, 0, 3].copy()
    del colors
    uvs = np.empty((needle_count, 12, 2), np.float32)
    mesh.uv_layers[0].data.foreach_get("uv", uvs.ravel())
    vertex_uv = np.empty((needle_count, 5, 2), np.float32)
    vertex_uv[:, :, 0] = (.014, .014, .014, .014, .982)
    vertex_uv[:, :, 1] = uvs[:, :1, 1]
    del uvs
    radial = xyz[:, :, :2].mean(axis=1)
    up = np.maximum(.22, np.linalg.norm(radial, axis=1) * .65)
    packed = .5 + .5 * radial / (np.abs(radial).sum(axis=1) + up)[:, None]
    np.savez(work / "Master.npz", xyz=xyz, normals=normals, vertex_uv=vertex_uv,
             faces=faces, packed=packed, sky=sky, branches=branches, fan_counts=fan_counts)
    print(f"Prepared {needle_count:,} needles across {len(fan_counts)} scaffolds, "
          f"{VIEWS_PER_FAN*fan_counts.sum()} cards and {bark_triangles} woody triangles", flush=True)


def grade_albedo(work):
    from PIL import Image

    # The layered 3D needles self-shadow more than a card under the same sun.
    # A small color adjustment brings the runtime crown closer to the master.
    lookup = [round(value * gain) for gain in (.92, .90, .94, 1) for value in range(256)]
    for name in ("CrownAlbedo.png", "CrownAlbedoBase.png"):
        path = work / name
        with Image.open(path) as image:
            graded = image.point(lookup)
        graded.save(path)


def main():
    if "--" in sys.argv:
        phase, work, output = sys.argv[sys.argv.index("--") + 1:]
        if phase == "extract":
            extract(Path(work))
        else:
            export(Path(work), Path(output), SOURCE, NAME, "Norway spruce", "CrownAlbedoBase.png")
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    args = parser.parse_args()
    work, output = ROOT / ".utmp/hero-norway-spruce-lod0", ROOT / "ArtSource" / NAME
    for folder in (work, output):
        folder.mkdir(parents=True, exist_ok=True)
    command = [str(args.blender), "-b", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    if not args.reuse_extract:
        subprocess.run(command + [str(SOURCE / "HeroNorwaySpruce.blend")] + script +
                       ["--", "extract", str(work), str(output)], check=True)
    bake(work, source=SOURCE, coverage_weighted_surface=True, leaf_texture="NeedleAlbedo.png",
         contact_radius=.084, tile_size=160, crossed_sprays=True)
    grade_albedo(work)
    subprocess.run(command + ["--factory-startup"] + script +
                   ["--", "export", str(work), str(output)], check=True)


if __name__ == "__main__":
    main()
