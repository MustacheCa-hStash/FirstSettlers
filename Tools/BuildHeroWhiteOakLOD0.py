"""Bake a runtime white oak with the beech pipeline's packed leaf normals and occlusion."""

import argparse
import json
import math
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, export, extract_master, write_bark


NAME = "HeroWhiteOakLOD0"
SOURCE = ROOT / "ArtSource/HeroWhiteOak"


def extract(work):
    from BuildHeroBeech import Beech
    from BuildHeroWhiteOak import WhiteOak

    class BranchLayout(WhiteOak):
        def __init__(self, rows):
            super().__init__(rows)
            self.count = 0
            self.branches = []
            self.wood_layout = []
            self.keep_spray = False

        def scaffold(self, *args):
            self.branches.append(self.count)
            self.keep_spray = True
            super().scaffold(*args)

        def wood_tube(self, points, radii, sides):
            rings, width = (), 0
            if sides == 38:
                rings, width = (0, 2, 4, 7, 10, 14, 18, 22, 26, 30, 34, 38, 43, 48, 52, 56), 12
            elif sides == 17:
                rings, width = (0, 5, 9, 12), 5
            elif sides in (23, 15):
                rings, width = (0, 3, 7, 12, 16, 20), 5
            elif sides == 8 and self.keep_spray and radii[0] > .04:
                rings, width = (0, 2, 5), 4
                self.keep_spray = False
            if rings:
                self.wood_layout.append((self.rng.getstate(), [points[i] for i in rings],
                                         [radii[i] for i in rings], width))
            self.rng.uniform(0, math.tau)
            self.rng.random()
            self.rng.random()

        def leaf(self, anchor, axis, crown_normal, length, **kwargs):
            # Match Beech.leaf's RNG sequence while using the actual saved oak surfaces.
            for _ in range(5):
                self.rng.random()
            self.rng.randrange(len(self.rows))
            self.count += 1

    rows = json.loads((SOURCE / "Maps/LeafRows.json").read_text())
    layout = BranchLayout(rows)
    layout.crown()
    # Include the terminal bole spray with the last scaffold instead of allocating
    # a full branch budget to the comparatively small leader tip.
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
            export(Path(work), Path(output), SOURCE, NAME, "white oak", "CrownAlbedoBase.png")
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    args = parser.parse_args()
    work, output = ROOT / ".utmp/hero-white-oak-lod0", ROOT / "ArtSource" / NAME
    for directory in (work, output):
        directory.mkdir(parents=True, exist_ok=True)
    command = [str(args.blender), "-b", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    if not args.reuse_extract:
        subprocess.run(command + [str(SOURCE / "HeroWhiteOak.blend")] + script +
                       ["--", "extract", str(work), str(output)], check=True)
    bake(work, source=SOURCE, fans_per_branch=8, coverage_weighted_surface=True)
    subprocess.run(command + ["--factory-startup"] + script +
                   ["--", "export", str(work), str(output)], check=True)


if __name__ == "__main__":
    main()
