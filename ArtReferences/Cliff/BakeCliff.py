"""Finish the requested half-tile seam repair and bake aligned relief maps.

Inputs are built-in imagegen outputs, not procedural replacement artwork.
Relief is estimated from local luminance/crevices; it is not scanned geometry.
Run with Pillow and numpy. No network or image-generation credentials needed.
"""
from pathlib import Path
import json
import re
import uuid
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = ROOT / 'Assets/Textures/Rock'
SIZE = 1024

def load(name):
    return np.asarray(Image.open(HERE / name).convert('RGB').resize(
        (SIZE, SIZE), Image.Resampling.LANCZOS), dtype=np.float64) / 255

def smoothstep(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)

def blur_wrap(a, sigma):
    freq = np.fft.fftfreq(SIZE)
    yy, xx = np.meshgrid(freq, freq, indexing='ij')
    kernel = np.exp(-2*np.pi**2*sigma**2*(xx*xx+yy*yy))
    return np.fft.ifft2(np.fft.fft2(a)*kernel).real

def close_edges(a, width=12):
    """Localized eased endpoint correction, preserving the interior artwork."""
    a = a.copy()
    for axis in (1, 0):
        v = np.swapaxes(a, 0, axis)
        delta = (v[-1]-v[0])*0.5
        for i in range(width):
            weight = 1-smoothstep(0, width-1, i)
            v[i] += delta*weight
            v[-1-i] -= delta*weight
    return a

def save(a, name):
    pixels = np.round(np.clip(a, 0, 1)*255).astype(np.uint8)
    Image.fromarray(pixels).save(OUT / name)
    return pixels

OUT.mkdir(parents=True, exist_ok=True)
original = load('Cliff_Source.png')
offset = np.roll(original, (SIZE//2, SIZE//2), axis=(0, 1))
edited = load('Cliff_SeamCleaned.png')
coord = (np.arange(SIZE)+.5)/SIZE
y, x = np.meshgrid(coord, coord, indexing='ij')
cross = 1-smoothstep(.045, .14, np.minimum(abs(x-.5), abs(y-.5)))
border = smoothstep(.025, .09, np.minimum.reduce([x, y, 1-x, 1-y]))
mask = (cross*border)[..., None]
albedo = offset*(1-mask)+edited*mask
# Preserve the original continuous outer perimeter of the offset image; repair
# only the central cross. Endpoint correction removes residual resampling error.
albedo = close_edges(albedo)
albedo_pixels = save(albedo, 'T_CliffFractured_Albedo.png')
luma = albedo @ np.array([.2126, .7152, .0722])
local = blur_wrap(luma, 20)
crevice = blur_wrap(np.clip((local-luma-.035)/.20, 0, 1), 2.2)
planes = blur_wrap(luma, 5)-blur_wrap(luma, 55)
grain = blur_wrap(luma, .9)-blur_wrap(luma, 4)
height = close_edges(np.clip(.62-.43*crevice+.55*planes+.10*grain, 0, 1))
height_pixels = save(height, 'T_CliffFractured_Height.png')
# Central differences wrap through the tile. Image rows run downward; Unity's
# tangent V runs upward, so the green component has the opposite row derivative.
pixel_meters = (1/.12)/SIZE
relief_meters = .18
dx = (np.roll(height, -1, 1)-np.roll(height, 1, 1))*.5*relief_meters/pixel_meters
dy = (np.roll(height, -1, 0)-np.roll(height, 1, 0))*.5*relief_meters/pixel_meters
n = np.stack([-dx, dy, np.ones_like(dx)], axis=-1)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
n = close_edges(n, 4)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
normal_pixels = save(n*.5+.5, 'T_CliffFractured_NormalGL.png')
preview = np.tile(albedo_pixels, (3, 3, 1))
Image.fromarray(preview).resize((1536,1536), Image.Resampling.LANCZOS).save(HERE/'Cliff_RepeatPreview.png')

report = {'size': SIZE, 'tile_meters_at_default': 1/.12,
          'relief_estimate_meters': relief_meters, 'maps': {}}
for name, a in [('albedo',albedo_pixels),('height',height_pixels),('normal',normal_pixels)]:
    f = a.astype(float)
    edge_x = float(np.abs(f[:,0]-f[:,-1]).max())
    edge_y = float(np.abs(f[0]-f[-1]).max())
    assert edge_x == 0 and edge_y == 0, (name, edge_x, edge_y)
    report['maps'][name] = {'max_opposite_edge_error_8bit': max(edge_x,edge_y),
        'mean_interior_neighbor_delta_8bit': float(np.abs(np.diff(f,axis=1)).mean())}
assert np.isfinite(n).all()
assert np.max(np.abs(np.linalg.norm(n,axis=-1)-1)) < 1e-5
report['normal_convention'] = 'OpenGL +Y, importer flipGreenChannel=0'
report['runtime_height'] = False
linear = np.where(albedo <= .04045, albedo/12.92, ((albedo+.055)/1.055)**2.4).mean(axis=(0,1))
report['average_albedo_linear'] = linear.tolist()
report['average_albedo_srgb'] = (1.055*linear**(1/2.4)-.055).tolist()
(HERE/'BakeReport.json').write_text(json.dumps(report,indent=2)+'\n')

# Repeat/trilinear mipmapped BC7 color and BC5 +Y normal; height is a linear BC4
# authoring export, not bound to the shader. Preserve GUIDs when rebaking.
for name, template, srgb, kind, fmt in [
    ('T_CliffFractured_Albedo.png','T_GrassGround_BladeAlbedo.png',1,0,25),
    ('T_CliffFractured_NormalGL.png','T_GrassGround_BladeNormal.png',0,1,27),
    ('T_CliffFractured_Height.png','T_GrassGround_BladeHeight.png',0,0,26)]:
    meta_path = OUT/(name+'.meta')
    old = meta_path.read_text() if meta_path.exists() else ''
    guid = re.search(r'^guid: (\w+)',old,re.M)
    guid = guid.group(1) if guid else uuid.uuid4().hex
    meta = (ROOT/'Assets/Textures/Ground/Grass'/(template+'.meta')).read_text()
    meta = re.sub(r'^guid: \w+', 'guid: '+guid, meta, flags=re.M)
    meta = re.sub(r'maxTextureSize: \d+', 'maxTextureSize: 1024', meta)
    meta = re.sub(r'sRGBTexture: \d+', 'sRGBTexture: '+str(srgb), meta)
    meta = re.sub(r'textureType: \d+', 'textureType: '+str(kind), meta)
    meta = re.sub(r'(buildTarget: Standalone.*?textureFormat:) \d+',r'\g<1> '+str(fmt),meta,flags=re.S)
    meta_path.write_text(meta)
print(json.dumps(report,indent=2))
