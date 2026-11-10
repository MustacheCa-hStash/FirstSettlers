"""Build runtime foliage cards from the three approved pioneer-tree masters."""

import argparse
import json
import math
from pathlib import Path
import subprocess
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, export, extract_master, write_bark
from BuildHeroNorwaySpruceLOD0 import allocate_fans


SPECIES = {
    "PaperBirch": ("Paper Birch", "LeafAlbedo.png", .105, (.97, .95, 1.025, 1)),
    "QuakingAspen": ("Quaking Aspen", "LeafAlbedo.png", .113, (.96, .95, 1.03, 1)),
    "EasternWhitePine": ("Eastern White Pine", "NeedleAlbedo.png", .095, (.96, .95, 1.02, 1)),
}


def replay_tube(layout, points, radii, rings, width):
    if rings:
        layout.wood_layout.append((layout.rng.getstate(), [points[i] for i in rings],
                                   [radii[i] for i in rings], width))
    layout.rng.uniform(0, math.tau)
    layout.rng.random()
    layout.rng.random()


def build_bark(work, layout):
    from BuildHeroBeech import Beech

    for state, points, radii, width in layout.wood_layout:
        layout.rng.setstate(state)
        Beech.wood_tube(layout, points, radii, width)
    for start, height, width, axis in sorted(getattr(layout, "cones_for_lod", ()), key=lambda item: -item[1])[:7]:
        points = [start + axis * height * t for t in (0, .33, .78, 1)]
        Beech.wood_tube(layout, points, [width * r for r in (.26, .88, .63, .09)], 4)
    write_bark(work, layout.wood)
    return sum(len(face) - 2 for face in layout.wood.faces)


def extract_broadleaf(work, source, species):
    from BuildHeroPioneerBroadleaves import PaperBirch, QuakingAspen

    tree_type = PaperBirch if species == "PaperBirch" else QuakingAspen

    class BranchLayout(tree_type):
        def __init__(self, rows):
            super().__init__(rows)
            self.count = 0
            self.branches = []
            self.wood_layout = []
            self.in_scaffold = False
            self.keep_spray = False

        def scaffold(self, *args):
            self.branches.append(self.count)
            self.in_scaffold = self.keep_spray = True
            super().scaffold(*args)
            self.in_scaffold = False

        def wood_tube(self, points, radii, sides):
            rings, width = (), 0
            if sides in (26, 29):
                rings, width = (0, 2, 4, 7, 10, 13, 17, 21, 25, 29, 33, 37, 40), 10
            elif sides in (16, 19) and not self.in_scaffold:
                rings, width = tuple(range(0, len(points), 3)) + (len(points)-1,), 6
                rings = tuple(dict.fromkeys(rings))
            elif sides in (9, 10):
                rings, width = (0, len(points)//2, len(points)-1), 4
            elif sides in (12, 17):
                rings = (0, 3, 7, 12, 16, 20) if radii[0] > .095 else (0, 5, 12, 20)
                width = 5 if radii[0] > .095 else 4
            elif sides == 6 and self.keep_spray and radii[0] > .031:
                rings, width = (0, 2, 4), 4
                self.keep_spray = False
            replay_tube(self, points, radii, rings, width)

        def leaf(self, anchor, axis, crown_normal, length):
            # Replay the five shape draws and the texture choice from the master.
            for _ in range(5):
                self.rng.random()
            self.rng.randrange(len(self.rows))
            self.count += 1

    rows = json.loads((source / "Maps/LeafRows.json").read_text())
    layout = BranchLayout(rows)
    layout.crown()
    branches = layout.branches + [layout.count]
    extract_master(work, layout.count, branches, rows_per_leaf=len(rows[0]))
    bark_triangles = build_bark(work, layout)
    fan_counts = allocate_fans(branches, bark_triangles)
    # Keep the existing archive schema used by the shared surface baker.
    master = work / "Master.npz"
    with np.load(master) as data:
        geometry = dict(data)
    np.savez(master, **geometry, fan_counts=fan_counts)
    print(f"{species}: {layout.count:,} leaves, {fan_counts.sum()*3} cards, {bark_triangles} bark triangles", flush=True)


def extract_pine(work):
    import bpy
    from mathutils import Vector
    from BuildHeroEasternWhitePine import EasternWhitePine, needle_distribution

    class BranchLayout(EasternWhitePine):
        def __init__(self):
            super().__init__()
            self.branches = []
            self.wood_layout = []
            self.cones_for_lod = []

        def scaffold(self, *args):
            self.branches.append(len(self.shoots))
            super().scaffold(*args)

        def wood_tube(self, points, radii, sides):
            rings, width = (), 0
            if sides == 34:
                rings, width = (0, 1, 2, 4, 7, 12, 18, 25, 32, 40, 48, 57, 64, 71), 11
            elif sides == 13:
                rings, width = (0, 4, 9), 5
            elif sides == 9:
                rings, width = (0, 1, 2), 4
            elif sides in (11, 17):
                if radii[0] > .105:
                    rings = (0, 5, 11, 17, 20)
                else:
                    rings = (0, 10, 20) if radii[0] > .048 else (0, 20)
                width = 5 if radii[0] > .095 else 4
            replay_tube(self, points, radii, rings, width)

        def add_cone(self, start, height, width):
            axis = Vector((self.rng.uniform(-.18, .18), self.rng.uniform(-.18, .18), -1)).normalized()
            self.cones_for_lod.append((start, height, width, axis))

    layout = BranchLayout()
    layout.crown()
    _, _, _, _, counts = needle_distribution(layout.shoots, np.random.default_rng(506972))
    cumulative = np.concatenate(([0], np.cumsum(counts) * 5))
    branches = cumulative[layout.branches + [len(counts)]]
    bark_triangles = build_bark(work, layout)
    fan_counts = allocate_fans(branches, bark_triangles)

    mesh = bpy.data.objects["Foliage"].data
    count = int(branches[-1])
    if len(mesh.vertices) != count * 7 or len(mesh.polygons) != count * 9:
        raise ValueError("Saved pine needles no longer match the authored branch layout.")
    xyz = np.empty((count, 7, 3), np.float32)
    normals = np.empty_like(xyz)
    mesh.vertices.foreach_get("co", xyz.ravel())
    mesh.vertices.foreach_get("normal", normals.ravel())
    indices = np.empty((count, 9, 3), np.int32)
    mesh.loops.foreach_get("vertex_index", indices.ravel())
    faces = indices[0].copy()
    if not np.array_equal(indices[-1] - 7 * (count - 1), faces):
        raise ValueError("Saved pine needle topology differs from the template.")
    del indices
    colors = np.empty((count, 7, 4), np.float32)
    mesh.color_attributes["LeafSkyVisibility"].data.foreach_get("color", colors.ravel())
    sky = colors[:, 0, 3].copy()
    del colors
    uvs = np.empty((count, 27, 2), np.float32)
    mesh.uv_layers[0].data.foreach_get("uv", uvs.ravel())
    vertex_uv = np.empty((count, 7, 2), np.float32)
    vertex_uv[:, :, 0] = (.012, .012, .012, .515, .515, .515, .986)
    vertex_uv[:, :, 1] = uvs[:, :1, 1]
    del uvs
    radial = xyz[:, :, :2].mean(axis=1)
    up = np.maximum(.22, np.linalg.norm(radial, axis=1) * .65)
    packed = .5 + .5 * radial / (np.abs(radial).sum(axis=1) + up)[:, None]
    np.savez(work / "Master.npz", xyz=xyz, normals=normals, vertex_uv=vertex_uv,
             faces=faces, packed=packed, sky=sky, branches=branches, fan_counts=fan_counts)
    print(f"EasternWhitePine: {count:,} needles, {fan_counts.sum()*3} cards, {bark_triangles} bark triangles", flush=True)


def main():
    if "--" in sys.argv:
        phase, species, work, output = sys.argv[sys.argv.index("--") + 1:]
        source = ROOT / "ArtSource" / ("Hero" + species)
        if phase == "export":
            export(Path(work), Path(output), source, "Hero" + species + "LOD0",
                   SPECIES[species][0], "CrownAlbedoBase.png")
        elif species == "EasternWhitePine":
            extract_pine(Path(work))
        else:
            extract_broadleaf(Path(work), source, species)
        return

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("species", choices=SPECIES)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    args = parser.parse_args()
    from PIL import Image

    species = args.species
    source = ROOT / "ArtSource" / ("Hero" + species)
    work = ROOT / ".utmp/hero-pioneer-lod0" / species
    output = source.with_name(source.name + "LOD0")
    for folder in (work, output):
        folder.mkdir(parents=True, exist_ok=True)
    command = [str(args.blender), "-b", "-t", "4", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    suffix = [species, str(work), str(output)]
    if not args.reuse_extract:
        subprocess.run(command + [str(source / (source.name + ".blend"))] + script +
                       ["--", "extract"] + suffix, check=True)
    _, texture, radius, tint = SPECIES[species]
    bake(work, source=source, coverage_weighted_surface=True, leaf_texture=texture,
         contact_radius=radius, tile_size=136, crossed_sprays=True)
    lookup = [min(255, round(value * gain)) for gain in tint for value in range(256)]
    albedo = work / "CrownAlbedoBase.png"
    with Image.open(albedo) as image:
        graded = image.point(lookup)
    graded.save(albedo)
    subprocess.run(command + ["--factory-startup"] + script + ["--", "export"] + suffix, check=True)


if __name__ == "__main__":
    main()
