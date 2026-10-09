"""Reuse the existing reed artwork; pack two repeats for an eight-vertex roof."""
from pathlib import Path
from PIL import Image
import numpy as np
root=Path(__file__).resolve().parents[1]
folder=root/'Assets/Textures/Buildings/Wood'
atlas=Image.open(folder/'thatch-roof-atlas.png').convert('RGBA')
upper=np.asarray(atlas)[:1024].copy()
tile=Image.open(folder/'thatch-seamless-albedo.png').convert('RGBA').resize((1016,1008),Image.Resampling.LANCZOS)
data=np.asarray(tile);wide=np.concatenate((data,data),axis=1)
packed=Image.fromarray(np.pad(wide,((8,8),(8,8),(0,0)),mode='wrap'))
atlas.paste(packed,(0,1024));atlas.save(folder/'thatch-roof-atlas.png')
assert np.array_equal(np.asarray(atlas)[:1024],upper)
print('ATLAS PACK PASS: wood/wattle pixels unchanged; same reeds repeated twice across lower half.')
