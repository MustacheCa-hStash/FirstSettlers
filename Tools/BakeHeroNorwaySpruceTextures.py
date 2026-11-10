"""Bake bark relief and a compact albedo ramp for individually modeled spruce needles."""

from pathlib import Path
import sys

import numpy as np
from PIL import Image

from BakeHeroBeechTextures import bark


def needle_atlas(path):
    # The atlas runs along each solid needle. Rows vary age and green tone.
    length = np.linspace(0, 1, 192)[None, :, None]
    age = np.linspace(0, 1, 64)[:, None, None]
    mature = np.array([49, 84, 43])
    fresh = np.array([87, 123, 61])
    color = mature + age * (fresh - mature)
    color = color * (.86 + .15 * np.sin(length * np.pi * .81)) + np.maximum(0, length - .78) * 12
    grain = np.random.default_rng(8494).normal(0, .72, color.shape[:2])
    Image.fromarray(np.uint8(np.clip(color + grain[:, :, None], 0, 255))).save(path)


if __name__ == "__main__":
    source = Path(sys.argv[1])
    maps = source / "Maps"
    maps.mkdir(parents=True, exist_ok=True)
    bark(source, maps, size=1536, normal_strength=5.2)
    needle_atlas(maps / "NeedleAlbedo.png")
