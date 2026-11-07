"""Build fitted foliage atlases and tiling bark materials from woodland reference sheets."""
import json
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter


SPECIES = ("RedMaple", "SugarMaple", "PaperBirch", "QuakingAspen", "AmericanBeech",
           "WhiteOak", "WeepingWillow", "NorwaySpruce", "EasternWhitePine")
TILE = 512


def cell(sheet, index, inset=0):
    w, h = sheet.size
    x, y = index % 3, index // 3
    return sheet.crop((round(w*x/3)+inset, round(h*y/3)+inset,
                       round(w*(x+1)/3)-inset, round(h*(y+1)/3)-inset))


def keyed_specimen(sheet, index):
    data = np.asarray(cell(sheet, index).convert("RGB"), dtype=np.float32)
    red, green, blue = data.transpose(2, 0, 1)
    magenta = np.minimum(red, blue) - green
    alpha = np.clip((35-magenta)/58, 0, 1)
    data[..., 0] = np.where(magenta > 0, np.minimum(red, green * 1.14), red)
    data[..., 2] = np.where(magenta > 0, np.minimum(blue, green * .85), blue)
    rgba = np.concatenate((data, 255*alpha[..., None]), axis=2)
    pic = Image.fromarray(np.uint8(rgba), "RGBA")
    bounds = pic.getbbox()
    if bounds is None:
        raise ValueError(f"Missing specimen {index}")
    return pic.crop(bounds)


def foliage_card(specimen, index, variation):
    rng = random.Random(8291 + index * 63 + variation)
    pic = Image.new("RGBA", (TILE, TILE))
    draw = ImageDraw.Draw(pic)
    willow, spruce, pine = index == 6, index == 7, index == 8
    segments = []
    center = lambda t: .48 + math.sin(t*2.3 + variation)*.08*t
    start, end = .90, .12
    twig = [(int(TILE*center(j/10)), int(TILE*(start+(end-start)*j/10))) for j in range(11)]
    draw.line(twig, (88,77,47,255), width=4)
    for level in range(9):
        t = (level+.65)/10
        y = start+(end-start)*t
        for sign in (-1,1):
            reach = (.13 + .23*math.sin(math.pi*t)**.8)*rng.uniform(.79,1.07)
            if willow:
                reach *= .86
            tip = (center(t)+sign*reach, y-rng.uniform(.052,.20))
            if willow:
                tip = (tip[0], y+rng.uniform(.015,.09))
            draw.line([(TILE*center(t),TILE*y),(TILE*tip[0],TILE*tip[1])], (97,91,52,255), width=2)
            segments.append(((center(t),y),tip,sign,t))

    def paste(x,y,heading,length):
        # The petiole starts near the attachment point, with the sheet's tips pointing outward.
        angle = math.atan2(heading[0], -heading[1])
        stretch = rng.uniform(.71,1.05)
        height = round(TILE*length)
        width = max(3, round(height*specimen.width/specimen.height*stretch))
        leaf = specimen.resize((width,height),Image.Resampling.LANCZOS)
        bright = ImageEnhance.Brightness(leaf.convert("RGB")).enhance(rng.uniform(.76,1.17))
        bright.putalpha(leaf.getchannel("A"))
        leaf = bright.rotate(-math.degrees(angle),Image.Resampling.BICUBIC,expand=True)
        x += math.sin(angle)*length*.39
        y -= math.cos(angle)*length*.39
        pic.alpha_composite(leaf,(round(x*TILE-leaf.width*.5),round(y*TILE-leaf.height*.5)))

    for root, tip, sign, depth in segments:
        n = 7 if willow else 5
        for k in range(n):
            t = .25+.66*(k+.25)/n
            x = root[0]+(tip[0]-root[0])*t
            y = root[1]+(tip[1]-root[1])*t
            length = (.15 if willow else .24 if pine else .225 if spruce else .123)
            length *= rng.uniform(.77,1.22)*(1-.30*depth)
            # Twigs alternate between upward and outward leaves, or hang on willow.
            dy = rng.uniform(.75,1.3) if willow else rng.uniform(-1.22,.28)
            dx = sign*rng.uniform(.48,1.0)
            paste(x,y,(dx,dy),length)
            if not willow and (k+n+variation)%2 == 0:
                paste(x,y,(dx*.40,-dy*.62-.34),length*.85)
        paste(*tip,(sign*.9,.75 if willow else -.8),.22 if pine else .19 if spruce else .116)
    for i in range(4):
        t = .62 + .075*i
        paste(center(t), start+(end-start)*t, ((-1)**i*.45,-1), .13)
    alpha = pic.getchannel("A")
    bounds = alpha.point(lambda v: 255 if v >= 65 else 0).getbbox()
    # Normalize artwork scale while keeping fixed alpha-safe gutters.
    core = pic.crop(bounds)
    core.thumbnail((TILE-30,TILE-28), Image.Resampling.LANCZOS)
    result = Image.new("RGBA",(TILE,TILE))
    result.alpha_composite(core,((TILE-core.width)//2,(TILE-core.height)//2))
    return result


def fit_rows(pic):
    alpha = np.array(pic.getchannel("A")) >= 49
    ys, xs = np.nonzero(alpha)
    top, bottom = max(1, int(ys.min())-2), min(TILE-2,int(ys.max())+3)
    edges = np.round(np.linspace(top,bottom,5)).astype(int)
    widths = []
    for lo, hi in zip(edges[:-1], edges[1:]):
        x = np.nonzero(alpha[lo:hi+1])[1]
        widths.append((max(1,int(x.min())-2), min(TILE-1,int(x.max())+3)))
    rows = []
    for i, y in enumerate(edges):
        adjacent = widths[max(0,i-1):min(len(widths),i+1)]
        rows.append((int(y),min(w[0] for w in adjacent),max(w[1] for w in adjacent)))
    # Tighten each row against every actual occupied scanline in the adjacent strips.
    # The interpolated edge still encloses the cutout and its filtering margin.
    strips = []
    for a,b in zip(edges,edges[1:]):
        spans = []
        for y in range(a,b+1):
            occupied = np.flatnonzero(alpha[y])
            if len(occupied):
                spans.append(((y-a)/(b-a),int(occupied[0])-2,int(occupied[-1])+3))
        strips.append(spans)
    for order in (range(5),range(4,-1,-1),range(5)):
        for i in order:
            y,left,right = rows[i]
            left_limit,right_limit = float(TILE),0
            for segment in (i-1,i):
                if not 0 <= segment < len(strips):
                    continue
                _,other_left,other_right = rows[i-1 if segment==i-1 else i+1]
                for t,scan_left,scan_right in strips[segment]:
                    weight = t if segment==i-1 else 1-t
                    if weight <= 0:
                        continue
                    left_limit = min(left_limit,(scan_left-(1-weight)*other_left)/weight)
                    right_limit = max(right_limit,(scan_right-(1-weight)*other_right)/weight)
            rows[i] = (y,max(left,math.floor(left_limit)),min(right,math.ceil(right_limit)))
    for (a,b),spans in zip(zip(rows,rows[1:]),strips):
        for t,left,right in spans:
            if (1-t)*a[1]+t*b[1] > left+.001 or (1-t)*a[2]+t*b[2] < right-.001:
                raise ValueError("Fitted foliage mesh clips the source cutout")
    fitted = sum((b[0]-a[0])*((a[2]-a[1])+(b[2]-b[1]))*.5 for a,b in zip(rows,rows[1:]))
    tight_rectangle = (xs.max()-xs.min()+5)*(bottom-top)
    return {"rows": [[n/TILE for n in row] for row in rows], "area": fitted/(TILE*TILE),
            "tightRectArea": tight_rectangle/(TILE*TILE), "opaqueArea": float(alpha.mean())}


def padded_tile(pic):
    data = np.array(pic)
    rgb = data[...,:3].copy()
    valid = data[...,3] >= 48
    for _ in range(8):
        for axis, step in ((0,-1),(0,1),(1,-1),(1,1)):
            fill = ~valid & np.roll(valid,step,axis)
            rgb[fill] = np.roll(rgb,step,axis)[fill]
            valid[fill] = True
    rgb[~valid] = (62,82,37)
    data[...,:3] = rgb
    return Image.fromarray(data,"RGBA")


def bark_maps(sheet, index):
    # Mirrored texel-center borders preserve repeat continuity in both axes.
    sample = cell(sheet,index,8).resize((256,512),Image.Resampling.LANCZOS)
    sample = ImageEnhance.Color(sample).enhance(.66)
    color = Image.new("RGB",(512,1024))
    color.paste(sample,(0,0))
    color.paste(sample.transpose(Image.Transpose.FLIP_LEFT_RIGHT),(256,0))
    color.paste(color.crop((0,0,512,512)).transpose(Image.Transpose.FLIP_TOP_BOTTOM),(0,512))
    gray = np.asarray(color.convert("L"),dtype=np.float32)/255
    smooth = np.asarray(color.convert("L").filter(ImageFilter.GaussianBlur(5)),dtype=np.float32)/255
    relief = .65*gray+.35*(gray-smooth)
    intensity = .42 if index in (2,3,4) else .85
    dx = (np.roll(relief,-1,1)-np.roll(relief,1,1))*intensity
    dy = (np.roll(relief,-1,0)-np.roll(relief,1,0))*intensity
    vector = np.stack((-dx,dy,np.ones_like(dx)),axis=2)
    normal = vector / np.linalg.norm(vector,axis=2,keepdims=True)
    return color,Image.fromarray(np.uint8((normal*.5+.5)*255),"RGB")


def main():
    leaf_sheet,bark_sheet,output = map(Path,sys.argv[1:])
    output.mkdir(parents=True,exist_ok=True)
    leaves,bark = Image.open(leaf_sheet),Image.open(bark_sheet)
    footprints = {}
    for index,name in enumerate(SPECIES):
        specimen = keyed_specimen(leaves,index)
        atlas = Image.new("RGBA",(TILE*2,TILE*2))
        footprints[name] = []
        for variant in range(4):
            tile = foliage_card(specimen,index,variant)
            footprints[name].append(fit_rows(tile))
            atlas.paste(padded_tile(tile),((variant%2)*TILE,(variant//2)*TILE))
        atlas.save(output/(name+"_Foliage.png"))
        color,normal = bark_maps(bark,index)
        color.save(output/(name+"_Bark.png"))
        normal.save(output/(name+"_BarkNormal.png"))
    (output/"FoliageFootprints.json").write_text(json.dumps(footprints,indent=2))


if __name__ == "__main__":
    main()
