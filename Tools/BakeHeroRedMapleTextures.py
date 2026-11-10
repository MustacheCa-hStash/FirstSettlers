"""Prepare red maple bark relief, leaf atlas, and fitted leaf UV rows."""

from pathlib import Path
import sys

from BakeHeroBeechTextures import bark, foliage


if __name__ == "__main__":
    source = Path(sys.argv[1])
    maps = source / "Maps"
    maps.mkdir(parents=True, exist_ok=True)
    bark(source, maps, size=1536, normal_strength=5.4)
    foliage(source, maps, color_scale=(.98, 1.06, 1.08), fit_rounded_lobes=True)
