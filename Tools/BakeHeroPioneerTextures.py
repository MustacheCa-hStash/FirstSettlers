"""Prepare hero birch, aspen, and white pine material maps."""

from pathlib import Path

import numpy as np
from PIL import Image

from BakeHeroBeechTextures import bark, foliage


def white_pine_needles(path):
    # Lengthwise hue shift and age variation for individually modeled needles.
    length = np.linspace(0, 1, 256)[None, :, None]
    age = np.linspace(0, 1, 96)[:, None, None]
    mature = np.array([50, 78, 53])
    fresh = np.array([80, 116, 73])
    color = mature + age * (fresh - mature)
    color = color * (.88 + .16 * np.sin(length * np.pi * .82))
    color += np.maximum(0, length - .75) * np.array([23, 11, 7])
    grain = np.random.default_rng(692).normal(0, .64, color.shape[:2])
    Image.fromarray(np.uint8(np.clip(color + grain[:, :, None], 0, 255))).save(path)


if __name__ == "__main__":
    art = Path(__file__).resolve().parents[1] / "ArtSource"
    for name, relief, tint in (
        ("HeroPaperBirch", 3.9, (.95, 1.04, 1.04)),
        ("HeroQuakingAspen", 2.6, (.95, 1.05, 1.10)),
        ("HeroEasternWhitePine", 6.0, None),
    ):
        source = art / name
        maps = source / "Maps"
        maps.mkdir(parents=True, exist_ok=True)
        bark(source, maps, size=1536, normal_strength=relief)
        if tint is not None:
            foliage(source, maps, color_scale=tint, fit_rounded_lobes=True)
        else:
            white_pine_needles(maps / "NeedleAlbedo.png")
