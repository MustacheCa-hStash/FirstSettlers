"""Bake runtime grass variants and a periodic short-cover surface from the master."""
import json
import re
from pathlib import Path

import numpy as np
from numba import njit
from PIL import Image
from scipy.ndimage import distance_transform_edt, gaussian_filter

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets/ArtReview/MeadowGrass"
TILE = 256
GROUND_SIZE = 768
GROUND_SPAN = 128 * .3 / 144 * 10
TRIS = np.array([(a+i, a+i+1, a+i+4) if half == 0 else (a+i, a+i+4, a+i+3)
                 for a in range(0, 21, 3) for i in (0, 1) for half in (0, 1)])


@njit
def raster(projected, colors, triangles, width, periodic):
    rgb = np.zeros((width, width, 3), np.float32)
    depth = np.full((width, width), -10., np.float32)
    for b in range(len(projected)):
        for ia, ib, ic in triangles:
            a, c, d = projected[b, ia], projected[b, ib], projected[b, ic]
            det = (c[0]-a[0])*(d[1]-a[1])-(c[1]-a[1])*(d[0]-a[0])
            if abs(det) < 1e-6:
                continue
            for y in range(int(np.floor(min(a[1],c[1],d[1]))), int(np.ceil(max(a[1],c[1],d[1])))):
                for x in range(int(np.floor(min(a[0],c[0],d[0]))), int(np.ceil(max(a[0],c[0],d[0])))):
                    if not periodic and (x < 0 or x >= width or y < 0 or y >= width):
                        continue
                    u = ((x+.5-a[0])*(d[1]-a[1])-(y+.5-a[1])*(d[0]-a[0]))/det
                    v = ((c[0]-a[0])*(y+.5-a[1])-(c[1]-a[1])*(x+.5-a[0]))/det
                    if u < 0 or v < 0 or u+v > 1:
                        continue
                    z = a[2]*(1-u-v)+c[2]*u+d[2]*v
                    sy, sx = y % width, x % width
                    if z <= depth[sy,sx]:
                        continue
                    depth[sy,sx] = z
                    for channel in range(3):
                        rgb[sy,sx,channel] = (colors[b,ia,channel]*(1-u-v)
                            + colors[b,ib,channel]*u + colors[b,ic,channel]*v)
    return rgb, depth


def srgb(values):
    v = np.clip(values, 0, 1)
    return np.uint8(np.rint(255*np.where(v < .0031308, v*12.92, 1.055*v**(1/2.4)-.055)))


def card(points, colors, angle, tilt, tile, atlas, result):
    side = np.array([np.cos(angle), np.sin(angle), 0])
    forward = np.array([-side[1], side[0], 0])
    up = np.cos(tilt)*np.array([0,0,1]) + np.sin(tilt)*forward
    normal = np.cross(side, up)
    p = points @ np.stack((side,up,normal), axis=1)
    lo = p[:,:,:2].min(axis=(0,1)) - .018
    hi = p[:,:,:2].max(axis=(0,1)) + .018
    p[:,:,:2] = (p[:,:,:2]-lo)/(hi-lo) * (TILE*2)
    rgb, depth = raster(p, colors, TRIS, TILE*2, False)
    coverage = depth > -9
    if not coverage.any():
        raise ValueError("Empty blade card")
    nearest = distance_transform_edt(~coverage, return_distances=False, return_indices=True)
    image = np.dstack((srgb(rgb[tuple(nearest)]), coverage.astype(np.uint8)*255))
    tile_image = Image.fromarray(image)
    tile_image = Image.merge("RGBA", tuple(c.resize((TILE,TILE),Image.Resampling.BOX) for c in tile_image.split()))
    tx, ty = tile % 4, tile // 4
    atlas.paste(tile_image.transpose(Image.Transpose.FLIP_TOP_BOTTOM), (tx*TILE,(3-ty)*TILE))

    base = len(result["positions"])//3
    plane = float(np.median(points[:,1,:] @ normal))
    for a,b in ((0,0),(1,0),(0,1),(1,1)):
        pt = side*(lo[0] if a == 0 else hi[0]) + up*(lo[1] if b == 0 else hi[1]) + normal*plane
        result["positions"].extend((pt[0],pt[2],pt[1]))
        result["uv"].extend(((tx+a)/8,(ty+b)/4))
    result["indices"].extend(base+i for i in (0,1,2,1,3,2))


def variant(vertices, colors, center, index, atlas):
    roots = vertices[:,1,:2]
    local = roots - center
    keep = np.max(abs(local),axis=1) < .142
    points = vertices[keep].copy()
    points[:,:,:2] -= center
    tones = colors[keep]
    if len(points) < 20:
        raise ValueError("Insufficient blades in grass variant")
    height = points[:,:,2].max(axis=1)
    near = dict(positions=[], uv=[], indices=[])
    mid = dict(positions=[], uv=[], indices=[])
    modeled = np.argsort(height)[-3:]
    # The master carries many overlapping blades at each root. Reassemble
    # adjacent source samples in each spray so that coverage survives projection.
    nearby = (center, center + [-.32,.18], center + [.24,-.28])
    groups, palettes = [], []
    for i, source in enumerate(nearby):
        mask = np.max(abs(roots-source),axis=1) < .157
        sample = vertices[mask].copy()
        sample[:,:,:2] -= source
        angle = i*2.09
        sample[:,:,:2] = sample[:,:,:2] @ np.array(
            [[np.cos(angle), np.sin(angle)],[-np.sin(angle), np.cos(angle)]])
        low = sample[:,:,2].max(axis=1) <= .38
        sample = sample[low]
        sample[:,:,2] *= 1.12
        groups.append(sample)
        root_light = .72 + .28*np.clip(sample[:,:,2]/.31,0,1)
        palettes.append(colors[mask][low] * np.array([1.08,1.04,.83]) * root_light[:,:,None])
    dense = np.concatenate(groups)
    palette = np.concatenate(palettes)
    crown = dense[:,:,2].max(axis=1)
    short = crown < np.quantile(crown, .79)
    card(dense, palette, .22+index*.53, .16, index*5, atlas, near)
    card(dense, palette, 2.15+index*.53, .23, index*5+1, atlas, near)
    card(dense[short], palette[short], 1.10+index*.49, .43, index*5+2, atlas, near)
    for j in modeled:
        # Two-segment mesh ribbons retain the individual curved silhouette.
        base = len(near["positions"])//3
        for row in (0,3,5,7):
            for col in (0,2):
                p = points[j,row*3+col]
                near["positions"].extend((float(p[0]),float(p[2]*.89),float(p[1])))
                near["uv"].extend((.433 + index*.012, .865 + row*.014))
        near["indices"].extend(base+2*r+i for r in range(3) for i in (0,1,2,1,3,2))
    low = points.copy()
    low[:,:,2] *= .88
    low[:,:,:2] *= 1.26
    card(low, tones, .28+index*.66, .06, index*5+3, atlas, mid)
    card(low, tones, 1.87+index*.66, .19, index*5+4, atlas, mid)
    # A third azimuth keeps the lower cards from all going edge-on on slopes.
    # Reuse the first bake and offset its roots slightly to break the cross.
    angle = 2.10
    c, s = np.cos(angle), np.sin(angle)
    first_card = np.asarray(mid["positions"][:12]).reshape(4,3)
    for x,y,z in first_card:
        mid["positions"].extend((float(x*c-z*s+.032),float(y*.92-.012),float(x*s+z*c-.026)))
    mid["uv"].extend(mid["uv"][:8])
    mid["indices"].extend(8+i for i in (0,1,2,1,3,2))
    return near, mid


def ground(vertices, colors):
    start = np.array([-.98,3.10])
    roots = vertices[:,1,:2] - start
    mask = np.all((roots>=0)&(roots<GROUND_SPAN),axis=1) & (vertices[:,:,2].max(axis=1) < .24)
    points = vertices[mask].copy()
    bases = points[:,1:2,:2].copy()
    points[:,:,:2] = bases + (points[:,:,:2]-bases)*.54
    points[:,:,:2] = (points[:,:,:2]-start) * GROUND_SIZE / GROUND_SPAN
    points[:,:,2] *= .17
    # Interleave a second layer of finer blades from the same master. Wrap
    # their roots and tips together so the full tuft crosses the tile edge.
    fine = points.copy()
    roots_px = fine[:,1:2,:2].copy()
    fine[:,:,:2] = roots_px - (fine[:,:,:2]-roots_px)*.73 + [91,137]
    fine[:,:,2] *= .62
    palette = colors[mask]
    rgb, depth = raster(np.concatenate((points,fine)),
                        np.concatenate((palette,palette*.83)), TRIS, GROUND_SIZE, True)
    covered = depth > -9
    rng = np.random.default_rng(5130)
    noise = rng.standard_normal(covered.shape).astype(np.float32)
    litter = gaussian_filter(noise, 1.15, mode="wrap")
    turf = gaussian_filter(covered.astype(np.float32), 4, mode="wrap")
    pockets = gaussian_filter(turf, 19, mode="wrap")
    # Keep contrast at two spatial scales so a filtered hillside still has
    # turf structure even after individual blade silhouettes are subpixel.
    clumps = gaussian_filter(turf, 8, mode="wrap") - gaussian_filter(turf, 36, mode="wrap")
    clumps /= max(float(np.std(clumps)), .001)
    clumps = np.clip(clumps,-2.3,2.3)
    drifts = gaussian_filter(noise, 51, mode="wrap") - gaussian_filter(noise, 112, mode="wrap")
    drifts = np.clip(drifts/max(float(np.std(drifts)), .0001), -2.3, 2.3)
    exposed = np.clip((.43-pockets)*3.2 + litter*.52, 0, .56)
    base = np.array([.073,.106,.026]) * (.86 + turf[:,:,None]*.34)
    base = base*(1-exposed[:,:,None]) + np.array([.088,.061,.030])*exposed[:,:,None]
    result = np.where(covered[:,:,None], rgb*.59 + base*.50, base)
    result *= (1 + litter[:,:,None]*.21 + clumps[:,:,None]*.065)
    height_field = np.where(covered,np.maximum(depth,0),0)
    relief = gaussian_filter(height_field, .85, mode="wrap")*.72 + litter*.0018
    # Sample the periodic height field from multiple sides. This gives
    # recessed gaps dark rims while keeping the live sun free to move.
    occlusion = np.zeros_like(relief)
    for distance, weight in ((2,.33),(5,.29),(10,.23),(18,.15)):
        for x,y in ((1,0),(0,1),(-1,0),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            neighbor = np.roll(height_field,(y*distance,x*distance),(0,1))
            occlusion += weight * np.clip((neighbor-height_field)/.019,0,1)/8
    occlusion = gaussian_filter(occlusion, .72, mode="wrap")
    ao = np.clip(1 - .72*occlusion - .22*turf, .53, 1)
    visible_color = result * (.57 + .43*ao[:,:,None])
    save_map(srgb(visible_color), "ShortCover.png")
    meter_per_pixel = GROUND_SPAN / GROUND_SIZE
    dx = (np.roll(relief,-1,axis=1)-np.roll(relief,1,axis=1))/(2*meter_per_pixel)
    dy = (np.roll(relief,-1,axis=0)-np.roll(relief,1,axis=0))/(2*meter_per_pixel)
    normal = np.stack((-dx,-dy,np.ones_like(dx)),axis=2)
    normal /= np.linalg.norm(normal,axis=2,keepdims=True)
    save_map(np.uint8(np.rint((normal*.5+.5)*255)), "ShortCoverNormal.png")
    save_map(np.uint8(np.rint(np.repeat(ao[:,:,None],3,axis=2)*255)), "ShortCoverAO.png")
    # Pilot terrain expects linear tone, world-XZ normals and height in one
    # sample. Retain only a little AO in the tone so shade can still vary live.
    luminance = result @ np.array([.2126,.7152,.0722])
    tone = .50 + (luminance/np.mean(luminance)-1)*.33 + (ao-np.mean(ao))*.68 + clumps*.018 + drifts*.028
    tone = np.clip(tone,.13,.86)
    tone += .50 - np.mean(tone)
    height = np.clip(.4 + (relief-np.mean(relief))*19, 0, 1)
    packed = np.dstack((tone, normal[:,:,:2]*.5+.5, height))
    save_map(np.uint8(np.rint(np.clip(packed,0,1)*255)), "PilotTurfSurface.png")
    near_turf(points, fine, palette, drifts, litter, rng)
    print(f"Periodic {GROUND_SPAN:.2f} m ground sample: {covered.mean():.0%} projected blade coverage.")


def save_map(pixels, name):
    Image.fromarray(pixels).transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(OUT/name)


def near_turf(points, fine, palette, drifts, litter, rng):
    """Build the full-color near-chunk surface from actual overlapping shapes."""
    # Olive and fresh blades share the mat with occasional dried tips.
    colors = palette.copy()
    n = len(colors)
    choices = rng.random(n)
    colors *= np.where(choices[:,None,None] < .28, [.96,1.17,.50], [.80,.85,.36])
    dry = choices > .952
    colors[dry] = colors[dry]*.27 + [.115,.087,.030]
    blades = np.concatenate((points,fine))
    blades[:,:,:2] = blades[:,1:2,:2] + (blades[:,:,:2]-blades[:,1:2,:2])*.80
    blades[:,:,2] *= .82

    # Small curled straw, flat green cover and angular grit share the same
    # height/visibility bake as the blades, so color and relief stay aligned.
    debris, debris_color = [], []
    for count, length, width, color, lift in (
        (260, (3,13), (.45,1.8), [.245,.175,.081], .005),
        (3400, (3,12), (1.1,3.5), [.092,.141,.016], .009),
        (2600, (2,9), (1.0,2.7), [.143,.190,.021], .011),
        (110, (2,6), (.9,3.0), [.205,.192,.154], .005),
    ):
        for _ in range(count):
            origin = rng.uniform(0, GROUND_SIZE, 2)
            angle = rng.uniform(0, 2*np.pi)
            axis = np.array([np.cos(angle),np.sin(angle)])
            side = np.array([-axis[1],axis[0]])
            span, breadth = rng.uniform(*length), rng.uniform(*width)
            bend = rng.uniform(-.44,.44)
            tint = np.array(color)*rng.uniform(.62,1.36)
            for t in np.linspace(0,1,8):
                for column in (-1,0,1):
                    p = origin + axis*span*t + side*(span*bend*t*t + column*breadth*np.sin(np.pi*t))
                    z = .001 + lift*np.sin(np.pi*t)*(.78 if column else 1)
                    debris.append((p[0],p[1],z))
                    edge_tone = .66 if column == -1 else 1.26 if column == 0 else .91
                    debris_color.append(tint*edge_tone)
    geometry = np.concatenate((blades,np.asarray(debris).reshape(-1,24,3)))
    surface_colors = np.concatenate((colors,colors*.80,np.asarray(debris_color).reshape(-1,24,3)))
    rgb, depth = raster(geometry,surface_colors,TRIS,GROUND_SIZE,True)
    covered = depth > -9
    height = np.where(covered,np.maximum(depth,0),0)
    cover = gaussian_filter(covered.astype(np.float32), 6, mode="wrap")
    soil = np.clip((.49-cover)*1.35 + .27*litter - .085*drifts,0,.58)
    substrate = np.array([.074,.115,.011]) * (1 + .13*drifts[:,:,None] + .25*litter[:,:,None])
    substrate = substrate*(1-soil[:,:,None]) + np.array([.120,.088,.034])*soil[:,:,None]
    albedo = np.where(covered[:,:,None], rgb*.93 + substrate*.09, substrate)
    # Short, soft height-field occlusion darkens gaps without baking a sun angle.
    occlusion = np.zeros_like(height)
    for distance, weight in ((2,.38),(5,.30),(11,.22),(20,.10)):
        for x,y in ((1,0),(0,1),(-1,0),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            neighbor = np.roll(height,(y*distance,x*distance),(0,1))
            occlusion += weight*np.clip((neighbor-height)/.014,0,1)/8
    ao = np.clip(1 - 1.16*gaussian_filter(occlusion,.65,mode="wrap") - .18*cover,.45,1)
    relief = gaussian_filter(height,.8,mode="wrap")*.72 + litter*.0017
    dx = (np.roll(relief,-1,axis=1)-np.roll(relief,1,axis=1))*GROUND_SIZE/(2*GROUND_SPAN)
    dy = (np.roll(relief,-1,axis=0)-np.roll(relief,1,axis=0))*GROUND_SIZE/(2*GROUND_SPAN)
    normal = np.stack((-dx,-dy,np.ones_like(dx)),axis=2)
    normal /= np.linalg.norm(normal,axis=2,keepdims=True)
    save_map(srgb(albedo), "NearTurfAlbedo.png")
    save_map(np.uint8(np.rint(np.dstack((normal*.5+.5,ao))*255)), "NearTurfRelief.png")


def add_pilot_underlayer(atlas, variants):
    # Reuse the pilot's dense basal spray and authored olive tint beneath the
    # new curved blades. Its two cards cover gaps in the projected master.
    with Image.open(ROOT/"ArtSource/MeadowGrass/BasalCutoutClean.png") as cutout:
        if cutout.size != (1024, 512) or cutout.mode != "RGBA":
            raise ValueError("Expected a 1024 × 512 RGBA meadow cutout")
        source = np.asarray(cutout)[:, :512].copy()
    rgb = source[:,:,:3] / 255
    linear = np.where(rgb < .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4)
    root_light = np.linspace(1,.77,len(source))[:,None,None]
    source[:,:,:3] = srgb(linear * [.62,.77,.69] * root_light)
    # This cutout was authored for the pilot's .42 clip. The review's .24
    # clip is intended for supersampled geometry cards. Reject the low-alpha
    # photographic matte before sharing that lower threshold.
    alpha = source[:,:,3] / 255
    source[:,:,3] = np.uint8(np.rint(np.clip((alpha-.52)/.25,0,1)*255))
    atlas.paste(Image.fromarray(source), (1024,512))
    mesh = (ROOT/"Assets/Models/Grass+Flowers/PilotMeadowGrass_Near.asset").read_text()
    data = np.frombuffer(bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", mesh)[1]),
                         dtype="<f4").reshape(-1,8)[:16]
    for index, (near, _) in enumerate(variants):
        base = len(near["positions"])//3
        angle = .29 + .81*index
        c, s = np.cos(angle), np.sin(angle)
        for x,y,z,_,_,_,u,v in data:
            near["positions"].extend((float(x*c-z*s), float(y*(.98+.035*index)), float(x*s+z*c)))
            near["uv"].extend((float(.5+u*.5),float(v*.5)))
        for offset in (0,8):
            near["indices"].extend(base+offset+2*r+i for r in range(3) for i in (0,1,2,1,3,2))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    data = np.load(ROOT/".utmp/meadow-grass-master.npz")
    vertices, colors = data["vertices"], data["colors"]
    atlas = Image.new("RGBA",(TILE*8,TILE*4))
    variants = [variant(vertices,colors,np.array(center),i,atlas)
                for i,center in enumerate(((-.83,4.46),(.32,5.27),(1.14,4.23)))]
    y = np.linspace(1,0,TILE)[:,None,None]
    swatch = srgb(np.array([.125,.191,.029])[None,None,:] * (.71 + y*.38))
    solid = np.dstack((np.repeat(swatch,TILE,axis=1),np.full((TILE,TILE),255,np.uint8)))
    atlas.paste(Image.fromarray(solid),(TILE*3,0))
    add_pilot_underlayer(atlas, variants)
    atlas.save(OUT/"BladeAtlas.png")
    near = [v[0] for v in variants]
    # The three modeled blades occupy indices 18–71 between the two card sets.
    short = [dict(v, indices=v["indices"][:18]+v["indices"][72:]) for v in near]
    (OUT/"Variants.json").write_text(json.dumps(dict(near=near,shortNear=short,mid=[v[1] for v in variants])))
    ground(vertices,colors)
    print("Baked three variants at 36 tall near / 18 short near / 6 mid triangles per placement.")


if __name__ == "__main__":
    main()
