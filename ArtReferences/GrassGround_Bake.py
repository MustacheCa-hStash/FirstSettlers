"""Bake periodic, stylized ground blades from the broad-tuft silhouette recipe.

No photograph or painted veins: flat tapered leaves, smooth root-to-tip tones,
and shallow physical relief. Runtime uses a single linear RGBA texture:
R = tone / 2, GB = world-XZ normal components remapped to 0..1, A = height.
The RGB albedo, normal and height exports are authoring/reference maps only.
"""
from pathlib import Path
import json
import math
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Textures/Ground/Grass"
SIZE = 1024
TILE_METERS = 1 / 0.45
RELIEF_METERS = 0.012
rng = np.random.default_rng(47296)
height = np.full((SIZE, SIZE), 0.06, dtype=np.float32)
tone = np.full_like(height, 0.79)

# Periodic splatting wraps every leaf, including those crossing texture edges.
# The taper matches GrassTuftBroad_Texture.py; here blades lie over the soil.
for _ in range(1900):
    root = rng.uniform(0, SIZE, 2)
    length = rng.uniform(48, 125)
    width = rng.uniform(8, 16)
    angle = rng.uniform(0, math.tau)
    lean = rng.uniform(-0.15, 0.15) * length
    radius = int(math.ceil(length + abs(lean) + width))
    xi = np.arange(int(root[0]) - radius, int(root[0]) + radius + 1)
    yi = np.arange(int(root[1]) - radius, int(root[1]) + radius + 1)
    dx, dy = np.meshgrid(xi - root[0], yi - root[1])
    along = dx * math.cos(angle) + dy * math.sin(angle)
    across = -dx * math.sin(angle) + dy * math.cos(angle)
    t = np.clip(along / length, 0, 1)
    half_width = width * 0.5 * (1 - t ** 1.1)
    distance_edge = half_width - np.abs(across - lean * t ** 2)
    alpha = np.clip(distance_edge + 0.5, 0, 1)
    alpha *= np.clip(along + 0.5, 0, 1) * np.clip(length - along + 0.5, 0, 1)
    # Broad, softly rounded blade cross-section; no razor-like normal ridges.
    cross_section = np.clip(distance_edge / np.maximum(half_width, 0.5), 0, 1)
    leaf_height = rng.uniform(0.23, 0.63) + 0.19 * np.sin(t * math.pi) * cross_section
    ids = np.ix_(yi % SIZE, xi % SIZE)
    old_height = height[ids]
    top = leaf_height > old_height
    blend = alpha * top
    height[ids] = old_height * (1 - blend) + leaf_height * blend
    # Exactly the same low-frequency root-to-tip idea as the live grass shader.
    leaf_tone = rng.uniform(0.88, 1.04) * (0.88 + 0.24 * t ** 0.85)
    tone[ids] = tone[ids] * (1 - blend) + leaf_tone * blend

def periodic_soften(values):
    return (values * 4 + np.roll(values, 1, 0) + np.roll(values, -1, 0) +
            np.roll(values, 1, 1) + np.roll(values, -1, 1)) / 8

height = periodic_soften(periodic_soften(height))
tone = periodic_soften(tone)
tone /= float(np.mean(tone))  # Neutral average: distance fades do not darken grass.
pixel_meters = TILE_METERS / SIZE
slope_x = -(np.roll(height, -1, 1) - np.roll(height, 1, 1)) * RELIEF_METERS / (2 * pixel_meters)
# PNG rows run down; Unity UV-V/world-Z run up.
slope_z = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * RELIEF_METERS / (2 * pixel_meters)
normal_length = np.sqrt(slope_x ** 2 + slope_z ** 2 + 1)
nx, nz, ny = slope_x / normal_length, slope_z / normal_length, 1 / normal_length

def save(name, values):
    Image.fromarray(np.uint8(np.clip(values, 0, 1) * 255 + 0.5)).save(OUT / name)

packed = np.stack((tone * 0.5, nx * 0.5 + 0.5, nz * 0.5 + 0.5, height), axis=-1)
save("T_GrassGround_BladeSurface.png", packed)
palette = np.array((0.29, 0.62, 0.24))
albedo_linear = tone[..., None] * palette
albedo_srgb = np.where(albedo_linear < 0.0031308, albedo_linear * 12.92,
                       1.055 * albedo_linear ** (1 / 2.4) - 0.055)
save("T_GrassGround_BladeAlbedo.png", albedo_srgb)
save("T_GrassGround_BladeNormal.png", np.stack((nx, nz, ny), axis=-1) * 0.5 + 0.5)
save("T_GrassGround_BladeHeight.png", height)

# Compare wrap transitions with ordinary neighbouring pixels, rather than
# forcing duplicate edge pixels (which would flatten the derivative at a seam).
stats = {}
for name, values in (("tone", tone), ("height", height), ("normal", np.stack((nx, nz), -1))):
    for axis in (0, 1):
        wrap = np.mean(np.abs(np.take(values, 0, axis) - np.take(values, -1, axis)))
        interior = np.mean(np.abs(np.diff(values, axis=axis)))
        ratio = float(wrap / max(float(interior), 1e-8))
        stats[f"{name}_axis{axis}_wrap_to_interior"] = ratio
        assert ratio < 1.7, f"Unexpected seam in {name} axis {axis}: {ratio}"
stats["mean_tone"] = float(np.mean(tone))
stats["tile_meters"] = TILE_METERS
stats["relief_meters"] = RELIEF_METERS
(ROOT / "ArtReferences/GrassGround_BakeReport.json").write_text(json.dumps(stats, indent=2) + "\n")
print(json.dumps(stats, indent=2))
