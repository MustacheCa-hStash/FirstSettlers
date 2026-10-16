"""Deterministic periodic flame flipbook. Every temporal harmonic wraps exactly at 36 frames."""
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
out=ROOT/'Assets/Resources/Lighting';out.mkdir(parents=True,exist_ok=True)
n=120
x,y=np.meshgrid(np.linspace(-1,1,n),np.linspace(1,0,n))
atlas=Image.new('RGBA',(768,768))
frames=[]
for frame in range(36):
    t=frame/36*2*np.pi
    flow=x+.12*np.sin(y*11-t*2)*y+.055*np.sin(y*27-t*3)
    turbulence=(np.sin(flow*19+y*23-t*3)+.5*np.sin(flow*41-y*34+t*5)+.3*np.sin(flow*83+y*67-t*7))/1.8
    heat=np.zeros_like(x)
    for offset,tip,spread,phase in [(0,.9,.25,0),(-.22,.65,.105,1.7),(.22,.73,.13,3.1)]:
        tip=tip+.06*np.sin(t+phase)
        h=y/tip
        center=offset*(1-y)+.11*np.sin(y*12-t*2+phase)*y
        width=spread*np.maximum(1-h,.001)**.7
        tongue=np.exp(-((x-center)/width)**2)*np.clip((tip-y)*14,0,1)
        tongue*=np.clip((y-.015)*25,0,1)
        heat=np.maximum(heat,tongue)
    energy=np.clip(heat*(.9+.45*turbulence)-.07,0,1)
    alpha=np.clip(energy*4,0,1)*np.clip((1-y)*20,0,1)
    core=np.clip(energy*1.6-y*.7,0,1)
    r=np.ones_like(x)
    g=.16+.82*core
    b=.015+.32*core**3
    blue=np.exp(-((y-.055)/.035)**2)*np.exp(-(x/.18)**2)*.28
    r=r*(1-blue);g=g*(1-blue)+blue*.25;b=b+blue
    rgba=np.stack([r,g,b,alpha],axis=-1)
    rgba=np.uint8(np.clip(rgba,0,1)*255)
    image=Image.fromarray(rgba,'RGBA');frames.append(image)
    atlas.paste(image,((frame%6)*128+4,(frame//6)*128+4))
atlas.save(out/'TorchFlameAtlas.png')
preview=ROOT/'.utmp/local-flame';preview.mkdir(parents=True,exist_ok=True)
background=Image.new('RGBA',(256,256),(14,17,22,255))
gif=[]
for image in frames:
    sample=background.copy();sample.alpha_composite(image.resize((220,220)),(18,18));gif.append(sample.convert('RGB'))
gif[0].save(preview/'flame-loop.gif',save_all=True,append_images=gif[1:],duration=42,loop=0)
print(out/'TorchFlameAtlas.png')
