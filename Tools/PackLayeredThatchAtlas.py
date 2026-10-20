"""Pack the generated cutout fringe into the roof atlas's unused upper right.
Run PrepareThatchAtlas.py with Redesign/straw-generated-source.png first.
"""
from pathlib import Path
from PIL import Image
import numpy as np
root=Path(__file__).resolve().parents[1]
path=root/'Assets/Textures/Buildings/Wood/thatch-roof-atlas.png'
atlas=Image.open(path).convert('RGBA')
before=np.asarray(atlas).copy()
fringe=Image.open(root/'ArtReferences/ThatchRoof/Redesign/fringe-generated-source.png').convert('RGBA')
atlas.paste(fringe.resize((1024,1024),Image.Resampling.LANCZOS),(1024,0))
after=np.asarray(atlas)
assert np.array_equal(before[:1024,:1024],after[:1024,:1024])
assert np.array_equal(before[1024:],after[1024:])
assert after[:1024,1024:,3].min()==0 and after[:1024,1024:,3].max()==255
atlas.save(path)
print('LAYERED ATLAS PASS: cutout fringe packed; timber, weave and opaque straw retained.')
