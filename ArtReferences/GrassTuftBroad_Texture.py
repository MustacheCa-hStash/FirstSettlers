"""Generate a blade silhouette mask. RGB is uniformly white, even at edges.

Blade color and the root-to-tip gradient belong entirely to the Unity shader.
"""

from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw


root = Path(__file__).resolve().parents[1]
out = root / "Assets" / "Textures" / "Grass" / "T_GrassTuftBroad_Cutout.png"
size = 1024
scale = 2
image = Image.new("L", (size * scale, size * scale), 0)
draw = ImageDraw.Draw(image)
rng = random.Random(47296)


def point(x, y):
    return (round(x * scale), round(y * scale))


def leaf(base_x, tip_y, width, lean):
    base_y = 1005
    left, right = [], []
    for step in range(13):
        t = step / 12
        center = base_x + lean * t ** 2
        y = base_y + (tip_y - base_y) * t
        half_width = width * 0.5 * (1 - t ** 1.1)
        left.append(point(center - half_width, y))
        right.append(point(center + half_width, y))
    draw.polygon(left + right[::-1], fill=255)


# Overlapping broad roots cover the soil; varied pointed tips retain a readable
# blade silhouette. Small root notches avoid an obvious rectangular card edge.
for i in range(16):
    x = 36 + i * 63 + rng.uniform(-10, 10)
    leaf(x, rng.uniform(55, 430), rng.uniform(82, 112), rng.uniform(-38, 38))
for i in range(9):
    x = 35 + i * 118 + rng.uniform(-15, 15)
    leaf(x, rng.uniform(440, 650), rng.uniform(82, 112), rng.uniform(-30, 30))
for x, top, width in [
    (145, 945, 12), (390, 940, 14), (635, 952, 11), (880, 944, 13),
]:
    draw.polygon((point(x, top), point(x - width / 2, 1010),
                  point(x + width / 2, 1010)), fill=0)

image = image.resize((512, 512), Image.Resampling.LANCZOS)
art_bounds = image.getbbox()
art = image.crop(art_bounds).resize((484, 488), Image.Resampling.LANCZOS)
packed = Image.new("L", (512, 512), 0)
packed.paste(art, (14, 12))
alpha = np.array(packed)
alpha[alpha < 8] = 0
pixels = np.full((512, 512, 4), 255, dtype=np.uint8)
pixels[:, :, 3] = alpha
image = Image.fromarray(pixels)
assert np.all(pixels[:, :, :3] == 255), "Blade interiors must contain no texture detail"
image.save(out, optimize=True)
print("Saved", out, "coverage", round(float((pixels[:, :, 3] > 127).mean()), 4),
      "bounds", image.getchannel("A").getbbox())
