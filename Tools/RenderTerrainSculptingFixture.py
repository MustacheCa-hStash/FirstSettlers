"""Render actual P2 fixture triangles for geometry QA (requires NumPy and Pillow)."""
from pathlib import Path
import argparse
import numpy as np
from PIL import Image, ImageDraw, ImageFont


def load_obj(path):
    vertices, faces = [], []
    for line in path.read_text().splitlines():
        if line.startswith("v "):
            vertices.append([float(x) for x in line.split()[1:]])
        elif line.startswith("f "):
            faces.append([int(x) - 1 for x in line.split()[1:]])
    return np.asarray(vertices)[np.asarray(faces)]


def clip_polygon(poly, axis, boundary, positive=True):
    result = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        da, db = a[axis] - boundary, b[axis] - boundary
        ina = da >= 0 if positive else da <= 0
        inb = db >= 0 if positive else db <= 0
        if ina:
            result.append(a)
        if ina != inb:
            result.append(a + (b - a) * (da / (da - db)))
    return result


def sections(triangles):
    result = []
    for triangle in triangles:
        points = []
        for i in range(3):
            a, b = triangle[i], triangle[(i + 1) % 3]
            if abs(a[2]) < 1e-9:
                points.append(a)
            if a[2] * b[2] < 0:
                points.append(a + (b - a) * (-a[2] / (b[2] - a[2])))
        unique = []
        for p in points:
            if not any(np.linalg.norm(p - q) < 1e-8 for q in unique):
                unique.append(p)
        if len(unique) == 2:
            result.append(np.asarray(unique))
    return result


def rasterize(triangles, size):
    """Orthographic triangle depth buffer; clipping only exposes the interior."""
    forward = np.array([-.30, -.50, .80])
    forward /= np.linalg.norm(forward)
    right = np.cross([0, 1, 0], forward)
    right /= np.linalg.norm(right)
    down = np.cross(right, forward)
    basis = np.asarray([right, down, forward])
    light = np.array([-.35, .85, -.4])
    light /= np.linalg.norm(light)
    projected, colors = [], []
    for triangle in triangles:
        poly = list(triangle)
        for axis, value, positive in [(2, 0, True), (2, 7, False), (0, -9, True), (0, 8, False)]:
            if poly:
                poly = clip_polygon(poly, axis, value, positive)
        if len(poly) < 3:
            continue
        normal = np.cross(triangle[1] - triangle[0], triangle[2] - triangle[0])
        normal /= max(np.linalg.norm(normal), 1e-20)
        brightness = .57 + .43 * max(0, np.dot(normal, light))
        color = np.array([107, 149, 174]) * brightness
        for i in range(1, len(poly) - 1):
            projected.append(np.asarray([poly[0], poly[i], poly[i + 1]]) @ basis.T)
            colors.append(color)
    projected = np.asarray(projected)
    minimum = projected[:, :, :2].min(axis=(0, 1))
    maximum = projected[:, :, :2].max(axis=(0, 1))
    extent = maximum - minimum
    scale = min((size[0] - 50) / extent[0], (size[1] - 50) / extent[1])
    offset = (np.asarray(size) - extent * scale) * .5
    projected[:, :, :2] = (projected[:, :, :2] - minimum) * scale + offset
    pixels = np.full((size[1], size[0], 3), [241, 245, 249], dtype=np.uint8)
    depth = np.full((size[1], size[0]), np.inf)
    for tri, color in zip(projected, colors):
        xmin = max(0, int(np.floor(tri[:, 0].min())))
        xmax = min(size[0] - 1, int(np.ceil(tri[:, 0].max())))
        ymin = max(0, int(np.floor(tri[:, 1].min())))
        ymax = min(size[1] - 1, int(np.ceil(tri[:, 1].max())))
        if xmin > xmax or ymin > ymax:
            continue
        a, b, c = tri
        denominator = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(denominator) < 1e-10:
            continue
        x, y = np.meshgrid(np.arange(xmin, xmax + 1) + .5, np.arange(ymin, ymax + 1) + .5)
        w0 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / denominator
        w1 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / denominator
        w2 = 1 - w0 - w1
        z = w0 * a[2] + w1 * b[2] + w2 * c[2]
        region = depth[ymin:ymax + 1, xmin:xmax + 1]
        visible = (w0 >= -1e-10) & (w1 >= -1e-10) & (w2 >= -1e-10) & (z < region)
        region[visible] = z[visible]
        pixels[ymin:ymax + 1, xmin:xmax + 1][visible] = color.astype(np.uint8)
    return Image.fromarray(pixels)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    folder = args.root / ".utmp/terrain-sculpting/p2"
    patch = load_obj(folder / "tunnel-patch.obj")
    baseline = load_obj(folder / "masked-baseline.obj")
    triangles = np.concatenate([patch, baseline])
    segments = sections(triangles)
    canvas = Image.new("RGB", (1800, 820), "#f8fafc")
    draw = ImageDraw.Draw(canvas)
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    def font(size):
        return ImageFont.truetype(str(font_path), size) if font_path.exists() else ImageFont.load_default(size=size)
    title, heading, body, small = font(38), font(27), font(23), font(21)
    draw.text((65, 38), "P2 terrain reconstruction · 0.3 m sampling", font=title, fill="#203649")
    draw.text((65, 125), "Centre section through the mesh", font=heading, fill="#203649")
    draw.text((920, 125), "Cutaway of actual extracted triangles", font=heading, fill="#203649")
    x0, y0, sx, sy = 400, 600, 37.5, 55
    def point(x, y):
        return x0 + x * sx, y0 - y * sy
    for y in [0, 2, 4]:
        draw.line([point(-9, y), point(8, y)], fill="#dbe3eb", width=1)
        draw.text((38, point(0, y)[1] - 14), str(y), font=small, fill="#64748b")
    draw.text((65, 203), "Height (m)", font=small, fill="#64748b")
    for x in [-8, -4, 0, 4, 8]:
        draw.text((point(x, 0)[0] - 12, 641), str(x), font=small, fill="#64748b")
    draw.text((225, 674), "Horizontal position (m)", font=small, fill="#64748b")
    for segment in segments:
        a, b = segment
        low, high = 0., 1.
        if abs(b[0] - a[0]) < 1e-12:
            if not -9 <= a[0] <= 8:
                continue
        else:
            t0, t1 = sorted(((-9 - a[0]) / (b[0] - a[0]), (8 - a[0]) / (b[0] - a[0])))
            low, high = max(low, t0), min(high, t1)
        if low <= high:
            draw.line([point(*(a + (b - a) * t)[:2]) for t in (low, high)], fill="#256187", width=3)
    def label(text, at, target):
        draw.text(at, text, font=body, fill="#253849")
        start = (at[0] + 20, at[1] + 30)
        end = point(*target)
        draw.line([start, end], fill="#64748b", width=2)
        draw.ellipse((end[0] - 3, end[1] - 3, end[0] + 3, end[1] + 3), fill="#64748b")
    label("Retained stone roof", (385, 239), (3.2, 3.5))
    label("Tunnel ceiling", (125, 370), (-1.5, 2.7))
    label("Walkable floor", (340, 605), (2.8, 0))
    canvas.paste(rasterize(triangles, (840, 470)), (905, 195))
    draw.text((65, 729), "Front half omitted in the cutaway to expose the interior. The fixture includes both side walls and the roof.", font=body, fill="#475569")
    draw.text((65, 772), f"Exported Unity geometry · replacement patch: {len(patch):,} triangles · production materials and lighting arrive later.", font=small, fill="#64748b")
    output = args.root / "Documentation/Images/TerrainSculptingP2.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output)
    print(output)


if __name__ == "__main__":
    main()
