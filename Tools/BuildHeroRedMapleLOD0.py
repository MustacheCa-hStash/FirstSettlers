"""Bake the approved red maple into fitted sprays with packed normals and occlusion."""

import argparse
import json
import math
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, export, extract_master, write_bark


NAME = "HeroRedMapleLOD0"
SOURCE = ROOT / "ArtSource/HeroRedMaple"


def extract(work, tree_type=None, source=SOURCE):
    from BuildHeroBeech import Beech
    from BuildHeroRedMaple import RedMaple

    class BranchLayout(tree_type or RedMaple):
        def __init__(self, rows):
            super().__init__(rows)
            self.count = 0
            self.branches = []
            self.wood_layout = []
            self.in_scaffold = False
            self.keep_fan = False

        def scaffold(self, *args):
            self.branches.append(self.count)
            self.in_scaffold = True
            self.keep_fan = True
            super().scaffold(*args)
            self.in_scaffold = False

        def wood_tube(self, points, radii, sides):
            rings, width = (), 0
            if sides == 28:
                rings, width = (0, 2, 4, 7, 10, 13, 16, 20, 24, 28, 32, 36, 40, 44, 48), 11
            elif sides == 19 and not self.in_scaffold:
                rings, width = (*range(0, len(points) - 1, 4), len(points) - 1), 7
            elif sides == 12:
                rings, width = (0, 4, 9), 5
            elif sides in (13, 19) and self.in_scaffold:
                rings = (0, 3, 7, 12, 16, 20) if radii[0] > .13 else (0, 4, 10, 15, 20)
                width = 5 if radii[0] > .13 else 4
            elif sides == 7 and self.in_scaffold and self.keep_fan and radii[0] > .042:
                rings, width = (0, 2, 4), 4
                self.keep_fan = False
            if rings:
                self.wood_layout.append((self.rng.getstate(), [points[i] for i in rings],
                                         [radii[i] for i in rings], width))
            self.rng.uniform(0, math.tau)
            self.rng.random()
            self.rng.random()

        def leaf(self, anchor, axis, crown_normal, length, **kwargs):
            # Replay the authored branch layout while reading the saved leaf surfaces.
            for _ in range(5):
                self.rng.random()
            self.rng.randrange(len(self.rows))
            self.count += 1

    rows = json.loads((source / "Maps/LeafRows.json").read_text())
    layout = BranchLayout(rows)
    layout.crown()
    layout.branches.append(layout.count)
    extract_master(work, layout.count, layout.branches, rows_per_leaf=len(rows[0]))
    for state, points, radii, width in layout.wood_layout:
        layout.rng.setstate(state)
        Beech.wood_tube(layout, points, radii, width)
    write_bark(work, layout.wood)


def main():
    if "--" in sys.argv:
        phase, work, output = sys.argv[sys.argv.index("--") + 1:]
        if phase == "extract":
            extract(Path(work))
        else:
            export(Path(work), Path(output), SOURCE, NAME, "Red Maple", "CrownAlbedoBase.png")
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    args = parser.parse_args()
    work, output = ROOT / ".utmp/hero-red-maple-lod0", ROOT / "ArtSource" / NAME
    for directory in (work, output):
        directory.mkdir(parents=True, exist_ok=True)
    command = [str(args.blender), "-b", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    if not args.reuse_extract:
        subprocess.run(command + [str(SOURCE / "HeroRedMaple.blend")] + script +
                       ["--", "extract", str(work), str(output)], check=True)
    bake(work, source=SOURCE, fans_per_branch=9, coverage_weighted_surface=True,
         contact_radius=.12, tile_size=160, crossed_sprays=True)
    from PIL import Image

    # Match the source crown under direct sun while leaving the packed surface data intact.
    lookup = [round(value * gain) for gain in (.95, .91, .96, 1) for value in range(256)]
    albedo = work / "CrownAlbedoBase.png"
    with Image.open(albedo) as image:
        graded = image.point(lookup)
    graded.save(albedo)
    subprocess.run(command + ["--factory-startup"] + script +
                   ["--", "export", str(work), str(output)], check=True)


if __name__ == "__main__":
    main()
