"""Pack the two pilot meadow cutouts as basal blades (left) and seed stems (right)."""
from pathlib import Path

from PIL import Image


root = Path(__file__).resolve().parents[1]
source = root / "ArtSource" / "MeadowGrass"
destination = root / "Assets" / "Textures" / "Grass" / "T_PilotMeadowBlades.png"
tile = 512
gutter = 12
atlas = Image.new("RGBA", (tile * 2, tile))

for index, name in enumerate(("BasalBlades", "SeedStems")):
    with Image.open(source / f"{name}.png") as original:
        art = original.convert("RGBA")
    if index == 0:
        # End just above the photographed litter at the base. The card sinks this edge into the ground.
        art = art.crop((0, 0, art.width, round(art.height * .93)))
    matte = 122 if index == 0 else 48
    alpha = art.getchannel("A").point(lambda value: max(0, round((value - matte) * 255 / (255 - matte))))
    art.putalpha(alpha)
    art = art.crop(alpha.point(lambda value: 255 if value >= 64 else 0).getbbox())
    art = art.resize((tile - 2 * gutter, tile - 2 * gutter), Image.Resampling.LANCZOS)
    atlas.paste(art, (index * tile + gutter, gutter))
atlas.save(destination, optimize=True)
