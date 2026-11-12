"""Bake six sculpted cloud volumes into linear directional-light/opacity tiles.

Run with NumPy, SciPy and Pillow. RGB stores the response to left, overhead and
right illumination, and alpha stores view opacity. Texture row zero is the top
of the atlas. The sky shader indexes tiles from the bottom.
"""
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import zoom


ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Assets/Textures/Sky/MeadowCloudAtlas.png"
TILE = 512
SHAPE = (128, 320, 384)  # view depth, height, width

# Each lobe is center X/Y/Z followed by its three radii.
FORMS = (
    ((-.28, -.14, 0, .65, .34, .63), (.11, -.12, .05, .76, .35, .63),
     (-.40, .16, -.16, .44, .48, .51), (.08, .26, .02, .43, .53, .55),
     (.46, .10, .16, .40, .35, .44), (-.73, -.07, .12, .28, .24, .37)),
    ((-.32, -.15, -.06, .69, .28, .53), (.34, -.14, .06, .67, .27, .55),
     (-.57, .07, .15, .37, .31, .43), (-.12, .14, -.17, .46, .45, .51),
     (.27, .02, .12, .39, .31, .47), (.66, .04, -.05, .32, .26, .39)),
    ((-.15, -.18, .08, .81, .28, .52), (-.48, .05, -.12, .42, .36, .44),
     (-.05, .10, .16, .48, .39, .49), (.37, -.08, -.08, .54, .29, .47),
     (.64, -.09, .06, .35, .24, .32)),
    ((-.32, -.12, .05, .64, .22, .41), (.33, -.08, -.12, .64, .23, .48),
     (-.42, .05, -.06, .39, .30, .41), (.02, .09, .13, .43, .33, .37),
     (.52, .07, .09, .39, .29, .34)),
    ((-.22, -.12, 0, .55, .27, .49), (.15, .13, .08, .50, .49, .46),
     (.43, -.06, -.14, .41, .28, .38), (-.44, .07, .16, .28, .30, .32)),
    ((-.46, -.10, .05, .48, .13, .24), (-.21, .01, -.06, .42, .21, .27),
     (.12, -.03, .08, .34, .14, .22), (.51, -.10, -.10, .26, .085, .16)),
)


def bake(lobes, seed):
    z, y, x = np.ogrid[-1:1:complex(SHAPE[0]), 1:-1:complex(SHAPE[1]), -1.25:1.25:complex(SHAPE[2])]
    density = np.full(SHAPE, -2, dtype=np.float32)
    for cx, cy, cz, rx, ry, rz in lobes:
        lobe = 1 - ((x-cx)/rx)**2 - ((y-cy)/ry)**2 - ((z-cz)/rz)**2
        # Smooth union gives connected lobes with rounded shoulders.
        blend = np.maximum(.32 - np.abs(density - lobe), 0)
        density[:] = np.maximum(density, lobe) + blend**2 / 1.28

    rng = np.random.default_rng(seed)
    # Coherent erosion carries across view depth so smaller lobes remain
    # visible in silhouette after projection. Random noise per pixel would
    # disappear in the bake or shimmer when the clouds drift.
    for scale, strength in ((11, .32), (24, .16), (49, .075)):
        grid = rng.random((scale, scale), dtype=np.float32) - .5
        density += zoom(grid, (SHAPE[1] / scale, SHAPE[2] / scale), order=3)[None, :, :] * strength
    for scale, strength in ((7, .67), (14, .35), (28, .16), (53, .060)):
        grid = rng.random((scale, scale, scale), dtype=np.float32) - .5
        noise = zoom(grid, tuple(n / scale for n in SHAPE), order=1, prefilter=False)
        density += noise * strength

    density = np.clip((density - .015) / .60, 0, 1) ** 1.28
    step = density * (10 / SHAPE[0])
    view = np.cumsum(step, axis=0) - step
    weight = np.exp(-view) * -np.expm1(-step)
    opacity = weight.sum(axis=0)

    # Integrate the light reaching each visible volume sample. Top and the two
    # shoulder directions are retained for interpolation by the moving sun.
    top = np.cumsum(density, axis=1) * (7 / SHAPE[1])
    channels = []
    for direction in (1, 0, -1):
        if direction:
            side = np.cumsum(density[:, :, ::direction], axis=2)[:, :, ::direction] * (8 / SHAPE[2])
            light = np.exp(-.60 * top - .62 * side)
        else:
            light = np.exp(-top)
        channels.append((weight * light).sum(axis=0) / np.maximum(opacity, .0001))

    tile = Image.fromarray(np.uint8(np.rint(np.clip(np.stack((*channels, opacity), axis=-1), 0, 1) * 255)))
    # Resize each channel independently to preserve unassociated light response.
    return Image.merge("RGBA", tuple(b.resize((TILE, TILE), Image.Resampling.BICUBIC) for b in tile.split()))


def main():
    atlas = Image.new("RGBA", (3 * TILE, 2 * TILE))
    for i, lobes in enumerate(FORMS):
        print(f"Baking cloud {i + 1}/{len(FORMS)}", flush=True)
        atlas.paste(bake(lobes, 7364 + i * 311), (i % 3 * TILE, (1 - i // 3) * TILE))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(OUTPUT)


if __name__ == "__main__":
    main()
