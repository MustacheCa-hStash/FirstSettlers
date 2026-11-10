"""Bake sugar maple bark relief and fitted summer leaf atlas."""

from pathlib import Path
import sys

from BakeHeroBeechTextures import bark, foliage


if __name__ == "__main__":
    source = Path(sys.argv[1])
    maps = source / "Maps"
    maps.mkdir(parents=True, exist_ok=True)
    bark(source, maps, size=1536, normal_strength=6.3)
    foliage(source, maps, color_scale=(.96, 1.05, 1.02), fit_rounded_lobes=True)
