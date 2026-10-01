"""Generate the simple, single-material bumblebee UV0 color atlas."""

from pathlib import Path
from PIL import Image


SIZE = 512
HALF = SIZE // 2
OUTPUT = Path(__file__).parents[1] / "Assets" / "Textures" / "Fauna" / "T_BumblebeeAtlas.png"
OUTPUT.parent.mkdir(parents=True, exist_ok=True)

image = Image.new("RGB", (SIZE, SIZE))
pixels = image.load()


def lerp(a, b, t):
    return round(a + (b - a) * t)


def mix(a, b, t):
    return tuple(lerp(a[i], b[i], t) for i in range(3))


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


head = (34, 32, 31)
yellow = (239, 188, 49)
black = (38, 34, 31)

for y in range(SIZE):
    for x in range(SIZE):
        if y < HALF and x < HALF:
            # Top left: head. The near-black has enough value to show soft lighting.
            color = head
        elif y < HALF:
            # Top right: warm yellow thorax, with a very mild color shift.
            t = (y / (HALF - 1)) * 0.16
            color = mix(yellow, (210, 144, 30), t)
        elif x < HALF:
            # Bottom left: two broad black bands over a yellow abdomen.
            v = (y - HALF) / (HALF - 1)
            band_a = smoothstep(0.24, 0.27, v) * (1.0 - smoothstep(0.40, 0.43, v))
            band_b = smoothstep(0.58, 0.61, v) * (1.0 - smoothstep(0.74, 0.77, v))
            tip = smoothstep(0.90, 0.94, v)
            color = mix(yellow, black, max(band_a, band_b, tip))
        else:
            # Bottom right: soft off-white wing. Opaque for the current renderer.
            u = (x - HALF) / (HALF - 1)
            v = (y - HALF) / (HALF - 1)
            edge = min(u, 1-u, v, 1-v)
            color = mix((225, 231, 232), (248, 249, 246), smoothstep(0.0, 0.40, edge))
        pixels[x, y] = color

image.save(OUTPUT)
print(OUTPUT)
