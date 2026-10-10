"""Same deterministic, atlas-safe timber projection used by BuildTrimUV in Unity."""
def noise(seed,slot):
 x=(seed*0x45d9f3b+(slot+1)*0x9e3779b9)&0xffffffff
 x^=x>>16;x=(x*0x7feb352d)&0xffffffff;x^=x>>15;x=(x*0x846ca68b)&0xffffffff;x^=x>>16
 return (x&0xffffff)/16777216.0

def sample(p,normal,lo,hi,grain,seed):
 dominant=max(range(3),key=lambda a:abs(normal[a]));axes=[a for a in range(3) if a!=dominant]
 cap=dominant==grain
 if cap:
  if noise(seed,7)>.5:axes.reverse()
  a,b=axes;tile=0 if noise(seed,8)<.5 else 1
  u=.018+tile*.25+noise(seed,9)*.03+(p[a]-lo[a])*.5
  v=.272+noise(seed,10)*.03+(p[b]-lo[b])*.5
 else:
  a=grain;b=next(a for a in axes if a!=grain)
  span=min(.87,max(.06,(hi[a]-lo[a])*(.18+.04*noise(seed,0))))
  position=(p[a]-lo[a])/max(.000001,hi[a]-lo[a])
  if noise(seed,1)>.5:position=1-position
  u=.02+noise(seed,2)*(.96-span)+position*span
  across=(p[b]-lo[b])
  if noise(seed,3)>.5:across=(hi[b]-lo[b])-across
  v=.772+noise(seed,4)*.013+dominant*.002+across*.20
 return u,v
