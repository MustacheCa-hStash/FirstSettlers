"""Finish the generated atlas after user-authorized pixel cleanup.

Uses Pillow and NumPy only. No mesh, material or Unity asset changes.
"""
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parent
SIZE = 2048
PAD = 16
SOURCE = ROOT / 'ordinary-wood-trim-design.png'
WEAVE_SOURCE = ROOT / 'ordinary-wood-trim-original.png'


def rgba_image(a):
    return Image.fromarray(np.clip(np.rint(a), 0, 255).astype(np.uint8))


def periodic(a, axes=(1,), margin=32):
    """Blend opposing narrow edge strips; retain the interior artwork.

    Paired edge pixels converge to the same value, without relying on
    a whole-atlas repeat for regions inside the atlas.
    """
    out = a.astype(np.float32).copy()
    for axis in axes:
        view = np.moveaxis(out, axis, 0)
        original = view.copy()
        for d in range(min(margin, len(view)//4)):
            weight = 0.5 + 0.5*np.cos(np.pi*d/margin)
            target = (original[d] + original[-1-d])*0.5
            view[d] = original[d]*(1-weight) + target*weight
            view[-1-d] = original[-1-d]*(1-weight) + target*weight
    return out


def dilate_rgb(rgb, valid, iterations=40):
    """Propagate twig colours into transparent texels, avoiding chroma halos."""
    out = rgb.astype(np.float32).copy()
    known = valid.copy()
    for _ in range(iterations):
        total = np.zeros_like(out)
        count = np.zeros(known.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            k = np.roll(known, (dy, dx), (0, 1))
            v = np.roll(out, (dy, dx), (0, 1))
            total += v*k[..., None]
            count += k
        ring = (~known) & (count > 0)
        if not ring.any():
            break
        out[ring] = total[ring]/count[ring, None]
        known[ring] = True
    out[~known] = np.median(out[valid], axis=0)
    return out


def clean_weave(im):
    src = np.asarray(im.convert('RGBA')).astype(np.float32)
    hsv = np.asarray(im.convert('RGB').convert('HSV')).astype(np.float32)/255
    hue, sat = hsv[..., 0], hsv[..., 1]
    # Use the ORIGINAL pre-repair RGB artwork, not the damaged generated alpha
    # or bright-colour repair candidates. Its neutral checkerboard is separated
    # from the brown twig silhouettes by colour saturation.
    brown = (hue > .015) & (hue < .20)
    solid = (sat > .16) & brown
    mask = Image.fromarray(solid.astype(np.uint8)*255)
    # Close pinholes and smooth single-pixel contour noise without eroding rods.
    mask = mask.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
    mask = mask.filter(ImageFilter.MedianFilter(3)).filter(ImageFilter.GaussianBlur(.45))
    # Only trustworthy wood colours seed RGB dilation; checkerboard-mixed edge
    # pixels cannot leave pale halos in the new antialiased silhouette.
    rgb = dilate_rgb(src[..., :3], (sat > .22) & brown)
    return rgba_image(np.dstack((rgb, np.asarray(mask)))), int((~solid).sum())


def make_weave(im, size):
    a = np.asarray(im.resize((size, size), Image.Resampling.LANCZOS)).astype(np.float32)
    # Use premultiplied colour while blending mask boundaries.
    alpha = a[..., 3:]/255
    p = periodic(np.concatenate((a[..., :3]*alpha, a[..., 3:]), axis=2), (1, 0), 32)
    # Do not leave broad partially transparent twigs from seam crossfades.
    # Rebuild a solid silhouette and restrict antialiasing to its contour.
    mask_image = Image.fromarray((p[..., 3] >= 128).astype(np.uint8)*255)
    mask_image = mask_image.filter(ImageFilter.MedianFilter(3)).filter(ImageFilter.GaussianBlur(.45))
    mask = np.asarray(mask_image).astype(np.float32)
    mask[mask < 8] = 0
    mask[mask > 247] = 255
    rgb = np.divide(p[..., :3], np.maximum(p[..., 3:]/255, 1e-6))
    rgb = dilate_rgb(np.clip(rgb, 0, 255), mask >= 128)
    # Dilated colours can differ at empty edge texels; make the complete RGBA
    # endpoints identical as well as the visible premultiplied colour.
    rgb = periodic(rgb, (1, 0), 8)
    # Match alpha endpoint pixels only; avoid feathering full boundary strips.
    mask = periodic(mask, (1, 0), 1)
    rgba = np.dstack((rgb, mask))
    return np.asarray(rgba_image(rgba))


def edge_delta(a, axis):
    v = np.moveaxis(a.astype(np.int16), axis, 0)
    return int(np.abs(v[0] - v[-1]).max())


def uv_rect(box):
    x0, y0, x1, y1 = box
    return [(x0+.5)/SIZE, 1-(y1-.5)/SIZE, (x1-.5)/SIZE, 1-(y0+.5)/SIZE]


def font(size):
    try:
        return ImageFont.truetype('C:/Windows/Fonts/arial.ttf', size)
    except OSError:
        return ImageFont.load_default(size=size)


def checker(size, cell=24):
    y, x = np.indices((size[1], size[0]))
    c = np.where(((x//cell+y//cell)%2)[..., None], 225, 190)
    return Image.fromarray(np.repeat(c, 3, axis=2).astype(np.uint8)).convert('RGBA')


def main():
    source = Image.open(SOURCE).convert('RGBA')
    sw, sh = source.size
    atlas = np.zeros((SIZE, SIZE, 4), np.uint8)
    atlas[..., 3] = 255
    regions = {}
    wood_deltas = []
    for i, label in enumerate(('A', 'B', 'C', 'D')):
        crop = source.crop((0, round(sh*i/8)+2, sw, round(sh*(i+1)/8)-2)).convert('RGB')
        inner = np.asarray(crop.resize((SIZE, 256-2*PAD), Image.Resampling.LANCZOS))
        inner = np.asarray(rgba_image(periodic(inner, (1,), 48)))
        wood_deltas.append(edge_delta(inner, 1))
        filled = np.pad(inner, ((PAD, PAD), (0, 0), (0, 0)), mode='edge')
        atlas[i*256:(i+1)*256, :, :3] = filled
        regions[label] = {'content_pixels_top_left': [0, i*256+PAD, SIZE, (i+1)*256-PAD], 'repeat': 'U', 'nominal_metres': [4, .5]}

    utility = {
        'E': ((0, .5, .25, .75), (0, 1024, 512, 1536)),
        'F': ((.25, .5, .5, .75), (512, 1024, 1024, 1536)),
        'G': ((0, .75, .5, .875), (0, 1536, 1024, 1792)),
        'H': ((0, .875, .5, 1), (0, 1792, 1024, 2048)),
    }
    for label, (frac, box) in utility.items():
        crop = source.crop(tuple(round(v*n) for v, n in zip(frac, (sw, sh, sw, sh)))).convert('RGB')
        x0, y0, x1, y1 = box
        inner = np.asarray(crop.resize((x1-x0-2*PAD, y1-y0-2*PAD), Image.Resampling.LANCZOS))
        if label in ('G', 'H'):
            inner = np.asarray(rgba_image(periodic(inner, (1,), 32)))
        atlas[y0:y1, x0:x1, :3] = np.pad(inner, ((PAD, PAD), (PAD, PAD), (0, 0)), mode='edge')
        regions[label] = {'content_pixels_top_left': [x0+PAD, y0+PAD, x1-PAD, y1-PAD], 'repeat': 'local U' if label in ('G', 'H') else 'none'}

    weave_art = Image.open(WEAVE_SOURCE).convert('RGBA')
    ww, wh = weave_art.size
    weave_source, removed_background = clean_weave(weave_art.crop((ww//2, wh//2, ww, wh)))
    weave = make_weave(weave_source, 1024-2*PAD)
    # Wrap padding carries the opposite side's texels around the content tile.
    atlas[1024:, 1024:] = np.pad(weave, ((PAD, PAD), (PAD, PAD), (0, 0)), mode='wrap')
    regions['I'] = {'content_pixels_top_left': [1040, 1040, 2032, 2032], 'repeat': 'local U and V', 'nominal_metres': [2, 2]}
    for region in regions.values():
        region['uv_texel_centres'] = uv_rect(region['content_pixels_top_left'])
    rgba_image(atlas).save(ROOT / 'ordinary-wood-trim-albedo.png')
    standalone = make_weave(weave_source, 1024)
    rgba_image(standalone).save(ROOT / 'wattle-tile-albedo.png')

    # A labelled reference is separate from the clean material texture.
    preview = Image.new('RGBA', (2048, 2380), '#f4f1e9')
    preview.alpha_composite(Image.alpha_composite(checker((2048, 2048), 32), rgba_image(atlas)))
    draw = ImageDraw.Draw(preview)
    labels = [('A  RIVEN PLANKS', 20, 22), ('B  ALTERNATE PLANKS', 20, 278), ('C  HEWN RAILS / BEAMS', 20, 534), ('D  POLES / BARK', 20, 790), ('E  END GRAIN', 20, 1046), ('F  END GRAIN', 532, 1046), ('G  LASHING', 20, 1558), ('H  PEG GRAIN', 20, 1814), ('I  WATTLE + ALPHA', 1044, 1046)]
    for text, x, y in labels:
        bounds = draw.textbbox((x, y), text, font=font(31))
        draw.rectangle((bounds[0]-9, bounds[1]-7, bounds[2]+9, bounds[3]+7), fill=(245, 241, 230, 235))
        draw.text((x, y), text, font=font(31), fill='#242923')
    draw.text((28, 2100), 'LABELLED PREVIEW ONLY — use ordinary-wood-trim-albedo.png on the mesh', font=font(34), fill='#242923')
    draw.text((28, 2160), '2048 x 2048 RGBA atlas | 16 px gutters | wood repeats along grain', font=font(30), fill='#242923')
    draw.text((28, 2215), 'Wattle repeats inside region I using UV sections; standalone tile also supplied.', font=font(30), fill='#242923')
    draw.text((28, 2270), 'Source artwork was 1254 x 1254; export is upscaled, without new captured detail.', font=font(28), fill='#242923')
    preview.convert('RGB').save(ROOT / 'trim-labelled-preview.png')

    repeated = Image.new('RGBA', (1536, 1390), '#f4f1e9')
    d = ImageDraw.Draw(repeated)
    d.text((20, 12), 'REPEAT CHECK — wood strips x3; wattle tile x2 by x2', font=font(29), fill='#242923')
    for i in range(4):
        band = rgba_image(atlas[i*256+PAD:(i+1)*256-PAD]).resize((512, 100), Image.Resampling.LANCZOS)
        for j in range(3):
            repeated.alpha_composite(band, (j*512, 65+i*120))
    tile = rgba_image(standalone).resize((384, 384), Image.Resampling.LANCZOS)
    bg = checker((768, 768), 24)
    for yy in range(2):
        for xx in range(2):
            bg.alpha_composite(tile, (xx*384, yy*384))
    repeated.alpha_composite(bg, (16, 580))
    d.text((815, 620), 'Gaps are real alpha.', font=font(30), fill='#242923')
    d.text((815, 674), 'Wood is fully opaque.', font=font(30), fill='#242923')
    d.text((815, 728), 'Paired edges match.', font=font(30), fill='#242923')
    d.text((815, 790), 'Checkerboard is preview only.', font=font(26), fill='#242923')
    repeated.convert('RGB').save(ROOT / 'repeat-check-preview.png')

    report = {
        'source_size': list(source.size), 'atlas_size': [SIZE, SIZE], 'upscaled': True,
        'padding_pixels': PAD, 'wood_alpha_min': int(atlas[:1024, :, 3].min()),
        'utility_alpha_min': int(atlas[1024:, :1024, 3].min()),
        'weave_source': WEAVE_SOURCE.name,
        'source_background_pixels_excluded': removed_background,
        'revision': 2,
        'wattle_content_holes_below_half_alpha_fraction': float((weave[..., 3] < 128).mean()),
        'wood_U_edge_max_differences_RGBA': wood_deltas,
        'wattle_content_edge_max_differences_RGBA': {'U': edge_delta(weave, 1), 'V': edge_delta(weave, 0)},
        'standalone_wattle_edge_max_differences_RGBA': {'U': edge_delta(standalone, 1), 'V': edge_delta(standalone, 0)},
        'regions': regions,
        'limits': ['Paired pixel edges are continuous; natural repetition remains visible.', 'Source detail is upscaled rather than newly captured.', 'Atlas gutters do not prevent all low-mip cross-region mixing.', 'No Unity in-game import or performance validation was run.'],
    }
    assert report['wood_alpha_min'] == report['utility_alpha_min'] == 255
    assert not any(wood_deltas)
    assert edge_delta(weave, 1) == edge_delta(weave, 0) == 0
    assert edge_delta(standalone, 1) == edge_delta(standalone, 0) == 0
    assert .10 < report['wattle_content_holes_below_half_alpha_fraction'] < .65
    (ROOT / 'atlas-layout.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'regions'}, indent=2))


if __name__ == '__main__':
    main()
