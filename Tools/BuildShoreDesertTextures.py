"""Build periodic terrain albedo and companion tangent-space normals from the art masters."""
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource" / "ShoreDesert"
OUTPUT = ROOT / "Assets" / "Textures" / "Ground" / "ShoreDesert"
SIZE = 1024


def periodic(source):
    # Periodic-plus-smooth decomposition removes mismatched boundary trends
    # without mirroring features or blurring detail at the repeat seam.
    jumps = np.zeros_like(source)
    jumps[0, :] = source[-1, :] - source[0, :]
    jumps[-1, :] = -jumps[0, :]
    jumps[:, 0] += source[:, -1] - source[:, 0]
    jumps[:, -1] -= source[:, -1] - source[:, 0]
    wave = 2 * np.cos(2 * np.pi * np.arange(SIZE) / SIZE)
    laplacian = wave[:, None] + wave[None, :] - 4
    laplacian[0, 0] = 1
    smooth = np.fft.fft2(jumps, axes=(0, 1)) / laplacian[:, :, None]
    smooth[0, 0] = 0
    return source - np.fft.ifft2(smooth, axes=(0, 1)).real


def save(data, name):
    encoded = (np.clip(data, 0, 1) * 255 + .5).astype(np.uint8)
    Image.fromarray(encoded, "RGB").save(OUTPUT / name)


def bake(surface, fine_relief, broad_relief):
    with Image.open(SOURCE / f"{surface}SandSource.png") as image:
        image = image.convert("RGB").resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        albedo = periodic(np.asarray(image, dtype=float) / 255)
    save(albedo, f"T_{surface}Sand_Albedo.png")

    luminance = albedo @ np.array([.2126, .7152, .0722])
    grains = gaussian_filter(luminance, .75, mode="wrap")
    relief = grains - gaussian_filter(grains, 7, mode="wrap")
    broad = gaussian_filter(luminance, 8, mode="wrap")
    broad -= gaussian_filter(luminance, 38, mode="wrap")
    height = relief * fine_relief + broad * broad_relief
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / 2
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) / 2
    normals = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    save(normals * .5 + .5, f"T_{surface}Sand_Normal.png")


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    bake("Shore", fine_relief=5, broad_relief=4)
    bake("Desert", fine_relief=2.2, broad_relief=9)
