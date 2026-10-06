"""Compose the supplied leaf by affine transforms only; never synthesize leaf shapes."""
from pathlib import Path
import hashlib
import io
import json
import math
import zipfile
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SOURCE = Path(r"C:\Users\samee\OneDrive\Desktop\TreeModels\kenney_foliage-sprites\PNG\Shaded\sprite_0086.png")
SIZE = 1024
SEED = 73129
CORNER = .22
PREFIX = "sugar_maple_card_v02"


def png_bytes(im):
    buff = io.BytesIO()
    im.save(buff, format="PNG")
    return buff.getvalue()


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    source = Image.open(SOURCE).convert("RGBA")
    a = np.asarray(source).copy()
    alpha = a[..., 3].copy()
    ys, xs = np.nonzero(alpha)
    bbox = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    yy, xx = np.mgrid[:a.shape[0], :a.shape[1]]
    t = np.clip((yy - bbox[1]) / (bbox[3] - bbox[1] - 1), 0, 1)
    # A neutral brightness gradient from a light tip to a slightly darker base.
    # Alpha is copied byte-for-byte; no veins, new contours, or painted leaves.
    source_gray = a[..., :3].astype(float).mean(axis=2)
    gray = np.rint(source_gray * (235 - 37 * t) / 255).astype(np.uint8)
    a[..., :3] = gray[..., None]
    graded = Image.fromarray(a)
    assert np.array_equal(np.asarray(graded)[..., 3], alpha)
    graded.save(ROOT / "sugar_maple_leaf_graded.png")
    leaf = graded.crop(bbox)
    rng = np.random.default_rng(SEED)
    sprites = []
    for i in range(140):
        width = int(rng.integers(210, 315))
        angle = float(rng.uniform(-180, 180))
        # Resample gray and alpha separately to avoid bright RGB fringes from
        # unpremultiplying a bicubic/Lanczos result at partially covered edges.
        new_size = (width, round(width * leaf.height / leaf.width))
        gray_layer = leaf.getchannel("R").resize(new_size, Image.Resampling.LANCZOS)
        alpha_layer = leaf.getchannel("A").resize(new_size, Image.Resampling.LANCZOS)
        gray_layer = gray_layer.rotate(angle, Image.Resampling.BICUBIC, expand=True, fillcolor=213)
        alpha_layer = alpha_layer.rotate(angle, Image.Resampling.BICUBIC, expand=True, fillcolor=0)
        rotated = Image.merge("RGBA", (gray_layer, gray_layer, gray_layer, alpha_layer))
        sprites.append((rotated, np.asarray(rotated)[..., 3] >= 128, width, angle))

    outline_uv = [(CORNER, 0), (1-CORNER, 0), (1, CORNER), (1, 1-CORNER),
                  (1-CORNER, 1), (CORNER, 1), (0, 1-CORNER), (0, CORNER)]
    outline = [(round(u*(SIZE-1)), round(v*(SIZE-1))) for u, v in outline_uv]
    allowed_im = Image.new("L", (SIZE, SIZE))
    # Inset the placement envelope, preserving every complete leaf within the mesh.
    inset = 14
    safe_outline = [(inset + round(u*(SIZE-1-2*inset)), inset + round(v*(SIZE-1-2*inset)))
                    for u, v in outline_uv]
    ImageDraw.Draw(allowed_im).polygon(safe_outline, fill=255)
    allowed = np.asarray(allowed_im) > 0
    occupied = np.zeros((SIZE, SIZE), dtype=bool)
    placements = []
    merged = Image.new("RGBA", (SIZE, SIZE))
    target = .655
    for layer_index in range(60):
        candidates = []
        for _ in range(650):
            sprite_id = int(rng.integers(len(sprites)))
            sprite, mask, width, angle = sprites[sprite_id]
            x = int(rng.integers(inset, SIZE-inset-sprite.width+1))
            y = int(rng.integers(inset, SIZE-inset-sprite.height+1))
            region = np.s_[y:y+sprite.height, x:x+sprite.width]
            # Reject instead of clipping: all source leaf pixels remain intact.
            nonzero = np.asarray(sprite)[..., 3] > 0
            if np.any(nonzero & ~allowed[region]):
                continue
            new = int(np.count_nonzero(mask & ~occupied[region]))
            total = int(np.count_nonzero(mask))
            fraction = new / total
            if new < 3300 or fraction < .22:
                continue
            # Prefer useful coverage, with moderate overlap once the first layer exists.
            preferred = .88 if layer_index < 9 else .65
            score = new * (1 - .45 * abs(fraction-preferred))
            candidates.append((score, sprite_id, x, y, new, fraction))
        if not candidates:
            break
        candidates.sort(reverse=True)
        # Choose among the strongest candidates to keep spacing less regular.
        _, sprite_id, x, y, new, fraction = candidates[int(rng.integers(min(4, len(candidates))))]
        sprite, mask, width, angle = sprites[sprite_id]
        region = np.s_[y:y+sprite.height, x:x+sprite.width]
        occupied[region] |= mask
        merged.alpha_composite(sprite, (x, y))
        placements.append({"sprite": sprite_id, "x": x, "y": y,
                           "source_leaf_width_px": width, "rotation_degrees": round(angle, 5),
                           "new_opaque_pixels": new, "new_fraction": round(fraction, 5)})
        if occupied.mean() >= target:
            break

    # Give transparent RGB a neutral continuation instead of a black halo.
    # This has no effect on alpha coverage or the visible leaf contours.
    out = np.asarray(merged).copy()
    out[out[..., 3] == 0, :3] = 213
    merged = Image.fromarray(out)
    merged.save(ROOT / f"{PREFIX}.png")
    merged.getchannel("A").save(ROOT / f"{PREFIX}_opacity.png")
    preview = Image.new("RGBA", (SIZE, SIZE), (36, 39, 43, 255))
    preview.alpha_composite(merged)
    preview.convert("RGB").save(ROOT / f"{PREFIX}_preview.png")

    # Editable OpenRaster stack: every layer is an unchanged transformed copy.
    image_xml = ET.Element("image", {"w": str(SIZE), "h": str(SIZE), "name": "Sugar maple exact-copy card"})
    stack = ET.SubElement(image_xml, "stack")
    with zipfile.ZipFile(ROOT / f"{PREFIX}.ora", "w") as z:
        z.writestr("mimetype", "image/openraster", compress_type=zipfile.ZIP_STORED)
        for i in reversed(range(len(placements))):
            p = placements[i]
            name = f"data/leaf-{i+1:02d}.png"
            ET.SubElement(stack, "layer", {"name": f"Leaf {i+1:02d}", "src": name,
                "x": str(p["x"]), "y": str(p["y"]), "opacity": "1.0",
                "visibility": "visible", "composite-op": "svg:src-over"})
            z.writestr(name, png_bytes(sprites[p["sprite"]][0]), compress_type=zipfile.ZIP_DEFLATED)
        z.writestr("stack.xml", ET.tostring(image_xml, encoding="utf-8", xml_declaration=True))
        z.writestr("mergedimage.png", png_bytes(merged))
        thumbnail = merged.copy()
        thumbnail.thumbnail((256, 256))
        z.writestr("Thumbnails/thumbnail.png", png_bytes(thumbnail))

    mtl = "\n".join([f"newmtl {PREFIX}", "Ka 1 1 1", "Kd 1 1 1", "Ks 0 0 0", "d 1",
                      f"map_Kd {PREFIX}.png", f"map_d {PREFIX}_opacity.png", ""])
    (ROOT / f"{PREFIX}.mtl").write_text(mtl, encoding="ascii")
    for name, uv in [("octagon", outline_uv), ("square", [(0,0),(1,0),(1,1),(0,1)])]:
        lines = [f"mtllib {PREFIX}.mtl", f"o SugarMapleCard_{name}",
                 "# 0.6 m card in XY; bottom-center attachment origin; front normal +Z"]
        for u, v in uv:
            lines.append(f"v {(u-.5)*.6:.7f} {(1-v)*.6:.7f} 0")
        for u, v in uv:
            lines.append(f"vt {u:.7f} {1-v:.7f}")
        lines += ["vn 0 0 1", f"usemtl {PREFIX}"]
        # Reversed texture-space winding makes front normals point +Z.
        for k in range(1, len(uv)-1):
            indices = (1, k+2, k+1)
            lines.append("f " + " ".join(f"{i}/{i}/1" for i in indices))
        (ROOT / f"{PREFIX}_{name}.obj").write_text("\n".join(lines)+"\n", encoding="ascii")

    actual = np.asarray(merged)[..., 3] >= 128
    geometry_im = Image.new("L", (SIZE, SIZE))
    ImageDraw.Draw(geometry_im).polygon(outline, fill=255)
    geometry = np.asarray(geometry_im) > 0
    assert not np.any(actual & ~geometry), "Leaf coverage escapes the octagonal mesh"
    stats = {"source": str(SOURCE), "source_sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
             "seed": SEED, "dimensions": [SIZE, SIZE], "leaf_copies": len(placements),
             "coverage_at_alpha_0_5_percent": round(float(actual.mean()*100), 3),
             "coverage_inside_octagon_percent": round(float(actual.sum()/geometry.sum()*100), 3),
             "octagon_area_fraction": 1-2*CORNER**2, "octagon_triangles": 6, "square_triangles": 2,
             "graded_leaf_rgb": "Supplied sprite shading multiplied by neutral tip/base gradient (235/255 to 198/255); original alpha preserved",
             "placements": placements}
    (ROOT / f"{PREFIX}_layout.json").write_text(json.dumps(stats, indent=2), encoding="utf-8")
    print(json.dumps({k:v for k,v in stats.items() if k!="placements"}, indent=2))


if __name__ == "__main__":
    main()
