"""Bake the Sugar Maple master into fitted foliage sprays with packed surface data."""

import argparse
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeechLOD0 import ROOT, bake, export
from BuildHeroRedMapleLOD0 import extract


NAME = "HeroSugarMapleLOD0"
SOURCE = ROOT / "ArtSource/HeroSugarMaple"


def main():
    if "--" in sys.argv:
        phase, work, output = sys.argv[sys.argv.index("--") + 1:]
        if phase == "extract":
            from BuildHeroSugarMaple import SugarMaple

            extract(Path(work), SugarMaple, SOURCE)
        else:
            export(Path(work), Path(output), SOURCE, NAME, "Sugar Maple", "CrownAlbedoBase.png")
        return

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    args = parser.parse_args()
    work = ROOT / ".utmp/hero-sugar-maple-lod0"
    output = ROOT / "ArtSource" / NAME
    work.mkdir(parents=True, exist_ok=True)
    output.mkdir(parents=True, exist_ok=True)

    command = [str(args.blender), "-b", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    if not args.reuse_extract:
        subprocess.run(command + [str(SOURCE / "HeroSugarMaple.blend")] + script +
                       ["--", "extract", str(work), str(output)], check=True)
    bake(work, source=SOURCE, fans_per_branch=10, coverage_weighted_surface=True,
         contact_radius=.12, tile_size=144, crossed_sprays=True)
    from PIL import Image

    # Tame the brightest card greens while keeping the packed surface data linear.
    lookup = [min(255, round(value * gain)) for gain in (.96, .94, 1.03, 1) for value in range(256)]
    albedo = work / "CrownAlbedoBase.png"
    with Image.open(albedo) as image:
        graded = image.point(lookup)
    graded.save(albedo)
    subprocess.run(command + ["--factory-startup"] + script +
                   ["--", "export", str(work), str(output)], check=True)


if __name__ == "__main__":
    main()
