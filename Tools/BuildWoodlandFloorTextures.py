"""Prepare woodland albedo, relief maps and alpha cutouts for the Unity pilot."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


root = Path(__file__).resolve().parents[1]
source = root / "ArtSource" / "WoodlandFloor"
output = root / "Assets" / "Textures" / "Ground" / "WoodlandPilot"
output.mkdir(parents=True, exist_ok=True)


def save(pixels, name):
    Image.fromarray(np.uint8(np.clip(pixels, 0, 255) + .5)).save(output / f"{name}.png")


def seamless_edges(pixels):
    # Feather paired texels at the wrap seam without changing the center of the tile.
    for axis in (0, 1):
        rows = pixels.swapaxes(0, axis)
        for i in range(24):
            amount = .5 * (.5 + .5 * np.cos(i * np.pi / 24))
            start, end = rows[i].copy(), rows[-1-i].copy()
            rows[i] = start + amount * (end - start)
            rows[-1-i] = end + amount * (start - end)
    return pixels


for name in ("DryDuff", "DampDuff"):
    with Image.open(source / f"{name}.png") as art:
        rgb = art.convert("RGB").resize((1024, 1024), Image.Resampling.LANCZOS)
    pixels = seamless_edges(np.array(rgb, dtype=np.float32))
    save(pixels, name + "_Albedo")
    gray = Image.fromarray(np.uint8(pixels)).convert("L").filter(ImageFilter.GaussianBlur(1.25))
    heights = np.asarray(gray, dtype=np.float32)
    low, high = np.percentile(heights, (7, 96))
    heights = np.clip((heights - low) / max(high - low, 1), 0, 1)
    dx = (np.roll(heights, -1, 1) - np.roll(heights, 1, 1)) * 2.5
    dy = (np.roll(heights, -1, 0) - np.roll(heights, 1, 0)) * 2.5
    normal = np.stack((-dx, dy, np.ones_like(dx)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    save((normal * .5 + .5) * 255, name + "_Normal")
    save(heights * 255, name + "_Height")
    if name == "DryDuff":
        save((.55 + .45 * heights) * 255, name + "_AO")

for name in ("Groundcover", "Hazel"):
    with Image.open(source / f"{name}.png") as original:
        art = original.convert("RGBA")
    alpha = art.getchannel("A").point(lambda value: max(0, round((value - 55) * 255 / 200)))
    art.putalpha(alpha)
    bounds = alpha.point(lambda value: 255 if value >= 86 else 0).getbbox()
    if not bounds:
        raise ValueError(f"{name} contains no opaque specimen")
    size = (768, 512) if name == "Groundcover" else (768, 768)
    result = Image.new("RGBA", size)
    result.paste(art.crop(bounds).resize((size[0]-24, size[1]-24), Image.Resampling.LANCZOS), (12, 12))
    result.save(output / f"{name}.png")
