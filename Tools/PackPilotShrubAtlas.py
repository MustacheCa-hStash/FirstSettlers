"""Separate the three generated branch sprays, retaining alpha and mip gutters."""
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import binary_dilation, label


root = Path(__file__).resolve().parents[1]
source = np.array(Image.open(root / "ArtSource/PilotShrubs/BranchSprays.png").convert("RGBA"))
labels, _ = label(source[:, :, 3] > 65, structure=np.ones((3, 3)))
counts = np.bincount(labels.ravel())
specimens = sorted(np.argsort(counts[1:])[-3:] + 1,
                   key=lambda number: np.where(labels == number)[1].mean())
atlas = Image.new("RGBA", (1024, 1024))
for i, specimen in enumerate(specimens):
    mask = labels == specimen
    y, x = np.where(mask)
    alpha = np.where(binary_dilation(mask, iterations=2),
                     np.clip((source[:, :, 3].astype(float) - 55)*255/195, 0, 255), 0)
    isolated = source.copy()
    isolated[:, :, 3] = alpha.astype("uint8")
    card = Image.fromarray(isolated).crop((x.min(), y.min(), x.max()+1, y.max()+1))
    card = card.resize((488, 488), Image.Resampling.LANCZOS)
    atlas.paste(card, (i % 2 * 512 + 12, i // 2 * 512 + 12))

atlas.save(root / "Assets/Textures/Ground/WoodlandPilot/ShrubSprays.png")
