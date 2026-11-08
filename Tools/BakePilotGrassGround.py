"""Bake a periodic oblique grass surface from the pilot's actual tuft mesh."""
from pathlib import Path
import re

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SIZE = 1024
TILE_METERS = 2.75
mesh = (ROOT / "Assets/Models/Grass+Flowers/PilotMeadowGrass_Near.asset").read_text()
stream = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", mesh)[1])
vertex_count = int(re.search(r"m_VertexCount: (\d+)", mesh)[1])
indices = np.frombuffer(bytes.fromhex(re.search(r"m_IndexBuffer: ([0-9a-f]+)", mesh)[1]),
                        dtype="<u2").reshape(-1, 3)
vertices = np.frombuffer(stream, dtype="<f4").reshape(vertex_count, 8)
positions, uv = vertices[:, :3], vertices[:, 6:8]
atlas = np.asarray(Image.open(ROOT / "Assets/Textures/Grass/T_PilotMeadowBlades.png").convert("RGBA")) / 255
depth = np.full((SIZE, SIZE), -1.0, dtype=np.float32)
blade_height = np.zeros_like(depth)
tone = np.zeros_like(depth)
rng = np.random.default_rng(58199)
VIEW_SHEAR = .84  # An orthographic view at about 50° above the ground.


def blur(periodic, radius):
    pad = int(np.ceil(radius * 4))
    src = Image.fromarray(np.uint8(np.clip(np.pad(periodic, pad, mode="wrap"), 0, 1) * 255))
    return np.asarray(src.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)[pad:-pad, pad:-pad] / 255


def bake_tuft(center, angle, scale, tint):
    c, s = np.cos(angle), np.sin(angle)
    point = positions * scale
    x = (point[:, 0]*c + point[:, 2]*s + center[0]) * SIZE / TILE_METERS
    forward = -point[:, 0]*s + point[:, 2]*c + center[1]
    z = (forward + point[:, 1]*VIEW_SHEAR) * SIZE / TILE_METERS
    for tri in indices:
        ax, bx, cx = x[tri]
        ay, by, cy = z[tri]
        area = (bx-ax)*(cy-ay)-(cx-ax)*(by-ay)
        if abs(area) < 0.01:
            continue
        iy, ix = np.mgrid[int(np.floor(min(ay, by, cy))):int(np.ceil(max(ay, by, cy))),
                          int(np.floor(min(ax, bx, cx))):int(np.ceil(max(ax, bx, cx)))]
        u = ((ix+.5-ax)*(cy-ay)-(iy+.5-ay)*(cx-ax))/area
        v = ((iy+.5-ay)*(bx-ax)-(ix+.5-ax)*(by-ay))/area
        inside = (u>=0) & (v>=0) & (u+v<=1)
        if not inside.any():
            continue
        weights = np.stack((1-u[inside]-v[inside], u[inside], v[inside]), axis=1)
        st = weights @ uv[tri]
        texels = atlas[np.clip(((1-st[:, 1])*atlas.shape[0]).astype(int), 0, atlas.shape[0]-1),
                       np.clip((st[:, 0]*atlas.shape[1]).astype(int), 0, atlas.shape[1]-1)]
        height = np.clip(weights @ point[tri, 1], 0, .84)
        # Compare rays in the unwrapped tile so overhanging tips keep the same
        # ordering at both sides of the periodic bake.
        distance = height * (1 + VIEW_SHEAR*VIEW_SHEAR)
        ys, xs = iy[inside] % SIZE, ix[inside] % SIZE
        draw = (texels[:, 3] >= .42) & (distance > depth[ys, xs])
        depth[ys[draw], xs[draw]] = distance[draw]
        blade_height[ys[draw], xs[draw]] = height[draw]
        luma = texels[:, :3] @ np.array((.21, .72, .07))
        tone[ys[draw], xs[draw]] = (.43 + .26*luma + .27*height + tint)[draw]


grid = 13
for z in range(grid):
    for x in range(grid):
        center = (np.array((x, z)) + rng.uniform(.05, .95, 2)) * TILE_METERS / grid
        phase = center * 2 * np.pi / TILE_METERS
        clump = .58*np.sin(phase[0]*3+np.cos(phase[1]*2))+.42*np.cos(phase[1]*4-phase[0])
        bake_tuft(center, rng.uniform(0, np.pi*2), rng.uniform(.91, 1.25)+clump*.11, rng.uniform(-.06,.06)+clump*.08)

# Clump-base occlusion supplies the structure that survives distant mip levels.
# Short blade ridges remain in the packed normal and shallow parallax channels.
coverage = (depth >= 0).astype(np.float32)
base_shadow = blur(coverage, 15)
crown = blur(blade_height, 6)
# Fine occluded blades still read through the upper canopy's gaps.
subcanopy = .30 + blur(tone, 2.5)*.34 + crown*.12 + rng.normal(0,.017,depth.shape)
ground = np.where(coverage > 0, tone-base_shadow*.07, subcanopy)
ground += (blur(crown,27)-.30)*.37
ground = ground - np.mean(ground) + .50
relief = blur(blade_height, 2.0)*.54 + crown*.39
normal_x = (np.roll(relief, 1, axis=1) - np.roll(relief, -1, axis=1))*2.5
normal_z = (np.roll(relief, 1, axis=0) - np.roll(relief, -1, axis=0))*2.5
packed = np.stack((ground, .5+.5*normal_x, .5+.5*normal_z, relief), axis=2)
output = ROOT / "Assets/Textures/Ground/Grass/T_PilotMeadowGroundOblique.png"
Image.fromarray(np.uint8(np.clip(packed, 0, 1)*255+.5)).save(output)
print(f"{output.name}: {SIZE}², {TILE_METERS} m tile, {coverage.mean():.1%} blade silhouettes")
