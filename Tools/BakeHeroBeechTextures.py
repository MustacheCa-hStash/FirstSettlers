"""Prepare beech material maps and fitted leaf UV rows from the source photographs."""

import json
from pathlib import Path
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, gaussian_filter


def write_normal(path, height, strength, wrap=False):
    if wrap:
        dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * .5
        dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * .5
    else:
        dy, dx = np.gradient(height)
    normal = np.stack((-dx * strength, dy * strength, np.ones_like(height)), axis=2)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    Image.fromarray(np.uint8(np.clip(normal * 127.5 + 127.5, 0, 255))).save(path)


def bark(root, output, size=1024, normal_strength=3.2):
    source = Image.open(root / "BarkSource.png").convert("RGB").resize((size, size), Image.Resampling.LANCZOS)
    color = np.asarray(source, dtype=np.float32).copy()
    # Opposite edges converge over a short overlap without mirroring the entire bark.
    seam = 64
    for axis in (0, 1):
        row = np.moveaxis(color, axis, 0)
        blend = np.cos(np.linspace(0, np.pi / 2, seam))[:, None, None] ** 2
        correction = (row[-seam:][::-1] - row[:seam]) * blend * .5
        row[:seam] += correction
        row[-seam:] -= correction[::-1]
    color = color * np.array((.91, .92, .90))
    Image.fromarray(np.uint8(np.clip(color, 0, 255))).save(output / "BarkAlbedo.png")
    gray = (color @ np.array((.22, .71, .07))) / 255
    height = gaussian_filter(gray, 1, mode="wrap") - .72 * gaussian_filter(gray, 16, mode="wrap")
    write_normal(output / "BarkNormal.png", height, normal_strength, wrap=True)
    roughness = np.clip(.83 + (gaussian_filter(gray, 3, mode="wrap") - .5) * .32, .62, .92)
    Image.fromarray(np.uint8(roughness * 255)).save(output / "BarkRoughness.png")


def foliage(root, output, color_scale=(.87, 1.01, .93), fit_rounded_lobes=False):
    source = Image.open(root / "LeafSource.png").convert("RGBA")
    size, tile = 2048, 1024
    atlas = np.zeros((size, size, 4), dtype=np.uint8)
    outlines = []
    levels = (.001, .08, .19, .34, .49, .64, .79, .91, .999) if fit_rounded_lobes else (
        .001, .07, .23, .43, .64, .81, .94, .999)
    for i in range(4):
        col, row = i % 2, i // 2
        specimen = source.crop((col * source.width // 2, row * source.height // 2,
                                (col + 1) * source.width // 2, (row + 1) * source.height // 2))
        box = specimen.getchannel("A").point(lambda a: 255 if a >= 85 else 0).getbbox()
        specimen = specimen.crop(box)
        specimen.thumbnail((tile - 72, tile - 72), Image.Resampling.LANCZOS)
        specimen = specimen.resize((round(specimen.width * (tile-72) / specimen.height), tile-72),
                                   Image.Resampling.LANCZOS)
        x, y = col * tile + (tile - specimen.width) // 2, row * tile + 36
        image = np.asarray(specimen).copy()
        opaque = image[:, :, 3] >= 83
        fitted = []
        for index, level in enumerate(levels):
            center_y = round((1 - level) * (specimen.height - 1))
            # Include the nearby silhouette so the texture can supply fine teeth.
            radius = 4 if index in (0, len(levels) - 1) else 26
            lo_row, hi_row = center_y-radius, center_y+radius+1
            if fit_rounded_lobes:
                # Enclose the lobes between mesh rings so the cutout can retain their shape.
                lo_row = round((1 - levels[min(index + 1, len(levels) - 1)]) * (specimen.height - 1))
                hi_row = round((1 - levels[max(index - 1, 0)]) * (specimen.height - 1)) + 1
            _, columns = np.where(opaque[max(0, lo_row):hi_row])
            mid = specimen.width * .5
            lo = max(-4, min(columns) - 9) if columns.size else mid - 2
            hi = min(specimen.width+4, max(columns) + 9) if columns.size else mid + 2
            fitted.append((level, 1-(y+center_y+.5)/size,
                           [(p-mid)/specimen.width for p in (lo, mid, hi)],
                           [(x+p+.5)/size for p in (lo, mid, hi)]))
        outlines.append(fitted)
        atlas[y:y+specimen.height, x:x+specimen.width] = image
    present = atlas[:, :, 3] > 100
    nearest = distance_transform_edt(~present, return_distances=False, return_indices=True)
    atlas[:, :, :3] = atlas[tuple(nearest)][:, :, :3]
    # Cooler natural greens preserve the yellow-green veins and age variation.
    atlas[:, :, :3] = np.uint8(np.clip(atlas[:, :, :3] * color_scale, 0, 255))
    Image.fromarray(atlas).save(output / "LeafAlbedo.png")
    gray = (atlas[:, :, :3] @ np.array((.22, .71, .07))) / 255
    relief = gaussian_filter(gray, 1) - gaussian_filter(gray, 8) * .72
    write_normal(output / "LeafNormal.png", relief, 1.55)
    (output / "LeafRows.json").write_text(json.dumps(outlines, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    source, maps = map(Path, sys.argv[1:3])
    maps.mkdir(parents=True, exist_ok=True)
    bark(source, maps)
    foliage(source, maps)
