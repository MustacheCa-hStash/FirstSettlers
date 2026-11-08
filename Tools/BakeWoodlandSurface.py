"""Model and bake woodland surface relief and a live-fern frond atlas.

Coordinates are metres in a top-down X/Y plane, with Z as height. The tile
rasterizer wraps triangle fragments, including branches that cross a seam.
"""
from pathlib import Path
import math
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/Textures/Ground/WoodlandPilot"
ART = ROOT / "ArtSource/WoodlandSurface"
METERS = 3.84
SIZE = 2048


class Mesh:
    def __init__(self):
        self.points = []
        self.colors = []
        self.faces = []

    def grid(self, rows, colors):
        offset = len(self.points)
        self.points.extend(np.reshape(rows, (-1, 3)))
        self.colors.extend(np.reshape(colors, (-1, 3)))
        stride = len(rows[0])
        for i in range(len(rows)-1):
            for j in range(stride-1):
                a = offset + i*stride + j
                self.faces.extend(((a,a+1,a+stride), (a+1,a+stride+1,a+stride)))

    def write_obj(self, path):
        with path.open("w", encoding="utf-8") as file:
            file.write("# High-poly bake specimens. Coordinates are X, height, Z.\n")
            for (x,y,z), color in zip(self.points, self.colors):
                file.write("v " + " ".join(f"{v:.5f}" for v in (x,z,y,*[c/255 for c in color])) + "\n")
            for a,b,c in self.faces:
                file.write(f"f {a+1} {b+1} {c+1}\n")


def leaf(mesh, root, tip, width, color, rng, sorrel=False, serration=.08):
    root, tip = np.asarray(root), np.asarray(tip)
    axis = tip - root
    along = axis[:2] / max(1e-6, np.linalg.norm(axis[:2]))
    right = np.array((-along[1], along[0], 0))
    rows, tones = [], []
    curl = rng.uniform(.11,.27) * width
    color = np.asarray(color)
    for i, t in enumerate(np.linspace(0, 1, 9)):
        profile = math.sin(math.pi*(t**(.72 if sorrel else .86)))**(.56 if sorrel else .76)
        profile = max(.026, profile) * (1 + serration * (-1 if i%2 else 1))
        row, shade = [], []
        for v in (-1,-.48,0,.48,1):
            point = root + axis*t + right*(width*profile*v)
            point[2] += curl*math.sin(math.pi*t)*(1-1.65*v*v)
            if sorrel and i >= 7:
                point -= axis*.15*(i-6)/2*(1-abs(v))
            rib = .14 if v == 0 else -.035*abs(v)
            shade.append(color*(.88+.16*t+rib+rng.uniform(-.045,.045)))
            row.append(point)
        rows.append(row)
        tones.append(shade)
    mesh.grid(rows, tones)


def tube(mesh, path, radii, color, rng, sides=8):
    path = np.asarray(path)
    rings, colors = [], []
    tint = np.asarray(color)
    bark = rng.uniform(.76,1.19,sides)
    for i, (center,radius) in enumerate(zip(path,radii)):
        dx, dy = (path[min(i+1,len(path)-1)] - path[max(i-1,0), :])[:2]
        length = max(np.hypot(dx,dy), 1e-6)
        side = np.array((-dy/length,dx/length,0))
        ring, tones = [], []
        for j in range(sides+1):
            a = j*math.tau/sides
            p = center + radius*side*math.cos(a)
            p[2] += radius*math.sin(a)*.77
            ring.append(p)
            tones.append(tint*(bark[j%sides]+.10*math.sin(a)+rng.uniform(-.08,.08)))
        rings.append(ring)
        colors.append(tones)
    mesh.grid(rings,colors)


def frond(mesh, base, vector, width, color, rng, pairs=14):
    base = np.asarray(base)
    axis = np.asarray(vector)
    planar = axis[:2]/np.linalg.norm(axis[:2])
    across = np.array((-planar[1],planar[0],0))
    def spine(t):
        return base + axis*t + across*(width*.14*math.sin(t*math.pi))
    tube(mesh, [spine(t) for t in np.linspace(0,1,10)],
         np.linspace(width*.030,width*.003,10), (132,150,68), rng, sides=5)
    for side in (-1,1):
        for j in range(pairs):
            t = .14+j*.775/pairs+(side+1)*.009+rng.uniform(-.005,.005)
            reach = width*(.20+.78*math.sin((t-.055)*math.pi)**.9)*(1-.44*t)
            start = spine(t)
            end = start + across*(side*reach) + axis*(.045+.06*(1-t))
            end[2] += reach*.10
            leaf(mesh,start,end,width*(.13-.070*t),np.asarray(color)*rng.uniform(.78,1.18),
                 rng,serration=.13)
    leaf(mesh,spine(.89),spine(1),width*.072,color,rng)


def woodland(seed, damp):
    layout = np.random.default_rng(seed)
    rng = np.random.default_rng(seed+7)
    mesh = Mesh()
    centers = layout.random((31,2))*METERS
    radii = layout.uniform(.16,.66,(len(centers),1))*layout.uniform(.52,1.35,(len(centers),2))
    angles = layout.uniform(0,math.tau,len(centers))
    population = layout.uniform(.38,1.15,len(centers))
    for _ in range(6350):
        x,y = layout.random(2)*METERS
        z = layout.uniform(.008,.026)
        theta = layout.uniform(0,math.tau)
        size = layout.uniform(.65,1.34)
        rank = layout.random()
        specimen_seed = layout.integers(2**31)
        delta = (np.array((x,y))-centers+METERS*.5)%METERS-METERS*.5
        rotated = np.stack((delta[:,0]*np.cos(angles)+delta[:,1]*np.sin(angles),
                            delta[:,1]*np.cos(angles)-delta[:,0]*np.sin(angles)),axis=1)
        distances = np.sum((rotated/radii)**2,axis=1)
        colony = np.max(np.maximum(0,1-distances)**.72*population)
        if rank > .019+min(.90,colony*(1.15 if damp else .82)):
            continue
        specimen = np.random.default_rng(specimen_seed)
        colors = np.array((specimen.uniform(63,100),specimen.uniform(98,139),specimen.uniform(38,64)))
        sorrel = specimen.random() < .69
        for j in range(3 if sorrel else 2):
            a = theta+j*math.tau/3
            direction = np.array((math.cos(a),math.sin(a),0))
            stem = np.array((x,y,z))
            root = stem+direction*(.003*size)
            tip = stem+direction*((.044 if sorrel else .069)*size)
            tip[2] += specimen.uniform(-.009,.009)
            leaf(mesh,root,tip,(.0185 if sorrel else .022)*size,colors*specimen.uniform(.83,1.16),specimen,sorrel)
    for i in range(53 if damp else 34):
        x,y = rng.random(2)*METERS
        a = rng.uniform(0,math.tau)
        length = rng.uniform(.09,.23)
        frond(mesh,(x,y,.021),(math.cos(a)*length,math.sin(a)*length,.004),
              rng.uniform(.026,.044),(83,120,54),rng,pairs=7)
    # Fragmented deadwood. Low curved branches are individually modelled,
    # including thinner offshoots, so they bake as readable grounded shapes.
    wood = Mesh()
    rng = np.random.default_rng(seed+37)
    for i in range(108 if damp else 147):
        x,y = rng.random(2)*METERS
        a = rng.uniform(0,math.tau)
        length = rng.uniform(.07,.32) if i>=18 else rng.uniform(.32,.76)
        radius = rng.uniform(.002,.011) if i>=18 else rng.uniform(.011,.029)
        if i < 3:
            length *= 1.12
            radius *= 1.38
        forward=np.array((math.cos(a),math.sin(a),0))
        side=np.array((-forward[1],forward[0],0))
        start=np.array((x,y,.009+radius*.17))
        bow,kink=rng.uniform(-.17,.17,2)*length
        path=[start+forward*length*t+side*(bow*math.sin(t*2.1)+kink*t*t)
              for t in np.linspace(0,1,8)]
        shade=rng.uniform(.78,1.23)
        tube(wood,path,np.linspace(radius,radius*rng.uniform(.18,.62),len(path)),
             np.array((126,99,76))*shade,rng)
        if i<38:
            fork=path[3]
            tip=fork+forward*length*.31+side*length*rng.uniform(.20,.37)
            tip[2]-=.004
            tube(wood,[fork,(fork+tip)*.50+side*.011,tip],[radius*.60,radius*.34,radius*.08],
                 np.array((132,106,81))*shade,rng)
    return mesh,wood


def needle_bed(seed, damp):
    rng=np.random.default_rng(seed)
    mesh=Mesh()
    for i in range(5200 if damp else 6100):
        x,y=rng.random(2)*METERS
        phase=np.array((x,y))*math.tau/METERS
        drift=.57+.21*math.sin(phase[0]*3+math.sin(phase[1]*2))+.19*math.cos(phase[1]*4-phase[0]*2)
        if rng.random()>drift:
            continue
        angle=rng.uniform(0,math.tau)
        direction=np.array((math.cos(angle),math.sin(angle),0))
        side=np.array((-direction[1],direction[0],0))
        start=np.array((x,y,rng.uniform(.004,.010)))
        length=rng.uniform(.032,.122)
        width=rng.uniform(.00055,.00165)
        color=np.array((151,116,72) if not damp else (127,98,65))*rng.uniform(.68,1.23)
        bend=rng.uniform(-.13,.13)*length
        rows, tones=[], []
        for t in (0,.43,.76,1):
            tip=start+direction*(length*t)+side*(bend*math.sin(t*2.3))
            tip[2]+=.0025*math.sin(math.pi*t)
            half=side*width*(.12+.88*math.sin(t*math.pi)**.55)
            rows.append((tip-half,tip+half))
            tones.append((color*(.88+.14*t),color*(1.04+.08*t)))
        mesh.grid(rows,tones)
    return mesh


def raster(mesh, shape, scale, height, pixels, wrap):
    points = np.asarray(mesh.points,dtype=np.float32)
    points[:,:2] *= scale
    colors = np.asarray(mesh.colors,dtype=np.float32)
    surface = np.zeros(shape,dtype=bool)
    for tri in mesh.faces:
        aa,bb,cc = points[list(tri)]
        u,v = bb-aa,cc-aa
        det = u[0]*v[1]-v[0]*u[1]
        if abs(det)<1e-6:
            continue
        lo=np.floor(np.minimum(np.minimum(aa[:2],bb[:2]),cc[:2])).astype(int)
        hi=np.ceil(np.maximum(np.maximum(aa[:2],bb[:2]),cc[:2])).astype(int)
        iy,ix=np.ogrid[lo[1]:hi[1],lo[0]:hi[0]]
        dx=ix+.5-aa[0]
        dy=iy+.5-aa[1]
        b=(dx*v[1]-dy*v[0])/det
        c=(dy*u[0]-dx*u[1])/det
        mask=(b>=0)&(c>=0)&(b+c<=1)
        yy,xx=np.where(mask)
        if not yy.size:
            continue
        z=aa[2]+u[2]*b[yy,xx]+v[2]*c[yy,xx]
        y,x=yy+lo[1],xx+lo[0]
        if wrap:
            y %= shape[0]
            x %= shape[1]
        else:
            assert y.min()>=0 and x.min()>=0 and y.max()<shape[0] and x.max()<shape[1]
        top=z>height[y,x]
        y,x,z=y[top],x[top],z[top]
        p,q=b[yy[top],xx[top],None],c[yy[top],xx[top],None]
        ca,cb,ccol=colors[list(tri)]
        pixels[y,x]=ca+(cb-ca)*p+(ccol-ca)*q
        height[y,x]=z
        surface[y,x]=True
    return surface


def save(pixels, name):
    Image.fromarray(np.uint8(np.clip(pixels,0,255)+.5)).save(OUT/f"{name}.png")


def tile_source(name, suffix, mode):
    source=Image.open(OUT/f"{name}_{suffix}.png").convert(mode)
    pixels=np.array(source.resize((768,768),Image.Resampling.LANCZOS))
    repeated=Image.fromarray(np.tile(pixels,(3,3,1) if mode=="RGB" else (3,3)))
    return np.array(repeated.resize((SIZE,SIZE),Image.Resampling.LANCZOS),dtype=np.float32)


def bake_ground(name, damp):
    source="DampDuff" if damp else "DryDuff"
    color=tile_source(source,"Albedo","RGB")
    base=tile_source(source,"Height","L")*.000027+.001
    h=base.copy()
    mesh,wood=woodland(25193,damp)
    scale=(SIZE/METERS,SIZE/METERS)
    needles=needle_bed(6337,damp)
    raster(needles,h.shape,scale,h,color,True)
    mask=raster(mesh,h.shape,scale,h,color,True)
    woody=raster(wood,h.shape,scale,h,color,True)
    mask |= woody
    bark=Image.open(ROOT/"Assets/Textures/Trees/Woodland/WhiteOak_Bark.png").convert("L")
    grain=np.tile(np.asarray(bark.resize((SIZE//4,SIZE//4)),dtype=np.float32),(4,4))
    grain=np.clip(1+(grain-np.mean(grain))*.010,.65,1.33)
    color[woody]*=grain[woody,None]
    relative=h-base
    contact=np.maximum(0,gaussian_filter(relative,4.0,mode="wrap")-relative*.58)
    contact=np.minimum(.26, contact*16)
    color*= (1-contact[...,None]*(.80 if damp else .47))
    # Keep a few coarse moss pillows between the leaves. Variation is periodic.
    yy,xx=np.ogrid[:SIZE,:SIZE]
    phase=math.tau/SIZE
    moss=(np.sin(xx*phase*2+np.cos(yy*phase*3))+
          .65*np.cos((xx+yy)*phase*5)+.55*np.sin((xx-yy)*phase*9))
    fill=np.clip((moss-(1.09 if damp else 1.43))*.40,0,.55)*(1-mask)
    grain=.70+base/.021
    color=color*(1-fill[...,None]) + np.array((71,87,42))*grain[...,None]*fill[...,None]
    h += fill*.005
    smooth=gaussian_filter(h,1.22,mode="wrap")
    grad=SIZE/METERS*.29
    dx=(np.roll(smooth,-1,1)-np.roll(smooth,1,1))*grad
    dy=(np.roll(smooth,-1,0)-np.roll(smooth,1,0))*grad
    normal=np.stack((-dx,dy,np.ones_like(dx)),axis=2)
    normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    save(color,name+"_Albedo")
    save((normal*.5+.5)*255,name+"_Normal")
    save(smooth/.057*255,name+"_Height")
    if not damp:
        original=tile_source(source,"AO","L")
        save(original*(1-contact),name+"_AO")
    greenery=((color[:,:,1]>color[:,:,0]*1.11)&(color[:,:,1]>color[:,:,2]*1.24)).mean()
    print(f"{name}: {len(mesh.faces)+len(wood.faces)+len(needles.faces):,} triangles baked, {greenery:.1%} green texels")


def main():
    ART.mkdir(parents=True,exist_ok=True)
    bake_ground("WoodlandDuffDry",False)
    bake_ground("WoodlandDuffDamp",True)
    if "--surface-only" in sys.argv:
        return
    fern=Mesh()
    rng=np.random.default_rng(24601)
    frond(fern,(.49,.965,.012),(.019,-.927,.009),.237,(95,134,57),rng,pairs=16)
    shape=(1024,512)
    h=np.full(shape,-1,dtype=np.float32)
    color=np.zeros((*shape,3),dtype=np.float32)
    mask=raster(fern,shape,(shape[1],shape[0]),h,color,False)
    save(np.dstack((color,mask*255)),"WoodlandFernFrond")
    specimens=Mesh()
    leaf(specimens,(0,0,.014),(.07,0,.012),.027,(81,127,49),rng,True)
    leaf(specimens,(0,.10,.010),(.088,.10,.012),.024,(94,130,51),rng)
    tube(specimens,[(0,.15,.012),(.10,.21,.012),(.24,.26,.012)],
         [.018,.013,.002],(146,111,76),rng)
    frond(specimens,(.30,.02,.012),(.04,.48,.009),.115,(95,134,57),rng)
    specimens.write_obj(ART/"BakeSpecimens.obj")


if __name__ == "__main__":
    main()
