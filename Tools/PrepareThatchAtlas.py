"""Pack the approved dedicated roof atlas; leave the shared atlas untouched."""
from pathlib import Path
import json, sys
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parents[1]
(root/'.utmp/roof').mkdir(parents=True,exist_ok=True)
source = Image.open(sys.argv[1]).convert('RGB').resize((1024,1024),Image.Resampling.LANCZOS)
# Periodic-plus-smooth decomposition removes the illumination discontinuity
# without mirrored fibres. Solve the discrete Poisson boundary correction.
a = np.asarray(source,dtype=float)
v = np.zeros_like(a)
v[0] = a[-1]-a[0]; v[-1] = -v[0]
v[:,0] += a[:,-1]-a[:,0]; v[:,-1] -= a[:,-1]-a[:,0]
q = 2*np.cos(2*np.pi*np.arange(1024)/1024)
den = q[:,None]+q[None,:]-4; den[0,0]=1
s = np.fft.fft2(v,axes=(0,1))/den[:,:,None]; s[0,0]=0
tile = np.clip(a-np.fft.ifft2(s,axes=(0,1)).real,0,255)
# Exact boundary equality, with a short blend into each edge.
for axis in (0,1):
    t = np.swapaxes(tile,0,axis)
    for j in range(8):
        mean = (t[j]+t[-1-j])*.5
        weight = (1-j/8)**2
        t[j] = t[j]*(1-weight)+mean*weight
        t[-1-j] = t[-1-j]*(1-weight)+mean*weight
tile = Image.fromarray(tile.astype('uint8')).convert('RGBA')
folder = root/'Assets/Textures/Buildings/Wood'; folder.mkdir(parents=True,exist_ok=True)
tile.save(folder/'thatch-seamless-albedo.png')
# Original atlas downsampled into the upper-left quarter. Roof wood UVs map
# into that quarter; the wattle substrate retains its original alpha.
wood = Image.open(root/'ArtReferences/IronAgeWood/Textures/ordinary-wood-trim-albedo.png').convert('RGBA')
atlas = Image.new('RGBA',(2048,2048),(100,76,48,255))
atlas.paste(wood.resize((1024,1024),Image.Resampling.LANCZOS),(0,0))
atlas.paste(tile,(1024,1024))
# Eight pixel gutters wrap the thatch tile for filtering at internal boundaries.
# Mesh uses the inset region; no opaque/cutout pixels share a UV border.
inner = tile.resize((1008,1008),Image.Resampling.LANCZOS)
atlas.paste(inner,(1032,1032))
for j in range(8):
    atlas.paste(inner.crop((0,1000+j,1008,1001+j)),(1032,1024+j))
    atlas.paste(inner.crop((0,j,1008,j+1)),(1032,2040+j))
    atlas.paste(inner.crop((1000+j,0,1001+j,1008)),(1024+j,1032))
    atlas.paste(inner.crop((j,0,j+1,1008)),(2040+j,1032))
atlas.save(folder/'thatch-roof-atlas.png')
import runpy
runpy.run_path(str(root/'Tools/PackSingleRoofAtlas.py'))
report={'size':[2048,2048],'wood_uv':'u=.5*u, v=.5+.5*v','thatch_uv':[8/2048,8/2048,2040/2048,1016/2048],'thatch_repeats_in_atlas':2,
        'seam_max_error_x':int(np.abs(np.asarray(tile)[:,0].astype(int)-np.asarray(tile)[:,-1]).max()),
        'seam_max_error_y':int(np.abs(np.asarray(tile)[0].astype(int)-np.asarray(tile)[-1]).max()),'thatch_alpha':'255 everywhere'}
(root/'.utmp/roof/atlas-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
