"""Prepare the white oak source material maps from its bark and leaf scans."""

from pathlib import Path
import sys

import numpy as np
from PIL import Image

from BakeHeroBeechTextures import bark, foliage


if __name__ == "__main__":
    source = Path(sys.argv[1])
    maps = source / "Maps"
    maps.mkdir(parents=True, exist_ok=True)
    bark(source, maps, size=1536, normal_strength=6.2)
    foliage(source, maps, color_scale=(1.12, 1.19, 1.25), fit_rounded_lobes=True)
    roughness = np.asarray(Image.open(maps / "BarkRoughness.png"))
    metal_smooth = np.zeros((*roughness.shape, 4), dtype=np.uint8)
    metal_smooth[:, :, 3] = 255 - roughness
    Image.fromarray(metal_smooth).save(maps / "BarkMetalSmooth.png")
