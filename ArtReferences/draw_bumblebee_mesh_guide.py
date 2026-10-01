"""Draw the orthographic, mirrored bumblebee mesh tracing guide."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


OUT = Path(__file__).with_name("bumblebee_mesh_guide.png")
W, H, AA = 1600, 1280, 3
SCALE = 500
ORIGIN = (800, 650)

# Coordinates are Blender X/Y units. The head faces +Y.
BODY_RIGHT = [
    (0.00, 0.58), (0.09, 0.57), (0.15, 0.53), (0.19, 0.47),
    (0.19, 0.39), (0.16, 0.32), (0.14, 0.29), (0.21, 0.27),
    (0.27, 0.21), (0.30, 0.12), (0.31, 0.01), (0.29, -0.10),
    (0.25, -0.18), (0.29, -0.21), (0.34, -0.30), (0.35, -0.41),
    (0.32, -0.52), (0.25, -0.62), (0.14, -0.68), (0.00, -0.70),
]
BODY_ROWS = [(0.19, 0.39), (0.14, 0.29), (0.30, 0.12),
             (0.29, -0.10), (0.25, -0.18), (0.34, -0.30),
             (0.32, -0.52)]
FOREWING_RIGHT = [
    (0.15, 0.14), (0.33, 0.24), (0.54, 0.31), (0.78, 0.37),
    (0.98, 0.35), (1.08, 0.29), (1.07, 0.22), (0.98, 0.17),
    (0.78, 0.12), (0.56, 0.09), (0.40, 0.07), (0.17, 0.04),
]
FORE_CHORDS = [(1, 10), (2, 9), (3, 8), (4, 7)]
HINDWING_RIGHT = [
    (0.16, -0.02), (0.32, 0.00), (0.49, -0.03), (0.67, -0.08),
    (0.80, -0.14), (0.83, -0.21), (0.76, -0.26), (0.61, -0.27),
    (0.42, -0.21), (0.17, -0.13),
]
HIND_CHORDS = [(1, 8), (2, 7), (3, 6)]
LEGS_RIGHT = [
    [(0.25, 0.22), (0.36, 0.36), (0.43, 0.40)],
    [(0.30, -0.03), (0.45, -0.18), (0.55, -0.35)],
    [(0.29, -0.28), (0.43, -0.42), (0.48, -0.57)],
]
ANTENNA_RIGHT = [(0.08, 0.55), (0.17, 0.68), (0.24, 0.72)]


def pt(xy, side=1):
    x, y = xy
    return (round((ORIGIN[0] + side * x * SCALE) * AA),
            round((ORIGIN[1] - y * SCALE) * AA))


def draw_vertices(draw, points, side=1, radius=6):
    for point in points:
        x, y = pt(point, side)
        r = radius * AA
        draw.ellipse((x-r, y-r, x+r, y+r), fill="#080808")


def dashed(draw, a, b, side=1, color="#666666"):
    from math import hypot
    x0, y0 = pt(a, side)
    x1, y1 = pt(b, side)
    length = hypot(x1-x0, y1-y0)
    if length == 0:
        return
    ux, uy = (x1-x0)/length, (y1-y0)/length
    for offset in range(0, int(length), 18*AA):
        end = min(offset + 10*AA, length)
        draw.line((x0+ux*offset, y0+uy*offset,
                   x0+ux*end, y0+uy*end), fill=color, width=2*AA)


canvas = Image.new("RGB", (W*AA, H*AA), "white")
d = ImageDraw.Draw(canvas)
font_path = "C:/Windows/Fonts/arial.ttf"
title = ImageFont.truetype(font_path, 32*AA)
small = ImageFont.truetype(font_path, 21*AA)
tiny = ImageFont.truetype(font_path, 18*AA)

d.text((80*AA, 65*AA), "BUMBLEBEE  /  TOP ORTHOGRAPHIC MESH GUIDE",
       font=title, fill="#181818")
d.text((80*AA, 113*AA), "Head toward +Y   |   Mirror across X = 0",
       font=small, fill="#555555")

# A faint axis remains visible in the empty margin, away from mesh edges.
d.line((pt((0, 0.80)), pt((0, -0.80))), fill="#dedede", width=2*AA)
d.text((820*AA, 195*AA), "+Y", font=small, fill="#666666")

# Six legs and two antennae are optional low-detail silhouette pieces.
for side in (-1, 1):
    for leg in LEGS_RIGHT:
        d.line([pt(p, side) for p in leg], fill="#343434", width=4*AA, joint="curve")
        draw_vertices(d, leg, side, 5)
    d.line([pt(p, side) for p in ANTENNA_RIGHT], fill="#333333", width=3*AA)
    draw_vertices(d, ANTENNA_RIGHT, side, 5)

# Draw hindwings, then forewings, then the body so the roots read as tucked under it.
for points, chords, fill in ((HINDWING_RIGHT, HIND_CHORDS, "#eff2f2"),
                             (FOREWING_RIGHT, FORE_CHORDS, "#e6ecee")):
    for side in (-1, 1):
        polygon = [pt(p, side) for p in points]
        d.polygon(polygon, fill=fill)
        d.line(polygon + polygon[:1], fill="#161616", width=3*AA, joint="curve")
        for first, second in chords:
            d.line((pt(points[first], side), pt(points[second], side)),
                   fill="#555555", width=2*AA)
        draw_vertices(d, points, side)

body = BODY_RIGHT + [(-x, y) for x, y in reversed(BODY_RIGHT[1:-1])]
d.polygon([pt(p) for p in body], fill="#d2d2d2")
d.line([pt(p) for p in body] + [pt(body[0])], fill="#161616", width=4*AA, joint="curve")
for x, y in BODY_ROWS:
    left, mid, right = pt((-x, y)), pt((0, y)), pt((x, y))
    d.line((left, mid, right), fill="#474747", width=2*AA)
    draw_vertices(d, [(0, y)], radius=5)
draw_vertices(d, body)

# X-ray dashes reveal hidden root edges beneath the raised thorax.
for side in (-1, 1):
    for wing in (FOREWING_RIGHT, HINDWING_RIGHT):
        dashed(d, wing[0], wing[1], side)
        dashed(d, wing[-1], wing[-2], side)
        draw_vertices(d, [wing[0], wing[-1]], side, 5)

d.line((80*AA, 1112*AA, 1520*AA, 1112*AA), fill="#dddddd", width=2*AA)
d.text((80*AA, 1137*AA), "Black dots = vertices     Straight lines = edges     Dashed lines = wing root hidden under thorax",
       font=tiny, fill="#333333")
d.text((80*AA, 1176*AA), "Body: raised and rounded     Wings: flat, separate islands     Left side: mirror the right side",
       font=tiny, fill="#555555")

canvas.resize((W, H), Image.Resampling.LANCZOS).save(OUT)
print(OUT)
